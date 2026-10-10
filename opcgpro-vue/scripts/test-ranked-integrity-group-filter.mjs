import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const root = path.resolve(import.meta.dirname, '..')
const requireDependency = createRequire(process.env.L12_NODE_DEPENDENCY_PACKAGE || path.join(root, 'package.json'))
const Vue = requireDependency('vue')
const { parse, compileScript, compileTemplate } = requireDependency('@vue/compiler-sfc')
const ts = requireDependency('typescript')
const panelPath = process.env.L12_GROUP_PANEL_SOURCE || path.join(root, 'src/l12/site/AdminRankedIntegrityPanel.vue')
const pagedPath = path.join(root, 'src/l12/site/PagedCollection.vue')
const ownPath = path.join(root, 'scripts/test-ranked-integrity-group-filter.mjs')
const fingerprint = file => ({ file: path.relative(root, file).replaceAll(path.sep, '/'),
  sha256: createHash('sha256').update(fs.readFileSync(file)).digest('hex') })
const before = [panelPath, pagedPath, ownPath].map(fingerprint)
const warnings = []

// A small in-memory Vue renderer, not a browser or a copy of panel logic. It
// renders the actual SFC templates and supports Vue's real input directives.
function node(tag, text = '') {
  return { tag, text, children: [], parent: null, props: {}, listeners: new Map(), scopes: [],
    tagName: tag.toUpperCase(), value: '', checked: false,
    addEventListener(name, handler) { this.listeners.set(name, handler) },
    removeEventListener(name) { this.listeners.delete(name) },
  }
}
function detach(child) {
  if (child.parent) {
    const index = child.parent.children.indexOf(child)
    if (index >= 0) child.parent.children.splice(index, 1)
    child.parent = null
  }
}
const renderer = Vue.createRenderer({
  createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) {
    detach(child); child.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
  },
  remove: detach, parentNode: child => child.parent,
  nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] || null,
  setText: (child, text) => { child.text = text },
  setElementText(child, text) { for (const old of child.children) old.parent = null; child.children = []; child.text = text },
  patchProp(child, name, previous, value) {
    child.props[name] = value
    if (name === 'value') { child.value = value ?? ''; child._value = value }
    if (name === 'type') child.type = value
    if (name === 'checked') child.checked = value
  },
  setScopeId: (child, id) => child.scopes.push(id),
})
function text(child) { return child.tag === '#comment' ? '' : child.text + child.children.map(text).join('') }
function findAll(child, predicate) { return [...(predicate(child) ? [child] : []), ...child.children.flatMap(item => findAll(item, predicate))] }
const hasClass = (child, value) => String(child.props.class || '').split(/\s+/).includes(value)
function component(vnode, type) {
  if (vnode?.component?.type === type) return vnode.component
  if (vnode?.component) { const found = component(vnode.component.subTree, type); if (found) return found }
  if (Array.isArray(vnode?.children)) for (const child of vnode.children) { const found = component(child, type); if (found) return found }
}
function evaluate(code, imports, filename) {
  const compiled = ts.transpileModule(code, { fileName: filename,
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
  const errors = (compiled.diagnostics || []).filter(error => error.category === ts.DiagnosticCategory.Error)
  assert.equal(errors.length, 0, errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, '\n')).join('\n'))
  const module = { exports: {} }
  new Function('require', 'exports', 'module', compiled.outputText)(id => {
    if (id === 'vue') return Vue
    if (Object.hasOwn(imports, id)) return imports[id]
    throw new Error(`Unexpected SFC dependency ${id} in ${filename}`)
  }, module.exports, module)
  return module.exports
}
function loadSfc(filename, imports = {}) {
  const { descriptor, errors } = parse(fs.readFileSync(filename, 'utf8'), { filename })
  assert.deepEqual(errors, [])
  const id = createHash('sha256').update(path.basename(filename)).digest('hex').slice(0, 8)
  const script = compileScript(descriptor, { id })
  const template = compileTemplate({ id, filename, source: descriptor.template.content,
    scoped: descriptor.styles.some(style => style.scoped),
    compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
  assert.deepEqual(template.errors, [])
  const result = evaluate(script.content, imports, filename).default
  result.render = evaluate(template.code, {}, filename + '.template').render
  return result
}
const PagedCollection = loadSfc(pagedPath)
const Actions = { props: ['rows', 'selected'], render() { return Vue.h('div', { class: 'selected-fixture', 'data-selected': this.selected.join(',') }, `${this.selected.length}`) } }
const Picker = { props: ['modelValue', 'label'], emits: ['update:modelValue', 'change'], render() { return Vue.h('span', this.label) } }
const first = { id: 'alpha', name: 'Alpha' }, second = { id: 'beta', name: 'Beta' }
const otherFirst = { id: 'gamma', name: 'Gamma' }, otherSecond = { id: 'delta', name: 'Delta' }
const row = (id, a, b, winner = 0, disposition = 'unreviewed', signal = 'very-short-match') => ({
  id: `audit-${id}`, matchId: id, seasonId: 'synthetic', firstAccountId: a.id, firstPlayer: a.name,
  secondAccountId: b.id, secondPlayer: b.name, winner, durationMs: 60000, meaningfulCommandCount: 3,
  conclusionKind: 'surrender', finalRound: 1, signals: [{ code: signal, label: '合成风险信号' }],
  reviewRecommended: true, effectiveDisposition: disposition, enforcement: 'none', createdAt: '2026-10-08T00:00:00Z',
})
const rows = []
for (let i = 0; i < 61; i++) {
  rows.push(row(`A-${i}`, i % 2 ? second : first, i % 2 ? first : second, i % 2 ? 1 : 0,
    ['normal', 'insufficient', 'system-error', 'confirmed', 'review'][i] || 'unreviewed'))
  if (i < 20) rows.push(row(`B-${i}`, otherFirst, otherSecond))
  if (i < 6) rows.push(row(`C-${i}`, first, { id: `opponent-${i}`, name: `Other${i}` }, 0, 'unreviewed',
    ['unilateral-score-transfer', 'repeated-padded-transfer', 'linked-loser-cluster'][Math.floor(i / 2)]))
}
rows.push(row('LOSER', first, { id: 'loser-opponent', name: 'Opponent' }, 1, 'unreviewed', 'unilateral-score-transfer'))
rows.push(row('NO-TRANSFER', first, { id: 'no-transfer', name: 'Opponent' }))
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
const settle = async () => { await Promise.resolve(); await Vue.nextTick(); await Promise.resolve(); await Vue.nextTick() }
const fixtures = []
async function fixture() {
  const queries = [], plans = []
  const Panel = loadSfc(panelPath, {
    './PagedCollection.vue': { __esModule: true, default: PagedCollection }, './RankedIntegrityActions.vue': { __esModule: true, default: Actions },
    './AdminAccountPicker.vue': { __esModule: true, default: Picker }, '../rankedIntegrity': { integrityLabel: value => value },
    '@/l12/platform': { adminApi: { rankedIntegrityAudits(query) { queries.push({ ...query }); return plans.length ? plans.shift() : Promise.resolve(structuredClone(rows)) } } },
  })
  const container = node('root')
  const app = renderer.createApp(Panel)
  app.config.warnHandler = message => warnings.push(message)
  app.config.errorHandler = error => { throw error }
  app.mount(container)
  await settle()
  const instance = app._instance, state = instance.setupState
  let mounted = true
  const result = { app, instance, state, container, queries, plans,
    paged: () => component(instance.subTree, PagedCollection),
    visible: () => findAll(container, child => hasClass(child, 'integrity-row')).map(article => text(findAll(article, child => child.tag === 'code')[0])),
    buttons: () => findAll(container, child => child.tag === 'button'),
    async click(label) {
      const button = result.buttons().find(child => label instanceof RegExp ? label.test(text(child)) : text(child) === label)
      assert.ok(button, `Button absent: ${label}`); assert.ok(!button.props.disabled, `Disabled button: ${label}`)
      assert.equal(typeof button.props.onClick, 'function')
      button.props.onClick({ target: button, currentTarget: button }); await settle()
    },
    unmount() { if (mounted) { app.unmount(); mounted = false } },
  }
  fixtures.push(result)
  return result
}
let passed = 0
const checks = []
async function check(name, action) { await action(); passed++; checks.push(name) }
// runtime-dom's text-input directive only needs an inactive input sentinel from
// this synthetic host; it does not create or use a browser/DOM implementation.
const previousDocument = Object.getOwnPropertyDescriptor(globalThis, 'document')
if (!previousDocument) Object.defineProperty(globalThis, 'document', { value: { activeElement: null }, configurable: true })
try {
  const f = await fixture()
  await check('real Panel supplies the loaded items to the real PagedCollection', async () => {
    assert.equal(f.paged().props.items.length, 89)
    assert.equal(f.paged().setupState.page, 1)
    assert.equal(f.paged().setupState.visible.length, 10)
    assert.equal(f.visible().length, 10)
    assert.equal(f.queries.length, 1)
    assert.equal(f.queries[0].limit, 300)
  })
  await check('click group from display page three filters actual child items and resets to page one', async () => {
    await f.click('下一页'); await f.click('下一页')
    assert.equal(f.paged().setupState.page, 3)
    assert.ok(f.visible().some(id => id.startsWith('B-')))
    await f.click(/^Alpha \/ Beta/)
    assert.equal(f.paged().props.items.length, 61)
    assert.ok(f.paged().props.items.every(item => item.matchId.startsWith('A-')))
    assert.equal(f.paged().setupState.page, 1)
    assert.equal(f.paged().setupState.pages, 7)
    assert.ok(f.visible().every(id => id.startsWith('A-')))
    assert.equal(f.queries.length, 1)
  })
  await check('61 members are reachable over seven real pages while selection remains 50', async () => {
    const seen = new Set(f.visible())
    for (let page = 1; page < 7; page++) { await f.click('下一页'); for (const id of f.visible()) seen.add(id) }
    assert.equal(seen.size, 61)
    assert.ok([...seen].every(id => id.startsWith('A-')))
    assert.equal(f.state.selected.length, 50)
  })
  await check('same-group click from page seven resets real child page to one', async () => {
    await f.click(/^Alpha \/ Beta/)
    assert.equal(f.paged().setupState.page, 1)
    assert.equal(f.paged().props.items.length, 61)
  })
  await check('all four terminal states render disabled and are absent from bulk selection', async () => {
    for (let index = 0; index < 4; index++) {
      const article = findAll(f.container, child => hasClass(child, 'integrity-row')).find(item => text(findAll(item, child => child.tag === 'code')[0]) === `A-${index}`)
      const input = findAll(article, child => child.tag === 'input')[0]
      assert.equal(input.props.disabled, true)
      assert.equal(input.checked, false)
      assert.ok(!f.state.selected.includes(`A-${index}`))
    }
    assert.ok(f.state.selected.includes('A-4'), 'review remains selectable')
  })
  await check('another group from a later page remounts pager and reports full group count', async () => {
    await f.click('下一页'); await f.click('下一页')
    await f.click(/^Gamma \/ Delta/)
    assert.equal(f.paged().setupState.page, 1)
    assert.equal(f.paged().setupState.pages, 2)
    assert.equal(f.paged().props.items.length, 20)
    assert.ok(f.visible().every(id => id.startsWith('B-')))
  })
  await check('beneficiary uses the three signal codes and winning-account member IDs', async () => {
    await f.click(/^Alpha · 涉及/)
    assert.deepEqual(f.paged().props.items.map(item => item.matchId), ['C-0', 'C-1', 'C-2', 'C-3', 'C-4', 'C-5'])
    assert.equal(f.state.selected.length, 6)
    assert.equal(f.paged().setupState.page, 1)
    const chip = findAll(f.container, child => hasClass(child, 'group-filter'))[0]
    assert.match(text(chip), /6条（当前查询结果）/)
  })
  await check('clear restores 89 query rows and page one without another query', async () => {
    await f.click('清除归组筛选')
    assert.equal(f.paged().props.items.length, 89)
    assert.equal(f.paged().setupState.page, 1)
    assert.equal(f.state.activeGroup, null)
    assert.equal(f.state.selected.length, 0)
    assert.equal(f.queries.length, 1)
    assert.match(text(findAll(f.container, child => hasClass(child, 'query-scope'))[0]), /最多300条.*不代表完整对局历史/)
  })
  await check('query reload clears previous group and selected IDs', async () => {
    await f.click(/^Alpha \/ Beta/)
    f.plans.push(Promise.resolve(rows.filter(item => item.matchId.startsWith('B-')).slice(0, 6)))
    await f.state.load(); await settle()
    assert.equal(f.state.activeGroup, null)
    assert.equal(f.state.selected.length, 0)
    assert.equal(f.paged().props.items.length, 6)
    assert.ok(f.visible().every(id => id.startsWith('B-')))
  })
  await check('successful zero-result query renders empty current scope', async () => {
    f.plans.push(Promise.resolve([])); await f.state.load(); await settle()
    assert.equal(f.paged().props.items.length, 0)
    assert.equal(f.visible().length, 0)
    assert.equal(findAll(f.container, child => hasClass(child, 'empty')).length, 1)
    assert.equal(f.state.notice, '')
  })
  await check('failed query keeps no old rows and is not presented as an empty history', async () => {
    f.plans.push(Promise.reject(new Error('synthetic read error'))); await f.state.load(); await settle()
    assert.equal(f.paged().props.items.length, 0)
    assert.equal(f.visible().length, 0)
    assert.equal(findAll(f.container, child => hasClass(child, 'empty')).length, 0)
    assert.equal(f.state.notice, 'synthetic read error')
    assert.match(text(findAll(f.container, child => hasClass(child, 'query-scope'))[0]), /当前查询加载失败/)
  })
  await check('ABA old error/finally cannot clear current loading or overwrite the newest A result', async () => {
    const oldA = deferred(), middleB = deferred(), newA = deferred()
    f.plans.push(oldA.promise, middleB.promise, newA.promise)
    f.state.accountId = 'alpha'; const firstLoad = f.state.load()
    f.state.accountId = 'gamma'; const secondLoad = f.state.load()
    f.state.accountId = 'alpha'; const thirdLoad = f.state.load()
    await settle()
    assert.equal(f.state.loading, true)
    middleB.reject(new Error('stale B failure')); await secondLoad; await settle()
    assert.equal(f.state.loading, true)
    assert.equal(f.state.notice, '')
    newA.resolve(rows.filter(item => item.matchId.startsWith('A-')).slice(4, 7)); await thirdLoad; await settle()
    assert.equal(f.visible().length, 3)
    assert.equal(f.state.loading, false)
    oldA.resolve(rows); await firstLoad; await settle()
    assert.equal(f.paged().props.items.length, 3)
    assert.ok(f.visible().every(id => id.startsWith('A-')))
    assert.equal(f.state.activeGroup, null)
    assert.equal(f.state.notice, '')
    assert.deepEqual(f.queries.slice(-3).map(query => query.accountId), ['alpha', 'gamma', 'alpha'])
  })
  await check('unmount ignores late success without mutating captured real setup state', async () => {
    const unmounted = await fixture(), pending = deferred()
    unmounted.plans.push(pending.promise)
    const load = unmounted.state.load(); await settle()
    unmounted.unmount()
    pending.resolve(rows); await load; await settle()
    assert.equal(unmounted.state.rows.length, 0)
    assert.equal(unmounted.state.notice, '')
    assert.equal(unmounted.state.loading, true)
    assert.equal(unmounted.container.children.length, 0)
  })
  await check('unmount ignores late failure and its finally block', async () => {
    const unmounted = await fixture(), pending = deferred()
    unmounted.plans.push(pending.promise)
    const load = unmounted.state.load(); await settle()
    unmounted.unmount()
    pending.reject(new Error('unmounted failure')); await load; await settle()
    assert.equal(unmounted.state.rows.length, 0)
    assert.equal(unmounted.state.notice, '')
    assert.equal(unmounted.state.loading, true)
  })
  assert.ok(fixtures.every(item => item.queries.every(query => query.limit === 300)), 'existing query limit changed')
  assert.deepEqual(warnings, [])
  assert.deepEqual([panelPath, pagedPath, ownPath].map(fingerprint), before, 'source changed during test')
  if (process.env.L12_GROUP_TEST_OUT) {
    fs.mkdirSync(process.env.L12_GROUP_TEST_OUT, { recursive: true })
    fs.writeFileSync(path.join(process.env.L12_GROUP_TEST_OUT, 'result.json'), JSON.stringify({ status: 'passed', passed, checks, sources: before, warnings }, null, 2))
  }
  console.log(`Ranked integrity portable real SFC mount: ${passed}/${passed} passed; no browser, network or added dependencies.`)
} catch (error) {
  if (process.env.L12_GROUP_TEST_OUT) {
    fs.mkdirSync(process.env.L12_GROUP_TEST_OUT, { recursive: true })
    fs.writeFileSync(path.join(process.env.L12_GROUP_TEST_OUT, 'result.json'), JSON.stringify({ status: 'failed', passed, checks, sources: before, warnings, error: String(error) }, null, 2))
  }
  throw error
} finally {
  for (const item of fixtures) item.unmount()
  if (!previousDocument) delete globalThis.document
}
