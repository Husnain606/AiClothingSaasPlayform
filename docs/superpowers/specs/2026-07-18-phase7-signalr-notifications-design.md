# Phase 7 — SignalR Real-Time + Notifications: Design Spec

> **Status:** DESIGN — not yet implemented. Companion buildable plan:
> [`docs/superpowers/plans/2026-07-18-phase7-signalr-notifications.md`](../plans/2026-07-18-phase7-signalr-notifications.md).
> Baseline verified 2026-07-18: `dotnet test` → **446 passed / 0 failed / 0 skipped**
> (24 Domain + 334 Application + 88 Infrastructure).

## 1. Overview

Phase 1 (`2026-06-18-phase1-core-saas-backend-design.md` §12, line 791) named "SignalR real-time
notifications" as explicit future work. This phase delivers it: a persisted `Notification` entity,
a REST history/unread-count/mark-read surface, one SignalR hub (`NotificationsHub`) for live push,
and Angular integration (admin bell + dropdown + toast; customer order-status toast).

**Goal:** admins and customers see in-product notifications for tenant-relevant events
(orders, payments, low stock, reviews) without polling, and without losing anything if the
live channel is down — the REST list is always the recoverable source of truth.

**Scope boundary — what this phase does NOT touch:**
- No Azure Service Bus consumption (publish-only try-on integration from Phase 5a is untouched — D7).
- No push notifications outside the browser tab (no web push / FCM / APNs).
- No notification preferences/settings UI (mute, digest, channel choice) — every trigger fires
  unconditionally to its recipient set.
- No customer-side notification history/bell — customers get a single ephemeral order-status
  toast only (D5); their notification rows are still persisted server-side for audit/REST parity,
  but no Angular storefront UI reads them back in this phase.

## 2. Architecture fit

Follows the existing layering exactly (Phase 1 spec §6, `Controller → Service → Repository`,
`ResponseData<T>` envelope) and the existing MediatR convention: **MediatR is used exclusively
for domain events published after a write commits** (Phase 1 spec §6, line 315-317) — never
for commands/queries, never invoked directly by a service via `_mediator.Publish`.

Confirmed dispatch mechanism (ground truth, not the Phase 1 spec's description — see drift note
below): every entity descends from `BaseEntity` (`src/FashionSaaS.Domain/Entities/BaseEntity.cs`),
which carries a private `List<IDomainEvent>` exposed via `AddDomainEvent`/`DomainEvents`/
`ClearDomainEvents`. `UnitOfWork.SaveChangesAsync`
(`src/FashionSaaS.Infrastructure/Persistence/UnitOfWork.cs`) collects entities with pending
`DomainEvents` **before** EF's `SaveChangesAsync`, then **after** the save, wraps each event as
`DomainEventNotification<TEvent>` (`src/FashionSaaS.Infrastructure/Persistence/DomainEventNotification.cs`)
and calls `IPublisher.Publish(...)`. Handlers are `INotificationHandler<DomainEventNotification<TEvent>>`
classes under `src/FashionSaaS.Infrastructure/EventHandlers/`, auto-discovered by MediatR's
assembly scan (`ServiceCollectionExtensions.AddMediatRWithBehaviors`,
`src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs:155-167`) — **no explicit
registration needed for a new handler.**

Phase 7 adds notification handlers to this exact pipeline:

```
Service method mutates entity → entity.AddDomainEvent(new XEvent(...))
  → unitOfWork.SaveChangesAsync(ct)   [event row committed as part of the same transaction]
    → UnitOfWork dispatches DomainEventNotification<XEvent> via MediatR
      → XNotificationHandler.Handle(...)
          1. notificationService.CreateAsync(...)   — persists a Notification row (source of truth)
          2. hubContext.Clients.Group("tenant:{id}").SendAsync("ReceiveNotification", dto)  — live push (best-effort)
```

Persist-then-push (D2) means a missed push (client offline, hub restart) is always recoverable
from `GET api/tenant/notifications`. The push is fire-and-forget from the caller's perspective —
if `SendAsync` throws, the handler logs and swallows (mirrors the existing Service Bus
publish-only swallow pattern from Phase 5a, per `aab5b5c` in the recent commit history) rather
than failing the write that already committed.

**Spec/code drift note (rung 1 beats rung 4):** the Phase 1 spec describes a `TenantOwnedEntity`
intermediate base class; the actual code has no such type — every tenant-scoped entity (including
the new `Notification`) declares its own `TenantId` property directly on top of `BaseEntity`. This
plan follows the real code.

## 3. `Notification` entity

`src/FashionSaaS.Domain/Entities/Notification.cs` (new), modelled on `Review.cs` /
`BaseEntity.cs` for shape and on `Order.cs` for the tenant-scoping convention:

| Property | Type | Notes |
|---|---|---|
| `Id`, `CreatedAt`, `UpdatedAt` | inherited from `BaseEntity` | |
| `TenantId` | `Guid?` | nullable — a super-admin/platform-level notification (none in this phase's trigger set, but the column stays nullable for forward compatibility) is `null`; every trigger in this phase sets it. |
| `RecipientUserId` | `Guid?` | `null` = "broadcast to all tenant admins in `tenant:{TenantId}`"; set = "this specific user" (used for the customer-reachable branch of `OrderStatusChanged`). |
| `Type` | `NotificationType` (new enum) | `OrderPlaced`, `OrderStatusChanged`, `PaymentConfirmed`, `LowStock`, `ReviewSubmitted`. |
| `Title` | `string` | short, e.g. `"New order #{OrderNumber}"`. |
| `Message` | `string` | one-line human summary. |
| `EntityName` | `string` | e.g. `"Order"`, `"ProductVariant"`, `"Review"` — deep-link target type. |
| `EntityId` | `Guid` | deep-link target id. |
| `IsRead` | `bool` | default `false`. |
| `ReadAt` | `DateTime?` | set on mark-read. |

Indexes (per `docs/CONVENTIONS.md` §4 — index read-heavy entities per actual query patterns):
composite `(TenantId, RecipientUserId, IsRead, CreatedAt DESC)` for the paged/unread-count query,
mirrors how `StockAdjustment`/`AuditLog` are queried elsewhere.

Tenant scoping: a global query filter on `Notification` in `ApplicationDbContext.OnModelCreating`,
identical pattern to every other tenant entity (`ApplicationDbContext.cs`, e.g.
`modelBuilder.Entity<Order>().HasQueryFilter(o => o.TenantId == currentTenantService.TenantId);`)
— `Notification` uses `n => n.TenantId == null || n.TenantId == currentTenantService.TenantId`
since `TenantId` is nullable here (unlike `Order`/`Product`, which are never null).

## 4. Hub design + group strategy (D1)

`src/FashionSaaS.API/Hubs/NotificationsHub.cs` (new) — lives in `FashionSaaS.API` per D1;
`Microsoft.AspNetCore.SignalR` is part of the ASP.NET Core shared framework, so **no new NuGet
package** is needed server-side (confirmed: it ships in `Microsoft.AspNetCore.App`, not a
separate package reference).

```csharp
[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        string? tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        string? userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }
}
```

Group membership is derived **only from server-verified JWT claims** on the authenticated
`Context.User` — the client never supplies a tenant/user id to join a group (matches D1
verbatim: "never client-supplied"). This reuses the exact claim names already emitted by
`JwtService` (`src/FashionSaaS.Infrastructure/Services/JwtService.cs:35`, `"tenant_id"`) and
already read the same way by `TenantResolutionMiddleware.cs:40` and every controller's
`ClaimTypes.NameIdentifier` accessor (e.g. `InventoryController.cs:17`) — no new claim types
introduced.

Push contract: handlers call
`hubContext.Clients.Group($"tenant:{tenantId}").SendAsync("ReceiveNotification", dto, ct)` (or
`Clients.Group($"user:{userId}")` for the customer-reachable branch), where `dto` is a
`NotificationResponse` shaped identically to the REST list response (single DTO, one
Angular-side model, no drift between push payload and REST payload).

## 5. Trigger matrix

| Trigger | Domain event | Existing or new? | Where raised | Recipients | Notification.Type |
|---|---|---|---|---|---|
| Order placed | `OrderPlacedEvent` | **New** | `OrderService.CreateAsync` (`src/FashionSaaS.Application/Orders/OrderService.cs:99-153`) — currently raises no events at all | tenant admins (`tenant:{id}`) | `OrderPlaced` |
| Order status changed | `OrderStatusChangedEvent` | **New** | `OrderService.TransitionAsync` (`OrderService.cs:227-250`, used by `ConfirmAsync`/`ShipAsync`/`DeliverAsync`) and `CancelAsync` (`OrderService.cs:252-302`) | tenant admins + the customer (`user:{CustomerId}`) if reachable | `OrderStatusChanged` |
| Payment confirmed (subscription) | **RESOLVED** — `PaymentConfirmed` is subscription-billing-scoped, not order-scoped. This phase attaches a new handler to the **existing** `PaymentConfirmedEvent` (`src/FashionSaaS.Domain/Events/PaymentConfirmedEvent.cs`, `(Guid TenantId, string TenantEmail, decimal Amount)`) rather than inventing an order-scoped event. The event carries no payment/order id, so the handler's `Notification.EntityId` uses `TenantId` with `EntityName = "TenantSubscription"`. | **Existing, already raised, currently zero consumers** | `SubscriptionService.ConfirmPaymentAsync` (`src/FashionSaaS.Application/Subscriptions/SubscriptionService.cs:187`) when SuperAdmin confirms a subscription payment | tenant admin (`tenant:{id}`) | `PaymentConfirmed` |
| Low stock | `LowStockEvent` | **Existing, already raised, currently zero consumers** | `InventoryService.AdjustStockAsync` (`src/FashionSaaS.Application/Inventory/InventoryService.cs:62-67`), threshold = `InventoryService.LowStockThreshold` (`= 5`, line 28) | tenant admins (`tenant:{id}`) | `LowStock` |
| Review submitted | `ReviewSubmittedEvent` | **New — and the write path itself is new. RESOLVED: in scope for this phase.** No customer-facing review-submission endpoint exists anywhere in the codebase today (`ReviewService.cs` has only `GetAllAsync`/`GetByIdAsync`/`ApproveAsync`/`RejectAsync`/`DeleteAsync`; no `CreateAsync`; no `StoreReviewsController` — confirmed by repo-wide search for `CreateReview`/`SubmitReview`). This phase builds `ReviewService.SubmitAsync` + a new `StoreReviewsController` (`POST api/store/reviews`, mirroring `StoreOrdersController`'s `[Authorize(Roles = "Customer")]` pattern) + a FluentValidation validator, creating the review `Pending` and raising `ReviewSubmittedEvent` (see plan Group E and OPEN QUESTIONS §2). | tenant admins (`tenant:{id}`) | `ReviewSubmitted` |

`ReviewModeratedEvent` (existing, raised by `ReviewService.ApproveAsync`/`RejectAsync`,
`ReviewService.cs:44,71`) also currently has zero consumers but is **not** in D3's trigger list —
left unwired, out of scope, noted for a future phase.

## 6. Auth (D4)

`src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs`'s `AddJwtAuthentication`
(lines 69-94) configures `TokenValidationParameters` but has **no `JwtBearerEvents`** today
(confirmed: zero matches for `JwtBearerEvents`/`OnMessageReceived` repo-wide). SignalR's
browser client cannot attach an `Authorization` header to the WebSocket upgrade request, so the
standard ASP.NET Core pattern — reading the token from the `access_token` query string, scoped
to the hub path only — is added:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
            context.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

This is documented, official ASP.NET Core SignalR guidance (query via Microsoft Learn MCP at
build time per this repo's CLAUDE.md — .NET 10 postdates training data). The `StartsWithSegments`
guard means every other JWT-protected route is unaffected — the query-string fallback only
applies on the hub path.

Role distinction (D4): tenant-admin vs customer connections are the same hub; recipient targeting
happens entirely via group membership (`tenant:{id}` vs `user:{id}`), not via separate hubs or
role checks inside `OnConnectedAsync` — a customer only ever receives what's pushed to their own
`user:{id}` group (order-status), never `tenant:{id}` broadcasts, because nothing in this phase's
trigger set pushes tenant-admin notifications to a customer's user-group.

## 7. REST endpoints (D6)

Under the existing `api/tenant/...` controller convention (`InventoryController.cs` /
`ReviewsController.cs` pattern: `[ApiController]`, `[Authorize(Roles = "...")]`,
`[EnableRateLimiting("AuthenticatedPolicy")]`, `ResponseData<T>` returned via
`StatusCode(response.StatusCode, response)`), route constants added to
`src/FashionSaaS.API/Constants/ApiUrl.cs` (`TenantInventory` pattern, lines 162-167):

| Method | Route | Response | Notes |
|---|---|---|---|
| `GET` | `api/tenant/notifications` | `ResponseData<PagedResult<NotificationResponse>>` | tenant + (recipient-or-broadcast) filtered, newest first |
| `GET` | `api/tenant/notifications/unread-count` | `ResponseData<int>` | |
| `PUT` | `api/tenant/notifications/{id}/mark-read` | `ResponseData<bool>` | |
| `PUT` | `api/tenant/notifications/mark-all-read` | `ResponseData<bool>` | |

Tenant scoping happens automatically via the global query filter (§3) — no manual `tenantId`
filtering needed in the service layer beyond what EF already applies, matching every other
tenant-scoped repository in this codebase.

## 8. Angular integration (D5)

- **New dependency:** `@microsoft/signalr` (npm) — the one pre-approved new third-party package
  (D5); Angular is `^21.1.0` (confirmed, `fashionsaas-storefront/package.json`), no built-in
  SignalR client exists, and Microsoft ships no framework-native alternative.
- **Admin bell:** `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.html`
  currently renders a Bootstrap-icon topbar with only "Back to store" and "Logout"
  (`admin-layout.component.html:35-42`, `bi bi-*` icon classes, Bootstrap 5 — not Material). A
  bell icon (`bi bi-bell`) + unread-count badge is added between them; a dropdown lists recent
  notifications (calls the REST list), and live arrivals both increment the badge and push a
  toast — this reuses the existing `ToastContainerComponent` already imported into
  `AdminLayoutComponent` (`admin-layout.component.ts` imports list).
- **Notification feature module:** follows the existing lazy-route convention
  (`admin.routes.ts`, `loadChildren`/`loadComponent` per feature) — but the bell/dropdown are
  header-level UI, not a routed page, so they live as a standalone component instantiated
  directly in `AdminLayoutComponent`, with a `NotificationsAdminService` (HTTP) and a
  `NotificationHubService` (SignalR connection lifecycle) under
  `fashionsaas-storefront/src/app/admin/notifications/`, mirroring the
  `InventoryAdminService`/`ApiService` wrapper pattern (`inventory-admin.service.ts`: injects
  `ApiService`, maps `ApiResponse<T>.data` out of the envelope via RxJS `map`).
- **Hub connection:** built with `@microsoft/signalr`'s `HubConnectionBuilder`, URL from
  `environment.apiBaseUrl` (same env token the `ApiService` already uses) plus
  `/hubs/notifications`, `.withUrl(url, { accessTokenFactory: () => authService's current token })`
  — no query-string wiring needed client-side; the SignalR client attaches
  `?access_token=...` automatically when `accessTokenFactory` is supplied, which is what the
  server-side `OnMessageReceived` handler (§6) expects.
- **Customer-side (minimal, D5):** a single toast subscription on the storefront root — when the
  customer's own `user:{id}` group receives an `OrderStatusChanged` push, show one toast
  ("Your order #1234 is now Shipped"); no bell, no history list, no read/unread state
  client-side (their history still exists via the same REST endpoints/table server-side, just
  not surfaced in this phase's customer UI, per the scope boundary in §1).

## 9. Out of scope

- Azure Service Bus consumption of any kind (D7) — the try-on service's publish-only Service Bus
  integration from Phase 5a is untouched.
- Notification preferences/mute/digest settings.
- Customer-facing notification bell/history UI (toast only).
- Wiring `ReviewModeratedEvent` (existing, unconsumed) into notifications — not in D3's trigger
  list; left for a future phase.
- Any hub other than `NotificationsHub` (no separate customer hub, no admin-only hub).
- API versioning changes (none exist in this codebase today — flat `api/tenant/...` routes).
