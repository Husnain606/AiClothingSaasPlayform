namespace FashionSaaS.TryOn.Application.Messaging;

public interface ITryOnEventPublisher
{
    /// <summary>
    /// Publishes a TryOnCompleted event. Implementations must never throw — a messaging
    /// outage must not fail the customer-facing try-on request (spec §9: publish-only,
    /// side-channel, not the source of truth).
    /// </summary>
    Task PublishAsync(TryOnCompletedEvent @event, CancellationToken cancellationToken);
}
