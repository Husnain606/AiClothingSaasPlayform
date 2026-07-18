using FashionSaaS.TryOn.Domain;

namespace FashionSaaS.TryOn.Application.Measurement;

public record MeasurementResultResponse(
    decimal ChestCm,
    decimal WaistCm,
    decimal HipsCm,
    decimal ShoulderWidthCm,
    decimal InseamCm,
    SizeCode RecommendedSize,
    decimal Confidence);
