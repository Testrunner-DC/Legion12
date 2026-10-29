import assert from 'node:assert/strict'
import { readFileSync, writeFileSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import ts from 'typescript'
import { reactive } from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import { idbModel } from './test-deck-cache-database.mjs'
import './test-deck-legacy-import-style.mjs'

const front = resolve(fileURLToPath(new URL('..', import.meta.url)))
const require = createRequire(import.meta.url)
const pins = {}
const text = file => { const raw = readFileSync(file, 'utf8').replaceAll('\r\n', '\n'); pins[file.slice(front.length + 1)] = createHash('sha256').update(raw).digest('hex'); return raw }
const dataUrl = code => 'data:text/javascript;base64,' + Buffer.from(code).toString('base64')
class Storage {
  values = new Map(); writes = []
  getItem(key) { return this.values.get(key) ?? null }
  setItem(key, value) { this.writes.push(key); this.values.set(key, String(value)) }
  removeItem(key) { this.writes.push(key); this.values.delete(key) }
}
globalThis.localStorage = new Storage(); globalThis.sessionStorage = new Storage()
const platformState = reactive({ account: null, token: '' })
let handler = async () => { throw new Error('unexpected HTTP') }
globalThis.__transactionalDeckFixture = { platformState, request: (...args) => handler(...args) }
const model = idbModel(); globalThis.indexedDB = model.factory
const loaded = new Map()
async function load(file, environment = '/') {
  const key = file + environment
  if (loaded.has(key)) return loaded.get(key)
  let source = text(file).replace('import.meta.env.BASE_URL', JSON.stringify(environment))
  let code = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
  const imports = [...code.matchAll(/from\s+['"]([^'"]+)['"]/g)]
  for (const match of imports) {
    const specifier = match[1]
    let replacement
    if (specifier.endsWith('/platform') || specifier === './platform') replacement = dataUrl(`
      export const platformState = globalThis.__transactionalDeckFixture.platformState;
      export const platformRequest = (...args) => globalThis.__transactionalDeckFixture.request(...args);
      export const getDeckCoverRequestHeaders = () => ({'X-L12-Deck-Cover':'1'});
      export const getRepresentativeDeckRequestHeaders = () => ({'X-L12-Deck-Cover':'2'});
      export const getEffectiveOperationsPolicy = async () => ({defaultPresetDeckIds:[]});`)
    else if (specifier.endsWith('.json')) replacement = dataUrl('export default ' + (specifier.includes('product-inclusions') ? '{products:[],cards:[]}' : specifier.includes('archive-assets') ? '{cards:[]}' : '[]'))
    else if (specifier === './cardPresentation') replacement = dataUrl('export const normalizeLookupCardType = value => value')
    else if (specifier.startsWith('.')) replacement = await load(resolve(dirname(file), specifier + '.ts'), environment)
    else replacement = pathToFileURL(require.resolve(specifier)).href
    code = code.replaceAll(`from '${specifier}'`, `from '${replacement}'`).replaceAll(`from "${specifier}"`, `from '${replacement}'`)
  }
  const url = dataUrl(code)
  loaded.set(key, url)
  return url
}
const decks = await import(await load(resolve(front, 'src/l12/decks.ts')))
const drafts = await import(await load(resolve(front, 'src/l12/site/deckEditorDraft.ts')))
const database = await import(await load(resolve(front, 'src/l12/deckCacheDatabase.ts')))
const fixtureDeck = name => ({ name, updatedAt: '2026-10-29', masterId: 'S01-01M1', cardIds: ['S01-0101', 'S01-0102'], moraleIds: [], specialIds: [], representativeCardIds: ['S01-0102'] })
const oldRaw = JSON.stringify({ old: fixtureDeck('旧牌库') })
localStorage.values.set('l12-custom-decks-v1', oldRaw)
await decks.initializeDeckCache()
assert.deepEqual(decks.loadSavedDecks(), {}, 'Legacy is not silently adopted as the new authority')
assert.equal(decks.loadLegacyDeckImports()[0].raw, oldRaw)
assert.equal(await decks.enableRepresentativeCards(), true)
let saved = await decks.saveDeck(fixtureDeck('新牌库'))
assert.deepEqual(decks.loadSavedDecks()['新牌库'].representativeCardIds, ['S01-0102'])
assert.equal(localStorage.getItem('l12-custom-decks-v1'), oldRaw)
assert.deepEqual(localStorage.writes, [])
localStorage.values.set('l12-custom-decks-v1', oldRaw + ' ')
await decks.initializeDeckCache()
assert.equal(decks.loadLegacyDeckImports().length, 2)
saved = await decks.saveDeck({ ...saved, name: '新页继续保存' })
await decks.saveSelectedDeckName('friendly', saved.name)
assert.equal(decks.loadSelectedDeckName('friendly', decks.loadSavedDecks()), saved.name)
const raced = await Promise.allSettled([decks.saveDeck({ ...saved, name: '并发一' }), decks.saveDeck({ ...saved, name: '并发二' })])
assert.equal(raced.filter(item => item.status === 'fulfilled').length, 1)
assert.equal(raced.filter(item => item.status === 'rejected').length, 1)
assert.equal(Object.values(decks.loadSavedDecks()).length, 1)
assert.deepEqual(localStorage.writes, [])
let releaseFinalCommit
model.pauseNextWriteCommit(complete => { releaseFinalCommit = complete })
const completedAfterSwitch = decks.saveDeck(fixtureDeck('put之后换号'))
const rejectedAfterPut = assert.rejects(completedAfterSwitch, /账号已切换/)
while (!releaseFinalCommit) await new Promise(resolveWait => setImmediate(resolveWait))
platformState.account = { id: 'after-put-other-owner' }; platformState.token = 'other-token'
releaseFinalCommit(); await rejectedAfterPut
await decks.initializeDeckCache()
assert.deepEqual(decks.loadSavedDecks(), {}, 'Old-owner completion must not publish into the new account')
platformState.account = null; platformState.token = ''
await decks.initializeDeckCache()
const scope = drafts.transactionalDraftScope('a')
const firstDraft = await drafts.writeTransactionalDeckDraft(scope, fixtureDeck('草稿一'), null, null, null, null)
await assert.rejects(drafts.writeTransactionalDeckDraft(scope, fixtureDeck('草稿二'), null, null, null, null), /其他页面|另一个页面/)
await drafts.writeTransactionalDeckDraft(scope, fixtureDeck('换号恢复稿'), null, null, null, null, true)
assert.equal((await drafts.readTransactionalDeckDrafts(scope)).length, 2)
await drafts.clearTransactionalDeckDraft(scope, firstDraft)
assert.equal((await drafts.readTransactionalDeckDrafts(scope)).length, 1)
const guestOne = drafts.transactionalDraftScope(null)
await drafts.writeTransactionalDeckDraft(guestOne, fixtureDeck('访客草稿'), null, null, null, null)
sessionStorage.values.clear()
assert.equal((await drafts.readTransactionalDeckDrafts(drafts.transactionalDraftScope(null))).length, 0)
const testDrafts = await import(await load(resolve(front, 'src/l12/site/deckEditorDraft.ts'), '/testrun/'))
assert.equal((await testDrafts.readTransactionalDeckDrafts(testDrafts.transactionalDraftScope('a'))).length, 0)
platformState.account = { id: 'a' }; platformState.token = 'token-a'
localStorage.values.set('l12-custom-decks-v1:a', JSON.stringify({ accountLegacy: fixtureDeck('账号旧资料') }))
await decks.initializeDeckCache()
assert.equal(decks.loadLegacyDeckImports().length, 1, 'Account legacy sources must not read the guest namespace')
handler = async (_path, options) => ({ ...JSON.parse(options.body), id: 'account-selected', revision: 1 })
const accountSaved = await decks.saveDeck(fixtureDeck('账号选牌'))
await decks.saveSelectedDeckName('ranked', accountSaved.name)
assert.equal(decks.hasSavedDeckSelection('ranked'), true)
assert.equal(decks.loadSelectedDeckName('ranked', decks.loadSavedDecks()), accountSaved.name)
handler = async () => ({})
await decks.deleteDeck(accountSaved)
assert.equal(decks.loadSelectedDeckName('ranked', decks.loadSavedDecks()), '')
let release, called = false
handler = async (_path, options) => { called = true; return new Promise(resolveResponse => { release = () => resolveResponse({ ...JSON.parse(options.body), id: 'server-a', revision: 1 }) }) }
const lateSave = decks.saveDeck(fixtureDeck('迟到保存'))
const rejectedLate = assert.rejects(lateSave, /账号已切换/)
while (!called) await new Promise(resolveWait => setImmediate(resolveWait))
platformState.account = { id: 'b' }; platformState.token = 'token-b'
await decks.initializeDeckCache(); release(); await rejectedLate
assert.deepEqual(decks.loadSavedDecks(), {})
assert.deepEqual(localStorage.writes, [])
platformState.account = { id: 'a' }; platformState.token = 'token-a'
await decks.initializeDeckCache(); called = false
const interrupted = decks.saveDeck(fixtureDeck('服务端确认本机失败'))
const rejectedInterrupted = assert.rejects(interrupted, error => error.serverConfirmed === true)
while (!called) await new Promise(resolveWait => setImmediate(resolveWait))
model.failNextCommit(); release(); await rejectedInterrupted
assert.deepEqual(decks.loadSavedDecks(), {})
assert.deepEqual(localStorage.writes, [])
const editorRaw = text(resolve(front, 'src/l12/L12DeckEditor.vue'))
const editorTree = ts.createSourceFile('editor.ts', parse(editorRaw).descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true)
function editorFunctions(names, dependencies, prefix = '') {
  const selected = names.map(name => editorTree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name).getText(editorTree)).join('\n')
  const code = ts.transpileModule(selected, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(dependencies), prefix + code + '; return {' + names.join(',') + '}')(...Object.values(dependencies))
}
assert(editorRaw.includes('@click="saveLocalDraft()"'), 'The default template action must not pass MouseEvent as a recovery flag')
assert(editorRaw.includes('@click="saveLocalDraft(true)"'), 'The conflict must expose an explicit recovery action')
const conflictScope = drafts.transactionalDraftScope('conflict-ui')
const originalDraft = await drafts.writeTransactionalDeckDraft(conflictScope, fixtureDeck('原草稿'), null, null, null, null)
const conflictRefs = {
  deckMutationBusy: { value: false }, deletingDeck: { value: false }, draftBusy: { value: false }, draftConflict: { value: false },
  editorContentRevision: { value: 1 }, localDraft: { value: null }, localDraftEntry: { value: null },
  activeDeckName: { value: null }, activeDeckId: { value: null }, activeDeckRevision: { value: null },
  selectedDraftSlot: { value: '' }, persistedContentRevision: { value: 0 }, restoredLocalDraft: { value: false },
  secondaryActionsOpen: { value: true }, notice: { value: '' },
}
let confirmRecovery = true, actorEpoch = 0
const conflictUI = editorFunctions(['saveLocalDraft'], {
  ...conflictRefs, window: { confirm: () => confirmRecovery }, draftScope: () => conflictScope,
  editorContext: () => ({ epoch: actorEpoch }), isCurrentEditorContext: context => context.epoch === actorEpoch,
  currentDeck: () => ({ ...fixtureDeck('未完成当前构筑'), masterId: '' }),
  writeTransactionalDeckDraft: drafts.writeTransactionalDeckDraft, mutationError: error => error.message,
  refreshLocalDraft: async () => {
    const entries = await drafts.readTransactionalDeckDrafts(conflictScope)
    conflictRefs.localDraftEntry.value = entries.find(item => item.slot === conflictRefs.selectedDraftSlot.value) ?? entries[0]
  },
})
await conflictUI.saveLocalDraft()
assert.equal(conflictRefs.draftConflict.value, true)
assert.equal((await drafts.readTransactionalDeckDrafts(conflictScope)).length, 1)
await conflictUI.saveLocalDraft({ type: 'click' })
assert.equal((await drafts.readTransactionalDeckDrafts(conflictScope)).length, 1, 'MouseEvent must remain ordinary CAS')
confirmRecovery = false; await conflictUI.saveLocalDraft(true)
assert.equal((await drafts.readTransactionalDeckDrafts(conflictScope)).length, 1, 'Cancellation performs no write')
confirmRecovery = true
let finishRecovery
model.pauseNextWriteCommit(complete => { finishRecovery = complete })
const recoverySave = conflictUI.saveLocalDraft(true)
while (!finishRecovery) await new Promise(resolveWait => setImmediate(resolveWait))
assert.equal(conflictRefs.persistedContentRevision.value, 0, 'Recovery cannot declare success before transaction complete')
finishRecovery(); await recoverySave
const recoveredCopies = await drafts.readTransactionalDeckDrafts(conflictScope)
assert.equal(recoveredCopies.length, 2)
assert.equal(recoveredCopies.find(item => item.slot === originalDraft.slot).raw, originalDraft.raw, 'Recovery preserves original draft byte-for-byte')
assert.equal(recoveredCopies.find(item => item.slot !== originalDraft.slot).draft.deck.masterId, '', 'Incomplete construction can persist as a recovery draft')
assert.equal(conflictRefs.draftConflict.value, false)
assert.equal(conflictRefs.persistedContentRevision.value, 1)
conflictRefs.draftConflict.value = true; conflictRefs.editorContentRevision.value = 2
finishRecovery = null; model.pauseNextWriteCommit(complete => { finishRecovery = complete })
const oldActorRecovery = conflictUI.saveLocalDraft(true)
while (!finishRecovery) await new Promise(resolveWait => setImmediate(resolveWait))
const oldSelectedSlot = conflictRefs.selectedDraftSlot.value
actorEpoch++
const accountWatch = editorTree.statements.find(node => ts.isExpressionStatement(node) && ts.isCallExpression(node.expression)
  && node.expression.expression.getText(editorTree) === 'watch'
  && node.expression.arguments[0]?.getText(editorTree).includes('platformState.account?.id, platformState.token'))
const accountCallbackCode = ts.transpileModule('const switchActor = ' + accountWatch.expression.arguments[1].getText(editorTree),
  { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const actorDependencies = { draftConflict: conflictRefs.draftConflict, deckReadBusy: { value: false }, savedDirectoryLoading: { value: false },
  savedDirectory: { value: null }, savedDirectoryError: { value: '' }, loading: { value: false }, applyEditorAccountChange() {},
  refreshEditorAccountReads: async () => {}, editorContext: () => ({}) }
new Function(...Object.keys(actorDependencies), 'let editorAccountEpoch=0,directoryReadSequence=0,deckReadSequence=0;' + accountCallbackCode + ';return switchActor')
  (...Object.values(actorDependencies))(['b', 'token-b'], ['a', 'token-a'])
assert.equal(conflictRefs.draftConflict.value, false, 'The actual account watcher clears the old recovery action')
conflictRefs.notice.value = 'new account notice'
finishRecovery(); await oldActorRecovery
assert.equal(conflictRefs.selectedDraftSlot.value, oldSelectedSlot)
assert.equal(conflictRefs.persistedContentRevision.value, 1)
assert.equal(conflictRefs.notice.value, 'new account notice', 'Late old-owner recovery cannot publish success to the new actor')
const pendingDrafts = new Map([['pending', { accountId: 'draft-recovery-owner', deck: fixtureDeck('待恢复'), name: null, id: null, revision: null }]])
const pendingDraftCount = { value: 1 }, recoveryNotice = { value: '' }
let scopeDenied = true
const recovery = editorFunctions(['recoverPendingAccountDrafts'], {
  pendingAccountDrafts: pendingDrafts, pendingDraftCount, notice: recoveryNotice,
  draftScopeFor: owner => { if (scopeDenied) throw Error('session storage unavailable'); return drafts.transactionalDraftScope(owner) },
  writeTransactionalDeckDraft: drafts.writeTransactionalDeckDraft,
}, 'let recoveringAccountDrafts = false;')
await recovery.recoverPendingAccountDrafts()
assert.equal(pendingDrafts.size, 1, 'Failure before opening IDB must retain the old working document')
scopeDenied = false; model.failNextCommit()
await recovery.recoverPendingAccountDrafts()
assert.equal(pendingDrafts.size, 1, 'IDB failure must retain the old working document')
await Promise.all([recovery.recoverPendingAccountDrafts(), recovery.recoverPendingAccountDrafts()])
assert.equal(pendingDrafts.size, 0)
assert.equal((await drafts.readTransactionalDeckDrafts(drafts.transactionalDraftScope('draft-recovery-owner'))).length, 1, 'Parallel recovery retries may not duplicate a pending draft')
let resolveLegacyName, loadedLegacy = false
const legacyRevision = { value: 1 }
const restore = editorFunctions(['loadLegacyCandidate'], {
  confirmDiscardChanges: () => true, window: { confirm: () => true }, deploymentBasePath: '/',
  editorContext: () => ({}), editorContentRevision: legacyRevision,
  uniqueDeckCopyName: () => new Promise(resolveName => { resolveLegacyName = resolveName }),
  isCurrentEditorContext: () => true, loadDeck: () => { loadedLegacy = true }, persistedContentRevision: { value: 0 },
  notice: { value: '' }, mutationError: error => error.message,
  activeDeckName: { value: null }, activeDeckId: { value: null }, activeDeckRevision: { value: null },
})
const restoringLegacy = restore.loadLegacyCandidate(fixtureDeck('旧资料'))
legacyRevision.value++; resolveLegacyName('旧资料副本'); await restoringLegacy
assert.equal(loadedLegacy, false, 'An edit during a legacy-source lookup must not be overwritten')
platformState.account = { id: 'manual-import-owner' }; platformState.token = 'manual-import-token'
const legacyForImport = { ...fixtureDeck('旧资料'), id: 'legacy-original', revision: 9 }
const retainedLegacyRaw = JSON.stringify({ [legacyForImport.name]: legacyForImport })
localStorage.values.set('l12-custom-decks-v1:manual-import-owner', retainedLegacyRaw)
await decks.initializeDeckCache()
const importRequests = []
handler = async (path, options) => {
  importRequests.push({ path, method: options.method, body: JSON.parse(options.body) })
  assert.equal(path, '/api/decks'); assert.equal(options.method, 'POST')
  assert.equal(JSON.parse(options.body).id, undefined)
  return { ...JSON.parse(options.body), id: 'created-import-' + importRequests.length, revision: 1 }
}
const retainedModern = await decks.saveDeck(fixtureDeck('保留的现代牌库'))
const importRefs = Object.fromEntries(['publicationId','publicationCode','publicationVersion','activeDeckName','activeDeckId','activeDeckRevision',
  'deckName','masterId','counts','benchCounts','representativeCardIds','specialIds','alternateArtSelections','alternateArtCopies',
  'openingHandIds','persistedContentRevision','editorContentRevision','restoredLocalDraft','draftConflict','notice',
  'representativesEnabled','deckMutationBusy','deletingDeck','savedDecks','moraleIds'].map(key => [key,{value:null}]))
Object.assign(importRefs,{ counts:{value:{}},benchCounts:{value:{}},representativeCardIds:{value:[]},specialIds:{value:[]},
  alternateArtSelections:{value:{}},alternateArtCopies:{value:{}},editorContentRevision:{value:1},persistedContentRevision:{value:0},
  representativesEnabled:{value:true},deckMutationBusy:{value:false},deletingDeck:{value:false},moraleIds:{value:[]},notice:{value:''} })
const importUI = editorFunctions(['loadLegacyCandidate','loadDeck','currentDeck','canSaveCurrentDeck','onSave'], {
  ...importRefs, confirmDiscardChanges:()=>true,window:{confirm:()=>true},deploymentBasePath:'/',
  editorContext:()=>({}),isCurrentEditorContext:()=>true,uniqueDeckCopyName:async name=>name,
  byId:{value:new Map()},MAIN_DECK_TYPES:new Set(['legion','tactic','artifact']),resolvePublishedDeck:async()=>{},
  entries:{get value(){return Object.entries(importRefs.counts.value).map(([id,count])=>({card:{id},count}))}},
  benchEntries:{get value(){return Object.entries(importRefs.benchCounts.value).map(([id,count])=>({card:{id},count}))}},
  validation:{value:''},saveDeck:decks.saveDeck,loadSavedDecks:decks.loadSavedDecks,refreshSavedDirectoryAfterMutation:()=>{},
  clearCurrentDraftAfterServerSave:async()=>{},router:{replace:async()=>{}},route:{query:{}},
  deckErrorBelongsToCurrentAccount:()=>true,mutationError:error=>error.message,
}, 'let editorDocumentEpoch=0;')
await importUI.loadLegacyCandidate(legacyForImport)
assert.equal(importRefs.deckName.value, '旧资料 旧资料')
assert.equal(importRefs.activeDeckName.value, null)
assert.equal(importRefs.activeDeckId.value, null)
assert.equal(importRefs.activeDeckRevision.value, null)
await importUI.onSave()
assert.equal(importRequests.length, 2, 'Normal save after explicit as-new import must issue exactly one new POST')
assert.equal(importRefs.activeDeckId.value, 'created-import-2')
assert.notEqual(importRefs.activeDeckId.value, legacyForImport.id)
assert.deepEqual(decks.loadSavedDecks()[retainedModern.name], retainedModern, 'An existing modern deck must not change')
assert.equal(localStorage.getItem('l12-custom-decks-v1:manual-import-owner'), retainedLegacyRaw, 'Legacy source bytes must remain intact')
assert.equal(Object.keys(decks.loadSavedDecks()).length, 2)
importRefs.activeDeckName.value='未知旧草稿';importRefs.activeDeckId.value=null;importRefs.activeDeckRevision.value=null
await importUI.onSave()
assert.equal(importRequests.length, 2, 'Ordinary unproven old drafts must still be rejected before a request')
assert.match(importRefs.notice.value,/无法确认.*原牌库版本/)
for (const name of ['L12DeckEditor.vue', 'LobbyPage.vue', 'site/DeckLibraryPage.vue', 'site/BattleHubPage.vue', 'site/SandboxPage.vue']) {
  const file = resolve(front, 'src/l12', name), raw = text(file), parsed = parse(raw, { filename: file })
  assert.equal(parsed.errors.length, 0)
  const script = compileScript(parsed.descriptor, { id: name, inlineTemplate: false })
  const template = compileTemplate({ source: parsed.descriptor.template.content, filename: file, id: name,
    compilerOptions: { bindingMetadata: script.bindings } })
  assert.equal(template.errors.length, 0, name + ' real SFC template compiles')
  if (name.includes('BattleHub') || name.includes('Sandbox')) assert(raw.includes('await saveSelectedDeckName('))
}
const platformSource = text(resolve(front, 'src/l12/platform.ts'))
const headerTree = ts.createSourceFile('platform.ts', platformSource, ts.ScriptTarget.Latest, true)
const header = headerTree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === 'getRepresentativeDeckRequestHeaders')
assert(header)
assert(!/localStorage|navigator\.locks/.test(header.getText(headerTree)))
const productionHeader = ts.transpileModule(header.getText(headerTree).replace('export ', ''), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
assert.deepEqual(new Function('getDeckCoverRequestHeaders', productionHeader + ';return getRepresentativeDeckRequestHeaders()')(() => ({ 'X-L12-Deck-Cover': '1' })), { 'X-L12-Deck-Cover': '2' })
assert.deepEqual(new Function('getDeckCoverRequestHeaders', productionHeader + ';return getRepresentativeDeckRequestHeaders()')(() => ({})), {})
assert(!/localStorage\.(setItem|removeItem)\(/.test(text(resolve(front, 'src/l12/decks.ts'))))
const report = { passed: true, sourcePins: pins, boundary: 'Production functions and actual SFC compile with deterministic IDB model; native browser pending.' }
if (process.env.L12_DECK_DELIVERY_ROOT) {
  const deliveryRoot = resolve(process.env.L12_DECK_DELIVERY_ROOT)
  const leaseFiles = [
    'src/l12/L12DeckEditor.vue', 'src/l12/LobbyPage.vue', 'src/l12/deckCacheCodec.ts', 'src/l12/deckCacheStorage.ts',
    'src/l12/deckCacheDatabase.ts', 'src/l12/decks.ts', 'src/l12/platform.ts', 'src/l12/site/deckEditorDraft.ts',
    'src/l12/site/publicDeckRead.ts', 'src/l12/site/DeckLibraryPage.vue', 'src/l12/site/BattleHubPage.vue', 'src/l12/site/SandboxPage.vue',
    'scripts/deck-transaction-test-fixture.mjs', 'scripts/test-deck-cache-database.mjs', 'scripts/test-deck-transactional-consumers.mjs',
    'scripts/test-deck-editor-draft.mjs', 'scripts/test-deck-sync-authority.mjs', 'scripts/test-private-deck-read-consumers.mjs',
    'scripts/test-private-deck-mode-consumers.mjs', 'scripts/test-private-deck-editor-consumers.mjs', 'scripts/test-private-deck-name-reconciliation.mjs',
    'scripts/test-private-deck-light-consumers.mjs', 'scripts/test-private-deck-metadata-consumers.mjs', 'scripts/test-public-deck-read-consumers.mjs',
  ]
  const filePins = leaseFiles.map(relative => {
    const bytes = readFileSync(resolve(front, relative))
    return { path: relative, bytes: bytes.length, rawSha256: createHash('sha256').update(bytes).digest('hex'),
      lfSha256: createHash('sha256').update(bytes.toString('utf8').replaceAll('\r\n', '\n')).digest('hex') }
  })
  const frozen = JSON.parse(readFileSync(resolve(deliveryRoot, '../frozen-complete-scope.json'), 'utf8'))
  const backendPins = frozen.files.filter(item => !item.path.includes('opcgpro-vue')).map(item => {
    const current = createHash('sha256').update(readFileSync(item.path)).digest('hex')
    return { path: item.path, frozenSha256: item.sha256, currentSha256: current, unchanged: current === item.sha256 }
  })
  assert(backendPins.every(item => item.unchanged), 'The frozen backend lease must remain untouched')
  const nativePath = resolve(deliveryRoot, 'native-idb-receipt-67afc33c-0fae-4339-b847-e7c104985025.json')
  const native = JSON.parse(readFileSync(nativePath, 'utf8'))
  assert.equal(Object.keys(native.results).length, 6)
  assert(Object.values(native.results).every(item => item.passed))
  for (const [name, expected] of Object.entries(native.pins))
    assert.equal(filePins.find(item => item.path === `src/l12/${name}.ts`)?.lfSha256
      ?? pins[`src\\l12\\${name}.ts`], expected, `Reused native evidence remains bound to ${name}`)
  const backendReceipt = JSON.parse(readFileSync(resolve(deliveryRoot, '../backend-delivery.json'), 'utf8'))
  const oldBackendReceiptMismatches = backendReceipt.sources.flatMap(item => {
    const actual = backendPins.find(current => current.path === item.path)?.currentSha256
    return actual && actual !== item.sha256 ? [{ path: item.path, receiptSha256: item.sha256, currentSha256: actual }] : []
  })
  writeFileSync(resolve(deliveryRoot, 'frontend-delivery.json'), JSON.stringify({
    generatedAt: new Date().toISOString(), state: 'frontend source frozen for Main independent combined acceptance; not deployment',
    files: filePins, frozenBackendDependencies: backendPins, oldBackendReceiptMismatches,
    nativeEvidence: { path: nativePath, reusedSourcePins: native.pins, cases: 6, passed: 6, ownedServiceExited: true },
    focusedEvidence: process.env.L12_DECK_TARGETED_REPORT?.split(/[\\/]/).at(-1) ?? null,
    limits: ['Legacy-format scenario fixtures seed logical transaction records, not browser localStorage. Independent production-source tests keep legacy and IDB ports separate.',
      'Main owns full combined qualification, Git and deployment.',
      'Native receipt exercises four production persistence modules; the resumed manual-import panel has SFC/type tests but no full-App visual/hit-test receipt.',
      'The backend-delivery 170 receipt has a different DeckRepresentativeHttpTests.cs pin; do not call it same-source current backend qualification.'],
  }, null, 2))
}
if (process.env.L12_DECK_TARGETED_REPORT) writeFileSync(process.env.L12_DECK_TARGETED_REPORT, JSON.stringify(report, null, 2))
console.log(JSON.stringify(report))
