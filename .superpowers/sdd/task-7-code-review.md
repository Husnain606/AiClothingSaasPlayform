# Task 7 Code Review — Shared Components & UI Library

**Commit reviewed:** `47bd487` — feat(shared): implement shared components and UI library (37 files, +2279)
**Reviewer verification:** `npx ng test --watch=false --include "src/app/shared/**/*.spec.ts"` → **11 files / 89 tests, all pass** (matches report). Note: running `npx vitest run src/app/shared` directly fails (TestBed env not initialized) — tests must go through the `@angular/build:unit-test` builder; not a defect, just a gotcha.

## Verdict: NEEDS FIXES

## Spec compliance — PASS

All 7 components, 2 directives, 2 pipes exist with the specified inputs/outputs, all standalone, all exported from `src/app/shared/index.ts`:

- HeaderComponent: logo, nav links, cart badge from `CartService.cart$` (`map(cart => cart.itemCount)`), user dropdown with logout via `AuthService`, hamburger toggle. Service imports verified real: `AuthService.isAuthenticated()/getCurrentUser()/logout()` exist in `core/services/auth.service.ts`; `cart$` with `itemCount` exists in `features/cart/services/cart.service.ts`; `CurrentUser` model exists. Will resolve when mounted in Task 8.
- FooterComponent, LoadingSpinnerComponent (fullPage/size/message), AlertComponent (type/message/dismissible/autoDismissMs + dismissed), PaginationComponent (currentPage/totalPages/pageSize + pageChange, windowed ±2, disabled first/last), ModalComponent (title/content/confirmText/cancelText/type + confirmed/cancelled), SearchBarComponent (debounced search + clear + Enter): all match spec.
- No `any` in production code (only two `(window as any)` casts in a spec file — acceptable). No HttpClient in shared. Subscriptions handled: header uses async pipe; search-bar completes its Subject in ngOnDestroy; alert clears its timeout; lazy-load-image disconnects the observer.

## Findings

### Important (blocks)

1. **`header.component.html:79` — user dropdown depends on Bootstrap JS, which is not loaded anywhere.** `data-bs-toggle="dropdown"` requires `bootstrap.bundle.min.js`. `angular.json` includes only `bootstrap.min.css` (no `scripts` entry), and nothing imports the Bootstrap JS module in `main.ts`/components. When mounted in Task 8, the user menu (Profile/Orders/Addresses/**Logout**) will never open — a spec-required behavior is dead. Fix: add the bundle to `angular.json` scripts, or (preferred, zoneless-friendly) drive the dropdown with an Angular property like the hamburger already does.

2. **`alert.component.ts:23` — auto-dismiss does not trigger change detection in this zoneless app.** The app is Angular 21 with no zone.js dependency and no polyfills (zoneless CD). Mutating `this.isVisible` inside a raw `window.setTimeout` callback schedules no CD pass; the alert only visually disappears if a parent happens to bind `(dismissed)` (whose listener marks views dirty as a side effect). With no listener bound, the alert never hides. Fix: inject `ChangeDetectorRef` and call `markForCheck()` in `onDismiss()`, or convert `isVisible` to a signal.

3. **`header.component.html` (lines 4–116, all nav items) — nav links are `<a>` elements with `(click)` and no `href`/`routerLink`: not keyboard-accessible.** Anchors without href are not focusable, so the entire navigation (catalog, cart, login, account menu items, logout) is unreachable by keyboard, and middle-click/open-in-new-tab is broken. `RouterModule` is imported but unused. Fix: use `routerLink` (and keep `(click)="closeNavbar()"` for the mobile menu).

4. **`safe-html.pipe.ts:15` — `bypassSecurityTrustHtml` on arbitrary input with no trusted-content contract.** The pipe disables Angular's HTML sanitizer for whatever string it receives. It is currently unused elsewhere in the app, so there is no live XSS today, but as a barrel-exported shared pipe it invites unsafe use on API/user-supplied content (e.g., product descriptions). Per the global constraint this is only acceptable if documented for trusted content. Fix: add an explicit doc comment ("only for app-controlled/trusted HTML; never for user/API content") — or use `sanitizer.sanitize(SecurityContext.HTML, value)` instead if the intent is safe rich text.

### Minor (note only)

5. `modal.component.html` — no Escape-to-close, no focus trap, missing `aria-modal="true"`/`tabindex="-1"` on the dialog. Escape was called out as nice-to-have, so noting, not blocking.
6. `modal.component.ts:23,28` — component mutates its own `@Input() isVisible`; parent-owned state won't stay in sync. Prefer parent-controlled visibility via `*ngIf` or a `closed` output only.
7. `pagination.component.html:7,21,34` — `href="javascript:void(0)"` anti-pattern; prefer `<button class="page-link">` or `role="button"` + keydown handling. (Tabindex/disabled handling is otherwise done well.)
8. `pagination.component.ts:23` — window is a fixed ±2, so at the edges only 3 numbers render instead of a constant 5; cosmetic.
9. `header.component.html:45` — badge span renders (empty) even when count is 0 with an empty class string; harmless but could be a single `*ngIf="(cartItemCount$ | async) ?? 0 > 0"` guard. Also `pageSize` input on pagination is accepted but unused in logic/template.
10. Inline `style="cursor: pointer;"` repeated 10x in header — belongs in the SCSS.

## Tests — GOOD

Spot-checked alert, pagination, header, modal, search-bar, safe-html specs: assertions are behavioral, not `toBeTruthy` chains. Alert auto-dismiss uses `vi.useFakeTimers`/`advanceTimersByTime` (incl. 0-disables and destroy-cleanup cases); pagination asserts emit-with-value, out-of-bounds suppression, and same-page suppression; search-bar asserts 300 ms debounce and clear-emits-empty; header mocks AuthService/CartService and stubs `router.navigate`. UI/UX otherwise consistent: Bootstrap 5 classes, primary/success/danger palette matches the app, `role="alert"`, aria-labels on icon-only buttons, `d-none d-sm-inline` responsive pagination, visually-hidden spinner text.
