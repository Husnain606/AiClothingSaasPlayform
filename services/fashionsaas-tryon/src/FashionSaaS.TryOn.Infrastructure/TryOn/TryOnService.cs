using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
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
    IOptions<GeminiSettings> geminiOptions,
    ITryOnEventPublisher eventPublisher,
    IUsageQuotaService usageQuotaService)
{
    private const string ResultMimeType = "image/png";

    // Mirrors TryOnController's [RequestSizeLimit(15_000_000)] on the inbound photo upload, applied here
    // to the server-side garment-image fetch so a malicious/misbehaving host can't force an unbounded download.
    private const long MaxGarmentImageBytes = 15_000_000;

    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnResultResponse? Data)> RenderAsync(
        TryOnRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

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
            using HttpResponseMessage garmentResponse = await httpClient
                .GetAsync(new Uri(form.GarmentImageUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            garmentResponse.EnsureSuccessStatusCode();

            // SSRF/DoS guard: GetByteArrayAsync has no body-size cap, so a malicious or misbehaving host
            // (even one that passed the host allowlist) could stream an unbounded response. Reject up front
            // via Content-Length when the server reports it, and enforce the same cap while reading, mirroring
            // TryOnController's [RequestSizeLimit(15_000_000)] on the inbound request.
            var declaredLength = garmentResponse.Content.Headers.ContentLength;
            if (declaredLength is > MaxGarmentImageBytes)
            {
                await RecordAsync(form, TryOnStatus.Failed, "Garment image exceeds the maximum allowed size.", cancellationToken).ConfigureAwait(false);
                return (false, 502, "We couldn't load the product image right now. Please try again.", null);
            }

            await using Stream garmentStream = await garmentResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream garmentMemory = new();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await garmentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxGarmentImageBytes)
                {
                    await RecordAsync(form, TryOnStatus.Failed, "Garment image exceeds the maximum allowed size.", cancellationToken).ConfigureAwait(false);
                    return (false, 502, "We couldn't load the product image right now. Please try again.", null);
                }

                await garmentMemory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            garmentBytes = garmentMemory.ToArray();
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

        TryOnRequest saved = await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken).ConfigureAwait(false);
        await eventPublisher.PublishAsync(
            new TryOnCompletedEvent(saved.Id, saved.TenantId, saved.CustomerId, saved.ProductId, saved.CreatedAt),
            cancellationToken).ConfigureAwait(false);

        var dataUri = $"data:{resultPart.InlineData.MimeType};base64,{resultPart.InlineData.Data}";
        return (true, 200, "Success", new TryOnResultResponse(dataUri));
    }

    private async Task<TryOnRequest> RecordAsync(TryOnRequestForm form, TryOnStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        TryOnRequest entity = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = status,
            FailureReason = failureReason
        };
        dbContext.TryOnRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }
}
