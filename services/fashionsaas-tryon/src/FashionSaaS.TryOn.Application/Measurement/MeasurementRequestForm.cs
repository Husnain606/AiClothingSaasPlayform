using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Application.Measurement;

public class MeasurementRequestForm
{
    public required IFormFile Photo { get; init; }
    public decimal? HeightCm { get; init; }
}
