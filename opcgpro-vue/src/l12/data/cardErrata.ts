export interface CardErrataRecord {
  id: string
  cardId: string
  sourceProduct: string
  previousEffect: string
}

export const CARD_ERRATA: readonly CardErrataRecord[] = [
  {
    id: 'S02-06M2-ST06-effect',
    cardId: 'S02-06M2',
    sourceProduct: 'ST06|彼界阵营预组',
    previousEffect: '规则上，可完成的试炼数量增加1张。\n每完成1次试炼，可获得1符文。\n回合1次 当我方成功发动战术效果时，试炼+1。',
  },
]

export function cardErrataForCard(cardId: string) {
  return CARD_ERRATA.filter(entry => entry.cardId === cardId)
}
