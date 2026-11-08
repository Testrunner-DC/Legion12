export type FactionPresentation = {
  label: string
  color: string
  icon?: string
}

/** One source of truth for every faction-facing UI surface. */
export const factionPresentation: Record<string, FactionPresentation> = {
  tianting: { label: '天廷', color: '#dbbc00', icon: '/brand/faction-tianting.png' },
  bijie: { label: '彼界', color: '#31873f', icon: '/brand/faction-otherworld.png' },
  otherworld: { label: '彼界', color: '#31873f', icon: '/brand/faction-otherworld.png' },
  gaotianyuan: { label: '高天原', color: '#db0d17', icon: '/brand/faction-gaotianyuan.png' },
  asgard: { label: '阿斯加德', color: '#342f2f', icon: '/brand/faction-asgard.png' },
  taiyangcheng: { label: '太阳城', color: '#74227e', icon: '/brand/faction-taiyangcheng.png' },
  universal: { label: '通用', color: '#b4b2af' },
  olympus: { label: '奥林匹斯', color: '#0091be', icon: '/brand/faction-olympus.png' },
}

/**
 * The rules have exactly six factions plus Universal.  Everything else is a
 * card category or feature (for example Disaster and Collaboration), never a
 * faction.  Keep this allow-list explicit so a new label cannot silently
 * become a faction just because it arrived in a catalog payload.
 */
export const OFFICIAL_FACTION_KEYS = Object.freeze([
  'tianting', 'otherworld', 'gaotianyuan', 'asgard', 'taiyangcheng', 'olympus', 'universal',
] as const)

const factionAliases: Record<string, string> = {
  universal: 'universal', 通用: 'universal', 中立: 'universal',
  tianting: 'tianting', 天廷: 'tianting',
  taiyangcheng: 'taiyangcheng', 太阳城: 'taiyangcheng',
  asgard: 'asgard', 阿斯加德: 'asgard',
  gaotianyuan: 'gaotianyuan', 高天原: 'gaotianyuan',
  olympus: 'olympus', 奥林匹斯: 'olympus',
  otherworld: 'otherworld', bijie: 'otherworld', 彼界: 'otherworld',
}

export function normalizeFactionKey(faction?: string | null) {
  const value = faction?.trim() ?? ''
  return factionAliases[value.toLocaleLowerCase('zh-CN')] ?? value
}

export function isOfficialFaction(faction?: string | null) {
  return OFFICIAL_FACTION_KEYS.includes(normalizeFactionKey(faction) as typeof OFFICIAL_FACTION_KEYS[number])
}

export function getFactionPresentation(faction?: string): FactionPresentation {
  return factionPresentation[normalizeFactionKey(faction)] ?? factionPresentation.universal
}

export function factionLabel(faction?: string | null) {
  const key = normalizeFactionKey(faction)
  return isOfficialFaction(key) ? factionPresentation[key]?.label ?? '' : ''
}
