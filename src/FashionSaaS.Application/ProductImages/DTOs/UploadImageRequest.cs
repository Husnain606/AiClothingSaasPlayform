namespace FashionSaaS.Application.ProductImages.DTOs;

/// <summary>
/// Metadata for a product image upload. The binary content is supplied separately as a
/// (Stream content, string fileName) pair by the controller (Task 18), which derives them
/// from the uploaded IFormFile; content-type/size validation is enforced at that boundary.
/// </summary>
public class UploadImageRequest
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public string? AltText { get; set; }
}
