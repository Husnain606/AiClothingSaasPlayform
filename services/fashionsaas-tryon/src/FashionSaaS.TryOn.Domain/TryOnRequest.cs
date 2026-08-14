namespace FashionSaaS.TryOn.Domain;

public class TryOnRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public TryOnStatus Status { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// The Hugging Face job/event id this request is waiting on. Null for a request that never
    /// reached submission (e.g. rejected on quota). Deliberately RETAINED after the request goes
    /// terminal, so a completed or failed render can still be traced back to its upstream job.
    /// </summary>
    public string? ExternalJobId { get; set; }

    /// <summary>The finished image's Hugging Face-served URL. Set only when Status is Completed.</summary>
    public string? ResultImageUrl { get; set; }
}
