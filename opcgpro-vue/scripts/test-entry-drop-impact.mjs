import assert from 'node:assert/strict'
import fs from 'node:fs'
import { parse } from '@vue/compiler-sfc'
import ts from 'typescript'

const source = fs.readFileSync(new URL('../src/l12/game/ZoneMovementPresentationLayer.vue', import.meta.url), 'utf8')
const sfc = parse(source)
assert.deepEqual(sfc.errors, [])
const tree = ts.createSourceFile('movement.ts', sfc.descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true)
function actual(name, bindings = {}) {
  const node = tree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
  assert.ok(node, name)
  const js = ts.transpileModule(node.getText(tree), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(bindings), `${js};return ${name}`)(...Object.values(bindings))
}
const framesFor = actual('legionEntryFrames', {
  entryTransform: actual('entryTransform'), entryDropTransform: actual('entryDropTransform'),
  legionEntryFocusRect: () => ({ x: 400, y: 300, width: 140, height: 196 }),
})
const movement = {
  legionEntry: true, entrySimplified: false, entryImpactTier: 'heavy', entryImpactStrength: 1,
  fromRect: { x: 100, y: 600, width: 72, height: 101 },
  toRect: { x: 650, y: 250, width: 72, height: 101 }, toRotation: 0,
  playerIndex: 1, key: 'entry:heavy', card: { instanceId: 'heavy' },
}
const frames = framesFor(movement)
const raised = frames.find(frame => frame.offset === .65)
const contact = frames.find(frame => frame.offset === .82)
assert.match(raised.transform, /perspective\(900px\).*translate3d\(550px,-470px,140px\).*rotateX\(-18deg\)/)
assert.equal(raised.easing, 'cubic-bezier(.55,0,.9,.45)', 'descent must accelerate instead of linearly sliding')
assert.match(contact.transform, /translate3d\(550px,-350px,0px\).*scale\(0.965,0.9\)/)
assert.match(raised.filter, /34px 22px/, 'elevated card needs a separated soft shadow')
assert.match(contact.filter, /2px 3px/, 'contact shadow must tighten')
assert.match(frames.at(-1).transform, /scale\(1,1\) rotate\(0deg\)/)
assert.equal(frames.at(-1).offset, 1)
assert.ok(frames.every((frame, index) => index === 0 || frame.offset > frames[index - 1].offset))
assert.ok(frames.slice(1).every(frame => !/scale\(1\.0[1-9]/.test(frame.transform)),
  'landing must compress once and settle without a scale overshoot')

const calls = []
let cancelled = 0
const board = { offsetWidth:800 }
const target = { name:'destination-card' }
const root = { value: { closest(selector) {
  assert.equal(selector, '[data-l12-game-board]')
  return board
} } }
const battlefieldImpactStrengthFromTroops = value => typeof value !== 'number' || !Number.isFinite(value) || value <= 0
  ? 0 : value < 3000 ? .35 : value < 6000 ? .65 : 1
const impactFor = actual('legionEntryImpact', { battlefieldImpactStrengthFromTroops })
assert.deepEqual(impactFor({ troops:1000, baseTroops:9000 }), { tier:'light', strength:.35 })
assert.deepEqual(impactFor({ troops:4000 }), { tier:'medium', strength:.65 })
assert.deepEqual(impactFor({ troops:6000 }), { tier:'heavy', strength:1 })
const startBattlefieldImpact = input => {
  calls.push(input)
  let retired = false
  return {
    scopes:input.strength < .5 ? ['slot'] : input.strength < .85 ? ['slot','half'] : ['slot','half','board'],
    animations:[],
    cancel() { if (!retired) { retired = true; cancelled++ } },
  }
}
const shake = actual('startEntryBoardImpact', { presentationRoot: root,
  viewportLayoutRect: () => ({ top:0, height:800, width:400 }),
  cardElementInZone: () => target, startBattlefieldImpact })
for (const [troops, expectedScopes] of [[1000,['slot']], [4000,['slot','half']], [6000,['slot','half','board']]]) {
  const impact = impactFor({ troops })
  const sample = { ...movement, card:{ instanceId:`troops-${troops}` }, key:`entry:${troops}`,
    entryImpactTier:impact.tier, entryImpactStrength:impact.strength }
  shake(sample, 820)
  const call = calls.at(-1)
  assert.deepEqual(sample.entrySceneImpact.scopes, expectedScopes)
  assert.equal(call.duration, 820)
  assert.equal(call.contactOffset, .82)
  assert.equal(call.direction, -1)
  assert.equal(call.anchor, target)
}
const beforeExcluded = calls.length
for (const change of [
  { entrySimplified: true }, { legionEntry: false }, { entryImpactTier: undefined },
]) shake({ ...movement, ...change }, 820)
assert.equal(calls.length, beforeExcluded, 'off, ordinary moves and missing public impact facts stay inert')
movement.holderWrapper = { name:'landing-ghost' }
shake(movement, 320)
assert.equal(calls.at(-1).duration, 320, 'fast motion must retain its short duration')
assert.deepEqual(calls.at(-1).followers, [{ element:movement.holderWrapper, scale:.5 }],
  'the landed face must share the scaled scene displacement')
shake(movement, 320)
assert.equal(cancelled, 1, 'same movement reentry retires its prior scene feedback')
const cleanup = actual('cleanupEntryImpact')
cleanup(movement)
assert.equal(cancelled, 2)
assert.equal(movement.entrySceneImpact, undefined)
cleanup(movement)
assert.equal(cancelled, 2, 'cleanup is idempotent')
root.value = null
shake(movement, 820)
assert.equal(calls.length, beforeExcluded + 2, 'unmounted component cannot shake another board')
assert.match(sfc.descriptor.template.content, /ref="presentationRoot"/)
assert.match(source, /activeEventFaceReady\.value = true[\s\S]*startEntryImpact\(active\.value, duration\)/,
  'decoded template fallback must share the same field impact')
assert.match(source, /65%\{[^}]*perspective\(900px\)[^}]*rotateX\(-18deg\)/,
  'template fallback must keep the depth/drop route')
assert.match(source, /82%\{[^}]*scale\(var\(--move-impact-scale\),var\(--move-impact-squash\)\)/)
assert.match(source, /function observeDestination\(movement: Movement\)[\s\S]*new MutationObserver\(coverCurrent\)[\s\S]*childList:true, subtree:true/,
  'entry owner must cover a same-instance destination mounted after the holder is shown')
assert.match(source, /function revealTarget\(movement: Movement \| null = active\.value\)[\s\S]*destinationObserver\?\.disconnect\(\)/,
  'hide, dispose, cancel and match reset share observer cleanup through revealTarget')
assert.doesNotMatch(source, /move-impact-settle-scale/,
  'landing may not retain the old rebound scale token')
console.log('Entry drop/whole-board impact: actual frame physics, scoped ownership, preferences and lifecycle passed')
