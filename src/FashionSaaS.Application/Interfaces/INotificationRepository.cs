using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface INotificationRepository : IGenericRepository<Notification>
{
    /// <summary>
    /// Returns the page of notifications visible to the recipient plus the subset of THOSE
    /// items' ids that are broadcast notifications (<c>RecipientUserId == null</c>) already read
    /// by <see cref="NotificationFilter.RecipientUserId"/> — callers must treat a broadcast row's
    /// own <see cref="Notification.IsRead"/> as meaningless and instead check membership in
    /// <c>ReadBroadcastIds</c> for "read by this user".
    /// </summary>
    Task<(IReadOnlyList<Notification> Items, int Total, IReadOnlySet<Guid> ReadBroadcastIds)> GetPagedAsync(
        NotificationFilter filter, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);

    /// <summary>
    /// Records that <paramref name="userId"/> has read the broadcast notification
    /// <paramref name="notificationId"/>. Idempotent — marking read twice does not throw or
    /// duplicate the row. Callers must only invoke this for a broadcast notification
    /// (<c>RecipientUserId == null</c>); a targeted notification keeps updating its own
    /// <see cref="Notification.IsRead"/>/<see cref="Notification.ReadAt"/> fields instead.
    /// </summary>
    Task MarkBroadcastReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
}
