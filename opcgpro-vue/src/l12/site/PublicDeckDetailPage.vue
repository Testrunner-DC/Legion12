<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { createDeckImageBlob, deckImageGroups, downloadDeckImage, encodeDeckCode } from './deckShare'
import { automaticExtraCardIdsForMaster, deckCountSummary, loadDeckCatalog, loadOfficialPresetDecks, uniqueDeckCopyName, loadPrivatePublicationSource, captureDeckAccountGuard, deckErrorBelongsToCurrentAccount, normalOpeningHandCopies, saveDeck, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { platformState, publicDeckApi, publicDeckReadApi, type PublicDeckGuide, type PublicDeckStatisticsPage, type PublicDeckVersionChange, type PublicDeckVersionPage, type PublicDeckVersionRead } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import CardDetailContent from '@/l12/CardDetailContent.vue'
import CardImage from '@/l12/CardImage.vue'
import DeckConstructionBrowser, { type ConstructionEntry } from './DeckConstructionBrowser.vue'
import StatisticsScope from './StatisticsScope.vue'
import { samplePublicDeckOpeningHand } from './publicDeckHands'
import { capturePublicDeckCounterGuard, isOfficialPublicDeckCounterTarget, mergePublicDeckCounters } from './publicDeckEntry'
import { useActionGate } from '@/l12/useActionGate'
import { deckEditorQuery } from './deckEditorNavigation'
import { resolveOfficialDeck } from './officialDeckReference'
import { consumeSummaryOpen } from './publicDeckSummary'
import { settlePublicDeckRead, toPublicDeckCurrentEntry,
  validatePublicDeckCurrent, validatePublicDeckStatistics, validatePublicDeckVersion, validatePublicDeckVersionPage,
  type OfficialDeckDetailEntry, type PublicDeckDetailEntry, type PublicDeckPresentationEntry } from './publicDeckRead'

const route = useRoute()
const router = useRouter()
const { pending: actionBusy, isPending: actionPending, run: runAction } = useActionGate()
const publicDeckActionKey = (deckId: string, accountId = platformState.account?.id ?? 'anonymous') =>
  `public-deck:${accountId}:${deckId}`
const catalog = ref<DeckCard[]>([])
const entry = ref<PublicDeckPresentationEntry | null>(null)
let counterDocumentEpoch = 0
watch(() => route.fullPath, () => counterDocumentEpoch++, { flush: 'sync' })
onBeforeUnmount(() => counterDocumentEpoch++)
function captureCounterContext(value: PublicDeckPresentationEntry, key: string) {
  return capturePublicDeckCounterGuard(value, {
    actorCurrent: captureDeckAccountGuard(),
    document: () => `${counterDocumentEpoch}:${route.fullPath}`,
    entry: () => entry.value,
    actionCurrent: () => actionPending(key),
  })
}
const notice = ref('')
const loading = ref(true)
const imagePreview = ref<{ blob: Blob; url: string } | null>(null)
const openingHandIds = ref<string[]>([])
const selectedCard = ref<DeckCard | null>(null)
const mobileDetailsOpen = ref(false)
const versionsState = ref<'idle' | 'loading' | 'available' | 'unavailable'>('idle')
const versionsNotice = ref('')
const versionsPage = ref<PublicDeckVersionPage | null>(null)
const selectedVersionState = ref<'idle' | 'loading' | 'available' | 'unavailable'>('idle')
const selectedVersionNotice = ref('')
const selectedVersion = ref<PublicDeckVersionRead | null>(null)
const statisticsState = ref<'idle' | 'loading' | 'available' | 'unavailable'>('idle')
const statisticsNotice = ref('')
const statisticsPage = ref<PublicDeckStatisticsPage | null>(null)
const refreshRequired = ref(false)
let versionsRequestSequence = 0
let selectedVersionRequestSequence = 0
let statisticsRequestSequence = 0
let detailLoadGeneration = 0
let detailMounted = false
let detailDisposed = false
interface DetailLoadContext {
  generation: number
  reference: string
  path: string
  documentEpoch: number
  actorCurrent: () => boolean
  expectedReadToken?: string
}
let detailCanonicalNavigation: { context: DetailLoadContext; targetPath: string; targetReference: string } | null = null
function selectCard(card: DeckCard) {
  selectedCard.value = card
  mobileDetailsOpen.value = window.matchMedia('(max-width:1000px)').matches
}
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const master = computed(() => entry.value ? byId.value.get(entry.value.deck.masterId) : undefined)
const backTo = computed(() => typeof route.query.from === 'string' && route.query.from.startsWith('/decks') ? route.query.from : '/decks?tab=plaza')
const entries = computed<ConstructionEntry[]>(() => {
  if (!entry.value) return []
  const rows: ConstructionEntry[] = []
  const add = (ids: string[], section: string) => {
    const totals = ids.reduce((map, id) => map.set(id, (map.get(id) || 0) + 1), new Map<string, number>())
    totals.forEach((quantity, cardId) => rows.push({ cardId, quantity, section }))
  }
  add(entry.value.deck.cardIds, 'main')
  add(entry.value.deck.moraleIds, 'morale')
  add(entry.value.deck.specialIds ?? [], 'special')
  add(automaticExtraCardIdsForMaster(entry.value.deck.masterId), 'automatic')
  return rows
})
const curve = computed(() => {
  const values = Array(9).fill(0) as number[]
  entries.value.filter(row => row.section === 'main').forEach(row => values[Math.min(8, byId.value.get(row.cardId)?.cost ?? 0)] += row.quantity)
  return values
})
const curveMax = computed(() => Math.max(1, ...curve.value))
const guide = computed(() => entry.value?.guide ?? emptyGuide())
const matchups = computed(() => entry.value?.matchups ?? [])
const hasGuide = computed(() => Object.values(guide.value).some(value => value.trim()))
const hasMatchups = computed(() => matchups.value.length > 0)
const matchStatisticsRange = computed(() => {
  const statistics = statisticsPage.value
  if (!statistics) return ''
  const from = new Date(statistics.from)
  const to = new Date(statistics.to)
  if (!Number.isFinite(from.valueOf()) || !Number.isFinite(to.valueOf()) || from.getUTCFullYear() < 2000)
    return `统计最近 ${statistics.recentDays} 天`
  const format = (value: Date) => value.toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })
  return `最近 ${statistics.recentDays} 天（${format(from)} 至 ${format(to)}，UTC+8）`
})
const matchStatisticsSample = computed(() => {
  const statistics = statisticsPage.value
  if (!statistics) return ''
  if (statistics.sampleStatus === 'available') return `可展示 ${statistics.games} 场；每次分页均标注该次读取的实际统计时间窗。`
  if (statistics.sampleStatus === 'insufficient') return `样本不足：最近 ${statistics.recentDays} 天没有达到 3 场门槛的公开分组。`
  return `最近 ${statistics.recentDays} 天暂无可核验的公开版本对局统计。`
})
const matchStatisticsItems = computed(() => [
  '只计入已结束、无错误、非沙盒，且开局时已绑定到这一公开牌库不可变版本的对局；不会用作者总战绩或后来更新的构筑替代。',
  '按公开版本、使用主宰和对方主宰分组；每组至少 3 场才公开，低于门槛的组不会返回场次、胜负或胜率。',
  '作废／暂扣对局及禁用／删除账号参与的对局不计；这里未按先后手、运营规则版本或卡效版本拆分。',
])
const sectionTabs = computed(() => [
  { id: 'construction', label: '构筑', visible: true },
  { id: 'guide', label: '指南', visible: hasGuide.value },
  { id: 'matchups', label: '对局建议', visible: hasMatchups.value },
  { id: 'versions', label: '版本', visible: entry.value?.source === 'public' },
  { id: 'matches', label: '对局', visible: entry.value?.source === 'public' },
  { id: 'hands', label: '起手', visible: true },
].filter(item => item.visible))
const deckCopies = computed(() => entry.value ? deckImageGroups(entry.value.deck, catalog.value)
  .flatMap(group => Array.from({ length: group.count }, (_, index) => ({
    ...group,
    key: `${group.cardId}:${group.artId || 'original'}:${index}`,
    card: byId.value.get(group.cardId),
  }))).filter(copy => Boolean(copy.card)) : [])
const eligibleDeckCopies = computed(() => normalOpeningHandCopies(deckCopies.value, copy => copy.card))
const openingHand = computed(() => {
  const copies = new Map(deckCopies.value.map(copy => [copy.key, copy]))
  return openingHandIds.value.flatMap(key => {
    const copy = copies.get(key)
    return copy ? [copy] : []
  })
})

function publicDeckReference(value: PublicDeckPresentationEntry) {
  if (value.source === 'official') return value.id
  if (!value.publicCode) throw new Error('公开牌库规范地址无效')
  return value.publicCode
}

function invalidateReadSections() {
  versionsRequestSequence++; selectedVersionRequestSequence++; statisticsRequestSequence++
  versionsState.value = 'idle'; versionsNotice.value = ''; versionsPage.value = null
  selectedVersionState.value = 'idle'; selectedVersionNotice.value = ''; selectedVersion.value = null
  statisticsState.value = 'idle'; statisticsNotice.value = ''; statisticsPage.value = null
}

function singleRouteQuery(value: unknown) { return typeof value === 'string' ? value : undefined }

function captureInitialReadContext() {
  const generation = ++detailLoadGeneration, reference = String(route.params.deckId || '')
  const hasExpectedReadToken = Object.hasOwn(route.query, 'expectedReadToken')
  const hasTicket = Object.hasOwn(route.query, 'summaryOpen')
  const expectedReadToken = singleRouteQuery(route.query.expectedReadToken)
  const ticket = singleRouteQuery(route.query.summaryOpen)
  const accountCurrent = captureDeckAccountGuard()
  let handoffCurrent = () => true
  if (hasExpectedReadToken || hasTicket) {
    if (!expectedReadToken || !/^[a-f0-9]{64}$/.test(expectedReadToken) || !ticket)
      return { error: '牌库目录链接已失效，请刷新后重新读取。' } as const
    const handoff = consumeSummaryOpen(ticket, reference, expectedReadToken)
    if (!handoff) return { error: '牌库目录链接已过期或账号已变化，请刷新后重新读取。' } as const
    handoffCurrent = handoff.actorCurrent
  }
  const context: DetailLoadContext = { generation, reference, path: route.fullPath,
    documentEpoch: counterDocumentEpoch, expectedReadToken,
    actorCurrent: () => accountCurrent() && handoffCurrent() }
  return { context } as const
}

function officialDetailEntry(id: string, preset: SavedL12Deck): OfficialDeckDetailEntry {
  return { id, publicCode: null, source: 'official', official: true, name: preset.name, author: '十二军团官方预组',
    deck: preset, views: 0, likes: 0, copies: 0, viewerLiked: false, liked: false, canEdit: false,
    createdAt: '', updatedAt: '', seasonCompliant: true, seasonComplianceReason: null, version: 1,
    guide: emptyGuide(), matchups: [], contentRevision: 0, contentUpdatedAt: null, readToken: null }
}

async function loadDetail() {
  const captured = captureInitialReadContext()
  invalidateReadSections()
  detailCanonicalNavigation = null
  entry.value = null
  selectedCard.value = null
  openingHandIds.value = []
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  imagePreview.value = null
  notice.value = ''
  refreshRequired.value = false
  loading.value = true
  if ('error' in captured) {
    notice.value = captured.error ?? '牌库目录链接无法确认，请刷新后重新读取。'
    refreshRequired.value = true
    loading.value = false
    return
  }
  const context = captured.context
  const current = () => detailMounted && !detailDisposed && context.generation === detailLoadGeneration
    && context.actorCurrent() && context.path === route.fullPath && context.reference === String(route.params.deckId || '')
    && context.documentEpoch === counterDocumentEpoch
  if (!current() || route.name !== 'public-deck-detail' || !context.reference) return
  try {
    const cards = await loadDeckCatalog()
    if (!current()) return
    catalog.value = cards
    const id = context.reference
    let loaded: PublicDeckPresentationEntry
    if (id.startsWith('official-') || id.startsWith('official:')) {
      const preset = await resolveOfficialDeck(id, loadOfficialPresetDecks)
      if (!current()) return
      loaded = officialDetailEntry(id, { ...preset, specialIds: preset.specialIds ?? [], updatedAt: '' })
    } else {
      const result = await settlePublicDeckRead(publicDeckReadApi.current(id, context.expectedReadToken), validatePublicDeckCurrent)
      if (!current()) return
      if (result.status === 'refresh-required') {
        refreshRequired.value = true; notice.value = result.message; return
      }
      if (result.status === 'unavailable') throw new Error(result.message)
      loaded = toPublicDeckCurrentEntry(result.value)
      const canonicalReference = loaded.publicCode
      if (canonicalReference !== id) {
        const target = { name: 'public-deck-detail', params: { deckId: canonicalReference }, query: route.query, hash: route.hash }
        detailCanonicalNavigation = { context, targetPath: router.resolve(target).fullPath, targetReference: canonicalReference }
        const failure = await router.replace(target)
        if (detailCanonicalNavigation?.context === context) detailCanonicalNavigation = null
        if (!current()) return
        if (failure) throw new Error('牌库规范地址跳转未完成，请重新打开')
      }
    }
    if (!current()) return
    entry.value = loaded
    selectedCard.value = master.value ?? byId.value.get(loaded.deck.cardIds[0] || '') ?? null
    redrawOpeningHand()
    if (loaded.source === 'public') {
      void loadVersionPage(1)
      void loadStatisticsPage(1)
      void recordInitialView(entry.value!).catch(() => undefined)
    }
  } catch (error) {
    if (current()) notice.value = error instanceof Error ? error.message : '公开牌库加载失败'
  } finally {
    if (detailCanonicalNavigation?.context === context) detailCanonicalNavigation = null
    if (current()) loading.value = false
  }
}

watch(() => [route.fullPath, platformState.account?.id, platformState.token], (next, previous) => {
  if (!detailMounted || detailDisposed || previous && next.every((value, index) => value === previous[index])) return
  const canonical = detailCanonicalNavigation
  if (canonical && canonical.context.generation === detailLoadGeneration && canonical.context.actorCurrent()
    && previous && previous[0] === canonical.context.path && next[0] === canonical.targetPath
    && next[1] === previous[1] && next[2] === previous[2] && route.name === 'public-deck-detail'
    && String(route.params.deckId || '') === canonical.targetReference) {
    // Only the synchronous arrival at this load's exact target can extend its
    // context. An external navigation invalidates it before replace resolves.
    canonical.context.path = route.fullPath
    canonical.context.reference = canonical.targetReference
    canonical.context.documentEpoch = counterDocumentEpoch
    return
  }
  void loadDetail()
}, { flush: 'sync' })
onMounted(() => { detailMounted = true; void loadDetail() })
onBeforeUnmount(() => {
  detailDisposed = true
  detailMounted = false
  detailLoadGeneration++
  detailCanonicalNavigation = null
  invalidateReadSections()
})

interface PinnedReadContext {
  generation: PublicDeckDetailEntry
  canEdit: boolean
  current: () => boolean
}
function capturePinnedReadContext(): PinnedReadContext | null {
  const value = entry.value
  if (!value || value.source !== 'public') return null
  const actorCurrent = captureDeckAccountGuard(), path = route.fullPath, documentEpoch = counterDocumentEpoch
  const loadGeneration = detailLoadGeneration, body = value.deck, canEdit = value.canEdit
  return { generation: value, canEdit, current: () => detailMounted && !detailDisposed
    && actorCurrent() && route.name === 'public-deck-detail' && route.fullPath === path
    && counterDocumentEpoch === documentEpoch && detailLoadGeneration === loadGeneration
    && entry.value?.source === 'public' && entry.value.id === value.id && entry.value.publicCode === value.publicCode
    && entry.value.readToken === value.readToken && entry.value.catalogVersion === value.catalogVersion
    && entry.value.policyVersion === value.policyVersion && entry.value.deck === body && entry.value.canEdit === canEdit }
}

function requireReadRefresh(message: string) {
  refreshRequired.value = true
  notice.value = message
}

async function loadVersionPage(page: number) {
  const context = capturePinnedReadContext()
  if (!context) return
  const request = ++versionsRequestSequence, pageSize = 30
  versionsState.value = 'loading'; versionsNotice.value = ''; versionsPage.value = null
  selectedVersionRequestSequence++; selectedVersionState.value = 'idle'; selectedVersionNotice.value = ''; selectedVersion.value = null
  const result = await settlePublicDeckRead(
    publicDeckReadApi.versions(context.generation.publicCode, page, pageSize, context.generation.readToken),
    value => validatePublicDeckVersionPage(value, context.generation, page, pageSize, context.canEdit))
  if (!context.current() || request !== versionsRequestSequence) return
  if (result.status === 'available') {
    versionsPage.value = result.value; versionsState.value = 'available'; return
  }
  versionsState.value = 'unavailable'; versionsNotice.value = result.message
  if (result.status === 'refresh-required') requireReadRefresh(result.message)
}

async function loadSelectedVersion(version: number) {
  const context = capturePinnedReadContext()
  if (!context) return
  const request = ++selectedVersionRequestSequence
  selectedVersionState.value = 'loading'; selectedVersionNotice.value = ''; selectedVersion.value = null
  const result = await settlePublicDeckRead(
    publicDeckReadApi.version(context.generation.publicCode, version, context.generation.readToken),
    value => validatePublicDeckVersion(value, context.generation, version, context.canEdit))
  if (!context.current() || request !== selectedVersionRequestSequence) return
  if (result.status === 'available') {
    selectedVersion.value = result.value; selectedVersionState.value = 'available'; return
  }
  selectedVersionState.value = 'unavailable'; selectedVersionNotice.value = result.message
  if (result.status === 'refresh-required') requireReadRefresh(result.message)
}

async function loadStatisticsPage(page: number) {
  const context = capturePinnedReadContext()
  if (!context) return
  const request = ++statisticsRequestSequence, pageSize = 30
  statisticsState.value = 'loading'; statisticsNotice.value = ''; statisticsPage.value = null
  const result = await settlePublicDeckRead(
    publicDeckReadApi.statistics(context.generation.publicCode, page, pageSize, context.generation.readToken),
    value => validatePublicDeckStatistics(value, context.generation, page, pageSize))
  if (!context.current() || request !== statisticsRequestSequence) return
  if (result.status === 'available') {
    statisticsPage.value = result.value; statisticsState.value = 'available'; return
  }
  statisticsState.value = 'unavailable'; statisticsNotice.value = result.message
  if (result.status === 'refresh-required') requireReadRefresh(result.message)
}

const versionPageCount = computed(() => Math.max(1, Math.ceil((versionsPage.value?.total ?? 0) / (versionsPage.value?.pageSize ?? 30))))
const statisticsPageCount = computed(() => Math.max(1, Math.ceil((statisticsPage.value?.total ?? 0) / (statisticsPage.value?.pageSize ?? 30))))

async function refreshCurrentDeck() {
  const before = route.fullPath, query = { ...route.query }
  delete query.expectedReadToken; delete query.summaryOpen
  const target = { name: 'public-deck-detail', params: { deckId: String(route.params.deckId || '') }, query, hash: route.hash }
  if (router.resolve(target).fullPath === before) { void loadDetail(); return }
  const failure = await router.replace(target)
  if (failure) { notice.value = '刷新地址未完成，请重新打开牌库。'; return }
}

async function copySelectedVersionCode() {
  const context = capturePinnedReadContext(), selected = selectedVersion.value
  if (!context || !selected) return
  await navigator.clipboard.writeText(encodeDeckCode(selected.deck))
  if (context.current() && selectedVersion.value === selected)
    notice.value = `版本 ${selected.metadata.version} 的牌库码已复制`
}

async function recordInitialView(value: PublicDeckPresentationEntry) {
  if (isOfficialPublicDeckCounterTarget(value)) return
  const viewedKey = `l12:public-deck-viewed:${value.id}`
  if (sessionStorage.getItem(viewedKey)) return
  const key = publicDeckActionKey(value.id), current = captureCounterContext(value, key)
  const marker = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}:${counterDocumentEpoch}`
  await runAction(key, async () => {
    if (!current() || sessionStorage.getItem(viewedKey)) return
    sessionStorage.setItem(viewedKey, marker)
    try {
      const counters = await publicDeckApi.counter(publicDeckReference(value), 'view')
      if (current()) entry.value = mergePublicDeckCounters(entry.value, counters)
    } catch {
      if (sessionStorage.getItem(viewedKey) === marker) sessionStorage.removeItem(viewedKey)
    }
  })
}

async function uniqueName(base: string, current: () => boolean = () => true) {
  return uniqueDeckCopyName(base, current)
}
async function copyToMine() {
  if (!entry.value) return
  const id = entry.value.id
  const currentAccount = captureDeckAccountGuard()
  const accountId = platformState.account?.id
  const key = publicDeckActionKey(id, accountId), current = captureCounterContext(entry.value, key)
  await runAction(key, async () => {
    try {
      if (!current() || !entry.value) return
      const source = entry.value.deck
      const name = await uniqueName(source.name, current)
      if (!current()) return
      const deck = { ...source, id: undefined, revision: undefined, name, publicationId: null, publicationVersion: null, cardIds: [...source.cardIds], moraleIds: [...source.moraleIds], specialIds: [...(source.specialIds ?? [])], updatedAt: new Date().toISOString() }
      const saved = await saveDeck(deck)
      if (current())
        notice.value = `已复制《${saved.name}》到我的牌库`
      if (!current()) return
      if (!isOfficialPublicDeckCounterTarget(entry.value!)) {
        try {
          const counters = await publicDeckApi.counter(publicDeckReference(entry.value!), 'copy')
          if (current()) entry.value = mergePublicDeckCounters(entry.value, counters)
        } catch { /* 本地复制已经成功；远端统计失败不得反写为复制失败。 */ }
      }
    } catch (error) {
      if (current() && currentAccount() && deckErrorBelongsToCurrentAccount(error))
        notice.value = error instanceof Error ? error.message : '复制到我的牌库失败'
    }
  })
}
async function toggleLike() {
  if (!entry.value || isOfficialPublicDeckCounterTarget(entry.value)) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  const id = entry.value.id
  const reference = publicDeckReference(entry.value)
  const accountId = platformState.account.id
  const key = publicDeckActionKey(id, accountId), current = captureCounterContext(entry.value, key)
  await runAction(key, async () => {
    if (!current()) return
    try {
      const counters = await publicDeckApi.counter(reference, 'like')
      if (current()) entry.value = mergePublicDeckCounters(entry.value, counters)
    } catch (error) {
      if (current())
        notice.value = error instanceof Error ? error.message : '点赞失败'
    }
  })
}
async function copyCode() {
  if (!entry.value) return
  await navigator.clipboard.writeText(encodeDeckCode(entry.value.deck)); notice.value = '牌库码已复制'
}
async function previewImage() {
  if (!entry.value) return
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  const blob = await createDeckImageBlob(entry.value.deck, catalog.value, { publicUrl: publicDeckUrl() })
  imagePreview.value = { blob, url: URL.createObjectURL(blob) }
}
async function editDeck() {
  const opened = entry.value
  if (!opened?.canEdit) return
  const account = platformState.account?.id, token = platformState.token, id = opened.id
  const context = capturePinnedReadContext()
  if (!context) return
  const current = context.current
  await runAction(publicDeckActionKey(id, account), async () => {
  try {
    const source = opened.deck
    const existing = await loadPrivatePublicationSource(id, current)
    if (!current()) return
    const name = existing?.name ?? await uniqueName(source.name, current)
    if (!current()) return
    const saved = await saveDeck({ ...source, id: existing?.id, revision: existing?.revision,
      name, cardIds: [...source.cardIds], moraleIds: [...source.moraleIds], specialIds: [...(source.specialIds ?? [])] })
    if (!current() || account !== platformState.account?.id || token !== platformState.token || entry.value?.id !== id) return
    await router.push({ path: '/deck-editor', query: deckEditorQuery(route.fullPath, saved.name, entry.value.id, saved.id) })
  } catch (error) {
    if (current() && account === platformState.account?.id && token === platformState.token && deckErrorBelongsToCurrentAccount(error))
      notice.value = error instanceof Error ? error.message : '读取或保存牌库失败'
  }
  })
}
async function deleteDeck() {
  if (!entry.value?.canEdit || !window.confirm('确定删除这个公开牌库？')) return
  const id = entry.value.id
  const reference = publicDeckReference(entry.value)
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(id, accountId), async () => {
    try {
      await publicDeckApi.delete(reference)
      if (accountId === platformState.account?.id) await router.replace(backTo.value)
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '删除公开牌库失败'
    }
  })
}
function emptyGuide(): PublicDeckGuide {
  return { buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' }
}
function scrollToSection(section: string) {
  document.getElementById(`public-deck-${section}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
function publicDeckUrl() {
  if (!entry.value || entry.value.official || typeof window === 'undefined') return ''
  return new URL(router.resolve({ name: 'public-deck-detail', params: { deckId: publicDeckReference(entry.value) } }).href, window.location.origin).href
}
function redrawOpeningHand() {
  if (!entry.value) return
  openingHandIds.value = samplePublicDeckOpeningHand(eligibleDeckCopies.value.map(copy => copy.key))
}
function cardName(cardId: string) { return byId.value.get(cardId)?.nameZh || cardId }
function masterName(masterId: string) { return byId.value.get(masterId)?.nameZh || masterId }
function changeLabel(change: PublicDeckVersionChange) {
  const section = { master: '主宰', main: '主牌', morale: '士气', special: '试炼/额外' }[change.section]
  if (change.section === 'master') return `${section}改为 ${cardName(change.cardId)}`
  if (change.previousQuantity === 0) return `${section}新增 ${cardName(change.cardId)} ×${change.currentQuantity}`
  if (change.currentQuantity === 0) return `${section}移除 ${cardName(change.cardId)} ×${change.previousQuantity}`
  return `${section}调整 ${cardName(change.cardId)}：${change.previousQuantity} → ${change.currentQuantity}`
}
function formatTime(value?: string) {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}
function formatRate(value: number) { return `${(value * 100).toFixed(1)}%` }
</script>

<template>
  <main class="public-deck-detail" data-ui-contract="public-deck-detail-page" :aria-busy="actionBusy">
    <router-link class="back-link" :to="backTo">← 返回公开牌库</router-link>
    <p v-if="loading" class="state">正在载入构筑……</p>
    <div v-else-if="!entry" class="state error"><p>{{ notice || '未找到这个公开牌库' }}</p><button v-if="refreshRequired" @click="refreshCurrentDeck">刷新并重新读取</button></div>
    <template v-else>
      <header class="detail-head">
        <DeckProfile :master-id="entry.deck.masterId" :master-name="master?.nameZh" :fallback-url="master?.imageUrl" :name="entry.deck.name" :context="entry.author" :meta="`${deckCountSummary(entry.deck.cardIds, byId).label} 张主牌 · ${entry.deck.moraleIds.length} 张士气`"/>
        <div class="metrics"><span>浏览 {{ entry.views ?? 0 }}</span><span>点赞 {{ entry.likes }}</span><span>复制 {{ entry.copies }}</span><span>{{ entry.seasonCompliant === false ? '不符合本赛季' : '符合本赛季' }}</span></div>
      </header>
      <div class="detail-toolbar">
        <nav class="detail-tabs" aria-label="公开牌库详情内容">
          <button v-for="tab in sectionTabs" :key="tab.id" @click="scrollToSection(tab.id)">{{ tab.label }}</button>
        </nav>
        <div class="actions"><button v-if="!entry.official" :disabled="!platformState.account || actionPending(publicDeckActionKey(entry.id))" @click="toggleLike">♡ {{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : entry.liked ? '取消点赞' : '点赞' }}</button><button @click="copyCode">复制牌库码</button><button @click="previewImage">生成牌库图</button><button v-if="entry.canEdit" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="editDeck">编辑牌库</button><button v-if="entry.canEdit" class="danger" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="deleteDeck">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '删除公开牌库' }}</button><button class="primary" :disabled="actionPending(publicDeckActionKey(entry.id))" @click="copyToMine">{{ actionPending(publicDeckActionKey(entry.id)) ? '处理中…' : '复制到我的牌库' }}</button></div>
      </div>
      <section id="public-deck-construction" class="deck-layout detail-anchor-section">
        <aside>
          <section><b>费用曲线</b><div class="curve"><i v-for="(value,index) in curve" :key="index"><span :style="{ height: `${Math.max(4, value / curveMax * 72)}px` }"></span><small>{{ index === 8 ? '8+' : index }}</small><em>{{ value }}</em></i></div></section>
          <section><b>构筑摘要</b><p v-if="entry.deck.cardIds.length">主牌<strong>{{ entry.deck.cardIds.length }}</strong></p><p v-if="entry.deck.moraleIds.length">士气<strong>{{ entry.deck.moraleIds.length }}</strong></p><p v-if="entry.deck.specialIds?.length">额外<strong>{{ entry.deck.specialIds.length }}</strong></p><p v-if="automaticExtraCardIdsForMaster(entry.deck.masterId).length">自动额外<strong>{{ automaticExtraCardIdsForMaster(entry.deck.masterId).length }}</strong></p></section>
          <section id="public-deck-construction-filters" aria-label="构筑筛选"></section>
        </aside>
        <div class="public-deck-main"><DeckConstructionBrowser :entries="entries" :catalog="catalog" :master-faction="master?.faction" :title="`${entry.deck.name} · 全部构筑`" filter-target="#public-deck-construction-filters" hide-header external-details @select="selectCard"/>

      <section v-if="hasGuide" id="public-deck-guide" class="content-panel detail-anchor-section" data-detail-section="guide">
        <header><div><h2>牌库指南</h2><p v-if="entry.contentUpdatedAt">作者更新于 {{ formatTime(entry.contentUpdatedAt) }} · 修订 {{ entry.contentRevision }}</p></div></header>
        <div class="reading-sections">
          <article v-for="item in [['构筑思路',guide.buildIdea],['起手建议',guide.opening],['关键牌与配合',guide.keyCards],['常见展开',guide.commonSequence],['替换建议',guide.substitutions]].filter(item => item[1])" :key="item[0]"><h3>{{ item[0] }}</h3><p>{{ item[1] }}</p></article>
        </div>
      </section>
      <section v-if="hasMatchups" id="public-deck-matchups" class="content-panel detail-anchor-section" data-detail-section="matchups">
        <header><div><h2>对局建议</h2><p>按对方主宰查看作者提供的思路、关键牌与换牌建议。</p></div></header>
        <div class="matchup-list"><article v-for="row in matchups" :key="row.opponentMasterId"><header class="matchup-city"><DeckProfile compact :master-id="row.opponentMasterId" :master-name="masterName(row.opponentMasterId)" :name="`对阵 ${masterName(row.opponentMasterId)}`"/></header><p v-if="row.notes"><b>思路</b>{{ row.notes }}</p><p v-if="row.keyCards"><b>关键牌</b>{{ row.keyCards }}</p><p v-if="row.suggestedSwaps"><b>换牌</b>{{ row.suggestedSwaps }}</p></article></div>
      </section>
      <section v-if="entry.source === 'public'" id="public-deck-versions" class="content-panel detail-anchor-section" data-detail-section="versions">
        <header><div><h2>全部公开版本</h2><p v-if="versionsPage">共 {{ versionsPage.total }} 个版本 · 第 {{ versionsPage.page }} / {{ versionPageCount }} 页</p></div></header>
        <p v-if="versionsState === 'loading'" class="empty-copy">正在读取版本目录……</p>
        <p v-else-if="versionsState === 'unavailable'" class="empty-copy">{{ versionsNotice }}</p>
        <div v-else-if="versionsState === 'available' && versionsPage?.items.length" class="version-list"><details v-for="version in versionsPage.items" :key="version.version" :open="version.version === versionsPage.items[0]?.version"><summary><b>版本 {{ version.version }}</b><span>{{ version.name }}</span><time>{{ formatTime(version.createdAt) }}</time></summary><p v-if="version.version === 1">首次发布</p><ul v-else-if="version.changes.length"><li v-for="change in version.changes" :key="`${change.section}-${change.cardId}`">{{ changeLabel(change) }}</li></ul><p v-else>这个版本没有可展示的数量变化。</p><button :disabled="selectedVersionState === 'loading'" @click="loadSelectedVersion(version.version)">查看版本 {{ version.version }} 构筑</button></details></div>
        <p v-else-if="versionsState === 'available'" class="empty-copy">这个牌库没有公开版本记录。</p>
        <div v-if="versionsState === 'available' && versionPageCount > 1" class="editor-actions"><button :disabled="(versionsPage?.page ?? 1) <= 1" @click="loadVersionPage((versionsPage?.page ?? 1) - 1)">上一页</button><button :disabled="(versionsPage?.page ?? 1) >= versionPageCount" @click="loadVersionPage((versionsPage?.page ?? 1) + 1)">下一页</button></div>
        <p v-if="selectedVersionState === 'loading'" class="empty-copy">正在读取所选版本构筑……</p>
        <p v-else-if="selectedVersionState === 'unavailable'" class="empty-copy">{{ selectedVersionNotice }}</p>
        <article v-else-if="selectedVersionState === 'available' && selectedVersion" class="matchup-editor"><h3>版本 {{ selectedVersion.metadata.version }} · {{ selectedVersion.metadata.name }}</h3><p>{{ masterName(selectedVersion.deck.masterId) }} · {{ selectedVersion.deck.cardIds.length }} 张主牌 · {{ selectedVersion.deck.moraleIds.length }} 张士气 · {{ selectedVersion.deck.specialIds.length }} 张额外牌</p><div class="editor-actions"><button @click="copySelectedVersionCode">复制该版本牌库码</button></div></article>
      </section>
      <section v-if="entry.source === 'public'" id="public-deck-matches" class="content-panel detail-anchor-section" data-detail-section="matches">
        <header><div><h2>版本对局</h2><p>按开局时绑定的不可变公开版本统计。</p></div></header>
        <p v-if="statisticsState === 'loading'" class="empty-copy">正在读取匿名统计……</p>
        <p v-else-if="statisticsState === 'unavailable'" class="empty-copy">{{ statisticsNotice }}</p>
        <template v-else-if="statisticsState === 'available' && statisticsPage"><StatisticsScope :summary="matchStatisticsRange" :sample="matchStatisticsSample" :items="matchStatisticsItems"/><div v-if="statisticsPage.groups.length" class="match-stat-list"><article v-for="stat in statisticsPage.groups" :key="`${stat.version}-${stat.masterId}-${stat.opponentMasterId}`"><header><b>版本 {{ stat.version }}</b><span>{{ masterName(stat.masterId) }} 对阵 {{ masterName(stat.opponentMasterId) }}</span></header><dl><div><dt>场次</dt><dd>{{ stat.games }}</dd></div><div><dt>胜率</dt><dd>{{ formatRate(stat.winRate) }}</dd></div><div><dt>胜 / 负 / 平</dt><dd>{{ stat.wins }} / {{ stat.losses }} / {{ stat.draws }}</dd></div></dl></article></div><p v-else class="empty-copy">{{ matchStatisticsSample }}</p><div v-if="statisticsPageCount > 1" class="editor-actions"><button :disabled="statisticsPage.page <= 1" @click="loadStatisticsPage(statisticsPage.page - 1)">上一页</button><button :disabled="statisticsPage.page >= statisticsPageCount" @click="loadStatisticsPage(statisticsPage.page + 1)">下一页</button></div></template>
      </section>
      <section id="public-deck-hands" class="content-panel detail-anchor-section" data-detail-section="hands">
        <header><div><h2>随机起手</h2><p>随机展示当前构筑中的 6 张主牌。</p></div><button @click="redrawOpeningHand">重新抽取</button></header>
        <div class="opening-hand"><article v-for="copy in openingHand" :key="copy.key"><button class="hand-card" :aria-label="`查看${copy.card!.nameZh}详情，${copy.label}`" @click="selectCard(copy.card!)"><CardImage :card-id="copy.cardImageId" :legacy-url="copy.legacyUrl" :alt="`${copy.card!.nameZh} · ${copy.label}`" intent="thumb" fit="contain"/><b>{{ copy.card!.nameZh }}</b><small>{{ copy.label }}</small></button></article></div>
      </section>
        </div>
        <aside class="archive-detail public-card-detail" aria-label="卡牌详情"><CardDetailContent v-if="selectedCard" :card="selectedCard" :show-catalog-only="false"/></aside>
      </section>
</template>
    <div v-if="notice && entry" class="notice"><span>{{ notice }}</span><button v-if="refreshRequired" @click="refreshCurrentDeck">刷新并重新读取</button></div>
    <div v-if="imagePreview && entry" class="preview-mask" @click.self="imagePreview = null"><section><button class="close" @click="imagePreview = null">×</button><img :src="imagePreview.url" alt="牌库图预览"/><footer><button class="primary" @click="downloadDeckImage(entry.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
  </main>
  <CatalogCardDetails v-if="mobileDetailsOpen && selectedCard" :card="selectedCard" :show-catalog-only="false" @close="mobileDetailsOpen = false"/>
</template>

<style scoped>
.deck-layout{grid-template-columns:180px minmax(0,1fr) var(--l12-card-detail-sidebar-width,274px)!important;align-items:start}.public-deck-main{min-width:0}.public-card-detail{position:sticky;top:14px;min-width:0;max-height:calc(100dvh - 84px);overflow:auto}.opening-hand .hand-card{width:100%;min-width:0;padding:0;white-space:normal;border:0;background:transparent}.matchup-city :deep(.deck-profile){width:100%;border:0;background:transparent}@media(max-width:1000px){.deck-layout{grid-template-columns:1fr!important}.public-card-detail{display:none!important}.deck-layout>aside:first-child{grid-template-columns:1fr 1fr}}
.public-deck-detail{box-sizing:border-box;min-height:100%;padding:24px clamp(14px,3vw,48px) 56px;color:#eee;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.back-link{display:inline-block;margin-bottom:14px;color:#80d8dc;text-decoration:none}.detail-head{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:14px;align-items:end;padding:14px;border:1px solid #35434c;background:#101820}.detail-head :deep(.deck-profile){border:0;background:transparent}.metrics{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.metrics span{padding:6px 8px;border:1px solid #43515a;background:#0a1117;color:#bac4c5;font-size:12px}.detail-tabs{display:flex;gap:6px;margin-top:14px;overflow-x:auto;padding-bottom:2px}.detail-tabs button,.content-panel button,.content-panel select{min-height:38px;padding:7px 11px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:800;white-space:nowrap}.detail-tabs button.active{border-color:#e0bf6d;color:#f4d980}.deck-layout{display:grid;grid-template-columns:220px minmax(0,1fr);gap:14px;margin-top:14px}.deck-layout>aside{display:grid;align-content:start;gap:10px}.deck-layout>aside:first-child section{padding:12px;border:1px solid #35434c;background:#0c141a}.deck-layout>aside:first-child p{display:flex;justify-content:space-between;color:#8c999d;font-size:13px}.deck-layout>aside:first-child strong{color:#eee}.curve{display:flex;height:105px;align-items:end;gap:3px;margin-top:8px}.curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.curve small,.curve em{font-size:11px;font-style:normal}.content-panel{margin-top:14px;padding:16px;border:1px solid #35434c;background:#0c141a}.content-panel>header{display:flex;justify-content:space-between;gap:12px;align-items:start;margin-bottom:14px}.content-panel h2,.content-panel h3,.content-panel p{margin:0}.content-panel header p,.empty-copy,.hand-note{margin-top:5px;color:#8c999d}.content-panel label{display:grid;gap:6px;margin:12px 0;color:#bac4c5;font-weight:700}.content-panel textarea{box-sizing:border-box;width:100%;padding:10px;border:1px solid #4c5a62;background:#081015;color:#eee;font:inherit;line-height:1.6;resize:vertical}.reading-sections,.matchup-list,.version-list,.match-list{display:grid;gap:10px}.reading-sections article,.matchup-list article,.matchup-editor,.version-list details,.match-list article{padding:12px;border:1px solid #35434c;background:#101820}.reading-sections p,.matchup-list p{margin-top:6px;white-space:pre-wrap;line-height:1.65}.matchup-list p b{display:inline-block;min-width:64px;color:#d8c07b}.matchup-city{display:flex;align-items:center;gap:10px;margin-bottom:10px}.matchup-city :deep(.l12-card-image){width:54px;height:54px;flex:none;object-fit:cover;object-position:center 24%;border:1px solid #536169}.matchup-editor{margin-top:10px}.editor-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:12px}.content-panel .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.content-panel .danger{border-color:#9e3944;background:#4d171d}.version-list summary{display:grid;grid-template-columns:auto minmax(0,1fr) auto;gap:10px;cursor:pointer}.version-list time,.match-list time{color:#8c999d}.version-list ul{margin:10px 0 0;padding-left:22px}.version-list details>p{margin-top:10px;color:#bac4c5}.match-list article{display:grid;grid-template-columns:auto minmax(0,1fr) auto auto;gap:10px;align-items:center}.mobile-replay{display:none}.opening-hand{display:grid;grid-template-columns:repeat(6,minmax(90px,1fr));gap:10px}.opening-hand article{min-width:0}.opening-hand img{display:block;width:100%;aspect-ratio:5/7;object-fit:contain;background:#050708}.opening-hand b{display:block;overflow-wrap:anywhere;margin-top:5px;text-align:center;line-height:1.4}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;margin-top:14px;padding-top:14px;border-top:1px solid #35434c}.actions button,.preview-mask button{min-height:40px;padding:8px 12px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.actions .primary,.preview-mask .primary{border-color:#e0bf6d;background:#e0bf6d;color:#090c0e}.actions .danger{border-color:#9e3944;background:#4d171d}.notice{position:fixed;right:18px;bottom:18px;padding:10px 13px;border:1px solid #d5b45f;background:#251b08;color:#f4d980}.state{display:grid;min-height:45vh;place-items:center;color:#89969a}.state.error{color:#e3a8ad}.preview-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:#010406d9}.preview-mask>section{position:relative;width:min(1100px,94vw);max-height:90vh;border:1px solid #52606a;background:#111923}.preview-mask img{display:block;width:100%;max-height:78vh;object-fit:contain}.preview-mask .close{position:absolute;right:8px;top:8px}.preview-mask footer{display:flex;justify-content:flex-end;padding:10px}
@media(max-width:700px){.public-deck-detail{padding:14px 11px 44px}.detail-head{grid-template-columns:1fr;align-items:start}.metrics{justify-content:flex-start}.detail-tabs{margin-inline:-11px;padding-inline:11px}.deck-layout{grid-template-columns:1fr}.deck-layout>aside{grid-template-columns:1fr 1fr}.content-panel{padding:12px}.content-panel>header{align-items:stretch;flex-direction:column}.editor-actions{flex-wrap:wrap}.editor-actions button{flex:1}.version-list summary{grid-template-columns:auto 1fr}.version-list summary time{grid-column:1/-1}.match-list article{grid-template-columns:1fr}.desktop-replay{display:none}.mobile-replay{display:inline;color:#8c999d}.opening-hand{display:flex;overflow-x:auto;padding-bottom:8px}.opening-hand article{flex:0 0 112px}.actions button{flex:1 1 42%}.notice{position:static}.preview-mask{padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}}
@media(max-width:440px){.deck-layout>aside{grid-template-columns:1fr}.metrics span{font-size:11px}.actions button{font-size:12px}}
.detail-toolbar{display:flex;align-items:flex-start;justify-content:space-between;gap:10px;margin-top:14px}.detail-toolbar .detail-tabs{flex:1;flex-wrap:wrap;margin-top:0;overflow:visible}.detail-toolbar .actions{display:flex;flex:1;flex-wrap:wrap;justify-content:flex-end;margin:0;padding:0;border:0}.detail-anchor-section{scroll-margin-top:16px}.deck-layout.detail-anchor-section{margin-top:14px}
.opening-hand :deep(.l12-card-image){display:block;width:100%;aspect-ratio:5/7;background:#050708}.opening-hand small{display:block;overflow-wrap:anywhere;margin-top:4px;color:#cdbb7d;font-size:11px;text-align:center;line-height:1.35}
.match-stat-list{display:grid;gap:10px}.match-stat-list article{padding:12px;border:1px solid #35434c;background:#101820}.match-stat-list article>header{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px}.match-stat-list article>header span{color:#bac4c5}.match-stat-list dl{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin:10px 0 0}.match-stat-list dl div{padding:8px;border:1px solid #34434a;background:#0a1117}.match-stat-list dt{color:#8c999d;font-size:12px}.match-stat-list dd{margin:4px 0 0;color:#f0d47c;font-size:18px;font-weight:900}
@media(max-width:700px){.match-stat-list dl{grid-template-columns:1fr}}
@media(max-width:900px){.detail-toolbar{flex-direction:column}.detail-toolbar .detail-tabs,.detail-toolbar .actions{width:100%;justify-content:flex-start}.detail-toolbar .actions button{flex:1 1 auto}}
</style>
