import type { ActionEvent } from '../types'

export type ActionPresentationKind = 'turn-start' | 'main-phase' | 'turn-end'

export interface ActionPresentation {
  sequence: number
  kind: ActionPresentationKind
  playerIndex?: number
  label: string
  text: string
}

const genericLabels: Record<ActionPresentationKind, string> = {
  'turn-start': '回合开始',
  'main-phase': '主要阶段',
  'turn-end': '回合结束',
}

/**
 * Converts the server-projected event into a presentation-safe action.
 * Hidden card identity is deliberately never read for draws. The server event
 * sequence remains the only ordering and de-duplication key.
 */
export function actionPresentationFromEvent(event: ActionEvent): ActionPresentation | null {
  const kind: ActionPresentationKind | null = event.type === 'turn-start'
    ? 'turn-start'
    : event.type === 'phase' && event.text === '进入主要阶段'
      ? 'main-phase'
      : event.type === 'phase' && event.text === '执行结束阶段'
        ? 'turn-end'
        : null
  if (!kind) return null
  return {
    sequence: event.sequence,
    kind,
    playerIndex: event.playerIndex,
    label: genericLabels[kind],
    text: event.text,
  }
}

export const actionPresentationDurations: Record<ActionPresentationKind, number> = {
  'turn-start': 780,
  'main-phase': 680,
  'turn-end': 680,
}
