import { reactive } from 'vue'
import type { ActiveTutorialState, GameState, RankedClockView, RoomState, TutorialLessonId, TutorialProjection } from './types'
import type { SavedL12Deck } from './decks'
import type { EffectiveOperationsPolicy, PlatformPresence, RankedSettlement } from './platform'
import type { MatchGovernanceResult } from './matchGovernance'
import { createGameReentryController } from './gameReentry'
import { deploymentStorageKey, deploymentWebSocketPath, endpointHttpBase } from './deploymentBase'
import { chatResource } from './chatResource'
import { GameStateCardFrameRecovery, restoreGameStateCardFrame } from './gameStateCardFrame'
import { HAND_ARRAY_DELTA_CAPABILITY, restoreHandSplices, validHandDeltaChangePath } from './handArrayDelta'

export type L12RecoveryPhase = 'idle' | 'opening-websocket' | 'authenticating' | 'session-claimed'
  | 'snapshot-received' | 'snapshot-mismatch' | 'snapshot-acknowledged' | 'authentication-rejected'
  | 'superseded' | 'disconnected'
export type L12ConnectionIssue = 'none' | 'http' | 'websocket' | 'authentication' | 'maintenance' | 'superseded'
export type ResponseMode = 'default' | 'valid-only' | 'invalid-five-seconds'
export interface ResponsePreferenceState { confirmedMode: ResponseMode; syncPending: boolean }
export interface ResponsePreferenceResult extends ResponsePreferenceState {
  matchApplied: boolean
  accountSynced: boolean
  requestId?: string
}
export interface BugClientConnectionDiagnostic {
  capturedAt: string
  currentRoute: string
  httpStatus: string
  httpStatusCode?: number
  apiStatus: string
  apiStatusCode?: number
  webSocketReadyState: string
  closeCode?: number
  closeReason?: string
  lastHeartbeatAt?: string
  lastPongAt?: string
  retryCount: number
  roomCode?: string
  matchId?: string
  connectionGeneration?: number
  recoveryPhase: string
  authenticationState: string
  maintenanceState: string
}

function normalizeEndpoint(value: string) {
  return value.trim().replace(/\/ws\/$/, '/ws')
}

const configuredEndpoint = String(import.meta.env.VITE_WS_URL || '').trim()
const defaultEndpoint = configuredEndpoint || (location.protocol === 'https:'
  ? `wss://${location.host}${deploymentWebSocketPath()}`
  : `ws://${location.hostname || 'localhost'}:8080/ws`)
const storedEndpoint = localStorage.getItem(deploymentStorageKey('l12-endpoint')) || ''
// 正式 HTTPS 页面必须跟随当前域名，避免历史调试地址在部署后把玩家永久带到旧服务。
const initialEndpoint = location.protocol === 'https:' && !configuredEndpoint
  ? defaultEndpoint
  : (storedEndpoint || defaultEndpoint)

export const CONNECTION_HANDSHAKE_TIMEOUT_MS = 10_000
export const SNAPSHOT_RECOVERY_MAX_ATTEMPTS = 4
const CARD_FRAME_PLAIN_RECOVERY_TIMEOUT_MS = 10_000
const MATCHMAKING_INITIAL_STATE_GRACE_MS = 800
let connectPromise: Promise<void> | null = null
let cancelPendingConnect: ((reason?: string) => void) | null = null
let connectionAttemptSerial = 0
let reconnectTimer: ReturnType<typeof setTimeout> | null = null
let heartbeatTimer: ReturnType<typeof setInterval> | null = null
let unansweredHeartbeats = 0
let matchmakingPollTimer: ReturnType<typeof setInterval> | null = null
let matchmakingRecoveryTimer: ReturnType<typeof setTimeout> | null = null
let snapshotRecoveryTimer: ReturnType<typeof setTimeout> | null = null
let snapshotRecoveryAttempt = 0
let reconnectAttempts = 0
let automaticConnectionEnabled = false
let negotiatedRequestIds = false
let negotiatedEventWindowDelta = false
let negotiatedHandArrayDelta = false
let negotiatedPresenceDelta = false
let negotiatedPresenceOrderReuse = false
let negotiatedGameStateCardFrame = false
let presenceRecoveryPending = false
let presenceBaselineEpoch = ''
let gameStateCardFrameRecoveryTimer: ReturnType<typeof setTimeout> | null = null
const gameStateCardFrameRecovery = new GameStateCardFrameRecovery()
let pendingActionEnvelope: null | Record<string, unknown> = null
let pendingActionResentAttempt = -1
let lastGameStateEnvelope: any = null
let retiredObservation: null | { roomCode: string; matchId: string; assignedMatchId: string } = null
let resourceFallbackTimer: ReturnType<typeof setTimeout> | null = null
let resourceFallbackLastAt = 0
const resourceNames = ['friends', 'rankedIntegrity', 'alternateArtNotifications', 'seasonSummaryNotifications', 'operationsPolicy', 'presence', 'rulesContent', 'tournaments'] as const
const fallbackResourceNames = resourceNames.filter(resource => resource !== 'rulesContent')

function dispatchResourceChange(resource: string, detail: Record<string, unknown> = {}) {
  const eventDetail = { resource, ...detail }
  window.dispatchEvent(new CustomEvent('l12-resource-changed', { detail: eventDetail }))
  window.dispatchEvent(new CustomEvent(`l12-resource-${resource}`, { detail: eventDetail }))
}

function acceptResourceRevision(resource: string, epoch: unknown, revision: unknown, allowEqual = false) {
  const nextEpoch = String(epoch || '')
  if (nextEpoch && nextEpoch !== l12State.resourceEpoch) {
    l12State.resourceEpoch = nextEpoch
    l12State.resourceRevisions = {}
    presenceBaselineEpoch = ''
  }
  const nextRevision = Number(revision || 0)
  const current = Number(l12State.resourceRevisions[resource] || 0)
  if (nextRevision < current || (!allowEqual && nextRevision === current && current !== 0)) return false
  l12State.resourceRevisions[resource] = nextRevision
  return true
}

function requestPresenceRecovery(socket: WebSocket) {
  if (presenceRecoveryPending || socket !== l12State.socket || socket.readyState !== WebSocket.OPEN) return
  presenceRecoveryPending = true
  socket.send(JSON.stringify({ type: 'syncPresence' }))
}

function validPresenceItem(item: any) {
  return item !== null && typeof item === 'object' && !Array.isArray(item)
    && typeof item.accountId === 'string' && item.accountId.length > 0
    && typeof item.username === 'string'
    && typeof item.online === 'boolean'
    && typeof item.activity === 'string'
    && (item.roomCode === null || typeof item.roomCode === 'string')
    && typeof item.canInvite === 'boolean'
    && typeof item.canSpectate === 'boolean'
    && (item.actionReason === null || typeof item.actionReason === 'string')
    && typeof item.friendStatus === 'string'
    && typeof item.friendDirection === 'string'
}

function validPresenceItems(items: any) {
  if (!Array.isArray(items)) return false
  const accountIds = new Set<string>()
  for (const item of items) {
    if (!validPresenceItem(item) || accountIds.has(item.accountId)) return false
    accountIds.add(item.accountId)
  }
  return true
}

function validPresenceIdentity(message: any) {
  return message?.resource === 'presence'
    && typeof message.epoch === 'string' && message.epoch.length > 0
    && typeof message.revision === 'number' && Number.isSafeInteger(message.revision)
    && message.revision >= 0
}

function rebuildPresenceDelta(message: any) {
  if (!negotiatedPresenceDelta || presenceRecoveryPending || !validPresenceIdentity(message)
    || !presenceBaselineEpoch) return null
  const epoch = message.epoch
  const baseRevision = message.baseRevision
  const revision = message.revision
  const currentRevision = Number(l12State.resourceRevisions.presence || 0)
  if (epoch !== l12State.resourceEpoch || epoch !== presenceBaselineEpoch
    || typeof baseRevision !== 'number' || !Number.isSafeInteger(baseRevision) || baseRevision < 0
    || baseRevision !== currentRevision || revision <= baseRevision
    || !Array.isArray(message.removedAccountIds) || !Array.isArray(message.upserts)
    || !Array.isArray(l12State.presence)) return null
  const hasOrderFromBaseline = Object.prototype.hasOwnProperty.call(message, 'orderFromBaseline')
  const orderFromBaseline = hasOrderFromBaseline && message.orderFromBaseline === true
  if (hasOrderFromBaseline && !orderFromBaseline) return null
  if (orderFromBaseline
    ? !negotiatedPresenceOrderReuse || Object.prototype.hasOwnProperty.call(message, 'order')
    : !Array.isArray(message.order)) return null

  const entries = new Map<string, any>()
  for (const item of l12State.presence) {
    const accountId = typeof item?.accountId === 'string' ? item.accountId : ''
    if (!accountId || entries.has(accountId)) return null
    entries.set(accountId, item)
  }
  const removed = new Set<string>()
  for (const value of message.removedAccountIds) {
    if (typeof value !== 'string' || !value || removed.has(value) || !entries.has(value)) return null
    removed.add(value)
  }
  const upserts = new Map<string, any>()
  for (const item of message.upserts) {
    if (!validPresenceItem(item)) return null
    const accountId = item.accountId
    if (removed.has(accountId) || upserts.has(accountId)) return null
    upserts.set(accountId, item)
  }
  // Explicit removals filter the previous order; an unseen identity needs a
  // complete order. Validate before exposing any reconstructed state.
  if (orderFromBaseline && [...upserts.keys()].some(id => !entries.has(id))) return null
  removed.forEach(accountId => entries.delete(accountId))
  upserts.forEach((item, accountId) => entries.set(accountId, item))
  const order = new Set<string>()
  const rebuilt = []
  for (const value of orderFromBaseline ? entries.keys() : message.order) {
    if (typeof value !== 'string' || !value || order.has(value) || !entries.has(value)) return null
    order.add(value)
    rebuilt.push(entries.get(value))
  }
  if (order.size !== entries.size) return null
  return { items: rebuilt, epoch, revision }
}

function cancelResourceFallback() {
  if (resourceFallbackTimer !== null) window.clearTimeout(resourceFallbackTimer)
  resourceFallbackTimer = null
}

function scheduleResourceFallback() {
  cancelResourceFallback()
  if (!automaticConnectionEnabled || !localStorage.getItem(deploymentStorageKey('l12-auth-token'))) return
  resourceFallbackTimer = window.setTimeout(() => {
    resourceFallbackTimer = null
    if (l12State.status === 'online' || !automaticConnectionEnabled) return
    if (typeof document === 'undefined' || !document.hidden) {
      resourceFallbackLastAt = Date.now()
      fallbackResourceNames.forEach(resource => dispatchResourceChange(resource, { fallback: true }))
    }
    scheduleResourceFallback()
  }, 60_000)
}

if (typeof document !== 'undefined') document.addEventListener('visibilitychange', () => {
  if (!document.hidden && l12State.status !== 'online' && Date.now() - resourceFallbackLastAt >= 60_000) {
    resourceFallbackLastAt = Date.now()
    fallbackResourceNames.forEach(resource => dispatchResourceChange(resource, { fallback: true }))
  }
})

function createActionRequestId() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}

const tutorialLessonIds = new Set<TutorialLessonId>(['deploy', 'move', 'combat', 'response', 'preparation', 'finale'])
function tutorialProjection(value: unknown): TutorialProjection | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const candidate = value as Partial<TutorialProjection>
  if (!tutorialLessonIds.has(candidate.lessonId as TutorialLessonId) || ![1, 2].includes(candidate.version as number)
    || typeof candidate.stepId !== 'string' || !candidate.stepId.trim() || candidate.stepId.length > 128
    || (candidate.status !== 'active' && candidate.status !== 'completed')) return null
  if (candidate.version === 2) {
    const focus = candidate.focus
    const keys = ['artifactInstanceId', 'counterInstanceId', 'costCardInstanceId', 'legionInstanceId'] as const
    if (candidate.lessonId !== 'response' || candidate.recipeId !== 'public-response-v1'
      || !focus || typeof focus !== 'object' || Array.isArray(focus)
      || keys.some(key => focus[key] !== null && (typeof focus[key] !== 'string'
        || !focus[key]!.trim() || focus[key]!.length > 128))) return null
    return { lessonId: 'response', version: 2, recipeId: 'public-response-v1',
      focus: Object.fromEntries(keys.map(key => [key, focus[key]])) as NonNullable<TutorialProjection['focus']>,
      stepId: candidate.stepId.trim(), status: candidate.status }
  }
  return { lessonId: candidate.lessonId as TutorialLessonId, version: 1,
    stepId: candidate.stepId.trim(), status: candidate.status }
}

function completePendingAction(responseRequestId?: unknown) {
  if (!pendingActionEnvelope) return
  if (negotiatedRequestIds
    && String(responseRequestId || '') !== String(pendingActionEnvelope.requestId || '')) return
  pendingActionEnvelope = null
  pendingActionResentAttempt = -1
  l12State.pendingAction = false
}

function cancelPendingAction() {
  pendingActionEnvelope = null
  pendingActionResentAttempt = -1
  l12State.pendingAction = false
}

function rebuildDeltaGameState(delta: any) {
  try {
  const baseline = lastGameStateEnvelope
  if (!baseline || baseline.type !== 'gameState'
    || String(baseline.state?.matchId || '') !== String(delta.matchId || '')
    || Number(baseline.state?.revision ?? -1) !== Number(delta.baseRevision ?? -2)
    || !Array.isArray(delta.changes)) return null
  const rebuilt = JSON.parse(JSON.stringify(baseline))
  const hasWindow = Object.hasOwn(delta, 'eventWindow')
  const hasHands = Object.hasOwn(delta, 'handSplices')
  if (hasHands && (!negotiatedHandArrayDelta || typeof delta.matchId !== 'string' || !delta.matchId
    || !Number.isSafeInteger(delta.baseRevision) || !Number.isSafeInteger(delta.revision))) return null
  if (hasWindow && !negotiatedEventWindowDelta) return null
  for (const change of delta.changes) {
    if (!Array.isArray(change?.path) || change.path.length === 0) return null
    if (hasHands && !validHandDeltaChangePath(rebuilt, change.path)) return null
    if (hasWindow && change.path[0] === 'state'
      && (change.path.length === 1 || change.path[1] === 'recentEvents')) return null
    let target: any = rebuilt
    for (const segment of change.path.slice(0, -1)) {
      if (target == null || !(segment in target)) return null
      target = target[segment]
    }
    const key = change.path.at(-1) as string | number
    if (change.remove) delete target[key]
    else target[key] = change.value
  }
  if (hasWindow) {
    // Rebuild the original recipient array before all animations/log/replay consumers.
    // Privacy gaps are legitimate; no consecutive numbering or live catalog refill.
    const window = delta.eventWindow
    const events = rebuilt.state?.recentEvents
    const sequences = (items: any): number[] | null => {
      if (!Array.isArray(items) || items.length > 128) return null
      const result: number[] = []
      for (const item of items) {
        const sequence = item?.sequence
        if (!item || typeof item !== 'object' || Array.isArray(item)
          || !Number.isSafeInteger(sequence) || sequence <= 0
          || (result.length > 0 && sequence <= result[result.length - 1]!)) return null
        result.push(sequence)
      }
      return result
    }
    const equals = (actual: number[] | null, expected: any) => actual !== null
      && Array.isArray(expected) && actual.length === expected.length
      && actual.every((value, index) => value === expected[index])
    if (!window || typeof window !== 'object' || Array.isArray(window)
      || !equals(sequences(events), window.baseSequences)
      || !Number.isSafeInteger(window.removeFirst) || window.removeFirst < 0
      || window.removeFirst >= events.length || sequences(window.append) === null) return null
    const next = [...events.slice(window.removeFirst), ...JSON.parse(JSON.stringify(window.append))]
    if (!equals(sequences(next), window.resultSequences)) return null
    rebuilt.state.recentEvents = next
  }
  if (Number(rebuilt.state?.revision ?? -1) !== Number(delta.revision ?? -2)) return null
  if (hasHands && (rebuilt.state?.matchId !== delta.matchId
    || !restoreHandSplices(baseline, rebuilt, delta.handSplices, delta.changes))) return null
  return rebuilt
  } catch {
    // Bad paths or shapes must request authoritative full recovery, not partially publish a window.
    return null
  }
}

function clearReconnectTimer() {
  if (reconnectTimer !== null) window.clearTimeout(reconnectTimer)
  reconnectTimer = null
}

function clearHeartbeat() {
  if (heartbeatTimer !== null) window.clearInterval(heartbeatTimer)
  heartbeatTimer = null
  unansweredHeartbeats = 0
}

function clearSnapshotRecovery() {
  if (snapshotRecoveryTimer !== null) window.clearTimeout(snapshotRecoveryTimer)
  snapshotRecoveryTimer = null
  snapshotRecoveryAttempt = 0
}

function clearGameStateCardFrameRecoveryTimer() {
  if (gameStateCardFrameRecoveryTimer !== null) window.clearTimeout(gameStateCardFrameRecoveryTimer)
  gameStateCardFrameRecoveryTimer = null
}

function resetGameStateCardFrameRecovery() {
  clearGameStateCardFrameRecoveryTimer()
  gameStateCardFrameRecovery.reset()
}

function requestPlainGameStateRecovery(socket: WebSocket, onExhausted: () => void) {
  const requestId = pendingActionEnvelope?.requestId
  if (!gameStateCardFrameRecovery.begin(requestId)) {
    // A second packed frame can race ahead of the server processing the first plain request.
    // Ignore only that bounded in-flight case; once recovery ended, packed data is a hard fault.
    if (!gameStateCardFrameRecovery.active) onExhausted()
    return
  }
  negotiatedGameStateCardFrame = false
  // The same request id remains authoritative even if this connection attempt had already resent it.
  pendingActionResentAttempt = -1
  clearSnapshotRecovery()
  try {
    socket.send(JSON.stringify({ type: 'syncState', plainFull: true }))
  } catch {
    onExhausted()
    return
  }
  gameStateCardFrameRecoveryTimer = window.setTimeout(() => {
    gameStateCardFrameRecoveryTimer = null
    if (socket === l12State.socket && gameStateCardFrameRecovery.active) onExhausted()
  }, CARD_FRAME_PLAIN_RECOVERY_TIMEOUT_MS)
}

function requestSnapshotRecovery(socket: WebSocket, onExhausted: () => void) {
  if (gameStateCardFrameRecovery.active) return
  if (snapshotRecoveryTimer !== null || socket !== l12State.socket || socket.readyState !== WebSocket.OPEN) return
  if (snapshotRecoveryAttempt >= SNAPSHOT_RECOVERY_MAX_ATTEMPTS) {
    onExhausted()
    return
  }
  socket.send(JSON.stringify({ type: 'syncState' }))
  const delay = Math.min(1000 * (2 ** snapshotRecoveryAttempt++), 8000)
  snapshotRecoveryTimer = window.setTimeout(() => {
    snapshotRecoveryTimer = null
    if (socket === l12State.socket && l12State.recoveryPhase === 'snapshot-mismatch')
      requestSnapshotRecovery(socket, onExhausted)
  }, delay)
}

function clearMatchmakingPolling() {
  if (matchmakingPollTimer !== null) window.clearInterval(matchmakingPollTimer)
  matchmakingPollTimer = null
}

function clearMatchmakingRecovery() {
  if (matchmakingRecoveryTimer !== null) window.clearTimeout(matchmakingRecoveryTimer)
  matchmakingRecoveryTimer = null
}

function isRetiredObservationMessage(message: any) {
  if (!retiredObservation) return false
  const matchId = message.type === 'gameStateDelta' ? message.matchId
    : message.type === 'gameStateCardFrameV1' ? message.payload?.state?.matchId
    : message.type === 'gameState' ? message.state?.matchId : ''
  return Boolean((retiredObservation.matchId && matchId === retiredObservation.matchId)
    || (retiredObservation.roomCode && ['roomState', 'roomLeft', 'roomClosed'].includes(message.type)
      && message.roomCode === retiredObservation.roomCode))
}

function retireObservationForMatch(message: any) {
  const roomCode = typeof message.retiredObservationRoomCode === 'string'
    ? message.retiredObservationRoomCode : l12State.spectating ? l12State.room?.roomCode || '' : ''
  const matchId = typeof message.retiredObservationMatchId === 'string'
    ? message.retiredObservationMatchId : l12State.spectating ? l12State.game?.matchId || '' : ''
  if ((!roomCode && !matchId) || retiredObservation?.assignedMatchId === message.matchId) return
  retiredObservation = { roomCode, matchId, assignedMatchId: String(message.matchId || '') }
  if (l12State.game) chatResource.retireOpponent(l12State.game.matchId)
  clearSnapshotRecovery()
  resetGameStateCardFrameRecovery()
  cancelPendingAction()
  lastGameStateEnvelope = null
  l12State.room = null
  l12State.game = null
  l12State.rankedClock = null
  l12State.rankedSettlement = null
  l12State.matchGovernanceResult = null
  l12State.spectating = false
  l12State.observerView = 'public'
  l12State.gmEnabled = false
  l12State.refereeControlsEnabled = false
  l12State.tutorial = null
  l12State.leavingRoom = false
}

function requestMatchedState(socket: WebSocket, onExhausted: () => void) {
  clearMatchmakingRecovery()
  if (socket !== l12State.socket || socket.readyState !== WebSocket.OPEN) return
  matchmakingRecoveryTimer = window.setTimeout(() => {
    matchmakingRecoveryTimer = null
    if (l12State.matchFound && !l12State.game && socket === l12State.socket && socket.readyState === WebSocket.OPEN) {
      l12State.recoveryPhase = 'snapshot-mismatch'
      l12State.notice = '匹配初始状态尚未到达，正在请求完整权威状态…'
      syncGameReentry()
      requestSnapshotRecovery(socket, onExhausted)
    }
  }, MATCHMAKING_INITIAL_STATE_GRACE_MS)
}

function startMatchmakingPolling() {
  if (matchmakingPollTimer !== null) return
  matchmakingPollTimer = window.setInterval(() => {
    if (l12State.matchmaking?.queued && l12State.socket?.readyState === WebSocket.OPEN)
      l12State.socket.send(JSON.stringify({ type: 'pollMatchmaking' }))
  }, 5_000)
}

function startHeartbeat(socket: WebSocket, onTimeout: () => void) {
  clearHeartbeat()
  heartbeatTimer = window.setInterval(() => {
    if (socket.readyState === WebSocket.OPEN) {
      if (unansweredHeartbeats >= 2) {
        clearHeartbeat()
        onTimeout()
        return
      }
      unansweredHeartbeats += 1
      l12State.lastHeartbeatAt = new Date().toISOString()
      socket.send(JSON.stringify({ type: 'ping' }))
    }
  }, 25_000)
}

function scheduleReconnect(reason = '') {
  if (!automaticConnectionEnabled || reconnectTimer !== null || !localStorage.getItem(deploymentStorageKey('l12-auth-token'))) return
  const delay = Math.min(1_000 * (2 ** reconnectAttempts), 15_000)
  reconnectAttempts += 1
  l12State.retryCount = reconnectAttempts
  l12State.recoveryPhase = 'disconnected'
  const detail = reason.trim().replace(/[。…]+$/, '')
  l12State.notice = detail
    ? `${detail}，${Math.ceil(delay / 1000)} 秒后自动重连…`
    : `连接中断，${Math.ceil(delay / 1000)} 秒后自动重连…`
  reconnectTimer = window.setTimeout(() => {
    reconnectTimer = null
    void connect().catch(() => undefined)
  }, delay)
}

export const l12State = reactive({
  socket: null as WebSocket | null,
  status: 'offline' as 'offline' | 'connecting' | 'online',
  nickname: localStorage.getItem(deploymentStorageKey('l12-nickname')) || '',
  endpoint: normalizeEndpoint(initialEndpoint),
  sessionId: '',
  room: null as RoomState | null,
  game: null as GameState | null,
  spectating: false,
  observerView: 'public' as 'public' | 'referee',
  leavingRoom: false,
  gmEnabled: false,
  refereeControlsEnabled: false,
  tutorialAvailable: false,
  tutorial: null as ActiveTutorialState | null,
  pendingAction: false,
  notice: '',
  operationsPolicy: null as EffectiveOperationsPolicy | null,
  accountId: '',
  presence: [] as PlatformPresence[],
  resourceEpoch: '',
  resourceRevisions: {} as Record<string, number>,
  friendInvitation: null as null | { invitationId: string; roomCode: string; fromAccountId: string; fromName: string },
  outgoingFriendInvitation: null as null | { invitationId: string; roomCode: string; targetAccountId: string },
  matchmaking: null as null | { queued: boolean; mode?: 'ranked' | 'casual'; joinedAt?: string },
  matchFound: null as null | { mode?: 'ranked' | 'casual'; roomCode: string; matchId?: string },
  rankedSettlement: null as RankedSettlement | null,
  rankedClock: null as RankedClockView | null,
  matchGovernanceResult: null as MatchGovernanceResult | null,
  responsePreference: { confirmedMode: 'default', syncPending: false } as ResponsePreferenceState,
  responsePreferenceResult: null as ResponsePreferenceResult | null,
  connectionGeneration: 0,
  recoveryPhase: 'idle' as L12RecoveryPhase,
  connectionIssue: 'none' as L12ConnectionIssue,
  lastHeartbeatAt: '' as string,
  lastPongAt: '' as string,
  lastCloseCode: null as number | null,
  lastCloseReason: '' as string,
  retryCount: 0,
})

const gameReentry = createGameReentryController(async () => {
  // 动态加载避免 net -> router -> platform -> net 的初始化环；恢复消息到达时路由已经安装。
  const { router } = await import('../router/index')
  return {
    currentRoute: () => ({
      path: router.currentRoute.value.path,
      fullPath: router.currentRoute.value.fullPath,
      replay: router.currentRoute.value.meta.replay === true,
    }),
    enterGame: async () => { await router.replace('/game') },
    afterEach: listener => router.afterEach(() => listener()),
  }
})

function syncGameReentry() {
  void gameReentry.update({
    status: l12State.status,
    recoveryPhase: l12State.recoveryPhase,
    connectionGeneration: l12State.connectionGeneration,
    game: l12State.game ? { matchId: l12State.game.matchId, revision: l12State.game.revision } : null,
    leavingRoom: l12State.leavingRoom,
  })
}

function socketReadyStateName(socket: WebSocket | null) {
  const state = socket?.readyState
  return state === WebSocket.CONNECTING ? 'connecting'
    : state === WebSocket.OPEN ? 'open'
      : state === WebSocket.CLOSING ? 'closing'
        : state === WebSocket.CLOSED ? 'closed' : 'absent'
}

function httpEndpoint(path: string) {
  try {
    return `${endpointHttpBase(l12State.endpoint)}${path}`
  } catch { return `${location.protocol}//${location.hostname}:8080${path}` }
}

async function diagnosticFetch(path: string, authenticated = false) {
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 3_000)
  try {
    const headers = new Headers()
    const token = authenticated ? localStorage.getItem(deploymentStorageKey('l12-auth-token')) : ''
    if (token) headers.set('Authorization', `Bearer ${token}`)
    const response = await fetch(httpEndpoint(path), { headers, signal: controller.signal, cache: 'no-store' })
    const payload = await response.json().catch(() => ({})) as { status?: string; maintenance?: boolean }
    return { reachable: true, statusCode: response.status, payload }
  } catch {
    return { reachable: false, statusCode: undefined, payload: {} as { status?: string; maintenance?: boolean } }
  } finally { window.clearTimeout(timeout) }
}

export async function captureBugClientDiagnostic(currentRoute: string): Promise<BugClientConnectionDiagnostic> {
  const [health, api] = await Promise.all([diagnosticFetch('/health'), diagnosticFetch('/api/auth/me', true)])
  const authenticationState = !localStorage.getItem(deploymentStorageKey('l12-auth-token')) ? 'missing-token'
    : api.statusCode === 200 ? 'authenticated'
      : api.statusCode === 401 ? 'rejected'
        : api.reachable ? 'unknown-response' : 'unreachable'
  const maintenanceActive = health.payload.maintenance === true || health.payload.status === 'maintenance'
  return {
    capturedAt: new Date().toISOString(),
    currentRoute,
    httpStatus: health.reachable ? (maintenanceActive ? 'maintenance' : 'ok') : 'unreachable',
    httpStatusCode: health.statusCode,
    apiStatus: api.reachable ? authenticationState : 'unreachable',
    apiStatusCode: api.statusCode,
    webSocketReadyState: socketReadyStateName(l12State.socket),
    closeCode: l12State.lastCloseCode ?? undefined,
    closeReason: l12State.lastCloseReason || undefined,
    lastHeartbeatAt: l12State.lastHeartbeatAt || undefined,
    lastPongAt: l12State.lastPongAt || undefined,
    retryCount: l12State.retryCount,
    roomCode: l12State.room?.roomCode,
    matchId: l12State.game?.matchId,
    connectionGeneration: l12State.connectionGeneration || undefined,
    recoveryPhase: l12State.recoveryPhase,
    authenticationState,
    maintenanceState: maintenanceActive ? 'active' : health.reachable ? 'inactive' : 'unknown',
  }
}

export function connect(): Promise<void> {
  if (l12State.socket?.readyState === WebSocket.OPEN && l12State.status === 'online') {
    syncGameReentry()
    return Promise.resolve()
  }
  if (connectPromise) return connectPromise
  const authToken = localStorage.getItem(deploymentStorageKey('l12-auth-token')) || ''
  if (!authToken) {
    l12State.notice = '请先登录账号'
    l12State.connectionIssue = 'authentication'
    l12State.recoveryPhase = 'authentication-rejected'
    syncGameReentry()
    return Promise.reject(new Error(l12State.notice))
  }
  automaticConnectionEnabled = true
  clearReconnectTimer()
  clearSnapshotRecovery()
  l12State.status = 'connecting'
  l12State.connectionIssue = 'none'
  l12State.recoveryPhase = 'opening-websocket'
  l12State.notice = ''
  syncGameReentry()
  l12State.endpoint = normalizeEndpoint(l12State.endpoint)
  localStorage.setItem(deploymentStorageKey('l12-endpoint'), l12State.endpoint)
  const attempt = ++connectionAttemptSerial
  const pending = new Promise<void>((resolve, reject) => {
    let socket: WebSocket
    let settled = false
    let failed = false
    let claimedGeneration: number | null = null
    let handshakeTimer: ReturnType<typeof setTimeout> | null = null

    const clearHandshakeTimer = () => {
      if (handshakeTimer !== null) window.clearTimeout(handshakeTimer)
      handshakeTimer = null
    }
    const isCurrentAttempt = () => connectionAttemptSerial === attempt && l12State.socket === socket
    const settle = (error?: Error) => {
      if (settled) return
      settled = true
      clearHandshakeTimer()
      if (cancelPendingConnect === cancel) cancelPendingConnect = null
      if (error) reject(error)
      else resolve()
    }
    const cancel = (reason = '连接已取消') => {
      failed = true
      settle(new Error(reason))
    }
    const failConnection = (reason: string) => {
      if (!isCurrentAttempt() || failed) return
      chatResource.disconnected()
      failed = true
      clearHeartbeat()
      clearSnapshotRecovery()
      resetGameStateCardFrameRecovery()
      negotiatedGameStateCardFrame = false
      l12State.status = 'offline'
      l12State.connectionIssue = 'websocket'
      l12State.notice = reason
      l12State.recoveryPhase = 'disconnected'
      syncGameReentry()
      settle(new Error(reason))
      scheduleReconnect(reason)
      socket.close(4000, 'websocket recovery required')
    }
    const replaceBrokenSnapshotConnection = () => {
      failConnection('权威状态多次同步仍未完成')
    }

    negotiatedEventWindowDelta = false
    negotiatedHandArrayDelta = false
    negotiatedGameStateCardFrame = false
    resetGameStateCardFrameRecovery()
    try { socket = new WebSocket(l12State.endpoint) }
    catch (error) {
      clearHeartbeat()
      l12State.socket = null
      l12State.status = 'offline'
      l12State.connectionIssue = 'websocket'
      l12State.recoveryPhase = 'disconnected'
      l12State.notice = '暂时无法连接服务器，正在自动重试。'
      syncGameReentry()
      scheduleReconnect()
      reject(error instanceof Error ? error : new Error(l12State.notice))
      return
    }
    cancelPendingConnect = cancel
    l12State.socket = socket
    handshakeTimer = window.setTimeout(() => {
      if (!isCurrentAttempt() || settled || failed) return
      failed = true
      clearHeartbeat()
      clearSnapshotRecovery()
      l12State.status = 'offline'
      l12State.connectionIssue = 'websocket'
      l12State.recoveryPhase = 'disconnected'
      l12State.notice = '连接握手超时，正在自动重试。'
      syncGameReentry()
      settle(new Error(l12State.notice))
      scheduleReconnect()
      socket.close(4000, 'handshake timeout')
    }, CONNECTION_HANDSHAKE_TIMEOUT_MS)
    socket.onopen = () => {
      if (!isCurrentAttempt() || failed) {
        socket.close(1000, 'stale connection attempt')
        return
      }
      l12State.recoveryPhase = 'authenticating'
      socket.send(JSON.stringify({
        type: 'hello',
        authToken,
        capabilities: { requestIds: true, deltaGameState: true, eventWindowDeltaV1: true, presenceDeltaV1: true,
          presenceOrderReuseV1: true, chatV1: true, gameStateCardFrameV1: true, [HAND_ARRAY_DELTA_CAPABILITY]: true },
      }))
    }
    socket.onmessage = (event) => {
      // 新连接已经接管后，丢弃旧 WebSocket 迟到的消息，避免恢复快照被旧状态回滚。
      if (!isCurrentAttempt() || failed) return
      const encodedMessage = String(event.data)
      let message = JSON.parse(encodedMessage)
      // A successful pair owns the next snapshot. Old observation packets must
      // not replace its room, poison its delta baseline, or trigger recovery.
      if (isRetiredObservationMessage(message)) return
      const wireWasPlainGameState = message.type === 'gameState'
      if (message.type === 'gameStateCardFrameV1') {
        const rebuilt = negotiatedGameStateCardFrame ? restoreGameStateCardFrame(message, encodedMessage) : null
        if (!rebuilt) {
          l12State.recoveryPhase = 'snapshot-mismatch'
          l12State.notice = '完整快照校验失败，正在请求未压缩权威状态…'
          syncGameReentry()
          requestPlainGameStateRecovery(socket, replaceBrokenSnapshotConnection)
          return
        }
        message = rebuilt
      }
      if (message.type === 'gameStateDelta') {
        const rebuilt = rebuildDeltaGameState(message)
        if (!rebuilt) {
          l12State.recoveryPhase = 'snapshot-mismatch'
          l12State.notice = '增量快照基线不匹配，正在请求完整权威状态…'
          requestSnapshotRecovery(socket, replaceBrokenSnapshotConnection)
          return
        }
        message = rebuilt
      }
      // Any current server message proves the receive path is still alive.
      unansweredHeartbeats = 0
      if (message.type === 'session') {
        const nextAccountId = String(message.accountId || '')
        const nextSessionId = String(message.sessionId || '')
        if (l12State.accountId !== nextAccountId) retiredObservation = null
        if (l12State.accountId !== nextAccountId || l12State.sessionId !== nextSessionId) l12State.tutorial = null
        cancelResourceFallback()
        negotiatedRequestIds = Boolean(message.capabilities?.requestIds)
        negotiatedEventWindowDelta = message.capabilities?.deltaGameState === true
          && message.capabilities?.eventWindowDeltaV1 === true
        negotiatedHandArrayDelta = message.capabilities?.deltaGameState === true
          && message.capabilities?.[HAND_ARRAY_DELTA_CAPABILITY] === true
        negotiatedPresenceDelta = message.capabilities?.presenceDeltaV1 === true
        negotiatedPresenceOrderReuse = negotiatedPresenceDelta && message.capabilities?.presenceOrderReuseV1 === true
        resetGameStateCardFrameRecovery()
        negotiatedGameStateCardFrame = message.capabilities?.gameStateCardFrameV1 === true
        presenceRecoveryPending = false
        presenceBaselineEpoch = ''
        claimedGeneration = Number(message.connectionGeneration || 0)
        l12State.sessionId = nextSessionId
        l12State.accountId = nextAccountId
        l12State.tutorialAvailable = message.tutorialAvailable === true
        l12State.nickname = message.name
        l12State.connectionGeneration = claimedGeneration
        chatResource.connect(l12State.accountId, message.capabilities?.chatV1 === true, payload => {
          if (!isCurrentAttempt() || failed || socket.readyState !== WebSocket.OPEN) return false
          try { socket.send(JSON.stringify(payload)); return true } catch { return false }
        }, message.chatServerUtcMs)
        l12State.recoveryPhase = 'session-claimed'
        l12State.notice = message.recovered ? '连接已恢复，正在同步对局状态…' : ''
        startHeartbeat(socket, () => failConnection('服务器长时间未响应心跳，正在自动重连。'))
        if (l12State.matchFound && !l12State.game) requestMatchedState(socket, replaceBrokenSnapshotConnection)
        syncGameReentry()
      }
      else if (message.type === 'authenticationRequired') {
        failed = true
        clearHeartbeat()
        clearSnapshotRecovery()
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'authentication'
        l12State.recoveryPhase = 'authentication-rejected'
        l12State.notice = message.message || '登录状态已失效，请重新登录账号'
        syncGameReentry()
        settle(new Error(l12State.notice))
        socket.close(4001, 'authentication rejected')
      }
      else if (message.type === 'passwordChangeRequired' || message.type === 'connectionRejected') {
        failed = true
        clearHeartbeat()
        clearSnapshotRecovery()
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'authentication'
        l12State.recoveryPhase = 'authentication-rejected'
        l12State.notice = message.message || '连接认证未完成'
        syncGameReentry()
        settle(new Error(l12State.notice))
        socket.close(4001, String(message.reason || 'connection rejected').slice(0, 120))
      }
      else if (message.type === 'sessionSuperseded') {
        failed = true
        clearHeartbeat()
        clearSnapshotRecovery()
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'superseded'
        l12State.recoveryPhase = 'superseded'
        l12State.notice = message.message || '此账号已由另一个页面接管连接'
        syncGameReentry()
        settle(new Error(l12State.notice))
        socket.close(4002, 'session superseded')
      }
      else if (['chatAck', 'chatMessage', 'chatHistory', 'chatReadAck'].includes(message.type)) chatResource.receive(message)
      else if (message.type === 'pong') l12State.lastPongAt = new Date().toISOString()
      else if (message.type === 'roomState') {
        if (l12State.leavingRoom) return
        const projectedTutorial = tutorialProjection(message.tutorial)
        l12State.room = { ...message, tutorial: projectedTutorial }
        if (l12State.tutorial && (l12State.tutorial.roomCode !== String(message.roomCode || '')
          || !projectedTutorial || projectedTutorial.lessonId !== l12State.tutorial.lessonId
          || projectedTutorial.version !== l12State.tutorial.version)) l12State.tutorial = null
        if (!message.started) {
          if (l12State.game) chatResource.retireOpponent(l12State.game.matchId)
          l12State.game = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.observerView = 'public'
          l12State.gmEnabled = false
          l12State.refereeControlsEnabled = false
          completePendingAction(message.requestId)
        }
        syncGameReentry()
      }
      else if (message.type === 'effectiveOperationsPolicy') {
        const accepted = acceptResourceRevision('operationsPolicy', message.epoch, message.revision, true)
        if (accepted && Number(message.policy?.version ?? 0) >= Number(l12State.operationsPolicy?.version ?? 0)) {
          l12State.operationsPolicy = message.policy
          dispatchResourceChange('operationsPolicy', { revision: message.revision })
          if (message.policy?.maintenance?.active) l12State.connectionIssue = 'maintenance'
          else if (l12State.connectionIssue === 'maintenance') l12State.connectionIssue = 'none'
        }
      }
      else if (message.type === 'resourceVersions') {
        const epoch = String(message.epoch || '')
        if (epoch && epoch !== l12State.resourceEpoch) {
          l12State.resourceEpoch = epoch
          l12State.resourceRevisions = {}
          presenceBaselineEpoch = ''
        }
        for (const [resource, revision] of Object.entries(message.revisions || {}))
          l12State.resourceRevisions[resource] = Number(revision || 0)
        resourceNames.forEach(resource => dispatchResourceChange(resource, { fullSync: true }))
      }
      else if (message.type === 'resourceChanged') {
        const resource = String(message.resource || '')
        if (resourceNames.includes(resource as typeof resourceNames[number])
          && acceptResourceRevision(resource, message.epoch, message.revision))
          dispatchResourceChange(resource, { revision: message.revision })
      }
      else if (message.type === 'presenceSnapshot') {
        if (validPresenceIdentity(message) && validPresenceItems(message.items)
          && acceptResourceRevision('presence', message.epoch, message.revision, true)) {
          l12State.presence = message.items
          presenceRecoveryPending = false
          presenceBaselineEpoch = message.epoch
          dispatchResourceChange('presence', { revision: message.revision })
        }
        else if (!validPresenceIdentity(message) || !validPresenceItems(message.items))
          requestPresenceRecovery(socket)
      }
      else if (message.type === 'presenceDelta') {
        const rebuilt = rebuildPresenceDelta(message)
        if (!rebuilt) requestPresenceRecovery(socket)
        else {
          l12State.presence = rebuilt.items
          l12State.resourceEpoch = rebuilt.epoch
          l12State.resourceRevisions.presence = rebuilt.revision
          dispatchResourceChange('presence', { revision: rebuilt.revision })
        }
      }
      else if (message.type === 'operationsBlocked') {
        l12State.notice = message.message || '当前运营规则不允许执行此操作'
        completePendingAction(message.requestId)
      }
      else if (message.type === 'maintenanceWarning') {
        l12State.notice = message.message || '服务器即将维护，请尽快结束当前对局。'
      }
      else if (message.type === 'matchGovernanceResult') {
        l12State.matchGovernanceResult = message as MatchGovernanceResult
        l12State.notice = message.message || ''
      }
      else if (message.type === 'friendInvitation') l12State.friendInvitation = message
      else if (message.type === 'friendInvitationResolved' || message.type === 'friendInvitationRevoked') {
        if (!message.invitationId || l12State.friendInvitation?.invitationId === message.invitationId)
          l12State.friendInvitation = null
        if (!message.invitationId || l12State.outgoingFriendInvitation?.invitationId === message.invitationId)
          l12State.outgoingFriendInvitation = null
        if (message.type === 'friendInvitationRevoked' && message.message) l12State.notice = message.message
      }
      else if (message.type === 'friendRoomCreated') {
        if (l12State.friendInvitation?.invitationId === message.invitationId)
          l12State.friendInvitation = null
        if (l12State.outgoingFriendInvitation?.invitationId === message.invitationId)
          l12State.outgoingFriendInvitation = null
        l12State.notice = message.message || '好友房间已创建'
        window.dispatchEvent(new CustomEvent('l12-friend-room-created', { detail: message }))
      }
      else if (message.type === 'friendInvitationSent') {
        if (message.invitationId && message.roomCode && message.targetAccountId) {
          l12State.outgoingFriendInvitation = {
            invitationId: message.invitationId,
            roomCode: message.roomCode,
            targetAccountId: message.targetAccountId,
          }
        }
        l12State.notice = message.message || ''
      }
      else if (message.type === 'friendInvitationRejected') {
        if (!message.invitationId || l12State.outgoingFriendInvitation?.invitationId === message.invitationId)
          l12State.outgoingFriendInvitation = null
        l12State.notice = message.message || ''
      }
      else if (message.type === 'matchmakingState') {
        if (!message.queued && l12State.matchFound) return
        l12State.matchmaking = message.queued ? message : null
        if (message.queued) startMatchmakingPolling()
        else clearMatchmakingPolling()
        l12State.notice = message.message || ''
        if (message.observationResyncRequired === true && l12State.spectating && l12State.room
          && !l12State.leavingRoom && !l12State.matchFound)
          socket.send(JSON.stringify({ type: 'syncState' }))
      }
      else if (message.type === 'matchmakingFound') {
        if (l12State.matchFound?.matchId === message.matchId) return
        clearMatchmakingPolling()
        l12State.matchmaking = null
        if (l12State.game?.matchId === message.matchId && !l12State.spectating) return
        retireObservationForMatch(message)
        l12State.matchFound = { mode: message.mode, roomCode: message.roomCode, matchId: message.matchId }
        l12State.notice = message.message || '匹配成功，正在建立对局'
        syncGameReentry()
        requestMatchedState(socket, replaceBrokenSnapshotConnection)
      }
      else if (message.type === 'matchmakingRejected') {
        const wasRecoveringMatch = Boolean(l12State.matchFound)
        clearMatchmakingPolling()
        clearMatchmakingRecovery()
        if (wasRecoveringMatch) {
          clearSnapshotRecovery()
          if (l12State.status === 'online') l12State.recoveryPhase = 'snapshot-acknowledged'
        }
        l12State.matchmaking = null
        l12State.matchFound = null
        l12State.notice = message.message || '匹配请求未能完成'
      }
      else if (message.type === 'roomLeft' || message.type === 'roomClosed') {
        retiredObservation = null
        if (l12State.game) chatResource.retireOpponent(l12State.game.matchId)
        clearMatchmakingRecovery()
        clearSnapshotRecovery()
        if (l12State.status === 'online' && l12State.recoveryPhase === 'snapshot-mismatch')
          l12State.recoveryPhase = 'snapshot-acknowledged'
        l12State.room = null
        l12State.game = null
        lastGameStateEnvelope = null
        l12State.rankedClock = null
        l12State.matchGovernanceResult = null
        l12State.spectating = false
        l12State.observerView = 'public'
        l12State.leavingRoom = false
        l12State.gmEnabled = false
        l12State.refereeControlsEnabled = false
        l12State.tutorial = null
        cancelPendingAction()
        l12State.matchFound = null
        l12State.notice = message.message || ''
        syncGameReentry()
      }
      else if (message.type === 'gameState') {
        if (l12State.leavingRoom) return
        const incoming = message.state as GameState
        if (message.tournamentId) {
          incoming.tournamentId = message.tournamentId
          incoming.tournamentCode = message.tournamentCode
          incoming.tournamentMatchId = message.tournamentMatchId
        }
        incoming.playerBadges = (message.playerBadges as GameState['playerBadges']) || []
        const current = l12State.game
        // 同一对局只接受不低于当前 revision 的权威快照；新对局可从较小 revision 重新开始。
        if (!current || current.matchId !== incoming.matchId || incoming.revision >= current.revision) {
          const resolvesOnlineSnapshotMismatch = l12State.status === 'online'
            && l12State.recoveryPhase === 'snapshot-mismatch'
            && (!incoming.roomCode || l12State.room?.roomCode === incoming.roomCode)
          l12State.game = incoming
          if (current && current.matchId !== incoming.matchId) chatResource.retireOpponent(current.matchId)
          if (incoming.phase === 'GameOver') chatResource.retireOpponent(incoming.matchId)
          lastGameStateEnvelope = JSON.parse(JSON.stringify(message))
          if (wireWasPlainGameState) gameStateCardFrameRecovery.recordPublishedPlainFrame(message)
          l12State.rankedClock = message.rankedClock
            ? { ...(message.rankedClock as RankedClockView), receivedAtMs: Date.now() }
            : null
          l12State.spectating = Boolean(message.spectating)
          l12State.observerView = message.spectating && message.observerView === 'referee' ? 'referee' : 'public'
          l12State.gmEnabled = Boolean(message.gmEnabled)
          l12State.refereeControlsEnabled = message.refereeControlsEnabled === true
            && l12State.spectating && l12State.observerView === 'referee'
            && Boolean(message.tournamentId && message.tournamentMatchId)
          const projectedTutorial = tutorialProjection(message.tutorial)
          const tutorialCapabilityConflict = Boolean(projectedTutorial && (l12State.gmEnabled || l12State.spectating))
          if (tutorialCapabilityConflict) {
            l12State.gmEnabled = false
            l12State.refereeControlsEnabled = false
            l12State.notice = '教学状态异常，管理操作已停用'
          }
          const roomCode = String(incoming.roomCode || l12State.room?.roomCode || '')
          l12State.tutorial = projectedTutorial && !tutorialCapabilityConflict
            && Boolean(l12State.accountId && l12State.sessionId && roomCode && incoming.matchId)
            ? { ...projectedTutorial, accountId: l12State.accountId, sessionId: l12State.sessionId,
                connectionGeneration: l12State.connectionGeneration, roomCode, matchId: incoming.matchId }
            : null
          completePendingAction(message.requestId)
          l12State.rankedSettlement = message.rankedSettlement || null
          if (message.responsePreference) l12State.responsePreference = message.responsePreference as ResponsePreferenceState
          if (l12State.status === 'connecting') l12State.recoveryPhase = 'snapshot-received'
          else if (resolvesOnlineSnapshotMismatch) {
            clearSnapshotRecovery()
            l12State.recoveryPhase = 'snapshot-acknowledged'
          }
          clearMatchmakingRecovery()
          l12State.matchFound = null
          if (message.recovered || resolvesOnlineSnapshotMismatch || l12State.notice.includes('正在同步')) l12State.notice = ''
          syncGameReentry()
        }
      }
      else if (message.type === 'responsePreferenceState') {
        l12State.responsePreference = message as ResponsePreferenceState
      }
      else if (message.type === 'responsePreferenceResult') {
        l12State.responsePreferenceResult = message as ResponsePreferenceResult
        l12State.responsePreference = {
          confirmedMode: message.confirmedMode as ResponseMode,
          syncPending: Boolean(message.syncPending),
        }
      }
      else if (message.type === 'responsePreferenceRejected') {
        l12State.notice = String(message.message || '响应设置未写入，请确认当前房间及操作权限')
        l12State.responsePreferenceResult = {
          ...l12State.responsePreference,
          matchApplied: false,
          accountSynced: false,
          requestId: typeof message.requestId === 'string' ? message.requestId : undefined,
        }
      }
      else if (message.type === 'recoveryComplete') {
        const generation = Number(message.connectionGeneration || 0)
        if (claimedGeneration === null || generation !== claimedGeneration || generation !== l12State.connectionGeneration) return
        const expectedMatchId = String(message.matchId || '')
        const expectedRevision = message.recoveryRevision == null ? null : Number(message.recoveryRevision)
        const snapshotMatches = l12State.leavingRoom || !expectedMatchId || (l12State.game?.matchId === expectedMatchId
          && (expectedRevision == null || l12State.game.revision >= expectedRevision))
        const roomMatches = l12State.leavingRoom || !message.roomCode || l12State.room?.roomCode === message.roomCode
        const cardFrameRecoveryActive = gameStateCardFrameRecovery.active
        if (cardFrameRecoveryActive && !gameStateCardFrameRecovery.hasPublishedPlainFrame) {
          replaceBrokenSnapshotConnection()
          return
        }
        if (!snapshotMatches || !roomMatches) {
          l12State.recoveryPhase = 'snapshot-mismatch'
          l12State.notice = '权威恢复快照尚未完整到达，正在重新同步…'
          syncGameReentry()
          if (cardFrameRecoveryActive) replaceBrokenSnapshotConnection()
          else requestSnapshotRecovery(socket, replaceBrokenSnapshotConnection)
          return
        }
        clearSnapshotRecovery()
        const refereeReceiptPending = pendingActionEnvelope?.type === 'gmAction'
          && typeof (pendingActionEnvelope.command as Record<string, unknown> | undefined)?.refereeMatchId === 'string'
        if (!message.roomCode) {
          retiredObservation = null
          if (!refereeReceiptPending) cancelPendingAction()
          l12State.room = null
          l12State.game = null
          lastGameStateEnvelope = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.observerView = 'public'
          l12State.gmEnabled = false
          l12State.refereeControlsEnabled = false
          l12State.tutorial = null
          l12State.matchFound = null
        }
        else if (!message.matchId) {
          if (!refereeReceiptPending) cancelPendingAction()
          l12State.game = null
          lastGameStateEnvelope = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.observerView = 'public'
          l12State.gmEnabled = false
          l12State.refereeControlsEnabled = false
          l12State.tutorial = null
        }
        if (cardFrameRecoveryActive) {
          clearGameStateCardFrameRecoveryTimer()
          if (!gameStateCardFrameRecovery.finish().ready) {
            replaceBrokenSnapshotConnection()
            return
          }
        }
        reconnectAttempts = 0
        l12State.retryCount = 0
        // Close metadata belongs to the previous socket generation. Once the full
        // authoritative recovery handshake succeeds it must not taint new Bug diagnostics.
        l12State.lastCloseCode = null
        l12State.lastCloseReason = ''
        l12State.status = 'online'
        l12State.recoveryPhase = 'snapshot-acknowledged'
        l12State.connectionIssue = l12State.operationsPolicy?.maintenance.active ? 'maintenance' : 'none'
        l12State.notice = ''
        if (l12State.leavingRoom) socket.send(JSON.stringify({ type: 'leaveRoom' }))
        else if (pendingActionEnvelope && negotiatedRequestIds
          && pendingActionResentAttempt !== attempt && socket.readyState === WebSocket.OPEN) {
          pendingActionResentAttempt = attempt
          socket.send(JSON.stringify(pendingActionEnvelope))
        }
        syncGameReentry()
        settle()
      }
      else if (message.type === 'refereeActionReceipt') {
        completePendingAction(message.requestId)
        l12State.notice = '上次裁判调整已保存'
      }
      else if (message.type === 'error' || message.type === 'actionRejected' || message.type === 'deckRejected'
        || message.type === 'tutorialRejected' || message.type === 'tutorialUnavailable'
        || message.type === 'tutorialRequestExpired' || message.type === 'tutorialRestartRequired'
        || message.type === 'sandboxForbidden'
        || message.type === 'tournamentRoomRejected' || message.type === 'tournamentResultPending') {
        if (!(l12State.spectating && message.message === '观战者不能执行对局操作')) l12State.notice = message.message
        completePendingAction(message.requestId)
        l12State.leavingRoom = false
        syncGameReentry()
      }
    }
    socket.onerror = () => failConnection('暂时无法连接服务器，正在自动重试。')
    socket.onclose = (event) => {
      if (isCurrentAttempt()) {
        chatResource.disconnected()
        failed = true
        clearSnapshotRecovery()
        resetGameStateCardFrameRecovery()
        clearHandshakeTimer()
        clearHeartbeat()
        clearMatchmakingPolling()
        clearMatchmakingRecovery()
        l12State.matchmaking = null
        l12State.socket = null
        lastGameStateEnvelope = null
        negotiatedGameStateCardFrame = false
        negotiatedPresenceDelta = false
        negotiatedPresenceOrderReuse = false
        presenceRecoveryPending = false
        presenceBaselineEpoch = ''
        l12State.status = 'offline'
        l12State.lastCloseCode = event.code || null
        l12State.lastCloseReason = String(event.reason || '').slice(0, 160)
        if (event.code === 4001 || event.code === 1008) l12State.connectionIssue = 'authentication'
        else if (event.code === 4002) l12State.connectionIssue = 'superseded'
        else if (l12State.connectionIssue === 'none') l12State.connectionIssue = 'websocket'
        l12State.recoveryPhase = event.code === 4002 ? 'superseded' : 'disconnected'
        if (!negotiatedRequestIds || [4001, 4002, 1008].includes(event.code)) cancelPendingAction()
        l12State.gmEnabled = false
        l12State.refereeControlsEnabled = false
        if (l12State.observerView === 'referee') {
          l12State.game = null
          lastGameStateEnvelope = null
          l12State.observerView = 'public'
        }
        syncGameReentry()
        settle(new Error(l12State.notice || '连接已关闭'))
        if (![4001, 4002, 1008].includes(event.code)) scheduleReconnect()
        if (![4001, 4002, 1008].includes(event.code)) scheduleResourceFallback()
      }
    }
  })
  const tracked = pending.finally(() => {
    if (connectPromise === tracked) connectPromise = null
  })
  connectPromise = tracked
  return tracked
}

export function startAutomaticConnection() {
  automaticConnectionEnabled = true
  if (!localStorage.getItem(deploymentStorageKey('l12-auth-token'))) return
  if (l12State.status === 'offline') void connect().catch(() => undefined)
}

export function stopAutomaticConnection() {
  automaticConnectionEnabled = false
  clearReconnectTimer()
  cancelResourceFallback()
  reconnectAttempts = 0
  l12State.retryCount = 0
  disconnect()
}

export function disconnect() {
  chatResource.reset()
  automaticConnectionEnabled = false
  connectionAttemptSerial += 1
  clearReconnectTimer()
  cancelResourceFallback()
  clearSnapshotRecovery()
  resetGameStateCardFrameRecovery()
  clearHeartbeat()
  clearMatchmakingPolling()
  clearMatchmakingRecovery()
  const socket = l12State.socket
  l12State.socket = null
  const cancel = cancelPendingConnect
  cancelPendingConnect = null
  connectPromise = null
  cancel?.()
  socket?.close()
  l12State.status = 'offline'
  l12State.recoveryPhase = 'idle'
  l12State.connectionIssue = 'none'
  l12State.sessionId = ''
  l12State.accountId = ''
  l12State.presence = []
  l12State.resourceEpoch = ''
  l12State.resourceRevisions = {}
  l12State.room = null
  l12State.game = null
  l12State.spectating = false
  l12State.observerView = 'public'
  l12State.leavingRoom = false
  l12State.gmEnabled = false
  l12State.refereeControlsEnabled = false
  l12State.tutorialAvailable = false
  l12State.tutorial = null
  cancelPendingAction()
  negotiatedRequestIds = false
  negotiatedEventWindowDelta = false
  negotiatedHandArrayDelta = false
  negotiatedPresenceDelta = false
  negotiatedPresenceOrderReuse = false
  negotiatedGameStateCardFrame = false
  presenceRecoveryPending = false
  presenceBaselineEpoch = ''
  lastGameStateEnvelope = null
  retiredObservation = null
  l12State.matchmaking = null
  l12State.matchFound = null
  l12State.rankedSettlement = null
  l12State.rankedClock = null
  l12State.matchGovernanceResult = null
  syncGameReentry()
}

export function send(payload: unknown) {
  if (l12State.socket?.readyState !== WebSocket.OPEN) {
    l12State.notice = '尚未连接服务器'
    return
  }
  l12State.notice = ''
  l12State.socket.send(JSON.stringify(payload))
}

export interface RoomOptions {
  matchModeId: string
  spectating: 'public' | 'friends' | 'disabled'
  handVisibility: 'request' | 'public'
  disasterMode: 'all' | 'random' | 'season' | 'none'
  useCardRestrictions: boolean
}

export type SandboxDisasterMode = 'all' | 'random' | 'custom' | 'none'

export const createRoom = (options?: RoomOptions) => { l12State.spectating = false; l12State.leavingRoom = false; send({ type: 'createRoom', options }) }
export const updateRoomOptions = (options: RoomOptions) => send({ type: 'updateRoomOptions', options })
export const createSandbox = (playerDeck?: SavedL12Deck, opponentDeck?: SavedL12Deck, disasterMode: SandboxDisasterMode = 'none') => {
  l12State.spectating = false
  l12State.leavingRoom = false
  l12State.gmEnabled = false
  l12State.refereeControlsEnabled = false
  send({ type: 'createSandbox', request: { playerDeck, opponentDeck, disasterMode } })
}
export function createTutorial(lessonId: TutorialLessonId) {
  if (!tutorialLessonIds.has(lessonId)) return null
  if (!l12State.tutorialAvailable) { l12State.notice = '教学暂未在当前服务器开放'; return null }
  if (l12State.status !== 'online' || l12State.socket?.readyState !== WebSocket.OPEN) {
    l12State.notice = '教学服务器尚未连接'
    return null
  }
  if (l12State.matchmaking?.queued) {
    l12State.notice = '请先取消当前匹配，再开始教学'
    return null
  }
  if (l12State.room || l12State.game || l12State.leavingRoom) {
    l12State.notice = '请等待当前房间完全退出后再开始教学'
    return null
  }
  if (l12State.pendingAction) return null
  const requestId = createActionRequestId()
  l12State.tutorial = null
  l12State.pendingAction = true
  pendingActionEnvelope = { type: 'createTutorial', lessonId, requestId }
  send(pendingActionEnvelope)
  if (l12State.socket?.readyState !== WebSocket.OPEN) cancelPendingAction()
  return requestId
}
export const joinRoom = (roomCode: string) => { l12State.spectating = false; l12State.leavingRoom = false; send({ type: 'joinRoom', roomCode }) }
export const joinMatchmaking = (mode: 'ranked' | 'casual', deck?: SavedL12Deck) => send({ type: 'joinMatchmaking', mode, deck })
export const cancelMatchmaking = () => { clearMatchmakingPolling(); send({ type: 'cancelMatchmaking' }) }
export const enterTournamentMatch = (tournamentId: string, matchId: string) => {
  l12State.spectating = false
  l12State.leavingRoom = false
  send({ type: 'enterTournamentMatch', tournamentId, matchId })
}
export const inviteFriend = (accountId: string) => send({ type: 'inviteFriend', accountId })
export const cancelFriendInvitation = (invitationId: string) => send({ type: 'cancelFriendInvitation', invitationId })
export const resolveFriendInvitation = (invitationId: string, accept: boolean) => send({ type: 'resolveFriendInvitation', invitationId, accept })
export const spectateRoom = (roomCode: string) => { l12State.leavingRoom = false; send({ type: 'spectateRoom', roomCode }) }
export const spectateTournamentMatch = (tournamentId: string, matchId: string) => {
  l12State.spectating = true
  l12State.leavingRoom = false
  send({ type: 'spectateTournamentMatch', tournamentId, matchId })
}
export const selectDeck = (deckIndex: number) => send({ type: 'selectDeck', deckIndex })
export const selectCustomDeck = (deck: SavedL12Deck) => send({ type: 'selectCustomDeck', deck })
export const setReady = (ready: boolean) => send({ type: 'ready', ready })
export const getResponsePreference = () => send({ type: 'getResponsePreference' })
export const setResponsePreference = (mode: ResponseMode) => {
  const requestId = createActionRequestId()
  send({ type: 'setResponsePreference', mode, requestId })
  return requestId
}
export const returnToRoom = () => {
  void gameReentry.requestExit(l12State.game?.matchId)
  setReady(false)
}
export const leaveRoom = () => {
  void gameReentry.requestExit(l12State.game?.matchId)
  l12State.leavingRoom = true
  syncGameReentry()
  send({ type: 'leaveRoom' })
}
export function gameAction(command: Record<string, unknown>) {
  if (l12State.spectating || l12State.pendingAction) return
  l12State.pendingAction = true
  pendingActionEnvelope = { type: 'gameAction', requestId: createActionRequestId(), command }
  send(pendingActionEnvelope)
  if (l12State.socket?.readyState !== WebSocket.OPEN) cancelPendingAction()
}

export function sandboxAction(actingPlayerIndex: number, command: Record<string, unknown>) {
  if (!l12State.gmEnabled || l12State.pendingAction) return
  l12State.pendingAction = true
  pendingActionEnvelope = {
    type: 'sandboxAction', requestId: createActionRequestId(), actingPlayerIndex, command,
  }
  send(pendingActionEnvelope)
  if (l12State.socket?.readyState !== WebSocket.OPEN) cancelPendingAction()
}

export function gmAction(command: Record<string, unknown>) {
  if (!l12State.gmEnabled || l12State.pendingAction) return
  l12State.pendingAction = true
  pendingActionEnvelope = { type: 'gmAction', requestId: createActionRequestId(), command }
  send(pendingActionEnvelope)
  if (l12State.socket?.readyState !== WebSocket.OPEN) cancelPendingAction()
}

export function refereeGmAction(command: Record<string, unknown>) {
  if (!l12State.refereeControlsEnabled || !l12State.spectating
    || l12State.observerView !== 'referee' || !l12State.rankedClock?.paused
    || l12State.pendingAction) return
  l12State.pendingAction = true
  pendingActionEnvelope = { type: 'gmAction', requestId: createActionRequestId(),
    command: { ...command, refereeMatchId: l12State.game?.matchId } }
  send(pendingActionEnvelope)
  if (l12State.socket?.readyState !== WebSocket.OPEN) cancelPendingAction()
}
