namespace FashionSaaS.Application.Wishlists.DTOs;

public class AddWishlistItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
}
