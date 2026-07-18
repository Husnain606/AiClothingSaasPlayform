namespace FashionSaaS.TryOn.Domain;

public class ChatRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public ChatRequestStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public int MessageLength { get; set; }
    public int ReplyLength { get; set; }
    public bool HadProductContext { get; set; }
}
