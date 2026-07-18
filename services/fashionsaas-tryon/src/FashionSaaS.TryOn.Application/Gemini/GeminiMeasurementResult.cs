using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiMeasurementResult(
    [property: JsonPropertyName("chestCm")] decimal ChestCm,
    [property: JsonPropertyName("waistCm")] decimal WaistCm,
    [property: JsonPropertyName("hipsCm")] decimal HipsCm,
    [property: JsonPropertyName("shoulderWidthCm")] decimal ShoulderWidthCm,
    [property: JsonPropertyName("inseamCm")] decimal InseamCm,
    [property: JsonPropertyName("recommendedSize")] string RecommendedSize,
    [property: JsonPropertyName("confidence")] decimal Confidence);
