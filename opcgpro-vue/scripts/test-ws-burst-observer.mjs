import assert from 'node:assert/strict'
import fs from 'node:fs'
import crypto from 'node:crypto'
import path from 'node:path'
import { WsBurstObserver, finalizeClientObservationStatus } from './ws-burst-observer.mjs'

// Read-only original bytes travel with the test; optional local evidence remains
// external, but a normal build must not require this workstation's archive.
const frozen = fs.readFileSync(new URL('./fixtures/ws-burst-r7-original.txt', import.meta.url))
assert.equal(crypto.createHash('sha256').update(frozen).digest('hex').toUpperCase(), '9CF6B5E3E300ED2082A6630BE1638817439995073334B3B8BAC97EAADC1ABAA0')
const owned = fs.readFileSync(new URL('./ws-burst-observer.mjs', import.meta.url), 'utf8')
const body = owned.slice(owned.indexOf("import crypto from 'node:crypto'"))
const restored = body.replace("'gameStateCardFrameV1','gameStatePropertyFrameV1','gameStateDelta'", "'gameStateCardFrameV1','gameStateDelta'")
  .replace("['gameState','gameStateCardFrameV1','gameStatePropertyFrameV1'].includes(type)", "['gameState','gameStateCardFrameV1'].includes(type)")
assert.equal(restored, frozen.toString('utf8').replaceAll('\r\n', '\n'))
const attributes = fs.readFileSync(process.env.L12_QA_SOURCE_REPO ? path.join(process.env.L12_QA_SOURCE_REPO, '.gitattributes') : new URL('../../.gitattributes', import.meta.url), 'utf8')
assert.ok(attributes.split(/\r?\n/).includes('/opcgpro-vue/scripts/ws-burst-observer.mjs text eol=lf'), 'Observer checkout must preserve LF for the exact frozen reverse comparison')
let cases = 1
function run({ frameCap = 4096, count = 60, verified = true, lastAt = 3000 } = {}) {
  const observer = new WsBurstObserver({ frameCap, now: () => 0 }); observer.begin(0)
  for (let room = 0; room < 30; room++) observer.observeSend(room, room * 2, 0)
  for (let slot = 0; slot < count; slot++) {
    const at = slot === 58 ? lastAt : 3000
    const sequence = observer.observeFrame(slot, 'gameStatePropertyFrameV1', 12345 + slot, verified, at)
    if (slot % 2 === 0) observer.observeResponse(slot / 2, slot, sequence, at, verified)
  }
  return observer.summary()
}
const complete = run()
assert.equal(finalizeClientObservationStatus('completed', complete), 'completed')
assert.equal(complete.terminalCoverageComplete, 1); assert.equal(complete.firstTerminalByConnection.length, 60)
assert.equal(complete.cleanupTotalsByType.gameStatePropertyFrameV1.frames, 60)
assert.equal(complete.cleanupTotalsByType.gameStatePropertyFrameV1.bytes, Array.from({ length: 60 }, (_, index) => 12345 + index).reduce((a, b) => a + b))
assert(complete.perSurrender3s.every(row => row.late3s === 0)); cases++
for (const options of [{ count: 59 }, { frameCap: 1 }, { verified: false }]) {
  assert.equal(finalizeClientObservationStatus('completed', run(options)), 'completed-with-errors'); cases++
}
const late = run({ lastAt: 3000.001 })
assert.equal(late.perSurrender3s.find(row => row.numericRoomSlot === 29).late3s, 1)
assert.equal(late.first3sByType.gameStatePropertyFrameV1.frames, 59)
assert.equal(late.original3sMs, 3000); assert.equal(late.retries, 0); assert.equal(late.failuresConvertedToPass, 0); cases++
console.log(JSON.stringify({ status: 'passed', cases, frozenReverseExact: true, changedTypeOnly: true, networkCalls: 0 }))
