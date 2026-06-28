namespace FashionSaaS.Application.Wishlists.DTOs;

public class WishlistItemResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }

    // Product summary (resolved from the catalog).
    public string? ProductName { get; set; }
    public string? ProductSlug { get; set; }
    public decimal? ProductBasePrice { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
