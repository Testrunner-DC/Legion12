import type { ActionEvent } from '../types'

export type PublicRevealPresentationDecision =
  | { kind: 'full-card' }
  | { kind: 'linked-continuation'; parentSequence: number }

type RevealCorrelation = {
  sequence: number
  playerIndex: number
  instanceId: string
}

// These are catalog identities, not per-activation identities.  They are kept
// exact because the current server has exactly one search-add producer: the
// Oiran resolver emits reveal-add, performs the private hand move, and then
// synchronously emits search-add for that same selected card.  Sequence
// proximity is only a defensive shape check; it never establishes this
// relationship for another ability or scene.
const OIRAN_GIFT_ABILITY_ID = 'S01-0419:ability:play:97db2728de7886de'

function exactOiranScene(event: ActionEvent, sceneKey: 'reveal-add' | 'search-add') {
  const abilityId = event.effectAbilityId?.trim()
  const sceneId = event.effectSceneId?.trim()
  const cards = event.cards ?? []
  const card = cards.length === 1 ? cards[0] : undefined
  const playerIndex = event.playerIndex
  if (abilityId !== OIRAN_GIFT_ABILITY_ID
    || sceneId !== `${OIRAN_GIFT_ABILITY_ID}:presentation:${sceneKey}`
    || !card?.instanceId || card.hidden === true
    || typeof playerIndex !== 'number' || !Number.isSafeInteger(playerIndex) || playerIndex < 0) return null
  return {
    sequence: event.sequence,
    playerIndex,
    instanceId: card.instanceId,
  }
}

function matchesProvenOiranProducerPair(reveal: RevealCorrelation, search: RevealCorrelation) {
  return search.sequence > reveal.sequence
    // The authority producer emits at most the intervening private hand-add
    // fact between these two public facts. A wider gap is not proven to be the
    // same presentation and must keep the full-card fallback.
    && search.sequence - reveal.sequence <= 2
    && search.playerIndex === reveal.playerIndex
    && search.instanceId === reveal.instanceId
}

/**
 * Tracks only the immediately preceding public-card presentation candidate.
 * A fully identified Oiran reveal-add/search-add producer pair may share one
 * card-face presentation. Missing metadata, hidden cards, interleaved or
 * ambiguous presentations and every later independent reveal retain the
 * existing full-card fallback.
 */
export function createPublicRevealPresentationTracker() {
  let pendingReveal: RevealCorrelation | null = null

  return {
    classify(event: ActionEvent): PublicRevealPresentationDecision {
      const reveal = event.type === 'reveal' ? exactOiranScene(event, 'reveal-add') : null
      const search = event.type === 'search' ? exactOiranScene(event, 'search-add') : null
      const linked = Boolean(search && pendingReveal && matchesProvenOiranProducerPair(pendingReveal, search))
      const parentSequence = linked ? pendingReveal?.sequence : undefined
      // Two reveal starts without the expected continuation are ambiguous. Do
      // not silently pair the second activation with a later result merely
      // because catalog identity, selected instance and sequence happen to fit.
      pendingReveal = reveal && !pendingReveal ? reveal : null
      return parentSequence !== undefined
        ? { kind: 'linked-continuation', parentSequence }
        : { kind: 'full-card' }
    },
    reset() {
      pendingReveal = null
    },
  }
}

/**
 * A producer-proven child can share presentation time only with the exact
 * parent reserved by this same delivered batch. Requiring the queue tail keeps
 * an unrelated authority reservation from being skipped or reordered.
 */
export function findLinkedPublicContinuationParent<T extends { sequence: number }>(
  decision: PublicRevealPresentationDecision,
  batchParents: ReadonlyMap<number, T>,
  queue: readonly T[],
): T | null {
  if (decision.kind !== 'linked-continuation') return null
  const parent = batchParents.get(decision.parentSequence)
  return parent && queue.at(-1) === parent ? parent : null
}
