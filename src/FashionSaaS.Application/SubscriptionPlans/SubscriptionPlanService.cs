using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.SubscriptionPlans.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.SubscriptionPlans;

public class SubscriptionPlanService(
    ISubscriptionPlanRepository planRepository,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService)
{
    public async Task<ResponseData<SubscriptionPlanResponse>> CreateAsync(CreateSubscriptionPlanRequest request,
        Guid adminId, string ip, string ua)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ResponseData<SubscriptionPlanResponse>.Failure("Plan name is required.", 400);

        if (request.Price < 0)
            return ResponseData<SubscriptionPlanResponse>.Failure("Price must be zero or greater.", 400);

        if (request.DurationDays <= 0)
            return ResponseData<SubscriptionPlanResponse>.Failure("Duration must be greater than zero.", 400);

        if (request.ProductLimit < 0 || request.UserLimit < 0 || request.AiUsageLimit < 0 || request.StorageLimitMb < 0)
            return ResponseData<SubscriptionPlanResponse>.Failure("Limits must be zero or greater.", 400);

        var plan = new SubscriptionPlan
        {
            PlanType = request.PlanType,
            Name = request.Name,
            Price = request.Price,
            DurationDays = request.DurationDays,
            TrialDays = request.TrialDays,
            ProductLimit = request.ProductLimit,
            UserLimit = request.UserLimit,
            AiUsageLimit = request.AiUsageLimit,
            StorageLimitMb = request.StorageLimitMb,
            IsActive = true
        };

        await planRepository.AddAsync(plan);
        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, null, "SubscriptionPlanCreated", "SubscriptionPlan", plan.Id,
            null, new { plan.Name, plan.Price }, ip, ua);

        return ResponseData<SubscriptionPlanResponse>.Success(Map(plan), "Plan created.", 201);
    }

    public async Task<ResponseData<SubscriptionPlanResponse>> UpdateAsync(Guid id,
        UpdateSubscriptionPlanRequest request, Guid adminId, string ip, string ua)
    {
        var plan = await planRepository.GetByIdAsync(id);
        if (plan is null)
            return ResponseData<SubscriptionPlanResponse>.Failure("Plan not found.", 404);

        if (string.IsNullOrWhiteSpace(request.Name))
            return ResponseData<SubscriptionPlanResponse>.Failure("Plan name is required.", 400);

        if (request.Price < 0)
            return ResponseData<SubscriptionPlanResponse>.Failure("Price must be zero or greater.", 400);

        var old = new { plan.Name, plan.Price };

        plan.Name = request.Name;
        plan.Price = request.Price;
        plan.DurationDays = request.DurationDays;
        plan.TrialDays = request.TrialDays;
        plan.ProductLimit = request.ProductLimit;
        plan.UserLimit = request.UserLimit;
        plan.AiUsageLimit = request.AiUsageLimit;
        plan.StorageLimitMb = request.StorageLimitMb;
        plan.IsActive = request.IsActive;

        await planRepository.UpdateAsync(plan);
        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, null, "SubscriptionPlanUpdated", "SubscriptionPlan", plan.Id,
            old, new { plan.Name, plan.Price }, ip, ua);

        return ResponseData<SubscriptionPlanResponse>.Success(Map(plan));
    }

    public async Task<ResponseData<IReadOnlyList<SubscriptionPlanResponse>>> GetAllAsync()
    {
        var plans = await planRepository.GetAllAsync();
        return ResponseData<IReadOnlyList<SubscriptionPlanResponse>>.Success(
            plans.Select(Map).ToList());
    }

    public async Task<ResponseData<IReadOnlyList<SubscriptionPlanResponse>>> GetActiveAsync()
    {
        var plans = await planRepository.GetActiveAsync();
        return ResponseData<IReadOnlyList<SubscriptionPlanResponse>>.Success(
            plans.Select(Map).ToList());
    }

    public async Task<ResponseData<SubscriptionPlanResponse>> GetByIdAsync(Guid id)
    {
        var plan = await planRepository.GetByIdAsync(id);
        if (plan is null)
            return ResponseData<SubscriptionPlanResponse>.Failure("Plan not found.", 404);

        return ResponseData<SubscriptionPlanResponse>.Success(Map(plan));
    }

    public async Task<ResponseData<bool>> DeleteAsync(Guid id, Guid adminId, string ip, string ua)
    {
        var plan = await planRepository.GetByIdAsync(id);
        if (plan is null)
            return ResponseData<bool>.Failure("Plan not found.", 404);

        await planRepository.DeleteAsync(plan);
        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, null, "SubscriptionPlanDeleted", "SubscriptionPlan", id,
            new { plan.Name }, null, ip, ua);

        return ResponseData<bool>.Success(true, "Plan deleted.");
    }

    private static SubscriptionPlanResponse Map(SubscriptionPlan p) => new()
    {
        Id = p.Id,
        PlanType = p.PlanType,
        Name = p.Name,
        Price = p.Price,
        DurationDays = p.DurationDays,
        TrialDays = p.TrialDays,
        ProductLimit = p.ProductLimit,
        UserLimit = p.UserLimit,
        AiUsageLimit = p.AiUsageLimit,
        StorageLimitMb = p.StorageLimitMb,
        IsActive = p.IsActive
    };
}
