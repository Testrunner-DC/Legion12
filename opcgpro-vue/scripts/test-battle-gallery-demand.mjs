import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import * as Vue from 'vue'

const board = fs.readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
const script = board.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1]
assert(script, 'inspect the actual battle component')
const tree = ts.createSourceFile('GameBoard.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const calls = []
function visit(node) { if (ts.isCallExpression(node)) calls.push(node); ts.forEachChild(node, visit) }
visit(tree)
const compile = text => ts.transpileModule(text, { compilerOptions: {
  target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext,
} }).outputText.replace(/^"use strict";\s*/, '')
const lifecycle = name => calls.filter(node => ts.isIdentifier(node.expression) && node.expression.text === name)
const mounted = lifecycle('onMounted').find(node => node.getText(tree).includes('lastHiddenRevealSequence'))
assert(mounted, 'execute the original board-mounted callback, not a replacement fixture')
let mountRequests = 0
const sequences = [{ value: 0 }, { value: 0 }, { value: 0 }]
const mount = new Function('props', 'lastHiddenRevealSequence', 'lastPublicRevealSequence', 'lastDiceSequence',
  'alternateArtApi', 'publicAlternateArts', 'galleryViewportReady', `return (${compile(mounted.arguments[0].getText(tree)).trim().replace(/;$/, '')})`)(
  { game: { recentEvents: [] } }, ...sequences,
  { gallery: () => { mountRequests++; return Promise.resolve([]) } }, { value: [] }, { value: false })
mount()
assert.equal(mountRequests, 0, 'entering a battle without viewing details must not fetch the whole public gallery')

const helperSource = fs.readFileSync(new URL('../src/l12/game/battleGalleryRead.ts', import.meta.url), 'utf8')
const helper = await import(`data:text/javascript;base64,${Buffer.from(compile(helperSource)).toString('base64')}`)
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
const init = tree.statements.flatMap(node => ts.isVariableStatement(node) ? [...node.declarationList.declarations] : [])
  .find(node => ts.isIdentifier(node.name) && node.name.text === 'publicGalleryRead')
const watcher = lifecycle('watch').find(node => node.getText(tree).includes('publicGalleryRead.read'))
assert(init && watcher, 'the real component must bind demand to the tested reader')
assert(lifecycle('onBeforeUnmount').some(node => node.getText(tree).includes('publicGalleryRead.dispose()')),
  'board retirement must reject late public metadata')
const mobile = { value: false }, opened = { value: false }, focus = { value: null }, inspector = { value: null }
const ready = { value: false }
const arts = { value: [] }, pending = deferred()
let requests = 0, binding
const reader = new Function('createBattleGalleryReader', 'battleGalleryDetailDemand', 'alternateArtApi',
  'publicAlternateArts', 'mobileLandscapeViewport', 'mobileInspectorOpen', 'focusCard', 'inspectorFocusCard', 'galleryViewportReady', 'watch',
  `${compile(`const ${init.getText(tree)};\n${watcher.getText(tree)};`)}\nreturn publicGalleryRead`)(
  helper.createBattleGalleryReader, helper.battleGalleryDetailDemand,
  { gallery: () => { requests++; return pending.promise } }, arts, mobile, opened, focus, inspector, ready,
  (source, callback, options) => { binding = { source, callback }; if (options?.immediate) callback(source()) })
const update = () => binding.callback(binding.source())
await Promise.resolve()
assert.equal(requests, 0)
mobile.value = true
ready.value = true
focus.value = inspector.value = { cardId: 'synthetic-visible', hidden: false }
update(); await Promise.resolve()
assert.equal(requests, 0, 'a closed mobile inspector must not cause a gallery read')
opened.value = true
focus.value = { cardId: 'synthetic-hidden', hidden: true }
update(); await Promise.resolve()
assert.equal(requests, 0, 'hidden card detail is not public metadata demand')
focus.value = { cardId: 'synthetic-visible', hidden: false }
update(); update(); await Promise.resolve()
assert.equal(requests, 1, 'rapid demand changes share the same in-flight read')
const metadata = [{ id: 'public-art', artist: 'synthetic artist', collaboration: 'synthetic collaboration' }]
pending.resolve(metadata)
assert.equal(await reader.read(), true)
assert.strictEqual(arts.value, metadata, 'metadata is preserved for the existing image/base-card matching consumer')
mobile.value = false
inspector.value = { cardId: 'synthetic-another', hidden: false }
update(); await Promise.resolve()
assert.equal(requests, 1, 'a loaded board reuses its public display data without per-hover reads')
assert.strictEqual(helper.battleGalleryDetailDemand(false, false, null, inspector.value), inspector.value)
assert.equal(helper.battleGalleryDetailDemand(false, true, null, { cardId: '', hidden: false }), null)
assert.equal(helper.battleGalleryDetailDemand(true, true, null, inspector.value), null,
  'mobile prompt-auto focus is not a visible opened card')

let retryCalls = 0, accepted = 0
const retry = helper.createBattleGalleryReader(() => {
  retryCalls++
  return retryCalls === 1 ? Promise.reject(new Error('synthetic read failure')) : Promise.resolve(metadata)
}, () => { accepted++ })
assert.equal(await retry.read(), false)
await Promise.resolve()
assert.equal(retryCalls, 1, 'read failure must not loop or retry in the background')
assert.equal(await retry.read(), true)
assert.equal(retryCalls, 2, 'a later genuine demand may retry')
assert.equal(accepted, 1)
const late = deferred()
let lateAccepted = 0, lateRequests = 0
const retired = helper.createBattleGalleryReader(() => { lateRequests++; return late.promise }, () => { lateAccepted++ })
const activeRead = retired.read()
await Promise.resolve()
retired.dispose(); retired.dispose()
late.resolve(metadata)
assert.equal(await activeRead, false)
assert.equal(lateAccepted, 0, 'retired boards cannot receive a late gallery response')
assert.equal(await retired.read(), false)
assert.equal(lateRequests, 1)
let cancelledBeforeStart = 0
const neverStarted = helper.createBattleGalleryReader(() => { cancelledBeforeStart++; return Promise.resolve(metadata) }, () => assert.fail('retired reader'))
const cancelledRead = neverStarted.read()
neverStarted.dispose()
assert.equal(await cancelledRead, false)
assert.equal(cancelledBeforeStart, 0, 'retirement before the queued loader starts must not initiate a read')

// Execute the actual watch with Vue's real pre-flush scheduler, including the
// setup-time focus before the mounted layout hook identifies a folded phone.
for (const initialFocus of ['replay', 'prompt-auto']) {
  const scope = Vue.effectScope()
  const initial = { cardId: 'synthetic-' + initialFocus, hidden: false }
  const r = Vue.ref(false), m = Vue.ref(false), o = Vue.ref(false)
  const f = Vue.ref(initialFocus === 'replay' ? initial : null), i = Vue.ref(initial)
  let scheduledReads = 0
  const scheduled = helper.createBattleGalleryReader(() => { scheduledReads++; return Promise.resolve(metadata) }, () => {})
  scope.run(() => new Function('battleGalleryDetailDemand', 'publicGalleryRead', 'galleryViewportReady',
    'mobileLandscapeViewport', 'mobileInspectorOpen', 'focusCard', 'inspectorFocusCard', 'watch',
    compile(watcher.getText(tree)))(helper.battleGalleryDetailDemand, scheduled, r, m, o, f, i, Vue.watch))
  await Vue.nextTick(); await Promise.resolve()
  assert.equal(scheduledReads, 0, 'setup-time focus cannot authorize a read before layout is ready')
  m.value = true
  r.value = true
  await Vue.nextTick(); await Promise.resolve()
  assert.equal(scheduledReads, 0, 'layout initialization must preserve folded mobile no-read')
  if (initialFocus === 'prompt-auto') f.value = initial
  o.value = true
  await Vue.nextTick(); await Promise.resolve()
  assert.equal(scheduledReads, 1, 'opening real public detail creates one demand after layout is ready')
  await scheduled.read()
  scheduled.dispose(); scope.stop()
}
reader.dispose(); retry.dispose()
console.log(JSON.stringify({ status: 'passed', actualBoardMountAndWatch: true, realVueInitializationCases: 2, hiddenAndFoldedReads: 0,
  visibleDemandReads: requests, networkCalls: 0, lateResponsesApplied: lateAccepted, onlineBenefitMeasured: false }))
