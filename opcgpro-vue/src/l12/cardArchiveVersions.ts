import { cardTypeFilterKey } from './cardPresentation'
import { moraleIdentities, type DeckCard } from './decks'

export interface LogicalArchiveCard {
  logicalId: string
  defaultVersion: DeckCard
  versions: DeckCard[]
}

const rarityRank: Record<string, number> = {
  C: 0,
  U: 1,
  UC: 1,
  R: 2,
  SR: 3,
  L: 4,
  SEC: 5,
  P: 6,
}

const moraleVersionIdentity = new Map(moraleIdentities.flatMap(identity =>
  identity.versionCardIds.map(cardId => [cardId, identity.canonicalCardId] as const)))

function normalizedText(value: string | undefined) {
  return (value ?? '').normalize('NFKC').toLocaleLowerCase('zh-CN')
    .replace(/[\s，。；：、·<>〈〉《》【】（）()「」『』,.!:;\-_/]/g, '')
}

function normalizedFaction(value: string) {
  return value === 'bijie' ? 'otherworld' : value
}

function ruleIdentity(card: DeckCard) {
  const traits = [...(card.traits ?? [])].map(normalizedText).sort().join('|')
  return [
    normalizedText(card.nameZh),
    cardTypeFilterKey(card.cardType),
    normalizedFaction(card.faction),
    card.cost ?? '',
    card.troops ?? '',
    card.hp ?? '',
    card.disasterLevel ?? '',
    card.trialValue ?? '',
    normalizedText(card.profession),
    traits,
    normalizedText(card.effect),
  ].join('::')
}

function productRank(product: string): [number, number, string] {
  const season = product.match(/^S(\d+)$/i)
  if (season) return [0, Number(season[1]), product]
  const starter = product.match(/^ST(\d+)$/i)
  if (starter) return [1, Number(starter[1]), product]
  return [2, Number.MAX_SAFE_INTEGER, product]
}

function compareTuple(a: [number, number, string], b: [number, number, string]) {
  return a[0] - b[0] || a[1] - b[1] || a[2].localeCompare(b[2], 'zh-CN')
}

function rarityValue(rarity: string | undefined) {
  if (!rarity?.trim()) return 100
  return rarityRank[rarity.trim().toUpperCase()] ?? 99
}

/** Earliest product wins; within it, the lowest known rarity wins. Missing or
 * unknown rarity sorts after known rarities, then card number makes ties stable. */
export function compareArchiveVersions(a: DeckCard, b: DeckCard) {
  return compareTuple(productRank(a.product), productRank(b.product))
    || rarityValue(a.rarity) - rarityValue(b.rarity)
    || a.number.localeCompare(b.number, 'zh-CN', { numeric: true })
    || a.id.localeCompare(b.id, 'zh-CN', { numeric: true })
}

function logicalIdentity(card: DeckCard) {
  const moraleId = moraleVersionIdentity.get(card.id)
    ?? moraleVersionIdentity.get(card.archiveBaseCardId ?? '')
  // Printed morale variants share one archive identity. The Olympus god-power
  // reverse is a runtime state of S02-05C1, not the canonical identity of its A art.
  // Presentation-only `st` cards inherit the identity of their direct STxx-C1 base.
  return moraleId ? `morale:${moraleId}` : `rules:${ruleIdentity(card)}`
}

export function groupArchiveCards(cards: readonly DeckCard[]): LogicalArchiveCard[] {
  const uniqueCards = [...new Map(cards.map(card => [card.id, card])).values()]
  const grouped = new Map<string, DeckCard[]>()
  uniqueCards.forEach(card => {
    const key = logicalIdentity(card)
    const versions = grouped.get(key) ?? []
    versions.push(card)
    grouped.set(key, versions)
  })
  return [...grouped.entries()].map(([logicalId, unsorted]) => {
    const versions = [...unsorted].sort(compareArchiveVersions)
    return { logicalId, defaultVersion: versions[0], versions }
  })
}
