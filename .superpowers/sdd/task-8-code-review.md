# Task 8 Code Review: Routing Configuration & Layout

**Commit reviewed:** `dac1e59` (fashionsaas-storefront)
**Reviewer verdict:** APPROVED (with one Important follow-up finding, pre-existing but newly user-visible)

## Verification performed

- Diffed `git show c8eb6b7:src/app/app.routes.ts` against `dac1e59` version line by line.
- Grepped all `router.navigate` / `routerLink` / `navigateByUrl` targets across `src/`.
- Read all new layout/404 components, templates, styles, and specs; read `app.routes.spec.ts` in full.
- Ran `npm test -- --watch=false`: **308 passed / 41 failed** (report claims 309/40; report itself notes one flaky baseline test oscillating between 40/41 — confirmed). Every failing spec file is in `src/app/features/**` (order.service, cart-summary, product.service, checkout-review, cart-list, product-list, register, login, account-state, product-detail, cart.service) — the known pre-existing set. No new failures.
- Ran `npm run build`: success, initial bundle **593.22 kB** (budget warning pre-existing, reduced ~105 kB by lazy loading), 7 lazy chunks emitted as claimed.

## Correctness — guard/route preservation: PASS

Old config → new config mapping, verified exact:

| Route | Old guards | New guards | Status |
|---|---|---|---|
| `products` | authGuard | authGuard | preserved |
| `products/:id` | authGuard | authGuard | preserved |
| `account` | authGuard | authGuard | preserved |
| `cart` | authGuard | authGuard | preserved |
| `checkout` | authGuard, cartNotEmptyGuard | authGuard, cartNotEmptyGuard (same order) | preserved |
| `login`, `register` | none | none (under AuthLayout) | preserved |
| `''` | redirectTo `/products`, pathMatch full | redirectTo `products` (child of MainLayout), pathMatch full | preserved |

- No route lost, no path renamed. Wildcard `**` is the last entry of the top-level array; root redirect is `pathMatch: 'full'`.
- All 7 `loadComponent` imports use correct named exports (`.then(m => m.XComponent)`); route spec resolves all 7 at runtime and build emits 7 chunks — no default-vs-named mismatch.
- Every in-code navigation target resolves: `/products`, `/products/:id`, `/cart`, `/checkout`, `/account`, `/login`, `/register`, `/` — all live. authGuard's `/login` redirect and cartNotEmptyGuard's `/products` redirect both still resolve.
- `/login` for already-authenticated users: unguarded, same as before Task 8 — no regression.

## Findings

### Important

1. **`src/app/shared/components/header/header.component.html:6,30,90,98,106` — header navigates to dead routes.** Brand logo → `/home`, Catalog nav link → `/catalog`, account dropdown → `/account/profile`, `/account/orders`, `/account/addresses`. None of these paths exist in the route config (only `/products` and `/account` do), so all five now land on the 404 page. These links are pre-existing (header predates Task 8 and was never mounted — old `app.html` was Angular scaffold), so Task 8 did not break them, but Task 8's MainLayout is what makes the header live, turning latent dead links into user-visible primary-nav 404s. Also contradicts the implementation report's claim that the `products` redirect "match[es] header links". Should be fixed in the header (or child routes added) in a follow-up task — likely Task 9 (header integration) territory, but must not be forgotten.

### Minor (no action required)

2. `src/app/layouts/auth-layout/auth-layout.component.ts:2` — imports both `RouterOutlet` and `RouterModule`; `RouterModule` alone already provides the outlet directive. Harmless redundancy.
3. The wildcard 404 renders outside both layouts (no header/footer). Matches the report's route table and spec's minimal-friendly-404 intent; noting it as a deliberate choice, not a defect.
4. Footer links are `href="#"` placeholders — pre-existing, out of scope.

## Quality — PASS

- Both layouts and NotFound are standalone, logic-free (empty classes), no `any` anywhere in the diff.
- Sticky footer verified in code: `.app-shell { min-height: 100vh }` + `d-flex flex-column` + `flex-grow-1` on `<main>`. `min-height` (not `height`) avoids the double-scroll trap. Spec tests assert the flex classes.
- App root is a bare `<router-outlet />`; `app.ts` stripped of the unused `title` signal; `app.spec.ts` rewritten to assert the outlet-only shell and absence of scaffold `<h1>` — meaningful, not padding.
- `NotFoundComponent` exported from the shared barrel (`src/app/shared/index.ts`).

## Tests — PASS

`src/app/app.routes.spec.ts` (11 tests) asserts real invariants: guard identity via `toContain(authGuard)` and exact `toEqual([authGuard, cartNotEmptyGuard])` order on checkout, `canActivate` undefined on login/register, `loadComponent` presence and runtime resolution of all 7 lazy imports, titles on every navigable route, wildcard last-position by index, and a `RouterTestingHarness` navigation test asserting `toBeInstanceOf(NotFoundComponent)` for an unknown URL. Layout specs assert header/footer presence/absence, outlet presence, flex classes, brand href `/`, and 404 CTA href `/products`. No toBeTruthy padding beyond standard "should create" smoke tests. Vitest-only syntax, `TestBed.resetTestingModule()` convention followed.

## UI/UX — PASS (code inspection)

- 404: centered flex column, `clamp(5rem, 18vw, 9rem)` display code, friendly copy, prominent `btn-primary btn-lg` "Back to shopping" CTA to `/products`. Bootstrap idiomatic.
- Auth layout: full-height centered column, card capped at `max-width: 480px`, `w-100` below that — matches spec. Brand link home with aria-label.
- No fixed widths or `100vw` usage introduced; no horizontal-scroll risk in the new CSS.
