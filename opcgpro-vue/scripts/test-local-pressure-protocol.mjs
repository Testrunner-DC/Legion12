import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {pathToFileURL} from 'node:url'
import {assertLocalPressureTarget, adaptFrozenPressureCore, createLocalQualificationProtocol, cleanupLocalQualificationAccounts, digest} from './local-pressure-protocol.mjs'
import {createLocalWireCostObserver} from './local-pressure-wire-cost.mjs'
import './test-local-pressure-wire-cost.mjs'
import {OWNED_FROZEN_CORE} from './owned-workload-sources.mjs'

const repo = path.resolve(import.meta.dirname, '../..')
let cases = 0
assert.equal(assertLocalPressureTarget('http://127.0.0.1:34567/'), 'http://127.0.0.1:34567/'); cases++
for (const url of ['https://127.0.0.1:34567/', 'http://localhost:34567/', 'http://154.201.80.91:34567/',
  'http://user:pass@127.0.0.1:34567/', 'http://127.0.0.1:34567/x', 'http://127.0.0.1:34567/?x=1',
  'http://127.0.0.1:34567/#x', 'http://127.0.0.1/']) {
  assert.throws(() => assertLocalPressureTarget(url)); cases++
}
const frozenPath = OWNED_FROZEN_CORE
const bytes = fs.readFileSync(frozenPath)
assert.equal(digest(bytes), 'F737052DCBFDA4A29FF53BA5DFAD76E771774C1C89C993275FFAFEAA023684FD'); cases++
const source = bytes.toString('utf8')
const adapted = adaptFrozenPressureCore(source, pathToFileURL(path.join(import.meta.dirname, 'local-pressure-protocol.mjs')).href)
assert.ok(adapted.includes('local_qualification_cannot_run_remote')); cases++
assert.throws(() => adaptFrozenPressureCore(source.replace('async function runPressure(configuration) {', 'async function different(configuration) {'), 'file:///unused'))
cases++
assert.equal(digest(fs.readFileSync(frozenPath)), digest(bytes)); cases++
const netSourcePath = path.join(repo, 'opcgpro-vue/src/l12/net.ts')
const cardFrameSourcePath = path.join(repo, 'opcgpro-vue/src/l12/gameStateCardFrame.ts')
const handArraySourcePath = path.join(repo, 'opcgpro-vue/src/l12/handArrayDelta.ts')
const binding = {kind: 'local-real-http-qualification-v1', baseUrl: 'http://127.0.0.1:34567/',
  netSourcePath, cardFrameSourcePath, handArraySourcePath, deployedNetSha256: digest(fs.readFileSync(netSourcePath)),
  cardFrameSha256: digest(fs.readFileSync(cardFrameSourcePath)), handArraySha256: digest(fs.readFileSync(handArraySourcePath))}
const protocol = await createLocalQualificationProtocol(binding)
assert.equal(protocol.wireCost, false); cases++
assert.equal((await createLocalQualificationProtocol(binding, {wireCost: true})).wireCost, true); cases++
await assert.rejects(() => createLocalQualificationProtocol(binding, {wireCost: 'true'})); cases++
assert.equal(typeof protocol.reduce, 'function'); cases++
assert.equal(typeof protocol.codec.restoreGameStateCardFrame, 'function'); cases++
assert.equal(protocol.reduce(null, {type: 'gameStateDelta'}, false), null); cases++
for (const bad of [{...binding, deployedNetSha256: '0'.repeat(64)}, {...binding, cardFrameSha256: '0'.repeat(64)},
  {...binding, baseUrl: 'https://legion-12.com/'}, {...binding, kind: 'remote-release'}]) {
  await assert.rejects(() => createLocalQualificationProtocol(bad)); cases++
}
const enabled = await createLocalQualificationProtocol(binding, {handArrayDeltaV1: true})
const card = id => ({instanceId: id, cardId: 'synthetic', unknown: {text: '完整值\\"', nested: [1, true]}})
const base = {type: 'gameState', state: {matchId: 'synthetic', revision: 1, stateHash: 'h1', you: 0,
  players: [{playerIndex: 0, hand: [card('a')]}, {playerIndex: 1, hand: [card('b')]}]}}
const delta = {type: 'gameStateDelta', matchId: 'synthetic', baseRevision: 1, revision: 2,
  changes: [{path: ['state', 'revision'], value: 2}, {path: ['state', 'stateHash'], value: 'h2'}],
  handSplices: {baseStateHash: 'h1', resultStateHash: 'h2', edits: [
    {playerIndex: 0, prefix: 1, remove: 0, insert: [card('c')], baseIds: ['a'], resultIds: ['a', 'c']},
    {playerIndex: 1, prefix: 0, remove: 1, insert: [], baseIds: ['b'], resultIds: []}]}}
const expected = structuredClone(base)
expected.state.revision = 2; expected.state.stateHash = 'h2'
expected.state.players[0].hand.push(card('c')); expected.state.players[1].hand = []
const before = JSON.stringify(base)
assert.deepEqual(enabled.reduce(base, delta, true, true), expected); cases++
assert.equal(enabled.reduce(base, delta, true, false), null); cases++
assert.equal(protocol.reduce(base, delta, true, true), null); cases++
assert.equal(enabled.reduce(expected, delta, true, true), null); cases++
for (const mutate of [d => {d.handSplices.baseStateHash = 'bad'}, d => {d.handSplices.edits[1].resultIds = ['bad']},
  d => {d.handSplices.edits[1].playerIndex = 0}, d => {d.handSplices.edits[0].prefix = -1},
  d => {d.changes.push({path: ['state', 'players', '0', 'hand'], value: []})},
  d => {d.changes.push({path: ['state', '__proto__', 'localProbePollution'], value: true})},
  d => {d.changes.push({path: ['state', 'you'], value: 1})}]) {
  const bad = structuredClone(delta); mutate(bad)
  assert.equal(enabled.reduce(base, bad, true, true), null); cases++
}
const legacy = structuredClone(delta); delete legacy.handSplices
legacy.changes.push({path: ['state', 'players', 0, 'hand'], value: expected.state.players[0].hand},
  {path: ['state', 'players', 1, 'hand'], value: []})
assert.deepEqual(protocol.reduce(base, legacy, true), expected); cases++
assert.deepEqual(enabled.reduce(base, legacy, true, false), expected); cases++
assert.equal(JSON.stringify(base), before); assert.equal({}.localProbePollution, undefined); cases++
for (const bad of [{...binding, handArraySha256: '0'.repeat(64)}, {...binding, handArraySourcePath: undefined}]) {
  await assert.rejects(() => createLocalQualificationProtocol(bad, {handArrayDeltaV1: true})); cases++
}
for (const anchor of ['gameStateCardFrameV1: true },',
  'const rebuilt = reduceEnvelope(this.envelope, message, this.negotiatedEventWindowDelta)',
  "if (type === 'session') {"]) {
  assert.throws(() => adaptFrozenPressureCore(source.replace(anchor, 'changed anchor'), 'file:///unused')); cases++
  assert.throws(() => adaptFrozenPressureCore(source + '\n// ' + anchor, 'file:///unused')); cases++
}
const privateCopy = enabled.reduce(base, delta, true, true)
privateCopy.state.players[0].hand[0].unknown.nested.push('consumer-only')
assert.equal(JSON.stringify(base), before)
assert.deepEqual(delta.handSplices.edits[0].insert, [card('c')]); cases++
const oldBinding = {...binding}; delete oldBinding.handArraySourcePath; delete oldBinding.handArraySha256
const oldProtocol = await createLocalQualificationProtocol(oldBinding)
assert.deepEqual(oldProtocol.reduce(base, legacy, true), expected); cases++
await assert.rejects(() => createLocalQualificationProtocol(oldBinding, {handArrayDeltaV1: true})); cases++
await assert.rejects(() => createLocalQualificationProtocol(binding, {handArrayDeltaV1: 'true'})); cases++

// Execute the generated workload's actual client, with memory-only transport events.
// These are pure hello/session/reconstruction contracts, not real socket or pressure evidence.
const classStart = adapted.indexOf('class WsClient {')
const classEnd = adapted.indexOf('\nasync function checkCommit(', classStart)
assert.ok(classStart >= 0 && classEnd > classStart)
const createClient = ({requested, serverHand = true, localBase = binding.baseUrl, wireCost = false}) => {
  const sent = []
  class MemoryWebSocket extends EventTarget {
    static OPEN = 1
    static CLOSING = 2
    readyState = 1
    constructor() {super(); queueMicrotask(() => this.dispatchEvent(new Event('open')))}
    emit(message) {this.dispatchEvent(Object.assign(new Event('message'), {data: JSON.stringify(message)}))}
    send(raw) {
      const message = JSON.parse(raw); sent.push(message)
      if (message.type === 'hello') queueMicrotask(() => {
        this.emit({type: 'session', capabilities: {...message.capabilities, handArrayDeltaV1: serverHand}})
        this.emit({type: 'recoveryComplete'})
      })
    }
    close() {this.readyState = 3; this.dispatchEvent(new Event('close'))}
  }
  const Client = new Function('productionCodec', 'createPresenceAdapter', 'requireContract', 'SafeError',
    'localProtocolBase', 'localHandArrayDelta', 'reduceEnvelope', 'WebSocket',
    adapted.slice(classStart, classEnd) + ';return WsClient;')(
    protocol.codec, () => ({handle: () => null, reset() {}}), (condition, code) => assert.ok(condition, code),
    Error, localBase, requested, requested ? enabled.reduce : protocol.reduce, MemoryWebSocket)
  const metrics = {currentStage: 'pure', recordWs() {}, reconstructionErrors: 0, presenceNegotiatedClients: 0,
    localHandArrayDelta: {enabled: requested, negotiatedSessions: 0, packets: 0, wireBytes: 0, applied: 0, rejected: 0}}
  const consumerOrder = []
  if (wireCost) {
    const counter = createLocalWireCostObserver()
    metrics.localWireCost = {observe: row => {consumerOrder.push('diagnostic'); counter.observe(row)},
      summarize: rows => counter.summarize(rows)}
  }
  const client = new Client('synthetic-only', metrics, 1000, new AbortController().signal,
    {observeParsed: () => wireCost ? {} : null, afterDispatch() {consumerOrder.push('dispatch')}})
  return {client, metrics, sent, consumerOrder}
}
let clientCases = 0
const live = createClient({requested: true})
await live.client.connect('ws://127.0.0.1:34567/ws')
assert.equal(live.sent[0].capabilities.handArrayDeltaV1, true)
assert.equal(live.client.negotiatedHandArrayDelta, true)
assert.equal(live.metrics.localHandArrayDelta.negotiatedSessions, 1); clientCases++
live.client.socket.emit(base); live.client.socket.emit(delta)
assert.deepEqual(live.client.envelope, expected)
assert.deepEqual(live.metrics.localHandArrayDelta, {enabled: true, negotiatedSessions: 1,
  packets: 1, wireBytes: Buffer.byteLength(JSON.stringify(delta)), applied: 1, rejected: 0}); clientCases++
const lastGood = JSON.stringify(live.client.envelope)
live.client.socket.emit(delta) // Wrong committed baseline must not partially publish either hand.
assert.equal(JSON.stringify(live.client.envelope), lastGood)
assert.equal(live.metrics.localHandArrayDelta.packets, 2)
assert.equal(live.metrics.localHandArrayDelta.wireBytes, 2 * Buffer.byteLength(JSON.stringify(delta)))
assert.equal(live.metrics.localHandArrayDelta.applied, 1)
assert.equal(live.metrics.localHandArrayDelta.rejected, 1)
assert.equal(live.metrics.reconstructionErrors, 1); clientCases++
live.client.socket.close(); assert.equal(live.client.negotiatedHandArrayDelta, false); clientCases++
const unsupported = createClient({requested: true, serverHand: false})
await assert.rejects(() => unsupported.client.connect('ws://127.0.0.1:34567/ws'), /hand-array-capability-not-negotiated/)
assert.equal(unsupported.metrics.localHandArrayDelta.negotiatedSessions, 0)
unsupported.client.socket.emit(base); unsupported.client.socket.emit(delta)
assert.deepEqual(unsupported.client.envelope, base)
assert.equal(unsupported.metrics.localHandArrayDelta.rejected, 1)
unsupported.client.socket.close(); clientCases++
const old = createClient({requested: false})
await old.client.connect('ws://127.0.0.1:34567/ws')
assert.equal(Object.hasOwn(old.sent[0].capabilities, 'handArrayDeltaV1'), false)
assert.equal(old.client.negotiatedHandArrayDelta, false)
old.client.socket.emit(base); old.client.socket.emit(legacy)
assert.deepEqual(old.client.envelope, expected)
assert.equal(old.metrics.localHandArrayDelta.packets, 0)
old.client.socket.emit(base); old.client.socket.emit(delta)
assert.deepEqual(old.client.envelope, base)
assert.equal(old.metrics.localHandArrayDelta.rejected, 1)
old.client.socket.close(); clientCases++
const nonlocal = createClient({requested: true, localBase: null})
await nonlocal.client.connect('ws://127.0.0.1:34567/ws')
assert.equal(Object.hasOwn(nonlocal.sent[0].capabilities, 'handArrayDeltaV1'), false)
assert.equal(nonlocal.client.negotiatedHandArrayDelta, false)
nonlocal.client.socket.close(); clientCases++

const diagnostic = createClient({requested: true, wireCost: true})
await diagnostic.client.connect('ws://127.0.0.1:34567/ws')
diagnostic.client.socket.emit(base); diagnostic.client.socket.emit(delta)
assert.deepEqual(diagnostic.client.envelope, expected)
const diagnosticBytes = Buffer.byteLength(JSON.stringify(delta))
const diagnosticSummary = diagnostic.metrics.localWireCost.summarize([{stage: 'pure', type: 'gameStateDelta', bytes: diagnosticBytes}])
assert.equal(diagnosticSummary.reconciled, true); assert.equal(diagnosticSummary.failures, 0)
assert.equal(diagnosticSummary.rows[0].parts.handSplices, Buffer.byteLength(JSON.stringify(delta.handSplices)))
assert.deepEqual(diagnostic.consumerOrder.slice(-4), ['dispatch', 'diagnostic', 'dispatch', 'diagnostic']); clientCases++
const invalidPacked = {type: 'gameStateCardFrameV1', payload: {invalid: true},
  cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null}
diagnostic.client.socket.emit(invalidPacked)
assert.deepEqual(diagnostic.client.envelope, expected)
assert.equal(diagnostic.metrics.reconstructionErrors, 1)
const rejectedFrameSummary = diagnostic.metrics.localWireCost.summarize([
  {stage: 'pure', type: 'gameStateDelta', bytes: diagnosticBytes},
  {stage: 'pure', type: 'gameStateCardFrameV1', bytes: Buffer.byteLength(JSON.stringify(invalidPacked))},
])
assert.equal(rejectedFrameSummary.reconciled, true)
assert.equal(rejectedFrameSummary.rows.length, 2); clientCases++
diagnostic.client.socket.close(); clientCases++
assert.equal(old.metrics.localWireCost, undefined); clientCases++

const runner = fs.readFileSync(path.join(import.meta.dirname, 'qualify-local-pressure.mjs'), 'utf8')
const optionStart = runner.indexOf('assert.ok(process.argv.slice(2).every(')
const optionEnd = runner.indexOf('\nconst accepted =', optionStart)
assert.ok(optionStart >= 0 && optionEnd > optionStart)
const parseLocalOptions = args => new Function('assert', 'process', runner.slice(optionStart, optionEnd)
  + ';return {hostProbeOnly,cleanupProbeOnly,notificationProbeOnly,handArrayDeltaV1,wireCost};')(assert, {argv: ['node', 'pure', ...args]})
assert.equal(parseLocalOptions([]).handArrayDeltaV1, false)
assert.equal(parseLocalOptions(['--hand-array-delta']).handArrayDeltaV1, true)
assert.equal(parseLocalOptions(['--host-probe', '--hand-array-delta']).hostProbeOnly, true)
assert.throws(() => parseLocalOptions(['--remote']))
assert.equal(parseLocalOptions([]).wireCost, false)
assert.equal(parseLocalOptions(['--wire-cost']).wireCost, true)
assert.ok(runner.includes('await core.initializeLocalQualificationProtocol(binding, {handArrayDeltaV1, wireCost})'))
assert.ok(runner.includes('handArraySha256: digest(fs.readFileSync('))
assert.ok(runner.includes('handArrayDeltaObservation: {...result.localHandArrayDelta,'))
clientCases++
console.log(JSON.stringify({status: 'passed', cases, frozenCoreUnchanged: true, networkCalls: 0}))
console.log(JSON.stringify({status: 'passed', actualAdaptedClientCases: clientCases,
  scope: 'memory transport, real generated client and real net/hand module; not actual WS, load or online capacity', networkCalls: 0}))

const accounts = Array.from({length: 69}, (_, index) => ({preflightAbsent: true,
  registrationOutcome: 'confirmed', id: 'synthetic-' + index, username: 'local-' + index,
  createdAt: '2026-10-21T00:00:00Z'}))
const context = {local: true, baseUrl: binding.baseUrl, expectedCommit: 'actual-local-version',
  cleanupDeadline: Date.now() + 900000}
let mutations = 0, waits = 0
const deleted = new Set()
const request = async (_base, route, _deadline, options) => {
  if (route === 'health') return {status: 'ok', serverVersion: context.expectedCommit}
  if (options.method) {mutations++; if (route.endsWith('/delete')) deleted.add(route.split('/')[4]); return}
  const id = route.split('/')[3], row = accounts.find(item => item.id === id)
  return {...row, role: 'player', permissionVersion: 1, deleted: deleted.has(id),
    username: deleted.has(id) ? 'deleted-' + id : row.username}
}
const cleanup = await cleanupLocalQualificationAccounts(context, {token: 'synthetic'}, accounts, request,
  async ms => {assert.equal(ms, 1300); waits++})
assert.deepEqual(cleanup, {playerSessionsRevoked: 69, adminSessionRevoked: 1, syntheticAccountsDeleted: 69, failures: 0})
assert.equal(mutations, 139); assert.equal(waits, 139)
await assert.rejects(() => cleanupLocalQualificationAccounts({...context, baseUrl: 'https://legion-12.com/'}, {}, [], request))
let mismatchWrites = 0
const mismatch = await cleanupLocalQualificationAccounts(context, {}, [accounts[0]], async (_base, route, _deadline, options) => {
  if (options.method) mismatchWrites++
  return route === 'health' ? {status: 'ok', serverVersion: 'wrong-version'} : undefined
}, async () => {})
assert.equal(mismatch.syntheticAccountsDeleted, 0); assert.equal(mismatch.failures, 2)
assert.equal(mismatchWrites, 0)
console.log(JSON.stringify({status: 'passed', localCleanupCases: 5, syntheticAccounts: 69, networkCalls: 0}))
