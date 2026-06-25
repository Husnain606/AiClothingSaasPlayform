# FashionSaaS — Engineering Conventions

These are binding conventions for this codebase. Every new feature and refactor must follow them. They override the example code in the implementation plan where they conflict.

---

## 1. Third-party API calls — use Refit (interface-based), not raw `HttpClient`

When calling any external HTTP/REST API (payment gateways, AI services, courier/logistics APIs, etc.), define a **Refit service interface** and let Refit generate the implementation. Do **not** hand-roll `HttpClient` request/response plumbing.

**How:**
- Add the `Refit` (and `Refit.HttpClientFactory`) package.
- Declare an interface in the Application layer (the contract) e.g.:
  ```csharp
  public interface IPaymentGatewayApi
  {
      [Post("/v1/charges")]
      Task<ChargeResponse> CreateChargeAsync([Body] ChargeRequest request, CancellationToken ct = default);
  }
  ```
- Register in Infrastructure DI with `services.AddRefitClient<IPaymentGatewayApi>().ConfigureHttpClient(c => c.BaseAddress = new Uri(options.BaseUrl));`
- Base URLs / keys come from the Options pattern (see §2), never hard-coded.
- Use Polly handlers (`AddTransientHttpErrorPolicy`) for resiliency where appropriate.

**Phase 1 status:** No HTTP third-party integrations exist yet (the only external integration is SMTP via MailKit, which is not an HTTP API). Apply this convention the moment an HTTP integration is introduced.

---

## 2. Reading configuration — use the Options pattern, not `IConfiguration` indexing

Never read settings with `configuration["Section:Key"]` string indexing in services. Bind a strongly-typed settings class and inject `IOptions<T>` (or `IOptionsSnapshot<T>` for scoped/reloadable, `IOptionsMonitor<T>` for singletons that must observe changes).

**How:**
- Define a POCO per config section in a `Configuration/` (or `Options/`) folder, e.g. `JwtSettings`, `SmtpSettings`, `EncryptionSettings`, `CorsSettings`.
- Register: `services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));`
- Inject: a service constructor takes `IOptions<JwtSettings> jwtOptions` and uses `jwtOptions.Value`.
- Startup-only values needed before the DI container is built (e.g. the JWT signing key for the auth handler) may be bound once via `configuration.GetSection("JwtSettings").Get<JwtSettings>()` — still through the typed class, not raw indexing.
- Secrets (`JwtSettings:Secret`, `SmtpSettings:Password`, `ConnectionStrings:DefaultConnection`, `EncryptionSettings:BankFieldKey`) still come from environment variables / `appsettings.Development.json` (gitignored) / Key Vault — the Options pattern binds them; it does not change where the values live.
- Prefer validation: `services.AddOptions<JwtSettings>().Bind(...).ValidateDataAnnotations().ValidateOnStart();`

---

## 3. Error handling — use a global exception handler (`IExceptionHandler`), not custom exception middleware

Use ASP.NET Core's built-in global exception handling (`IExceptionHandler` + `AddProblemDetails` + `app.UseExceptionHandler`), not a hand-written `try/catch` middleware component.

**How:**
- Implement `IExceptionHandler` (e.g. `GlobalExceptionHandler`) with `ValueTask<bool> TryHandleAsync(HttpContext, Exception, CancellationToken)`.
- Map domain/application exceptions to status + the standard `ResponseData<string>` envelope: `NotFoundException → 404`, `ForbiddenException → 403`, `ValidationException → 400` (include `Errors`), `ConflictException → 409`, everything else → `500` with a generic message (never leak stack traces/internal detail to the client). Log the exception server-side (secrets masked by the Serilog policy).
- Register: `builder.Services.AddExceptionHandler<GlobalExceptionHandler>();` and `builder.Services.AddProblemDetails();`
- Pipeline: `app.UseExceptionHandler();` early in the pipeline (replacing any custom exception middleware). Guard against writing after the response has started.

---

## 4. Index read-heavy / low-write entities by their real query patterns

Any column frequently used in a `WHERE`, `JOIN`, or `ORDER BY` must have an appropriate EF Core index declared in that entity's `IEntityTypeConfiguration`. Drive index choices from the **actual repository query methods**, not guesses.

**Rules:**
- Entities read far more than written (lookups/catalogs — e.g. `Tenant` by slug, `SubscriptionPlan` catalog, `Role` by name) should be liberally indexed: index-maintenance cost on writes is negligible because writes are rare, and the read speedup is large.
- Use **composite indexes** that match the predicate shape, in selectivity order. Examples grounded in this codebase's queries:
  - `SubscriptionPayment` → `(Status, DueDate)` for the overdue / due-soon background-job queries; plus `SubscriptionId`, `TenantId`.
  - `TenantSubscription` → `(TenantId, Status)` for active-subscription-by-tenant lookups.
  - `User` → unique index on `Email` (login), index on `TenantId` (per-tenant listing).
  - `BankAccount` → `TenantId`.
  - Append-only / high-write tables already indexed for their read paths: `AuditLog` `(EntityName, EntityId)` + `CreatedAt`; `UserLoginAttempt` `(Email, CreatedAt)`.
- Don't over-index hot write paths — every index adds write cost. Index where reads dominate or the predicate is on the critical path.
- Unique constraints (slug, email, role name) are both correctness guards and indexes — keep them.

## 5. Use the lightest collection type the consumer actually needs

Keep the codebase lightweight: pick the smallest-capability collection abstraction for each signature, by demand.

**Rules:**
- **Method parameters** that only iterate → `IEnumerable<T>`.
- **Return values** the caller only reads → `IReadOnlyList<T>` / `IReadOnlyCollection<T>` (or `IEnumerable<T>` when streaming/lazy is intended).
- **Public mutable collections** (caller must `Add`/`Remove`) → `ICollection<T>` / `IList<T>` — only when mutation is genuinely required.
- Avoid exposing concrete `List<T>` in public/service/DTO signatures unless `List`-specific behaviour is needed.
- Materialize a lazy query once at the boundary (`ToList()`/`ToArray()`) and pass `IReadOnlyList<T>` downstream — never re-enumerate an `IEnumerable<T>` multiple times (guards against multiple-enumeration of deferred EF queries).
- **EF Core exception:** entity **navigation collections** must stay `ICollection<T>` (or `List<T>`) — EF needs `Add` support for change tracking. Do **not** downgrade navigation properties to `IEnumerable<T>`.

---

_Conventions added 2026-06-24 (§1–3); §4–5 added 2026-06-24. Applies to Phase 1 code (refactored to comply) and all subsequent phases._
