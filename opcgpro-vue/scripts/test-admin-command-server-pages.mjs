import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const require = createRequire(import.meta.url)
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const source = fs.readFileSync(new URL('../src/l12/site/AdminCommandsPage.vue', import.meta.url), 'utf8')
const pagedSource = fs.readFileSync(new URL('../src/l12/site/PagedCollection.vue', import.meta.url), 'utf8')
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const flush = async () => { for (let i = 0; i < 6; i++) { await Promise.resolve(); await Vue.nextTick() } }
const row = n => ({ id: `synthetic-command-${n}`, type: 'synthetic.operation', actorId: 'synthetic-actor', actorName: '合成操作者', requestedAt: '2026-01-01T00:00:00Z',
  scope: 'synthetic:scope', dryRun: false, risk: 'low', status: 'executed', permission: 'synthetic.permission', payload: { marker: 'synthetic-payload' }, result: { marker: 'synthetic-result' }, correlationId: `synthetic-correlation-${n}`, resourceVersion: 1, updatedAt: '2026-01-01T00:00:00Z' })
const pageFor = (page = 1, total = 205) => ({ items: Array.from({ length: Math.min(10, Math.max(0, total - (page - 1) * 10)) }, (_, i) => row((page - 1) * 10 + i + 1)), page, pageSize: 10, total })
function node(tag, text = '') {
  const listeners = new Map()
  return { tag, tagName: tag.toUpperCase(), text, children: [], parent: null, props: {}, value: '', selected: false, selectedIndex: -1,
    addEventListener(event, listener) { const current = listeners.get(event) ?? []; current.push(listener); listeners.set(event, current) },
    removeEventListener(event, listener) { listeners.set(event, (listeners.get(event) ?? []).filter(value => value !== listener)) },
    dispatch(event) { const value = { target: this }; for (const listener of listeners.get(event) ?? []) listener(value); this.props['on' + event[0].toUpperCase() + event.slice(1)]?.(value) },
    get options() { return this.children.filter(child => child.tag === 'option') },
  }
}
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
const renderer = Vue.createRenderer({ createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) { detach(child); child.parent = parent; const index = anchor ? parent.children.indexOf(anchor) : -1; if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child) }, remove: detach,
  parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, text) => { child.text = text }, setElementText(child, text) { child.children = []; child.text = text }, setScopeId() {},
  patchProp(child, key, old, value) { child.props[key] = value; if (key === 'value') { child.value = value; child._value = value } },
})
const text = root => root.text + root.children.map(text).join('')
const walk = root => [root, ...root.children.flatMap(walk)]
function button(root, label) { const found = walk(root).filter(child => child.tag === 'button' && text(child) === label); assert.equal(found.length, 1, label); return found[0] }
function evaluate(code, imports) {
  const module = { exports: {} }
  const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(output, { module, exports: module.exports, require: id => { assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id] }, Date, Error, Map, Set, console })
  return module.exports
}
function compile(content, imports, render) {
  const { descriptor, errors } = parse(content); assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'command-server-page-regression' })
  const component = evaluate(script.content, { vue: Vue, ...imports }).default
  const template = compileTemplate({ source: descriptor.template.content, filename: 'actual-command-page.vue', id: 'command-server-page-regression', compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
  assert.deepEqual(template.errors, [])
  component.render = render ? evaluate(template.code, { vue: Vue }).render : () => null
  return component
}
async function fixture(render = false, permitted = true) {
  const calls = [], legacyCalls = [], details = [], errors = [], allowed = Vue.ref(permitted)
  const platformState = Vue.reactive({ account: { id: 'actor-A', permissionVersion: 1 }, token: 'synthetic-token-A' })
  const adminApi = {
    commands: async () => { legacyCalls.push(1); return Array.from({ length: 200 }, (_, i) => row(i + 1)) },
    commandsPage: query => { const call = { query: { ...query }, ...deferred() }; calls.push(call); return call.promise },
    command: id => { const call = { id, ...deferred() }; details.push(call); return call.promise },
  }
  const component = compile(source, { '@/l12/platform': { adminApi, platformState, hasPermission: () => allowed.value }, './PagedCollection.vue': { __esModule: true, default: compile(pagedSource, {}, true) } }, render)
  const root = node('root'), app = renderer.createApp(component); app.config.errorHandler = error => errors.push(error)
  app.mount(root); await flush()
  return { state: app._instance.setupState, calls, legacyCalls, details, errors, allowed, platformState, root, unmount: () => app.unmount() }
}
async function ready(render = false) {
  const f = await fixture(render)
  try { assert.equal(f.legacyCalls.length, 0, 'do not fall back to the newest-200 legacy window'); assert.equal(f.calls.length, 1); f.calls[0].resolve(pageFor()); await flush(); assert.deepEqual(f.errors, []); return f }
  catch (error) { f.unmount(); throw error }
}
async function use(run, render = false) { const f = await ready(render); try { await run(f); assert.deepEqual(f.errors, []) } finally { f.unmount() } }
async function load(f, page) { const pending = f.state.load(page); f.calls.at(-1).resolve(pageFor(page)); await pending; await flush() }
const results = []
async function test(name, run) { try { await run(); results.push({ name, passed: true }) } catch (error) { results.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) } }

await test('server query uses page 10/full total, page 2 and server-clamped last page without slicing', () => use(async f => {
  assert.deepEqual(f.calls[0].query, { page: 1, pageSize: 10, status: '' }); assert.equal(f.state.commands.length, 10); assert.equal(f.state.result.total, 205)
  await load(f, 2); assert.equal(f.state.commands[0].id, 'synthetic-command-11')
  const over = f.state.load(99); f.calls.at(-1).resolve(pageFor(21)); await over; assert.equal(f.state.page, 21); assert.equal(f.state.commands.length, 5)
}))
await test('raw status options preserve semantics, reset page and query ABA retires old list', () => use(async f => {
  const old = f.state.load(2); f.state.status = 'failed'; f.state.status = ''; f.calls[1].resolve(pageFor(2)); await old; assert.equal(f.state.result, null)
  for (const status of ['requested', 'executed', 'failed', 'rejected']) { f.state.status = status; const current = f.state.load(); assert.equal(f.calls.at(-1).query.status, status); assert.equal(f.calls.at(-1).query.page, 1); f.calls.at(-1).resolve(pageFor()); await current }
}))
await test('list old success/error/finally cannot replace current result or loading, and error is not empty success', () => use(async f => {
  const old = f.state.load(2), current = f.state.load(3); f.calls[1].resolve(pageFor(2)); await old; assert.equal(f.state.loading, true); assert.equal(f.state.result, null)
  f.calls[2].resolve(pageFor(3)); await current; assert.equal(f.state.page, 3)
  const badOld = f.state.load(2), badCurrent = f.state.load(1); f.calls[3].reject(new Error('old read')); await badOld; assert.equal(f.state.loading, true); assert.equal(f.state.notice, '')
  f.calls[4].reject(new Error('current read')); await badCurrent; assert.equal(f.state.notice, 'current read'); assert.equal(f.state.result, null); assert.equal(f.state.loading, false)
}))
await test('detail A then B keeps B across late A success and does not overwrite detail loading/error', () => use(async f => {
  const a = f.state.show(row(1).id), b = f.state.show(row(2).id); f.details[1].resolve(row(2)); await b; f.details[0].resolve(row(1)); await a; assert.equal(f.state.selected.id, row(2).id)
  const old = f.state.show(row(3).id), current = f.state.show(row(4).id); f.details[2].reject(new Error('old detail')); await old; assert.equal(f.state.detailLoading, true); assert.equal(f.state.detailError, ''); assert.equal(f.state.selected, null)
  f.details[3].reject(new Error('current detail')); await current; assert.equal(f.state.detailError, 'current detail'); assert.equal(f.state.detailLoading, false); assert.equal(f.state.selected, null)
}))
await test('closing pending detail retires both late success and failure', () => use(async f => {
  const a = f.state.show(row(1).id); f.state.closeDetail(); f.details[0].resolve(row(1)); await a; assert.equal(f.state.selected, null); assert.equal(f.state.selectedId, ''); assert.equal(f.state.detailLoading, false)
  const b = f.state.show(row(2).id); f.state.closeDetail(); f.details[1].reject(new Error('closed detail')); await b; assert.equal(f.state.detailError, ''); assert.equal(f.state.selected, null)
}))
await test('page/filter/refresh clear details; stale or foreign-page ids cannot start a new detail read', () => use(async f => {
  const stale = f.state.show(row(1).id); await load(f, 2); f.details[0].resolve(row(1)); await stale; assert.equal(f.state.selected, null)
  await f.state.show(row(1).id); assert.equal(f.details.length, 1)
  const current = f.state.show(row(11).id); f.state.status = 'failed'; f.state.status = ''; f.details[1].reject(new Error('stale same-status ABA')); await current; assert.equal(f.state.detailError, '')
  await load(f, 1); const refresh = f.state.show(row(1).id); const reload = f.state.load(); f.details[2].resolve(row(1)); await refresh; assert.equal(f.state.selected, null); f.calls.at(-1).resolve(pageFor()); await reload
}))
await test('token/id/permission-version ABA and permission loss retire list and detail ownership', () => use(async f => {
  const detail = f.state.show(row(1).id), read = f.state.load(2), oldRead = f.calls.at(-1)
  f.platformState.token = 'synthetic-token-B'; f.platformState.token = 'synthetic-token-A'; assert.equal(f.state.result, null); assert.equal(f.state.selected, null)
  oldRead.resolve(pageFor(2)); f.details[0].resolve(row(1)); await read; await detail; await flush(); assert.equal(f.state.result, null)
  f.calls.at(-1).resolve(pageFor()); await flush(); f.platformState.account.permissionVersion = 2; f.platformState.account.permissionVersion = 1; assert.equal(f.state.result, null)
  await flush(); f.calls.at(-1).resolve(pageFor()); await flush(); f.platformState.account = { id: 'actor-B', permissionVersion: 1 }; f.platformState.account = { id: 'actor-A', permissionVersion: 1 }; assert.equal(f.state.result, null)
  await flush(); f.calls.at(-1).resolve(pageFor()); await flush(); f.allowed.value = false; assert.equal(f.state.result, null); assert.equal(f.state.commands.length, 0)
}))
await test('unmount ignores pending list/detail success/error/finally', async () => {
  const f = await ready(), detail = f.state.show(row(1).id), read = f.state.load(2); f.unmount(); f.details[0].reject(new Error('unmounted detail')); f.calls[1].resolve(pageFor(2)); await detail; await read
  assert.equal(f.state.selected, null); assert.equal(f.state.result, null); assert.equal(f.state.detailError, ''); assert.equal(f.state.notice, '')
})
await test('unsafe metadata/truncated or duplicate page and mismatched detail fail closed', () => use(async f => {
  const detail = f.state.show(row(1).id); f.details[0].resolve(row(2)); await detail; assert.equal(f.state.selected, null); assert.ok(f.state.detailError)
  for (const response of [{ ...pageFor(), items: [] }, { ...pageFor(), total: Number.MAX_SAFE_INTEGER + 1 }, { ...pageFor(), items: Array.from({ length: 10 }, () => row(1)) }]) {
    const pending = f.state.load(1); f.calls.at(-1).resolve(response); await pending; assert.equal(f.state.result, null); assert.match(f.state.notice, /分页响应无效/)
  }
}))
await test('permission absent on mount causes zero list/detail requests', async () => {
  const f = await fixture(false, false); try { assert.equal(f.calls.length, 0); assert.equal(f.legacyCalls.length, 0); await f.state.show(row(1).id); assert.equal(f.details.length, 0) } finally { f.unmount() }
})
await test('real full SFC/Paged template navigates intact rows, closes pending detail, reads payload/result only', () => use(async f => {
  assert.equal(walk(f.root).filter(child => child.props.class === 'row').length, 10)
  button(f.root, '下一页').props.onClick(); f.calls.at(-1).resolve(pageFor(2)); await flush(); assert.equal(f.state.page, 2)
  assert.equal(walk(f.root).filter(child => child.props.class === 'row').length, 10)
  walk(f.root).find(child => child.props.class === 'row').props.onClick(); await flush(); button(f.root, '关闭').props.onClick(); f.details[0].resolve(row(11)); await flush(); assert.equal(f.state.selected, null)
  walk(f.root).find(child => child.props.class === 'row').props.onClick(); f.details[1].resolve(row(11)); await flush()
  assert.ok(text(f.root).includes('synthetic-payload')); assert.ok(text(f.root).includes('synthetic-result'))
  assert.equal(walk(f.root).some(child => child.tag === 'button' && /批准|执行/.test(text(child))), false)
  const select = walk(f.root).find(child => child.tag === 'select'); select.options.forEach(option => { option.selected = option.props.value === 'requested' }); select.dispatch('change'); await flush()
  assert.equal(f.calls.at(-1).query.status, 'requested'); assert.equal(f.calls.at(-1).query.page, 1); assert.equal(f.state.selected, null)
  f.calls.at(-1).reject(new Error('visible read failure')); await flush(); assert.ok(text(f.root).includes('visible read failure')); assert.ok(!text(f.root).includes('暂无命令记录'))
}, true))

for (const result of results) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
console.log('RESULT ' + JSON.stringify({ cases: results, passed: results.filter(result => result.passed).length, failed: results.filter(result => !result.passed).length,
  sourceSha256: createHash('sha256').update(source).digest('hex').toUpperCase(), actualSfcScript: true, actualSfcTemplate: true, realApiCalls: 0, networkCalls: 0 }))
if (results.some(result => !result.passed)) process.exitCode = 1
