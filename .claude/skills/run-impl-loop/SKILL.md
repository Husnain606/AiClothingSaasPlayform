---
name: run-impl-loop
description: Drive an approved implementation plan end-to-end in this session — analyze, implement, validate, test, architect-review, triage, fix, summarize — delegating the mechanical stages to the impl-build and architect-review workflows.
---

Invoked as `/run-impl-loop <plan-path>`. You (the main agent, not a subagent) drive this loop
directly across the session — it is not itself a single Workflow call, because triage and
go/no-go decisions belong to you and, where genuinely ambiguous, to Dan.

**Stages, in order, per section/task of the plan:**

1. **Analyze** — read the plan section (or the whole plan if small). Note global constraints,
   locked decisions, and anything that looks stale against the current codebase (a referenced
   file/type that's moved, a dependency that's since changed). Do not silently assume the plan is
   still accurate — spot-check the key file paths it names.
2. **Implement** — invoke the `impl-build` skill/workflow for the section:
   `Workflow({ name: "impl-build", args: { plan: "<plan-path>", section: "<section>" } })`.
3. **Validate + Test** — these are internal to the `impl-build` workflow (validator + fix loop,
   then testing-expert) — read its returned report rather than re-running the checks yourself.
4. **Architect-review** — invoke the `architect-review` skill/workflow against the section's diff:
   `Workflow({ name: "architect-review", args: { base: "<commit-before-section>", head: "HEAD" } })`.
5. **Triage** — for every finding tagged `real`, decide: fix now (Critical/Important) or record as
   a known Minor item for the final whole-plan review. A finding that conflicts with what the plan
   explicitly mandates is Dan's call, not yours — present the finding and the plan text, ask which
   governs, don't silently pick.
6. **Fix** — for findings you're fixing now, dispatch one fix pass covering all of them together
   (not one dispatch per finding), then re-run `architect-review` on the fix's diff to confirm.
7. **Summary** — report, per section: files changed, test counts, review verdict, and any
   deviations from the plan's literal text with rationale. Move to the next section/task.

**End of plan:** once every section is done, do a final whole-plan `architect-review` across the
full diff (plan's first commit → HEAD), triage that the same way, then hand off per
`docs/projectStandards/implementation-plan-format.md`'s status-banner convention (exact build/test
counts, not "should be passing").

**Never** skip the architect-review stage to save time, and never mark a section done with a
FAIL validator verdict outstanding.
