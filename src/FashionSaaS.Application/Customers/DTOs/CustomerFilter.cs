namespace FashionSaaS.Application.Customers.DTOs;

public class CustomerFilter
{
    public Guid TenantId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
