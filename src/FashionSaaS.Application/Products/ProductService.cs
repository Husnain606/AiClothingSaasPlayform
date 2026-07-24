using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Products;

/// <summary>
/// Product catalog management. Input-shape validation is handled by FluentValidation
/// at the API boundary (CONVENTIONS §8); this service only enforces business rules:
/// slug uniqueness, category existence/tenant scoping, publish gating (name + category
/// + at least one active variant + at least one image), status transitions with domain
/// events, and delete rules (Draft only — Active/Archived products are not hard-deleted).
/// </summary>
public class ProductService(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository,
    IProductVariantRepository variantRepository,
    IProductImageRepository imageRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<ProductService> logger)
{
    public async Task<ResponseData<ProductResponse>> CreateAsync(CreateProductRequest request,
        Guid createdByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        if (await productRepository.SlugExistsAsync(tenantId, request.Slug, null, ct))
            return ResponseData<ProductResponse>.Failure($"Slug '{request.Slug}' is already in use.", 409);

        Category? category = await categoryRepository.GetByIdAsync(request.CategoryId);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Category not found.", 404);

        var product = new Product
        {
            TenantId = tenantId,
            CategoryId = request.CategoryId,
            Name = request.Name,
            Slug = request.Slug,
            Description = request.Description,
            BasePrice = request.BasePrice,
            Tags = request.Tags,
            Status = ProductStatus.Draft
        };

        await productRepository.AddAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(createdByUserId, tenantId, "ProductCreated", "Product", product.Id,
            null, new { product.Name, product.Slug, product.CategoryId, product.BasePrice }, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} created for tenant {TenantId}", product.Id, tenantId);

        // Re-fetch with full nav graph so the response object is consistent with GetById
        // (counts are genuinely 0 for a brand-new product, but this avoids relying on
        //  unloaded collections and keeps Create/Update symmetric).
        Product created = await productRepository.GetByIdWithDetailsAsync(product.Id, ct)
                      ?? product; // fallback — should never be null immediately after insert
        return ResponseData<ProductResponse>.Success(
            MapDetailedResponse(created), "Product created.", 201);
    }

    public async Task<ResponseData<ProductResponse>> UpdateAsync(Guid id, UpdateProductRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        if (await productRepository.SlugExistsAsync(tenantId, request.Slug, id, ct))
            return ResponseData<ProductResponse>.Failure($"Slug '{request.Slug}' is already in use.", 409);

        // Capture category name before save — re-fetch only if CategoryId actually changed.
        if (request.CategoryId != product.CategoryId)
        {
            Category? newCategory = await categoryRepository.GetByIdAsync(request.CategoryId);
            if (newCategory is null || newCategory.TenantId != tenantId)
                return ResponseData<ProductResponse>.Failure("Category not found.", 404);
            _ = newCategory.Name;
        }
        else
        {
            // CategoryId unchanged — resolve name without an extra round-trip.
            _ = (await categoryRepository.GetByIdAsync(product.CategoryId))?.Name;
        }

        var old = new { product.Name, product.Slug, product.Description, product.CategoryId, product.BasePrice, product.Tags };
        product.Name = request.Name;
        product.Slug = request.Slug;
        product.Description = request.Description;
        product.CategoryId = request.CategoryId;
        product.BasePrice = request.BasePrice;
        product.Tags = request.Tags;

        await productRepository.UpdateAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "ProductUpdated", "Product", product.Id,
            old, new { product.Name, product.Slug, product.Description, product.CategoryId, product.BasePrice, product.Tags },
            ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} updated for tenant {TenantId}", product.Id, tenantId);

        // Re-fetch with nav graph so VariantCount/PrimaryImageUrl/review stats reflect reality.
        Product? updated = await productRepository.GetByIdWithDetailsAsync(product.Id, ct);
        return ResponseData<ProductResponse>.Success(MapDetailedResponse(updated!), "Product updated.");
    }

    public async Task<ResponseData<ProductResponse>> PublishAsync(Guid id,
        Guid publishedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        if (string.IsNullOrWhiteSpace(product.Name))
            return ResponseData<ProductResponse>.Failure("Cannot publish: product has no name.", 400);

        Category? category = await categoryRepository.GetByIdAsync(product.CategoryId);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Cannot publish: product has no valid category.", 400);

        IReadOnlyList<ProductVariant> variants = await variantRepository.GetByProductAsync(product.Id, ct);
        if (!variants.Any(v => v.IsActive))
        {
            return ResponseData<ProductResponse>.Failure(
                "Cannot publish: product needs at least one active variant.", 400);
        }

        IReadOnlyList<ProductImage> images = await imageRepository.GetByProductAsync(product.Id, ct);
        if (images.Count == 0)
        {
            return ResponseData<ProductResponse>.Failure(
                "Cannot publish: product needs at least one image.", 400);
        }

        product.Status = ProductStatus.Active;
        product.AddDomainEvent(new ProductPublishedEvent(product.Id, tenantId));

        await productRepository.UpdateAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(publishedByUserId, tenantId, "ProductPublished", "Product", product.Id,
            new { Status = ProductStatus.Draft }, new { Status = ProductStatus.Active }, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} published for tenant {TenantId}", product.Id, tenantId);

        // Re-fetch with nav graph so VariantCount/PrimaryImageUrl/review stats reflect reality.
        Product? published = await productRepository.GetByIdWithDetailsAsync(product.Id, ct);
        return ResponseData<ProductResponse>.Success(MapDetailedResponse(published!), "Product published.");
    }

    public async Task<ResponseData<ProductResponse>> ArchiveAsync(Guid id,
        Guid archivedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        ProductStatus previousStatus = product.Status;
        product.Status = ProductStatus.Archived;
        product.AddDomainEvent(new ProductArchivedEvent(product.Id, tenantId));

        await productRepository.UpdateAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(archivedByUserId, tenantId, "ProductArchived", "Product", product.Id,
            new { Status = previousStatus }, new { Status = ProductStatus.Archived }, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} archived for tenant {TenantId}", product.Id, tenantId);

        // Re-fetch with nav graph so VariantCount/PrimaryImageUrl/review stats reflect reality.
        Product? archived = await productRepository.GetByIdWithDetailsAsync(product.Id, ct);
        return ResponseData<ProductResponse>.Success(MapDetailedResponse(archived!), "Product archived.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<bool>.Failure("Product not found.", 404);

        // Only Draft products may be hard-deleted; Active products should be archived first,
        // and Archived products are kept for records (spec §8).
        if (product.Status != ProductStatus.Draft)
        {
            return ResponseData<bool>.Failure(
                "Only draft products can be deleted; archive active products and keep archived ones for records.", 409);
        }

        await productRepository.DeleteAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deletedByUserId, tenantId, "ProductDeleted", "Product", product.Id,
            new { product.Name, product.Slug }, null, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} deleted for tenant {TenantId}", product.Id, tenantId);
        return ResponseData<bool>.Success(true, "Product deleted.");
    }

    public async Task<ResponseData<PagedResult<ProductResponse>>> GetAllAsync(ProductFilter filter,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<ProductResponse>>.Failure("Tenant could not be resolved.", 400);

        // Enforce tenant scope regardless of the inbound filter value.
        filter.TenantId = tenantId;

        (IReadOnlyList<Product>? items, var total) = await productRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<ProductResponse>
        {
            Items = items.Select(p => MapToResponse(p, p.Category?.Name)).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<ProductResponse>>.Success(page);
    }

    public async Task<ResponseData<ProductResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdWithDetailsAsync(id, ct);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        return ResponseData<ProductResponse>.Success(MapDetailedResponse(product));
    }

    public async Task<ResponseData<ProductResponse>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetBySlugWithDetailsAsync(tenantId, slug, ct);
        if (product is null)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        return ResponseData<ProductResponse>.Success(MapDetailedResponse(product));
    }

    private static ProductResponse MapToResponse(Product p, string? categoryName) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        Description = p.Description,
        CategoryId = p.CategoryId,
        CategoryName = categoryName,
        BasePrice = p.BasePrice,
        Status = p.Status,
        Tags = p.Tags,
        VariantCount = p.Variants.Count,
        PrimaryImageUrl = p.Images.FirstOrDefault(i => i.IsPrimary)?.Url ?? p.Images.FirstOrDefault()?.Url,
        ApprovedReviewCount = p.Reviews.Count(r => r.Status == ReviewStatus.Approved),
        AverageRating = p.Reviews.Where(r => r.Status == ReviewStatus.Approved)
            .Select(r => (double?)r.Rating)
            .DefaultIfEmpty(null)
            .Average(),
        CreatedAt = p.CreatedAt
    };

    private static ProductResponse MapDetailedResponse(Product p) => MapToResponse(p, p.Category?.Name);
}
