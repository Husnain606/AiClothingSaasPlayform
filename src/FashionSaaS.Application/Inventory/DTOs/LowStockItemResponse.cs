namespace FashionSaaS.Application.Inventory.DTOs;

public class LowStockItemResponse
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
}
