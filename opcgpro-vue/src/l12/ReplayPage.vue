<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import L12SettingsModal from './site/L12SettingsModal.vue'
import { closeSettingsAndRestore, openBugFeedbackFromSettings, rememberSettingsOpener } from './site/bugFeedbackEntry'
import { loadDeckCatalog, type DeckCard } from './decks'
import { adminApi, PlatformRequestError, platformState } from './platform'
import { adminReplayDetail, consumeImportedReplay, replayFocusCardAt, replayGameAt, type MatchDetail } from './replayModel'
import { isMobileDeviceExperience, landscapeTeleportTarget } from './mobileViewport'
import { buildReplayIndex, createReplayIndexAccumulator, type ReplayIndexCategory } from './replayIndex'
import { ReplayArchiveLoader } from './replayArchiveLoader'
import { createPlayerReplayReader } from './playerReplayArchive'
import { recordedMatchOutcome } from './replayOutcome'

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
const archive = shallowRef<ReplayArchiveLoader | null>(null)
const archiveVersion = ref(0)
const replayBuffering = ref(false)
const indexOpen = ref(false)
const indexRound = ref<number | ''>('')
const indexCategory = ref<ReplayIndexCategory | ''>('')
const indexSearch = ref('')
const indexPage = ref(0)
const indexError = ref('')
const settingsOpen = ref(false)
const settingsOpener = ref<HTMLElement | null>(null)
const indexPageSize = 40
const mobileReplayBlocked = isMobileDeviceExperience()
let timer: ReturnType<typeof setTimeout> | null = null
let playbackGeneration = 0
let readGeneration = 0
let initialReadController: AbortController | null = null
let blockedStep: number | null = null
let disposed = false
let indexBuilder = createReplayIndexAccumulator(null, true)
const replayPresentationBusy = ref(false)
const isAdminReplay = computed(() => route.name === 'admin-match-replay')
const loadingReplayPage = computed(() => { archiveVersion.value; return Boolean(archive.value?.loading) })
const replayIssue = computed(() => { archiveVersion.value; return archive.value?.issue ?? null })
const replayNextCursor = computed(() => { archiveVersion.value; return archive.value?.hasMore ?? false })
const indexedSteps = computed(() => { archiveVersion.value; return archive.value?.indexedSteps ?? detail.value?.commands.length ?? 0 })
const replayWindow = computed(() => {
  archiveVersion.value
  if (!detail.value) return null
  if (!archive.value) return { detail: detail.value, step: selectedStep.value, disasterBefore: undefined }
  const window = archive.value.window(selectedStep.value)
  return window ? { detail: { ...detail.value, commands: window.commands }, step: window.step,
    disasterBefore: window.disasterBefore } : null
})
const replayCatalog = computed(() => new Map(cards.value.map(card => [card.id, card])))
const currentGame = computed(() => {
  const window = replayWindow.value
  const game = window ? replayGameAt(window.detail, window.step, replayCatalog.value, window.disasterBefore) : null
  if (!game || !isAdminReplay.value) return game
  for (const player of game.players)
    for (const card of player.field.flat())
      if (card?.hidden && card.cardId && card.cardId !== 'hidden-card') card.identityKnown = true
  return game
})
const replayFocusCard = computed(() => {
  const window = replayWindow.value
  return window ? replayFocusCardAt(window.detail, window.step, cards.value, window.disasterBefore) : null
})
const replayIndex = computed(() => {
  archiveVersion.value
  if (archive.value) return { rounds: [...indexBuilder.index.rounds], events: [...indexBuilder.index.events] }
  return detail.value ? buildReplayIndex(detail.value, isAdminReplay.value) : { rounds: [], events: [] }
})
const indexedRounds = computed(() => [...new Set(replayIndex.value.rounds.map(item => item.round))])
const filteredIndexEvents = computed(() => replayIndex.value.events.filter(item =>
  (indexRound.value === '' || item.round === indexRound.value)
  && (!indexCategory.value || item.category === indexCategory.value)
  && (!indexSearch.value.trim() || item.label.includes(indexSearch.value.trim()))))
const visibleIndexEvents = computed(() => filteredIndexEvents.value.slice(indexPage.value * indexPageSize, (indexPage.value + 1) * indexPageSize))
const visibleRoundStarts = computed(() => replayIndex.value.rounds.filter(item =>
  item.round === (indexRound.value || currentGame.value?.round)).slice(0, 4))
const totalSteps = computed(() => { archiveVersion.value; return archive.value?.total ?? detail.value?.commands.length ?? 0 })
const atFirst = computed(() => selectedStep.value <= 0)
const atLast = computed(() => selectedStep.value >= totalSteps.value - 1)
const returnLabel = computed(() => isAdminReplay.value ? '返回后台对局档案' : '返回对局记录')
const replayResult = computed(() => {
  if (!detail.value || !currentGame.value || !atLast.value) return null
  const window = replayWindow.value
  const raw = window?.detail.commands[window.step]?.state
  const outcome = recordedMatchOutcome(detail.value.match, raw)
  if (outcome.kind === 'ongoing') return null
  return { state: outcome.kind,
    players: [0, 1].map(index => ({ name: index === 0 ? detail.value!.match.player0 : detail.value!.match.player1,
      result: outcome.kind === 'decided' ? outcome.winner === index ? '胜' : '负'
        : outcome.kind === 'invalid' ? '无效' : outcome.kind === 'draw' ? '平' : '无法确认' })) }
})
function isCurrentRead(generation: number) { return !disposed && generation === readGeneration }
function clearReadSession() {
  readGeneration++
  initialReadController?.abort(); initialReadController = null
  archive.value?.dispose(); archive.value = null
  indexBuilder = createReplayIndexAccumulator(null, true)
  detail.value = null; indexError.value = ''; blockedStep = null; replayBuffering.value = false; loading.value = false
  archiveVersion.value++
  stop()
}
watch(() => [platformState.token, platformState.account?.id, platformState.account?.role,
  platformState.account?.permissionVersion, platformState.account?.disabled] as const,
() => { clearReadSession(); error.value = '账号或权限已变化，请返回重新打开回放' }, { flush: 'sync' })
watch(() => [route.name, route.params.matchId] as const,
() => { clearReadSession(); error.value = '回放入口已变化，请重新打开' }, { flush: 'sync' })
watch(selectedStep, step => { archive.value?.focus(step); archive.value?.prefetch(step) })
onMounted(() => {
  window.addEventListener('online', resumeAfterReadFailure)
  if (!mobileReplayBlocked) void loadReplay()
})
onBeforeUnmount(() => {
  disposed = true; clearReadSession()
  window.removeEventListener('online', resumeAfterReadFailure)
})
async function loadReplay() {
  clearReadSession()
  const generation = readGeneration
  const controller = new AbortController()
  initialReadController = controller
  loading.value = true; error.value = ''; catalogWarning.value = ''
  try {
    try {
      const catalog = await loadDeckCatalog()
      if (!isCurrentRead(generation)) return
      cards.value = catalog
    } catch {
      if (!isCurrentRead(generation)) return
      cards.value = []
      catalogWarning.value = '卡牌资料暂不可用，回放仍可播放；部分主宰与阵营效果信息可能不完整。'
    }
    if (route.name === 'json-replay') detail.value = consumeImportedReplay()
    else if (isAdminReplay.value) {
      const matchId = String(route.params.matchId ?? '')
      if (matchId) {
        const [summary, firstPage] = await Promise.all([adminApi.match(matchId), adminApi.replayPage(matchId, undefined, 50, controller.signal)])
        if (!isCurrentRead(generation)) return
        detail.value = { ...adminReplayDetail({ ...summary, replay: firstPage.items }), commands: [] }
        detail.value.match.commandCount = firstPage.totalCommands
        indexBuilder = createReplayIndexAccumulator(detail.value.viewerPlayerIndex, true)
        const loader = new ReplayArchiveLoader((cursor, signal) => adminApi.replayPage(matchId, cursor, 50, signal), {
          matchId,
          onPage: (commands, start) => indexBuilder.append(commands, start),
          onChange: () => {
            if (!isCurrentRead(generation)) return
            if (loader.issue?.denied) {
              detail.value = null; indexBuilder = createReplayIndexAccumulator(null, true)
              error.value = '账号或权限已失效，请返回重新打开回放'; stop()
            }
            if (!loader.issue) indexError.value = ''
            archiveVersion.value++
          },
        })
        archive.value = loader
        loader.seed(firstPage)
      }
    } else if (route.name === 'match-replay') {
      const matchId = String(route.params.matchId ?? '')
      if (matchId) {
        const read = createPlayerReplayReader(matchId)
        const firstPage = await read(undefined, controller.signal)
        if (!isCurrentRead(generation)) return
        detail.value = { match: firstPage.match, commands: [], viewerPlayerIndex: firstPage.viewerPlayerIndex }
        indexBuilder = createReplayIndexAccumulator(firstPage.viewerPlayerIndex, false)
        const loader = new ReplayArchiveLoader(read, {
          matchId,
          onPage: (commands, start) => indexBuilder.append(commands, start),
          onChange: () => {
            if (!isCurrentRead(generation)) return
            if (loader.issue?.denied) {
              detail.value = null; indexBuilder = createReplayIndexAccumulator(null, false)
              error.value = '账号或权限已失效，请返回重新打开回放'; stop()
            }
            if (!loader.issue) indexError.value = ''
            archiveVersion.value++
          },
        })
        archive.value = loader
        loader.seed(firstPage)
      }
    }
    if (!isCurrentRead(generation)) return
    if (!detail.value) throw new Error('未找到可播放的回放，请返回对局记录重新选择')
    if (!totalSteps.value) throw new Error('这场对局没有可播放的状态快照')
    selectedStep.value = 0
    archive.value?.prefetch(0)
  } catch (reason) {
    if (!isCurrentRead(generation)) return
    error.value = reason instanceof PlatformRequestError && reason.status === 410 && reason.code === 'sandbox_replay_expired'
      ? '回放已过期' : reason instanceof Error ? reason.message : '读取回放失败'
  } finally {
    if (isCurrentRead(generation)) { loading.value = false; initialReadController = null }
  }
}
function clearPlaybackTimer() {
  if (timer) window.clearTimeout(timer)
  timer = null
}
function stop() { playing.value = false; playbackGeneration++; clearPlaybackTimer(); replayBuffering.value = false; blockedStep = null }
function selectStep(step: number) { selectedStep.value = step; archive.value?.focus(step); archive.value?.prefetch(step) }
async function ensureReplayStepLoaded(index: number, retry = false) {
  const loader = archive.value
  if (!loader) return Boolean(detail.value?.commands[index])
  const generation = readGeneration
  const intent = playbackGeneration
  if (!loader.commandAt(index)) { blockedStep = index; replayBuffering.value = true }
  const loaded = await loader.ensure(index, retry)
  if (!isCurrentRead(generation) || archive.value !== loader || intent !== playbackGeneration) return false
  if (loaded) { blockedStep = null; replayBuffering.value = false }
  else if (loader.issue?.terminal) { replayBuffering.value = false; stop() }
  return loaded
}
async function previous() {
  stop()
  const target = Math.max(0, selectedStep.value - 1)
  const intent = playbackGeneration
  if (await ensureReplayStepLoaded(target) && intent === playbackGeneration) selectStep(target)
}
async function next() {
  stop()
  const target = Math.min(totalSteps.value - 1, selectedStep.value + 1)
  const intent = playbackGeneration
  if (await ensureReplayStepLoaded(target) && intent === playbackGeneration) selectStep(target)
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
async function seekIndex(step: number) {
  if (replayPresentationBusy.value || step < 0 || step >= indexedSteps.value) return
  stop()
  const intent = playbackGeneration
  if (await ensureReplayStepLoaded(step) && intent === playbackGeneration) { selectStep(step); closeIndex() }
}
async function loadMoreIndex() {
  if (!archive.value || loadingReplayPage.value) return
  stop(); indexError.value = ''
  const generation = readGeneration
  const loaded = await archive.value.loadMore(true)
  if (isCurrentRead(generation) && !loaded) indexError.value = replayIssue.value?.message ?? '暂时无法读取索引'
}
function toggle() {
  if (!currentGame.value || replayIssue.value?.terminal) return
  if (playing.value) return stop()
  playing.value = true
  if (atLast.value) {
    void ensureReplayStepLoaded(0).then(loaded => {
      if (loaded && playing.value) { selectStep(0); startPlaybackTimer() }
    })
  } else startPlaybackTimer()
  archive.value?.prefetch(selectedStep.value)
}
function startPlaybackTimer() {
  clearPlaybackTimer()
  schedulePlaybackAdvance(++playbackGeneration)
}
function schedulePlaybackAdvance(generation: number) {
  if (!playing.value || generation !== playbackGeneration) return
  timer = window.setTimeout(() => void advancePlayback(generation), Math.round(3000 / playbackSpeed.value))
}
async function advancePlayback(generation: number) {
  timer = null
  if (!playing.value || generation !== playbackGeneration) return
  if (replayPresentationBusy.value) {
    timer = window.setTimeout(() => void advancePlayback(generation), 40); return
  }
  if (atLast.value) return stop()
  const target = selectedStep.value + 1
  if (!await ensureReplayStepLoaded(target)) return
  if (!playing.value || generation !== playbackGeneration) return
  selectStep(target)
  if (atLast.value) return stop()
  schedulePlaybackAdvance(generation)
}
async function resumeAfterReadFailure(event?: Event) {
  if (disposed) return
  if (!archive.value) { if (error.value) await loadReplay(); return }
  if (replayIssue.value?.terminal || (!replayIssue.value && !replayBuffering.value)) return
  const automatic = event?.type === 'online'
  const generation = playbackGeneration
  const target = blockedStep ?? selectedStep.value
  if (await ensureReplayStepLoaded(target, true)) {
    if (generation !== playbackGeneration) return
    if (!automatic || playing.value) selectStep(target)
    if (playing.value) startPlaybackTimer()
  }
}
function setPlaybackSpeed(speed: 1 | 2 | 3) {
  if (playbackSpeed.value === speed) return
  playbackSpeed.value = speed
  // The in-flight step owns playbackGeneration until it settles. Changing speed
  // only affects its next delay; it must not retire that requested frame.
  if (playing.value && !replayBuffering.value) startPlaybackTimer()
}
function returnFromReplay() {
  stop()
  if (isAdminReplay.value) {
    router.push({ name: 'admin', query: { ...route.query, section: 'matches', matchId: detail.value?.match.matchId } }); return
  }
  router.push(route.name === 'json-replay' ? { name: 'records' }
    : { name: 'records', query: { selected: detail.value?.match.matchId } })
}
function openReplaySettings(event?: Event) { settingsOpener.value = rememberSettingsOpener(event); settingsOpen.value = true }
function closeReplaySettings() { void closeSettingsAndRestore(() => { settingsOpen.value = false }, settingsOpener.value) }
function openReplayFeedback() { void openBugFeedbackFromSettings(() => { settingsOpen.value = false }, settingsOpener.value) }
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
        <strong>{{ replayResult.state === 'unknown' ? '结果无法确认' : '对局结束' }}</strong>
        <span v-for="(player, index) in replayResult.players" :key="index" :data-result="player.result"><b>{{ player.name }}</b><em>{{ player.result }}</em></span>
      </div>

      <div v-if="currentGame" class="replay-controls" aria-label="回放控制">
        <button :disabled="atFirst || replayPresentationBusy" @click="previous">上一步</button>
        <button class="play" @click="toggle">{{ playing ? '暂停' : '播放' }}</button>
        <button v-for="speed in ([1, 2, 3] as const)" :key="speed" class="speed" :class="{ active: playbackSpeed === speed }" :aria-pressed="playbackSpeed === speed" @click="setPlaybackSpeed(speed)">{{ speed.toFixed(1) }}</button>
        <button :disabled="atLast || replayPresentationBusy || Boolean(replayIssue?.terminal)" @click="next">{{ replayBuffering ? '缓冲中' : '下一步' }}</button>
        <button :aria-expanded="indexOpen" aria-controls="replay-index" @click="openIndex">回放定位</button>
        <small>步骤 {{ selectedStep + 1 }} / {{ totalSteps }}<template v-if="archive"> · 分页</template></small>
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
          <button v-if="archive && replayNextCursor" :disabled="loadingReplayPage" @click="loadMoreIndex">{{ loadingReplayPage ? '加载中' : '继续加载索引' }}</button>
        </footer>
        <p v-if="archive && replayNextCursor" class="replay-index-note">已索引 {{ indexedSteps }} / {{ totalSteps }} 步</p>
        <p v-if="indexError" role="alert" class="replay-index-note">{{ indexError }}，可重试加载</p>
      </section>

      <div v-if="currentGame && (replayBuffering || replayIssue)" class="replay-read-status" role="status" aria-live="polite">
        <span>{{ replayIssue?.message || '正在缓冲后续回放，画面和进度已保留…' }}</span>
        <button v-if="replayIssue && !replayIssue.terminal" :disabled="loadingReplayPage" @click="resumeAfterReadFailure">{{ loadingReplayPage ? '重试中' : '重试继续' }}</button>
      </div>

      <main v-if="loading || error" class="replay-loading">
        <p>{{ loading ? '正在加载回放…' : error }}</p>
        <button v-if="error && !detail" @click="loadReplay">重新读取</button><button v-if="error" @click="returnFromReplay">{{ returnLabel }}</button>
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
.replay-mobile-blocked{position:fixed;inset:0;display:grid;place-content:center;justify-items:center;gap:14px;background:radial-gradient(circle,rgba(28,70,74,.28),transparent 40%),#050809;color:#e7e4da;font-weight:900}.replay-mobile-blocked p{margin:0;font-size:18px}.replay-mobile-blocked button{min-height:38px;padding:8px 12px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-size:14px;font-weight:900}
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
.replay-read-status{position:fixed;z-index:var(--l12-battle-fixed-controls-z,5100);bottom:76px;left:50%;transform:translateX(-50%);display:flex;align-items:center;gap:12px;max-width:calc(100vw - 32px);padding:10px 14px;background:#101a20ed;border:1px solid #6c6250;color:#f1eee6;font-size:14px;overflow-wrap:anywhere}
.replay-read-status button{flex-shrink:0;padding:6px 10px;border:1px solid #b59d60;background:#182126;color:#f4dda0}
</style>
