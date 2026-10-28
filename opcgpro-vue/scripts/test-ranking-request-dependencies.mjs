import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const source = readFileSync(new URL('../src/l12/site/RankingsPage.vue', import.meta.url), 'utf8')

// Actual Vue lifecycle, watchers and compiled template events in an in-memory
// host. No browser/layout, real HTTP bytes or server performance claim.
const find = (node, test) => [...(test(node) ? [node] : []), ...node.children.flatMap(child => find(child, test))]
const text = node => node.tag === '#comment' ? '' : node.text + node.children.map(text).join('')
function element(tag, value = '') {
  return { tag, text: value, children: [], props: {}, parent: null, value: '', listeners: new Map(), tagName: tag.toUpperCase(),
    get options() { return find(this, n => n.tag === 'option') }, addEventListener(k, v) { this.listeners.set(k, v) }, removeEventListener(k) { this.listeners.delete(k) } }
}
function detach(node) { if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1); node.parent = null }
const renderer = Vue.createRenderer({
  createElement: tag => element(tag), createText: value => element('#text', value), createComment: value => element('#comment', value),
  insert(node, parent, anchor = null) { detach(node); node.parent = parent; const at = anchor ? parent.children.indexOf(anchor) : -1; if (at < 0) parent.children.push(node); else parent.children.splice(at, 0, node) },
  remove: detach, parentNode: n => n.parent, nextSibling: n => n.parent?.children[n.parent.children.indexOf(n) + 1] ?? null,
  setText: (n, v) => { n.text = v }, setElementText(n, v) { n.children.forEach(c => { c.parent = null }); n.children = []; n.text = v },
  patchProp(n, k, before, v) { n.props[k] = v; if (k === 'value') { n.value = v; n._value = v } }, setScopeId() {},
})
const flush = async () => { for (let i = 0; i < 12; i++) { await Promise.resolve(); await Vue.nextTick() } }
const pending = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const response = (username = 'current', range = 'season') => ({ players: [{ rank: 1, username, faction: 'order', tier: '青铜', titles: [], games: 1, wins: 1, losses: 0, winRate: 100 }], analytics: { range, masters: [], matchups: [], summary: { matches: 1, placedPlayers: 1, activeMasters: 0 } } })
function fixture(options = {}, input = source) {
  const root = element('root'), calls = [], warnings = [], timers = new Map(), visibility = new Map()
  const document = { hidden: false, addEventListener: (k, v) => visibility.set(k, v), removeEventListener: k => visibility.delete(k) }
  const stub = { __esModule: true, default: { render: () => Vue.h('span') } }
  const imports = { vue: Vue, '../horizontalScrollArrows': { vHorizontalScrollArrows: {} }, '@/l12/specialAssets': { masterProfileUrl: id => `synthetic:${id}` },
    '@/l12/RankedIdentityBadge.vue': stub, './RankedMasterTitleRulesModal.vue': stub, './MasterMatchupMatrix.vue': stub, './StatisticsScope.vue': stub,
    '@/l12/platform': { platformState: Vue.reactive({ account: null }), rankedApi: {
      leaderboard(faction, range) { calls.push({ endpoint: 'leaderboard', faction, range }); return options.leaderboard?.(calls.filter(c => c.endpoint === 'leaderboard').length - 1) ?? Promise.resolve(response('current', range)) },
      history() { calls.push({ endpoint: 'history' }); return options.history?.() ?? Promise.resolve({ honors: [], factionTotals: [{ seasonName: '已结算赛季', factions: [] }] }) },
    } } }
  function evaluate(code) {
    const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
    const module = { exports: {} }
    const timer = fn => { timers.set(timers.size + 1, fn); return timers.size }
    new Function('require', 'module', 'exports', 'document', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', output)(
      id => { assert.ok(Object.hasOwn(imports, id), `Unexpected dependency ${id}`); return imports[id] }, module, module.exports, document, timer, id => timers.delete(id), timer, id => timers.delete(id))
    return module.exports
  }
  const descriptor = parse(input, { filename: 'RankingsPage.vue' }).descriptor
  const script = compileScript(descriptor, { id: 'ranking-requests' })
  const template = compileTemplate({ id: 'ranking-requests', filename: 'RankingsPage.vue', source: descriptor.template.content, compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
  assert.deepEqual(template.errors, [])
  const component = evaluate(script.content).default; component.render = evaluate(template.code).render
  const app = renderer.createApp(component); app.config.warnHandler = warning => warnings.push(warning)
  const vm = app.mount(root)
  return { root, calls, state: vm.$.setupState, timers, visibility, document, count: endpoint => calls.filter(c => c.endpoint === endpoint).length,
    async click(label) { const button = find(root, n => n.tag === 'button' && text(n) === label)[0]; assert.ok(button, `Rendered button ${label}`); assert.ok(!button.props.disabled, `Enabled button ${label}`); button.props.onClick(); await flush() },
    close() { app.unmount(); assert.deepEqual(warnings, []) } }
}
let count = 0
async function test(name, run) { await run(); count++; console.log('PASS', name) }
await test('mounted players do not fetch hidden history', async () => {
  const f = fixture(); try { await flush(); assert.equal(f.count('leaderboard'), 1); assert.equal(f.count('history'), 0) } finally { f.close() }
})
await test('explicit project, faction, range and manual reads retain leaderboard freshness', async () => {
  const f = fixture(); try { await flush(); for (const label of ['秩序', '近7天', '主宰榜', '对阵一览', '历史荣誉', '刷新数据', '玩家榜', '刷新数据']) await f.click(label)
    assert.equal(f.count('leaderboard'), 9); assert.equal(f.count('history'), 2)
    assert.deepEqual(f.calls.filter(c => c.endpoint === 'leaderboard').at(-1), { endpoint: 'leaderboard', faction: 'order', range: '7d' })
  } finally { f.close() }
})
await test('history reentry and manual refresh always read both dependencies', async () => {
  const f = fixture(); try { await flush(); for (const label of ['历史荣誉', '玩家榜', '历史荣誉', '刷新数据']) await f.click(label)
    assert.equal(f.count('leaderboard'), 5); assert.equal(f.count('history'), 3)
    assert.ok(find(f.root, n => String(n.props.class).split(' ').includes('summary-strip')).length, 'History still displays analytics summary')
  } finally { f.close() }
})
await test('rapid changes preserve one flight and only fetch the latest project dependencies', async () => {
  const old = pending(), f = fixture({ leaderboard: index => index === 0 ? old.promise : Promise.resolve(response('latest')) })
  try { await flush(); for (const label of ['主宰榜', '对阵一览', '历史荣誉']) await f.click(label)
    assert.equal(f.count('leaderboard'), 1); assert.equal(f.count('history'), 0)
    old.resolve(response('old')); await flush(); assert.equal(f.count('leaderboard'), 2); assert.equal(f.count('history'), 1); assert.equal(f.state.players[0].username, 'latest')
  } finally { old.resolve(response()); f.close(); await flush() }
})
await test('ABA through history ignores stale players and never fetches intermediate history', async () => {
  const old = pending(), f = fixture({ leaderboard: index => index === 0 ? old.promise : Promise.resolve(response('latest')) })
  try { await flush(); await f.click('历史荣誉'); await f.click('玩家榜'); old.resolve(response('stale')); await flush()
    assert.equal(f.count('leaderboard'), 2); assert.equal(f.count('history'), 0); assert.equal(f.state.players[0].username, 'latest')
  } finally { old.resolve(response()); f.close(); await flush() }
})
await test('unused history failure cannot break other projects', async () => {
  const f = fixture({ history: () => Promise.reject(new Error('history unavailable')) })
  try { await flush(); assert.equal(f.count('history'), 0); assert.equal(f.state.players[0].username, 'current'); assert.equal(f.state.error, '') } finally { f.close() }
})
await test('visible history failure preserves snapshot until explicit retry', async () => {
  let fails = true
  const f = fixture({ history: () => fails ? Promise.reject(new Error('history unavailable')) : Promise.resolve({ honors: [], factionTotals: [] }) })
  try { await flush(); await f.click('历史荣誉'); assert.equal(f.state.error, 'history unavailable'); assert.equal(f.state.players[0].username, 'current')
    assert.equal(f.count('leaderboard'), 2); assert.equal(f.count('history'), 1); assert.equal(f.timers.size, 0)
    fails = false; await f.click('刷新数据'); assert.equal(f.count('leaderboard'), 3); assert.equal(f.count('history'), 2); assert.equal(f.state.error, '')
  } finally { f.close() }
})
await test('stale group errors cannot replace the latest project', async () => {
  const old = pending(), f = fixture({ leaderboard: index => index === 0 ? old.promise : Promise.resolve(response('latest')) })
  try { await flush(); await f.click('主宰榜'); old.reject(new Error('stale failure')); await flush(); assert.equal(f.state.error, ''); assert.equal(f.state.players[0].username, 'latest') }
  finally { old.resolve(response()); f.close(); await flush() }
})
await test('local controls and visibility do not fetch; unmounted results do not repaint', async () => {
  const f = fixture(); try { await flush(); await f.click('主宰榜')
    const input = find(f.root, n => n.tag === 'input')[0]
    input.value = '查询'; input.props['onUpdate:modelValue']('查询')
    find(f.root, n => n.tag === 'select')[0].props['onUpdate:modelValue']('winRate')
    await f.click('最强称号规则'); f.document.hidden = true; f.visibility.get('visibilitychange')?.(); f.document.hidden = false; f.visibility.get('visibilitychange')?.(); await flush()
    assert.equal(f.count('leaderboard'), 2); assert.equal(f.count('history'), 0); assert.equal(f.timers.size, 0)
  } finally { f.close() }
  const hold = pending(), late = fixture({ leaderboard: () => hold.promise }); await flush(); late.close(); hold.resolve(response('unmounted')); await flush()
  assert.deepEqual(late.state.players, []); assert.equal(late.state.hasAnalytics, false); assert.equal(late.count('leaderboard'), 1)
})
const historyBranch = "requestedTab === 'history' ? rankedApi.history() : Promise.resolve(null)"
assert.ok(source.includes(historyBranch), 'Dependency mutation anchor exists')
for (const [name, mutated, labels, historyCount, leaderboardCount] of [
  ['unconditional hidden history', source.replace(historyBranch, 'rankedApi.history()'), [], 0, 1],
  ['missing tab refresh', source.replace('watch([faction, range, tab], load)', 'watch([faction, range], load)'), ['主宰榜'], 0, 2],
  ['missing visible history', source.replace(historyBranch, 'Promise.resolve(null)'), ['历史荣誉'], 1, 2],
  ['settled history cache', source.replace('rankedApi.history()', '(history.value.factionTotals.length ? Promise.resolve(history.value) : rankedApi.history())'), ['历史荣誉', '玩家榜', '历史荣誉'], 2, 4],
]) {
  await test(`counter guard rejects ${name}`, async () => {
    assert.notEqual(mutated, source, `Mutation applies: ${name}`)
    await assert.rejects(async () => {
      const f = fixture({}, mutated)
      try { await flush(); for (const label of labels) await f.click(label); assert.equal(f.count('leaderboard'), leaderboardCount); assert.equal(f.count('history'), historyCount) }
      finally { f.close() }
    }, assert.AssertionError)
  })
}
console.log(`Ranking request dependencies: ${count}/${count} actual SFC scenarios and negative controls passed`)
