import { reactive } from 'vue'
import type { GameState, RankedClockView, RoomState } from './types'
import type { SavedL12Deck } from './decks'
import type { EffectiveOperationsPolicy, RankedSettlement } from './platform'

export type L12RecoveryPhase = 'idle' | 'opening-websocket' | 'authenticating' | 'session-claimed'
  | 'snapshot-received' | 'snapshot-mismatch' | 'snapshot-acknowledged' | 'authentication-rejected'
  | 'superseded' | 'disconnected'
export type L12ConnectionIssue = 'none' | 'http' | 'websocket' | 'authentication' | 'maintenance' | 'superseded'
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
  ? `wss://${location.host}/ws`
  : `ws://${location.hostname || 'localhost'}:8080/ws`)
const storedEndpoint = localStorage.getItem('l12-endpoint') || ''
// 正式 HTTPS 页面必须跟随当前域名，避免历史调试地址在部署后把玩家永久带到旧服务。
const initialEndpoint = location.protocol === 'https:' && !configuredEndpoint
  ? defaultEndpoint
  : (storedEndpoint || defaultEndpoint)

let connectPromise: Promise<void> | null = null
let reconnectTimer: ReturnType<typeof setTimeout> | null = null
let heartbeatTimer: ReturnType<typeof setInterval> | null = null
let matchmakingPollTimer: ReturnType<typeof setInterval> | null = null
let matchmakingRecoveryTimer: ReturnType<typeof setTimeout> | null = null
let reconnectAttempts = 0
let automaticConnectionEnabled = false

function clearReconnectTimer() {
  if (reconnectTimer !== null) window.clearTimeout(reconnectTimer)
  reconnectTimer = null
}

function clearHeartbeat() {
  if (heartbeatTimer !== null) window.clearInterval(heartbeatTimer)
  heartbeatTimer = null
}

function clearMatchmakingPolling() {
  if (matchmakingPollTimer !== null) window.clearInterval(matchmakingPollTimer)
  matchmakingPollTimer = null
}

function clearMatchmakingRecovery() {
  if (matchmakingRecoveryTimer !== null) window.clearTimeout(matchmakingRecoveryTimer)
  matchmakingRecoveryTimer = null
}

function requestMatchedState(socket: WebSocket) {
  if (socket.readyState !== WebSocket.OPEN) return
  socket.send(JSON.stringify({ type: 'syncState' }))
  clearMatchmakingRecovery()
  matchmakingRecoveryTimer = window.setTimeout(() => {
    matchmakingRecoveryTimer = null
    if (l12State.matchFound && !l12State.game && socket === l12State.socket && socket.readyState === WebSocket.OPEN)
      socket.send(JSON.stringify({ type: 'syncState' }))
  }, 800)
}

function startMatchmakingPolling() {
  if (matchmakingPollTimer !== null) return
  matchmakingPollTimer = window.setInterval(() => {
    if (l12State.matchmaking?.queued && l12State.socket?.readyState === WebSocket.OPEN)
      l12State.socket.send(JSON.stringify({ type: 'pollMatchmaking' }))
  }, 5_000)
}

function startHeartbeat(socket: WebSocket) {
  clearHeartbeat()
  heartbeatTimer = window.setInterval(() => {
    if (socket.readyState === WebSocket.OPEN) {
      l12State.lastHeartbeatAt = new Date().toISOString()
      socket.send(JSON.stringify({ type: 'ping' }))
    }
  }, 25_000)
}

function scheduleReconnect() {
  if (!automaticConnectionEnabled || reconnectTimer !== null || !localStorage.getItem('l12-auth-token')) return
  const delay = Math.min(1_000 * (2 ** reconnectAttempts), 15_000)
  reconnectAttempts += 1
  l12State.retryCount = reconnectAttempts
  l12State.recoveryPhase = 'disconnected'
  l12State.notice = `连接中断，${Math.ceil(delay / 1000)} 秒后自动重连…`
  reconnectTimer = window.setTimeout(() => {
    reconnectTimer = null
    void connect().catch(() => undefined)
  }, delay)
}

export const l12State = reactive({
  socket: null as WebSocket | null,
  status: 'offline' as 'offline' | 'connecting' | 'online',
  nickname: localStorage.getItem('l12-nickname') || '',
  endpoint: normalizeEndpoint(initialEndpoint),
  sessionId: '',
  room: null as RoomState | null,
  game: null as GameState | null,
  spectating: false,
  leavingRoom: false,
  gmEnabled: false,
  pendingAction: false,
  notice: '',
  operationsPolicy: null as EffectiveOperationsPolicy | null,
  friendInvitation: null as null | { invitationId: string; roomCode: string; fromAccountId: string; fromName: string },
  matchmaking: null as null | { queued: boolean; mode?: 'ranked' | 'casual'; joinedAt?: string },
  matchFound: null as null | { mode?: 'ranked' | 'casual'; roomCode: string; matchId?: string },
  rankedSettlement: null as RankedSettlement | null,
  rankedClock: null as RankedClockView | null,
  connectionGeneration: 0,
  recoveryPhase: 'idle' as L12RecoveryPhase,
  connectionIssue: 'none' as L12ConnectionIssue,
  lastHeartbeatAt: '' as string,
  lastPongAt: '' as string,
  lastCloseCode: null as number | null,
  lastCloseReason: '' as string,
  retryCount: 0,
})

function socketReadyStateName(socket: WebSocket | null) {
  const state = socket?.readyState
  return state === WebSocket.CONNECTING ? 'connecting'
    : state === WebSocket.OPEN ? 'open'
      : state === WebSocket.CLOSING ? 'closing'
        : state === WebSocket.CLOSED ? 'closed' : 'absent'
}

function httpEndpoint(path: string) {
  try {
    const url = new URL(l12State.endpoint)
    url.protocol = url.protocol === 'wss:' ? 'https:' : 'http:'
    url.pathname = path
    url.search = ''
    url.hash = ''
    return url.toString()
  } catch { return `${location.protocol}//${location.hostname}:8080${path}` }
}

async function diagnosticFetch(path: string, authenticated = false) {
  const controller = new AbortController()
  const timeout = window.setTimeout(() => controller.abort(), 3_000)
  try {
    const headers = new Headers()
    const token = authenticated ? localStorage.getItem('l12-auth-token') : ''
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
  const authenticationState = !localStorage.getItem('l12-auth-token') ? 'missing-token'
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
  if (l12State.socket?.readyState === WebSocket.OPEN && l12State.status === 'online') return Promise.resolve()
  if (connectPromise) return connectPromise
  const authToken = localStorage.getItem('l12-auth-token') || ''
  if (!authToken) {
    l12State.notice = '请先登录账号'
    l12State.connectionIssue = 'authentication'
    l12State.recoveryPhase = 'authentication-rejected'
    return Promise.reject(new Error(l12State.notice))
  }
  automaticConnectionEnabled = true
  clearReconnectTimer()
  l12State.status = 'connecting'
  l12State.connectionIssue = 'none'
  l12State.recoveryPhase = 'opening-websocket'
  l12State.notice = ''
  l12State.endpoint = normalizeEndpoint(l12State.endpoint)
  localStorage.setItem('l12-endpoint', l12State.endpoint)
  const pending = new Promise<void>((resolve, reject) => {
    const socket = new WebSocket(l12State.endpoint)
    let settled = false
    l12State.socket = socket
    socket.onopen = () => {
      l12State.recoveryPhase = 'authenticating'
      socket.send(JSON.stringify({ type: 'hello', authToken }))
    }
    socket.onmessage = (event) => {
      // 新连接已经接管后，丢弃旧 WebSocket 迟到的消息，避免恢复快照被旧状态回滚。
      if (l12State.socket !== socket) return
      const message = JSON.parse(String(event.data))
      if (message.type === 'session') {
        l12State.sessionId = message.sessionId
        l12State.nickname = message.name
        l12State.connectionGeneration = Number(message.connectionGeneration || 0)
        l12State.recoveryPhase = 'session-claimed'
        l12State.notice = message.recovered ? '连接已恢复，正在同步对局状态…' : ''
        startHeartbeat(socket)
      }
      else if (message.type === 'authenticationRequired') {
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'authentication'
        l12State.recoveryPhase = 'authentication-rejected'
        l12State.notice = message.message || '登录状态已失效，请重新登录账号'
        if (!settled) { settled = true; reject(new Error(l12State.notice)) }
        socket.close(4001, 'authentication rejected')
      }
      else if (message.type === 'passwordChangeRequired' || message.type === 'connectionRejected') {
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'authentication'
        l12State.recoveryPhase = 'authentication-rejected'
        l12State.notice = message.message || '连接认证未完成'
        if (!settled) { settled = true; reject(new Error(l12State.notice)) }
        socket.close(4001, String(message.reason || 'connection rejected').slice(0, 120))
      }
      else if (message.type === 'sessionSuperseded') {
        automaticConnectionEnabled = false
        l12State.connectionIssue = 'superseded'
        l12State.recoveryPhase = 'superseded'
        l12State.notice = message.message || '此账号已由另一个页面接管连接'
        if (!settled) { settled = true; reject(new Error(l12State.notice)) }
      }
      else if (message.type === 'pong') l12State.lastPongAt = new Date().toISOString()
      else if (message.type === 'roomState') {
        if (l12State.leavingRoom) return
        l12State.room = message
        if (!message.started) {
          l12State.game = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.gmEnabled = false
          l12State.pendingAction = false
        }
      }
      else if (message.type === 'effectiveOperationsPolicy') {
        l12State.operationsPolicy = message.policy
        if (message.policy?.maintenance?.active) l12State.connectionIssue = 'maintenance'
      }
      else if (message.type === 'operationsBlocked') {
        l12State.notice = message.message || '当前运营规则不允许执行此操作'
        l12State.pendingAction = false
      }
      else if (message.type === 'friendInvitation') l12State.friendInvitation = message
      else if (message.type === 'friendInvitationResolved') l12State.friendInvitation = null
      else if (message.type === 'friendRoomCreated') {
        l12State.friendInvitation = null
        l12State.notice = message.message || '好友房间已创建'
        window.dispatchEvent(new CustomEvent('l12-friend-room-created', { detail: message }))
      }
      else if (message.type === 'friendInvitationSent' || message.type === 'friendInvitationRejected') l12State.notice = message.message || ''
      else if (message.type === 'matchmakingState') {
        if (!message.queued && l12State.matchFound) return
        l12State.matchmaking = message.queued ? message : null
        if (message.queued) startMatchmakingPolling()
        else clearMatchmakingPolling()
        l12State.notice = message.message || ''
      }
      else if (message.type === 'matchmakingFound') {
        clearMatchmakingPolling()
        l12State.matchmaking = null
        l12State.matchFound = { mode: message.mode, roomCode: message.roomCode, matchId: message.matchId }
        l12State.notice = message.message || '匹配成功，正在建立对局'
        requestMatchedState(socket)
      }
      else if (message.type === 'matchmakingRejected') {
        clearMatchmakingPolling()
        clearMatchmakingRecovery()
        l12State.matchmaking = null
        l12State.matchFound = null
        l12State.notice = message.message || '匹配请求未能完成'
      }
      else if (message.type === 'roomLeft' || message.type === 'roomClosed') {
        clearMatchmakingRecovery()
        l12State.room = null
        l12State.game = null
        l12State.rankedClock = null
        l12State.spectating = false
        l12State.leavingRoom = false
        l12State.gmEnabled = false
        l12State.pendingAction = false
        l12State.matchFound = null
        l12State.notice = message.message || ''
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
          l12State.game = incoming
          l12State.rankedClock = message.rankedClock
            ? { ...(message.rankedClock as RankedClockView), receivedAtMs: Date.now() }
            : null
          l12State.spectating = Boolean(message.spectating)
          l12State.gmEnabled = Boolean(message.gmEnabled)
          l12State.pendingAction = false
          l12State.rankedSettlement = message.rankedSettlement || null
          if (l12State.status === 'connecting') l12State.recoveryPhase = 'snapshot-received'
          clearMatchmakingRecovery()
          l12State.matchFound = null
          if (message.recovered || l12State.notice.includes('正在同步')) l12State.notice = ''
        }
      }
      else if (message.type === 'recoveryComplete') {
        const generation = Number(message.connectionGeneration || 0)
        if (generation !== l12State.connectionGeneration) return
        const expectedMatchId = String(message.matchId || '')
        const expectedRevision = message.recoveryRevision == null ? null : Number(message.recoveryRevision)
        const snapshotMatches = l12State.leavingRoom || !expectedMatchId || (l12State.game?.matchId === expectedMatchId
          && (expectedRevision == null || l12State.game.revision >= expectedRevision))
        const roomMatches = l12State.leavingRoom || !message.roomCode || l12State.room?.roomCode === message.roomCode
        if (!snapshotMatches || !roomMatches) {
          l12State.recoveryPhase = 'snapshot-mismatch'
          l12State.notice = '权威恢复快照尚未完整到达，正在重新同步…'
          socket.send(JSON.stringify({ type: 'syncState' }))
          return
        }
        if (!message.roomCode) {
          l12State.room = null
          l12State.game = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.gmEnabled = false
          l12State.matchFound = null
        }
        else if (!message.matchId) {
          l12State.game = null
          l12State.rankedClock = null
          l12State.spectating = false
          l12State.gmEnabled = false
        }
        reconnectAttempts = 0
        l12State.retryCount = 0
        l12State.status = 'online'
        l12State.recoveryPhase = 'snapshot-acknowledged'
        l12State.connectionIssue = l12State.operationsPolicy?.maintenance.active ? 'maintenance' : 'none'
        l12State.notice = ''
        if (l12State.leavingRoom) socket.send(JSON.stringify({ type: 'leaveRoom' }))
        if (!settled) { settled = true; resolve() }
      }
      else if (message.type === 'error' || message.type === 'actionRejected' || message.type === 'deckRejected'
        || message.type === 'tournamentRoomRejected' || message.type === 'tournamentResultPending') {
        l12State.notice = message.message
        l12State.pendingAction = false
        l12State.leavingRoom = false
      }
    }
    socket.onerror = () => {
      if (l12State.socket !== socket) return
      l12State.status = 'offline'
      l12State.connectionIssue = 'websocket'
      l12State.notice = '暂时无法连接服务器，正在自动重试。'
      if (!settled) { settled = true; reject(new Error(l12State.notice)) }
    }
    socket.onclose = (event) => {
      if (l12State.socket === socket) {
        clearHeartbeat()
        clearMatchmakingPolling()
        clearMatchmakingRecovery()
        l12State.matchmaking = null
        l12State.socket = null
        l12State.status = 'offline'
        l12State.lastCloseCode = event.code || null
        l12State.lastCloseReason = String(event.reason || '').slice(0, 160)
        if (event.code === 4001 || event.code === 1008) l12State.connectionIssue = 'authentication'
        else if (event.code === 4002) l12State.connectionIssue = 'superseded'
        else if (l12State.connectionIssue === 'none') l12State.connectionIssue = 'websocket'
        l12State.recoveryPhase = event.code === 4002 ? 'superseded' : 'disconnected'
        l12State.pendingAction = false
        l12State.gmEnabled = false
        if (!settled) { settled = true; reject(new Error(l12State.notice || '连接已关闭')) }
        if (![4001, 4002, 1008].includes(event.code)) scheduleReconnect()
      }
    }
  })
  connectPromise = pending.finally(() => { connectPromise = null })
  return connectPromise
}

export function startAutomaticConnection() {
  automaticConnectionEnabled = true
  if (!localStorage.getItem('l12-auth-token')) return
  if (l12State.status === 'offline') void connect().catch(() => undefined)
}

export function stopAutomaticConnection() {
  automaticConnectionEnabled = false
  clearReconnectTimer()
  reconnectAttempts = 0
  l12State.retryCount = 0
  disconnect()
}

export function disconnect() {
  automaticConnectionEnabled = false
  clearReconnectTimer()
  clearHeartbeat()
  clearMatchmakingPolling()
  clearMatchmakingRecovery()
  l12State.socket?.close()
  l12State.socket = null
  l12State.status = 'offline'
  l12State.recoveryPhase = 'idle'
  l12State.connectionIssue = 'none'
  l12State.sessionId = ''
  l12State.room = null
  l12State.game = null
  l12State.spectating = false
  l12State.leavingRoom = false
  l12State.gmEnabled = false
  l12State.pendingAction = false
  l12State.matchmaking = null
  l12State.matchFound = null
  l12State.rankedSettlement = null
  l12State.rankedClock = null
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
  disasterMode: 'all' | 'random' | 'none'
  useCardRestrictions: boolean
}

export type SandboxDisasterMode = 'all' | 'random' | 'custom' | 'none'

export const createRoom = (options?: RoomOptions) => { l12State.spectating = false; l12State.leavingRoom = false; send({ type: 'createRoom', options }) }
export const updateRoomOptions = (options: RoomOptions) => send({ type: 'updateRoomOptions', options })
export const createSandbox = (playerDeck?: SavedL12Deck, opponentDeck?: SavedL12Deck, disasterMode: SandboxDisasterMode = 'none') => {
  l12State.spectating = false
  l12State.leavingRoom = false
  l12State.gmEnabled = false
  send({ type: 'createSandbox', request: { playerDeck, opponentDeck, disasterMode } })
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
export const returnToRoom = () => setReady(false)
export const leaveRoom = () => {
  l12State.leavingRoom = true
  send({ type: 'leaveRoom' })
}
export function gameAction(command: Record<string, unknown>) {
  if (l12State.pendingAction) return
  l12State.pendingAction = true
  send({ type: 'gameAction', command })
  if (l12State.socket?.readyState !== WebSocket.OPEN) l12State.pendingAction = false
}

export function sandboxAction(actingPlayerIndex: number, command: Record<string, unknown>) {
  if (!l12State.gmEnabled || l12State.pendingAction) return
  l12State.pendingAction = true
  send({ type: 'sandboxAction', actingPlayerIndex, command })
  if (l12State.socket?.readyState !== WebSocket.OPEN) l12State.pendingAction = false
}

export function gmAction(command: Record<string, unknown>) {
  if (!l12State.gmEnabled || l12State.pendingAction) return
  l12State.pendingAction = true
  send({ type: 'gmAction', command })
  if (l12State.socket?.readyState !== WebSocket.OPEN) l12State.pendingAction = false
}
