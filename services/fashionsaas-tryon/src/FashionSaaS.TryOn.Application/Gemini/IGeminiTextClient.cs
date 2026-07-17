using Refit;

namespace FashionSaaS.TryOn.Application.Gemini;

public interface IGeminiTextClient
{
    [Post("/v1beta/models/{model}:generateContent")]
    Task<GeminiTextGenerateContentResponse> GenerateContentAsync(
        string model,
        [Header("x-goog-api-key")] string apiKey,
        [Body] GeminiTextGenerateContentRequest request,
        CancellationToken cancellationToken);
}
