using System.Text.Json;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

// Lives in Infrastructure for the same reason as TryOnService (see the note atop TryOnService.cs):
// it depends on the concrete TryOnDbContext, and an Application -> Infrastructure reference would
// be circular.
namespace FashionSaaS.TryOn.Infrastructure.Measurement;

public class MeasurementService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiTextClient geminiClient,
    IOptions<GeminiSettings> geminiOptions,
    IUsageQuotaService usageQuotaService)
{
    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, MeasurementResultResponse? Data)> EstimateAsync(
        MeasurementRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordAsync(form, MeasurementStatus.Failed, "Monthly AI usage quota exceeded.", null, cancellationToken).ConfigureAwait(false);
            return (false, 429, "You've reached this month's AI usage limit. Upgrade your plan or try again next month.", null);
        }

        byte[] photoBytes;
        await using (Stream stream = form.Photo.OpenReadStream())
        await using (MemoryStream memory = new())
        {
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            photoBytes = memory.ToArray();
        }

        GeminiTextGenerateContentResponse response;
        try
        {
            var promptText = GeminiPrompts.MeasurementInstruction + GeminiPrompts.MeasurementHeightHint(form.HeightCm);
            GeminiTextGenerateContentRequest request = new(
                Contents:
                [
                    new GeminiTextContent(
                        Parts:
                        [
                            new GeminiTextPart(Text: promptText),
                            new GeminiTextPart(InlineData: new GeminiTextInlineData(
                                MimeType: form.Photo.ContentType,
                                Data: Convert.ToBase64String(photoBytes)))
                        ],
                        Role: "user")
                ]);
            response = await geminiClient.GenerateContentAsync(_gemini.TextModel, _gemini.ApiKey, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAsync(form, MeasurementStatus.Failed, $"Gemini API error: {ex.Message}", null, cancellationToken).ConfigureAwait(false);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        var replyText = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

        GeminiMeasurementResult? parsed = null;
        if (replyText is not null)
        {
            try
            {
                parsed = JsonSerializer.Deserialize<GeminiMeasurementResult>(replyText);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (parsed is null || !Enum.TryParse<SizeCode>(parsed.RecommendedSize, ignoreCase: true, out SizeCode recommendedSize))
        {
            await RecordAsync(form, MeasurementStatus.Failed, "Could not parse measurement response.", null, cancellationToken).ConfigureAwait(false);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        MeasurementResultResponse result = new(
            parsed.ChestCm, parsed.WaistCm, parsed.HipsCm, parsed.ShoulderWidthCm, parsed.InseamCm,
            recommendedSize, parsed.Confidence);

        await RecordAsync(form, MeasurementStatus.Completed, null, result, cancellationToken).ConfigureAwait(false);
        return (true, 200, "Success", result);
    }

    private async Task<MeasurementRequest> RecordAsync(
        MeasurementRequestForm form, MeasurementStatus status, string? failureReason,
        MeasurementResultResponse? result, CancellationToken cancellationToken)
    {
        MeasurementRequest entity = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            Status = status,
            FailureReason = failureReason,
            HeightCmProvided = form.HeightCm.HasValue,
            ChestCm = result?.ChestCm,
            WaistCm = result?.WaistCm,
            HipsCm = result?.HipsCm,
            ShoulderWidthCm = result?.ShoulderWidthCm,
            InseamCm = result?.InseamCm,
            RecommendedSize = result?.RecommendedSize,
            ConfidenceScore = result?.Confidence
        };
        dbContext.MeasurementRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }
}
