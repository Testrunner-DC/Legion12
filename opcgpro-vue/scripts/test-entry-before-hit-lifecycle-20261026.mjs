import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { fileURLToPath } from 'node:url'

const gameRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../src/l12/game')
const movementSource = fs.readFileSync(path.join(gameRoot, 'ZoneMovementPresentationLayer.vue'), 'utf8')
const combatSource = fs.readFileSync(path.join(gameRoot, 'CombatMotionPresentationLayer.vue'), 'utf8')

function scriptTree(fileName, source) {
  const parsed = parse(source)
  assert.deepEqual(parsed.errors, [], `${fileName} must remain a valid SFC`)
  return ts.createSourceFile(fileName, parsed.descriptor.scriptSetup.content,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
}

const movementTree = scriptTree('ZoneMovementPresentationLayer.ts', movementSource)
const combatTree = scriptTree('CombatMotionPresentationLayer.ts', combatSource)
const coordinatorSource = fs.readFileSync(path.join(gameRoot, 'presentationSequenceCoordinator.ts'), 'utf8')
const coordinatorModule = { exports: {} }
Function('module', 'exports', ts.transpileModule(coordinatorSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(coordinatorModule, coordinatorModule.exports)

function actual(tree, name, bindings = {}) {
  const declaration = tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
  assert.ok(declaration, `${name} must be implemented in the shared presentation layer`)
  const output = ts.transpileModule(declaration.getText(tree), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
  }).outputText
  return Function(...Object.keys(bindings), `${output}; return ${name}`)(...Object.values(bindings))
}

class Element {
  constructor(className = '') {
    this.className = className
    this.dataset = {}
    this.style = {}
    this.children = []
    this.parentElement = null
    this.complete = true
    this.naturalWidth = 100
  }
  appendChild(child) { this.children.push(child); child.parentElement = this; return child }
  cloneNode(deep = false) {
    const clone = new Element(this.className)
    clone.dataset = { ...this.dataset }
    clone.style = { ...this.style }
    clone.complete = this.complete
    clone.naturalWidth = this.naturalWidth
    if (deep) this.children.forEach(child => clone.appendChild(child.cloneNode(true)))
    return clone
  }
  querySelector(selector) {
    if (selector === '[data-l12-zone-animation-face="true"]')
      return this.children.find(child => child.dataset.l12ZoneAnimationFace === 'true') ?? null
    return null
  }
}

const holder = new Element('l12-zone-flight-ghost')
holder.dataset.movementInstanceId = 'generic-entry-instance'
holder.dataset.movementTo = 'field'
holder.dataset.movementToX = '640'
holder.dataset.movementToY = '310'
holder.dataset.movementToWidth = '96'
holder.dataset.movementToHeight = '134'
holder.dataset.movementToRotation = '90'
const face = holder.appendChild(new Element('card-tile'))
face.dataset.l12ZoneAnimationFace = 'true'
face.appendChild(new Element('l12-card-image'))

const querySelector = selector => selector.includes('generic-entry-instance') ? holder : null
const entryLandingHolder = actual(combatTree, 'entryLandingHolder', {
  document: { querySelector }, CSS: { escape: value => value }, HTMLElement: Element,
})
assert.equal(entryLandingHolder('generic-entry-instance'), holder,
  'a confirmed defeat must be able to inherit the still-owned entry holder')

const snapshotDecodedAuthoritativeCardVisual = (_source, clone) => {
  clone.dataset.decodedSnapshot = 'true'
  return true
}
const captureEntryLandingCard = actual(combatTree, 'captureEntryLandingCard', {
  entryLandingHolder, snapshotDecodedAuthoritativeCardVisual, HTMLElement: Element,
})

for (const [view, ownerIndex] of [['actor', 0], ['opponent', 1], ['spectator', undefined]]) {
  const captured = captureEntryLandingCard({
    instanceId: 'generic-entry-instance', cardId: 'any-legion', cardType: 'legion',
    name: `public-${view}`, ownerIndex, hidden: false, tapped: false,
  }, 1)
  assert(captured, `${view} must retain the public entry face until hit/defeat takes ownership`)
  assert.deepEqual({ ...captured.rect }, { left: 592, top: 243, width: 96, height: 134 },
    `${view} defeat must start from the completed landing rectangle`)
  assert.equal(captured.card.tapped, true, `${view} must inherit the rested landing pose`)
  assert.equal(captured.eventIndex, 1)
  assert.equal(captured.pixelsReady, true)
  assert.equal(captured.ghost.dataset.decodedSnapshot, 'true')
}

const stampMovementLandingGeometry = actual(movementTree, 'stampMovementLandingGeometry')
const stamped = new Element('l12-zone-flight-ghost')
stampMovementLandingGeometry({
  holderWrapper: stamped,
  toRect: { x: 180, y: 90, width: 48, height: 67 },
  toRotation: 0,
})
assert.deepEqual({ ...stamped.dataset }, {
  movementToX: '180', movementToY: '90', movementToWidth: '48', movementToHeight: '67', movementToRotation: '0',
}, 'logical narrow/mobile layouts must publish the same complete landing geometry contract')

const trace = []
const coordinator = coordinatorModule.exports.createPresentationSequenceCoordinator()
const entry = coordinator.reserve(13, 20, { visualKey: 'generic-entry-instance' })
const combat = coordinator.reserve(16, 40, { visualKey: 'generic-entry-instance' })
entry.prepareVisualOwner({
  show: () => trace.push('entry-show'),
  hide: () => trace.push('entry-hide'),
  dispose: () => trace.push('entry-dispose'),
})
const entryGrant = entry.waitUntilGranted()
const combatGrant = combat.waitUntilGranted()
const entryRelease = await entryGrant
trace.push('entry-land')
entryRelease(true)
const combatRelease = await combatGrant
assert.equal(coordinator.ownsVisualKey('generic-entry-instance'), true,
  'the completed entry owner must stay visible while its combat successor prepares')
const landed = captureEntryLandingCard({
  instanceId: 'generic-entry-instance', cardId: 'any-legion', cardType: 'legion',
  name: 'generic legion', hidden: false, tapped: false,
}, 1)
assert(landed, 'the granted combat successor must capture the retained landed pixels')
combat.prepareVisualOwner({
  show: () => trace.push('combat-show'),
  hide: () => trace.push('combat-hide'),
  dispose: () => trace.push('combat-dispose'),
})
trace.push('hit-start')
combatRelease(true)
assert.deepEqual(trace, [
  'entry-show', 'entry-show', 'entry-land', 'combat-show', 'entry-dispose', 'hit-start', 'combat-dispose',
], 'one visual owner must hand off only after complete landing and before the hit begins')
assert.equal(coordinator.ownsVisualKey('generic-entry-instance'), false)

assert.match(combatSource,
  /const deferred = [\s\S]*?confirmedDefeatIds[\s\S]*?entryLandingHolder/,
  'only confirmed deaths with an owned entry holder may use the deferred handoff')
assert.match(combatSource,
  /reserve\(job\.event\.sequence, 40, \{ visualKey: pending\.card\.instanceId \}\)[\s\S]*?waitUntilGranted\(\)[\s\S]*?captureEntryLandingCard/,
  'combat must reserve first, wait for the entry owner to finish, then capture the landed face')
assert.match(combatSource,
  /claimedDeferredDefeats\.has\(card\.instanceId\)[\s\S]*?claimedDeferredDefeats\.add\(pending\.card\.instanceId\)/,
  'combat and leave facts in one batch must schedule only one deferred defeat owner per instance')
assert.doesNotMatch(`${movementSource}\n${combatSource}`, /S02-0005|戏法师的傀儡/,
  'the lifecycle fix must be shared and may not special-case the reported card')

// Main integration checks invoke the actual asynchronous component functions.
// DOM/WAAPI are deterministic adapters, not a claim of rendered browser pixels.
const results = []
async function test(name, run) {
  try { await run(); results.push({ name, passed: true }) }
  catch (error) { results.push({ name, passed: false, error: error.message }) }
}
for (const rotation of [0, 90]) for (const tapped of [false, true]) {
  await test(`landed pose/${rotation}/${tapped}`, () => {
    holder.dataset.movementToRotation = String(rotation)
    const card = { instanceId: 'generic-entry-instance', hidden: false, tapped }
    const snapshot = captureEntryLandingCard(card, 0)
    assert.equal(snapshot.card.tapped, rotation === 90)
    assert.equal(card.tapped, tapped, 'presentation may never mutate authority')
  })
}
holder.dataset.movementToRotation = '90'

function asyncFixture() {
  const trace = [], presentations = new Set(), overlays = new Set(), animations = new Set()
  const defeatedInstances = new Set(), concealedInstances = new Set()
  const props = { paused: false, synchronizing: false,
    sequenceCoordinator: coordinatorModule.exports.createPresentationSequenceCoordinator() }
  let releaseTick, pendingTick = false, liveCard = null, decoded = true
  const animationFinishes = []
  const bindings = {
    props, presentations, overlays, animations, defeatedInstances, concealedInstances,
    fieldSnapshots: new Map(), notifyBusy: () => {}, cardElement: () => liveCard,
    nextTick: () => new Promise(resolve => { pendingTick = true; releaseTick = resolve }),
    captureEntryLandingCard: (...args) => decoded ? captureEntryLandingCard(...args) : null,
    prepareDefeat: (captured, event, index, presentation) => {
      trace.push('prepare-hit')
      const wrapper = { style: {}, remove() { trace.push('remove-hit') } }
      overlays.add(wrapper); presentation.wrapper = wrapper
      return { wrapper, damage: {} }
    },
    animateDefeat: (_captured, _event, _index, presentation) => {
      trace.push('hit-start')
      let finish
      const animation = { finished: new Promise(resolve => { finish = resolve }),
        cancel() { animations.delete(animation); finish() } }
      presentation.animations.add(animation); animations.add(animation)
      animationFinishes.push(() => { animations.delete(animation); finish() })
    },
  }
  const declarations = ['finishPresentation', 'presentDeferredDefeat'].map(name =>
    combatTree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name).getText(combatTree)).join('\n')
  const compiled = ts.transpileModule(declarations, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
  }).outputText
  const runtime = Function(...Object.keys(bindings), `let playbackGeneration=1, preparationCount=0;
    ${compiled}; return { run:presentDeferredDefeat, get count(){return preparationCount},
      retire(){playbackGeneration++; for(const p of [...presentations]) finishPresentation(p,false)},
      cancel(){for(const p of [...presentations]) finishPresentation(p,false)} }`)(...Object.values(bindings))
  const entry = props.sequenceCoordinator.reserve(13, 20, { visualKey: 'generic-entry-instance' })
  entry.prepareVisualOwner({ show: () => trace.push('entry-show'), hide: () => {}, dispose: () => trace.push('entry-dispose') })
  return {
    props, trace, runtime, entry, presentations, overlays, animations, defeatedInstances, concealedInstances,
    tickPending: () => pendingTick, flushTick: () => { pendingTick = false; releaseTick() },
    reenter: () => { liveCard = {} }, undecodable: () => { decoded = false },
    finishAnimation: () => animationFinishes.splice(0).forEach(finish => finish()),
    dispose: () => { runtime.cancel(); props.sequenceCoordinator.reset() },
  }
}
const turn = () => new Promise(resolve => setTimeout(resolve, 0))
async function until(condition) {
  for (let index = 0; index < 16 && !condition(); index++) await turn()
  assert(condition(), 'bounded deterministic lifecycle must reach the expected stage')
}
function start(f) {
  f.runtime.run({ event: { sequence: 16, type: 'combat' }, captured: [],
    confirmedDefeatIds: new Set(['generic-entry-instance']) }, {
    card: { instanceId: 'generic-entry-instance', hidden: false, tapped: false }, eventIndex: 1,
  })
}
await test('actual deferred lifecycle lands before one hit and fully releases', async () => {
  const f = asyncFixture()
  try {
    const releaseEntry = await f.entry.waitUntilGranted()
    start(f)
    await turn()
    assert.equal(f.tickPending(), false, 'hit must wait while entry is still flying')
    f.trace.push('entry-land'); releaseEntry(true)
    await until(f.tickPending)
    assert.equal(f.overlays.size, 0)
    f.flushTick(); await until(() => f.trace.includes('hit-start'))
    assert(f.trace.indexOf('entry-land') < f.trace.indexOf('hit-start'))
    assert.equal(f.trace.filter(item => item === 'hit-start').length, 1)
    assert.equal(f.overlays.size, 1)
    f.finishAnimation(); await until(() => f.presentations.size === 0)
    assert.equal(f.runtime.count, 0); assert.equal(f.overlays.size, 0)
    assert.equal(f.props.sequenceCoordinator.ownsVisualKey('generic-entry-instance'), false)
  } finally { f.dispose() }
})
for (const boundary of ['reset', 'cancel', 'hidden', 'sync', 'paused', 'reentry', 'decode-failure']) {
  await test(`actual deferred lifecycle rejects late work/${boundary}`, async () => {
    const f = asyncFixture()
    try {
      const releaseEntry = await f.entry.waitUntilGranted()
      start(f); releaseEntry(true)
      await until(f.tickPending)
      if (boundary === 'reset') f.runtime.retire()
      if (boundary === 'cancel') f.runtime.cancel()
      if (boundary === 'hidden') f.concealedInstances.add('generic-entry-instance')
      if (boundary === 'sync') f.props.synchronizing = true
      if (boundary === 'paused') f.props.paused = true
      if (boundary === 'reentry') f.reenter()
      if (boundary === 'decode-failure') f.undecodable()
      f.flushTick(); await turn(); await turn()
      assert.equal(f.trace.includes('prepare-hit'), false, 'late work must not even mount a ghost')
      assert.equal(f.trace.includes('hit-start'), false)
      assert.equal(f.defeatedInstances.size, 0, 'retired callback cannot poison next generation')
      assert.equal(f.overlays.size, 0); assert.equal(f.presentations.size, 0)
      assert.equal(f.runtime.count, 0)
    } finally { f.dispose() }
  })
}
await test('paused reservation resumes only after completed landing', async () => {
  const f = asyncFixture()
  try {
    const releaseEntry = await f.entry.waitUntilGranted()
    start(f)
    f.props.paused = true
    for (const p of f.presentations) p.reservation.setPaused(true)
    f.trace.push('entry-land'); releaseEntry(true)
    await turn(); await turn()
    assert.equal(f.tickPending(), false); assert.equal(f.overlays.size, 0)
    f.props.paused = false
    for (const p of f.presentations) p.reservation.setPaused(false)
    await until(f.tickPending)
    f.flushTick(); await until(() => f.trace.includes('hit-start'))
    assert(f.trace.indexOf('entry-land') < f.trace.indexOf('hit-start'))
    f.finishAnimation(); await until(() => f.presentations.size === 0)
  } finally { f.dispose() }
})
await test('actual event dispatcher preserves public leave when hidden combat has no captured/deferred visual', () => {
  const call = combatTree.statements.find(node => ts.isExpressionStatement(node)
    && ts.isCallExpression(node.expression) && node.expression.expression.getText(combatTree) === 'watch'
    && ts.isArrowFunction(node.expression.arguments[0])
    && node.expression.arguments[0].body.getText(combatTree).startsWith('[props.revision,')).expression
  const dispatcher = ts.transpileModule(`const dispatch=${call.arguments[1].getText(combatTree)}`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
  }).outputText
  const publicCard = { instanceId: 'ordinary-known-field', hidden: false }
  const props = { revision: 2, synchronizing: false, events: [
    { sequence: 1, type: 'combat', cards: [{ ...publicCard, hidden: true }] },
    { sequence: 2, type: 'leave', text: '公开军团阵亡', cards: [publicCard] },
  ] }
  const live = new Element(), captured = []
  const bindings = {
    props, reset: () => { throw new Error('unexpected reset') }, nextTick: () => Promise.resolve(),
    refreshFieldSnapshots: () => {}, isDefeatLeave: actual(combatTree, 'isDefeatLeave'),
    captureCards: actual(combatTree, 'captureCards', {
      concealedInstances: new Set(), fieldSnapshots: new Map(), presentations: new Set(),
      finishPresentation: () => {}, cardElement: () => live,
      snapshotCard: () => ({ ghost: live, rect: {}, pixelsReady: true }), HTMLElement: Element,
    }),
    masterDamageValue: () => null, presentMasterHit: () => { throw new Error('unexpected master hit') },
    presentDefeats: job => captured.push(...job.captured.map(item => item.card.instanceId)),
    entryLandingHolder: () => null, presentDeferredDefeat: () => { throw new Error('unexpected deferred hit') },
  }
  const dispatch = Function(...Object.keys(bindings),
    `let initialized=true,lastRevision=1,lastSequence=0,playbackGeneration=1; ${dispatcher}; return dispatch`)(...Object.values(bindings))
  dispatch()
  assert.deepEqual(captured, ['ordinary-known-field'], 'uncaptured combat may not claim and swallow the public leave fallback')
})
console.log(JSON.stringify({ suite: 'entry-before-hit actual asynchronous lifecycle', results }, null, 2))
if (results.some(result => !result.passed)) process.exitCode = 1

console.log(JSON.stringify({
  suite: 'entry before hit/defeat shared lifecycle',
  authorityOrder: ['enter', 'combat', 'leave'],
  reservationOrder: ['entry(sequence, priority 20)', 'combat(sequence, priority 40)'],
  ownerHandoff: ['entry flight owns public pixels', 'entry completes at field rectangle', 'combat inherits landed pixels', 'hit/defeat releases once'],
  views: ['actor', 'opponent', 'spectator'],
  layouts: ['wide logical canvas', 'narrow/mobile logical canvas'],
}, null, 2))
