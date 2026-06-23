using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class SubscriptionRepository(ApplicationDbContext context)
    : GenericRepository<TenantSubscription>(context), ISubscriptionRepository
{
    public async Task<TenantSubscription?> GetActiveByTenantIdAsync(Guid tenantId)
        => await DbSet.Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status == SubscriptionStatus.Active);

    public async Task<IReadOnlyList<TenantSubscription>> GetExpiredActiveAsync(DateTime asOf)
        => await DbSet.Include(s => s.Tenant)
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndDate < asOf)
            .ToListAsync();
}
