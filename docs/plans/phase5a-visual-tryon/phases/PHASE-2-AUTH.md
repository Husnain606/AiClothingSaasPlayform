# Phase 2 — Auth: `ai_usage_limit` Claim & Independent JWT Validation (Buildable Plan)

> **STATUS — not started (2026-07-11).**

## Reference

- Master plan: [`../MASTER.md`](../MASTER.md) — locked decisions D7, D8.
- **Dependency (consumed, not redefined):** [`PHASE-1-SCAFFOLD.md`](PHASE-1-SCAFFOLD.md) — `TryOnDbContext` (`Persistence/TryOnDbContext.cs`), `TryOnRequest`/`TryOnStatus` (Domain project), `AddTryOnInfrastructure(IServiceCollection, IConfiguration)` extension, `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Program.cs`.

### Contract checklist (confirm against landed code before editing)

- [ ] `src/FashionSaaS.Infrastructure/Services/JwtService.cs:17-54` — `GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false)` exists exactly as read; this phase adds a 5th parameter.
- [ ] `src/FashionSaaS.Application/Interfaces/IJwtService.cs` — the interface declaration to update in lockstep.
- [ ] `src/FashionSaaS.Application/Auth/AuthService.cs:308-` — `IssueTokensAsync(User user, IEnumerable<string> roles, string? tenantSlug, bool mfaVerified)` private helper, called from 3 sites (`LoginAsync` ~line 89, `LoginMfaAsync` ~line 152, `RefreshTokenAsync` ~line 191).
- [ ] `src/FashionSaaS.Application/Interfaces/ISubscriptionRepository.cs:7` — `Task<TenantSubscription?> GetActiveByTenantIdAsync(Guid tenantId)` exists exactly as read.
- [ ] `src/FashionSaaS.Domain/Entities/TenantSubscription.cs` — `Plan` navigation property (`SubscriptionPlan`) is populated by `GetActiveByTenantIdAsync` (confirm via `SubscriptionRepository` implementation — if `Plan` is not `Include()`-d, add `.Include(s => s.Plan)` there; check before writing Group A).
- [ ] `tests/FashionSaaS.Application.Tests/Auth/AuthServiceTests.cs` — exactly 2 `GenerateAccessToken` Setup/Verify call sites (`LoginAsync_ValidCredentials_NonSuperAdmin_ReturnsTokens` ~line 59, `LoginAsync_SuperAdmin_ReturnsMfaRequired_NoJwtIssued`'s `Verify` ~line 140) that need a 5th argument matcher added.
- [ ] `tests/FashionSaaS.Infrastructure.Tests/Security/JwtServiceTests.cs:70,95,109` — 3 calls to `_service.GenerateAccessToken(user, roles)` (2-arg, relying on existing optional-parameter defaults) — these compile unchanged since a new *trailing* optional parameter doesn't break omitted-optional-arg call sites.

### Locked decisions in force

- **D7** — `ai_usage_limit` claim added to `JwtService.GenerateAccessToken`; TryOn service validates the same JWT independently (shared `JwtSettings:Secret`/`Issuer`/`Audience`), no call back to the main API.
- **D8** — the TryOn service reads `tenant_id`, customer id (`sub` claim), and `ai_usage_limit` directly from the validated `ClaimsPrincipal` — no middleware needed (see rationale in Group C).

## 1. Ordered task checklist

### Group A — Main API: `ai_usage_limit` claim

- [ ] **A1** Update `IJwtService.GenerateAccessToken` signature (§2 code sample).
- [ ] **A2** Update `JwtService.GenerateAccessToken` implementation to add the claim (§2 code sample).
- [ ] **A3** Write the failing test in `tests/FashionSaaS.Infrastructure.Tests/Security/JwtServiceTests.cs` for the new claim (§3 exact test list, new test `GenerateAccessToken_IncludesAiUsageLimitClaim`).
- [ ] **A4** Run: `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter GenerateAccessToken_IncludesAiUsageLimitClaim` (from `E:\AIcLOTHING`) — expect FAIL (parameter doesn't exist).
- [ ] **A5** Implement A1/A2 for real, run again — expect PASS.
- [ ] **A6** Update `AuthService`: inject `ISubscriptionRepository`, look up `AiUsageLimit` in `IssueTokensAsync`, pass to `GenerateAccessToken` (§2 code sample).
- [ ] **A7** Update `AuthServiceTests.cs`: add `_subscriptionRepo` mock field, add it to `CreateService()`'s constructor call, update the 2 existing `GenerateAccessToken` Setup/Verify call sites to match the new 5-arg signature, add 2 new tests (§3 exact test list).
- [ ] **A8** Run: `dotnet test tests/FashionSaaS.Application.Tests --filter AuthServiceTests` (from `E:\AIcLOTHING`) — expect all green (existing + 2 new = confirm exact count against current file: currently N tests in the class; testing-expert reports the exact before/after count).
- [ ] **A9** Commit:

```bash
git add src/FashionSaaS.Application/Interfaces/IJwtService.cs src/FashionSaaS.Infrastructure/Services/JwtService.cs src/FashionSaaS.Application/Auth/AuthService.cs tests/FashionSaaS.Infrastructure.Tests/Security/JwtServiceTests.cs tests/FashionSaaS.Application.Tests/Auth/AuthServiceTests.cs
git commit -m "feat(auth): add ai_usage_limit JWT claim sourced from tenant's active SubscriptionPlan"
```

### Group B — Main API: full-solution regression check

- [ ] **B1** Run the **entire** main API test suite (not just the two touched files) — a constructor signature change on `AuthService` and an interface change on `IJwtService` can affect other call sites:

```bash
dotnet test FashionSaaS.sln
```

Expected: all tests green, exact count reported (this is the full-suite gate — the plan cannot predict the exact pre-existing total, so testing-expert records it verbatim in the phase report).

### Group C — TryOn service: JWT Bearer auth + `ICurrentTryOnContext`

- [ ] **C1** Add the JWT Bearer package's config binding: `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json` gains a `JwtSettings` section (§2 code sample) — **same** `Secret`/`Issuer`/`Audience` values as the main API's dev config (`src/FashionSaaS.API/appsettings.Development.json` + `appsettings.json`), per D7's shared-signing-key requirement.
- [ ] **C2** Create `JwtSettings.cs` in the TryOn Application project (own copy, not shared — D3's independence principle applies here too) (§2 code sample).
- [ ] **C3** Create `ICurrentTryOnContext` + `CurrentTryOnContext` (§2 code samples) — a scoped service reading `TenantId`/`CustomerId`/`AiUsageLimit` directly from `IHttpContextAccessor.HttpContext.User` claims. **Design note (why no middleware):** unlike the main API's `TenantResolutionMiddleware` (which resolves a tenant from a route-slug OR falls back to the JWT), this service has exactly one resolution path — the JWT claim, always — so a claims-reading scoped service is sufficient; a middleware would add indirection with no behavioral difference. This is a deliberate simplification (YAGNI), not an oversight.
- [ ] **C4** Wire `AddAuthentication`/`AddJwtBearer` + `ICurrentTryOnContext` registration into a new `AddTryOnAuthentication` extension (§2 code sample, modelled on `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs`'s `AddJwtAuthentication`).
- [ ] **C5** Update `Program.cs` to call `AddTryOnAuthentication`, `AddHttpContextAccessor`, `app.UseAuthentication()`, `app.UseAuthorization()`, and add `[Authorize]` to `HealthController` — no, **do not** authorize `HealthController` (it must stay a public liveness probe); instead prove auth works via a new authenticated test endpoint stub (§2 code sample `WhoAmIController`) that Phase 3's real endpoint will replace.
- [ ] **C6** Write the failing test for `CurrentTryOnContext` (§3 exact test list, `tests/FashionSaaS.TryOn.Infrastructure.Tests/CurrentTryOnContextTests.cs`).
- [ ] **C7** Run: `dotnet test services/fashionsaas-tryon/tests/FashionSaaS.TryOn.Infrastructure.Tests --filter CurrentTryOnContextTests` — expect FAIL.
- [ ] **C8** Implement, run again — expect PASS.
- [ ] **C9** Manual verification: start the service (`dotnet run` from the Api project), issue a JWT from the **main** API's `/api/auth/login` (or hand-craft one with the same dev secret using a short script/`jwt.io`-equivalent for a smoke check), call `GET /api/whoami` on the TryOn service with `Authorization: Bearer <token>` — expect `200` with the tenant/customer ids echoed back; call without a token — expect `401`.
- [ ] **C10** Commit:

```bash
git add services/fashionsaas-tryon/src
git commit -m "feat(tryon): independent JWT validation, ICurrentTryOnContext, WhoAmI smoke endpoint"
```

### Group D — Validate

- [ ] **D1** `dotnet build FashionSaaS.sln` (main API) — zero warnings.
- [ ] **D1b** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — zero warnings.
- [ ] **D2** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every `.cs` file touched/created in Groups A and C — clean.
- [ ] **D3** testing-expert writes/confirms the §3 exact test list across both solutions.
- [ ] **D4** `dotnet test FashionSaaS.sln` and `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — both green, exact counts reported.

## 2. Code samples — files to create / modify

### A1 — `src/FashionSaaS.Application/Interfaces/IJwtService.cs`

`E:\AIcLOTHING\src\FashionSaaS.Application\Interfaces\IJwtService.cs` — modify the one line:

```csharp
// Before:
string GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false);

// After:
string GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false, int aiUsageLimit = 0);
```

### A2 — `src/FashionSaaS.Infrastructure/Services/JwtService.cs`

`E:\AIcLOTHING\src\FashionSaaS.Infrastructure\Services\JwtService.cs:17,30-37` — modify the method signature and claims list:

```csharp
// Before (line 17):
public string GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false)

// After:
public string GenerateAccessToken(User user, IEnumerable<string> roles, string? tenantSlug = null, bool mfaVerified = false, int aiUsageLimit = 0)
```

```csharp
// Before (lines 30-37):
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            new("mfa_verified", mfaVerified.ToString().ToLower())
        };

// After — add one line:
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            new("mfa_verified", mfaVerified.ToString().ToLower()),
            new("ai_usage_limit", aiUsageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
```

### A6 — `src/FashionSaaS.Application/Auth/AuthService.cs`

`E:\AIcLOTHING\src\FashionSaaS.Application\Auth\AuthService.cs:13-22` — add `ISubscriptionRepository` to the primary constructor:

```csharp
// Before:
public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ILoginAttemptRepository loginAttemptRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    IEmailService emailService,
    IFieldEncryptionService fieldEncryption,
    ISuperAdminIpGuardService ipGuardService)

// After:
public class AuthService(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ILoginAttemptRepository loginAttemptRepository,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IUnitOfWork unitOfWork,
    IAuditLogService auditLogService,
    IEmailService emailService,
    IFieldEncryptionService fieldEncryption,
    ISuperAdminIpGuardService ipGuardService,
    ISubscriptionRepository subscriptionRepository)
```

`AuthService.cs:308-317` (the `IssueTokensAsync` private helper) — modify to look up and pass `aiUsageLimit`:

```csharp
// Before:
    private async Task<(string accessToken, string rawRefreshToken)> IssueTokensAsync(
        User user, IEnumerable<string> roles, string? tenantSlug, bool mfaVerified)
    {
        // Materialize once — roles is used for both JWT generation and SuperAdmin check below.
        var roleList = roles as IReadOnlyList<string> ?? roles.ToList();

        // Pass tenantSlug so the JWT carries the tenant_slug claim (security requirement)
        var accessToken = jwtService.GenerateAccessToken(user, roleList, tenantSlug, mfaVerified);

// After:
    private async Task<(string accessToken, string rawRefreshToken)> IssueTokensAsync(
        User user, IEnumerable<string> roles, string? tenantSlug, bool mfaVerified)
    {
        // Materialize once — roles is used for both JWT generation and SuperAdmin check below.
        var roleList = roles as IReadOnlyList<string> ?? roles.ToList();

        // Tenant-less users (platform SuperAdmin) get 0 — there is no subscription to read a
        // limit from, and SuperAdmin never calls the TryOn service as a tenant customer.
        var aiUsageLimit = 0;
        if (user.TenantId is { } tenantId)
        {
            var subscription = await subscriptionRepository.GetActiveByTenantIdAsync(tenantId);
            aiUsageLimit = subscription?.Plan.AiUsageLimit ?? 0;
        }

        // Pass tenantSlug so the JWT carries the tenant_slug claim (security requirement)
        var accessToken = jwtService.GenerateAccessToken(user, roleList, tenantSlug, mfaVerified, aiUsageLimit);
```

Add the using directive if not already present: `using FashionSaaS.Application.Interfaces;` (already present per the file's existing imports at line 6 — confirm before editing; `ISubscriptionRepository` lives in that namespace).

### A7 — `tests/FashionSaaS.Application.Tests/Auth/AuthServiceTests.cs`

Add the mock field (near the other `Mock<...>` fields, ~line 23):

```csharp
private readonly Mock<ISubscriptionRepository> _subscriptionRepo = new();
```

Update `CreateService()` (~lines 26-29):

```csharp
// Before:
    private AuthService CreateService() => new(
        _userRepo.Object, _refreshRepo.Object, _loginAttemptRepo.Object,
        _passwordHasher.Object, _jwtService.Object, _uow.Object,
        _auditLog.Object, _emailService.Object, _fieldEncryption.Object,
        _ipGuardService.Object);

// After:
    private AuthService CreateService() => new(
        _userRepo.Object, _refreshRepo.Object, _loginAttemptRepo.Object,
        _passwordHasher.Object, _jwtService.Object, _uow.Object,
        _auditLog.Object, _emailService.Object, _fieldEncryption.Object,
        _ipGuardService.Object, _subscriptionRepo.Object);
```

Update the 2 existing call sites that reference `GenerateAccessToken` with an explicit 4-arg matcher, adding the 5th (`It.IsAny<int>()`):

```csharp
// LoginAsync_ValidCredentials_NonSuperAdmin_ReturnsTokens, before:
        _jwtService.Setup(j => j.GenerateAccessToken(
            user,
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<string?>(),
            false)).Returns("access_token");

// After:
        _jwtService.Setup(j => j.GenerateAccessToken(
            user,
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<string?>(),
            false,
            It.IsAny<int>())).Returns("access_token");
```

```csharp
// LoginAsync_SuperAdmin_ReturnsMfaRequired_NoJwtIssued, before:
        _jwtService.Verify(j => j.GenerateAccessToken(
            It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);

// After:
        _jwtService.Verify(j => j.GenerateAccessToken(
            It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int>()),
            Times.Never);
```

Add 2 new tests (full code in §3's exact test list below).

### C1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/appsettings.Development.json`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\appsettings.Development.json` — extend Phase 1's file:

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
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

### C2 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/JwtSettings.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\JwtSettings.cs` (independent copy of `src/FashionSaaS.Application/Configuration/JwtSettings.cs` — D3's principle applied to config, not just the response envelope).

```csharp
using System.ComponentModel.DataAnnotations;

namespace FashionSaaS.TryOn.Application;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    [Required]
    [MinLength(32)]
    public string Secret { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;
}
```

Requires the `System.ComponentModel.Annotations` package is implicitly available (it's part of the BCL under `net10.0`, no package reference needed — confirm the main API's `JwtSettings.cs` has no extra `PackageReference` for it either, which it doesn't).

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/ICurrentTryOnContext.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Application\ICurrentTryOnContext.cs` (modelled on `src/FashionSaaS.Application/Interfaces/ICurrentTenantService.cs`'s shape, adapted to this service's single resolution path — read-only, no `SetTenant`-style mutator, since claims are the only source).

```csharp
namespace FashionSaaS.TryOn.Application;

public interface ICurrentTryOnContext
{
    Guid TenantId { get; }
    Guid CustomerId { get; }
    int AiUsageLimit { get; }
    bool IsAuthenticated { get; }
}
```

### C3 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/Security/CurrentTryOnContext.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\Security\CurrentTryOnContext.cs` (reads the 3 claims the main API's `JwtService` now writes: `tenant_id`, `sub`/`ClaimTypes.NameIdentifier`, `ai_usage_limit`; dual-checks the subject claim the same way `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs`'s `SuperAdminPolicy` rate-limit partition does, since `JwtBearerHandler`'s default inbound claim mapping rewrites `sub` to `ClaimTypes.NameIdentifier`).

```csharp
using System.Security.Claims;
using FashionSaaS.TryOn.Application;
using Microsoft.AspNetCore.Http;

namespace FashionSaaS.TryOn.Infrastructure.Security;

public class CurrentTryOnContext(IHttpContextAccessor httpContextAccessor) : ICurrentTryOnContext
{
    // "sub" is the JWT registered claim name for subject; using the literal (rather than
    // System.IdentityModel.Tokens.Jwt's JwtRegisteredClaimNames.Sub constant) avoids this
    // project needing its own reference to that package — Infrastructure has no other need for it.
    private const string SubClaimType = "sub";

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid TenantId =>
        Guid.TryParse(User?.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    public Guid CustomerId =>
        Guid.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst(SubClaimType)?.Value,
            out var id) ? id : Guid.Empty;

    public int AiUsageLimit =>
        int.TryParse(User?.FindFirst("ai_usage_limit")?.Value, out var limit) ? limit : 0;
}
```

### C4 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Infrastructure/DependencyInjection.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Infrastructure\DependencyInjection.cs` — extend Phase 1's file with auth wiring (modelled on `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs`'s `AddJwtAuthentication`, lines ~65-83):

```csharp
using System.Text;
using FashionSaaS.TryOn.Application;
using FashionSaaS.TryOn.Infrastructure.Persistence;
using FashionSaaS.TryOn.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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

    public static IServiceCollection AddTryOnAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings section is missing from configuration.");
        if (string.IsNullOrEmpty(jwtSettings.Secret))
            throw new InvalidOperationException("JwtSettings:Secret is not set.");

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTryOnContext, CurrentTryOnContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }
}
```

### C5 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Program.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Program.cs` — extend Phase 1's file:

```csharp
using FashionSaaS.TryOn.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTryOnInfrastructure(builder.Configuration);
builder.Services.AddTryOnAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

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
```

### C5 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/WhoAmIController.cs`

`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Controllers\WhoAmIController.cs` — a throwaway smoke-test endpoint proving auth + claims-reading works end-to-end; **Phase 3 deletes this file** once the real `POST /api/tryon` endpoint exists to prove the same thing.

```csharp
using FashionSaaS.TryOn.Api.Common;
using FashionSaaS.TryOn.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FashionSaaS.TryOn.Api.Controllers;

[ApiController]
[Route("api/whoami")]
[Authorize]
public class WhoAmIController(ICurrentTryOnContext context) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var response = ResponseData<object>.Success(new
        {
            context.TenantId,
            context.CustomerId,
            context.AiUsageLimit
        });
        return StatusCode(response.StatusCode, response);
    }
}
```

## 3. Exact test list (testing-expert)

Paradigm: main-API tests — xUnit + Moq + FluentAssertions (unchanged from existing convention). TryOn-service tests — xUnit + FluentAssertions, `ClaimsPrincipal` constructed directly (no HTTP pipeline needed to test `CurrentTryOnContext` in isolation) using a fake `IHttpContextAccessor` via Moq.

### Main API — `tests/FashionSaaS.Infrastructure.Tests/Security/JwtServiceTests.cs`
- **`GenerateAccessToken_IncludesAiUsageLimitClaim`** — new test:

```csharp
[Fact]
public void GenerateAccessToken_IncludesAiUsageLimitClaim()
{
    var user = new User { Id = Guid.NewGuid(), Email = "owner@brand.com", TenantId = Guid.NewGuid() };
    var token = _service.GenerateAccessToken(user, new List<string> { "AdminOwner" }, aiUsageLimit: 500);

    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
    var jwt = handler.ReadJwtToken(token);

    jwt.Claims.Should().ContainSingle(c => c.Type == "ai_usage_limit" && c.Value == "500");
}
```

### Main API — `tests/FashionSaaS.Application.Tests/Auth/AuthServiceTests.cs`
- **`LoginAsync_ValidCredentials_NonSuperAdmin_ReturnsTokens`** (existing, modified per §2's Setup update) — still asserts token issuance; now also implicitly proves the 5-arg call matches.
- **`LoginAsync_ReadsAiUsageLimitFromActiveSubscription_PassesToJwtService`** — new test:

```csharp
[Fact]
public async Task LoginAsync_ReadsAiUsageLimitFromActiveSubscription_PassesToJwtService()
{
    var tenantId = Guid.NewGuid();
    var tenant = new Tenant { Id = tenantId, Slug = "brand-slug" };
    var user = new User
    {
        Id = Guid.NewGuid(), Email = "owner@brand.com",
        PasswordHash = "hash", IsActive = true, TenantId = tenantId,
        Tenant = tenant,
        UserRoles = new List<UserRole>
        {
            new() { Role = new Role { Name = RoleType.AdminOwner, Scope = RoleScope.Tenant } }
        }
    };
    var plan = new SubscriptionPlan { AiUsageLimit = 250 };
    var subscription = new TenantSubscription { TenantId = tenantId, Plan = plan };

    _userRepo.Setup(r => r.GetByEmailAsync("owner@brand.com")).ReturnsAsync(user);
    _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
    _passwordHasher.Setup(h => h.Verify("Password@1", "hash")).Returns(true);
    _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("owner@brand.com", 15)).ReturnsAsync(0);
    _subscriptionRepo.Setup(r => r.GetActiveByTenantIdAsync(tenantId)).ReturnsAsync(subscription);
    _jwtService.Setup(j => j.GenerateAccessToken(
        user, It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), false, 250)).Returns("access_token");
    _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
    _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
    _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

    var service = CreateService();
    var result = await service.LoginAsync(
        new LoginRequest { Email = "owner@brand.com", Password = "Password@1" },
        "127.0.0.1", "Mozilla");

    result.IsSuccess.Should().BeTrue();
    _jwtService.Verify(j => j.GenerateAccessToken(
        user, It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), false, 250), Times.Once);
}
```

- **`LoginAsync_NoActiveSubscription_PassesZeroAiUsageLimit`** — new test:

```csharp
[Fact]
public async Task LoginAsync_NoActiveSubscription_PassesZeroAiUsageLimit()
{
    var tenantId = Guid.NewGuid();
    var tenant = new Tenant { Id = tenantId, Slug = "brand-slug" };
    var user = new User
    {
        Id = Guid.NewGuid(), Email = "owner@brand.com",
        PasswordHash = "hash", IsActive = true, TenantId = tenantId,
        Tenant = tenant,
        UserRoles = new List<UserRole>
        {
            new() { Role = new Role { Name = RoleType.AdminOwner, Scope = RoleScope.Tenant } }
        }
    };

    _userRepo.Setup(r => r.GetByEmailAsync("owner@brand.com")).ReturnsAsync(user);
    _userRepo.Setup(r => r.GetByIdWithRolesAsync(user.Id)).ReturnsAsync(user);
    _passwordHasher.Setup(h => h.Verify("Password@1", "hash")).Returns(true);
    _loginAttemptRepo.Setup(r => r.GetRecentFailureCountAsync("owner@brand.com", 15)).ReturnsAsync(0);
    _subscriptionRepo.Setup(r => r.GetActiveByTenantIdAsync(tenantId)).ReturnsAsync((TenantSubscription?)null);
    _jwtService.Setup(j => j.GenerateAccessToken(
        user, It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), false, 0)).Returns("access_token");
    _jwtService.Setup(j => j.GenerateRefreshToken()).Returns("raw_refresh");
    _passwordHasher.Setup(h => h.Hash("raw_refresh")).Returns("hashed_refresh");
    _uow.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

    var service = CreateService();
    var result = await service.LoginAsync(
        new LoginRequest { Email = "owner@brand.com", Password = "Password@1" },
        "127.0.0.1", "Mozilla");

    result.IsSuccess.Should().BeTrue();
    _jwtService.Verify(j => j.GenerateAccessToken(
        user, It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), false, 0), Times.Once);
}
```

### TryOn service — `tests/FashionSaaS.TryOn.Infrastructure.Tests/CurrentTryOnContextTests.cs`

```csharp
using System.Security.Claims;
using FashionSaaS.TryOn.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;

namespace FashionSaaS.TryOn.Infrastructure.Tests;

public class CurrentTryOnContextTests
{
    private static CurrentTryOnContext CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return new CurrentTryOnContext(accessor.Object);
    }

    [Fact]
    public void TenantId_ReadsFromTenantIdClaim()
    {
        var tenantId = Guid.NewGuid();
        var context = CreateContext(new Claim("tenant_id", tenantId.ToString()));
        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void CustomerId_ReadsFromNameIdentifierClaim()
    {
        var customerId = Guid.NewGuid();
        var context = CreateContext(new Claim(ClaimTypes.NameIdentifier, customerId.ToString()));
        context.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public void AiUsageLimit_ReadsFromAiUsageLimitClaim()
    {
        var context = CreateContext(new Claim("ai_usage_limit", "500"));
        context.AiUsageLimit.Should().Be(500);
    }

    [Fact]
    public void AiUsageLimit_MissingClaim_DefaultsToZero()
    {
        var context = CreateContext();
        context.AiUsageLimit.Should().Be(0);
    }

    [Fact]
    public void IsAuthenticated_NoIdentity_ReturnsFalse()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var context = new CurrentTryOnContext(accessor.Object);
        context.IsAuthenticated.Should().BeFalse();
    }
}
```

> **Known coverage gap:** C9's manual smoke test (real JWT issued by the main API, consumed by the TryOn service) is not automated in this phase — an automated cross-service integration test is listed as Phase 3's testing-strategy item (spec §13: "a manual/documented smoke test... likely an integration test in the try-on service's test suite constructing a token with the same shared secret"). Phase 3 will add `WhoAmITokenAcceptanceTests.cs`-equivalent coverage once the real endpoint replaces `WhoAmIController`.

## 4. Observability

- None added — this phase doesn't touch logging. Deferred to whichever phase first needs to debug a real failure mode (Phase 3's Gemini error handling).

## 5. OPEN QUESTIONS (decisions, not facts)

1. **Is `Plan` actually eager-loaded by `GetActiveByTenantIdAsync`?** The contract checklist above flags this — if the concrete `SubscriptionRepository.GetActiveByTenantIdAsync` implementation doesn't `.Include(s => s.Plan)`, accessing `subscription.Plan.AiUsageLimit` in A6 will throw a `NullReferenceException` at runtime (EF Core doesn't lazy-load without a proxy setup, which this codebase doesn't use). *Default: verify the implementation first; if `Plan` isn't included, add `.Include(s => s.Plan)` to that query as part of Group A (a one-line addition, in scope for this phase since it's required for the feature to function correctly) — confirm before A6.*

## 6. Assumptions

- The main API's `SubscriptionRepository.GetActiveByTenantIdAsync` returns at most one row per tenant (an "active" subscription is unique per tenant) — consistent with `TenantSubscription.Status` being a single enum value per row, not a collection.
- No other code in the main solution calls `IJwtService.GenerateAccessToken` besides `AuthService.IssueTokensAsync` (verified: `grep -rln GenerateAccessToken src tests` returned only `AuthService.cs`, `IJwtService.cs`, `JwtService.cs`, and the two test files listed in the contract checklist — no other production call sites exist).
