---
name: impl-build
description: Implement a plan (or a focused slice/fix-list), validate it in a loop until clean, then write the plan's exact tests. Reconciles a stale plan to the real code (bounded), surfaces every deviation, and stops on a material/blocking divergence. Returns implementation + deviations + validation verdict + test results.
---

Invoke the `impl-build` workflow via the Workflow tool to build an approved plan (or a bounded
slice of one):

```
Workflow({ name: "impl-build", args: { plan: "docs/plans/<topic>.md", section: "optional section/task identifier" } })
```

- Pass `plan` as the path to an approved implementation plan (house format per
  `docs/projectStandards/implementation-plan-format.md`). Pass `section` to scope the run to one
  task/section instead of the whole plan.
- The workflow pipelines: `implementer` builds the section → `validator` checks it against the
  brief and `.claude/rules/backend/*.md` → on FAIL, a fix pass re-dispatches the implementer with
  the validator's exact findings → repeat until the validator passes or a bound on retries is hit
  → `testing-expert` writes and runs the plan's exact named tests.
- **Reconciling a stale plan**: if the plan's code samples reference something the real codebase
  has since renamed/moved/removed, the workflow may adapt within the section's stated intent
  (e.g. the Phase 3 `TryOnService` relocation from `Application` to `Infrastructure` to resolve a
  real circular dependency) — but any divergence **must be surfaced explicitly** in the returned
  report, never silently absorbed. A **material or blocking** divergence (the plan's approach is
  no longer viable at all, not just relocated) stops the workflow rather than guessing at a
  replacement design — that decision belongs to the human running `run-impl-loop`.
- Returns: files changed, the validation verdict (with history if it took more than one fix
  pass), exact test names + pass/fail/skip counts, and a list of every deviation from the plan's
  literal text with its rationale.
