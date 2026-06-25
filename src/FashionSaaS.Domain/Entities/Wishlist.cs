namespace FashionSaaS.Domain.Entities;

public class Wishlist : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }

    public ICollection<WishlistItem> Items { get; set; } = new List<WishlistItem>();
}
