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

        var category = await categoryRepository.GetByIdAsync(request.CategoryId);
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
        return ResponseData<ProductResponse>.Success(
            MapToResponse(product, category.Name), "Product created.", 201);
    }

    public async Task<ResponseData<ProductResponse>> UpdateAsync(Guid id, UpdateProductRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        if (await productRepository.SlugExistsAsync(tenantId, request.Slug, id, ct))
            return ResponseData<ProductResponse>.Failure($"Slug '{request.Slug}' is already in use.", 409);

        Category? category = null;
        if (request.CategoryId != product.CategoryId)
        {
            category = await categoryRepository.GetByIdAsync(request.CategoryId);
            if (category is null || category.TenantId != tenantId)
                return ResponseData<ProductResponse>.Failure("Category not found.", 404);
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
        var categoryName = category?.Name ?? (await categoryRepository.GetByIdAsync(product.CategoryId))?.Name;
        return ResponseData<ProductResponse>.Success(MapToResponse(product, categoryName), "Product updated.");
    }

    public async Task<ResponseData<ProductResponse>> PublishAsync(Guid id,
        Guid publishedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        if (string.IsNullOrWhiteSpace(product.Name))
            return ResponseData<ProductResponse>.Failure("Cannot publish: product has no name.", 400);

        var category = await categoryRepository.GetByIdAsync(product.CategoryId);
        if (category is null || category.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Cannot publish: product has no valid category.", 400);

        var variants = await variantRepository.GetByProductAsync(product.Id, ct);
        if (!variants.Any(v => v.IsActive))
            return ResponseData<ProductResponse>.Failure(
                "Cannot publish: product needs at least one active variant.", 400);

        var images = await imageRepository.GetByProductAsync(product.Id, ct);
        if (images.Count == 0)
            return ResponseData<ProductResponse>.Failure(
                "Cannot publish: product needs at least one image.", 400);

        product.Status = ProductStatus.Active;
        product.AddDomainEvent(new ProductPublishedEvent(product.Id, tenantId));

        await productRepository.UpdateAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(publishedByUserId, tenantId, "ProductPublished", "Product", product.Id,
            new { Status = ProductStatus.Draft }, new { Status = ProductStatus.Active }, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} published for tenant {TenantId}", product.Id, tenantId);
        return ResponseData<ProductResponse>.Success(MapToResponse(product, category.Name), "Product published.");
    }

    public async Task<ResponseData<ProductResponse>> ArchiveAsync(Guid id,
        Guid archivedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        var previousStatus = product.Status;
        product.Status = ProductStatus.Archived;
        product.AddDomainEvent(new ProductArchivedEvent(product.Id, tenantId));

        await productRepository.UpdateAsync(product);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(archivedByUserId, tenantId, "ProductArchived", "Product", product.Id,
            new { Status = previousStatus }, new { Status = ProductStatus.Archived }, ipAddress, userAgent);

        logger.LogInformation("Product {ProductId} archived for tenant {TenantId}", product.Id, tenantId);
        var category = await categoryRepository.GetByIdAsync(product.CategoryId);
        return ResponseData<ProductResponse>.Success(MapToResponse(product, category?.Name), "Product archived.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(id);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<bool>.Failure("Product not found.", 404);

        // Only Draft products may be hard-deleted; Active/Archived must be archived, not removed (spec §8).
        if (product.Status != ProductStatus.Draft)
            return ResponseData<bool>.Failure(
                "Only draft products can be deleted. Archive published or archived products instead.", 409);

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

        var (items, total) = await productRepository.GetPagedAsync(filter, ct);

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

        var product = await productRepository.GetByIdWithDetailsAsync(id, ct);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        return ResponseData<ProductResponse>.Success(MapDetailedResponse(product));
    }

    public async Task<ResponseData<ProductResponse>> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductResponse>.Failure("Tenant could not be resolved.", 400);

        // The repository exposes no slug-based detail lookup, so resolve the id from the
        // tenant-scoped paged set, then load the full details graph for the match.
        var (items, _) = await productRepository.GetPagedAsync(
            new ProductFilter { TenantId = tenantId, Page = 1, PageSize = int.MaxValue }, ct);
        var match = items.FirstOrDefault(p => p.Slug == slug);
        if (match is null)
            return ResponseData<ProductResponse>.Failure("Product not found.", 404);

        var product = await productRepository.GetByIdWithDetailsAsync(match.Id, ct);
        if (product is null || product.TenantId != tenantId)
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
