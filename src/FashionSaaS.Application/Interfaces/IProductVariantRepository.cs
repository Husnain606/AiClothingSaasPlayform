using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IProductVariantRepository : IGenericRepository<ProductVariant>
{
    Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<ProductVariant>> GetByProductAsync(Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductVariant>> GetLowStockAsync(Guid tenantId, int threshold, CancellationToken ct = default);
}
