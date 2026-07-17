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
        // CA1031: bare catch is deliberate here, not sloppy — ITryOnEventPublisher's contract says
        // implementations "must never throw". This is a publish-only side-channel (spec §9), and a
        // messaging failure of ANY exception type (ServiceBusException, ObjectDisposedException,
        // Azure.RequestFailedException, a misconfigured-client Exception, etc.) must never turn an
        // already-persisted, already-successful try-on request into a failure for the customer. — 2026-07-17
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish TryOnCompleted event for TryOnRequestId {TryOnRequestId}", @event.TryOnRequestId);
        }
#pragma warning restore CA1031
    }
}
