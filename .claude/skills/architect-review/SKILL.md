---
name: architect-review
description: Run the relevant reviewers (backend/frontend/fullstack architects + backend security auditor) in parallel for rule-adherence, security, and bug hunt, then adversarially verify each finding line-by-line. Returns findings tagged real/noise for the main agent's final triage.
---

Invoke the `architect-review` workflow via the Workflow tool to review a change (a diff, a commit
range, or a set of files):

```
Workflow({ name: "architect-review", args: { base: "<base-ref-or-commit>", head: "<head-ref-or-commit-or-HEAD>", paths: ["optional/scoped/paths"] } })
```

- Pass `base`/`head` as git refs or commit SHAs bounding the change to review (e.g. the commit
  before an implementer's task and `HEAD` after). Omit `paths` to let the workflow infer scope
  from `git diff --stat`; pass it to scope a review to specific files/directories.
- The workflow fans out `architect-backend`, `architect-frontend`, `architect-fullstack`, and
  `security-auditor-backend` in parallel (only the ones relevant to the changed paths), collects
  their findings, then dispatches a `findings-verifier` per finding to adjudicate real vs. noise.
- It returns a consolidated list of findings, each tagged `real` or `noise` with the verifier's
  evidence — the calling agent does final triage (what to fix now vs. defer) and never re-derives
  the reviewers' analysis itself.
- Read the workflow's result directly; don't re-run the individual architect agents yourself after
  calling this — that duplicates the work the workflow already did.
