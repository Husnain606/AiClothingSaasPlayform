using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Inventory.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Inventory;

/// <summary>
/// Stock management. Input-shape validation (variant id present, non-zero delta, valid
/// reason enum) is handled by FluentValidation at the API boundary (CONVENTIONS §8);
/// this service enforces business rules: stock may never go negative, every adjustment
/// is recorded as an append-only <see cref="StockAdjustment"/> with the resulting quantity
/// and acting user, and a <see cref="LowStockEvent"/> is raised when stock falls to or
/// below the low-stock threshold. The variant mutation and the audit row are committed in
/// a single SaveChanges so the running total and the ledger never diverge.
/// </summary>
public class InventoryService(
    IProductVariantRepository variantRepository,
    IStockAdjustmentRepository stockAdjustmentRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<InventoryService> logger)
{
    /// <summary>Stock at or below this level triggers a low-stock domain event.</summary>
    public const int LowStockThreshold = 5;

    public async Task<ResponseData<StockAdjustmentResponse>> AdjustStockAsync(AdjustStockRequest request,
        Guid adjustedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<StockAdjustmentResponse>.Failure("Tenant could not be resolved.", 400);

        // Tracked load so the StockQuantity mutation and any domain event flow through SaveChanges.
        var variant = await variantRepository.GetByIdAsync(request.VariantId);
        if (variant is null || variant.TenantId != tenantId)
            return ResponseData<StockAdjustmentResponse>.Failure("Variant not found.", 404);

        var newQty = variant.StockQuantity + request.Delta;
        if (newQty < 0)
            return ResponseData<StockAdjustmentResponse>.Failure(
                $"Adjustment would drive stock negative (current {variant.StockQuantity}, delta {request.Delta}).", 400);

        var previousQty = variant.StockQuantity;
        variant.StockQuantity = newQty;

        var adjustment = new StockAdjustment
        {
            TenantId = tenantId,
            ProductVariantId = variant.Id,
            Delta = request.Delta,
            Reason = request.Reason,
            ResultingQuantity = newQty,
            AdjustedByUserId = adjustedByUserId
        };
        await stockAdjustmentRepository.AddAsync(adjustment);

        // Raise the low-stock event on the tracked variant BEFORE saving so the
        // UnitOfWork dispatches it as part of this commit.
        if (newQty <= LowStockThreshold)
            variant.AddDomainEvent(new LowStockEvent(variant.Id, tenantId, newQty));

        await variantRepository.UpdateAsync(variant);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(adjustedByUserId, tenantId, "StockAdjusted", "ProductVariant", variant.Id,
            new { StockQuantity = previousQty },
            new { StockQuantity = newQty, request.Delta, request.Reason }, ipAddress, userAgent);

        logger.LogInformation(
            "Stock for variant {VariantId} adjusted by {Delta} to {NewQty} ({Reason}) by user {UserId} for tenant {TenantId}",
            variant.Id, request.Delta, newQty, request.Reason, adjustedByUserId, tenantId);

        return ResponseData<StockAdjustmentResponse>.Success(MapToResponse(adjustment), "Stock adjusted.", 200);
    }

    public async Task<ResponseData<IReadOnlyList<LowStockItemResponse>>> GetLowStockAsync(int threshold,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<LowStockItemResponse>>.Failure("Tenant could not be resolved.", 400);

        var variants = await variantRepository.GetLowStockAsync(tenantId, threshold, ct);
        var responses = variants.Select(v => new LowStockItemResponse
        {
            VariantId = v.Id,
            ProductId = v.ProductId,
            Sku = v.Sku,
            Size = v.Size,
            Color = v.Color,
            StockQuantity = v.StockQuantity
        }).ToList();

        return ResponseData<IReadOnlyList<LowStockItemResponse>>.Success(responses);
    }

    public async Task<ResponseData<IReadOnlyList<StockAdjustmentResponse>>> GetStockHistoryAsync(Guid variantId,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<StockAdjustmentResponse>>.Failure("Tenant could not be resolved.", 400);

        var variant = await variantRepository.GetByIdAsync(variantId);
        if (variant is null || variant.TenantId != tenantId)
            return ResponseData<IReadOnlyList<StockAdjustmentResponse>>.Failure("Variant not found.", 404);

        var adjustments = await stockAdjustmentRepository.GetByVariantAsync(variantId, ct);
        var responses = adjustments.Select(MapToResponse).ToList();
        return ResponseData<IReadOnlyList<StockAdjustmentResponse>>.Success(responses);
    }

    private static StockAdjustmentResponse MapToResponse(StockAdjustment a) => new()
    {
        Id = a.Id,
        ProductVariantId = a.ProductVariantId,
        Delta = a.Delta,
        Reason = a.Reason,
        ResultingQuantity = a.ResultingQuantity,
        AdjustedByUserId = a.AdjustedByUserId,
        CreatedAt = a.CreatedAt
    };
}
