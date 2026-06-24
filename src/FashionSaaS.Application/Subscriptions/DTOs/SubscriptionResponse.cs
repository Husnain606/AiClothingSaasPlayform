using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Subscriptions.DTOs;

public class SubscriptionResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal Price { get; set; }
}
