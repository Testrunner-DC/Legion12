import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import assert from 'node:assert/strict'
import {pathToFileURL, fileURLToPath} from 'node:url'
import {execFileSync} from 'node:child_process'
import {createPressureMeasurementCheckpoint, persistPressureMeasurementCheckpoint} from './lib/l12-pressure-checkpoint.mjs'

const root = process.env.L12_CHECKPOINT_TEST_OUTPUT
assert(root && path.isAbsolute(root) && !fs.existsSync(root))
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  ". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; "
  + '$null=Assert-L12GeneratedPath -Path $env:L12_CHECKPOINT_TEST_OUTPUT -Purpose "actual pressure function cleanup-boundary tests"'],
{stdio: 'pipe', windowsHide: true})
fs.mkdirSync(root, {recursive: true})
const core = 'D:/GPT/Legion12/artifacts/performance-causal-isolation-20261016/cohort30-full-workload-r7/pressure-load-r7-approved-preparation-durable-r4.mjs'
const original = core.replace('preparation-durable-r4', 'preparation-r3')
const source = fs.readFileSync(core, 'utf8').replaceAll('\r\n', '\n')
const oldSource = fs.readFileSync(original, 'utf8').replaceAll('\r\n', '\n')
const imported = "import {createPressureMeasurementCheckpoint, persistPressureMeasurementCheckpoint} from 'file:///D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/scripts/lib/l12-pressure-checkpoint.mjs'\n"
assert.equal(source.split(imported).length, 2)
const additionStart = source.indexOf('    // Persist completed numeric observation before potentially slow account\n')
const additionEnd = source.indexOf('    if(identities.admin) {', additionStart)
assert(additionStart > 0 && additionEnd > additionStart)
assert.equal((source.slice(0, additionStart) + source.slice(additionEnd)).replace(imported, ''), oldSource,
  'Exact reverse must restore the frozen workload; no budgets, commands, SLO or cleanup edits')
const start = source.indexOf('async function runPressure(configuration) {')
const end = source.indexOf('\nfunction parseInteger(', start)
assert(start > 0 && end > start)
const body = source.slice(start, end).replace('fileURLToPath(import.meta.url)', 'fileURLToPath(coreImportMetaUrl)')
assert(!body.includes('import.meta'))
const outputs = () => ({attempted: 100, accepted: 100,
  outcomes: {accepted: 100, rejected: 0, 'timed-out': 0, 'transport-error': 0, 'advanced-without-ack': 0}})
class FakeMetrics {
  constructor() {
    Object.assign(this, {eventLoopLagMs: [], httpRows: [], cleanupTimings: [], clientFrameTimings: [],
      reconstructionErrors: 0, presenceReconstructionErrors: 0, presenceRecoveries: 0,
      presenceDeltaApplied: 0, presenceNegotiatedClients: 60,
      burstObserver: {begin() {}, summary: () => ({complete: true})}})
  }
  httpSummary() { return {audit: {skipped: false, requests: 30, errors: 0, latencyMs: {p95: 800}}} }
  commandSummary() { return {overall: outputs(), byType: {playCard: {accepted: 1}, attack: {accepted: 1}}} }
  wsSummary() { return {gameState: {fullMessages: 60, fullPayloadBytes: 12000000}} }
}
const frames = slot => ({numericClientSlot: slot, numericRoomSlot: Math.floor(slot / 2), receiver: slot % 2,
  type: 'gameState', terminal: true, delta: false, sendToCallbackMs: slot === 59 ? 3001 : 1200,
  exceededOriginal3s: Number(slot === 59), wireBytes: 173000, logicalJsonBytes: 190000})
let passed = 0
async function caseRun(name, mode) {
  const selected = path.join(root, name); fs.mkdirSync(selected)
  const output = path.join(selected, 'measurement-before-account-cleanup.json')
  if (mode === 'write-refused') fs.writeFileSync(output, 'owned-existing-proof\n', {flag: 'wx'})
  let allowCleanup, enteredCleanup
  const cleanupGate = new Promise(resolve => {allowCleanup = resolve})
  const cleanupEntered = new Promise(resolve => {enteredCleanup = resolve})
  const dependencies = {
    endpoint() {}, StopController: class {constructor() {this.controller = new AbortController(); this.reason = null} request(reason) {this.reason = reason; this.controller.abort()}},
    Metrics: FakeMetrics, validateOriginalWorkloadPlan() {}, LoadV2Markers: class {recordLoopDrift() {} report() {return {client_complete: 0, client: {rows: [{slot: 0, exceeded_original_3s: 1}]}}}},
    MAX_WALL_MS: 900000, PRESSURE_DEADLINE_MS: 720000, STOP_POLL_MS: 500,
    ACTION_INTERVAL_MS: 2000, MATCH_PLAYERS: 60, OUTSIDE_USERS: 10, REQUIRED_PLAYERS: 69, MAX_IDENTITIES: 70,
    STAGE_ROOMS: [10, 20, 30], PUBLIC_BASE: 'https://legion-12.com/testrun/',
    SafeError: Error, safeFailure: () => 'fixture-business-failure',
    checkCommit: async () => '8c33b7f595bc0cab599eef560c5d5e3d80c96124',
    authenticateCredential: async () => ({accountId: 'private-admin', token: 'private-not-exported'}),
    registerSyntheticPlayers: async () => Array.from({length: 69}, (_, i) => ({accountId: 'private-' + i})),
    prepareWorkloads: async () => ({skips: {}, workloads: []}),
    createRoomPair: async (ctx, a, b, metrics, rooms) => {
      const roomSlot = rooms.length
      const peer = slot => ({closed: false, terminalFrame: null, cardFrameDecodeFailures: 0,
        packedFrames: 1, plainFrames: 0, plainRecoveryRequests: 0, numericClientSlot: slot})
      rooms.push({host: peer(roomSlot * 2), guest: peer(roomSlot * 2 + 1), started: true})
    },
    preparePayloadFloor: async () => ({status: 'ready'}),
    runStage: async rooms => ({name: 'rooms-' + rooms, rooms, durationMs: 60000,
      minimumActiveRooms: rooms, maximumActiveRooms: rooms, outsideUsers: 10}),
    inspectPayloadFloor: () => ({status: 'ready', observedRooms: 30}),
    cleanupRoom: async (room, ctx, cleanup) => {
      room.host.terminalFrame = frames(room.host.numericClientSlot)
      room.guest.terminalFrame = frames(room.guest.numericClientSlot)
      cleanup.compliantSurrenders++; cleanup.roomsLeft++
    },
    cleanupDualLedgerAccounts: async () => {
      enteredCleanup(); await cleanupGate
      if (mode === 'cleanup-throws') throw new Error('local injected cleanup failure')
      return {playerSessionsRevoked: 69, adminSessionRevoked: 1, syntheticAccountsDeleted: 69, failures: 0}
    },
    createPressureMeasurementCheckpoint, persistPressureMeasurementCheckpoint, fs, fileURLToPath,
    sha256File: file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase(),
    coreImportMetaUrl: pathToFileURL(core).href, deployedNetSha: 'B'.repeat(64), round: n => Math.round(n * 100) / 100,
    distribution: rows => ({count: rows.length}), finalizeClientObservationStatus: status => status,
  }
  const run = new Function(...Object.keys(dependencies), body + '\nreturn runPressure;')(...Object.values(dependencies))
  const pending = run({local: true, baseUrl: 'http://127.0.0.1/', expectedCommit: '8c33b7f595bc0cab599eef560c5d5e3d80c96124',
    diagnosticNonce: 'f'.repeat(32), provisionPlayers: true, credentials: {admin: {}},
    measurementCheckpointPath: output, stageSeconds: 60, requestTimeoutMs: 10000,
    httpIntervalMs: 2000, wsIntervalMs: 2000, progressFn() {}})
  let timer
  try { await Promise.race([cleanupEntered, new Promise((_, reject) => {timer = setTimeout(() => reject(new Error('cleanup boundary not reached')), 2000)})]) }
  finally {clearTimeout(timer)}
  if (mode === 'write-refused') assert.equal(fs.readFileSync(output, 'utf8'), 'owned-existing-proof\n')
  else {
    const capture = JSON.parse(fs.readFileSync(output, 'utf8'))
    assert.equal(capture.finalAccepted, false); assert.equal(capture.cleanupPending, true)
    assert.equal(capture.metrics.terminalFrames.length, 60)
    assert.equal(capture.metrics.terminalFrames[59].sendToCallbackMs, 3001)
    assert(!fs.readFileSync(output, 'utf8').includes('private-'))
  }
  allowCleanup()
  if (mode === 'cleanup-throws') await assert.rejects(pending, /local injected cleanup failure/)
  else {
    const report = await pending
    assert.equal(report.status, mode === 'write-refused' ? 'failed' : 'inconclusive')
    if (mode === 'write-refused') assert.equal(report.failure, 'measurement-checkpoint-unavailable')
  }
  passed++; console.log('PASS actual runPressure boundary: ' + name)
}
await caseRun('cleanup-stalled', 'normal')
await caseRun('cleanup-fails', 'cleanup-throws')
await caseRun('existing-proof-kept', 'write-refused')
console.log(JSON.stringify({status: 'PASS', passed, frozenWorkloadExactlyRestored: true,
  networkCalls: 0, measuredPerformance: false, originalSloMs: 3000, remoteExecutionReady: false}))
