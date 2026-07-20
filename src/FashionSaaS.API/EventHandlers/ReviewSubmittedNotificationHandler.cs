using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.EventHandlers;

/// <summary>
/// Fires when a customer submits a new review via <c>POST api/store/reviews</c>
/// (<c>ReviewService.SubmitAsync</c>) — lets tenant admins know a review is awaiting
/// moderation. Broadcasts to tenant admins. See <see cref="OrderPlacedNotificationHandler"/>
/// for why this lives in the API project.
/// </summary>
public class ReviewSubmittedNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<ReviewSubmittedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<ReviewSubmittedEvent>>
{
    public async Task Handle(DomainEventNotification<ReviewSubmittedEvent> notification, CancellationToken cancellationToken)
    {
        ReviewSubmittedEvent evt = notification.DomainEvent;
        var title = "New review submitted";
        var message = $"A {evt.Rating}-star review was submitted for product {evt.ProductId} and awaits moderation.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.ReviewSubmitted,
            title, message, "Review", evt.ReviewId, cancellationToken);

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
            logger.LogWarning(ex, "Failed to push ReviewSubmitted live notification for review {ReviewId}", evt.ReviewId);
        }
#pragma warning restore CA1031
    }
}
