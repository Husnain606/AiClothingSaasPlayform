namespace FashionSaaS.Application.ProductVariants.DTOs;

public class UpdateVariantRequest
{
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public decimal? PriceOverride { get; set; }
}
