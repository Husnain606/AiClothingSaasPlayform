using System.Text.Json;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.BackgroundJobs;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.TryOn.Infrastructure.Tests.BackgroundJobs;

public class TryOnPollingWorkerTests
{
    private readonly Mock<IHuggingFaceTryOnClient> _huggingFace = new();
    private readonly Mock<ITryOnEventPublisher> _eventPublisher = new();

    private (TryOnDbContext DbContext, IServiceScopeFactory ScopeFactory) CreateScopedDbContext()
    {
        var dbName = Guid.NewGuid().ToString();
        ServiceCollection services = new();
        services.AddDbContext<TryOnDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(_huggingFace.Object);
        services.AddSingleton(_eventPublisher.Object);
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolving the seed DbContext from its own scope of the SAME container (rather than a
        // freestanding DbContextOptionsBuilder) is what makes it share the in-memory store with
        // the scopes RunOnceAsync creates later - EF's InMemory provider ties a named database to
        // the internal service provider that built it, and AddDbContext's internal provider is
        // shared across every scope of this one container.
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        TryOnDbContext dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<TryOnDbContext>();
        return (dbContext, scopeFactory);
    }

    [Fact]
    public async Task RunOnceAsync_JobStillPending_LeavesRowUnchanged()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-1" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Processing);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<TryOnResultEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_JobComplete_UpdatesRowAndPublishesSuccessEvent()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-2" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Complete, "https://space.hf.space/file=result.png", null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Completed);
        reloaded.ResultImageUrl.Should().Be("https://space.hf.space/file=result.png");

        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnResultEvent>(e => e.TryOnRequestId == request.Id && e.IsSuccess && e.ResultImageUrl == "https://space.hf.space/file=result.png"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_JobFailed_UpdatesRowAndPublishesFailureEvent()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-3" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, "CUDA out of memory"));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Failed);
        reloaded.FailureReason.Should().Be("CUDA out of memory");

        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnResultEvent>(e => !e.IsSuccess && e.FailureReason == "CUDA out of memory"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OversizedFailureReason_TruncatesTo500Chars()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-4" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        var oversized = new string('x', 800);
        _huggingFace.Setup(h => h.PollAsync("evt-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, oversized));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.FailureReason!.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task RunOnceAsync_ProcessingPastTenMinutes_ForceFailsWithTimeoutReason()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = TryOnStatus.Processing,
            ExternalJobId = "evt-5",
            CreatedAt = DateTime.UtcNow.AddMinutes(-11)
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Failed);
        reloaded.FailureReason.Should().Be("Try-on render timed out.");

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<TryOnResultEvent>(e => !e.IsSuccess), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_CompleteWithoutResultUrl_IsTreatedAsFailureNotSuccess()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-6" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-6", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Complete, null, null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Failed, "a completion with no image is not a usable result");
        reloaded.ResultImageUrl.Should().BeNull();

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<TryOnResultEvent>(e => !e.IsSuccess), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OneJobThrows_StillPollsTheOthers()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        var bad = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-bad" };
        var good = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-good" };
        dbContext.TryOnRequests.AddRange(bad, good);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JsonException("unparseable Space response"));
        _huggingFace.Setup(h => h.PollAsync("evt-good", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Complete, "https://space.hf.space/file=ok.png", null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        // Without per-job isolation the throwing job aborted the whole tick, so this one would
        // still be Processing - and would eventually be force-failed as a bogus "timeout".
        TryOnRequest reloadedGood = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == good.Id);
        reloadedGood.Status.Should().Be(TryOnStatus.Completed);

        TryOnRequest reloadedBad = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == bad.Id);
        reloadedBad.Status.Should().Be(TryOnStatus.Processing, "a transient parse failure should be retried next tick, not marked terminal");
    }

    [Fact]
    public async Task RunOnceAsync_TerminalTransition_StampsUpdatedAt()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        DateTime createdAt = DateTime.UtcNow.AddMinutes(-3);
        var request = new TryOnRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = TryOnStatus.Processing,
            ExternalJobId = "evt-7",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Complete, "https://space.hf.space/file=r.png", null));

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.AsNoTracking().SingleAsync(t => t.Id == request.Id);
        reloaded.UpdatedAt.Should().BeAfter(createdAt, "resolution happens minutes after creation, so it needs its own timestamp");
    }

    [Fact]
    public async Task RunOnceAsync_NoProcessingRows_DoesNothing()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext();
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = Guid.NewGuid(), Status = TryOnStatus.Completed });
        await dbContext.SaveChangesAsync();

        using TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        _huggingFace.Verify(h => h.PollAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
