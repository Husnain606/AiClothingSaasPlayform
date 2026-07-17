# Phase 6 — AI Body Measurement + Fashion Chatbot (Buildable Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> execute this plan — each lettered task group below is an independent unit dispatched to a
> subagent, validated, then the next group starts. Do not free-hand the order.

> **STATUS — NOT STARTED (2026-07-18).** No code written yet. This is the buildable plan; build
> after user sign-off on both this file and the companion design spec
> (`docs/superpowers/specs/2026-07-18-phase6-ai-measurement-chatbot-design.md`).

## Reference

- Design spec: [`../specs/2026-07-18-phase6-ai-measurement-chatbot-design.md`](../specs/2026-07-18-phase6-ai-measurement-chatbot-design.md) — read first; this plan implements it verbatim, does not redecide it.
- **Dependency (consumed, not redefined):** Phase 5a's `FashionSaaS.TryOn` microservice (`services/fashionsaas-tryon/`) — existing symbols this plan builds on, verified against the real code on 2026-07-18:
  - `TryOnService` (`Infrastructure/TryOn/TryOnService.cs`) — constructor `TryOnService(TryOnDbContext, ICurrentTryOnContext, IGeminiImageClient, IHttpClientFactory, IOptions<GeminiSettings>, ITryOnEventPublisher)`; today runs its own inline quota `CountAsync` (`TryOnService.cs:39-43`) — Task Group B extracts this into the new shared `IUsageQuotaService` and changes this constructor signature.
  - `ICurrentTryOnContext` (`Application/ICurrentTryOnContext.cs`) — `TenantId`, `CustomerId`, `AiUsageLimit`, `IsAuthenticated`. Unchanged by this plan.
  - `GeminiSettings` (`Application/Gemini/GeminiSettings.cs`) — `ApiKey`, `BaseUrl`, `Model`, `AllowedGarmentImageHosts`. Task Group A adds `TextModel`.
  - `TryOnDbContext` (`Infrastructure/Persistence/TryOnDbContext.cs`) — one `DbSet<TryOnRequest>`, `OnModelCreating` calls `ApplyConfigurationsFromAssembly`. Task Groups C/D add two more `DbSet`s; no `OnModelCreating` change needed (assembly-scan already picks up new `IEntityTypeConfiguration<T>` classes).
  - `ResponseData<T>` (`Api/Common/ResponseData.cs`) — `internal class`, `Success`/`Failure` factory methods, `IsSuccess/StatusCode/Message/Data/Errors`. Reused unchanged by the two new controllers.
  - `BaseEntity` (`Domain/BaseEntity.cs`) — `Id`, `CreatedAt`, `UpdatedAt`, all defaulted in-class. Both new entities inherit it.
  - `DependencyInjection.cs` (`Infrastructure/DependencyInjection.cs`) — `AddTryOnInfrastructure` / `AddTryOnAuthentication` extension methods on `IServiceCollection`. Task Groups A–D each add registrations here.
  - `Program.cs` (`Api/Program.cs`) — registers `IGeminiImageClient` via `AddRefitClient`, FluentValidation auto-validation + `AddValidatorsFromAssembly`. Task Group A adds a second `AddRefitClient<IGeminiTextClient>()` call.

### Contract checklist (confirm against landed code before editing)
- [ ] `TryOnService.RenderAsync(TryOnRequestForm, CancellationToken)` returns `(bool IsSuccess, int StatusCode, string Message, TryOnResultResponse? Data)` — unchanged signature; only its constructor and internal quota check change (Group B).
- [ ] `TryOnRequestConfiguration` indexes `(TenantId, Status, CreatedAt)` (`TryOnRequestConfiguration.cs:17`) — the new `MeasurementRequestConfiguration`/`ChatRequestConfiguration` must declare the identical composite index shape, since `IUsageQuotaService` queries all three tables on exactly that predicate.
- [ ] `ServiceBusTryOnEventPublisher.PublishAsync` never throws (deliberate bare catch, `ServiceBusTryOnEventPublisher.cs:30-35`) — confirms Group C/D do **not** need to wire Service Bus publishing for measurement/chat (design spec §15: explicitly out of scope, no event defined).

### Locked decisions in force
- **D1** — both features extend `FashionSaaS.TryOn` in place; no new service/solution/database.
- **D2** — Gemini powers both; chat uses a new `IGeminiTextClient` Refit interface (image client's DTOs don't fit — verified API shape differs, see design spec §7).
- **D3** — fully stateless photo handling for measurement (same rule as `TryOnRequest`); nothing image-related ever persisted.
- **D4** — `POST /api/measure`: multipart photo + optional `heightCm`; returns measurements + recommended size + confidence; persists `MeasurementRequest` (values only).
- **D5** — `POST /api/chat`: JSON `messages[]` (client-held, capped) + optional `productContext`; returns assistant reply; persists `ChatRequest` (lengths only, no transcript).
- **D6** — one combined `ai_usage_limit` pool across `TryOnRequest` + `MeasurementRequest` + `ChatRequest`, via new `IUsageQuotaService`.
- **D7** — storefront: (a) "Find My Size" section on product detail, mirroring "Try It On"; (b) floating chat widget, storefront-wide, product-context-aware.
- **D8** — same auth model, independent JWT validation, no main-API changes.
- **D9** — no new third-party libraries; Refit/FluentValidation/EF Core already approved for this service; Angular uses existing HttpClient/RxJS patterns.

### Gemini API facts (official docs — cited inline)
- `POST /v1beta/models/{model}:generateContent` — same endpoint path serves both image and text generation; `x-goog-api-key` header auth (unchanged from Phase 5a's `IGeminiImageClient`). `systemInstruction` is a top-level request field, sibling to `contents`, not nested inside it. Each `contents[]` entry carries an optional `role` (`"user"`/`"model"`) for multi-turn replay. Response text is at `candidates[0].content.parts[0].text`. Source: `https://ai.google.dev/api/generate-content` (fetched 2026-07-18).

## 1. Ordered task checklist

Execute top-to-bottom; build (`dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln`) after each lettered group.

### Group A — Gemini text-generation client + centralized prompts
- [ ] **A1** Add `TextModel` to `GeminiSettings` (`Application/Gemini/GeminiSettings.cs`).
- [ ] **A2** Create `Application/Gemini/GeminiTextDtos.cs` — text request/response records.
- [ ] **A3** Create `Application/Gemini/IGeminiTextClient.cs` — Refit interface.
- [ ] **A4** Create `Application/Gemini/GeminiPrompts.cs` — static prompt constants/builders.
- [ ] **A5** Register `IGeminiTextClient` in `Api/Program.cs` (second `AddRefitClient` call, same pattern as the existing image client).

### Group B — Shared combined-pool quota service (initial: try-on only)
- [ ] **B1** Create `Application/Quota/IUsageQuotaService.cs`.
- [ ] **B2** Create `Infrastructure/Quota/UsageQuotaService.cs` — sums **only** `TryOnRequests`, the one feature table that exists at this point in the sequence; the interface is deliberately shaped so Groups C and D can each add one more summed table without changing its signature.
- [ ] **B3** Register `IUsageQuotaService` in `Infrastructure/DependencyInjection.cs`.
- [ ] **B4** Modify `TryOnService` to accept `IUsageQuotaService` and drop its inline `CountAsync` quota query.
- [ ] **B5** Update `TryOnServiceTests.CreateService(...)` to inject a stubbed `IUsageQuotaService` (existing 5 tests keep passing unmodified in assertions — only construction changes).
- [ ] **B6** Write `UsageQuotaServiceTests` covering try-on-only counting (exact tests in §3) — `MeasurementRequests`/`ChatRequests` don't exist yet, so Group B's own quota coverage is try-on-only; Groups C and D each add their extension test alongside their own new table.

### Group C — Measurement feature (backend)
- [ ] **C1** Create `Domain/MeasurementRequest.cs`, `Domain/MeasurementStatus.cs`, `Domain/SizeCode.cs`.
- [ ] **C2** Create `Infrastructure/Persistence/Configurations/MeasurementRequestConfiguration.cs`.
- [ ] **C3** Add `DbSet<MeasurementRequest> MeasurementRequests` to `TryOnDbContext`.
- [ ] **C4** Generate EF Core migration `AddMeasurementRequest`.
- [ ] **C5** Create `Application/Gemini/GeminiMeasurementResult.cs` (parse-only DTO for Gemini's JSON reply).
- [ ] **C6** Create `Application/Measurement/MeasurementRequestForm.cs`, `MeasurementRequestFormValidator.cs`, `MeasurementResultResponse.cs`.
- [ ] **C7** Create `Infrastructure/Measurement/MeasurementService.cs`.
- [ ] **C8** Create `Api/Controllers/MeasureController.cs`.
- [ ] **C9** Register `MeasurementService` in `Infrastructure/DependencyInjection.cs`.
- [ ] **C10** Extend `UsageQuotaService.GetUsedThisMonthAsync` to add the `MeasurementRequests` term, now that C3 has added the `DbSet` it needs — with a new test asserting the combined count spans both tables.

### Group D — Chat feature (backend)
- [ ] **D1** Create `Domain/ChatRequest.cs`, `Domain/ChatRequestStatus.cs`.
- [ ] **D2** Create `Infrastructure/Persistence/Configurations/ChatRequestConfiguration.cs`.
- [ ] **D3** Add `DbSet<ChatRequest> ChatRequests` to `TryOnDbContext`.
- [ ] **D4** Generate EF Core migration `AddChatRequest`.
- [ ] **D5** Create `Application/Chat/ChatMessage.cs`, `ChatProductContext.cs`, `ChatRequestDto.cs`, `ChatRequestValidator.cs`, `ChatResultResponse.cs`.
- [ ] **D6** Create `Infrastructure/Chat/ChatService.cs`.
- [ ] **D7** Create `Api/Controllers/ChatController.cs`.
- [ ] **D8** Register `ChatService` in `Infrastructure/DependencyInjection.cs`.
- [ ] **D9** Extend `UsageQuotaService.GetUsedThisMonthAsync` to add the `ChatRequests` term, now that D3 has added the `DbSet` it needs — with a new test asserting the combined count spans all three tables (this is the final, design-spec-§9 shape of the quota service).

### Group E — Storefront: Find My Size
- [ ] **E1** Create `features/catalog/models/measurement.model.ts`.
- [ ] **E2** Create `features/catalog/services/measurement.service.ts`.
- [ ] **E3** Extend `product-detail.component.ts` with Find My Size state/methods.
- [ ] **E4** Extend `product-detail.component.html` with the Find My Size section.
- [ ] **E5** Update `environment.ts`/`environment.prod.ts` — none needed (reuses `tryOnApiBaseUrl`); confirm no new key required.

### Group F — Storefront: fashion chat widget
- [ ] **F1** Create `features/chat/models/chat.model.ts`.
- [ ] **F2** Create `features/chat/services/chat.service.ts`.
- [ ] **F3** Create `features/chat/components/chat-widget/chat-widget.component.ts` + `.html` + `.css`.
- [ ] **F4** Wire `<app-chat-widget>` into `layouts/main-layout/main-layout.component.html` + `.ts`.
- [ ] **F5** Extend `product-detail.component.ts`/`.html` to pass `productContext` into the widget when opened from a product page.

### Group G — Validate
- [ ] **G1** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — zero warnings (warnings = errors).
- [ ] **G1b** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every changed/created `.cs` file — clean.
- [ ] **G2** testing-expert writes the exact test list in §2 (backend) and §3 (frontend).
- [ ] **G3** `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — green. **Expected count: 19 (baseline, Phase 5a) + tests added by this phase (enumerated in §2) = exact total confirmed at validation time, not assumed here.**
- [ ] **G4** `npm test` (storefront, Vitest) — green, including the new specs in §3.
- [ ] **G5** `npm run lint` (storefront ESLint) — clean on all new/changed files.

## 2. Code samples — files to create / modify

### A1 — `GeminiSettings.cs` (modify)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiSettings.cs` (modelled on the existing `Model` property, same file).

Add after the existing `Model` property (line 16):
```csharp
    /// <summary>
    /// Model used for text-generation calls (chatbot replies, and measurement's structured-JSON
    /// response) — distinct from <see cref="Model"/>, which is the image-generation model used by
    /// try-on. Both share the same generateContent endpoint shape but are different model families
    /// (see Phase 6 design spec §5, §7 for the verified API shape difference). Decided default,
    /// not provisional — confirmed against Google's current model catalog (design spec §7).
    /// </summary>
    [Required]
    public string TextModel { get; init; } = "gemini-2.5-flash";

    /// <summary>
    /// Total character budget across the client-held chat history sent on each <c>/api/chat</c>
    /// call (design spec §6.2), on top of the fixed "last 20 messages" cap. Decided default, not
    /// provisional — configurable per-tenant/per-deployment via this setting.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ChatHistoryMaxTotalChars { get; init; } = 8_000;
```

### A2 — `Application/Gemini/GeminiTextDtos.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiTextDtos.cs` (modelled on `GeminiDtos.cs`, same folder — separate DTO set per design spec §3.2/§5.2: text requests need `role` per turn and a top-level `systemInstruction` that the image DTOs don't have).

**Decided (resolves the measurement image-attachment gap):** the body-measurement call is a single multimodal `generateContent` request through this same text client — the photo goes in as an `inline_data` part alongside the text-prompt part, on the identical request/response shape chat uses. `GeminiTextPart` therefore needs an optional `InlineData` alongside its existing optional `Text`, exactly one of which is set per part. The casing (`inlineData`/`mimeType`/`data`) mirrors `GeminiDtos.cs`'s existing `GeminiPart.InlineData`/`GeminiInlineData` (see that file, same folder) so both DTO sets stay consistent with the Gemini shape this codebase already uses.
```csharp
using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiTextGenerateContentRequest(
    [property: JsonPropertyName("contents")] GeminiTextContent[] Contents,
    [property: JsonPropertyName("systemInstruction")] GeminiTextContent? SystemInstruction = null,
    [property: JsonPropertyName("generationConfig")] GeminiTextGenerationConfig? GenerationConfig = null);

public record GeminiTextContent(
    [property: JsonPropertyName("parts")] GeminiTextPart[] Parts,
    [property: JsonPropertyName("role")] string? Role = null);

public record GeminiTextPart(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("inlineData")] GeminiTextInlineData? InlineData = null);

public record GeminiTextInlineData(
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("data")] string Data);

public record GeminiTextGenerationConfig(
    [property: JsonPropertyName("temperature")] double? Temperature = null,
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens = null);

public record GeminiTextGenerateContentResponse(
    [property: JsonPropertyName("candidates")] GeminiTextCandidate[]? Candidates);

public record GeminiTextCandidate(
    [property: JsonPropertyName("content")] GeminiTextContent? Content);
```
`Text`-only parts (chat turns, and the measurement text prompt) continue to construct via the positional `new GeminiTextPart(someText)` call already used in D6's `ChatService` sample — `Text` stays the first parameter so existing call sites are unaffected by adding `InlineData`.

### A3 — `Application/Gemini/IGeminiTextClient.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\IGeminiTextClient.cs` (modelled on `IGeminiImageClient.cs:1-13`).
```csharp
using Refit;

namespace FashionSaaS.TryOn.Application.Gemini;

public interface IGeminiTextClient
{
    [Post("/v1beta/models/{model}:generateContent")]
    Task<GeminiTextGenerateContentResponse> GenerateContentAsync(
        string model,
        [Header("x-goog-api-key")] string apiKey,
        [Body] GeminiTextGenerateContentRequest request,
        CancellationToken cancellationToken);
}
```

### A4 — `Application/Gemini/GeminiPrompts.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiPrompts.cs` — centralizes both prompts per design spec §6 ("static, centrally defined constants — not inline strings scattered through service code").
```csharp
using System.Globalization;

namespace FashionSaaS.TryOn.Application.Gemini;

/// <summary>
/// Centralizes every prompt/persona string sent to Gemini, so prompt changes are a single-file
/// review (design spec §6) rather than edits scattered across MeasurementService/ChatService.
/// </summary>
public static class GeminiPrompts
{
    public const string MeasurementInstruction =
        """
        You are a body-measurement estimation assistant for an online clothing store. Given a
        single photo of a person and, optionally, their height in centimeters, estimate their body
        measurements. Respond with ONLY a JSON object matching this exact shape, no prose, no
        markdown fences:
        {"chestCm": number, "waistCm": number, "hipsCm": number, "shoulderWidthCm": number,
         "inseamCm": number, "recommendedSize": "XS"|"S"|"M"|"L"|"XL"|"XXL", "confidence": number between 0 and 1}
        If a height in cm is provided, use it as a scale reference for improved accuracy. If no
        height is provided, estimate proportionally and lower the confidence score accordingly.
        Never ask the user for more information — always return your best estimate in the exact
        JSON shape above.
        """;

    public static string MeasurementHeightHint(decimal? heightCm) =>
        heightCm is null
            ? string.Empty
            : $" Reference height: {heightCm.Value.ToString(CultureInfo.InvariantCulture)} cm.";

    public const string ChatPersonaAndRules =
        """
        You are the shopping assistant for this store. You help customers with fashion, sizing,
        and product questions.

        Rules you must always follow:
        1. Only answer questions about fashion, sizing, fit, materials, care instructions, or the
           products in this store. If asked about anything else (general knowledge, other brands,
           personal advice unrelated to shopping, or anything off-topic), politely decline and
           steer the conversation back to how you can help with their shopping.
        2. Never invent facts about a specific product — price, stock, materials, or availability —
           unless that fact was given to you in this conversation's product context. If you don't
           have the information, say so and suggest the customer check the product page or contact
           support.
        3. Never ask the customer for personal information (name, address, payment details, account
           credentials, or any other PII), and never repeat back any personal information the
           customer volunteers — redirect to the topic instead.
        4. Keep responses concise and friendly, in plain text (no markdown tables or code blocks).
        """;

    public static string ChatProductContextLine(string name, string description, IReadOnlyList<string> sizes) =>
        $" The customer is currently viewing: {name} — {description}. Available sizes: {string.Join(", ", sizes)}.";
}
```

### A5 — `Api/Program.cs` (modify)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Program.cs`. Insert immediately after the existing `AddRefitClient<IGeminiImageClient>()` block (after line 24):
```csharp
builder.Services.AddRefitClient<IGeminiTextClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        GeminiSettings settings = sp.GetRequiredService<IOptions<GeminiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
    });
```
(`IGeminiTextClient` resolves via the existing `using FashionSaaS.TryOn.Application.Gemini;` already at the top of the file — no new `using` needed.)

---

### B1 — `Application/Quota/IUsageQuotaService.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Quota\IUsageQuotaService.cs` — new folder, modelled on `Messaging/ITryOnEventPublisher.cs`'s one-method-interface shape.
```csharp
namespace FashionSaaS.TryOn.Application.Quota;

/// <summary>
/// The single combined ai_usage_limit pool spanning try-on, measurement, and chat (design spec §9)
/// — one number per tenant (Phase 1's SubscriptionPlan.AiUsageLimit, read via the ai_usage_limit
/// JWT claim), consumed by three independent feature tables.
/// </summary>
public interface IUsageQuotaService
{
    Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken cancellationToken);
}
```

### B2 — `Infrastructure/Quota/UsageQuotaService.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Quota\UsageQuotaService.cs` (modelled on `TryOnService`'s original inline query, `TryOnService.cs:38-43`, now extracted). **Decided sequencing:** at this point in the build order only `TryOnRequests` exists, so this initial version sums that table alone. Group C's C10 and Group D's D9 each extend this same method body with one more summed `CountAsync` call — see those tasks for the exact diff — reaching the final three-table shape (sum of three independent `CountAsync` calls rather than a cross-table UNION, per design spec §9's rationale) only once Group D lands. This version compiles standalone; there is no broken-intermediate-build gap.
```csharp
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Quota;

public class UsageQuotaService(TryOnDbContext dbContext) : IUsageQuotaService
{
    public async Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime startOfMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var tryOnCount = await dbContext.TryOnRequests
            .Where(t => t.TenantId == tenantId && t.Status == TryOnStatus.Completed && t.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return tryOnCount;
    }
}
```

### B3 — `Infrastructure/DependencyInjection.cs` (modify)
Add inside `AddTryOnInfrastructure`, after the existing `services.AddScoped<TryOn.TryOnService>();` line (line 27):
```csharp
        services.AddScoped<IUsageQuotaService, UsageQuotaService>();
```
Add `using FashionSaaS.TryOn.Application.Quota;` and `using FashionSaaS.TryOn.Infrastructure.Quota;` to the file's `using` block.

### B4 — `Infrastructure/TryOn/TryOnService.cs` (modify)
Change the constructor (currently `TryOnService.cs:19-25`) to add the new dependency and replace the inline quota query with a call to it:
```csharp
public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiImageClient geminiClient,
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiSettings> geminiOptions,
    ITryOnEventPublisher eventPublisher,
    IUsageQuotaService usageQuotaService)
{
    ...
    public async Task<(bool IsSuccess, int StatusCode, string Message, TryOnResultResponse? Data)> RenderAsync(
        TryOnRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordAsync(form, TryOnStatus.Failed, "Monthly AI try-on quota exceeded.", cancellationToken).ConfigureAwait(false);
            return (false, 429, "You've reached this month's try-on limit. Upgrade your plan or try again next month.", null);
        }
        // ... rest of method unchanged
```
Remove the now-unused inline `DateTime startOfMonth = ...` / `dbContext.TryOnRequests.Where(...).CountAsync(...)` block (`TryOnService.cs:38-43`) and the now-unnecessary `using Microsoft.EntityFrameworkCore;` if nothing else in the file needs it (confirm before removing — `RecordAsync`'s `SaveChangesAsync` doesn't need that `using`, but double-check no other EF Core extension method is used elsewhere in the file first).
Add `using FashionSaaS.TryOn.Application.Quota;` to the file's `using` block.

### B5 — `TryOnServiceTests.cs` (modify)
`E:\AIcLOTHING\services\fashionsaas-tryon\tests\FashionSaaS.TryOn.Application.Tests\TryOn\TryOnServiceTests.cs`. Add a mock field alongside the existing three (after line 20):
```csharp
    private readonly Mock<IUsageQuotaService> _usageQuota = new();
```
In `CreateService` (lines 26-44), stub the new mock and pass it into the constructor:
```csharp
    private TryOnService CreateService(TryOnDbContext dbContext, int aiUsageLimit, HttpMessageHandler? garmentHandler = null)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(Guid.NewGuid());
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        // The quota-exceeded test (RenderAsync_QuotaExceeded_ReturnsFailureWithoutCallingGemini) still
        // seeds a Completed TryOnRequest row directly into dbContext and asserts on it — but the SERVICE
        // no longer counts it itself; it asks IUsageQuotaService. So that test must also stub the mock
        // to return a used-count reflecting the seeded row (1), keeping the test's existing assertions
        // (429, no Gemini call, Failed row persisted) valid.
        _usageQuota.Setup(q => q.GetUsedThisMonthAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbContext.TryOnRequests.Count(t => t.TenantId == _tenantId && t.Status == TryOnStatus.Completed));

#pragma warning disable CA2000
        HttpMessageHandler handler = garmentHandler ?? new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
#pragma warning restore CA2000

        IOptions<GeminiSettings> options = Options.Create(new GeminiSettings { ApiKey = "test-key", Model = "test-model" });

        return new TryOnService(dbContext, _context.Object, _gemini.Object, factory.Object, options, _eventPublisher.Object, _usageQuota.Object);
    }
```
Add `using FashionSaaS.TryOn.Application.Quota;` to the test file's `using` block. **No assertion in any of the 5 existing tests changes** — only construction. This is a mechanical refactor-follow-through, not new test coverage (new quota-service coverage is Group B's own test list in §2).

---

### C1 — `Domain/MeasurementRequest.cs`, `MeasurementStatus.cs`, `SizeCode.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\MeasurementRequest.cs` (modelled on `TryOnRequest.cs`).
```csharp
namespace FashionSaaS.TryOn.Domain;

public class MeasurementRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public MeasurementStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public bool HeightCmProvided { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipsCm { get; set; }
    public decimal? ShoulderWidthCm { get; set; }
    public decimal? InseamCm { get; set; }
    public SizeCode? RecommendedSize { get; set; }
    public decimal? ConfidenceScore { get; set; }
}
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\MeasurementStatus.cs`:
```csharp
namespace FashionSaaS.TryOn.Domain;

public enum MeasurementStatus
{
    Completed,
    Failed
}
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\SizeCode.cs`:
```csharp
namespace FashionSaaS.TryOn.Domain;

public enum SizeCode
{
    Xs,
    S,
    M,
    L,
    Xl,
    Xxl
}
```

### C2 — `Infrastructure/Persistence/Configurations/MeasurementRequestConfiguration.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Persistence\Configurations\MeasurementRequestConfiguration.cs` (modelled on `TryOnRequestConfiguration.cs` — identical composite-index shape, required by `UsageQuotaService`'s query).
```csharp
using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class MeasurementRequestConfiguration : IEntityTypeConfiguration<MeasurementRequest>
{
    public void Configure(EntityTypeBuilder<MeasurementRequest> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.FailureReason).HasMaxLength(500);
        builder.Property(m => m.ChestCm).HasPrecision(5, 1);
        builder.Property(m => m.WaistCm).HasPrecision(5, 1);
        builder.Property(m => m.HipsCm).HasPrecision(5, 1);
        builder.Property(m => m.ShoulderWidthCm).HasPrecision(5, 1);
        builder.Property(m => m.InseamCm).HasPrecision(5, 1);
        builder.Property(m => m.ConfidenceScore).HasPrecision(3, 2);

        // Same shape as TryOnRequestConfiguration's index — IUsageQuotaService.GetUsedThisMonthAsync
        // filters WHERE TenantId = X AND Status = Completed AND CreatedAt >= start-of-month.
        builder.HasIndex(m => new { m.TenantId, m.Status, m.CreatedAt });
    }
}
```

### C3 — `TryOnDbContext.cs` (modify)
Add after the existing `DbSet<TryOnRequest>` line (`TryOnDbContext.cs:8`):
```csharp
    public DbSet<MeasurementRequest> MeasurementRequests => Set<MeasurementRequest>();
```

### C4 — EF Core migration
Run from `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/`:
```
dotnet ef migrations add AddMeasurementRequest --startup-project ../FashionSaaS.TryOn.Api
```
Confirm the generated migration only adds the `MeasurementRequests` table + its composite index — no unrelated model drift from `TryOnRequests`.

### C5 — `Application/Gemini/GeminiMeasurementResult.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiMeasurementResult.cs` — parse-only DTO for Gemini's JSON reply (design spec §6.1).
```csharp
using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiMeasurementResult(
    [property: JsonPropertyName("chestCm")] decimal ChestCm,
    [property: JsonPropertyName("waistCm")] decimal WaistCm,
    [property: JsonPropertyName("hipsCm")] decimal HipsCm,
    [property: JsonPropertyName("shoulderWidthCm")] decimal ShoulderWidthCm,
    [property: JsonPropertyName("inseamCm")] decimal InseamCm,
    [property: JsonPropertyName("recommendedSize")] string RecommendedSize,
    [property: JsonPropertyName("confidence")] decimal Confidence);
```

### C6 — `Application/Measurement/MeasurementRequestForm.cs`, `MeasurementRequestFormValidator.cs`, `MeasurementResultResponse.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Measurement\MeasurementRequestForm.cs` (modelled on `TryOn/TryOnRequestForm.cs`):
```csharp
using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Application.Measurement;

public class MeasurementRequestForm
{
    public required IFormFile Photo { get; init; }
    public decimal? HeightCm { get; init; }
}
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Measurement\MeasurementRequestFormValidator.cs` (modelled on `TryOn/TryOnRequestFormValidator.cs`; no host-allowlist rule needed — measurement never fetches a remote URL):
```csharp
using FluentValidation;

namespace FashionSaaS.TryOn.Application.Measurement;

public class MeasurementRequestFormValidator : AbstractValidator<MeasurementRequestForm>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png"];
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB, matches TryOnRequestFormValidator

    public MeasurementRequestFormValidator()
    {
        RuleFor(x => x.Photo)
            .Must(f => AllowedContentTypes.Contains(f.ContentType))
            .WithMessage("Photo must be a JPEG or PNG image.")
            .Must(f => f.Length > 0 && f.Length <= MaxPhotoBytes)
            .WithMessage("Photo must be between 1 byte and 10 MB.");

        RuleFor(x => x.HeightCm)
            .InclusiveBetween(50, 250)
            .When(x => x.HeightCm.HasValue)
            .WithMessage("HeightCm must be between 50 and 250 if provided.");
    }
}
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Measurement\MeasurementResultResponse.cs`:
```csharp
using FashionSaaS.TryOn.Domain;

namespace FashionSaaS.TryOn.Application.Measurement;

public record MeasurementResultResponse(
    decimal ChestCm,
    decimal WaistCm,
    decimal HipsCm,
    decimal ShoulderWidthCm,
    decimal InseamCm,
    SizeCode RecommendedSize,
    decimal Confidence);
```

### C7 — `Infrastructure/Measurement/MeasurementService.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Measurement\MeasurementService.cs` (modelled on `TryOn/TryOnService.cs`'s overall shape — quota check, in-memory photo handling, Gemini call, persist, return — with the vendor call and result parsing swapped for measurement's text/JSON reply instead of an image).
```csharp
using System.Text.Json;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Measurement;

public class MeasurementService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiTextClient geminiClient,
    IOptions<GeminiSettings> geminiOptions,
    IUsageQuotaService usageQuotaService)
{
    private readonly GeminiSettings _gemini = geminiOptions.Value;

    public async Task<(bool IsSuccess, int StatusCode, string Message, MeasurementResultResponse? Data)> EstimateAsync(
        MeasurementRequestForm form, CancellationToken cancellationToken)
    {
        var usedThisMonth = await usageQuotaService.GetUsedThisMonthAsync(currentContext.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordAsync(form, MeasurementStatus.Failed, "Monthly AI usage quota exceeded.", null, cancellationToken).ConfigureAwait(false);
            return (false, 429, "You've reached this month's AI usage limit. Upgrade your plan or try again next month.", null);
        }

        byte[] photoBytes;
        await using (Stream stream = form.Photo.OpenReadStream())
        await using (MemoryStream memory = new())
        {
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            photoBytes = memory.ToArray();
        }

        GeminiTextGenerateContentResponse response;
        try
        {
            var promptText = GeminiPrompts.MeasurementInstruction + GeminiPrompts.MeasurementHeightHint(form.HeightCm);
            GeminiTextGenerateContentRequest request = new(
                Contents:
                [
                    new GeminiTextContent(
                        Parts:
                        [
                            new GeminiTextPart(Text: promptText),
                            new GeminiTextPart(InlineData: new GeminiTextInlineData(
                                MimeType: form.Photo.ContentType,
                                Data: Convert.ToBase64String(photoBytes)))
                        ],
                        Role: "user")
                ]);
            response = await geminiClient.GenerateContentAsync(_gemini.TextModel, _gemini.ApiKey, request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAsync(form, MeasurementStatus.Failed, $"Gemini API error: {ex.Message}", null, cancellationToken).ConfigureAwait(false);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        var replyText = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.Text))?.Text;

        GeminiMeasurementResult? parsed = null;
        if (replyText is not null)
        {
            try
            {
                parsed = JsonSerializer.Deserialize<GeminiMeasurementResult>(replyText);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (parsed is null || !Enum.TryParse<SizeCode>(parsed.RecommendedSize, ignoreCase: true, out SizeCode recommendedSize))
        {
            await RecordAsync(form, MeasurementStatus.Failed, "Could not parse measurement response.", null, cancellationToken).ConfigureAwait(false);
            return (false, 502, "The measurement estimate failed. Please try again in a moment.", null);
        }

        MeasurementResultResponse result = new(
            parsed.ChestCm, parsed.WaistCm, parsed.HipsCm, parsed.ShoulderWidthCm, parsed.InseamCm,
            recommendedSize, parsed.Confidence);

        await RecordAsync(form, MeasurementStatus.Completed, null, result, cancellationToken).ConfigureAwait(false);
        return (true, 200, "Success", result);
    }

    private async Task<MeasurementRequest> RecordAsync(
        MeasurementRequestForm form, MeasurementStatus status, string? failureReason,
        MeasurementResultResponse? result, CancellationToken cancellationToken)
    {
        MeasurementRequest entity = new()
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            Status = status,
            FailureReason = failureReason,
            HeightCmProvided = form.HeightCm.HasValue,
            ChestCm = result?.ChestCm,
            WaistCm = result?.WaistCm,
            HipsCm = result?.HipsCm,
            ShoulderWidthCm = result?.ShoulderWidthCm,
            InseamCm = result?.InseamCm,
            RecommendedSize = result?.RecommendedSize,
            ConfidenceScore = result?.Confidence
        };
        dbContext.MeasurementRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }
}
```
**Resolved:** this method now attaches the photo as an `inline_data` part (base64 `photoBytes` + `form.Photo.ContentType`) alongside the text-prompt part, both under a single `user`-role `GeminiTextContent`, per the decision recorded against `GeminiTextDtos.cs` (§A2) and design spec §5.1. The previous version of this sample read `photoBytes` into memory and never attached them to the request — that gap is fixed above.

### C8 — `Api/Controllers/MeasureController.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Controllers\MeasureController.cs` (modelled verbatim on `TryOnController.cs`, including its `CA1515` suppression rationale).
```csharp
using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.Measurement;
using FashionSaaS.TryOn.Infrastructure.Measurement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/measure")]
[Authorize]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class MeasureController(MeasurementService measurementService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PostAsync([FromForm] MeasurementRequestForm form, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, MeasurementResultResponse? data) = await measurementService.EstimateAsync(form, cancellationToken);

        ResponseData<MeasurementResultResponse> response = isSuccess
            ? ResponseData<MeasurementResultResponse>.Success(data!, message, statusCode)
            : ResponseData<MeasurementResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
```

### C9 — `Infrastructure/DependencyInjection.cs` (modify)
Add alongside the existing `services.AddScoped<TryOn.TryOnService>();`:
```csharp
        services.AddScoped<Measurement.MeasurementService>();
```

### C10 — `Infrastructure/Quota/UsageQuotaService.cs` (modify — extends B2)
Now that C3 has added `TryOnDbContext.MeasurementRequests`, extend the method body added in B2 with a second summed term:
```csharp
        var measurementCount = await dbContext.MeasurementRequests
            .Where(m => m.TenantId == tenantId && m.Status == MeasurementStatus.Completed && m.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return tryOnCount + measurementCount;
```
(This replaces B2's `return tryOnCount;` line.) Add `UsageQuotaService_GetUsedThisMonthAsync_SumsTryOnAndMeasurementForTenant` to `FashionSaaS.TryOn.Infrastructure.Tests` (exact test in §3) — seeds one `Completed` row in each of `TryOnRequests`/`MeasurementRequests` for the same tenant, asserts the returned count is 2.

---

### D1 — `Domain/ChatRequest.cs`, `ChatRequestStatus.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\ChatRequest.cs`:
```csharp
namespace FashionSaaS.TryOn.Domain;

public class ChatRequest : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public ChatRequestStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public int MessageLength { get; set; }
    public int ReplyLength { get; set; }
    public bool HadProductContext { get; set; }
}
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\ChatRequestStatus.cs`:
```csharp
namespace FashionSaaS.TryOn.Domain;

public enum ChatRequestStatus
{
    Completed,
    Failed
}
```

### D2 — `Infrastructure/Persistence/Configurations/ChatRequestConfiguration.cs` (create)
```csharp
using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class ChatRequestConfiguration : IEntityTypeConfiguration<ChatRequest>
{
    public void Configure(EntityTypeBuilder<ChatRequest> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FailureReason).HasMaxLength(500);

        // Same shape as TryOnRequestConfiguration's index — required by IUsageQuotaService.
        builder.HasIndex(c => new { c.TenantId, c.Status, c.CreatedAt });
    }
}
```

### D3 — `TryOnDbContext.cs` (modify, second addition)
```csharp
    public DbSet<ChatRequest> ChatRequests => Set<ChatRequest>();
```

### D4 — EF Core migration
```
dotnet ef migrations add AddChatRequest --startup-project ../FashionSaaS.TryOn.Api
```

### D5 — `Application/Chat/*.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Chat\ChatMessage.cs`:
```csharp
namespace FashionSaaS.TryOn.Application.Chat;

public record ChatMessage(string Role, string Content);
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Chat\ChatProductContext.cs`:
```csharp
namespace FashionSaaS.TryOn.Application.Chat;

public record ChatProductContext(string Name, string Description, IReadOnlyList<string> Sizes);
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Chat\ChatRequestDto.cs`:
```csharp
namespace FashionSaaS.TryOn.Application.Chat;

public record ChatRequestDto(IReadOnlyList<ChatMessage> Messages, ChatProductContext? ProductContext);
```
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Chat\ChatRequestValidator.cs` (FluentValidation, input-shape only per CONVENTIONS §8 — the message-array cap is D5's locked "last 20"; the total-char ceiling is `GeminiSettings.ChatHistoryMaxTotalChars`, decided default 8,000, §A1):
```csharp
using FluentValidation;
using FashionSaaS.TryOn.Application.Gemini;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Application.Chat;

public class ChatRequestValidator : AbstractValidator<ChatRequestDto>
{
    private const int MaxMessages = 20;

    public ChatRequestValidator(IOptions<GeminiSettings> geminiOptions)
    {
        var maxTotalChars = geminiOptions.Value.ChatHistoryMaxTotalChars;

        RuleFor(x => x.Messages)
            .NotEmpty()
            .WithMessage("At least one message is required.")
            .Must(m => m.Count <= MaxMessages)
            .WithMessage($"No more than {MaxMessages} messages may be sent.")
            .Must(m => m.Sum(msg => msg.Content.Length) <= maxTotalChars)
            .WithMessage($"Total message content must not exceed {maxTotalChars} characters.");

        RuleForEach(x => x.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Role).Must(r => r is "user" or "model").WithMessage("Role must be 'user' or 'model'.");
            message.RuleFor(m => m.Content).NotEmpty();
        });
    }
}
```
FluentValidation's `AddValidatorsFromAssembly` (already wired in `Program.cs`, per the header's contract checklist) resolves constructor dependencies through DI, so no extra registration is needed beyond `GeminiSettings` already being bound as `IOptions<GeminiSettings>`.
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Chat\ChatResultResponse.cs`:
```csharp
namespace FashionSaaS.TryOn.Application.Chat;

public record ChatResultResponse(string Reply);
```

### D6 — `Infrastructure/Chat/ChatService.cs` (create)
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Chat\ChatService.cs`.
```csharp
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.Quota;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

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

        var latestMessage = dto.Messages[^1];

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
```

### D7 — `Api/Controllers/ChatController.cs` (create)
```csharp
using System.Diagnostics.CodeAnalysis;
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.Chat;
using FashionSaaS.TryOn.Infrastructure.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "ASP.NET Core MVC controller discovery requires public top-level classes.")]
public class ChatController(ChatService chatService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] ChatRequestDto dto, CancellationToken cancellationToken)
    {
        (var isSuccess, var statusCode, var message, ChatResultResponse? data) = await chatService.ReplyAsync(dto, cancellationToken);

        ResponseData<ChatResultResponse> response = isSuccess
            ? ResponseData<ChatResultResponse>.Success(data!, message, statusCode)
            : ResponseData<ChatResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
```

### D8 — `Infrastructure/DependencyInjection.cs` (modify)
```csharp
        services.AddScoped<Chat.ChatService>();
```

### D9 — `Infrastructure/Quota/UsageQuotaService.cs` (modify — extends C10)
Now that D3 has added `TryOnDbContext.ChatRequests`, extend the method body once more with the third and final summed term:
```csharp
        var chatCount = await dbContext.ChatRequests
            .Where(c => c.TenantId == tenantId && c.Status == ChatRequestStatus.Completed && c.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        return tryOnCount + measurementCount + chatCount;
```
(This replaces C10's `return tryOnCount + measurementCount;` line — the method now matches design spec §9's three-table shape exactly, and `UsageQuotaService.cs` never contained a reference to a table that didn't yet exist at any point in the build sequence.) Add `UsageQuotaService_GetUsedThisMonthAsync_SumsAllThreeTablesForTenant` (exact test in §3) to `FashionSaaS.TryOn.Infrastructure.Tests`.

---

### E1 — `features/catalog/models/measurement.model.ts` (create)
`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\models\measurement.model.ts` (modelled on `try-on.model.ts`):
```typescript
export interface MeasurementApiResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message: string;
  data: T | null;
  errors: string[] | null;
}

export interface MeasurementResult {
  chestCm: number;
  waistCm: number;
  hipsCm: number;
  shoulderWidthCm: number;
  inseamCm: number;
  recommendedSize: string;
  confidence: number;
}
```

### E2 — `features/catalog/services/measurement.service.ts` (create)
`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\services\measurement.service.ts` (modelled directly on `try-on.service.ts`):
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { MeasurementApiResponse, MeasurementResult } from '../models/measurement.model';

@Injectable({ providedIn: 'root' })
export class MeasurementService {
  constructor(private http: HttpClient) {}

  estimate(photo: File, heightCm?: number): Observable<MeasurementResult> {
    const formData = new FormData();
    formData.append('photo', photo);
    if (heightCm) {
      formData.append('heightCm', heightCm.toString());
    }

    return this.http
      .post<MeasurementApiResponse<MeasurementResult>>(`${environment.tryOnApiBaseUrl}/measure`, formData)
      .pipe(
        map((response) => {
          if (!response.data) {
            throw new Error(response.message || 'Measurement estimate failed.');
          }
          return response.data;
        })
      );
  }
}
```

### E3/E4 — `product-detail.component.ts` / `.html` (modify)
Add alongside the existing Try It On state (after `tryOnError$`, `product-detail.component.ts:34`):
```typescript
  // Find My Size state (design spec §12 — mirrors Try It On's stateless pattern)
  measurementPhotoFile: File | null = null;
  measurementHeightCm: number | null = null;
  measurementResult$ = new BehaviorSubject<MeasurementResult | null>(null);
  measurementLoading$ = new BehaviorSubject<boolean>(false);
  measurementError$ = new BehaviorSubject<string | null>(null);
```
Add `private measurementService: MeasurementService` to the constructor parameter list, alongside `private tryOnService: TryOnService`. Add `import { MeasurementService } from '../../services/measurement.service';` and `import { MeasurementResult } from '../../models/measurement.model';`.
Add methods alongside `onTryOnPhotoSelected`/`submitTryOn`:
```typescript
  onMeasurementPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.measurementPhotoFile = input.files?.[0] ?? null;
    this.measurementError$.next(null);
    this.measurementResult$.next(null);
  }

  submitMeasurement(): void {
    if (!this.measurementPhotoFile) {
      this.measurementError$.next('Please choose a photo first.');
      return;
    }

    this.measurementLoading$.next(true);
    this.measurementError$.next(null);
    this.measurementResult$.next(null);

    this.measurementService
      .estimate(this.measurementPhotoFile, this.measurementHeightCm ?? undefined)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.measurementLoading$.next(false);
          this.measurementResult$.next(result);
        },
        error: (err) => {
          this.measurementLoading$.next(false);
          const status = err?.status;
          this.measurementError$.next(
            status === 429
              ? "You've reached this month's AI usage limit. Upgrade your plan or try again next month."
              : 'The measurement estimate failed. Please try again in a moment.'
          );
        },
      });
  }

  isRecommendedSize(size: string): boolean {
    return this.measurementResult$.value?.recommendedSize?.toUpperCase() === size.toUpperCase();
  }
```
`product-detail.component.html`: add a "Find My Size" section immediately after the existing "Try It On" `<div class="try-on-section ...">` block (`product-detail.component.html:216-243`), following the identical structure — file input, submit button with loading state, error alert, and a result panel that lists the five measurements plus highlights the recommended size against `getUniqueSizes()` (e.g. `<span [class.badge-recommended]="isRecommendedSize(size)">{{ size }}</span>` inside the existing size-button loop, or a small standalone list — exact markup is an implementation-time styling choice, not a locked decision).

### E5 — environment files
No change. Confirm at implementation time that `${environment.tryOnApiBaseUrl}/measure` is reachable at the same base URL as `/tryon` (design spec §12 — same service, same base URL, no new environment key).

---

### F1 — `features/chat/models/chat.model.ts` (create)
`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\chat\models\chat.model.ts`:
```typescript
export interface ChatApiResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message: string;
  data: T | null;
  errors: string[] | null;
}

export interface ChatMessage {
  role: 'user' | 'model';
  content: string;
}

export interface ChatProductContext {
  name: string;
  description: string;
  sizes: string[];
}

export interface ChatResult {
  reply: string;
}
```

### F2 — `features/chat/services/chat.service.ts` (create)
`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\chat\services\chat.service.ts` (holds the capped client-side history per D5 — last 20 messages — and exposes it as observable state for the widget):
```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable } from 'rxjs';
import { map, tap } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { ChatApiResponse, ChatMessage, ChatProductContext, ChatResult } from '../models/chat.model';

const MAX_MESSAGES = 20;

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly messagesSubject = new BehaviorSubject<ChatMessage[]>([]);
  readonly messages$ = this.messagesSubject.asObservable();

  constructor(private http: HttpClient) {}

  sendMessage(content: string, productContext?: ChatProductContext): Observable<ChatResult> {
    const userMessage: ChatMessage = { role: 'user', content };
    const history = [...this.messagesSubject.value, userMessage].slice(-MAX_MESSAGES);
    this.messagesSubject.next(history);

    return this.http
      .post<ChatApiResponse<ChatResult>>(`${environment.tryOnApiBaseUrl}/chat`, {
        messages: history,
        productContext: productContext ?? null,
      })
      .pipe(
        map((response) => {
          if (!response.data) {
            throw new Error(response.message || 'Chat reply failed.');
          }
          return response.data;
        }),
        tap((result) => {
          const withReply = [...this.messagesSubject.value, { role: 'model', content: result.reply } as ChatMessage].slice(-MAX_MESSAGES);
          this.messagesSubject.next(withReply);
        })
      );
  }

  clear(): void {
    this.messagesSubject.next([]);
  }
}
```

### F3 — `features/chat/components/chat-widget/chat-widget.component.ts` (create)
`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\chat\components\chat-widget\chat-widget.component.ts`:
```typescript
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { BehaviorSubject } from 'rxjs';
import { ChatService } from '../../services/chat.service';
import { ChatProductContext } from '../../models/chat.model';

@Component({
  selector: 'app-chat-widget',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './chat-widget.component.html',
  styleUrls: ['./chat-widget.component.css'],
})
export class ChatWidgetComponent {
  @Input() productContext?: ChatProductContext;

  isOpen$ = new BehaviorSubject<boolean>(false);
  sending$ = new BehaviorSubject<boolean>(false);
  error$ = new BehaviorSubject<string | null>(null);
  draft = new FormControl('');

  messages$ = this.chatService.messages$;

  constructor(private chatService: ChatService) {}

  toggle(): void {
    this.isOpen$.next(!this.isOpen$.value);
  }

  send(): void {
    const content = (this.draft.value ?? '').trim();
    if (!content) {
      return;
    }

    this.sending$.next(true);
    this.error$.next(null);
    this.draft.setValue('');

    this.chatService.sendMessage(content, this.productContext).subscribe({
      next: () => this.sending$.next(false),
      error: (err) => {
        this.sending$.next(false);
        const status = err?.status;
        this.error$.next(
          status === 429
            ? "You've reached this month's AI usage limit. Upgrade your plan or try again next month."
            : 'The assistant is unavailable right now. Please try again in a moment.'
        );
      },
    });
  }
}
```
`chat-widget.component.html` — a minimal floating-button + panel structure (exact visual styling is an implementation-time choice, not a locked decision): a fixed-position toggle button, and when open, a scrollable message list (`*ngFor` over `messages$ | async`, styled by `role`), an input bound to `draft`, and a send button disabled while `sending$` is true. `chat-widget.component.css` — fixed positioning (`position: fixed; bottom/right`), consistent with a typical floating-widget pattern; no third-party CSS framework beyond what the storefront already uses (Bootstrap classes, matching the Try It On section's existing use of `btn`/`alert`/`form-control`).

### F4 — `main-layout.component.html` / `.ts` (modify)
`main-layout.component.html`: add `<app-chat-widget></app-chat-widget>` as a sibling of `<app-footer></app-footer>` (after `main-layout.component.html:6`), so it floats over every page inside the main layout (not the auth layout — a logged-out visitor on login/register doesn't need the shopping assistant).
`main-layout.component.ts`: add `ChatWidgetComponent` to the `imports` array and the import statement.

### F5 — `product-detail.component.ts` / `.html` (modify, second addition)
When the product detail page hosts `<app-chat-widget>` directly (instead of relying only on the layout-level one) it can pass richer context — but per design spec §12 the widget is added **once at the shell level**, not per-page. So instead: `product-detail.component.ts` exposes a `productContextForChat` getter/computed value (`{ name, description, sizes }` from the loaded `product`), and **the main-layout-level widget needs a way to receive it from whichever page is active.** This requires either (a) a shared `ChatContextService` that pages can call to set/clear the active product context (analogous to a lightweight, RxJS-backed shared-state service — no new library), or (b) passing it through route data. **Resolve which at implementation time — flagged in OPEN QUESTIONS §4**; the simpler option (a) is recommended: `ChatContextService` with a `BehaviorSubject<ChatProductContext | null>`, `setContext()`/`clearContext()` methods, injected by both `ProductDetailComponent` (calls `setContext()` in `ngOnInit`, `clearContext()` in `ngOnDestroy`) and `ChatWidgetComponent` (subscribes instead of taking `productContext` as an `@Input()` — revise F3's `@Input()` to a service subscription if option (a) is chosen).

## 3. Exact test list (testing-expert)

Paradigm: xUnit + FluentAssertions + Moq for backend (EF Core in-memory provider for persistence tests, `Mock<T>` for Gemini/quota/context dependencies) — identical to Phase 5a's established pattern. Vitest + `HttpClientTestingModule` + `TestBed.resetTestingModule()` for frontend — identical to `try-on.service.spec.ts`'s established pattern.

### Backend — Domain tests (`FashionSaaS.TryOn.Domain.Tests`)
- **`NewMeasurementRequest_HasNonEmptyId`** — mirrors `TryOnRequestTests.NewTryOnRequest_HasNonEmptyId`.
- **`NewMeasurementRequest_DefaultsToCompletedStatus`** — pins `MeasurementStatus.Completed` as the enum's zero value.
- **`MeasurementRequest_CanBeMarkedFailedWithReason`**.
- **`NewChatRequest_HasNonEmptyId`**.
- **`NewChatRequest_DefaultsToCompletedStatus`**.
- **`ChatRequest_CanBeMarkedFailedWithReason`**.

### Backend — Infrastructure tests (`FashionSaaS.TryOn.Infrastructure.Tests`)
Written incrementally, one group at a time, matching the sequencing decision that `UsageQuotaService` only ever references tables that already exist (§B2/§C10/§D9):
- (Group B, task B6) **`UsageQuotaService_GetUsedThisMonthAsync_SumsTryOnRequestsOnlyForTenant`** — seeds one `Completed` `TryOnRequest` row for the tenant, asserts the returned count is 1 (the only table summed at this point in the sequence).
- (Group B, task B6) **`UsageQuotaService_GetUsedThisMonthAsync_ExcludesOtherTenants`**.
- (Group B, task B6) **`UsageQuotaService_GetUsedThisMonthAsync_ExcludesFailedRows`**.
- (Group B, task B6) **`UsageQuotaService_GetUsedThisMonthAsync_ExcludesRowsBeforeStartOfMonth`**.
- (Group C) **`SaveChangesAsync_PersistsMeasurementRequest`** — mirrors `TryOnDbContextTests.SaveChangesAsync_PersistsTryOnRequest`.
- (Group C) **`MeasurementRequests_QueryByTenantAndStatus_ReturnsOnlyMatching`**.
- (Group C, task C10) **`UsageQuotaService_GetUsedThisMonthAsync_SumsTryOnAndMeasurementForTenant`** — seeds one `Completed` row in each of `TryOnRequests`/`MeasurementRequests` for the same tenant, asserts the returned count is 2.
- (Group D) **`SaveChangesAsync_PersistsChatRequest`**.
- (Group D) **`ChatRequests_QueryByTenantAndStatus_ReturnsOnlyMatching`**.
- (Group D, task D9) **`UsageQuotaService_GetUsedThisMonthAsync_SumsAllThreeTablesForTenant`** — seeds one `Completed` row in each of `TryOnRequests`/`MeasurementRequests`/`ChatRequests` for the same tenant, asserts the returned count is 3 (this is the final, design-spec-§9 shape).
> **Known coverage gap:** the month-boundary test above is timing-sensitive (depends on `DateTime.UtcNow` at test-run time); if this proves flaky, inject a clock abstraction — not attempted in this plan since `TryOnService`'s existing quota logic already has this same characteristic today (`DateTime.UtcNow` inline, `TryOnService.cs:38`) and isn't being changed beyond relocation.

### Backend — Application tests (`FashionSaaS.TryOn.Application.Tests`)
- **`MeasurementService_QuotaExceeded_ReturnsFailureWithoutCallingGemini`** — mirrors `TryOnServiceTests.RenderAsync_QuotaExceeded_ReturnsFailureWithoutCallingGemini`.
- **`MeasurementService_Success_PersistsCompletedRowWithParsedValues`**.
- **`MeasurementService_GeminiReturnsUnparseableJson_PersistsFailedRowWithReason`**.
- **`MeasurementService_GeminiReturnsInvalidSizeCode_PersistsFailedRowWithReason`**.
- **`MeasurementService_GeminiApiError_PersistsFailedRowWithReason`**.
- **`ChatService_QuotaExceeded_ReturnsFailureWithoutCallingGemini`**.
- **`ChatService_Success_PersistsCompletedRowWithLengthsNotContent`** — asserts the persisted `ChatRequest.FailureReason` is null and no property on the entity contains the raw message/reply text (guards D5's "lengths only" decision).
- **`ChatService_Success_WithProductContext_SetsHadProductContextTrue`**.
- **`ChatService_Success_WithoutProductContext_SetsHadProductContextFalse`**.
- **`ChatService_GeminiReturnsEmptyReply_PersistsFailedRowWithReason`**.
- **`ChatService_GeminiApiError_PersistsFailedRowWithReason`**.
- **`ChatRequestValidator_MoreThanTwentyMessages_FailsValidation`**.
- **`ChatRequestValidator_TotalCharsOverCap_FailsValidation`**.
- **`ChatRequestValidator_EmptyMessages_FailsValidation`**.
- **`MeasurementRequestFormValidator_HeightOutOfRange_FailsValidation`**.
- **`MeasurementRequestFormValidator_ValidHeightOrNone_PassesValidation`**.
- (Group B refactor regression) **all 5 existing `TryOnServiceTests` methods, unmodified assertions, re-run green after the `IUsageQuotaService` constructor change.**

### Backend — Api acceptance tests (`FashionSaaS.TryOn.Api.Tests`)
- **`PostMeasure_NoToken_Returns401`** — mirrors `TryOnAuthenticationAcceptanceTests.PostTryOn_NoToken_Returns401`.
- **`PostMeasure_ValidTokenSignedWithSharedSecret_PassesAuthentication`**.
- **`PostChat_NoToken_Returns401`**.
- **`PostChat_ValidTokenSignedWithSharedSecret_PassesAuthentication`**.

### Frontend — `measurement.service.spec.ts` (Vitest)
- posts multipart form data (photo + optional heightCm) to `${tryOnApiBaseUrl}/measure`.
- emits the parsed `MeasurementResult` on success.
- throws when the response envelope has no data (failure envelope), mirroring `try-on.service.spec.ts`'s three-test shape exactly.

### Frontend — `chat.service.spec.ts` (Vitest)
- posts `{ messages, productContext }` JSON to `${tryOnApiBaseUrl}/chat`.
- appends the user message to `messages$` before the response arrives (optimistic history update).
- appends the model's reply to `messages$` on success.
- caps `messages$` at 20 entries when sending beyond that count.
- throws when the response envelope has no data.

### Frontend — `product-detail.component.spec.ts` (extend existing spec)
- Find My Size section renders the recommended size highlighted against `getUniqueSizes()` output (DOM-level assertion, per the Phase 4b duplicate-render lesson already cited in Phase 5a §13 — assert rendered DOM, not just component state).
- shows the 429-specific error message on a quota-exceeded response.

### Frontend — `chat-widget.component.spec.ts` (new)
- toggling `isOpen$` shows/hides the panel (DOM-level).
- sending a message clears the draft input and disables the send control while `sending$` is true.
- displays the assistant's reply once the mocked service response resolves.

## 4. Observability

No new spans/meters — this service has no existing OpenTelemetry instrumentation beyond ASP.NET Core's defaults (unchanged from Phase 5a, which didn't add any either). Structured Serilog logging follows CONVENTIONS §9: `MeasurementService`/`ChatService` log at `Information` on completion and `Warning` on Gemini failures, matching `ServiceBusTryOnEventPublisher`'s existing `LogWarning` pattern — no chat message content or photo bytes are ever logged (CONVENTIONS §9's "never log secrets or PII", and design spec §4.2's "lengths only" rule extends to logs too).

## 5. OPEN QUESTIONS — resolution log

Four of the six questions originally raised here are now decided (2026-07-18); the plan and code samples throughout this document have been updated accordingly, so nothing below blocks execution. The remaining two (#4, #6) are frontend-only, non-blocking styling/wiring choices, each with a stated recommended default — deliberately out of scope for this resolution pass, which targeted backend sequencing/design gaps only.

1. **RESOLVED — Gemini text-model name for `GeminiSettings.TextModel`.** Decided: `gemini-2.5-flash` (§A1) — a confirmed configurable default, not provisional.
2. **RESOLVED — measurement request shape.** Decided: a single multimodal `generateContent` call through `IGeminiTextClient`, photo attached as an `inline_data` part alongside the text-prompt part on the same request (§A2, §C7; design spec §5.1). `GeminiTextPart` now carries an optional `InlineData` (mirroring `GeminiPart.InlineData` on the image DTOs, casing taken from the existing `GeminiDtos.cs`). `MeasurementService.EstimateAsync` (§C7) has been corrected to actually attach `photoBytes` to the request — the earlier sample read them into memory and never used them.
3. **RESOLVED — chat total-character cap.** Decided: 8,000 chars, a confirmed configurable default via the new `GeminiSettings.ChatHistoryMaxTotalChars` (§A1), consumed by `ChatRequestValidator` (§D5) instead of a hardcoded constant.
4. **Still open — how the storefront-wide chat widget receives per-page product context (§F5).** Two viable approaches (a shared `ChatContextService`, or route-data passthrough) — recommended default is the shared service, but not locked. *Default: shared `ChatContextService`; confirm at implementation time.* (Out of scope for this resolution pass — frontend wiring choice, not a backend sequencing/design gap.)
5. **RESOLVED — Group B/C/D build-order sequencing.** Decided: `UsageQuotaService` (§B2) is built in Group B summing **only** `TryOnRequests` — the one table that exists at that point — with the interface shaped for extension. Group C's task C10 extends it to add the `MeasurementRequests` term (with its own test) once C3 lands; Group D's task D9 extends it again to add the `ChatRequests` term (with its own test) once D3 lands. No task in this plan now references a table that doesn't yet exist at that point in the sequence, so the "build after each lettered group" instruction (§1) applies uniformly to B, C, and D with no exception needed.
6. **Still open — whether the storefront already has a customer-facing toast/alert mechanism outside the admin area.** Phase 5a §15 flagged this as unresolved for try-on too and it appears to have shipped using inline Bootstrap `alert` divs (confirmed in the current `product-detail.component.html:234` `alert alert-warning` block) rather than a toast service — this plan's Find My Size and chat-widget error displays follow that same already-established inline-alert pattern, not a toast. *Default: inline alert divs, matching the shipped Try It On section; confirm no toast service was introduced elsewhere since.* (Out of scope for this resolution pass — frontend styling choice, not a backend sequencing/design gap.)

## 6. Assumptions

- The Gemini model configured as `GeminiSettings.Model` (image generation, `gemini-2.5-flash-image` today) is a different model family from `TextModel` (`gemini-2.5-flash`, decided), and cannot itself be repurposed for text-only or JSON-structured replies — this is why measurement calls `IGeminiTextClient`/`TextModel` rather than `IGeminiImageClient`/`Model` (OPEN QUESTIONS §2, resolved).
- `SubscriptionPlan.AiUsageLimit` and its `ai_usage_limit` JWT claim are already intended by the business as one combined pool across all AI features on this tenant, not a per-feature limit that happens to be reused — this is asserted by D6 and by the absence of any per-feature limit field in the schema (design spec §3.2), not independently re-verified against product/business intent in this plan.
- No Service Bus consumer or event is needed for measurement/chat completions in this phase (design spec §15) — if that assumption is wrong, Groups C/D would need a `MeasurementCompleted`/`ChatCompleted` event mirroring `TryOnCompletedEvent`, which is not built here.
- The existing `dotnet-ef` tool and `TryOnDbContext`'s migrations assembly configuration (`Infrastructure/DependencyInjection.cs:25`, `b.MigrationsAssembly(...)`) require no changes to support two additional entity migrations — same assembly, same `DbContext`.

**No further changes to this plan will be made without your sign-off.**
