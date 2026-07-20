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

public class LowStockNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();

    public LowStockNotificationHandlerTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
    }

    private NotificationService CreateNotificationService() =>
        new(_notifications.Object, _uow.Object, _tenant.Object, NullLogger<NotificationService>.Instance);

    private LowStockNotificationHandler CreateHandler() =>
        new(CreateNotificationService(), _hubContext.Object, NullLogger<LowStockNotificationHandler>.Instance);

    [Fact]
    public async Task Handle_CreatesNotificationAndPushesToTenantGroup()
    {
        // First-ever test exercising LowStockEvent dispatch end-to-end — the event has existed
        // since InventoryService.AdjustStockAsync but had zero consumers before this handler.
        var tenantId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var evt = new LowStockEvent(variantId, tenantId, 2);
        var notification = new DomainEventNotification<LowStockEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _notifications.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.TenantId == tenantId && n.RecipientUserId == null && n.Type == NotificationType.LowStock &&
            n.EntityName == "ProductVariant" && n.EntityId == variantId)), Times.Once);
        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
