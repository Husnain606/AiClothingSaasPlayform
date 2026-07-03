# Task 4 Report: Customer Store Endpoints (api/store/orders)

## Summary

Implemented the customer-facing Orders API surface consumed by the Phase 3 Angular storefront checkout: `StoreOrdersController` with `Create`, `GetMine`, `GetById`, `Cancel`, backed verbatim by the Task 3 `OrderService`. Build clean, full suite green (419/419), zero regressions.

## Files changed

- Modified: `src/FashionSaaS.API/Constants/ApiUrl.cs` — added `ApiUrl.StoreOrders` (Create, GetMine, GetById, Cancel), matching the brief exactly.
- Created: `src/FashionSaaS.API/Controllers/Store/StoreOrdersController.cs` — primary-constructor controller, `[Authorize(Roles = "Customer")]`, `[EnableRateLimiting("AuthenticatedPolicy")]`, `UserId`/`Email`/`Ip`/`Ua` props matching `ProductsController`'s idiom, all four actions per the brief with `[ProducesResponseType]` on every action, routes exclusively via `ApiUrl` constants, `return StatusCode(response.StatusCode, response)` throughout.
- Modified: `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` — added `public record CancelOrderRequest(string Reason);` once, in the shared DTOs file (not inline in the controller), so Task 5 can reuse it.

No changes were needed to `JwtService.cs` or to role seeding — see resolutions below.

## Binding resolution 1: email claim

**Verified the brief's `ClaimTypes.Email` is correct — no JwtService change needed.**

- `JwtService.GenerateAccessToken` (`src/FashionSaaS.Infrastructure/Services/JwtService.cs:33`) issues the email claim as `new(JwtRegisteredClaimNames.Email, user.Email)`, i.e. the JWT short claim type `"email"`.
- JWT bearer auth is configured in `src/FashionSaaS.API/Extensions/ServiceCollectionExtensions.cs` (`AddJwtAuthentication`) with a plain `TokenValidationParameters` and no `MapInboundClaims = false` anywhere in the codebase (confirmed via repo-wide grep — zero matches).
- Because inbound claim mapping is NOT disabled, `JwtSecurityTokenHandler`'s default inbound claim type map is active, which remaps the `"email"` short claim to `ClaimTypes.Email` (the long URI) on the server's `ClaimsPrincipal` at validation time.
- Therefore `User.FindFirstValue(ClaimTypes.Email)` correctly resolves the authenticated customer's email in the controller. Used as-is, matching the brief and the existing `ClaimTypes.NameIdentifier` convention used across every other controller in the codebase.
- No JwtService modification was made or needed.

## Binding resolution 2: Customer role seeding

**Outcome: already existed. No seeding change, no migration.**

`src/FashionSaaS.Infrastructure/Persistence/Configurations/RoleConfiguration.cs` already seeds all 7 roles including:
```csharp
new Role { Id = Guid.Parse("10000000-0000-0000-0000-000000000007"), Name = RoleType.Customer, Scope = RoleScope.Customer, CreatedAt = seedDate, UpdatedAt = seedDate }
```
`RoleType.Customer` is also already defined in `src/FashionSaaS.Domain/Enums/RoleType.cs` (value 7). This seeding predates Task 4 (already present on the branch). Step 3 of the brief was a no-op — verified, not modified. No `Phase4CustomerRole` migration was created.

## Binding resolution 3: CancelOrderRequest placement

Defined once in `src/FashionSaaS.Application/Orders/DTOs/OrderDtos.cs` as `public record CancelOrderRequest(string Reason);`, not inline in the controller file, so Task 5 (admin-side cancel/ship) can reuse it without duplication.

## Verification

```
dotnet build --configuration Release
  Build succeeded. 0 Errors, 12 Warnings (all pre-existing NU1701 package-compat warnings, unrelated to this change).

dotnet test --configuration Release
  FashionSaaS.Domain.Tests:          24 passed
  FashionSaaS.Application.Tests:    309 passed
  FashionSaaS.Infrastructure.Tests:  86 passed
  Total: 419 passed, 0 failed, 0 skipped — matches expected 419, zero regressions.
```

No Roslyn `get_diagnostics` tool was available in this session's toolset; build-clean (0 errors) was used as the equivalent gate.

## Concerns / follow-ups

- None blocking. Controller-level tests are intentionally out of scope per the brief (service-level coverage from Task 3 is the gate).
- Task 5 will need `ShipOrderRequest` — not created here, out of scope for Task 4.
