# Task 7 Report (Phase 4b — Admin area): Catalog module

> **Note:** this file previously held the stale Phase 2 Task 7 report ("Full Test Suite and
> Release Build Verification", 354 backend tests, dated 2026-06-30). That content is superseded
> and has been overwritten with the Phase 4b Task 7 (catalog module) report below. The old content
> remains in git history.

**Status:** COMPLETE
**Branch:** `feature/phase4b-admin-area` (fashionsaas-storefront)
**Commits:**
- `d6e6b10` — models + `CatalogAdminService` + service spec
- `b924d25` — products/categories/variants/images UI, placeholder removal, StatusBadge extension

## Summary

Implemented Task 7 of the Phase 4b admin-area brief (`.superpowers/sdd/task-7-brief.md`). Before
writing any code, verified the brief's assumed backend contract against the actual .NET source
(`ProductsController`, `CategoriesController`, `ProductVariantsController`,
`ProductImagesController` and their Application-layer DTOs under `src/FashionSaaS.Application/*`)
per the repo's source-of-truth rule (code > docs). **The brief's example code has several real
divergences from the backend it targets** — see Backend-shape surprises below. All product code in
this task follows the verified real contract, not the brief's illustrative snippets.

## Backend-shape surprises (brief vs. verified source)

| Brief assumed | Actual backend (`file:line`) | Fix applied |
|---|---|---|
| Publish/Archive/Deactivate/SetPrimary are `PUT` | All four are `[HttpPost]` (`ProductsController.cs:71,81`, `ProductVariantsController.cs:51`, `ProductImagesController.cs:78`) | Service calls `apiService.post(...)` for these four actions |
| `reorderCategories(orderedIds: string[])` → `{orderedIds}` | `ReorderCategoryRequest` is `{ items: [{ id, sortOrder }] }` (`ReorderCategoryRequest.cs:3-12`) | `reorderCategories(items: CategoryOrderItem[])`; `CategoryTreeComponent.onReorder` computes `sortOrder` from array position |
| `CategoryDto.parentId` | Response DTO field is `ParentCategoryId` (`CategoryResponse.cs:9`); `CreateCategoryRequest`/tree nodes have no flat `parentId` (tree nodes only carry `children`) | Model uses `parentCategoryId`; `CategoryTreeNodeDto` has no parent field |
| `CreateProductRequest { name, description, categoryId, basePrice }` | Real DTO also requires `Slug` and has optional `Tags` (`CreateProductRequest.cs:3-11`) | Added `slug` (required) to the form and request/model; `tags` added as optional (not surfaced in the form UI, out of brief's explicit field list) |
| `ProductStatus = 'draft'\|'published'\|'archived'` | Enum is `Draft=1, Active=2, Archived=3` (`ProductStatus.cs`); JSON serializes as PascalCase strings | Type is `'Draft'\|'Active'\|'Archived'`; list/badge logic updated (`status !== 'Active'` gates Publish, etc.) |
| `reorderImages` body `{orderedIds}` | `ReorderImagesRequest.Ids` (`ReorderImagesRequest.cs:6`) | Body sent as `{ ids: orderedIds }` |
| Image upload form fields unspecified beyond `productId`/`file` | Server binds `[FromForm] UploadImageForm { File, ProductId, VariantId, AltText }` (`ProductImagesController.cs:100-106`) | FormData keys are `File`/`ProductId`/optional `AltText` (PascalCase, matching ASP.NET model binding by convention) |
| `updateVariant` takes `Partial<CreateVariantRequest>` | `UpdateVariantRequest` is a distinct shape: `{ sku, size, color, isActive, priceOverride }` — no `productId`, adds `isActive` | Separate `UpdateVariantRequest` type; `productId` never sent on update |
| Variant `stockQuantity`/plain `price` | Real fields are `Size`, `Color`, `Sku`, `StockQuantity`, `PriceOverride` (nullable) + response-only `EffectivePrice` (`VariantResponse.cs`, `AddVariantRequest.cs`) | Model/table use `priceOverride` (input) + `effectivePrice` (display); no plain `price` field exists |
| Shared kit paths `../../shared/...` from `catalog/` | Actual Task 3 kit lives at `src/app/admin/shared/{components,services,models}` — one level up from where the brief's snippets import it, and `ToastService`/`DataTableComponent` etc. are exactly where Task 6's `orders` module imports them from | Used the real paths (`../../shared/components/...`, `../../shared/services/toast.service`), matching Task 6's `order-list`/`order-detail` convention exactly |

## What was built

- `admin/catalog/models/catalog-admin.model.ts` — DTOs/requests matching verified backend field
  names and casing (`CategoryDto`, `CategoryTreeNodeDto`, `ProductDto`, `ProductSummaryDto`,
  `ProductVariantDto`, `ProductImageDto`, `CreateProductRequest`/`UpdateProductRequest` (same
  shape), `CreateCategoryRequest`, `UpdateCategoryRequest`, `CategoryOrderItem`,
  `CreateVariantRequest`, `UpdateVariantRequest`, `ProductFilter`).
- `admin/catalog/services/catalog-admin.service.ts` (+ spec, 23 tests) — full CRUD surface per
  the brief's public API, wired to the verified real routes/verbs/bodies.
- `admin/catalog/product-list/*` (5 tests) — `DataTableComponent`-backed paged/searchable list;
  `StatusBadgeComponent` for status; publish/archive gated by current status; delete via
  `ConfirmModalComponent`. Extended `StatusBadgeComponent`'s shared color map with
  `draft`/`archived` (lowercased-key lookup already existing) so admin-catalog reuses it without a
  new badge component.
- `admin/catalog/product-form/*` (6 tests) — reactive form (`name`, `slug`, `description`,
  `categoryId`, `basePrice`); create vs. edit mode from the `:id` route param; edit mode embeds
  `VariantTableComponent` and `ImageManagerComponent` for that product. Fixed a real bug present in
  the brief's own example: building `form = this.fb.group(...)` as a field initializer reads `this.fb`
  before the constructor assigns it (`TS2729`, verified via the Angular build failing) — moved form
  construction into the constructor body.
- `admin/catalog/categories/category-tree.component.*` (5 tests) — recursive tree rendering from
  `GET /tenant/categories/tree`; per-node **`<select>`-based move** (accessible, no drag-and-drop,
  per the brief's own rationale) posting `{ newParentId }`; delete; root-category create with a
  slugified name. `onReorder(orderedIds)` recomputes `sortOrder` from array position and posts the
  real `{ items: [{id, sortOrder}] }` shape.
- `admin/catalog/variants/variant-table.component.*` (4 tests) — per-product CRUD table
  (add/deactivate/delete) using the real `CreateVariantRequest` fields (no plain `price`).
- `admin/catalog/images/image-manager.component.*` (5 tests) — multipart upload via `<input
  type="file">`, move-up-based reorder (posts full ordered id list), set-primary, delete.
- `admin/catalog/catalog.routes.ts` — replaced the Task 2 placeholder
  (`catalog-placeholder.component.ts`, **deleted**, confirmed no remaining references) with
  `''` → product list, `'categories'` → category tree, `'new'`/`':id'` → product form, all lazy.
  `admin.routes.ts` already pointed at `catalog.routes.ts` via `loadChildren`; no change needed there.

## Test counts

| File | Tests |
|---|---|
| `catalog-admin.service.spec.ts` | 23 |
| `product-list.component.spec.ts` | 5 |
| `product-form.component.spec.ts` | 6 |
| `category-tree.component.spec.ts` | 5 |
| `variant-table.component.spec.ts` | 4 |
| `image-manager.component.spec.ts` | 5 |
| **Task 7 total** | **48** |

**Full suite:** 628 (baseline) + 48 = **676 tests, 68 files — passed identically on 2 consecutive
`npm run test:ci` runs.**

One test-authoring fix beyond the brief's literal spec: `product-form.component.spec.ts`'s
`provideRouter([])` caused an unhandled `NG04002` rejection in the two tests that call `onSubmit()`
without spying on `Router.navigate` (submit ⇒ real navigation attempt against an empty route table).
Registered a matching dummy route (`{ path: 'admin/catalog', children: [] }`) instead of leaving it
empty — this is a test-infra fix, not a behavior change.

## Build

`npm run build:prod`: initial bundle **608.01 kB raw / 123.97 kB transfer** — under the 620 kB gate.
All new catalog admin components (`product-list-component`, `product-form-component`,
`category-tree-component`, plus variant/image chunks) are lazy chunks, confirmed in the build's lazy
chunk listing; only `admin-routes` (8.56 kB) grew in the eagerly-loaded set, unrelated to catalog.

## Deviations from the brief (beyond backend-shape fixes above)

- Product status literals are `'Draft'|'Active'|'Archived'` (PascalCase, matching the wire enum),
  not the brief's lowercase `'draft'|'published'|'archived'`.
- `ProductDto`/`ProductSummaryDto` split: list view uses the lightweight `ProductSummaryResponse`
  shape (no `description`/`variantCount`/etc.) per the backend's own documented list/detail split;
  the form's edit-mode load uses the full `ProductDto`.
- Added `slug` as a required form field (backend requires it; brief's form omitted it).
- Category move UI uses a `<select>` of all tree nodes per row (brief's own suggested pattern:
  "drag-free move via a parent `<select>` per node to keep interaction accessible and testable").

## Concerns / follow-ups

- `CreateCategoryRequest`/product creation don't auto-generate slugs server-side (verified: no
  slug-generation logic visible in the DTOs); the category tree's root-create does simple
  client-side slugification (`name.toLowerCase().replace(/\s+/g,'-')`) — acceptable for admin-created
  categories but worth a product-slug-collision check if two products share a name (not in scope
  here; the API's `Create`/`Update` presumably validates/rejects duplicates server-side, not
  independently verified).
- No drag-and-drop reordering for categories or images (both use explicit up/select controls per
  the brief's own accessibility-first guidance) — flag if product wants true drag reordering later.

## Fix Round 1 — duplicate product rendering (review finding)

**Bug:** `product-list.component.html` rendered every product **twice** — once inside
`<app-data-table>` (which itself renders a full `<table>` with headers/rows/pagination —
`data-table.component.html:1-59`) and again via a second, hand-rolled `<table>` immediately below
it, iterating the same `rows` array a second time to add Name/Status/Edit/Publish/Archive/Delete
cells. This was a direct copy of a flawed pattern in the Task 7 brief. The existing 5 product-list
tests never caught it because they only asserted on `component.rows`/service-call spies — never on
rendered DOM — so the double-render shipped unnoticed.

**Root cause of the gap:** `DataTableComponent` (`data-table.component.ts`) only renders
`'text'|'currency'|'date'` cell kinds inline; it declared a `'custom'` member of the
`cellTemplate` union type but **never implemented it** — no consumer (including
`order-list`, the "reference" module) had a working per-cell action mechanism. `order-list`
avoids the problem entirely because it only needs one action (row-click → navigate), wired via a
`(click)="handleTableClick($event)"` wrapper around `<app-data-table>` that does `event.target`
delegation — it has no per-row multi-button action cell at all.

**Fix applied:**
1. Implemented the dead `'custom'` cell-template branch in `DataTableComponent` for real, using
   Angular's standard per-column custom-cell idiom: a `@ContentChild('customCell')
   TemplateRef<DataTableCustomCellContext<T>>` input, rendered via `*ngTemplateOutlet` in
   `data-table.component.html` for any column with `cellTemplate: 'custom'`, passing `{ row,
   column }` as the template context. This only activates the already-declared-but-unused type —
   it does not change behavior for `'text'/'currency'/'date'` columns or for `order-list`, which
   uses none of this.
2. `product-list.component.ts`: `status` and the new `id`-keyed `Actions` column are now
   `cellTemplate: 'custom'`; removed nothing else from the column list.
3. `product-list.component.html`: removed the second `<table>` entirely. A single `<app-data-table>`
   now owns all rendering; a projected `<ng-template #customCell let-row="row" let-column="column">`
   switches on `column.key` to render the `StatusBadgeComponent` for the `status` column and the
   Edit/Publish/Archive/Delete controls for the `id` (actions) column — same markup/handlers as
   before, just relocated into the single table's custom-cell slot instead of a duplicate table.
4. `product-list.component.spec.ts`: seeded a **second** product (`product2`, id `p2`) so a
   duplicate-render regression is distinguishable from a coincidental single-row match, and added
   two DOM-level tests:
   - `renders exactly one table row per product (no duplicate rendering)` — queries
     `fixture.nativeElement.querySelectorAll('tbody tr')` and asserts the count equals
     `component.rows.length` (2), not 4.
   - `renders each product name exactly once in the DOM` — counts substring occurrences of each
     product's name in `fixture.nativeElement.textContent` and asserts exactly 1 each (would have
     caught the bug directly: previously each name appeared twice).

**Before/after row count (2 seeded products):** before the fix, `tbody tr` count was **4** (2 from
`app-data-table`'s own table + 2 from the ad-hoc second table); after the fix it is **2** — the DOM
assertion confirms single rendering.

**Verification:**
- `npm run test:ci` — **678 passed, 68 files** — run twice, identical both times (baseline 676 + 2
  new DOM-assertion tests in `product-list.component.spec.ts`).
- `npm run build` — initial bundle **608.01 kB raw / 123.97 kB transfer** — unchanged from the
  pre-fix baseline (no regression from the `DataTableComponent` API addition, since it's additive
  and only activates on `cellTemplate: 'custom'`).

**Commit:** `fix(admin): render products via single DataTable, not duplicated ad-hoc table`
(catalog/product-list scope, plus the DataTable `custom`-cell implementation it depends on in
`shared/components/data-table/`).
