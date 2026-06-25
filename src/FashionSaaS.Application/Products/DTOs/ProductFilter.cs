using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Products.DTOs;

public class ProductFilter
{
    public Guid TenantId { get; set; }
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public ProductStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
