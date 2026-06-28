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

        var customer = await customerRepository.GetByIdAsync(customerId);
        if (customer is null || customer.TenantId != tenantId)
            return ResponseData<WishlistResponse>.Failure("Customer not found.", 404);

        var wishlist = await wishlistRepository.GetByCustomerAsync(customerId, ct);
        if (wishlist is null || wishlist.TenantId != tenantId)
            return ResponseData<WishlistResponse>.Failure("Wishlist not found.", 404);

        // Resolve product summaries once per distinct product to avoid N+1.
        var productIds = wishlist.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = new Dictionary<Guid, Product>();
        foreach (var pid in productIds)
        {
            var product = await productRepository.GetByIdWithDetailsAsync(pid, ct);
            if (product is not null && product.TenantId == tenantId)
                products[pid] = product;
        }

        var response = new WishlistResponse
        {
            Id = wishlist.Id,
            CustomerId = wishlist.CustomerId,
            Items = wishlist.Items.Select(i => MapItem(i, products.GetValueOrDefault(i.ProductId))).ToList()
        };

        return ResponseData<WishlistResponse>.Success(response);
    }

    public async Task<ResponseData<bool>> RemoveItemAsync(Guid itemId,
        Guid removedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var item = await wishlistRepository.GetItemAsync(itemId, ct);
        if (item is null || item.TenantId != tenantId)
            return ResponseData<bool>.Failure("Wishlist item not found.", 404);

        await wishlistRepository.RemoveItemAsync(item);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(removedByUserId, tenantId, "WishlistItemRemoved", "WishlistItem", item.Id,
            new { item.WishlistId, item.ProductId, item.ProductVariantId }, null, ipAddress, userAgent);

        logger.LogInformation("Wishlist item {ItemId} removed for tenant {TenantId}", item.Id, tenantId);
        return ResponseData<bool>.Success(true, "Wishlist item removed.");
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
