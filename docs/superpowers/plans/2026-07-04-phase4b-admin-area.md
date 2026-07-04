# Phase 4b: Role-Routed Admin Area Implementation Plan

> **REQUIRED SUB-SKILL:** Every task in this plan is executed via the `superpowers:test-driven-development` skill (red → green → refactor, one behavior at a time) and reviewed via `superpowers:requesting-code-review` before being marked done. Tasks are executed with `superpowers:subagent-driven-development`; each task is an independent unit of work with its own commit. Do not skip the RED step — every new file starts with a failing test.

## Goal

Add a role-routed `/admin` area to the existing `fashionsaas-storefront` Angular app (no new app) that gives tenant-admin roles (AdminOwner, StoreManager, InventoryManager, OrderManager, ContentManager) a full store-management dashboard, and gives SuperAdmin a platform console over the existing 37 `api/admin/*` endpoints. One login, three-way post-login redirect by JWT role, MFA challenge step for SuperAdmin, lazy-loaded so shopper bundle size is unaffected.

Backend prerequisite (Phase 4a, already merged on `feature/phase4a-orders-backend`): Orders domain, `api/store/orders/*`, `api/tenant/orders/*`, `api/tenant/reports/*` all exist and are tested. This plan consumes them; it adds **zero** new backend endpoints.

## Architecture

```
fashionsaas-storefront/src/app/
  core/                          # extended: AuthService role parsing, new guards
  features/
    auth/                        # extended: MFA challenge step on login
    account/, cart/, catalog/, checkout/   # untouched (shopper area)
    admin/                       # NEW lazy feature area
      admin.routes.ts            #   /admin/** route table (loadChildren from app.routes.ts)
      layout/                    #   AdminLayoutComponent (sidebar + topbar)
      shared/                    #   toast, data-table, kpi-card, confirm-modal,
                                  #   date-range-picker, status-badge (admin-local shared kit)
      dashboard/
      orders/
      catalog/                   #   tenant catalog management (products/categories/variants/images)
      inventory/
      customers/
      discounts/
      reviews/
      reports/
      settings/
      platform/                  #   SuperAdmin-only, loadChildren from admin.routes.ts
        home/ tenants/ plans/ subscriptions/ payments/ users/ security/
  shared/                        # existing storefront shared kit (untouched, still used by admin where generic)
```

Routing nests three ways under the router root:
- `/` (existing `MainLayoutComponent`) — shopper, untouched.
- `/` (existing `AuthLayoutComponent`) — login/register, extended with MFA step.
- `/admin` (new `AdminLayoutComponent`) — lazy `loadChildren`, guarded by `authGuard + adminRoleGuard`. Contains tenant-store modules directly under it and a nested `/admin/platform/**` guarded additionally by `superAdminGuard`.

## Tech Stack (exact versions, from repo inspection)

- Angular `^21.1.0` (package.json), standalone components, `@angular/build:application` + `@angular/build:unit-test` (Vitest `^4.0.8`) — **see Global Constraint #1 below, this is NOT currently zoneless**.
- Bootstrap `^5.3.0`, CSS-only (no ng-bootstrap/PrimeNG) — utility classes + component `.scss`, Bootstrap Icons (`bi-*`) already in use.
- RxJS `~7.8.0`, class-based services with constructor DI (existing convention — NOT `inject()` functional style, except in functional guards which already use `inject()`).
- **New dependency this phase:** `ng2-charts` + `chart.js` (Task 5, dashboard only, lazy-loaded with the admin chunk).
- TypeScript `~5.9.2`, strict mode (verify `tsconfig.json` `"strict": true` before Task 1 starts — treat as blocking if false).

## Global Constraints

1. **ZONELESS CONFLICT (must read before Task 1):** The spec assumes a zoneless app. Repo inspection shows `app.config.ts` has **no** `provideZonelessChangeDetection()` call, and `main.ts` does plain `bootstrapApplication`. However, `shared/components/alert/alert.component.ts` already contains a comment "Auto-dismiss fires from a raw setTimeout; under zoneless change detection no CD pass is scheduled, so mark the view dirty explicitly" and manually calls `cdr.markForCheck()` — i.e., the codebase is written *defensively as if* zoneless, without the provider wired in. **Resolution for this plan:** Task 1 adds `provideZonelessChangeDetection()` to `app.config.ts` as its first sub-step (it is a one-line prerequisite the spec assumes exists). All new admin code additionally follows the defensive zoneless pattern (`markForCheck()` after async/timer callbacks that mutate state consumed by the template) regardless, so this is safe even if the user defers turning the provider on. Flagged explicitly per instructions — this is not a silent deviation.
2. Angular 21 standalone components only; no NgModules. Constructor DI for services (matches existing convention); `inject()` only inside functional guards/resolvers.
3. Bootstrap 5.3 CSS-only. No new UI library. Icons via `bi-*` classes (already loaded).
4. **Vitest conventions (mandatory, copied from working examples in this repo):**
   - No `fakeAsync`/`tick()`. Use `vi.useFakeTimers()` / `vi.advanceTimersByTime()` / `vi.useRealTimers()` in `afterEach`.
   - Use `fixture.componentRef.setInput('propName', value)` for `@Input()` properties, never direct field assignment, when the input is `@Input()`-decorated (direct assignment remains fine for plain public fields that are not `@Input()`).
   - `TestBed.resetTestingModule()` as the first line of every `beforeEach` that configures a fresh TestBed.
   - `provideRouter([])` (or a narrow route array) in providers for any component/service that injects `Router`/`ActivatedRoute`, instead of full `RouterTestingModule`.
   - Mock services via `Partial<T>` + `vi.fn()`, provided with `{ provide: X, useValue: mockX }` — never real HTTP.
5. Strict TypeScript: no `any`. Use `unknown` + narrowing or precise interfaces. (Existing code has a few `any`s in older interceptors — do not introduce new ones; do not need to fix old ones unless the file is touched by a task.)
6. **Bundle budget:** storefront initial chunk must stay ≤ 600 kB after `ng build --configuration production` (matches `angular.json` budget already set to `maximumWarning: 600kB`). The entire `/admin` area (including ng2-charts) must load via `loadChildren` so it never enters the initial chunk. Verified explicitly in Task 11.
7. No `alert()` calls anywhere in new code. The existing `shared/components/alert` component is a static banner, not a toast — Task 3 builds a new imperative `ToastService` + `ToastComponent` for the admin area; do not reuse `AlertComponent` for transient notifications.
8. WCAG 2.1 AA: every icon-only button has `aria-label`; every form control has an associated `<label>`; focus is visibly indicated (Bootstrap default outlines must not be suppressed); modals trap focus and restore it to the triggering element on close; color is never the sole status indicator (status badges pair color with text).
9. **Suite green ×2 per task:** run `npm run test:ci` twice in a row after each task's implementation is complete, before moving to the next task. Flaky output (differs between the two runs) blocks the task — fix before proceeding.
10. **Type consistency across tasks:** interfaces and service method signatures defined in Task 4 (`OrderAdminService`, `ReportService`, TS DTOs) are consumed verbatim (same method names, same parameter/return types) by Tasks 5–11. No task may redefine or duplicate a type introduced in Task 4.
11. Backend base URL fix: `environment.apiBaseUrl` currently `http://localhost:5000/api/v1` (dev) and `https://api.fashionsaas.com/api/v1` (prod) — **mismatched** with backend routes, which are unversioned `api/...` (confirmed against `src/FashionSaaS.API/Constants/ApiUrl.cs`, e.g. `api/auth/login`, `api/tenant/orders`, `api/store/orders`). Task 4 fixes **both** `environment.ts` and `environment.prod.ts` to drop the `/v1` segment.

## Conflicts and deviations found during research (explicit, no silent fixes)

1. **Zoneless not wired in** — see Global Constraint #1. Fixed in Task 1 by adding the provider; flagged rather than silently worked around.
2. **`apiBaseUrl` has a stray `/v1`** in both environment files — confirmed against backend `ApiUrl.cs`. Fixed in Task 4 (both files).
3. **Existing checkout `OrderService`** (`features/checkout/services/order.service.ts`) posts to bare `orders` (i.e. `{apiBaseUrl}/orders`), which has never matched any backend route (`api/store/orders` is the real one, confirmed in `ApiUrl.StoreOrders`). This was presumably built ahead of the Phase 4a backend landing. Task 4 repoints it at `store/orders` and aligns its request shape to `CreateOrderRequest` (`shippingAddress`, `paymentInfo: {cardholderName, cardNumber}`, `items: [{productId, quantity, variant}]`) — the existing payload also sends `productName`/`price`/`expiryMonth`/`expiryYear` fields the backend `CreateOrderRequest` does not accept; these are dropped in Task 4 to keep the request body honest.
4. **`account.model.ts`'s `Order.status`** union is `'pending' | 'processing' | 'shipped' | 'delivered' | 'cancelled'` — `'processing'` does not exist in the backend's `OrderStatus` enum (`Pending, Confirmed, Shipped, Delivered, Cancelled`, serialized lowercase). The checkout feature's own `order.model.ts` already has the correct union (`'pending' | 'confirmed' | 'shipped' | 'delivered' | 'cancelled'`). Task 4 corrects `account.model.ts` to match and re-points `AccountService.getOrders`/`getOrderById` at `store/orders` (currently `account/orders`, which is not a backend route either — confirmed absent from `ApiUrl.cs`).
5. **No new backend endpoints required** — verified every admin/tenant/store route referenced by this plan already exists in `ApiUrl.cs`. Platform console consumes the existing 37 `api/admin/*` endpoints only, per spec section 7.

---

## Task 1 — Auth upgrade: role parsing, three-way redirect, guards, MFA challenge

### Files
- Edit: `fashionsaas-storefront/src/app/app.config.ts`
- Edit: `fashionsaas-storefront/src/app/core/services/auth.service.ts`
- Edit: `fashionsaas-storefront/src/app/core/services/auth.service.spec.ts` (create if absent — check first; none was found in the glob, so create)
- Edit: `fashionsaas-storefront/src/app/features/auth/models/auth.model.ts`
- Create: `fashionsaas-storefront/src/app/features/auth/guards/admin-role.guard.ts`
- Create: `fashionsaas-storefront/src/app/features/auth/guards/admin-role.guard.spec.ts`
- Create: `fashionsaas-storefront/src/app/features/auth/guards/super-admin.guard.ts`
- Create: `fashionsaas-storefront/src/app/features/auth/guards/super-admin.guard.spec.ts`
- Edit: `fashionsaas-storefront/src/app/features/auth/components/login/login.component.ts`
- Edit: `fashionsaas-storefront/src/app/features/auth/components/login/login.component.html`
- Edit: `fashionsaas-storefront/src/app/features/auth/components/login/login.component.spec.ts`

### Interfaces

**Consumes** (backend, confirmed): `POST api/auth/login` → `ResponseData<LoginResponse>` where `LoginResponse = { accessToken: string|null, refreshToken: null, mfaRequired: boolean, mfaToken: string|null }` (refreshToken is always stripped server-side before the response leaves the controller — it travels only via HttpOnly cookie). `POST api/auth/login/mfa` body `{ mfaToken: string, code: string }` → same `ResponseData<LoginResponse>` shape but with `mfaRequired: false` and a populated `accessToken` on success.

JWT claims (confirmed in `JwtService.cs`): `sub` (userId), `email`, `tenant_id`, `tenant_slug?`, `mfa_verified` ("true"/"false" string), and one `role` claim (`ClaimTypes.Role`, i.e. `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) **per role** — a user can have multiple `role` claims, so `jwt-decode` of a multi-role token yields either a `string` or `string[]` for the `role` key depending on how many roles are present. This plan's decode helper normalizes both shapes into a `string[]`.

**Produces** (this task, consumed verbatim by later tasks):

```typescript
// features/auth/models/auth.model.ts
export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string | null;
  refreshToken: string | null;
  mfaRequired: boolean;
  mfaToken: string | null;
}

export interface LoginMfaRequest {
  mfaToken: string;
  code: string;
}

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  confirmPassword: string;
}

export type AppRole =
  | 'SuperAdmin'
  | 'AdminOwner'
  | 'StoreManager'
  | 'InventoryManager'
  | 'OrderManager'
  | 'ContentManager'
  | 'Customer';

export interface CurrentUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: AppRole[];
}

export const TENANT_ADMIN_ROLES: AppRole[] = [
  'AdminOwner',
  'StoreManager',
  'InventoryManager',
  'OrderManager',
  'ContentManager',
];
```

`AuthService` new public surface (added to existing class, existing `login`/`register`/`logout`/`getToken`/`setToken`/`clearToken`/`isAuthenticated`/`getCurrentUser` signatures unchanged):

```typescript
loginMfa(request: LoginMfaRequest): Observable<LoginResponse>;
getRoles(): AppRole[];                 // synchronous, reads current JWT
hasAnyRole(roles: AppRole[]): boolean; // synchronous
isSuperAdmin(): boolean;
isTenantAdmin(): boolean;              // true iff hasAnyRole(TENANT_ADMIN_ROLES)
postLoginRedirectPath(): string;       // '/admin/platform' | '/admin' | '/products'
```

`adminRoleGuard: CanActivateFn` — true iff `authService.isAuthenticated()` current value is true AND `authService.hasAnyRole([...TENANT_ADMIN_ROLES, 'SuperAdmin'])` (SuperAdmin can still open `/admin` shell to reach `/admin/platform`, but individual tenant-module routes additionally restrict via per-route `data.roles`, added in Task 2). Redirects to `/login` if unauthenticated, to `/products` if authenticated but role-less (e.g. Customer).

`superAdminGuard: CanActivateFn` — true iff authenticated AND `isSuperAdmin()`. Redirects to `/admin` if authenticated but not SuperAdmin (a tenant admin hitting `/admin/platform/**` bounces to their own dashboard, not an error page), to `/login` if unauthenticated.

### TDD steps

**Step 1.0 — RED: zoneless provider (prerequisite, Global Constraint #1).**

Edit `app.config.ts`:

```typescript
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, HTTP_INTERCEPTORS } from '@angular/common/http';
import { routes } from './app.routes';
import { AuthInterceptor } from './core/interceptors/auth.interceptor';
import { ErrorInterceptor } from './core/interceptors/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: ErrorInterceptor, multi: true },
  ],
};
```

No test file targets `app.config.ts` directly (matches existing convention — it isn't unit tested in this repo); verification is that `npm run test:ci` still passes in full (zoneless can surface timing bugs in existing specs that relied on implicit zone flushes — if any existing spec fails here, fix that spec's async handling using the `vi.useFakeTimers()` convention, do not revert the provider).

**Step 1.1 — RED: `auth.service.spec.ts` for role parsing and redirect.** Create the file (none existed):

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from './auth.service';
import { ApiService } from './api.service';
import { LoginResponse } from '../../features/auth/models/auth.model';

function makeToken(roles: string[], overrides: Record<string, unknown> = {}): string {
  const header = { alg: 'HS256', typ: 'JWT' };
  const payload = {
    sub: 'user-1',
    email: 'admin@example.com',
    role: roles.length === 1 ? roles[0] : roles,
    tenant_id: 'tenant-1',
    mfa_verified: 'false',
    ...overrides,
  };
  const encode = (obj: unknown) =>
    btoa(JSON.stringify(obj)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode(header)}.${encode(payload)}.signature`;
}

describe('AuthService — role parsing and redirect', () => {
  let service: AuthService;
  let mockApiService: Partial<ApiService>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    localStorage.clear();

    mockApiService = { post: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: ApiService, useValue: mockApiService },
        { provide: HttpClient, useValue: {} },
      ],
    });
    service = TestBed.inject(AuthService);
  });

  it('returns no roles when there is no token', () => {
    expect(service.getRoles()).toEqual([]);
  });

  it('parses a single role claim into a one-element array', () => {
    service.setToken(makeToken(['AdminOwner']));
    expect(service.getRoles()).toEqual(['AdminOwner']);
  });

  it('parses a multi-role claim array', () => {
    service.setToken(makeToken(['AdminOwner', 'StoreManager']));
    expect(service.getRoles()).toEqual(['AdminOwner', 'StoreManager']);
  });

  it('hasAnyRole matches when at least one role overlaps', () => {
    service.setToken(makeToken(['StoreManager']));
    expect(service.hasAnyRole(['AdminOwner', 'StoreManager'])).toBe(true);
  });

  it('hasAnyRole is false when there is no overlap', () => {
    service.setToken(makeToken(['Customer']));
    expect(service.hasAnyRole(['AdminOwner'])).toBe(false);
  });

  it('isSuperAdmin is true only for SuperAdmin role', () => {
    service.setToken(makeToken(['SuperAdmin']));
    expect(service.isSuperAdmin()).toBe(true);
    expect(service.isTenantAdmin()).toBe(false);
  });

  it('isTenantAdmin is true for any tenant-admin role', () => {
    service.setToken(makeToken(['InventoryManager']));
    expect(service.isTenantAdmin()).toBe(true);
    expect(service.isSuperAdmin()).toBe(false);
  });

  it('postLoginRedirectPath routes SuperAdmin to /admin/platform', () => {
    service.setToken(makeToken(['SuperAdmin']));
    expect(service.postLoginRedirectPath()).toBe('/admin/platform');
  });

  it('postLoginRedirectPath routes tenant admin roles to /admin', () => {
    service.setToken(makeToken(['OrderManager']));
    expect(service.postLoginRedirectPath()).toBe('/admin');
  });

  it('postLoginRedirectPath routes Customer to /products', () => {
    service.setToken(makeToken(['Customer']));
    expect(service.postLoginRedirectPath()).toBe('/products');
  });

  it('postLoginRedirectPath routes a role-less token to /products', () => {
    service.setToken(makeToken([]));
    expect(service.postLoginRedirectPath()).toBe('/products');
  });

  it('loginMfa posts to auth/login/mfa and stores the returned access token', () => {
    const response: LoginResponse = {
      accessToken: makeToken(['SuperAdmin']),
      refreshToken: null,
      mfaRequired: false,
      mfaToken: null,
    };
    (mockApiService.post as ReturnType<typeof vi.fn>).mockReturnValue(
      of({ statusCode: 200, message: 'ok', data: response, errors: null, timestamp: '' })
    );

    service.loginMfa({ mfaToken: 'challenge-token', code: '123456' }).subscribe((result) => {
      expect(result.accessToken).toBe(response.accessToken);
    });

    expect(mockApiService.post).toHaveBeenCalledWith('auth/login/mfa', {
      mfaToken: 'challenge-token',
      code: '123456',
    });
    expect(service.getToken()).toBe(response.accessToken);
  });
});
```

Run `npm run test:ci -- --run auth.service.spec` — fails: `getRoles`, `hasAnyRole`, `isSuperAdmin`, `isTenantAdmin`, `postLoginRedirectPath`, `loginMfa` do not exist yet.

**Step 1.2 — GREEN: implement in `auth.service.ts`.** Add imports and methods (keep all existing methods and their bodies unchanged):

```typescript
import { AppRole, TENANT_ADMIN_ROLES, LoginMfaRequest, LoginResponse } from '../../features/auth/models/auth.model';
// ...existing imports stay

// inside AuthService class, after existing methods:

  loginMfa(request: LoginMfaRequest): Observable<LoginResponse> {
    return this.apiService.post<LoginResponse>('auth/login/mfa', request).pipe(
      tap((response: ApiResponse<LoginResponse>) => {
        if (response.data.accessToken) {
          this.setToken(response.data.accessToken);
          this.isAuthenticated$.next(true);
          this.loadCurrentUser();
        }
      }),
      map((response: ApiResponse<LoginResponse>) => response.data)
    );
  }

  getRoles(): AppRole[] {
    const token = this.getToken();
    if (!token) return [];
    try {
      const payload = this.decodeToken(token);
      const raw = payload.role;
      if (!raw) return [];
      return (Array.isArray(raw) ? raw : [raw]) as AppRole[];
    } catch {
      return [];
    }
  }

  hasAnyRole(roles: AppRole[]): boolean {
    const mine = this.getRoles();
    return roles.some((r) => mine.includes(r));
  }

  isSuperAdmin(): boolean {
    return this.hasAnyRole(['SuperAdmin']);
  }

  isTenantAdmin(): boolean {
    return this.hasAnyRole(TENANT_ADMIN_ROLES);
  }

  postLoginRedirectPath(): string {
    if (this.isSuperAdmin()) return '/admin/platform';
    if (this.isTenantAdmin()) return '/admin';
    return '/products';
  }
```

Also update `loadCurrentUser()`'s `roles: payload.roles || []` to `roles: this.getRoles()` so `CurrentUser.roles` (used by the header) reflects the real `role` claim instead of a nonexistent `payload.roles` field:

```typescript
  private loadCurrentUser(): void {
    const token = this.getToken();
    if (token) {
      try {
        const payload = this.decodeToken(token);
        const currentUser: CurrentUser = {
          id: payload.sub || '',
          email: payload.email || '',
          firstName: payload.firstName || '',
          lastName: payload.lastName || '',
          roles: this.getRoles(),
        };
        this.currentUser$.next(currentUser);
      } catch (e) {
        console.error('Failed to load current user from token', e);
      }
    }
  }
```

Run `npm run test:ci -- --run auth.service.spec` — passes.

**Step 1.3 — RED: `admin-role.guard.spec.ts`.**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { adminRoleGuard } from './admin-role.guard';
import { AuthService } from '../../../core/services/auth.service';

describe('adminRoleGuard', () => {
  let mockAuth: Partial<AuthService>;
  let router: Router;

  const run = () =>
    TestBed.runInInjectionContext(() =>
      adminRoleGuard({} as never, { url: '/admin' } as never)
    );

  beforeEach(() => {
    TestBed.resetTestingModule();
    mockAuth = {
      isAuthenticated: () => of(true),
      hasAnyRole: vi.fn().mockReturnValue(true),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: mockAuth }],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  it('allows access for an authenticated tenant-admin or SuperAdmin role', async () => {
    const result = await run();
    expect(result).toBe(true);
  });

  it('redirects to /login when not authenticated', async () => {
    mockAuth.isAuthenticated = () => of(false);
    const result = await run();
    expect(result).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/admin' } });
  });

  it('redirects to /products when authenticated but role-less', async () => {
    (mockAuth.hasAnyRole as ReturnType<typeof vi.fn>).mockReturnValue(false);
    const result = await run();
    expect(result).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/products']);
  });
});
```

**Step 1.4 — GREEN: `admin-role.guard.ts`.**

```typescript
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, take } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';
import { TENANT_ADMIN_ROLES } from '../models/auth.model';

export const adminRoleGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated().pipe(
    take(1),
    map((isAuthenticated) => {
      if (!isAuthenticated) {
        router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
        return false;
      }
      if (!authService.hasAnyRole([...TENANT_ADMIN_ROLES, 'SuperAdmin'])) {
        router.navigate(['/products']);
        return false;
      }
      return true;
    })
  );
};
```

**Step 1.5 — RED/GREEN: `super-admin.guard.spec.ts` / `super-admin.guard.ts`** (mirrors 1.3/1.4 exactly, only the role check and off-ramp differ):

```typescript
// super-admin.guard.spec.ts — same scaffolding as admin-role.guard.spec.ts, with:
it('redirects to /admin when authenticated but not SuperAdmin', async () => {
  (mockAuth.isSuperAdmin as ReturnType<typeof vi.fn>).mockReturnValue(false);
  const result = await run();
  expect(result).toBe(false);
  expect(router.navigate).toHaveBeenCalledWith(['/admin']);
});
```

```typescript
// super-admin.guard.ts
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, take } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';

export const superAdminGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated().pipe(
    take(1),
    map((isAuthenticated) => {
      if (!isAuthenticated) {
        router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
        return false;
      }
      if (!authService.isSuperAdmin()) {
        router.navigate(['/admin']);
        return false;
      }
      return true;
    })
  );
};
```

**Step 1.6 — MFA challenge step on the login page.** Read current `login.component.ts` shape first (existing file, not shown in research excerpt but consistent with `account.component.ts` conventions — constructor DI, template-driven or reactive form). Extend it with a two-step state machine:

```typescript
// login.component.ts — add to existing class (keep existing form/fields/submit-for-password-login intact)
import { Component, OnInit } from '@angular/core';
// ...existing imports plus:
import { LoginMfaRequest } from '../../models/auth.model';

// inside LoginComponent:
  step: 'credentials' | 'mfa' = 'credentials';
  mfaToken = '';
  mfaCode = '';
  mfaError = '';
  mfaSubmitting = false;

  // existing onSubmit() for step 1 — after a successful this.authService.login(...) call,
  // branch on the response instead of always navigating:
  onSubmit(): void {
    if (this.loginForm.invalid) return;
    this.isSubmitting = true;
    this.errorMessage = '';

    this.authService.login(this.loginForm.value).subscribe({
      next: (response) => {
        this.isSubmitting = false;
        if (response.mfaRequired && response.mfaToken) {
          this.step = 'mfa';
          this.mfaToken = response.mfaToken;
          return;
        }
        this.router.navigateByUrl(this.returnUrl ?? this.authService.postLoginRedirectPath());
      },
      error: (err) => {
        this.isSubmitting = false;
        this.errorMessage = 'Invalid email or password.';
      },
    });
  }

  onSubmitMfa(): void {
    if (!this.mfaCode || this.mfaCode.length !== 6) {
      this.mfaError = 'Enter the 6-digit code from your authenticator app.';
      return;
    }
    this.mfaSubmitting = true;
    this.mfaError = '';

    const request: LoginMfaRequest = { mfaToken: this.mfaToken, code: this.mfaCode };
    this.authService.loginMfa(request).subscribe({
      next: () => {
        this.mfaSubmitting = false;
        this.router.navigateByUrl(this.returnUrl ?? this.authService.postLoginRedirectPath());
      },
      error: () => {
        this.mfaSubmitting = false;
        this.mfaError = 'Invalid or expired code. Try again.';
      },
    });
  }

  onBackToCredentials(): void {
    this.step = 'credentials';
    this.mfaCode = '';
    this.mfaError = '';
  }
```

Template addition to `login.component.html` (appended, existing credentials form wrapped in `*ngIf="step === 'credentials'"`):

```html
<form *ngIf="step === 'credentials'" [formGroup]="loginForm" (ngSubmit)="onSubmit()">
  <!-- ...existing fields unchanged... -->
</form>

<form *ngIf="step === 'mfa'" (ngSubmit)="onSubmitMfa()" aria-label="Two-factor verification">
  <p class="text-muted">Enter the 6-digit code from your authenticator app.</p>
  <div class="mb-3">
    <label for="mfaCode" class="form-label">Verification code</label>
    <input
      id="mfaCode"
      name="mfaCode"
      type="text"
      inputmode="numeric"
      maxlength="6"
      class="form-control"
      [class.is-invalid]="mfaError"
      [(ngModel)]="mfaCode"
      [ngModelOptions]="{standalone: true}"
      autocomplete="one-time-code"
      required />
    <div class="invalid-feedback" *ngIf="mfaError">{{ mfaError }}</div>
  </div>
  <button type="submit" class="btn btn-primary w-100" [disabled]="mfaSubmitting">
    {{ mfaSubmitting ? 'Verifying…' : 'Verify' }}
  </button>
  <button type="button" class="btn btn-link w-100" (click)="onBackToCredentials()">
    Back to sign in
  </button>
</form>
```

**Step 1.7 — tests for the login MFA branch** added to `login.component.spec.ts` (existing file — extend it):

```typescript
// added to existing describe block, using the existing mockAuthService pattern already in the file
it('shows the MFA step when login response requires MFA', () => {
  mockAuthService.login = vi.fn().mockReturnValue(
    of({ accessToken: null, refreshToken: null, mfaRequired: true, mfaToken: 'challenge-abc' })
  );
  component.loginForm.setValue({ email: 'super@example.com', password: 'Password1!' });

  component.onSubmit();

  expect(component.step).toBe('mfa');
  expect(component.mfaToken).toBe('challenge-abc');
});

it('navigates by role-based redirect after successful non-MFA login', () => {
  const navSpy = vi.spyOn(router, 'navigateByUrl');
  mockAuthService.login = vi.fn().mockReturnValue(
    of({ accessToken: 'token', refreshToken: null, mfaRequired: false, mfaToken: null })
  );
  mockAuthService.postLoginRedirectPath = vi.fn().mockReturnValue('/admin');
  component.loginForm.setValue({ email: 'owner@example.com', password: 'Password1!' });

  component.onSubmit();

  expect(navSpy).toHaveBeenCalledWith('/admin');
});

it('submits the MFA code and redirects on success', () => {
  const navSpy = vi.spyOn(router, 'navigateByUrl');
  mockAuthService.loginMfa = vi.fn().mockReturnValue(
    of({ accessToken: 'token', refreshToken: null, mfaRequired: false, mfaToken: null })
  );
  mockAuthService.postLoginRedirectPath = vi.fn().mockReturnValue('/admin/platform');
  component.step = 'mfa';
  component.mfaToken = 'challenge-abc';
  component.mfaCode = '123456';

  component.onSubmitMfa();

  expect(mockAuthService.loginMfa).toHaveBeenCalledWith({ mfaToken: 'challenge-abc', code: '123456' });
  expect(navSpy).toHaveBeenCalledWith('/admin/platform');
});

it('shows an error and stays on the MFA step for an invalid code', () => {
  mockAuthService.loginMfa = vi.fn().mockReturnValue(throwError(() => new Error('invalid')));
  component.step = 'mfa';
  component.mfaToken = 'challenge-abc';
  component.mfaCode = '000000';

  component.onSubmitMfa();

  expect(component.step).toBe('mfa');
  expect(component.mfaError).toContain('Invalid or expired code');
});

it('rejects a code that is not 6 digits without calling the service', () => {
  mockAuthService.loginMfa = vi.fn();
  component.step = 'mfa';
  component.mfaCode = '123';

  component.onSubmitMfa();

  expect(mockAuthService.loginMfa).not.toHaveBeenCalled();
  expect(component.mfaError).toBeTruthy();
});
```

### Verification

```
npm run test:ci -- --run auth.service.spec admin-role.guard.spec super-admin.guard.spec login.component.spec
npm run test:ci
npm run test:ci
```
Expect: all new specs pass; full suite green twice in a row; no regressions in existing auth/header specs (header consumes `CurrentUser.roles`, unaffected by the signature change since the field name is unchanged).

---

## Task 2 — Admin shell: AdminLayoutComponent, route scaffolds, header "Dashboard" link

### Files
- Create: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.html`
- Create: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.scss`
- Create: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/layout/menu-config.ts`
- Create: `fashionsaas-storefront/src/app/admin/layout/menu-config.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/admin.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/admin.routes.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/platform.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/platform.routes.spec.ts`
- Edit: `fashionsaas-storefront/src/app/app.routes.ts` (add lazy `/admin` branch)
- Edit: `fashionsaas-storefront/src/app/app.routes.spec.ts` (extend existing assertions)
- Edit: `fashionsaas-storefront/src/app/shared/components/header/header.component.ts`
- Edit: `fashionsaas-storefront/src/app/shared/components/header/header.component.html`
- Edit: `fashionsaas-storefront/src/app/shared/components/header/header.component.spec.ts`

### Interfaces

**Produces** (consumed by Tasks 3–11 for menu rendering and by Task 11 for the hardening pass):

```typescript
// admin/layout/menu-config.ts
import { AppRole } from '../../features/auth/models/auth.model';

export interface AdminMenuItem {
  label: string;
  icon: string;       // bi-* class suffix, e.g. 'speedometer2'
  path: string;        // relative to /admin or /admin/platform
  roles: AppRole[];    // menu item visible iff current user hasAnyRole(roles)
}

export const TENANT_MENU: AdminMenuItem[] = [
  { label: 'Dashboard',  icon: 'speedometer2',  path: '/admin',            roles: ['AdminOwner', 'StoreManager'] },
  { label: 'Orders',     icon: 'bag-check',     path: '/admin/orders',     roles: ['AdminOwner', 'OrderManager', 'StoreManager'] },
  { label: 'Catalog',    icon: 'grid',          path: '/admin/catalog',    roles: ['AdminOwner', 'StoreManager', 'ContentManager'] },
  { label: 'Inventory',  icon: 'boxes',         path: '/admin/inventory',  roles: ['AdminOwner', 'InventoryManager'] },
  { label: 'Customers',  icon: 'people',        path: '/admin/customers',  roles: ['AdminOwner', 'StoreManager'] },
  { label: 'Discounts',  icon: 'tag',           path: '/admin/discounts',  roles: ['AdminOwner', 'StoreManager'] },
  { label: 'Reviews',    icon: 'star',          path: '/admin/reviews',    roles: ['AdminOwner', 'StoreManager'] },
  { label: 'Reports',    icon: 'bar-chart',     path: '/admin/reports',    roles: ['AdminOwner', 'StoreManager'] },
  { label: 'Settings',   icon: 'gear',          path: '/admin/settings',   roles: ['AdminOwner'] },
];

export const PLATFORM_MENU: AdminMenuItem[] = [
  { label: 'Home',           icon: 'speedometer2', path: '/admin/platform',              roles: ['SuperAdmin'] },
  { label: 'Tenants',        icon: 'building',     path: '/admin/platform/tenants',      roles: ['SuperAdmin'] },
  { label: 'Plans',          icon: 'card-list',    path: '/admin/platform/plans',        roles: ['SuperAdmin'] },
  { label: 'Subscriptions',  icon: 'receipt',      path: '/admin/platform/subscriptions',roles: ['SuperAdmin'] },
  { label: 'Payments',       icon: 'credit-card',  path: '/admin/platform/payments',     roles: ['SuperAdmin'] },
  { label: 'Platform Users', icon: 'people-fill',  path: '/admin/platform/users',        roles: ['SuperAdmin'] },
  { label: 'Security',       icon: 'shield-lock',  path: '/admin/platform/security',     roles: ['SuperAdmin'] },
];

export function visibleMenuItems(items: AdminMenuItem[], userRoles: AppRole[]): AdminMenuItem[] {
  return items.filter((item) => item.roles.some((r) => userRoles.includes(r)));
}
```

`AdminLayoutComponent` public surface:

```typescript
menuItems: AdminMenuItem[];        // set in ngOnInit from visibleMenuItems(...) based on authService.getRoles()
isPlatform: boolean;               // true if authService.isSuperAdmin() — drives which menu array and page title
isDrawerOpen = false;              // < 992px collapse state
toggleDrawer(): void;
closeDrawer(): void;
onLogout(): void;                  // delegates to AuthService.logout() + router to /login
```

### TDD steps

**Step 2.1 — RED: `menu-config.spec.ts`.**

```typescript
import { describe, it, expect } from 'vitest';
import { TENANT_MENU, PLATFORM_MENU, visibleMenuItems } from './menu-config';

describe('visibleMenuItems', () => {
  it('includes an item when the user has one of its required roles', () => {
    const result = visibleMenuItems(TENANT_MENU, ['StoreManager']);
    expect(result.map((i) => i.label)).toContain('Dashboard');
    expect(result.map((i) => i.label)).toContain('Orders');
  });

  it('excludes Settings for non-AdminOwner roles', () => {
    const result = visibleMenuItems(TENANT_MENU, ['StoreManager']);
    expect(result.map((i) => i.label)).not.toContain('Settings');
  });

  it('includes only Inventory-relevant items for InventoryManager', () => {
    const result = visibleMenuItems(TENANT_MENU, ['InventoryManager']);
    expect(result.map((i) => i.label)).toEqual(['Inventory']);
  });

  it('shows every platform item to SuperAdmin', () => {
    const result = visibleMenuItems(PLATFORM_MENU, ['SuperAdmin']);
    expect(result.length).toBe(PLATFORM_MENU.length);
  });

  it('shows no platform items to a tenant-admin role', () => {
    const result = visibleMenuItems(PLATFORM_MENU, ['AdminOwner']);
    expect(result.length).toBe(0);
  });
});
```

**Step 2.2 — GREEN:** implement `menu-config.ts` exactly as specified in Interfaces above. Run `npm run test:ci -- --run menu-config.spec` — passes.

**Step 2.3 — RED: `admin-layout.component.spec.ts`.**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterOutlet } from '@angular/router';
import { AdminLayoutComponent } from './admin-layout.component';
import { AuthService } from '../../core/services/auth.service';

describe('AdminLayoutComponent', () => {
  let fixture: ComponentFixture<AdminLayoutComponent>;
  let component: AdminLayoutComponent;
  let mockAuth: Partial<AuthService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockAuth = {
      getRoles: vi.fn().mockReturnValue(['AdminOwner']),
      isSuperAdmin: vi.fn().mockReturnValue(false),
      logout: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [AdminLayoutComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: mockAuth }],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminLayoutComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('builds the tenant menu for a tenant-admin role', () => {
    expect(component.isPlatform).toBe(false);
    expect(component.menuItems.map((i) => i.label)).toContain('Settings');
  });

  it('builds the platform menu for SuperAdmin', () => {
    (mockAuth.isSuperAdmin as ReturnType<typeof vi.fn>).mockReturnValue(true);
    (mockAuth.getRoles as ReturnType<typeof vi.fn>).mockReturnValue(['SuperAdmin']);
    component.ngOnInit();
    expect(component.isPlatform).toBe(true);
    expect(component.menuItems.map((i) => i.label)).toContain('Tenants');
  });

  it('starts with the drawer closed', () => {
    expect(component.isDrawerOpen).toBe(false);
  });

  it('toggles the drawer open and closed', () => {
    component.toggleDrawer();
    expect(component.isDrawerOpen).toBe(true);
    component.toggleDrawer();
    expect(component.isDrawerOpen).toBe(false);
  });

  it('closes the drawer explicitly', () => {
    component.isDrawerOpen = true;
    component.closeDrawer();
    expect(component.isDrawerOpen).toBe(false);
  });

  it('logs out and clears session on onLogout', () => {
    component.onLogout();
    expect(mockAuth.logout).toHaveBeenCalled();
  });
});
```

**Step 2.4 — GREEN: `admin-layout.component.ts`.**

```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { AdminMenuItem, TENANT_MENU, PLATFORM_MENU, visibleMenuItems } from './menu-config';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './admin-layout.component.html',
  styleUrls: ['./admin-layout.component.scss'],
})
export class AdminLayoutComponent implements OnInit {
  menuItems: AdminMenuItem[] = [];
  isPlatform = false;
  isDrawerOpen = false;

  constructor(private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    this.isPlatform = this.authService.isSuperAdmin();
    const roles = this.authService.getRoles();
    this.menuItems = visibleMenuItems(this.isPlatform ? PLATFORM_MENU : TENANT_MENU, roles);
  }

  toggleDrawer(): void {
    this.isDrawerOpen = !this.isDrawerOpen;
  }

  closeDrawer(): void {
    this.isDrawerOpen = false;
  }

  onLogout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
```

`admin-layout.component.html`:

```html
<div class="admin-shell">
  <button
    type="button"
    class="btn btn-outline-secondary d-lg-none admin-drawer-toggle"
    (click)="toggleDrawer()"
    [attr.aria-expanded]="isDrawerOpen"
    aria-label="Toggle admin navigation">
    <i class="bi bi-list"></i>
  </button>

  <aside class="admin-sidebar" [class.admin-sidebar--open]="isDrawerOpen">
    <div class="admin-sidebar__brand">
      <i class="bi bi-shop me-2"></i>{{ isPlatform ? 'Platform Console' : 'Store Admin' }}
    </div>
    <nav aria-label="Admin navigation">
      <ul class="nav flex-column">
        <li class="nav-item" *ngFor="let item of menuItems">
          <a
            class="nav-link"
            [routerLink]="item.path"
            routerLinkActive="active"
            [routerLinkActiveOptions]="{ exact: item.path === '/admin' || item.path === '/admin/platform' }"
            (click)="closeDrawer()">
            <i class="bi" [class]="'bi-' + item.icon"></i> {{ item.label }}
          </a>
        </li>
      </ul>
    </nav>
  </aside>

  <div class="admin-backdrop d-lg-none" *ngIf="isDrawerOpen" (click)="closeDrawer()"></div>

  <div class="admin-content">
    <header class="admin-topbar d-flex justify-content-between align-items-center border-bottom px-3 py-2">
      <a routerLink="/products" class="btn btn-sm btn-outline-secondary">
        <i class="bi bi-arrow-left"></i> Back to store
      </a>
      <button type="button" class="btn btn-sm btn-outline-danger" (click)="onLogout()">
        <i class="bi bi-box-arrow-right"></i> Logout
      </button>
    </header>
    <main class="admin-main p-3">
      <router-outlet></router-outlet>
    </main>
  </div>
</div>
```

`admin-layout.component.scss` (responsive drawer, < 992px per spec 4.3):

```scss
.admin-shell { display: flex; min-height: 100vh; }
.admin-sidebar {
  width: 240px;
  background: #212529;
  color: #fff;
  flex-shrink: 0;

  .nav-link { color: rgba(255,255,255,.75); }
  .nav-link.active, .nav-link:hover { color: #fff; background: rgba(255,255,255,.1); }
}
.admin-content { flex: 1; min-width: 0; }
.admin-drawer-toggle { display: none; }
.admin-backdrop {
  position: fixed; inset: 0; background: rgba(0,0,0,.4); z-index: 1030;
}

@media (max-width: 991.98px) {
  .admin-drawer-toggle { display: inline-flex; position: fixed; top: .5rem; left: .5rem; z-index: 1040; }
  .admin-sidebar {
    position: fixed; top: 0; left: 0; bottom: 0; z-index: 1035;
    transform: translateX(-100%);
    transition: transform .2s ease-in-out;
  }
  .admin-sidebar--open { transform: translateX(0); }
}
```

**Step 2.5 — RED: `admin.routes.spec.ts` and `platform.routes.spec.ts`** (mirrors the existing `app.routes.spec.ts` pattern of asserting shape rather than rendering):

```typescript
// admin/admin.routes.spec.ts
import { describe, it, expect } from 'vitest';
import { adminRoutes } from './admin.routes';
import { adminRoleGuard } from '../features/auth/guards/admin-role.guard';

describe('admin routes configuration', () => {
  it('guards the root admin route with adminRoleGuard', () => {
    const root = adminRoutes.find((r) => r.path === '')!;
    expect(root.canActivate).toContain(adminRoleGuard);
  });

  it('lazily loads dashboard, orders, catalog, inventory, customers, discounts, reviews, reports, settings, and platform', () => {
    const expectedPaths = [
      '', 'orders', 'catalog', 'inventory', 'customers', 'discounts', 'reviews', 'reports', 'settings', 'platform',
    ];
    const root = adminRoutes.find((r) => r.path === '')!;
    const childPaths = root.children!.map((c) => c.path);
    for (const p of expectedPaths) {
      expect(childPaths).toContain(p);
    }
  });

  it('every child route (except platform, which nests its own children) lazy-loads a component or children', () => {
    const root = adminRoutes.find((r) => r.path === '')!;
    for (const child of root.children!) {
      if (child.path === 'platform') {
        expect(typeof child.loadChildren).toBe('function');
      } else {
        expect(typeof child.loadComponent ?? typeof child.loadChildren).not.toBe('undefined');
      }
    }
  });
});
```

```typescript
// admin/platform/platform.routes.spec.ts
import { describe, it, expect } from 'vitest';
import { platformRoutes } from './platform.routes';
import { superAdminGuard } from '../../features/auth/guards/super-admin.guard';

describe('platform routes configuration', () => {
  it('guards the platform root with superAdminGuard', () => {
    const root = platformRoutes.find((r) => r.path === '')!;
    expect(root.canActivate).toContain(superAdminGuard);
  });

  it('defines routes for home, tenants, plans, subscriptions, payments, users, security', () => {
    const root = platformRoutes.find((r) => r.path === '')!;
    const paths = root.children!.map((c) => c.path);
    expect(paths).toEqual(
      expect.arrayContaining(['', 'tenants', 'plans', 'subscriptions', 'payments', 'users', 'security'])
    );
  });
});
```

**Step 2.6 — GREEN: `admin.routes.ts`.** (Task 3's shared kit and Tasks 5–10's feature components do not exist yet — route stubs reference the eventual paths; components are created in their respective tasks. To keep this task's suite green in isolation, Task 2 creates minimal placeholder `loadComponent` targets for dashboard/orders/etc. is NOT done — instead the route table below points at paths that Tasks 5–10 will create, and Task 2's own tests only assert on route *shape* (`path`, `canActivate`, presence of `loadComponent`/`loadChildren` as a function), never resolving them. This keeps Task 2 fully decoupled from unwritten feature modules while still proving the scaffold is correct.)

```typescript
import { Routes } from '@angular/router';
import { AdminLayoutComponent } from './layout/admin-layout.component';
import { adminRoleGuard } from '../features/auth/guards/admin-role.guard';

export const adminRoutes: Routes = [
  {
    path: '',
    component: AdminLayoutComponent,
    canActivate: [adminRoleGuard],
    children: [
      {
        path: '',
        title: 'Dashboard | FashionSaaS Admin',
        loadComponent: () =>
          import('./dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        path: 'orders',
        title: 'Orders | FashionSaaS Admin',
        loadChildren: () => import('./orders/orders.routes').then((m) => m.ordersRoutes),
      },
      {
        path: 'catalog',
        title: 'Catalog | FashionSaaS Admin',
        loadChildren: () => import('./catalog/catalog.routes').then((m) => m.catalogRoutes),
      },
      {
        path: 'inventory',
        title: 'Inventory | FashionSaaS Admin',
        loadChildren: () => import('./inventory/inventory.routes').then((m) => m.inventoryRoutes),
      },
      {
        path: 'customers',
        title: 'Customers | FashionSaaS Admin',
        loadChildren: () => import('./customers/customers.routes').then((m) => m.customersRoutes),
      },
      {
        path: 'discounts',
        title: 'Discounts | FashionSaaS Admin',
        loadChildren: () => import('./discounts/discounts.routes').then((m) => m.discountsRoutes),
      },
      {
        path: 'reviews',
        title: 'Reviews | FashionSaaS Admin',
        loadChildren: () => import('./reviews/reviews.routes').then((m) => m.reviewsRoutes),
      },
      {
        path: 'reports',
        title: 'Reports | FashionSaaS Admin',
        loadChildren: () => import('./reports/reports.routes').then((m) => m.reportsRoutes),
      },
      {
        path: 'settings',
        title: 'Settings | FashionSaaS Admin',
        loadChildren: () => import('./settings/settings.routes').then((m) => m.settingsRoutes),
      },
      {
        path: 'platform',
        loadChildren: () => import('./platform/platform.routes').then((m) => m.platformRoutes),
      },
    ],
  },
];
```

```typescript
// admin/platform/platform.routes.ts
import { Routes } from '@angular/router';
import { superAdminGuard } from '../../features/auth/guards/super-admin.guard';

export const platformRoutes: Routes = [
  {
    path: '',
    canActivate: [superAdminGuard],
    children: [
      {
        path: '',
        title: 'Platform Home | FashionSaaS',
        loadComponent: () => import('./home/platform-home.component').then((m) => m.PlatformHomeComponent),
      },
      {
        path: 'tenants',
        title: 'Tenants | FashionSaaS',
        loadChildren: () => import('./tenants/tenants.routes').then((m) => m.tenantsRoutes),
      },
      {
        path: 'plans',
        title: 'Plans | FashionSaaS',
        loadChildren: () => import('./plans/plans.routes').then((m) => m.plansRoutes),
      },
      {
        path: 'subscriptions',
        title: 'Subscriptions | FashionSaaS',
        loadChildren: () => import('./subscriptions/subscriptions.routes').then((m) => m.subscriptionsRoutes),
      },
      {
        path: 'payments',
        title: 'Payments | FashionSaaS',
        loadChildren: () => import('./payments/payments.routes').then((m) => m.paymentsRoutes),
      },
      {
        path: 'users',
        title: 'Platform Users | FashionSaaS',
        loadChildren: () => import('./users/platform-users.routes').then((m) => m.platformUsersRoutes),
      },
      {
        path: 'security',
        title: 'Security | FashionSaaS',
        loadChildren: () => import('./security/security.routes').then((m) => m.securityRoutes),
      },
    ],
  },
];
```

**Step 2.7 — wire `/admin` into `app.routes.ts`** (top-level lazy branch, sibling to the existing `MainLayoutComponent`/`AuthLayoutComponent` entries, inserted before the `'**'` wildcard):

```typescript
// app.routes.ts — add this entry to the routes array, after the AuthLayoutComponent block
  {
    path: 'admin',
    loadChildren: () => import('./admin/admin.routes').then((m) => m.adminRoutes),
  },
```

Extend `app.routes.spec.ts` with:

```typescript
it('lazily loads the admin area without a component on the app-level route', () => {
  const adminRoute = routes.find((r) => r.path === 'admin')!;
  expect(adminRoute).toBeDefined();
  expect(adminRoute.component).toBeUndefined();
  expect(typeof adminRoute.loadChildren).toBe('function');
});
```

Note: the existing `'should resolve every lazy loadComponent to a component class'` test enumerates `mainLayoutRoute.children` and `authLayoutRoute.children` only — `admin` is a top-level sibling entry, not a child of either, so that test's `lazyRoutes.length` assertion (`toBe(7)`) is unaffected and does not need updating.

**Step 2.8 — header "Dashboard" link for admin roles.** Extend `header.component.ts`:

```typescript
// added to existing HeaderComponent
  showDashboardLink$!: Observable<boolean>;

  // in ngOnInit(), alongside existing assignments:
  this.showDashboardLink$ = this.currentUser$.pipe(
    map((user) => !!user && (this.authService.isTenantAdmin() || this.authService.isSuperAdmin()))
  );
```

Template addition to `header.component.html`, inside the existing `*ngIf="isLoggedIn$ | async"` dropdown block, before "My Account":

```html
<li *ngIf="showDashboardLink$ | async">
  <a class="dropdown-item" routerLink="/admin" (click)="closeNavbar()">
    <i class="bi bi-speedometer2 me-2"></i>Dashboard
  </a>
</li>
```

Extend `header.component.spec.ts`:

```typescript
it('shows the Dashboard link for a tenant-admin role', async () => {
  mockAuthService.isTenantAdmin = vi.fn().mockReturnValue(true);
  mockAuthService.isSuperAdmin = vi.fn().mockReturnValue(false);
  mockAuthService.getCurrentUser = vi.fn().mockReturnValue(of({ id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: ['StoreManager'] }));
  component.ngOnInit();

  const visible = await firstValueFrom(component.showDashboardLink$);
  expect(visible).toBe(true);
});

it('hides the Dashboard link for a Customer', async () => {
  mockAuthService.isTenantAdmin = vi.fn().mockReturnValue(false);
  mockAuthService.isSuperAdmin = vi.fn().mockReturnValue(false);
  mockAuthService.getCurrentUser = vi.fn().mockReturnValue(of({ id: '1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: ['Customer'] }));
  component.ngOnInit();

  const visible = await firstValueFrom(component.showDashboardLink$);
  expect(visible).toBe(false);
});
```

(Add `isTenantAdmin`/`isSuperAdmin` to the existing `mockAuthService` object in the spec's `beforeEach` if the object literal is typed as `Partial<AuthService>`, which the existing file's pattern already uses per the account/modal spec conventions confirmed above.)

### Verification

```
npm run test:ci -- --run menu-config.spec admin-layout.component.spec admin.routes.spec platform.routes.spec header.component.spec app.routes.spec
npm run test:ci
npm run test:ci
```

---

## Task 3 — Admin shared kit: toast, data-table, KPI card, confirm modal, date-range picker, status badge

### Files
- Create: `fashionsaas-storefront/src/app/admin/shared/services/toast.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/services/toast.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/toast-container/toast-container.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/toast-container/toast-container.component.html`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/toast-container/toast-container.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/data-table/data-table.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/data-table/data-table.component.html`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/data-table/data-table.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/kpi-card/kpi-card.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/kpi-card/kpi-card.component.html`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/kpi-card/kpi-card.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/confirm-modal/confirm-modal.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/confirm-modal/confirm-modal.component.html`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/confirm-modal/confirm-modal.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/date-range-picker/date-range-picker.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/date-range-picker/date-range-picker.component.html`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/date-range-picker/date-range-picker.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/status-badge/status-badge.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/components/status-badge/status-badge.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/index.ts` (barrel, mirrors `shared/index.ts` pattern)
- Edit: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.ts` / `.html` (mount `<app-toast-container>` once at shell level)
- Edit: `fashionsaas-storefront/src/app/admin/layout/admin-layout.component.spec.ts`

### Interfaces (produced here, consumed verbatim by Tasks 5–11)

```typescript
// admin/shared/services/toast.service.ts
export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface ToastMessage {
  id: number;
  kind: ToastKind;
  text: string;
}

// public API
success(text: string): void;
error(text: string): void;
info(text: string): void;
warning(text: string): void;
dismiss(id: number): void;
readonly toasts$: Observable<ToastMessage[]>;
```

```typescript
// admin/shared/components/data-table/data-table.component.ts
export interface DataTableColumn<T> {
  key: keyof T & string;
  header: string;
  sortable?: boolean;
  cellTemplate?: 'text' | 'currency' | 'date' | 'custom';
}

// @Input()s
columns: DataTableColumn<T>[];
rows: T[];
totalCount: number;
pageNumber: number;   // 1-based
pageSize: number;
sortKey: string | null;
sortDirection: 'asc' | 'desc';
loading: boolean;
emptyMessage: string;

// @Output()s
pageChange: EventEmitter<number>;
sortChange: EventEmitter<{ key: string; direction: 'asc' | 'desc' }>;
```

```typescript
// admin/shared/components/kpi-card/kpi-card.component.ts
// @Input()s
label: string;
value: string | number;
icon: string;          // bi-* suffix
trend?: 'up' | 'down' | 'flat';
trendLabel?: string;
```

```typescript
// admin/shared/components/confirm-modal/confirm-modal.component.ts
// @Input()s
isOpen: boolean;
title: string;
message: string;
confirmLabel = 'Confirm';
cancelLabel = 'Cancel';
tone: 'primary' | 'danger' = 'primary';
requireTypedConfirmation?: string;   // e.g. tenant name — Task 11's tenant delete uses this
// @Output()s
confirmed: EventEmitter<void>;
cancelled: EventEmitter<void>;
```

```typescript
// admin/shared/components/date-range-picker/date-range-picker.component.ts
export interface DateRange { from: string; to: string; }  // ISO yyyy-MM-dd
// @Input() range: DateRange;
// @Output() rangeChange: EventEmitter<DateRange>;
```

```typescript
// admin/shared/components/status-badge/status-badge.component.ts
// @Input() status: string;  // any OrderStatus/ReviewStatus/etc lowercase string
```

### TDD steps

**Step 3.1 — RED: `toast.service.spec.ts`.**

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [ToastService] });
    service = TestBed.inject(ToastService);
  });

  it('starts with no toasts', async () => {
    expect(await firstValueFrom(service.toasts$)).toEqual([]);
  });

  it('adds a success toast', async () => {
    service.success('Order confirmed');
    const toasts = await firstValueFrom(service.toasts$);
    expect(toasts).toHaveLength(1);
    expect(toasts[0]).toMatchObject({ kind: 'success', text: 'Order confirmed' });
  });

  it('adds toasts of every kind', async () => {
    service.success('a');
    service.error('b');
    service.info('c');
    service.warning('d');
    const toasts = await firstValueFrom(service.toasts$);
    expect(toasts.map((t) => t.kind)).toEqual(['success', 'error', 'info', 'warning']);
  });

  it('assigns unique incrementing ids', async () => {
    service.success('a');
    service.success('b');
    const toasts = await firstValueFrom(service.toasts$);
    expect(toasts[0].id).not.toBe(toasts[1].id);
  });

  it('dismisses a toast by id', async () => {
    service.success('a');
    const [first] = await firstValueFrom(service.toasts$);
    service.dismiss(first.id);
    const toasts = await firstValueFrom(service.toasts$);
    expect(toasts).toHaveLength(0);
  });

  it('auto-dismisses a toast after 5000ms', async () => {
    vi.useFakeTimers();
    service.success('auto-dismiss me');
    expect((await firstValueFrom(service.toasts$))).toHaveLength(1);

    vi.advanceTimersByTime(5000);
    expect((await firstValueFrom(service.toasts$))).toHaveLength(0);
    vi.useRealTimers();
  });
});
```

**Step 3.2 — GREEN: `toast.service.ts`.**

```typescript
import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface ToastMessage {
  id: number;
  kind: ToastKind;
  text: string;
}

const AUTO_DISMISS_MS = 5000;

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly toastsSubject = new BehaviorSubject<ToastMessage[]>([]);
  readonly toasts$ = this.toastsSubject.asObservable();

  success(text: string): void { this.push('success', text); }
  error(text: string): void { this.push('error', text); }
  info(text: string): void { this.push('info', text); }
  warning(text: string): void { this.push('warning', text); }

  dismiss(id: number): void {
    this.toastsSubject.next(this.toastsSubject.value.filter((t) => t.id !== id));
  }

  private push(kind: ToastKind, text: string): void {
    const toast: ToastMessage = { id: this.nextId++, kind, text };
    this.toastsSubject.next([...this.toastsSubject.value, toast]);
    setTimeout(() => this.dismiss(toast.id), AUTO_DISMISS_MS);
  }
}
```

**Step 3.3 — RED: `toast-container.component.spec.ts`.**

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastContainerComponent } from './toast-container.component';
import { ToastService } from '../../services/toast.service';

describe('ToastContainerComponent', () => {
  let fixture: ComponentFixture<ToastContainerComponent>;
  let component: ToastContainerComponent;
  let toastService: ToastService;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [ToastContainerComponent],
      providers: [ToastService],
    }).compileComponents();

    fixture = TestBed.createComponent(ToastContainerComponent);
    component = fixture.componentInstance;
    toastService = TestBed.inject(ToastService);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders a toast pushed through the service', () => {
    toastService.success('Saved!');
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Saved!');
  });

  it('renders the correct Bootstrap class per toast kind', () => {
    toastService.error('Failed!');
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('.toast');
    expect(el.className).toContain('text-bg-danger');
  });

  it('removes a toast from the DOM when dismissed', () => {
    toastService.success('Bye');
    fixture.detectChanges();
    const [toast] = component.toasts;
    component.onDismiss(toast.id);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Bye');
  });
});
```

**Step 3.4 — GREEN: `toast-container.component.ts` / `.html`.**

```typescript
import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { ToastService, ToastMessage } from '../../services/toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './toast-container.component.html',
})
export class ToastContainerComponent implements OnInit, OnDestroy {
  toasts: ToastMessage[] = [];
  private sub?: Subscription;

  constructor(private toastService: ToastService) {}

  ngOnInit(): void {
    this.sub = this.toastService.toasts$.subscribe((toasts) => (this.toasts = toasts));
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  onDismiss(id: number): void {
    this.toastService.dismiss(id);
  }

  kindClass(kind: string): string {
    const map: Record<string, string> = {
      success: 'text-bg-success',
      error: 'text-bg-danger',
      info: 'text-bg-info',
      warning: 'text-bg-warning',
    };
    return map[kind] ?? 'text-bg-secondary';
  }
}
```

```html
<div class="toast-container position-fixed top-0 end-0 p-3" style="z-index: 1080" role="region" aria-label="Notifications">
  <div
    *ngFor="let toast of toasts"
    class="toast show mb-2"
    [class]="kindClass(toast.kind)"
    role="status"
    aria-live="polite">
    <div class="d-flex">
      <div class="toast-body">{{ toast.text }}</div>
      <button
        type="button"
        class="btn-close btn-close-white me-2 m-auto"
        aria-label="Dismiss notification"
        (click)="onDismiss(toast.id)">
      </button>
    </div>
  </div>
</div>
```

**Step 3.5 — mount in the shell.** Edit `admin-layout.component.ts` imports to include `ToastContainerComponent`, and `admin-layout.component.html` to add `<app-toast-container></app-toast-container>` once, at the top of `.admin-shell`. Add one assertion to `admin-layout.component.spec.ts`:

```typescript
it('renders the toast container', () => {
  const el = fixture.nativeElement.querySelector('app-toast-container');
  expect(el).toBeTruthy();
});
```

**Step 3.6 — RED/GREEN: `status-badge.component.spec.ts` / `.ts`** (simplest component, no template file — inline template):

```typescript
// status-badge.component.spec.ts
import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusBadgeComponent } from './status-badge.component';

describe('StatusBadgeComponent', () => {
  let fixture: ComponentFixture<StatusBadgeComponent>;
  let component: StatusBadgeComponent;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [StatusBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatusBadgeComponent);
    component = fixture.componentInstance;
  });

  it('renders the status text capitalized', () => {
    fixture.componentRef.setInput('status', 'confirmed');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('Confirmed');
  });

  it('applies the success color for delivered', () => {
    fixture.componentRef.setInput('status', 'delivered');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('span').className).toContain('text-bg-success');
  });

  it('applies the danger color for cancelled', () => {
    fixture.componentRef.setInput('status', 'cancelled');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('span').className).toContain('text-bg-danger');
  });

  it('applies a neutral color for an unrecognized status', () => {
    fixture.componentRef.setInput('status', 'unknown-status');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('span').className).toContain('text-bg-secondary');
  });
});
```

```typescript
// status-badge.component.ts
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

const COLOR_MAP: Record<string, string> = {
  pending: 'text-bg-secondary',
  confirmed: 'text-bg-primary',
  shipped: 'text-bg-info',
  delivered: 'text-bg-success',
  cancelled: 'text-bg-danger',
  approved: 'text-bg-success',
  rejected: 'text-bg-danger',
  active: 'text-bg-success',
  inactive: 'text-bg-secondary',
  suspended: 'text-bg-warning',
};

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule],
  template: `<span class="badge" [class]="colorClass">{{ label }}</span>`,
})
export class StatusBadgeComponent {
  @Input() status = '';

  get colorClass(): string {
    return COLOR_MAP[this.status.toLowerCase()] ?? 'text-bg-secondary';
  }

  get label(): string {
    return this.status.length ? this.status[0].toUpperCase() + this.status.slice(1) : '';
  }
}
```

**Step 3.7 — RED/GREEN: `kpi-card.component.spec.ts` / `.ts` / `.html`.**

```typescript
// kpi-card.component.spec.ts
import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { KpiCardComponent } from './kpi-card.component';

describe('KpiCardComponent', () => {
  let fixture: ComponentFixture<KpiCardComponent>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [KpiCardComponent] }).compileComponents();
    fixture = TestBed.createComponent(KpiCardComponent);
    fixture.componentRef.setInput('label', 'Revenue');
    fixture.componentRef.setInput('value', '$12,400');
    fixture.componentRef.setInput('icon', 'cash-stack');
  });

  it('renders label and value', () => {
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Revenue');
    expect(text).toContain('$12,400');
  });

  it('renders the icon class', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('i').className).toContain('bi-cash-stack');
  });

  it('shows an up-trend indicator when trend is up', () => {
    fixture.componentRef.setInput('trend', 'up');
    fixture.componentRef.setInput('trendLabel', '+12% vs last period');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.text-success')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('+12% vs last period');
  });

  it('shows a down-trend indicator when trend is down', () => {
    fixture.componentRef.setInput('trend', 'down');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.text-danger')).toBeTruthy();
  });

  it('renders no trend indicator when trend is not provided', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.kpi-trend')).toBeFalsy();
  });
});
```

```typescript
// kpi-card.component.ts
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './kpi-card.component.html',
})
export class KpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string | number;
  @Input({ required: true }) icon!: string;
  @Input() trend?: 'up' | 'down' | 'flat';
  @Input() trendLabel?: string;
}
```

```html
<div class="card h-100">
  <div class="card-body d-flex align-items-center">
    <div class="rounded-circle bg-light p-3 me-3">
      <i class="bi" [class]="'bi-' + icon" style="font-size: 1.5rem"></i>
    </div>
    <div>
      <div class="text-muted small">{{ label }}</div>
      <div class="fs-4 fw-bold">{{ value }}</div>
      <div class="kpi-trend small" *ngIf="trend">
        <span [class.text-success]="trend === 'up'" [class.text-danger]="trend === 'down'">
          <i class="bi" [class.bi-arrow-up-short]="trend === 'up'" [class.bi-arrow-down-short]="trend === 'down'" [class.bi-dash]="trend === 'flat'"></i>
          {{ trendLabel }}
        </span>
      </div>
    </div>
  </div>
</div>
```

**Step 3.8 — RED/GREEN: `confirm-modal.component.spec.ts` / `.ts` / `.html`** (built on the existing `ModalComponent` pattern from `shared/components/modal`, but adds the typed-confirmation input needed for tenant delete in Task 11):

```typescript
// confirm-modal.component.spec.ts
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmModalComponent } from './confirm-modal.component';

describe('ConfirmModalComponent', () => {
  let fixture: ComponentFixture<ConfirmModalComponent>;
  let component: ConfirmModalComponent;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [ConfirmModalComponent] }).compileComponents();
    fixture = TestBed.createComponent(ConfirmModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('isOpen', true);
    fixture.componentRef.setInput('title', 'Cancel order');
    fixture.componentRef.setInput('message', 'This cannot be undone.');
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('does not render when isOpen is false', () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.modal')).toBeFalsy();
  });

  it('emits confirmed when the confirm button is clicked and no typed confirmation is required', () => {
    vi.spyOn(component.confirmed, 'emit');
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]');
    button.click();
    expect(component.confirmed.emit).toHaveBeenCalled();
  });

  it('emits cancelled when the cancel button is clicked', () => {
    vi.spyOn(component.cancelled, 'emit');
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="cancel-btn"]');
    button.click();
    expect(component.cancelled.emit).toHaveBeenCalled();
  });

  it('disables confirm until the typed confirmation matches', () => {
    fixture.componentRef.setInput('requireTypedConfirmation', 'my-tenant');
    fixture.detectChanges();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]');
    expect(button.disabled).toBe(true);

    component.typedValue = 'my-tenant';
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
  });

  it('resets the typed value when reopened', () => {
    fixture.componentRef.setInput('requireTypedConfirmation', 'my-tenant');
    component.typedValue = 'my-tenant';
    fixture.componentRef.setInput('isOpen', false);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    expect(component.typedValue).toBe('');
  });
});
```

```typescript
// confirm-modal.component.ts
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-confirm-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './confirm-modal.component.html',
})
export class ConfirmModalComponent implements OnChanges {
  @Input() isOpen = false;
  @Input({ required: true }) title!: string;
  @Input({ required: true }) message!: string;
  @Input() confirmLabel = 'Confirm';
  @Input() cancelLabel = 'Cancel';
  @Input() tone: 'primary' | 'danger' = 'primary';
  @Input() requireTypedConfirmation?: string;
  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  typedValue = '';

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen) {
      this.typedValue = '';
    }
  }

  get canConfirm(): boolean {
    return !this.requireTypedConfirmation || this.typedValue === this.requireTypedConfirmation;
  }

  onConfirm(): void {
    if (this.canConfirm) {
      this.confirmed.emit();
    }
  }

  onCancel(): void {
    this.cancelled.emit();
  }
}
```

```html
<div class="modal d-block" tabindex="-1" role="dialog" *ngIf="isOpen" aria-modal="true" [attr.aria-label]="title">
  <div class="modal-dialog">
    <div class="modal-content">
      <div class="modal-header" [class.bg-danger]="tone === 'danger'" [class.text-white]="tone === 'danger'">
        <h5 class="modal-title">{{ title }}</h5>
      </div>
      <div class="modal-body">
        <p>{{ message }}</p>
        <div *ngIf="requireTypedConfirmation" class="mb-2">
          <label for="typedConfirm" class="form-label">
            Type <strong>{{ requireTypedConfirmation }}</strong> to confirm
          </label>
          <input id="typedConfirm" class="form-control" [(ngModel)]="typedValue" name="typedConfirm" />
        </div>
      </div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" data-testid="cancel-btn" (click)="onCancel()">{{ cancelLabel }}</button>
        <button
          type="button"
          class="btn"
          [class.btn-primary]="tone === 'primary'"
          [class.btn-danger]="tone === 'danger'"
          data-testid="confirm-btn"
          [disabled]="!canConfirm"
          (click)="onConfirm()">
          {{ confirmLabel }}
        </button>
      </div>
    </div>
  </div>
</div>
<div class="modal-backdrop show" *ngIf="isOpen"></div>
```

**Step 3.9 — RED/GREEN: `date-range-picker.component.spec.ts` / `.ts` / `.html`.**

```typescript
// date-range-picker.component.spec.ts
import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DateRangePickerComponent } from './date-range-picker.component';

describe('DateRangePickerComponent', () => {
  let fixture: ComponentFixture<DateRangePickerComponent>;
  let component: DateRangePickerComponent;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [DateRangePickerComponent] }).compileComponents();
    fixture = TestBed.createComponent(DateRangePickerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('range', { from: '2026-06-01', to: '2026-07-01' });
    fixture.detectChanges();
  });

  it('renders the current from/to values in the inputs', () => {
    const fromInput: HTMLInputElement = fixture.nativeElement.querySelector('#from-date');
    const toInput: HTMLInputElement = fixture.nativeElement.querySelector('#to-date');
    expect(fromInput.value).toBe('2026-06-01');
    expect(toInput.value).toBe('2026-07-01');
  });

  it('emits rangeChange when "from" changes and is valid (from <= to)', () => {
    const spy = vi.spyOn(component.rangeChange, 'emit');
    component.onFromChange('2026-06-15');
    expect(spy).toHaveBeenCalledWith({ from: '2026-06-15', to: '2026-07-01' });
  });

  it('does not emit when "from" would be after "to"', () => {
    const spy = vi.spyOn(component.rangeChange, 'emit');
    component.onFromChange('2026-07-15');
    expect(spy).not.toHaveBeenCalled();
    expect(component.validationError).toBeTruthy();
  });

  it('does not emit when the range exceeds 366 days (matches backend max)', () => {
    const spy = vi.spyOn(component.rangeChange, 'emit');
    component.onToChange('2028-01-01');
    expect(spy).not.toHaveBeenCalled();
    expect(component.validationError).toContain('366');
  });
});
```

```typescript
// date-range-picker.component.ts
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface DateRange { from: string; to: string; }

const MAX_RANGE_DAYS = 366;

@Component({
  selector: 'app-date-range-picker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './date-range-picker.component.html',
})
export class DateRangePickerComponent {
  @Input({ required: true }) range!: DateRange;
  @Output() rangeChange = new EventEmitter<DateRange>();

  validationError = '';

  onFromChange(value: string): void {
    this.tryEmit({ from: value, to: this.range.to });
  }

  onToChange(value: string): void {
    this.tryEmit({ from: this.range.from, to: value });
  }

  private tryEmit(next: DateRange): void {
    const from = new Date(next.from);
    const to = new Date(next.to);
    if (from > to) {
      this.validationError = 'Start date must be before end date.';
      return;
    }
    const days = (to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24);
    if (days > MAX_RANGE_DAYS) {
      this.validationError = `Range cannot exceed ${MAX_RANGE_DAYS} days.`;
      return;
    }
    this.validationError = '';
    this.rangeChange.emit(next);
  }
}
```

```html
<div class="d-flex align-items-end gap-2 flex-wrap">
  <div>
    <label for="from-date" class="form-label small mb-0">From</label>
    <input
      id="from-date"
      type="date"
      class="form-control form-control-sm"
      [value]="range.from"
      (change)="onFromChange($any($event.target).value)" />
  </div>
  <div>
    <label for="to-date" class="form-label small mb-0">To</label>
    <input
      id="to-date"
      type="date"
      class="form-control form-control-sm"
      [value]="range.to"
      (change)="onToChange($any($event.target).value)" />
  </div>
  <div class="text-danger small" *ngIf="validationError" role="alert">{{ validationError }}</div>
</div>
```

**Step 3.10 — RED/GREEN: `data-table.component.spec.ts` / `.ts` / `.html`** (generic server-paged/sorted table; `<T>` kept as `Record<string, unknown>` at the template layer since Angular templates cannot use generics directly — the component class stays generic for callers):

```typescript
// data-table.component.spec.ts
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DataTableComponent } from './data-table.component';

interface Row { id: string; name: string; amount: number; }

describe('DataTableComponent', () => {
  let fixture: ComponentFixture<DataTableComponent<Row>>;
  let component: DataTableComponent<Row>;

  const rows: Row[] = [
    { id: '1', name: 'Alice', amount: 10 },
    { id: '2', name: 'Bob', amount: 20 },
  ];

  beforeEach(async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [DataTableComponent] }).compileComponents();
    fixture = TestBed.createComponent(DataTableComponent as any);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('columns', [
      { key: 'name', header: 'Name', sortable: true },
      { key: 'amount', header: 'Amount', sortable: true, cellTemplate: 'currency' },
    ]);
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('totalCount', 2);
    fixture.componentRef.setInput('pageNumber', 1);
    fixture.componentRef.setInput('pageSize', 20);
    fixture.componentRef.setInput('sortKey', null);
    fixture.componentRef.setInput('sortDirection', 'asc');
    fixture.componentRef.setInput('loading', false);
    fixture.componentRef.setInput('emptyMessage', 'No results');
    fixture.detectChanges();
  });

  it('renders one row per data row', () => {
    const trs = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(trs.length).toBe(2);
  });

  it('renders column headers', () => {
    const text = fixture.nativeElement.querySelector('thead').textContent;
    expect(text).toContain('Name');
    expect(text).toContain('Amount');
  });

  it('shows a loading state instead of rows', () => {
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="table-loading"]')).toBeTruthy();
  });

  it('shows the empty message when there are no rows and not loading', () => {
    fixture.componentRef.setInput('rows', []);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No results');
  });

  it('emits sortChange when a sortable header is clicked', () => {
    const spy = vi.spyOn(component.sortChange, 'emit');
    const header: HTMLElement = fixture.nativeElement.querySelector('[data-sort-key="name"]');
    header.click();
    expect(spy).toHaveBeenCalledWith({ key: 'name', direction: 'asc' });
  });

  it('toggles sort direction when the same column is clicked twice', () => {
    const spy = vi.spyOn(component.sortChange, 'emit');
    fixture.componentRef.setInput('sortKey', 'name');
    fixture.componentRef.setInput('sortDirection', 'asc');
    fixture.detectChanges();
    const header: HTMLElement = fixture.nativeElement.querySelector('[data-sort-key="name"]');
    header.click();
    expect(spy).toHaveBeenCalledWith({ key: 'name', direction: 'desc' });
  });

  it('computes total pages and emits pageChange', () => {
    fixture.componentRef.setInput('totalCount', 45);
    fixture.componentRef.setInput('pageSize', 20);
    fixture.detectChanges();
    expect(component.totalPages).toBe(3);

    const spy = vi.spyOn(component.pageChange, 'emit');
    component.goToPage(2);
    expect(spy).toHaveBeenCalledWith(2);
  });

  it('does not emit pageChange for an out-of-range page', () => {
    const spy = vi.spyOn(component.pageChange, 'emit');
    component.goToPage(0);
    component.goToPage(999);
    expect(spy).not.toHaveBeenCalled();
  });
});
```

```typescript
// data-table.component.ts
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface DataTableColumn<T> {
  key: keyof T & string;
  header: string;
  sortable?: boolean;
  cellTemplate?: 'text' | 'currency' | 'date' | 'custom';
}

@Component({
  selector: 'app-data-table',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './data-table.component.html',
})
export class DataTableComponent<T extends Record<string, unknown>> {
  @Input({ required: true }) columns!: DataTableColumn<T>[];
  @Input({ required: true }) rows!: T[];
  @Input() totalCount = 0;
  @Input() pageNumber = 1;
  @Input() pageSize = 20;
  @Input() sortKey: string | null = null;
  @Input() sortDirection: 'asc' | 'desc' = 'asc';
  @Input() loading = false;
  @Input() emptyMessage = 'No results found.';

  @Output() pageChange = new EventEmitter<number>();
  @Output() sortChange = new EventEmitter<{ key: string; direction: 'asc' | 'desc' }>();

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  cellValue(row: T, column: DataTableColumn<T>): unknown {
    return row[column.key];
  }

  onSort(column: DataTableColumn<T>): void {
    if (!column.sortable) return;
    const direction: 'asc' | 'desc' =
      this.sortKey === column.key && this.sortDirection === 'asc' ? 'desc' : 'asc';
    this.sortChange.emit({ key: column.key, direction });
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.pageChange.emit(page);
  }
}
```

```html
<div class="table-responsive">
  <table class="table table-hover align-middle">
    <thead>
      <tr>
        <th
          *ngFor="let col of columns"
          [attr.data-sort-key]="col.sortable ? col.key : null"
          [class.sortable]="col.sortable"
          role="columnheader"
          [attr.aria-sort]="sortKey === col.key ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'"
          (click)="onSort(col)"
          style="cursor: pointer">
          {{ col.header }}
          <i
            class="bi"
            *ngIf="col.sortable"
            [class.bi-arrow-up]="sortKey === col.key && sortDirection === 'asc'"
            [class.bi-arrow-down]="sortKey === col.key && sortDirection === 'desc'"
            [class.bi-arrow-down-up]="sortKey !== col.key"></i>
        </th>
      </tr>
    </thead>
    <tbody>
      <tr *ngFor="let row of rows">
        <td *ngFor="let col of columns">
          <ng-container [ngSwitch]="col.cellTemplate">
            <span *ngSwitchCase="'currency'">{{ (cellValue(row, col) | number: '1.2-2') }}</span>
            <span *ngSwitchCase="'date'">{{ cellValue(row, col) | date: 'medium' }}</span>
            <span *ngSwitchDefault>{{ cellValue(row, col) }}</span>
          </ng-container>
        </td>
      </tr>
    </tbody>
  </table>

  <div *ngIf="loading" data-testid="table-loading" class="text-center py-4">
    <div class="spinner-border" role="status"><span class="visually-hidden">Loading…</span></div>
  </div>

  <div *ngIf="!loading && rows.length === 0" class="text-center text-muted py-4">
    {{ emptyMessage }}
  </div>

  <nav *ngIf="!loading && rows.length > 0" aria-label="Table pagination" class="d-flex justify-content-between align-items-center mt-2">
    <span class="text-muted small">{{ totalCount }} total</span>
    <ul class="pagination pagination-sm mb-0">
      <li class="page-item" [class.disabled]="pageNumber === 1">
        <button class="page-link" (click)="goToPage(pageNumber - 1)" aria-label="Previous page">Previous</button>
      </li>
      <li class="page-item disabled"><span class="page-link">{{ pageNumber }} / {{ totalPages }}</span></li>
      <li class="page-item" [class.disabled]="pageNumber === totalPages">
        <button class="page-link" (click)="goToPage(pageNumber + 1)" aria-label="Next page">Next</button>
      </li>
    </ul>
  </nav>
</div>
```

Note the `[cellTemplate] | number`/`date` pipes require `CommonModule` (already imported) — no extra module needed.

**Step 3.11 — barrel.**

```typescript
// admin/shared/index.ts
export * from './services/toast.service';
export * from './components/toast-container/toast-container.component';
export * from './components/data-table/data-table.component';
export * from './components/kpi-card/kpi-card.component';
export * from './components/confirm-modal/confirm-modal.component';
export * from './components/date-range-picker/date-range-picker.component';
export * from './components/status-badge/status-badge.component';
```

### Verification

```
npm run test:ci -- --run toast.service.spec toast-container.component.spec data-table.component.spec kpi-card.component.spec confirm-modal.component.spec date-range-picker.component.spec status-badge.component.spec admin-layout.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 4 — API layer & contract reconciliation

### Files
- Create: `fashionsaas-storefront/src/app/admin/shared/models/order-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/models/report.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/services/order-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/services/order-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/services/report-api.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/shared/services/report-api.service.spec.ts`
- Edit: `fashionsaas-storefront/src/environments/environment.ts`
- Edit: `fashionsaas-storefront/src/environments/environment.prod.ts`
- Edit: `fashionsaas-storefront/src/app/features/checkout/services/order.service.ts`
- Edit: `fashionsaas-storefront/src/app/features/checkout/services/order.service.spec.ts`
- Edit: `fashionsaas-storefront/src/app/features/account/services/account.service.ts`
- Edit: `fashionsaas-storefront/src/app/features/account/services/account.service.spec.ts`
- Edit: `fashionsaas-storefront/src/app/features/account/models/account.model.ts`
- Edit: `fashionsaas-storefront/src/app/admin/shared/index.ts` (add new exports)

### Interfaces

**Consumes** (backend, confirmed against `ApiUrl.cs`, `OrderDtos.cs`, `ReportDtos.cs`): `TenantOrders.{GetAll,GetById,Confirm,Ship,Deliver,Cancel}` under `api/tenant/orders`; `TenantReports.{Summary,SalesOverTime,TopProducts,StatusBreakdown,CustomerAnalytics,InventoryTrends,CategorySales}` under `api/tenant/reports`, each accepting `?format=csv` for a raw `text/csv` file response instead of `ResponseData<T>`; `StoreOrders.Create` = `api/store/orders` accepting `CreateOrderRequest`. All non-CSV responses are `ApiResponse<T>` (`{statusCode, message, data, errors, timestamp}` per `core/models/api-response.model.ts`, already the working envelope shape — matches backend `ResponseData<T>`'s `isSuccess/statusCode/message/data` fields structurally through `data`).

**Produces** (consumed verbatim by Tasks 5–11):

```typescript
// admin/shared/models/order-admin.model.ts
export type OrderStatus = 'pending' | 'confirmed' | 'shipped' | 'delivered' | 'cancelled';

export interface OrderVariant {
  size?: string;
  color?: string;
}

export interface ShippingAddress {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
}

export interface OrderItemDto {
  productId: string;
  productName: string;
  price: number;
  quantity: number;
  variant?: OrderVariant;
}

export interface OrderDto {
  orderId: string;        // order-number, e.g. "ORD-2026-000001" — used for search/display
  id: string;              // internal guid — used for admin detail route + action calls
  customerId: string;
  orderDate: string;       // ISO
  status: OrderStatus;
  items: OrderItemDto[];
  shippingAddress: ShippingAddress;
  subtotal: number;
  tax: number;
  shippingCost: number;
  total: number;
  trackingNumber?: string | null;
}

export interface CreateOrderItemRequest {
  productId: string;
  quantity: number;
  variant?: OrderVariant;
}

export interface CreateOrderRequest {
  shippingAddress: ShippingAddress;
  paymentInfo: { cardholderName: string; cardNumber: string };
  items: CreateOrderItemRequest[];
}

export interface OrderFilter {
  status?: OrderStatus;
  from?: string;           // ISO date
  to?: string;             // ISO date
  customerId?: string;
  customerEmail?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}
```

```typescript
// admin/shared/models/report.model.ts
export interface SummaryReport {
  revenue: number;
  orderCount: number;
  avgOrderValue: number;
  newCustomers: number;
  pendingReviews: number;
  lowStockCount: number;
}

export interface SalesPoint {
  periodStart: string;
  revenue: number;
  orderCount: number;
}

export interface TopProduct {
  productId: string;
  productName: string;
  revenue: number;
  units: number;
}

export interface StatusBreakdown {
  status: string;
  count: number;
  revenue: number;
}

export interface TopCustomer {
  customerId: string;
  email: string;
  totalSpend: number;
  orderCount: number;
}

export interface CustomerAnalytics {
  newCustomersOverTime: SalesPoint[];
  repeatPurchaseRate: number;
  topCustomers: TopCustomer[];
}

export interface LowStockItem {
  variantId: string;
  productName: string;
  sku: string;
  stockQuantity: number;
}

export interface InventoryTrends {
  adjustmentsOverTime: SalesPoint[];
  lowStock: LowStockItem[];
}

export interface CategorySales {
  categoryId: string;
  categoryName: string;
  revenue: number;
  units: number;
}

export type ReportInterval = 'Day' | 'Week' | 'Month';

export interface ReportDateParams {
  from: string;  // ISO yyyy-MM-dd
  to: string;
}
```

`OrderAdminService` public surface:

```typescript
getOrders(filter: OrderFilter): Observable<PagedResult<OrderDto>>;
getOrder(id: string): Observable<OrderDto>;
confirm(id: string): Observable<OrderDto>;
ship(id: string, trackingNumber?: string): Observable<OrderDto>;
deliver(id: string): Observable<OrderDto>;
cancel(id: string, reason: string): Observable<OrderDto>;
```

`ReportApiService` public surface:

```typescript
getSummary(params: ReportDateParams): Observable<SummaryReport>;
getSalesOverTime(params: ReportDateParams, interval: ReportInterval): Observable<SalesPoint[]>;
getTopProducts(params: ReportDateParams, take: number, by: string): Observable<TopProduct[]>;
getStatusBreakdown(params: ReportDateParams): Observable<StatusBreakdown[]>;
getCustomerAnalytics(params: ReportDateParams, interval: ReportInterval): Observable<CustomerAnalytics>;
getInventoryTrends(params: ReportDateParams): Observable<InventoryTrends>;
getCategorySales(params: ReportDateParams, categoryId?: string): Observable<CategorySales[]>;
downloadCsv(report: string, params: Record<string, string>): void;  // report: 'summary'|'sales-over-time'|'top-products'|'order-status-breakdown'|'customer-analytics'|'inventory-trends'|'category-sales'
```

### TDD steps

**Step 4.1 — RED: environment fix has no direct spec (matches existing convention — untested config files); fixed as part of Step 4.2's service specs which assert on the corrected base URL.**

**Step 4.2 — GREEN: fix both environment files.**

```typescript
// environment.ts
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000/api',
  tenantSlug: 'default-tenant',
};
```

```typescript
// environment.prod.ts
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.fashionsaas.com/api',
  tenantSlug: '',
};
```

**Step 4.3 — RED: `order-admin.service.spec.ts`** (mirrors the `HttpClientTestingModule`/`HttpTestingController` convention confirmed in the existing `order.service.spec.ts`):

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { OrderAdminService } from './order-admin.service';
import { OrderDto } from '../models/order-admin.model';

describe('OrderAdminService', () => {
  let service: OrderAdminService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/tenant/orders`;

  const order: OrderDto = {
    orderId: 'ORD-2026-000001',
    id: 'guid-1',
    customerId: 'cust-1',
    orderDate: '2026-07-01T00:00:00Z',
    status: 'pending',
    items: [],
    shippingAddress: {
      firstName: 'A', lastName: 'B', email: 'a@b.com', phone: '555',
      street: 's', city: 'c', state: 'st', zipCode: 'z', country: 'US',
    },
    subtotal: 10, tax: 1, shippingCost: 0, total: 11,
  };

  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [OrderAdminService, ApiService],
    });
    service = TestBed.inject(OrderAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets a paged list of orders with filter params', () => {
    service.getOrders({ status: 'pending', page: 2, pageSize: 10 }).subscribe();
    const req = httpMock.expectOne(
      (r) => r.url === base && r.params.get('status') === 'pending' && r.params.get('page') === '2'
    );
    expect(req.request.method).toBe('GET');
    req.flush(wrap({ items: [order], totalCount: 1, pageNumber: 2, pageSize: 10, totalPages: 1 }));
  });

  it('gets a single order by id', () => {
    service.getOrder('guid-1').subscribe((o) => expect(o.id).toBe('guid-1'));
    const req = httpMock.expectOne(`${base}/guid-1`);
    expect(req.request.method).toBe('GET');
    req.flush(wrap(order));
  });

  it('confirms an order', () => {
    service.confirm('guid-1').subscribe();
    const req = httpMock.expectOne(`${base}/guid-1/confirm`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap(order));
  });

  it('ships an order with a tracking number', () => {
    service.ship('guid-1', 'TRACK123').subscribe();
    const req = httpMock.expectOne(`${base}/guid-1/ship`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ trackingNumber: 'TRACK123' });
    req.flush(wrap(order));
  });

  it('ships an order without a tracking number', () => {
    service.ship('guid-1').subscribe();
    const req = httpMock.expectOne(`${base}/guid-1/ship`);
    expect(req.request.body).toEqual({ trackingNumber: null });
    req.flush(wrap(order));
  });

  it('marks an order delivered', () => {
    service.deliver('guid-1').subscribe();
    const req = httpMock.expectOne(`${base}/guid-1/deliver`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap(order));
  });

  it('cancels an order with a reason', () => {
    service.cancel('guid-1', 'Customer request').subscribe();
    const req = httpMock.expectOne(`${base}/guid-1/cancel`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ reason: 'Customer request' });
    req.flush(wrap(order));
  });
});
```

**Step 4.4 — GREEN: `order-admin.service.ts`.**

```typescript
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse, PagedResult } from '../../../core/models/api-response.model';
import { OrderDto, OrderFilter } from '../models/order-admin.model';

@Injectable({ providedIn: 'root' })
export class OrderAdminService {
  private readonly base = 'tenant/orders';

  constructor(private apiService: ApiService) {}

  getOrders(filter: OrderFilter): Observable<PagedResult<OrderDto>> {
    let params = new HttpParams();
    if (filter.status) params = params.set('status', filter.status);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    if (filter.customerId) params = params.set('customerId', filter.customerId);
    if (filter.customerEmail) params = params.set('customerEmail', filter.customerEmail);
    if (filter.search) params = params.set('search', filter.search);
    params = params.set('page', String(filter.page ?? 1));
    params = params.set('pageSize', String(filter.pageSize ?? 20));

    return this.apiService
      .get<PagedResult<OrderDto>>(this.base, params)
      .pipe(map((response: ApiResponse<PagedResult<OrderDto>>) => response.data));
  }

  getOrder(id: string): Observable<OrderDto> {
    return this.apiService
      .get<OrderDto>(`${this.base}/${id}`)
      .pipe(map((response: ApiResponse<OrderDto>) => response.data));
  }

  confirm(id: string): Observable<OrderDto> {
    return this.apiService
      .put<OrderDto>(`${this.base}/${id}/confirm`, {})
      .pipe(map((response: ApiResponse<OrderDto>) => response.data));
  }

  ship(id: string, trackingNumber?: string): Observable<OrderDto> {
    return this.apiService
      .put<OrderDto>(`${this.base}/${id}/ship`, { trackingNumber: trackingNumber ?? null })
      .pipe(map((response: ApiResponse<OrderDto>) => response.data));
  }

  deliver(id: string): Observable<OrderDto> {
    return this.apiService
      .put<OrderDto>(`${this.base}/${id}/deliver`, {})
      .pipe(map((response: ApiResponse<OrderDto>) => response.data));
  }

  cancel(id: string, reason: string): Observable<OrderDto> {
    return this.apiService
      .put<OrderDto>(`${this.base}/${id}/cancel`, { reason })
      .pipe(map((response: ApiResponse<OrderDto>) => response.data));
  }
}
```

`ApiService.get` must accept `HttpParams`, already true. Confirm the request URL assertion style used above matches predicate-based `httpMock.expectOne` (needed because `HttpParams` is appended by Angular's `HttpClient`, not part of the base string).

**Step 4.5 — RED: `report-api.service.spec.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { ReportApiService } from './report-api.service';

describe('ReportApiService', () => {
  let service: ReportApiService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/tenant/reports`;
  const range = { from: '2026-06-01', to: '2026-07-01' };
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ReportApiService, ApiService],
    });
    service = TestBed.inject(ReportApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets the summary report', () => {
    service.getSummary(range).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/summary`);
    expect(req.request.params.get('from')).toBe('2026-06-01');
    req.flush(wrap({ revenue: 100, orderCount: 2, avgOrderValue: 50, newCustomers: 1, pendingReviews: 0, lowStockCount: 0 }));
  });

  it('gets sales-over-time with an interval', () => {
    service.getSalesOverTime(range, 'Week').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/sales-over-time` && r.params.get('interval') === 'Week');
    req.flush(wrap([]));
  });

  it('gets top products with take and sort key', () => {
    service.getTopProducts(range, 5, 'units').subscribe();
    const req = httpMock.expectOne(
      (r) => r.url === `${base}/top-products` && r.params.get('take') === '5' && r.params.get('by') === 'units'
    );
    req.flush(wrap([]));
  });

  it('gets order status breakdown', () => {
    service.getStatusBreakdown(range).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/order-status-breakdown`);
    req.flush(wrap([]));
  });

  it('gets customer analytics', () => {
    service.getCustomerAnalytics(range, 'Month').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/customer-analytics`);
    req.flush(wrap({ newCustomersOverTime: [], repeatPurchaseRate: 0, topCustomers: [] }));
  });

  it('gets inventory trends', () => {
    service.getInventoryTrends(range).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/inventory-trends`);
    req.flush(wrap({ adjustmentsOverTime: [], lowStock: [] }));
  });

  it('gets category sales, optionally scoped to a category', () => {
    service.getCategorySales(range, 'cat-1').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/category-sales` && r.params.get('categoryId') === 'cat-1');
    req.flush(wrap([]));
  });
});
```

CSV download is exercised in a separate lightweight spec that stubs DOM APIs (mirrors no existing convention in-repo, so this plan introduces the minimal necessary mock):

```typescript
// appended to report-api.service.spec.ts
describe('ReportApiService.downloadCsv', () => {
  let service: ReportApiService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/tenant/reports`;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ReportApiService, ApiService],
    });
    service = TestBed.inject(ReportApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests the CSV as a blob and triggers a download', () => {
    const clickSpy = vi.fn();
    const createElementSpy = vi.spyOn(document, 'createElement').mockReturnValue({
      set href(_v: string) {},
      set download(_v: string) {},
      click: clickSpy,
    } as unknown as HTMLAnchorElement);
    vi.spyOn(window.URL, 'createObjectURL').mockReturnValue('blob:mock');
    vi.spyOn(window.URL, 'revokeObjectURL').mockImplementation(() => {});

    service.downloadCsv('summary', { from: '2026-06-01', to: '2026-07-01' });

    const req = httpMock.expectOne(
      (r) => r.url === `${base}/summary` && r.params.get('format') === 'csv'
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['csv,data']));

    expect(clickSpy).toHaveBeenCalled();
    createElementSpy.mockRestore();
  });
});
```

**Step 4.6 — GREEN: `report-api.service.ts`.**

```typescript
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse } from '../../../core/models/api-response.model';
import {
  SummaryReport, SalesPoint, TopProduct, StatusBreakdown,
  CustomerAnalytics, InventoryTrends, CategorySales,
  ReportInterval, ReportDateParams,
} from '../models/report.model';

const REPORT_PATHS: Record<string, string> = {
  summary: 'summary',
  'sales-over-time': 'sales-over-time',
  'top-products': 'top-products',
  'order-status-breakdown': 'order-status-breakdown',
  'customer-analytics': 'customer-analytics',
  'inventory-trends': 'inventory-trends',
  'category-sales': 'category-sales',
};

@Injectable({ providedIn: 'root' })
export class ReportApiService {
  private readonly base = 'tenant/reports';

  constructor(private apiService: ApiService, private http: HttpClient) {}

  private rangeParams(params: ReportDateParams): HttpParams {
    return new HttpParams().set('from', params.from).set('to', params.to);
  }

  getSummary(params: ReportDateParams): Observable<SummaryReport> {
    return this.apiService
      .get<SummaryReport>(`${this.base}/summary`, this.rangeParams(params))
      .pipe(map((r: ApiResponse<SummaryReport>) => r.data));
  }

  getSalesOverTime(params: ReportDateParams, interval: ReportInterval): Observable<SalesPoint[]> {
    const p = this.rangeParams(params).set('interval', interval);
    return this.apiService
      .get<SalesPoint[]>(`${this.base}/sales-over-time`, p)
      .pipe(map((r: ApiResponse<SalesPoint[]>) => r.data));
  }

  getTopProducts(params: ReportDateParams, take: number, by: string): Observable<TopProduct[]> {
    const p = this.rangeParams(params).set('take', String(take)).set('by', by);
    return this.apiService
      .get<TopProduct[]>(`${this.base}/top-products`, p)
      .pipe(map((r: ApiResponse<TopProduct[]>) => r.data));
  }

  getStatusBreakdown(params: ReportDateParams): Observable<StatusBreakdown[]> {
    return this.apiService
      .get<StatusBreakdown[]>(`${this.base}/order-status-breakdown`, this.rangeParams(params))
      .pipe(map((r: ApiResponse<StatusBreakdown[]>) => r.data));
  }

  getCustomerAnalytics(params: ReportDateParams, interval: ReportInterval): Observable<CustomerAnalytics> {
    const p = this.rangeParams(params).set('interval', interval);
    return this.apiService
      .get<CustomerAnalytics>(`${this.base}/customer-analytics`, p)
      .pipe(map((r: ApiResponse<CustomerAnalytics>) => r.data));
  }

  getInventoryTrends(params: ReportDateParams): Observable<InventoryTrends> {
    return this.apiService
      .get<InventoryTrends>(`${this.base}/inventory-trends`, this.rangeParams(params))
      .pipe(map((r: ApiResponse<InventoryTrends>) => r.data));
  }

  getCategorySales(params: ReportDateParams, categoryId?: string): Observable<CategorySales[]> {
    let p = this.rangeParams(params);
    if (categoryId) p = p.set('categoryId', categoryId);
    return this.apiService
      .get<CategorySales[]>(`${this.base}/category-sales`, p)
      .pipe(map((r: ApiResponse<CategorySales[]>) => r.data));
  }

  downloadCsv(report: string, params: Record<string, string>): void {
    const path = REPORT_PATHS[report] ?? report;
    let httpParams = new HttpParams({ fromObject: params }).set('format', 'csv');
    this.http
      .get(`${environment.apiBaseUrl}/${this.base}/${path}`, { params: httpParams, responseType: 'blob' })
      .subscribe((blob: Blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${path}-${params['from']}-${params['to']}.csv`;
        a.click();
        window.URL.revokeObjectURL(url);
      });
  }
}
```

**Step 4.7 — RED/GREEN: checkout `OrderService` repoint (conflict #3).** Update the existing `order.service.spec.ts` first (RED — the assertions below fail against the current implementation because the URL and body shape both change):

```typescript
// order.service.spec.ts — replace ordersUrl and the createOrder test body/assertions
const ordersUrl = `${environment.apiBaseUrl}/store/orders`;
// ...
it('should create an order with only backend-accepted fields', () => {
  // ...same shippingAddress/checkoutForm/cartItems fixtures as before...
  service.createOrder(checkoutForm, cartItems).subscribe();

  const req = httpMock.expectOne(ordersUrl);
  expect(req.request.method).toBe('POST');
  expect(req.request.body).toEqual({
    shippingAddress,
    paymentInfo: { cardholderName: 'John Doe', cardNumber: '****1111' },
    items: [{ productId: '1', quantity: 2, variant: { size: 'M', color: 'Red' } }],
  });
  req.flush(emptyApiResponse);
});
// getOrders/getOrderById/cancelOrder tests: update ordersUrl references only (same relative paths, new base)
```

GREEN — `order.service.ts`:

```typescript
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { Order } from '../models/order.model';
import { CheckoutForm } from '../models/checkout.model';
import { CartItem } from '../../cart/models/cart.model';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse } from '../../../core/models/api-response.model';

@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly apiUrl = 'store/orders';

  constructor(private apiService: ApiService) {}

  createOrder(checkout: CheckoutForm, cartItems: CartItem[]): Observable<Order> {
    const payload = {
      shippingAddress: checkout.shippingAddress,
      paymentInfo: {
        cardholderName: checkout.paymentInfo.cardholderName,
        cardNumber: checkout.paymentInfo.cardNumber,
        // CVV, expiryMonth, expiryYear are never sent — backend CreateOrderRequest does not accept them
      },
      items: cartItems.map((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        variant: item.selectedVariant,
      })),
    };
    return this.apiService.post<Order>(this.apiUrl, payload).pipe(map((r: ApiResponse<Order>) => r.data));
  }

  getOrders(): Observable<Order[]> {
    return this.apiService.get<Order[]>(this.apiUrl).pipe(map((r: ApiResponse<Order[]>) => r.data));
  }

  getOrderById(orderId: string): Observable<Order> {
    return this.apiService.get<Order>(`${this.apiUrl}/${orderId}`).pipe(map((r: ApiResponse<Order>) => r.data));
  }

  cancelOrder(orderId: string): Observable<Order> {
    return this.apiService.put<Order>(`${this.apiUrl}/${orderId}/cancel`, {}).pipe(map((r: ApiResponse<Order>) => r.data));
  }
}
```

**Step 4.8 — RED/GREEN: `account.model.ts` status fix + `AccountService` repoint (conflict #4).** Edit `account.model.ts`:

```typescript
export interface Order {
  orderId: string;
  orderDate: Date;
  items: OrderItem[];
  subtotal: number;
  tax: number;
  total: number;
  status: 'pending' | 'confirmed' | 'shipped' | 'delivered' | 'cancelled';
  shippingAddress: Address;
}
```

Update `account.service.spec.ts` (existing file — RED first, changing `account/orders` URLs to `store/orders`):

```typescript
// account.service.spec.ts — update URL fixtures only, same test bodies otherwise
const ordersUrl = `${environment.apiBaseUrl}/store/orders`;
// getOrders test expects httpMock.expectOne matching ordersUrl with pageNumber/pageSize params
// getOrderById test expects `${ordersUrl}/${orderId}`
```

GREEN — `account.service.ts` (only `getOrders`/`getOrderById` change):

```typescript
  getOrders(page: number = 1, pageSize: number = 10): Observable<Order[]> {
    const params = new HttpParams()
      .set('pageNumber', page.toString())
      .set('pageSize', pageSize.toString());

    return this.apiService
      .get<PagedResult<Order>>('store/orders', params)
      .pipe(map((response: ApiResponse<PagedResult<Order>>) => response.data.items));
  }

  getOrderById(orderId: string): Observable<Order> {
    return this.apiService
      .get<Order>(`store/orders/${orderId}`)
      .pipe(map((response: ApiResponse<Order>) => response.data));
  }
```

**Step 4.9 — barrel update.** Add to `admin/shared/index.ts`:

```typescript
export * from './models/order-admin.model';
export * from './models/report.model';
export * from './services/order-admin.service';
export * from './services/report-api.service';
```

### Verification

```
npm run test:ci -- --run order-admin.service.spec report-api.service.spec order.service.spec account.service.spec
npm run test:ci
npm run test:ci
```

---

## Task 5 — Dashboard home

### Setup (before RED)

```
npm install ng2-charts@^7 chart.js@^4 --save
```

`ng2-charts`/`chart.js` are imported only inside `admin/dashboard/*` files, which are reached solely via `admin.routes.ts`'s `loadComponent` for the `''` child route nested under the already-lazy `/admin` branch — they never enter the initial bundle (verified in Task 11).

### Files
- Create: `fashionsaas-storefront/src/app/admin/dashboard/dashboard.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/dashboard/dashboard.component.html`
- Create: `fashionsaas-storefront/src/app/admin/dashboard/dashboard.component.spec.ts`

### Interfaces

**Consumes** (Task 4, verbatim): `ReportApiService.{getSummary, getSalesOverTime, getTopProducts, getStatusBreakdown}`; `SummaryReport`, `SalesPoint`, `TopProduct`, `StatusBreakdown`, `ReportInterval`, `ReportDateParams` from `admin/shared/models/report.model.ts`. `DateRangePickerComponent` (`[range]`/`(rangeChange)`, `DateRange` shape `{from,to}`) and `KpiCardComponent` (`[label][value][icon][trend][trendLabel]`) from Task 3's `admin/shared` barrel.

**Produces:**

```typescript
// dashboard.component.ts
interval: ReportInterval;              // 'Day' | 'Week' | 'Month', default 'Day'
range: DateRange;                      // default: last 30 days
loading: boolean;
error: string | null;
summary: SummaryReport | null;
salesChartData: ChartData<'line'>;
topProductsChartData: ChartData<'bar'>;
statusChartData: ChartData<'doughnut'>;
onRangeChange(range: DateRange): void;
onIntervalChange(interval: ReportInterval): void;
```

### TDD steps

**Step 5.1 — RED: `dashboard.component.spec.ts`.**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { ReportApiService } from '../shared/services/report-api.service';
import { SummaryReport, SalesPoint, TopProduct, StatusBreakdown } from '../shared/models/report.model';

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let mockReportApi: Partial<ReportApiService>;

  const summary: SummaryReport = {
    revenue: 15420.5, orderCount: 87, avgOrderValue: 177.24,
    newCustomers: 12, pendingReviews: 4, lowStockCount: 3,
  };
  const salesPoints: SalesPoint[] = [
    { periodStart: '2026-06-01', revenue: 1000, orderCount: 5 },
    { periodStart: '2026-06-02', revenue: 1500, orderCount: 7 },
  ];
  const topProducts: TopProduct[] = [
    { productId: 'p1', productName: 'Denim Jacket', revenue: 5000, units: 40 },
  ];
  const statusBreakdown: StatusBreakdown[] = [
    { status: 'delivered', count: 50, revenue: 10000 },
    { status: 'pending', count: 10, revenue: 1200 },
  ];

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockReportApi = {
      getSummary: vi.fn().mockReturnValue(of(summary)),
      getSalesOverTime: vi.fn().mockReturnValue(of(salesPoints)),
      getTopProducts: vi.fn().mockReturnValue(of(topProducts)),
      getStatusBreakdown: vi.fn().mockReturnValue(of(statusBreakdown)),
    };

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ReportApiService, useValue: mockReportApi }],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads and displays exact KPI values from the summary report', () => {
    expect(component.summary?.revenue).toBe(15420.5);
    expect(component.summary?.orderCount).toBe(87);
    expect(component.summary?.avgOrderValue).toBe(177.24);
    expect(component.summary?.newCustomers).toBe(12);
    expect(component.summary?.pendingReviews).toBe(4);
    expect(component.summary?.lowStockCount).toBe(3);
  });

  it('builds sales-over-time line chart data from the report points', () => {
    expect(component.salesChartData.labels).toEqual(['2026-06-01', '2026-06-02']);
    expect(component.salesChartData.datasets[0].data).toEqual([1000, 1500]);
  });

  it('builds top-products bar chart data', () => {
    expect(component.topProductsChartData.labels).toEqual(['Denim Jacket']);
    expect(component.topProductsChartData.datasets[0].data).toEqual([5000]);
  });

  it('builds status donut chart data', () => {
    expect(component.statusChartData.labels).toEqual(['delivered', 'pending']);
    expect(component.statusChartData.datasets[0].data).toEqual([50, 10]);
  });

  it('re-fetches all four reports when the date range changes', () => {
    (mockReportApi.getSummary as ReturnType<typeof vi.fn>).mockClear();
    component.onRangeChange({ from: '2026-05-01', to: '2026-05-31' });
    expect(mockReportApi.getSummary).toHaveBeenCalledWith({ from: '2026-05-01', to: '2026-05-31' });
  });

  it('re-fetches sales-over-time with the new interval on interval change', () => {
    (mockReportApi.getSalesOverTime as ReturnType<typeof vi.fn>).mockClear();
    component.onIntervalChange('Week');
    expect(component.interval).toBe('Week');
    expect(mockReportApi.getSalesOverTime).toHaveBeenCalledWith(component.range, 'Week');
  });

  it('shows a loading state while reports are in flight', () => {
    TestBed.resetTestingModule();
    let resolveFn!: (v: SummaryReport) => void;
    const pending = new Promise<SummaryReport>((res) => (resolveFn = res));
    mockReportApi = {
      getSummary: vi.fn().mockReturnValue(pending as unknown as ReturnType<ReportApiService['getSummary']>),
      getSalesOverTime: vi.fn().mockReturnValue(of(salesPoints)),
      getTopProducts: vi.fn().mockReturnValue(of(topProducts)),
      getStatusBreakdown: vi.fn().mockReturnValue(of(statusBreakdown)),
    };
    return TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ReportApiService, useValue: mockReportApi }],
    })
      .compileComponents()
      .then(() => {
        const f = TestBed.createComponent(DashboardComponent);
        f.detectChanges();
        expect(f.componentInstance.loading).toBe(true);
      });
  });

  it('shows an error message when a report call fails', () => {
    TestBed.resetTestingModule();
    mockReportApi = {
      getSummary: vi.fn().mockReturnValue(throwError(() => new Error('network'))),
      getSalesOverTime: vi.fn().mockReturnValue(of(salesPoints)),
      getTopProducts: vi.fn().mockReturnValue(of(topProducts)),
      getStatusBreakdown: vi.fn().mockReturnValue(of(statusBreakdown)),
    };
    return TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: ReportApiService, useValue: mockReportApi }],
    })
      .compileComponents()
      .then(() => {
        const f = TestBed.createComponent(DashboardComponent);
        f.detectChanges();
        expect(f.componentInstance.error).toContain('Failed to load dashboard');
      });
  });
});
```

**Step 5.2 — GREEN: `dashboard.component.ts`.**

```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin, catchError, of as rxOf } from 'rxjs';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData } from 'chart.js';
import { ReportApiService } from '../shared/services/report-api.service';
import { SummaryReport, SalesPoint, TopProduct, StatusBreakdown, ReportInterval } from '../shared/models/report.model';
import { DateRangePickerComponent, DateRange } from '../shared/components/date-range-picker/date-range-picker.component';
import { KpiCardComponent } from '../shared/components/kpi-card/kpi-card.component';

function defaultRange(): DateRange {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 30);
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  return { from: iso(from), to: iso(to) };
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, BaseChartDirective, DateRangePickerComponent, KpiCardComponent],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  interval: ReportInterval = 'Day';
  range: DateRange = defaultRange();
  loading = true;
  error: string | null = null;
  summary: SummaryReport | null = null;

  salesChartData: ChartData<'line'> = { labels: [], datasets: [{ data: [], label: 'Revenue' }] };
  topProductsChartData: ChartData<'bar'> = { labels: [], datasets: [{ data: [], label: 'Revenue' }] };
  statusChartData: ChartData<'doughnut'> = { labels: [], datasets: [{ data: [] }] };

  constructor(private reportApi: ReportApiService) {}

  ngOnInit(): void {
    this.loadAll();
  }

  onRangeChange(range: DateRange): void {
    this.range = range;
    this.loadAll();
  }

  onIntervalChange(interval: ReportInterval): void {
    this.interval = interval;
    this.reportApi.getSalesOverTime(this.range, this.interval).subscribe((points) => {
      this.applySalesPoints(points);
    });
  }

  private loadAll(): void {
    this.loading = true;
    this.error = null;
    forkJoin({
      summary: this.reportApi.getSummary(this.range),
      sales: this.reportApi.getSalesOverTime(this.range, this.interval),
      topProducts: this.reportApi.getTopProducts(this.range, 5, 'revenue'),
      status: this.reportApi.getStatusBreakdown(this.range),
    })
      .pipe(
        catchError(() => {
          this.error = 'Failed to load dashboard data. Please try again.';
          return rxOf(null);
        })
      )
      .subscribe((result) => {
        this.loading = false;
        if (!result) return;
        this.summary = result.summary;
        this.applySalesPoints(result.sales);
        this.applyTopProducts(result.topProducts);
        this.applyStatusBreakdown(result.status);
      });
  }

  private applySalesPoints(points: SalesPoint[]): void {
    this.salesChartData = {
      labels: points.map((p) => p.periodStart),
      datasets: [{ data: points.map((p) => p.revenue), label: 'Revenue' }],
    };
  }

  private applyTopProducts(products: TopProduct[]): void {
    this.topProductsChartData = {
      labels: products.map((p) => p.productName),
      datasets: [{ data: products.map((p) => p.revenue), label: 'Revenue' }],
    };
  }

  private applyStatusBreakdown(breakdown: StatusBreakdown[]): void {
    this.statusChartData = {
      labels: breakdown.map((b) => b.status),
      datasets: [{ data: breakdown.map((b) => b.count) }],
    };
  }
}
```

`dashboard.component.html`:

```html
<div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mb-3">
  <h1 class="h4 mb-0">Dashboard</h1>
  <div class="d-flex align-items-end gap-2">
    <div>
      <label for="interval-select" class="form-label small mb-0">Interval</label>
      <select id="interval-select" class="form-select form-select-sm" [value]="interval"
              (change)="onIntervalChange($any($event.target).value)">
        <option value="Day">Day</option>
        <option value="Week">Week</option>
        <option value="Month">Month</option>
      </select>
    </div>
    <app-date-range-picker [range]="range" (rangeChange)="onRangeChange($event)"></app-date-range-picker>
  </div>
</div>

<div *ngIf="error" class="alert alert-danger" role="alert">{{ error }}</div>

<div *ngIf="loading" class="text-center py-5">
  <div class="spinner-border" role="status"><span class="visually-hidden">Loading…</span></div>
</div>

<ng-container *ngIf="!loading && !error && summary as s">
  <div class="row g-3 mb-4">
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="Revenue" [value]="'$' + (s.revenue | number:'1.2-2')" icon="cash-stack"></app-kpi-card>
    </div>
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="Orders" [value]="s.orderCount" icon="bag-check"></app-kpi-card>
    </div>
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="Avg Order Value" [value]="'$' + (s.avgOrderValue | number:'1.2-2')" icon="graph-up"></app-kpi-card>
    </div>
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="New Customers" [value]="s.newCustomers" icon="person-plus"></app-kpi-card>
    </div>
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="Pending Reviews" [value]="s.pendingReviews" icon="star"></app-kpi-card>
    </div>
    <div class="col-sm-6 col-lg-4 col-xl-2">
      <app-kpi-card label="Low Stock" [value]="s.lowStockCount" icon="exclamation-triangle"></app-kpi-card>
    </div>
  </div>

  <div class="row g-3">
    <div class="col-lg-8">
      <div class="card"><div class="card-body">
        <h2 class="h6">Sales over time</h2>
        <canvas baseChart [data]="salesChartData" type="line"></canvas>
      </div></div>
    </div>
    <div class="col-lg-4">
      <div class="card"><div class="card-body">
        <h2 class="h6">Order status</h2>
        <canvas baseChart [data]="statusChartData" type="doughnut"></canvas>
      </div></div>
    </div>
    <div class="col-12">
      <div class="card"><div class="card-body">
        <h2 class="h6">Top products</h2>
        <canvas baseChart [data]="topProductsChartData" type="bar"></canvas>
      </div></div>
    </div>
  </div>
</ng-container>
```

### Verification

```
npm run test:ci -- --run dashboard.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 6 — Orders module

### Files
- Create: `fashionsaas-storefront/src/app/admin/orders/orders.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-list/order-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-list/order-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-list/order-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-detail/order-detail.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-detail/order-detail.component.html`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-detail/order-detail.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-status.utils.ts`
- Create: `fashionsaas-storefront/src/app/admin/orders/order-status.utils.spec.ts`

### Interfaces

**Consumes** (Task 4 verbatim): `OrderAdminService.{getOrders,getOrder,confirm,ship,deliver,cancel}`, `OrderDto`, `OrderFilter`, `OrderStatus`. Task 3 kit: `DataTableComponent<T>` (`[columns][rows][totalCount][pageNumber][pageSize][sortKey][sortDirection][loading][emptyMessage]`, `(pageChange)(sortChange)`), `StatusBadgeComponent` (`[status]`), `ConfirmModalComponent` (`[isOpen][title][message][confirmLabel][tone]`, `(confirmed)(cancelled)`), `ToastService.{success,error}`.

**Produces** (status-gating logic reused by Task 7's publish/archive gating pattern):

```typescript
// order-status.utils.ts
export function availableActions(status: OrderStatus): Array<'confirm' | 'ship' | 'deliver' | 'cancel'> {
  switch (status) {
    case 'pending': return ['confirm', 'cancel'];
    case 'confirmed': return ['ship', 'cancel'];
    case 'shipped': return ['deliver'];
    case 'delivered': return [];
    case 'cancelled': return [];
  }
}
```

### TDD steps

**Step 6.1 — RED/GREEN: `order-status.utils.spec.ts` / `.ts`.**

```typescript
import { describe, it, expect } from 'vitest';
import { availableActions } from './order-status.utils';

describe('availableActions', () => {
  it('pending orders can be confirmed or cancelled', () => {
    expect(availableActions('pending')).toEqual(['confirm', 'cancel']);
  });
  it('confirmed orders can be shipped or cancelled', () => {
    expect(availableActions('confirmed')).toEqual(['ship', 'cancel']);
  });
  it('shipped orders can only be delivered', () => {
    expect(availableActions('shipped')).toEqual(['deliver']);
  });
  it('delivered orders have no further actions', () => {
    expect(availableActions('delivered')).toEqual([]);
  });
  it('cancelled orders have no further actions', () => {
    expect(availableActions('cancelled')).toEqual([]);
  });
});
```

(Implementation is the switch shown in Interfaces above — write it to satisfy these directly.)

**Step 6.2 — RED: `order-list.component.spec.ts`.**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter, Router } from '@angular/router';
import { OrderListComponent } from './order-list.component';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { OrderDto } from '../../shared/models/order-admin.model';

describe('OrderListComponent', () => {
  let fixture: ComponentFixture<OrderListComponent>;
  let component: OrderListComponent;
  let mockOrderApi: Partial<OrderAdminService>;

  const orders: OrderDto[] = [
    {
      orderId: 'ORD-2026-000001', id: 'g1', customerId: 'c1', orderDate: '2026-07-01T00:00:00Z',
      status: 'pending', items: [], shippingAddress: {} as any, subtotal: 10, tax: 1, shippingCost: 0, total: 11,
    },
  ];

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockOrderApi = {
      getOrders: vi.fn().mockReturnValue(of({ items: orders, totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1 })),
    };

    await TestBed.configureTestingModule({
      imports: [OrderListComponent],
      providers: [provideRouter([]), { provide: OrderAdminService, useValue: mockOrderApi }],
    }).compileComponents();

    fixture = TestBed.createComponent(OrderListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the first page of orders on init', () => {
    expect(mockOrderApi.getOrders).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }));
    expect(component.rows.length).toBe(1);
    expect(component.totalCount).toBe(1);
  });

  it('re-queries when the status filter changes', () => {
    (mockOrderApi.getOrders as ReturnType<typeof vi.fn>).mockClear();
    component.onStatusFilterChange('confirmed');
    expect(mockOrderApi.getOrders).toHaveBeenCalledWith(expect.objectContaining({ status: 'confirmed', page: 1 }));
  });

  it('re-queries when the search term changes', () => {
    (mockOrderApi.getOrders as ReturnType<typeof vi.fn>).mockClear();
    component.onSearchChange('ORD-2026');
    expect(mockOrderApi.getOrders).toHaveBeenCalledWith(expect.objectContaining({ search: 'ORD-2026', page: 1 }));
  });

  it('re-queries when the date range changes', () => {
    (mockOrderApi.getOrders as ReturnType<typeof vi.fn>).mockClear();
    component.onRangeChange({ from: '2026-06-01', to: '2026-07-01' });
    expect(mockOrderApi.getOrders).toHaveBeenCalledWith(
      expect.objectContaining({ from: '2026-06-01', to: '2026-07-01', page: 1 })
    );
  });

  it('changes page on pageChange event', () => {
    (mockOrderApi.getOrders as ReturnType<typeof vi.fn>).mockClear();
    component.onPageChange(2);
    expect(mockOrderApi.getOrders).toHaveBeenCalledWith(expect.objectContaining({ page: 2 }));
  });

  it('navigates to order detail on row select', () => {
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.onRowSelect(0);
    expect(navSpy).toHaveBeenCalledWith(['/admin/orders', 'g1']);
  });
});
```

**Step 6.3 — GREEN: `order-list.component.ts`.**

```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { OrderDto, OrderFilter, OrderStatus } from '../../shared/models/order-admin.model';
import { DataTableComponent, DataTableColumn } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { DateRangePickerComponent, DateRange } from '../../shared/components/date-range-picker/date-range-picker.component';

@Component({
  selector: 'app-order-list',
  standalone: true,
  imports: [CommonModule, RouterModule, DataTableComponent, StatusBadgeComponent, DateRangePickerComponent],
  templateUrl: './order-list.component.html',
})
export class OrderListComponent implements OnInit {
  columns: DataTableColumn<OrderDto>[] = [
    { key: 'orderId', header: 'Order #' },
    { key: 'orderDate', header: 'Date', cellTemplate: 'date' },
    { key: 'status', header: 'Status' },
    { key: 'total', header: 'Total', cellTemplate: 'currency' },
  ];
  rows: OrderDto[] = [];
  totalCount = 0;
  pageNumber = 1;
  pageSize = 20;
  loading = false;
  statusFilter: OrderStatus | '' = '';
  search = '';
  range: DateRange = { from: '', to: '' };

  constructor(private orderApi: OrderAdminService, private router: Router) {}

  ngOnInit(): void {
    this.load();
  }

  onRowSelect(index: number): void {
    const row = this.rows[index];
    if (row) this.router.navigate(['/admin/orders', row.id]);
  }

  handleTableClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    const tr = target.closest('tbody tr');
    if (!tr) return;
    const index = Array.from(tr.parentElement?.children ?? []).indexOf(tr);
    this.onRowSelect(index);
  }

  onStatusFilterChange(status: string): void {
    this.statusFilter = status as OrderStatus | '';
    this.pageNumber = 1;
    this.load();
  }

  onSearchChange(term: string): void {
    this.search = term;
    this.pageNumber = 1;
    this.load();
  }

  onRangeChange(range: DateRange): void {
    this.range = range;
    this.pageNumber = 1;
    this.load();
  }

  onPageChange(page: number): void {
    this.pageNumber = page;
    this.load();
  }

  private load(): void {
    this.loading = true;
    const filter: OrderFilter = {
      page: this.pageNumber,
      pageSize: this.pageSize,
      ...(this.statusFilter ? { status: this.statusFilter } : {}),
      ...(this.search ? { search: this.search } : {}),
      ...(this.range.from ? { from: this.range.from } : {}),
      ...(this.range.to ? { to: this.range.to } : {}),
    };
    this.orderApi.getOrders(filter).subscribe((result) => {
      this.rows = result.items;
      this.totalCount = result.totalCount;
      this.loading = false;
    });
  }
}
```

`order-list.component.html`:

```html
<div class="d-flex justify-content-between align-items-center mb-3">
  <h1 class="h4 mb-0">Orders</h1>
</div>

<div class="row g-2 mb-3 align-items-end">
  <div class="col-auto">
    <label for="status-filter" class="form-label small mb-0">Status</label>
    <select id="status-filter" class="form-select form-select-sm" (change)="onStatusFilterChange($any($event.target).value)">
      <option value="">All</option>
      <option value="pending">Pending</option>
      <option value="confirmed">Confirmed</option>
      <option value="shipped">Shipped</option>
      <option value="delivered">Delivered</option>
      <option value="cancelled">Cancelled</option>
    </select>
  </div>
  <div class="col-auto">
    <label for="search" class="form-label small mb-0">Search</label>
    <input id="search" class="form-control form-control-sm" placeholder="Order number"
           (change)="onSearchChange($any($event.target).value)" />
  </div>
  <div class="col-auto">
    <app-date-range-picker [range]="range" (rangeChange)="onRangeChange($event)"></app-date-range-picker>
  </div>
</div>

<div (click)="handleTableClick($event)" style="cursor:pointer">
  <app-data-table
    [columns]="columns"
    [rows]="rows"
    [totalCount]="totalCount"
    [pageNumber]="pageNumber"
    [pageSize]="pageSize"
    [sortKey]="null"
    sortDirection="asc"
    [loading]="loading"
    emptyMessage="No orders found."
    (pageChange)="onPageChange($event)">
  </app-data-table>
</div>
```

**Step 6.4 — RED: `order-detail.component.spec.ts`.**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { OrderDetailComponent } from './order-detail.component';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { ToastService } from '../../shared/services/toast.service';
import { OrderDto } from '../../shared/models/order-admin.model';

describe('OrderDetailComponent', () => {
  let fixture: ComponentFixture<OrderDetailComponent>;
  let component: OrderDetailComponent;
  let mockOrderApi: Partial<OrderAdminService>;
  let mockToast: Partial<ToastService>;

  const baseOrder: OrderDto = {
    orderId: 'ORD-2026-000001', id: 'g1', customerId: 'c1', orderDate: '2026-07-01T00:00:00Z',
    status: 'pending', items: [], shippingAddress: {} as any, subtotal: 10, tax: 1, shippingCost: 0, total: 11,
  };

  function setup(order: OrderDto): void {
    mockOrderApi = {
      getOrder: vi.fn().mockReturnValue(of(order)),
      confirm: vi.fn().mockReturnValue(of({ ...order, status: 'confirmed' })),
      ship: vi.fn().mockReturnValue(of({ ...order, status: 'shipped', trackingNumber: 'T1' })),
      deliver: vi.fn().mockReturnValue(of({ ...order, status: 'delivered' })),
      cancel: vi.fn().mockReturnValue(of({ ...order, status: 'cancelled' })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [OrderDetailComponent],
      providers: [
        provideRouter([]),
        { provide: OrderAdminService, useValue: mockOrderApi },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'g1' } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(OrderDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => setup(baseOrder));

  it('loads the order on init', () => {
    expect(mockOrderApi.getOrder).toHaveBeenCalledWith('g1');
    expect(component.order?.orderId).toBe('ORD-2026-000001');
  });

  it('exposes confirm and cancel as available actions for a pending order', () => {
    expect(component.actions).toEqual(['confirm', 'cancel']);
  });

  it('confirms the order and shows a success toast', () => {
    component.onConfirm();
    expect(mockOrderApi.confirm).toHaveBeenCalledWith('g1');
    expect(component.order?.status).toBe('confirmed');
    expect(mockToast.success).toHaveBeenCalled();
  });

  it('opens the ship modal and ships with a tracking number', () => {
    component.openShipModal();
    expect(component.shipModalOpen).toBe(true);
    component.onShipConfirmed('TRACK-1');
    expect(mockOrderApi.ship).toHaveBeenCalledWith('g1', 'TRACK-1');
    expect(component.shipModalOpen).toBe(false);
  });

  it('marks delivered', () => {
    component.onDeliver();
    expect(mockOrderApi.deliver).toHaveBeenCalledWith('g1');
  });

  it('opens the cancel modal and cancels with a reason', () => {
    component.openCancelModal();
    component.onCancelConfirmed('Out of stock');
    expect(mockOrderApi.cancel).toHaveBeenCalledWith('g1', 'Out of stock');
    expect(component.cancelModalOpen).toBe(false);
  });

  it('shows an error toast when an action fails', () => {
    TestBed.resetTestingModule();
    mockOrderApi = {
      getOrder: vi.fn().mockReturnValue(of(baseOrder)),
      confirm: vi.fn().mockReturnValue(throwError(() => new Error('fail'))),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [OrderDetailComponent],
      providers: [
        provideRouter([]),
        { provide: OrderAdminService, useValue: mockOrderApi },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'g1' } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(OrderDetailComponent);
    fixture.detectChanges();
    fixture.componentInstance.onConfirm();
    expect(mockToast.error).toHaveBeenCalled();
  });
});
```

**Step 6.5 — GREEN: `order-detail.component.ts`.**

```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { OrderDto } from '../../shared/models/order-admin.model';
import { availableActions } from '../order-status.utils';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { ConfirmModalComponent } from '../../shared/components/confirm-modal/confirm-modal.component';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-order-detail',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent, ConfirmModalComponent],
  templateUrl: './order-detail.component.html',
})
export class OrderDetailComponent implements OnInit {
  order: OrderDto | null = null;
  actions: ReturnType<typeof availableActions> = [];
  shipModalOpen = false;
  cancelModalOpen = false;
  trackingNumberInput = '';
  cancelReasonInput = '';

  constructor(
    private route: ActivatedRoute,
    private orderApi: OrderAdminService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.orderApi.getOrder(id).subscribe((order) => this.applyOrder(order));
  }

  private applyOrder(order: OrderDto): void {
    this.order = order;
    this.actions = availableActions(order.status);
  }

  onConfirm(): void {
    if (!this.order) return;
    this.orderApi.confirm(this.order.id).subscribe({
      next: (order) => { this.applyOrder(order); this.toast.success('Order confirmed.'); },
      error: () => this.toast.error('Failed to confirm order.'),
    });
  }

  openShipModal(): void { this.shipModalOpen = true; }
  onShipCancelled(): void { this.shipModalOpen = false; }
  onShipConfirmed(trackingNumber: string): void {
    if (!this.order) return;
    this.orderApi.ship(this.order.id, trackingNumber || undefined).subscribe({
      next: (order) => { this.applyOrder(order); this.shipModalOpen = false; this.toast.success('Order shipped.'); },
      error: () => { this.shipModalOpen = false; this.toast.error('Failed to ship order.'); },
    });
  }

  onDeliver(): void {
    if (!this.order) return;
    this.orderApi.deliver(this.order.id).subscribe({
      next: (order) => { this.applyOrder(order); this.toast.success('Order marked delivered.'); },
      error: () => this.toast.error('Failed to update order.'),
    });
  }

  openCancelModal(): void { this.cancelModalOpen = true; }
  onCancelCancelled(): void { this.cancelModalOpen = false; }
  onCancelConfirmed(reason: string): void {
    if (!this.order) return;
    this.orderApi.cancel(this.order.id, reason).subscribe({
      next: (order) => { this.applyOrder(order); this.cancelModalOpen = false; this.toast.success('Order cancelled.'); },
      error: () => { this.cancelModalOpen = false; this.toast.error('Failed to cancel order.'); },
    });
  }
}
```

`order-detail.component.html` (ship/cancel modals use `ConfirmModalComponent` with a plain text input bound in the parent, since Task 3's modal has no built-in text-field-except-typed-confirmation; tracking number / reason are captured via simple inputs above the modal trigger to keep the modal's API untouched):

```html
<ng-container *ngIf="order as o">
  <div class="d-flex justify-content-between align-items-start mb-3">
    <div>
      <h1 class="h4 mb-0">{{ o.orderId }}</h1>
      <app-status-badge [status]="o.status"></app-status-badge>
    </div>
    <div class="d-flex gap-2">
      <button *ngIf="actions.includes('confirm')" class="btn btn-primary btn-sm" (click)="onConfirm()">Confirm</button>
      <ng-container *ngIf="actions.includes('ship')">
        <input class="form-control form-control-sm d-inline-block w-auto" placeholder="Tracking number (optional)"
               [(ngModel)]="trackingNumberInput" name="trackingNumberInput" />
        <button class="btn btn-primary btn-sm" (click)="openShipModal()">Ship</button>
      </ng-container>
      <button *ngIf="actions.includes('deliver')" class="btn btn-primary btn-sm" (click)="onDeliver()">Mark Delivered</button>
      <ng-container *ngIf="actions.includes('cancel')">
        <input class="form-control form-control-sm d-inline-block w-auto" placeholder="Cancellation reason"
               [(ngModel)]="cancelReasonInput" name="cancelReasonInput" />
        <button class="btn btn-outline-danger btn-sm" (click)="openCancelModal()">Cancel</button>
      </ng-container>
    </div>
  </div>

  <div class="row g-3">
    <div class="col-md-6">
      <div class="card"><div class="card-body">
        <h2 class="h6">Items</h2>
        <table class="table table-sm">
          <thead><tr><th>Product</th><th>Qty</th><th>Price</th></tr></thead>
          <tbody>
            <tr *ngFor="let item of o.items">
              <td>{{ item.productName }} <span *ngIf="item.variant">({{ item.variant.size }} {{ item.variant.color }})</span></td>
              <td>{{ item.quantity }}</td>
              <td>{{ item.price | number:'1.2-2' }}</td>
            </tr>
          </tbody>
        </table>
      </div></div>
    </div>
    <div class="col-md-6">
      <div class="card"><div class="card-body">
        <h2 class="h6">Shipping address</h2>
        <p class="mb-0">
          {{ o.shippingAddress.firstName }} {{ o.shippingAddress.lastName }}<br />
          {{ o.shippingAddress.street }}<br />
          {{ o.shippingAddress.city }}, {{ o.shippingAddress.state }} {{ o.shippingAddress.zipCode }}<br />
          {{ o.shippingAddress.country }}
        </p>
        <h2 class="h6 mt-3">Totals</h2>
        <p class="mb-0">
          Subtotal: {{ o.subtotal | number:'1.2-2' }}<br />
          Tax: {{ o.tax | number:'1.2-2' }}<br />
          Shipping: {{ o.shippingCost | number:'1.2-2' }}<br />
          <strong>Total: {{ o.total | number:'1.2-2' }}</strong>
        </p>
        <p *ngIf="o.trackingNumber" class="mt-2">Tracking: {{ o.trackingNumber }}</p>
      </div></div>
    </div>
  </div>

  <app-confirm-modal
    [isOpen]="shipModalOpen"
    title="Ship order"
    [message]="'Mark ' + o.orderId + ' as shipped?'"
    confirmLabel="Ship"
    (confirmed)="onShipConfirmed(trackingNumberInput)"
    (cancelled)="onShipCancelled()">
  </app-confirm-modal>

  <app-confirm-modal
    [isOpen]="cancelModalOpen"
    title="Cancel order"
    [message]="'Cancel ' + o.orderId + '? This cannot be undone.'"
    confirmLabel="Cancel order"
    tone="danger"
    (confirmed)="onCancelConfirmed(cancelReasonInput)"
    (cancelled)="onCancelCancelled()">
  </app-confirm-modal>
</ng-container>
```

Add `FormsModule` to the component's `imports` array (needed for `[(ngModel)]`).

**Step 6.6 — `orders.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const ordersRoutes: Routes = [
  { path: '', loadComponent: () => import('./order-list/order-list.component').then((m) => m.OrderListComponent) },
  { path: ':id', loadComponent: () => import('./order-detail/order-detail.component').then((m) => m.OrderDetailComponent) },
];
```

### Verification

```
npm run test:ci -- --run order-status.utils.spec order-list.component.spec order-detail.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 7 — Catalog module

### Files
- Create: `fashionsaas-storefront/src/app/admin/catalog/catalog.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/models/catalog-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/services/catalog-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/services/catalog-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-list/product-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-list/product-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-list/product-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-form/product-form.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-form/product-form.component.html`
- Create: `fashionsaas-storefront/src/app/admin/catalog/product-form/product-form.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/categories/category-tree.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/categories/category-tree.component.html`
- Create: `fashionsaas-storefront/src/app/admin/catalog/categories/category-tree.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/variants/variant-table.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/variants/variant-table.component.html`
- Create: `fashionsaas-storefront/src/app/admin/catalog/variants/variant-table.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/images/image-manager.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/catalog/images/image-manager.component.html`
- Create: `fashionsaas-storefront/src/app/admin/catalog/images/image-manager.component.spec.ts`

### Interfaces

**Consumes** (backend, `ApiUrl.cs`): `TenantCategories.{GetAll,GetTree,GetById,Create,Update,Move,Reorder,Delete}`, `TenantProducts.{GetAll,GetById,GetBySlug,Create,Update,Publish,Archive,Delete}`, `TenantProductVariants.{GetByProduct,Add,Update,Deactivate,Delete}`, `TenantProductImages.{GetByProduct,Upload,Reorder,SetPrimary,Delete}`. Task 3: `DataTableComponent`, `ConfirmModalComponent`, `ToastService`. Task 6 pattern: reactive forms + status-gated actions.

**Produces:**

```typescript
// catalog/models/catalog-admin.model.ts
export type ProductStatus = 'draft' | 'published' | 'archived';

export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  parentId: string | null;
  sortOrder: number;
  children?: CategoryDto[];
}

export interface ProductDto {
  id: string;
  name: string;
  slug: string;
  description: string;
  categoryId: string;
  status: ProductStatus;
  basePrice: number;
  createdAt: string;
}

export interface ProductVariantDto {
  id: string;
  productId: string;
  sku: string;
  size?: string;
  color?: string;
  price: number;
  stockQuantity: number;
  isActive: boolean;
}

export interface ProductImageDto {
  id: string;
  productId: string;
  url: string;
  sortOrder: number;
  isPrimary: boolean;
}

export interface CreateProductRequest {
  name: string;
  description: string;
  categoryId: string;
  basePrice: number;
}

export interface CreateCategoryRequest {
  name: string;
  parentId: string | null;
  sortOrder: number;
}

export interface CreateVariantRequest {
  productId: string;
  sku: string;
  size?: string;
  color?: string;
  price: number;
  stockQuantity: number;
}
```

`CatalogAdminService` public surface:

```typescript
getCategoryTree(): Observable<CategoryDto[]>;
getCategories(): Observable<CategoryDto[]>;
createCategory(req: CreateCategoryRequest): Observable<CategoryDto>;
updateCategory(id: string, req: CreateCategoryRequest): Observable<CategoryDto>;
moveCategory(id: string, newParentId: string | null): Observable<CategoryDto>;
reorderCategories(orderedIds: string[]): Observable<void>;
deleteCategory(id: string): Observable<void>;

getProducts(page: number, pageSize: number, search?: string): Observable<PagedResult<ProductDto>>;
getProduct(id: string): Observable<ProductDto>;
createProduct(req: CreateProductRequest): Observable<ProductDto>;
updateProduct(id: string, req: CreateProductRequest): Observable<ProductDto>;
publishProduct(id: string): Observable<ProductDto>;
archiveProduct(id: string): Observable<ProductDto>;
deleteProduct(id: string): Observable<void>;

getVariants(productId: string): Observable<ProductVariantDto[]>;
addVariant(req: CreateVariantRequest): Observable<ProductVariantDto>;
updateVariant(id: string, req: Partial<CreateVariantRequest>): Observable<ProductVariantDto>;
deactivateVariant(id: string): Observable<ProductVariantDto>;
deleteVariant(id: string): Observable<void>;

getImages(productId: string): Observable<ProductImageDto[]>;
uploadImage(productId: string, file: File): Observable<ProductImageDto>;
reorderImages(productId: string, orderedIds: string[]): Observable<void>;
setPrimaryImage(id: string): Observable<void>;
deleteImage(id: string): Observable<void>;
```

### TDD steps

**Step 7.1 — RED: `catalog-admin.service.spec.ts`** (one representative test per endpoint group; full coverage, terse bodies):

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { CatalogAdminService } from './catalog-admin.service';

describe('CatalogAdminService', () => {
  let service: CatalogAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [CatalogAdminService, ApiService] });
    service = TestBed.inject(CatalogAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets the category tree', () => {
    service.getCategoryTree().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/categories/tree`).flush(wrap([]));
  });

  it('creates a category', () => {
    service.createCategory({ name: 'Shoes', parentId: null, sortOrder: 0 }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/categories`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({ id: 'c1', name: 'Shoes', slug: 'shoes', parentId: null, sortOrder: 0 }));
  });

  it('moves a category', () => {
    service.moveCategory('c1', 'c2').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/categories/c1/move`);
    expect(req.request.body).toEqual({ newParentId: 'c2' });
    req.flush(wrap({}));
  });

  it('reorders categories', () => {
    service.reorderCategories(['c1', 'c2']).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/categories/reorder`);
    expect(req.request.body).toEqual({ orderedIds: ['c1', 'c2'] });
    req.flush(wrap(null));
  });

  it('deletes a category', () => {
    service.deleteCategory('c1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/categories/c1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets a paged product list with optional search', () => {
    service.getProducts(1, 20, 'jacket').subscribe();
    const req = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/tenant/products` && r.params.get('search') === 'jacket'
    );
    req.flush(wrap({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }));
  });

  it('creates a product', () => {
    service.createProduct({ name: 'Jacket', description: 'd', categoryId: 'c1', basePrice: 99 }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('publishes a product', () => {
    service.publishProduct('p1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1/publish`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap({}));
  });

  it('archives a product', () => {
    service.archiveProduct('p1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1/archive`);
    req.flush(wrap({}));
  });

  it('deletes a product', () => {
    service.deleteProduct('p1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets variants for a product', () => {
    service.getVariants('p1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1/variants`).flush(wrap([]));
  });

  it('adds a variant', () => {
    service.addVariant({ productId: 'p1', sku: 'SKU-1', price: 10, stockQuantity: 5 }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/variants`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('deactivates a variant', () => {
    service.deactivateVariant('v1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/variants/v1/deactivate`).flush(wrap({}));
  });

  it('deletes a variant', () => {
    service.deleteVariant('v1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/variants/v1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets images for a product', () => {
    service.getImages('p1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1/images`).flush(wrap([]));
  });

  it('uploads an image as multipart form data', () => {
    const file = new File(['x'], 'photo.png', { type: 'image/png' });
    service.uploadImage('p1', file).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/images`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush(wrap({}));
  });

  it('reorders images', () => {
    service.reorderImages('p1', ['i1', 'i2']).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/p1/images/reorder`);
    expect(req.request.body).toEqual({ orderedIds: ['i1', 'i2'] });
    req.flush(wrap(null));
  });

  it('sets an image as primary', () => {
    service.setPrimaryImage('i1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/images/i1/set-primary`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap(null));
  });

  it('deletes an image', () => {
    service.deleteImage('i1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/products/images/i1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });
});
```

**Step 7.2 — GREEN: `catalog-admin.service.ts`.**

```typescript
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse, PagedResult } from '../../../core/models/api-response.model';
import {
  CategoryDto, ProductDto, ProductVariantDto, ProductImageDto,
  CreateProductRequest, CreateCategoryRequest, CreateVariantRequest,
} from '../models/catalog-admin.model';

@Injectable({ providedIn: 'root' })
export class CatalogAdminService {
  constructor(private apiService: ApiService) {}

  private unwrap<T>(obs: Observable<ApiResponse<T>>): Observable<T> {
    return obs.pipe(map((r) => r.data));
  }

  getCategoryTree(): Observable<CategoryDto[]> {
    return this.unwrap(this.apiService.get<CategoryDto[]>('tenant/categories/tree'));
  }

  getCategories(): Observable<CategoryDto[]> {
    return this.unwrap(this.apiService.get<CategoryDto[]>('tenant/categories'));
  }

  createCategory(req: CreateCategoryRequest): Observable<CategoryDto> {
    return this.unwrap(this.apiService.post<CategoryDto>('tenant/categories', req));
  }

  updateCategory(id: string, req: CreateCategoryRequest): Observable<CategoryDto> {
    return this.unwrap(this.apiService.put<CategoryDto>(`tenant/categories/${id}`, req));
  }

  moveCategory(id: string, newParentId: string | null): Observable<CategoryDto> {
    return this.unwrap(this.apiService.put<CategoryDto>(`tenant/categories/${id}/move`, { newParentId }));
  }

  reorderCategories(orderedIds: string[]): Observable<void> {
    return this.unwrap(this.apiService.put<void>('tenant/categories/reorder', { orderedIds }));
  }

  deleteCategory(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`tenant/categories/${id}`));
  }

  getProducts(page: number, pageSize: number, search?: string): Observable<PagedResult<ProductDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (search) params = params.set('search', search);
    return this.unwrap(this.apiService.get<PagedResult<ProductDto>>('tenant/products', params));
  }

  getProduct(id: string): Observable<ProductDto> {
    return this.unwrap(this.apiService.get<ProductDto>(`tenant/products/${id}`));
  }

  createProduct(req: CreateProductRequest): Observable<ProductDto> {
    return this.unwrap(this.apiService.post<ProductDto>('tenant/products', req));
  }

  updateProduct(id: string, req: CreateProductRequest): Observable<ProductDto> {
    return this.unwrap(this.apiService.put<ProductDto>(`tenant/products/${id}`, req));
  }

  publishProduct(id: string): Observable<ProductDto> {
    return this.unwrap(this.apiService.put<ProductDto>(`tenant/products/${id}/publish`, {}));
  }

  archiveProduct(id: string): Observable<ProductDto> {
    return this.unwrap(this.apiService.put<ProductDto>(`tenant/products/${id}/archive`, {}));
  }

  deleteProduct(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`tenant/products/${id}`));
  }

  getVariants(productId: string): Observable<ProductVariantDto[]> {
    return this.unwrap(this.apiService.get<ProductVariantDto[]>(`tenant/products/${productId}/variants`));
  }

  addVariant(req: CreateVariantRequest): Observable<ProductVariantDto> {
    return this.unwrap(this.apiService.post<ProductVariantDto>('tenant/variants', req));
  }

  updateVariant(id: string, req: Partial<CreateVariantRequest>): Observable<ProductVariantDto> {
    return this.unwrap(this.apiService.put<ProductVariantDto>(`tenant/variants/${id}`, req));
  }

  deactivateVariant(id: string): Observable<ProductVariantDto> {
    return this.unwrap(this.apiService.put<ProductVariantDto>(`tenant/variants/${id}/deactivate`, {}));
  }

  deleteVariant(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`tenant/variants/${id}`));
  }

  getImages(productId: string): Observable<ProductImageDto[]> {
    return this.unwrap(this.apiService.get<ProductImageDto[]>(`tenant/products/${productId}/images`));
  }

  uploadImage(productId: string, file: File): Observable<ProductImageDto> {
    const formData = new FormData();
    formData.append('productId', productId);
    formData.append('file', file);
    return this.unwrap(this.apiService.post<ProductImageDto>('tenant/products/images', formData));
  }

  reorderImages(productId: string, orderedIds: string[]): Observable<void> {
    return this.unwrap(this.apiService.put<void>(`tenant/products/${productId}/images/reorder`, { orderedIds }));
  }

  setPrimaryImage(id: string): Observable<void> {
    return this.unwrap(this.apiService.put<void>(`tenant/products/images/${id}/set-primary`, {}));
  }

  deleteImage(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`tenant/products/images/${id}`));
  }
}
```

**Step 7.3 — RED/GREEN: `product-list.component`** (list + publish/archive/delete actions, mirrors Task 6's `order-list` pattern):

```typescript
// product-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { ProductListComponent } from './product-list.component';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ToastService } from '../../shared/services/toast.service';
import { ProductDto } from '../models/catalog-admin.model';

describe('ProductListComponent', () => {
  let fixture: ComponentFixture<ProductListComponent>;
  let component: ProductListComponent;
  let mockCatalog: Partial<CatalogAdminService>;
  let mockToast: Partial<ToastService>;

  const product: ProductDto = {
    id: 'p1', name: 'Jacket', slug: 'jacket', description: 'd', categoryId: 'c1',
    status: 'draft', basePrice: 99, createdAt: '2026-07-01T00:00:00Z',
  };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCatalog = {
      getProducts: vi.fn().mockReturnValue(of({ items: [product], totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1 })),
      publishProduct: vi.fn().mockReturnValue(of({ ...product, status: 'published' })),
      archiveProduct: vi.fn().mockReturnValue(of({ ...product, status: 'archived' })),
      deleteProduct: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ProductListComponent],
      providers: [provideRouter([]), { provide: CatalogAdminService, useValue: mockCatalog }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(ProductListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads products on init', () => {
    expect(component.rows.length).toBe(1);
  });

  it('publishes a product and reloads', () => {
    component.onPublish(product);
    expect(mockCatalog.publishProduct).toHaveBeenCalledWith('p1');
    expect(mockToast.success).toHaveBeenCalled();
  });

  it('archives a product', () => {
    component.onArchive(product);
    expect(mockCatalog.archiveProduct).toHaveBeenCalledWith('p1');
  });

  it('opens the delete confirmation and deletes on confirm', () => {
    component.openDeleteModal(product);
    expect(component.deleteModalOpen).toBe(true);
    expect(component.productPendingDelete).toBe(product);
    component.onDeleteConfirmed();
    expect(mockCatalog.deleteProduct).toHaveBeenCalledWith('p1');
    expect(component.deleteModalOpen).toBe(false);
  });

  it('searches products', () => {
    (mockCatalog.getProducts as ReturnType<typeof vi.fn>).mockClear();
    component.onSearchChange('jacket');
    expect(mockCatalog.getProducts).toHaveBeenCalledWith(1, 20, 'jacket');
  });
});
```

```typescript
// product-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ProductDto } from '../models/catalog-admin.model';
import { DataTableComponent, DataTableColumn } from '../../shared/components/data-table/data-table.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { ConfirmModalComponent } from '../../shared/components/confirm-modal/confirm-modal.component';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [CommonModule, RouterModule, DataTableComponent, StatusBadgeComponent, ConfirmModalComponent],
  templateUrl: './product-list.component.html',
})
export class ProductListComponent implements OnInit {
  columns: DataTableColumn<ProductDto>[] = [
    { key: 'name', header: 'Name', sortable: true },
    { key: 'status', header: 'Status' },
    { key: 'basePrice', header: 'Price', cellTemplate: 'currency' },
    { key: 'createdAt', header: 'Created', cellTemplate: 'date' },
  ];
  rows: ProductDto[] = [];
  totalCount = 0;
  pageNumber = 1;
  pageSize = 20;
  loading = false;
  search = '';
  deleteModalOpen = false;
  productPendingDelete: ProductDto | null = null;

  constructor(private catalog: CatalogAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  onSearchChange(term: string): void {
    this.search = term;
    this.pageNumber = 1;
    this.load();
  }

  onPageChange(page: number): void {
    this.pageNumber = page;
    this.load();
  }

  onPublish(product: ProductDto): void {
    this.catalog.publishProduct(product.id).subscribe({
      next: () => { this.toast.success('Product published.'); this.load(); },
      error: () => this.toast.error('Failed to publish product.'),
    });
  }

  onArchive(product: ProductDto): void {
    this.catalog.archiveProduct(product.id).subscribe({
      next: () => { this.toast.success('Product archived.'); this.load(); },
      error: () => this.toast.error('Failed to archive product.'),
    });
  }

  openDeleteModal(product: ProductDto): void {
    this.productPendingDelete = product;
    this.deleteModalOpen = true;
  }

  onDeleteCancelled(): void {
    this.deleteModalOpen = false;
    this.productPendingDelete = null;
  }

  onDeleteConfirmed(): void {
    if (!this.productPendingDelete) return;
    this.catalog.deleteProduct(this.productPendingDelete.id).subscribe({
      next: () => { this.toast.success('Product deleted.'); this.deleteModalOpen = false; this.load(); },
      error: () => { this.toast.error('Failed to delete product.'); this.deleteModalOpen = false; },
    });
  }

  private load(): void {
    this.loading = true;
    this.catalog.getProducts(this.pageNumber, this.pageSize, this.search || undefined).subscribe((result) => {
      this.rows = result.items;
      this.totalCount = result.totalCount;
      this.loading = false;
    });
  }
}
```

`product-list.component.html`:

```html
<div class="d-flex justify-content-between align-items-center mb-3">
  <h1 class="h4 mb-0">Products</h1>
  <a routerLink="new" class="btn btn-primary btn-sm"><i class="bi bi-plus-lg"></i> New product</a>
</div>

<input class="form-control form-control-sm mb-3 w-auto" placeholder="Search products"
       (change)="onSearchChange($any($event.target).value)" />

<app-data-table [columns]="columns" [rows]="rows" [totalCount]="totalCount" [pageNumber]="pageNumber"
  [pageSize]="pageSize" [sortKey]="null" sortDirection="asc" [loading]="loading"
  emptyMessage="No products found." (pageChange)="onPageChange($event)">
</app-data-table>

<table class="table table-sm mt-2">
  <tbody>
    <tr *ngFor="let p of rows">
      <td>{{ p.name }}</td>
      <td>
        <a [routerLink]="[p.id]" class="btn btn-sm btn-outline-secondary">Edit</a>
        <button class="btn btn-sm btn-outline-success" *ngIf="p.status !== 'published'" (click)="onPublish(p)">Publish</button>
        <button class="btn btn-sm btn-outline-warning" *ngIf="p.status !== 'archived'" (click)="onArchive(p)">Archive</button>
        <button class="btn btn-sm btn-outline-danger" (click)="openDeleteModal(p)">Delete</button>
      </td>
    </tr>
  </tbody>
</table>

<app-confirm-modal
  [isOpen]="deleteModalOpen"
  title="Delete product"
  [message]="'Delete ' + (productPendingDelete?.name ?? '') + '? This cannot be undone.'"
  confirmLabel="Delete"
  tone="danger"
  (confirmed)="onDeleteConfirmed()"
  (cancelled)="onDeleteCancelled()">
</app-confirm-modal>
```

**Step 7.4 — RED/GREEN: `product-form.component`** (reactive form for create/edit, shared route):

```typescript
// product-form.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { ProductFormComponent } from './product-form.component';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('ProductFormComponent', () => {
  let fixture: ComponentFixture<ProductFormComponent>;
  let component: ProductFormComponent;
  let mockCatalog: Partial<CatalogAdminService>;
  let mockToast: Partial<ToastService>;

  function setup(paramId: string | null): void {
    mockCatalog = {
      getCategories: vi.fn().mockReturnValue(of([{ id: 'c1', name: 'Shoes', slug: 'shoes', parentId: null, sortOrder: 0 }])),
      getProduct: vi.fn().mockReturnValue(of({
        id: 'p1', name: 'Jacket', slug: 'jacket', description: 'd', categoryId: 'c1', status: 'draft', basePrice: 99, createdAt: '',
      })),
      createProduct: vi.fn().mockReturnValue(of({ id: 'p2' })),
      updateProduct: vi.fn().mockReturnValue(of({ id: 'p1' })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ProductFormComponent],
      providers: [
        provideRouter([]),
        { provide: CatalogAdminService, useValue: mockCatalog },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => paramId } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ProductFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('starts in create mode with an empty form when there is no id param', () => {
    setup(null);
    expect(component.isEditMode).toBe(false);
    expect(component.form.value.name).toBe('');
  });

  it('loads the product and patches the form in edit mode', () => {
    setup('p1');
    expect(component.isEditMode).toBe(true);
    expect(mockCatalog.getProduct).toHaveBeenCalledWith('p1');
    expect(component.form.value.name).toBe('Jacket');
  });

  it('does not submit an invalid form', () => {
    setup(null);
    component.form.patchValue({ name: '' });
    component.onSubmit();
    expect(mockCatalog.createProduct).not.toHaveBeenCalled();
  });

  it('creates a product and navigates back to the list on success', () => {
    setup(null);
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.form.setValue({ name: 'New', description: 'd', categoryId: 'c1', basePrice: 50 });
    component.onSubmit();
    expect(mockCatalog.createProduct).toHaveBeenCalledWith({ name: 'New', description: 'd', categoryId: 'c1', basePrice: 50 });
    expect(navSpy).toHaveBeenCalledWith(['/admin/catalog']);
  });

  it('updates a product in edit mode', () => {
    setup('p1');
    component.form.patchValue({ name: 'Updated' });
    component.onSubmit();
    expect(mockCatalog.updateProduct).toHaveBeenCalledWith('p1', expect.objectContaining({ name: 'Updated' }));
  });

  it('shows a toast error when the save fails', () => {
    setup(null);
    (mockCatalog.createProduct as ReturnType<typeof vi.fn>).mockReturnValue(throwError(() => new Error('fail')));
    component.form.setValue({ name: 'New', description: 'd', categoryId: 'c1', basePrice: 50 });
    component.onSubmit();
    expect(mockToast.error).toHaveBeenCalled();
  });
});
```

```typescript
// product-form.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { CategoryDto } from '../models/catalog-admin.model';
import { ToastService } from '../../shared/services/toast.service';
import { VariantTableComponent } from '../variants/variant-table.component';
import { ImageManagerComponent } from '../images/image-manager.component';

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, VariantTableComponent, ImageManagerComponent],
  templateUrl: './product-form.component.html',
})
export class ProductFormComponent implements OnInit {
  isEditMode = false;
  productId: string | null = null;
  categories: CategoryDto[] = [];

  form = this.fb.group({
    name: this.fb.nonNullable.control('', Validators.required),
    description: this.fb.nonNullable.control('', Validators.required),
    categoryId: this.fb.nonNullable.control('', Validators.required),
    basePrice: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0.01)]),
  });

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private catalog: CatalogAdminService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    this.catalog.getCategories().subscribe((categories) => (this.categories = categories));
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isEditMode = true;
      this.productId = id;
      this.catalog.getProduct(id).subscribe((product) => {
        this.form.setValue({
          name: product.name,
          description: product.description,
          categoryId: product.categoryId,
          basePrice: product.basePrice,
        });
      });
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.getRawValue();
    const save = this.isEditMode && this.productId
      ? this.catalog.updateProduct(this.productId, payload)
      : this.catalog.createProduct(payload);

    save.subscribe({
      next: () => {
        this.toast.success(this.isEditMode ? 'Product updated.' : 'Product created.');
        this.router.navigate(['/admin/catalog']);
      },
      error: () => this.toast.error('Failed to save product.'),
    });
  }
}
```

`product-form.component.html`:

```html
<h1 class="h4 mb-3">{{ isEditMode ? 'Edit product' : 'New product' }}</h1>

<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div class="mb-3">
    <label for="name" class="form-label">Name</label>
    <input id="name" class="form-control" formControlName="name" />
  </div>
  <div class="mb-3">
    <label for="description" class="form-label">Description</label>
    <textarea id="description" class="form-control" formControlName="description"></textarea>
  </div>
  <div class="mb-3">
    <label for="categoryId" class="form-label">Category</label>
    <select id="categoryId" class="form-select" formControlName="categoryId">
      <option value="" disabled>Select a category</option>
      <option *ngFor="let c of categories" [value]="c.id">{{ c.name }}</option>
    </select>
  </div>
  <div class="mb-3">
    <label for="basePrice" class="form-label">Base price</label>
    <input id="basePrice" type="number" step="0.01" class="form-control" formControlName="basePrice" />
  </div>
  <button type="submit" class="btn btn-primary">Save</button>
  <a routerLink="/admin/catalog" class="btn btn-link">Cancel</a>
</form>

<ng-container *ngIf="isEditMode && productId">
  <hr />
  <app-variant-table [productId]="productId"></app-variant-table>
  <hr />
  <app-image-manager [productId]="productId"></app-image-manager>
</ng-container>
```

**Step 7.5 — RED/GREEN: `category-tree.component`** (tree view with move/reorder):

```typescript
// category-tree.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CategoryTreeComponent } from './category-tree.component';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('CategoryTreeComponent', () => {
  let fixture: ComponentFixture<CategoryTreeComponent>;
  let component: CategoryTreeComponent;
  let mockCatalog: Partial<CatalogAdminService>;
  let mockToast: Partial<ToastService>;

  const tree = [
    { id: 'c1', name: 'Shoes', slug: 'shoes', parentId: null, sortOrder: 0, children: [
      { id: 'c2', name: 'Sneakers', slug: 'sneakers', parentId: 'c1', sortOrder: 0, children: [] },
    ]},
  ];

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCatalog = {
      getCategoryTree: vi.fn().mockReturnValue(of(tree)),
      moveCategory: vi.fn().mockReturnValue(of(tree[0])),
      reorderCategories: vi.fn().mockReturnValue(of(undefined)),
      deleteCategory: vi.fn().mockReturnValue(of(undefined)),
      createCategory: vi.fn().mockReturnValue(of(tree[0])),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [CategoryTreeComponent],
      providers: [{ provide: CatalogAdminService, useValue: mockCatalog }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(CategoryTreeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the category tree on init', () => {
    expect(component.tree.length).toBe(1);
    expect(component.tree[0].children?.length).toBe(1);
  });

  it('moves a category to a new parent', () => {
    component.onMove('c2', 'c1');
    expect(mockCatalog.moveCategory).toHaveBeenCalledWith('c2', 'c1');
  });

  it('reorders siblings', () => {
    component.onReorder(['c2', 'c1']);
    expect(mockCatalog.reorderCategories).toHaveBeenCalledWith(['c2', 'c1']);
  });

  it('deletes a category and reloads', () => {
    component.onDelete('c2');
    expect(mockCatalog.deleteCategory).toHaveBeenCalledWith('c2');
  });

  it('creates a new root category', () => {
    component.newCategoryName = 'Accessories';
    component.onCreateRoot();
    expect(mockCatalog.createCategory).toHaveBeenCalledWith({ name: 'Accessories', parentId: null, sortOrder: 0 });
  });
});
```

```typescript
// category-tree.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { CategoryDto } from '../models/catalog-admin.model';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-category-tree',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './category-tree.component.html',
})
export class CategoryTreeComponent implements OnInit {
  tree: CategoryDto[] = [];
  newCategoryName = '';

  constructor(private catalog: CatalogAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.catalog.getCategoryTree().subscribe((tree) => (this.tree = tree));
  }

  onMove(id: string, newParentId: string | null): void {
    this.catalog.moveCategory(id, newParentId).subscribe({
      next: () => { this.toast.success('Category moved.'); this.load(); },
      error: () => this.toast.error('Failed to move category.'),
    });
  }

  onReorder(orderedIds: string[]): void {
    this.catalog.reorderCategories(orderedIds).subscribe({
      next: () => { this.toast.success('Order updated.'); this.load(); },
      error: () => this.toast.error('Failed to reorder categories.'),
    });
  }

  onDelete(id: string): void {
    this.catalog.deleteCategory(id).subscribe({
      next: () => { this.toast.success('Category deleted.'); this.load(); },
      error: () => this.toast.error('Failed to delete category.'),
    });
  }

  onCreateRoot(): void {
    if (!this.newCategoryName.trim()) return;
    this.catalog.createCategory({ name: this.newCategoryName, parentId: null, sortOrder: this.tree.length }).subscribe({
      next: () => { this.newCategoryName = ''; this.toast.success('Category created.'); this.load(); },
      error: () => this.toast.error('Failed to create category.'),
    });
  }
}
```

`category-tree.component.html` (indentation-based tree, drag-free move via a parent `<select>` per node to keep interaction accessible and testable):

```html
<div class="d-flex gap-2 mb-3">
  <input class="form-control form-control-sm w-auto" placeholder="New root category" [(ngModel)]="newCategoryName" name="newCategoryName" />
  <button class="btn btn-sm btn-primary" (click)="onCreateRoot()">Add</button>
</div>

<ng-template #node let-category let-depth="depth">
  <div class="d-flex align-items-center gap-2 py-1" [style.paddingLeft.px]="depth * 20">
    <i class="bi bi-folder"></i>
    <span>{{ category.name }}</span>
    <button class="btn btn-sm btn-outline-danger ms-auto" [attr.aria-label]="'Delete ' + category.name" (click)="onDelete(category.id)">
      <i class="bi bi-trash"></i>
    </button>
  </div>
  <ng-container *ngFor="let child of category.children">
    <ng-container *ngTemplateOutlet="node; context: { $implicit: child, depth: depth + 1 }"></ng-container>
  </ng-container>
</ng-template>

<div *ngFor="let root of tree">
  <ng-container *ngTemplateOutlet="node; context: { $implicit: root, depth: 0 }"></ng-container>
</div>
```

**Step 7.6 — RED/GREEN: `variant-table.component`** (CRUD table per product):

```typescript
// variant-table.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { VariantTableComponent } from './variant-table.component';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('VariantTableComponent', () => {
  let fixture: ComponentFixture<VariantTableComponent>;
  let component: VariantTableComponent;
  let mockCatalog: Partial<CatalogAdminService>;
  let mockToast: Partial<ToastService>;

  const variant = { id: 'v1', productId: 'p1', sku: 'SKU-1', size: 'M', color: 'Red', price: 20, stockQuantity: 5, isActive: true };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCatalog = {
      getVariants: vi.fn().mockReturnValue(of([variant])),
      addVariant: vi.fn().mockReturnValue(of(variant)),
      updateVariant: vi.fn().mockReturnValue(of(variant)),
      deactivateVariant: vi.fn().mockReturnValue(of({ ...variant, isActive: false })),
      deleteVariant: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [VariantTableComponent],
      providers: [{ provide: CatalogAdminService, useValue: mockCatalog }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(VariantTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('productId', 'p1');
    fixture.detectChanges();
  });

  it('loads variants for the given product', () => {
    expect(mockCatalog.getVariants).toHaveBeenCalledWith('p1');
    expect(component.variants.length).toBe(1);
  });

  it('adds a new variant from the form', () => {
    component.newVariant = { sku: 'SKU-2', size: 'L', color: 'Blue', price: 25, stockQuantity: 10 };
    component.onAdd();
    expect(mockCatalog.addVariant).toHaveBeenCalledWith({ productId: 'p1', sku: 'SKU-2', size: 'L', color: 'Blue', price: 25, stockQuantity: 10 });
  });

  it('deactivates a variant', () => {
    component.onDeactivate(variant as any);
    expect(mockCatalog.deactivateVariant).toHaveBeenCalledWith('v1');
  });

  it('deletes a variant', () => {
    component.onDelete(variant as any);
    expect(mockCatalog.deleteVariant).toHaveBeenCalledWith('v1');
  });
});
```

```typescript
// variant-table.component.ts
import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ProductVariantDto, CreateVariantRequest } from '../models/catalog-admin.model';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-variant-table',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './variant-table.component.html',
})
export class VariantTableComponent implements OnChanges {
  @Input({ required: true }) productId!: string;
  variants: ProductVariantDto[] = [];
  newVariant: Omit<CreateVariantRequest, 'productId'> = { sku: '', size: '', color: '', price: 0, stockQuantity: 0 };

  constructor(private catalog: CatalogAdminService, private toast: ToastService) {}

  ngOnChanges(): void {
    if (this.productId) this.load();
  }

  private load(): void {
    this.catalog.getVariants(this.productId).subscribe((variants) => (this.variants = variants));
  }

  onAdd(): void {
    this.catalog.addVariant({ productId: this.productId, ...this.newVariant }).subscribe({
      next: () => {
        this.toast.success('Variant added.');
        this.newVariant = { sku: '', size: '', color: '', price: 0, stockQuantity: 0 };
        this.load();
      },
      error: () => this.toast.error('Failed to add variant.'),
    });
  }

  onDeactivate(variant: ProductVariantDto): void {
    this.catalog.deactivateVariant(variant.id).subscribe({
      next: () => { this.toast.success('Variant deactivated.'); this.load(); },
      error: () => this.toast.error('Failed to deactivate variant.'),
    });
  }

  onDelete(variant: ProductVariantDto): void {
    this.catalog.deleteVariant(variant.id).subscribe({
      next: () => { this.toast.success('Variant deleted.'); this.load(); },
      error: () => this.toast.error('Failed to delete variant.'),
    });
  }
}
```

`variant-table.component.html`:

```html
<h2 class="h6">Variants</h2>
<table class="table table-sm">
  <thead><tr><th>SKU</th><th>Size</th><th>Color</th><th>Price</th><th>Stock</th><th>Status</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let v of variants">
      <td>{{ v.sku }}</td><td>{{ v.size }}</td><td>{{ v.color }}</td>
      <td>{{ v.price | number:'1.2-2' }}</td><td>{{ v.stockQuantity }}</td>
      <td>{{ v.isActive ? 'Active' : 'Inactive' }}</td>
      <td>
        <button class="btn btn-sm btn-outline-warning" *ngIf="v.isActive" (click)="onDeactivate(v)">Deactivate</button>
        <button class="btn btn-sm btn-outline-danger" (click)="onDelete(v)">Delete</button>
      </td>
    </tr>
  </tbody>
</table>

<div class="row g-2 align-items-end">
  <div class="col-auto"><label class="form-label small mb-0">SKU</label><input class="form-control form-control-sm" [(ngModel)]="newVariant.sku" name="sku" /></div>
  <div class="col-auto"><label class="form-label small mb-0">Size</label><input class="form-control form-control-sm" [(ngModel)]="newVariant.size" name="size" /></div>
  <div class="col-auto"><label class="form-label small mb-0">Color</label><input class="form-control form-control-sm" [(ngModel)]="newVariant.color" name="color" /></div>
  <div class="col-auto"><label class="form-label small mb-0">Price</label><input type="number" step="0.01" class="form-control form-control-sm" [(ngModel)]="newVariant.price" name="price" /></div>
  <div class="col-auto"><label class="form-label small mb-0">Stock</label><input type="number" class="form-control form-control-sm" [(ngModel)]="newVariant.stockQuantity" name="stockQuantity" /></div>
  <div class="col-auto"><button class="btn btn-sm btn-primary" (click)="onAdd()">Add variant</button></div>
</div>
```

**Step 7.7 — RED/GREEN: `image-manager.component`** (upload multipart, reorder, set primary):

```typescript
// image-manager.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ImageManagerComponent } from './image-manager.component';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('ImageManagerComponent', () => {
  let fixture: ComponentFixture<ImageManagerComponent>;
  let component: ImageManagerComponent;
  let mockCatalog: Partial<CatalogAdminService>;
  let mockToast: Partial<ToastService>;

  const image = { id: 'i1', productId: 'p1', url: 'https://x/img.png', sortOrder: 0, isPrimary: true };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCatalog = {
      getImages: vi.fn().mockReturnValue(of([image])),
      uploadImage: vi.fn().mockReturnValue(of(image)),
      reorderImages: vi.fn().mockReturnValue(of(undefined)),
      setPrimaryImage: vi.fn().mockReturnValue(of(undefined)),
      deleteImage: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ImageManagerComponent],
      providers: [{ provide: CatalogAdminService, useValue: mockCatalog }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(ImageManagerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('productId', 'p1');
    fixture.detectChanges();
  });

  it('loads images for the product', () => {
    expect(mockCatalog.getImages).toHaveBeenCalledWith('p1');
    expect(component.images.length).toBe(1);
  });

  it('uploads a selected file', () => {
    const file = new File(['x'], 'a.png', { type: 'image/png' });
    component.onFileSelected({ target: { files: [file] } } as unknown as Event);
    expect(mockCatalog.uploadImage).toHaveBeenCalledWith('p1', file);
  });

  it('sets an image as primary', () => {
    component.onSetPrimary(image as any);
    expect(mockCatalog.setPrimaryImage).toHaveBeenCalledWith('i1');
  });

  it('reorders images by moving one up', () => {
    component.images = [image as any, { ...image, id: 'i2', sortOrder: 1 } as any];
    component.onMoveUp(1);
    expect(mockCatalog.reorderImages).toHaveBeenCalledWith('p1', ['i2', 'i1']);
  });

  it('deletes an image', () => {
    component.onDelete(image as any);
    expect(mockCatalog.deleteImage).toHaveBeenCalledWith('i1');
  });
});
```

```typescript
// image-manager.component.ts
import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CatalogAdminService } from '../services/catalog-admin.service';
import { ProductImageDto } from '../models/catalog-admin.model';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-image-manager',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './image-manager.component.html',
})
export class ImageManagerComponent implements OnChanges {
  @Input({ required: true }) productId!: string;
  images: ProductImageDto[] = [];

  constructor(private catalog: CatalogAdminService, private toast: ToastService) {}

  ngOnChanges(): void {
    if (this.productId) this.load();
  }

  private load(): void {
    this.catalog.getImages(this.productId).subscribe((images) => (this.images = images));
  }

  onFileSelected(event: Event): void {
    const files = (event.target as HTMLInputElement).files;
    if (!files || files.length === 0) return;
    this.catalog.uploadImage(this.productId, files[0]).subscribe({
      next: () => { this.toast.success('Image uploaded.'); this.load(); },
      error: () => this.toast.error('Failed to upload image.'),
    });
  }

  onSetPrimary(image: ProductImageDto): void {
    this.catalog.setPrimaryImage(image.id).subscribe({
      next: () => { this.toast.success('Primary image updated.'); this.load(); },
      error: () => this.toast.error('Failed to set primary image.'),
    });
  }

  onMoveUp(index: number): void {
    if (index <= 0) return;
    const reordered = [...this.images];
    [reordered[index - 1], reordered[index]] = [reordered[index], reordered[index - 1]];
    this.catalog.reorderImages(this.productId, reordered.map((i) => i.id)).subscribe({
      next: () => { this.images = reordered; this.load(); },
      error: () => this.toast.error('Failed to reorder images.'),
    });
  }

  onDelete(image: ProductImageDto): void {
    this.catalog.deleteImage(image.id).subscribe({
      next: () => { this.toast.success('Image deleted.'); this.load(); },
      error: () => this.toast.error('Failed to delete image.'),
    });
  }
}
```

`image-manager.component.html`:

```html
<h2 class="h6">Images</h2>
<div class="d-flex flex-wrap gap-2 mb-2">
  <div *ngFor="let img of images; let i = index" class="border rounded p-1 text-center" style="width:120px">
    <img [src]="img.url" [alt]="'Product image ' + (i + 1)" class="img-fluid" style="max-height:80px" />
    <div class="small" *ngIf="img.isPrimary">Primary</div>
    <div class="d-flex justify-content-center gap-1 mt-1">
      <button class="btn btn-sm btn-outline-secondary" [disabled]="i === 0" (click)="onMoveUp(i)" aria-label="Move up"><i class="bi bi-arrow-up"></i></button>
      <button class="btn btn-sm btn-outline-primary" *ngIf="!img.isPrimary" (click)="onSetPrimary(img)">Set primary</button>
      <button class="btn btn-sm btn-outline-danger" (click)="onDelete(img)" aria-label="Delete image"><i class="bi bi-trash"></i></button>
    </div>
  </div>
</div>
<label for="image-upload" class="form-label small">Upload image</label>
<input id="image-upload" type="file" accept="image/*" class="form-control form-control-sm w-auto" (change)="onFileSelected($event)" />
```

**Step 7.8 — `catalog.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const catalogRoutes: Routes = [
  { path: '', loadComponent: () => import('./product-list/product-list.component').then((m) => m.ProductListComponent) },
  { path: 'categories', loadComponent: () => import('./categories/category-tree.component').then((m) => m.CategoryTreeComponent) },
  { path: 'new', loadComponent: () => import('./product-form/product-form.component').then((m) => m.ProductFormComponent) },
  { path: ':id', loadComponent: () => import('./product-form/product-form.component').then((m) => m.ProductFormComponent) },
];
```

Add a "Categories" link to `product-list.component.html`'s header row: `<a routerLink="categories" class="btn btn-outline-secondary btn-sm">Categories</a>`.

### Verification

```
npm run test:ci -- --run catalog-admin.service.spec product-list.component.spec product-form.component.spec category-tree.component.spec variant-table.component.spec image-manager.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 8 — Inventory + customers

### Files
- Create: `fashionsaas-storefront/src/app/admin/inventory/inventory.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/models/inventory-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/services/inventory-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/services/inventory-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/stock-adjust/stock-adjust.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/stock-adjust/stock-adjust.component.html`
- Create: `fashionsaas-storefront/src/app/admin/inventory/stock-adjust/stock-adjust.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/low-stock/low-stock.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/inventory/low-stock/low-stock.component.html`
- Create: `fashionsaas-storefront/src/app/admin/inventory/low-stock/low-stock.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/customers.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/models/customer-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/services/customer-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/services/customer-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-list/customer-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-list/customer-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-list/customer-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-detail/customer-detail.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-detail/customer-detail.component.html`
- Create: `fashionsaas-storefront/src/app/admin/customers/customer-detail/customer-detail.component.spec.ts`

### Interfaces

**Consumes** (backend): `TenantInventory.{AdjustStock,GetLowStock,GetStockHistory}`; `TenantCustomers.{GetAll,GetById,Update,Deactivate}`; `TenantWishlists.GetByCustomer`. Task 4: `OrderAdminService.getOrders` (filtered via `OrderFilter.customerId`), `OrderDto`. Task 3 kit: `DataTableComponent`, `ToastService`, `ConfirmModalComponent`.

**Produces:**

```typescript
// inventory/models/inventory-admin.model.ts
export interface StockAdjustRequest {
  variantId: string;
  delta: number;      // positive or negative
  reason: string;
}

export interface LowStockItem {
  variantId: string;
  productName: string;
  sku: string;
  stockQuantity: number;
}

export interface StockHistoryEntry {
  id: string;
  variantId: string;
  delta: number;
  reason: string;
  createdAt: string;
  resultingQuantity: number;
}
```

```typescript
// customers/models/customer-admin.model.ts
export interface CustomerDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  isActive: boolean;
  createdAt: string;
}

export interface WishlistItemDto {
  id: string;
  productId: string;
  productName: string;
  addedAt: string;
}
```

`InventoryAdminService`:

```typescript
adjustStock(req: StockAdjustRequest): Observable<void>;
getLowStock(threshold?: number): Observable<LowStockItem[]>;
getStockHistory(variantId: string): Observable<StockHistoryEntry[]>;
```

`CustomerAdminService`:

```typescript
getCustomers(page: number, pageSize: number, search?: string): Observable<PagedResult<CustomerDto>>;
getCustomer(id: string): Observable<CustomerDto>;
deactivateCustomer(id: string): Observable<CustomerDto>;
getWishlist(customerId: string): Observable<WishlistItemDto[]>;
```

### TDD steps

**Step 8.1 — RED/GREEN: `inventory-admin.service.spec.ts` / `.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { InventoryAdminService } from './inventory-admin.service';

describe('InventoryAdminService', () => {
  let service: InventoryAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [InventoryAdminService, ApiService] });
    service = TestBed.inject(InventoryAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('adjusts stock with a delta and reason', () => {
    service.adjustStock({ variantId: 'v1', delta: -2, reason: 'Damaged' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/inventory/adjust`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ variantId: 'v1', delta: -2, reason: 'Damaged' });
    req.flush(wrap(null));
  });

  it('gets low-stock items with an optional threshold', () => {
    service.getLowStock(3).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/tenant/inventory/low-stock` && r.params.get('threshold') === '3');
    req.flush(wrap([]));
  });

  it('gets low-stock items without a threshold param when omitted', () => {
    service.getLowStock().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/inventory/low-stock`);
    expect(req.request.params.has('threshold')).toBe(false);
    req.flush(wrap([]));
  });

  it('gets stock history for a variant', () => {
    service.getStockHistory('v1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/inventory/variants/v1/history`).flush(wrap([]));
  });
});
```

```typescript
// inventory-admin.service.ts
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse } from '../../../core/models/api-response.model';
import { StockAdjustRequest, LowStockItem, StockHistoryEntry } from '../models/inventory-admin.model';

@Injectable({ providedIn: 'root' })
export class InventoryAdminService {
  constructor(private apiService: ApiService) {}

  adjustStock(req: StockAdjustRequest): Observable<void> {
    return this.apiService.post<void>('tenant/inventory/adjust', req).pipe(map((r: ApiResponse<void>) => r.data));
  }

  getLowStock(threshold?: number): Observable<LowStockItem[]> {
    const params = threshold !== undefined ? new HttpParams().set('threshold', String(threshold)) : undefined;
    return this.apiService
      .get<LowStockItem[]>('tenant/inventory/low-stock', params)
      .pipe(map((r: ApiResponse<LowStockItem[]>) => r.data));
  }

  getStockHistory(variantId: string): Observable<StockHistoryEntry[]> {
    return this.apiService
      .get<StockHistoryEntry[]>(`tenant/inventory/variants/${variantId}/history`)
      .pipe(map((r: ApiResponse<StockHistoryEntry[]>) => r.data));
  }
}
```

**Step 8.2 — RED/GREEN: `stock-adjust.component`.**

```typescript
// stock-adjust.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { StockAdjustComponent } from './stock-adjust.component';
import { InventoryAdminService } from '../services/inventory-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('StockAdjustComponent', () => {
  let fixture: ComponentFixture<StockAdjustComponent>;
  let component: StockAdjustComponent;
  let mockInventory: Partial<InventoryAdminService>;
  let mockToast: Partial<ToastService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockInventory = {
      adjustStock: vi.fn().mockReturnValue(of(undefined)),
      getStockHistory: vi.fn().mockReturnValue(of([])),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [StockAdjustComponent],
      providers: [{ provide: InventoryAdminService, useValue: mockInventory }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(StockAdjustComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variantId', 'v1');
    fixture.detectChanges();
  });

  it('loads stock history for the variant', () => {
    expect(mockInventory.getStockHistory).toHaveBeenCalledWith('v1');
  });

  it('does not submit without a reason', () => {
    component.delta = 5;
    component.reason = '';
    component.onSubmit();
    expect(mockInventory.adjustStock).not.toHaveBeenCalled();
    expect(component.validationError).toBeTruthy();
  });

  it('submits a valid adjustment', () => {
    component.delta = -3;
    component.reason = 'Damaged in transit';
    component.onSubmit();
    expect(mockInventory.adjustStock).toHaveBeenCalledWith({ variantId: 'v1', delta: -3, reason: 'Damaged in transit' });
    expect(mockToast.success).toHaveBeenCalled();
  });
});
```

```typescript
// stock-adjust.component.ts
import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InventoryAdminService } from '../services/inventory-admin.service';
import { StockHistoryEntry } from '../models/inventory-admin.model';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-stock-adjust',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-adjust.component.html',
})
export class StockAdjustComponent implements OnChanges {
  @Input({ required: true }) variantId!: string;
  delta = 0;
  reason = '';
  validationError = '';
  history: StockHistoryEntry[] = [];

  constructor(private inventory: InventoryAdminService, private toast: ToastService) {}

  ngOnChanges(): void {
    if (this.variantId) this.loadHistory();
  }

  private loadHistory(): void {
    this.inventory.getStockHistory(this.variantId).subscribe((history) => (this.history = history));
  }

  onSubmit(): void {
    if (!this.reason.trim() || this.delta === 0) {
      this.validationError = 'Enter a non-zero quantity and a reason.';
      return;
    }
    this.validationError = '';
    this.inventory.adjustStock({ variantId: this.variantId, delta: this.delta, reason: this.reason }).subscribe({
      next: () => {
        this.toast.success('Stock adjusted.');
        this.delta = 0;
        this.reason = '';
        this.loadHistory();
      },
      error: () => this.toast.error('Failed to adjust stock.'),
    });
  }
}
```

`stock-adjust.component.html`:

```html
<div class="row g-2 align-items-end mb-2">
  <div class="col-auto">
    <label for="delta" class="form-label small mb-0">Quantity change</label>
    <input id="delta" type="number" class="form-control form-control-sm" [(ngModel)]="delta" name="delta" />
  </div>
  <div class="col-auto">
    <label for="reason" class="form-label small mb-0">Reason</label>
    <input id="reason" class="form-control form-control-sm" [(ngModel)]="reason" name="reason" />
  </div>
  <div class="col-auto"><button class="btn btn-sm btn-primary" (click)="onSubmit()">Adjust</button></div>
</div>
<div class="text-danger small" *ngIf="validationError" role="alert">{{ validationError }}</div>

<table class="table table-sm">
  <thead><tr><th>Date</th><th>Delta</th><th>Reason</th><th>Resulting qty</th></tr></thead>
  <tbody>
    <tr *ngFor="let h of history">
      <td>{{ h.createdAt | date:'medium' }}</td><td>{{ h.delta }}</td><td>{{ h.reason }}</td><td>{{ h.resultingQuantity }}</td>
    </tr>
  </tbody>
</table>
```

**Step 8.3 — RED/GREEN: `low-stock.component`** (table with threshold param, links to per-variant adjust/history):

```typescript
// low-stock.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LowStockComponent } from './low-stock.component';
import { InventoryAdminService } from '../services/inventory-admin.service';

describe('LowStockComponent', () => {
  let fixture: ComponentFixture<LowStockComponent>;
  let component: LowStockComponent;
  let mockInventory: Partial<InventoryAdminService>;

  const items = [{ variantId: 'v1', productName: 'Jacket', sku: 'SKU-1', stockQuantity: 2 }];

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockInventory = { getLowStock: vi.fn().mockReturnValue(of(items)) };

    await TestBed.configureTestingModule({
      imports: [LowStockComponent],
      providers: [{ provide: InventoryAdminService, useValue: mockInventory }],
    }).compileComponents();
    fixture = TestBed.createComponent(LowStockComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads low-stock items with the default threshold', () => {
    expect(mockInventory.getLowStock).toHaveBeenCalledWith(5);
    expect(component.items.length).toBe(1);
  });

  it('reloads when the threshold changes', () => {
    (mockInventory.getLowStock as ReturnType<typeof vi.fn>).mockClear();
    component.onThresholdChange(10);
    expect(mockInventory.getLowStock).toHaveBeenCalledWith(10);
  });

  it('selects a variant to view its adjust/history panel', () => {
    component.onSelectVariant('v1');
    expect(component.selectedVariantId).toBe('v1');
  });
});
```

```typescript
// low-stock.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { InventoryAdminService } from '../services/inventory-admin.service';
import { LowStockItem } from '../models/inventory-admin.model';
import { StockAdjustComponent } from '../stock-adjust/stock-adjust.component';

const DEFAULT_THRESHOLD = 5;

@Component({
  selector: 'app-low-stock',
  standalone: true,
  imports: [CommonModule, StockAdjustComponent],
  templateUrl: './low-stock.component.html',
})
export class LowStockComponent implements OnInit {
  items: LowStockItem[] = [];
  threshold = DEFAULT_THRESHOLD;
  selectedVariantId: string | null = null;

  constructor(private inventory: InventoryAdminService) {}

  ngOnInit(): void {
    this.load();
  }

  onThresholdChange(threshold: number): void {
    this.threshold = threshold;
    this.load();
  }

  onSelectVariant(variantId: string): void {
    this.selectedVariantId = variantId;
  }

  private load(): void {
    this.inventory.getLowStock(this.threshold).subscribe((items) => (this.items = items));
  }
}
```

`low-stock.component.html`:

```html
<h1 class="h4 mb-3">Low stock</h1>
<div class="mb-3">
  <label for="threshold" class="form-label small mb-0">Threshold</label>
  <input id="threshold" type="number" class="form-control form-control-sm w-auto" [value]="threshold"
         (change)="onThresholdChange($any($event.target).valueAsNumber)" />
</div>
<table class="table table-sm">
  <thead><tr><th>Product</th><th>SKU</th><th>Stock</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let i of items">
      <td>{{ i.productName }}</td><td>{{ i.sku }}</td><td>{{ i.stockQuantity }}</td>
      <td><button class="btn btn-sm btn-outline-primary" (click)="onSelectVariant(i.variantId)">Adjust</button></td>
    </tr>
  </tbody>
</table>
<app-stock-adjust *ngIf="selectedVariantId" [variantId]="selectedVariantId"></app-stock-adjust>
```

**Step 8.4 — `inventory.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const inventoryRoutes: Routes = [
  { path: '', loadComponent: () => import('./low-stock/low-stock.component').then((m) => m.LowStockComponent) },
];
```

**Step 8.5 — RED/GREEN: `customer-admin.service.spec.ts` / `.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { CustomerAdminService } from './customer-admin.service';

describe('CustomerAdminService', () => {
  let service: CustomerAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [CustomerAdminService, ApiService] });
    service = TestBed.inject(CustomerAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets a paged customer list', () => {
    service.getCustomers(1, 20).subscribe();
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/tenant/customers`).flush(wrap({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }));
  });

  it('gets a single customer', () => {
    service.getCustomer('c1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/customers/c1`).flush(wrap({}));
  });

  it('deactivates a customer', () => {
    service.deactivateCustomer('c1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/customers/c1/deactivate`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap({}));
  });

  it('gets a customer wishlist', () => {
    service.getWishlist('c1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/customers/c1/wishlist`).flush(wrap([]));
  });
});
```

```typescript
// customer-admin.service.ts
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse, PagedResult } from '../../../core/models/api-response.model';
import { CustomerDto, WishlistItemDto } from '../models/customer-admin.model';

@Injectable({ providedIn: 'root' })
export class CustomerAdminService {
  constructor(private apiService: ApiService) {}

  getCustomers(page: number, pageSize: number, search?: string): Observable<PagedResult<CustomerDto>> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    if (search) params = params.set('search', search);
    return this.apiService
      .get<PagedResult<CustomerDto>>('tenant/customers', params)
      .pipe(map((r: ApiResponse<PagedResult<CustomerDto>>) => r.data));
  }

  getCustomer(id: string): Observable<CustomerDto> {
    return this.apiService.get<CustomerDto>(`tenant/customers/${id}`).pipe(map((r: ApiResponse<CustomerDto>) => r.data));
  }

  deactivateCustomer(id: string): Observable<CustomerDto> {
    return this.apiService
      .put<CustomerDto>(`tenant/customers/${id}/deactivate`, {})
      .pipe(map((r: ApiResponse<CustomerDto>) => r.data));
  }

  getWishlist(customerId: string): Observable<WishlistItemDto[]> {
    return this.apiService
      .get<WishlistItemDto[]>(`tenant/customers/${customerId}/wishlist`)
      .pipe(map((r: ApiResponse<WishlistItemDto[]>) => r.data));
  }
}
```

**Step 8.6 — RED/GREEN: `customer-list.component`.**

```typescript
// customer-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { CustomerListComponent } from './customer-list.component';
import { CustomerAdminService } from '../services/customer-admin.service';

describe('CustomerListComponent', () => {
  let fixture: ComponentFixture<CustomerListComponent>;
  let component: CustomerListComponent;
  let mockCustomers: Partial<CustomerAdminService>;

  const customer = { id: 'c1', email: 'a@b.com', firstName: 'A', lastName: 'B', isActive: true, createdAt: '2026-01-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCustomers = { getCustomers: vi.fn().mockReturnValue(of({ items: [customer], totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1 })) };

    await TestBed.configureTestingModule({
      imports: [CustomerListComponent],
      providers: [provideRouter([]), { provide: CustomerAdminService, useValue: mockCustomers }],
    }).compileComponents();
    fixture = TestBed.createComponent(CustomerListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads customers on init', () => {
    expect(component.rows.length).toBe(1);
  });

  it('searches customers', () => {
    (mockCustomers.getCustomers as ReturnType<typeof vi.fn>).mockClear();
    component.onSearchChange('a@b.com');
    expect(mockCustomers.getCustomers).toHaveBeenCalledWith(1, 20, 'a@b.com');
  });

  it('paginates', () => {
    (mockCustomers.getCustomers as ReturnType<typeof vi.fn>).mockClear();
    component.onPageChange(2);
    expect(mockCustomers.getCustomers).toHaveBeenCalledWith(2, 20, undefined);
  });
});
```

```typescript
// customer-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { CustomerAdminService } from '../services/customer-admin.service';
import { CustomerDto } from '../models/customer-admin.model';
import { DataTableComponent, DataTableColumn } from '../../shared/components/data-table/data-table.component';

@Component({
  selector: 'app-customer-list',
  standalone: true,
  imports: [CommonModule, RouterModule, DataTableComponent],
  templateUrl: './customer-list.component.html',
})
export class CustomerListComponent implements OnInit {
  columns: DataTableColumn<CustomerDto>[] = [
    { key: 'email', header: 'Email' },
    { key: 'firstName', header: 'First name' },
    { key: 'lastName', header: 'Last name' },
    { key: 'createdAt', header: 'Joined', cellTemplate: 'date' },
  ];
  rows: CustomerDto[] = [];
  totalCount = 0;
  pageNumber = 1;
  pageSize = 20;
  loading = false;
  search = '';

  constructor(private customers: CustomerAdminService) {}

  ngOnInit(): void {
    this.load();
  }

  onSearchChange(term: string): void {
    this.search = term;
    this.pageNumber = 1;
    this.load();
  }

  onPageChange(page: number): void {
    this.pageNumber = page;
    this.load();
  }

  private load(): void {
    this.loading = true;
    this.customers.getCustomers(this.pageNumber, this.pageSize, this.search || undefined).subscribe((result) => {
      this.rows = result.items;
      this.totalCount = result.totalCount;
      this.loading = false;
    });
  }
}
```

`customer-list.component.html`:

```html
<h1 class="h4 mb-3">Customers</h1>
<input class="form-control form-control-sm mb-3 w-auto" placeholder="Search by email or name"
       (change)="onSearchChange($any($event.target).value)" />
<app-data-table [columns]="columns" [rows]="rows" [totalCount]="totalCount" [pageNumber]="pageNumber"
  [pageSize]="pageSize" [sortKey]="null" sortDirection="asc" [loading]="loading"
  emptyMessage="No customers found." (pageChange)="onPageChange($event)">
</app-data-table>
<table class="table table-sm mt-2">
  <tbody>
    <tr *ngFor="let c of rows"><td><a [routerLink]="[c.id]">{{ c.email }}</a></td></tr>
  </tbody>
</table>
```

**Step 8.7 — RED/GREEN: `customer-detail.component`** (orders via `OrderFilter.customerId`, wishlist, deactivate):

```typescript
// customer-detail.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { CustomerDetailComponent } from './customer-detail.component';
import { CustomerAdminService } from '../services/customer-admin.service';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('CustomerDetailComponent', () => {
  let fixture: ComponentFixture<CustomerDetailComponent>;
  let component: CustomerDetailComponent;
  let mockCustomers: Partial<CustomerAdminService>;
  let mockOrders: Partial<OrderAdminService>;
  let mockToast: Partial<ToastService>;

  const customer = { id: 'c1', email: 'a@b.com', firstName: 'A', lastName: 'B', isActive: true, createdAt: '2026-01-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockCustomers = {
      getCustomer: vi.fn().mockReturnValue(of(customer)),
      deactivateCustomer: vi.fn().mockReturnValue(of({ ...customer, isActive: false })),
      getWishlist: vi.fn().mockReturnValue(of([])),
    };
    mockOrders = { getOrders: vi.fn().mockReturnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 })) };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [CustomerDetailComponent],
      providers: [
        { provide: CustomerAdminService, useValue: mockCustomers },
        { provide: OrderAdminService, useValue: mockOrders },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'c1' } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(CustomerDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the customer, their orders, and wishlist', () => {
    expect(mockCustomers.getCustomer).toHaveBeenCalledWith('c1');
    expect(mockOrders.getOrders).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'c1' }));
    expect(mockCustomers.getWishlist).toHaveBeenCalledWith('c1');
  });

  it('deactivates the customer', () => {
    component.onDeactivate();
    expect(mockCustomers.deactivateCustomer).toHaveBeenCalledWith('c1');
    expect(mockToast.success).toHaveBeenCalled();
  });
});
```

```typescript
// customer-detail.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { CustomerAdminService } from '../services/customer-admin.service';
import { OrderAdminService } from '../../shared/services/order-admin.service';
import { CustomerDto, WishlistItemDto } from '../models/customer-admin.model';
import { OrderDto } from '../../shared/models/order-admin.model';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  templateUrl: './customer-detail.component.html',
})
export class CustomerDetailComponent implements OnInit {
  customer: CustomerDto | null = null;
  orders: OrderDto[] = [];
  wishlist: WishlistItemDto[] = [];

  constructor(
    private route: ActivatedRoute,
    private customers: CustomerAdminService,
    private orderApi: OrderAdminService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.customers.getCustomer(id).subscribe((customer) => (this.customer = customer));
    this.orderApi.getOrders({ customerId: id, page: 1, pageSize: 20 }).subscribe((result) => (this.orders = result.items));
    this.customers.getWishlist(id).subscribe((wishlist) => (this.wishlist = wishlist));
  }

  onDeactivate(): void {
    if (!this.customer) return;
    this.customers.deactivateCustomer(this.customer.id).subscribe({
      next: (customer) => { this.customer = customer; this.toast.success('Customer deactivated.'); },
      error: () => this.toast.error('Failed to deactivate customer.'),
    });
  }
}
```

`customer-detail.component.html`:

```html
<ng-container *ngIf="customer as c">
  <div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0">{{ c.firstName }} {{ c.lastName }} ({{ c.email }})</h1>
    <button class="btn btn-outline-danger btn-sm" *ngIf="c.isActive" (click)="onDeactivate()">Deactivate</button>
    <span class="badge text-bg-secondary" *ngIf="!c.isActive">Inactive</span>
  </div>

  <div class="row g-3">
    <div class="col-md-6">
      <h2 class="h6">Orders</h2>
      <table class="table table-sm">
        <thead><tr><th>Order #</th><th>Status</th><th>Total</th></tr></thead>
        <tbody>
          <tr *ngFor="let o of orders">
            <td>{{ o.orderId }}</td><td><app-status-badge [status]="o.status"></app-status-badge></td><td>{{ o.total | number:'1.2-2' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="col-md-6">
      <h2 class="h6">Wishlist</h2>
      <ul class="list-group">
        <li class="list-group-item" *ngFor="let w of wishlist">{{ w.productName }}</li>
      </ul>
    </div>
  </div>
</ng-container>
```

**Step 8.8 — `customers.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const customersRoutes: Routes = [
  { path: '', loadComponent: () => import('./customer-list/customer-list.component').then((m) => m.CustomerListComponent) },
  { path: ':id', loadComponent: () => import('./customer-detail/customer-detail.component').then((m) => m.CustomerDetailComponent) },
];
```

### Verification

```
npm run test:ci -- --run inventory-admin.service.spec stock-adjust.component.spec low-stock.component.spec customer-admin.service.spec customer-list.component.spec customer-detail.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 9 — Discounts + reviews

### Files
- Create: `fashionsaas-storefront/src/app/admin/discounts/discounts.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/models/discount-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/services/discount-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/services/discount-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-list/discount-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-list/discount-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-list/discount-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-form/discount-form.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-form/discount-form.component.html`
- Create: `fashionsaas-storefront/src/app/admin/discounts/discount-form/discount-form.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/reviews.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/models/review-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/services/review-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/services/review-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/review-queue/review-queue.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/reviews/review-queue/review-queue.component.html`
- Create: `fashionsaas-storefront/src/app/admin/reviews/review-queue/review-queue.component.spec.ts`

### Interfaces

**Consumes** (backend): `TenantDiscounts.{GetAll,GetById,GetByCode,Create,Update,Deactivate,Delete}`; `TenantReviews.{GetAll,GetById,Approve,Reject,Delete}`. Backend returns `400` with a validation error keyed by the offending field (`errors: {[key]: string[]}` per `ApiResponse<T>`) when a discount code is not unique — Task 9 surfaces `errors['code']?.[0]` via `ToastService.error`. Task 3 kit: `DataTableComponent`, `ConfirmModalComponent`, `ToastService`.

**Produces:**

```typescript
// discounts/models/discount-admin.model.ts
export type DiscountType = 'percentage' | 'fixed';

export interface DiscountDto {
  id: string;
  code: string;
  type: DiscountType;
  value: number;
  isActive: boolean;
  startsAt: string;
  endsAt: string;
}

export interface CreateDiscountRequest {
  code: string;
  type: DiscountType;
  value: number;
  startsAt: string;
  endsAt: string;
}
```

```typescript
// reviews/models/review-admin.model.ts
export type ReviewStatus = 'pending' | 'approved' | 'rejected';

export interface ReviewDto {
  id: string;
  productId: string;
  productName: string;
  customerEmail: string;
  rating: number;
  comment: string;
  status: ReviewStatus;
  createdAt: string;
}
```

`DiscountAdminService`:

```typescript
getDiscounts(page: number, pageSize: number): Observable<PagedResult<DiscountDto>>;
getDiscount(id: string): Observable<DiscountDto>;
createDiscount(req: CreateDiscountRequest): Observable<DiscountDto>;
updateDiscount(id: string, req: CreateDiscountRequest): Observable<DiscountDto>;
deactivateDiscount(id: string): Observable<DiscountDto>;
deleteDiscount(id: string): Observable<void>;
```

`ReviewAdminService`:

```typescript
getReviews(status?: ReviewStatus): Observable<ReviewDto[]>;
approve(id: string): Observable<ReviewDto>;
reject(id: string, reason: string): Observable<ReviewDto>;
```

### TDD steps

**Step 9.1 — RED/GREEN: `discount-admin.service.spec.ts` / `.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { DiscountAdminService } from './discount-admin.service';

describe('DiscountAdminService', () => {
  let service: DiscountAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [DiscountAdminService, ApiService] });
    service = TestBed.inject(DiscountAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets a paged discount list', () => {
    service.getDiscounts(1, 20).subscribe();
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/tenant/discounts`).flush(wrap({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }));
  });

  it('gets a single discount', () => {
    service.getDiscount('d1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/discounts/d1`).flush(wrap({}));
  });

  it('creates a discount', () => {
    service.createDiscount({ code: 'SAVE10', type: 'percentage', value: 10, startsAt: '2026-07-01', endsAt: '2026-08-01' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/discounts`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('updates a discount', () => {
    service.updateDiscount('d1', { code: 'SAVE10', type: 'percentage', value: 15, startsAt: '2026-07-01', endsAt: '2026-08-01' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/discounts/d1`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap({}));
  });

  it('deactivates a discount', () => {
    service.deactivateDiscount('d1').subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/discounts/d1/deactivate`).flush(wrap({}));
  });

  it('deletes a discount', () => {
    service.deleteDiscount('d1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/discounts/d1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });
});
```

```typescript
// discount-admin.service.ts
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse, PagedResult } from '../../../core/models/api-response.model';
import { DiscountDto, CreateDiscountRequest } from '../models/discount-admin.model';

@Injectable({ providedIn: 'root' })
export class DiscountAdminService {
  constructor(private apiService: ApiService) {}

  getDiscounts(page: number, pageSize: number): Observable<PagedResult<DiscountDto>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.apiService
      .get<PagedResult<DiscountDto>>('tenant/discounts', params)
      .pipe(map((r: ApiResponse<PagedResult<DiscountDto>>) => r.data));
  }

  getDiscount(id: string): Observable<DiscountDto> {
    return this.apiService.get<DiscountDto>(`tenant/discounts/${id}`).pipe(map((r: ApiResponse<DiscountDto>) => r.data));
  }

  createDiscount(req: CreateDiscountRequest): Observable<DiscountDto> {
    return this.apiService.post<DiscountDto>('tenant/discounts', req).pipe(map((r: ApiResponse<DiscountDto>) => r.data));
  }

  updateDiscount(id: string, req: CreateDiscountRequest): Observable<DiscountDto> {
    return this.apiService.put<DiscountDto>(`tenant/discounts/${id}`, req).pipe(map((r: ApiResponse<DiscountDto>) => r.data));
  }

  deactivateDiscount(id: string): Observable<DiscountDto> {
    return this.apiService
      .put<DiscountDto>(`tenant/discounts/${id}/deactivate`, {})
      .pipe(map((r: ApiResponse<DiscountDto>) => r.data));
  }

  deleteDiscount(id: string): Observable<void> {
    return this.apiService.delete<void>(`tenant/discounts/${id}`).pipe(map((r: ApiResponse<void>) => r.data));
  }
}
```

**Step 9.2 — RED/GREEN: `discount-list.component`** (list + deactivate + delete, code-uniqueness error surfaced from the form in Step 9.3):

```typescript
// discount-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { DiscountListComponent } from './discount-list.component';
import { DiscountAdminService } from '../services/discount-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('DiscountListComponent', () => {
  let fixture: ComponentFixture<DiscountListComponent>;
  let component: DiscountListComponent;
  let mockDiscounts: Partial<DiscountAdminService>;
  let mockToast: Partial<ToastService>;

  const discount = { id: 'd1', code: 'SAVE10', type: 'percentage' as const, value: 10, isActive: true, startsAt: '2026-07-01', endsAt: '2026-08-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockDiscounts = {
      getDiscounts: vi.fn().mockReturnValue(of({ items: [discount], totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1 })),
      deactivateDiscount: vi.fn().mockReturnValue(of({ ...discount, isActive: false })),
      deleteDiscount: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [DiscountListComponent],
      providers: [provideRouter([]), { provide: DiscountAdminService, useValue: mockDiscounts }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(DiscountListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads discounts on init', () => {
    expect(component.rows.length).toBe(1);
  });

  it('deactivates a discount', () => {
    component.onDeactivate(discount as any);
    expect(mockDiscounts.deactivateDiscount).toHaveBeenCalledWith('d1');
  });

  it('opens the delete modal and deletes on confirm', () => {
    component.openDeleteModal(discount as any);
    component.onDeleteConfirmed();
    expect(mockDiscounts.deleteDiscount).toHaveBeenCalledWith('d1');
  });
});
```

```typescript
// discount-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { DiscountAdminService } from '../services/discount-admin.service';
import { DiscountDto } from '../models/discount-admin.model';
import { DataTableComponent, DataTableColumn } from '../../shared/components/data-table/data-table.component';
import { ConfirmModalComponent } from '../../shared/components/confirm-modal/confirm-modal.component';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-discount-list',
  standalone: true,
  imports: [CommonModule, RouterModule, DataTableComponent, ConfirmModalComponent],
  templateUrl: './discount-list.component.html',
})
export class DiscountListComponent implements OnInit {
  columns: DataTableColumn<DiscountDto>[] = [
    { key: 'code', header: 'Code' },
    { key: 'type', header: 'Type' },
    { key: 'value', header: 'Value' },
    { key: 'endsAt', header: 'Ends', cellTemplate: 'date' },
  ];
  rows: DiscountDto[] = [];
  totalCount = 0;
  pageNumber = 1;
  pageSize = 20;
  loading = false;
  deleteModalOpen = false;
  discountPendingDelete: DiscountDto | null = null;

  constructor(private discounts: DiscountAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  onPageChange(page: number): void {
    this.pageNumber = page;
    this.load();
  }

  onDeactivate(discount: DiscountDto): void {
    this.discounts.deactivateDiscount(discount.id).subscribe({
      next: () => { this.toast.success('Discount deactivated.'); this.load(); },
      error: () => this.toast.error('Failed to deactivate discount.'),
    });
  }

  openDeleteModal(discount: DiscountDto): void {
    this.discountPendingDelete = discount;
    this.deleteModalOpen = true;
  }

  onDeleteCancelled(): void {
    this.deleteModalOpen = false;
  }

  onDeleteConfirmed(): void {
    if (!this.discountPendingDelete) return;
    this.discounts.deleteDiscount(this.discountPendingDelete.id).subscribe({
      next: () => { this.toast.success('Discount deleted.'); this.deleteModalOpen = false; this.load(); },
      error: () => { this.toast.error('Failed to delete discount.'); this.deleteModalOpen = false; },
    });
  }

  private load(): void {
    this.loading = true;
    this.discounts.getDiscounts(this.pageNumber, this.pageSize).subscribe((result) => {
      this.rows = result.items;
      this.totalCount = result.totalCount;
      this.loading = false;
    });
  }
}
```

`discount-list.component.html`:

```html
<div class="d-flex justify-content-between align-items-center mb-3">
  <h1 class="h4 mb-0">Discounts</h1>
  <a routerLink="new" class="btn btn-primary btn-sm"><i class="bi bi-plus-lg"></i> New discount</a>
</div>
<app-data-table [columns]="columns" [rows]="rows" [totalCount]="totalCount" [pageNumber]="pageNumber"
  [pageSize]="pageSize" [sortKey]="null" sortDirection="asc" [loading]="loading"
  emptyMessage="No discounts found." (pageChange)="onPageChange($event)">
</app-data-table>
<table class="table table-sm mt-2">
  <tbody>
    <tr *ngFor="let d of rows">
      <td>{{ d.code }}</td>
      <td>
        <a [routerLink]="[d.id]" class="btn btn-sm btn-outline-secondary">Edit</a>
        <button class="btn btn-sm btn-outline-warning" *ngIf="d.isActive" (click)="onDeactivate(d)">Deactivate</button>
        <button class="btn btn-sm btn-outline-danger" (click)="openDeleteModal(d)">Delete</button>
      </td>
    </tr>
  </tbody>
</table>
<app-confirm-modal [isOpen]="deleteModalOpen" title="Delete discount"
  [message]="'Delete ' + (discountPendingDelete?.code ?? '') + '?'" confirmLabel="Delete" tone="danger"
  (confirmed)="onDeleteConfirmed()" (cancelled)="onDeleteCancelled()">
</app-confirm-modal>
```

**Step 9.3 — RED/GREEN: `discount-form.component`** (create/edit, surfaces code-uniqueness `400` via toast):

```typescript
// discount-form.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { DiscountFormComponent } from './discount-form.component';
import { DiscountAdminService } from '../services/discount-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('DiscountFormComponent', () => {
  let fixture: ComponentFixture<DiscountFormComponent>;
  let component: DiscountFormComponent;
  let mockDiscounts: Partial<DiscountAdminService>;
  let mockToast: Partial<ToastService>;

  function setup(paramId: string | null): void {
    mockDiscounts = {
      createDiscount: vi.fn().mockReturnValue(of({ id: 'd2' })),
      updateDiscount: vi.fn().mockReturnValue(of({ id: 'd1' })),
      getDiscount: vi.fn().mockReturnValue(of({ id: 'd1', code: 'SAVE10', type: 'percentage', value: 10, isActive: true, startsAt: '2026-07-01', endsAt: '2026-08-01' })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [DiscountFormComponent],
      providers: [
        provideRouter([]),
        { provide: DiscountAdminService, useValue: mockDiscounts },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => paramId } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(DiscountFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('creates a discount on submit in create mode', () => {
    setup(null);
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.form.setValue({ code: 'SAVE20', type: 'percentage', value: 20, startsAt: '2026-07-01', endsAt: '2026-08-01' });
    component.onSubmit();
    expect(mockDiscounts.createDiscount).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith(['/admin/discounts']);
  });

  it('surfaces a duplicate-code validation error via a toast', () => {
    setup(null);
    (mockDiscounts.createDiscount as ReturnType<typeof vi.fn>).mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { code: ['Discount code already exists.'] } } }))
    );
    component.form.setValue({ code: 'SAVE10', type: 'percentage', value: 10, startsAt: '2026-07-01', endsAt: '2026-08-01' });
    component.onSubmit();
    expect(mockToast.error).toHaveBeenCalledWith('Discount code already exists.');
  });

  it('loads an existing discount in edit mode', () => {
    setup('d1');
    expect(component.isEditMode).toBe(true);
    expect(component.form.value.code).toBe('SAVE10');
  });
});
```

```typescript
// discount-form.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { DiscountAdminService } from '../services/discount-admin.service';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-discount-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './discount-form.component.html',
})
export class DiscountFormComponent implements OnInit {
  isEditMode = false;
  discountId: string | null = null;

  form = this.fb.group({
    code: this.fb.nonNullable.control('', Validators.required),
    type: this.fb.nonNullable.control<'percentage' | 'fixed'>('percentage', Validators.required),
    value: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0.01)]),
    startsAt: this.fb.nonNullable.control('', Validators.required),
    endsAt: this.fb.nonNullable.control('', Validators.required),
  });

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private discounts: DiscountAdminService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isEditMode = true;
      this.discountId = id;
      this.discounts.getDiscount(id).subscribe((discount) => this.form.setValue({
        code: discount.code, type: discount.type, value: discount.value, startsAt: discount.startsAt, endsAt: discount.endsAt,
      }));
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.getRawValue();
    const save = this.isEditMode && this.discountId
      ? this.discounts.updateDiscount(this.discountId, payload)
      : this.discounts.createDiscount(payload);

    save.subscribe({
      next: () => {
        this.toast.success(this.isEditMode ? 'Discount updated.' : 'Discount created.');
        this.router.navigate(['/admin/discounts']);
      },
      error: (err: unknown) => {
        const message = err instanceof HttpErrorResponse ? err.error?.errors?.code?.[0] : undefined;
        this.toast.error(message ?? 'Failed to save discount.');
      },
    });
  }
}
```

`discount-form.component.html`:

```html
<h1 class="h4 mb-3">{{ isEditMode ? 'Edit discount' : 'New discount' }}</h1>
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div class="mb-3">
    <label for="code" class="form-label">Code</label>
    <input id="code" class="form-control" formControlName="code" />
  </div>
  <div class="mb-3">
    <label for="type" class="form-label">Type</label>
    <select id="type" class="form-select" formControlName="type">
      <option value="percentage">Percentage</option>
      <option value="fixed">Fixed amount</option>
    </select>
  </div>
  <div class="mb-3">
    <label for="value" class="form-label">Value</label>
    <input id="value" type="number" step="0.01" class="form-control" formControlName="value" />
  </div>
  <div class="mb-3">
    <label for="startsAt" class="form-label">Starts</label>
    <input id="startsAt" type="date" class="form-control" formControlName="startsAt" />
  </div>
  <div class="mb-3">
    <label for="endsAt" class="form-label">Ends</label>
    <input id="endsAt" type="date" class="form-control" formControlName="endsAt" />
  </div>
  <button type="submit" class="btn btn-primary">Save</button>
  <a routerLink="/admin/discounts" class="btn btn-link">Cancel</a>
</form>
```

**Step 9.4 — `discounts.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const discountsRoutes: Routes = [
  { path: '', loadComponent: () => import('./discount-list/discount-list.component').then((m) => m.DiscountListComponent) },
  { path: 'new', loadComponent: () => import('./discount-form/discount-form.component').then((m) => m.DiscountFormComponent) },
  { path: ':id', loadComponent: () => import('./discount-form/discount-form.component').then((m) => m.DiscountFormComponent) },
];
```

**Step 9.5 — RED/GREEN: `review-admin.service.spec.ts` / `.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { ReviewAdminService } from './review-admin.service';

describe('ReviewAdminService', () => {
  let service: ReviewAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [ReviewAdminService, ApiService] });
    service = TestBed.inject(ReviewAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets reviews filtered by status', () => {
    service.getReviews('pending').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/tenant/reviews` && r.params.get('status') === 'pending');
    req.flush(wrap([]));
  });

  it('gets all reviews when no status is given', () => {
    service.getReviews().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/reviews`);
    expect(req.request.params.has('status')).toBe(false);
    req.flush(wrap([]));
  });

  it('approves a review', () => {
    service.approve('r1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/reviews/r1/approve`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap({}));
  });

  it('rejects a review with a reason', () => {
    service.reject('r1', 'Spam').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/reviews/r1/reject`);
    expect(req.request.body).toEqual({ reason: 'Spam' });
    req.flush(wrap({}));
  });
});
```

```typescript
// review-admin.service.ts
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse } from '../../../core/models/api-response.model';
import { ReviewDto, ReviewStatus } from '../models/review-admin.model';

@Injectable({ providedIn: 'root' })
export class ReviewAdminService {
  constructor(private apiService: ApiService) {}

  getReviews(status?: ReviewStatus): Observable<ReviewDto[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.apiService.get<ReviewDto[]>('tenant/reviews', params).pipe(map((r: ApiResponse<ReviewDto[]>) => r.data));
  }

  approve(id: string): Observable<ReviewDto> {
    return this.apiService.put<ReviewDto>(`tenant/reviews/${id}/approve`, {}).pipe(map((r: ApiResponse<ReviewDto>) => r.data));
  }

  reject(id: string, reason: string): Observable<ReviewDto> {
    return this.apiService
      .put<ReviewDto>(`tenant/reviews/${id}/reject`, { reason })
      .pipe(map((r: ApiResponse<ReviewDto>) => r.data));
  }
}
```

**Step 9.6 — RED/GREEN: `review-queue.component`** (moderation queue, approve / reject-with-reason-modal):

```typescript
// review-queue.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ReviewQueueComponent } from './review-queue.component';
import { ReviewAdminService } from '../services/review-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('ReviewQueueComponent', () => {
  let fixture: ComponentFixture<ReviewQueueComponent>;
  let component: ReviewQueueComponent;
  let mockReviews: Partial<ReviewAdminService>;
  let mockToast: Partial<ToastService>;

  const review = { id: 'r1', productId: 'p1', productName: 'Jacket', customerEmail: 'a@b.com', rating: 4, comment: 'Nice', status: 'pending' as const, createdAt: '2026-07-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockReviews = {
      getReviews: vi.fn().mockReturnValue(of([review])),
      approve: vi.fn().mockReturnValue(of({ ...review, status: 'approved' })),
      reject: vi.fn().mockReturnValue(of({ ...review, status: 'rejected' })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [ReviewQueueComponent],
      providers: [{ provide: ReviewAdminService, useValue: mockReviews }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(ReviewQueueComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads pending reviews by default', () => {
    expect(mockReviews.getReviews).toHaveBeenCalledWith('pending');
    expect(component.reviews.length).toBe(1);
  });

  it('approves a review', () => {
    component.onApprove(review as any);
    expect(mockReviews.approve).toHaveBeenCalledWith('r1');
    expect(mockToast.success).toHaveBeenCalled();
  });

  it('opens the reject modal and rejects with a reason', () => {
    component.openRejectModal(review as any);
    expect(component.rejectModalOpen).toBe(true);
    component.onRejectConfirmed('Inappropriate content');
    expect(mockReviews.reject).toHaveBeenCalledWith('r1', 'Inappropriate content');
    expect(component.rejectModalOpen).toBe(false);
  });
});
```

```typescript
// review-queue.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReviewAdminService } from '../services/review-admin.service';
import { ReviewDto } from '../models/review-admin.model';
import { ConfirmModalComponent } from '../../shared/components/confirm-modal/confirm-modal.component';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './review-queue.component.html',
})
export class ReviewQueueComponent implements OnInit {
  reviews: ReviewDto[] = [];
  rejectModalOpen = false;
  reviewPendingReject: ReviewDto | null = null;
  rejectReasonInput = '';

  constructor(private reviewApi: ReviewAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.reviewApi.getReviews('pending').subscribe((reviews) => (this.reviews = reviews));
  }

  onApprove(review: ReviewDto): void {
    this.reviewApi.approve(review.id).subscribe({
      next: () => { this.toast.success('Review approved.'); this.load(); },
      error: () => this.toast.error('Failed to approve review.'),
    });
  }

  openRejectModal(review: ReviewDto): void {
    this.reviewPendingReject = review;
    this.rejectModalOpen = true;
  }

  onRejectCancelled(): void {
    this.rejectModalOpen = false;
  }

  onRejectConfirmed(reason: string): void {
    if (!this.reviewPendingReject) return;
    this.reviewApi.reject(this.reviewPendingReject.id, reason).subscribe({
      next: () => { this.toast.success('Review rejected.'); this.rejectModalOpen = false; this.load(); },
      error: () => { this.toast.error('Failed to reject review.'); this.rejectModalOpen = false; },
    });
  }
}
```

`review-queue.component.html`:

```html
<h1 class="h4 mb-3">Review moderation queue</h1>
<div class="card mb-2" *ngFor="let r of reviews">
  <div class="card-body">
    <h2 class="h6">{{ r.productName }} — {{ r.rating }}/5</h2>
    <p class="text-muted small mb-1">{{ r.customerEmail }}</p>
    <p>{{ r.comment }}</p>
    <button class="btn btn-sm btn-outline-success" (click)="onApprove(r)">Approve</button>
    <input class="form-control form-control-sm d-inline-block w-auto" placeholder="Rejection reason"
           [(ngModel)]="rejectReasonInput" name="rejectReasonInput" />
    <button class="btn btn-sm btn-outline-danger" (click)="openRejectModal(r)">Reject</button>
  </div>
</div>
<div *ngIf="reviews.length === 0" class="text-muted">No pending reviews.</div>

<app-confirm-modal
  [isOpen]="rejectModalOpen"
  title="Reject review"
  [message]="'Reject this review from ' + (reviewPendingReject?.customerEmail ?? '') + '?'"
  confirmLabel="Reject"
  tone="danger"
  (confirmed)="onRejectConfirmed(rejectReasonInput)"
  (cancelled)="onRejectCancelled()">
</app-confirm-modal>
```

**Step 9.7 — `reviews.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const reviewsRoutes: Routes = [
  { path: '', loadComponent: () => import('./review-queue/review-queue.component').then((m) => m.ReviewQueueComponent) },
];
```

### Verification

```
npm run test:ci -- --run discount-admin.service.spec discount-list.component.spec discount-form.component.spec review-admin.service.spec review-queue.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 10 — Reports + settings

### Files
- Create: `fashionsaas-storefront/src/app/admin/reports/reports.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/reports/report-page/report-page.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/reports/report-page/report-page.component.html`
- Create: `fashionsaas-storefront/src/app/admin/reports/report-page/report-page.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/settings.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/models/settings-admin.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/services/settings-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/services/settings-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/settings-guard/admin-owner.guard.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/settings-guard/admin-owner.guard.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/profile/tenant-profile.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/profile/tenant-profile.component.html`
- Create: `fashionsaas-storefront/src/app/admin/settings/profile/tenant-profile.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/users/tenant-users.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/users/tenant-users.component.html`
- Create: `fashionsaas-storefront/src/app/admin/settings/users/tenant-users.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/subscription/tenant-subscription.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/subscription/tenant-subscription.component.html`
- Create: `fashionsaas-storefront/src/app/admin/settings/subscription/tenant-subscription.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/bank-account/tenant-bank-account.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/settings/bank-account/tenant-bank-account.component.html`
- Create: `fashionsaas-storefront/src/app/admin/settings/bank-account/tenant-bank-account.component.spec.ts`

### Interfaces

**Consumes** (backend): `TenantReports.*` (Task 4's `ReportApiService`, all 7 getters + `downloadCsv`); `TenantProfile.{Get,Update}`; `TenantUsers.{GetAll,GetById,Create,Update,AssignRole,Delete}`; `TenantSubscription.{Get,GetPayments}`; `TenantBankAccount.{Get,GetFull,Create,Update}` — `GetFull` requires a fresh TOTP code, mirrored from the MFA challenge pattern in Task 1 (`code` field, not a session token). Task 3 kit: `DataTableComponent`, `KpiCardComponent`, `DateRangePickerComponent`, `ToastService`.

**Produces:**

```typescript
// settings/models/settings-admin.model.ts
export interface TenantProfileDto {
  id: string;
  name: string;
  slug: string;
  contactEmail: string;
  contactPhone: string;
}

export interface TenantUserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  isActive: boolean;
}

export interface CreateTenantUserRequest {
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
}

export interface TenantSubscriptionDto {
  id: string;
  planName: string;
  status: string;
  currentPeriodEnd: string;
}

export interface TenantPaymentDto {
  id: string;
  amount: number;
  status: string;
  paidAt: string | null;
}

export interface BankAccountMaskedDto {
  id: string;
  accountHolderName: string;
  maskedAccountNumber: string;  // e.g. "****1234"
  bankName: string;
}

export interface BankAccountFullDto extends BankAccountMaskedDto {
  accountNumber: string;
  routingNumber: string;
}
```

`SettingsAdminService`:

```typescript
getProfile(): Observable<TenantProfileDto>;
updateProfile(req: Partial<TenantProfileDto>): Observable<TenantProfileDto>;
getUsers(): Observable<TenantUserDto[]>;
createUser(req: CreateTenantUserRequest): Observable<TenantUserDto>;
assignRole(userId: string, role: string): Observable<TenantUserDto>;
deleteUser(userId: string): Observable<void>;
getSubscription(): Observable<TenantSubscriptionDto>;
getPayments(): Observable<TenantPaymentDto[]>;
getBankAccount(): Observable<BankAccountMaskedDto>;
getBankAccountFull(code: string): Observable<BankAccountFullDto>;
```

`adminOwnerGuard: CanActivateFn` — true iff `authService.hasAnyRole(['AdminOwner'])`; redirects to `/admin` otherwise (mirrors `superAdminGuard`'s off-ramp pattern from Task 1).

### TDD steps

**Step 10.1 — RED: `report-page.component.spec.ts`** (one generic, config-driven page reused for all 7 reports via route data):

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { ReportPageComponent } from './report-page.component';
import { ReportApiService } from '../../shared/services/report-api.service';

describe('ReportPageComponent', () => {
  let fixture: ComponentFixture<ReportPageComponent>;
  let component: ReportPageComponent;
  let mockReportApi: Partial<ReportApiService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockReportApi = {
      getSalesOverTime: vi.fn().mockReturnValue(of([{ periodStart: '2026-07-01', revenue: 100, orderCount: 2 }])),
      downloadCsv: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [ReportPageComponent],
      providers: [
        { provide: ReportApiService, useValue: mockReportApi },
        { provide: ActivatedRoute, useValue: { snapshot: { data: { reportKey: 'sales-over-time', title: 'Sales over time' } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ReportPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the report identified by route data', () => {
    expect(mockReportApi.getSalesOverTime).toHaveBeenCalled();
    expect(component.rows.length).toBe(1);
  });

  it('triggers a CSV download with the current date range', () => {
    component.onDownloadCsv();
    expect(mockReportApi.downloadCsv).toHaveBeenCalledWith('sales-over-time', { from: component.range.from, to: component.range.to });
  });

  it('reloads when the date range changes', () => {
    (mockReportApi.getSalesOverTime as ReturnType<typeof vi.fn>).mockClear();
    component.onRangeChange({ from: '2026-06-01', to: '2026-06-30' });
    expect(mockReportApi.getSalesOverTime).toHaveBeenCalled();
  });
});
```

**Step 10.2 — GREEN: `report-page.component.ts`** (dispatches to the correct `ReportApiService` getter by `reportKey`, normalizing every report shape into a flat row array for `DataTableComponent`):

```typescript
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ReportApiService } from '../../shared/services/report-api.service';
import { DateRangePickerComponent, DateRange } from '../../shared/components/date-range-picker/date-range-picker.component';
import { DataTableComponent, DataTableColumn } from '../../shared/components/data-table/data-table.component';

type ReportKey =
  | 'summary' | 'sales-over-time' | 'top-products' | 'order-status-breakdown'
  | 'customer-analytics' | 'inventory-trends' | 'category-sales';

const REPORT_COLUMNS: Record<ReportKey, DataTableColumn<Record<string, unknown>>[]> = {
  'summary': [{ key: 'revenue', header: 'Revenue', cellTemplate: 'currency' }, { key: 'orderCount', header: 'Orders' }],
  'sales-over-time': [{ key: 'periodStart', header: 'Period', cellTemplate: 'date' }, { key: 'revenue', header: 'Revenue', cellTemplate: 'currency' }, { key: 'orderCount', header: 'Orders' }],
  'top-products': [{ key: 'productName', header: 'Product' }, { key: 'revenue', header: 'Revenue', cellTemplate: 'currency' }, { key: 'units', header: 'Units' }],
  'order-status-breakdown': [{ key: 'status', header: 'Status' }, { key: 'count', header: 'Count' }, { key: 'revenue', header: 'Revenue', cellTemplate: 'currency' }],
  'customer-analytics': [{ key: 'email', header: 'Customer' }, { key: 'totalSpend', header: 'Total spend', cellTemplate: 'currency' }, { key: 'orderCount', header: 'Orders' }],
  'inventory-trends': [{ key: 'productName', header: 'Product' }, { key: 'sku', header: 'SKU' }, { key: 'stockQuantity', header: 'Stock' }],
  'category-sales': [{ key: 'categoryName', header: 'Category' }, { key: 'revenue', header: 'Revenue', cellTemplate: 'currency' }, { key: 'units', header: 'Units' }],
};

function defaultRange(): DateRange {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 30);
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  return { from: iso(from), to: iso(to) };
}

@Component({
  selector: 'app-report-page',
  standalone: true,
  imports: [CommonModule, DateRangePickerComponent, DataTableComponent],
  templateUrl: './report-page.component.html',
})
export class ReportPageComponent implements OnInit {
  reportKey!: ReportKey;
  title = '';
  columns: DataTableColumn<Record<string, unknown>>[] = [];
  rows: Record<string, unknown>[] = [];
  range: DateRange = defaultRange();
  loading = false;

  constructor(private route: ActivatedRoute, private reportApi: ReportApiService) {}

  ngOnInit(): void {
    this.reportKey = this.route.snapshot.data['reportKey'];
    this.title = this.route.snapshot.data['title'];
    this.columns = REPORT_COLUMNS[this.reportKey];
    this.load();
  }

  onRangeChange(range: DateRange): void {
    this.range = range;
    this.load();
  }

  onDownloadCsv(): void {
    this.reportApi.downloadCsv(this.reportKey, { from: this.range.from, to: this.range.to });
  }

  private load(): void {
    this.loading = true;
    const finish = (rows: Record<string, unknown>[]) => { this.rows = rows; this.loading = false; };

    switch (this.reportKey) {
      case 'summary':
        this.reportApi.getSummary(this.range).subscribe((r) => finish([r as unknown as Record<string, unknown>]));
        break;
      case 'sales-over-time':
        this.reportApi.getSalesOverTime(this.range, 'Day').subscribe((r) => finish(r as unknown as Record<string, unknown>[]));
        break;
      case 'top-products':
        this.reportApi.getTopProducts(this.range, 10, 'revenue').subscribe((r) => finish(r as unknown as Record<string, unknown>[]));
        break;
      case 'order-status-breakdown':
        this.reportApi.getStatusBreakdown(this.range).subscribe((r) => finish(r as unknown as Record<string, unknown>[]));
        break;
      case 'customer-analytics':
        this.reportApi.getCustomerAnalytics(this.range, 'Day').subscribe((r) => finish(r.topCustomers as unknown as Record<string, unknown>[]));
        break;
      case 'inventory-trends':
        this.reportApi.getInventoryTrends(this.range).subscribe((r) => finish(r.lowStock as unknown as Record<string, unknown>[]));
        break;
      case 'category-sales':
        this.reportApi.getCategorySales(this.range).subscribe((r) => finish(r as unknown as Record<string, unknown>[]));
        break;
    }
  }
}
```

`report-page.component.html`:

```html
<div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mb-3">
  <h1 class="h4 mb-0">{{ title }}</h1>
  <div class="d-flex align-items-end gap-2">
    <app-date-range-picker [range]="range" (rangeChange)="onRangeChange($event)"></app-date-range-picker>
    <button class="btn btn-outline-secondary btn-sm" (click)="onDownloadCsv()"><i class="bi bi-download"></i> CSV</button>
  </div>
</div>
<app-data-table [columns]="columns" [rows]="rows" [totalCount]="rows.length" [pageNumber]="1" [pageSize]="rows.length || 1"
  [sortKey]="null" sortDirection="asc" [loading]="loading" emptyMessage="No data for this range.">
</app-data-table>
```

**Step 10.3 — `reports.routes.ts`** (route `data` drives the shared page; titles/keys match `ApiUrl.TenantReports`):

```typescript
import { Routes } from '@angular/router';
import { ReportPageComponent } from './report-page/report-page.component';

export const reportsRoutes: Routes = [
  { path: '', redirectTo: 'summary', pathMatch: 'full' },
  { path: 'summary', component: ReportPageComponent, data: { reportKey: 'summary', title: 'Summary' } },
  { path: 'sales-over-time', component: ReportPageComponent, data: { reportKey: 'sales-over-time', title: 'Sales over time' } },
  { path: 'top-products', component: ReportPageComponent, data: { reportKey: 'top-products', title: 'Top products' } },
  { path: 'order-status-breakdown', component: ReportPageComponent, data: { reportKey: 'order-status-breakdown', title: 'Order status breakdown' } },
  { path: 'customer-analytics', component: ReportPageComponent, data: { reportKey: 'customer-analytics', title: 'Customer analytics' } },
  { path: 'inventory-trends', component: ReportPageComponent, data: { reportKey: 'inventory-trends', title: 'Inventory trends' } },
  { path: 'category-sales', component: ReportPageComponent, data: { reportKey: 'category-sales', title: 'Category sales' } },
];
```

Note: `ReportPageComponent` is a direct (non-lazy) `component:` route entry here because it is already reached only via the lazy `reports.routes.ts` chunk — no separate `loadComponent` indirection is needed per sub-report.

**Step 10.4 — RED/GREEN: `settings-admin.service.spec.ts` / `.ts`.**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { SettingsAdminService } from './settings-admin.service';

describe('SettingsAdminService', () => {
  let service: SettingsAdminService;
  let httpMock: HttpTestingController;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [SettingsAdminService, ApiService] });
    service = TestBed.inject(SettingsAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets the tenant profile', () => {
    service.getProfile().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/profile`).flush(wrap({}));
  });

  it('updates the tenant profile', () => {
    service.updateProfile({ name: 'New Name' }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/profile`);
    expect(req.request.method).toBe('PUT');
    req.flush(wrap({}));
  });

  it('gets tenant users', () => {
    service.getUsers().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/users`).flush(wrap([]));
  });

  it('creates a tenant user', () => {
    service.createUser({ email: 'a@b.com', firstName: 'A', lastName: 'B', roles: ['StoreManager'] }).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/users`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('assigns a role to a user', () => {
    service.assignRole('u1', 'InventoryManager').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/users/u1/assign-role`);
    expect(req.request.body).toEqual({ role: 'InventoryManager' });
    req.flush(wrap({}));
  });

  it('deletes a tenant user', () => {
    service.deleteUser('u1').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/users/u1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets the subscription', () => {
    service.getSubscription().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/subscription`).flush(wrap({}));
  });

  it('gets subscription payments', () => {
    service.getPayments().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/subscription/payments`).flush(wrap([]));
  });

  it('gets the masked bank account', () => {
    service.getBankAccount().subscribe();
    httpMock.expectOne(`${environment.apiBaseUrl}/tenant/bank-account`).flush(wrap({}));
  });

  it('gets the full bank account with a TOTP code', () => {
    service.getBankAccountFull('123456').subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/bank-account/full`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: '123456' });
    req.flush(wrap({}));
  });
});
```

```typescript
// settings-admin.service.ts
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse } from '../../../core/models/api-response.model';
import {
  TenantProfileDto, TenantUserDto, CreateTenantUserRequest,
  TenantSubscriptionDto, TenantPaymentDto, BankAccountMaskedDto, BankAccountFullDto,
} from '../models/settings-admin.model';

@Injectable({ providedIn: 'root' })
export class SettingsAdminService {
  constructor(private apiService: ApiService) {}

  getProfile(): Observable<TenantProfileDto> {
    return this.apiService.get<TenantProfileDto>('tenant/profile').pipe(map((r: ApiResponse<TenantProfileDto>) => r.data));
  }

  updateProfile(req: Partial<TenantProfileDto>): Observable<TenantProfileDto> {
    return this.apiService.put<TenantProfileDto>('tenant/profile', req).pipe(map((r: ApiResponse<TenantProfileDto>) => r.data));
  }

  getUsers(): Observable<TenantUserDto[]> {
    return this.apiService.get<TenantUserDto[]>('tenant/users').pipe(map((r: ApiResponse<TenantUserDto[]>) => r.data));
  }

  createUser(req: CreateTenantUserRequest): Observable<TenantUserDto> {
    return this.apiService.post<TenantUserDto>('tenant/users', req).pipe(map((r: ApiResponse<TenantUserDto>) => r.data));
  }

  assignRole(userId: string, role: string): Observable<TenantUserDto> {
    return this.apiService
      .put<TenantUserDto>(`tenant/users/${userId}/assign-role`, { role })
      .pipe(map((r: ApiResponse<TenantUserDto>) => r.data));
  }

  deleteUser(userId: string): Observable<void> {
    return this.apiService.delete<void>(`tenant/users/${userId}`).pipe(map((r: ApiResponse<void>) => r.data));
  }

  getSubscription(): Observable<TenantSubscriptionDto> {
    return this.apiService
      .get<TenantSubscriptionDto>('tenant/subscription')
      .pipe(map((r: ApiResponse<TenantSubscriptionDto>) => r.data));
  }

  getPayments(): Observable<TenantPaymentDto[]> {
    return this.apiService
      .get<TenantPaymentDto[]>('tenant/subscription/payments')
      .pipe(map((r: ApiResponse<TenantPaymentDto[]>) => r.data));
  }

  getBankAccount(): Observable<BankAccountMaskedDto> {
    return this.apiService
      .get<BankAccountMaskedDto>('tenant/bank-account')
      .pipe(map((r: ApiResponse<BankAccountMaskedDto>) => r.data));
  }

  getBankAccountFull(code: string): Observable<BankAccountFullDto> {
    return this.apiService
      .post<BankAccountFullDto>('tenant/bank-account/full', { code })
      .pipe(map((r: ApiResponse<BankAccountFullDto>) => r.data));
  }
}
```

**Step 10.5 — RED/GREEN: `admin-owner.guard.spec.ts` / `.ts`** (mirrors `superAdminGuard` from Task 1 exactly, only the role check and off-ramp differ):

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { adminOwnerGuard } from './admin-owner.guard';
import { AuthService } from '../../../core/services/auth.service';

describe('adminOwnerGuard', () => {
  let mockAuth: Partial<AuthService>;
  let router: Router;

  const run = () => TestBed.runInInjectionContext(() => adminOwnerGuard({} as never, { url: '/admin/settings' } as never));

  beforeEach(() => {
    TestBed.resetTestingModule();
    mockAuth = { isAuthenticated: () => of(true), hasAnyRole: vi.fn().mockReturnValue(true) };
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: mockAuth }] });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  it('allows access for AdminOwner', async () => {
    expect(await run()).toBe(true);
  });

  it('redirects to /admin for a non-AdminOwner tenant admin', async () => {
    (mockAuth.hasAnyRole as ReturnType<typeof vi.fn>).mockReturnValue(false);
    expect(await run()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/admin']);
  });

  it('redirects to /login when unauthenticated', async () => {
    mockAuth.isAuthenticated = () => of(false);
    expect(await run()).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/admin/settings' } });
  });
});
```

```typescript
// admin-owner.guard.ts
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, take } from 'rxjs/operators';
import { AuthService } from '../../../core/services/auth.service';

export const adminOwnerGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.isAuthenticated().pipe(
    take(1),
    map((isAuthenticated) => {
      if (!isAuthenticated) {
        router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
        return false;
      }
      if (!authService.hasAnyRole(['AdminOwner'])) {
        router.navigate(['/admin']);
        return false;
      }
      return true;
    })
  );
};
```

**Step 10.6 — RED/GREEN: `tenant-profile.component`.**

```typescript
// tenant-profile.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ReactiveFormsModule } from '@angular/forms';
import { TenantProfileComponent } from './tenant-profile.component';
import { SettingsAdminService } from '../services/settings-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('TenantProfileComponent', () => {
  let fixture: ComponentFixture<TenantProfileComponent>;
  let component: TenantProfileComponent;
  let mockSettings: Partial<SettingsAdminService>;
  let mockToast: Partial<ToastService>;

  const profile = { id: 't1', name: 'Acme', slug: 'acme', contactEmail: 'a@b.com', contactPhone: '555' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockSettings = {
      getProfile: vi.fn().mockReturnValue(of(profile)),
      updateProfile: vi.fn().mockReturnValue(of(profile)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantProfileComponent, ReactiveFormsModule],
      providers: [{ provide: SettingsAdminService, useValue: mockSettings }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantProfileComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads and patches the profile form', () => {
    expect(component.form.value.name).toBe('Acme');
  });

  it('saves the updated profile', () => {
    component.form.patchValue({ name: 'Acme Updated' });
    component.onSubmit();
    expect(mockSettings.updateProfile).toHaveBeenCalledWith(expect.objectContaining({ name: 'Acme Updated' }));
    expect(mockToast.success).toHaveBeenCalled();
  });
});
```

```typescript
// tenant-profile.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { SettingsAdminService } from '../services/settings-admin.service';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-tenant-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './tenant-profile.component.html',
})
export class TenantProfileComponent implements OnInit {
  form = this.fb.group({
    name: this.fb.nonNullable.control('', Validators.required),
    contactEmail: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    contactPhone: this.fb.nonNullable.control('', Validators.required),
  });

  constructor(private fb: FormBuilder, private settings: SettingsAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.settings.getProfile().subscribe((profile) => {
      this.form.setValue({ name: profile.name, contactEmail: profile.contactEmail, contactPhone: profile.contactPhone });
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.settings.updateProfile(this.form.getRawValue()).subscribe({
      next: () => this.toast.success('Profile updated.'),
      error: () => this.toast.error('Failed to update profile.'),
    });
  }
}
```

`tenant-profile.component.html`:

```html
<h1 class="h4 mb-3">Tenant profile</h1>
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div class="mb-3">
    <label for="name" class="form-label">Store name</label>
    <input id="name" class="form-control" formControlName="name" />
  </div>
  <div class="mb-3">
    <label for="contactEmail" class="form-label">Contact email</label>
    <input id="contactEmail" type="email" class="form-control" formControlName="contactEmail" />
  </div>
  <div class="mb-3">
    <label for="contactPhone" class="form-label">Contact phone</label>
    <input id="contactPhone" class="form-control" formControlName="contactPhone" />
  </div>
  <button type="submit" class="btn btn-primary">Save</button>
</form>
```

**Step 10.7 — RED/GREEN: `tenant-users.component`** (list + create + assign-role):

```typescript
// tenant-users.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { TenantUsersComponent } from './tenant-users.component';
import { SettingsAdminService } from '../services/settings-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('TenantUsersComponent', () => {
  let fixture: ComponentFixture<TenantUsersComponent>;
  let component: TenantUsersComponent;
  let mockSettings: Partial<SettingsAdminService>;
  let mockToast: Partial<ToastService>;

  const user = { id: 'u1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: ['StoreManager'], isActive: true };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockSettings = {
      getUsers: vi.fn().mockReturnValue(of([user])),
      createUser: vi.fn().mockReturnValue(of(user)),
      assignRole: vi.fn().mockReturnValue(of({ ...user, roles: ['InventoryManager'] })),
      deleteUser: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantUsersComponent, FormsModule],
      providers: [{ provide: SettingsAdminService, useValue: mockSettings }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantUsersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads tenant users on init', () => {
    expect(component.users.length).toBe(1);
  });

  it('creates a new user', () => {
    component.newUser = { email: 'c@d.com', firstName: 'C', lastName: 'D', roles: ['OrderManager'] };
    component.onCreate();
    expect(mockSettings.createUser).toHaveBeenCalledWith(component.newUser);
  });

  it('assigns a role to an existing user', () => {
    component.onAssignRole(user as any, 'InventoryManager');
    expect(mockSettings.assignRole).toHaveBeenCalledWith('u1', 'InventoryManager');
  });

  it('deletes a user', () => {
    component.onDelete(user as any);
    expect(mockSettings.deleteUser).toHaveBeenCalledWith('u1');
  });
});
```

```typescript
// tenant-users.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsAdminService } from '../services/settings-admin.service';
import { TenantUserDto, CreateTenantUserRequest } from '../models/settings-admin.model';
import { ToastService } from '../../shared/services/toast.service';

const ASSIGNABLE_ROLES = ['AdminOwner', 'StoreManager', 'InventoryManager', 'OrderManager', 'ContentManager'];

@Component({
  selector: 'app-tenant-users',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './tenant-users.component.html',
})
export class TenantUsersComponent implements OnInit {
  users: TenantUserDto[] = [];
  roles = ASSIGNABLE_ROLES;
  newUser: CreateTenantUserRequest = { email: '', firstName: '', lastName: '', roles: [] };

  constructor(private settings: SettingsAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.settings.getUsers().subscribe((users) => (this.users = users));
  }

  onCreate(): void {
    this.settings.createUser(this.newUser).subscribe({
      next: () => {
        this.toast.success('User created.');
        this.newUser = { email: '', firstName: '', lastName: '', roles: [] };
        this.load();
      },
      error: () => this.toast.error('Failed to create user.'),
    });
  }

  onAssignRole(user: TenantUserDto, role: string): void {
    this.settings.assignRole(user.id, role).subscribe({
      next: () => { this.toast.success('Role updated.'); this.load(); },
      error: () => this.toast.error('Failed to update role.'),
    });
  }

  onDelete(user: TenantUserDto): void {
    this.settings.deleteUser(user.id).subscribe({
      next: () => { this.toast.success('User removed.'); this.load(); },
      error: () => this.toast.error('Failed to remove user.'),
    });
  }
}
```

`tenant-users.component.html`:

```html
<h1 class="h4 mb-3">Team members</h1>
<table class="table table-sm">
  <thead><tr><th>Email</th><th>Name</th><th>Roles</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let u of users">
      <td>{{ u.email }}</td><td>{{ u.firstName }} {{ u.lastName }}</td><td>{{ u.roles.join(', ') }}</td>
      <td>
        <select class="form-select form-select-sm d-inline-block w-auto" (change)="onAssignRole(u, $any($event.target).value)">
          <option value="" disabled selected>Assign role</option>
          <option *ngFor="let r of roles" [value]="r">{{ r }}</option>
        </select>
        <button class="btn btn-sm btn-outline-danger" (click)="onDelete(u)">Remove</button>
      </td>
    </tr>
  </tbody>
</table>

<h2 class="h6">Invite a user</h2>
<div class="row g-2 align-items-end">
  <div class="col-auto"><input class="form-control form-control-sm" placeholder="Email" [(ngModel)]="newUser.email" name="email" /></div>
  <div class="col-auto"><input class="form-control form-control-sm" placeholder="First name" [(ngModel)]="newUser.firstName" name="firstName" /></div>
  <div class="col-auto"><input class="form-control form-control-sm" placeholder="Last name" [(ngModel)]="newUser.lastName" name="lastName" /></div>
  <div class="col-auto"><button class="btn btn-sm btn-primary" (click)="onCreate()">Invite</button></div>
</div>
```

**Step 10.8 — RED/GREEN: `tenant-subscription.component`.**

```typescript
// tenant-subscription.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TenantSubscriptionComponent } from './tenant-subscription.component';
import { SettingsAdminService } from '../services/settings-admin.service';

describe('TenantSubscriptionComponent', () => {
  let fixture: ComponentFixture<TenantSubscriptionComponent>;
  let component: TenantSubscriptionComponent;
  let mockSettings: Partial<SettingsAdminService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockSettings = {
      getSubscription: vi.fn().mockReturnValue(of({ id: 's1', planName: 'Pro', status: 'active', currentPeriodEnd: '2026-08-01' })),
      getPayments: vi.fn().mockReturnValue(of([{ id: 'pay1', amount: 99, status: 'paid', paidAt: '2026-07-01' }])),
    };

    await TestBed.configureTestingModule({
      imports: [TenantSubscriptionComponent],
      providers: [{ provide: SettingsAdminService, useValue: mockSettings }],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantSubscriptionComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the subscription and payment history', () => {
    expect(component.subscription?.planName).toBe('Pro');
    expect(component.payments.length).toBe(1);
  });
});
```

```typescript
// tenant-subscription.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SettingsAdminService } from '../services/settings-admin.service';
import { TenantSubscriptionDto, TenantPaymentDto } from '../models/settings-admin.model';

@Component({
  selector: 'app-tenant-subscription',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './tenant-subscription.component.html',
})
export class TenantSubscriptionComponent implements OnInit {
  subscription: TenantSubscriptionDto | null = null;
  payments: TenantPaymentDto[] = [];

  constructor(private settings: SettingsAdminService) {}

  ngOnInit(): void {
    this.settings.getSubscription().subscribe((s) => (this.subscription = s));
    this.settings.getPayments().subscribe((p) => (this.payments = p));
  }
}
```

`tenant-subscription.component.html`:

```html
<h1 class="h4 mb-3">Subscription</h1>
<div class="card mb-3" *ngIf="subscription as s"><div class="card-body">
  <p><strong>Plan:</strong> {{ s.planName }}</p>
  <p><strong>Status:</strong> {{ s.status }}</p>
  <p><strong>Renews:</strong> {{ s.currentPeriodEnd | date:'mediumDate' }}</p>
</div></div>
<h2 class="h6">Payment history</h2>
<table class="table table-sm">
  <thead><tr><th>Date</th><th>Amount</th><th>Status</th></tr></thead>
  <tbody>
    <tr *ngFor="let p of payments"><td>{{ p.paidAt | date:'mediumDate' }}</td><td>{{ p.amount | number:'1.2-2' }}</td><td>{{ p.status }}</td></tr>
  </tbody>
</table>
```

**Step 10.9 — RED/GREEN: `tenant-bank-account.component`** (masked view + TOTP-gated full reveal):

```typescript
// tenant-bank-account.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { TenantBankAccountComponent } from './tenant-bank-account.component';
import { SettingsAdminService } from '../services/settings-admin.service';
import { ToastService } from '../../shared/services/toast.service';

describe('TenantBankAccountComponent', () => {
  let fixture: ComponentFixture<TenantBankAccountComponent>;
  let component: TenantBankAccountComponent;
  let mockSettings: Partial<SettingsAdminService>;
  let mockToast: Partial<ToastService>;

  const masked = { id: 'b1', accountHolderName: 'Acme', maskedAccountNumber: '****1234', bankName: 'Chase' };
  const full = { ...masked, accountNumber: '00001234', routingNumber: '021000021' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockSettings = {
      getBankAccount: vi.fn().mockReturnValue(of(masked)),
      getBankAccountFull: vi.fn().mockReturnValue(of(full)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantBankAccountComponent, FormsModule],
      providers: [{ provide: SettingsAdminService, useValue: mockSettings }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantBankAccountComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('shows the masked account by default', () => {
    expect(component.masked?.maskedAccountNumber).toBe('****1234');
    expect(component.full).toBeNull();
  });

  it('reveals the full account with a valid TOTP code', () => {
    component.totpCode = '123456';
    component.onReveal();
    expect(mockSettings.getBankAccountFull).toHaveBeenCalledWith('123456');
    expect(component.full?.accountNumber).toBe('00001234');
  });

  it('shows an error toast for an invalid TOTP code', () => {
    (mockSettings.getBankAccountFull as ReturnType<typeof vi.fn>).mockReturnValue(throwError(() => new Error('invalid')));
    component.totpCode = '000000';
    component.onReveal();
    expect(mockToast.error).toHaveBeenCalled();
    expect(component.full).toBeNull();
  });
});
```

```typescript
// tenant-bank-account.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SettingsAdminService } from '../services/settings-admin.service';
import { BankAccountMaskedDto, BankAccountFullDto } from '../models/settings-admin.model';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-tenant-bank-account',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './tenant-bank-account.component.html',
})
export class TenantBankAccountComponent implements OnInit {
  masked: BankAccountMaskedDto | null = null;
  full: BankAccountFullDto | null = null;
  totpCode = '';

  constructor(private settings: SettingsAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.settings.getBankAccount().subscribe((account) => (this.masked = account));
  }

  onReveal(): void {
    this.settings.getBankAccountFull(this.totpCode).subscribe({
      next: (full) => { this.full = full; this.totpCode = ''; },
      error: () => this.toast.error('Invalid or expired verification code.'),
    });
  }
}
```

`tenant-bank-account.component.html`:

```html
<h1 class="h4 mb-3">Bank account</h1>
<div class="card" *ngIf="masked as m"><div class="card-body">
  <p><strong>Holder:</strong> {{ m.accountHolderName }}</p>
  <p><strong>Bank:</strong> {{ m.bankName }}</p>
  <p><strong>Account:</strong> {{ full ? full.accountNumber : m.maskedAccountNumber }}</p>
  <p *ngIf="full"><strong>Routing:</strong> {{ full.routingNumber }}</p>

  <div *ngIf="!full" class="d-flex align-items-end gap-2">
    <div>
      <label for="totp" class="form-label small mb-0">Authenticator code</label>
      <input id="totp" class="form-control form-control-sm" maxlength="6" [(ngModel)]="totpCode" name="totpCode" />
    </div>
    <button class="btn btn-sm btn-outline-primary" (click)="onReveal()">Reveal full number</button>
  </div>
</div></div>
```

**Step 10.10 — `settings.routes.ts`** (`adminOwnerGuard` on the root, per Global Constraint on route-level guarding style from Task 1/2):

```typescript
import { Routes } from '@angular/router';
import { adminOwnerGuard } from './settings-guard/admin-owner.guard';

export const settingsRoutes: Routes = [
  {
    path: '',
    canActivate: [adminOwnerGuard],
    children: [
      { path: '', redirectTo: 'profile', pathMatch: 'full' },
      { path: 'profile', loadComponent: () => import('./profile/tenant-profile.component').then((m) => m.TenantProfileComponent) },
      { path: 'users', loadComponent: () => import('./users/tenant-users.component').then((m) => m.TenantUsersComponent) },
      { path: 'subscription', loadComponent: () => import('./subscription/tenant-subscription.component').then((m) => m.TenantSubscriptionComponent) },
      { path: 'bank-account', loadComponent: () => import('./bank-account/tenant-bank-account.component').then((m) => m.TenantBankAccountComponent) },
    ],
  },
];
```

### Verification

```
npm run test:ci -- --run report-page.component.spec settings-admin.service.spec admin-owner.guard.spec tenant-profile.component.spec tenant-users.component.spec tenant-subscription.component.spec tenant-bank-account.component.spec
npm run test:ci
npm run test:ci
```

---

## Task 11 — Platform console + hardening

### Files
- Create: `fashionsaas-storefront/src/app/admin/platform/models/platform.model.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/services/platform-admin.service.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/services/platform-admin.service.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/home/platform-home.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/home/platform-home.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/home/platform-home.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenants.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-list/tenant-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-list/tenant-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-list/tenant-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-detail/tenant-detail.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-detail/tenant-detail.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-detail/tenant-detail.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-form/tenant-form.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-form/tenant-form.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/tenants/tenant-form/tenant-form.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/plans/plans.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/plans/plan-list/plan-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/plans/plan-list/plan-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/plans/plan-list/plan-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/subscriptions/subscriptions.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/subscriptions/subscription-list/subscription-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/subscriptions/subscription-list/subscription-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/subscriptions/subscription-list/subscription-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/payments/payments.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/payments/payment-list/payment-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/payments/payment-list/payment-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/payments/payment-list/payment-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/users/platform-users.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/users/platform-user-list/platform-user-list.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/users/platform-user-list/platform-user-list.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/users/platform-user-list/platform-user-list.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/security.routes.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/audit-logs/audit-logs.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/audit-logs/audit-logs.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/audit-logs/audit-logs.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/login-attempts/login-attempts.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/login-attempts/login-attempts.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/login-attempts/login-attempts.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/mfa-setup/mfa-setup.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/mfa-setup/mfa-setup.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/mfa-setup/mfa-setup.component.spec.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/bank-account/platform-bank-account.component.ts`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/bank-account/platform-bank-account.component.html`
- Create: `fashionsaas-storefront/src/app/admin/platform/security/bank-account/platform-bank-account.component.spec.ts`
- Edit: `fashionsaas-storefront/README.md` (route table)
- Edit: `docs/PROJECT_PROGRESS.md` (Phase 4b section)

### Interfaces

**Consumes** (backend, `ApiUrl.cs`): all `Admin*` groups — `AdminTenants.{GetAll,GetById,Create,Update,Suspend,Activate,Delete}`, `AdminUsers.{GetAll,GetById,Create,Update,Delete,Unlock}`, `AdminSubscriptionPlans.{GetAll,GetById,Create,Update,Delete}`, `AdminSubscriptions.{GetAll,GetById,Assign,ChangePlan,Suspend,Reactivate}`, `AdminPayments.{GetAll,GetById,Confirm}`, `AdminBankAccount.{Get,GetFull,Create,Update}`, `AdminAuditLogs.{GetAll,GetById}`, `AdminLoginAttempts.GetAll`, `AdminMfa.{Setup,VerifySetup,BackupCodes,RegenerateBackupCodes}`. All under `superAdminGuard` (Task 1, verbatim). Task 3 kit throughout. Task 10's `ConfirmModalComponent.requireTypedConfirmation` (Task 3, verbatim) used for tenant delete.

**Produces:**

```typescript
// platform/models/platform.model.ts
export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  status: 'active' | 'suspended';
  createdAt: string;
}

export interface CreateTenantRequest {
  name: string;
  slug: string;
  ownerEmail: string;
}

export interface PlatformUserDto {
  id: string;
  email: string;
  roles: string[];
  isLocked: boolean;
}

export interface SubscriptionPlanDto {
  id: string;
  name: string;
  price: number;
  billingCycle: string;
}

export interface CreatePlanRequest {
  name: string;
  price: number;
  billingCycle: string;
}

export interface PlatformSubscriptionDto {
  id: string;
  tenantId: string;
  tenantName: string;
  planId: string;
  planName: string;
  status: string;
}

export interface PlatformPaymentDto {
  id: string;
  subscriptionId: string;
  amount: number;
  status: string;
  createdAt: string;
}

export interface AuditLogDto {
  id: string;
  userId: string;
  action: string;
  ip: string;
  createdAt: string;
}

export interface LoginAttemptDto {
  id: string;
  email: string;
  success: boolean;
  ip: string;
  createdAt: string;
}

export interface MfaSetupResponse {
  qrCodeDataUrl: string;
  secret: string;
}

export interface PlatformBankAccountMaskedDto {
  id: string;
  accountHolderName: string;
  maskedAccountNumber: string;
  bankName: string;
}
```

`PlatformAdminService` (all thin `ApiService` wrappers, mirrors `CatalogAdminService`'s `unwrap` pattern from Task 7):

```typescript
getTenants(page: number, pageSize: number): Observable<PagedResult<TenantDto>>;
getTenant(id: string): Observable<TenantDto>;
createTenant(req: CreateTenantRequest): Observable<TenantDto>;
updateTenant(id: string, req: Partial<CreateTenantRequest>): Observable<TenantDto>;
suspendTenant(id: string): Observable<TenantDto>;
activateTenant(id: string): Observable<TenantDto>;
deleteTenant(id: string): Observable<void>;

getPlatformUsers(): Observable<PlatformUserDto[]>;
unlockPlatformUser(id: string): Observable<PlatformUserDto>;

getPlans(): Observable<SubscriptionPlanDto[]>;
createPlan(req: CreatePlanRequest): Observable<SubscriptionPlanDto>;
updatePlan(id: string, req: CreatePlanRequest): Observable<SubscriptionPlanDto>;
deletePlan(id: string): Observable<void>;

getSubscriptions(): Observable<PlatformSubscriptionDto[]>;
assignSubscription(tenantId: string, planId: string): Observable<PlatformSubscriptionDto>;
changeSubscriptionPlan(id: string, planId: string): Observable<PlatformSubscriptionDto>;
suspendSubscription(id: string): Observable<PlatformSubscriptionDto>;
reactivateSubscription(id: string): Observable<PlatformSubscriptionDto>;

getPayments(subscriptionId?: string): Observable<PlatformPaymentDto[]>;
confirmPayment(id: string): Observable<PlatformPaymentDto>;

getAuditLogs(filter: { userId?: string; from?: string; to?: string }): Observable<AuditLogDto[]>;
getLoginAttempts(filter: { email?: string; from?: string; to?: string }): Observable<LoginAttemptDto[]>;

setupMfa(): Observable<MfaSetupResponse>;
verifyMfaSetup(code: string): Observable<void>;

getPlatformBankAccount(): Observable<PlatformBankAccountMaskedDto>;
```

### TDD steps

**Step 11.1 — RED/GREEN: `platform-admin.service.spec.ts` / `.ts`** (one representative test per endpoint; identical shape to Task 7's `catalog-admin.service` — terse):

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '@env/environment';
import { ApiService } from '../../../core/services/api.service';
import { PlatformAdminService } from './platform-admin.service';

describe('PlatformAdminService', () => {
  let service: PlatformAdminService;
  let httpMock: HttpTestingController;
  const base = environment.apiBaseUrl;
  const wrap = <T>(data: T) => ({ statusCode: 200, message: 'ok', data, errors: null, timestamp: '' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule], providers: [PlatformAdminService, ApiService] });
    service = TestBed.inject(PlatformAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets a paged tenant list', () => {
    service.getTenants(1, 20).subscribe();
    httpMock.expectOne((r) => r.url === `${base}/admin/tenants`).flush(wrap({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 }));
  });

  it('creates a tenant', () => {
    service.createTenant({ name: 'Acme', slug: 'acme', ownerEmail: 'a@b.com' }).subscribe();
    const req = httpMock.expectOne(`${base}/admin/tenants`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('suspends a tenant', () => {
    service.suspendTenant('t1').subscribe();
    httpMock.expectOne(`${base}/admin/tenants/t1/suspend`).flush(wrap({}));
  });

  it('activates a tenant', () => {
    service.activateTenant('t1').subscribe();
    httpMock.expectOne(`${base}/admin/tenants/t1/activate`).flush(wrap({}));
  });

  it('deletes a tenant', () => {
    service.deleteTenant('t1').subscribe();
    const req = httpMock.expectOne(`${base}/admin/tenants/t1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets platform users', () => {
    service.getPlatformUsers().subscribe();
    httpMock.expectOne(`${base}/admin/users`).flush(wrap([]));
  });

  it('unlocks a platform user', () => {
    service.unlockPlatformUser('u1').subscribe();
    httpMock.expectOne(`${base}/admin/users/u1/unlock`).flush(wrap({}));
  });

  it('gets subscription plans', () => {
    service.getPlans().subscribe();
    httpMock.expectOne(`${base}/admin/subscription-plans`).flush(wrap([]));
  });

  it('creates a plan', () => {
    service.createPlan({ name: 'Pro', price: 99, billingCycle: 'monthly' }).subscribe();
    const req = httpMock.expectOne(`${base}/admin/subscription-plans`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({}));
  });

  it('deletes a plan', () => {
    service.deletePlan('p1').subscribe();
    const req = httpMock.expectOne(`${base}/admin/subscription-plans/p1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(wrap(null));
  });

  it('gets subscriptions', () => {
    service.getSubscriptions().subscribe();
    httpMock.expectOne(`${base}/admin/subscriptions`).flush(wrap([]));
  });

  it('assigns a subscription to a tenant', () => {
    service.assignSubscription('t1', 'p1').subscribe();
    const req = httpMock.expectOne(`${base}/admin/subscriptions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ tenantId: 't1', planId: 'p1' });
    req.flush(wrap({}));
  });

  it('changes a subscription plan', () => {
    service.changeSubscriptionPlan('s1', 'p2').subscribe();
    const req = httpMock.expectOne(`${base}/admin/subscriptions/s1/change-plan`);
    expect(req.request.body).toEqual({ planId: 'p2' });
    req.flush(wrap({}));
  });

  it('suspends a subscription', () => {
    service.suspendSubscription('s1').subscribe();
    httpMock.expectOne(`${base}/admin/subscriptions/s1/suspend`).flush(wrap({}));
  });

  it('reactivates a subscription', () => {
    service.reactivateSubscription('s1').subscribe();
    httpMock.expectOne(`${base}/admin/subscriptions/s1/reactivate`).flush(wrap({}));
  });

  it('gets payments, optionally scoped to a subscription', () => {
    service.getPayments('s1').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/admin/payments` && r.params.get('subscriptionId') === 's1');
    req.flush(wrap([]));
  });

  it('confirms a payment', () => {
    service.confirmPayment('pay1').subscribe();
    httpMock.expectOne(`${base}/admin/payments/pay1/confirm`).flush(wrap({}));
  });

  it('gets audit logs with filters', () => {
    service.getAuditLogs({ userId: 'u1' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/admin/audit-logs` && r.params.get('userId') === 'u1');
    req.flush(wrap([]));
  });

  it('gets login attempts with filters', () => {
    service.getLoginAttempts({ email: 'a@b.com' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/admin/login-attempts` && r.params.get('email') === 'a@b.com');
    req.flush(wrap([]));
  });

  it('sets up MFA', () => {
    service.setupMfa().subscribe();
    const req = httpMock.expectOne(`${base}/admin/mfa/setup`);
    expect(req.request.method).toBe('POST');
    req.flush(wrap({ qrCodeDataUrl: 'data:...', secret: 'ABC' }));
  });

  it('verifies MFA setup with a code', () => {
    service.verifyMfaSetup('123456').subscribe();
    const req = httpMock.expectOne(`${base}/admin/mfa/verify-setup`);
    expect(req.request.body).toEqual({ code: '123456' });
    req.flush(wrap(null));
  });

  it('gets the platform bank account', () => {
    service.getPlatformBankAccount().subscribe();
    httpMock.expectOne(`${base}/admin/bank-account`).flush(wrap({}));
  });
});
```

```typescript
// platform-admin.service.ts
import { Injectable } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ApiService } from '../../../core/services/api.service';
import { ApiResponse, PagedResult } from '../../../core/models/api-response.model';
import {
  TenantDto, CreateTenantRequest, PlatformUserDto, SubscriptionPlanDto, CreatePlanRequest,
  PlatformSubscriptionDto, PlatformPaymentDto, AuditLogDto, LoginAttemptDto, MfaSetupResponse,
  PlatformBankAccountMaskedDto,
} from '../models/platform.model';

@Injectable({ providedIn: 'root' })
export class PlatformAdminService {
  constructor(private apiService: ApiService) {}

  private unwrap<T>(obs: Observable<ApiResponse<T>>): Observable<T> {
    return obs.pipe(map((r) => r.data));
  }

  getTenants(page: number, pageSize: number): Observable<PagedResult<TenantDto>> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.unwrap(this.apiService.get<PagedResult<TenantDto>>('admin/tenants', params));
  }

  getTenant(id: string): Observable<TenantDto> {
    return this.unwrap(this.apiService.get<TenantDto>(`admin/tenants/${id}`));
  }

  createTenant(req: CreateTenantRequest): Observable<TenantDto> {
    return this.unwrap(this.apiService.post<TenantDto>('admin/tenants', req));
  }

  updateTenant(id: string, req: Partial<CreateTenantRequest>): Observable<TenantDto> {
    return this.unwrap(this.apiService.put<TenantDto>(`admin/tenants/${id}`, req));
  }

  suspendTenant(id: string): Observable<TenantDto> {
    return this.unwrap(this.apiService.put<TenantDto>(`admin/tenants/${id}/suspend`, {}));
  }

  activateTenant(id: string): Observable<TenantDto> {
    return this.unwrap(this.apiService.put<TenantDto>(`admin/tenants/${id}/activate`, {}));
  }

  deleteTenant(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`admin/tenants/${id}`));
  }

  getPlatformUsers(): Observable<PlatformUserDto[]> {
    return this.unwrap(this.apiService.get<PlatformUserDto[]>('admin/users'));
  }

  unlockPlatformUser(id: string): Observable<PlatformUserDto> {
    return this.unwrap(this.apiService.put<PlatformUserDto>(`admin/users/${id}/unlock`, {}));
  }

  getPlans(): Observable<SubscriptionPlanDto[]> {
    return this.unwrap(this.apiService.get<SubscriptionPlanDto[]>('admin/subscription-plans'));
  }

  createPlan(req: CreatePlanRequest): Observable<SubscriptionPlanDto> {
    return this.unwrap(this.apiService.post<SubscriptionPlanDto>('admin/subscription-plans', req));
  }

  updatePlan(id: string, req: CreatePlanRequest): Observable<SubscriptionPlanDto> {
    return this.unwrap(this.apiService.put<SubscriptionPlanDto>(`admin/subscription-plans/${id}`, req));
  }

  deletePlan(id: string): Observable<void> {
    return this.unwrap(this.apiService.delete<void>(`admin/subscription-plans/${id}`));
  }

  getSubscriptions(): Observable<PlatformSubscriptionDto[]> {
    return this.unwrap(this.apiService.get<PlatformSubscriptionDto[]>('admin/subscriptions'));
  }

  assignSubscription(tenantId: string, planId: string): Observable<PlatformSubscriptionDto> {
    return this.unwrap(this.apiService.post<PlatformSubscriptionDto>('admin/subscriptions', { tenantId, planId }));
  }

  changeSubscriptionPlan(id: string, planId: string): Observable<PlatformSubscriptionDto> {
    return this.unwrap(this.apiService.put<PlatformSubscriptionDto>(`admin/subscriptions/${id}/change-plan`, { planId }));
  }

  suspendSubscription(id: string): Observable<PlatformSubscriptionDto> {
    return this.unwrap(this.apiService.put<PlatformSubscriptionDto>(`admin/subscriptions/${id}/suspend`, {}));
  }

  reactivateSubscription(id: string): Observable<PlatformSubscriptionDto> {
    return this.unwrap(this.apiService.put<PlatformSubscriptionDto>(`admin/subscriptions/${id}/reactivate`, {}));
  }

  getPayments(subscriptionId?: string): Observable<PlatformPaymentDto[]> {
    const params = subscriptionId ? new HttpParams().set('subscriptionId', subscriptionId) : undefined;
    return this.unwrap(this.apiService.get<PlatformPaymentDto[]>('admin/payments', params));
  }

  confirmPayment(id: string): Observable<PlatformPaymentDto> {
    return this.unwrap(this.apiService.put<PlatformPaymentDto>(`admin/payments/${id}/confirm`, {}));
  }

  getAuditLogs(filter: { userId?: string; from?: string; to?: string }): Observable<AuditLogDto[]> {
    let params = new HttpParams();
    if (filter.userId) params = params.set('userId', filter.userId);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    return this.unwrap(this.apiService.get<AuditLogDto[]>('admin/audit-logs', params));
  }

  getLoginAttempts(filter: { email?: string; from?: string; to?: string }): Observable<LoginAttemptDto[]> {
    let params = new HttpParams();
    if (filter.email) params = params.set('email', filter.email);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    return this.unwrap(this.apiService.get<LoginAttemptDto[]>('admin/login-attempts', params));
  }

  setupMfa(): Observable<MfaSetupResponse> {
    return this.unwrap(this.apiService.post<MfaSetupResponse>('admin/mfa/setup', {}));
  }

  verifyMfaSetup(code: string): Observable<void> {
    return this.unwrap(this.apiService.post<void>('admin/mfa/verify-setup', { code }));
  }

  getPlatformBankAccount(): Observable<PlatformBankAccountMaskedDto> {
    return this.unwrap(this.apiService.get<PlatformBankAccountMaskedDto>('admin/bank-account'));
  }
}
```

**Step 11.2 — RED/GREEN: `platform-home.component`** (counts assembled client-side from three list calls — no dedicated summary endpoint exists per the backend contract, confirmed against `ApiUrl.cs`):

```typescript
// platform-home.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PlatformHomeComponent } from './platform-home.component';
import { PlatformAdminService } from '../services/platform-admin.service';

describe('PlatformHomeComponent', () => {
  let fixture: ComponentFixture<PlatformHomeComponent>;
  let component: PlatformHomeComponent;
  let mockPlatform: Partial<PlatformAdminService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getTenants: vi.fn().mockReturnValue(of({ items: [], totalCount: 12, pageNumber: 1, pageSize: 1, totalPages: 12 })),
      getSubscriptions: vi.fn().mockReturnValue(of([{ id: 's1', tenantId: 't1', tenantName: 'A', planId: 'p1', planName: 'Pro', status: 'active' }])),
      getPlatformUsers: vi.fn().mockReturnValue(of([{ id: 'u1', email: 'x@y.com', roles: ['SuperAdmin'], isLocked: false }])),
    };

    await TestBed.configureTestingModule({
      imports: [PlatformHomeComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }],
    }).compileComponents();
    fixture = TestBed.createComponent(PlatformHomeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('assembles tenant, subscription, and user counts client-side', () => {
    expect(component.tenantCount).toBe(12);
    expect(component.activeSubscriptionCount).toBe(1);
    expect(component.platformUserCount).toBe(1);
  });
});
```

```typescript
// platform-home.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { PlatformAdminService } from '../services/platform-admin.service';
import { KpiCardComponent } from '../../shared/components/kpi-card/kpi-card.component';

@Component({
  selector: 'app-platform-home',
  standalone: true,
  imports: [CommonModule, KpiCardComponent],
  templateUrl: './platform-home.component.html',
})
export class PlatformHomeComponent implements OnInit {
  tenantCount = 0;
  activeSubscriptionCount = 0;
  platformUserCount = 0;

  constructor(private platform: PlatformAdminService) {}

  ngOnInit(): void {
    forkJoin({
      tenants: this.platform.getTenants(1, 1),
      subscriptions: this.platform.getSubscriptions(),
      users: this.platform.getPlatformUsers(),
    }).subscribe(({ tenants, subscriptions, users }) => {
      this.tenantCount = tenants.totalCount;
      this.activeSubscriptionCount = subscriptions.filter((s) => s.status === 'active').length;
      this.platformUserCount = users.length;
    });
  }
}
```

`platform-home.component.html`:

```html
<h1 class="h4 mb-3">Platform overview</h1>
<div class="row g-3">
  <div class="col-sm-4"><app-kpi-card label="Tenants" [value]="tenantCount" icon="building"></app-kpi-card></div>
  <div class="col-sm-4"><app-kpi-card label="Active subscriptions" [value]="activeSubscriptionCount" icon="receipt"></app-kpi-card></div>
  <div class="col-sm-4"><app-kpi-card label="Platform users" [value]="platformUserCount" icon="people-fill"></app-kpi-card></div>
</div>
```

**Step 11.3 — RED/GREEN: `tenant-list.component`** (list + suspend/activate; delete routed through detail's typed-confirm):

```typescript
// tenant-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { TenantListComponent } from './tenant-list.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('TenantListComponent', () => {
  let fixture: ComponentFixture<TenantListComponent>;
  let component: TenantListComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const tenant = { id: 't1', name: 'Acme', slug: 'acme', status: 'active' as const, createdAt: '2026-01-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getTenants: vi.fn().mockReturnValue(of({ items: [tenant], totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1 })),
      suspendTenant: vi.fn().mockReturnValue(of({ ...tenant, status: 'suspended' })),
      activateTenant: vi.fn().mockReturnValue(of(tenant)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantListComponent],
      providers: [provideRouter([]), { provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads tenants on init', () => {
    expect(component.rows.length).toBe(1);
  });

  it('suspends a tenant', () => {
    component.onSuspend(tenant as any);
    expect(mockPlatform.suspendTenant).toHaveBeenCalledWith('t1');
  });

  it('activates a tenant', () => {
    component.onActivate(tenant as any);
    expect(mockPlatform.activateTenant).toHaveBeenCalledWith('t1');
  });
});
```

```typescript
// tenant-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { TenantDto } from '../../models/platform.model';
import { DataTableComponent, DataTableColumn } from '../../../shared/components/data-table/data-table.component';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-tenant-list',
  standalone: true,
  imports: [CommonModule, RouterModule, DataTableComponent],
  templateUrl: './tenant-list.component.html',
})
export class TenantListComponent implements OnInit {
  columns: DataTableColumn<TenantDto>[] = [
    { key: 'name', header: 'Name' }, { key: 'slug', header: 'Slug' },
    { key: 'status', header: 'Status' }, { key: 'createdAt', header: 'Created', cellTemplate: 'date' },
  ];
  rows: TenantDto[] = [];
  totalCount = 0;
  pageNumber = 1;
  pageSize = 20;
  loading = false;

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  onPageChange(page: number): void {
    this.pageNumber = page;
    this.load();
  }

  onSuspend(tenant: TenantDto): void {
    this.platform.suspendTenant(tenant.id).subscribe({
      next: () => { this.toast.success('Tenant suspended.'); this.load(); },
      error: () => this.toast.error('Failed to suspend tenant.'),
    });
  }

  onActivate(tenant: TenantDto): void {
    this.platform.activateTenant(tenant.id).subscribe({
      next: () => { this.toast.success('Tenant activated.'); this.load(); },
      error: () => this.toast.error('Failed to activate tenant.'),
    });
  }

  private load(): void {
    this.loading = true;
    this.platform.getTenants(this.pageNumber, this.pageSize).subscribe((result) => {
      this.rows = result.items;
      this.totalCount = result.totalCount;
      this.loading = false;
    });
  }
}
```

`tenant-list.component.html`:

```html
<div class="d-flex justify-content-between align-items-center mb-3">
  <h1 class="h4 mb-0">Tenants</h1>
  <a routerLink="new" class="btn btn-primary btn-sm"><i class="bi bi-plus-lg"></i> New tenant</a>
</div>
<app-data-table [columns]="columns" [rows]="rows" [totalCount]="totalCount" [pageNumber]="pageNumber"
  [pageSize]="pageSize" [sortKey]="null" sortDirection="asc" [loading]="loading"
  emptyMessage="No tenants found." (pageChange)="onPageChange($event)">
</app-data-table>
<table class="table table-sm mt-2">
  <tbody>
    <tr *ngFor="let t of rows">
      <td>{{ t.name }}</td>
      <td>
        <a [routerLink]="[t.id]" class="btn btn-sm btn-outline-secondary">Manage</a>
        <button class="btn btn-sm btn-outline-warning" *ngIf="t.status === 'active'" (click)="onSuspend(t)">Suspend</button>
        <button class="btn btn-sm btn-outline-success" *ngIf="t.status === 'suspended'" (click)="onActivate(t)">Activate</button>
      </td>
    </tr>
  </tbody>
</table>
```

**Step 11.4 — RED/GREEN: `tenant-detail.component`** (delete with typed confirmation via Task 3's `ConfirmModalComponent.requireTypedConfirmation`):

```typescript
// tenant-detail.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { TenantDetailComponent } from './tenant-detail.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('TenantDetailComponent', () => {
  let fixture: ComponentFixture<TenantDetailComponent>;
  let component: TenantDetailComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const tenant = { id: 't1', name: 'Acme', slug: 'acme', status: 'active' as const, createdAt: '2026-01-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getTenant: vi.fn().mockReturnValue(of(tenant)),
      deleteTenant: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantDetailComponent],
      providers: [
        provideRouter([]),
        { provide: PlatformAdminService, useValue: mockPlatform },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 't1' } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the tenant', () => {
    expect(component.tenant?.name).toBe('Acme');
  });

  it('requires the typed tenant name before deletion is confirmable', () => {
    component.openDeleteModal();
    expect(component.deleteModalOpen).toBe(true);
    expect(component.requireTypedConfirmation).toBe('acme');
  });

  it('deletes the tenant and navigates back to the list on confirm', () => {
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.onDeleteConfirmed();
    expect(mockPlatform.deleteTenant).toHaveBeenCalledWith('t1');
    expect(navSpy).toHaveBeenCalledWith(['/admin/platform/tenants']);
  });
});
```

```typescript
// tenant-detail.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { TenantDto } from '../../models/platform.model';
import { ConfirmModalComponent } from '../../../shared/components/confirm-modal/confirm-modal.component';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-tenant-detail',
  standalone: true,
  imports: [CommonModule, ConfirmModalComponent],
  templateUrl: './tenant-detail.component.html',
})
export class TenantDetailComponent implements OnInit {
  tenant: TenantDto | null = null;
  deleteModalOpen = false;
  requireTypedConfirmation = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private platform: PlatformAdminService,
    private toast: ToastService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.platform.getTenant(id).subscribe((tenant) => {
      this.tenant = tenant;
      this.requireTypedConfirmation = tenant.slug;
    });
  }

  openDeleteModal(): void {
    this.deleteModalOpen = true;
  }

  onDeleteCancelled(): void {
    this.deleteModalOpen = false;
  }

  onDeleteConfirmed(): void {
    if (!this.tenant) return;
    this.platform.deleteTenant(this.tenant.id).subscribe({
      next: () => {
        this.toast.success('Tenant deleted.');
        this.router.navigate(['/admin/platform/tenants']);
      },
      error: () => { this.toast.error('Failed to delete tenant.'); this.deleteModalOpen = false; },
    });
  }
}
```

`tenant-detail.component.html`:

```html
<ng-container *ngIf="tenant as t">
  <div class="d-flex justify-content-between align-items-center mb-3">
    <h1 class="h4 mb-0">{{ t.name }}</h1>
    <button class="btn btn-outline-danger btn-sm" (click)="openDeleteModal()">Delete tenant</button>
  </div>
  <p><strong>Slug:</strong> {{ t.slug }}</p>
  <p><strong>Status:</strong> {{ t.status }}</p>

  <app-confirm-modal
    [isOpen]="deleteModalOpen"
    title="Delete tenant"
    [message]="'This permanently deletes ' + t.name + ' and all its data.'"
    confirmLabel="Delete tenant"
    tone="danger"
    [requireTypedConfirmation]="requireTypedConfirmation"
    (confirmed)="onDeleteConfirmed()"
    (cancelled)="onDeleteCancelled()">
  </app-confirm-modal>
</ng-container>
```

**Step 11.5 — RED/GREEN: `tenant-form.component`** (create + update, reused route):

```typescript
// tenant-form.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { TenantFormComponent } from './tenant-form.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('TenantFormComponent', () => {
  let fixture: ComponentFixture<TenantFormComponent>;
  let component: TenantFormComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = { createTenant: vi.fn().mockReturnValue(of({ id: 't2' })) };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [TenantFormComponent],
      providers: [
        provideRouter([]),
        { provide: PlatformAdminService, useValue: mockPlatform },
        { provide: ToastService, useValue: mockToast },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => null } } } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TenantFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates a tenant and navigates to the list', () => {
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    component.form.setValue({ name: 'New Co', slug: 'new-co', ownerEmail: 'owner@newco.com' });
    component.onSubmit();
    expect(mockPlatform.createTenant).toHaveBeenCalledWith({ name: 'New Co', slug: 'new-co', ownerEmail: 'owner@newco.com' });
    expect(navSpy).toHaveBeenCalledWith(['/admin/platform/tenants']);
  });

  it('does not submit an invalid form', () => {
    component.form.patchValue({ name: '' });
    component.onSubmit();
    expect(mockPlatform.createTenant).not.toHaveBeenCalled();
  });
});
```

```typescript
// tenant-form.component.ts
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-tenant-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './tenant-form.component.html',
})
export class TenantFormComponent {
  form = this.fb.group({
    name: this.fb.nonNullable.control('', Validators.required),
    slug: this.fb.nonNullable.control('', Validators.required),
    ownerEmail: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
  });

  constructor(
    private fb: FormBuilder,
    private router: Router,
    private platform: PlatformAdminService,
    private toast: ToastService
  ) {}

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.platform.createTenant(this.form.getRawValue()).subscribe({
      next: () => {
        this.toast.success('Tenant created.');
        this.router.navigate(['/admin/platform/tenants']);
      },
      error: () => this.toast.error('Failed to create tenant.'),
    });
  }
}
```

`tenant-form.component.html`:

```html
<h1 class="h4 mb-3">New tenant</h1>
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div class="mb-3"><label for="name" class="form-label">Name</label><input id="name" class="form-control" formControlName="name" /></div>
  <div class="mb-3"><label for="slug" class="form-label">Slug</label><input id="slug" class="form-control" formControlName="slug" /></div>
  <div class="mb-3"><label for="ownerEmail" class="form-label">Owner email</label><input id="ownerEmail" type="email" class="form-control" formControlName="ownerEmail" /></div>
  <button type="submit" class="btn btn-primary">Create</button>
  <a routerLink="/admin/platform/tenants" class="btn btn-link">Cancel</a>
</form>
```

**Step 11.6 — `tenants.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const tenantsRoutes: Routes = [
  { path: '', loadComponent: () => import('./tenant-list/tenant-list.component').then((m) => m.TenantListComponent) },
  { path: 'new', loadComponent: () => import('./tenant-form/tenant-form.component').then((m) => m.TenantFormComponent) },
  { path: ':id', loadComponent: () => import('./tenant-detail/tenant-detail.component').then((m) => m.TenantDetailComponent) },
];
```

**Step 11.7 — RED/GREEN: `plan-list.component`** (CRUD, terse — same shape as Task 9's `discount-list`):

```typescript
// plan-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { PlanListComponent } from './plan-list.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('PlanListComponent', () => {
  let fixture: ComponentFixture<PlanListComponent>;
  let component: PlanListComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const plan = { id: 'p1', name: 'Pro', price: 99, billingCycle: 'monthly' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getPlans: vi.fn().mockReturnValue(of([plan])),
      createPlan: vi.fn().mockReturnValue(of(plan)),
      deletePlan: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [PlanListComponent, FormsModule],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(PlanListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads plans on init', () => {
    expect(component.plans.length).toBe(1);
  });

  it('creates a plan', () => {
    component.newPlan = { name: 'Basic', price: 29, billingCycle: 'monthly' };
    component.onCreate();
    expect(mockPlatform.createPlan).toHaveBeenCalledWith(component.newPlan);
  });

  it('deletes a plan', () => {
    component.onDelete(plan as any);
    expect(mockPlatform.deletePlan).toHaveBeenCalledWith('p1');
  });
});
```

```typescript
// plan-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { SubscriptionPlanDto, CreatePlanRequest } from '../../models/platform.model';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-plan-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './plan-list.component.html',
})
export class PlanListComponent implements OnInit {
  plans: SubscriptionPlanDto[] = [];
  newPlan: CreatePlanRequest = { name: '', price: 0, billingCycle: 'monthly' };

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.platform.getPlans().subscribe((plans) => (this.plans = plans));
  }

  onCreate(): void {
    this.platform.createPlan(this.newPlan).subscribe({
      next: () => {
        this.toast.success('Plan created.');
        this.newPlan = { name: '', price: 0, billingCycle: 'monthly' };
        this.load();
      },
      error: () => this.toast.error('Failed to create plan.'),
    });
  }

  onDelete(plan: SubscriptionPlanDto): void {
    this.platform.deletePlan(plan.id).subscribe({
      next: () => { this.toast.success('Plan deleted.'); this.load(); },
      error: () => this.toast.error('Failed to delete plan.'),
    });
  }
}
```

`plan-list.component.html`:

```html
<h1 class="h4 mb-3">Subscription plans</h1>
<table class="table table-sm">
  <tbody>
    <tr *ngFor="let p of plans">
      <td>{{ p.name }}</td><td>{{ p.price | number:'1.2-2' }}</td><td>{{ p.billingCycle }}</td>
      <td><button class="btn btn-sm btn-outline-danger" (click)="onDelete(p)">Delete</button></td>
    </tr>
  </tbody>
</table>
<h2 class="h6">New plan</h2>
<div class="row g-2 align-items-end">
  <div class="col-auto"><input class="form-control form-control-sm" placeholder="Name" [(ngModel)]="newPlan.name" name="name" /></div>
  <div class="col-auto"><input type="number" step="0.01" class="form-control form-control-sm" placeholder="Price" [(ngModel)]="newPlan.price" name="price" /></div>
  <div class="col-auto">
    <select class="form-select form-select-sm" [(ngModel)]="newPlan.billingCycle" name="billingCycle">
      <option value="monthly">Monthly</option>
      <option value="annual">Annual</option>
    </select>
  </div>
  <div class="col-auto"><button class="btn btn-sm btn-primary" (click)="onCreate()">Add plan</button></div>
</div>
```

**Step 11.8 — `plans.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const plansRoutes: Routes = [
  { path: '', loadComponent: () => import('./plan-list/plan-list.component').then((m) => m.PlanListComponent) },
];
```

**Step 11.9 — RED/GREEN: `subscription-list.component`** (assign/change-plan/suspend/reactivate):

```typescript
// subscription-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { SubscriptionListComponent } from './subscription-list.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('SubscriptionListComponent', () => {
  let fixture: ComponentFixture<SubscriptionListComponent>;
  let component: SubscriptionListComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const sub = { id: 's1', tenantId: 't1', tenantName: 'Acme', planId: 'p1', planName: 'Pro', status: 'active' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getSubscriptions: vi.fn().mockReturnValue(of([sub])),
      getPlans: vi.fn().mockReturnValue(of([{ id: 'p2', name: 'Enterprise', price: 199, billingCycle: 'monthly' }])),
      changeSubscriptionPlan: vi.fn().mockReturnValue(of({ ...sub, planId: 'p2' })),
      suspendSubscription: vi.fn().mockReturnValue(of({ ...sub, status: 'suspended' })),
      reactivateSubscription: vi.fn().mockReturnValue(of(sub)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [SubscriptionListComponent, FormsModule],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(SubscriptionListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads subscriptions and plans on init', () => {
    expect(component.subscriptions.length).toBe(1);
    expect(component.plans.length).toBe(1);
  });

  it('changes a subscription plan', () => {
    component.onChangePlan(sub as any, 'p2');
    expect(mockPlatform.changeSubscriptionPlan).toHaveBeenCalledWith('s1', 'p2');
  });

  it('suspends a subscription', () => {
    component.onSuspend(sub as any);
    expect(mockPlatform.suspendSubscription).toHaveBeenCalledWith('s1');
  });

  it('reactivates a subscription', () => {
    component.onReactivate(sub as any);
    expect(mockPlatform.reactivateSubscription).toHaveBeenCalledWith('s1');
  });
});
```

```typescript
// subscription-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { PlatformSubscriptionDto, SubscriptionPlanDto } from '../../models/platform.model';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-subscription-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './subscription-list.component.html',
})
export class SubscriptionListComponent implements OnInit {
  subscriptions: PlatformSubscriptionDto[] = [];
  plans: SubscriptionPlanDto[] = [];

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.platform.getSubscriptions().subscribe((subs) => (this.subscriptions = subs));
    this.platform.getPlans().subscribe((plans) => (this.plans = plans));
  }

  onChangePlan(sub: PlatformSubscriptionDto, planId: string): void {
    this.platform.changeSubscriptionPlan(sub.id, planId).subscribe({
      next: () => { this.toast.success('Plan changed.'); this.reload(); },
      error: () => this.toast.error('Failed to change plan.'),
    });
  }

  onSuspend(sub: PlatformSubscriptionDto): void {
    this.platform.suspendSubscription(sub.id).subscribe({
      next: () => { this.toast.success('Subscription suspended.'); this.reload(); },
      error: () => this.toast.error('Failed to suspend subscription.'),
    });
  }

  onReactivate(sub: PlatformSubscriptionDto): void {
    this.platform.reactivateSubscription(sub.id).subscribe({
      next: () => { this.toast.success('Subscription reactivated.'); this.reload(); },
      error: () => this.toast.error('Failed to reactivate subscription.'),
    });
  }

  private reload(): void {
    this.platform.getSubscriptions().subscribe((subs) => (this.subscriptions = subs));
  }
}
```

`subscription-list.component.html`:

```html
<h1 class="h4 mb-3">Subscriptions</h1>
<table class="table table-sm">
  <thead><tr><th>Tenant</th><th>Plan</th><th>Status</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let s of subscriptions">
      <td>{{ s.tenantName }}</td>
      <td>
        <select class="form-select form-select-sm d-inline-block w-auto" [ngModel]="s.planId" (ngModelChange)="onChangePlan(s, $event)">
          <option [value]="s.planId">{{ s.planName }}</option>
          <option *ngFor="let p of plans" [value]="p.id">{{ p.name }}</option>
        </select>
      </td>
      <td>{{ s.status }}</td>
      <td>
        <button class="btn btn-sm btn-outline-warning" *ngIf="s.status === 'active'" (click)="onSuspend(s)">Suspend</button>
        <button class="btn btn-sm btn-outline-success" *ngIf="s.status === 'suspended'" (click)="onReactivate(s)">Reactivate</button>
      </td>
    </tr>
  </tbody>
</table>
```

**Step 11.10 — `subscriptions.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const subscriptionsRoutes: Routes = [
  { path: '', loadComponent: () => import('./subscription-list/subscription-list.component').then((m) => m.SubscriptionListComponent) },
];
```

**Step 11.11 — RED/GREEN: `payment-list.component`** (list by subscription + confirm):

```typescript
// payment-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PaymentListComponent } from './payment-list.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('PaymentListComponent', () => {
  let fixture: ComponentFixture<PaymentListComponent>;
  let component: PaymentListComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const payment = { id: 'pay1', subscriptionId: 's1', amount: 99, status: 'pending', createdAt: '2026-07-01' };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getPayments: vi.fn().mockReturnValue(of([payment])),
      confirmPayment: vi.fn().mockReturnValue(of({ ...payment, status: 'confirmed' })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [PaymentListComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(PaymentListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads all payments on init', () => {
    expect(mockPlatform.getPayments).toHaveBeenCalledWith(undefined);
    expect(component.payments.length).toBe(1);
  });

  it('filters by subscription id', () => {
    (mockPlatform.getPayments as ReturnType<typeof vi.fn>).mockClear();
    component.onSubscriptionFilterChange('s1');
    expect(mockPlatform.getPayments).toHaveBeenCalledWith('s1');
  });

  it('confirms a payment', () => {
    component.onConfirm(payment as any);
    expect(mockPlatform.confirmPayment).toHaveBeenCalledWith('pay1');
  });
});
```

```typescript
// payment-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { PlatformPaymentDto } from '../../models/platform.model';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-payment-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './payment-list.component.html',
})
export class PaymentListComponent implements OnInit {
  payments: PlatformPaymentDto[] = [];
  subscriptionFilter = '';

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  onSubscriptionFilterChange(subscriptionId: string): void {
    this.subscriptionFilter = subscriptionId;
    this.load();
  }

  onConfirm(payment: PlatformPaymentDto): void {
    this.platform.confirmPayment(payment.id).subscribe({
      next: () => { this.toast.success('Payment confirmed.'); this.load(); },
      error: () => this.toast.error('Failed to confirm payment.'),
    });
  }

  private load(): void {
    this.platform.getPayments(this.subscriptionFilter || undefined).subscribe((payments) => (this.payments = payments));
  }
}
```

`payment-list.component.html`:

```html
<h1 class="h4 mb-3">Payments</h1>
<input class="form-control form-control-sm mb-3 w-auto" placeholder="Filter by subscription ID"
       (change)="onSubscriptionFilterChange($any($event.target).value)" />
<table class="table table-sm">
  <thead><tr><th>Date</th><th>Amount</th><th>Status</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let p of payments">
      <td>{{ p.createdAt | date:'mediumDate' }}</td><td>{{ p.amount | number:'1.2-2' }}</td><td>{{ p.status }}</td>
      <td><button class="btn btn-sm btn-outline-success" *ngIf="p.status === 'pending'" (click)="onConfirm(p)">Confirm</button></td>
    </tr>
  </tbody>
</table>
```

**Step 11.12 — `payments.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const paymentsRoutes: Routes = [
  { path: '', loadComponent: () => import('./payment-list/payment-list.component').then((m) => m.PaymentListComponent) },
];
```

**Step 11.13 — RED/GREEN: `platform-user-list.component`** (list + unlock):

```typescript
// platform-user-list.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PlatformUserListComponent } from './platform-user-list.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('PlatformUserListComponent', () => {
  let fixture: ComponentFixture<PlatformUserListComponent>;
  let component: PlatformUserListComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  const user = { id: 'u1', email: 'x@y.com', roles: ['SuperAdmin'], isLocked: true };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getPlatformUsers: vi.fn().mockReturnValue(of([user])),
      unlockPlatformUser: vi.fn().mockReturnValue(of({ ...user, isLocked: false })),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [PlatformUserListComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(PlatformUserListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads platform users on init', () => {
    expect(component.users.length).toBe(1);
  });

  it('unlocks a locked user', () => {
    component.onUnlock(user as any);
    expect(mockPlatform.unlockPlatformUser).toHaveBeenCalledWith('u1');
  });
});
```

```typescript
// platform-user-list.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { PlatformUserDto } from '../../models/platform.model';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-platform-user-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './platform-user-list.component.html',
})
export class PlatformUserListComponent implements OnInit {
  users: PlatformUserDto[] = [];

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.load();
  }

  onUnlock(user: PlatformUserDto): void {
    this.platform.unlockPlatformUser(user.id).subscribe({
      next: () => { this.toast.success('User unlocked.'); this.load(); },
      error: () => this.toast.error('Failed to unlock user.'),
    });
  }

  private load(): void {
    this.platform.getPlatformUsers().subscribe((users) => (this.users = users));
  }
}
```

`platform-user-list.component.html`:

```html
<h1 class="h4 mb-3">Platform users</h1>
<table class="table table-sm">
  <thead><tr><th>Email</th><th>Roles</th><th>Status</th><th></th></tr></thead>
  <tbody>
    <tr *ngFor="let u of users">
      <td>{{ u.email }}</td><td>{{ u.roles.join(', ') }}</td><td>{{ u.isLocked ? 'Locked' : 'Active' }}</td>
      <td><button class="btn btn-sm btn-outline-warning" *ngIf="u.isLocked" (click)="onUnlock(u)">Unlock</button></td>
    </tr>
  </tbody>
</table>
```

**Step 11.14 — `platform-users.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const platformUsersRoutes: Routes = [
  { path: '', loadComponent: () => import('./platform-user-list/platform-user-list.component').then((m) => m.PlatformUserListComponent) },
];
```

**Step 11.15 — RED/GREEN: `audit-logs.component` / `login-attempts.component`** (filtered tables, same shape — shown once, second mirrors it):

```typescript
// audit-logs.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { AuditLogsComponent } from './audit-logs.component';
import { PlatformAdminService } from '../../services/platform-admin.service';

describe('AuditLogsComponent', () => {
  let fixture: ComponentFixture<AuditLogsComponent>;
  let component: AuditLogsComponent;
  let mockPlatform: Partial<PlatformAdminService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = { getAuditLogs: vi.fn().mockReturnValue(of([{ id: 'a1', userId: 'u1', action: 'login', ip: '1.1.1.1', createdAt: '2026-07-01' }])) };

    await TestBed.configureTestingModule({
      imports: [AuditLogsComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }],
    }).compileComponents();
    fixture = TestBed.createComponent(AuditLogsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads audit logs with an empty filter by default', () => {
    expect(mockPlatform.getAuditLogs).toHaveBeenCalledWith({});
    expect(component.logs.length).toBe(1);
  });

  it('re-queries when the range filter changes', () => {
    (mockPlatform.getAuditLogs as ReturnType<typeof vi.fn>).mockClear();
    component.onRangeChange({ from: '2026-06-01', to: '2026-07-01' });
    expect(mockPlatform.getAuditLogs).toHaveBeenCalledWith({ from: '2026-06-01', to: '2026-07-01' });
  });
});
```

```typescript
// audit-logs.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { AuditLogDto } from '../../models/platform.model';
import { DateRangePickerComponent, DateRange } from '../../../shared/components/date-range-picker/date-range-picker.component';

@Component({
  selector: 'app-audit-logs',
  standalone: true,
  imports: [CommonModule, DateRangePickerComponent],
  templateUrl: './audit-logs.component.html',
})
export class AuditLogsComponent implements OnInit {
  logs: AuditLogDto[] = [];
  range: DateRange | null = null;

  constructor(private platform: PlatformAdminService) {}

  ngOnInit(): void {
    this.load();
  }

  onRangeChange(range: DateRange): void {
    this.range = range;
    this.load();
  }

  private load(): void {
    const filter = this.range ? { from: this.range.from, to: this.range.to } : {};
    this.platform.getAuditLogs(filter).subscribe((logs) => (this.logs = logs));
  }
}
```

`audit-logs.component.html`:

```html
<h1 class="h4 mb-3">Audit logs</h1>
<app-date-range-picker *ngIf="range" [range]="range" (rangeChange)="onRangeChange($event)"></app-date-range-picker>
<table class="table table-sm">
  <thead><tr><th>Date</th><th>User</th><th>Action</th><th>IP</th></tr></thead>
  <tbody>
    <tr *ngFor="let l of logs"><td>{{ l.createdAt | date:'medium' }}</td><td>{{ l.userId }}</td><td>{{ l.action }}</td><td>{{ l.ip }}</td></tr>
  </tbody>
</table>
```

```typescript
// login-attempts.component.spec.ts — mirrors audit-logs.component.spec.ts with getLoginAttempts and an email filter
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LoginAttemptsComponent } from './login-attempts.component';
import { PlatformAdminService } from '../../services/platform-admin.service';

describe('LoginAttemptsComponent', () => {
  let fixture: ComponentFixture<LoginAttemptsComponent>;
  let component: LoginAttemptsComponent;
  let mockPlatform: Partial<PlatformAdminService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = { getLoginAttempts: vi.fn().mockReturnValue(of([{ id: 'l1', email: 'a@b.com', success: false, ip: '1.1.1.1', createdAt: '2026-07-01' }])) };

    await TestBed.configureTestingModule({
      imports: [LoginAttemptsComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }],
    }).compileComponents();
    fixture = TestBed.createComponent(LoginAttemptsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads login attempts on init', () => {
    expect(mockPlatform.getLoginAttempts).toHaveBeenCalledWith({});
    expect(component.attempts.length).toBe(1);
  });

  it('filters by email', () => {
    (mockPlatform.getLoginAttempts as ReturnType<typeof vi.fn>).mockClear();
    component.onEmailFilterChange('a@b.com');
    expect(mockPlatform.getLoginAttempts).toHaveBeenCalledWith({ email: 'a@b.com' });
  });
});
```

```typescript
// login-attempts.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { LoginAttemptDto } from '../../models/platform.model';

@Component({
  selector: 'app-login-attempts',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './login-attempts.component.html',
})
export class LoginAttemptsComponent implements OnInit {
  attempts: LoginAttemptDto[] = [];
  emailFilter = '';

  constructor(private platform: PlatformAdminService) {}

  ngOnInit(): void {
    this.load();
  }

  onEmailFilterChange(email: string): void {
    this.emailFilter = email;
    this.load();
  }

  private load(): void {
    const filter = this.emailFilter ? { email: this.emailFilter } : {};
    this.platform.getLoginAttempts(filter).subscribe((attempts) => (this.attempts = attempts));
  }
}
```

`login-attempts.component.html`:

```html
<h1 class="h4 mb-3">Login attempts</h1>
<input class="form-control form-control-sm mb-3 w-auto" placeholder="Filter by email"
       (change)="onEmailFilterChange($any($event.target).value)" />
<table class="table table-sm">
  <thead><tr><th>Date</th><th>Email</th><th>Success</th><th>IP</th></tr></thead>
  <tbody>
    <tr *ngFor="let a of attempts"><td>{{ a.createdAt | date:'medium' }}</td><td>{{ a.email }}</td><td>{{ a.success ? 'Yes' : 'No' }}</td><td>{{ a.ip }}</td></tr>
  </tbody>
</table>
```

**Step 11.16 — RED/GREEN: `mfa-setup.component`** (QR flow via `admin/mfa/setup` + `admin/mfa/verify-setup`):

```typescript
// mfa-setup.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { MfaSetupComponent } from './mfa-setup.component';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('MfaSetupComponent', () => {
  let fixture: ComponentFixture<MfaSetupComponent>;
  let component: MfaSetupComponent;
  let mockPlatform: Partial<PlatformAdminService>;
  let mockToast: Partial<ToastService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      setupMfa: vi.fn().mockReturnValue(of({ qrCodeDataUrl: 'data:image/png;base64,abc', secret: 'SECRET' })),
      verifyMfaSetup: vi.fn().mockReturnValue(of(undefined)),
    };
    mockToast = { success: vi.fn(), error: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [MfaSetupComponent, FormsModule],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }, { provide: ToastService, useValue: mockToast }],
    }).compileComponents();
    fixture = TestBed.createComponent(MfaSetupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('fetches the QR code and secret on init', () => {
    expect(component.qrCodeDataUrl).toBe('data:image/png;base64,abc');
    expect(component.secret).toBe('SECRET');
  });

  it('verifies the setup code and shows success', () => {
    component.verificationCode = '123456';
    component.onVerify();
    expect(mockPlatform.verifyMfaSetup).toHaveBeenCalledWith('123456');
    expect(component.verified).toBe(true);
  });

  it('shows an error on an invalid code', () => {
    (mockPlatform.verifyMfaSetup as ReturnType<typeof vi.fn>).mockReturnValue(throwError(() => new Error('invalid')));
    component.verificationCode = '000000';
    component.onVerify();
    expect(mockToast.error).toHaveBeenCalled();
    expect(component.verified).toBe(false);
  });
});
```

```typescript
// mfa-setup.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-mfa-setup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './mfa-setup.component.html',
})
export class MfaSetupComponent implements OnInit {
  qrCodeDataUrl = '';
  secret = '';
  verificationCode = '';
  verified = false;

  constructor(private platform: PlatformAdminService, private toast: ToastService) {}

  ngOnInit(): void {
    this.platform.setupMfa().subscribe((response) => {
      this.qrCodeDataUrl = response.qrCodeDataUrl;
      this.secret = response.secret;
    });
  }

  onVerify(): void {
    this.platform.verifyMfaSetup(this.verificationCode).subscribe({
      next: () => { this.verified = true; this.toast.success('MFA enabled.'); },
      error: () => { this.verified = false; this.toast.error('Invalid verification code.'); },
    });
  }
}
```

`mfa-setup.component.html`:

```html
<h1 class="h4 mb-3">Two-factor authentication setup</h1>
<div *ngIf="!verified">
  <img [src]="qrCodeDataUrl" alt="MFA QR code" *ngIf="qrCodeDataUrl" style="max-width:200px" />
  <p class="text-muted small">Or enter this key manually: <code>{{ secret }}</code></p>
  <div class="d-flex align-items-end gap-2">
    <div>
      <label for="mfa-verify-code" class="form-label small mb-0">Verification code</label>
      <input id="mfa-verify-code" class="form-control form-control-sm" maxlength="6" [(ngModel)]="verificationCode" name="verificationCode" />
    </div>
    <button class="btn btn-sm btn-primary" (click)="onVerify()">Verify and enable</button>
  </div>
</div>
<div *ngIf="verified" class="alert alert-success">Two-factor authentication is now enabled.</div>
```

**Step 11.17 — RED/GREEN: `platform-bank-account.component`** (masked view, mirrors Task 10's tenant version at the platform endpoint):

```typescript
// platform-bank-account.component.spec.ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PlatformBankAccountComponent } from './platform-bank-account.component';
import { PlatformAdminService } from '../../services/platform-admin.service';

describe('PlatformBankAccountComponent', () => {
  let fixture: ComponentFixture<PlatformBankAccountComponent>;
  let component: PlatformBankAccountComponent;
  let mockPlatform: Partial<PlatformAdminService>;

  beforeEach(async () => {
    TestBed.resetTestingModule();
    mockPlatform = {
      getPlatformBankAccount: vi.fn().mockReturnValue(of({ id: 'b1', accountHolderName: 'Platform Inc', maskedAccountNumber: '****9999', bankName: 'Wells Fargo' })),
    };

    await TestBed.configureTestingModule({
      imports: [PlatformBankAccountComponent],
      providers: [{ provide: PlatformAdminService, useValue: mockPlatform }],
    }).compileComponents();
    fixture = TestBed.createComponent(PlatformBankAccountComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the masked platform bank account', () => {
    expect(component.account?.maskedAccountNumber).toBe('****9999');
  });
});
```

```typescript
// platform-bank-account.component.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlatformAdminService } from '../../services/platform-admin.service';
import { PlatformBankAccountMaskedDto } from '../../models/platform.model';

@Component({
  selector: 'app-platform-bank-account',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './platform-bank-account.component.html',
})
export class PlatformBankAccountComponent implements OnInit {
  account: PlatformBankAccountMaskedDto | null = null;

  constructor(private platform: PlatformAdminService) {}

  ngOnInit(): void {
    this.platform.getPlatformBankAccount().subscribe((account) => (this.account = account));
  }
}
```

`platform-bank-account.component.html`:

```html
<h1 class="h4 mb-3">Platform bank account</h1>
<div class="card" *ngIf="account as a"><div class="card-body">
  <p><strong>Holder:</strong> {{ a.accountHolderName }}</p>
  <p><strong>Bank:</strong> {{ a.bankName }}</p>
  <p><strong>Account:</strong> {{ a.maskedAccountNumber }}</p>
</div></div>
```

**Step 11.18 — `security.routes.ts`.**

```typescript
import { Routes } from '@angular/router';

export const securityRoutes: Routes = [
  { path: '', redirectTo: 'audit-logs', pathMatch: 'full' },
  { path: 'audit-logs', loadComponent: () => import('./audit-logs/audit-logs.component').then((m) => m.AuditLogsComponent) },
  { path: 'login-attempts', loadComponent: () => import('./login-attempts/login-attempts.component').then((m) => m.LoginAttemptsComponent) },
  { path: 'mfa-setup', loadComponent: () => import('./mfa-setup/mfa-setup.component').then((m) => m.MfaSetupComponent) },
  { path: 'bank-account', loadComponent: () => import('./bank-account/platform-bank-account.component').then((m) => m.PlatformBankAccountComponent) },
];
```

**Step 11.19 — wire the sub-route tables into `platform.routes.ts`.** (Task 2 already created `platform.routes.ts` with `loadChildren` pointers to `./tenants/tenants.routes`, `./plans/plans.routes`, `./subscriptions/subscriptions.routes`, `./payments/payments.routes`, `./users/platform-users.routes`, `./security/security.routes` — those imports now resolve because Steps 11.6/11.8/11.10/11.12/11.14/11.18 created the target files. No edit to `platform.routes.ts` itself is needed; this step is verification-only: run `npm run test:ci -- --run platform.routes.spec` to confirm the routes Task 2 already asserted on now actually load without a missing-module error at build time (`ng build` in Step 11.21 is the authoritative check).

**Step 11.20 — final hardening: bundle budget verification.**

```
npm run build -- --configuration production
```

Read the build output's initial-chunk size line; assert it is ≤ 600 kB (matches `angular.json`'s existing `maximumWarning: 600kB` budget — Global Constraint #6). Record the reported size of the lazy `admin` chunk (and its `ng2-charts`/`chart.js` sub-chunk) in the task's completion report for future reference; there is no enforced ceiling on the admin chunk itself, only the initial chunk.

**Step 11.21 — final hardening: prod environment grep.**

```
grep -n "localhost\|/api/v1" fashionsaas-storefront/src/environments/environment.prod.ts
```

Expect no matches (Task 4 already removed both `localhost` and `/v1`; this is the final confirmation gate before sign-off, re-run here because Task 4 ran before Tasks 5–10 existed and could in principle have been touched again by a later task's environment-dependent test scaffolding — it was not, per each task's Files list above, but the grep is the actual evidence, not the assumption).

**Step 11.22 — final hardening: full suite run twice.**

```
npm run test:ci
npm run test:ci
```

Confirm identical pass counts and no flaky diffs between the two runs (Global Constraint #9, final gate for the whole plan — supersedes the per-task ×2 runs, which remain in each task above as early-exit gates).

**Step 11.23 — documentation updates.**

Edit `fashionsaas-storefront/README.md`: add an "Admin area routes" table (mirrors any existing route-table section if present, or appended as a new section) listing every top-level `/admin/**` path added across Tasks 2–11 with its guard (`adminRoleGuard`/`superAdminGuard`/`adminOwnerGuard`) and required role(s), e.g.:

```markdown
## Admin area routes

| Path | Guard | Roles |
| --- | --- | --- |
| /admin | adminRoleGuard | AdminOwner, StoreManager, InventoryManager, OrderManager, ContentManager, SuperAdmin |
| /admin/orders | adminRoleGuard | AdminOwner, OrderManager, StoreManager |
| /admin/catalog | adminRoleGuard | AdminOwner, StoreManager, ContentManager |
| /admin/inventory | adminRoleGuard | AdminOwner, InventoryManager |
| /admin/customers | adminRoleGuard | AdminOwner, StoreManager |
| /admin/discounts | adminRoleGuard | AdminOwner, StoreManager |
| /admin/reviews | adminRoleGuard | AdminOwner, StoreManager |
| /admin/reports | adminRoleGuard | AdminOwner, StoreManager |
| /admin/settings | adminRoleGuard + adminOwnerGuard | AdminOwner |
| /admin/platform | adminRoleGuard + superAdminGuard | SuperAdmin |
```

Edit `docs/PROJECT_PROGRESS.md`: add a "Phase 4b: Role-Routed Admin Area" section following the existing phase-entry format used for Phase 1/2/3 (status: COMPLETE, test count from the final `npm run test:ci` run in Step 11.22, brief bullet list of the 8 tenant modules + platform console, and a note that zero new backend endpoints were required).

### Verification

```
npm run test:ci -- --run platform-admin.service.spec platform-home.component.spec tenant-list.component.spec tenant-detail.component.spec tenant-form.component.spec plan-list.component.spec subscription-list.component.spec payment-list.component.spec platform-user-list.component.spec audit-logs.component.spec login-attempts.component.spec mfa-setup.component.spec platform-bank-account.component.spec
npm run test:ci
npm run test:ci
npm run build -- --configuration production
```

---

## Execution Notes

- Tasks execute strictly sequentially, 1 → 11. Task 4 is a hard blocker for Tasks 5–11: every later task's service consumes either `OrderAdminService`/`ReportApiService`/the fixed `environment.apiBaseUrl` (Task 4's own products) or a same-shaped sibling service built on the same `ApiService`-wrapping pattern Task 4 establishes. Do not start Task 5 until Task 4's verification block is green.
- Every task (1–11) ends its own TDD steps with the "suite green ×2" gate (`npm run test:ci` run twice, identical output) before the next task starts — this is Global Constraint #9 applied per-task, not just at the end.
- The production bundle-budget check (`ng build --configuration production`, Global Constraint #6) is verified exactly once, at the very end of Task 11 (Step 11.20) — not after every task — because it is only meaningful once the full `/admin` lazy chunk (including Task 5's `ng2-charts`) exists to measure against the initial-chunk budget.
- Tasks 6–10 (Orders, Catalog, Inventory+Customers, Discounts+Reviews, Reports+Settings) are independent of each other in principle (each owns a disjoint route subtree and service) but are still executed in listed order per the plan's "sequential, one task at a time" execution model — this is a process choice for review clarity, not a technical dependency beyond all of them requiring Task 4.
- Task 11's platform console (tenants/plans/subscriptions/payments/users/security) depends only on Task 1 (`superAdminGuard`), Task 2 (`platform.routes.ts` scaffold, already wired to the exact `loadChildren` paths Task 11 fills in), and Task 3 (shared kit) — not on Tasks 5–10's tenant-side modules — but remains last so the final hardening pass (bundle check, env grep, doc updates) has the complete app to verify against.
