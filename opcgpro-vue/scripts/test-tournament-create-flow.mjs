import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import * as Vue from 'vue'

const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r }); return { promise, resolve } }
const flush = async () => { for (let i = 0; i < 12; i++) { await Promise.resolve(); await Vue.nextTick() } }
const renderer = Vue.createRenderer({ createElement: () => ({ children: [] }), createText: () => ({}), createComment: () => ({}), insert(child, parent) { parent.children.push(child) }, remove() {}, setText() {}, setElementText() {}, parentNode: () => null, nextSibling: () => null, patchProp() {} })
const policy = { disasterCardIds: [...Array.from({ length: 8 }, (_, i) => `S01-DS0${i + 1}`), 'S01-DS10'] }
const cases = []
async function test(name, run) { try { await run(); cases.push({ name, passed: true }) } catch (error) { cases.push({ name, passed: false, error: error.message.split('\n').slice(0, 3).join(' ') }) } }
function fixture(options = {}) {
  let version = 20, policyCalls = 0, creations = 0
  const calls = [], summaryCalls = [], store = new Map(), emitted = []
  const platformState = Vue.reactive({ token: 'synthetic-session', account: { id: 'synthetic-owner' } })
  const route = Vue.reactive({ fullPath: '/battle/tournaments?section=create' })
  const api = {
    summaries: async query => { summaryCalls.push(query); if (options.contextWait) await options.contextWait.promise; return { platformVersion: version, items: [], page: 1, pageSize: 1, total: 0, totalPages: 0 } },
    create: async (input, expectedVersion, dryRun = false) => {
      calls.push({ input: JSON.parse(JSON.stringify(input)), expectedVersion, dryRun })
      if (expectedVersion !== version) throw new Error('资源版本已变化，请刷新后重试')
      if (dryRun && options.previewWait) await options.previewWait.promise
      if (dryRun && options.conflictAfterPreview) version++
      if (!dryRun) { creations++; version++ }
      return { id: dryRun ? 'synthetic-preview' : 'synthetic-created', version: 1, code: 'SYNTHETIC' }
    },
  }
  api.createContext = () => api.summaries({ section: 'host', page: 1, pageSize: 1 })
  const imports = {
    '../horizontalScrollArrows': { vHorizontalScrollArrows: {} },
    vue: Vue,
    'vue-router': { useRoute: () => route },
    '@/l12/platform': { platformState, tournamentApi: api, friendApi: { friends: async () => [] }, getEffectiveOperationsPolicy: async () => { policyCalls++; if (policyCalls > 1 && options.policyWait) await options.policyWait.promise; return policy } },
  }
  const modules = new Map()
  const require = id => {
    if (id === './platform') return imports['@/l12/platform']
    if (id in imports) return imports[id]
    const relative = id.replace(/^@\/l12\//, '').replace(/^\.\//, '')
    if (!/^(tournamentCreatePolicy|tournamentCreateFlow|tournamentTime)$/.test(relative)) throw new Error(`Unexpected import ${id}`)
    if (modules.has(relative)) return modules.get(relative)
    const module = { exports: {} }; modules.set(relative, module.exports)
    const source = readFileSync(new URL(`../src/l12/${relative}.ts`, import.meta.url), 'utf8')
    vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { module, exports: module.exports, require, Date, Error, JSON, Number, String, Object, Set, console }, { filename: relative })
    return module.exports
  }
  const localStorage = { getItem: key => store.get(key) ?? null, setItem: (key, value) => store.set(key, String(value)), removeItem: key => store.delete(key) }
  const source = process.env.L12_TOURNAMENT_CREATE_BASELINE === '1'
    ? execFileSync('git', ['-c', 'safe.directory=D:/GPT/Legion12/worktrees/lubu-pingyang-20261001', '-C', 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001', 'show', '1160e7cd:opcgpro-vue/src/l12/site/TournamentCreateWizard.vue'], { encoding: 'utf8' })
    : readFileSync(new URL('../src/l12/site/TournamentCreateWizard.vue', import.meta.url), 'utf8')
  const compiled = compileScript(parse(source).descriptor, { id: 'tournament-create-runtime' })
  const module = { exports: {} }
  vm.runInNewContext(ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { module, exports: module.exports, require, localStorage, window: { location: { reload() {} } }, Date, Error, JSON, Number, String, Object, Set, console }, { filename: 'TournamentCreateWizard.vue' })
  module.exports.default.render = () => null
  const vnode = Vue.h(module.exports.default, { platformVersion: options.oldVersion ?? 0, onCreated: value => emitted.push(value) })
  const root = { children: [] }; renderer.render(vnode, root)
  return { state: vnode.component.setupState, calls, summaryCalls, store, emitted, platformState, route, options, creations: () => creations, unmount: () => renderer.render(null, root) }
}
async function prepared(options) { const f = fixture(options); await flush(); f.state.form.name = '原始草稿'; await flush(); return f }

await test('direct create route with version zero creates using one fresh context', async () => {
  const f = await prepared({ oldVersion: 0 })
  try { await f.state.create(); assert.equal(f.creations(), 1); assert.equal(f.emitted.length, 1); assert.deepEqual(f.calls.map(c => c.expectedVersion), [20, 20]); assert.equal(f.summaryCalls.length, 1); assert.equal(f.summaryCalls[0].pageSize, 1); assert.deepEqual(f.calls[0].input, f.calls[1].input) } finally { f.unmount() }
})
await test('stale page version is not reused', async () => {
  const f = await prepared({ oldVersion: 7 })
  try { await f.state.create(); assert.equal(f.creations(), 1); assert.deepEqual(f.calls.map(c => c.expectedVersion), [20, 20]) } finally { f.unmount() }
})
await test('real conflict keeps draft, never auto-retries and permits an explicit fresh attempt', async () => {
  const f = await prepared({ oldVersion: 20, conflictAfterPreview: true })
  try { await f.state.create(); assert.equal(f.creations(), 0); assert.equal(f.calls.length, 2); assert.equal(f.state.form.name, '原始草稿'); assert(f.store.has('l12-tournament-create-draft-v3')); assert.equal(f.state.busy, false); f.options.conflictAfterPreview = false; await f.state.create(); assert.equal(f.creations(), 1); assert.equal(f.calls.length, 4) } finally { f.unmount() }
})
await test('synchronous double click admits only one creation flow', async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, policyWait: wait })
  try { const first = f.state.create(), second = f.state.create(); await flush(); wait.resolve(); await Promise.all([first, second]); assert.equal(f.creations(), 1); assert.equal(f.calls.length, 2) } finally { f.unmount() }
})
await test('unmount while policy is pending sends no preview or commit', async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, policyWait: wait })
  const pending = f.state.create(); await flush(); f.unmount(); wait.resolve(); await pending; assert.equal(f.calls.length, 0)
})
await test('account change during context read cannot submit for either account', async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, contextWait: wait })
  try { const pending = f.state.create(); await flush(); f.platformState.token = 'another-session'; f.platformState.account = { id: 'another-owner' }; wait.resolve(); await pending; assert.equal(f.calls.length, 0); assert.equal(f.emitted.length, 0) } finally { f.unmount() }
})
await test('input is frozen before async reads and a newer draft is retained', async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, policyWait: wait })
  try { const pending = f.state.create(); await flush(); f.state.form.name = '后来编辑的新草稿'; await flush(); wait.resolve(); await pending; assert.equal(f.calls[0].input.name, '原始草稿'); assert.equal(f.creations(), 1); assert(f.store.get('l12-tournament-create-draft-v3')?.includes('后来编辑的新草稿')) } finally { f.unmount() }
})
for (const phase of ['context', 'preview']) for (const aba of [false, true]) await test(`same-path route ${aba ? 'ABA' : 'change'} during ${phase} stops the old creation`, async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, [phase === 'context' ? 'contextWait' : 'previewWait']: wait })
  try {
    const pending = f.state.create(); await flush()
    const original = f.route.fullPath
    f.route.fullPath = '/battle/tournaments?section=create#another-step'
    if (aba) f.route.fullPath = original
    wait.resolve(); await pending
    assert.equal(f.calls.length, phase === 'context' ? 0 : 1)
    assert.equal(f.creations(), 0); assert.equal(f.emitted.length, 0)
    assert(f.store.has('l12-tournament-create-draft-v3'))
  } finally { f.unmount() }
})
await test('same-token account ABA during context read cannot revive creation', async () => {
  const wait = deferred(), f = await prepared({ oldVersion: 20, contextWait: wait })
  try {
    const pending = f.state.create(); await flush()
    const original = f.platformState.account
    f.platformState.account = { id: 'another-owner' }; f.platformState.account = original
    wait.resolve(); await pending; assert.equal(f.calls.length, 0); assert.equal(f.emitted.length, 0)
  } finally { f.unmount() }
})
console.log(JSON.stringify({ suite: 'real tournament creation script-setup', passed: cases.filter(c => c.passed).length, total: cases.length, cases }, null, 2))
assert(cases.every(c => c.passed), 'Tournament creation regressions failed')
