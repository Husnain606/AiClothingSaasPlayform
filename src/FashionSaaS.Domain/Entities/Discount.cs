using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Discount : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}
