<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import { loadDeckCatalog, type DeckCard } from './decks'
import { adminApi, PlatformRequestError, platformRequest } from './platform'
import { adminReplayDetail, consumeImportedReplay, replayFocusCardAt, replayGameAt, type MatchDetail } from './replayModel'

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
let timer: ReturnType<typeof setInterval> | null = null

const replayCatalog = computed(() => new Map(cards.value.map(card => [card.id, card])))
const currentGame = computed(() => detail.value ? replayGameAt(detail.value, selectedStep.value, replayCatalog.value) : null)
const replayFocusCard = computed(() => detail.value ? replayFocusCardAt(detail.value, selectedStep.value, cards.value) : null)
const isAdminReplay = computed(() => route.name === 'admin-match-replay')
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

onMounted(loadReplay)
onBeforeUnmount(stop)

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
  if (timer) window.clearInterval(timer)
  timer = null
}

function stop() {
  playing.value = false
  clearPlaybackTimer()
}

function previous() {
  stop()
  selectedStep.value = Math.max(0, selectedStep.value - 1)
}

async function ensureReplayStepLoaded(index: number) {
  if (!isAdminReplay.value || !detail.value || index < detail.value.commands.length) return true
  if (!replayNextCursor.value || loadingReplayPage.value) return false
  loadingReplayPage.value = true
  try {
    const matchId = String(route.params.matchId ?? '')
    const page = await adminApi.replayPage(matchId, replayNextCursor.value)
    detail.value.commands.push(...page.items)
    replayNextCursor.value = page.nextCursor
    replayTotalCommands.value = page.totalCommands
    return index < detail.value.commands.length
  } catch (reason) {
    error.value = reason instanceof PlatformRequestError && reason.status === 410 && reason.code === 'sandbox_replay_expired'
      ? '回放已过期'
      : reason instanceof Error ? reason.message : '读取下一页回放失败'
    stop()
    return false
  } finally { loadingReplayPage.value = false }
}
async function next() {
  stop()
  const target = Math.min(totalSteps.value - 1, selectedStep.value + 1)
  if (await ensureReplayStepLoaded(target)) selectedStep.value = target
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
  const interval = 2700 / playbackSpeed.value
  timer = window.setInterval(() => void (async () => {
    if (atLast.value) return stop()
    const target = selectedStep.value + 1
    if (!await ensureReplayStepLoaded(target)) return
    selectedStep.value = target
    if (atLast.value) stop()
  })(), interval)
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
  router.push({
    name: 'records',
    query: route.name === 'json-replay' ? { source: 'json' } : { selected: detail.value?.match.matchId },
  })
}
</script>

<template>
  <div class="game-page replay-page">
    <GameBoard v-if="currentGame" :game="currentGame" :replay-focus-card="replayFocusCard" read-only />

    <div class="replay-route-controls">
      <span v-if="detail">{{ detail.match.player0 }} VS {{ detail.match.player1 }}</span>
      <button @click="returnFromReplay">{{ returnLabel }}</button>
    </div>

    <p v-if="catalogWarning" class="replay-catalog-warning" role="status">{{ catalogWarning }}</p>

    <div v-if="replayResult" class="replay-result" :data-state="replayResult.state" aria-live="polite">
      <strong>对局结束</strong>
      <span v-for="(player, index) in replayResult.players" :key="index" :data-result="player.result"><b>{{ player.name }}</b><em>{{ player.result }}</em></span>
    </div>

    <div v-if="currentGame" class="replay-controls" aria-label="回放控制">
      <button :disabled="atFirst" @click="previous">上一步</button>
      <button class="play" @click="toggle">{{ playing ? '暂停' : '播放' }}</button>
      <button v-for="speed in ([1, 2, 3] as const)" :key="speed" class="speed" :class="{ active: playbackSpeed === speed }" :aria-pressed="playbackSpeed === speed" @click="setPlaybackSpeed(speed)">{{ speed.toFixed(1) }}</button>
      <button :disabled="atLast || loadingReplayPage" @click="next">{{ loadingReplayPage ? '加载中' : '下一步' }}</button>
      <small>步骤 {{ selectedStep + 1 }} / {{ totalSteps }}<template v-if="isAdminReplay"> · 分页</template></small>
    </div>

    <main v-if="loading || error" class="replay-loading">
      <p>{{ loading ? '正在加载回放…' : error }}</p>
      <button v-if="error" @click="returnFromReplay">{{ returnLabel }}</button>
    </main>
  </div>
</template>

<style scoped>
.replay-page{background:#050809}
.replay-route-controls{position:fixed;z-index:3200;top:12px;right:14px;display:flex;align-items:center;gap:9px;padding:6px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-route-controls span{max-width:310px;overflow:hidden;padding:0 7px;color:#aeb8b7;font-size:14px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}
.replay-route-controls button,.replay-controls button,.replay-loading button{padding:8px 12px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-size:14px;font-weight:900}
.replay-route-controls button:hover,.replay-controls button:hover:not(:disabled),.replay-loading button:hover{border-color:#d7c06f;color:#f4dda0}
.replay-catalog-warning{position:fixed;z-index:3190;top:64px;right:14px;max-width:min(420px,calc(100vw - 28px));margin:0;padding:8px 11px;border:1px solid #8b7540;background:#171308ed;color:#dccb91;font-size:14px;font-weight:800;line-height:1.5;box-shadow:0 8px 24px #000}
.replay-result{position:fixed;z-index:3200;left:50%;bottom:15px;display:flex;align-items:center;gap:12px;min-width:310px;padding:9px 13px;border:1px solid #b79c4e;background:#080d11f2;box-shadow:0 8px 24px #000;transform:translateX(-50%)}
.replay-result>strong{padding-right:10px;border-right:1px solid #49545a;color:#d9c16f}.replay-result span{display:flex;min-width:100px;justify-content:space-between;gap:10px;color:#e7e4da;white-space:nowrap}.replay-result em{font-style:normal;font-weight:900}.replay-result span[data-result="胜"] em{color:#8fd9b1}.replay-result span[data-result="负"] em,.replay-result[data-state="invalid"] em{color:#d99199}.replay-result[data-state="draw"] em{color:#c5b76e}
.replay-controls{position:fixed;z-index:3200;left:14px;bottom:14px;display:flex;align-items:center;gap:7px;padding:7px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-controls .play{min-width:64px;border-color:#b79c4e;background:#2c2612;color:#f4dda0}
.replay-controls .speed{min-width:38px;padding-inline:8px;color:#8f9a9c}.replay-controls .speed.active{border-color:#d7c06f;background:#443816;color:#f4dda0}
.replay-controls button:disabled{cursor:not-allowed;opacity:.35}
.replay-controls small{min-width:92px;padding:0 6px;color:#919b98;font-size:14px;text-align:center}
.replay-loading{position:fixed;z-index:3300;inset:0;display:grid;place-content:center;justify-items:center;gap:14px;background:radial-gradient(circle,rgba(28,70,74,.28),transparent 40%),#050809;color:#e7e4da;font-weight:900}
@media(max-width:760px){.replay-result{top:58px;bottom:auto;min-width:0}.replay-result>strong{display:none}.replay-route-controls span{display:none}.replay-controls{right:14px;justify-content:center}.replay-controls small{position:absolute;right:0;bottom:100%;padding:5px 7px;background:#080d11ed}}
</style>
