// B5 阶段 1：从 GameBoard.vue 下沉的提示文案工具（纯函数，只依赖 Prompt 与文案助手）。
import type { Prompt } from '../../types'
import { promptInstructionCopy, promptPaymentCopy } from '../promptPlayerCopy'

export function promptActionText(prompt: Prompt) {
  return promptInstructionCopy(prompt, prompt.text)
}

export function promptChoiceText(prompt: Prompt, choice: string, fallback: string) {
  return prompt.choiceLabels?.[choice]?.trim()
    || fallback
}

export function inlinePromptTitle(prompt: Prompt) {
  return prompt.presentation?.title?.trim() || prompt.text.trim()
}

export function inlinePromptInstruction(prompt: Prompt) {
  const instruction = promptActionText(prompt)
  return instruction === inlinePromptTitle(prompt) ? '' : instruction
}

export function inlinePromptPayment(prompt: Prompt) {
  return promptPaymentCopy(prompt)
}
