import type { SavedL12Deck } from '@/l12/decks'
import type { PublishedDeck } from '@/l12/platform'

export function preservePublicDeckDetails<T extends { details?: unknown }>(current: T | null, updated: T): T {
  return { ...updated, details: updated.details ?? current?.details }
}

export function matchesPublishedDeckReference(deck: SavedL12Deck, published: PublishedDeck, ownerId?: string) {
  const publicationId = deck.publicationId?.trim()
  const publicationVersion = deck.publicationVersion
  return Boolean(publicationId && publicationVersion
    && published.id === publicationId
    && published.deck.publicationId === publicationId
    && published.deck.publicationVersion === publicationVersion
    && (!ownerId || published.ownerId === ownerId))
}

export function publicDeckRouteReference(published: PublishedDeck) {
  return published.publicCode?.trim() || ''
}
