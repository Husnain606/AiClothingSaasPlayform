using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Reviews.DTOs;

public class ReviewResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid CustomerId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public ReviewStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
