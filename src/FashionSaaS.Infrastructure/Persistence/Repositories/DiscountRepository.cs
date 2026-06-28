using FashionSaaS.Application.Discounts.DTOs;
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

    public async Task<(IReadOnlyList<Discount> Items, int Total)> GetPagedAsync(DiscountFilter filter, CancellationToken ct = default)
    {
        var query = DbSet
            .AsNoTracking()
            .AsQueryable()
            .Where(d => d.TenantId == filter.TenantId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(d => d.Code.Contains(term));
        }

        if (filter.IsActive.HasValue)
            query = query.Where(d => d.IsActive == filter.IsActive.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
