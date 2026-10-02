import { normalizeLookupCardType } from './cardPresentation'
import { getEffectiveOperationsPolicy, platformRequest, platformState, type OperationsCardRestriction } from './platform'
import { deploymentPath } from './deploymentBase'
import moraleIdentityData from '../../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'
import cardProductInclusionsData from '../../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'
import cardArchiveAssetsData from '../../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'
import seasonTwoRulesData from '../../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'
import {
  NORMAL_OPENING_HAND_CARD_TYPES,
  bypassesNormalDrawDeck,
  isDerivedDeckSpecialCard,
  isNormalOpeningHandCard,
  normalOpeningHandCopies,
} from './openingHandEligibility'

export { isNormalOpeningHandCard, normalOpeningHandCopies }

export interface DeckCard {
  id: string
  number: string
  nameZh: string
  cardType: string
  isCounterTactic?: boolean
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
  /** Only account-owned decks have a server-issued identity and revision. */
  id?: string
  revision?: number
  publicationId?: string | null
  publicationVersion?: number | null
  name: string
  masterId: string
  cardIds: string[]
  moraleIds: string[]
  specialIds: string[]
  benchIds?: string[]
  alternateArtSelections?: Record<string, string>
  alternateArtCopies?: Record<string, string[]>
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
const CACHE_ACTIVITY_KEY = 'l12-deck-cache-activity-v1'
export const SELECTED_DECK_KEY = 'l12-selected-custom-deck'
export const L12_DECK_SELECTION_SCOPES = [
  'ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent',
] as const
export type L12DeckSelectionScope = typeof L12_DECK_SELECTION_SCOPES[number]
export const MAIN_DECK_TYPES = NORMAL_OPENING_HAND_CARD_TYPES

/** 费用筛选按规则费用维度处理；没有印刷费用的非主宰卡归入0费，但仍不伪造卡面数字。 */
export function filterableCardCost(card: Pick<DeckCard, 'cardType' | 'cost'>): number | null {
  return card.cardType === 'master' ? null : (card.cost ?? 0)
}
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
const seasonTwoIdentityById = new Map((seasonTwoRulesData as Array<{
  id: string; cardType: string; isCounterTactic?: boolean; deckLimit?: number
}>).map(card => [card.id, card]))

const lookupFactionMap: Record<string, string> = {
  通用: 'universal', 天廷: 'tianting', 高天原: 'gaotianyuan', 阿斯加德: 'asgard',
  太阳城: 'taiyangcheng', 奥林匹斯: 'olympus', 彼界: 'otherworld', 天灾: 'disaster',
}

export const moraleIdentities = moraleIdentityData as MoraleIdentity[]
export const cardArchiveProducts = cardProductInclusionsData.products as string[]
const productInclusions = cardProductInclusionsData.cards as CardProductInclusion[]
const productInclusionsByCardId = new Map(productInclusions.map(entry => [entry.cardId, entry]))
const productInclusionsByNormalizedCardId = new Map(productInclusions.map(entry => [entry.cardId.toLocaleLowerCase(), entry]))
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

export function cardProductsForIds(cardIds: readonly string[]) {
  const included = new Set(cardIds.flatMap(cardId => productInclusionsByNormalizedCardId.get(cardId.toLocaleLowerCase())?.products ?? []))
  return [
    ...cardArchiveProducts.filter(product => included.delete(product)),
    ...[...included].sort((left, right) => left.localeCompare(right, 'zh-CN', { numeric: true })),
  ]
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
  const authoritative = seasonTwoIdentityById.get(card.cardNo)
  return normalizeCardDimensions(normalizeMoraleCatalogCard({
    id: card.cardNo,
    number: card.cardNo,
    nameZh: card.name,
    cardType: authoritative?.cardType ?? normalizeLookupCardType(card.type, card.name),
    isCounterTactic: authoritative?.isCounterTactic === true,
    product: card.cardNo.split('-')[0] || 'UNKNOWN',
    faction: lookupFactionMap[card.faction] ?? card.faction,
    imageUrl: card.image ? `https://twelve-legions-card-lookup.pages.dev${card.image}` : undefined,
    cost: card.cost ?? undefined,
    troops: card.attack ?? undefined,
    hp: card.health ?? undefined,
    disasterLevel: card.disasterLevel ?? undefined,
    trialValue: card.trialValue ?? undefined,
    deckLimit: authoritative?.deckLimit ?? card.deckLimit ?? undefined,
    rarity: normalizeLookupRarity(card.rarity),
    traits: card.tags ?? [],
    profession: card.subType || undefined,
    effect: card.effectText ?? undefined,
  }))
}

let catalogPromise: Promise<DeckCard[]> | null = null

function accountStorageKey(accountId = platformState.account?.id) {
  return accountId ? `${STORAGE_KEY}:${accountId}` : STORAGE_KEY
}

function selectedDeckStorageKey(accountId = platformState.account?.id) {
  return accountId ? `${SELECTED_DECK_KEY}:${accountId}` : SELECTED_DECK_KEY
}

function scopedSelectedDeckStorageKey(scope: L12DeckSelectionScope, accountId = platformState.account?.id) {
  return `${selectedDeckStorageKey(accountId)}:${scope}`
}

export function loadSelectedDeckName(scope: L12DeckSelectionScope,
    decks: Readonly<Record<string, SavedL12Deck>>) {
  const scoped = localStorage.getItem(scopedSelectedDeckStorageKey(scope))
  if (scoped) {
    const selected = Object.values(decks).find(deck => deck.id && deck.id === scoped)
    if (selected) return selected.name
    if (scoped.startsWith('unresolved:')) return ''
    if (decks[scoped] && !decks[scoped].id) return scoped
    // A stale account name must never silently select a different deck that reused that name.
    return ''
  }
  // 兼容编辑器曾写入的单一选择键；迁移只读取，不在打开选择器时产生副作用。
  const legacy = localStorage.getItem(selectedDeckStorageKey())
  if (legacy) {
    const selected = Object.values(decks).find(deck => deck.id && deck.id === legacy)
    if (selected) return selected.name
    if (legacy.startsWith('unresolved:')) return ''
    if (decks[legacy] && !decks[legacy].id) return legacy
    return ''
  }
  return Object.keys(decks)[0] ?? ''
}

export function saveSelectedDeckName(scope: L12DeckSelectionScope, name: string) {
  const deck = loadSavedDecks()[name]
  localStorage.setItem(scopedSelectedDeckStorageKey(scope), deck?.id || name)
}

function normalizeSavedDeck(deck: SavedL12Deck): SavedL12Deck {
  const alternateArtSelections = Object.fromEntries(Object.entries(deck.alternateArtSelections ?? {})
    .filter(([cardId, artId]) => cardId.trim() && typeof artId === 'string' && artId.trim())
    .slice(0, 128).map(([cardId, artId]) => [cardId.trim(), artId.trim()]))
  const alternateArtCopies = Object.fromEntries(Object.entries(deck.alternateArtCopies ?? {})
    .filter(([cardId, artIds]) => cardId.trim() && Array.isArray(artIds))
    .slice(0, 128).map(([cardId, artIds]) => [cardId.trim(), artIds.slice(0, 50)
      .map(artId => typeof artId === 'string' ? artId.trim() : '')]))
  return {
    ...deck,
    cardIds: [...deck.cardIds],
    moraleIds: (deck.moraleIds ?? []).map(canonicalMoraleCardId),
    specialIds: [...(deck.specialIds ?? [])],
    benchIds: (deck.benchIds ?? []).filter(id => typeof id === 'string' && id.trim())
      .slice(0, 200).map(id => id.trim()),
    alternateArtSelections,
    alternateArtCopies,
  }
}

function readSavedDecks(storageKey: string): Record<string, SavedL12Deck> {
  try {
    const value = JSON.parse(localStorage.getItem(storageKey) || '{}')
    if (!value || typeof value !== 'object') return {}
    return Object.fromEntries(Object.entries(value).map(([name, raw]) => [name, normalizeSavedDeck(raw as SavedL12Deck)]))
  } catch {
    return {}
  }
}

function writeSavedDecks(decks: Record<string, SavedL12Deck>, storageKey = accountStorageKey()) {
  localStorage.setItem(storageKey, JSON.stringify(decks))
}

function sameDeckName(first: string, second: string) {
  return first.toLocaleLowerCase('zh-CN') === second.toLocaleLowerCase('zh-CN')
}

function upsertCachedDeck(decks: Record<string, SavedL12Deck>, deck: SavedL12Deck) {
  Object.keys(decks).filter(name => sameDeckName(name, deck.name)
    || Boolean(deck.id && decks[name]?.id === deck.id)).forEach(name => delete decks[name])
  decks[deck.name] = deck
}

function deckIdentityContent(deck: SavedL12Deck) {
  return JSON.stringify([deck.masterId, deck.cardIds, deck.moraleIds, deck.specialIds, deck.benchIds ?? []])
}

function migrateDeckSelectionReferences(context: ReturnType<typeof captureDeckStorageContext>,
  previous: Readonly<Record<string, SavedL12Deck>>, authoritative: Readonly<Record<string, SavedL12Deck>>) {
  const currentById = new Map(Object.values(authoritative).filter(deck => deck.id).map(deck => [deck.id!, deck]))
  const keys = [context.selectedKey, ...L12_DECK_SELECTION_SCOPES.map(scope =>
    scopedSelectedDeckStorageKey(scope, context.accountId))]
  for (const key of keys) {
    const value = localStorage.getItem(key)
    if (!value || currentById.has(value) || value.startsWith('unresolved:')) continue
    const cached = previous[value]
    const exactId = cached?.id && currentById.get(cached.id)
    const legacy = !cached?.id && cached && authoritative[value]
      && deckIdentityContent(cached) === deckIdentityContent(authoritative[value])
        ? authoritative[value] : null
    const matched = exactId || legacy
    localStorage.setItem(key, matched?.id || `unresolved:${value}`)
  }
}

function captureDeckStorageContext() {
  const accountId = platformState.account?.id
  return {
    accountId,
    token: platformState.token,
    storageKey: accountStorageKey(accountId),
    selectedKey: selectedDeckStorageKey(accountId),
  }
}

function assertCompleteDeckAccount(context: ReturnType<typeof captureDeckStorageContext>) {
  if (!!context.accountId === !!context.token) return
  throw new Error('账号登录状态正在变更，请稍后重试')
}

async function withGuestDeckMutation<T>(context: ReturnType<typeof captureDeckStorageContext>, mutate: () => T | Promise<T>): Promise<T> {
  const run = () => {
    if (!isCurrentDeckStorageContext(context)) throw new Error('账号已切换，请重新打开牌库')
    return mutate()
  }
  return globalThis.navigator?.locks
    ? navigator.locks.request(`l12:deck-save:${context.storageKey}`, run) : run()
}

function deckCacheActivityKey(accountId: string | undefined) {
  return `${CACHE_ACTIVITY_KEY}:${accountId ?? 'guest'}`
}

function markDeckCacheActivity(context: ReturnType<typeof captureDeckStorageContext>) {
  const stamp = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
  localStorage.setItem(deckCacheActivityKey(context.accountId), stamp)
  return stamp
}

function deckCacheActivity(context: ReturnType<typeof captureDeckStorageContext>) {
  return localStorage.getItem(deckCacheActivityKey(context.accountId)) ?? ''
}

function isCurrentDeckStorageContext(context: ReturnType<typeof captureDeckStorageContext>) {
  return platformState.account?.id === context.accountId && platformState.token === context.token
}

function visibleDecksAfterAsyncWork(context: ReturnType<typeof captureDeckStorageContext>) {
  return isCurrentDeckStorageContext(context) ? readSavedDecks(context.storageKey) : loadSavedDecks()
}

export function loadDeckCatalog(): Promise<DeckCard[]> {
  if (catalogPromise) return catalogPromise
  catalogPromise = Promise.all([
    fetch(deploymentPath('/data/l12/cards.s1.json'), { cache: 'no-store' }),
    fetch(deploymentPath('/data/l12/cards.lookup.json'), { cache: 'no-store' }),
    fetch(deploymentPath('/data/l12/cards.st.json'), { cache: 'no-store' }),
  ]).then(async ([s1Response, lookupResponse, stResponse]) => {
    if (!s1Response.ok || !lookupResponse.ok || !stResponse.ok) throw new Error('卡牌数据加载失败')
    const seasonOne: DeckCard[] = await s1Response.json()
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
  return readSavedDecks(accountStorageKey())
}

export async function syncSavedDecksFromAccount(): Promise<Record<string, SavedL12Deck>> {
  const context = captureDeckStorageContext()
  const local = readSavedDecks(context.storageKey)
  if (!context.accountId || !context.token) return local
  const activity = markDeckCacheActivity(context)
  try {
    const remote = await platformRequest<SavedL12Deck[]>('/api/decks')
    // Mutation completion, a newer sync, or an account switch makes this response stale for both cache and current view.
    if (!isCurrentDeckStorageContext(context) || deckCacheActivity(context) !== activity) {
      return visibleDecksAfterAsyncWork(context)
    }
    const authoritative = Object.fromEntries(remote.map(deck => [deck.name, normalizeSavedDeck(deck)]))
    // 登录同步只消费服务端权威列表。旧标签页、其他设备或旧版留下的缓存不能因远端缺失而自动上传。
    migrateDeckSelectionReferences(context, local, authoritative)
    writeSavedDecks(authoritative, context.storageKey)
    return authoritative
  } catch {
    return visibleDecksAfterAsyncWork(context)
  }
}

export async function loadOfficialPresetDecks(): Promise<OfficialL12PresetDeck[]> {
  const responses = await Promise.all([
    fetch(deploymentPath('/data/l12/preset-decks.s1.json')),
    fetch(deploymentPath('/data/l12/preset-decks.s2.json')),
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
  const context = captureDeckStorageContext()
  let decks = await syncSavedDecksFromAccount()
  if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
  // 登录账号的官方预组只在服务端创建账号时初始化一次。这里不得按“缺失名称”反复补齐，
  // 否则玩家主动删除的预组会在下一次进入大厅/牌库页时重新出现。
  if (context.accountId || context.token) return decks
  await withGuestDeckMutation(context, () => {
    const current = readSavedDecks(context.storageKey)
    const previous = structuredClone(current)
    let changed = false
    for (const deck of Object.values(current)) {
      if (deck.id && deck.revision) continue
      deck.id ||= globalThis.crypto?.randomUUID?.() || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
      deck.revision ||= 1
      changed = true
    }
    if (changed) {
      writeSavedDecks(current, context.storageKey)
      migrateDeckSelectionReferences(context, previous, current)
    }
  })
  decks = readSavedDecks(context.storageKey)
  const guestSeedKey = 'l12:official-presets:guest-seeded:v1'
  if (localStorage.getItem(guestSeedKey) === 'true') return decks
  if (Object.keys(decks).length > 0) {
    localStorage.setItem(guestSeedKey, 'true')
    return decks
  }
  const presets = await loadOfficialPresetDecks()
  if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
  const configuredMasterIds = await getEffectiveOperationsPolicy()
    .then(policy => new Set(policy.defaultPresetDeckIds))
    .catch(() => null)
  if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
  const defaultPresets = configuredMasterIds?.size
    ? presets.filter(preset => configuredMasterIds.has(preset.masterId))
    : presets
  return await withGuestDeckMutation(context, () => {
  const current = readSavedDecks(context.storageKey)
  if (localStorage.getItem(guestSeedKey) === 'true') return current
  if (Object.keys(current).length === 0) defaultPresets.forEach(preset => {
    current[preset.name] = {
      ...preset,
      id: globalThis.crypto?.randomUUID?.() || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
      revision: 1,
      cardIds: [...preset.cardIds],
      moraleIds: [...preset.moraleIds],
      specialIds: [...(preset.specialIds ?? [])],
      updatedAt: new Date().toISOString(),
    }
  })
  writeSavedDecks(current, context.storageKey)
  localStorage.setItem(guestSeedKey, 'true')
  return current
  })
}

export async function saveDeck(deck: SavedL12Deck): Promise<SavedL12Deck> {
  const context = captureDeckStorageContext()
  assertCompleteDeckAccount(context)
  markDeckCacheActivity(context)
  const normalized = normalizeSavedDeck(deck)
  try {
    const commit = (saved: SavedL12Deck) => {
      const decks = readSavedDecks(context.storageKey)
      upsertCachedDeck(decks, saved)
      writeSavedDecks(decks, context.storageKey)
      markDeckCacheActivity(context)
      return saved
    }
    if (!context.accountId) {
      const saveLocal = () => {
        const entries = Object.values(readSavedDecks(context.storageKey))
        const current = entries.find(value => normalized.id ? value.id === normalized.id : sameDeckName(value.name, normalized.name))
        if (normalized.id && (!current || current.revision !== normalized.revision)
          || !normalized.id && current?.id)
          throw new Error('牌库已被其他操作更新或删除，请重新打开；当前修改可另存为牌库')
        if (entries.some(value => sameDeckName(value.name, normalized.name)
          && (normalized.id ? value.id !== normalized.id : Boolean(value.id))))
          throw new Error('已有同名牌库，请使用其他名称')
        return commit({ ...normalized, id: normalized.id || globalThis.crypto?.randomUUID?.()
          || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
          revision: (normalized.revision ?? 0) + 1 })
      }
      // 同源标签页使用同一锁，在锁内读取版本并落盘，避免并发检查后覆盖。
      return await withGuestDeckMutation(context, saveLocal)
    }
    const saved = normalizeSavedDeck(await (normalized.id
        ? platformRequest<SavedL12Deck>(`/api/decks/by-id/${encodeURIComponent(normalized.id)}`, {
          method: 'PUT', body: JSON.stringify({ deck: normalized, expectedRevision: normalized.revision }),
        })
        : platformRequest<SavedL12Deck>('/api/decks', { method: 'POST', body: JSON.stringify(normalized) })))
    // Saving, importing or copying a deck must not silently change a battle mode's selection.
    // Each mode records its own explicit choice through saveSelectedDeckName.
    return commit(saved)
  } catch (error) {
    markDeckCacheActivity(context)
    throw error
  }
}

export async function deleteDeck(target: string | SavedL12Deck): Promise<void> {
  const context = captureDeckStorageContext()
  assertCompleteDeckAccount(context)
  markDeckCacheActivity(context)
  try {
    const remove = async () => {
    const name = typeof target === 'string' ? target : target.name
    const selectedId = typeof target === 'string' ? undefined : target.id
    if (context.accountId && typeof target !== 'string' && !selectedId)
      throw new Error('牌库身份尚未同步，请刷新后重试')
    const cached = readSavedDecks(context.storageKey)
    const current = selectedId
      ? Object.values(cached).find(deck => deck.id === selectedId)
      : cached[name]
    if (!current) throw new Error('牌库已变化，请刷新后重试')
    if (typeof target !== 'string' && target.revision !== current.revision)
      throw new Error('牌库已被其他操作更新，请刷新后重试')
    if (context.accountId) {
      if (!current.id || !current.revision) throw new Error('牌库身份尚未同步，请刷新后重试')
      try {
        await platformRequest(`/api/decks/by-id/${encodeURIComponent(current.id)}?expectedRevision=${current.revision}`, { method: 'DELETE' })
      } catch (error) {
        // DELETE is idempotent from the user's perspective: 404 also confirms that the server no longer has this deck.
        if (!error || typeof error !== 'object' || !('status' in error) || error.status !== 404) throw error
      }
    }
    const decks = readSavedDecks(context.storageKey)
    Object.keys(decks).filter(deckName => current.id
      ? decks[deckName]?.id === current.id
      : sameDeckName(deckName, name)).forEach(deckName => delete decks[deckName])
    writeSavedDecks(decks, context.storageKey)
    const selectedWasDeleted = (value: string | null) => Boolean(value && (current.id
      ? value === current.id || (sameDeckName(value, current.name) && !Object.values(decks)
        .some(deck => sameDeckName(deck.name, value)))
      : sameDeckName(value, name)))
    if (selectedWasDeleted(localStorage.getItem(context.selectedKey))) localStorage.removeItem(context.selectedKey)
    L12_DECK_SELECTION_SCOPES.forEach(scope => {
      const scopedKey = scopedSelectedDeckStorageKey(scope, context.accountId)
      if (selectedWasDeleted(localStorage.getItem(scopedKey))) localStorage.removeItem(scopedKey)
    })
    markDeckCacheActivity(context)
    }
    if (context.accountId) await remove()
    else await withGuestDeckMutation(context, remove)
  } catch (error) {
    markDeckCacheActivity(context)
    throw error
  }
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
    if (isDerivedSpecialCard(card)) return `${card?.nameZh ?? id}为 Limit ${card?.deckLimit ?? 1} 的衍生卡，不能放入主牌库`
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
    fetch(deploymentPath('/data/l12/cards.lookup.json'), { cache: 'no-store' }),
  ])
  if (!lookupResponse.ok) throw new Error('卡牌版本数据加载失败')
  const lookup: LookupCard[] = await lookupResponse.json()
  const catalogById = new Map(catalog.map(card => [card.id, card]))
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
    const base = byId.get(asset.baseCardId) ?? catalogById.get(asset.baseCardId)
    const existing = byId.get(asset.id)
    if (existing) {
      byId.set(asset.id, {
        ...existing,
        products: [...asset.products],
        rarity: asset.rarity || existing.rarity,
        archiveBaseCardId: asset.baseCardId,
      })
      return
    }
    if (!base) return
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
  return bypassesNormalDrawDeck(card)
}

export function isDerivedSpecialCard(card: DeckCard | undefined) {
  return isDerivedDeckSpecialCard(card)
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
