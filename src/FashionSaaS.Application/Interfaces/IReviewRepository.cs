using FashionSaaS.Application.Reviews.DTOs;
using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IReviewRepository : IGenericRepository<Review>
{
    Task<(IReadOnlyList<Review> Items, int Total)> GetPagedAsync(ReviewFilter filter, CancellationToken ct = default);

    Task<bool> ExistsByCustomerAndProductAsync(Guid tenantId, Guid customerId, Guid productId, CancellationToken ct = default);
}
