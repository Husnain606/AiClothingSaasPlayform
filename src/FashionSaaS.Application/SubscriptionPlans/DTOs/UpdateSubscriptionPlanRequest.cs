namespace FashionSaaS.Application.SubscriptionPlans.DTOs;

public class UpdateSubscriptionPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DurationDays { get; set; }
    public int TrialDays { get; set; }
    public int ProductLimit { get; set; }
    public int UserLimit { get; set; }
    public int AiUsageLimit { get; set; }
    public long StorageLimitMb { get; set; }
    public bool IsActive { get; set; }
}
