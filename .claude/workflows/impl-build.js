export const meta = {
  name: 'impl-build',
  description: 'Implement a plan section, validate in a loop until clean, then write its exact tests',
  phases: [
    { title: 'Implement' },
    { title: 'Validate' },
    { title: 'Test' },
  ],
}

const VALIDATION_SCHEMA = {
  type: 'object',
  properties: {
    verdict: { type: 'string', enum: ['PASS', 'FAIL'] },
    findings: { type: 'array', items: { type: 'string' } },
  },
  required: ['verdict', 'findings'],
}

const REPORT_SCHEMA = {
  type: 'object',
  properties: {
    status: { type: 'string', enum: ['DONE', 'DONE_WITH_CONCERNS', 'NEEDS_CONTEXT', 'BLOCKED'] },
    files_changed: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    deviations: { type: 'array', items: { type: 'string' } },
  },
  required: ['status', 'summary'],
}

const TEST_REPORT_SCHEMA = {
  type: 'object',
  properties: {
    tests_written: { type: 'array', items: { type: 'string' } },
    passed: { type: 'number' },
    failed: { type: 'number' },
    skipped: { type: 'number' },
    could_not_write: { type: 'array', items: { type: 'string' } },
  },
  required: ['tests_written', 'passed', 'failed', 'skipped'],
}

const plan = args?.plan
const section = args?.section ?? 'the entire plan'
if (!plan) {
  throw new Error('impl-build requires args.plan (path to the implementation plan)')
}

const MAX_FIX_ATTEMPTS = 3

phase('Implement')
let implReport = await agent(
  `Read the implementation plan at ${plan} and implement ${section}. Follow docs/CONVENTIONS.md and .claude/rules/backend/*.md. Match existing codebase patterns exactly (this codebase uses primary constructors and ASP.NET Core Controllers). If the plan's code samples reference something the real codebase has since moved/renamed, adapt within the section's stated intent and report the deviation explicitly — do not silently absorb it, and stop with BLOCKED if the divergence is material (the planned approach is no longer viable at all). No third-party library without explicit approval — report NEEDS_CONTEXT with a no-library alternative if one seems needed. After any .cs change, run dotnet build (0 warnings/0 errors required) and report the files you changed.`,
  { label: 'implementer', agentType: 'implementer', schema: REPORT_SCHEMA }
)

if (implReport?.status === 'BLOCKED' || implReport?.status === 'NEEDS_CONTEXT') {
  log(`Implementer reported ${implReport.status} — stopping before validation.`)
  return { implementation: implReport, validation: null, tests: null }
}

phase('Validate')
let validation = await agent(
  `Validate the implementation just done for ${section} of ${plan} against the plan's requirements and .claude/rules/backend/*.md. Re-run dotnet build and the relevant tests yourself; don't trust reported numbers without reproducing them. Files changed per the implementer's report: ${(implReport?.files_changed ?? []).join(', ') || '(not reported — check git status/diff yourself)'}.`,
  { label: 'validator', agentType: 'validator', schema: VALIDATION_SCHEMA }
)

let attempts = 0
while (validation?.verdict === 'FAIL' && attempts < MAX_FIX_ATTEMPTS) {
  attempts++
  log(`Validation FAIL (attempt ${attempts}/${MAX_FIX_ATTEMPTS}) — dispatching fix pass.`)
  implReport = await agent(
    `Fix the following validator findings for ${section} of ${plan}, then re-run dotnet build and report:\n\n${(validation.findings ?? []).map((f) => `- ${f}`).join('\n')}`,
    { label: `implementer:fix-${attempts}`, agentType: 'implementer', schema: REPORT_SCHEMA }
  )
  if (implReport?.status === 'BLOCKED') {
    log('Implementer BLOCKED on fix pass — stopping.')
    return { implementation: implReport, validation, tests: null }
  }
  validation = await agent(
    `Re-validate the fix pass for ${section} of ${plan} against the same findings and the plan's requirements. Re-run dotnet build and tests yourself.`,
    { label: `validator:recheck-${attempts}`, agentType: 'validator', schema: VALIDATION_SCHEMA }
  )
}

if (validation?.verdict === 'FAIL') {
  log(`Still FAIL after ${MAX_FIX_ATTEMPTS} fix attempts — escalating rather than looping further.`)
  return { implementation: implReport, validation, tests: null }
}

phase('Test')
const testReport = await agent(
  `Write and run the exact tests specified in ${plan} for ${section}. Use the exact test names from the plan where given. Never weaken a test to make it pass. Report exact pass/fail/skip counts per test project.`,
  { label: 'testing-expert', agentType: 'testing-expert', schema: TEST_REPORT_SCHEMA }
)

return { implementation: implReport, validation, tests: testReport, fixAttempts: attempts }
