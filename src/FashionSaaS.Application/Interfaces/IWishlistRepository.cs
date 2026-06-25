using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IWishlistRepository : IGenericRepository<Wishlist>
{
    Task<Wishlist?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
}
