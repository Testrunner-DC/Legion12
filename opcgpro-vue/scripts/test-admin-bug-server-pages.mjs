import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const require = createRequire(import.meta.url)
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const source = fs.readFileSync(new URL('../src/l12/site/AdminBugsPage.vue', import.meta.url), 'utf8')
const pagedSource = fs.readFileSync(new URL('../src/l12/site/PagedCollection.vue', import.meta.url), 'utf8')
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const flush = async () => { for (let i = 0; i < 6; i++) { await Promise.resolve(); await Vue.nextTick() } }
const row = n => ({ id: `synthetic-bug-${n}`, reporterName: '合成反馈者', title: `合成反馈${n}`, description: '合成问题', page: '/synthetic', version: 'synthetic-client',
  status: 'new', priority: 'normal', assignee: '', adminNotes: '', fixCommit: '', regressionTest: '', deployedVersion: '', duplicateOf: '', history: [], createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' })
const pageFor = (page = 1, total = 25, platformVersion = 9) => ({ items: Array.from({ length: Math.min(10, Math.max(0, total - (page - 1) * 10)) }, (_, i) => row((page - 1) * 10 + i + 1)), page, pageSize: 10, total, platformVersion })

const previousGlobals = ['Document', 'ShadowRoot'].map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)])
for (const [key, descriptor] of previousGlobals) if (!descriptor) Object.defineProperty(globalThis, key, { value: class {}, configurable: true })
function node(tag, text = '') {
  const events = new Map()
  return { tag, tagName: tag.toUpperCase(), text, children: [], parent: null, props: {}, value: '', selected: false, selectedIndex: -1,
    addEventListener(name, listener) { const listeners = events.get(name) ?? []; listeners.push(listener); events.set(name, listeners) },
    removeEventListener(name, listener) { events.set(name, (events.get(name) ?? []).filter(value => value !== listener)) },
    dispatch(name) { for (const listener of events.get(name) ?? []) listener({ target: this }) },
    getRootNode() { return this }, get options() { return this.children.filter(child => child.tag === 'option') },
  }
}
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
const renderer = Vue.createRenderer({ createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) { detach(child); child.parent = parent; const index = anchor ? parent.children.indexOf(anchor) : -1; if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child) }, remove: detach,
  parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, text) => { child.text = text }, setElementText(child, text) { child.children = []; child.text = text }, setScopeId() {},
  patchProp(child, key, old, value) { child.props[key] = value; if (key === 'value') { child.value = value; child._value = value } if (key === 'type') child.type = value },
})
const text = root => root.text + root.children.map(text).join('')
const walk = root => [root, ...root.children.flatMap(walk)]
function button(root, label) { const matches = walk(root).filter(child => child.tag === 'button' && text(child) === label); assert.equal(matches.length, 1, label); return matches[0] }
function evaluate(code, imports) {
  const module = { exports: {} }
  const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(output, { module, exports: module.exports, require: id => { assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id] }, Date, Error, Map, Set, console })
  return module.exports
}
function compile(content, imports, render) {
  const { descriptor, errors } = parse(content); assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'bug-server-page-regression' })
  const component = evaluate(script.content, { vue: Vue, ...imports }).default
  const template = compileTemplate({ source: descriptor.template.content, filename: 'actual-bug-page.vue', id: 'bug-server-page-regression', compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
  assert.deepEqual(template.errors, [])
  component.render = render ? evaluate(template.code, { vue: Vue }).render : () => null
  return component
}
async function fixture(render = false) {
  const calls = [], legacyCalls = [], writes = [], errors = []
  const route = Vue.reactive({ params: {} }), platformState = Vue.reactive({ account: { id: 'actor-A', permissionVersion: 1 }, token: 'synthetic-token-A' })
  const allowed = Vue.ref(true)
  const adminApi = {
    bugs: async () => { legacyCalls.push(1); return Array.from({ length: 25 }, (_, i) => row(i + 1)) },
    bugsPage: query => { const call = { query: { ...query }, ...deferred() }; calls.push(call); return call.promise },
    updateBug: (id, body) => { const call = { id, body: { ...body }, ...deferred() }; writes.push(call); return call.promise },
  }
  const paged = compile(pagedSource, {}, true)
  const component = compile(source, { 'vue-router': { useRoute: () => route }, '@/l12/platform': { adminApi, platformState, hasPermission: () => allowed.value }, './PagedCollection.vue': { __esModule: true, default: paged } }, render)
  const root = node('root'), app = renderer.createApp(component)
  app.component('router-link', { props: ['to'], setup: (props, { slots }) => () => Vue.h('a', { href: props.to }, slots.default?.()) })
  app.config.errorHandler = error => errors.push(error)
  app.mount(root); await flush()
  return { state: app._instance.setupState, calls, legacyCalls, writes, errors, root, route, platformState, allowed, unmount: () => app.unmount() }
}
async function ready(render = false) {
  const f = await fixture(render)
  try { assert.equal(f.legacyCalls.length, 0, 'no full-collection fallback'); assert.equal(f.calls.length, 1); f.calls[0].resolve(pageFor()); await flush(); assert.deepEqual(f.errors, []); return f }
  catch (error) { f.unmount(); throw error }
}
async function use(run, render = false) { const f = await ready(render); try { await run(f); assert.deepEqual(f.errors, []) } finally { f.unmount() } }
async function resolveLoad(f, page = 1, total = 25) { const pending = f.state.load(page); f.calls.at(-1).resolve(pageFor(page, total)); await pending; await flush() }
const results = []
async function test(name, run) { try { await run(); results.push({ name, passed: true }) } catch (error) { results.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) } }

try {
  await test('initial bounded query and page 2/server last-page clamp', () => use(async f => {
    assert.deepEqual(f.calls[0].query, { page: 1, pageSize: 10, status: '', priority: '', search: '' })
    assert.equal(f.state.bugs.length, 10); await resolveLoad(f, 2); assert.equal(f.state.bugs[0].id, 'synthetic-bug-11')
    const over = f.state.load(99); f.calls.at(-1).resolve(pageFor(3)); await over; await flush(); assert.equal(f.state.page, 3); assert.equal(f.state.bugs.length, 5)
  }))
  await test('search/status/priority and route ID reset page; query ABA retires even without a new request', () => use(async f => {
    const old = f.state.load(2); f.state.search = 'other'; f.state.search = ''; f.calls[1].resolve(pageFor(2)); await old; await flush()
    assert.equal(f.state.result, null); assert.equal(f.state.loading, false)
    f.state.search = ' exact '; f.state.status = 'resolved'; f.state.priority = 'high'
    const current = f.state.load(); assert.deepEqual(f.calls.at(-1).query, { page: 1, pageSize: 10, status: 'resolved', priority: 'high', search: 'exact' })
    f.calls.at(-1).resolve(pageFor(1)); await current
    f.route.params.bugId = 'synthetic-bug-4'; await flush(); assert.equal(f.calls.at(-1).query.page, 1); assert.equal(f.calls.at(-1).query.search, 'synthetic-bug-4')
  }))
  await test('older success/error/finally cannot publish or clear current loading; current error is not empty success', () => use(async f => {
    const old = f.state.load(2), latest = f.state.load(3); f.calls[1].reject(new Error('stale')); await old; await flush()
    assert.equal(f.state.loading, true); assert.equal(f.state.loadError, '')
    f.calls[2].resolve(pageFor(3)); await latest; await flush(); assert.equal(f.state.page, 3)
    const failed = f.state.load(); f.calls.at(-1).reject(new Error('current read failure')); await failed; await flush()
    assert.equal(f.state.result, null); assert.equal(f.state.loadError, 'current read failure'); assert.equal(f.state.loading, false)
  }))
  await test('all unsaved editor fields/comments/closing disposition survive pages and refresh without submit', () => use(async f => {
    const item = f.state.bugs[0]; Object.assign(item, { status: 'implementation', priority: 'high', assignee: 'synthetic staff', adminNotes: 'draft notes', fixCommit: 'draft commit', regressionTest: 'named test', deployedVersion: 'deployed marker', duplicateOf: 'synthetic-bug-8' })
    f.state.comments[item.id] = 'unsent comment'; f.state.openClosure(item); f.state.closureDrafts[item.id] = 'duplicate'
    await resolveLoad(f, 2); await resolveLoad(f, 1)
    for (const key of ['status', 'priority', 'assignee', 'adminNotes', 'fixCommit', 'regressionTest', 'deployedVersion', 'duplicateOf']) assert.equal(f.state.bugs[0][key], item[key])
    assert.equal(f.state.comments[item.id], 'unsent comment'); assert.equal(f.state.closing[item.id], true); assert.equal(f.state.closureDrafts[item.id], 'duplicate'); assert.equal(f.writes.length, 0)
    await resolveLoad(f, 1); assert.equal(f.state.bugs[0].adminNotes, 'draft notes'); assert.equal(f.state.comments[item.id], 'unsent comment')
  }))
  await test('actor/token/perms ABA retires drafts and both read/write late outcomes', () => use(async f => {
    const item = f.state.bugs[0]; item.adminNotes = 'private synthetic actor draft'; f.state.comments[item.id] = 'synthetic actor comment'
    const save = f.state.save(item), pendingRead = f.state.load(2)
    f.platformState.token = 'synthetic-token-B'; f.platformState.token = 'synthetic-token-A'
    assert.equal(f.state.bugs.length, 0); assert.equal(f.state.comments[item.id], undefined)
    f.writes[0].resolve(row(1)); f.calls[1].resolve(pageFor(2)); await save; await pendingRead; await flush()
    assert.equal(f.state.notice, ''); assert.equal(f.state.result, null)
    f.calls.at(-1).resolve(pageFor()); await flush(); assert.equal(f.state.bugs[0].adminNotes, '')
    f.platformState.account.permissionVersion = 2; f.platformState.account.permissionVersion = 1; assert.equal(f.state.result, null)
  }))
  await test('unmount blocks late success/error/finally and old submitted business is not falsely cancelled', async () => {
    const f = await ready(); const pending = f.state.save(f.state.bugs[0]); assert.equal(f.writes.length, 1); f.unmount(); f.writes[0].reject(new Error('late write')); await pending; await flush(); assert.equal(f.state.notice, '')
    const g = await ready(); const old = g.state.load(2); g.unmount(); g.calls[1].reject(new Error('late read')); await old; assert.equal(g.state.result, null); assert.equal(g.state.loadError, '')
  })
  await test('save keeps CAS and other report drafts then re-reads current filter/clamped page', () => use(async f => {
    const item = f.state.bugs[0], other = f.state.bugs[1]; other.adminNotes = 'other unsaved'; f.state.comments[other.id] = 'other comment'
    item.adminNotes = 'submitted'; f.state.comments[item.id] = 'submitted comment'; const pending = f.state.save(item)
    assert.equal(f.writes[0].body.expectedVersion, 9); assert.equal(f.writes[0].body.adminNotes, 'submitted'); assert.equal(f.writes[0].body.comment, 'submitted comment')
    f.writes[0].resolve({ ...row(1), adminNotes: 'submitted' }); await flush(); assert.equal(f.calls.at(-1).query.page, 1)
    const readback = pageFor(1, 25, 10); readback.items[0] = { ...row(1), adminNotes: 'submitted' }; f.calls.at(-1).resolve(readback); await pending; await flush()
    assert.equal(f.state.comments[item.id], ''); assert.equal(f.state.bugs[1].adminNotes, 'other unsaved'); assert.equal(f.state.comments[other.id], 'other comment')
  }))
  await test('CAS conflict preserves editing/comment/closure, and changed server row is not silently rebased', () => use(async f => {
    const item = f.state.bugs[0]; item.adminNotes = 'keep stale draft'; f.state.comments[item.id] = 'keep comment'; f.state.openClosure(item); f.state.closureDrafts[item.id] = 'rejected'
    const failed = f.state.save(item); f.writes[0].reject(Object.assign(new Error('version conflict'), { status: 409, code: 'version_conflict' })); await failed; await flush()
    assert.equal(f.state.bugs[0].adminNotes, 'keep stale draft'); assert.equal(f.state.comments[item.id], 'keep comment')
    const reload = f.state.load(); const newer = pageFor(1, 25, 10); newer.items[0] = { ...row(1), adminNotes: 'remote update' }; f.calls.at(-1).resolve(newer); await reload
    const again = f.state.save(f.state.bugs[0]); assert.equal(f.writes[1].body.expectedVersion, 9); f.writes[1].reject(new Error('still old basis')); await again
    assert.equal(f.state.bugs[0].adminNotes, 'keep stale draft'); assert.equal(f.state.closureDrafts[item.id], 'rejected')
    f.state.discardDraft(f.state.bugs[0]); assert.equal(f.state.bugs[0].adminNotes, 'remote update'); assert.equal(f.state.comments[item.id], '')
  }))
  await test('closing retains three exact dispositions/evidence and never drops JSON-omitted fields/comments', async () => {
    for (const disposition of ['fixed_verified', 'duplicate', 'rejected']) await use(async f => {
      const item = f.state.bugs[0]; item.fixCommit = 'synthetic commit'; item.regressionTest = 'named regression'; item.deployedVersion = 'deployed'; item.duplicateOf = 'synthetic-bug-5'; f.state.comments[item.id] = 'reason'; f.state.openClosure(item); f.state.closureDrafts[item.id] = disposition
      const pending = f.state.closeBug(item), write = f.writes.at(-1)
      assert.equal(write.body.status, 'closed'); assert.equal(write.body.closureDisposition, disposition); assert.equal(write.body.expectedVersion, 9)
      assert.equal(write.body.fixCommit, disposition === 'fixed_verified' ? 'synthetic commit' : undefined)
      assert.equal(write.body.regressionTest, disposition === 'fixed_verified' ? 'named regression' : undefined)
      assert.equal(write.body.deployedVersion, disposition === 'fixed_verified' ? 'deployed' : undefined)
      assert.equal(write.body.duplicateOf, disposition === 'duplicate' ? 'synthetic-bug-5' : '')
      assert.equal(write.body.comment, disposition === 'rejected' ? 'reason' : undefined)
      assert.equal(Object.hasOwn(write.body, 'verifiedBy'), false); assert.equal(Object.hasOwn(write.body, 'verifiedAt'), false)
      write.resolve({ ...item, status: 'closed', closureDisposition: disposition }); await flush()
      const readback = pageFor(1, 25, 10); readback.items[0] = { ...row(1), status: 'closed', closureDisposition: disposition }
      f.calls.at(-1).resolve(readback); await pending; await flush()
      assert.equal(f.state.comments[item.id], disposition === 'rejected' ? '' : 'reason')
      if (disposition !== 'fixed_verified') {
        assert.equal(f.state.bugs[0].fixCommit, 'synthetic commit'); assert.equal(f.state.bugs[0].regressionTest, 'named regression'); assert.equal(f.state.bugs[0].deployedVersion, 'deployed')
        assert.equal(f.state.draftConflict(f.state.bugs[0]), true, 'unsubmitted evidence retains original CAS')
      }
    })
  })
  await test('closing exits current filter and accepts server clamped page while other cached drafts survive', () => use(async f => {
    f.state.status = 'new'; await resolveLoad(f, 3); const closed = f.state.bugs[0], other = f.state.bugs[1]
    other.adminNotes = 'keep another report'; f.state.comments[other.id] = 'other unsent comment'
    f.state.openClosure(closed); f.state.closureDrafts[closed.id] = 'rejected'; f.state.comments[closed.id] = 'evidence insufficient'
    const close = f.state.closeBug(closed); f.writes.at(-1).resolve({ ...closed, status: 'closed' }); await flush()
    assert.equal(f.calls.at(-1).query.status, 'new'); assert.equal(f.calls.at(-1).query.page, 3)
    f.calls.at(-1).resolve(pageFor(2, 20)); await close; await flush(); assert.equal(f.state.page, 2); assert.equal(f.state.bugs.length, 10)
    f.state.status = ''; await resolveLoad(f, 3); assert.equal(f.state.bugs[1].adminNotes, 'keep another report'); assert.equal(f.state.comments[other.id], 'other unsent comment')
  }))
  await test('unchanged row under changed platform version cannot silently lift a cached draft CAS', () => use(async f => {
    const item = f.state.bugs[0]; item.adminNotes = 'same-row global version draft'; await resolveLoad(f, 2)
    const back = f.state.load(1); f.calls.at(-1).resolve(pageFor(1, 25, 10)); await back
    const pending = f.state.save(f.state.bugs[0]); assert.equal(f.writes[0].body.expectedVersion, 9)
    f.writes[0].reject(Object.assign(new Error('global conflict'), { status: 409, code: 'version_conflict' })); await pending
    assert.equal(f.state.bugs[0].adminNotes, 'same-row global version draft')
    f.state.discardDraft(f.state.bugs[0]); const fresh = f.state.save(f.state.bugs[0]); assert.equal(f.writes[1].body.expectedVersion, 10)
    f.writes[1].reject(new Error('bounded mock stop')); await fresh
  }))
  await test('missing disposition/permission/current row or invalid page metadata cannot execute or fake completeness', () => use(async f => {
    const stale = f.state.bugs[0]; await f.state.closeBug(stale); assert.equal(f.writes.length, 0)
    f.allowed.value = false; await f.state.save(stale); f.state.openClosure(stale); await f.state.closeBug(stale); assert.equal(f.writes.length, 0); assert.equal(f.state.closing[stale.id], false); f.allowed.value = true
    await resolveLoad(f, 2); await f.state.save(stale); assert.equal(f.writes.length, 0)
    const invalid = f.state.load(); f.calls.at(-1).resolve({ ...pageFor(2), items: [] }); await invalid; assert.equal(f.state.result, null); assert.match(f.state.loadError, /分页响应无效/)
    const unsafe = f.state.load(1); f.calls.at(-1).resolve({ ...pageFor(), total: Number.MAX_SAFE_INTEGER + 1 }); await unsafe; assert.equal(f.state.result, null)
  }))
  await test('closure retains confirmed assignment/notes without submitting or dropping different drafts', () => use(async f => {
    const initial = f.state.load(1); const confirmed = { ...row(1), assignee: 'confirmed-owner', adminNotes: 'confirmed-notes' }
    const initialPage = pageFor(); initialPage.items[0] = confirmed; f.calls.at(-1).resolve(initialPage); await initial; await flush()
    const item = f.state.bugs[0]; item.assignee = 'unsaved-owner'; item.adminNotes = 'unsaved-notes'; f.state.comments[item.id] = 'close reason'
    f.state.openClosure(item); f.state.closureDrafts[item.id] = 'rejected'
    const pending = f.state.closeBug(item)
    assert.equal(f.writes.at(-1).body.assignee, 'confirmed-owner'); assert.equal(f.writes.at(-1).body.adminNotes, 'confirmed-notes')
    assert.equal(f.writes.at(-1).body.expectedVersion, 9)
    const closed = { ...confirmed, status: 'closed', closureDisposition: 'rejected' }; f.writes.at(-1).resolve(closed); await flush()
    const next = pageFor(1, 25, 10); next.items[0] = closed; f.calls.at(-1).resolve(next); await pending; await flush()
    assert.equal(f.state.bugs[0].assignee, 'unsaved-owner'); assert.equal(f.state.bugs[0].adminNotes, 'unsaved-notes')
    assert.equal(f.state.comments[item.id], ''); assert.ok(f.state.draftConflict(f.state.bugs[0]))
  }))
  await test('actual full SFC template passes server page rows intact and buttons/input invoke real handlers', () => use(async f => {
    assert.equal(walk(f.root).filter(child => child.props.class === 'bug-row').length, 10)
    button(f.root, '下一页').props.onClick(); f.calls.at(-1).resolve(pageFor(2)); await flush()
    assert.equal(f.state.page, 2); assert.equal(walk(f.root).filter(child => child.props.class === 'bug-row').length, 10)
    const submitted = f.state.save(f.state.bugs[0]); await flush()
    assert.equal(walk(f.root).find(child => child.tag === 'fieldset').props.disabled, true)
    await f.state.save(f.state.bugs[0]); assert.equal(f.writes.length, 1, 'disabled editor is also guarded by the real handler')
    f.writes[0].reject(new Error('controlled write failure')); await submitted; await flush()
    assert.equal(walk(f.root).find(child => child.tag === 'fieldset').props.disabled, false)
    const filter = walk(f.root).find(child => child.tag === 'input' && String(child.props.placeholder).startsWith('编号'))
    filter.value = 'real memory input'; filter.dispatch('input'); assert.equal(f.state.search, 'real memory input')
    button(f.root, '查询').props.onClick(); assert.equal(f.calls.at(-1).query.search, 'real memory input')
    f.calls.at(-1).reject(new Error('visible error')); await flush(); assert.ok(text(f.root).includes('visible error')); assert.ok(!text(f.root).includes('暂无符合筛选条件'))
  }, true))
} finally {
  for (const [key, descriptor] of previousGlobals) { if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key] }
}
for (const result of results) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
console.log('RESULT ' + JSON.stringify({ cases: results, passed: results.filter(result => result.passed).length, failed: results.filter(result => !result.passed).length,
  sourceSha256: createHash('sha256').update(source).digest('hex').toUpperCase(), actualSfcScript: true, actualSfcTemplate: true, realApiCalls: 0, networkCalls: 0 }))
if (results.some(result => !result.passed)) process.exitCode = 1
