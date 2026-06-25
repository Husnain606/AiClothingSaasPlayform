using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Reviews.DTOs;

public class ReviewFilter
{
    public Guid TenantId { get; set; }
    public Guid? ProductId { get; set; }
    public ReviewStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
