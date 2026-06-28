using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Discounts.DTOs;

public class CreateDiscountRequest
{
    public string Code { get; set; } = string.Empty;
    public DiscountType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public int? MaxRedemptions { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
}
