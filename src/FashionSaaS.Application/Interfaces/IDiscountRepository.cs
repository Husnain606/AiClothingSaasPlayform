using FashionSaaS.Application.Discounts.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IDiscountRepository : IGenericRepository<Discount>
{
    Task<Discount?> GetByCodeAsync(Guid tenantId, string code, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(Guid tenantId, string code, Guid? excludeId = null, CancellationToken ct = default);
    Task<(IReadOnlyList<Discount> Items, int Total)> GetPagedAsync(DiscountFilter filter, CancellationToken ct = default);
}
