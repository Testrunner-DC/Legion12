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
assert(packageDocument.scripts['check:performance-architecture'].includes('check-action-gate.mjs'))
assert(packageDocument.scripts['check:performance-architecture'].includes('check-http-traffic-guard.mjs'))
assert(packageDocument.scripts['check:performance-architecture'].includes('check-performance-observability.mjs'))
assert(changeGate.includes('npm.cmd" @("run", "check:performance-architecture")'))
assert.equal((workflow.match(/npm run check:performance-architecture/g) ?? []).length, 0)
const assertSingleStandardBuild = source => assert.equal(
  (source.match(/^\s+npm run build 2>&1 \| Tee-Object -FilePath \$log\s*$/gm) ?? []).length, 1)
assertSingleStandardBuild(workflow)
assert.throws(() => assertSingleStandardBuild(workflow.replace('npm run build 2>&1', 'echo skipped 2>&1')))
assert.throws(() => assertSingleStandardBuild(workflow + '\n          npm run build 2>&1 | Tee-Object -FilePath $log\n'))

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
  runtimeAcceptance: {
    windowSeconds: 60, minimumSamples: 20, slowRequestThresholdMilliseconds: 1000,
    maximumSlowRequestPercent: 5, maximumServerErrorPercent: 1, maximumRateLimitedPercent: 20,
  },
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

console.log('Performance architecture lock regression passed: 15/15')

for (const expression of [
  'Promise.allSettled([one(),two(),three(),four()])',
  'const tasks=[one(),two(),three(),four()]; Promise.allSettled(tasks)',
  'async function init(){ void loadOne(); loadTwo().catch(()=>{}); loadThree(); void loadFour() }',
  'onMounted(()=>{ loadOne(); loadTwo(); loadThree(); loadFour() })',
  'async function init(){const first=loadOne();const second=loadTwo();const third=loadThree();await loadFour()}',
]) assert(analyzeSource(expression).maximumParallelPageLoad >= 4, expression)
assert.equal(analyzeSource('async function init(){await loadOne();await loadTwo();await loadThree();await loadFour()}').maximumParallelPageLoad, 1)
assert.equal(analyzeSource('async function init(){if(ok){loadOne();loadTwo()}else{loadThree();loadFour()}}').maximumParallelPageLoad, 2)
console.log('Settled/background fan-out regression passed: 7/7')

const privateFetchCases = [
  ['private instance wrapper', 'class A { private fetch(){ return this.read() } go(){ return this.fetch() } }', 0],
  ['native call in wrapper body', 'class A { private fetch(){ return globalThis.fetch("/api") } go(){ return this.fetch() } }', 1],
  ['bare native call in wrapper body', 'class A { private fetch(){ return fetch("/api") } go(){ return this.fetch() } }', 1],
  ['lexical arrow', 'class A { private fetch(){ return this.read() } go(){ return () => this.fetch() } }', 0],
  ['instance field arrow', 'class A { private fetch(){ return this.read() } go = () => this.fetch() }', 0],
  ['unknown instance', 'class A { go(){ return this.fetch() } }', 1],
  ['unrelated client', 'client.fetch("/api")', 1],
  ['public method', 'class A { fetch(){ return this.read() } go(){ return this.fetch() } }', 1],
  ['protected method', 'class A { protected fetch(){ return this.read() } go(){ return this.fetch() } }', 1],
  ['static declared fetch', 'class A { private static fetch(){ return this.read() } static go(){ return this.fetch() } }', 1],
  ['getter fetch', 'class A { private get fetch(){ return this.read } go(){ return this.fetch() } }', 1],
  ['inherited method', 'class A { private fetch(){ return this.read() } } class B extends A { go(){ return this.fetch() } }', 1],
  ['nested class boundary', 'class A { private fetch(){ return this.read() } go(){ return class B { go(){ return this.fetch() } } } }', 1],
  ['ordinary function boundary', 'class A { private fetch(){ return this.read() } go(){ return function(){ return this.fetch() } } }', 1],
  ['object method boundary', 'class A { private fetch(){ return this.read() } go(){ return { go(){ return this.fetch() } } } }', 1],
  ['static caller', 'class A { private fetch(){ return this.read() } static go(){ return this.fetch() } }', 1],
  ['static block caller', 'class A { private fetch(){ return this.read() } static { this.fetch() } }', 1],
  ['static field arrow', 'class A { private fetch(){ return this.read() } static go = () => this.fetch() }', 1],
  ['accessor caller remains conservative', 'class A { private fetch(){ return this.read() } get value(){ return this.fetch() } }', 1],
  ['computed method name', 'class A { private fetch(){ return this.read() } [this.fetch()](){} }', 1],
  ['computed field name', 'class A { private fetch(){ return this.read() } [this.fetch()] = 1 }', 1],
  ['browser fetch variants remain counted', 'fetch("/a"); window.fetch("/b"); globalThis.fetch("/c"); self.fetch("/d")', 4],
  ['private method reassigned to native', 'class A { private fetch(){ return this.read() } go(){ this.fetch = globalThis.fetch; return this.fetch("/api") } }', 1],
  ['private method reassigned by element', 'class A { private fetch(){ return this.read() } go(){ this["fetch"] = globalThis.fetch; return this.fetch("/api") } }', 1],
  ['instance mutation through Object.assign', 'class A { private fetch(){ return this.read() } go(){ Object.assign(this, { fetch: globalThis.fetch }); return this.fetch("/api") } }', 1],
  ['instance mutation through descriptor', 'class A { private fetch(){ return this.read() } go(){ Object.defineProperty(this, "fetch", { value: globalThis.fetch }); return this.fetch("/api") } }', 1],
  ['wrapped assignment receiver', 'class A { private fetch(){ return this.read() } go(){ (this).fetch = globalThis.fetch; return this.fetch("/api") } }', 1],
  ['cast assignment receiver', 'class A { private fetch(){ return this.read() } go(){ (this as A).fetch = globalThis.fetch; return this.fetch("/api") } }', 1],
  ['wrapped mutator receiver', 'class A { private fetch(){ return this.read() } go(){ Object.assign((this), { fetch: globalThis.fetch }); return this.fetch("/api") } }', 1],
  ['wrapped mutator callee and cast receiver', 'class A { private fetch(){ return this.read() } go(){ (Object.assign)(this as A, { fetch: globalThis.fetch }); return this.fetch("/api") } }', 1],
  ['angle assertion receiver', 'class A { private fetch(){ return this.read() } go(){ (<A>this).fetch = globalThis.fetch; return this.fetch("/api") } }', 1],
  ['non-null receiver', 'class A { private fetch(){ return this.read() } go(){ Object.assign(this!, { fetch: globalThis.fetch }); return this.fetch("/api") } }', 1],
]
for (const [name, source, expected] of privateFetchCases)
  assert.equal(analyzeSource(source).rawFetch, expected, name)
console.log(`Private wrapper/native fetch boundary regression passed: ${privateFetchCases.length}/${privateFetchCases.length}`)
