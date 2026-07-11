using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Events;
using FashionSaaS.Infrastructure.EventHandlers;
using FashionSaaS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.EventHandlers;

public class SuperAdminLoginFromNewIpEventHandlerTests
{
    private readonly Mock<IEmailService> _emailService = new();
    private readonly Mock<IAuditLogService> _auditLogService = new();

    private SuperAdminLoginFromNewIpEventHandler CreateHandler() =>
        new(_emailService.Object, _auditLogService.Object,
            NullLogger<SuperAdminLoginFromNewIpEventHandler>.Instance);

    [Fact]
    public async Task Handle_RaisedEvent_CallsSendSecurityAlertAsyncOnce()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var evt = new SuperAdminLoginFromNewIpEvent(userId, "superadmin@system.com", "10.0.0.1", DateTime.UtcNow);
        var notification = new DomainEventNotification<SuperAdminLoginFromNewIpEvent>(evt);

        _emailService
            .Setup(e => e.SendSecurityAlertAsync(evt.Email, evt.NewIpAddress, evt.OccurredAt))
            .Returns(Task.CompletedTask);
        _auditLogService
            .Setup(a => a.LogAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        SuperAdminLoginFromNewIpEventHandler handler = CreateHandler();

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert: security alert email sent exactly once with correct args
        _emailService.Verify(
            e => e.SendSecurityAlertAsync(evt.Email, evt.NewIpAddress, evt.OccurredAt),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RaisedEvent_CallsAuditLogAsyncOnce()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var evt = new SuperAdminLoginFromNewIpEvent(userId, "superadmin@system.com", "10.0.0.1", DateTime.UtcNow);
        var notification = new DomainEventNotification<SuperAdminLoginFromNewIpEvent>(evt);

        _emailService
            .Setup(e => e.SendSecurityAlertAsync(evt.Email, evt.NewIpAddress, evt.OccurredAt))
            .Returns(Task.CompletedTask);
        _auditLogService
            .Setup(a => a.LogAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<object?>(),
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        SuperAdminLoginFromNewIpEventHandler handler = CreateHandler();

        // Act
        await handler.Handle(notification, CancellationToken.None);

        // Assert: audit log written exactly once with action=SuperAdminLoginFromNewIp
        _auditLogService.Verify(
            a => a.LogAsync(
                evt.UserId,
                null,
                "SuperAdminLoginFromNewIp",
                "User",
                evt.UserId,
                null,
                It.IsAny<object?>(),
                evt.NewIpAddress,
                "System"),
            Times.Once);
    }
}
