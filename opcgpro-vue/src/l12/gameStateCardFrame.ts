export const GAME_STATE_CARD_FRAME_CAPABILITY = 'gameStateCardFrameV1'
export const GAME_STATE_CARD_FRAME_TYPE = 'gameStateCardFrameV1'

const MAX_WIRE_BYTES = 2 * 1024 * 1024
const MAX_EVENTS = 128
const MAX_REFERENCES = 1024
const encoder = new TextEncoder()

type JsonObject = Record<string, any>

function isObject(value: unknown): value is JsonObject {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function byteLength(value: unknown) {
  return encoder.encode(JSON.stringify(value)).byteLength
}

function cloneJson<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

/** Restore and validate the whole packet before exposing any reconstructed payload. */
export function restoreGameStateCardFrame(value: unknown, encoded?: string): JsonObject | null {
  try {
    if (!isObject(value) || value.type !== GAME_STATE_CARD_FRAME_TYPE
      || Object.keys(value).sort().join(',') !== 'cardSnapshots,eventCardRefs,lastActionCardRefs,payload,type'
      || (encoded === undefined ? byteLength(value) : encoder.encode(encoded).byteLength) > MAX_WIRE_BYTES) return null
    const payload = value.payload
    const state = payload?.state
    const events = state?.recentEvents
    const eventCardRefs = value.eventCardRefs
    const snapshots = value.cardSnapshots
    if (!isObject(payload) || payload.type !== 'gameState' || !isObject(state)
      || !Array.isArray(events) || events.length > MAX_EVENTS
      || !Array.isArray(eventCardRefs) || eventCardRefs.length !== events.length
      || !Array.isArray(snapshots) || snapshots.length > MAX_REFERENCES
      || !snapshots.every(isObject)) return null

    const snapshotSizes = snapshots.map(snapshot => byteLength(snapshot))
    const usedSnapshots = new Set<number>()
    let referenceCount = 0
    let expandedBytes = byteLength(payload)
    const assignments: Array<{ owner: JsonObject, refs: number[] }> = []
    const plan = (owner: unknown, refs: unknown) => {
      if (!isObject(owner) || owner.cards !== null || !Array.isArray(refs)) return false
      for (const ref of refs) {
        if (!Number.isSafeInteger(ref) || ref < 0 || ref >= snapshots.length
          || ++referenceCount > MAX_REFERENCES) return false
        expandedBytes += snapshotSizes[ref] + 1
        if (expandedBytes > MAX_WIRE_BYTES) return false
        usedSnapshots.add(ref)
      }
      assignments.push({ owner, refs })
      return true
    }
    for (let index = 0; index < events.length; index += 1)
      if (!plan(events[index], eventCardRefs[index])) return null
    if (state.lastAction === null) {
      if (value.lastActionCardRefs !== null) return null
    } else if (!plan(state.lastAction, value.lastActionCardRefs)) return null
    if (usedSnapshots.size !== snapshots.length) return null

    // Every reference receives an independent value. Animation/view consumers can never mutate a
    // dictionary object shared with another event, player projection, or later frame.
    for (const assignment of assignments)
      assignment.owner.cards = assignment.refs.map(ref => cloneJson(snapshots[ref]))
    if (byteLength(payload) > MAX_WIRE_BYTES) return null
    return payload
  } catch {
    return null
  }
}

function identity(message: unknown) {
  if (!isObject(message) || message.type !== 'gameState' || !isObject(message.state)) return ''
  const matchId = String(message.state.matchId || '')
  const revision = Number(message.state.revision)
  if (!matchId || !Number.isSafeInteger(revision) || revision < 0) return ''
  return `${matchId}\u0000${revision}\u0000${String(message.state.stateHash || '')}`
}

/**
 * Pure per-connection recovery state. Socket I/O stays in net.ts; this object only proves that a
 * corrupt packed full frame starts one plain recovery and that a valid plain frame arrived before
 * recovery completes. Every valid gameState still follows net.ts's original complete consumer path.
 */
export class GameStateCardFrameRecovery {
  private disabledForConnection = false
  private recovering = false
  private pendingRequestId = ''
  private publishedIdentity = ''

  get active() { return this.recovering }
  get disabled() { return this.disabledForConnection }
  get hasPublishedPlainFrame() { return this.publishedIdentity !== '' }

  begin(pendingRequestId: unknown) {
    if (this.disabledForConnection) return false
    this.disabledForConnection = true
    this.recovering = true
    this.pendingRequestId = String(pendingRequestId || '')
    this.publishedIdentity = ''
    return true
  }

  recordPublishedPlainFrame(message: unknown) {
    if (!this.recovering) return
    this.publishedIdentity = identity(message)
  }

  finish() {
    if (!this.recovering || !this.publishedIdentity)
      return { ready: false, pendingRequestId: '' }
    this.recovering = false
    const pendingRequestId = this.pendingRequestId
    this.pendingRequestId = ''
    this.publishedIdentity = ''
    return { ready: true, pendingRequestId }
  }

  reset() {
    this.disabledForConnection = false
    this.recovering = false
    this.pendingRequestId = ''
    this.publishedIdentity = ''
  }
}
