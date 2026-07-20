using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// Attached to the EXISTING subscription-billing <see cref="PaymentConfirmedEvent"/> (raised
/// in <c>SubscriptionService.ConfirmPaymentAsync</c> when SuperAdmin confirms a tenant's
/// subscription payment) — no new event, no <c>OrderService</c> involvement (design spec
/// §OPEN QUESTIONS 1). The event carries no payment/order id, so
/// <see cref="Domain.Entities.Notification.EntityId"/> uses <c>evt.TenantId</c> with
/// <c>EntityName = "TenantSubscription"</c> — the closest available identifier, not a
/// synthetic/incorrect one. See <see cref="OrderPlacedNotificationHandler"/> for why this
/// lives in the API project.
/// </summary>
public class PaymentConfirmedNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<PaymentConfirmedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<PaymentConfirmedEvent>>
{
    public async Task Handle(DomainEventNotification<PaymentConfirmedEvent> notification, CancellationToken cancellationToken)
    {
        PaymentConfirmedEvent evt = notification.DomainEvent;
        var title = "Subscription payment confirmed";
        var message = $"Payment of {evt.Amount:C} confirmed for {evt.TenantEmail}.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.PaymentConfirmed,
            title, message, "TenantSubscription", evt.TenantId, cancellationToken);

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
            logger.LogWarning(ex, "Failed to push PaymentConfirmed live notification for tenant {TenantId}", evt.TenantId);
        }
#pragma warning restore CA1031
    }
}
