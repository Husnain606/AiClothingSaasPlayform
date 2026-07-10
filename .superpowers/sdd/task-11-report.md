## Task 11 — Platform console + hardening — Report

No prior `task-11-report.md` existed (only the brief was present), so this is a fresh report, not an overwrite.

### Verification performed before implementation

Per the "verify against live backend" instruction, every route/DTO in the brief's illustrative code was checked against `ApiUrl.cs`, `Controllers/Admin/*.cs`, and the real `Application/*/DTOs/*.cs` files before writing any frontend code. Real divergences found and followed (not the brief's samples):

1. **MFA setup is `[HttpGet]`, not POST** (`MfaController.Setup`) — `PlatformAdminService.setupMfa()` uses `apiService.get(...)`.
2. **`ChangePlanRequest.NewPlanId`**, not `planId` — `changeSubscriptionPlan()` posts `{ newPlanId }`.
3. **`PaymentsController.GetAll([FromQuery] Guid subscriptionId)`** — payments are always scoped to a subscription, there is no "list all payments" endpoint. `PaymentListComponent` requires a subscription ID to be entered before it queries; `getPayments()` takes a required (not optional) `subscriptionId` parameter.
4. **`AssignSubscriptionRequest`** requires `StartDate` in addition to `TenantId`/`PlanId` — `assignSubscription()` takes a third `startDate` argument.
5. **Real DTO field shapes used throughout** instead of the brief's fictional ones:
   - `TenantResponse`/`CreateTenantRequest` use `Email`, not `ownerEmail`; no `slug`-based lookup change needed since `slug` is real.
   - `UserResponse` (platform users) has `FirstName`/`LastName`/`IsActive` — no `isLocked` field exists on the response DTO even though the `Unlock` endpoint exists. The UI treats `!isActive` as "locked" since that's the only signal the DTO actually exposes.
   - `SubscriptionPlanResponse`/`CreateSubscriptionPlanRequest` carry the full real field set (`PlanType` enum, `DurationDays`, `TrialDays`, `ProductLimit`, `UserLimit`, `AiUsageLimit`, `StorageLimitMb`) — not the brief's simplified `{ name, price, billingCycle }`.
   - `SubscriptionResponse` has `TenantId` + `PlanName` (no `tenantName`/`planId` on the read side) — the subscription list shows `tenantId` directly (no tenant-name join available from this endpoint) and offers a plan-select dropdown to drive `changeSubscriptionPlan`.
   - `PaymentResponse` has `DueDate`/`PaidAt`/`Status` (`Pending`/`Confirmed`/`Overdue`), not the brief's `createdAt`.
   - `AuditLogResponse`/`LoginAttemptResponse` match the real `AuditLogQueryService`/`LoginAttemptService` DTOs (`EntityName`, `EntityId`, `IpAddress`, `IsSuccess`, `FailureReason`, etc.), not the brief's abbreviated shape.
   - `BankAccountResponse` — real fields are `AccountTitle`/`AccountNumber`/`BankName`/`BranchCode`/`Iban`, not `accountHolderName`/`maskedAccountNumber`. `AdminBankAccount.GetFull` requires a fresh TOTP re-verification (`VerifyTotpRequest.TotpCode`) per `BankAccountController.GetFull` — out of scope for this task per the brief's produced-method list (only masked `Get` was specified); the reveal flow was not built for the platform console (mirrors the same gap noted for tenant bank-account in Task 10, item (c) below).
   - `MfaSetupResponse` uses `QrCodeUrl`/`SecretBase32`, not `qrCodeDataUrl`/`secret`.
6. **Enum values serialize as PascalCase strings** (`JsonStringEnumConverter` registered globally in `Program.cs`) — `SubscriptionStatus.Active` → `"Active"`, `PaymentStatus.Pending` → `"Pending"`, etc. Frontend DTOs type these fields as plain `string` (matching the established convention in `settings-admin.model.ts`'s `TenantSubscriptionDto.status`), not narrowed unions, since the backend response isn't contractually limited to enum members from the client's point of view.

### Files created

- `platform/models/platform.model.ts`
- `platform/services/platform-admin.service.ts` (+ `.spec.ts`, 26 tests)
- `platform/home/platform-home.component.ts` (+ `.html`, `.spec.ts`)
- `platform/tenants/tenant-list/`, `tenant-detail/`, `tenant-form/` (+ specs), `tenants.routes.ts` updated
- `platform/plans/plan-list/` (+ specs), `plans.routes.ts` updated
- `platform/subscriptions/subscription-list/` (+ specs), `subscriptions.routes.ts` updated
- `platform/payments/payment-list/` (+ specs), `payments.routes.ts` updated
- `platform/users/platform-user-list/` (+ specs), `platform-users.routes.ts` updated
- `platform/security/audit-logs/`, `login-attempts/`, `mfa-setup/`, `bank-account/` (+ specs), `security.routes.ts` updated

All six Task 2 placeholder components (`tenants-placeholder`, `plans-placeholder`, `subscriptions-placeholder`, `payments-placeholder`, `platform-users-placeholder`, `security-placeholder`) were **deleted**. Confirmed via `grep -rln "placeholder" src/app/admin/platform` and a repo-wide `grep -rl "\-placeholder\.component"` that zero references remain anywhere in `src/`. `platform.routes.ts` required no edit — Task 2's `loadChildren` pointers already targeted the exact file paths these steps created.

### Conventions followed

- **DataTable `'custom'` column type** used for `tenant-list` (status badge + action buttons via `#customCell`) — no duplicate hand-rolled table alongside it. Simpler list views without row-level custom rendering (plans, subscriptions, payments, platform-users, audit-logs, login-attempts) use a plain `<table>` directly, matching the established pattern for similarly simple tenant-side modules (e.g. `plan-list`/`subscription-list` don't need per-row custom templates beyond what plain interpolation covers).
- **`ConfirmModalComponent.requireTypedConfirmation`** used for tenant delete (types the tenant's `slug` to confirm) — the one destructive, hard-to-reverse action in this task, consistent with the brief's explicit call-out.
- **DOM row-count regression tests** added to every list view with no exceptions: tenant-list, plan-list, subscription-list, payment-list, platform-user-list, audit-logs, login-attempts — each asserts `querySelectorAll('table tbody tr').length` equals the component's underlying row-count field.

### Zoneless CD test fixes (bugs found and fixed during TDD)

Two `NG0100: ExpressionChangedAfterItHasBeenCheckedError` failures surfaced from patterns that don't hold up under zoneless change detection:
1. **`plan-list.component.spec.ts` — `'creates a plan'`**: asserted `toHaveBeenCalledWith(component.newPlan)` *after* `onCreate()` had already reset `newPlan` inside the synchronous `of()` callback. Fixed by capturing the submitted value before calling `onCreate()`.
2. **`payment-list.component`**: the template's `*ngIf="searched && payments.length === 0"` (later `*ngIf="subscriptionId && ..."`) combined two component fields that both mutate in the same synchronous tick when a plain method call (not a native DOM event) drives the mutation — zoneless CD's "assert no changes" pass then sees inconsistent values. Fixed by (a) introducing a single derived `noResultsFound` boolean set once after data arrives instead of a multi-field inline template expression, and (b) changing the regression test to dispatch a real `change` DOM event (matching the established `order-detail.component.spec.ts` pattern) rather than calling the component method directly, since native events route through Angular's zoneless event-coalescing correctly.

### Final hardening

1. **Bundle budget**: `ng build --configuration production` → **initial total 607.72 kB** (raw), well under the 620 kB ceiling and in line with the Task 5-10 history (604.49 → 604.86 → 606.64 → 608.01 → 608.02 → **607.72 kB**). Grepped every initial/eager chunk (`main-*.js` + the 7 initial `chunk-*.js` files) for `PlatformAdminService`, `TenantListComponent`, `PlanListComponent`, `SubscriptionListComponent`, `PaymentListComponent`, `PlatformUserListComponent`, `AuditLogsComponent`, `LoginAttemptsComponent`, `MfaSetupComponent`, `PlatformBankAccountComponent`, `PlatformHomeComponent` — **zero matches**. The entire platform console is 100% lazy.
2. **`@angular/cdk` audit**: `grep -rn "@angular/cdk" src/` → **zero matches**. Still only a `package.json` dependency (peer artifact), no runtime imports anywhere — unchanged from Task 5's finding.
3. **Prod environment grep**: `grep -n "localhost\|/api/v1" src/environments/environment.prod.ts` → **zero matches** (exit code 1/no match). The real prod API URL `https://api.fashionsaas.com/api` is present in `environment.prod.ts` and confirmed present in the compiled `dist/fashionsaas-storefront/browser/chunk-YNCTXTIT.js`; `localhost` does not appear anywhere in the built `dist/**/*.js`. **Found and fixed a stale README.md doc drift while verifying this**: the README's environment-config table claimed `apiBaseUrl` was `.../api/v1` for both dev and prod, but the real files (checked directly) use `/api` (no `/v1`) — Task 4 removed `/v1` from the actual environment files but the README table was never updated. Corrected both rows.
4. **Full suite ×2** (post-implementation): both runs **99 test files passed (99), 828 tests passed (828)**, identical, zero flakes.
5. **Docs updated**:
   - `fashionsaas-storefront/README.md`: added an "Admin area routes" table (all `/admin/**` top-level paths + guards + roles) and a "Platform console" sub-table (every `/admin/platform/**` path). Also corrected the stale `/api/v1` → `/api` drift noted above and updated "Current suite: 493/493" → "828/828".
   - `docs/PROJECT_PROGRESS.md`: added a "Phase 4b: Role-Routed Admin Area" section (status COMPLETE, 11/11 tasks, 828 tests, module-by-module bullet list, key architecture notes including the DataTable/ConfirmModal conventions, the DOM row-count regression discipline, the Task 9 enum-serialization fix, the "verify against live backend" discipline and its concrete payoffs, the 100%-lazy platform console, and the final bundle number).

### Follow-up items re-confirmed (not fixed here, per brief's instruction — status only)

(a) **Order-detail ship/cancel ConfirmModal a11y defect** — **landed**. Verified via `git log` (commit `3f610eb`, present on this branch, predates this task's platform commit) and by grepping `order-detail.component.html` for `requireReason` (now used for both the ship and cancel modals, `trackingNumberInput`/`cancelReasonInput` sibling fields removed). This was the spawned background task from Task 9; it completed and its commit is already on `feature/phase4b-admin-area`.

(b) **`WishlistItemResponse.ProductName` nullable** — **still open, unchanged, cosmetic**. Confirmed via direct read of `FashionSaaS.Application/Wishlists/DTOs/WishlistItemResponse.cs:10` (`public string? ProductName { get; set; }`). Out of scope for Task 11.

(c) **Bank-account create/update UI gap** — **still open, real, out-of-scope**. Confirmed via repo-wide grep: no frontend code anywhere calls `createBankAccount`/`updateBankAccount`/references `CreateBankAccountRequest`/`UpdateBankAccountRequest`. Both the tenant-side (`settings/bank-account/tenant-bank-account.component.ts`, Task 10) and the new platform-side (`security/bank-account/platform-bank-account.component.ts`, this task) are read-only (masked-view, with tenant-side also supporting the TOTP-gated full-reveal). Task 11's platform bank-account component intentionally matches the brief's produced-method list (`getPlatformBankAccount()` only) — no reveal or create/update flow was in scope.

### Gates

- `npm run test:ci` (first run, post-implementation): **99 test files passed (99), 828 tests passed (828)**.
- `npm run test:ci` (second run, identical): **99 test files passed (99), 828 tests passed (828)**.
- `npm run test:ci` (final gate, immediately before doc updates — third and fourth runs): both **99 passed (99) / 828 passed (828)**, identical to the above.
- Baseline was 766 tests (86 files); this task added **62 new tests** (36 platform-module tests across services/components + 26 in the `platform-admin.service.spec.ts` itself — see the file list above for the full breakdown).
- `ng build --configuration production`: succeeded, zero errors. **Initial bundle: 607.72 kB** (gate: ≤ 620 kB) — pass.

### Commits

1. `3f610eb` — `fix(phase4b): move ship/cancel ConfirmModal inputs inside dialog scope` (pre-existing spawned-task fix, committed as part of finishing this task's branch state; not new work by this task, but was uncommitted and needed to land before the platform console commit for a clean history).
2. `5c3c9f4` — `feat(phase4b): Task 11 - SuperAdmin platform console` (all platform console files + placeholder removal).
3. (docs commit — see final response for hash.)

### Concerns

- `SubscriptionListComponent`'s "change plan" dropdown has no explicit "current plan, unchanged" no-op guard beyond the disabled placeholder option — selecting the tenant's current plan again would still fire `changeSubscriptionPlan` with the same ID. Low-risk (idempotent on the backend, no destructive effect) but worth tightening if this becomes a real workflow pain point.
- `PlatformUserListComponent` treats `!isActive` as "locked" since the real `UserResponse` DTO has no dedicated lock-state field even though `Unlock` is a real endpoint — this is a backend DTO expressiveness gap (an account could be inactive for reasons other than a lockout), not a frontend defect, and mirrors the same class of gap flagged for `ReviewResponse` in Task 9.

## Fix Round 1

A Task 11 review found three `PlatformAdminService` methods typed as `Observable<T[]>` when the real backend endpoints return paged results, plus a related required-field gap on login attempts. Confirmed all four issues by reading the real backend source (not assumed), then fixed the frontend to match.

### Real backend shapes found (confirmed via direct source read)

1. **`GET admin/audit-logs`** — `AuditLogsController.GetAll` binds `AuditLogFilterRequest` (`Action`, `EntityName`, `UserId`, `From`, `To`, `Page`, `PageSize`) and calls `AuditLogQueryService.GetPagedAsync`, returning `ResponseData<PagedResult<AuditLogResponse>>` (`src/FashionSaaS.Application/AuditLogs/AuditLogQueryService.cs:10-26`). Not a bare array.
2. **`GET admin/login-attempts`** — `LoginAttemptsController.GetAll` binds `LoginAttemptFilterRequest` (`Email`, `IpAddress`, `IsSuccess`, `Page`, `PageSize`) and calls `LoginAttemptService.GetByEmailAsync`, which **returns a 400 failure if `filter.Email` is empty** (`src/FashionSaaS.Application/LoginAttempts/LoginAttemptService.cs:13-14`: `if (string.IsNullOrEmpty(filter.Email)) return ResponseData<...>.Failure("Email is required.", 400);`) before returning `PagedResult<LoginAttemptResponse>`. Confirms the brief's suspicion: **email is a hard-required query param**, not optional — the old frontend filter type/UI would have always 400'd if a user cleared the field or the page loaded without one.
3. **`GET admin/users`** — `UsersController.GetAll` binds `[FromQuery] UserFilterRequest` (`Search`, `IsActive`, `Page` default 1, `PageSize` default 20) and calls `UserService.GetByTenantAsync`, returning `ResponseData<PagedResult<UserResponse>>` (`src/FashionSaaS.Application/Users/UserService.cs:90-111`). The old frontend sent zero query params and expected a bare array.
4. Backend `PagedResult<T>` shape (`src/FashionSaaS.Application/Common/PagedResult.cs`): `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage` — matches the existing frontend `PagedResult<T>` convention in `src/app/core/models/api-response.model.ts` (`items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`) already used by `getTenants()`. Reused that exact interface rather than inventing a new one. (Note: the frontend interface's field is `pageNumber` while the backend serializes `page` — a pre-existing mismatch already present for `getTenants()`/`tenant-list` before this task; out of scope here since components track `pageNumber` client-side and never read it off the response, so it does not affect the fix. Flagged separately, not fixed in this round.)

### Service changes (`platform-admin.service.ts`)

- `getAuditLogs(filter, page = 1, pageSize = 50)` → `Observable<PagedResult<AuditLogDto>>`; added `page`/`pageSize` query params (plus `action`/`entityName` param support matching the real filter DTO).
- `getLoginAttempts(filter: LoginAttemptFilter, page = 1, pageSize = 50)` → `Observable<PagedResult<LoginAttemptDto>>`; `LoginAttemptFilter.email` is now **required** (`string`, not `email?: string`), always sent as a query param; `isSuccess` support added.
- `getPlatformUsers(page, pageSize, filter: PlatformUserFilter = {})` → `Observable<PagedResult<PlatformUserDto>>`; added required `page`/`pageSize` params and optional `search`/`isActive` filter params matching `UserFilterRequest`.
- Added `AuditLogFilter`, `LoginAttemptFilter`, `PlatformUserFilter` types to `platform.model.ts`.

### Component changes

1. **`audit-logs.component.ts`/`.html`** — now uses `app-data-table` (mirroring `tenant-list`'s established pattern), reads `result.items`/`result.totalCount`, tracks `pageNumber`/`pageSize`/`loading`, wires `(pageChange)`. Range-filter changes reset to page 1.
2. **`login-attempts.component.ts`/`.html`** — rewritten around the required-email fix: no default/on-init search (previously it fired an empty-filter request on `ngOnInit`, which now always 400s). Added a `canSearch` getter (non-empty trimmed email) and an explicit search form (`(ngSubmit)="onSearch()"`) with the submit button `[disabled]="!canSearch"`, so the UI structurally prevents a 400 rather than allowing one. Results table (`app-data-table`) only renders after a successful search (`*ngIf="searched"`), with pagination wired.
3. **`platform-user-list.component.ts`/`.html`** — now uses `app-data-table` with `pageNumber`/`pageSize`/`totalCount`/`loading` state; `load()` calls `getPlatformUsers(pageNumber, pageSize)` and reads `.items`/`.totalCount`; `(pageChange)` wired. (Search/isActive filter plumbing exists on the service but no filter UI was added to this component — the brief's step 4 treated filter UI as conditional ("if the backend filter supports it"); pagination was the concrete, confirmed-broken behavior and is now fixed. Consider a follow-up if a search box is desired.)
4. **`platform-home.component.ts`** — `getPlatformUsers()` call updated to `getPlatformUsers(1, 1)` (matching the existing `getTenants(1, 1)` count-only pattern) and `platformUserCount` now reads `users.totalCount` instead of `users.length` (which would have been `undefined` on a paged object, silently breaking the KPI card).

### Tests updated (would have failed against the old buggy code)

- `platform-admin.service.spec.ts`: `getPlatformUsers` tests now assert paged params (`page`/`pageSize`) and flush a `PagedResult`-shaped response instead of a bare array; added a filter-params test. `getAuditLogs`/`getLoginAttempts` tests now flush `PagedResult`-shaped responses and assert `page`/`pageSize` params are sent.
- `audit-logs.component.spec.ts`: mock now returns a `PagedResult`; asserts `component.totalCount` in addition to `.logs.length`; added a pagination re-query test; range-change assertion updated to expect the 3-arg call signature.
- `login-attempts.component.spec.ts`: fully rewritten — asserts no API call fires on init or on `onSearch()` with an empty email (`canSearch` false), asserts a call only fires once a non-empty email is entered and submitted, and asserts pagination re-queries with the current email.
- `platform-user-list.component.spec.ts`: mock now returns a `PagedResult`; asserts `getPlatformUsers` is called with `(1, 20)`; added a pagination re-query test.
- `platform-home.component.spec.ts`: `getPlatformUsers` mock now returns a `PagedResult` (`{ items: [...], totalCount: 1, ... }`) instead of a bare array — the component's `users.totalCount` read would otherwise be `undefined`.

All of the above tests fail if the fix is reverted (bare-array mocks + no pagination args), so they cover the exact class of bug found.

### Gates (both required, both green)

- `npm run test:ci` — run 1: **99 test files passed (99), 834 tests passed (834)**. Run 2 (identical): **99 test files passed (99), 834 tests passed (834)**. (net +6 vs. the prior 828 — reflects the added pagination/required-email/filter test cases described above.)
- `npm run build` — succeeded, zero errors. **Initial bundle: 607.72 kB** (unchanged from the pre-fix baseline — no regression).

### Follow-up flagged (not fixed in this round, out of scope)

- Frontend `PagedResult<T>` (`src/app/core/models/api-response.model.ts`) types the page field as `pageNumber`, but every backend `PagedResult<T>` (`Common/PagedResult.cs`) serializes it as `page`. Currently harmless because no consumer reads that field off the response (all track `pageNumber` client-side), but it's a latent type/runtime mismatch across every paged endpoint (`getTenants` included), not just the three fixed here.
