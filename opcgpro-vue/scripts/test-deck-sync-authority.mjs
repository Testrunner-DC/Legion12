import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { reactive, watch } from 'vue'
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc'

const deckSourceUrl = new URL('../src/l12/decks.ts', import.meta.url)
const editorSourceUrl = new URL('../src/l12/L12DeckEditor.vue', import.meta.url)
const deckSource = readFileSync(deckSourceUrl, 'utf8').replaceAll('\r\n', '\n')
const editorSource = readFileSync(editorSourceUrl, 'utf8').replaceAll('\r\n', '\n')
const openingHandImport = `import {
  NORMAL_OPENING_HAND_CARD_TYPES,
  bypassesNormalDrawDeck,
  isDerivedDeckSpecialCard,
  isNormalOpeningHandCard,
  normalOpeningHandCopies,
} from './openingHandEligibility'`

class MemoryStorage {
  values = new Map()
  get length() { return this.values.size }
  clear() { this.values.clear() }
  getItem(key) { return this.values.has(key) ? this.values.get(key) : null }
  key(index) { return [...this.values.keys()][index] ?? null }
  removeItem(key) { this.values.delete(key) }
  setItem(key, value) { this.values.set(key, String(value)) }
}

globalThis.localStorage = new MemoryStorage()
const platformState = reactive({ account: null, token: '' })
let requestHandler = async () => { throw new Error('Unexpected platform request') }
globalThis.__deckAuthorityTestPlatform = {
  platformState,
  watch,
  getEffectiveOperationsPolicy: async () => ({ defaultPresetDeckIds: [] }),
  platformRequest: (...args) => requestHandler(...args),
}

const productionModuleUrl = source => 'data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText).toString('base64')
const publicDeckCounters = await import(productionModuleUrl(
  readFileSync(new URL('../src/l12/site/publicDeckEntry.ts', import.meta.url), 'utf8')))
const binaryUrl = productionModuleUrl(readFileSync(new URL('../src/l12/deckCodeCodec.ts', import.meta.url), 'utf8'))
const codecUrl = productionModuleUrl(readFileSync(new URL('../src/l12/deckCacheCodec.ts', import.meta.url), 'utf8')
  .replace("from './deckCodeCodec'", "from '" + binaryUrl + "'"))
const storageUrl = productionModuleUrl(readFileSync(new URL('../src/l12/deckCacheStorage.ts', import.meta.url), 'utf8')
  .replace("from './deckCodeCodec'", "from '" + binaryUrl + "'")
  .replace("from './deckCacheCodec'", "from '" + codecUrl + "'"))
const cacheStorage = await import(storageUrl)

const executableSource = deckSource
  .replace("import { watch } from 'vue'", 'const { watch } = globalThis.__deckAuthorityTestPlatform')
  .replace("from './deckCacheStorage'", "from '" + storageUrl + "'")
  .replace("from './deckCacheCodec'", "from '" + codecUrl + "'")
  .replace("import { normalizeLookupCardType } from './cardPresentation'", "const normalizeLookupCardType = value => value")
  .replace(
    "import { getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'",
    "const { getEffectiveOperationsPolicy, platformRequest, platformState } = globalThis.__deckAuthorityTestPlatform",
  )
  .replace("import { deploymentPath } from './deploymentBase'", "const deploymentPath = value => value")
  .replace("import moraleIdentityData from '../../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'", 'const moraleIdentityData = []')
  .replace("import cardProductInclusionsData from '../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'", 'const cardProductInclusionsData = { products: [], cards: [] }')
  .replace("import cardArchiveAssetsData from '../../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'", 'const cardArchiveAssetsData = { cards: [] }')
  .replace("import seasonTwoRulesData from '../../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'", 'const seasonTwoRulesData = []')
  .replace(openingHandImport, `const NORMAL_OPENING_HAND_CARD_TYPES = new Set(['legion', 'tactic', 'artifact'])
const isDerivedDeckSpecialCard = card => card?.cardType === 'token' || card?.id === 'S02-01S1' || card?.id === 'S02-06S2'
const bypassesNormalDrawDeck = card => isDerivedDeckSpecialCard(card) || Boolean(card?.effect?.includes('构筑时不计入卡组数量'))
const isNormalOpeningHandCard = card => Boolean(card && NORMAL_OPENING_HAND_CARD_TYPES.has(card.cardType) && !bypassesNormalDrawDeck(card))
const normalOpeningHandCopies = (copies, cardForCopy) => copies.filter(copy => isNormalOpeningHandCard(cardForCopy(copy)))`)
const executableJavaScript = ts.transpileModule(executableSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const decksModule = await import(`data:text/javascript;base64,${Buffer.from(executableJavaScript).toString('base64')}`).catch(error => { throw new Error('Production deck module failed: ' + error.message) })

function deck(name, updatedAt = '2026-09-09T00:00:00.000Z') {
  return { name, masterId: 'S01-01M1', cardIds: [], moraleIds: [], specialIds: [], updatedAt }
}

function owned(name, updatedAt = '2026-09-09T00:00:00.000Z', revision = 1, id = `id-${name}`) {
  return { ...deck(name, updatedAt), id, revision }
}

function accountKey(accountId = 'account-a') {
  return `l12-custom-decks-v1:${accountId}`
}

function seedAccountDecks(entries, accountId = 'account-a') {
  localStorage.setItem(accountKey(accountId), JSON.stringify(Object.fromEntries(entries.map(item =>
    [item.name, item.id ? item : { ...item, id: `id-${item.name}`, revision: 1 }]))))
}

function readAccountDecks(accountId = 'account-a') {
  return cacheStorage.requireDeckCache(cacheStorage.readDeckCache(localStorage, accountKey(accountId), 'account:' + accountId)).decks
}

function authenticate() {
  platformState.account = { id: 'account-a' }
  platformState.token = 'token-a'
}

function deferred() {
  let resolve
  let reject
  const promise = new Promise((onResolve, onReject) => { resolve = onResolve; reject = onReject })
  return { promise, resolve, reject }
}

async function authenticatedSyncUsesServerAuthority() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([deck('已在服务器删除的旧预组')])
  const requests = []
  requestHandler = async (path, init = {}) => {
    requests.push([path, init.method ?? 'GET'])
    if (path === '/api/decks' && !init.method) return []
    throw new Error('同步不得上传本地陈旧牌库')
  }
  const result = await decksModule.syncSavedDecksFromAccount()
  assert.deepEqual(requests, [['/api/decks', 'GET']], '登录同步只能读取服务器，不能 PUT 远端缺失的旧缓存')
  assert.deepEqual(result, {})
  assert.deepEqual(readAccountDecks(), {}, '成功同步后服务器缺失项必须从账号缓存消失')
}

function authenticatedLoadDoesNotImplicitlyMigrateGuestCache() {
  localStorage.clear()
  authenticate()
  localStorage.setItem('l12-custom-decks-v1', JSON.stringify({ 游客旧牌库: deck('游客旧牌库') }))
  assert.deepEqual(decksModule.loadSavedDecks(), {}, '登录读取不得把全局游客/旧缓存自动复制到账号命名空间')
  assert.equal(localStorage.getItem(accountKey()), null)
}

async function deleteWaitsForServerAndPreservesFailure() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([deck('待删除')])
  localStorage.setItem('l12-selected-custom-deck:account-a', '待删除')
  const pending = deferred()
  requestHandler = (path, init = {}) => {
    assert.equal(path, '/api/decks/by-id/id-%E5%BE%85%E5%88%A0%E9%99%A4?expectedRevision=1')
    assert.equal(init.method, 'DELETE')
    return pending.promise
  }
  const deletion = decksModule.deleteDeck('待删除')
  assert.ok(deletion instanceof Promise, '删除 API 必须可等待')
  assert.ok(readAccountDecks()['待删除'], '服务器响应前不得先删本地缓存')
  pending.resolve({})
  await deletion
  assert.deepEqual(readAccountDecks(), {})
  assert.equal(decksModule.loadSelectedDeckName('ranked', decksModule.loadSavedDecks()), '', 'Deleted selection is unresolved by the same atomic cache commit')
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a'), '待删除', 'Selection physical key remains unchanged')

  seedAccountDecks([deck('删除失败保留')])
  localStorage.setItem('l12-selected-custom-deck:account-a', '删除失败保留')
  requestHandler = async () => { throw new Error('simulated DELETE 500') }
  await assert.rejects(decksModule.deleteDeck('删除失败保留'), /simulated DELETE 500/)
  assert.ok(readAccountDecks()['删除失败保留'], 'DELETE 失败必须保留牌库缓存')
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a'), '删除失败保留')
}

async function deleteUsesSelectedIdentityAndRejectsStaleRevision() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([owned('旧名称', undefined, 1, 'id-original')])
  const staleSelection = readAccountDecks()['旧名称']
  seedAccountDecks([
    owned('旧名称', undefined, 1, 'id-reused'),
    owned('已改名', undefined, 2, 'id-original'),
  ])
  requestHandler = async () => { throw new Error('陈旧选择不得发出删除请求') }
  await assert.rejects(decksModule.deleteDeck(staleSelection), /其他操作更新/)
  assert.equal(Object.keys(readAccountDecks()).length, 2)

  const selected = readAccountDecks()['已改名']
  requestHandler = async (path, init = {}) => {
    assert.equal(path, '/api/decks/by-id/id-original?expectedRevision=2')
    assert.equal(init.method, 'DELETE')
    return {}
  }
  await decksModule.deleteDeck(selected)
  assert.deepEqual(Object.keys(readAccountDecks()), ['旧名称'], '按 ID 删除不能误删复用旧名称的新牌库')
  assert.equal(readAccountDecks()['旧名称'].id, 'id-reused')
}

async function allSelectionScopesRemainIsolatedAfterAtomicDeletion() {
  assert.deepEqual(decksModule.L12_DECK_SELECTION_SCOPES,
    ['ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent'])
  for (const actor of ['guest', 'account']) {
    localStorage.clear()
    if (actor === 'account') authenticate()
    else { platformState.account = null; platformState.token = '' }
    const cacheKey = actor === 'account' ? accountKey() : 'l12-custom-decks-v1'
    const selectionKey = actor === 'account' ? 'l12-selected-custom-deck:account-a' : 'l12-selected-custom-deck'
    const original = owned('所有模式待删除', undefined, 1, 'selection-original')
    const survivor = owned('其他模式保留', undefined, 1, 'selection-survivor')
    localStorage.setItem(cacheKey, JSON.stringify({ [original.name]: original, [survivor.name]: survivor }))
    const scopes = decksModule.L12_DECK_SELECTION_SCOPES
    for (const [index, scope] of scopes.entries()) {
      decksModule.saveSelectedDeckName(scope, index % 2 ? survivor.name : original.name)
    }
    localStorage.setItem(selectionKey, original.id)
    const otherOwnerKey = 'l12-selected-custom-deck:account-b:ranked'
    localStorage.setItem(otherOwnerKey, 'other-owner-id')
    const pointers = new Map([selectionKey, ...scopes.map(scope => `${selectionKey}:${scope}`)]
      .map(key => [key, localStorage.getItem(key)]))
    requestHandler = async (path, init = {}) => {
      assert.equal(path, '/api/decks/by-id/selection-original?expectedRevision=1')
      assert.equal(init.method, 'DELETE')
      return {}
    }
    await decksModule.deleteDeck(original)
    const afterDelete = decksModule.loadSavedDecks()
    for (const [index, scope] of scopes.entries()) {
      assert.equal(decksModule.loadSelectedDeckName(scope, afterDelete), index % 2 ? survivor.name : '',
        `${actor}/${scope}: only deleted references become unresolved`)
    }
    for (const [key, raw] of pointers) assert.equal(localStorage.getItem(key), raw,
      `${actor}: selection keys must not be separately rewritten`)
    assert.equal(localStorage.getItem(otherOwnerKey), 'other-owner-id')
    requestHandler = async (path, init = {}) => {
      assert.equal(path, '/api/decks'); assert.equal(init.method, 'POST')
      return owned(original.name, undefined, 1, 'selection-replacement')
    }
    await decksModule.saveDeck(deck(original.name))
    const afterReplacement = decksModule.loadSavedDecks()
    for (const [index, scope] of scopes.entries()) assert.equal(
      decksModule.loadSelectedDeckName(scope, afterReplacement), index % 2 ? survivor.name : '',
      `${actor}/${scope}: reusing a deleted name must not revive its old selection`)
    decksModule.saveSelectedDeckName('ranked', original.name)
    assert.equal(decksModule.loadSelectedDeckName('ranked', afterReplacement), original.name)
    for (const [index, scope] of scopes.entries()) {
      if (scope === 'ranked') continue
      assert.equal(decksModule.loadSelectedDeckName(scope, afterReplacement), index % 2 ? survivor.name : '',
        `${actor}/${scope}: explicit ranked selection cannot change another mode`)
    }
    assert.equal(localStorage.getItem(otherOwnerKey), 'other-owner-id')
  }
}

async function saveWaitsForServerAndPreservesFailure() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([deck('原牌库')])
  localStorage.setItem('l12-selected-custom-deck:account-a', 'id-原牌库')
  localStorage.setItem('l12-selected-custom-deck:account-a:ranked', 'id-原牌库')
  const pending = deferred()
  requestHandler = () => pending.promise
  const saving = decksModule.saveDeck(deck('新牌库', '2026-09-09T01:00:00.000Z'))
  assert.ok(saving instanceof Promise, '保存 API 必须可等待')
  assert.equal(readAccountDecks()['新牌库'], undefined, '服务器响应前不得宣告本地保存成功')
  pending.resolve(owned('新牌库', '2026-09-09T01:00:01.000Z'))
  await saving
  assert.equal(readAccountDecks()['新牌库'].updatedAt, '2026-09-09T01:00:01.000Z', '缓存应采用服务器确认的版本')
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a'), 'id-原牌库', '新建或复制不得改动旧版全局选择')
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a:ranked'), 'id-原牌库', '新建或复制不得暗改排位选择')

  requestHandler = async () => { throw new Error('simulated PUT 500') }
  await assert.rejects(decksModule.saveDeck(deck('失败牌库')), /simulated PUT 500/)
  assert.equal(readAccountDecks()['失败牌库'], undefined, 'PUT 失败不得留下只在本地的假成功牌库')
  assert.ok(readAccountDecks()['原牌库'])
}

async function staleSyncResponsesCannotOverwriteNewerState() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([deck('删除目标')])
  const staleDeleteSync = deferred()
  requestHandler = () => staleDeleteSync.promise
  const syncingBeforeDelete = decksModule.syncSavedDecksFromAccount()
  requestHandler = async (_path, init = {}) => {
    assert.equal(init.method, 'DELETE')
    return {}
  }
  await decksModule.deleteDeck('删除目标')
  staleDeleteSync.resolve([deck('删除目标')])
  await syncingBeforeDelete
  assert.equal(readAccountDecks()['删除目标'], undefined, 'DELETE 成功后，较早 GET 的迟到响应不得恢复缓存')

  seedAccountDecks([deck('保存目标', '2026-09-09T00:00:00.000Z')])
  const staleSaveSync = deferred()
  requestHandler = () => staleSaveSync.promise
  const syncingBeforeSave = decksModule.syncSavedDecksFromAccount()
  requestHandler = async (_path, init = {}) => {
    assert.equal(init.method, 'PUT')
    return owned('保存目标', '2026-09-09T03:00:00.000Z', 2)
  }
  await decksModule.saveDeck({ ...readAccountDecks()['保存目标'], updatedAt: '2026-09-09T02:00:00.000Z' })
  staleSaveSync.resolve([deck('保存目标', '2026-09-09T00:00:00.000Z')])
  await syncingBeforeSave
  assert.equal(readAccountDecks()['保存目标'].updatedAt, '2026-09-09T03:00:00.000Z', 'PUT 成功后，较早 GET 不得覆盖确认版本')

  seedAccountDecks([deck('账号甲')], 'account-a')
  seedAccountDecks([deck('账号乙')], 'account-b')
  const oldAccountSync = deferred()
  requestHandler = () => oldAccountSync.promise
  const syncingOldAccount = decksModule.syncSavedDecksFromAccount()
  platformState.account = { id: 'account-b' }
  platformState.token = 'token-b'
  oldAccountSync.resolve([deck('账号甲远端')])
  const visible = await syncingOldAccount
  assert.deepEqual(Object.keys(visible), ['账号乙'], '切换账号后，旧账号 GET 结果不得返回给当前页面')
}

async function guestPresetSeedingCannotCrossAnAccountSwitch() {
  localStorage.clear()
  platformState.account = null
  platformState.token = ''
  seedAccountDecks([deck('账号乙')], 'account-b')
  const presetResponses = [deferred(), deferred()]
  let fetchIndex = 0
  globalThis.fetch = () => presetResponses[fetchIndex++].promise
  const ensuring = decksModule.ensureOfficialPrebuiltDecks()
  await Promise.resolve()
  await Promise.resolve()
  assert.equal(fetchIndex, 2, 'guest preset loading should be in flight')
  platformState.account = { id: 'account-b' }
  platformState.token = 'token-b'
  presetResponses[0].resolve({ ok: true, json: async () => [deck('游客官方预组')] })
  presetResponses[1].resolve({ ok: true, json: async () => [] })
  const visible = await ensuring
  assert.deepEqual(Object.keys(visible), ['账号乙'])
  assert.deepEqual(Object.keys(readAccountDecks('account-b')), ['账号乙'], '游客预组异步结果不得写入刚登录的账号缓存')
}

async function stableIdentityRenameAndSelectionMigration() {
  localStorage.clear()
  authenticate()
  const original = owned('原名')
  seedAccountDecks([original])
  localStorage.setItem('l12-selected-custom-deck:account-a:ranked', original.id)
  requestHandler = async (path, init = {}) => {
    assert.equal(path, '/api/decks/by-id/id-%E5%8E%9F%E5%90%8D')
    assert.equal(init.method, 'PUT')
    assert.equal(JSON.parse(init.body).expectedRevision, 1)
    return owned('新名', '2026-09-09T04:00:00.000Z', 2, original.id)
  }
  await decksModule.saveDeck({ ...original, name: '新名' })
  assert.deepEqual(Object.keys(readAccountDecks()), ['新名'], '原名不得留下第二副牌库')
  assert.equal(readAccountDecks()['新名'].id, original.id)
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a:ranked'), original.id)
  assert.equal(decksModule.loadSelectedDeckName('ranked', decksModule.loadSavedDecks()), '新名')

  requestHandler = async () => { throw new Error('旧版本冲突') }
  await assert.rejects(decksModule.saveDeck({ ...readAccountDecks()['新名'], name: '再次改名' }), /旧版本冲突/)
  assert.deepEqual(Object.keys(readAccountDecks()), ['新名'], '冲突不得篡改已确认缓存')

  const copy = { ...readAccountDecks()['新名'], id: undefined, revision: undefined, name: '副本' }
  requestHandler = async (path, init = {}) => {
    assert.equal(path, '/api/decks')
    assert.equal(init.method, 'POST')
    assert.equal(JSON.parse(init.body).id, undefined)
    return owned('副本', undefined, 1, 'id-copy')
  }
  await decksModule.saveDeck(copy)
  assert.equal(readAccountDecks()['新名'].id, original.id)
  assert.equal(readAccountDecks()['副本'].id, 'id-copy')

  localStorage.clear()
  seedAccountDecks([deck('旧名')])
  localStorage.setItem(accountKey(), JSON.stringify({ 旧名: deck('旧名') }))
  localStorage.setItem('l12-selected-custom-deck:account-a:casual', '旧名')
  requestHandler = async () => [owned('旧名')]
  const migrated = await decksModule.syncSavedDecksFromAccount()
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a:casual'), '旧名', 'Migration must not perform separate selection-key writes')
  assert.equal(decksModule.loadSelectedDeckName('casual', migrated), '旧名')
  requestHandler = async () => [owned('远端改名', undefined, 2, 'id-旧名')]
  const renamed = await decksModule.syncSavedDecksFromAccount()
  assert.equal(decksModule.loadSelectedDeckName('casual', renamed), '远端改名')
}

function editorAwaitsAtomicMutation() {
  const saveBlock = editorSource.match(/async function onSave\(\)[\s\S]*?\n}\n\nasync function onSaveAs/)
  assert.ok(saveBlock, '编辑器保存入口必须是异步事务')
  assert.match(saveBlock[0], /await saveDeck\(/, '必须等待权威保存结果')
  assert.doesNotMatch(saveBlock[0], /deleteDeck\(/, '改名不得再另存后删除旧牌库')
  assert.match(editorSource, /activeDeckId\.value/, '编辑器必须保留稳定身份')
  assert.match(saveBlock[0], /catch\s*\(/, '保存或改名失败必须进入显式错误提示')

  const deleteBlock = editorSource.match(/async function confirmDelete\(\)[\s\S]*?\n}/)
  assert.ok(deleteBlock, '编辑器删除入口必须是异步事务')
  assert.match(deleteBlock[0], /await deleteDeck\(/)
  assert.match(deleteBlock[0], /catch\s*\(/, '删除失败必须显示错误且不能关闭为成功')
}

function everyUiSaveCallAwaitsConfirmation() {
  const callers = [
    '../src/l12/L12DeckEditor.vue',
    '../src/l12/site/DeckLibraryPage.vue',
    '../src/l12/site/AdminMatchesPanel.vue',
  ]
  for (const relativePath of callers) {
    const source = readFileSync(new URL(relativePath, import.meta.url), 'utf8').replaceAll('\r\n', '\n')
    const calls = source.split('\n').filter(line => line.includes('saveDeck(') && !line.includes('function saveDeck('))
    for (const line of calls) assert.match(line, /await saveDeck\(/, `${relativePath} 存在未等待的牌库保存：${line.trim()}`)
  }
}

function changedDeckComponentsRemainSyntacticallyValid() {
  const callers = [
    '../src/l12/L12DeckEditor.vue',
    '../src/l12/site/DeckLibraryPage.vue',
    '../src/l12/site/AdminMatchesPanel.vue',
  ]
  for (const relativePath of callers) {
    const filename = new URL(relativePath, import.meta.url).pathname
    const source = readFileSync(new URL(relativePath, import.meta.url), 'utf8')
    const parsed = parse(source, { filename })
    assert.deepEqual(parsed.errors, [], `${relativePath} SFC parse errors`)
    compileScript(parsed.descriptor, { id: relativePath })
    const template = compileTemplate({
      source: parsed.descriptor.template.content,
      filename,
      id: relativePath,
      compilerOptions: { expressionPlugins: ['typescript'] },
    })
    assert.deepEqual(template.errors, [], `${relativePath} template compile errors`)
  }
}

async function staleGuestSaveCannotOverwriteOrResurrect() {
  localStorage.clear()
  platformState.account = null
  platformState.token = ''
  const original = await decksModule.saveDeck(deck('双标签游客'))
  const latest = await decksModule.saveDeck({ ...original, name: '标签A的新名' })
  await assert.rejects(decksModule.saveDeck(original), /更新|变化/, '标签B的旧版本必须被拒绝')
  assert.equal(decksModule.loadSavedDecks()['标签A的新名'].revision, latest.revision)
  await decksModule.deleteDeck(latest)
  await assert.rejects(decksModule.saveDeck(original), /更新|变化/, '旧标签不得复活已删除牌库')
  const a = await decksModule.saveDeck(deck('Deck-A'))
  const b = await decksModule.saveDeck(deck('Deck-B'))
  await assert.rejects(decksModule.saveDeck({ ...a, name: 'deck-b' }), /同名/, '改名不得删除另一稳定ID')
  await assert.rejects(decksModule.saveDeck(deck('deck-b')), /更新|同名/, '大小写变体新建不得覆盖原对象')
  await assert.rejects(decksModule.saveDeck({ ...b, revision: undefined }), /更新/, '不得用缺失的版本号更新')
  assert.deepEqual(Object.values(decksModule.loadSavedDecks()).map(value => value.id).sort(), [a.id, b.id].sort())
}

async function guestDeleteAndSaveShareLock() {
  localStorage.clear()
  platformState.account = null
  platformState.token = ''
  const original = await decksModule.saveDeck(deck('并发删除'))
  const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'navigator')
  const gate = deferred(), names = []
  let tail = gate.promise
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { locks: {
    request(name, callback) {
      names.push(name)
      const result = tail.then(callback)
      tail = result.catch(() => undefined)
      return result
    },
  } } })
  try {
    const deleting = decksModule.deleteDeck(original)
    const saving = decksModule.saveDeck(original)
    const rejected = assert.rejects(saving, /变化|更新/)
    assert.equal(Object.keys(decksModule.loadSavedDecks()).length, 1, '锁持有时删除必须等待')
    assert.equal(names.length, 2)
    assert.equal(names[0], names[1], '删除和保存必须使用同一把同源锁')
    gate.resolve()
    await deleting
    await rejected
    assert.deepEqual(decksModule.loadSavedDecks(), {})
  } finally {
    if (descriptor) Object.defineProperty(globalThis, 'navigator', descriptor)
    else delete globalThis.navigator
  }
}

async function guestPresetIdentityMigration() {
  localStorage.clear()
  platformState.account = null
  platformState.token = ''
  globalThis.fetch = async () => ({ ok: true, json: async () => [deck('默认预组')] })
  const first = await decksModule.ensureOfficialPrebuiltDecks()
  assert.ok(first['默认预组'].id)
  assert.equal(first['默认预组'].revision, 1)
  await decksModule.deleteDeck(first['默认预组'])
  assert.deepEqual(await decksModule.ensureOfficialPrebuiltDecks(), {}, '删除过的预组不得重新播种')
  localStorage.setItem('l12-custom-decks-v1', JSON.stringify({ 旧游客牌库: deck('旧游客牌库') }))
  localStorage.setItem('l12-selected-custom-deck', '旧游客牌库')
  for (const scope of decksModule.L12_DECK_SELECTION_SCOPES) localStorage.setItem(`l12-selected-custom-deck:${scope}`, '旧游客牌库')
  const migrated = await decksModule.ensureOfficialPrebuiltDecks()
  assert.ok(migrated['旧游客牌库'].id)
  assert.equal(migrated['旧游客牌库'].revision, 1)
  assert.equal(localStorage.getItem('l12-selected-custom-deck'), '旧游客牌库', 'Guest migration commits interpretation in the cache, never another key')
  for (const scope of decksModule.L12_DECK_SELECTION_SCOPES) {
    assert.equal(decksModule.loadSelectedDeckName(scope, migrated), '旧游客牌库')
    assert.equal(localStorage.getItem(`l12-selected-custom-deck:${scope}`), '旧游客牌库', 'All physical selection keys remain byte-for-byte')
  }
  assert.equal((await decksModule.ensureOfficialPrebuiltDecks())['旧游客牌库'].id, migrated['旧游客牌库'].id)
}

await staleGuestSaveCannotOverwriteOrResurrect()
await guestDeleteAndSaveShareLock()
await guestPresetIdentityMigration()
await authenticatedSyncUsesServerAuthority()
authenticatedLoadDoesNotImplicitlyMigrateGuestCache()
await deleteWaitsForServerAndPreservesFailure()
await deleteUsesSelectedIdentityAndRejectsStaleRevision()
await allSelectionScopesRemainIsolatedAfterAtomicDeletion()
await saveWaitsForServerAndPreservesFailure()
await staleSyncResponsesCannotOverwriteNewerState()
await guestPresetSeedingCannotCrossAnAccountSwitch()
await stableIdentityRenameAndSelectionMigration()
editorAwaitsAtomicMutation()
everyUiSaveCallAwaitsConfirmation()
changedDeckComponentsRemainSyntacticallyValid()
async function accountLateMutationCannotCrossIdentity() {
  for (const operation of ['save', 'delete']) {
    localStorage.clear(); authenticate()
    seedAccountDecks([owned('原牌库')])
    const beforeA = localStorage.getItem(accountKey())
    const pending = deferred(); requestHandler = () => pending.promise
    const task = operation === 'save' ? decksModule.saveDeck(deck('迟到保存')) : decksModule.deleteDeck('原牌库')
    const captured = task.then(() => { throw Error('Late mutation unexpectedly succeeded') }, error => error)
    platformState.account = { id: 'account-b' }; platformState.token = 'token-b'
    seedAccountDecks([owned('乙牌库')], 'account-b')
    const beforeB = localStorage.getItem(accountKey('account-b'))
    pending.resolve(operation === 'save' ? owned('迟到保存') : {})
    const error = await captured
    assert.match(error.message, /服务器已确认.*账号已切换/)
    assert.equal(error.serverConfirmed, true)
    assert.equal(decksModule.deckErrorBelongsToCurrentAccount(error), false)
    assert.equal(localStorage.getItem(accountKey()), beforeA)
    assert.equal(localStorage.getItem(accountKey('account-b')), beforeB)
  }
}
async function confirmedSaveQuotaPreservesOriginalAndDraft() {
  localStorage.clear(); authenticate(); seedAccountDecks([owned('原牌库')])
  localStorage.setItem('l12-selected-custom-deck:account-a:ranked', 'id-原牌库')
  localStorage.setItem('l12:deck-editor-draft:v1:account%3Aaccount-a', '本机独有草稿')
  const before = localStorage.getItem(accountKey())
  const set = localStorage.setItem.bind(localStorage)
  localStorage.setItem = (key, value) => { if (key === accountKey()) throw Error('QuotaExceededError'); set(key, value) }
  let requests = 0
  requestHandler = async () => { requests++; return owned('云端已保存') }
  try {
    await assert.rejects(decksModule.saveDeck(deck('云端已保存')), error => {
      assert.equal(error.serverConfirmed, true)
      assert.match(error.message, /服务器已确认.*缓存写入失败.*Quota/)
      return true
    })
  } finally { localStorage.setItem = set }
  assert.equal(requests, 1, 'Local failure must not automatically repeat POST')
  assert.equal(localStorage.getItem(accountKey()), before)
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a:ranked'), 'id-原牌库')
  assert.equal(localStorage.getItem('l12:deck-editor-draft:v1:account%3Aaccount-a'), '本机独有草稿')
}
async function lateSameAccountAckCannotResurrectOrDowngrade() {
  localStorage.clear(); authenticate(); seedAccountDecks([owned('同ID')])
  const original = readAccountDecks()['同ID']
  const pending = deferred()
  requestHandler = (_path, init) => init.method === 'PUT' ? pending.promise : Promise.resolve({})
  const saving = decksModule.saveDeck(original)
  const captured = saving.then(() => { throw Error('Old save unexpectedly won') }, error => error)
  await decksModule.deleteDeck(original)
  const afterDelete = localStorage.getItem(accountKey())
  pending.resolve(owned('同ID', undefined, 2, original.id))
  assert.match((await captured).message, /服务器已确认.*迟到/)
  assert.equal(localStorage.getItem(accountKey()), afterDelete)
  assert.deepEqual(readAccountDecks(), {})

  seedAccountDecks([owned('同ID', undefined, 4, original.id)])
  const before = localStorage.getItem(accountKey())
  requestHandler = async () => owned('同ID', undefined, 3, original.id)
  await assert.rejects(decksModule.saveDeck(readAccountDecks()['同ID']), /低修订/)
  assert.equal(localStorage.getItem(accountKey()), before)
}
async function cacheUnavailableCannotSeedSaveOrDelete() {
  localStorage.clear(); platformState.account = null; platformState.token = ''
  const raw = JSON.stringify({ good: deck('good'), bad: { ...deck('bad'), futureField: true } })
  localStorage.setItem('l12-custom-decks-v1', raw)
  const snapshot = decksModule.loadSavedDecksState()
  assert.equal(snapshot.status, 'unavailable'); assert.equal(snapshot.decks, null)
  assert.throws(() => decksModule.loadSavedDecks(), /缓存/)
  let requests = 0; requestHandler = async () => { requests++; throw Error('Unexpected API') }
  const fetch = globalThis.fetch; globalThis.fetch = async () => { requests++; throw Error('Unexpected fetch') }
  try {
    await assert.rejects(decksModule.ensureOfficialPrebuiltDecks(), /缓存/)
    await assert.rejects(decksModule.saveDeck(deck('新牌库')), /缓存/)
    await assert.rejects(decksModule.deleteDeck('good'), /缓存/)
  } finally { globalThis.fetch = fetch }
  assert.equal(requests, 0); assert.equal(localStorage.getItem('l12-custom-decks-v1'), raw)
}
async function exactMetadataAndMigrationQuota() {
  localStorage.clear(); platformState.account = null; platformState.token = ''
  const full = { ...owned(' 无损元数据 ', undefined, 4), masterId: ' s01-01M1 ',
    cardIds: ['B', 'A', 'B'], moraleIds: [' r ', 'R'], specialIds: ['✨', ''], benchIds: ['B', 'A', 'B'],
    publicationId: null, publicationVersion: null, alternateArtSelections: { A: ' art ' }, alternateArtCopies: { A: ['', 'art', ''] } }
  const raw = JSON.stringify({ [full.name]: full })
  localStorage.setItem('l12-custom-decks-v1', raw)
  localStorage.setItem('l12-selected-custom-deck:friendly', full.name)
  const set = localStorage.setItem.bind(localStorage)
  localStorage.setItem = (key, value) => { if (key === 'l12-custom-decks-v1') throw Error('QuotaExceeded'); set(key, value) }
  try { await assert.rejects(decksModule.ensureOfficialPrebuiltDecks(), /写入失败/) }
  finally { localStorage.setItem = set }
  assert.equal(localStorage.getItem('l12-custom-decks-v1'), raw)
  assert.equal(localStorage.getItem('l12-selected-custom-deck:friendly'), full.name)
  const migrated = await decksModule.ensureOfficialPrebuiltDecks()
  assert.deepEqual(migrated[full.name], full)
  assert.equal(decksModule.loadSelectedDeckName('friendly', migrated), full.name)
  assert.equal(localStorage.getItem('l12-selected-custom-deck:friendly'), full.name)
  const persisted = localStorage.getItem('l12-custom-decks-v1')
  await decksModule.ensureOfficialPrebuiltDecks()
  assert.equal(localStorage.getItem('l12-custom-decks-v1'), persisted, 'One-time migration does not write again')
}
async function specialDictionaryNamesStayData() {
  localStorage.clear(); platformState.account = null; platformState.token = ''
  const saved = await decksModule.saveDeck(deck('__proto__'))
  assert.equal(Object.getPrototypeOf(decksModule.loadSavedDecks()), Object.prototype)
  assert.equal(Object.hasOwn(decksModule.loadSavedDecks(), '__proto__'), true)
  assert.equal(decksModule.loadSavedDecks()['__proto__'].id, saved.id)
}
function unavailableReadConsumersRemainVisible() {
  for (const relative of ['../src/l12/LobbyPage.vue', '../src/l12/site/BattleHubPage.vue']) {
    const text = readFileSync(new URL(relative, import.meta.url), 'utf8')
    const start = text.indexOf('function visibleDecks('), end = text.indexOf('const customDecks', start)
    const javascript = ts.transpileModule(text.slice(start, end), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
    const notice = { notice: '' }
    const run = new Function('loadSavedDecksState', 'l12State', javascript + '; return visibleDecks()')
    const result = run(() => ({ status: 'unavailable', decks: null, error: Error('本机牌库不可用') }), notice)
    assert.deepEqual(result, {}); assert.equal(notice.notice, '本机牌库不可用', 'Unavailable UI collection has an explicit visible error')
  }
  const home = readFileSync(new URL('../src/l12/site/HomeRevisitActions.vue', import.meta.url), 'utf8')
  const start = home.indexOf('function refreshRecent()'), end = home.indexOf('function refreshWhenVisible()', start)
  const run = new Function('verifiedAccount', 'recentName', 'cacheError', 'loadSavedDecksState', 'recentHomeDeckName', home.slice(start, end) + '; refreshRecent()')
  const recentName = { value: '旧资料' }, cacheError = { value: '' }
  run({ value: true }, recentName, cacheError, () => ({ status: 'unavailable', decks: null, error: Error('读取失败') }), () => { throw Error('Must not interpret unavailable as empty') })
  assert.equal(recentName.value, ''); assert.equal(cacheError.value, '读取失败')
}

await accountLateMutationCannotCrossIdentity()
await confirmedSaveQuotaPreservesOriginalAndDraft()
await lateSameAccountAckCannotResurrectOrDowngrade()
await cacheUnavailableCannotSeedSaveOrDelete()
await exactMetadataAndMigrationQuota()
await specialDictionaryNamesStayData()
unavailableReadConsumersRemainVisible()
function actualScript(relative) {
  const text = parse(readFileSync(new URL(relative, import.meta.url), 'utf8')).descriptor.scriptSetup.content
  return ts.createSourceFile(relative, text, ts.ScriptTarget.ES2022, true, ts.ScriptKind.TS)
}
function actualHandler(relative, name, environment) {
  const script = actualScript(relative)
  const declaration = script.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
  assert.ok(declaration, `Production handler ${name} exists`)
  const code = ts.transpileModule(declaration.getText(script), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(environment), code + '; return ' + name)(...Object.values(environment))
}
async function queuedPublicCopyUsesOriginalAuthenticationGeneration() {
  for (const change of ['account', 'token', 'aba', 'unchanged']) {
    localStorage.clear(); authenticate(); seedAccountDecks([owned('原始牌库')])
    const beforeA = localStorage.getItem(accountKey())
    const gate = deferred(); let callback, posts = 0, copies = 0, actionActive = false
    const entry = { value: { id: 'public-qa', publicCode: '23456789ABCD', ownerId: 'public-author', official: false, deck: deck('公开复制') } }
    const originalBody = entry.value.deck
    const notice = { value: '' }
    requestHandler = async () => { posts++; return owned('公开复制') }
    const captureCounterContext = actualHandler('../src/l12/site/PublicDeckDetailPage.vue', 'captureCounterContext', {
      capturePublicDeckCounterGuard: publicDeckCounters.capturePublicDeckCounterGuard,
      captureDeckAccountGuard: decksModule.captureDeckAccountGuard,
      counterDocumentEpoch: 0, route: { fullPath: '/decks/23456789ABCD' }, entry,
      actionPending: () => actionActive,
    })
    const handler = actualHandler('../src/l12/site/PublicDeckDetailPage.vue', 'copyToMine', {
      entry, notice, platformState,
      captureDeckAccountGuard: decksModule.captureDeckAccountGuard,
      captureCounterContext,
      isOfficialPublicDeckCounterTarget: publicDeckCounters.isOfficialPublicDeckCounterTarget,
      mergePublicDeckCounters: publicDeckCounters.mergePublicDeckCounters,
      publicDeckActionKey: () => 'public-qa-action',
      runAction: (_key, work) => { actionActive = true; callback = work; return gate.promise },
      uniqueName: value => value, saveDeck: decksModule.saveDeck,
      publicDeckApi: { counter: async (reference, kind) => {
        assert.equal(reference, entry.value.publicCode); assert.equal(kind, 'copy'); copies++
        return { id: entry.value.id, publicCode: entry.value.publicCode, views: 0, likes: 0,
          copies, viewerLiked: false, canEdit: false }
      } },
      publicDeckRouteReference: value => value.publicCode,
      deckErrorBelongsToCurrentAccount: decksModule.deckErrorBelongsToCurrentAccount,
    })
    const running = handler()
    assert.equal(typeof callback, 'function')
    if (change === 'account' || change === 'aba') { platformState.account = { id: 'account-b' }; platformState.token = 'token-b' }
    if (change === 'token') platformState.token = 'renewed-a'
    if (change === 'aba') { platformState.account = { id: 'account-a' }; platformState.token = 'token-a' }
    await callback(); actionActive = false; gate.resolve(); await running
    assert.strictEqual(entry.value.deck, originalBody, 'Counter completion preserves the current body reference')
    if (change === 'unchanged') {
      assert.equal(posts, 1); assert.equal(copies, 1); assert.match(notice.value, /已复制/)
    } else {
      assert.equal(posts, 0, 'Queued copy must not issue POST for a successor authentication generation')
      assert.equal(copies, 0); assert.equal(notice.value, '')
      assert.equal(localStorage.getItem(accountKey()), beforeA)
    }
  }
}
async function authenticationABARejectsRealLateSaveAndDelete() {
  for (const action of ['save', 'delete']) {
    localStorage.clear(); authenticate(); seedAccountDecks([owned('ABA原文')])
    const before = localStorage.getItem(accountKey()), pending = deferred()
    requestHandler = () => pending.promise
    const operation = action === 'save' ? decksModule.saveDeck(deck('ABA迟到')) : decksModule.deleteDeck('ABA原文')
    const caught = operation.then(() => { throw Error('Authentication ABA was accepted') }, error => error)
    platformState.account = { id: 'account-b' }; platformState.token = 'token-b'
    platformState.account = { id: 'account-a' }; platformState.token = 'token-a'
    pending.resolve(action === 'save' ? owned('ABA迟到') : {})
    const error = await caught
    assert.match(error.message, /服务器已确认.*账号已切换/)
    assert.equal(decksModule.deckErrorBelongsToCurrentAccount(error), false)
    assert.equal(localStorage.getItem(accountKey()), before)
  }
}
function confirmedEditorCatchUsesAccurateStage() {
  const script = actualScript('../src/l12/L12DeckEditor.vue')
  for (const name of ['onSave', 'onSaveAs', 'confirmDelete']) {
    const method = script.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
    const attempt = method.body.statements.find(ts.isTryStatement)
    const body = ts.transpileModule(attempt.catchClause.block.getText(script), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
    const run = new Function('error', 'context', 'notice', 'isCurrentEditorContext', 'deckErrorBelongsToCurrentAccount', 'mutationError', 'deck', body)
    const confirmed = Object.assign(Error('服务器已确认本次操作；本机缓存未更新'), { serverConfirmed: true })
    const notice = { value: '' }
    run(confirmed, {}, notice, () => true, () => true, error => error.message, { name: '具名牌库' })
    assert.equal(notice.value, confirmed.message, 'A confirmed cloud operation is not described as a failed save/delete')
    run(Error('模拟未确认失败'), {}, notice, () => true, () => true, error => error.message, { name: '具名牌库' })
    assert.match(notice.value, /失败.*模拟未确认失败/, 'Unconfirmed failure keeps the existing failure presentation')
    notice.value = '新身份提示'
    run(confirmed, {}, notice, () => true, () => false, error => error.message, { name: '具名牌库' })
    assert.equal(notice.value, '新身份提示')
  }
}
async function unavailablePrivateCacheDoesNotBlockPublicLibrary() {
  localStorage.clear(); platformState.account = null; platformState.token = ''
  const raw = JSON.stringify({ bad: { ...deck('bad'), unsupported: true } })
  localStorage.setItem('l12-custom-decks-v1', raw)
  let posts = 0; requestHandler = async () => { posts++; throw Error('Must not issue a private API operation') }
  const script = actualScript('../src/l12/site/DeckLibraryPage.vue')
  const mounted = script.statements.find(item => ts.isExpressionStatement(item) && ts.isCallExpression(item.expression)
    && item.expression.expression.getText(script) === 'onMounted')
  const callback = mounted.expression.arguments[0]
  const code = ts.transpileModule('const initialize = ' + callback.getText(script), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  const catalog = { value: [] }, saved = { value: [] }, published = { value: [] }, notice = { value: '' }, arts = { value: [] }, policy = { value: null }
  const environment = {
    restoreFiltersFromRoute: () => {}, libraryContext: () => ({}), libraryContextCurrent: () => true,
    catalog, saved, published, notice, ownedAlternateArts: arts, operationsPolicy: policy, platformState,
    loadDeckCatalog: async () => ['PUBLIC-CATALOG'], ensureOfficialPrebuiltDecks: decksModule.ensureOfficialPrebuiltDecks,
    alternateArtApi: { mine: async () => [] },
    loadOfficialPresetDecks: async () => [deck('公开预组')],
    publicDeckApi: { list: async () => [{ id: 'public-card', deck: deck('公开牌库') }] },
    getEffectiveOperationsPolicy: async () => ({ defaultPresetDeckIds: [] }),
    deckErrorBelongsToCurrentAccount: decksModule.deckErrorBelongsToCurrentAccount,
    nextTick: async () => {}, sessionStorage: { getItem: () => null }, route: { fullPath: '/decks' },
  }
  const initialize = new Function(...Object.keys(environment), code + '; return initialize')(...Object.values(environment))
  await initialize()
  assert.deepEqual(catalog.value, ['PUBLIC-CATALOG'])
  assert.equal(published.value.length, 2); assert.equal(published.value[1].id, 'public-card')
  assert.match(notice.value, /缓存/); assert.deepEqual(saved.value, {})
  assert.equal(decksModule.loadSavedDecksState().status, 'unavailable')
  assert.equal(localStorage.getItem('l12-custom-decks-v1'), raw); assert.equal(posts, 0)
}

await queuedPublicCopyUsesOriginalAuthenticationGeneration()
await authenticationABARejectsRealLateSaveAndDelete()
confirmedEditorCatchUsesAccurateStage()
await unavailablePrivateCacheDoesNotBlockPublicLibrary()
console.log('Deck authority/migration/Quota/unavailable, real queued public copies, authentication ABA, editor stage and public-init isolation passed')
