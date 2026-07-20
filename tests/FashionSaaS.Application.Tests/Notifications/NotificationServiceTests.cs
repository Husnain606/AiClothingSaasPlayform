using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Infrastructure.Persistence;
using FashionSaaS.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.Application.Tests.Notifications;

/// <summary>
/// NotificationService tests run against the REAL NotificationRepository over an EF Core
/// in-memory ApplicationDbContext (mirrors ReportServiceTests) because the value under test
/// is the tenant/recipient scoping performed in the repository query, not just service
/// branching. Only ICurrentTenantService and IUnitOfWork's IPublisher dependency are mocked.
/// </summary>
public class NotificationServiceTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    // The ApplicationDbContext's global tenant query filter closes over the ICurrentTenantService
    // instance it was constructed with (ApplicationDbContext.cs:91-92) — that instance, not the
    // one NotificationService is separately given, governs every read. Mirrors ReportServiceTests'
    // CreateContext(tenantId, dbName) so the filter is bound to a real tenant per context instance.
    private static ApplicationDbContext CreateContext(Guid tenantId, string dbName)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options, currentTenant.Object);
    }

    private static NotificationService CreateService(ApplicationDbContext ctx, Guid? tenantId)
    {
        var currentTenant = new Mock<ICurrentTenantService>();
        currentTenant.Setup(c => c.TenantId).Returns(tenantId);
        var publisher = new Mock<IPublisher>();
        // CA2000 suppressed: UnitOfWork.Dispose() only disposes the shared `ctx`, which the
        // test already disposes via its own `using` — a separate Dispose() call on this
        // wrapper would be a redundant no-op, not a real resource leak (mirrors
        // OrderWorkflowE2ETests' identical suppression).
#pragma warning disable CA2000
        var unitOfWork = new UnitOfWork(ctx, publisher.Object);
#pragma warning restore CA2000
        return new NotificationService(new NotificationRepository(ctx), unitOfWork, currentTenant.Object,
            NullLogger<NotificationService>.Instance);
    }

    private static Notification MakeNotification(Guid tenantId, Guid? recipientUserId, bool isRead = false) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        RecipientUserId = recipientUserId,
        Type = NotificationType.OrderPlaced,
        Title = "Title",
        Message = "Message",
        EntityName = "Order",
        EntityId = Guid.NewGuid(),
        IsRead = isRead,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task CreateAsync_PersistsNotificationRow()
    {
        using ApplicationDbContext ctx = CreateContext(_tenantA, Guid.NewGuid().ToString());
        NotificationService service = CreateService(ctx, _tenantA);
        var entityId = Guid.NewGuid();

        Notification created = await service.CreateAsync(_tenantA, _userA, NotificationType.OrderPlaced,
            "New order", "Order placed.", "Order", entityId);

        created.Id.Should().NotBeEmpty();
        ctx.Notifications.Should().ContainSingle(n => n.Id == created.Id
            && n.TenantId == _tenantA
            && n.RecipientUserId == _userA
            && n.Type == NotificationType.OrderPlaced
            && n.EntityId == entityId
            && !n.IsRead);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        // Writes bypass the global query filter, so both tenants' rows can be seeded through a
        // single tenantA-bound context — only reads are scoped by the filter.
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        ctx.Notifications.AddRange(
            MakeNotification(_tenantA, null),
            MakeNotification(_tenantB, null));
        await ctx.SaveChangesAsync();

        NotificationService service = CreateService(ctx, _tenantA);
        // Filter's own TenantId is attacker/client-controlled input — service must override it
        // with the resolved current-tenant value, never trust the inbound filter. The global
        // query filter (bound to this context's tenantA) is the second, independent line of
        // defense against a spoofed value.
        var filter = new NotificationFilter { TenantId = _tenantB, RecipientUserId = _userA, Page = 1, PageSize = 20 };

        ResponseData<PagedResult<NotificationResponse>> result = await service.GetPagedAsync(filter);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByRecipientOrBroadcast()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        Notification broadcast = MakeNotification(_tenantA, null);
        Notification forUserA = MakeNotification(_tenantA, _userA);
        Notification forUserB = MakeNotification(_tenantA, _userB);
        ctx.Notifications.AddRange(broadcast, forUserA, forUserB);
        await ctx.SaveChangesAsync();

        NotificationService service = CreateService(ctx, _tenantA);
        var filter = new NotificationFilter { RecipientUserId = _userA, Page = 1, PageSize = 20 };

        ResponseData<PagedResult<NotificationResponse>> result = await service.GetPagedAsync(filter);

        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Select(n => n.Id).Should().BeEquivalentTo([broadcast.Id, forUserA.Id]);
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyUnread()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        ctx.Notifications.AddRange(
            MakeNotification(_tenantA, _userA, isRead: false),
            MakeNotification(_tenantA, _userA, isRead: true),
            MakeNotification(_tenantA, null, isRead: false));
        await ctx.SaveChangesAsync();

        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<int> result = await service.GetUnreadCountAsync(_userA);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(2);
    }

    [Fact]
    public async Task MarkReadAsync_SetsIsReadAndReadAt()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        Notification notification = MakeNotification(_tenantA, _userA);
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<bool> result = await service.MarkReadAsync(notification.Id, _userA);

        result.IsSuccess.Should().BeTrue();
        Notification? updated = await ctx.Notifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeTrue();
        updated.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkReadAsync_NotFound_ReturnsFailure404()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<bool> result = await service.MarkReadAsync(Guid.NewGuid(), _userA);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksTargetedRowAndRecordsPerUserBroadcastReceipt()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        Notification broadcast = MakeNotification(_tenantA, null);
        Notification forUserA = MakeNotification(_tenantA, _userA);
        Notification forUserB = MakeNotification(_tenantA, _userB);
        ctx.Notifications.AddRange(broadcast, forUserA, forUserB);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<bool> result = await service.MarkAllReadAsync(_userA);

        result.IsSuccess.Should().BeTrue();
        // The broadcast row is shared across every recipient — it must stay untouched. Only a
        // per-user NotificationRead receipt records that userA (specifically) has read it, so
        // userB (who never marked anything read) is unaffected.
        (await ctx.Notifications.FindAsync(broadcast.Id))!.IsRead.Should().BeFalse();
        ctx.NotificationReads.Should().ContainSingle(r => r.NotificationId == broadcast.Id && r.UserId == _userA);
        (await ctx.Notifications.FindAsync(forUserA.Id))!.IsRead.Should().BeTrue();
        (await ctx.Notifications.FindAsync(forUserB.Id))!.IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task MarkReadAsync_BroadcastNotification_OnlyMarksReadForRequestingUser()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        Notification broadcast = MakeNotification(_tenantA, null);
        ctx.Notifications.Add(broadcast);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<bool> result = await service.MarkReadAsync(broadcast.Id, _userA);

        result.IsSuccess.Should().BeTrue();
        // The shared row itself must stay unread — admin B, who never marked it read, must still
        // see it as unread in both the list and the unread count.
        (await ctx.Notifications.FindAsync(broadcast.Id))!.IsRead.Should().BeFalse();

        var filterForUserB = new NotificationFilter { RecipientUserId = _userB, Page = 1, PageSize = 20 };
        ResponseData<PagedResult<NotificationResponse>> pageForUserB = await service.GetPagedAsync(filterForUserB);
        pageForUserB.Data!.Items.Should().ContainSingle(n => n.Id == broadcast.Id && !n.IsRead);

        ResponseData<int> unreadCountForUserB = await service.GetUnreadCountAsync(_userB);
        unreadCountForUserB.Data.Should().Be(1);

        var filterForUserA = new NotificationFilter { RecipientUserId = _userA, Page = 1, PageSize = 20 };
        ResponseData<PagedResult<NotificationResponse>> pageForUserA = await service.GetPagedAsync(filterForUserA);
        pageForUserA.Data!.Items.Should().ContainSingle(n => n.Id == broadcast.Id && n.IsRead);

        ResponseData<int> unreadCountForUserA = await service.GetUnreadCountAsync(_userA);
        unreadCountForUserA.Data.Should().Be(0);
    }

    [Fact]
    public async Task MarkReadAsync_BroadcastNotification_CalledTwice_IsIdempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        using ApplicationDbContext ctx = CreateContext(_tenantA, dbName);
        Notification broadcast = MakeNotification(_tenantA, null);
        ctx.Notifications.Add(broadcast);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        NotificationService service = CreateService(ctx, _tenantA);

        ResponseData<bool> first = await service.MarkReadAsync(broadcast.Id, _userA);
        ResponseData<bool> second = await service.MarkReadAsync(broadcast.Id, _userA);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        ctx.NotificationReads.Should().ContainSingle(r => r.NotificationId == broadcast.Id && r.UserId == _userA);
    }
}
