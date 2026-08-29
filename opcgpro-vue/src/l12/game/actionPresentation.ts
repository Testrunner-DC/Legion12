import type { ActionEvent } from '../types'

export type ActionPresentationKind = 'phase'

export interface ActionPresentation {
  sequence: number
  kind: ActionPresentationKind
  playerIndex?: number
  label: string
  text: string
}

const genericLabels: Record<ActionPresentationKind, string> = {
  phase: '阶段变化',
}

/**
 * Converts the server-projected event into a presentation-safe action.
 * Hidden card identity is deliberately never read for draws. The server event
 * sequence remains the only ordering and de-duplication key.
 */
export function actionPresentationFromEvent(event: ActionEvent): ActionPresentation | null {
  if (event.type !== 'phase') return null
  return {
    sequence: event.sequence,
    kind: 'phase',
    playerIndex: event.playerIndex,
    label: genericLabels.phase,
    text: event.text,
  }
}

export const actionPresentationDurations: Record<ActionPresentationKind, number> = {
  phase: 620,
}
