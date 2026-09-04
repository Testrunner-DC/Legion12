<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import GameBoard from './game/GameBoard.vue'
import { platformRequest } from './platform'
import { consumeImportedReplay, replayGameAt, type MatchDetail } from './replayModel'

const route = useRoute()
const router = useRouter()
const detail = ref<MatchDetail | null>(null)
const selectedStep = ref(0)
const playing = ref(false)
const loading = ref(true)
const error = ref('')
let timer: ReturnType<typeof setInterval> | null = null

const currentGame = computed(() => detail.value ? replayGameAt(detail.value, selectedStep.value) : null)
const totalSteps = computed(() => detail.value?.commands.length ?? 0)
const atFirst = computed(() => selectedStep.value <= 0)
const atLast = computed(() => selectedStep.value >= totalSteps.value - 1)

onMounted(loadReplay)
onBeforeUnmount(stop)

async function loadReplay() {
  loading.value = true
  error.value = ''
  try {
    if (route.name === 'json-replay') detail.value = consumeImportedReplay()
    else {
      const matchId = String(route.params.matchId ?? '')
      if (matchId) detail.value = await platformRequest<MatchDetail>(`/api/matches/${encodeURIComponent(matchId)}`)
    }
    if (!detail.value) throw new Error('未找到可播放的回放，请返回对局记录重新选择')
    if (!detail.value.commands.length) throw new Error('这场对局没有可播放的状态快照')
    selectedStep.value = 0
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取回放失败'
  } finally { loading.value = false }
}

function stop() {
  playing.value = false
  if (timer) window.clearInterval(timer)
  timer = null
}

function previous() {
  stop()
  selectedStep.value = Math.max(0, selectedStep.value - 1)
}

function next() {
  stop()
  selectedStep.value = Math.min(totalSteps.value - 1, selectedStep.value + 1)
}

function toggle() {
  if (!detail.value?.commands.length) return
  if (playing.value) return stop()
  if (atLast.value) selectedStep.value = 0
  playing.value = true
  timer = window.setInterval(() => {
    if (atLast.value) return stop()
    selectedStep.value += 1
  }, 900)
}

function returnToRecords() {
  stop()
  router.push({
    name: 'records',
    query: route.name === 'json-replay' ? { source: 'json' } : { selected: detail.value?.match.matchId },
  })
}
</script>

<template>
  <div class="game-page replay-page">
    <GameBoard v-if="currentGame" :game="currentGame" read-only />

    <div class="replay-route-controls">
      <span v-if="detail">{{ detail.match.player0 }} VS {{ detail.match.player1 }}</span>
      <button @click="returnToRecords">返回对局记录</button>
    </div>

    <div v-if="currentGame" class="replay-controls" aria-label="回放控制">
      <button :disabled="atFirst" @click="previous">上一步</button>
      <button class="play" @click="toggle">{{ playing ? '暂停' : '播放' }}</button>
      <button :disabled="atLast" @click="next">下一步</button>
      <small>步骤 {{ selectedStep + 1 }} / {{ totalSteps }}</small>
    </div>

    <main v-if="loading || error" class="replay-loading">
      <p>{{ loading ? '正在加载回放…' : error }}</p>
      <button v-if="error" @click="returnToRecords">返回对局记录</button>
    </main>
  </div>
</template>

<style scoped>
.replay-page{background:#050809}
.replay-route-controls{position:fixed;z-index:3200;top:12px;right:14px;display:flex;align-items:center;gap:9px;padding:6px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-route-controls span{max-width:310px;overflow:hidden;padding:0 7px;color:#aeb8b7;font-size:9px;font-weight:900;text-overflow:ellipsis;white-space:nowrap}
.replay-route-controls button,.replay-controls button,.replay-loading button{padding:8px 12px;border:1px solid #667276;background:#11191c;color:#f1eee6;font-size:10px;font-weight:900}
.replay-route-controls button:hover,.replay-controls button:hover:not(:disabled),.replay-loading button:hover{border-color:#d7c06f;color:#f4dda0}
.replay-controls{position:fixed;z-index:3200;left:14px;bottom:14px;display:flex;align-items:center;gap:7px;padding:7px;border:1px solid #445057;background:#080d11ed;box-shadow:0 8px 24px #000}
.replay-controls .play{min-width:64px;border-color:#b79c4e;background:#2c2612;color:#f4dda0}
.replay-controls button:disabled{cursor:not-allowed;opacity:.35}
.replay-controls small{min-width:92px;padding:0 6px;color:#919b98;font-size:9px;text-align:center}
.replay-loading{position:fixed;z-index:3300;inset:0;display:grid;place-content:center;justify-items:center;gap:14px;background:radial-gradient(circle,rgba(28,70,74,.28),transparent 40%),#050809;color:#e7e4da;font-weight:900}
@media(max-width:640px){.replay-route-controls span{display:none}.replay-controls{right:14px;justify-content:center}.replay-controls small{position:absolute;right:0;bottom:100%;padding:5px 7px;background:#080d11ed}}
</style>
