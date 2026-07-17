---
name: docs-standards-sync
description: Detect drift between our standards/governance docs and the actual code/config. One agent per doc compares its claims to reality; returns a consolidated drift report with proposed fixes. Proposes only — never edits.
---

Invoke the `docs-standards-sync` workflow via the Workflow tool to audit governance docs against
reality:

```
Workflow({ name: "docs-standards-sync", args: { docs: ["docs/CONVENTIONS.md", "docs/projectStandards/coding-standards.md", "CLAUDE.md"] } })
```

- Pass `docs` as the list of governance files to check (default to `docs/CONVENTIONS.md`,
  `docs/projectStandards/*.md`, `CLAUDE.md`, and `.claude/rules/backend/*.md` if omitted).
- One agent per doc reads its claims, then greps/reads the actual codebase to check whether each
  claim still holds — a stack described that isn't what's running, a "banned" pattern that's
  actually used pervasively, a file path that no longer exists.
- **This project has known drift**: `docs/projectStandards/*.md` describes a generic template
  (PostgreSQL/Npgsql, Kommand CQRS, Minimal APIs, no primary constructors) that does not match the
  actual FashionSaaS code (SQL Server, MVC Controllers, primary constructors used throughout,
  `docs/CONVENTIONS.md` as the real binding conventions). The workflow should surface this as a
  standing, known drift item, not just report it as if newly discovered each run — but it should
  still flag *new* drift the same way.
- Returns a consolidated report: doc → claim → actual state → proposed fix. **Proposes only** —
  the calling agent decides whether/how to apply fixes, per the project's "decisions are Dan's"
  rule; never auto-edit a governance doc from this workflow's output without asking first.
