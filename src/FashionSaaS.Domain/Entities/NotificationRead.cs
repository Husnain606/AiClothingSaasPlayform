namespace FashionSaaS.Domain.Entities;

/// <summary>
/// Per-recipient read receipt for a broadcast <see cref="Notification"/> (<c>RecipientUserId ==
/// null</c>, e.g. "all tenant admins"). A broadcast row is shared across every recipient, so its
/// own <see cref="Notification.IsRead"/>/<see cref="Notification.ReadAt"/> cannot represent "read
/// by this particular user" — one row per (notification, user) pair exists once that user marks
/// it read. Targeted notifications (<c>RecipientUserId != null</c>) have exactly one recipient
/// and keep using their own <see cref="Notification.IsRead"/>/<see cref="Notification.ReadAt"/>
/// fields; no row is ever created here for a targeted notification.
/// Pure join/receipt record — no <see cref="BaseEntity"/>, mirrors <see cref="UserRole"/>.
/// </summary>
public class NotificationRead
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; }
}
