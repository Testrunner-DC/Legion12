import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { nextTick, reactive, watch } from 'vue'

const helperSource = readFileSync(new URL('../src/l12/gameReentry.ts', import.meta.url), 'utf8')
const netSource = readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8')
const appSource = readFileSync(new URL('../src/App.vue', import.meta.url), 'utf8')
const helperJavaScript = ts.transpileModule(helperSource, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
const { createGameReentryController } = await import(`data:text/javascript;base64,${Buffer.from(helperJavaScript).toString('base64')}`)

const reportedGame = {
  matchId: 'synthetic-reentry-regression',
  revision: 3,
}

class FakeRouter {
  route
  replacements = []
  listeners = new Set()
  failNextReplacement = false

  constructor(path = '/battle', replay = false) {
    this.route = { path, fullPath: path, replay }
  }

  currentRoute() { return { ...this.route } }
  afterEach(listener) { this.listeners.add(listener); return () => this.listeners.delete(listener) }
  navigate(path, replay = false) {
    this.route = { path, fullPath: path, replay }
    for (const listener of [...this.listeners]) listener()
  }
  async enterGame() {
    this.replacements.push('/game')
    if (this.failNextReplacement) {
      this.failNextReplacement = false
      throw new Error('simulated concurrent navigation cancellation')
    }
    this.navigate('/game')
  }
}

function state(overrides = {}) {
  return {
    status: 'connecting',
    recoveryPhase: 'opening-websocket',
    connectionGeneration: 25,
    game: null,
    leavingRoom: false,
    ...overrides,
  }
}

async function reproduceMissingRouteDependency() {
  const connection = reactive({ game: null, recoveryPhase: 'opening-websocket', leavingRoom: false })
  const route = reactive({ path: '/battle', replay: false })
  const stop = watch(() => [connection.game, connection.recoveryPhase], ([game, recoveryPhase]) => {
    if (game && recoveryPhase === 'snapshot-acknowledged' && !connection.leavingRoom
      && route.path !== '/game' && !route.replay) route.path = '/game'
  })

  connection.game = reportedGame
  connection.recoveryPhase = 'snapshot-received'
  await nextTick()
  connection.recoveryPhase = 'snapshot-acknowledged'
  await nextTick()
  assert.equal(route.path, '/game', 'the state watcher enters after the ack transition')

  route.path = '/battle'
  await nextTick()
  assert.equal(route.path, '/battle', 'route-only transitions do not wake the legacy state watcher')

  connection.game = { ...reportedGame }
  await nextTick()
  assert.equal(route.path, '/game', 'a duplicate same-match snapshot makes the legacy watcher bypass exit suppression')
  stop()
}

async function authoritativeAckAndRouteEventsBothReconcile() {
  const router = new FakeRouter('/battle')
  const controller = createGameReentryController(async () => router)

  await controller.update(state({ recoveryPhase: 'session-claimed' }))
  await controller.update(state({ recoveryPhase: 'snapshot-received', game: reportedGame }))
  assert.deepEqual(router.replacements, [], 'a game snapshot must not enter before recoveryComplete')

  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame }))
  await controller.settle()
  assert.equal(router.route.path, '/game')
  assert.deepEqual(router.replacements, ['/game'])

  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame }))
  await controller.settle()
  assert.deepEqual(router.replacements, ['/game'], 'a duplicate recovery ack must not navigate twice')

  router.navigate('/battle')
  await controller.settle()
  assert.equal(router.route.path, '/game', 'a later /battle route event must re-enter the acknowledged match')
  assert.deepEqual(router.replacements, ['/game', '/game'])
  controller.dispose()
}

async function replayDisconnectAndExplicitExitStayOut() {
  const replayRouter = new FakeRouter('/battle/records/replay/example', true)
  const replayController = createGameReentryController(async () => replayRouter)
  await replayController.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame }))
  assert.deepEqual(replayRouter.replacements, [], 'replay routes must not be replaced by a live game')
  replayController.dispose()

  const router = new FakeRouter('/battle')
  const controller = createGameReentryController(async () => router)
  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame }))
  await controller.requestExit(reportedGame.matchId)
  router.navigate('/battle')
  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: { ...reportedGame } }))
  await controller.settle()
  assert.equal(router.route.path, '/battle', 'the explicit exit intent must suppress a duplicate same-match snapshot')

  await controller.update(state({ status: 'offline', recoveryPhase: 'disconnected', game: reportedGame }))
  router.navigate('/news')
  await controller.settle()
  assert.equal(router.route.path, '/news', 'a stale disconnected snapshot must not drive navigation')

  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: { ...reportedGame } }))
  await controller.settle()
  assert.equal(router.route.path, '/news', 'same-match recovery ack must preserve explicit exit suppression')

  const nextGame = { matchId: 'next-authoritative-match', revision: 1 }
  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: nextGame }))
  await controller.settle()
  assert.equal(router.route.path, '/game', 'a different acknowledged match clears the old exit suppression')
  controller.dispose()
}

async function cancelledNavigationRetriesOnTheNextRouteEvent() {
  const router = new FakeRouter('/battle')
  router.failNextReplacement = true
  const controller = createGameReentryController(async () => router)
  await controller.update(state({ status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame }))
  assert.equal(router.route.path, '/battle')
  assert.equal(router.replacements.length, 1)

  router.navigate('/battle')
  await controller.settle()
  assert.equal(router.route.path, '/game')
  assert.equal(router.replacements.length, 2, 'the route completion stream must retry a cancelled initial entry')
  controller.dispose()
}

async function disconnectWinsWhileTheRouterIsStillLoading() {
  const router = new FakeRouter('/battle')
  let releaseRouter
  const delayedRouter = new Promise(resolve => { releaseRouter = resolve })
  const controller = createGameReentryController(() => delayedRouter)
  const acknowledged = controller.update(state({
    status: 'online', recoveryPhase: 'snapshot-acknowledged', game: reportedGame,
  }))
  await Promise.resolve()
  const disconnected = controller.update(state({
    status: 'offline', recoveryPhase: 'disconnected', game: reportedGame,
  }))
  releaseRouter(router)
  await Promise.all([acknowledged, disconnected])
  await controller.settle()
  assert.deepEqual(router.replacements, [], 'a delayed router load must re-check the latest connection state')
  controller.dispose()
}

function singleIntegrationEntryIsLocked() {
  assert.doesNotMatch(appSource, /useRouter|router\.(?:push|replace)\(['"]\/game['"]\)/,
    'App must not restore a second automatic game navigation path')
  assert.match(netSource, /createGameReentryController/)
  assert.match(netSource, /enterGame:\s*async\s*\(\)\s*=>\s*\{\s*await router\.replace\(['"]\/game['"]\)/)
  assert.match(netSource, /export const returnToRoom[\s\S]*?gameReentry\.requestExit\(l12State\.game\?\.matchId\)/)
  assert.match(netSource, /export const leaveRoom[\s\S]*?gameReentry\.requestExit\(l12State\.game\?\.matchId\)/)
}

await reproduceMissingRouteDependency()
await authoritativeAckAndRouteEventsBothReconcile()
await replayDisconnectAndExplicitExitStayOut()
await cancelledNavigationRetriesOnTheNextRouteEvent()
await disconnectWinsWhileTheRouterIsStillLoading()
singleIntegrationEntryIsLocked()
console.log('L12 game reentry behavior: 6/6 passed')
