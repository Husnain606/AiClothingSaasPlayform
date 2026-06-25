using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Review : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid CustomerId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    public Product? Product { get; set; }
    public Customer? Customer { get; set; }
}
