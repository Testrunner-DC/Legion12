import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import * as logViewModel from '../src/l12/game/logViewModel.ts'

const require = createRequire(import.meta.url)
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript, compileTemplate } = require('@vue/compiler-sfc')
const source = process.argv.includes('--baseline')
  ? execFileSync('git', ['show', 'HEAD:opcgpro-vue/src/l12/game/BattleEventLog.vue'], { cwd: new URL('../..', import.meta.url), encoding: 'utf8' })
  : fs.readFileSync(new URL('../src/l12/game/BattleEventLog.vue', import.meta.url), 'utf8')
const flush = async () => { for (let index = 0; index < 6; index++) { await Vue.nextTick(); await Promise.resolve() } }
const event = (sequence, text = `合成记录${sequence}`) => ({ sequence, type: 'draw', text, cards: [], playerIndex: 0 })
const history = () => Array.from({ length: 12 }, (_, index) => event(index + 1))
const descendants = root => [root, ...root.children.flatMap(descendants)]
function node(tag, text = '') {
  const value = { tag, text, props: {}, children: [], parent: null, writes: [], viewport: 180, extraHeight: 0, top: 0 }
  Object.defineProperties(value, {
    clientHeight: { get: () => value.viewport },
    clientWidth: { get: () => 320 }, offsetWidth: { get: () => 330 },
    scrollHeight: { get: () => Math.max(value.viewport, descendants(value).reduce((height, item) =>
      height + (item.props['data-event-sequence'] != null ? 70 : item.props.class === 'combat-detail' ? 100 : 0), value.extraHeight)) },
    scrollTop: { get: () => value.top, set: next => {
      value.top = Math.max(0, Math.min(next, value.scrollHeight - value.clientHeight)); value.writes.push(value.top)
    } },
  })
  value.getBoundingClientRect = () => ({ left: 0, right: 330, top: 0, bottom: value.viewport })
  // Browser DOM elements are non-reactive; preserve their identity here too.
  return Vue.markRaw(value)
}
function detach(child) {
  if (!child.parent) return
  child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null
}
const renderer = Vue.createRenderer({
  createElement: tag => node(tag), createText: text => node('#text', text), createComment: text => node('#comment', text),
  insert(child, parent, anchor = null) {
    detach(child); child.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
  },
  remove: detach, parentNode: child => child.parent,
  nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
  setText: (child, text) => { child.text = text },
  setElementText(child, text) { child.children = []; child.text = text },
  patchProp: (child, key, previous, value) => { child.props[key] = value }, setScopeId() {},
})
function evaluate(code, imports, globals) {
  const module = { exports: {} }
  const output = ts.transpileModule(code, { compilerOptions: { module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInNewContext(output, { module, exports: module.exports, require: id => {
    assert.ok(Object.hasOwn(imports, id), `unexpected import ${id}`); return imports[id]
  }, console, ...globals })
  return module.exports
}
function compile(globals) {
  const { descriptor, errors } = parse(source)
  assert.deepEqual(errors, [])
  const script = compileScript(descriptor, { id: 'battle-log-follow-latest' })
  const component = evaluate(script.content, { vue: Vue, './logViewModel': logViewModel }, globals).default
  const template = compileTemplate({ source: descriptor.template.content, filename: 'BattleEventLog.vue',
    id: 'battle-log-follow-latest', compilerOptions: { bindingMetadata: script.bindings } })
  assert.deepEqual(template.errors, [])
  component.render = evaluate(template.code, { vue: Vue }, globals).render
  return component
}
async function fixture({ events = history(), observer = true, neutral = false } = {}) {
  const observers = []
  class ResizeObserver {
    constructor(callback) { this.callback = callback; this.targets = []; this.disconnected = false; observers.push(this) }
    observe(target) { this.targets.push(target) }
    disconnect() { this.disconnected = true }
  }
  const component = compile(observer ? { ResizeObserver } : {})
  const value = Vue.ref(events), root = node('root')
  const app = renderer.createApp({ render: () => Vue.h(component, {
    events: value.value, you: 0, names: ['合成甲', '合成乙'], neutralView: neutral,
  }) })
  app.mount(root); await flush()
  const state = app._instance.subTree.component.setupState
  const list = descendants(root).find(item => item.props.class === 'event-list')
  assert(list)
  return { app, root, state, list, events: value, observers,
    atBottom: () => Math.abs(list.scrollHeight - list.scrollTop - list.clientHeight) <= 2,
    scroll: top => { list.scrollTop = top; list.props.onScroll() },
    wheel: deltaY => list.props.onWheelPassive({ deltaY }),
    resize: async () => { for (const item of observers) item.callback([]); await flush() },
  }
}
const results = []
async function check(name, run) { await run(); results.push(name); console.log(`PASS ${name}`) }

await check('mount and ordinary append follow actual compiled projection', async () => {
  const f = await fixture(); assert(f.atBottom()); assert.equal(f.state.followLatest, true)
  f.events.value.push(event(13)); await flush(); assert(f.atBottom()); f.app.unmount()
})
await check('same-sequence replacement still schedules a follow', async () => {
  const f = await fixture(); const writes = f.list.writes.length
  f.events.value = f.events.value.map(item => ({ ...item, text: item.text + '更新' }))
  await flush(); assert(f.list.writes.length > writes); assert(f.atBottom()); f.app.unmount()
})
await check('merged combat detail changes without a new tail sequence follow', async () => {
  const card = { instanceId: 'synthetic-attacker', cardId: 'synthetic', name: '合成军团', cardType: 'legion', troops: 4000 }
  const combat = (sequence, type, kind, outcome) => ({ sequence, type, text: kind, cards: [card], playerIndex: 0,
    playerCombat: { combatId: 'synthetic-combat', eventKind: kind, outcomeCode: outcome, attackerTroops: 4000 } })
  const f = await fixture({ events: [...history(), combat(13, 'attack', 'attack', 'declared')] })
  const tail = f.state.visible.at(-1).sequence, writes = f.list.writes.length
  f.events.value.push(combat(14, 'defense', 'defense', 'unblocked')); await flush()
  assert.equal(f.state.visible.at(-1).sequence, tail)
  assert(f.list.writes.length > writes); assert(f.atBottom()); f.app.unmount()
})
await check('wheel reading older entries survives new events and content resize', async () => {
  const f = await fixture(); f.wheel(-120); f.scroll(130)
  assert.equal(f.state.followLatest, false)
  f.events.value.push(event(13)); await flush(); assert.equal(f.list.scrollTop, 130)
  f.list.extraHeight += 200; await f.resize(); assert.equal(f.list.scrollTop, 130)
  assert(descendants(f.root).some(item => item.props.class === 'jump-latest')); f.app.unmount()
})
await check('user input before queued nextTick wins over pending follow', async () => {
  const f = await fixture(); f.state.latest(); f.wheel(-120); f.scroll(80)
  await flush(); assert.equal(f.list.scrollTop, 80); assert.equal(f.state.followLatest, false); f.app.unmount()
})
await check('scrolling back to the bottom resumes following', async () => {
  const f = await fixture(); f.wheel(-120); f.scroll(80)
  f.scroll(f.list.scrollHeight); assert.equal(f.state.followLatest, true)
  f.events.value.push(event(13)); await flush(); assert(f.atBottom()); f.app.unmount()
})
await check('explicit latest button restores tail after history reading', async () => {
  const f = await fixture(); f.wheel(-120); f.scroll(80); await flush()
  const button = descendants(f.root).find(item => item.props.class === 'jump-latest')
  button.props.onClick(); await flush(); assert(f.atBottom()); assert.equal(f.state.followLatest, true); f.app.unmount()
})
await check('layout scroll events do not silently opt out of following', async () => {
  const f = await fixture(); f.list.extraHeight += 200; f.list.props.onScroll()
  assert.equal(f.state.followLatest, true); await f.resize(); assert(f.atBottom()); f.app.unmount()
})
await check('touch history reading pauses before the default scroll occurs', async () => {
  const f = await fixture(); f.list.props.onTouchstartPassive({ touches: [{ clientY: 100 }] })
  f.list.props.onTouchmovePassive({ touches: [{ clientY: 180 }] }); assert.equal(f.state.followLatest, false)
  f.scroll(70); f.events.value.push(event(13)); await flush(); assert.equal(f.list.scrollTop, 70); f.app.unmount()
})
await check('downward wheel at the bottom does not pause following', async () => {
  const f = await fixture(); f.wheel(120); assert.equal(f.state.followLatest, true)
  f.events.value.push(event(13)); await flush(); assert(f.atBottom()); f.app.unmount()
})
await check('keyboard scrolling pauses but card button keys do not', async () => {
  const f = await fixture(); f.list.props.onKeydown({ target: {}, key: 'Home' })
  assert.equal(f.state.followLatest, true)
  f.list.props.onKeydown({ target: f.list, key: 'Home' }); f.scroll(0)
  f.events.value.push(event(13)); await flush(); assert.equal(f.list.scrollTop, 0)
  f.list.props.onKeydown({ target: f.list, key: 'End' }); f.scroll(f.list.scrollHeight)
  assert.equal(f.state.followLatest, true); f.app.unmount()
})
await check('native scrollbar pauses while ordinary content clicks keep following', async () => {
  const f = await fixture(); f.list.props.onPointerdown({ target: f.list, pointerType: 'mouse', clientX: 150 })
  assert.equal(f.state.followLatest, true)
  f.list.props.onPointerdown({ target: f.list, pointerType: 'mouse', clientX: 329 }); f.scroll(90)
  f.events.value.push(event(13)); await flush(); assert.equal(f.list.scrollTop, 90)
  f.scroll(f.list.scrollHeight); f.list.props.onPointerup(); assert.equal(f.state.followLatest, true); f.app.unmount()
})
await check('resize observes both viewport and content, including hidden reopening', async () => {
  const f = await fixture(); assert.equal(f.observers[0].targets.length, 2)
  f.list.viewport = 0; await f.resize(); f.events.value.push(event(13)); await flush()
  f.list.viewport = 120; await f.resize(); assert(f.atBottom()); f.app.unmount()
})
await check('summary expansion follows only when not reading history', async () => {
  const f = await fixture(); const before = f.list.writes.length
  f.state.toggleAction(1); await flush(); assert(f.list.writes.length > before)
  f.wheel(-1); f.scroll(90); const paused = f.list.writes.length
  f.state.toggleCombat(2); await flush(); assert.equal(f.list.writes.length, paused); f.app.unmount()
})
await check('no ResizeObserver fallback still follows updates and neutral view', async () => {
  const f = await fixture({ observer: false, neutral: true }); assert(f.atBottom())
  f.events.value.push(event(13)); await flush(); assert(f.atBottom()); f.app.unmount()
})
await check('unmount disconnects observer and cancels queued DOM writes', async () => {
  const f = await fixture(); f.state.latest(); const before = f.list.writes.length
  f.app.unmount(); f.observers[0].callback([]); await flush()
  assert.equal(f.observers[0].disconnected, true); assert.equal(f.list.writes.length, before)
})
console.log(JSON.stringify({ status: 'passed', cases: results.length,
  sourceSha256: createHash('sha256').update(source).digest('hex'),
  scope: 'actual compiled BattleEventLog SFC and actual log projection; synthetic renderer geometry, not browser/device acceptance' }))
