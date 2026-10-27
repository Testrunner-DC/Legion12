import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const sourcePath = new URL('../src/l12/site/AdminAccountPicker.vue', import.meta.url)
const source = fs.readFileSync(sourcePath, 'utf8')
const deferred = () => {
  let resolve, reject
  const promise = new Promise((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}
const flush = async () => { for (let index = 0; index < 6; index++) { await Promise.resolve(); await Vue.nextTick() } }
const account = (id, options = {}) => ({ id, username: options.username || `玩家-${id}`, role: 'player',
  permissionVersion: 1, disabled: Boolean(options.disabled), deleted: Boolean(options.deleted), permissions: [] })
const page = (items, deleted = false, overrides = {}) => ({ items, page: 1, pageSize: 20, total: items.length,
  activeCount: deleted ? 80 : 100, deletedCount: deleted ? items.length : 4, ...overrides })

function node(tag, text = '') { return { tag, text, children: [], parent: null, props: {} } }
function detach(child) {
  if (!child.parent) return
  const index = child.parent.children.indexOf(child)
  if (index >= 0) child.parent.children.splice(index, 1)
  child.parent = null
}
function descendants(root, tag) {
  const matches = []
  const visit = current => {
    if (current.tag === tag) matches.push(current)
    for (const child of current.children || []) visit(child)
  }
  visit(root)
  return matches
}
const renderer = Vue.createRenderer({
  createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) {
    detach(child); child.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
  },
  remove: detach, parentNode: child => child.parent,
  nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, text) => { child.text = text },
  setElementText(child, text) { child.children = []; child.text = text },
  patchProp: (child, key, previous, value) => { child.props[key] = value }, setScopeId() {},
})
function evaluate(code, imports, globals = {}) {
  const module = { exports: {} }
  const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(output, { module, exports: module.exports, require: id => {
    assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id]
  }, console, Error, ...globals })
  return module.exports
}
function compileActualPicker(imports, windowMock) {
  const { descriptor, errors } = parse(source)
  assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'admin-account-picker-page-consumer' })
  const component = evaluate(script.content, { vue: Vue, '@/l12/platform': imports }, { window: windowMock }).default
  const template = compileTemplate({ source: descriptor.template.content, filename: 'AdminAccountPicker.vue',
    id: 'admin-account-picker-page-consumer', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  component.render = evaluate(template.code, { vue: Vue }).render
  return component
}

async function fixture(modelValue = '') {
  const calls = [], updates = [], selectedEvents = [], changes = []
  const timers = new Map(); let timerId = 0
  const permission = Vue.ref(true)
  const platformState = Vue.reactive({ account: { id: 'actor-A', permissions: ['admin.accounts.read'] }, token: 'token-A' })
  const windowMock = {
    setTimeout(callback, delay = 0) { const id = ++timerId; timers.set(id, { callback, delay }); return id },
    clearTimeout(id) { timers.delete(id) },
  }
  const adminApi = { accountsPage: query => {
    const call = { query: { ...query }, ...deferred() }; calls.push(call); return call.promise
  } }
  const component = compileActualPicker({ adminApi, platformState,
    hasPermission: value => value === 'admin.accounts.read' && permission.value }, windowMock)
  const value = Vue.ref(modelValue)
  const root = node('root')
  const app = renderer.createApp({ render: () => Vue.h(component, {
    modelValue: value.value,
    'onUpdate:modelValue': next => { updates.push(next); value.value = next },
    onSelected: next => selectedEvents.push(next), onChange: next => changes.push(next),
  }) })
  app.mount(root); await flush()
  const state = app._instance.subTree.component.setupState
  return { app, root, state, calls, updates, selectedEvents, changes, permission, platformState, timers,
    value, runTimers: async delay => {
      const pending = [...timers.entries()].filter(([, timer]) => delay === undefined || timer.delay === delay)
      for (const [id, timer] of pending) { timers.delete(id); timer.callback() }
      await flush()
    }, unmount: () => app.unmount() }
}
async function ready(rows = [account('baseline')]) {
  const instance = await fixture()
  assert.equal(instance.calls.length, 1)
  instance.calls[0].resolve(page(rows)); await flush()
  return instance
}
async function resolveSearch(calls, activeRows, deletedRows = []) {
  assert.equal(calls.length, 2)
  calls[0].resolve(page(activeRows))
  calls[1].resolve(page(deletedRows, true))
  await flush()
}

const results = []
async function test(name, run) {
  try { await run(); results.push({ name, passed: true }) }
  catch (error) { results.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) }
}
async function useReady(run, rows) {
  const instance = await ready(rows)
  try { await run(instance) } finally { instance.unmount() }
}

await test('initial read is one bounded active account page and never the legacy full list', () => useReady(async instance => {
  assert.deepEqual(instance.calls[0].query, { page: 1, pageSize: 20, search: '', deleted: false })
  assert.equal(instance.state.accounts.length, 1)
  assert.doesNotMatch(source, /adminApi\.accounts\(\)/)
}))

await test('typing debounces to one server search and accepts server-only matches without local refiltering', () => useReady(async instance => {
  instance.state.onInput({ target: { value: 'first' } })
  instance.state.onInput({ target: { value: ' mail@example.test ' } })
  assert.equal([...instance.timers.values()].filter(timer => timer.delay === 220).length, 1)
  await instance.runTimers(220)
  const batch = instance.calls.slice(1)
  assert.deepEqual(batch.map(call => call.query), [
    { page: 1, pageSize: 20, search: 'mail@example.test', deleted: false },
    { page: 1, pageSize: 20, search: 'mail@example.test', deleted: true },
  ])
  await resolveSearch(batch, [account('server-email-match', { username: '不含搜索词' })])
  assert.equal(instance.state.candidates.length, 1)
  assert.equal(instance.state.candidates[0].id, 'server-email-match')
}))

await test('older success and finally cannot replace a newer request; current failure remains an error over old rows', () => useReady(async instance => {
  const oldStart = instance.calls.length; const old = instance.state.loadAccounts('old')
  const latestStart = instance.calls.length; const latest = instance.state.loadAccounts('latest')
  await resolveSearch(instance.calls.slice(oldStart, oldStart + 2), [account('old-result')])
  await old; assert.equal(instance.state.accounts[0].id, 'baseline'); assert.equal(instance.state.loading, true)
  const latestCalls = instance.calls.slice(latestStart, latestStart + 2)
  latestCalls[0].reject(new Error('current lookup failed')); latestCalls[1].resolve(page([], true))
  await latest; await flush()
  assert.equal(instance.state.error, 'current lookup failed')
  assert.equal(instance.state.accounts[0].id, 'baseline', 'failed lookup must not masquerade as an empty success')
  assert.ok(source.indexOf('v-else-if="error"') < source.indexOf('v-else>没有匹配的玩家账号'))
}))

await test('account, token and permission ABA retire every old reply before it can commit', () => useReady(async instance => {
  const tokenOldStart = instance.calls.length; const tokenOld = instance.state.loadAccounts('token-old')
  instance.platformState.token = 'token-B'; instance.platformState.token = 'token-A'; await flush()
  const tokenLoads = instance.calls.slice(tokenOldStart + 2)
  assert.equal(tokenLoads.length, 2, 'flush-sync scope watcher must observe both token transitions')
  await resolveSearch(instance.calls.slice(tokenOldStart, tokenOldStart + 2), [account('stale-token')])
  await tokenOld; tokenLoads[0].resolve(page([account('token-B-row')])); tokenLoads[1].resolve(page([account('token-A-row')]))
  await flush(); assert.equal(instance.state.accounts[0].id, 'token-A-row')

  const permissionOldStart = instance.calls.length; const permissionOld = instance.state.loadAccounts('permission-old')
  instance.permission.value = false; instance.permission.value = true; await flush()
  const permissionReload = instance.calls.at(-1)
  assert.equal(instance.state.error, '')
  await resolveSearch(instance.calls.slice(permissionOldStart, permissionOldStart + 2), [account('stale-permission')])
  await permissionOld; permissionReload.resolve(page([account('permission-restored')]))
  await flush(); assert.equal(instance.state.accounts[0].id, 'permission-restored')

  instance.state.choose(instance.state.accounts[0]); await flush()
  const accountOldStart = instance.calls.length; const accountOld = instance.state.loadAccounts('account-old')
  instance.platformState.account = { id: 'actor-B', permissions: ['admin.accounts.read'] }
  instance.platformState.account = { id: 'actor-A', permissions: ['admin.accounts.read'] }
  await flush(); assert.ok(instance.updates.includes(''))
  await resolveSearch(instance.calls.slice(accountOldStart, accountOldStart + 2), [account('stale-account')])
  await accountOld
  const accountReloads = instance.calls.slice(accountOldStart + 2)
  accountReloads[0].resolve(page([account('actor-B-row')]))
  accountReloads[1].resolve(page([account('actor-A-row')]))
  await flush(); assert.equal(instance.state.accounts[0].id, 'actor-A-row')
}))

await test('selected ID backfills through the same page API; disabled and deleted rows stay visible but unselectable', async () => {
  const instance = await fixture('deleted-id')
  try {
    assert.equal(instance.calls.length, 2)
    assert.ok(instance.calls.every(call => call.query.search === 'deleted-id' && call.query.pageSize === 20))
    await resolveSearch(instance.calls, [account('disabled-id', { disabled: true }), account('enabled-id')],
      [account('deleted-id', { deleted: true, username: '已删除玩家' })])
    assert.equal(instance.state.selectedAccount.id, 'deleted-id')
    assert.equal(instance.state.query, '已删除玩家')
    assert.equal(instance.state.selectableCandidates.map(row => row.id).join(','), 'enabled-id')
    const updatesBefore = instance.updates.length
    instance.state.choose(instance.state.accounts.find(row => row.id === 'disabled-id'))
    instance.state.choose(instance.state.accounts.find(row => row.id === 'deleted-id'))
    assert.equal(instance.updates.length, updatesBefore)
  } finally { instance.unmount() }
})

await test('keyboard navigation skips unavailable rows and Enter chooses the active bounded candidate', () => useReady(async instance => {
  const start = instance.calls.length; const pending = instance.state.loadAccounts('keyboard')
  await resolveSearch(instance.calls.slice(start, start + 2), [account('disabled', { disabled: true }),
    account('enabled-1'), account('enabled-2')]); await pending
  instance.state.onFocus(); assert.equal(instance.state.activeAccount.id, 'enabled-1')
  let prevented = 0
  instance.state.onKeydown({ key: 'ArrowDown', preventDefault: () => { prevented++ } })
  assert.equal(instance.state.activeAccount.id, 'enabled-2')
  instance.state.onKeydown({ key: 'Enter', preventDefault: () => { prevented++ } }); await flush()
  assert.equal(instance.value.value, 'enabled-2'); assert.equal(instance.changes.at(-1).id, 'enabled-2')
  assert.equal(prevented, 2); assert.equal(instance.state.open, false)
}))

await test('typing immediately retires old candidates from Enter', () => useReady(async instance => {
  instance.state.onFocus(); await flush()
  const updatesBefore = instance.updates.length; const selectedBefore = instance.selectedEvents.length
  const changesBefore = instance.changes.length
  instance.state.onInput({ target: { value: 'new-keyboard' } })
  instance.state.onKeydown({ key: 'Enter', preventDefault() {} }); await flush()
  assert.equal(instance.updates.length, updatesBefore)
  assert.equal(instance.selectedEvents.length, selectedBefore)
  assert.equal(instance.changes.length, changesBefore)
}, [account('old-keyboard')]))

await test('typing rejects a click retained by a stale rendered option', async () => {
  const instance = await ready([account('old-pointer')])
  try {
    instance.state.onFocus(); await flush()
    const oldButton = descendants(instance.root, 'button').find(button => button.props.role === 'option')
    assert.ok(oldButton?.props.onClick, 'actual rendered old option must expose its click handler')
    const updatesBefore = instance.updates.length; const selectedBefore = instance.selectedEvents.length
    const changesBefore = instance.changes.length
    instance.state.onInput({ target: { value: 'new-pointer' } }); await flush()
    oldButton.props.onClick(); await flush()
    assert.equal(instance.updates.length, updatesBefore)
    assert.equal(instance.selectedEvents.length, selectedBefore)
    assert.equal(instance.changes.length, changesBefore)
  } finally { instance.unmount() }
})

await test('typing retires an in-flight old search before debounce and new results remain selectable', () => useReady(async instance => {
  const oldStart = instance.calls.length; const oldPending = instance.state.loadAccounts('old-request')
  instance.state.onInput({ target: { value: 'new-request' } })
  await resolveSearch(instance.calls.slice(oldStart, oldStart + 2), [account('stale-old-result')])
  await oldPending
  assert.equal(instance.state.accounts[0].id, 'baseline')
  assert.equal(instance.state.query, 'new-request')

  await instance.runTimers(220)
  const newCalls = instance.calls.slice(oldStart + 2)
  await resolveSearch(newCalls, [account('fresh-new-result')])
  assert.equal(instance.state.activeAccount.id, 'fresh-new-result')
  instance.state.onKeydown({ key: 'Enter', preventDefault() {} }); await flush()
  assert.equal(instance.value.value, 'fresh-new-result')
  assert.equal(instance.changes.at(-1).id, 'fresh-new-result')
}))

await test('invalid or oversized page metadata is rejected without erasing the last valid candidates', () => useReady(async instance => {
  const start = instance.calls.length; const pending = instance.state.loadAccounts('bad-page')
  const calls = instance.calls.slice(start, start + 2)
  calls[0].resolve(page(Array.from({ length: 21 }, (_, index) => account(`overflow-${index}`)), false,
    { total: 21, pageSize: 21 }))
  calls[1].resolve(page([], true))
  await pending; await flush()
  assert.match(instance.state.error, /分页响应无效/)
  assert.equal(instance.state.accounts[0].id, 'baseline')
}))

await test('unmount cancels debounce and pending responses cannot write state', async () => {
  const instance = await ready()
  instance.state.onInput({ target: { value: 'pending' } })
  assert.equal([...instance.timers.values()].filter(timer => timer.delay === 220).length, 1)
  instance.unmount(); assert.equal(instance.timers.size, 0)
  const second = await ready()
  const start = second.calls.length; const pending = second.state.loadAccounts('after-unmount')
  second.unmount(); await resolveSearch(second.calls.slice(start, start + 2), [account('late-unmounted')])
  await pending; assert.equal(second.state.accounts[0].id, 'baseline')
})

for (const result of results)
  console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
console.log('RESULT ' + JSON.stringify({ cases: results, passed: results.filter(result => result.passed).length,
  failed: results.filter(result => !result.passed).length,
  sourceSha256: createHash('sha256').update(source).digest('hex').toUpperCase(), actualSfc: true,
  actualTemplate: true, realApiCalls: 0, networkCalls: 0 }))
if (results.some(result => !result.passed)) process.exitCode = 1
