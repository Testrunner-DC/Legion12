import type { LocationQueryRaw } from 'vue-router'

export const DEFAULT_DECK_EDITOR_RETURN = '/decks?tab=mine'

export function deckEditorReturnTarget(value: unknown): string {
  if (typeof value !== 'string' || !/^\/decks(?:[/?#]|$)/.test(value)) return DEFAULT_DECK_EDITOR_RETURN
  return value
}

export function deckEditorQuery(returnTo: unknown, deckName?: string, publicationId?: string): LocationQueryRaw {
  return {
    ...(deckName ? { deck: deckName } : {}),
    ...(publicationId ? { published: publicationId } : {}),
    returnTo: deckEditorReturnTarget(returnTo),
  }
}
