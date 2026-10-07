import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const scriptRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const sourceRoot = path.resolve(process.env.L12_READ_SOURCE_ROOT || scriptRoot)
const dependencyRoot = path.resolve(process.env.L12_READ_DEPENDENCY_ROOT || scriptRoot)
const requireDependency = createRequire(path.join(dependencyRoot, 'package.json'))
const ts = requireDependency('typescript')
const read = relative => fs.readFileSync(path.join(sourceRoot, relative), 'utf8')
const scriptOf = source => source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? source

function parsed(source, name = 'public-deck-read.ts') {
  const file = ts.createSourceFile(name, scriptOf(source), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0, `${name} must parse`)
  return file
}

function functionSource(source, name) {
  const file = parsed(source)
  const node = file.statements.find(row => ts.isFunctionDeclaration(row) && row.name?.text === name)
  assert.ok(node, `${name} must exist`)
  return node.getText(file)
}

function javascript(source) {
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

function variableInitializer(source, name) {
  const file = parsed(source)
  const node = file.statements.filter(ts.isVariableStatement).flatMap(row => row.declarationList.declarations)
    .find(row => row.name.getText(file) === name)
  assert.ok(node?.initializer, `${name} must exist`)
  return node.initializer.getText(file)
}

const token = 'a'.repeat(64)
const generation = { id: 'publication-A', publicCode: 'CODE-A', readToken: token, catalogVersion: 'catalog-1', policyVersion: 7 }
const counts = () => ({ main: 50, uncountedMain: 0, morale: 10, special: 3, bench: 0 })
const environment = () => ({ status: 'known', value: '2.5', reason: null })
function summary(owner = false, readToken = null) {
  return { id: generation.id, source: 'public', name: '牌库 A', masterId: 'M', masterName: '主宰', faction: '阵营', author: '作者',
    publicCode: generation.publicCode, publicationVersion: owner ? 135 : null,
    createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-07T00:00:00Z', counts: counts(), legal: true,
    legalityReason: null, environment: environment(), views: 4, likes: 3, copies: 2, viewerLiked: owner,
    canEdit: owner, readToken }
}
function deck(owner = false, version = 135, name = '牌库 A') {
  return { name, masterId: 'M', cardIds: ['C1', 'C1'], moraleIds: ['R1'], specialIds: ['S1'],
    updatedAt: '2026-10-07T00:00:00Z', publicationId: owner ? generation.id : null,
    publicationVersion: owner ? version : null }
}
function current(owner = false) {
  return { summary: summary(owner), version: 135, deck: deck(owner),
    guide: { buildIdea: '思路', opening: '起手', keyCards: '关键', commonSequence: '展开', substitutions: '替换' },
    matchups: [{ opponentMasterId: 'O', notes: '思路', keyCards: '关键', suggestedSwaps: '换牌' }],
    contentRevision: 2, contentUpdatedAt: '2026-10-07T00:00:00Z', readToken: token,
    catalogVersion: generation.catalogVersion, policyVersion: generation.policyVersion }
}
function ownerCurrent(id = generation.id, publicCode = generation.publicCode, readToken = token, content = {}) {
  const value = current(true)
  value.summary.id = id; value.summary.publicCode = publicCode
  value.deck.publicationId = id; value.readToken = readToken
  Object.assign(value, content)
  return value
}
function contentCurrent(guide = current(true).guide, matchups = current(true).matchups,
  readToken = 'b'.repeat(64), contentRevision = 3) {
  return { id: generation.id, publicCode: generation.publicCode, guide, matchups, contentRevision,
    contentUpdatedAt: '2026-10-07T01:00:00Z', readToken,
    catalogVersion: generation.catalogVersion, policyVersion: generation.policyVersion, canEdit: true }
}
function localDeck(name = '本地牌库') {
  return { id: 'private-A', revision: 1, publicationId: null, publicationVersion: null, name, masterId: 'M',
    cardIds: ['C1', 'C1'], moraleIds: ['R1'], specialIds: ['S1'], updatedAt: '2026-10-07T00:00:00Z' }
}
function publishedEntry(source = localDeck()) {
  return { id: generation.id, publicCode: generation.publicCode, ownerId: 'owner', author: '作者', views: 0, likes: 0,
    copies: 0, liked: false, createdAt: '2026-10-07T00:00:00Z', updatedAt: '2026-10-07T00:00:00Z',
    deck: { ...structuredClone(source), publicationId: generation.id, publicationVersion: 1 } }
}
function metadata(version, changes = version === 1 ? [] : [{ section: 'main', cardId: `C${version}`, previousQuantity: 0, currentQuantity: 1 }]) {
  return { version, name: `版本 ${version}`, masterId: 'M', createdAt: '2026-10-07T00:00:00Z', counts: counts(),
    legal: true, legalityReason: null, environment: environment(), changes }
}
function versionPage(page = 1, pageSize = 30, total = 135, items = [metadata(135)], owner = false) {
  return { ...generation, items, total, page, pageSize, canEdit: owner }
}
function versionRead(version = 7, owner = false) {
  return { ...generation, metadata: metadata(version), deck: deck(owner, version, `版本 ${version}`), canEdit: owner }
}
function statisticGroup(version = 7, masterId = 'M', opponentMasterId = 'O') {
  return { version, masterId, opponentMasterId, games: 3, wins: 1, losses: 1, draws: 1, winRate: 0.3333 }
}
function statistics(page = 1, pageSize = 30, total = 1, groups = [statisticGroup()], status = 'available') {
  return { ...generation, from: '2026-07-09T00:00:00Z', to: '2026-10-07T00:00:00Z', recentDays: 90,
    games: status === 'available' ? Math.max(3, total * 3) : 0, sampleStatus: status,
    groups: status === 'available' ? groups : [], total: status === 'available' ? total : 0, page, pageSize }
}

async function oldLoadUsesLegacyBody() {
  const baseline = process.env.L12_READ_BASELINE_DETAIL
    || path.resolve(sourceRoot, '../.tmp/c-u3-read/before/opcgpro-vue/src/l12/site/PublicDeckDetailPage.vue')
  const source = fs.readFileSync(baseline, 'utf8')
  const calls = []
  const state = {
    route: { name: 'public-deck-detail', params: { deckId: 'legacy-A' }, fullPath: '/decks/legacy-A', query: {}, hash: '' },
    entry: { value: null }, catalog: { value: [] }, selectedCard: { value: null }, openingHandIds: { value: [] },
    imagePreview: { value: null }, notice: { value: '' }, loading: { value: true }, master: { value: null },
  }
  const body = { id: 'pub-A', publicCode: 'A', ownerId: 'owner', author: 'author',
    deck: { name: 'legacy body', masterId: 'M', cardIds: ['C'], moraleIds: [], specialIds: [], updatedAt: 'now' },
    views: 0, likes: 0, copies: 0, liked: false, createdAt: 'now', updatedAt: 'now' }
  const code = `let detailLoadGeneration=0,detailMounted=true,detailDisposed=false,counterDocumentEpoch=0,detailCanonicalNavigation=null;\n${functionSource(source, 'loadDetail')}`
  const loadDetail = evaluated(code, {
    ...state, captureDeckAccountGuard: () => () => true, loadDeckCatalog: async () => [],
    publicDeckApi: { get: async reference => { calls.push(['legacy', reference]); return body } },
    resolveOfficialDeck: async () => { throw new Error('unexpected official read') }, loadOfficialPresetDecks: async () => [],
    publicDeckRouteReference: value => value.publicCode, router: { resolve: target => ({ fullPath: `/decks/${target.params.deckId}` }), replace: async () => undefined },
    redrawOpeningHand: () => {}, isOfficialPublicDeckCounterTarget: () => false, recordInitialView: async () => {}, URL,
  }, 'loadDetail')
  await loadDetail()
  return calls
}

function apiRuntime() {
  const source = read('src/l12/platform.ts'), requests = []
  const code = [functionSource(source, 'publicDeckReadReference'), functionSource(source, 'publicDeckReadToken'),
    functionSource(source, 'publicDeckReadPage'), `const publicDeckReadApi=${variableInitializer(source, 'publicDeckReadApi')};`].join('\n')
  const publicDeckReadApi = evaluated(code, { URLSearchParams,
    platformRequest: (value, options) => { requests.push(options ? { url: value, options } : value); return value } }, 'publicDeckReadApi')
  return { publicDeckReadApi, requests }
}

function initialContextRuntime(route, captureDeckAccountGuard, consumeSummaryOpen) {
  const source = read('src/l12/site/PublicDeckDetailPage.vue')
  return evaluated(`let detailLoadGeneration=0,counterDocumentEpoch=0;\n${functionSource(source, 'singleRouteQuery')}\n${functionSource(source, 'captureInitialReadContext')}`,
    { route, captureDeckAccountGuard, consumeSummaryOpen }, 'captureInitialReadContext')
}

function pinnedContextRuntime(entry, route) {
  const source = read('src/l12/site/PublicDeckDetailPage.vue')
  let actorEpoch = 0
  const captureDeckAccountGuard = () => { const captured = actorEpoch; return () => captured === actorEpoch }
  const runtime = evaluated(`let counterDocumentEpoch=0,detailLoadGeneration=1,detailMounted=true,detailDisposed=false;\n${functionSource(source, 'capturePinnedReadContext')}\n({capture:capturePinnedReadContext,document:()=>counterDocumentEpoch++,load:()=>detailLoadGeneration++,dispose:()=>detailDisposed=true})`,
    { entry, route, captureDeckAccountGuard }, '({capture:capturePinnedReadContext,document:()=>counterDocumentEpoch++,load:()=>detailLoadGeneration++,dispose:()=>detailDisposed=true})')
  return { ...runtime, actor: () => actorEpoch++ }
}

function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
async function flush() { for (let index = 0; index < 12; index++) await Promise.resolve() }

function auxiliaryRuntime(helper) {
  const source = read('src/l12/site/PublicDeckDetailPage.vue')
  const entry = vueDependency().ref(helper.toPublicDeckCurrentEntry(current(false)))
  const route = vueDependency().reactive({ name: 'public-deck-detail', fullPath: '/decks/CODE-A' })
  const versionsState = vueDependency().ref('idle'), versionsNotice = vueDependency().ref(''), versionsPageRef = vueDependency().ref(null)
  const selectedVersionState = vueDependency().ref('idle'), selectedVersionNotice = vueDependency().ref(''), selectedVersion = vueDependency().ref(null)
  const statisticsState = vueDependency().ref('idle'), statisticsNotice = vueDependency().ref(''), statisticsPageRef = vueDependency().ref(null)
  const notice = vueDependency().ref(''), refreshRequired = vueDependency().ref(false)
  const versionRows = [], selectedRows = [], statisticRows = []
  let actorEpoch = 0
  const captureDeckAccountGuard = () => { const captured = actorEpoch; return () => captured === actorEpoch }
  const publicDeckReadApi = {
    versions: (reference, page, pageSize, readToken) => { const row = { reference, page, pageSize, readToken, ...deferred() }; versionRows.push(row); return row.promise },
    version: (reference, version, readToken) => { const row = { reference, version, readToken, ...deferred() }; selectedRows.push(row); return row.promise },
    statistics: (reference, page, pageSize, readToken) => { const row = { reference, page, pageSize, readToken, ...deferred() }; statisticRows.push(row); return row.promise },
  }
  const code = `let counterDocumentEpoch=0,detailLoadGeneration=1,detailMounted=true,detailDisposed=false;
    let versionsRequestSequence=0,selectedVersionRequestSequence=0,statisticsRequestSequence=0;
    ${functionSource(source, 'capturePinnedReadContext')}
    ${functionSource(source, 'requireReadRefresh')}
    ${functionSource(source, 'loadVersionPage')}
    ${functionSource(source, 'loadSelectedVersion')}
    ${functionSource(source, 'loadStatisticsPage')}`
  const methods = evaluated(code, { entry, route, captureDeckAccountGuard, versionsState, versionsNotice,
    versionsPage: versionsPageRef, selectedVersionState, selectedVersionNotice, selectedVersion,
    statisticsState, statisticsNotice, statisticsPage: statisticsPageRef, notice, refreshRequired,
    publicDeckReadApi, settlePublicDeckRead: helper.settlePublicDeckRead,
    validatePublicDeckVersionPage: helper.validatePublicDeckVersionPage,
    validatePublicDeckVersion: helper.validatePublicDeckVersion,
    validatePublicDeckStatistics: helper.validatePublicDeckStatistics,
  }, '({loadVersionPage,loadSelectedVersion,loadStatisticsPage,document:()=>counterDocumentEpoch++,load:()=>detailLoadGeneration++})')
  return { ...methods, entry, route, versionsState, versionsNotice, versionsPage: versionsPageRef,
    selectedVersionState, selectedVersionNotice, selectedVersion, statisticsState, statisticsNotice,
    statisticsPage: statisticsPageRef, notice, refreshRequired, versionRows, selectedRows, statisticRows,
    actor: () => actorEpoch++ }
}

function contentEditorRuntime(helper, publicationId = generation.id) {
  const vue = vueDependency(), source = read('src/l12/site/PublicDeckContentEditor.vue')
  const props = vue.reactive({ publicationId, catalog: [] })
  const platformState = vue.reactive({ account: { id: 'owner' }, token: 'session-owner' })
  const currentRows = [], updateRows = [], events = [], pending = new Set()
  let actorEpoch = 0
  const captureDeckAccountGuard = () => {
    const captured = `${actorEpoch}:${platformState.account?.id ?? ''}:${platformState.token}`
    return () => captured === `${actorEpoch}:${platformState.account?.id ?? ''}:${platformState.token}`
  }
  const publicDeckReadApi = {
    current: (reference, expectedReadToken) => { const row = { reference, expectedReadToken, ...deferred() }; currentRows.push(row); return row.promise },
    updateContent: (reference, expectedReadToken, guide, matchups) => {
      const row = { reference, expectedReadToken, guide, matchups, ...deferred() }; updateRows.push(row); return row.promise
    },
  }
  const isPending = key => pending.has(key)
  const run = async (key, action) => {
    if (pending.has(key)) return
    pending.add(key)
    try { return await action() } finally { pending.delete(key) }
  }
  const code = `
    const guide=ref(emptyGuide()),matchups=ref([]),loading=ref(false),error=ref(''),revision=ref(0),updatedAt=ref('');
    const readGeneration=ref(null),refreshRequired=ref(false),matchupPickerIndex=ref(null);
    let contentEpoch=0,contentRequestSequence=0,draftEpoch=0,draftBaselineKey='';
    const actionKey=computed(()=>\`public-deck:\${platformState.account?.id ?? 'anonymous'}:\${props.publicationId}\`);
    ${functionSource(source, 'emptyGuide')}
    ${functionSource(source, 'contentDraftKey')}
    ${functionSource(source, 'loadContent')}
    ${functionSource(source, 'saveContent')}
    ${functionSource(source, 'refreshContentGeneration')}
    watch(()=>JSON.stringify({guide:guide.value,matchups:matchups.value}),()=>draftEpoch++,{flush:'sync'});
  `
  const runtime = evaluated(code, { ref: vue.ref, computed: vue.computed, watch: vue.watch, props, platformState,
    captureDeckAccountGuard, publicDeckReadApi, settlePublicDeckRead: helper.settlePublicDeckRead,
    validatePublicDeckCurrent: helper.validatePublicDeckCurrent, validatePublicDeckContentCurrent: helper.validatePublicDeckContentCurrent,
    currentReadGeneration: helper.currentReadGeneration, isPending, run,
    emit: (name, message) => events.push([name, message]) },
  '({loadContent,saveContent,refreshContentGeneration,guide,matchups,loading,error,revision,updatedAt,readGeneration,refreshRequired,dispose:()=>{contentEpoch++;contentRequestSequence++},draftEpoch:()=>draftEpoch})')
  return { ...runtime, props, platformState, currentRows, updateRows, events,
    actor: () => actorEpoch++, setPublication: value => { props.publicationId = value } }
}

function deckLibraryRuntime(helper, initialDeck, options = {}) {
  const vue = vueDependency(), source = read('src/l12/site/DeckLibraryPage.vue')
  const clone = value => JSON.parse(JSON.stringify(value))
  const platformState = vue.reactive({ account: { id: 'owner' }, token: 'session-owner' })
  const route = vue.reactive({ fullPath: '/decks?tab=mine' }), tab = vue.ref('mine')
  const storage = { [initialDeck.name]: clone(initialDeck) }
  const publishRows = [], currentRows = [], updateRows = [], saveRows = [], saveCalls = [], pendingActions = new Set()
  let actorEpoch = 0, summaryLoads = 0, referenceLoads = 0
  const captureDeckAccountGuard = () => {
    const captured = `${actorEpoch}:${platformState.account?.id ?? ''}:${platformState.token}`
    return () => captured === `${actorEpoch}:${platformState.account?.id ?? ''}:${platformState.token}`
  }
  const publicDeckApi = {
    publish: deck => { const row = { deck: clone(deck), ...deferred() }; publishRows.push(row); return row.promise },
  }
  const publicDeckReadApi = {
    current: (reference, expectedReadToken) => { const row = { reference, expectedReadToken, ...deferred() }; currentRows.push(row); return row.promise },
    updateContent: (reference, expectedReadToken, guide, matchups) => {
      const row = { reference, expectedReadToken, guide, matchups, ...deferred() }; updateRows.push(row); return row.promise
    },
  }
  const saveDeck = value => {
    saveCalls.push(clone(value))
    if (options.saveError) return Promise.reject(options.saveError)
    const confirmed = { ...clone(value), id: value.id ?? 'private-A', revision: (value.revision ?? 0) + 1 }
    const commit = () => { storage[confirmed.name] = confirmed; return clone(confirmed) }
    if (!options.deferSave) return Promise.resolve(commit())
    const row = { value: clone(value), confirmed: clone(confirmed), ...deferred() }
    saveRows.push(row)
    return row.promise.then(commit)
  }
  const loadSavedDecks = () => clone(storage)
  const runAction = async (key, action) => {
    if (pendingActions.has(key)) return
    pendingActions.add(key)
    try { return await action() } finally { pendingActions.delete(key) }
  }
  const actionPending = key => pendingActions.has(key)
  const code = `
    const saved=ref(loadSavedDecks()),publishName=ref(${JSON.stringify(initialDeck.name)}),showPublish=ref(true),notice=ref('');
    const publishGuide=ref({buildIdea:'',opening:'',keyCards:'',commonSequence:'',substitutions:''}),publishMatchups=ref([]);
    let libraryAccountEpoch=0,pendingPublicDeckPublish=null,publishRequestSequence=0;
    ${functionSource(source, 'libraryContext')}
    ${functionSource(source, 'libraryContextCurrent')}
    ${functionSource(source, 'publishDeckBodyKey')}
    ${functionSource(source, 'normalizedPublishContent')}
    ${functionSource(source, 'publishContentKey')}
    ${functionSource(source, 'publishContentHasValues')}
    ${functionSource(source, 'matchingPendingPublish')}
    ${functionSource(source, 'publishDeck')}
  `
  const runtime = evaluated(code, { ref: vue.ref, route, tab, platformState, captureDeckAccountGuard,
    validateDeck: () => '', catalog: vue.ref([]), runAction, actionPending, publicDeckApi, publicDeckReadApi,
    publicDeckRouteReference: entry => entry.publicCode, saveDeck, loadSavedDecks,
    settlePublicDeckRead: helper.settlePublicDeckRead, validatePublicDeckCurrent: helper.validatePublicDeckCurrent,
    currentReadGeneration: helper.currentReadGeneration, validatePublicDeckContentCurrent: helper.validatePublicDeckContentCurrent,
    loadSummarySources: () => { summaryLoads++ }, loadOwnReferences: () => { referenceLoads++ },
    deckErrorBelongsToCurrentAccount: () => true },
  '({publishDeck,saved,publishName,showPublish,notice,publishGuide,publishMatchups,pending:()=>pendingPublicDeckPublish,dispose:()=>{publishRequestSequence++;pendingPublicDeckPublish=null},invalidateAccount:()=>{libraryAccountEpoch++;publishRequestSequence++;pendingPublicDeckPublish=null}})')
  return { ...runtime, platformState, route, tab, publishRows, currentRows, updateRows, saveRows, saveCalls,
    summaryLoads: () => summaryLoads, referenceLoads: () => referenceLoads, actor: () => actorEpoch++,
    changeAccount: () => { actorEpoch++; platformState.account = { id: 'other' }; platformState.token = 'session-other'; runtime.invalidateAccount() } }
}

function vueDependency() { return requireDependency('vue') }

async function main() {
  if (process.env.L12_PUBLIC_DECK_READ_BASELINE_ONLY === '1') {
    const calls = await oldLoadUsesLegacyBody()
    console.log(JSON.stringify({ actualOldLoadCalls: calls }))
    assert.deepEqual(calls, [['current', 'legacy-A']], '社区详情必须改为有界 current wire，不能继续读取旧 full DTO')
    return
  }

  const helper = productionModule('src/l12/site/publicDeckRead.ts')
  const summaryHelper = productionModule('src/l12/site/publicDeckSummary.ts')
  const vue = requireDependency('vue')
  let passed = 0
  async function check(label, action) { await action(); passed++; console.log(`PASS ${passed}: ${label}`) }

  await check('current accepts anonymous body with null nested summary pin and real top-level pin', () => {
    const value = helper.validatePublicDeckCurrent(current(false))
    assert.equal(value.summary.readToken, null); assert.equal(value.readToken, token)
    assert.equal(value.deck.publicationId, null)
  })
  await check('current accepts exact owner provenance only', () => {
    const value = helper.validatePublicDeckCurrent(current(true))
    assert.equal(value.summary.publicationVersion, 135); assert.equal(value.deck.publicationId, generation.id)
  })
  for (const [label, mutate] of [
    ['foreign owner id', value => { value.deck.publicationId = 'foreign'; return value }],
    ['foreign owner version', value => { value.deck.publicationVersion = 134; return value }],
    ['anonymous owner leak', value => { value.deck.publicationId = value.summary.id; return value }],
    ['nested directory pin', value => { value.summary.readToken = token; return value }],
    ['legacy full details', value => Object.assign(value, { details: {} })],
    ['legacy versions', value => Object.assign(value, { versions: [] })],
    ['legacy statistics', value => Object.assign(value, { matchStatistics: {} })],
    ['malformed top pin', value => { value.readToken = 'A'.repeat(64); return value }],
  ]) await check(`current rejects ${label}`, () => {
    const owner = label.startsWith('foreign owner')
    assert.throws(() => helper.validatePublicDeckCurrent(mutate(structuredClone(current(owner)))))
  })
  await check('current maps to a new bounded presentation entry without old full DTO', () => {
    const value = helper.toPublicDeckCurrentEntry(current(false))
    assert.equal(value.readToken, token); assert.equal(value.publicCode, generation.publicCode)
    assert.equal(Object.hasOwn(value, 'details'), false); assert.equal(Object.hasOwn(value, 'ownerId'), false)
  })
  await check('true Vue ref lands the bounded body as a reactive proxy', () => {
    const raw = helper.toPublicDeckCurrentEntry(current(false)), entry = vue.ref(null)
    entry.value = raw
    assert.equal(vue.isReactive(entry.value), true); assert.notStrictEqual(entry.value.deck, raw.deck)
    assert.equal(entry.value.readToken, token)
  })

  await check('metadata page stays body-free and ordered newest first', () => {
    const items = [metadata(35), metadata(34), metadata(33)]
    const value = helper.validatePublicDeckVersionPage(versionPage(1, 30, 35, items), generation, 1, 30, false)
    assert.deepEqual(value.items.map(item => item.version), [35, 34, 33])
    assert.equal(value.items.some(item => Object.hasOwn(item, 'deck')), false)
  })
  for (const [label, mutate] of [
    ['embedded body', value => { value.items[0].deck = deck(); return value }],
    ['duplicate version', value => { value.items.push(structuredClone(value.items[0])); return value }],
    ['wrong catalog', value => { value.catalogVersion = 'other'; return value }],
    ['wrong policy', value => { value.policyVersion++; return value }],
    ['wrong pin', value => { value.readToken = 'b'.repeat(64); return value }],
  ]) await check(`metadata rejects ${label}`, () => {
    assert.throws(() => helper.validatePublicDeckVersionPage(mutate(versionPage()), generation, 1, 30, false))
  })
  await check('135 histories remain reachable on a bounded fifth page', () => {
    const items = Array.from({ length: 15 }, (_, index) => metadata(15 - index))
    const value = helper.validatePublicDeckVersionPage(versionPage(5, 30, 135, items), generation, 5, 30, false)
    assert.equal(value.items.length, 15); assert.equal(Math.ceil(value.total / value.pageSize), 5)
  })
  await check('selected history expands exactly one body', () => {
    const value = helper.validatePublicDeckVersion(versionRead(7), generation, 7, false)
    assert.equal(value.metadata.version, 7); assert.equal(Array.isArray(value.deck.cardIds), true)
    assert.equal(Object.hasOwn(value, 'items'), false)
  })
  await check('selected history rejects a mismatched version and owner proof', () => {
    assert.throws(() => helper.validatePublicDeckVersion(versionRead(7), generation, 8, false))
    const value = versionRead(7, false); value.deck.publicationId = generation.id
    assert.throws(() => helper.validatePublicDeckVersion(value, generation, 7, false))
  })

  await check('available statistics validate the actual 90-day three-game group', () => {
    const value = helper.validatePublicDeckStatistics(statistics(), generation, 1, 30)
    assert.equal(value.sampleStatus, 'available'); assert.equal(value.groups[0].winRate, 0.3333)
  })
  for (const status of ['empty', 'insufficient']) await check(`${status} statistics remain an explicit available response`, () => {
    const value = helper.validatePublicDeckStatistics(statistics(1, 30, 0, [], status), generation, 1, 30)
    assert.equal(value.sampleStatus, status); assert.equal(value.groups.length, 0)
  })
  await check('105 statistic groups remain reachable on page four', () => {
    const groups = Array.from({ length: 15 }, (_, index) => statisticGroup(15 - index, `M${index}`, `O${index}`))
    const value = helper.validatePublicDeckStatistics(statistics(4, 30, 105, groups), generation, 4, 30)
    assert.equal(value.groups.length, 15); assert.equal(Math.ceil(value.total / value.pageSize), 4)
  })
  for (const [label, mutate] of [
    ['sub-threshold group', value => { value.groups[0].games = 2; value.groups[0].wins = 1; value.groups[0].losses = 1; value.groups[0].draws = 0; value.groups[0].winRate = 0.5; return value }],
    ['bad outcome sum', value => { value.groups[0].draws = 0; return value }],
    ['wrong generation', value => { value.catalogVersion = 'other'; return value }],
    ['fake empty fallback', value => { value.sampleStatus = 'empty'; return value }],
  ]) await check(`statistics reject ${label}`, () => {
    assert.throws(() => helper.validatePublicDeckStatistics(mutate(statistics()), generation, 1, 30))
  })

  await check('409 becomes refresh-required without an unpinned fallback', async () => {
    const error = Object.assign(new Error('conflict'), { status: 409, code: 'public_deck_read_conflict' })
    const result = await helper.settlePublicDeckRead(Promise.reject(error), helper.validatePublicDeckCurrent)
    assert.equal(result.status, 'refresh-required'); assert.match(result.message, /刷新/)
  })
  await check('storage failure stays unavailable rather than fake empty', async () => {
    const error = Object.assign(new Error('schema internals'), { status: 503, code: 'storage_unavailable' })
    const result = await helper.settlePublicDeckRead(Promise.reject(error), value => value)
    assert.equal(result.status, 'unavailable'); assert.doesNotMatch(result.message, /schema/)
  })
  await check('malformed success stays unavailable rather than exposing validator internals', async () => {
    const result = await helper.settlePublicDeckRead(Promise.resolve({}), helper.validatePublicDeckCurrent)
    assert.equal(result.status, 'unavailable'); assert.match(result.message, /无法确认/)
  })
  await check('thin content response accepts canonical change and exact idempotent replay only', () => {
    const changed = helper.validatePublicDeckContentCurrent(contentCurrent(), generation, 2)
    assert.equal(changed.contentRevision, 3); assert.equal(changed.canEdit, true)
    const replay = contentCurrent(current(true).guide, current(true).matchups, token, 2)
    assert.equal(helper.validatePublicDeckContentCurrent(replay, generation, 2).readToken, token)
    for (const mutate of [
      value => Object.assign(value, { deck: {} }),
      value => { value.canEdit = false; return value },
      value => { value.catalogVersion = 'foreign'; return value },
      value => { value.contentRevision = 1; return value },
    ]) assert.throws(() => helper.validatePublicDeckContentCurrent(mutate(contentCurrent()), generation, 2))
  })

  await check('directory ticket is one-use and the retained actor guard outlives ticket TTL', () => {
    let actor = true, now = 10_000
    const originalNow = Date.now; Date.now = () => now
    try {
      const open = summaryHelper.rememberSummaryOpen({ ...summary(), readToken: token }, () => actor)
      const receipt = summaryHelper.consumeSummaryOpen(open.query.summaryOpen, open.reference, token)
      assert.ok(receipt); assert.equal(summaryHelper.consumeSummaryOpen(open.query.summaryOpen, open.reference, token), null)
      now += 600_000; assert.equal(receipt.actorCurrent(), true); actor = false; assert.equal(receipt.actorCurrent(), false)
    } finally { Date.now = originalNow }
  })
  await check('expired, mismatched and actor-changed tickets cannot authorize initial current', () => {
    let actor = true, now = 20_000
    const originalNow = Date.now; Date.now = () => now
    try {
      const expired = summaryHelper.rememberSummaryOpen({ ...summary(), readToken: token }, () => actor)
      now += 300_001; assert.equal(summaryHelper.consumeSummaryOpen(expired.query.summaryOpen, expired.reference, token), null)
      const wrong = summaryHelper.rememberSummaryOpen({ ...summary(), readToken: token }, () => actor)
      assert.equal(summaryHelper.consumeSummaryOpen(wrong.query.summaryOpen, 'OTHER', token), null)
      const changed = summaryHelper.rememberSummaryOpen({ ...summary(), readToken: token }, () => actor)
      actor = false; assert.equal(summaryHelper.consumeSummaryOpen(changed.query.summaryOpen, changed.reference, token), null)
    } finally { Date.now = originalNow }
  })
  await check('actual initial consumer rejects a missing ticket while direct legacy reference remains anonymous-readable', () => {
    const guarded = initialContextRuntime({ params: { deckId: 'CODE-A' }, fullPath: '/decks/CODE-A?expectedReadToken=x',
      query: { expectedReadToken: token }, hash: '' }, () => () => true, summaryHelper.consumeSummaryOpen)()
    assert.ok('error' in guarded)
    const direct = initialContextRuntime({ params: { deckId: 'legacy-id' }, fullPath: '/decks/legacy-id', query: {}, hash: '' },
      () => () => true, summaryHelper.consumeSummaryOpen)()
    assert.equal(direct.context.reference, 'legacy-id'); assert.equal(direct.context.expectedReadToken, undefined)
    const repeated = initialContextRuntime({ params: { deckId: 'CODE-A' }, fullPath: '/decks/CODE-A',
      query: { expectedReadToken: [token, token], summaryOpen: ['one', 'two'] }, hash: '' },
      () => () => true, summaryHelper.consumeSummaryOpen)()
    assert.ok('error' in repeated)
  })
  await check('actual initial consumer accepts a genuine ticket and carries its actor guard', () => {
    let actor = true
    const open = summaryHelper.rememberSummaryOpen({ ...summary(), readToken: token }, () => actor)
    const route = { params: { deckId: open.reference }, fullPath: `/decks/${open.reference}?expectedReadToken=${token}&summaryOpen=${open.query.summaryOpen}`,
      query: open.query, hash: '' }
    const result = initialContextRuntime(route, () => () => true, summaryHelper.consumeSummaryOpen)()
    assert.equal(result.context.expectedReadToken, token); assert.equal(result.context.actorCurrent(), true)
    actor = false; assert.equal(result.context.actorCurrent(), false)
  })
  await check('actual pinned auxiliary guard rejects route, actor, document, load and body changes', () => {
    const entry = vue.ref(helper.toPublicDeckCurrentEntry(current(false)))
    const route = vue.reactive({ name: 'public-deck-detail', fullPath: '/decks/CODE-A' })
    let runtime = pinnedContextRuntime(entry, route), context = runtime.capture(); assert.equal(context.current(), true)
    route.fullPath = '/decks/OTHER'; assert.equal(context.current(), false)
    route.fullPath = '/decks/CODE-A'; runtime = pinnedContextRuntime(entry, route); context = runtime.capture(); runtime.actor(); assert.equal(context.current(), false)
    runtime = pinnedContextRuntime(entry, route); context = runtime.capture(); runtime.document(); assert.equal(context.current(), false)
    runtime = pinnedContextRuntime(entry, route); context = runtime.capture(); runtime.load(); assert.equal(context.current(), false)
    runtime = pinnedContextRuntime(entry, route); context = runtime.capture(); entry.value = { ...entry.value, deck: { ...entry.value.deck } }; assert.equal(context.current(), false)
  })
  await check('actual Vue metadata consumer keeps only the newest reordered page', async () => {
    const row = auxiliaryRuntime(helper), pageTwo = row.loadVersionPage(2), pageThree = row.loadVersionPage(3)
    assert.deepEqual(row.versionRows.map(item => [item.page, item.readToken]), [[2, token], [3, token]])
    row.versionRows[1].resolve(versionPage(3, 30, 135, [metadata(75)])); await pageThree
    assert.equal(row.versionsPage.value.page, 3); assert.equal(row.versionsState.value, 'available')
    row.versionRows[0].resolve(versionPage(2, 30, 135, [metadata(105)])); await pageTwo
    assert.equal(row.versionsPage.value.page, 3)
  })
  await check('actual Vue statistics consumer keeps only the newest as-of page', async () => {
    const row = auxiliaryRuntime(helper), first = row.loadStatisticsPage(1), second = row.loadStatisticsPage(2)
    row.statisticRows[1].resolve(statistics(2, 30, 60, [statisticGroup(2)])); await second
    assert.equal(row.statisticsPage.value.page, 2); assert.equal(row.statisticsState.value, 'available')
    row.statisticRows[0].resolve(statistics(1, 30, 60, [statisticGroup(3)])); await first
    assert.equal(row.statisticsPage.value.page, 2)
  })
  await check('actual Vue selected-history consumer retains exactly one newest body', async () => {
    const row = auxiliaryRuntime(helper), first = row.loadSelectedVersion(7), second = row.loadSelectedVersion(8)
    row.selectedRows[1].resolve(versionRead(8)); await second
    assert.equal(row.selectedVersion.value.metadata.version, 8)
    row.selectedRows[0].resolve(versionRead(7)); await first
    assert.equal(row.selectedVersion.value.metadata.version, 8)
  })
  await check('actual Vue auxiliary 409 exposes refresh and never retries without its pin', async () => {
    const row = auxiliaryRuntime(helper), pending = row.loadStatisticsPage(1)
    row.statisticRows[0].reject(Object.assign(new Error('conflict'), { status: 409, code: 'public_deck_read_conflict' }))
    await pending
    assert.equal(row.statisticRows.length, 1); assert.equal(row.statisticRows[0].readToken, token)
    assert.equal(row.statisticsState.value, 'unavailable'); assert.equal(row.refreshRequired.value, true)
  })
  await check('actual Vue auxiliary late reply cannot cross an actor epoch', async () => {
    const row = auxiliaryRuntime(helper), pending = row.loadVersionPage(1)
    row.actor(); row.actor()
    row.versionRows[0].resolve(versionPage(1, 30, 135, [metadata(135)])); await pending
    assert.equal(row.versionsPage.value, null); assert.equal(row.versionsState.value, 'loading')
  })

  await check('actual typed API serializes current, metadata, one body and statistics with the same pin', () => {
    const row = apiRuntime()
    row.publicDeckReadApi.current('A/B', token)
    row.publicDeckReadApi.versions('A/B', 2, 30, token)
    row.publicDeckReadApi.version('A/B', 7, token)
    row.publicDeckReadApi.statistics('A/B', 4, 100, token)
    assert.deepEqual(row.requests, [
      `/api/public-decks/A%2FB/current?expectedReadToken=${token}`,
      `/api/public-decks/A%2FB/versions?page=2&pageSize=30&expectedReadToken=${token}`,
      `/api/public-decks/A%2FB/versions/7?expectedReadToken=${token}`,
      `/api/public-decks/A%2FB/statistics?page=4&pageSize=100&expectedReadToken=${token}`,
    ])
  })
  await check('typed API permits explicit unpinned current and rejects invalid client parameters', () => {
    const row = apiRuntime(); row.publicDeckReadApi.current('legacy-id')
    assert.equal(row.requests[0], '/api/public-decks/legacy-id/current')
    assert.throws(() => row.publicDeckReadApi.current('legacy-id', 'A'.repeat(64)))
    assert.throws(() => row.publicDeckReadApi.versions('legacy-id', 1, 101, token))
    assert.throws(() => row.publicDeckReadApi.version('legacy-id', 0, token))
  })
  await check('typed API sends one exact thin content PUT with the required lower-case pin', () => {
    const row = apiRuntime(), guide = current(true).guide, matchups = current(true).matchups
    row.publicDeckReadApi.updateContent('A/B', token, guide, matchups)
    assert.equal(row.requests.length, 1)
    assert.equal(row.requests[0].url, `/api/public-decks/A%2FB/content/current?expectedReadToken=${token}`)
    assert.equal(row.requests[0].options.method, 'PUT')
    assert.deepEqual(JSON.parse(row.requests[0].options.body), { guide, matchups })
    assert.throws(() => row.publicDeckReadApi.updateContent('A/B', token.toUpperCase(), guide, matchups))
    assert.equal(row.requests.length, 1)
  })

  await check('actual content editor loads owner current and saves one canonical thin head', async () => {
    const row = contentEditorRuntime(helper), loading = row.loadContent()
    assert.deepEqual(row.currentRows.map(item => [item.reference, item.expectedReadToken]), [[generation.id, undefined]])
    row.currentRows[0].resolve(ownerCurrent()); await loading
    assert.equal(row.readGeneration.value.readToken, token); assert.equal(row.revision.value, 2)
    row.guide.value.buildIdea = '  新\r\n思路  '
    const saving = row.saveContent(); await flush()
    assert.equal(row.updateRows.length, 1); assert.equal(row.updateRows[0].expectedReadToken, token)
    assert.equal(row.updateRows[0].guide.buildIdea, '  新\r\n思路  ')
    const guide = { ...current(true).guide, buildIdea: '新\n思路' }
    row.updateRows[0].resolve(contentCurrent(guide, current(true).matchups)); await saving
    assert.equal(row.guide.value.buildIdea, '新\n思路'); assert.equal(row.readGeneration.value.readToken, 'b'.repeat(64))
    assert.deepEqual(row.events, [['saved', '指南和对局建议已保存，可返回公开详情查看']])
  })
  await check('actual content editor adopts the pin but preserves input made during save', async () => {
    const row = contentEditorRuntime(helper), loading = row.loadContent()
    row.currentRows[0].resolve(ownerCurrent()); await loading
    row.guide.value.buildIdea = '已提交草稿'
    const saving = row.saveContent(); await flush()
    row.guide.value.buildIdea = '等待期间的新草稿'
    row.updateRows[0].resolve(contentCurrent({ ...current(true).guide, buildIdea: '已提交草稿' }, current(true).matchups))
    await saving
    assert.equal(row.guide.value.buildIdea, '等待期间的新草稿'); assert.equal(row.revision.value, 3)
    assert.deepEqual(row.events, [['saved', '提交时的公开内容已保存；当前编辑仍有未保存修改']])
    const stale = row.saveContent(); await flush(); row.actor(); row.actor()
    row.updateRows[1].resolve(contentCurrent({ ...current(true).guide, buildIdea: '等待期间的新草稿' }, current(true).matchups, 'c'.repeat(64), 4))
    await stale
    assert.equal(row.revision.value, 3); assert.equal(row.events.length, 1)
  })
  await check('actual content editor exposes 409, refreshes only its pin, and writes again only after a click', async () => {
    const row = contentEditorRuntime(helper), loading = row.loadContent()
    row.currentRows[0].resolve(ownerCurrent()); await loading
    row.guide.value.buildIdea = '保留的草稿'
    const saving = row.saveContent(); await flush()
    row.updateRows[0].reject(Object.assign(new Error('conflict'), { status: 409, code: 'public_deck_read_conflict' })); await saving
    assert.equal(row.refreshRequired.value, true); assert.equal(row.updateRows.length, 1)
    const refreshing = row.refreshContentGeneration(); await flush()
    row.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, 'c'.repeat(64), {
      guide: { ...current(true).guide, buildIdea: '服务器内容' }, contentRevision: 4,
      contentUpdatedAt: '2026-10-07T02:00:00Z' }))
    await refreshing
    assert.equal(row.guide.value.buildIdea, '保留的草稿'); assert.equal(row.refreshRequired.value, false)
    assert.match(row.error.value, /已获取最新内容/); assert.equal(row.updateRows.length, 1)
    const explicit = row.saveContent(); await flush()
    assert.equal(row.updateRows.length, 2); assert.equal(row.updateRows[1].expectedReadToken, 'c'.repeat(64))
    row.updateRows[1].resolve(contentCurrent({ ...current(true).guide, buildIdea: '保留的草稿' }, current(true).matchups, 'd'.repeat(64), 5))
    await explicit
  })
  await check('actual content editor reloads server content after an initial failure unless a local draft exists', async () => {
    const empty = contentEditorRuntime(helper), failed = empty.loadContent()
    empty.currentRows[0].reject(Object.assign(new Error('storage'), { status: 503, code: 'storage_unavailable' })); await failed
    const retry = empty.refreshContentGeneration(); await flush()
    empty.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: { ...current(true).guide, buildIdea: '服务器恢复内容' } })); await retry
    assert.equal(empty.guide.value.buildIdea, '服务器恢复内容'); assert.equal(empty.error.value, '已获取最新内容。')
    const edited = contentEditorRuntime(helper), editedFailed = edited.loadContent()
    edited.currentRows[0].reject(Object.assign(new Error('storage'), { status: 503, code: 'storage_unavailable' })); await editedFailed
    edited.guide.value.buildIdea = '本地恢复草稿'
    const editedRetry = edited.refreshContentGeneration(); await flush()
    edited.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: { ...current(true).guide, buildIdea: '服务器恢复内容' } })); await editedRetry
    assert.equal(edited.guide.value.buildIdea, '本地恢复草稿'); assert.match(edited.error.value, /草稿仍保留/)
  })
  await check('actual content editor rejects late document and unmounted replies', async () => {
    const row = contentEditorRuntime(helper), first = row.loadContent()
    row.setPublication('publication-B'); const second = row.loadContent()
    row.currentRows[1].resolve(ownerCurrent('publication-B', 'CODE-B', 'c'.repeat(64), { guide: { ...current(true).guide, buildIdea: 'B' } })); await second
    row.currentRows[0].resolve(ownerCurrent()); await first
    assert.equal(row.readGeneration.value.id, 'publication-B'); assert.equal(row.guide.value.buildIdea, 'B')
    row.guide.value.buildIdea = '卸载草稿'
    const saving = row.saveContent(); await flush(); row.dispose()
    row.updateRows[0].resolve({ ...contentCurrent({ ...current(true).guide, buildIdea: '卸载草稿' }), id: 'publication-B', publicCode: 'CODE-B' })
    await saving
    assert.equal(row.revision.value, 2); assert.equal(row.events.length, 0)
  })

  await check('actual library publish saves provenance, reads current, then sends one thin content write', async () => {
    const row = deckLibraryRuntime(helper, localDeck()), blank = { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
    row.publishGuide.value.buildIdea = '  发布\r\n内容  '
    const action = row.publishDeck(); await flush(); assert.ok(row.publishRows[0], row.notice.value)
    row.publishRows[0].resolve(publishedEntry()); await flush()
    assert.equal(row.saveCalls.length, 1); assert.equal(row.currentRows.length, 1)
    row.currentRows[0].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    assert.equal(row.updateRows.length, 1); assert.equal(row.updateRows[0].expectedReadToken, token)
    row.updateRows[0].resolve(contentCurrent({ ...blank, buildIdea: '发布\n内容' }, [], 'b'.repeat(64), 1)); await action
    assert.equal(row.publishRows.length, 1); assert.equal(row.showPublish.value, false); assert.equal(row.tab.value, 'plaza')
    assert.equal(row.pending(), null); assert.equal(row.summaryLoads(), 1); assert.match(row.notice.value, /同步发布/)
  })
  await check('actual library same-key double calls cannot invalidate the accepted publish, provenance, or content waits', async () => {
    const row = deckLibraryRuntime(helper, localDeck(), { deferSave: true })
    const blank = { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
    row.publishGuide.value.buildIdea = '拒重仍完成'
    const accepted = row.publishDeck(); await flush()
    await row.publishDeck(); assert.equal(row.publishRows.length, 1)
    row.publishRows[0].resolve(publishedEntry()); await flush()
    assert.equal(row.saveRows.length, 1)
    await row.publishDeck(); assert.equal(row.saveCalls.length, 1)
    row.saveRows[0].resolve(); await flush()
    assert.equal(row.currentRows.length, 1)
    row.currentRows[0].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    assert.equal(row.updateRows.length, 1)
    await row.publishDeck(); assert.equal(row.updateRows.length, 1)
    row.updateRows[0].resolve(contentCurrent({ ...blank, buildIdea: '拒重仍完成' }, [], 'b'.repeat(64), 1)); await accepted
    assert.equal(row.publishRows.length, 1); assert.equal(row.saveCalls.length, 1); assert.equal(row.currentRows.length, 1)
    assert.equal(row.updateRows.length, 1); assert.equal(row.pending(), null); assert.equal(row.showPublish.value, false)
  })
  await check('actual library preserves B1 server-confirmed provenance failure without retrying an old private revision', async () => {
    const saveError = Object.assign(new Error('服务器已确认本次操作；本机缓存未更新，请刷新后同步'), { serverConfirmed: true })
    const row = deckLibraryRuntime(helper, localDeck(), { saveError })
    row.publishGuide.value.buildIdea = '仍需保存的指南草稿'
    const first = row.publishDeck(); await flush(); row.publishRows[0].resolve(publishedEntry()); await first
    assert.equal(row.publishRows.length, 1); assert.equal(row.saveCalls.length, 1)
    assert.equal(row.currentRows.length, 0); assert.equal(row.updateRows.length, 0); assert.ok(row.pending())
    assert.equal(row.publishGuide.value.buildIdea, '仍需保存的指南草稿')
    assert.match(row.notice.value, /本机缓存未更新|本机牌库缓存尚未同步/)
    assert.match(row.notice.value, /草稿仍保留/); assert.doesNotMatch(row.notice.value, /再次点击|确认公开/)
    await row.publishDeck()
    assert.equal(row.publishRows.length, 1); assert.equal(row.saveCalls.length, 1)
    assert.equal(row.currentRows.length, 0); assert.match(row.notice.value, /请先刷新页面同步/)
  })
  await check('actual library 409 keeps a bound pending publication and an explicit retry does not republish', async () => {
    const row = deckLibraryRuntime(helper, localDeck()), blank = { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
    row.publishGuide.value.buildIdea = '需要重试'
    const first = row.publishDeck(); await flush(); assert.ok(row.publishRows[0], row.notice.value)
    row.publishRows[0].resolve(publishedEntry()); await flush()
    row.currentRows[0].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    row.updateRows[0].reject(Object.assign(new Error('conflict'), { status: 409, code: 'public_deck_read_conflict' })); await first
    assert.ok(row.pending()); assert.equal(row.showPublish.value, true); assert.match(row.notice.value, /再次点击/)
    const second = row.publishDeck(); await flush()
    assert.equal(row.publishRows.length, 1); assert.equal(row.currentRows.length, 2)
    row.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    assert.equal(row.updateRows.length, 2)
    row.updateRows[1].resolve(contentCurrent({ ...blank, buildIdea: '需要重试' }, [], 'b'.repeat(64), 1)); await second
    assert.equal(row.publishRows.length, 1); assert.equal(row.pending(), null); assert.equal(row.showPublish.value, false)
  })
  await check('actual library reconciles a network-uncertain content save without a second publish or write', async () => {
    const row = deckLibraryRuntime(helper, localDeck()), blank = { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
    row.publishGuide.value.buildIdea = '已实际保存'
    const first = row.publishDeck(); await flush(); assert.ok(row.publishRows[0], row.notice.value)
    row.publishRows[0].resolve(publishedEntry()); await flush()
    row.currentRows[0].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    row.updateRows[0].reject(Object.assign(new Error('storage'), { status: 503, code: 'storage_unavailable' })); await first
    const second = row.publishDeck(); await flush()
    row.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, 'b'.repeat(64),
      { guide: { ...blank, buildIdea: '已实际保存' }, matchups: [], contentRevision: 1,
        contentUpdatedAt: '2026-10-07T03:00:00Z' })); await second
    assert.equal(row.publishRows.length, 1); assert.equal(row.updateRows.length, 1)
    assert.equal(row.pending(), null); assert.equal(row.showPublish.value, false)
  })
  await check('actual library preserves edits made during save and reuses the publication on the next click', async () => {
    const row = deckLibraryRuntime(helper, localDeck()), blank = { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
    row.publishGuide.value.buildIdea = '提交快照'
    const first = row.publishDeck(); await flush(); assert.ok(row.publishRows[0], row.notice.value)
    row.publishRows[0].resolve(publishedEntry()); await flush()
    row.currentRows[0].resolve(ownerCurrent(generation.id, generation.publicCode, token,
      { guide: blank, matchups: [], contentRevision: 0, contentUpdatedAt: null })); await flush()
    row.publishGuide.value.buildIdea = '等待期间的新编辑'
    row.updateRows[0].resolve(contentCurrent({ ...blank, buildIdea: '提交快照' }, [], 'b'.repeat(64), 1)); await first
    assert.equal(row.showPublish.value, true); assert.ok(row.pending()); assert.match(row.notice.value, /未保存修改/)
    const second = row.publishDeck(); await flush(); assert.equal(row.publishRows.length, 1)
    row.currentRows[1].resolve(ownerCurrent(generation.id, generation.publicCode, 'b'.repeat(64),
      { guide: { ...blank, buildIdea: '提交快照' }, matchups: [], contentRevision: 1,
        contentUpdatedAt: '2026-10-07T03:00:00Z' })); await flush()
    row.updateRows[1].resolve(contentCurrent({ ...blank, buildIdea: '等待期间的新编辑' }, [], 'c'.repeat(64), 2)); await second
    assert.equal(row.publishRows.length, 1); assert.equal(row.showPublish.value, false); assert.equal(row.pending(), null)
  })
  await check('actual library ignores publish replies after account ABA or unmount', async () => {
    const account = deckLibraryRuntime(helper, localDeck()), first = account.publishDeck()
    await flush(); assert.ok(account.publishRows[0], account.notice.value)
    account.changeAccount(); account.publishRows[0].resolve(publishedEntry()); await first
    assert.equal(account.saveCalls.length, 0); assert.equal(account.currentRows.length, 0); assert.equal(account.notice.value, '')
    const unmounted = deckLibraryRuntime(helper, localDeck()), second = unmounted.publishDeck()
    await flush(); assert.ok(unmounted.publishRows[0], unmounted.notice.value)
    unmounted.dispose(); unmounted.publishRows[0].resolve(publishedEntry()); await second
    assert.equal(unmounted.saveCalls.length, 0); assert.equal(unmounted.currentRows.length, 0); assert.equal(unmounted.notice.value, '')
  })

  await check('actual component no longer calls legacy full detail and starts bounded sibling reads', () => {
    const source = read('src/l12/site/PublicDeckDetailPage.vue'), load = functionSource(source, 'loadDetail')
    assert.doesNotMatch(load, /publicDeckApi\.get/); assert.match(load, /publicDeckReadApi\.current/)
    assert.match(load, /loadVersionPage\(1\)/); assert.match(load, /loadStatisticsPage\(1\)/)
  })
  await check('actual component exposes independent loaded, unavailable and refresh states without fake DTO text', () => {
    const source = read('src/l12/site/PublicDeckDetailPage.vue')
    assert.match(source, /versionsState/); assert.match(source, /statisticsState/); assert.match(source, /refreshRequired/)
    assert.doesNotMatch(source, /details\.versions|details\.matchStatistics|构筑正文未变化|publicDeckApi\.get\(/)
  })
  await check('canonical share path excludes ticket and expected pin', () => {
    const source = functionSource(read('src/l12/site/PublicDeckDetailPage.vue'), 'publicDeckUrl')
    assert.doesNotMatch(source, /summaryOpen|expectedReadToken|route\.query/); assert.match(source, /publicDeckReference/)
  })
  await check('legacy eight-method API initializer remains at its reviewed normalized source digest', () => {
    const initializer = variableInitializer(read('src/l12/platform.ts'), 'publicDeckApi').replace(/\r\n/g, '\n')
    assert.equal(crypto.createHash('sha256').update(initializer).digest('hex'),
      'e779f4eeb21694006de74ad038c985a28377d19f4d1ae5d17be93cfc84a41e9f')
  })
  await check('actual content editor keeps fields and picker behavior while exposing guarded refresh and save states', () => {
    const source = read('src/l12/site/PublicDeckContentEditor.vue'), save = functionSource(source, 'saveContent')
    assert.equal((source.match(/maxlength="1200"/g) ?? []).length, 5)
    assert.equal((source.match(/maxlength="800"/g) ?? []).length, 3)
    assert.match(source, /SingleCardPicker/); assert.match(source, /dialogChange/); assert.match(source, /handlePickerKey/)
    assert.match(source, /重新获取最新内容/); assert.match(source, /!readGeneration \|\| refreshRequired/)
    assert.match(save, /publicDeckReadApi\.updateContent/); assert.doesNotMatch(save, /publicDeckApi\.updateContent/)
  })
  await check('actual library publish uses current reconciliation and never calls the legacy content writer', () => {
    const publish = functionSource(read('src/l12/site/DeckLibraryPage.vue'), 'publishDeck')
    assert.match(publish, /publicDeckApi\.publish/); assert.match(publish, /publicDeckReadApi\.current/)
    assert.match(publish, /publicDeckReadApi\.updateContent/); assert.doesNotMatch(publish, /publicDeckApi\.updateContent/)
    assert.match(publish, /matchingPendingPublish/); assert.match(publish, /draftUnchanged/)
  })
  await check('all three touched consumer templates parse and compile with the real Vue compiler', () => {
    const compiler = requireDependency('@vue/compiler-sfc')
    for (const filename of ['PublicDeckDetailPage.vue', 'PublicDeckContentEditor.vue', 'DeckLibraryPage.vue']) {
      const source = read(`src/l12/site/${filename}`), result = compiler.parse(source, { filename })
      assert.deepEqual(result.errors, [])
      const compiled = compiler.compileTemplate({ source: result.descriptor.template.content, filename, id: 'cu3' })
      assert.deepEqual(compiled.errors, [])
    }
  })

  console.log(`C-U3 focused Node result: ${passed}/${passed}, failed=0, skipped=0`)
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url)
  await main()
