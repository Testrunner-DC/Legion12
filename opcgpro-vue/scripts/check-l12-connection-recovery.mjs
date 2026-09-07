import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const frontendRoot = fileURLToPath(new URL('..', import.meta.url))
const sourceRoot = join(frontendRoot, 'src', 'l12')
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
  globalThis.localStorage = storage
  globalThis.location = { protocol: 'https:', host: 'legion-12.com', hostname: 'legion-12.com' }
  globalThis.window = {
    setTimeout: timers.setTimeout,
    clearTimeout: timers.clearTimeout,
    setInterval: timers.setInterval,
    clearInterval: timers.clearInterval,
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() {},
  }
  globalThis.CustomEvent = class { constructor(type, options) { this.type = type; this.detail = options?.detail } }
  globalThis.__viteEnv = {}
  globalThis.__l12DisconnectCalls = 0
  globalThis.__l12NetState = { endpoint: 'wss://legion-12.com/ws', nickname: '' }
  FakeWebSocket.instances = []
  globalThis.WebSocket = FakeWebSocket
  return { timers, storage }
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

async function loadPlatformModule() {
  const filename = join(sourceRoot, 'platform.ts')
  let source = readFileSync(filename, 'utf8')
  source = source
    .replace("import { computed, reactive } from 'vue'", `
      const reactive = value => value
      const computed = getter => ({ get value() { return getter() } })
    `)
    .replace("import { disconnect, l12State, type BugClientConnectionDiagnostic } from './net'", `
      const l12State = globalThis.__l12NetState
      const disconnect = () => { globalThis.__l12DisconnectCalls += 1 }
    `)
  return importJavaScript(compile(source, filename), 'l12-platform-recovery-test')
}

async function loadNetModule() {
  const filename = join(sourceRoot, 'net.ts')
  let source = readFileSync(filename, 'utf8')
  source = source
    .replace("import { reactive } from 'vue'", 'const reactive = value => value')
    .replace("import { createGameReentryController } from './gameReentry'", `
      const createGameReentryController = () => ({
        update: async () => {},
        requestExit: async () => {},
        dispose: () => {},
      })
    `)
    .replaceAll('import.meta.env.VITE_WS_URL', 'globalThis.__viteEnv.VITE_WS_URL')
  return importJavaScript(compile(source, filename), 'l12-net-recovery-test')
}

async function loadRouteGuard(platform) {
  const filename = join(frontendRoot, 'src', 'router', 'index.ts')
  const source = readFileSync(filename, 'utf8')
  const body = source.slice(source.indexOf('router.beforeEach('))
  globalThis.__l12GuardPlatform = platform
  globalThis.__l12Guard = null
  await importJavaScript(compile(`
    const { canAccessAdmin, platformState, authState, refreshCurrentAccount } = globalThis.__l12GuardPlatform
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

const tests = [
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
    await assert.rejects(platform.initializeAuth(), /simulated offline network/)
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
    await assert.rejects(platform.initializeAuth(), /first request failed/)
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
    await assert.rejects(platform.initializeAuth(), /temporary network failure/)
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
      capabilities: { requestIds: true, deltaGameState: true },
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
      ] })
    assert.equal(net.l12State.game.revision, 5)
    assert.equal(net.l12State.game.players[0].hp, 19)
    assert.deepEqual(net.l12State.game.players[0].hand, ['private-card'])

    const syncBefore = socket.sent.filter(item => JSON.parse(item).type === 'syncState').length
    socket.receive({ type: 'gameStateDelta', matchId: 'delta-match', baseRevision: 3, revision: 6,
      changes: [{ path: ['state', 'revision'], value: 6, remove: false }] })
    assert.equal(net.l12State.game.revision, 5)
    assert.equal(socket.sent.filter(item => JSON.parse(item).type === 'syncState').length, syncBefore + 1)
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
