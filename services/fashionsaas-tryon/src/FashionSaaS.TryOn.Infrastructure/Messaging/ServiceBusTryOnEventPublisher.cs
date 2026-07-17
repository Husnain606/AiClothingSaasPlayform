using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.TryOn.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Messaging;

public class ServiceBusTryOnEventPublisher(
    ServiceBusClient client,
    IOptions<ServiceBusSettings> settings,
    ILogger<ServiceBusTryOnEventPublisher> logger) : ITryOnEventPublisher
{
    private readonly string _topicName = settings.Value.TopicName;

    public async Task PublishAsync(TryOnCompletedEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceBusSender sender = client.CreateSender(_topicName);
            var body = JsonSerializer.Serialize(@event);
            var message = new ServiceBusMessage(body) { ContentType = "application/json" };
            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ServiceBusException or InvalidOperationException)
        {
            // Publish-only side-channel (spec §9) — a Service Bus outage must never fail the
            // customer-facing try-on request. Logged and swallowed, not rethrown.
            logger.LogWarning(ex, "Failed to publish TryOnCompleted event for TryOnRequestId {TryOnRequestId}", @event.TryOnRequestId);
        }
    }
}
