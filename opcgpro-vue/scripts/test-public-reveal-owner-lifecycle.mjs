import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const sfc = parse(readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8'),
  { filename:'GameBoard.vue' }).descriptor
const script = sfc.scriptSetup?.content ?? ''
const ast = ts.createSourceFile('GameBoard.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const names = new Set([
  'resetPublicRevealCardReadiness',
  'markPublicRevealCardReady',
  'markPublicRevealMounted',
  'onPublicRevealMounted',
  'finalizePublicReveal',
  'finishPublicReveal',
  'retirePublicReveal',
  'showNextPublicReveal',
])
const functions = ast.statements
  .filter(node => ts.isFunctionDeclaration(node) && node.name && names.has(node.name.text))
  .map(node => node.getText(ast))
assert.equal(functions.length, names.size, 'all actual public reveal lifecycle functions must be present')
const faceView = ast.statements.flatMap(node => ts.isVariableStatement(node) ? [...node.declarationList.declarations] : [])
  .find(node => node.name.getText(ast) === 'publicRevealFaces')
assert.ok(faceView, 'load handlers require a render-captured immutable owner token')

const harnessSource = `
let publicRevealOwner = null
const publicReveal = { value: null }
const computed = getter => ({ get value() { return getter() } })
const ${faceView.getText(ast)}
const publicRevealReadyInstanceIds = { value: new Set() }
const publicRevealQueue = []
let publicRevealWaiting = false
let publicRevealWaitingReservation = null
let publicRevealTimer = null
let presentationEpoch = 4
let publicRevealOwnerSerial = 0
const modalPresentationPaused = { value: false }
const calls = { acquired: [], ownerReleased: 0 }
const publicRevealAuthorityReleases = new Map()
const presentationVisualOwnerKeys = {value:new Set()}
const props = {game:{players:[]}}
let authorityZone = 'field'
const CSS = {escape:value=>value}
class HTMLElement {
  constructor(ownerKey = '', instanceId = 'same-instance') { this.dataset = {presentationOwnerKey:ownerKey,cardInstanceId:instanceId} }
  closest(selector) { return selector.includes('[data-l12-zone="'+authorityZone+'"]') ? this : null }
}
const document = {querySelectorAll(selector){return [new HTMLElement('',selector.match(/data-card-instance-id="([^"]+)"/)[1])]}}
function acquireAuthoritativeCardVisibility(target) { calls.acquired.push(target.dataset.cardInstanceId); return () => {} }
const nextTick = callback => Promise.resolve().then(callback)
${functions.join('\n')}
return {
  calls,
  publicReveal,
  publicRevealQueue,
  ready: publicRevealReadyInstanceIds,
  HTMLElement,
  setOwner(key, mounted = false, retiring = false) {
    publicRevealOwner = { key, mounted, retiring, release: () => { calls.ownerReleased += 1 } }
  },
  owner() { return publicRevealOwner },
  markPublicRevealCardReady,
  markPublicRevealMounted,
  onPublicRevealMounted,
  publicRevealFaces,
  setAuthority({instanceId='same-instance',zone='field',domZone=zone,hidden=false,moving=false}={}) {
    authorityZone=domZone
    const card={instanceId,hidden}
    props.game.players=[{playerIndex:0,master:{deployedAsLegion:false},field:zone==='field'?[[card]]:[[]],
      relic:zone==='relic'?card:null,extraRelics:zone==='extraRelics'?[card]:[]}]
    presentationVisualOwnerKeys.value=moving?new Set([instanceId]):new Set()
  },
  finishPublicReveal,
  retirePublicReveal,
  showNextPublicReveal,
}
`
const output = ts.transpileModule(harnessSource, {
  compilerOptions:{ module:ts.ModuleKind.None, target:ts.ScriptTarget.ES2022 },
}).outputText
const api = new Function(output)()
const flush = () => new Promise(resolve => setTimeout(resolve, 0))

api.setOwner('4:20:1', true)
api.publicReveal.value = { ownerKey:'4:20:1', cards:[] }
api.publicRevealQueue.push({ sentinel:true })
api.retirePublicReveal('4:20:1')
assert.equal(api.publicReveal.value, null, 'retire removes the current overlay')
assert.equal(api.calls.ownerReleased, 0, 'a mounted overlay keeps its release while leave is running')
await api.showNextPublicReveal()
assert.equal(api.publicRevealQueue.length, 1, 'a new event cannot start during the old leave interval')
api.publicRevealQueue.length = 0

api.finishPublicReveal(new api.HTMLElement('old-owner'))
assert.equal(api.calls.ownerReleased, 0, 'a stale after-leave cannot release the current owner')
api.finishPublicReveal(new api.HTMLElement('4:20:1'))
assert.equal(api.calls.ownerReleased, 1, 'the matching after-leave releases exactly once')
assert.equal(api.owner(), null, 'matching leave clears the owner before the next event')

api.setOwner('4:21:2', false)
api.publicReveal.value = { ownerKey:'4:21:2', cards:[] }
api.retirePublicReveal('4:21:2')
await flush()
assert.equal(api.calls.ownerReleased, 2, 'an overlay blocked before mount retires on the next tick')
assert.equal(api.owner(), null, 'unmounted retirement cannot strand the queue owner')

const card = { instanceId:'same-instance' }
api.setOwner('5:1:3', false)
api.publicReveal.value = { ownerKey:'5:1:3', cards:[card] }
api.markPublicRevealCardReady('4:99:old', card, { kind:'cdn' })
api.markPublicRevealCardReady('5:1:3', card, { kind:'placeholder' })
assert.deepEqual(api.calls.acquired, [], 'old-match loads and placeholder loads cannot claim the new owner')
api.markPublicRevealCardReady('5:1:3', card, { kind:'cdn' })
assert.deepEqual(api.calls.acquired, [], 'a matching decoded image changes readiness without claiming physical authority')
assert.deepEqual([...api.ready.value], ['same-instance'], 'ready state belongs to the matching owner only')

api.markPublicRevealMounted('old-owner')
assert.equal(api.owner().mounted, false, 'a stale mount hook cannot mutate a new owner')
api.markPublicRevealMounted('5:1:3')
assert.equal(api.owner().mounted, true, 'the matching mount hook records the real transition owner')

const oldEntries = api.publicRevealFaces.value
api.setOwner('6:1:4', false)
api.publicReveal.value = { ownerKey:'6:1:4', cards:[card] }
api.onPublicRevealMounted({ el:new api.HTMLElement('5:1:3') })
assert.equal(api.owner().mounted, false, 'the actual vnode hook cannot read the replacement owner key')
api.markPublicRevealCardReady(oldEntries[0].ownerKey, oldEntries[0].card, { kind:'cdn' })
assert.deepEqual(api.calls.acquired, [], 'a retired render entry cannot acquire authority or alter the new owner')
api.onPublicRevealMounted({ el:new api.HTMLElement('6:1:4') })
assert.equal(api.owner().mounted, true)
assert.match(sfc.template.content, /@vue:mounted="onPublicRevealMounted"/)
assert.match(sfc.template.content, /@load="markPublicRevealCardReady\(entry\.ownerKey, entry\.card, \$event\)"/)
assert.doesNotMatch(sfc.template.content, /@load="markPublicRevealCardReady\(publicReveal\.ownerKey/)
const independentCases = [
  ['public field effect', {zone:'field'}, {}],
  ['public relic effect', {zone:'relic'}, {}],
  ['public extra relic effect', {zone:'extraRelics',moving:true}, {}],
  ['public master effect', {zone:'master',instanceId:'master-0'}, {}],
  ['moving master effect', {zone:'master',instanceId:'master-0',moving:true}, {}],
  ['ordinary public reveal is an independent display', {zone:'field'}, {independent:false}],
  ['private hand visibility is not controlled by explanation', {zone:'hand'}, {}],
  ['graveyard reveal is an independent display', {zone:'graveyard'}, {}],
  ['unknown placement grants no physical control', {zone:'unknown'}, {}],
  ['hidden authority is not rewritten', {zone:'field',hidden:true}, {}],
  ['hidden event does not acquire physical control', {zone:'field'}, {hidden:true}],
  ['physical movement owns its own handoff', {zone:'field',moving:true}, {}],
  ['graveyard overlay stays independent', {zone:'field',domZone:'graveyard'}, {}],
  ['continuation image has no physical control', {zone:'field'}, {kind:'entry-continuation'}],
]
for (const [label, authority, presentation] of independentCases) {
  const checked = new Function(output)()
  checked.setAuthority(authority)
  const source = {instanceId:authority.instanceId ?? 'same-instance',hidden:Boolean(presentation.hidden)}
  checked.setOwner('8:1:1')
  checked.publicReveal.value = {ownerKey:'8:1:1',kind:presentation.kind ?? 'full-card',cards:[source],
    independentEffectSourceInstanceId:presentation.independent === false ? undefined : source.instanceId}
  checked.markPublicRevealCardReady('8:1:1',source,{kind:'cdn'})
  assert.deepEqual(checked.calls.acquired,[],label+': central readiness takes no physical lease')
  assert.deepEqual([...checked.ready.value],[source.instanceId],label+': matching image readiness is preserved')
  checked.markPublicRevealCardReady('8:1:1',source,{kind:'cdn'})
  assert.deepEqual(checked.calls.acquired,[],label+': duplicate ready also takes no physical lease')
  assert.equal(checked.ready.value.size,1,label+': duplicate readiness is idempotent')
}
console.log('Public reveal owner lifecycle passed: original20 + 56 independent-display assertions')
