import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const ts = require('typescript')
const scriptsRoot = path.dirname(fileURLToPath(import.meta.url))
const gameRoot = path.resolve(scriptsRoot, '../src/l12/game')
const helperPath = path.join(gameRoot, 'battlefieldImpactPresentation.ts')
const zoneSource = fs.readFileSync(path.join(gameRoot, 'ZoneMovementPresentationLayer.vue'), 'utf8')
const combatSource = fs.readFileSync(path.join(gameRoot, 'CombatMotionPresentationLayer.vue'), 'utf8')

assert.ok(fs.existsSync(helperPath),
  'RED: entry and combat currently have no shared battlefield-impact primitive; card motion is visually detached from its zone')

const helperSource = fs.readFileSync(helperPath, 'utf8')
assert.doesNotMatch(helperSource, /setTimeout|setInterval|requestAnimationFrame|\bqueue\b|\bwatch\s*\(/,
  'scene feedback must stay inside the existing authoritative presentation transaction, not create another scheduler')

const createdElements = []
class Element {
  constructor(name, ownerDocument) {
    this.name = name
    this.ownerDocument = ownerDocument
    this.parentElement = null
    this.children = []
    this.dataset = {}
    this.style = {}
    this.animations = []
    this.removed = false
    this.className = ''
    createdElements.push(this)
  }
  get classList() { return { contains: value => this.className.split(/\s+/).includes(value) } }
  appendChild(child) { child.parentElement = this; this.children.push(child); return child }
  setAttribute(name, value) { this[name] = value }
  remove() {
    if (this.parentElement) this.parentElement.children.splice(this.parentElement.children.indexOf(this), 1)
    this.parentElement = null
    this.removed = true
  }
  closest(selector) {
    for (let node = this; node; node = node.parentElement) {
      if (selector === '.formation-slot' && node.name === 'slot') return node
      if (selector === '.battlefield-half' && node.name === 'half') return node
      if (selector === '[data-l12-game-board]' && node.name === 'board') return node
    }
    return null
  }
  querySelector(selector) {
    if (selector.includes('[data-player-index=')) return this.find(node => node.name === 'half'
      && String(node.dataset.playerIndex) === selector.match(/data-player-index="([^"]+)"/)?.[1])
    if (selector === '.battle-zone') return this.find(node => node.name === 'battle-zone')
    return null
  }
  find(predicate) {
    for (const child of this.children) {
      if (predicate(child)) return child
      const nested = child.find(predicate)
      if (nested) return nested
    }
    return null
  }
  contains(node) { return node === this || this.children.some(child => child.contains(node)) }
  animate(frames, options) {
    const listeners = new Map()
    const animation = {
      target: this, frames, options, playState: 'running',
      addEventListener(type, callback) {
        if (!listeners.has(type)) listeners.set(type, [])
        listeners.get(type).push(callback)
      },
      finish() {
        if (this.playState !== 'running') return
        this.playState = 'finished'
        for (const callback of listeners.get('finish') ?? []) callback()
      },
      cancel() {
        if (this.playState !== 'running') return
        this.playState = 'idle'
        for (const callback of listeners.get('cancel') ?? []) callback()
      },
    }
    this.animations.push(animation)
    return animation
  }
}

const document = { createElement: () => new Element('wave', document) }
const module = { exports: {} }
const transpiled = ts.transpileModule(helperSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText
vm.runInNewContext(transpiled, { module, exports: module.exports, HTMLElement: Element, document, console })
const { battlefieldImpactScopes, battlefieldImpactStrengthFromTroops, startBattlefieldImpact } = module.exports
assert.equal(typeof battlefieldImpactScopes, 'function')
assert.equal(typeof battlefieldImpactStrengthFromTroops, 'function')
assert.equal(typeof startBattlefieldImpact, 'function')

for (const [troops, strength, scopes] of [
  [1000, .35, ['slot']], [4000, .65, ['slot', 'half']], [6000, 1, ['slot', 'half', 'board']],
]) {
  assert.equal(battlefieldImpactStrengthFromTroops(troops), strength)
  assert.deepEqual([...battlefieldImpactScopes(battlefieldImpactStrengthFromTroops(troops))], scopes)
}
assert.deepEqual([...battlefieldImpactScopes(.35)], ['slot'], 'low power stays local to the landing/struck slot')
assert.deepEqual([...battlefieldImpactScopes(.65)], ['slot', 'half'], 'medium power reaches the owning battlefield half')
assert.deepEqual([...battlefieldImpactScopes(1)], ['slot', 'half', 'board'], 'high power reaches the whole felt board')
assert.deepEqual([...battlefieldImpactScopes(0)], [])
assert.deepEqual([...battlefieldImpactScopes(Number.NaN)], [])

function fixture() {
  const board = new Element('board', document)
  board.dataset.l12GameBoard = ''
  const half = board.appendChild(new Element('half', document)); half.dataset.playerIndex = '1'; half.className = 'opponent-half'
  const battleZone = half.appendChild(new Element('battle-zone', document))
  const slot = battleZone.appendChild(new Element('slot', document))
  const card = slot.appendChild(new Element('card', document))
  return { board, half, battleZone, slot, card }
}

function play(strength, direction = 1, disabled = false) {
  const f = fixture()
  const run = startBattlefieldImpact({
    root: f.board, anchor: f.card, playerIndex: 1, strength, direction,
    duration: 820, contactOffset: .82, transactionKey: `event:${strength}:${direction}`, disabled,
  })
  return { ...f, run }
}

for (const [strength, expected] of [[.35, ['slot']], [.65, ['slot', 'half']], [1, ['slot', 'half', 'board']]]) {
  const f = play(strength)
  assert.deepEqual([...f.run.scopes], expected)
  for (const [name, element] of [['slot', f.slot], ['half', f.half], ['board', f.board]]) {
    assert.equal(element.animations.length > 0, expected.includes(name), `${strength} scope/${name}`)
    for (const animation of element.animations) {
      assert.equal(animation.options.duration, 820, 'scene feedback remains inside the existing landing duration')
      assert.equal(animation.options.composite, 'add', 'scene translate must preserve board/mobile transforms')
      assert.ok(animation.frames.every(frame => !Object.hasOwn(frame, 'transform')),
        'surface feedback must use individual translate/filter rather than replace layout transforms')
    }
  }
  const waves = [f.slot, f.half, f.board].flatMap(element => element.children.filter(child => child.name === 'wave'))
  assert.equal(waves.length, expected.length)
  assert.ok(waves.every(wave => wave.style.pointerEvents === 'none'), 'impact paint can never steal board clicks')
  f.run.cancel()
  assert.ok(f.run.animations.every(animation => animation.playState !== 'running'))
  assert.ok(waves.every(wave => wave.removed), 'cancel/match reset must remove transient scene paint')
}

const down = play(.65, 1)
const up = play(.65, -1)
const downHalfFrame = down.half.animations[0].frames.find(frame => typeof frame.translate === 'string'
  && Number.parseFloat(frame.translate.split(' ')[1]) !== 0)
const upHalfFrame = up.half.animations[0].frames.find(frame => typeof frame.translate === 'string'
  && Number.parseFloat(frame.translate.split(' ')[1]) !== 0)
assert.ok(Number.parseFloat(downHalfFrame.translate.split(' ')[1]) > 0)
assert.ok(Number.parseFloat(upHalfFrame.translate.split(' ')[1]) < 0,
  'opposite player sides must recoil away from the combat line')

const off = play(1, 1, true)
assert.deepEqual([...off.run.scopes], [])
assert.equal(off.run.animations.length, 0, 'explicit animation-off must not shake or paint the battlefield')

function actualFunction(source, name, bindings) {
  const script = source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)?.[1]
  assert.ok(script, 'script setup')
  const tree = ts.createSourceFile(`${name}.ts`, script, ts.ScriptTarget.Latest, true)
  const declaration = tree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
  assert.ok(declaration, name)
  const js = ts.transpileModule(declaration.getText(tree), {
    compilerOptions: { target: ts.ScriptTarget.ES2022 },
  }).outputText
  return new Function(...Object.keys(bindings), `${js};return ${name}`)(...Object.values(bindings))
}

const actualEntryImpact = actualFunction(zoneSource, 'legionEntryImpact', { battlefieldImpactStrengthFromTroops })
assert.deepEqual(actualEntryImpact({ troops: 1000, baseTroops: 9000 }), { tier: 'light', strength: .35 })
assert.deepEqual(actualEntryImpact({ troops: 4000 }), { tier: 'medium', strength: .65 })
assert.deepEqual(actualEntryImpact({ troops: 6000 }), { tier: 'heavy', strength: 1 })
const battleMotionStrength = value => typeof value === 'number' && Number.isFinite(value) && value > 0
  ? Math.sqrt(Math.min(value, 10000) / 10000) : 0
const actualCombatImpact = actualFunction(combatSource, 'combatBattlefieldImpactStrength', {
  battlefieldImpactStrengthFromTroops, battleMotionStrength,
})
for (const [troops, expected] of [[1000, .35], [4000, .65], [6000, 1]]) {
  const event = { cards: [{ troops, hidden: false }, { troops: 1000, hidden: false }] }
  assert.equal(actualCombatImpact(event, 1, 0), expected, `combat public source troops/${troops}`)
}
assert.equal(actualCombatImpact({ cards: [{ troops: 6000, hidden: true }] }, 1, 0), 0,
  'a concealed source cannot enlarge the scene from a guessed troop value')

{
  const f = fixture(), sceneCalls = [], remembered = []
  const sceneAnimation = { cancel() {} }
  const presentation = { visualKey: 'target', sceneImpactStarted: false }
  const actualCombatHit = actualFunction(combatSource, 'animateBattlefieldHit', {
    l12BattleMotionDisabled: () => false,
    document: { querySelector: () => f.board },
    combatBattlefieldImpactStrength: actualCombatImpact,
    startBattlefieldImpact: input => { sceneCalls.push(input); return { scopes:['slot','half','board'], animations:[sceneAnimation], cancel() {} } },
    presentationDuration: () => 280,
    remember: animation => remembered.push(animation),
  })
  const event = { sequence: 9, cards: [{ troops: 6000, hidden: false }, { troops: 1000, hidden: false }] }
  actualCombatHit(event, 1, f.card, presentation, 1000, 1)
  actualCombatHit(event, 1, f.card, presentation, 1000, 1)
  assert.equal(sceneCalls.length, 1, 'one visual owner starts one scene response for the transaction')
  assert.equal(sceneCalls[0].strength, 1)
  assert.equal(sceneCalls[0].direction, -1)
  assert.equal(sceneCalls[0].duration, 280)
  assert.equal(sceneCalls[0].transactionKey, '9:target')
  assert.equal(sceneCalls[0].followers, undefined, 'in-board targets inherit scene motion only once')
  assert.deepEqual(remembered, [sceneAnimation], 'scene work joins the existing presentation lifecycle')

  const hiddenPresentation = { visualKey:'hidden', sceneImpactStarted:false }
  actualCombatHit({ sequence:10, cards:[{ troops:6000, hidden:true }] }, 1, f.card,
    hiddenPresentation, 0, 1)
  assert.equal(sceneCalls.length, 1, 'hidden source with no public damage cannot invent a scene impact')
}

assert.match(zoneSource, /from ['"]\.\/battlefieldImpactPresentation['"]/, 'entry must use the shared scene primitive')
assert.match(zoneSource, /function startEntryBoardImpact\([\s\S]*?startBattlefieldImpact\(/,
  'entry scene feedback must start from the existing landing transaction')
assert.match(zoneSource, /function startEntryImpact\([\s\S]*?startEntryBoardImpact\(/)
assert.doesNotMatch(zoneSource, /entryImpactTier !== 'heavy'/,
  'low and medium public troop tiers must no longer be disconnected from their battlefield')
assert.match(combatSource, /from ['"]\.\/battlefieldImpactPresentation['"]/, 'combat must use the same scene primitive')
assert.match(combatSource, /function animateBattlefieldHit\([\s\S]*?startBattlefieldImpact\(/,
  'survival, defeat and master-hit paths need one bounded scene-impact gateway')
assert.doesNotMatch(`${zoneSource}\n${combatSource}`, /addEventListener\(['"]l12-battlefield-impact/,
  'do not introduce a second event bus or presentation queue')

const boundaries = []
function boundary(name, run) {
  try { run(); boundaries.push({ name, passed:true }) }
  catch (error) { boundaries.push({ name, passed:false, error:error.message }) }
}
for (const stage of ['wave-animation', 'second-surface', 'follower']) {
  boundary(`partial scene failure rolls back all work/${stage}`, () => {
    const start = createdElements.length, f = fixture()
    const follower = new Element('follower', document)
    const fail = () => { throw new Error('synthetic-scene-fault') }
    if (stage === 'wave-animation') {
      const create = document.createElement
      document.createElement = () => { const wave = create(); wave.animate = fail; return wave }
    }
    if (stage === 'second-surface') f.half.animate = fail
    if (stage === 'follower') follower.animate = fail
    let thrown = false
    try {
      startBattlefieldImpact({ root:f.board, anchor:f.card, playerIndex:1,
        strength:1, direction:1, duration:820, contactOffset:.82, transactionKey:'fault',
        followers:[{element:follower}] })
    } catch { thrown = true }
    finally { document.createElement = () => new Element('wave', document) }
    const nodes = createdElements.slice(start)
    const running = nodes.flatMap(node => node.animations).filter(animation => animation.playState === 'running')
    const waves = nodes.filter(node => node.name === 'wave' && node.parentElement && !node.removed)
    assert.equal(running.length, 0, 'partial construction cannot leave animations outside owner cleanup')
    assert.equal(waves.length, 0, 'partial construction cannot orphan a transient wave')
    assert.equal(thrown, false, 'optional scene feedback failure must not abort the original card animation')
  })
}
boundary('cross-board anchor fails closed', () => {
  const a = fixture(), b = fixture()
  const run = startBattlefieldImpact({ root:a.board, anchor:b.card, playerIndex:1,
    strength:1, direction:1, duration:820, contactOffset:.82, transactionKey:'foreign' })
  assert.equal(run.animations.length, 0)
})
boundary('missing owned half cannot degrade to arbitrary whole-board shake', () => {
  const f = fixture()
  const run = startBattlefieldImpact({ root:f.board, anchor:null,
    strength:1, direction:1, duration:820, contactOffset:.82, transactionKey:'unknown' })
  assert.equal(run.animations.length, 0)
})
for (const check of ['follower', 'timing']) {
  boundary(`external defeat visual shares actual scene displacement/${check}`, () => {
    const f = fixture(), calls = []
    f.board.offsetWidth = 500
    const wrapper = new Element('external-defeat', document)
    const hit = actualFunction(combatSource, 'animateBattlefieldHit', {
      l12BattleMotionDisabled:()=>false, document:{querySelector:()=>f.board},
      combatBattlefieldImpactStrength:actualCombatImpact,
      startBattlefieldImpact:input=>{calls.push(input);return {animations:[{}]}},
      presentationDuration:()=>280, remember:()=>{}, viewportLayoutRect:()=>({width:1000}),
    })
    hit({sequence:99,cards:[{troops:6000,hidden:false}]}, 1, null,
      {visualKey:'defeated',sceneImpactStarted:false,wrapper}, 1000, 1, .14, 920,
      'cubic-bezier(.3,.58,.28,1)')
    assert.equal(calls.length, 1)
    if (check === 'follower') {
      assert.equal(calls[0].followers?.length, 1)
      assert.equal(calls[0].followers[0].element, wrapper)
      assert.equal(calls[0].followers[0].scale, 2)
    } else {
      assert.equal(calls[0].duration, 920)
      assert.equal(calls[0].contactOffset, .14)
      assert.equal(calls[0].easing, 'cubic-bezier(.3,.58,.28,1)')
    }
  })
}
boundary('structured public attack power controls battlefield range', () => {
  const event = { type:'combat', cards:[{troops:1000,hidden:false}],
    playerCombat:{attackerTroops:6000} }
  assert.equal(actualCombatImpact(event, 1, 1000), 1)
})
boundary('public damage uses the same heavy range without exposing a hidden source', () => {
  const event = { cards:[{troops:9000,hidden:true}] }
  assert.equal(actualCombatImpact(event, 1, 6000), 1)
  assert.equal(actualCombatImpact(event, 1, 0), 0)
})
boundary('public defender power controls retaliation range', () => {
  assert.equal(actualCombatImpact({ cards:[{troops:1000},{troops:1000}],
    playerCombat:{defenderTroops:6000} }, 0, 0), 1)
})
boundary('shared clock and scaled follower exactly match nested scene displacement', () => {
  const f = fixture(), follower = new Element('external', document)
  const run = startBattlefieldImpact({ root:f.board, anchor:f.card, playerIndex:1,
    strength:1, direction:-1, duration:920, contactOffset:.14,
    easing:'cubic-bezier(.3,.58,.28,1)', transactionKey:'clock', followers:[{element:follower,scale:2}] })
  assert.ok(run.animations.every(animation => animation.options.duration === 920
    && animation.options.easing === 'cubic-bezier(.3,.58,.28,1)'))
  const surface = [f.slot,f.half,f.board].map(node => node.animations[0])
  for (let index = 0; index < surface[0].frames.length; index++) {
    const sum = [0,0]
    for (const animation of surface) animation.frames[index].translate.split(' ').forEach((value,axis) => sum[axis] += Number.parseFloat(value))
    follower.animations[0].frames[index].translate.split(' ').forEach((value,axis) =>
      assert.ok(Math.abs(Number.parseFloat(value) - sum[axis] * 2) < 1e-9))
  }
  for (const animation of run.animations) animation.finish()
  assert.ok([f.slot,f.half,f.board].every(node => !node.children.some(child => child.name === 'wave')))
  run.cancel(); run.cancel()
})
boundary('one hostile cancellation cannot strand other scene resources', () => {
  const f = play(1)
  f.run.animations[0].cancel = () => { throw new Error('synthetic-cancel-failure') }
  f.run.cancel()
  assert.ok(f.run.animations.slice(1).every(animation => animation.playState !== 'running'))
  assert.ok([f.slot,f.half,f.board].every(node => !node.children.some(child => child.name === 'wave')))
})
console.log(JSON.stringify({ suite:'Main scene lifecycle boundaries', boundaries }, null, 2))
if (boundaries.some(test => !test.passed)) process.exitCode = 1

console.log(JSON.stringify({
  suite: 'battlefield impact context',
  tiers: { light: 'slot', medium: 'slot+half', heavy: 'slot+half+board' },
  guarantees: ['two directions', 'same duration', 'additive transforms', 'pointer-safe paint', 'cancel cleanup', 'animation-off'],
}, null, 2))
