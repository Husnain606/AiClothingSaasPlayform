using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.API.BackgroundJobs;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.BackgroundJobs;

public class TryOnResultConsumerTests
{
    // A syntactically-valid but unreachable namespace: the consumer only touches ServiceBusClient
    // inside ExecuteAsync (never started here), so these tests exercise HandleMessageAsync without
    // any broker. Same approach as ServiceBusTryOnEventPublisherTests in the try-on service.
    private const string UnreachableConnectionString =
        "Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=invalid;";

    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();

    public TryOnResultConsumerTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
    }

    private TryOnResultConsumer CreateConsumer()
    {
        NotificationService notificationService = new(
            _notifications.Object, _uow.Object, _tenant.Object, NullLogger<NotificationService>.Instance);

#pragma warning disable CA2000 // never connects (ExecuteAsync is not started); disposed at process exit
        ServiceBusClient client = new(UnreachableConnectionString);
#pragma warning restore CA2000

        IOptions<ServiceBusSettings> settings = Options.Create(new ServiceBusSettings
        {
            ConnectionString = UnreachableConnectionString,
            TopicName = "tryon-events",
            SubscriptionName = "main-api-tryon-results"
        });

        return new TryOnResultConsumer(
            notificationService, _hubContext.Object, NullLogger<TryOnResultConsumer>.Instance, client, settings);
    }

    // Mirrors ServiceBusTryOnEventPublisher's plain JsonSerializer.Serialize(@event) — no naming
    // policy, so the wire format is PascalCase. Keeping this shape in the test is what proves the
    // two independently-deployed services still agree on the contract.
    private static ServiceBusReceivedMessage BuildMessage(object payload) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(payload)));

    [Fact]
    public async Task HandleMessageAsync_Success_CreatesTryOnCompletedNotificationAndPushesToUserGroup()
    {
        var customerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = requestId,
            TenantId = tenantId,
            CustomerId = customerId,
            ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsSuccess = true,
            ResultImageUrl = "https://space.hf.space/file=result.png",
            FailureReason = (string?)null
        });

        using TryOnResultConsumer consumer = CreateConsumer();
        await consumer.HandleMessageAsync(message, CancellationToken.None);

        _notifications.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.TenantId == tenantId && n.RecipientUserId == customerId &&
            n.Type == NotificationType.TryOnCompleted &&
            n.EntityName == "TryOnRequest" && n.EntityId == requestId)), Times.Once);

        _hubClients.Verify(c => c.Group($"user:{customerId}"), Times.Once);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_Failure_CreatesTryOnFailedNotification()
    {
        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsSuccess = false,
            ResultImageUrl = (string?)null,
            FailureReason = "Render failed"
        });

        using TryOnResultConsumer consumer = CreateConsumer();
        await consumer.HandleMessageAsync(message, CancellationToken.None);

        _notifications.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.Type == NotificationType.TryOnFailed && n.EntityName == "TryOnRequest")), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_HubPushThrows_DoesNotThrow_NotificationAlreadyPersisted()
    {
        _clientProxy
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub disposed"));

        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsSuccess = true,
            ResultImageUrl = "https://space.hf.space/file=result.png",
            FailureReason = (string?)null
        });

        using TryOnResultConsumer consumer = CreateConsumer();
        Func<Task> act = async () => await consumer.HandleMessageAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notifications.Verify(r => r.AddAsync(It.IsAny<Notification>()), Times.Once,
            "the notification must still be persisted even though the live push failed");
    }

    [Fact]
    public async Task HandleMessageAsync_UndeserializableBody_DoesNotThrowAndCreatesNoNotification()
    {
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("not json at all"));

        using TryOnResultConsumer consumer = CreateConsumer();
        Func<Task> act = async () => await consumer.HandleMessageAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notifications.Verify(r => r.AddAsync(It.IsAny<Notification>()), Times.Never);
    }
}
