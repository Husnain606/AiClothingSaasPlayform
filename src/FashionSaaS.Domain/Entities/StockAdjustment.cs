using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class StockAdjustment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductVariantId { get; set; }
    public int Delta { get; set; }
    public StockAdjustmentReason Reason { get; set; }
    public int ResultingQuantity { get; set; }
    public Guid AdjustedByUserId { get; set; }

    public ProductVariant? ProductVariant { get; set; }
}
