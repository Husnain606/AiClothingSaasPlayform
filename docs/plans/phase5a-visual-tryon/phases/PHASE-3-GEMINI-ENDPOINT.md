# Phase 3 — Gemini Integration & `POST /api/tryon` Endpoint (Buildable Plan)

> **STATUS — not started (2026-07-11).**

## Reference

- Master plan: [`../MASTER.md`](../MASTER.md) — locked decisions D3, D4, D8, D9, D11.
- **Dependency (consumed, not redefined):** [`PHASE-1-SCAFFOLD.md`](PHASE-1-SCAFFOLD.md) — `TryOnDbContext`, `TryOnRequest`/`TryOnStatus`, `ResponseData<T>` (`FashionSaaS.TryOn.Api.Common`). [`PHASE-2-AUTH.md`](PHASE-2-AUTH.md) — `ICurrentTryOnContext` (`TenantId`, `CustomerId`, `AiUsageLimit`), JWT Bearer auth already wired, `WhoAmIController` (deleted by this phase — Group D).

### Contract checklist (confirm against landed code before editing)

- [ ] `ICurrentTryOnContext.TenantId/.CustomerId/.AiUsageLimit` — exact property names from Phase 2, consumed by `TryOnService` unchanged.
- [ ] `TryOnDbContext.TryOnRequests` — `DbSet<TryOnRequest>` from Phase 1.
- [ ] `ResponseData<T>.Success`/`.Failure` — exact static factory signatures from Phase 1 (`FashionSaaS.TryOn.Api.Common.ResponseData<T>`).

### Gemini API facts (official docs — cited inline)

- The `generateContent` REST endpoint accepts inline image bytes via a `parts[].inline_data` object with `mime_type` and base64 `data` fields, alongside a `parts[].text` prompt part, under `contents[]`. `generationConfig.responseModalities` controls whether the response includes an image part (set to `["IMAGE"]` for image-editing/compositing tasks). Source: https://ai.google.dev/api/generate-content
- REST field names in the JSON wire format are `camelCase` (`inlineData`, `mimeType`, `generationConfig`, `responseModalities`) even though some client-SDK docs show `snake_case` — this plan uses the wire-format `camelCase` names via explicit `[JsonPropertyName]` attributes, matching the actual REST contract. Source: https://ai.google.dev/api/generate-content
- `Refit.HttpClientFactory` current stable version is `13.1.0`. Source: https://www.nuget.org/packages/refit.httpclientfactory

## 1. Ordered task checklist

### Group A — Gemini Refit client (Application + Infrastructure)

- [ ] **A1** Add the Refit package to the Application project (Refit interfaces live in Application so `TryOnService` can depend on the abstraction; the concrete `HttpClient` registration lives in Infrastructure/Api per D2's layering):

```bash
cd services/fashionsaas-tryon
dotnet add src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj package Refit --version 13.1.0
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj package Refit.HttpClientFactory --version 13.1.0
```

- [ ] **A2** Create the Gemini DTOs and `IGeminiImageClient` Refit interface (§2 code samples).
- [ ] **A3** Create `GeminiSettings` options class (§2 code sample) and add config to `appsettings.Development.json` (§2 code sample) — **placeholder API key**, real key supplied via local user-secrets or environment variable at run time, never committed.
- [ ] **A4** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json
git commit -m "feat(tryon): Gemini Refit client interface and DTOs"
```

### Group B — `TryOnService` orchestration (Application)

- [ ] **B1** Write the failing tests for `TryOnService` (§3 exact test list) — quota-exceeded short-circuit, successful render persists a `Completed` row, Gemini failure persists a `Failed` row with reason.
- [ ] **B2** Run: `dotnet test tests/FashionSaaS.TryOn.Application.Tests` (new test project — create it first per §2's project-scaffold note) — expect FAIL (`TryOnService` doesn't exist).
- [ ] **B3** Implement `TryOnService`, `TryOnResultResponse`, `TryOnRequestForm`, `TryOnRequestFormValidator` (§2 code samples).
- [ ] **B4** Run the same tests — expect PASS.
- [ ] **B5** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests services/fashionsaas-tryon/FashionSaaS.TryOn.sln
git commit -m "feat(tryon): TryOnService orchestration — quota, Gemini call, audit row"
```

### Group C — `TryOnController` + wiring, delete `WhoAmIController`

- [ ] **C1** Delete Phase 2's throwaway smoke endpoint:

```bash
rm services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/WhoAmIController.cs
```

- [ ] **C2** Create `TryOnController` (§2 code sample) — `POST /api/tryon`, `[Authorize]`, multipart form binding.
- [ ] **C3** Wire `IGeminiImageClient` (via `AddRefitClient`), `GeminiSettings` options, `TryOnService`, and FluentValidation auto-validation into `Program.cs` (§2 code sample).
- [ ] **C4** Manual verification — start the service, obtain a JWT from the main API (real login or a hand-crafted token signed with the shared dev secret), then:

```bash
curl -X POST http://localhost:5050/api/tryon \
  -H "Authorization: Bearer <token>" \
  -F "photo=@/path/to/test-photo.jpg" \
  -F "garmentImageUrl=https://res.cloudinary.com/<demo-public-image>.jpg" \
  -F "productId=<any-guid>"
```

Expect a `200` with `{"isSuccess":true, ..., "data": {"resultImageDataUri": "data:image/png;base64,..."}}` (or a `502`/`500` with a friendly message if the Gemini API key placeholder hasn't been replaced with a real key — this is expected until a real key is configured; the important verification is that quota/validation/auth all run correctly before the Gemini call fails).

- [ ] **C5** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api
git commit -m "feat(tryon): POST /api/tryon endpoint, wire Gemini client and validation"
```

### Group D — Cross-service JWT acceptance test (closes Phase 2's coverage gap)

- [ ] **D1** Write the failing integration-style test proving a JWT signed by the main API's dev secret is accepted by the TryOn service's authentication pipeline (§3 exact test list, `TryOnAuthenticationAcceptanceTests.cs`) — uses `WebApplicationFactory<Program>` against the real `AddTryOnAuthentication` pipeline, no HTTP server needed.
- [ ] **D2** Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Api.Tests` (new test project — create it first per §2's project-scaffold note) — expect FAIL.
- [ ] **D3** No production code changes needed (the pipeline already exists from Phase 2) — if the test fails for a reason other than "project doesn't exist yet," investigate before proceeding (this would indicate a real Phase 2 defect).
- [ ] **D4** Run again — expect PASS.
- [ ] **D5** Commit:

```bash
git add services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Api.Tests services/fashionsaas-tryon/FashionSaaS.TryOn.sln
git commit -m "test(tryon): cross-service JWT acceptance test"
```

### Group E — Validate

- [ ] **E1** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — zero warnings.
- [ ] **E2** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every `.cs` file touched/created in Groups A-D — clean.
- [ ] **E3** testing-expert writes/confirms the §3 exact test list.
- [ ] **E4** `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — green, exact count reported.

## 2. Code samples — files to create / modify

### Project scaffold note (needed before A2/B1/D1)

Two new test projects and one settings addition, created the same way as Phase 1's Group A:

```bash
cd services/fashionsaas-tryon
dotnet new xunit -n FashionSaaS.TryOn.Application.Tests -o tests/FashionSaaS.TryOn.Application.Tests -f net10.0
dotnet new xunit -n FashionSaaS.TryOn.Api.Tests -o tests/FashionSaaS.TryOn.Api.Tests -f net10.0
dotnet sln add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj
dotnet sln add tests/FashionSaaS.TryOn.Api.Tests/FashionSaaS.TryOn.Api.Tests.csproj
dotnet add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj reference src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj
dotnet add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj package FluentAssertions --version 6.12.1
dotnet add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj package Moq --version 4.20.72
dotnet add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory --version 10.0.9
dotnet add tests/FashionSaaS.TryOn.Application.Tests/FashionSaaS.TryOn.Application.Tests.csproj reference src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj
dotnet add tests/FashionSaaS.TryOn.Api.Tests/FashionSaaS.TryOn.Api.Tests.csproj reference src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj
dotnet add tests/FashionSaaS.TryOn.Api.Tests/FashionSaaS.TryOn.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.9
dotnet add tests/FashionSaaS.TryOn.Api.Tests/FashionSaaS.TryOn.Api.Tests.csproj package FluentAssertions --version 6.12.1
```

### A2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Gemini/GeminiDtos.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace FashionSaaS.TryOn.Application.Gemini;

public record GeminiGenerateContentRequest(
    [property: JsonPropertyName("contents")] GeminiContent[] Contents,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

public record GeminiContent(
    [property: JsonPropertyName("parts")] GeminiPart[] Parts);

public record GeminiPart(
    [property: JsonPropertyName("inlineData")] GeminiInlineData? InlineData = null,
    [property: JsonPropertyName("text")] string? Text = null);

public record GeminiInlineData(
    [property: JsonPropertyName("mimeType")] string MimeType,
    [property: JsonPropertyName("data")] string Data);

public record GeminiGenerationConfig(
    [property: JsonPropertyName("responseModalities")] string[] ResponseModalities);

public record GeminiGenerateContentResponse(
    [property: JsonPropertyName("candidates")] GeminiCandidate[]? Candidates);

public record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content);
```

### A2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Gemini/IGeminiImageClient.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\IGeminiImageClient.cs`

```csharp
using Refit;

namespace FashionSaaS.TryOn.Application.Gemini;

public interface IGeminiImageClient
{
    [Post("/v1beta/models/{model}:generateContent")]
    Task<GeminiGenerateContentResponse> GenerateContentAsync(
        string model,
        [Header("x-goog-api-key")] string apiKey,
        [Body] GeminiGenerateContentRequest request,
        CancellationToken cancellationToken);
}
```

### A3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Gemini/GeminiSettings.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Gemini\GeminiSettings.cs` (modelled on Phase 2's `JwtSettings.cs` options-with-validation shape).

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.Gemini;

public class GeminiSettings
{
    public const string SectionName = "GeminiSettings";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com";

    [Required]
    public string Model { get; init; } = "gemini-2.5-flash-image";
}
```

### A3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\appsettings.Development.json` — extend Phase 2's file:

```json
{
  "ConnectionStrings": {
    "TryOnConnection": "Server=.;Database=TryOnDb;User Id=sa;Password=12345678;Encrypt=False;TrustServerCertificate=True"
  },
  "JwtSettings": {
    "Secret": "DEV-ONLY-PlaceholderSecretKeyThatIs32Chars!!",
    "Issuer": "FashionSaaS",
    "Audience": "FashionSaaSUsers"
  },
  "GeminiSettings": {
    "ApiKey": "DEV-ONLY-REPLACE-WITH-REAL-KEY-VIA-USER-SECRETS",
    "BaseUrl": "https://generativelanguage.googleapis.com",
    "Model": "gemini-2.5-flash-image"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

### B3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnRequestForm.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\TryOn\TryOnRequestForm.cs`

```csharp
using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnRequestForm
{
    public required IFormFile Photo { get; init; }
    public required string GarmentImageUrl { get; init; }
    public required Guid ProductId { get; init; }
    public Guid? ProductVariantId { get; init; }
}
```

This requires the Application project to reference `Microsoft.AspNetCore.Http.Features` — add it:

```bash
dotnet add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj package Microsoft.AspNetCore.Http.Features --version 5.0.17
```

### B3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnRequestFormValidator.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\TryOn\TryOnRequestFormValidator.cs` (modelled on `src/FashionSaaS.Application/Categories/Validators/CreateCategoryRequestValidator.cs:1-29`; spec §12 — file type/size validated before any external call).

```csharp
using FluentValidation;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnRequestFormValidator : AbstractValidator<TryOnRequestForm>
{
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png"];
    private const long MaxPhotoBytes = 10 * 1024 * 1024; // 10 MB

    public TryOnRequestFormValidator()
    {
        RuleFor(x => x.Photo)
            .Must(f => AllowedContentTypes.Contains(f.ContentType))
            .WithMessage("Photo must be a JPEG or PNG image.")
            .Must(f => f.Length > 0 && f.Length <= MaxPhotoBytes)
            .WithMessage("Photo must be between 1 byte and 10 MB.");

        RuleFor(x => x.GarmentImageUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("GarmentImageUrl must be a valid HTTPS URL.");

        RuleFor(x => x.ProductId).NotEmpty();
    }
}
```

### B3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnResultResponse.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\TryOn\TryOnResultResponse.cs`

```csharp
namespace FashionSaaS.TryOn.Application.TryOn;

public record TryOnResultResponse(string ResultImageDataUri);
```

### B3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnService.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\TryOn\TryOnService.cs` — the core orchestration. Per spec §8/D11: `photoBytes` and the Gemini result bytes live only as local variables for the duration of this method call; neither is ever assigned to a field, added to the DbContext, or written anywhere except the final `data:` URI returned to the caller.

// ICurrentTryOnContext lives in the parent FashionSaaS.TryOn.Application namespace (Phase 2) —
// this using is required since C#'s namespace lookup does not auto-include a "parent"
// namespace string across separate files, even though this file's own namespace is nested
// under it by name.
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Application.TryOn;

public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiImageClient geminiClient,
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiSettings> geminiOptions)
{
    private readonly GeminiSettings _gemini = geminiOptions.Value;

    private const string ResultMimeType = "image/png";

    public async Task<(bool isSuccess, int statusCode, string message, TryOnResultResponse? data)> RenderAsync(
        TryOnRequestForm form, CancellationToken cancellationToken)
    {
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usedThisMonth = await dbContext.TryOnRequests
            .Where(t => t.TenantId == currentContext.TenantId
                        && t.Status == TryOnStatus.Completed
                        && t.CreatedAt >= startOfMonth)
            .CountAsync(cancellationToken);

        if (usedThisMonth >= currentContext.AiUsageLimit)
        {
            await RecordAsync(form, TryOnStatus.Failed, "Monthly AI try-on quota exceeded.", cancellationToken);
            return (false, 429, "You've reached this month's try-on limit. Upgrade your plan or try again next month.", null);
        }

        byte[] photoBytes;
        await using (var stream = form.Photo.OpenReadStream())
        await using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, cancellationToken);
            photoBytes = memory.ToArray();
        }

        byte[] garmentBytes;
        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            garmentBytes = await httpClient.GetByteArrayAsync(form.GarmentImageUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            await RecordAsync(form, TryOnStatus.Failed, $"Could not fetch garment image: {ex.Message}", cancellationToken);
            return (false, 502, "We couldn't load the product image right now. Please try again.", null);
        }

        GeminiGenerateContentResponse response;
        try
        {
            var request = new GeminiGenerateContentRequest(
                Contents:
                [
                    new GeminiContent(
                    [
                        new GeminiPart(InlineData: new GeminiInlineData("image/jpeg", Convert.ToBase64String(photoBytes))),
                        new GeminiPart(InlineData: new GeminiInlineData(ResultMimeType, Convert.ToBase64String(garmentBytes))),
                        new GeminiPart(Text: "Composite the second image (a clothing item) onto the person in the first image, keeping their pose and background. Return only the resulting image.")
                    ])
                ],
                GenerationConfig: new GeminiGenerationConfig(["IMAGE"]));

            response = await geminiClient.GenerateContentAsync(_gemini.Model, _gemini.ApiKey, request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RecordAsync(form, TryOnStatus.Failed, $"Gemini API error: {ex.Message}", cancellationToken);
            return (false, 502, "The try-on render failed. Please try again in a moment.", null);
        }

        var resultPart = response.Candidates?
            .SelectMany(c => c.Content?.Parts ?? [])
            .FirstOrDefault(p => p.InlineData is not null);

        if (resultPart?.InlineData is null)
        {
            await RecordAsync(form, TryOnStatus.Failed, "Gemini returned no image.", cancellationToken);
            return (false, 502, "The try-on render failed. Please try again in a moment.", null);
        }

        await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken);

        var dataUri = $"data:{resultPart.InlineData.MimeType};base64,{resultPart.InlineData.Data}";
        return (true, 200, "Success", new TryOnResultResponse(dataUri));
    }

    private async Task RecordAsync(TryOnRequestForm form, TryOnStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        dbContext.TryOnRequests.Add(new TryOnRequest
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = status,
            FailureReason = failureReason
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### C2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/TryOnController.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Controllers\TryOnController.cs` (modelled on `src/FashionSaaS.API/Controllers/Auth/AuthController.cs`'s controller shape — `StatusCode(response.StatusCode, response)`).

```csharp
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application.TryOn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/tryon")]
[Authorize]
public class TryOnController(TryOnService tryOnService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PostAsync([FromForm] TryOnRequestForm form, CancellationToken cancellationToken)
    {
        var (isSuccess, statusCode, message, data) = await tryOnService.RenderAsync(form, cancellationToken);

        var response = isSuccess
            ? ResponseData<TryOnResultResponse>.Success(data!, message, statusCode)
            : ResponseData<TryOnResultResponse>.Failure(message, statusCode);

        return StatusCode(response.StatusCode, response);
    }
}
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/DependencyInjection.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\DependencyInjection.cs` — add the `TryOnService` registration (Gemini client + `HttpClient` are registered in `Program.cs` since `AddRefitClient` is an `IServiceCollection` extension typically called at the composition root; keeping it there also matches D2 — Api is the composition root):

```csharp
// Add inside AddTryOnInfrastructure, after the DbContext registration:
        services.AddScoped<FashionSaaS.TryOn.Application.TryOn.TryOnService>();
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Program.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Program.cs` — extend Phase 2's file:

```csharp
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Infrastructure;
using FluentValidation;
using FluentValidation.AspNetCore;
using Refit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTryOnInfrastructure(builder.Configuration);
builder.Services.AddTryOnAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddOptions<GeminiSettings>()
    .Bind(builder.Configuration.GetSection(GeminiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRefitClient<IGeminiImageClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
    });

builder.Services.AddHttpClient(); // plain named client for the garment-image GET (TryOnService's IHttpClientFactory.CreateClient())

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(FashionSaaS.TryOn.Application.TryOn.TryOnRequestFormValidator).Assembly);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FashionSaaS.TryOn API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required for WebApplicationFactory<Program> in D1's acceptance test to locate the entry point.
public partial class Program;
```

## 3. Exact test list (testing-expert)

Paradigm: `TryOnService` tests use EF Core `InMemoryDatabase` for `TryOnDbContext` + Moq for `ICurrentTryOnContext`/`IGeminiImageClient`/`IHttpClientFactory`. The Api-level acceptance test uses `Microsoft.AspNetCore.Mvc.Testing`'s `WebApplicationFactory<Program>`.

### `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs`

```csharp
using System.Net;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Application.Gemini;
using FashionSaaS.TryOn.Application.TryOn;
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
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
    private readonly Guid _tenantId = Guid.NewGuid();

    private static TryOnDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TryOnDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private TryOnService CreateService(TryOnDbContext dbContext, int aiUsageLimit, HttpMessageHandler? garmentHandler = null)
    {
        _context.Setup(c => c.TenantId).Returns(_tenantId);
        _context.Setup(c => c.CustomerId).Returns(Guid.NewGuid());
        _context.Setup(c => c.AiUsageLimit).Returns(aiUsageLimit);

        var handler = garmentHandler ?? new StubHandler(HttpStatusCode.OK, [1, 2, 3]);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));

        var options = Options.Create(new GeminiSettings { ApiKey = "test-key", Model = "test-model" });

        return new TryOnService(dbContext, _context.Object, _gemini.Object, factory.Object, options);
    }

    private static IFormFile CreateFakePhoto()
    {
        var bytes = new byte[] { 9, 9, 9 };
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "photo.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
    }

    [Fact]
    public async Task RenderAsync_QuotaExceeded_ReturnsFailureWithoutCallingGemini()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = _tenantId, Status = TryOnStatus.Completed, CreatedAt = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, aiUsageLimit: 1);
        var form = new TryOnRequestForm { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        var (isSuccess, statusCode, _, data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(429);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // Spec §15: a quota-exceeded attempt still gets its own audit row (Status=Failed,
        // so it never counts toward the quota itself) — helps evaluate whether limits are sane.
        var failedRow = await dbContext.TryOnRequests.SingleAsync(t => t.Status == TryOnStatus.Failed);
        failedRow.FailureReason.Should().Be("Monthly AI try-on quota exceeded.");
    }

    [Fact]
    public async Task RenderAsync_Success_PersistsCompletedRowAndReturnsDataUri()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, aiUsageLimit: 10);
        var form = new TryOnRequestForm { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiGenerateContentResponse(
            [
                new GeminiCandidate(new GeminiContent([new GeminiPart(InlineData: new GeminiInlineData("image/png", "QUJD"))]))
            ]));

        var (isSuccess, statusCode, _, data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeTrue();
        statusCode.Should().Be(200);
        data!.ResultImageDataUri.Should().Be("data:image/png;base64,QUJD");

        var saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Completed);
        saved.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task RenderAsync_GeminiReturnsNoImage_PersistsFailedRowWithReason()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, aiUsageLimit: 10);
        var form = new TryOnRequestForm { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

        _gemini.Setup(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeminiGenerateContentResponse([new GeminiCandidate(new GeminiContent([new GeminiPart(Text: "no image")]))]));

        var (isSuccess, statusCode, _, data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();

        var saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
        saved.FailureReason.Should().Be("Gemini returned no image.");
    }

    [Fact]
    public async Task RenderAsync_GarmentImageFetchFails_PersistsFailedRowWithoutCallingGemini()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, aiUsageLimit: 10, garmentHandler: new StubHandler(HttpStatusCode.NotFound, []));
        var form = new TryOnRequestForm { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/missing.jpg", ProductId = Guid.NewGuid() };

        var (isSuccess, statusCode, _, data) = await service.RenderAsync(form, CancellationToken.None);

        isSuccess.Should().BeFalse();
        statusCode.Should().Be(502);
        data.Should().BeNull();
        _gemini.Verify(g => g.GenerateContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeminiGenerateContentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var saved = await dbContext.TryOnRequests.SingleAsync();
        saved.Status.Should().Be(TryOnStatus.Failed);
    }
}

// Minimal fake HttpMessageHandler for the garment-image GET — avoids a real network call in a unit test.
internal class StubHandler(HttpStatusCode statusCode, byte[] body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(body) };
        if (statusCode != HttpStatusCode.OK)
        {
            response.EnsureSuccessStatusCode(); // throws HttpRequestException, matching real HttpClient behavior on 404
        }
        return Task.FromResult(response);
    }
}
```

> Note: `StubHandler.SendAsync` calling `EnsureSuccessStatusCode()` on a non-OK response throws synchronously inside the method — this is intentional and mirrors what `HttpClient.GetByteArrayAsync` does internally on a non-success status, so `RenderAsync_GarmentImageFetchFails...` exercises the real `HttpRequestException` catch path in `TryOnService.RenderAsync`.

### `tests/FashionSaaS.TryOn.Api.Tests/TryOnAuthenticationAcceptanceTests.cs`

Closes Phase 2's flagged coverage gap (spec §13: "an integration test in the try-on service's test suite constructing a token with the same shared secret").

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using FluentAssertions;

namespace FashionSaaS.TryOn.Api.Tests;

public class TryOnAuthenticationAcceptanceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevSecret = "DEV-ONLY-PlaceholderSecretKeyThatIs32Chars!!";

    private static string IssueToken(Guid tenantId, Guid customerId, int aiUsageLimit)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(DevSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("ai_usage_limit", aiUsageLimit.ToString())
        };
        var token = new JwtSecurityToken("FashionSaaS", "FashionSaaSUsers", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task PostTryOn_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/tryon", new MultipartFormDataContent());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTryOn_ValidTokenSignedWithSharedSecret_PassesAuthentication()
    {
        var token = IssueToken(Guid.NewGuid(), Guid.NewGuid(), aiUsageLimit: 10);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/tryon", new MultipartFormDataContent());

        // A missing multipart body fails FluentValidation (400), not authentication (401/403) —
        // this proves the JWT passed the pipeline and the request reached the controller.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
```

> **Known coverage gap:** no automated test exercises a *real* Gemini API call (Group C4 is manual-only) — verifying actual Gemini response parsing against the live API is out of scope for unit/integration tests per this codebase's existing convention (no repository test hits a real external HTTP dependency either, e.g. Cloudinary in the main API has no live-network test).

## 4. Observability

- None added — no Serilog wiring exists in this service yet (consistent with Phase 1/2's YAGNI note). If a future phase needs to debug real Gemini failures in production, add Serilog matching `src/FashionSaaS.API/Program.cs:16-27`'s pattern at that time.

## 5. OPEN QUESTIONS (decisions, not facts)

1. **Exact Gemini model name and pricing tier** — this plan uses `gemini-2.5-flash-image` per the spec's own noted default; Google's model catalog changes quickly. *Default: verify the exact current model identifier against https://ai.google.dev/gemini-api/docs (or the Microsoft/Google model catalog if surfaced there) immediately before running Group A — do not trust this plan's model string without a live check.*
2. **Where does the real Gemini API key live in non-dev environments?** *Default: environment variable / secret manager injected into `GeminiSettings:ApiKey` at deploy time, following whatever secret-injection mechanism the main API already uses for `Cloudinary`/`SmtpSettings` credentials in production (not established in this plan — infra concern, confirm the existing mechanism before Group A's dev placeholder is replaced for a real deployment).*

## 6. Assumptions

- `Microsoft.AspNetCore.Mvc.Testing` version `10.0.9` matches the already-pinned `10.0.9` ASP.NET Core package family used elsewhere in this repo.
- The garment image referenced by `GarmentImageUrl` is always publicly fetchable over plain HTTPS (a Cloudinary URL, per spec §5) — no signed-URL/auth-header fetch logic is needed.
