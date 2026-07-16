using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiGenerateContentRequest(
    [property: JsonPropertyName("contents")] GeminiContent[] Contents,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

public record GeminiContent(
    [property: JsonPropertyName("parts")] GeminiPart[] Parts);

public record GeminiPart(
    [property: JsonPropertyName("inlineData")] GeminiInlineData? InlineData = null,
    [property: JsonPropertyName("text")] string? Text = null);

public record GeminiInlineData(
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("data")] string Data);

public record GeminiGenerationConfig(
    [property: JsonPropertyName("responseModalities")] string[] ResponseModalities);

public record GeminiGenerateContentResponse(
    [property: JsonPropertyName("candidates")] GeminiCandidate[]? Candidates);

public record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content);
