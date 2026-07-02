# Task 7 Report (Phase 3) — Shared Module: Components, Directives, Pipes + Test Fixes

> Note: `.superpowers/sdd/task-7-report.md` already holds the committed Phase 2 Task 7 report, so this Phase 3 report uses a distinct filename (matching the Phase 3 convention, e.g. `task-6-account-module-report.md`).

**Status:** COMPLETE — all `src/app/shared/**` spec files pass (0 failed)
**Commit:** `47bd487871968fa005f845ccd95bd818108297bc` — `feat(shared): implement shared components and UI library` (37 files, 2279 insertions, fashionsaas-storefront repo)
**Build:** `npm run build` succeeds (known/acceptable budget warning: initial bundle 698.67 kB vs 500 kB budget)

## Test Results (shared module — 89 tests, all passing)

| Spec file | Tests |
|---|---|
| components/alert/alert.component.spec.ts | 12 |
| components/footer/footer.component.spec.ts | 4 |
| components/header/header.component.spec.ts | 8 |
| components/loading-spinner/loading-spinner.component.spec.ts | 8 |
| components/modal/modal.component.spec.ts | 12 |
| components/pagination/pagination.component.spec.ts | 12 |
| components/search-bar/search-bar.component.spec.ts | 11 |
| directives/highlight.directive.spec.ts | 4 |
| directives/lazy-load-image.directive.spec.ts | 5 |
| pipes/safe-html.pipe.spec.ts | 5 |
| pipes/truncate.pipe.spec.ts | 8 |

Full-suite run: `Test Files 16 failed | 22 passed (38)`, `Tests 40 failed | 281 passed (321)` — all remaining failures are pre-existing in `src/app/features/**` (known, out of scope for Task 7). No shared spec appears in the failure list.

## Root Causes and Fixes

1. **alert / search-bar specs (0 tests collected):** Not a syntax error — `fakeAsync()` requires `zone.js/testing`, which is absent (the Vitest environment is zoneless). Replaced `fakeAsync`/`tick` with `vi.useFakeTimers()` / `vi.advanceTimersByTime()`; real timers restored in `afterEach`.
2. **loading-spinner (2 failed) / modal (1 failed) — NG0100:** In zoneless change detection, mutating `@Input` properties directly does not mark the view dirty, so `detectChanges()` skips the refresh and `checkNoChanges` throws. Fixed with `fixture.componentRef.setInput(...)` (and `markForCheck()` for the non-input `searchTerm` in search-bar).
3. **lazy-load-image (3 failed):** jsdom has no `IntersectionObserver`; the old spec also poisoned `window` by assigning `undefined` (keeping the `'IntersectionObserver' in window` guard truthy). Stubbed a `MockIntersectionObserver` class via `vi.stubGlobal` in `beforeEach`, used `delete` for the "not available" test, cleaned up in `afterEach`. Added an intersection-callback test. Directive code unchanged.
4. **header (unhandled NG04002 rejection + intermittent failure):** `router.navigate` executed for real against an empty route table. Stubbed with `vi.spyOn(...).mockResolvedValue(true)` in all navigation tests.
5. **TestBed pollution ("test module already instantiated"):** Some features specs leak an instantiated TestBed into the shared Vitest worker; whichever shared spec ran first in that worker failed. Added defensive `TestBed.resetTestingModule()` at the top of `beforeEach` in all shared specs using TestBed.

All fixes are confined to `src/app/shared/`.

## Fix Round 1

Addressed all 4 Important findings from `task-7-code-review.md` plus Minor items 5, 7, 9 (pageSize), and 10.

### Important findings

1. **Header dropdown / Bootstrap JS** — Removed `data-bs-toggle="dropdown"`. Dropdown is now Angular-driven: new `isUserMenuOpen` flag with `toggleUserMenu()`/`closeUserMenu()`, menu shown via `[class.show]` on `.dropdown-menu`, toggle changed from `<a href="#">` to `<button type="button" class="nav-link dropdown-toggle">` with `[attr.aria-expanded]`. Added `@HostListener('document:click')` that closes the menu when the click target is outside the header (`ElementRef.nativeElement.contains` check). Menu also closes on any nav item click and on logout (`closeNavbar()` now resets both flags).

2. **Alert auto-dismiss under zoneless CD** — `alert.component.ts` now injects `ChangeDetectorRef` and calls `markForCheck()` in `onDismiss()`, so the setTimeout-driven auto-dismiss schedules a CD pass. Timeout cleanup in `ngOnDestroy` was already present (verified by existing test). All existing fake-timer tests pass unchanged.

3. **Nav links keyboard accessibility** — All header nav items converted from `<a (click)="onNavigate(...)">` (no href) to `routerLink` anchors (`/home`, `/catalog`, `/cart`, `/login`, `/register`, `/account/profile|orders|addresses`) keeping `(click)="closeNavbar()"` for mobile. Logout is now a real `<button class="dropdown-item text-danger">`. `onNavigate()` was removed as dead code; its two spec tests were replaced with user-menu tests (toggle, outside-click close, inside-click stays open, close-on-navbar-close).

4. **SafeHtmlPipe XSS footgun** — Added prominent JSDoc on the pipe class: bypasses the sanitizer, only for app-authored trusted HTML, never user/API content, with the `sanitizer.sanitize(SecurityContext.HTML, ...)` alternative noted. Falsy-input guard (return `''`) was already present.

### Minor fixes applied

- **Pagination**: `href="javascript:void(0)"` anchors replaced with `<button type="button" class="page-link">` elements; prev/next use native `[disabled]` (tabindex hacks removed). Unused `pageSize` @Input removed from component and its spec assertion.
- **Modal**: added `aria-modal="true"` and `tabindex="-1"` to the dialog (`role="dialog"` already present); added `@HostListener('document:keydown.escape')` that emits `cancelled` (via `onCancel()`) when visible. No focus trap (per scope).
- **Header styles**: all 10 inline `style="cursor: pointer;"` removed; cursor + button.nav-link reset (background: none, border: 0) moved into `header.component.scss`.

### Verification

- `npx ng test --watch=false --include "src/app/shared/**/*.spec.ts"` → **11 files / 91 tests, all pass** (was 89; net +2 from header spec changes).
- `npm run build` → succeeds; only the known 698.67 kB initial-budget warning.
- Pre-existing catalog test failures untouched/out of scope.

### Commit

`c8eb6b7` — fix(shared): address code review findings - a11y, zoneless CD, dropdown, XSS doc (11 files changed, +146/-61)
