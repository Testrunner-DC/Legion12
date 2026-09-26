import type { ActionEvent, Card, PlayerView, Prompt } from '../types'

export type VisualZone = 'hand' | 'library' | 'field' | 'graveyard' | 'relic' | 'master' | 'center' | 'disaster' | 'resolving' | 'attached'

export function isCardEffectPresentationEvent(event: ActionEvent) {
  return Boolean(event.effectSceneId && event.effectResultStatus !== 'declared'
    && (event.type === 'effect-result' || event.type === 'effect-trigger'
      || event.type === 'effect-response' || event.type === 'effect-activation'))
}

export function cardEffectPresentationCards(event: ActionEvent) {
  // Structured effect events publish the source first. Remaining cards are
  // targets/results and must not be presented as additional activators.
  if (isCardEffectPresentationEvent(event)) return event.cards?.slice(0, 1) ?? []
  return event.cards ?? []
}

export function movementCardsForEvent(event: ActionEvent) {
  if (event.type === 'move' || event.type === 'attach' || event.type === 'mill') return event.cards ?? []
  return (event.cards ?? []).slice(0, 1)
}

export type MovementClaimState = {
  initialized: boolean
  lastSequence: number
  claimedKeys: Set<string>
}

export function createMovementClaimState(): MovementClaimState {
  return { initialized: false, lastSequence: 0, claimedKeys: new Set<string>() }
}

export function resetMovementClaimState(state: MovementClaimState, baselineSequence = 0) {
  // An explicit match reset establishes a baseline even when the new match has
  // no events yet. Its first later movement is live and must be animated.
  state.initialized = true
  state.lastSequence = baselineSequence
  state.claimedKeys.clear()
}

export function claimFreshMovementEvents(events: ActionEvent[], state: MovementClaimState) {
  const highest = Math.max(0, ...events.map(event => event.sequence))
  if (!state.initialized) {
    state.initialized = true
    state.lastSequence = highest
    return []
  }
  const fresh = events
    .filter(event => event.sequence > state.lastSequence)
    .sort((left, right) => left.sequence - right.sequence)
  // Commit the authoritative cursor before image decoding, nextTick, or the
  // global sequence coordinator can yield. Vue watchers may re-enter while
  // those asynchronous steps are pending; the same snapshot must not be
  // claimed a second time.
  state.lastSequence = Math.max(state.lastSequence, highest)
  const oldestRetained = Math.min(highest, ...events.map(event => event.sequence))
  for (const key of state.claimedKeys) {
    const separator = key.indexOf(':')
    if (separator > 0 && Number(key.slice(0, separator)) < oldestRetained) state.claimedKeys.delete(key)
  }
  return fresh
}

export function movementFactKey(event: ActionEvent, cardIndex: number, card: Card | undefined,
  from: VisualZone, to: VisualZone) {
  return `${event.sequence}:${cardIndex}:${card?.instanceId ?? 'cardless'}:${from}>${to}`
}

export function claimMovementFact(state: MovementClaimState, key: string) {
  if (state.claimedKeys.has(key)) return false
  state.claimedKeys.add(key)
  return true
}

export function isMovementCardConcealed(event: ActionEvent, card: Card | undefined, from?: VisualZone) {
  // A normal library never exposes its top identity. Even when the resulting
  // graveyard event carries the now-public card snapshot, the flight starts as
  // the official deck back and the destination becomes visible only afterward.
  if (from === 'library') return true
  // identityKnown is meaningful only for a card that is still covered. Normal
  // authoritative event cards keep the model default false even after their
  // identity has become public (for example, an opponent playing from hand).
  if (!card || event.type === 'counter-set') return true
  if (!card.cardId || card.cardId === 'hidden-card') return true
  return card.hidden === true && card.identityKnown !== true
}

export function isCombatDefeatLeaveEvent(event: ActionEvent) {
  return event.type === 'leave' && /阵亡|击杀|兵力不高于0|承受.*致命/.test(event.text)
}

export function isSupersededLeaveEvent(event: ActionEvent, batchEvents: ActionEvent[]) {
  if (event.type !== 'leave') return false
  const instanceIds = new Set((event.cards ?? []).map(card => card.instanceId))
  return batchEvents.some(candidate => candidate !== event
    && ['grave', 'discard', 'return', 'move', 'derived-vanished'].includes(candidate.type)
    && (candidate.cards ?? []).some(card => instanceIds.has(card.instanceId)))
}

export function leaveMovementDestination(event: ActionEvent): VisualZone {
  if (event.cards?.[0]?.isMasterLegion || /返回.*主宰区/.test(event.text)) return 'master'
  if (/返回.*手牌|加入.*手牌/.test(event.text)) return 'hand'
  if (/返回.*牌库|牌库顶|牌库底/.test(event.text)) return 'library'
  // A card that is explicitly removed/vanished has no graveyard destination.
  // Let it leave toward the neutral presentation anchor instead of implying a
  // graveyard move that never happened in the authoritative state.
  if (/移出|移除|消灭|不进入其他区域/.test(event.text)) return 'center'
  return 'graveyard'
}

function promptZone(value: string | undefined): VisualZone | null {
  if (value === '手牌') return 'hand'
  if (value === '牌库') return 'library'
  if (value === '墓地') return 'graveyard'
  if (value === '战场') return 'field'
  if (value === '圣物区') return 'relic'
  if (value === '主宰区') return 'master'
  return null
}

export function collectPromptSourceZoneHints(prompts: Prompt[]) {
  const result = new Map<string, VisualZone>()
  for (const prompt of prompts) for (const [key, value] of Object.entries(prompt.data ?? {})) {
    if (!key.endsWith(':zone')) continue
    const zone = promptZone(value)
    if (zone) result.set(key.slice(0, -':zone'.length), zone)
  }
  return result
}

export type VisualFieldState = { instanceId: string; tapped: boolean }

export function collectVisualFieldState(players: PlayerView[]) {
  const result = new Map<string, VisualFieldState>()
  for (const player of players) {
    for (const card of player.field.flat()) if (card) result.set(card.instanceId, { instanceId: card.instanceId, tapped: card.tapped })
    if (player.relic) result.set(player.relic.instanceId, { instanceId: player.relic.instanceId, tapped: player.relic.tapped })
    result.set(`master-${player.playerIndex}`, { instanceId: `master-${player.playerIndex}`, tapped: Boolean(player.master.tapped) })
  }
  return result
}

export function collectKnownCardZones(players: PlayerView[]) {
  const result = new Map<string, VisualZone>()
  const append = (cards: Array<Card | null | undefined> | undefined, zone: VisualZone) => {
    for (const card of cards ?? []) if (card?.instanceId) result.set(card.instanceId, zone)
  }
  for (const player of players) {
    append(player.hand, 'hand')
    append(player.graveyard, 'graveyard')
    append(player.resolving, 'resolving')
    append([player.libraryTop], 'library')
    append([player.relic, ...(player.extraRelics ?? [])], 'relic')
    for (const fieldCard of player.field.flat()) {
      append([fieldCard], 'field')
      append(fieldCard?.attachedCards, 'attached')
    }
  }
  return result
}

export function changedTappedStates(previous: Map<string, VisualFieldState>, next: Map<string, VisualFieldState>) {
  const changes: Array<{ instanceId: string; fromTapped: boolean; toTapped: boolean }> = []
  for (const [instanceId, nextState] of next) {
    const prior = previous.get(instanceId)
    if (prior && prior.tapped !== nextState.tapped)
      changes.push({ instanceId, fromTapped: prior.tapped, toTapped: nextState.tapped })
  }
  return changes
}
