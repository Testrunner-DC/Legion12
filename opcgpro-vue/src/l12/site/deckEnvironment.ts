import cardProductInclusionsData from '../../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'
import { cardProductsForIds, type SavedL12Deck } from '@/l12/decks'

export const DECK_ENVIRONMENTS = ['1.0', '2.0', '2.5'] as const
export type DeckEnvironment = typeof DECK_ENVIRONMENTS[number] | 'pending'
export type DeckEnvironmentFilter = typeof DECK_ENVIRONMENTS[number] | 'all'

const CURRENT_25_PRODUCTS = new Set([
  'ST01|天廷阵营预组',
  'ST02|太阳城阵营预组',
  'ST03|阿斯加德阵营预组',
  'ST04|高天原阵营预组',
  'ST05|奥林匹斯阵营预组',
  'ST06|彼界阵营预组',
])
const productInclusions = cardProductInclusionsData.cards as Array<{
  cardId: string
  cardPool: string
  products: string[]
}>
const cardPoolById = new Map(productInclusions.map(entry => [entry.cardId.toLocaleLowerCase(), entry.cardPool]))
const deckEnvironmentCache = new WeakMap<object, DeckEnvironment>()

export interface DeckEnvironmentEvidence {
  cardPool?: string
  products: readonly string[]
}

/**
 * Environment identity is deliberately closed over the currently released pools.
 * A future pool or ST product stays pending until its explicit mapping is added here.
 */
export function deckEnvironmentFromEvidence(evidence: readonly DeckEnvironmentEvidence[]): DeckEnvironment {
  if (!evidence.length || evidence.some(card => !card.cardPool || !card.products.length)) return 'pending'

  const starterProducts = evidence.flatMap(card => card.products)
    .map(product => product.trim())
    .filter(product => /^ST\d+/i.test(product))
    .filter(product => !product.endsWith('（勘误收录）'))
  if (starterProducts.some(product => !CURRENT_25_PRODUCTS.has(product))) return 'pending'
  if (starterProducts.some(product => CURRENT_25_PRODUCTS.has(product))) return '2.5'

  const pools = new Set(evidence.map(card => card.cardPool))
  if ([...pools].some(pool => pool !== 'S01' && pool !== 'S02')) return 'pending'
  if (pools.has('S02')) return '2.0'
  return pools.has('S01') ? '1.0' : 'pending'
}

export function deckEnvironmentForCardIds(cardIds: readonly string[]): DeckEnvironment {
  const uniqueIds = [...new Set(cardIds.filter(Boolean).map(cardId => cardId.toLocaleLowerCase()))]
  return deckEnvironmentFromEvidence(uniqueIds.map(cardId => ({
    cardPool: cardPoolById.get(cardId),
    products: cardProductsForIds([cardId]),
  })))
}

export function deckEnvironmentForDeck(deck: Pick<SavedL12Deck, 'masterId' | 'cardIds' | 'moraleIds' | 'specialIds' | 'benchIds'>) {
  const cached = deckEnvironmentCache.get(deck)
  if (cached) return cached
  const environment = deckEnvironmentForCardIds([
    deck.masterId,
    ...deck.cardIds,
    ...deck.moraleIds,
    ...(deck.specialIds ?? []),
    ...(deck.benchIds ?? []),
  ])
  deckEnvironmentCache.set(deck, environment)
  return environment
}

export function deckEnvironmentLabel(environment: DeckEnvironment) {
  return environment === 'pending' ? '环境待定' : `环境 ${environment}`
}
