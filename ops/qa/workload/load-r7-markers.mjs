import crypto from 'node:crypto'
import { ClientBoundaries } from './client-boundaries.mjs'

export const LOAD_V2_WINDOW_MS = 15_000
export const TERMINAL_TRACE_ROOMS = Object.freeze([0,5,11,17,23,29])

const finite = value => Number.isFinite(value) && value >= 0

// This coordinator only labels six terminal commands already chosen by the preserved cleanup loop.
// It never selects, delays, retries, or sends a business command.
export class LoadV2Markers {
  #nonce
  #now
  #observers
  #connectionSlots = new Map()
  #requestConnections = new Map()
  #begunRooms = new Set()
  #startedAt = null
  #deadlineAt = null
  #invalid = 0
  #expiredBegins = 0
  #lateCallbacks = 0
  #onMarker

  constructor({ nonce, now = () => performance.now(), onMarker = () => {} }) {
    if (!/^[a-f0-9]{32}$/.test(nonce) || typeof now !== 'function'
        || typeof onMarker !== 'function') throw new Error('invalid-load-v2-marker-setup')
    this.#nonce = nonce
    this.#now = now
    this.#onMarker = onMarker
    this.#observers = new Map()
  }

  tryBegin({ stage, client, commandType, roomSlot, sentAt = this.#now() }) {
    const slot = TERMINAL_TRACE_ROOMS.indexOf(roomSlot)
    if (stage !== 'cleanup' || commandType !== 'surrender' || slot < 0 || this.#begunRooms.has(roomSlot)) return null
    if (!client || typeof client.send !== 'function' || !finite(sentAt)) { this.#invalid++; return null }
    if (this.#deadlineAt !== null && sentAt >= this.#deadlineAt) { this.#expiredBegins++; return null }
    // ClientBoundaries keeps its proven per-observer connection bound. Each
    // selected requesting socket has its own observer, as in the accepted r6.
    const observer = new ClientBoundaries({nonce:this.#nonce,now:this.#now,
      sendReceipt: (_connectionSlot,payload)=>client.send(payload)})
    const requestId = `l12diag:${this.#nonce}:${slot}:${crypto.randomUUID().replaceAll('-', '')}`
    if (!observer.begin({slot,connectionSlot:0,requestId,commandType,sentAt})) { this.#invalid++; return null }
    this.#observers.set(slot,observer)
    this.#connectionSlots.set(client,slot)
    this.#requestConnections.set(requestId,slot)
    this.#begunRooms.add(roomSlot)
    if (this.#startedAt === null) { this.#startedAt=sentAt;this.#deadlineAt=sentAt+LOAD_V2_WINDOW_MS }
    this.#onMarker({schema:1,kind:1,stageRooms:30,slot,commandKind:3,connectionSlot:slot,markerSent:1})
    return requestId
  }

  observeParsed(client, observation) {
    const connectionSlot = this.#connectionSlots.get(client)
    if (connectionSlot === undefined) return null
    let requestId = observation?.message?.requestId
    if (observation?.message?.type === 'gameStateDelta') {
      const matching = Array.isArray(observation.message.changes)
        ? observation.message.changes.filter(change => Array.isArray(change?.path)
          && change.path.length === 1 && change.path[0] === 'requestId' && change.remove !== true)
        : []
      requestId = matching.length === 1 ? matching[0].value : null
    }
    // Unrelated traffic on one of the six selected sockets never counts as a late
    // marked callback. Keep request identifiers private and out of every report.
    if (this.#requestConnections.get(requestId) !== connectionSlot) return null
    if (!finite(observation?.callbackAt)) {
      this.#invalid++
      return null
    }
    if (this.#deadlineAt !== null && observation.callbackAt >= this.#deadlineAt) {
      this.#lateCallbacks++
      return null
    }
    return this.#observers.get(connectionSlot).observeParsed({ ...observation, connectionSlot:0 })
  }

  afterDispatch(marker, completedAt = this.#now()) {
    if (!marker) return false
    return this.#observers.get(marker.slot)?.afterDispatch(marker, completedAt) ?? false
  }

  recordLoopDrift(milliseconds) {
    for (const observer of this.#observers.values()) observer.recordLoopDrift(milliseconds)
  }

  report(now = this.#now()) {
    const parts=[...this.#observers.values()].map(observer=>observer.report())
    const client={rows:parts.flatMap(part=>part.rows).sort((a,b)=>a.slot-b.slot),
      invalid_records:parts.reduce((n,part)=>n+part.invalid_records,0)}
    const rows = client.rows
    const complete = this.#invalid === 0 && this.#expiredBegins === 0 && this.#lateCallbacks === 0
      && client.invalid_records === 0 && this.#begunRooms.size === 6 && rows.length === 6
      && rows.every(row => row.response_seen === 1 && row.callback_valid === 1
        && row.parse_valid === 1 && row.dispatch_valid === 1 && row.receipt_send_valid === 1
        && row.terminal === 1 && row.response_kind === 1 && row.command_kind === 3
        && row.exceeded_original_3s === 0)
    return {
      schema: 1,
      mode: 'terminal-bounds-v2',
      expected_samples: 6,
      absolute_window_ms: LOAD_V2_WINDOW_MS,
      begun_samples: this.#begunRooms.size,
      play_card_begun: 0,
      end_turn_begun: 0,
      surrender_begun: this.#begunRooms.size,
      deadline_elapsed: Number(this.#deadlineAt !== null && finite(now) && now >= this.#deadlineAt),
      expired_begins: this.#expiredBegins,
      late_callbacks: this.#lateCallbacks,
      invalid_records: this.#invalid,
      client_complete: Number(complete),
      client,
    }
  }
}
