using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// This type lives in Infrastructure (not Application, as an earlier plan draft suggested) because
// it depends on the concrete TryOnDbContext, which lives in Infrastructure.Persistence. Infrastructure
// already references Application (for ICurrentTryOnContext/JwtSettings, from Phase 2) — an Application
// -> Infrastructure reference here would be circular (confirmed: MSBuild error MSB4006 when attempted).
// Placing the orchestration service here, alongside its DbContext dependency, keeps the layering acyclic
// while still depending only on Application abstractions (ICurrentTryOnContext, IGeminiImageClient) for
// everything else — 2026-07-17.
namespace FashionSaaS.TryOn.Infrastructure.TryOn;

public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiImageClient geminiClient,
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiSettings> geminiOptions)
{
    private const string ResultMimeType = "image/png";

    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnResultResponse? Data)> RenderAsync(
        TryOnRequestForm form, CancellationToken cancellationToken)
    {
        DateTime startOfMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usedThisMonth = await dbContext.TryOnRequests
            .Where(t => t.TenantId == currentContext.TenantId
                        && t.Status == TryOnStatus.Completed
                        && t.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordAsync(form, TryOnStatus.Failed, "Monthly AI try-on quota exceeded.", cancellationToken).ConfigureAwait(false);
            return (false, 429, "You've reached this month's try-on limit. Upgrade your plan or try again next month.", null);
        }

        byte[] photoBytes;
        await using (Stream stream = form.Photo.OpenReadStream())
        await using (MemoryStream memory = new())
        {
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            photoBytes = memory.ToArray();
        }

        byte[] garmentBytes;
        try
        {
            using HttpClient httpClient = httpClientFactory.CreateClient();
            garmentBytes = await httpClient.GetByteArrayAsync(new Uri(form.GarmentImageUrl), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            await RecordAsync(form, TryOnStatus.Failed, $"Could not fetch garment image: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return (false, 502, "We couldn't load the product image right now. Please try again.", null);
        }

        GeminiGenerateContentResponse response;
        try
        {
            GeminiGenerateContentRequest request = new(
                Contents:
                [
                    new GeminiContent(
                    [
                        new GeminiPart(InlineData: new GeminiInlineData("image/jpeg", Convert.ToBase64String(photoBytes))),
                        new GeminiPart(InlineData: new GeminiInlineData(ResultMimeType, Convert.ToBase64String(garmentBytes))),
                        new GeminiPart(Text: "Composite the second image (a clothing item) onto the person in the first image, keeping their pose and background. Return only the resulting image.")
                    ])
                ],
                GenerationConfig: new GeminiGenerationConfig(["IMAGE"]));

            response = await geminiClient.GenerateContentAsync(_gemini.Model, _gemini.ApiKey, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAsync(form, TryOnStatus.Failed, $"Gemini API error: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return (false, 502, "The try-on render failed. Please try again in a moment.", null);
        }

        GeminiPart? resultPart = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => p.InlineData is not null);

        if (resultPart?.InlineData is null)
        {
            await RecordAsync(form, TryOnStatus.Failed, "Gemini returned no image.", cancellationToken).ConfigureAwait(false);
            return (false, 502, "The try-on render failed. Please try again in a moment.", null);
        }

        await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken).ConfigureAwait(false);

        var dataUri = $"data:{resultPart.InlineData.MimeType};base64,{resultPart.InlineData.Data}";
        return (true, 200, "Success", new TryOnResultResponse(dataUri));
    }

    private async Task RecordAsync(TryOnRequestForm form, TryOnStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        dbContext.TryOnRequests.Add(new TryOnRequest
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = status,
            FailureReason = failureReason
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
