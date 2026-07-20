using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Notifications;

/// <summary>
/// Persists <see cref="Notification"/> rows and serves the tenant-scoped REST surface
/// (<c>GET/PUT api/tenant/notifications/...</c>). <see cref="CreateAsync"/> is also called
/// directly by the Group D MediatR event handlers — those run AFTER the triggering write
/// already committed, so this does its own <see cref="IUnitOfWork.SaveChangesAsync"/> rather
/// than composing into the caller's transaction. Reads are tenant-scoped via the global EF
/// query filter (fail-closed) plus an explicit recipient check here, since the filter alone
/// allows every tenant recipient to see broadcast (<c>RecipientUserId == null</c>) rows.
/// </summary>
public class NotificationService(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenantService currentTenant,
    ILogger<NotificationService> logger)
{
    public async Task<Notification> CreateAsync(Guid? tenantId, Guid? recipientUserId, NotificationType type,
        string title, string message, string entityName, Guid entityId, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            EntityName = entityName,
            EntityId = entityId
        };

        await notificationRepository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Notification {Type} created for tenant {TenantId}", type, tenantId);
        return notification;
    }

    public async Task<ResponseData<PagedResult<NotificationResponse>>> GetPagedAsync(NotificationFilter filter,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<NotificationResponse>>.Failure("Tenant could not be resolved.", 400);

        filter.TenantId = tenantId;
        (IReadOnlyList<Notification> items, var total, IReadOnlySet<Guid> readBroadcastIds) =
            await notificationRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<NotificationResponse>
        {
            Items = items.Select(n => MapToResponse(n, readBroadcastIds)).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<NotificationResponse>>.Success(page);
    }

    public async Task<ResponseData<int>> GetUnreadCountAsync(Guid recipientUserId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<int>.Failure("Tenant could not be resolved.", 400);

        var count = await notificationRepository.GetUnreadCountAsync(tenantId, recipientUserId, ct);
        return ResponseData<int>.Success(count);
    }

    public async Task<ResponseData<bool>> MarkReadAsync(Guid id, Guid recipientUserId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        // GetByIdAsync goes through DbContext.Find, which still applies the global tenant
        // query filter — but the filter alone permits every recipient in the tenant to load a
        // broadcast row, so ownership of a recipient-targeted row must be checked explicitly.
        Notification? notification = await notificationRepository.GetByIdAsync(id);
        if (notification is null || notification.TenantId != tenantId)
            return ResponseData<bool>.Failure("Notification not found.", 404);

        if (notification.RecipientUserId is { } owner)
        {
            if (owner != recipientUserId)
                return ResponseData<bool>.Failure("Notification not found.", 404);

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await notificationRepository.UpdateAsync(notification);
        }
        else
        {
            // Broadcast notification (no single recipient) — record a per-user read receipt
            // instead of mutating the shared row; otherwise one admin marking it read would hide
            // it from every other admin in the tenant who hasn't seen it yet.
            await notificationRepository.MarkBroadcastReadAsync(notification.Id, recipientUserId, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return ResponseData<bool>.Success(true, "Notification marked read.");
    }

    public async Task<ResponseData<bool>> MarkAllReadAsync(Guid recipientUserId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        await notificationRepository.MarkAllReadAsync(tenantId, recipientUserId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return ResponseData<bool>.Success(true, "All notifications marked read.");
    }

    // IsRead reflects "read BY THE REQUESTING USER", not the raw entity field: a broadcast row's
    // own IsRead is shared across every recipient and is meaningless per-user, so it's read from
    // the caller-supplied per-page read-receipt set instead. A targeted row has exactly one
    // recipient, so its own IsRead is already correct.
    private static NotificationResponse MapToResponse(Notification n, IReadOnlySet<Guid> readBroadcastIds) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Message = n.Message,
        EntityName = n.EntityName,
        EntityId = n.EntityId,
        IsRead = n.RecipientUserId is null ? readBroadcastIds.Contains(n.Id) : n.IsRead,
        CreatedAt = n.CreatedAt
    };
}
