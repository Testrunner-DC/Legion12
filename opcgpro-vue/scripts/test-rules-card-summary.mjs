import assert from 'node:assert/strict'
import fs from 'node:fs'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import * as Vue from 'vue'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'

const require = createRequire(import.meta.url)
const read = file => fs.readFileSync(new URL(`../src/l12/${file}`, import.meta.url), 'utf8')
const source = read('site/RulesCardSummary.vue')
const host = (type = 'root', text = '') => ({ type, tagName: type.toUpperCase(), text, props: {}, children: [], parent: null,
  addEventListener() {}, removeEventListener() {}, getRootNode() { return document },
  get options() { return this.children.filter(child => child.type === 'option') } })
const teleport = host('teleport')
globalThis.window = { addEventListener() {}, removeEventListener() {} }
globalThis.document = { activeElement: null }
globalThis.Document = class MemoryDocument {}
globalThis.ShadowRoot = class MemoryShadowRoot {}
const renderer = Vue.createRenderer({
  createElement: type => host(type), createText: text => host('text', text), createComment: text => host('comment', text),
  insert(node, parent, anchor) {
    if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1)
    node.parent = parent; const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(node); else parent.children.splice(index, 0, node)
  },
  remove(node) { const index = node.parent?.children.indexOf(node) ?? -1; if (index >= 0) node.parent.children.splice(index, 1) },
  patchProp: (node, key, _before, value) => { node.props[key] = value },
  setText: (node, text) => { node.text = text }, setElementText: (node, text) => { node.text = text; node.children = [] },
  parentNode: node => node.parent, nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] ?? null,
  querySelector: () => teleport, setScopeId() {},
})
function compile(text, filename, imports) {
  const parsed = parse(text, { filename }); assert.deepEqual(parsed.errors, [])
  const script = compileScript(parsed.descriptor, { id: filename })
  const template = compileTemplate({ source: parsed.descriptor.template.content, filename, id: filename,
    compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  const module = { exports: {} }
  const run = input => new Function('require', 'module', 'exports', ts.transpileModule(input, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: false },
  }).outputText)(id => id === 'vue' ? Vue : imports[id] ?? require(id), module, module.exports)
  run(script.content); const component = module.exports.default
  run(template.code); component.render = module.exports.render
  return component
}
const cardDetail = compile(read('CatalogCardDetails.vue'), 'CatalogCardDetails.vue', {
  './CardDetailContent.vue': { default: { props: ['card'], render() { return Vue.h('span', { 'data-detail-card': this.card.id }, this.card.nameZh) } } },
  './mobileViewport': { landscapeTeleportTarget: () => teleport },
})
let catalogRead = async () => cards
const componentFor = text => compile(text, 'RulesCardSummary.vue', {
  '@/l12/CardImage.vue': { default: { props: ['cardId', 'alt'], render() { return Vue.h('img', { 'data-card-id': this.cardId, alt: this.alt }) } } },
  '@/l12/CatalogCardDetails.vue': { default: cardDetail },
  '@/l12/decks': { loadDeckCatalog: () => catalogRead() },
})
const card = (id, nameZh, cardType = 'legion') => ({ id, number: id, nameZh, cardType, imageUrl: '' })
const cards = [card('MASTER-A', '主宰甲', 'master'), card('MASTER-B', '主宰乙', 'master'),
  card('CARD-A', '限制卡甲'), card('CARD-B', '限制卡乙'), card('DS-A', '天灾甲', 'disaster'), card('S01-DS10', '湮灭', 'disaster')]
const rules = [{ cardId: 'CARD-A', maxCopies: 2, reason: '通用上限' },
  { cardId: 'CARD-A', masterId: 'MASTER-A', maxCopies: 0, reason: '甲专属禁用' },
  { cardId: 'CARD-A', masterId: 'MASTER-B', maxCopies: 1 }]
const flatten = node => [node, ...node.children.flatMap(flatten)]
const textOf = node => node.text + node.children.map(textOf).join('')
const flush = async () => { for (let i = 0; i < 12; i++) { await Promise.resolve(); await Vue.nextTick() } }
function mountComponent(component, props) {
  const root = host(); const app = renderer.createApp(component, props)
  app.config.warnHandler = () => {}; app.mount(root)
  return { app, root, nodes: () => [...flatten(root), ...flatten(teleport)],
    unmount: () => app.unmount() }
}
function mount(props, text = source) { return mountComponent(componentFor(text), props) }
function button(view, label) {
  const values = view.nodes().filter(node => node.type === 'button' && node.props['aria-label'] === label)
  assert.equal(values.length, 1, label); return values[0]
}
function assertScopes(view) {
  for (const [label, limit] of [['所有主宰的通用禁限卡', '最多携带 2 张'],
    ['主宰为主宰甲时的禁限卡', '禁止携带（0 张）'], ['主宰为主宰乙时的禁限卡', '最多携带 1 张']]) {
    const groups = view.nodes().filter(node => node.props['aria-label'] === label)
    assert.equal(groups.length, 1); assert.ok(textOf(groups[0]).includes(limit))
    assert.ok(textOf(groups[0]).includes('限制卡甲'))
  }
}
const results = []
async function test(name, work) { try { await work(); results.push({ name, passed: true }) }
catch (error) { results.push({ name, passed: false, error: error.stack }) } }

await test('same card retains independent global and two master-specific limits in rendered groups', async () => {
  const view = mount({ cards, cardRestrictions: rules }); try { await flush(); assertScopes(view) } finally { view.unmount() }
})
await test('real catalog detail component receives the clicked restricted card and master', async () => {
  const view = mount({ cards, cardRestrictions: rules }); try {
    await flush(); button(view, '查看主宰甲主宰详情').props.onClick(); await flush()
    assert.ok(view.nodes().some(node => node.props.role === 'dialog' && node.props['aria-label'] === '主宰甲'))
    view.nodes().find(node => node.props['aria-label'] === '关闭卡牌详情').props.onClick(); await flush()
    view.nodes().find(node => node.props['aria-label'] === '查看限制卡甲卡牌详情').props.onClick(); await flush()
    assert.ok(view.nodes().some(node => node.props['data-detail-card'] === 'CARD-A'))
  } finally { view.unmount() }
})
await test('disaster snapshot order and fixed annihilation are preserved', async () => {
  const view = mount({ cards, disasterCardIds: ['DS-A', 'S01-DS10'], cardRestrictions: [], disasterMode: 'season' })
  try { await flush(); const images = view.nodes().filter(node => node.type === 'img').map(node => node.props['data-card-id'])
    assert.deepEqual(images, ['DS-A', 'S01-DS10']); assert.match(textOf(view.root), /湮灭 · 固定末位/)
    button(view, '查看天灾甲卡牌详情').props.onClick(); await flush()
    assert.ok(view.nodes().some(node => node.props['data-detail-card'] === 'DS-A'))
  } finally { view.unmount() }
})
await test('none mode does not display stale disaster IDs or reinterpret an empty pool', async () => {
  const view = mount({ cards, disasterCardIds: ['DS-A'], disasterMode: 'none' })
  try { await flush(); assert.match(textOf(view.root), /本场不使用天灾/); assert.equal(view.nodes().filter(node => node.type === 'img').length, 0) }
  finally { view.unmount() }
})
await test('unknown disaster, master and restriction IDs remain visible with exact limits', async () => {
  const view = mount({ cards, disasterCardIds: ['UNKNOWN-DS'], cardRestrictions: [{ masterId: 'UNKNOWN-MASTER', cardId: 'UNKNOWN-CARD', maxCopies: 1 }] })
  try { await flush(); for (const id of ['UNKNOWN-DS', 'UNKNOWN-MASTER', 'UNKNOWN-CARD']) assert.ok(textOf(view.root).includes(id))
    assert.match(textOf(view.root), /最多携带 1 张/); assert.equal(button(view, '查看UNKNOWN-CARD卡牌详情').props.disabled, true)
  } finally { view.unmount() }
})
await test('public catalog failure keeps rule IDs and permits an explicit retry', async () => {
  let calls = 0; catalogRead = async () => { if (++calls === 1) throw new Error('fixture read failure'); return cards }
  const view = mount({ disasterCardIds: ['DS-A'], cardRestrictions: rules })
  try { await flush(); assert.match(textOf(view.root), /卡牌资料暂不可读取/); assert.match(textOf(view.root), /CARD-A/)
    view.nodes().find(node => node.type === 'button' && textOf(node) === '重试').props.onClick(); await flush()
    assert.equal(calls, 2); assertScopes(view)
  } finally { view.unmount(); catalogRead = async () => cards }
})
await test('late catalog resolution after unmount does not recreate a detail or append rendered nodes', async () => {
  let resolve; catalogRead = () => new Promise(done => { resolve = done })
  const view = mount({ disasterCardIds: ['DS-A'] }); await flush(); view.unmount()
  resolve(cards); await flush(); assert.equal(view.root.children.length, 0); assert.equal(teleport.children.length, 0)
  catalogRead = async () => cards
})
await test('scope-erasing mutant is rejected by the actual rendered association assertions', async () => {
  const mutant = source.replace("const key = rule.masterId || ''", "const key = ''")
  assert.notEqual(mutant, source); const view = mount({ cards, cardRestrictions: rules }, mutant)
  try { await flush(); assert.throws(() => assertScopes(view)) } finally { view.unmount() }
})
await test('all rule consumers pass original snapshot arrays to the same component', () => {
  for (const [file, disaster, restrictions] of [
    ['site/TournamentAccountDetail.vue', 'tournament.rules.disasterCardIds', 'tournament.rules.cardRestrictions'],
    ['site/BattleHubPage.vue', 'operationsPolicy.disasterCardIds', 'operationsPolicy.cardRestrictions'],
    ['site/TournamentPublicDetail.vue', 'tournament.rules.disasterCardIds', 'tournament.rules.cardRestrictions'],
    ['site/TournamentCreateWizard.vue', 'form.disasterCardIds', 'form.cardRestrictions'],
    ['site/AdminTournamentWorkbench.vue', 'detail.rules.disasterCardIds', 'detail.rules.cardRestrictions'],
    ['site/RuleCenterPage.vue', 'policy.disasterCardIds', 'policy.cardRestrictions'],
  ]) {
    const content = read(file); const { descriptor } = parse(content)
    assert.match(descriptor.scriptSetup.content, /import RulesCardSummary from '.\/RulesCardSummary.vue'/)
    assert.ok(descriptor.template.content.includes(`:disaster-card-ids="${disaster}"`))
    assert.ok(descriptor.template.content.includes(`:card-restrictions="${restrictions}"`))
  }
})
await test('actual shared headcount separates registration, waiting, checked-in and waitlist', async () => {
  const counts = Vue.reactive({ registered: 1, pendingCheckIn: 1, checkedIn: 0, waitlisted: 0 })
  const countComponent = compile(read('site/TournamentParticipantCounts.vue'), 'TournamentParticipantCounts.vue', {})
  const view = mountComponent(countComponent, { counts, maxPlayers: 16 })
  try { await flush(); assert.match(textOf(view.root), /已报名 1 \/ 16/); assert.match(textOf(view.root), /待签到 1 · 已签到 0 · 候补 0/)
    counts.pendingCheckIn = 0; counts.checkedIn = 1; counts.waitlisted = 2; await flush()
    assert.match(textOf(view.root), /已报名 1 \/ 16/); assert.match(textOf(view.root), /待签到 0 · 已签到 1 · 候补 2/)
    counts.registered = 0; counts.checkedIn = 0; await flush(); assert.match(textOf(view.root), /已报名 0 \/ 16/)
  } finally { view.unmount() }
})
await test('public headcount uses committed registration total and derives waiting without private identities', async () => {
  const view = mountComponent(compile(read('site/TournamentParticipantCounts.vue'), 'TournamentParticipantCounts.vue', {}),
    { counts: { registered: 3, checkedIn: 1, waitlisted: 2 }, maxPlayers: 16 })
  try { await flush(); assert.match(textOf(view.root), /已报名 3 \/ 16/); assert.match(textOf(view.root), /待签到 2 · 已签到 1 · 候补 2/) }
  finally { view.unmount() }
})
await test('construction editor keeps adding and deleting the exact card/master pair through shared slots', async () => {
  const changes = []
  const component = compile(read('site/ConstructionRuleEditor.vue'), 'ConstructionRuleEditor.vue', {
    '@/l12/SingleCardPicker.vue': { default: { render: () => null } },
    './RulesCardSummary.vue': { default: componentFor(source) },
  })
  const view = mountComponent(component, { cards, modelValue: rules, 'onUpdate:modelValue': value => changes.push(value) })
  try {
    await flush(); assertScopes(view)
    const group = view.nodes().find(node => node.props['aria-label'] === '主宰为主宰甲时的禁限卡')
    flatten(group).find(node => node.type === 'button' && textOf(node) === '删除此项规则').props.onClick(); await flush()
    assert.deepEqual(changes[0], rules.filter(rule => rule.masterId !== 'MASTER-A'))
    const state = view.app._instance.setupState
    state.choose({ cardId: 'CARD-B' }); state.masterId = 'MASTER-A'; state.maxCopies = 1; state.reason = '甲新增'
    state.save(); await flush()
    assert.deepEqual(changes[1].at(-1), { cardId: 'CARD-B', masterId: 'MASTER-A', maxCopies: 1, reason: '甲新增' })
  } finally { view.unmount() }
})
await test('disaster picker keeps selecting cards while its selected-pool display uses the shared renderer', async () => {
  const changes = []
  const component = compile(read('site/DisasterPoolPicker.vue'), 'DisasterPoolPicker.vue', {
    '@/l12/CardImage.vue': { default: { render: () => Vue.h('img') } },
    './RulesCardSummary.vue': { default: componentFor(source) },
  })
  const view = mountComponent(component, { cards, modelValue: ['S01-DS10'], lockedId: 'S01-DS10',
    'onUpdate:modelValue': value => changes.push(value) })
  try {
    await flush(); assert.ok(view.nodes().some(node => node.props['data-ui-contract'] === 'rules-card-summary'))
    view.app._instance.setupState.toggle('S01-DS10'); assert.equal(changes.length, 0)
    view.app._instance.setupState.toggle('DS-A'); assert.deepEqual(changes[0], ['DS-A', 'S01-DS10'])
  } finally { view.unmount() }
})
console.log(JSON.stringify({ suite: 'shared rules card summary actual SFC rendering',
  sourceSha256: crypto.createHash('sha256').update(source).digest('hex'), cases: results.length,
  passed: results.filter(item => item.passed).length, failed: results.filter(item => !item.passed).length,
  transport: 'in-memory Vue renderer; public catalog fixture; real CatalogCardDetails wrapper', results }, null, 2))
if (results.some(item => !item.passed)) process.exitCode = 1
