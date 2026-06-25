namespace FashionSaaS.Application.Subscriptions.DTOs;

public class AssignSubscriptionRequest
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public DateTime StartDate { get; set; }
}
