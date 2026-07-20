using FashionSaaS.API.EventHandlers;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.EventHandlers;

public class OrderStatusChangedNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();

    public OrderStatusChangedNotificationHandlerTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
    }

    private NotificationService CreateNotificationService() =>
        new(_notifications.Object, _uow.Object, _tenant.Object, NullLogger<NotificationService>.Instance);

    private OrderStatusChangedNotificationHandler CreateHandler() =>
        new(CreateNotificationService(), _hubContext.Object, NullLogger<OrderStatusChangedNotificationHandler>.Instance);

    [Fact]
    public async Task Handle_PushesToTenantAndCustomerGroups()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = new OrderStatusChangedEvent(Guid.NewGuid(), tenantId, customerId, "ORD-2026-000001",
            OrderStatus.Pending, OrderStatus.Confirmed);
        var notification = new DomainEventNotification<OrderStatusChangedEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _hubClients.Verify(c => c.Group($"user:{customerId}"), Times.Once);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
