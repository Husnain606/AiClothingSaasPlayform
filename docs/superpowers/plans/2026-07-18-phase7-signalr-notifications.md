# Phase 7 — SignalR Real-Time + Notifications (Buildable Plan)

> **STATUS — DESIGN, not yet built (2026-07-18).** Baseline verified this session via
> `dotnet test` (no code changes made yet): **446 passed / 0 failed / 0 skipped**
> (24 Domain + 334 Application + 88 Infrastructure). No `.cs` or `.ts` files touched. Run via
> `/run-impl-loop docs/superpowers/plans/2026-07-18-phase7-signalr-notifications.md` per this
> repo's workflow.

## Reference

- Design spec: [`docs/superpowers/specs/2026-07-18-phase7-signalr-notifications-design.md`](../specs/2026-07-18-phase7-signalr-notifications-design.md) — read first; this plan implements it symbol-by-symbol.
- Architecture ground truth: `docs/superpowers/specs/2026-06-18-phase1-core-saas-backend-design.md` §6 (layering, MediatR-after-write convention, `ResponseData<T>`), §10 (middleware order).
- Plan format: `docs/projectStandards/implementation-plan-format.md`.

## 1. Locked decisions (do not revisit without sign-off)

| # | Decision | Resolution |
|---|---|---|
| D1 | Hub location & group strategy | **`NotificationsHub` lives in `FashionSaaS.API` (in-framework, no new server NuGet). Groups `tenant:{tenantId}` and `user:{userId}` joined server-side from JWT claims on connect — never client-supplied.** |
| D2 | Persist-then-push | **`Notification` entity persisted first (REST-recoverable); SignalR push is best-effort second. A missed push never loses a notification.** |
| D3 | Trigger set | **OrderPlaced (new event), OrderStatusChanged (new event), PaymentConfirmed (subscription-billing-scoped — attaches a new handler to the EXISTING `PaymentConfirmedEvent`, resolved per §OPEN QUESTIONS 1), LowStock (existing event, first consumer), ReviewSubmitted (new event + new write path, resolved per §OPEN QUESTIONS 2), all riding the existing MediatR-after-write pipeline. Only 3 domain events are new: `OrderPlacedEvent`, `OrderStatusChangedEvent`, `ReviewSubmittedEvent`.** |
| D4 | Hub auth | **JWT bearer via `access_token` query string, `OnMessageReceived` scoped to the hub path only. Same claim names already emitted by `JwtService` (`tenant_id`, `ClaimTypes.NameIdentifier`, `ClaimTypes.Role`) — no new claim types.** |
| D5 | Angular client | **`@microsoft/signalr` — the one new, pre-approved third-party dependency. Admin: bell + badge + dropdown + toast in `AdminLayoutComponent`. Customer: single order-status toast, no history UI.** |
| D6 | REST surface | **`GET api/tenant/notifications` (paged), `GET api/tenant/notifications/unread-count`, `PUT api/tenant/notifications/{id}/mark-read`, `PUT api/tenant/notifications/mark-all-read`. Standard `ResponseData<T>`, tenant-scoped via global query filter.** |
| D7 | Service Bus | **Out of scope — Phase 5a's publish-only try-on integration is untouched.** |
| D8 | Migration | **One EF Core migration for `Notification`, via the existing `dotnet ef migrations add <Name> --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure` workflow.** |
| Scope | **Scope boundary** | **This phase adds: `Notification` entity + 4 REST endpoints + 1 hub + 5 event handlers (3 on new events: OrderPlaced, OrderStatusChanged, ReviewSubmitted; 2 on existing events: PaymentConfirmed, LowStock) + `OrderService`/`ReviewService` event-raising + a minimal customer review-submit endpoint (required to have anything to raise `ReviewSubmitted` from) + Angular bell/toast. It does NOT add notification preferences, a customer notification history UI, Service Bus consumption, or API versioning.** |

## 2. Contract checklist (confirmed against landed code before editing)

- [x] `BaseEntity` (`src/FashionSaaS.Domain/Entities/BaseEntity.cs`) — `Id`, `CreatedAt`, `UpdatedAt`, `AddDomainEvent(IDomainEvent)`, `DomainEvents`, `ClearDomainEvents()`. No `TenantId` on the base — every entity declares its own.
- [x] `UnitOfWork.SaveChangesAsync` (`src/FashionSaaS.Infrastructure/Persistence/UnitOfWork.cs`) collects pending `DomainEvents` pre-save, dispatches `DomainEventNotification<TEvent>` via `IPublisher.Publish` post-save.
- [x] `DomainEventNotification<TDomainEvent>` (`src/FashionSaaS.Infrastructure/Persistence/DomainEventNotification.cs`) — `record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification where TDomainEvent : IDomainEvent`.
- [x] `AddMediatRWithBehaviors` (`src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs:155-167`) scans `Application` + `Infrastructure` assemblies — new handlers in `Infrastructure/EventHandlers/` are auto-discovered, no registration edit needed.
- [x] `LowStockEvent` (`src/FashionSaaS.Domain/Events/LowStockEvent.cs`) — `record LowStockEvent(Guid ProductVariantId, Guid TenantId, int Remaining) : IDomainEvent` — already raised in `InventoryService.AdjustStockAsync` (`src/FashionSaaS.Application/Inventory/InventoryService.cs:62-67`), zero existing consumers.
- [x] `PaymentConfirmedEvent` (`src/FashionSaaS.Domain/Events/PaymentConfirmedEvent.cs`) — `record PaymentConfirmedEvent(Guid TenantId, string TenantEmail, decimal Amount) : IDomainEvent` — subscription-billing scoped (no `OrderId`), raised via `payment.AddDomainEvent(new PaymentConfirmedEvent(tenant.Id, tenant.Email, payment.Amount));` in `SubscriptionService.ConfirmPaymentAsync` (`src/FashionSaaS.Application/Subscriptions/SubscriptionService.cs:187`) when SuperAdmin confirms a subscription payment. **Resolved (§OPEN QUESTIONS 1): this phase reuses this event as-is — attaches a new `INotificationHandler<DomainEventNotification<PaymentConfirmedEvent>>`, no new event, no `OrderService` involvement.** Note the event carries no payment/order id, so its handler's `Notification.EntityId` uses `evt.TenantId` with `EntityName = "TenantSubscription"`.
- [x] `OrderService.CreateAsync` (`src/FashionSaaS.Application/Orders/OrderService.cs:31-153`) raises **no** domain events today; captures payment synchronously (`request.PaymentInfo.CardNumber` → `CardLast4`, line 96-97) with no separate authorize/capture step. **This phase raises only `OrderPlacedEvent` from here — no order-scoped payment event (§OPEN QUESTIONS 1).**
- [x] `OrderService.TransitionAsync` (`OrderService.cs:227-250`) is the shared status-change path for `ConfirmAsync`/`ShipAsync`/`DeliverAsync`; `CancelAsync` (`OrderService.cs:252-302`) is separate but also a status change.
- [x] `ReviewService` (`src/FashionSaaS.Application/Reviews/ReviewService.cs`) has **no** `CreateAsync`/submit method; no `StoreReviewsController` exists (`src/FashionSaaS.API/Controllers/Store/` contains only `StoreOrdersController.cs`). **Resolved (§OPEN QUESTIONS 2): building the minimal submit path is in scope for this phase (Group E).**
- [x] `IReviewRepository` (`src/FashionSaaS.Application/Interfaces/IReviewRepository.cs`) extends `IGenericRepository<Review>` (`src/FashionSaaS.Application/Interfaces/IGenericRepository.cs:11`), which **does** declare `Task AddAsync(T entity)` — **verified: `reviewRepository.AddAsync(review)` in the E5 code sample is valid as written, no correction needed.**
- [x] `StoreOrdersController` (`src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs`) — canonical customer-auth pattern to mirror for `StoreReviewsController`: `[ApiController]`, `[Authorize(Roles = "Customer")]`, `[EnableRateLimiting("AuthenticatedPolicy")]`, `Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);`, plus `Email`/`Ip`/`Ua` helper properties.
- [x] `ApplicationDbContext` (`src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs`) — `ICurrentTenantService` injected via primary constructor; `HasQueryFilter` per tenant entity in `OnModelCreating`; `ApplyConfigurationsFromAssembly` auto-applies `IEntityTypeConfiguration<T>` classes from `Persistence/Configurations/`.
- [x] `AddJwtAuthentication` (`src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs:69-94`) has no `JwtBearerEvents` today — confirmed by repo-wide search.
- [x] JWT claims emitted by `JwtService.GenerateAccessToken` (`src/FashionSaaS.Infrastructure/Services/JwtService.cs:35,47,50`): `"tenant_id"`, `"tenant_slug"`, `ClaimTypes.Role` (per role), plus the framework-standard `ClaimTypes.NameIdentifier` for user id (read the same way everywhere, e.g. `InventoryController.cs:17`).
- [x] `ApiUrl.TenantInventory` (`src/FashionSaaS.API/Constants/ApiUrl.cs:162-167`) — route-constant convention to mirror for `TenantNotifications`.
- [x] Angular: `ApiResponse<T>` (`fashionsaas-storefront/src/app/core/models/api-response.model.ts`) = `{ statusCode, message, data, errors, timestamp }`; `ApiService` (`core/services/api.service.ts`) wraps `HttpClient` against `environment.apiBaseUrl`; `InventoryAdminService` (`admin/inventory/services/inventory-admin.service.ts`) is the canonical admin-service pattern; `AdminLayoutComponent` (`admin/layout/admin-layout.component.ts`/`.html`) is standalone, Bootstrap-5 icons (`bi bi-*`), already imports `ToastContainerComponent`; Angular `^21.1.0`, `@microsoft/signalr` absent from `package.json`.
- [x] Latest migration: `src/FashionSaaS.Infrastructure/Persistence/Migrations/20260703101408_Phase4Orders.cs`. `dotnet ef` workflow: `dotnet ef migrations add <Name> --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure`.

### ASP.NET Core / SignalR API facts (cited)

- `Microsoft.AspNetCore.SignalR` (server) ships as part of the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`) — no separate NuGet `PackageReference` needed for the hub itself. Source: Microsoft Learn (`microsoft_docs_search` at build time — confirm current for .NET 10, since this postdates training data; the shared-framework inclusion has been stable since SignalR's ASP.NET Core integration and is not expected to change).
- The documented pattern for authenticating a SignalR connection when the client can't send an `Authorization` header (WebSocket handshake) is `JwtBearerEvents.OnMessageReceived` reading `context.Request.Query["access_token"]`, guarded by `context.HttpContext.Request.Path.StartsWithSegments(...)` to scope it to hub paths only. **Must be re-verified against current Microsoft Learn docs during implementation** (query `microsoft_docs_search` for "SignalR JWT authentication access_token" before writing `Group C` below) since the exact ASP.NET Core version's API surface postdates this assistant's training cutoff.
- `@microsoft/signalr`'s `HubConnectionBuilder().withUrl(url, { accessTokenFactory })` attaches the token as `?access_token=` on negotiate/WebSocket requests automatically — client code never manually appends the query string.

## 3. Ordered task checklist

Execute top-to-bottom; build after each lettered group.

### Group A — Domain + persistence (Notification entity, new events, migration)

- [ ] **A1** Create `src/FashionSaaS.Domain/Enums/NotificationType.cs` — `enum NotificationType { OrderPlaced, OrderStatusChanged, PaymentConfirmed, LowStock, ReviewSubmitted }`.
- [ ] **A2** Create `src/FashionSaaS.Domain/Entities/Notification.cs` (modelled on `Review.cs` shape + `BaseEntity`).
- [ ] **A3** Create `src/FashionSaaS.Domain/Events/OrderPlacedEvent.cs` — `record OrderPlacedEvent(Guid OrderId, Guid TenantId, string OrderNumber, decimal Total) : IDomainEvent`.
- [ ] **A4** Create `src/FashionSaaS.Domain/Events/OrderStatusChangedEvent.cs` — `record OrderStatusChangedEvent(Guid OrderId, Guid TenantId, Guid CustomerId, string OrderNumber, OrderStatus PreviousStatus, OrderStatus NewStatus) : IDomainEvent`.
- [ ] **A5** Create `src/FashionSaaS.Domain/Events/ReviewSubmittedEvent.cs` — `record ReviewSubmittedEvent(Guid ReviewId, Guid TenantId, Guid ProductId, int Rating) : IDomainEvent`.
- [ ] **A6** Create `src/FashionSaaS.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` (modelled on `ReviewConfiguration.cs`) — composite index on `(TenantId, RecipientUserId, IsRead, CreatedAt)`.
- [ ] **A7** Edit `src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs` — add `public DbSet<Notification> Notifications => Set<Notification>();` and the tenant/nullable query filter in `OnModelCreating`.
- [ ] **A8** Run the probe-migration check (`dotnet ef migrations add _probe --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure` then inspect for unintended drift, then `dotnet ef migrations remove`), then `dotnet ef migrations add AddNotifications --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure`.

> **No `OrderPaymentConfirmedEvent` in this phase** — removed per §OPEN QUESTIONS 1: `PaymentConfirmed` reuses the existing subscription-billing `PaymentConfirmedEvent` (Group D task instead of a Group A event + Group E raise-site).

### Group B — Application layer (NotificationService, repository, DTOs, REST controller)

- [ ] **B1** Create `src/FashionSaaS.Application/Interfaces/INotificationRepository.cs` + `src/FashionSaaS.Infrastructure/Repositories/NotificationRepository.cs` (modelled on `ReviewRepository`).
- [ ] **B2** Create `src/FashionSaaS.Application/Notifications/DTOs/NotificationResponse.cs`, `NotificationFilter.cs`.
- [ ] **B3** Create `src/FashionSaaS.Application/Notifications/NotificationService.cs` — `CreateAsync`, `GetPagedAsync`, `GetUnreadCountAsync`, `MarkReadAsync`, `MarkAllReadAsync`. Reused by both REST controller and every new event handler (Group D) for persistence — the one place `Notification` rows get written.
- [ ] **B4** Edit `src/FashionSaaS.API/Constants/ApiUrl.cs` — add `TenantNotifications` nested class (mirrors `TenantInventory`, lines 162-167).
- [ ] **B5** Create `src/FashionSaaS.API/Controllers/Tenant/NotificationsController.cs` (modelled on `InventoryController.cs`).

### Group C — Hub + JWT-over-WebSocket auth wiring

- [ ] **C1** Query `microsoft_docs_search`/`microsoft_docs_fetch` for current ASP.NET Core (.NET 10) SignalR + JWT bearer query-string auth guidance; confirm the `OnMessageReceived` shape and hub-path constant before writing C2/C3.
- [ ] **C2** Create `src/FashionSaaS.API/Hubs/NotificationsHub.cs` (design spec §4).
- [ ] **C3** Edit `AddJwtAuthentication` (`ServiceCollectionExtensions.cs:69-94`) — add `options.Events = new JwtBearerEvents { OnMessageReceived = ... }` per design spec §6.
- [ ] **C4** Edit `src/FashionSaaS.API/Program.cs` — add `builder.Services.AddSignalR();` alongside the other `AddXxx` calls (~line 43), and `app.MapHub<NotificationsHub>("/hubs/notifications");` after `app.MapControllers()` (~line 160).

### Group D — MediatR event handlers (persist + push)

- [ ] **D1** Create `src/FashionSaaS.Infrastructure/EventHandlers/OrderPlacedNotificationHandler.cs` — `INotificationHandler<DomainEventNotification<OrderPlacedEvent>>`, injects `NotificationService`, `IHubContext<NotificationsHub>`, `ILogger<>`; broadcasts to `tenant:{TenantId}`.
- [ ] **D2** Create `src/FashionSaaS.Infrastructure/EventHandlers/OrderStatusChangedNotificationHandler.cs` — pushes to `tenant:{TenantId}` **and** `user:{CustomerId}`.
- [ ] **D3** Create `src/FashionSaaS.Infrastructure/EventHandlers/PaymentConfirmedNotificationHandler.cs` — `INotificationHandler<DomainEventNotification<PaymentConfirmedEvent>>` attached to the **existing** subscription-billing event (raised in `SubscriptionService.ConfirmPaymentAsync`); pushes to `tenant:{TenantId}`. No `OrderService` edit involved — this event already fires today.
- [ ] **D4** Create `src/FashionSaaS.Infrastructure/EventHandlers/LowStockNotificationHandler.cs` — first consumer of the existing `LowStockEvent`; pushes to `tenant:{TenantId}`.
- [ ] **D5** Create `src/FashionSaaS.Infrastructure/EventHandlers/ReviewSubmittedNotificationHandler.cs` — pushes to `tenant:{TenantId}`.

### Group E — OrderService / ReviewService event-raising + minimal review-submit endpoint

- [ ] **E1** Edit `OrderService.CreateAsync` (`OrderService.cs:99-153`) — after `order` is constructed and before `orderRepository.AddAsync(order)`, add `order.AddDomainEvent(new OrderPlacedEvent(order.Id, tenantId, order.OrderNumber, order.Total));`. **No payment event raised here** — the order flow's notifications are `OrderPlacedEvent` and `OrderStatusChangedEvent` only (§OPEN QUESTIONS 1).
- [ ] **E2** Edit `OrderService.TransitionAsync` (`OrderService.cs:227-250`) — after `beforeSave?.Invoke(order);` and before `SaveChangesAsync`, add `order.AddDomainEvent(new OrderStatusChangedEvent(order.Id, order.TenantId, order.CustomerId, order.OrderNumber, previousStatus, target));`.
- [ ] **E3** Edit `OrderService.CancelAsync` (`OrderService.cs:252-302`) — same `AddDomainEvent(new OrderStatusChangedEvent(...))` call before `SaveChangesAsync`, `previousStatus` → `OrderStatus.Cancelled`.
- [ ] **E4** Create `src/FashionSaaS.Application/Reviews/DTOs/SubmitReviewRequest.cs` + `SubmitReviewRequestValidator.cs` (FluentValidation, per CONVENTIONS §8).
- [ ] **E5** Edit `ReviewService.cs` — add `SubmitAsync(Guid productId, Guid customerId, SubmitReviewRequest request, ...)`: creates a `Review` with `Status = ReviewStatus.Pending`, calls `review.AddDomainEvent(new ReviewSubmittedEvent(review.Id, tenantId, productId, request.Rating));` before `SaveChangesAsync`.
- [ ] **E6** Create `src/FashionSaaS.API/Controllers/Store/StoreReviewsController.cs` (modelled on `StoreOrdersController.cs`) — single `POST api/store/reviews` action calling `ReviewService.SubmitAsync`.

### Group F — Angular (SignalR client, admin bell, customer toast)

- [ ] **F1** `npm install @microsoft/signalr` in `fashionsaas-storefront` (D5 — pre-approved).
- [ ] **F2** Create `fashionsaas-storefront/src/app/admin/notifications/models/notification.model.ts` (mirrors `NotificationResponse`).
- [ ] **F3** Create `fashionsaas-storefront/src/app/admin/notifications/services/notifications-admin.service.ts` (modelled on `inventory-admin.service.ts`) — `getPaged`, `getUnreadCount`, `markRead`, `markAllRead`.
- [ ] **F4** Create `fashionsaas-storefront/src/app/core/services/notification-hub.service.ts` — wraps `HubConnectionBuilder`, exposes an `Observable<NotificationDto>` for `ReceiveNotification` events; used by both admin bell and customer toast.
- [ ] **F5** Create `fashionsaas-storefront/src/app/admin/notifications/notification-bell/notification-bell.component.ts` (+ `.html`/`.scss`) — bell icon, unread badge, dropdown list, mark-read-on-open, toast on live arrival.
- [ ] **F6** Edit `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.html` (lines 35-42) — insert `<app-notification-bell>` between "Back to store" and "Logout".
- [ ] **F7** Edit `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.ts` — import/register `NotificationBellComponent`.
- [ ] **F8** Wire a minimal customer-side subscription (storefront root component or a small `CustomerOrderToastService`) — subscribes to `NotificationHubService`, filters `Type === 'OrderStatusChanged'`, shows one toast via the existing `ToastContainerComponent`/toast service.

### Group G — Validate

- [ ] **G1** `dotnet build` — zero warnings (warnings = errors).
- [ ] **G1b** Serena **`get_diagnostics_for_file`** (`min_severity: 2`) on every changed/created `.cs` file — clean.
- [ ] **G2** testing-expert writes the §5 exact test list.
- [ ] **G3** `dotnet test` — green, report exact `passed/failed/skipped` counts (baseline 446 + new tests below).
- [ ] **G4** `ng test --watch=false` (per `package.json` `test:ci` script, Vitest) — green for new Angular specs.
- [ ] **G5** Manual smoke: connect two hub clients (simulated tenant admin + simulated customer) via a REST-triggered write (e.g. `POST` an order), confirm both REST list and push arrive, confirm mark-read persists and unread-count updates.

## 4. Code samples — files to create / modify

### A2 — `src/FashionSaaS.Domain/Entities/Notification.cs`
`E:\AIcLOTHING\src\FashionSaaS.Domain\Entities\Notification.cs` (new; modelled on `Review.cs` for shape, `BaseEntity.cs` for inheritance).
```csharp
using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid? TenantId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
```

### A3-A5 — new domain events
`E:\AIcLOTHING\src\FashionSaaS.Domain\Events\OrderPlacedEvent.cs` (modelled on `LowStockEvent.cs`).
```csharp
namespace FashionSaaS.Domain.Events;

public record OrderPlacedEvent(Guid OrderId, Guid TenantId, string OrderNumber, decimal Total) : IDomainEvent;
```
`E:\AIcLOTHING\src\FashionSaaS.Domain\Events\OrderStatusChangedEvent.cs`
```csharp
using FashionSaaS.Domain.Enums;

namespace FashionSaaS.Domain.Events;

public record OrderStatusChangedEvent(
    Guid OrderId, Guid TenantId, Guid CustomerId, string OrderNumber,
    OrderStatus PreviousStatus, OrderStatus NewStatus) : IDomainEvent;
```
`E:\AIcLOTHING\src\FashionSaaS.Domain\Events\ReviewSubmittedEvent.cs`
```csharp
namespace FashionSaaS.Domain.Events;

public record ReviewSubmittedEvent(Guid ReviewId, Guid TenantId, Guid ProductId, int Rating) : IDomainEvent;
```

### A8 — `ApplicationDbContext.cs` edit
`E:\AIcLOTHING\src\FashionSaaS.Infrastructure\Persistence\ApplicationDbContext.cs` (modelled on the existing `Order`/`Product` filter lines).
```csharp
public DbSet<Notification> Notifications => Set<Notification>();

// in OnModelCreating, alongside the Order/Product filters:
modelBuilder.Entity<Notification>()
    .HasQueryFilter(n => n.TenantId == null || n.TenantId == currentTenantService.TenantId);
```

### B3 — `NotificationService.cs`
`E:\AIcLOTHING\src\FashionSaaS.Application\Notifications\NotificationService.cs` (new; modelled on `ReviewService.cs` for the `ResponseData<T>`/tenant-guard/audit shape — note `CreateAsync` is called from event handlers, which run *after* the triggering write already committed, so it does its own `SaveChangesAsync` rather than composing into the caller's transaction).
```csharp
using FashionSaaS.Application.Common;
using FashionSaaS.Application.Interfaces;
using FashionSaaS.Application.Notifications.DTOs;
using FashionSaaS.Domain.Entities;
using FashionSaaS.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Application.Notifications;

public class NotificationService(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenantService currentTenant,
    ILogger<NotificationService> logger)
{
    public async Task<Notification> CreateAsync(Guid? tenantId, Guid? recipientUserId, NotificationType type,
        string title, string message, string entityName, Guid entityId, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            EntityName = entityName,
            EntityId = entityId
        };

        await notificationRepository.AddAsync(notification);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Notification {Type} created for tenant {TenantId}", type, tenantId);
        return notification;
    }

    public async Task<ResponseData<PagedResult<NotificationResponse>>> GetPagedAsync(NotificationFilter filter,
        CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<PagedResult<NotificationResponse>>.Failure("Tenant could not be resolved.", 400);

        filter.TenantId = tenantId;
        (IReadOnlyList<Notification>? items, var total) = await notificationRepository.GetPagedAsync(filter, ct);

        var page = new PagedResult<NotificationResponse>
        {
            Items = items.Select(MapToResponse).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return ResponseData<PagedResult<NotificationResponse>>.Success(page);
    }

    public async Task<ResponseData<int>> GetUnreadCountAsync(Guid recipientUserId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<int>.Failure("Tenant could not be resolved.", 400);

        var count = await notificationRepository.GetUnreadCountAsync(tenantId, recipientUserId, ct);
        return ResponseData<int>.Success(count);
    }

    public async Task<ResponseData<bool>> MarkReadAsync(Guid id, Guid recipientUserId, CancellationToken ct = default)
    {
        Notification? notification = await notificationRepository.GetByIdAsync(id);
        if (notification is null)
            return ResponseData<bool>.Failure("Notification not found.", 404);

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await notificationRepository.UpdateAsync(notification);
        await unitOfWork.SaveChangesAsync(ct);

        return ResponseData<bool>.Success(true, "Notification marked read.");
    }

    public async Task<ResponseData<bool>> MarkAllReadAsync(Guid recipientUserId, CancellationToken ct = default)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ResponseData<bool>.Failure("Tenant could not be resolved.", 400);

        await notificationRepository.MarkAllReadAsync(tenantId, recipientUserId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return ResponseData<bool>.Success(true, "All notifications marked read.");
    }

    private static NotificationResponse MapToResponse(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Message = n.Message,
        EntityName = n.EntityName,
        EntityId = n.EntityId,
        IsRead = n.IsRead,
        CreatedAt = n.CreatedAt
    };
}
```

### C2 — `NotificationsHub.cs`
`E:\AIcLOTHING\src\FashionSaaS.API\Hubs\NotificationsHub.cs` (new).
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FashionSaaS.API.Hubs;

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

### C3 — `ServiceCollectionExtensions.cs` edit (JWT-over-query-string for the hub path)
`E:\AIcLOTHING\src\FashionSaaS.API\Extensions\ServiceCollectionExtensions.cs` (inside `AddJwtAuthentication`, `.AddJwtBearer(options => { ... })`, after `options.TokenValidationParameters = ...;`, lines 74-89 — modelled on the documented ASP.NET Core SignalR pattern, **re-confirm exact shape against Microsoft Learn MCP per task C1** before landing).
```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        PathString path = context.HttpContext.Request.Path;
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
            context.Token = accessToken;
        return Task.CompletedTask;
    }
};
```

### C4 — `Program.cs` edits
`E:\AIcLOTHING\src\FashionSaaS.API\Program.cs` — add near the other `builder.Services.AddXxx` calls (~line 43):
```csharp
builder.Services.AddSignalR();
```
Add after `app.MapControllers();` (~line 160):
```csharp
app.MapHub<NotificationsHub>("/hubs/notifications");
```

### D1 — `OrderPlacedNotificationHandler.cs`
`E:\AIcLOTHING\src\FashionSaaS.Infrastructure\EventHandlers\OrderPlacedNotificationHandler.cs` (modelled on `SuperAdminLoginFromNewIpEventHandler.cs`).
```csharp
using FashionSaaS.API.Hubs;
using FashionSaaS.Application.Notifications;
using FashionSaaS.Domain.Enums;
using FashionSaaS.Domain.Events;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FashionSaaS.Infrastructure.EventHandlers;

public class OrderPlacedNotificationHandler(
    NotificationService notificationService,
    IHubContext<NotificationsHub> hubContext,
    ILogger<OrderPlacedNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderPlacedEvent>>
{
    public async Task Handle(DomainEventNotification<OrderPlacedEvent> notification, CancellationToken cancellationToken)
    {
        OrderPlacedEvent evt = notification.DomainEvent;
        var title = $"New order {evt.OrderNumber}";
        var message = $"Order {evt.OrderNumber} placed for {evt.Total:C}.";

        Domain.Entities.Notification saved = await notificationService.CreateAsync(
            evt.TenantId, recipientUserId: null, NotificationType.OrderPlaced,
            title, message, "Order", evt.OrderId, cancellationToken);

        try
        {
            await hubContext.Clients.Group($"tenant:{evt.TenantId}")
                .SendAsync("ReceiveNotification", saved, cancellationToken);
        }
        catch (Exception ex)
        {
            // Persisted row already committed above — a failed live push is recoverable via
            // GET api/tenant/notifications (D2). Swallow and log, matching the Phase 5a
            // Service Bus publish-only swallow pattern.
            logger.LogWarning(ex, "Failed to push OrderPlaced live notification for order {OrderId}", evt.OrderId);
        }
    }
}
```
`OrderStatusChangedNotificationHandler.cs`, `LowStockNotificationHandler.cs`, `ReviewSubmittedNotificationHandler.cs` follow the identical shape (persist via `NotificationService.CreateAsync`, push via `IHubContext<NotificationsHub>` in a try/catch, log-and-swallow on push failure) — `OrderStatusChangedNotificationHandler` additionally pushes to `Clients.Group($"user:{evt.CustomerId}")` for the customer-reachable branch.

`PaymentConfirmedNotificationHandler.cs` is `INotificationHandler<DomainEventNotification<PaymentConfirmedEvent>>` — attached to the **existing** `PaymentConfirmedEvent(Guid TenantId, string TenantEmail, decimal Amount)` (`src/FashionSaaS.Domain/Events/PaymentConfirmedEvent.cs`), raised today by `SubscriptionService.ConfirmPaymentAsync` (`SubscriptionService.cs:187`). Since the event carries no payment/order id, the handler calls `notificationService.CreateAsync(evt.TenantId, recipientUserId: null, NotificationType.PaymentConfirmed, title, message, "TenantSubscription", evt.TenantId, ct)` (using `TenantId` as `EntityId` — there is no better id available on this event) and pushes to `tenant:{evt.TenantId}` only, same try/catch-and-log shape as `OrderPlacedNotificationHandler`.

### E1 — `OrderService.CreateAsync` edit
`E:\AIcLOTHING\src\FashionSaaS.Application\Orders\OrderService.cs` — insert immediately before `await orderRepository.AddAsync(order);` (line 145):
```csharp
order.AddDomainEvent(new OrderPlacedEvent(order.Id, tenantId, order.OrderNumber, order.Total));

await orderRepository.AddAsync(order);
```
(See OPEN QUESTIONS §1 — `PaymentConfirmed` is not raised from `OrderService` at all; it rides the existing subscription-billing `PaymentConfirmedEvent` instead.)

### E2 — `OrderService.TransitionAsync` edit
`E:\AIcLOTHING\src\FashionSaaS.Application\Orders\OrderService.cs` — insert after `beforeSave?.Invoke(order);` (line 240), before `await unitOfWork.SaveChangesAsync(ct);`:
```csharp
order.AddDomainEvent(new OrderStatusChangedEvent(
    order.Id, order.TenantId, order.CustomerId, order.OrderNumber, previousStatus, target));
```

### E3 — `OrderService.CancelAsync` edit
Same file — insert after the stock-restore loop (line 292), before `await unitOfWork.SaveChangesAsync(ct);` (line 294):
```csharp
order.AddDomainEvent(new OrderStatusChangedEvent(
    order.Id, order.TenantId, order.CustomerId, order.OrderNumber, previousStatus, OrderStatus.Cancelled));
```

### E5 — `ReviewService.SubmitAsync` (new method)
`E:\AIcLOTHING\src\FashionSaaS.Application\Reviews\ReviewService.cs` — modelled on `ApproveAsync`'s tenant-guard/audit shape, added as a new method:
```csharp
public async Task<ResponseData<ReviewResponse>> SubmitAsync(Guid productId, Guid customerId,
    SubmitReviewRequest request, string ipAddress, string userAgent, CancellationToken ct = default)
{
    if (currentTenant.TenantId is not { } tenantId)
        return ResponseData<ReviewResponse>.Failure("Tenant could not be resolved.", 400);

    var review = new Review
    {
        TenantId = tenantId,
        ProductId = productId,
        CustomerId = customerId,
        Rating = request.Rating,
        Title = request.Title,
        Body = request.Body,
        Status = ReviewStatus.Pending
    };
    review.AddDomainEvent(new ReviewSubmittedEvent(review.Id, tenantId, productId, request.Rating));

    await reviewRepository.AddAsync(review);
    await unitOfWork.SaveChangesAsync(ct);

    await auditLogService.LogAsync(customerId, tenantId, "ReviewSubmitted", "Review", review.Id,
        null, new { review.ProductId, review.Rating }, ipAddress, userAgent);

    logger.LogInformation("Review {ReviewId} submitted for product {ProductId}", review.Id, productId);
    return ResponseData<ReviewResponse>.Success(MapToResponse(review), "Review submitted.", 201);
}
```
Requires `reviewRepository.AddAsync` — **confirmed**: `IReviewRepository` extends `IGenericRepository<Review>` (`IGenericRepository.cs:11`), which declares `Task AddAsync(T entity)`. The call above is valid as written.

## 5. Exact test list (testing-expert)

Paradigm: matches the existing suite — EF Core in-memory provider for repository/service tests, NSubstitute for mocked collaborators (`IHubContext<NotificationsHub>`, `IPublisher`), xUnit `[Fact]`/`[Theory]`. No coverage/CRAP gate (none exists today).

### Domain tests (`tests/FashionSaaS.Domain.Tests`)
- **`Notification_DefaultsIsReadFalse`** — a newly constructed `Notification` has `IsRead == false` and `ReadAt == null`.
- **`Order_CreateAsync_something...`** — N/A here; Domain project has no service-level tests (matches existing split: Domain tests cover entities/value objects only, per the 24-test baseline).

### Application tests (`tests/FashionSaaS.Application.Tests`)
- **`NotificationService_CreateAsync_PersistsNotificationRow`** — asserts a `Notification` row exists after `CreateAsync`.
- **`NotificationService_GetPagedAsync_FiltersByTenant`** — cross-tenant notifications never returned.
- **`NotificationService_GetPagedAsync_FiltersByRecipientOrBroadcast`** — a notification with `RecipientUserId = null` appears for every tenant admin; one with a specific `RecipientUserId` appears only for that user.
- **`NotificationService_GetUnreadCountAsync_CountsOnlyUnread`**.
- **`NotificationService_MarkReadAsync_SetsIsReadAndReadAt`**.
- **`NotificationService_MarkReadAsync_NotFound_ReturnsFailure404`**.
- **`NotificationService_MarkAllReadAsync_MarksAllForRecipient`**.
- **`OrderService_CreateAsync_RaisesOrderPlacedEvent`** — asserts `order.DomainEvents` contains `OrderPlacedEvent` after `CreateAsync`, before `ClearDomainEvents` (or asserts via a captured `IPublisher.Publish` call if events are cleared post-dispatch — confirm which pattern existing `ReviewService` tests use and mirror it). No `PaymentConfirmed`-related event is raised from `OrderService` (§OPEN QUESTIONS 1).
- **`OrderService_ConfirmAsync_RaisesOrderStatusChangedEvent_PendingToConfirmed`**.
- **`OrderService_ShipAsync_RaisesOrderStatusChangedEvent_ConfirmedToShipped`**.
- **`OrderService_DeliverAsync_RaisesOrderStatusChangedEvent_ShippedToDelivered`**.
- **`OrderService_CancelAsync_RaisesOrderStatusChangedEvent_ToCancelled`**.
- **`ReviewService_SubmitAsync_CreatesPendingReviewAndRaisesReviewSubmittedEvent`**.
- **`ReviewService_SubmitAsync_TenantUnresolved_ReturnsFailure400`**.
- **`SubmitReviewRequestValidator_RejectsRatingOutOfRange`** (FluentValidation, per CONVENTIONS §8 — mirrors `RejectReviewRequestValidator` if one exists; confirm at build time).

### Infrastructure tests (`tests/FashionSaaS.Infrastructure.Tests`)
- **`OrderPlacedNotificationHandler_Handle_CreatesNotificationAndPushesToTenantGroup`** — mocked `IHubContext<NotificationsHub>`, asserts `Clients.Group("tenant:{id}")` `SendAsync("ReceiveNotification", ...)` called once.
- **`OrderPlacedNotificationHandler_Handle_HubPushThrows_SwallowsAndLogsWarning`** — asserts the handler does not rethrow when `SendAsync` throws (persist-then-push resilience, D2).
- **`OrderStatusChangedNotificationHandler_Handle_PushesToTenantAndCustomerGroups`** — asserts both `Group("tenant:{id}")` and `Group("user:{customerId}")` receive the push.
- **`PaymentConfirmedNotificationHandler_Handle_CreatesNotificationAndPushesToTenantGroup`** — attached to the existing `PaymentConfirmedEvent`; test publishes `DomainEventNotification<PaymentConfirmedEvent>` directly (no `OrderService`/`SubscriptionService` involvement needed to exercise the handler).
- **`LowStockNotificationHandler_Handle_CreatesNotificationAndPushesToTenantGroup`** — first-ever test exercising `LowStockEvent` dispatch end-to-end (previously unconsumed).
- **`ReviewSubmittedNotificationHandler_Handle_CreatesNotificationAndPushesToTenantGroup`**.
- **`ApplicationDbContext_Notification_QueryFilter_ScopesToTenantOrBroadcast`** — mirrors the existing `Order`/`Product` query-filter test pattern (confirm exact existing test name to mirror, e.g. via `find_symbol` on `OrderQueryFilterTests`-style classes, before authoring).

> **Known coverage gap:** no test exercises the live SignalR wire protocol end-to-end (real WebSocket handshake, real JWT-over-query-string negotiation) — that is covered only by the manual smoke step (G5), since this repo has no existing SignalR/WebSocket integration-test harness to extend. Flag to Dan whether a `TestServer`-based hub integration test is wanted in a follow-up.

**Expected count:** 446 (baseline) + ~7 (Domain-adjacent/Application new) + ~8 (Application handler-adjacent) + ~7 (Infrastructure) — **testing-expert reports the exact final number; do not pre-commit to an estimate as fact.**

## 6. Angular test list (Vitest, `ng test`)

- **`NotificationHubService — connects with accessTokenFactory and joins via server-derived groups (no client-supplied ids)`**.
- **`NotificationHubService — reconnects and re-subscribes ReceiveNotification handler`**.
- **`NotificationsAdminService — getPaged unwraps ApiResponse.data`**.
- **`NotificationBellComponent — renders unread badge count from getUnreadCount()`**.
- **`NotificationBellComponent — marks read on dropdown open`**.
- **`NotificationBellComponent — shows toast on live ReceiveNotification event`**.
- **`CustomerOrderToastService — shows one toast on OrderStatusChanged for own user group only`**.

## 7. Observability

- `ILogger<T>` structured logs (named properties, per CONVENTIONS §9) in every new handler: info on successful create+push, warning on push failure (swallowed), matching `SuperAdminLoginFromNewIpEventHandler`'s `logger.LogWarning` style.
- No new OpenTelemetry spans/meters added in this phase — out of scope; existing ASP.NET Core auto-instrumentation (if any exists in `Program.cs`) covers the new controller/hub endpoints without additional code.

## 8. Explicitly OUT of scope

- **Azure Service Bus consumption** (D7) — Phase 5a's publish-only try-on integration untouched.
- **Notification preferences/mute/digest** — every trigger fires unconditionally.
- **Customer-facing notification history UI** — customer gets only the ephemeral toast (F8); their rows persist server-side but no Angular list reads them back.
- **`ReviewModeratedEvent` wiring** — existing, unconsumed, not in D3's trigger set; left for a future phase.
- **API versioning** — none exists in this codebase; not introduced here.

## 9. Risks

- **`PaymentConfirmed` is subscription-billing-scoped, not order-scoped** — resolved (§OPEN QUESTIONS 1) by attaching a new handler to the existing `PaymentConfirmedEvent` (raised in `SubscriptionService.ConfirmPaymentAsync`) rather than inventing an order-scoped event; the order flow's own notifications (`OrderPlaced`, `OrderStatusChanged`) are unaffected.
- **`ReviewSubmitted` requires new customer-facing surface** (no existing submit endpoint) — mitigated by scoping `StoreReviewsController`/`SubmitAsync` to the minimum needed to raise the event (§OPEN QUESTIONS 2); a fuller customer review UX is explicitly not this phase's job.
- **SignalR JWT-over-query-string pattern postdates training data** — mitigated by the mandatory Microsoft Learn MCP lookup at task C1 before C2/C3 are written.
- **Push-failure swallowing could mask a systemic hub outage** — mitigated by the `LogWarning` on every swallowed push; if this becomes noisy in practice, a follow-up phase could add a metric/alert, but that's out of scope here.

## 10. OPEN QUESTIONS (decisions, not facts)

1. **RESOLVED — `PaymentConfirmed` trigger point.** Decided: `PaymentConfirmed` is **subscription-billing-scoped, not order-scoped**. It attaches a new `INotificationHandler<DomainEventNotification<PaymentConfirmedEvent>>` to the **existing** `PaymentConfirmedEvent(Guid TenantId, string TenantEmail, decimal Amount)` (`src/FashionSaaS.Domain/Events/PaymentConfirmedEvent.cs`), which already fires when SuperAdmin confirms a subscription payment (`SubscriptionService.ConfirmPaymentAsync`, `src/FashionSaaS.Application/Subscriptions/SubscriptionService.cs:187`). The previously proposed `OrderPaymentConfirmedEvent` is removed entirely, and `OrderService.CreateAsync` raises no payment-related event — the order flow's notifications are `OrderPlacedEvent` and `OrderStatusChangedEvent` only. See the contract checklist (§2), Group A/D/E task lists, and §5/§9 for the corresponding updates.
2. **RESOLVED — `ReviewSubmitted` / customer review submission is in scope.** Decided: the minimal customer review-submission path is built as part of this phase, since none exists today (`ReviewService.cs` has no `CreateAsync`/submit method; no `StoreReviewsController` exists). Group E builds `POST api/store/reviews` (new `StoreReviewsController`, mirroring `StoreOrdersController`'s `[Authorize(Roles = "Customer")]` auth pattern), `ReviewService.SubmitAsync` (mirrors `ApproveAsync`'s tenant-guard/audit shape), and a FluentValidation validator for the request DTO (rating 1-5, comment length caps, `productId` required) — the review is created `Pending` per the existing moderation flow and raises `ReviewSubmittedEvent`. `IReviewRepository.AddAsync` was verified to exist (inherited from `IGenericRepository<Review>`, `IGenericRepository.cs:11`) — no code-sample correction was needed.
3. **Exact final test count.** Per house rules, do not pre-commit to a number — testing-expert reports the exact `dotnet test` and `ng test` counts at Group G3/G4; the ~22 new backend tests estimated in §5 is a planning aid, not a contract.

## 11. Assumptions

- The existing `AuthenticatedPolicy` rate-limiting policy (used by `InventoryController`/`ReviewsController`) is appropriate for `NotificationsController` and `StoreReviewsController` without a new policy.
- No API-versioning migration is expected mid-phase; new routes are added flat under `api/tenant/...` and `api/store/...` matching every existing controller.
- Angular's `environment.apiBaseUrl` is HTTP(S)-schemed; the hub URL substitutes `http`→`ws`/`https`→`wss` implicitly via `@microsoft/signalr`'s own transport negotiation — no manual scheme-rewriting code needed (confirm against `@microsoft/signalr` docs at implementation time, not yet verified against current library version).

**No further changes to this plan will be made without your sign-off.**
