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
const mobileReplayNotice = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const canUseSelectedReplay = computed(() => Boolean(selected.value?.endedUtc && selected.value.commandCount > 0))
const mobileReplayBlocked = isMobileDeviceExperience()

function blockMobileReplay() {
  error.value = '请到电脑端查看回放'
  mobileReplayNotice.value = true
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
    <div v-if="mobileReplayNotice" class="mobile-replay-notice" role="alertdialog" aria-modal="true" aria-label="移动端回放提示">
      <p>请到电脑端查看回放</p>
      <button type="button" @click="mobileReplayNotice = false">知道了</button>
    </div>
    <div class="records-workspace">
      <aside class="records-list">
        <button v-for="match in matches" :key="match.matchId"
          :class="{ selected: selected?.matchId === match.matchId }" @click="selectMatch(match)">
          <span><b>{{ match.player0 }}</b><em>VS</em><b>{{ match.player1 }}</b></span>
          <small>{{ dateLabel(match.startedUtc) }} · {{ match.commandCount }} 次操作</small>
          <i>{{ resultLabel(match) }}</i>
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
          <div>
            <span>{{ dateLabel(selected.startedUtc) }}</span>
            <b>{{ resultLabel(selected) }}</b>
            <small>{{ selected.commandCount }} 个回放步骤</small>
          </div>
          <p v-if="selected.commandCount === 0">这场对局的回放载荷已清理，摘要与结算结果仍保留。</p>
          <p v-else>回放将在独立的完整对战界面中打开。进入播放器前不会加载或渲染棋盘。</p>
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
.mobile-replay-notice{position:fixed;z-index:2147483600;left:50%;top:50%;display:grid;box-sizing:border-box;width:min(320px,calc(100vw - 24px));justify-items:center;gap:14px;padding:18px;border:1px solid #667276;background:#090e10;color:#eeeae0;box-shadow:0 0 0 100vmax rgba(0,0,0,.58),0 16px 40px #000;transform:translate(-50%,-50%)}.mobile-replay-notice p{margin:0;font-size:15px;font-weight:900}.mobile-replay-notice button{min-width:88px;min-height:36px;padding:7px 12px;border:1px solid #8a9692;background:#172021;color:#fff;font-weight:900}
.record-launch{display:grid;min-height:360px;place-items:center;align-content:center;gap:24px;border:1px solid rgba(240,239,229,.16);background:radial-gradient(circle at 50% 42%,rgba(41,117,123,.13),transparent 45%),rgba(4,7,8,.48);text-align:center}
.record-launch>div{display:flex;align-items:center;justify-content:center;gap:14px}.record-launch span,.record-launch small{color:#78817d;font-size:14px}.record-launch b{color:#ece9df;font-size:15px}.record-launch p{max-width:520px;margin:0;color:#8f9793;font-size:14px;line-height:1.8}.record-launch button{padding:13px 32px;border:1px solid #d7c06f;background:#2c2612;color:#f4dda0;font-weight:900;letter-spacing:.12em}.record-launch button:disabled{cursor:not-allowed;opacity:.35}
@media(max-width:720px){.records-header{align-items:flex-start;gap:12px}.record-file-actions{flex-wrap:wrap}.record-launch>div{flex-direction:column;gap:6px}}
</style>
