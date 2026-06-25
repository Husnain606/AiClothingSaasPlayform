using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Infrastructure.BackgroundJobs;

public class SubscriptionExpiryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionExpiryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SubscriptionExpiryJob failed");
            }
        }
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        var payments     = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var tenants      = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var email        = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var uow          = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTime.UtcNow;

        // ── 1. Expire active subscriptions past their end date ───────────────
        var expired = await subscriptions.GetExpiredActiveAsync(now);
        foreach (var sub in expired)
        {
            var gracePeriod = sub.EndDate.AddDays(3);
            if (now >= gracePeriod)
            {
                sub.Status = SubscriptionStatus.Expired;
                sub.AddDomainEvent(new SubscriptionExpiredEvent(sub.TenantId, sub.Tenant?.Email ?? string.Empty));
                await subscriptions.UpdateAsync(sub);

                if (sub.Tenant is not null)
                {
                    sub.Tenant.IsActive = false;
                    await tenants.UpdateAsync(sub.Tenant);
                    await email.SendTenantSuspendedAsync(sub.Tenant.Email, "Subscription expired.");
                    logger.LogInformation("Suspended tenant {TenantId} due to expired subscription", sub.TenantId);
                }
            }
        }

        // ── 2. Mark overdue payments ─────────────────────────────────────────
        var overduePayments = await payments.GetPendingOverdueAsync(now);
        foreach (var payment in overduePayments)
        {
            payment.Status = PaymentStatus.Overdue;
            payment.AddDomainEvent(new PaymentOverdueEvent(
                payment.TenantId,
                payment.Tenant?.Email ?? string.Empty,
                payment.Amount,
                payment.DueDate));
            await payments.UpdateAsync(payment);

            var tenant = await tenants.GetByIdAsync(payment.TenantId);
            if (tenant is not null)
                await email.SendPaymentOverdueAsync(tenant.Email, payment.Amount, payment.DueDate);

            logger.LogInformation("Marked payment {PaymentId} as overdue", payment.Id);
        }

        // ── 3. Send 7-day payment reminders ──────────────────────────────────
        var dueSoon = await payments.GetDueSoonAsync(now.AddDays(7));
        foreach (var payment in dueSoon)
        {
            payment.AddDomainEvent(new PaymentReminderEvent(
                payment.TenantId,
                payment.Tenant?.Email ?? string.Empty,
                payment.Amount,
                payment.DueDate));

            var tenant = await tenants.GetByIdAsync(payment.TenantId);
            if (tenant is not null)
                await email.SendPaymentReminderAsync(tenant.Email, payment.Amount, payment.DueDate);

            logger.LogInformation("Sent payment reminder for payment {PaymentId}", payment.Id);
        }

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("SubscriptionExpiryJob completed at {Time}", now);
    }
}
