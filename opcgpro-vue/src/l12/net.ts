import { reactive } from 'vue'
import type { GameState, RoomState } from './types'
import type { SavedL12Deck } from './decks'
import type { EffectiveOperationsPolicy, RankedSettlement } from './platform'

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
    if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: 'ping' }))
  }, 25_000)
}

function scheduleReconnect() {
  if (!automaticConnectionEnabled || reconnectTimer !== null || !localStorage.getItem('l12-auth-token')) return
  const delay = Math.min(1_000 * (2 ** reconnectAttempts), 15_000)
  reconnectAttempts += 1
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
  rankedSettlement: null as RankedSettlement | null,
})

export function connect(): Promise<void> {
  if (l12State.socket?.readyState === WebSocket.OPEN && l12State.status === 'online') return Promise.resolve()
  if (connectPromise) return connectPromise
  const authToken = localStorage.getItem('l12-auth-token') || ''
  if (!authToken) {
    l12State.notice = '请先登录账号'
    return Promise.reject(new Error(l12State.notice))
  }
  automaticConnectionEnabled = true
  clearReconnectTimer()
  l12State.status = 'connecting'
  l12State.notice = ''
  l12State.endpoint = normalizeEndpoint(l12State.endpoint)
  localStorage.setItem('l12-endpoint', l12State.endpoint)
  const pending = new Promise<void>((resolve, reject) => {
    const socket = new WebSocket(l12State.endpoint)
    let settled = false
    l12State.socket = socket
    socket.onopen = () => {
      socket.send(JSON.stringify({ type: 'hello', authToken }))
    }
    socket.onmessage = (event) => {
      // 新连接已经接管后，丢弃旧 WebSocket 迟到的消息，避免恢复快照被旧状态回滚。
      if (l12State.socket !== socket) return
      const message = JSON.parse(String(event.data))
      if (message.type === 'session') {
        l12State.sessionId = message.sessionId
        l12State.nickname = message.name
        l12State.status = 'online'
        l12State.notice = message.recovered ? '连接已恢复，正在同步对局状态…' : ''
        reconnectAttempts = 0
        startHeartbeat(socket)
        if (l12State.leavingRoom) socket.send(JSON.stringify({ type: 'leaveRoom' }))
        if (!settled) { settled = true; resolve() }
      }
      else if (message.type === 'authenticationRequired') {
        automaticConnectionEnabled = false
        l12State.notice = message.message || '登录状态已失效，请重新登录账号'
        if (!settled) { settled = true; reject(new Error(l12State.notice)) }
        socket.close()
      }
      else if (message.type === 'roomState') {
        if (l12State.leavingRoom) return
        l12State.room = message
        if (!message.started) {
          l12State.game = null
          l12State.spectating = false
          l12State.gmEnabled = false
          l12State.pendingAction = false
        }
      }
      else if (message.type === 'effectiveOperationsPolicy') l12State.operationsPolicy = message.policy
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
        l12State.matchmaking = message.queued ? message : null
        if (message.queued) startMatchmakingPolling()
        else clearMatchmakingPolling()
        l12State.notice = message.message || ''
      }
      else if (message.type === 'matchmakingFound') {
        clearMatchmakingPolling()
        l12State.matchmaking = null
        l12State.notice = message.message || '匹配成功'
      }
      else if (message.type === 'matchmakingRejected') {
        clearMatchmakingPolling()
        l12State.matchmaking = null
        l12State.notice = message.message || '匹配请求未能完成'
      }
      else if (message.type === 'roomLeft' || message.type === 'roomClosed') {
        l12State.room = null
        l12State.game = null
        l12State.spectating = false
        l12State.leavingRoom = false
        l12State.gmEnabled = false
        l12State.pendingAction = false
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
        const current = l12State.game
        // 同一对局只接受不低于当前 revision 的权威快照；新对局可从较小 revision 重新开始。
        if (!current || current.matchId !== incoming.matchId || incoming.revision >= current.revision) {
          l12State.game = incoming
          l12State.spectating = Boolean(message.spectating)
          l12State.gmEnabled = Boolean(message.gmEnabled)
          l12State.pendingAction = false
          l12State.rankedSettlement = message.rankedSettlement || null
          if (message.recovered || l12State.notice.includes('正在同步')) l12State.notice = ''
        }
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
      l12State.notice = '暂时无法连接服务器，正在自动重试。'
      if (!settled) { settled = true; reject(new Error(l12State.notice)) }
    }
    socket.onclose = () => {
      if (l12State.socket === socket) {
        clearHeartbeat()
        clearMatchmakingPolling()
        l12State.matchmaking = null
        l12State.socket = null
        l12State.status = 'offline'
        l12State.pendingAction = false
        l12State.gmEnabled = false
        if (!settled) { settled = true; reject(new Error(l12State.notice || '连接已关闭')) }
        scheduleReconnect()
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
  disconnect()
}

export function disconnect() {
  automaticConnectionEnabled = false
  clearReconnectTimer()
  clearHeartbeat()
  clearMatchmakingPolling()
  l12State.socket?.close()
  l12State.socket = null
  l12State.status = 'offline'
  l12State.sessionId = ''
  l12State.room = null
  l12State.game = null
  l12State.spectating = false
  l12State.leavingRoom = false
  l12State.gmEnabled = false
  l12State.pendingAction = false
  l12State.matchmaking = null
  l12State.rankedSettlement = null
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
