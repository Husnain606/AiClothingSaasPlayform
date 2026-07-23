using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Infrastructure.EventHandlers;

/// <summary>
/// Handles SuperAdminLoginFromNewIpEvent dispatched by UnitOfWork via the
/// DomainEventNotification&lt;T&gt; wrapper. Sends a security alert email and
/// writes an AuditLog entry.  No secrets are logged.
/// </summary>
public class SuperAdminLoginFromNewIpEventHandler(
    IEmailService emailService,
    IAuditLogService auditLogService,
    ILogger<SuperAdminLoginFromNewIpEventHandler> logger)
    : INotificationHandler<DomainEventNotification<SuperAdminLoginFromNewIpEvent>>
{
    public async Task Handle(
        DomainEventNotification<SuperAdminLoginFromNewIpEvent> notification,
        CancellationToken cancellationToken)
    {
        SuperAdminLoginFromNewIpEvent evt = notification.DomainEvent;

        logger.LogWarning(
            "Super Admin {UserId} logged in from new IP. Sending security alert.",
            evt.UserId);

        // Send security alert email — IEmailService.SendSecurityAlertAsync is defined in Application.Interfaces
        try
        {
            await emailService.SendSecurityAlertAsync(evt.Email, evt.NewIpAddress, evt.OccurredAt);
        }
        // CA1031 suppressed: email delivery is best-effort; the business operation (login +
        // audit trail) already committed and must not fail because a notification couldn't be
        // sent — matches the persist-then-push swallow pattern used elsewhere in this codebase.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send security alert email to {Email} for SuperAdmin {UserId} new-IP login.",
                evt.Email, evt.UserId);
        }
#pragma warning restore CA1031

        // Append-only audit log entry — no raw secrets written, only IP + timestamp
        await auditLogService.LogAsync(
            userId: evt.UserId,
            tenantId: null,
            action: "SuperAdminLoginFromNewIp",
            entityName: "User",
            entityId: evt.UserId,
            oldValues: null,
            newValues: new { evt.NewIpAddress, evt.OccurredAt },
            ipAddress: evt.NewIpAddress,
            userAgent: "System");
    }
}
