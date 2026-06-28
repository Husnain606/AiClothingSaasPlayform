using FashionSaaS.Application.Interfaces;
using FashionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.Infrastructure.Persistence.Repositories;

public class WishlistRepository(ApplicationDbContext context)
    : GenericRepository<Wishlist>(context), IWishlistRepository
{
    public async Task<Wishlist?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
        => await DbSet
            .AsNoTracking()
            .Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.CustomerId == customerId, ct);
}
