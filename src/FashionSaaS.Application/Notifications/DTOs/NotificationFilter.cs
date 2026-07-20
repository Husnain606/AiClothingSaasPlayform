namespace FashionSaaS.Application.Notifications.DTOs;

public class NotificationFilter
{
    public Guid TenantId { get; set; }
    public Guid RecipientUserId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
