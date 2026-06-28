using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class CategoryRepository(ApplicationDbContext context)
    : GenericRepository<Category>(context), ICategoryRepository
{
    public async Task<bool> SlugExistsAsync(Guid tenantId, string slug, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            c => c.TenantId == tenantId && c.Slug == slug && (excludeId == null || c.Id != excludeId),
            ct);

    public async Task<IReadOnlyList<Category>> GetTreeAsync(Guid tenantId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.ParentCategoryId)
            .ThenBy(c => c.SortOrder)
            .ToListAsync(ct);
}
