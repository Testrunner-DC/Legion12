import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const source = readFileSync(new URL('../src/l12/game/authoritativeCardVisibility.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { acquireAuthoritativeCardVisibility, authoritativeCardVisibilityLeaseCount } = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)
const node = visibility => ({ style:{ visibility } })

const shared = node('')
const releaseZone = acquireAuthoritativeCardVisibility(shared)
const releaseState = acquireAuthoritativeCardVisibility(shared)
assert.equal(authoritativeCardVisibilityLeaseCount(shared), 2, 'the shared lease exposes its active owner count for regression verification')
assert.equal(shared.style.visibility, 'hidden', 'overlapping layers share the hidden authority node')
releaseZone()
assert.equal(shared.style.visibility, 'hidden', 'one cancelled layer cannot reveal a node still owned by another layer')
releaseZone()
assert.equal(shared.style.visibility, 'hidden', 'release is idempotent')
releaseState()
assert.equal(shared.style.visibility, '', 'the last inverse-order release restores the original inline value')
assert.equal(authoritativeCardVisibilityLeaseCount(shared), 0, 'the last release removes the lease entry')

const prehidden = node('collapse')
const releasePrehiddenA = acquireAuthoritativeCardVisibility(prehidden)
const releasePrehiddenB = acquireAuthoritativeCardVisibility(prehidden)
releasePrehiddenB()
releasePrehiddenA()
assert.equal(prehidden.style.visibility, 'collapse', 'a non-empty original inline visibility is restored exactly')

const replaced = node('')
const replacement = node('visible')
const releaseReplaced = acquireAuthoritativeCardVisibility(replaced)
const releaseReplacement = acquireAuthoritativeCardVisibility(replacement)
releaseReplaced()
assert.equal(replaced.style.visibility, '', 'a detached/replaced old node releases only its own lease')
assert.equal(replacement.style.visibility, 'hidden', 'releasing the old node never mutates its replacement')
releaseReplacement()
assert.equal(replacement.style.visibility, 'visible', 'the replacement restores its independent original value')

const unmounted = node('')
const cleanup = [acquireAuthoritativeCardVisibility(unmounted), acquireAuthoritativeCardVisibility(unmounted)]
cleanup.splice(0).forEach(release => release())
assert.equal(unmounted.style.visibility, '', 'unmount/reset cleanup releases every outstanding owner')

const exceptional = node('')
try {
  const release = acquireAuthoritativeCardVisibility(exceptional)
  try { throw new Error('animation failed') } finally { release() }
} catch {}
assert.equal(exceptional.style.visibility, '', 'animation exceptions release the visibility lease in finally paths')

// Central explanation is independent of every physical card. Run the actual
// ready handler against real shared leases; this encodes the user's rule.
const board = parse(readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')).descriptor
const boardAst = ts.createSourceFile('GameBoard.ts', board.scriptSetup.content, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const names = new Set(['markPublicRevealCardReady','resetPublicRevealCardReadiness',
  'acquirePublicRevealAuthority','releasePublicRevealAuthority'])
const actualFunctions = boardAst.statements.filter(item => ts.isFunctionDeclaration(item) && names.has(item.name?.text))
  .map(item => item.getText(boardAst))
assert.ok(actualFunctions.some(fn=>fn.startsWith('function markPublicRevealCardReady')), 'use the real central image handler')
const contractFactory = ts.transpileModule(`
const calls={acquired:0}
function acquireAuthoritativeCardVisibility(target) {
  calls.acquired++
  return bindings.acquireAuthoritativeCardVisibility(target)
}
const publicRevealAuthorityReleases=new Map()
const publicRevealReadyInstanceIds={value:new Set()}
const publicReveal={value:null}
let publicRevealOwner={key:'central-owner',retiring:false}
const presentationVisualOwnerKeys={value:new Set()}
const props={game:{players:[],revision:100}}
const CSS={escape:value=>value}
class HTMLElement {
  constructor(zone,visibility) { this.zone=zone;this.style={visibility};this.dataset={cardInstanceId:'source-card'} }
  matches() { return false }
  querySelector() { return null }
  closest(selector) { return selector.includes('[data-l12-zone="'+this.zone+'"]')?this:null }
}
let target
const document={querySelectorAll(){return [target]}}
${actualFunctions.join('\n')}
return {
  configure({zone='relic',domZone=zone==='extraRelics'?'relic':zone,tapped=false,hidden=false,
    eventHidden=false,independent=true,kind='full-card',departed=false,wrongInstance=false,visibility=''}={}) {
    target=new HTMLElement(domZone,visibility)
    const authority={instanceId:wrongInstance?'another-instance':'source-card',tapped,hidden,cardId:'S01-0117'}
    const eventCard={instanceId:'source-card',tapped,hidden:eventHidden,cardId:'S01-0117'}
    props.game.players=[{playerIndex:0,master:{deployedAsLegion:false},
      field:zone==='field'&&!departed?[[authority]]:[[]],
      relic:zone==='relic'&&!departed?authority:null,
      extraRelics:zone==='extraRelics'&&!departed?[authority]:[]}]
    publicReveal.value={ownerKey:'central-owner',kind,cards:[eventCard],
      independentEffectSourceInstanceId:independent?eventCard.instanceId:undefined}
    return {target,eventCard}
  },
  start(card,key='central-owner',kind='cdn') { markPublicRevealCardReady(key,card,{kind}) },
  end() {
    if(typeof resetPublicRevealCardReadiness==='function')resetPublicRevealCardReadiness()
    else releasePublicRevealAuthority()
  },
  setPhysicalOwner(owned) { presentationVisualOwnerKeys.value=owned?new Set(['source-card']):new Set() },
  advanceRevision() { props.game.revision++ },
  retire() { publicRevealOwner.retiring=true },
  readyCount() { return publicRevealReadyInstanceIds.value.size },
  centralLeaseCount() { return calls.acquired },
  projection() { return JSON.stringify(props.game.players) },
}
`,{compilerOptions:{module:ts.ModuleKind.None,target:ts.ScriptTarget.ES2022}}).outputText
const createContract=()=>new Function('bindings',contractFactory)({acquireAuthoritativeCardVisibility})
let cases=0
const failures=[]
const checkCase=(label,run)=>{try{run();cases++}catch(error){failures.push({label,message:error.message})}}
for(const zone of ['field','master','relic','extraRelics','hand','graveyard','library','center'])
for(const tapped of [false,true])for(const readiness of ['before-physical-release','after-physical-release'])
for(const response of ['same-revision','next-revision']){
  const label=`${zone}/${tapped?'rested':'active'}/${readiness}/${response}`
  checkCase(label,()=>{
    const api=createContract(),{target,eventCard}=api.configure({zone,tapped})
    api.setPhysicalOwner(true)
    const releasePhysical=acquireAuthoritativeCardVisibility(target)
    if(response==='next-revision')api.advanceRevision()
    if(readiness==='after-physical-release')releasePhysical()
    const before=authoritativeCardVisibilityLeaseCount(target),projection=api.projection()
    api.start(eventCard)
    assert.equal(api.centralLeaseCount(),0,label+': explanation never acquires a physical visibility lease')
    assert.equal(authoritativeCardVisibilityLeaseCount(target),before,label+': physical owner count is unchanged')
    api.start(eventCard)
    assert.equal(api.readyCount(),1,label+': duplicate ready is idempotent')
    assert.equal(api.centralLeaseCount(),0,label+': duplicate load cannot add a lease')
    assert.equal(api.projection(),projection,label+': recipient identities and hidden flags are unchanged')
    releasePhysical()
    api.setPhysicalOwner(false)
    assert.equal(target.style.visibility,'',label+': physical release restores the card while explanation stays')
    api.end()
    assert.equal(api.readyCount(),0,label+': central cleanup clears only image readiness')
    assert.equal(authoritativeCardVisibilityLeaseCount(target),0,label+': no stale central lease')
  })
}
for(const [label,configuration] of [
  ['ordinary reveal is also independent',{independent:false}],
  ['source that has truly left is not restored by explanation',{departed:true}],
  ['another instance is not changed',{wrongInstance:true}],
  ['hidden authoritative card stays in its own visibility state',{hidden:true,visibility:'hidden'}],
  ['hidden event does not change the body',{eventHidden:true}],
  ['hand projection stays private',{zone:'hand',hidden:true,visibility:'collapse'}],
  ['graveyard overlay is independent',{zone:'graveyard'}],
  ['non-full-card continuation owns no pixels',{kind:'entry-continuation'}],
  ['a stale DOM zone does not authorize explanation to hide it',{domZone:'graveyard'}],
])checkCase(label,()=>{
  const api=createContract(),{target,eventCard}=api.configure(configuration),original=target.style.visibility
  const projection=api.projection()
  api.start(eventCard)
  assert.equal(api.centralLeaseCount(),0,label)
  assert.equal(target.style.visibility,original,label+': underlying visibility is unchanged')
  assert.equal(api.projection(),projection,label+': no recipient data is revealed or rewritten')
  api.end()
  assert.equal(target.style.visibility,original,label+': cleanup is independent')
})
checkCase('stale, placeholder and retiring loads cannot claim readiness',()=>{
  const api=createContract(),{eventCard}=api.configure()
  api.start(eventCard,'old-owner')
  api.start(eventCard,'central-owner','placeholder')
  assert.equal(api.readyCount(),0)
  api.retire();api.start(eventCard)
  assert.equal(api.readyCount(),0)
  assert.equal(api.centralLeaseCount(),0)
})
checkCase('central retirement cannot release a real physical owner',()=>{
  const api=createContract(),{target,eventCard}=api.configure({zone:'field'})
  const releasePhysical=acquireAuthoritativeCardVisibility(target)
  api.setPhysicalOwner(true);api.start(eventCard);api.end()
  assert.equal(authoritativeCardVisibilityLeaseCount(target),1)
  assert.equal(target.style.visibility,'hidden')
  releasePhysical();assert.equal(target.style.visibility,'')
})
if(failures.length)console.error(JSON.stringify({contract:'independent central explanation',passed:cases,failures},null,2))
assert.equal(failures.length,0,'central explanation must never control physical card visibility')
console.log(`Independent central explanation contract passed: ${cases} cases`)

console.log('authoritative card visibility lease tests passed')

// Keep persistent-zone ownership in the normal shared visibility regression chain.
await import('./test-master-zone-lifecycle-20261026.mjs')
await import('./test-entry-before-hit-lifecycle-20261026.mjs')
