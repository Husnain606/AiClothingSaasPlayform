using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.TryOn.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls every Processing TryOnRequest on a fixed interval, following the same
/// PeriodicTimer + per-tick DI scope + swallow-and-continue pattern as the main API's
/// SubscriptionExpiryJob (the only other BackgroundService in this codebase).
/// </summary>
public class TryOnPollingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TryOnPollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
#pragma warning disable CA1031
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TryOnPollingWorker tick failed");
            }
#pragma warning restore CA1031
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        TryOnDbContext dbContext = scope.ServiceProvider.GetRequiredService<TryOnDbContext>();
        IHuggingFaceTryOnClient huggingFaceClient = scope.ServiceProvider.GetRequiredService<IHuggingFaceTryOnClient>();
        ITryOnEventPublisher eventPublisher = scope.ServiceProvider.GetRequiredService<ITryOnEventPublisher>();

        List<TryOnRequest> processing = await dbContext.TryOnRequests
            .Where(t => t.Status == TryOnStatus.Processing)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (TryOnRequest request in processing)
        {
            // Per-job isolation: without it, ONE job throwing (e.g. a Space returning a body
            // PollAsync can't parse) would abort the whole tick, so every other job in the batch
            // went unpolled - every tick - and eventually got force-failed as "timed out" despite
            // having rendered fine. One bad job must not starve the rest.
#pragma warning disable CA1031
            try
            {
                await PollOneAsync(dbContext, huggingFaceClient, eventPublisher, request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to poll try-on request {TryOnRequestId}; other requests continue", request.Id);
            }
#pragma warning restore CA1031
        }
    }

    private static async Task PollOneAsync(
        TryOnDbContext dbContext,
        IHuggingFaceTryOnClient huggingFaceClient,
        ITryOnEventPublisher eventPublisher,
        TryOnRequest request,
        CancellationToken ct)
    {
        if (DateTime.UtcNow - request.CreatedAt > ProcessingTimeout)
        {
            await FailAsync(dbContext, eventPublisher, request, "Try-on render timed out.", ct);
            return;
        }

        HuggingFaceJobResult result = await huggingFaceClient.PollAsync(request.ExternalJobId!, ct);

        switch (result.State)
        {
            // A Complete carrying no path is not a success: storing a null ResultImageUrl while
            // publishing IsSuccess:true would leave the row self-contradictory and give the
            // storefront a "Completed" render with nothing to show.
            case HuggingFaceJobState.Complete when !string.IsNullOrWhiteSpace(result.ResultImageUrl):
                await CompleteAsync(dbContext, eventPublisher, request, result.ResultImageUrl, ct);
                break;
            case HuggingFaceJobState.Complete:
                await FailAsync(dbContext, eventPublisher, request, "Hugging Face reported completion without a result image.", ct);
                break;
            case HuggingFaceJobState.Failed:
                await FailAsync(dbContext, eventPublisher, request, result.ErrorMessage ?? "Hugging Face render failed.", ct);
                break;
            case HuggingFaceJobState.Pending:
                break; // leave it Processing, try again next tick
            default:
                throw new InvalidOperationException($"Unknown HuggingFaceJobState: {result.State}");
        }
    }

    private static async Task CompleteAsync(TryOnDbContext dbContext, ITryOnEventPublisher eventPublisher,
        TryOnRequest request, string resultImageUrl, CancellationToken ct)
    {
        request.Status = TryOnStatus.Completed;
        request.ResultImageUrl = resultImageUrl;
        // Resolution now happens minutes after creation, so UpdatedAt is the only record of WHEN
        // the render actually finished; BaseEntity only stamps it at construction.
        request.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new TryOnResultEvent(request.Id, request.TenantId, request.CustomerId, request.ProductId, request.CreatedAt,
                IsSuccess: true, resultImageUrl, FailureReason: null),
            ct).ConfigureAwait(false);
    }

    private static async Task FailAsync(TryOnDbContext dbContext, ITryOnEventPublisher eventPublisher,
        TryOnRequest request, string reason, CancellationToken ct)
    {
        request.Status = TryOnStatus.Failed;
        // Same 500-char cap as TryOnService.RecordFailureAsync - an upstream error body here can
        // be arbitrarily long and would otherwise crash this exact SaveChangesAsync call.
        request.FailureReason = reason is { Length: > 500 } ? reason[..500] : reason;
        request.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new TryOnResultEvent(request.Id, request.TenantId, request.CustomerId, request.ProductId, request.CreatedAt,
                IsSuccess: false, ResultImageUrl: null, request.FailureReason),
            ct).ConfigureAwait(false);
    }
}
