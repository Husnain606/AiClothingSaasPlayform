using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class ProductRepository(ApplicationDbContext context)
    : GenericRepository<Product>(context), IProductRepository
{
    public async Task<bool> SlugExistsAsync(Guid tenantId, string slug, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            p => p.TenantId == tenantId && p.Slug == slug && (excludeId == null || p.Id != excludeId),
            ct);

    public async Task<Product?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product?> GetBySlugWithDetailsAsync(Guid tenantId, string slug, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Slug == slug, ct);

    public async Task<(IReadOnlyList<Product> Items, int Total)> GetPagedAsync(ProductFilter filter, CancellationToken ct = default)
    {
        var query = DbSet
            .AsNoTracking()
            .AsQueryable()
            .Where(p => p.TenantId == filter.TenantId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(p => p.Name.Contains(filter.Search) || (p.Tags != null && p.Tags.Contains(filter.Search)));

        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Name)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
