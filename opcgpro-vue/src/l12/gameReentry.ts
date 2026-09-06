export interface GameReentrySnapshot {
  status: 'offline' | 'connecting' | 'online'
  recoveryPhase: string
  connectionGeneration: number
  game: null | { matchId: string; revision: number }
  leavingRoom: boolean
}

export interface GameReentryRoute {
  path: string
  fullPath: string
  replay: boolean
}

export interface GameReentryRouter {
  currentRoute(): GameReentryRoute
  enterGame(): Promise<void>
  afterEach(listener: () => void): () => void
}

export interface GameReentryController {
  update(snapshot: GameReentrySnapshot): Promise<void>
  requestExit(matchId?: string): Promise<void>
  settle(): Promise<void>
  dispose(): void
}

export function shouldReenterGame(snapshot: GameReentrySnapshot, route: GameReentryRoute, exitMatchId = '') {
  return snapshot.status === 'online'
    && snapshot.recoveryPhase === 'snapshot-acknowledged'
    && Boolean(snapshot.game?.matchId)
    && snapshot.game?.matchId !== exitMatchId
    && !snapshot.leavingRoom
    && route.path !== '/game'
    && !route.replay
}

/**
 * Reconciles the authoritative connection state with the current SPA route.
 *
 * State delivery and route completion are independent event streams. Observing
 * only the state stream misses a later browser-back/initial-navigation event,
 * so the coordinator deliberately subscribes to both streams.
 */
export function createGameReentryController(loadRouter: () => Promise<GameReentryRouter>): GameReentryController {
  let snapshot: GameReentrySnapshot | null = null
  let exitMatchId = ''
  let router: GameReentryRouter | null = null
  let routerPromise: Promise<GameReentryRouter> | null = null
  let removeRouteListener: (() => void) | null = null
  let disposed = false
  let work: Promise<void> = Promise.resolve()

  async function ensureRouter() {
    if (router) return router
    if (!routerPromise) {
      routerPromise = loadRouter().then(loaded => {
        if (disposed) return loaded
        router = loaded
        removeRouteListener = loaded.afterEach(() => { void enqueueReconcile() })
        return loaded
      }).catch(error => {
        routerPromise = null
        throw error
      })
    }
    return routerPromise
  }

  async function reconcile() {
    const candidate = snapshot
    if (disposed || !candidate || candidate.status !== 'online'
      || candidate.recoveryPhase !== 'snapshot-acknowledged' || !candidate.game?.matchId
      || candidate.leavingRoom || candidate.game.matchId === exitMatchId) return
    try {
      const activeRouter = await ensureRouter()
      const latest = snapshot
      if (disposed || !latest || !shouldReenterGame(latest, activeRouter.currentRoute(), exitMatchId)) return
      await activeRouter.enterGame()
    } catch {
      // A concurrent/initial navigation may cancel this attempt. The next state
      // delivery or afterEach route event deterministically retries reconciliation.
    }
  }

  function enqueueReconcile() {
    const queued = work.then(reconcile, reconcile)
    work = queued.catch(() => undefined)
    return queued
  }

  return {
    update(next) {
      snapshot = { ...next, game: next.game ? { ...next.game } : null }
      if (!snapshot.game) exitMatchId = ''
      else if (exitMatchId && snapshot.game.matchId !== exitMatchId) exitMatchId = ''
      return enqueueReconcile()
    },
    requestExit(matchId = snapshot?.game?.matchId) {
      if (matchId) exitMatchId = matchId
      return enqueueReconcile()
    },
    async settle() {
      let observed: Promise<void>
      do {
        observed = work
        await observed
      } while (observed !== work)
    },
    dispose() {
      disposed = true
      removeRouteListener?.()
      removeRouteListener = null
      router = null
      routerPromise = null
    },
  }
}
