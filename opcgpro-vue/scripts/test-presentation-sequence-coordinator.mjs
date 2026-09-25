import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/game/presentationSequenceCoordinator.ts', import.meta.url), 'utf8')
const output = ts.transpileModule(source, { compilerOptions:{ module:ts.ModuleKind.ESNext, target:ts.ScriptTarget.ES2022 } }).outputText
const { createPresentationSequenceCoordinator } = await import(`data:text/javascript;base64,${Buffer.from(output).toString('base64')}`)
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))

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
console.log('Presentation sequence coordinator passed: 7/7 assertions')
