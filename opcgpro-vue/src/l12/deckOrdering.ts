import type { DeckCard } from './decks'

const TYPE_PRIORITY: Record<string, number> = {
  legion: 0,
  artifact: 1,
  tactic: 2,
  'counter-tactic': 2,
}

/**
 * 十二军团牌库的唯一默认顺序：军团→圣物→战术→其他；本阵营→中立；
 * 费用高→低；编号前→后。编辑器、详情与牌库图必须全部复用此比较器。
 */
export function compareDeckCards(left: DeckCard | undefined, right: DeckCard | undefined, masterFaction?: string) {
  if (!left || !right) return left ? -1 : right ? 1 : 0
  const typeDifference = (TYPE_PRIORITY[left.cardType] ?? 3) - (TYPE_PRIORITY[right.cardType] ?? 3)
  if (typeDifference) return typeDifference

  const factionPriority = (card: DeckCard) => !masterFaction ? 0
    : card.faction === masterFaction ? 0 : card.faction === 'universal' ? 1 : 2
  const factionDifference = factionPriority(left) - factionPriority(right)
  if (factionDifference) return factionDifference

  const costDifference = (right.cost ?? Number.NEGATIVE_INFINITY) - (left.cost ?? Number.NEGATIVE_INFINITY)
  if (costDifference) return costDifference
  return (left.number || left.id).localeCompare(right.number || right.id, 'zh-CN', { numeric: true })
}

export function compareDeckCardIds(
  leftId: string,
  rightId: string,
  byId: ReadonlyMap<string, DeckCard>,
  masterFaction?: string,
) {
  return compareDeckCards(byId.get(leftId), byId.get(rightId), masterFaction)
    || leftId.localeCompare(rightId, 'zh-CN', { numeric: true })
}
