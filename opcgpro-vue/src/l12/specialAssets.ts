const assetRoot = '/assets/l12/special'
// Public assets are served outside Vite's hashed bundle. Keep an explicit
// revision so a previously cached 404 for a newly added Profile cannot survive
// after the file has been deployed.
const masterProfileAssetRevision = '20260903-2'

const roundCardAssets: Record<string, string> = {
  'S01-0216': 'Round_S01-0216-卡诺匹斯箱.png',
  'S01-0217': 'Round_S01-0217-卡诺匹斯罐一.png',
  'S01-0218': 'Round_S01-0219-S01-0218-卡诺匹斯罐二.png',
  'S01-0219': 'Round_S01-0219-卡诺匹斯罐三.png',
  'S01-0220': 'Profile-S01-0220-卡诺匹斯罐四.png',
  'S02-06S1': 'Round_S02-06S1-符文.png',
}

const disasterRoundAssets: Record<string, string> = {
  'S01-DS01': 'S01-DS01.png',
  'S01-DS02': 'S01-DS02.png',
  'S01-DS03': 'S01-DS03.png',
  'S01-DS04': 'S01-DS04.png',
  'S01-DS05': 'S01-DS05.png',
  'S01-DS06': 'S01-DS06.png',
  'S01-DS07': 'S01-DS07.png',
  'S01-DS08': 'S01-DS08.png',
  'S01-DS09': 'S01-DS09.png',
  'S01-DS10': 'S01-DS10.png',
  'S02-DS01': 'S02-DS01.png',
  'S02-DS02': 'S02-DS02.png',
  'S02-DS03': 'S02-DS03.png',
  'S02-DS04': 'S02-DS04.png',
  'S02-DS05': 'S02-DS05.png',
  'S02-DS06': 'S02-DS06.png',
  'ST-DS01': 'ST-DS01.png',
  'ST-DS02': 'ST-DS02.png',
  'ST-DS03': 'ST-DS03.png',
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
  return masterId ? `${assetRoot}/master/${masterId}.png?v=${masterProfileAssetRevision}` : fallback
}

export function roundCardUrl(cardId?: string, fallback?: string) {
  const asset = cardId ? roundCardAssets[cardId] : undefined
  return asset ? `${assetRoot}/round/${asset}` : fallback
}

export function disasterRoundUrl(cardId?: string, fallback?: string) {
  const asset = cardId ? disasterRoundAssets[cardId] : undefined
  return asset ? `${assetRoot}/round/${asset}` : fallback
}

export const destructionRoundBackUrl = `${assetRoot}/round/Round_Cardback_Destruction.png`
