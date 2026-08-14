# Free Virtual Try-On via Hugging Face — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Gemini image generation behind "Try It On" with a free Hugging Face Space (your own duplicated copy), using an async submit→poll→push flow so the free CPU tier's 1–5 minute render time doesn't block the HTTP request.

**Architecture:** `POST /api/tryon` keeps today's validation (quota, photo read, garment fetch) then submits to Hugging Face instead of Gemini, saves a new `Processing` row, and returns `202` immediately. A new background poller in the try-on microservice watches for completion and publishes one Service Bus event (success or failure). A brand-new consumer in the main API (none exists today) turns that into a `Notification` + a live SignalR push. The storefront shows a processing state, then fetches the final result via a new GET endpoint once the push arrives.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), Azure Service Bus (local: emulator), SignalR, Angular 21, xUnit + FluentAssertions + Moq.

**Spec:** `docs/superpowers/specs/2026-07-26-tryon-free-huggingface-design.md`

---

## STATUS — executed 2026-08-15 (branch `worktree-tryon-huggingface`, 15 commits, unmerged)

**Tasks 1-8: code complete. 71 of 75 steps executed; 4 blocked (marked ⛔ inline).**

| Gate | Result |
|---|---|
| `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` | 0 Warning(s) 0 Error(s) |
| `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` | 88/88 passing |
| `dotnet build FashionSaaS.sln` | 0 Warning(s) 0 Error(s) |
| `dotnet test FashionSaaS.sln` | 579/579 passing (was 571 at branch point) |
| `npx ng build` (storefront) | green |
| Storefront specs for this feature | 37/37 passing |
| Serena `get_diagnostics_for_file` | clean, apart from a pre-existing repo-wide IDE1006 |
| Replaced Gemini/event/DTO types removed | no live references remain |

**⛔ Blocked:** Task 2 Step 0, Task 6 Step 9, Task 8 Step 8, and the Validate gate's live run all
require a real duplicated Hugging Face Space. None exists, so `HuggingFaceSettings` holds
placeholders and the Gradio protocol in `HuggingFaceTryOnClient` is **unverified** — isolated behind
`IHuggingFaceTryOnClient` so only that class should need changing.

### Defects found DURING execution that the plan did not anticipate (all fixed)

1. **SignalR ignores MVC's `AddJsonOptions`** — hub payloads serialized `"type":5` while the
   storefront compares `'TryOnCompleted'`, so the completion guard could never match. Also fixed the
   same latent break in `customer-order-toast.service.ts`, dead since Phase 7. Locked by
   `NotificationPushContractTests` (asserts both directions).
2. **Captive dependency** — `TryOnResultConsumer` (singleton via `AddHostedService`) took the scoped
   `NotificationService`; Development scope validation would have thrown at startup. Now resolved per
   message from `IServiceScopeFactory`. Missed initially because the tests used `new`, bypassing DI.
3. **Main API would not boot in Production** — `ServiceBusSettings.ConnectionString` was `[Required]`
   + `ValidateOnStart()`, but `containerApps.bicep` injects no such env var. The consumer is now
   registered only when a connection string is present.
4. **AI quota was bypassable** — `UsageQuotaService` counted only `Completed`; async leaves rows
   `Processing` for minutes. Now counts `!= Failed`.
5. **A Service Bus fault took down the whole API** — `BackgroundServiceExceptionBehavior` defaults to
   `StopHost`. `StartProcessingAsync` is now guarded.
6. **Production discarded every event** — `serviceBus.bicep` created the topic with no subscription
   (an Azure topic with none drops all messages). Added `main-api-tryon-results`.
7. Plus: tenant/customer scoping moved into SQL; per-job poll isolation; `Complete`-without-path
   treated as failure; `UpdatedAt` stamped on resolution; `ConfigureAwait(false)`; storefront
   `tryOnRequestId` cleared so a redelivered push can't resurrect a stale result.

### Deviations from the plan as written
- Tests for the main-API consumer live in `FashionSaaS.Infrastructure.Tests` (where the existing
  notification-handler tests live), not `FashionSaaS.API.Tests` as the plan guessed.
- The plan suggested making `NotificationService.CreateAsync` virtual for mocking; unnecessary — the
  codebase already constructs a real `NotificationService` with mocked dependencies.
- Tasks 3→4 and 4→5 deliberately left the try-on solution non-building, as the plan predicted.
- The whole Angular suite could not compile until 15 pre-existing fixture type errors were repaired
  (`Product.tags`, `WishlistItem`) — approved as a scope extension.


## Global Constraints

- **No new third-party NuGet/npm packages.** The Hugging Face client is hand-rolled `HttpClient` (Refit can't model Gradio's SSE queue protocol). Everything else uses packages already in the two solutions.
- **All `.cs` edits go through Serena MCP tools**, never native Edit/Write — a `PreToolUse` hook blocks native writes on `.cs`. Angular `.ts`/`.html` use native tools.
- **Verification gate for every `.cs` change:** `dotnet build` (warnings-as-errors) on the touched solution, **and** `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every touched file.
- **Poll timeout: 10 minutes.** A `Processing` row untouched past this is force-failed with `"Try-on render timed out."`.
- **`FailureReason` stays capped at 500 chars** (existing `HasMaxLength(500)`, existing truncation logic) — this session's earlier bug fix (a Gemini error body once crashed `SaveChangesAsync` with a SQL truncation error) stays load-bearing for Hugging Face error bodies too.
- **SignalR push is best-effort and never fails the underlying write** — matches `OrderPlacedNotificationHandler`'s exact "persist-then-push, swallow push failures" pattern.
- **⚠️ OPEN QUESTION carried into Task 2 — the exact Hugging Face Space API shape is unverified.** This plan can't be tested against a live Space (none exists yet — you need to sign up and duplicate one first). Task 2 is written against the current, documented Gradio 4.x/5.x "queue" protocol (upload → submit → SSE poll), but **every duplicated Space auto-generates its own exact API reference** (a "Use via API" panel showing the real endpoint names and payload shape for that specific Space). Task 2's first step is reading that panel and confirming/adjusting the client against it — do not skip this and assume the plan's payload shapes are exactly right.
- **Never commit** unless the human explicitly asks. Steps below end with a `git commit` command; run it only when the human has authorized committing for this run.

---

## File Structure

**New files — try-on microservice**

| Path | Responsibility |
|---|---|
| `src/FashionSaaS.TryOn.Application/HuggingFace/HuggingFaceSettings.cs` | Options POCO: `SpaceUrl`, `ApiToken` |
| `src/FashionSaaS.TryOn.Application/HuggingFace/IHuggingFaceTryOnClient.cs` | The Space-swap-point interface |
| `src/FashionSaaS.TryOn.Infrastructure/HuggingFace/HuggingFaceTryOnClient.cs` | Hand-rolled `HttpClient` + SSE implementation |
| `src/FashionSaaS.TryOn.Infrastructure/BackgroundJobs/TryOnPollingWorker.cs` | Polls `Processing` rows, applies the 10-minute timeout |
| `tests/FashionSaaS.TryOn.Infrastructure.Tests/HuggingFace/HuggingFaceTryOnClientTests.cs` | Task 2 tests |
| `tests/FashionSaaS.TryOn.Infrastructure.Tests/BackgroundJobs/TryOnPollingWorkerTests.cs` | Task 5 tests |

**Modified files — try-on microservice**

| Path | Change |
|---|---|
| `src/FashionSaaS.TryOn.Domain/TryOnStatus.cs` | Add `Processing` |
| `src/FashionSaaS.TryOn.Domain/TryOnRequest.cs` | Add `ExternalJobId`, `ResultImageUrl` |
| `src/FashionSaaS.TryOn.Application/Messaging/ITryOnEventPublisher.cs` | Generalize to `TryOnResultEvent` |
| `src/FashionSaaS.TryOn.Application/Messaging/TryOnCompletedEvent.cs` | Replaced by new `TryOnResultEvent.cs` |
| `src/FashionSaaS.TryOn.Infrastructure/Messaging/ServiceBusTryOnEventPublisher.cs` | Signature update only |
| `src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs` | `RenderAsync` → `SubmitAsync`; new `CompletePollingAsync`/`FailPollingAsync`; drop Gemini image call entirely |
| `src/FashionSaaS.TryOn.Application/TryOn/TryOnResultResponse.cs` | Replaced by `TryOnSubmittedResponse` (submit) + a new status DTO (Task 7) |
| `src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs` | `PostAsync` returns `202`; new `GetAsync(id)` |
| `src/FashionSaaS.TryOn.Api/Program.cs` | Drop `IGeminiImageClient` registration; add `HuggingFaceSettings` binding + `HttpClient` registration; `AddHostedService<TryOnPollingWorker>()` |
| `src/FashionSaaS.TryOn.Api/appsettings.Development.json` | Add `HuggingFaceSettings` section |
| `src/FashionSaaS.TryOn.Infrastructure/Persistence/Migrations/` | One new migration |
| `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs` | Rewritten for `SubmitAsync`; Gemini-specific tests removed |

**Deleted files — try-on microservice** (dead once the image-generation call is gone — confirmed `TryOnService` is their only consumer)

| Path |
|---|
| `src/FashionSaaS.TryOn.Application/Gemini/IGeminiImageClient.cs` |
| `src/FashionSaaS.TryOn.Application/Gemini/GeminiDtos.cs` |

`GeminiSettings.cs` itself is **not** deleted — `TextModel`, `ApiKey`, `BaseUrl`, `AllowedGarmentImageHosts` are still used by Chat, Measurement, and `TryOnRequestFormValidator`. Only the now-dead `Model` property (image-model name, used exclusively at the deleted Gemini call site) is removed from it.

**New files — main API**

| Path | Responsibility |
|---|---|
| `src/FashionSaaS.Application/Configuration/ServiceBusSettings.cs` | New — main API has no Service Bus config today |
| `src/FashionSaaS.API/BackgroundJobs/TryOnResultConsumer.cs` | The new consumer — none exists in this repo today |
| `tests/FashionSaaS.API.Tests/BackgroundJobs/TryOnResultConsumerTests.cs` | Task 6 tests |

**Modified files — main API**

| Path | Change |
|---|---|
| `src/FashionSaaS.Domain/Enums/NotificationType.cs` | Add `TryOnCompleted`, `TryOnFailed` |
| `src/FashionSaaS.API/Program.cs` | Bind `ServiceBusSettings`; register `ServiceBusClient`; `AddHostedService<TryOnResultConsumer>()` |
| `src/FashionSaaS.API/appsettings.Development.json` | Add `ServiceBusSettings` section |
| `services/fashionsaas-tryon/servicebus-emulator-config.json` | Add a subscription entry on the existing `tryon-events` topic |

**Modified files — storefront**

| Path | Change |
|---|---|
| `fashionsaas-storefront/src/app/features/catalog/models/try-on.model.ts` | New submit/status response shapes |
| `fashionsaas-storefront/src/app/features/catalog/services/try-on.service.ts` | `render()` → `submit()` + new `getResult()` |
| `fashionsaas-storefront/src/app/admin/notifications/models/notification.model.ts` | Extend `NotificationTypeName` |
| `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.ts` | Async submit, processing state, SignalR-driven completion |
| `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.html` | Processing-state markup; result rendered from a URL, not a data URI |

---

## Task 1: Schema — `Processing` status, job tracking, result URL

**Files:**
- Modify: `src/FashionSaaS.TryOn.Domain/TryOnStatus.cs`
- Modify: `src/FashionSaaS.TryOn.Domain/TryOnRequest.cs`
- Test: `tests/FashionSaaS.TryOn.Domain.Tests/TryOnStatusTests.cs` (new, tiny)

**Interfaces:**
- Consumes: nothing.
- Produces: `TryOnStatus.Processing`; `TryOnRequest.ExternalJobId` (`string?`); `TryOnRequest.ResultImageUrl` (`string?`).

- [x] **Step 1: Write the failing test**

Create `tests/FashionSaaS.TryOn.Domain.Tests/TryOnStatusTests.cs`:

```csharp
using FashionSaaS.TryOn.Domain;
using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class TryOnStatusTests
{
    [Fact]
    public void TryOnStatus_HasProcessingValue()
    {
        Enum.IsDefined(typeof(TryOnStatus), "Processing").Should().BeTrue();
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Domain.Tests --filter TryOnStatusTests`
Expected: FAIL — `Processing` is not defined.

- [x] **Step 3: Add the enum value**

In `src/FashionSaaS.TryOn.Domain/TryOnStatus.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
namespace FashionSaaS.TryOn.Domain;

public enum TryOnStatus
{
    Completed,
    Failed,
    Processing
}
```

> Adding `Processing` last (not alphabetically/logically first) preserves the existing `0`/`1` integer values for `Completed`/`Failed` already stored in the database — inserting it earlier would silently reinterpret every existing row.

- [x] **Step 4: Add the two new columns**

In `src/FashionSaaS.TryOn.Domain/TryOnRequest.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
namespace FashionSaaS.TryOn.Domain;

public class TryOnRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public TryOnStatus Status { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>The Hugging Face job/event id this request is waiting on. Null once terminal.</summary>
    public string? ExternalJobId { get; set; }

    /// <summary>The finished image's Hugging Face-served URL. Set only when Status is Completed.</summary>
    public string? ResultImageUrl { get; set; }
}
```

- [x] **Step 5: Run the test to verify it passes**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Domain.Tests --filter TryOnStatusTests`
Expected: PASS.

- [x] **Step 6: Add the EF configuration for the two new columns**

Find the try-on service's `TryOnRequestConfiguration` (the file with `HasMaxLength(500)` on `FailureReason` — read it first to confirm current content), then **via Serena `replace_content`** add, alongside the existing property mappings:

```csharp
        builder.Property(r => r.ExternalJobId).HasMaxLength(200);
        builder.Property(r => r.ResultImageUrl).HasMaxLength(2000);
```

- [x] **Step 7: Generate and apply the migration**

```bash
dotnet ef migrations add AddTryOnProcessingState --project services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure --startup-project services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api
```

Expected: a new migration under `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Persistence/Migrations/`. Open it and confirm it contains `AddColumn<string>(name: "ExternalJobId", table: "TryOnRequests", maxLength: 200, nullable: true)` and the same for `ResultImageUrl` (maxLength 2000) — **no column for `Status` itself changes**, since adding an enum member doesn't alter the underlying `int` column.

```bash
dotnet ef database update --project services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure --startup-project services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api
```

Expected: `Done.`

- [x] **Step 8: Verify schema and full build**

```bash
sqlcmd -S localhost -U sa -P 12345678 -C -d TryOnDb -Q "SET NOCOUNT ON; SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='TryOnRequests' ORDER BY COLUMN_NAME" -W
```

Expected columns include `ExternalJobId` and `ResultImageUrl` alongside the existing ones.

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on the two modified domain files and the configuration file. Expected: no diagnostics.

- [x] **Step 9: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Domain/ services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Persistence/ services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Domain.Tests/TryOnStatusTests.cs
git commit -m "feat(tryon): add Processing status, job tracking, result URL to TryOnRequest"
```

---

## Task 2: Hugging Face client — the Space swap point

⚠️ **Before writing production code in this task, complete Step 0.** Everything after it depends on the real payload shape of your actual duplicated Space, which this plan cannot see in advance.

**Files:**
- Create: `src/FashionSaaS.TryOn.Application/HuggingFace/HuggingFaceSettings.cs`
- Create: `src/FashionSaaS.TryOn.Application/HuggingFace/IHuggingFaceTryOnClient.cs`
- Create: `src/FashionSaaS.TryOn.Infrastructure/HuggingFace/HuggingFaceTryOnClient.cs`
- Test: `tests/FashionSaaS.TryOn.Infrastructure.Tests/HuggingFace/HuggingFaceTryOnClientTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `IHuggingFaceTryOnClient.SubmitAsync(byte[] personPhoto, byte[] garmentImage, CancellationToken ct) : Task<string>` — returns the job/event id.
  - `IHuggingFaceTryOnClient.PollAsync(string jobId, CancellationToken ct) : Task<HuggingFaceJobResult>`
  - `record HuggingFaceJobResult(HuggingFaceJobState State, string? ResultImageUrl, string? ErrorMessage);`
  - `enum HuggingFaceJobState { Pending, Complete, Failed }`

- [ ] **Step 0: Confirm the real API shape (you do this, not the implementer alone)**  — ⛔ **NOT DONE / BLOCKED:** no duplicated Space exists yet, so the Gradio protocol below is written but UNVERIFIED.

Once you've duplicated a Space (e.g. Kolors-Virtual-Try-On) to your own Hugging Face account:
1. Open your duplicated Space → find its "Use via API" or "API" panel (every Gradio Space has one).
2. Note the exact `api_name` for the try-on prediction function (commonly something like `/tryon` or `/generate_image` — varies by Space).
3. Note whether inputs are images given directly as base64/`FileData` objects in the submit call, or whether the Space requires a separate `POST {spaceUrl}/upload` step first (most current Gradio Spaces with `gr.Image` inputs use the upload step — confirm this for yours specifically).
4. Paste that panel's exact curl/Python example into a scratch file so Step 3 below can be adjusted to match it exactly instead of the best-known-default shape given here.

The code below assumes the common current pattern (upload-then-submit, SSE poll) — Step 3 is written against it, but if your Space's real API differs, adjust `HuggingFaceTryOnClient`'s request-building before proceeding; everything from Task 3 onward only depends on the interface (`SubmitAsync`/`PollAsync`), not this internal shape.

- [x] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.TryOn.Infrastructure.Tests/HuggingFace/HuggingFaceTryOnClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Infrastructure.HuggingFace;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Tests.HuggingFace;

public class HuggingFaceTryOnClientTests
{
    private static HuggingFaceTryOnClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test-space.hf.space") },
            Options.Create(new HuggingFaceSettings { SpaceUrl = "https://test-space.hf.space", ApiToken = "test-token" }),
            NullLogger<HuggingFaceTryOnClient>.Instance);

    [Fact]
    public async Task SubmitAsync_UploadsBothImagesThenSubmits_ReturnsEventId()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[\"/tmp/person.jpg\"]") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[\"/tmp/garment.jpg\"]") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"event_id\":\"evt-123\"}") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        var jobId = await client.SubmitAsync([1, 2, 3], [4, 5, 6], CancellationToken.None);

        jobId.Should().Be("evt-123");
        handler.Requests.Should().HaveCount(3);
        handler.Requests[2].RequestUri!.PathAndQuery.Should().Contain("/call/");
    }

    [Fact]
    public async Task PollAsync_SseCompleteEvent_ReturnsCompleteWithResultUrl()
    {
        const string sse = "event: complete\ndata: [{\"path\": \"https://test-space.hf.space/file=/tmp/result.png\"}]\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Complete);
        result.ResultImageUrl.Should().Be("https://test-space.hf.space/file=/tmp/result.png");
    }

    [Fact]
    public async Task PollAsync_SseErrorEvent_ReturnsFailedWithMessage()
    {
        const string sse = "event: error\ndata: \"CUDA out of memory\"\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Failed);
        result.ErrorMessage.Should().Contain("CUDA out of memory");
    }

    [Fact]
    public async Task PollAsync_NoTerminalEventYet_ReturnsPending()
    {
        const string sse = "event: generating\ndata: {\"progress\": 0.4}\n\n";
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Pending);
    }

    [Fact]
    public async Task PollAsync_ConnectionDrops_ReturnsPendingNotThrow()
    {
        var handler = new SequenceHandler(new HttpRequestException("connection reset"));

        HuggingFaceTryOnClient client = CreateClient(handler);

        HuggingFaceJobResult result = await client.PollAsync("evt-123", CancellationToken.None);

        result.State.Should().Be(HuggingFaceJobState.Pending);
    }
}

/// <summary>Replays a fixed sequence of responses (or throws) per call, in order — one per SendAsync.</summary>
internal sealed class SequenceHandler : HttpMessageHandler
{
    private readonly Queue<object> _queue;
    public List<HttpRequestMessage> Requests { get; } = [];

    public SequenceHandler(params object[] responsesOrExceptions) => _queue = new Queue<object>(responsesOrExceptions);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var next = _queue.Count > 0 ? _queue.Dequeue() : throw new InvalidOperationException("No more queued responses.");
        if (next is Exception ex) throw ex;
        return Task.FromResult((HttpResponseMessage)next);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter HuggingFaceTryOnClientTests`
Expected: FAIL — build error, the types don't exist yet.

- [x] **Step 3: Create the settings class**

Create `src/FashionSaaS.TryOn.Application/HuggingFace/HuggingFaceSettings.cs` **via Serena `create_text_file`**:

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.HuggingFace;

public class HuggingFaceSettings
{
    public const string SectionName = "HuggingFaceSettings";

    /// <summary>Base URL of your own duplicated Space, e.g. https://your-username-your-space.hf.space</summary>
    [Required]
    public string SpaceUrl { get; init; } = string.Empty;

    [Required]
    public string ApiToken { get; init; } = string.Empty;
}
```

- [x] **Step 4: Create the interface and result types**

Create `src/FashionSaaS.TryOn.Application/HuggingFace/IHuggingFaceTryOnClient.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.TryOn.Application.HuggingFace;

public enum HuggingFaceJobState
{
    Pending,
    Complete,
    Failed
}

public record HuggingFaceJobResult(HuggingFaceJobState State, string? ResultImageUrl, string? ErrorMessage);

/// <summary>
/// Talks to your duplicated Hugging Face Space. Not a Refit interface — Gradio's queue API is
/// job-based (submit, then poll an SSE stream), which Refit doesn't model. This is the ONLY
/// abstraction the rest of the try-on flow depends on; if you switch Spaces or providers later,
/// only the Infrastructure implementation changes.
/// </summary>
public interface IHuggingFaceTryOnClient
{
    /// <summary>Submits a render job. Returns the Space's job/event id.</summary>
    Task<string> SubmitAsync(byte[] personPhoto, byte[] garmentImage, CancellationToken ct);

    /// <summary>
    /// Checks a job's current state. Returns Pending (not an exception) for both "still
    /// rendering" and "transient connection problem" — the caller polls again either way.
    /// </summary>
    Task<HuggingFaceJobResult> PollAsync(string jobId, CancellationToken ct);
}
```

- [x] **Step 5: Implement the client**

Create `src/FashionSaaS.TryOn.Infrastructure/HuggingFace/HuggingFaceTryOnClient.cs` **via Serena `create_text_file`**:

```csharp
using System.Text;
using System.Text.Json;
using FashionSaaS.TryOn.Application.HuggingFace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.HuggingFace;

/// <summary>
/// Hand-rolled Gradio queue client — not Refit, since Refit can't model an SSE poll response.
/// Verify this against your actual duplicated Space's "Use via API" panel (plan Task 2, Step 0)
/// before trusting it in production; the upload-then-submit shape here is the current common
/// Gradio 4.x/5.x pattern, not something this plan could test live.
/// </summary>
public class HuggingFaceTryOnClient : IHuggingFaceTryOnClient
{
    private const string PredictApiName = "tryon"; // CONFIRM against your Space's real api_name (Task 2, Step 0)

    private readonly HttpClient _http;
    private readonly string _spaceUrl;
    private readonly ILogger<HuggingFaceTryOnClient> _logger;

    public HuggingFaceTryOnClient(HttpClient http, IOptions<HuggingFaceSettings> settings, ILogger<HuggingFaceTryOnClient> logger)
    {
        _http = http;
        _spaceUrl = settings.Value.SpaceUrl.TrimEnd('/');
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Value.ApiToken);
        _logger = logger;
    }

    public async Task<string> SubmitAsync(byte[] personPhoto, byte[] garmentImage, CancellationToken ct)
    {
        var personPath = await UploadAsync(personPhoto, "person.jpg", ct);
        var garmentPath = await UploadAsync(garmentImage, "garment.jpg", ct);

        var payload = new
        {
            data = new object[]
            {
                new { path = personPath, meta = new { _type = "gradio.FileData" } },
                new { path = garmentPath, meta = new { _type = "gradio.FileData" } }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync($"{_spaceUrl}/call/{PredictApiName}", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("event_id").GetString()
            ?? throw new InvalidOperationException("Hugging Face submit response had no event_id.");
    }

    private async Task<string> UploadAsync(byte[] imageBytes, string fileName, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(imageBytes);
        content.Add(fileContent, "files", fileName);

        using HttpResponseMessage response = await _http.PostAsync($"{_spaceUrl}/upload", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement[0].GetString()
            ?? throw new InvalidOperationException("Hugging Face upload response had no file path.");
    }

    public async Task<HuggingFaceJobResult> PollAsync(string jobId, CancellationToken ct)
    {
        // Any transient failure (dropped connection, timeout) is reported as Pending, never
        // thrown — the caller (TryOnPollingWorker) just tries again on its next tick, and the
        // 10-minute overall timeout (enforced by the worker, not here) is what actually gives up.
#pragma warning disable CA1031
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{_spaceUrl}/call/{PredictApiName}/{jobId}");
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using StreamReader reader = new(stream);

            string? currentEvent = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    currentEvent = line["event: ".Length..].Trim();
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal) && currentEvent is not null)
                {
                    var data = line["data: ".Length..];

                    if (currentEvent == "complete")
                    {
                        using JsonDocument doc = JsonDocument.Parse(data);
                        var resultUrl = doc.RootElement[0].GetProperty("path").GetString();
                        return new HuggingFaceJobResult(HuggingFaceJobState.Complete, resultUrl, null);
                    }

                    if (currentEvent == "error")
                    {
                        return new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, data.Trim('"'));
                    }
                }
            }

            return new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex, "Transient error polling Hugging Face job {JobId}; will retry", jobId);
            return new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null);
        }
#pragma warning restore CA1031
    }
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter HuggingFaceTryOnClientTests`
Expected: PASS — 5 passed, 0 failed.

- [x] **Step 7: Run the full verification gate**

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on the three new files. Expected: no diagnostics.

- [x] **Step 8: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/HuggingFace/ services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/HuggingFace/ services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests/HuggingFace/
git commit -m "feat(tryon): add Hugging Face Space client (submit + SSE poll)"
```

---

## Task 3: Generalize the Service Bus event to carry both outcomes

**Files:**
- Modify: `src/FashionSaaS.TryOn.Application/Messaging/ITryOnEventPublisher.cs`
- Delete: `src/FashionSaaS.TryOn.Application/Messaging/TryOnCompletedEvent.cs`
- Create: `src/FashionSaaS.TryOn.Application/Messaging/TryOnResultEvent.cs`
- Modify: `src/FashionSaaS.TryOn.Infrastructure/Messaging/ServiceBusTryOnEventPublisher.cs`
- Modify: `services/fashionsaas-tryon/servicebus-emulator-config.json`
- Test: `tests/FashionSaaS.TryOn.Infrastructure.Tests/Messaging/ServiceBusTryOnEventPublisherTests.cs` (already exists — update it)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `record TryOnResultEvent(Guid TryOnRequestId, Guid TenantId, Guid CustomerId, Guid ProductId, DateTime CreatedAt, bool IsSuccess, string? ResultImageUrl, string? FailureReason);` and `ITryOnEventPublisher.PublishAsync(TryOnResultEvent, CancellationToken)`.

- [x] **Step 1: Replace the event type**

Delete `src/FashionSaaS.TryOn.Application/Messaging/TryOnCompletedEvent.cs` and create `src/FashionSaaS.TryOn.Application/Messaging/TryOnResultEvent.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.TryOn.Application.Messaging;

/// <summary>
/// Published exactly once per TryOnRequest, on EITHER outcome (unlike the old success-only
/// TryOnCompletedEvent) — the main API's consumer needs to notify the customer of a failure
/// too, not just a success.
/// </summary>
public record TryOnResultEvent(
    Guid TryOnRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid ProductId,
    DateTime CreatedAt,
    bool IsSuccess,
    string? ResultImageUrl,
    string? FailureReason);
```

- [x] **Step 2: Update the publisher interface**

In `src/FashionSaaS.TryOn.Application/Messaging/ITryOnEventPublisher.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
namespace FashionSaaS.TryOn.Application.Messaging;

public interface ITryOnEventPublisher
{
    /// <summary>
    /// Publishes a try-on result (success or failure). Implementations must never throw — a
    /// messaging outage must not fail the underlying try-on request.
    /// </summary>
    Task PublishAsync(TryOnResultEvent @event, CancellationToken cancellationToken);
}
```

- [x] **Step 3: Update the Service Bus implementation**

In `src/FashionSaaS.TryOn.Infrastructure/Messaging/ServiceBusTryOnEventPublisher.cs` **via Serena `replace_regex`**, change every `TryOnCompletedEvent` reference to `TryOnResultEvent` (the parameter type in `PublishAsync` and the log message's interpolated type, if any) — the rest of the class (sender-per-call pattern, bare catch swallowing all exceptions) is unchanged.

- [x] **Step 4: Add the new Service Bus subscription for the main API**

In `services/fashionsaas-tryon/servicebus-emulator-config.json`, add a subscription entry to the existing `tryon-events` topic's currently-empty `"Subscriptions": []` array (native Edit — this is JSON, not `.cs`):

```json
            "Subscriptions": [
              {
                "Name": "main-api-tryon-results",
                "Properties": {
                  "DefaultMessageTimeToLive": "PT1H",
                  "LockDuration": "PT30S",
                  "MaxDeliveryCount": 5
                }
              }
            ]
```

> `docker-compose.yml` already bind-mounts this exact file (`./services/fashionsaas-tryon/servicebus-emulator-config.json:/ServiceBus_Emulator/ConfigFiles/Config.json`) — no compose changes needed, just this file.

- [x] **Step 5: Update the existing publisher test**

Read `tests/FashionSaaS.TryOn.Infrastructure.Tests/Messaging/ServiceBusTryOnEventPublisherTests.cs` first, then update every `TryOnCompletedEvent` construction to `TryOnResultEvent`, filling the four new fields with representative values (e.g. `IsSuccess: true, ResultImageUrl: "https://example.hf.space/file=result.png", FailureReason: null` for a success-path test).

- [x] **Step 6: Run the tests, then the full verification gate**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter ServiceBusTryOnEventPublisherTests`
Expected: PASS.

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: this will FAIL — `TryOnService.cs` still constructs the old `TryOnCompletedEvent`. **That is expected**; Task 4 fixes it. Confirm the only errors are `TryOnCompletedEvent`-related in `TryOnService.cs`.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every file this task touched.

- [x] **Step 7: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Messaging/ services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Messaging/ServiceBusTryOnEventPublisher.cs services/fashionsaas-tryon/servicebus-emulator-config.json services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests/Messaging/ServiceBusTryOnEventPublisherTests.cs
git commit -m "feat(tryon): generalize TryOnCompletedEvent to TryOnResultEvent (success or failure)"
```

Note: this commit deliberately leaves the solution non-building — Task 4 fixes `TryOnService.cs`, the only other consumer.

---

## Task 4: `TryOnService` — submit to Hugging Face instead of Gemini

**Files:**
- Modify: `src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs`
- Delete: `src/FashionSaaS.TryOn.Application/Gemini/IGeminiImageClient.cs`
- Delete: `src/FashionSaaS.TryOn.Application/Gemini/GeminiDtos.cs`
- Modify: `src/FashionSaaS.TryOn.Application/Gemini/GeminiSettings.cs` (drop `Model`)
- Create: `src/FashionSaaS.TryOn.Application/TryOn/TryOnSubmittedResponse.cs`
- Delete: `src/FashionSaaS.TryOn.Application/TryOn/TryOnResultResponse.cs`
- Modify: `src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs`
- Modify: `src/FashionSaaS.TryOn.Api/Program.cs`
- Modify: `src/FashionSaaS.TryOn.Api/appsettings.Development.json`
- Modify: `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs`

**Interfaces:**
- Consumes: `IHuggingFaceTryOnClient` (Task 2), `TryOnResultEvent`/`ITryOnEventPublisher` (Task 3), `TryOnStatus.Processing`/`ExternalJobId` (Task 1).
- Produces: `TryOnService.SubmitAsync(TryOnRequestForm form, CancellationToken ct) : Task<(bool IsSuccess, int StatusCode, string Message, TryOnSubmittedResponse? Data)>` — same shape as today's `RenderAsync`, renamed, with a new response type carrying only `Guid RequestId`.

- [x] **Step 1: Write the failing tests**

Read the current `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs` in full (quoted above in this plan's research) before editing — you are REPLACING it, not appending, since every test currently drives the now-removed Gemini call path. Replace the whole file:

```csharp
using System.Net;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Messaging;
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

        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => dbContext.TryOnRequests.Count(t => t.TenantId == _tenantId && t.Status != TryOnStatus.Failed));

#pragma warning disable CA2000
        HttpMessageHandler handler = garmentHandler ?? new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
#pragma warning restore CA2000

        return new TryOnService(dbContext, _context.Object, _huggingFace.Object, factory.Object, _eventPublisher.Object, _usageQuota.Object);
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

        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<TryOnResultEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "no event is published at submit time - only once the poller resolves the job");
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
#pragma warning disable CA2000
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
}

internal sealed class StubHandler(HttpStatusCode statusCode, byte[] body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = new(statusCode) { Content = new ByteArrayContent(body) };
        if (statusCode != HttpStatusCode.OK)
        {
            response.EnsureSuccessStatusCode();
        }
        return Task.FromResult(response);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests --filter TryOnServiceTests`
Expected: FAIL — build errors (`TryOnSubmittedResponse`, new constructor shape, `SubmitAsync` don't exist yet).

- [x] **Step 3: Create the new response DTO, delete the old one**

Delete `src/FashionSaaS.TryOn.Application/TryOn/TryOnResultResponse.cs`. Create `src/FashionSaaS.TryOn.Application/TryOn/TryOnSubmittedResponse.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.TryOn.Application.TryOn;

public record TryOnSubmittedResponse(Guid RequestId);
```

- [x] **Step 4: Delete the dead Gemini image types, trim `GeminiSettings`**

Delete `src/FashionSaaS.TryOn.Application/Gemini/IGeminiImageClient.cs` and `src/FashionSaaS.TryOn.Application/Gemini/GeminiDtos.cs` entirely — confirmed their only consumer was `TryOnService`, which this task rewrites.

In `src/FashionSaaS.TryOn.Application/Gemini/GeminiSettings.cs` **via Serena `replace_regex`**, remove the `Model` property (image-model name) and its doc comment — `ApiKey`, `BaseUrl`, `TextModel`, `ChatHistoryMaxTotalChars`, `AllowedGarmentImageHosts` all stay; they're used by Chat, Measurement, and `TryOnRequestFormValidator`.

- [x] **Step 5: Rewrite `TryOnService`**

In `src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;

// This type lives in Infrastructure (not Application) because it depends on the concrete
// TryOnDbContext — see the original placement rationale comment history for why an Application
// -> Infrastructure reference would be circular.
namespace FashionSaaS.TryOn.Infrastructure.TryOn;

public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IHuggingFaceTryOnClient huggingFaceClient,
    IHttpClientFactory httpClientFactory,
    ITryOnEventPublisher eventPublisher,
    IUsageQuotaService usageQuotaService)
{
    // Mirrors TryOnController's [RequestSizeLimit(15_000_000)] on the inbound photo upload,
    // applied here to the server-side garment-image fetch so a malicious/misbehaving host can't
    // force an unbounded download.
    private const long MaxGarmentImageBytes = 15_000_000;

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
            using HttpClient httpClient = httpClientFactory.CreateClient();
            using HttpResponseMessage garmentResponse = await httpClient
                .GetAsync(new Uri(form.GarmentImageUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            garmentResponse.EnsureSuccessStatusCode();

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
```

> `RecordFailureAsync` replaces the old `RecordAsync(form, status, reason, ct)` — it's now failure-only, since the success path no longer records a terminal row itself (that happens later, in `TryOnPollingWorker`, Task 5).

- [x] **Step 6: Update the controller**

In `src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Infrastructure.TryOn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/tryon")]
[Authorize]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class TryOnController(TryOnService tryOnService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PostAsync([FromForm] TryOnRequestForm form, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, TryOnSubmittedResponse? data) = await tryOnService.SubmitAsync(form, cancellationToken);

        ResponseData<TryOnSubmittedResponse> response = isSuccess
            ? ResponseData<TryOnSubmittedResponse>.Success(data!, message, statusCode)
            : ResponseData<TryOnSubmittedResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
```

> The `GetAsync(id)` status-check endpoint is Task 7 — added once the polling worker (Task 5) actually populates a terminal result to fetch.

- [x] **Step 7: Update `Program.cs`**

In `src/FashionSaaS.TryOn.Api/Program.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.HuggingFace;
using FashionSaaS.TryOn.Infrastructure;
using FashionSaaS.TryOn.Infrastructure.BackgroundJobs;
using FashionSaaS.TryOn.Infrastructure.HuggingFace;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddTryOnInfrastructure(builder.Configuration);
builder.Services.AddTryOnAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddOptions<GeminiSettings>()
    .Bind(builder.Configuration.GetSection(GeminiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HuggingFaceSettings>()
    .Bind(builder.Configuration.GetSection(HuggingFaceSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRefitClient<IGeminiTextClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        GeminiSettings settings = sp.GetRequiredService<IOptions<GeminiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
    });

builder.Services.AddHttpClient<IHuggingFaceTryOnClient, HuggingFaceTryOnClient>((sp, client) =>
{
    HuggingFaceSettings settings = sp.GetRequiredService<IOptions<HuggingFaceSettings>>().Value;
    client.BaseAddress = new Uri(settings.SpaceUrl);
    // Free-tier CPU rendering can genuinely take minutes; the default 100s HttpClient timeout
    // would abort a slow-but-successful poll response.
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHostedService<TryOnPollingWorker>();

builder.Services.AddHttpClient(); // plain named client for the garment-image GET

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(FashionSaaS.TryOn.Application.TryOn.TryOnRequestFormValidator).Assembly);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FashionSaaS.TryOn API", Version = "v1" });
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

public partial class Program
{
    private Program()
    {
    }
}
```

> Note `IGeminiImageClient`'s `AddRefitClient` registration is gone (that interface no longer exists); `IGeminiTextClient`'s stays (Chat/Measurement still use it). `TryOnPollingWorker` (Task 5) doesn't exist yet — this step's build will fail until Task 5 lands; that's expected, same pattern as Task 3→4.

- [x] **Step 8: Add the config section**

In `src/FashionSaaS.TryOn.Api/appsettings.Development.json`, add (native Edit — JSON, not `.cs`):

```json
  "HuggingFaceSettings": {
    "SpaceUrl": "https://your-username-your-space.hf.space",
    "ApiToken": "REPLACE-WITH-YOUR-HUGGING-FACE-TOKEN"
  },
```

> These are placeholder values — you must replace both once you've duplicated your Space and generated an HF access token, or the app will fail `ValidateOnStart()` with a clear error rather than silently misbehaving.

- [x] **Step 9: Run tests, expect the known Task-5-shaped failure**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests --filter TryOnServiceTests`
Expected: PASS — 4 passed, 0 failed (this test file doesn't touch `Program.cs`, so it's unaffected by the `TryOnPollingWorker` reference).

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: FAILS — `Program.cs` references `TryOnPollingWorker`, which doesn't exist until Task 5. Confirm that's the *only* error.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every `.cs` file this task touched (not `Program.cs`, since it's expected to have the one known error).

- [x] **Step 10: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/ services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Program.cs services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs
git commit -m "feat(tryon): submit to Hugging Face instead of Gemini, return 202 immediately"
```

---

## Task 5: The polling worker — restores the build

**Files:**
- Create: `src/FashionSaaS.TryOn.Infrastructure/BackgroundJobs/TryOnPollingWorker.cs`
- Test: `tests/FashionSaaS.TryOn.Infrastructure.Tests/BackgroundJobs/TryOnPollingWorkerTests.cs`

**Interfaces:**
- Consumes: `IHuggingFaceTryOnClient.PollAsync` (Task 2), `TryOnResultEvent`/`ITryOnEventPublisher` (Task 3), `TryOnStatus.Processing`/`ExternalJobId`/`ResultImageUrl` (Task 1).
- Produces: nothing new consumed elsewhere — this is the terminal piece that makes `Processing` rows resolve.

- [x] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.TryOn.Infrastructure.Tests/BackgroundJobs/TryOnPollingWorkerTests.cs`:

```csharp
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

    private static (TryOnDbContext DbContext, IServiceScopeFactory ScopeFactory) CreateScopedDbContext(
        Mock<IHuggingFaceTryOnClient> huggingFace, Mock<ITryOnEventPublisher> eventPublisher)
    {
        var dbName = Guid.NewGuid().ToString();
        ServiceCollection services = new();
        services.AddDbContext<TryOnDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(huggingFace.Object);
        services.AddSingleton(eventPublisher.Object);
        ServiceProvider provider = services.BuildServiceProvider();

        var dbContext = new TryOnDbContext(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(dbName).Options);
        return (dbContext, provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task RunOnceAsync_JobStillPending_LeavesRowUnchanged()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-1" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null));

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Processing);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<TryOnResultEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_JobComplete_UpdatesRowAndPublishesSuccessEvent()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-2" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Complete, "https://space.hf.space/file=result.png", null));

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Completed);
        reloaded.ResultImageUrl.Should().Be("https://space.hf.space/file=result.png");

        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnResultEvent>(e => e.TryOnRequestId == request.Id && e.IsSuccess && e.ResultImageUrl == "https://space.hf.space/file=result.png"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_JobFailed_UpdatesRowAndPublishesFailureEvent()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-3" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, "CUDA out of memory"));

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Failed);
        reloaded.FailureReason.Should().Be("CUDA out of memory");

        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnResultEvent>(e => !e.IsSuccess && e.FailureReason == "CUDA out of memory"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_OversizedFailureReason_TruncatesTo500Chars()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        var request = new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-4" };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        var oversized = new string('x', 800);
        _huggingFace.Setup(h => h.PollAsync("evt-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Failed, null, oversized));

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.SingleAsync(t => t.Id == request.Id);
        reloaded.FailureReason!.Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task RunOnceAsync_ProcessingPastTenMinutes_ForceFailsWithTimeoutReason()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        var request = new TryOnRequest
        {
            TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = TryOnStatus.Processing, ExternalJobId = "evt-5",
            CreatedAt = DateTime.UtcNow.AddMinutes(-11)
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        _huggingFace.Setup(h => h.PollAsync("evt-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HuggingFaceJobResult(HuggingFaceJobState.Pending, null, null));

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        TryOnRequest reloaded = await dbContext.TryOnRequests.SingleAsync(t => t.Id == request.Id);
        reloaded.Status.Should().Be(TryOnStatus.Failed);
        reloaded.FailureReason.Should().Be("Try-on render timed out.");

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<TryOnResultEvent>(e => !e.IsSuccess), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_NoProcessingRows_DoesNothing()
    {
        (TryOnDbContext dbContext, IServiceScopeFactory scopeFactory) = CreateScopedDbContext(_huggingFace, _eventPublisher);
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = Guid.NewGuid(), Status = TryOnStatus.Completed });
        await dbContext.SaveChangesAsync();

        TryOnPollingWorker worker = new(scopeFactory, NullLogger<TryOnPollingWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        _huggingFace.Verify(h => h.PollAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter TryOnPollingWorkerTests`
Expected: FAIL — `TryOnPollingWorker` doesn't exist.

- [x] **Step 3: Implement the worker**

Create `src/FashionSaaS.TryOn.Infrastructure/BackgroundJobs/TryOnPollingWorker.cs` **via Serena `create_text_file`**:

```csharp
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
        using var timer = new PeriodicTimer(PollInterval);
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

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TryOnDbContext>();
        var huggingFaceClient = scope.ServiceProvider.GetRequiredService<IHuggingFaceTryOnClient>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<ITryOnEventPublisher>();

        List<TryOnRequest> processing = await dbContext.TryOnRequests
            .Where(t => t.Status == TryOnStatus.Processing)
            .ToListAsync(ct);

        foreach (TryOnRequest request in processing)
        {
            if (DateTime.UtcNow - request.CreatedAt > ProcessingTimeout)
            {
                await FailAsync(dbContext, eventPublisher, request, "Try-on render timed out.", ct);
                continue;
            }

            HuggingFaceJobResult result = await huggingFaceClient.PollAsync(request.ExternalJobId!, ct);

            switch (result.State)
            {
                case HuggingFaceJobState.Complete:
                    await CompleteAsync(dbContext, eventPublisher, request, result.ResultImageUrl!, ct);
                    break;
                case HuggingFaceJobState.Failed:
                    await FailAsync(dbContext, eventPublisher, request, result.ErrorMessage ?? "Hugging Face render failed.", ct);
                    break;
                case HuggingFaceJobState.Pending:
                    break; // leave it Processing, try again next tick
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result.State, "Unknown HuggingFaceJobState");
            }
        }
    }

    private static async Task CompleteAsync(TryOnDbContext dbContext, ITryOnEventPublisher eventPublisher,
        TryOnRequest request, string resultImageUrl, CancellationToken ct)
    {
        request.Status = TryOnStatus.Completed;
        request.ResultImageUrl = resultImageUrl;
        await dbContext.SaveChangesAsync(ct);

        await eventPublisher.PublishAsync(
            new TryOnResultEvent(request.Id, request.TenantId, request.CustomerId, request.ProductId, request.CreatedAt,
                IsSuccess: true, resultImageUrl, FailureReason: null),
            ct);
    }

    private static async Task FailAsync(TryOnDbContext dbContext, ITryOnEventPublisher eventPublisher,
        TryOnRequest request, string reason, CancellationToken ct)
    {
        request.Status = TryOnStatus.Failed;
        // Same 500-char cap as TryOnService.RecordFailureAsync - an upstream error body here can
        // be arbitrarily long and would otherwise crash this exact SaveChangesAsync call.
        request.FailureReason = reason is { Length: > 500 } ? reason[..500] : reason;
        await dbContext.SaveChangesAsync(ct);

        await eventPublisher.PublishAsync(
            new TryOnResultEvent(request.Id, request.TenantId, request.CustomerId, request.ProductId, request.CreatedAt,
                IsSuccess: false, ResultImageUrl: null, request.FailureReason),
            ct);
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter TryOnPollingWorkerTests`
Expected: PASS — 6 passed, 0 failed.

- [x] **Step 5: Run the full verification gate — the build is restored**

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: `0 Warning(s) 0 Error(s)` — this is the task that restores the try-on solution's build.

Run: `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: 0 failed. Record the exact count.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every `.cs` file touched across Tasks 1-5. Expected: no diagnostics.

- [x] **Step 6: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/BackgroundJobs/ services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests/BackgroundJobs/
git commit -m "feat(tryon): add polling worker that resolves Processing jobs and publishes results"
```

---

## Task 6: Main API — consume the result event, push a live notification

**Files:**
- Create: `src/FashionSaaS.Application/Configuration/ServiceBusSettings.cs`
- Create: `src/FashionSaaS.API/BackgroundJobs/TryOnResultConsumer.cs`
- Modify: `src/FashionSaaS.Domain/Enums/NotificationType.cs`
- Modify: `src/FashionSaaS.API/Program.cs`
- Modify: `src/FashionSaaS.API/appsettings.Development.json`
- Test: `tests/FashionSaaS.API.Tests/BackgroundJobs/TryOnResultConsumerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks directly (this is a separate .NET solution/deployable — it never references the try-on microservice's assemblies). It agrees with Task 3's wire *shape* by convention: the JSON produced by `ServiceBusTryOnEventPublisher`'s default `JsonSerializer.Serialize(@event)` (no custom naming policy, so property names serialize exactly as declared: PascalCase).
- Produces: nothing consumed by later tasks in this plan.

- [x] **Step 1: Write the failing tests**

Create `tests/FashionSaaS.API.Tests/BackgroundJobs/TryOnResultConsumerTests.cs`:

```csharp
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.API.BackgroundJobs;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FashionSaaS.API.Tests.BackgroundJobs;

public class TryOnResultConsumerTests
{
    private readonly Mock<NotificationService> _notificationService;
    private readonly Mock<IHubContext<NotificationsHub>> _hubContext = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public TryOnResultConsumerTests()
    {
        // NotificationService has no interface (matches existing codebase convention - it's a
        // concrete Application-layer service registered and consumed directly), so it's mocked
        // via its public virtual-by-default members through Moq's class-mocking support; only
        // CreateAsync is exercised here.
        _notificationService = new Mock<NotificationService>(
            Mock.Of<INotificationRepository>(), Mock.Of<Application.Interfaces.IUnitOfWork>(),
            Mock.Of<Application.Interfaces.ICurrentTenantService>(), NullLogger<NotificationService>.Instance);

        Mock<IHubClients> clients = new();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);
    }

    private TryOnResultConsumer CreateConsumer() =>
        new(_notificationService.Object, _hubContext.Object, NullLogger<TryOnResultConsumer>.Instance);

    private static ServiceBusReceivedMessage BuildMessage(object payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        return ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromBytes(body));
    }

    [Fact]
    public async Task HandleMessageAsync_Success_CreatesTryOnCompletedNotificationAndPushes()
    {
        var customerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = requestId, TenantId = tenantId, CustomerId = customerId, ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, IsSuccess = true, ResultImageUrl = "https://space.hf.space/file=result.png", FailureReason = (string?)null
        });

        await CreateConsumer().HandleMessageAsync(message, CancellationToken.None);

        _notificationService.Verify(n => n.CreateAsync(
            tenantId, customerId, NotificationType.TryOnCompleted,
            It.IsAny<string>(), It.IsAny<string>(), "TryOnRequest", requestId, It.IsAny<CancellationToken>()), Times.Once);
        _clientProxy.Verify(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_Failure_CreatesTryOnFailedNotification()
    {
        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, IsSuccess = false, ResultImageUrl = (string?)null, FailureReason = "Render failed"
        });

        await CreateConsumer().HandleMessageAsync(message, CancellationToken.None);

        _notificationService.Verify(n => n.CreateAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), NotificationType.TryOnFailed,
            It.IsAny<string>(), It.IsAny<string>(), "TryOnRequest", It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_HubPushThrows_DoesNotThrow_NotificationAlreadyPersisted()
    {
        ServiceBusReceivedMessage message = BuildMessage(new
        {
            TryOnRequestId = Guid.NewGuid(), TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, IsSuccess = true, ResultImageUrl = "https://space.hf.space/file=result.png", FailureReason = (string?)null
        });
        _clientProxy.Setup(c => c.SendCoreAsync("ReceiveNotification", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub disposed"));

        Func<Task> act = () => CreateConsumer().HandleMessageAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _notificationService.Verify(n => n.CreateAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<NotificationType>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once,
            "the notification must still be persisted even though the live push failed");
    }
}
```

> **Implementer note:** if `NotificationService`'s members aren't virtual (Moq's class-mocking requires `virtual`), you have two options: (a) make `CreateAsync` virtual (a one-line, low-risk change, consistent with this being the first time it's mocked as a class rather than via an interface), or (b) extract a minimal `INotificationService` interface covering just `CreateAsync` and have `TryOnResultConsumer` depend on that instead. Prefer (a) — smaller, and every other existing caller of `NotificationService` is unaffected either way. Whichever you choose, keep the test's mocking mechanism consistent with it.

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/FashionSaaS.API.Tests --filter TryOnResultConsumerTests`
Expected: FAIL — `TryOnResultConsumer`, `NotificationType.TryOnCompleted`/`TryOnFailed` don't exist yet.

- [x] **Step 3: Add the two notification types**

In `src/FashionSaaS.Domain/Enums/NotificationType.cs` **via Serena `replace_content`**, replace the whole file:

```csharp
namespace FashionSaaS.Domain.Enums;

public enum NotificationType
{
    OrderPlaced,
    OrderStatusChanged,
    PaymentConfirmed,
    LowStock,
    ReviewSubmitted,
    TryOnCompleted,
    TryOnFailed
}
```

- [x] **Step 4: Create the main API's `ServiceBusSettings`**

Create `src/FashionSaaS.Application/Configuration/ServiceBusSettings.cs` **via Serena `create_text_file`** — a separate class from the try-on microservice's (different solution, different assembly, can't be shared):

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.Application.Configuration;

public class ServiceBusSettings
{
    public const string SectionName = "ServiceBusSettings";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string TopicName { get; init; } = "tryon-events";

    [Required]
    public string SubscriptionName { get; init; } = "main-api-tryon-results";
}
```

- [x] **Step 5: Implement the consumer**

Create `src/FashionSaaS.API/BackgroundJobs/TryOnResultConsumer.cs` **via Serena `create_text_file`**:

```csharp
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Configuration;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FashionSaaS.API.BackgroundJobs;

/// <summary>
/// The wire shape published by the try-on microservice's ServiceBusTryOnEventPublisher
/// (services/fashionsaas-tryon/.../Messaging/TryOnResultEvent.cs). Duplicated here — the two
/// services are separate deployables with no shared assembly — and must be kept in sync with
/// that type by hand if it ever changes.
/// </summary>
internal sealed record TryOnResultMessage(
    Guid TryOnRequestId, Guid TenantId, Guid CustomerId, Guid ProductId, DateTime CreatedAt,
    bool IsSuccess, string? ResultImageUrl, string? FailureReason);

/// <summary>
/// Consumes TryOnResultEvent messages from the try-on microservice and turns them into a
/// persisted Notification plus a live SignalR push to the customer who requested the render —
/// the first Service Bus consumer in this codebase (previously publish-only). Lives in the API
/// project, not Infrastructure, because it needs IHubContext&lt;NotificationsHub&gt; — the same
/// reasoning as OrderPlacedNotificationHandler.
/// </summary>
public class TryOnResultConsumer : BackgroundService
{
    private readonly NotificationService _notificationService;
    private readonly IHubContext<NotificationsHub> _hubContext;
    private readonly ILogger<TryOnResultConsumer> _logger;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSettings _settings;
    private ServiceBusProcessor? _processor;

    public TryOnResultConsumer(
        NotificationService notificationService,
        IHubContext<NotificationsHub> hubContext,
        ILogger<TryOnResultConsumer> logger,
        ServiceBusClient client,
        IOptions<ServiceBusSettings> settings)
    {
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
        _client = client;
        _settings = settings.Value;
    }

    // Test-only constructor: the tests exercise HandleMessageAsync directly and never start the
    // processor, so the ServiceBusClient/settings plumbing above is irrelevant to them.
    internal TryOnResultConsumer(NotificationService notificationService, IHubContext<NotificationsHub> hubContext, ILogger<TryOnResultConsumer> logger)
    {
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
        _client = null!;
        _settings = null!;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_settings.TopicName, _settings.SubscriptionName);
        _processor.ProcessMessageAsync += async args =>
        {
            await HandleMessageAsync(args.Message, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };
        _processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "TryOnResultConsumer processor error");
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    internal async Task HandleMessageAsync(ServiceBusReceivedMessage message, CancellationToken ct)
    {
        TryOnResultMessage? evt = JsonSerializer.Deserialize<TryOnResultMessage>(
            message.Body.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (evt is null)
        {
            _logger.LogWarning("TryOnResultConsumer received a message that could not be deserialized");
            return;
        }

        var type = evt.IsSuccess ? NotificationType.TryOnCompleted : NotificationType.TryOnFailed;
        var title = evt.IsSuccess ? "Your try-on is ready" : "Your try-on failed";
        var message2 = evt.IsSuccess
            ? "Your virtual try-on has finished rendering."
            : $"Your try-on couldn't be completed: {evt.FailureReason}";

        Domain.Entities.Notification saved = await _notificationService.CreateAsync(
            evt.TenantId, evt.CustomerId, type, title, message2, "TryOnRequest", evt.TryOnRequestId, ct);

        try
        {
            await _hubContext.Clients.Group($"user:{evt.CustomerId}")
                .SendAsync("ReceiveNotification", saved, ct);
        }
        // CA1031 suppressed: same "must never throw" boundary as OrderPlacedNotificationHandler -
        // the Notification row already committed above, so a live-push failure of any kind must
        // be swallowed and logged, not fail message processing (which would redeliver it).
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push TryOnResult live notification for request {TryOnRequestId}", evt.TryOnRequestId);
        }
#pragma warning restore CA1031
    }
}
```

- [x] **Step 6: Register everything in `Program.cs`**

Read the main API's `Program.cs` first to find the right insertion point (near where `AddSignalR()`/`AddHostedService`-equivalent registrations live — `SubscriptionExpiryJob` is registered inside `AddInfrastructure` per Task-1's-research `DependencyInjection.cs`, but `TryOnResultConsumer` needs `IHubContext<NotificationsHub>`, which only exists once `MapHub`/SignalR is configured in the API project — register it in `Program.cs` directly, alongside `AddSignalR()`, not inside `AddInfrastructure`).

**Via Serena**, add near the existing `builder.Services.AddSignalR();` line:

```csharp
builder.Services.AddOptions<ServiceBusSettings>()
    .Bind(builder.Configuration.GetSection(ServiceBusSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(sp =>
    new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusSettings>>().Value.ConnectionString));

builder.Services.AddHostedService<TryOnResultConsumer>();
```

Add the necessary `using FashionSaaS.API.BackgroundJobs;`, `using FashionSaaS.Application.Configuration;`, `using Azure.Messaging.ServiceBus;`, `using Microsoft.Extensions.Options;` if not already present.

- [x] **Step 7: Add the config section**

In `src/FashionSaaS.API/appsettings.Development.json`, add (native Edit — JSON):

```json
  "ServiceBusSettings": {
    "ConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "tryon-events",
    "SubscriptionName": "main-api-tryon-results"
  },
```

> This must be the exact same emulator connection string/topic the try-on service publishes to (both point at the same local Service Bus emulator instance), and `SubscriptionName` must exactly match the subscription name added to `servicebus-emulator-config.json` in Task 3, Step 4.

- [x] **Step 8: Run the tests, then the full verification gate**

Run: `dotnet test tests/FashionSaaS.API.Tests --filter TryOnResultConsumerTests`
Expected: PASS — 3 passed, 0 failed.

Run: `dotnet build FashionSaaS.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Run: `dotnet test FashionSaaS.sln`
Expected: 0 failed. Record the exact count (this repo had 571 passing before this task).

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on every file this task touched.

- [ ] **Step 9: Manual verification — the emulator round trip**  — ⛔ **NOT DONE:** requires the emulator + both APIs running against a real Space.

Restart both the Service Bus emulator (`docker compose up -d` if not already running with the updated config) and both APIs, then submit a real try-on (once Hugging Face is configured) or, for a quick smoke test without a real Space, manually publish a test message to the `tryon-events` topic and confirm a `Notification` row appears in the main API's database and the try-on service's log shows no errors.

- [x] **Step 10: Commit**

```bash
git add src/FashionSaaS.Application/Configuration/ServiceBusSettings.cs src/FashionSaaS.API/BackgroundJobs/ src/FashionSaaS.Domain/Enums/NotificationType.cs src/FashionSaaS.API/Program.cs src/FashionSaaS.API/appsettings.Development.json tests/FashionSaaS.API.Tests/BackgroundJobs/
git commit -m "feat(notifications): consume TryOnResultEvent from Service Bus, push live notification"
```

---

## Task 7: Try-on service — status/result endpoint for the storefront to poll after a push

**Files:**
- Create: `src/FashionSaaS.TryOn.Application/TryOn/TryOnStatusResponse.cs`
- Modify: `src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs`
- Modify: `src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs`
- Test: `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs`

**Interfaces:**
- Consumes: `TryOnRequest.Status`/`ResultImageUrl`/`FailureReason` (Task 1).
- Produces: `TryOnService.GetStatusAsync(Guid requestId, CancellationToken ct) : Task<(bool IsSuccess, int StatusCode, string Message, TryOnStatusResponse? Data)>`; `GET api/tryon/{id}`.

- [x] **Step 1: Write the failing tests**

Append to `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs`:

```csharp

    [Fact]
    public async Task GetStatusAsync_OwnRequest_ReturnsCurrentState()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);
        var request = new TryOnRequest
        {
            TenantId = _tenantId, CustomerId = _context.Object.CustomerId, Status = TryOnStatus.Completed,
            ResultImageUrl = "https://space.hf.space/file=result.png"
        };
        dbContext.TryOnRequests.Add(request);
        await dbContext.SaveChangesAsync();

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) = await service.GetStatusAsync(request.Id, CancellationToken.None);

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

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) = await service.GetStatusAsync(request.Id, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(404);
        data.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_UnknownId_Returns404()
    {
        await using TryOnDbContext dbContext = CreateDbContext();
        TryOnService service = CreateService(dbContext, aiUsageLimit: 10);

        (var isSuccess, var statusCode, var _, TryOnStatusResponse? data) = await service.GetStatusAsync(Guid.NewGuid(), CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(404);
        data.Should().BeNull();
    }
```

> Note: `CreateService`'s `_context` mock's `CustomerId` setup returns a fixed `Guid.NewGuid()` captured once inside `CreateService` (read the current implementation) — the first test above reuses `_context.Object.CustomerId` to match whatever `CreateService` set up, so the "own request" check passes. If `CreateService` doesn't expose a stable customer id this way, adjust the test to call `_context.Setup(c => c.CustomerId).Returns(...)` with an explicit value before constructing the request, and use that same value here.

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests --filter TryOnServiceTests`
Expected: FAIL — `GetStatusAsync`/`TryOnStatusResponse` don't exist.

- [x] **Step 3: Create the response DTO**

Create `src/FashionSaaS.TryOn.Application/TryOn/TryOnStatusResponse.cs` **via Serena `create_text_file`**:

```csharp
namespace FashionSaaS.TryOn.Application.TryOn;

public record TryOnStatusResponse(string Status, string? ResultImageUrl, string? FailureReason);
```

- [x] **Step 4: Add `GetStatusAsync` to `TryOnService`**

**Via Serena `insert_after_symbol`**, add this method to `TryOnService` (after `RecordFailureAsync`):

```csharp

    /// <summary>
    /// Fetches the current status of a try-on request. Scoped to the requesting customer AND
    /// tenant — a request that exists but isn't theirs returns the same 404 as one that doesn't
    /// exist at all, so this never confirms another customer's request exists.
    /// </summary>
    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnStatusResponse? Data)> GetStatusAsync(
        Guid requestId, CancellationToken cancellationToken)
    {
        TryOnRequest? request = await dbContext.TryOnRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            .ConfigureAwait(false);

        if (request is null || request.TenantId != currentContext.TenantId || request.CustomerId != currentContext.CustomerId)
            return (false, 404, "Try-on request not found.", null);

        return (true, 200, "Success", new TryOnStatusResponse(request.Status.ToString(), request.ResultImageUrl, request.FailureReason));
    }
```

Add `using Microsoft.EntityFrameworkCore;` to the file's usings if not already present (for `AsNoTracking`/`FirstOrDefaultAsync`).

- [x] **Step 5: Add the controller action**

**Via Serena `insert_after_symbol`**, add to `TryOnController` (after `PostAsync`):

```csharp

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, TryOnStatusResponse? data) = await tryOnService.GetStatusAsync(id, cancellationToken);

        ResponseData<TryOnStatusResponse> response = isSuccess
            ? ResponseData<TryOnStatusResponse>.Success(data!, message, statusCode)
            : ResponseData<TryOnStatusResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
```

- [x] **Step 6: Run the tests, then the full verification gate**

Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests --filter TryOnServiceTests`
Expected: PASS — all tests in the file, including the 3 new ones.

Run: `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Run: `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln`
Expected: 0 failed.

Then run `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) on the two touched files.

- [x] **Step 7: Commit**

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnStatusResponse.cs services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/TryOn/TryOnService.cs services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs
git commit -m "feat(tryon): add GET api/tryon/{id} for fetching current status/result"
```

---

## Task 8: Storefront — processing state, SignalR-driven completion

**Files:**
- Modify: `fashionsaas-storefront/src/app/features/catalog/models/try-on.model.ts`
- Modify: `fashionsaas-storefront/src/app/features/catalog/services/try-on.service.ts`
- Modify: `fashionsaas-storefront/src/app/admin/notifications/models/notification.model.ts`
- Modify: `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.ts`
- Modify: `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.html`
- Test: the co-located `*.spec.ts` files

**Interfaces:**
- Consumes: `POST /api/tryon` now returns `{ requestId }` with `202` (Task 4); `GET /api/tryon/{id}` (Task 7); `NotificationHubService.notificationReceived$` (existing, untouched).

> Use native Edit/Write here — the Serena hook only guards `.cs`.

- [x] **Step 1: Update the model**

Replace `fashionsaas-storefront/src/app/features/catalog/models/try-on.model.ts`:

```typescript
export interface TryOnApiResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message: string;
  data: T | null;
  errors: string[] | null;
}

export interface TryOnSubmitted {
  requestId: string;
}

export interface TryOnStatus {
  status: 'Processing' | 'Completed' | 'Failed';
  resultImageUrl: string | null;
  failureReason: string | null;
}
```

- [x] **Step 2: Update the service — `submit()` + `getStatus()`**

Replace `fashionsaas-storefront/src/app/features/catalog/services/try-on.service.ts`:

```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { TryOnApiResponse, TryOnSubmitted, TryOnStatus } from '../models/try-on.model';

@Injectable({ providedIn: 'root' })
export class TryOnService {
  constructor(private http: HttpClient) {}

  submit(
    photo: File,
    garmentImageUrl: string,
    productId: string,
    productVariantId?: string
  ): Observable<TryOnSubmitted> {
    const formData = new FormData();
    formData.append('photo', photo);
    formData.append('garmentImageUrl', garmentImageUrl);
    formData.append('productId', productId);
    if (productVariantId) {
      formData.append('productVariantId', productVariantId);
    }

    return this.http
      .post<TryOnApiResponse<TryOnSubmitted>>(`${environment.tryOnApiBaseUrl}/tryon`, formData)
      .pipe(
        map((response) => {
          if (!response.data) {
            throw new Error(response.message || 'Try-on submission failed.');
          }
          return response.data;
        })
      );
  }

  getStatus(requestId: string): Observable<TryOnStatus> {
    return this.http
      .get<TryOnApiResponse<TryOnStatus>>(`${environment.tryOnApiBaseUrl}/tryon/${requestId}`)
      .pipe(
        map((response) => {
          if (!response.data) {
            throw new Error(response.message || 'Could not fetch try-on status.');
          }
          return response.data;
        })
      );
  }
}
```

- [x] **Step 3: Extend the notification type union**

In `fashionsaas-storefront/src/app/admin/notifications/models/notification.model.ts`, change:

```typescript
export type NotificationTypeName =
  | 'OrderPlaced'
  | 'OrderStatusChanged'
  | 'PaymentConfirmed'
  | 'LowStock'
  | 'ReviewSubmitted'
  | 'TryOnCompleted'
  | 'TryOnFailed';
```

- [x] **Step 4: Rewire the Try It On flow to async**

Read the current `product-detail.component.ts` in full first (confirm the exact current field names/imports match what's quoted in this plan's research before editing — it may have changed since). Replace the Try It On state block and its two methods with:

```typescript
  // Try It On state — now async: submit returns immediately, the result arrives later via
  // SignalR (see ngOnInit's notificationReceived$ subscription below).
  tryOnPhotoFile: File | null = null;
  tryOnRequestId: string | null = null;
  tryOnResultImageUrl$ = new BehaviorSubject<string | null>(null);
  tryOnProcessing$ = new BehaviorSubject<boolean>(false);
  tryOnError$ = new BehaviorSubject<string | null>(null);
  ...
  onTryOnPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.tryOnPhotoFile = input.files?.[0] ?? null;
    this.tryOnError$.next(null);
    this.tryOnResultImageUrl$.next(null);
  }

  submitTryOn(): void {
    const product = this.product$.value;
    const variant = this.selectedVariant$.value;

    if (!this.tryOnPhotoFile) {
      this.tryOnError$.next('Please choose a photo first.');
      return;
    }
    if (!product?.primaryImageUrl) {
      this.tryOnError$.next('This product has no image to try on.');
      return;
    }

    this.tryOnProcessing$.next(true);
    this.tryOnError$.next(null);
    this.tryOnResultImageUrl$.next(null);

    this.tryOnService
      .submit(this.tryOnPhotoFile, product.primaryImageUrl, this.productId, variant?.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (submitted) => {
          // Deliberately do NOT clear tryOnProcessing$ here - it stays true until the SignalR
          // push (or a page reload, which loses this state - a known limitation carried over
          // from the existing "fully stateless" design) resolves it.
          this.tryOnRequestId = submitted.requestId;
        },
        error: (err) => {
          this.tryOnProcessing$.next(false);
          const status = err?.status;
          this.tryOnError$.next(
            status === 429
              ? "You've reached this month's try-on limit. Upgrade your plan or try again next month."
              : 'The try-on render failed. Please try again in a moment.'
          );
        },
      });
  }

  private onTryOnNotification(notification: NotificationDto): void {
    if (notification.entityId !== this.tryOnRequestId) return;
    if (notification.type !== 'TryOnCompleted' && notification.type !== 'TryOnFailed') return;

    this.tryOnService
      .getStatus(this.tryOnRequestId!)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (status) => {
          this.tryOnProcessing$.next(false);
          if (status.status === 'Completed' && status.resultImageUrl) {
            this.tryOnResultImageUrl$.next(status.resultImageUrl);
          } else {
            this.tryOnError$.next(status.failureReason ?? 'The try-on render failed. Please try again in a moment.');
          }
        },
        error: () => {
          this.tryOnProcessing$.next(false);
          this.tryOnError$.next('The try-on render failed. Please try again in a moment.');
        },
      });
  }
```

In `ngOnInit` (read the current method first to see where other `takeUntil(this.destroy$)` subscriptions are set up, and add alongside them), subscribe to the hub's push stream:

```typescript
    this.notificationHub
      .connect(); // no-op if already connected - matches existing app-wide connection lifecycle
    this.notificationHub.notificationReceived$
      .pipe(takeUntil(this.destroy$))
      .subscribe((notification) => this.onTryOnNotification(notification));
```

Add the two new imports/injections this needs — `NotificationHubService` from `../../../../core/services/notification-hub.service` and `NotificationDto` from `../../../../admin/notifications/models/notification.model` — and inject `NotificationHubService` in the constructor alongside the existing services (`tryOnService`, etc. — read the current constructor to match its exact style).

> `NotificationHubService.connect()` is idempotent (confirmed in this plan's research) — calling it here even though the user may already be connected from elsewhere in the app is safe and required, since an anonymous/logged-out storefront visitor might not have connected yet.

- [x] **Step 5: Update the template**

In `product-detail.component.html`, replace the Try It On markup block:

```html
          <div class="try-on-section mb-4">
            <label class="form-label">Try It On</label>
            <input
              type="file"
              accept="image/jpeg,image/png"
              class="form-control mb-2"
              (change)="onTryOnPhotoSelected($event)"
            />
            <button
              class="btn btn-outline-primary w-100 mb-2"
              (click)="submitTryOn()"
              [disabled]="(tryOnProcessing$ | async) === true"
            >
              <span *ngIf="(tryOnProcessing$ | async) === true">Generating your try-on… this can take a few minutes on the free tier.</span>
              <span *ngIf="(tryOnProcessing$ | async) !== true">
                <i class="bi bi-magic me-2"></i>Try It On
              </span>
            </button>
            <div *ngIf="tryOnError$ | async as tryOnError" class="alert alert-warning" role="alert">
              {{ tryOnError }}
            </div>
            <img
              *ngIf="tryOnResultImageUrl$ | async as tryOnResultImageUrl"
              [src]="tryOnResultImageUrl"
              alt="Try-on render result"
              class="img-fluid rounded try-on-result"
            />
          </div>
```

> The result `<img>` now points at a Hugging Face-served URL (`[src]="tryOnResultImageUrl"`) instead of the old inline `data:` URI — no other change needed since `[src]` binds to any string URL equally.

- [x] **Step 6: Update the specs**

Update `product-detail.component.spec.ts` for the new field names (`tryOnProcessing$` not `tryOnLoading$`, `tryOnResultImageUrl$` not `tryOnResultDataUri$`), mock `TryOnService.submit`/`getStatus` instead of `render`, and mock `NotificationHubService` (a simple `{ connect: () => {}, notificationReceived$: new Subject() }` stub). Add these new cases:

```typescript
  it('shows the processing state after a successful submit and does not clear it immediately', () => {
    // mockTryOnService.submit returns of({ requestId: 'req-1' })
    component.submitTryOn();
    expect(component.tryOnProcessing$.value).toBe(true);
    expect(component.tryOnRequestId).toBe('req-1');
  });

  it('resolves to the result image when a matching TryOnCompleted notification arrives', () => {
    component.tryOnRequestId = 'req-1';
    // mockTryOnService.getStatus returns of({ status: 'Completed', resultImageUrl: 'https://x/y.png', failureReason: null })
    notificationSubject.next({ type: 'TryOnCompleted', entityId: 'req-1' } as NotificationDto);
    expect(component.tryOnProcessing$.value).toBe(false);
    expect(component.tryOnResultImageUrl$.value).toBe('https://x/y.png');
  });

  it('ignores a notification for a different requestId', () => {
    component.tryOnRequestId = 'req-1';
    notificationSubject.next({ type: 'TryOnCompleted', entityId: 'some-other-id' } as NotificationDto);
    expect(component.tryOnProcessing$.value).toBe(true); // unchanged - still whatever it was before
  });

  it('shows the failure reason when a TryOnFailed notification arrives', () => {
    component.tryOnRequestId = 'req-1';
    // mockTryOnService.getStatus returns of({ status: 'Failed', resultImageUrl: null, failureReason: 'CUDA out of memory' })
    notificationSubject.next({ type: 'TryOnFailed', entityId: 'req-1' } as NotificationDto);
    expect(component.tryOnError$.value).toBe('CUDA out of memory');
  });
```

- [x] **Step 7: Run the frontend build and tests**

```bash
cd fashionsaas-storefront && npx ng build
```

Expected: clean bundle, no new errors (the pre-existing account/catalog spec compile breakage documented in the Phase 9a work is unrelated and untouched by this task — if `ng test`'s whole-repo run fails for those same pre-existing reasons, use the same scoped-verification approach from that prior work: a disposable temp tsconfig including only this task's touched spec files, deleted after use).

- [ ] **Step 8: Verify in the browser (once you have a real Hugging Face Space configured)**  — ⛔ **NOT DONE / BLOCKED:** no Space configured.

Start the API, the try-on service, and `ng serve`. Log in, open a product, attach a photo, submit. Confirm: the button immediately shows the "Generating…" state; after the real render completes (1-5 minutes on free tier), the result image appears without a page reload; check the browser console for zero errors during the wait. Also confirm a deliberately-bad garment image or an oversized photo still surfaces the existing synchronous validation errors immediately, unaffected by this change.

- [x] **Step 9: Commit**

```bash
git add fashionsaas-storefront/src/app/features/catalog/ fashionsaas-storefront/src/app/admin/notifications/models/notification.model.ts
git commit -m "feat(storefront): async try-on submit with SignalR-driven completion"
```

---

## Validate

- [x] `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` → `0 Warning(s) 0 Error(s)`
- [x] `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` → 0 failed
- [x] `dotnet build FashionSaaS.sln` → `0 Warning(s) 0 Error(s)`
- [x] `dotnet test FashionSaaS.sln` → 0 failed
- [x] `mcp__serena__get_diagnostics_for_file` (`min_severity: 2`) clean on every touched `.cs` file
- [x] `cd fashionsaas-storefront && npx ng build` → clean bundle
- [ ] ⛔ **BLOCKED** Manual: a full live run once Hugging Face is configured — submit → processing state → SignalR push → result renders; a deliberately-failing job (revoke the HF token temporarily, or set an invalid `SpaceUrl`) → processing state → SignalR push → error message renders
- [x] `grep -rn "IGeminiImageClient\|TryOnCompletedEvent\|TryOnResultResponse\b" --include=*.cs services/fashionsaas-tryon` returns **no matches** (confirms full removal)

## Notes for the reviewer

- **Tasks 3→4 and 4→5 each deliberately leave the try-on solution non-building** — this mirrors the exact re-sequencing lesson from the Phase 9a payment-proof work: a change that spans multiple files sometimes can't be split into independently-buildable commits without an awkward temporary shim, and the plan says so explicitly rather than pretending otherwise.
- **The Gradio API shape in Task 2 is the biggest source of real-world risk in this plan** — it's written against the current common pattern but cannot be verified until you have a live Space. Budget time for Task 2 to need adjustment once you see your actual Space's API panel.
- **A known UX gap, inherited from the existing "fully stateless" Try It On design**: if the customer reloads the page while a render is `Processing`, the in-memory `tryOnRequestId`/processing state is lost and there's no UI path back to it (the backend `GET /api/tryon/{id}` endpoint from Task 7 exists and could support a "check my pending try-on" affordance later, but building that isn't in this plan's scope).
- **Pre-existing security note, unrelated to this plan but observed during research**: `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json` currently contains what looks like a real Gemini API key committed to the repository. Worth rotating and moving to the existing outside-repo secrets-backup mechanism (established earlier this session) — flagging for your attention, not fixing here since it's out of this plan's scope.
