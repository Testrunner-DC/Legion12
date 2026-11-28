// B5 阶段 1（第三批）：天灾区域的状态判定从 GameBoard.vue 下沉为纯函数。
// 只做「给定事实 → 结果」的映射，组件侧保留薄包装并注入自身状态。
import type { Card, DisasterCardView } from '../../types'

export function disasterSlotState(
  card: DisasterCardView | null,
  activeDisasterId: string | null | undefined,
  resolvedDisasterIds: ReadonlySet<string>,
) {
  if (!card || card.hidden) return 'unrevealed'
  if (card.instanceId === activeDisasterId) return 'active'
  if (resolvedDisasterIds.has(card.instanceId)) return 'resolved'
  return 'revealed'
}

export function isVisibleDisasterCard(card: DisasterCardView): card is Card {
  return !card.hidden && Boolean(card.cardId && card.name && card.cardType)
}

export function isCurrentTrial(
  trials: Array<{ instanceId: string; trialCompleted?: boolean; trialProgress?: number }> | undefined,
  trial: { instanceId: string },
) {
  return trials?.find(candidate => !candidate.trialCompleted
    && (candidate.trialProgress ?? 0) < 8)?.instanceId === trial.instanceId
}
