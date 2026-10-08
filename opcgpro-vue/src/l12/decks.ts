import { watch } from 'vue'
import { readDeckCache, requireDeckCache, commitDeckCache, interpretDeckSelection,
  DeckCacheStorageError, type ReadableDeckCache, type DeckSelectionAliases } from './deckCacheStorage'
import { encodeDeckCache, decodeDeckCache } from './deckCacheCodec'

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

export interface PrivateDeckSummaryCounts {
  main: number
  uncountedMain: number
  morale: number
  special: number
  bench: number
}

export interface PrivateDeckSummary {
  id: string
  revision: number
  name: string
  masterId: string
  updatedAt: string
  publicationId: string | null
  publicationVersion: number | null
  counts: PrivateDeckSummaryCounts
  legal: boolean
  legalityReason: string | null
}

export interface PrivateDeckSummaryPage {
  items: PrivateDeckSummary[]
  total: number
  page: number
  pageSize: number
  generation: number
  permissionVersion: number
  catalogVersion: string
  policyVersion: number
  facets: {
    masters: Array<{ masterId: string; count: number }>
    legal: number
    illegal: number
  }
}

export interface PrivateDeckSummaryQuery {
  page?: number
  pageSize?: number
  keyword?: string
  masterId?: string
  legal?: boolean
  sort?: 'latest' | 'name'
  exactName?: string
  publicationId?: string
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
  const snapshot = readDeckCache(localStorage, accountStorageKey(), deckCacheOwner(platformState.account?.id))
  if (snapshot.status === 'unavailable') return ''
  const keys = [scopedSelectedDeckStorageKey(scope), selectedDeckStorageKey()]
  for (const key of keys) {
    const raw = localStorage.getItem(key)
    if (!raw) continue
    const value = interpretDeckSelection(snapshot, key, raw)
    if (value.startsWith('unresolved:')) return ''
    const selected = Object.values(decks).find(deck => deck.id && deck.id === value)
    if (selected) return selected.name
    return Object.hasOwn(decks, value) && !decks[value].id ? value : ''
  }
  return Object.keys(decks)[0] ?? ''
}

export function saveSelectedDeckName(scope: L12DeckSelectionScope, name: string) {
  const decks = readSavedDecks(accountStorageKey())
  const deck = Object.hasOwn(decks, name) ? decks[name] : undefined
  if (!deck) throw new Error('当前模式没有可选择的牌库，请刷新后重试')
  localStorage.setItem(scopedSelectedDeckStorageKey(scope), deck.id || name)
}

function deckCacheOwner(accountId?: string) {
  return accountId ? `account:${accountId}` : 'guest'
}
function ownerForStorageKey(storageKey: string) {
  return storageKey === STORAGE_KEY ? 'guest' : `account:${storageKey.slice(STORAGE_KEY.length + 1)}`
}
function normalizeSavedDeck(deck: SavedL12Deck): SavedL12Deck {
  // Storage validation must retain original metadata, IDs and every copy's position.
  return decodeDeckCache(encodeDeckCache(deck))
}
function readSavedDecks(storageKey: string): Record<string, SavedL12Deck> {
  return requireDeckCache(readDeckCache(localStorage, storageKey, ownerForStorageKey(storageKey))).decks
}

function sameDeckName(first: string, second: string) {
  return first.toLocaleLowerCase('zh-CN') === second.toLocaleLowerCase('zh-CN')
}

function upsertCachedDeck(decks: Record<string, SavedL12Deck>, deck: SavedL12Deck) {
  Object.keys(decks).filter(name => sameDeckName(name, deck.name)
    || Boolean(deck.id && decks[name]?.id === deck.id)).forEach(name => delete decks[name])
  Object.defineProperty(decks, deck.name, { value: deck, enumerable: true, writable: true, configurable: true })
}

function deckIdentityContent(deck: SavedL12Deck) {
  return JSON.stringify([deck.masterId, deck.cardIds, deck.moraleIds, deck.specialIds, deck.benchIds ?? []])
}

function migrateDeckSelectionReferences(context: ReturnType<typeof captureDeckStorageContext>,
  previous: ReadableDeckCache, authoritative: Readonly<Record<string, SavedL12Deck>>): DeckSelectionAliases {
  const currentById = new Map(Object.values(authoritative).filter(deck => deck.id).map(deck => [deck.id!, deck]))
  const aliases: DeckSelectionAliases = {}
  const keys = [context.selectedKey, ...L12_DECK_SELECTION_SCOPES.map(scope =>
    scopedSelectedDeckStorageKey(scope, context.accountId))]
  for (const key of keys) {
    const raw = localStorage.getItem(key)
    if (!raw) continue
    const effective = interpretDeckSelection(previous, key, raw)
    if (effective.startsWith('unresolved:')) { aliases[key] = { raw, resolved: effective }; continue }
    const cached = Object.values(previous.decks).find(deck => deck.id === effective) ?? (Object.hasOwn(previous.decks, raw) ? previous.decks[raw] : undefined)
    const legacy = !cached?.id && cached && authoritative[raw]
      && deckIdentityContent(cached) === deckIdentityContent(authoritative[raw]) ? authoritative[raw] : null
    const matched = currentById.get(effective) || (cached?.id && currentById.get(cached.id)) || legacy
    const resolved = matched?.id || `unresolved:${raw}`
    if (resolved !== raw) aliases[key] = { raw, resolved }
  }
  return aliases
}

let deckAccountEpoch = 0
watch(() => [platformState.account?.id, platformState.token], (current, previous) => {
  if (!previous || current[0] !== previous[0] || current[1] !== previous[1]) deckAccountEpoch++
}, { flush: 'sync' })

export function captureDeckAccountGuard() {
  const context = captureDeckStorageContext()
  return () => isCurrentDeckStorageContext(context)
}

function captureDeckStorageContext() {
  const accountId = platformState.account?.id
  return {
    accountId,
    epoch: deckAccountEpoch,
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
  return deckAccountEpoch === context.epoch && platformState.account?.id === context.accountId && platformState.token === context.token
}

function visibleDecksAfterAsyncWork(context: ReturnType<typeof captureDeckStorageContext>) {
  return isCurrentDeckStorageContext(context) ? readSavedDecks(context.storageKey) : loadSavedDecks()
}

const deckErrorContexts = new WeakMap<object, ReturnType<typeof captureDeckStorageContext>>()
export function deckErrorBelongsToCurrentAccount(error: unknown) {
  const context = error && typeof error === 'object' ? deckErrorContexts.get(error) : undefined
  return !context || isCurrentDeckStorageContext(context)
}
function operationError(error: unknown, context: ReturnType<typeof captureDeckStorageContext>, confirmed = false): Error {
  const original = error instanceof Error ? error : new Error(String(error))
  const result = confirmed ? new Error(`服务器已确认本次操作；${original.message}。本机缓存未更新，请刷新后同步`) : original
  Object.assign(result, { serverConfirmed: confirmed })
  deckErrorContexts.set(result, context)
  return result
}
function requireCurrentOperation(context: ReturnType<typeof captureDeckStorageContext>, activity: string) {
  if (!isCurrentDeckStorageContext(context)) throw new Error('账号已切换，已忽略旧操作的本机结果')
  if (deckCacheActivity(context) !== activity) throw new Error('牌库已被更新，已忽略迟到的本机结果')
}
function currentDeckSnapshot(context: ReturnType<typeof captureDeckStorageContext>) {
  return requireDeckCache(readDeckCache(localStorage, context.storageKey, deckCacheOwner(context.accountId)))
}
function commitSavedDecks(context: ReturnType<typeof captureDeckStorageContext>, previous: ReadableDeckCache,
  decks: Record<string, SavedL12Deck>, activity: string) {
  const aliases = migrateDeckSelectionReferences(context, previous, decks)
  return commitDeckCache(localStorage, context.storageKey, previous, decks, activity, aliases,
    () => requireCurrentOperation(context, activity)).decks
}

function privateDeckObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && Object.getPrototypeOf(value) === Object.prototype
}
function privateDeckExactFields(value: Record<string, unknown>, fields: readonly string[]) {
  const keys = Reflect.ownKeys(value)
  return keys.length === fields.length && fields.every(field => Object.hasOwn(value, field))
}
function privateDeckInteger(value: unknown, minimum = 0): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum
}
function privateDeckText(value: unknown, allowEmpty = false): value is string {
  return typeof value === 'string' && (allowEmpty || Boolean(value.trim()))
}
function privateDeckContractError(message: string): never {
  throw new Error(`牌库目录响应无效：${message}`)
}
function normalizePrivateDeckSummaryQuery(query: PrivateDeckSummaryQuery = {}) {
  const page = query.page ?? 1, pageSize = query.pageSize ?? 30, sort = query.sort ?? 'latest'
  if (!privateDeckInteger(page, 1) || !privateDeckInteger(pageSize, 1) || pageSize > 100)
    throw new Error('牌库目录页码或每页数量无效')
  if (sort !== 'latest' && sort !== 'name') throw new Error('牌库目录排序无效')
  if (query.legal !== undefined && typeof query.legal !== 'boolean') throw new Error('牌库目录合法性筛选无效')
  if (query.keyword !== undefined && typeof query.keyword !== 'string') throw new Error('牌库目录搜索词无效')
  if (query.masterId !== undefined && typeof query.masterId !== 'string') throw new Error('牌库目录主宰筛选无效')
  if (query.exactName !== undefined && (!privateDeckText(query.exactName) || query.exactName.trim().length > 24))
    throw new Error('牌库名称筛选无效')
  if (query.publicationId !== undefined && (!privateDeckText(query.publicationId) || query.publicationId.trim().length > 128))
    throw new Error('牌库来源筛选无效')
  return { page, pageSize, sort, keyword: query.keyword?.trim() || undefined,
    masterId: query.masterId?.trim() || undefined, legal: query.legal,
    exactName: query.exactName?.trim(), publicationId: query.publicationId?.trim() }
}

export function privateDeckSummaryRequestPath(query: PrivateDeckSummaryQuery = {}) {
  const normalized = normalizePrivateDeckSummaryQuery(query)
  const values = new URLSearchParams({ page: String(normalized.page), pageSize: String(normalized.pageSize), sort: normalized.sort })
  if (normalized.keyword) values.set('keyword', normalized.keyword)
  if (normalized.masterId) values.set('masterId', normalized.masterId)
  if (normalized.legal !== undefined) values.set('legal', String(normalized.legal))
  if (normalized.exactName !== undefined) values.set('exactName', normalized.exactName)
  if (normalized.publicationId !== undefined) values.set('publicationId', normalized.publicationId)
  return `/api/decks/summaries?${values}`
}

export function validatePrivateDeckSummaryPage(value: unknown, query: PrivateDeckSummaryQuery = {}): PrivateDeckSummaryPage {
  const normalized = normalizePrivateDeckSummaryQuery(query)
  const pageFields = ['items', 'total', 'page', 'pageSize', 'generation', 'permissionVersion', 'catalogVersion', 'policyVersion', 'facets'] as const
  if (!privateDeckObject(value) || !privateDeckExactFields(value, pageFields)) privateDeckContractError('分页字段不完整')
  if (!Array.isArray(value.items) || !privateDeckInteger(value.total) || value.page !== normalized.page
    || value.pageSize !== normalized.pageSize || !privateDeckInteger(value.generation)
    || !privateDeckInteger(value.permissionVersion) || !privateDeckInteger(value.policyVersion)
    || typeof value.catalogVersion !== 'string' || !/^[0-9A-F]{64}$/.test(value.catalogVersion))
    privateDeckContractError('分页身份或版本字段无效')
  if (value.items.length > normalized.pageSize || value.items.length > value.total) privateDeckContractError('分页数量无效')
  const itemFields = ['id', 'revision', 'name', 'masterId', 'updatedAt', 'publicationId', 'publicationVersion', 'counts', 'legal', 'legalityReason'] as const
  const countFields = ['main', 'uncountedMain', 'morale', 'special', 'bench'] as const
  const identities = new Set<string>()
  const items = value.items.map((entry, index) => {
    if (!privateDeckObject(entry) || !privateDeckExactFields(entry, itemFields)) privateDeckContractError(`第${index + 1}项字段无效`)
    if (!privateDeckText(entry.id) || identities.has(entry.id as string) || !privateDeckInteger(entry.revision, 1)
      || !privateDeckText(entry.name) || !privateDeckText(entry.masterId) || !privateDeckText(entry.updatedAt)
      || !Number.isFinite(Date.parse(entry.updatedAt as string)) || typeof entry.legal !== 'boolean')
      privateDeckContractError(`第${index + 1}项身份或正文元数据无效`)
    identities.add(entry.id as string)
    if (normalized.exactName !== undefined && String(entry.name).trim().toUpperCase() !== normalized.exactName.toUpperCase())
      privateDeckContractError(`第${index + 1}项名称筛选不一致`)
    if (normalized.publicationId !== undefined && entry.publicationId !== normalized.publicationId)
      privateDeckContractError(`第${index + 1}项公开来源筛选不一致`)
    const hasPublication = entry.publicationId !== null
    if (hasPublication !== (entry.publicationVersion !== null)
      || hasPublication && (!privateDeckText(entry.publicationId) || !privateDeckInteger(entry.publicationVersion, 1)))
      privateDeckContractError(`第${index + 1}项公开来源无效`)
    const counts = entry.counts
    if (!privateDeckObject(counts) || !privateDeckExactFields(counts, countFields)
      || countFields.some(field => !privateDeckInteger(counts[field]))) privateDeckContractError(`第${index + 1}项计数无效`)
    if (entry.legal ? entry.legalityReason !== null
      : !privateDeckText(entry.legalityReason)) privateDeckContractError(`第${index + 1}项合法性原因无效`)
    if (normalized.masterId && (entry.masterId as string).toLocaleLowerCase() !== normalized.masterId.toLocaleLowerCase())
      privateDeckContractError(`第${index + 1}项不符合主宰筛选`)
    if (normalized.legal !== undefined && entry.legal !== normalized.legal) privateDeckContractError(`第${index + 1}项不符合合法性筛选`)
    return entry as unknown as PrivateDeckSummary
  })
  if (!privateDeckObject(value.facets) || !privateDeckExactFields(value.facets, ['masters', 'legal', 'illegal'])
    || !Array.isArray(value.facets.masters) || !privateDeckInteger(value.facets.legal) || !privateDeckInteger(value.facets.illegal))
    privateDeckContractError('聚合字段无效')
  const masters = new Set<string>()
  for (const facet of value.facets.masters) {
    if (!privateDeckObject(facet) || !privateDeckExactFields(facet, ['masterId', 'count'])
      || !privateDeckText(facet.masterId) || !privateDeckInteger(facet.count, 1)
      || masters.has((facet.masterId as string).toLocaleLowerCase())) privateDeckContractError('主宰聚合无效')
    masters.add((facet.masterId as string).toLocaleLowerCase())
  }
  if (value.facets.legal + value.facets.illegal !== value.total
    || value.facets.masters.reduce((total, facet) => total + Number((facet as Record<string, unknown>).count), 0) !== value.total)
    privateDeckContractError('聚合总数无效')
  return { ...(value as unknown as PrivateDeckSummaryPage), items }
}

export async function loadPrivateDeckSummaryPage(query: PrivateDeckSummaryQuery = {}): Promise<PrivateDeckSummaryPage> {
  const context = captureDeckStorageContext()
  try {
    assertCompleteDeckAccount(context)
    if (!context.accountId || !context.token) throw new Error('请先登录账号再读取牌库目录')
    const response = await platformRequest<unknown>(privateDeckSummaryRequestPath(query), { cache: 'no-store' })
    if (!isCurrentDeckStorageContext(context)) throw new Error('账号已切换，已忽略旧牌库目录')
    return validatePrivateDeckSummaryPage(response, query)
  } catch (error) { throw operationError(error, context) }
}

export async function loadPrivateDeckBody(target: Pick<PrivateDeckSummary, 'id' | 'revision'>,
    current: () => boolean = () => true): Promise<SavedL12Deck> {
  const context = captureDeckStorageContext()
  try {
    assertCompleteDeckAccount(context)
    if (!context.accountId || !context.token) throw new Error('请先登录账号再读取牌库正文')
    if (!privateDeckText(target.id) || !privateDeckInteger(target.revision, 1)) throw new Error('牌库身份或修订无效')
    if (!current()) throw new Error('页面已切换，已取消牌库正文读取')
    const response = await platformRequest<unknown>(
      `/api/decks/by-id/${encodeURIComponent(target.id)}?expectedRevision=${target.revision}`, { cache: 'no-store' })
    if (!isCurrentDeckStorageContext(context) || !current()) throw new Error('账号或页面已切换，已忽略旧牌库正文')
    const saved = normalizeSavedDeck(response as SavedL12Deck)
    if (saved.id !== target.id || saved.revision !== target.revision)
      throw new Error('服务器牌库身份或修订结果不一致')
    return await withGuestDeckMutation(context, () => {
      if (!isCurrentDeckStorageContext(context) || !current()) throw new Error('账号或页面已切换，已忽略旧牌库正文')
      const previous = currentDeckSnapshot(context)
      const sameIdentity = Object.values(previous.decks).find(deck => deck.id === saved.id)
      if (sameIdentity?.revision && sameIdentity.revision > saved.revision!)
        throw new Error('本机已有更新修订，请刷新目录后重试')
      const nameCollision = Object.values(previous.decks).find(deck => deck.id !== saved.id && sameDeckName(deck.name, saved.name))
      if (nameCollision) throw new Error('本机已有另一副同名牌库，未覆盖任何缓存')
      const decks = { ...previous.decks }
      upsertCachedDeck(decks, saved)
      const activity = markDeckCacheActivity(context)
      const committed = commitSavedDecks(context, previous, decks, activity)
      const result = Object.values(committed).find(deck => deck.id === saved.id)
      if (!result || result.revision !== saved.revision) throw new Error('牌库正文缓存提交结果不一致')
      return result
    })
  } catch (error) { throw operationError(error, context) }
}

export async function uniqueDeckCopyName(base: string, current: () => boolean = () => true): Promise<string> {
  const context = captureDeckStorageContext()
  try {
    assertCompleteDeckAccount(context)
    const stem = base.trim().slice(0, 24)
    if (!stem) throw new Error('请填写牌库名称')
    const valid = () => isCurrentDeckStorageContext(context) && current()
    if (!valid()) throw new Error('页面已切换，已取消复制')
    if (!context.accountId) {
      const cached = Object.values(currentDeckSnapshot(context).decks)
      for (let number = 1; ; number++) {
        const ending = number === 1 ? '' : ` ${number}`
        const name = `${stem.slice(0, 24 - ending.length)}${ending}`
        if (!cached.some(deck => sameDeckName(deck.name, name))) return name
      }
    }
    for (let number = 1; number <= 32; number++) {
      if (!valid()) throw new Error('页面已切换，已取消复制')
      const ending = number === 1 ? '' : ` ${number}`
      const name = `${stem.slice(0, 24 - ending.length)}${ending}`
      const result = await loadPrivateDeckSummaryPage({ exactName: name, page: 1, pageSize: 1 })
      if (!valid()) throw new Error('账号或页面已切换，已取消复制')
      if (result.total > 1) throw new Error('存在多副同名牌库，请先检查我的牌库')
      if (result.total === 0) return name
    }
    throw new Error('同名牌库较多，请先调整牌库名称')
  } catch (error) { throw operationError(error, context) }
}

export async function loadPrivatePublicationSource(publicationId: string,
    current: () => boolean = () => true): Promise<SavedL12Deck | null> {
  const context = captureDeckStorageContext()
  try {
    assertCompleteDeckAccount(context)
    if (!context.accountId || !context.token) throw new Error('请先登录账号')
    const valid = () => isCurrentDeckStorageContext(context) && current()
    if (!valid()) throw new Error('页面已切换，已取消读取')
    const result = await loadPrivateDeckSummaryPage({ publicationId, page: 1, pageSize: 2 })
    if (!valid()) throw new Error('账号或页面已切换，已忽略旧结果')
    if (result.total > 1) throw new Error('多副牌库关联此公开版本，请从我的牌库选择要编辑的牌库')
    if (result.total === 0) return null
    const target = result.items[0]
    if (!target) throw new Error('牌库来源暂不可读取，请刷新后重试')
    return await loadPrivateDeckBody(target, valid)
  } catch (error) { throw operationError(error, context) }
}

export function loadDeckCatalog(): Promise<DeckCard[]> {
  if (catalogPromise) return catalogPromise
  const request = Promise.all([
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
  catalogPromise = request
  void request.catch(() => {
    if (catalogPromise === request) catalogPromise = null
  })
  return request
}

export function loadSavedDecksState() {
  return readDeckCache(localStorage, accountStorageKey(), deckCacheOwner(platformState.account?.id))
}
export function loadSavedDecks(): Record<string, SavedL12Deck> {
  return requireDeckCache(loadSavedDecksState()).decks
}

export async function syncSavedDecksFromAccount(): Promise<Record<string, SavedL12Deck>> {
  const context = captureDeckStorageContext()
  try {
    const previous = currentDeckSnapshot(context)
    if (!context.accountId || !context.token) return previous.decks
    const activity = markDeckCacheActivity(context)
    let remote: SavedL12Deck[]
    try { remote = await platformRequest<SavedL12Deck[]>('/api/decks') }
    catch { return visibleDecksAfterAsyncWork(context) }
    if (!isCurrentDeckStorageContext(context) || deckCacheActivity(context) !== activity) return visibleDecksAfterAsyncWork(context)
    const authoritative = Object.fromEntries(remote.map(deck => [deck.name, normalizeSavedDeck(deck)]))
    return await withGuestDeckMutation(context, () => {
      if (!isCurrentDeckStorageContext(context) || deckCacheActivity(context) !== activity) return visibleDecksAfterAsyncWork(context)
      try { return commitSavedDecks(context, previous, authoritative, activity) }
      catch (error) {
        if (error instanceof DeckCacheStorageError && error.kind === 'conflict') return visibleDecksAfterAsyncWork(context)
        throw error
      }
    })
  } catch (error) { throw operationError(error, context) }
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
  try {
    let decks = await syncSavedDecksFromAccount()
    if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
    if (context.accountId || context.token) return decks
    await withGuestDeckMutation(context, () => {
      const previous = currentDeckSnapshot(context)
      const current = structuredClone(previous.decks)
      let changed = previous.status === 'legacy'
      for (const deck of Object.values(current)) {
        if (deck.id && deck.revision) continue
        deck.id ||= globalThis.crypto?.randomUUID?.() || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
        deck.revision ||= 1
        changed = true
      }
      if (changed) commitSavedDecks(context, previous, current, markDeckCacheActivity(context))
    })
    decks = readSavedDecks(context.storageKey)
    const guestSeedKey = 'l12:official-presets:guest-seeded:v1'
    if (localStorage.getItem(guestSeedKey) === 'true') return decks
    if (Object.keys(decks).length > 0) { localStorage.setItem(guestSeedKey, 'true'); return decks }
    const presets = await loadOfficialPresetDecks()
    if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
    const configuredMasterIds = await getEffectiveOperationsPolicy().then(policy => new Set(policy.defaultPresetDeckIds)).catch(() => null)
    if (!isCurrentDeckStorageContext(context)) return loadSavedDecks()
    const defaults = configuredMasterIds?.size ? presets.filter(preset => configuredMasterIds.has(preset.masterId)) : presets
    return await withGuestDeckMutation(context, () => {
      const previous = currentDeckSnapshot(context)
      if (localStorage.getItem(guestSeedKey) === 'true' || Object.keys(previous.decks).length > 0) return previous.decks
      const current = Object.fromEntries(defaults.map(preset => [preset.name, { ...preset,
        id: globalThis.crypto?.randomUUID?.() || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`,
        revision: 1, cardIds: [...preset.cardIds], moraleIds: [...preset.moraleIds],
        specialIds: [...(preset.specialIds ?? [])], updatedAt: new Date().toISOString() }]))
      const result = commitSavedDecks(context, previous, current, markDeckCacheActivity(context))
      // Seeding is idempotent even if this optional marker cannot be persisted.
      try { localStorage.setItem(guestSeedKey, 'true') } catch { /* Existing decks prevent reseeding. */ }
      return result
    })
  } catch (error) { throw operationError(error, context) }
}

export async function saveDeck(deck: SavedL12Deck): Promise<SavedL12Deck> {
  const context = captureDeckStorageContext()
  let confirmed = false
  try {
    assertCompleteDeckAccount(context)
    const normalized = normalizeSavedDeck(deck)
    if (!context.accountId) return await withGuestDeckMutation(context, () => {
      const previous = currentDeckSnapshot(context)
      const entries = Object.values(previous.decks)
      const current = entries.find(value => normalized.id ? value.id === normalized.id : sameDeckName(value.name, normalized.name))
      if (normalized.id && (!current || current.revision !== normalized.revision) || !normalized.id && current?.id)
        throw new Error('牌库已被其他操作更新或删除，请重新打开；当前修改可另存为牌库')
      if (entries.some(value => sameDeckName(value.name, normalized.name)
        && (normalized.id ? value.id !== normalized.id : Boolean(value.id)))) throw new Error('已有同名牌库，请使用其他名称')
      const saved = { ...normalized, id: normalized.id || globalThis.crypto?.randomUUID?.()
        || `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`, revision: (normalized.revision ?? 0) + 1 }
      const currentDecks = { ...previous.decks }
      upsertCachedDeck(currentDecks, saved)
      commitSavedDecks(context, previous, currentDecks, markDeckCacheActivity(context))
      return saved
    })
    const previous = currentDeckSnapshot(context)
    const activity = markDeckCacheActivity(context)
    const response = await (normalized.id
      ? platformRequest<SavedL12Deck>(`/api/decks/by-id/${encodeURIComponent(normalized.id)}`, {
        method: 'PUT', body: JSON.stringify({ deck: normalized, expectedRevision: normalized.revision }),
      }) : platformRequest<SavedL12Deck>('/api/decks', { method: 'POST', body: JSON.stringify(normalized) }))
    confirmed = true
    const saved = normalizeSavedDeck(response)
    if (!saved.id || !saved.revision || normalized.id && saved.id !== normalized.id)
      throw new Error('服务器牌库身份或修订结果不一致')
    return await withGuestDeckMutation(context, () => {
      requireCurrentOperation(context, activity)
      const latest = currentDeckSnapshot(context)
      const existing = Object.values(latest.decks).find(value => value.id === saved.id)
      if (existing?.revision && existing.revision > saved.revision!) throw new Error('迟到的低修订结果不能覆盖当前牌库')
      const current = { ...previous.decks }
      upsertCachedDeck(current, saved)
      commitSavedDecks(context, previous, current, activity)
      return saved
    })
  } catch (error) { throw operationError(error, context, confirmed) }
}

export async function deleteDeck(target: string | SavedL12Deck): Promise<void> {
  const context = captureDeckStorageContext()
  let confirmed = false
  try {
    assertCompleteDeckAccount(context)
    const execute = async () => {
      const previous = currentDeckSnapshot(context)
      const name = typeof target === 'string' ? target : target.name
      const id = typeof target === 'string' ? undefined : target.id
      if (context.accountId && typeof target !== 'string' && !id) throw new Error('牌库身份尚未同步，请刷新后重试')
      const current = id ? Object.values(previous.decks).find(deck => deck.id === id) : (Object.hasOwn(previous.decks, name) ? previous.decks[name] : undefined)
      if (!current) throw new Error('牌库已变化，请刷新后重试')
      if (typeof target !== 'string' && target.revision !== current.revision) throw new Error('牌库已被其他操作更新，请刷新后重试')
      const activity = markDeckCacheActivity(context)
      if (context.accountId) {
        if (!current.id || !current.revision) throw new Error('牌库身份尚未同步，请刷新后重试')
        try { await platformRequest(`/api/decks/by-id/${encodeURIComponent(current.id)}?expectedRevision=${current.revision}`, { method: 'DELETE' }) }
        catch (error) {
          if (!error || typeof error !== 'object' || !('status' in error) || error.status !== 404) throw error
        }
        confirmed = true
      }
      const commit = () => {
        requireCurrentOperation(context, activity)
        const decks = { ...previous.decks }
        Object.keys(decks).filter(deckName => current.id ? decks[deckName]?.id === current.id : sameDeckName(deckName, name))
          .forEach(deckName => delete decks[deckName])
        commitSavedDecks(context, previous, decks, activity)
      }
      if (context.accountId) await withGuestDeckMutation(context, commit)
      else commit()
    }
    if (context.accountId) await execute()
    else await withGuestDeckMutation(context, execute)
  } catch (error) { throw operationError(error, context, confirmed) }
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
