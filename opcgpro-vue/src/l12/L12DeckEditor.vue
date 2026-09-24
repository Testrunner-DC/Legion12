<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { masterProfileUrl } from './specialAssets'
import { compareDeckCards } from './deckOrdering'
import { createDeckImageBlob, downloadDeckImage } from './site/deckShare'
import { samplePublicDeckOpeningHand } from './site/publicDeckHands'
import {
  MAIN_DECK_TYPES, automaticExtraCardIdsForMaster, buildMoraleDeck, deckCountSummary, deleteDeck, doesNotCountTowardMainDeck, effectiveDeckLimit, ensureOfficialPrebuiltDecks, filterableCardCost, isDerivedSpecialCard, loadDeckCatalog, loadSavedDecks, trialCapacityForMaster,
  saveDeck, validateDeck, type DeckCard, type SavedL12Deck,
} from './decks'
import { alternateArtApi, getEffectiveOperationsPolicy, platformState, publicDeckApi, type AlternateArt, type OperationsCardRestriction } from './platform'
import CardImage from './CardImage.vue'
import CardDetailContent from './CardDetailContent.vue'
import DeckProfile from './DeckProfile.vue'
import MobileFilterSheet from './site/MobileFilterSheet.vue'
import PublicDeckContentEditor from './site/PublicDeckContentEditor.vue'

const router = useRouter()
const route = useRoute()
const returnTo = computed(() => typeof route.query.returnTo === 'string' && route.query.returnTo.startsWith('/') ? route.query.returnTo : '/decks')
const catalog = ref<DeckCard[]>([])
const savedDecks = ref<Record<string, SavedL12Deck>>({})
const loading = ref(true)
const notice = ref('')
const deckName = ref('新牌库')
const masterId = ref('')
const counts = ref<Record<string, number>>({})
const benchCounts = ref<Record<string, number>>({})
const query = ref('')
const typeFilter = ref('all')
const factionFilter = ref('all')
const productFilters = ref<string[]>([])
const costFilter = ref('all')
const troopsFilter = ref('all')
const disasterFilter = ref('all')
const legalityFilter = ref('all')
const sortMode = ref<'number' | 'cost' | 'troops' | 'name'>('number')
const selected = ref<DeckCard | null>(null)
const activeDeckName = ref<string | null>(null)
const specialIds = ref<string[]>([])
const catalogTab = ref<'master' | 'main' | 'extra'>('master')
const pendingDeleteName = ref('')
const deckMutationBusy = ref(false)
const deletingDeck = ref(false)
const deckImageUrl = ref('')
const deckImageBlob = ref<Blob | null>(null)
const generatingDeckImage = ref(false)
const publicationId = ref(typeof route.query.published === 'string' ? route.query.published : '')
const editorContentRevision = ref(0)
const ownedAlternateArts = ref<AlternateArt[]>([])
const alternateArtSelections = ref<Record<string, string>>({})
const alternateArtCopies = ref<Record<string, string[]>>({})
const operationsRestrictions = ref<OperationsCardRestriction[]>([])
const workspace = ref<'gallery' | 'stats' | 'hand' | 'content'>('gallery')
const mobilePane = ref<'pool' | 'deck' | 'insights'>('pool')
const mobileFiltersOpen = ref(false)
const mobileDetailOpen = ref(false)
const openingHandIds = ref<string[]>([])
type DeckSection = 'master' | 'main' | 'morale' | 'extra' | 'bench'
const SECTION_STORAGE_KEY = 'l12-deck-editor-sections-v1'
const collapsedSections = ref<Record<DeckSection, boolean>>({ master: false, main: false, morale: true, extra: false, bench: false })
try {
  collapsedSections.value = { ...collapsedSections.value, ...JSON.parse(localStorage.getItem(SECTION_STORAGE_KEY) || '{}') }
} catch { /* 使用默认分区状态 */ }

watch([deckName, masterId, counts, benchCounts, specialIds, alternateArtSelections, alternateArtCopies], () => editorContentRevision.value++, { deep: true, flush: 'sync' })
watch(collapsedSections, value => localStorage.setItem(SECTION_STORAGE_KEY, JSON.stringify(value)), { deep: true })

const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原', asgard: '阿斯加德',
  taiyangcheng: '太阳城', olympus: '奥林匹斯', otherworld: '彼界',
}
const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术（主动／反击）', artifact: '圣物',
}

onMounted(async () => {
  try {
    const [loadedCatalog, loadedDecks, arts] = await Promise.all([
      loadDeckCatalog(),
      ensureOfficialPrebuiltDecks(),
      platformState.account ? alternateArtApi.mine().catch(() => [] as AlternateArt[]) : Promise.resolve([] as AlternateArt[]),
    ])
    catalog.value = loadedCatalog
    savedDecks.value = loadedDecks
    ownedAlternateArts.value = arts
    void getEffectiveOperationsPolicy().then(policy => {
      operationsRestrictions.value = policy.cardRestrictions
    }).catch(() => undefined)
    const requested = typeof router.currentRoute.value.query.deck === 'string' ? router.currentRoute.value.query.deck : ''
    if (requested && savedDecks.value[requested]) loadDeck(savedDecks.value[requested], true)
    else selected.value = mainCards.value[0] ?? null
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库编辑器加载失败'
  } finally {
    loading.value = false
  }
})

const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const productOptions = computed(() => [...new Set(catalog.value.map(card => card.product))].sort())
const masters = computed(() => catalog.value.filter(card => (card.cardType === 'master' || card.cardType === 'divinity') && card.id !== 'S01-02M2'))
const selectedMaster = computed(() => byId.value.get(masterId.value))
const automaticExtraCards = computed(() => automaticExtraCardIdsForMaster(selectedMaster.value?.id)
  .map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const mainCards = computed(() => catalog.value.filter(card => MAIN_DECK_TYPES.has(card.cardType)
  && !isDerivedSpecialCard(card)))
const countSummary = computed(() => deckCountSummary(
  Object.entries(counts.value).flatMap(([id, count]) => Array(count).fill(id)), byId.value))
const totalCards = computed(() => countSummary.value.counted)
const uncountedCards = computed(() => countSummary.value.uncounted)
const moraleIds = computed(() => buildMoraleDeck(selectedMaster.value, catalog.value))
const trialCapacity = computed(() => trialCapacityForMaster(selectedMaster.value))
const availableTrials = computed(() => catalog.value.filter(card => card.cardType === 'trial'
  && card.faction === selectedMaster.value?.faction))
const selectedTrials = computed(() => specialIds.value.map(id => byId.value.get(id)).filter(Boolean) as DeckCard[])
const selectedAlternateArts = computed(() => selected.value
  ? ownedAlternateArts.value.filter(art => art.baseCardId === selected.value!.id) : [])
const entries = computed(() => Object.entries(counts.value)
  .filter(([, count]) => count > 0)
  .map(([id, count]) => ({ card: byId.value.get(id)!, count }))
  .filter(entry => entry.card)
  .sort((a, b) => compareDeckCards(a.card, b.card, selectedMaster.value?.faction)))
const benchEntries = computed(() => Object.entries(benchCounts.value)
  .filter(([, count]) => count > 0)
  .map(([id, count]) => ({ card: byId.value.get(id)!, count }))
  .filter(entry => entry.card)
  .sort((a, b) => compareDeckCards(a.card, b.card, selectedMaster.value?.faction)))
const benchTotal = computed(() => benchEntries.value.reduce((sum, entry) => sum + entry.count, 0))
const moraleCards = computed(() => moraleIds.value.map(id => byId.value.get(id)).filter((card): card is DeckCard => Boolean(card)))

function restrictionFor(cardId: string) {
  return operationsRestrictions.value.find(rule => rule.cardId === cardId && rule.masterId === masterId.value)
    ?? operationsRestrictions.value.find(rule => rule.cardId === cardId && !rule.masterId)
}
function allowedCopies(card: DeckCard) {
  return Math.min(effectiveDeckLimit(card, masterId.value), restrictionFor(card.id)?.maxCopies ?? Number.MAX_SAFE_INTEGER)
}
function cardLegality(card: DeckCard) {
  const restriction = restrictionFor(card.id)
  if (!restriction) return 'allowed'
  return restriction.maxCopies <= 0 ? 'banned' : 'restricted'
}
function entryIssue(card: DeckCard, count: number) {
  if (selectedMaster.value && card.faction !== 'universal' && card.faction !== selectedMaster.value.faction) return '与当前主宰阵营不符'
  const restriction = restrictionFor(card.id)
  const limit = allowedCopies(card)
  if (count > limit) return restriction?.reason ? `超过上限 ${limit}（${restriction.reason}）` : `超过上限 ${limit}`
  if (restriction?.maxCopies === 0) return restriction.reason ? `当前禁用（${restriction.reason}）` : '当前禁用'
  return ''
}
const filteredBaseCards = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  const master = selectedMaster.value
  return mainCards.value.filter(card => {
    if (master && card.faction !== 'universal' && card.faction !== master.faction) return false
    if (factionFilter.value !== 'all' && card.faction !== factionFilter.value) return false
    if (typeFilter.value !== 'all' && cardTypeFilterKey(card.cardType) !== typeFilter.value) return false
    if (productFilters.value.length && !productFilters.value.includes(card.product)) return false
    const filterCost = filterableCardCost(card)
    if (costFilter.value !== 'all' && (filterCost === null || (costFilter.value === '7+'
      ? filterCost < 7
      : filterCost !== Number(costFilter.value)))) return false
    if (troopsFilter.value !== 'all') {
      const troops = card.troops ?? 0
      if (troopsFilter.value === '0-999' && troops >= 1000) return false
      if (troopsFilter.value === '1000-1999' && (troops < 1000 || troops >= 2000)) return false
      if (troopsFilter.value === '2000-2999' && (troops < 2000 || troops >= 3000)) return false
      if (troopsFilter.value === '3000+' && troops < 3000) return false
    }
    if (disasterFilter.value !== 'all' && (disasterFilter.value === 'none'
      ? !!card.disasterLevel
      : card.disasterLevel !== Number(disasterFilter.value))) return false
    if (legalityFilter.value !== 'all' && cardLegality(card) !== legalityFilter.value) return false
    return !keyword || [card.nameZh, card.number, card.profession, ...(card.traits ?? []), card.effect]
      .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
  }).sort((a, b) => {
    if (sortMode.value === 'name') return a.nameZh.localeCompare(b.nameZh, 'zh-CN')
    if (sortMode.value === 'cost') return (filterableCardCost(a) ?? 99) - (filterableCardCost(b) ?? 99) || a.number.localeCompare(b.number)
    if (sortMode.value === 'troops') return (b.troops ?? -1) - (a.troops ?? -1) || a.number.localeCompare(b.number)
    return a.number.localeCompare(b.number)
  })
})
interface PoolCardAppearance { key: string; card: DeckCard; art?: AlternateArt }
const filtered = computed<PoolCardAppearance[]>(() => filteredBaseCards.value.flatMap(card => [
  { key: `${card.id}:original`, card },
  ...ownedAlternateArts.value.filter(art => art.baseCardId === card.id)
    .map(art => ({ key: `${card.id}:${art.id}`, card, art })),
]))
const validation = computed(() => validateDeck({
  name: deckName.value, masterId: masterId.value,
  cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
  moraleIds: moraleIds.value,
  specialIds: specialIds.value,
}, catalog.value, operationsRestrictions.value))
const curve = computed(() => {
  const values = Array(9).fill(0) as number[]
  entries.value.forEach(({ card, count }) => values[Math.min(8, card.cost ?? 0)] += count)
  return values
})
const maxCurve = computed(() => Math.max(1, ...curve.value))
const typeStats = computed(() => Object.entries(entries.value.reduce<Record<string, number>>((result, entry) => {
  const label = cardTypeLabel(entry.card.cardType, entry.card.isCounterTactic)
  result[label] = (result[label] ?? 0) + entry.count
  return result
}, {})).sort((a, b) => b[1] - a[1]))
const factionStats = computed(() => Object.entries(entries.value.reduce<Record<string, number>>((result, entry) => {
  const label = factionLabels[entry.card.faction] ?? entry.card.faction
  result[label] = (result[label] ?? 0) + entry.count
  return result
}, {})).sort((a, b) => b[1] - a[1]))
const openingHand = computed(() => openingHandIds.value.map(id => byId.value.get(id)).filter((card): card is DeckCard => Boolean(card)))
const activeFilterCount = computed(() => [query.value.trim(), typeFilter.value !== 'all', factionFilter.value !== 'all',
  productFilters.value.length > 0, costFilter.value !== 'all', troopsFilter.value !== 'all', disasterFilter.value !== 'all',
  legalityFilter.value !== 'all', sortMode.value !== 'number'].filter(Boolean).length)
const activeFilterSummary = computed(() => [
  query.value.trim() ? `搜索“${query.value.trim()}”` : '',
  typeFilter.value === 'all' ? '' : typeLabels[typeFilter.value],
  factionFilter.value === 'all' ? '' : factionLabels[factionFilter.value],
  productFilters.value.length ? `${productFilters.value.length} 个卡池` : '',
  costFilter.value === 'all' ? '' : `${costFilter.value}费`,
  troopsFilter.value === 'all' ? '' : `兵力 ${troopsFilter.value}`,
  disasterFilter.value === 'all' ? '' : disasterFilter.value === 'none' ? '无天灾等级' : `天灾 ${disasterFilter.value}`,
  legalityFilter.value === 'all' ? '' : ({ allowed: '可用', restricted: '受限', banned: '禁用' } as Record<string,string>)[legalityFilter.value],
  sortMode.value === 'number' ? '' : `按${({ cost: '费用', troops: '兵力', name: '名称' } as Record<string,string>)[sortMode.value]}排序`,
].filter(Boolean).join(' · '))

function setWorkspace(next: 'gallery' | 'stats' | 'hand' | 'content') {
  workspace.value = next
  mobilePane.value = next === 'gallery' ? 'pool' : 'insights'
  if (next === 'hand' && !openingHandIds.value.length) redrawOpeningHand()
}

function publicDeckUrl(id = publicationId.value) {
  if (!id || typeof window === 'undefined') return ''
  return new URL(router.resolve({ name: 'public-deck-detail', params: { deckId: id } }).href, window.location.origin).href
}
function setMobilePane(next: 'pool' | 'deck' | 'insights') {
  mobilePane.value = next
  if (next === 'pool') workspace.value = 'gallery'
  if (next === 'insights' && workspace.value === 'gallery') workspace.value = 'stats'
}
function redrawOpeningHand() {
  openingHandIds.value = samplePublicDeckOpeningHand(entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)))
}
function toggleSection(section: DeckSection) {
  collapsedSections.value = { ...collapsedSections.value, [section]: !collapsedSections.value[section] }
}
function selectCard(card: DeckCard) {
  selected.value = card
  if (window.matchMedia('(max-width:820px)').matches) mobileDetailOpen.value = true
}

function chooseMaster(id: string) {
  masterId.value = id
  const master = byId.value.get(id)
  const next: Record<string, number> = {}
  Object.entries(counts.value).forEach(([cardId, count]) => {
    const card = byId.value.get(cardId)
    if (card && master && (card.faction === 'universal' || card.faction === master.faction)) next[cardId] = count
  })
  counts.value = next
  benchCounts.value = Object.fromEntries(Object.entries(benchCounts.value).filter(([cardId]) => {
    const card = byId.value.get(cardId)
    return card && master && (card.faction === 'universal' || card.faction === master.faction)
  }))
  alternateArtCopies.value = Object.fromEntries(Object.entries(alternateArtCopies.value)
    .filter(([cardId]) => Boolean(next[cardId])))
  alternateArtSelections.value = Object.fromEntries(Object.entries(alternateArtSelections.value)
    .filter(([cardId]) => !MAIN_DECK_TYPES.has(byId.value.get(cardId)?.cardType ?? '') || Boolean(next[cardId])))
  const capacity = trialCapacityForMaster(master)
  specialIds.value = specialIds.value.filter(specialId => {
    const card = byId.value.get(specialId)
    return card?.cardType === 'trial' && card.faction === master?.faction
  }).slice(0, capacity)
  catalogTab.value = 'main'
  openingHandIds.value = []
}

function toggleTrial(card: DeckCard) {
  if (specialIds.value.includes(card.id)) {
    specialIds.value = specialIds.value.filter(id => id !== card.id)
    return
  }
  if (specialIds.value.length >= trialCapacity.value) {
    notice.value = `该主宰的试炼区只能携带 ${trialCapacity.value} 张`
    return
  }
  specialIds.value = [...specialIds.value, card.id]
  selected.value = card
  notice.value = ''
}

function normalizedAppearanceList(cardId: string, count = counts.value[cardId] || 0) {
  const explicit = [...(alternateArtCopies.value[cardId] ?? [])].slice(0, count)
  if (!explicit.length && alternateArtSelections.value[cardId]) return Array(count).fill(alternateArtSelections.value[cardId]) as string[]
  while (explicit.length < count) explicit.push('')
  return explicit
}

function appearanceCount(entry: PoolCardAppearance) {
  const artId = entry.art?.id ?? ''
  return normalizedAppearanceList(entry.card.id).filter(value => value === artId).length
}

function addCardAppearance(card: DeckCard, artId = '') {
  if (!selectedMaster.value) { notice.value = '请先选择主宰'; return }
  const count = counts.value[card.id] || 0
  const limit = allowedCopies(card)
  if (count >= limit) { notice.value = `${card.nameZh} 的原画与异画合计最多 ${limit} 张`; return }
  if (!doesNotCountTowardMainDeck(card) && totalCards.value >= 50) { notice.value = '主牌库最多 50 张'; return }
  const copies = normalizedAppearanceList(card.id, count)
  copies.push(artId)
  alternateArtCopies.value = { ...alternateArtCopies.value, [card.id]: copies }
  const selections = { ...alternateArtSelections.value }; delete selections[card.id]; alternateArtSelections.value = selections
  counts.value = { ...counts.value, [card.id]: count + 1 }
  selected.value = card
  openingHandIds.value = []
  notice.value = ''
}

function add(card: DeckCard) { addCardAppearance(card) }
function addAppearance(entry: PoolCardAppearance) { addCardAppearance(entry.card, entry.art?.id ?? '') }

function remove(id: string) {
  const count = counts.value[id] || 0
  const copies = normalizedAppearanceList(id, count)
  copies.pop()
  const next = { ...counts.value }
  if ((next[id] || 0) <= 1) delete next[id]
  else next[id]--
  counts.value = next
  const nextCopies = { ...alternateArtCopies.value }
  if (copies.some(Boolean)) nextCopies[id] = copies
  else delete nextCopies[id]
  alternateArtCopies.value = nextCopies
  openingHandIds.value = []
}

function addToBench(card: DeckCard) {
  if (!selectedMaster.value) { notice.value = '请先选择主宰'; return }
  if (card.faction !== 'universal' && card.faction !== selectedMaster.value.faction) { notice.value = `${card.nameZh} 与当前主宰阵营不符`; return }
  const count = benchCounts.value[card.id] ?? 0
  const limit = effectiveDeckLimit(card, masterId.value)
  if (count >= limit) { notice.value = `${card.nameZh} 的备选区最多保存 ${limit} 张`; return }
  if (benchTotal.value >= 200) { notice.value = '备选区最多保存 200 张卡牌'; return }
  benchCounts.value = { ...benchCounts.value, [card.id]: count + 1 }
  notice.value = `已将《${card.nameZh}》加入备选区`
}

function removeFromBench(cardId: string) {
  const next = { ...benchCounts.value }
  if ((next[cardId] ?? 0) <= 1) delete next[cardId]
  else next[cardId]--
  benchCounts.value = next
}

function moveMainToBench(card: DeckCard) {
  const before = benchCounts.value[card.id] ?? 0
  addToBench(card)
  if ((benchCounts.value[card.id] ?? 0) > before) remove(card.id)
}

function moveBenchToMain(card: DeckCard) {
  const before = counts.value[card.id] ?? 0
  addCardAppearance(card)
  if ((counts.value[card.id] ?? 0) > before) removeFromBench(card.id)
}

function removeAppearance(entry: PoolCardAppearance) {
  const count = counts.value[entry.card.id] || 0
  if (!count) return
  const artId = entry.art?.id ?? ''
  const copies = normalizedAppearanceList(entry.card.id, count)
  const index = copies.lastIndexOf(artId)
  if (index < 0) return
  copies.splice(index, 1)
  const nextCounts = { ...counts.value }
  if (count <= 1) delete nextCounts[entry.card.id]
  else nextCounts[entry.card.id] = count - 1
  counts.value = nextCounts
  const nextCopies = { ...alternateArtCopies.value }
  if (copies.some(Boolean)) nextCopies[entry.card.id] = copies
  else delete nextCopies[entry.card.id]
  alternateArtCopies.value = nextCopies
  notice.value = ''
}

function newDeck() {
  publicationId.value = ''
  activeDeckName.value = null
  deckName.value = '新牌库'
  masterId.value = ''
  counts.value = {}
  benchCounts.value = {}
  specialIds.value = []
  alternateArtSelections.value = {}
  alternateArtCopies.value = {}
  openingHandIds.value = []
  selected.value = mainCards.value[0] ?? null
  notice.value = '已新建空白牌库'
}

function currentDeck(): SavedL12Deck {
  return {
    name: deckName.value.trim(), masterId: masterId.value,
    cardIds: entries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
    moraleIds: moraleIds.value, specialIds: [...specialIds.value], updatedAt: new Date().toISOString(),
    benchIds: benchEntries.value.flatMap(entry => Array(entry.count).fill(entry.card.id)),
    alternateArtSelections: { ...alternateArtSelections.value },
    alternateArtCopies: Object.fromEntries(Object.entries(alternateArtCopies.value).map(([id, values]) => [id, [...values]])),
  }
}

function mutationError(error: unknown, fallback: string) {
  return error instanceof Error ? error.message : fallback
}

async function onSave() {
  if (validation.value) { notice.value = validation.value; return }
  if (deckMutationBusy.value || deletingDeck.value) return
  deckMutationBusy.value = true
  const deck = currentDeck()
  const previousName = activeDeckName.value
  const requestedRevision = editorContentRevision.value
  try {
    const saved = await saveDeck(deck)
    let oldNameDeleteError: unknown = null
    if (previousName && previousName.toLocaleLowerCase('zh-CN') !== saved.name.toLocaleLowerCase('zh-CN')) {
      try {
        // 改名先确认新名称已保存；旧名称删除失败时保留两份，绝不以丢失新牌库换取表面原子性。
        await deleteDeck(previousName)
      } catch (error) {
        oldNameDeleteError = error
      }
    }
    savedDecks.value = loadSavedDecks()
    const editorUnchanged = editorContentRevision.value === requestedRevision
    if (editorUnchanged) {
      activeDeckName.value = saved.name
      deckName.value = saved.name
    }
    if (oldNameDeleteError) {
      notice.value = `已保存新名称〈${saved.name}〉，但旧牌库〈${previousName}〉删除失败：${mutationError(oldNameDeleteError, '请稍后重试')}`
    } else if (editorUnchanged) {
      notice.value = `已保存〈${saved.name}〉，可在房间中选择`
    }
  } catch (error) {
    notice.value = `牌库保存失败：${mutationError(error, '请稍后重试')}`
  } finally {
    deckMutationBusy.value = false
  }
}

async function onSaveAs() {
  if (validation.value) { notice.value = validation.value; return }
  if (deckMutationBusy.value || deletingDeck.value) return
  const base = `${deckName.value.trim()} 副本`
  let name = base.slice(0, 24)
  let suffix = 2
  while (savedDecks.value[name]) {
    const ending = ` ${suffix++}`
    name = `${base.slice(0, 24 - ending.length)}${ending}`
  }
  const deck = { ...currentDeck(), name }
  const requestedRevision = editorContentRevision.value
  deckMutationBusy.value = true
  try {
    const saved = await saveDeck(deck)
    savedDecks.value = loadSavedDecks()
    if (editorContentRevision.value === requestedRevision) {
      publicationId.value = ''
      activeDeckName.value = saved.name
      deckName.value = saved.name
      notice.value = `已另存为〈${saved.name}〉`
    }
  } catch (error) {
    notice.value = `牌库另存失败：${mutationError(error, '请稍后重试')}`
  } finally {
    deckMutationBusy.value = false
  }
}

async function publishCurrentDeck() {
  if (!platformState.account) { notice.value = '请先登录账号，再公开牌库'; return }
  if (validation.value) { notice.value = validation.value; return }
  if (deckMutationBusy.value || deletingDeck.value) return
  deckMutationBusy.value = true
  const requestedRevision = editorContentRevision.value
  const publishedId = publicationId.value
  try {
    const deck = currentDeck()
    const saved = await saveDeck(deck)
    savedDecks.value = loadSavedDecks()
    const result = await publicDeckApi.publish(saved, publishedId || undefined)
    if (editorContentRevision.value === requestedRevision) {
      activeDeckName.value = saved.name
      deckName.value = saved.name
      publicationId.value = result.id
      await router.replace({ query: { ...route.query, deck: saved.name, published: result.id } })
      notice.value = publishedId ? `已更新公开牌库〈${saved.name}〉` : `已公开〈${saved.name}〉，后续可从此处更新公开版本`
    }
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '公开牌库失败'
  } finally {
    deckMutationBusy.value = false
  }
}

function loadDeck(deck: SavedL12Deck, preservePublication = false) {
  if (!preservePublication) publicationId.value = ''
  activeDeckName.value = deck.name
  deckName.value = deck.name
  masterId.value = deck.masterId
  const next: Record<string, number> = {}
  deck.cardIds.forEach(id => next[id] = (next[id] || 0) + 1)
  counts.value = next
  const nextBench: Record<string, number> = {}
  ;(deck.benchIds ?? []).forEach(id => nextBench[id] = (nextBench[id] ?? 0) + 1)
  benchCounts.value = nextBench
  specialIds.value = [...(deck.specialIds ?? [])]
  const nextSelections = { ...(deck.alternateArtSelections ?? {}) }
  const nextCopies = Object.fromEntries(Object.entries(deck.alternateArtCopies ?? {}).map(([id, values]) => [id, [...values]]))
  Object.entries(nextSelections).forEach(([cardId, artId]) => {
    const card = byId.value.get(cardId)
    if (!card || !MAIN_DECK_TYPES.has(card.cardType) || nextCopies[cardId]) return
    nextCopies[cardId] = Array(next[cardId] || 0).fill(artId)
    delete nextSelections[cardId]
  })
  Object.entries(next).forEach(([cardId, count]) => {
    if (!nextCopies[cardId]) return
    nextCopies[cardId] = nextCopies[cardId].slice(0, count)
    while (nextCopies[cardId].length < count) nextCopies[cardId].push('')
  })
  alternateArtSelections.value = nextSelections
  alternateArtCopies.value = nextCopies
  openingHandIds.value = []
  notice.value = `已载入〈${deck.name}〉`
}

function selectAlternateArt(cardId: string, artId: string) {
  const next = { ...alternateArtSelections.value }
  if (artId) next[cardId] = artId
  else delete next[cardId]
  alternateArtSelections.value = next
}

function requestDelete(name = activeDeckName.value ?? '') {
  if (!name) { notice.value = '当前不是已保存牌库'; return }
  pendingDeleteName.value = name
}

async function confirmDelete() {
  const name = pendingDeleteName.value
  if (!name || deletingDeck.value || deckMutationBusy.value) return
  deletingDeck.value = true
  try {
    await deleteDeck(name)
    savedDecks.value = loadSavedDecks()
    if (activeDeckName.value === name) newDeck()
    notice.value = `已删除〈${name}〉`
    pendingDeleteName.value = ''
  } catch (error) {
    notice.value = `删除〈${name}〉失败：${mutationError(error, '请稍后重试')}`
  } finally {
    deletingDeck.value = false
  }
}

function resetFilters() {
  query.value = ''
  typeFilter.value = factionFilter.value = costFilter.value = troopsFilter.value = disasterFilter.value = legalityFilter.value = 'all'
  productFilters.value = []
  sortMode.value = 'number'
}

function toggleProductFilter(product: string) {
  productFilters.value = productFilters.value.includes(product)
    ? productFilters.value.filter(value => value !== product)
    : [...productFilters.value, product]
}

function closeDeckImage() {
  if (deckImageUrl.value) URL.revokeObjectURL(deckImageUrl.value)
  deckImageUrl.value = ''
  deckImageBlob.value = null
}

async function generateDeckImage() {
  if (validation.value) { notice.value = validation.value; return }
  generatingDeckImage.value = true
  closeDeckImage()
  try {
    deckImageBlob.value = await createDeckImageBlob(currentDeck(), catalog.value, { publicUrl: publicDeckUrl() })
    deckImageUrl.value = URL.createObjectURL(deckImageBlob.value)
  } catch (error) {
    notice.value = error instanceof Error ? error.message : '牌库图生成失败'
  } finally {
    generatingDeckImage.value = false
  }
}

async function saveGeneratedDeckImage() {
  if (!deckImageBlob.value) return
  await downloadDeckImage(currentDeck(), catalog.value, deckImageBlob.value)
}

onBeforeUnmount(() => {
  closeDeckImage()
})
</script>

<template>
  <div class="deck-builder-shell">
    <header class="deck-builder-topbar">
      <button class="back-button" @click="router.push(returnTo)">← 返回上一级</button>
      <div><small>GRANDUMI FRAMEWORK · LEGION12 STYLE</small><h1>牌库编辑器</h1></div>
      <label>牌库名称<input v-model="deckName" maxlength="24"/></label>
      <label class="saved-deck-switcher">已保存牌库<select :value="activeDeckName ?? ''" @change="savedDecks[($event.target as HTMLSelectElement).value] && loadDeck(savedDecks[($event.target as HTMLSelectElement).value])"><option value="">新牌库</option><option v-for="deck in savedDecks" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select></label>
      <div class="deck-total" :class="{ valid: !validation }"><b>{{ countSummary.label }}</b><span>/ 40–50<span v-if="uncountedCards">（括号内不计构筑）</span></span></div>
      <div class="deck-file-actions">
        <button @click="newDeck">新建牌库</button>
        <button class="primary" :disabled="!!validation || deckMutationBusy || deletingDeck" @click="onSave">{{ deckMutationBusy ? '保存中…' : '保存牌库' }}</button>
        <button :disabled="!!validation || deckMutationBusy || deletingDeck" @click="onSaveAs">另存为牌库</button>
        <button :disabled="!!validation || generatingDeckImage" @click="generateDeckImage">{{ generatingDeckImage ? '生成中…' : '生成牌库图' }}</button>
        <button :disabled="!!validation || deckMutationBusy || deletingDeck" @click="publishCurrentDeck">{{ publicationId ? '更新公开牌库' : '公开牌库' }}</button>
        <button class="delete-deck" :disabled="!activeDeckName || deckMutationBusy || deletingDeck" @click="requestDelete()">删除牌库</button>
      </div>
    </header>

    <nav class="deck-mobile-nav" aria-label="移动端牌库编辑工作区">
      <button :class="{ active: mobilePane === 'pool' }" @click="setMobilePane('pool')">卡池</button>
      <button :class="{ active: mobilePane === 'deck' }" @click="setMobilePane('deck')">牌表 · {{ totalCards }}</button>
      <button :class="{ active: mobilePane === 'insights' && workspace !== 'content' }" @click="setMobilePane('insights')">统计 / 起手</button>
      <button v-if="publicationId" :class="{ active: workspace === 'content' }" @click="setWorkspace('content')">公开内容</button>
    </nav>

    <main v-if="loading" class="deck-loading">正在载入卡牌数据…</main>
    <main v-else class="deck-builder-grid" :data-mobile-pane="mobilePane">
      <div class="deck-side-column">
      <aside class="deck-detail-panel grand-panel">
        <p class="kicker">CARD DETAIL</p><h2>卡牌详情</h2>
        <section v-if="selected" class="builder-card-detail archive-detail">
          <CardDetailContent :card="selected" :show-catalog-only="false"/>
          <label v-if="selectedAlternateArts.length && !MAIN_DECK_TYPES.has(selected.cardType)" class="alternate-art-selector">
            <span>对局卡图</span>
            <select :value="alternateArtSelections[selected.id] ?? ''" @change="selectAlternateArt(selected!.id, ($event.target as HTMLSelectElement).value)">
              <option value="">使用原始卡图</option>
              <option v-for="art in selectedAlternateArts" :key="art.id" :value="art.id">{{ art.artCode }} · {{ art.displayName }}</option>
            </select>
            <small>已选择的异画只改变本人的对局显示，不改变卡牌规则。</small>
          </label>
        </section>
        <p v-else class="empty-detail">选择卡牌后在此查看详情。</p>
      </aside>
      <aside class="saved-decks-panel grand-panel">
        <p class="kicker">SAVED DECKS</p><h2>已保存牌库</h2>
        <div class="saved-list"><article v-for="deck in savedDecks" :key="deck.name" :class="{ active: deck.name === activeDeckName }"><button @click="loadDeck(deck)"><DeckProfile compact :master-id="deck.masterId" :master-name="byId.get(deck.masterId)?.nameZh" :name="deck.name" :meta="`${deckCountSummary(deck.cardIds, byId).label} 张`"/></button><button class="delete" @click="requestDelete(deck.name)">×</button></article><p v-if="!Object.keys(savedDecks).length">暂无本地牌库</p></div>
      </aside>
      </div>

      <div class="deck-center-column">
      <nav class="workspace-tabs" aria-label="牌库编辑器工作区">
        <button :class="{ active: workspace === 'gallery' }" @click="setWorkspace('gallery')">Gallery 卡池</button>
        <button :class="{ active: workspace === 'stats' }" @click="setWorkspace('stats')">Stats 统计</button>
        <button :class="{ active: workspace === 'hand' }" @click="setWorkspace('hand')">Hand 起手</button>
        <button v-if="publicationId" :class="{ active: workspace === 'content' }" @click="setWorkspace('content')">公开内容</button>
      </nav>
      <section v-show="workspace === 'gallery'" class="deck-catalog grand-panel">
        <article v-if="selectedMaster" class="current-deck-summary" aria-label="当前牌库主宰信息">
          <img :src="masterProfileUrl(selectedMaster.id, selectedMaster.imageUrl)" :alt="selectedMaster.nameZh"/>
          <div><small>当前主宰</small><b>{{ selectedMaster.nameZh }}</b><span>{{ factionLabels[selectedMaster.faction] }} · 士气 {{ moraleIds.length }} 张</span></div>
        </article>
        <header><div><p class="kicker">CARD POOL</p><h2>{{ catalogTab === 'master' ? '主宰' : catalogTab === 'main' ? '主牌库' : '额外卡牌' }}</h2></div><span>{{ catalogTab === 'master' ? masters.length : catalogTab === 'main' ? filtered.length : availableTrials.length + automaticExtraCards.length }} 张结果</span></header>
        <nav class="catalog-tabs" aria-label="牌库构筑卡池分类">
          <button :class="{ active: catalogTab === 'master' }" @click="catalogTab = 'master'">主宰</button>
          <button :class="{ active: catalogTab === 'main' }" @click="catalogTab = 'main'">主牌库</button>
          <button :class="{ active: catalogTab === 'extra' }" @click="catalogTab = 'extra'">额外卡牌</button>
        </nav>
        <div v-if="catalogTab === 'main'" class="catalog-filter-controls">
          <MobileFilterSheet v-model="mobileFiltersOpen" title="筛选卡池" :active-count="activeFilterCount" always-visible @reset="resetFilters">
          <section class="catalog-filter-bar" aria-label="主牌库筛选">
          <label class="filter-search">搜索<input v-model="query" placeholder="卡名、编号、效果"/></label>
          <label>类型<select v-model="typeFilter"><option value="all">全部主牌</option><option v-for="(label,key) in typeLabels" :key="key" :value="key">{{ label }}</option></select></label>
          <label>阵营<select v-model="factionFilter"><option value="all">全部阵营</option><option v-for="(label,key) in factionLabels" :key="key" :value="key">{{ label }}</option></select></label>
          <fieldset class="product-filter" aria-label="卡池（可多选）">
            <legend>卡池（可多选）</legend>
            <button type="button" :class="{ active: !productFilters.length }" @click="productFilters = []">全部</button>
            <button v-for="value in productOptions" :key="value" type="button" :class="{ active: productFilters.includes(value) }" :aria-pressed="productFilters.includes(value)" @click="toggleProductFilter(value)">{{ value }}</button>
          </fieldset>
          <label>费用<select v-model="costFilter"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value" :value="value">{{ value }}</option></select></label>
          <label>兵力<select v-model="troopsFilter"><option value="all">全部兵力</option><option value="0-999">0–999</option><option value="1000-1999">1000–1999</option><option value="2000-2999">2000–2999</option><option value="3000+">3000+</option></select></label>
          <label>天灾等级<select v-model="disasterFilter"><option value="all">全部</option><option value="none">无</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select></label>
          <label>可用状态<select v-model="legalityFilter"><option value="all">全部</option><option value="allowed">可用</option><option value="restricted">受限</option><option value="banned">禁用</option></select></label>
          <label>排序<select v-model="sortMode"><option value="number">编号</option><option value="cost">费用</option><option value="troops">兵力</option><option value="name">名称</option></select></label>
          <button class="filter-reset" @click="resetFilters">清除筛选</button>
          </section>
          <template #apply-label>查看 {{ filtered.length }} 张结果</template>
          </MobileFilterSheet>
          <button v-if="activeFilterCount" class="catalog-filter-summary" type="button" @click="mobileFiltersOpen = true">{{ activeFilterSummary }}</button>
          <button v-if="activeFilterCount" class="catalog-filter-clear" type="button" @click="resetFilters">清除</button>
        </div>
        <div v-if="catalogTab === 'master'" class="deck-card-grid">
          <article v-for="master in masters" :key="master.id" class="deck-card" :class="{ chosen: master.id === masterId }" @click="selectCard(master)">
            <button class="card-image" @dblclick.stop="chooseMaster(master.id)"><CardImage :card-id="master.id" :legacy-url="master.imageUrl" :alt="master.nameZh" intent="thumb" fit="cover"/></button>
            <div><b>{{ master.nameZh }}</b><small>{{ master.number }} · {{ factionLabels[master.faction] }}</small></div>
            <button class="choose-special" @click.stop="chooseMaster(master.id)">{{ master.id === masterId ? '已选择' : '选择主宰' }}</button>
          </article>
        </div>
        <div v-else-if="catalogTab === 'main'" class="deck-card-grid">
          <article v-for="entry in filtered" :key="entry.key" class="deck-card" :class="{ chosen: appearanceCount(entry), 'alternate-art-card': entry.art, 'landscape-thumbnail': isHorizontalCardType(entry.card.cardType), invalid: entryIssue(entry.card, counts[entry.card.id] || 0) }" @click="selectCard(entry.card)">
            <button class="card-image" @dblclick.stop="addAppearance(entry)">
              <CardImage :card-id="entry.art?.cardImageId || entry.card.id" :legacy-url="entry.art && !entry.art.builtIn ? (entry.art.thumbnailUrl || entry.art.imageUrl) : entry.card.imageUrl" :alt="entry.art?.displayName || entry.card.nameZh" intent="thumb" :fit="isHorizontalCardType(entry.card.cardType) ? 'contain' : 'cover'"/>
              <b v-if="appearanceCount(entry)" class="copy-count">×{{ appearanceCount(entry) }}</b>
            </button>
            <div><b>{{ entry.card.nameZh }}<em v-if="entry.art">异画</em></b><small>{{ entry.art?.artCode || entry.card.number }} · {{ cardTypeLabel(entry.card.cardType, entry.card.isCounterTactic) }}</small><span v-if="entryIssue(entry.card, counts[entry.card.id] || 0)" class="entry-issue">{{ entryIssue(entry.card, counts[entry.card.id] || 0) }}</span></div>
            <div class="pool-count-controls">
              <button :disabled="!appearanceCount(entry)" aria-label="减少一张" @click.stop="removeAppearance(entry)">−</button>
              <strong>{{ appearanceCount(entry) }}</strong>
              <button :disabled="!masterId || (counts[entry.card.id] || 0) >= allowedCopies(entry.card) || (!doesNotCountTowardMainDeck(entry.card) && totalCards >= 50)" aria-label="增加一张" @click.stop="addAppearance(entry)">＋</button>
            </div>
            <button class="add-to-bench" :disabled="!masterId || (benchCounts[entry.card.id] || 0) >= effectiveDeckLimit(entry.card, masterId)" @click.stop="addToBench(entry.card)">加入备选区</button>
          </article>
        </div>
        <div v-else class="deck-card-grid">
          <article v-for="trial in availableTrials" :key="trial.id" class="deck-card landscape-thumbnail" :class="{ chosen: specialIds.includes(trial.id) }" @click="selectCard(trial)">
            <button class="card-image" @dblclick.stop="toggleTrial(trial)"><CardImage :card-id="trial.id" :legacy-url="trial.imageUrl" :alt="trial.nameZh" intent="thumb"/></button>
            <div><b>{{ trial.nameZh }}</b><small>{{ trial.number }} · 试炼</small></div>
            <button class="choose-special" @click.stop="toggleTrial(trial)">{{ specialIds.includes(trial.id) ? '移出额外区' : '加入额外区' }}</button>
          </article>
          <article v-for="card in automaticExtraCards" :key="card.id" class="deck-card chosen" @click="selectCard(card)">
            <button class="card-image"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" fit="cover"/></button>
            <div><b>{{ card.nameZh }}</b><small>{{ card.number }} · 主宰专属</small></div><button class="choose-special" disabled>自动配置</button>
          </article>
          <p v-if="!availableTrials.length && !automaticExtraCards.length" class="empty-extra">当前主宰没有可配置的额外卡牌。</p>
        </div>
      </section>
      <section v-show="workspace === 'stats'" class="deck-insight-panel grand-panel" data-editor-workspace="stats">
        <header><div><p class="kicker">DECK STATS</p><h2>当前构筑统计</h2></div><strong :class="{ valid: !validation }">{{ validation || '构筑合法' }}</strong></header>
        <div class="stats-summary">
          <article><small>主牌</small><b>{{ totalCards }}</b><span v-if="uncountedCards">其中 {{ uncountedCards }} 张不计构筑</span></article>
          <article><small>士气</small><b>{{ moraleIds.length }}</b><span>{{ selectedMaster ? factionLabels[selectedMaster.faction] : '未选择主宰' }}</span></article>
          <article><small>额外区</small><b>{{ selectedTrials.length + automaticExtraCards.length }}</b><span>试炼 {{ selectedTrials.length }} · 自动 {{ automaticExtraCards.length }}</span></article>
          <article><small>备选区</small><b>{{ benchTotal }}</b><span>不计入合法性</span></article>
        </div>
        <section class="stats-block"><h3>费用曲线</h3><div class="cost-curve expanded"><i v-for="(value,index) in curve" :key="index"><span :style="{height:`${Math.max(4, value / maxCurve * 120)}px`}"></span><b>{{ index === 8 ? '8+' : index }}</b><small>{{ value }}</small></i></div></section>
        <div class="stats-columns"><section class="stats-block"><h3>卡牌类型</h3><article v-for="[label,value] in typeStats" :key="label"><span>{{ label }}</span><b>{{ value }}</b></article></section><section class="stats-block"><h3>阵营分布</h3><article v-for="[label,value] in factionStats" :key="label"><span>{{ label }}</span><b>{{ value }}</b></article></section></div>
      </section>
      <section v-show="workspace === 'hand'" class="deck-insight-panel hand-workspace grand-panel" data-editor-workspace="hand">
        <header><div><p class="kicker">OPENING HAND</p><h2>当前构筑试抽</h2><p>从尚未保存的当前主牌中随机抽取 6 张；不会修改牌库或生成对局记录。</p></div><button @click="redrawOpeningHand">重新试抽</button></header>
        <div class="editor-opening-hand"><article v-for="(card,index) in openingHand" :key="`${card.id}-${index}`" @click="selectCard(card)"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/><b>{{ card.nameZh }}</b><small>{{ card.number }}</small></article><p v-if="!openingHand.length">当前主牌为空，先返回 Gallery 加入卡牌。</p></div>
      </section>
      <PublicDeckContentEditor v-if="publicationId" v-show="workspace === 'content'" :publication-id="publicationId" :catalog="catalog" @saved="notice = $event"/>
      </div>

      <aside class="deck-list grand-panel">
        <header><div><p class="kicker">DECK LIST</p><h2>{{ selectedMaster?.nameZh || '未选择主宰' }}</h2></div><b>{{ countSummary.label }}</b></header>
        <div class="cost-curve"><i v-for="(value,index) in curve" :key="index"><span :style="{height:`${Math.max(4, value / maxCurve * 56)}px`}"></span><b>{{ index === 8 ? '8+' : index }}</b><small>{{ value }}</small></i></div>
        <div class="deck-sections">
          <section class="deck-zone" data-deck-section="master"><header><button @click="toggleSection('master')"><span>主宰</span><b>{{ selectedMaster ? '1/1' : '0/1' }}</b><i>{{ collapsedSections.master ? '展开' : '折叠' }}</i></button></header><div v-if="!collapsedSections.master" class="deck-zone-body"><article v-if="selectedMaster" class="deck-entry-row" @click="selectCard(selectedMaster)"><CardImage class="deck-entry-banner" :card-id="selectedMaster.id" :legacy-url="selectedMaster.imageUrl" :alt="selectedMaster.nameZh" intent="thumb" fit="cover" native-orientation/><span>主</span><div><b>{{ selectedMaster.nameZh }}</b><small>{{ factionLabels[selectedMaster.faction] }}</small></div><strong>×1</strong></article><p v-else>请从卡池选择主宰。</p></div></section>
          <section class="deck-zone" data-deck-section="main"><header><button @click="toggleSection('main')"><span>主牌库</span><b>{{ totalCards }}/40–50</b><i>{{ collapsedSections.main ? '展开' : '折叠' }}</i></button></header><div v-if="!collapsedSections.main" class="deck-zone-body"><article v-for="entry in entries" :key="entry.card.id" class="deck-entry-row" :class="{ invalid: entryIssue(entry.card, entry.count) }" @click="selectCard(entry.card)"><CardImage class="deck-entry-banner" :card-id="entry.card.id" :legacy-url="entry.card.imageUrl" :alt="entry.card.nameZh" intent="thumb" fit="cover" native-orientation object-position="center 28%"/><span>{{ entry.card.cost ?? '—' }}</span><div><b>{{ entry.card.nameZh }}</b><small>{{ entry.card.number }}</small><em v-if="entryIssue(entry.card, entry.count)">{{ entryIssue(entry.card, entry.count) }}</em></div><strong>×{{ entry.count }}</strong><button aria-label="增加一张" :disabled="entry.count >= allowedCopies(entry.card) || (!doesNotCountTowardMainDeck(entry.card) && totalCards >= 50)" @click.stop="add(entry.card)">＋</button><button title="移入备选区" aria-label="移入备选区" @click.stop="moveMainToBench(entry.card)">备</button><button aria-label="减少一张" @click.stop="remove(entry.card.id)">−</button></article><p v-if="!entries.length">从 Gallery 加入卡牌，或从备选区移回主牌。</p></div></section>
          <section class="deck-zone" data-deck-section="morale"><header><button @click="toggleSection('morale')"><span>士气</span><b>{{ moraleIds.length }}/{{ selectedMaster?.faction === 'taiyangcheng' ? 6 : 8 }}</b><i>{{ collapsedSections.morale ? '展开' : '折叠' }}</i></button></header><div v-if="!collapsedSections.morale" class="deck-zone-body"><article v-for="(card,index) in moraleCards" :key="`${card.id}-${index}`" class="deck-entry-row" @click="selectCard(card)"><CardImage class="deck-entry-banner" :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" fit="cover" native-orientation/><span>士</span><div><b>{{ card.nameZh }}</b><small>{{ card.number }}</small></div><strong>×1</strong></article><p v-if="!moraleCards.length">选择主宰后自动配置士气。</p></div></section>
          <section class="deck-zone" data-deck-section="extra"><header><button @click="toggleSection('extra')"><span>额外区</span><b>{{ selectedTrials.length }}/{{ trialCapacity }}<span v-if="automaticExtraCards.length"> + {{ automaticExtraCards.length }} 自动</span></b><i>{{ collapsedSections.extra ? '展开' : '折叠' }}</i></button></header><div v-if="!collapsedSections.extra" class="deck-zone-body"><article v-for="trial in selectedTrials" :key="trial.id" class="deck-entry-row" @click="selectCard(trial)"><CardImage class="deck-entry-banner" :card-id="trial.id" :legacy-url="trial.imageUrl" :alt="trial.nameZh" intent="thumb" fit="cover" native-orientation/><span>{{ trial.trialValue ?? '试' }}</span><div><b>{{ trial.nameZh }}</b><small>{{ trial.number }} · 试炼</small></div><strong>×1</strong><button aria-label="移出额外区" @click.stop="toggleTrial(trial)">−</button></article><article v-for="card in automaticExtraCards" :key="card.id" class="deck-entry-row" @click="selectCard(card)"><CardImage class="deck-entry-banner" :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" fit="cover" native-orientation/><span>专</span><div><b>{{ card.nameZh }}</b><small>{{ card.number }} · 自动配置</small></div><strong>×1</strong><button aria-label="主宰自动配置" disabled>锁</button></article><p v-if="!selectedTrials.length && !automaticExtraCards.length">当前没有额外区卡牌。</p></div></section>
          <section class="deck-zone" data-deck-section="bench"><header><button @click="toggleSection('bench')"><span>备选区</span><b>{{ benchTotal }} 张</b><i>{{ collapsedSections.bench ? '展开' : '折叠' }}</i></button><small>不计入主牌数量与合法性</small></header><div v-if="!collapsedSections.bench" class="deck-zone-body"><article v-for="entry in benchEntries" :key="entry.card.id" class="deck-entry-row" @click="selectCard(entry.card)"><CardImage class="deck-entry-banner" :card-id="entry.card.id" :legacy-url="entry.card.imageUrl" :alt="entry.card.nameZh" intent="thumb" fit="cover" native-orientation/><span>{{ entry.card.cost ?? '—' }}</span><div><b>{{ entry.card.nameZh }}</b><small>{{ entry.card.number }}</small></div><strong>×{{ entry.count }}</strong><button title="加入主牌库" aria-label="加入主牌库" :disabled="(counts[entry.card.id] || 0) >= allowedCopies(entry.card) || (!doesNotCountTowardMainDeck(entry.card) && totalCards >= 50)" @click.stop="moveBenchToMain(entry.card)">＋主</button><button aria-label="移出备选区" @click.stop="removeFromBench(entry.card.id)">−</button></article><p v-if="!benchEntries.length">从 Gallery 加入暂不采用的卡牌。</p></div></section>
        </div>
        <footer :class="{ error: validation }">{{ notice || validation || '牌库合法，可以保存并用于房间对战' }}</footer>
      </aside>
    </main>
    <div v-if="mobileDetailOpen && selected" class="builder-modal-mask mobile-card-detail-mask" @click.self="mobileDetailOpen = false">
      <section class="mobile-card-detail" role="dialog" aria-modal="true" aria-label="卡牌详情"><header><b>{{ selected.nameZh }}</b><button aria-label="关闭卡牌详情" @click="mobileDetailOpen = false">×</button></header><CardDetailContent :card="selected" :show-catalog-only="false"/></section>
    </div>
    <div v-if="pendingDeleteName" class="builder-modal-mask" @click.self="deletingDeck ? undefined : pendingDeleteName = ''">
      <section class="delete-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="delete-deck-title">
        <h2 id="delete-deck-title">删除〈{{ pendingDeleteName }}〉？</h2>
        <p>牌库删除后不可找回</p>
        <footer><button class="danger" :disabled="deletingDeck" @click="confirmDelete">{{ deletingDeck ? '删除中…' : '继续删除' }}</button><button :disabled="deletingDeck" @click="pendingDeleteName = ''">取消</button></footer>
      </section>
    </div>
    <div v-if="deckImageUrl" class="builder-modal-mask" @click.self="closeDeckImage">
      <section class="deck-image-dialog" role="dialog" aria-modal="true" aria-labelledby="deck-image-title">
        <header><h2 id="deck-image-title">牌库图预览</h2><button aria-label="关闭" @click="closeDeckImage">×</button></header>
        <img :src="deckImageUrl" :alt="`${deckName}牌库图`"/>
        <footer><button class="primary" @click="saveGeneratedDeckImage">下载牌库图</button><button @click="closeDeckImage">关闭</button></footer>
      </section>
    </div>
  </div>
</template>

<style scoped>
.selected-extra-cards{grid-template-rows:auto minmax(0,1fr)}
.portrait-guide{position:fixed;z-index:3000;inset:0;display:grid;place-items:center;padding:max(18px,env(safe-area-inset-top)) max(18px,env(safe-area-inset-right)) max(18px,env(safe-area-inset-bottom)) max(18px,env(safe-area-inset-left));background:#020609dd;backdrop-filter:blur(8px)}.portrait-guide section{box-sizing:border-box;width:min(420px,100%);padding:22px;border:1px solid #d2b25c;background:#11191d;box-shadow:0 20px 70px #000;text-align:center}.portrait-guide small{color:#62c7ce;font-size:11px;font-weight:900;letter-spacing:.15em}.portrait-guide h2{margin:8px 0;font-size:22px}.portrait-guide p{color:#9aa5a3;font-size:13px;line-height:1.7}.portrait-guide button{min-width:140px;min-height:44px;margin-top:8px;border:1px solid #d2b25c;background:#d2b25c;color:#101313;font-weight:900}
.selected-extra-cards>header{flex-wrap:wrap;gap:6px}
.deck-entry-row>strong{flex:none;white-space:nowrap}
.deck-entry-row>div>small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.deck-builder-shell{position:absolute;inset:0;display:flex;flex-direction:column;overflow:hidden;background:radial-gradient(circle at 50% 0,rgba(22,108,120,.2),transparent 38%),linear-gradient(135deg,#080b0d,#160b0d 58%,#071216);color:#eee}
.deck-builder-topbar{height:74px;flex:none;display:flex;align-items:center;gap:18px;padding:10px 20px;border-bottom:1px solid #675f59;background:rgba(8,10,12,.94)}
.deck-builder-topbar>div:nth-child(2){margin-right:auto}.deck-builder-topbar small,.kicker{color:#c7a85d;font-size:14px;font-weight:900;letter-spacing:.18em}.deck-builder-topbar h1{margin:2px 0 0;font-size:23px}.deck-builder-topbar label{display:grid;gap:4px;color:#b6bab6;font-size:14px;font-weight:900}.deck-builder-topbar input{width:260px;min-height:38px;padding:8px 11px;font-size:15px;font-weight:900}.deck-builder-topbar button{padding:9px 14px}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111;font-weight:900}.deck-builder-topbar .primary:disabled{opacity:.3}.deck-total{display:flex;align-items:baseline;gap:4px;color:#bc5961}.deck-total.valid{color:#5cc1b8}.deck-total b{font-size:25px}.deck-total span{font-size:14px}
.deck-loading{display:grid;flex:1;place-items:center;color:#b7b9b5}.deck-builder-grid{display:grid;grid-template-columns:260px minmax(480px,1fr) 330px;gap:10px;min-height:0;padding:10px}.deck-builder-grid .grand-panel{min-height:0;padding:13px;border-radius:2px}.deck-builder-grid h2{margin:3px 0 12px;font-size:18px}.deck-side-column{display:grid;grid-template-rows:minmax(280px,1fr) minmax(160px,.72fr);gap:10px;min-height:0}.deck-detail-panel,.saved-decks-panel{display:flex;min-height:0;flex-direction:column;overflow:hidden}.empty-detail{color:#7f8985;font-size:14px;line-height:1.6}.saved-list{display:grid;gap:5px;overflow-y:auto;overscroll-behavior:contain;padding-right:3px}.saved-list article{border:1px solid #353c3e;background:#111619}.saved-list article>button:first-child{display:grid;width:100%;grid-template-columns:34px minmax(0,1fr);align-items:center;gap:7px;padding:7px;text-align:left}.saved-list article>button:first-child>img{width:34px;height:34px;object-fit:cover;border:1px solid #535e5b;border-radius:2px}.saved-deck-copy{display:grid;min-width:0;gap:2px}.saved-deck-copy b,.saved-deck-copy small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.saved-deck-copy small{color:#89938f;font-size:14px}.saved-list span{color:#737d79;font-size:14px}.saved-list article{display:flex}.saved-list .delete{width:32px;border:0;border-left:1px solid #353c3e;color:#bd5961}.saved-list p{color:#666;font-size:14px}
.deck-catalog{display:flex;flex-direction:column;overflow:hidden}.current-deck-summary{display:flex;flex:none;align-items:center;align-self:flex-start;min-width:250px;gap:10px;margin:0 0 10px;padding:8px 12px 8px 8px;border-left:2px solid #42abb3;background:#10191b;text-align:left}.current-deck-summary img{width:52px;height:52px;object-fit:cover;border-radius:2px}.current-deck-summary div{display:grid;gap:2px}.current-deck-summary small{color:#6bc5ca;font-size:14px;font-weight:900;letter-spacing:.12em}.current-deck-summary b{font-size:14px}.current-deck-summary span{color:#9aa5a1;font-size:14px}.deck-catalog>header,.deck-list>header{display:flex;align-items:center;justify-content:space-between;flex:none}.deck-catalog>header span{color:#7f8985;font-size:14px}.catalog-filter-bar{display:grid;grid-template-columns:minmax(180px,1.6fr) minmax(150px,1.2fr) repeat(4,minmax(86px,.75fr)) 62px;gap:7px;align-items:end;margin:0 0 10px;padding:9px;border:1px solid #354041;background:#0b1112}.catalog-filter-bar label{display:grid;gap:4px;min-width:0;color:#959f9b;font-size:14px;font-weight:900}.catalog-filter-bar input,.catalog-filter-bar select{width:100%;min-width:0;height:32px}.catalog-filter-bar .filter-reset{height:32px;min-height:32px}.product-filter{display:flex;min-width:0;flex-wrap:wrap;gap:3px;margin:0;padding:0;border:0}.product-filter legend{width:100%;margin-bottom:1px;color:#959f9b;font-size:14px;font-weight:900}.product-filter button{min-height:28px;padding:3px 7px;border:1px solid #4a5552;background:#141a1b;color:#b8bfbb;font-size:12px;font-weight:900}.product-filter button.active{border-color:#70d7df;background:#174e54;color:#fff}.deck-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(118px,1fr));gap:9px;overflow:auto;padding:3px 4px 20px}.deck-card{min-width:0;border:1px solid #303638;background:#101416;box-shadow:3px 3px 0 #050607}.deck-card.chosen{border-color:#c5a456}.deck-card.alternate-art-card{border-color:#5c4d2d;background:#17140e}.card-image{position:relative;display:block;width:100%;aspect-ratio:5/7;overflow:hidden;border:0;background:#171d1f}.card-image>.l12-card-image,.card-image>img{width:100%;height:100%;object-fit:cover}.card-image>span{display:grid;height:100%;place-items:center;font-size:24px}.copy-count{position:absolute;right:4px;top:4px;padding:3px 6px;background:#07181a;color:#71d1d0}.deck-card>div{display:grid;gap:2px;padding:7px}.deck-card>div b{overflow:hidden;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.deck-card>div b em{margin-left:5px;padding:1px 4px;border:1px solid #a9883c;color:#dfc66f;font-size:11px;font-style:normal}.deck-card>div small{color:#757d79;font-size:14px}.add-card{width:100%;padding:6px;border:0;border-top:1px solid #303638;color:#cdbb89;font-size:14px}.add-card:disabled{color:#4d5351}
.deck-card.landscape-thumbnail .card-image>.l12-card-image{width:100%;height:100%;transform:none}
.deck-list{display:flex;flex-direction:column;overflow:hidden}.deck-list>header>b{font-size:27px;color:#65c4c3}.cost-curve{display:flex;height:88px;align-items:end;gap:5px;padding:8px 4px;border-top:1px solid #333;border-bottom:1px solid #333}.cost-curve i{display:grid;flex:1;align-items:end;justify-items:center;height:68px;font-style:normal}.cost-curve i span{width:100%;max-width:22px;background:linear-gradient(#d2b560,#7f6530)}.cost-curve i b,.cost-curve i small{font-size:14px}.cost-curve i small{color:#777}.deck-entries{flex:1;overflow:auto;padding:7px 0}.deck-entry-row{display:flex;position:relative;isolation:isolate;align-items:center;gap:7px;margin-bottom:5px;padding:6px;overflow:hidden;border:1px solid #354041;border-left:2px solid #3da4ad;background:#111719}.deck-entry-row>span{display:grid;width:27px;height:27px;flex:none;place-items:center;background:#080a0b;color:#eee;font-weight:900}.deck-entry-row>div{display:grid;min-width:0;flex:1}.deck-entry-row>div b{overflow:hidden;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.deck-entry-row small{color:#d0d5d1;font-size:14px}.deck-entry-row strong{color:#f0d98e}.deck-entry-row button{width:27px;height:27px;flex:none;border:1px solid #5c6461;background:#101516;color:#eee;font-size:15px;font-weight:900}.deck-entry-row button:hover:not(:disabled){border-color:#70d7df;background:#1b565b}.deck-entry-row button:disabled{color:#9a8b64;opacity:.78}.deck-entries>p{color:#69716e;font-size:14px;line-height:1.6}.builder-card-detail.archive-detail{display:block;min-height:0;flex:1;margin:0 -1px;padding-top:8px;border-top:1px solid #3d4241;border-left:0;overflow-x:hidden;overflow-y:auto;background:#0b0f10}.builder-card-detail :deep(.card-detail-copy){min-width:0}.builder-card-detail :deep(.archive-tags){flex-wrap:wrap}.builder-card-detail :deep(.archive-effect p){white-space:pre-line;overflow-wrap:anywhere}.deck-list footer{min-height:32px;padding:8px;border-top:1px solid #315854;color:#72c8bd;font-size:14px}.deck-list footer.error{border-color:#673a3d;color:#d2757b}
@media(max-width:1180px){.deck-builder-grid{grid-template-columns:210px minmax(420px,1fr) 290px}.deck-builder-topbar label:not(.saved-deck-switcher){display:none}}
@media(max-width:820px){
  .deck-builder-shell{position:fixed;overflow:auto}.deck-builder-topbar{position:sticky;z-index:20;top:0;height:64px;padding:8px;gap:8px}.deck-builder-topbar>div:nth-child(2) small{display:none}.deck-builder-topbar h1{font-size:18px}.deck-builder-topbar button{padding:7px 9px}.deck-total b{font-size:20px}
  .deck-builder-grid{display:flex;flex-direction:column;overflow:visible;padding:8px}.deck-builder-grid .grand-panel{overflow:visible}.deck-side-column{display:contents}.deck-detail-panel{order:1;min-height:520px}.saved-decks-panel{order:4;max-height:360px}.deck-catalog{order:2;min-height:72vh}.deck-list{order:3;min-height:70vh}.deck-card-grid{grid-template-columns:repeat(3,minmax(92px,1fr));max-height:68vh}.deck-entries{max-height:46vh}.current-deck-summary img{width:64px;height:64px}.catalog-filter-bar{grid-template-columns:repeat(2,minmax(0,1fr))}.catalog-filter-bar .filter-search{grid-column:1/-1}
}
.deck-builder-topbar button{padding:8px 11px;border:1px solid #69716e;background:#171c1d;color:#f1eee5;font-weight:900}.deck-builder-topbar button:hover:not(:disabled){border-color:#70d7df;background:#1b565b;color:#fff}.deck-builder-topbar .back-button{border-color:#d7d2c4;background:#e8e3d7;color:#101314}.deck-builder-topbar .primary{border-color:#e4dfd0;background:#e4dfd0;color:#111}.deck-builder-topbar .delete-deck{border-color:#8c343c;color:#f1a3aa}.deck-builder-topbar button:disabled{color:#717775;background:#252929;opacity:.45}.deck-file-actions{display:flex;gap:6px}.filter-reset{width:100%;min-height:34px;border:1px solid #5d6865;background:#161c1d;color:#eee;font-weight:900}.filter-reset:hover{border-color:#70d7df;background:#1b565b}
.pool-count-controls{display:grid!important;grid-template-columns:1fr 34px 1fr;gap:0!important;padding:0!important;border-top:1px solid #303638}.pool-count-controls button{min-height:30px;border:0;background:#151a1b;color:#e8e4d9;font-size:17px;font-weight:900}.pool-count-controls button:hover:not(:disabled){background:#1d6167;color:#fff}.pool-count-controls strong{display:grid;place-items:center;border-inline:1px solid #303638;background:#090c0d;color:#d7c483;font-size:14px}
.saved-list article{border-color:#424b4d;background:#111619}.saved-list article>button:first-child{background:#111619;color:#f1eee5}.saved-list article>button:first-child:hover,.saved-list article>button:first-child:focus-visible{border-color:#70d7df;background:#18383b;color:#fff}.saved-list b{color:#f1eee5}.saved-list span{color:#aab4b0}.saved-list article.active{border-color:#86e8ee;background:#123e42;box-shadow:inset 3px 0 #86e8ee}.saved-list article.active>button:first-child{background:#123e42;color:#fff}.saved-list article.active span{color:#d5f4f1}.saved-list .delete{background:#211418;color:#f29ba4}.saved-list .delete:hover{background:#6b222b;color:#fff}.saved-list p{color:#929b97}
.deck-entry-banner{position:absolute;z-index:-2;inset:0;width:100%;height:100%;object-fit:cover;object-position:center 28%;opacity:.56;filter:saturate(.9) contrast(1.12)}.deck-entry-row::after{content:'';position:absolute;z-index:-1;inset:0;background:linear-gradient(90deg,rgba(5,8,9,.91),rgba(9,13,14,.48) 48%,rgba(5,8,9,.88))}.deck-extra-entries :deep(.deck-entry-banner.landscape-thumbnail-image){left:0;top:0;width:100%;height:100%;transform:none}
@media(max-width:1180px){.deck-file-actions button{padding:7px 8px;font-size:14px}}
@media(max-width:820px){.deck-builder-topbar{height:auto;min-height:64px;flex-wrap:wrap}.deck-file-actions{order:5;width:100%;display:grid;grid-template-columns:repeat(3,1fr)}}
.trial-builder{margin:12px 0;padding:10px;border:1px solid #42605a;background:#0a1212}.trial-builder>header,.selected-trials>header{display:flex;align-items:center;justify-content:space-between}.trial-builder>header span,.selected-trials>header span{color:#78d2be;font-size:14px;font-weight:900}.trial-builder>p{margin:5px 0 9px;color:#84918c;font-size:14px;line-height:1.5}.trial-options{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}.trial-options button{min-width:0;padding:6px;border:1px solid #384744;background:#101817;color:#e9e5dc;text-align:left}.trial-options button.selected{border-color:#6cd5b4;background:#17332c}.trial-options b,.trial-options small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:14px}.trial-options small{margin-top:3px;color:#85908c;font-size:14px}.trial-thumb{display:block;width:100%;aspect-ratio:8/5;margin-bottom:5px;overflow:hidden;background:#080b0b}.trial-thumb img{width:100%;height:100%;object-fit:contain}
.selected-extra-cards{flex:none;display:grid;gap:4px;max-height:150px;padding:8px 0;border-top:1px solid #3d4241}.selected-extra-cards>header{display:flex;align-items:center;justify-content:space-between}.selected-extra-cards>header span{color:#78d2be;font-size:14px;font-weight:900}.deck-extra-entries{overflow-y:auto;overscroll-behavior:contain}
.trial-thumb.upright img{width:100%;height:100%;object-fit:cover;transform:translate(-50%,-50%)}.automatic-extra-builder{border-color:#8a6a3d}
.catalog-tabs{display:grid;grid-template-columns:repeat(3,1fr);gap:5px;margin:0 0 10px}.catalog-tabs button,.choose-special{min-height:32px;border:1px solid #48504e;background:#101617;color:#c8cfcb;font-weight:900}.catalog-tabs button.active,.choose-special:hover:not(:disabled){border-color:#73d4d8;background:#194b50;color:#fff}.choose-special{width:100%;border-width:1px 0 0}.empty-extra{grid-column:1/-1;padding:32px;color:#8b9490;text-align:center}
.builder-modal-mask{position:fixed;z-index:1000;inset:0;display:grid;place-items:center;padding:20px;background:rgba(0,4,7,.82);backdrop-filter:blur(6px)}.delete-confirm-dialog,.deck-image-dialog{width:min(92vw,520px);border:1px solid #8b7650;background:#111719;color:#f4f0e6;box-shadow:0 22px 80px #000;padding:22px}.delete-confirm-dialog h2,.deck-image-dialog h2{margin:0;font-size:20px}.delete-confirm-dialog p{margin:18px 0;color:#d9c9af;font-weight:900}.delete-confirm-dialog footer,.deck-image-dialog footer{display:flex;justify-content:center;gap:10px}.delete-confirm-dialog button,.deck-image-dialog button{min-width:120px;padding:10px 16px;border:1px solid #66716e;background:#171d1f;color:#f4f0e6;font-weight:900}.delete-confirm-dialog .danger{border-color:#a23943;background:#681f28;color:#fff}.deck-image-dialog{width:min(94vw,1100px)}.deck-image-dialog>header{display:flex;align-items:center;justify-content:space-between;margin-bottom:14px}.deck-image-dialog>header button{min-width:42px}.deck-image-dialog>img{display:block;width:100%;max-height:70vh;object-fit:contain;background:#070a0c}.deck-image-dialog footer{margin-top:14px}.deck-image-dialog .primary{border-color:#d9bc72;background:#d9bc72;color:#111}
.saved-list article>button:first-child{display:block;padding:0}.saved-list article>button:first-child :deep(.deck-profile){border:0;background:transparent}.saved-list article.active>button:first-child :deep(.deck-profile){background:#123e42}
.saved-deck-switcher select{min-width:150px;max-width:210px;height:38px}.deck-center-column{display:flex;min-width:0;min-height:0;flex-direction:column;gap:7px}.workspace-tabs{display:grid;grid-template-columns:repeat(3,1fr);gap:5px;flex:none}.workspace-tabs button,.deck-mobile-nav button{min-height:36px;border:1px solid #48504e;background:#101617;color:#c8cfcb;font-weight:900}.workspace-tabs button.active,.deck-mobile-nav button.active{border-color:#73d4d8;background:#194b50;color:#fff}.deck-center-column>.grand-panel{flex:1}.deck-mobile-nav,.mobile-filter-trigger,.mobile-filter-header,.mobile-filter-apply{display:none}.catalog-filter-bar{grid-template-columns:repeat(auto-fit,minmax(92px,1fr))}.catalog-filter-bar .filter-search{grid-column:span 2}.catalog-filter-bar .product-filter{grid-column:span 2}.deck-card.invalid,.deck-entry-row.invalid{border-color:#a74a52}.entry-issue,.deck-entry-row em{color:#f09199;font-size:12px;font-style:normal}.add-to-bench{width:100%;min-height:30px;border:0;border-top:1px solid #303638;background:#111719;color:#cdbb89;font-weight:900}.deck-sections{min-height:0;flex:1;overflow:auto;padding:7px 0}.deck-zone{border-bottom:1px solid #343b3c}.deck-zone>header{display:grid;gap:3px;padding:5px 0}.deck-zone>header>button{display:grid;grid-template-columns:1fr auto auto;gap:8px;align-items:center;width:100%;min-height:32px;border:0;background:#0b1112;color:#eee;text-align:left}.deck-zone>header>button b{color:#74ccc8}.deck-zone>header>button i{color:#8d9692;font-size:12px;font-style:normal}.deck-zone>header>small{color:#8d9692;font-size:12px}.deck-zone-body>p{color:#77817d;font-size:13px}.deck-zone .deck-entry-row button[aria-label="加入主牌库"]{width:42px;font-size:12px}.deck-insight-panel{overflow:auto}.deck-insight-panel>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.deck-insight-panel>header>strong{max-width:55%;color:#e07e86;text-align:right}.deck-insight-panel>header>strong.valid{color:#72c8bd}.stats-summary{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.stats-summary article,.stats-block{padding:12px;border:1px solid #354041;background:#0b1112}.stats-summary article{display:grid;gap:5px}.stats-summary small,.stats-summary span{color:#8f9995}.stats-summary b{font-size:24px}.stats-block{margin-top:10px}.stats-block h3{margin:0 0 10px}.cost-curve.expanded{height:154px}.cost-curve.expanded i{height:135px}.stats-columns{display:grid;grid-template-columns:1fr 1fr;gap:10px}.stats-columns .stats-block article{display:flex;justify-content:space-between;padding:7px 0;border-bottom:1px solid #2c3434}.hand-workspace>header p{margin:4px 0;color:#8f9995}.hand-workspace>header button{min-height:38px}.editor-opening-hand{display:grid;grid-template-columns:repeat(6,minmax(90px,1fr));gap:10px;margin-top:18px}.editor-opening-hand article{display:grid;gap:5px;min-width:0}.editor-opening-hand :deep(.l12-card-image){width:100%;aspect-ratio:5/7;object-fit:cover}.editor-opening-hand b,.editor-opening-hand small{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.editor-opening-hand small{color:#8f9995}.mobile-card-detail{width:min(94vw,620px);max-height:90vh;overflow:auto;padding:14px;border:1px solid #6c7774;background:#0b1112}.mobile-card-detail>header{display:flex;position:sticky;z-index:1;top:-14px;align-items:center;justify-content:space-between;padding:8px;background:#0b1112}.mobile-card-detail>header button{width:40px;height:40px}.mobile-card-detail :deep(.archive-detail){grid-template-columns:1fr}
@media(max-width:820px){
  .deck-builder-shell{overflow:hidden}.deck-builder-topbar{position:relative;z-index:20;max-height:42vh;overflow:auto}.deck-builder-topbar>div:nth-child(2),.deck-total{display:none}.deck-builder-topbar .saved-deck-switcher{display:grid!important;min-width:0;flex:1}.saved-deck-switcher select{width:100%;min-width:0;max-width:none}.deck-file-actions{flex:none}
  .deck-mobile-nav{display:grid;grid-template-columns:repeat(3,1fr);gap:4px;padding:6px 8px;border-bottom:1px solid #394141;background:#080d0e}.deck-mobile-nav button{min-height:42px}
  .deck-builder-grid{display:block;flex:1;min-height:0;overflow:auto;padding:8px}.deck-side-column{display:none}.deck-center-column{min-height:100%}.deck-center-column>.grand-panel{min-height:0}.deck-list{min-height:100%;overflow:visible}.deck-builder-grid[data-mobile-pane="pool"] .deck-list,.deck-builder-grid[data-mobile-pane="insights"] .deck-list,.deck-builder-grid[data-mobile-pane="deck"] .deck-center-column{display:none}.deck-builder-grid[data-mobile-pane="pool"] .workspace-tabs{display:none}.deck-builder-grid[data-mobile-pane="insights"] .workspace-tabs button:first-child{display:none}.deck-builder-grid[data-mobile-pane="insights"] .workspace-tabs{grid-template-columns:repeat(2,1fr)}
  .deck-catalog{min-height:100%;overflow:visible}.deck-card-grid{grid-template-columns:repeat(2,minmax(0,1fr));max-height:none;overflow:visible}.current-deck-summary{min-width:0}.mobile-filter-trigger{display:flex;align-items:center;justify-content:center;gap:6px;min-height:42px;margin-bottom:8px}.mobile-filter-trigger span{display:grid;width:22px;height:22px;place-items:center;border-radius:50%;background:#194b50}.catalog-filter-bar{display:none;position:fixed;z-index:1200;right:0;bottom:0;left:0;grid-template-columns:repeat(2,minmax(0,1fr));max-height:min(82vh,680px);margin:0;padding:14px max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(14px,env(safe-area-inset-left));overflow:auto;border:1px solid #6a7773;background:#0b1112;box-shadow:0 -18px 60px #000}.catalog-filter-bar.mobile-open{display:grid}.mobile-filter-header{display:flex;grid-column:1/-1;align-items:center;justify-content:space-between}.mobile-filter-header button{width:40px;height:40px}.catalog-filter-bar .filter-search,.catalog-filter-bar .product-filter,.mobile-filter-apply{grid-column:1/-1}.mobile-filter-apply{display:block;min-height:44px}.stats-summary{grid-template-columns:repeat(2,1fr)}.stats-columns{grid-template-columns:1fr}.editor-opening-hand{grid-template-columns:repeat(2,minmax(0,1fr))}.deck-list>.cost-curve{display:none}.deck-sections{overflow:visible}.deck-entry-row button{width:34px;height:34px}.deck-zone .deck-entry-row button[aria-label="加入主牌库"]{width:48px}.mobile-card-detail-mask{padding:max(10px,env(safe-area-inset-top)) max(10px,env(safe-area-inset-right)) max(10px,env(safe-area-inset-bottom)) max(10px,env(safe-area-inset-left))}
}
@media(min-width:821px) and (max-width:1439px){.deck-builder-grid{grid-template-columns:210px minmax(420px,1fr) 300px}.editor-opening-hand{grid-template-columns:repeat(3,1fr)}.stats-summary{grid-template-columns:repeat(2,1fr)}}
@media(max-height:520px) and (min-width:821px) and (max-width:900px){.deck-builder-topbar{height:auto;min-height:58px;padding:6px 8px;gap:7px}.deck-builder-topbar>div:nth-child(2),.deck-total{display:none}.deck-builder-topbar .saved-deck-switcher{display:grid!important}.deck-file-actions{min-width:0;overflow-x:auto}.deck-builder-grid{grid-template-columns:minmax(0,1fr) 280px;padding:6px}.deck-side-column{display:none}.workspace-tabs button{min-height:30px}.deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(94px,1fr))}.catalog-filter-bar{grid-template-columns:repeat(4,minmax(76px,1fr));max-height:118px;overflow:auto}.deck-list>.cost-curve{display:none}}
.deck-builder-grid{grid-template-columns:calc(var(--l12-card-detail-sidebar-width,274px) - 56px) minmax(480px,1fr) var(--l12-card-detail-sidebar-width,274px)}
.workspace-tabs{grid-template-columns:repeat(auto-fit,minmax(120px,1fr))}
.catalog-filter-controls{display:flex;align-items:center;gap:8px;margin-bottom:10px;min-width:0}.catalog-filter-controls :deep(.mobile-filter-trigger){display:inline-flex}.catalog-filter-summary{min-width:0;overflow:hidden;border:0;background:transparent;color:#b9c6c4;font-size:12px;text-align:left;text-overflow:ellipsis;white-space:nowrap}.catalog-filter-clear{flex:none;border:0;background:transparent;color:#74d1d6;font-weight:900}.catalog-filter-bar{grid-template-columns:repeat(2,minmax(0,1fr));margin:0;padding:0;border:0;background:transparent}.catalog-filter-bar .filter-search,.catalog-filter-bar .product-filter{grid-column:1/-1}.deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(138px,1fr));gap:10px}.deck-card.landscape-thumbnail .card-image{aspect-ratio:8/5}.deck-card.landscape-thumbnail .card-image>.l12-card-image{position:static;width:100%;height:100%;transform:none}.deck-card>div b,.deck-card>div small{display:block;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.trial-thumb img{position:static;width:100%;height:100%;transform:none}.trial-thumb{aspect-ratio:8/5}
@media(max-width:1180px) and (min-width:821px){.deck-builder-grid{grid-template-columns:200px minmax(420px,1fr) 260px}}
@media(max-width:820px){.deck-mobile-nav{grid-template-columns:repeat(auto-fit,minmax(86px,1fr))}.catalog-filter-bar{display:grid;position:static;max-height:none;padding:0;overflow:visible;border:0;box-shadow:none}.deck-card-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.catalog-filter-controls{align-items:stretch}.catalog-filter-summary{flex:1}.deck-builder-grid[data-mobile-pane="insights"] .deck-center-column{display:flex}.deck-builder-grid[data-mobile-pane="insights"] .deck-catalog{display:none}}
@media(max-height:520px) and (min-width:821px) and (max-width:900px){.deck-builder-grid{grid-template-columns:minmax(0,1fr) 260px}.deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(108px,1fr))}}
</style>
