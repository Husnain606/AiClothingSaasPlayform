using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Inventory.DTOs;

public class AdjustStockRequest
{
    public Guid VariantId { get; set; }

    /// <summary>Signed change applied to the variant's current stock (positive = add, negative = remove).</summary>
    public int Delta { get; set; }
    public StockAdjustmentReason Reason { get; set; }
}
