export const HORIZONTAL_CARD_TYPES = new Set(['disaster', 'destruction', 'trial'])

export function isHorizontalCardType(cardType?: string | null) {
  return Boolean(cardType && HORIZONTAL_CARD_TYPES.has(cardType))
}

export function normalizeLookupCardType(sourceType: string, cardName: string) {
  if (sourceType === '待识别' && cardName.trim() === '符文') return 'token'
  const map: Record<string, string> = {
    战术: 'tactic', 反击战术: 'tactic', 军团: 'legion', 衍生卡: 'token',
    士气卡: 'rune', 主城: 'divinity', 主宰: 'master', 圣物: 'artifact',
    天灾终局: 'destruction', 试炼卡: 'trial', 待识别: 'unknown',
  }
  return map[sourceType] ?? 'unknown'
}

export function cardTypeFilterKey(cardType: string) {
  return cardType
}

export function isCounterTacticCard(card?: { cardType?: string | null; isCounterTactic?: boolean } | null) {
  return Boolean(card?.cardType === 'tactic' && card.isCounterTactic === true)
}

export function cardTypeLabel(cardType: string, isCounterTactic = false) {
  if (cardType === 'tactic' && isCounterTactic) return '反击战术'
  const labels: Record<string, string> = {
    legion: '军团', tactic: '主动战术', rune: '士气卡', artifact: '圣物',
    divinity: '主城', master: '主宰', destruction: '天灾', disaster: '天灾',
    token: '衍生卡牌', trial: '试炼卡', unknown: '待识别',
  }
  return labels[cardType] ?? cardType
}
