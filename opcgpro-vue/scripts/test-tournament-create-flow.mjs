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
  const platformState = Vue.reactive({ token: 'synthetic-session', account: { id: 'synthetic-owner', permissions: options.admin ? ['tournaments.manage'] : [] } })
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
    '@/l12/platform': { platformState, tournamentApi: api, friendApi: { friends: async () => [] }, hasPermission: permission => platformState.account?.permissions?.includes(permission) === true, getEffectiveOperationsPolicy: async () => { policyCalls++; if (options.failInitialPolicy && policyCalls === 1) throw new Error('synthetic policy failure'); if (policyCalls > 1 && options.policyWait) await options.policyWait.promise; return policy } },
    '@/l12/decks': { loadDeckCatalog: async () => [] },
    './ConstructionRuleEditor.vue': { default: {} },
    './DisasterPoolPicker.vue': { default: {} },
    './RulesCardSummary.vue': { default: {} },
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
  if (options.draft) store.set('l12-tournament-create-draft-v4', JSON.stringify(options.draft))
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
  try { await f.state.create(); assert.equal(f.creations(), 0); assert.equal(f.calls.length, 2); assert.equal(f.state.form.name, '原始草稿'); assert(f.store.has('l12-tournament-create-draft-v4')); assert.equal(f.state.busy, false); f.options.conflictAfterPreview = false; await f.state.create(); assert.equal(f.creations(), 1); assert.equal(f.calls.length, 4) } finally { f.unmount() }
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
  try { const pending = f.state.create(); await flush(); f.state.form.name = '后来编辑的新草稿'; await flush(); wait.resolve(); await pending; assert.equal(f.calls[0].input.name, '原始草稿'); assert.equal(f.creations(), 1); assert(f.store.get('l12-tournament-create-draft-v4')?.includes('后来编辑的新草稿')) } finally { f.unmount() }
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
    assert(f.store.has('l12-tournament-create-draft-v4'))
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
await test('automatic mode is admin-only and keeps the approved timing defaults', async () => {
  const player = await prepared({ oldVersion: 20 })
  try {
    player.state.form.operationMode = 'automated'
    player.state.form.startAt = '2026-11-01T20:00'
    await player.state.create()
    assert.equal(player.calls.length, 0)
    assert.equal(player.creations(), 0)
    assert.match(player.state.notice, /无权创建自动运行赛事/)
  } finally { player.unmount() }

  const admin = await prepared({ oldVersion: 20, admin: true })
  try {
    admin.state.form.operationMode = 'automated'
    admin.state.form.startAt = '2026-11-01T20:00'
    await admin.state.create()
    assert.equal(admin.creations(), 1)
    assert.equal(admin.calls[0].input.operationMode, 'automated')
    assert.equal(admin.calls[0].input.intermissionMinutes, 3)
    assert.equal(admin.calls[0].input.lateGraceMinutes, 5)
  } finally { admin.unmount() }
})
await test('explicit tournament disaster pool survives preview and commit instead of reverting to operations defaults', async () => {
  const f = await prepared({ oldVersion: 20 })
  try {
    const selected = [...policy.disasterCardIds]
    selected[0] = 'S02-DS01'
    f.state.form.disasterCardIds = selected
    await f.state.create()
    assert.equal(f.creations(), 1)
    assert.deepEqual(f.calls[0].input.disasterCardIds, selected)
    assert.deepEqual(f.calls[1].input.disasterCardIds, selected)
    assert.notDeepEqual(f.calls[0].input.disasterCardIds, policy.disasterCardIds)
  } finally { f.unmount() }
})
await test('restored custom pool survives policy hydration', async () => {
  const selected = [...policy.disasterCardIds]; selected[0] = 'S02-DS01'
  const f = fixture({ draft: { disasterMode: 'all', disasterCardIds: selected } })
  try { await flush(); assert.deepEqual([...f.state.form.disasterCardIds], selected) } finally { f.unmount() }
})
await test('failed default-policy hydration keeps a restored pool and can retry creation safely', async () => {
  const selected = [...policy.disasterCardIds]; selected[0] = 'S02-DS01'
  const f = fixture({ failInitialPolicy: true, draft: { name: '恢复的赛事', disasterMode: 'all', disasterCardIds: selected } })
  try {
    await flush()
    assert.deepEqual([...f.state.form.disasterCardIds], selected)
    assert(f.state.policyError.includes('synthetic policy failure'))
    await f.state.create()
    assert.equal(f.creations(), 1)
    assert.deepEqual(f.calls[0].input.disasterCardIds, selected)
  } finally { f.unmount() }
})
await test('registration window is converted to UTC once and frozen across preview and commit', async () => {
  const wait = deferred(), f = await prepared({ policyWait: wait })
  try {
    f.state.registrationOpensLocal = '2026-12-01T09:30'
    f.state.registrationClosesLocal = '2026-12-02T10:00'
    await flush()
    const pending = f.state.create(); await flush()
    f.state.registrationOpensLocal = '2026-12-03T11:00'
    await flush(); wait.resolve(); await pending
    assert.equal(f.creations(), 1)
    for (const call of f.calls) {
      assert.equal(call.input.registrationOpensAt, new Date('2026-12-01T09:30').toISOString())
      assert.equal(call.input.registrationClosesAt, new Date('2026-12-02T10:00').toISOString())
    }
    assert.match(f.store.get('l12-tournament-create-draft-v4'), /2026-12-03T11:00/)
  } finally { f.unmount() }
})
await test('restored registration draft populates both actual controls and submitted UTC fields', async () => {
  const f = fixture({ draft: { name: '恢复报名窗口', registrationOpensAt: '2026-12-01T09:30', registrationClosesAt: '2026-12-02T10:00' } })
  try {
    await flush()
    assert.equal(f.state.registrationOpensLocal, '2026-12-01T09:30')
    assert.equal(f.state.registrationClosesLocal, '2026-12-02T10:00')
    await f.state.create()
    assert.equal(f.creations(), 1)
    assert.equal(f.calls[1].input.registrationOpensAt, new Date('2026-12-01T09:30').toISOString())
    assert.equal(f.calls[1].input.registrationClosesAt, new Date('2026-12-02T10:00').toISOString())
  } finally { f.unmount() }
})
await test('clearing either registration control sends null instead of a previous draft date', async () => {
  const f = fixture({ draft: { name: '清除报名窗口', registrationOpensAt: '2026-12-01T09:30', registrationClosesAt: '2026-12-02T10:00' } })
  try {
    await flush(); f.state.registrationOpensLocal = ''; f.state.registrationClosesLocal = ''; await flush()
    await f.state.create()
    assert.equal(f.creations(), 1)
    assert.equal(f.calls[1].input.registrationOpensAt, null)
    assert.equal(f.calls[1].input.registrationClosesAt, null)
  } finally { f.unmount() }
})
await test('management preserves untouched timestamp precision while editing the other boundary', async () => {
  const original = { id: 'window-event', version: 7, organizerAccountId: 'owner', referees: [],
    registrationOpensAt: '2026-12-01T01:30:45.1234567Z', registrationClosesAt: '2026-12-02T02:00:59.7654321Z' }
  const calls = []
  const source = readFileSync(new URL('../src/l12/site/TournamentManagementPanel.vue', import.meta.url), 'utf8')
  const compiled = compileScript(parse(source).descriptor, { id: 'tournament-registration-management-runtime' })
  const module = { exports: {} }
  const imports = { vue: Vue, 'vue-router': { useRoute: () => Vue.reactive({ fullPath: '/battle/tournaments/window-event' }) }, './TournamentAnnouncement.vue': { default: {} }, '@/l12/tournamentLabels': { tournamentAutomationStatusText: value => value },
    '@/l12/platform': { platformState: Vue.reactive({ token: 'session', account: { id: 'owner' } }), friendApi: { friends: async () => [] }, tournamentApi: { setRegistrationWindow: async (...args) => { calls.push(args); return original } } } }
  vm.runInNewContext(ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require: id => { assert(id in imports); return imports[id] }, Date, Error, Map, console })
  module.exports.default.render = () => null
  const vnode = Vue.h(module.exports.default, { tournament: original, accountId: 'owner', canOrganize: true })
  const root = { children: [] }; renderer.render(vnode, root)
  try {
    const state = vnode.component.setupState
    state.saveRegistrationWindow(); await flush()
    assert.equal(calls[0][2], original.registrationOpensAt)
    assert.equal(calls[0][3], original.registrationClosesAt)
    state.registrationClosesLocal = '2026-12-04T10:00'
    state.saveRegistrationWindow(); await flush()
    assert.equal(calls[1][2], original.registrationOpensAt)
    assert.equal(calls[1][3], new Date('2026-12-04T10:00').toISOString())
  } finally { renderer.render(null, root) }
})
console.log(JSON.stringify({ suite: 'real tournament creation script-setup', passed: cases.filter(c => c.passed).length, total: cases.length, cases }, null, 2))
assert(cases.every(c => c.passed), 'Tournament creation regressions failed')
