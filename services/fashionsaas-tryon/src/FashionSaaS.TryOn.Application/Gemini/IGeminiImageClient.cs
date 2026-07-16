using Refit;

namespace FashionSaaS.TryOn.Application.Gemini;

public interface IGeminiImageClient
{
    [Post("/v1beta/models/{model}:generateContent")]
    Task<GeminiGenerateContentResponse> GenerateContentAsync(
        string model,
        [Header("x-goog-api-key")] string apiKey,
        [Body] GeminiGenerateContentRequest request,
        CancellationToken cancellationToken);
}
