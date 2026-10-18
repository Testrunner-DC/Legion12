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
  course: 'src/l12/tutorial/tutorialCourse.ts', guide: 'src/l12/tutorial/tutorialGuidance.ts',
  router: 'src/router/index.ts', admin: 'src/l12/site/adminSections.ts', sandbox: 'src/l12/site/SandboxPage.vue',
  script: 'scripts/test-tutorial-contract.mjs',
}
const source = Object.fromEntries(Object.entries(files).map(([key, relative]) =>
  [key, fs.readFileSync(path.join(front, relative), 'utf8')]))
const cases = []
function check(name, work) { work(); cases.push(name); console.log(`PASS ${name}`) }

check('one continuous first chapter opens inline without a second directory link', () => {
  assert.match(source.hub, /<TutorialPage v-else embedded\s*\/>/)
  assert.doesNotMatch(source.hub + source.page, /进入教学目录|lesson-grid|开始本关|三关可任选/)
  for (const chapter of ['初识十二军团', '卡牌效果与响应', '完成一场对局']) assert.ok(source.course.includes(chapter))
  assert.match(source.page, /尚未开放/)
  assert.match(source.page, /questionOpen \? introduction\.answer : introduction\.text/)
  assert.match(source.page, /明白了，回到主线/)
})
check('introductions explain goals, card types, resource origins and tradeoffs in Legion12 terms', () => {
  for (const text of ['血量降至0', '主宰', '军团', '战术', '圣物', '手牌从哪里来', '牌库顶部抽1张',
    '先攻玩家第一回合不抽', '士气牌库', '追加2张', '追加1张', '重置阶段', '费用', '兵力', '代价']) assert.ok(source.course.includes(text), text)
  assert.doesNotMatch(source.page + source.course + source.overlay + source.guide, /部署|法力|召唤/)
  const template = parse(source.overlay, { filename: files.overlay }).descriptor.template.content
  assert.doesNotMatch(template, /\{\{[^}]*\b(?:instanceId|cardInstanceId|lessonId|stepId)\b/)
  assert.match(source.page, /<CardImage :card-id="card\.id"/)
})
check('guide stays on the real board with inline questions, readable dialogue and non-intercepting focus', () => {
  for (const text of ['继续主线', '回到主线']) assert.ok(source.overlay.includes(text))
  const template = parse(source.overlay, { filename: files.overlay }).descriptor.template.content
  assert.doesNotMatch(template, /\{\{\s*tutorial\.(?:lessonId|stepId)|服务器权威|不会通关|判定说明/)
  assert.match(source.overlay, /pointer-events:\s*none/)
  assert.match(source.overlay, /44px/)
  assert.ok(source.overlay.includes('<BattleOverlayPortal>'), 'Expanded teaching copy must not be squeezed into the narrow action lane')
  assert.match(source.overlay, /continueMainline/)
  assert.match(source.game, /@continue-mainline="continueTutorialMainline"/)
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
  assert.doesNotMatch(source.game, /recordTutorialCompletion\(/)
  assert.match(source.game, /recordGuidedSceneReview/)
  assert.match(source.game, /tutorialHandoffDecision/)
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

check('legacy action completions cannot promote the new conversation-led chapter', () => {
  const storage = new MemoryStorage()
  progress.recordTutorialCompletion(projection({ accountId: 'legacy-account' }), binding({ accountId: 'legacy-account' }), storage)
  assert.deepEqual(progress.loadGuidedTutorialProgress('legacy-account', storage), { version: 2, introductionRead: false, reviewedScenes: [] })
  assert.equal(progress.recordGuidedSceneReview(projection({ accountId: 'legacy-account' }), binding({ accountId: 'legacy-account' }), storage), false)
})
check('guided learning requires introduction, authoritative result, review and ordered scenes', () => {
  const storage = new MemoryStorage(), accountId = 'guided-account'
  const view = extra => projection({ accountId, ...extra }), owner = extra => binding({ accountId, ...extra })
  assert.equal(progress.markGuidedIntroductionRead(accountId, storage), true)
  assert.equal(progress.recordGuidedSceneReview(view({ status: 'active' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({ stepId: 'deploy' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({ lessonId: 'move' }), owner(), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ connectionGeneration: 99 }), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ sessionId: 'new-session' }), storage), false)
  assert.equal(progress.recordGuidedSceneReview(view({}), owner({ matchId: 'wrong-match' }), storage), false)
  for (const lessonId of ['deploy', 'move', 'combat']) assert.equal(progress.recordGuidedSceneReview(view({ lessonId }), owner(), storage), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage).reviewedScenes, ['deploy', 'move', 'combat'])
  assert.deepEqual(progress.loadGuidedTutorialProgress('another-guided-account', storage).reviewedScenes, [])
  assert.equal(progress.restartGuidedTutorial(accountId, storage), true)
  assert.deepEqual(progress.loadGuidedTutorialProgress(accountId, storage), { version: 2, introductionRead: false, reviewedScenes: [] })
})
check('unsaved learning stays bounded to this account and does not overwrite unreadable storage', () => {
  let writes = 0
  const denied = { getItem() { throw Error('denied') }, setItem() { writes++; assert.fail('unreadable key cannot be overwritten') } }
  assert.equal(progress.markGuidedIntroductionRead('unsaved-account', denied), false)
  assert.equal(progress.recordGuidedSceneReview(projection({ accountId: 'unsaved-account' }), binding({ accountId: 'unsaved-account' }), denied), false)
  assert.deepEqual(progress.loadGuidedTutorialProgress('unsaved-account', denied).reviewedScenes, ['deploy'])
  assert.equal(progress.loadGuidedTutorialProgress('other-unsaved-account', denied).introductionRead, false)
  assert.equal(writes, 0)
  const quota = { getItem() { return null }, setItem() { throw Error('quota') } }
  assert.equal(progress.markGuidedIntroductionRead('quota-account', quota), false)
  assert.equal(progress.loadGuidedTutorialProgress('quota-account', quota).introductionRead, true)
})
check('malformed or out-of-order chapter records cannot claim all learning is done', () => {
  const storage = new MemoryStorage()
  storage.setItem(progress.guidedTutorialStorageKey('malformed-account'), JSON.stringify({ version: 2, introductionRead: true, reviewedScenes: ['deploy', 'combat', 'move'] }))
  assert.deepEqual(progress.loadGuidedTutorialProgress('malformed-account', storage).reviewedScenes, ['deploy'])
  const mutable = progress.loadGuidedTutorialProgress('malformed-account', storage)
  mutable.reviewedScenes.push('move', 'combat')
  assert.deepEqual(progress.loadGuidedTutorialProgress('malformed-account', storage).reviewedScenes, ['deploy'])
})
const course = await import('data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source.course, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText).toString('base64'))
check('new practice waits for actual roomLeft and fences account, token, generation and unrelated rooms', () => {
  const expected = { accountId: 'a', token: 'token-a', sessionId: 's', connectionGeneration: 9, roomCode: 'old-room', matchId: 'old-match' }
  const cleared = { accountId: 'a', token: 'token-a', connectedAccountId: 'a', sessionId: 's', connectionGeneration: 9,
    online: true, available: true, roomCode: null, matchId: null, leaving: false, pending: false }
  assert.equal(course.tutorialHandoffDecision(expected, cleared), 'ready')
  for (const extra of [{ roomCode: 'old-room' }, { matchId: 'old-match' }, { leaving: true }, { pending: true }])
    assert.equal(course.tutorialHandoffDecision(expected, { ...cleared, ...extra }), 'waiting')
  for (const extra of [{ accountId: 'b' }, { connectedAccountId: 'b' }, { token: 'new-token' }, { sessionId: 'other' },
    { connectionGeneration: 10 }, { online: false }, { available: false }, { roomCode: 'unrelated' }, { matchId: 'unrelated' }])
    assert.equal(course.tutorialHandoffDecision(expected, { ...cleared, ...extra }), 'cancelled')
})
check('hand and morale sources are separate concepts with real first-player exceptions', () => {
  const ids = course.tutorialIntroduction.map(step => step.id)
  assert.ok(ids.indexOf('goal') < ids.indexOf('legion'))
  assert.ok(ids.indexOf('hand') < ids.indexOf('practice') && ids.indexOf('morale') < ids.indexOf('practice'))
  assert.deepEqual(course.tutorialScenes, ['deploy', 'move', 'combat'])
  for (const step of course.tutorialIntroduction) assert.ok(step.title && step.text && step.question && step.answer)
  assert.match(course.tutorialIntroduction.find(step => step.id === 'hand').flow, /牌库.*手牌/)
  assert.match(course.tutorialIntroduction.find(step => step.id === 'morale').answer, /追加新士气.*恢复已有士气/)
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
