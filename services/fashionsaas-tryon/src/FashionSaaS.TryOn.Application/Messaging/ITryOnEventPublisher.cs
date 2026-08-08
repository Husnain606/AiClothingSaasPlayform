namespace FashionSaaS.TryOn.Application.Messaging;

public interface ITryOnEventPublisher
{
    /// <summary>
    /// Publishes a try-on result (success or failure). Implementations must never throw — a
    /// messaging outage must not fail the underlying try-on request.
    /// </summary>
    Task PublishAsync(TryOnResultEvent @event, CancellationToken cancellationToken);
}
