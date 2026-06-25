# Phase 2 — Product / Inventory / Catalog Backend — Design

**Status:** Approved (design), pending spec review.
**Builds on:** Phase 1 Core SaaS Backend (complete, merged to `master`).
**Conventions:** Binding — see [docs/CONVENTIONS.md](../../CONVENTIONS.md) (§1 Refit, §2 Options pattern, §3 global `IExceptionHandler`, §4 indexing, §5 lightweight collections).

---

## 1. Overview

Phase 2 adds each tenant's **product catalog** to the FashionSaaS backend: hierarchical categories, products with size×color variants, per-variant inventory, product imagery on Cloudinary, plus the supporting **Customer**, **Discount/Coupon**, **Review**, and **Wishlist** data models and their admin/service layers.

**Explicitly deferred to Phase 3** (customer storefront): customer authentication/self-registration, cart, checkout, order placement, coupon *redemption*, and customer-written reviews/wishlist actions. Phase 2 builds the data models and admin/service layers those features will consume; it does not build the customer-facing write flows.

Everything is **tenant-owned and tenant-isolated** — a tenant's catalog is private to that tenant.

---

## 2. Tech Stack & Conventions (inherited from Phase 1)

| Concern | Choice |
|---|---|
| Framework | .NET 10, ASP.NET Core 10 Web API |
| Architecture | Clean Architecture, feature-sliced Application layer |
| Persistence | EF Core, SQL Server, single-DB multi-tenancy |
| Response envelope | `ResponseData<T>` everywhere; controllers `return StatusCode(response.StatusCode, response)` |
| Config | Options pattern (`IOptions<T>`) — CONVENTIONS §2 |
| Errors | Global `IExceptionHandler` — CONVENTIONS §3 |
| Third-party HTTP | Refit; for Cloudinary use its official SDK behind an abstraction (see §6) — CONVENTIONS §1 |
| Collections | Lightest read-only type by demand; EF nav collections stay `ICollection<T>` — CONVENTIONS §5 |
| Domain events | MediatR via `DomainEventNotification<T>` wrapper, auto-dispatched by `UnitOfWork.SaveChangesAsync` |
| Repos/services | `GenericRepository<T>`/`GenericService<T>` base + entity-specific |
| Tests | xUnit + Moq + FluentAssertions + EF Core InMemory |
| Rate limiting | Reuse Phase 1 policies (AuthenticatedPolicy for tenant endpoints) |

### Multi-tenancy (apply the Phase-1 review fix from the start)
Every Phase 2 entity is tenant-owned. Apply the **dynamic** EF global query filter pattern — the filter lambda references the injected `ICurrentTenantService` instance (NOT a value captured in `OnModelCreating`, which the model cache freezes). All catalog reads are automatically scoped to the current tenant; cross-tenant access uses `.IgnoreQueryFilters()` only on explicit SuperAdmin paths. Controller-level guards remain as defense-in-depth, and `TenantId` is sourced only from `ICurrentTenantService` (resolved after authentication — Phase-1 pipeline order).

---

## 3. Roles (reused from Phase 1 `RoleType`)

| Role | Phase 2 capability |
|---|---|
| `SuperAdmin` | Cross-tenant oversight (read), MFA-gated |
| `AdminOwner` | Full catalog management for own tenant |
| `StoreManager` | Manage products, categories, variants |
| `InventoryManager` | Stock adjustments, low-stock |
| `ContentManager` | Product content + images |
| `Customer` | (data model only in Phase 2; capabilities in Phase 3) |

Tenant catalog endpoints require an appropriate tenant role + `AuthenticatedPolicy` rate limiting.

---

## 4. Domain Model

All entities tenant-owned (`TenantId`), inherit `BaseEntity`, included in the dynamic query filter.

### 4.1 Category (hierarchical)
- `Id`, `TenantId`, `Name`, `Slug` (unique per tenant), `Description?`
- `ParentCategoryId?` (self-reference → tree), `SortOrder`, `IsActive`
- Nav: `ParentCategory`, `ICollection<Category> Children`, `ICollection<Product> Products`
- **Rules:** slug unique per tenant; cycle prevention (a category cannot be its own ancestor); deleting a category with children or products is blocked (or reparents — see §8).

### 4.2 Product
- `Id`, `TenantId`, `CategoryId`, `Name`, `Slug` (unique per tenant), `Description?`
- `BasePrice` (decimal), `Status` (enum: Draft / Active / Archived), `Tags?` (csv or json)
- Nav: `Category`, `ICollection<ProductVariant> Variants`, `ICollection<ProductImage> Images`, `ICollection<Review> Reviews`
- **Rules:** slug unique per tenant; a product must have ≥1 active variant to be `Active`; price ≥ 0.

### 4.3 ProductVariant
- `Id`, `TenantId`, `ProductId`, `Size` (string/enum), `Color` (string), `Sku` (unique per tenant)
- `StockQuantity` (int ≥ 0), `PriceOverride?` (decimal), `IsActive`
- **Rules:** SKU unique per tenant; (ProductId, Size, Color) unique; stock never negative; effective price = `PriceOverride ?? Product.BasePrice`.

### 4.4 ProductImage
- `Id`, `TenantId`, `ProductId`, `VariantId?`, `CloudinaryPublicId`, `Url`, `AltText?`, `SortOrder`, `IsPrimary`
- **Rules:** exactly one primary image per product; deleting the row also deletes the Cloudinary asset (best-effort, logged).

### 4.5 StockAdjustment (append-only audit)
- `Id`, `TenantId`, `ProductVariantId`, `Delta` (int, +/-), `Reason` (enum: Restock / Sale / Correction / Damage / Return), `ResultingQuantity`, `AdjustedByUserId`, `CreatedAt`
- **Rules:** never updated/deleted (append-only, like AuditLog); applying an adjustment updates the variant's `StockQuantity` atomically and records the result.

### 4.6 Customer (admin-managed in Phase 2)
- `Id`, `TenantId`, `FirstName`, `LastName`, `Email` (unique per tenant), `Phone?`, `IsActive`
- No password/auth in Phase 2 (self-registration + auth are Phase 3). `PasswordHash` deliberately omitted now; Phase 3 extends.
- Nav: `ICollection<Review> Reviews`, `Wishlist?`

### 4.7 Discount / Coupon
- `Id`, `TenantId`, `Code` (unique per tenant), `Type` (enum: Percentage / FixedAmount), `Value` (decimal)
- `MinOrderAmount?`, `MaxRedemptions?`, `RedemptionCount` (default 0), `StartsAt`, `EndsAt`, `IsActive`
- **Rules:** code unique per tenant; value > 0; percentage ≤ 100; admin defines/manages now — redemption against an order happens in Phase 3.

### 4.8 Review
- `Id`, `TenantId`, `ProductId`, `CustomerId`, `Rating` (1–5), `Title?`, `Body?`
- `Status` (enum: Pending / Approved / Rejected) for admin moderation, `CreatedAt`
- **Rules:** rating 1–5; one review per (Customer, Product); only Approved reviews are returned to storefront reads (Phase 3). In Phase 2, reviews are created via admin/seed; customer submission is Phase 3.

### 4.9 Wishlist / WishlistItem
- `Wishlist`: `Id`, `TenantId`, `CustomerId` (one per customer)
- `WishlistItem`: `Id`, `TenantId`, `WishlistId`, `ProductId`, `ProductVariantId?`, `CreatedAt`
- **Rules:** an item is unique per (Wishlist, Product, Variant). Customer-facing add/remove is Phase 3; data model + admin/service read now.

### 4.10 Enums
`ProductStatus`, `StockAdjustmentReason`, `DiscountType`, `ReviewStatus`.

---

## 5. Application Layer (feature-sliced)

New feature folders, each with Commands / Queries / DTOs / Service following Phase 1 patterns:

- `Categories/` — Create, Update, Delete, Reorder, MoveNode; GetTree, GetById, GetAll(filter).
- `Products/` — Create, Update, Publish, Archive, Delete; GetAll(filter+paging→`PagedResult`), GetById, GetBySlug.
- `ProductVariants/` — Add, Update, Deactivate, Delete; GetByProduct.
- `ProductImages/` — Upload (→ Cloudinary), Delete, SetPrimary, Reorder; GetByProduct.
- `Inventory/` — AdjustStock, GetLowStock(threshold), GetStockHistory(variant).
- `Customers/` — Create, Update, Deactivate; GetAll(filter+paging), GetById.
- `Discounts/` — Create, Update, Deactivate, Delete; GetAll, GetById, GetByCode.
- `Reviews/` — Approve, Reject, Delete; GetAll(filter: status/product+paging), GetById. (Create = Phase 3.)
- `Wishlists/` — GetByCustomer; admin remove-item. (Customer add = Phase 3.)

New interfaces in `Application/Interfaces/`: `ICategoryRepository`, `IProductRepository`, `IProductVariantRepository`, `IProductImageRepository`, `IStockAdjustmentRepository`, `ICustomerRepository`, `IDiscountRepository`, `IReviewRepository`, `IWishlistRepository`, `IImageStorageService`. New domain events as needed (e.g. `ProductPublishedEvent`, `LowStockEvent`).

All services return `ResponseData<T>`; paged queries return `ResponseData<PagedResult<T>>`; state changes raise domain events (added before `SaveChangesAsync`) and write `AuditLog` entries via the Phase-1 `IAuditLogService`.

---

## 6. Cloudinary Image Storage

- `IImageStorageService` (Application abstraction): `UploadAsync(stream, fileName, folder) → (PublicId, Url)`, `DeleteAsync(publicId)`.
- Infrastructure `CloudinaryImageStorageService` implements it using the official **CloudinaryDotNet** SDK (a maintained client, not hand-rolled `HttpClient` — satisfies CONVENTIONS §1; abstraction keeps Application/Domain Cloudinary-free).
- Config via Options pattern: `CloudinarySettings { CloudName, ApiKey, ApiSecret }` — **secrets (ApiKey/ApiSecret) from environment / `appsettings.Development.json`, never in `appsettings.json`** (Phase-1 secret-hygiene rule). `ValidateOnStart` on the required values.
- Uploads namespaced per tenant (folder = tenant slug/id) for isolation. We persist `PublicId` + `Url`; deletion is best-effort with failure logged (never blocks the DB delete).
- Image upload endpoints accept `multipart/form-data`; validate content-type + size.

---

## 7. API Surface (tenant-scoped controllers, Phase-1 conventions)

Controllers under `Controllers/Tenant/` (AdminOwner/StoreManager/etc.) — thin, `[HttpVerb(ApiUrl.X)]`, full `ProducesResponseType` set, `[Authorize(Roles=...)]`, `[EnableRateLimiting("AuthenticatedPolicy")]`:

- `CategoriesController`, `ProductsController`, `ProductVariantsController`, `ProductImagesController`, `InventoryController`, `CustomersController`, `DiscountsController`, `ReviewsController`, `WishlistsController`.

`ApiUrl` gains nested static classes for each (e.g. `TenantProducts`, `TenantCategories`, …). Read endpoints are shaped so the Phase 3 storefront can consume them (e.g. product list with variants + primary image + approved-review summary).

SuperAdmin cross-tenant read/oversight endpoints are optional and minimal (deferred unless trivially free).

---

## 8. Key Business Rules & Edge Cases

- **Slugs** unique per tenant (Category, Product); validated format (lowercase/hyphen) — reuse the Phase-1 slug-style validation.
- **SKU** unique per tenant; (Product, Size, Color) unique.
- **Category hierarchy:** cycle prevention on move/create; delete blocked when the node has children or assigned products (return 409 with guidance) — no silent reparenting.
- **Stock:** never negative; adjustments are atomic + audited; `Active` product requires ≥1 active in-stock-or-backorderable variant (backorder out of scope → require ≥1 active variant).
- **Publish:** Draft→Active requires name, category, ≥1 active variant, ≥1 image; Archive hides from storefront reads.
- **Reviews:** only `Approved` surfaced to storefront; moderation transitions audited.
- **Images:** one primary per product; Cloudinary delete failure logged, DB row still removed.
- **Tenant isolation:** all the above scoped by tenant; cross-tenant reference (e.g. a product's category from another tenant) impossible by construction (query filter + FK within tenant).

---

## 9. Testing Strategy

- Domain: variant effective-price, slug/SKU rules, hierarchy cycle detection, stock-non-negative.
- Application: each service's happy + failure paths (uniqueness conflicts→409, not-found→404, validation→400), publish gating, review moderation, stock adjustment math + audit, discount validity.
- Infrastructure: `CloudinaryImageStorageService` against a mocked Cloudinary client (no live calls in tests); repository query/filter behavior on InMemory; tenant-isolation tests.
- One EF migration for the Phase 2 schema; verify a follow-up `migrations add` probe is empty (no churn).

---

## 10. Out of Scope for Phase 2 (→ Phase 3+)

- Customer authentication / self-registration, cart, checkout, order placement.
- Coupon redemption against orders; customer-submitted reviews; customer-managed wishlist writes.
- Angular storefront UI (Phase 3).
- Payment gateway, shipping, tax (later phases).
- Search/faceted filtering beyond basic category/price/status filters (later optimization).

---

## 11. Build Order (informs the implementation plan)

1. Domain entities + enums + events + value objects (slug reuse).
2. Application interfaces + new DTOs + `IImageStorageService`.
3. Infrastructure: EF configurations + dynamic tenant query filters + migration; repositories; `CloudinaryImageStorageService` + Options + DI.
4. Application services: Categories → Products → Variants → Inventory → Images → Customers → Discounts → Reviews → Wishlists.
5. API: `ApiUrl` additions + tenant controllers + multipart image upload.
6. Cross-cutting: audit + domain events wired; tenant-isolation tests; final review.
