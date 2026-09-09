import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc'

const deckSourceUrl = new URL('../src/l12/decks.ts', import.meta.url)
const editorSourceUrl = new URL('../src/l12/L12DeckEditor.vue', import.meta.url)
const deckSource = readFileSync(deckSourceUrl, 'utf8').replaceAll('\r\n', '\n')
const editorSource = readFileSync(editorSourceUrl, 'utf8').replaceAll('\r\n', '\n')

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
  .replace("import moraleIdentityData from '../../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'", 'const moraleIdentityData = []')
  .replace("import cardProductInclusionsData from '../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'", 'const cardProductInclusionsData = { products: [], cards: [] }')
  .replace("import cardArchiveAssetsData from '../../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'", 'const cardArchiveAssetsData = { cards: [] }')
const executableJavaScript = ts.transpileModule(executableSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const decksModule = await import(`data:text/javascript;base64,${Buffer.from(executableJavaScript).toString('base64')}`)

function deck(name, updatedAt = '2026-09-09T00:00:00.000Z') {
  return { name, masterId: 'S01-01M1', cardIds: [], moraleIds: [], specialIds: [], updatedAt }
}

function accountKey(accountId = 'account-a') {
  return `l12-custom-decks-v1:${accountId}`
}

function seedAccountDecks(entries, accountId = 'account-a') {
  localStorage.setItem(accountKey(accountId), JSON.stringify(Object.fromEntries(entries.map(item => [item.name, item]))))
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
    assert.equal(path, '/api/decks/%E5%BE%85%E5%88%A0%E9%99%A4')
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

async function saveWaitsForServerAndPreservesFailure() {
  localStorage.clear()
  authenticate()
  seedAccountDecks([deck('原牌库')])
  const pending = deferred()
  requestHandler = () => pending.promise
  const saving = decksModule.saveDeck(deck('新牌库', '2026-09-09T01:00:00.000Z'))
  assert.ok(saving instanceof Promise, '保存 API 必须可等待')
  assert.equal(readAccountDecks()['新牌库'], undefined, '服务器响应前不得宣告本地保存成功')
  pending.resolve(deck('新牌库', '2026-09-09T01:00:01.000Z'))
  await saving
  assert.equal(readAccountDecks()['新牌库'].updatedAt, '2026-09-09T01:00:01.000Z', '缓存应采用服务器确认的版本')

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
    return deck('保存目标', '2026-09-09T03:00:00.000Z')
  }
  await decksModule.saveDeck(deck('保存目标', '2026-09-09T02:00:00.000Z'))
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

function editorAwaitsMutationsAndKeepsRenameOrder() {
  const saveBlock = editorSource.match(/async function onSave\(\)[\s\S]*?\n}\n\nasync function onSaveAs/)
  assert.ok(saveBlock, '编辑器保存入口必须是异步事务')
  const saveIndex = saveBlock[0].indexOf('await saveDeck(')
  const deleteIndex = saveBlock[0].indexOf('await deleteDeck(')
  assert.ok(saveIndex >= 0 && deleteIndex > saveIndex, '改名必须先等新名称保存成功，再删除旧名称')
  assert.match(saveBlock[0], /toLocaleLowerCase|localeCompare/, '大小写等价名称不得误删服务端刚更新的同一牌库')
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
await saveWaitsForServerAndPreservesFailure()
await staleSyncResponsesCannotOverwriteNewerState()
await guestPresetSeedingCannotCrossAnAccountSwitch()
editorAwaitsMutationsAndKeepsRenameOrder()
everyUiSaveCallAwaitsConfirmation()
changedDeckComponentsRemainSyntacticallyValid()
console.log('Deck server authority and mutation ordering: 9/9 passed')
