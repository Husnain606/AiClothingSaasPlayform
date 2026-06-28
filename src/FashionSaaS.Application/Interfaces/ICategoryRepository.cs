using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, Guid? excludeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Category>> GetTreeAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);
    Task<bool> HasProductsAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);
}
