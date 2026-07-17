---
description: Multi-tenant isolation rules for the FashionSaaS backend (auto-loads for src/FashionSaaS.* and services/*/src/*.cs)
---

# Tenancy — first-class invariant

`tenant_id` is a first-class invariant across the backend. Verified against real code
(`src/FashionSaaS.Infrastructure/Persistence/*`, `TenantResolutionMiddleware.cs`).

- Every tenant-scoped entity carries a non-null, indexed `TenantId` column.
- Global EF Core query filters enforce tenant scoping on reads. **Fail closed**: no tenant
  context in scope must mean zero rows or an explicit exception — never "all rows."
- Treat `IgnoreQueryFilters()` as a privileged, audited operation. Don't reach for it to make a
  query "just work" — that's usually a sign the query should be scoped differently.
- Enforce `TenantId` on **writes** in the service/handler layer too — query filters only
  constrain reads, they don't stop a write from omitting or forging a tenant id.
- SuperAdmin (platform-level) users are tenant-less (`user.TenantId is null`) — code that reads
  a tenant-scoped value (e.g. a subscription's AI usage limit) must explicitly branch on this
  and default to a safe value (e.g. `0`), never assume every user has a tenant.
- New microservices (e.g. `services/fashionsaas-tryon`) that read tenant-scoped claims from a
  JWT must resolve `TenantId` the same way the main API issues it — verify the claim name and
  type match end-to-end before trusting a cross-service token.
