import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import * as Vue from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'

const filename = new URL('../src/l12/site/TournamentManagementPanel.vue', import.meta.url)
const sfc = fs.readFileSync(filename, 'utf8')
const { descriptor, errors } = parse(sfc, { filename: filename.pathname })
assert.deepEqual(errors, [])
const componentSource = compileScript(descriptor, { id: 'management-drafts' }).content
assert.deepEqual(compileTemplate({ source: descriptor.template.content, filename: filename.pathname, id: 'management-drafts' }).errors, [])
const compiled = ts.transpileModule(componentSource, { compilerOptions: {
  target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS,
} }).outputText
const node = () => ({ children: [], parent: null })
const renderer = Vue.createRenderer({
  createElement: node, createText: node, createComment: node,
  setText() {}, setElementText() {}, patchProp() {},
  parentNode: value => value.parent, nextSibling: () => null,
  insert(value, parent) { value.parent = parent; parent.children.push(value) },
  remove(value) { if (value.parent) value.parent.children = value.parent.children.filter(child => child !== value) },
})
const flush = async () => { for (let i = 0; i < 6; i++) { await Promise.resolve(); await Vue.nextTick() } }
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
const original = () => ({ id: 'tournament-A', version: 10, status: 'registration', phase: 'registration-open',
  visibility: 'public', registrationVisibility: 'public', registrationOpensAt: '2031-01-01T10:02:03.456Z',
  registrationClosesAt: '2031-01-02T11:12:13.789+00:00', announcement: 'server announcement', referees: [], organizerAccountId: 'actor-A' })

function mount() {
  const state = Vue.reactive({ tournament: original(), accountId: 'actor-A', canOrganize: true })
  const platformState = Vue.reactive({ account: { id: 'actor-A' }, token: 'token-A' })
  const route = Vue.reactive({ fullPath: '/tournament/A' })
  const calls = [], plans = [], updates = [], notices = []
  const api = new Proxy({}, { get: (_target, method) => (...args) => {
    calls.push({ method, args })
    const queued = plans.shift()
    if (queued) return queued.promise
    const result = { ...state.tournament, version: state.tournament.version + 1 }
    if (method === 'setVisibility') { result.visibility = args[2]; result.registrationVisibility = args[3] }
    if (method === 'setRegistrationWindow') { result.registrationOpensAt = args[2]; result.registrationClosesAt = args[3] }
    return Promise.resolve(result)
  } })
  const module = { exports: {} }
  vm.runInNewContext(compiled, { module, exports: module.exports, console, Error, require(id) {
    if (id === 'vue') return Vue
    if (id === 'vue-router') return { useRoute: () => route }
    if (id === './TournamentAnnouncement.vue') return { default: { render: () => null } }
    if (id === '@/l12/platform') return { platformState, tournamentApi: api, friendApi: { friends: () => Promise.resolve([]) } }
    if (id === '@/l12/tournamentLabels') return { tournamentAutomationStatusText: String, tournamentFormatText: String }
    throw new Error(`unexpected import ${id}`)
  } }, { filename: filename.pathname })
  const component = module.exports.default
  const setup = component.setup
  let instance
  component.setup = (props, context) => { instance = Vue.getCurrentInstance(); return setup(props, context) }
  component.render = () => null // Actual setup/watch/lifecycle, no browser DOM or template UI claim.
  const app = renderer.createApp({ setup: () => () => Vue.h(component, { ...state,
    onUpdated: value => { updates.push(value); state.tournament = value }, onNotice: value => notices.push(value),
  }) })
  app.mount(node())
  let mounted = true
  return { state, platformState, route, calls, plans, updates, notices, get form() { return instance.setupState },
    unmount() { if (mounted) { mounted = false; app.unmount() } } }
}

const results = []
async function check(name, work) {
  const f = mount()
  try { await flush(); await work(f); results.push({ name, passed: true }) }
  catch (error) { results.push({ name, passed: false, error: error.message }) }
  finally { f.unmount(); await flush() }
}
const edit = f => { f.form.visibility = 'code'; f.form.registrationVisibility = 'staff'; f.form.registrationOpensLocal = '2032-03-04T05:06'; f.form.registrationClosesLocal = '2032-03-05T06:07' }
const draft = f => [f.form.visibility, f.form.registrationVisibility, f.form.registrationOpensLocal, f.form.registrationClosesLocal]

await check('same-value DTO replacement preserves both visibility choices', async f => {
  edit(f); const before = draft(f).slice(0, 2)
  f.state.tournament = { ...f.state.tournament }; await flush()
  assert.deepEqual(draft(f).slice(0, 2), before)
})
await check('same-value DTO replacement preserves both registration datetime inputs', async f => {
  edit(f); const before = draft(f).slice(2)
  f.state.tournament = { ...f.state.tournament }; await flush()
  assert.deepEqual(draft(f).slice(2), before)
})
await check('unrelated version and announcement refresh preserves the draft', async f => {
  edit(f); const before = draft(f)
  f.state.tournament = { ...f.state.tournament, version: 11, announcement: 'new server announcement' }; await flush()
  assert.deepEqual(draft(f), before)
})
await check('real server field changes reset their existing form groups', async f => {
  edit(f)
  const next = { ...f.state.tournament, visibility: 'code', registrationVisibility: 'staff',
    registrationOpensAt: '2033-01-01T01:02:03.456Z', registrationClosesAt: null }
  f.state.tournament = next; await flush()
  assert.equal(f.form.visibility, next.visibility); assert.equal(f.form.registrationVisibility, next.registrationVisibility)
  assert.equal(f.form.registrationOpensLocal, f.form.localDate(next.registrationOpensAt)); assert.equal(f.form.registrationClosesLocal, '')
})
await check('failed save preserves draft and reports the real error', async f => {
  edit(f); const before = draft(f), failure = deferred(); f.plans.push(failure)
  f.form.saveVisibility(); failure.reject(new Error('synthetic CAS conflict')); await flush()
  assert.deepEqual(draft(f), before); assert.equal(f.updates.length, 0)
  assert.equal(f.notices.at(-1), 'synthetic CAS conflict')
})
await check('successful save preserves expected version and unedited timestamp precision', async f => {
  const source = { ...f.state.tournament }
  f.form.saveRegistrationWindow(); await flush()
  const call = f.calls[0]
  assert.equal(call.method, 'setRegistrationWindow')
  assert.equal(call.args[1], source.version)
  assert.equal(call.args[2], source.registrationOpensAt); assert.equal(call.args[3], source.registrationClosesAt)
  assert.equal(f.updates.length, 1)
  f.form.visibility = 'code'; f.form.registrationVisibility = 'staff'
  const version = f.state.tournament.version
  f.form.saveVisibility(); await flush()
  assert.equal(f.calls[1].args[1], version)
  assert.equal(f.state.tournament.visibility, 'code'); assert.equal(f.form.visibility, 'code')
})
await check('server visibility changes do not clear unrelated datetime edits', async f => {
  edit(f); const dates = draft(f).slice(2)
  f.state.tournament = { ...f.state.tournament, visibility: 'code' }; await flush()
  assert.deepEqual(draft(f).slice(0, 2), ['code', 'public'])
  assert.deepEqual(draft(f).slice(2), dates)
})
await check('server datetime changes do not clear unrelated visibility edits', async f => {
  edit(f); const choices = draft(f).slice(0, 2)
  f.state.tournament = { ...f.state.tournament, registrationClosesAt: null }; await flush()
  assert.deepEqual(draft(f).slice(0, 2), choices)
  assert.equal(f.form.registrationOpensLocal, f.form.localDate(f.state.tournament.registrationOpensAt))
  assert.equal(f.form.registrationClosesLocal, '')
})
await check('same-value account replacement preserves drafts without changing authoritative DTO', async f => {
  const frozen = Object.freeze({ ...f.state.tournament })
  f.state.tournament = frozen; await flush(); edit(f); f.form.reason = 'unsaved reason'
  const before = draft(f)
  f.platformState.account = { ...f.platformState.account }; await flush()
  assert.deepEqual(draft(f), before); assert.equal(f.form.reason, 'unsaved reason')
  assert.equal(f.state.tournament, frozen); assert.equal(frozen.visibility, 'public')
})
for (const boundary of ['account', 'token', 'route', 'tournament', 'permission']) {
  await check(`${boundary} boundary resets sensitive local drafts`, async f => {
    edit(f); f.form.reason = 'private reason'; f.form.postponeAt = '2032-02-02T02:02'
    f.form.transferAccountId = 'private-recipient'; f.form.transferReason = 'private transfer'; f.form.cancelArmed = true
    if (boundary === 'account') { f.state.accountId = 'actor-B'; f.platformState.account = { id: 'actor-B' } }
    else if (boundary === 'token') f.platformState.token = 'token-B'
    else if (boundary === 'route') f.route.fullPath = '/tournament/other'
    else if (boundary === 'tournament') f.state.tournament = { ...f.state.tournament, id: 'tournament-B' }
    else f.state.canOrganize = false
    await flush()
    assert.deepEqual(draft(f).slice(0, 2), ['public', 'public'])
    assert.equal(f.form.registrationOpensLocal, f.form.localDate(f.state.tournament.registrationOpensAt))
    assert.equal(f.form.registrationClosesLocal, f.form.localDate(f.state.tournament.registrationClosesAt))
    for (const key of ['reason', 'postponeAt', 'transferAccountId', 'transferReason']) assert.equal(f.form[key], '')
    assert.equal(f.form.cancelArmed, false)
  })
}
await check('unchanged automatic readback does not cancel an already-sent valid save', async f => {
  edit(f); const gate = deferred(); f.plans.push(gate)
  f.form.saveVisibility(); assert.equal(f.calls.length, 1)
  f.state.tournament = { ...f.state.tournament }; await flush()
  assert.deepEqual(draft(f).slice(0, 2), ['code', 'staff'])
  gate.resolve({ ...f.state.tournament, version: 11, visibility: 'code', registrationVisibility: 'staff' }); await flush()
  assert.equal(f.calls.length, 1); assert.equal(f.updates.length, 1)
  assert.equal(f.form.busy, false); assert.equal(f.notices.at(-1), '公开范围已保存')
})
await check('A-B-A rejects an old failure without clearing the new owner write state', async f => {
  edit(f); const retired = deferred(), active = deferred(); f.plans.push(retired, active)
  f.form.saveVisibility()
  f.state.accountId = 'actor-B'; f.platformState.account = { id: 'actor-B' }; await flush()
  f.state.accountId = 'actor-A'; f.platformState.account = { id: 'actor-A' }; await flush()
  f.form.visibility = 'code'; f.form.saveVisibility()
  assert.equal(f.calls.length, 2); assert.equal(f.form.busy, true)
  retired.reject(new Error('retired old actor failure')); await flush()
  assert.equal(f.form.busy, true); assert.equal(f.notices.length, 0); assert.equal(f.updates.length, 0)
  active.resolve({ ...f.state.tournament, version: 11, visibility: 'code' }); await flush()
  assert.equal(f.form.busy, false); assert.equal(f.updates.length, 1)
})
await check('unmounted owner ignores a late successful save response', async f => {
  edit(f); const gate = deferred(); f.plans.push(gate); f.form.saveVisibility(); f.unmount()
  gate.resolve({ ...f.state.tournament, visibility: 'code', version: 11 }); await flush()
  assert.equal(f.updates.length, 0); assert.equal(f.notices.length, 0)
})
await check('edited registration dates still convert only edited values and support explicit clearing', async f => {
  f.form.registrationOpensLocal = ''; f.form.registrationClosesLocal = '2032-04-05T06:07'
  const version = f.state.tournament.version
  f.form.saveRegistrationWindow(); await flush()
  assert.equal(f.calls[0].args[1], version); assert.equal(f.calls[0].args[2], null)
  assert.equal(f.calls[0].args[3], new Date('2032-04-05T06:07').toISOString())
})
await check('announcement and start-check stale-version guards remain active', async f => {
  await f.form.saveAnnouncement({ tournamentId: f.state.tournament.id, version: 9, text: 'stale' })
  assert.equal(f.calls.length, 0)
  const announcement = deferred(); f.plans.push(announcement)
  const saving = f.form.saveAnnouncement({ tournamentId: f.state.tournament.id, version: 10, text: 'new announcement' })
  f.state.tournament = { ...f.state.tournament, version: 11 }; await flush()
  announcement.resolve({ ...f.state.tournament, announcement: 'late announcement' }); await saving; await flush()
  assert.equal(f.updates.length, 0)
  const checkGate = deferred(); f.plans.push(checkGate)
  const checking = f.form.checkStart()
  f.state.tournament = { ...f.state.tournament, version: 12 }; await flush()
  checkGate.resolve({ canStart: true, blockers: [], warnings: [], tournamentVersion: 11 }); await checking; await flush()
  assert.equal(f.form.startCheck, null)
})
console.log(JSON.stringify({ actualSfcSetupAndVueLifecycle: true, noNetwork: true, results }))
assert.equal(results.filter(result => !result.passed).length, 0, 'management draft regression failures')
