using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

// Parse DTO for Gemini's raw reply: every numeric field is nullable so a missing JSON property
// surfaces as null (to be rejected) instead of silently deserializing to 0. The response
// DTO/entity keep non-nullable decimals — values are only written once validated.
public record GeminiMeasurementResult(
    [property: JsonPropertyName("chestCm")] decimal? ChestCm,
    [property: JsonPropertyName("waistCm")] decimal? WaistCm,
    [property: JsonPropertyName("hipsCm")] decimal? HipsCm,
    [property: JsonPropertyName("shoulderWidthCm")] decimal? ShoulderWidthCm,
    [property: JsonPropertyName("inseamCm")] decimal? InseamCm,
    [property: JsonPropertyName("recommendedSize")] string? RecommendedSize,
    [property: JsonPropertyName("confidence")] decimal? Confidence);
