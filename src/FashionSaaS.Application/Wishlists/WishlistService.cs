using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Wishlists.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Wishlists;

/// <summary>
/// Wishlist management (admin/back-office surface for Phase 2). This service enforces
/// business rules: tenant scoping and admin removal of items. GetByCustomer returns the
/// customer's wishlist with each item enriched by a product summary (name, slug, base
/// price, primary image) resolved from the catalog. Customer-driven add arrives in
/// Phase 3. There is no inbound request DTO for removal (the item id is a route value),
/// so no validator is required (CONVENTIONS §8).
/// </summary>
public class WishlistService(
    IWishlistRepository wishlistRepository,
    ICustomerRepository customerRepository,
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<WishlistService> logger)
{
    public async Task<ResponseData<WishlistResponse>> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<WishlistResponse>.Failure("Tenant could not be resolved.", 400);

        Customer? customer = await customerRepository.GetByIdAsync(customerId);
        if (customer is null || customer.TenantId != tenantId)
            return ResponseData<WishlistResponse>.Failure("Customer not found.", 404);

        Wishlist? wishlist = await wishlistRepository.GetByCustomerAsync(customerId, ct);
        if (wishlist is null || wishlist.TenantId != tenantId)
            return ResponseData<WishlistResponse>.Failure("Wishlist not found.", 404);

        return ResponseData<WishlistResponse>.Success(await BuildResponseAsync(wishlist, tenantId, ct));
    }

    /// <summary>Customer-facing: resolves the caller's own Customer row by email (creating
    /// it on first contact, matching the OrderService/ReviewService pattern) and returns
    /// their wishlist - an empty one if they've never added anything.</summary>
    public async Task<ResponseData<WishlistResponse>> GetMineAsync(string customerEmail, string firstName,
        string lastName, string? phone, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<WishlistResponse>.Failure("Tenant could not be resolved.", 400);

        Customer customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, firstName, lastName, phone, ct);

        Wishlist? wishlist = await wishlistRepository.GetByCustomerAsync(customer.Id, ct);
        if (wishlist is null)
        {
            return ResponseData<WishlistResponse>.Success(
                new WishlistResponse { Id = Guid.Empty, CustomerId = customer.Id, Items = [] });
        }

        return ResponseData<WishlistResponse>.Success(await BuildResponseAsync(wishlist, tenantId, ct));
    }

    /// <summary>Customer-facing: adds a product (optionally a specific variant) to the
    /// caller's own wishlist, creating the wishlist itself on first use.</summary>
    public async Task<ResponseData<WishlistItemResponse>> AddItemAsync(string customerEmail, string firstName,
        string lastName, string? phone, AddWishlistItemRequest request,
        Guid addedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<WishlistItemResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdWithDetailsAsync(request.ProductId, ct);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<WishlistItemResponse>.Failure("Product not found.", 404);

        Customer customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, firstName, lastName, phone, ct);

        Wishlist? wishlist = await wishlistRepository.GetByCustomerAsync(customer.Id, ct);
        if (wishlist is null)
        {
            wishlist = new Wishlist { TenantId = tenantId, CustomerId = customer.Id };
            // GenericRepository.AddAsync cascades Added correctly for a brand-new root.
            await wishlistRepository.AddAsync(wishlist);
        }
        else if (wishlist.Items.Any(i => i.ProductId == request.ProductId && i.ProductVariantId == request.ProductVariantId))
        {
            return ResponseData<WishlistItemResponse>.Failure("Product is already in your wishlist.", 409);
        }

        var item = new WishlistItem
        {
            TenantId = tenantId,
            WishlistId = wishlist.Id,
            ProductId = request.ProductId,
            ProductVariantId = request.ProductVariantId
        };
        await wishlistRepository.AddItemAsync(item);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(addedByUserId, tenantId, "WishlistItemAdded", "WishlistItem", item.Id,
            null, new { item.WishlistId, item.ProductId, item.ProductVariantId }, ipAddress, userAgent);

        logger.LogInformation("Wishlist item {ItemId} added for tenant {TenantId}", item.Id, tenantId);
        return ResponseData<WishlistItemResponse>.Success(MapItem(item, product), "Added to wishlist.", 201);
    }

    /// <summary>Customer-facing removal: unlike the admin RemoveItemAsync, this verifies
    /// the item actually belongs to the calling customer's own wishlist before deleting.</summary>
    public async Task<ResponseData<bool>> RemoveMyItemAsync(string customerEmail, Guid itemId,
        Guid removedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        // Same identity resolution as the sibling methods above; if the customer genuinely
        // has no wishlist yet, the lookup below still returns 404.
        Customer customer = await customerRepository.GetOrCreateByEmailAsync(
            tenantId, customerEmail, string.Empty, string.Empty, null, ct);

        WishlistItem? item = await wishlistRepository.GetItemAsync(itemId, ct);
        if (item is null || item.TenantId != tenantId)
            return ResponseData<bool>.Failure("Wishlist item not found.", 404);

        Wishlist? wishlist = await wishlistRepository.GetByIdAsync(item.WishlistId);
        if (wishlist is null || wishlist.CustomerId != customer.Id)
            return ResponseData<bool>.Failure("Wishlist item not found.", 404);

        await wishlistRepository.RemoveItemAsync(item);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(removedByUserId, tenantId, "WishlistItemRemoved", "WishlistItem", item.Id,
            new { item.WishlistId, item.ProductId, item.ProductVariantId }, null, ipAddress, userAgent);

        logger.LogInformation("Wishlist item {ItemId} removed for tenant {TenantId}", item.Id, tenantId);
        return ResponseData<bool>.Success(true, "Wishlist item removed.");
    }

    public async Task<ResponseData<bool>> RemoveItemAsync(Guid itemId,
        Guid removedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        WishlistItem? item = await wishlistRepository.GetItemAsync(itemId, ct);
        if (item is null || item.TenantId != tenantId)
            return ResponseData<bool>.Failure("Wishlist item not found.", 404);

        await wishlistRepository.RemoveItemAsync(item);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(removedByUserId, tenantId, "WishlistItemRemoved", "WishlistItem", item.Id,
            new { item.WishlistId, item.ProductId, item.ProductVariantId }, null, ipAddress, userAgent);

        logger.LogInformation("Wishlist item {ItemId} removed for tenant {TenantId}", item.Id, tenantId);
        return ResponseData<bool>.Success(true, "Wishlist item removed.");
    }

    private async Task<WishlistResponse> BuildResponseAsync(Wishlist wishlist, Guid tenantId, CancellationToken ct)
    {
        // Resolve product summaries once per distinct product to avoid N+1.
        var productIds = wishlist.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = new Dictionary<Guid, Product>();
        foreach (Guid pid in productIds)
        {
            Product? product = await productRepository.GetByIdWithDetailsAsync(pid, ct);
            if (product is not null && product.TenantId == tenantId)
                products[pid] = product;
        }

        return new WishlistResponse
        {
            Id = wishlist.Id,
            CustomerId = wishlist.CustomerId,
            Items = wishlist.Items.Select(i => MapItem(i, products.GetValueOrDefault(i.ProductId))).ToList()
        };
    }

    private static WishlistItemResponse MapItem(WishlistItem item, Product? product) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductVariantId = item.ProductVariantId,
        ProductName = product?.Name,
        ProductSlug = product?.Slug,
        ProductBasePrice = product?.BasePrice,
        PrimaryImageUrl = product?.Images.FirstOrDefault(i => i.IsPrimary)?.Url
                          ?? product?.Images.FirstOrDefault()?.Url,
        CreatedAt = item.CreatedAt
    };
}
