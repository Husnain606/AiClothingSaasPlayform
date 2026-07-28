# Phase 9a — Manual Payment Proof for Customer Orders (Design)

**Date:** 2026-07-25
**Status:** Approved (design), pending implementation plan
**Scope:** Customer checkout only. Tenant platform-subscription payment proof is a **separate**
sub-project (Phase 9b) that reuses the storage abstraction defined here.

---

## 1. Goal

Replace the current mock card-payment step at checkout with a **manual/offline payment
verification flow**, the pattern WooCommerce calls "Direct Bank Transfer":

1. The customer pays out-of-band (bank transfer, wallet, etc.).
2. At checkout, the customer **uploads proof of payment** (image or PDF). Proof is mandatory —
   the order cannot be submitted without it.
3. The order is created **on hold** (`OrderStatus.Pending`) and is *not* fulfilled yet.
4. The receiving **tenant reviews the proof** and either approves it (order proceeds) or
   rejects it (order cancelled with a reason).
5. Only after approval does the order continue through the existing
   `Confirmed → Shipped → Delivered` lifecycle.

**No payment gateway is integrated.** There is no Stripe/PayPal/card processing, and no card
data is stored anywhere in the system after this change.

## 2. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Proof required at checkout | **Yes — blocking** | One linear checkout; no orphaned unpaid orders with no proof. |
| Order status model | **Reuse existing `OrderStatus`** | `Pending` = "on hold, awaiting proof review"; `Confirmed` = proof approved; `Cancelled` + reason = proof rejected. No new enum values, no status-migration churn. |
| Approve / reject endpoints | **Reuse existing `confirm` / `cancel`** | Both already exist and already audit-log + raise domain events. Only one new guard is added (confirm requires a proof). |
| Storage now | **Local filesystem** | Dev/pre-deployment. |
| Storage later | **Azure Blob Storage** | Swapped by changing **one DI registration line**; no other code changes. |
| Serving proofs | **Stream through a backend endpoint** | Identical code for local and Azure. DB stores an opaque storage key, never a URL — so no SAS-URL logic leaks into the app when Azure is adopted. |

### Explicit non-goals

- No payment gateway, no card processing, no PCI scope.
- No automatic proof validation (no OCR, no amount matching) — a human reviews it.
- No partial payments or instalments — one proof per order.
- No customer re-upload after rejection. A rejected order is `Cancelled`; the customer places
  a new order. (Deliberately deferred; revisit if it proves painful in practice.)

## 3. Architecture

```
Customer                     API                        Storage            Tenant
   |                          |                            |                 |
   |-- POST /api/store/orders |                            |                 |
   |   (multipart: order      |                            |                 |
   |    JSON + proof file) -->|                            |                 |
   |                          |-- validate type/size/magic |                 |
   |                          |-- SaveAsync(stream) ------>|                 |
   |                          |<-- storageKey -------------|                 |
   |                          |-- BEGIN TX                 |                 |
   |                          |   insert Order (Pending)   |                 |
   |                          |   insert OrderPaymentProof |                 |
   |                          |   decrement stock          |                 |
   |                          |-- COMMIT                   |                 |
   |<-- 201 (order on hold) --|                            |                 |
   |                          |                            |                 |
   |                          |<-- GET .../payment-proof ------------------- |
   |                          |-- OpenReadAsync(key) ----->|                 |
   |                          |--- stream + content-type ------------------> |
   |                          |                            |                 |
   |                          |<-- PUT .../confirm  (approve) ------------- |
   |                          |     or .../cancel  (reject + reason)        |
```

Everything downstream of `Confirmed` (`Shipped`, `Delivered`) is **untouched**.

## 4. Components

### 4.1 Domain — `FashionSaaS.Domain`

New entity (rich mutable entity, not a record — per project conventions):

```csharp
public class OrderPaymentProof : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public string StorageKey { get; set; } = string.Empty;      // opaque; never a URL
    public string ContentType { get; set; } = string.Empty;     // validated allowlist value
    public string OriginalFileName { get; set; } = string.Empty; // display only; never used in a path
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }

    public Order Order { get; set; } = null!;
}
```

`Order` changes:
- **Add** `public OrderPaymentProof? PaymentProof { get; set; }` (one-to-one).
- **Remove** the card fields (`CardLast4`, and any cardholder-name field) — no card data is
  captured anymore. Their removal is part of the EF migration.

`Tenant` change:
- **Add** `public string? PaymentInstructions { get; set; }` — the tenant-authored text telling
  customers where to send payment (see §7).

### 4.2 Application — `FashionSaaS.Application`

**New storage abstraction** (`Application/Interfaces/IPaymentProofStorageService.cs`), sitting
beside the existing `IImageStorageService`:

```csharp
public interface IPaymentProofStorageService
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<(Stream Content, string ContentType)> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
```

`SaveAsync` returns the opaque storage key to persist. `DeleteAsync` is best-effort (used only
for orphan cleanup) and must never throw.

**New repository** `IOrderPaymentProofRepository` following the existing repository pattern.

**Removed:** `CreateOrderPaymentDto`, and `CreateOrderRequest.PaymentInfo`.

**`OrderService` changes:**
- `CreateAsync` takes the proof (stream, original filename, content-type, size). It calls
  `SaveAsync` first, then creates the `Order` **and** the `OrderPaymentProof` inside the *same*
  `IUnitOfWork` transaction as the existing stock decrements. An order can never be committed
  without its proof row.
- `TransitionAsync` gains one guard: if `target == OrderStatus.Confirmed` and the order has no
  `PaymentProof`, return `400` — "Payment proof is required before confirming this order."
  All other transitions are unchanged.
- New `GetProofForTenantAsync(Guid orderId, ...)` — tenant-scoped.
- New `GetProofForCustomerAsync(Guid orderId, string customerEmail, ...)` — own-order only.

**New validator** (FluentValidation, in the Orders feature folder) enforcing *input shape only*
per the project's validation-scope rule: proof present, content-type in allowlist, size within
the configured maximum.

### 4.3 Infrastructure — `FashionSaaS.Infrastructure`

`LocalFilePaymentProofStorageService : IPaymentProofStorageService`:
- Root directory from a new `PaymentProofStorageSettings` POCO, bound via the Options pattern with
  `.ValidateDataAnnotations().ValidateOnStart()` (consistent with existing `*Settings` classes).
- Key shape: `{tenantId}/{orderId}/{guid}{ext}`.
- **The filename is always server-generated** from the validated content-type — the client's
  filename never contributes to the path.
- On read, the resolved absolute path is verified to sit under the configured root; anything
  escaping it is rejected (path-traversal guard).

Also: `IEntityTypeConfiguration<OrderPaymentProof>` in `Persistence/Configurations/`
(`StorageKey`, `ContentType`, `OriginalFileName` given explicit `HasMaxLength`; `TenantId`
indexed; unique index on `OrderId` to enforce one-to-one), plus an EF migration covering the new
table, the dropped card columns, and the new `Tenant.PaymentInstructions` column.

**DI (`Program.cs`) — the single swap point:**

```csharp
services.AddScoped<IPaymentProofStorageService, LocalFilePaymentProofStorageService>();
// Azure later: swap this one line for AzureBlobPaymentProofStorageService. Nothing else changes.
```

### 4.4 API — `FashionSaaS.API`

| Endpoint | Change |
|---|---|
| `POST /api/store/orders` | `[FromBody]` JSON → `[FromForm]` multipart (order fields + `IFormFile paymentProof`), with `[RequestSizeLimit]` matching the configured max. |
| `GET /api/tenant/orders/{id}/payment-proof` | **New.** Streams the proof for a tenant's own order. |
| `GET /api/store/orders/{id}/payment-proof` | **New.** Streams the proof for the caller's own order. |
| `PUT /api/tenant/orders/{id}/confirm` | Unchanged signature; now rejects with `400` if no proof exists. |
| `PUT /api/tenant/orders/{id}/cancel` | Unchanged — this *is* the rejection path (reason required). |
| `GET /api/{slug}/payment-instructions` | **New, public** (see §7). Returns the tenant's free-text instructions only. |
| `PUT /api/tenant/profile` | Accepts the new `PaymentInstructions` field. |

New route constants go in `ApiUrl.StoreOrders` / `ApiUrl.TenantOrders` / `ApiUrl.PublicCatalog`.

### 4.5 Storefront — `fashionsaas-storefront`

- **Payment step:** the card form is replaced by a file input
  (`accept="image/*,application/pdf"`) with client-side type/size feedback. "Place Order" stays
  disabled until a file is attached.
- **Payment instructions** (where to send the money) are rendered above the upload control,
  fetched from the new public endpoint — see §7.
- **Review step:** shows the attached filename instead of "Card ending in ….".
- **Confirmation + order detail:** a "View payment proof" link hitting the store proof endpoint,
  and copy making clear the order is *on hold pending payment verification*.
- **Tenant admin order detail:** the proof rendered inline (image) or linked (PDF), beside the
  existing Confirm / Cancel actions.

## 5. Error handling

**File validation is defense-in-depth** — the client-side `accept` attribute is trivially
bypassed, so the validator *and* the service both check:

- **Allowlist, never a blocklist:** `image/jpeg`, `image/png`, `image/webp`, `application/pdf`.
- **Magic-number verification:** the declared content-type must match the file's actual leading
  bytes — JPEG `FF D8 FF`, PNG `89 50 4E 47`, WebP `RIFF....WEBP`, PDF `%PDF`. A mismatch is
  `400`. This prevents an executable renamed `.pdf` from ever reaching storage.
- **Size cap: 10 MB**, from configuration (`PaymentProofStorageSettings.MaxFileSizeBytes`,
  default `10_485_760`), enforced by the validator *and* `[RequestSizeLimit]`.

| Failure | Response |
|---|---|
| Proof missing / wrong type / oversized / magic-number mismatch | `400` + field errors in the `ResponseData` envelope |
| Storage `SaveAsync` fails | `502`, safe message ("We couldn't save your payment proof. Please try again.") — never leaks paths or provider internals |
| DB commit fails after file written | Order creation fails; the orphaned file is best-effort deleted (failure logged, never thrown). An orphaned file is harmless; an order without a proof is not. |
| Confirm on an order with no proof | `400` |
| Tenant requests another tenant's proof | `404` (query filter + explicit check) |
| Customer requests another customer's proof | `404`, **not** `403` — so it doesn't leak that the order exists |
| `storageKey` resolves outside the configured root | Rejected before any read (path-traversal guard) |

Secrets and absolute paths are never logged, per the project's logging rules.

## 6. Testing

**Unit — validator:** proof missing; each disallowed content-type; at-limit and over-limit sizes.

**Unit — `OrderService`:** proof persisted in the same transaction as the order; storage failure
aborts creation; DB failure triggers orphan cleanup; confirm-without-proof returns `400`;
confirm-with-proof succeeds; cancel-with-reason still works as the rejection path.

**Unit — storage service:** save → read round-trip is byte-identical; magic-number mismatch
rejected; path-traversal key rejected; `DeleteAsync` swallows a missing-file error.

**Unit — authorization:** cross-tenant proof read → `404`; cross-customer proof read → `404`.

**Unit — payment instructions:** the public endpoint returns the tenant's string and **no** bank
fields; an unset value yields the fallback rather than an error; the profile-update validator
enforces the max length.

**Integration / manual:** full flow end-to-end — customer uploads proof → order lands `Pending`
→ tenant views the proof → approve path reaches `Confirmed → Shipped → Delivered`; reject path
reaches `Cancelled` with the reason recorded.

## 7. Payment instructions (how the customer learns where to pay)

**The constraint.** Verified against the code: `GET /api/tenant/bank-account` is
`[Authorize(Roles = "AdminOwner")]` (`Controllers/Tenant/TenantBankAccountController.cs:14`) and
the platform one is `[Authorize(Roles = "SuperAdmin")]` + `[Authorize(Policy = "MfaVerified")]`
(`Controllers/Admin/BankAccountController.cs:13-14`). A `Customer`-role caller can reach
**neither**, and `BankAccount` fields are AES-256-GCM encrypted at rest — deliberately sensitive.

**Decision: the encrypted `BankAccount` record is never exposed to customers.** Instead the
tenant writes their own free-text payment instructions, so the tenant controls exactly what is
disclosed and the encrypted record stays behind the admin boundary untouched.

- `Tenant` gains `public string? PaymentInstructions { get; set; }` (nullable text, generous
  `HasMaxLength`, e.g. 2000).
- Editable by the tenant via the existing `PUT /api/tenant/profile` (add the field to the
  update request + validator + the profile DTO).
- Readable by customers via a new **public** endpoint alongside the other
  `PublicCatalog` routes — `GET /api/{slug}/payment-instructions` — resolved by the existing
  `TenantResolutionMiddleware`. It returns only that string; no bank fields, nothing encrypted.
- The storefront payment step renders it above the upload control. If the tenant hasn't set it,
  the step shows a neutral "contact the store for payment details" fallback rather than an empty
  panel.

## 8. Follow-on work (not in this spec)

- **Phase 9b:** the same manual-proof pattern for **tenant platform-subscription** payments —
  tenant uploads proof against a `SubscriptionPayment`, SuperAdmin approves via the existing
  `PUT /api/admin/payments/{id}/confirm`. Reuses `IPaymentProofStorageService` unchanged.
- **Azure Blob adoption:** implement `AzureBlobPaymentProofStorageService`, change the one DI line.
- Notifying the customer when their proof is approved or rejected (the SignalR/notification
  infrastructure from Phase 7 exists; wiring a proof-reviewed notification is a separate change).
