using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Products.DTOs;

public class ProductResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal BasePrice { get; set; }
    public ProductStatus Status { get; set; }
    public string? Tags { get; set; }
    public int VariantCount { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public int ApprovedReviewCount { get; set; }
    public double? AverageRating { get; set; }
    public DateTime CreatedAt { get; set; }
}
