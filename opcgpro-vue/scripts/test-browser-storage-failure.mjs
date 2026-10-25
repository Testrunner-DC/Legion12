import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import { reactive } from 'vue'

const read = name => fs.readFileSync(new URL(`../src/l12/${name}`, import.meta.url), 'utf8')
const portSource = read('browserStorage.ts')
const platformSource = read('platform.ts')
const netSource = read('net.ts')
const compile = source => ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
function select(source, names) {
  const tree = ts.createSourceFile('actual.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(tree.parseDiagnostics.length, 0)
  return tree.statements.filter(node => {
    if (ts.isImportDeclaration(node)) return node.moduleSpecifier.text === './browserStorage'
    if (ts.isFunctionDeclaration(node)) return names.includes(node.name?.text)
    return ts.isVariableStatement(node) && node.declarationList.declarations.some(item => names.includes(item.name.getText(tree)))
  }).map(node => node.getText(tree)).join('\n').replaceAll('import.meta.env.VITE_WS_URL', "''")
}
const platform = compile(select(platformSource, ['loadAccount', 'platformState', 'authState',
  'remember', 'forgetAccount', 'platformSessionVersion', 'authRefreshPromise']))
const net = compile(select(netSource, ['normalizeEndpoint', 'configuredEndpoint', 'defaultEndpoint',
  'storedEndpoint', 'initialEndpoint', 'l12State']))
function runtime({ prefix = '', getterDenied = false, readDenied = false, quotaKey = '', removeDenied = false,
  denyAfterQuota = false, initial = new Map() } = {}) {
  const values = new Map(initial)
  const fault = { readDenied, quotaKey, removeDenied, denyAllWrites: false }
  const storage = {
    getItem(key) { if (fault.readDenied) throw Error('synthetic read denied'); return values.get(key) ?? null },
    setItem(key, value) {
      if (key === fault.quotaKey || fault.denyAllWrites) {
        if (denyAfterQuota) fault.denyAllWrites = true
        throw Error('synthetic quota')
      }
      values.set(key, String(value))
    },
    removeItem(key) { if (fault.removeDenied || fault.denyAllWrites) throw Error('synthetic removal denied'); values.delete(key) },
  }
  const context = vm.createContext({ reactive, location: { protocol: 'https:', host: 'synthetic.invalid', hostname: 'synthetic.invalid' },
    deploymentStorageKey: key => prefix + key, deploymentWebSocketPath: () => '/ws',
    clearAuthRefreshRetry() {}, disconnect() { context.disconnects++ }, disconnects: 0 })
  Object.defineProperty(context, 'localStorage', { configurable: true, get() {
    if (getterDenied) throw Error('synthetic getter denied')
    return storage
  } })
  const evaluate = (code, suffix) => vm.runInContext(`(function(require){const exports={};${code}\n${suffix}\n})(require)`, context)
  context.require = () => { throw Error('unexpected dependency') }
  const port = evaluate(compile(portSource), 'return exports')
  context.require = name => { assert.equal(name, './browserStorage'); return port }
  const state = evaluate(net, 'return exports.l12State')
  context.l12State = state
  const auth = evaluate(platform, 'return { platformState:exports.platformState, authState:exports.authState, remember, forgetAccount }')
  return { ...auth, net: state, values, port, context, storage, fault }
}

const results = []
async function check(name, run) {
  try { await run(); results.push({ name, passed: true }) }
  catch (error) { results.push({ name, passed: false, error: error.stack || String(error) }) }
}
const account = id => ({ id, username: id, role: 'player' })
for (const mode of ['getterDenied', 'readDenied']) await check(`actual platform/net initialize as guest when ${mode}`, () => {
  const value = runtime({ [mode]: true })
  assert.equal(value.platformState.token, '')
  assert.equal(value.platformState.account, null)
  assert.equal(value.authState.verified, false)
  assert.equal(value.net.nickname, '')
  assert.equal(value.net.endpoint, 'wss://synthetic.invalid/ws')
})
await check('corrupt account JSON never blocks initialization or verifies an identity', () => {
  const value = runtime({ initial: new Map([['l12-account', '{broken'], ['l12-auth-token', 'synthetic-token']]) })
  assert.equal(value.platformState.account, null)
  assert.equal(value.authState.verified, false)
})
for (const quotaKey of ['l12-account', 'l12-auth-token', 'l12-nickname']) await check(`login ${quotaKey} write failure never publishes verified identity`, () => {
  const value = runtime({ quotaKey })
  let failure
  try { value.remember(account('synthetic-a'), 'synthetic-token') } catch (error) { failure = error }
  assert.equal(value.platformState.account, null)
  assert.equal(value.platformState.token, '')
  assert.equal(value.authState.verified, false)
  assert.equal(value.net.nickname, '')
  assert.equal(value.values.size, 0)
  assert.match(failure?.message || '', /无法保存登录状态/)
})
await check('a failed account switch preserves prior identity and rolls back partial writes', () => {
  const initial = new Map([['l12-account', JSON.stringify(account('synthetic-a'))],
    ['l12-auth-token', 'token-a'], ['l12-nickname', 'synthetic-a']])
  const value = runtime({ initial, quotaKey: 'l12-auth-token' })
  let failure
  try { value.remember(account('synthetic-b'), 'token-b') } catch (error) { failure = error }
  assert.equal(value.platformState.account.id, 'synthetic-a')
  assert.equal(value.platformState.token, 'token-a')
  assert.equal(value.authState.verified, false)
  assert.deepEqual([...value.values], [...initial])
  assert.match(failure?.message || '', /无法保存登录状态/)
})
await check('logout retires all live identity even when every storage removal throws', () => {
  const value = runtime({ removeDenied: true })
  value.remember(account('synthetic-a'), 'token-a')
  assert.doesNotThrow(() => value.forgetAccount())
  assert.equal(value.platformState.account, null)
  assert.equal(value.platformState.token, '')
  assert.equal(value.authState.verified, false)
  assert.equal(value.net.nickname, '')
  assert.equal(value.context.disconnects, 1)
  const reload = runtime({ initial: value.values })
  assert.equal(reload.platformState.token, '')
  assert.equal(reload.platformState.account, null)
})
for (const previousToken of ['', 'previous-token']) await check(`token commits last even when rollback is rejected (${previousToken ? 'switch' : 'first login'})`, () => {
  const initial = new Map(previousToken ? [['l12-auth-token', previousToken]] : [])
  const value = runtime({ initial, quotaKey: 'l12-nickname', denyAfterQuota: true })
  assert.throws(() => value.remember(account('synthetic-new'), 'new-token'), /无法保存登录状态/)
  assert.equal(value.values.get('l12-auth-token') || '', previousToken)
  assert.equal(value.platformState.token, previousToken)
  assert.equal(value.authState.verified, false)
})
await check('logout reports incomplete persistent retirement if both remove and fallback write fail', () => {
  const value = runtime()
  value.remember(account('synthetic-a'), 'token-a')
  value.fault.removeDenied = true
  value.fault.denyAllWrites = true
  assert.throws(() => value.forgetAccount(), /重新打开可能恢复登录/)
  assert.equal(value.platformState.token, '')
  assert.equal(value.platformState.account, null)
  assert.equal(value.authState.verified, false)
  assert.equal(value.net.nickname, '')
})
await check('guest initialization cleanup remains nonthrowing when the storage getter is denied', () => {
  const value = runtime({ getterDenied: true })
  assert.doesNotThrow(() => value.forgetAccount())
  assert.equal(value.platformState.token, '')
})
await check('healthy storage retains the same login and persisted reload', () => {
  const value = runtime()
  value.remember(account('synthetic-a'), 'token-a')
  assert.equal(value.authState.verified, true)
  const reload = runtime({ initial: value.values })
  assert.equal(reload.platformState.token, 'token-a')
  assert.equal(reload.platformState.account.id, 'synthetic-a')
  assert.equal(reload.authState.verified, false)
})
await check('mounted test environment does not inherit or overwrite formal storage', () => {
  const initial = new Map([['l12-auth-token', 'formal-token'], ['l12-account', JSON.stringify(account('formal'))]])
  const value = runtime({ prefix: 'test:', initial })
  assert.equal(value.platformState.token, '')
  value.remember(account('test'), 'test-token')
  value.forgetAccount()
  assert.equal(value.values.get('l12-auth-token'), 'formal-token')
  assert.equal(value.values.get('l12-account'), initial.get('l12-account'))
})
await check('optional reads do not become a hidden authentication or durable memory fallback', () => {
  const value = runtime()
  const port = value.port.createOptionalBrowserStorage(() => { throw Error('policy denied') })
  port.setItem('synthetic', 'not-persisted')
  assert.equal(port.getItem('synthetic'), null)
  assert.equal(value.values.size, 0)
})
await check('durable storage port never turns an unreadable private cache into a missing cache', () => {
  const value = runtime({ readDenied: true })
  assert.throws(() => value.port.persistentLocalStorage.getItem('synthetic-private-cache'), /read denied/)
})
console.log(JSON.stringify({ suite: 'browser-storage-failure', passed: results.filter(item => item.passed).length,
  failed: results.filter(item => !item.passed).length, results }, null, 2))
if (results.some(item => !item.passed)) process.exitCode = 1
