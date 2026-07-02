# Task 10: Build & Deployment Configuration — Report

**Status:** COMPLETE

## Summary

Investigated the 592.77 kB initial bundle budget overage, confirmed it is not caused by
accidental eager feature imports, and resolved it with a justified budget increase plus
production build hardening (env file replacement wiring, sourcemaps off, hashed output).
Added npm scripts, a reference nginx SPA-fallback config for Phase 8, and a storefront
README.

## Budget investigation (before raising anything)

Checked for the failure mode called out in the dispatch — a shared/app-shell barrel
re-exporting something that drags feature code into the initial chunk:

- `src/app/app.routes.ts`: every feature route (`catalog`, `product-detail`, `cart`,
  `checkout`, `account`, `login`, `register`) already uses `loadComponent()` — all
  lazy-loaded. Confirmed present in both the dev and prod build's lazy chunk list.
- `src/app/layouts/main-layout/main-layout.component.ts` eagerly imports
  `HeaderComponent`/`FooterComponent` from `src/app/shared/index.ts` (a real,
  necessary dependency for the app shell, not a bug).
- `HeaderComponent` imports `CartService` (for the header cart-count badge) and
  `AuthService`. Checked `CartService`'s import chain
  (`src/app/features/cart/services/cart.service.ts`) — it only imports its own
  models (`Cart`, `CartItem`, `Product`), not any cart *components*. No feature
  component code leaks into the eager graph.
- `src/app/shared/index.ts` only re-exports shared components/directives/pipes — no
  feature imports.

Conclusion: no code-splitting bug. The initial bundle is genuinely:
- ~360 kB JS (Angular 21 + RxJS runtime + app shell, zoneless bootstrap, router,
  HttpClient + 2 interceptors)
- ~232 kB CSS (`node_modules/bootstrap/dist/css/bootstrap.min.css`, unpurged, used via
  Bootstrap utility/component classes across effectively every template — checked with
  a grep across `src/app/**/*.html` for `btn|card|form|nav|modal|badge|alert|table|
  container|row|col` classes; matches in 28 of the component templates spanning every
  feature module, layouts, and shared components)

Purging/tree-shaking Bootstrap CSS was considered but rejected as "not cheap" — it would
require a PurgeCSS/content-scanning setup and full visual regression QA across every
screen, which is disproportionate to a build-config task and risks visual breakage.

## Resolution

**Budget raised, not silently**: `angular.json` initial budget
`maximumWarning` 500kB → **600kB** (kept `maximumError` at 1MB). Documented the
rationale inline in the storefront README's "Bundle budgets" section (Angular CLI
doesn't support JSON comments, so the justification lives in README.md rather than
angular.json).

**Numbers:**
- Before: Initial total 592.77 kB (warning, budget was 500kB)
- After: Initial total 592.76 kB (clean, budget is 600kB) — build produces **zero
  warnings**
- No code changes reduced bundle size; this is a policy correction, not an
  optimization, because no waste was found.

## Production build verification

Ran `npx ng build --configuration production` (clean `dist/` first):

- Build succeeded, **no warnings** (previously 1 budget warning).
- `fileReplacements` added to the `production` configuration in `angular.json`
  (previously **not wired** — this was a gap from the dispatch's "verify" step):
  ```json
  "fileReplacements": [
    { "replace": "src/environments/environment.ts", "with": "src/environments/environment.prod.ts" }
  ]
  ```
- Grep of emitted output (`dist/fashionsaas-storefront/browser/*.js`):
  - `api.fashionsaas.com` **found** in `chunk-2HLORLQI.js` — prod URL is present.
  - `localhost:5000` **not found** in any emitted `.js` file — dev URL correctly
    excluded.
- Output hashing: confirmed on (`outputHashing: "all"` already present; all emitted
  filenames carry content hashes, e.g. `main-7NXFQOC7.js`, `styles-KY4SUSDE.css`).
- Sourcemaps: explicitly set `"sourceMap": false` on the production configuration
  (previously unset/defaulted). Verified zero `.map` files in the build output.

`environment.ts` / `environment.prod.ts` shape check: both export the same three
properties (`production`, `apiBaseUrl`, `tenantSlug`) — no shape drift.

## npm scripts added (package.json)

```json
"build:prod": "ng build --configuration production",
"test:ci": "ng test --watch=false",
"analyze": "ng build --configuration production --stats-json"
```
Verified all three run successfully; `analyze` emits
`dist/fashionsaas-storefront/stats.json`.

## Deploy reference artifact

`deploy/nginx.conf` (new file) — reference-only nginx server block with:
- `try_files $uri $uri/ /index.html` SPA fallback for client-side routing
- gzip for text/JS/CSS/SVG
- immutable long-cache headers for hashed `.js`/`.css` and other static assets
- `no-store` on `index.html` itself (since it references current hashed bundle names)

Clearly marked in-file as reference-only; actual container/cloud wiring deferred to
Phase 8.

## README

`README.md` rewritten with: prerequisites, dev server, test commands (`test` vs
`test:ci`), production build instructions, a bundle-budget rationale section, an
`analyze` section, an environment configuration table (`environment.ts` vs
`environment.prod.ts`), a full route map (path / layout / component / guards), and a
deploy note pointing at `deploy/nginx.conf` + Phase 8.

## .gitignore

Added `test-results.txt` (was untracked at repo root, per dispatch note) so it's never
accidentally committed.

## Files created/modified

- Modified: `fashionsaas-storefront/angular.json` (fileReplacements, budget 500→600kB,
  sourceMap: false on production config)
- Modified: `fashionsaas-storefront/package.json` (added `build:prod`, `test:ci`,
  `analyze` scripts)
- Modified: `fashionsaas-storefront/.gitignore` (added `test-results.txt`)
- Modified: `fashionsaas-storefront/README.md` (full rewrite per deliverable 5)
- Created: `fashionsaas-storefront/deploy/nginx.conf`

## Final test count

493/493 passing (42 test files), verified via both `npm test -- --watch=false` and
`npm run test:ci`.

## Commit

Submodule `fashionsaas-storefront` commit: `31a678ceb86c9b5a93655ee44d76c038bce0425f`
```
build: production build configuration, budgets, SPA deploy reference
```
5 files changed (`.gitignore`, `README.md`, `angular.json`, `package.json`,
`deploy/nginx.conf`).

Note: the outer `AIcLOTHING` superproject's submodule pointer for
`fashionsaas-storefront` now shows as modified (`git status` → `M
fashionsaas-storefront`) since the submodule advanced. Not committed at the
superproject level — left for the user/maintainer to commit alongside other Phase 3
progress-ledger updates, consistent with how prior tasks (6, 7, 8) were tracked.
