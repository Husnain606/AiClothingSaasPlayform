using System.Net;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FashionSaaS.TryOn.Infrastructure.TryOn;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FashionSaaS.TryOn.Application.Tests.TryOn;

public class TryOnServiceTests
{
    private readonly Mock<ICurrentTryOnContext> _context = new();
    private readonly Mock<IHuggingFaceTryOnClient> _huggingFace = new();
    private readonly Mock<IUsageQuotaService> _usageQuota = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    private static TryOnDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TryOnService CreateService(TryOnDbContext dbContext, int aiUsageLimit, HttpMessageHandler? garmentHandler = null)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(_customerId);
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dbContext.TryOnRequests.Count(t => t.TenantId == _tenantId && t.Status != TryOnStatus.Failed));

        // CA2000 suppressed: the handler/HttpClient are test doubles handed to the mocked
        // IHttpClientFactory; TryOnService disposes the HttpClient itself (via its own `using`
        // block) once SubmitAsync runs, and each test creates a fresh instance — no real leak.
#pragma warning disable CA2000
        HttpMessageHandler handler = garmentHandler ?? new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
#pragma warning restore CA2000

        return new TryOnService(dbContext, _context.Object, _huggingFace.Object, factory.Object, _usageQuota.Object);
    }

    private static FormFile CreateFakePhoto()
    {
        byte[] bytes = [9, 9, 9];
        MemoryStream stream = new(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
    }

    [Fact]
    public async Task SubmitAsync_QuotaExceeded_ReturnsFailureWithoutCallingHuggingFace()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = _tenantId, Status = TryOnStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        TryOnService service = CreateService(dbContext, aiUsageLimit: 1);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        (var isSuccess, var statusCode, var _, TryOnSubmittedResponse? data) = await service.SubmitAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(429);
        data.Should().BeNull();
        _huggingFace.Verify(h => h.SubmitAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);

        TryOnRequest failedRow = await dbContext.TryOnRequests.SingleAsync(t => t.Status == TryOnStatus.Failed);
        failedRow.FailureReason.Should().Be("Monthly AI try-on quota exceeded.");
    }

    [Fact]
    public async Task SubmitAsync_Success_PersistsProcessingRowWithJobId_Returns202()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _huggingFace.Setup(h => h.SubmitAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("evt-123");

        (var isSuccess, var statusCode, var _, TryOnSubmittedResponse? data) = await service.SubmitAsync(form, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(202);
        data.Should().NotBeNull();

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Processing);
        saved.ExternalJobId.Should().Be("evt-123");
        saved.Id.Should().Be(data!.RequestId);
    }

    [Fact]
    public async Task SubmitAsync_HuggingFaceSubmitThrows_PersistsFailedRowWithoutProcessingState()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _huggingFace.Setup(h => h.SubmitAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Space unreachable"));

        (var isSuccess, var statusCode, var _, TryOnSubmittedResponse? data) = await service.SubmitAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
        saved.ExternalJobId.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_GarmentImageFetchFails_PersistsFailedRowWithoutCallingHuggingFace()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
#pragma warning disable CA2000 // see justification in CreateService above — TryOnService owns disposal
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10, garmentHandler: new StubHandler(HttpStatusCode.NotFound, []));
#pragma warning restore CA2000
        TryOnRequestForm form = new() { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/missing.jpg", ProductId = Guid.NewGuid() };

        (var isSuccess, var statusCode, var _, TryOnSubmittedResponse? data) = await service.SubmitAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();
        _huggingFace.Verify(h => h.SubmitAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);

        TryOnRequest saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
    }

    [Fact]
    public async Task GetStatusAsync_OwnCompletedRequest_ReturnsStatusAndResultUrl()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        var request = new TryOnRequest
        {
            TenantId = _tenantId,
            CustomerId = _customerId,
            Status = TryOnStatus.Completed,
            ResultImageUrl = "https://space.hf.space/file=result.png"
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) =
            await service.GetStatusAsync(request.Id, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.Status.Should().Be("Completed");
        data.ResultImageUrl.Should().Be("https://space.hf.space/file=result.png");
    }

    [Fact]
    public async Task GetStatusAsync_AnotherCustomersRequest_Returns404()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        var request = new TryOnRequest { TenantId = _tenantId, CustomerId = Guid.NewGuid(), Status = TryOnStatus.Completed };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) =
            await service.GetStatusAsync(request.Id, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(404, "another customer's request must be indistinguishable from a missing one");
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_AnotherTenantsRequest_Returns404()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = _customerId, Status = TryOnStatus.Completed };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) =
            await service.GetStatusAsync(request.Id, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(404);
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_UnknownId_Returns404()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) =
            await service.GetStatusAsync(Guid.NewGuid(), CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(404);
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_FailedRequest_ReturnsFailureReason()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        var request = new TryOnRequest
        {
            TenantId = _tenantId,
            CustomerId = _customerId,
            Status = TryOnStatus.Failed,
            FailureReason = "Try-on render timed out."
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        (var isSuccess, var _, var _, TryOnStatusResponse? data) =
            await service.GetStatusAsync(request.Id, CancellationToken.None);

        isSuccess.Should().BeTrue();
        data!.Status.Should().Be("Failed");
        data.ResultImageUrl.Should().BeNull();
        data.FailureReason.Should().Be("Try-on render timed out.");
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
