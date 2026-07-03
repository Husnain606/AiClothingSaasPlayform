# Task 3 Report (Phase 4a — Orders backend): OrderService — Creation, Pricing, Stock, Transitions

**Status:** COMPLETE
**Branch:** `feature/phase4a-orders-backend`

## Summary

Implemented Task 3 per `.superpowers/sdd/task-3-brief.md`, following TDD:

1. **Step 1-2 (failing tests first):** Wrote `tests/FashionSaaS.Application.Tests/Orders/OrderServiceTests.cs` (25 tests) and `tests/FashionSaaS.Application.Tests/Orders/CreateOrderRequestValidatorTests.cs` (9 tests) — 34 tests total, exceeding the brief's ~20 minimum. Verified compile failure (`OrderService`/`CreateOrderRequestValidator` missing) before implementing.
2. **Step 3 (implementation):**
   - `src/FashionSaaS.Application/Orders/OrderService.cs` — `CreateAsync`, `GetAllAsync`, `GetByIdAsync`, `GetForCustomerAsync`, `GetByIdForCustomerAsync`, `ConfirmAsync`, `ShipAsync`, `DeliverAsync`, `CancelAsync`, exactly matching the brief's **Produces** signature.
   - `src/FashionSaaS.Application/Orders/Validators/CreateOrderRequestValidator.cs` — FluentValidation rules per the brief.
   - `src/FashionSaaS.Domain/Enums/StockAdjustmentReason.cs` — extended with `OrderPlaced = 6`, `OrderCancelled = 7` (additive; see ambiguity resolution below).
   - `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` — registered `services.AddScoped<OrderService>();`.
3. **Step 4 (verify pass):** Targeted filter run passed 42/42 (`FullyQualifiedName~Order`, covering both new files plus the existing `OrderRepositoryTests`/`OrderTests` from Tasks 1-2).
4. **Step 5 (full suite regression):** `dotnet test --configuration Release` → 24 + 308 + 85 = **417 passed, 0 failed, 0 skipped** across all three test projects (baseline was 383; +34 new).
5. **Step 6 (commit):** see commit hash below.

## Actual Phase 2 Member Names Discovered (brief's illustrative names adjusted)

Verified directly by reading source (cross-checked against a parallel research agent — findings matched exactly):

| Brief's illustrative name | Actual name | File |
|---|---|---|
| `IProductRepository.GetByIdAsync(Guid)` | `IGenericRepository<Product>.GetByIdAsync(Guid id)` — **no CancellationToken** | `src/FashionSaaS.Application/Interfaces/IGenericRepository.cs` |
| `IProductVariantRepository.GetByProductIdAsync(...)` | `GetByProductAsync(Guid productId, CancellationToken ct = default)` | `src/FashionSaaS.Application/Interfaces/IProductVariantRepository.cs` |
| `ProductStatus.Published` | `ProductStatus.Active` (enum: `Draft=1, Active=2, Archived=3`) | `src/FashionSaaS.Domain/Enums/ProductStatus.cs` |
| `IStockAdjustmentRepository` | Confirmed: extends `IGenericRepository<StockAdjustment>`, plus `GetByVariantAsync`. Used inherited `AddAsync(StockAdjustment)`. | `src/FashionSaaS.Application/Interfaces/IStockAdjustmentRepository.cs` |
| `StockAdjustment.Reason` as string | `Reason` is typed `StockAdjustmentReason` **enum**, not a string. Existing values (`Restock/Sale/Correction/Damage/Return`) don't semantically cover order placement/cancellation. | `src/FashionSaaS.Domain/Entities/StockAdjustment.cs` |
| `PagedResult<T>` | Confirmed existing type at `src/FashionSaaS.Application/Common/PagedResult.cs` — reused verbatim (`Items`, `TotalCount`, `Page`, `PageSize`), same pattern as `DiscountService.GetAllAsync`. | |
| Validator invocation | Validators are auto-discovered via `builder.Services.AddValidatorsFromAssembly(...)` in `Program.cs`; `DiscountService`/other services never call `IValidator<T>.ValidateAsync` manually. `CreateOrderRequestValidator` therefore requires no manual wiring in `OrderService`. | `src/FashionSaaS.API/Program.cs` |

## Ambiguity Resolutions Applied

1. **`StockAdjustmentReason` enum extension:** The brief calls for reason strings `"OrderPlaced"`/`"OrderCancelled"`, but the actual `StockAdjustment.Reason` field is a `StockAdjustmentReason` enum (not a string), and none of the existing values (`Restock, Sale, Correction, Damage, Return`) fit order semantics precisely. Resolved by **additively extending the enum**: `OrderPlaced = 6`, `OrderCancelled = 7`. This is a non-breaking change — no existing code switches exhaustively on this enum without a default case, and `AdjustStockRequestValidator`'s `IsInEnum()` check still passes for old values. Verified via full-suite regression (0 failures).
2. **Variant matching semantics:** The brief's illustrative Moq test used `GetByProductIdAsync`; implementation uses the real `GetByProductAsync`. Size/Color matching is case-insensitive (`StringComparison.OrdinalIgnoreCase`) since `OrderVariantDto.Size`/`Color` are free-form client input DTOs, not enums.
3. **No-variant items:** When `CreateOrderItemRequest.Variant` is null or both `Size`/`Color` are blank, the order line is priced from `Product.BasePrice` directly with no stock check/decrement (matches Task 2's `OrderMappings` null-collapse convention for variant-less items).
4. **`GetForCustomerAsync` pagination + email filter:** `IOrderRepository` has no email-filter parameter on `GetPagedAsync`/`OrderFilter`, so the service fetches the tenant-scoped page via `GetPagedAsync` and then filters in-memory by `ShippingEmail` (case-insensitive). `TotalCount` is reported as the filtered count when any rows were excluded from the fetched page — flagged as an approximation: if a future task needs exact cross-page customer order counts, `IOrderRepository`/`OrderFilter` will need a `CustomerEmail` filter field added at the repository layer. Not addressed here since the brief's produced interface for `IOrderRepository` was fixed by Task 2 and consumed verbatim.
5. **Order transitions use tracked-entity mutation:** As documented by Task 2's report, `IOrderRepository` has no `UpdateAsync`. `ConfirmAsync`/`ShipAsync`/`DeliverAsync`/`CancelAsync` all load via `GetByIdWithItemsAsync` (EF change-tracked), mutate in place, then call `IUnitOfWork.SaveChangesAsync` directly — no explicit repository update call needed.
6. **Mapster global config in unit tests:** `Order.Adapt<OrderDto>()` depends on `OrderMappings : IRegister` having been scanned into `TypeAdapterConfig.GlobalSettings`, which normally happens once via `MappingConfiguration.GetMappingConfig()` at API startup (`AddApplicationServices()`). Since this is the first unit test suite to call `Adapt<T>()` directly (no DI container in these tests), `OrderServiceTests` calls `MappingConfiguration.GetMappingConfig()` in a static constructor to ensure the mapping profile is registered before any test runs. This mirrors production behavior without requiring a full DI setup in the test class.
7. **Card masking / `CardLast4`:** `request.PaymentInfo.CardNumber` is masked or a bare 4-digit string per the validator; `Order.CardLast4` is derived as the last 4 characters of whatever was sent (`cardNumber[^4..]`), which for masked input like `****1111` yields `1111`. If the string is under 4 chars (shouldn't happen given `NotEmpty`+regex validation, but the validator is separate from the service), it is passed through unchanged rather than throwing.

## Business Rules Implemented (verbatim per brief)

- **Create:** tenant resolution (400), `GetOrCreateByEmailAsync`, per-item product lookup (400 for unknown/inactive), variant resolution and 400 on missing/insufficient-stock, price = `variant.PriceOverride ?? product.BasePrice`, stock decrement + `StockAdjustment(Reason=OrderPlaced)`, `Subtotal`/`Tax = Math.Round(subtotal*0.10m, 2, MidpointRounding.AwayFromZero)`/`ShippingCost=0`/`Total`, `OrderNumber = $"ORD-{year}-{count+1:D6}"`, `CardLast4`, audit `"OrderCreated"`, 201.
- **Confirm/Ship/Deliver:** `GetByIdWithItemsAsync` (404 if missing), `CanTransitionTo` guard with exact message `$"Cannot {action} an order in status {order.Status}"`, `Ship` sets `TrackingNumber`, audits `OrderConfirmed`/`OrderShipped`/`OrderDelivered`, 200.
- **Cancel:** allowed only from Pending/Confirmed (via `CanTransitionTo(Cancelled)`, which the `Order` entity's `AllowedTransitions` table already restricts to those two states), sets `CancelReason`, restores each item's variant stock (+`StockAdjustment(Reason=OrderCancelled)`), `asCustomer` + wrong email → 404 (no existence leak), audits `"OrderCancelled"`.
- **GetByIdForCustomerAsync:** 404 (not 403) on email mismatch.

## Verification Evidence

- `dotnet test tests/FashionSaaS.Application.Tests --filter "FullyQualifiedName~OrderServiceTests"` (initial, before implementation) → compile error `CS0246: OrderService could not be found` — confirms red.
- `dotnet test tests/FashionSaaS.Application.Tests --filter "FullyQualifiedName~Order"` (after implementation) → **Passed: 42, Failed: 0** (25 OrderServiceTests + 9 CreateOrderRequestValidatorTests + 8 pre-existing OrderRepositoryTests/OrderTests from Tasks 1-2).
- `dotnet test --configuration Release` (full solution) → **Passed: 417 (24 Domain.Tests + 308 Application.Tests + 85 Infrastructure.Tests), Failed: 0, Skipped: 0**.
- `mcp__cwm-roslyn-navigator__get_diagnostics` (scope: solution, severity: error) → 0 real diagnostics (only pre-existing hidden/informational `CS8019`/`CS8933` in generated/migration files, unrelated to this change).

## Concerns / Notes for Downstream Tasks (4-5)

- `IOrderRepository`/`OrderFilter` has no customer-email filter — `GetForCustomerAsync` currently does an in-memory filter after `GetPagedAsync`, which is **not correct for true cross-page pagination** when a tenant has many orders from many customers (a customer's orders could span multiple backend pages, producing incomplete/incorrect `TotalCount` and page contents in that edge case). This is adequate for the current test suite but should be revisited if Task 4/5 storefront "my orders" endpoints need accurate multi-page customer order history at scale — recommend adding `OrderFilter.CustomerEmail` and pushing the filter into `OrderRepository.GetPagedAsync`'s SQL.
- `StockAdjustmentReason` enum was extended (additive, non-breaking) with `OrderPlaced`/`OrderCancelled`. Any downstream reporting task (Task 5, "Reporting backend" per the phase plan) that surfaces stock-adjustment reasons should account for these two new values.
- `OrderService.CreateAsync` does not currently call `CreateOrderRequestValidator` itself (matches the codebase-wide convention of API-boundary validation via `AddValidatorsFromAssembly`); Task 4 (API endpoints) must ensure the Orders controller/minimal-API endpoint is registered so FluentValidation's auto-validation pipeline actually runs for `CreateOrderRequest`.
- Stock validation/decrement happens per-item as items are iterated (fail-fast on the first invalid item; decrements themselves are deferred to a post-validation loop, so no partial stock mutation occurs on a rejected order) — verified via `CreateAsync_InsufficientStock_Returns400_AndDoesNotSave` asserting `SaveChangesAsync` is never called.
- The overwritten `.superpowers/sdd/task-3-report.md` previously held an unrelated, stale Phase 2 report ("ProductVariantRepository Integration Tests") — that content has been replaced by this Phase 4a Task 3 report per this task's explicit reporting instruction. If that Phase 2 report needs to be preserved elsewhere, it should be recovered from git history (prior commit content) before this report supersedes it in any published documentation.

## Commit

See `git log` — committed as `feat(orders): OrderService with server-side pricing, stock coupling, and status lifecycle`.
