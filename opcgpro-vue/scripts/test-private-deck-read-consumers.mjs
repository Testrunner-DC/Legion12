import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const borrowed = process.env.L12_PRIVATE_DECK_DEPENDENCY_ROOT || root
const requireDependency = createRequire(path.join(borrowed, 'package.json'))
const ts = requireDependency('typescript')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const clone = value => JSON.parse(JSON.stringify(value))
const ref = value => ({ value })
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }

function scriptOf(source) { return source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? source }
function sourceFile(relative) {
  const file = ts.createSourceFile(relative, scriptOf(read(relative)), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0, `${relative} must remain valid TypeScript`)
  return file
}
function functionSource(relative, name) {
  const file = sourceFile(relative)
  const node = file.statements.find(statement => ts.isFunctionDeclaration(statement) && statement.name?.text === name)
  assert.ok(node, `${relative} must define ${name}`)
  return node.getText(file).replace(/^export\s+/, '')
}
function compileBlock(relative, names, dependencies, prelude = '', result = `({${names.join(',')}})`) {
  const source = names.map(name => functionSource(relative, name)).join('\n')
  const output = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(dependencies), `${prelude}\n${output}\nreturn ${result};`)(...Object.values(dependencies))
}
function mountedCallback(dependencies) {
  const file = sourceFile('src/l12/site/DeckLibraryPage.vue')
  const calls = file.statements.filter(statement => ts.isExpressionStatement(statement)
    && ts.isCallExpression(statement.expression) && statement.expression.expression.getText(file) === 'onMounted')
  assert.equal(calls.length, 1, 'DeckLibraryPage must register one mounted callback')
  const output = ts.transpileModule(`export default ${calls[0].expression.arguments[0].getText(file)}`, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const exports = {}
  return new Function('exports', ...Object.keys(dependencies),
    `let libraryMounted=false;let libraryDisposed=false;${functionSource('src/l12/site/DeckLibraryPage.vue', 'applyLibraryCacheSnapshot')};${output};return exports.default;`)(
      exports, ...Object.values(dependencies))
}
function callsIn(relative, name) {
  const file = sourceFile(relative), node = file.statements.find(statement => ts.isFunctionDeclaration(statement) && statement.name?.text === name)
  const calls = []
  const visit = current => { if (ts.isCallExpression(current)) calls.push(current.expression.getText(file)); ts.forEachChild(current, visit) }
  visit(node)
  return calls
}

const summary = (id = 'deck-A', revision = 2, overrides = {}) => ({
  id, revision, name: `牌库-${id}`, masterId: 'MASTER-A', updatedAt: '2026-10-07T12:00:00.000Z',
  publicationId: null, publicationVersion: null,
  counts: { main: 40, uncountedMain: 1, morale: 10, special: 0, bench: 2 },
  legal: true, legalityReason: null, ...overrides,
})
function page(items = [summary()], total = items.length, query = {}) {
  const legal = items.every(item => item.legal) ? total : items.filter(item => item.legal).length
  return {
    items, total, page: query.page ?? 1, pageSize: query.pageSize ?? 30,
    generation: 91, permissionVersion: 7, catalogVersion: 'A'.repeat(64), policyVersion: 11,
    facets: { masters: total ? [{ masterId: items[0]?.masterId ?? 'MASTER-A', count: total }] : [], legal, illegal: total - legal },
  }
}
const body = (id = 'deck-A', revision = 2, name = '重命名牌库') => ({
  id, revision, name, masterId: 'MASTER-A', cardIds: ['C1', 'C1'], moraleIds: ['M1'], specialIds: [], benchIds: ['B1'],
  publicationId: null, publicationVersion: null, alternateArtSelections: {}, alternateArtCopies: {}, updatedAt: '2026-10-07T12:00:00.000Z',
})

let passed = 0
async function check(label, run) { await run(); passed++; console.log(`PASS ${passed}: ${label}`) }

const deckHelpers = compileBlock('src/l12/decks.ts', [
  'privateDeckObject', 'privateDeckExactFields', 'privateDeckInteger', 'privateDeckText', 'privateDeckContractError',
  'normalizePrivateDeckSummaryQuery', 'privateDeckSummaryRequestPath', 'validatePrivateDeckSummaryPage',
], {})

await check('summary query encodes one bounded server page and every supported filter', () => {
  const url = new URL(deckHelpers.privateDeckSummaryRequestPath({ page: 3, pageSize: 100, keyword: '中文 &', masterId: 'M/A', legal: false, sort: 'name' }), 'http://owned.invalid')
  assert.equal(url.pathname, '/api/decks/summaries'); assert.equal(url.searchParams.get('page'), '3')
  assert.equal(url.searchParams.get('pageSize'), '100'); assert.equal(url.searchParams.get('keyword'), '中文 &')
  assert.equal(url.searchParams.get('masterId'), 'M/A'); assert.equal(url.searchParams.get('legal'), 'false'); assert.equal(url.searchParams.get('sort'), 'name')
})
await check('summary query rejects unbounded and malformed parameters before a request', () => {
  assert.throws(() => deckHelpers.privateDeckSummaryRequestPath({ pageSize: 101 }))
  assert.throws(() => deckHelpers.privateDeckSummaryRequestPath({ page: 0 }))
  assert.throws(() => deckHelpers.privateDeckSummaryRequestPath({ sort: 'trend' }))
})
await check('thin summary page validates without creating body arrays', () => {
  const value = deckHelpers.validatePrivateDeckSummaryPage(page([summary()], 1), {})
  assert.equal(value.items[0].counts.main, 40); assert.equal(Object.hasOwn(value.items[0], 'cardIds'), false)
})
await check('available empty page remains a successful authoritative directory result', () => {
  const value = deckHelpers.validatePrivateDeckSummaryPage(page([], 0), {})
  assert.equal(value.total, 0); assert.deepEqual(value.items, []); assert.deepEqual(value.facets.masters, [])
})
for (const [label, mutate] of [
  ['body injection', value => { value.items[0].cardIds = [] }],
  ['missing count', value => { delete value.items[0].counts.bench }],
  ['duplicate identity', value => { value.items.push(clone(value.items[0])); value.total = 2; value.facets.masters[0].count = 2; value.facets.legal = 2 }],
  ['mismatched publication', value => { value.items[0].publicationId = 'public-A' }],
  ['invalid revision', value => { value.items[0].revision = 0 }],
  ['invalid catalog pin', value => { value.catalogVersion = 'short' }],
  ['invalid facet total', value => { value.facets.legal = 0 }],
  ['legal row with reason', value => { value.items[0].legalityReason = 'should be null' }],
]) await check(`summary validator rejects ${label}`, () => {
  const value = page([summary()], 1); mutate(value); assert.throws(() => deckHelpers.validatePrivateDeckSummaryPage(value, {}))
})
await check('summary validator binds response page, size, master and legal filters', () => {
  assert.throws(() => deckHelpers.validatePrivateDeckSummaryPage(page([summary()], 1), { page: 2 }))
  assert.throws(() => deckHelpers.validatePrivateDeckSummaryPage(page([summary()], 1), { masterId: 'OTHER' }))
  assert.throws(() => deckHelpers.validatePrivateDeckSummaryPage(page([summary()], 1), { legal: false }))
})

function summaryLoaderRuntime() {
  const calls = [], context = { accountId: 'owner', token: 'token', epoch: 1 }, state = { current: true }
  const functions = compileBlock('src/l12/decks.ts', [
    'privateDeckObject', 'privateDeckExactFields', 'privateDeckInteger', 'privateDeckText', 'privateDeckContractError',
    'normalizePrivateDeckSummaryQuery', 'privateDeckSummaryRequestPath', 'validatePrivateDeckSummaryPage', 'loadPrivateDeckSummaryPage',
  ], {
    captureDeckStorageContext: () => context,
    assertCompleteDeckAccount: () => {},
    platformRequest: (url, init) => { const row = { url, init, ...deferred() }; calls.push(row); return row.promise },
    isCurrentDeckStorageContext: () => state.current,
    operationError: error => error,
  })
  return { ...functions, calls, state }
}
await check('actual summary loader requests only metadata and performs no cache operation', async () => {
  const runtime = summaryLoaderRuntime(), query = { page: 2, pageSize: 30, keyword: '目标', legal: true, sort: 'name' }
  const loading = runtime.loadPrivateDeckSummaryPage(query)
  assert.equal(runtime.calls.length, 1); assert.match(runtime.calls[0].url, /^\/api\/decks\/summaries\?/)
  assert.equal(runtime.calls[0].init.cache, 'no-store')
  runtime.calls[0].resolve(page([summary()], 31, query)); const result = await loading
  assert.equal(result.page, 2); assert.equal(result.total, 31)
})
await check('actual summary loader drops an account transition after the reply', async () => {
  const runtime = summaryLoaderRuntime(), loading = runtime.loadPrivateDeckSummaryPage({})
  runtime.state.current = false; runtime.calls[0].resolve(page([summary()], 1))
  await assert.rejects(loading, /账号已切换/)
})
await check('actual summary loader preserves a 401 or transport failure without an empty-page fallback', async () => {
  const runtime = summaryLoaderRuntime(), error = Object.assign(new Error('unauthorized'), { status: 401, code: 'request_failed' })
  const loading = runtime.loadPrivateDeckSummaryPage({}); runtime.calls[0].reject(error)
  await assert.rejects(loading, candidate => candidate === error)
})

function directoryRuntime() {
  let account = 'owner', token = 'token-A', epoch = 1, route = '/decks?tab=mine', currentTab = 'mine', references = 0
  const mineQuery = ref(''), mineHomeCityFilter = ref('all'), mineLegalFilter = ref('all'), mineSort = ref('latest'), minePage = ref(1)
  const privateSummaryPage = ref(null), privateLoadState = ref('loading'), privateLoadError = ref('')
  const requests = [], platformState = { get account() { return account ? { id: account } : null }, get token() { return token } }
  const libraryContext = () => ({ account, token, epoch, route, tab: currentTab })
  const libraryContextCurrent = context => context.account === account && context.token === token && context.epoch === epoch
    && context.route === route && context.tab === currentTab
  const compiled = compileBlock('src/l12/site/DeckLibraryPage.vue', ['mineDirectoryQuery', 'loadMineDirectory'], {
    minePage, mineQuery, mineHomeCityFilter, mineLegalFilter, mineSort, PAGE_SIZE: 30,
    libraryContext, platformState, privateSummaryPage, privateLoadState, privateLoadError,
    libraryContextCurrent, loadPrivateDeckSummaryPage: query => { const row = { query: clone(query), ...deferred() }; requests.push(row); return row.promise },
    deckErrorBelongsToCurrentAccount: () => true, loadOwnReferences: () => { references++ },
  }, 'let privateDirectorySequence=0,libraryCounterDocumentEpoch=0,libraryDisposed=false,libraryMounted=true;',
  `({mineDirectoryQuery,loadMineDirectory,document:()=>libraryCounterDocumentEpoch++,dispose:()=>{libraryDisposed=true;privateDirectorySequence++}})`)
  return { ...compiled, mineQuery, mineHomeCityFilter, mineLegalFilter, mineSort, minePage,
    privateSummaryPage, privateLoadState, privateLoadError, requests, references: () => references,
    setAccount(value, nextToken = token) { account = value; token = nextToken; epoch++ },
    setRoute(value) { route = value; compiled.document() }, setTab(value) { currentTab = value; compiled.document() },
  }
}
await check('actual Vue directory consumer sends server page, filters and sort', async () => {
  const runtime = directoryRuntime(); runtime.minePage.value = 3; runtime.mineQuery.value = ' 中文 '
  runtime.mineHomeCityFilter.value = 'MASTER-A'; runtime.mineLegalFilter.value = 'illegal'; runtime.mineSort.value = 'name'
  const loading = runtime.loadMineDirectory(); assert.deepEqual(runtime.requests[0].query,
    { page: 3, pageSize: 30, keyword: '中文', masterId: 'MASTER-A', legal: false, sort: 'name' })
  runtime.requests[0].resolve(page([summary('deck-C')], 61, runtime.requests[0].query)); await loading
  assert.equal(runtime.privateSummaryPage.value.total, 61); assert.equal(runtime.privateLoadState.value, 'available')
  assert.equal(runtime.references(), 1)
})
await check('empty server page changes directory state without synthesizing bodies', async () => {
  const runtime = directoryRuntime(), loading = runtime.loadMineDirectory()
  runtime.requests[0].resolve(page([], 0, runtime.requests[0].query)); await loading
  assert.deepEqual(runtime.privateSummaryPage.value.items, []); assert.equal(runtime.privateLoadState.value, 'available')
})
for (const transition of ['reorder', 'filter', 'account ABA', 'token ABA', 'route ABA', 'tab ABA', 'unmount']) await check(`actual directory drops ${transition} reply`, async () => {
  const runtime = directoryRuntime(), first = runtime.loadMineDirectory()
  if (transition === 'reorder') { const second = runtime.loadMineDirectory(); runtime.requests[1].resolve(page([summary('new')], 1, runtime.requests[1].query)); await second }
  if (transition === 'filter') runtime.mineQuery.value = 'changed'
  if (transition === 'account ABA') { runtime.setAccount('other'); runtime.setAccount('owner') }
  if (transition === 'token ABA') { runtime.setAccount('owner', 'token-B'); runtime.setAccount('owner', 'token-A') }
  if (transition === 'route ABA') { runtime.setRoute('/other'); runtime.setRoute('/decks?tab=mine') }
  if (transition === 'tab ABA') { runtime.setTab('plaza'); runtime.setTab('mine') }
  if (transition === 'unmount') runtime.dispose()
  runtime.requests[0].resolve(page([summary('old')], 1, runtime.requests[0].query)); await first
  assert.notEqual(runtime.privateSummaryPage.value?.items[0]?.id, 'old')
})
await check('latest directory failure is unavailable, not an authoritative empty page', async () => {
  const runtime = directoryRuntime(), loading = runtime.loadMineDirectory(); runtime.requests[0].reject(new Error('storage unavailable')); await loading
  assert.equal(runtime.privateLoadState.value, 'unavailable'); assert.equal(runtime.privateSummaryPage.value, null)
  assert.match(runtime.privateLoadError.value, /storage unavailable/)
})

const scopes = ['ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent']
function bodyRuntime(options = {}) {
  let current = true, activityMarks = 0, commits = 0, snapshot = options.snapshot ?? {
    status: 'ready', raw: 'raw-1', owner: 'account:owner', generation: 'old', aliases: {},
    decks: { Old: body('deck-A', 1, 'Old'), Unrelated: body('deck-U', 4, 'Unrelated') },
  }
  const calls = [], locks = [], committed = [], storage = new Map()
  const selectedKey = 'l12-selected-custom-deck:owner'
  for (const key of [selectedKey, ...scopes.map(scope => `${selectedKey}:${scope}`)]) storage.set(key, 'Old')
  const localStorage = { getItem: key => storage.has(key) ? storage.get(key) : null, setItem: (key, value) => storage.set(key, value) }
  const navigator = { locks: { request: async (name, action) => { locks.push(name); return action() } } }
  const requests = options.deferred ? null : options.response ?? body()
  const dependencies = {
    platformState: { account: { id: 'owner' }, token: 'token' }, SELECTED_DECK_KEY: 'l12-selected-custom-deck',
    L12_DECK_SELECTION_SCOPES: scopes,
    localStorage, captureDeckStorageContext: () => ({ accountId: 'owner', token: 'token', epoch: 1,
      storageKey: 'l12-custom-decks-v1:owner', selectedKey }),
    assertCompleteDeckAccount: () => {}, isCurrentDeckStorageContext: () => current,
    platformRequest: (url, init) => {
      const row = { url, init, ...(options.deferred ? deferred() : {}) }; calls.push(row)
      if (options.error) return Promise.reject(options.error)
      return options.deferred ? row.promise : Promise.resolve(clone(requests))
    },
    normalizeSavedDeck: value => {
      if (!value || !Array.isArray(value.cardIds) || !Array.isArray(value.moraleIds) || !Array.isArray(value.specialIds))
        throw new Error('牌库正文格式无效')
      return clone(value)
    },
    currentDeckSnapshot: () => snapshot,
    markDeckCacheActivity: () => { activityMarks++; return `activity-${activityMarks}` },
    requireCurrentOperation: () => { if (!current) throw new Error('stale operation') },
    commitDeckCache: (_storage, _key, previous, decks, generation, aliases, guard) => {
      guard(); commits++
      if (options.commitError) throw options.commitError
      snapshot = { status: 'ready', raw: `raw-${commits + 1}`, owner: previous.owner, generation, decks: clone(decks), aliases: clone(aliases) }
      committed.push({ decks: clone(decks), aliases: clone(aliases) }); return snapshot
    },
    interpretDeckSelection: (_previous, _key, raw) => raw,
    operationError: error => error,
  }
  const functions = compileBlock('src/l12/decks.ts', [
    'selectedDeckStorageKey', 'scopedSelectedDeckStorageKey', 'sameDeckName', 'upsertCachedDeck', 'deckIdentityContent',
    'migrateDeckSelectionReferences', 'withGuestDeckMutation', 'commitSavedDecks',
    'privateDeckText', 'privateDeckInteger', 'loadPrivateDeckBody',
  ], dependencies)
  async function call(target = { id: 'deck-A', revision: 2 }, guard = () => true) {
    const navigatorDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'navigator')
    const storageDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'localStorage')
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: navigator })
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: localStorage })
    try { return await functions.loadPrivateDeckBody(target, guard) }
    finally {
      if (navigatorDescriptor) Object.defineProperty(globalThis, 'navigator', navigatorDescriptor); else delete globalThis.navigator
      if (storageDescriptor) Object.defineProperty(globalThis, 'localStorage', storageDescriptor); else delete globalThis.localStorage
    }
  }
  return { ...functions, call, calls, locks, committed, storage, snapshot: () => snapshot,
    setCurrent: value => { current = value }, setSnapshot: value => { snapshot = value },
    activityMarks: () => activityMarks, commits: () => commits }
}

await check('selected body uses exact id and revision, then one real Web Lock/CAS merge', async () => {
  const runtime = bodyRuntime({ response: body() }), result = await runtime.call()
  assert.equal(runtime.calls[0].url, '/api/decks/by-id/deck-A?expectedRevision=2'); assert.equal(runtime.calls[0].init.cache, 'no-store')
  assert.deepEqual(runtime.locks, ['l12:deck-save:l12-custom-decks-v1:owner']); assert.equal(runtime.commits(), 1)
  assert.equal(result.name, '重命名牌库'); assert.equal(runtime.snapshot().decks.Old, undefined)
  assert.equal(runtime.snapshot().decks['重命名牌库'].id, 'deck-A'); assert.equal(runtime.snapshot().decks.Unrelated.id, 'deck-U')
  const aliases = runtime.committed[0].aliases
  for (const key of ['l12-selected-custom-deck:owner', ...scopes.map(scope => `l12-selected-custom-deck:owner:${scope}`)])
    assert.deepEqual(aliases[key], { raw: 'Old', resolved: 'deck-A' })
})
await check('metadata can never masquerade as a SavedL12Deck body', async () => {
  const runtime = bodyRuntime({ response: summary() }); await assert.rejects(runtime.call(), /正文格式无效/)
  assert.equal(runtime.commits(), 0); assert.equal(runtime.activityMarks(), 0)
})
await check('mismatched server identity and revision never enter the cache', async () => {
  const wrongId = bodyRuntime({ response: body('other', 2) }); await assert.rejects(wrongId.call(), /身份或修订/); assert.equal(wrongId.commits(), 0)
  const wrongRevision = bodyRuntime({ response: body('deck-A', 3) }); await assert.rejects(wrongRevision.call(), /身份或修订/); assert.equal(wrongRevision.commits(), 0)
})
await check('a newer cached revision wins without being overwritten', async () => {
  const snapshot = { status: 'ready', raw: 'raw', owner: 'account:owner', generation: 'new', aliases: {},
    decks: { Newer: body('deck-A', 3, 'Newer'), Other: body('deck-U', 1, 'Other') } }
  const runtime = bodyRuntime({ response: body(), snapshot }); await assert.rejects(runtime.call(), /更新修订/)
  assert.equal(runtime.commits(), 0); assert.equal(runtime.snapshot().decks.Newer.revision, 3); assert.ok(runtime.snapshot().decks.Other)
})
await check('another identity with the same name blocks merge and preserves both cached objects', async () => {
  const snapshot = { status: 'ready', raw: 'raw', owner: 'account:owner', generation: 'old', aliases: {},
    decks: { Old: body('deck-A', 1, 'Old'), '重命名牌库': body('deck-B', 8, '重命名牌库') } }
  const runtime = bodyRuntime({ response: body(), snapshot }); await assert.rejects(runtime.call(), /另一副同名/)
  assert.equal(runtime.commits(), 0); assert.equal(Object.keys(runtime.snapshot().decks).length, 2)
})
await check('merge re-reads the latest cache after the network reply and retains concurrent objects', async () => {
  const runtime = bodyRuntime({ deferred: true }), loading = runtime.call()
  const latest = clone(runtime.snapshot()); latest.raw = 'raw-latest'; latest.decks.Concurrent = body('deck-C', 5, 'Concurrent')
  runtime.setSnapshot(latest); runtime.calls[0].resolve(body()); const result = await loading
  assert.equal(result.id, 'deck-A'); assert.equal(runtime.snapshot().decks.Concurrent.revision, 5)
})
await check('route or sequence guard can cancel before request and after reply with zero cache write', async () => {
  const before = bodyRuntime({ response: body() }); await assert.rejects(before.call(undefined, () => false), /取消/); assert.equal(before.calls.length, 0)
  const after = bodyRuntime({ deferred: true }); let allowed = true; const loading = after.call(undefined, () => allowed)
  allowed = false; after.calls[0].resolve(body()); await assert.rejects(loading, /页面已切换/); assert.equal(after.commits(), 0)
})
await check('account transition after reply prevents lock and cache merge', async () => {
  const runtime = bodyRuntime({ deferred: true }), loading = runtime.call(); runtime.setCurrent(false); runtime.calls[0].resolve(body())
  await assert.rejects(loading, /账号或页面已切换/); assert.equal(runtime.locks.length, 0); assert.equal(runtime.commits(), 0)
})
for (const [status, code] of [[401, 'request_failed'], [404, 'request_failed'], [409, 'deck_revision_conflict']]) await check(`${status} selected read performs no retry, merge, deletion or scope write`, async () => {
  const error = Object.assign(new Error(`HTTP ${status}`), { status, code })
  const runtime = bodyRuntime({ error }); const before = [...runtime.storage.entries()]
  await assert.rejects(runtime.call(), candidate => candidate === error)
  assert.equal(runtime.calls.length, 1); assert.equal(runtime.commits(), 0); assert.deepEqual([...runtime.storage.entries()], before)
})
await check('Quota/CAS persistence failure leaves the previous deck snapshot intact and is not server-confirmed', async () => {
  const quota = new Error('QuotaExceededError'), runtime = bodyRuntime({ response: body(), commitError: quota }), before = clone(runtime.snapshot())
  await assert.rejects(runtime.call(), error => error === quota && error.serverConfirmed !== true)
  assert.deepEqual(runtime.snapshot(), before); assert.equal(runtime.commits(), 1)
})

function mountRuntime(account) {
  const calls = { summaries: 0, full: 0 }, platformState = { account: account ? { id: 'owner' } : null, token: account ? 'token' : '' }
  const catalog = ref([]), saved = ref({}), ownedAlternateArts = ref([]), operationsPolicy = ref(null), notice = ref(''), privateLoadState = ref('loading')
  const context = { account: platformState.account?.id, token: platformState.token, epoch: 1, route: '/decks?tab=mine', tab: 'mine' }
  const mounted = mountedCallback({
    restoreFiltersFromRoute: () => {}, platformState, privateLoadState,
    libraryContext: () => context, loadSavedDecksState: () => ({ status: 'missing', decks: {} }),
    loadSummarySources: async () => {}, libraryContextCurrent: () => true,
    loadDeckCatalog: async () => [], loadMineDirectory: async () => { calls.summaries++ },
    ensureOfficialPrebuiltDecks: async () => { calls.full++; return { Guest: body('guest', 1, 'Guest') } },
    alternateArtApi: { mine: async () => [] }, catalog, saved, ownedAlternateArts, operationsPolicy, notice,
    getEffectiveOperationsPolicy: async () => null, loadOwnReferences: () => {}, nextTick: async () => {},
    sessionStorage: { getItem: () => null }, route: { fullPath: '/decks?tab=mine' }, listScrollHost: () => null,
    deckErrorBelongsToCurrentAccount: () => true,
  })
  return { mounted, calls, saved, privateLoadState }
}
await check('actual authenticated Vue mount selects the summary directory and never the full-body sync', async () => {
  const runtime = mountRuntime(true); await runtime.mounted(); assert.deepEqual(runtime.calls, { summaries: 1, full: 0 })
})
await check('actual guest Vue mount retains original official preset seeding', async () => {
  const runtime = mountRuntime(false); await runtime.mounted(); assert.deepEqual(runtime.calls, { summaries: 0, full: 1 })
  assert.equal(runtime.saved.value.Guest.name, 'Guest'); assert.equal(runtime.privateLoadState.value, 'available')
})
await check('every private row action resolves the selected id and revision before using a body', () => {
  for (const name of ['editMine', 'deleteMine', 'duplicateMine', 'copyMineCode', 'previewMineImage', 'publishDeck'])
    assert.ok(callsIn('src/l12/site/DeckLibraryPage.vue', name).includes('readMineDeck'), `${name} must read the selected body`)
  assert.ok(callsIn('src/l12/site/DeckLibraryPage.vue', 'previewMineImage').includes('previewImage'))
  assert.ok(callsIn('src/l12/site/DeckLibraryPage.vue', 'editMine').includes('publishedCopyFor'))
})
await check('mine template cannot pass thin metadata directly to body encoders, images, delete or editor navigation', () => {
  const source = read('src/l12/site/DeckLibraryPage.vue')
  assert.equal(source.includes('@click="copyCode(deck)"'), false); assert.equal(source.includes('@click="previewImage(deck)"'), false)
  assert.equal(source.includes(':to="editorLink(deck.name'), false); assert.match(source, /@click="editMine\(deck\)"/)
  assert.match(source, /@click="deleteMine\(deck\)"/); assert.match(source, /:meta="mineCountLabel\(deck\)"/)
})
await check('authenticated library has no full-body directory fallback while legacy sync remains for C-U5 callers', () => {
  const library = read('src/l12/site/DeckLibraryPage.vue'), sync = functionSource('src/l12/decks.ts', 'syncSavedDecksFromAccount')
  assert.equal(library.includes('syncSavedDecksFromAccount'), false)
  assert.match(library, /platformState\.account \? loadMineDirectory\(\)\.then\(\(\) => null\) : ensureOfficialPrebuiltDecks\(\)/)
  assert.match(sync, /platformRequest<SavedL12Deck\[]>\('\/api\/decks'\)/)
})

console.log(`C-U4 focused private read result: ${passed}/${passed}, failed=0, skipped=0`)
