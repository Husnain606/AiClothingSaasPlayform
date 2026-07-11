using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.ProductImages.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.ProductImages;

/// <summary>
/// Product image management backed by Cloudinary (behind <see cref="IImageStorageService"/>).
/// Input-shape validation (required ids, AltText length, non-empty reorder list) is handled
/// by FluentValidation at the API boundary (CONVENTIONS §8); content-type and size limits are
/// enforced at the controller (Task 18). This service owns the business rules: the owning
/// product must exist within the current tenant, uploads are namespaced under a per-tenant
/// Cloudinary folder, and the "exactly one primary image while images exist" invariant is
/// preserved across upload, delete and set-primary. Cloudinary deletes are best-effort and
/// must never block removal of the database row.
/// </summary>
public class ProductImageService(
    IProductImageRepository imageRepository,
    IProductRepository productRepository,
    IImageStorageService imageStorage,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<ProductImageService> logger)
{
    public async Task<ResponseData<ProductImageResponse>> UploadAsync(UploadImageRequest request,
        Stream content, string fileName,
        Guid uploadedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<ProductImageResponse>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(request.ProductId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<ProductImageResponse>.Failure("Product not found.", 404);

        // Cloudinary assets are namespaced per tenant/product so deletes and listings stay scoped.
        var folder = $"tenants/{tenantId}/products/{request.ProductId}";
        (var publicId, var url) = await imageStorage.UploadAsync(content, fileName, folder, ct);

        IReadOnlyList<ProductImage> existing = await imageRepository.GetByProductAsync(request.ProductId, ct);
        var isFirst = existing.Count == 0;

        var image = new ProductImage
        {
            TenantId = tenantId,
            ProductId = request.ProductId,
            VariantId = request.VariantId,
            CloudinaryPublicId = publicId,
            Url = url,
            AltText = request.AltText,
            SortOrder = existing.Count,
            IsPrimary = isFirst
        };

        await imageRepository.AddAsync(image);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(uploadedByUserId, tenantId, "ProductImageUploaded", "ProductImage", image.Id,
            null, new { image.ProductId, image.VariantId, image.IsPrimary, image.SortOrder },
            ipAddress, userAgent);

        logger.LogInformation(
            "Image {ImageId} (publicId {PublicId}) uploaded for product {ProductId} tenant {TenantId}; primary={IsPrimary}",
            image.Id, publicId, image.ProductId, tenantId, image.IsPrimary);

        return ResponseData<ProductImageResponse>.Success(MapToResponse(image), "Image uploaded.", 201);
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        ProductImage? image = await imageRepository.GetByIdAsync(id);
        if (image is null || image.TenantId != tenantId)
            return ResponseData<bool>.Failure("Image not found.", 404);

        var wasPrimary = image.IsPrimary;
        Guid productId = image.ProductId;
        var publicId = image.CloudinaryPublicId;

        // Remove the DB row first and commit — this must succeed regardless of the storage outcome.
        await imageRepository.DeleteAsync(image);

        // Preserve the single-primary invariant: if the primary was deleted, promote the next image.
        if (wasPrimary)
        {
            var remaining = (await imageRepository.GetByProductAsync(productId, ct))
                .Where(i => i.Id != id)
                .OrderBy(i => i.SortOrder)
                .ToList();
            if (remaining.Count > 0)
            {
                remaining[0].IsPrimary = true;
                await imageRepository.UpdateAsync(remaining[0]);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Best-effort storage delete. CloudinaryImageStorageService logs failures internally; a
        // storage error must not surface as a failure here since the DB row is already gone.
        // CA1031 suppressed deliberately: any storage-provider exception (network, auth, 4xx/5xx)
        // must be swallowed here by design, not just specific ones — the DB row deletion already
        // succeeded and is the source of truth.
#pragma warning disable CA1031
        try
        {
            await imageStorage.DeleteAsync(publicId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Cloudinary delete failed for publicId {PublicId} (image {ImageId}); DB row already removed",
                publicId, id);
        }
#pragma warning restore CA1031

        await auditLogService.LogAsync(deletedByUserId, tenantId, "ProductImageDeleted", "ProductImage", id,
            new { productId, wasPrimary }, null, ipAddress, userAgent);

        logger.LogInformation("Image {ImageId} deleted for product {ProductId} tenant {TenantId}",
            id, productId, tenantId);
        return ResponseData<bool>.Success(true, "Image deleted.");
    }

    public async Task<ResponseData<bool>> SetPrimaryAsync(SetPrimaryRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        ProductImage? image = await imageRepository.GetByIdAsync(request.ImageId);
        if (image is null || image.TenantId != tenantId)
            return ResponseData<bool>.Failure("Image not found.", 404);

        IReadOnlyList<ProductImage> images = await imageRepository.GetByProductAsync(image.ProductId, ct);
        foreach (ProductImage img in images)
        {
            var shouldBePrimary = img.Id == image.Id;
            if (img.IsPrimary != shouldBePrimary)
            {
                img.IsPrimary = shouldBePrimary;
                await imageRepository.UpdateAsync(img);
            }
        }

        // The chosen image may not have been in the listing if it was just added; ensure it is set.
        if (!image.IsPrimary)
        {
            image.IsPrimary = true;
            await imageRepository.UpdateAsync(image);
        }

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "ProductImagePrimarySet", "ProductImage", image.Id,
            null, new { image.ProductId, image.Id }, ipAddress, userAgent);

        logger.LogInformation("Image {ImageId} set primary for product {ProductId} tenant {TenantId}",
            image.Id, image.ProductId, tenantId);
        return ResponseData<bool>.Success(true, "Primary image set.");
    }

    public async Task<ResponseData<bool>> ReorderAsync(Guid productId, ReorderImagesRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(productId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<bool>.Failure("Product not found.", 404);

        var images = (await imageRepository.GetByProductAsync(productId, ct)).ToDictionary(i => i.Id);

        for (var index = 0; index < request.Ids.Count; index++)
        {
            if (images.TryGetValue(request.Ids[index], out ProductImage? image) && image.SortOrder != index)
            {
                image.SortOrder = index;
                await imageRepository.UpdateAsync(image);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "ProductImagesReordered", "ProductImage", productId,
            null, new { productId, OrderedIds = request.Ids }, ipAddress, userAgent);

        logger.LogInformation("Images reordered for product {ProductId} tenant {TenantId}", productId, tenantId);
        return ResponseData<bool>.Success(true, "Images reordered.");
    }

    public async Task<ResponseData<IReadOnlyList<ProductImageResponse>>> GetByProductAsync(Guid productId,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<ProductImageResponse>>.Failure("Tenant could not be resolved.", 400);

        Product? product = await productRepository.GetByIdAsync(productId);
        if (product is null || product.TenantId != tenantId)
            return ResponseData<IReadOnlyList<ProductImageResponse>>.Failure("Product not found.", 404);

        IReadOnlyList<ProductImage> images = await imageRepository.GetByProductAsync(productId, ct);
        var responses = images.OrderBy(i => i.SortOrder).Select(MapToResponse).ToList();
        return ResponseData<IReadOnlyList<ProductImageResponse>>.Success(responses);
    }

    private static ProductImageResponse MapToResponse(ProductImage i) => new()
    {
        Id = i.Id,
        ProductId = i.ProductId,
        VariantId = i.VariantId,
        Url = i.Url,
        AltText = i.AltText,
        SortOrder = i.SortOrder,
        IsPrimary = i.IsPrimary
    };
}
