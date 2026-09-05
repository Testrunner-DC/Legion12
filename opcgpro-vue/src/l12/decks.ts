import { normalizeLookupCardType } from './cardPresentation'
import { getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'
import moraleIdentityData from '../../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'
import cardProductInclusionsData from '../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'
import cardArchiveAssetsData from '../../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'

export interface DeckCard {
  id: string
  number: string
  nameZh: string
  cardType: string
  product: string
  faction: string
  imageUrl?: string
  cost?: number
  hp?: number
  troops?: number
  disasterLevel?: number
  trialValue?: number
  rarity?: string
  deckLimit?: number
  traits?: string[]
  profession?: string
  effect?: string
  canonicalMoraleId?: string
  archiveBaseCardId?: string
  products?: string[]
}

export interface MoraleIdentity {
  faction: string
  displayName: string
  canonicalCardId: string
  versionCardIds: string[]
  godPowerCardId?: string
  godPowerDisplayName?: string
  godPowerDisplayNumber?: string
  godPowerEffectText?: string
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
  health?: number | null
  disasterLevel?: number | null
  trialValue?: number | null
  deckLimit?: number | null
  rarity?: string | null
  image?: string
  effectText?: string
  tags?: string[]
  subType?: string
}

const STORAGE_KEY = 'l12-custom-decks-v1'
export const SELECTED_DECK_KEY = 'l12-selected-custom-deck'
export const L12_DECK_SELECTION_SCOPES = [
  'ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent',
] as const
export type L12DeckSelectionScope = typeof L12_DECK_SELECTION_SCOPES[number]
export const MAIN_DECK_TYPES = new Set(['legion', 'tactic', 'counter-tactic', 'artifact'])
const AUTOMATIC_EXTRA_CARD_IDS: Readonly<Record<string, readonly string[]>> = {
  'S01-02M1': ['S01-02M2'],
}

interface CardProductInclusion {
  cardId: string
  cardPool: string
  products: string[]
}

interface CardArchiveAsset {
  id: string
  baseCardId: string
  number?: string
  nameZh?: string
  effect?: string
  product: string
  products: string[]
  rarity: string
  sourceArchiveName: string
}
const S1_COUNTER_TACTICS = new Set([
  'S01-0016', 'S01-0017', 'S01-0018', 'S01-0019', 'S01-0020', 'S01-0021',
  'S01-0120', 'S01-0223', 'S01-0224', 'S01-0320', 'S01-0420',
])

const lookupFactionMap: Record<string, string> = {
  通用: 'universal', 天廷: 'tianting', 高天原: 'gaotianyuan', 阿斯加德: 'asgard',
  太阳城: 'taiyangcheng', 奥林匹斯: 'olympus', 彼界: 'otherworld', 天灾: 'disaster',
}

export const moraleIdentities = moraleIdentityData as MoraleIdentity[]
export const cardArchiveProducts = cardProductInclusionsData.products as string[]
const productInclusions = cardProductInclusionsData.cards as CardProductInclusion[]
const productInclusionsByCardId = new Map(productInclusions.map(entry => [entry.cardId, entry]))
const cardArchiveAssets = cardArchiveAssetsData.cards as CardArchiveAsset[]
const moraleIdentityByFaction = new Map(moraleIdentities.map(identity => [identity.faction, identity]))
const moraleIdentityByVersion = new Map(moraleIdentities.flatMap(identity =>
  identity.versionCardIds.map(cardId => [cardId, identity] as const)))
const moraleIdentityByGodPower = new Map(moraleIdentities.filter(identity => identity.godPowerCardId)
  .map(identity => [identity.godPowerCardId!, identity] as const))

export function canonicalMoraleCardId(cardId: string) {
  return (moraleIdentityByVersion.get(cardId) ?? moraleIdentityByGodPower.get(cardId))?.canonicalCardId ?? cardId
}

export function displayCardNumber(card: Pick<DeckCard, 'id' | 'number'>) {
  return card.number
}

function withProductInclusions(card: DeckCard): DeckCard {
  const inclusion = productInclusionsByCardId.get(card.id)
  return inclusion ? { ...card, products: [...inclusion.products] } : card
}

export function automaticExtraCardIdsForMaster(masterId: string | null | undefined) {
  return [...(masterId ? AUTOMATIC_EXTRA_CARD_IDS[masterId] ?? [] : [])]
}

function normalizeMoraleCatalogCard(card: DeckCard): DeckCard {
  const identity = moraleIdentityByVersion.get(card.id)
  if (identity) return { ...card, nameZh: identity.displayName, canonicalMoraleId: identity.canonicalCardId }
  const powerIdentity = moraleIdentityByGodPower.get(card.id)
  return powerIdentity
    ? { ...card, nameZh: powerIdentity.godPowerDisplayName ?? '神力·奥林匹斯', canonicalMoraleId: powerIdentity.canonicalCardId }
    : card
}

function normalizeCardDimensions(card: DeckCard): DeckCard {
  return card.cardType === 'master'
    ? { ...card, cost: undefined, troops: undefined }
    : card
}

function normalizeLookupRarity(value: string | null | undefined) {
  const rarity = value?.trim().toUpperCase()
  return rarity && ['C', 'U', 'UC', 'R', 'SR', 'L', 'SEC', 'P'].includes(rarity) ? rarity : undefined
}

function lookupDeckCard(card: LookupCard): DeckCard {
  return normalizeCardDimensions(normalizeMoraleCatalogCard({
    id: card.cardNo,
    number: card.cardNo,
    nameZh: card.name,
    cardType: normalizeLookupCardType(card.type, card.name),
    product: card.cardNo.split('-')[0] || 'UNKNOWN',
    faction: lookupFactionMap[card.faction] ?? card.faction,
    imageUrl: card.image ? `https://twelve-legions-card-lookup.pages.dev${card.image}` : undefined,
    cost: card.cost ?? undefined,
    troops: card.attack ?? undefined,
    hp: card.health ?? undefined,
    disasterLevel: card.disasterLevel ?? undefined,
    trialValue: card.trialValue ?? undefined,
    deckLimit: card.deckLimit ?? undefined,
    rarity: normalizeLookupRarity(card.rarity),
    traits: card.tags ?? [],
    profession: card.subType || undefined,
    effect: card.effectText ?? undefined,
  }))
}

let catalogPromise: Promise<DeckCard[]> | null = null

function accountStorageKey() {
  return platformState.account ? `${STORAGE_KEY}:${platformState.account.id}` : STORAGE_KEY
}

function selectedDeckStorageKey() {
  return platformState.account ? `${SELECTED_DECK_KEY}:${platformState.account.id}` : SELECTED_DECK_KEY
}

function scopedSelectedDeckStorageKey(scope: L12DeckSelectionScope) {
  return `${selectedDeckStorageKey()}:${scope}`
}

export function loadSelectedDeckName(scope: L12DeckSelectionScope,
    decks: Readonly<Record<string, SavedL12Deck>>) {
  const scoped = localStorage.getItem(scopedSelectedDeckStorageKey(scope))
  if (scoped && decks[scoped]) return scoped
  // 兼容编辑器曾写入的单一选择键；迁移只读取，不在打开选择器时产生副作用。
  const legacy = localStorage.getItem(selectedDeckStorageKey())
  if (legacy && decks[legacy]) return legacy
  return Object.keys(decks)[0] ?? ''
}

export function saveSelectedDeckName(scope: L12DeckSelectionScope, name: string) {
  localStorage.setItem(scopedSelectedDeckStorageKey(scope), name)
}

function writeSavedDecks(decks: Record<string, SavedL12Deck>) {
  localStorage.setItem(accountStorageKey(), JSON.stringify(decks))
}

export function loadDeckCatalog(): Promise<DeckCard[]> {
  if (catalogPromise) return catalogPromise
  catalogPromise = Promise.all([
    fetch('/data/l12/cards.s1.json', { cache: 'no-store' }),
    fetch('/data/l12/cards.lookup.json', { cache: 'no-store' }),
    fetch('/data/l12/cards.st.json', { cache: 'no-store' }),
  ]).then(async ([s1Response, lookupResponse, stResponse]) => {
    if (!s1Response.ok || !lookupResponse.ok || !stResponse.ok) throw new Error('卡牌数据加载失败')
    const seasonOneRaw: DeckCard[] = await s1Response.json()
    const seasonOne = seasonOneRaw.map(card => S1_COUNTER_TACTICS.has(card.id)
      ? { ...card, cardType: 'counter-tactic' }
      : card)
    const lookup: LookupCard[] = await lookupResponse.json()
    const seasonTwo = lookup.filter(card => card.cardNo?.startsWith('S02-')).map(lookupDeckCard)
    const starterProducts: DeckCard[] = await stResponse.json()
    return [...seasonOne, ...seasonTwo, ...starterProducts]
      .map(normalizeMoraleCatalogCard)
      .map(normalizeCardDimensions)
  })
  return catalogPromise
}

export function loadSavedDecks(): Record<string, SavedL12Deck> {
  try {
    const key = accountStorageKey()
    if (platformState.account && localStorage.getItem(key) === null) {
      const legacy = localStorage.getItem(STORAGE_KEY)
      if (legacy) localStorage.setItem(key, legacy)
    }
    const value = JSON.parse(localStorage.getItem(key) || '{}')
    if (!value || typeof value !== 'object') return {}
    return Object.fromEntries(Object.entries(value).map(([name, raw]) => {
      const deck = raw as SavedL12Deck
      return [name, {
        ...deck,
        moraleIds: (deck.moraleIds ?? []).map(canonicalMoraleCardId),
        specialIds: Array.isArray(deck.specialIds) ? deck.specialIds : [],
      }]
    }))
  } catch {
    return {}
  }
}

export async function syncSavedDecksFromAccount(): Promise<Record<string, SavedL12Deck>> {
  const local = loadSavedDecks()
  if (!platformState.account || !platformState.token) return local
  try {
    const remote = await platformRequest<SavedL12Deck[]>('/api/decks')
    const merged: Record<string, SavedL12Deck> = Object.fromEntries(remote.map(deck => [deck.name, {
      ...deck,
      cardIds: [...deck.cardIds],
      moraleIds: deck.moraleIds.map(canonicalMoraleCardId),
      specialIds: [...(deck.specialIds ?? [])],
    }]))
    for (const deck of Object.values(local)) {
      const serverDeck = merged[deck.name]
      if (!serverDeck || deck.updatedAt > serverDeck.updatedAt) {
        const saved = await platformRequest<SavedL12Deck>('/api/decks', { method: 'PUT', body: JSON.stringify(deck) })
        merged[saved.name] = {
          ...saved,
          moraleIds: saved.moraleIds.map(canonicalMoraleCardId),
          specialIds: [...(saved.specialIds ?? [])],
        }
      }
    }
    writeSavedDecks(merged)
    return merged
  } catch {
    return local
  }
}

export async function loadOfficialPresetDecks(): Promise<OfficialL12PresetDeck[]> {
  const responses = await Promise.all([
    fetch('/data/l12/preset-decks.s1.json'),
    fetch('/data/l12/preset-decks.s2.json'),
  ])
  if (responses.some(response => !response.ok)) throw new Error('官方预组加载失败')
  const seasons = await Promise.all(responses.map(response => response.json() as Promise<OfficialL12PresetDeck[]>))
  return seasons.flat().map(deck => ({
    ...deck,
    moraleIds: deck.moraleIds.map(canonicalMoraleCardId),
    specialIds: deck.specialIds ?? [],
  }))
}

export async function ensureOfficialPrebuiltDecks() {
  const decks = await syncSavedDecksFromAccount()
  // 登录账号的官方预组只在服务端创建账号时初始化一次。这里不得按“缺失名称”反复补齐，
  // 否则玩家主动删除的预组会在下一次进入大厅/牌库页时重新出现。
  if (platformState.account && platformState.token) return decks
  const guestSeedKey = 'l12:official-presets:guest-seeded:v1'
  if (localStorage.getItem(guestSeedKey) === 'true') return decks
  if (Object.keys(decks).length > 0) {
    localStorage.setItem(guestSeedKey, 'true')
    return decks
  }
  const presets = await loadOfficialPresetDecks()
  const configuredMasterIds = await getEffectiveOperationsPolicy()
    .then(policy => new Set(policy.defaultPresetDeckIds))
    .catch(() => null)
  const defaultPresets = configuredMasterIds?.size
    ? presets.filter(preset => configuredMasterIds.has(preset.masterId))
    : presets
  let changed = false
  defaultPresets.forEach(preset => {
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
  if (changed) writeSavedDecks(decks)
  localStorage.setItem(guestSeedKey, 'true')
  return decks
}

export function saveDeck(deck: SavedL12Deck) {
  const decks = loadSavedDecks()
  deck = { ...deck, moraleIds: deck.moraleIds.map(canonicalMoraleCardId) }
  decks[deck.name] = deck
  writeSavedDecks(decks)
  localStorage.setItem(selectedDeckStorageKey(), deck.name)
  if (platformState.account) void platformRequest('/api/decks', { method: 'PUT', body: JSON.stringify(deck) }).catch(() => undefined)
}

export function deleteDeck(name: string) {
  const decks = loadSavedDecks()
  delete decks[name]
  writeSavedDecks(decks)
  const selectedKey = selectedDeckStorageKey()
  if (localStorage.getItem(selectedKey) === name) localStorage.removeItem(selectedKey)
  L12_DECK_SELECTION_SCOPES.forEach(scope => {
    const scopedKey = scopedSelectedDeckStorageKey(scope)
    if (localStorage.getItem(scopedKey) === name) localStorage.removeItem(scopedKey)
  })
  if (platformState.account) void platformRequest(`/api/decks/${encodeURIComponent(name)}`, { method: 'DELETE' }).catch(() => undefined)
}

export function validateDeck(deck: Pick<SavedL12Deck, 'name' | 'masterId' | 'cardIds' | 'moraleIds'> & { specialIds?: string[] }, catalog: DeckCard[], restrictions: readonly OperationsCardRestriction[] = []) {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const master = byId.get(deck.masterId)
  const normalizedMoraleIds = deck.moraleIds.map(canonicalMoraleCardId)
  if (!deck.name.trim() || deck.name.trim().length > 24) return '牌库名称须为 1–24 个字符'
  if (!master || master.cardType !== 'master') return '请选择主宰'
  if (master.id === 'S01-02M2') return '复苏的奥西里斯不能被选择为主宰；请选择伊西斯'
  const countedMainDeckSize = deckCountSummary(deck.cardIds, byId).counted
  if (countedMainDeckSize < 40 || countedMainDeckSize > 50) return `主牌库须为 40–50 张（规则标明不计入构筑的卡牌除外，当前 ${countedMainDeckSize} 张）`
  const counts = new Map<string, number>()
  const resolveRestriction = (cardId: string) => restrictions.find(rule => rule.cardId === cardId && rule.masterId === deck.masterId)
    ?? restrictions.find(rule => rule.cardId === cardId && !rule.masterId)
  const seasonalCounts = [deck.masterId, ...deck.cardIds, ...normalizedMoraleIds, ...(deck.specialIds ?? [])]
    .reduce((map, id) => map.set(id, (map.get(id) ?? 0) + 1), new Map<string, number>())
  for (const [id, count] of seasonalCounts) {
    const rule = resolveRestriction(id)
    if (!rule || count <= rule.maxCopies) continue
    const name = byId.get(id)?.nameZh ?? id
    return rule.maxCopies === 0
      ? `${name} 当前被禁用${rule.reason ? `：${rule.reason}` : ''}`
      : `${name} 当前最多可投入 ${rule.maxCopies} 张${rule.reason ? `：${rule.reason}` : ''}`
  }
  for (const id of deck.cardIds) {
    const card = byId.get(id)
    if (!card || !MAIN_DECK_TYPES.has(card.cardType)) return `无效主牌：${id}`
    if (card.faction !== 'universal' && card.faction !== master.faction) return `${card.nameZh} 与主宰阵营不符`
    const count = (counts.get(id) || 0) + 1
    const seasonal = resolveRestriction(id)
    const limit = Math.min(card.deckLimit ?? 3, seasonal?.maxCopies ?? Number.MAX_SAFE_INTEGER)
    if (count > limit) return `${card.nameZh} 同编号最多 ${limit} 张`
    counts.set(id, count)
  }
  const moraleCount = master.faction === 'taiyangcheng' ? 6 : 8
  if (normalizedMoraleIds.length !== moraleCount) return `士气牌库须为 ${moraleCount} 张`
  if (normalizedMoraleIds.some(id => {
    const card = byId.get(id)
    return !card || card.cardType !== 'rune' || card.faction !== master.faction
      || moraleIdentityByFaction.get(master.faction)?.canonicalCardId !== id
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

/**
 * The playable catalog intentionally excludes presentation-only alternate card
 * numbers. The archive filters by the authoritative product directory and adds
 * only approved presentation assets without changing deck building, sandbox,
 * or server card identities.
 */
export async function loadCardArchiveCatalog(): Promise<DeckCard[]> {
  const [catalog, lookupResponse] = await Promise.all([
    loadDeckCatalog(),
    fetch('/data/l12/cards.lookup.json', { cache: 'no-store' }),
  ])
  if (!lookupResponse.ok) throw new Error('卡牌版本数据加载失败')
  const lookup: LookupCard[] = await lookupResponse.json()
  const byId = new Map(catalog
    .filter(card => productInclusionsByCardId.has(card.id))
    .map(card => [card.id, withProductInclusions(card)]))
  lookup.filter(card => productInclusionsByCardId.has(card.cardNo)).map(lookupDeckCard).forEach(card => {
    if (byId.has(card.id)) return
    const base = card.id.endsWith('A') ? byId.get(card.id.slice(0, -1)) : undefined
    const archiveVersion = base && base.nameZh.trim() === card.nameZh.trim()
      ? {
          ...base,
          id: card.id,
          number: card.number,
          product: card.product,
          imageUrl: card.imageUrl,
          rarity: card.rarity ?? base.rarity,
        }
      : card
    byId.set(archiveVersion.id, withProductInclusions(normalizeCardDimensions(normalizeMoraleCatalogCard(archiveVersion))))
  })
  cardArchiveAssets.forEach(asset => {
    const base = byId.get(asset.baseCardId)
    if (!base || byId.has(asset.id)) return
    byId.set(asset.id, {
      ...base,
      id: asset.id,
      number: asset.number ?? asset.id,
      nameZh: asset.nameZh ?? base.nameZh,
      effect: asset.effect ?? base.effect,
      product: asset.product,
      products: [...asset.products],
      rarity: asset.rarity,
      imageUrl: undefined,
      archiveBaseCardId: asset.baseCardId,
    })
  })
  return [...byId.values()]
}

export function effectiveDeckLimit(card: DeckCard, masterId: string, restrictions: readonly OperationsCardRestriction[] = []) {
  const configured = restrictions.find(rule => rule.cardId === card.id && rule.masterId === masterId)?.maxCopies
    ?? restrictions.find(rule => rule.cardId === card.id && !rule.masterId)?.maxCopies
    ?? Number.MAX_SAFE_INTEGER
  return Math.min(card.deckLimit ?? 3, configured)
}

export function doesNotCountTowardMainDeck(card: DeckCard | undefined) {
  return !!card?.effect?.includes('构筑时不计入卡组数量')
}

export function deckCountSummary(cardIds: readonly string[], cards: ReadonlyMap<string, DeckCard>) {
  const uncounted = cardIds.reduce((sum, id) => sum + (doesNotCountTowardMainDeck(cards.get(id)) ? 1 : 0), 0)
  const counted = cardIds.length - uncounted
  return { counted, uncounted, label: `${counted}${uncounted ? `(${uncounted})` : ''}` }
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
  const identity = moraleIdentityByFaction.get(master.faction)
  if (!identity || !catalog.some(card => card.id === identity.canonicalCardId)) return []
  return Array(master.faction === 'taiyangcheng' ? 6 : 8).fill(identity.canonicalCardId)
}
