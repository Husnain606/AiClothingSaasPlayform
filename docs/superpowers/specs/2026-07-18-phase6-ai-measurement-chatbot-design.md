# Phase 6: AI Body Measurement + Fashion Chatbot — Design Specification

**Date:** 2026-07-18
**Status:** APPROVED — pending user review of this written spec before plan-writing
**Depends on:** Phase 1 (`SubscriptionPlan.AiUsageLimit`, JWT `ai_usage_limit` claim), Phase 2 (Product catalog — sizes, description, for chat product context), Phase 3 (storefront, customer auth), Phase 5a (`FashionSaaS.TryOn` microservice — this phase extends it in place; Gemini vendor integration, stateless-photo pattern, quota mechanism all established there).

---

## 1. Goal

Give a storefront customer two AI-assisted shopping aids, both riding on the existing `FashionSaaS.TryOn` microservice rather than a new one:

1. **Body measurement** — upload a photo (+ optional height), get back estimated body measurements and a recommended size.
2. **Fashion chatbot** — a floating chat widget, answering sizing/product/fashion questions, optionally grounded in the product the customer is currently viewing.

## 2. Scope decomposition context

Per Phase 5a §2, "AI Virtual Try-On" was always two capabilities: visual try-on (5a, built) and size/fit prediction (5b, deferred at the time). This phase supersedes the old "5b" label: brainstorming settled on **body measurement + size recommendation** as the concrete shape of the size/fit capability, and bundled it with a second, independently-useful AI feature (the chatbot) because both are thin, stateless, Gemini-backed additions to the same service with the same quota pool — splitting them into separate microservices or separate quota pools would be arbitrary, not principled.

**Explicitly decided, not incidental (D1):** both features extend `FashionSaaS.TryOn` in place. No new service, no new solution, no new database. This is the opposite lean from 5a's "new capability → new service" heuristic — but the reasoning is the same underlying test (see 5a §3.2): does the new work have a genuinely distinct infra profile or transactional boundary from what already exists? Here it does not — same auth model, same Gemini vendor, same quota pool, same stateless-photo posture, same tenant. Standing up a second/third microservice for two features that share literally every one of those axes with an existing service would be premature decomposition, the same anti-pattern 5a's rationale warns against in the other direction.

## 3. Architecture

### 3.1 Service boundary — extend, don't fork

Both features live inside the existing `services/fashionsaas-tryon/` solution, in new feature folders alongside `TryOn/`:

- `FashionSaaS.TryOn.Domain` → adds `MeasurementRequest`, `ChatRequest` entities (siblings of `TryOnRequest`).
- `FashionSaaS.TryOn.Application` → adds `Measurement/`, `Chat/`, and a `Gemini/` text-generation client alongside the existing image client; adds a shared `Quota/IUsageQuotaService` abstraction.
- `FashionSaaS.TryOn.Infrastructure` → adds `Measurement/MeasurementService.cs`, `Chat/ChatService.cs`, `Quota/UsageQuotaService.cs`; extends `TryOnDbContext` with two new `DbSet`s and their `IEntityTypeConfiguration`s; extends `DependencyInjection.cs`.
- `FashionSaaS.TryOn.Api` → adds `MeasureController` (`POST /api/measure`) and `ChatController` (`POST /api/chat`), beside the existing `TryOnController`.

**No new project, no new `.sln`, no new database, no new auth pipeline.** The existing JWT bearer scheme, `ICurrentTryOnContext`, `ResponseData<T>` envelope, and Gemini vendor credentials are reused unchanged.

### 3.2 Architecture rationale

- **Why extend rather than fork a third microservice per feature:** the three features (try-on, measurement, chat) share tenant, auth, vendor, quota pool, and stateless-photo posture. 5a's own rationale for standing up a new service was distinct infra profile / cost isolation — none of that differs here. A single service with three thin feature folders is the simpler, more honest reflection of how coupled these capabilities actually are.
- **Why one combined quota pool across three tables, not per-feature quotas:** `SubscriptionPlan.AiUsageLimit` and the `ai_usage_limit` JWT claim are already a single number per tenant (Phase 1/5a) — there is no per-feature limit field anywhere in the schema, and introducing one now would need a main-API schema change explicitly out of scope for this backend-extension phase (D8: no changes to the main API). One pool, three contributing tables, is the only shape the existing data model supports without touching the monolith.
- **Why the Gemini text call reuses the same `/v1beta/models/{model}:generateContent` REST endpoint as the image client, via a second Refit interface rather than one shared interface:** verified against Google's current API docs (see §7) — the endpoint path is identical for text and image generation, but the request/response shapes diverge enough (text needs a `systemInstruction` top-level field and per-turn `role`; image needs `generationConfig.responseModalities: ["IMAGE"]` and has no system instruction or multi-turn `role` need in this codebase's existing usage) that forcing them into one Refit interface/DTO set would mean optional fields on both sides that are never simultaneously meaningful. Two small interfaces, two small DTO sets, one shared settings section (`GeminiSettings`, extended with a `TextModel` field) is more honest than one interface with a `Role`/`SystemInstruction` nobody sets for images.
- **Why photo statelessness (D3) extends to measurement but chat has no photo at all:** measurement's body photo is exactly as sensitive as try-on's — same rule, same justification (5a §8), reapplied verbatim. Chat never receives an image, so this concern doesn't arise there; the persisted `ChatRequest` row is pure metadata (lengths, not content) for the same audit/quota reason `TryOnRequest` is metadata-only.

## 4. Domain model

### 4.1 `MeasurementRequest` entity

```
MeasurementRequest : BaseEntity (Id, CreatedAt, UpdatedAt)
  TenantId: Guid
  CustomerId: Guid
  Status: MeasurementStatus          // enum: Completed, Failed — mirrors TryOnStatus
  FailureReason: string?
  HeightCmProvided: bool             // true if the customer supplied a reference height
  ChestCm: decimal?
  WaistCm: decimal?
  HipsCm: decimal?
  ShoulderWidthCm: decimal?
  InseamCm: decimal?
  RecommendedSize: SizeCode?         // enum: XS, S, M, L, XL, XXL
  ConfidenceScore: decimal?          // 0.0–1.0, Gemini's self-reported confidence
  CreatedAt: DateTime                 // UTC; quota-counting timestamp
```

**No image fields — same fully-stateless rule as `TryOnRequest` (D3, reapplying Phase 5a §8 verbatim).** The measurement values are the only durable artifact; the uploaded body photo is held in memory only for the duration of the request and never written to disk, a database column, or blob storage. All numeric fields are nullable because a `Failed` row records no measurements.

### 4.2 `ChatRequest` entity

```
ChatRequest : BaseEntity (Id, CreatedAt, UpdatedAt)
  TenantId: Guid
  CustomerId: Guid
  Status: ChatRequestStatus          // enum: Completed, Failed
  FailureReason: string?
  MessageLength: int                // char count of the customer's latest message (not the transcript)
  ReplyLength: int                  // char count of the assistant's reply; 0 when Failed
  HadProductContext: bool           // true if the storefront passed productContext on this call
  CreatedAt: DateTime                // UTC; quota-counting timestamp
```

**Exact persistence decision (D5, resolved):** `ChatRequest` stores **lengths only, never the message or reply text**. Rationale: the conversation history is client-held per D5 (the storefront resends up to the last 20 messages each call — there is no server-side transcript to begin with), and this table's sole job is quota accounting + a minimal audit trail (mirroring `TryOnRequest`'s "bare usage-counter/audit row" pattern, Phase 5a §4.1) — not conversation logging. Storing free-text chat content would also introduce a PII/content-retention question this phase does not need to open. One row is written per `POST /api/chat` call (i.e., per customer message, matching D6's "1 chat message = 1 unit").

Both entities follow the same tenant-isolation pattern as `TryOnRequest`: an EF Core global query filter via the same `ICurrentTryOnContext`-backed mechanism, configured per-entity in `Infrastructure/Persistence/Configurations/`.

## 5. Vendor integration — Gemini

### 5.1 Measurement — a single multimodal request via the new text client (decided)

**Decided (resolves what was an open item at spec-writing time; see §16):** measurement does **not** reuse `IGeminiImageClient`. It is one multimodal `generateContent` request through the new `IGeminiTextClient` (§5.2) — the same client, model family, and DTO set chat uses. The customer's photo goes in as an `inline_data` part (base64-encoded bytes + `mimeType`), alongside a text part carrying the measurement prompt (§6.1); an optional `systemInstruction` carries the extraction persona. The response is parsed as JSON **text** — `candidates[0].content.parts[0].text` deserialized into `GeminiMeasurementResult` (chest/waist/hips/shoulderWidth/inseam/recommendedSize/confidence) — never as an image part, so no `generationConfig.responseModalities` setting is needed for this call. This means `IGeminiTextClient`'s request DTOs must support an optional inline-image part on a `GeminiTextPart` in addition to its existing text-only part, so the same client and DTO set handles both text-only chat turns and the image+text measurement call. The image-generation model configured as `GeminiSettings.Model` (`gemini-2.5-flash-image` today) is not used for measurement at all — measurement calls `GeminiSettings.TextModel` (§5.2), the same model chat uses.

### 5.2 Chat — new Refit text-generation client

A second Refit interface, `IGeminiTextClient`, targets the same host/path shape (`POST /v1beta/models/{model}:generateContent`) with a request/response DTO set shaped for text:

- `GeminiTextGenerateContentRequest { Contents: GeminiTextContent[], SystemInstruction: GeminiTextContent?, GenerationConfig: GeminiTextGenerationConfig? }`
- `GeminiTextContent { Role: string?, Parts: GeminiTextPart[] }` — `Role` is `"user"` or `"model"`, used to replay the client-held conversation history as alternating turns.
- `GeminiTextPart { Text: string?, InlineData: GeminiTextInlineData? }` — exactly one of `Text`/`InlineData` is set per part. `InlineData` (`GeminiTextInlineData { MimeType: string, Data: string }`, base64-encoded) is what lets measurement (§5.1) attach the customer's photo alongside its text prompt on this same DTO set; chat never sets it. Casing (`inlineData`/`mimeType`/`data`) mirrors the existing image-client DTOs (`GeminiPart.InlineData`/`GeminiInlineData` in `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Gemini/GeminiDtos.cs`) so both DTO sets stay consistent with the Gemini shape this codebase already uses.
- `GeminiTextGenerationConfig { Temperature: double?, MaxOutputTokens: int? }`
- Response: `GeminiTextGenerateContentResponse { Candidates: GeminiTextCandidate[]? }` → `Candidates[0].Content.Parts[0].Text` is the reply.

**Verified against Google's current REST API docs (`ai.google.dev/api/generate-content`), 2026-07-18:** `systemInstruction` is a top-level request field, sibling to `contents`, not nested inside it; each `contents[]` entry carries an optional `role` (`"user"` / `"model"`) for multi-turn chat; the response shape is `candidates[].content.parts[].text`. This confirms the existing image DTOs (Phase 5a's `GeminiContent`, which has no `Role` and no top-level `SystemInstruction`) cannot be reused unmodified for chat without adding fields never used by the image path — hence the separate DTO set (§3.2).

`GeminiSettings` (Application/Gemini) gains two fields, both decided defaults (not provisional), configurable per-tenant/per-deployment:
```
TextModel: string = "gemini-2.5-flash"            // [Required], same pattern as existing Model
ChatHistoryMaxTotalChars: int = 8_000              // total-char budget for the client-held chat history (§6.2, §9)
```
`ApiKey`, `BaseUrl`, and the `AllowedGarmentImageHosts` SSRF allowlist (measurement doesn't fetch a remote image so this list is irrelevant to it) are shared as-is.

## 6. Gemini prompting approach

Both prompts are **static, centrally defined constants** — not inline strings scattered through service code — in a new `Application/Gemini/GeminiPrompts.cs` static class. This satisfies CONVENTIONS' spirit of no-magic-strings-in-services and makes prompt changes a single-file review.

### 6.1 Measurement prompt

```
System role (implicit in the single-turn prompt, no systemInstruction needed — see rationale below):
"You are a body-measurement estimation assistant for an online clothing store. Given a single
photo of a person and, optionally, their height in centimeters, estimate their body measurements.
Respond with ONLY a JSON object matching this exact shape, no prose, no markdown fences:
{"chestCm": number, "waistCm": number, "hipsCm": number, "shoulderWidthCm": number,
 "inseamCm": number, "recommendedSize": "XS"|"S"|"M"|"L"|"XL"|"XXL", "confidence": number between 0 and 1}
If a height in cm is provided, use it as a scale reference for improved accuracy. If no height is
provided, estimate proportionally and lower the confidence score accordingly. Never ask the user
for more information — always return your best estimate in the exact JSON shape above."
```
The customer's photo is attached as inline image data (same `GeminiInlineData` pattern as try-on); the optional height is interpolated into the text part as `"Reference height: {heightCm} cm."` when supplied, omitted entirely when not (so the "if no height is provided" branch of the prompt is what governs, not an empty/zero placeholder).

**Why no `systemInstruction` for measurement:** it's a single-turn, single-purpose call with no conversation history — folding the instruction into the one text part alongside the image is simpler than standing up a `systemInstruction` field that would only ever hold this one static string. Chat, by contrast, has a real multi-turn history where separating persona/rules from the turn-by-turn content is the correct shape.

**Response parsing:** the returned text part is deserialized as `GeminiMeasurementResult` (a parse-only record: `ChestCm, WaistCm, HipsCm, ShoulderWidthCm, InseamCm, RecommendedSize, Confidence`, all matching the prompt's JSON keys via `JsonPropertyName`). A parse failure (malformed JSON, missing required key, out-of-range enum value) is treated as `MeasurementStatus.Failed` with `FailureReason = "Could not parse measurement response."` — never a silently-defaulted measurement.

### 6.2 Chatbot persona + guardrails

```
Static system instruction (Application/Gemini/GeminiPrompts.cs, sent as the request's systemInstruction
field on every /api/chat call):

"You are the shopping assistant for {TenantDisplayName}'s online store. You help customers with
fashion, sizing, and product questions.

Rules you must always follow:
1. Only answer questions about fashion, sizing, fit, materials, care instructions, or the products
   in this store. If asked about anything else (general knowledge, other brands, personal advice
   unrelated to shopping, or anything off-topic), politely decline and steer the conversation back
   to how you can help with their shopping.
2. Never invent facts about a specific product — price, stock, materials, or availability — unless
   that fact was given to you in this conversation's product context. If you don't have the
   information, say so and suggest the customer check the product page or contact support.
3. Never ask the customer for personal information (name, address, payment details, account
   credentials, or any other PII), and never repeat back any personal information the customer
   volunteers — redirect to the topic instead.
4. Keep responses concise and friendly, in plain text (no markdown tables or code blocks)."

Per-call product context (when the storefront passes productContext), appended as an additional
line of the system instruction, not as a fabricated prior chat turn: "The customer is currently
viewing: {name} — {description}. Available sizes: {sizes}."
```

**Why a static system instruction plus a dynamic product-context line, rather than folding both into one templated string per call:** the persona/rules block never changes call-to-call; only the product-context line varies (or is absent). Keeping them as two concatenated pieces (both still sourced from `GeminiPrompts`, one constant + one interpolation helper) keeps the constant reviewable as a single stable block while the per-call variance is visibly isolated to one line.

**Conversation history replay:** the client-held `messages` array (D5) is mapped 1:1 to `GeminiTextContent[]` turns with alternating `Role: "user"`/`"model"`, capped to the last 20 messages (older messages are simply not sent — the client already truncates per D5, but the service re-truncates defensively server-side rather than trusting the client cap).

**Response:** `Candidates[0].Content.Parts[0].Text`, returned as-is (no parsing/validation beyond non-empty) since it's free text for direct display, not structured data.

## 7. External API facts verified

- `POST /v1beta/models/{model}:generateContent` accepts both image-in/image-out (Phase 5a's usage) and text-in/text-out requests, including mixed image+text input under the text-generation configuration (§5.1); the endpoint path and auth (`x-goog-api-key` header) are identical across both usages. `systemInstruction` is a top-level sibling of `contents`, not nested. `contents[].role` distinguishes user/model turns for multi-turn context. Response text lives at `candidates[0].content.parts[0].text`. Source: `https://ai.google.dev/api/generate-content` (fetched 2026-07-18).
- **Decided (not provisional):** `gemini-2.5-flash` is the confirmed model name for `GeminiSettings.TextModel`, used by both measurement and chat.

## 8. Auth

Unchanged from Phase 5a (D8): the main API's JWT (with its existing `tenant_id`, customer identity, and `ai_usage_limit` claims) is validated independently by the try-on service's existing JWT bearer configuration. `MeasureController` and `ChatController` are `[Authorize]`-decorated exactly like `TryOnController`, reading tenant/customer/quota from the same `ICurrentTryOnContext`. **No changes to the main API** — the `ai_usage_limit` claim already covers all three features by virtue of being a single combined pool (§3.2).

## 9. Quota enforcement — combined pool across three tables

`SubscriptionPlan.AiUsageLimit` (read via the `ai_usage_limit` JWT claim, per Phase 5a §6–7) now gates **all three** features from one pool. `TryOnService.RenderAsync` today counts only `TryOnRequests` for the current tenant/month; this phase extracts that logic into a shared `IUsageQuotaService` (Application) / `UsageQuotaService` (Infrastructure) with:

```
Task<int> GetUsedThisMonthAsync(Guid tenantId, CancellationToken ct);
```

implemented, in its final form, as the sum of three independent `CountAsync` queries (one per table, each filtered `TenantId == tenantId && Status == Completed && CreatedAt >= startOfMonth`), rather than a SQL `UNION` — this keeps each entity's query index (`(TenantId, Status, CreatedAt)`, matching `TryOnRequestConfiguration`'s existing index shape, Phase 5a's Infrastructure config) doing exactly the work it was built for, and avoids a cross-table UNION query that EF Core cannot express cleanly across three unrelated `DbSet`s in one LINQ expression. `TryOnService`, `MeasurementService`, and `ChatService` each call `GetUsedThisMonthAsync` before their respective vendor call and compare against `currentContext.AiUsageLimit`, exactly mirroring `TryOnService`'s existing `usedThisMonth >= currentContext.AiUsageLimit` check (Phase 5a implementation, `TryOnService.cs:45`).

**Decided build sequencing (implementation plan detail, not a redesign):** since `MeasurementRequests`/`ChatRequests` don't exist until later in the build order, `UsageQuotaService` is built incrementally — summing only `TryOnRequests` when it's first created, then extended to add the `MeasurementRequests` term once that table lands, then the `ChatRequests` term once that table lands — reaching this section's three-table shape only at the end. `IUsageQuotaService`'s single-method signature never changes across that sequence.

**Quota-exceeded persistence:** each feature records its own `Failed` row with `FailureReason = "Monthly AI usage quota exceeded."` before rejecting — mirroring `TryOnService.RecordAsync` being called on the quota-exceeded path today (`TryOnService.cs:47`). This keeps the combined-pool audit trail complete (a tenant hitting their limit shows up in whichever table they hit it from).

## 10. Photo handling — measurement is fully stateless (chat has no photo)

Identical rule to Phase 5a §8, reapplied to measurement's body photo: the uploaded photo is held in memory only long enough to forward to Gemini; the response (structured JSON, not an image) is parsed and only the derived numeric values are persisted. The photo itself is never written to disk, a database column, or blob storage, and the measurement service needs no Cloudinary or blob-storage credentials. Chat receives no image input at all, so this section doesn't apply to it — chat's statelessness concern is instead about not persisting message content (§4.2).

## 11. API contract

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/measure` | POST | multipart: `photo` file + optional `heightCm`. Returns estimated measurements (chest/waist/hips/shoulderWidth/inseam, cm) + `recommendedSize` (XS–XXL) + `confidence`. Persists a `MeasurementRequest` row (values only, no image). |
| `/api/chat` | POST | JSON body: `messages: {role, content}[]` (client-held history, capped to last 20 / 8,000 total chars via `GeminiSettings.ChatHistoryMaxTotalChars`, decided default — mirroring D5), optional `productContext: {name, description, sizes}`. Returns `{ reply: string }`. Persists a `ChatRequest` row (lengths only). |

Both follow the existing `ResponseData<T>` envelope (`IsSuccess`, `StatusCode`, `Message`, `Data`, `Errors`) — the same internal `Api/Common/ResponseData.cs` type `TryOnController` already uses, extended to wrap the two new response DTOs.

Quota-exceeded response: `429`, same shape and tone as try-on's existing quota message (`TryOnService.cs:48`), reworded per-feature (e.g. "You've reached this month's AI usage limit...").

## 12. Frontend (storefront) integration

- **Find My Size** (D7a): a new section on the product detail page component (`features/catalog/components/product-detail/`), modelled directly on the existing "Try It On" section (`product-detail.component.ts`/`.html`, Phase 5a) — photo upload input, optional height field, submit button, loading/error state, and a result panel showing the estimated measurements plus the recommended size **highlighted against the product's actual available sizes** (`getUniqueSizes()`, already present on the component). A new `MeasurementService` (`features/catalog/services/measurement.service.ts`) mirrors `TryOnService`'s shape (FormData POST, `ApiResponse<T>` unwrap-or-throw).
- **Fashion chat widget** (D7b): a new standalone component (e.g. `features/chat/components/chat-widget/`), floating and available across the storefront (added once at the shell/app level, not per-page), backed by a new `ChatService` (`features/chat/services/chat.service.ts`) that holds the capped client-side message history in component/service state and POSTs it each turn. When opened from a product detail page, the host page passes `productContext` (name/description/available sizes) into the widget — the widget itself has no knowledge of "being on a product page"; that's the host's job to supply, keeping the widget reusable on non-product pages too.
- New environment config: both features reuse the existing `tryOnApiBaseUrl` (same service, same base URL) — no new environment keys needed.
- Zoneless CD, Vitest conventions, strict TS, WCAG 2.1 AA — unchanged, per established storefront conventions.

## 13. Error handling

- Gemini failure (timeout, malformed/unparseable response, content-policy rejection) → `Status = Failed`, `FailureReason` populated, friendly error surfaced via the storefront's existing toast/inline-alert pattern (same mechanism the Try It On section already uses for its own errors).
- Quota exceeded → `429` before any Gemini call, no wasted spend (§9).
- Photo upload validation (file type/size) for measurement enforced before any external call, via a `MeasurementRequestFormValidator` mirroring `TryOnRequestFormValidator` (Phase 5a).
- Chat input validation (non-empty message, message-array length cap, per-message and total character caps) via a `ChatRequestValidator`, FluentValidation, input-shape only per CONVENTIONS §8.

## 14. Testing strategy

Unchanged in kind from Phase 5a §13: xUnit + FluentAssertions + Moq across Domain/Application/Infrastructure/Api test projects already established for `FashionSaaS.TryOn`; Vitest for the two new storefront pieces, zoneless conventions, DOM-level assertions. Exact named test list is specified in the implementation plan, not here.

## 15. Out of scope for Phase 6 (explicitly deferred)

- Saving or reusing the measurement photo, in any form (§10, same rule as try-on).
- A persisted chat transcript / chat history page for customers — `ChatRequest` stores lengths only, never content (§4.2); there is nothing to show a history of.
- Per-feature quota tiers or a UI to configure them — one combined pool, no new configuration surface (§3.2, §9).
- Moderation of uploaded body photos (content safety review) — same open item Phase 5a flagged for try-on photos, not addressed here either.
- Any Service Bus event for measurement or chat completions — Phase 5a's `TryOnCompleted` publish-only pattern is not extended to these two features in this phase (no identified consumer need yet); revisit only if a concrete consumer emerges.
- Any change to the main API (`FashionSaaS.Api`/`FashionSaaS.Application`/etc.) — the `ai_usage_limit` claim already exists and already covers this combined pool (D8).
- Multi-language chat support, voice input, or image-based chat (customer sending a photo mid-conversation) — text-only, single-language chat as scoped in D5.

## 16. Open items for the planning stage — resolution log

- **RESOLVED (2026-07-18):** Gemini text-model for `GeminiSettings.TextModel` is `gemini-2.5-flash` — a confirmed configurable default, not provisional (§5.2, §7).
- **RESOLVED (2026-07-18):** measurement calls the new `IGeminiTextClient`/`TextModel`, not `IGeminiImageClient`/`Model` — a single multimodal request with the photo as an `inline_data` part alongside the text prompt, on the same DTO set chat uses (§5.1, §5.2).
- **RESOLVED (2026-07-18):** the chat total-character cap is 8,000 chars, a confirmed configurable default via `GeminiSettings.ChatHistoryMaxTotalChars` (§5.2, §9); the "last 20 messages" cap was already locked by D5.
- Still open: whether the storefront already has a customer-facing toast mechanism outside the admin area (Phase 5a §15 flagged this as unresolved for try-on too — resolve once, reuse for both). Not part of this resolution pass — a frontend styling choice, not a backend design/sequencing gap.
