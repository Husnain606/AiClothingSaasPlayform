# Phase 2 — Product / Inventory / Catalog Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the tenant-scoped product catalog backend for FashionSaaS — hierarchical categories, products with size×color variants, per-variant inventory with audit, Cloudinary product images, and Customer/Discount/Review/Wishlist data + admin layers — on the existing .NET 10 Clean Architecture foundation.

**Architecture:** Identical to Phase 1 (Domain → Application → Infrastructure → API, feature-sliced Application). NO new architectural patterns. Every new entity is tenant-owned and added to the dynamic EF global query filter. All services return `ResponseData<T>`; controllers are thin; domain events via `DomainEventNotification<T>`; config via `IOptions<T>`; errors via the global `IExceptionHandler`.

**Tech Stack:** .NET 10 · EF Core (SQL Server) · CloudinaryDotNet · MediatR · FluentValidation · Serilog · xUnit · Moq · FluentAssertions · EF Core InMemory.

**Spec:** docs/superpowers/specs/2026-06-24-phase2-product-catalog-backend-design.md

## Global Constraints

- Target framework `net10.0`; all Phase-1 conventions in docs/CONVENTIONS.md are BINDING (§1 Refit/SDK-behind-abstraction, §2 Options pattern — no `IConfiguration` string-indexing in services, §3 global `IExceptionHandler`, §4 index read-heavy/queried columns, §5 lightest read-only collection type; EF nav collections stay `ICollection<T>`).
- **Mirror Phase 1 patterns exactly.** Implementers MUST read the analogous Phase 1 file before writing a new one (e.g. read `TenantService`/`TenantsController`/`TenantConfiguration` before writing `ProductService`/`ProductsController`/`ProductConfiguration`). Match naming, structure, error codes, audit calls, and test style.
- All new entities are **tenant-owned**: `TenantId` column; added to the **dynamic** global query filter in `ApplicationDbContext` (lambda references `currentTenantService` instance — never a local captured in `OnModelCreating`). Cross-tenant access only via explicit `.IgnoreQueryFilters()` on SuperAdmin paths.
- `TenantId` is sourced only from `ICurrentTenantService` (resolved after authentication per the corrected Phase-1 pipeline). Tenant controllers also keep the cross-tenant 403 guard pattern where they fetch by id.
- All amounts are `decimal`. Slugs/SKUs unique per tenant. Append-only entities (StockAdjustment) are never updated/deleted.
- Secrets (Cloudinary ApiKey/ApiSecret) from env / `appsettings.Development.json` only — never `appsettings.json`. Bind via Options + `ValidateOnStart`.
- Every controller action: `[HttpVerb(ApiUrl.X)]`, `[ProducesResponseType(200/201)]`, `[ProducesResponseType(typeof(ResponseData<string>),400)]`, `[ProducesResponseType(typeof(ResponseData<string>),500)]`, `[Authorize(Roles=...)]`, `[EnableRateLimiting("AuthenticatedPolicy")]`; returns `StatusCode(response.StatusCode, response)`.
- Each task ends with: solution builds clean (NU1701 warnings excepted), `dotnet test` green, and a commit.

---

## File Structure Map (new files; mirrors Phase 1 layout)

```
src/FashionSaaS.Domain/
  Enums/            ProductStatus, StockAdjustmentReason, DiscountType, ReviewStatus
  Entities/         Category, Product, ProductVariant, ProductImage, StockAdjustment,
                    Customer, Discount, Review, Wishlist, WishlistItem
  Events/           ProductPublishedEvent, ProductArchivedEvent, LowStockEvent,
                    ReviewModeratedEvent (+ others as needed)
src/FashionSaaS.Application/
  Interfaces/       ICategoryRepository, IProductRepository, IProductVariantRepository,
                    IProductImageRepository, IStockAdjustmentRepository, ICustomerRepository,
                    IDiscountRepository, IReviewRepository, IWishlistRepository, IImageStorageService
  Configuration/    CloudinarySettings
  Categories/  Products/  ProductVariants/  ProductImages/  Inventory/
  Customers/  Discounts/  Reviews/  Wishlists/        (each: Commands/ Queries/ DTOs/ <Service>.cs)
src/FashionSaaS.Infrastructure/
  Persistence/Configurations/   <Entity>Configuration.cs (10)
  Persistence/Repositories/     <Entity>Repository.cs (9)
  Persistence/Migrations/       Phase2Catalog (generated)
  Services/                     CloudinaryImageStorageService.cs
src/FashionSaaS.API/
  Constants/ApiUrl.cs           (add nested classes)
  Controllers/Tenant/           Categories, Products, ProductVariants, ProductImages, Inventory,
                                Customers, Discounts, Reviews, Wishlists Controllers
tests/                          Domain.Tests, Application.Tests, Infrastructure.Tests additions
```

---

## Task 1: Domain — Enums, Events
**Files:** Create `Domain/Enums/{ProductStatus,StockAdjustmentReason,DiscountType,ReviewStatus}.cs`; `Domain/Events/{ProductPublishedEvent,ProductArchivedEvent,LowStockEvent,ReviewModeratedEvent}.cs`. Test: `Domain.Tests` (none needed beyond compile).
**Interfaces — Produces:** enums + events consumed by entities/services.
- [ ] Create enums: `ProductStatus { Draft=1, Active=2, Archived=3 }`, `StockAdjustmentReason { Restock=1, Sale=2, Correction=3, Damage=4, Return=5 }`, `DiscountType { Percentage=1, FixedAmount=2 }`, `ReviewStatus { Pending=1, Approved=2, Rejected=3 }`.
- [ ] Create events as `record … : IDomainEvent` (pure marker — no MediatR in Domain): `ProductPublishedEvent(Guid ProductId, Guid TenantId)`, `ProductArchivedEvent(Guid ProductId, Guid TenantId)`, `LowStockEvent(Guid ProductVariantId, Guid TenantId, int Remaining)`, `ReviewModeratedEvent(Guid ReviewId, Guid TenantId, ReviewStatus Status)`.
- [ ] `dotnet build src/FashionSaaS.Domain` → clean. Commit: `feat(phase2): add catalog enums and domain events`.

## Task 2: Domain — Category, Product, ProductVariant, ProductImage
**Files:** Create those 4 entity files in `Domain/Entities/`. Read Phase 1 `Tenant.cs`/`User.cs` first for the BaseEntity + nav-collection style.
**Interfaces — Consumes:** BaseEntity, enums (Task 1). Produces: entities for EF config (Task 5) + services.
- [ ] `Category : BaseEntity` — `Guid TenantId; string Name; string Slug; string? Description; Guid? ParentCategoryId; int SortOrder; bool IsActive=true;` nav: `Category? ParentCategory; ICollection<Category> Children = new List<Category>(); ICollection<Product> Products = new List<Product>();`
- [ ] `Product : BaseEntity` — `Guid TenantId; Guid CategoryId; string Name; string Slug; string? Description; decimal BasePrice; ProductStatus Status = ProductStatus.Draft; string? Tags;` nav: `Category? Category; ICollection<ProductVariant> Variants; ICollection<ProductImage> Images; ICollection<Review> Reviews;` (init collections).
- [ ] `ProductVariant : BaseEntity` — `Guid TenantId; Guid ProductId; string Size; string Color; string Sku; int StockQuantity; decimal? PriceOverride; bool IsActive=true;` nav `Product?`.
- [ ] `ProductImage : BaseEntity` — `Guid TenantId; Guid ProductId; Guid? VariantId; string CloudinaryPublicId; string Url; string? AltText; int SortOrder; bool IsPrimary;` nav `Product?`.
- [ ] `dotnet build src/FashionSaaS.Domain` clean. Commit: `feat(phase2): add Category, Product, ProductVariant, ProductImage entities`.

## Task 3: Domain — StockAdjustment, Customer, Discount, Review, Wishlist, WishlistItem
**Files:** those 6 entity files in `Domain/Entities/`.
- [ ] `StockAdjustment : BaseEntity` (append-only) — `Guid TenantId; Guid ProductVariantId; int Delta; StockAdjustmentReason Reason; int ResultingQuantity; Guid AdjustedByUserId;` nav `ProductVariant?`.
- [ ] `Customer : BaseEntity` — `Guid TenantId; string FirstName; string LastName; string Email; string? Phone; bool IsActive=true;` nav `ICollection<Review> Reviews; Wishlist? Wishlist;` (no password in Phase 2).
- [ ] `Discount : BaseEntity` — `Guid TenantId; string Code; DiscountType Type; decimal Value; decimal? MinOrderAmount; int? MaxRedemptions; int RedemptionCount; DateTime StartsAt; DateTime EndsAt; bool IsActive=true;`
- [ ] `Review : BaseEntity` — `Guid TenantId; Guid ProductId; Guid CustomerId; int Rating; string? Title; string? Body; ReviewStatus Status = ReviewStatus.Pending;` nav `Product? Customer?`.
- [ ] `Wishlist : BaseEntity` — `Guid TenantId; Guid CustomerId;` nav `ICollection<WishlistItem> Items;`. `WishlistItem : BaseEntity` — `Guid TenantId; Guid WishlistId; Guid ProductId; Guid? ProductVariantId;` nav `Wishlist?`.
- [ ] `dotnet build src/FashionSaaS.Domain` clean. Commit: `feat(phase2): add StockAdjustment, Customer, Discount, Review, Wishlist entities`.

## Task 4: Application — Repository interfaces + IImageStorageService + CloudinarySettings
**Files:** Create the 9 repo interfaces + `IImageStorageService` in `Application/Interfaces/`; `Application/Configuration/CloudinarySettings.cs`. Read Phase 1 `ITenantRepository`/`IUserRepository` first.
- [ ] Each `I<Entity>Repository : IGenericRepository<Entity>` with entity-specific reads, e.g.:
  - `ICategoryRepository`: `Task<bool> SlugExistsAsync(Guid tenantId, string slug, Guid? excludeId=null); Task<IReadOnlyList<Category>> GetTreeAsync(Guid tenantId);`
  - `IProductRepository`: `SlugExistsAsync(...); Task<Product?> GetByIdWithDetailsAsync(Guid id); Task<(IReadOnlyList<Product> items,int total)> GetPagedAsync(ProductFilter filter);`
  - `IProductVariantRepository`: `Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId=null); Task<IReadOnlyList<ProductVariant>> GetByProductAsync(Guid productId); Task<IReadOnlyList<ProductVariant>> GetLowStockAsync(Guid tenantId, int threshold);`
  - `IStockAdjustmentRepository`: `Task<IReadOnlyList<StockAdjustment>> GetByVariantAsync(Guid variantId);`
  - `ICustomerRepository`: `Task<bool> EmailExistsAsync(Guid tenantId, string email, Guid? excludeId=null);`
  - `IDiscountRepository`: `Task<Discount?> GetByCodeAsync(Guid tenantId, string code); Task<bool> CodeExistsAsync(...);`
  - `IReviewRepository`: `Task<(IReadOnlyList<Review> items,int total)> GetPagedAsync(...);`
  - `IWishlistRepository`: `Task<Wishlist?> GetByCustomerAsync(Guid customerId);`
  - `IProductImageRepository`: `Task<IReadOnlyList<ProductImage>> GetByProductAsync(Guid productId); Task<ProductImage?> GetPrimaryAsync(Guid productId);`
- [ ] `IImageStorageService`: `Task<(string PublicId, string Url)> UploadAsync(Stream content, string fileName, string folder, CancellationToken ct=default); Task DeleteAsync(string publicId, CancellationToken ct=default);`
- [ ] `CloudinarySettings { public const string SectionName="Cloudinary"; [Required] string CloudName; [Required] string ApiKey; [Required] string ApiSecret; }`
- [ ] Return types follow §5 (`IReadOnlyList<T>`). `dotnet build src/FashionSaaS.Application` clean. Commit: `feat(phase2): add catalog repository interfaces, IImageStorageService, CloudinarySettings`.

## Task 5: Infrastructure — EF configurations + dynamic tenant query filters + migration
**Files:** 10 `<Entity>Configuration.cs` in `Persistence/Configurations/`; modify `ApplicationDbContext.cs` (add DbSets + query filters); generate migration. Read Phase 1 `TenantConfiguration`/`BankAccountConfiguration` + the corrected `ApplicationDbContext` query-filter pattern first.
- [ ] Add `DbSet<>` for all 10 entities to `ApplicationDbContext`.
- [ ] Add each tenant-owned entity to the **dynamic** global query filter, mirroring the fixed BankAccount pattern: `modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == currentTenantService.TenantId);` (and Category, ProductVariant, ProductImage, StockAdjustment, Customer, Discount, Review, Wishlist, WishlistItem). NOTE: when a parent has a filter, related entities must also be filtered consistently (EF requires query filters on both ends of required relationships — apply to all).
- [ ] Configurations: column sizes (Name 200, Slug 200, Sku 100, Url 2000, etc.); indexes per §4 — Category `(TenantId,Slug)` unique + `ParentCategoryId`; Product `(TenantId,Slug)` unique + `CategoryId` + `Status`; ProductVariant `(TenantId,Sku)` unique + `(ProductId)` + `(ProductId,Size,Color)` unique; ProductImage `ProductId`; StockAdjustment `ProductVariantId`+`CreatedAt`; Customer `(TenantId,Email)` unique; Discount `(TenantId,Code)` unique; Review `(ProductId,Status)` + `(CustomerId,ProductId)` unique; Wishlist `CustomerId` unique; WishlistItem `(WishlistId,ProductId,ProductVariantId)` unique. Decimal precision (18,2) on prices. Delete behaviors: Restrict for Category→Product and Product→Variant (block delete with children); Cascade for Product→Images, Wishlist→Items.
- [ ] Self-referencing Category: configure `ParentCategory`/`Children` with `OnDelete(Restrict)`.
- [ ] Generate migration: `dotnet ef migrations add Phase2Catalog --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure`. Verify it contains all tables/indexes and a probe `migrations add` is empty.
- [ ] `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): EF configs, tenant query filters, Phase2Catalog migration`.

## Task 6: Infrastructure — Repositories + UnitOfWork wiring
**Files:** 9 `<Entity>Repository.cs` in `Persistence/Repositories/` (StockAdjustment shares pattern). Read Phase 1 `UserRepository`/`TenantRepository`. Register in `DependencyInjection.cs`.
- [ ] Implement each repo `: GenericRepository<Entity>` with the interface methods using EF (`FirstOrDefaultAsync`, `.Where(...).ToListAsync()`, `.Include(...)` for detail/tree reads, `IgnoreQueryFilters()` only where a SuperAdmin cross-tenant read is explicitly needed). `GetTreeAsync` loads all tenant categories and builds the tree (or returns flat list ordered by parent + sort — service builds tree). Paged reads return `(items,total)`.
- [ ] Register all repos in `DependencyInjection.AddInfrastructure` (Scoped). Add `services.Configure`/`AddOptions<CloudinarySettings>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.
- [ ] If UnitOfWork exposes repos, extend it; else services inject repos directly (match Phase 1 — services inject repos + IUnitOfWork).
- [ ] `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): catalog repositories + DI registration`.

## Task 7: Infrastructure — CloudinaryImageStorageService
**Files:** Create `Services/CloudinaryImageStorageService.cs`; add `CloudinaryDotNet` package to Infrastructure; register in DI.
**Interfaces — Consumes:** `IImageStorageService`, `IOptions<CloudinarySettings>`.
- [ ] Add package: `dotnet add src/FashionSaaS.Infrastructure package CloudinaryDotNet`.
- [ ] Implement `CloudinaryImageStorageService(IOptions<CloudinarySettings> options) : IImageStorageService`: construct `Cloudinary` with `new Account(CloudName, ApiKey, ApiSecret)`. `UploadAsync` → `ImageUploadParams { File = new FileDescription(fileName, content), Folder = folder, ... }`; return `(result.PublicId, result.SecureUrl.ToString())`; throw on error result. `DeleteAsync` → `DeletionParams(publicId)`; log (do not throw) on failure.
- [ ] Register `services.AddScoped<IImageStorageService, CloudinaryImageStorageService>()`.
- [ ] Add `Cloudinary` section to `appsettings.Development.json` (placeholder dev values) — NOT `appsettings.json`.
- [ ] Test with a mocked Cloudinary boundary OR an interface-level test (no live calls). `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): Cloudinary image storage service behind IImageStorageService`.

## Task 8: Application — CategoryService (tree, cycle prevention)
**Files:** `Application/Categories/` (Commands: Create/Update/Delete/Reorder/Move; Queries: GetTree/GetById/GetAll; DTOs; `CategoryService.cs`). Test: `Application.Tests/Categories/CategoryServiceTests.cs`. Read Phase 1 `TenantService` (slug + audit + events pattern).
- [ ] DTOs: `CreateCategoryRequest`, `UpdateCategoryRequest`, `CategoryResponse`, `CategoryTreeNode` (Id, Name, Slug, SortOrder, `IReadOnlyList<CategoryTreeNode> Children`).
- [ ] `CategoryService`: Create (validate slug format+uniqueness, parent exists+same tenant); Update; Move (set ParentCategoryId — **reject if the new parent is the node itself or any descendant** → 400 cycle); Delete (**409 if has children or assigned products**); Reorder (update SortOrder); GetTree (build nested tree from `GetTreeAsync`); GetById/GetAll. Audit each mutation; return `ResponseData<T>`.
- [ ] Tests: slug uniqueness conflict→409; cycle on move→400; delete-with-children→409; tree builds correct nesting; happy paths. `dotnet test` green. Commit: `feat(phase2): CategoryService with hierarchy + cycle prevention`.

## Task 9: Application — ProductService (publish/archive)
**Files:** `Application/Products/`. Tests. Read `CategoryService` + Phase 1 patterns.
- [ ] DTOs: Create/Update requests, `ProductResponse` (incl. category name, variant count, primary image url, approved-review summary), `ProductFilter` (search, categoryId, status, page, pageSize).
- [ ] `ProductService`: Create (slug uniqueness, category exists, BasePrice≥0, Status=Draft); Update; Publish (Draft→Active requires name, category, ≥1 active variant, ≥1 image → else 400 with reason; raise `ProductPublishedEvent`); Archive (→Archived, `ProductArchivedEvent`); Delete (409 if it has variants? per spec block or cascade — block delete of Active; allow delete of Draft); GetAll(paged), GetById, GetBySlug. Audit + events.
- [ ] Tests: publish gating (missing variant/image→400), slug conflict→409, archive transition, paging. Commit: `feat(phase2): ProductService with publish/archive gating`.

## Task 10: Application — ProductVariantService
**Files:** `Application/ProductVariants/`. Tests.
- [ ] DTOs: Add/Update requests, `VariantResponse` (incl. effective price = `PriceOverride ?? product.BasePrice`).
- [ ] `ProductVariantService`: Add (SKU uniqueness per tenant; (Product,Size,Color) uniqueness→409; product exists); Update; Deactivate; Delete; GetByProduct. Audit.
- [ ] Tests: SKU conflict→409, duplicate size+color→409, effective price calculation. Commit: `feat(phase2): ProductVariantService`.

## Task 11: Application — InventoryService (stock adjust, low-stock, history)
**Files:** `Application/Inventory/`. Tests.
- [ ] DTOs: `AdjustStockRequest` (variantId, delta, reason), `StockAdjustmentResponse`, `LowStockItemResponse`.
- [ ] `InventoryService`: AdjustStock (load variant; `newQty = StockQuantity + delta`; **reject if newQty < 0 → 400**; set StockQuantity; create append-only `StockAdjustment` with ResultingQuantity + AdjustedByUserId; if newQty ≤ lowStockThreshold raise `LowStockEvent`; single SaveChanges); GetLowStock(threshold); GetStockHistory(variantId). Audit.
- [ ] Tests: positive/negative adjustment, negative-stock rejection→400, audit record created with correct ResultingQuantity, low-stock event raised. Commit: `feat(phase2): InventoryService with stock audit + low-stock`.

## Task 12: Application — ProductImageService (Cloudinary)
**Files:** `Application/ProductImages/`. Tests (mock `IImageStorageService`).
- [ ] DTOs: `UploadImageRequest` (productId, optional variantId, alt, stream/file handled at controller), `ProductImageResponse`.
- [ ] `ProductImageService`: Upload (call `imageStorage.UploadAsync` with folder = tenant id/slug; persist row; if first image set IsPrimary); Delete (remove row; `imageStorage.DeleteAsync` best-effort, log on failure); SetPrimary (unset others, set one — exactly one primary); Reorder; GetByProduct. Audit.
- [ ] Tests: upload persists publicId+url (mock storage), set-primary enforces single primary, delete still removes row when storage delete throws. Commit: `feat(phase2): ProductImageService with Cloudinary upload/primary/reorder`.

## Task 13: Application — CustomerService
**Files:** `Application/Customers/`. Tests.
- [ ] `CustomerService`: Create (email uniqueness per tenant→409), Update, Deactivate, GetAll(paged+filter), GetById. Audit. (No auth/password — Phase 3.)
- [ ] Tests: email conflict→409, paging, deactivate. Commit: `feat(phase2): CustomerService`.

## Task 14: Application — DiscountService
**Files:** `Application/Discounts/`. Tests.
- [ ] `DiscountService`: Create (code uniqueness→409; Value>0; percentage≤100→400; StartsAt<EndsAt→400), Update, Deactivate, Delete, GetAll, GetById, GetByCode. Audit. (Redemption = Phase 3.)
- [ ] Tests: code conflict→409, percentage>100→400, invalid date range→400. Commit: `feat(phase2): DiscountService`.

## Task 15: Application — ReviewService (moderation)
**Files:** `Application/Reviews/`. Tests.
- [ ] `ReviewService`: Approve, Reject, Delete; GetAll(filter: status/product, paged), GetById. Each moderation transition raises `ReviewModeratedEvent` + audit. (Customer submission = Phase 3; Phase 2 may include an admin/seed create for testing.)
- [ ] Tests: approve/reject transitions + event, paged filter by status. Commit: `feat(phase2): ReviewService moderation`.

## Task 16: Application — WishlistService
**Files:** `Application/Wishlists/`. Tests.
- [ ] `WishlistService`: GetByCustomer (returns wishlist + items with product summary); admin RemoveItem. (Customer add = Phase 3.)
- [ ] Tests: get-by-customer returns items, remove-item. Commit: `feat(phase2): WishlistService`.

## Task 17: API — ApiUrl additions + Categories/Products/Variants controllers
**Files:** modify `Constants/ApiUrl.cs`; create `Controllers/Tenant/{Categories,Products,ProductVariants}Controller.cs`. Read Phase 1 `TenantBankAccountController`/`UsersController` for the thin + attribute pattern.
- [ ] Add nested `ApiUrl` classes: `TenantCategories`, `TenantProducts`, `TenantProductVariants` (route consts).
- [ ] Controllers: thin, `[Authorize(Roles="AdminOwner,StoreManager,ContentManager")]` (Variants also InventoryManager as appropriate), `[EnableRateLimiting("AuthenticatedPolicy")]`, full ProducesResponseType set, `StatusCode(response.StatusCode, response)`. Wire all service methods.
- [ ] `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): Categories, Products, ProductVariants controllers`.

## Task 18: API — ProductImages (multipart) + Inventory controllers
**Files:** `Controllers/Tenant/{ProductImages,Inventory}Controller.cs`; `ApiUrl` additions.
- [ ] `ProductImagesController`: Upload accepts `IFormFile` (`multipart/form-data`); validate content-type (image/*) + size limit; open stream → `ProductImageService.Upload`. Delete/SetPrimary/Reorder/GetByProduct. `[Authorize(Roles="AdminOwner,StoreManager,ContentManager")]`.
- [ ] `InventoryController`: AdjustStock, GetLowStock, GetStockHistory. `[Authorize(Roles="AdminOwner,InventoryManager")]`.
- [ ] Tests where feasible. `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): ProductImages (multipart) + Inventory controllers`.

## Task 19: API — Customers / Discounts / Reviews / Wishlists controllers
**Files:** those 4 controllers; `ApiUrl` additions.
- [ ] Thin controllers wrapping the services; appropriate tenant roles; full attributes. Reviews moderation endpoints `[Authorize(Roles="AdminOwner,StoreManager")]`.
- [ ] `dotnet build` + `dotnet test` clean. Commit: `feat(phase2): Customers, Discounts, Reviews, Wishlists controllers`.

## Task 20: Cross-cutting — tenant-isolation tests + final wiring + review
**Files:** `Infrastructure.Tests` tenant-isolation tests; verify DI graph; verify migration probe empty.
- [ ] Add tenant-isolation tests: with `ICurrentTenantService` set to tenant A, queries return only A's catalog rows; switching tenant changes results (exercise the dynamic query filter for the new entities).
- [ ] Verify all new services/repos/options resolve (DI smoke check or reason about it); confirm `dotnet ef migrations add _probe` is empty then remove.
- [ ] Full `dotnet build` (clean) + `dotnet test` (all green). Commit: `test(phase2): tenant-isolation coverage + final wiring`.
- [ ] Then run the whole-branch review (subagent-driven-development final review) and finishing-a-development-branch.

---

## Self-Review (writing-plans)

- **Spec coverage:** Categories (T2,5,8) · Products (T2,5,9) · Variants (T2,5,10) · Inventory/StockAdjustment (T3,5,11) · Images/Cloudinary (T2,4,5,7,12,18) · Customer (T3,5,13) · Discount (T3,5,14) · Review (T3,5,15) · Wishlist (T3,5,16) · API (T17–19) · multi-tenancy/isolation (T5,20) · conventions (Global Constraints). All spec §4–§9 items mapped.
- **No placeholders:** every task has concrete files, rules, and commit messages; code shown for novel parts (entities, query filter, Cloudinary, cycle/stock rules); repetitive CRUD references the Phase-1 file to mirror (intentional — DRY against an established codebase, not a vague "implement it").
- **Type consistency:** repo method names in T4 match their use in T8–16; enum names (T1) match entity usage (T2–3); `ResponseData`/`PagedResult`/`IReadOnlyList` per conventions throughout.
- **Build order:** Domain → interfaces → EF/migration → repos → Cloudinary → services → controllers → isolation tests, each independently testable and committable.

## Execution Handoff
After approval, implement via **superpowers:subagent-driven-development** (fresh implementer per task + per-task spec/quality review + final whole-branch review), then **finishing-a-development-branch**.
