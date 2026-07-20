using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class NotificationRepository(ApplicationDbContext context)
    : GenericRepository<Notification>(context), INotificationRepository
{
    public async Task<(IReadOnlyList<Notification> Items, int Total)> GetPagedAsync(NotificationFilter filter, CancellationToken ct = default)
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

        return (items, total);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .CountAsync(n => n.TenantId == tenantId
                && (n.RecipientUserId == null || n.RecipientUserId == recipientUserId)
                && !n.IsRead, ct);

    public async Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default)
    {
        List<Notification> unread = await DbSet
            .Where(n => n.TenantId == tenantId
                && (n.RecipientUserId == null || n.RecipientUserId == recipientUserId)
                && !n.IsRead)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        foreach (Notification notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }
    }
}
