using FashionSaaS.Application.Common;
using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Discounts;

/// <summary>
/// Discount/promo-code management. Input-shape validation (required code, value &gt; 0,
/// percentage ≤ 100, StartsAt &lt; EndsAt, non-negative MinOrderAmount, MaxRedemptions ≥ 1)
/// is handled by FluentValidation at the API boundary (CONVENTIONS §8); this service only
/// enforces business rules: code uniqueness per tenant, tenant scoping, and soft
/// deactivation vs hard delete. Redemption against real orders arrives in Phase 3.
/// </summary>
public class DiscountService(
    IDiscountRepository discountRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    ICurrentTenantService currentTenant,
    ILogger<DiscountService> logger)
{
    public async Task<ResponseData<DiscountResponse>> CreateAsync(CreateDiscountRequest request,
        Guid createdByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<DiscountResponse>.Failure("Tenant could not be resolved.", 400);

        if (await discountRepository.CodeExistsAsync(tenantId, request.Code, null, ct))
            return ResponseData<DiscountResponse>.Failure($"Code '{request.Code}' is already in use.", 409);

        var discount = new Discount
        {
            TenantId = tenantId,
            Code = request.Code,
            Type = request.Type,
            Value = request.Value,
            MinOrderAmount = request.MinOrderAmount,
            MaxRedemptions = request.MaxRedemptions,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            RedemptionCount = 0,
            IsActive = true
        };

        await discountRepository.AddAsync(discount);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(createdByUserId, tenantId, "DiscountCreated", "Discount", discount.Id,
            null, new { discount.Code, discount.Type, discount.Value, discount.StartsAt, discount.EndsAt }, ipAddress, userAgent);

        logger.LogInformation("Discount {DiscountId} ({Code}) created for tenant {TenantId}",
            discount.Id, discount.Code, tenantId);

        return ResponseData<DiscountResponse>.Success(MapToResponse(discount), "Discount created.", 201);
    }

    public async Task<ResponseData<DiscountResponse>> UpdateAsync(Guid id, UpdateDiscountRequest request,
        Guid updatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<DiscountResponse>.Failure("Tenant could not be resolved.", 400);

        var discount = await discountRepository.GetByIdAsync(id);
        if (discount is null || discount.TenantId != tenantId)
            return ResponseData<DiscountResponse>.Failure("Discount not found.", 404);

        if (await discountRepository.CodeExistsAsync(tenantId, request.Code, id, ct))
            return ResponseData<DiscountResponse>.Failure($"Code '{request.Code}' is already in use.", 409);

        var old = new { discount.Code, discount.Type, discount.Value, discount.MinOrderAmount, discount.MaxRedemptions, discount.StartsAt, discount.EndsAt };
        discount.Code = request.Code;
        discount.Type = request.Type;
        discount.Value = request.Value;
        discount.MinOrderAmount = request.MinOrderAmount;
        discount.MaxRedemptions = request.MaxRedemptions;
        discount.StartsAt = request.StartsAt;
        discount.EndsAt = request.EndsAt;

        await discountRepository.UpdateAsync(discount);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(updatedByUserId, tenantId, "DiscountUpdated", "Discount", discount.Id,
            old, new { discount.Code, discount.Type, discount.Value, discount.MinOrderAmount, discount.MaxRedemptions, discount.StartsAt, discount.EndsAt },
            ipAddress, userAgent);

        logger.LogInformation("Discount {DiscountId} updated for tenant {TenantId}", discount.Id, tenantId);

        return ResponseData<DiscountResponse>.Success(MapToResponse(discount), "Discount updated.");
    }

    public async Task<ResponseData<bool>> DeactivateAsync(Guid id,
        Guid deactivatedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var discount = await discountRepository.GetByIdAsync(id);
        if (discount is null || discount.TenantId != tenantId)
            return ResponseData<bool>.Failure("Discount not found.", 404);

        var previous = discount.IsActive;
        discount.IsActive = false;

        await discountRepository.UpdateAsync(discount);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deactivatedByUserId, tenantId, "DiscountDeactivated", "Discount", discount.Id,
            new { IsActive = previous }, new { IsActive = false }, ipAddress, userAgent);

        logger.LogInformation("Discount {DiscountId} deactivated for tenant {TenantId}", discount.Id, tenantId);
        return ResponseData<bool>.Success(true, "Discount deactivated.");
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id,
        Guid deletedByUserId, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        var discount = await discountRepository.GetByIdAsync(id);
        if (discount is null || discount.TenantId != tenantId)
            return ResponseData<bool>.Failure("Discount not found.", 404);

        await discountRepository.DeleteAsync(discount);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(deletedByUserId, tenantId, "DiscountDeleted", "Discount", discount.Id,
            new { discount.Code }, null, ipAddress, userAgent);

        logger.LogInformation("Discount {DiscountId} deleted for tenant {TenantId}", discount.Id, tenantId);
        return ResponseData<bool>.Success(true, "Discount deleted.");
    }

    public async Task<ResponseData<IReadOnlyList<DiscountResponse>>> GetAllAsync(CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<IReadOnlyList<DiscountResponse>>.Failure("Tenant could not be resolved.", 400);

        var discounts = await discountRepository.GetByTenantAsync(tenantId, ct);
        var responses = discounts.Select(MapToResponse).ToList();
        return ResponseData<IReadOnlyList<DiscountResponse>>.Success(responses);
    }

    public async Task<ResponseData<DiscountResponse>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<DiscountResponse>.Failure("Tenant could not be resolved.", 400);

        var discount = await discountRepository.GetByIdAsync(id);
        if (discount is null || discount.TenantId != tenantId)
            return ResponseData<DiscountResponse>.Failure("Discount not found.", 404);

        return ResponseData<DiscountResponse>.Success(MapToResponse(discount));
    }

    public async Task<ResponseData<DiscountResponse>> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<DiscountResponse>.Failure("Tenant could not be resolved.", 400);

        var discount = await discountRepository.GetByCodeAsync(tenantId, code, ct);
        if (discount is null)
            return ResponseData<DiscountResponse>.Failure("Discount not found.", 404);

        return ResponseData<DiscountResponse>.Success(MapToResponse(discount));
    }

    private static DiscountResponse MapToResponse(Discount d) => new()
    {
        Id = d.Id,
        Code = d.Code,
        Type = d.Type,
        Value = d.Value,
        MinOrderAmount = d.MinOrderAmount,
        MaxRedemptions = d.MaxRedemptions,
        RedemptionCount = d.RedemptionCount,
        StartsAt = d.StartsAt,
        EndsAt = d.EndsAt,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt
    };
}
