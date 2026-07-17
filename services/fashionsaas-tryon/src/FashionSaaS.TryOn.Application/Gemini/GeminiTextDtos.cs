using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiTextGenerateContentRequest(
    [property: JsonPropertyName("contents")] GeminiTextContent[] Contents,
    [property: JsonPropertyName("systemInstruction")] GeminiTextContent? SystemInstruction = null,
    [property: JsonPropertyName("generationConfig")] GeminiTextGenerationConfig? GenerationConfig = null);

public record GeminiTextContent(
    [property: JsonPropertyName("parts")] GeminiTextPart[] Parts,
    [property: JsonPropertyName("role")] string? Role = null);

public record GeminiTextPart(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("inlineData")] GeminiTextInlineData? InlineData = null);

public record GeminiTextInlineData(
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("data")] string Data);

public record GeminiTextGenerationConfig(
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens = null);

public record GeminiTextGenerateContentResponse(
    [property: JsonPropertyName("candidates")] GeminiTextCandidate[]? Candidates);

public record GeminiTextCandidate(
    [property: JsonPropertyName("content")] GeminiTextContent? Content);
