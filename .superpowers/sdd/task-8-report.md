# Task 8 Report: Routing Configuration & Layout

**Status:** COMPLETE
**Commit:** `dac1e59` (fashionsaas-storefront repo, on top of base `c8eb6b7`)
**Date:** 2026-07-02

## Files Created

- `src/app/layouts/main-layout/main-layout.component.{ts,html,scss,spec.ts}` — header + outlet + footer, flex-column `min-height: 100vh` sticky-footer shell (min-height, not height, so no nested-scroll trap)
- `src/app/layouts/auth-layout/auth-layout.component.{ts,html,scss,spec.ts}` — minimal centered card shell (max-width 480px), brand link back to `/`, no header/footer
- `src/app/shared/components/not-found/not-found.component.{ts,html,scss,spec.ts}` — large friendly 404 with "Back to shopping" CTA button linking to `/products`
- `src/app/app.routes.spec.ts` — route configuration + navigation harness tests

## Files Modified

- `src/app/app.routes.ts` — restructured with parent layout routes, all routes lazy, titles added
- `src/app/app.html` — reduced to `<router-outlet />` (removed ~340 lines of Angular scaffold)
- `src/app/app.ts` — removed unused `title` signal
- `src/app/app.spec.ts` — updated to test the new minimal root shell (old spec asserted scaffold "Hello" heading)
- `src/app/shared/index.ts` — exported `NotFoundComponent` from barrel

## Route Table

| Path | Layout | Component | Guards | Lazy | Title |
|---|---|---|---|---|---|
| `''` | MainLayout | redirect → `products` (full) | — | — | — |
| `products` | MainLayout | CatalogComponent | authGuard | yes | Products \| FashionSaaS |
| `products/:id` | MainLayout | ProductDetailComponent | authGuard | yes | Product Details \| FashionSaaS |
| `cart` | MainLayout | CartComponent | authGuard | yes | Shopping Cart \| FashionSaaS |
| `checkout` | MainLayout | CheckoutComponent | authGuard, cartNotEmptyGuard | yes | Checkout \| FashionSaaS |
| `account` | MainLayout | AccountComponent | authGuard | yes | My Account \| FashionSaaS |
| `login` | AuthLayout | LoginComponent | — | yes | Sign In \| FashionSaaS |
| `register` | AuthLayout | RegisterComponent | — | yes | Create Account \| FashionSaaS |
| `**` | — | NotFoundComponent | — | no | Page Not Found \| FashionSaaS |

All pre-existing guards preserved exactly (`''` redirect kept pointing at `products` — the actual catalog path in this app, matching header links and the authGuard/cartNotEmptyGuard redirect targets).

## Tests

- New tests: 26 (MainLayout 6, AuthLayout 5, NotFound 4, route config/navigation 11) — plus rewrote the 2 stale `App` root specs
- Route spec covers: empty-path redirect, layout mounting, authGuard on products/cart/account, both guards on checkout (exact order), no guards on login/register, all 7 `loadComponent` functions resolve to component classes, titles on every navigable route, wildcard is last and maps to NotFoundComponent, and a `RouterTestingHarness` navigation test rendering NotFoundComponent for an unknown URL
- Full suite: **309 passed / 40 failed** (baseline was 282 passed / 41 failed — all remaining failures are the known pre-existing ones in `src/app/features/**`; no new failures, one baseline test is flaky between 40/41)
- Vitest syntax only, `TestBed.resetTestingModule()` convention followed, no fakeAsync/zone APIs

## Build

`npm run build` succeeds. Bonus: lazy loading dropped the initial bundle from ~698 kB to **593.22 kB** (budget warning still present but reduced by ~105 kB). 7 lazy chunks emitted (catalog, product-detail, cart, checkout, account, login, register).
