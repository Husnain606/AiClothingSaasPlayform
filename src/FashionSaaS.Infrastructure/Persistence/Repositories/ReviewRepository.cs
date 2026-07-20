using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class ReviewRepository(ApplicationDbContext context)
    : GenericRepository<Review>(context), IReviewRepository
{
    public async Task<(IReadOnlyList<Review> Items, int Total)> GetPagedAsync(ReviewFilter filter, CancellationToken ct = default)
    {
        IQueryable<Review> query = DbSet
            .AsNoTracking()
            .AsQueryable()
            .Where(r => r.TenantId == filter.TenantId);

        if (filter.ProductId.HasValue)
            query = query.Where(r => r.ProductId == filter.ProductId.Value);

        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);

        var total = await query.CountAsync(ct);

        List<Review> items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<bool> ExistsByCustomerAndProductAsync(Guid tenantId, Guid customerId, Guid productId, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            r => r.TenantId == tenantId && r.CustomerId == customerId && r.ProductId == productId,
            ct);
}
