export const meta = {
  name: 'docs-standards-sync',
  description: 'Detect drift between governance docs and actual code/config; propose fixes, never edit',
  phases: [{ title: 'Check' }],
}

const DEFAULT_DOCS = [
  'docs/CONVENTIONS.md',
  'docs/projectStandards/coding-standards.md',
  'docs/projectStandards/backend-architecture.md',
  'docs/projectStandards/build-configuration.md',
  'docs/projectStandards/frontend-standards.md',
  'CLAUDE.md',
]

const DRIFT_SCHEMA = {
  type: 'object',
  properties: {
    drift: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          claim: { type: 'string' },
          actual_state: { type: 'string' },
          evidence: { type: 'string' },
          proposed_fix: { type: 'string' },
        },
        required: ['claim', 'actual_state', 'evidence', 'proposed_fix'],
      },
    },
  },
  required: ['drift'],
}

const docs = args?.docs ?? DEFAULT_DOCS

phase('Check')
const results = await parallel(
  docs.map((doc) => async () => {
    const result = await agent(
      `Read ${doc} and check every concrete claim it makes (stack choices, banned/required patterns, file paths, project names) against the actual code in this repository. Known standing drift: docs/projectStandards/*.md describes a generic PostgreSQL/Npgsql/Kommand/Minimal-API template that does NOT match the real FashionSaaS code (SQL Server, MVC Controllers, primary constructors used throughout) — docs/CONVENTIONS.md is the real, as-built convention doc. Report that as a known item if you find it, but ALSO look for anything else you find that's drifted, not just this known case. For each drift item: state the doc's claim, the actual state (with file:line or command evidence), and a proposed fix (which side should change — the doc or the code — with a one-line reason). Do not edit anything.`,
      { label: `check:${doc}`, schema: DRIFT_SCHEMA }
    )
    return { doc, drift: result?.drift ?? [] }
  })
)

const total = results.reduce((n, r) => n + r.drift.length, 0)
log(`${total} drift item(s) across ${docs.length} doc(s).`)

return { results }
