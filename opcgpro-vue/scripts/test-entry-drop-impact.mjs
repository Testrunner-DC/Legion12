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
}
const frames = framesFor(movement)
const raised = frames.find(frame => frame.offset === .65)
const contact = frames.find(frame => frame.offset === .78)
assert.match(raised.transform, /perspective\(900px\).*translate3d\(550px,-470px,140px\).*rotateX\(-22deg\)/)
assert.equal(raised.easing, 'cubic-bezier(.55,0,.9,.45)', 'descent must accelerate instead of linearly sliding')
assert.match(contact.transform, /translate3d\(550px,-350px,0px\).*scale\(1.08,0.9\)/)
assert.match(raised.filter, /34px 22px/, 'elevated card needs a separated soft shadow')
assert.match(contact.filter, /2px 3px/, 'contact shadow must tighten')
assert.match(frames.at(-1).transform, /scale\(1,1\) rotate\(0deg\)/)
assert.equal(frames.at(-1).offset, 1)
assert.ok(frames.every((frame, index) => index === 0 || frame.offset > frames[index - 1].offset))

const calls = []
let cancelled = 0
const board = { offsetWidth:800, animate: (keyframes, options) => {
  calls.push({ keyframes, options })
  return { cancel() { cancelled++ } }
} }
const root = { value: { closest(selector) {
  assert.equal(selector, '[data-l12-game-board]')
  return board
} } }
const shake = actual('startEntryBoardImpact', { presentationRoot: root,
  viewportLayoutRect: () => ({ width:400 }) })
shake(movement, 820)
assert.equal(calls.length, 1)
assert.equal(calls[0].options.composite, 'add', 'preserve any existing inner translate')
assert.equal(calls[0].options.duration, 820)
assert.equal(calls[0].keyframes.find(frame => frame.offset === .78).translate, '0px 6px')
assert.equal(calls[0].keyframes.at(-1).translate, '0px 0px')
assert.ok(calls[0].keyframes.some(frame => frame.translate === '-16px -8px'))
assert.ok(calls[0].keyframes.every(frame => !('transform' in frame)), 'never replace board scaling/rotation')
for (const change of [
  { entrySimplified: true }, { legionEntry: false }, { entryImpactTier: 'light' },
  { entryImpactTier: 'medium' }, { entryImpactTier: undefined },
]) shake({ ...movement, ...change }, 820)
assert.equal(calls.length, 1, 'off, ordinary moves and lower tiers must not shake the field')
shake(movement, 320)
assert.equal(cancelled, 1, 'reentry must retire the previous animation')
assert.equal(calls[1].options.duration, 320, 'fast motion must retain its short duration')
let ghostFrames
let ghostCancelled = 0
movement.holderWrapper = { animate(frames) { ghostFrames = frames; return { cancel() { ghostCancelled++ } } } }
shake(movement, 820)
assert.equal(ghostFrames.find(frame => frame.offset === .81).translate, '-8px -4px',
  'the landed face must share the scaled field camera displacement')
const cleanup = actual('cleanupEntryImpact')
cleanup(movement)
assert.equal(cancelled, 3)
assert.equal(ghostCancelled, 1)
assert.equal(movement.entryBoardAnimation, undefined)
cleanup(movement)
assert.equal(cancelled, 3, 'cleanup is idempotent')
root.value = null
shake(movement, 820)
assert.equal(calls.length, 3, 'unmounted component cannot shake another board')
assert.match(sfc.descriptor.template.content, /ref="presentationRoot"/)
assert.match(source, /activeEventFaceReady\.value = true[\s\S]*startEntryBoardImpact\(active\.value, duration\)/,
  'decoded template fallback must share the same field impact')
assert.match(source, /65%\{[^}]*perspective\(900px\)[^}]*rotateX\(-22deg\)/,
  'template fallback must keep the depth/drop route')
assert.match(source, /78%\{[^}]*scale\(var\(--move-impact-scale\),var\(--move-impact-squash\)\)/)
console.log('Entry drop/whole-board impact: actual frame physics, scoped ownership, preferences and lifecycle passed')
