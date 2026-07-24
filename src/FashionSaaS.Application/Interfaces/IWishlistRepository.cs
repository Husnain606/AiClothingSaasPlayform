using FashionSaaS.Domain.Entities;

namespace FashionSaaS.Application.Interfaces;

public interface IWishlistRepository : IGenericRepository<Wishlist>
{
    Task<Wishlist?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);

    /// <summary>Tracked single wishlist item for admin removal (load-then-delete).</summary>
    Task<WishlistItem?> GetItemAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Removes a wishlist item from the change tracker (committed by the unit of work).</summary>
    Task RemoveItemAsync(WishlistItem item);

    /// <summary>
    /// Explicitly tracks a brand-new WishlistItem as Added. Assigning it only via
    /// wishlist.Items.Add(item) on an already-tracked Wishlist would risk the same
    /// client-generated-key tracking ambiguity fixed in MfaService - see
    /// IUserRepository.AddMfaSettingsAsync remarks.
    /// </summary>
    Task AddItemAsync(WishlistItem item);
}
