<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { createDeckImageBlob, decodeDeckCode, downloadDeckImage, encodeDeckCode } from './deckShare'
import { captureDeckAccountGuard, deckCountSummary, deleteDeck as deleteSavedDeck, ensureOfficialPrebuiltDecks, loadDeckCatalog, loadPrivateDeckBody, loadPrivateDeckSummaryPage, uniqueDeckCopyName, loadPrivatePublicationSource, loadSavedDecks, loadSavedDecksState, deckErrorBelongsToCurrentAccount, saveDeck, validateDeck, type DeckCard, type PrivateDeckSummary, type PrivateDeckSummaryPage, type PrivateDeckSummaryQuery, type SavedL12Deck } from '@/l12/decks'
import { alternateArtApi, deckLibraryApi, getEffectiveOperationsPolicy, platformState, publicDeckApi, publicDeckReadApi, type AlternateArt, type EffectiveOperationsPolicy, type PublishedDeck, type PublicDeckGuide, type PublicDeckMatchup, type PublicDeckSummary, type PublicDeckSummaryPage, type PublicDeckSummaryQuery, type OwnPublicDeckReference } from '@/l12/platform'
import { useRoute, useRouter } from 'vue-router'
import DeckProfile from '@/l12/DeckProfile.vue'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import MobileFilterSheet from './MobileFilterSheet.vue'
import { useActionGate } from '@/l12/useActionGate'
import { capturePublicDeckCounterGuard, isOfficialPublicDeckCounterTarget, mergePublicDeckCounters, publicDeckRouteReference, publicDeckSummaryReference } from './publicDeckEntry'
import { deckEnvironmentLabel, type DeckEnvironmentFilter } from './deckEnvironment'
import { createSummaryRequestGate, matchesOwnPublication, rememberSummaryOpen, validateOwnReferences, validateSummaryPage } from './publicDeckSummary'
import { currentReadGeneration, settlePublicDeckRead, validatePublicDeckContentCurrent, validatePublicDeckCurrent } from './publicDeckRead'
import { deckEditorQuery } from './deckEditorNavigation'

const PAGE_SIZE = 30
const HOT_DECK_PIXELS_PER_SECOND = [48, 46] as const
const tab = ref<'mine' | 'plaza'>('plaza')
const pageRoot = ref<HTMLElement | null>(null)
const importTrigger = ref<HTMLButtonElement | null>(null)
const importInput = ref<HTMLInputElement | null>(null)
const actionViewport = window.matchMedia('(min-width:701px)')
const desktopActions = ref(actionViewport.matches)
function updateActionViewport() { desktopActions.value = actionViewport.matches }
actionViewport.addEventListener('change', updateActionViewport)
onBeforeUnmount(() => actionViewport.removeEventListener('change', updateActionViewport))
const catalog = ref<DeckCard[]>([])
const ownedAlternateArts = ref<AlternateArt[]>([])
const saved = ref<Record<string, SavedL12Deck>>({})
type MineDeckEntry = PrivateDeckSummary & { guestDeck?: SavedL12Deck }
const privateSummaryPage = ref<PrivateDeckSummaryPage | null>(null)
const privateLoadState = ref<'loading' | 'available' | 'unavailable'>('loading')
const privateLoadError = ref('')
let privateDirectorySequence = 0
let mineBodyReadSequence = 0
const published = ref<Array<PublicDeckSummary | PublishedDeck>>([])
const summaryPage = ref<PublicDeckSummaryPage | null>(null)
const facetPage = ref<PublicDeckSummaryPage | null>(null)
const hotSummaries = ref<PublicDeckSummary[]>([])
const publicLoadState = ref<'loading' | 'available' | 'unavailable'>('loading')
const publicLoadError = ref('')
const ownReferences = ref<OwnPublicDeckReference[]>([])
const referenceState = ref<'idle' | 'loading' | 'available' | 'unavailable'>('idle')
const referenceError = ref('')
const updatedSince = ref<string | undefined>()
let libraryMounted = false
let libraryDisposed = false
const operationsPolicy = ref<EffectiveOperationsPolicy | null>(null)
const query = ref('')
const importCode = ref('')
const importError = ref('')
const showImport = ref(false)
const importBusy = ref(false)
let importRequestSequence = 0
watch(importCode, () => { importRequestSequence++ }, { flush: 'sync' })
const notice = ref('')
const publishName = ref('')
const showPublish = ref(false)
const factionFilter = ref('all')
const masterFilter = ref('all')
const legalFilter = ref<'all' | 'legal' | 'illegal'>('all')
const environmentFilter = ref<DeckEnvironmentFilter>('all')
const cardFilter = ref('')
const updatedFilter = ref<'all' | '1' | '7' | '30' | '90' | '365'>('all')
const sortMode = ref<'trend' | 'copies' | 'likes' | 'views' | 'latest' | 'name'>('trend')
const plazaFiltersOpen = ref(false)
const imagePreview = ref<{ deck: SavedL12Deck; blob: Blob; url: string } | null>(null)
let imageReadSequence = 0
const deletingMine = ref('')
const mineQuery = ref('')
const mineHomeCityFilter = ref('all')
const mineLegalFilter = ref<'all' | 'legal' | 'illegal'>('all')
const mineSort = ref<'latest' | 'name'>('latest')
const minePage = ref(1)
const plazaPage = ref(1)
const hotDeckHoverPaused = ref([false, false])
const hotDeckFocusPaused = ref([false, false])
const hotDeckTouchPaused = ref([false, false])
const cardPickerOpen = ref(false)
const publishGuide = ref<PublicDeckGuide>({ buildIdea: '', opening: '', keyCards: '', commonSequence: '', substitutions: '' })
const publishMatchups = ref<PublicDeckMatchup[]>([])
interface PendingPublicDeckPublish {
  accountId: string
  token: string
  accountEpoch: number
  deckId: string
  deckRevision: number
  deckBodyKey: string
  publicationId: string
  publicationVersion: number
  reference: string
  contentKey: string
  guide: PublicDeckGuide
  matchups: PublicDeckMatchup[]
  provenanceSyncRequired: boolean
}
let pendingPublicDeckPublish: PendingPublicDeckPublish | null = null
let publishRequestSequence = 0
const route = useRoute()
const router = useRouter()
const { pending: actionBusy, isPending: actionPending, run: runAction } = useActionGate()
let hotDeckResizeObserver: ResizeObserver | null = null
let libraryAccountEpoch = 0
let libraryCounterDocumentEpoch = 0
watch(() => route.fullPath, () => {
  libraryCounterDocumentEpoch++
  mineBodyReadSequence++
  if (libraryMounted && platformState.account && tab.value === 'mine') void loadMineDirectory()
}, { flush: 'sync' })
onBeforeUnmount(() => libraryCounterDocumentEpoch++)
function libraryContext() { return { account: platformState.account?.id, token: platformState.token, epoch: libraryAccountEpoch, route: route.fullPath, tab: tab.value } }
function libraryContextCurrent(context: ReturnType<typeof libraryContext>, document = true) {
  return context.account === platformState.account?.id && context.token === platformState.token && context.epoch === libraryAccountEpoch
    && (!document || context.route === route.fullPath && context.tab === tab.value)
}
function mineDirectoryQuery(): PrivateDeckSummaryQuery {
  return {
    page: minePage.value, pageSize: PAGE_SIZE,
    keyword: mineQuery.value.trim() || undefined,
    masterId: mineHomeCityFilter.value === 'all' ? undefined : mineHomeCityFilter.value,
    legal: mineLegalFilter.value === 'all' ? undefined : mineLegalFilter.value === 'legal',
    sort: mineSort.value,
  }
}
async function loadMineDirectory() {
  const context = libraryContext(), query = mineDirectoryQuery(), queryKey = JSON.stringify(query)
  const documentEpoch = libraryCounterDocumentEpoch, request = ++privateDirectorySequence
  if (!platformState.account) {
    privateSummaryPage.value = null; privateLoadState.value = 'available'; privateLoadError.value = ''
    return
  }
  privateLoadState.value = 'loading'; privateLoadError.value = ''
  const current = () => !libraryDisposed && request === privateDirectorySequence
    && documentEpoch === libraryCounterDocumentEpoch && libraryContextCurrent(context)
    && JSON.stringify(mineDirectoryQuery()) === queryKey
  try {
    const page = await loadPrivateDeckSummaryPage(query)
    if (!current()) return
    privateSummaryPage.value = page; privateLoadState.value = 'available'
    if (libraryMounted) void loadOwnReferences()
  } catch (error) {
    if (!current() || !deckErrorBelongsToCurrentAccount(error)) return
    privateSummaryPage.value = null; privateLoadState.value = 'unavailable'
    privateLoadError.value = error instanceof Error ? error.message : '牌库目录暂不可读取'
  }
}
watch(() => [platformState.account?.id, platformState.token], async () => {
  libraryAccountEpoch++
  privateDirectorySequence++
  mineBodyReadSequence++
  publishRequestSequence++
  pendingPublicDeckPublish = null
  const context = libraryContext(), snapshot = loadSavedDecksState()
  saved.value = snapshot.status === 'unavailable' ? {} : snapshot.decks
  privateSummaryPage.value = null
  privateLoadState.value = platformState.account ? 'loading' : 'available'
  privateLoadError.value = ''
  ownedAlternateArts.value = []
  deletingMine.value = ''; publishName.value = ''; showPublish.value = false
  closeImagePreview()
  notice.value = snapshot.status === 'unavailable' ? snapshot.error.message : ''
  ownReferences.value = []; referenceState.value = 'idle'; referenceError.value = ''
  if (libraryMounted) {
    await loadSummarySources()
    if (libraryDisposed || !libraryContextCurrent(context, false)) return
  }
  if (snapshot.status === 'unavailable' && !platformState.account) return
  try {
    const [decks, arts] = await Promise.all([
      platformState.account ? loadMineDirectory().then(() => null) : ensureOfficialPrebuiltDecks(),
      platformState.account ? alternateArtApi.mine() : Promise.resolve([] as AlternateArt[]),
    ])
    if (!libraryDisposed && libraryContextCurrent(context, false)) {
      if (decks) saved.value = decks
      ownedAlternateArts.value = arts
    }
    if (libraryMounted && !libraryDisposed && libraryContextCurrent(context, false)) await loadOwnReferences()
  } catch (error) {
    if (libraryContextCurrent(context, false) && deckErrorBelongsToCurrentAccount(error)) notice.value = error instanceof Error ? error.message : '牌库暂不可读取'
  }
}, { flush: 'sync' })
onBeforeUnmount(() => { publishRequestSequence++; pendingPublicDeckPublish = null })

const publicDeckActionKey = (deckId: string, accountId = platformState.account?.id ?? 'anonymous') =>
  `public-deck:${accountId}:${deckId}`
function captureCounterContext(value: PublishedDeck | PublicDeckSummary, key: string) {
  const context = libraryContext()
  return capturePublicDeckCounterGuard(value, {
    actorCurrent: () => libraryContextCurrent(context),
    document: () => `${libraryCounterDocumentEpoch}:${route.fullPath}`,
    entry: () => published.value.find(item => item.id === value.id) ?? null,
    actionCurrent: () => actionPending(key),
  })
}
const editorLink = (deckName?: string, publicationId?: string, personalDeckId?: string) => ({
  path: '/deck-editor',
  query: deckEditorQuery(route.fullPath, deckName, publicationId, personalDeckId),
})

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
function updateHotDeckDuration(loop: HTMLElement) {
  const track = loop.parentElement
  const rowIndex = Number(track?.dataset.hotRowTrack) - 1
  const targetSpeed = HOT_DECK_PIXELS_PER_SECOND[rowIndex]
  const loopWidth = loop.getBoundingClientRect().width
  if (!track || !targetSpeed || loopWidth <= 0) return
  track.style.setProperty('--hot-deck-duration', `${loopWidth / targetSpeed}s`)
  track.dataset.motionReady = 'true'
}
function syncHotDeckMotion() {
  hotDeckResizeObserver?.disconnect()
  hotDeckResizeObserver = typeof ResizeObserver === 'undefined'
    ? null
    : new ResizeObserver(entries => entries.forEach(entry => updateHotDeckDuration(entry.target as HTMLElement)))
  const tracks = pageRoot.value?.querySelectorAll<HTMLElement>('.hot-deck-track') ?? []
  tracks.forEach(track => {
    track.removeAttribute('data-motion-ready')
    const originalLoop = track.querySelector<HTMLElement>(':scope > .hot-deck-loop:not([aria-hidden="true"])')
    if (!originalLoop) return
    hotDeckResizeObserver?.observe(originalLoop)
    updateHotDeckDuration(originalLoop)
  })
}
onBeforeUnmount(() => {
  hotDeckResizeObserver?.disconnect()
  hotDeckResizeObserver = null
})
// This phase is synchronous local cache work, not a pending HTTP page load.
function applyLibraryCacheSnapshot() {
  const snapshot = loadSavedDecksState()
  saved.value = snapshot.status === 'unavailable' ? {} : snapshot.decks
  if (snapshot.status === 'unavailable') notice.value = snapshot.error.message
}
onMounted(async () => {
  restoreFiltersFromRoute()
  libraryMounted = true
  if (!platformState.account) privateLoadState.value = 'available'
  const context = libraryContext()
  applyLibraryCacheSnapshot()
  await loadSummarySources()
  if (libraryDisposed || !libraryContextCurrent(context, false)) return
  try {
    const [cards, decks, arts] = await Promise.all([
      loadDeckCatalog().then(cards => { catalog.value = cards; return cards }),
      (platformState.account ? loadMineDirectory().then(() => null) : ensureOfficialPrebuiltDecks()).catch(error => {
        if (libraryContextCurrent(context, false) && deckErrorBelongsToCurrentAccount(error))
          notice.value = error instanceof Error ? error.message : '本机牌库暂不可读取'
        return null
      }),
      platformState.account ? alternateArtApi.mine().catch(() => [] as AlternateArt[]) : Promise.resolve([] as AlternateArt[]),
    ])
    if (!libraryContextCurrent(context, false)) return
    catalog.value = cards
    if (decks) saved.value = decks
    ownedAlternateArts.value = arts
    const policy = await getEffectiveOperationsPolicy().catch(() => null)
    if (!libraryContextCurrent(context, false)) return
    operationsPolicy.value = policy
    void loadOwnReferences()
    await nextTick()
    const savedScroll = sessionStorage.getItem(`l12:deck-library:scroll:${route.fullPath}`)
    if (savedScroll) listScrollHost()?.scrollTo({ top: Number(savedScroll) || 0 })
  } catch (error) {
    if (libraryContextCurrent(context, false) && deckErrorBelongsToCurrentAccount(error))
      notice.value = error instanceof Error ? error.message : '牌库页面加载失败'
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const localMine = computed<MineDeckEntry[]>(() => Object.values(saved.value).map(deck => {
  const main = deckCountSummary(deck.cardIds, byId.value)
  const legalityReason = validateDeck(deck, catalog.value, operationsPolicy.value?.cardRestrictions)
  return {
    id: deck.id || `guest:${encodeURIComponent(deck.name)}`, revision: deck.revision ?? 1,
    name: deck.name, masterId: deck.masterId, updatedAt: deck.updatedAt,
    publicationId: deck.publicationId ?? null, publicationVersion: deck.publicationVersion ?? null,
    counts: { main: main.counted, uncountedMain: main.uncounted, morale: deck.moraleIds.length,
      special: deck.specialIds.length, bench: deck.benchIds?.length ?? 0 },
    legal: !legalityReason, legalityReason: legalityReason || null, guestDeck: deck,
  }
}).sort((left, right) => right.updatedAt.localeCompare(left.updatedAt)))
const mine = computed<MineDeckEntry[]>(() => platformState.account
  ? privateSummaryPage.value?.items ?? [] : localMine.value)
const homeCities = computed(() => {
  const ids = platformState.account
    ? privateSummaryPage.value?.facets.masters.map(item => item.masterId) ?? []
    : [...new Set(mine.value.map(deck => deck.masterId))]
  if (mineHomeCityFilter.value !== 'all' && !ids.includes(mineHomeCityFilter.value)) ids.push(mineHomeCityFilter.value)
  return ids.map(id => byId.value.get(id)).filter(Boolean) as DeckCard[]
})
const filteredMine = computed(() => {
  if (platformState.account) return mine.value
  const keyword = mineQuery.value.trim().toLocaleLowerCase('zh-CN')
  const values = mine.value.filter(deck => {
    return (!keyword || deck.name.toLocaleLowerCase('zh-CN').includes(keyword))
      && (mineHomeCityFilter.value === 'all' || deck.masterId === mineHomeCityFilter.value)
      && (mineLegalFilter.value === 'all' || (mineLegalFilter.value === 'legal') === deck.legal)
  })
  return [...values].sort((left, right) => mineSort.value === 'name'
    ? left.name.localeCompare(right.name, 'zh-CN') : right.updatedAt.localeCompare(left.updatedAt))
})
const mineTotal = computed(() => platformState.account ? privateSummaryPage.value?.total ?? 0 : filteredMine.value.length)
const minePageCount = computed(() => Math.max(1, Math.ceil(mineTotal.value / PAGE_SIZE)))
const pagedMine = computed(() => platformState.account ? filteredMine.value
  : filteredMine.value.slice((minePage.value - 1) * PAGE_SIZE, minePage.value * PAGE_SIZE))
const mineFilterActive = computed(() => Boolean(mineQuery.value.trim()) || mineHomeCityFilter.value !== 'all' || mineLegalFilter.value !== 'all' || mineSort.value !== 'latest')
const plazaFactions = computed(() => facetPage.value?.facets.factions.map(item => item.value) ?? [])
const plazaMasters = computed(() => (facetPage.value?.facets.masters ?? []).map(item => byId.value.get(item.value)).filter(Boolean) as DeckCard[])
const plazaCards = computed(() => (facetPage.value?.facets.cards ?? []).map(item => byId.value.get(item.value)).filter(Boolean) as DeckCard[])
const plazaCardPickerItems = computed<SingleCardPickerItem[]>(() => plazaCards.value.map(card => ({
  id: card.id, cardId: card.id, number: card.number, name: card.nameZh, nameZh: card.nameZh,
  cardType: card.cardType, faction: card.faction, product: card.product, cost: card.cost,
  disasterLevel: card.disasterLevel, effect: card.effect, imageUrl: card.imageUrl, cardImageId: card.id, detailCard: card,
})))
const publishHomeCities = computed(() => catalog.value.filter(card => card.cardType === 'master'))
const filteredPublished = computed(() => published.value.filter((item): item is PublicDeckSummary => 'source' in item))
const plazaTotal = computed(() => summaryPage.value?.total ?? 0)
const plazaPageCount = computed(() => Math.max(1, Math.ceil(plazaTotal.value / PAGE_SIZE)))
const pagedPublished = filteredPublished
const hotDeckRows = computed(() => {
  const ranked = hotSummaries.value
  if (!ranked.length) return []
  const first = ranked.filter((_, index) => index % 2 === 0)
  const second = ranked.filter((_, index) => index % 2 === 1)
  return [first, second.length ? second : [...first].reverse()]
})
function summaryQuery(): PublicDeckSummaryQuery {
  return { source: 'all', page: plazaPage.value, pageSize: PAGE_SIZE, keyword: query.value.trim() || undefined,
    masterId: masterFilter.value === 'all' ? undefined : masterFilter.value,
    faction: factionFilter.value === 'all' ? undefined : factionFilter.value,
    legal: legalFilter.value === 'all' ? undefined : legalFilter.value === 'legal',
    environment: environmentFilter.value === 'all' ? undefined : environmentFilter.value,
    cardId: cardFilter.value || undefined, updatedAfter: updatedSince.value, sort: sortMode.value }
}
const summaryGate = createSummaryRequestGate(() => JSON.stringify({ ...libraryContext(), documentEpoch: libraryCounterDocumentEpoch, query: summaryQuery() }))
const referenceGate = createSummaryRequestGate(() => JSON.stringify({ ...libraryContext(), documentEpoch: libraryCounterDocumentEpoch, ids: visiblePublicationIds() }))
async function loadSummarySources() {
  if (!libraryMounted || libraryDisposed) return
  const request = summaryQuery(), current = summaryGate.begin()
  const baseline: PublicDeckSummaryQuery = { source: 'all', page: 1, pageSize: 16, sort: 'trend' }
  publicLoadState.value = 'loading'; publicLoadError.value = ''
  published.value = []; summaryPage.value = null; facetPage.value = null; hotSummaries.value = []
  try {
    const [response, baselineResponse] = await Promise.all([deckLibraryApi.summaries(request), deckLibraryApi.summaries(baseline)])
    const page = validateSummaryPage(response, request), global = validateSummaryPage(baselineResponse, baseline)
    if (!current()) return
    if (page.catalogVersion !== global.catalogVersion || page.policyVersion !== global.policyVersion)
      throw new Error('牌库目录规则正在变化，请刷新后重新筛选')
    summaryPage.value = page; published.value = page.items; publicLoadState.value = 'available'
    facetPage.value = global; hotSummaries.value = global.items
    if (plazaPage.value > Math.max(1, Math.ceil(page.total / PAGE_SIZE))) plazaPage.value = Math.max(1, Math.ceil(page.total / PAGE_SIZE))
  } catch (error) {
    if (current()) { publicLoadState.value = 'unavailable'; publicLoadError.value = error instanceof Error ? error.message : '公开牌库当前无法读取' }
  }
}
function visiblePublicationIds() {
  return [...new Set(pagedMine.value.map(deck => deck.publicationId).filter((id): id is string => Boolean(id)))].slice(0, 100)
}
async function loadOwnReferences() {
  if (!libraryMounted || libraryDisposed) return
  const actor = platformState.account?.id, ids = visiblePublicationIds(), current = referenceGate.begin()
  ownReferences.value = []; referenceError.value = ''
  if (!actor || !platformState.token || !ids.length) { referenceState.value = 'idle'; return }
  referenceState.value = 'loading'
  try {
    const items = validateOwnReferences(await deckLibraryApi.ownReferences(ids), ids, actor)
    if (current()) { ownReferences.value = items; referenceState.value = 'available' }
  } catch (error) {
    if (current()) { referenceState.value = 'unavailable'; referenceError.value = error instanceof Error ? error.message : '公开引用当前无法确认' }
  }
}
watch(() => updatedFilter.value, value => {
  updatedSince.value = value === 'all' ? undefined : new Date(Date.now() - Number(value) * 86400000).toISOString()
}, { immediate: true, flush: 'sync' })
// Identity refresh is serialized by the synchronous-start account watcher.
// These watchers handle only navigation/filter/visible-row changes, avoiding a
// second identical directory/reference request for the same identity event.
watch(() => JSON.stringify({ route: route.fullPath, tab: tab.value, documentEpoch: libraryCounterDocumentEpoch, query: summaryQuery() }), () => {
  if (libraryMounted) void loadSummarySources()
})
watch(() => JSON.stringify({ route: route.fullPath, tab: tab.value, ids: visiblePublicationIds() }), () => {
  if (libraryMounted) void loadOwnReferences()
})
onBeforeUnmount(() => {
  libraryDisposed = true; privateDirectorySequence++; mineBodyReadSequence++
  summaryGate.dispose(); referenceGate.dispose(); ownReferences.value = []
})
watch(hotDeckRows, async () => {
  await nextTick()
  syncHotDeckMotion()
}, { flush: 'post' })
function hotDeckRowPaused(rowIndex: number) {
  return Boolean(hotDeckHoverPaused.value[rowIndex] || hotDeckFocusPaused.value[rowIndex] || hotDeckTouchPaused.value[rowIndex])
}
const plazaFilterCount = computed(() => [factionFilter.value !== 'all', masterFilter.value !== 'all', legalFilter.value !== 'all', environmentFilter.value !== 'all', !!cardFilter.value, updatedFilter.value !== 'all', sortMode.value !== 'trend'].filter(Boolean).length)
const plazaFilterSummary = computed(() => [
  factionFilter.value === 'all' ? '' : (factionLabels[factionFilter.value] || factionFilter.value),
  masterFilter.value === 'all' ? '' : byId.value.get(masterFilter.value)?.nameZh,
  legalFilter.value === 'all' ? '' : legalFilter.value === 'legal' ? '符合本赛季' : '不符合本赛季',
  environmentFilter.value === 'all' ? '' : `环境 ${environmentFilter.value}`,
  cardFilter.value ? `含${byId.value.get(cardFilter.value)?.nameZh || cardFilter.value}` : '',
  updatedFilter.value === 'all' ? '' : `${updatedFilter.value}天内更新`,
  sortMode.value === 'trend' ? '' : ({ copies: '最多复制', likes: '最多点赞', views: '最多浏览', latest: '最新发布', name: '按名称' } as const)[sortMode.value],
].filter(Boolean).join(' · '))

async function uniqueName(base: string, current: () => boolean = () => true) {
  return uniqueDeckCopyName(base, current)
}
function mineActionKey(action: string, entry: Pick<PrivateDeckSummary, 'id' | 'revision'>) {
  return `private-deck:${platformState.account?.id ?? 'guest'}:${action}:${entry.id}:${entry.revision}`
}
function captureMineBodyCurrent(entry: Pick<PrivateDeckSummary, 'id' | 'revision'>, extra: () => boolean = () => true) {
  const context = libraryContext(), documentEpoch = libraryCounterDocumentEpoch
  const queryKey = JSON.stringify(mineDirectoryQuery()), request = ++mineBodyReadSequence
  const actorCurrent = captureDeckAccountGuard()
  return () => request === mineBodyReadSequence && !libraryDisposed && documentEpoch === libraryCounterDocumentEpoch
    && actorCurrent() && libraryContextCurrent(context) && JSON.stringify(mineDirectoryQuery()) === queryKey && extra()
    && (!platformState.account || privateSummaryPage.value?.items.some(
      candidate => candidate.id === entry.id && candidate.revision === entry.revision) === true)
}
async function readMineDeck(entry: MineDeckEntry, current: () => boolean) {
  if (platformState.account) return loadPrivateDeckBody(entry, current)
  if (!current()) throw new Error('页面已切换，已取消牌库正文读取')
  const deck = entry.guestDeck ?? Object.values(saved.value).find(candidate => candidate.id
    ? candidate.id === entry.id : candidate.name === entry.name)
  if (!deck || deck.id && (deck.id !== entry.id || deck.revision !== entry.revision))
    throw new Error('牌库已变化，请刷新后重试')
  return deck
}
async function copyToMine(entry: PublishedDeck) {
  const accountId = platformState.account?.id
  const key = publicDeckActionKey(entry.id, accountId), current = captureCounterContext(entry, key)
  await runAction(key, async () => {
    if (!current()) return
    try {
      const name = await uniqueName(entry.deck.name, current)
      if (!current()) return
      const deck = { ...entry.deck, id: undefined, revision: undefined, name, publicationId: null, publicationVersion: null, cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...(entry.deck.specialIds ?? [])], updatedAt: new Date().toISOString() }
      const confirmed = await saveDeck(deck)
      if (current()) {
        saved.value = loadSavedDecks()
        await loadMineDirectory()
        if (!current()) return
        notice.value = `已复制《${confirmed.name}》到我的牌库`
      }
      if (current() && !isOfficialPublicDeckCounterTarget(entry)) {
        try {
          const counters = await publicDeckApi.counter(publicDeckRouteReference(entry), 'copy')
          if (current()) {
            const latest = published.value.find(item => item.id === entry.id)!
            updatePublished(mergePublicDeckCounters(latest, counters))
          }
        } catch { /* The deck save is confirmed; a counter failure cannot undo it or repeat the save. */ }
      }
    } catch (error) {
      if (!deckErrorBelongsToCurrentAccount(error)) return
      if (current())
        notice.value = error instanceof Error ? error.message : '复制到我的牌库失败'
    }
  })
}
function updatePublished(entry: PublishedDeck | PublicDeckSummary) {
  const index = published.value.findIndex(item => item.id === entry.id)
  if (index >= 0) published.value[index] = entry
  const hotIndex = hotSummaries.value.findIndex(item => item.id === entry.id)
  if (hotIndex >= 0 && 'source' in entry) hotSummaries.value[hotIndex] = entry
}
function publishedCopyFor(deck: Pick<SavedL12Deck, 'publicationId' | 'publicationVersion'>) {
  if (referenceState.value !== 'available') return undefined
  return ownReferences.value.find(item => matchesOwnPublication(deck, item, platformState.account?.id))
}
async function editMine(entry: MineDeckEntry) {
  const key = mineActionKey('edit', entry)
  if (actionPending(key)) return
  const current = captureMineBodyCurrent(entry)
  await runAction(key, async () => {
    try {
      const deck = await readMineDeck(entry, current)
      if (!current()) return
      sessionStorage.setItem(`l12:deck-library:scroll:${route.fullPath}`, String(listScrollHost()?.scrollTop ?? window.scrollY))
      await router.push(editorLink(deck.name, publishedCopyFor(entry)?.publicCode, deck.id))
    } catch (error) {
      if (current() && deckErrorBelongsToCurrentAccount(error)) notice.value = error instanceof Error ? error.message : '牌库正文暂不可读取'
    }
  })
}
async function deleteMine(entry: MineDeckEntry) {
  const context = libraryContext()
  if (deletingMine.value) return
  const stillPublic = publishedCopyFor(entry)
  const message = stillPublic
    ? `确定删除我的牌库《${entry.name}》？公开版本仍会长期保留，并继续显示在公开牌库。`
    : `确定删除我的牌库《${entry.name}》？此操作不会删除任何公开版本。`
  if (!window.confirm(message)) return
  const key = mineActionKey('delete', entry)
  if (actionPending(key)) return
  deletingMine.value = entry.id
  const current = captureMineBodyCurrent(entry, () => deletingMine.value === entry.id)
  await runAction(key, async () => {
    try {
      const deck = await readMineDeck(entry, current)
      if (!current()) return
      await deleteSavedDeck(deck)
      if (!libraryContextCurrent(context)) return
      saved.value = loadSavedDecks()
      await loadMineDirectory()
      if (!libraryContextCurrent(context)) return
      notice.value = stillPublic ? `已删除本地牌库《${entry.name}》，公开版本保持不变` : `已删除《${entry.name}》`
    } catch (error) {
      if (!libraryContextCurrent(context) || !deckErrorBelongsToCurrentAccount(error)) return
      notice.value = error instanceof Error ? error.message : '删除牌库失败'
    } finally {
      if (libraryContextCurrent(context, false)) deletingMine.value = ''
    }
  })
}
async function duplicateMine(entry: MineDeckEntry) {
  const context = libraryContext()
  const key = mineActionKey('duplicate', entry)
  if (actionPending(key)) return
  const current = captureMineBodyCurrent(entry)
  await runAction(key, async () => {
    try {
      const deck = await readMineDeck(entry, current)
      if (!current()) return
      const name = await uniqueName(`${deck.name} 副本`, current)
      if (!current()) return
      const copy = { ...deck, id: undefined, revision: undefined,
        name, publicationId: null, publicationVersion: null }
      await saveDeck(copy)
      if (!libraryContextCurrent(context)) return
      saved.value = loadSavedDecks()
      await loadMineDirectory()
      if (libraryContextCurrent(context)) notice.value = `已复制牌库《${copy.name}》`
    } catch (error) {
      if (!libraryContextCurrent(context) || !deckErrorBelongsToCurrentAccount(error)) return
      notice.value = error instanceof Error ? error.message : '复制牌库失败'
    }
  })
}
async function openDeck(entry: PublicDeckSummary) {
  const context = libraryContext(), actorCurrent = captureDeckAccountGuard()
  const intent = rememberSummaryOpen(entry, actorCurrent)
  const deckId = intent.reference
  if (!deckId) {
    notice.value = '该牌库暂时没有可用的详情地址'
    return
  }
  sessionStorage.setItem(`l12:deck-library:scroll:${route.fullPath}`, String(listScrollHost()?.scrollTop ?? window.scrollY))
  try {
    if (!libraryContextCurrent(context) || !actorCurrent()) return
    await router.push({ name: 'public-deck-detail', params: { deckId }, query: { from: route.fullPath, ...intent.query } })
  } catch {
    notice.value = '无法打开该牌库详情，请稍后重试'
  }
}
function listScrollHost(): HTMLElement | null {
  return pageRoot.value?.closest<HTMLElement>('.site-content') ?? document.scrollingElement as HTMLElement | null
}
function seasonRequirement(entry: PublicDeckSummary) {
  return entry.legal ? { compliant: true, label: '符合本赛季', reason: '符合当前赛季构筑要求' }
    : { compliant: false, label: '不符合本赛季', reason: entry.legalityReason || '不符合当前赛季构筑要求' }
}
function fullDeckSeasonRequirement(entry: PublishedDeck) {
  if (!entry.official && entry.seasonCompliant !== undefined) return entry.seasonCompliant
    ? { compliant: true, label: '符合本赛季', reason: operationsPolicy.value?.season.name ? `符合${operationsPolicy.value.season.name}构筑要求` : '符合当前赛季构筑要求' }
    : { compliant: false, label: '不符合本赛季', reason: entry.seasonComplianceReason || '不符合当前赛季构筑要求' }
  if (!operationsPolicy.value) return { compliant: false, label: '赛季要求未知', reason: '当前无法读取赛季规则' }
  const reason = validateDeck(entry.deck, catalog.value, operationsPolicy.value.cardRestrictions)
  return reason
    ? { compliant: false, label: '不符合本赛季', reason }
    : { compliant: true, label: '符合本赛季', reason: `符合${operationsPolicy.value.season.name || '当前赛季'}构筑要求` }
}
function deckFaction(entry: PublicDeckSummary) {
  return entry.faction || 'universal'
}
function summaryCountLabel(entry: PublicDeckSummary) {
  return entry.counts.uncountedMain ? `${entry.counts.main}(${entry.counts.uncountedMain})` : String(entry.counts.main)
}
function mineCountLabel(entry: MineDeckEntry) {
  const main = entry.counts.uncountedMain ? `${entry.counts.main}(${entry.counts.uncountedMain})` : String(entry.counts.main)
  return `${main} 张主牌 · ${entry.counts.morale} 张士气`
}
async function toggleLike(entry: PublishedDeck | PublicDeckSummary) {
  if (isOfficialPublicDeckCounterTarget(entry)) return
  if (!platformState.account) { notice.value = '请先登录账号再点赞'; return }
  const accountId = platformState.account.id
  const key = publicDeckActionKey(entry.id, accountId), current = captureCounterContext(entry, key)
  await runAction(key, async () => {
    if (!current()) return
    try {
      const counters = await publicDeckApi.counter('source' in entry ? publicDeckSummaryReference(entry) : publicDeckRouteReference(entry), 'like')
      if (current()) updatePublished(mergePublicDeckCounters(published.value.find(item => item.id === entry.id)!, counters))
    }
    catch (error) {
      if (current())
        notice.value = error instanceof Error ? error.message : '点赞失败'
    }
  })
}
function publishDeckBodyKey(deck: SavedL12Deck) {
  const orderedMap = (value: Record<string, string | string[]> | undefined) => Object.fromEntries(
    Object.entries(value ?? {}).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0))
  return JSON.stringify({ name: deck.name, masterId: deck.masterId, cardIds: deck.cardIds,
    moraleIds: deck.moraleIds, specialIds: deck.specialIds, benchIds: deck.benchIds ?? [],
    alternateArtSelections: orderedMap(deck.alternateArtSelections), alternateArtCopies: orderedMap(deck.alternateArtCopies) })
}
function normalizedPublishContent(guide: PublicDeckGuide, matchups: PublicDeckMatchup[]) {
  const text = (value: string) => value.replace(/\r\n/g, '\n').replace(/\r/g, '\n').trim()
  const normalizedGuide = { buildIdea: text(guide.buildIdea), opening: text(guide.opening),
    keyCards: text(guide.keyCards), commonSequence: text(guide.commonSequence), substitutions: text(guide.substitutions) }
  const normalizedMatchups = matchups.map(item => ({ opponentMasterId: item.opponentMasterId.trim().toUpperCase(),
    notes: text(item.notes), keyCards: text(item.keyCards), suggestedSwaps: text(item.suggestedSwaps) }))
    .filter(item => item.opponentMasterId || item.notes || item.keyCards || item.suggestedSwaps)
    .sort((left, right) => left.opponentMasterId < right.opponentMasterId ? -1 : left.opponentMasterId > right.opponentMasterId ? 1 : 0)
  return { guide: normalizedGuide, matchups: normalizedMatchups }
}
function publishContentKey(guide: PublicDeckGuide, matchups: PublicDeckMatchup[]) {
  return JSON.stringify(normalizedPublishContent(guide, matchups))
}
function publishContentHasValues(guide: PublicDeckGuide, matchups: PublicDeckMatchup[]) {
  const content = normalizedPublishContent(guide, matchups)
  return Object.values(content.guide).some(Boolean) || content.matchups.length > 0
}
function matchingPendingPublish(deck: SavedL12Deck, context: ReturnType<typeof libraryContext>, deckBodyKey: string) {
  const pending = pendingPublicDeckPublish
  return pending && pending.accountId === context.account && pending.token === context.token
    && pending.accountEpoch === context.epoch && pending.deckId === deck.id && pending.deckRevision === deck.revision
    && pending.deckBodyKey === deckBodyKey ? pending : null
}
async function publishDeck() {
  const deckName = publishName.value
  if (!deckName) return
  if (!platformState.account) { notice.value = '请先登录账号再公开牌库'; return }
  const actionKey = `public-deck:publish:${deckName}`
  if (actionPending(actionKey)) return
  const context = libraryContext(), actorCurrent = captureDeckAccountGuard(), request = ++publishRequestSequence
  const submittedGuide = { ...publishGuide.value }
  const submittedMatchups = publishMatchups.value.map(item => ({ ...item }))
  const submittedContentKey = publishContentKey(submittedGuide, submittedMatchups)
  const hasContent = publishContentHasValues(submittedGuide, submittedMatchups)
  const baseCurrent = () => request === publishRequestSequence && actorCurrent() && libraryContextCurrent(context)
    && showPublish.value && publishName.value === deckName
  let current = baseCurrent
  await runAction(actionKey, async () => {
    try {
      if (!baseCurrent()) return
      const directoryAvailable = typeof mine !== 'undefined'
      const directoryEntry = directoryAvailable ? mine.value.find(entry => entry.name === deckName) : undefined
      if (directoryAvailable && !directoryEntry) throw new Error('牌库目录已变化，请重新选择后再公开')
      const bodyCurrent = directoryEntry ? captureMineBodyCurrent(directoryEntry, baseCurrent) : baseCurrent
      const deck = directoryEntry ? await readMineDeck(directoryEntry, bodyCurrent) : saved.value[deckName]
      if (!deck || !bodyCurrent()) return
      const validationError = validateDeck(deck, catalog.value)
      if (validationError) { notice.value = validationError; return }
      if (!deck.id || !Number.isSafeInteger(deck.revision)) { notice.value = '牌库状态已变化，请重新选择后再公开'; return }
      const deckBodyKey = publishDeckBodyKey(deck)
      let expectedDeckId = deck.id, expectedDeckRevision = deck.revision, expectedDeckBodyKey = deckBodyKey
      current = () => {
        const selected = saved.value[deckName]
        return baseCurrent() && selected?.id === expectedDeckId && selected.revision === expectedDeckRevision
          && publishDeckBodyKey(selected) === expectedDeckBodyKey
      }
      if (!current()) return
      let pending = matchingPendingPublish(deck, context, deckBodyKey)
      if (pending?.provenanceSyncRequired) {
        notice.value = '服务器已确认牌库发布信息，但本机牌库缓存尚未同步；公开内容草稿仍保留，请先刷新页面同步我的牌库。'
        return
      }
      const retryingPending = Boolean(pending)
      if (!pending) {
        const entry = await publicDeckApi.publish(deck)
        if (!current()) return
        if (entry.deck.publicationId !== entry.id || !Number.isSafeInteger(entry.deck.publicationVersion)
          || Number(entry.deck.publicationVersion) < 1) throw new Error('公开牌库返回的发布身份无效')
        pending = {
          accountId: context.account!, token: context.token, accountEpoch: context.epoch,
          deckId: deck.id!, deckRevision: deck.revision!, deckBodyKey,
          publicationId: entry.id, publicationVersion: entry.deck.publicationVersion!,
          reference: publicDeckRouteReference(entry), contentKey: submittedContentKey,
          guide: submittedGuide, matchups: submittedMatchups, provenanceSyncRequired: false,
        }
        pendingPublicDeckPublish = pending
      } else {
        pending = { ...pending, contentKey: submittedContentKey, guide: submittedGuide, matchups: submittedMatchups }
        pendingPublicDeckPublish = pending
      }
      let selected = saved.value[deckName]
      if (selected?.publicationId !== pending.publicationId || selected.publicationVersion !== pending.publicationVersion) {
        const confirmed = await saveDeck({ ...deck, publicationId: pending.publicationId, publicationVersion: pending.publicationVersion })
        if (!baseCurrent()) return
        saved.value = loadSavedDecks()
        selected = saved.value[deckName]
        if (!confirmed.id || !Number.isSafeInteger(confirmed.revision) || Number(confirmed.revision) < 1
          || !selected || selected.id !== confirmed.id || selected.revision !== confirmed.revision
          || publishDeckBodyKey(selected) !== publishDeckBodyKey(confirmed)) return
        pending = { ...pending, deckId: confirmed.id!, deckRevision: confirmed.revision!,
          deckBodyKey: publishDeckBodyKey(confirmed) }
        pendingPublicDeckPublish = pending
        expectedDeckId = confirmed.id
        expectedDeckRevision = confirmed.revision
        expectedDeckBodyKey = pending.deckBodyKey
      }
      if (!current() || !selected || selected.id !== pending.deckId || selected.revision !== pending.deckRevision
        || publishDeckBodyKey(selected) !== pending.deckBodyKey) return
      if (hasContent || retryingPending) {
        const read = await settlePublicDeckRead(publicDeckReadApi.current(pending.reference), validatePublicDeckCurrent)
        if (!current()) return
        if (read.status !== 'available') {
          notice.value = `牌库已公开，但公开内容暂未确认保存；${read.message}草稿已保留，请再次点击“确认公开”。`
          return
        }
        if (!read.value.summary.canEdit || read.value.summary.id !== pending.publicationId)
          throw new Error('当前账号无法确认这个公开牌库的内容')
        const generation = currentReadGeneration(read.value)
        if (publishContentKey(read.value.guide, read.value.matchups) !== submittedContentKey) {
          const write = await settlePublicDeckRead(
            publicDeckReadApi.updateContent(generation.publicCode, generation.readToken, submittedGuide, submittedMatchups),
            value => validatePublicDeckContentCurrent(value, generation, read.value.contentRevision))
          if (!current()) return
          if (write.status !== 'available') {
            notice.value = `牌库已公开，但公开内容暂未确认保存；${write.message}草稿已保留，请再次点击“确认公开”。`
            return
          }
        }
      }
      if (!current()) return
      const currentDraftKey = publishContentKey(publishGuide.value, publishMatchups.value)
      const draftUnchanged = currentDraftKey === submittedContentKey
      pendingPublicDeckPublish = draftUnchanged ? null : { ...pending, contentKey: currentDraftKey,
        guide: { ...publishGuide.value }, matchups: publishMatchups.value.map(item => ({ ...item })) }
      if (typeof loadMineDirectory !== 'undefined') await loadMineDirectory()
      void loadSummarySources(); void loadOwnReferences()
      if (draftUnchanged) { showPublish.value = false; tab.value = 'plaza' }
      notice.value = draftUnchanged
        ? (hasContent ? '牌库与公开内容已同步发布' : '牌库已公开到公开牌库')
        : '提交时的牌库与公开内容已保存；当前编辑仍有未保存修改'
    } catch (error) {
      if (!current() || !deckErrorBelongsToCurrentAccount(error)) return
      if (error && typeof error === 'object' && (error as { serverConfirmed?: boolean }).serverConfirmed === true) {
        if (pendingPublicDeckPublish) pendingPublicDeckPublish = { ...pendingPublicDeckPublish, provenanceSyncRequired: true }
        notice.value = `${error instanceof Error ? error.message : '服务器已确认牌库发布信息，但本机缓存未更新'}；公开内容草稿仍保留，请先刷新页面同步我的牌库。`
        return
      }
      notice.value = pendingPublicDeckPublish
        ? `牌库已公开，但公开内容暂未确认保存；${error instanceof Error ? error.message : '请稍后重试'}。草稿已保留，请再次点击“确认公开”。`
        : error instanceof Error ? error.message : '公开牌库失败'
    }
  })
}
async function editPublished(entry: PublishedDeck) {
  const context = libraryContext()
  const actorCurrent = captureDeckAccountGuard(), current = () => actorCurrent() && libraryContextCurrent(context)
  try {
    const existing = await loadPrivatePublicationSource(entry.id, current)
    if (!current()) return
    const name = existing?.name ?? await uniqueName(entry.deck.name, current)
    if (!current()) return
    const deck = { ...entry.deck, id: existing?.id, revision: existing?.revision,
      name, cardIds: [...entry.deck.cardIds], moraleIds: [...entry.deck.moraleIds], specialIds: [...entry.deck.specialIds] }
    const confirmed = await saveDeck(deck)
    if (!libraryContextCurrent(context)) return
    saved.value = loadSavedDecks()
    await router.push(editorLink(confirmed.name, publicDeckRouteReference(entry), confirmed.id))
  } catch (error) { if (!libraryContextCurrent(context) || !deckErrorBelongsToCurrentAccount(error)) return; notice.value = error instanceof Error ? error.message : '牌库保存失败' }
}
async function deletePublished(entry: PublishedDeck) {
  if (!window.confirm('确定删除这个公开牌库？删除后将不再显示在公开牌库。')) return
  const accountId = platformState.account?.id
  await runAction(publicDeckActionKey(entry.id, accountId), async () => {
    try {
      await publicDeckApi.delete(publicDeckRouteReference(entry))
      if (accountId === platformState.account?.id) {
        published.value = published.value.filter(item => item.id !== entry.id)
        notice.value = `已从公开牌库删除《${entry.deck.name}》`
      }
    } catch (error) {
      if (accountId === platformState.account?.id)
        notice.value = error instanceof Error ? error.message : '删除公开牌库失败'
    }
  })
}
async function copyMineCode(entry: MineDeckEntry) {
  const key = mineActionKey('copy-code', entry)
  if (actionPending(key)) return
  const current = captureMineBodyCurrent(entry)
  await runAction(key, async () => {
    try {
      const deck = await readMineDeck(entry, current)
      if (!current()) return
      await navigator.clipboard.writeText(encodeDeckCode(deck))
      if (current()) notice.value = '牌库码已复制'
    } catch (error) {
      if (current() && deckErrorBelongsToCurrentAccount(error)) notice.value = error instanceof Error ? error.message : '牌库码复制失败'
    }
  })
}
function publicDeckUrl(id: string) {
  return new URL(router.resolve({ name: 'public-deck-detail', params: { deckId: id } }).href, window.location.origin).href
}
async function verifiedPublicDeckUrl(deck: SavedL12Deck) {
  const id = deck.publicationId?.trim()
  if (!id || !deck.publicationVersion) return ''
  const actor = platformState.account?.id, context = libraryContext()
  if (!actor) return ''
  const currentActor = captureDeckAccountGuard()
  const references = validateOwnReferences(await deckLibraryApi.ownReferences([id]), [id], actor)
  if (!currentActor() || !libraryContextCurrent(context)) throw new Error('账号或页面已切换，请重新生成牌库图')
  const candidate = references[0]
  return matchesOwnPublication(deck, candidate, actor) ? publicDeckUrl(candidate!.publicCode) : ''
}
async function previewImage(deck: SavedL12Deck, selectedCurrent: () => boolean = () => true) {
  const context = libraryContext(), actorCurrent = captureDeckAccountGuard(), request = ++imageReadSequence
  const current = () => request === imageReadSequence && actorCurrent() && libraryContextCurrent(context) && selectedCurrent()
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  imagePreview.value = null
  try {
    const publicUrl = await verifiedPublicDeckUrl(deck)
    if (!current()) return
    const presentationDeck = publicUrl ? { ...deck, alternateArtSelections: {}, alternateArtCopies: {} } : deck
    const blob = await createDeckImageBlob(presentationDeck, catalog.value, {
      publicUrl, alternateArts: publicUrl ? [] : ownedAlternateArts.value,
    })
    if (current()) imagePreview.value = { deck, blob, url: URL.createObjectURL(blob) }
  } catch (error) {
    if (current()) notice.value = error instanceof Error ? error.message : '公开牌库引用当前无法确认，请稍后再生成牌库图'
  }
}
async function previewMineImage(entry: MineDeckEntry) {
  const key = mineActionKey('image', entry)
  if (actionPending(key)) return
  const current = captureMineBodyCurrent(entry)
  await runAction(key, async () => {
    try {
      const deck = await readMineDeck(entry, current)
      if (current()) await previewImage(deck, current)
    } catch (error) {
      if (current() && deckErrorBelongsToCurrentAccount(error)) notice.value = error instanceof Error ? error.message : '牌库图生成失败'
    }
  })
}
function closeImagePreview() {
  imageReadSequence++
  if (imagePreview.value) URL.revokeObjectURL(imagePreview.value.url)
  imagePreview.value = null
}
async function copyPreviewImage() {
  if (!imagePreview.value) return
  try {
    await navigator.clipboard.write([new ClipboardItem({ 'image/png': imagePreview.value.blob })])
    notice.value = '牌库图已复制到剪贴板'
  } catch { notice.value = '当前浏览器不支持复制图片，请使用下载' }
}
async function importFromCode() {
  if (importBusy.value || !showImport.value) return
  const context = libraryContext()
  const originalCode = importCode.value
  const documentEpoch = libraryCounterDocumentEpoch
  const request = ++importRequestSequence
  const current = () => !libraryDisposed && showImport.value && importRequestSequence === request
    && libraryCounterDocumentEpoch === documentEpoch && importCode.value === originalCode && libraryContextCurrent(context)
  importError.value = ''
  if (!importCode.value.trim()) {
    importError.value = '请粘贴牌库码'
    return
  }
  importBusy.value = true
  try {
    const deck = decodeDeckCode(originalCode)
    deck.name = await uniqueName(deck.name, current)
    if (!current()) return
    const error = validateDeck(deck, catalog.value)
    if (error) throw new Error(error)
    const confirmed = await saveDeck(deck)
    if (!current()) return
    saved.value = loadSavedDecks(); minePage.value = 1
    await loadMineDirectory()
    if (!current()) return
    notice.value = `已导入《${confirmed.name}》`
    closeImportModal()
  } catch (error) { if (!current() || !deckErrorBelongsToCurrentAccount(error)) return; importError.value = error instanceof Error ? error.message : '牌库码导入失败' }
  finally { importBusy.value = false }
}
async function openImportModal() {
  importError.value = ''
  showImport.value = true
  await nextTick()
  importInput.value?.focus()
}
function closeImportModal() {
  importRequestSequence++
  showImport.value = false
  importError.value = ''
  importCode.value = ''
  void nextTick(() => importTrigger.value?.focus())
}
function handleImportDialogKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') closeImportModal()
}
function resetPlazaFilters() {
  factionFilter.value = 'all'; masterFilter.value = 'all'; legalFilter.value = 'all'; environmentFilter.value = 'all'; cardFilter.value = ''; updatedFilter.value = 'all'; sortMode.value = 'trend'
}
function choosePlazaCard(card: SingleCardPickerItem) {
  cardFilter.value = card.cardId
  cardPickerOpen.value = false
}
function resetMineFilters() {
  mineQuery.value = ''; mineHomeCityFilter.value = 'all'; mineLegalFilter.value = 'all'; mineSort.value = 'latest'
}
function handleHotDeckFocusOut(event: FocusEvent, rowIndex: number) {
  const next = event.relatedTarget
  if (!(next instanceof Node) || !(event.currentTarget as HTMLElement).contains(next)) hotDeckFocusPaused.value[rowIndex] = false
}
function handleHotDeckPointerDown(event: PointerEvent, rowIndex: number) {
  if (event.pointerType === 'touch') hotDeckTouchPaused.value[rowIndex] = true
}
function handleHotDeckPointerEnd(event: PointerEvent, rowIndex: number) {
  if (event.pointerType === 'touch') hotDeckTouchPaused.value[rowIndex] = false
}
function pageItems(total: number, current: number) {
  const pages = total <= 7
    ? Array.from({ length: total }, (_, index) => index + 1)
    : [...new Set([1, total, current - 1, current, current + 1].filter(value => value >= 1 && value <= total))].sort((a, b) => a - b)
  const items: Array<{ key: string; label: string; page?: number }> = []
  pages.forEach((page, index) => {
    if (index && page - pages[index - 1] > 1) items.push({ key: `gap-${page}`, label: '…' })
    items.push({ key: `page-${page}`, label: String(page), page })
  })
  return items
}
function addPublishMatchup() {
  const selected = new Set(publishMatchups.value.map(row => row.opponentMasterId))
  const opponentMasterId = publishHomeCities.value.find(card => !selected.has(card.id))?.id ?? ''
  publishMatchups.value.push({ opponentMasterId, notes: '', keyCards: '', suggestedSwaps: '' })
}
function routeValue(key: string, fallback = '') {
  const value = route.query[key]
  return typeof value === 'string' ? value : fallback
}
function restoreFiltersFromRoute() {
  const requestedTab = routeValue('tab', 'plaza')
  tab.value = requestedTab === 'mine' ? 'mine' : 'plaza'
  query.value = routeValue('q')
  masterFilter.value = routeValue('master', 'all')
  factionFilter.value = routeValue('faction', 'all')
  legalFilter.value = ['legal', 'illegal'].includes(routeValue('legal')) ? routeValue('legal') as 'legal' | 'illegal' : 'all'
  environmentFilter.value = ['1.0', '2.0', '2.5'].includes(routeValue('environment')) ? routeValue('environment') as DeckEnvironmentFilter : 'all'
  cardFilter.value = routeValue('card')
  updatedFilter.value = ['1', '7', '30', '90', '365'].includes(routeValue('updated')) ? routeValue('updated') as typeof updatedFilter.value : 'all'
  sortMode.value = ['copies', 'likes', 'views', 'latest', 'name'].includes(routeValue('sort')) ? routeValue('sort') as typeof sortMode.value : 'trend'
}
function setQueryValue(next: Record<string, string>, key: string, value: string, fallback = '') {
  if (value && value !== fallback) next[key] = value
  else delete next[key]
}
watch([tab, query, masterFilter, factionFilter, legalFilter, environmentFilter, cardFilter, updatedFilter, sortMode], () => {
  const next: Record<string, string> = {}
  setQueryValue(next, 'tab', tab.value, 'plaza')
  setQueryValue(next, 'q', query.value.trim())
  setQueryValue(next, 'master', masterFilter.value, 'all')
  setQueryValue(next, 'faction', factionFilter.value, 'all')
  setQueryValue(next, 'legal', legalFilter.value, 'all')
  setQueryValue(next, 'environment', environmentFilter.value, 'all')
  setQueryValue(next, 'card', cardFilter.value)
  setQueryValue(next, 'updated', updatedFilter.value, 'all')
  setQueryValue(next, 'sort', sortMode.value, 'trend')
  const current = new URLSearchParams(Object.entries(route.query).flatMap(([key, value]) => typeof value === 'string' ? [[key, value]] : [])).toString()
  const target = new URLSearchParams(next).toString()
  if (current !== target) void router.replace({ path: '/decks', query: next })
})
watch(() => route.query, restoreFiltersFromRoute, { deep: true })
watch([mineQuery, mineHomeCityFilter, mineLegalFilter, mineSort], () => {
  mineBodyReadSequence++
  if (minePage.value !== 1) minePage.value = 1
  else if (libraryMounted && platformState.account) void loadMineDirectory()
})
watch(minePage, () => {
  mineBodyReadSequence++
  if (libraryMounted && platformState.account) void loadMineDirectory()
})
watch(tab, () => {
  libraryCounterDocumentEpoch++
  mineBodyReadSequence++
  if (libraryMounted && platformState.account && tab.value === 'mine') void loadMineDirectory()
}, { flush: 'sync' })
watch([query, masterFilter, factionFilter, legalFilter, environmentFilter, cardFilter, updatedFilter, sortMode], () => { plazaPage.value = 1 })
watch(minePageCount, total => { minePage.value = Math.min(minePage.value, total) }, { immediate: true })
watch(plazaPageCount, total => {
  if (publicLoadState.value === 'available') plazaPage.value = Math.min(plazaPage.value, total)
}, { immediate: true })
</script>

<template>
  <div ref="pageRoot" class="deck-page" :aria-busy="actionBusy">
    <header class="page-head"><div><small>DECKS</small><h1>牌库管理</h1><p>构筑、保存、分享并发现公开牌库。</p></div><router-link :to="editorLink()">＋ 新建牌库</router-link></header>
    <div class="deck-tabs"><button :class="{ active: tab === 'plaza' }" @click="tab = 'plaza'">公开牌库</button><button :class="{ active: tab === 'mine' }" @click="tab = 'mine'">我的牌库</button></div>
    <p v-if="notice" class="deck-notice">{{ notice }}</p>

    <template v-if="tab === 'mine'">
      <p v-if="referenceState === 'unavailable'" role="status">{{ referenceError || '公开牌库状态暂时无法确认，请刷新后重试' }}</p>
      <section class="mine-toolbar"><input v-model="mineQuery" type="search" placeholder="按牌库名称搜索"/><select v-model="mineHomeCityFilter" aria-label="按主宰筛选"><option value="all">全部主宰</option><option v-for="city in homeCities" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select><select v-model="mineLegalFilter" aria-label="按合法性筛选"><option value="all">全部合法性</option><option value="legal">构筑合法</option><option value="illegal">构筑不合法</option></select><select v-model="mineSort" aria-label="我的牌库排序"><option value="latest">最近更新</option><option value="name">按名称</option></select><span>{{ mineTotal }} 个结果</span><button v-if="mineFilterActive" @click="resetMineFilters">清除筛选</button><button ref="importTrigger" type="button" @click="openImportModal">导入牌库码</button><button type="button" :disabled="!mineTotal" @click="showPublish = true">公开牌库</button></section>
      <section v-if="pagedMine.length" class="mine-grid"><article v-for="deck in pagedMine" :key="`${deck.id}:${deck.revision}`"><DeckProfile :master-id="deck.masterId" :master-name="byId.get(deck.masterId)?.nameZh" :fallback-url="byId.get(deck.masterId)?.imageUrl" :name="deck.name" :meta="mineCountLabel(deck)"/><small v-if="publishedCopyFor(deck)" class="mine-public-state">已公开 · 删除本地牌库不会删除公开版本</small><div class="deck-card-actions"><button :disabled="actionPending(mineActionKey('edit',deck))" @click="editMine(deck)">编辑</button><details :open="desktopActions"><summary>更多操作</summary><div><button :disabled="actionPending(mineActionKey('duplicate',deck))" @click="duplicateMine(deck)">复制牌库</button><button :disabled="actionPending(mineActionKey('copy-code',deck))" @click="copyMineCode(deck)">复制牌库码</button><button :disabled="actionPending(mineActionKey('image',deck))" @click="previewMineImage(deck)">生成牌库图</button><button class="danger" :disabled="deletingMine === deck.id || actionPending(mineActionKey('delete',deck))" @click="deleteMine(deck)">{{ deletingMine === deck.id ? '删除中…' : '删除' }}</button></div></details></div></article></section>
      <nav v-if="mineTotal && minePageCount > 1" class="deck-pagination" aria-label="我的牌库分页"><button :disabled="minePage === 1" @click="minePage--">上一页</button><template v-for="item in pageItems(minePageCount,minePage)" :key="item.key"><button v-if="item.page" :class="{ active: item.page === minePage }" :aria-current="item.page === minePage ? 'page' : undefined" @click="minePage = item.page">{{ item.label }}</button><span v-else>{{ item.label }}</span></template><button :disabled="minePage === minePageCount" @click="minePage++">下一页</button></nav>
      <div v-if="platformState.account && privateLoadState === 'unavailable'" class="empty-state"><b>牌库目录暂不可读取</b><p>{{ privateLoadError }}</p><button @click="loadMineDirectory">重新读取</button></div>
      <div v-else-if="platformState.account && privateLoadState === 'loading' && !pagedMine.length" class="empty-state"><b>正在读取牌库目录</b><p>这里只加载名称、计数和状态。</p></div>
      <div v-else-if="!mineTotal && mineFilterActive" class="empty-state"><b>没有符合筛选条件的牌库</b><p>调整名称、主宰或合法性筛选后再试。</p><button @click="resetMineFilters">清除筛选</button></div>
      <div v-else-if="!mineTotal" class="empty-state"><b>还没有自定义牌库</b><p>从编辑器新建牌库，或使用上方按钮导入其他玩家分享的牌库码。</p><router-link :to="editorLink()">打开牌库编辑器</router-link></div>
    </template>

    <template v-else>
      <p v-if="publicLoadState === 'loading'" role="status">正在读取牌库目录…</p>
      <p v-else-if="publicLoadState === 'unavailable'" role="alert">{{ publicLoadError }}</p>
      <p v-else-if="summaryPage?.sourceAvailability.public === 'disabled'" role="status">公开牌库暂未开放，当前显示官方预组。</p>
      <section class="plaza-toolbar"><input v-model="query" placeholder="搜索牌库名称、作者或主宰"/><MobileFilterSheet v-model="plazaFiltersOpen" title="牌库筛选与排序" :active-count="plazaFilterCount" @reset="resetPlazaFilters"><div class="plaza-filter-fields"><label>主宰<select v-model="masterFilter"><option value="all">全部主宰</option><option v-for="city in plazaMasters" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select></label><label>阵营<select v-model="factionFilter"><option value="all">全部阵营</option><option v-for="faction in plazaFactions" :key="faction" :value="faction">{{ factionLabels[faction] || faction }}</option></select></label><label>赛季合法性<select v-model="legalFilter"><option value="all">全部</option><option value="legal">符合本赛季</option><option value="illegal">不符合本赛季</option></select></label><label>环境<select v-model="environmentFilter"><option value="all">全部环境</option><option value="1.0">1.0</option><option value="2.0">2.0</option><option value="2.5">2.5</option></select></label><label>包含卡牌<button type="button" class="card-picker-button" @click="cardPickerOpen = true">{{ cardFilter ? `${byId.get(cardFilter)?.nameZh || cardFilter} · ${byId.get(cardFilter)?.number || ''}` : '选择单卡' }}</button></label><label>更新时间<select v-model="updatedFilter"><option value="all">不限时间</option><option value="1">1天内</option><option value="7">7天内</option><option value="30">30天内</option><option value="90">90天内</option><option value="365">365天内</option></select></label><label>排序<select v-model="sortMode"><option value="trend">综合热度</option><option value="copies">最多复制</option><option value="likes">最多点赞</option><option value="views">最多浏览</option><option value="latest">最新发布</option><option value="name">按名称</option></select></label></div><template #apply-label>查看 {{ plazaTotal }} 个牌库</template></MobileFilterSheet><div class="plaza-desktop-filters"><select v-model="masterFilter" aria-label="按主宰筛选"><option value="all">全部主宰</option><option v-for="city in plazaMasters" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select><select v-model="factionFilter" aria-label="按阵营筛选"><option value="all">全部阵营</option><option v-for="faction in plazaFactions" :key="faction" :value="faction">{{ factionLabels[faction] || faction }}</option></select><select v-model="legalFilter" aria-label="按合法性筛选"><option value="all">全部合法性</option><option value="legal">符合本赛季</option><option value="illegal">不符合本赛季</option></select><select v-model="environmentFilter" aria-label="按环境筛选"><option value="all">全部环境</option><option value="1.0">环境 1.0</option><option value="2.0">环境 2.0</option><option value="2.5">环境 2.5</option></select><button type="button" class="card-picker-button" @click="cardPickerOpen = true">{{ cardFilter ? `含：${byId.get(cardFilter)?.nameZh || cardFilter}` : '选择包含卡牌' }}</button><button v-if="cardFilter" type="button" class="card-filter-clear" @click="cardFilter = ''">清除单卡</button><select v-model="updatedFilter" aria-label="按更新时间筛选"><option value="all">不限时间</option><option value="1">1天内</option><option value="7">7天内</option><option value="30">30天内</option><option value="90">90天内</option><option value="365">365天内</option></select><select v-model="sortMode" aria-label="排序"><option value="trend">综合热度</option><option value="copies">最多复制</option><option value="likes">最多点赞</option><option value="views">最多浏览</option><option value="latest">最新发布</option><option value="name">按名称</option></select></div><button :disabled="!mineTotal" @click="showPublish = true">发布我的牌库</button></section>
      <button v-if="plazaFilterCount" type="button" class="plaza-filter-summary" @click="plazaFiltersOpen = true">{{ plazaFilterSummary }}</button>
      <section v-if="hotDeckRows.length" class="hot-decks" aria-labelledby="hot-decks-title">
        <header><h2 id="hot-decks-title">热门牌库</h2></header>
        <div v-for="(row,rowIndex) in hotDeckRows" :key="rowIndex" class="hot-deck-viewport" :class="{ paused: hotDeckRowPaused(rowIndex) }" :data-hot-row-viewport="rowIndex + 1" @mouseenter="hotDeckHoverPaused[rowIndex] = true" @mouseleave="hotDeckHoverPaused[rowIndex] = false" @focusin="hotDeckFocusPaused[rowIndex] = true" @focusout="handleHotDeckFocusOut($event,rowIndex)" @pointerdown="handleHotDeckPointerDown($event,rowIndex)" @pointerup="handleHotDeckPointerEnd($event,rowIndex)" @pointercancel="handleHotDeckPointerEnd($event,rowIndex)">
          <div class="hot-deck-track" :class="`row-${rowIndex + 1}`" :data-hot-row-track="rowIndex + 1">
            <div v-for="copyIndex in 2" :key="copyIndex" class="hot-deck-loop" :aria-hidden="copyIndex === 2 ? 'true' : undefined">
              <button v-for="entry in row" :key="`${copyIndex}-${entry.id}`" type="button" :tabindex="copyIndex === 2 ? -1 : 0" :data-hot-row="rowIndex + 1" :data-hot-copy="copyIndex" :data-public-code="publicDeckSummaryReference(entry)" :aria-label="`查看热门牌库《${entry.name}》`" @click="openDeck(entry)">
                <DeckProfile compact :master-id="entry.masterId" :master-name="entry.masterName" :fallback-url="byId.get(entry.masterId)?.imageUrl" :name="entry.name" :context="entry.author" :meta="`浏览 ${entry.views ?? 0} · 点赞 ${entry.likes} · 复制 ${entry.copies}`"/>
                <span class="deck-environment-badge">{{ deckEnvironmentLabel(entry.environment.value ?? 'pending') }}</span>
              </button>
            </div>
          </div>
        </div>
      </section>
      <div class="plaza-result-line"><b>{{ plazaTotal }}</b> 个牌库<span v-if="plazaFilterCount"> · 已启用 {{ plazaFilterCount }} 项筛选</span><button v-if="plazaFilterCount" @click="resetPlazaFilters">清除筛选</button></div>
      <section class="plaza-grid"><article v-for="entry in pagedPublished" :key="entry.id" :class="`faction-${deckFaction(entry)}`"><button class="plaza-summary" @click="openDeck(entry)"><DeckProfile :master-id="entry.masterId" :master-name="entry.masterName" :fallback-url="byId.get(entry.masterId)?.imageUrl" :name="entry.name" :context="entry.author" :meta="`${summaryCountLabel(entry)} 主牌 · ${entry.counts.morale} 士气`"/><span class="deck-environment-badge">{{ deckEnvironmentLabel(entry.environment.value ?? 'pending') }}</span></button><footer><div class="plaza-card-stats"><span>浏览量 {{ entry.views ?? 0 }}</span><span>点赞 {{ entry.likes }}</span><span>复制 {{ entry.copies }}</span></div><span class="season-compliance" :class="{ compliant: seasonRequirement(entry).compliant }" :title="seasonRequirement(entry).reason">{{ seasonRequirement(entry).label }}</span><div class="plaza-card-actions"><button @click="openDeck(entry)">查看构筑</button><details class="deck-actions-menu" :open="desktopActions"><summary>更多操作</summary><div><button :class="{ liked: entry.viewerLiked }" :disabled="entry.source === 'official' || actionPending(publicDeckActionKey(entry.id))" @click="toggleLike(entry)">♡ {{ entry.viewerLiked ? '取消点赞' : '点赞' }}</button></div></details></div></footer></article></section>
      <nav v-if="plazaTotal && plazaPageCount > 1" class="deck-pagination" aria-label="公开牌库分页"><button :disabled="plazaPage === 1" @click="plazaPage--">上一页</button><template v-for="item in pageItems(plazaPageCount,plazaPage)" :key="item.key"><button v-if="item.page" :class="{ active: item.page === plazaPage }" :aria-current="item.page === plazaPage ? 'page' : undefined" @click="plazaPage = item.page">{{ item.label }}</button><span v-else>{{ item.label }}</span></template><button :disabled="plazaPage === plazaPageCount" @click="plazaPage++">下一页</button></nav>
      <div v-if="publicLoadState === 'available' && !plazaTotal" class="empty-state"><b>{{ plazaTotal ? '没有符合筛选条件的公开牌库' : '还没有公开牌库' }}</b><p v-if="plazaTotal">调整筛选条件后再试。</p><button v-if="plazaFilterCount" @click="resetPlazaFilters">清除筛选</button></div>
    </template>
    <div v-if="showImport" class="modal-mask" @click.self="closeImportModal" @keydown="handleImportDialogKeydown"><section class="import-modal" role="dialog" aria-modal="true" aria-labelledby="import-deck-title"><header><h2 id="import-deck-title">导入牌库码</h2><button type="button" aria-label="关闭导入牌库码" @click="closeImportModal">×</button></header><form @submit.prevent="importFromCode"><label for="deck-import-code">牌库码</label><input id="deck-import-code" ref="importInput" v-model="importCode" autocomplete="off" placeholder="粘贴 L12D2 开头的牌库码"/><p v-if="importError" class="import-error" role="alert">{{ importError }}</p><footer><button type="button" @click="closeImportModal">取消</button><button class="primary" type="submit" :disabled="importBusy">确认导入</button></footer></form></section></div>
    <div v-if="showPublish" class="modal-mask" @click.self="showPublish = false"><section class="publish-modal"><header><h2>公开牌库</h2><button @click="showPublish = false">×</button></header><div class="publish-form"><p>选择一个已保存且合法的牌库；可在首次发布时同步填写公开内容。</p><select v-model="publishName"><option value="">选择牌库</option><option v-for="deck in mine" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><h3>牌库指南</h3><label>构筑思路<textarea v-model="publishGuide.buildIdea" rows="3" maxlength="1200"/></label><label>起手建议<textarea v-model="publishGuide.opening" rows="2" maxlength="1200"/></label><label>关键牌与配合<textarea v-model="publishGuide.keyCards" rows="2" maxlength="1200"/></label><label>常见展开<textarea v-model="publishGuide.commonSequence" rows="2" maxlength="1200"/></label><label>替换建议<textarea v-model="publishGuide.substitutions" rows="2" maxlength="1200"/></label><section class="publish-matchups"><header><h3>对局建议</h3><button type="button" @click="addPublishMatchup">添加主宰</button></header><article v-for="(row,index) in publishMatchups" :key="`${row.opponentMasterId}-${index}`"><label>对方主宰<select v-model="row.opponentMasterId"><option value="" disabled>请选择</option><option v-for="city in publishHomeCities" :key="city.id" :value="city.id">{{ city.nameZh }}</option></select></label><label>对局思路<textarea v-model="row.notes" rows="2" maxlength="800"/></label><label>关键牌<textarea v-model="row.keyCards" rows="2" maxlength="800"/></label><label>建议换牌<textarea v-model="row.suggestedSwaps" rows="2" maxlength="800"/></label><button type="button" class="danger" @click="publishMatchups.splice(index,1)">移除</button></article></section></div><button class="primary" :disabled="!publishName || !platformState.account || actionPending(`public-deck:publish:${publishName}`)" @click="publishDeck">{{ actionPending(`public-deck:publish:${publishName}`) ? '公开中…' : '确认公开' }}</button></section></div>
    <div v-if="imagePreview" class="modal-mask image-mask" @click.self="closeImagePreview"><section class="image-preview"><header><div><small>16:9 牌库分享图</small><h2>{{ imagePreview.deck.name }} · 牌库图</h2></div><button @click="closeImagePreview">×</button></header><img :src="imagePreview.url" alt="牌库图预览"/><footer><button @click="copyPreviewImage">复制图片</button><button class="primary" @click="downloadDeckImage(imagePreview.deck,catalog,imagePreview.blob)">下载 PNG</button></footer></section></div>
    <SingleCardPicker v-if="cardPickerOpen" title="选择公开牌库必须包含的卡牌" :items="plazaCardPickerItems" @select="choosePlazaCard" @close="cardPickerOpen = false"/>
  </div>
</template>

<style scoped>
.deck-page{min-height:100%;padding:28px clamp(18px,3vw,48px) 58px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;margin-bottom:18px}.page-head small{color:#51c3cb;font:900 14px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0 3px;font-size:30px}.page-head p{margin:0;color:#77858c;font-size:14px}.page-head>a{padding:12px 18px;border:1px solid #e2c474;background:#e2c474;color:#0a0e10;font-weight:900;text-decoration:none}.deck-tabs{display:grid;grid-template-columns:1fr 1fr;margin-bottom:14px;border:1px solid #35434c;background:#0b1117}.deck-tabs button{padding:13px;border:0;background:transparent;color:#76848c;font-weight:900}.deck-tabs button.active{background:linear-gradient(135deg,#7c1724,#ad2d39);color:#fff}.import-panel,.plaza-toolbar{display:grid;grid-template-columns:1fr auto auto;gap:8px;margin-bottom:14px;padding:12px;border:1px solid #35434c;background:#101820}.import-panel input,.plaza-toolbar input,.publish-modal select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.import-panel button,.plaza-toolbar button{padding:0 15px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.mine-grid,.plaza-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.mine-grid>article,.plaza-grid>article{overflow:hidden;border:1px solid rgba(235,230,216,.16);background:#101821}.mine-grid>article{padding:14px}.deck-banner{position:relative;height:112px;overflow:hidden;background:linear-gradient(120deg,#1d5860,#5c1723)}.deck-banner .l12-card-image{width:100%;height:100%;filter:saturate(.8) brightness(.72)}.deck-banner::after{content:'';position:absolute;inset:0;background:linear-gradient(90deg,rgba(5,8,10,.1),rgba(5,8,10,.85))}.deck-banner span{position:absolute;z-index:2;right:13px;bottom:12px;font-size:17px;font-weight:900}.mine-grid h2{margin:13px 0 4px;font-size:17px}.mine-grid p{margin:0;color:#78868c;font-size:14px}.mine-grid>article>div:last-child{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.mine-grid a,.mine-grid button{padding:7px 9px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-size:14px;font-weight:900;text-decoration:none}.plaza-summary{display:block;width:100%;padding:12px;border:0;background:transparent;color:#fff;text-align:left}.banner-strip{display:flex;height:82px;overflow:hidden;background:#080e12}.banner-strip .l12-card-image{width:20%;height:100%}.plaza-summary h2{margin:11px 0 4px;font-size:16px}.plaza-summary p,.plaza-summary span{display:block;margin:0;color:#77858b;font-size:14px}.plaza-grid footer{display:flex;align-items:center;justify-content:space-between;padding:9px 12px;border-top:1px solid rgba(235,230,216,.1)}.plaza-grid footer button{border:0;background:transparent;color:#93a0a5;font-size:14px;font-weight:900}.plaza-grid footer button.liked{color:#df6672}.plaza-grid footer span{color:#65727a;font-size:14px}.empty-state{display:grid;min-height:360px;place-items:center;align-content:center;border:1px dashed #34414a;color:#738089;text-align:center}.empty-state b{color:#aab2b4}.empty-state p{font-size:14px}.empty-state a{padding:10px 14px;border:1px solid #57c2c9;color:#78dbe1;font-size:14px;text-decoration:none}.deck-notice{position:fixed;z-index:80;right:18px;bottom:18px;max-width:420px;padding:11px 14px;border:1px solid #d5b45f;background:#251b08;color:#f4d980;font-size:14px;font-weight:900}.modal-mask{position:fixed;z-index:90;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.78);backdrop-filter:blur(8px)}.deck-detail{display:flex;width:min(1040px,95vw);max-height:90vh;flex-direction:column;overflow:hidden;border:1px solid #52606a;background:#111923}.deck-detail>header,.publish-modal header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.deck-detail header small{color:#52c4cb;font-size:14px}.deck-detail h2{margin:5px 0;font-size:24px}.deck-detail header p{margin:0;color:#7c898f;font-size:14px}.deck-detail header button,.publish-modal header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.detail-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:10px;padding:20px;overflow:auto}.detail-grid article{position:relative;min-width:0}.detail-grid .l12-card-image{display:block;width:100%;aspect-ratio:5/7;background:#070b0f}.detail-grid article>b{position:absolute;right:4px;top:4px;display:grid;width:26px;height:26px;place-items:center;border-radius:50%;background:#e0bf6d;color:#080b0d}.detail-grid span{display:block;margin-top:4px;overflow:hidden;font-size:14px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}.deck-detail>footer{display:flex;justify-content:flex-end;gap:8px;padding:16px 20px;border-top:1px solid #354149}.deck-detail>footer button,.publish-modal>.primary{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.primary{border-color:#e0bf6d!important;background:#e0bf6d!important;color:#090c0e!important}.publish-modal{width:min(500px,94vw);padding-bottom:20px;border:1px solid #52606a;background:#111923}.publish-modal h2{margin:0}.publish-modal p{padding:0 20px;color:#849097;font-size:14px;line-height:1.7}.publish-modal select{width:calc(100% - 40px);margin:0 20px 12px}.publish-modal>.primary{display:block;margin:0 20px 0 auto}
@media(max-width:1050px){.mine-grid,.plaza-grid{grid-template-columns:1fr 1fr}}
@media(max-width:700px){.deck-page{padding:18px 12px 48px}.page-head{align-items:flex-start;flex-direction:column;gap:12px}.import-panel,.plaza-toolbar{grid-template-columns:1fr}.import-panel button,.plaza-toolbar button{padding:11px}.mine-grid,.plaza-grid{grid-template-columns:1fr}.detail-grid{grid-template-columns:repeat(3,1fr)}.deck-detail>footer{flex-wrap:wrap}.deck-detail>footer button{flex:1 1 40%}}
.plaza-toolbar{grid-template-columns:minmax(220px,1fr) 135px 145px auto auto}.plaza-toolbar select{padding:11px;border:1px solid #46545d;background:#070d12;color:#fff}.deck-notice{z-index:100}.deck-detail{width:min(1180px,95vw)}.deck-detail>header,.image-preview header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px;border-bottom:1px solid #354149}.image-preview header small{color:#52c4cb;font-size:14px}.image-preview h2{margin:5px 0;font-size:24px}.image-preview header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.deck-analysis{display:grid;grid-template-columns:230px 1fr;min-height:0;overflow:hidden}.deck-analysis>aside{display:grid;align-content:start;gap:12px;padding:20px;border-right:1px solid #354149;overflow:auto}.deck-analysis>aside>section{padding:12px;border:1px solid #334049;background:#0b1218}.deck-analysis>aside>section>b{font-size:14px}.detail-master{display:flex;gap:10px;align-items:center}.detail-master .l12-card-image{width:72px;aspect-ratio:5/7}.detail-master div{display:grid;gap:4px}.detail-master small,.detail-master span{color:#78868c;font-size:14px}.detail-curve{display:flex;height:92px;align-items:end;gap:3px;margin-top:8px}.detail-curve i{display:grid;flex:1;align-items:end;justify-items:center;font-style:normal}.detail-curve i>span{width:100%;max-width:16px;background:linear-gradient(#e1bf6d,#8c6a29)}.detail-curve small,.detail-curve em{font-size:14px;font-style:normal}.detail-curve em{color:#89959a}.deck-analysis>aside>section>p{display:flex;justify-content:space-between;margin:7px 0;color:#89959a;font-size:14px}.deck-analysis>aside>section>p strong{color:#e8e4da}.detail-grid article>b{width:auto;min-width:28px;padding:0 4px}.detail-grid article>small{display:block;margin-top:3px;overflow:hidden;color:#77858c;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.image-mask{z-index:95}.image-preview{width:min(1200px,96vw);max-height:94vh;border:1px solid #52606a;background:#111923}.image-preview>img{display:block;width:100%;max-height:74vh;object-fit:contain;background:#05080a}.image-preview footer{display:flex;justify-content:flex-end;gap:8px;padding:14px 20px;border-top:1px solid #354149}.image-preview footer button{padding:10px 13px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}
.deck-analysis>.construction-browser{min-width:0;padding:20px;overflow:hidden}
.deck-detail>footer .danger{border-color:#9e3944;background:#4d171d;color:#ffdce0}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:1fr}.deck-analysis{grid-template-columns:1fr;overflow:auto}.deck-analysis>aside{border-right:0;border-bottom:1px solid #354149}.deck-analysis>.construction-browser{padding:14px}}
.mine-grid>article>:deep(.deck-profile){border:0;background:transparent}.plaza-summary>:deep(.deck-profile){border:0;background:transparent}.deck-analysis>aside>:deep(.deck-profile){width:100%}
.plaza-grid footer{--deck-faction:72,84,91;display:grid;grid-template-columns:auto auto auto minmax(112px,1fr) auto;align-items:center;gap:8px;padding:10px 12px;border-top-color:rgba(var(--deck-faction),.48);background:linear-gradient(90deg,rgba(var(--deck-faction),.2),rgba(var(--deck-faction),.08))}.plaza-grid .faction-tianting footer{--deck-faction:34,105,113}.plaza-grid .faction-taiyangcheng footer{--deck-faction:126,91,28}.plaza-grid .faction-asgard footer{--deck-faction:44,79,122}.plaza-grid .faction-gaotianyuan footer{--deck-faction:128,43,54}.plaza-grid .faction-olympus footer{--deck-faction:86,56,126}.plaza-grid .faction-otherworld footer,.plaza-grid .faction-bijie footer{--deck-faction:35,112,83}.plaza-grid footer button{color:#d2d9d8;font-size:14px}.plaza-grid footer button.liked{color:#ff8995}.plaza-grid footer span{color:#c7cecd;font-size:14px;white-space:nowrap}.plaza-grid footer .season-compliance{justify-self:end;color:#f0a9ad;font-weight:900}.plaza-grid footer .season-compliance.compliant{color:#a4e4c8}
@media(max-width:700px){.plaza-grid footer{grid-template-columns:repeat(3,auto);justify-content:space-between}.plaza-grid footer .season-compliance{grid-column:1/3;justify-self:start}}
.plaza-filter-fields{display:grid;gap:14px}.plaza-filter-fields label{display:grid;gap:6px;color:#b9c2c4;font-size:13px;font-weight:900}.plaza-filter-fields select{width:100%;padding:10px;border:1px solid #46545d;background:#070d12;color:#fff}.plaza-desktop-filters{display:contents}.season-only{display:flex!important;align-items:center;gap:7px!important;color:#c9d0d0;font-size:13px;font-weight:900;white-space:nowrap}.season-only input{width:17px;height:17px;min-height:0;padding:0;accent-color:#d1ad50}.plaza-filter-summary{display:none}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:minmax(0,1fr) auto!important;align-items:stretch}.plaza-toolbar>input{min-width:0}.plaza-desktop-filters{display:none}.plaza-toolbar>button:last-child{grid-column:1/-1;min-height:44px}.plaza-filter-summary{display:block;width:100%;margin:-6px 0 12px;padding:8px 10px;border:1px solid #52636a;background:#101a20;color:#c7d8d6;font-size:12px;font-weight:800;text-align:left}.deck-page{overflow-x:clip}.plaza-grid footer{row-gap:7px}.deck-detail,.image-preview{width:100%;max-height:100dvh;border:0}.deck-detail>header,.image-preview header{padding:14px}.deck-detail>footer,.image-preview footer{padding:12px;gap:7px}.deck-detail h2,.image-preview h2{font-size:20px}}
@media(max-width:520px){.deck-page{padding:14px 12px 42px}.page-head{gap:8px;margin-bottom:12px}.page-head small{font-size:11px}.page-head h1{font-size:25px}.page-head p{font-size:12px}.page-head>a{padding:9px 12px;font-size:13px}.deck-tabs{margin-bottom:10px}.deck-tabs button{min-height:44px;padding:9px;font-size:13px}.deck-notice{position:static;max-width:none;margin:0 0 10px;padding:9px 10px;font-size:12px;box-shadow:none}.import-panel,.plaza-toolbar{gap:7px;margin-bottom:10px;padding:9px}.import-panel input,.plaza-toolbar input{padding:10px;font-size:12px}.import-panel button,.plaza-toolbar button{min-height:44px;padding:9px 11px;font-size:13px}.mine-grid,.plaza-grid{gap:9px}.mine-grid>article{padding:11px}.deck-banner{height:96px}.mine-grid h2,.plaza-summary h2{font-size:15px}.mine-grid p,.plaza-summary p,.plaza-summary span,.plaza-grid footer button,.plaza-grid footer span{font-size:12px}.empty-state{min-height:280px}.empty-state p,.empty-state a{font-size:12px}.plaza-filter-summary{margin:-3px 0 9px}}
.plaza-toolbar{grid-template-columns:minmax(220px,1fr) repeat(7,minmax(108px,auto)) auto}.plaza-result-line{display:flex;align-items:center;gap:5px;margin:-4px 0 12px;color:#7d8a8f;font-size:13px}.plaza-result-line b{color:#e8e4da}.plaza-result-line button{margin-left:auto;border:0;background:transparent;color:#75cdd2;font-weight:900}.plaza-filter-fields select{color-scheme:dark}
.mine-public-state{display:block;margin-top:8px;color:#79cfc9;font-size:12px}.mine-grid button.danger{border-color:#8f3d47;background:#2e1519;color:#f2a4ac}
@media(max-width:1180px) and (min-width:701px){.plaza-toolbar{grid-template-columns:minmax(220px,1fr) repeat(3,minmax(110px,1fr))}.plaza-toolbar>button:last-child{grid-column:4}.plaza-desktop-filters{display:contents}}
@media(max-width:700px){.plaza-toolbar{grid-template-columns:minmax(0,1fr) auto!important}.plaza-result-line{font-size:12px}}
.mine-toolbar{display:grid;grid-template-columns:minmax(220px,1fr) repeat(3,minmax(120px,auto)) auto auto;align-items:center;gap:8px;margin-bottom:14px;padding:12px;border:1px solid #35434c;background:#101820}.mine-toolbar input,.mine-toolbar select{min-width:0;padding:10px;border:1px solid #46545d;background:#070d12;color:#fff}.mine-toolbar span{color:#a7b0b1;white-space:nowrap}.mine-toolbar button,.empty-state button,.card-picker-button,.card-filter-clear{min-height:40px;padding:8px 11px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.deck-card-actions{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.deck-card-actions details,.deck-actions-menu{display:contents}.deck-card-actions summary,.deck-actions-menu summary{display:none}.deck-card-actions details>div,.deck-actions-menu>div{display:flex;gap:6px}.publish-modal{display:grid;width:min(860px,96vw);max-height:92dvh;grid-template-rows:auto minmax(0,1fr) auto;overflow:hidden}.publish-form{display:grid;gap:10px;overflow:auto;padding:0 20px 16px}.publish-form>p{padding:0}.publish-form h3{margin:8px 0 0}.publish-form label{display:grid;gap:5px;color:#c3cccb;font-weight:800}.publish-form textarea,.publish-form select{box-sizing:border-box;width:100%;padding:9px;border:1px solid #46545d;background:#070d12;color:#fff;font:inherit;resize:vertical}.publish-modal>.primary{margin:12px 20px 0 auto}.publish-matchups{display:grid;gap:10px}.publish-matchups>header{display:flex;align-items:center;justify-content:space-between}.publish-matchups>header button,.publish-matchups article>button{min-height:38px;padding:7px 10px;border:1px solid #5b6870;background:#16212a;color:#fff;font-weight:900}.publish-matchups article{display:grid;grid-template-columns:minmax(140px,.8fr) repeat(3,minmax(150px,1fr)) auto;align-items:end;gap:8px;padding:10px;border:1px solid #35434c;background:#0b1218}.publish-matchups .danger{border-color:#8f3d47;background:#2e1519}.plaza-filter-fields .card-picker-button{width:100%;overflow:hidden;text-align:left;text-overflow:ellipsis;white-space:nowrap}.plaza-desktop-filters .card-picker-button{min-width:130px}.card-filter-clear{padding-inline:9px}.empty-state button{margin-top:8px}
@media(max-width:900px){.mine-toolbar{grid-template-columns:repeat(2,minmax(0,1fr))}.mine-toolbar input{grid-column:1/-1}.mine-toolbar span{align-self:center}.publish-matchups article{grid-template-columns:1fr 1fr}.publish-matchups article>button{grid-column:1/-1}}
@media(max-width:700px){.mine-toolbar{grid-template-columns:1fr;padding:9px}.mine-toolbar input{grid-column:auto}.deck-card-actions{align-items:stretch}.deck-card-actions>a{flex:1}.deck-card-actions details,.deck-actions-menu{display:block;position:relative;flex:1}.deck-card-actions summary,.deck-actions-menu summary{display:grid;min-height:38px;padding:7px 9px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-size:13px;font-weight:900;list-style:none;place-items:center}.deck-card-actions details>div,.deck-actions-menu>div{display:none;position:absolute;z-index:8;right:0;bottom:calc(100% + 5px);min-width:160px;padding:6px;border:1px solid #53616a;background:#101820;box-shadow:0 12px 30px #000}.deck-card-actions details[open]>div,.deck-actions-menu[open]>div{display:grid}.deck-card-actions details>div button,.deck-actions-menu>div button{width:100%;min-height:40px}.plaza-grid footer{grid-template-columns:repeat(2,minmax(0,1fr))}.plaza-grid footer>span{white-space:normal}.plaza-grid footer>button,.deck-actions-menu{min-height:40px}.publish-modal{width:100%;max-height:100dvh;border:0}.publish-matchups article{grid-template-columns:1fr}}
@media(max-width:700px){.deck-card-actions details>div,.deck-actions-menu>div{position:static;box-sizing:border-box;min-width:0;width:100%;margin-top:5px}.deck-card-actions>a{align-self:start}}
.mine-toolbar{grid-template-columns:minmax(220px,1fr) repeat(3,minmax(120px,auto)) repeat(4,auto)}
.deck-pagination{display:flex;flex-wrap:wrap;align-items:center;justify-content:center;gap:6px;margin:16px 0}.deck-pagination button{min-width:40px;min-height:40px;padding:7px 10px;border:1px solid #4b5961;background:#0b1218;color:#e8e5dd;font-weight:900}.deck-pagination button.active{border-color:#e0bf6d;color:#f1d376}.deck-pagination button:disabled{cursor:not-allowed;opacity:.45}.deck-pagination span{padding:0 3px;color:#77858c}
.import-modal{width:min(560px,94vw);max-height:calc(100dvh - 24px);overflow:auto;border:1px solid #52606a;background:#111923}.import-modal>header{display:flex;align-items:center;justify-content:space-between;padding:18px 20px;border-bottom:1px solid #354149}.import-modal h2{margin:0}.import-modal>header button{width:34px;height:34px;border:1px solid #53616a;background:#0b1117;color:#fff}.import-modal form{display:grid;gap:9px;padding:20px}.import-modal label{color:#c3cccb;font-weight:900}.import-modal input{box-sizing:border-box;width:100%;padding:11px;border:1px solid #46545d;background:#070d12;color:#fff;font:inherit}.import-modal footer{display:flex;justify-content:flex-end;gap:8px;margin-top:8px}.import-modal footer button{min-height:40px;padding:8px 12px;border:1px solid #59666e;background:#15202a;color:#fff;font-weight:900}.import-error{margin:0;color:#f0a9ad;font-size:13px}
@media(max-width:900px){.mine-toolbar{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:700px){.mine-toolbar{grid-template-columns:1fr}.deck-pagination{gap:4px}.deck-pagination button{min-width:38px;padding-inline:8px}.import-modal{width:100%;max-height:100dvh;border:0}.import-modal footer button{flex:1}}
.hot-decks{margin:0 0 18px;padding:4px 0 0}.hot-decks>header{padding:0 0 10px}.hot-decks h2{margin:0;font-size:17px}.hot-deck-viewport{overflow:hidden}.hot-deck-viewport+.hot-deck-viewport{margin-top:8px}.hot-deck-track{display:flex;width:max-content;animation:none;will-change:transform}.hot-deck-track[data-motion-ready="true"]{animation:hot-decks-slide var(--hot-deck-duration) linear infinite}.hot-deck-track.row-2{animation-direction:reverse}.hot-deck-viewport.paused .hot-deck-track{animation-play-state:paused}.hot-deck-loop{display:flex;flex:none;gap:8px;padding-right:8px}.hot-deck-loop>button{position:relative;width:clamp(325px,26vw,429px);padding:0;border:0;background:transparent;color:inherit;text-align:left}.hot-deck-loop>button:focus-visible{outline:2px solid #e0bf6d;outline-offset:-2px}.hot-deck-loop :deep(.deck-profile){grid-template-columns:50px minmax(0,1fr);height:102px;box-sizing:border-box;gap:10px;padding:9px;background:#101821}.hot-deck-loop :deep(.deck-profile__portrait){width:50px}.hot-deck-loop :deep(.deck-profile__copy){display:flex;min-width:0;flex-direction:column;padding-right:84px}.hot-deck-loop :deep(.deck-profile__copy b){order:1}.hot-deck-loop :deep(.deck-profile__copy small){order:2;margin:3px 0 0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.hot-deck-loop :deep(.deck-profile__copy span){display:none}.hot-deck-loop :deep(.deck-profile__copy em){order:3}.plaza-grid>article{display:grid;grid-template-rows:auto 1fr}.plaza-summary{position:relative;padding:0}.plaza-summary>:deep(.deck-profile){min-height:102px;padding:14px;border:0;background:transparent}.plaza-summary :deep(.deck-profile__copy){display:flex;min-width:0;flex-direction:column;padding-right:84px}.plaza-summary :deep(.deck-profile__copy b){order:1;font-size:16px}.plaza-summary :deep(.deck-profile__copy small){order:2;margin:5px 0 0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.plaza-summary :deep(.deck-profile__copy span){order:3}.plaza-summary :deep(.deck-profile__copy em){order:4}.deck-environment-badge{position:absolute;z-index:1;top:9px;right:9px;display:inline-flex!important;min-height:22px;box-sizing:border-box;align-items:center;padding:3px 7px;border:1px solid #59666e;background:#111a21;color:#dce5e5!important;font-size:11px!important;font-weight:900;line-height:1;white-space:nowrap;pointer-events:none}.plaza-grid footer{grid-template-columns:minmax(0,1fr) auto auto}.plaza-card-stats,.plaza-card-actions{display:flex;align-items:center;gap:10px;min-width:0}.plaza-card-stats{flex-wrap:wrap}.plaza-card-actions{justify-content:flex-end}.mine-grid>article{display:grid;grid-template-rows:auto auto 1fr;padding:0}.mine-grid>article>:deep(.deck-profile){min-height:102px;padding:14px}.mine-public-state{margin:0;padding:0 14px 10px}.mine-grid>article>.deck-card-actions{align-self:end;margin:0;padding:10px 14px;border-top:1px solid rgba(235,230,216,.1);background:#0d151c}
@keyframes hot-decks-slide{from{transform:translateX(0)}to{transform:translateX(-50%)}}
@media(max-width:700px){.hot-decks{margin-bottom:14px;padding-top:2px}.hot-decks>header{padding-bottom:8px}.hot-decks h2{font-size:15px}.hot-deck-loop>button{width:230px}.hot-deck-loop :deep(.deck-profile){grid-template-columns:38px minmax(0,1fr);height:74px;gap:8px;padding:7px}.hot-deck-loop :deep(.deck-profile__portrait){width:38px}.plaza-grid footer{grid-template-columns:1fr auto}.plaza-card-stats{grid-column:1/-1}.plaza-grid footer .season-compliance{grid-column:auto;justify-self:start}.plaza-card-actions{min-height:40px}.mine-grid>article{padding:0}}
@media(prefers-reduced-motion:reduce){.hot-deck-viewport{overflow-x:auto;scrollbar-width:thin}.hot-deck-track{animation:none!important;transform:none!important}.hot-deck-loop[aria-hidden="true"]{display:none}}
</style>

