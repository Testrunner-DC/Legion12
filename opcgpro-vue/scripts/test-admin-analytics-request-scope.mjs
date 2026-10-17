import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import vm from 'node:vm'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as Vue from 'vue'

// Execute the real script-setup with Vue's reactivity/lifecycle, without a DOM,
// a server, copied request logic, or any real analytics/session data.
const require = createRequire(import.meta.url)
const catalog = ['A', 'B'].map(id => ({ id, number: id, nameZh: `主宰${id}`, cardType: 'master' }))
catalog.push({ id: 'C', number: 'C', nameZh: '合成军团', cardType: 'legion' })
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const flush = async () => { for (let i = 0; i < 5; i++) { await Promise.resolve(); await Vue.nextTick() } }
const reportFor = id => ({ selectedMasterId: id || null, items: catalog.filter(c => c.cardType === 'master').map(c => ({ masterId: c.id, participantSamples: 31 })), matchups: [], trend: [], popularDecks: id ? [{ signature: id, samples: 31, wins: 15, winRate: .5, cards: [] }] : [] })
const renderer = Vue.createRenderer({
  createElement: () => ({ children: [] }), createText: () => ({}), createComment: () => ({}),
  insert(child, parent) { parent.children.push(child) }, remove() {}, setText() {}, setElementText() {},
  parentNode: () => null, nextSibling: () => null, patchProp() {},
})
function mount(file, overrides = {}, props = {}) {
  const source = readFileSync(new URL(`../src/l12/${file}`, import.meta.url), 'utf8')
  const { descriptor } = parse(source)
  const script = compileScript(descriptor, { id: file })
  const code = ts.transpileModule(script.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const notices = []
  const platform = {
    adminApi: {}, getEffectiveOperationsPolicy: async () => ({ season: { id: 'S1', name: '合成赛季' } }),
    platformState: Vue.reactive({ token: 'fixture-admin-token', account: {
      id: 'fixture-admin', username: '分析夹具管理员', role: 'admin', createdAt: '2026-01-01T00:00:00Z', publicHistory: false,
      permissions: ['admin.analytics.read'], permissionVersion: 1,
    } }),
    authState: Vue.reactive({ initialized: true, verified: true, refreshing: false }),
    ...(overrides['@/l12/platform'] || {}),
  }
  const imports = {
    vue: Vue,
    '@/l12/decks': { loadDeckCatalog: async () => catalog }, './decks': { loadDeckCatalog: async () => catalog, filterableCardCost: c => c.cost ?? null },
    '@/l12/specialAssets': { masterProfileUrl: id => id }, './mobileViewport': { landscapeTeleportTarget: () => 'body' },
    './cardPresentation': { cardTypeFilterKey: value => value, cardTypeLabel: value => value, isHorizontalCardType: () => false },
    ...overrides,
    '@/l12/platform': platform,
  }
  vm.runInNewContext(code, { module, exports: module.exports, require: id => imports[id] ?? (id.endsWith('.vue') ? { default: {} } : require(id)), Date, Map, Set, Error, console }, { filename: file })
  const component = module.exports.default
  component.render = () => null
  const vnode = Vue.h(component, { ...props, onNotice: message => notices.push(message) })
  const root = { children: [] }
  renderer.render(vnode, root)
  return { state: vnode.component.setupState, notices, source, unmount: () => renderer.render(null, root) }
}
async function masterFixture() {
  const calls = []
  const fixture = mount('site/AdminMasterAnalyticsPanel.vue', { '@/l12/platform': {
    adminApi: { masterAnalytics: query => { const call = { query, ...deferred() }; calls.push(call); return call.promise } },
    getEffectiveOperationsPolicy: async () => ({ season: { id: 'S1', name: '合成赛季' } }),
  } })
  await flush()
  assert.equal(calls.length, 1)
  calls[0].resolve(reportFor('')); await flush()
  return { ...fixture, calls }
}
const cases = []
async function test(name, run) { try { await run(); cases.push({ name, passed: true }) } catch (error) { cases.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) } }

await test('master A→B→B reply→A reply keeps B report and drilldown', async () => {
  const f = await masterFixture()
  try {
    f.state.chooseMaster('A'); f.state.chooseMaster('B')
    f.calls[2].resolve(reportFor('B')); await flush()
    if (f.state.choosePopularDeck) f.state.choosePopularDeck(f.state.report.popularDecks[0])
    else f.state.popularDeck = f.state.report.popularDecks[0]
    f.calls[1].resolve(reportFor('A')); await flush()
    assert.equal(f.state.report.selectedMasterId, 'B')
    assert.equal(f.state.appliedScope.masterId, 'B')
    assert.equal(f.state.popularDeck.signature, 'B')
    f.state.choosePopularDeck(reportFor('A').popularDecks[0])
    assert.equal(f.state.popularDeck.signature, 'B', 'stale drilldown object must be ignored')
  } finally { f.unmount() }
})
await test('old success/failure/finally cannot clear current loading or emit an error', async () => {
  const f = await masterFixture()
  try {
    f.state.chooseMaster('A'); f.state.chooseMaster('B')
    f.calls[1].resolve(reportFor('A')); await flush()
    assert.equal(f.state.loading, true)
    assert.equal(f.notices.length, 0)
    assert.equal(f.state.report, null)
    f.state.chooseMaster('A'); f.state.chooseMaster('B')
    f.calls[3].reject(new Error('old failure')); await flush()
    assert.equal(f.state.loading, true)
    assert.equal(f.notices.length, 0)
    assert.equal(f.state.report, null)
    f.calls[2].resolve(reportFor('B')); await flush()
    assert.equal(f.state.loading, true, 'older B response cannot revive an ABA scope')
    assert.equal(f.state.report, null)
    f.calls[4].reject(new Error('current failure')); await flush()
    assert.equal(f.state.loading, false)
    assert.equal(f.state.report, null)
    assert.equal(f.state.appliedScope, null)
    assert.deepEqual(f.notices, ['current failure'])
  } finally { f.unmount() }
})
await test('complete scope invalidates pending replies including ABA without another request', async () => {
  for (const change of [s => { s.from = '2026-01-01' }, s => { s.to = '2026-01-31' }, s => { s.sort = 'win-rate' }, s => { s.range = 'season' }, s => { s.effectVersion = 'all' }, s => { s.currentSeason = { id: 'S2', name: '新赛季' } }, s => { s.selectedMasterId = 'B'; s.selectedMasterId = 'A' }]) {
    const f = await masterFixture()
    try {
      f.state.chooseMaster('A'); change(f.state)
      f.calls[1].resolve(reportFor('A')); await flush()
      assert.equal(f.state.report, null)
      assert.equal(f.state.appliedScope, null)
      assert.equal(f.state.loading, false)
    } finally { f.unmount() }
  }
})
await test('unmount invalidates pending request and late mounted bootstrap', async () => {
  const f = await masterFixture()
  f.state.chooseMaster('A'); f.unmount(); f.calls[1].reject(new Error('unmounted')); await flush()
  assert.equal(f.notices.length, 0)
  assert.equal(f.state.report, null)
  const gate = deferred(); let requests = 0
  const late = mount('site/AdminMasterAnalyticsPanel.vue', { '@/l12/platform': { adminApi: { masterAnalytics: async () => { requests++; return reportFor('') } }, getEffectiveOperationsPolicy: () => gate.promise } })
  late.unmount(); gate.resolve({ season: { id: 'S1', name: 'late' } }); await flush()
  assert.equal(requests, 0)
})
await test('master current/all query, truthful sample scope, response selected-master binding', async () => {
  const f = await masterFixture()
  try {
    assert.match(f.source, /v-model="effectVersion"[\s\S]*value="current"[\s\S]*value="all"/)
    f.state.effectVersion = 'all'; f.state.chooseMaster('B')
    assert.equal(f.calls[1].query.effectVersion, 'all')
    f.calls[1].resolve(reportFor('A')); await flush()
    assert.equal(f.state.selected, undefined, 'mismatched response cannot open another master drilldown')
    assert.equal(f.state.report, null)
    assert.equal(f.state.appliedScope, null)
    f.state.chooseMaster('B'); f.calls[2].resolve(reportFor('B')); await flush()
    assert.match(f.state.masterScopeSample, /62 份参赛方样本/)
    assert.match(f.state.masterScopeItems.join(' '), /引擎构建.*卡池/)
    assert.match(f.state.masterScopeItems.join(' '), /跨版本.*观察/)
    assert.match(f.source, /participantSamples >= 30/)
  } finally { f.unmount() }
})
await test('two master-only picker entries preserve all/clear, filters, and full analysis catalog', async () => {
  const f = mount('site/AdminCardAnalyticsPanel.vue'); await flush()
  try {
    assert.match(f.source, /v-if="masterPickerTarget"[^>]*:allowed-types="\['master'\]"/)
    assert.doesNotMatch(f.source, /<select v-model="filters\.(masterId|opponentMasterId)"/)
    assert.equal(f.state.masterPickerItems.length, 2)
    for (const field of ['masterId', 'opponentMasterId']) {
      f.state.masterPickerTarget = field
      f.state.chooseMasterFilter({ id: 'C', cardType: 'legion' })
      assert.equal(f.state.filters[field], '')
      f.state.chooseMasterFilter(f.state.masterPickerItems[0])
      assert.equal(f.state.filters[field], 'A')
      assert.equal(f.state.masterPickerTarget, null)
    }
    f.state.clearMasterFilter('masterId')
    assert.equal(f.state.filters.masterId, '')
    assert.equal(f.state.filters.opponentMasterId, 'A')
    assert.equal(f.state.selectedCardId, '')
    const analysisTag = f.source.match(/<SingleCardPicker v-if="pickerOpen"[^>]*>/)?.[0]
    assert.ok(analysisTag); assert.doesNotMatch(analysisTag, /allowed-types|:items/)
    const picker = mount('SingleCardPicker.vue', {}, { items: f.state.masterPickerItems, allowedTypes: ['master'] })
    try { assert.equal(picker.state.available.length, 2); picker.state.query = '主宰B'; assert.equal(picker.state.filtered[0].id, 'B') } finally { picker.unmount() }
  } finally { f.unmount() }
})
for (const result of cases) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
assert.equal(cases.filter(result => !result.passed).length, 0, `${cases.length} real analytics component cases`)
