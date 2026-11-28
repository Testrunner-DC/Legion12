// B5 阶段 3（候选 2）：自定天灾的 GM 动作从 GameBoard.vue 下沉。
// 依赖通过参数注入，模块本身不读组件状态。
import { ref, type Ref } from 'vue'
import type { Card, DisasterCardView, GameState, SingleCardPickerItem } from '../../types'
import { isVisibleDisasterCard } from './disasterState'

type GmAction = (command: Record<string, unknown>) => void

export function useDisasterActions(options: {
  game: () => GameState
  readOnly: () => boolean
  gmEnabled: () => boolean
  controlledPlayerIndex: () => number
  focusCard: Ref<Card | null>
  gmAction: GmAction
}) {
  const customDisasterSlot = ref<number | null>(null)

  function focusSessionDisaster(card: DisasterCardView, index?: number) {
    if (isVisibleDisasterCard(card)) options.focusCard.value = card
    if (index !== undefined && options.game().disasterMode === 'custom' && options.gmEnabled()
      && !options.readOnly() && index < 3)
      customDisasterSlot.value = index
  }

  function replaceCustomDisaster(card: SingleCardPickerItem) {
    if (customDisasterSlot.value === null) return
    options.gmAction({
      type: 'replaceDisaster',
      targetPlayer: options.controlledPlayerIndex(),
      slot: customDisasterSlot.value,
      cardId: card.id,
    })
    customDisasterSlot.value = null
  }

  return { customDisasterSlot, focusSessionDisaster, replaceCustomDisaster }
}
