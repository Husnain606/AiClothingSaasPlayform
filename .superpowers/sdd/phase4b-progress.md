# Phase 4b: Role-Routed Admin Area — SDD Progress Ledger

**Code repo/branch:** fashionsaas-storefront @ feature/phase4b-admin-area (base ba9593e)
**Plan:** docs/superpowers/plans/2026-07-04-phase4b-admin-area.md (9784 lines, 11 tasks)
**Spec:** docs/superpowers/specs/2026-07-02-phase4-admin-dashboard-design.md (section 4)
**Started:** 2026-07-04
**Backend contract:** Phase 4a merged at outer-repo 28e72d5 (api/store/orders, api/tenant/orders, api/tenant/reports live)

## Tasks

- [ ] Task 1: Auth upgrade (role parsing, three-way redirect, guards, MFA challenge, zoneless provider)
- [ ] Task 2: Admin shell (AdminLayout, /admin + /admin/platform scaffolds, header Dashboard link)
- [ ] Task 3: Admin shared kit (toast, data-table, KPI card, confirm modal, date-range picker, status badge)
- [ ] Task 4: API layer & contract reconciliation (TS DTOs, OrderAdminService, ReportApiService, apiBaseUrl /v1 fix, checkout/account repoints)
- [ ] Task 5: Dashboard home (ng2-charts, KPIs, charts)
- [ ] Task 6: Orders module (list/detail/status actions)
- [ ] Task 7: Catalog module (products, categories tree, variants, images)
- [ ] Task 8: Inventory + customers modules
- [ ] Task 9: Discounts + reviews modules
- [ ] Task 10: Reports + settings modules
- [ ] Task 11: Platform console + hardening (bundle budget, prod grep, suite ×2, docs)

## Completed

(none yet)

## Minor findings for final review

- Plan conflicts already resolved in-plan: zoneless provider missing (T1 adds), apiBaseUrl /v1 (T4), checkout OrderService dead route + dishonest payload (T4), account.model.ts phantom 'processing' status + AccountService dead route (T4)
