using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
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
            // CA1031 suppressed deliberately: this is a BackgroundService's timer loop — any
            // unhandled exception from one run must not crash the host or stop future runs.
            // OperationCanceledException is rethrown (shutdown), everything else is logged
            // and the loop continues.
#pragma warning disable CA1031
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
#pragma warning restore CA1031
        }
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ISubscriptionRepository subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionRepository>();
        IPaymentRepository payments = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        ITenantRepository tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        IEmailService email = scope.ServiceProvider.GetRequiredService<IEmailService>();
        IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        DateTime now = DateTime.UtcNow;

        // ── 1. Expire active subscriptions past their end date ───────────────
        IReadOnlyList<TenantSubscription> expired = await subscriptions.GetExpiredActiveAsync(now);
        foreach (TenantSubscription sub in expired)
        {
            DateTime gracePeriod = sub.EndDate.AddDays(3);
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
        IReadOnlyList<SubscriptionPayment> overduePayments = await payments.GetPendingOverdueAsync(now);
        foreach (SubscriptionPayment payment in overduePayments)
        {
            payment.Status = PaymentStatus.Overdue;
            payment.AddDomainEvent(new PaymentOverdueEvent(
                payment.TenantId,
                payment.Tenant?.Email ?? string.Empty,
                payment.Amount,
                payment.DueDate));
            await payments.UpdateAsync(payment);

            Tenant? tenant = await tenants.GetByIdAsync(payment.TenantId);
            if (tenant is not null)
                await email.SendPaymentOverdueAsync(tenant.Email, payment.Amount, payment.DueDate);

            logger.LogInformation("Marked payment {PaymentId} as overdue", payment.Id);
        }

        // ── 3. Send 7-day payment reminders ──────────────────────────────────
        IReadOnlyList<SubscriptionPayment> dueSoon = await payments.GetDueSoonAsync(now.AddDays(7));
        foreach (SubscriptionPayment payment in dueSoon)
        {
            payment.AddDomainEvent(new PaymentReminderEvent(
                payment.TenantId,
                payment.Tenant?.Email ?? string.Empty,
                payment.Amount,
                payment.DueDate));

            Tenant? tenant = await tenants.GetByIdAsync(payment.TenantId);
            if (tenant is not null)
                await email.SendPaymentReminderAsync(tenant.Email, payment.Amount, payment.DueDate);

            logger.LogInformation("Sent payment reminder for payment {PaymentId}", payment.Id);
        }

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("SubscriptionExpiryJob completed at {Time}", now);
    }
}
