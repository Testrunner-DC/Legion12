import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync, spawnSync} from 'node:child_process'
import {pathToFileURL} from 'node:url'
import {createRequire} from 'node:module'
import {REPO, EVIDENCE_ROOT, CACHE_ROOT, FROZEN_CORE, FROZEN_SHA, PLAN, EXECUTION_GAPS, digest,
  assertTestrunTarget, assertGeneratedDirectories, validateBinding, validateProtocolBinding,
  createTestrunProtocol, adaptFrozenCore, cleanupOwnedTestrunAccounts, strictSshArguments, currentProtocolClientBinding} from './testrun-pressure-protocol.mjs'
import {inspectNormalPressureEvidence} from './testrun-pressure-finalizer.mjs'

// This test has no real transport. All target URL strings below are allowlist or
// in-memory client fixtures, never destinations passed to fetch/SSH/WebSocket.
let forbiddenNetworkCalls = 0
globalThis.fetch = () => {forbiddenNetworkCalls++; throw new Error('NETWORK_FORBIDDEN_IN_OFFLINE_TEST')}
const runId = crypto.randomUUID().replaceAll('-', '')
const output = path.join(EVIDENCE_ROOT, 'offline-' + runId), cache = path.join(CACHE_ROOT, 'offline-' + runId)
assertGeneratedDirectories(output, cache)
fs.mkdirSync(output, {recursive: true}); fs.mkdirSync(cache, {recursive: true})
process.env.TEMP = cache; process.env.TMP = cache; process.env.TMPDIR = cache
process.on('uncaughtExceptionMonitor', error => fs.writeFileSync(path.join(output, 'offline-failure.json'),
  JSON.stringify({schema: 1, status: 'failed', message: error.message, stack: error.stack,
    networkCalls: forbiddenNetworkCalls, remoteApplyReady: false, onlineCapacityAccepted: false}, null, 2), {flag: 'wx'}))
const results = []
const check = (name, action) => {action(); results.push({name, status: 'passed'})}
const reject = (name, action) => check(name, () => assert.throws(action))
const asyncCheck = async (name, action) => {await action(); results.push({name, status: 'passed'})}
const head = execFileSync('git', ['-C', REPO, 'rev-parse', 'HEAD'], {encoding: 'utf8', windowsHide: true}).trim()
fs.writeFileSync(path.join(output, 'baseline.json'), JSON.stringify({schema: 1,
  leaseBaseline: {head: 'baf301a70fd37f0241fff8ec0eb5b6838b8e3c35',
    unrelatedUntracked: ['TwelveLegions.Platform.Tests/SnapshotJsonEncodingTests.cs', '服务端WebSocket/TwelveLegions/L12SnapshotJson.cs'],
    allFiveLeasedFilesAbsent: true}, testHead: head,
  testStartStatus: execFileSync('git', ['-C', REPO, 'status', '--short'], {encoding: 'utf8', windowsHide: true}),
  boundary: 'No product files, frozen chain or loopback entry are edited by this suite.'}, null, 2), {flag: 'wx'})

check('exact-test-targets', () => {
  assertTestrunTarget('https://legion-12.com/testrun/'); assertTestrunTarget('wss://legion-12.com/testrun/ws', true)
})
for (const bad of ['https://legion-12.com/', 'https://legion-12.com/testrun', 'https://legion-12.com:443/testrun/',
  'https://legion-12.com/testrun/?a=1', 'https://legion-12.com/testrun/#a', 'https://user@legion-12.com/testrun/',
  'https://154.201.80.91/testrun/', 'http://127.0.0.1:8084/', 'https://legion-12.com/testrun/../',
  'https://legion-12.com/testrun/%2f', 'https://LEGION-12.com/testrun/']) reject('reject-target-' + results.length, () => assertTestrunTarget(bad))
reject('reject-formal-websocket', () => assertTestrunTarget('wss://legion-12.com/ws', true))
check('strict-pinned-ssh-arguments-no-connection', () => {
  const selected = strictSshArguments()
  for (const required of ['StrictHostKeyChecking=yes', 'HostName=154.201.80.91', 'HostKeyAlias=154.201.80.91',
    'IdentitiesOnly=yes', 'BatchMode=yes', 'GlobalKnownHostsFile=NUL']) assert.ok(selected.includes(required))
})
const client = currentProtocolClientBinding()
check('five-real-current-client-sources', () => validateProtocolBinding(client))
for (const key of ['netSha256', 'cardFrameSha256', 'handArraySha256', 'propertyFrameSha256', 'propertyNamesSha256']) reject('reject-source-sha-' + key, () => validateProtocolBinding({...client, [key]: '0'.repeat(64)}))
reject('reject-unbound-hand-module', () => {const bad = {...client}; delete bad.handArraySourcePath; validateProtocolBinding(bad)})
const original = fs.readFileSync(FROZEN_CORE)
check('original-F737-unmodified', () => assert.equal(digest(original), FROZEN_SHA))
const adapted = adaptFrozenCore(original.toString('utf8'), pathToFileURL(path.join(import.meta.dirname, 'testrun-pressure-protocol.mjs')).href)
check('all-adapter-changes-reverse-byte-exactly', () => {
  assert.equal(adapted.evidence.byteExactRoundtrip, true); assert.equal(adapted.evidence.restoredSha256, FROZEN_SHA)
  assert.equal(adapted.evidence.executionEnabled, false)
  assert.ok(adapted.source.includes("requireContract(false,'normal_pressure_execution_not_admitted')"))
})
reject('reject-changed-frozen-source', () => adaptFrozenCore(original + '\n', 'file:///unused'))
fs.writeFileSync(path.join(output, 'frozen-adaptation.json'), JSON.stringify(adapted.evidence, null, 2), {flag: 'wx'})
const protocol = await createTestrunProtocol(client)
// Exercise the generated adapter's actual current-net dispatch and negotiation,
// not only a stub of its presence handler or a separately maintained reducer.
const presenceStart = adapted.source.indexOf('function initializePresenceConsumer(binding=FIXED) {')
const presenceEnd = adapted.source.indexOf('\nclass WsClient {', presenceStart)
assert.ok(presenceStart >= 0 && presenceEnd > presenceStart)
let presenceInitializer = adapted.source.slice(presenceStart, presenceEnd)
assert.equal(presenceInitializer.split('import.meta.url').length, 2)
presenceInitializer = presenceInitializer.replace('import.meta.url', JSON.stringify(import.meta.url))
const presenceFactory = new Function('fs', 'crypto', 'createRequire', 'FIXED', 'sha256File',
  'let createPresenceAdapter=null;'+presenceInitializer+';initializePresenceConsumer(FIXED);return createPresenceAdapter;')(
    fs, crypto, createRequire, client, file => digest(fs.readFileSync(file)))
const presenceItems = Array.from({length: 12}, (_, index) => ({accountId:'synthetic-presence-'+index,
  username:'合成'+index,online:true,activity:'idle',roomCode:null,canInvite:false,canSpectate:false,
  actionReason:null,friendStatus:'none',friendDirection:'none'}))
function presenceClient(reuse=true) {
  const sent=[]; const socket={readyState:1,send(raw){sent.push(JSON.parse(raw))}}
  const consumer=presenceFactory(socket)
  consumer.handle({type:'session',capabilities:{presenceDeltaV1:true,...(reuse?{presenceOrderReuseV1:true}:{})}})
  consumer.handle({type:'resourceVersions',epoch:'synthetic-presence',revisions:{presence:1}})
  consumer.handle({type:'presenceSnapshot',resource:'presence',epoch:'synthetic-presence',revision:1,items:structuredClone(presenceItems)})
  return {consumer,sent}
}
const presenceDelta = () => ({type:'presenceDelta',resource:'presence',epoch:'synthetic-presence',
  baseRevision:1,revision:2,removedAccountIds:[presenceItems[1].accountId],
  upserts:[{...presenceItems[3],activity:'inRoom'}],orderFromBaseline:true})
check('generated-real-presence-dispatch-restores-removals-and-upserts',()=>{
  const {consumer,sent}=presenceClient(), result=consumer.handle(presenceDelta())
  assert.equal(result.applied,true);assert.equal(result.invalid,false)
  assert.deepEqual(consumer.inspect().items,presenceItems.filter((_,i)=>i!==1)
    .map((row)=>row.accountId===presenceItems[3].accountId?{...row,activity:'inRoom'}:row))
  assert.equal(consumer.inspect().revision,2);assert.deepEqual(sent,[])
})
check('generated-real-presence-recovery-is-single-pending-and-nonmutating',()=>{
  const {consumer,sent}=presenceClient(), bad=presenceDelta(), before=structuredClone(consumer.inspect())
  bad.upserts.push({...presenceItems[0],accountId:'unseen'})
  assert.equal(consumer.handle(bad).recoveryRequested,true)
  assert.equal(consumer.handle(bad).recoveryRequested,false)
  assert.deepEqual(consumer.inspect().items,before.items);assert.equal(consumer.inspect().revision,1)
  assert.deepEqual(sent,[{type:'syncPresence'}])
  consumer.handle({type:'presenceSnapshot',resource:'presence',epoch:'synthetic-presence',revision:3,items:presenceItems})
  const next=presenceDelta();next.baseRevision=3;next.revision=4
  assert.equal(consumer.handle(next).applied,true);assert.equal(consumer.inspect().pending,false)
})
check('generated-real-presence-old-server-retains-order-and-rejects-unnegotiated-reuse',()=>{
  const {consumer}=presenceClient(false), legacy=presenceDelta();delete legacy.orderFromBaseline
  legacy.order=presenceItems.filter((_,i)=>i!==1).map(row=>row.accountId)
  assert.equal(consumer.handle(legacy).applied,true)
  const next=presenceDelta();next.baseRevision=2;next.revision=3;next.removedAccountIds=[]
  assert.equal(consumer.handle(next).invalid,true)
  assert.equal(consumer.inspect().revision,2)
})
check('generated-real-presence-reset-retires-negotiation-and-baseline',()=>{
  const {consumer}=presenceClient();consumer.reset()
  assert.deepEqual(consumer.inspect().items,[]);assert.equal(consumer.inspect().negotiated,false)
  assert.equal(consumer.handle(presenceDelta()).applied,false)
  assert.deepEqual(consumer.inspect().items,[])
})
const card = id => ({instanceId: id, cardId: 'synthetic', extra: {text: '中文完整值', values: [1, true]}})
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
check('real-net-and-hand-reconstruct', () => assert.deepEqual(protocol.reduce(base, delta, true, true), expected))
check('unnegotiated-hand-rejected', () => assert.equal(protocol.reduce(base, delta, true, false), null))
check('wrong-baseline-rejected', () => assert.equal(protocol.reduce(expected, delta, true, true), null))
for (const [name, mutate] of [
  ['bad-hash', d => {d.handSplices.baseStateHash = 'bad'}],
  ['partial-hand', d => {d.handSplices.edits[1].resultIds = ['bad']}],
  ['duplicate-player', d => {d.handSplices.edits[1].playerIndex = 0}],
  ['mixed-hand-write', d => {d.changes.push({path: ['state', 'players', 0, 'hand'], value: []})}],
  ['privacy-view', d => {d.changes.push({path: ['state', 'you'], value: 1})}],
  ['prototype', d => {d.changes.push({path: ['state', '__proto__', 'normalProbePollution'], value: true})}],
]) check('real-client-reject-' + name, () => {const bad = structuredClone(delta); mutate(bad); assert.equal(protocol.reduce(base, bad, true, true), null)})
const legacy = structuredClone(delta); delete legacy.handSplices
legacy.changes.push({path: ['state', 'players', 0, 'hand'], value: expected.state.players[0].hand},
  {path: ['state', 'players', 1, 'hand'], value: []})
check('legacy-delta-still-restores', () => assert.deepEqual(protocol.reduce(base, legacy, true, false), expected))
const classStart = adapted.source.indexOf('class WsClient {'), classEnd = adapted.source.indexOf('\nasync function checkCommit(', classStart)
assert.ok(classStart >= 0 && classEnd > classStart)
function memoryClient(serverHand = true, serverPresenceReuse = true, serverProperty = true) {
  const sent = [], recorded = []
  let socketConstructors = 0
  class MemoryWebSocket extends EventTarget {
    static OPEN = 1; static CLOSING = 2; readyState = 1
    constructor() {super(); socketConstructors++; queueMicrotask(() => this.dispatchEvent(new Event('open')))}
    emit(message) {this.dispatchEvent(Object.assign(new Event('message'), {data: JSON.stringify(message)}))}
    send(raw) {
      const message = JSON.parse(raw); sent.push(message)
      if (message.type === 'hello') queueMicrotask(() => {
        this.emit({type: 'session', capabilities: {...message.capabilities, handArrayDeltaV1: serverHand,
          presenceOrderReuseV1: serverPresenceReuse, gameStatePropertyFrameV1: serverProperty}})
        this.emit({type: 'recoveryComplete'})
      })
    }
    close() {this.readyState = 3; this.dispatchEvent(new Event('close'))}
  }
  const Client = new Function('productionCodec', 'createPresenceAdapter', 'requireContract', 'SafeError', 'reduceEnvelope', 'WebSocket', 'assertTestrunTarget',
    adapted.source.slice(classStart, classEnd) + ';return WsClient;')(protocol.codec, presenceFactory,
    (condition, code) => assert.ok(condition, code), Error, protocol.reduce, MemoryWebSocket, assertTestrunTarget)
  const metrics = {currentStage: 'memory-only', recordWs(...args) {recorded.push(args)}, reconstructionErrors: 0, presenceNegotiatedClients: 0,
    currentHandArrayDelta: {negotiatedSessions: 0, packets: 0, wireBytes: 0, applied: 0, rejected: 0},
    currentPresenceOrderReuse: {negotiatedSessions: 0, packets: 0, wireBytes: 0, applied: 0, rejected: 0}}
  return {client: new Client('synthetic-only', metrics, 1000, new AbortController().signal,
    {observeParsed() {return null}, afterDispatch() {}}), metrics, sent, recorded, socketConstructors: () => socketConstructors}
}
await asyncCheck('generated-actual-client-hello-session-and-hand-packet', async () => {
  const item = memoryClient()
  await item.client.connect('wss://legion-12.com/testrun/ws')
  assert.equal(item.sent[0].capabilities.handArrayDeltaV1, true)
  assert.equal(item.sent[0].capabilities.presenceOrderReuseV1, true)
  assert.equal(item.client.negotiatedHandArrayDelta, true)
  item.client.socket.emit(base); item.client.socket.emit(delta)
  assert.deepEqual(item.client.envelope, expected)
  assert.deepEqual(item.metrics.currentHandArrayDelta, {negotiatedSessions: 1, packets: 1,
    wireBytes: Buffer.byteLength(JSON.stringify(delta)), applied: 1, rejected: 0})
  item.client.socket.emit(delta)
  assert.equal(item.metrics.currentHandArrayDelta.rejected, 1); assert.deepEqual(item.client.envelope, expected)
  item.client.socket.close(); assert.equal(item.client.negotiatedHandArrayDelta, false)
})
await asyncCheck('generated-actual-client-refuses-missing-server-hand-capability', async () => {
  const item = memoryClient(false)
  await assert.rejects(() => item.client.connect('wss://legion-12.com/testrun/ws'), /hand-array-capability-not-negotiated/)
  item.client.socket.close()
})
await asyncCheck('generated-whole-socket-client-negotiates-restores-and-retires-presence-order', async () => {
  const item=memoryClient()
  await item.client.connect('wss://legion-12.com/testrun/ws')
  assert.equal(item.metrics.currentPresenceOrderReuse.negotiatedSessions,1)
  item.client.socket.emit({type:'resourceVersions',epoch:'synthetic-presence',revisions:{presence:1}})
  item.client.socket.emit({type:'presenceSnapshot',resource:'presence',epoch:'synthetic-presence',revision:1,items:presenceItems})
  const packet=presenceDelta();item.client.socket.emit(packet)
  assert.equal(item.client.presenceConsumer.inspect().revision,2)
  assert.equal(item.client.presenceConsumer.inspect().items.length,11)
  assert.deepEqual(item.metrics.currentPresenceOrderReuse,{negotiatedSessions:1,packets:1,
    wireBytes:Buffer.byteLength(JSON.stringify(packet)),applied:1,rejected:0})
  item.client.socket.close()
  assert.deepEqual(item.client.presenceConsumer.inspect().items,[])
  assert.equal(item.client.presenceConsumer.inspect().negotiated,false)
})
await asyncCheck('generated-whole-socket-client-keeps-old-server-order-fallback',async()=>{
  const item=memoryClient(true,false)
  await item.client.connect('wss://legion-12.com/testrun/ws')
  assert.equal(item.metrics.currentPresenceOrderReuse.negotiatedSessions,0)
  item.client.socket.emit({type:'resourceVersions',epoch:'synthetic-presence',revisions:{presence:1}})
  item.client.socket.emit({type:'presenceSnapshot',resource:'presence',epoch:'synthetic-presence',revision:1,items:presenceItems})
  const packet=presenceDelta();delete packet.orderFromBaseline
  packet.order=presenceItems.filter((_,i)=>i!==1).map(row=>row.accountId)
  item.client.socket.emit(packet)
  assert.equal(item.client.presenceConsumer.inspect().revision,2)
  assert.equal(item.metrics.currentPresenceOrderReuse.packets,0)
  item.client.socket.close()
})
await asyncCheck('generated-actual-client-refuses-formal-url-before-transport', async () => {
  const item = memoryClient()
  await assert.rejects(() => item.client.connect('wss://legion-12.com/ws'))
  assert.equal(item.sent.length, 0)
})
await asyncCheck('generated-actual-client-accepts-original-room-URL-object', async () => {
  const item = memoryClient()
  await item.client.connect(new URL('wss://legion-12.com/testrun/ws'))
  assert.equal(item.socketConstructors(), 1)
  assert.equal(item.sent[0].type, 'hello')
  assert.equal(item.client.negotiatedHandArrayDelta, true)
  item.client.socket.close()
})
for (const bad of ['wss://legion-12.com/ws', 'wss://legion-12.com/testrun/ws?x=1',
  'wss://legion-12.com/testrun/ws#x', 'wss://user@legion-12.com/testrun/ws',
  'ws://legion-12.com/testrun/ws', 'wss://example.invalid/testrun/ws']) {
  await asyncCheck('generated-URL-object-rejected-before-socket-' + results.length, async () => {
    const item = memoryClient()
    await assert.rejects(() => item.client.connect(new URL(bad)))
    assert.equal(item.socketConstructors(), 0)
    assert.equal(item.sent.length, 0)
  })
}

const propertyNames = JSON.parse(fs.readFileSync(client.propertyNamesSourcePath, 'utf8'))
const propertyTokens = new Map(propertyNames.map((name, index) => [name, '@' + index.toString(36)]))
function propertyFixtureEncode(value) {
  if (Array.isArray(value)) return value.map(propertyFixtureEncode)
  if (value === null || typeof value !== 'object') return value
  return Object.fromEntries(Object.entries(value).map(([key, item]) => [
    propertyTokens.get(key) ?? (/^[~@]/.test(key) ? '~' + key : key), propertyFixtureEncode(item)]))
}
const propertyFull = revision => ({type: 'gameState', state: {matchId: 'synthetic-property', revision,
  stateHash: 'h' + revision, phase: 'Main', you: 0, recentEvents: [], lastAction: null,
  players: [{playerIndex: 0, hand: []}, {playerIndex: 1, hand: []}]}})
const propertyFixture = payload => ({type: 'gameStatePropertyFrameV1', version: 1,
  matchId: payload.type === 'gameStateDelta' ? payload.matchId
    : payload.type === 'gameStateCardFrameV1' ? payload.payload.state.matchId : payload.state.matchId,
  payload: propertyFixtureEncode(payload)})
await asyncCheck('generated-property-full-delta-card-terminal-and-original-plain-recovery', async () => {
  const item = memoryClient(); await item.client.connect('wss://legion-12.com/testrun/ws')
  check('generated-property-actual-negotiation', () => {
    assert.equal(item.sent[0].capabilities.gameStatePropertyFrameV1, true)
    assert.equal(item.client.negotiatedPropertyFrame, true)
    assert.equal(item.metrics.currentPropertyFrame.negotiatedSessions, 1)
  })
  const payloads = [propertyFull(1), {type: 'gameStateDelta', matchId: 'synthetic-property', baseRevision: 1,
    revision: 2, changes: [{path: ['state', 'revision'], value: 2}, {path: ['state', 'stateHash'], value: 'h2'}]},
    {type: 'gameStateCardFrameV1', payload: propertyFull(3), cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null},
    {...propertyFull(4), state: {...propertyFull(4).state, phase: 'GameOver'}}]
  let wireBytes = 0
  for (const [index, payload] of payloads.entries()) {
    const packet = propertyFixture(payload); wireBytes += Buffer.byteLength(JSON.stringify(packet))
    item.client.socket.emit(packet)
    check('generated-property-dispatch-' + index, () => {
      assert.deepEqual(item.client.envelope, index === 3 ? payload : propertyFull(index + 1))
      assert.equal(item.metrics.currentPropertyFrame.applied, index + 1)
      assert.equal(item.metrics.reconstructionErrors, 0)
      assert.equal(item.recorded.at(-1)[0], 'gameStatePropertyFrameV1')
      assert.equal(item.recorded.at(-1)[3], index === 1 ? 'gameStateDelta' : 'gameState')
    })
  }
  check('generated-property-original-wire-byte-total', () => assert.deepEqual(item.metrics.currentPropertyFrame,
    {negotiatedSessions: 1, packets: 4, wireBytes, applied: 4, rejected: 0}))
  const before = structuredClone(item.client.envelope), bad = {...propertyFixture(propertyFull(5)), version: 2}
  item.client.socket.emit(bad); item.client.socket.emit(bad)
  check('generated-property-rejection-keeps-baseline-and-single-recovery', () => {
    assert.deepEqual(item.client.envelope, before)
    assert.equal(item.metrics.currentPropertyFrame.rejected, 2)
    assert.equal(item.sent.filter(value => value.type === 'syncState' && value.plainFull === true).length, 1)
    assert.equal(item.client.negotiatedPropertyFrame, false)
    assert.equal(item.client.negotiatedCardFrame, false)
    assert.equal(item.client.plainRecoveryRequests, 1)
  })
  item.client.socket.emit(propertyFull(5)); item.client.socket.emit({type: 'recoveryComplete'})
  check('generated-property-original-plain-recovery', () => assert.deepEqual(item.client.envelope, propertyFull(5)))
  item.client.socket.close()
  check('generated-property-close-retirement', () => assert.equal(item.client.negotiatedPropertyFrame, false))
})
await asyncCheck('generated-property-nested-card-uses-original-source-not-larger-wrapper', async () => {
  const item = memoryClient(); await item.client.connect('wss://legion-12.com/testrun/ws')
  const state = {matchId: 's'.repeat(200), recentEvents: [], lastAction: null, syntheticPadding: ''}
  const inner = {type: 'gameStateCardFrameV1', payload: {type: 'gameState', state},
    cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null}
  state.syntheticPadding = 'p'.repeat(2 * 1024 * 1024 - 16 - Buffer.byteLength(JSON.stringify(inner)))
  const packet = propertyFixture(inner)
  assert(Buffer.byteLength(JSON.stringify(packet)) > 2 * 1024 * 1024)
  item.client.socket.emit(packet)
  assert.equal(item.client.envelope.state.syntheticPadding.length, state.syntheticPadding.length)
  assert.equal(item.metrics.reconstructionErrors, 0); assert.equal(item.client.plainRecoveryRequests, 0)
  item.client.socket.close()
})
await asyncCheck('generated-property-refuses-server-without-capability', async () => {
  const item = memoryClient(true, true, false)
  await assert.rejects(() => item.client.connect('wss://legion-12.com/testrun/ws'), /property-frame-capability-not-negotiated/)
  item.client.socket.close()
})
const accounts = Array.from({length: 69}, (_, index) => ({preflightAbsent: true, registrationOutcome: 'confirmed',
  id: 'synthetic-' + index, username: 'normal-' + index, createdAt: '2026-10-22T00:00:00Z'}))
const context = {local: false, baseUrl: 'https://legion-12.com/testrun/', expectedCommit: head, runId,
  cleanupDeadline: Date.now() + 900000}
const admin = {token: 'a'.repeat(64)}
let mutations = 0, waits = 0, adminRevoked = false
const deleted = new Set()
const request = async (_base, route, _deadline, options) => {
  assert.equal(_base, context.baseUrl)
  if (route === 'health') return {status: 'ok', serverVersion: head, maintenance: false}
  if (route === 'api/auth/me') {assert.ok(adminRevoked); assert.deepEqual(options.expected, [401]); return}
  if (options.method) {
    mutations++
    if (route.endsWith('/delete')) {assert.equal(options.body.expectedVersion, 7); assert.equal(options.body.dryRun, false); deleted.add(route.split('/')[4])}
    if (route === 'api/auth/sessions/current') adminRevoked = true
    return
  }
  if(route.endsWith('/sessions')){assert.ok(deleted.has(route.split('/')[3]));return []}
  const id = route.split('/')[3], row = accounts.find(item => item.id === id)
  return {...row, role: 'player', permissionVersion: 7, deleted: deleted.has(id), username: deleted.has(id) ? 'deleted-' + id : row.username}
}
await asyncCheck('current-CAS-cleanup-69-accounts-70-sessions-and-readback', async () => {
  const previousNow=Date.now;let virtualNow=0
  try {
    Date.now=()=>virtualNow
    const cleanup = await cleanupOwnedTestrunAccounts({...context,cleanupDeadline:900000}, admin, accounts, request,
      async ms => {assert.ok(ms>0&&ms<=1300); waits++;virtualNow+=ms})
    assert.deepEqual(cleanup, {playerSessionsRevoked: 69, adminSessionRevoked: 1, syntheticAccountsDeleted: 69, failures: 0})
    assert.equal(mutations, 70); assert.equal(waits,69);assert.equal(virtualNow,89700)
  } finally {Date.now=previousNow}
})
for (const [name, change] of [['wrong-version', {serverVersion: '0'.repeat(40)}], ['maintenance', {maintenance: true}]])
  await asyncCheck('cleanup-zero-writes-on-' + name, async () => {
    let writes = 0
    const result = await cleanupOwnedTestrunAccounts(context, admin, [accounts[0]], async (_base, route, _deadline, options) => {
      if (options.method) writes++
      return route === 'health' ? {status: 'ok', serverVersion: head, maintenance: false, ...change} : undefined
    }, async () => {})
    assert.equal(writes, 0); assert.equal(result.failures, 2)
  })
await asyncCheck('cleanup-rejects-formal-before-any-request', async () => {
  await assert.rejects(() => cleanupOwnedTestrunAccounts({...context, baseUrl: 'https://legion-12.com/'}, admin, [], request))
})
await asyncCheck('cleanup-ownership-mismatch-cannot-delete-account', async () => {
  let accountWrites = 0
  const result = await cleanupOwnedTestrunAccounts(context, admin, [accounts[0]], async (_base, route, _deadline, options) => {
    if (options.method && route.startsWith('api/admin/')) accountWrites++
    if (route === 'health') return {status: 'ok', serverVersion: head, maintenance: false}
    if (route.startsWith('api/admin/')) return {...accounts[0], role: 'admin', deleted: false, permissionVersion: 1}
  }, async () => {})
  assert.equal(accountWrites, 0); assert.equal(result.syntheticAccountsDeleted, 0); assert.ok(result.failures > 0)
})

// Clearly synthetic, tiny package fixture: no release/deployment/capacity claim.
const fixture = path.join(cache, 'package-fixture'); fs.mkdirSync(fixture)
const archiveBytes = Buffer.from('offline-only checksum fixture; not an application package')
fs.writeFileSync(path.join(fixture, `l12-release-${head}.tar.gz`), archiveBytes, {flag: 'wx'})
const manifestPath = path.join(fixture, 'manifest.json')
const manifest = {schema: 3, commit: head, releaseSha256: digest(archiveBytes).toLowerCase(), releaseBaseCommit: '0'.repeat(40)}
fs.writeFileSync(manifestPath, JSON.stringify(manifest), {flag: 'wx'})
const binding = {schema: 1, kind: 'normal-testrun-pressure-v1', baseUrl: context.baseUrl, wsUrl: 'wss://legion-12.com/testrun/ws',
  test: {commit: head, pid: 101, dllSha256: 'B'.repeat(64), manifestPath,
    manifestSha256: digest(fs.readFileSync(manifestPath)), releaseSha256: digest(archiveBytes)},
  formal: {commit: head, pid: 102, dllSha256: 'B'.repeat(64)}, client}
check('same-package-distinct-environments-admitted-offline', () => validateBinding(binding))
check('different-package-formal-is-not-a-releaseBase-requirement', () => validateBinding({...binding, formal: {...binding.formal, commit: 'c'.repeat(40)}}))
reject('same-process-is-not-test-isolation', () => validateBinding({...binding, formal: {...binding.formal, pid: 101}}))
reject('manifest-hash-mismatch', () => validateBinding({...binding, test: {...binding.test, manifestSha256: '0'.repeat(64)}}))
reject('extra-binding-field', () => validateBinding({...binding, syntheticOnly: true}))

function prospectiveEvidence() {
  const floor = {status: 'ready', workersFrozen: true, expectedRooms: 30, observedRooms: 30, issues: [],
    minimumLogicalBytes: PLAN.minimumLogicalBytes, maximumLogicalBytes: PLAN.maximumLogicalBytes,
    rows: Array.from({length: 30}, (_, numericRoomSlot) => ({numericRoomSlot, sameMatch: true, sameRoom: true, sameRevision: true,
      exactAckMatches: 1, peerConvergenceMatches: 1, receivers: [0, 1].map(receiver => ({receiver, revision: 2, logicalJsonBytes: 200 * 1024}))}))}
  const terminalFrames = Array.from({length: 60}, (_, numericClientSlot) => ({numericClientSlot, numericRoomSlot: Math.floor(numericClientSlot / 2),
    receiver: numericClientSlot % 2, type: 'gameState', terminal: true, delta: false, wireType: 'gameStateCardFrameV1',
    wireBytes: 30000, logicalJsonBytes: 200 * 1024, sendToCallbackMs: 3000, exceededOriginal3s: 0, parseMs: 0.2, restoreMs: 0.1}))
  const http = Object.fromEntries(['authMe', 'deckSummaries', 'rankedProfile', 'rankings', 'tournamentSummaries',
    'publicDecks', 'matchHistory', 'cardAnalytics', 'audit', 'tournamentDetail'].map(name => [name, {requests: 1, errors: 0}]))
  const ledgers = [60, 9].map((count, index) => ({verified: true, coldReadVerified: true, count, seq: count * 2, cipherSha256: String(index + 1).repeat(64)}))
  const report = {schemaVersion: 1, status: 'completed', failure: null, stopReason: null, startedAt: new Date(0).toISOString(),
    terminalWindow: {startUtc: new Date(180000).toISOString(), endUtc: new Date(180100).toISOString()},
    expectedCommit: {value: head}, observedCommit: head,
    artifactBinding: {candidateBindingSha256: 'A'.repeat(64), manifestSha256: binding.test.manifestSha256,
      releaseSha256: binding.test.releaseSha256, testPid: 101, formalPid: 102, deployedNetSha256: client.netSha256,
      cardFrameSha256: client.cardFrameSha256, handArraySha256: client.handArraySha256,
      propertyFrameSha256: client.propertyFrameSha256, propertyNamesSha256: client.propertyNamesSha256},
    deltaReconstruction: {sourceSha256: client.netSha256, errors: 0}, presenceDeltaReconstruction: {sourceSha256: client.netSha256, errors: 0},
    budget: {roomStages: [10, 20, 30], stageSeconds: 60, maximumRooms: 30, playerIdentities: 60, distinctOutsideUsers: 10,
      syntheticIdentities: 69, maximumIdentities: 70, httpIntervalMs: 2000, wsPingIntervalMs: 2000, actionInspectionIntervalMs: 2000,
      maximumCommandsPerRoomPerInterval: 1, requestTimeoutMs: 10000, disjointMatchAndOutsideAccountPools: true},
    stages: [10, 20, 30].map(rooms => ({name: 'rooms-' + rooms, rooms, durationMs: 60000, minimumActiveRooms: rooms,
      maximumActiveRooms: rooms, outsideUsers: 10, measuredDistinctOutsideUsers: 10})),
    http, outsideHttpByStage: Object.fromEntries([10, 20, 30].map(rooms => ['rooms-' + rooms, structuredClone(http)])),
    commands: {overall: {attempted: 1, outcomes: {rejected: 0, 'timed-out': 0, 'transport-error': 0, 'advanced-without-ack': 0}}},
    currentHandArrayDelta: {negotiatedSessions: 60, packets: 1, wireBytes: 100, applied: 1, rejected: 0},
    currentPropertyFrame: {negotiatedSessions: 60, packets: 60, wireBytes: 100, applied: 60, rejected: 0},
    frameProtocol: {capability: 'gameStateCardFrameV1', decoderFailures: 0}, payloadFloorPrecheck: floor, terminalFrames,
    wsBurstClient: {captureComplete: 1, terminalCoverageComplete: 1, connectionsObserved: 60, droppedFrames: 0, invalidRecords: 0},
    actionBoundsClient: {expected_samples: 6, begun_samples: 6, surrender_begun: 6, play_card_begun: 0, end_turn_begun: 0,
      invalid_records: 0, expired_begins: 0, late_callbacks: 0, client_complete: 1,
      client: {invalid_records: 0, rows: [0, 5, 11, 17, 23, 29].map((_room, slot) => ({slot, command_kind: 3, response_kind: 1, terminal: 1,
        response_seen: 1, callback_valid: 1, parse_valid: 1, dispatch_valid: 1, receipt_send_valid: 1,
        wire_bytes: 30000, send_to_callback_ms: 3000, parse_ms: 0.2, exceeded_original_3s: 0}))}},
    cleanupPassiveTail: {originalFailureThresholdMs: 3000, passiveOnlyMs: 10000, retries: 0},
    cleanup: {roomsCreated: 30, compliantSurrenders: 30, roomsLeft: 30, sessionsRevoked: 70, roomErrors: [], sessionErrors: [],
      syntheticAccounts: {planned: 69, registrationAttempts: 69, confirmed: 69, logicalDeletesConfirmed: 69, uncertainRegistrationResponses: 0, errors: []}},
    ledgerReceipts: ledgers.map(({count, seq, cipherSha256}) => ({count, seq, cipherSha256}))}
  return {schema: 1, bindingSha256: 'A'.repeat(64), frozenCoreSha256: FROZEN_SHA, report, ledgers,
    preflight: {baseUrl: context.baseUrl, testPid: 101, formalPid: 102, diagnosticsOff: true, matchStorage: 'ephemeral',
      privateDeck: {configuredEnabled: true, effectiveMode: 'object', writable: true}, sharedWindowExclusive: true,
      analyticsItems: 1, auditItems: 1, tournamentItems: 1,
      resourcePolicy: {cpuWeight: 10, ioWeight: 10, nice: 10, oomScoreAdjust: 750, memoryHighBytes: 768 * 1024 ** 2, memoryMaxBytes: 896 * 1024 ** 2},
      disk: {programBytes: 1, dataBytes: 1, temporaryPeakBytes: 1, totalPeakBytes: 3, formalFreeBytes: 8 * 1024 ** 3}},
    lifecycle: {businessStartedUtcMs: 0, cleanupFinishedUtcMs: 182000, businessSucceeded: true, captureSucceeded: true,
      captureBeforeTeardown: true, teardownSucceeded: true, independentRecoverySessionVerified: true, allOwnedHelpersExited: true,
      originalSessionRevoked: true, recoverySessionRevoked: true, formalWrites: 0, serviceRestarts: 0, maintenanceWrites: 0},
    guard: {stopRequested: false, staleLimitMs: 5000, rows: Array.from({length: 92}, (_, index) => ({observedUtcMs: index * 2000,
      readOnly: true, stop: false, phase: 'complete', testPid: 101, formalPid: 102, roundElapsedMs: 2,
      testMemoryBytes: 850 * 1024 ** 2 - 1, hostAvailableBytes: 512 * 1024 ** 2, oomDelta: 0, restartDelta: 0}))},
    postflight: Object.fromEntries([['test', binding.test], ['formal', binding.formal]].map(([name, identity]) => [name,
      {pid: identity.pid, commit: identity.commit, dllSha256: identity.dllSha256, status: 'ok', maintenance: false, diagnosticsOff: true, restartDelta: 0}]))}
}
check('prospective-contract-valid-is-never-live-acceptance', () => {
  const result = inspectNormalPressureEvidence(prospectiveEvidence(), binding)
  assert.equal(result.status, 'prospective-input-contract-valid'); assert.equal(result.remoteApplyReady, false)
  assert.equal(result.onlineCapacityAccepted, false); assert.equal(result.executionEnabled, false)
  assert.deepEqual(result.missingExecutionComponents, EXECUTION_GAPS)
})
await asyncCheck('actual-frozen-cleanup-marker-begins-before-native-send-clock', async () => {
  const cleanupStart = adapted.source.indexOf('async function cleanupRoom(')
  const cleanupEnd = adapted.source.indexOf('\nasync function logout(', cleanupStart)
  const sendStart = adapted.source.indexOf('\n  send(payload) {', classStart)
  const sendEnd = adapted.source.indexOf('\n  close() {', sendStart)
  assert.ok(cleanupStart > 0 && cleanupEnd > cleanupStart && sendStart > 0 && sendEnd > sendStart)
  let clockCalls = 0, markerStartedAt
  const clock = {now: () => 1000 + [0, .1, .2, .28][clockCalls++]}
  const nativeSends = [], burstSends = []
  const send = new Function('performance', 'WebSocket', 'SafeError',
    'return ({' + adapted.source.slice(sendStart, sendEnd) + '}).send;')(clock, {OPEN: 1}, Error)
  const peer = slot => ({closed: false, numericRoomSlot: 0, numericClientSlot: slot,
    envelope: {state: {matchId: 'synthetic-clock-origin'}}, cleanupTraces: new Map(),
    socket: {readyState: 1, send: raw => nativeSends.push(JSON.parse(raw))},
    metrics: {currentStage: 'cleanup', burstObserver: {observeSend: (...args) => burstSends.push(args)}},
    send, close() {this.closed = true}})
  const room = {host: peer(0), guest: peer(1)}
  const cleanup = new Function('cleanupRoomProtocol', 'performance', 'crypto', 'remainingMs', 'delay', 'safeFailure',
    adapted.source.slice(cleanupStart, cleanupEnd) + ';return cleanupRoom;')(
      async (pair, options) => {
        const requestId = options.requestIdFactory()
        pair.host.send({type: 'command', requestId, command: {type: 'surrender'}})
        return {compliantSurrenders: 1, roomsLeft: 1, errors: []}
      }, clock, crypto, () => 3000, async () => {}, error => error.message)
  const outcome = {compliantSurrenders: 0, roomsLeft: 0, roomErrors: []}
  await cleanup(room, {cleanupDeadline: 10000, requestTimeoutMs: 10000, signal: {aborted: false},
    actionMarkers: {tryBegin: value => {markerStartedAt = value.sentAt; return 'synthetic-clock-request'}}}, outcome, 0)
  assert.equal(clockCalls, 4); assert.equal(markerStartedAt, 1000)
  assert.equal(room.host.terminalTrace.sentAt, 1000.28)
  assert.equal(room.guest.terminalTrace.sentAt, room.host.terminalTrace.sentAt)
  assert.equal(burstSends[0][2], room.host.terminalTrace.sentAt)
  assert.equal(nativeSends[0].requestId, 'synthetic-clock-request'); assert.deepEqual(outcome.roomErrors, [])
  assert.equal(room.host.closed && room.guest.closed, true)
  const evidence = prospectiveEvidence(), callbackAt = 2500
  evidence.report.terminalFrames[0].sendToCallbackMs = callbackAt - room.host.terminalTrace.sentAt
  evidence.report.actionBoundsClient.client.rows[0].send_to_callback_ms = callbackAt - markerStartedAt
  assert.ok(evidence.report.actionBoundsClient.client.rows[0].send_to_callback_ms
    > evidence.report.terminalFrames[0].sendToCallbackMs)
  const result = inspectNormalPressureEvidence(evidence, binding)
  assert.equal(result.onlineCapacityAccepted, false)
})
for (const [name, mutate] of [
  ['legacy-install', e => {e.install = {status: 'installed'}}],
  ['wrong-target', e => {e.preflight.baseUrl = 'https://legion-12.com/'}],
  ['same-pid', e => {e.preflight.formalPid = 101}],
  ['snapshot-F2', e => {e.preflight.privateDeck.effectiveMode = 'snapshot'}],
  ['diagnostic-on', e => {e.preflight.diagnosticsOff = false}],
  ['shared-window', e => {e.preflight.sharedWindowExclusive = false}],
  ['empty-projection', e => {e.preflight.tournamentItems = 0}],
  ['disk-cap', e => {e.preflight.disk.dataBytes = 512 * 1024 ** 2 + 1}],
  ['resource-raised', e => {e.preflight.resourcePolicy.memoryMaxBytes++}],
  ['short-stage', e => {e.report.stages[2].durationMs = 59999}],
  ['rooms-missing', e => {e.report.stages[2].minimumActiveRooms = 29}],
  ['outside-nine', e => {e.report.stages[2].measuredDistinctOutsideUsers = 9}],
  ['HTTP-skip', e => {e.report.http.audit.skipped = true}],
  ['HTTP-error', e => {e.report.outsideHttpByStage['rooms-20'].audit.errors = 1}],
  ['faster-cadence', e => {e.report.budget.httpIntervalMs = 1999}],
  ['timeout-changed', e => {e.report.budget.requestTimeoutMs = 30000}],
  ['no-hand-cap', e => {e.report.currentHandArrayDelta.negotiatedSessions = 59}],
  ['no-hand-packet', e => {e.report.currentHandArrayDelta.packets = 0}],
  ['hand-rejected', e => {e.report.currentHandArrayDelta.rejected = 1}],
  ['wrong-client', e => {e.report.deltaReconstruction.sourceSha256 = '0'.repeat(64)}],
  ['artifact-hand-sha', e => {e.report.artifactBinding.handArraySha256 = '0'.repeat(64)}],
  ['artifact-property-sha', e => {e.report.artifactBinding.propertyFrameSha256 = '0'.repeat(64)}],
  ['artifact-property-vocabulary-sha', e => {e.report.artifactBinding.propertyNamesSha256 = '0'.repeat(64)}],
  ['property-not-negotiated', e => {e.report.currentPropertyFrame.negotiatedSessions = 59}],
  ['property-not-exercised', e => {e.report.currentPropertyFrame.packets = 0}],
  ['property-reconstruction-rejected', e => {e.report.currentPropertyFrame.rejected = 1}],
  ['artifact-binding', e => {e.report.artifactBinding.candidateBindingSha256 = '0'.repeat(64)}],
  ['wire-size', e => {e.report.terminalFrames[1].wireBytes = 2 * 1024 * 1024 + 1}],
  ['small-payload', e => {e.report.payloadFloorPrecheck.rows[0].receivers[0].logicalJsonBytes = 180 * 1024 - 1}],
  ['payload-ACK', e => {e.report.payloadFloorPrecheck.rows[0].peerConvergenceMatches = 0}],
  ['missing-terminal', e => {e.report.terminalFrames.pop()}],
  ['late-terminal', e => {e.report.terminalFrames[1].sendToCallbackMs = 3000.001}],
  ['incomplete-burst', e => {e.report.wsBurstClient.captureComplete = 0}],
  ['marker-mismatch', e => {e.report.actionBoundsClient.client.rows[1].wire_bytes++}],
  ['marker-clock-before-native-impossible', e => {e.report.actionBoundsClient.client.rows[0].send_to_callback_ms = 2999.999}],
  ['marker-clock-over-3s-even-with-zero-flag', e => {e.report.actionBoundsClient.client.rows[0].send_to_callback_ms = 3000.001}],
  ['marker-clock-negative', e => {e.report.actionBoundsClient.client.rows[0].send_to_callback_ms = -1}],
  ['marker-clock-nan', e => {e.report.actionBoundsClient.client.rows[0].send_to_callback_ms = NaN}],
  ['marker-clock-infinite', e => {e.report.actionBoundsClient.client.rows[0].send_to_callback_ms = Infinity}],
  ['marker-parse-mismatch', e => {e.report.actionBoundsClient.client.rows[0].parse_ms += .001}],
  ['marker-wrong-slot', e => {e.report.actionBoundsClient.client.rows[0].slot = 1}],
  ['marker-wrong-command', e => {e.report.actionBoundsClient.client.rows[0].command_kind = 1}],
  ['marker-late-flag', e => {e.report.actionBoundsClient.client.rows[0].exceeded_original_3s = 1}],
  ['late-passive-tail', e => {e.report.cleanupPassiveTail.originalFailureThresholdMs = 10000}],
  ['missing-ledger', e => {e.ledgers.pop()}],
  ['no-cold-reader', e => {e.ledgers[0].coldReadVerified = false}],
  ['cleanup-not-proven', e => {e.report.cleanup.syntheticAccounts.logicalDeletesConfirmed = 68}],
  ['no-independent-recovery', e => {e.lifecycle.independentRecoverySessionVerified = false}],
  ['capture-after-teardown', e => {e.lifecycle.captureBeforeTeardown = false}],
  ['formal-write', e => {e.lifecycle.formalWrites = 1}],
  ['restart', e => {e.lifecycle.serviceRestarts = 1}],
  ['guard-stale', e => {e.guard.rows[2].observedUtcMs += 5000}],
  ['guard-memory', e => {e.guard.rows[1].testMemoryBytes = 850 * 1024 ** 2}],
  ['guard-available', e => {e.guard.rows[1].hostAvailableBytes--}],
  ['guard-OOM', e => {e.guard.rows[1].oomDelta = 1}],
  ['guard-deadline', e => {e.guard.rows[1].roundElapsedMs = 3750}],
  ['guard-coverage', e => {e.guard.rows.pop()}],
  ['post-pid-drift', e => {e.postflight.formal.pid++}],
]) reject('finalizer-reject-' + name, () => {const evidence = prospectiveEvidence(); mutate(evidence); inspectNormalPressureEvidence(evidence, binding)})

const pwsh = process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
const entry = path.join(REPO, 'ops/qa/Invoke-TestrunRepresentativePressure.ps1')
const bindingPath = path.join(fixture, 'binding.json')
fs.writeFileSync(bindingPath, JSON.stringify(binding), {flag: 'wx'})
await asyncCheck('actual-NoApply-entry-prepared-only-zero-network', async () => {
  const label = 'noapply-' + runId
  const child = spawnSync(pwsh, ['-NoProfile', '-File', entry, '-BindingPath', bindingPath,
    '-BindingSha256', digest(fs.readFileSync(bindingPath)), '-RunLabel', label],
  {windowsHide: true, encoding: 'utf8', timeout: 45000, maxBuffer: 1048576, env: process.env})
  fs.writeFileSync(path.join(output, 'noapply-process.json'), JSON.stringify({status: child.status, error: child.error?.code,
    stdout: child.stdout, stderr: child.stderr}, null, 2), {flag: 'wx'})
  assert.equal(child.status, 0, child.stderr)
  const receipt = JSON.parse(fs.readFileSync(path.join(EVIDENCE_ROOT, label, 'prepared-only.json')))
  assert.equal(receipt.status, 'prepared-only'); assert.equal(receipt.remoteApplyReady, false)
  assert.equal(receipt.onlineCapacityAccepted, false); assert.equal(receipt.liveIdentityVerified, false)
  for (const name of ['networkCalls', 'sshCalls', 'businessRequests', 'serviceWrites', 'formalWrites', 'serviceRestarts', 'maintenanceWrites']) assert.equal(receipt[name], 0)
})
check('actual-Apply-refused-before-output-creation', () => {
  const label = 'apply-refused-' + runId
  const child = spawnSync(pwsh, ['-NoProfile', '-File', entry, '-BindingPath', bindingPath,
    '-BindingSha256', digest(fs.readFileSync(bindingPath)), '-RunLabel', label, '-Apply'],
  {windowsHide: true, encoding: 'utf8', timeout: 15000})
  assert.notEqual(child.status, 0); assert.match(child.stderr, /NORMAL_PRESSURE_EXECUTION_NOT_IMPLEMENTED/)
  assert.equal(fs.existsSync(path.join(EVIDENCE_ROOT, label)), false)
  assert.equal(fs.existsSync(path.join(CACHE_ROOT, label)), false)
})
check('python-normal-guard-pure-threshold-and-profile-tests', () => {
  const bytes = execFileSync(process.env.L12_PYTHON || 'python', ['-B', path.join(REPO, 'ops/qa/testrun-normal-resource-guard.py'), '--self-test'],
    {windowsHide: true, encoding: 'utf8', timeout: 30000, env: {...process.env, PYTHONDONTWRITEBYTECODE: '1'}})
  const proof = JSON.parse(bytes)
  assert.equal(proof.status, 'offline-tests-passed'); assert.equal(proof.networkCalls, 0); assert.equal(proof.remoteExecutionEnabled, false)
  fs.writeFileSync(path.join(output, 'guard-offline-tests.json'), JSON.stringify(proof, null, 2), {flag: 'wx'})
})
check('frozen-and-loopback-source-not-edited', () => {
  assert.equal(digest(fs.readFileSync(FROZEN_CORE)), FROZEN_SHA)
  const protectedDiff = execFileSync('git', ['-C', REPO, 'diff', '--', 'opcgpro-vue/scripts/local-pressure-protocol.mjs',
    'opcgpro-vue/scripts/qualify-local-pressure.mjs'], {encoding: 'utf8', windowsHide: true})
  assert.equal(protectedDiff, '')
})
assert.equal(forbiddenNetworkCalls, 0)
const report = {schema: 1, status: 'offline-tests-passed', cases: results.length, results, output,
  networkCalls: 0, sshCalls: 0, businessRequests: 0, productBuilds: 0, remoteApplyReady: false,
  onlineCapacityAccepted: false, missingExecutionComponents: [...EXECUTION_GAPS],
  qaSources: ['ops/qa/Invoke-TestrunRepresentativePressure.ps1', 'opcgpro-vue/scripts/testrun-pressure-protocol.mjs',
    'ops/qa/testrun-normal-resource-guard.py', 'opcgpro-vue/scripts/testrun-pressure-finalizer.mjs',
    'opcgpro-vue/scripts/test-testrun-pressure-current.mjs'].map(relative => ({path: relative,
    sha256: digest(fs.readFileSync(path.join(REPO, relative)))})),
  priorObservedFailure: {classification: 'Git LF versus working-tree CRLF raw-byte mismatch',
    resolution: 'Retain raw loaded-module SHA and separately prove candidate content with only CRLF-to-LF normalization.'},
  fixtureBoundary: 'Synthetic contract and checksum fixtures plus actual current client memory transport. No real release or remote lifecycle was tested.'}
fs.writeFileSync(path.join(output, 'offline-tests.json'), JSON.stringify(report, null, 2), {flag: 'wx'})
console.log(JSON.stringify({status: report.status, cases: report.cases, output, networkCalls: 0,
  remoteApplyReady: false, onlineCapacityAccepted: false, missingExecutionComponents: report.missingExecutionComponents}))
