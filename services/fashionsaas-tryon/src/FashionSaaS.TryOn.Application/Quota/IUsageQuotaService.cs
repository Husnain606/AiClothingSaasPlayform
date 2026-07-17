namespace FashionSaaS.TryOn.Application.Quota;

/// <summary>
/// The single combined ai_usage_limit pool spanning try-on, measurement, and chat (design spec §9)
/// — one number per tenant (Phase 1's SubscriptionPlan.AiUsageLimit, read via the ai_usage_limit
/// JWT claim), consumed by three independent feature tables.
/// </summary>
public interface IUsageQuotaService
{
    Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken cancellationToken);
}
