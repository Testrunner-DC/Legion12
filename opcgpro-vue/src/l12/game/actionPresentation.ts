import type { ActionEvent, Card } from '../types'

export type ActionPresentationKind =
  | 'draw'
  | 'play'
  | 'attack'
  | 'defense'
  | 'support'
  | 'damage'
  | 'grave'
  | 'turn'

export interface ActionPresentation {
  sequence: number
  kind: ActionPresentationKind
  playerIndex?: number
  label: string
  text: string
  card?: Card
}

const genericLabels: Record<ActionPresentationKind, string> = {
  draw: '抽牌',
  play: '卡牌打出',
  attack: '进攻宣言',
  defense: '抵挡',
  support: '支援',
  damage: '伤害结算',
  grave: '阵亡 / 入墓',
  turn: '回合切换',
}

function firstPublicCard(event: ActionEvent) {
  return event.cards?.find(card => !card.hidden && card.identityKnown !== false)
}

/**
 * Converts the server-projected event into a presentation-safe action.
 * Hidden card identity is deliberately never read for draws. The server event
 * sequence remains the only ordering and de-duplication key.
 */
export function actionPresentationFromEvent(event: ActionEvent): ActionPresentation | null {
  let kind: ActionPresentationKind | null = null
  if (event.type === 'draw') kind = 'draw'
  else if (event.type === 'phase-detail' && /(?:从牌库)?抽取\s*\d+\s*张牌/.test(event.text)) kind = 'draw'
  else if (event.type === 'play' || event.type === 'put') kind = 'play'
  else if (event.type === 'attack') kind = 'attack'
  else if (event.type === 'defense' && Boolean(event.cards?.length)) kind = 'defense'
  else if (event.type === 'support') kind = 'support'
  else if (event.type === 'damage' || event.type === 'combat') kind = 'damage'
  else if (event.type === 'grave') kind = 'grave'
  else if (event.type === 'turn-start') kind = 'turn'
  if (!kind) return null

  const presentation: ActionPresentation = {
    sequence: event.sequence,
    kind,
    playerIndex: event.playerIndex,
    label: genericLabels[kind],
    text: kind === 'draw' ? '抽取卡牌' : event.text,
  }
  // Draw events can carry the public source of an effect. Rendering it here
  // would look like the drawn card and could leak or misrepresent information.
  if (kind !== 'draw' && kind !== 'turn' && kind !== 'damage') {
    presentation.card = firstPublicCard(event)
  }
  return presentation
}

export const actionPresentationDurations: Record<ActionPresentationKind, number> = {
  draw: 720,
  play: 980,
  attack: 820,
  defense: 760,
  support: 760,
  damage: 680,
  grave: 860,
  turn: 900,
}
