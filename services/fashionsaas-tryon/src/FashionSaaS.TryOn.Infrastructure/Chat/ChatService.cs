using System.Net;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

// Lives in Infrastructure for the same reason as TryOnService (see the note atop TryOnService.cs):
// it depends on the concrete TryOnDbContext, and an Application -> Infrastructure reference would
// be circular.
namespace FashionSaaS.TryOn.Infrastructure.Chat;

public class ChatService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiTextClient geminiClient,
    IOptions<GeminiSettings> geminiOptions,
    IUsageQuotaService usageQuotaService,
    ILogger<ChatService> logger)
{
    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, ChatResultResponse? Data)> ReplyAsync(
        ChatRequestDto dto, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        ChatMessage latestMessage = dto.Messages[^1];

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            ChatRequest quotaRow = await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                "Monthly AI usage quota exceeded.", cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Chat request {ChatRequestId} for tenant {TenantId} rejected: monthly AI usage quota exceeded", quotaRow.Id, currentContext.TenantId);
            return (false, 429, "You've reached this month's AI usage limit. Upgrade your plan or try again next month.", null);
        }

        var systemInstructionText = GeminiPrompts.ChatPersonaAndRules;
        if (dto.ProductContext is not null)
        {
            systemInstructionText += GeminiPrompts.ChatProductContextLine(
                dto.ProductContext.Name, dto.ProductContext.Description, dto.ProductContext.Sizes);
        }

        GeminiTextGenerateContentResponse response;
        try
        {
            GeminiTextGenerateContentRequest request = new(
                Contents: dto.Messages
                    .Select(m => new GeminiTextContent(Parts: [new GeminiTextPart(m.Content)], Role: m.Role))
                    .ToArray(),
                SystemInstruction: new GeminiTextContent(Parts: [new GeminiTextPart(systemInstructionText)]));

            response = await geminiClient.GenerateContentAsync(_gemini.TextModel, _gemini.ApiKey, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ApiException)
        {
            // Refit throws ApiException (distinct from HttpRequestException) for any non-2xx Gemini
            // response — it carries the real status code and body, which HttpRequestException does not.
            // Persist a status-aware reason for later debugging even though the client-facing message
            // stays generic (except for a 429, which gets its own clearer message).
            var failureReason = ex is ApiException apiEx
                ? $"Gemini API error: {(int)apiEx.StatusCode} {apiEx.StatusCode} - {apiEx.Content ?? apiEx.Message}"
                : $"Gemini API error: {ex.Message}";
            var clientMessage = ex is ApiException { StatusCode: HttpStatusCode.TooManyRequests }
                ? "The AI service is temporarily busy — please try again shortly."
                : "The assistant is unavailable right now. Please try again in a moment.";

            ChatRequest apiErrorRow = await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                failureReason, cancellationToken).ConfigureAwait(false);
            logger.LogWarning(ex, "Chat request {ChatRequestId} for tenant {TenantId} failed: Gemini API error", apiErrorRow.Id, currentContext.TenantId);
            return (false, 502, clientMessage, null);
        }

        var replyText = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

        if (string.IsNullOrEmpty(replyText))
        {
            ChatRequest emptyReplyRow = await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                "Gemini returned no reply.", cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Chat request {ChatRequestId} for tenant {TenantId} failed: Gemini returned no reply", emptyReplyRow.Id, currentContext.TenantId);
            return (false, 502, "The assistant is unavailable right now. Please try again in a moment.", null);
        }

        ChatRequest completedRow = await RecordAsync(latestMessage.Content.Length, replyText.Length, dto.ProductContext is not null,
            ChatRequestStatus.Completed, null, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Chat request {ChatRequestId} for tenant {TenantId} completed with status {Status}", completedRow.Id, currentContext.TenantId, ChatRequestStatus.Completed);
        return (true, 200, "Success", new ChatResultResponse(replyText));
    }

    private async Task<ChatRequest> RecordAsync(
        int messageLength, int replyLength, bool hadProductContext, ChatRequestStatus status,
        string? failureReason, CancellationToken cancellationToken)
    {
        ChatRequest entity = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            Status = status,
            FailureReason = failureReason,
            MessageLength = messageLength,
            ReplyLength = replyLength,
            HadProductContext = hadProductContext
        };
        dbContext.ChatRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }
}
