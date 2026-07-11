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

    // CA1311/CA1304/MA0011/CA1862 suppressed: this lambda is an EF Core LINQ expression tree
    // translated to SQL, not executed as C# — only the parameterless ToLower()/ToUpper() are
    // in EF Core's supported translatable-method set. Adding a CultureInfo/StringComparison
    // argument (the rules' suggested fix) would break SQL translation at runtime.
#pragma warning disable CA1311, CA1304, MA0011, CA1862
    public async Task<bool> SizeColorExistsAsync(Guid productId, string size, string color, Guid? excludeId = null, CancellationToken ct = default)
        => await DbSet.AnyAsync(
            v => v.ProductId == productId
                 && v.Size.ToLower() == size.ToLower()
                 && v.Color.ToLower() == color.ToLower()
                 && (excludeId == null || v.Id != excludeId),
            ct);
#pragma warning restore CA1311, CA1304, MA0011, CA1862

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
