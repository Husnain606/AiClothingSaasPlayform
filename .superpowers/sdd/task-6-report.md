# Task 6 (Phase 4a): ReportService — 7 Aggregate Queries — Report

**Status:** DONE
**Commit:** `ac30ea3` feat(reports): report service with 7 tenant aggregate queries and range validation
**Tests:** 436/436 passing solution-wide (baseline 420 + 16 new ReportServiceTests), Release build 0 errors, Roslyn diagnostics clean on new files.

## Files

- Created: `src/FashionSaaS.Application/Reports/DTOs/ReportDtos.cs`
- Created: `src/FashionSaaS.Application/Interfaces/IReportRepository.cs`
- Created: `src/FashionSaaS.Application/Reports/ReportService.cs`
- Created: `src/FashionSaaS.Application/Reports/Validators/ReportRangeValidator.cs`
- Created: `src/FashionSaaS.Infrastructure/Persistence/Repositories/ReportRepository.cs`
- Created: `tests/FashionSaaS.Application.Tests/Reports/ReportServiceTests.cs` (16 tests)
- Modified: `src/FashionSaaS.Infrastructure/DependencyInjection.cs` (`IReportRepository → ReportRepository`)
- Modified: `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` (`AddScoped<ReportService>()`)
- Modified: `tests/FashionSaaS.Application.Tests/FashionSaaS.Application.Tests.csproj` (added `Microsoft.EntityFrameworkCore.InMemory 10.0.9` + Infrastructure project reference so the REAL ReportRepository runs over an in-memory ApplicationDbContext)

## Resolved brief ambiguities — actual member names used

- **Review**: approval state is `Review.Status` of enum `ReviewStatus { Pending = 1, Approved = 2, Rejected = 3 }`. PendingReviews = count where `Status == ReviewStatus.Pending` (tenant-wide, not range-filtered — reviews awaiting moderation are a "now" metric).
- **StockAdjustment**: fields are `Delta` (int, signed quantity change), `Reason` (`StockAdjustmentReason` incl. `OrderPlaced = 6`, `OrderCancelled = 7`), `ProductVariantId`, `ResultingQuantity`, and `CreatedAt` (inherited from `BaseEntity`) used as the bucket timestamp.
- **Category**: parent link is `Category.ParentCategoryId` (`Guid?`); name is `Name`.

## InventoryTrends metric choice

`AdjustmentsOverTime` buckets stock-adjustment activity per **day**: `SalesPointDto.OrderCount` = **number of adjustments** in the bucket, `SalesPointDto.Revenue` = **Σ |Delta|** (absolute quantity moved). Both are reported so the dashboard can show either; the choice is documented on the DTO. Test asserts exact values for both.

## SQL-vs-memory aggregation split

- **In SQL** (`AsNoTracking`, minimal projections, explicit `TenantId` predicate on top of the global query filter): all filtering, and the fully-translatable `GroupBy` aggregates — status breakdown, top products (grouped on the `(ProductId, ProductName)` OrderItem snapshot so aggregates survive product edits/deletes), per-customer order counts/spend, category sales join+group.
- **In memory**: only time bucketing. Interval-aware queries project raw `(timestamp, value)` rows in SQL, then one private helper pair (`BucketStart` + `BucketPoints`) buckets and sums in memory, because the Monday-start week formula `date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).Date` is not EF-translatable. Documented trade-off in the repository header: acceptable at current per-tenant row counts and keeps the calendar math in one testable place. Day = calendar date, Week = Monday-start, Month = first of month (UTC).

## Other decisions

- Range guard written once (`ReportRangeValidator.Validate` called from a single private `RunAsync` guard in `ReportService`) and therefore applies to **all 7 methods**; tested via two representative methods (`GetSummaryAsync` from>to, `GetSalesOverTimeAsync` >366 days) plus unresolved-tenant → 400.
- `GetTopProductsAsync` additionally validates `by ∈ {revenue, units}` (case-insensitive) and `take ≥ 1` → 400.
- StatusBreakdown intentionally includes the Cancelled row (composition report); the revenue *metric* everywhere else excludes cancelled orders.
- Category sales: `categoryId == null` → top-level categories (`ParentCategoryId == null`), else direct children; each row counts **direct** products only (no descendant roll-up — documented on `CategorySalesDto`); zero-sales categories are included with 0/0.
- TopCustomers = top 5 by TotalSpend over non-cancelled orders in range, email joined from `Customers`.
- LowStock: `StockQuantity <= 5 && IsActive`, ordered ascending by quantity.

## Test evidence (exact-number assertions)

Fixed two-tenant seed; tenant A: 3 Jan orders (1 cancelled 999), 2 Feb orders, repeat/one-time customers, variants at stock 3 (active), 50 (active), 2 (inactive), pending+approved reviews, 3 stock adjustments. Key exacts: Revenue 500 (cancelled excluded), AOV 125, AOV 0 on empty range, month buckets 150/2 + 350/2, week buckets Monday-start (Wed Jan 7 + Sun Jan 11 → same 2026-01-05 bucket), by-units order (P1 14u) differs from by-revenue order (P2 240), repeat rate exactly 0.5, low-stock excludes inactive variant, drill-down child category 50/1, tenant B context sees only its own 1000/1.

## Concerns

- The EF InMemory provider does not enforce relational semantics; aggregate translation to real SQL Server (decimal sums, `SelectMany` + `Join` + `GroupBy`) should be smoke-checked when Task 7's controller is exercised against a real DB.
- This file previously held the Phase 2 "Catalog Workflow Integration Tests" report (commit 9a43418) — overwritten per Phase 4a task numbering; the old content remains in git history.
