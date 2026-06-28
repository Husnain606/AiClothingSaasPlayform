namespace FashionSaaS.Application.ProductVariants.DTOs;

public class UpdateVariantRequest
{
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? PriceOverride { get; set; }
}
