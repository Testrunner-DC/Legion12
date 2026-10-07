import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const sourceRoot = path.resolve(process.env.L12_COUNTER_SOURCE_ROOT || scriptRoot)
const borrowedRoot = path.resolve(process.env.L12_COUNTER_READONLY_ROOT || scriptRoot)
const dependencyRoot = path.resolve(process.env.L12_COUNTER_DEPENDENCY_ROOT || scriptRoot)
const requireDependency = createRequire(path.join(dependencyRoot, 'package.json'))
const typescript = () => requireDependency('typescript')
const read = relative => fs.readFileSync(path.join(sourceRoot, relative), 'utf8')
const scriptOf = source => source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? source
function parsed(source, name = 'counter.ts') {
  const ts = typescript()
  const file = ts.createSourceFile(name, scriptOf(source), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0, 'Counter source must parse before testing')
  return file
}
function functions(source) {
  const ts = typescript(), file = parsed(source)
  return new Map(file.statements.filter(ts.isFunctionDeclaration).map(node => [node.name?.text, node.getText(file)]))
}
function javascript(source) {
  const ts = typescript()
  return ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
}
function evaluated(source, dependencies, expression) {
  const exports = {}
  const run = new Function(...Object.keys(dependencies), 'exports', javascript(source) + `\nreturn (${expression});`)
  return run(...Object.values(dependencies), exports)
}

function productionModule(relative) {
  return evaluated(read(relative), {}, 'exports')
}

// This replaces only the previous counter-preservation leaf. The surrounding
// browser geometry, permissions and construction-consumer contracts remain.
export function publicDeckCounterBindingsContract({ detail, library, helper }) {
  try {
    const ts = typescript(), detailFunctions = functions(detail), libraryFunctions = functions(library)
    const helperFunctions = functions(helper)
    const merge = helperFunctions.get('mergePublicDeckCounters')
    if (!merge) return false
    const mergeFile = parsed(merge), returns = []
    const collect = node => { if (ts.isReturnStatement(node)) returns.push(node); ts.forEachChild(node, collect) }
    collect(mergeFile)
    const result = returns[0]?.expression
    if (!result || !ts.isObjectLiteralExpression(result)) return false
    const spreads = result.properties.filter(ts.isSpreadAssignment)
    if (spreads.length !== 1 || spreads[0].expression.getText(mergeFile) !== 'current') return false
    const properties = result.properties.filter(ts.isPropertyAssignment)
    const expected = { publicCode: 'publicCode', views: 'views', likes: 'likes', copies: 'copies',
      viewerLiked: 'viewerLiked', liked: 'viewerLiked', canEdit: 'canEdit' }
    if (properties.length !== Object.keys(expected).length || properties.some(item =>
      item.initializer.getText(mergeFile) !== `counters.${expected[item.name.getText(mergeFile)]}`)) return false
    for (const [set, name, kind] of [[detailFunctions, 'recordInitialView', 'view'], [detailFunctions, 'toggleLike', 'like'],
      [detailFunctions, 'copyToMine', 'copy'], [libraryFunctions, 'toggleLike', 'like'], [libraryFunctions, 'copyToMine', 'copy']]) {
      const source = set.get(name)
      if (!source) return false
      const file = parsed(source), calls = []
      const visit = node => { if (ts.isCallExpression(node)) calls.push(node); ts.forEachChild(node, visit) }
      visit(file)
      const counters = calls.filter(call => call.expression.getText(file) === 'publicDeckApi.counter')
      if (counters.length !== 1 || counters[0].arguments[1]?.text !== kind
        || calls.filter(call => call.expression.getText(file) === 'mergePublicDeckCounters').length !== 1
        || calls.filter(call => call.expression.getText(file) === 'captureCounterContext').length !== 1
        || calls.filter(call => call.expression.getText(file) === 'current').length < 2
        || calls.some(call => /^publicDeckApi\.(recordView|recordCopy|toggleLike)$/.test(call.expression.getText(file)))) return false
    }
    for (const [source, set, actor, epoch] of [[detail, detailFunctions, 'captureDeckAccountGuard()', 'counterDocumentEpoch'],
      [library, libraryFunctions, 'libraryContextCurrent(context)', 'libraryCounterDocumentEpoch']]) {
      const capture = set.get('captureCounterContext')
      if (!capture?.includes('capturePublicDeckCounterGuard') || !capture.includes(actor)
        || !capture.includes('actionPending(key)') || !capture.includes('route.fullPath') || !capture.includes(epoch)) return false
      const file = parsed(source), hasEpochWatch = file.statements.some(node => ts.isExpressionStatement(node)
        && ts.isCallExpression(node.expression) && node.expression.expression.getText(file) === 'watch'
        && node.expression.arguments[0]?.getText(file) === '() => route.fullPath'
        && node.expression.arguments[1]?.getText(file) === `() => ${epoch}++`
        && node.expression.arguments[2]?.getText(file).includes("'sync'"))
      if (!hasEpochWatch) return false
    }
    return true
  } catch { return false }
}

function frozenActorGuard(platformState) {
  // Execute the existing B1 watcher and guard, rather than inventing a substitute
  // identity comparison that could miss a token-only or A-B-A transition.
  const source = fs.readFileSync(path.join(borrowedRoot, 'src/l12/decks.ts'), 'utf8')
  const ts = typescript(), file = parsed(source), declarations = functions(source)
  const listenerNode = file.statements.find(node => ts.isExpressionStatement(node) && ts.isCallExpression(node.expression)
    && node.expression.expression.getText(file) === 'watch'
    && node.expression.arguments[0]?.getText(file).includes('platformState.token'))
  assert.ok(listenerNode, 'B1 synchronous actor watcher is required')
  const listeners = []
  const watch = (get, listener) => listeners.push({ get, listener, last: get() })
  const code = ['let deckAccountEpoch = 0;', listenerNode.getText(file),
    ...['captureDeckAccountGuard', 'captureDeckStorageContext', 'isCurrentDeckStorageContext'].map(name => declarations.get(name))].join('\n')
  const guard = evaluated(code, { platformState, watch, accountStorageKey: id => `cache:${id}`, selectedDeckStorageKey: id => `select:${id}` }, 'captureDeckAccountGuard')
  return { capture: guard, set(id, token) {
    platformState.account = id ? { id } : null; platformState.token = token
    for (const row of listeners) { const next = row.get(); row.listener(next, row.last); row.last = next }
  } }
}

function pageRuntime(kind, helper, baseline = false) {
  const source = read(`src/l12/site/${kind === 'detail' ? 'PublicDeckDetailPage' : 'DeckLibraryPage'}.vue`)
  const declarations = functions(source)
  const body = { name: 'counter deck', masterId: 'M', cardIds: ['A', 'A'], moraleIds: ['R'], specialIds: [], updatedAt: 'captured' }
  const details = { guide: { buildIdea: 'captured guide' }, versions: [{ version: 1 }], matchStatistics: { games: 3 } }
  const initial = { id: 'p', publicCode: 'CODE12', ownerId: 'A', deck: body, details, readToken: 'captured-token',
    views: 0, likes: 0, copies: 0, liked: false }
  const entry = { value: initial }, published = { value: [initial] }, notice = { value: '' }, saved = { value: {} }
  const route = { fullPath: '/decks/CODE12' }, platformState = { account: { id: 'A' }, token: 'token-A' }
  const actor = frozenActorGuard(platformState), active = new Set(), marker = new Map(), counters = [], saves = []
  let libraryEpoch = 0, counterReply = async () => scalar(), saveReply = async deck => ({ ...deck, name: 'confirmed copy' })
  const scalar = () => ({ id: 'p', publicCode: 'CODE12', views: 1, likes: 2, copies: 3, viewerLiked: true, canEdit: true })
  const runAction = async (key, action) => { if (active.has(key)) return; active.add(key); try { return await action() } finally { active.delete(key) } }
  const counter = async (reference, operation) => { counters.push([reference, operation]); return counterReply() }
  const legacy = async (reference, operation) => {
    counters.push([reference, `legacy-${operation}`])
    return { id: initial.id, publicCode: initial.publicCode, views: 1, likes: 2, copies: 3,
      ownerId: 'A', author: 'A', liked: true, deck: structuredClone(body), createdAt: 'captured', updatedAt: 'captured' }
  }
  const publicDeckApi = { counter, toggleLike: ref => legacy(ref, 'like'), recordCopy: ref => legacy(ref, 'copy') }
  const libraryContext = () => ({ epoch: libraryEpoch, id: platformState.account?.id, token: platformState.token, route: route.fullPath })
  const libraryContextCurrent = value => value.epoch === libraryEpoch && value.id === platformState.account?.id
    && value.token === platformState.token && value.route === route.fullPath
  const dependencies = { ...helper, entry, published, notice, saved, route, platformState, publicDeckApi, runAction,
    actionPending: key => active.has(key), captureDeckAccountGuard: actor.capture, libraryContext, libraryContextCurrent,
    publicDeckActionKey: (id, account = platformState.account?.id ?? 'anonymous') => `public-deck:${account}:${id}`,
    sessionStorage: { getItem: key => marker.get(key) ?? null, setItem: (key, value) => marker.set(key, value), removeItem: key => marker.delete(key) },
    uniqueName: name => name, loadSavedDecks: () => saved.value, saveDeck: async deck => { saves.push(deck); return saveReply(deck) },
    deckErrorBelongsToCurrentAccount: () => true,
    updatePublished: value => { published.value[0] = value },
  }
  const names = ['publicDeckReference', 'captureCounterContext', 'recordInitialView', 'toggleLike', 'copyToMine'].filter(name => declarations.has(name))
  const code = 'let counterDocumentEpoch = 0, libraryCounterDocumentEpoch = 0;\n' + names.map(name => declarations.get(name)).join('\n')
  const methods = evaluated(code, dependencies, `{ ${names.join(',')}, advanceDocument: () => { counterDocumentEpoch++; libraryCounterDocumentEpoch++ } }`)
  const current = () => kind === 'detail' ? entry.value : published.value[0]
  return { initial, body, details, notice, saved, counters, saves, marker, route, platformState, current,
    like: () => kind === 'detail' ? methods.toggleLike() : methods.toggleLike(published.value[0]),
    copy: () => kind === 'detail' ? methods.copyToMine() : methods.copyToMine(published.value[0]),
    view: () => methods.recordInitialView(entry.value),
    reply: value => { counterReply = value }, saveReply: value => { saveReply = value },
    setActor: (id, token) => { actor.set(id, token); libraryEpoch++ },
    setRoute: value => { route.fullPath = value; methods.advanceDocument() }, disposeAction: () => active.clear(),
    replace: value => { if (kind === 'detail') entry.value = value; else published.value[0] = value }, scalar, baseline }
}

function requestRuntime() {
  const ts = typescript(), source = read('src/l12/platform.ts'), file = parsed(source), declarations = functions(source)
  const error = file.statements.find(node => ts.isClassDeclaration(node) && node.name?.text === 'PlatformRequestError')
  const api = file.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
    .find(node => node.name.getText(file) === 'publicDeckApi')?.initializer
  const counter = api?.properties.find(node => ts.isPropertyAssignment(node) && node.name.getText(file) === 'counter')?.initializer
  assert.ok(counter, 'Use the actual new API function')
  const requests = [], configs = []
  const code = [error.getText(file), declarations.get('retryAfterMilliseconds'), declarations.get('platformRequest'),
    `const counter = ${counter.getText(file)};`].join('\n')
  const apiMethods = evaluated(code, {
    platformState: { token: 'A' }, platformSessionVersion: 1, apiBase: () => 'http://counter.invalid',
    PLATFORM_READ_TIMEOUT_MS: 10000, PLATFORM_MUTATION_TIMEOUT_MS: 20000, PLATFORM_UPLOAD_TIMEOUT_MS: 60000,
    PLATFORM_READ_MAX_ATTEMPTS: 2, requestFingerprint: async () => 'key', retryableReadFailure: () => true,
    RequestDeadlineError: class extends Error {}, authState: { verified: true }, forgetAccount: () => {}, refreshCurrentAccount: async () => null,
    fetch: async (url, init) => { requests.push({ url, method: init.method }); return new Response(JSON.stringify({ message: 'synthetic counter failure' }), { status: 503 }) },
    platformRequestCoordinator: { execute: async config => {
      configs.push({ method: config.method, maxAttempts: config.maxAttempts })
      for (let attempt = 1; ; attempt++) { try { return await config.run(new AbortController().signal) }
        catch (error) { if (attempt >= config.maxAttempts || !config.shouldRetry(error)) throw error } }
    } },
  }, '{counter, platformRequest}')
  return { ...apiMethods, requests, configs }
}

function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function detailLoadRuntime(helper, vueRefs = false) {
  const source = process.env.L12_DETAIL_LOAD_BASELINE_SOURCE
    ? fs.readFileSync(process.env.L12_DETAIL_LOAD_BASELINE_SOURCE, 'utf8') : read('src/l12/site/PublicDeckDetailPage.vue')
  const ts = typescript(), file = parsed(source), declarations = functions(source)
  const readHelper = productionModule('src/l12/site/publicDeckRead.ts')
  const summaryHelper = productionModule('src/l12/site/publicDeckSummary.ts')
  const officialHelper = productionModule('src/l12/site/officialDeckReference.ts')
  const vue = vueRefs ? requireDependency('vue') : null
  const makeRef = value => vue ? vue.ref(value) : { value }
  const entry = makeRef(null), catalog = makeRef([]), notice = makeRef(''), loading = makeRef(true), refreshRequired = makeRef(false)
  const selectedCard = makeRef(null), openingHandIds = makeRef([]), imagePreview = makeRef(null)
  const makeReactive = value => vue ? vue.reactive(value) : value
  const platformState = makeReactive({ account: { id: 'account-A' }, token: 'auth-A' }), actor = frozenActorGuard(platformState)
  const route = makeReactive({ name: 'public-deck-detail', fullPath: '/decks/A', params: { deckId: 'A' }, query: {}, hash: '' })
  const watchers = [], mounts = [], unmounts = [], active = new Set(), markers = new Map()
  const catalogs = [], gets = [], presets = [], replaces = [], posts = []
  let disposed = false, manualReplace = false, redraws = 0
  const targetPath = target => `/decks/${target.params.deckId}`
    + (Object.keys(target.query ?? {}).length ? `?${new URLSearchParams(target.query)}` : '') + (target.hash || '')
  const same = (left, right) => Array.isArray(left) && Array.isArray(right)
    ? left.length === right.length && left.every((value, index) => value === right[index]) : left === right
  function trigger() {
    for (const watcher of watchers) {
      const value = watcher.get()
      if (same(value, watcher.previous)) continue
      const previous = watcher.previous; watcher.previous = value; watcher.listener(value, previous)
    }
  }
  function applyRoute(id, fullPath = `/decks/${id}`, name = 'public-deck-detail') {
    route.params = { deckId: id }; route.fullPath = fullPath; route.name = name; trigger()
  }
  const readToken = 'a'.repeat(64)
  const publication = (code = 'A', label = code) => {
    const owner = platformState.account?.id === 'account-A'
    return { summary: { id: `pub-${code}`, source: 'public', name: label, masterId: 'M', masterName: 'master', faction: 'faction',
      author: 'author', publicCode: code, publicationVersion: owner ? 1 : null,
      createdAt: '2026-10-07T00:00:00Z', updatedAt: '2026-10-07T00:00:00Z',
      counts: { main: 1, uncountedMain: 0, morale: 0, special: 0, bench: 0 }, legal: true, legalityReason: null,
      environment: { status: 'known', value: '2.5', reason: null }, views: 0, likes: 0, copies: 0,
      viewerLiked: false, canEdit: owner, readToken: null }, version: 1,
      deck: { name: label, masterId: 'M', cardIds: ['C'], moraleIds: [], specialIds: [], updatedAt: '2026-10-07T00:00:00Z',
        publicationId: owner ? `pub-${code}` : null, publicationVersion: owner ? 1 : null },
      guide: { buildIdea: 'real guide', opening: '', keyCards: '', commonSequence: '', substitutions: '' }, matchups: [],
      contentRevision: 1, contentUpdatedAt: '2026-10-07T00:00:00Z', readToken, catalogVersion: 'catalog-1', policyVersion: 1 }
  }
  const byId = { get value() { return new Map(catalog.value.map(card => [card.id, card])) } }
  const master = { get value() { return entry.value ? byId.value.get(entry.value.deck.masterId) : null } }
  const dependencies = { ...helper, entry, catalog, notice, loading, refreshRequired, selectedCard, openingHandIds, imagePreview,
    platformState, route, byId, master, captureDeckAccountGuard: actor.capture,
    publicDeckActionKey: (id, account = platformState.account?.id ?? 'anonymous') => `public-deck:${account}:${id}`,
    actionPending: key => active.has(key),
    runAction: async (key, action) => { if (disposed || active.has(key)) return; active.add(key); try { return await action() } finally { active.delete(key) } },
    watch: (get, listener) => watchers.push({ get, listener, previous: get() }),
    onMounted: callback => mounts.push(callback), onBeforeUnmount: callback => unmounts.push(callback),
    sessionStorage: { getItem: key => markers.get(key) ?? null, setItem: (key, value) => markers.set(key, value), removeItem: key => markers.delete(key) },
    loadDeckCatalog: () => { const row = deferred(); catalogs.push(row); return row.promise },
    loadOfficialPresetDecks: () => { const row = deferred(); presets.push(row); return row.promise },
    publicDeckReadApi: {
      current: (reference, expectedReadToken) => { const row = { reference, expectedReadToken, ...deferred() }; gets.push(row); return row.promise },
    },
    publicDeckApi: {
      counter: async (reference, kind) => { posts.push([reference, kind]); return { id: `pub-${reference}`, publicCode: reference,
        views: 1, likes: 0, copies: 0, viewerLiked: false, canEdit: true } },
    },
    settlePublicDeckRead: readHelper.settlePublicDeckRead, validatePublicDeckCurrent: readHelper.validatePublicDeckCurrent,
    toPublicDeckCurrentEntry: readHelper.toPublicDeckCurrentEntry, consumeSummaryOpen: summaryHelper.consumeSummaryOpen,
    resolveOfficialDeck: officialHelper.resolveOfficialDeck, invalidateReadSections: () => {},
    loadVersionPage: async () => {}, loadStatisticsPage: async () => {},
    router: {
      resolve: target => ({ fullPath: targetPath(target) }),
      replace: target => {
        const row = { target, cancelled: false, ...deferred() }; replaces.push(row)
        const finish = () => {
          if (!row.cancelled) applyRoute(target.params.deckId, targetPath(target))
          row.resolve(row.cancelled ? { type: 'cancelled' } : undefined)
        }
        row.finish = finish
        if (!manualReplace) queueMicrotask(finish)
        return row.promise
      },
    }, redrawOpeningHand: () => { redraws++ },
  }
  const variableNames = new Set(['counterDocumentEpoch', 'detailLoadGeneration', 'detailMounted', 'detailDisposed', 'detailCanonicalNavigation'])
  const variables = file.statements.filter(node => ts.isVariableStatement(node)
    && node.declarationList.declarations.some(row => variableNames.has(row.name.getText(file)))).map(node => node.getText(file))
  const hooks = file.statements.filter(node => ts.isExpressionStatement(node) && ts.isCallExpression(node.expression)
    && (['onMounted', 'onBeforeUnmount'].includes(node.expression.expression.getText(file))
      || node.expression.expression.getText(file) === 'watch' && node.expression.arguments[0]?.getText(file).includes('route.fullPath')))
    .map(node => node.getText(file))
  const names = ['publicDeckReference', 'singleRouteQuery', 'captureInitialReadContext', 'officialDetailEntry', 'loadDetail',
    'captureCounterContext', 'recordInitialView', 'emptyGuide'].filter(name => declarations.has(name))
  evaluated([...variables, ...names.map(name => declarations.get(name)), ...hooks].join('\n'), dependencies, 'undefined')
  return { entry, catalog, notice, loading, selectedCard, catalogs, gets, presets, replaces, posts, publication, route, vue,
    mount: () => { for (const callback of mounts) callback() },
    navigate: (id, fullPath, name) => { for (const row of replaces) row.cancelled = true; applyRoute(id, fullPath, name) },
    setActor: (id, token) => { actor.set(id, token); for (const row of replaces) row.cancelled = true; trigger() },
    unmount: () => { for (const callback of unmounts) callback(); disposed = true; active.clear() },
    holdReplace: () => { manualReplace = true }, redraws: () => redraws,
    resolveCatalog: index => catalogs[index].resolve([{ id: 'M', nameZh: 'master' }, { id: 'C', nameZh: 'card' }]),
  }
}

async function flushLoad() { for (let index = 0; index < 12; index++) await Promise.resolve() }

async function loadLifecycleChecks(check, helper) {
  await check('load: normal mount loads one body and posts one view', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.gets[0].resolve(row.publication()); await flushLoad()
    assert.equal(row.entry.value.deck.name, 'A'); assert.equal(row.loading.value, false)
    assert.deepEqual(row.posts, [['A', 'view']]); assert.equal(row.gets.length, 1)
  })
  for (const canonical of [false, true]) await check(`load: real Vue ref/reactive ${canonical ? 'canonical' : 'normal'} uses landed proxy for one view`, async () => {
    const row = detailLoadRuntime(helper, true)
    if (canonical) row.navigate('alias-A')
    row.mount(); row.resolveCatalog(0); await flushLoad()
    const raw = row.publication('A'); row.gets[0].resolve(raw); await flushLoad()
    assert.equal(row.vue.isReactive(row.entry.value), true)
    assert.notStrictEqual(row.entry.value.deck, raw.deck)
    assert.deepEqual(row.posts, [['A', 'view']]); assert.equal(row.gets.length, 1)
    assert.equal(row.replaces.length, canonical ? 1 : 0)
  })
  await check('load: own canonical replace preserves query/hash, one GET and one view', async () => {
    const row = detailLoadRuntime(helper); row.navigate('alias-A'); row.route.query = { from: '/decks?tab=plaza' }; row.route.hash = '#guide'
    row.mount(); row.resolveCatalog(0); await flushLoad(); row.gets[0].resolve(row.publication('A')); await flushLoad()
    assert.equal(row.entry.value.id, 'pub-A'); assert.equal(row.route.params.deckId, 'A')
    assert.equal(row.replaces.length, 1); assert.equal(row.catalogs.length, 1); assert.equal(row.gets.length, 1)
    assert.deepEqual(row.replaces[0].target.query, { from: '/decks?tab=plaza' }); assert.equal(row.replaces[0].target.hash, '#guide')
    assert.deepEqual(row.posts, [['A', 'view']]); assert.equal(row.loading.value, false)
  })
  await check('load: route B starts during catalog await, old catalog cannot issue GET', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.navigate('B'); assert.equal(row.catalogs.length, 2)
    row.resolveCatalog(0); await flushLoad(); assert.equal(row.gets.length, 0)
    row.resolveCatalog(1); await flushLoad(); assert.equal(row.gets[0].reference, 'B')
    row.gets[0].resolve(row.publication('B')); await flushLoad()
    assert.equal(row.entry.value.id, 'pub-B'); assert.deepEqual(row.posts, [['B', 'view']])
  })
  await check('load: unmount during catalog await prevents catalog/UI/GET/view writes', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.unmount(); row.resolveCatalog(0); await flushLoad()
    assert.equal(row.catalog.value.length, 0); assert.equal(row.entry.value, null)
    assert.equal(row.gets.length, 0); assert.equal(row.posts.length, 0)
  })
  await check('load: route B loads successfully while old GET A is pending', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.navigate('B'); row.resolveCatalog(1); await flushLoad(); row.gets[1].resolve(row.publication('B')); await flushLoad()
    row.gets[0].resolve(row.publication('A', 'late A')); await flushLoad()
    assert.equal(row.entry.value.id, 'pub-B'); assert.equal(row.entry.value.deck.name, 'B')
    assert.deepEqual(row.posts, [['B', 'view']]); assert.deepEqual(row.gets.map(item => item.reference), ['A', 'B'])
  })
  await check('load: unmount during GET prevents late entry/notice/view writes', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.unmount(); row.gets[0].resolve(row.publication('A', 'late A')); await flushLoad()
    assert.equal(row.entry.value, null); assert.equal(row.notice.value, ''); assert.equal(row.posts.length, 0)
  })
  for (const stage of ['catalog', 'get']) await check(`load: ${stage} await route A-B-A keeps only fresh A`, async () => {
    const row = detailLoadRuntime(helper); row.mount()
    if (stage === 'get') { row.resolveCatalog(0); await flushLoad() }
    row.navigate('B'); row.navigate('A'); row.resolveCatalog(2); await flushLoad()
    const newest = row.gets.at(-1); newest.resolve(row.publication('A', 'fresh A')); await flushLoad()
    row.resolveCatalog(1); await flushLoad()
    if (stage === 'catalog') row.resolveCatalog(0)
    else row.gets[0].resolve(row.publication('A', 'stale A'))
    await flushLoad(); assert.equal(row.entry.value.deck.name, 'fresh A'); assert.deepEqual(row.posts, [['A', 'view']])
  })
  for (const stage of ['catalog', 'get']) for (const transition of ['account', 'token'])
    await check(`load: ${stage} await ${transition} A-B-A rejects stale owner context`, async () => {
      const row = detailLoadRuntime(helper); row.mount()
      if (stage === 'get') { row.resolveCatalog(0); await flushLoad() }
      row.setActor(transition === 'account' ? 'account-B' : 'account-A', 'auth-B'); row.setActor('account-A', 'auth-A')
      row.resolveCatalog(2); await flushLoad(); row.gets.at(-1).resolve(row.publication('A', 'fresh actor A')); await flushLoad()
      row.resolveCatalog(1); await flushLoad()
      if (stage === 'catalog') row.resolveCatalog(0)
      else row.gets[0].resolve(row.publication('A', 'stale owner A'))
      await flushLoad(); assert.equal(row.entry.value.deck.name, 'fresh actor A'); assert.deepEqual(row.posts, [['A', 'view']])
    })
  await check('load: actor switch immediately removes existing owner proof before new read', async () => {
    const row = detailLoadRuntime(helper); row.mount(); row.resolveCatalog(0); await flushLoad()
    const owned = row.publication(); owned.deck.publicationId = 'pub-A'; owned.deck.publicationVersion = 1
    row.gets[0].resolve(owned); await flushLoad(); assert.equal(row.entry.value.deck.publicationVersion, 1)
    row.setActor('account-B', 'auth-B'); assert.equal(row.entry.value, null)
    row.resolveCatalog(1); await flushLoad(); row.gets[1].resolve(row.publication('A', 'other viewer')); await flushLoad()
    assert.equal(row.entry.value.deck.publicationVersion, null); assert.equal(row.entry.value.deck.name, 'other viewer')
  })
  for (const stage of ['catalog', 'get']) await check(`load: late ${stage} failure never clears new B loading or notice`, async () => {
    const row = detailLoadRuntime(helper); row.mount()
    if (stage === 'get') { row.resolveCatalog(0); await flushLoad() }
    row.navigate('B'); row.resolveCatalog(1); await flushLoad()
    if (stage === 'catalog') row.catalogs[0].reject(new Error('late catalog A failure'))
    else row.gets[0].reject(new Error('late GET A failure'))
    await flushLoad(); assert.equal(row.loading.value, true); assert.equal(row.notice.value, '')
    row.gets.at(-1).resolve(row.publication('B')); await flushLoad()
    assert.equal(row.entry.value.id, 'pub-B'); assert.equal(row.notice.value, ''); assert.equal(row.loading.value, false)
  })
  await check('load: official index path remains local with zero public GET/view POST', async () => {
    const row = detailLoadRuntime(helper); row.navigate('official-0'); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.presets[0].resolve([{ name: 'official preserved', masterId: 'M', cardIds: ['C'], moraleIds: [], specialIds: [] }]); await flushLoad()
    assert.equal(row.entry.value.id, 'official-0'); assert.equal(row.entry.value.deck.name, 'official preserved')
    assert.equal(row.entry.value.official, true); assert.equal(row.gets.length, 0); assert.equal(row.posts.length, 0)
  })
  await check('load: late official preset await cannot overwrite new B', async () => {
    const row = detailLoadRuntime(helper); row.navigate('official-0'); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.navigate('B'); row.resolveCatalog(1); await flushLoad(); row.gets[0].resolve(row.publication('B')); await flushLoad()
    row.presets[0].resolve([{ name: 'late official', masterId: 'M', cardIds: ['C'], moraleIds: [] }]); await flushLoad()
    assert.equal(row.entry.value.id, 'pub-B'); assert.deepEqual(row.posts, [['B', 'view']])
  })
  for (const transition of ['route', 'account', 'unmount']) await check(`load: external ${transition} during canonical await invalidates its old context`, async () => {
    const row = detailLoadRuntime(helper); row.navigate('alias-A'); row.holdReplace(); row.mount(); row.resolveCatalog(0); await flushLoad()
    row.gets[0].resolve(row.publication('A', 'old canonical A')); await flushLoad(); assert.equal(row.replaces.length, 1)
    if (transition === 'unmount') row.unmount()
    else {
      if (transition === 'route') row.navigate('B')
      else row.setActor('account-B', 'auth-B')
      row.resolveCatalog(1); await flushLoad()
      row.gets[1].resolve(row.publication(transition === 'route' ? 'B' : 'A', 'fresh page')); await flushLoad()
      if (transition === 'account') { row.replaces[1].finish(); await flushLoad() }
    }
    row.replaces[0].finish(); await flushLoad()
    if (transition === 'unmount') { assert.equal(row.entry.value, null); assert.equal(row.posts.length, 0) }
    else { assert.equal(row.entry.value.deck.name, 'fresh page'); assert.deepEqual(row.posts, [[transition === 'route' ? 'B' : 'A', 'view']]) }
  })
}

async function main() {
  const helperSource = read('src/l12/site/publicDeckEntry.ts')
  const helper = evaluated(helperSource, {}, 'exports')
  let passed = 0
  async function check(label, action) {
    await action(); passed++; console.log(`PASS ${passed}: ${label}`)
  }
  if (process.env.L12_DETAIL_LOAD_VUE_ONLY === '1') {
    const row = detailLoadRuntime(helper, true); row.mount(); row.resolveCatalog(0); await flushLoad()
    const raw = row.publication(); row.gets[0].resolve(raw); await flushLoad()
    console.log(JSON.stringify({ actualVueReactive: row.vue.isReactive(row.entry.value),
      landedBodyIsProxy: row.entry.value.deck !== raw.deck, posts: row.posts }))
    assert.deepEqual(row.posts, [['A', 'view']], 'A normal load must POST exactly one view with a real Vue ref proxy')
    return
  }
  if (process.env.L12_DETAIL_LOAD_BASELINE_ONLY === '1') {
    const old = detailLoadRuntime(helper)
    old.mount(); old.resolveCatalog(0); await flushLoad()
    assert.equal(old.gets[0].reference, 'A')
    old.navigate('B'); old.gets[0].resolve(old.publication('A', 'late A')); await flushLoad()
    console.log(JSON.stringify({ oldCallback: 'actual onMounted', route: '/decks/B', displayed: old.entry.value?.deck.name,
      posts: old.posts, detailGetRequests: old.gets.map(row => row.reference) }))
    assert.equal(old.posts.length, 0, 'Cancelled initial GET must not POST a view for its old publication')
    return
  }
  if (process.env.L12_COUNTER_BASELINE_ONLY === '1') {
    const old = pageRuntime('detail', helper, true)
    await old.like()
    console.log(JSON.stringify({ realOldPageCalls: old.counters, bodyReplaced: old.current().deck !== old.body,
      tokenLost: old.current().readToken !== 'captured-token', detailsRetained: old.current().details === old.details }))
    assert.strictEqual(old.current().deck, old.body, 'Counter response must retain the already captured body')
    return
  }
  await check('scalar whitelist preserves body, guide, history, statistics and token references', () => {
    const row = pageRuntime('detail', helper), merged = helper.mergePublicDeckCounters(row.initial, row.scalar())
    assert.strictEqual(merged.deck, row.body); assert.strictEqual(merged.details, row.details)
    assert.equal(merged.readToken, 'captured-token'); assert.equal(merged.liked, true); assert.equal(merged.canEdit, true)
    assert.equal(merged.views, 1); assert.equal(merged.likes, 2); assert.equal(merged.copies, 3)
    assert.equal(row.initial.views, 0)
  })
  for (const [label, change] of [ ['wrong id', value => ({ ...value, id: 'other' })], ['wrong code', value => ({ ...value, publicCode: 'OTHER' })],
    ['negative', value => ({ ...value, views: -1 })], ['fraction', value => ({ ...value, copies: 0.1 })],
    ['overflow', value => ({ ...value, likes: 2147483648 })], ['NaN', value => ({ ...value, views: NaN })],
    ['boolean string', value => ({ ...value, viewerLiked: 'true' })], ['unknown body field', value => ({ ...value, deck: {} })],
    ['missing flag', value => { const result = { ...value }; delete result.canEdit; return result }] ])
    await check(`strict scalar rejection: ${label}`, () => {
      const row = pageRuntime('detail', helper)
      assert.throws(() => helper.mergePublicDeckCounters(row.initial, change(row.scalar())))
      assert.strictEqual(row.current().deck, row.body); assert.equal(row.current().views, 0)
    })
  for (const kind of ['detail', 'library']) {
    await check(`${kind}: actual like handler merges only scalar response`, async () => {
      const row = pageRuntime(kind, helper); await row.like()
      assert.deepEqual(row.counters, [['CODE12', 'like']]); assert.equal(row.current().likes, 2)
      assert.strictEqual(row.current().deck, row.body); assert.strictEqual(row.current().details, row.details)
      assert.equal(row.current().readToken, 'captured-token')
    })
    for (const transition of ['account-aba', 'token-aba', 'route-aba', 'entry', 'body'])
      await check(`${kind}: actual late handler rejects ${transition}`, async () => {
        const row = pageRuntime(kind, helper); let resolve
        row.reply(() => new Promise(done => { resolve = done }))
        const request = row.like()
        assert.equal(row.counters.length, 1)
        if (transition === 'account-aba') { row.setActor('B', 'token-B'); row.setActor('A', 'token-A') }
        if (transition === 'token-aba') { row.setActor('A', 'new-token'); row.setActor('A', 'token-A') }
        if (transition === 'route-aba') { row.setRoute('/decks/other'); row.setRoute('/decks/CODE12') }
        if (transition === 'entry') row.replace({ ...row.initial, id: 'other' })
        if (transition === 'body') row.replace({ ...row.initial, deck: { ...row.body, name: 'new document' } })
        resolve(row.scalar()); await request
        assert.equal(row.current().likes, 0); assert.equal(row.notice.value, '')
      })
    await check(`${kind}: confirmed copy stays successful when counter fails`, async () => {
      const row = pageRuntime(kind, helper); row.reply(async () => { throw new Error('synthetic counter failure') })
      await row.copy(); assert.equal(row.saves.length, 1); assert.deepEqual(row.counters, [['CODE12', 'copy']])
      assert.match(row.notice.value, /已复制.*confirmed copy/); assert.strictEqual(row.current().deck, row.body)
    })
    await check(`${kind}: serverConfirmed cache failure never repeats save or counts`, async () => {
      const row = pageRuntime(kind, helper)
      row.saveReply(async () => { throw Object.assign(new Error('服务器已确认；本机缓存未更新'), { serverConfirmed: true }) })
      await row.copy(); assert.equal(row.saves.length, 1); assert.equal(row.counters.length, 0)
      assert.match(row.notice.value, /服务器已确认/)
    })
    for (const officialFlag of ['official', 'source']) await check(`${kind}: ${officialFlag}=official has no counter POST`, async () => {
      const row = pageRuntime(kind, helper); row.replace({ ...row.initial, ...(officialFlag === 'official' ? { official: true } : { source: 'official' }) })
      await row.like(); await row.copy(); assert.equal(row.saves.length, 1); assert.equal(row.counters.length, 0)
      if (kind === 'detail') { await row.view(); assert.equal(row.counters.length, 0) }
    })
  }
  await check('actual view handler posts once and retains full content', async () => {
    const row = pageRuntime('detail', helper); await row.view(); await row.view()
    assert.deepEqual(row.counters, [['CODE12', 'view']]); assert.strictEqual(row.current().details, row.details)
    assert.strictEqual(row.current().deck, row.body); assert.equal(row.current().readToken, 'captured-token')
  })
  await check('failed view has one POST, no retry or fallback and removes only its own marker', async () => {
    const row = pageRuntime('detail', helper); row.reply(async () => { throw new Error('synthetic') }); await row.view()
    assert.equal(row.counters.length, 1); assert.equal(row.marker.size, 0); assert.equal(row.current().views, 0)
  })
  await check('view route change rejects a late scalar reply', async () => {
    const row = pageRuntime('detail', helper); let resolve
    row.reply(() => new Promise(done => { resolve = done })); const request = row.view()
    row.setRoute('/elsewhere'); resolve(row.scalar()); await request; assert.equal(row.current().views, 0)
  })
  await check('view action disposal rejects a late scalar reply', async () => {
    const row = pageRuntime('detail', helper); let resolve
    row.reply(() => new Promise(done => { resolve = done })); const request = row.view()
    row.disposeAction(); resolve(row.scalar()); await request; assert.equal(row.current().views, 0)
  })
  for (const kind of ['view', 'copy', 'like']) await check(`actual API ${kind}: failed POST occurs once without legacy fallback`, async () => {
    const request = requestRuntime(); await assert.rejects(request.counter('A/B', kind))
    assert.equal(request.requests.length, 1); assert.equal(request.requests[0].url, `http://counter.invalid/api/public-decks/A%2FB/counters/${kind}`)
    assert.deepEqual(request.configs, [{ method: 'POST', maxAttempts: 1 }])
  })
  await check('existing request function ignores mutation maxAttempts escalation', async () => {
    const request = requestRuntime()
    await assert.rejects(request.platformRequest('/api/public-decks/p/counters/copy', { method: 'POST', reliability: { maxAttempts: 10 } }))
    assert.equal(request.requests.length, 1); assert.equal(request.configs[0].maxAttempts, 1)
  })
  const detail = read('src/l12/site/PublicDeckDetailPage.vue'), library = read('src/l12/site/DeckLibraryPage.vue')
  await check('counter consumer binding contract accepts actual scalar wiring', () => {
    assert.equal(publicDeckCounterBindingsContract({ detail, library, helper: helperSource }), true)
  })
  for (const [label, mutation] of [['DTO spread', helperSource.replace('return { ...current,', 'return { ...counters,')],
    ['wrong likes field', helperSource.replace('likes: counters.likes', 'likes: counters.views')]])
    await check(`counter consumer negative contract: ${label}`, () => {
      assert.equal(publicDeckCounterBindingsContract({ detail, library, helper: mutation }), false)
    })
  await check('counter consumer negative contract: actor guard removed', () => {
    assert.equal(publicDeckCounterBindingsContract({ detail: detail.replace('actorCurrent: captureDeckAccountGuard()', 'actorCurrent: () => true'), library, helper: helperSource }), false)
  })
  await check('counter consumer negative contract: legacy fallback added', () => {
    assert.equal(publicDeckCounterBindingsContract({ detail: detail.replace('const counters = await publicDeckApi.counter(reference,', 'await publicDeckApi.toggleLike(reference); const counters = await publicDeckApi.counter(reference,'), library, helper: helperSource }), false)
  })
  await loadLifecycleChecks(check, helper)
  console.log(`C-U1 focused Node result: ${passed}/${passed}, failed=0, skipped=0`)
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url)
  await main()
