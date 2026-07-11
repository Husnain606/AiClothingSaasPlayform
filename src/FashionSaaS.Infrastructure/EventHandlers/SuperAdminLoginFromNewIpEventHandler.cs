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
        await emailService.SendSecurityAlertAsync(evt.Email, evt.NewIpAddress, evt.OccurredAt);

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
