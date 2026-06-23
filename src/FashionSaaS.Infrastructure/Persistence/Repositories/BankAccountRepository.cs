using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class BankAccountRepository(ApplicationDbContext context)
    : GenericRepository<BankAccount>(context), IBankAccountRepository
{
    public async Task<BankAccount?> GetByTenantIdAsync(Guid? tenantId)
        => await DbSet.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.IsActive);

    public async Task<BankAccount?> GetPlatformAccountAsync()
        => await DbSet.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == null && b.IsActive);
}
