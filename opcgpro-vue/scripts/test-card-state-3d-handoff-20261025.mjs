import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url), ts = require('typescript')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src/l12/game')
const source = fs.readFileSync(path.join(root, 'CardStateTransitionLayer.vue'), 'utf8')
const script = source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
const ast = ts.createSourceFile('CardStateTransitionLayer.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const statements = ast.statements.filter(node => !ts.isImportDeclaration(node))
const names = statements.flatMap(node => ts.isFunctionDeclaration(node) && node.name ? [node.name.text]
  : ts.isVariableStatement(node) ? node.declarationList.declarations.flatMap(item => ts.isIdentifier(item.name) ? [item.name.text] : []) : [])
const flush = async () => { for (let index = 0; index < 30; index++) await Promise.resolve() }

// Execute the real component functions and coordinator against a tiny DOM/WAAPI
// adapter. These tests prove lifecycle/order contracts, not rendered 3D pixels.
function fixture({ paused = false, mode = 'standard' } = {}) {
  let now = 0, timerId = 0
  const timers = new Map(), watches = [], unmounts = [], animations = []
  const setTimer = (fn, delay = 0) => { const id = ++timerId; timers.set(id, { at: now + delay, fn }); return id }
  class Element {
    constructor(tag = 'div') {
      this.tagName = tag.toUpperCase(); this.children = []; this.parentElement = null; this.dataset = {}; this.attributes = new Map()
      this.style = { visibility: '', border: '1px solid #eee', outline: 'auto', background: '#0b0d0e', boxShadow: '0 0 0 2px cyan' }
      this.className = ''; this.listeners = new Map(); this.rect = { left: 80, top: 90, width: 100, height: 140 }
      this.complete = true; this.naturalWidth = 500
    }
    get classList() { return { contains: value => this.className.split(/\s+/).includes(value),
      remove: (...values) => { this.className = this.className.split(/\s+/).filter(value => !values.includes(value)).join(' ') } } }
    get isConnected() { return this.connected || Boolean(this.parentElement?.isConnected) }
    appendChild(child) { this.children.push(child); child.parentElement = this; return child }
    remove() { if (this.parentElement) this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1); this.parentElement = null }
    removeAttribute(name) { this.attributes.delete(name) }
    getBoundingClientRect() { return this.rect }
    querySelectorAll(selector) {
      const descendants = this.children.flatMap(child => [child, ...child.querySelectorAll('*')])
      return selector === '*' ? descendants : descendants.filter(child => selector === 'img' ? child.tagName === 'IMG' : false)
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null }
    cloneNode() { const clone = new Element(this.tagName); clone.style = { ...this.style }; clone.className = this.className
      clone.complete = this.complete; clone.naturalWidth = this.naturalWidth
      this.children.forEach(child => clone.appendChild(child.cloneNode(true))); return clone }
    addEventListener(name, fn) { if (!this.listeners.has(name)) this.listeners.set(name, new Set()); this.listeners.get(name).add(fn) }
    removeEventListener(name, fn) { this.listeners.get(name)?.delete(fn) }
    decode() { return Promise.resolve() }
    animate(frames, options) {
      let resolve, reject
      const result = { frames, options, target: this, playState: 'running', onfinish: null, oncancel: null,
        finished: new Promise((yes, no) => { resolve = yes; reject = no }),
        commitStyles() { this.target.style.transform = this.frames.at(-1).transform },
        cancel() { if (this.playState !== 'running') return; this.playState = 'idle'; this.oncancel?.(); reject(new Error('canceled')) },
        finish() { this.playState = 'finished'; this.onfinish?.(); resolve() } }
      animations.push(result); return result
    }
  }
  const host = new Element(); host.connected = true
  const authority = new Element('button'); authority.connected = true; authority.className = 'card-tile selected'
  authority.appendChild(new Element('img'))
  const target = new Element(); target.connected = true; target.rect = { left: 160, top: 180, width: 100, height: 140 }
  const props = { paused, players: [], events: [], matchId: 'state-fixture', revision: 1, synchronizing: false, playbackSpeed: null }
  const environment = { console, AbortController, Map, Set, WeakMap, Promise,
    setTimeout: setTimer, clearTimeout: id => timers.delete(id), requestAnimationFrame: fn => setTimer(fn, 16),
    HTMLElement: Element, HTMLImageElement: Element, CSS: { escape: value => value },
    getComputedStyle: element => ({ display: 'block', visibility: element.style.visibility || 'visible' }),
    document: { createElement: tag => new Element(tag), querySelector: selector => selector.includes('target-unit') ? target : authority },
    landscapeTeleportElement: () => host, viewportLayoutRect: element => element.rect,
    settleElementGeometry: () => {}, snapshotDecodedAuthoritativeCardVisual: () => true,
    l12BattleMotionDisabled: () => mode === 'off',
    l12AnimationDuration: (standard, minimum) => Math.max(minimum, Math.round(standard * (mode === 'off' ? 0 : mode === 'fast' ? .55 : 1))),
    defineProps: () => props, withDefaults: value => value, ref: value => ({ value }),
    watch: (select, callback) => { const watch = { select, callback }; watches.push(watch); return () => watches.splice(watches.indexOf(watch), 1) },
    nextTick: callback => Promise.resolve().then(callback), onBeforeUnmount: callback => unmounts.push(callback) }
  function load(name) {
    const module = { exports: {} }
    const text = fs.readFileSync(path.join(root, name), 'utf8')
    vm.runInNewContext(ts.transpileModule(text, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText,
      { ...environment, module, exports: module.exports })
    return module.exports
  }
  const coordinatorModule = load('presentationSequenceCoordinator.ts'), claims = load('visualTransitionProjection.ts')
  const visibility = load('authoritativeCardVisibility.ts')
  Object.assign(environment, claims, visibility)
  props.sequenceCoordinator = coordinatorModule.createPresentationSequenceCoordinator()
  vm.createContext(environment)
  vm.runInContext(ts.transpileModule(statements.map(node => node.getText(ast)).join('\n')
    + '\nglobalThis.__state=Object.defineProperties({}, {' + names.map(name => `${name}:{get:()=>${name}}`).join(',') + '})',
    { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText, environment)
  const state = environment.__state
  async function advance(ms = 0) {
    const end = now + ms
    for (let index = 0; index < 1000; index++) {
      await flush()
      const item = [...timers.entries()].filter(([, timer]) => timer.at <= end).sort((a, b) => a[1].at - b[1].at)[0]
      if (!item) { now = end; await flush(); return }
      now = item[1].at; timers.delete(item[0]); item[1].fn()
    }
    throw new Error('Presentation preparation must be bounded')
  }
  function prepare(fromTapped, toTapped, { sequence = 1, decoded = true, attack = false, attackTroops } = {}) {
    const reservation = props.sequenceCoordinator.reserve(sequence, 30, { visualKey: 'unit' })
    reservation.setPaused(props.paused)
    const ghost = authority.cloneNode(true)
    if (!decoded) { ghost.children[0].complete = false; ghost.children[0].naturalWidth = 0 }
    const transition = state.createPreparedTransition({ key: `state-fixture:${sequence}`, instanceId: 'unit', fromTapped, toTapped,
      sourceRect: { ...authority.rect }, sourceGhost: ghost, presentationReservation: reservation,
      ...(attack ? { attackSequence: sequence, attackTargetInstanceId: 'target-unit', attackTroops } : {}) }, ghost, decoded)
    authority.className = `card-tile${toTapped ? ' tapped' : ''}`
    return transition
  }
  return { state, props, claims, authority, host, animations, advance, prepare,
    leases: () => visibility.authoritativeCardVisibilityLeaseCount(authority),
    visibleHolders: () => host.children.filter(child => child.style.visibility === 'visible'),
    setPaused(value) { const previous = props.paused; props.paused = value
      for (const watch of [...watches]) if (typeof watch.select() === 'boolean') watch.callback(value, previous) },
    async decode(transition) { const image = transition.sourceGhost.children[0]; image.complete = true; image.naturalWidth = 500
      for (const callback of [...(image.listeners.get('load') || [])]) callback(); await flush() },
    dispose() { unmounts.forEach(callback => callback()); props.sequenceCoordinator.reset() } }
}

const cases = []
async function test(name, run) { try { await run(); cases.push({ name, passed: true }) } catch (error) { cases.push({ name, passed: false, error: error.stack }) } }

await test('actual claim function ignores repeated revisions and recovery baselines but preserves real rest then ready', () => {
  const f = fixture(); try {
    const state = f.claims.createCardStateClaimState(), active = new Map([['unit', { instanceId: 'unit', tapped: false }]])
    f.claims.resetCardStateClaimState(state, 1, active, 10)
    const events = [
      { sequence: 11, playerCardStateTransition: { instanceId: 'unit', fromTapped: false, toTapped: true } },
      { sequence: 12, playerCardStateTransition: { instanceId: 'unit', fromTapped: true, toTapped: false } },
    ]
    const changes = f.claims.claimCardStateTransitions(state, 2, active, events)
    assert.deepEqual(Array.from(changes, change => [change.fromTapped, change.toTapped, change.orderingSequence]), [[false, true, 11], [true, false, 12]])
    assert.equal(f.claims.claimCardStateTransitions(state, 2, active, events).length, 0)
    f.claims.resetCardStateClaimState(state, 7, active, 20)
    assert.equal(f.claims.claimCardStateTransitions(state, 7, active, events).length, 0)
  } finally { f.dispose() }
})
await test('a conflicting payload at the accepted revision cannot create motion or rewrite the visual baseline', () => {
  const f=fixture();try{
    const state=f.claims.createCardStateClaimState(),baseline=new Map([['unit',{instanceId:'unit',tapped:false}]])
    f.claims.resetCardStateClaimState(state,7,baseline,30)
    const contradictory=new Map([['unit',{instanceId:'unit',tapped:true}]])
    assert.equal(f.claims.claimCardStateTransitions(state,7,contradictory,[]).length,0)
    assert.equal(state.states.get('unit').tapped,false,'same-revision object replacement is not a new authoritative fact')
    assert.equal(state.revision,7);assert.equal(state.lastEventSequence,30)
  }finally{f.dispose()}
})
for (const [from, to] of [[false, true], [true, false]]) await test(`ready/rest has one lifted 3D motion and an exact terminal authority/${from}`, async () => {
  const f = fixture(); try {
    const transition = f.prepare(from, to); f.state.queue.push(transition); f.state.showNext(); await f.advance()
    assert.equal(f.animations.length, 1); assert.equal(f.leases(), 1); assert.equal(f.visibleHolders().length, 1)
    const animation = f.animations[0]
    assert(animation.frames.some(frame => /perspective\(/.test(frame.transform) && /rotateX\(/.test(frame.transform)), 'ordinary state motion needs a genuine lifted/tilted pose')
    assert.equal(animation.frames.at(-1).transform, `translate3d(0px,0px,0) rotate(${to ? 90 : 0}deg) scale(1)`)
    assert(animation.options.duration <= 380)
    animation.finish(); await f.advance(); assert.equal(f.leases(), 0); assert.equal(f.host.children.length, 0)
  } finally { f.dispose() }
})
await test('attack remains one lunge/rest transaction with contact and lifted 3D pose', async () => {
  const f = fixture(); try {
    f.state.queue.push(f.prepare(false, true, { attack: true })); f.state.showNext(); await f.advance()
    assert.equal(f.animations.length, 1)
    const animation = f.animations[0]
    assert(animation.frames.some(frame => /perspective\(/.test(frame.transform) && /rotateX\(/.test(frame.transform)))
    assert(animation.frames.some(frame => frame.offset === .38)); assert(animation.frames.some(frame => frame.offset === .56))
    assert.equal(animation.frames.at(-1).transform, 'translate3d(0px,0px,0) rotate(90deg) scale(1)')
    animation.finish(); await f.advance(); assert.equal(f.animations.length, 1)
  } finally { f.dispose() }
})
await test('public attack-time troops alone set a bounded monotone motion strength', () => {
  const f = fixture(); try {
    const strengths = [1000, 3000, 6000, 10000].map(f.claims.battleMotionStrength)
    assert(strengths.every((value, index) => value > 0 && value <= 1 && (!index || value > strengths[index - 1])))
    assert.equal(f.claims.battleMotionStrength(1e30), 1, 'large values cannot launch cards off the board')
    for (const value of [undefined, null, '10000', NaN, Infinity, -1000])
      assert.equal(f.claims.battleMotionStrength(value), 0)
    for (const card of [{hidden:true,troops:10000}, {troops:NaN}, {troops:'10000'}, undefined])
      assert.equal(f.claims.publicBattleMotionTroops(card), undefined)
    assert.equal(f.claims.publicBattleMotionTroops({troops:0,baseTroops:10000}), 0, 'real zero never borrows printed power')
    for (const hidden of [false, true]) {
      const state = f.claims.createCardStateClaimState(), before = new Map([['unit',{instanceId:'unit',tapped:false}]])
      f.claims.resetCardStateClaimState(state,1,before,0)
      const events = [{sequence:1,type:'attack',cards:[{instanceId:'unit',hidden,troops:6000}]},
        {sequence:2,playerCardStateTransition:{instanceId:'unit',fromTapped:false,toTapped:true}}]
      const changes = f.claims.claimCardStateTransitions(state,2,new Map([['unit',{instanceId:'unit',tapped:true}]]),events)
      assert.equal(changes[0].attackTroops, hidden ? undefined : 6000)
    }
  } finally { f.dispose() }
})
await test('higher public troops strengthen the same attack without extending time or changing its final rest', async () => {
  const poses = []
  for (const attackTroops of [1000, 6000, 10000]) {
    const f = fixture(); try {
      f.state.queue.push(f.prepare(false,true,{attack:true,attackTroops})); f.state.showNext(); await f.advance()
      assert.equal(f.animations.length,1); assert.equal(f.visibleHolders().length,1)
      const animation = f.animations[0], contact = animation.frames.find(frame => frame.offset === .38)
      const match = contact.transform.match(/translate3d\(([-.\d]+)px,([-.\d]+)px,([-.\d]+)px\)/)
      assert(match); poses.push({distance:Math.hypot(+match[1],+match[2]),height:+match[3]})
      assert.equal(animation.options.duration,380)
      assert.equal(animation.frames.at(-1).transform,'translate3d(0px,0px,0) rotate(90deg) scale(1)')
      animation.finish(); await f.advance(); assert.equal(f.host.children.length,0)
    } finally { f.dispose() }
  }
  assert(poses.every((pose,index) => !index || pose.distance > poses[index-1].distance && pose.height > poses[index-1].height))
})
await test('only animation clone/holder lose UI frame while image and drop shadow remain', () => {
  const f = fixture(); try {
    const transition = f.prepare(false, true), ghost = transition.sourceGhost, holder = transition.holderWrapper
    for (const node of [ghost, holder]) { assert.equal(node.style.border, '0'); assert.equal(node.style.outline, 'none'); assert.equal(node.style.background, 'transparent') }
    assert.equal(ghost.style.boxShadow, 'none'); assert(holder.style.filter.includes('drop-shadow'))
    assert.equal(ghost.children[0].tagName, 'IMG'); assert.equal(f.authority.style.border, '1px solid #eee')
  } finally { f.dispose() }
})
await test('paused preparation holds old pixels and performs one state turn only after grant', async () => {
  const f = fixture({ paused: true }); try {
    f.state.queue.push(f.prepare(false, true)); f.state.showNext(); await f.advance()
    assert.equal(f.animations.length, 0); assert.equal(f.leases(), 1); assert.equal(f.visibleHolders().length, 1)
    f.setPaused(false); await f.advance(); assert.equal(f.animations.length, 1)
    f.animations[0].finish(); await f.advance(); f.setPaused(false); await f.advance(); assert.equal(f.animations.length, 1)
  } finally { f.dispose() }
})
await test('late decode after final authority painted must not rewind even while paused', async () => {
  const f = fixture({ paused: true }); try {
    const transition = f.prepare(false, true, { decoded: false })
    await f.advance(16); assert.equal(f.leases(), 0)
    await f.decode(transition)
    assert.equal(await transition.visualReady, false, 'a painted final state cannot be replaced by newly decoded old pixels')
    assert.equal(f.leases(), 0); assert.equal(f.visibleHolders().length, 0)
  } finally { f.dispose() }
})
await test('two true state changes hand off one owner in sequence without losing either fact', async () => {
  const f = fixture(); try {
    f.state.queue.push(f.prepare(false, true, { sequence: 11 }), f.prepare(true, false, { sequence: 12 }))
    f.state.showNext(); await f.advance(); assert.equal(f.animations.length, 1); assert.equal(f.leases(), 1)
    f.animations[0].finish(); await f.advance(); assert.equal(f.animations.length, 2)
    assert.equal(f.visibleHolders().length, 1); assert.equal(f.leases(), 1)
    f.animations[1].finish(); await f.advance(); assert.equal(f.leases(), 0); assert.equal(f.host.children.length, 0)
  } finally { f.dispose() }
})
for (const mode of ['fast', 'off']) await test('explicit game preference preserves bounded endpoints/' + mode, async () => {
  const f = fixture({ mode }); try {
    f.state.queue.push(f.prepare(false, true, { attack: true })); f.state.showNext(); await f.advance()
    const animation = f.animations[0]
    assert(animation.options.duration <= (mode === 'fast' ? 209 : 80))
    if (mode === 'off') {
      assert.equal(animation.frames.length, 2); assert(animation.frames.every(frame => !/perspective|rotateX/.test(frame.transform)))
    }
    assert.equal(animation.frames.at(-1).transform, 'translate3d(0px,0px,0) rotate(90deg) scale(1)')
  } finally { f.dispose() }
})
await test('unmount invalidates prepared work and late completion cannot restore a ghost', async () => {
  const f = fixture(); f.state.queue.push(f.prepare(false, true)); f.state.showNext(); await f.advance()
  const animation = f.animations[0]; f.dispose(); animation.finish(); await f.advance()
  assert.equal(f.leases(), 0); assert.equal(f.host.children.length, 0)
})

console.log(JSON.stringify({ suite: 'actual CardState 3D/ownership functions with deterministic DOM adapter', total: cases.length,
  passed: cases.filter(item => item.passed).length, failed: cases.filter(item => !item.passed).length, cases }, null, 2))
if (cases.some(item => !item.passed)) process.exitCode = 1
