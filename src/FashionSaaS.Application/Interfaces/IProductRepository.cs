using FashionSaaS.Application.Products.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<Product?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Product> Items, int Total)> GetPagedAsync(ProductFilter filter, CancellationToken ct = default);
}
