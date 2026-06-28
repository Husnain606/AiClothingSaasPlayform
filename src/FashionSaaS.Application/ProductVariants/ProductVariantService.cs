using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.ProductVariants.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.ProductVariants;

/// <summary>
/// Product variant management. Input-shape validation (required fields, lengths,
/// non-negative quantities/prices) is handled by FluentValidation at the API boundary
/// (CONVENTIONS §8); this service only enforces business rules: owning product exists
/// and is in the same tenant, SKU uniqueness per tenant, (Product, Size, Color)
/// uniqueness, and soft deactivation vs hard delete. Effective price is derived from
/// the variant override falling back to the owning product's base price.
/// </summary>
public class ProductVariantService(
    IProductVariantRepository variantRepository,
    IProductRepository productRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<ProductVariantService> logger)
{
    public async Task<ResponseData<VariantResponse>> AddAsync(AddVariantRequest request,
        Guid createdByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<VariantResponse>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(request.ProductId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<VariantResponse>.Failure("Product not found.", 404);

        if (await variantRepository.SkuExistsAsync(tenantId, request.Sku, null, ct))
            return ResponseData<VariantResponse>.Failure($"SKU '{request.Sku}' is already in use.", 409);

        // (Product, Size, Color) must be unique within the product — DB-side check (§6).
        if (await variantRepository.SizeColorExistsAsync(request.ProductId, request.Size, request.Color, null, ct))
            return ResponseData<VariantResponse>.Failure(
                $"A variant with size '{request.Size}' and color '{request.Color}' already exists for this product.", 409);

        var variant = new ProductVariant
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            Size = request.Size,
            Color = request.Color,
            Sku = request.Sku,
            StockQuantity = request.StockQuantity,
            PriceOverride = request.PriceOverride,
            IsActive = true
        };

        await variantRepository.AddAsync(variant);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(createdByUserId, tenantId, "VariantAdded", "ProductVariant", variant.Id,
            null, new { variant.ProductId, variant.Size, variant.Color, variant.Sku, variant.StockQuantity, variant.PriceOverride },
            ipAddress, userAgent);

        logger.LogInformation("Variant {VariantId} added to product {ProductId} for tenant {TenantId}",
            variant.Id, variant.ProductId, tenantId);

        return ResponseData<VariantResponse>.Success(MapToResponse(variant, product.BasePrice), "Variant added.", 201);
    }

    public async Task<ResponseData<VariantResponse>> UpdateAsync(Guid id, UpdateVariantRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<VariantResponse>.Failure("Tenant could not be resolved.", 400);

        var variant = await variantRepository.GetByIdAsync(id);
        if (variant is null || variant.TenantId != tenantId)
            return ResponseData<VariantResponse>.Failure("Variant not found.", 404);

        var product = await productRepository.GetByIdAsync(variant.ProductId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<VariantResponse>.Failure("Product not found.", 404);

        if (await variantRepository.SkuExistsAsync(tenantId, request.Sku, id, ct))
            return ResponseData<VariantResponse>.Failure($"SKU '{request.Sku}' is already in use.", 409);

        // (Product, Size, Color) uniqueness, excluding the variant being updated — DB-side check (§6).
        if (await variantRepository.SizeColorExistsAsync(variant.ProductId, request.Size, request.Color, id, ct))
            return ResponseData<VariantResponse>.Failure(
                $"A variant with size '{request.Size}' and color '{request.Color}' already exists for this product.", 409);

        var old = new { variant.Size, variant.Color, variant.Sku, variant.IsActive, variant.PriceOverride };
        variant.Size = request.Size;
        variant.Color = request.Color;
        variant.Sku = request.Sku;
        variant.IsActive = request.IsActive;
        variant.PriceOverride = request.PriceOverride;

        await variantRepository.UpdateAsync(variant);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "VariantUpdated", "ProductVariant", variant.Id,
            old, new { variant.Size, variant.Color, variant.Sku, variant.IsActive, variant.PriceOverride },
            ipAddress, userAgent);

        logger.LogInformation("Variant {VariantId} updated for tenant {TenantId}", variant.Id, tenantId);

        return ResponseData<VariantResponse>.Success(MapToResponse(variant, product.BasePrice), "Variant updated.");
    }

    public async Task<ResponseData<bool>> DeactivateAsync(Guid id,
        Guid deactivatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var variant = await variantRepository.GetByIdAsync(id);
        if (variant is null || variant.TenantId != tenantId)
            return ResponseData<bool>.Failure("Variant not found.", 404);

        var previous = variant.IsActive;
        variant.IsActive = false;

        await variantRepository.UpdateAsync(variant);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deactivatedByUserId, tenantId, "VariantDeactivated", "ProductVariant", variant.Id,
            new { IsActive = previous }, new { IsActive = false }, ipAddress, userAgent);

        logger.LogInformation("Variant {VariantId} deactivated for tenant {TenantId}", variant.Id, tenantId);
        return ResponseData<bool>.Success(true, "Variant deactivated.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var variant = await variantRepository.GetByIdAsync(id);
        if (variant is null || variant.TenantId != tenantId)
            return ResponseData<bool>.Failure("Variant not found.", 404);

        await variantRepository.DeleteAsync(variant);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deletedByUserId, tenantId, "VariantDeleted", "ProductVariant", variant.Id,
            new { variant.Sku, variant.Size, variant.Color }, null, ipAddress, userAgent);

        logger.LogInformation("Variant {VariantId} deleted for tenant {TenantId}", variant.Id, tenantId);
        return ResponseData<bool>.Success(true, "Variant deleted.");
    }

    public async Task<ResponseData<IReadOnlyList<VariantResponse>>> GetByProductAsync(Guid productId,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<VariantResponse>>.Failure("Tenant could not be resolved.", 400);

        var product = await productRepository.GetByIdAsync(productId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<IReadOnlyList<VariantResponse>>.Failure("Product not found.", 404);

        var variants = await variantRepository.GetByProductAsync(productId, ct);
        // All variants belong to the same product, so resolve effective price against one base price.
        var responses = variants.Select(v => MapToResponse(v, product.BasePrice)).ToList();
        return ResponseData<IReadOnlyList<VariantResponse>>.Success(responses);
    }

    private static VariantResponse MapToResponse(ProductVariant v, decimal productBasePrice) => new()
    {
        Id = v.Id,
        ProductId = v.ProductId,
        Size = v.Size,
        Color = v.Color,
        Sku = v.Sku,
        StockQuantity = v.StockQuantity,
        PriceOverride = v.PriceOverride,
        EffectivePrice = v.PriceOverride ?? productBasePrice,
        IsActive = v.IsActive,
        CreatedAt = v.CreatedAt
    };
}
