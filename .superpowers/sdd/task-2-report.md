# Task 2 Report (Phase 4a — Orders backend): Order DTOs, Repository, Mapster Profile, Customer Linkage

**Status:** COMPLETE
**Branch:** `feature/phase4a-orders-backend`
**Commit:** `82f94e2` — "feat(orders): DTOs, OrderRepository with paged filtering, customer email linkage, Mapster profile"

## Summary

Implemented Task 2 of the Phase 4a Orders backend per `.superpowers/sdd/task-2-brief.md`, following TDD:

1. **Step 1-2 (failing tests first):** Added `tests/FashionSaaS.Infrastructure.Tests/Repositories/OrderRepositoryTests.cs` (4 tests: `GetByIdWithItemsAsync_ReturnsOrderWithItems`, `GetPagedAsync_FiltersByStatusAndDateAndSearch`, `GetPagedAsync_TenantIsolation_ExcludesOtherTenants`, `CountForYearAsync_CountsOnlyTenantAndYear`) and appended `GetOrCreateByEmailAsync_CreatesThenReuses` to the existing `CustomerRepositoryTests.cs`. Verified compile failure (`IOrderRepository`/`OrderRepository`/`OrderFilter` missing) before implementing.
2. **Step 3 (implementation):**
   - `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` — all DTOs/filter verbatim from the brief (`ShippingAddressDto`, `CreateOrderItemRequest`, `OrderVariantDto`, `CreateOrderPaymentDto`, `CreateOrderRequest`, `OrderItemDto`, `OrderDto`, `OrderFilter`).
   - `src/FashionSaaS.Application/Interfaces/IOrderRepository.cs` — new interface, verbatim signatures from brief. Kept as a standalone interface (NOT extending `IGenericRepository<Order>`) since the brief's exact signature set doesn't match the generic base's shape, and the brief explicitly instructs implementing it exactly as shown.
   - `src/FashionSaaS.Application/Interfaces/ICustomerRepository.cs` — added `GetOrCreateByEmailAsync(Guid tenantId, string email, string firstName, string lastName, string? phone, CancellationToken ct = default)`.
   - `src/FashionSaaS.Infrastructure/Persistence/Repositories/OrderRepository.cs` — new plain class `OrderRepository(ApplicationDbContext context) : IOrderRepository`, implemented exactly per brief (AddAsync, GetByIdWithItemsAsync with Include(Items), GetPagedAsync with tenant/status/date-range/customer/search filters + paging, CountForYearAsync by tenant+year).
   - `src/FashionSaaS.Infrastructure/Persistence/Repositories/CustomerRepository.cs` — added `GetOrCreateByEmailAsync`, adapted to the file's existing pattern: uses the protected `DbSet` field (inherited from `GenericRepository<Customer>`) rather than a raw `context` field, since that's what this file already uses throughout (`DbSet.AnyAsync`, etc.).
   - `src/FashionSaaS.Application/Orders/Mappings/OrderMappings.cs` — `OrderMappings : IRegister`, mapping `Order -> OrderDto` (OrderId from OrderNumber, Status lowercased, flattened Shipping* fields projected into `ShippingAddressDto`) and `OrderItem -> OrderItemDto` (Price from UnitPrice, Variant null-collapsed when no variant/size/color data present). No manual registration needed — `MappingConfiguration.GetMappingConfig()` already calls `config.Scan(Assembly.GetExecutingAssembly())`, which picks up any `IRegister` implementation in the Application assembly.
   - `src/FashionSaaS.Infrastructure/DependencyInjection.cs` — registered `services.AddScoped<IOrderRepository, OrderRepository>();` next to the other Phase 2 catalog repositories.
3. **Step 4 (verify pass):** Targeted filter run passed 11/11 (`OrderRepositoryTests` + `CustomerRepositoryTests`). Full solution `dotnet test --configuration Release` passed 383/383 across all three test projects (Domain.Tests 24, Application.Tests 274, Infrastructure.Tests 85). Zero regressions.
4. **Step 5 (commit):** Committed as instructed, scoped to `src/FashionSaaS.Application`, `src/FashionSaaS.Infrastructure`, `tests/FashionSaaS.Infrastructure.Tests`.

## Ambiguity Resolutions Applied

- **CustomerRepository field name:** File uses `GenericRepository<Customer>` base with protected `DbSet`/`Context` fields (not a raw `context` parameter name) — `GetOrCreateByEmailAsync` was written using `DbSet` to match every other method in that file (`EmailExistsAsync`, `GetPagedAsync`).
- **IOrderRepository / OrderRepository base:** All existing catalog repositories (Discount, Category, Customer, etc.) extend `GenericRepository<T> : IGenericRepository<T>`, but the brief's `IOrderRepository` is a standalone interface not extending `IGenericRepository<Order>`, and its `AddAsync(Order order)` has no CancellationToken (matching `IGenericRepository<T>.AddAsync` shape coincidentally but not by inheritance). Implemented `OrderRepository` as a plain class per the brief's exact code sample — this is Task 3-7's contract, so no deviation was made.
- **CustomerRepositoryTests:** File already existed; new test appended at the end rather than creating a new file.
- **Tenant isolation test:** Kept exactly as specified; the global EF query filter (via `ICurrentTenantService`) plus the explicit `filter.TenantId` predicate in `GetPagedAsync` both contribute to hiding the other tenant's seeded order — test passes with `TotalCount == 0` either way.

## Verification Evidence

- `mcp__cwm-roslyn-navigator__get_diagnostics` (scope: solution, severity: error) → 0 diagnostics after implementation.
- `dotnet test tests/FashionSaaS.Infrastructure.Tests --filter "FullyQualifiedName~OrderRepositoryTests|FullyQualifiedName~CustomerRepositoryTests"` → Passed: 11, Failed: 0.
- `dotnet test --configuration Release` (full solution) → Passed: 383 (24 + 274 + 85), Failed: 0, Skipped: 0.

## Concerns / Notes for Downstream Tasks

- `IOrderRepository` does not extend `IGenericRepository<Order>`, so no `GetByIdAsync`/`GetAllAsync`/`UpdateAsync`/`DeleteAsync`/spec-based query methods are available on it. If Task 3+ needs an update/delete path for `Order` (e.g., status transitions, tracking number updates), it will need EF change-tracking directly via `GetByIdWithItemsAsync` + `SaveChangesAsync` through `IUnitOfWork`, or the interface will need to be extended in a later task — flagging this now since brief interfaces are consumed verbatim by Tasks 3-7.
- `OrderMappings`' `Variant` null-collapse checks `ProductVariantId == null && Size == "" && Color == ""` — this exactly matches the brief's given logic; note it requires all three conditions true to collapse to null (any one populated field keeps the Variant object).
- Pre-existing untracked `.superpowers/sdd/phase4a-progress.md` and `task-1-report.md` had unstaged modifications from outside this task's scope (likely touched by a concurrent/prior process) — left untouched and unstaged, not included in this commit.
