using Azure.Messaging.ServiceBus;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Tests.Messaging;

public class ServiceBusTryOnEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_UnreachableNamespace_SwallowsExceptionAndDoesNotThrow()
    {
        // A syntactically-valid but unreachable connection string — proves a real
        // send failure (timeout/connection-refused) is caught and logged, not rethrown,
        // per spec §9's "must not fail the customer-facing request" contract.
        const string unreachableConnectionString =
            "Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=invalid;";

        await using ServiceBusClient client = new(unreachableConnectionString, new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(2) }
        });
        IOptions<ServiceBusSettings> settings = Options.Create(new ServiceBusSettings { ConnectionString = unreachableConnectionString, TopicName = "tryon-events" });
        var publisher = new ServiceBusTryOnEventPublisher(client, settings, NullLogger<ServiceBusTryOnEventPublisher>.Instance);

        var @event = new TryOnCompletedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        Func<Task> act = async () => await publisher.PublishAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
