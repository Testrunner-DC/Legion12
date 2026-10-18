import type { ActionEvent, GameState } from '../types'

export type AttackConnectionRect = {
  left: number
  top: number
  width: number
  height: number
}

export type AttackConnectionGeometry = {
  startX: number
  startY: number
  controlX: number
  controlY: number
  endX: number
  endY: number
  path: string
}

export type AttackConnectionClaimState = {
  initialized: boolean
  matchId: string
  revision: number
  lastEventSequence: number
}

export type AttackConnectionClaim = {
  kind: 'none' | 'baseline' | 'retain' | 'intro'
  sequence?: number
}

type PendingDefense = NonNullable<GameState['pendingDefense']>

const point = (value: number) => Number(value.toFixed(2))

function rectEdge(rect: AttackConnectionRect, unitX: number, unitY: number, outward: boolean) {
  const centerX = rect.left + rect.width / 2
  const centerY = rect.top + rect.height / 2
  const halfWidth = Math.max(1, rect.width / 2)
  const halfHeight = Math.max(1, rect.height / 2)
  const extent = 1 / Math.max(Math.abs(unitX) / halfWidth, Math.abs(unitY) / halfHeight, .0001)
  const direction = outward ? 1 : -1
  return {
    x: centerX + unitX * extent * direction,
    y: centerY + unitY * extent * direction,
  }
}

/** Build a restrained outward arc between the closest edges of two objects. */
export function attackConnectionGeometry(source: AttackConnectionRect, target: AttackConnectionRect,
  viewportWidth: number): AttackConnectionGeometry | null {
  const values = [source.left, source.top, source.width, source.height,
    target.left, target.top, target.width, target.height, viewportWidth]
  if (values.some(value => !Number.isFinite(value)) || source.width <= 0 || source.height <= 0
    || target.width <= 0 || target.height <= 0 || viewportWidth <= 0) return null
  const sourceCenterX = source.left + source.width / 2
  const sourceCenterY = source.top + source.height / 2
  const targetCenterX = target.left + target.width / 2
  const targetCenterY = target.top + target.height / 2
  const dx = targetCenterX - sourceCenterX
  const dy = targetCenterY - sourceCenterY
  const distance = Math.hypot(dx, dy)
  if (distance < 2) return null
  const unitX = dx / distance
  const unitY = dy / distance
  const start = rectEdge(source, unitX, unitY, true)
  const end = rectEdge(target, unitX, unitY, false)
  const span = Math.max(1, Math.hypot(end.x - start.x, end.y - start.y))
  const midpointX = (start.x + end.x) / 2
  const midpointY = (start.y + end.y) / 2
  // Keep the relationship nearly straight; the combat information surface may
  // cover the middle of the line instead of forcing a conspicuous detour.
  const outwardX = midpointX < viewportWidth / 2 ? -1 : 1
  const bend = Math.min(20, Math.max(8, span * .035))
  let perpendicularX = -unitY
  let perpendicularY = unitX
  if (Math.abs(perpendicularX) > .01 && perpendicularX * outwardX < 0) {
    perpendicularX *= -1
    perpendicularY *= -1
  }
  const controlX = midpointX + perpendicularX * bend
  const controlY = midpointY + perpendicularY * bend
  const geometry = {
    startX: point(start.x),
    startY: point(start.y),
    controlX: point(controlX),
    controlY: point(controlY),
    endX: point(end.x),
    endY: point(end.y),
    path: '',
  }
  geometry.path = `M ${geometry.startX} ${geometry.startY} Q ${geometry.controlX} ${geometry.controlY} ${geometry.endX} ${geometry.endY}`
  return geometry
}

export function attackConnectionSourceColor(attackerPlayerIndex: number, viewerPlayerIndex: number) {
  return attackerPlayerIndex === viewerPlayerIndex ? '#65dce4' : '#f06f78'
}

export function createAttackConnectionClaimState(): AttackConnectionClaimState {
  return { initialized: false, matchId: '', revision: 0, lastEventSequence: 0 }
}

function isMatchingAttack(event: ActionEvent, pending: PendingDefense) {
  if (event.type !== 'attack') return false
  const attackerId = event.playerCombat?.attackerInstanceId ?? event.cards?.[0]?.instanceId
  if (attackerId !== pending.attackerInstanceId) return false
  if (pending.target.type === 'master') return !event.playerCombat?.targetInstanceId && !event.cards?.[1]?.instanceId
  const targetId = event.playerCombat?.targetInstanceId ?? event.cards?.[1]?.instanceId
  return targetId === pending.target.instanceId
}

/**
 * One authority event may introduce the arc once. Initial/recovery snapshots
 * retain spatial context without replaying historical draw-on motion.
 */
export function claimAttackConnection(state: AttackConnectionClaimState, input: {
  matchId: string
  revision: number
  synchronizing: boolean
  events: ActionEvent[]
  pending: GameState['pendingDefense']
}): AttackConnectionClaim {
  const highestSequence = Math.max(0, ...input.events.map(event => event.sequence))
  const boundary = !state.initialized || state.matchId !== input.matchId
    || input.synchronizing || input.revision < state.revision
  if (boundary) {
    state.initialized = true
    state.matchId = input.matchId
    state.revision = input.revision
    state.lastEventSequence = highestSequence
    return { kind: input.pending ? 'baseline' : 'none' }
  }

  const fresh = input.events.filter(event => event.sequence > state.lastEventSequence)
    .sort((left, right) => left.sequence - right.sequence)
  state.revision = Math.max(state.revision, input.revision)
  state.lastEventSequence = Math.max(state.lastEventSequence, highestSequence)
  if (!input.pending) return { kind: 'none' }
  const attack = fresh.reverse().find(event => isMatchingAttack(event, input.pending!))
  return attack ? { kind: 'intro', sequence: attack.sequence } : { kind: 'retain' }
}
