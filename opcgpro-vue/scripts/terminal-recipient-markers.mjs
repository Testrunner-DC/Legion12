import crypto from 'node:crypto'

export const RECIPIENT_WINDOW_MS = 15000
const finite = value => Number.isFinite(value) && value >= 0
const integer = value => Number.isSafeInteger(value) && value >= 0

// Optional numeric observation of already chosen terminal commands. This class
// never chooses/retries a game action, changes a timeout, or exports identities.
export class TerminalRecipientMarkers {
  #nonce; #now; #expected; #onMarker
  #rooms = new Map(); #clients = new Map(); #tokens = new WeakMap()
  #startedAt = null; #invalid = 0; #expired = 0; #driftCount = 0; #driftMax = 0

  constructor({nonce, expectedCommands = 30, now = () => performance.now(), onMarker = () => {}}) {
    if (!/^[a-f0-9]{32}$/.test(nonce) || !Number.isInteger(expectedCommands)
      || expectedCommands < 1 || expectedCommands > 32 || typeof now !== 'function'
      || typeof onMarker !== 'function') throw new Error('INVALID_TERMINAL_RECIPIENT_SETUP')
    this.#nonce = nonce; this.#expected = expectedCommands; this.#now = now; this.#onMarker = onMarker
  }

  bindRoom(room) {
    try {
      const clients = [room.host, room.guest], slot = clients[0].numericRoomSlot
      if (!Number.isInteger(slot) || slot < 0 || slot >= this.#expected || this.#rooms.has(slot)
        || clients[0] === clients[1] || clients.some(client => this.#clients.has(client)
          || client.numericRoomSlot !== slot || typeof client.send !== 'function')
        || clients[0].numericClientSlot !== slot * 2 || clients[1].numericClientSlot !== slot * 2 + 1
        || ![0, 1].includes(clients[0].playerIndex) || clients[1].playerIndex !== 1 - clients[0].playerIndex
        || typeof room.expectedMatchId !== 'string' || !room.expectedMatchId.length) throw new Error()
      const record = {slot, clients, matchId: room.expectedMatchId, requestId: null, rows: []}
      this.#rooms.set(slot, record)
      for (const client of clients) this.#clients.set(client, record)
      return true
    } catch { this.#invalid++; return false }
  }

  tryBegin({stage, client, commandType, roomSlot, sentAt = this.#now()}) {
    if (stage !== 'cleanup' || commandType !== 'surrender') return null
    const room = this.#rooms.get(roomSlot)
    if (!room || !room.clients.includes(client) || room.requestId !== null || !finite(sentAt)) {
      this.#invalid++; return null
    }
    if (this.#startedAt !== null && sentAt >= this.#startedAt + RECIPIENT_WINDOW_MS) {
      this.#expired++; return null
    }
    const views = room.clients.map(peer => peer.envelope?.state)
    if (views.some((state, index) => state?.matchId !== room.matchId
      || state.you !== room.clients[index].playerIndex || state.phase === 'GameOver'
      || !integer(state.revision)) || views[0].revision !== views[1].revision) {
      this.#invalid++; return null
    }
    this.#startedAt ??= sentAt
    room.requestId = `l12diag:${this.#nonce}:${roomSlot}:${crypto.randomBytes(16).toString('hex')}`
    room.sentAt = sentAt; room.beforeRevision = views[0].revision
    room.rows = [client, room.clients.find(peer => peer !== client)].map((peer, role) => ({
      client: peer, role, seen: false, dispatched: false, receiptSent: false,
    }))
    try { this.#onMarker({schema: 4, kind: 3, slot: roomSlot, marker_sent: 1}) }
    catch { this.#invalid++ }
    return room.requestId
  }

  // Called by the workload's existing native-send observation, not by the
  // request-id factory. Missing this boundary makes the trace incomplete.
  observeNativeSend(client, requestId, timestamp = this.#now()) {
    const room = this.#clients.get(client)
    if (!room || room.requestId === null || room.requestId !== requestId || room.rows[0]?.client !== client
      || room.nativeSendAt !== undefined || room.rows.some(row => row.seen)
      || !finite(timestamp) || timestamp < room.sentAt
      || timestamp >= this.#startedAt + RECIPIENT_WINDOW_MS) { this.#invalid++; return false }
    room.nativeSendAt = timestamp
    return true
  }

  observeParsed(client, observation) {
    const room = this.#clients.get(client), row = room?.rows.find(row => row.client === client)
    const message = observation?.message, state = message?.state
    if (!row || row.seen || message?.type !== 'gameState' || state?.phase !== 'GameOver'
      || state.matchId !== room.matchId) return null
    if (state.you !== client.playerIndex || !integer(state.revision) || state.revision <= room.beforeRevision
      || (row.role === 0 ? message.requestId !== room.requestId : message.requestId != null)
      || !finite(observation.callbackAt) || observation.callbackAt < room.sentAt
      || !finite(observation.parseStartedAt) || observation.parseStartedAt < observation.callbackAt
      || !finite(observation.parsedAt) || observation.parsedAt < observation.parseStartedAt
      || !integer(observation.bytes) || observation.bytes < 1 || observation.bytes > 2 * 1024 ** 2) {
      this.#invalid++; return null
    }
    if (observation.callbackAt >= this.#startedAt + RECIPIENT_WINDOW_MS) { this.#expired++; return null }
    row.seen = true; row.revision = state.revision; row.callbackAt = observation.callbackAt
    row.parseStartedAt = observation.parseStartedAt; row.parsedAt = observation.parsedAt; row.bytes = observation.bytes
    const marker = Object.freeze({})
    this.#tokens.set(marker, {room, row})
    return marker
  }

  afterDispatch(marker, completedAt = this.#now()) {
    if (!marker) return false
    const entry = this.#tokens.get(marker)
    if (!entry) { this.#invalid++; return false }
    this.#tokens.delete(marker)
    const {room, row} = entry
    if (!finite(completedAt) || completedAt < row.parsedAt
      || completedAt >= this.#startedAt + RECIPIENT_WINDOW_MS) { this.#invalid++; return false }
    row.dispatched = true; row.completedAt = completedAt
    try {
      // Dispatch timestamps are already captured; this ping is not a game ACK.
      row.client.send({type: 'ping', diagnosticNonce: this.#nonce,
        diagnosticSlot: room.slot, diagnosticRecipient: row.role})
      row.receiptSent = true
    } catch { this.#invalid++ }
    return row.receiptSent
  }

  recordLoopDrift(milliseconds) {
    if (!finite(milliseconds)) { this.#invalid++; return }
    this.#driftCount++; this.#driftMax = Math.max(this.#driftMax, milliseconds)
  }

  report() {
    const microseconds = value => Math.round(value * 1000)
    const rooms = [...this.#rooms.values()].filter(room => room.requestId !== null)
    const rows = rooms.flatMap(room => room.rows.filter(row => row.seen).map(row => ({
      slot: room.slot, recipient: row.role, client_slot: row.client.numericClientSlot,
      revision: row.revision, before_revision: room.beforeRevision,
      marker_to_callback_us: microseconds(row.callbackAt - room.sentAt),
      native_send_valid: Number(finite(room.nativeSendAt) && row.callbackAt >= room.nativeSendAt),
      callback_us: finite(room.nativeSendAt) ? microseconds(row.callbackAt - room.nativeSendAt) : 0,
      parse_us: microseconds(row.parsedAt - row.parseStartedAt),
      dispatch_us: row.dispatched ? microseconds(row.completedAt - row.parsedAt) : 0,
      dispatch_valid: Number(row.dispatched), receipt_send_valid: Number(row.receiptSent), wire_bytes: row.bytes,
      exceeded_original_3s: Number(finite(room.nativeSendAt) && row.callbackAt - room.nativeSendAt > 3000),
    }))).sort((a, b) => a.slot - b.slot || a.recipient - b.recipient)
    const complete = this.#invalid === 0 && this.#expired === 0 && rooms.length === this.#expected
      && rows.length === this.#expected * 2 && rows.every(row => row.native_send_valid && row.dispatch_valid && row.receipt_send_valid)
      && rooms.every(room => room.rows[0].revision === room.rows[1].revision)
    return {schema: 4, mode: 'terminal-recipients-v4', expected_commands: this.#expected,
      begun_commands: rooms.length, expected_recipients: this.#expected * 2, observed_recipients: rows.length,
      absolute_window_ms: RECIPIENT_WINDOW_MS, invalid_records: this.#invalid, expired_records: this.#expired,
      drift_count: this.#driftCount, maximum_drift_us: microseconds(this.#driftMax),
      trace_complete: Number(complete), original_3s_satisfied: Number(complete && rows.every(row => !row.exceeded_original_3s)),
      online_capacity_accepted: 0, rows}
  }
}
