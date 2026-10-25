import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const sfc = parse(readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8'),
  { filename:'GameBoard.vue' }).descriptor
const script = sfc.scriptSetup?.content ?? ''
const ast = ts.createSourceFile('GameBoard.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const names = new Set([
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
const calls = { acquired: [], authorityReleased: 0, ownerReleased: 0 }
class HTMLElement { constructor(ownerKey = '') { this.dataset = { presentationOwnerKey: ownerKey } } }
const nextTick = callback => Promise.resolve().then(callback)
function releasePublicRevealAuthority() { calls.authorityReleased += 1; publicRevealReadyInstanceIds.value = new Set() }
function acquirePublicRevealAuthority(card) { calls.acquired.push(card.instanceId) }
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
assert.deepEqual(api.calls.acquired, ['same-instance'], 'only the matching decoded image claims exact authority')
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
assert.deepEqual(api.calls.acquired, ['same-instance'], 'a retired render entry cannot claim the same card in a new owner')
api.onPublicRevealMounted({ el:new api.HTMLElement('6:1:4') })
assert.equal(api.owner().mounted, true)
assert.match(sfc.template.content, /@vue:mounted="onPublicRevealMounted"/)
assert.match(sfc.template.content, /@load="markPublicRevealCardReady\(entry\.ownerKey, entry\.card, \$event\)"/)
assert.doesNotMatch(sfc.template.content, /@load="markPublicRevealCardReady\(publicReveal\.ownerKey/)
console.log('Public reveal owner lifecycle passed: 20/20 assertions including real render callback capture')
