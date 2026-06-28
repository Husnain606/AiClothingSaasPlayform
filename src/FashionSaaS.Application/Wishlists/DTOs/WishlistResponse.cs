namespace FashionSaaS.Application.Wishlists.DTOs;

public class WishlistResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public IReadOnlyList<WishlistItemResponse> Items { get; set; } = new List<WishlistItemResponse>();
}
