import { reactive } from 'vue'
import type { GameState, RoomState } from './types'
import type { SavedL12Deck } from './decks'

function normalizeEndpoint(value: string) {
  return value.trim().replace(/\/ws\/$/, '/ws')
}

const defaultEndpoint = `ws://${location.hostname || 'localhost'}:8080/ws`

export const l12State = reactive({
  socket: null as WebSocket | null,
  status: 'offline' as 'offline' | 'connecting' | 'online',
  nickname: localStorage.getItem('l12-nickname') || '',
  endpoint: normalizeEndpoint(localStorage.getItem('l12-endpoint') || defaultEndpoint),
  sessionId: '',
  room: null as RoomState | null,
  game: null as GameState | null,
  spectating: false,
  pendingAction: false,
  notice: '',
})

export function connect(): Promise<void> {
  if (l12State.socket?.readyState === WebSocket.OPEN) return Promise.resolve()
  l12State.status = 'connecting'
  l12State.notice = ''
  localStorage.setItem('l12-nickname', l12State.nickname.trim())
  l12State.endpoint = normalizeEndpoint(l12State.endpoint)
  localStorage.setItem('l12-endpoint', l12State.endpoint)
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(l12State.endpoint)
    l12State.socket = socket
    socket.onopen = () => {
      l12State.status = 'online'
      send({ type: 'hello', name: l12State.nickname.trim() })
      resolve()
    }
    socket.onmessage = (event) => {
      const message = JSON.parse(String(event.data))
      if (message.type === 'session') l12State.sessionId = message.sessionId
      else if (message.type === 'roomState') l12State.room = message
      else if (message.type === 'roomLeft' || message.type === 'roomClosed') {
        l12State.room = null
        l12State.game = null
        l12State.spectating = false
        l12State.pendingAction = false
        l12State.notice = message.message || ''
      }
      else if (message.type === 'gameState') { l12State.game = message.state; l12State.spectating = Boolean(message.spectating); l12State.pendingAction = false }
      else if (message.type === 'error' || message.type === 'actionRejected' || message.type === 'deckRejected') { l12State.notice = message.message; l12State.pendingAction = false }
    }
    socket.onerror = () => {
      l12State.status = 'offline'
      l12State.notice = '无法连接服务器，请确认 C# 服务端已启动。'
      reject(new Error(l12State.notice))
    }
    socket.onclose = () => { l12State.status = 'offline'; l12State.pendingAction = false }
  })
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
  spectating: 'public' | 'friends' | 'disabled'
  handVisibility: 'request' | 'public'
  disasterMode: 'all' | 'random' | 'season' | 'none'
}

export const createRoom = (options?: RoomOptions) => { l12State.spectating = false; send({ type: 'createRoom', options }) }
export const joinRoom = (roomCode: string) => { l12State.spectating = false; send({ type: 'joinRoom', roomCode }) }
export const spectateRoom = (roomCode: string) => send({ type: 'spectateRoom', roomCode })
export const selectDeck = (deckIndex: number) => send({ type: 'selectDeck', deckIndex })
export const selectCustomDeck = (deck: SavedL12Deck) => send({ type: 'selectCustomDeck', deck })
export const setReady = (ready: boolean) => send({ type: 'ready', ready })
export const leaveRoom = () => send({ type: 'leaveRoom' })
export function gameAction(command: Record<string, unknown>) {
  if (l12State.pendingAction) return
  l12State.pendingAction = true
  send({ type: 'gameAction', command })
  if (l12State.socket?.readyState !== WebSocket.OPEN) l12State.pendingAction = false
}
