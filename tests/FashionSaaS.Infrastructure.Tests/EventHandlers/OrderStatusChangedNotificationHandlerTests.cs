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
    private readonly Mock<ICustomerRepository> _customerRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
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
        new(CreateNotificationService(), _customerRepository.Object, _userRepository.Object, _hubContext.Object,
            NullLogger<OrderStatusChangedNotificationHandler>.Instance);

    [Fact]
    public async Task Handle_PushesToTenantGroup_AndToLinkedUserGroup_WhenCustomerEmailMatchesAUser()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string email = "shopper@example.com";
        var customer = new Customer { Id = customerId, TenantId = tenantId, Email = email };
        var user = new User { Id = userId, Email = email };
        _customerRepository.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);
        _userRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync(user);

        var evt = new OrderStatusChangedEvent(Guid.NewGuid(), tenantId, customerId, "ORD-2026-000001",
            OrderStatus.Pending, OrderStatus.Confirmed);
        var notification = new DomainEventNotification<OrderStatusChangedEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _hubClients.Verify(c => c.Group($"user:{userId}"), Times.Once);
        _hubClients.Verify(c => c.Group($"user:{customerId}"), Times.Never);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_SkipsCustomerPush_WhenNoUserAccountLinksToTheCustomersEmail()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        const string email = "guest-legacy@example.com";
        var customer = new Customer { Id = customerId, TenantId = tenantId, Email = email };
        _customerRepository.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);
        _userRepository.Setup(r => r.GetByEmailAsync(email)).ReturnsAsync((User?)null);

        var evt = new OrderStatusChangedEvent(Guid.NewGuid(), tenantId, customerId, "ORD-2026-000002",
            OrderStatus.Pending, OrderStatus.Confirmed);
        var notification = new DomainEventNotification<OrderStatusChangedEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _hubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("user:", StringComparison.Ordinal))), Times.Never);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
