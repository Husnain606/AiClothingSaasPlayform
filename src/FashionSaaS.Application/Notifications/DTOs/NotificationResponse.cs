using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Notifications.DTOs;

public class NotificationResponse
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
