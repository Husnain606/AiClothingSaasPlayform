using FashionSaaS.API.EventHandlers;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.EventHandlers;

public class OrderPlacedNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();

    public OrderPlacedNotificationHandlerTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
    }

    private NotificationService CreateNotificationService() =>
        new(_notifications.Object, _uow.Object, _tenant.Object, NullLogger<NotificationService>.Instance);

    private OrderPlacedNotificationHandler CreateHandler() =>
        new(CreateNotificationService(), _hubContext.Object, NullLogger<OrderPlacedNotificationHandler>.Instance);

    [Fact]
    public async Task Handle_CreatesNotificationAndPushesToTenantGroup()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var evt = new OrderPlacedEvent(orderId, tenantId, "ORD-2026-000001", 99.00m);
        var notification = new DomainEventNotification<OrderPlacedEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _notifications.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.TenantId == tenantId && n.RecipientUserId == null && n.Type == NotificationType.OrderPlaced &&
            n.EntityName == "Order" && n.EntityId == orderId)), Times.Once);
        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HubPushThrows_SwallowsAndLogsWarning()
    {
        _clientProxy
            .Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var evt = new OrderPlacedEvent(Guid.NewGuid(), Guid.NewGuid(), "ORD-2026-000002", 50m);
        var notification = new DomainEventNotification<OrderPlacedEvent>(evt);

        Func<Task> act = async () => await CreateHandler().Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notifications.Verify(r => r.AddAsync(It.IsAny<Notification>()), Times.Once);
    }
}
