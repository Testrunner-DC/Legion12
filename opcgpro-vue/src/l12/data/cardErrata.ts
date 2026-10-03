export interface CardErrataRecord {
  id: string
  cardId: string
  sourceProduct: string
  previousEffect: string
}

export const CARD_ERRATA: readonly CardErrataRecord[] = [
  {
    id: 'S01-02M2-golden-ideal-effect',
    cardId: 'S01-02M2',
    sourceProduct: '黄金的理想乡',
    previousEffect: '我方 圣物区存在5张名字包含<卡诺匹斯>的圣物时：可将此主宰替换<伊西斯>登场。\n双人模式：此主宰登场即可获得游戏胜利。\n多人模式：主宰增加2点血量，并将墓地1张【太阳城】军团活跃登场。<陵墓守卫>兵力+1000。',
  },
  {
    id: 'S02-05M1-ST05-effect',
    cardId: 'S02-05M1',
    sourceProduct: 'ST05|奥林匹斯阵营预组',
    previousEffect: '回合1次 我方远程军团（远程图标）阵亡时，可翻转1张休整的士气。\n我方 回合1次 可消耗并翻转1神力或弃置1张手牌：选择我方1张费用为3至6的【奥林匹斯】军团，本回合获得 强攻 或 震击。',
  },
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
