import type { Prompt } from '../types'

// Display-only copy. Never mutate a Prompt, infer a rule, shorten card prose,
// resolve identities, or use this module for logs / commands / catalog text.
const redundantClauses = new Set([
  '确认后将继续进行对局准备', '确认后将以该顺序进行',
  '实际结果以权威处理为准', '后续结果以权威处理为准',
  '实际结果以权威结算为准', '后续结果以权威结算为准',
  '后续声明按步骤继续', '本次声明到此结束',
])
const shortClauses: Record<string, string> = {
  '不发动本次可选效果': '不发动', '不发动当前效果': '不发动',
  '不执行本次可选效果': '不发动', '不执行本次可选段': '跳过此段',
  '请根据当前情况完成选择并确认': '请选择',
  '请选择一种处理方式': '请选择一项',
  '请选择是否执行': '是否发动',
  '不打出响应牌': '不响应', '发动当前效果': '发动',
}

// Only exact, complete boilerplate clauses are removed. A clause containing a
// card, target, cost, duration, quantity, public state or condition is retained.
export function compactPromptCopy(value: string | undefined | null): string {
  if (!value) return ''
  return value.split(/([。；\n])/).reduce<string[]>((parts, part, index, all) => {
    if (index % 2) return parts
    const clause = part.trim()
    if (!clause || redundantClauses.has(clause)) return parts
    parts.push((shortClauses[clause] ?? clause) + (all[index + 1] ?? ''))
    return parts
  }, []).join('').trim().replace(/[；\n]+$/, '').replace(/；。/g, '。')
}

function sameCopy(left: string, right: string): boolean {
  return left.replace(/[。；\s]+$/g, '') === right.replace(/[。；\s]+$/g, '')
}

function actionCopy(value: string): string {
  // Equivalent wording for the *same* instructed action. Keep all numbers,
  // resource names, conditions and duration; only deduplicate exact results.
  return value.split(/(〈[^〉]*〉|《[^》]*》)/).map((part, index) => index % 2 ? part
    : part.replace(/^请选择并/, '').replace(/(支付|弃置)所选的(?=\d)/g, '$1')
      .replace(/作为响应费用/g, '作为费用')).join('')
}

// Structured narrative wrappers only, not fallback effectText / printed card prose.
function narrativeSituation(value: string): string {
  return value.replace(/^〈[^〉]+〉的(?:登场时|晋升登场|阵亡时|进攻时)效果(?:正在结算|可以选择是否发动|正在选择登场对象|正在选择墓地回收对象)。/, '')
}

function structuredResourcePayment(prompt: Prompt): boolean {
  if (prompt.kind !== 'resource-payment' || prompt.presentation?.paymentStatus !== 'pending') return false
  const situation = prompt.presentation.situation?.match(/^〈[^〉]+〉需要支付(\d+)份资源才能继续。可用资源为(.+)。$/)
  if (!situation) return false
  const instruction = prompt.presentation.instruction
  const exactInstruction = `请选择恰好${situation[1]}份可用资源并确认。`
  return Number(situation[1]) === prompt.minChoose && prompt.minChoose === prompt.maxChoose
    && prompt.presentation.paymentSummary === `${situation[1]}份资源（${situation[2]}）`
    && (instruction === exactInstruction
      || prompt.validChoices.includes('cancel') && instruction === `请选择恰好${situation[1]}份可用资源并确认；也可以取消当前操作。`)
}

function oneResponseQuestion(value: string): string {
  const lines = value.split('\n')
  const questions = lines.filter(line => line.trim() === '是否响应？').length
  if (questions < 2) return value
  return `${lines.filter(line => line.trim() !== '是否响应？').join('\n').trimEnd()}\n是否响应？`
}

function hasDeclineButton(prompt: Prompt): boolean {
  return prompt.validChoices.some(id =>
    compactPromptCopy(prompt.choiceLabels?.[id] || prompt.data?.[id]).replace(/[。；]+$/, '') === '不发动')
}

const noOpDeclineClauses = new Set([
  '不支付费用', '不支付弃牌费用', '不获得本次无法被进攻的保护',
  '不会弃牌', '也不会获得保护', '双方主宰都不会受到本次伤害',
  '不会抽牌', '也不会弃牌', '手牌和墓地都不会改变', '墓地和手牌都不会改变',
  '我方主宰不受此伤害', '不受此伤害', '不抽牌', '不抽牌也不弃牌',
  '不弃置牌库顶部卡牌', '也不进入目标选择', '也不消耗符文', '双方主宰的生命不变',
  '墓地和手牌都不改变', '不影响任何军团', '不选择对象', '不选择登场对象',
  '也不进入位置选择', '不移动任何墓地卡牌',
  '不支付展示并回顶的费用', '也不选择击杀目标',
])
const lifecycleEnding = /^(?:并|直接)?结束(?:本(?:次登场时)?效果|〈[^〉]+〉的登场时效果|这段后续|后续击杀处理)$/

// Classify complete, audited no-op templates. Unknown text and meaningful
// negative consequences (nonrefund, shuffle, prior effects, expiry) fail open:
// they remain visible, even on a decline button.
function noOpDecline(value: string): boolean {
  const clauses = value.replace(/^不发动(?:本次(?:登场时|回收|锁定)?效果)?[：，；]?/, '')
    .split(/[，；。]/).map(clause => clause.trim()).filter(Boolean)
  return clauses.every(clause => noOpDeclineClauses.has(clause)
    || lifecycleEnding.test(clause)
    || /^不将〈[^〉]+〉加入手牌$/.test(clause)
    || /^不移动任何〈[^〉]+〉$/.test(clause))
}

function declineInstruction(value: string): string {
  return value.split(/([。；\n])/).reduce<string[]>((parts, part, index, all) => {
    if (index % 2) return parts
    if (part.startsWith('不发动时') && noOpDecline(part.slice('不发动时'.length))) return parts
    const clause = part.replace(/，或选择[“「]不发动[”」](?:结束本(?:次登场时)?效果|结束这段后续|且不消耗符文)?$/, '')
      .replace(/^请选择[“「]发动[”」](?:并|继续)/, '')
    if (clause) parts.push(clause + (all[index + 1] ?? ''))
    return parts
  }, []).join('').replace(/[；\n]+$/, '').trim()
}

export function promptSituationCopy(prompt: Prompt): string {
  if (structuredResourcePayment(prompt)) return ''
  // Only presentation.situation has narrative wrappers; legacy/card prose stays
  // verbatim. responseContext remains recipient-safe server metadata.
  let text = prompt.presentation?.situation?.trim()
    ? narrativeSituation(prompt.presentation.situation.trim())
    : prompt.data?.effectText?.trim() || prompt.text.trim()
  if (prompt.presentation?.situation && hasDeclineButton(prompt))
    text = text.replace(/，也可以不发动(?=。|$)/g, '')
  const context = prompt.data?.responseContext?.trim()
  if (!context) return prompt.kind === 'response' ? oneResponseQuestion(text) : text
  // Once choosing a response's cost/target, do not ask whether to respond again.
  const detail = prompt.kind === 'response' ? context : context.replace(/\n是否响应？$/, '')
  if (text.includes(context)) return prompt.kind === 'response' ? oneResponseQuestion(text.replace(context, detail)) : text.replace(context, detail)
  const combined = text.includes(detail) ? text : `${text}\n${detail}`
  return prompt.kind === 'response' ? oneResponseQuestion(combined) : combined
}

export function promptInstructionCopy(prompt: Prompt, fallback = ''): string {
  if (structuredResourcePayment(prompt)) return '请选择要支付的资源。'
  const raw = compactPromptCopy(prompt.presentation?.instruction || fallback)
  const instruction = prompt.kind === 'option' && /^请决定是否执行〈[^〉]+〉的效果。$/.test(raw)
    ? '请选择处理方式。'
    : prompt.presentation?.instruction && hasDeclineButton(prompt) ? declineInstruction(raw) : raw
  // The action buttons already ask this question. Only suppress the complete
  // generic instruction, never a sentence with payment / targeting conditions.
  const genericDecision = new Set(['请选择一项。', '请选择是否发动', '请选择是否发动。', '请决定是否执行本次效果。', '请决定是否响应当前效果。'])
  if ((prompt.data?.uiPattern === 'effect-decision' || ['optional', 'option', 'response'].includes(prompt.kind))
      && genericDecision.has(instruction)) return ''
  return sameCopy(instruction, promptSituationCopy(prompt)) ? '' : instruction
}

export function promptConsequenceCopy(prompt: Prompt, id: string, label: string): string {
  let text = compactPromptCopy(prompt.presentation?.choiceConsequences?.[id])
  if (text.startsWith(`${label}：`)) text = text.slice(label.length + 1)
  if (label === '不发动') {
    if (noOpDecline(text)) return ''
    // The button already names the decision; retain every meaningful remainder.
    text = text.replace(/^不发动[：；，]/, '')
  }
  text = text.split(/([，；。\n])/).reduce<string[]>((parts, part, index, all) => {
    if (index % 2 || lifecycleEnding.test(part.trim())) return parts
    if (part) parts.push(part + (all[index + 1] ?? ''))
    return parts
  }, []).join('').replace(/[，；\n]+$/, '')
  return sameCopy(text, label) ? '' : text
}

export function promptSubmissionCopy(prompt: Prompt): string {
  if (structuredResourcePayment(prompt)
      && prompt.presentation?.submissionConsequence === `支付所选的${prompt.minChoose}份资源。`)
    return '确认后支付所选资源。'
  const text = compactPromptCopy(prompt.presentation?.submissionConsequence)
  return sameCopy(actionCopy(text), actionCopy(promptInstructionCopy(prompt))) || sameCopy(text, promptSituationCopy(prompt)) ? '' : text
}

export function promptPaymentCopy(prompt: Prompt): string {
  const summary = compactPromptCopy(prompt.presentation?.paymentSummary)
  const status = prompt.presentation?.paymentStatus
  if (!status || !summary) return summary
  if (status === 'pending' && summary === '尚未支付弃牌费用；选择手牌并确认后才会弃置。'
      && promptInstructionCopy(prompt).includes('弃置1张手牌')) return '费用待支付'
  // Do not repeat “已支付：已支付…”, but never turn pending into paid.
  const prefix = status === 'paid' ? '已支付' : '待支付'
  return summary.startsWith(prefix) || status === 'pending' && summary.startsWith('尚未支付')
    ? summary : `${prefix}：${summary}`
}

export function mulliganCopy(count: number, timed: boolean): string {
  return `已选 ${count} 张，换成等量新牌；未选则保留全部。${timed ? '超时保留原手牌。' : ''}`
}
