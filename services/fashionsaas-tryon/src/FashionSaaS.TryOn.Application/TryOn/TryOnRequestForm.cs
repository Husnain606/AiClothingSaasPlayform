using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnRequestForm
{
    public required IFormFile Photo { get; init; }
    public required string GarmentImageUrl { get; init; }
    public required Guid ProductId { get; init; }
    public Guid? ProductVariantId { get; init; }
}
