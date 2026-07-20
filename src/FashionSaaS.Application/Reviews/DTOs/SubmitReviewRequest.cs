namespace FashionSaaS.Application.Reviews.DTOs;

/// <summary>Customer-submitted review payload. Server resolves tenant/customer identity —
/// the client supplies only the product being reviewed and the review content.</summary>
public class SubmitReviewRequest
{
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
}
