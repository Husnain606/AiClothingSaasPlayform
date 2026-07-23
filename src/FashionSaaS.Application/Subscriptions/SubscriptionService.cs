using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Subscriptions.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Subscriptions;

public class SubscriptionService(
    ISubscriptionRepository subscriptionRepository,
    IPaymentRepository paymentRepository,
    ISubscriptionPlanRepository planRepository,
    ITenantRepository tenantRepository,
    IBankAccountRepository bankAccountRepository,
    IEmailService emailService,
    IAuditLogService auditLogService,
    IUnitOfWork unitOfWork,
    IFieldEncryptionService fieldEncryption,
    ILogger<SubscriptionService> logger)
{
    // ── AssignAsync ──────────────────────────────────────────────────────────

    public async Task<ResponseData<SubscriptionResponse>> AssignAsync(AssignSubscriptionRequest request,
        Guid adminId, string ip, string ua)
    {
        Tenant? tenant = await tenantRepository.GetByIdAsync(request.TenantId);
        if (tenant is null)
            return ResponseData<SubscriptionResponse>.Failure("Tenant not found.", 404);

        SubscriptionPlan? plan = await planRepository.GetByIdAsync(request.PlanId);
        if (plan is null)
            return ResponseData<SubscriptionResponse>.Failure("Plan not found.", 404);

        var durationDays = plan.PlanType == SubscriptionPlanType.FreeTrial ? plan.TrialDays : plan.DurationDays;
        DateTime endDate = request.StartDate.AddDays(durationDays);

        var subscription = new TenantSubscription
        {
            TenantId = request.TenantId,
            PlanId = request.PlanId,
            StartDate = request.StartDate,
            EndDate = endDate,
            Status = SubscriptionStatus.Active
        };
        subscription.AddDomainEvent(new SubscriptionAssignedEvent(tenant.Id, tenant.Email, plan.Name, endDate));

        await subscriptionRepository.AddAsync(subscription);

        // For paid plans: create a pending payment
        if (plan.PlanType != SubscriptionPlanType.FreeTrial && plan.Price > 0)
        {
            var payment = new SubscriptionPayment
            {
                TenantId = request.TenantId,
                SubscriptionId = subscription.Id,
                Amount = plan.Price,
                DueDate = DateTime.UtcNow.AddDays(7),
                Status = PaymentStatus.Pending
            };
            await paymentRepository.AddAsync(payment);

            BankAccount? platformAccount = await bankAccountRepository.GetPlatformAccountAsync();
            var bankDetails = platformAccount is not null
                ? $"Bank: {fieldEncryption.Decrypt(platformAccount.BankNameEncrypted)}, " +
                  $"Account: {fieldEncryption.MaskAccountNumber(fieldEncryption.Decrypt(platformAccount.AccountNumberEncrypted))}"
                : "Contact admin for bank details.";

            // Best-effort: a notification-send failure must never block the subscription/payment
            // rows staged above from being committed by the SaveChangesAsync call below, nor
            // turn an otherwise-successful assignment into a 500.
            try
            {
                await emailService.SendSubscriptionAssignedAsync(tenant.Email, plan.Name, endDate, bankDetails);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send SubscriptionAssigned email to {Email} for tenant {TenantId}.",
                    tenant.Email, tenant.Id);
            }
#pragma warning restore CA1031
        }

        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, tenant.Id, "SubscriptionAssigned", "TenantSubscription",
            subscription.Id, null, new { plan.Name, subscription.StartDate, subscription.EndDate }, ip, ua);

        return ResponseData<SubscriptionResponse>.Success(Map(subscription, plan), "Subscription assigned.", 201);
    }

    // ── ChangePlanAsync ──────────────────────────────────────────────────────

    public async Task<ResponseData<SubscriptionResponse>> ChangePlanAsync(Guid subscriptionId, Guid newPlanId,
        Guid adminId, string ip, string ua)
    {
        TenantSubscription? subscription = await subscriptionRepository.GetByIdAsync(subscriptionId);
        if (subscription is null)
            return ResponseData<SubscriptionResponse>.Failure("Subscription not found.", 404);

        SubscriptionPlan? newPlan = await planRepository.GetByIdAsync(newPlanId);
        if (newPlan is null)
            return ResponseData<SubscriptionResponse>.Failure("Plan not found.", 404);

        var old = new { subscription.PlanId, subscription.EndDate };

        var durationDays = newPlan.PlanType == SubscriptionPlanType.FreeTrial ? newPlan.TrialDays : newPlan.DurationDays;
        subscription.PlanId = newPlanId;
        subscription.EndDate = subscription.StartDate.AddDays(durationDays);
        subscription.Status = SubscriptionStatus.Active;

        await subscriptionRepository.UpdateAsync(subscription);

        // Create a new pending payment for the new plan if it is a paid plan
        if (newPlan.PlanType != SubscriptionPlanType.FreeTrial && newPlan.Price > 0)
        {
            var payment = new SubscriptionPayment
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                Amount = newPlan.Price,
                DueDate = DateTime.UtcNow.AddDays(7),
                Status = PaymentStatus.Pending
            };
            await paymentRepository.AddAsync(payment);
        }

        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, subscription.TenantId, "SubscriptionPlanChanged",
            "TenantSubscription", subscription.Id, old,
            new { subscription.PlanId, subscription.EndDate }, ip, ua);

        return ResponseData<SubscriptionResponse>.Success(Map(subscription, newPlan));
    }

    // ── SuspendAsync ─────────────────────────────────────────────────────────

    public async Task<ResponseData<SubscriptionResponse>> SuspendAsync(Guid subscriptionId,
        Guid adminId, string ip, string ua)
    {
        TenantSubscription? subscription = await subscriptionRepository.GetByIdAsync(subscriptionId);
        if (subscription is null)
            return ResponseData<SubscriptionResponse>.Failure("Subscription not found.", 404);
        if (subscription.Status == SubscriptionStatus.Suspended)
            return ResponseData<SubscriptionResponse>.Failure("Subscription is already suspended.", 400);

        var old = new { subscription.Status };
        subscription.Status = SubscriptionStatus.Suspended;

        await subscriptionRepository.UpdateAsync(subscription);
        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, subscription.TenantId, "SubscriptionSuspended",
            "TenantSubscription", subscription.Id, old, new { subscription.Status }, ip, ua);

        SubscriptionPlan? plan = await planRepository.GetByIdAsync(subscription.PlanId);
        return ResponseData<SubscriptionResponse>.Success(Map(subscription, plan));
    }

    // ── ReactivateAsync ──────────────────────────────────────────────────────

    public async Task<ResponseData<SubscriptionResponse>> ReactivateAsync(Guid subscriptionId,
        Guid adminId, string ip, string ua)
    {
        TenantSubscription? subscription = await subscriptionRepository.GetByIdAsync(subscriptionId);
        if (subscription is null)
            return ResponseData<SubscriptionResponse>.Failure("Subscription not found.", 404);
        if (subscription.Status == SubscriptionStatus.Active)
            return ResponseData<SubscriptionResponse>.Failure("Subscription is already active.", 400);

        var old = new { subscription.Status };
        subscription.Status = SubscriptionStatus.Active;

        await subscriptionRepository.UpdateAsync(subscription);
        await unitOfWork.SaveChangesAsync();
        await auditLogService.LogAsync(adminId, subscription.TenantId, "SubscriptionReactivated",
            "TenantSubscription", subscription.Id, old, new { subscription.Status }, ip, ua);

        SubscriptionPlan? plan = await planRepository.GetByIdAsync(subscription.PlanId);
        return ResponseData<SubscriptionResponse>.Success(Map(subscription, plan));
    }

    // ── ConfirmPaymentAsync ──────────────────────────────────────────────────

    public async Task<ResponseData<PaymentResponse>> ConfirmPaymentAsync(Guid paymentId,
        Guid adminId, string ip, string ua)
    {
        SubscriptionPayment? payment = await paymentRepository.GetByIdAsync(paymentId);
        if (payment is null)
            return ResponseData<PaymentResponse>.Failure("Payment not found.", 404);
        if (payment.Status == PaymentStatus.Confirmed)
            return ResponseData<PaymentResponse>.Failure("Payment already confirmed.", 400);

        var old = new { payment.Status };
        payment.Status = PaymentStatus.Confirmed;
        payment.PaidAt = DateTime.UtcNow;
        payment.ConfirmedByAdminId = adminId;

        Tenant? tenant = await tenantRepository.GetByIdAsync(payment.TenantId);
        if (tenant is not null)
        {
            payment.AddDomainEvent(new PaymentConfirmedEvent(tenant.Id, tenant.Email, payment.Amount));
        }

        await paymentRepository.UpdateAsync(payment);
        await unitOfWork.SaveChangesAsync();

        if (tenant is not null)
        {
            // Best-effort: the payment row already committed above (SaveChangesAsync). A
            // notification-send failure must never turn an already-confirmed payment into a 500.
            try
            {
                await emailService.SendPaymentConfirmedAsync(tenant.Email, payment.Amount);
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send PaymentConfirmed email to {Email} for payment {PaymentId}.",
                    tenant.Email, payment.Id);
            }
#pragma warning restore CA1031
        }

        await auditLogService.LogAsync(adminId, payment.TenantId, "PaymentConfirmed", "SubscriptionPayment",
            payment.Id, old, new { payment.Status, payment.PaidAt }, ip, ua);

        return ResponseData<PaymentResponse>.Success(MapPayment(payment));
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<ResponseData<SubscriptionResponse>> GetByTenantAsync(Guid tenantId)
    {
        TenantSubscription? sub = await subscriptionRepository.GetActiveByTenantIdAsync(tenantId);
        if (sub is null)
            return ResponseData<SubscriptionResponse>.Failure("No active subscription.", 404);
        return ResponseData<SubscriptionResponse>.Success(Map(sub, sub.Plan));
    }

    public async Task<ResponseData<IReadOnlyList<SubscriptionResponse>>> GetAllAsync()
    {
        IReadOnlyList<TenantSubscription> subs = await subscriptionRepository.GetAllAsync();
        return ResponseData<IReadOnlyList<SubscriptionResponse>>.Success(
            subs.Select(s => Map(s, s.Plan)).ToList());
    }

    public async Task<ResponseData<PaymentResponse>> GetPaymentByIdAsync(Guid paymentId)
    {
        SubscriptionPayment? payment = await paymentRepository.GetByIdAsync(paymentId);
        if (payment is null)
            return ResponseData<PaymentResponse>.Failure("Payment not found.", 404);
        return ResponseData<PaymentResponse>.Success(MapPayment(payment));
    }

    public async Task<ResponseData<IReadOnlyList<PaymentResponse>>> GetPaymentsBySubscriptionAsync(Guid subscriptionId)
    {
        IReadOnlyList<SubscriptionPayment> payments = await paymentRepository.GetBySubscriptionAsync(subscriptionId);
        return ResponseData<IReadOnlyList<PaymentResponse>>.Success(payments.Select(MapPayment).ToList());
    }

    public async Task<ResponseData<IReadOnlyList<PaymentResponse>>> GetAllPaymentsAsync()
    {
        IReadOnlyList<SubscriptionPayment> payments = await paymentRepository.GetAllAsync();
        return ResponseData<IReadOnlyList<PaymentResponse>>.Success(payments.Select(MapPayment).ToList());
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static SubscriptionResponse Map(TenantSubscription s, SubscriptionPlan? p) => new()
    {
        Id = s.Id,
        TenantId = s.TenantId,
        PlanName = p?.Name ?? string.Empty,
        Status = s.Status,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        Price = p?.Price ?? 0
    };

    private static PaymentResponse MapPayment(SubscriptionPayment p) => new()
    {
        Id = p.Id,
        TenantId = p.TenantId,
        SubscriptionId = p.SubscriptionId,
        Amount = p.Amount,
        DueDate = p.DueDate,
        PaidAt = p.PaidAt,
        Status = p.Status
    };
}
