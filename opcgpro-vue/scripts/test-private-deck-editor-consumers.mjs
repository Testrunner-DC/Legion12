import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'package.json'))
const ts = require('typescript')
const { ref, computed } = require('vue')
const { compileScript, compileTemplate, compileStyle, parse } = require('@vue/compiler-sfc')
const editorPath = 'src/l12/L12DeckEditor.vue'
const read = name => fs.readFileSync(path.join(root, name), 'utf8')
function tree(name) {
  const raw = read(name)
  const source = raw.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? raw
  const file = ts.createSourceFile(name, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0)
  return file
}
function functionText(name, names) {
  const file = tree(name)
  return names.map(key => {
    const node = file.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === key)
    assert.ok(node, `${name}: missing ${key}`)
    return node.getText(file).replace(/^export\s+/, '')
  }).join('\n')
}
function execute(source, dependencies, result) {
  const js = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(dependencies), `${js}\nreturn ${result}`)(...Object.values(dependencies))
}
function mounted(dependencies) {
  const file = tree(editorPath)
  const node = file.statements.find(item => ts.isExpressionStatement(item) && ts.isCallExpression(item.expression)
    && item.expression.expression.getText(file) === 'onMounted' && item.expression.arguments[0]?.modifiers?.some(m => m.kind === ts.SyntaxKind.AsyncKeyword))
  assert.ok(node, 'actual async editor mount must exist')
  return execute(`const mounted = ${node.expression.arguments[0].getText(file)}`, dependencies, 'mounted')
}
let passed = 0
async function check(name, action) { await action(); passed++; console.log(`PASS ${passed}: ${name}`) }
const platformState = { account: { id: 'synthetic-a' }, token: 'synthetic-token-a' }
const requests = []
const context = { accountId: 'synthetic-a', token: 'synthetic-token-a', epoch: 0, storageKey: 'synthetic-cache' }
const actualDecks = execute(functionText('src/l12/decks.ts', ['syncSavedDecksFromAccount', 'ensureOfficialPrebuiltDecks']), {
  platformState, captureDeckStorageContext: () => context, currentDeckSnapshot: () => ({ decks: {} }),
  markDeckCacheActivity: () => 'synthetic-activity', deckCacheActivity: () => 'synthetic-activity',
  isCurrentDeckStorageContext: () => true, visibleDecksAfterAsyncWork: () => ({}), loadSavedDecks: () => ({}),
  withGuestDeckMutation: async (_context, callback) => callback(), commitSavedDecks: (_context, _previous, decks) => decks,
  normalizeSavedDeck: value => value, DeckCacheStorageError: class extends Error {}, operationError: error => error,
  platformRequest: async requestPath => { requests.push(requestPath); return [] },
}, '({ syncSavedDecksFromAccount, ensureOfficialPrebuiltDecks })')
const dependencies = {
  platformState, editorAccountEpoch: 0, editorDocumentEpoch: 0, editorContentRevision: ref(0),
  catalog: ref([]), savedDecks: ref({}), loading: ref(true), notice: ref(''),
  selected: ref(null), mainCards: ref([]), operationsRestrictions: ref([]), operationsPolicyLoaded: ref(false), publicationCode: ref(''), publicationId: ref(''),
  route: { fullPath: '/deck-editor', query: {} }, router: { currentRoute: ref({ query: {} }) },
  loadDeckCatalog: async () => [], ensureOfficialPrebuiltDecks: actualDecks.ensureOfficialPrebuiltDecks,
  refreshOwnedAlternateArts: async () => undefined, getEffectiveOperationsPolicy: async () => ({ cardRestrictions: [] }),
  loadSavedDecks: () => ({}), loadDeck: () => undefined, refreshLocalDraft: () => undefined,
  resolvePublishedDeck: async () => undefined, deckErrorBelongsToCurrentAccount: () => true,
  authenticatedEditor: ref(true), editorContext: () => ({ accountId: platformState.account.id, token: platformState.token, route: '/deck-editor' }),
  isCurrentEditorContext: () => true, editorAlive: true,
  loadSavedDirectory: async () => undefined, loadRequestedSavedDeck: async () => undefined,
}
await check('actual authenticated editor mount never expands the full private collection', async () => {
  await mounted(dependencies)()
  assert.equal(requests.filter(requestPath => requestPath === '/api/decks').length, 0,
    'authenticated editor still follows the actual ensureOfficial → syncSavedDecks → GET /api/decks chain')
})

await check('real editor script, template and scoped styles compile', () => {
  const parsed = parse(read(editorPath), { filename: editorPath })
  assert.deepEqual(parsed.errors, [])
  const script = compileScript(parsed.descriptor, { id: 'private-editor-focused' })
  const template = compileTemplate({ source: parsed.descriptor.template.content, filename: editorPath,
    id: 'private-editor-focused', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  for (const style of parsed.descriptor.styles)
    assert.deepEqual(compileStyle({ source: style.content, filename: editorPath, id: 'private-editor-focused', scoped: style.scoped }).errors, [])
})

function declarationText(name) {
  const file = tree(editorPath)
  const statement = file.statements.find(item => ts.isVariableStatement(item)
    && item.declarationList.declarations.some(declaration => declaration.name.getText(file) === name))
  assert.ok(statement, `actual declaration ${name} is required`)
  return statement.getText(file)
}
function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
function summary(id = 'deck-a', revision = 3) {
  return { id, revision, name: `牌库-${id}`, masterId: 'MASTER', updatedAt: '2026-10-07T12:00:00Z',
    publicationId: null, publicationVersion: null, counts: { main: 40, uncountedMain: 1, morale: 8, special: 0, bench: 2 }, legal: true, legalityReason: null }
}
function body(id = 'deck-a', revision = 3) {
  return { id, revision, name: `正文-${id}`, masterId: 'MASTER', updatedAt: '2026-10-07T12:00:00Z',
    publicationId: null, publicationVersion: null, cardIds: ['CARD', 'CARD'], moraleIds: [], specialIds: [], benchIds: [],
    alternateArtSelections: {}, alternateArtCopies: {} }
}
function page(items = [summary()], number = 1, total = items.length) {
  return { items, total, page: number, pageSize: 30, generation: 4, permissionVersion: 2,
    catalogVersion: 'A'.repeat(64), policyVersion: 9, facets: { masters: total ? [{ masterId: 'MASTER', count: total }] : [], legal: total, illegal: 0 } }
}
function editorRuntime(options = {}) {
  const state = { calls: [], loaded: [], deleted: [], saved: [], published: [], images: [], confirmed: true,
    route: { fullPath: '/deck-editor', query: {} }, platform: { account: { id: 'synthetic-a' }, token: 'synthetic-a' },
    cache: {}, requestBody: async target => body(target.id, target.revision), requestPage: async query => page([summary()], query.page),
    requestDelete: async () => undefined, requestSave: async deck => ({ ...deck, id: deck.id ?? 'saved-id', revision: (deck.revision ?? 0) + 1 }),
    requestPublish: async deck => ({ id: 'public-id', publicCode: 'PUBLICCODE12', deck: { ...deck, publicationId: 'public-id', publicationVersion: 1 } }), ...options }
  const names = ['savedDecks', 'savedDirectory', 'savedDirectorySearch', 'savedDirectoryKeyword', 'savedDirectoryLoading',
    'savedDirectoryError', 'deckReadBusy', 'directoryReadSequence', 'deckReadSequence', 'editorAlive', 'authenticatedEditor',
    'savedDirectoryDecks', 'savedDirectoryTotal', 'savedDirectoryPages', 'notice', 'deckName', 'masterId', 'counts', 'benchCounts',
    'specialIds', 'alternateArtSelections', 'alternateArtCopies', 'activeDeckName', 'activeDeckId', 'activeDeckRevision',
    'pendingDeleteName', 'pendingDeleteDeck', 'deletingDeck', 'deckMutationBusy', 'publicationId', 'publicationCode', 'publicationVersion',
    'editorContentRevision', 'persistedContentRevision', 'restoredLocalDraft', 'openingHandIds', 'selected', 'mobileSavedDecksOpen',
    'editorAccountEpoch', 'editorDocumentEpoch', 'editorContext', 'isCurrentEditorContext', 'hasUnsavedChanges',
    'generatingDeckImage', 'deckImageBlob', 'deckImageUrl']
  const functions = ['mutationError', 'savedDeckCountLabel', 'loadSavedDirectory', 'searchSavedDirectory', 'loadRequestedSavedDeck',
    'refreshSavedDirectoryAfterMutation', 'confirmDiscardChanges', 'requestLoadDeck', 'loadDeck', 'newDeck', 'chooseMobileSavedDeck',
    'requestDelete', 'closePendingDelete', 'confirmDelete', 'canSaveCurrentDeck', 'onSave', 'onSaveAs',
    'publishCurrentDeck', 'generateDeckImage']
  const source = names.map(declarationText).join('\n') + '\n' + functionText(editorPath, functions)
  const runtime = execute(source, {
    ref, computed, platformState: state.platform, route: state.route, mainCards: ref([]), byId: ref(new Map()),
    MAIN_DECK_TYPES: new Set(['legion', 'tactic', 'artifact']), validation: options.validation ?? ref(''),
    catalog: options.catalog ?? ref([]), ownedAlternateArts: ref([]),
    window: { confirm: () => state.confirmed }, deckErrorBelongsToCurrentAccount: () => true,
    loadSavedDecks: () => state.cache, resolvePublishedDeck: async () => undefined,
    loadPrivateDeckSummaryPage: async query => { state.calls.push({ kind: 'directory', query }); return state.requestPage(query) },
    loadPrivateDeckBody: async (target, current) => { state.calls.push({ kind: 'body', target, current }); return state.requestBody(target, current) },
    deleteDeck: async deck => { state.deleted.push(deck); return state.requestDelete(deck) },
    saveDeck: async deck => { state.saved.push(deck); return state.requestSave(deck) },
    currentDeck: () => options.workingDeck ?? body('current', 2), clearCurrentDraftAfterServerSave: () => undefined,
    uniqueDeckCopyName: async (name, current) => { assert.equal(current(), true); return name },
    publicDeckApi: { publish: async deck => { state.published.push(deck); return state.requestPublish(deck) } },
    publicDeckRouteReference: result => result.publicCode,
    closeDeckImage: () => undefined, verifiedPublicDeckUrl: async () => undefined,
    createDeckImageBlob: async deck => { state.images.push(deck); return { image: true } },
    URL: { createObjectURL: () => 'blob:synthetic-image' },
    router: { replace: async () => undefined },
  }, `({${names.filter(name => !['editorAlive','directoryReadSequence','deckReadSequence','editorAccountEpoch','editorDocumentEpoch'].includes(name)).join(',')},${functions.join(',')},
    bumpAccount(){editorAccountEpoch++}, bumpDocument(){editorDocumentEpoch++}, dispose(){editorAlive=false;directoryReadSequence++;deckReadSequence++}})`)
  return { ...runtime, state }
}

await check('one directory request keeps server paging, search and metadata intact', async () => {
  const r = editorRuntime(); r.savedDirectoryKeyword.value = '中文 &'; r.state.requestPage = async () => page([summary()], 7, 300)
  await r.loadSavedDirectory(7)
  assert.deepEqual(r.state.calls.map(item => item.query), [{ page: 7, pageSize: 30, keyword: '中文 &' }])
  assert.equal(r.savedDirectoryTotal.value, 300); assert.equal(r.savedDirectoryDecks.value.length, 1)
  assert.equal('cardIds' in r.savedDirectoryDecks.value[0], false); assert.equal(r.savedDirectoryPages.value, 10)
  assert.match(r.savedDeckCountLabel(summary()), /40（1 不计构筑）/)
})
await check('search reloads only page one without traversing or opening bodies', async () => {
  const r = editorRuntime(); r.savedDirectorySearch.value = '  中文  '; r.searchSavedDirectory()
  await Promise.resolve(); assert.equal(r.state.calls.length, 1)
  assert.equal(r.state.calls[0].query.keyword, '中文'); assert.equal(r.state.calls[0].query.page, 1)
})
await check('unavailable directory stays distinct from an available empty result', async () => {
  const r = editorRuntime(); r.savedDirectory.value = page([summary()]); r.state.requestPage = async () => { throw new Error('不可读取') }
  await r.loadSavedDirectory(); assert.equal(r.savedDirectoryDecks.value.length, 1); assert.equal(r.savedDirectoryError.value, '不可读取')
  r.state.requestPage = async () => page([]); await r.loadSavedDirectory()
  assert.equal(r.savedDirectoryError.value, ''); assert.equal(r.savedDirectoryDecks.value.length, 0)
})
for (const [name, invalidate] of [
  ['account A-B-A', r => { r.state.platform.account = { id: 'b' }; r.bumpAccount(); r.state.platform.account = { id: 'synthetic-a' }; r.bumpAccount() }],
  ['same account token', r => { r.state.platform.token = 'new-token'; r.bumpAccount() }],
  ['unmount', r => r.dispose()],
]) await check(`directory ignores late success/failure after ${name}`, async () => {
  for (const rejects of [false, true]) {
    const wait = deferred(), r = editorRuntime({ requestPage: () => wait.promise })
    const pending = r.loadSavedDirectory(); invalidate(r)
    if (rejects) wait.reject(new Error('迟到失败')); else wait.resolve(page([summary()]))
    await pending; assert.equal(r.savedDirectory.value, null); assert.equal(r.savedDirectoryError.value, '')
  }
})
await check('a newer directory request wins over a late older page', async () => {
  const wait = deferred(), r = editorRuntime({ requestPage: query => query.page === 1 ? wait.promise : Promise.resolve(page([summary('page2')], 2)) })
  const pending = r.loadSavedDirectory(1); await r.loadSavedDirectory(2); wait.resolve(page([summary()])); await pending
  assert.equal(r.savedDirectory.value.page, 2)
})
await check('selected summary opens exactly its stable id/revision and retains real cards', async () => {
  const r = editorRuntime(); assert.equal(await r.requestLoadDeck(summary()), true)
  assert.deepEqual(r.state.calls[0].target, { id: 'deck-a', revision: 3 })
  assert.deepEqual(r.counts.value, { CARD: 2 }); assert.equal(r.activeDeckRevision.value, 3)
  assert.equal(r.deckReadBusy.value, false)
})
await check('guest selection retains its actual local body without private API calls', async () => {
  const r = editorRuntime(); r.state.platform.account = null; r.state.platform.token = ''
  assert.equal(await r.requestLoadDeck(body()), true); assert.equal(r.state.calls.length, 0)
})
await check('declining unsaved discard performs no read or editor replacement', async () => {
  const r = editorRuntime(); r.editorContentRevision.value++; r.state.confirmed = false
  assert.equal(await r.requestLoadDeck(summary()), false); assert.equal(r.state.calls.length, 0)
})
for (const [name, invalidate] of [
  ['content edit', r => { r.editorContentRevision.value++; r.deckName.value = '用户的新修改' }],
  ['new document', r => r.newDeck()],
  ['route change', r => { r.state.route.fullPath = '/deck-editor?deckId=other' }],
  ['account A-B-A', r => { r.bumpAccount(); r.bumpAccount() }],
  ['token A-B-A', r => { r.state.platform.token = 'b'; r.bumpAccount(); r.state.platform.token = 'synthetic-a'; r.bumpAccount() }],
  ['unmount', r => r.dispose()],
]) await check(`late body cannot replace editor after ${name}`, async () => {
  const wait = deferred(), r = editorRuntime({ requestBody: () => wait.promise })
  const pending = r.requestLoadDeck(summary()); invalidate(r); assert.equal(r.state.calls[0].current(), false)
  wait.resolve(body()); assert.equal(await pending, false); assert.equal(r.activeDeckId.value, null)
})
await check('duplicate load does not read twice', async () => {
  const wait = deferred(), r = editorRuntime({ requestBody: () => wait.promise })
  const pending = r.requestLoadDeck(summary()); assert.equal(await r.requestLoadDeck(summary('b')), false)
  wait.resolve(body()); await pending; assert.equal(r.state.calls.length, 1)
})
await check('closing mobile picker cancels its pending body without closing a new picker', async () => {
  const wait = deferred(), r = editorRuntime({ requestBody: () => wait.promise }); r.mobileSavedDecksOpen.value = true
  const pending = r.chooseMobileSavedDeck(summary()); r.mobileSavedDecksOpen.value = false
  assert.equal(r.state.calls[0].current(), false); wait.resolve(body()); await pending
  assert.equal(r.activeDeckId.value, null); assert.equal(r.mobileSavedDecksOpen.value, false)
})
for (const status of [401, 404, 409]) await check(`body ${status} never retries, deletes, or replaces a draft`, async () => {
  const r = editorRuntime({ requestBody: async () => { throw Object.assign(new Error(`HTTP ${status}`), { status }) } })
  assert.equal(await r.requestLoadDeck(summary()), false); assert.equal(r.state.calls.length, 1)
  assert.equal(r.state.deleted.length, 0); assert.equal(r.activeDeckId.value, null); assert.match(r.notice.value, new RegExp(String(status)))
})
await check('page-outside route id uses only a proven cached stable revision', async () => {
  const r = editorRuntime(); r.state.route.query = { deckId: 'outside' }; r.savedDecks.value = { cached: body('outside', 17) }
  r.savedDirectory.value = page([summary()]); await r.loadRequestedSavedDeck()
  assert.deepEqual(r.state.calls[0].target, { id: 'outside', revision: 17 })
})
await check('unknown route id never guesses a revision or scans all pages', async () => {
  const r = editorRuntime(); r.state.route.query = { deckId: 'outside' }; await r.loadRequestedSavedDeck()
  assert.equal(r.state.calls.length, 0); assert.match(r.notice.value, /搜索并选择/)
})
await check('stable route id takes priority over an unrelated matching name', async () => {
  const r = editorRuntime(); r.state.route.query = { deckId: 'outside', deck: 'same' }; r.savedDecks.value = { same: body('wrong') }
  await r.loadRequestedSavedDeck(); assert.equal(r.state.calls.length, 0)
})
await check('confirmed save is not reclassified as failure when list refresh fails', async () => {
  const r = editorRuntime({ requestPage: async () => { throw new Error('列表暂不可读') } })
  await r.onSave(); await new Promise(resolve => setImmediate(resolve)); assert.equal(r.state.saved.length, 1)
  assert.equal(r.activeDeckId.value, 'current'); assert.match(r.notice.value, /已保存/)
  assert.equal(r.savedDirectoryError.value, '列表暂不可读')
})
await check('deleting a summary first reads that exact instance, once', async () => {
  const r = editorRuntime(); r.requestDelete(summary()); await r.confirmDelete()
  assert.deepEqual(r.state.calls[0].target, { id: 'deck-a', revision: 3 })
  assert.equal(r.state.deleted.length, 1); assert.deepEqual(r.state.deleted[0].cardIds, ['CARD', 'CARD'])
})
await check('cancelled deletion during body read never sends DELETE', async () => {
  const wait = deferred(), r = editorRuntime({ requestBody: () => wait.promise })
  r.requestDelete(summary()); const pending = r.confirmDelete(); r.closePendingDelete()
  wait.resolve(body()); await pending; assert.equal(r.state.deleted.length, 0)
})
await check('editing while DELETE is in flight preserves the new working document', async () => {
  const wait = deferred(), r = editorRuntime({ requestDelete: () => wait.promise })
  r.activeDeckId.value = 'deck-a'; r.activeDeckRevision.value = 3; r.requestDelete(summary())
  const pending = r.confirmDelete(); await new Promise(resolve => setImmediate(resolve))
  assert.equal(r.state.deleted.length, 1, 'the real DELETE consumer must already be in flight')
  r.editorContentRevision.value++; r.deckName.value = '保留我的修改'
  wait.resolve(); await pending; assert.equal(r.deckName.value, '保留我的修改'); assert.match(r.notice.value, /当前修改仍保留/)
})

// Use the shipped catalog, prebuilt and validator, then execute the actual
// editor computations and mutation handlers. A summary's season flag cannot
// stand in for either the base construction or a successful save/publish.
const json = name => JSON.parse(read(name).replace(/^\uFEFF/, ''))
const cardCatalog = json('../服务端WebSocket/TwelveLegions/Data/cards.s1.json')
const prebuilt = { ...json('../服务端WebSocket/TwelveLegions/Data/preset-decks.s1.json')[0], name: '赛季受限构筑' }
const identities = json('../服务端WebSocket/TwelveLegions/Data/morale-identities.json')
const validators = execute(functionText('src/l12/openingHandEligibility.ts', ['isDerivedDeckSpecialCard', 'bypassesNormalDrawDeck'])
  + '\n' + functionText('src/l12/decks.ts', ['canonicalMoraleCardId', 'validateDeck', 'effectiveDeckLimit',
    'doesNotCountTowardMainDeck', 'isDerivedSpecialCard', 'deckCountSummary', 'trialCapacityForMaster']), {
  MAIN_DECK_TYPES: new Set(['legion', 'tactic', 'artifact']),
  moraleIdentityByFaction: new Map(identities.map(identity => [identity.faction, identity])),
  moraleIdentityByVersion: new Map(identities.flatMap(identity => identity.versionCardIds.map(id => [id, identity]))),
  moraleIdentityByGodPower: new Map(identities.filter(identity => identity.godPowerCardId).map(identity => [identity.godPowerCardId, identity])),
}, '({validateDeck,effectiveDeckLimit,doesNotCountTowardMainDeck,deckCountSummary})')
assert.equal(validators.validateDeck(prebuilt, cardCatalog), '')
const restrictedCard = cardCatalog.find(card => card.id === prebuilt.cardIds[0])
function constructionRuntime(maxCopies = 0, mutate = () => {}) {
  const deck = structuredClone(prebuilt); mutate(deck)
  const counts = ref(Object.fromEntries([...new Set(deck.cardIds)].map(id => [id, deck.cardIds.filter(cardId => cardId === id).length])))
  const byId = new Map(cardCatalog.map(card => [card.id, card]))
  const entries = computed(() => Object.entries(counts.value).map(([id, count]) => ({ card: byId.get(id) ?? { id }, count })))
  const dependencies = { ...validators, ref, computed, counts, entries, catalog: ref(cardCatalog),
    selectedMaster: ref(byId.get(deck.masterId)), masterId: ref(deck.masterId), deckName: ref(deck.name),
    moraleIds: ref(deck.moraleIds), specialIds: ref(deck.specialIds ?? []), operationsPolicyLoaded: ref(true),
    operationsRestrictions: ref([{ cardId: restrictedCard.id, maxCopies, reason: '本赛季排位规则' }]),
    totalCards: computed(() => validators.deckCountSummary(Object.entries(counts.value).flatMap(([id, count]) => Array(count).fill(id)), byId).counted),
    notice: ref(''), selected: ref(null), openingHandIds: ref([]), alternateArtCopies: ref({}), alternateArtSelections: ref({}),
    currentDeck: () => ({ ...deck, cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)) }),
  }
  const names = ['construction', 'validation', 'seasonValidation', 'seasonAdvisory']
  const source = names.filter(name => tree(editorPath).statements.some(node => ts.isVariableStatement(node)
    && node.declarationList.declarations.some(declaration => declaration.name.getText() === name))).map(declarationText).join('\n')
    + '\n' + functionText(editorPath, ['restrictionFor', 'allowedCopies', 'cardLegality', 'entryIssue', 'seasonEntryIssue', 'normalizedAppearanceList', 'addCardAppearance'])
  const runtime = execute(source, dependencies, '({validation, allowedCopies, cardLegality, entryIssue, seasonEntryIssue, addCardAppearance,'
    + 'seasonValidation: typeof seasonValidation === "undefined" ? null : seasonValidation,'
    + 'seasonAdvisory: typeof seasonAdvisory === "undefined" ? null : seasonAdvisory})')
  return { ...runtime, ...dependencies, workingDeck: dependencies.currentDeck() }
}
for (const maxCopies of [0, 1]) {
  await check(`season maxCopies=${maxCopies} is advisory, while the real base-valid deck saves, copies, publishes and exports`, async () => {
    const c = constructionRuntime(maxCopies)
    assert.equal(c.validation.value, '', 'season restrictions must not make the base construction invalid')
    assert.match(c.seasonValidation.value, /禁用|最多/)
    assert.match(c.seasonAdvisory.value, /排位不可用/)
    assert.equal(c.cardLegality(restrictedCard), maxCopies ? 'restricted' : 'banned')
    assert.match(c.seasonEntryIssue(restrictedCard), /本赛季排位/)
    assert.equal(c.entryIssue(restrictedCard, 3), '')
    assert.equal(c.allowedCopies(restrictedCard), 3)
    for (const action of ['onSave', 'onSaveAs', 'publishCurrentDeck', 'generateDeckImage']) {
      const r = editorRuntime({ validation: c.validation, catalog: c.catalog, workingDeck: c.workingDeck })
      await r[action]()
      assert.equal(r.state.saved.length, action === 'publishCurrentDeck' ? 2 : action === 'generateDeckImage' ? 0 : 1)
      assert.equal(r.state.published.length, action === 'publishCurrentDeck' ? 1 : 0)
      assert.equal(r.state.images.length, action === 'generateDeckImage' ? 1 : 0)
      assert.equal(r.deckMutationBusy.value, false)
      const submitted = r.state.saved[0] ?? r.state.images[0]
      assert.deepEqual(submitted.cardIds, c.workingDeck.cardIds)
      if (action === 'onSaveAs') { assert.equal(submitted.id, undefined); assert.equal(submitted.publicationId, null) }
    }
    c.counts.value = { ...c.counts.value, [restrictedCard.id]: 1 }
    c.addCardAppearance(restrictedCard, 'synthetic-art')
    c.addCardAppearance(restrictedCard)
    assert.equal(c.counts.value[restrictedCard.id], 3, 'banned/limited cards still add to their inherent copy limit')
    assert.deepEqual(c.alternateArtCopies.value[restrictedCard.id], ['', 'synthetic-art', ''])
    c.addCardAppearance(restrictedCard)
    assert.equal(c.counts.value[restrictedCard.id], 3, 'alternate and original art must share the inherent limit')
  })
}
for (const [name, mutate] of [
  ['short main deck', deck => { deck.cardIds = deck.cardIds.slice(0, 39) }],
  ['unknown card', deck => { deck.cardIds[0] = 'UNKNOWN' }],
  ['wrong faction', deck => { deck.cardIds[0] = cardCatalog.find(card => card.cardType === 'legion' && !['universal', 'tianting'].includes(card.faction)).id }],
  ['inherent copy limit', deck => { deck.cardIds[deck.cardIds.findIndex(id => id !== restrictedCard.id)] = restrictedCard.id }],
  ['wrong morale count', deck => { deck.moraleIds.pop() }],
  ['invalid master', deck => { deck.masterId = 'UNKNOWN' }],
]) await check(`${name} still prevents every authoritative editor action`, async () => {
  const c = constructionRuntime(0, mutate)
  assert.notEqual(c.validation.value, '')
  for (const action of ['onSave', 'onSaveAs', 'publishCurrentDeck', 'generateDeckImage']) {
    const r = editorRuntime({ validation: c.validation, catalog: c.catalog, workingDeck: c.workingDeck })
    await r[action]()
    assert.deepEqual([r.state.saved.length, r.state.published.length, r.state.images.length], [0, 0, 0])
    assert.equal(r.notice.value, c.validation.value)
  }
})
await check('missing seasonal policy stays unconfirmed without blocking a base-valid save', async () => {
  const c = constructionRuntime(); c.operationsPolicyLoaded.value = false
  assert.equal(c.validation.value, ''); assert.match(c.seasonAdvisory.value, /待确认/)
  const r = editorRuntime({ validation: c.validation, workingDeck: c.workingDeck }); await r.onSave()
  assert.equal(r.state.saved.length, 1)
})
await check('base-valid seasonal decks retain save locks and the unproven draft revision guard', async () => {
  const c = constructionRuntime(), wait = deferred()
  const r = editorRuntime({ validation: c.validation, workingDeck: c.workingDeck, requestSave: () => wait.promise })
  const first = r.onSave(); await Promise.resolve()
  await r.onSave(); await r.onSaveAs(); await r.publishCurrentDeck()
  assert.equal(r.state.saved.length, 1); assert.equal(r.state.published.length, 0)
  wait.resolve({ ...c.workingDeck, id: 'saved', revision: 1 }); await first
  assert.equal(r.deckMutationBusy.value, false)
  const draft = editorRuntime({ validation: c.validation, workingDeck: c.workingDeck })
  draft.activeDeckName.value = '旧草稿'; await draft.onSave(); await draft.publishCurrentDeck()
  assert.equal(draft.state.saved.length, 0); assert.match(draft.notice.value, /原牌库版本/)
})
await check('account A-B-A during save cannot launch a stale public publish', async () => {
  const c = constructionRuntime(), wait = deferred()
  const r = editorRuntime({ validation: c.validation, workingDeck: c.workingDeck, requestSave: () => wait.promise })
  const pending = r.publishCurrentDeck(); await Promise.resolve()
  r.bumpAccount(); r.bumpAccount(); wait.resolve({ ...c.workingDeck, id: 'saved', revision: 1 }); await pending
  assert.equal(r.state.published.length, 0); assert.equal(r.activeDeckId.value, null)
  assert.equal(r.deckMutationBusy.value, false)
})

console.log(`Private editor Focused: ${passed}/${passed}, failed=0, skipped=0`)
