using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

// Lives in Infrastructure for the same reason as TryOnService (see the note atop TryOnService.cs):
// it depends on the concrete TryOnDbContext, and an Application -> Infrastructure reference would
// be circular.
namespace FashionSaaS.TryOn.Infrastructure.Chat;

public class ChatService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiTextClient geminiClient,
    IOptions<GeminiSettings> geminiOptions,
    IUsageQuotaService usageQuotaService)
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
            await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                "Monthly AI usage quota exceeded.", cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                $"Gemini API error: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return (false, 502, "The assistant is unavailable right now. Please try again in a moment.", null);
        }

        var replyText = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

        if (string.IsNullOrEmpty(replyText))
        {
            await RecordAsync(latestMessage.Content.Length, 0, dto.ProductContext is not null, ChatRequestStatus.Failed,
                "Gemini returned no reply.", cancellationToken).ConfigureAwait(false);
            return (false, 502, "The assistant is unavailable right now. Please try again in a moment.", null);
        }

        await RecordAsync(latestMessage.Content.Length, replyText.Length, dto.ProductContext is not null,
            ChatRequestStatus.Completed, null, cancellationToken).ConfigureAwait(false);
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
