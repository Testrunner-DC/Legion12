<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { cancelMatchmaking, connect, createRoom, joinMatchmaking, joinRoom, l12State, leaveRoom, selectCustomDeck, setReady, spectateRoom, updateRoomOptions, type RoomOptions } from '@/l12/net'
import { deckCountSummary, ensureOfficialPrebuiltDecks, L12_DECK_SELECTION_SCOPES, loadDeckCatalog,
  loadSavedDecks, loadSelectedDeckName, saveSelectedDeckName, validateDeck, type DeckCard,
  type L12DeckSelectionScope, type SavedL12Deck } from '@/l12/decks'
import DeckProfile from '@/l12/DeckProfile.vue'
import SavedDeckSelector from '@/l12/SavedDeckSelector.vue'
import { getEffectiveOperationsPolicy, platformState, rankedApi, type EffectiveOperationsPolicy, type RankedOverview } from '@/l12/platform'
import RankedBroadcastTicker from './RankedBroadcastTicker.vue'

const router = useRouter()
const tab = ref<'match' | 'friendly' | 'sandbox'>('match')
const roomCode = ref('')
const roomOptions = ref<RoomOptions>({ matchModeId: 'friendly', spectating: 'public', handVisibility: 'request', disasterMode: 'all', useCardRestrictions: false })
const operationsPolicy = ref<EffectiveOperationsPolicy | null>(null)
const policyError = ref('')
const maintenanceActive = computed(() => operationsPolicy.value?.maintenance.active === true)
const customDecks = ref(loadSavedDecks())
const catalog = ref<DeckCard[]>([])
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const ranked = ref<RankedOverview | null>(null)
const selectedMatchMode = ref<'ranked' | 'casual'>('ranked')
const roomCodeCopied = ref(false)
const rankedRulesOpen = ref(false)
const selectedDeckNames = ref<Record<L12DeckSelectionScope, string>>({
  ranked: '', casual: '', friendly: '', 'sandbox-player': '', 'sandbox-opponent': '',
})
const deckSelectorOpen = ref(false)
const deckSelectorScope = ref<L12DeckSelectionScope>('friendly')
const savedDeckList = computed(() => Object.values(customDecks.value))
const activeDeckScope = computed<L12DeckSelectionScope>(() => {
  if (l12State.room || tab.value === 'friendly') return 'friendly'
  if (tab.value === 'sandbox') return 'sandbox-player'
  return selectedMatchMode.value
})
const currentDeck = computed(() => customDecks.value[selectedDeckNames.value[activeDeckScope.value]])
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
function deckError(deck: SavedL12Deck | undefined, scope = activeDeckScope.value) {
  if (!deck) return '尚未选择牌库'
  if (!catalog.value.length || (scopeNeedsPolicy(scope) && !operationsPolicy.value)) return '正在加载当前模式规则'
  return validateDeck(deck, catalog.value, restrictionsForScope(scope))
}
const currentDeckError = computed(() => deckError(currentDeck.value))
const selectorRestrictions = computed(() => restrictionsForScope(deckSelectorScope.value))
const selectorLoading = computed(() => !catalog.value.length
  || (scopeNeedsPolicy(deckSelectorScope.value) && !operationsPolicy.value))

function hydrateDeckSelections() {
  L12_DECK_SELECTION_SCOPES.forEach(scope => {
    selectedDeckNames.value[scope] = loadSelectedDeckName(scope, customDecks.value)
  })
}
function openDeckSelector() {
  if (l12State.room && me.value?.ready) return
  deckSelectorScope.value = activeDeckScope.value
  deckSelectorOpen.value = true
}
function confirmDeckSelection(deck: SavedL12Deck) {
  selectedDeckNames.value[deckSelectorScope.value] = deck.name
  saveSelectedDeckName(deckSelectorScope.value, deck.name)
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

watch(() => l12State.room?.options, options => {
  if (!options) return
  editableRoomOptions.value = {
    matchModeId: 'friendly',
    spectating: options.spectating,
    handVisibility: options.handVisibility,
    disasterMode: options.disasterMode === 'custom' || options.disasterMode === 'season' ? 'all' : options.disasterMode,
    useCardRestrictions: options.useCardRestrictions === true,
  }
}, { immediate: true, deep: true })

onMounted(async () => {
  ;[customDecks.value, catalog.value] = await Promise.all([ensureOfficialPrebuiltDecks(), loadDeckCatalog()])
  hydrateDeckSelections()
  try {
    const policy = await getEffectiveOperationsPolicy()
    operationsPolicy.value = policy
    l12State.operationsPolicy = policy
    roomOptions.value = {
      matchModeId: 'friendly',
      spectating: policy.defaultRoomConfig.spectating,
      handVisibility: policy.defaultRoomConfig.handVisibility,
      disasterMode: ['all', 'random', 'none'].includes(policy.defaultRoomConfig.disasterMode)
        ? policy.defaultRoomConfig.disasterMode as RoomOptions['disasterMode'] : 'all',
      useCardRestrictions: false,
    }
    ranked.value = await rankedApi.overview()
  } catch (error) { policyError.value = error instanceof Error ? error.message : '运营规则加载失败' }
  if (platformState.account && platformState.token && l12State.status === 'offline') {
    try { await connect() } catch { /* 页面保留离线提示，创建/加入时仍可重试。 */ }
  }
})

// 创建、加入或恢复好友房时，把该模式已确认的牌库同步到房间；选择器取消不会触发这里。
watch(() => [l12State.room?.roomCode, currentDeck.value?.name, currentDeckError.value] as const, ([roomCode]) => {
  const deck = currentDeck.value
  if (!roomCode || !deck || currentDeckError.value || me.value?.ready) return
  if (me.value?.customDeck && me.value.deckName === deck.name) return
  selectCustomDeck(deck)
}, { immediate: true })

async function chooseFaction(faction: 'order' | 'chaos' | 'fate') {
  try { await rankedApi.selectFaction(faction); ranked.value = await rankedApi.overview() }
  catch (error) { l12State.notice = error instanceof Error ? error.message : '派系选择失败' }
}
async function onMatch() {
  try {
    if (!operationsAllowed() || !(await ensureConnected())) return
    if (!currentDeck.value || currentDeckError.value) {
      l12State.notice = currentDeckError.value || '当前模式没有可用牌库'
      return
    }
    if (selectedMatchMode.value === 'ranked' && !ranked.value?.profile.faction) { l12State.notice = '请先选择本赛季派系'; return }
    joinMatchmaking(selectedMatchMode.value, currentDeck.value)
  } catch {}
}

async function ensureConnected() {
  if (!platformState.account || !platformState.token) { l12State.notice = '请先登录账号'; return false }
  if (l12State.status !== 'online') await connect()
  return true
}
function operationsAllowed() {
  if (!maintenanceActive.value) return true
  l12State.notice = operationsPolicy.value?.maintenance.message || '服务器正在维护，暂时无法开始新的对局'
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
    <section v-if="maintenanceActive" class="maintenance-banner"><b>服务器维护中</b><span>{{ operationsPolicy?.maintenance.message || '暂时停止创建和加入新对局，已开始对局与重连不受影响。' }}</span></section>
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
        <div class="room-settings"><div><b>禁限卡规则</b><select v-model="editableRoomOptions.useCardRestrictions"><option :value="false">不启用运营禁限卡</option><option :value="true">启用运营禁限卡</option></select></div><div><b>观战权限</b><select v-model="editableRoomOptions.spectating"><option value="public">允许所有玩家直接观战</option><option value="friends">仅限好友观战</option><option value="disabled">禁止观战</option></select></div><div><b>观战者查看手牌</b><select v-model="editableRoomOptions.handVisibility"><option value="request">需要当局玩家同意</option><option value="public">默认公开</option></select></div><div><b>天灾模式</b><select v-model="editableRoomOptions.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="none">不使用天灾</option></select></div></div>
        <button type="button" @click="saveRoomRules">保存房间规则</button>
      </section>
      <section class="room-current-deck"><DeckProfile v-if="currentDeck" compact :master-id="currentDeck.masterId" :master-name="byId.get(currentDeck.masterId)?.nameZh" :name="currentDeck.name" context="好友房牌库" :meta="`${deckCountSummary(currentDeck.cardIds, byId).label} 张主牌`"/><p v-else>当前没有已保存牌库</p><div><span :class="{ invalid: !!currentDeckError }">{{ currentDeckError || '符合好友房规则' }}</span><button type="button" :disabled="me?.ready" @click="openDeckSelector">更换牌库</button></div></section>
      <footer><button class="leave-room" type="button" @click="leaveRoom()">{{ l12State.room.yourPlayerIndex === 0 ? '关闭房间并返回大厅' : '离开房间并返回大厅' }}</button><router-link to="/decks">管理我的牌库</router-link><button class="primary" :disabled="l12State.room.players.length < 2 || !!currentDeckError" @click="setReady(!me?.ready)">{{ me?.ready ? '取消准备' : '准备对战' }}</button></footer>
    </section>

    <template v-else>
      <section class="current-deck panel"><DeckProfile v-if="currentDeck" :master-id="currentDeck.masterId" :master-name="byId.get(currentDeck.masterId)?.nameZh" :name="currentDeck.name" :context="activeDeckScope === 'ranked' ? '排位当前牌库' : activeDeckScope === 'casual' ? '休闲当前牌库' : activeDeckScope === 'friendly' ? '好友房当前牌库' : '沙盒我方牌库'" :meta="`${deckCountSummary(currentDeck.cardIds, byId).label} 张主牌`"/><DeckProfile v-else context="当前牌库" meta="当前没有已保存牌库"/><div class="current-deck-actions"><span :class="{ invalid: !!currentDeckError }">{{ currentDeckError || '符合当前模式规则' }}</span><button type="button" @click="openDeckSelector">更换牌库</button></div></section>
      <div class="mode-tabs"><button :class="{ active: tab === 'match' }" @click="tab = 'match'">匹配</button><button :class="{ active: tab === 'friendly' }" @click="tab = 'friendly'">好友房</button><button :class="{ active: tab === 'sandbox' }" @click="tab = 'sandbox'">单人</button></div>
      <div v-if="tab === 'match' && selectedMatchMode === 'ranked' && ranked" class="faction-totals faction-totals--overview" data-ui-contract="faction-totals-above-public-match"><article v-for="faction in ranked.config.factions" :key="faction.id" :style="{ '--accent': faction.color }"><b>{{ faction.name }}</b><span>七曜值 {{ (ranked.factionTotals[faction.name] || 0).toLocaleString() }}</span></article></div>

      <section v-if="tab === 'match'" class="mode-panel panel"><header class="public-match-head"><div><small>PUBLIC MATCH</small><h2>公开匹配</h2><p>排位使用赛季天灾与禁限卡；休闲使用全部天灾且不启用禁限卡。</p></div><button v-if="selectedMatchMode === 'ranked'" class="ranked-rules-button" type="button" @click="rankedRulesOpen = true">排位规则</button></header>
        <p v-if="selectedMatchMode === 'ranked' && operationsPolicy" class="season-name">当前赛季 · {{ operationsPolicy.season.name }}</p>
        <div v-if="selectedMatchMode === 'ranked' && ranked && !ranked.profile.faction" class="faction-select"><b>选择本赛季派系</b><span>赛季中可改选；改选后七曜值、定级和本赛季战绩重新开始。</span><div><button v-for="faction in ranked.config.factions" :key="faction.id" @click="chooseFaction(faction.id)">{{ faction.name }}</button></div></div>
        <div v-else-if="selectedMatchMode === 'ranked' && ranked" class="ranked-profile"><b>{{ ranked.profile.faction }} · {{ ranked.profile.placed ? ranked.profile.tier : `定级 ${ranked.profile.placementPlayed}/${ranked.config.placementMatches}` }}</b><span>{{ ranked.profile.displayValue }}<template v-if="ranked.profile.titles?.length"> · {{ ranked.profile.titles.join(' · ') }}</template><template v-else-if="ranked.profile.title"> · {{ ranked.profile.title }}</template></span><button @click="ranked.profile.faction = undefined">改选派系</button></div>
        <div class="match-options"><button :class="{ active: selectedMatchMode === 'ranked' }" @click="selectedMatchMode = 'ranked'">排位匹配</button><button :class="{ active: selectedMatchMode === 'casual' }" @click="selectedMatchMode = 'casual'">休闲匹配</button></div>
        <button v-if="l12State.matchmaking?.queued" class="cancel-match" @click="cancelMatchmaking()">取消{{ l12State.matchmaking.mode === 'ranked' ? '排位' : '休闲' }}匹配</button><button v-else class="primary" :disabled="!currentDeck || !!currentDeckError || maintenanceActive" @click="onMatch">开始{{ selectedMatchMode === 'ranked' ? '排位' : '休闲' }}匹配</button>
      </section>

      <section v-else-if="tab === 'friendly'" class="mode-panel panel friendly-panel"><small>FRIENDLY ROOM</small><h2>创建、加入或观战房间</h2><div class="account-identity" :class="{ missing: !platformState.account }"><span>{{ platformState.account ? '当前账号' : '尚未登录' }}</span><b>{{ platformState.account?.username || '登录后才能创建、加入或观战房间' }}</b><router-link to="/me">{{ platformState.account ? '账号设置 →' : '前往登录 →' }}</router-link></div><div class="join-row"><button class="primary" :disabled="maintenanceActive" @click="onCreate">创建新房间</button><span>房间码</span><input v-model="roomCode" maxlength="6" placeholder="输入 6 位房间码" @keyup.enter="onJoin"/><div class="join-actions"><button :disabled="maintenanceActive" @click="onJoin">加入对战</button><button class="spectate-button" :disabled="maintenanceActive" @click="onSpectate">直接观战</button></div></div><div class="room-settings"><div><b>禁限卡规则</b><select v-model="roomOptions.useCardRestrictions"><option :value="false">不启用运营禁限卡</option><option :value="true">启用运营禁限卡</option></select></div><div><b>观战权限</b><select v-model="roomOptions.spectating"><option value="public">允许所有玩家直接观战</option><option value="friends">仅限好友观战</option><option value="disabled">禁止观战</option></select></div><div><b>观战者查看手牌</b><select v-model="roomOptions.handVisibility"><option value="request">需要当局玩家同意</option><option value="public">默认公开</option></select></div><div><b>天灾模式</b><select v-model="roomOptions.disasterMode"><option value="all">全部天灾（禁用与选取）</option><option value="random">随机天灾（3张随机天灾＋最终堙灭）</option><option value="none">不使用天灾（天灾值恒为0）</option></select></div></div></section>

      <section v-else class="mode-panel panel"><small>TEST SANDBOX</small><h2>单人测试沙盒</h2><p>用于验证牌库、卡效、阶段与交互，不计入玩家战绩和排行榜。</p><button class="primary" @click="router.push('/sandbox')">进入测试沙盒</button></section>
    </template>
    <SavedDeckSelector :open="deckSelectorOpen" :mode="deckSelectorScope" :decks="savedDeckList"
      :catalog="catalog" :current-deck-name="selectorCurrentName" :restrictions="selectorRestrictions"
      :loading="selectorLoading" :disabled="!!(l12State.room && me?.ready)"
      @cancel="deckSelectorOpen = false" @confirm="confirmDeckSelection"/>
    <Teleport to="body"><div v-if="rankedRulesOpen" class="ranked-rules-backdrop" @click.self="rankedRulesOpen = false"><section class="ranked-rules-modal" role="dialog" aria-modal="true" aria-label="排位规则"><header><div><small>RANKED RULES</small><h2>排位规则</h2><p>{{ operationsPolicy?.season.name || '当前赛季' }}</p></div><button type="button" @click="rankedRulesOpen = false">×</button></header><div class="ranked-rules-scroll"><article><h3>七曜值</h3><p>完成 {{ ranked?.config.placementMatches || 5 }} 场定级赛后进入段位。胜负结算只显示自己的七曜值与段位变化；连胜、对手强度、段位保护与分差修正均由服务器权威计算。</p></article><article><h3>段位与称号</h3><div v-for="faction in ranked?.config.factions || []" :key="faction.id" class="rules-faction"><b>{{ faction.name }}</b><span>{{ faction.tiers.map(tier => `${tier.name}（${tier.minimum.toLocaleString()}）`).join(' → ') }}</span><small>仅最高段位可获得：第1名「{{ faction.firstTitle }}」；第2至5名「{{ faction.topFiveTitle }}」。每位主宰的赛季最强玩家另有专属称号。</small></div></article><article><h3>时间限制</h3><p>每位玩家总操作时间25分钟；单次操作与掉线重连均最多4分钟。单方超时判负；双方均无法恢复则对局无效。</p></article><article><h3>本赛季天灾</h3><p>{{ operationsPolicy?.disasterCardIds.length || 0 }} 张（含固定堙灭）。排位强制使用赛季天灾池；休闲、好友房与沙盒不继承此限制。</p></article><article><h3>本赛季禁限卡</h3><p v-if="operationsPolicy?.cardRestrictions.length">共 {{ operationsPolicy.cardRestrictions.length }} 条；牌库确认与匹配建立时均由服务器校验。具体限制：{{ operationsPolicy.cardRestrictions.map(rule => `${rule.cardId} 上限${rule.maxCopies}`).join('、') }}</p><p v-else>本赛季未设置禁限卡。</p></article></div><footer><button type="button" @click="rankedRulesOpen = false">我知道了</button></footer></section></div></Teleport>
    <p v-if="l12State.notice" class="battle-notice">{{ l12State.notice }}</p>
  </div>
</template>

<style scoped>
.public-match-head{display:flex;align-items:flex-start;justify-content:space-between;gap:24px}.public-match-head h2{margin:4px 0}.ranked-rules-button{min-width:150px;min-height:64px;border:1px solid #e1c759;background:#151c23;color:#f0d46d;font-weight:900}.season-name{width:max-content;padding:7px 10px;border-left:3px solid #d7bc55;background:#1d1b12;color:#ead982;font-size:12px}
.ranked-rules-backdrop{position:fixed;z-index:4000;inset:0;display:grid;place-items:center;padding:20px;background:rgba(0,0,0,.68)}.ranked-rules-modal{display:grid;width:min(760px,96vw);max-height:min(820px,92vh);overflow:hidden;border:1px solid #9b8438;background:#0d151d;color:#eef1ed;box-shadow:0 24px 80px #000}.ranked-rules-modal>header{display:flex;align-items:flex-start;justify-content:space-between;padding:20px 22px;border-bottom:1px solid #34414a}.ranked-rules-modal header small{color:#58c6cd;font:900 9px monospace;letter-spacing:.16em}.ranked-rules-modal header h2{margin:5px 0 2px}.ranked-rules-modal header p{margin:0;color:#d5ba59}.ranked-rules-modal header button{border:0;background:transparent;color:#b9c1c4;font-size:28px}.ranked-rules-scroll{display:grid;gap:12px;overflow:auto;padding:18px 22px}.ranked-rules-scroll article{padding:14px;border:1px solid #2c3943;background:#101b25}.ranked-rules-scroll h3{margin:0 0 8px;color:#e7ce72}.ranked-rules-scroll p{margin:0;color:#b5c0c4;line-height:1.75}.rules-faction{display:grid;gap:6px;margin-top:10px;padding:10px;border-left:3px solid #7b63dd;background:#0b1219}.rules-faction span,.rules-faction small{color:#9caaaf;line-height:1.6}.ranked-rules-modal>footer{display:flex;justify-content:flex-end;padding:14px 22px;border-top:1px solid #34414a}.ranked-rules-modal>footer button{padding:10px 24px;border:1px solid #ddc15a;background:#332a12;color:#f0d879;font-weight:900}
.battle-hub{width:min(980px,calc(100% - 40px));min-height:100%;margin:0 auto;padding:34px 0 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-start;justify-content:space-between;margin-bottom:22px}.page-head small,.panel>small,.panel header small{color:#50c4cc;font:900 9px monospace;letter-spacing:.2em}.page-head h1{margin:5px 0;font-size:30px}.page-head p,.mode-panel>p{margin:0;color:#7c8990;font-size:12px;line-height:1.7}.server-state{display:flex;align-items:center;gap:8px;color:#7b858a;font-size:11px;font-weight:900}.server-state i{width:8px;height:8px;border-radius:50%;background:#687177}.server-state.online i{background:#54c695;box-shadow:0 0 8px #54c695}.panel{border:1px solid rgba(235,230,216,.17);background:#101821;box-shadow:0 18px 50px rgba(0,0,0,.18)}.current-deck{display:grid;grid-template-columns:58px 1fr auto;align-items:center;gap:16px;padding:18px}.deck-thumb{display:grid;width:52px;height:70px;place-items:center;border:1px solid #d2b76f;background:linear-gradient(145deg,#6e1825,#13252a);font-size:20px;font-weight:900}.current-deck small,.current-deck b,.current-deck span{display:block}.current-deck small{color:#728089;font-size:10px}.current-deck b{margin:4px 0;font-size:17px}.current-deck span{color:#7f8b91;font-size:10px}.current-deck-actions{text-align:right}.current-deck-actions span.invalid,.room-current-deck span.invalid{color:#ef9ca4}.current-deck-actions button,.room-current-deck button{margin-top:7px;padding:8px 11px;border:1px solid #d5b862;background:#151b1d;color:#ead083;font-size:11px;font-weight:900}.mode-tabs{display:grid;grid-template-columns:repeat(3,1fr);margin:18px 0;border:1px solid rgba(235,230,216,.17);background:#0b1117}.mode-tabs button{padding:14px;border:0;background:transparent;color:#738089;font-weight:900}.mode-tabs button.active{background:linear-gradient(135deg,#8b1c2a,#ad2d38);color:#fff}.mode-panel{padding:28px}.mode-panel h2{margin:6px 0 8px;font-size:23px}.mode-panel>p{max-width:650px}.mode-panel label{display:block;margin:20px 0 12px;color:#aab2b4;font-size:11px;font-weight:900}.mode-panel input,.mode-panel select{width:100%;padding:12px;border:1px solid #45535c;background:#080e14;color:#fff;outline:none}.mode-panel input:focus,.mode-panel select:focus{border-color:#50c4cc}.primary{border-color:#e2c473!important;background:#e2c473!important;color:#0a0d0f!important;font-weight:900}.mode-panel>button.primary{min-width:220px;margin-top:22px;padding:13px;border:1px solid}.match-options{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:20px}.match-options button{padding:15px;border:1px solid #3e4a52;background:#0b1117;color:#7b868b}.join-row{display:grid;grid-template-columns:1fr auto 1fr auto;align-items:center;gap:9px}.join-row button{height:42px;padding:0 18px;border:1px solid #52606a;background:#121c24;color:#fff}.join-row span{color:#68757c;font-size:10px}.room-settings{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:18px}.room-settings>div{padding:14px;border:1px solid #354149;background:#0b1218}.room-settings b{display:block;margin-bottom:8px;font-size:11px}.room-stage{padding:26px}.room-stage>header{display:flex;align-items:center;justify-content:space-between}.room-stage h2{margin:4px 0}.room-stage code{padding:9px 12px;border:1px solid #d9bc6d;color:#f0d889;font-size:17px;letter-spacing:.16em}.versus{position:relative;display:grid;grid-template-columns:1fr 1fr;gap:60px;margin:24px 0}.versus article{display:flex;min-height:150px;flex-direction:column;align-items:center;justify-content:center;border:1px solid #37434a;background:#0a1117}.versus article.empty{opacity:.55}.versus span{color:#66747c;font:900 9px monospace;letter-spacing:.15em}.versus article>b{margin:10px 0 4px;font-size:20px}.versus p{margin:0;color:#78858b;font-size:10px}.versus em{margin-top:12px;color:#d9bb68;font-size:10px;font-style:normal;font-weight:900}.versus>strong{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);color:#a52b38}.room-current-deck{display:grid;grid-template-columns:minmax(0,1fr) auto;align-items:center;gap:14px;padding:12px;border:1px solid #39464e;background:#0a1117}.room-current-deck :deep(.deck-profile){border:0;background:transparent}.room-current-deck>div{text-align:right}.room-current-deck span{display:block;color:#7f8b91;font-size:10px}.room-current-deck>p{color:#87939a;font-size:11px}.room-stage footer{display:flex;align-items:center;justify-content:space-between;margin-top:20px}.room-stage footer a{color:#55c4ca;font-size:11px;text-decoration:none}.room-stage footer button{min-width:220px;padding:12px;border:1px solid}.battle-notice{padding:11px;border-left:3px solid #a52b38;background:#211016;color:#e6a8ad;font-size:11px}
@media(max-width:700px){.battle-hub{width:auto;padding:20px 12px 50px}.page-head{gap:12px}.page-head h1{font-size:25px}.current-deck{grid-template-columns:1fr}.current-deck-actions{text-align:left}.mode-panel{padding:20px}.join-row{grid-template-columns:1fr}.join-row span{text-align:center}.room-settings,.match-options,.versus,.room-current-deck{grid-template-columns:1fr}.room-current-deck>div{text-align:left}.versus{gap:10px}.versus>strong{display:none}.room-stage footer{align-items:stretch;flex-direction:column;gap:12px}.room-stage footer button{width:100%}}
.join-actions{display:flex;gap:6px}.join-actions .spectate-button{border-color:#4faeb5;color:#80dce2}
.room-code{display:flex;align-items:stretch;gap:7px}.room-code code{display:grid;place-items:center}.room-code button{padding:0 12px;border:1px solid #6d765f;background:#17201c;color:#f4e9bc;font-size:10px;font-weight:900;white-space:nowrap}.room-code button:hover{border-color:#e0c16d;background:#2a2718;color:#fff}
@media(max-width:700px){.join-actions{display:grid;grid-template-columns:1fr 1fr}}
.leave-room{border-color:#7b4147!important;background:#211116!important;color:#e7aeb3!important;font-weight:900}
.account-identity{display:grid;grid-template-columns:auto 1fr auto;align-items:center;gap:12px;margin:18px 0;padding:12px 14px;border:1px solid #3e4b53;background:#0a1117}.account-identity span{color:#75838a;font-size:9px;font-weight:900}.account-identity b{font-size:13px}.account-identity a{color:#55c4ca;font-size:10px;font-weight:900;text-decoration:none}.account-identity.missing{border-color:#7b4147}.account-identity.missing b{color:#dda6ab}
.player-online{margin-top:8px;color:#b76570;font-size:9px;font-style:normal;font-weight:900}.player-online.online{color:#58c99a}.room-rule-summary{display:flex;align-items:center;gap:8px;margin:-8px 0 18px;padding:11px 14px;border:1px solid #354149;background:#0a1117}.room-rule-summary b{margin-right:6px;color:#e4c675;font-size:11px}.room-rule-summary span{padding:4px 7px;background:#17212a;color:#aab4b8;font-size:9px;font-weight:900}
@media(max-width:700px){.room-rule-summary{align-items:stretch;flex-direction:column}.room-rule-summary span{text-align:center}}
.room-decks button{display:grid;grid-template-columns:38px 1fr;align-items:center;gap:8px;padding:8px}.room-decks button>img{width:38px;height:38px;object-fit:cover;border:1px solid #596269;border-radius:2px}.room-decks button>span,.room-decks button b,.room-decks button small{display:block}.room-decks button small{margin-top:4px;color:#77848a;font-size:9px}
.maintenance-banner,.policy-warning{display:flex;align-items:center;gap:12px;margin-bottom:16px;padding:13px 16px;border:1px solid #9a7135;background:#2a1e0e;color:#f0d695;font-size:11px}.maintenance-banner span,.policy-warning span{color:#c8b98f}.policy-warning{border-color:#6b4c52;background:#221217;color:#e1b2b8}.join-row button:disabled,.room-settings select:disabled{cursor:not-allowed;opacity:.45}
.current-deck{grid-template-columns:minmax(0,1fr) auto}.current-deck :deep(.deck-profile){border:0;background:transparent;padding:0}.room-decks button{display:block;padding:0}.room-decks button :deep(.deck-profile){width:100%;border:0;background:transparent}.room-decks button.active :deep(.deck-profile){background:#202017}
.room-rule-editor{margin:0 0 18px;padding:14px;border:1px solid #695b36;background:#11140f}.room-rule-editor>header{display:flex;align-items:center;justify-content:space-between}.room-rule-editor>header span{color:#877d62;font-size:9px}.room-rule-editor>.room-settings{margin-top:12px}.room-rule-editor>button{display:block;margin:12px 0 0 auto;padding:9px 18px;border:1px solid #d7bb69;background:#d7bb69;color:#111;font-weight:900}
@media(max-width:700px){.current-deck{grid-template-columns:1fr}}
.match-options button.active{border-color:#d8ba65;background:#2a2414;color:#f4db90}.faction-totals{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:18px 0}.faction-totals--overview{margin:-1px 12px 18px;padding:12px;border:1px solid rgba(235,230,216,.17);background:#101821}.faction-totals article{padding:12px;border:1px solid var(--accent);background:#0a1117}.faction-totals b,.faction-totals span{display:block}.faction-totals span{margin-top:5px;color:#d8c77f;font-size:11px}.faction-select,.ranked-profile{margin:14px 0;padding:14px;border:1px solid #45535c;background:#0a1117}.faction-select>b,.faction-select>span,.ranked-profile>b,.ranked-profile>span{display:block}.faction-select>span,.ranked-profile>span{margin:5px 0;color:#89969b;font-size:10px}.faction-select>div{display:flex;gap:8px;margin-top:10px}.faction-select button,.ranked-profile button,.cancel-match{padding:9px 14px;border:1px solid #887239;background:#211d10;color:#f0d582}.ranked-profile{display:grid;grid-template-columns:1fr auto;align-items:center}.ranked-profile span{grid-column:1}.ranked-profile button{grid-row:1/3;grid-column:2}.cancel-match{min-width:220px;margin-top:22px}
.match-found-stage{display:grid;min-height:260px;place-items:center;padding:36px;text-align:center}.match-found-stage small{color:#50c4cc;font:900 10px monospace;letter-spacing:.2em}.match-found-stage h2{margin:4px 0 0;font-size:30px}.match-found-stage p{margin:0;color:#9aa5aa;font-size:12px}.match-found-stage code{padding:8px 12px;border:1px solid #d9bc6d;color:#f0d889;letter-spacing:.18em}
.ranked-rules-modal{grid-template-rows:auto minmax(0,1fr) auto;height:min(820px,92vh);min-height:0}
.ranked-rules-scroll{min-height:0;align-content:start;overflow-x:hidden;overflow-y:scroll;overscroll-behavior:contain;scrollbar-gutter:stable}
</style>
