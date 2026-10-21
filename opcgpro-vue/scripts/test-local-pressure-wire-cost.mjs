import assert from 'node:assert/strict'
import {createLocalWireCostObserver} from './local-pressure-wire-cost.mjs'
let count = 0
const measure = (raw, type = 'gameStateDelta', stage = 'rooms-30') => {
  let now = 0
  const observer = createLocalWireCostObserver({clock: () => ++now})
  observer.observe({raw, wireType: type, wireBytes: Buffer.byteLength(raw), stage})
  const report = observer.summarize([{type, bytes: Buffer.byteLength(raw), stage}])
  assert.equal(report.reconciled, true); count++
  return report.rows[0]
}
for (const text of ['plain', '中文', 'emoji😀', 'quote" slash\\ ,]}[{: \n', '\\u4e2d']) {
  const value = JSON.stringify([{text}])
  const raw = '{"type":"gameStateDelta","changes":' + value
    + ',"eventWindow":{"events":[]},"handSplices":{"insert":[]},"privateExtra":"do-not-retain"}'
  const row = measure(raw)
  assert.equal(row.parts.changes, Buffer.byteLength(value)); count++
  assert.equal(row.parts.eventWindow, Buffer.byteLength('{"events":[]}')); count++
  assert.equal(row.parts.handSplices, Buffer.byteLength('{"insert":[]}')); count++
  assert.equal(row.failedPackets, 0); count++
  assert.equal(row.diagnosticMs, 1); count++
  assert.equal(JSON.stringify(row).includes('do-not-retain'), false); count++
}
for (const raw of [
  '{"type":"gameStateDelta","changes":[{"v":"\\u4e2d\\u6587\\u003C"}]}',
  ' { "type" : "gameStateDelta", "chan\\u0067es" : [true, false, null, -1.2e3, {"x":[1,{}]}] } \r\n',
  '{"type":"gameStateDelta","handSplices":null,"eventWindow":[],"changes":[]}',
]) {
  const row = measure(raw)
  assert.equal(row.failedPackets, 0); count++
  assert.equal(Object.values(row.parts).reduce((sum, value) => sum + value, 0), row.wireBytes); count++
}
const packed = '{"type":"gameStateCardFrameV1","payload":{"state":{"recentEvents":[{"cards":null}]}}'
  + ',"cardSnapshots":[{"text":"中\\u6587"}],"eventCardRefs":[[0]],"lastActionCardRefs":null}'
const packedRow = measure(packed, 'gameStateCardFrameV1')
assert.equal(packedRow.parts.cardSnapshots, Buffer.byteLength('[{"text":"中\\u6587"}]')); count++
assert.equal(packedRow.parts.eventCardRefs, 5); count++
assert.equal(packedRow.parts.lastActionCardRefs, 4); count++
for (const raw of [
  '{"type":"gameStateDelta","changes":[],"changes":[1]}',
  '{"type":"gameStateDelta","chan\\u0067es":[],"changes":[1]}',
  '{"type":"gameStateDelta","changes":[}',
  '{"type":"gameStateDelta","changes":["unterminated}',
  '{"type":"gameStateDelta","changes":[1]',
  '{"type":"gameStateDelta","changes":[1]} trailing',
  '{"type":"not-the-reported-type","changes":[]}',
  '{"type":"gameStateDelta","changes":"' + 'x'.repeat(2 * 1024 * 1024) + '"}',
]) {
  const row = measure(raw)
  assert.equal(row.failedPackets, 1); count++
  assert.equal(row.unattributedBytes, Buffer.byteLength(raw)); count++
  assert.equal(Object.values(row.parts).reduce((sum, value) => sum + value, 0), 0); count++
}
const observer = createLocalWireCostObserver()
const raw = '{"type":"gameStateDelta","changes":[]}'
const bytes = Buffer.byteLength(raw)
for (let index = 0; index < 1000; index++) observer.observe({raw, wireType: 'gameStateDelta',
  wireBytes: bytes, stage: 'secret-player-' + index})
const rows = Array.from({length: 1000}, () => ({type: 'gameStateDelta', bytes, stage: 'private-stage'}))
const bounded = observer.summarize(rows)
assert.equal(bounded.rows.length, 1); count++
assert.equal(bounded.rows[0].stage, 'other'); count++
assert.equal(JSON.stringify(bounded).includes('secret-player'), false); count++
assert.equal(bounded.reconciled, true); count++
assert.equal(observer.summarize([]).reconciled, false); count++
const empty = createLocalWireCostObserver()
empty.observe({raw: 'private body', wireType: 'presenceDelta', wireBytes: 12, stage: 'cleanup'})
assert.deepEqual(empty.summarize([]).rows, []); count++
assert.equal(empty.summarize([{type: 'gameStateDelta', bytes, stage: 'rooms-10'}]).reconciled, false); count++
empty.observe({raw, wireType: 'gameStateDelta', wireBytes: bytes + 1, stage: 'rooms-10'})
const mismatch = empty.summarize([{type: 'gameStateDelta', bytes: bytes + 1, stage: 'rooms-10'}])
assert.equal(mismatch.reconciled, true); count++
assert.equal(mismatch.failures, 1); count++
assert.equal(mismatch.rows[0].unattributedBytes, bytes + 1); count++
console.log(JSON.stringify({status: 'passed', cases: count, networkCalls: 0, filesWritten: 0,
  scope: 'pure raw-byte accounting only, not workload, protocol or capacity acceptance'}))

