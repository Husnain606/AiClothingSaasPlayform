# Task 1 Report: Order Domain — Entities, Enum, EF Configuration, Migration

**Status: DONE**
**Branch:** feature/phase4a-orders-backend
**Commit:** 2ec858d — "feat(orders): Order/OrderItem domain, status lifecycle, EF config, Phase4Orders migration"

## Files Created

- `src/FashionSaaS.Domain/Enums/OrderStatus.cs` — enum: Pending(0), Confirmed(1), Shipped(2), Delivered(3), Cancelled(4)
- `src/FashionSaaS.Domain/Entities/Order.cs` — Order entity with flattened shipping snapshot, money fields, `CanTransitionTo` lifecycle guard
- `src/FashionSaaS.Domain/Entities/OrderItem.cs` — OrderItem entity with product/variant snapshot fields
- `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderConfiguration.cs` — EF config (max lengths, decimal precision, indexes, FKs)
- `src/FashionSaaS.Infrastructure/Persistence/Configurations/OrderItemConfiguration.cs` — EF config (max lengths, decimal precision, indexes)
- `src/FashionSaaS.Infrastructure/Persistence/Migrations/20260703101408_Phase4Orders.cs` + `.Designer.cs` — new migration
- `tests/FashionSaaS.Domain.Tests/Entities/OrderTests.cs` — 12 tests (11 theory cases + 1 default-status fact)

## Files Modified

- `src/FashionSaaS.Infrastructure/Persistence/ApplicationDbContext.cs` — added `DbSet<Order> Orders`, `DbSet<OrderItem> OrderItems`, and tenant query filter for `Order` mirroring the exact existing pattern (`o.TenantId == currentTenantService.TenantId`, referencing the injected `currentTenantService` primary-constructor parameter, not a captured local or field — this codebase has no `_currentTenant` field name; the parameter is used directly, consistent with every other filter in the file). OrderItem intentionally has no filter of its own — reached only through Order, per brief guidance.
- `src/FashionSaaS.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — auto-updated by `dotnet ef migrations add`.

## TDD Sequence

1. Wrote `OrderTests.cs` verbatim from brief.
2. Ran `dotnet test tests/FashionSaaS.Domain.Tests --filter "FullyQualifiedName~OrderTests"` → compile error (CS0246/CS0103, `Order`/`OrderStatus` not found) — confirmed failing as expected.
3. Implemented `OrderStatus`, `Order`, `OrderItem` verbatim from brief.
4. Reran same filter → **12/12 passed**.
5. Added EF configurations and DbContext registration.
6. `dotnet build --configuration Release` → succeeded (only pre-existing NU1701 NuGet framework-compat warnings, unrelated to this task).
7. `dotnet ef migrations add Phase4Orders --startup-project src/FashionSaaS.API --project src/FashionSaaS.Infrastructure` → migration created (dotnet-ef 10.0.5 already installed as local tool; no install step needed).
8. `dotnet test --configuration Release` (full suite) → **378/378 passed** (24 Domain + 274 Application + 80 Infrastructure = 378; brief's baseline of 366 + 12 new = 378, confirmed exact match).
9. Committed.

## Migration Details

**Name:** `Phase4Orders` (`20260703101408_Phase4Orders`)

**Tables created:**
- `Orders` — Id (PK), TenantId, CustomerId, OrderNumber, Status (int), OrderDate, 9 shipping snapshot fields, CardLast4, Subtotal/Tax/ShippingCost/Total (all `decimal(18,2)`), TrackingNumber (nullable), CancelReason (nullable), CreatedAt, UpdatedAt. FK `Orders.CustomerId → Customers.Id` (Restrict).
- `OrderItems` — Id (PK), OrderId, ProductId, ProductVariantId (nullable), ProductName, Size, Color, UnitPrice (`decimal(18,2)`), Quantity, CreatedAt, UpdatedAt. FK `OrderItems.OrderId → Orders.Id` (Cascade).

**Indexes created:**
- `IX_Orders_TenantId_OrderNumber` (unique)
- `IX_Orders_TenantId_OrderDate`
- `IX_Orders_TenantId_Status`
- `IX_Orders_TenantId_CustomerId`
- `IX_Orders_CustomerId` (auto, FK)
- `IX_OrderItems_OrderId`
- `IX_OrderItems_ProductId`

All four Order indexes specified in the brief are present.

## Verification

- **Roslyn diagnostics** (`get_diagnostics`, scope=solution, severityFilter=all): 103 results, **all "hidden" severity** (unused-using / duplicate-global-using notices in generated `obj/` files and EF migration boilerplate) — **zero errors, zero warnings** in project source.
- **Build:** Release build succeeds; only pre-existing `NU1701` NuGet compat warnings (Base32, OtpSharp packages — present before this task, unrelated).
- **EF model warning:** `dotnet ef` console output shows: `Entity 'Order' has a global query filter defined and is the required end of a relationship with the entity 'OrderItem'...`. Verified this is **pre-existing, expected behavior** in this codebase — the identical warning class already fires for `Customer`↔`Order` and (confirmed by reproducing on a clean stash) for the Phase 2 catalog entities (e.g., `Product`↔`ProductVariant`). Not a regression; consistent with the established tenant-filter pattern where required relationships between filtered entities intentionally don't get matching child filters unless the brief calls for it (it explicitly said OrderItem needs none).
- **Full test suite:** `dotnet test --configuration Release` → 24 (Domain) + 274 (Application) + 80 (Infrastructure) = **378 passed, 0 failed, 0 skipped**.

## Self-Review Notes

- Implemented exactly per brief's verbatim code blocks — no deviations to entity/enum/config shapes.
- Confirmed field-naming assumption in the brief's Step 5 caveat was moot: the existing DbContext uses the injected primary-constructor parameter `currentTenantService` directly in every filter (no `_currentTenant` field exists anywhere in the file), so `Order`'s filter was written the same way for consistency.
- A `git stash`/`dotnet ef migrations add TempCheck` diagnostic detour (used to confirm the required-relationship EF warning is pre-existing, not a regression) briefly diverged the working tree; recovered cleanly via `git checkout --` on the snapshot file plus `git stash pop`, verified DbContext and snapshot content post-recovery before proceeding. No stray migrations or duplicate entries remain (`dotnet ef migrations list` shows exactly one `Phase4Orders` entry, correctly ordered after `Phase2Catalog`).
- Untracked `src/FashionSaaS.API/logs/` (Serilog output from running `dotnet ef` commands) was left unstaged/uncommitted — not part of this task's file set.
- Note: this report file previously contained stale content from an unrelated Phase 3 (Angular storefront) task 1; it has been fully overwritten with this task's report.
