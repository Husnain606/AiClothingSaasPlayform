namespace FashionSaaS.Domain.Entities;

public class ProductImage : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string CloudinaryPublicId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }

    public Product? Product { get; set; }
}
