import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'
import { deploymentSourceModuleUrl } from './deployment-source-module.mjs'
import { propertyFrameSourceModuleUrl } from './property-frame-source-module.mjs'

const frontendRoot = fileURLToPath(new URL('..', import.meta.url))
const sourceRoot = join(frontendRoot, 'src', 'l12')
const propertyNames = JSON.parse(readFileSync(join(sourceRoot, 'gameStatePropertyNamesV1.json'), 'utf8'))
function propertyPacket(payload) {
  const encode = value => {
    if (Array.isArray(value)) return value.map(encode)
    if (!value || typeof value !== 'object') return value
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [
      propertyNames.includes(key) ? '@' + propertyNames.indexOf(key).toString(36)
        : /^[~@]/.test(key) ? '~' + key : key, encode(item)]))
  }
  const matchId = payload.type === 'gameStateDelta' ? payload.matchId
    : payload.type === 'gameStateCardFrameV1' ? payload.payload.state.matchId : payload.state.matchId
  return { type: 'gameStatePropertyFrameV1', version: 1, matchId, payload: encode(payload) }
}
let moduleSerial = 0

class MemoryStorage {
  constructor(initial = {}) { this.values = new Map(Object.entries(initial)) }
  getItem(key) { return this.values.has(key) ? this.values.get(key) : null }
  setItem(key, value) { this.values.set(key, String(value)) }
  removeItem(key) { this.values.delete(key) }
  clear() { this.values.clear() }
}

class FakeTimers {
  now = 0
  nextId = 1
  tasks = new Map()

  setTimeout = (callback, delayMs = 0, ...args) => this.#add(callback, delayMs, 0, args)
  clearTimeout = id => this.tasks.delete(id)
  setInterval = (callback, delayMs = 0, ...args) => this.#add(callback, delayMs, Math.max(1, Number(delayMs) || 0), args)
  clearInterval = id => this.tasks.delete(id)

  #add(callback, delayMs, interval, args) {
    const id = this.nextId++
    this.tasks.set(id, { at: this.now + Math.max(0, Number(delayMs) || 0), callback, interval, args })
    return id
  }

  async advance(delayMs) {
    const target = this.now + Math.max(0, Number(delayMs) || 0)
    while (true) {
      const next = [...this.tasks.entries()]
        .filter(([, task]) => task.at <= target)
        .sort((left, right) => left[1].at - right[1].at || left[0] - right[0])[0]
      if (!next) break
      const [id, task] = next
      this.now = task.at
      if (task.interval) task.at += task.interval
      else this.tasks.delete(id)
      task.callback(...task.args)
      await flushPromises()
    }
    this.now = target
    await flushPromises()
  }
}

class FakeWebSocket {
  static CONNECTING = 0
  static OPEN = 1
  static CLOSING = 2
  static CLOSED = 3
  static instances = []

  readyState = FakeWebSocket.CONNECTING
  sent = []
  closeCalls = []
  onopen = null
  onmessage = null
  onerror = null
  onclose = null

  constructor(url) {
    this.url = url
    FakeWebSocket.instances.push(this)
  }

  open() {
    this.readyState = FakeWebSocket.OPEN
    this.onopen?.({})
  }

  receive(payload) {
    this.onmessage?.({ data: JSON.stringify(payload) })
  }

  send(payload) { this.sent.push(String(payload)) }

  fail() { this.onerror?.({}) }

  close(code = 1000, reason = '') {
    this.readyState = FakeWebSocket.CLOSING
    this.closeCalls.push({ code, reason })
  }

  emitClose(code = 1006, reason = '') {
    this.readyState = FakeWebSocket.CLOSED
    this.onclose?.({ code, reason })
  }
}

function installBrowserEnvironment(initialStorage = {}) {
  const timers = new FakeTimers()
  const storage = new MemoryStorage(initialStorage)
  const events = []
  globalThis.localStorage = storage
  globalThis.location = { protocol: 'https:', host: 'legion-12.com', hostname: 'legion-12.com' }
  globalThis.window = {
    setTimeout: timers.setTimeout,
    clearTimeout: timers.clearTimeout,
    setInterval: timers.setInterval,
    clearInterval: timers.clearInterval,
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent(event) { events.push(event) },
  }
  globalThis.CustomEvent = class { constructor(type, options) { this.type = type; this.detail = options?.detail } }
  globalThis.__viteEnv = {}
  globalThis.__l12DisconnectCalls = 0
  globalThis.__l12NetState = { endpoint: 'wss://legion-12.com/ws', nickname: '' }
  FakeWebSocket.instances = []
  globalThis.WebSocket = FakeWebSocket
  return { timers, storage, events }
}

async function flushPromises(turns = 12) {
  for (let index = 0; index < turns; index += 1) await Promise.resolve()
}

function compile(source, filename) {
  const result = ts.transpileModule(source, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2022,
      isolatedModules: true,
    },
    fileName: filename,
    reportDiagnostics: true,
  })
  const errors = (result.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error)
  assert.deepEqual(errors, [], `TypeScript transpilation failed for ${filename}`)
  return result.outputText
}

async function importJavaScript(source, label) {
  moduleSerial += 1
  const encoded = Buffer.from(`${source}\n//# sourceURL=${label}-${moduleSerial}.mjs`).toString('base64')
  return import(`data:text/javascript;base64,${encoded}#${moduleSerial}`)
}

// Keep the actual new sidecar in the existing data-URI harness. Only the same
// Vue adapter already used by this fixture is substituted; no chat method is
// stubbed and every existing connection/recovery assertion still executes.
function chatResourceModuleUrl() {
  const filename = join(sourceRoot, 'chatResource.ts')
  const source = readFileSync(filename, 'utf8').replace("import { reactive } from 'vue'", 'const reactive = value => value')
  const output = compile(source, filename)
  moduleSerial += 1
  return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}#chat-${moduleSerial}`
}

function gameStateCardFrameModuleUrl() {
  const filename = join(sourceRoot, 'gameStateCardFrame.ts')
  const output = compile(readFileSync(filename, 'utf8'), filename)
  moduleSerial += 1
  return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}#card-frame-${moduleSerial}`
}

function handArrayDeltaModuleUrl() {
  const filename = join(sourceRoot, 'handArrayDelta.ts')
  const output = compile(readFileSync(filename, 'utf8'), filename)
  moduleSerial += 1
  return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}#hand-array-${moduleSerial}`
}

function deckCoverModuleUrl() {
  const filename = join(sourceRoot, 'deckCover.ts')
  const output = compile(readFileSync(filename, 'utf8'), filename)
  moduleSerial += 1
  return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}#deck-cover-${moduleSerial}`
}

async function loadPlatformModule(base = '/') {
  const filename = join(sourceRoot, 'platform.ts')
  const reliabilityFilename = join(sourceRoot, 'platformRequestReliability.ts')
  const reliabilitySource = compile(readFileSync(reliabilityFilename, 'utf8'), reliabilityFilename)
  let source = readFileSync(filename, 'utf8')
  source = source
    .replace("from './chatResource'", `from '${chatResourceModuleUrl()}'`)
    .replace("from './deckCover'", `from '${deckCoverModuleUrl()}'`)
    .replace("import { computed, reactive } from 'vue'", `
      const reactive = value => value
      const computed = getter => ({ get value() { return getter() } })
    `)
    .replace("import { disconnect, l12State, type BugClientConnectionDiagnostic } from './net'", `
      const l12State = globalThis.__l12NetState
      const disconnect = () => { globalThis.__l12DisconnectCalls += 1 }
    `)
    .replace("from './deploymentBase'", `from '${deploymentSourceModuleUrl(base)}'`)
  source = source.replace(
    "import { createRequestCoordinator, RequestDeadlineError } from './platformRequestReliability'",
    reliabilitySource,
  )
  return importJavaScript(compile(source, filename), 'l12-platform-recovery-test')
}

async function loadNetModule(base = '/') {
  const filename = join(sourceRoot, 'net.ts')
  let source = readFileSync(filename, 'utf8')
  source = source
    .replace("from './chatResource'", `from '${chatResourceModuleUrl()}'`)
    .replace("from './gameStateCardFrame'", `from '${gameStateCardFrameModuleUrl()}'`)
    .replace("from './handArrayDelta'", `from '${handArrayDeltaModuleUrl()}'`)
    .replace("from './gameStatePropertyFrame'", `from '${propertyFrameSourceModuleUrl()}'`)
    .replace("import { reactive } from 'vue'", 'const reactive = value => value')
    .replace("import { createGameReentryController } from './gameReentry'", `
      const createGameReentryController = () => ({
        update: async () => {},
        requestExit: async () => {},
        dispose: () => {},
      })
    `)
    .replace("from './deploymentBase'", `from '${deploymentSourceModuleUrl(base)}'`)
    .replaceAll('import.meta.env.VITE_WS_URL', 'globalThis.__viteEnv.VITE_WS_URL')
  return importJavaScript(compile(source, filename), 'l12-net-recovery-test')
}

async function loadGamePageRetryModule(harness) {
  const filename = join(sourceRoot, 'GamePage.vue')
  const source = readFileSync(filename, 'utf8')
  const match = source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)
  assert.ok(match, 'GamePage must keep a script setup block')
  const parsed = ts.createSourceFile(filename, match[1], ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const retry = parsed.statements.find(statement => ts.isFunctionDeclaration(statement)
    && statement.name?.text === 'retryGameLoad')
  assert.ok(retry, 'GamePage must keep retryGameLoad as a named source function')
  globalThis.__l12GamePageRetryHarness = harness
  return importJavaScript(compile(`
    const { l12State, connect, send, WebSocket } = globalThis.__l12GamePageRetryHarness
    const loadRetrying = { value: false }
    ${retry.getText(parsed)}
    export { loadRetrying, retryGameLoad }
  `, filename), 'l12-game-page-retry-test')
}

async function loadRouteGuard(platform) {
  const filename = join(frontendRoot, 'src', 'router', 'index.ts')
  const source = readFileSync(filename, 'utf8')
  const body = source.slice(source.indexOf('router.beforeEach('))
  globalThis.__l12GuardPlatform = platform
  globalThis.__l12Guard = null
  await importJavaScript(compile(`
    const { canAccessAdmin, platformState, authState, refreshCurrentAccount } = globalThis.__l12GuardPlatform
    const beginRouteNavigation = () => {}
    const router = { beforeEach: guard => { globalThis.__l12Guard = guard } }
    ${body}
  `, filename), 'l12-route-guard')
  assert.equal(typeof globalThis.__l12Guard, 'function')
  return globalThis.__l12Guard
}

function accountFixture() {
  return { id: 'account-1', username: '测试玩家', role: 'player', createdAt: '2026-09-07T00:00:00Z', publicHistory: false }
}

function response(status, payload = {}) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Correlation-ID': `test-${status}` },
  })
}

function installFetchPlan(plan) {
  const calls = []
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), authorization: new Headers(init?.headers).get('Authorization') })
    const operation = plan.shift()
    assert.ok(operation, `unexpected fetch ${url}`)
    return operation({ url: String(url), init })
  }
  return calls
}

async function promiseOutcome(promise, timeoutMs = 25) {
  return Promise.race([
    promise.then(value => ({ status: 'fulfilled', value }), error => ({ status: 'rejected', error })),
    delay(timeoutMs).then(() => ({ status: 'pending' })),
  ])
}

function sendSuccessfulHandshake(socket, generation) {
  socket.open()
  socket.receive({ type: 'session', sessionId: `session-${generation}`, name: '测试玩家', connectionGeneration: generation })
  socket.receive({ type: 'effectiveOperationsPolicy', policy: { maintenance: { active: false } } })
  socket.receive({ type: 'recoveryComplete', connectionGeneration: generation })
}

function presenceItem(accountId, username, activity = 'idle') {
  return { accountId, username, online: true, activity, roomCode: null, canInvite: true,
    canSpectate: false, actionReason: null, friendStatus: 'none', friendDirection: 'none' }
}

const tests = [
  ['mounted test never starts or diagnoses with a legacy formal login', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'formal-only', 'l12-nickname': 'formal-name' })
    const net = await loadNetModule('/testrun/')
    assert.equal(net.l12State.nickname, '')
    net.startAutomaticConnection()
    assert.equal(FakeWebSocket.instances.length, 0)
    assert.equal((await promiseOutcome(net.connect())).status, 'rejected')
    assert.equal(FakeWebSocket.instances.length, 0)
    const calls = []
    globalThis.fetch = async (url, init) => {
      calls.push({ url, token: init.headers.get('Authorization') })
      return new Response(JSON.stringify({ status: 'ok' }), { status: 200 })
    }
    const diagnostic = await net.captureBugClientDiagnostic('/testrun/profile')
    assert.equal(diagnostic.authenticationState, 'missing-token')
    assert.equal(calls.length, 2)
    assert.ok(calls.every(call => call.url.startsWith('https://legion-12.com/testrun/')))
    assert.ok(calls.every(call => call.token === null))
    net.disconnect()
  }],
  ['actual HTTP auth and WebSocket reconnect keep mounted token across formal login changes', async () => {
    const { timers, storage } = installBrowserEnvironment({ 'l12-auth-token': 'formal-token' })
    const net = await loadNetModule('/testrun/')
    globalThis.__l12NetState = net.l12State
    const platform = await loadPlatformModule('/testrun/')
    const calls = []
    const testAccount = { id: 'mounted-test', username: 'test-name', role: 'player' }
    globalThis.fetch = async (url, init) => {
      calls.push({ url: String(url), token: init.headers.get('Authorization') })
      const body = String(url).endsWith('/api/auth/login')
        ? { account: testAccount, token: 'mounted-token' } : testAccount
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }
    await platform.login('synthetic-test', 'not-a-real-password')
    await platform.refreshCurrentAccount({ force: true })
    assert.deepEqual(calls, [
      { url: 'https://legion-12.com/testrun/api/auth/login', token: null },
      { url: 'https://legion-12.com/testrun/api/auth/me', token: 'Bearer mounted-token' },
    ])
    assert.equal(storage.getItem('l12-auth-token'), 'formal-token')
    const connected = net.connect(), first = FakeWebSocket.instances[0]
    assert.equal(first.url, 'wss://legion-12.com/testrun/ws')
    sendSuccessfulHandshake(first, 201)
    await connected
    assert.equal(JSON.parse(first.sent[0]).authToken, 'mounted-token')
    storage.setItem('l12-auth-token', 'formal-changed-while-test-open')
    first.emitClose(1006, 'synthetic reconnect')
    await timers.advance(1000)
    const replacement = FakeWebSocket.instances[1]
    sendSuccessfulHandshake(replacement, 202)
    await flushPromises()
    assert.equal(JSON.parse(replacement.sent[0]).authToken, 'mounted-token')
    assert.equal(net.l12State.status, 'online')
    assert.equal(storage.getItem('l12-auth-token'), 'formal-changed-while-test-open')
    net.disconnect()
  }],
  ['referee correction recovers its receipt after the finished table is no longer recoverable', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'referee-token' })
    const net = await loadNetModule(), connected = net.connect(), first = FakeWebSocket.instances[0]
    first.open()
    first.receive({ type: 'session', sessionId: 'referee-first', connectionGeneration: 101,
      capabilities: { requestIds: true, deltaGameState: true } })
    first.receive({ type: 'roomState', roomCode: 'REF001', started: true, yourPlayerIndex: null, players: [] })
    first.receive({ type: 'gameState', spectating: true, observerView: 'referee', gmEnabled: false,
      refereeControlsEnabled: true, tournamentId: 'tournament', tournamentMatchId: 'table',
      rankedClock: { paused: true }, state: { matchId: 'referee-match', roomCode: 'REF001', revision: 1, phase: 'Main', players: [] } })
    first.receive({ type: 'recoveryComplete', connectionGeneration: 101, roomCode: 'REF001', matchId: 'referee-match', recoveryRevision: 1 })
    await connected
    assert.equal(net.l12State.gmEnabled, false, 'referee must not receive sandbox capability')
    net.refereeGmAction({ type: 'setLife', targetPlayer: 0, value: 18, refereeReason: 'synthetic correction' })
    const firstCorrection = first.sent.map(value => JSON.parse(value)).find(value => value.type === 'gmAction')
    assert.equal(net.l12State.pendingAction, true)
    first.receive({ type: 'gameState', requestId: firstCorrection.requestId, spectating: true, observerView: 'referee', gmEnabled: false,
      refereeControlsEnabled: true, tournamentId: 'tournament', tournamentMatchId: 'table', rankedClock: { paused: true },
      state: { matchId: 'referee-match', roomCode: 'REF001', revision: 2, phase: 'Main', players: [] } })
    assert.equal(net.l12State.pendingAction, false, 'the normal spectator game-state acknowledgement must unlock the next correction')
    net.refereeGmAction({ type: 'setLife', targetPlayer: 0, value: 0, refereeReason: 'synthetic ruling' })
    const original = first.sent.map(value => JSON.parse(value)).filter(value => value.type === 'gmAction').at(-1)
    assert.equal(original.command.refereeMatchId, 'referee-match')
    first.emitClose(1006, 'confirmation lost')
    assert.equal(net.l12State.refereeControlsEnabled, false)
    assert.equal(net.l12State.pendingAction, true)
    await timers.advance(1_000)
    const replacement = FakeWebSocket.instances[1]
    replacement.open()
    replacement.receive({ type: 'session', sessionId: 'referee-next', connectionGeneration: 102,
      capabilities: { requestIds: true, deltaGameState: true } })
    replacement.receive({ type: 'recoveryComplete', connectionGeneration: 102 })
    const retried = replacement.sent.map(value => JSON.parse(value)).find(value => value.type === 'gmAction')
    assert.deepEqual(retried, original, 'missing completed table must only retry the exact receipt identity')
    assert.equal(net.l12State.pendingAction, true)
    replacement.receive({ type: 'refereeActionReceipt', requestId: original.requestId, accepted: true, revision: 2 })
    assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.game, null, 'receipt recovery must not fabricate or expose a privileged snapshot')
    net.disconnect()
  }],
  ['spectator matchmaking retires the old observation before accepting the new player snapshot', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'spectator-queue-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 111)
    await connected
    const oldState = { matchId: 'observed-old', roomCode: 'WATCH1', revision: 50,
      phase: 'Main', players: [], recentEvents: [], lastAction: null }
    socket.receive({ type: 'roomState', roomCode: 'WATCH1', started: true, yourPlayerIndex: null, players: [] })
    socket.receive({ type: 'gameState', spectating: true, state: oldState })
    net.joinMatchmaking('casual')
    socket.receive({ type: 'matchmakingState', queued: true, mode: 'casual' })
    net.cancelMatchmaking()
    socket.receive({ type: 'matchmakingState', queued: false })
    assert.equal(net.l12State.game.matchId, 'observed-old', 'cancel only the queue, not observation')
    socket.receive({ type: 'matchmakingState', queued: true, mode: 'casual' })
    socket.receive({ type: 'matchmakingFound', mode: 'casual', roomCode: 'OWN111', matchId: 'own-new',
      retiredObservationRoomCode: 'WATCH1', retiredObservationMatchId: 'observed-old' })
    assert.equal(net.l12State.game, null, 'the old game cannot satisfy matched-state recovery')
    assert.equal(net.l12State.spectating, false)
    const newState = { ...oldState, matchId: 'own-new', roomCode: 'OWN111', revision: 1 }
    socket.receive({ type: 'roomState', roomCode: 'OWN111', started: true, yourPlayerIndex: 1, players: [] })
    socket.receive({ type: 'gameState', spectating: false, state: newState })
    const sentBefore = socket.sent.length
    for (const packet of [
      { type: 'roomState', roomCode: 'WATCH1', started: false, players: [] },
      { type: 'gameState', spectating: true, state: { ...oldState, revision: 51 } },
      { type: 'gameStateDelta', matchId: 'observed-old', baseRevision: 50, revision: 51, changes: [] },
      { type: 'gameStateCardFrameV1', payload: { type: 'gameState', state: oldState } },
      { type: 'gameStatePropertyFrameV1', version: 1, matchId: 'observed-old', payload: { '@bad': true } },
      { type: 'roomClosed', roomCode: 'WATCH1' }, { type: 'roomLeft', roomCode: 'WATCH1' },
    ]) socket.receive(packet)
    assert.equal(net.l12State.game.matchId, 'own-new')
    assert.equal(net.l12State.room.roomCode, 'OWN111')
    assert.equal(net.l12State.spectating, false)
    assert.equal(socket.sent.length, sentBefore, 'retired frames must not start resynchronization')
    socket.receive({ type: 'gameStateDelta', matchId: 'own-new', baseRevision: 1, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 }] })
    assert.equal(net.l12State.game.revision, 2, 'the new full snapshot owns the delta baseline')
    net.leaveRoom()
    socket.receive({ type: 'roomLeft', roomCode: 'OWN111' })
    net.spectateRoom('WATCH1')
    socket.receive({ type: 'roomState', roomCode: 'WATCH1', started: true, yourPlayerIndex: null, players: [] })
    socket.receive({ type: 'gameState', spectating: true, state: oldState })
    assert.equal(net.l12State.game.matchId, 'observed-old', 'a later explicit observation is allowed after leaving the player room')
    net.disconnect()
  }],
  ['cold spectator handover rejects retired frames and still recovers a missing matched snapshot', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'spectator-cold-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 112)
    await connected
    socket.receive({ type: 'matchmakingFound', mode: 'casual', roomCode: 'OWN112', matchId: 'cold-new',
      retiredObservationRoomCode: 'WATCH2', retiredObservationMatchId: 'cold-old' })
    socket.receive({ type: 'gameState', spectating: true,
      state: { matchId: 'cold-old', roomCode: 'WATCH2', revision: 90, phase: 'Main', players: [] } })
    assert.equal(net.l12State.game, null)
    await timers.advance(800)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, 1)
    socket.receive({ type: 'roomState', roomCode: 'OWN112', started: true, yourPlayerIndex: 0, players: [] })
    socket.receive({ type: 'gameState', state: { matchId: 'cold-new', roomCode: 'OWN112', revision: 1,
      phase: 'Main', players: [], recentEvents: [], lastAction: null } })
    assert.equal(net.l12State.game.matchId, 'cold-new')
    assert.equal(net.l12State.matchFound, null)
    net.disconnect()
  }],
  ['replacement session retains the retired observation fence for the same account', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'spectator-reconnect-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'observer-114', accountId: 'observer-account', connectionGeneration: 114 })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 114 })
    await connected
    socket.receive({ type: 'matchmakingFound', mode: 'casual', roomCode: 'OWN114', matchId: 'reconnected-new',
      retiredObservationRoomCode: 'WATCH4', retiredObservationMatchId: 'reconnected-old' })
    socket.emitClose(1006, 'reconnect before matched snapshot')
    await timers.advance(1000)
    const replacement = FakeWebSocket.instances[1]
    replacement.open()
    replacement.receive({ type: 'session', sessionId: 'observer-115', accountId: 'observer-account', connectionGeneration: 115, recovered: true })
    replacement.receive({ type: 'gameState', spectating: true, state: {
      matchId: 'reconnected-old', roomCode: 'WATCH4', revision: 99, phase: 'Main', players: [] } })
    assert.equal(net.l12State.game, null)
    assert.equal(net.l12State.matchFound.matchId, 'reconnected-new')
    await timers.advance(700)
    replacement.receive({ type: 'matchmakingFound', mode: 'casual', roomCode: 'OWN114', matchId: 'reconnected-new',
      retiredObservationRoomCode: 'WATCH4', retiredObservationMatchId: 'reconnected-old' })
    await timers.advance(100)
    assert.equal(replacement.sent.filter(item => JSON.parse(item).type === 'syncState').length, 1,
      'replacement session must restart the missing matched snapshot grace without waiting for another found')
    replacement.receive({ type: 'roomState', roomCode: 'OWN114', started: true, yourPlayerIndex: 0, players: [] })
    replacement.receive({ type: 'gameState', state: {
      matchId: 'reconnected-new', roomCode: 'OWN114', revision: 1, phase: 'Main', players: [] } })
    replacement.receive({ type: 'recoveryComplete', connectionGeneration: 115, roomCode: 'OWN114', matchId: 'reconnected-new', recoveryRevision: 1 })
    assert.equal(net.l12State.game.matchId, 'reconnected-new')
    assert.equal(net.l12State.spectating, false)
    net.disconnect()
  }],
  ['authoritative lobby recovery retires the observation fence before a later explicit spectate', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'spectator-lobby-recovery-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'observer-117', accountId: 'observer-account', connectionGeneration: 117 })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 117 })
    await connected
    socket.receive({ type: 'matchmakingFound', mode: 'casual', roomCode: 'OWN117', matchId: 'finished-new',
      retiredObservationRoomCode: 'WATCH7', retiredObservationMatchId: 'returned-old' })
    socket.emitClose(1006, 'player room ended before reconnect')
    await timers.advance(1000)
    const replacement = FakeWebSocket.instances[1]
    replacement.open()
    replacement.receive({ type: 'session', sessionId: 'observer-118', accountId: 'observer-account', connectionGeneration: 118, recovered: true })
    replacement.receive({ type: 'recoveryComplete', connectionGeneration: 118 })
    assert.equal(net.l12State.matchFound, null)
    assert.equal(net.l12State.game, null)
    net.spectateRoom('WATCH7')
    replacement.receive({ type: 'roomState', roomCode: 'WATCH7', started: true, yourPlayerIndex: null, players: [] })
    replacement.receive({ type: 'gameState', spectating: true, state: {
      matchId: 'returned-old', roomCode: 'WATCH7', revision: 101, phase: 'Main', players: [] } })
    assert.equal(net.l12State.room.roomCode, 'WATCH7')
    assert.equal(net.l12State.game.matchId, 'returned-old')
    assert.equal(net.l12State.spectating, true)
    net.disconnect()
  }],
  ['failed pairing requeue notification restores visible queue and polling without leaving observation', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'spectator-rollback-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 116)
    await connected
    socket.receive({ type: 'roomState', roomCode: 'WATCH5', started: true, players: [] })
    socket.receive({ type: 'gameState', spectating: true, state: {
      matchId: 'rollback-observation', roomCode: 'WATCH5', revision: 1, phase: 'Main', players: [] } })
    socket.receive({ type: 'matchmakingState', queued: true, mode: 'casual' })
    socket.receive({ type: 'matchmakingRejected', message: '建立匹配房间失败，已恢复队列' })
    socket.receive({ type: 'matchmakingState', queued: true, mode: 'casual', observationResyncRequired: true,
      message: '建立对局失败，已恢复匹配队列' })
    assert.equal(net.l12State.matchmaking.queued, true)
    assert.equal(net.l12State.game.matchId, 'rollback-observation')
    assert.equal(net.l12State.spectating, true)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, 1,
      'restored observation must request a full baseline for any facts missed during durable creation')
    await timers.advance(5_000)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'pollMatchmaking').length, 1)
    net.disconnect()
  }],
  ['leaving ordinary observation retains the queued match and polling', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'spectator-exit-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 113)
    await connected
    socket.receive({ type: 'matchmakingState', queued: true, mode: 'casual' })
    socket.receive({ type: 'roomState', roomCode: 'WATCH3', started: true, players: [] })
    socket.receive({ type: 'gameState', spectating: true,
      state: { matchId: 'watch-exit', roomCode: 'WATCH3', revision: 1, phase: 'Main', players: [] } })
    net.leaveRoom()
    socket.receive({ type: 'roomLeft', roomCode: 'WATCH3' })
    assert.equal(net.l12State.game, null)
    assert.equal(net.l12State.matchmaking.queued, true)
    await timers.advance(5_000)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'pollMatchmaking').length, 1)
    net.disconnect()
  }],
  ['property frames preserve the actual full/delta/card-frame and reliable acknowledgement path', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'property-frame-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'property-session', connectionGeneration: 188,
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true,
        gameStateCardFrameV1: true, gameStatePropertyFrameV1: true } })
    socket.receive({ type: 'roomState', roomCode: 'PROP', yourPlayerIndex: 0, started: true, players: [] })
    const state = { matchId: 'property-match', roomCode: 'PROP', revision: 1, stateHash: 'h1',
      phase: 'Main', players: [], recentEvents: [], lastAction: null }
    socket.receive(propertyPacket({ type: 'gameState', state, rankedClock: { marker: 'full' } }))
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 188,
      roomCode: 'PROP', matchId: 'property-match', recoveryRevision: 1 })
    await connected
    assert.equal(net.l12State.game.revision, 1); assert.equal(net.l12State.rankedClock.marker, 'full')
    net.gameAction({ type: 'endTurn' })
    const action = socket.sent.map(JSON.parse).findLast(value => value.type === 'gameAction')
    socket.receive(propertyPacket({ type: 'gameStateDelta', matchId: 'property-match', baseRevision: 1, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 }, { path: ['state', 'stateHash'], value: 'h2' },
        { path: ['requestId'], value: action.requestId }, { path: ['rankedClock'], value: { marker: 'delta' } }] }))
    assert.equal(net.l12State.game.revision, 2); assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.rankedClock.marker, 'delta')
    socket.receive(propertyPacket({ type: 'gameStateCardFrameV1', payload: { type: 'gameState', state: {
      ...state, revision: 3, phase: 'GameOver', recentEvents: [{ sequence: 1, cards: null }], lastAction: null } },
      cardSnapshots: [{ name: '合成军团' }], eventCardRefs: [[0]], lastActionCardRefs: null }))
    assert.equal(net.l12State.game.phase, 'GameOver')
    assert.equal(net.l12State.game.recentEvents[0].cards[0].name, '合成军团')
    assert.equal(socket.sent.map(JSON.parse).filter(value => value.type === 'syncState').length, 0)
    net.disconnect()
  }],
  ['a property wrapper above two MiB preserves a bounded inner card frame through actual net dispatch', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'property-large-synthetic' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'property-large', connectionGeneration: 191,
      capabilities: { gameStateCardFrameV1: true, gameStatePropertyFrameV1: true } })
    const state = { matchId: 'b'.repeat(200), roomCode: 'BOUND', revision: 1, phase: 'Main', players: [],
      recentEvents: [], lastAction: null, syntheticPadding: '' }
    socket.receive({ type: 'roomState', roomCode: 'BOUND', yourPlayerIndex: 0, started: true, players: [] })
    const inner = { type: 'gameStateCardFrameV1', payload: { type: 'gameState', state },
      cardSnapshots: [], eventCardRefs: [], lastActionCardRefs: null }
    state.syntheticPadding = 'p'.repeat(2 * 1024 * 1024 - 16 - Buffer.byteLength(JSON.stringify(inner)))
    assert(Buffer.byteLength(JSON.stringify(inner)) <= 2 * 1024 * 1024)
    const packet = propertyPacket(inner)
    assert(Buffer.byteLength(JSON.stringify(packet)) > 2 * 1024 * 1024)
    socket.receive(packet)
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 191,
      roomCode: 'BOUND', matchId: state.matchId, recoveryRevision: 1 })
    await connected
    assert.equal(net.l12State.game.syntheticPadding.length, state.syntheticPadding.length)
    assert.equal(socket.sent.map(JSON.parse).filter(message => message.type === 'syncState').length, 0)
    net.disconnect()
  }],
  ['malformed property frames share bounded plain recovery without losing the pending request', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'property-bad-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'property-bad-session', connectionGeneration: 189,
      capabilities: { requestIds: true, gameStateCardFrameV1: true, gameStatePropertyFrameV1: true } })
    socket.receive({ type: 'roomState', roomCode: 'PFAIL', yourPlayerIndex: 0, started: true, players: [] })
    const state = { matchId: 'property-bad', roomCode: 'PFAIL', revision: 1, phase: 'Main', players: [] }
    socket.receive({ type: 'gameState', state })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 189, roomCode: 'PFAIL', matchId: 'property-bad', recoveryRevision: 1 })
    await connected
    net.gameAction({ type: 'endTurn' })
    const action = socket.sent.map(JSON.parse).findLast(value => value.type === 'gameAction')
    const broken = propertyPacket({ type: 'gameState', requestId: action.requestId, state: { ...state, phase: 'GameOver' } })
    broken.payload['@unknown'] = true
    socket.receive(broken); socket.receive(broken)
    assert.equal(net.l12State.game.phase, 'Main'); assert.equal(net.l12State.pendingAction, true)
    assert.equal(socket.sent.map(JSON.parse).filter(value => value.type === 'syncState' && value.plainFull === true).length, 1)
    socket.receive({ type: 'gameState', state, recovered: true })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 189, roomCode: 'PFAIL', matchId: 'property-bad', recoveryRevision: 1 })
    const retried = socket.sent.map(JSON.parse).filter(value => value.type === 'gameAction')
    assert.equal(retried.length, 2); assert.deepEqual(retried[1], action)
    socket.receive({ type: 'gameState', requestId: action.requestId, state: { ...state, revision: 2 } })
    assert.equal(net.l12State.pendingAction, false)
    net.disconnect()
  }],
  ['unnegotiated property frames never publish even when their contents are well formed', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'property-old-server-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 190); await connected
    socket.receive(propertyPacket({ type: 'gameState', state: { matchId: 'unnegotiated', revision: 1, phase: 'Main', players: [] } }))
    assert.equal(net.l12State.game, null)
    assert.equal(socket.sent.map(JSON.parse).filter(value => value.type === 'syncState' && value.plainFull === true).length, 1)
    net.disconnect()
  }],
  ['an in-flight delta cannot satisfy malformed card-frame plain recovery', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'card-frame-delta-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'card-frame-delta-session', connectionGeneration: 87,
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true,
        gameStateCardFrameV1: true } })
    socket.receive({ type: 'roomState', roomCode: 'FRAME2', yourPlayerIndex: 0, started: true, players: [] })
    socket.receive({ type: 'gameState', state: { matchId: 'card-frame-delta-match', roomCode: 'FRAME2',
      revision: 1, stateHash: 'card-frame-delta-1', phase: 'Main', players: [], recentEvents: [], lastAction: null } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 87,
      roomCode: 'FRAME2', matchId: 'card-frame-delta-match', recoveryRevision: 1 })
    await connected

    const malformed = { type: 'gameStateCardFrameV1',
      payload: { type: 'gameState', state: { matchId: 'card-frame-delta-match', roomCode: 'FRAME2',
        revision: 2, stateHash: 'card-frame-delta-2', phase: 'Main', players: [],
        recentEvents: [{ sequence: 1, cards: null }], lastAction: null } },
      eventCardRefs: [[1]], lastActionCardRefs: null,
      cardSnapshots: [{ instanceId: 'only-synthetic-card' }] }
    socket.receive(malformed)
    assert.equal(socket.sent.map(item => JSON.parse(item)).filter(item => item.type === 'syncState' && item.plainFull === true).length, 1)
    socket.receive({ type: 'gameStateDelta', matchId: 'card-frame-delta-match', baseRevision: 1, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 },
        { path: ['state', 'stateHash'], value: 'card-frame-delta-2' }] })
    assert.equal(net.l12State.game.revision, 2, 'the valid in-flight delta may still advance last-good state')
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 87,
      roomCode: 'FRAME2', matchId: 'card-frame-delta-match', recoveryRevision: 2 })
    assert.equal(socket.closeCalls.at(-1)?.code, 4000,
      'recoveryComplete without an original plain gameState must replace the connection')
    assert.equal(net.l12State.status, 'offline')
    net.disconnect()
  }],
  ['malformed card frame recovers through the normal gameState path without dropping outer metadata', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'card-frame-recovery-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'card-frame-session', connectionGeneration: 86,
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true,
        gameStateCardFrameV1: true } })
    socket.receive({ type: 'roomState', roomCode: 'FRAME1', yourPlayerIndex: 0, started: true, players: [] })
    const state = { matchId: 'card-frame-match', roomCode: 'FRAME1', revision: 7,
      stateHash: 'card-frame-state-7', phase: 'Main', players: [], recentEvents: [], lastAction: null }
    socket.receive({ type: 'gameState', state, rankedClock: { marker: 'initial-clock' },
      rankedSettlement: { marker: 'initial-settlement' },
      responsePreference: { confirmedMode: 'default', syncPending: false },
      spectating: false, gmEnabled: false })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 86,
      roomCode: 'FRAME1', matchId: 'card-frame-match', recoveryRevision: 7 })
    await connected

    net.gameAction({ type: 'endTurn' })
    const originalAction = socket.sent.map(item => JSON.parse(item)).findLast(item => item.type === 'gameAction')
    assert.ok(originalAction?.requestId)
    const malformed = { type: 'gameStateCardFrameV1',
      payload: { type: 'gameState', requestId: originalAction.requestId,
        state: { ...state, phase: 'GameOver', recentEvents: [{ sequence: 1, cards: null }] },
        rankedClock: { marker: 'must-not-publish' }, rankedSettlement: { marker: 'must-not-publish' } },
      eventCardRefs: [[1]], lastActionCardRefs: null,
      cardSnapshots: [{ instanceId: 'only-synthetic-card' }] }
    socket.receive(malformed)
    socket.receive(malformed)
    assert.equal(socket.sent.map(item => JSON.parse(item)).filter(item => item.type === 'syncState' && item.plainFull === true).length, 1)
    assert.equal(net.l12State.pendingAction, true, 'a malformed packed acknowledgement must not clear the request')
    assert.equal(net.l12State.game.phase, 'Main', 'a malformed packed frame must not publish partial state')
    assert.equal(net.l12State.rankedClock.marker, 'initial-clock', 'a malformed frame must retain last-good metadata')

    socket.receive({ type: 'gameState', state, recovered: true,
      rankedClock: { marker: 'plain-clock' }, rankedSettlement: { marker: 'plain-settlement' },
      responsePreference: { confirmedMode: 'manual', syncPending: true },
      spectating: false, gmEnabled: false })
    assert.equal(net.l12State.pendingAction, true, 'the plain sync frame is not the same-id action acknowledgement')
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 86,
      roomCode: 'FRAME1', matchId: 'card-frame-match', recoveryRevision: 7 })
    const sentActions = socket.sent.map(item => JSON.parse(item)).filter(item => item.type === 'gameAction')
    assert.equal(sentActions.length, 2, 'recovery must resend the original action exactly once on this attempt')
    assert.deepEqual(sentActions[1], originalAction, 'recovery must retain the original request id and command')

    socket.receive({ type: 'gameState', requestId: originalAction.requestId, state,
      rankedClock: { marker: 'ack-clock' }, rankedSettlement: { marker: 'ack-settlement' },
      responsePreference: { confirmedMode: 'auto-pass', syncPending: false },
      spectating: true, observerView: 'referee', gmEnabled: true })
    assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.rankedClock.marker, 'ack-clock')
    assert.equal(net.l12State.rankedSettlement.marker, 'ack-settlement')
    assert.deepEqual(net.l12State.responsePreference, { confirmedMode: 'auto-pass', syncPending: false })
    assert.equal(net.l12State.spectating, true)
    assert.equal(net.l12State.observerView, 'referee')
    assert.equal(net.l12State.gmEnabled, true)

    socket.receive({ type: 'gameStateDelta', matchId: 'card-frame-match', baseRevision: 7, revision: 8,
      changes: [{ path: ['state', 'revision'], value: 8 },
        { path: ['state', 'stateHash'], value: 'card-frame-state-8' }] })
    assert.equal(net.l12State.game.revision, 8)
    assert.equal(net.l12State.rankedClock.marker, 'ack-clock', 'delta must rebuild from the complete same-id ACK baseline')
    assert.equal(net.l12State.rankedSettlement.marker, 'ack-settlement')
    assert.deepEqual(net.l12State.responsePreference, { confirmedMode: 'auto-pass', syncPending: false })
    assert.equal(net.l12State.spectating, true)
    assert.equal(net.l12State.gmEnabled, true)
    net.disconnect()
  }],
  ['event window failure keeps private state atomic and requests full sync', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'window-token' })
    const net = await loadNetModule(), connected = net.connect(), socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'window-session', connectionGeneration: 83,
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true } })
    socket.receive({ type: 'roomState', roomCode: 'WINDOW', yourPlayerIndex: 0, started: true, players: [] })
    const event = sequence => ({ sequence, type: 'move', cards: [{ instanceId: `frozen-${sequence}`, effectText: '旧文本', tapped: false }] })
    socket.receive({ type: 'gameState', state: { matchId: 'window-match', roomCode: 'WINDOW', revision: 1, phase: 'Main',
      players: [{ hand: ['private'] }, { handCount: 1 }], recentEvents: [1, 4, 8].map(event), lastAction: null } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 83, roomCode: 'WINDOW', matchId: 'window-match', recoveryRevision: 1 })
    await connected
    socket.receive({ type: 'gameStateDelta', matchId: 'window-match', baseRevision: 1, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 }],
      eventWindow: { baseSequences: [1, 4, 8], removeFirst: 1, append: [event(12)], resultSequences: [4, 8, 12] } })
    assert.deepEqual(net.l12State.game.recentEvents, [4, 8, 12].map(event))
    assert.deepEqual(net.l12State.game.players[0].hand, ['private'])
    assert.equal(net.l12State.game.lastAction, null)
    const prior = JSON.stringify(net.l12State.game), syncBefore = socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    socket.receive({ type: 'gameStateDelta', matchId: 'window-match', baseRevision: 2, revision: 3,
      changes: [{ path: ['state', 'revision'], value: 3 }],
      eventWindow: { baseSequences: [1, 4, 8], removeFirst: 1, append: [event(16)], resultSequences: [8, 12, 16] } })
    assert.equal(JSON.stringify(net.l12State.game), prior)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, syncBefore + 1)
    net.disconnect()
  }],
  ['event window support is not inherited by an unnegotiated replacement', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'window-old-server' })
    const net = await loadNetModule(), connected = net.connect(), first = FakeWebSocket.instances[0]
    first.open()
    first.receive({ type: 'session', sessionId: 'window-old-1', connectionGeneration: 84,
      capabilities: { deltaGameState: true, eventWindowDeltaV1: true } })
    first.receive({ type: 'recoveryComplete', connectionGeneration: 84 })
    await connected
    first.emitClose(1006, 'reconnect')
    await timers.advance(1000)
    const replacement = FakeWebSocket.instances[1]
    replacement.open()
    replacement.receive({ type: 'session', sessionId: 'window-old-2', connectionGeneration: 85, capabilities: { deltaGameState: true } })
    replacement.receive({ type: 'gameState', state: { matchId: 'window-old-match', revision: 1, phase: 'Main', players: [], recentEvents: [{ sequence: 1, text: 'frozen' }] } })
    replacement.receive({ type: 'recoveryComplete', connectionGeneration: 85 })
    const prior = JSON.stringify(net.l12State.game)
    replacement.receive({ type: 'gameStateDelta', matchId: 'window-old-match', baseRevision: 1, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 }],
      eventWindow: { baseSequences: [1], removeFirst: 0, append: [{ sequence: 2 }], resultSequences: [1, 2] } })
    assert.equal(JSON.stringify(net.l12State.game), prior)
    assert.equal(net.l12State.recoveryPhase, 'snapshot-mismatch')
    net.disconnect()
  }],
  ['a half-open socket with no server replies is retried without requiring a close event', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'heartbeat-token' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 51)
    await pending
    await timers.advance(75_000)
    assert.equal(socket.closeCalls.at(-1)?.code, 4000)
    assert.equal(net.l12State.connectionIssue, 'websocket')
    await timers.advance(1000)
    assert.equal(FakeWebSocket.instances.length, 2)
    net.disconnect()
  }],
  ['server replies keep heartbeat alive and an explicitly superseded socket never reconnects', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'heartbeat-alive' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 52)
    await pending
    for (let i = 0; i < 4; i++) {
      await timers.advance(25_000)
      socket.receive({ type: 'pong' })
    }
    assert.equal(socket.closeCalls.length, 0)
    socket.receive({ type: 'sessionSuperseded' })
    socket.emitClose(4002, 'session superseded')
    await timers.advance(80_000)
    assert.equal(FakeWebSocket.instances.length, 1)
    net.disconnect()
  }],
  ['incomplete recovery acknowledgements back off instead of flooding full snapshots', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'backoff-token' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'backoff-session', connectionGeneration: 41 })
    const ack = { type: 'recoveryComplete', connectionGeneration: 41, roomCode: 'MISSING' }
    for (let i = 0; i < 100; i++) socket.receive(ack)
    const count = () => socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    assert.equal(count(), 1, 'repeated bad acks must not amplify memory pressure')
    await timers.advance(999)
    assert.equal(count(), 1)
    await timers.advance(1)
    assert.equal(count(), 2)
    socket.receive({ type: 'roomState', roomCode: 'MISSING', yourPlayerIndex: 0, players: [], started: false })
    socket.receive(ack)
    await pending
    await timers.advance(8_000)
    assert.equal(count(), 2, 'a valid snapshot must cancel pending retries')
    net.disconnect()
  }],
  ['queued matchmaking state suppresses eager sync while a missing initial snapshot recovers after grace', async () => {
    {
      const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'match-initial-token' })
      const net = await loadNetModule()
      const connected = net.connect()
      const socket = FakeWebSocket.instances[0]
      sendSuccessfulHandshake(socket, 42)
      await connected
      const syncCount = () => socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
      socket.receive({ type: 'matchmakingFound', mode: 'ranked', roomCode: 'MATCH1', matchId: 'match-one' })
      assert.equal(syncCount(), 0, 'the server-queued initial room/game batch must not race an eager sync')
      await timers.advance(799)
      assert.equal(syncCount(), 0)
      socket.receive({ type: 'roomState', roomCode: 'MATCH1', yourPlayerIndex: 0, started: true, players: [] })
      socket.receive({ type: 'gameState', state: {
        matchId: 'match-one', roomCode: 'MATCH1', revision: 1, phase: 'Main', players: [],
      } })
      await timers.advance(1)
      assert.equal(syncCount(), 0, 'the queued initial game state must cancel the grace recovery')
      net.disconnect()
    }

    {
      const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'match-missing-token' })
      const net = await loadNetModule()
      const connected = net.connect()
      const socket = FakeWebSocket.instances[0]
      sendSuccessfulHandshake(socket, 43)
      await connected
      const syncCount = () => socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
      socket.receive({ type: 'matchmakingFound', mode: 'ranked', roomCode: 'MATCH2', matchId: 'match-two' })
      await timers.advance(700)
      socket.receive({ type: 'matchmakingFound', mode: 'ranked', roomCode: 'MATCH2', matchId: 'match-two' })
      assert.equal(syncCount(), 0, 'duplicate match-found notifications must remain inside one grace window')
      await timers.advance(100)
      assert.equal(syncCount(), 1, 'a genuinely missing initial snapshot must request authoritative state')
      socket.receive({ type: 'roomState', roomCode: 'MATCH2', yourPlayerIndex: 0, started: true, players: [] })
      socket.receive({ type: 'gameState', state: {
        matchId: 'match-two', roomCode: 'MATCH2', revision: 1, phase: 'Main', players: [],
      } })
      await timers.advance(8_000)
      assert.equal(syncCount(), 1, 'a recovered initial snapshot must cancel the bounded retry timer')
      assert.equal(net.l12State.recoveryPhase, 'snapshot-acknowledged')
      socket.receive({ type: 'gameStateDelta', matchId: 'match-two', baseRevision: 0, revision: 2,
        changes: [{ path: ['state', 'revision'], value: 2 }] })
      assert.equal(syncCount(), 2, 'a later bad baseline must receive a fresh recovery budget')
      net.disconnect()
    }

    {
      const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'match-rejected-token' })
      const net = await loadNetModule()
      const connected = net.connect()
      const socket = FakeWebSocket.instances[0]
      sendSuccessfulHandshake(socket, 48)
      await connected
      const syncCount = () => socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
      socket.receive({ type: 'matchmakingFound', mode: 'ranked', roomCode: 'MATCH3', matchId: 'match-three' })
      await timers.advance(800)
      assert.equal(syncCount(), 1)
      socket.receive({ type: 'matchmakingRejected', message: '匹配已撤销' })
      await timers.advance(15_000)
      assert.equal(syncCount(), 1, 'an authoritative rejection must cancel the missing-initial-state retry')
      assert.equal(socket.closeCalls.length, 0)
      assert.equal(net.l12State.recoveryPhase, 'snapshot-acknowledged')
      assert.equal(net.l12State.notice, '匹配已撤销')
      net.disconnect()
    }
  }],
  ['permanent snapshot mismatch is bounded and falls back to a fresh connection generation', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'bounded-snapshot-token' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 44)
    await pending
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 44, roomCode: 'NEVER' })
    const syncCount = () => socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    assert.equal(syncCount(), 1)
    await timers.advance(15_000)
    assert.equal(syncCount(), net.SNAPSHOT_RECOVERY_MAX_ATTEMPTS,
      'snapshot recovery must issue only its fixed attempt budget')
    assert.equal(socket.closeCalls.at(-1)?.code, 4000,
      'exhausted snapshot recovery must replace the broken connection generation')
    assert.equal(net.l12State.status, 'offline')
    await timers.advance(1_000)
    assert.equal(FakeWebSocket.instances.length, 2, 'bounded recovery must retain automatic reconnect reliability')
    net.disconnect()
  }],
  ['consecutive broken generations keep reconnect backoff and preserve a queued request until recovery succeeds', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'multi-generation-token' })
    const net = await loadNetModule()
    const firstConnect = net.connect()
    const first = FakeWebSocket.instances[0]
    first.open()
    first.receive({ type: 'session', sessionId: 'multi-1', connectionGeneration: 45,
      capabilities: { requestIds: true, deltaGameState: true } })
    first.receive({ type: 'roomState', roomCode: 'MULTI1', yourPlayerIndex: 0, started: true, players: [] })
    first.receive({ type: 'gameState', state: {
      matchId: 'multi-match', roomCode: 'MULTI1', revision: 1, phase: 'Main', players: [],
    } })
    first.receive({ type: 'recoveryComplete', connectionGeneration: 45,
      roomCode: 'MULTI1', matchId: 'multi-match', recoveryRevision: 1 })
    await firstConnect
    net.gameAction({ type: 'endTurn' })
    const queuedAction = JSON.parse(first.sent.at(-1))
    assert.equal(net.l12State.pendingAction, true)

    first.receive({ type: 'gameStateDelta', matchId: 'multi-match', baseRevision: 0, revision: 2,
      changes: [{ path: ['state', 'revision'], value: 2 }] })
    await timers.advance(15_000)
    assert.equal(first.closeCalls.at(-1)?.code, 4000)
    assert.equal(net.l12State.status, 'offline')
    assert.equal(net.l12State.connectionIssue, 'websocket')
    assert.match(net.l12State.notice, /权威状态/)
    assert.equal(net.l12State.pendingAction, true, 'bounded replacement must retain the negotiated request envelope')
    assert.equal(net.l12State.retryCount, 1)

    await timers.advance(1_000)
    const second = FakeWebSocket.instances[1]
    second.open()
    second.receive({ type: 'session', sessionId: 'multi-2', connectionGeneration: 46,
      capabilities: { requestIds: true, deltaGameState: true } })
    second.receive({ type: 'recoveryComplete', connectionGeneration: 46,
      roomCode: 'MULTI1', matchId: 'multi-match', recoveryRevision: 2 })
    await timers.advance(net.CONNECTION_HANDSHAKE_TIMEOUT_MS)
    assert.equal(second.closeCalls.at(-1)?.code, 4000)
    assert.equal(net.l12State.status, 'offline')
    assert.equal(net.l12State.pendingAction, true)
    assert.equal(net.l12State.retryCount, 2,
      'an unsuccessful replacement generation must not reset reconnect backoff')
    await timers.advance(1_999)
    assert.equal(FakeWebSocket.instances.length, 2)
    await timers.advance(1)

    const third = FakeWebSocket.instances[2]
    third.open()
    third.receive({ type: 'session', sessionId: 'multi-3', connectionGeneration: 47,
      capabilities: { requestIds: true, deltaGameState: true } })
    third.receive({ type: 'roomState', roomCode: 'MULTI1', yourPlayerIndex: 0, started: true, players: [] })
    third.receive({ type: 'gameState', state: {
      matchId: 'multi-match', roomCode: 'MULTI1', revision: 2, phase: 'Main', players: [],
    } })
    third.receive({ type: 'recoveryComplete', connectionGeneration: 47,
      roomCode: 'MULTI1', matchId: 'multi-match', recoveryRevision: 2 })
    await flushPromises()
    assert.equal(net.l12State.status, 'online')
    assert.equal(net.l12State.retryCount, 0, 'only an authoritative recovery success may reset reconnect backoff')
    const resent = third.sent.filter(item => JSON.parse(item).type === 'gameAction')
    assert.equal(resent.length, 1)
    assert.deepEqual(JSON.parse(resent[0]), queuedAction)
    third.receive({ type: 'actionRejected', requestId: queuedAction.requestId, message: '权威响应' })
    assert.equal(net.l12State.pendingAction, false)

    third.emitClose(1006, 'post-recovery network loss')
    assert.equal(net.l12State.retryCount, 1)
    await timers.advance(999)
    assert.equal(FakeWebSocket.instances.length, 3)
    await timers.advance(1)
    assert.equal(FakeWebSocket.instances.length, 4,
      'successful recovery must restore the first reconnect delay')
    net.disconnect()
  }],
  ['GamePage retry reuses automatic connection recovery and syncs only an already healthy socket', async () => {
    const state = { socket: null, status: 'offline', recoveryPhase: 'disconnected', notice: 'old failure' }
    const sends = []
    let connectCalls = 0
    const page = await loadGamePageRetryModule({
      l12State: state,
      WebSocket: FakeWebSocket,
      connect: async () => {
        connectCalls += 1
        state.socket = { readyState: FakeWebSocket.OPEN }
        state.status = 'online'
        state.recoveryPhase = 'snapshot-acknowledged'
      },
      send: payload => sends.push(payload),
    })
    await page.retryGameLoad()
    assert.equal(connectCalls, 1)
    assert.equal(sends.length, 0, 'a new connection already completed its authoritative recovery')

    await page.retryGameLoad()
    assert.deepEqual(sends, [{ type: 'syncState' }],
      'an already healthy connection with a missing view may request one manual sync')

    state.recoveryPhase = 'snapshot-mismatch'
    state.notice = '权威恢复快照尚未完整到达，正在重新同步…'
    await page.retryGameLoad()
    assert.equal(sends.length, 1, 'manual retry must not duplicate an active baseline-recovery loop')
    assert.equal(state.notice, '权威恢复快照尚未完整到达，正在重新同步…',
      'a retry that cannot improve active recovery must preserve the truthful error state')
  }],
  ['verified route changes preserve authentication and never force a healthy socket to reconnect', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'route-token' })
    const platform = await loadPlatformModule()
    const calls = installFetchPlan([async () => response(200, accountFixture())])
    await platform.initializeAuth()
    const guard = await loadRouteGuard(platform)
    for (const path of ['/battle', '/battle/records', '/battle/friends']) {
      assert.equal(await guard({ meta: { requiresAccount: true }, fullPath: path }), true)
      assert.equal(platform.authState.verified, true)
    }
    assert.equal(calls.length, 1, 'navigation must not issue forced identity refreshes')
    assert.equal(globalThis.__l12DisconnectCalls, 0)
  }],
  ['route guard fails closed for cached but unverified identity and denied admin access', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'cached-token' })
    const platform = await loadPlatformModule()
    platform.platformState.account = accountFixture()
    installFetchPlan([async () => response(503, { message: 'temporary failure' })])
    const guard = await loadRouteGuard(platform)
    assert.deepEqual(await guard({ meta: { requiresAccount: true }, fullPath: '/battle' }),
      { name: 'me', query: { redirect: '/battle' } })
    platform.authState.verified = true
    assert.deepEqual(await guard({ meta: { requiresAdmin: true }, fullPath: '/admin' }),
      { name: 'me', query: { redirect: '/admin' } })
  }],
  ['spectator recovery rejects a missing room snapshot then completes without relaxing identity checks', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'spectator-token' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'spectator-session', connectionGeneration: 21, recovered: true })
    const game = { matchId: 'spectated-match', roomCode: 'WATCH1', revision: 8, phase: 'Main', players: [] }
    const ack = { type: 'recoveryComplete', connectionGeneration: 21, roomCode: 'WATCH1', matchId: game.matchId, recoveryRevision: 8 }
    socket.receive({ type: 'gameState', spectating: true, state: game })
    socket.receive(ack)
    assert.equal(net.l12State.recoveryPhase, 'snapshot-mismatch', 'the original missing room sequence must reproduce the incident')
    assert.equal((await promiseOutcome(pending)).status, 'pending')
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, 1)
    socket.receive({ type: 'roomState', roomCode: 'WATCH1', yourPlayerIndex: null, started: true, players: [] })
    socket.receive({ type: 'gameState', spectating: true, gmEnabled: false, state: game })
    socket.receive(ack)
    await pending
    socket.receive(ack)
    assert.equal(net.l12State.status, 'online')
    assert.equal(net.l12State.spectating, true)
    assert.equal(net.l12State.gmEnabled, false)
    assert.equal(net.l12State.room.yourPlayerIndex, null)
    assert.equal(net.l12State.notice, '')
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, 1, 'complete and repeated acknowledgements must not restart the sync loop')
    net.disconnect()
  }],

  ['ordinary and tournament spectator refresh accept ordered public room game and acknowledgement', async () => {
    for (const tournament of [false, true]) {
      installBrowserEnvironment({ 'l12-auth-token': 'spectator-token' })
      const net = await loadNetModule()
      const pending = net.connect()
      const socket = FakeWebSocket.instances[0]
      socket.open()
      socket.receive({ type: 'session', sessionId: 'fresh-spectator', connectionGeneration: 22, recovered: true })
      socket.receive({ type: 'roomState', roomCode: 'WATCH2', yourPlayerIndex: null, started: true, players: [] })
      socket.receive({ type: 'gameState', spectating: true, gmEnabled: false,
        ...(tournament ? { tournamentId: 'tournament-1', tournamentCode: 'EVENT1', tournamentMatchId: 'table-1' } : {}),
        state: { matchId: 'watched-game', roomCode: 'WATCH2', revision: 12, phase: 'Main', players: [] } })
      socket.receive({ type: 'recoveryComplete', connectionGeneration: 22, roomCode: 'WATCH2', matchId: 'watched-game', recoveryRevision: 12 })
      await pending
      assert.equal(net.l12State.status, 'online')
      assert.equal(net.l12State.spectating, true)
      assert.equal(net.l12State.gmEnabled, false)
      if (tournament) assert.equal(net.l12State.game.tournamentMatchId, 'table-1')
      assert.equal(socket.sent.some(item => JSON.parse(item).type === 'syncState'), false)
      net.leaveRoom()
      assert.equal(JSON.parse(socket.sent.at(-1)).type, 'leaveRoom')
      socket.receive({ type: 'roomLeft' })
      socket.receive({ type: 'recoveryComplete', connectionGeneration: 22, roomCode: null, matchId: null })
      assert.equal(net.l12State.room, null)
      assert.equal(net.l12State.game, null)
      assert.equal(net.l12State.spectating, false)
      net.disconnect()
    }
  }],

  ['spectator recovery still refuses a wrong room match or lower revision', async () => {
    for (const mismatch of ['room', 'match', 'revision']) {
      installBrowserEnvironment({ 'l12-auth-token': 'spectator-token' })
      const net = await loadNetModule()
      const pending = net.connect()
      const socket = FakeWebSocket.instances[0]
      socket.open()
      socket.receive({ type: 'session', sessionId: 'spectator-identity', connectionGeneration: 23 })
      socket.receive({ type: 'roomState', roomCode: mismatch === 'room' ? 'WRONG' : 'WATCH3', yourPlayerIndex: null, started: true, players: [] })
      socket.receive({ type: 'gameState', spectating: true,
        state: { matchId: mismatch === 'match' ? 'wrong-match' : 'expected-match', revision: mismatch === 'revision' ? 4 : 5, phase: 'Main', players: [] } })
      const ack = { type: 'recoveryComplete', connectionGeneration: 23, roomCode: 'WATCH3', matchId: 'expected-match', recoveryRevision: 5 }
      socket.receive(ack)
      assert.equal(net.l12State.recoveryPhase, 'snapshot-mismatch', `${mismatch} must not be bypassed for spectators`)
      assert.equal(net.l12State.status, 'connecting')
      socket.receive({ type: 'roomState', roomCode: 'WATCH3', yourPlayerIndex: null, started: true, players: [] })
      socket.receive({ type: 'gameState', spectating: true, state: { matchId: 'expected-match', revision: 5, phase: 'Main', players: [] } })
      socket.receive(ack)
      await pending
      assert.equal(net.l12State.status, 'online')
      net.disconnect()
    }
  }],

  ['temporary auth network failure retries and verifies cached token', async () => {
    const account = accountFixture()
    const { timers, storage } = installBrowserEnvironment({
      'l12-auth-token': 'retry-token',
      'l12-account': JSON.stringify(account),
    })
    const calls = installFetchPlan([
      () => { throw new TypeError('simulated offline network') },
      () => response(200, account),
    ])
    const platform = await loadPlatformModule()
    await assert.rejects(platform.initializeAuth(), error => error.code === 'network_error')
    assert.equal(platform.authState.initialized, true)
    assert.equal(platform.authState.verified, false)
    assert.equal(storage.getItem('l12-auth-token'), 'retry-token', 'a network failure must preserve the retryable token')
    await timers.advance(999)
    assert.equal(calls.length, 1)
    await timers.advance(1)
    assert.equal(calls.length, 2)
    assert.equal(platform.authState.verified, true)
    assert.equal(platform.platformState.account?.username, account.username)
  }],

  ['initialized but unverified auth bootstrap remains explicitly retryable', async () => {
    const account = accountFixture()
    installBrowserEnvironment({ 'l12-auth-token': 'manual-retry-token', 'l12-account': JSON.stringify(account) })
    const calls = installFetchPlan([
      () => { throw new TypeError('first request failed') },
      () => response(200, account),
    ])
    const platform = await loadPlatformModule()
    await assert.rejects(platform.initializeAuth(), error => error.code === 'network_error')
    await platform.initializeAuth()
    assert.equal(calls.length, 2, 'initialized=false must not be the only path that can verify a retained token')
    assert.equal(platform.authState.verified, true)
  }],

  ['temporary auth 5xx retries while remaining fail-closed', async () => {
    const account = accountFixture()
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'server-retry-token', 'l12-account': JSON.stringify(account) })
    const calls = installFetchPlan([
      () => response(503, { message: 'temporary outage' }),
      () => response(200, account),
    ])
    const platform = await loadPlatformModule()
    await assert.rejects(platform.initializeAuth(), error => error.status === 503)
    assert.equal(platform.authState.verified, false, 'cached identity must not grant permissions during a 5xx')
    await timers.advance(1_000)
    assert.equal(calls.length, 2)
    assert.equal(platform.authState.verified, true)
  }],

  ['hung auth request is aborted before entering the retry loop', async () => {
    const account = accountFixture()
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'hung-request-token', 'l12-account': JSON.stringify(account) })
    const calls = installFetchPlan([
      ({ init }) => new Promise((resolve, reject) => {
        init.signal.addEventListener('abort', () => reject(new DOMException('request timed out', 'AbortError')), { once: true })
      }),
      () => response(200, account),
    ])
    const platform = await loadPlatformModule()
    const pending = platform.initializeAuth()
    assert.equal(platform.AUTH_REFRESH_REQUEST_TIMEOUT_MS, 5_000)
    await timers.advance(platform.AUTH_REFRESH_REQUEST_TIMEOUT_MS - 1)
    assert.equal((await promiseOutcome(pending)).status, 'pending')
    await timers.advance(1)
    const aborted = await promiseOutcome(pending)
    assert.equal(aborted.status, 'rejected')
    assert.equal(aborted.error?.name, 'AbortError')
    assert.equal(platform.authState.verified, false)
    await timers.advance(platform.AUTH_REFRESH_RETRY_BASE_MS)
    assert.equal(calls.length, 2)
    assert.equal(platform.authState.verified, true)
  }],

  ['a 401 after a transient auth failure stops all further retries', async () => {
    const account = accountFixture()
    const { timers, storage } = installBrowserEnvironment({ 'l12-auth-token': 'eventually-rejected-token', 'l12-account': JSON.stringify(account) })
    const calls = installFetchPlan([
      () => { throw new TypeError('temporary network failure') },
      () => response(401, { message: 'expired' }),
    ])
    const platform = await loadPlatformModule()
    await assert.rejects(platform.initializeAuth(), error => error.code === 'network_error')
    await timers.advance(platform.AUTH_REFRESH_RETRY_BASE_MS)
    assert.equal(storage.getItem('l12-auth-token'), null)
    assert.equal(platform.authState.verified, false)
    await timers.advance(60_000)
    assert.equal(calls.length, 2)
  }],

  ['auth 401 clears credentials and never schedules a retry', async () => {
    const account = accountFixture()
    const { timers, storage } = installBrowserEnvironment({ 'l12-auth-token': 'rejected-token', 'l12-account': JSON.stringify(account) })
    const calls = installFetchPlan([() => response(401, { message: 'expired' })])
    const platform = await loadPlatformModule()
    assert.equal(await platform.initializeAuth(), null)
    assert.equal(storage.getItem('l12-auth-token'), null)
    assert.equal(platform.platformState.account, null)
    assert.equal(platform.authState.verified, false)
    assert.equal(globalThis.__l12DisconnectCalls, 1)
    await timers.advance(60_000)
    assert.equal(calls.length, 1, '401 must not enter the transient retry loop')
  }],

  ['duplicate connect calls share one socket and ack cannot precede session claim', async () => {
    const { storage } = installBrowserEnvironment({ 'l12-auth-token': 'valid-token' })
    const net = await loadNetModule()
    const first = net.connect()
    const duplicate = net.connect()
    assert.strictEqual(first, duplicate)
    assert.equal(FakeWebSocket.instances.length, 1)
    const socket = FakeWebSocket.instances[0]
    socket.open()
    assert.deepEqual(JSON.parse(socket.sent[0]), {
      type: 'hello', authToken: 'valid-token',
      capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true, presenceDeltaV1: true,
        presenceOrderReuseV1: true, chatV1: true, gameStateCardFrameV1: true, handArrayDeltaV1: true,
        gameStatePropertyFrameV1: true },
    })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 0 })
    assert.equal(net.l12State.status, 'connecting', 'recoveryComplete without this attempt session claim must be ignored')
    socket.receive({ type: 'session', sessionId: 'session-7', name: '测试玩家', connectionGeneration: 7 })
    socket.receive({ type: 'effectiveOperationsPolicy', policy: { maintenance: { active: false } } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 7 })
    await first
    assert.equal(net.l12State.status, 'online')
    assert.equal(net.l12State.recoveryPhase, 'snapshot-acknowledged')
    assert.equal(storage.getItem('l12-auth-token'), 'valid-token')
    assert.equal(socket.sent.some(item => ['createRoom', 'createSandbox'].includes(JSON.parse(item).type)), false,
      'connection regression must never create a real room or sandbox')
  }],

  ['disconnect cancels a pending attempt and a new connect is not trapped behind it', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'valid-token' })
    const net = await loadNetModule()
    const first = net.connect()
    const firstSocket = FakeWebSocket.instances[0]
    net.disconnect()
    const cancelled = await promiseOutcome(first)
    assert.equal(cancelled.status, 'rejected', 'disconnect must settle the in-flight connect promise')
    const second = net.connect()
    assert.notStrictEqual(second, first)
    assert.equal(FakeWebSocket.instances.length, 2, 'a fresh attempt must allocate a new WebSocket immediately')
    const secondSocket = FakeWebSocket.instances[1]
    firstSocket.open()
    assert.equal(firstSocket.sent.length, 0, 'a superseded socket opening late must not send hello')
    sendSuccessfulHandshake(secondSocket, 8)
    await second
    assert.equal(net.l12State.status, 'online')
  }],

  ['missing recoveryComplete hits a bounded handshake timeout and retries', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'valid-token' })
    const net = await loadNetModule()
    assert.equal(net.CONNECTION_HANDSHAKE_TIMEOUT_MS, 10_000)
    const first = net.connect()
    const firstSocket = FakeWebSocket.instances[0]
    firstSocket.open()
    firstSocket.receive({ type: 'session', sessionId: 'session-9', name: '测试玩家', connectionGeneration: 9 })
    firstSocket.receive({ type: 'effectiveOperationsPolicy', policy: { maintenance: { active: false } } })
    await timers.advance(net.CONNECTION_HANDSHAKE_TIMEOUT_MS)
    const timedOut = await promiseOutcome(first)
    assert.equal(timedOut.status, 'rejected')
    assert.match(String(timedOut.error?.message), /握手超时/)
    assert.equal(net.l12State.status, 'offline')
    assert.equal(net.l12State.retryCount, 1)
    assert.equal(firstSocket.closeCalls[0]?.code, 4000)
    await timers.advance(999)
    assert.equal(FakeWebSocket.instances.length, 1)
    await timers.advance(1)
    assert.equal(FakeWebSocket.instances.length, 2, 'handshake timeout must release connectPromise before backoff retry')
    const secondSocket = FakeWebSocket.instances[1]
    sendSuccessfulHandshake(secondSocket, 10)
    assert.equal(net.l12State.status, 'online')
  }],

  ['network close retries once and stale socket events cannot clobber the new generation', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'valid-token' })
    const net = await loadNetModule()
    const first = net.connect()
    const firstSocket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(firstSocket, 11)
    await first
    firstSocket.emitClose(1006, 'network lost')
    assert.equal(net.l12State.status, 'offline')
    await timers.advance(1_000)
    assert.equal(FakeWebSocket.instances.length, 2)
    const secondSocket = FakeWebSocket.instances[1]
    assert.strictEqual(net.l12State.socket, secondSocket)
    firstSocket.receive({ type: 'recoveryComplete', connectionGeneration: 11 })
    firstSocket.emitClose(4002, 'late stale close')
    assert.strictEqual(net.l12State.socket, secondSocket)
    assert.equal(net.l12State.status, 'connecting')
    sendSuccessfulHandshake(secondSocket, 12)
    assert.equal(net.l12State.connectionGeneration, 12)
    assert.equal(net.l12State.status, 'online')
    assert.equal(net.l12State.lastCloseCode, null, 'a recovered live socket must not report the prior generation close code')
    assert.equal(net.l12State.lastCloseReason, '', 'a recovered live socket must not report the prior generation close reason')
  }],

  ['WebSocket error retries even when the browser has not emitted close yet', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'valid-token' })
    const net = await loadNetModule()
    const first = net.connect()
    const firstSocket = FakeWebSocket.instances[0]
    firstSocket.open()
    firstSocket.fail()
    const failed = await promiseOutcome(first)
    assert.equal(failed.status, 'rejected')
    assert.equal(net.l12State.status, 'offline')
    assert.equal(net.l12State.retryCount, 1)
    assert.equal(firstSocket.closeCalls[0]?.code, 4000)
    await timers.advance(1_000)
    assert.equal(FakeWebSocket.instances.length, 2)
    sendSuccessfulHandshake(FakeWebSocket.instances[1], 13)
    assert.equal(net.l12State.status, 'online')
  }],

  ['authentication rejection never enters WebSocket reconnect loop', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'invalid-token' })
    const net = await loadNetModule()
    const pending = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'authenticationRequired', message: 'expired token' })
    assert.equal((await promiseOutcome(pending)).status, 'rejected')
    socket.emitClose(4001, 'authentication rejected')
    await timers.advance(60_000)
    assert.equal(FakeWebSocket.instances.length, 1)
    assert.equal(net.l12State.connectionIssue, 'authentication')
    assert.equal(net.l12State.recoveryPhase, 'disconnected')
  }],

  ['request ids ignore stale replies and resend exactly once after authoritative reconnect recovery', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'request-id-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const firstSocket = FakeWebSocket.instances[0]
    firstSocket.open()
    firstSocket.receive({ type: 'session', sessionId: 'request-session-1', name: '测试玩家',
      connectionGeneration: 71, capabilities: { requestIds: true, deltaGameState: true } })
    firstSocket.receive({ type: 'roomState', roomCode: 'REQ001', yourPlayerIndex: 0, started: true, players: [] })
    firstSocket.receive({ type: 'gameState', state: {
      matchId: 'request-match', roomCode: 'REQ001', revision: 1, phase: 'Main', players: [],
    } })
    firstSocket.receive({ type: 'recoveryComplete', connectionGeneration: 71,
      roomCode: 'REQ001', matchId: 'request-match', recoveryRevision: 1 })
    await connected

    net.gameAction({ type: 'endTurn' })
    const original = JSON.parse(firstSocket.sent.at(-1))
    assert.equal(original.type, 'gameAction')
    assert.equal(typeof original.requestId, 'string')
    firstSocket.receive({ type: 'actionRejected', requestId: 'older-request', message: '迟到响应' })
    assert.equal(net.l12State.pendingAction, true, 'a stale response must not release the current action')

    firstSocket.emitClose(1006, 'network lost after submit')
    assert.equal(net.l12State.pendingAction, true, 'a negotiated action remains retryable across disconnect')
    await timers.advance(1_000)
    const secondSocket = FakeWebSocket.instances[1]
    secondSocket.open()
    secondSocket.receive({ type: 'session', sessionId: 'request-session-2', name: '测试玩家',
      connectionGeneration: 72, recovered: true,
      capabilities: { requestIds: true, deltaGameState: true } })
    secondSocket.receive({ type: 'roomState', roomCode: 'REQ001', yourPlayerIndex: 0, started: true, players: [] })
    secondSocket.receive({ type: 'gameState', state: {
      matchId: 'request-match', roomCode: 'REQ001', revision: 1, phase: 'Main', players: [],
    } })
    assert.equal(secondSocket.sent.filter(item => JSON.parse(item).type === 'gameAction').length, 0,
      'the action must not race ahead of recoveryComplete')
    secondSocket.receive({ type: 'recoveryComplete', connectionGeneration: 72,
      roomCode: 'REQ001', matchId: 'request-match', recoveryRevision: 1 })
    await flushPromises()
    const resent = secondSocket.sent.filter(item => JSON.parse(item).type === 'gameAction')
    assert.equal(resent.length, 1)
    assert.deepEqual(JSON.parse(resent[0]), original)
    secondSocket.receive({ type: 'actionRejected', requestId: original.requestId, message: '权威响应' })
    assert.equal(net.l12State.pendingAction, false)
    net.disconnect()
  }],

  ['an action started while offline does not leave a negotiated request permanently pending', async () => {
    const { timers } = installBrowserEnvironment({ 'l12-auth-token': 'offline-action-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'offline-action-session', name: '测试玩家',
      connectionGeneration: 73, capabilities: { requestIds: true, deltaGameState: true } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 73 })
    await connected

    socket.emitClose(1006, 'network unavailable')
    assert.equal(net.l12State.status, 'offline')
    net.gameAction({ type: 'endTurn' })
    assert.equal(net.l12State.pendingAction, false,
      'an action that was never sent cannot be retried as if the server had accepted it')
    await timers.advance(1_000)
    net.disconnect()
  }],

  ['negotiated deltas rebuild the last private envelope and request a full snapshot on a missing base', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'delta-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'delta-session', name: '测试玩家',
      connectionGeneration: 81, capabilities: { requestIds: true, deltaGameState: true } })
    socket.receive({ type: 'roomState', roomCode: 'DELTA1', yourPlayerIndex: 0, started: true, players: [] })
    socket.receive({ type: 'gameState', state: {
      matchId: 'delta-match', roomCode: 'DELTA1', revision: 4, phase: 'Main',
      players: [{ hp: 20, hand: ['private-card'] }, { hp: 20, handCount: 1 }],
    } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 81,
      roomCode: 'DELTA1', matchId: 'delta-match', recoveryRevision: 4 })
    await connected

    socket.receive({ type: 'gameStateDelta', matchId: 'delta-match', baseRevision: 4, revision: 5,
      changes: [
        { path: ['state', 'revision'], value: 5, remove: false },
        { path: ['state', 'players', 0, 'hp'], value: 19, remove: false },
        { path: ['state', 'prompts'], value: [{ promptId: 'delta-prompt', kind: 'optional' }], remove: false },
        { path: ['state', 'players', 0, 'readyAfterNextKillSourceName'], value: null, remove: false },
      ] })
    assert.equal(net.l12State.game.revision, 5)
    assert.equal(net.l12State.game.players[0].hp, 19)
    assert.deepEqual(net.l12State.game.players[0].hand, ['private-card'])
    assert.equal(net.l12State.game.prompts[0].promptId, 'delta-prompt')
    assert.equal(Object.hasOwn(net.l12State.game.players[0], 'readyAfterNextKillSourceName'), true)
    assert.equal(net.l12State.game.players[0].readyAfterNextKillSourceName, null)

    const syncBefore = socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    socket.receive({ type: 'gameStateDelta', matchId: 'delta-match', baseRevision: 3, revision: 6,
      changes: [{ path: ['state', 'revision'], value: 6, remove: false }] })
    assert.equal(net.l12State.game.revision, 5)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, syncBefore + 1)
    net.disconnect()
  }],

  ['same-revision duplicate deltas acknowledge only the matching pending request', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'duplicate-delta-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'duplicate-delta-session', name: '测试玩家',
      connectionGeneration: 82, capabilities: { requestIds: true, deltaGameState: true } })
    socket.receive({ type: 'roomState', roomCode: 'DUP001', yourPlayerIndex: 0, started: true, players: [] })
    socket.receive({ type: 'gameState', requestId: null, state: {
      matchId: 'duplicate-delta-match', roomCode: 'DUP001', revision: 4, phase: 'Main',
      players: [{ hp: 20, hand: ['private-card'] }, { hp: 20, handCount: 1 }],
    } })
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 82,
      roomCode: 'DUP001', matchId: 'duplicate-delta-match', recoveryRevision: 4 })
    await connected
    net.gameAction({ type: 'endTurn' })
    const request = JSON.parse(socket.sent.at(-1))
    const sameRevision = requestId => ({ type: 'gameStateDelta', matchId: 'duplicate-delta-match',
      baseRevision: 4, revision: 4, changes: [{ path: ['requestId'], value: requestId, remove: false }] })
    socket.receive(sameRevision('other-request'))
    assert.equal(net.l12State.pendingAction, true)
    assert.equal(net.l12State.game.revision, 4)
    const syncBefore = socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    socket.receive({ ...sameRevision(request.requestId), baseRevision: 3 })
    assert.equal(net.l12State.pendingAction, true, 'invalid delta must not acknowledge the request')
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, syncBefore + 1)
    socket.receive(sameRevision(request.requestId))
    assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.game.revision, 4)
    assert.deepEqual(net.l12State.game.players[0].hand, ['private-card'])
    socket.receive(sameRevision(request.requestId))
    assert.equal(net.l12State.pendingAction, false, 'repeated acknowledgement is idempotent')
    assert.deepEqual(net.l12State.game.players[0].hand, ['private-card'])
    net.disconnect()
  }],

  ['resource revisions reject stale messages and a new server epoch accepts a lower revision', async () => {
    const { events } = installBrowserEnvironment({ 'l12-auth-token': 'resource-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'resource-session', accountId: 'account-1', name: '测试玩家', connectionGeneration: 91 })
    socket.receive({ type: 'resourceVersions', epoch: 'epoch-a', revisions: { friends: 5, presence: 2, rankedIntegrity: 1,
      alternateArtNotifications: 3, operationsPolicy: 4 } })
    socket.receive({ type: 'presenceSnapshot', epoch: 'epoch-a', revision: 2,
      resource: 'presence', items: [presenceItem('account-1', '测试玩家')] })
    socket.receive({ type: 'resourceChanged', resource: 'friends', epoch: 'epoch-a', revision: 6 })
    const friendEvents = () => events.filter(event => event.type === 'l12-resource-friends').length
    const afterCurrent = friendEvents()
    socket.receive({ type: 'resourceChanged', resource: 'friends', epoch: 'epoch-a', revision: 5 })
    socket.receive({ type: 'presenceSnapshot', epoch: 'epoch-a', revision: 1, items: [] })
    assert.equal(friendEvents(), afterCurrent, 'a stale resource event must not trigger a read')
    assert.equal(net.l12State.presence.length, 1, 'an older presence snapshot must not erase confirmed state')
    socket.receive({ type: 'resourceChanged', resource: 'friends', epoch: 'epoch-b', revision: 1 })
    assert.equal(friendEvents(), afterCurrent + 1, 'a restarted server epoch must accept its lower revision')
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 91 })
    await connected
    net.disconnect()
  }],

  ['presence delta rebuild is atomic and malformed packets request one full recovery', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'presence-delta-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'presence-delta-session', accountId: 'self', name: '测试玩家',
      connectionGeneration: 93, capabilities: { presenceDeltaV1: true } })
    socket.receive({ type: 'resourceVersions', epoch: 'presence-epoch', revisions: { presence: 1 } })
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'presence-epoch', revision: 1,
      items: [presenceItem('a', 'A'), presenceItem('b', 'B')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 1, revision: 3,
      removedAccountIds: ['a'], upserts: [presenceItem('b', 'B', 'room'), presenceItem('c', 'C')],
      order: ['c', 'b'] })
    assert.deepEqual(net.l12State.presence.map(item => [item.accountId, item.activity]), [['c', 'idle'], ['b', 'room']])
    assert.equal(net.l12State.resourceRevisions.presence, 3, 'a targeted global revision gap is valid')

    const beforeInvalid = JSON.stringify(net.l12State.presence)
    const invalid = { type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 2, revision: 4,
      removedAccountIds: ['c'], upserts: [presenceItem('x', 'private-leak')], order: ['b', 'x'] }
    socket.receive(invalid)
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 3, revision: 4,
      removedAccountIds: [], upserts: [presenceItem('b', 'B', 'playing')], order: ['c', 'b'] })
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'poison-epoch', revision: 99,
      items: [presenceItem('duplicate', 'one'), presenceItem('duplicate', 'two')] })
    assert.equal(JSON.stringify(net.l12State.presence), beforeInvalid, 'invalid deltas must not partially mutate presence')
    assert.equal(net.l12State.resourceEpoch, 'presence-epoch', 'bad full must not poison the accepted epoch')
    assert.equal(net.l12State.resourceRevisions.presence, 3)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 1,
      'bad packets request only one full snapshot until recovery')

    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'presence-epoch', revision: 3,
      items: [presenceItem('c', 'C'), presenceItem('b', 'B', 'room')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 3, revision: 4,
      removedAccountIds: ['missing'], upserts: [], order: ['c', 'b'] })
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 2,
      'a nonexistent removal is rejected after full clears the recovery guard')
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'presence-epoch', revision: 3,
      items: [presenceItem('c', 'C'), presenceItem('b', 'B', 'room')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 3,
      revision: '4', removedAccountIds: [], upserts: [], order: ['c', 'b'] })
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 3,
      'string revisions are not coerced')
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'presence-epoch', revision: 3,
      items: [presenceItem('c', 'C'), presenceItem('b', 'B', 'room')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 7, baseRevision: 3,
      revision: 4, removedAccountIds: [], upserts: [], order: ['c', 'b'] })
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 4,
      'numeric epochs are not coerced')
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'presence-epoch', revision: 3,
      items: [presenceItem('c', 'C'), presenceItem('b', 'B', 'room')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'presence-epoch', baseRevision: 3,
      revision: true, removedAccountIds: [], upserts: [], order: ['c', 'b'] })
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 5,
      'boolean revisions are not coerced')
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 93 })
    await connected
    net.disconnect()
  }],

  ['presence delta waits for one fully validated connection-local full baseline', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'presence-cold-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'presence-cold-session', accountId: 'self', name: '测试玩家',
      connectionGeneration: 95, capabilities: { presenceDeltaV1: true } })
    socket.receive({ type: 'resourceVersions', epoch: 'cold-epoch', revisions: { presence: 1 } })
    const delta = { type: 'presenceDelta', resource: 'presence', epoch: 'cold-epoch', baseRevision: 1, revision: 2,
      removedAccountIds: [], upserts: [presenceItem('self', '测试玩家', 'room')], order: ['self'] }
    socket.receive(delta)
    assert.deepEqual(net.l12State.presence, [])
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 1)
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'cold-epoch', revision: 1,
      items: [presenceItem('self', '测试玩家'), presenceItem('self', '重复')] })
    socket.receive(delta)
    assert.deepEqual(net.l12State.presence, [], 'a duplicate-id full cannot unlock delta acceptance')
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 1)
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'cold-epoch', revision: 1,
      items: [presenceItem('self', '测试玩家')] })
    socket.receive(delta)
    assert.equal(net.l12State.presence[0].activity, 'room')
    assert.equal(net.l12State.resourceRevisions.presence, 2)
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 95 })
    await connected
    net.disconnect()
  }],

  ['legacy presence client keeps full state and refuses unsolicited deltas', async () => {
    installBrowserEnvironment({ 'l12-auth-token': 'presence-legacy-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    socket.open()
    socket.receive({ type: 'session', sessionId: 'presence-legacy-session', accountId: 'legacy', name: '旧客户端',
      connectionGeneration: 94, capabilities: {} })
    socket.receive({ type: 'resourceVersions', epoch: 'legacy-epoch', revisions: { presence: 1 } })
    socket.receive({ type: 'presenceSnapshot', resource: 'presence', epoch: 'legacy-epoch', revision: 1,
      items: [presenceItem('legacy', '旧客户端')] })
    socket.receive({ type: 'presenceDelta', resource: 'presence', epoch: 'legacy-epoch', baseRevision: 1, revision: 2,
      removedAccountIds: [], upserts: [presenceItem('legacy', '旧客户端', 'room')],
      order: ['legacy'] })
    assert.equal(net.l12State.presence[0].activity, 'idle')
    assert.equal(net.l12State.resourceRevisions.presence, 1)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncPresence').length, 1)
    socket.receive({ type: 'recoveryComplete', connectionGeneration: 94 })
    await connected
    net.disconnect()
  }],

  ['resource fallback stays silent online and starts only after the WebSocket is unavailable for 60 seconds', async () => {
    const { timers, events } = installBrowserEnvironment({ 'l12-auth-token': 'fallback-token' })
    const net = await loadNetModule()
    const connected = net.connect()
    const socket = FakeWebSocket.instances[0]
    sendSuccessfulHandshake(socket, 92)
    await connected
    await timers.advance(50_000)
    const fallbackEvents = () => events.filter(event => event.type === 'l12-resource-changed' && event.detail?.fallback).length
    assert.equal(fallbackEvents(), 0, 'online steady state must not poll HTTP resources')
    socket.emitClose(1006, 'network unavailable')
    await timers.advance(59_999)
    assert.equal(fallbackEvents(), 0)
    await timers.advance(1)
    assert.equal(fallbackEvents(), 7,
      'offline fallback must coalesce to one signal per resource after 60 seconds')
    net.disconnect()
  }],
]

let passed = 0
const failures = []
for (const [name, test] of tests) {
  try {
    await test()
    passed += 1
  } catch (error) {
    failures.push({ name, error })
    console.error(`FAIL: ${name}`)
    console.error(error?.stack || error)
  }
}

if (failures.length) {
  console.error(`L12 connection recovery behavior: ${passed}/${tests.length} passed`)
  process.exitCode = 1
} else console.log(`L12 connection recovery behavior: ${passed}/${tests.length} passed`)
