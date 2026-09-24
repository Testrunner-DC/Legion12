import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { analyzeSource, evaluateArchitecture } from './check-performance-architecture.mjs'

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = path.resolve(frontendRoot, '..')
const packageDocument = JSON.parse(fs.readFileSync(path.join(frontendRoot, 'package.json'), 'utf8'))
const changeGate = fs.readFileSync(path.join(repositoryRoot, 'scripts', 'verify-l12-change.ps1'), 'utf8')
const workflow = fs.readFileSync(path.join(repositoryRoot, '.github', 'workflows', 'verify-release.yml'), 'utf8')
assert(packageDocument.scripts.build.includes('npm run check:performance-architecture'))
assert(packageDocument.scripts['check:performance-architecture'].includes('check-platform-request-reliability.mjs'))
assert(changeGate.includes('npm.cmd" @("run", "check:performance-architecture")'))
assert.equal((workflow.match(/npm run check:performance-architecture/g) ?? []).length, 1)

assert.deepEqual(analyzeSource(`<script setup lang="ts">
  fetch('/api/example')
  window.setInterval(() => refresh(), 1000)
  Promise.all([one(), two(), three(), four()])
</script>`, 'fixture.vue'), { rawFetch: 1, interval: 1, maximumParallelPageLoad: 4 })

const budgets = {
  schema: 1,
  defaults: {
    steadyStateHttpRequestsPerMinute: 0,
    hiddenTabRequestsPerMinute: 0,
    maximumMutationRequestsPerAction: 1,
    maximumInitialApiRequests: 3,
    maximumParallelPageLoads: 3,
  },
  approvedRawFetchModules: [],
  approvedIntervalModules: [],
  routes: {},
}

const evaluate = (findings, exceptions = []) => evaluateArchitecture({
  budgets,
  exceptionDocument: { schema: 1, exceptions },
  findings: new Map(findings),
  today: new Date('2026-09-24T00:00:00Z'),
})

const clean = [['src/clean.ts', { rawFetch: 0, interval: 0, maximumParallelPageLoad: 3 }]]
assert.deepEqual(evaluate(clean), [])
assert(evaluate([['src/new.ts', { rawFetch: 1, interval: 0, maximumParallelPageLoad: 0 }]])
  .some(message => message.includes('raw-fetch')))
assert(evaluate([['src/new.ts', { rawFetch: 0, interval: 1, maximumParallelPageLoad: 0 }]])
  .some(message => message.includes('interval')))
assert(evaluate([['src/new.ts', { rawFetch: 0, interval: 0, maximumParallelPageLoad: 4 }]])
  .some(message => message.includes('parallel-page-load')))

const exception = {
  id: 'PERF-TEST-001', rule: 'interval', path: 'src/legacy.ts', allowedValue: 1,
  owner: 'test', reason: 'legacy fixture', expiresAt: '2026-10-01',
}
assert.deepEqual(evaluate([['src/legacy.ts', { rawFetch: 0, interval: 1, maximumParallelPageLoad: 0 }]], [exception]), [])
assert(evaluate([['src/legacy.ts', { rawFetch: 0, interval: 0, maximumParallelPageLoad: 0 }]], [exception])
  .some(message => message.includes('stale')))
assert(evaluate([['src/legacy.ts', { rawFetch: 0, interval: 1, maximumParallelPageLoad: 0 }]], [{ ...exception, expiresAt: '2026-09-23' }])
  .some(message => message.includes('expired')))
assert(evaluate([['src/legacy.ts', { rawFetch: 0, interval: 2, maximumParallelPageLoad: 0 }]], [exception])
  .some(message => message.includes('above allowed')))

console.log('Performance architecture lock regression passed: 13/13')
