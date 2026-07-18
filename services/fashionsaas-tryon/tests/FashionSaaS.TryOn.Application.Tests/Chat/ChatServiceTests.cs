using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Chat;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.TryOn.Application.Tests.Chat;

public class ChatServiceTests
{
    private const string UserMessage = "Does this jacket run large?";
    private const string ModelReply = "It fits true to size; check the size guide for chest measurements.";

    private readonly Mock<ICurrentTryOnContext> _context = new();
    private readonly Mock<IGeminiTextClient> _gemini = new();
    private readonly Mock<IUsageQuotaService> _usageQuota = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static TryOnDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private ChatService CreateService(TryOnDbContext dbContext, int aiUsageLimit)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(Guid.NewGuid());
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        // Mirrors TryOnServiceTests: the quota mock reflects the Completed rows seeded into the
        // db context, evaluated lazily so rows seeded after CreateService still count.
        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dbContext.ChatRequests.Count(c => c.TenantId == _tenantId && c.Status == ChatRequestStatus.Completed));

        IOptions<GeminiSettings> options = Options.Create(new GeminiSettings { ApiKey = "test-key", TextModel = "test-text-model" });

        return new ChatService(dbContext, _context.Object, _gemini.Object, options, _usageQuota.Object);
    }

    private static ChatRequestDto CreateDto(ChatProductContext? productContext = null) =>
        new([new ChatMessage("user", UserMessage)], productContext);

    private void SetupGeminiReply(string? text) =>
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerateContentResponse(
            [
                new GeminiTextCandidate(new GeminiTextContent([new GeminiTextPart(Text: text)], Role: "model"))
            ]));

    [Fact]
    public async Task ChatService_QuotaExceeded_ReturnsFailureWithoutCallingGemini()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        dbContext.ChatRequests.Add(new ChatRequest { TenantId = _tenantId, Status = ChatRequestStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        ChatService service = CreateService(dbContext, aiUsageLimit: 1);

        (var isSuccess, var statusCode, var _, ChatResultResponse? data) = await service.ReplyAsync(CreateDto(), CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(429);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // A quota-exceeded attempt still gets its own audit row (Status=Failed, so it never
        // counts toward the quota itself) — same rule as TryOnService.
        ChatRequest failedRow = await dbContext.ChatRequests.SingleAsync(c => c.Status == ChatRequestStatus.Failed);
        failedRow.FailureReason.Should().Be("Monthly AI usage quota exceeded.");
    }

    [Fact]
    public async Task ChatService_Success_PersistsCompletedRowWithLengthsNotContent()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        ChatService service = CreateService(dbContext, aiUsageLimit: 10);
        SetupGeminiReply(ModelReply);

        (var isSuccess, var statusCode, var _, ChatResultResponse? data) = await service.ReplyAsync(CreateDto(), CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.Reply.Should().Be(ModelReply);

        ChatRequest saved = await dbContext.ChatRequests.SingleAsync();
        saved.Status.Should().Be(ChatRequestStatus.Completed);
        saved.TenantId.Should().Be(_tenantId);
        saved.MessageLength.Should().Be(UserMessage.Length);
        saved.ReplyLength.Should().Be(ModelReply.Length);

        // Guards the "lengths only" decision (design spec §4.2): no raw transcript text is
        // persisted — the only string property on the entity is FailureReason, and it's null here.
        saved.FailureReason.Should().BeNull();
        typeof(ChatRequest).GetProperties()
            .Where(p => p.PropertyType.Equals(typeof(string)))
            .Should().ContainSingle("FailureReason must remain the entity's only string property — no transcript text is ever stored")
            .Which.Name.Should().Be(nameof(ChatRequest.FailureReason));
    }

    [Fact]
    public async Task ChatService_Success_WithProductContext_SetsHadProductContextTrue()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        ChatService service = CreateService(dbContext, aiUsageLimit: 10);
        SetupGeminiReply(ModelReply);
        ChatProductContext productContext = new("Denim Jacket", "Classic fit denim jacket", ["S", "M", "L"]);

        (var isSuccess, var _, var _, ChatResultResponse? _) = await service.ReplyAsync(CreateDto(productContext), CancellationToken.None);

        isSuccess.Should().BeTrue();
        ChatRequest saved = await dbContext.ChatRequests.SingleAsync();
        saved.HadProductContext.Should().BeTrue();
    }

    [Fact]
    public async Task ChatService_Success_WithoutProductContext_SetsHadProductContextFalse()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        ChatService service = CreateService(dbContext, aiUsageLimit: 10);
        SetupGeminiReply(ModelReply);

        (var isSuccess, var _, var _, ChatResultResponse? _) = await service.ReplyAsync(CreateDto(), CancellationToken.None);

        isSuccess.Should().BeTrue();
        ChatRequest saved = await dbContext.ChatRequests.SingleAsync();
        saved.HadProductContext.Should().BeFalse();
    }

    [Fact]
    public async Task ChatService_GeminiReturnsEmptyReply_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        ChatService service = CreateService(dbContext, aiUsageLimit: 10);
        SetupGeminiReply(null);

        (var isSuccess, var statusCode, var _, ChatResultResponse? data) = await service.ReplyAsync(CreateDto(), CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        ChatRequest saved = await dbContext.ChatRequests.SingleAsync();
        saved.Status.Should().Be(ChatRequestStatus.Failed);
        saved.FailureReason.Should().Be("Gemini returned no reply.");
    }

    [Fact]
    public async Task ChatService_GeminiApiError_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        ChatService service = CreateService(dbContext, aiUsageLimit: 10);
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        (var isSuccess, var statusCode, var _, ChatResultResponse? data) = await service.ReplyAsync(CreateDto(), CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        ChatRequest saved = await dbContext.ChatRequests.SingleAsync();
        saved.Status.Should().Be(ChatRequestStatus.Failed);
        saved.FailureReason.Should().StartWith("Gemini API error:");
    }
}
