import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/presentationSequenceCoordinator.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, { compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 } }).outputText
const { createPresentationSequenceCoordinator } = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)
const visibilitySource = readFileSync(new URL('../src/l12/game/authoritativeCardVisibility.ts', import.meta.url), 'utf8')
const visibilityOutput = ts.transpileModule(visibilitySource,
  { compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 } }).outputText
const { acquireAuthoritativeCardVisibility, authoritativeCardVisibilityLeaseCount } = await import(
  `data:text/javascript;base64,${Buffer.from(visibilityOutput).toString('base64')}`)
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))

// Preserve the original ordering, pause and ownership-ledger contract.
const busy=[]
const coordinator=createPresentationSequenceCoordinator(value=>busy.push(value))
const later=coordinator.reserve(20,20)
const earlier=coordinator.reserve(10,10)
const order=[]
const laterGrant=later.waitUntilGranted().then(release=>{order.push('later');return release})
const earlierGrant=earlier.waitUntilGranted().then(release=>{order.push('earlier');return release})
await delay(10)
assert.deepEqual(order,['earlier'],'lower authoritative sequence must win even when registered after a later sequence')
;(await earlierGrant)()
await delay(10)
assert.deepEqual(order,['earlier','later'],'later presentation must wait for the earlier release')
;(await laterGrant)()

const high=coordinator.reserve(40,20)
const pausedLow=coordinator.reserve(30,10)
pausedLow.setPaused(true)
const modalOrder=[]
const highGrant=high.waitUntilGranted().then(release=>{modalOrder.push('high');return release})
const lowGrant=pausedLow.waitUntilGranted().then(release=>{modalOrder.push('low');return release})
await delay(10)
assert.deepEqual(modalOrder,[],'a modal-paused lower sequence must reserve the lane')
pausedLow.setPaused(false)
await delay(10)
assert.deepEqual(modalOrder,['low'],'resume must continue at the authoritative lower sequence')
;(await lowGrant)()
await delay(10)
assert.deepEqual(modalOrder,['low','high'],'higher sequence must resume only after lower presentation completes')
;(await highGrant)()
assert.deepEqual(busy,[true,false,true,false],'busy state must cover pending preparation, active presentation and modal pause without flicker')
coordinator.registerPresentationFact(19)
coordinator.registerPresentationFact(19)
coordinator.registerPresentationFact(0)
assert.equal(coordinator.ownsPresentationFact(19),true,'a visibly started authority fact is owned exactly once')
assert.equal(coordinator.ownsPresentationFact(0),false,'invalid event identities are never admitted')
coordinator.clearPresentationFacts()
assert.equal(coordinator.ownsPresentationFact(19),false,'match reset clears presentation fact ownership')
coordinator.registerPresentationFact(21)
coordinator.reset()
assert.equal(coordinator.ownsPresentationFact(21),false,'full coordinator reset clears presentation facts')

const ownerLog=[]
let authorityLeaseCount=0
const owner=value=>{
  let acquired=false
  return {
    show:()=>{
      if (!acquired) { acquired=true; authorityLeaseCount++ }
      ownerLog.push(`${value}:show:${authorityLeaseCount}`)
    },
    hide:()=>{
      if (acquired) { acquired=false; authorityLeaseCount-- }
      ownerLog.push(`${value}:hide:${authorityLeaseCount}`)
    },
    dispose:()=>{
      if (acquired) { acquired=false; authorityLeaseCount-- }
      ownerLog.push(`${value}:dispose:${authorityLeaseCount}`)
    },
  }
}

// Preparation is tied to reservation order, independent of watcher order.
const ownerCoordinator=createPresentationSequenceCoordinator()
const movementReservation=ownerCoordinator.reserve(50,20,{visualKey:'same-card'})
const stateReservation=ownerCoordinator.reserve(50,30,{visualKey:'same-card'})
const movementOwner=owner('movement')
const stateOwner=owner('state')
stateReservation.prepareVisualOwner(stateOwner)
movementReservation.prepareVisualOwner(movementOwner)
assert.deepEqual(ownerLog.slice(-2),['state:hide:0','movement:show:1'],
  'a later prepared holder cannot preempt the earlier reservation')
const movementReleasePromise=movementReservation.waitUntilGranted()
const stateReleasePromise=stateReservation.waitUntilGranted()
const movementRelease=await movementReleasePromise
stateReservation.prepareVisualOwner(stateOwner)
assert.equal(ownerLog.at(-1),'movement:show:1','preparing a later holder cannot truncate the current motion')
movementRelease(true)
assert.deepEqual(ownerLog.slice(-2),['state:show:2','movement:dispose:1'],
  'handoff acquires the successor lease before disposing the current owner')
assert.equal(authorityLeaseCount,1,'handoff never exposes the authority between ordered visual owners')
const stateRelease=await stateReleasePromise
stateRelease(true)
assert.equal(authorityLeaseCount,0,'the final owner releases every authority lease')

// Paused inline choices keep the previous pixels in a static, pointer-
// transparent holder. The actual GameBoard click test separately proves that
// the authoritative slot remains the native hit target underneath.
const promptCoordinator=createPresentationSequenceCoordinator()
const promptReservation=promptCoordinator.reserve(60,20,{visualKey:'clickable-card'})
promptReservation.setPaused(true)
const promptOwner=owner('prompt')
promptReservation.prepareVisualOwner(promptOwner)
const promptGrant=promptReservation.waitUntilGranted()
await delay(10)
assert.equal(authorityLeaseCount,1,'a paused prepared head keeps one static visual owner')
assert.equal(ownerLog.at(-1),'prompt:show:1','a paused head keeps the previous pixels instead of exposing the final state')
promptReservation.setPaused(false)
const promptRelease=await promptGrant
assert.equal(authorityLeaseCount,1,'unpause retains the same owner without a final-to-old visibility reversal')
promptRelease(true)
assert.equal(authorityLeaseCount,0,'unpaused presentation releases its authority lease')

// If the next reservation has not prepared its holder when the current motion
// ends, retain the current final pixels. The late successor takes the lease
// before that predecessor is disposed, so authority never flashes between.
const lateCoordinator=createPresentationSequenceCoordinator()
const completed=lateCoordinator.reserve(65,20,{visualKey:'late-card'})
const lateSuccessor=lateCoordinator.reserve(65,30,{visualKey:'late-card'})
const completedOwner=owner('completed')
completed.prepareVisualOwner(completedOwner)
const completedRelease=await completed.waitUntilGranted()
const lateGrant=lateSuccessor.waitUntilGranted()
completedRelease(true)
assert.equal(authorityLeaseCount,1,'a completed owner stays visible while its exact successor is still unprepared')
const lateOwner=owner('late')
lateSuccessor.prepareVisualOwner(lateOwner)
assert.deepEqual(ownerLog.slice(-2),['late:show:2','completed:dispose:1'],
  'a late-prepared successor acquires before the retained predecessor disposes')
const lateRelease=await lateGrant
lateRelease(true)
assert.equal(authorityLeaseCount,0,'late successor completion returns the retained lease to zero')

// Exercise the real visibility lease, including the Zone lifecycle boundary:
// releasing A may retain its holder while B is reserved but not prepared. A's
// closure must therefore survive until B acquires and the coordinator disposes
// A; clearing it merely because A's animation finished leaks one hidden lease.
const retainedCoordinator=createPresentationSequenceCoordinator()
const retainedTarget={style:{visibility:''}}
const retainedOwner=()=>{
  let releaseVisibility
  return {
    show:()=>{ releaseVisibility ??= acquireAuthoritativeCardVisibility(retainedTarget) },
    hide:()=>{ releaseVisibility?.(); releaseVisibility=undefined },
    dispose:()=>{ releaseVisibility?.(); releaseVisibility=undefined },
  }
}
const retainedA=retainedCoordinator.reserve(66,20,{visualKey:'retained-card'})
const retainedB=retainedCoordinator.reserve(66,30,{visualKey:'retained-card'})
retainedA.prepareVisualOwner(retainedOwner())
const retainedARelease=await retainedA.waitUntilGranted()
const retainedBGrant=retainedB.waitUntilGranted()
retainedARelease(true)
assert.equal(authoritativeCardVisibilityLeaseCount(retainedTarget),1,
  'released A retains exactly one real visibility lease while B is unprepared')
assert.equal(retainedTarget.style.visibility,'hidden',
  'authority remains covered while the reserved successor is unprepared')
retainedB.prepareVisualOwner(retainedOwner())
assert.equal(authoritativeCardVisibilityLeaseCount(retainedTarget),1,
  'B acquires before retained A disposes without leaking a second lease')
assert.equal(retainedTarget.style.visibility,'hidden',
  'atomic A-to-B handoff never exposes the authority')
const retainedBRelease=await retainedBGrant
retainedBRelease(true)
assert.equal(authoritativeCardVisibilityLeaseCount(retainedTarget),0,
  'B terminal release returns the real visibility lease to zero')
assert.equal(retainedTarget.style.visibility,'',
  'B terminal release restores the authority visibility')

// Ordering watermarks are not action identities: both later revisions must run.
const watermarkCoordinator=createPresentationSequenceCoordinator()
const watermarkStarts=[]
for (const revision of [10,11]) {
  const reservation=watermarkCoordinator.reserve(70,30,{visualKey:'watermark-card'})
  reservation.prepareVisualOwner({
    show:()=>watermarkStarts.push(`r${revision}:show`),
    hide:()=>watermarkStarts.push(`r${revision}:hide`),
    dispose:()=>watermarkStarts.push(`r${revision}:dispose`),
  })
  const release=await reservation.waitUntilGranted()
  watermarkStarts.push(`r${revision}:motion`)
  release(true)
}
assert.deepEqual(watermarkStarts.filter(value=>value.endsWith(':motion')),['r10:motion','r11:motion'],
  'two revisions sharing one event watermark remain two legal visual actions')

// Cancelling a prepared head transfers to the next reservation and reset owns
// final disposal; neither path leaves an authority lease behind.
const cancelCoordinator=createPresentationSequenceCoordinator()
const cancelled=cancelCoordinator.reserve(80,20,{visualKey:'cancel-card'})
const survivor=cancelCoordinator.reserve(81,20,{visualKey:'cancel-card'})
const cancelledOwner=owner('cancelled')
const survivorOwner=owner('survivor')
cancelled.prepareVisualOwner(cancelledOwner)
survivor.prepareVisualOwner(survivorOwner)
cancelled.cancel()
assert.deepEqual(ownerLog.slice(-2),['survivor:show:2','cancelled:dispose:1'],
  'cancelling the prepared head hands its lease to the next reservation first')
cancelCoordinator.reset()
assert.equal(authorityLeaseCount,0,'reset disposes the remaining prepared owner and returns leases to zero')

// A successor can legitimately finish without pixels (failed decode/missing
// target). It must cease being a successor before the retained predecessor is
// disposed; otherwise the released active item retains the old holder forever.
const discardedCoordinator=createPresentationSequenceCoordinator()
const discardedTarget={style:{visibility:''}}
let discardedRelease
const retainedBeforeDiscard=discardedCoordinator.reserve(90,20,{visualKey:'discard-card'})
const discardedSuccessor=discardedCoordinator.reserve(91,30,{visualKey:'discard-card'})
retainedBeforeDiscard.prepareVisualOwner({
  show:()=>{ discardedRelease ??= acquireAuthoritativeCardVisibility(discardedTarget) },
  hide:()=>{ discardedRelease?.(); discardedRelease=undefined },
  dispose:()=>{ discardedRelease?.(); discardedRelease=undefined },
})
const retainedBeforeDiscardGrant=retainedBeforeDiscard.waitUntilGranted()
const discardedSuccessorGrant=discardedSuccessor.waitUntilGranted()
;(await retainedBeforeDiscardGrant)(true)
assert.equal(authoritativeCardVisibilityLeaseCount(discardedTarget),1,
  'retained predecessor covers authority while the undecoded successor waits')
;(await discardedSuccessorGrant)(false)
assert.equal(authoritativeCardVisibilityLeaseCount(discardedTarget),0,
  'a released successor without a visual owner cannot retain predecessor pixels')
assert.equal(discardedTarget.style.visibility,'',
  'a discarded successor restores final authority visibility')

console.log('Presentation sequence coordinator passed: 36/36 assertions')
