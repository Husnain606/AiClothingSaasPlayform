namespace FashionSaaS.Application.ProductImages.DTOs;

/// <summary>Ordered list of image ids — the position in the list becomes the SortOrder.</summary>
public class ReorderImagesRequest
{
    public IReadOnlyList<Guid> Ids { get; set; } = [];
}
