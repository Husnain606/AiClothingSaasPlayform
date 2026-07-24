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

    public async Task<WishlistItem?> GetItemAsync(Guid itemId, CancellationToken ct = default)
        => await Context.Set<WishlistItem>().FirstOrDefaultAsync(i => i.Id == itemId, ct);

    public Task RemoveItemAsync(WishlistItem item)
    {
        Context.Set<WishlistItem>().Remove(item);
        return Task.CompletedTask;
    }

    public async Task AddItemAsync(WishlistItem item)
        => await Context.Set<WishlistItem>().AddAsync(item);
}
