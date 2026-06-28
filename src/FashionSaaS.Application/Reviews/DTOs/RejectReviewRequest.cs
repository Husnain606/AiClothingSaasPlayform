namespace FashionSaaS.Application.Reviews.DTOs;

public class RejectReviewRequest
{
    /// <summary>Moderator-supplied reason for rejecting the review. Recorded in the audit log.</summary>
    public string Reason { get; set; } = string.Empty;
}
