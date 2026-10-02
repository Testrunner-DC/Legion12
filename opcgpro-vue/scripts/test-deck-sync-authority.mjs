import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
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
const platformState = { account: null, token: '' }
let requestHandler = async () => { throw new Error('Unexpected platform request') }
globalThis.__deckAuthorityTestPlatform = {
  platformState,
  getEffectiveOperationsPolicy: async () => ({ defaultPresetDeckIds: [] }),
  platformRequest: (...args) => requestHandler(...args),
}

const executableSource = deckSource
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
const decksModule = await import(`data:text/javascript;base64,${Buffer.from(executableJavaScript).toString('base64')}`)

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
  return JSON.parse(localStorage.getItem(accountKey(accountId)) || '{}')
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
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a'), null)

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
  assert.equal(localStorage.getItem('l12-selected-custom-deck:account-a:casual'), 'id-旧名')
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

await authenticatedSyncUsesServerAuthority()
authenticatedLoadDoesNotImplicitlyMigrateGuestCache()
await deleteWaitsForServerAndPreservesFailure()
await deleteUsesSelectedIdentityAndRejectsStaleRevision()
await saveWaitsForServerAndPreservesFailure()
await staleSyncResponsesCannotOverwriteNewerState()
await guestPresetSeedingCannotCrossAnAccountSwitch()
await stableIdentityRenameAndSelectionMigration()
editorAwaitsAtomicMutation()
everyUiSaveCallAwaitsConfirmation()
changedDeckComponentsRemainSyntacticallyValid()
console.log('Deck server authority, atomic rename and selection migration passed')
