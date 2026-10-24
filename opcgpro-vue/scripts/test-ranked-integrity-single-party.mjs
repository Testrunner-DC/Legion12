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
const source = process.env.L12_SINGLE_PARTY_ACTIONS_SOURCE || path.join(root, 'src/l12/site/RankedIntegrityActions.vue')
const apiSource = path.join(root, 'src/l12/rankedIntegrity.ts')
const sha = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const baseline = [source, apiSource].map(sha)
const warnings = []
const runtimeErrors = new Set()

// The real Vue SFC and input directives run in memory, with no browser, Vite,
// file output, copied application state machine or live HTTP endpoint.
function node(tag, text = '') {
  return { tag, text, children: [], parent: null, props: {}, tagName: tag.toUpperCase(),
    value: '', checked: false, multiple: false, selected: false, listeners: new Map(),
    getRootNode() { let root = this; while (root.parent) root = root.parent; return root },
    get options() { return this.children.filter(child => child.tag === 'option') },
    addEventListener(name, handler) { this.listeners.set(name, handler) },
    removeEventListener(name) { this.listeners.delete(name) },
  }
}
function detach(child) {
  if (!child.parent) return
  const siblings = child.parent.children
  const index = siblings.indexOf(child)
  if (index >= 0) siblings.splice(index, 1)
  child.parent = null
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
    if (['checked', 'multiple', 'selected', 'type', 'disabled'].includes(name)) child[name] = value
  },
  setScopeId() {},
})
const text = child => child.tag === '#comment' ? '' : child.text + child.children.map(text).join('')
function evaluate(code, imports, filename) {
  const compiled = ts.transpileModule(code, { fileName: filename,
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true })
  const errors = (compiled.diagnostics || []).filter(error => error.category === ts.DiagnosticCategory.Error)
  assert.equal(errors.length, 0, errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, '\n')).join('\n'))
  const module = { exports: {} }
  new Function('require', 'exports', 'module', compiled.outputText)(id => {
    if (id === 'vue') return Vue
    if (Object.hasOwn(imports, id)) return imports[id]
    throw new Error(`Unexpected dependency ${id} in ${filename}`)
  }, module.exports, module)
  return module.exports
}
const api = evaluate(fs.readFileSync(apiSource, 'utf8'), {
  './platform': { platformRequest() { throw new Error('Live platform request forbidden in this fixture') } },
}, apiSource)
const settle = async () => { await Promise.resolve(); await Vue.nextTick(); await Promise.resolve(); await Vue.nextTick() }

function apiFixture(reply) {
  const calls = []
  const module = evaluate(fs.readFileSync(apiSource, 'utf8'), {
    './platform': { async platformRequest(url, options) {
      const body = JSON.parse(options.body)
      calls.push({ url, body })
      return reply(url, body)
    } },
  }, apiSource)
  return { calls, client: module.integrityApi }
}
const accountInput = { requestId: 'account-wire', disposition: 'confirmed', scope: 'account',
  targetAccountIds: ['player-a'], matchIds: ['match-a'], restrictedAccountIds: [], restrictionDays: null,
  reason: '只撤甲收益', evidence: '合成事实' }
const echo = input => ({ revision: 4, requestId: input.requestId, disposition: input.disposition,
  canConfirm: true, blockingReasons: [], scope: input.scope, targetAccountIds: input.targetAccountIds,
  matchIds: input.matchIds, accountEffects: [], playerNotificationAccountIds: input.targetAccountIds,
  decisionId: 'wire-decision' })
let apiPassed = 0
{
  const f = apiFixture((url, body) => echo(body.input || body))
  await f.client.preview(accountInput)
  await f.client.confirm(accountInput, 4)
  assert.deepEqual(f.calls.map(call => call.url), [
    '/api/admin/ranked/integrity/account/preview', '/api/admin/ranked/integrity/account/decisions',
  ], 'actual API module must isolate individual mutations from legacy DTO routes')
  apiPassed++
}
for (const mismatch of ['missing-scope', 'wrong-target']) {
  const f = apiFixture((url, input) => ({ ...echo(input),
    ...(mismatch === 'missing-scope' ? { scope: undefined } : { targetAccountIds: ['player-b'] }) }))
  await assert.rejects(() => f.client.preview(accountInput), 'unverified preview echo must never become a frozen account confirmation')
  assert.equal(f.calls.length, 1)
  apiPassed++
}
{
  const f = apiFixture(() => { throw new Error('HTTP 404 old service') })
  await assert.rejects(() => f.client.preview(accountInput), /404/)
  assert.deepEqual(f.calls.map(call => call.url), ['/api/admin/ranked/integrity/account/preview'], '404 must not fall back to a legacy mutation route')
  apiPassed++
}
{
  let rolledBack = false
  const f = apiFixture((url, body) => {
    if (rolledBack) throw new Error('HTTP 404 after service rollback')
    return echo(body.input || body)
  })
  await f.client.preview(accountInput)
  rolledBack = true
  await assert.rejects(() => f.client.confirm(accountInput, 4), /404/)
  assert.deepEqual(f.calls.map(call => call.url), [
    '/api/admin/ranked/integrity/account/preview', '/api/admin/ranked/integrity/account/decisions',
  ], 'confirm after rollback must not retry through the legacy DTO')
  apiPassed++
}
{
  const f = apiFixture(() => ({ revision: 4, canConfirm: true, accountEffects: [], decisionId: 'legacy-match' }))
  const legacy = { ...accountInput, scope: undefined, targetAccountIds: undefined }
  await f.client.preview(legacy)
  await f.client.confirm(legacy, 4)
  assert.deepEqual(f.calls.map(call => call.url), ['/api/admin/ranked/integrity/preview', '/api/admin/ranked/integrity/decisions'])
  apiPassed++
}
{
  const f = apiFixture((url, body) => ({ ...echo(body.input), targetAccountIds: ['player-b'] }))
  await assert.rejects(() => f.client.confirm(accountInput, 4), 'a mismatched confirmation response is not evidence of the requested result')
  apiPassed++
}
{
  const f = apiFixture(() => { throw new Error('must not send an ambiguous request') })
  await assert.rejects(() => f.client.preview({ ...accountInput, scope: undefined }))
  assert.equal(f.calls.length, 0, 'individual targets without explicit scope cannot be sent to a legacy route')
  apiPassed++
}

function fixture() {
  const requests = []
  const state = Vue.reactive({ account: { id: 'admin', permissions: ['admin.match-governance.write'] } })
  const { descriptor, errors } = parse(fs.readFileSync(source, 'utf8'), { filename: source })
  assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'single-party-test' })
  const template = compileTemplate({ id: 'single-party-test', filename: source,
    source: descriptor.template.content, compilerOptions: { bindingMetadata: script.bindings, hoistStatic: false } })
  assert.deepEqual(template.errors, [])
  const integrityApi = {
    decisions: async () => ({ items: [], nextCursor: null }), adminAppeals: async () => ({ items: [], nextCursor: null }),
    async preview(input) {
      requests.push(JSON.parse(JSON.stringify(input)))
      return { revision: 4, requestId: input.requestId, disposition: input.disposition, canConfirm: true,
        blockingReasons: [], matchIds: input.matchIds, accountEffects: [], playerNotificationAccountIds: input.targetAccountIds || [] }
    },
    async confirm(input) {
      requests.push({ confirmed: JSON.parse(JSON.stringify(input)) })
      return { decisionId: 'decision-a', disposition: input.disposition, scope: input.scope,
        targetAccountIds: input.targetAccountIds, playerNotificationAccountIds: input.targetAccountIds || [] }
    },
  }
  const component = evaluate(script.content, {
    '../platform': { platformState: state }, '../rankedIntegrity': { ...api, integrityApi },
  }, source).default
  component.render = evaluate(template.code, {}, source + '.template').render
  const row = { matchId: 'match-a', firstAccountId: 'player-a', firstPlayer: '玩家甲',
    secondAccountId: 'player-b', secondPlayer: '玩家乙' }
  const host = node('root')
  const app = renderer.createApp(component, { rows: [row], selected: ['match-a'] })
  app.config.warnHandler = warning => warnings.push(warning)
  app.config.errorHandler = error => { runtimeErrors.add(String(error?.stack || error)) }
  app.mount(host)
  return { app, host, vm: app._instance.setupState, requests }
}

// runtime-dom checks these brands when updating real v-model text directives.
// The memory host is neither a Document nor a ShadowRoot, and globals are restored.
const originalBrands = new Map(['Document', 'ShadowRoot'].map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]))
for (const name of originalBrands.keys()) if (!(name in globalThis))
  Object.defineProperty(globalThis, name, { value: class MemoryDomBrand {}, configurable: true })
try {
let passed = 0
for (const penalty of ['withdraw', 'withdraw-ban']) {
  const f = fixture()
  try {
    Object.assign(f.vm.form, { disposition: 'confirmed', scope: 'account', accountTargets: ['player-a'],
      penalty, days: 7, reason: '仅处置甲', evidence: '合成结算证据' })
    await settle()
    await f.vm.makePreview()
    const request = f.requests[0]
    assert.equal(request.scope, 'account', 'actual SFC must explicitly submit individual rather than whole-match scope')
    assert.deepEqual(request.targetAccountIds, ['player-a'], 'only the chosen participant is an action target')
    assert.deepEqual(request.matchIds, ['match-a'])
    assert.deepEqual(request.restrictedAccountIds, penalty === 'withdraw-ban' ? ['player-a'] : [])
    assert.equal(request.restrictionDays, penalty === 'withdraw-ban' ? 7 : null)
    assert.match(text(f.host), /仅撤回收益/)
    assert.match(text(f.host), /撤回收益.*禁赛/)
    await f.vm.confirm()
    assert.deepEqual(f.requests[1].confirmed, request, 'confirm must use the exact frozen preview input')
    passed++
  } finally { f.app.unmount() }
}

{
  const f = fixture()
  try {
    Object.assign(f.vm.form, { disposition: 'confirmed', scope: 'account', accountTargets: ['player-a'],
      penalty: 'withdraw', reason: '仅处置甲', evidence: '合成结算证据' })
    await f.vm.makePreview()
    f.vm.form.accountTargets = ['player-b']
    assert.equal(f.vm.preview, null, 'changing target must invalidate the confirmed preview synchronously')
    await f.vm.confirm()
    assert.equal(f.requests.length, 1, 'a stale target preview cannot send a confirmation')
    passed++
  } finally { f.app.unmount() }
}

{
  const f = fixture()
  try {
    f.vm.prepareRevoke({ decisionId: 'old-account-decision', scope: 'account', targetAccountIds: ['player-a'] })
    assert.equal(f.vm.form.scope, 'account')
    assert.deepEqual([...f.vm.form.accountTargets], ['player-a'], 'revocation retains the exact original individual targets')
    passed++
  } finally { f.app.unmount() }
}
assert.deepEqual([...runtimeErrors], [])
assert.deepEqual(warnings, [])
assert.deepEqual([source, apiSource].map(sha), baseline, 'the probe must not modify its product sources')
console.log(`Ranked integrity single-party actual SFC behavior: ${passed}/4; actual API compatibility boundary: ${apiPassed}/8 cases passed.`)
} finally {
  for (const [name, descriptor] of originalBrands)
    if (descriptor) Object.defineProperty(globalThis, name, descriptor)
    else delete globalThis[name]
}
