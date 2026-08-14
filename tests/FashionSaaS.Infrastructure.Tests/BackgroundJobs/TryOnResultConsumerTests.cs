using System.Reflection;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Registers NotificationService as SCOPED, exactly as the real app does
    /// (ServiceCollectionExtensions.AddApplicationServices), so these tests exercise the same
    /// scope-resolution path production uses instead of handing the consumer a captured instance.
    /// </summary>
    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddSingleton(_notifications.Object);
        services.AddSingleton(_uow.Object);
        services.AddSingleton(_tenant.Object);
        services.AddSingleton<ILogger<NotificationService>>(NullLogger<NotificationService>.Instance);
        services.AddScoped<NotificationService>();

        // validateScopes mirrors what WebApplicationBuilder.Build() enables in Development.
        return services.BuildServiceProvider(validateScopes: true);
    }

    private TryOnResultConsumer CreateConsumer(ServiceProvider provider)
    {
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
            provider.GetRequiredService<IServiceScopeFactory>(),
            _hubContext.Object,
            NullLogger<TryOnResultConsumer>.Instance,
            client,
            settings);
    }

    // Mirrors ServiceBusTryOnEventPublisher's plain JsonSerializer.Serialize(@event) - no naming
    // policy, so the wire format is PascalCase.
    //
    // NOTE, honestly: this pins down what THIS service accepts; it does NOT prove the try-on
    // service still sends it. The two live in separate solutions with no shared assembly, so if
    // TryOnResultEvent changes there, nothing here fails - the payload just stops deserializing at
    // runtime. Deserialization is case-insensitive, which absorbs casing drift but not renames.
    // A real guard would need a shared contract package or a cross-service integration test.
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

        using ServiceProvider provider = BuildContainer();
        using TryOnResultConsumer consumer = CreateConsumer(provider);
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

        using ServiceProvider provider = BuildContainer();
        using TryOnResultConsumer consumer = CreateConsumer(provider);
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

        using ServiceProvider provider = BuildContainer();
        using TryOnResultConsumer consumer = CreateConsumer(provider);
        Func<Task> act = async () => await consumer.HandleMessageAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notifications.Verify(r => r.AddAsync(It.IsAny<Notification>()), Times.Once,
            "the notification must still be persisted even though the live push failed");
    }

    [Fact]
    public void Consumer_ResolvesNotificationServicePerMessage_NotAsACapturedSingletonDependency()
    {
        // Regression guard. TryOnResultConsumer is registered via AddHostedService, i.e. a SINGLETON,
        // while NotificationService is scoped (AddApplicationServices) and holds a scoped
        // ApplicationDbContext. Taking NotificationService as a constructor parameter is a captive
        // dependency: WebApplicationBuilder.Build() enables scope validation in Development and the
        // whole API then throws "Cannot consume scoped service ... from singleton" at startup.
        // The earlier version of this class did exactly that, and every test here missed it because
        // they constructed the consumer with `new` and never went through the container.
        ConstructorInfo ctor = typeof(TryOnResultConsumer).GetConstructors().Single();

        ctor.GetParameters().Should().NotContain(p => p.ParameterType == typeof(NotificationService),
            "a singleton BackgroundService must not capture a scoped service; resolve it from IServiceScopeFactory per message");
        ctor.GetParameters().Should().Contain(p => p.ParameterType == typeof(IServiceScopeFactory));
    }

    [Fact]
    public async Task HandleMessageAsync_UndeserializableBody_DoesNotThrowAndCreatesNoNotification()
    {
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            BinaryData.FromString("not json at all"));

        using ServiceProvider provider = BuildContainer();
        using TryOnResultConsumer consumer = CreateConsumer(provider);
        Func<Task> act = async () => await consumer.HandleMessageAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notifications.Verify(r => r.AddAsync(It.IsAny<Notification>()), Times.Never);
    }
}
