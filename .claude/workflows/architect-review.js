export const meta = {
  name: 'architect-review',
  description: 'Parallel architect + security review of a diff, then adversarial verification of each finding',
  phases: [
    { title: 'Review', detail: 'backend/frontend/fullstack architects + security auditor, in parallel' },
    { title: 'Verify', detail: 'one findings-verifier per finding, real vs. noise' },
  ],
}

const FINDINGS_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          file: { type: 'string' },
          line: { type: 'number' },
          severity: { type: 'string', enum: ['Critical', 'Important', 'Minor'] },
          summary: { type: 'string' },
          failure_scenario: { type: 'string' },
        },
        required: ['file', 'summary', 'failure_scenario'],
      },
    },
  },
  required: ['findings'],
}

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string', enum: ['real', 'noise'] },
    evidence: { type: 'string' },
  },
  required: ['verdict', 'evidence'],
}

const base = args?.base ?? 'HEAD~1'
const head = args?.head ?? 'HEAD'
const scopedPaths = args?.paths ?? null

const diffScope = scopedPaths
  ? `Scope the review to these paths only: ${scopedPaths.join(', ')}.`
  : `Run \`git diff --stat ${base} ${head}\` yourself first to see which paths changed, and scope your review to those.`

const backendPrompt = `Review the diff between ${base} and ${head} in this .NET backend (src/FashionSaaS.*, services/*/src/*.cs) for rule adherence and real bugs. ${diffScope} You are the architect-backend agent — call your own agent definition's instructions. Return findings as structured output.`
const frontendPrompt = `Review the diff between ${base} and ${head} in the fashionsaas-storefront Angular app for rule adherence and real bugs. ${diffScope} You are the architect-frontend agent — call your own agent definition's instructions. Return findings as structured output.`
const fullstackPrompt = `Review the diff between ${base} and ${head} for cross-stack seam issues (backend <-> storefront, backend <-> try-on microservice). ${diffScope} You are the architect-fullstack agent — call your own agent definition's instructions. Return findings as structured output.`
const securityPrompt = `Audit the diff between ${base} and ${head} in this .NET backend for OWASP-relevant issues, tenant isolation, secret handling, and SSRF/injection risk. ${diffScope} You are the security-auditor-backend agent — call your own agent definition's instructions. Return findings as structured output.`

phase('Review')
const reviews = await parallel([
  () => agent(backendPrompt, { label: 'architect-backend', agentType: 'architect-backend', schema: FINDINGS_SCHEMA }),
  () => agent(frontendPrompt, { label: 'architect-frontend', agentType: 'architect-frontend', schema: FINDINGS_SCHEMA }),
  () => agent(fullstackPrompt, { label: 'architect-fullstack', agentType: 'architect-fullstack', schema: FINDINGS_SCHEMA }),
  () => agent(securityPrompt, { label: 'security-auditor-backend', agentType: 'security-auditor-backend', schema: FINDINGS_SCHEMA }),
])

const allFindings = reviews
  .filter(Boolean)
  .flatMap((r) => r.findings ?? [])

if (allFindings.length === 0) {
  log('No findings from any reviewer.')
  return { findings: [] }
}

log(`${allFindings.length} findings from review — verifying each.`)

phase('Verify')
const verified = await parallel(
  allFindings.map((f) => async () => {
    const verdict = await agent(
      `Adversarially verify this finding. Try to refute it by reading the actual code.\n\nFile: ${f.file}:${f.line ?? '?'}\nSeverity claimed: ${f.severity ?? 'unspecified'}\nSummary: ${f.summary}\nClaimed failure scenario: ${f.failure_scenario}`,
      { label: `verify:${f.file}`, agentType: 'findings-verifier', schema: VERDICT_SCHEMA }
    )
    return { ...f, verdict: verdict?.verdict ?? 'noise', evidence: verdict?.evidence ?? 'verification agent returned no result' }
  })
)

const real = verified.filter((f) => f.verdict === 'real')
const noise = verified.filter((f) => f.verdict === 'noise')
log(`${real.length} real, ${noise.length} noise.`)

return { findings: verified, real, noise }
