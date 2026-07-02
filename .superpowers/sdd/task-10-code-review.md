# Task 10 Code Review: Build & Deployment Configuration

**Commit reviewed:** `31a678ceb86c9b5a93655ee44d76c038bce0425f` (fashionsaas-storefront)
**Verdict: APPROVED**

All 7 checks pass with direct evidence (build reproduced, tests re-run, files read in full).

## 1. angular.json — PASS

`git show 31a678c -- angular.json` confirms the diff touches only the `production`
configuration block:

- `fileReplacements` (env.ts → environment.prod.ts) added under `production` only.
  `development` configuration (lines 55-59) has no `fileReplacements` key — untouched.
- Budget: `maximumWarning` 500kB → **600kB**; `maximumError` stays at **1MB** (sensible,
  already present pre-change, not touched by this diff).
- `sourceMap: false` added to `production` only. `development` config explicitly sets
  `sourceMap: true` (unchanged) — clean separation, no cross-contamination.

## 2. Environment parity — PASS

`src/environments/environment.ts`:
```
{ production: false, apiBaseUrl: 'http://localhost:5000/api/v1', tenantSlug: 'default-tenant' }
```
`src/environments/environment.prod.ts`:
```
{ production: true, apiBaseUrl: 'https://api.fashionsaas.com/api/v1', tenantSlug: '' }
```
Identical property names/shape (`production`, `apiBaseUrl`, `tenantSlug`).

Grep of `src/` for `environment\.\w+` found exactly one accessed property across the
codebase: `environment.apiBaseUrl`, used in:
- `src/app/core/services/api.service.ts:9`
- `src/app/features/checkout/services/order.service.spec.ts:14`

`apiBaseUrl` exists in both files — no risk of a runtime `undefined` from a name mismatch.

## 3. Prod build reproduction — PASS

Ran `Remove-Item dist -Recurse -Force; npm run build:prod` directly:

- Build succeeded, zero budget warnings. Initial total **592.76 kB** (under the 600kB
  warning threshold).
- `dist/fashionsaas-storefront/browser/*.js`: `api.fashionsaas.com` found in
  `chunk-2HLORLQI.js`; `localhost:5000` found in **zero** files.
- `.map` file count in browser output: **0**.

Matches the implementation report's claims exactly.

## 4. package.json scripts — PASS

```
"build:prod": "ng build --configuration production",
"test:ci": "ng test --watch=false",
"analyze": "ng build --configuration production --stats-json"
```
All syntactically valid. Ran `npm run test:ci`:
```
Test Files  42 passed (42)
Tests  493 passed (493)
```
493/493 — matches the report. (Console noise from jsdom's unimplemented `window.alert` is
expected test-environment chatter, not a failure.)

## 5. deploy/nginx.conf — PASS

SPA fallback at the `location /` block: `try_files $uri $uri/ /index.html;` — correct.
No dangerous directives (gzip config, per-extension immutable caching, `no-store` on
`index.html` are all reasonable). Clearly marked reference-only in a header comment:
"STATUS: Reference only — not wired into any deployment pipeline yet... Phase 8 scope."

## 6. README.md — PASS

- Documented commands (`npm start`, `npm test`, `npm run test:ci`, `npm run build:prod`,
  `npm run analyze`) all match actual `package.json` scripts.
- Environment table (production/apiBaseUrl/tenantSlug values for dev vs prod) matches
  the actual `environment.ts` / `environment.prod.ts` contents exactly.
- Route map table matches `src/app/app.routes.ts` exactly, including guards
  (`authGuard` on products/product-detail/cart/checkout/account, plus
  `cartNotEmptyGuard` on checkout; no guards on login/register) — verified against the
  routes file directly, not just the report's claim.
- No fabricated claims: explicitly states no Docker/CI pipeline exists yet and defers
  to Phase 8; test count claim (493/493) matches actual run.

## 7. .gitignore — PASS

`test-results.txt` added to `.gitignore`. `git show 31a678c --stat` lists exactly 5
changed files (`.gitignore`, `README.md`, `angular.json`, `deploy/nginx.conf`,
`package.json`) — `test-results.txt` itself was not added/tracked in this commit.

## Summary

Small, well-scoped config diff. Dev/prod configuration boundaries are clean, environment
parity holds, the budget increase is justified and documented (not a silent suppression),
the nginx reference config is correct and clearly labeled non-authoritative, and all
documentation claims (README) were independently verified against the actual source
files rather than taken on faith. No fixes needed.
