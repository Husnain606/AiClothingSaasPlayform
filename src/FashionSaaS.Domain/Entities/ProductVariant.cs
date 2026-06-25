namespace FashionSaaS.Domain.Entities;

public class ProductVariant : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? PriceOverride { get; set; }
    public bool IsActive { get; set; } = true;

    public Product? Product { get; set; }
}
