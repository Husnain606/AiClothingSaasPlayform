namespace FashionSaaS.TryOn.Domain;

public class TryOnRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public TryOnStatus Status { get; set; }
    public string? FailureReason { get; set; }
}
