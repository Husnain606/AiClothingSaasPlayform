# Phase 4 — Azure Service Bus Publish-Only Event (Buildable Plan)

> **STATUS — not started (2026-07-11).**

## Reference

- Master plan: [`../MASTER.md`](../MASTER.md) — locked decision D10.
- **Dependency (consumed, not redefined):** [`PHASE-3-GEMINI-ENDPOINT.md`](PHASE-3-GEMINI-ENDPOINT.md) — `TryOnService.RenderAsync`'s success path (the `RecordAsync(form, TryOnStatus.Completed, null, cancellationToken)` call site), `TryOnRequest` entity (`Id`, `TenantId`, `CustomerId`, `ProductId`, `CreatedAt`).

### Contract checklist (confirm against landed code before editing)

- [ ] `TryOnService.RenderAsync` (Phase 3) — the exact point after `await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken);` and before building `dataUri`, where the publish call is inserted.
- [ ] `TryOnRequest` fields (`Id`, `TenantId`, `CustomerId`, `ProductId`, `CreatedAt`) — unchanged from Phase 1, all needed for the event payload (spec §9).

### Azure Service Bus API facts (official docs — cited inline)

- Current stable package: `Azure.Messaging.ServiceBus` version `7.20.1`. Source: https://www.nuget.org/packages/Azure.Messaging.ServiceBus
- `ServiceBusClient`, `ServiceBusSender` are safe to cache and reuse as singletons for the app's lifetime; both are `IAsyncDisposable`. Source: https://learn.microsoft.com/en-us/dotnet/api/overview/azure/messaging.servicebus-readme?view=azure-dotnet
- A real local-dev option exists: the official **Azure Service Bus emulator** (`microsoft/azure-messaging-servicebus-emulator` Docker image), AMQP port `5672`, HTTP admin port `5300`, entities (topics/subscriptions) declared via a JSON config file mounted into the container. Explicitly dev/test-only — "no official support," "don't use for production." Source: https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator, https://hub.docker.com/r/microsoft/azure-messaging-servicebus-emulator
- The emulator's fixed local connection string shape is `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;` — **verify this exact string against the live emulator quickstart before running Group C**, since this is flagged by the spec as a fast-moving area (§15 open item). Source: https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator

## 1. Ordered task checklist

### Group A — `ITryOnEventPublisher` abstraction + `TryOnCompletedEvent`

- [ ] **A1** Add the package to the Infrastructure project:

```bash
cd services/fashionsaas-tryon
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj package Azure.Messaging.ServiceBus --version 7.20.1
```

- [ ] **A2** Create `TryOnCompletedEvent` (Application, so `TryOnService` can construct one without depending on Infrastructure) and `ITryOnEventPublisher` (Application interface — D2's cross-layer-interface rule: the interface lives where it's consumed, the implementation lives in Infrastructure) (§2 code samples).
- [ ] **A3** Create `ServiceBusSettings` options class (§2 code sample) and add config (§2 code sample).
- [ ] **A4** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application
git commit -m "feat(tryon): TryOnCompletedEvent and ITryOnEventPublisher abstraction"
```

### Group B — `ServiceBusTryOnEventPublisher` implementation

- [ ] **B1** Write the failing test for the publisher (§3 exact test list) — proves it calls `ServiceBusSender.SendMessageAsync` with the correct JSON payload, and proves a send failure is swallowed (logged, not thrown) so the caller's request never fails because of a messaging outage.
- [ ] **B2** Run: `dotnet test tests/FashionSaaS.TryOn.Infrastructure.Tests --filter ServiceBusTryOnEventPublisherTests` — expect FAIL.
- [ ] **B3** Implement `ServiceBusTryOnEventPublisher` (§2 code sample).
- [ ] **B4** Run again — expect PASS.
- [ ] **B5** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests
git commit -m "feat(tryon): ServiceBusTryOnEventPublisher — publish-only, swallows send failures"
```

### Group C — Wire into `TryOnService` + DI + local emulator config

- [ ] **C1** Update `TryOnService.RenderAsync` to publish after a successful `Completed` record (§2 code sample — modifies Phase 3's file).
- [ ] **C2** Update `TryOnServiceTests.cs` (Phase 3) — the `TryOnService` constructor now takes one more dependency (`ITryOnEventPublisher`); update `CreateService` and add a new assertion to `RenderAsync_Success_PersistsCompletedRowAndReturnsDataUri` (§3 exact test list — modifies Phase 3's test file).
- [ ] **C3** Register `ServiceBusClient` as a singleton and `ITryOnEventPublisher` in DI (§2 code sample — modifies `DependencyInjection.cs` and `Program.cs`).
- [ ] **C4** Add `appsettings.Development.json`'s `ServiceBusSettings` section (§2 code sample).
- [ ] **C5** Create the local emulator config file `services/fashionsaas-tryon/servicebus-emulator-config.json` and a short `docker-compose.servicebus.yml` for running it (§2 code samples) — dev-only tooling, not part of the deployed service.
- [ ] **C6** Manual verification — start the emulator (`docker compose -f services/fashionsaas-tryon/docker-compose.servicebus.yml up -d`), start the TryOn service, drive a successful `POST /api/tryon` (Phase 3's Group C4 flow with a real Gemini key), and confirm no exception surfaces to the HTTP response even if the emulator is stopped mid-test (simulating a Service Bus outage) — stop the emulator, repeat the same request, and confirm the customer still gets their `200` render result.
- [ ] **C7** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Application.Tests services/fashionsaas-tryon/docker-compose.servicebus.yml services/fashionsaas-tryon/servicebus-emulator-config.json
git commit -m "feat(tryon): publish TryOnCompleted after a successful render; local emulator setup"
```

### Group D — Validate

- [ ] **D1** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — zero warnings.
- [ ] **D2** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every `.cs` file touched/created in Groups A-C — clean.
- [ ] **D3** testing-expert writes/confirms the §3 exact test list.
- [ ] **D4** `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — green, exact count reported.

## 2. Code samples — files to create / modify

### A2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Messaging/TryOnCompletedEvent.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Messaging\TryOnCompletedEvent.cs` (payload shape verbatim from spec §9).

```csharp
namespace FashionSaaS.TryOn.Application.Messaging;

public record TryOnCompletedEvent(
    Guid TryOnRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid ProductId,
    DateTime CreatedAt);
```

### A2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Messaging/ITryOnEventPublisher.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Messaging\ITryOnEventPublisher.cs`

```csharp
namespace FashionSaaS.TryOn.Application.Messaging;

public interface ITryOnEventPublisher
{
    /// <summary>
    /// Publishes a TryOnCompleted event. Implementations must never throw — a messaging
    /// outage must not fail the customer-facing try-on request (spec §9: publish-only,
    /// side-channel, not the source of truth).
    /// </summary>
    Task PublishAsync(TryOnCompletedEvent @event, CancellationToken cancellationToken);
}
```

### A3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Messaging/ServiceBusSettings.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\Messaging\ServiceBusSettings.cs` (modelled on `GeminiSettings.cs`'s options-with-validation shape).

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application.Messaging;

public class ServiceBusSettings
{
    public const string SectionName = "ServiceBusSettings";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string TopicName { get; init; } = "tryon-events";
}
```

### C4 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\appsettings.Development.json` — extend Phase 3's file:

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
  "ServiceBusSettings": {
    "ConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "TopicName": "tryon-events"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

### C5 — `services/fashionsaas-tryon/servicebus-emulator-config.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\servicebus-emulator-config.json` — declares the `tryon-events` topic (no subscription needed — publish-only per D10). **Verify this schema against the live emulator docs before use** (Group C6) — the emulator's config format is a fast-moving area per this plan's risk note.

```json
{
  "UserConfig": {
    "Namespaces": [
      {
        "Name": "sbemulatorns",
        "Topics": [
          {
            "Name": "tryon-events",
            "Properties": {
              "DefaultMessageTimeToLive": "PT1H"
            },
            "Subscriptions": []
          }
        ]
      }
    ],
    "Logging": { "Type": "Console" }
  }
}
```

### C5 — `services/fashionsaas-tryon/docker-compose.servicebus.yml`

`E:\AIcLOTHING\services\fashionsaas-tryon\docker-compose.servicebus.yml` (dev-only tooling — modelled on the official emulator's documented compose shape).

```yaml
services:
  servicebus-emulator:
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    container_name: tryon-servicebus-emulator
    ports:
      - "5672:5672"
      - "5300:5300"
    volumes:
      - ./servicebus-emulator-config.json:/ServiceBus_Emulator/ConfigFiles/Config.json
    environment:
      - SQL_SERVER=servicebus-sql
      - MSSQL_SA_PASSWORD=DevOnly_Passw0rd!
      - ACCEPT_EULA=Y
    depends_on:
      - servicebus-sql
  servicebus-sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: tryon-servicebus-sql
    environment:
      - ACCEPT_EULA=Y
      - MSSQL_SA_PASSWORD=DevOnly_Passw0rd!
```

### B3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Messaging/ServiceBusTryOnEventPublisher.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Messaging\ServiceBusTryOnEventPublisher.cs`

```csharp
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FashionSaaS.TryOn.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Messaging;

public class ServiceBusTryOnEventPublisher(
    ServiceBusClient client,
    IOptions<ServiceBusSettings> settings,
    ILogger<ServiceBusTryOnEventPublisher> logger) : ITryOnEventPublisher
{
    private readonly string _topicName = settings.Value.TopicName;

    public async Task PublishAsync(TryOnCompletedEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await using var sender = client.CreateSender(_topicName);
            var body = JsonSerializer.Serialize(@event);
            var message = new ServiceBusMessage(body) { ContentType = "application/json" };
            await sender.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is ServiceBusException or InvalidOperationException)
        {
            // Publish-only side-channel (spec §9) — a Service Bus outage must never fail the
            // customer-facing try-on request. Logged and swallowed, not rethrown.
            logger.LogWarning(ex, "Failed to publish TryOnCompleted event for TryOnRequestId {TryOnRequestId}", @event.TryOnRequestId);
        }
    }
}
```

### C1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/TryOn/TryOnService.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\TryOn\TryOnService.cs` — modify the constructor and the success path (Phase 3's file):

```csharp
// Before (constructor):
public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiImageClient geminiClient,
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiSettings> geminiOptions)

// After:
public class TryOnService(
    TryOnDbContext dbContext,
    ICurrentTryOnContext currentContext,
    IGeminiImageClient geminiClient,
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiSettings> geminiOptions,
    ITryOnEventPublisher eventPublisher)
```

```csharp
// Before (the success path, after the resultPart null-check):
        await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken);

        var dataUri = $"data:{resultPart.InlineData.MimeType};base64,{resultPart.InlineData.Data}";
        return (true, 200, "Success", new TryOnResultResponse(dataUri));

// After — RecordAsync now needs to return the saved entity so the event can carry its Id:
        var saved = await RecordAsync(form, TryOnStatus.Completed, null, cancellationToken);
        await eventPublisher.PublishAsync(
            new TryOnCompletedEvent(saved.Id, saved.TenantId, saved.CustomerId, saved.ProductId, saved.CreatedAt),
            cancellationToken);

        var dataUri = $"data:{resultPart.InlineData.MimeType};base64,{resultPart.InlineData.Data}";
        return (true, 200, "Success", new TryOnResultResponse(dataUri));
```

```csharp
// Before (RecordAsync):
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

// After — return the entity:
    private async Task<TryOnRequest> RecordAsync(TryOnRequestForm form, TryOnStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        var entity = new TryOnRequest
        {
            TenantId = currentContext.TenantId,
            CustomerId = currentContext.CustomerId,
            ProductId = form.ProductId,
            ProductVariantId = form.ProductVariantId,
            Status = status,
            FailureReason = failureReason
        };
        dbContext.TryOnRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }
```

Add the using directive: `using FashionSaaS.TryOn.Application.Messaging;`.

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/DependencyInjection.cs`

Add inside `AddTryOnInfrastructure`, after the `TryOnService` scoped registration:

```csharp
        services.AddOptions<ServiceBusSettings>()
            .Bind(configuration.GetSection(ServiceBusSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
            new ServiceBusClient(sp.GetRequiredService<IOptions<ServiceBusSettings>>().Value.ConnectionString));
        services.AddScoped<ITryOnEventPublisher, ServiceBusTryOnEventPublisher>();
```

Add using directives: `using FashionSaaS.TryOn.Application.Messaging;`, `using FashionSaaS.TryOn.Infrastructure.Messaging;`, `using Azure.Messaging.ServiceBus;`, `using Microsoft.Extensions.Options;`.

## 3. Exact test list (testing-expert)

### `tests/FashionSaaS.TryOn.Infrastructure.Tests/Messaging/ServiceBusTryOnEventPublisherTests.cs`

`ServiceBusClient`/`ServiceBusSender` are `sealed` in the Azure SDK, so this test exercises the publisher against the emulator connection string is **not** unit-testable by mocking the SDK types directly; instead, test the swallow-on-failure contract via a client pointed at an unreachable endpoint (fast, deterministic failure), and verify no exception escapes.

```csharp
using Azure.Messaging.ServiceBus;
using FashionSaaS.TryOn.Application.Messaging;
using FashionSaaS.TryOn.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FashionSaaS.TryOn.Infrastructure.Tests.Messaging;

public class ServiceBusTryOnEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_UnreachableNamespace_SwallowsExceptionAndDoesNotThrow()
    {
        // A syntactically-valid but unreachable connection string — proves a real
        // send failure (timeout/connection-refused) is caught and logged, not rethrown,
        // per spec §9's "must not fail the customer-facing request" contract.
        const string unreachableConnectionString =
            "Endpoint=sb://127.0.0.1:1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=invalid;";

        await using var client = new ServiceBusClient(unreachableConnectionString, new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0, TryTimeout = TimeSpan.FromSeconds(2) }
        });
        var settings = Options.Create(new ServiceBusSettings { ConnectionString = unreachableConnectionString, TopicName = "tryon-events" });
        var publisher = new ServiceBusTryOnEventPublisher(client, settings, NullLogger<ServiceBusTryOnEventPublisher>.Instance);

        var @event = new TryOnCompletedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);

        var act = async () => await publisher.PublishAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

### `tests/FashionSaaS.TryOn.Application.Tests/TryOn/TryOnServiceTests.cs` (Phase 3's file — modify)

Update `CreateService` to accept and pass a mocked `ITryOnEventPublisher`:

```csharp
// Add field:
private readonly Mock<ITryOnEventPublisher> _eventPublisher = new();

// Update CreateService's `new TryOnService(...)` call to add the 6th argument:
return new TryOnService(dbContext, _context.Object, _gemini.Object, factory.Object, options, _eventPublisher.Object);
```

Update `RenderAsync_Success_PersistsCompletedRowAndReturnsDataUri` to add one assertion after the existing `saved.TenantId.Should().Be(_tenantId);` line:

```csharp
        _eventPublisher.Verify(p => p.PublishAsync(
            It.Is<TryOnCompletedEvent>(e => e.TryOnRequestId == saved.Id && e.TenantId == _tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
```

Add a new test proving a Failed render never publishes:

```csharp
[Fact]
public async Task RenderAsync_Failure_NeverPublishesEvent()
{
    await using var dbContext = CreateDbContext();
    var service = CreateService(dbContext, aiUsageLimit: 1);
    dbContext.TryOnRequests.Add(new TryOnRequest { TenantId = _tenantId, Status = TryOnStatus.Completed, CreatedAt = DateTime.UtcNow });
    await dbContext.SaveChangesAsync();
    var form = new TryOnRequestForm { Photo = CreateFakePhoto(), GarmentImageUrl = "https://example.com/g.jpg", ProductId = Guid.NewGuid() };

    await service.RenderAsync(form, CancellationToken.None);

    _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<TryOnCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

> **Known coverage gap:** no automated test exercises the emulator end-to-end (real AMQP round-trip) — Group C6 is a manual verification step. This matches the main API's own testing convention of not standing up real external infra (Cloudinary, SMTP) in the automated suite.

## 4. Observability

- `ServiceBusTryOnEventPublisher` logs a `Warning` (via the built-in `Microsoft.Extensions.Logging` abstraction — no new library, already a transitive dependency of every ASP.NET Core app) on publish failure — the first log statement in this service. No structured logging framework (Serilog) is introduced in this phase; the default console provider is sufficient for a single warning line (YAGNI — add Serilog only if/when this service needs richer log routing).

## 5. OPEN QUESTIONS (decisions, not facts)

1. **Exact emulator connection-string and Config.json schema** — verify both against https://learn.microsoft.com/en-us/azure/service-bus-messaging/test-locally-with-service-bus-emulator immediately before Group C5/C6; the values in this plan are sourced from a search summary, not a full read of the live doc, and the spec itself (§15) flags this as unverified.
2. **Production Service Bus namespace provisioning** — out of scope for this plan (infra/deployment concern); `ServiceBusSettings:ConnectionString` in non-dev environments comes from whatever secret-injection mechanism is chosen for `GeminiSettings:ApiKey` in Phase 3's OPEN QUESTIONS — the two should be resolved together at deploy time.

## 6. Assumptions

- Docker is available in the local dev environment for running the Service Bus emulator (a new tooling assumption for this repo — no prior phase or the main API's existing dev workflow required Docker; flag this to the user if Docker is not already part of their local setup).
