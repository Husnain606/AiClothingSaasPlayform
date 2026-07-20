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

public class ReviewSubmittedNotificationHandlerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentTenantService> _tenant = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();

    public ReviewSubmittedNotificationHandlerTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>())).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
    }

    private NotificationService CreateNotificationService() =>
        new(_notifications.Object, _uow.Object, _tenant.Object, NullLogger<NotificationService>.Instance);

    private ReviewSubmittedNotificationHandler CreateHandler() =>
        new(CreateNotificationService(), _hubContext.Object, NullLogger<ReviewSubmittedNotificationHandler>.Instance);

    [Fact]
    public async Task Handle_CreatesNotificationAndPushesToTenantGroup()
    {
        var tenantId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var evt = new ReviewSubmittedEvent(reviewId, tenantId, productId, 4);
        var notification = new DomainEventNotification<ReviewSubmittedEvent>(evt);

        await CreateHandler().Handle(notification, CancellationToken.None);

        _notifications.Verify(r => r.AddAsync(It.Is<Notification>(n =>
            n.TenantId == tenantId && n.RecipientUserId == null && n.Type == NotificationType.ReviewSubmitted &&
            n.EntityName == "Review" && n.EntityId == reviewId)), Times.Once);
        _hubClients.Verify(c => c.Group($"tenant:{tenantId}"), Times.Once);
        _clientProxy.Verify(
            c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
