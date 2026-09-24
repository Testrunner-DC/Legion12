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
  if (event.type === 'move' || event.type === 'attach') return event.cards ?? []
  return (event.cards ?? []).slice(0, 1)
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
