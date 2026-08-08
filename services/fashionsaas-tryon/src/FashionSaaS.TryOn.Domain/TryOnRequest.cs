namespace FashionSaaS.TryOn.Domain;

public class TryOnRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public TryOnStatus Status { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>The Hugging Face job/event id this request is waiting on. Null once terminal.</summary>
    public string? ExternalJobId { get; set; }

    /// <summary>The finished image's Hugging Face-served URL. Set only when Status is Completed.</summary>
    public string? ResultImageUrl { get; set; }
}
