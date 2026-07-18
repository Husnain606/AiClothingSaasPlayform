using System.Text.Json;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
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
    IUsageQuotaService usageQuotaService,
    ILogger<MeasurementService> logger)
{
    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, MeasurementResultResponse? Data)> EstimateAsync(
        MeasurementRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            MeasurementRequest quotaRow = await RecordAsync(form, MeasurementStatus.Failed, "Monthly AI usage quota exceeded.", null, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Measurement request {MeasurementRequestId} for tenant {TenantId} rejected: monthly AI usage quota exceeded", quotaRow.Id, currentContext.TenantId);
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
            MeasurementRequest apiErrorRow = await RecordAsync(form, MeasurementStatus.Failed, $"Gemini API error: {ex.Message}", null, cancellationToken).ConfigureAwait(false);
            logger.LogWarning(ex, "Measurement request {MeasurementRequestId} for tenant {TenantId} failed: Gemini API error", apiErrorRow.Id, currentContext.TenantId);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        var replyText = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

        GeminiMeasurementResult? parsed = null;
        var jsonPayload = replyText is null ? null : ExtractJsonPayload(replyText);
        if (jsonPayload is not null)
        {
            try
            {
                parsed = JsonSerializer.Deserialize<GeminiMeasurementResult>(jsonPayload);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (parsed is null || !Enum.TryParse<SizeCode>(parsed.RecommendedSize, ignoreCase: true, out SizeCode recommendedSize))
        {
            MeasurementRequest unparseableRow = await RecordAsync(form, MeasurementStatus.Failed, "Could not parse measurement response.", null, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Measurement request {MeasurementRequestId} for tenant {TenantId} failed: could not parse measurement response", unparseableRow.Id, currentContext.TenantId);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        MeasurementResultResponse? result = TryBuildValidatedResponse(parsed, recommendedSize);
        if (result is null)
        {
            MeasurementRequest incompleteRow = await RecordAsync(form, MeasurementStatus.Failed, "incomplete measurement data from model", null, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Measurement request {MeasurementRequestId} for tenant {TenantId} failed: incomplete measurement data from model", incompleteRow.Id, currentContext.TenantId);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        MeasurementRequest completedRow = await RecordAsync(form, MeasurementStatus.Completed, null, result, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Measurement request {MeasurementRequestId} for tenant {TenantId} completed with status {Status}", completedRow.Id, currentContext.TenantId, MeasurementStatus.Completed);
        return (true, 200, "Success", result);
    }

    // Gemini routinely wraps its JSON reply in a markdown code fence or surrounds it with prose
    // despite prompt instructions. Strip a leading/trailing fence if present; otherwise fall back
    // to the substring between the first '{' and the last '}'.
    private static string? ExtractJsonPayload(string replyText)
    {
        var text = replyText.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal) && text.Length > 6)
        {
            text = text[3..^3];
            if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                text = text[4..];
            }

            return text.Trim();
        }

        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    // Every numeric field must be present and positive before we return a Completed result —
    // a missing field must never silently become 0 in a response handed to the customer.
    private static MeasurementResultResponse? TryBuildValidatedResponse(GeminiMeasurementResult parsed, SizeCode recommendedSize)
    {
        if (parsed.ChestCm is not > 0m || parsed.WaistCm is not > 0m || parsed.HipsCm is not > 0m ||
            parsed.ShoulderWidthCm is not > 0m || parsed.InseamCm is not > 0m || parsed.Confidence is not > 0m)
        {
            return null;
        }

        return new MeasurementResultResponse(
            parsed.ChestCm.GetValueOrDefault(), parsed.WaistCm.GetValueOrDefault(), parsed.HipsCm.GetValueOrDefault(),
            parsed.ShoulderWidthCm.GetValueOrDefault(), parsed.InseamCm.GetValueOrDefault(),
            recommendedSize, parsed.Confidence.GetValueOrDefault());
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
