namespace FashionSaaS.Application.Discounts.DTOs;

public class DiscountFilter
{
    public Guid TenantId { get; set; }
    public string? Search { get; set; }   // matches Code (contains)
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
