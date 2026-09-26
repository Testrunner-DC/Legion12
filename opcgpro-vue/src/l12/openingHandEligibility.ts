export interface OpeningHandCardIdentity {
  id: string
  cardType: string
  effect?: string
}

export const NORMAL_OPENING_HAND_CARD_TYPES: ReadonlySet<string> = new Set(['legion', 'tactic', 'artifact'])

export function isDerivedDeckSpecialCard(card: OpeningHandCardIdentity | undefined) {
  return card?.cardType === 'token' || card?.id === 'S02-01S1' || card?.id === 'S02-06S2'
}

export function bypassesNormalDrawDeck(card: OpeningHandCardIdentity | undefined) {
  return isDerivedDeckSpecialCard(card) || Boolean(card?.effect?.includes('构筑时不计入卡组数量'))
}

export function isNormalOpeningHandCard(card: OpeningHandCardIdentity | undefined) {
  return Boolean(card && NORMAL_OPENING_HAND_CARD_TYPES.has(card.cardType) && !bypassesNormalDrawDeck(card))
}

export function normalOpeningHandCopies<T>(copies: readonly T[], cardForCopy: (copy: T) => OpeningHandCardIdentity | undefined) {
  return copies.filter(copy => isNormalOpeningHandCard(cardForCopy(copy)))
}
