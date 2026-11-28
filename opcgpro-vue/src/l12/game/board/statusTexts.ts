// B5 阶段 3（候选 3）：卡牌状态文案从 GameBoard.vue 下沉为纯函数。
// 唯一外部依赖是「按实例找当前场上的卡」，隐藏卡不展示状态。
import type { Card } from '../../types'

export function buildStatusTexts(card: Card, findCurrentCard: (instanceId: string) => Card | undefined): string[] {
  const statuses: string[] = []
  if (card.hasStrongAttack) statuses.push('强攻：进攻主宰时额外造成 1 点伤害。')
  if (card.hasSureHit) statuses.push('必中：进攻不可被抵挡或支援。')
  if (card.hasShock) statuses.push('震击：进攻目标左右相邻的军团本回合兵力-2000。')
  if ((card.immortalUses ?? 0) > 0) statuses.push(`免死：剩余 ${card.immortalUses} 次。`)
  if (card.hasCharge) statuses.push('冲锋：登场回合可以进攻。')
  if (card.cannotAttack) statuses.push('当前不能进攻。')
  if (card.cannotSupport) statuses.push('当前不能支援。')
  for (const modifier of card.timedModifiers ?? []) {
    if (modifier.troopsDelta) statuses.push(`${modifier.source}：兵力${modifier.troopsDelta > 0 ? '+' : ''}${modifier.troopsDelta}。`)
    if (modifier.costDelta) statuses.push(`${modifier.source}：费用${modifier.costDelta > 0 ? '+' : ''}${modifier.costDelta}。`)
  }
  const current = findCurrentCard(card.instanceId)
  if (current && !current.hidden) for (const effect of current.statusEffects ?? []) {
    if (effect.kind !== 'conditional-attack' || !effect.label?.trim()) continue
    const source = effect.source?.trim()
    statuses.push(`${effect.label.trim()}${source ? `（来源：${source}）` : ''}。`)
  }
  return statuses
}
