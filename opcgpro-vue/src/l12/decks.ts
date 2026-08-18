import { normalizeLookupCardType } from './cardPresentation'

export interface DeckCard {
  id: string
  number: string
  nameZh: string
  cardType: string
  product: string
  faction: string
  imageUrl?: string
  cost?: number
  troops?: number
  disasterLevel?: number
  trialValue?: number
  traits?: string[]
  profession?: string
  effect?: string
}

export interface SavedL12Deck {
  name: string
  masterId: string
  cardIds: string[]
  moraleIds: string[]
  specialIds: string[]
  updatedAt: string
}

export interface OfficialL12PresetDeck {
  name: string
  masterId: string
  cardIds: string[]
  moraleIds: string[]
  specialIds?: string[]
}

interface LookupCard {
  cardNo: string
  name: string
  type: string
  faction: string
  cost?: number | null
  attack?: number | null
  trialValue?: number | null
  image?: string
  effectText?: string
  tags?: string[]
  subType?: string
}

const STORAGE_KEY = 'l12-custom-decks-v1'
export const SELECTED_DECK_KEY = 'l12-selected-custom-deck'
export const MAIN_DECK_TYPES = new Set(['legion', 'tactic', 'counter-tactic', 'artifact'])
const S1_COUNTER_TACTICS = new Set([
  'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
  'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420',
])

const lookupFactionMap: Record<string, string> = {
  通用: 'universal', 天廷: 'tianting', 高天原: 'gaotianyuan', 阿斯加德: 'asgard',
  太阳城: 'taiyangcheng', 奥林匹斯: 'olympus', 彼界: 'otherworld', 天灾: 'disaster',
}

let catalogPromise: Promise<DeckCard[]> | null = null

export function loadDeckCatalog(): Promise<DeckCard[]> {
  if (catalogPromise) return catalogPromise
  catalogPromise = Promise.all([
    fetch('/data/l12/cards.s1.json'),
    fetch('/data/l12/cards.lookup.json'),
  ]).then(async ([s1Response, lookupResponse]) => {
    if (!s1Response.ok || !lookupResponse.ok) throw new Error('卡牌数据加载失败')
    const seasonOneRaw: DeckCard[] = await s1Response.json()
    const seasonOne = seasonOneRaw.map(card => S1_COUNTER_TACTICS.has(card.id)
      ? { ...card, cardType: 'counter-tactic' }
      : card)
    const lookup: LookupCard[] = await lookupResponse.json()
    const seasonTwo = lookup.filter(card => card.cardNo?.startsWith('S02-')).map(card => ({
      id: card.cardNo,
      number: card.cardNo,
      nameZh: card.name,
      cardType: normalizeLookupCardType(card.type, card.name),
      product: 'S02',
      faction: lookupFactionMap[card.faction] ?? card.faction,
      imageUrl: card.image ? `https://twelve-legions-card-lookup.pages.dev${card.image}` : undefined,
      cost: card.cost ?? undefined,
      troops: card.attack ?? undefined,
      trialValue: card.trialValue ?? undefined,
      traits: card.tags ?? [],
      profession: card.subType || undefined,
      effect: card.effectText ?? undefined,
    }))
    return [...seasonOne, ...seasonTwo]
  })
  return catalogPromise
}

export function loadSavedDecks(): Record<string, SavedL12Deck> {
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}')
    if (!value || typeof value !== 'object') return {}
    return Object.fromEntries(Object.entries(value).map(([name, raw]) => {
      const deck = raw as SavedL12Deck
      return [name, { ...deck, specialIds: Array.isArray(deck.specialIds) ? deck.specialIds : [] }]
    }))
  } catch {
    return {}
  }
}

export async function loadOfficialPresetDecks(): Promise<OfficialL12PresetDeck[]> {
  const responses = await Promise.all([
    fetch('/data/l12/preset-decks.s1.json'),
    fetch('/data/l12/preset-decks.s2.json'),
  ])
  if (responses.some(response => !response.ok)) throw new Error('官方预组加载失败')
  const seasons = await Promise.all(responses.map(response => response.json() as Promise<OfficialL12PresetDeck[]>))
  return seasons.flat().map(deck => ({ ...deck, specialIds: deck.specialIds ?? [] }))
}

export async function ensureOfficialPrebuiltDecks() {
  const decks = loadSavedDecks()
  const presets = await loadOfficialPresetDecks()
  let changed = false
  presets.forEach(preset => {
    if (decks[preset.name]) return
    decks[preset.name] = {
      ...preset,
      cardIds: [...preset.cardIds],
      moraleIds: [...preset.moraleIds],
      specialIds: [...(preset.specialIds ?? [])],
      updatedAt: new Date().toISOString(),
    }
    changed = true
  })
  if (changed) localStorage.setItem(STORAGE_KEY, JSON.stringify(decks))
  return decks
}

export function saveDeck(deck: SavedL12Deck) {
  const decks = loadSavedDecks()
  decks[deck.name] = deck
  localStorage.setItem(STORAGE_KEY, JSON.stringify(decks))
  localStorage.setItem(SELECTED_DECK_KEY, deck.name)
}

export function deleteDeck(name: string) {
  const decks = loadSavedDecks()
  delete decks[name]
  localStorage.setItem(STORAGE_KEY, JSON.stringify(decks))
  if (localStorage.getItem(SELECTED_DECK_KEY) === name) localStorage.removeItem(SELECTED_DECK_KEY)
}

export function validateDeck(deck: Pick<SavedL12Deck, 'name' | 'masterId' | 'cardIds' | 'moraleIds'> & { specialIds?: string[] }, catalog: DeckCard[]) {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const master = byId.get(deck.masterId)
  if (!deck.name.trim() || deck.name.trim().length > 24) return '牌库名称须为 1–24 个字符'
  if (!master || master.cardType !== 'master') return '请选择主宰'
  if (master.id === 'S01-02M2') return '复苏的奥西里斯不能被选择为主宰；请选择伊西斯'
  const countedMainDeckSize = deck.cardIds.filter(id => id !== 'S01-0212').length
  if (countedMainDeckSize < 40 || countedMainDeckSize > 50) return `主牌库须为 40–50 张（陵墓守卫不计入，当前 ${countedMainDeckSize} 张）`
  const counts = new Map<string, number>()
  for (const id of deck.cardIds) {
    const card = byId.get(id)
    if (!card || !MAIN_DECK_TYPES.has(card.cardType)) return `无效主牌：${id}`
    if (card.faction !== 'universal' && card.faction !== master.faction) return `${card.nameZh} 与主宰阵营不符`
    const count = (counts.get(id) || 0) + 1
    if (count > 3) return `${card.nameZh} 同编号最多 3 张`
    counts.set(id, count)
  }
  const moraleCount = master.faction === 'taiyangcheng' ? 6 : 8
  if (deck.moraleIds.length !== moraleCount) return `士气牌库须为 ${moraleCount} 张`
  if (deck.moraleIds.some(id => {
    const card = byId.get(id)
    return !card || !['rune', 'divinity'].includes(card.cardType) || card.faction !== master.faction
  })) return '士气卡与主宰阵营不符'
  const trialCapacity = trialCapacityForMaster(master)
  if ((deck.specialIds ?? []).length !== trialCapacity) return trialCapacity
    ? `试炼区须为 ${trialCapacity} 张（当前 ${(deck.specialIds ?? []).length} 张）`
    : `${master.nameZh} 不能携带试炼`
  if (new Set(deck.specialIds ?? []).size !== (deck.specialIds ?? []).length) return '试炼区不能放入重复卡牌'
  if ((deck.specialIds ?? []).some(id => {
    const card = byId.get(id)
    return !card || card.cardType !== 'trial' || card.faction !== master.faction
  })) return '特殊区卡牌与主宰阵营不符'
  return ''
}

export function trialCapacityForMaster(master: DeckCard | undefined) {
  if (!master || master.faction !== 'otherworld') return 0
  const effect = master.effect ?? ''
  let capacity = 1
  const carried = effect.match(/可携带\s*(\d+)\s*张[^。；\n]*试炼/)
  if (carried) capacity = Math.max(capacity, Number(carried[1]) || 0)
  for (const match of effect.matchAll(/可完成的试炼数量增加\s*(\d+)\s*张/g)) capacity += Number(match[1]) || 0
  return capacity
}

export function buildMoraleDeck(master: DeckCard | undefined, catalog: DeckCard[]) {
  if (!master) return []
  const morale = catalog.find(card => ['rune', 'divinity'].includes(card.cardType) && card.faction === master.faction)
  if (!morale) return []
  return Array(master.faction === 'taiyangcheng' ? 6 : 8).fill(morale.id)
}
