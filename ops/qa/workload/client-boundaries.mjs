import { performance } from 'node:perf_hooks'

const finite = n => Number.isFinite(n) && n >= 0
const commandKinds = new Map([['playCard', 1], ['endTurn', 2], ['surrender', 3]])

// Sidecar for an already-authenticated synthetic peer. It does not own business
// dispatch, parse a second copy, change acknowledgements, or export payloads.
// callbackAt is application message-callback entry, NOT native first-byte time.
export class ClientBoundaries {
  #nonce; #send; #now; #rows = new Map(); #keys = new Map(); #invalid = 0; #drift = 0
  constructor({ nonce, sendReceipt, now = () => performance.now() }) {
    if (!/^[a-f0-9]{32}$/.test(nonce) || typeof sendReceipt !== 'function') throw new Error('invalid probe setup')
    this.#nonce = nonce; this.#send = sendReceipt; this.#now = now
  }
  begin({ slot, connectionSlot, requestId, commandType, sentAt = this.#now() }) {
    if (!Number.isInteger(slot) || slot < 0 || slot >= 6 || this.#rows.has(slot)
      || !Number.isInteger(connectionSlot) || connectionSlot < 0 || connectionSlot >= 4
      || !commandKinds.has(commandType) || !finite(sentAt)
      || typeof requestId !== 'string'
      || !new RegExp(`^l12diag:${this.#nonce}:${slot}:[a-f0-9]{32}$`).test(requestId)) {
      this.#invalid++; return false
    }
    this.#keys.set(requestId, { slot, connectionSlot })
    this.#rows.set(slot, { slot, connection_slot: connectionSlot, command_kind: commandKinds.get(commandType),
      sent_at_ms: sentAt, response_seen: 0, response_kind: 0, action_accepted: 0, terminal: 0,
      callback_valid: 0, parse_valid: 0, dispatch_valid: 0, receipt_send_valid: 0,
      send_to_callback_ms: 0, parse_ms: 0, dispatch_ms: 0, receipt_send_ms: 0,
      callback_to_receipt_ms: 0, wire_bytes: 0, exceeded_original_3s: 0 })
    return true
  }
  observeParsed({ connectionSlot, message, callbackAt, parseStartedAt, parsedAt, bytes }) {
    let requestId = message?.requestId
    if (message?.type === 'gameStateDelta') {
      const changes = Array.isArray(message.changes) ? message.changes : []
      const matching = changes.filter(c => Array.isArray(c?.path) && c.path.length === 1 && c.path[0] === 'requestId')
      if (matching.length !== 1 || matching[0].remove === true) return null
      requestId = matching[0].value
    }
    const key = this.#keys.get(requestId)
    if (!key || key.connectionSlot !== connectionSlot) return null
    const row = this.#rows.get(key.slot)
    if (row.response_seen) return null
    if (![callbackAt, parseStartedAt, parsedAt].every(finite) || callbackAt < row.sent_at_ms
      || parseStartedAt < callbackAt || parsedAt < parseStartedAt || !Number.isSafeInteger(bytes) || bytes < 0) {
      this.#invalid++; return null
    }
    const responseKind = ({ gameState: 1, gameStateDelta: 2, actionRejected: 3, error: 4 })[message?.type]
    if (!responseKind) return null
    row.response_seen = row.callback_valid = row.parse_valid = 1
    row.response_kind = responseKind
    row.action_accepted = Number(responseKind === 1 || responseKind === 2)
    row.terminal = Number(responseKind === 1 && message.state?.phase === 'GameOver')
    row.send_to_callback_ms = callbackAt - row.sent_at_ms
    row.parse_ms = parsedAt - parseStartedAt
    row.wire_bytes = bytes
    row.exceeded_original_3s = Number(row.send_to_callback_ms > 3000)
    // Receipt goes through the peer's existing single sender, on this same socket.
    // A send failure stays a failed observation; it cannot affect business dispatch.
    try {
      const before = this.#now()
      this.#send(connectionSlot, { type: 'ping', diagnosticNonce: this.#nonce, diagnosticSlot: key.slot })
      const after = this.#now()
      if (!finite(before) || !finite(after) || before < parsedAt || after < before) throw new Error('invalid sidecar clock')
      row.receipt_send_valid = 1; row.receipt_send_ms = after - before
      row.callback_to_receipt_ms = before - callbackAt
    } catch { /* Numeric missing flag, not a network-loss diagnosis. */ }
    return { slot: key.slot, parsedAt }
  }
  afterDispatch(marker, completedAt = this.#now()) {
    const row = marker && this.#rows.get(marker.slot)
    if (!row || !row.response_seen || row.dispatch_valid || !finite(completedAt) || completedAt < marker.parsedAt) {
      this.#invalid++; return false
    }
    row.dispatch_valid = 1; row.dispatch_ms = completedAt - marker.parsedAt
    return true
  }
  recordLoopDrift(milliseconds) {
    if (!finite(milliseconds)) { this.#invalid++; return }
    this.#drift = Math.max(this.#drift, milliseconds)
  }
  report() {
    return { schema: 2, client_only: 1, native_first_byte_measured: 0, one_way_network_measured: 0,
      original_threshold_ms: 3000, expected_samples: this.#rows.size, invalid_records: this.#invalid,
      maximum_event_loop_drift_ms: this.#drift,
      rows: [...this.#rows.values()].sort((a, b) => a.slot - b.slot).map(({ sent_at_ms, ...numeric }) => numeric) }
  }
}
