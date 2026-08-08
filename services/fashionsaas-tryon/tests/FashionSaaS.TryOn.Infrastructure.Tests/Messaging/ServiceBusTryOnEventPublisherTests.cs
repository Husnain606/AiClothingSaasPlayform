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

        var @event = new TryOnResultEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow,
            IsSuccess: true, ResultImageUrl: "https://example.hf.space/file=result.png", FailureReason: null);

        Func<Task> act = async () => await publisher.PublishAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_UnexpectedExceptionType_SwallowsAndDoesNotThrow()
    {
        // ITryOnEventPublisher's contract is "must never throw" for ANY messaging failure, not just
        // ServiceBusException or InvalidOperationException. A disposed ServiceBusClient throws
        // ObjectDisposedException when creating a sender, representative of that broader exception
        // class, proving the widened catch-all still swallows and logs it.
        const string unreachableConnectionString =
            "Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=invalid;";

        ServiceBusClient client = new(unreachableConnectionString, new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(2) }
        });
        await client.DisposeAsync();

        IOptions<ServiceBusSettings> settings = Options.Create(new ServiceBusSettings { ConnectionString = unreachableConnectionString, TopicName = "tryon-events" });
        var publisher = new ServiceBusTryOnEventPublisher(client, settings, NullLogger<ServiceBusTryOnEventPublisher>.Instance);

        var @event = new TryOnResultEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow,
            IsSuccess: false, ResultImageUrl: null, FailureReason: "Render failed");

        Func<Task> act = async () => await publisher.PublishAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
