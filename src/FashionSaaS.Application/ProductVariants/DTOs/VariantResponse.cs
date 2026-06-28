namespace FashionSaaS.Application.ProductVariants.DTOs;

public class VariantResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? PriceOverride { get; set; }

    /// <summary>Effective sale price: the variant override when set, otherwise the owning product's base price.</summary>
    public decimal EffectivePrice { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
