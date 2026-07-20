using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class NotificationRepository(ApplicationDbContext context)
    : GenericRepository<Notification>(context), INotificationRepository
{
    public async Task<(IReadOnlyList<Notification> Items, int Total, IReadOnlySet<Guid> ReadBroadcastIds)> GetPagedAsync(
        NotificationFilter filter, CancellationToken ct = default)
    {
        IQueryable<Notification> query = DbSet
            .AsNoTracking()
            .Where(n => n.TenantId == filter.TenantId
                && (n.RecipientUserId == null || n.RecipientUserId == filter.RecipientUserId));

        var total = await query.CountAsync(ct);

        List<Notification> items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        // Bulk lookup scoped to just this page's broadcast rows (never per-row/N+1) — a broadcast
        // notification's own IsRead is shared across recipients, so "read by this user" is
        // determined by the presence of a NotificationRead receipt instead.
        var broadcastIdsOnPage = items
            .Where(n => n.RecipientUserId == null)
            .Select(n => n.Id)
            .ToList();

        HashSet<Guid> readBroadcastIds = broadcastIdsOnPage.Count == 0
            ? []
            : (await Context.Set<NotificationRead>()
                .AsNoTracking()
                .Where(r => r.UserId == filter.RecipientUserId && broadcastIdsOnPage.Contains(r.NotificationId))
                .Select(r => r.NotificationId)
                .ToListAsync(ct))
            .ToHashSet();

        return (items, total, readBroadcastIds);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
    {
        IQueryable<Guid> readBroadcastIds = Context.Set<NotificationRead>()
            .Where(r => r.UserId == recipientUserId)
            .Select(r => r.NotificationId);

        return await DbSet
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId
                && ((n.RecipientUserId == recipientUserId && !n.IsRead)
                    || (n.RecipientUserId == null && !readBroadcastIds.Contains(n.Id))))
            .CountAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
    {
        List<Notification> unreadTargeted = await DbSet
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == recipientUserId && !n.IsRead)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        foreach (Notification notification in unreadTargeted)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        IQueryable<Guid> alreadyReadBroadcastIds = Context.Set<NotificationRead>()
            .Where(r => r.UserId == recipientUserId)
            .Select(r => r.NotificationId);

        List<Guid> unreadBroadcastIds = await DbSet
            .AsNoTracking()
            .Where(n => n.TenantId == tenantId && n.RecipientUserId == null && !alreadyReadBroadcastIds.Contains(n.Id))
            .Select(n => n.Id)
            .ToListAsync(ct);

        if (unreadBroadcastIds.Count > 0)
        {
            await Context.Set<NotificationRead>().AddRangeAsync(
                unreadBroadcastIds.Select(id => new NotificationRead
                {
                    NotificationId = id,
                    UserId = recipientUserId,
                    ReadAt = now
                }),
                ct);
        }
    }

    public async Task MarkBroadcastReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
    {
        NotificationRead? existing = await Context.Set<NotificationRead>()
            .FindAsync([notificationId, userId], ct);
        if (existing is not null)
            return;

        await Context.Set<NotificationRead>().AddAsync(new NotificationRead
        {
            NotificationId = notificationId,
            UserId = userId,
            ReadAt = DateTime.UtcNow
        }, ct);
    }
}
