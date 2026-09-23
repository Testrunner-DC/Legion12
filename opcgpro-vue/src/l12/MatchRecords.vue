<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { platformRequest } from './platform'
import {
  exportReplayPayload, parseReplayPayload, rememberImportedReplay, type MatchDetail, type MatchSummary,
} from './replayModel'
import { isMobileDeviceExperience } from './mobileViewport'

const router = useRouter()
const route = useRoute()
const matches = ref<MatchSummary[]>([])
const selected = ref<MatchSummary | null>(null)
const loading = ref(false)
const error = ref('')
const fileInput = ref<HTMLInputElement | null>(null)
const canUseSelectedReplay = computed(() => Boolean(selected.value?.endedUtc && selected.value.commandCount > 0))
const mobileReplayBlocked = isMobileDeviceExperience()

function blockMobileReplay() {
  error.value = '请到电脑端查看回放'
}

onMounted(async () => {
  await loadMatches()
  const selectedId = String(route.query.selected ?? '')
  selected.value = matches.value.find(match => match.matchId === selectedId) ?? null
})

async function loadMatches() {
  loading.value = true
  error.value = ''
  try {
    matches.value = await platformRequest<MatchSummary[]>('/api/matches?limit=10')
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取对局记录失败'
  } finally { loading.value = false }
}

function selectMatch(match: MatchSummary) {
  selected.value = match
  error.value = ''
}

function playSelected() {
  if (mobileReplayBlocked) return blockMobileReplay()
  if (selected.value?.endedUtc && selected.value.commandCount > 0)
    router.push({ name: 'match-replay', params: { matchId: selected.value.matchId } })
}

function openReplayImport() {
  if (mobileReplayBlocked) return blockMobileReplay()
  fileInput.value?.click()
}

async function resolveSelectedDetail() {
  if (!selected.value) return null
  return platformRequest<MatchDetail>(`/api/matches/${encodeURIComponent(selected.value.matchId)}`)
}

function replayFileDate(raw: string) {
  const date = new Date(raw)
  if (Number.isNaN(date.getTime())) return '日期未知'
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

async function exportReplay() {
  error.value = ''
  try {
    const detail = await resolveSelectedDetail()
    if (!detail) return
    const blob = new Blob([JSON.stringify(exportReplayPayload(detail))], { type: 'application/json' })
    const anchor = document.createElement('a')
    anchor.href = URL.createObjectURL(blob)
    anchor.download = `${replayFileDate(detail.match.startedUtc)}-${detail.match.matchId}.json`
    anchor.click()
    window.setTimeout(() => URL.revokeObjectURL(anchor.href), 1000)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '保存回放失败'
  }
}

async function importReplay(event: Event) {
  if (mobileReplayBlocked) {
    ;(event.target as HTMLInputElement).value = ''
    return blockMobileReplay()
  }
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  error.value = ''
  try {
    const detail = parseReplayPayload(JSON.parse(await file.text()))
    rememberImportedReplay(detail)
    await router.push({ name: 'json-replay' })
  } catch (reason) {
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

function durationLabel(match: MatchSummary) {
  if (!match.endedUtc) return '进行中'
  const duration = Math.max(0, new Date(match.endedUtc).getTime() - new Date(match.startedUtc).getTime())
  if (!Number.isFinite(duration)) return '时长未知'
  const seconds = Math.round(duration / 1000)
  const minutes = Math.floor(seconds / 60)
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`
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
      <div>
        <p class="kicker">PLAYER REPLAYS · 7 DAYS / RECENT 10 / JSON</p>
        <h1>对局回放</h1>
        <small class="records-retention-note">仅保存7天内最近10场回放，历史回放文件可能随版本更新失效。</small>
      </div>
      <div class="record-file-actions">
        <input ref="fileInput" type="file" accept="application/json,.json" @change="importReplay"/>
        <button @click="openReplayImport">打开 JSON 回放</button>
        <button :disabled="!canUseSelectedReplay" @click="exportReplay">保存 JSON</button>
        <button @click="loadMatches">刷新记录</button>
      </div>
    </header>
    <p v-if="error" class="records-error">{{ error }}</p>
    <div class="records-workspace">
      <aside class="records-list">
        <button v-for="match in matches" :key="match.matchId"
          :class="{ selected: selected?.matchId === match.matchId }" @click="selectMatch(match)">
          <span><b>{{ match.player0 }}</b><em>VS</em><b>{{ match.player1 }}</b></span>
          <small class="record-decks">{{ match.deck0 || '未命名牌库' }} · {{ match.deck1 || '未命名牌库' }}</small>
          <small>{{ dateLabel(match.startedUtc) }} · {{ durationLabel(match) }} · {{ match.commandCount }} 次操作</small>
          <i class="record-result">{{ resultLabel(match) }}</i>
        </button>
        <p v-if="!loading && !matches.length">尚无已记录对局。</p>
      </aside>

      <main v-if="selected" class="record-detail">
        <header>
          <div>
            <small>ROOM {{ selected.roomCode }}</small>
            <h2>{{ selected.player0 }} <em>VS</em> {{ selected.player1 }}</h2>
          </div>
          <code>{{ selected.matchId.slice(0, 12) }}</code>
        </header>
        <section class="record-launch">
          <div class="record-summary-grid">
            <span><small>对局结果</small><b>{{ resultLabel(selected) }}</b></span>
            <span><small>对局时长</small><b>{{ durationLabel(selected) }}</b></span>
            <span><small>开始时间</small><b>{{ dateLabel(selected.startedUtc) }}</b></span>
            <span><small>结束时间</small><b>{{ selected.endedUtc ? dateLabel(selected.endedUtc) : '进行中' }}</b></span>
            <span><small>{{ selected.player0 }}</small><b>{{ selected.deck0 || '未命名牌库' }}</b></span>
            <span><small>{{ selected.player1 }}</small><b>{{ selected.deck1 || '未命名牌库' }}</b></span>
            <span><small>操作数</small><b>{{ selected.commandCount }}</b></span>
          </div>
          <p v-if="selected.commandCount === 0">这场对局的回放载荷已清理，摘要与结算结果仍保留。</p>
          <p v-else-if="mobileReplayBlocked" class="mobile-replay-inline">移动端可查看完整摘要；请到电脑端播放回放。</p>
          <p v-else>回放将在独立的完整对战界面中打开。</p>
          <button class="primary" :disabled="!canUseSelectedReplay" @click="playSelected">播放回放</button>
        </section>
      </main>
      <div v-else class="records-placeholder">{{ loading ? '正在读取对局记录…' : '选择一场对局，或打开 JSON 回放' }}</div>
    </div>
  </section>
</template>

<style scoped>
.record-file-actions{display:flex;align-items:center;gap:8px}.record-file-actions input{display:none}
.records-retention-note{display:block;margin-top:6px;color:#87918e;font-size:13px;line-height:1.5}
.record-launch{display:grid;min-height:360px;place-items:center;align-content:center;gap:24px;border:1px solid rgba(240,239,229,.16);background:radial-gradient(circle at 50% 42%,rgba(41,117,123,.13),transparent 45%),rgba(4,7,8,.48);text-align:center}
.record-summary-grid{display:grid;width:min(680px,92%);grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;text-align:left}.record-summary-grid>span{display:grid;gap:4px;padding:10px;border:1px solid rgba(240,239,229,.13);background:#090d0e}.record-launch span,.record-launch small{color:#78817d;font-size:13px}.record-launch b{overflow-wrap:anywhere;color:#ece9df;font-size:14px}.record-launch p{max-width:520px;margin:0;color:#8f9793;font-size:14px;line-height:1.8}.record-launch button{min-height:var(--l12-site-hit,44px);padding:10px 32px;border:1px solid #d7c06f;background:#2c2612;color:#f4dda0;font-weight:900;letter-spacing:.12em}.record-launch button:disabled{cursor:not-allowed;opacity:.35}.mobile-replay-inline{padding:8px 12px;border-left:3px solid #d7c06f;background:#211c10;color:#d9c891!important}.record-decks{padding-right:72px;overflow-wrap:anywhere}.record-result{max-width:42%;text-align:right}
@media(max-width:700px){.match-records{height:auto;min-height:100%;overflow:visible;padding:12px}.records-header{align-items:flex-start;flex-direction:column;gap:12px}.records-header h1{font-size:24px}.record-file-actions{display:grid;width:100%;grid-template-columns:repeat(3,minmax(0,1fr));gap:6px}.record-file-actions button{min-height:var(--l12-site-hit,44px);padding:7px 5px;font-size:12px}.records-workspace{display:block;padding-top:10px}.records-list{max-height:none!important;padding-right:0;overflow:visible!important;border-right:0;border-bottom:1px solid rgba(240,239,229,.18)}.records-list>button{min-height:88px;padding:9px}.records-list>button span b{max-width:44%;white-space:normal}.records-list>button small{font-size:11px}.record-detail{margin-top:12px}.record-detail>header{gap:8px}.record-detail>header h2{font-size:18px}.record-launch{min-height:0;gap:14px;padding:14px 0}.record-summary-grid{width:calc(100% - 20px);grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}.record-summary-grid>span{padding:8px}.record-launch>button{width:calc(100% - 20px)}.records-placeholder{min-height:150px}}
</style>
