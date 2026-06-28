using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class DiscountRepository(ApplicationDbContext context)
    : GenericRepository<Discount>(context), IDiscountRepository
{
    public async Task<Discount?> GetByCodeAsync(Guid tenantId, string code, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Code == code, ct);

    public async Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            d => d.TenantId == tenantId && d.Code == code && (excludeId == null || d.Id != excludeId),
            ct);

    public async Task<IReadOnlyList<Discount>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
}
