// B5 阶段 3（候选 5）：退出提示摘要从 GameBoard.vue 下沉为纯函数。
// 文案后果仍复用 promptPlayerCopy 的同一实现，避免出现第二套措辞。
import type { Prompt } from '../../types'
import { promptConsequenceCopy } from '../promptPlayerCopy'

export function promptExitSummary(prompt: Prompt, choice: string): string {
  const label = prompt.choiceLabels?.[choice]?.trim() || (choice === 'skip' ? '不发动' : '取消')
  const consequence = promptConsequenceCopy(prompt, choice, label)
  return consequence && consequence !== label ? `${label}：${consequence}` : label
}
