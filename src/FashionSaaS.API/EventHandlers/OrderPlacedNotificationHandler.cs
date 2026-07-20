using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// Persists a broadcast (<c>RecipientUserId = null</c>) notification for every tenant admin
/// when a new order is placed, then best-effort pushes it live to the <c>tenant:{TenantId}</c>
/// SignalR group. Persist happens first (D2, persist-then-push) — a missed live push is still
/// recoverable via <c>GET api/tenant/notifications</c>. Lives in the API project (not
/// Infrastructure/EventHandlers, per the design spec) because <see cref="NotificationsHub"/> and
/// <c>IHubContext&lt;NotificationsHub&gt;</c> require the ASP.NET Core SignalR/hosting surface
/// that only the API project references — Infrastructure has no project reference to API (API
/// already references Infrastructure, so the reverse would be circular). Discovered by MediatR
/// via the API assembly added to <c>AddMediatRWithBehaviors</c>'s scan.
/// </summary>
public class OrderPlacedNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<OrderPlacedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderPlacedEvent>>
{
    public async Task Handle(DomainEventNotification<OrderPlacedEvent> notification, CancellationToken cancellationToken)
    {
        OrderPlacedEvent evt = notification.DomainEvent;
        var title = $"New order {evt.OrderNumber}";
        var message = $"Order {evt.OrderNumber} placed for {evt.Total:C}.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.OrderPlaced,
            title, message, "Order", evt.OrderId, cancellationToken);

        try
        {
            await hubContext.Clients.Group($"tenant:{evt.TenantId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        // CA1031 suppressed: this is a "must never throw" boundary (persist-then-push, D2) —
        // the Notification row already committed above, so a live-push failure of ANY kind
        // (transport, serialization, disposed hub, etc.) must be swallowed and logged rather
        // than fail a handler whose real work already succeeded (matches the Phase 5a Service
        // Bus publish-only swallow pattern).
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push OrderPlaced live notification for order {OrderId}", evt.OrderId);
        }
#pragma warning restore CA1031
    }
}
