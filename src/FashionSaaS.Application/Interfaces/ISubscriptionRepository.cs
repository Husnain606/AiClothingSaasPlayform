using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ISubscriptionRepository : IGenericRepository<TenantSubscription>
{
    Task<TenantSubscription?> GetActiveByTenantIdAsync(Guid tenantId);
    Task<IReadOnlyList<TenantSubscription>> GetExpiredActiveAsync(DateTime asOf);
}
