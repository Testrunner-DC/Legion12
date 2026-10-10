<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { cancelMatchmaking, connect, createRoom, joinMatchmaking, joinRoom, l12State, leaveRoom, selectCustomDeck, setReady, spectateRoom, updateRoomOptions, type RoomOptions } from '@/l12/net'
import { deckCountSummary, ensureOfficialPrebuiltDecks, L12_DECK_SELECTION_SCOPES, loadDeckCatalog, loadPrivateDeckBody,
  loadSavedDecksState, deckErrorBelongsToCurrentAccount, loadSelectedDeckName, saveSelectedDeckName, SELECTED_DECK_KEY,
  validateDeck, type DeckCard, type L12DeckSelectionScope, type PrivateDeckSummary, type SavedL12Deck } from '@/l12/decks'
import DeckProfile from '@/l12/DeckProfile.vue'
import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import { getEffectiveOperationsPolicy, normalizeRankedConfig, platformState, rankedApi, type EffectiveOperationsPolicy, type RankedOverview } from '@/l12/platform'
import RankedBroadcastTicker from './RankedBroadcastTicker.vue'
import { maintenanceCountdown } from './maintenanceCountdown'

const router = useRouter()
const tab = ref<'match' | 'friendly' | 'sandbox'>('match')
const roomCode = ref('')
const roomOptions = ref<RoomOptions>({ matchModeId: 'friendly', spectating: 'public', handVisibility: 'request', disasterMode: 'all', useCardRestrictions: false })
const operationsPolicy = ref<EffectiveOperationsPolicy | null>(null)
const policyError = ref('')
const policyNow = ref(Date.now())
const maintenanceView = computed(() => operationsPolicy.value
  ? maintenanceCountdown(operationsPolicy.value.maintenance, policyNow.value)
  : null)
const maintenanceActive = computed(() => operationsPolicy.value?.maintenance.entryBlocked === true)

type ModeDeckSelection = {
  id: string
  revision: number
  name: string
  body: SavedL12Deck
  summary?: PrivateDeckSummary
}
type DeckSelectionCandidate = PrivateDeckSummary | SavedL12Deck

function emptySelections(): Record<L12DeckSelectionScope, ModeDeckSelection | null> {
  return { ranked: null, casual: null, friendly: null, 'sandbox-player': null, 'sandbox-opponent': null }
}
function emptySelectionMessages(): Record<L12DeckSelectionScope, string> {
  return { ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '' }
}
function visibleDecks(): Record<string, SavedL12Deck> {
  const snapshot = loadSavedDecksState()
  if (snapshot.status === 'unavailable') { l12State.notice = snapshot.error.message; return {} }
  return snapshot.decks
}
const cachedDecks = ref(visibleDecks())
const catalog = ref<DeckCard[]>([])
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const detailCard = ref<DeckCard | null>(null)
const seasonDisasterCards = computed(() => (operationsPolicy.value?.disasterCardIds || [])
  .map(cardId => byId.value.get(cardId)).filter((card): card is DeckCard => Boolean(card)))
const seasonRestrictionCards = computed(() => (operationsPolicy.value?.cardRestrictions || [])
  .map(rule => ({ rule, card: byId.value.get(rule.cardId), master: rule.masterId ? byId.value.get(rule.masterId) : undefined }))
  .filter((item): item is typeof item & { card: DeckCard } => Boolean(item.card)))
const seasonTimeFormatter = new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', hour12: false,
})
function formatSeasonBoundary(value?: string) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : seasonTimeFormatter.format(date)
}
function formatSeasonPeriod() {
  const startsAt = formatSeasonBoundary(operationsPolicy.value?.season.startsAt)
  const endsAt = formatSeasonBoundary(operationsPolicy.value?.season.endsAt)
  if (startsAt && endsAt) return `${startsAt} 至 ${endsAt}（UTC+8）`
  if (startsAt) return `${startsAt} 起，未设置结束时间（UTC+8）`
  if (endsAt) return `截至 ${endsAt}（UTC+8）`
  return '未设置固定起止时间'
}
const ranked = ref<RankedOverview | null>(null)
function normalizeRankedOverview(view: RankedOverview): RankedOverview {
  return { ...view, config: normalizeRankedConfig(view.config) }
}
function formatRankedDuration(seconds: number) {
  if (seconds % 60 === 0) return `${seconds / 60} 分钟`
  if (seconds > 60) return `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`
  return `${seconds} 秒`
}
const selectedMatchMode = ref<'ranked' | 'casual'>('ranked')
const roomCodeCopied = ref(false)
const rankedRulesOpen = ref(false)
const changingFaction = ref(false)
const pendingFaction = ref<'order' | 'chaos' | 'fate' | null>(null)
const factionSaving = ref(false)
const pendingFactionName = computed(() => ranked.value?.config.factions.find(item => item.id === pendingFaction.value)?.name || '')
const selectedDeckNames = ref<Record<L12DeckSelectionScope, string>>({
  ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '',
})
const selectedDecks = ref<Record<L12DeckSelectionScope, ModeDeckSelection | null>>(emptySelections())
const selectionMessages = ref<Record<L12DeckSelectionScope, string>>(emptySelectionMessages())
const deckSelectorOpen = ref(false)
const deckSelectorScope = ref<L12DeckSelectionScope>('friendly')
const deckSelectorConfirming = ref(false)
const deckSelectorError = ref('')
const modeActionPending = ref(false)
const guestDeckList = computed(() => platformState.account?.id && platformState.token ? [] : Object.values(cachedDecks.value))
const activeDeckScope = computed<L12DeckSelectionScope>(() => {
  if (l12State.room || tab.value === 'friendly') return 'friendly'
  if (tab.value === 'sandbox') return 'sandbox-player'
  return selectedMatchMode.value
})
const currentSelection = computed(() => selectedDecks.value[activeDeckScope.value])
const currentDeck = computed(() => currentSelection.value?.body)
const selectorCurrentName = computed(() => selectedDeckNames.value[deckSelectorScope.value])
const me = computed(() => l12State.room?.players.find(player => player.playerIndex === l12State.room?.yourPlayerIndex))
const isRoomHost = computed(() => l12State.room?.yourPlayerIndex === 0)
const editableRoomOptions = ref<RoomOptions>({ ...roomOptions.value })

function friendlyUsesRestrictions() {
  return l12State.room?.options?.useCardRestrictions ?? roomOptions.value.useCardRestrictions
}
function scopeNeedsPolicy(scope: L12DeckSelectionScope) {
  return scope === 'ranked' || (scope === 'friendly' && friendlyUsesRestrictions())
}
function restrictionsForScope(scope: L12DeckSelectionScope) {
  if (scope === 'ranked' || (scope === 'friendly' && friendlyUsesRestrictions()))
    return operationsPolicy.value?.cardRestrictions ?? []
  return []
}
function friendlyRulesKey() {
  return friendlyUsesRestrictions()
    ? `restricted:${operationsPolicy.value?.version ?? 'unversioned'}`
    : 'open'
}
function deckError(deck: SavedL12Deck | undefined, scope = activeDeckScope.value) {
  if (selectionMessages.value[scope]) return selectionMessages.value[scope]
  if (!deck) return '尚未选择牌库'
  if (!catalog.value.length || (scopeNeedsPolicy(scope) && !operationsPolicy.value)) return '正在加载当前模式规则'
  return validateDeck(deck, catalog.value, restrictionsForScope(scope))
}
const currentDeckError = computed(() => deckError(currentDeck.value))
const selectorRestrictions = computed(() => restrictionsForScope(deckSelectorScope.value))
const selectorLoading = computed(() => !catalog.value.length
  || (scopeNeedsPolicy(deckSelectorScope.value) && !operationsPolicy.value))

function selectionStorageBase() {
  return platformState.account?.id ? `${SELECTED_DECK_KEY}:${platformState.account.id}` : SELECTED_DECK_KEY
}
function hasStoredSelection(scope: L12DeckSelectionScope) {
  const base = selectionStorageBase()
  try { return Boolean(localStorage.getItem(`${base}:${scope}`) || localStorage.getItem(base)) }
  catch { return false }
}
function hydrateDeckSelections() {
  L12_DECK_SELECTION_SCOPES.forEach(scope => {
    const name = loadSelectedDeckName(scope, cachedDecks.value)
    const deck = name ? cachedDecks.value[name] : undefined
    selectedDeckNames.value[scope] = name
    selectedDecks.value[scope] = deck?.id && deck.revision
      ? { id: deck.id, revision: deck.revision, name: deck.name, body: deck }
      : null
    selectionMessages.value[scope] = !deck && hasStoredSelection(scope)
      ? '原选择缺少可验证的服务器修订，请重新选择牌库'
      : ''
  })
}
function openDeckSelector() {
  if (l12State.room && me.value?.ready) return
  deckSelectorScope.value = activeDeckScope.value
  deckSelectorError.value = ''
  deckSelectorOpen.value = true
}

let componentAlive = true
let identityEpoch = 0
let selectorActionEpoch = 0
let modeActionEpoch = 0

function identityCurrent(epoch: number, accountId: string | undefined, token: string) {
  return componentAlive && identityEpoch === epoch && platformState.account?.id === accountId && platformState.token === token
}
function selectionCurrent(scope: L12DeckSelectionScope, id: string, revision: number) {
  const selection = selectedDecks.value[scope]
  return selection?.id === id && selection.revision === revision
}
function requestStatus(error: unknown) {
  return error && typeof error === 'object' && 'status' in error && typeof error.status === 'number' ? error.status : 0
}
function bodyFailureMessage(error: unknown, scope: L12DeckSelectionScope, markSelection = true) {
  const status = requestStatus(error)
  if (status === 404) {
    const message = '这副牌库当前不可用，请重新选择；其他模式的选择没有改变'
    if (markSelection) selectionMessages.value[scope] = message
    return message
  }
  if (status === 409) {
    const message = '牌库已有新修订，请刷新目录并明确选择；未自动重试旧正文'
    if (markSelection) selectionMessages.value[scope] = message
    return message
  }
  return error instanceof Error ? error.message : '牌库正文暂不可读取'
}
function cachedSelection(deck: SavedL12Deck, summary?: PrivateDeckSummary): ModeDeckSelection {
  if (!deck.id || !deck.revision) throw new Error('牌库缺少稳定身份或修订，请重新选择')
  return { id: deck.id, revision: deck.revision, name: deck.name, body: deck, summary }
}

async function confirmDeckSelection(candidate: DeckSelectionCandidate) {
  if (deckSelectorConfirming.value || (l12State.room && me.value?.ready)) return
  const scope = deckSelectorScope.value
  const action = ++selectorActionEpoch
  const epoch = identityEpoch
  const accountId = platformState.account?.id
  const token = platformState.token
  const id = candidate.id
  const revision = candidate.revision
  const current = () => identityCurrent(epoch, accountId, token) && selectorActionEpoch === action
    && deckSelectorOpen.value && deckSelectorScope.value === scope && !(l12State.room && me.value?.ready)
  deckSelectorConfirming.value = true
  deckSelectorError.value = ''
  try {
    if (!id || !revision) throw new Error('牌库缺少稳定身份或修订，请重新选择')
    const deck = 'counts' in candidate
      ? await loadPrivateDeckBody({ id, revision }, current)
      : candidate
    if (!current()) return
    if (deck.id !== id || deck.revision !== revision) throw new Error('服务器返回了不同的牌库身份或修订，请重新选择')
    const invalid = !catalog.value.length || (scopeNeedsPolicy(scope) && !operationsPolicy.value)
      ? '正在加载当前模式规则'
      : validateDeck(deck, catalog.value, restrictionsForScope(scope))
    if (invalid) { deckSelectorError.value = invalid; return }
    saveSelectedDeckName(scope, deck.name)
    if (!current()) return
    const selection = cachedSelection(deck, 'counts' in candidate ? candidate : undefined)
    const nextCache = { ...cachedDecks.value }
    Object.keys(nextCache).filter(name => nextCache[name]?.id === deck.id).forEach(name => delete nextCache[name])
    nextCache[deck.name] = deck
    cachedDecks.value = nextCache
    selectedDecks.value[scope] = selection
    selectedDeckNames.value[scope] = deck.name
    selectionMessages.value[scope] = ''
    deckSelectorOpen.value = false
  } catch (error) {
    if (current() && deckErrorBelongsToCurrentAccount(error)) deckSelectorError.value = bodyFailureMessage(error, scope, false)
  } finally {
    if (identityCurrent(epoch, accountId, token) && selectorActionEpoch === action) deckSelectorConfirming.value = false
  }
}
function cancelDeckSelection() {
  selectorActionEpoch++
  deckSelectorConfirming.value = false
  deckSelectorError.value = ''
  deckSelectorOpen.value = false
}
function visibleDeckLabel(index: number) {
  const player = l12State.room?.players[index]
  if (!player) return '尚未选择牌库'
  return player.playerIndex === l12State.room?.yourPlayerIndex
    ? (player.deckName || '尚未选择牌库')
    : '已选择牌库'
}
const optionLabels = {
  spectating: { public: '公开观战', friends: '仅好友观战', disabled: '禁止观战' },
  handVisibility: { request: '查看手牌需申请', public: '观战者可看手牌' },
  disasterMode: { all: '全部天灾', random: '随机天灾', season: '赛季天灾', custom: '自定天灾（沙盒）', none: '不使用天灾' },
} as const

let maintenanceClockTimer = 0
let refreshingOperationsPolicy = false
let roomDefaultsHydrated = false
let roomDeckSubmitKey = ''
let roomDeckSyncKey = ''
let roomDeckSyncPromise: Promise<boolean> | null = null

function consumeOperationsPolicy(policy: EffectiveOperationsPolicy) {
  operationsPolicy.value = policy
  policyError.value = ''
  if (roomDefaultsHydrated) return
  roomOptions.value = {
    matchModeId: 'friendly',
    spectating: policy.defaultRoomConfig.spectating,
    handVisibility: policy.defaultRoomConfig.handVisibility,
    disasterMode: ['all', 'random', 'season', 'none'].includes(policy.defaultRoomConfig.disasterMode)
      ? policy.defaultRoomConfig.disasterMode as RoomOptions['disasterMode'] : 'all',
    useCardRestrictions: false,
  }
  roomDefaultsHydrated = true
}

async function refreshOperationsPolicy() {
  if (refreshingOperationsPolicy) return
  refreshingOperationsPolicy = true
  try {
    const policy = await getEffectiveOperationsPolicy()
    consumeOperationsPolicy(policy)
    l12State.operationsPolicy = policy
  } catch (error) {
    policyError.value = error instanceof Error ? error.message : '运营规则加载失败'
  } finally {
    refreshingOperationsPolicy = false
  }
}

watch(() => l12State.operationsPolicy, policy => {
  if (policy) consumeOperationsPolicy(policy)
}, { immediate: true })

watch(() => l12State.room?.options, options => {
  if (!options) return
  editableRoomOptions.value = {
    matchModeId: 'friendly',
    spectating: options.spectating,
    handVisibility: options.handVisibility,
    disasterMode: options.disasterMode === 'custom' ? 'all' : options.disasterMode,
    useCardRestrictions: options.useCardRestrictions === true,
  }
}, { immediate: true, deep: true })

async function refreshGuestDecks(epoch: number, accountId: string | undefined, token: string) {
  if (accountId || token) return
  try {
    const decks = await ensureOfficialPrebuiltDecks()
    if (!identityCurrent(epoch, accountId, token)) return
    cachedDecks.value = decks
    hydrateDeckSelections()
  } catch (error) {
    if (identityCurrent(epoch, accountId, token) && deckErrorBelongsToCurrentAccount(error))
      l12State.notice = error instanceof Error ? error.message : '牌库暂不可读取'
  }
}

hydrateDeckSelections()
watch(() => [platformState.account?.id, platformState.token] as const, ([accountId, token]) => {
  identityEpoch++
  selectorActionEpoch++
  modeActionEpoch++
  roomDeckSubmitKey = ''
  roomDeckSyncKey = ''
  roomDeckSyncPromise = null
  deckSelectorOpen.value = false
  deckSelectorConfirming.value = false
  deckSelectorError.value = ''
  cachedDecks.value = visibleDecks()
  try { hydrateDeckSelections() }
  catch (error) { l12State.notice = error instanceof Error ? error.message : '牌库选择暂不可读取' }
  if (!accountId && !token) void refreshGuestDecks(identityEpoch, accountId, token)
}, { flush: 'sync' })

onMounted(async () => {
  maintenanceClockTimer = window.setInterval(() => { policyNow.value = Date.now() }, 1_000)
  window.addEventListener('l12-resource-operationsPolicy', onOperationsResource)
  const epoch = identityEpoch
  const account = platformState.account?.id, token = platformState.token
  try {
    const [decks, cards] = await Promise.all([
      account && token ? Promise.resolve(cachedDecks.value) : ensureOfficialPrebuiltDecks(),
      loadDeckCatalog(),
    ])
    if (identityCurrent(epoch, account, token)) {
      catalog.value = cards
      cachedDecks.value = decks
      hydrateDeckSelections()
    }
  } catch (error) {
    if (identityCurrent(epoch, account, token) && deckErrorBelongsToCurrentAccount(error))
      l12State.notice = error instanceof Error ? error.message : '牌库暂不可读取'
  }
  if (!identityCurrent(epoch, account, token)) return
  await refreshOperationsPolicy()
  if (!identityCurrent(epoch, account, token)) return
  try { ranked.value = normalizeRankedOverview(await rankedApi.overview()) }
  catch (error) {
    if (identityCurrent(epoch, account, token)) l12State.notice = error instanceof Error ? error.message : '排位资料加载失败'
  }
  if (!identityCurrent(epoch, account, token)) return
  if (platformState.account && platformState.token && l12State.status === 'offline') {
    try { await connect() } catch { /* 页面保留离线提示，创建/加入时仍可重试。 */ }
  }
})

onBeforeUnmount(() => {
  componentAlive = false
  identityEpoch++
  selectorActionEpoch++
  modeActionEpoch++
  roomDeckSyncPromise = null
  window.clearInterval(maintenanceClockTimer)
  window.removeEventListener('l12-resource-operationsPolicy', onOperationsResource)
})

function onOperationsResource(event: Event) {
  if ((event as CustomEvent).detail?.fallback === true && l12State.status !== 'online')
    void refreshOperationsPolicy()
}

async function loadSelectedBody(scope: L12DeckSelectionScope, current: () => boolean) {
  const selection = selectedDecks.value[scope]
  if (!selection?.id || !selection.revision) throw new Error('当前选择缺少可验证的牌库修订，请重新选择')
  if (!platformState.account?.id || !platformState.token) return selection.body
  const body = await loadPrivateDeckBody({ id: selection.id, revision: selection.revision }, current)
  if (!current()) return null
  if (body.id !== selection.id || body.revision !== selection.revision)
    throw new Error('服务器返回了不同的牌库身份或修订，请重新选择')
  const nextCache = { ...cachedDecks.value }
  Object.keys(nextCache).filter(name => nextCache[name]?.id === body.id).forEach(name => delete nextCache[name])
  nextCache[body.name] = body
  cachedDecks.value = nextCache
  selectedDecks.value[scope] = { ...selection, name: body.name, body }
  selectedDeckNames.value[scope] = body.name
  return body
}

async function runRoomDeckSync(key: string, roomCode: string, id: string, revision: number, rulesKey: string,
    epoch: number, accountId: string | undefined, token: string) {
  const current = () => identityCurrent(epoch, accountId, token) && l12State.room?.roomCode === roomCode
    && selectionCurrent('friendly', id, revision) && friendlyRulesKey() === rulesKey && !me.value?.ready
  try {
    const deck = await loadSelectedBody('friendly', current)
    if (!deck || !current()) return false
    const invalid = deckError(deck, 'friendly')
    if (invalid) { l12State.notice = invalid; return false }
    selectCustomDeck(deck)
    if (!current()) return false
    roomDeckSubmitKey = key
    return true
  } catch (error) {
    if (current() && deckErrorBelongsToCurrentAccount(error)) l12State.notice = bodyFailureMessage(error, 'friendly')
    return false
  }
}

async function syncCurrentRoomDeck() {
  const roomCode = l12State.room?.roomCode
  const selection = selectedDecks.value.friendly
  if (!roomCode || !selection || me.value?.ready) return false
  const epoch = identityEpoch
  const accountId = platformState.account?.id
  const token = platformState.token
  const rulesKey = friendlyRulesKey()
  const key = `${epoch}:${roomCode}:${selection.id}:${selection.revision}:${rulesKey}`
  if (roomDeckSubmitKey === key) return true
  if (roomDeckSyncPromise && roomDeckSyncKey === key) return roomDeckSyncPromise
  roomDeckSyncKey = key
  const task = runRoomDeckSync(key, roomCode, selection.id, selection.revision, rulesKey, epoch, accountId, token)
  roomDeckSyncPromise = task
  try { return await task }
  finally {
    if (roomDeckSyncPromise === task) {
      roomDeckSyncPromise = null
      roomDeckSyncKey = ''
    }
  }
}

// 创建、加入或恢复好友房时，只展开并提交已确认的稳定ID+修订；选择器取消不会触发这里。
watch(() => [l12State.room?.roomCode, selectedDecks.value.friendly?.id, selectedDecks.value.friendly?.revision,
  me.value?.ready, catalog.value.length, operationsPolicy.value?.version, l12State.room?.options?.useCardRestrictions] as const,
() => { if (l12State.room?.roomCode && !me.value?.ready) void syncCurrentRoomDeck() }, { immediate: true })

async function chooseFaction(faction: 'order' | 'chaos' | 'fate') {
  if (factionSaving.value) return
  if (ranked.value?.profile.faction) { pendingFaction.value = faction; return }
  await submitFaction(faction)
}
async function submitFaction(faction: 'order' | 'chaos' | 'fate') {
  if (factionSaving.value) return
  factionSaving.value = true
  try {
    await rankedApi.selectFaction(faction)
    ranked.value = normalizeRankedOverview(await rankedApi.overview())
    changingFaction.value = false
    pendingFaction.value = null
  }
  catch (error) { l12State.notice = error instanceof Error ? error.message : '派系选择失败' }
  finally { factionSaving.value = false }
}
async function onMatch() {
  if (modeActionPending.value) return
  const action = ++modeActionEpoch
  const epoch = identityEpoch
  const accountId = platformState.account?.id
  const token = platformState.token
  const mode = selectedMatchMode.value
  const scope: L12DeckSelectionScope = mode
  const selection = selectedDecks.value[scope]
  const current = () => identityCurrent(epoch, accountId, token) && modeActionEpoch === action
    && selectedMatchMode.value === mode && tab.value === 'match' && !l12State.room
    && Boolean(selection && selectionCurrent(scope, selection.id, selection.revision))
  modeActionPending.value = true
  try {
    if (!operationsAllowed() || !(await ensureConnected())) return
    if (!current() || !selection) {
      if (identityCurrent(epoch, accountId, token)) l12State.notice = '当前模式没有可验证的牌库，请重新选择'
      return
    }
    const deck = await loadSelectedBody(scope, current)
    if (!deck || !current()) return
    const invalid = deckError(deck, scope)
    if (invalid) { l12State.notice = invalid; return }
    if (mode === 'ranked' && !ranked.value?.profile.faction) { l12State.notice = '请先选择本赛季派系'; return }
    joinMatchmaking(mode, deck)
  } catch (error) {
    if (current() && deckErrorBelongsToCurrentAccount(error)) l12State.notice = bodyFailureMessage(error, scope)
  } finally {
    if (identityCurrent(epoch, accountId, token) && modeActionEpoch === action) modeActionPending.value = false
  }
}

async function toggleReady() {
  if (me.value?.ready) { setReady(false); return }
  if (modeActionPending.value) return
  modeActionPending.value = true
  try {
    if (await syncCurrentRoomDeck()) setReady(true)
  } finally { modeActionPending.value = false }
}

async function ensureConnected() {
  if (!platformState.account || !platformState.token) { l12State.notice = '请先登录账号'; return false }
  if (l12State.status !== 'online') await connect()
  return true
}
function operationsAllowed() {
  if (!maintenanceActive.value) return true
  l12State.notice = '维护即将开始/维护中，对局功能已关闭。'
  return false
}
function saveRoomRules() { if (isRoomHost.value) updateRoomOptions(editableRoomOptions.value) }
async function onCreate() { try { if (operationsAllowed() && await ensureConnected()) createRoom(roomOptions.value) } catch {} }
async function onJoin() { try { if (operationsAllowed() && await ensureConnected()) joinRoom(roomCode.value.trim()) } catch {} }
async function onSpectate() { try { if (operationsAllowed() && await ensureConnected()) spectateRoom(roomCode.value.trim()) } catch {} }
async function copyRoomCode() {
  const code = l12State.room?.roomCode
  if (!code) return
  try {
    await navigator.clipboard.writeText(code)
  } catch {
    const input = document.createElement('textarea')
    input.value = code
    input.style.position = 'fixed'
    input.style.opacity = '0'
    document.body.appendChild(input)
    input.select()
    document.execCommand('copy')
    input.remove()
  }
  roomCodeCopied.value = true
  window.setTimeout(() => { roomCodeCopied.value = false }, 1600)
}
</script>

<template>
  <div class="battle-hub">
    <RankedBroadcastTicker />
    <header class="page-head"><div><small>BATTLE LOBBY</small><h1>开始对战</h1><p>选择模式并确认当前牌库，准备后进入对局。</p></div><div class="server-state" :class="l12State.status"><i/><span>{{ l12State.status === 'online' ? '服务器在线' : '尚未连接' }}</span></div></header>
    <section v-if="maintenanceView" class="maintenance-banner" :class="maintenanceView.phase"><b>{{ maintenanceView.title }}</b><strong>{{ maintenanceView.countdown }}</strong><span>{{ maintenanceView.message }}</span></section>
      <section v-else-if="policyError" class="policy-warning"><b>运营规则暂不可用</b><span>{{ policyError }}。页面暂用安全默认值，服务端仍会在操作时进行权威校验。</span></section>

    <section v-if="l12State.matchFound && !l12State.game" class="match-found-stage panel" data-ui-contract="match-found-state-recovery">
      <small>MATCH FOUND</small><h2>匹配成功</h2>
      <p>正在建立对局并同步双方状态，请稍候……</p>
      <code>{{ l12State.matchFound.roomCode }}</code>
    </section>
    <section v-else-if="l12State.room" class="room-stage panel">
      <header><div><small>FRIENDLY ROOM</small><h2>友谊战整备室</h2></div><div class="room-code"><code>{{ l12State.room.roomCode }}</code><button type="button" @click="copyRoomCode">{{ roomCodeCopied ? '已复制' : '复制房间码' }}</button></div></header>
      <div class="versus">
        <article v-for="index in [0,1]" :key="index" :class="{ empty: !l12State.room.players[index] }"><span>PLAYER {{ index + 1 }}</span><b>{{ l12State.room.players[index]?.name || '等待玩家' }}</b><p>{{ visibleDeckLabel(index) }}</p><i class="player-online" :class="{ online: l12State.room.players[index]?.connected }">{{ l12State.room.players[index] ? (l12State.room.players[index]?.connected ? '在线' : '已断开') : '等待加入' }}</i><em>{{ l12State.room.players[index]?.ready ? '已准备' : '未准备' }}</em></article><strong>VS</strong>
      </div>
      <div v-if="l12State.room.options" class="room-rule-summary"><b>房主规则</b><span>好友房</span><span>{{ l12State.room.options.useCardRestrictions ? '启用运营禁限卡' : '不启用运营禁限卡' }}</span><span>{{ optionLabels.spectating[l12State.room.options.spectating] }}</span><span>{{ optionLabels.handVisibility[l12State.room.options.handVisibility] }}</span><span>{{ optionLabels.disasterMode[l12State.room.options.disasterMode] }}</span><span v-if="l12State.room.operationsPolicyVersion">运营规则 v{{ l12State.room.operationsPolicyVersion }}</span></div>
      <section v-if="isRoomHost" class="room-rule-editor">
        <header><b>调整房间规则</b><span>保存后双方准备状态会重置</span></header>
        <div class="room-settings"><div><b>禁限卡规则</b><select v-model="editableRoomOptions.useCardRestrictions"><option :value="false">不启用运营禁限卡</option><option :value="true">启用运营禁限卡</option></select></div><div><b>观战权限</b><select v-model="editableRoomOptions.spectating"><option value="public">允许所有玩家直接观战</option><option value="friends">仅限好友观战</option><option value="disabled">禁止观战</option></select></div><div><b>观战者查看手牌</b><select v-model="editableRoomOptions.handVisibility"><option value="request">需要当局玩家同意</option><option value="public">默认公开</option></select></div><div><b>天灾模式</b><select v-model="editableRoomOptions.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="season">赛季天灾</option><option value="none">不使用天灾</option></select></div></div>
        <button type="button" @click="saveRoomRules">保存房间规则</button>
      </section>
      <section v-if="operationsPolicy?.announcements?.length" class="long-term-announcements" data-ui-contract="long-term-announcements-above-deck"><article v-for="item in operationsPolicy.announcements" :key="item.id"><b>长期公告</b><span>{{ item.content }}</span></article></section>
      <section class="room-current-deck"><DeckProfile v-if="currentDeck" compact :master-id="currentDeck.masterId" :master-name="byId.get(currentDeck.masterId)?.nameZh" :name="currentDeck.name" context="好友房牌库" :meta="`${deckCountSummary(currentDeck.cardIds, byId).label} 张主牌`"/><p v-else>当前没有已保存牌库</p><div><span :class="{ invalid: !!currentDeckError }">{{ currentDeckError || '符合好友房规则' }}</span><button type="button" :disabled="me?.ready" @click="openDeckSelector">更换牌库</button></div></section>
      <footer><button class="leave-room" type="button" @click="leaveRoom()">{{ l12State.room.yourPlayerIndex === 0 ? '关闭房间并返回大厅' : '离开房间并返回大厅' }}</button><router-link to="/decks">管理我的牌库</router-link><button class="primary" :disabled="modeActionPending || l12State.room.players.length < 2 || !!currentDeckError" @click="toggleReady">{{ me?.ready ? '取消准备' : modeActionPending ? '正在校验牌库…' : '准备对战' }}</button></footer>
    </section>

    <template v-else>
      <section v-if="operationsPolicy?.announcements?.length" class="long-term-announcements" data-ui-contract="long-term-announcements-above-deck"><article v-for="item in operationsPolicy.announcements" :key="item.id"><b>长期公告</b><span>{{ item.content }}</span></article></section>
      <section class="current-deck panel"><DeckProfile v-if="currentDeck" :master-id="currentDeck.masterId" :master-name="byId.get(currentDeck.masterId)?.nameZh" :name="currentDeck.name" :context="activeDeckScope === 'ranked' ? '排位当前牌库' : activeDeckScope === 'casual' ? '休闲当前牌库' : activeDeckScope === 'friendly' ? '好友房当前牌库' : '沙盒我方牌库'" :meta="`${deckCountSummary(currentDeck.cardIds, byId).label} 张主牌`"/><DeckProfile v-else context="当前牌库" meta="当前没有已保存牌库"/><div class="current-deck-actions"><span :class="{ invalid: !!currentDeckError }">{{ currentDeckError || '符合当前模式规则' }}</span><button type="button" @click="openDeckSelector">更换牌库</button></div></section>
      <div class="mode-tabs"><button :class="{ active: tab === 'match' }" @click="tab = 'match'">匹配</button><button :class="{ active: tab === 'friendly' }" @click="tab = 'friendly'">好友房</button><button :class="{ active: tab === 'sandbox' }" @click="tab = 'sandbox'">单人</button></div>
      <div v-if="tab === 'match' && selectedMatchMode === 'ranked' && ranked" class="faction-totals faction-totals--overview" data-ui-contract="faction-totals-above-public-match"><article v-for="faction in ranked.config.factions" :key="faction.id" :style="{ '--accent': faction.color }"><b>{{ faction.name }}</b><span>七曜值 {{ (ranked.factionTotals[faction.name] || 0).toLocaleString() }}</span></article></div>

      <section v-if="tab === 'match'" class="mode-panel panel"><header class="public-match-head"><div><small>PUBLIC MATCH</small><h2>公开匹配</h2></div><button v-if="selectedMatchMode === 'ranked'" class="ranked-rules-button" type="button" @click="rankedRulesOpen = true">排位规则</button></header>
        <p v-if="selectedMatchMode === 'ranked' && operationsPolicy" class="season-name"><b>当前赛季</b><span>{{ operationsPolicy.season.name }}</span></p>
        <div v-if="selectedMatchMode === 'ranked' && ranked && (!ranked.profile.faction || changingFaction)" class="faction-select"><b>{{ changingFaction ? '改选本赛季派系' : '选择本赛季派系' }}</b><strong v-if="changingFaction" class="faction-reset-warning">注意：确认更换后七曜值将清零，定级与本赛季战绩重新开始。</strong><span v-else>请选择本赛季参与排位的派系。</span><button v-if="changingFaction" :disabled="factionSaving" @click="changingFaction = false">返回，不更改</button><div><button v-for="faction in ranked.config.factions" :key="faction.id" :disabled="factionSaving || faction.name === ranked.profile.faction || faction.id === ranked.profile.faction" @click="chooseFaction(faction.id)">{{ faction.name }}</button></div></div>
        <div v-else-if="selectedMatchMode === 'ranked' && ranked" class="ranked-profile"><b>{{ ranked.profile.faction }} · {{ ranked.profile.placed ? ranked.profile.tier : `定级 ${ranked.profile.placementPlayed}/${ranked.config.placementMatches}` }}</b><span>{{ ranked.profile.displayValue }}<template v-if="ranked.profile.titles?.length"> · {{ ranked.profile.titles.join(' · ') }}</template><template v-else-if="ranked.profile.title"> · {{ ranked.profile.title }}</template></span><button @click="changingFaction = true">改选派系</button></div>
        <div class="match-options"><button :class="{ active: selectedMatchMode === 'ranked' }" @click="selectedMatchMode = 'ranked'">排位匹配</button><button :class="{ active: selectedMatchMode === 'casual' }" @click="selectedMatchMode = 'casual'">休闲匹配</button></div>
        <button v-if="l12State.matchmaking?.queued" class="cancel-match" @click="cancelMatchmaking()">取消{{ l12State.matchmaking.mode === 'ranked' ? '排位' : '休闲' }}匹配</button><button v-else class="primary" :disabled="modeActionPending || !currentDeck || !!currentDeckError || maintenanceActive" @click="onMatch">{{ modeActionPending ? '正在校验牌库…' : `开始${selectedMatchMode === 'ranked' ? '排位' : '休闲'}匹配` }}</button>
      </section>

      <section v-else-if="tab === 'friendly'" class="mode-panel panel friendly-panel"><small>FRIENDLY ROOM</small><h2>创建、加入或观战房间</h2><div class="account-identity" :class="{ missing: !platformState.account }"><span>{{ platformState.account ? '当前账号' : '尚未登录' }}</span><b>{{ platformState.account?.username || '登录后才能创建、加入或观战房间' }}</b><router-link to="/me">{{ platformState.account ? '账号设置 →' : '前往登录 →' }}</router-link></div><div class="join-row"><button class="primary" :disabled="maintenanceActive" @click="onCreate">创建新房间</button><span>房间码</span><input v-model="roomCode" maxlength="6" placeholder="输入 6 位房间码" @keyup.enter="onJoin"/><div class="join-actions"><button :disabled="maintenanceActive" @click="onJoin">加入对战</button><button class="spectate-button" :disabled="maintenanceActive" @click="onSpectate">直接观战</button></div></div><div class="room-settings"><div><b>禁限卡规则</b><select v-model="roomOptions.useCardRestrictions"><option :value="false">不启用运营禁限卡</option><option :value="true">启用运营禁限卡</option></select></div><div><b>观战权限</b><select v-model="roomOptions.spectating"><option value="public">允许所有玩家直接观战</option><option value="friends">仅限好友观战</option><option value="disabled">禁止观战</option></select></div><div><b>观战者查看手牌</b><select v-model="roomOptions.handVisibility"><option value="request">需要当局玩家同意</option><option value="public">默认公开</option></select></div><div><b>天灾模式</b><select v-model="roomOptions.disasterMode"><option value="all">全部天灾（禁用与选取）</option><option value="random">随机天灾（3张随机天灾＋最终湮灭）</option><option value="season">赛季天灾（使用当前赛季天灾池）</option><option value="none">不使用天灾（天灾值恒为0）</option></select></div></div></section>

      <section v-else class="mode-panel panel"><small>TEST SANDBOX</small><h2>单人测试沙盒</h2><p>用于验证牌库、卡效、阶段与交互，不计入玩家战绩和排行榜。</p><button class="primary" @click="router.push('/sandbox')">进入测试沙盒</button></section>
    </template>
    <SavedDeckSelector :open="deckSelectorOpen" :mode="deckSelectorScope" :guest-decks="guestDeckList"
      :catalog="catalog" :current-deck-id="selectedDecks[deckSelectorScope]?.id"
      :current-deck-revision="selectedDecks[deckSelectorScope]?.revision" :current-deck-name="selectorCurrentName"
      :restrictions="selectorRestrictions" :uses-season-restrictions="scopeNeedsPolicy(deckSelectorScope)"
      :loading="selectorLoading" :disabled="!!(l12State.room && me?.ready)" :confirming="deckSelectorConfirming"
      :action-error="deckSelectorError" @cancel="cancelDeckSelection" @confirm="confirmDeckSelection"/>
    <Teleport to="body">
      <div v-if="pendingFaction" class="ranked-rules-backdrop" @click.self="!factionSaving && (pendingFaction = null)">
        <section class="ranked-rules-modal ui-state-scope" role="dialog" aria-modal="true" aria-labelledby="faction-confirm-title" @keydown.esc="!factionSaving && (pendingFaction = null)">
          <header><h2 id="faction-confirm-title">确认改为{{ pendingFactionName }}？</h2></header>
          <div class="ranked-rules-scroll"><p class="faction-reset-warning">七曜值将清零，定级进度和本赛季战绩将重新开始。此操作不会因返回页面而撤销。</p><p>取消将保留当前派系及全部现有进度。</p></div>
          <footer><button autofocus :disabled="factionSaving" @click="pendingFaction = null">取消，保留当前派系</button><button :disabled="factionSaving" @click="submitFaction(pendingFaction!)">{{ factionSaving ? '正在更改…' : '确认清零并更改' }}</button></footer>
        </section>
      </div>
      <div v-if="rankedRulesOpen" class="ranked-rules-backdrop" @click.self="rankedRulesOpen = false">
        <section class="ranked-rules-modal ui-state-scope" role="dialog" aria-modal="true" aria-label="排位规则">
          <header><div><small>RANKED RULES</small><h2>排位规则</h2><p>{{ operationsPolicy?.season.name || '当前赛季' }}</p></div><button type="button" @click="rankedRulesOpen = false">×</button></header>
          <div class="ranked-rules-scroll">
            <article><h3>七曜值</h3><p>完成 {{ ranked?.config.placementMatches || 5 }} 场定级赛后进入段位。胜负结算只显示自己的七曜值与段位变化；连胜、对手强度、段位保护与分差修正均由服务器权威计算。</p></article>
            <article><h3>赛季持续周期</h3><p>{{ formatSeasonPeriod() }}</p></article>
            <article v-if="ranked"><h3>排位用时</h3><p>每位玩家总操作时间 {{ formatRankedDuration(ranked.config.timeControl.totalTimeSeconds) }}，单次操作 {{ formatRankedDuration(ranked.config.timeControl.operationTimeSeconds) }}，断线重连宽限 {{ formatRankedDuration(ranked.config.timeControl.reconnectGraceSeconds) }}。天灾禁选／选择每一步 {{ formatRankedDuration(ranked.config.timeControl.disasterDecisionSeconds) }}，超时由服务器从合法候选中自动选择；手牌调度 {{ formatRankedDuration(ranked.config.timeControl.mulliganDecisionSeconds) }}，超时保留原手牌。</p></article>
            <article><h3>段位与派系称号</h3><div v-for="faction in ranked?.config.factions || []" :key="faction.id" class="rules-faction"><b>{{ faction.name }}</b><span>{{ faction.tiers.map(tier => `${tier.name}（${tier.minimum.toLocaleString()}）`).join(' → ') }}</span><small>仅最高段位可获得：第1名「{{ faction.firstTitle }}」；第2至5名「{{ faction.topFiveTitle }}」。</small></div></article>
            <article><h3>最强主宰规则</h3><p>每位主宰在赛季结算时按排位成绩确定最强玩家并授予对应专属称号；当前赛季成绩不会提前进入历史荣誉。</p></article>
            <article v-if="seasonDisasterCards.length"><h3>本赛季天灾</h3><p>{{ seasonDisasterCards.length }} 张（含固定湮灭）。排位强制使用赛季天灾池；好友房仅在房主选择“赛季天灾”时使用开房时的赛季快照；休闲与沙盒不继承此限制。</p><div class="season-card-grid"><button v-for="card in seasonDisasterCards" :key="card.id" type="button" :aria-label="`查看${card.nameZh}卡牌详情`" @click="detailCard = card"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb" fit="contain"/><b>{{ card.nameZh }}</b><small>{{ card.number || card.id }}</small></button></div></article>
            <article v-if="seasonRestrictionCards.length"><h3>本赛季禁限卡</h3><div class="season-card-grid season-card-grid--restrictions"><button v-for="item in seasonRestrictionCards" :key="`${item.rule.masterId || '*'}:${item.card.id}`" type="button" :aria-label="`查看${item.card.nameZh}卡牌详情`" @click="detailCard = item.card"><CardImage :card-id="item.card.id" :legacy-url="item.card.imageUrl" :alt="item.card.nameZh" intent="thumb" fit="contain"/><b>{{ item.card.nameZh }}</b><small>{{ item.card.number || item.card.id }}</small><span>{{ item.rule.masterId ? `仅限${item.master?.nameZh || item.rule.masterId}` : '全部主宰' }} · 上限 {{ item.rule.maxCopies }} 张</span></button></div></article>
          </div>
          <footer><button type="button" @click="rankedRulesOpen = false">我知道了</button></footer>
        </section>
      </div>
    </Teleport>
    <CatalogCardDetails v-if="detailCard" :card="detailCard" :show-catalog-only="false" @close="detailCard = null"/>
    <p v-if="l12State.notice" class="battle-notice">{{ l12State.notice }}</p>
  </div>
</template>

<style scoped>
.faction-reset-warning{display:block;padding:14px;border:1px solid #d56d63;background:#30191b;color:#ffb6a9;font-weight:800;line-height:1.7}.ranked-rules-modal>footer{flex-wrap:wrap;gap:12px}

.public-match-head{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.public-match-head h2{margin:4px 0}.ranked-rules-button{min-height:44px;padding:6px 2px;border:0;border-bottom:1px solid #e1c759;background:transparent;color:#f0d46d;font-weight:900}.season-name{display:flex;align-items:baseline;gap:10px;width:max-content;padding:10px 14px;border-left:4px solid #d7bc55;background:#1d1b12;color:#ead982}.season-name b{font-size:14px}.season-name span{font-size:17px;font-weight:900}
.ranked-rules-backdrop{position:fixed;z-index:4000;inset:0;display:grid;place-items:center;padding:20px;background:rgba(0,0,0,.68)}.ranked-rules-modal{box-sizing:border-box;display:grid;grid-template-rows:auto minmax(0,1fr) auto;width:min(760px,calc(100vw - 40px));max-height:min(820px,calc(100vh - 40px));overflow:hidden;border:1px solid #9b8438;background:#0d151d;color:#eef1ed;box-shadow:0 24px 80px #000}.ranked-rules-modal>header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px 22px;border-bottom:1px solid #34414a}.ranked-rules-modal header small{color:#58c6cd;font:900 14px monospace;letter-spacing:.16em}.ranked-rules-modal header h2{margin:5px 0 2px}.ranked-rules-modal header p{margin:0;color:#d5ba59}.ranked-rules-modal header button{border:0;background:transparent;color:#b9c1c4;font-size:28px}.ranked-rules-scroll{display:grid;min-height:0;align-content:start;gap:12px;overflow-x:hidden;overflow-y:scroll;padding:18px 22px}.ranked-rules-scroll article{padding:14px;border:1px solid #2c3943;background:#101b25}.ranked-rules-scroll h3{margin:0 0 8px;color:#e7ce72}.ranked-rules-scroll p{margin:0;color:#b5c0c4;line-height:1.75}.rules-faction{display:grid;gap:6px;margin-top:10px;padding:10px;border-left:3px solid #7b63dd;background:#0b1219}.rules-faction span,.rules-faction small{color:#9caaaf;line-height:1.6}.ranked-rules-modal>footer{display:flex;justify-content:flex-end;padding:14px 22px;border-top:1px solid #34414a}.ranked-rules-modal>footer button{padding:10px 24px;border:1px solid #ddc15a;background:#332a12;color:#f0d879;font-weight:900}
.season-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(132px,100%),1fr));gap:10px;margin-top:12px}.season-card-grid button{box-sizing:border-box;display:grid;min-width:0;gap:6px;padding:8px;overflow:hidden;border:1px solid #3b4a55;background:#0a1118;color:#eef1ed;text-align:left;cursor:pointer}.season-card-grid .l12-card-image{display:block;width:100%;aspect-ratio:5/7;min-width:0;background:#05090c}.season-card-grid b,.season-card-grid small,.season-card-grid span{min-width:0;overflow-wrap:anywhere}.season-card-grid b{font-size:13px}.season-card-grid small{color:#91a0a6}.season-card-grid span{color:#d9c56f;font-size:12px;line-height:1.45}.season-card-grid button:focus-visible{outline:2px solid #58c6cd;outline-offset:2px}
.battle-hub{width:min(980px,calc(100% - 40px));min-height:100%;margin:0 auto;padding:34px 0 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:22px}.page-head small,.panel>small,.panel header small{color:#50c4cc;font:900 14px monospace;letter-spacing:.2em}.page-head h1{margin:5px 0;font-size:30px}.page-head p,.mode-panel>p{margin:0;color:#7c8990;font-size:14px;line-height:1.7}.server-state{display:flex;align-items:center;gap:8px;color:#7b858a;font-size:14px;font-weight:900}.server-state i{width:8px;height:8px;border-radius:50%;background:#687177}.server-state.online i{background:#54c695;box-shadow:0 0 8px #54c695}.panel{border:1px solid rgba(235,230,216,.17);background:#101821;box-shadow:0 18px 50px rgba(0,0,0,.18)}.current-deck{display:grid;grid-template-columns:58px 1fr auto;align-items:center;gap:16px;padding:18px}.deck-thumb{display:grid;width:52px;height:70px;place-items:center;border:1px solid #d2b76f;background:linear-gradient(145deg,#6e1825,#13252a);font-size:20px;font-weight:900}.current-deck small,.current-deck b,.current-deck span{display:block}.current-deck small{color:#728089;font-size:14px}.current-deck b{margin:4px 0;font-size:17px}.current-deck span{color:#7f8b91;font-size:14px}.current-deck-actions{text-align:right}.current-deck-actions span.invalid,.room-current-deck span.invalid{color:#ef9ca4}.current-deck-actions button,.room-current-deck button{margin-top:7px;padding:8px 11px;border:1px solid #d5b862;background:#151b1d;color:#ead083;font-size:14px;font-weight:900}.mode-tabs{display:grid;grid-template-columns:repeat(3,1fr);margin:18px 0;border:1px solid rgba(235,230,216,.17);background:#0b1117}.mode-tabs button{padding:14px;border:0;background:transparent;color:#738089;font-weight:900}.mode-tabs button.active{background:linear-gradient(135deg,#8b1c2a,#ad2d38);color:#fff}.mode-panel{padding:28px}.mode-panel h2{margin:6px 0 8px;font-size:23px}.mode-panel>p{max-width:650px}.mode-panel label{display:block;margin:20px 0 12px;color:#aab2b4;font-size:14px;font-weight:900}.mode-panel input,.mode-panel select{width:100%;padding:12px;border:1px solid #45535c;background:#080e14;color:#fff;outline:none}.mode-panel input:focus,.mode-panel select:focus{border-color:#50c4cc}.primary{border-color:#e2c473!important;background:#e2c473!important;color:#0a0d0f!important;font-weight:900}.mode-panel>button.primary{min-width:220px;margin-top:22px;padding:13px;border:1px solid}.match-options{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:20px}.match-options button{padding:15px;border:1px solid #3e4a52;background:#0b1117;color:#7b868b}.join-row{display:grid;grid-template-columns:1fr auto 1fr auto;align-items:center;gap:9px}.join-row button{height:42px;padding:0 18px;border:1px solid #52606a;background:#121c24;color:#fff}.join-row span{color:#68757c;font-size:14px}.room-settings{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:18px}.room-settings>div{padding:14px;border:1px solid #354149;background:#0b1218}.room-settings b{display:block;margin-bottom:8px;font-size:14px}.room-stage{padding:26px}.room-stage>header{display:flex;align-items:center;justify-content:space-between}.room-stage h2{margin:4px 0}.room-stage code{padding:9px 12px;border:1px solid #d9bc6d;color:#f0d889;font-size:17px;letter-spacing:.16em}.versus{position:relative;display:grid;grid-template-columns:1fr 1fr;gap:60px;margin:24px 0}.versus article{display:flex;min-height:150px;flex-direction:column;align-items:center;justify-content:center;border:1px solid #37434a;background:#0a1117}.versus article.empty{opacity:.55}.versus span{color:#66747c;font:900 14px monospace;letter-spacing:.15em}.versus article>b{margin:10px 0 4px;font-size:20px}.versus p{margin:0;color:#78858b;font-size:14px}.versus em{margin-top:12px;color:#d9bb68;font-size:14px;font-style:normal;font-weight:900}.versus>strong{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);color:#a52b38}.room-current-deck{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:14px;padding:12px;border:1px solid #39464e;background:#0a1117}.room-current-deck :deep(.deck-profile){border:0;background:transparent}.room-current-deck>div{text-align:right}.room-current-deck span{display:block;color:#7f8b91;font-size:14px}.room-current-deck>p{color:#87939a;font-size:14px}.room-stage footer{display:flex;align-items:center;justify-content:space-between;margin-top:20px}.room-stage footer a{color:#55c4ca;font-size:14px;text-decoration:none}.room-stage footer button{min-width:220px;padding:12px;border:1px solid}.battle-notice{padding:11px;border-left:3px solid #a52b38;background:#211016;color:#e6a8ad;font-size:14px}
@media(max-width:700px){.battle-hub{width:auto;padding:20px 12px 50px}.page-head{gap:12px}.page-head h1{font-size:25px}.current-deck{grid-template-columns:1fr}.current-deck-actions{text-align:left}.mode-panel{padding:20px}.join-row{grid-template-columns:1fr}.join-row span{text-align:center}.room-settings,.match-options,.versus,.room-current-deck{grid-template-columns:1fr}.room-current-deck>div{text-align:left}.versus{gap:10px}.versus>strong{display:none}.room-stage footer{align-items:stretch;flex-direction:column;gap:12px}.room-stage footer button{width:100%}}
.join-actions{display:flex;gap:6px}.join-actions .spectate-button{border-color:#4faeb5;color:#80dce2}
.room-code{display:flex;align-items:stretch;gap:7px}.room-code code{display:grid;place-items:center}.room-code button{padding:0 12px;border:1px solid #6d765f;background:#17201c;color:#f4e9bc;font-size:14px;font-weight:900;white-space:nowrap}.room-code button:hover{border-color:#e0c16d;background:#2a2718;color:#fff}
@media(max-width:700px){.join-actions{display:grid;grid-template-columns:1fr 1fr}}
.leave-room{border-color:#7b4147!important;background:#211116!important;color:#e7aeb3!important;font-weight:900}
.account-identity{display:grid;grid-template-columns:auto 1fr auto;align-items:center;gap:12px;margin:18px 0;padding:12px 14px;border:1px solid #3e4b53;background:#0a1117}.account-identity span{color:#75838a;font-size:14px;font-weight:900}.account-identity b{font-size:14px}.account-identity a{color:#55c4ca;font-size:14px;font-weight:900;text-decoration:none}.account-identity.missing{border-color:#7b4147}.account-identity.missing b{color:#dda6ab}
.player-online{margin-top:8px;color:#b76570;font-size:14px;font-style:normal;font-weight:900}.player-online.online{color:#58c99a}.room-rule-summary{display:flex;align-items:center;gap:8px;margin:-8px 0 18px;padding:11px 14px;border:1px solid #354149;background:#0a1117}.room-rule-summary b{margin-right:6px;color:#e4c675;font-size:14px}.room-rule-summary span{padding:4px 7px;background:#17212a;color:#aab4b8;font-size:14px;font-weight:900}
@media(max-width:700px){.room-rule-summary{align-items:stretch;flex-direction:column}.room-rule-summary span{text-align:center}}
.room-decks button{display:grid;grid-template-columns:38px 1fr;align-items:center;gap:8px;padding:8px}.room-decks button>img{width:38px;height:38px;object-fit:cover;border:1px solid #596269;border-radius:2px}.room-decks button>span,.room-decks button b,.room-decks button small{display:block}.room-decks button small{margin-top:4px;color:#77848a;font-size:14px}
.maintenance-banner,.policy-warning{display:flex;align-items:center;gap:12px;margin-bottom:16px;padding:13px 16px;border:1px solid #9a7135;background:#2a1e0e;color:#f0d695;font-size:14px}.maintenance-banner{flex-wrap:wrap}.maintenance-banner strong{color:#fff0b3;font:900 14px monospace;white-space:nowrap}.maintenance-banner span{min-width:0;flex:1 1 260px}.maintenance-banner span,.policy-warning span{color:#c8b98f}.maintenance-banner.active{border-color:#a54a52;background:#2b1217}.policy-warning{border-color:#6b4c52;background:#221217;color:#e1b2b8}.join-row button:disabled,.room-settings select:disabled{cursor:not-allowed;opacity:.45}
.current-deck{grid-template-columns:minmax(0,1fr) auto}.current-deck :deep(.deck-profile){border:0;background:transparent;padding:0}.room-decks button{display:block;padding:0}.room-decks button :deep(.deck-profile){width:100%;border:0;background:transparent}.room-decks button.active :deep(.deck-profile){background:#202017}
.long-term-announcements{display:grid;gap:7px;margin:0 0 12px}.long-term-announcements article{display:grid;grid-template-columns:auto minmax(0,1fr);align-items:start;gap:10px;padding:11px 13px;border:1px solid #705f34;border-left:4px solid #d6b85e;background:#18160f;color:#d8ddd9}.long-term-announcements b{color:#f0d477;font-size:14px;white-space:nowrap}.long-term-announcements span{white-space:pre-wrap;font-size:14px;line-height:1.65}
.room-rule-editor{margin:0 0 18px;padding:14px;border:1px solid #695b36;background:#11140f}.room-rule-editor>header{display:flex;align-items:center;justify-content:space-between}.room-rule-editor>header span{color:#877d62;font-size:14px}.room-rule-editor>.room-settings{margin-top:12px}.room-rule-editor>button{display:block;margin:12px 0 0 auto;padding:9px 18px;border:1px solid #d7bb69;background:#d7bb69;color:#111;font-weight:900}
@media(max-width:700px){.current-deck{grid-template-columns:1fr}}
.match-options button.active{border-color:#d8ba65;background:#2a2414;color:#f4db90}.faction-totals{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:18px 0}.faction-totals--overview{margin:-1px 12px 18px;padding:12px;border:1px solid rgba(235,230,216,.17);background:#101821}.faction-totals article{padding:12px;border:1px solid var(--accent);background:#0a1117}.faction-totals b,.faction-totals span{display:block}.faction-totals span{margin-top:5px;color:#d8c77f;font-size:14px}.faction-select,.ranked-profile{margin:14px 0;padding:14px;border:1px solid #45535c;background:#0a1117}.faction-select>b,.faction-select>span,.ranked-profile>b,.ranked-profile>span{display:block}.faction-select>span,.ranked-profile>span{margin:5px 0;color:#89969b;font-size:14px}.faction-select>div{display:flex;gap:8px;margin-top:10px}.faction-select button,.ranked-profile button,.cancel-match{padding:9px 14px;border:1px solid #887239;background:#211d10;color:#f0d582}.ranked-profile{display:grid;grid-template-columns:1fr auto;align-items:center}.ranked-profile span{grid-column:1}.ranked-profile button{grid-row:1/3;grid-column:2}.cancel-match{min-width:220px;margin-top:22px}
.match-found-stage{display:grid;min-height:260px;place-items:center;padding:36px;text-align:center}.match-found-stage small{color:#50c4cc;font:900 14px monospace;letter-spacing:.2em}.match-found-stage h2{margin:4px 0 0;font-size:30px}.match-found-stage p{margin:0;color:#9aa5aa;font-size:14px}.match-found-stage code{padding:8px 12px;border:1px solid #d9bc6d;color:#f0d889;letter-spacing:.18em}
.ranked-rules-modal{grid-template-rows:auto minmax(0,1fr) auto;height:min(820px,92vh);min-height:0}
.ranked-rules-scroll{min-height:0;align-content:start;overflow-x:hidden;overflow-y:scroll;overscroll-behavior:contain;scrollbar-gutter:stable}
@media(max-width:520px){.ranked-rules-backdrop{padding:10px}.ranked-rules-modal{width:calc(100vw - 20px);max-height:calc(100vh - 20px)}.ranked-rules-modal>header,.ranked-rules-modal>footer{padding:13px 14px}.ranked-rules-scroll{padding:12px}.ranked-rules-scroll article{padding:11px}}
@media(max-width:520px){.battle-hub{padding:14px 10px 34px}.page-head{gap:7px;margin-bottom:13px}.page-head small,.panel>small,.panel header small,.match-found-stage small{font-size:10px;letter-spacing:.13em}.page-head h1{margin:3px 0;font-size:24px}.page-head p,.mode-panel>p,.server-state,.current-deck small,.current-deck span,.mode-panel label,.join-row span,.room-current-deck span,.room-current-deck>p,.battle-notice,.account-identity span,.account-identity b,.account-identity a,.player-online,.room-rule-summary b,.room-rule-summary span,.maintenance-banner,.policy-warning,.maintenance-banner strong,.long-term-announcements b,.long-term-announcements span,.room-rule-editor>header span{font-size:12px}.panel{box-shadow:0 10px 28px rgba(0,0,0,.16)}.current-deck{gap:8px;padding:11px}.deck-thumb{width:42px;height:56px;font-size:16px}.current-deck b{margin:2px 0;font-size:14px}.current-deck-actions button,.room-current-deck button,.room-code button{min-height:44px;margin-top:5px;padding:7px 9px;font-size:12px}.mode-tabs{margin:12px 0}.mode-tabs button{min-height:44px;padding:8px;font-size:12px}.mode-panel{padding:14px}.mode-panel h2{margin:3px 0 5px;font-size:18px}.mode-panel label{margin:13px 0 7px}.mode-panel input,.mode-panel select{min-height:44px;padding:8px;font-size:12px}.mode-panel>button.primary,.cancel-match{min-width:0;min-height:44px;margin-top:14px;padding:9px;font-size:13px}.match-options{gap:7px;margin-top:13px}.match-options button{min-height:44px;padding:8px;font-size:12px}.join-row{gap:7px}.join-row button{height:40px;padding:0 11px;font-size:12px}.room-settings{gap:8px;margin-top:12px}.room-settings>div{padding:10px}.room-settings b{margin-bottom:5px;font-size:12px}.room-stage{padding:14px}.room-stage h2{font-size:18px}.room-stage code{padding:7px 8px;font-size:13px}.versus{margin:14px 0}.versus article{min-height:104px}.versus span{font-size:10px}.versus article>b{margin:6px 0 2px;font-size:15px}.versus p,.versus em{font-size:12px}.room-stage footer{margin-top:14px}.room-stage footer a{font-size:12px}.room-stage footer button{min-width:0;min-height:44px;padding:9px;font-size:13px}.account-identity{gap:7px;margin:12px 0;padding:9px}.room-rule-summary{gap:5px;margin:-3px 0 12px;padding:8px}.faction-totals{gap:5px;margin:12px 0}.faction-totals article,.faction-totals--overview{padding:8px}.faction-totals span,.faction-select>span,.ranked-profile>span{font-size:12px}.faction-select,.ranked-profile{margin:10px 0;padding:10px}.faction-select>div{flex-wrap:wrap;margin-top:7px}.faction-select button,.ranked-profile button{min-height:44px;padding:7px 9px;font-size:12px}.match-found-stage{min-height:190px;padding:20px}.match-found-stage h2{font-size:23px}.match-found-stage p{font-size:12px}.ranked-rules-modal>header{padding:11px 12px}.ranked-rules-modal header small{font-size:10px}.ranked-rules-modal header h2{font-size:18px}.ranked-rules-modal header p,.ranked-rules-scroll p{font-size:12px}.ranked-rules-scroll{padding:10px}.ranked-rules-scroll article{padding:9px}.ranked-rules-scroll h3{font-size:14px}.ranked-rules-modal>footer{padding:10px 12px}.ranked-rules-modal>footer button{min-height:44px;padding:8px 16px;font-size:12px}}
@media(max-width:700px){.public-match-head{align-items:center}.public-match-head>div{min-width:0}.ranked-rules-button{flex:0 0 auto;font-size:12px}.page-head small,.panel>small,.panel header small{font-size:var(--l12-site-eyebrow,10.5px);letter-spacing:.13em}}
</style>


