using System.Net;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FashionSaaS.TryOn.Infrastructure.TryOn;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace FashionSaaS.TryOn.Application.Tests.TryOn;

public class TryOnServiceTests
{
    private readonly Mock<ICurrentTryOnContext> _context = new();
    private readonly Mock<IGeminiImageClient> _gemini = new();
    private readonly Mock<ITryOnEventPublisher> _eventPublisher = new();
    private readonly Mock<IUsageQuotaService> _usageQuota = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private static TryOnDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TryOnService CreateService(TryOnDbContext dbContext, int aiUsageLimit, HttpMessageHandler? garmentHandler = null)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(Guid.NewGuid());
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        // The quota-exceeded test (RenderAsync_QuotaExceeded_ReturnsFailureWithoutCallingGemini) still
        // seeds a Completed TryOnRequest row directly into dbContext and asserts on it — but the SERVICE
        // no longer counts it itself; it asks IUsageQuotaService. So that test must also stub the mock
        // to return a used-count reflecting the seeded row (1), keeping the test's existing assertions
        // (429, no Gemini call, Failed row persisted) valid. Evaluated lazily (at invocation time,
        // not CreateService time) because some tests seed their Completed row after CreateService.
        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dbContext.TryOnRequests.Count(t => t.TenantId == _tenantId && t.Status == TryOnStatus.Completed));

        // CA2000 suppressed: the handler/HttpClient are test doubles handed to the mocked
        // IHttpClientFactory; TryOnService disposes the HttpClient itself (via its own `using`
        // block) once RenderAsync runs, and each test creates a fresh instance — no real leak.
#pragma warning disable CA2000
        HttpMessageHandler handler = garmentHandler ?? new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
#pragma warning restore CA2000

        IOptions<GeminiSettings> options = Options.Create(new GeminiSettings { ApiKey = "test-key", Model = "test-model" });

        return new TryOnService(dbContext, _context.Object, _gemini.Object, factory.Object, options, _eventPublisher.Object, _usageQuota.Object);
    }

    private static FormFile CreateFakePhoto()
    {
        byte[] bytes = [9, 9, 9];
        MemoryStream stream = new(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
    }

    [Fact]
    public async Task RenderAsync_QuotaExceeded_ReturnsFailureWithoutCallingGemini()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = _tenantId, Status = TryOnStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        TryOnService service = CreateService(dbContext, aiUsageLimit: 1);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        (var isSuccess, var statusCode, var _, TryOnResultResponse? data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(429);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // Spec §15: a quota-exceeded attempt still gets its own audit row (Status=Failed,
        // so it never counts toward the quota itself) — helps evaluate whether limits are sane.
        TryOnRequest failedRow = await dbContext.TryOnRequests.SingleAsync(t => t.Status == TryOnStatus.Failed);
        failedRow.FailureReason.Should().Be("Monthly AI try-on quota exceeded.");
    }

    [Fact]
    public async Task RenderAsync_Success_PersistsCompletedRowAndReturnsDataUri()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiGenerateContentResponse(
            [
                new GeminiCandidate(new GeminiContent([new GeminiPart(InlineData: new GeminiInlineData("image/png", "QUJD"))]))
            ]));

        (var isSuccess, var statusCode, var _, TryOnResultResponse? data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.ResultImageDataUri.Should().Be("data:image/png;base64,QUJD");

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Completed);
        saved.TenantId.Should().Be(_tenantId);

        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnCompletedEvent>(e => e.TryOnRequestId == saved.Id && e.TenantId == _tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenderAsync_Failure_NeverPublishesEvent()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 1);
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = _tenantId, Status = TryOnStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        await service.RenderAsync(form, CancellationToken.None);

        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<TryOnCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenderAsync_GeminiReturnsNoImage_PersistsFailedRowWithReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiGenerateContentResponse([new GeminiCandidate(new GeminiContent([new GeminiPart(Text: "no image")]))]));

        (var isSuccess, var statusCode, var _, TryOnResultResponse? data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
        saved.FailureReason.Should().Be("Gemini returned no image.");
    }

    [Fact]
    public async Task RenderAsync_GarmentImageFetchFails_PersistsFailedRowWithoutCallingGemini()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
#pragma warning disable CA2000 // see justification in CreateService above — TryOnService owns disposal
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10, garmentHandler: new StubHandler(HttpStatusCode.NotFound, []));
#pragma warning restore CA2000
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/missing.jpg", ProductId = Guid.NewGuid() };

        (var isSuccess, var statusCode, var _, TryOnResultResponse? data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
    }
}

// Minimal fake HttpMessageHandler for the garment-image GET — avoids a real network call in a unit test.
internal sealed class StubHandler(HttpStatusCode statusCode, byte[] body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = new(statusCode) { Content = new ByteArrayContent(body) };
        if (statusCode != HttpStatusCode.OK)
        {
            response.EnsureSuccessStatusCode(); // throws HttpRequestException, matching real HttpClient behavior on 404
        }
        return Task.FromResult(response);
    }
}
