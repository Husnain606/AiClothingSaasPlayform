using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IStockAdjustmentRepository : IGenericRepository<StockAdjustment>
{
    Task<IReadOnlyList<StockAdjustment>> GetByVariantAsync(Guid variantId, CancellationToken ct = default);
}
