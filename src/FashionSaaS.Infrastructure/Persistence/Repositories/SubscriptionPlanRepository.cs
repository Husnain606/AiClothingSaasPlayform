using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class SubscriptionPlanRepository(ApplicationDbContext context)
    : GenericRepository<SubscriptionPlan>(context), ISubscriptionPlanRepository
{
    public async Task<IReadOnlyList<SubscriptionPlan>> GetActiveAsync()
        => await DbSet.Where(p => p.IsActive).ToListAsync();
}
