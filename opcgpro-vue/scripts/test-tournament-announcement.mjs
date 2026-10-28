import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import * as Vue from 'vue'
import { renderToString } from '@vue/server-renderer'

const source = readFileSync(new URL('../src/l12/site/TournamentAnnouncement.vue', import.meta.url), 'utf8')
const parsed = parse(source, { filename: 'TournamentAnnouncement.vue' })
assert.deepEqual(parsed.errors, [])
const template = compileTemplate({ source: parsed.descriptor.template.content, filename: 'TournamentAnnouncement.vue', id: 'announcement' })
assert.deepEqual(template.errors, [])
const hasHtmlDirective = text => /(?:^|[\s<])v-html(?:[\s=>]|$)/.test(text)
assert(!hasHtmlDirective(parsed.descriptor.template.content))
assert(hasHtmlDirective('<p v-html="announcement"></p>'), 'injected v-html must be rejected')
function component(inlineTemplate = false) {
  const compiled = compileScript(parsed.descriptor, { id: 'announcement', inlineTemplate })
  const module = { exports: {} }
  const javascript = ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(javascript, { module, exports: module.exports, require: id => {
    if (id === 'vue') return Vue
    throw new Error('Unexpected import: ' + id)
  }, console }, { filename: 'TournamentAnnouncement.vue' })
  return module.exports.default
}
const renderer = Vue.createRenderer({
  createElement: () => ({ children: [] }), createText: () => ({}), createComment: () => ({}),
  insert(child, parent) { parent.children.push(child) }, remove() {}, setText() {}, setElementText() {},
  parentNode: () => null, nextSibling: () => null, patchProp() {},
})
const flush = async () => { for (let i = 0; i < 4; i++) { await Promise.resolve(); await Vue.nextTick() } }
function fixture(overrides = {}) {
  const props = Vue.reactive({ tournamentId: 'a', version: 1, announcement: '旧公告', canEdit: true, busy: false, ...overrides })
  const events = []
  const target = component()
  target.render = () => null
  const root = { children: [] }
  const parent = Vue.h({ render: () => Vue.h(target, { ...props, onSave: event => events.push({ ...event }) }) })
  renderer.render(parent, root)
  return { props, events, state: parent.component.subTree.component.setupState, close: () => renderer.render(null, root) }
}
let passed = 0
async function test(name, fn) { await fn(); passed++; console.log('PASS ' + name) }

await test('save emits frozen exact text and version; blank clears', async () => {
  const f = fixture()
  try {
    f.state.draft = '联系 QQ 123\n奖励：卡包'
    f.state.save()
    f.state.draft = '后续草稿'
    assert.deepEqual(f.events[0], { tournamentId: 'a', version: 1, text: '联系 QQ 123\n奖励：卡包' })
    f.state.draft = ''
    f.state.save()
    assert.deepEqual(f.events[1], { tournamentId: 'a', version: 1, text: '' })
  } finally { f.close() }
})
await test('length boundary rejects oversize without truncation', async () => {
  const f = fixture({ announcement: '' })
  try {
    f.state.draft = '甲'.repeat(2000); f.state.save(); assert.equal(f.events[0].text.length, 2000)
    f.state.draft = '乙'.repeat(2001); f.state.save(); assert.equal(f.events.length, 1); assert.equal(f.state.draft.length, 2001); assert.equal(f.state.invalid, true)
  } finally { f.close() }
})
await test('permission loss and busy prevent sending', async () => {
  const f = fixture()
  try {
    f.state.draft = '新公告'; f.props.canEdit = false; await flush(); f.state.save(); assert.equal(f.events.length, 0)
    f.props.canEdit = true; f.props.busy = true; await flush(); f.state.save(); assert.equal(f.events.length, 0)
    f.props.busy = false; await flush(); f.state.save(); assert.equal(f.events.length, 1)
  } finally { f.close() }
})
await test('switching tournament resets draft and version', async () => {
  const f = fixture()
  try {
    f.state.draft = '尚未保存'; f.props.tournamentId = 'b'; f.props.version = 7; f.props.announcement = '另一场'; await flush()
    assert.equal(f.state.draft, '另一场'); f.state.draft = '另一场更新'; f.state.save()
    assert.deepEqual(f.events[0], { tournamentId: 'b', version: 7, text: '另一场更新' })
  } finally { f.close() }
})
await test('old save response cannot overwrite a newer draft', async () => {
  const f = fixture()
  try {
    f.state.draft = '已提交'; f.state.save(); f.state.draft = '正在编辑的新草稿'
    f.props.version = 2; f.props.announcement = '已提交'; await flush()
    assert.equal(f.state.draft, '正在编辑的新草稿'); assert.equal(f.state.conflict, true)
    f.state.save(); assert.equal(f.events.length, 1)
    f.state.resetDraft(); assert.equal(f.state.draft, '已提交'); assert.equal(f.state.conflict, false)
  } finally { f.close() }
})
await test('same announcement acknowledgement accepts pristine submitted draft', async () => {
  const f = fixture()
  try {
    f.state.draft = '已提交'; f.state.save(); f.props.version = 2; f.props.announcement = '已提交'; await flush()
    assert.equal(f.state.conflict, false); f.state.draft = '下一版'; f.state.save()
    assert.deepEqual(f.events[1], { tournamentId: 'a', version: 2, text: '下一版' })
  } finally { f.close() }
})
await test('actual template escapes markup, retains newlines and hides empty public announcement', async () => {
  const Sfc = component(true)
  const content = '<img src=x onerror=alert(1)>\n奖励'
  const html = await renderToString(Vue.createSSRApp(Sfc, { tournamentId: 'a', version: 1, announcement: content, canEdit: false, busy: false }))
  assert(html.includes('&lt;img')); assert(!html.includes('<img')); assert(html.includes('\n奖励'))
  const empty = await renderToString(Vue.createSSRApp(Sfc, { tournamentId: 'a', version: 1, announcement: '', canEdit: false, busy: false }))
  assert(!empty.includes('赛事公告'))
  const whitespace = await renderToString(Vue.createSSRApp(Sfc, { tournamentId: 'a', version: 1, announcement: '  \n  ', canEdit: false, busy: false }))
  assert(!whitespace.includes('赛事公告'))
  assert(parsed.descriptor.styles.some(style => /white-space\s*:\s*pre-wrap/.test(style.content)))
})
await test('actual editable template displays a disabled editor during save', async () => {
  const Sfc = component(true)
  const html = await renderToString(Vue.createSSRApp(Sfc, { tournamentId: 'a', version: 2, announcement: '原公告', canEdit: true, busy: true }))
  assert(html.includes('<textarea')); assert(html.includes('保存公告')); assert(/<textarea[^>]*disabled/.test(html)); assert(/<button[^>]*disabled/.test(html))
})
console.log('Tournament announcement real SFC cases passed: ' + passed)

function managementFixture() {
  const pending = []
  const pendingChecks = [], starts = []
  const route = Vue.reactive({ fullPath: '/battle/tournaments/a' })
  const platformState = Vue.reactive({ token: 'token-a', account: { id: 'owner' } })
  const props = Vue.reactive({ tournament: { id: 'a', version: 1, status: 'registration', referees: [], visibility: 'public', registrationVisibility: 'public' }, accountId: 'owner', canOrganize: true })
  const events = []
  const source = readFileSync(new URL('../src/l12/site/TournamentManagementPanel.vue', import.meta.url), 'utf8')
  const compiled = compileScript(parse(source).descriptor, { id: 'announcement-management' })
  const module = { exports: {} }
  const imports = {
    vue: Vue,
    'vue-router': { useRoute: () => route },
    './TournamentAnnouncement.vue': { default: {} },
    '@/l12/platform': { platformState, friendApi: { friends: async () => [] }, tournamentApi: {
      setAnnouncement: (id, version, text) => new Promise(resolve => pending.push({ id, version, text, resolve })),
      startCheck: id => new Promise(resolve => pendingChecks.push({ id, resolve })),
      start: async (...args) => { starts.push(args); return { ...props.tournament, status: 'running' } },
    } },
    '@/l12/tournamentLabels': { tournamentAutomationStatusText: value => value },
  }
  vm.runInNewContext(ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require: id => { if (!(id in imports)) throw new Error('Unexpected import: ' + id); return imports[id] }, console, Date, Error },
    { filename: 'TournamentManagementPanel.vue' })
  const target = module.exports.default; target.render = () => null
  const root = { children: [] }
  const parent = Vue.h({ render: () => Vue.h(target, { ...props, onUpdated: event => events.push(event), onNotice: text => events.push(text) }) })
  renderer.render(parent, root)
  return { props, route, platformState, pending, pendingChecks, starts, events, state: parent.component.subTree.component.setupState, close: () => renderer.render(null, root) }
}
await test('real management SFC sends exact values and publishes only its current reply', async () => {
  const f = managementFixture()
  try {
    const work = f.state.saveAnnouncement({ tournamentId: 'a', version: 1, text: '内容\n奖励' }); await flush()
    assert.equal(f.pending.length, 1); assert.equal(f.pending[0].text, '内容\n奖励')
    f.pending[0].resolve({ id: 'a', version: 2, announcement: '内容\n奖励' }); await work
    assert.equal(f.events[0].announcement, '内容\n奖励')
  } finally { f.close() }
})
for (const change of ['route', 'account', 'account-aba', 'token-aba', 'event', 'version', 'permission', 'unmount']) {
  await test('management ignores late reply after ' + change, async () => {
    const f = managementFixture()
    const work = f.state.saveAnnouncement({ tournamentId: 'a', version: 1, text: '旧请求' }); await flush()
    if (change === 'route') f.route.fullPath = '/battle/tournaments/b'
    if (change === 'account') f.props.accountId = 'other'
    if (change === 'account-aba') { const previous = f.platformState.account; f.platformState.account = { id: 'other' }; f.platformState.account = previous }
    if (change === 'token-aba') { f.platformState.token = 'token-b'; f.platformState.token = 'token-a' }
    if (change === 'event') f.props.tournament = { ...f.props.tournament, id: 'b', version: 1 }
    if (change === 'version') f.props.tournament = { ...f.props.tournament, version: 2 }
    if (change === 'permission') f.props.canOrganize = false
    if (change === 'unmount') f.close()
    await flush()
    f.pending[0].resolve({ id: 'a', version: 2, announcement: '旧请求' }); await work
    assert.equal(f.events.length, 0)
    if (change !== 'unmount') f.close()
  })
}
const checked = overrides => ({ tournamentVersion: 1, eligiblePlayers: 4, checkedInWaitlistedPlayers: 1,
  waitlistedPlayers: 2, openJudgeCases: 0, canStart: true, blockers: [], warnings: [],
  format: 'swiss-cut', swissRounds: 3, cutSize: 4, ...overrides })
await test('normal entry executes actual precheck and guarded start with the checked version', async () => {
  const f = managementFixture()
  try {
    f.state.start(); assert.equal(f.starts.length, 0)
    const work = f.state.checkStart(); await flush()
    assert.equal(f.pendingChecks.length, 1); assert.equal(f.pendingChecks[0].id, 'a')
    f.pendingChecks[0].resolve(checked()); await work
    assert.equal(f.state.startCheck.checkedInWaitlistedPlayers, 1)
    f.state.start(); await flush(); assert.equal(f.starts.length, 1); assert.equal(f.starts[0][1], 1)
  } finally { f.close() }
})
for (const change of ['account-aba', 'token-aba', 'route-aba', 'version-aba', 'permission', 'unmount']) {
  await test('normal entry rejects actual precheck reply after ' + change, async () => {
    const f = managementFixture()
    const work = f.state.checkStart(); await flush()
    if (change === 'account-aba') { const value = f.platformState.account; f.platformState.account = { id: 'other' }; f.platformState.account = value }
    if (change === 'token-aba') { f.platformState.token = 'other'; f.platformState.token = 'token-a' }
    if (change === 'route-aba') { f.route.fullPath = '/other'; f.route.fullPath = '/battle/tournaments/a' }
    if (change === 'version-aba') { f.props.tournament = { ...f.props.tournament, version: 2 }; f.props.tournament = { ...f.props.tournament, version: 1 } }
    if (change === 'permission') f.props.canOrganize = false
    if (change === 'unmount') f.close()
    f.pendingChecks[0].resolve(checked()); await work
    assert.equal(f.state.startCheck, null); f.state.start(); assert.equal(f.starts.length, 0)
    if (change !== 'unmount') f.close()
  })
}
await test('normal entry keeps only newest same-version precheck and permits single nonapplicable null fields', async () => {
  const f = managementFixture()
  try {
    const old = f.state.checkStart(), fresh = f.state.checkStart(); await flush()
    f.pendingChecks[1].resolve(checked({ format: 'single', swissRounds: null, cutSize: null })); await fresh
    f.pendingChecks[0].resolve(checked({ eligiblePlayers: 99 })); await old
    assert.equal(f.state.startCheck.format, 'single'); assert.equal(f.state.startCheck.eligiblePlayers, 4)
  } finally { f.close() }
})
for (const malformed of [null, {}, checked({ tournamentVersion: 0 }), checked({ checkedInWaitlistedPlayers: -1 }),
  checked({ blockers: [{}] }), checked({ canStart: true, blockers: ['存在阻塞'] })]) {
  await test('normal entry rejects unusable precheck without exposing internal parse errors: ' + JSON.stringify(malformed), async () => {
    const f = managementFixture()
    try {
      const work = f.state.checkStart(); await flush(); f.pendingChecks[0].resolve(malformed); await work
      assert.equal(f.state.startCheck, null); f.state.start(); assert.equal(f.starts.length, 0)
      assert(f.events.includes('开赛检查不可用，请刷新后重试'))
    } finally { f.close() }
  })
}
console.log('Tournament announcement and precheck normal-entry SFC cases passed: ' + passed)
