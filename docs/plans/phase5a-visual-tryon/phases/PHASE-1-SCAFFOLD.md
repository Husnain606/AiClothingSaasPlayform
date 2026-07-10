# Phase 1 — Service Scaffold (Buildable Plan)

> **STATUS — not started (2026-07-11).**

## Reference

- Master plan: [`../MASTER.md`](../MASTER.md) — locked decisions D1, D2, D3, D5, D6.
- No prior phase to depend on (this is the first phase).

### Contract checklist (confirm against landed code before editing)

- [ ] `E:\AIcLOTHING\Directory.Build.props` exists at repo root and auto-applies to every project under it via MSBuild's upward directory walk (verified: no explicit `<Import>` needed — `services/fashionsaas-tryon/` is a sibling of `src/`, still under the repo root, so the strict analyzer baseline (`TreatWarningsAsErrors`, `AnalysisMode=All`, Meziantou + SonarAnalyzer) applies automatically).
- [ ] `E:\AIcLOTHING\.editorconfig` similarly applies via the EditorConfig upward walk — no action needed.
- [ ] `src/FashionSaaS.Domain/Entities/BaseEntity.cs:5-16` — confirms the real `BaseEntity` shape being mirrored (this phase writes an **independent, simplified copy** — no `DomainEvents` plumbing, since `TryOnRequest` never raises domain events — YAGNI).

### Locked decisions in force

- **D1** — new solution `FashionSaaS.TryOn.sln`, own SQL Server DB, at `services/fashionsaas-tryon/`.
- **D2** — 4-project Clean Architecture layering, Controllers-based API (not Minimal-API `IEndpoint`).
- **D5** — EF Core 10.0.9 + `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9.
- **D6** — direct `<PackageReference Version="...">` per project (matches actual repo convention, not aspirational central management).

## 1. Ordered task checklist

Execute top-to-bottom; build after each lettered group.

### Group A — Solution & project scaffold

- [ ] **A1** Create the solution and 4 projects via the .NET CLI (run from `E:\AIcLOTHING`):

```bash
mkdir -p services/fashionsaas-tryon/src
cd services/fashionsaas-tryon
dotnet new sln -n FashionSaaS.TryOn
dotnet new classlib -n FashionSaaS.TryOn.Domain -o src/FashionSaaS.TryOn.Domain -f net10.0
dotnet new classlib -n FashionSaaS.TryOn.Application -o src/FashionSaaS.TryOn.Application -f net10.0
dotnet new classlib -n FashionSaaS.TryOn.Infrastructure -o src/FashionSaaS.TryOn.Infrastructure -f net10.0
dotnet new webapi -n FashionSaaS.TryOn.Api -o src/FashionSaaS.TryOn.Api -f net10.0 --use-controllers
dotnet sln add src/FashionSaaS.TryOn.Domain/FashionSaaS.TryOn.Domain.csproj
dotnet sln add src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj
dotnet sln add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj
dotnet sln add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj
mkdir -p tests
dotnet new xunit -n FashionSaaS.TryOn.Domain.Tests -o tests/FashionSaaS.TryOn.Domain.Tests -f net10.0
dotnet new xunit -n FashionSaaS.TryOn.Infrastructure.Tests -o tests/FashionSaaS.TryOn.Infrastructure.Tests -f net10.0
dotnet sln add tests/FashionSaaS.TryOn.Domain.Tests/FashionSaaS.TryOn.Domain.Tests.csproj
dotnet sln add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj
```

Expected: `FashionSaaS.TryOn.sln` lists 6 projects; `dotnet build` from `services/fashionsaas-tryon` succeeds (webapi template's default `WeatherForecast` sample code will be removed in Group E, so it may currently emit unused-code warnings — that's expected transiently and fixed by Group E).

- [ ] **A2** Wire project references (run from `services/fashionsaas-tryon`):

```bash
dotnet add src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj reference src/FashionSaaS.TryOn.Domain/FashionSaaS.TryOn.Domain.csproj
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj reference src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj reference src/FashionSaaS.TryOn.Domain/FashionSaaS.TryOn.Domain.csproj
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj reference src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj reference src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj
dotnet add tests/FashionSaaS.TryOn.Domain.Tests/FashionSaaS.TryOn.Domain.Tests.csproj reference src/FashionSaaS.TryOn.Domain/FashionSaaS.TryOn.Domain.csproj
dotnet add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj reference src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj
dotnet add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj reference src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj
```

- [ ] **A3** Add package references. Domain project: none needed. Application project — add FluentValidation (matches D4):

```bash
dotnet add src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj package FluentValidation --version 12.1.1
```

Infrastructure project — EF Core SqlServer + Design + Tools:

```bash
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.9
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.9
dotnet add src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Tools --version 10.0.9
```

Api project — auth (added now so the csproj exists; wired to actual middleware in Phase 2), Swagger, FluentValidation auto-validation:

```bash
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.9
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj package FluentValidation.AspNetCore --version 11.3.1
dotnet add src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj package Swashbuckle.AspNetCore --version 10.2.2
```

Infrastructure test project — EF Core InMemory (matches the actual `CategoryRepositoryTests.cs:19-22` pattern) + FluentAssertions + Moq:

```bash
dotnet add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj package Microsoft.EntityFrameworkCore.InMemory --version 10.0.9
dotnet add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj package FluentAssertions --version 6.12.1
dotnet add tests/FashionSaaS.TryOn.Infrastructure.Tests/FashionSaaS.TryOn.Infrastructure.Tests.csproj package Moq --version 4.20.72
dotnet add tests/FashionSaaS.TryOn.Domain.Tests/FashionSaaS.TryOn.Domain.Tests.csproj package FluentAssertions --version 6.12.1
```

Expected: `dotnet build` from `services/fashionsaas-tryon` succeeds with all package restores resolved.

### Group B — Domain: `TryOnStatus`, `BaseEntity`, `TryOnRequest`

- [ ] **B1** Create `TryOnStatus` enum and `TryOnRequest` entity (Group C's own `BaseEntity`, defined here since Domain has zero project refs).

- [ ] **B2** Write the failing test for `TryOnRequest` defaults (see §3 code sample `tests/FashionSaaS.TryOn.Domain.Tests/TryOnRequestTests.cs`).
- [ ] **B3** Run: `dotnet test tests/FashionSaaS.TryOn.Domain.Tests --filter TryOnRequestTests` (from `services/fashionsaas-tryon`) — expect FAIL (`TryOnRequest`/`BaseEntity` don't exist yet).
- [ ] **B4** Implement `BaseEntity.cs`, `TryOnStatus.cs`, `TryOnRequest.cs` (§3 code samples).
- [ ] **B5** Run the same test — expect PASS (3/3).
- [ ] **B6** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Domain services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Domain.Tests services/fashionsaas-tryon/FashionSaaS.TryOn.sln services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests
git commit -m "feat(tryon): scaffold FashionSaaS.TryOn solution, TryOnRequest domain entity"
```

### Group C — Infrastructure: `TryOnDbContext`, EF config, DI

- [ ] **C1** Write the failing test for `TryOnDbContext` persisting a `TryOnRequest` (§3 code sample `tests/FashionSaaS.TryOn.Infrastructure.Tests/TryOnDbContextTests.cs`).
- [ ] **C2** Run: `dotnet test tests/FashionSaaS.TryOn.Infrastructure.Tests --filter TryOnDbContextTests` — expect FAIL (`TryOnDbContext` doesn't exist).
- [ ] **C3** Implement `TryOnDbContext.cs`, `Configurations/TryOnRequestConfiguration.cs`, `DependencyInjection.cs` (§3 code samples).
- [ ] **C4** Run the same test — expect PASS (2/2).
- [ ] **C5** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests
git commit -m "feat(tryon): add TryOnDbContext with TryOnRequest EF configuration"
```

### Group D — EF migration

- [ ] **D1** Generate the initial migration (run from `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure`):

```bash
dotnet ef migrations add InitialCreate --startup-project ../FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj --output-dir Persistence/Migrations
```

Expected: a `Persistence/Migrations/` folder with `<timestamp>_InitialCreate.cs` + `.Designer.cs` + `TryOnDbContextModelSnapshot.cs`, creating a single `TryOnRequests` table. This requires **C6** (connection string in Group E's `appsettings.Development.json`) to exist first if the design-time context needs a real connection — if `dotnet ef` fails with "no connection string," complete Group E's A1 (`appsettings.Development.json`) first, then return here.

- [ ] **D2** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Persistence/Migrations
git commit -m "feat(tryon): add InitialCreate EF migration for TryOnRequests table"
```

### Group E — API: `Program.cs`, health endpoint, config

- [ ] **E1** Replace the webapi template's default `Program.cs` and delete its scaffolded `WeatherForecast.cs` / `Controllers/WeatherForecastController.cs` (§3 code samples).
- [ ] **E2** Create `appsettings.json` and `appsettings.Development.json` (§3 code samples) — own database `TryOnDb`, distinct from the main API's `AiClothing` database.
- [ ] **E3** Create `HealthController.cs` (§3 code sample) — `GET /api/health` returns `ResponseData<string>.Success("healthy")` after confirming the DB connection opens.
- [ ] **E4** Apply the migration to create the local dev database:

```bash
cd services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure
dotnet ef database update --startup-project ../FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj
```

Expected: a new `TryOnDb` database appears on the local SQL Server instance with a `TryOnRequests` table.

- [ ] **E5** Run the service and hit the health endpoint:

```bash
cd services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api
dotnet run --urls http://localhost:5050
```

In another terminal: `curl http://localhost:5050/api/health` — expect `{"isSuccess":true,"statusCode":200,"message":"healthy","data":"healthy","errors":null}`.

- [ ] **E6** Commit:

```bash
git add services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api
git commit -m "feat(tryon): Program.cs wiring, appsettings, health endpoint"
```

### Group F — Validate

- [ ] **F1** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — zero warnings (warnings = errors, inherited from the root `Directory.Build.props`).
- [ ] **F1b** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every `.cs` file created in Groups B, C, E — clean.
- [ ] **F2** testing-expert writes the §2 exact test list (the two tests already TDD'd in B2/C1 are already covered; testing-expert confirms they exist verbatim and reports the run).
- [ ] **F3** `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — green, exact count reported (expect 5: 3 `TryOnRequestTests` + 2 `TryOnDbContextTests`).

## 2. Code samples — files to create

### B1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Domain/BaseEntity.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\BaseEntity.cs` (modelled on `src/FashionSaaS.Domain/Entities/BaseEntity.cs:5-16`, simplified — no `DomainEvents`, since `TryOnRequest` never raises one; YAGNI per this entity's actual needs).

```csharp
namespace FashionSaaS.TryOn.Domain;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### B1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Domain/TryOnStatus.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\TryOnStatus.cs`

```csharp
namespace FashionSaaS.TryOn.Domain;

public enum TryOnStatus
{
    Completed,
    Failed
}
```

### B1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Domain/TryOnRequest.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Domain\TryOnRequest.cs` (modelled on `src/FashionSaaS.Domain/Entities/StockAdjustment.cs:5-15` — a bare event-log entity; per spec §4.1/D11, **no image fields of any kind**).

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
}
```

### B2 — `services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Domain.Tests/TryOnRequestTests.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\tests\FashionSaaS.TryOn.Domain.Tests\TryOnRequestTests.cs` (modelled on `tests/FashionSaaS.Domain.Tests/BaseEntityTests.cs:8-15`).

```csharp
using FashionSaaS.TryOn.Domain;
using FluentAssertions;

namespace FashionSaaS.TryOn.Domain.Tests;

public class TryOnRequestTests
{
    [Fact]
    public void NewTryOnRequest_HasNonEmptyId()
    {
        var request = new TryOnRequest();
        request.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void NewTryOnRequest_DefaultsToCompletedStatus()
    {
        // TryOnStatus.Completed is the enum's zero value (default(TryOnStatus)) — this
        // test pins the enum's declared order so a future reordering is caught.
        var request = new TryOnRequest();
        request.Status.Should().Be(TryOnStatus.Completed);
    }

    [Fact]
    public void TryOnRequest_CanBeMarkedFailedWithReason()
    {
        var request = new TryOnRequest
        {
            Status = TryOnStatus.Failed,
            FailureReason = "Gemini API timeout"
        };
        request.Status.Should().Be(TryOnStatus.Failed);
        request.FailureReason.Should().Be("Gemini API timeout");
    }
}
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Persistence/TryOnDbContext.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Persistence\TryOnDbContext.cs` (modelled on `src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs:7-11` shape, simplified — no tenant query filter here; Phase 2 adds tenant scoping to the read path once `ICurrentTryOnContext` exists).

```csharp
using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Persistence;

public class TryOnDbContext(DbContextOptions<TryOnDbContext> options) : DbContext(options)
{
    public DbSet<TryOnRequest> TryOnRequests => Set<TryOnRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TryOnDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Persistence/Configurations/TryOnRequestConfiguration.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Persistence\Configurations\TryOnRequestConfiguration.cs` (modelled on `src/FashionSaaS.Infrastructure/Persistence/Configurations/StockAdjustmentConfiguration.cs:7-20`).

```csharp
using FashionSaaS.TryOn.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FashionSaaS.TryOn.Infrastructure.Persistence.Configurations;

public class TryOnRequestConfiguration : IEntityTypeConfiguration<TryOnRequest>
{
    public void Configure(EntityTypeBuilder<TryOnRequest> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.FailureReason).HasMaxLength(500);

        // Quota-counting query filters on these three; CreatedAt additionally orders the
        // month-window scan (D8's COUNT(*) WHERE TenantId = X AND Status = Completed AND
        // CreatedAt >= start-of-month).
        builder.HasIndex(t => new { t.TenantId, t.Status, t.CreatedAt });
    }
}
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/DependencyInjection.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\DependencyInjection.cs` (modelled on `src/FashionSaaS.Infrastructure/DependencyInjection.cs:13-38` shape, minimal — only the DbContext registration; Phase 2 adds auth-related registrations here).

```csharp
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FashionSaaS.TryOn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTryOnInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TryOnDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("TryOnConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:TryOnConnection not set."),
                b => b.MigrationsAssembly(typeof(TryOnDbContext).Assembly.FullName)));

        return services;
    }
}
```

### C1 — `services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests/TryOnDbContextTests.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\tests\FashionSaaS.TryOn.Infrastructure.Tests\TryOnDbContextTests.cs` (modelled on `tests/FashionSaaS.Infrastructure.Tests/Repositories/CategoryRepositoryTests.cs:12-40`).

```csharp
using FashionSaaS.TryOn.Domain;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FashionSaaS.TryOn.Infrastructure.Tests;

public class TryOnDbContextTests
{
    private static TryOnDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TryOnDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TryOnDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsTryOnRequest()
    {
        await using var ctx = CreateContext();
        var request = new TryOnRequest
        {
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Status = TryOnStatus.Completed
        };

        ctx.TryOnRequests.Add(request);
        await ctx.SaveChangesAsync();

        var saved = await ctx.TryOnRequests.FindAsync(request.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TryOnStatus.Completed);
    }

    [Fact]
    public async Task TryOnRequests_QueryByTenantAndStatus_ReturnsOnlyMatching()
    {
        await using var ctx = CreateContext();
        var tenantId = Guid.NewGuid();
        ctx.TryOnRequests.AddRange(
            new TryOnRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Completed },
            new TryOnRequest { TenantId = tenantId, CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Failed },
            new TryOnRequest { TenantId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Status = TryOnStatus.Completed });
        await ctx.SaveChangesAsync();

        var count = await ctx.TryOnRequests
            .Where(t => t.TenantId == tenantId && t.Status == TryOnStatus.Completed)
            .CountAsync();

        count.Should().Be(1);
    }
}
```

### E1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Program.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Program.cs` (modelled on `src/FashionSaaS.API/Program.cs` shape, trimmed to what Phase 1 needs — Phase 2 adds `AddAuthentication`/`UseAuthentication`, Phase 3 adds the Refit client registration).

```csharp
using FashionSaaS.TryOn.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTryOnInfrastructure(builder.Configuration);

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
app.MapControllers();

app.Run();
```

Also delete the webapi template's scaffolded files (they are not listed here because they are deletions, not creations):
```bash
rm services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/WeatherForecast.cs
rm services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/WeatherForecastController.cs
```

### E2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\appsettings.json` (modelled on `src/FashionSaaS.API/appsettings.json` shape).

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### E2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\appsettings.Development.json` (own database `TryOnDb`, distinct from the main API's `AiClothing` — D1's "own DB" requirement).

```json
{
  "ConnectionStrings": {
    "TryOnConnection": "Server=.;Database=TryOnDb;User Id=sa;Password=12345678;Encrypt=False;TrustServerCertificate=True"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

### E3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Common/ResponseData.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Common\ResponseData.cs` — **independent copy** of the main API's envelope per D3 (verbatim shape from `src/FashionSaaS.Application/Common/ResponseData.cs:3-16`, just a different namespace; placed in the Api project since this service has no need to reference it from Application in Phase 1 — Phase 3 will move it to `FashionSaaS.TryOn.Application/Common/` if a service class needs to return it directly).

```csharp
namespace FashionSaaS.TryOn.Api.Common;

public class ResponseData<T>
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public IEnumerable<string>? Errors { get; set; }

    public static ResponseData<T> Success(T data, string message = "Success", int statusCode = 200)
        => new() { IsSuccess = true, StatusCode = statusCode, Message = message, Data = data };

    public static ResponseData<T> Failure(string message, int statusCode = 400, IEnumerable<string>? errors = null)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message, Errors = errors };
}
```

### E3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/HealthController.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Controllers\HealthController.cs` (modelled on `src/FashionSaaS.API/Controllers/Auth/AuthController.cs:1-34` controller shape).

```csharp
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(TryOnDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            var failure = ResponseData<string>.Failure("Database unreachable.", 503);
            return StatusCode(failure.StatusCode, failure);
        }

        var response = ResponseData<string>.Success("healthy");
        return StatusCode(response.StatusCode, response);
    }
}
```

## 3. Exact test list (testing-expert)

Paradigm: xUnit + FluentAssertions, EF Core `InMemoryDatabase` for `TryOnDbContext` tests (no Moq needed in this phase — no dependencies to mock yet).

### Domain tests (`tests/FashionSaaS.TryOn.Domain.Tests/TryOnRequestTests.cs`)
- **`NewTryOnRequest_HasNonEmptyId`** — `BaseEntity`'s `Guid.NewGuid()` default is populated on construction.
- **`NewTryOnRequest_DefaultsToCompletedStatus`** — pins the enum's declared zero-value order.
- **`TryOnRequest_CanBeMarkedFailedWithReason`** — `Status`/`FailureReason` are independently settable.

### Infrastructure tests (`tests/FashionSaaS.TryOn.Infrastructure.Tests/TryOnDbContextTests.cs`)
- **`SaveChangesAsync_PersistsTryOnRequest`** — a `TryOnRequest` round-trips through `TryOnDbContext` via `FindAsync`.
- **`TryOnRequests_QueryByTenantAndStatus_ReturnsOnlyMatching`** — the `(TenantId, Status, CreatedAt)` index shape (Group C's config) supports the exact filter D8's quota `COUNT` query will use, proven here at the LINQ level.

> **Known coverage gap:** this phase does not test the actual EF migration SQL against a real SQL Server instance (only `InMemoryDatabase`) — `dotnet ef database update` in Group E is a manual verification step, not an automated test. A real-SQL-Server integration test is out of scope for Phase 1 (no such pattern exists yet for the main API's tests either — verified: no `TenantRepositoryTests.cs`-style test uses a real SQL Server connection, all use `InMemoryDatabase`).

## 4. Observability

- None added in this phase (no Serilog wiring yet — Phase 1 is scaffold-only). If a later phase needs request logging, it will follow the main API's `Serilog.AspNetCore` pattern (`Program.cs:16-27`) — not introduced here to keep this phase's diff minimal (YAGNI).

## 5. OPEN QUESTIONS (decisions, not facts)

1. **Should the TryOn service's Development SQL Server credentials differ from the main API's (`sa`/`12345678`)?** *Default: reuse the same local `sa` credentials against a separate `TryOnDb` database name, since both run against the same local SQL Server instance in dev — confirm before Group E if a different local DB server/instance is intended.*

## 6. Assumptions

- A local SQL Server instance (matching the main API's `Server=.` connection string target) is reachable at implementation time, matching the environment the main API's own tests/dev workflow already assumes.
- `dotnet-ef` global/local tool is available (the main API's own `dotnet ef migrations add` workflow already depends on this, so this is a pre-existing environment assumption, not a new one).
