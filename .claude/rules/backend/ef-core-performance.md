---
description: EF Core query and configuration performance rules for the FashionSaaS backend (SQL Server)
---

# EF Core — query performance and configuration

Per `docs/CONVENTIONS.md` §4, §6, §7. This backend is **EF Core 10 on SQL Server** — not
PostgreSQL/Npgsql (the generic template in `docs/projectStandards/backend-architecture.md`
describes a Postgres/Kommand architecture this codebase does not use; don't apply it here).

- **Compose as `IQueryable<T>`**, materialize once (`ToListAsync`/`FirstOrDefaultAsync`/`CountAsync`).
  Never build an in-memory list and filter with LINQ-to-objects.
- **`AsNoTracking()`** on every read-only query (lists, paged reads, lookups, projections). Keep
  tracking only for a single-entity fetch the caller will mutate and save.
- **`AsSplitQuery()`** for any query with two or more collection `Include`s, to avoid a cartesian
  explosion. Prefer `AsNoTracking()` alongside it (EF de-dups roots in split queries).
- **Existence checks** use `AnyAsync(predicate)`, never `CountAsync() > 0` or full materialization.
- **Project to a DTO** (`.Select(x => new Dto{...})`) for list/summary reads that don't need the
  full entity graph.
- **Paginate at the database** (`Skip`/`Take` on the `IQueryable` + a separate `CountAsync`) —
  never page in memory.
- **No lazy loading** — eager-load with `Include`; lazy proxies are not enabled.
- **One `IEntityTypeConfiguration<T>` per entity** under `Persistence/Configurations/`, applied via
  `ApplyConfigurationsFromAssembly`. Don't configure entities inline in `OnModelCreating` — the
  only thing that stays there is the cross-cutting tenant/soft-delete query filter.
- **Index by real query patterns**, not guesses — composite indexes in selectivity order, matching
  actual repository predicates. Don't over-index hot write paths.
- Seed data (`HasData`) uses static timestamps, never `DateTime.UtcNow` (causes migration churn).
