---
description: Domain entity shape and async discipline for the FashionSaaS backend
---

# Entity design and async discipline

- **Rich mutable entities, not records** for anything with identity and lifecycle (`User`,
  `TryOnRequest`, etc.). Records are for value objects and parse DTOs only.
- **Primary constructors ARE used** in this codebase's actual convention (services, controllers,
  and several entities use them pervasively — confirmed by direct code inspection across
  `FashionSaaS.API`, `FashionSaaS.Infrastructure`, and `FashionSaaS.TryOn.*`). This differs from
  the generic template in `docs/projectStandards`/root `CLAUDE.md`, which bans them — per the
  project's own authority hierarchy ("if docs and code disagree, the code wins"), match the real
  codebase, not the stated non-negotiable. Flag the drift if you notice it, don't silently "fix"
  working code to match the doc.
- `tenant_id` on every tenant-scoped entity (see `tenancy.md`).
- **Async discipline, no exceptions:** no `.Result`/`.Wait()`, no `async void`,
  `CancellationToken` threaded through every async call in a request path. `ConfigureAwait(false)`
  in library-layer code (Infrastructure/Application) that doesn't need the sync context back.
- Verification gate for any `.cs` change: `dotnet build` (warnings-as-errors) is necessary but not
  sufficient — some Roslyn IDE naming rules (e.g. `IDE1006`, the `Async`-suffix rule) aren't
  enforced by `dotnet build`/`dotnet format`. Run the Serena LSP diagnostics tool
  (`mcp__serena__get_diagnostics_for_file`, `min_severity: 2`) on every touched file when Serena
  is available and bound to the file's actual location.
