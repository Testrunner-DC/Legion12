import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'

const root = path.resolve(import.meta.dirname, '..')
const require = createRequire(process.env.L12_NODE_DEPENDENCY_PACKAGE || path.join(root, 'package.json'))
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const pagePath = path.join(root, 'src/l12/site/AdminAccountDetailPage.vue')
const apiPath = path.join(root, 'src/l12/platform.ts')
const riskPath = path.join(root, 'src/l12/site/useAdminRiskAction.ts')
const files = [pagePath, apiPath, riskPath]
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const before = files.map(hash)
const errors = [], warnings = []
const flush = async () => { for (let i = 0; i < 8; i++) { await Promise.resolve(); await Vue.nextTick() } }
function evaluate(code, imports) {
  const result = ts.transpileModule(code, { reportDiagnostics: true, compilerOptions: {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS,
  } })
  assert.equal((result.diagnostics || []).filter(d => d.category === ts.DiagnosticCategory.Error).length, 0)
  const module = { exports: {} }
  new Function('require', 'module', 'exports', result.outputText)(id => {
    if (id === 'vue') return Vue
    assert.ok(Object.hasOwn(imports, id), `Unexpected import ${id}`)
    return imports[id]
  }, module, module.exports)
  return module.exports
}
const { useAdminRiskAction } = evaluate(fs.readFileSync(riskPath, 'utf8'), {})

// Exercise the actual three API members and their existing command helpers,
// without evaluating unrelated platform globals or copying request behavior.
const platformSource = ts.createSourceFile(apiPath, fs.readFileSync(apiPath, 'utf8'), ts.ScriptTarget.Latest, true)
const helperNames = new Set(['commandKey', 'commandBody'])
const helpers = platformSource.statements.filter(n => ts.isFunctionDeclaration(n) && helperNames.has(n.name?.text))
const apiDeclaration = platformSource.statements.flatMap(n => ts.isVariableStatement(n) ? [...n.declarationList.declarations] : [])
  .find(n => n.name.getText(platformSource) === 'adminApi')
assert.ok(apiDeclaration && ts.isObjectLiteralExpression(apiDeclaration.initializer))
const names = ['accountRankedRestriction', 'setAccountRankedRestriction', 'clearAccountRankedRestriction']
const members = apiDeclaration.initializer.properties.filter(n => names.includes(n.name?.getText(platformSource)))
assert.equal(helpers.length, 2); assert.equal(members.length, 3)
const apiCode = `import { platformRequest } from './wire';\n${helpers.map(n => n.getText(platformSource)).join('\n')}\nexport const client = {${members.map(n => n.getText(platformSource)).join(',\n')}}`
const apiCalls = []
const wire = evaluate(apiCode, { './wire': { async platformRequest(url, options) {
  apiCalls.push({ url, options }); if (url.includes('old-service')) throw new Error('HTTP 404'); return {}
} } }).client
const command = { reason: 'synthetic', expectedVersion: 5, idempotencyKey: 'same-attempt' }
await wire.accountRankedRestriction('a/b')
await wire.setAccountRankedRestriction('a/b', 7, command)
await wire.clearAccountRankedRestriction('a/b', command)
assert.equal(apiCalls[0].url, '/api/admin/accounts/a%2Fb/ranked-restriction')
assert.equal(apiCalls[1].options.method, 'PUT')
assert.deepEqual(JSON.parse(apiCalls[1].options.body), { ...command, restrictionDays: 7 })
assert.equal(apiCalls[2].url, '/api/admin/accounts/a%2Fb/ranked-restriction/clear')
assert.equal(apiCalls[2].options.method, 'POST')
assert.deepEqual(JSON.parse(apiCalls[2].options.body), command)
await assert.rejects(wire.setAccountRankedRestriction('old-service', 7, command), /404/)
assert.equal(apiCalls.length, 4, 'old service must not trigger a status/whole-match fallback')

function node(tag, text = '') {
  return { tag, text, children: [], parent: null, props: {}, tagName: tag.toUpperCase(), value: '', listeners: new Map(),
    addEventListener(name, callback) { this.listeners.set(name, callback) }, removeEventListener(name) { this.listeners.delete(name) },
    getRootNode() { let root = this; while (root.parent) root = root.parent; return root },
    get options() { return this.children.filter(child => child.tag === 'option') },
  }
}
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
const renderer = Vue.createRenderer({
  createElement: tag => node(tag), createText: value => node('#text', value), createComment: value => node('#comment', value),
  insert(child, parent, anchor) { detach(child); child.parent = parent; const i = anchor ? parent.children.indexOf(anchor) : -1; if (i < 0) parent.children.push(child); else parent.children.splice(i, 0, child) },
  remove: detach, parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] || null,
  setText(child, value) { child.text = value }, setElementText(child, value) { child.children.forEach(c => { c.parent = null }); child.children = []; child.text = value },
  patchProp(child, name, previous, value) { child.props[name] = value; if (name === 'value') { child.value = value; child._value = value } if (['checked', 'multiple', 'selected', 'type', 'disabled'].includes(name)) child[name] = value },
  setScopeId() {},
})
const content = child => child.tag === '#comment' ? '' : child.text + child.children.map(content).join('')
const walk = child => [child, ...child.children.flatMap(walk)]
function button(root, label) { const matches = walk(root).filter(n => n.tag === 'button' && content(n) === label); assert.equal(matches.length, 1, label); return matches[0] }
async function fixture({ permitted = true, active = false, readFails = false, failOnce = false } = {}) {
  const calls = [], route = Vue.reactive({ params: { accountId: 'player-a' }, query: { tab: 'access' } })
  const risk = useAdminRiskAction()
  const platformState = Vue.reactive({ account: { id: 'admin' } })
  const adminApi = {
    async account(id) { return { id, username: 'Synthetic Player', role: 'player', permissionVersion: 3 } },
    async accountRankedRestriction(id) { if (readFails) throw new Error('HTTP 404'); return {
      accountId: id, version: 5, restrictionUntil: active ? new Date(Date.now() + 86400000).toISOString() : null,
      effectiveRestrictionUntil: active ? new Date(Date.now() + 86400000 * 14).toISOString() : null,
    } },
    async setAccountRankedRestriction(id, days, input) { calls.push({ kind: 'set', id, days, ...input }); if (failOnce) { failOnce = false; throw new Error('synthetic retry') } },
    async clearAccountRankedRestriction(id, input) { calls.push({ kind: 'clear', id, ...input }) },
  }
  const { descriptor, errors: parseErrors } = parse(fs.readFileSync(pagePath, 'utf8'))
  assert.deepEqual(parseErrors, [])
  const script = compileScript(descriptor, { id: 'account-ranked-restriction' })
  const component = evaluate(script.content, {
    '../horizontalScrollArrows': { vHorizontalScrollArrows: {} },
    '@/l12/platform': { adminApi, platformState, hasPermission: () => permitted },
    'vue-router': { useRoute: () => route, useRouter: () => ({ push: async () => {} }) },
    './useAdminRiskAction': { useAdminRiskAction: () => risk },
    './AdminRiskActionDialog.vue': { __esModule: true, default: { render: () => null } },
  }).default
  const template = compileTemplate({ source: descriptor.template.content, filename: pagePath, id: 'account-ranked-restriction', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  component.render = evaluate(template.code, {}).render
  const app = renderer.createApp(component)
  app.component('router-link', { render() { return Vue.h('a', this.$slots.default?.()) } })
  app.config.errorHandler = error => errors.push(error)
  app.config.warnHandler = warning => warnings.push(warning)
  const root = node('root'); app.mount(root); await flush()
  return { root, state: app._instance.setupState, risk, route, calls, close: () => app.unmount() }
}
const brands = new Map(['Document', 'ShadowRoot'].map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]))
for (const name of brands.keys()) if (!globalThis[name]) Object.defineProperty(globalThis, name, { configurable: true, value: class {} })
let uiCases = 0
async function test(options, run) { const f = await fixture(options); try { await run(f); uiCases++ } finally { f.close() } }
try {
  await test({}, async f => {
    assert.equal(button(f.root, '设置排位禁赛').props.disabled, false)
    f.state.restrictionDays = 1.5; f.state.restrictionReason = 'synthetic'
    button(f.root, '设置排位禁赛').props.onClick(); await flush()
    assert.match(f.state.restrictionError, /整数/); assert.equal(f.risk.riskAction.value, null); assert.equal(f.calls.length, 0)
  })
  await test({ permitted: false }, async f => {
    assert.equal(button(f.root, '设置排位禁赛').props.disabled, true)
    f.state.changeRankedRestriction(false); assert.equal(f.risk.riskAction.value, null); assert.equal(f.calls.length, 0)
  })
  await test({ failOnce: true }, async f => {
    f.state.restrictionDays = 7; f.state.restrictionReason = 'captured reason'
    button(f.root, '设置排位禁赛').props.onClick(); await flush()
    assert.match(f.risk.riskAction.value.impact, /7 天/)
    f.state.restrictionDays = 21; f.state.restrictionReason = 'changed behind modal'
    await f.risk.confirmRiskAction(); assert.match(f.risk.riskError.value, /retry/)
    await f.risk.confirmRiskAction(); await flush()
    assert.equal(f.calls.length, 2); assert.deepEqual(f.calls[0], f.calls[1])
    assert.equal(f.calls[0].days, 7); assert.equal(f.calls[0].reason, 'captured reason'); assert.equal(f.calls[0].expectedVersion, 5)
  })
  await test({ active: true }, async f => {
    f.state.restrictionReason = 'clear only manual'
    button(f.root, '解除手工禁赛').props.onClick(); await flush()
    assert.match(f.risk.riskAction.value.impact, /完整性处置禁赛/)
    await f.risk.confirmRiskAction(); await flush()
    assert.equal(f.calls.length, 1); assert.equal(f.calls[0].kind, 'clear'); assert.equal(f.calls[0].id, 'player-a')
    assert.equal(Object.hasOwn(f.calls[0], 'days'), false)
  })
  await test({}, async f => {
    f.state.restrictionReason = 'captured target'; button(f.root, '设置排位禁赛').props.onClick(); await flush()
    const original = f.risk.riskAction.value
    f.route.params.accountId = 'player-b'; await flush()
    assert.equal(f.risk.riskAction.value, null)
    await assert.rejects(original.run(), /账号已切换/); assert.equal(f.calls.length, 0)
  })
  await test({ readFails: true }, async f => {
    assert.match(f.state.restrictionError, /404/); assert.equal(f.state.rankedRestriction, null)
    assert.equal(walk(f.root).filter(n => n.tag === 'button' && content(n) === '设置排位禁赛').length, 0)
    assert.equal(f.calls.length, 0)
  })
  assert.deepEqual(errors, []); assert.deepEqual(warnings, [])
} finally {
  for (const [name, descriptor] of brands) { if (descriptor) Object.defineProperty(globalThis, name, descriptor); else delete globalThis[name] }
}
assert.deepEqual(files.map(hash), before, 'regression must not modify source files')
console.log(`Account ranked restriction: actual API 4 cases and actual SFC ${uiCases} cases passed; no browser, service, or file output.`)
