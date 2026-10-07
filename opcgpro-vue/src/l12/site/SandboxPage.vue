<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { connect, createSandbox, l12State } from '@/l12/net'
import { ensureOfficialPrebuiltDecks, deckErrorBelongsToCurrentAccount, loadSavedDecksState, loadDeckCatalog, loadPrivateDeckBody,
  loadSelectedDeckName, saveSelectedDeckName, SELECTED_DECK_KEY, validateDeck, type DeckCard, type L12DeckSelectionScope,
  type PrivateDeckSummary, type SavedL12Deck } from '@/l12/decks'
import { platformState } from '@/l12/platform'
import DeckProfile from '@/l12/DeckProfile.vue'
import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'

type SandboxScope = Extract<L12DeckSelectionScope, 'sandbox-player' | 'sandbox-opponent'>
type SandboxDeckSelection = { id: string; revision: number; name: string; body: SavedL12Deck; summary?: PrivateDeckSummary }
type DeckSelectionCandidate = PrivateDeckSummary | SavedL12Deck

const cachedDecks = ref<Record<string, SavedL12Deck>>({})
const catalog = ref<DeckCard[]>([])
const playerDeckName = ref('')
const opponentDeckName = ref('')
const selectedDecks = ref<Record<SandboxScope, SandboxDeckSelection | null>>({ 'sandbox-player': null, 'sandbox-opponent': null })
const selectionMessages = ref<Record<SandboxScope, string>>({ 'sandbox-player': '', 'sandbox-opponent': '' })
const selectorTarget = ref<'sandbox-player' | 'sandbox-opponent' | null>(null)
const selectorConfirming = ref(false)
const selectorError = ref('')
const disasterMode = ref<'all' | 'random' | 'none' | 'custom'>('none')
const creating = ref(false)
const guestDecks = computed(() => platformState.account?.id && platformState.token ? [] : Object.values(cachedDecks.value))
const playerDeck = computed(() => selectedDecks.value['sandbox-player']?.body)
const opponentDeck = computed(() => selectedDecks.value['sandbox-opponent']?.body)
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const playerDeckError = computed(() => selectionMessages.value['sandbox-player']
  || (playerDeck.value ? (!catalog.value.length ? '正在加载沙盒构筑规则' : validateDeck(playerDeck.value, catalog.value)) : '请选择我方牌库'))
const opponentDeckError = computed(() => selectionMessages.value['sandbox-opponent']
  || (opponentDeck.value ? (!catalog.value.length ? '正在加载沙盒构筑规则' : validateDeck(opponentDeck.value, catalog.value)) : '请选择对手牌库'))
const selectorCurrentName = computed(() => selectorTarget.value === 'sandbox-opponent'
  ? opponentDeckName.value : playerDeckName.value)
const selectorCurrent = computed(() => selectorTarget.value ? selectedDecks.value[selectorTarget.value] : null)

let componentAlive = true
let identityEpoch = 0
let selectorActionEpoch = 0
let createActionEpoch = 0

function identityCurrent(epoch: number, accountId: string | undefined, token: string) {
  return componentAlive && identityEpoch === epoch && platformState.account?.id === accountId && platformState.token === token
}
function selectionStorageBase() {
  return platformState.account?.id ? `${SELECTED_DECK_KEY}:${platformState.account.id}` : SELECTED_DECK_KEY
}
function hasStoredSelection(scope: SandboxScope) {
  const base = selectionStorageBase()
  try { return Boolean(localStorage.getItem(`${base}:${scope}`) || localStorage.getItem(base)) }
  catch { return false }
}
function selectionCurrent(scope: SandboxScope, id: string, revision: number) {
  const selection = selectedDecks.value[scope]
  return selection?.id === id && selection.revision === revision
}
function requestStatus(error: unknown) {
  return error && typeof error === 'object' && 'status' in error && typeof error.status === 'number' ? error.status : 0
}
function bodyFailureMessage(error: unknown, scope: SandboxScope, markSelection = true) {
  const status = requestStatus(error)
  if (status === 404) {
    const message = '这副牌库当前不可用，请重新选择；另一方牌库没有改变'
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

function hydrateScope(scope: SandboxScope) {
  const name = loadSelectedDeckName(scope, cachedDecks.value)
  const deck = name ? cachedDecks.value[name] : undefined
  if (scope === 'sandbox-player') playerDeckName.value = name
  else opponentDeckName.value = name
  selectedDecks.value[scope] = deck?.id && deck.revision
    ? { id: deck.id, revision: deck.revision, name: deck.name, body: deck }
    : null
  selectionMessages.value[scope] = !deck && hasStoredSelection(scope)
    ? '原选择缺少可验证的服务器修订，请重新选择牌库'
    : ''
}

function refreshCacheView() {
  const snapshot = loadSavedDecksState()
  cachedDecks.value = snapshot.status === 'unavailable' ? {} : snapshot.decks
  playerDeckName.value = ''; opponentDeckName.value = ''
  if (snapshot.status === 'unavailable') {
    selectedDecks.value['sandbox-player'] = null
    selectedDecks.value['sandbox-opponent'] = null
    selectionMessages.value['sandbox-player'] = snapshot.error.message
    selectionMessages.value['sandbox-opponent'] = snapshot.error.message
    l12State.notice = snapshot.error.message
    return
  }
  try {
    hydrateScope('sandbox-player')
    hydrateScope('sandbox-opponent')
  } catch (error) { l12State.notice = error instanceof Error ? error.message : '牌库选择暂不可读取' }
}

async function refreshGuestDecks(epoch: number, accountId: string | undefined, token: string) {
  if (accountId || token) return
  try {
    const decks = await ensureOfficialPrebuiltDecks()
    if (!identityCurrent(epoch, accountId, token)) return
    cachedDecks.value = decks
    hydrateScope('sandbox-player')
    hydrateScope('sandbox-opponent')
  } catch (error) {
    if (identityCurrent(epoch, accountId, token) && deckErrorBelongsToCurrentAccount(error))
      l12State.notice = error instanceof Error ? error.message : '牌库暂不可读取'
  }
}

refreshCacheView()
watch(() => [platformState.account?.id, platformState.token] as const, ([accountId, token]) => {
  identityEpoch++
  selectorActionEpoch++
  createActionEpoch++
  selectorTarget.value = null
  selectorConfirming.value = false
  selectorError.value = ''
  creating.value = false
  refreshCacheView()
  if (!accountId && !token) void refreshGuestDecks(identityEpoch, accountId, token)
}, { flush: 'sync' })

onMounted(async () => {
  const epoch = identityEpoch
  const account = platformState.account?.id, token = platformState.token
  try {
    const [savedDecks, cards] = await Promise.all([
      account && token ? Promise.resolve(cachedDecks.value) : ensureOfficialPrebuiltDecks(),
      loadDeckCatalog(),
    ])
    if (!identityCurrent(epoch, account, token)) return
    catalog.value = cards
    cachedDecks.value = savedDecks
    hydrateScope('sandbox-player')
    hydrateScope('sandbox-opponent')
    if (platformState.account && platformState.token && l12State.status === 'offline') {
      try { await connect() } catch { /* 页面保留服务端提示。 */ }
    }
  } catch (error) {
    if (identityCurrent(epoch, account, token) && deckErrorBelongsToCurrentAccount(error))
      l12State.notice = error instanceof Error ? error.message : '牌库暂不可读取'
  }
})

onBeforeUnmount(() => {
  componentAlive = false
  identityEpoch++
  selectorActionEpoch++
  createActionEpoch++
})

async function confirmDeckSelection(candidate: DeckSelectionCandidate) {
  const scope = selectorTarget.value
  if (!scope || selectorConfirming.value || creating.value) return
  const action = ++selectorActionEpoch
  const epoch = identityEpoch
  const accountId = platformState.account?.id
  const token = platformState.token
  const id = candidate.id
  const revision = candidate.revision
  const current = () => identityCurrent(epoch, accountId, token) && selectorActionEpoch === action
    && selectorTarget.value === scope && !creating.value
  selectorConfirming.value = true
  selectorError.value = ''
  try {
    if (!id || !revision) throw new Error('牌库缺少稳定身份或修订，请重新选择')
    const deck = 'counts' in candidate
      ? await loadPrivateDeckBody({ id, revision }, current)
      : candidate
    if (!current()) return
    if (deck.id !== id || deck.revision !== revision) throw new Error('服务器返回了不同的牌库身份或修订，请重新选择')
    const invalid = !catalog.value.length ? '正在加载沙盒构筑规则' : validateDeck(deck, catalog.value)
    if (invalid) { selectorError.value = invalid; return }
    saveSelectedDeckName(scope, deck.name)
    if (!current()) return
    const nextCache = { ...cachedDecks.value }
    Object.keys(nextCache).filter(name => nextCache[name]?.id === deck.id).forEach(name => delete nextCache[name])
    nextCache[deck.name] = deck
    cachedDecks.value = nextCache
    selectedDecks.value[scope] = { id, revision, name: deck.name, body: deck,
      summary: 'counts' in candidate ? candidate : undefined }
    selectionMessages.value[scope] = ''
    if (scope === 'sandbox-player') playerDeckName.value = deck.name
    else opponentDeckName.value = deck.name
    selectorTarget.value = null
  } catch (error) {
    if (current() && deckErrorBelongsToCurrentAccount(error)) selectorError.value = bodyFailureMessage(error, scope, false)
  } finally {
    if (identityCurrent(epoch, accountId, token) && selectorActionEpoch === action) selectorConfirming.value = false
  }
}

function cancelDeckSelection() {
  selectorActionEpoch++
  selectorConfirming.value = false
  selectorError.value = ''
  selectorTarget.value = null
}

async function loadSelectionBody(scope: SandboxScope, id: string, revision: number, current: () => boolean) {
  const selection = selectedDecks.value[scope]
  if (!selection || selection.id !== id || selection.revision !== revision)
    throw new Error('沙盒牌库选择已改变，请重新确认')
  const body = await loadPrivateDeckBody({ id, revision }, current)
  if (!current()) return null
  if (body.id !== id || body.revision !== revision) throw new Error('服务器返回了不同的牌库身份或修订，请重新选择')
  const nextCache = { ...cachedDecks.value }
  Object.keys(nextCache).filter(name => nextCache[name]?.id === body.id).forEach(name => delete nextCache[name])
  nextCache[body.name] = body
  cachedDecks.value = nextCache
  selectedDecks.value[scope] = { ...selection, name: body.name, body }
  if (scope === 'sandbox-player') playerDeckName.value = body.name
  else opponentDeckName.value = body.name
  return body
}

async function startSandbox() {
  if (creating.value) return
  if (!platformState.account || !platformState.token) { l12State.notice = '请先登录账号'; return }
  const player = selectedDecks.value['sandbox-player']
  const opponent = selectedDecks.value['sandbox-opponent']
  if (!player || !opponent) {
    l12State.notice = playerDeckError.value || opponentDeckError.value || '请选择双方牌库'
    return
  }
  const action = ++createActionEpoch
  const epoch = identityEpoch
  const accountId = platformState.account.id
  const token = platformState.token
  const mode = disasterMode.value
  const current = () => identityCurrent(epoch, accountId, token) && createActionEpoch === action
    && disasterMode.value === mode && selectionCurrent('sandbox-player', player.id, player.revision)
    && selectionCurrent('sandbox-opponent', opponent.id, opponent.revision)
  let failureScope: SandboxScope = 'sandbox-player'
  creating.value = true
  try {
    const playerBody = await loadSelectionBody('sandbox-player', player.id, player.revision, current)
    if (!playerBody || !current()) return
    failureScope = 'sandbox-opponent'
    const opponentBody = player.id === opponent.id && player.revision === opponent.revision
      ? playerBody
      : await loadSelectionBody('sandbox-opponent', opponent.id, opponent.revision, current)
    if (!opponentBody || !current()) return
    if (player.id === opponent.id && player.revision === opponent.revision) {
      selectedDecks.value['sandbox-opponent'] = { ...opponent, name: opponentBody.name, body: opponentBody }
      opponentDeckName.value = opponentBody.name
    }
    const playerError = validateDeck(playerBody, catalog.value)
    const opponentError = validateDeck(opponentBody, catalog.value)
    if (playerError || opponentError) { l12State.notice = playerError || opponentError; return }
    if (l12State.status !== 'online') await connect()
    if (!current()) return
    createSandbox(playerBody, opponentBody, mode)
  } catch (error) {
    if (current() && deckErrorBelongsToCurrentAccount(error)) l12State.notice = bodyFailureMessage(error, failureScope)
  } finally {
    if (identityCurrent(epoch, accountId, token) && createActionEpoch === action) creating.value = false
  }
}
</script>

<template>
  <div class="sandbox-page">
    <section>
      <header><div><small>TEST SANDBOX</small><h1>单人测试沙盒</h1><p>复用正式规则内核，GM 指令仅对本沙盒生效；沙盒不会进入个人对局记录或排位统计。</p></div><span :class="l12State.status"><i/>{{ l12State.status === 'online' ? '服务器在线' : '服务器离线' }}</span></header>
      <div class="sandbox-grid">
        <section class="sandbox-deck"><b>我方牌库</b><DeckProfile v-if="playerDeck" compact :master-id="playerDeck.masterId" :master-name="byId.get(playerDeck.masterId)?.nameZh" :name="playerDeck.name" context="我方"/><p v-else>没有已保存牌库</p><span :class="{ invalid: !!playerDeckError }">{{ playerDeckError || '符合沙盒构筑规则' }}</span><button type="button" :disabled="creating" @click="selectorError = ''; selectorTarget = 'sandbox-player'">更换牌库</button></section>
        <section class="sandbox-deck"><b>对手牌库</b><DeckProfile v-if="opponentDeck" compact :master-id="opponentDeck.masterId" :master-name="byId.get(opponentDeck.masterId)?.nameZh" :name="opponentDeck.name" context="对手"/><p v-else>没有已保存牌库</p><span :class="{ invalid: !!opponentDeckError }">{{ opponentDeckError || '符合沙盒构筑规则' }}</span><button type="button" :disabled="creating" @click="selectorError = ''; selectorTarget = 'sandbox-opponent'">更换牌库</button></section>
        <div class="sandbox-account"><b>测试账号</b><span>{{ platformState.account?.username || '尚未登录' }}</span><router-link v-if="!platformState.account" to="/me">前往登录</router-link></div>
        <label><b>天灾模式</b><select v-model="disasterMode"><option value="none">不使用天灾</option><option value="random">随机天灾</option><option value="all">全部天灾</option><option value="custom">自定天灾（四张始终公开）</option></select></label>
      </div>
      <div class="capabilities"><article><b>卡牌与区域</b><span>加牌、置顶/置底、墓地、无视费用打出、击杀与状态切换。</span></article><article><b>阶段与数值</b><span>切换回合玩家和阶段，调整血量、天灾值、士气并触发天灾。</span></article><article><b>可复现记录</b><span>每条 GM 指令由服务端校验，并写入与实战相同的状态快照。</span></article></div>
      <p v-if="l12State.notice" class="notice">{{ l12State.notice }}</p>
      <footer><router-link to="/lobby">← 返回对战大厅</router-link><button :disabled="creating || !playerDeck || !opponentDeck || !!playerDeckError || !!opponentDeckError" @click="startSandbox">{{ creating ? '正在校验并建立…' : '建立测试沙盒' }}</button></footer>
    </section>
    <SavedDeckSelector :open="!!selectorTarget" :mode="selectorTarget || 'sandbox-player'" :guest-decks="guestDecks"
      :catalog="catalog" :current-deck-id="selectorCurrent?.id" :current-deck-revision="selectorCurrent?.revision"
      :current-deck-name="selectorCurrentName" :loading="!catalog.length" :disabled="creating"
      :confirming="selectorConfirming" :action-error="selectorError"
      @cancel="cancelDeckSelection" @confirm="confirmDeckSelection"/>
  </div>
</template>

<style scoped>
.sandbox-page{display:grid;min-height:100%;place-items:center;padding:30px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.sandbox-page>section{width:min(960px,100%);padding:34px;border:1px solid #35424a;background:#101821;box-shadow:0 26px 70px #0007}.sandbox-page header{display:flex;align-items:flex-start;justify-content:space-between;gap:20px}.sandbox-page small{color:#53c4cb;font:900 14px monospace;letter-spacing:.18em}.sandbox-page h1{margin:7px 0;font-size:31px}.sandbox-page p{color:#839097;font-size:14px;line-height:1.7}.sandbox-page header>span{display:flex;align-items:center;gap:7px;color:#8a555d;font-size:14px;font-weight:900}.sandbox-page header>span.online{color:#55c795}.sandbox-page header i{width:7px;height:7px;border-radius:50%;background:currentColor;box-shadow:0 0 8px currentColor}.sandbox-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:12px;margin:24px 0}.sandbox-grid label{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-grid b{display:block;margin-bottom:8px;font-size:14px}.sandbox-grid input,.sandbox-grid select{width:100%;padding:11px;border:1px solid #46545c;background:#070c10;color:#fff;font-weight:700;outline:none}.sandbox-grid input:focus,.sandbox-grid select:focus{border-color:#53c4cb}.capabilities{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.capabilities article{padding:17px;border-left:2px solid #c5aa5f;background:#0a1117}.capabilities b,.capabilities span{display:block}.capabilities span{margin-top:7px;color:#718087;font-size:14px;line-height:1.6}.notice{padding:10px;border-left:3px solid #a52b38;background:#211016;color:#e6a8ad!important}.sandbox-page footer{display:flex;align-items:center;justify-content:space-between;margin-top:24px}.sandbox-page a{color:#68d1d7;font-size:14px;text-decoration:none}.sandbox-page footer button{min-width:220px;padding:13px;border:1px solid #dfc26d;background:#dfc26d;color:#090d10;font-weight:900}.sandbox-page footer button:disabled{opacity:.45}@media(max-width:700px){.sandbox-page{padding:12px}.sandbox-page>section{padding:22px}.sandbox-page header,.sandbox-page footer{align-items:stretch;flex-direction:column}.sandbox-grid,.capabilities{grid-template-columns:1fr}}
.sandbox-account{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-account b,.sandbox-account span{display:block}.sandbox-account span{padding:11px;border:1px solid #46545c;background:#070c10;color:#fff;font-weight:700}.sandbox-account a{display:inline-block;margin-top:8px}
.sandbox-grid label :deep(.deck-profile){margin-bottom:10px}
.sandbox-deck{padding:14px;border:1px solid #344149;background:#0a1117}.sandbox-deck>b,.sandbox-deck>span{display:block}.sandbox-deck :deep(.deck-profile){margin-bottom:10px}.sandbox-deck>span{min-height:28px;color:#70cda3;font-size:14px}.sandbox-deck>span.invalid{color:#e89aa2}.sandbox-deck>button{width:100%;padding:10px;border:1px solid #d8bb68;background:#151b1d;color:#ecd282;font-weight:900}.sandbox-deck>button:disabled{opacity:.45;cursor:not-allowed}.sandbox-deck>p{min-height:64px;margin:0 0 10px;color:#7d8b92}
.sandbox-grid{grid-template-columns:repeat(2,minmax(0,1fr));align-items:start}.sandbox-grid label{box-sizing:border-box;min-height:0}.sandbox-grid input,.sandbox-grid select{box-sizing:border-box}
@media(max-width:700px){.sandbox-grid{grid-template-columns:1fr}}
</style>
<style src="./SandboxPage.mobile.css"></style>
