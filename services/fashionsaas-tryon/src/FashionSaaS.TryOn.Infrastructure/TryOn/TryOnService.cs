using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// This type lives in Infrastructure (not Application, as an earlier plan draft suggested) because
// it depends on the concrete TryOnDbContext, which lives in Infrastructure.Persistence. Infrastructure
// already references Application (for ICurrentTryOnContext/JwtSettings, from Phase 2) — an Application
// -> Infrastructure reference here would be circular (confirmed: MSBuild error MSB4006 when attempted).
// Placing the orchestration service here, alongside its DbContext dependency, keeps the layering acyclic
// while still depending only on Application abstractions (ICurrentTryOnContext, IHuggingFaceTryOnClient)
// for everything else — 2026-07-17.
//
// Publishing TryOnResultEvent is TryOnPollingWorker's job (it fires once the Hugging Face job
// actually resolves), not this class's — SubmitAsync only ever gets the job as far as Processing.
namespace FashionSaaS.TryOn.Infrastructure.TryOn;

public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IHuggingFaceTryOnClient huggingFaceClient,
    IHttpClientFactory httpClientFactory,
    IUsageQuotaService usageQuotaService)
{
    // Mirrors TryOnController's [RequestSizeLimit(15_000_000)] on the inbound photo upload, applied here
    // to the server-side garment-image fetch so a malicious/misbehaving host can't force an unbounded download.
    private const long MaxGarmentImageBytes = 15_000_000;

    /// <summary>
    /// Named client for the garment-image fetch, registered with redirects DISABLED (see Program.cs).
    /// TryOnRequestFormValidator only allowlists the host of the URL as submitted; with the default
    /// redirect-following client, an open redirect on an allowlisted host would let that check be
    /// bypassed and send this server anywhere - cloud metadata (169.254.169.254), localhost, internal
    /// services. Refusing to follow redirects keeps the allowlisted host the ONLY host contacted.
    /// </summary>
    public const string GarmentHttpClientName = "garment-image";

    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnSubmittedResponse? Data)> SubmitAsync(
        TryOnRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordFailureAsync(form, "Monthly AI try-on quota exceeded.", cancellationToken).ConfigureAwait(false);
            return (false, 429, "You've reached this month's try-on limit. Upgrade your plan or try again next month.", null);
        }

        byte[] photoBytes;
        await using (Stream stream = form.Photo.OpenReadStream())
        await using (MemoryStream memory = new())
        {
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            photoBytes = memory.ToArray();
        }

        byte[] garmentBytes;
        try
        {
            using HttpClient httpClient = httpClientFactory.CreateClient(GarmentHttpClientName);
            using HttpResponseMessage garmentResponse = await httpClient
                .GetAsync(new Uri(form.GarmentImageUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            garmentResponse.EnsureSuccessStatusCode();

            // SSRF/DoS guard: GetByteArrayAsync has no body-size cap, so a malicious or misbehaving host
            // (even one that passed the host allowlist) could stream an unbounded response. Reject up front
            // via Content-Length when the server reports it, and enforce the same cap while reading, mirroring
            // TryOnController's [RequestSizeLimit(15_000_000)] on the inbound request.
            var declaredLength = garmentResponse.Content.Headers.ContentLength;
            if (declaredLength is > MaxGarmentImageBytes)
            {
                await RecordFailureAsync(form, "Garment image exceeds the maximum allowed size.", cancellationToken).ConfigureAwait(false);
                return (false, 502, "We couldn't load the product image right now. Please try again.", null);
            }

            await using Stream garmentStream = await garmentResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream garmentMemory = new();
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await garmentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxGarmentImageBytes)
                {
                    await RecordFailureAsync(form, "Garment image exceeds the maximum allowed size.", cancellationToken).ConfigureAwait(false);
                    return (false, 502, "We couldn't load the product image right now. Please try again.", null);
                }

                await garmentMemory.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            garmentBytes = garmentMemory.ToArray();
        }
        catch (HttpRequestException ex)
        {
            await RecordFailureAsync(form, $"Could not fetch garment image: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return (false, 502, "We couldn't load the product image right now. Please try again.", null);
        }

        string jobId;
        try
        {
            jobId = await huggingFaceClient.SubmitAsync(photoBytes, garmentBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordFailureAsync(form, $"Hugging Face submit error: {ex.Message}", cancellationToken).ConfigureAwait(false);
            return (false, 502, "The try-on service is temporarily unavailable. Please try again shortly.", null);
        }

        TryOnRequest saved = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = TryOnStatus.Processing,
            ExternalJobId = jobId
        };
        dbContext.TryOnRequests.Add(saved);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, 202, "Your try-on is being generated.", new TryOnSubmittedResponse(saved.Id));
    }

    /// <summary>
    /// Fetches a try-on request's current state. Scoped to the requesting customer AND tenant —
    /// a request that exists but isn't theirs returns the same 404 as one that doesn't exist at
    /// all, so this never confirms another customer's request exists.
    /// </summary>
    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnStatusResponse? Data)> GetStatusAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        // Tenant/customer scoping belongs in the SQL predicate, not a post-fetch check: this
        // DbContext has no global tenant query filter, so filtering in memory would mean another
        // tenant's row is actually read out of the database before being rejected. Composed here so
        // the query can only ever return a row this caller owns (fail closed).
        TryOnRequest? request = await dbContext.TryOnRequests
            .AsNoTracking()
            .Where(r => r.Id == requestId
                        && r.TenantId == currentContext.TenantId
                        && r.CustomerId == currentContext.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return (false, 404, "Try-on request not found.", null);
        }

        return (true, 200, "Success",
            new TryOnStatusResponse(request.Status.ToString(), request.ResultImageUrl, request.FailureReason));
    }

    private async Task RecordFailureAsync(TryOnRequestForm form, string failureReason, CancellationToken cancellationToken)
    {
        TryOnRequest entity = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = TryOnStatus.Failed,
            // Truncated to match TryOnRequestConfiguration's HasMaxLength(500) - an upstream
            // Hugging Face error body can be arbitrarily long and previously (with Gemini) crashed
            // this save with a SQL truncation error, masking the real failure behind an unrelated 500.
            FailureReason = failureReason is { Length: > 500 } ? failureReason[..500] : failureReason
        };
        dbContext.TryOnRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
