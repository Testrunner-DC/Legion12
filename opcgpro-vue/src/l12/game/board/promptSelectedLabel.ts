// B5 阶段 3（候选 7）：提示里「已选目标」的单条文案下沉。
// 战场目标优先走 battlefieldTargetLabel，其次 choiceLabels/data，符文与士气各自兜底。
import type { Prompt } from '../../types'
import { battlefieldTargetLabel } from '../battlefieldLabels'

export type MoraleChoiceLike = { id: string; label: string; detail: string }

export function promptSelectedLabel(
  prompt: Prompt,
  id: string,
  context: {
    controlledPlayerIndex: number
    game: Parameters<typeof battlefieldTargetLabel>[0]
    moraleChoices: readonly MoraleChoiceLike[]
  },
): string {
  const battlefield = battlefieldTargetLabel(context.game, context.controlledPlayerIndex, id, prompt.choiceLabels?.[id])
  if (battlefield) return battlefield
  const label = prompt.choiceLabels?.[id]?.trim() || prompt.data?.[id]?.trim()
  if (label) return label
  if (/^rune:\d+$/.test(id)) return `彼界符文 ${Number(id.split(':')[1])}`
  const resource = context.moraleChoices.find(choice => choice.id === id)
  return resource ? `${resource.label}（${resource.detail}）` : ''
}
