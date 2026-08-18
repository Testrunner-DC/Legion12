const assetRoot = '/assets/l12/special'

const roundCardAssets: Record<string, string> = {
  'S01-0216': 'Round_S01-0216-卡诺匹斯箱.png',
  'S01-0217': 'Round_S01-0217-卡诺匹斯罐一.png',
  'S01-0218': 'Round_S01-0219-S01-0218-卡诺匹斯罐二.png',
  'S01-0219': 'Round_S01-0219-卡诺匹斯罐三.png',
  'S01-0220': 'Profile-S01-0220-卡诺匹斯罐四.png',
  'S02-06S1': 'Round_S02-06S1-符文.png',
}

export const factionLogoUrls: Record<string, string> = {
  tianting: `${assetRoot}/logo/tianting.png`,
  taiyangcheng: `${assetRoot}/logo/taiyangcheng.png`,
  olympus: `${assetRoot}/logo/olympus.png`,
  otherworld: `${assetRoot}/logo/otherworld.png`,
  asgard: `${assetRoot}/logo/asgard.png`,
  gaotianyuan: `${assetRoot}/logo/gaotianyuan.png`,
}

export const godPowerLogoUrl = `${assetRoot}/logo/olympus-god-power.png`
export const defaultSiteLogoUrl = `${assetRoot}/logo/main.png`
export const transparentSiteLogoUrl = `${assetRoot}/logo/main-trans.png`

export function masterProfileUrl(masterId?: string, fallback?: string) {
  return masterId ? `${assetRoot}/master/${masterId}.png` : fallback
}

export function roundCardUrl(cardId?: string, fallback?: string) {
  const asset = cardId ? roundCardAssets[cardId] : undefined
  return asset ? `${assetRoot}/round/${asset}` : fallback
}

export const destructionRoundBackUrl = `${assetRoot}/round/Round_Cardback_Destruction.png`
