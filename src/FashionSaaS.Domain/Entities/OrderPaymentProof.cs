namespace FashionSaaS.Domain.Entities;

/// <summary>
/// The customer's uploaded proof of an out-of-band payment (bank transfer, wallet, etc.).
/// Exactly one per order — enforced by a unique index on <see cref="OrderId"/>. The binary
/// lives in payment-proof storage; only the opaque <see cref="StorageKey"/> is persisted here,
/// never a URL, so the storage provider can change without touching this row.
/// </summary>
public class OrderPaymentProof : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>Opaque key understood only by the storage provider. Never a URL.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Validated against the allowlist at upload; used as the download Content-Type.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The client's filename, kept for display only — never used to build a path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
}
