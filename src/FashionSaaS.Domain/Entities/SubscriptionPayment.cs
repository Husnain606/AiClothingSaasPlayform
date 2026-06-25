using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class SubscriptionPayment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public Guid? ConfirmedByAdminId { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public TenantSubscription Subscription { get; set; } = null!;
}
