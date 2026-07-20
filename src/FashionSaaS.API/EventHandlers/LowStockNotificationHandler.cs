using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// First-ever consumer of the existing <see cref="LowStockEvent"/> (raised in
/// <c>InventoryService.AdjustStockAsync</c> since before this phase, previously unconsumed).
/// Broadcasts to tenant admins. See <see cref="OrderPlacedNotificationHandler"/> for why this
/// lives in the API project.
/// </summary>
public class LowStockNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<LowStockNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<LowStockEvent>>
{
    public async Task Handle(DomainEventNotification<LowStockEvent> notification, CancellationToken cancellationToken)
    {
        LowStockEvent evt = notification.DomainEvent;
        var title = "Low stock alert";
        var message = $"Product variant {evt.ProductVariantId} has only {evt.Remaining} unit(s) remaining.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.LowStock,
            title, message, "ProductVariant", evt.ProductVariantId, cancellationToken);

        // CA1031 suppressed: "must never throw" boundary (persist-then-push, D2) — the
        // Notification row already committed above, so a live-push failure must be swallowed
        // and logged rather than fail a handler whose real work already succeeded.
#pragma warning disable CA1031
        try
        {
            await hubContext.Clients.Group($"tenant:{evt.TenantId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push LowStock live notification for variant {ProductVariantId}", evt.ProductVariantId);
        }
#pragma warning restore CA1031
    }
}
