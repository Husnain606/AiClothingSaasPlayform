using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// Persists a broadcast notification for tenant admins on every order status transition
/// (confirm/ship/deliver/cancel), then best-effort pushes it to both the
/// <c>tenant:{TenantId}</c> group (admins) and the <c>user:{CustomerId}</c> group (the
/// customer whose order changed) — see design spec §OPEN QUESTIONS/D3. See
/// <see cref="OrderPlacedNotificationHandler"/> for why this lives in the API project.
/// </summary>
public class OrderStatusChangedNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<OrderStatusChangedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderStatusChangedEvent>>
{
    public async Task Handle(DomainEventNotification<OrderStatusChangedEvent> notification, CancellationToken cancellationToken)
    {
        OrderStatusChangedEvent evt = notification.DomainEvent;
        var title = $"Order {evt.OrderNumber} {evt.NewStatus}";
        var message = $"Order {evt.OrderNumber} moved from {evt.PreviousStatus} to {evt.NewStatus}.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.OrderStatusChanged,
            title, message, "Order", evt.OrderId, cancellationToken);

        // CA1031 suppressed on both catches below: "must never throw" boundary (persist-then-
        // push, D2) — the Notification row already committed above, so either group push
        // failing must be swallowed and logged independently, never fail the handler.
#pragma warning disable CA1031
        try
        {
            await hubContext.Clients.Group($"tenant:{evt.TenantId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push OrderStatusChanged live notification to tenant group for order {OrderId}", evt.OrderId);
        }

        try
        {
            await hubContext.Clients.Group($"user:{evt.CustomerId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push OrderStatusChanged live notification to customer group for order {OrderId}", evt.OrderId);
        }
#pragma warning restore CA1031
    }
}
