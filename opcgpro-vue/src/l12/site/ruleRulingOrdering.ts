export interface RuleRulingOrderValue {
  id: string
  recordedAt: string
  cardIds: readonly string[]
}

export type ResolveCardNumber = (cardId: string) => string | undefined

export function compareCardNumbers(left: string, right: string) {
  return left.localeCompare(right, 'zh-CN', { numeric: true, sensitivity: 'base' })
}

export function rulingCardSortKey(item: RuleRulingOrderValue, resolveCardNumber: ResolveCardNumber) {
  return item.cardIds.map(resolveCardNumber).filter((value): value is string => Boolean(value))
    .sort(compareCardNumbers).at(-1) || ''
}

export function compareCardRulingsByNumber(
  left: RuleRulingOrderValue,
  right: RuleRulingOrderValue,
  resolveCardNumber: ResolveCardNumber,
) {
  const leftCard = rulingCardSortKey(left, resolveCardNumber)
  const rightCard = rulingCardSortKey(right, resolveCardNumber)
  const leftGroup = leftCard ? 0 : left.cardIds.length ? 1 : 2
  const rightGroup = rightCard ? 0 : right.cardIds.length ? 1 : 2
  return leftGroup - rightGroup
    || (leftCard && rightCard ? compareCardNumbers(rightCard, leftCard) : 0)
    || right.recordedAt.localeCompare(left.recordedAt)
    || compareCardNumbers(left.id, right.id)
}

export function compareRulingsByScoreAndDate(
  left: Pick<RuleRulingOrderValue, 'id' | 'recordedAt'>,
  right: Pick<RuleRulingOrderValue, 'id' | 'recordedAt'>,
  leftScore: number,
  rightScore: number,
) {
  return rightScore - leftScore
    || right.recordedAt.localeCompare(left.recordedAt)
    || compareCardNumbers(left.id, right.id)
}
