import fs from 'node:fs'
import path from 'node:path'
import assert from 'node:assert/strict'
import {execFileSync} from 'node:child_process'
import {createPressureMeasurementCheckpoint, persistPressureMeasurementCheckpoint,
  validatePressureMeasurementCheckpoint} from './lib/l12-pressure-checkpoint.mjs'

const root = process.env.L12_CHECKPOINT_TEST_OUTPUT
assert(root && path.isAbsolute(root) && !fs.existsSync(root), 'Fresh governed output required')
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  ". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; "
  + '$null=Assert-L12GeneratedPath -Path $env:L12_CHECKPOINT_TEST_OUTPUT -Purpose "numeric pressure checkpoint tests"'],
{stdio: 'pipe', windowsHide: true})
fs.mkdirSync(root, {recursive: true})
let passed = 0
const test = (name, run) => { run(); passed++; console.log('PASS ' + name) }
const fresh = () => createPressureMeasurementCheckpoint({candidate: '8c33b7f595bc0cab599eef560c5d5e3d80c96124',
  coreSourceSha256: 'A'.repeat(64), metrics: {elapsedMs: 700000,
    stages: [10, 20, 30].map(rooms => ({rooms, outsideUsers: 10, durationMs: 60000})),
    terminalFrames: Array.from({length: 60}, (_, slot) => ({numericClientSlot: slot,
      numericRoomSlot: Math.floor(slot / 2), receiver: slot % 2, sendToCallbackMs: slot === 59 ? 3001 : 1200,
      exceededOriginal3s: Number(slot === 59), logicalJsonBytes: 190000, wireBytes: 173000})),
    actionBounds: {client: {rows: [{slot: 0, exceeded_original_3s: 1}]}},
    http: {audit: {requests: 30, errors: 0, latencyMs: {p95: 800}}},
    commands: {overall: {attempted: 100, accepted: 100}}, websocket: {gameState: {fullPayloadBytes: 1000000}},
    payloadFloor: {observedRooms: 30}, reconstructionErrors: 0, presenceReconstructionErrors: 0}})
test('late terminal observation is durable but never accepted', () => {
  const value = fresh(), output = path.join(root, 'measurement-before-account-cleanup.json')
  const receipt = persistPressureMeasurementCheckpoint(output, value)
  const actual = JSON.parse(fs.readFileSync(output, 'utf8'))
  assert.equal(receipt.finalAccepted, false); assert.equal(actual.cleanupPending, true)
  assert.equal(actual.metrics.terminalFrames[59].sendToCallbackMs, 3001)
  assert.equal(actual.metrics.terminalFrames[59].exceededOriginal3s, 1)
})
test('existing proof cannot be overwritten', () => {
  assert.throws(() => persistPressureMeasurementCheckpoint(path.join(root, 'measurement-before-account-cleanup.json'), fresh()), /EEXIST/)
})
test('missing observations stay missing', () => {
  const value = fresh(); value.metrics.terminalFrames = []
  assert.equal(validatePressureMeasurementCheckpoint(value).metrics.terminalFrames.length, 0)
})
for (const [name, change] of [
  ['cannot claim final success', x => {x.finalAccepted = true}],
  ['cannot pretend cleanup completed', x => {x.cleanupPending = false}],
  ['cannot relax original SLO', x => {x.originalSloMs = 4000}],
  ['cannot extend wall limit', x => {x.maximumWallMs = 1000000}],
  ['cannot extend pressure limit', x => {x.pressureBudgetMs = 800000}],
  ['duplicate terminal slot rejected', x => {x.metrics.terminalFrames[1] = x.metrics.terminalFrames[0]}],
  ['wrong room binding rejected', x => {x.metrics.terminalFrames[0].numericRoomSlot = 1}],
  ['late observation cannot be labelled timely', x => {x.metrics.terminalFrames[59].exceededOriginal3s = 0}],
  ['non-finite timing rejected', x => {x.metrics.terminalFrames[0].sendToCallbackMs = NaN}],
  ['secret field rejected', x => {x.metrics.http.token = 'do-not-export'}],
  ['raw strings rejected on readback', x => {x.metrics.http.audit.body = 'private response'}],
  ['unknown top-level field rejected', x => {x.password = 'do-not-export'}],
  ['wrong candidate rejected', x => {x.candidate = 'dev'}],
]) test(name, () => {const value = fresh(); change(value); assert.throws(() => validatePressureMeasurementCheckpoint(value))})
test('strings are not serialized into numeric aggregates', () => {
  const value = fresh(); value.metrics.http.audit.reason = 'server supplied text'
  assert.throws(() => validatePressureMeasurementCheckpoint(value))
})
test('wrong output name cannot be used', () => {
  assert.throws(() => persistPressureMeasurementCheckpoint(path.join(root, 'other.json'), fresh()), /output_scope/)
})
console.log(JSON.stringify({status: 'PASS', passed, networkCalls: 0, applicationWrites: 0,
  originalSloMs: 3000, measuredPerformance: false}))
