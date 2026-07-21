using System.Security.Claims;
using FashionSaaS.API.Hubs;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace FashionSaaS.Infrastructure.Tests.Hubs;

public class NotificationsHubTests
{
    private static (NotificationsHub Hub, Mock<IGroupManager> Groups) CreateHub(
        Guid? tenantId, Guid? userId, params string[] roles)
    {
        var claims = new List<Claim>();
        if (tenantId is { } tid)
            claims.Add(new Claim("tenant_id", tid.ToString()));
        if (userId is { } uid)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, uid.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.User).Returns(principal);
        context.Setup(c => c.ConnectionId).Returns("connection-1");
        context.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hub = new NotificationsHub
        {
            Context = context.Object,
            Groups = groups.Object
        };
        return (hub, groups);
    }

    [Fact]
    public async Task OnConnectedAsync_CustomerRole_DoesNotJoinTenantGroup()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        (NotificationsHub hub, Mock<IGroupManager> groups) = CreateHub(tenantId, userId, nameof(RoleType.Customer));

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"tenant:{tenantId}", It.IsAny<CancellationToken>()),
            Times.Never);
        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"user:{userId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_StaffRole_JoinsBothTenantAndUserGroups()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        (NotificationsHub hub, Mock<IGroupManager> groups) = CreateHub(tenantId, userId, nameof(RoleType.OrderManager));

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"tenant:{tenantId}", It.IsAny<CancellationToken>()),
            Times.Once);
        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"user:{userId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_NoRoles_JoinsNeitherTenantGroup_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        (NotificationsHub hub, Mock<IGroupManager> groups) = CreateHub(tenantId, userId);

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"tenant:{tenantId}", It.IsAny<CancellationToken>()),
            Times.Never);
        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"user:{userId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_SuperAdmin_TenantLess_JoinsOnlyUserGroup()
    {
        var userId = Guid.NewGuid();
        (NotificationsHub hub, Mock<IGroupManager> groups) = CreateHub(tenantId: null, userId, nameof(RoleType.SuperAdmin));

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.Is<string>(name => name.StartsWith("tenant:", StringComparison.Ordinal)), It.IsAny<CancellationToken>()),
            Times.Never);
        groups.Verify(
            g => g.AddToGroupAsync("connection-1", $"user:{userId}", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
