using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class SubscriptionRepository(ApplicationDbContext context)
    : GenericRepository<TenantSubscription>(context), ISubscriptionRepository
{
    // Hides GenericRepository<T>.GetAllAsync(): the base implementation has no Include, so
    // s.Plan is always null and SubscriptionService.GetAllAsync silently reports an empty
    // PlanName/zero Price for every subscription.
    public new async Task<IReadOnlyList<TenantSubscription>> GetAllAsync()
        => await DbSet.AsNoTracking().Include(s => s.Plan).ToListAsync();

    public async Task<TenantSubscription?> GetActiveByTenantIdAsync(Guid tenantId)
        => await DbSet.Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active);

    public async Task<IReadOnlyList<TenantSubscription>> GetExpiredActiveAsync(DateTime asOf)
        => await DbSet.Include(s => s.Tenant)
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate < asOf)
            .ToListAsync();
}
