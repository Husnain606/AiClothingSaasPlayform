using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Subscriptions.DTOs;

public class PaymentResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public PaymentStatus Status { get; set; }
}
