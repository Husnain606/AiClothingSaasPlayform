# Phase 5a: Visual Try-On — Phased Implementation Plan (Master)

> **Goal**: Stand up a **new, standalone** `.NET 10` microservice, `FashionSaaS.TryOn`, at
> `services/fashionsaas-tryon/`, with its **own SQL Server database**, that lets a storefront
> customer upload a photo and see a chosen product composited onto it via Gemini's image API,
> enforces a per-tenant monthly quota sourced from a new JWT claim, and publishes a
> publish-only `TryOnCompleted` event to Azure Service Bus. **Nothing in the existing
> `FashionSaaS.API` solution changes except:** `JwtService.GenerateAccessToken` gains one new
> claim (`ai_usage_limit`), and `AuthService` gains a `SubscriptionRepository` lookup to source
> it. **No image (input photo or rendered result) is ever persisted anywhere, in either
> service** — this is the core non-negotiable constraint of the whole feature (spec §8).
> **Detail**: each phase's buildable plan lives under `phases/`; this master is the index +
> source of locked decisions.
> **Spec**: [`docs/superpowers/specs/2026-07-10-phase5a-visual-tryon-design.md`](../../superpowers/specs/2026-07-10-phase5a-visual-tryon-design.md) (approved, commit `c9aec8d`).
> **Anchors**: `File.cs:line` anchors below are code-verified as of this plan's authoring — re-confirm on touch.

## 1. Locked decisions (do not revisit without sign-off)

| # | Decision | Resolution |
|---|---|---|
| D1 | Service topology | **Standalone microservice** `FashionSaaS.TryOn` at `services/fashionsaas-tryon/`, own solution (`FashionSaaS.TryOn.sln`), own SQL Server database (own connection string, own EF Core migrations) — no FKs into the main platform DB. |
| D2 | Layering | Mirrors the **actual** (not aspirational) main-API conventions: `FashionSaaS.TryOn.Domain` → `FashionSaaS.TryOn.Application` → `FashionSaaS.TryOn.Infrastructure` → `FashionSaaS.TryOn.Api`. Controllers-based API (the main API uses Controllers, **not** Minimal-API `IEndpoint` — see D3), `ResponseData<T>` envelope, FluentValidation, MediatR-free (the one `TryOn` use case doesn't need a mediator — see Phase 3 rationale). |
| D3 | Response envelope | Port the **actual** `ResponseData<T>` type (`FashionSaaS.Application.Common.ResponseData<T>`, verified at `src/FashionSaaS.Application/Common/ResponseData.cs:3-16`) into the new service's own `Common/` folder as an **independent copy** — no project reference to the main solution (spec §10: "a fresh, independent implementation... not a shared type"). `Result<T>` from `backend-architecture.md` is a design-doc-only construct, never implemented in this codebase — do not use it as a model. |
| D4 | Validation | FluentValidation **is** the real, actually-used convention in this codebase (verified: `src/FashionSaaS.Application/Categories/Validators/CreateCategoryRequestValidator.cs:1-29`, `Program.cs:8-9,53-58`) — despite `backend-architecture.md` calling it rejected. Per this project's own source-of-truth hierarchy (code > docs), the new service uses FluentValidation identically to the main API. |
| D5 | Persistence | EF Core 10 + `Microsoft.EntityFrameworkCore.SqlServer` (verified version `10.0.9` in `src/FashionSaaS.Infrastructure/FashionSaaS.Infrastructure.csproj`), matching the main API's actual provider (SQL Server, not the design doc's aspirational Postgres/Npgsql — that migration never happened; code wins). New service: own `TryOnDbContext`, own migrations. |
| D6 | Package versioning | This repo's actual `.csproj` files pin `Version=` directly per package (verified: `FashionSaaS.API.csproj`, `FashionSaaS.Infrastructure.csproj`) — `Directory.Packages.props` central management is aspirational for runtime packages (only Analyzers/Testing are pinned there today). The new service's `.csproj` files follow the **actual** convention: direct `<PackageReference Include="..." Version="...">`. |
| D7 | Auth | Main API's `JwtService.GenerateAccessToken` (`src/FashionSaaS.Infrastructure/Services/JwtService.cs:17-54`) gains one new claim, `ai_usage_limit` (int, stringified), sourced from `SubscriptionPlan.AiUsageLimit` (`src/FashionSaaS.Domain/Entities/SubscriptionPlan.cs:14`) via `ISubscriptionRepository.GetActiveByTenantIdAsync` (`src/FashionSaaS.Application/Interfaces/ISubscriptionRepository.cs:7`). The TryOn service independently validates the same JWT (shared `JwtSettings:Secret`/`Issuer`/`Audience` config) — **no** call back to the main API. |
| D8 | Quota source of truth | `TryOnRequest` rows in the **new service's own DB** are the quota counter: `COUNT(*) WHERE TenantId = X AND Status = Completed AND CreatedAt >= start-of-month`. The JWT's `ai_usage_limit` claim is the ceiling. |
| D9 | Gemini client | Refit-typed interface (Dan's explicit per-library approval, scoped to third-party AI API clients for this project — brainstorming transcript). Model: `gemini-2.5-flash-image` (verify current name/pricing against Google's live docs before Phase 3 build — flagged as an OPEN QUESTION in Phase 3). |
| D10 | Messaging | `Azure.Messaging.ServiceBus` (official SDK), publish-only, topic `tryon-events`, event `TryOnCompleted`, **no consumer** (explicit YAGNI deviation, spec §9). |
| D11 | Image persistence | **Zero image bytes ever touch disk, DB column, or blob storage in either service**, at any point, under any option (spec §8). `TryOnRequest` carries **zero** image fields — a bare usage-counter/audit row. |
| D12 | Frontend integration point | A "Try It On" section on the storefront's existing `ProductDetailComponent` (`fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.ts`) — not a new route/page. |
| Scope | **Scope boundary** | This plan builds Phase 5a (Visual Try-On) **only**. It does **not** touch Phase 5b (size/fit prediction — separate future spec), does **not** decompose any other part of the monolith into microservices (explicitly deferred, spec §3.2), does **not** add a Service Bus consumer, and does **not** add any saved-photo/history feature (impossible by design — spec §14). |

## 2. Implementation roadmap (sequence & dependencies)

| Phase | Area | Layer | Depends on | Detailed plan |
|---|---|---|---|---|
| **1** | Service scaffold: solution, 4 projects, `TryOnRequest` entity + EF config + migration, `TryOnDbContext`, health endpoint | Backend | — | [PHASE-1-SCAFFOLD.md](phases/PHASE-1-SCAFFOLD.md) |
| **2** | Auth: main API's `ai_usage_limit` claim + TryOn service's independent JWT validation & tenant/customer context | Backend (both solutions) | Phase 1 | [PHASE-2-AUTH.md](phases/PHASE-2-AUTH.md) |
| **3** | Gemini Refit client, `POST /api/tryon` endpoint, quota enforcement, error handling | Backend | Phase 1, 2 | [PHASE-3-GEMINI-ENDPOINT.md](phases/PHASE-3-GEMINI-ENDPOINT.md) |
| **4** | Azure Service Bus publish-only `TryOnCompleted` event | Backend | Phase 3 | [PHASE-4-SERVICEBUS.md](phases/PHASE-4-SERVICEBUS.md) |
| **5** | Angular: environment config, `TryOnService`, "Try It On" UI on product detail | Frontend | Phase 3 (needs a live endpoint contract) | [PHASE-5-FRONTEND.md](phases/PHASE-5-FRONTEND.md) |

**Status (live, 2026-07-11):**
- All phases: **not started.**

Phases 1→2→3 are strictly ordered (each builds on the DbContext/auth/endpoint the previous phase created). Phase 4 depends only on Phase 3's successful-completion code path. Phase 5 depends only on Phase 3's finalized HTTP contract (request/response DTOs) — it can start once Phase 3's endpoint signature is locked, in parallel with Phase 4. Each phase ends with its own **Validate** group (zero-warning build + Serena `get_diagnostics_for_file` + green named tests) — merge each phase's branch/commits before starting the next only if working serially; if using `subagent-driven-development`, execute strictly in the 1→2→3→(4∥5) order above since later phases' contract checklists assume earlier phases landed.

## 3. Phase summaries

**Phase 1 — Scaffold.** New `FashionSaaS.TryOn.sln` with `Domain`/`Application`/`Infrastructure`/`Api` projects (net10.0, matching main API's actual `.csproj` shape). `TryOnRequest : BaseEntity` (own copy of the 3-field `BaseEntity` — `Id`/`CreatedAt`/`UpdatedAt`, no domain events needed) with `TenantId`, `CustomerId`, `ProductId`, `ProductVariantId?`, `Status` (`TryOnStatus` enum: `Completed`/`Failed`), `FailureReason?`. `TryOnDbContext` with a single `DbSet<TryOnRequest>`, one EF migration, a `GET /api/health` endpoint proving the service boots and connects to its own DB.

**Phase 2 — Auth.** In the **main** `FashionSaaS.API` solution: `IJwtService.GenerateAccessToken` gains an `int aiUsageLimit` parameter; `JwtService` adds the `ai_usage_limit` claim; `AuthService` (constructor + `IssueTokensAsync`) is wired to fetch it via `ISubscriptionRepository.GetActiveByTenantIdAsync`, defaulting to `0` for tenant-less (SuperAdmin) logins. In the **new** `FashionSaaS.TryOn.Api`: JWT Bearer authentication configured against the same `JwtSettings` (shared `Secret`/`Issuer`/`Audience`), plus a `ICurrentTryOnContext` (this service's own minimal equivalent of `ICurrentTenantService`) populated from `tenant_id`, `sub` (customer id), and `ai_usage_limit` claims by a small middleware.

**Phase 3 — Gemini integration.** Refit interface `IGeminiImageClient` hitting Gemini's image-generation endpoint; `TryOnService` (Application layer) orchestrates: quota check (`COUNT` query against `TryOnRequest`) → fetch garment image via plain `HttpClient.GetAsync` → call Gemini with both images → persist a `TryOnRequest` audit row (`Completed`/`Failed`) → return result bytes directly in the HTTP response (base64 data URI). `TryOnController` exposes `POST /api/tryon` (multipart). No job queue — fully synchronous.

**Phase 4 — Service Bus.** `Azure.Messaging.ServiceBus` `ServiceBusClient`/`ServiceBusSender` registered as a singleton; after a `Completed` `TryOnRequest` is saved, `TryOnService` publishes a `TryOnCompleted` message (`{TryOnRequestId, TenantId, CustomerId, ProductId, CreatedAt}`) to topic `tryon-events`. Publish-only; failure to publish does **not** fail the customer-facing request (logged, swallowed — the event is a side-channel, not the source of truth).

**Phase 5 — Frontend.** `environment.ts`/`environment.prod.ts` gain `tryOnApiBaseUrl`. New `TryOnService` (Angular) POSTs the multipart request. `ProductDetailComponent` gains a "Try It On" section: file input, submit button, loading/error states, and an `<img>` showing the returned data-URI result — nothing saved, nothing persisted client-side beyond the current view.

## 4. Explicitly OUT of scope for this plan

- **Phase 5b (size/fit prediction)** — separate future spec/plan, not touched here.
- **Decomposing any other part of the platform into microservices** — explicitly discussed and deferred (spec §3.2); this plan touches only `FashionSaaS.TryOn` (new) and two narrow points in the existing `FashionSaaS.API`/`FashionSaaS.Application`/`FashionSaaS.Infrastructure` (the JWT claim + `AuthService` wiring in Phase 2) — no other existing file changes.
- **Any Service Bus consumer** — publish-only (spec §9, D10).
- **Saved-photo / history / gallery feature** — impossible by design (D11); no such endpoints, tables, or UI exist in any phase.
- **Photo moderation / content-safety review** — flagged as a real future consideration in the spec (§14), not addressed by this plan.
- **Per-plan-tier `AiUsageLimit` configuration UI** — the field and its admin CRUD already exist (Phase 4b); this plan only makes the existing value meaningful by enforcing it.

## 5. Risks

- **Gemini API surface/pricing drift** — the spec flags this space as fast-moving and post-dating training knowledge. Phase 3 carries an explicit OPEN QUESTION to re-verify the exact model name/endpoint/pricing against Google's live docs immediately before implementation, not from memory.
- **Azure Service Bus local-dev story** — no emulator confirmed yet for this environment. Phase 4 carries an explicit OPEN QUESTION to verify against current Microsoft Learn documentation before implementation (Microsoft Learn MCP is available and mandatory per `CLAUDE.md` for any Microsoft technology).
- **Shared JWT secret duplication** — Phase 2 duplicates `JwtSettings:Secret` into the new service's own `appsettings.Development.json`. This is an accepted, explicit tradeoff of true service independence (spec §3.2) — mitigated by using the **same** dev placeholder value so local dev "just works," with a note that production secret management (e.g., a shared vault reference) is an infra concern outside this plan's scope.
- **`AuthService` constructor churn (Phase 2)** — adding `ISubscriptionRepository` to `AuthService`'s primary constructor touches every existing `AuthServiceTests` test that constructs it. Mitigated by Phase 2's task listing the exact existing test file and every call site that needs updating (verified: only `tests/FashionSaaS.Application.Tests/Auth/AuthServiceTests.cs` and `tests/FashionSaaS.Infrastructure.Tests/Security/JwtServiceTests.cs` reference `GenerateAccessToken`).

**No further changes to this master plan will be made without your sign-off. Phase-level detail lives in `phases/` and may be refined per-phase during execution.**
