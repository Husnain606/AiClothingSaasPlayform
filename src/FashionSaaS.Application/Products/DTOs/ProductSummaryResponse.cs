using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Application.Products.DTOs;

/// <summary>
/// Lightweight projection for paged list views. The paged query does not eagerly
/// load navigation collections (CONVENTIONS §6), so list rows omit variant/image
/// detail; use <see cref="ProductResponse"/> from the details query for those.
/// </summary>
public class ProductSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public ProductStatus Status { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
}
