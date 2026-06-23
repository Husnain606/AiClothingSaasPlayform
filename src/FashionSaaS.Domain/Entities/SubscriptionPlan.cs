using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class SubscriptionPlan : BaseEntity
{
    public SubscriptionPlanType PlanType { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DurationDays { get; set; }
    public int TrialDays { get; set; }
    public int ProductLimit { get; set; }
    public int UserLimit { get; set; }
    public int AiUsageLimit { get; set; }
    public long StorageLimitMb { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<TenantSubscription> TenantSubscriptions { get; set; } = new List<TenantSubscription>();
}
