import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const require = createRequire(import.meta.url)
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const accountSource = fs.readFileSync(new URL('../src/l12/site/AdminAccountsPage.vue', import.meta.url), 'utf8')
const pagedSource = fs.readFileSync(new URL('../src/l12/site/PagedCollection.vue', import.meta.url), 'utf8')
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const flush = async () => { for (let i = 0; i < 5; i++) { await Promise.resolve(); await Vue.nextTick() } }
const row = n => ({ id: `synthetic-${n}`, username: `合成${n}`, role: 'player', permissionVersion: 7, disabled: false, deleted: false, permissions: [] })
const pageFor = (page = 1, deleted = false, total = 25) => ({ items: Array.from({ length: Math.min(10, Math.max(0, total - (page - 1) * 10)) }, (_, i) => ({ ...row((page - 1) * 10 + i + 1), deleted })), page, pageSize: 10, total, activeCount: 100, deletedCount: 5 })
function node(tag, text = '') { return { tag, text, children: [], parent: null, props: {} } }
const content = root => root.text + root.children.map(content).join('')
function button(root, label) {
  const visit = node => [node, ...node.children.flatMap(visit)]
  const matches = visit(root).filter(node => node.tag === 'button' && content(node) === label)
  assert.equal(matches.length, 1, label); return matches[0]
}
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
const renderer = Vue.createRenderer({ createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) { detach(child); child.parent = parent; const i = anchor ? parent.children.indexOf(anchor) : -1; if (i < 0) parent.children.push(child); else parent.children.splice(i, 0, child) }, remove: detach,
  parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, text) => { child.text = text }, setElementText(child, text) { child.children = []; child.text = text }, patchProp: (child, key, old, value) => { child.props[key] = value }, setScopeId() {},
})
function evaluate(code, imports, globals = {}) {
  const module = { exports: {} }
  const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(output, { module, exports: module.exports, require: id => { assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id] }, Date, Error, Map, Set, console, ...globals })
  return module.exports
}
function compile(source, imports = {}, globals = {}, render = false) {
  const { descriptor, errors } = parse(source); assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'account-page-regression' })
  const component = evaluate(script.content, { vue: Vue, ...imports }, globals).default
  const template = compileTemplate({ source: descriptor.template.content, filename: 'actual-account-SFC.vue', id: 'account-page-regression', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  if (render) {
    component.render = evaluate(template.code, { vue: Vue }).render
  } else component.render = () => null
  return component
}
async function fixture() {
  const calls = [], legacyCalls = [], riskCalls = [], mutationCalls = []
  const timers = new Map(); let timerId = 0
  const platformState = Vue.reactive({ account: { id: 'actor-A', permissionVersion: 1 }, token: 'synthetic-token-A' })
  const riskAction = Vue.shallowRef(null), riskBusy = Vue.ref(false), riskError = Vue.ref('')
  const resetGate = deferred()
  const clipboardGate = deferred()
  const adminApi = {
    accounts: async () => { legacyCalls.push(1); return Array.from({ length: 25 }, (_, i) => row(i + 1)) },
    accountsPage: query => { const call = { query: { ...query }, ...deferred() }; calls.push(call); return call.promise },
    setRole: async (...args) => { mutationCalls.push({ kind: 'role', args }) },
    setAccountStatus: async (...args) => { mutationCalls.push({ kind: 'status', args }); return { revokedSessions: 1 } },
    revokeSessions: async (...args) => { mutationCalls.push({ kind: 'revoke', args }); return { revokedCount: 1 } },
    resetAccountPassword: async (...args) => { mutationCalls.push({ kind: 'reset', args }); return resetGate.promise },
    deleteAccount: async (...args) => { mutationCalls.push({ kind: 'delete', args }); return { removedPrivateRecords: 0, revokedSessions: 1 } },
  }
  const component = compile(accountSource, { '@/l12/platform': { adminApi, hasPermission: () => true, platformState }, './PagedCollection.vue': { default: {} }, './AdminRiskActionDialog.vue': { default: {} }, './useAdminRiskAction': { useAdminRiskAction: () => ({ riskAction, riskBusy, riskError, requestRiskAction: action => { riskCalls.push(action); riskAction.value = action }, cancelRiskAction: () => { riskAction.value = null }, confirmRiskAction() {} }) } }, {
    setTimeout: callback => { const id = ++timerId; timers.set(id, callback); return id }, clearTimeout: id => timers.delete(id),
    navigator: { clipboard: { writeText: () => clipboardGate.promise } },
  })
  const root = node('root'), vnode = Vue.h(component)
  renderer.render(vnode, root); await flush()
  return { state: vnode.component.setupState, calls, legacyCalls, riskCalls, mutationCalls, resetGate, clipboardGate, platformState, timers,
    runTimers: async () => { const pending = [...timers.values()]; timers.clear(); pending.forEach(callback => callback()); await flush() },
    unmount: () => renderer.render(null, root) }
}
async function ready() {
  const f = await fixture()
  try { assert.equal(f.legacyCalls.length, 0, 'no full-collection fallback'); assert.equal(f.calls.length, 1); f.calls[0].resolve(pageFor()); await flush(); return f }
  catch (error) { f.unmount(); throw error }
}
const results = []
async function test(name, run) { try { await run(); results.push({ name, passed: true }) } catch (error) { results.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) } }
async function use(run) { const f = await ready(); try { await run(f) } finally { f.unmount() } }

await test('initial request uses a bounded active server page and global partition counts', () => use(async f => {
  assert.deepEqual(f.calls[0].query, { page: 1, pageSize: 10, search: '', deleted: false })
  assert.equal(f.state.activeAccounts.length, 10); assert.equal(f.state.activeResult.total, 25); assert.equal(f.state.deletedCount, 5)
  assert.equal(f.state.activeCount, 100)
}))
await test('page 2 and server-clamped last page are accepted without client slicing', () => use(async f => {
  const pending = f.state.loadActive(2); assert.equal(f.calls[1].query.page, 2)
  f.calls[1].resolve(pageFor(2)); await pending; await flush()
  assert.equal(f.state.activePage, 2); assert.equal(f.state.activeAccounts[0].id, 'synthetic-11')
  const over = f.state.loadActive(99); f.calls[2].resolve(pageFor(3)); await over; await flush()
  assert.equal(f.state.activePage, 3); assert.equal(f.state.activeAccounts.length, 5)
}))
await test('search debounces once, resets page, uses only server filtering, keeps global counts', () => use(async f => {
  f.state.activePage = 2; f.state.search = 'first'; f.state.search = ' final '
  assert.equal(f.state.activeAccounts.length, 0); assert.equal(f.state.activePage, 1); assert.equal(f.timers.size, 1)
  assert.equal(f.state.deletedCount, 5)
  await f.runTimers(); assert.equal(f.calls.length, 2); assert.equal(f.calls[1].query.search, 'final'); assert.equal(f.calls[1].query.page, 1)
  f.calls[1].resolve({ ...pageFor(1, false, 1), items: [row(3)] }); await flush()
  assert.equal(f.state.activeAccounts.length, 1, 'do not local-filter a server result a second time')
}))
await test('older success cannot replace a newer page or its loading state', () => use(async f => {
  const old = f.state.loadActive(2), latest = f.state.loadActive(3)
  f.calls[1].resolve(pageFor(2)); await old; await flush()
  assert.equal(f.state.loading, true); assert.equal(f.state.activeResult, null)
  f.calls[2].resolve(pageFor(3)); await latest; await flush()
  assert.equal(f.state.activePage, 3); assert.equal(f.state.loading, false)
}))
await test('late failure/finally ignored; current failure is an error, not successful empty', () => use(async f => {
  const old = f.state.loadActive(2), latest = f.state.loadActive(3)
  f.calls[1].reject(new Error('old failure')); await old; await flush()
  assert.equal(f.state.loading, true); assert.equal(f.state.activeError, '')
  f.calls[2].reject(new Error('current failure')); await latest; await flush()
  assert.equal(f.state.activeError, 'current failure'); assert.equal(f.state.activeResult, null); assert.equal(f.state.loading, false)
  assert.match(accountSource, /activeResult && !activeAccounts.length/)
}))
await test('deleted dialog reads only its own page and closing retires late success/error', () => use(async f => {
  f.state.showDeleted = true; await flush(); assert.equal(f.calls[1].query.deleted, true)
  f.state.showDeleted = false; f.calls[1].resolve(pageFor(1, true)); await flush()
  assert.equal(f.state.deletedResult, null); assert.equal(f.state.deletedLoading, false)
  f.state.showDeleted = true; await flush(); f.state.showDeleted = false
  f.calls[2].reject(new Error('closed failure')); await flush(); assert.equal(f.state.deletedError, '')
  assert.equal(f.state.activeAccounts.length, 10)
  f.state.showDeleted = true; await flush()
  const active = f.state.loadActive(2)
  f.calls[4].resolve({ ...pageFor(2), deletedCount: 8 }); await active; await flush()
  f.calls[3].resolve({ ...pageFor(1, true), deletedCount: 7 }); await flush()
  assert.equal(f.state.deletedCount, 8, 'older other partition reply cannot overwrite newer global counts')
}))
await test('deleted page navigation and search share scope but never fetch all deleted rows', () => use(async f => {
  f.state.showDeleted = true; await flush(); f.calls[1].resolve(pageFor(1, true)); await flush()
  const pending = f.state.loadDeleted(2); assert.equal(f.calls[2].query.deleted, true); assert.equal(f.calls[2].query.page, 2)
  f.calls[2].resolve(pageFor(2, true)); await pending; await flush(); assert.equal(f.state.deletedAccounts.length, 10)
  f.state.search = 'query'; assert.equal(f.state.deletedPage, 1); assert.equal(f.state.deletedResult, null)
  await f.runTimers(); assert.equal(f.calls.at(-1).query.deleted, true); assert.equal(f.calls.at(-1).query.search, 'query')
  assert.match(accountSource, /:server-page="deletedPage"[\s\S]*@page="loadDeleted"/)
}))
await test('account/token ABA synchronously retires old data and late replies', () => use(async f => {
  const pending = f.state.loadActive(2)
  f.platformState.token = 'synthetic-token-B'; f.platformState.token = 'synthetic-token-A'
  assert.equal(f.state.activeResult, null); assert.equal(f.state.deletedCount, null)
  f.calls[1].resolve(pageFor(2)); await pending; await flush(); assert.equal(f.state.activeResult, null)
  const current = f.calls.at(-1); assert.equal(current.query.page, 1); current.resolve(pageFor()); await flush()
  f.platformState.account = { id: 'actor-B', permissionVersion: 1 }; f.platformState.account = { id: 'actor-A', permissionVersion: 1 }
  assert.equal(f.state.activeResult, null)
}))
await test('unmount cancels debounce and blocks pending commits, notices and finally', async () => {
  const f = await ready(); f.state.search = 'pending'; f.unmount(); assert.equal(f.timers.size, 0)
  const g = await ready(); const pending = g.state.loadActive(2); g.unmount(); g.calls[1].reject(new Error('after unmount')); await pending; await flush()
  assert.equal(g.state.activeResult, null); assert.equal(g.state.activeError, '')
})
await test('server PagedCollection passes page rows intact, emits navigation, honors busy/offset/total', async () => {
  const component = compile(pagedSource, {}, {}, true), rows = Array.from({ length: 10 }, (_, i) => row(i + 11)), events = []
  const props = Vue.reactive({ items: rows, pageSize: 10, serverPage: 2, serverTotal: 45, busy: false })
  let slot
  const root = node('root'), app = renderer.createApp({ render: () => Vue.h(component, { ...props, onPage: value => events.push(value) }, { default: value => { slot = value; return Vue.h('span', String(value.items.length)) } }) })
  app.mount(root); await flush()
  try {
    assert.equal(slot.items.length, 10); assert.equal(slot.offset, 10)
    const instance = app._instance.subTree.component, state = instance.setupState
    assert.equal(state.pages, 5); button(root, '下一页').props.onClick(); assert.deepEqual(events, [3])
    props.busy = true; await flush(); assert.equal(button(root, '上一页').props.disabled, true); button(root, '上一页').props.onClick(); assert.deepEqual(events, [3])
    props.serverPage = 3; props.items = rows.slice(0, 5); await flush(); assert.equal(slot.items.length, 5); assert.equal(slot.offset, 20)
  } finally { app.unmount() }
})
await test('old client PagedCollection keeps slicing, page resets, slot offsets and no server event', async () => {
  const component = compile(pagedSource, {}, {}, true), events = [], props = Vue.reactive({ items: Array.from({ length: 23 }, (_, i) => row(i + 1)) })
  let slot; const root = node('root'), app = renderer.createApp({ render: () => Vue.h(component, { ...props, onPage: value => events.push(value) }, { default: value => { slot = value; return Vue.h('span', String(value.items.length)) } }) })
  app.mount(root); await flush()
  try {
    const state = app._instance.subTree.component.setupState
    assert.equal(slot.items.length, 10); assert.equal(slot.offset, 0); button(root, '下一页').props.onClick(); await flush()
    assert.equal(slot.items[0].id, 'synthetic-11'); assert.equal(slot.offset, 10)
    props.items = [row(99)]; await flush(); assert.equal(state.page, 1); assert.equal(slot.items.length, 1); assert.equal(slot.offset, 0); assert.equal(events.length, 0)
  } finally { app.unmount() }
})
await test('risk reasons/CAS/protected controls preserved and late password response cannot cross actor', async () => {
  const f = await fixture()
  try {
    assert.match(accountSource, /account.username === 'Admin' \|\| account.id === platformState.account\?\.id/)
    for (const permission of ['admin.accounts.roles.write', 'admin.accounts.status.write', 'admin.sessions.revoke']) assert.ok(accountSource.includes(permission))
    f.state.resetPassword(row(7)); assert.equal(f.riskCalls.length, 0, 'reason required')
    f.state.reasons['synthetic-7'] = 'synthetic reason'; f.state.resetPassword(row(7)); const pending = f.riskCalls[0].run()
    assert.deepEqual(f.mutationCalls[0].args, ['synthetic-7', 'synthetic reason', 7])
    f.platformState.token = 'synthetic-token-B'; f.platformState.token = 'synthetic-token-A'
    f.resetGate.resolve({ temporaryPassword: 'synthetic-only-never-a-real-secret' }); await pending; await flush()
    assert.equal(f.state.temporaryPassword, ''); assert.equal(f.state.temporaryPasswordAccount, '')
    assert.equal(f.state.notice, '')
    f.state.temporaryPassword = 'synthetic-clipboard-only'
    const copied = f.state.copyPassword(); f.platformState.token = 'synthetic-token-C'; f.platformState.token = 'synthetic-token-A'
    f.clipboardGate.resolve(); await copied; await flush(); assert.equal(f.state.notice, '')
  } finally { f.unmount() }
})
await test('nonempty total cannot accept an empty/truncated page or unsafe integer metadata', () => use(async f => {
  const missing = f.state.loadActive(2); f.calls.at(-1).resolve({ ...pageFor(2), items: [] }); await missing; await flush()
  assert.equal(f.state.activeResult, null); assert.match(f.state.activeError, /分页响应无效/)
  const unsafe = f.state.loadActive(1); f.calls.at(-1).resolve({ ...pageFor(), activeCount: Number.MAX_SAFE_INTEGER + 1 }); await unsafe; await flush()
  assert.equal(f.state.activeResult, null); assert.match(f.state.activeError, /分页响应无效/)
}))
for (const result of results) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
console.log('RESULT ' + JSON.stringify({ cases: results, passed: results.filter(r => r.passed).length, failed: results.filter(r => !r.passed).length,
  accountSourceSha256: createHash('sha256').update(accountSource).digest('hex').toUpperCase(), pagedSourceSha256: createHash('sha256').update(pagedSource).digest('hex').toUpperCase(), actualSfcScript: true, actualPagedTemplate: true, realApiCalls: 0, networkCalls: 0 }))
if (results.some(result => !result.passed)) process.exitCode = 1
