---
description: Error handling, configuration, and third-party HTTP conventions for the FashionSaaS backend
---

# Error handling, configuration, and external HTTP calls

Per `docs/CONVENTIONS.md` §1–3 (confirmed in real code: `GlobalExceptionHandler.cs`, Options
pattern across `*Settings` classes, `IGeminiImageClient` via Refit).

**Error handling** — use ASP.NET Core's built-in `IExceptionHandler`, not hand-written
try/catch middleware:
- Map exceptions to status + the `ResponseData<string>` envelope: `NotFoundException → 404`,
  `ForbiddenException → 403`, `ValidationException → 400` (with `Errors`), `ConflictException →
  409`, everything else → `500` with a generic message. Never leak stack traces to the client.
- Log the exception server-side (secrets masked by `SensitiveDataDestructuringPolicy`).

**Configuration** — bind a strongly-typed settings class + `IOptions<T>`, never
`configuration["Section:Key"]` string indexing:
- One POCO per config section (`JwtSettings`, `GeminiSettings`, `ServiceBusSettings`, etc.).
- `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` where startup
  validation matters.
- Secrets still come from environment variables / gitignored `appsettings.Development.json` /
  Key Vault — the Options pattern binds them, it doesn't relocate them.

**Third-party HTTP calls** — use a Refit interface, not hand-rolled `HttpClient` plumbing:
- Declare the interface in the Application layer, register with
  `AddRefitClient<T>().ConfigureHttpClient(...)` in Infrastructure DI.
- Base URLs/keys come from Options, never hard-coded.
- If a call involves fetching a resource whose size isn't controlled by us (e.g. an image URL
  supplied in a request), don't use an unbounded call like `GetByteArrayAsync` — stream with a
  `Content-Length` check and a bounded read so an oversized or slow response can't exhaust memory.

**Logging** — Serilog, structured, secrets masked:
- Message templates with named properties (`logger.LogInformation("Product {ProductId} ...", id)`),
  never string interpolation.
- Never log secrets/PII directly; keep `SensitiveDataDestructuringPolicy`'s property set current
  when adding new sensitive DTOs.
- A "must never throw" boundary type (e.g. a best-effort event publisher) may use a bare
  `catch (Exception)` deliberately — but only with a one-line comment citing the contract and,
  if an analyzer flags it, a narrowly-scoped `#pragma warning disable/restore` around just that
  block, never a blanket suppression.
