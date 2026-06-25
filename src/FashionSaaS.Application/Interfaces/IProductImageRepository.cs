using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IProductImageRepository : IGenericRepository<ProductImage>
{
    Task<IReadOnlyList<ProductImage>> GetByProductAsync(Guid productId, CancellationToken ct = default);
    Task<ProductImage?> GetPrimaryAsync(Guid productId, CancellationToken ct = default);
}
