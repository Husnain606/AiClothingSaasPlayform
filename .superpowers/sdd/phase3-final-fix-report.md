# Phase 3 Final Fix: Wire Catalog Add-to-Cart to CartService

## Bug

Both catalog entry points to the primary shopping flow were no-ops. Each left a
`// TODO: Connect to CartService in Task 4` stub that only did
`console.log(...)` / `alert(...)` without ever calling `CartService.addItem`.

- `src/app/features/catalog/components/catalog/catalog.component.ts` — grid `onAddToCart(product)`
- `src/app/features/catalog/components/product-detail/product-detail.component.ts` — `addToCart()`

## Changes

### `catalog.component.ts`
- Imported and injected `CartService` (`../../../cart/services/cart.service`).
- `onAddToCart(product)` now calls `cartService.addItem(product, 1)` (no variant
  from the grid), piped through the existing `takeUntil(this.destroy$)` pattern.
- On success: shows a success `alert()` (matches existing UX style — no toast
  system introduced). On error: logs via `console.error` and shows a
  failure `alert()`. Removed the TODO and console.log stub.

### `product-detail.component.ts`
- Imported and injected `CartService`.
- `addToCart()` now calls
  `cartService.addItem(product, qty, variant ? { size: variant.size, color: variant.color } : undefined)`,
  reusing the existing `product`, `variant`, `qty` locals and the pre-existing
  validation (`product` must exist; a variant is required if
  `product.variantCount > 0`). Piped through `takeUntil(this.destroy$)`.
- On success: navigates to `/cart` via the already-injected `Router` (mirrors
  the wishlist component's established pattern). On error: logs via
  `console.error` and sets `error$` with a user-facing message (consistent
  with the component's existing error-state UI). Removed the TODO and
  console.log stub.

### Tests
- `catalog.component.spec.ts`: mocked `CartService.addItem` with
  `vi.fn().mockReturnValue(of(mockCart))`; updated the existing add-to-cart
  test to assert `addItem` is called with `(product, 1)`; added an error-path
  test asserting the failure alert and `console.error` call.
- `product-detail.component.spec.ts`: mocked `CartService.addItem` similarly;
  updated the existing add-to-cart test to assert `addItem` is called with
  `(product, qty, { size, color })` and that `router.navigate(['/cart'])`
  fires. Added tests for: no-variant product (`addItem` called with
  `undefined` variant), error path (`error$` set, no navigation), missing
  product (`addItem` not called), and required-but-missing variant (`addItem`
  not called).

## Test counts

- Before: 493 tests passing (per task brief)
- After: **498 tests passing** (42 test files), 0 failing — `npm run test:ci`

## Gate results

```
npm run test:ci     -> 42 test files passed (42), 498 tests passed (498)
npm run build:prod  -> Application bundle generation complete, no errors/warnings
```

## Commit

`ba9593e` — `fix(catalog): wire add-to-cart to CartService from grid and product detail`

(committed inside the `fashionsaas-storefront` git submodule, branch `master`)
