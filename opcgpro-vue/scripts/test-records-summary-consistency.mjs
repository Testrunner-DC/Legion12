import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import vm from 'node:vm'

// Bounded diagnosis/regression: actual SFC script/setup + template, actual replay model,
// controlled in-memory API/identity/router/timers. No browser or network.
const root = fileURLToPath(new URL('../', import.meta.url))
const require = createRequire(path.join(root, 'package.json'))
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const output = process.env.L12_QA_OUT
assert.ok(output && path.isAbsolute(output), 'Explicit absolute L12_QA_OUT required')
assert.ok(process.env.L12_PWSH && path.isAbsolute(process.env.L12_PWSH), 'Shared initializer must bind absolute L12_PWSH')
const quote = value => `'${value.replaceAll("'", "''")}'`
const guard = spawnSync(process.env.L12_PWSH, ['-NoProfile', '-NonInteractive', '-Command',
  `. ${quote(path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1'))}\n$null=Assert-L12GeneratedPath -Path ${quote(output)} -Purpose 'records summary actual SFC diagnosis'`],
{ encoding: 'utf8', windowsHide: true })
assert.equal(guard.status, 0, `Physical output guard failed: ${guard.stderr || guard.stdout}`)
fs.mkdirSync(output, { recursive: true })
const sourcePaths = {
  records: 'src/l12/MatchRecords.vue', replay: 'src/l12/ReplayPage.vue',
  model: 'src/l12/replayModel.ts', index: 'src/l12/replayIndex.ts',
  outcome: 'src/l12/replayOutcome.ts',
}
const bytes = Object.fromEntries(Object.entries(sourcePaths).map(([key, value]) => [key, fs.readFileSync(path.join(root, value))]))
const source = Object.fromEntries(Object.entries(bytes).map(([key, value]) => [key, value.toString('utf8')]))
const hash = value => createHash('sha256').update(value).digest('hex').toUpperCase()
const sourceHashes = Object.fromEntries(Object.entries(sourcePaths).map(([key, value]) => [value, hash(bytes[key])]))
const results = [], warnings = [], runtimeErrors = []
let mountedFixtures = 0, forbiddenNetworkCalls = 0

function evaluate(code, imports = {}, environment = {}, filename = 'actual-source.ts') {
  const module = { exports: {} }
  const compiled = ts.transpileModule(code, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
  vm.runInNewContext(compiled, {
    module, exports: module.exports,
    require: id => { assert.ok(Object.hasOwn(imports, id), `Unexpected dependency ${id}`); return imports[id] },
    Date, Intl, Error, TypeError, Map, Set, Promise, Math, JSON, Number, String, Object, Array,
    AbortController, DOMException, TextEncoder, console,
    fetch() { forbiddenNetworkCalls++; throw new Error('Network forbidden in synthetic diagnosis') },
    ...environment,
  }, { filename })
  return module.exports
}
const model = evaluate(source.model, {}, {}, sourcePaths.model)
const replayIndex = evaluate(source.index, {}, {}, sourcePaths.index)
const outcome = evaluate(source.outcome, {}, {}, sourcePaths.outcome)
function compileSfc(key) {
  const { descriptor, errors } = parse(source[key], { filename: sourcePaths[key] })
  assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: `records-consistency-${key}` })
  const template = compileTemplate({ source: descriptor.template.content, filename: sourcePaths[key], id: `records-consistency-${key}`, compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  return { script: script.content, render: evaluate(template.code, { vue: Vue }, {}, `actual-${key}-template`).render }
}
const compiled = { records: compileSfc('records'), replay: compileSfc('replay') }
function node(tag, text = '') { return { tag, text, children: [], parent: null, props: {}, value: '', tagName: tag.toUpperCase() } }
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
function descendants(rootNode, predicate) { return [...(predicate(rootNode) ? [rootNode] : []), ...rootNode.children.flatMap(child => descendants(child, predicate))] }
function text(rootNode) { return rootNode.tag === '#comment' ? '' : rootNode.text + rootNode.children.map(text).join('') }
const classes = element => String(element.props.class || '').split(/\s+/)
function surface(rootNode, className) { return descendants(rootNode, element => classes(element).includes(className)).map(text).join('\n') }
function createRenderer(teleports) {
  return Vue.createRenderer({
    createElement: tag => node(tag), createText: value => node('#text', value), createComment: value => node('#comment', value),
    insert(child, parent, anchor = null) { detach(child); child.parent = parent; const position = anchor ? parent.children.indexOf(anchor) : -1; if (position < 0) parent.children.push(child); else parent.children.splice(position, 0, child) },
    remove: detach, parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
    setText(child, value) { child.text = value }, setElementText(child, value) { for (const entry of [...child.children]) detach(entry); child.text = value },
    patchProp(child, name, previous, value) { child.props[name] = value; if (name === 'value') child.value = value },
    setScopeId() {}, querySelector: () => teleports,
  })
}
const flush = async () => { for (let i = 0; i < 20; i++) { await Promise.resolve(); await Vue.nextTick() } }
const nullComponent = { render: () => null }
class RequestError extends Error { constructor(status, code = 'synthetic') { super(`合成请求失败${status}`); this.status = status; this.code = code } }
const summary = (overrides = {}) => ({
  matchId: 'synthetic-record-A', roomCode: 'QA', player0: '合成甲', player1: '合成乙', deck0: '', deck1: '',
  startedUtc: '2026-10-22T00:00:00Z', endedUtc: '2026-10-22T00:02:03Z', winner: 0, error: null, finalHash: 'synthetic-final', commandCount: 1,
  ...overrides,
})
function detail(match, { phase = 'GameOver', winner = match.winner, agreedDraw = false } = {}) {
  const player = index => ({ PlayerIndex: index, Name: index === 0 ? match.player0 : match.player1, Faction: 'synthetic', MasterId: `synthetic-master-${index}`, Hp: 5, MaxHp: 10, Field: [[null,null,null],[null,null,null]], Hand: [], Morale: [], Library: [], Graveyard: [], SpecialZones: {} })
  return { match, viewerPlayerIndex: 0, commands: [{ sequence: 1, receivedUtc: match.endedUtc || match.startedUtc, playerIndex: 0, command: { type: 'synthetic-fixture' }, accepted: true, revision: 1, stateHash: 'synthetic-final', state: { MatchId: match.matchId, RoomCode: match.roomCode, Phase: phase, Round: 2, ActivePlayer: 0, FirstPlayer: 0, Players: [player(0),player(1)], Events: [], Winner: winner, EndedByAgreedDraw: agreedDraw } }] }
}
async function fixture(key, { match = summary(), replayDetail, json = false, selected = true, mobile = false } = {}) {
  const main = node('root'), teleports = node('teleports'), renderer = createRenderer(teleports)
  const route = Vue.reactive({ name: key === 'records' ? 'records' : json ? 'json-replay' : 'match-replay', params: { matchId: match.matchId }, query: selected ? { selected: match.matchId } : {} })
  const identity = Vue.reactive({ token: 'synthetic-token-A', account: { id: 'synthetic-account-A', role: 'player', permissionVersion: 1, disabled: false } })
  const auth = Vue.reactive({ verified: true })
  const store = { list: [match], failure: null, replay: replayDetail || detail(match) }
  const requests = [], navigation = [], downloads = [], timers = new Map(), listeners = new Map()
  let timerId = 0
  const window = { setTimeout(fn, ms) { const id = ++timerId; timers.set(id, { fn, ms }); return id }, clearTimeout: id => timers.delete(id), addEventListener: (name, callback) => listeners.set(name, callback), removeEventListener: name => listeners.delete(name) }
  const imports = {
    vue: Vue,
    'vue-router': { useRoute: () => route, useRouter: () => ({ push: value => navigation.push(value) }) },
    './platform': { platformState: identity, authState: auth, PlatformRequestError: RequestError, adminApi: {}, async platformRequest(requestPath) {
      requests.push(requestPath)
      if (store.read) return store.read(requestPath)
      if (store.failure) {
        if (store.failure.status === 401) { identity.token = ''; identity.account = null; auth.verified = false }
        throw store.failure
      }
      return requestPath.startsWith('/api/matches?') ? store.list : store.replay
    } },
    './replayModel': model,
    './replayOutcome': outcome,
    './mobileViewport': { isMobileDeviceExperience: () => mobile, landscapeTeleportTarget: () => 'body' },
    './site/VisibleDisasterSummary.vue': { __esModule: true, default: nullComponent },
    './game/GameBoard.vue': { __esModule: true, default: { props: ['game'], render() { return Vue.h('div', { class: 'actual-board-binding', 'data-phase': this.game.phase }, '只读棋盘替身') } } },
    './site/L12SettingsModal.vue': { __esModule: true, default: nullComponent },
    './site/bugFeedbackEntry': { closeSettingsAndRestore() {}, openBugFeedbackFromSettings() {}, rememberSettingsOpener() { return null } },
    './decks': { loadDeckCatalog: async () => [] },
    './replayIndex': replayIndex,
    './replayArchiveLoader': { ReplayArchiveLoader: class { constructor() { throw new Error('Admin archive branch outside this bounded player diagnosis') } } },
  }
  if (json) model.rememberImportedReplay(store.replay)
  const component = evaluate(compiled[key].script, imports, { window, Blob,
    URL: { createObjectURL: () => 'blob:synthetic-local', revokeObjectURL() {} },
    document: { getElementById: () => null, querySelector: () => null, createElement(tag) {
      assert.equal(tag, 'a')
      return { href: '', download: '', click() { downloads.push({ href: this.href, filename: this.download }) } }
    } },
  }, sourcePaths[key]).default
  component.render = compiled[key].render
  const app = renderer.createApp(component)
  app.config.errorHandler = error => runtimeErrors.push(error.stack || String(error))
  app.config.warnHandler = warning => warnings.push(warning)
  app.component('router-link', { props: ['to'], setup(props, context) { return () => Vue.h('a', { ...context.attrs, href: typeof props.to === 'string' ? props.to : '#' }, context.slots.default?.()) } })
  const instance = app.mount(main)
  mountedFixtures++
  await flush()
  const state = instance.$.setupState
  return { state, main, teleports, store, identity, auth, requests, navigation, downloads, route, timers, listeners,
    unmount() { app.unmount(); mountedFixtures--; if (json) model.rememberImportedReplay(null) },
  }
}
async function use(key, options, run) { const f = await fixture(key, options); try { return await run(f) } finally { f.unmount(); await flush() } }
async function test(id, name, run) {
  const observation = {}
  try { await run(observation); results.push({ id, name, status: 'passed', observation }) }
  catch (error) { results.push({ id, name, status: error.code === 'ERR_ASSERTION' ? 'failed' : 'harness-error', observation, error: error.stack || String(error) }) }
}

await test('CONTROL-01', 'actual list selection, winning label and valid UTC duration', observation => use('records', {}, f => {
  observation.selectedId = f.state.selected?.matchId
  observation.result = surface(f.main, 'record-summary-grid')
  assert.equal(f.state.selected.matchId, 'synthetic-record-A')
  assert.equal(f.state.resultLabel(f.state.selected), '合成甲 胜')
  assert.equal(f.state.durationLabel(f.state.selected), '2:03')
  assert.match(observation.result, /合成甲 胜/)
}))
await test('HR-01-SAME-ID', 'refresh same ID must reconcile selected payload availability', observation => use('records', {}, async f => {
  f.store.list = [summary({ commandCount: 0 })]
  await f.state.loadMatches(); await flush()
  observation.listCount = f.state.matches[0].commandCount
  observation.selectedCount = f.state.selected?.commandCount
  observation.playable = f.state.canUseSelectedReplay
  observation.detailText = surface(f.main, 'record-detail')
  assert.equal(f.state.selected?.commandCount, 0, 'detail must use the refreshed summary, not the old object')
  assert.equal(f.state.canUseSelectedReplay, false)
}))
await test('HR-01-REMOVED', 'refresh that removes selected match must clear stale detail', observation => use('records', {}, async f => {
  f.store.list = []
  await f.state.loadMatches(); await flush()
  observation.listLength = f.state.matches.length
  observation.selectedId = f.state.selected?.matchId
  observation.detailText = surface(f.main, 'record-detail')
  assert.equal(f.state.selected, null, 'missing match must not remain selected/playable')
  assert.equal(surface(f.main, 'record-detail'), '')
}))
for (const change of ['logout', 'account-switch', 'refresh-401']) {
  await test(`HR-02-${change.toUpperCase()}`, 'already rendered player history must clear on ' + change, observation => use('records', {}, async f => {
    if (change === 'logout') { f.identity.token = ''; f.identity.account = null; f.auth.verified = false }
    if (change === 'account-switch') { f.identity.token = 'synthetic-token-B'; f.identity.account = { id: 'synthetic-account-B', role: 'player', permissionVersion: 1 } }
    if (change === 'refresh-401') { f.store.failure = new RequestError(401); await f.state.loadMatches() }
    await flush()
    observation.account = f.identity.account?.id || null
    observation.matches = f.state.matches.map(match => match.matchId)
    observation.selectedId = f.state.selected?.matchId
    observation.visibleOldNames = text(f.main).includes('合成甲')
    observation.error = f.state.error
    assert.equal(f.state.matches.length, 0, 'previous account history must not remain rendered')
    assert.equal(f.state.selected, null)
    assert.equal(observation.visibleOldNames, false)
  }))
}
const terminalCases = [
  { id: 'WIN-0', match: summary({ winner: 0 }), winner: 0, expectedList: /合成甲 胜/, expectedReplay: 'decided' },
  { id: 'WIN-1', match: summary({ winner: 1 }), winner: 1, expectedList: /合成乙 胜/, expectedReplay: 'decided' },
  // The summary DTO does not carry the draw fact: do not invent it for the list.
  { id: 'AGREED-DRAW', match: summary({ winner: null }), winner: null, agreedDraw: true, expectedList: /已结束.*无法确认/, expectedReplay: 'draw' },
  { id: 'INVALID', match: summary({ winner: null, error: 'synthetic-authority-invalid' }), winner: null, expectedList: /无效/, expectedReplay: 'invalid' },
  { id: 'UNKNOWN-LEGACY', match: summary({ winner: null }), winner: null, expectedList: /已结束.*无法确认/, expectedReplay: 'unknown' },
]
for (const item of terminalCases) {
  await test(`HR-03-${item.id}`, 'same terminal meaning in list and actual replay: ' + item.id, async observation => {
    await use('records', { match: item.match }, f => {
      observation.listLabel = f.state.resultLabel(f.state.selected)
      observation.listText = surface(f.main, 'record-result')
    })
    await use('replay', { match: item.match, replayDetail: detail(item.match, item) }, f => {
      assert.equal(f.state.loading, false, 'actual player read must settle')
      assert.equal(f.state.error, '', 'fixture must load without a product error')
      assert.equal(descendants(f.main, element => classes(element).includes('actual-board-binding')).length, 1, 'actual replay template must bind its controlled child board')
      observation.replayState = f.state.replayResult?.state || null
      observation.replayText = surface(f.teleports, 'replay-result')
      observation.playerResults = f.state.replayResult?.players.map(player => player.result)
    })
    assert.match(observation.listLabel, item.expectedList, 'list must distinguish the authoritative outcome rather than only ended')
    assert.equal(observation.replayState, item.expectedReplay)
  })
}
for (const item of [
  { id: 'MAIN-NO-END', phase: 'Main', endedUtc: null },
  { id: 'MAIN-WITH-SUMMARY-END', phase: 'Main', endedUtc: '2026-10-22T00:02:03Z' },
  { id: 'INITIATIVE-NO-END', phase: 'Initiative', endedUtc: null },
]) {
  await test(`HR-04-${item.id}`, 'last available JSON frame alone is not a terminal draw: ' + item.id, async observation => {
    const match = summary({ winner: null, endedUtc: item.endedUtc })
    const envelope = { format: 'legion12-replay', version: 1, compatibilityVersion: 1, detail: detail(match, { phase: item.phase, winner: null }) }
    const parsed = model.parseReplayPayload(envelope)
    observation.importAccepted = true
    await use('replay', { match, replayDetail: parsed, json: true }, f => {
      observation.phase = f.state.currentGame?.phase
      observation.atLast = f.state.atLast
      observation.resultState = f.state.replayResult?.state || null
      observation.visibleResult = surface(f.teleports, 'replay-result')
      observation.error = f.state.error
      assert.equal(f.state.error, '', 'synthetic JSON must reach the actual player without harness error')
      assert.equal(f.state.replayResult, null, 'nonterminal final available frame must not display a concluded draw')
    })
  })
}
await test('CONTROL-02', 'ReplayPage already clears its board on account change (not a duplicate gap)', observation => use('replay', {}, async f => {
  assert.ok(f.state.currentGame)
  f.identity.token = ''
  f.identity.account = null
  await flush()
  observation.currentGame = f.state.currentGame
  observation.error = f.state.error
  observation.result = surface(f.teleports, 'replay-result')
  assert.equal(f.state.currentGame, null)
  assert.equal(f.state.detail, null)
  assert.equal(observation.result, '')
}))

const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
for (const mode of ['late-success', 'late-rejection', 'account-ABA', 'verified-ABA']) {
  await test(`HR-02-${mode.toUpperCase()}`, 'old list read cannot overwrite a newer identity/read: ' + mode, observation => use('records', {}, async f => {
    const old = deferred()
    f.store.read = () => old.promise
    const pending = f.state.loadMatches()
    await flush()
    if (mode === 'verified-ABA') { f.auth.verified = false; f.auth.verified = true }
    else {
      f.identity.token = 'synthetic-token-B'; f.identity.account = { id: 'synthetic-account-B', role: 'player' }
      if (mode === 'account-ABA') { f.identity.token = 'synthetic-token-A'; f.identity.account = { id: 'synthetic-account-A', role: 'player' } }
    }
    const fresh = summary({ matchId: 'synthetic-fresh-record', player0: '新读取甲' })
    f.store.read = async () => [fresh]
    await f.state.loadMatches(); await flush()
    if (mode === 'late-rejection') old.reject(new Error('旧账号迟到失败，不应覆盖新页面'))
    else old.resolve([summary()])
    await pending; await flush()
    observation.matches = f.state.matches.map(match => match.matchId)
    observation.error = f.state.error
    observation.loading = f.state.loading
    assert.deepEqual([...observation.matches], ['synthetic-fresh-record'])
    assert.equal(f.state.error, '')
    assert.equal(f.state.loading, false)
    assert.equal(f.state.selected, null)
  }))
}
await test('HR-02-UNMOUNT', 'late list result cannot write after actual component unmount', async observation => {
  const f = await fixture('records')
  const wait = deferred()
  f.store.read = () => wait.promise
  const pending = f.state.loadMatches()
  await flush()
  const previous = f.state.matches
  f.unmount()
  wait.resolve([summary({ matchId: 'synthetic-late-after-unmount' })])
  await pending; await flush()
  observation.writtenAfterUnmount = f.state.matches !== previous
  assert.equal(observation.writtenAfterUnmount, false)
})
await test('HR-02-LATE-EXPORT', 'old account detail cannot trigger a late JSON download', observation => use('records', {}, async f => {
  const wait = deferred()
  f.store.read = () => wait.promise
  const pending = f.state.exportReplay()
  f.identity.token = ''; f.identity.account = null; f.auth.verified = false
  wait.resolve(detail(summary()))
  await pending; await flush()
  observation.downloads = f.downloads
  assert.equal(f.downloads.length, 0)
}))
await test('HR-02-LATE-IMPORT', 'file text from a retired identity cannot become the imported replay', observation => use('records', {}, async f => {
  const wait = deferred()
  model.rememberImportedReplay(null)
  const input = { value: 'synthetic.json', files: [{ text: () => wait.promise }] }
  const pending = f.state.importReplay({ target: input })
  f.identity.token = ''; f.identity.account = null; f.auth.verified = false
  wait.resolve(JSON.stringify(model.exportReplayPayload(detail(summary()))))
  await pending; await flush()
  observation.navigations = f.navigation.length
  observation.remembered = Boolean(model.consumeImportedReplay())
  assert.equal(f.navigation.length, 0)
  assert.equal(model.consumeImportedReplay(), null)
  assert.equal(input.value, '')
}))
await test('CONTROL-03', 'play, JSON export and JSON import remain usable for the current account', observation => use('records', {}, async f => {
  f.state.playSelected()
  assert.equal(f.navigation[0].name, 'match-replay')
  await f.state.exportReplay()
  assert.equal(f.downloads.length, 1)
  assert.equal(f.downloads[0].filename, '2026-10-22-synthetic-record-A.json')
  const input = { value: 'synthetic.json', files: [{ text: async () => JSON.stringify(model.exportReplayPayload(detail(summary()))) }] }
  await f.state.importReplay({ target: input }); await flush()
  observation.destinations = f.navigation.map(value => value.name)
  observation.downloads = f.downloads.length
  assert.equal(f.navigation.at(-1).name, 'json-replay')
  assert.equal(model.consumeImportedReplay().match.matchId, 'synthetic-record-A')
  assert.equal(input.value, '')
  model.rememberImportedReplay(null)
}))
await test('CONTROL-04', 'mobile playback/import restrictions remain unchanged', observation => use('records', { mobile: true }, f => {
  f.state.playSelected(); f.state.openReplayImport()
  observation.error = f.state.error
  observation.destinations = f.navigation.length
  assert.equal(f.navigation.length, 0)
  assert.equal(f.state.error, '请到电脑端查看回放')
}))
for (const [id, raw, expected, summaryOverrides = {}] of [
  ['terminal-event-draw', { Phase: 'GameOver', Winner: null, Events: [{ Type: 'game-draw' }] }, 'draw'],
  ['terminal-event-invalid', { Phase: 'GameOver', Winner: null, Events: [{ Type: 'game-invalid' }] }, 'invalid'],
  ['missing-terminal-facts', { Phase: 'GameOver', Winner: null }, 'unknown'],
  ['conflicting-winners', { Phase: 'GameOver', Winner: 1 }, 'unknown'],
  ['nonterminal-with-summary-winner', { Phase: 'Main', Winner: 0 }, 'ongoing'],
  ['summary-error-with-winner', { Phase: 'GameOver', Winner: 0 }, 'unknown', { winner: 0, error: 'synthetic-authority-invalid' }],
  ['summary-error-with-draw', { Phase: 'GameOver', Winner: null, EndedByAgreedDraw: true }, 'unknown', { winner: null, error: 'synthetic-authority-invalid' }],
]) {
  await test(`HR-03-FACT-${id}`, 'shared outcome reader uses recorded facts without modifying them', observation => {
    const match = summary({ winner: id === 'conflicting-winners' || id === 'nonterminal-with-summary-winner' ? 0 : null, ...summaryOverrides })
    const before = JSON.stringify({ match, raw })
    observation.kind = outcome.recordedMatchOutcome(match, raw).kind
    assert.equal(observation.kind, expected)
    assert.equal(JSON.stringify({ match, raw }), before)
  })
}

let sourceStable = true
for (const [key, relative] of Object.entries(sourcePaths)) if (hash(fs.readFileSync(path.join(root, relative))) !== hash(bytes[key])) sourceStable = false
const report = {
  schemaVersion: 1, scope: 'HR-01 through HR-04, actual player records/replay SFC in memory',
  status: runtimeErrors.length || warnings.length || results.some(result => result.status === 'harness-error') || !sourceStable ? 'harness-error'
    : results.some(result => result.status === 'failed') ? 'defects-reproduced' : 'passed',
  sourceHashes, scriptSha256: hash(fs.readFileSync(fileURLToPath(import.meta.url))),
  totals: { cases: results.length, passed: results.filter(result => result.status === 'passed').length, failed: results.filter(result => result.status === 'failed').length, harnessErrors: results.filter(result => result.status === 'harness-error').length },
  sourceStable, runtimeErrors, warnings, mountedFixturesAfter: mountedFixtures, forbiddenNetworkCalls, cases: results,
  boundaries: ['This runner never mutates product sources, shared contracts, documentation or Git', 'No browser, build, .NET or real API/server access', 'Actual SFC setup/template and replay model; API, identity, router and child board are controlled fixtures', 'No DOM geometry or real server persistence proof', 'Unknown summaries cannot establish a draw; record the uncertainty without rewriting history', 'HR-05/06/07 not executed in this tranche'],
}
fs.writeFileSync(path.join(output, 'sfc-summary-report.json'), JSON.stringify(report, null, 2))
console.log(JSON.stringify(report, null, 2))
if (report.status !== 'passed' || mountedFixtures || forbiddenNetworkCalls) process.exitCode = 1
