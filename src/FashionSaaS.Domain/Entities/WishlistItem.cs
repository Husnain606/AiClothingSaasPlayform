namespace FashionSaaS.Domain.Entities;

public class WishlistItem : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid WishlistId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }

    public Wishlist? Wishlist { get; set; }
}
