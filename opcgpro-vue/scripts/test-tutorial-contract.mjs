import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const runId = new Date().toISOString().replace(/[:.]/g, '-') + '-' + crypto.randomUUID().slice(0, 8)
const ciRoot = process.env.GITHUB_ACTIONS === 'true' && process.env.RUNNER_TEMP
  ? path.resolve(process.env.RUNNER_TEMP, 'l12-tutorial-contracts') : null
const output = process.env.L12_TUTORIAL_OUTPUT
  || (process.env.L12_ARTIFACT_ROOT && path.join(process.env.L12_ARTIFACT_ROOT, 'tutorial-build-20261018/frontend/contracts-' + runId))
  || (ciRoot && path.join(ciRoot, 'evidence', runId))
const cache = process.env.L12_TUTORIAL_VITE_CACHE
  || (process.env.L12_WORK_CACHE && path.join(process.env.L12_WORK_CACHE, 'tutorial-build-20261018/frontend/contracts-current'))
  || (ciRoot && path.join(ciRoot, 'cache'))
assert.ok(output && cache, 'Initialize L12 generated storage, or select explicit tutorial output and cache roots')
execFileSync(process.env.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'), ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_INITIALIZER -CacheRoot $env:L12_TUTORIAL_VITE_CACHE | Out-Null; . $env:L12_GUARD; '
    + '@($env:L12_TUTORIAL_OUTPUT,$env:L12_TUTORIAL_VITE_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "tutorial focused UI contract" }'], {
  stdio: 'pipe',
  env: { ...process.env,
    L12_TUTORIAL_OUTPUT: output,
    L12_TUTORIAL_VITE_CACHE: cache,
    L12_INITIALIZER: path.join(front, '../ops/windows/Initialize-L12BuildEnvironment.ps1'),
    L12_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') },
})
fs.mkdirSync(output, { recursive: true })

const files = {
  types: 'src/l12/types.ts', net: 'src/l12/net.ts', game: 'src/l12/GamePage.vue',
  hub: 'src/l12/site/BattleHubPage.vue', page: 'src/l12/site/TutorialPage.vue',
  overlay: 'src/l12/tutorial/TutorialGuidanceOverlay.vue', progress: 'src/l12/tutorial/tutorialProgress.ts',
  router: 'src/router/index.ts', admin: 'src/l12/site/adminSections.ts', sandbox: 'src/l12/site/SandboxPage.vue',
  script: 'scripts/test-tutorial-contract.mjs',
}
const source = Object.fromEntries(Object.entries(files).map(([key, relative]) =>
  [key, fs.readFileSync(path.join(front, relative), 'utf8')]))
const cases = []
function check(name, work) { work(); cases.push(name); console.log(`PASS ${name}`) }

check('three playable lessons and five unavailable lessons stay presentation-only', () => {
  assert.match(source.hub, /跟着提示，练习部署支付、普通移动和攻防/)
  assert.match(source.page, /const lessons:[\s\S]*id: 'deploy'[\s\S]*id: 'move'[\s\S]*id: 'combat'/)
  assert.match(source.page, /其余课程正在准备/)
  assert.doesNotMatch(source.page, /lesson\.order >|completed\.includes\([^)]*order|未接入服务器合法场景|不会假完成/)
  assert.equal((source.page.match(/@click="startLesson\(lesson\.id\)"/g) || []).length, 1)
})
check('lesson copy names the fixed first-slice actors and cells without private ids', () => {
  for (const text of ['1 费《西施》', '后排中格的《西施》', '前排左格军团进攻对方主宰']) assert.ok(source.page.includes(text))
  assert.doesNotMatch(source.page + source.overlay, /S01-0116|instanceId|cardInstanceId/)
})
check('overlay is player-facing, minimizable, and keeps touch targets clear', () => {
  for (const text of ['新手教学', '学习提示', '本关完成，可查看场面变化或返回目录']) assert.ok(source.overlay.includes(text))
  const template = parse(source.overlay, { filename: files.overlay }).descriptor.template.content
  assert.doesNotMatch(template, /\{\{\s*tutorial\.(?:lessonId|stepId)|服务器权威|不会通关|判定说明/)
  assert.match(source.overlay, /left:14px;top:14px/)
  assert.match(source.overlay, /header button\{min-width:44px;min-height:44px/)
  assert.match(source.overlay, /summary\{min-height:44px/)
  assert.ok(source.overlay.includes('<BattleOverlayPortal>'), 'Expanded teaching copy must not be squeezed into the narrow action lane')
  assert.ok(source.overlay.includes('width:min(360px,calc(var(--l12-viewport-width,100vw) - 200px))'), 'The readable panel must use the logical canvas width')
  assert.match(source.overlay, /mobile-battle-dock__context > \.tutorial-guidance-restore\)\{[^}]*position:static!important[^}]*min-height:44px!important/)
})
check('tutorial route replaces public single-player while sandbox is admin-only', () => {
  assert.ok(source.router.includes("path: '/battle/tutorial'"))
  assert.ok(source.router.includes("path: 'operations/sandbox'"))
  assert.match(source.router, /path: '\/sandbox'[\s\S]{0,180}redirect: '\/admin\/operations\/sandbox'[\s\S]{0,180}requiresAdmin: true/)
  assert.match(source.admin, /id: 'sandbox'[\s\S]{0,200}permissions: \['admin\.operations\.write'\]/)
  assert.match(source.sandbox, /platformState\.account\?\.role === 'admin'[\s\S]{0,100}hasPermission\('admin\.operations\.write'\)/)
})
check('wire request is exact and has no client fixture authority', () => {
  assert.match(source.net, /pendingActionEnvelope = \{ type: 'createTutorial', lessonId, requestId \}/)
  const fn = source.net.slice(source.net.indexOf('export function createTutorial('), source.net.indexOf('export const joinRoom'))
  assert.doesNotMatch(fn, /deck|actor|seed|checkpoint|gm|setTimeout/)
  assert.match(fn, /l12State\.matchmaking\?\.queued/)
})
check('all tutorial and sandbox wire failures enter the real pending-action consumer', () => {
  for (const type of ['tutorialRejected', 'tutorialUnavailable', 'tutorialRequestExpired', 'tutorialRestartRequired', 'sandboxForbidden'])
    assert.ok(source.net.includes(`message.type === '${type}'`) || source.net.includes(`message.type === '${type}'`))
})
check('tutorial capability conflicts fail closed without enabling GM or referee controls', () => {
  assert.match(source.net, /tutorialCapabilityConflict = Boolean\(projectedTutorial && \(l12State\.gmEnabled \|\| l12State\.spectating\)\)/)
  assert.match(source.net, /if \(tutorialCapabilityConflict\) \{\s*l12State\.gmEnabled = false\s*l12State\.refereeControlsEnabled = false/)
})
check('completion is bound to authoritative completed tutorial identity only', () => {
  assert.ok(source.page.includes('l12State.accountId === accountId.value'))
  assert.ok(source.page.includes('l12State.accountId === expectedAccountId'))
  assert.match(source.game, /current\?\.status !== 'completed'/)
  assert.match(source.game, /platformState\.account\?\.id === current\.accountId/)
  assert.match(source.game, /l12State\.connectionGeneration === current\.connectionGeneration/)
  assert.doesNotMatch(source.game, /game\.phase === 'GameOver'[\s\S]{0,120}recordTutorialCompletion/)
  assert.match(source.game, /tutorialRoom \? '\/battle\/tutorial'/)
  assert.match(source.game, /sandboxRoom \? '\/admin\/operations\/sandbox'/)
  assert.match(source.page, /v-if="l12State\.notice"[^>]*role="alert"/)
  assert.match(source.page, /matchmakingBusy[\s\S]*请先取消当前匹配，再开始教学/)
  assert.match(source.page, /请先结束并退出当前对局，再开始教学/)
})
check('new SFC script blocks compile independently', () => {
  for (const name of ['page', 'overlay']) {
    const descriptor = parse(source[name], { filename: files[name] }).descriptor
    assert.ok(compileScript(descriptor, { id: `tutorial-${name}` }).content)
  }
})

const progressCompiled = ts.transpileModule(source.progress, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }, reportDiagnostics: true,
})
assert.deepEqual((progressCompiled.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error), [])
const progress = await import('data:text/javascript;base64,' + Buffer.from(progressCompiled.outputText).toString('base64'))
class MemoryStorage {
  values = new Map()
  writes = 0
  getItem(key) { return this.values.get(key) ?? null }
  setItem(key, value) { this.writes++; this.values.set(key, String(value)) }
}
const projection = (overrides = {}) => ({ lessonId: 'deploy', version: 1, stepId: 'completed', status: 'completed',
  accountId: 'account-a', sessionId: 'session-a1', connectionGeneration: 11,
  roomCode: 'TUTOR1', matchId: 'match-a1', ...overrides })
const binding = (overrides = {}) => ({ accountId: 'account-a', sessionId: 'session-a1',
  connectionGeneration: 11, matchId: 'match-a1', ...overrides })

check('active, wrong-version, wrong-match and wrong-identity states never record completion', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection({ status: 'active' }), binding(), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection({ version: 2 }), binding(), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ matchId: 'other' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ sessionId: 'other' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ connectionGeneration: 12 }), storage), false)
  assert.equal(storage.writes, 0)
})
check('completed lessons persist per account and exact match only', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), storage, '2026-10-18T00:00:00Z'), true)
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), storage, '2026-10-18T00:00:01Z'), true)
  assert.deepEqual(progress.loadTutorialProgress('account-a', storage), ['deploy'])
  assert.deepEqual(progress.loadTutorialProgress('account-b', storage), [])
  assert.equal(storage.writes, 1)
})
check('unreadable, unwritable, and blocked browser storage never throws or claims completion', () => {
  const unreadable = { getItem() { throw new Error('synthetic read denial') }, setItem() { assert.fail('must not overwrite unreadable progress') } }
  const unwritable = { getItem() { return null }, setItem() { throw new Error('synthetic quota') } }
  assert.deepEqual(progress.loadTutorialProgress('account-a', unreadable), [])
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), unreadable), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding(), unwritable), false)
  Object.defineProperty(globalThis, 'window', { configurable: true, get() { throw new Error('synthetic security denial') } })
  assert.deepEqual(progress.loadTutorialProgress('account-a'), [])
  assert.equal(progress.recordTutorialCompletion(projection(), binding()), false)
})
check('account ABA cannot apply an old completion and a fresh A binding can persist', () => {
  const storage = new MemoryStorage()
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ accountId: 'account-b' }), storage), false)
  assert.equal(progress.recordTutorialCompletion(projection(), binding({ sessionId: 'session-a2', connectionGeneration: 12 }), storage), false)
  const fresh = projection({ sessionId: 'session-a3', connectionGeneration: 13, matchId: 'match-a3', lessonId: 'move' })
  assert.equal(progress.recordTutorialCompletion(fresh, binding({ sessionId: 'session-a3', connectionGeneration: 13, matchId: 'match-a3' }), storage), true)
  assert.deepEqual(progress.loadTutorialProgress('account-a', storage), ['move'])
})

class BrowserStorage extends MemoryStorage {
  removeItem(key) { this.values.delete(key) }
  clear() { this.values.clear() }
}
class FakeWebSocket {
  static CONNECTING = 0; static OPEN = 1; static CLOSING = 2; static CLOSED = 3; static instances = []
  readyState = FakeWebSocket.CONNECTING; sent = []; onopen = null; onmessage = null; onerror = null; onclose = null
  constructor(url) {
    this.url = url
    Object.defineProperty(this, '__v_skip', { value: true })
    FakeWebSocket.instances.push(this)
  }
  open() { this.readyState = FakeWebSocket.OPEN; this.onopen?.({}) }
  receive(payload) { this.onmessage?.({ data: JSON.stringify(payload) }) }
  send(payload) { this.sent.push(String(payload)) }
  close(code = 1000, reason = '') { this.readyState = FakeWebSocket.CLOSED; this.onclose?.({ code, reason }) }
}
const browserStorage = new BrowserStorage()
browserStorage.setItem('l12-auth-token', 'tutorial-focused-token')
globalThis.localStorage = browserStorage
globalThis.location = { protocol: 'https:', host: 'legion-12.com', hostname: 'legion-12.com' }
Object.defineProperty(globalThis, 'window', { configurable: true, writable: true, value:
  { localStorage: browserStorage, setTimeout, clearTimeout, setInterval, clearInterval,
    addEventListener() {}, removeEventListener() {}, dispatchEvent() {} } })
globalThis.CustomEvent = class { constructor(type, options) { this.type = type; this.detail = options?.detail } }
globalThis.WebSocket = FakeWebSocket

console.log('INFO loading actual net consumer through focused Vite SSR')
const vite = await createServer({ root: front, cacheDir: cache, configFile: false, appType: 'custom',
  logLevel: 'silent', server: { middlewareMode: true }, optimizeDeps: { noDiscovery: true } })
try {
  console.log('INFO focused Vite SSR ready')
  const net = await vite.ssrLoadModule('/src/l12/net.ts')
  console.log('INFO actual net module loaded')
  const connected = net.connect()
  const socket = FakeWebSocket.instances[0]
  socket.open()
  socket.receive({ type: 'session', accountId: 'account-a', sessionId: 'tutorial-session', name: '教学玩家',
    tutorialAvailable: true, connectionGeneration: 21, capabilities: { requestIds: true, deltaGameState: true } })
  socket.receive({ type: 'recoveryComplete', connectionGeneration: 21 })
  await connected
  console.log('INFO actual net handshake complete')
  check('actual net consumer sends exact create request and releases every tutorial rejection by matching id', () => {
    for (const type of ['tutorialUnavailable', 'tutorialRequestExpired', 'tutorialRestartRequired', 'tutorialRejected']) {
      const requestId = net.createTutorial('deploy')
      const sent = JSON.parse(socket.sent.at(-1))
      assert.deepEqual(Object.keys(sent).sort(), ['lessonId', 'requestId', 'type'])
      assert.deepEqual(sent, { type: 'createTutorial', lessonId: 'deploy', requestId })
      assert.equal(net.l12State.pendingAction, true)
      socket.receive({ type, requestId: 'stale-request', message: '迟到失败' })
      assert.equal(net.l12State.pendingAction, true, `${type} stale response must not release current request`)
      assert.equal(net.l12State.room, null); assert.equal(net.l12State.game, null)
      socket.receive({ type, requestId, message: `可重试 ${type}` })
      assert.equal(net.l12State.pendingAction, false)
      assert.equal(net.l12State.notice, `可重试 ${type}`)
    }
  })
  check('actual sandboxForbidden consumer releases matching actions without discarding room identity', () => {
    net.l12State.gmEnabled = true
    net.l12State.room = { roomCode: 'SANDBX', sandbox: true }
    net.l12State.game = { matchId: 'sandbox-match', roomCode: 'SANDBX', revision: 1, phase: 'Main', players: [] }
    net.sandboxAction(0, { type: 'endTurn' })
    const request = JSON.parse(socket.sent.at(-1))
    socket.receive({ type: 'sandboxForbidden', requestId: request.requestId, message: '沙盒权限已失效' })
    assert.equal(net.l12State.pendingAction, false)
    assert.equal(net.l12State.room.roomCode, 'SANDBX')
    assert.equal(net.l12State.game.matchId, 'sandbox-match')
  })
  check('actual permission rejection releases response settings without claiming a durable change', () => {
    net.l12State.responsePreference = { confirmedMode: 'valid-only', syncPending: true }
    const requestId = net.setResponsePreference('default')
    socket.receive({ type: 'responsePreferenceRejected', requestId, message: '操作权限已变化' })
    assert.deepEqual(net.l12State.responsePreference, { confirmedMode: 'valid-only', syncPending: true })
    assert.deepEqual(net.l12State.responsePreferenceResult, {
      confirmedMode: 'valid-only', syncPending: true, matchApplied: false, accountSynced: false, requestId,
    })
    assert.equal(net.l12State.notice, '操作权限已变化')
    const dock = fs.readFileSync(path.join(front, 'src/l12/game/BattleUtilityDock.vue'), 'utf8')
    assert.match(dock, /responseRequestId\.value && result\.requestId !== responseRequestId\.value/)
    assert.match(dock, /responseDraft\.value = result\.confirmedMode[\s\S]*busy\.value = false[\s\S]*responseRequestId\.value = ''/)
  })
  check('actual gameState consumer disables GM/referee on tutorial conflict', () => {
    net.l12State.room = { roomCode: 'TUTOR2', started: true, players: [], tutorial: { lessonId: 'move', version: 1, stepId: 'move', status: 'active' } }
    socket.receive({ type: 'gameState', gmEnabled: true, refereeControlsEnabled: true, spectating: false,
      tutorial: { lessonId: 'move', version: 1, stepId: 'move', status: 'active' },
      state: { matchId: 'tutorial-conflict', roomCode: 'TUTOR2', revision: 1, phase: 'Main', players: [] } })
    assert.equal(net.l12State.gmEnabled, false)
    assert.equal(net.l12State.refereeControlsEnabled, false)
    assert.equal(net.l12State.tutorial, null)
  })
  check('replacement session clears tutorial binding without clearing same-account observation fences', () => {
    net.l12State.tutorial = projection({ status: 'active', sessionId: 'tutorial-session', connectionGeneration: 21 })
    socket.receive({ type: 'session', accountId: 'account-a', sessionId: 'replacement-session', name: '教学玩家',
      tutorialAvailable: true, connectionGeneration: 22, capabilities: { requestIds: true, deltaGameState: true } })
    assert.equal(net.l12State.tutorial, null)
    assert.equal(net.l12State.sessionId, 'replacement-session')
    assert.equal(net.l12State.tutorialAvailable, true)
    assert.match(source.net, /if \(l12State\.accountId !== nextAccountId\) retiredObservation = null/)
  })
  net.disconnect()
} finally { await vite.close() }

const report = { status: 'GREEN', count: cases.length, cases,
  sourceSha256: Object.fromEntries(Object.entries(files).map(([key, relative]) =>
    [key, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, relative))).digest('hex').toUpperCase()])),
  boundaries: ['Focused Node/Vite SSR consumer test; no browser viewport claim.', 'Synthetic socket messages; no server acknowledgement claim.'] }
fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n')
console.log(`Tutorial focused contracts: ${cases.length} passed`)
