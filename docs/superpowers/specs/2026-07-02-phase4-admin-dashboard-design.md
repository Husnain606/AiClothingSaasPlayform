# Phase 4: Orders Backend + Tenant Admin Dashboard — Design Specification

**Date:** 2026-07-02
**Status:** APPROVED APPROACH A (extend-then-build) — pending user spec review
**Depends on:** Phase 1 (Core SaaS backend), Phase 2 (Catalog backend), Phase 3 (Customer storefront, merged 2026-07-02)

---

## 1. Goal

Phase 4 delivers two halves that make FashionSaaS operational end-to-end:

1. **Orders + Reporting backend** (.NET 10, extends the existing solution): an Orders domain the Phase 3 storefront checkout can actually post to, tenant-scoped order management, and a full reporting suite (aggregates + CSV export).
2. **Tenant Admin Dashboard** (new `/admin` area inside the existing `fashionsaas-storefront` app, routed by the logged-in user's role): store owners manage their catalog, inventory, orders, customers, discounts, reviews, and settings, with an analytics home page.

**Audience:** Tenant admins only (roles AdminOwner, StoreManager, InventoryManager, OrderManager, ContentManager). Super-admin platform console is out of scope (future phase).

---

## 2. Backend — Orders Domain

### 2.1 Entities (FashionSaaS.Domain)

**Order**
| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| TenantId | Guid | multi-tenant filter (ICurrentTenantService pattern — injected service reference in query filter, NOT local capture) |
| CustomerId | Guid | FK → Customer |
| OrderNumber | string | `ORD-{yyyy}-{6-digit seq}` unique per tenant |
| Status | OrderStatus enum | Pending, Confirmed, Shipped, Delivered, Cancelled |
| OrderDate | DateTime (UTC) | |
| ShippingFirstName/LastName/Email/Phone/Street/City/State/ZipCode/Country | string | flattened snapshot (no FK — orders are immutable records) |
| CardLast4 | string(4) | masked payment reference only; NO full card data ever stored |
| Subtotal, Tax, ShippingCost, Total | decimal(18,2) | Tax = 10% flat (matches storefront); ShippingCost = 0 for now |
| TrackingNumber | string? | set at Ship |
| CancelReason | string? | set at Cancel |

**OrderItem**
| Field | Type | Notes |
|---|---|---|
| Id, OrderId | Guid | |
| ProductId | Guid | reference only |
| ProductVariantId | Guid? | null for variant-less products |
| ProductName, Size, Color | string snapshot | survives later product edits/deletes |
| UnitPrice | decimal(18,2) snapshot | |
| Quantity | int | ≥1 |

**Status transitions (enforced in OrderService, invalid transition → business-rule error):**
Pending → Confirmed → Shipped → Delivered. Cancelled allowed from Pending or Confirmed only.

**Inventory coupling:** stock decremented per variant at order creation; restored on cancellation. Insufficient stock at creation → validation failure listing the offending items.

### 2.2 API — Customer-facing (consumed by storefront)

New area `api/store/*`: `[Authorize(Roles = "Customer")]`, tenant resolved by existing TenantResolutionMiddleware, AuthenticatedPolicy rate limit. (This activates the currently unused `Customer` role.)

| Endpoint | Purpose |
|---|---|
| POST `api/store/orders` | Create order from checkout payload `{shippingAddress, paymentInfo(masked card only — CVV/full PAN rejected by validator), items:[{productId, quantity, variant}]}` → `ResponseData<OrderDto>`. Prices are read server-side from Product/Variant — client prices ignored. |
| GET `api/store/orders` | Current customer's orders (paged) |
| GET `api/store/orders/{id}` | Own order only (cross-customer access → 404) |
| PUT `api/store/orders/{id}/cancel` | Own order, only while Pending/Confirmed |

**Storefront contract alignment:** response DTO matches Phase 3's `Order` model (orderId, orderDate, status lowercase strings, items, shippingAddress, subtotal/tax/shippingCost/total, trackingNumber). Storefront `environment.apiBaseUrl` currently says `/api/v1` while the backend uses unversioned `api/...` routes — resolved by pointing apiBaseUrl at the real base during integration (one-line storefront change; verified in the integration task).

### 2.3 API — Tenant admin

`api/tenant/orders/*`, roles **AdminOwner, OrderManager, StoreManager**, dedicated-verb style matching Phase 2 controllers:

| Endpoint | Purpose |
|---|---|
| GET `api/tenant/orders` | Filterable list (status, date range, customer, search by order number), paged |
| GET `api/tenant/orders/{id}` | Full detail incl. items |
| PUT `api/tenant/orders/{id}/confirm` | Pending → Confirmed |
| PUT `api/tenant/orders/{id}/ship` | Confirmed → Shipped, body `{trackingNumber?}` |
| PUT `api/tenant/orders/{id}/deliver` | Shipped → Delivered |
| PUT `api/tenant/orders/{id}/cancel` | Pending/Confirmed → Cancelled, body `{reason}`; restores stock |

Conventions: ResponseData<T>, [ProducesResponseType] on every action, ApiUrl static routes, FluentValidation for inputs, service layer owns business rules, AuditLogService on all mutations, Mapster profiles for Order/OrderItem DTOs.

---

## 3. Backend — Reporting Suite

New feature slice `api/tenant/reports/*`, roles **AdminOwner, StoreManager** (read-only). Live EF aggregate queries — no materialized views or background jobs until data volume demands it (explicit YAGNI decision).

| Endpoint | Returns |
|---|---|
| GET `reports/summary?from&to` | KPI card block: revenue, orderCount, avgOrderValue, newCustomers, pendingReviews, lowStockCount |
| GET `reports/sales-over-time?from&to&interval=day\|week\|month` | `[{periodStart, revenue, orderCount}]` |
| GET `reports/top-products?from&to&take=10&by=revenue\|units` | product snapshots with revenue + units |
| GET `reports/order-status-breakdown?from&to` | count + revenue per status |
| GET `reports/customer-analytics?from&to&interval` | new customers per interval, repeat-purchase rate, top customers by spend |
| GET `reports/inventory-trends?from&to` | stock adjustments over time + current low-stock list |
| GET `reports/category-sales?from&to` | revenue/units per category (drill-down: `?categoryId=` for its children) |

**CSV export:** every report endpoint accepts `?format=csv` → `text/csv; charset=utf-8` with `Content-Disposition: attachment`. Same query path, different serializer — no duplicate logic.

**Definitions (single source of truth):** revenue = sum of Total on orders NOT Cancelled, bucketed by OrderDate; repeat rate = customers with ≥2 non-cancelled orders ÷ customers with ≥1, in range. Date range validated: `from ≤ to`, max 366 days.

---

## 4. Frontend — `/admin` area inside `fashionsaas-storefront` (role-routed, single app)

No new app. The admin dashboard is a lazy-loaded feature area of the existing storefront, sharing its core (ApiService, AuthService, interceptors), conventions (standalone, zoneless CD, Bootstrap 5.3 CSS-only, Vitest, strict TS, smart/dumb split), and build. Charts: **ng2-charts (Chart.js)** — the one new dependency.

### 4.0 Role-based routing (the app decides what you see)

- **Post-login redirect by role:** after `api/auth/login`, AuthService reads the JWT roles claim. Admin-tier roles (AdminOwner, StoreManager, InventoryManager, OrderManager, ContentManager) → redirect to `/admin`; `Customer` (or no admin role) → `/products` as today.
- **New AdminLayoutComponent** (sidebar + topbar) parallel to the existing MainLayout (shopper) and AuthLayout. `/admin/**` routes live under it; every admin route carries `authGuard + adminRoleGuard` (new functional guard checking JWT roles). Deeper role checks per module (e.g. `/admin/settings` = AdminOwner only).
- **Shopper area untouched:** existing storefront routes and layouts stay as-is. Admins can still browse the shop; the header shows a "Dashboard" link when the user has an admin role (and the admin topbar links back to the store).
- **Lazy isolation:** the entire `/admin` area (and ng2-charts) loads via `loadChildren`, so shoppers never download admin code — bundle impact on the storefront's initial chunk ≈ 0.
- Menus render from the role claim (UI convenience only; enforcement remains server-side).

### 4.1 Module map (lazy feature routes under `/admin`, AdminLayout: sidebar + topbar)

| Module | Contents | Backend | Menu visible to |
|---|---|---|---|
| core (existing, extended) | AuthService gains role parsing from JWT + post-login role redirect; new adminRoleGuard | api/auth | — |
| dashboard | KPI cards, sales chart, top products, status donut, date-range picker | reports/* | AdminOwner, StoreManager |
| orders | list (filter/search/pager), detail, status actions (confirm/ship/deliver/cancel with confirm modal) | tenant/orders | AdminOwner, OrderManager, StoreManager |
| catalog | products CRUD + publish/archive, categories tree (move/reorder), variants, image upload/reorder/primary | tenant/products, categories, variants, images | AdminOwner, StoreManager, ContentManager |
| inventory | stock adjust, low-stock view, per-variant history | tenant/inventory | AdminOwner, InventoryManager |
| customers | list/detail (orders + wishlist), deactivate | tenant/customers, wishlists | AdminOwner, StoreManager |
| discounts | CRUD, deactivate | tenant/discounts | AdminOwner, StoreManager |
| reviews | moderation queue, approve/reject with reason | tenant/reviews | AdminOwner, StoreManager |
| reports | full report pages per section 3 + CSV download buttons | reports/* | AdminOwner, StoreManager |
| settings | tenant profile, tenant users + role assignment, subscription view, bank account (masked; TOTP re-verify for full) | tenant/profile, users, subscription, bank-account | AdminOwner only |
| shared | table w/ sort+pager, KPI card, chart wrappers, confirm modal, toast/alert, status badge, date-range picker, empty/loading states | — | — |

### 4.2 Security model (frontend)

- One login for everyone (`api/auth/login`); JWT roles claim drives post-login redirect, adminRoleGuard, and menu rendering (UI convenience — enforcement stays server-side).
- 401 → interceptor clears session, redirects to login. 403 → friendly "no permission" page.
- No `alert()` anywhere new — shared toast component introduced for the admin area (Phase 3 backlog lesson); existing storefront alert() call sites migrated opportunistically when touched.

### 4.3 UX standards

Responsive (admin sidebar collapses to drawer < 992px), WCAG 2.1 AA (labels, keyboard nav, aria on icon buttons, focus states), skeleton/loading states on every async view, empty states with CTAs, destructive actions require confirm modal, tables: server-side paging. Bundle budget: storefront initial chunk must stay ≤ 600 kB (admin is lazy); a separate budget check on the admin chunk in the plan's final task.

---

## 5. Error handling

Backend: existing global IExceptionHandler; business-rule violations return 400 ResponseData with message; invalid status transition → 400 `"Cannot ship an order in status Pending"`. Frontend: ErrorInterceptor maps 400 messages into toasts; form-level validation mirrors FluentValidation rules where practical.

## 6. Testing strategy

**Backend (xUnit + FluentAssertions + Moq, patterns from Phases 1-2):** Domain tests (status transitions, order number gen); Application tests (OrderService create/price-from-server/stock decrement+restore/transitions; ReportService each metric with seeded in-memory data incl. cancelled-order exclusion); Repository integration tests (filters, tenant isolation); one E2E workflow test (checkout → confirm → ship → deliver; cancel path restores stock). Target: existing 366 stay green + ~80-100 new.

**Frontend (Vitest, Phase 3 conventions):** zoneless rules (no fakeAsync; vi fake timers; setInput; TestBed.resetTestingModule per beforeEach; provideRouter([])); services 100%, components ≥80%; suite green ×2 before any task completes. Roslyn Navigator used during backend tasks for symbol navigation/diagnostics per project convention.

## 7. Explicitly out of scope

Super-admin platform console; real payment processing (CardLast4 only; payment gateway is a later phase); email notifications on status change (Phase 7 real-time/notifications); materialized reporting views; storefront anonymous browsing change (separate pending product decision); mobile apps.

## 8. Execution shape

Backend first (Orders domain → order APIs → reporting → tests), then the admin area inside the storefront app (role routing + AdminLayout → dashboard → orders → catalog → inventory/customers/discounts/reviews → reports → settings → hardening), finishing with an integration task (apiBaseUrl fix + live checkout-to-admin-order smoke test: customer places order in shop, admin sees and ships it). Detailed task breakdown belongs to the implementation plan (writing-plans), executed via subagent-driven development with per-task code review, as in Phase 3.
