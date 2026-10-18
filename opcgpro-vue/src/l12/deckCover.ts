import type { DeckCard } from './decks'

export const DECK_COVER_REQUEST_HEADERS = { 'X-L12-Deck-Cover': '1' } as const

export function deckCoverHeadersForOrigin(apiBase: string, pageOrigin?: string): Readonly<Record<string, string>> {
  if (!pageOrigin) return {}
  try {
    const page = new URL(pageOrigin), api = new URL(apiBase, pageOrigin)
    // Old cross-origin services cannot allow a new header in their CORS preflight.
    // Keep that directory usable without extra probes or full-deck fallback reads.
    return /^https?:$/.test(page.protocol) && /^https?:$/.test(api.protocol)
      && api.origin === page.origin ? DECK_COVER_REQUEST_HEADERS : {}
  } catch { return {} }
}

export const DECK_COVER_CARD_TYPES = ['legion', 'tactic', 'artifact'] as const
export type DeckCoverCardType = typeof DECK_COVER_CARD_TYPES[number]

const coverTypes = new Set<string>(DECK_COVER_CARD_TYPES)

export function isDeckCoverCard(card: DeckCard | undefined): card is DeckCard & { cardType: DeckCoverCardType } {
  return Boolean(card && coverTypes.has(card.cardType))
}

export function compareDeckCoverCards(left: DeckCard, right: DeckCard) {
  const cost = (right.cost ?? Number.NEGATIVE_INFINITY) - (left.cost ?? Number.NEGATIVE_INFINITY)
  if (cost) return cost
  const troops = (right.troops ?? 0) - (left.troops ?? 0)
  if (troops) return troops
  const number = (right.number || right.id).localeCompare(left.number || left.id, 'zh-CN', { numeric: true })
  return number || right.id.localeCompare(left.id, 'zh-CN', { numeric: true })
}

/**
 * Returns representative card IDs in right-to-left display order.
 * Legion cards therefore come first, followed by tactics and artifacts.
 */
export function selectDeckCoverCardIds(cardIds: readonly string[], byId: ReadonlyMap<string, DeckCard>) {
  const seen = new Set<string>()
  const groups = new Map<DeckCoverCardType, DeckCard[]>(DECK_COVER_CARD_TYPES.map(type => [type, []]))
  for (const rawId of cardIds) {
    const key = rawId.toLocaleUpperCase('en-US')
    if (seen.has(key)) continue
    seen.add(key)
    const card = byId.get(rawId) ?? byId.get(key)
    if (isDeckCoverCard(card)) groups.get(card.cardType)!.push(card)
  }
  for (const cards of groups.values()) cards.sort(compareDeckCoverCards)

  const picked = new Map<DeckCoverCardType, DeckCard[]>(DECK_COVER_CARD_TYPES.map(type => [type, []]))
  for (const type of DECK_COVER_CARD_TYPES) {
    const card = groups.get(type)![0]
    if (card) picked.get(type)!.push(card)
  }
  // Missing types refill in the same legion → tactic → artifact priority, then flatten in that order.
  for (const type of DECK_COVER_CARD_TYPES) {
    const cards = groups.get(type)!
    for (let index = 1; index < cards.length && selectedCount(picked) < 3; index++)
      picked.get(type)!.push(cards[index]!)
  }
  return DECK_COVER_CARD_TYPES.flatMap(type => picked.get(type)!).slice(0, 3).map(card => card.id)
}

export function validateDeckCoverCardIds(value: unknown,
  byId?: ReadonlyMap<string, DeckCard>): value is string[] {
  return Array.isArray(value) && value.length <= 3
    && value.every(id => typeof id === 'string' && Boolean(id.trim()))
    && new Set(value.map(id => id.toLocaleUpperCase('en-US'))).size === value.length
    && (!byId || value.every(id => isDeckCoverCard(byId.get(id) ?? byId.get(id.toLocaleUpperCase('en-US')))))
}

export function validateOptionalDeckCoverCardIds(value: Record<string, unknown>,
  byId?: ReadonlyMap<string, DeckCard>) {
  return !Object.hasOwn(value, 'coverCardIds') || validateDeckCoverCardIds(value.coverCardIds, byId)
}

function selectedCount(groups: ReadonlyMap<DeckCoverCardType, readonly DeckCard[]>) {
  let count = 0
  for (const cards of groups.values()) count += cards.length
  return count
}
