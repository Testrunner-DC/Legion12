import assert from 'node:assert/strict'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const Vue = require('vue'), ts = require('typescript')
const panel = new URL('../src/l12/site/AdminAlternateArtsPanel.vue', import.meta.url)
const source = fs.readFileSync(panel, 'utf8')
const compile = code => ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const module = { exports: {} }
new Function('require', 'module', 'exports', compile(fs.readFileSync(new URL('../src/l12/site/useAdminRiskAction.ts', import.meta.url), 'utf8')))(id => { assert.equal(id, 'vue'); return Vue }, module, module.exports)
const reset = source.match(/^function resetRule\(\).+$/m)?.[0]
assert(reset)
const code = compile(reset + '\n' + source.slice(source.indexOf('async function performSaveRule('), source.indexOf('function addEventAccount(')))
function fixture({ kind = 'rank-reached', active = true, fail = false } = {}) {
  const bindings = { ruleForm: Vue.reactive({ id: 'rule-a', alternateArtId: 'art-a', kind, seasonId: 'S01', eventId: 'event-a', masterId: '', minimumTierIndex: 1, active }), canWrite: Vue.ref(true), selectedRuleArt: Vue.ref({ displayName: '测试异画' }), awardRules: Vue.ref([]), platformState: Vue.reactive({ account: { id: 'admin-a' }, token: 'session-a' }) }
  const notices = [], calls = [], risk = module.exports.useAdminRiskAction()
  const api = new Function(...Object.keys(bindings), 'adminApi', 'notice', 'requestRiskAction', code + ';return{saveRule}')(...Object.values(bindings), { async saveAlternateArtAwardRule(payload) { calls.push(structuredClone(payload)); if (fail) throw new Error('synthetic save failure'); return { ...payload, id: payload.id || 'saved' } } }, value => notices.push(value), risk.requestRiskAction)
  return { ...bindings, ...risk, ...api, calls, notices }
}
let passed = 0, failed = 0
async function test(name, run) {
  try { await run(); passed++; console.log('PASS ' + name) }
  catch (error) { failed++; console.error('FAIL ' + name); console.error(error.message) }
}
for (const [kind, active, message] of [['rank-reached', true, /缺失权益已补发/], ['rank-reached', false, /已停用/], ['event', true, /名单执行派发/], ['season-final', true, /归档结算/], ['master-champion-season-final', true, /归档结算/]]) {
  await test('actual save remains confirmed and reports authoritative result: ' + kind + '/' + active, async () => {
    const f = fixture({ kind, active }); f.saveRule(); assert.equal(f.calls.length, 0); await f.confirmRiskAction(); assert.equal(f.calls.length, 1); assert.match(f.notices.at(-1), message)
  })
}
await test('confirmation submits exactly the original rule even if the backing form changes', async () => {
  const f = fixture(), original = structuredClone(Vue.toRaw(f.ruleForm)); f.saveRule()
  Object.assign(f.ruleForm, { alternateArtId: 'art-b', seasonId: 'S02', minimumTierIndex: 4, active: false, kind: 'event' })
  await f.confirmRiskAction(); assert.deepEqual(f.calls, [original])
})
for (const drift of ['permission', 'actor', 'token']) await test('confirmation rejects changed ' + drift + ' before any request', async () => {
  const f = fixture(); f.saveRule()
  if (drift === 'permission') f.canWrite.value = false
  if (drift === 'actor') f.platformState.account.id = 'admin-b'
  if (drift === 'token') f.platformState.token = 'session-b'
  await f.confirmRiskAction(); assert.equal(f.calls.length, 0); assert.match(f.riskError.value, /变化|失效|权限|登录/)
})
await test('failed save preserves the draft and never claims delivery', async () => {
  const f = fixture({ fail: true }); f.saveRule(); await f.confirmRiskAction(); assert.equal(f.ruleForm.id, 'rule-a'); assert.equal(f.awardRules.value.length, 0); assert.equal(f.riskError.value, 'synthetic save failure'); assert(!f.notices.some(value => value.includes('已补发')))
})
assert.equal(fs.readFileSync(fileURLToPath(panel), 'utf8'), source, 'test must not modify product source')
console.log(JSON.stringify({ suite: 'Actual alternate-art rule save intent', passed, failed, networkCalls: 0 }))
if (failed) process.exitCode = 1
