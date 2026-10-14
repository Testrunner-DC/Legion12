import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import vm from 'node:vm'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'

const repo = fileURLToPath(new URL('../', import.meta.url))
// DOM directive type checks only; no browser or document is created.
globalThis.Document = class {}
globalThis.ShadowRoot = class {}
const require = createRequire(path.join(repo, 'package.json'))
const Vue = require('vue'), ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const sourcePath = process.argv[2] || path.join(repo, 'src/l12/site/AdminReleasesPage.vue')
const source = fs.readFileSync(sourcePath, 'utf8')
const templateParse = require('@vue/compiler-dom').parse
function adminCalls(input) {
  const script = parse(input).descriptor.scriptSetup?.content
  if (!script) return []
  const tree = ts.createSourceFile('release-readonly.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const calls = []
  function visit(node) {
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)
      && node.expression.expression.getText(tree) === 'adminApi') calls.push(node.expression.name.text)
    ts.forEachChild(node, visit)
  }
  visit(tree)
  return calls
}
function templateNodes(input) {
  const template = parse(input).descriptor.template?.content
  if (!template) return []
  const nodes = []
  function visit(node) { nodes.push(node); for (const child of node.children || []) visit(child) }
  visit(templateParse(template, { comments: false }))
  return nodes
}
export function hasReadonlyReleaseArtifactSource(input) {
  const calls = adminCalls(input)
  const allowed = new Set(['releaseArtifacts', 'releaseEnvironments', 'releaseRuns', 'deployRelease', 'rollbackRelease'])
  if (!calls.includes('releaseArtifacts') || calls.some(name => !allowed.has(name))) return false
  const models = templateNodes(input).flatMap(node => (node.props || []).filter(prop => prop.type === 7 && prop.name === 'model'))
  return models.length > 0 && models.every(prop => ['environmentId', 'artifactId', 'reason'].includes(prop.exp?.content))
}
export function hasReadonlyReleaseRuntimeSnapshot(input) {
  if (!adminCalls(input).includes('releaseEnvironments') || !adminCalls(input).includes('releaseRuns')) return false
  const nodes = templateNodes(input)
  const findLoop = expression => nodes.find(node => node.tag === 'article' && (node.props || []).some(prop =>
    prop.type === 7 && prop.name === 'for' && prop.exp?.content.replace(/\s+/g, '') === expression))
  function expressions(root) {
    if (!root) return ''
    const values = []
    function visit(node) {
      if (node.type === 5) values.push(node.content.content)
      for (const prop of node.props || []) if (prop.type === 7 && prop.exp) values.push(prop.exp.content)
      for (const child of node.children || []) visit(child)
    }
    visit(root)
    return values.join('\n').replace(/\s+/g, '')
  }
  const runtime = expressions(findLoop('iteminenvironments'))
  const runs = expressions(findLoop('runinruns'))
  return ['item.health', 'item.webSocket', 'item.activeArtifactId', 'item.activeCommit', 'item.observedAt'].every(field => runtime.includes(field))
    && ['run.status', 'run.checks'].every(field => runs.includes(field))
}
assert.ok(hasReadonlyReleaseArtifactSource(source))
assert.ok(hasReadonlyReleaseRuntimeSnapshot(source))
assert.ok(hasReadonlyReleaseArtifactSource(source.replace('后台发布能力需单独接入并验收', '任意替换标题说明')))
assert.equal(hasReadonlyReleaseArtifactSource(source.replace('adminApi.releaseArtifacts()', 'adminApi.registerReleaseArtifact()')), false)
assert.equal(hasReadonlyReleaseArtifactSource(source.replace('<template>', '<template><input v-model="verified"/>')), false)
assert.equal(hasReadonlyReleaseRuntimeSnapshot(source.replace('probeLabel(item.webSocket)', "'静态冒烟文案'")), false)
assert.equal(hasReadonlyReleaseRuntimeSnapshot(source.replace('adminApi.releaseEnvironments()', 'Promise.resolve([])')), false)
console.log('Release read-only semantic guards: actual API source, no client registration, caption independence, self-verification rejection, and real runtime fields passed.')
const sha256 = createHash('sha256').update(source).digest('hex').toUpperCase()
const env = (environment, configured = true, version = 1) => ({ environment, version, adapterConfigured: configured, state: configured ? 'healthy' : 'unconfigured', activeArtifactId: null, activeCommit: null, lastRunId: null, health: { success: configured, code: configured ? 'ok' : 'adapter-unconfigured', durationMs: 0 }, webSocket: { success: configured, code: configured ? 'ok' : 'adapter-unconfigured', durationMs: 0 }, observedAt: '2026-10-14T00:00:00Z' })
const artifact = (id, environments) => ({ id, environments, commit: 'a'.repeat(40), releaseSha256: 'b'.repeat(64), verifiedAt: '2026-10-14T00:00:00Z', verificationGates: ['synthetic-local'] })
const runFor = (environment, artifactId, status = 'succeeded') => ({ id: `synthetic-${environment}`, environment, artifactId, status, checks: [], startedAt: '2026-10-14T00:00:00Z' })
const flush = async () => { for (let i = 0; i < 5; i++) { await Promise.resolve(); await Vue.nextTick() } }
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done }); return { promise, resolve } }
function node(tag, text = '') { return { tag, text, children: [], parent: null, props: {}, value: '', tagName: tag.toUpperCase(), selected: false, listeners: new Map(), getRootNode: () => ({}), get options() { return descendants(this, item => item.tag === 'option') }, addEventListener(name, handler) { this.listeners.set(name, handler) }, removeEventListener(name) { this.listeners.delete(name) } } }
function descendants(root, predicate) { return [...(predicate(root) ? [root] : []), ...root.children.flatMap(child => descendants(child, predicate))] }
const content = root => root.tag === '#comment' ? '' : root.text + root.children.map(content).join('')
function detach(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null }
const renderer = Vue.createRenderer({ createElement: tag => node(tag), createText: value => node('#text', value), createComment: value => node('#comment', value),
  insert(child, parent, anchor = null) { detach(child); child.parent = parent; const index = anchor ? parent.children.indexOf(anchor) : -1; if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child) }, remove: detach,
  parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, value) => { child.text = value }, setElementText(child, value) { child.children.forEach(detach); child.children = []; child.text = value },
  patchProp(child, name, previous, value) { child.props[name] = value; if (name === 'value') { child.value = value; child._value = value } }, setScopeId() {},
})
function evaluate(code, imports, name) {
  const module = { exports: {} }
  const compiled = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(compiled, { module, exports: module.exports, require: id => { assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id] }, Date, Error, Map, Set, console }, { filename: name })
  return module.exports
}
async function fixture({ configured = true, allowed = true, artifacts = [artifact('synthetic-A', ['staging'])], environments = [env('staging', configured)], runs = [], previewGate = null } = {}) {
  const calls = [], risks = []
  const store = { artifacts, environments, runs }
  const riskBusy = Vue.ref(false)
  const executePermission = Vue.ref(allowed)
  const { descriptor } = parse(source)
  const script = compileScript(descriptor, { id: 'release-capability-check' })
  const imports = { vue: Vue, './AdminRiskActionDialog.vue': { default: { render: () => null } },
    './useAdminRiskAction': { useAdminRiskAction: () => ({ riskAction: Vue.ref(null), riskBusy, riskError: Vue.ref(''), requestRiskAction: value => risks.push(value), cancelRiskAction() {}, confirmRiskAction() {} }) },
    '@/l12/platform': { hasPermission: permission => permission !== 'releases.execute' || executePermission.value, adminApi: {
      releaseArtifacts: async () => store.artifacts, releaseEnvironments: async () => store.environments, releaseRuns: async () => store.runs,
      // Zero endpoints, credentials, network, adapters or execution callbacks.
      deployRelease: async (...args) => { assert.equal(args[3], true, 'execution API forbidden even in mock'); calls.push({ kind: 'deploy-preview', args }); if (previewGate) await previewGate.promise; return { applied: false, plan: { steps: [] } } },
      rollbackRelease: async (...args) => { assert.equal(args[2], true, 'execution API forbidden even in mock'); calls.push({ kind: 'rollback-preview', args }); if (previewGate) await previewGate.promise; return { applied: false, plan: { steps: [] } } },
    } },
  }
  const component = evaluate(script.content, imports, sourcePath).default
  const template = compileTemplate({ source: descriptor.template.content, filename: sourcePath, id: 'release-capability-check', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  component.render = evaluate(template.code, { vue: Vue }, 'actual-release-template').render
  const root = node('root'), vnode = Vue.h(component)
  renderer.render(vnode, root); await flush()
  const state = vnode.component.setupState
  state.reason = '合成本机检查'; await flush()
  const button = label => { const found = descendants(root, item => item.tag === 'button' && content(item).trim() === label); assert.equal(found.length, 1, label); return found[0] }
  return { state, store, calls, risks, riskBusy, executePermission, root, button, unmount: () => renderer.render(null, root) }
}
const cases = []
async function test(name, check) { try { await check(); cases.push({ name, passed: true }) } catch (error) { cases.push({ name, passed: false, error: error.message.split('\n').slice(0, 4).join(' ') }) } }
async function use(options, check) { const f = await fixture(options); try { await check(f) } finally { f.unmount() } }

await test('unconfigured empty ledger explains capability, not actual service deployment; buttons disabled', () => use({ configured: false, artifacts: [] }, async f => {
  assert.equal(f.button('1. 预演').props.disabled, true)
  assert.equal(f.button('2. 执行发布').props.disabled, true)
  assert.match(content(f.root), /未接入后台发布能力/)
  assert.match(content(f.root), /发布台账版本/)
  assert.match(content(f.root), /不代表.*未部署/)
  assert.match(content(f.root), /不包含.*外部.*部署/)
  assert.match(content(f.root), /未检查/)
  await f.state.perform(true); assert.equal(f.calls.length, 0)
}))
await test('unconfigured with stale artifact is guarded inside actual preview handler', () => use({ configured: false }, async f => {
  await f.state.perform(true); assert.equal(f.calls.length, 0)
}))
await test('permission remains an actual handler guard, not only template disabled', () => use({ allowed: false }, async f => {
  await f.state.perform(true); assert.equal(f.calls.length, 0)
  f.state.preview = { applied: false, plan: { steps: [] } }; f.state.releasePreviewKey = f.state.releaseInputKey
  f.state.execute(); assert.equal(f.risks.length, 0)
}))
await test('selected artifact must still be compatible with selected environment', () => use({}, async f => {
  f.state.artifactId = 'missing-or-incompatible'; await flush()
  await f.state.perform(true); assert.equal(f.calls.length, 0)
}))
await test('configured valid preview works; loss/return of capability cannot revive old preview', () => use({}, async f => {
  assert.equal(Boolean(f.button('1. 预演').props.disabled), false)
  await f.state.perform(true); await flush(); assert.equal(f.calls.length, 1); assert.ok(f.state.previewReady)
  f.state.execute(); assert.equal(f.risks.length, 1, 'valid bound preview keeps the existing risk confirmation')
  f.state.environments = [env('staging', false)]; await flush(); assert.equal(Boolean(f.state.previewReady), false)
  f.state.environments = [env('staging', true)]; await flush(); assert.equal(Boolean(f.state.previewReady), false)
  f.state.execute(); assert.equal(f.risks.length, 1)
}))
await test('artifact removal at same environment version invalidates old preview', () => use({}, async f => {
  await f.state.perform(true); await flush(); assert.ok(f.state.previewReady)
  f.state.artifacts = []; await flush(); assert.equal(Boolean(f.state.previewReady), false)
  f.state.artifacts = [artifact('synthetic-A', ['staging'])]; await flush(); assert.equal(Boolean(f.state.previewReady), false)
}))
await test('rollback uses its own environment/artifact and rejects failed or unconfigured targets', () => use({ artifacts: [artifact('synthetic-A', ['staging']), artifact('synthetic-P', ['production'])], environments: [env('staging'), env('production')], runs: [runFor('production', 'synthetic-P')] }, async f => {
  const target = f.state.runs[0]
  await f.state.previewRollback(target); await flush(); assert.equal(f.calls.length, 1); assert.equal(f.calls[0].args[0], target.id)
  f.state.environments = [env('staging'), env('production', false)]; await flush()
  f.state.rollback(target); assert.equal(f.risks.length, 0)
  await f.state.previewRollback(target); assert.equal(f.calls.length, 1)
  f.state.environments = [env('staging'), env('production')]; await flush()
  await f.state.previewRollback({ ...target, status: 'failed' }); assert.equal(f.calls.length, 1)
  f.state.artifacts = [artifact('synthetic-A', ['staging']), artifact('synthetic-P', ['staging'])]; await flush()
  await f.state.previewRollback(target); assert.equal(f.calls.length, 1)
}))
await test('configured degraded probes are failed checks, not unavailable adapter, and do not block preview', () => use({ environments: [{ ...env('staging'), state: 'degraded', health: { success: false, code: 'health-failed', durationMs: 3 } }] }, async f => {
  await f.state.perform(true); assert.equal(f.calls.length, 1)
  assert.doesNotMatch(content(f.root), /未接入后台发布能力/)
  assert.match(content(f.root), /失败.*health-failed/)
}))
await test('permission loss/return retires completed previews rather than reviving them', () => use({}, async f => {
  await f.state.perform(true); await flush(); assert.ok(f.state.previewReady)
  f.executePermission.value = false; await flush(); assert.equal(Boolean(f.state.previewReady), false)
  f.executePermission.value = true; await flush(); assert.equal(Boolean(f.state.previewReady), false)
}))
await test('capability ABA during real deploy/rollback preview waits rejects late results', async () => {
  for (const kind of ['deploy', 'rollback']) {
    const gate = deferred()
    await use({ previewGate: gate, runs: [runFor('staging', 'synthetic-A')] }, async f => {
      const pending = kind === 'deploy' ? f.state.perform(true) : f.state.previewRollback(f.state.runs[0])
      assert.equal(f.calls.length, 1)
      f.state.environments = [env('staging', false)]; await flush()
      f.state.environments = [env('staging', true)]; await flush()
      gate.resolve(); await pending; await flush()
      assert.equal(Boolean(f.state.previewReady), false)
      assert.equal(f.state.rollbackPreviewKey, '')
      assert.equal(f.state.preview, null)
    })
  }
})
for (const result of cases) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.error ? `: ${result.error}` : ''}`)
const report = { schemaVersion: 1, sourcePath, sha256, cases, passed: cases.filter(c => c.passed).length, failed: cases.filter(c => !c.passed).length, actualSfcScriptAndTemplate: true, networkCalls: 0, realApiCalls: 0, executionApiCalls: 0 }
console.log('RESULT ' + JSON.stringify(report))
if (report.failed) process.exitCode = 1
