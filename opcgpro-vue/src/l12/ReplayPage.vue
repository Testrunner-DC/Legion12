<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import L12SettingsModal from './site/L12SettingsModal.vue'
import { closeSettingsAndRestore, openBugFeedbackFromSettings, rememberSettingsOpener } from './site/bugFeedbackEntry'
import { loadDeckCatalog, type DeckCard } from './decks'
import { adminApi, PlatformRequestError, platformRequest } from './platform'
import { adminReplayDetail, consumeImportedReplay, replayFocusCardAt, replayGameAt, type MatchDetail } from './replayModel'
import { isMobileDeviceExperience, landscapeTeleportTarget } from './mobileViewport'
import { buildReplayIndex, type ReplayIndexCategory } from './replayIndex'

const route = useRoute()
const router = useRouter()
const detail = ref<MatchDetail | null>(null)
const cards = ref<DeckCard[]>([])
const selectedStep = ref(0)
const playing = ref(false)
const playbackSpeed = ref<1 | 2 | 3>(1)
const loading = ref(true)
const error = ref('')
const catalogWarning = ref('')
const replayNextCursor = ref<string | undefined>()
const replayTotalCommands = ref(0)
const loadingReplayPage = ref(false)
const replayPresentationBusy = ref(false)
const indexOpen = ref(false)
const indexRound = ref<number | ''>('')
const indexCategory = ref<ReplayIndexCategory | ''>('')
const indexSearch = ref('')
const indexPage = ref(0)
const indexError = ref('')
const settingsOpen = ref(false)
const settingsOpener = ref<HTMLElement | null>(null)
const indexPageSize = 40
// Mobile replay is intentionally a hard stop: do not load its data or mount a
// board behind a message that a player cannot use on this form factor.
const mobileReplayBlocked = isMobileDeviceExperience()
let timer: ReturnType<typeof setTimeout> | null = null
let playbackGeneration = 0
let disposed = false

const replayCatalog = computed(() => new Map(cards.value.map(card => [card.id, card])))
const currentGame = computed(() => {
  const game = detail.value ? replayGameAt(detail.value, selectedStep.value, replayCatalog.value) : null
  if (!game || route.name !== 'admin-match-replay') return game
  // Raw archive frames are authoritative; IdentityKnown was not a persisted viewer flag.
  // Decorate only this admin display instance, never the shared mapper or exported JSON.
  for (const player of game.players)
    for (const card of player.field.flat())
      if (card?.hidden && card.cardId && card.cardId !== 'hidden-card') card.identityKnown = true
  return game
})
const replayFocusCard = computed(() => detail.value ? replayFocusCardAt(detail.value, selectedStep.value, cards.value) : null)
const isAdminReplay = computed(() => route.name === 'admin-match-replay')
const replayIndex = computed(() => detail.value ? buildReplayIndex(detail.value, isAdminReplay.value) : { rounds: [], events: [] })
const indexedRounds = computed(() => [...new Set(replayIndex.value.rounds.map(item => item.round))])
const filteredIndexEvents = computed(() => replayIndex.value.events.filter(item =>
  (indexRound.value === '' || item.round === indexRound.value)
  && (!indexCategory.value || item.category === indexCategory.value)
  && (!indexSearch.value.trim() || item.label.includes(indexSearch.value.trim()))))
const visibleIndexEvents = computed(() => filteredIndexEvents.value.slice(indexPage.value * indexPageSize, (indexPage.value + 1) * indexPageSize))
const visibleRoundStarts = computed(() => replayIndex.value.rounds.filter(item =>
  item.round === (indexRound.value || currentGame.value?.round)).slice(0, 4))
const totalSteps = computed(() => isAdminReplay.value ? replayTotalCommands.value : detail.value?.commands.length ?? 0)
const atFirst = computed(() => selectedStep.value <= 0)
const atLast = computed(() => selectedStep.value >= totalSteps.value - 1)
const returnLabel = computed(() => isAdminReplay.value ? '返回后台对局档案' : '返回对局记录')
const replayResult = computed(() => {
  if (!detail.value || !currentGame.value || !atLast.value) return null
  const winner = detail.value.match.winner ?? currentGame.value.winner
  if (winner === 0 || winner === 1) return {
    state: 'decided',
    players: [
      { name: detail.value.match.player0, result: winner === 0 ? '胜' : '负' },
      { name: detail.value.match.player1, result: winner === 1 ? '胜' : '负' },
    ],
  }
  const invalid = Boolean(detail.value.match.error)
  return {
    state: invalid ? 'invalid' : 'draw',
    players: [
      { name: detail.value.match.player0, result: invalid ? '无效' : '平' },
      { name: detail.value.match.player1, result: invalid ? '无效' : '平' },
    ],
  }
})

onMounted(() => {
  if (!mobileReplayBlocked) void loadReplay()
})
onBeforeUnmount(() => { disposed = true; stop() })

async function loadReplay() {
  loading.value = true
  error.value = ''
  catalogWarning.value = ''
  try {
    try { cards.value = await loadDeckCatalog() }
    catch {
      cards.value = []
      catalogWarning.value = '卡牌资料暂不可用，回放仍可播放；部分主宰与阵营效果信息可能不完整。'
    }
    if (route.name === 'json-replay') detail.value = consumeImportedReplay()
    else if (isAdminReplay.value) {
      const matchId = String(route.params.matchId ?? '')
      if (matchId) {
        const [summary, firstPage] = await Promise.all([adminApi.match(matchId), adminApi.replayPage(matchId)])
        detail.value = adminReplayDetail({ ...summary, replay: firstPage.items })
        replayNextCursor.value = firstPage.nextCursor
        replayTotalCommands.value = firstPage.totalCommands
      }
    }
    else {
      const matchId = String(route.params.matchId ?? '')
      if (matchId) detail.value = await platformRequest<MatchDetail>(`/api/matches/${encodeURIComponent(matchId)}`)
    }
    if (!detail.value) throw new Error('未找到可播放的回放，请返回对局记录重新选择')
    if (!detail.value.commands.length) throw new Error('这场对局没有可播放的状态快照')
    selectedStep.value = 0
  } catch (reason) {
    error.value = reason instanceof PlatformRequestError && reason.status === 410 && reason.code === 'sandbox_replay_expired'
      ? '回放已过期'
      : reason instanceof Error ? reason.message : '读取回放失败'
  } finally { loading.value = false }
}

function clearPlaybackTimer() {
  if (timer) window.clearTimeout(timer)
  timer = null
}

function stop() {
  playing.value = false
  playbackGeneration++
  clearPlaybackTimer()
}

function previous() {
  stop()
  selectedStep.value = Math.max(0, selectedStep.value - 1)
}

async function ensureReplayStepLoaded(index: number, indexOnly = false) {
  if (!isAdminReplay.value || !detail.value || index < detail.value.commands.length) return true
  if (!replayNextCursor.value || loadingReplayPage.value) return false
  loadingReplayPage.value = true
  try {
    const matchId = String(route.params.matchId ?? '')
    const page = await adminApi.replayPage(matchId, replayNextCursor.value)
    if (disposed) return false
    if (!page.items.length && page.nextCursor === replayNextCursor.value) throw new Error('本页回放暂不可用')
    detail.value.commands.push(...page.items)
    replayNextCursor.value = page.nextCursor
    replayTotalCommands.value = page.totalCommands
    return index < detail.value.commands.length
  } catch (reason) {
    const message = reason instanceof PlatformRequestError && reason.status === 410 && reason.code === 'sandbox_replay_expired'
      ? '回放已过期'
      : reason instanceof Error ? reason.message : '读取下一页回放失败'
    if (indexOnly) indexError.value = message
    else error.value = message
    stop()
    return false
  } finally { loadingReplayPage.value = false }
}
async function next() {
  stop()
  const target = Math.min(totalSteps.value - 1, selectedStep.value + 1)
  if (await ensureReplayStepLoaded(target)) selectedStep.value = target
}

function openIndex() {
  stop()
  if (indexOpen.value) return closeIndex()
  indexOpen.value = true
  void nextTick(() => document.getElementById('replay-index')?.querySelector('button')?.focus())
}

function closeIndex() {
  indexOpen.value = false
  void nextTick(() => document.querySelector<HTMLButtonElement>('.replay-controls [aria-controls="replay-index"]')?.focus())
}

function seekIndex(step: number) {
  if (replayPresentationBusy.value || loadingReplayPage.value || !detail.value
    || step < 0 || step >= detail.value.commands.length) return
  stop()
  selectedStep.value = step
  closeIndex()
}

async function loadMoreIndex() {
  if (!detail.value || loadingReplayPage.value) return
  stop()
  indexError.value = ''
  await ensureReplayStepLoaded(detail.value.commands.length, true)
}

function toggle() {
  if (!detail.value?.commands.length) return
  if (playing.value) return stop()
  if (atLast.value) selectedStep.value = 0
  playing.value = true
  startPlaybackTimer()
}

function startPlaybackTimer() {
  clearPlaybackTimer()
  const generation = ++playbackGeneration
  schedulePlaybackAdvance(generation)
}

function schedulePlaybackAdvance(generation: number) {
  if (!playing.value || generation !== playbackGeneration) return
  const delay = Math.max(120, Math.round(650 / playbackSpeed.value))
  timer = window.setTimeout(() => void advancePlayback(generation), delay)
}

async function advancePlayback(generation: number) {
  timer = null
  if (!playing.value || generation !== playbackGeneration) return
  if (replayPresentationBusy.value) {
    timer = window.setTimeout(() => void advancePlayback(generation), 40)
    return
  }
  if (atLast.value) return stop()
  const target = selectedStep.value + 1
  if (!await ensureReplayStepLoaded(target)) return stop()
  if (!playing.value || generation !== playbackGeneration) return
  selectedStep.value = target
  if (atLast.value) return stop()
  schedulePlaybackAdvance(generation)
}

function setPlaybackSpeed(speed: 1 | 2 | 3) {
  if (playbackSpeed.value === speed) return
  playbackSpeed.value = speed
  if (playing.value) startPlaybackTimer()
}

function returnFromReplay() {
  stop()
  if (isAdminReplay.value) {
    router.push({ name: 'admin', query: { ...route.query, section: 'matches', matchId: detail.value?.match.matchId } })
    return
  }
  router.push(route.name === 'json-replay'
    ? { name: 'records' }
    : { name: 'records', query: { selected: detail.value?.match.matchId } })
}
function openReplaySettings(event?: Event) {
  settingsOpener.value = rememberSettingsOpener(event)
  settingsOpen.value = true
}
function closeReplaySettings() {
  void closeSettingsAndRestore(() => { settingsOpen.value = false }, settingsOpener.value)
}
function openReplayFeedback() {
  void openBugFeedbackFromSettings(() => { settingsOpen.value = false }, settingsOpener.value)
}
</script>

<template>
  <div class="game-page replay-page">
    <main v-if="mobileReplayBlocked" class="replay-mobile-blocked" role="status">
      <p>请到电脑端查看回放</p>
      <button type="button" @click="openReplaySettings">设置</button>
      <button @click="returnFromReplay">{{ returnLabel }}</button>
    </main>
    <GameBoard v-else-if="currentGame" :game="currentGame" :replay-focus-card="replayFocusCard"
      :replay-playback-speed="playbackSpeed" :reveal-both-hands="isAdminReplay" read-only @replay-presentation-change="replayPresentationBusy = $event" />

    <Teleport v-if="!mobileReplayBlocked" :to="landscapeTeleportTarget()">
      <div class="replay-route-controls">
        <span v-if="detail">{{ detail.match.player0 }} VS {{ detail.match.player1 }}</span>
        <button type="button" @click="openReplaySettings">设置</button>
        <button @click="returnFromReplay">{{ returnLabel }}</button>
      </div>

      <p v-if="catalogWarning" class="replay-catalog-warning" role="status">{{ catalogWarning }}</p>

      <div v-if="replayResult" class="replay-result" :data-state="replayResult.state" aria-live="polite">
        <strong>对局结束</strong>
        <span v-for="(player, index) in replayResult.players" :key="index" :data-result="player.result"><b>{{ player.name }}</b><em>{{ player.result }}</em></span>
      </div>

      <div v-if="currentGame" class="replay-controls" aria-label="回放控制">
        <button :disabled="atFirst || replayPresentationBusy" @click="previous">上一步</button>
        <button class="play" @click="toggle">{{ playing ? '暂停' : '播放' }}</button>
        <button v-for="speed in ([1, 2, 3] as const)" :key="speed" class="speed" :class="{ active: playbackSpeed === speed }" :aria-pressed="playbackSpeed === speed" @click="setPlaybackSpeed(speed)">{{ speed.toFixed(1) }}</button>
        <button :disabled="atLast || loadingReplayPage || replayPresentationBusy" @click="next">{{ loadingReplayPage ? '加载中' : '下一步' }}</button>
        <button :aria-expanded="indexOpen" aria-controls="replay-index" @click="openIndex">回放定位</button>
        <small>步骤 {{ selectedStep + 1 }} / {{ totalSteps }}<template v-if="isAdminReplay"> · 分页</template></small>
      </div>

      <section v-if="currentGame && indexOpen" id="replay-index" class="replay-index" aria-label="回放定位" @keydown.esc="closeIndex">
        <header><strong>回放定位</strong><button aria-label="关闭回放定位" @click="closeIndex">关闭</button></header>
        <div class="replay-index-filters">
          <label>回合<select v-model="indexRound" @change="indexPage = 0"><option value="">全部回合</option><option v-for="round in indexedRounds" :key="round" :value="round">第{{ round }}回合</option></select></label>
          <label>事件<select v-model="indexCategory" @change="indexPage = 0"><option value="">全部事件</option><option value="action">卡牌与进攻</option><option value="cost">费用支付</option><option value="effect">效果处理</option><option value="disaster">天灾</option><option value="result">胜负</option></select></label>
          <label class="replay-index-search">卡名或事件<input v-model="indexSearch" placeholder="搜索已显示的事件" @input="indexPage = 0" /></label>
        </div>
        <div class="replay-index-rounds" aria-label="回合起点">
          <button v-for="item in visibleRoundStarts" :key="item.id" :disabled="loadingReplayPage || replayPresentationBusy" :data-step="item.step" @click="seekIndex(item.step)">第{{ item.round }}回合 · {{ isAdminReplay ? (item.activePlayer === 0 ? '下方' : '上方') : (item.activePlayer === (detail?.viewerPlayerIndex ?? 0) ? '我方' : '对方') }}回合开始</button>
        </div>
        <div class="replay-index-events">
          <p v-if="!visibleIndexEvents.length">没有符合条件的事件</p>
          <button v-for="item in visibleIndexEvents" :key="item.id" :disabled="loadingReplayPage || replayPresentationBusy" :data-step="item.step" @click="seekIndex(item.step)"><span>{{ item.label }}</span><small>{{ item.round ? `第${item.round}回合 · ` : '' }}步骤 {{ item.step + 1 }}</small></button>
        </div>
        <footer>
          <span>{{ filteredIndexEvents.length }}个事件</span>
          <button :disabled="indexPage === 0" @click="indexPage--">前页</button>
          <button :disabled="(indexPage + 1) * indexPageSize >= filteredIndexEvents.length" @click="indexPage++">后页</button>
          <button v-if="isAdminReplay && replayNextCursor" :disabled="loadingReplayPage" @click="loadMoreIndex">{{ loadingReplayPage ? '加载中' : '继续加载索引' }}</button>
        </footer>
        <p v-if="isAdminReplay && replayNextCursor" class="replay-index-note">已索引 {{ detail?.commands.length }} / {{ totalSteps }} 步</p>
        <p v-if="indexError" role="alert" class="replay-index-note">{{ indexError }}，可重试加载</p>
      </section>

      <main v-if="loading || error" class="replay-loading">
        <p>{{ loading ? '正在加载回放…' : error }}</p>
        <button v-if="error" @click="returnFromReplay">{{ returnLabel }}</button>
      </main>
    </Teleport>

    <Teleport :to="landscapeTeleportTarget()">
      <div v-if="settingsOpen" class="replay-settings-mask" @click.self="closeReplaySettings">
        <L12SettingsModal @close="closeReplaySettings" @feedback="openReplayFeedback"/>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.replay-page{background:#050809}
.replay-mobile-blocked{position:fixed;inset:0;display:grid;place-content:center;justify-items:center;gap:14px;background:radial-gradient(circle,rgba(28,70,74,.28),transparent 40%),#050809;color:#e7e4da;font-weight:900}.replay-mobile-blocked p{margin:0;font-size:18px}.replay-mobile-blocked button{padding:8px 12px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-size:14px;font-weight:900}
.replay-route-controls{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);top:12px;right:14px;display:flex;align-items:center;gap:9px;padding:6px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-route-controls span{max-width:310px;overflow:hidden;padding:0 7px;color:#aeb8b7;font-size:14px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}
.replay-route-controls button,.replay-controls button,.replay-loading button{padding:8px 12px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-size:14px;font-weight:900}
.replay-route-controls button:hover,.replay-controls button:hover:not(:disabled),.replay-loading button:hover{border-color:#d7c06f;color:#f4dda0}
.replay-catalog-warning{position:fixed;z-index:3190;top:64px;right:14px;max-width:min(420px,calc(100vw - 28px));margin:0;padding:8px 11px;border:1px solid #8b7540;background:#171308ed;color:#dccb91;font-size:14px;font-weight:800;line-height:1.5;box-shadow:0 8px 24px #000}
.replay-result{position:fixed;z-index:3200;left:50%;bottom:15px;display:flex;align-items:center;gap:12px;min-width:310px;padding:9px 13px;border:1px solid #b79c4e;background:#080d11f2;box-shadow:0 8px 24px #000;transform:translateX(-50%)}
.replay-result>strong{padding-right:10px;border-right:1px solid #49545a;color:#d9c16f}.replay-result span{display:flex;min-width:100px;justify-content:space-between;gap:10px;color:#e7e4da;white-space:nowrap}.replay-result em{font-style:normal;font-weight:900}.replay-result span[data-result="胜"] em{color:#8fd9b1}.replay-result span[data-result="负"] em,.replay-result[data-state="invalid"] em{color:#d99199}.replay-result[data-state="draw"] em{color:#c5b76e}
.replay-controls{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);left:14px;bottom:14px;display:flex;align-items:center;gap:7px;padding:7px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-controls .play{min-width:64px;border-color:#b79c4e;background:#2c2612;color:#f4dda0}
.replay-controls .speed{min-width:38px;padding-inline:8px;color:#8f9a9c}.replay-controls .speed.active{border-color:#d7c06f;background:#443816;color:#f4dda0}
.replay-controls button:disabled{cursor:not-allowed;opacity:.35}
.replay-controls{max-width:calc(100vw - 28px);box-sizing:border-box;flex-wrap:wrap}
.replay-index{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);left:14px;bottom:112px;width:min(460px,calc(100vw - 28px));max-height:calc(100dvh - 190px);box-sizing:border-box;display:flex;flex-direction:column;gap:10px;padding:12px;border:1px solid #667276;background:#080d11fa;color:#e7e4da;box-shadow:0 8px 24px #000;font-size:14px}
.replay-index header,.replay-index footer{display:flex;align-items:center;gap:8px;flex:none}.replay-index header{justify-content:space-between}.replay-index footer{flex-wrap:wrap}.replay-index button{min-height:36px;padding:6px 9px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-weight:800}.replay-index button:hover:not(:disabled),.replay-index button:focus-visible{border-color:#d7c06f}.replay-index button:disabled{opacity:.4;cursor:not-allowed}
.replay-index-filters{display:grid;grid-template-columns:1fr 1fr;gap:8px;flex:none}.replay-index label{display:grid;gap:4px;min-width:0;color:#adb8b7}.replay-index select,.replay-index input{width:100%;min-width:0;box-sizing:border-box;min-height:36px;padding:5px;border:1px solid #667276;background:#11191c;color:#f1eee6;font:inherit}.replay-index select option{background:#11191c;color:#f1eee6}.replay-index-search{grid-column:1/-1}
.replay-index-rounds{display:flex;flex-wrap:wrap;gap:6px;flex:none}.replay-index-events{min-height:0;overflow:auto;display:grid;gap:6px;overscroll-behavior:contain}.replay-index-events button{display:grid;gap:5px;text-align:left;overflow-wrap:anywhere}.replay-index-events small,.replay-index-note{color:#adb8b7;font-size:12px}.replay-index-note,.replay-index-events p{margin:0}.replay-result{bottom:80px}
.replay-index-events button{height:auto;min-height:58px;align-content:center}
.replay-controls small{min-width:92px;padding:0 6px;color:#919b98;font-size:14px;text-align:center}
.replay-settings-mask{position:fixed;z-index:4000;inset:0;display:grid;place-items:center;padding:18px;background:#010407c9;backdrop-filter:blur(8px)}
.replay-loading{position:fixed;z-index:3300;inset:0;display:grid;place-content:center;justify-items:center;gap:14px;background:radial-gradient(circle,rgba(28,70,74,.28),transparent 40%),#050809;color:#e7e4da;font-weight:900}
@media(max-width:760px){.replay-result{top:58px;bottom:auto;min-width:0}.replay-result>strong{display:none}.replay-route-controls span{display:none}.replay-controls{right:14px;justify-content:center}.replay-controls small{position:absolute;right:0;bottom:100%;padding:5px 7px;background:#080d11ed}}
</style>
