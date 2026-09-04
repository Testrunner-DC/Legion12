<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { platformRequest } from './platform'
import {
  consumeImportedReplay, exportReplayPayload, parseReplayPayload, rememberImportedReplay,
  type MatchDetail, type MatchSummary,
} from './replayModel'

const router = useRouter()
const route = useRoute()
const matches = ref<MatchSummary[]>([])
const selected = ref<MatchSummary | null>(null)
const imported = ref<MatchDetail | null>(null)
const loading = ref(false)
const error = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const selectedSummary = computed(() => imported.value?.match ?? selected.value)

onMounted(async () => {
  await loadMatches()
  if (route.query.source === 'json') imported.value = consumeImportedReplay()
  else {
    const selectedId = String(route.query.selected ?? '')
    selected.value = matches.value.find(match => match.matchId === selectedId) ?? null
  }
})

async function loadMatches() {
  loading.value = true
  error.value = ''
  try {
    matches.value = await platformRequest<MatchSummary[]>('/api/matches?limit=100')
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取对局记录失败'
  } finally { loading.value = false }
}

function selectMatch(match: MatchSummary) {
  selected.value = match
  imported.value = null
  error.value = ''
}

function playSelected() {
  if (imported.value) {
    rememberImportedReplay(imported.value)
    router.push({ name: 'json-replay' })
    return
  }
  if (selected.value) router.push({ name: 'match-replay', params: { matchId: selected.value.matchId } })
}

async function resolveSelectedDetail() {
  if (imported.value) return imported.value
  if (!selected.value) return null
  return platformRequest<MatchDetail>(`/api/matches/${encodeURIComponent(selected.value.matchId)}`)
}

async function exportReplay() {
  error.value = ''
  try {
    const detail = await resolveSelectedDetail()
    if (!detail) return
    const blob = new Blob([JSON.stringify(exportReplayPayload(detail), null, 2)], { type: 'application/json' })
    const anchor = document.createElement('a')
    anchor.href = URL.createObjectURL(blob)
    anchor.download = `${detail.match.matchId}.l12-replay.json`
    anchor.click()
    window.setTimeout(() => URL.revokeObjectURL(anchor.href), 1000)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '保存回放失败'
  }
}

async function importReplay(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  error.value = ''
  try {
    imported.value = parseReplayPayload(JSON.parse(await file.text()))
    selected.value = null
  } catch (reason) {
    imported.value = null
    error.value = reason instanceof Error ? reason.message : '回放导入失败'
  } finally { input.value = '' }
}

function dateLabel(raw: string) {
  const date = new Date(raw)
  if (Number.isNaN(date.getTime())) return '时间未知'
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
  }).format(date)
}

function resultLabel(match: MatchSummary) {
  if (!match.endedUtc) return '进行中'
  if (match.winner === null || match.winner === undefined) return '已结束'
  return `${match.winner === 0 ? match.player0 : match.player1} 胜`
}
</script>

<template>
  <section class="match-records grand-panel">
    <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
    <header class="records-header">
      <div><p class="kicker">MATCH RECORDS · SQLITE / JSON</p><h1>对局记录</h1></div>
      <div class="record-file-actions">
        <input ref="fileInput" type="file" accept="application/json,.json" @change="importReplay"/>
        <button @click="fileInput?.click()">打开 JSON 回放</button>
        <button :disabled="!selectedSummary" @click="exportReplay">保存 JSON</button>
        <button @click="loadMatches">刷新记录</button>
      </div>
    </header>
    <p v-if="error" class="records-error">{{ error }}</p>
    <div class="records-workspace">
      <aside class="records-list">
        <button v-if="imported" class="selected imported-record" @click="selected = null">
          <span><b>{{ imported.match.player0 }}</b><em>VS</em><b>{{ imported.match.player1 }}</b></span>
          <small>本地 JSON · {{ imported.commands.length }} 个步骤</small>
          <i>待播放</i>
        </button>
        <button v-for="match in matches" :key="match.matchId"
          :class="{ selected: !imported && selected?.matchId === match.matchId }" @click="selectMatch(match)">
          <span><b>{{ match.player0 }}</b><em>VS</em><b>{{ match.player1 }}</b></span>
          <small>{{ dateLabel(match.startedUtc) }} · {{ match.commandCount }} 次操作</small>
          <i>{{ resultLabel(match) }}</i>
        </button>
        <p v-if="!loading && !matches.length && !imported">尚无已记录对局。</p>
      </aside>

      <main v-if="selectedSummary" class="record-detail">
        <header>
          <div>
            <small>{{ imported ? 'LOCAL JSON REPLAY' : `ROOM ${selectedSummary.roomCode}` }}</small>
            <h2>{{ selectedSummary.player0 }} <em>VS</em> {{ selectedSummary.player1 }}</h2>
            <p>{{ selectedSummary.deck0 }} · {{ selectedSummary.deck1 }}</p>
          </div>
          <code>{{ selectedSummary.matchId.slice(0, 12) }}</code>
        </header>
        <section class="record-launch">
          <div>
            <span>{{ dateLabel(selectedSummary.startedUtc) }}</span>
            <b>{{ resultLabel(selectedSummary) }}</b>
            <small>{{ imported?.commands.length ?? selectedSummary.commandCount }} 个回放步骤</small>
          </div>
          <p>回放将在独立的完整对战界面中打开。进入播放器前不会加载或渲染棋盘。</p>
          <button class="primary" :disabled="!imported && !selectedSummary.endedUtc" @click="playSelected">播放回放</button>
        </section>
      </main>
      <div v-else class="records-placeholder">{{ loading ? '正在读取对局记录…' : '选择一场对局，或打开 JSON 回放' }}</div>
    </div>
  </section>
</template>

<style scoped>
.record-file-actions{display:flex;align-items:center;gap:8px}.record-file-actions input{display:none}
.record-launch{display:grid;min-height:360px;place-items:center;align-content:center;gap:24px;border:1px solid rgba(240,239,229,.16);background:radial-gradient(circle at 50% 42%,rgba(41,117,123,.13),transparent 45%),rgba(4,7,8,.48);text-align:center}
.record-launch>div{display:flex;align-items:center;justify-content:center;gap:14px}.record-launch span,.record-launch small{color:#78817d;font-size:10px}.record-launch b{color:#ece9df;font-size:15px}.record-launch p{max-width:520px;margin:0;color:#8f9793;font-size:11px;line-height:1.8}.record-launch button{padding:13px 32px;border:1px solid #d7c06f;background:#2c2612;color:#f4dda0;font-weight:900;letter-spacing:.12em}.record-launch button:disabled{cursor:not-allowed;opacity:.35}.imported-record{border-color:#d7c06f!important}
@media(max-width:720px){.records-header{align-items:flex-start;gap:12px}.record-file-actions{flex-wrap:wrap}.record-launch>div{flex-direction:column;gap:6px}}
</style>
