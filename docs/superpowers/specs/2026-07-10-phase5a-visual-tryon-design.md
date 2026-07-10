# Phase 5a: Visual Try-On — Design Specification

**Date:** 2026-07-10
**Status:** APPROVED — pending user review of this written spec before plan-writing
**Depends on:** Phase 1 (SubscriptionPlan.AiUsageLimit field, JWT auth), Phase 2 (Product/ProductImage/Cloudinary), Phase 3 (storefront, customer auth), Phase 4a (Orders — no direct dependency, but shares conventions)

---

## 1. Goal

Let a customer upload a photo of themselves and see a specific product rendered onto their body before buying, on the product detail page. This is the first of two Phase 5 sub-projects (5a: Visual Try-On, 5b: Size/Fit Prediction — brainstormed and planned separately, in that order per explicit decision).

## 2. Scope decomposition context

"AI Virtual Try-On: Microservice for size/fit prediction" (the README's one-line description) actually names two architecturally distinct capabilities: image-based visual try-on (this doc) and numeric size/fit prediction (a separate future spec, Phase 5b). They were decomposed into separate phases because they have unrelated data sources, unrelated infra profiles, and unrelated ML approaches — bundling them into one spec would blur unrelated decisions.

**Explicitly decided, not incidental:** Phase 5a is built as a standalone microservice — this is deliberate, not a size-driven default. The user's long-term direction is a broader move toward microservices, and Try-On was chosen as the first candidate because it is genuinely greenfield (zero existing data/tables to migrate) and has a distinct infra profile (external paid AI API calls, cost isolation, independent scaling from checkout). A separate, explicit decision was made **not** to decompose the existing core commerce monolith (Customer/Order/Product/Tenant) at this time — see "Architecture rationale" below for the full reasoning; that decision stands independent of this feature and is not revisited by this spec.

## 3. Architecture

### 3.1 Service boundary

A new, fully independent .NET 10 Web API microservice: **`FashionSaaS.TryOn`**.

- **Location:** `services/fashionsaas-tryon/` — a new top-level folder, sibling to `src/` and `fashionsaas-storefront/`, NOT nested inside `src/`. This is the first extracted service and establishes where future services will live.
- **Solution:** its own `FashionSaaS.TryOn.sln`, independent of `FashionSaaS.sln`.
- **Database:** its own SQL Server database (own connection string, own EF Core migrations) — no foreign keys into the main platform's database, no shared DbContext.
- **Layering:** mirrors the main API's Clean Architecture conventions for consistency of developer experience — `FashionSaaS.TryOn.Domain` → `FashionSaaS.TryOn.Application` → `FashionSaaS.TryOn.Infrastructure` → `FashionSaaS.TryOn.Api` — even though each layer is thin given the smaller bounded context. Coding standards (rich domain entities, no primary constructors, async discipline, naming conventions, `dotnet format` + Roslyn LSP verification gate) apply identically to this new service.

### 3.2 Architecture rationale (decisions made during brainstorming, recorded for future reference)

- **Why a standalone service and not a feature inside the monolith:** greenfield with no migration risk, distinct infra profile (external paid AI API, cost/rate isolation from checkout), explicit user direction to begin a gradual microservices migration starting from new capabilities rather than decomposing existing ones (the "strangler fig" pattern).
- **Why the rest of the platform is NOT being decomposed right now:** the core commerce domain (Order/Product/Customer/Tenant) has deep transactional coupling today (placing an order is one ACID transaction spanning stock check, stock decrement, order creation, discount application) — splitting that requires distributed sagas, compensating transactions, and eventual consistency, a fundamentally harder problem than adding a new service. There is no current evidence of the classic microservices drivers (team-ownership conflicts, measured scaling bottlenecks, need for independent release cadence) that would justify that cost today. This was an explicit, discussed decision, not an oversight — revisit only when a concrete, measured pain point (not a hypothetical one) identifies where the real seam is. Reporting/Analytics was noted as the most likely *next* extraction candidate when that time comes (read-heavy, tolerates staleness, doesn't participate in transactional writes) — not in scope for any current phase.
- **Why its own database instead of sharing the monolith's:** true service autonomy — no schema change in either service can break the other, matches the stated microservices direction. Tradeoff accepted: the service cannot join to Product/Customer/Tenant tables, so any reference data it needs (e.g., the garment image URL) must be passed in by the caller rather than looked up.
- **Why JWT validation is independent rather than routing through the main API as a gateway:** true decoupling — the try-on service has zero runtime dependency on the monolith being up. Tradeoff accepted: JWT signing-key configuration must be duplicated (or shared via a secrets mechanism) across both services.
- **Why the AiUsageLimit quota is read from a JWT claim rather than a live API call to the main API:** same independence rationale. Tradeoff accepted: the limit is only as fresh as the customer's current access token (already short-lived, ~15 minutes, per the existing access+refresh token pattern), so a mid-session plan downgrade takes up to one token refresh to take effect. Acceptable given quota enforcement is a soft business control, not a security boundary.

## 4. Domain model

### 4.1 `TryOnRequest` entity

```
TryOnRequest : BaseEntity (Id, CreatedAt, UpdatedAt — matches main API's BaseEntity convention)
  TenantId: Guid
  CustomerId: Guid
  ProductId: Guid
  ProductVariantId: Guid?
  GarmentImageUrl: string       // snapshot of the product image used — this service cannot join to Products
  PersonImageUrl: string?       // Cloudinary URL, populated ONLY if customer opted to save their photo; null otherwise
  ResultImageUrl: string        // Cloudinary URL of the rendered result
  Status: TryOnStatus           // enum: Completed, Failed (synchronous flow — no Pending/Processing state needed since Gemini responds in one call)
  FailureReason: string?        // populated when Status = Failed (e.g. Gemini error, quota exceeded before the call was even attempted — though quota-exceeded requests may not warrant a persisted row at all; decide in planning)
  CreatedAt: DateTime            // UTC; also the quota-counting timestamp
```

Follows this codebase's established event-log entity pattern (`AuditLog`, `StockAdjustment`, `LoginAttempt`) — every render attempt is recorded, and the same table serves as the quota-counting mechanism: `COUNT(*) WHERE TenantId = X AND Status = Completed AND CreatedAt >= start-of-current-calendar-month`.

Tenant isolation: same EF Core global query filter pattern as the main API (`HasQueryFilter` referencing an injected `ICurrentTenantService`, resolved from the independently-validated JWT's `tenant_id` claim) — this service re-implements that pattern itself rather than sharing code with the monolith.

### 4.2 Saved photo (opt-in)

The customer's saved photo (when `PersonImageUrl` is populated) is associated with `(TenantId, CustomerId)`. Account settings in the storefront calls this service directly to fetch/delete it — exact endpoint shape decided during planning (likely `GET /api/tryon/saved-photo` and `DELETE /api/tryon/saved-photo`, scoped to the authenticated customer from their JWT).

## 5. Vendor integration — Gemini image API

- **Client:** a Refit-typed interface (explicit library approval from the user, scoped to third-party AI API clients for this project).
- **Flow:** synchronous request/response. The service sends the customer's uploaded photo + the garment image (fetched from the passed-in `GarmentImageUrl`) to Gemini's image generation/editing endpoint (model TBD at planning time between Gemini 2.5/3 Flash Image and Pro Image — Flash is cheaper and likely sufficient; verify current model names/endpoints against Google's current API docs at planning time, since vendor APIs evolve quickly) and receives the composited result image in the same call.
- **No job queue or polling** — unlike dedicated try-on vendors (FASHN.ai, etc.), which were evaluated and explicitly not chosen in favor of Gemini's more general-purpose, cheaper, synchronous API.
- **Known tradeoff, accepted:** Gemini is a general-purpose image model, not purpose-trained for garment-fit realism the way a dedicated try-on API is. Result quality/consistency (garment draping, pose fidelity) may be less proven. If quality proves insufficient after building this, the vendor integration is isolated behind the Application layer's service interface, making a swap to a dedicated vendor (e.g. FASHN.ai) a contained change, not a redesign.

## 6. Auth

- Main API's `JwtService` (existing) gains one new claim: `aiUsageLimit`, populated from the tenant's current `SubscriptionPlan.AiUsageLimit` at login/token-issuance time. This is the first real use of that Phase-1 field — it exists on `SubscriptionPlan` today but is currently read/enforced nowhere in the codebase.
- The Try-On service independently validates the same JWT (shared signing-key configuration) and reads `tenant_id`, customer identity, and `aiUsageLimit` directly from claims — no call back to the main API for any part of the request path.

## 7. Quota enforcement

Before calling Gemini: `COUNT(TryOnRequest WHERE TenantId = current tenant AND Status = Completed AND CreatedAt >= start of current calendar month)`. If `count >= aiUsageLimit` claim value, reject with a clear, friendly error (exact HTTP status/response shape decided in planning — likely 429 with a `ResponseData`-style envelope matching the main API's conventions for consistency, even though this is a different service).

## 8. Photo consent & retention

- **Default:** ephemeral. The uploaded photo is used for the single render and not persisted — `PersonImageUrl` stays null, nothing is written to Cloudinary for the input photo.
- **Opt-in save:** a consent checkbox ("Save my photo for future try-ons") on the try-on UI. If checked, the photo is uploaded to this service's own Cloudinary integration and `PersonImageUrl` is populated. On subsequent visits, if a saved photo exists, the customer can reuse it without re-uploading.
- **Management:** the customer can view/delete their saved photo from Account settings (storefront calls this service directly).
- **Isolation:** photos are tenant-scoped; no cross-tenant access. This service owns its own Cloudinary credentials/folder structure — implementation duplicates the main API's `IImageStorageService`/`CloudinaryImageStorageService` pattern rather than referencing it, per the "own database, own everything" service-autonomy decision.

## 9. Messaging — Azure Service Bus

- **Decision (explicit, discussed):** wire in Azure Service Bus now, even though no consumer exists yet, to establish the messaging pattern for future services to build on — a deliberate deviation from strict YAGNI, made knowingly.
- After every successful (`Status = Completed`) try-on, the service publishes a `TryOnCompleted` event to a Service Bus **topic** (name TBD at planning time, e.g. `tryon-events`).
- **Event payload (minimal):** `{ TryOnRequestId, TenantId, CustomerId, ProductId, CreatedAt }` — enough for a future consumer to react and pull further detail from this service's own API if needed; no garment/result image URLs in the event itself (keeps messages small; consumers fetch details on demand).
- **No consumer exists yet** — this is publish-only infrastructure for now. Local development/testing story for Service Bus (emulator vs. a real Azure namespace for dev) must be verified against current Microsoft Learn documentation during plan-writing, since Azure tooling in this space evolves quickly and post-dates general training knowledge.
- Uses the official `Azure.Messaging.ServiceBus` SDK (first-party Microsoft/Azure package — verify current API shape via Microsoft Learn MCP during planning, per this project's standing rule for any Microsoft technology).

## 10. API contract (shape — exact routes/DTOs finalized during planning)

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/tryon` | POST | multipart: photo file + `garmentImageUrl` + `productId` + `productVariantId?` + `savePhoto: bool`. Returns the rendered result image URL. |
| `/api/tryon/saved-photo` | GET | Returns the customer's saved photo URL, if any. |
| `/api/tryon/saved-photo` | DELETE | Deletes the customer's saved photo. |

Response envelope is a fresh, independent implementation matching the main API's `ResponseData<T>` *shape* (`IsSuccess`, `StatusCode`, `Message`, `Data`, `Errors`) — not a shared type or project reference — so the storefront's existing `ApiService` unwrapping logic works unchanged against this service too, while the two codebases stay fully decoupled.

## 11. Frontend (storefront) integration

- New environment config entry: `tryOnApiBaseUrl` (both `environment.ts` and `environment.prod.ts`).
- A "Try It On" section on the product detail page (`features/catalog/components/product-detail/`): photo upload (or "use my saved photo" if one exists), consent checkbox, submit, loading state, result display.
- Saved-photo view/delete control added to Account settings, calling the try-on service directly (same direct-call pattern as the main try-on flow — no proxying through the main API).
- Zoneless CD, Vitest conventions, strict TS, WCAG 2.1 AA — all established storefront conventions apply unchanged.

## 12. Error handling

- Gemini API failure (timeout, content policy rejection, malformed image, etc.) → `TryOnRequest.Status = Failed`, `FailureReason` populated, friendly error surfaced to the customer via the storefront's toast pattern (from the admin area's shared kit, or a customer-facing equivalent — decide in planning whether the storefront already has an equivalent toast mechanism or needs one).
- Quota exceeded → clear, friendly message before any Gemini call is attempted (no wasted spend).
- Photo upload validation (file type, size limits) enforced before any external call.

## 13. Testing strategy

- **Backend (new service):** xUnit + FluentAssertions + Moq, mirroring the main API's conventions — domain tests (status/quota logic), application tests (service orchestration, Gemini client mocked via Refit's testability, quota enforcement edge cases), infrastructure tests (repository, tenant isolation via the in-memory-DbContext + mocked `ICurrentTenantService` pattern already established), and one end-to-end workflow test per the pattern that caught real bugs in Phase 4a (real repositories over a shared in-memory context).
- **Frontend:** Vitest, established zoneless conventions (no fakeAsync, `setInput`, `TestBed.resetTestingModule`, `provideRouter`), DOM-level assertions for the try-on UI flow (matches the lesson from Phase 4b's duplicate-render bug — assert rendered DOM, not just component state).
- **Cross-service:** at minimum, a manual/documented smoke test verifying a JWT issued by the main API is accepted by the independently-validating try-on service (the two services must agree on signing key/algorithm — this is a real integration seam worth an explicit test, likely an integration test in the try-on service's test suite constructing a token with the same shared secret).

## 14. Out of scope for Phase 5a (explicitly deferred)

- Size/fit prediction (Phase 5b — separate spec, separate design conversation).
- A dedicated try-on history/gallery page for customers.
- Moderation of uploaded customer photos (content safety review) — flagged as a real future consideration given photo uploads, not addressed now.
- Any Service Bus **consumer** — publish-only in this phase.
- Per-plan-tier configuration UI for `AiUsageLimit` (the field already exists and is settable via the existing admin subscription-plan CRUD from Phase 4b; no new UI needed to set the number itself, only to *enforce* it, which this phase does).
- Decomposing any other part of the platform into further microservices (explicitly discussed and deferred — see section 3.2).

## 15. Open items for the planning stage

- Exact Gemini model name/endpoint and current pricing tier — verify against Google's current docs at plan-writing time (fast-moving space).
- Exact Azure Service Bus local-dev/testing approach (emulator availability, connection-string management for local dev vs. cloud) — verify against current Microsoft Learn documentation.
- Whether a quota-exceeded attempt gets its own `TryOnRequest` row (for audit/analytics of "how often are tenants hitting their limit") or is rejected without persistence — lean toward recording it (helps evaluate whether limits are set sensibly) but decide explicitly during planning.
- Exact toast/notification mechanism on the customer-facing storefront (confirm whether one already exists outside the admin area, or needs to be added).
