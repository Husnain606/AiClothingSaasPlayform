using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid? TenantId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
