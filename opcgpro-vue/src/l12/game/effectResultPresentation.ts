import type { ActionEvent, Card } from '../types'

const TERMINAL_LABELS: Record<string, string> = {
  resolved: '完成',
  negated: '被无效',
  skipped: '跳过',
  failed: '未能完成',
  declined: '选择不发动',
}

function segmentLabel(event: ActionEvent) {
  const index = event.effectSegmentIndex
  const count = event.effectSegmentCount
  return typeof index === 'number' && typeof count === 'number'
    && Number.isSafeInteger(index) && Number.isSafeInteger(count)
    && index > 0 && count > 1 && index <= count
    ? `第${index}/${count}段` : ''
}

export function effectResultPresentationText(event: ActionEvent, displayedSource: Card | undefined) {
  const subject = displayedSource && !displayedSource.hidden && displayedSource.name
    ? `〈${displayedSource.name}〉的效果` : '效果'
  const base = event.effectText?.trim() || subject
  if (event.type !== 'effect-result') return base

  const status = event.effectResultStatus
  const label = status ? TERMINAL_LABELS[status] : undefined
  if (!label) return base

  const segment = segmentLabel(event)
  const result = status === 'resolved' && !segment ? '' : `${segment}${label}`
  const semantic = event.playerLogSemantic
  const outcome = displayedSource && !displayedSource.hidden && displayedSource.instanceId
    && semantic?.sourceInstanceId === displayedSource.instanceId
    ? semantic.outcomeLabel?.trim() : ''
  const detail = [result, outcome].filter(Boolean).join('；')
  return detail ? `${base}（${detail}）` : base
}
