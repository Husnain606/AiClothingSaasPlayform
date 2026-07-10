# Phase 5 — Storefront: "Try It On" on the Product Detail Page (Buildable Plan)

> **STATUS — not started (2026-07-11).**

## Reference

- Master plan: [`../MASTER.md`](../MASTER.md) — locked decisions D11, D12.
- **Dependency (consumed, not redefined):** [`PHASE-3-GEMINI-ENDPOINT.md`](PHASE-3-GEMINI-ENDPOINT.md) — the finalized `POST /api/tryon` contract: multipart form (`photo` file, `garmentImageUrl` string, `productId` guid, `productVariantId?` guid), response `ResponseData<TryOnResultResponse>` where `TryOnResultResponse = { resultImageDataUri: string }` (camelCase on the wire — confirm the TryOn Api project's JSON serialization uses the default camelCase policy; ASP.NET Core's default `System.Text.Json` output is camelCase, matching the main API's existing `ApiResponse<T>` casing convention already consumed by the storefront).

### Contract checklist (confirm against landed code before editing)

- [ ] `fashionsaas-storefront/src/app/core/interceptors/auth.interceptor.ts:9-17` — attaches `Authorization: Bearer <token>` to **every** outgoing `HttpClient` request regardless of target origin (no URL filtering) — confirms no extra auth wiring is needed for calls to the TryOn service's different base URL.
- [ ] `fashionsaas-storefront/src/environments/environment.ts` (lines 1-5) and `environment.prod.ts` (lines 1-5) — current shape `{ production: boolean, apiBaseUrl: string, tenantSlug: string }`, extended here with `tryOnApiBaseUrl`.
- [ ] `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.ts:18-37` — current field/constructor shape, extended here with try-on state.
- [ ] `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.html:201-213` — the `action-buttons` block, used as the insertion anchor for the new "Try It On" section (inserted immediately after this block, before the "Product Tags" section at line 215).

### Locked decisions in force

- **D11** — nothing about the photo or result is ever written to any Angular service field beyond the current component instance's lifetime; no `localStorage`/`sessionStorage`, no caching service (unlike `ProductService`'s deliberate `productCache` — this is the one place in the codebase that must **not** follow that caching pattern).
- **D12** — the whole feature lives inside `ProductDetailComponent`; no new route, no new page.

## 1. Ordered task checklist

### Group A — Environment config

- [ ] **A1** Add `tryOnApiBaseUrl` to both environment files (§2 code samples).
- [ ] **A2** Commit:

```bash
git add fashionsaas-storefront/src/environments
git commit -m "feat(storefront): add tryOnApiBaseUrl environment config"
```

### Group B — `TryOnService` (Angular)

- [ ] **B1** Create the model and service files (§2 code samples).
- [ ] **B2** Write the failing tests (§3 exact test list, `try-on.service.spec.ts`).
- [ ] **B3** Run: `npm run test:ci -- try-on.service.spec.ts` (from `fashionsaas-storefront`) — expect FAIL (`TryOnService` doesn't exist).
- [ ] **B4** Implement `TryOnService` for real, run again — expect PASS.
- [ ] **B5** Commit:

```bash
cd fashionsaas-storefront
git add src/app/features/catalog/models/try-on.model.ts src/app/features/catalog/services/try-on.service.ts src/app/features/catalog/services/try-on.service.spec.ts
git commit -m "feat(storefront): TryOnService — POST multipart to the try-on microservice"
```

### Group C — `ProductDetailComponent` "Try It On" section

- [ ] **C1** Write the failing tests for the new component methods (§3 exact test list — additions to the existing `product-detail.component.spec.ts`).
- [ ] **C2** Run: `npm run test:ci -- product-detail.component.spec.ts` — expect FAIL (methods don't exist).
- [ ] **C3** Implement the component changes (§2 code sample — modifies `product-detail.component.ts`).
- [ ] **C4** Implement the template changes (§2 code sample — modifies `product-detail.component.html`).
- [ ] **C5** Run the same tests — expect PASS.
- [ ] **C6** Commit:

```bash
git add src/app/features/catalog/components/product-detail
git commit -m "feat(storefront): Try It On section on product detail page"
```

### Group D — Manual browser verification

- [ ] **D1** Start the main API, the TryOn service (Phase 3/4's `dotnet run`), and the storefront (`npm run start`).
- [ ] **D2** Navigate to any `/products/:id` page as a logged-in customer, upload a photo, submit — confirm a loading state appears, then either the rendered result image or a friendly error (if the Gemini key is still a placeholder).
- [ ] **D3** Navigate away and back to the same product — confirm the previous result/photo is gone (nothing persisted, per D11) — the file input and result area are both empty/reset.
- [ ] **D4** As a customer whose tenant is at its `aiUsageLimit`, submit again — confirm the friendly quota-exceeded message (429) renders instead of a generic error.

### Group E — Validate

- [ ] **E1** `npm run lint` (ESLint) and `npx tsc --noEmit` — clean, matching this project's existing frontend gate (per `frontend-standards.md`'s severity model — any error fails).
- [ ] **E2** `npm run test:ci` (full suite) — green, exact count reported.

## 2. Code samples — files to create / modify

### A1 — `fashionsaas-storefront/src/environments/environment.ts`

`E:\AIcLOTHING\fashionsaas-storefront\src\environments\environment.ts`

```typescript
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5000/api',
  tenantSlug: 'default-tenant',
  tryOnApiBaseUrl: 'http://localhost:5050/api',
};
```

### A1 — `fashionsaas-storefront/src/environments/environment.prod.ts`

`E:\AIcLOTHING\fashionsaas-storefront\src\environments\environment.prod.ts`

```typescript
export const environment = {
  production: true,
  apiBaseUrl: 'https://api.fashionsaas.com/api',
  tenantSlug: '',
  tryOnApiBaseUrl: 'https://tryon.fashionsaas.com/api',
};
```

### B1 — `fashionsaas-storefront/src/app/features/catalog/models/try-on.model.ts`

`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\models\try-on.model.ts` — the TryOn service's own response envelope (D3's independent-copy principle applied to the frontend too — **not** the main API's `ApiResponse<T>`, whose fields don't match: no `isSuccess`, has a `timestamp` field the TryOn service doesn't send).

```typescript
export interface TryOnApiResponse<T> {
  isSuccess: boolean;
  statusCode: number;
  message: string;
  data: T | null;
  errors: string[] | null;
}

export interface TryOnResult {
  resultImageDataUri: string;
}
```

### B1 — `fashionsaas-storefront/src/app/features/catalog/services/try-on.service.ts`

`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\services\try-on.service.ts` (modelled on `ProductService`'s `@Injectable({ providedIn: 'root' })` shape, but using `HttpClient` directly rather than the shared `ApiService` — `ApiService` is scoped to `environment.apiBaseUrl`, and this call targets the separate `environment.tryOnApiBaseUrl` origin).

```typescript
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import { TryOnApiResponse, TryOnResult } from '../models/try-on.model';

@Injectable({ providedIn: 'root' })
export class TryOnService {
  constructor(private http: HttpClient) {}

  render(
    photo: File,
    garmentImageUrl: string,
    productId: string,
    productVariantId?: string
  ): Observable<TryOnResult> {
    const formData = new FormData();
    formData.append('photo', photo);
    formData.append('garmentImageUrl', garmentImageUrl);
    formData.append('productId', productId);
    if (productVariantId) {
      formData.append('productVariantId', productVariantId);
    }

    return this.http
      .post<TryOnApiResponse<TryOnResult>>(`${environment.tryOnApiBaseUrl}/tryon`, formData)
      .pipe(
        map((response) => {
          if (!response.data) {
            throw new Error(response.message || 'Try-on render failed.');
          }
          return response.data;
        })
      );
  }
}
```

### C3 — `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.ts`

`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\components\product-detail\product-detail.component.ts` — add imports, a constructor param, and 4 new fields/methods. Nothing here is cached or persisted beyond the component instance (D11) — a fresh navigation to the same product creates a fresh component instance with fresh `BehaviorSubject`s, so there is nothing to explicitly clear.

```typescript
// Add to imports at the top:
import { TryOnService } from '../../services/try-on.service';

// Add to the constructor (after cartService):
  constructor(
    private productService: ProductService,
    private cartService: CartService,
    private tryOnService: TryOnService,
    private route: ActivatedRoute,
    private router: Router
  ) {}

// Add new fields (after currentImageIndex):
  tryOnPhotoFile: File | null = null;
  tryOnResultDataUri$ = new BehaviorSubject<string | null>(null);
  tryOnLoading$ = new BehaviorSubject<boolean>(false);
  tryOnError$ = new BehaviorSubject<string | null>(null);

// Add new methods (near addToCart):
  /**
   * Try It On — spec §8 (fully stateless): the uploaded photo and rendered result
   * exist only in this component's memory for the current view. Nothing is sent
   * anywhere except the try-on service's single request/response.
   */
  onTryOnPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.tryOnPhotoFile = input.files?.[0] ?? null;
    this.tryOnError$.next(null);
    this.tryOnResultDataUri$.next(null);
  }

  submitTryOn(): void {
    const product = this.product$.value;
    const variant = this.selectedVariant$.value;

    if (!this.tryOnPhotoFile) {
      this.tryOnError$.next('Please choose a photo first.');
      return;
    }
    if (!product?.primaryImageUrl) {
      this.tryOnError$.next('This product has no image to try on.');
      return;
    }

    this.tryOnLoading$.next(true);
    this.tryOnError$.next(null);
    this.tryOnResultDataUri$.next(null);

    this.tryOnService
      .render(this.tryOnPhotoFile, product.primaryImageUrl, this.productId, variant?.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.tryOnLoading$.next(false);
          this.tryOnResultDataUri$.next(result.resultImageDataUri);
        },
        error: (err) => {
          this.tryOnLoading$.next(false);
          const status = err?.status;
          this.tryOnError$.next(
            status === 429
              ? "You've reached this month's try-on limit. Upgrade your plan or try again next month."
              : 'The try-on render failed. Please try again in a moment.'
          );
        },
      });
  }
```

### C4 — `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.html`

`E:\AIcLOTHING\fashionsaas-storefront\src\app\features\catalog\components\product-detail\product-detail.component.html:213-214` — insert a new section immediately after the existing `action-buttons` closing `</div>` (line 213) and before the `<!-- Product Tags -->` comment (line 215):

```html
          <!-- Try It On (spec: fully stateless — no consent checkbox, no saved-photo affordance) -->
          <div class="try-on-section mb-4">
            <label class="form-label">Try It On</label>
            <input
              type="file"
              accept="image/jpeg,image/png"
              class="form-control mb-2"
              (change)="onTryOnPhotoSelected($event)"
            />
            <button
              class="btn btn-outline-primary w-100 mb-2"
              (click)="submitTryOn()"
              [disabled]="(tryOnLoading$ | async) === true"
            >
              <span *ngIf="(tryOnLoading$ | async) === true">Rendering...</span>
              <span *ngIf="(tryOnLoading$ | async) !== true">
                <i class="bi bi-magic me-2"></i>Try It On
              </span>
            </button>
            <div *ngIf="tryOnError$ | async as tryOnError" class="alert alert-warning" role="alert">
              {{ tryOnError }}
            </div>
            <img
              *ngIf="tryOnResultDataUri$ | async as tryOnResultDataUri"
              [src]="tryOnResultDataUri"
              alt="Try-on render result"
              class="img-fluid rounded try-on-result"
            />
          </div>
```

## 3. Exact test list (testing-expert)

Paradigm: Vitest, `vi.fn()` mocks, `TestBed.configureTestingModule` with `useValue` service mocks — matching `product-detail.component.spec.ts`'s existing convention exactly.

### `fashionsaas-storefront/src/app/features/catalog/services/try-on.service.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TryOnService } from './try-on.service';
import { environment } from '../../../../environments/environment';

describe('TryOnService', () => {
  let service: TryOnService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(TryOnService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('posts multipart form data to the try-on microservice base URL', () => {
    const photo = new File(['fake'], 'photo.jpg', { type: 'image/jpeg' });

    service.render(photo, 'https://cdn.example.com/garment.jpg', 'product-1', 'variant-1').subscribe();

    const req = httpMock.expectOne(`${environment.tryOnApiBaseUrl}/tryon`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({ isSuccess: true, statusCode: 200, message: 'Success', data: { resultImageDataUri: 'data:image/png;base64,abc' }, errors: null });
  });

  it('emits the result data URI on success', () => {
    const photo = new File(['fake'], 'photo.jpg', { type: 'image/jpeg' });
    const next = vi.fn();

    service.render(photo, 'https://cdn.example.com/garment.jpg', 'product-1').subscribe(next);

    const req = httpMock.expectOne(`${environment.tryOnApiBaseUrl}/tryon`);
    req.flush({ isSuccess: true, statusCode: 200, message: 'Success', data: { resultImageDataUri: 'data:image/png;base64,xyz' }, errors: null });

    expect(next).toHaveBeenCalledWith({ resultImageDataUri: 'data:image/png;base64,xyz' });
  });

  it('throws when the response has no data (failure envelope)', () => {
    const photo = new File(['fake'], 'photo.jpg', { type: 'image/jpeg' });
    const error = vi.fn();

    service.render(photo, 'https://cdn.example.com/garment.jpg', 'product-1').subscribe({ next: () => {}, error });

    const req = httpMock.expectOne(`${environment.tryOnApiBaseUrl}/tryon`);
    req.flush({ isSuccess: false, statusCode: 429, message: 'Quota exceeded.', data: null, errors: null });

    expect(error).toHaveBeenCalled();
  });
});
```

This requires `HttpClientTestingModule` — confirm it's already a dependency (Angular's own testing package, part of `@angular/common/http/testing`, no new package needed).

### `fashionsaas-storefront/src/app/features/catalog/components/product-detail/product-detail.component.spec.ts` (additions)

Add to the existing `beforeEach`'s providers list a mock `TryOnService`:

```typescript
// Add near the other Partial<...> declarations:
let tryOnService: Partial<TryOnService>;

// In beforeEach, before configureTestingModule:
tryOnService = { render: vi.fn() };

// Add to providers array:
{ provide: TryOnService, useValue: tryOnService },
```

Add the import: `import { TryOnService } from '../../services/try-on.service';`

New tests:

```typescript
describe('Try It On', () => {
  it('shows an error when submitting without a photo', () => {
    component.tryOnPhotoFile = null;
    component.submitTryOn();
    let error: string | null = null;
    component.tryOnError$.subscribe((e) => (error = e));
    expect(error).toBe('Please choose a photo first.');
  });

  it('renders the result data URI on a successful submit', () => {
    component.product$.next(mockProduct);
    component.tryOnPhotoFile = new File(['x'], 'photo.jpg', { type: 'image/jpeg' });
    (tryOnService.render as any).mockReturnValue(of({ resultImageDataUri: 'data:image/png;base64,abc' }));

    component.submitTryOn();

    let result: string | null = null;
    component.tryOnResultDataUri$.subscribe((r) => (result = r));
    expect(result).toBe('data:image/png;base64,abc');
  });

  it('shows the quota-exceeded message on a 429 error', () => {
    component.product$.next(mockProduct);
    component.tryOnPhotoFile = new File(['x'], 'photo.jpg', { type: 'image/jpeg' });
    (tryOnService.render as any).mockReturnValue(throwError(() => ({ status: 429 })));

    component.submitTryOn();

    let error: string | null = null;
    component.tryOnError$.subscribe((e) => (error = e));
    expect(error).toContain("this month's try-on limit");
  });

  it('resets the photo selection and clears prior state when a new file is chosen', () => {
    component.tryOnResultDataUri$.next('data:image/png;base64,stale');
    component.tryOnError$.next('stale error');

    const file = new File(['x'], 'new-photo.jpg', { type: 'image/jpeg' });
    const event = { target: { files: [file] } } as unknown as Event;
    component.onTryOnPhotoSelected(event);

    expect(component.tryOnPhotoFile).toBe(file);
    let result: string | null = null;
    component.tryOnResultDataUri$.subscribe((r) => (result = r));
    expect(result).toBeNull();
  });
});
```

> **Known coverage gap:** no automated test proves the HTML template's file input/button/result `<img>` render correctly against a live DOM in this phase's test list (the existing `product-detail.component.spec.ts` file doesn't appear to assert template DOM elsewhere either, based on the excerpt read — if that's confirmed false during implementation and the file does have DOM-level assertions elsewhere, testing-expert should add an equivalent DOM assertion here for consistency, per the lesson from Phase 4b's duplicate-render bug: assert rendered DOM, not just component state).

## 4. Observability

- None added — matches the rest of the storefront's existing convention (`console.error` on failure paths elsewhere in this same component, e.g. `ngOnInit`'s error handler) — no new logging infrastructure introduced.

## 5. OPEN QUESTIONS (decisions, not facts)

1. **Toast vs. inline alert for errors** — this plan uses an inline Bootstrap `alert-warning` div (matching this component's existing `error$` pattern elsewhere in the template, based on the CSS classes already in use — `btn`, `alert` conventions are Bootstrap, consistent with the rest of the file). *Default: inline alert, consistent with the rest of this component; confirm during implementation whether a toast service already exists elsewhere in the storefront (spec §15 flags this as unresolved) and switch to it if so, for consistency.*

## 6. Assumptions

- `Product.primaryImageUrl` (confirmed field name, `product-detail.component.spec.ts:30`) is a public, unauthenticated-fetchable Cloudinary URL suitable as `GarmentImageUrl` — matches spec §5's assumption that the garment image is "a public/signed HTTPS resource."
- No existing Angular HTTP interceptor rejects or short-circuits requests to a different origin than `environment.apiBaseUrl` (confirmed: `AuthInterceptor` only reads/attaches a header, doesn't filter by URL; `error.interceptor.ts` was not read in this plan's research — verify during C1 that it doesn't assume all errors come from the main API's `ApiResponse<T>` shape in a way that would mishandle a `TryOnApiResponse<T>` error body; if it does, `TryOnService.render` already catches and rethrows a plain `Error`/passes through the raw `HttpErrorResponse`, so the component's own `error: (err) => ...` handler reads `err.status` directly rather than relying on the interceptor's transformation).
