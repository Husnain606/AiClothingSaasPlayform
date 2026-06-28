namespace FashionSaaS.Application.Products.DTOs;

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public decimal BasePrice { get; set; }
    public string? Tags { get; set; }
}
