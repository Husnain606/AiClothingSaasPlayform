using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class ProductVariantRepository(ApplicationDbContext context)
    : GenericRepository<ProductVariant>(context), IProductVariantRepository
{
    public async Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            v => v.TenantId == tenantId && v.Sku == sku && (excludeId == null || v.Id != excludeId),
            ct);

    public async Task<IReadOnlyList<ProductVariant>> GetByProductAsync(Guid productId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(v => v.ProductId == productId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductVariant>> GetLowStockAsync(Guid tenantId, int threshold, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.StockQuantity <= threshold && v.IsActive)
            .ToListAsync(ct);
}
