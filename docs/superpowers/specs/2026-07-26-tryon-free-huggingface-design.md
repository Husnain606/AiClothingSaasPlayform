# Free Virtual Try-On via Hugging Face (Design)

**Date:** 2026-07-26
**Status:** Approved (design), pending implementation plan

---

## 1. Goal

Replace the Gemini-based image generation behind the storefront's "Try It On" feature
with a genuinely free backend, while keeping the render quality customers actually see
comparable to (or better than) commercial competitors — benchmarked directly against
**VibeTry**, the paid Shopify app (`vibetry-ai-try-on-and-sizing`) live on
zarposhcollection.com, whose UX is a 2-step "upload a full-body photo → visualize the
fit" modal, functionally identical to the flow this storefront already has.

**Why Gemini doesn't work today:** the dev API key's free tier has a hard `limit: 0` on
the image-generation model's token quota — confirmed via the actual 429 response, not a
guess. No amount of retrying fixes this; it needs a billing-enabled key, which is
explicitly what we're avoiding until the product actually launches.

**No payment gateway or paid API is introduced by this change.**

## 2. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Model source | **A Hugging Face Space (e.g. Kolors-Virtual-Try-On) duplicated into your own HF account** | Free, and a purpose-built try-on model outperforms Gemini's general-purpose image editing repurposed for this task. Duplicating (vs. calling a public demo Space directly) means you're not subject to a stranger's demo being rate-limited or taken down by other users. |
| Hosting tier | **Free CPU** to start | Zero cost. Render time is materially slower (1-5+ minutes) than Gemini's near-instant response — see UX flow below. Upgradeable to a paid GPU later purely as a speed optimization, not a functional requirement. |
| UX for the wait | **Async submit + SignalR push** | A synchronous HTTP call can't hold open for minutes. Reuses Phase 7's existing real-time notification infrastructure end-to-end. |
| Storefront experience | **Matches VibeTry's shape**: upload → "processing" state → result appears in place, no separate page/redirect | Already what this storefront does today for the synchronous case; this design only changes what happens *between* submit and result. |

### Explicit non-goals

- No change to Find My Size / the fashion chatbot — those use Gemini's **text** model,
  which has real free-tier quota and works fine today. This change is scoped to the
  **image generation** path only.
- No GPU upgrade, no billing enablement — purely the free tier.
- No attempt to exactly replicate VibeTry's UI; it's a quality/UX *reference point*, not a
  pixel-for-pixel target.

## 3. Architecture

Today: `POST /api/tryon` synchronously calls Gemini and returns the final image (or
error) in one response. Quota check, photo read, and garment-image fetch are unchanged
by this design — only the actual render call changes.

New flow:

1. **Submit** — `POST /api/tryon` does today's validation (quota, photo read, garment
   fetch), then submits the job to your duplicated Hugging Face Space instead of calling
   Gemini. Saves a `TryOnRequest` row with a new `Processing` status (added alongside
   today's `Completed`/`Failed`) plus the Space's job/event id. Returns `202` immediately
   with `{ requestId }`.
2. **Poll** — A new background worker in the try-on microservice polls Hugging Face for
   that job's completion on an interval, with a timeout (10 minutes) so a stuck job
   doesn't hang forever.
3. **Record + publish** — On completion, updates the `TryOnRequest` row (`Completed` +
   result image URL — Gradio Spaces serve output from their own stable URL, no separate
   upload step needed — or `Failed` + reason, reusing the existing 500-char truncation),
   then publishes one event to Service Bus (generalizing today's success-only
   `TryOnCompletedEvent` into a single event carrying both outcomes).
4. **Consume + push** (new, main API) — A new Service Bus consumer receives that event,
   creates a `Notification` (two new `NotificationType` values: `TryOnCompleted`,
   `TryOnFailed`), and pushes it over the existing `NotificationsHub` to
   `group("user:{customerId}")` — same "persist-then-push, swallow push failures"
   pattern as `OrderPlacedNotificationHandler`.
5. **Storefront** — shows a "processing" state after the `202`; the existing SignalR
   listener (Phase 7) handles the two new notification types to display the result image
   or error, in place, matching the reference UX.

## 4. Components

### 4.1 Try-on microservice — Domain

- `TryOnStatus` gains a third value: `Processing` (alongside existing `Completed`,
  `Failed`).
- `TryOnRequest` gains two nullable columns: `ExternalJobId` (string — the Hugging Face
  job/event id, so the poller knows what it's waiting on) and `ResultImageUrl` (string —
  the finished image's stable Gradio-served URL; no separate storage step needed, unlike
  the payment-proof feature's local files).

### 4.2 Try-on microservice — Application/Infrastructure

- **`IHuggingFaceTryOnClient`** — NOT a Refit interface like the existing
  `IGeminiImageClient`. Gradio's API is job-based: `POST {spaceUrl}/call/{api_name}`
  (submit, returns an event id) then `GET {spaceUrl}/call/{api_name}/{event_id}` (an
  SSE stream you read until a `complete` or `error` message). Refit doesn't model SSE,
  so this is a small hand-rolled `HttpClient`-based class — the one place in this
  feature that doesn't follow the existing Refit-client pattern, and that's noted
  explicitly rather than silently deviating.
- **`HuggingFaceSettings`** (`SpaceUrl`, `ApiToken`) — bound via `IOptions<T>` with
  `.ValidateDataAnnotations().ValidateOnStart()`, same convention as `GeminiSettings`.
- **`TryOnService.RenderAsync`** splits into `SubmitAsync` (today's validation + quota +
  garment fetch, then submit to HF, save `Processing` row, return `202`) and a new
  `TryOnPollingWorker : BackgroundService` that polls `Processing` rows, asks the HF
  client for status, and on completion calls an updated `RecordAsync` (same truncation
  logic, now also setting `ResultImageUrl` on success).
- **`ITryOnEventPublisher`**'s payload generalizes from `TryOnCompletedEvent` to a single
  `TryOnResultEvent(TryOnRequestId, TenantId, CustomerId, ProductId, CreatedAt, IsSuccess,
  ResultImageUrl?, FailureReason?)` — published on both outcomes now (today: success only).

### 4.3 Main API (new)

- `NotificationType` gains `TryOnCompleted`, `TryOnFailed`.
- A new `BackgroundService` (e.g. `TryOnResultConsumer`) using
  `ServiceBusClient.CreateProcessor(topic, subscription)` on a **new** subscription on
  the existing `tryon-events` topic (needs adding to the Service Bus emulator config for
  local dev, and to the Bicep template for Azure) — deserializes `TryOnResultEvent`,
  calls `NotificationService.CreateAsync(...)`, pushes via
  `hubContext.Clients.Group($"user:{customerId}").SendAsync("ReceiveNotification", saved)`,
  wrapped in the same swallow-and-log try/catch as the existing handlers.

### 4.4 Storefront

- After the `202`, show a "processing" state keyed on `requestId`.
- Existing SignalR listener gets a case for `TryOnCompleted`/`TryOnFailed` (matched by
  `EntityId == requestId`) to swap in the result image or show the failure message, in
  the same panel — no redirect, matching the VibeTry-style in-place experience.

## 5. Error handling

- **Quota exceeded / garment-fetch failure** — unchanged from today: detected before any
  Hugging Face call, still return synchronously (`429` / `502`) with an immediate
  `Failed` row. No `Processing` state for these.
- **Hugging Face submit fails** (Space down, bad token, network error) — treated as a
  failed submission: record `Failed` synchronously, return `502`. No job to poll.
- **Poll timeout** — a `Processing` row that hasn't completed within 10 minutes is marked
  `Failed` with `"Try-on render timed out."` and the failure event is published, so the
  customer isn't left waiting forever on a free-tier cold start or queue backup.
- **Gradio SSE reports an error** (the Space's own pipeline failed) — mapped to `Failed`
  with the Space's error message, truncated to 500 chars exactly like today's
  Gemini-error truncation (this session's earlier bug fix stays load-bearing here).
- **HF config missing/invalid at startup** — `HuggingFaceSettings` fails fast at boot via
  `ValidateOnStart()`, not silently at first request.
- **SignalR push failure** — never fails the underlying operation; the `TryOnRequest` and
  `Notification` rows are already durably saved before the push is attempted.

## 6. Testing

Covered in full in the brainstorming session; summary:

- **Try-on microservice**: unit tests for the Gradio SSE client (submit/complete/error/
  dropped-connection), the polling worker (pending/complete/fail/timeout), and
  `SubmitAsync` (unchanged quota/garment checks + new `202`/`Processing` path).
- **Main API**: unit tests for the new Service Bus consumer (success → `TryOnCompleted`
  notification + push; failure → `TryOnFailed`; push failure swallowed, never rethrown).
- **Storefront**: component tests for the processing-state persistence and the two
  notification-driven UI outcomes.
- **Manual/integration**: one full live run once the Hugging Face account and duplicated
  Space exist — submit → `Processing` appears → poller completes it → SignalR push
  reaches a connected client → result renders.

## 7. Follow-on work (not in this spec)

- **GPU upgrade**: if free-tier CPU latency proves unacceptable in practice, upgrading
  the duplicated Space to a paid GPU tier is a Hugging Face account-level change with no
  code impact — the polling worker already tolerates variable latency.
- **Alternate model**: if Kolors-Virtual-Try-On's quality/reliability disappoints in
  practice, swapping to a different duplicated Space (e.g. IDM-VTON) only touches
  `HuggingFaceSettings.SpaceUrl` and possibly the SSE payload shape in
  `IHuggingFaceTryOnClient` — the rest of the architecture (polling, events, SignalR
  push) is model-agnostic.
