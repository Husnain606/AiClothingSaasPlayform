using System.Net;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Measurement;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Refit;

namespace FashionSaaS.TryOn.Application.Tests.Measurement;

public class MeasurementServiceTests
{
    private const string ValidReplyJson =
        """{"chestCm": 96.5, "waistCm": 80.0, "hipsCm": 100.0, "shoulderWidthCm": 45.5, "inseamCm": 78.0, "recommendedSize": "M", "confidence": 0.85}""";

    private readonly Mock<ICurrentTryOnContext> _context = new();
    private readonly Mock<IGeminiTextClient> _gemini = new();
    private readonly Mock<IUsageQuotaService> _usageQuota = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static TryOnDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private MeasurementService CreateService(TryOnDbContext dbContext, int aiUsageLimit)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(Guid.NewGuid());
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        // Mirrors TryOnServiceTests: the quota mock reflects the Completed rows seeded into the
        // db context, evaluated lazily so rows seeded after CreateService still count.
        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dbContext.MeasurementRequests.Count(m => m.TenantId == _tenantId && m.Status == MeasurementStatus.Completed));

        IOptions<GeminiSettings> options = Options.Create(new GeminiSettings { ApiKey = "test-key", TextModel = "test-text-model" });

        return new MeasurementService(dbContext, _context.Object, _gemini.Object, options, _usageQuota.Object,
            NullLogger<MeasurementService>.Instance);
    }

    private static FormFile CreateFakePhoto()
    {
        byte[] bytes = [9, 9, 9];
        MemoryStream stream = new(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
    }

    private void SetupGeminiReply(string? text) =>
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiTextGenerateContentResponse(
            [
                new GeminiTextCandidate(new GeminiTextContent([new GeminiTextPart(Text: text)], Role: "model"))
            ]));

    [Fact]
    public async Task MeasurementService_QuotaExceeded_ReturnsFailureWithoutCallingGemini()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        dbContext.MeasurementRequests.Add(new MeasurementRequest { TenantId = _tenantId, Status = MeasurementStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        MeasurementService service = CreateService(dbContext, aiUsageLimit: 1);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(429);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // A quota-exceeded attempt still gets its own audit row (Status=Failed, so it never
        // counts toward the quota itself) — same rule as TryOnService.
        MeasurementRequest failedRow = await dbContext.MeasurementRequests.SingleAsync(m => m.Status == MeasurementStatus.Failed);
        failedRow.FailureReason.Should().Be("Monthly AI usage quota exceeded.");
    }

    [Fact]
    public async Task MeasurementService_Success_PersistsCompletedRowWithParsedValues()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto(), HeightCm = 175 };
        SetupGeminiReply(ValidReplyJson);

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.ChestCm.Should().Be(96.5m);
        data.RecommendedSize.Should().Be(SizeCode.M);
        data.Confidence.Should().Be(0.85m);

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Completed);
        saved.TenantId.Should().Be(_tenantId);
        saved.HeightCmProvided.Should().BeTrue();
        saved.ChestCm.Should().Be(96.5m);
        saved.WaistCm.Should().Be(80.0m);
        saved.HipsCm.Should().Be(100.0m);
        saved.ShoulderWidthCm.Should().Be(45.5m);
        saved.InseamCm.Should().Be(78.0m);
        saved.RecommendedSize.Should().Be(SizeCode.M);
        saved.ConfidenceScore.Should().Be(0.85m);
    }

    [Fact]
    public async Task MeasurementService_GeminiReturnsUnparseableJson_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        SetupGeminiReply("Sorry, I can't estimate measurements from this photo.");

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().Be("Could not parse measurement response.");
    }

    [Fact]
    public async Task MeasurementService_GeminiReturnsInvalidSizeCode_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        SetupGeminiReply(
            """{"chestCm": 96.5, "waistCm": 80.0, "hipsCm": 100.0, "shoulderWidthCm": 45.5, "inseamCm": 78.0, "recommendedSize": "GIGANTIC", "confidence": 0.85}""");

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().Be("Could not parse measurement response.");
    }

    [Fact]
    public async Task MeasurementService_GeminiReturnsFencedJson_ParsesSuccessfully()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        SetupGeminiReply("```json\n" + ValidReplyJson + "\n```");

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.ChestCm.Should().Be(96.5m);
        data.RecommendedSize.Should().Be(SizeCode.M);

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Completed);
        saved.ChestCm.Should().Be(96.5m);
        saved.WaistCm.Should().Be(80.0m);
        saved.ConfidenceScore.Should().Be(0.85m);
    }

    [Fact]
    public async Task MeasurementService_GeminiReturnsPartialJson_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };

        // waistCm is missing — the service must fail rather than fabricate a 0 measurement.
        SetupGeminiReply(
            """{"chestCm": 96.5, "hipsCm": 100.0, "shoulderWidthCm": 45.5, "inseamCm": 78.0, "recommendedSize": "M", "confidence": 0.85}""");

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().Be("incomplete measurement data from model");
        saved.WaistCm.Should().BeNull();
    }

    [Fact]
    public async Task MeasurementService_GeminiApiError_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().StartWith("Gemini API error:");
    }


    [Fact]
    public async Task MeasurementService_GeminiReturnsRateLimitError_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(await CreateRateLimitApiExceptionAsync());

        (var isSuccess, var statusCode, var message, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        message.Should().Be("The AI service is temporarily busy — please try again shortly.");
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().Contain("429").And.Contain("TooManyRequests");
    }


    [Fact]
    public async Task MeasurementService_GeminiErrorBodyExceedsColumnLimit_TruncatesInsteadOfCrashing()
    {
        // Regression test: a real Gemini error body can exceed FailureReason's HasMaxLength(500)
        // (MeasurementRequestConfiguration) - previously this crashed SaveChangesAsync with a SQL
        // truncation DbUpdateException, masking the actual API error behind an unrelated 500.
        await using TryOnDbContext dbContext = CreateDbContext();
        MeasurementService service = CreateService(dbContext, aiUsageLimit: 10);
        MeasurementRequestForm form = new() { Photo = CreateFakePhoto() };
        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiTextGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(await CreateOversizedApiExceptionAsync());

        (var isSuccess, var statusCode, var _, MeasurementResultResponse? data) = await service.EstimateAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        MeasurementRequest saved = await dbContext.MeasurementRequests.SingleAsync();
        saved.Status.Should().Be(MeasurementStatus.Failed);
        saved.FailureReason.Should().NotBeNull();
        saved.FailureReason!.Length.Should().BeLessThanOrEqualTo(500);
    }

    private static async Task<ApiException> CreateOversizedApiExceptionAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/test-text-model:generateContent");
        var oversizedMessage = new string('x', 800);
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                "{\"error\":{\"code\":429,\"message\":\"" + oversizedMessage + "\",\"status\":\"RESOURCE_EXHAUSTED\"}}")
        };
        return await ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
    }

    // Refit doesn't expose a public ApiException constructor — the documented way to build one in a
    // test is its internal `Create` factory (accessed via its public static entry point), fed a real
    // HttpResponseMessage carrying the status code/body Refit would have received from Gemini.
    private static async Task<ApiException> CreateRateLimitApiExceptionAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/test-text-model:generateContent");
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":429,"message":"Resource has been exhausted","status":"RESOURCE_EXHAUSTED"}}""")
        };
        return await ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
    }
}
