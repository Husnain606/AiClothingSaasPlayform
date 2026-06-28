using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Inventory.DTOs;

public class StockAdjustmentResponse
{
    public Guid Id { get; set; }
    public Guid ProductVariantId { get; set; }
    public int Delta { get; set; }
    public StockAdjustmentReason Reason { get; set; }
    public int ResultingQuantity { get; set; }
    public Guid AdjustedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
