using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class StockAdjustmentRepository(ApplicationDbContext context)
    : GenericRepository<StockAdjustment>(context), IStockAdjustmentRepository
{
    public async Task<IReadOnlyList<StockAdjustment>> GetByVariantAsync(Guid variantId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(s => s.ProductVariantId == variantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
}
