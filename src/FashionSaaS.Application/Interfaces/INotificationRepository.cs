using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface INotificationRepository : IGenericRepository<Notification>
{
    Task<(IReadOnlyList<Notification> Items, int Total)> GetPagedAsync(NotificationFilter filter, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid tenantId, Guid recipientUserId, CancellationToken ct = default);
}
