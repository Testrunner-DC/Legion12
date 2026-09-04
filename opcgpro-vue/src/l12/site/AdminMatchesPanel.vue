<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import {
  adminApi,
  type AdminMatchDetail,
  type AdminMatchPage,
  type AdminMatchParticipant,
  type AdminMatchSummary,
  type PlatformAccount,
} from '@/l12/platform'

const props = withDefaults(defineProps<{ initialMatchId?: string }>(), { initialMatchId: '' })
const emit = defineEmits<{ notice: [message: string] }>()

const view = ref<'recent' | 'player'>('recent')
const page = ref<AdminMatchPage>({ items: [], total: 0 })
const detail = ref<AdminMatchDetail | null>(null)
const accounts = ref<PlatformAccount[]>([])
const cards = ref<DeckCard[]>([])
const selectedAccountId = ref('')
const loading = ref(false)
const detailLoading = ref(false)
const replayExpanded = ref(false)
const filters = ref({ from: '', to: '', mode: '', status: 'completed', player: '', masterId: '' })

const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const visibleMatches = computed(() => page.value.items)
const completedCount = computed(() => visibleMatches.value.filter(match => match.status === 'completed').length)
const abnormalCount = computed(() => visibleMatches.value.filter(match => match.status === 'invalid' || match.error).length)
const averageDuration = computed(() => {
  const durations = visibleMatches.value.map(match => match.durationSeconds).filter((value): value is number => typeof value === 'number' && value >= 0)
  return durations.length ? Math.round(durations.reduce((sum, value) => sum + value, 0) / durations.length) : 0
})
const selectedPlayer = computed(() => accounts.value.find(account => account.id === selectedAccountId.value))
const playerSummary = computed(() => {
  if (view.value !== 'player') return null
  let wins = 0
  let losses = 0
  for (const match of visibleMatches.value) {
    const player = match.players.find(item => item.accountId === selectedAccountId.value)
    if (player?.result === 'win') wins++
    if (player?.result === 'loss') losses++
  }
  return { games: wins + losses, wins, losses, rate: wins + losses ? wins / (wins + losses) : 0 }
})

function modeLabel(mode: string) {
  return ({ ranked: '排位', casual: '休闲', friendly: '好友房', tournament: '赛事', legacy: '历史' } as Record<string, string>)[mode] || mode || '未知模式'
}
function resultLabel(result: string) {
  return ({ win: '胜', loss: '负', draw: '和', invalid: '无效', pending: '进行中' } as Record<string, string>)[result] || result
}
function statusLabel(match: AdminMatchSummary) {
  if (match.status === 'ongoing') return '进行中'
  if (match.status === 'invalid' || match.error) return '异常／无效'
  return '已结束'
}
function dateLabel(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}
function durationLabel(seconds?: number | null) {
  if (typeof seconds !== 'number') return '—'
  const minutes = Math.floor(seconds / 60)
  return `${minutes}:${String(Math.max(0, Math.floor(seconds % 60))).padStart(2, '0')}`
}
function coverageLabel(value: AdminMatchDetail['coverage']) {
  if (!value) return '暂无覆盖说明'
  if (value.inferredFacts || value.inferredDeckSnapshots) return '含历史推断数据'
  if (value.partialFacts) return '含命令边界推断'
  return '结构化精确采集'
}
function participantCardGroups(participant: AdminMatchParticipant) {
  const groups = new Map<string, { cardId: string; quantity: number; zone: string }>()
  for (const item of participant.deckCards ?? []) {
    const key = `${item.section || 'main'}:${item.cardId}`
    const existing = groups.get(key)
    if (existing) existing.quantity += item.quantity
    else groups.set(key, { cardId: item.cardId, quantity: item.quantity, zone: item.section || 'main' })
  }
  return [...groups.values()].sort((left, right) => left.zone.localeCompare(right.zone) || left.cardId.localeCompare(right.cardId))
}
function factLabel(type: string) {
  return ({
    'deck-included': '构筑收录', draw: '抽取', 'search-or-hand-add': '检索／加入手牌', search: '检索', 'hand-add': '加入手牌', play: '打出',
    activate: '发动', push: '进入堆叠', resolve: '结算', negate: '被无效', fizzle: '目标失效',
    'zone-move': '区域移动', damage: '造成伤害', kill: '击杀',
  } as Record<string, string>)[type] || type
}

async function ensureReferenceData() {
  if (!cards.value.length) cards.value = await loadDeckCatalog()
  if (!accounts.value.length) accounts.value = await adminApi.accounts()
}
async function loadMatches(reset = true) {
  if (view.value === 'player' && !selectedAccountId.value) {
    page.value = { items: [], total: 0 }
    detail.value = null
    return
  }
  loading.value = true
  try {
    const cursor = reset ? undefined : page.value.nextCursor || undefined
    const query = { ...filters.value, player: filters.value.player || undefined, cursor, limit: 50 }
    const next = view.value === 'player'
      ? await adminApi.playerMatches(selectedAccountId.value, query)
      : await adminApi.matches(query)
    page.value = reset ? next : { ...next, items: [...page.value.items, ...next.items] }
    if (reset) {
      const requested = props.initialMatchId && next.items.some(item => item.matchId === props.initialMatchId)
        ? props.initialMatchId : next.items[0]?.matchId
      if (requested) await selectMatch(requested)
      else detail.value = null
    }
  } catch (error) {
    emit('notice', error instanceof Error ? error.message : '对局档案加载失败')
  } finally { loading.value = false }
}
async function selectMatch(matchId: string) {
  detailLoading.value = true
  replayExpanded.value = false
  try { detail.value = await adminApi.match(matchId) }
  catch (error) { emit('notice', error instanceof Error ? error.message : '对局详情加载失败') }
  finally { detailLoading.value = false }
}
async function changeView(next: 'recent' | 'player') {
  view.value = next
  if (next === 'player') await ensureReferenceData()
  await loadMatches(true)
}
async function selectPlayer() { await loadMatches(true) }
function toggleReplay(event: Event) { replayExpanded.value = (event.currentTarget as HTMLDetailsElement).open }

watch(() => props.initialMatchId, async matchId => {
  if (!matchId) return
  view.value = 'recent'
  await selectMatch(matchId)
})
onMounted(async () => {
  await ensureReferenceData()
  await loadMatches(true)
})
</script>

<template>
  <section class="match-admin">
    <header class="module-header">
      <div><small>MATCH ARCHIVE</small><h2>对局档案</h2><p>从最近对局、玩家和实际构筑进入同一份赛后权威记录；沙盒不进入档案与分析。</p></div>
      <button :disabled="loading" @click="loadMatches(true)">刷新</button>
    </header>

    <div class="view-tabs">
      <button :class="{ active: view === 'recent' }" @click="changeView('recent')">最近对局</button>
      <button :class="{ active: view === 'player' }" @click="changeView('player')">按玩家查询</button>
    </div>

    <section class="filter-panel">
      <label v-if="view === 'player'">玩家<select v-model="selectedAccountId" @change="selectPlayer"><option value="">选择玩家</option><option v-for="account in accounts" :key="account.id" :value="account.id">{{ account.username }} · {{ account.id.slice(0, 8) }}</option></select></label>
      <label v-else>玩家／账号<input v-model="filters.player" placeholder="账号、玩家名或 ID" @keyup.enter="loadMatches(true)"/></label>
      <label>模式<select v-model="filters.mode"><option value="">全部正式模式</option><option value="ranked">排位</option><option value="casual">休闲</option><option value="friendly">好友房</option><option value="tournament">赛事</option></select></label>
      <label>状态<select v-model="filters.status"><option value="">全部状态</option><option value="completed">已结束</option><option value="ongoing">进行中</option><option value="invalid">异常／无效</option></select></label>
      <label>开始日期<input v-model="filters.from" type="date"/></label>
      <label>结束日期<input v-model="filters.to" type="date"/></label>
      <label>主宰编号<input v-model="filters.masterId" placeholder="例如 S01-01M1"/></label>
      <button class="query" :disabled="loading || (view === 'player' && !selectedAccountId)" @click="loadMatches(true)">查询</button>
    </section>

    <div class="metric-strip">
      <article><small>符合条件</small><b>{{ page.total }}</b><span>场正式对局</span></article>
      <article><small>本页已结束</small><b>{{ completedCount }}</b><span>不含沙盒</span></article>
      <article><small>异常／无效</small><b>{{ abnormalCount }}</b><span>便于快速复盘</span></article>
      <article><small>平均时长</small><b>{{ durationLabel(averageDuration) }}</b><span>本页可计算对局</span></article>
      <article v-if="playerSummary"><small>{{ selectedPlayer?.username || '玩家' }}</small><b>{{ playerSummary.wins }}-{{ playerSummary.losses }}</b><span>{{ (playerSummary.rate * 100).toFixed(1) }}% 胜率</span></article>
    </div>

    <div class="match-workspace">
      <section class="match-list panel-shell">
        <header><b>{{ view === 'recent' ? '最近记录' : `${selectedPlayer?.username || '玩家'}的记录` }}</b><span>按开始时间倒序</span></header>
        <button v-for="match in visibleMatches" :key="match.matchId" class="match-row" :class="{ selected: detail?.summary.matchId === match.matchId }" @click="selectMatch(match.matchId)">
          <span class="match-identity"><small>{{ modeLabel(match.modeId) }} · {{ dateLabel(match.startedUtc) }}</small><b><template v-for="(player,index) in match.players" :key="player.accountId || player.displayName"><em v-if="index"> VS </em>{{ player.displayName }}</template></b><code>{{ match.matchId.slice(0, 12) }}</code></span>
          <span class="match-result"><b>{{ statusLabel(match) }}</b><small>{{ durationLabel(match.durationSeconds) }} · {{ match.commandCount }} 次操作</small></span>
          <span class="match-players"><i v-for="player in match.players" :key="`${match.matchId}-${player.accountId}`" :data-result="player.result"><CardImage v-if="player.masterId" :card-id="player.masterId" :alt="player.masterId" intent="thumb"/><em>{{ player.deckName || '未记录牌库' }}</em><b>{{ resultLabel(player.result) }}</b></i></span>
        </button>
        <div v-if="loading" class="empty">正在读取对局档案…</div>
        <div v-else-if="!visibleMatches.length" class="empty">没有符合条件的正式对局</div>
        <button v-if="page.nextCursor" class="load-more" :disabled="loading" @click="loadMatches(false)">加载更早记录</button>
      </section>

      <section class="match-detail panel-shell">
        <div v-if="detailLoading" class="empty">正在读取赛后权威记录…</div>
        <template v-else-if="detail">
          <header class="detail-header"><div><small>{{ modeLabel(detail.summary.modeId) }} · {{ coverageLabel(detail.coverage) }}</small><h3>{{ detail.summary.players.map(player => player.displayName).join(' VS ') }}</h3><p>{{ dateLabel(detail.summary.startedUtc) }} · {{ durationLabel(detail.summary.durationSeconds) }} · {{ statusLabel(detail.summary) }}</p></div><code>{{ detail.summary.matchId }}</code></header>

          <section class="participant-grid">
            <article v-for="participant in detail.participants" :key="participant.playerIndex" class="participant-card">
              <header><CardImage v-if="participant.masterId" :card-id="participant.masterId" :alt="participant.masterId" intent="thumb"/><span><small>玩家 {{ participant.playerIndex + 1 }}</small><b>{{ participant.displayName }}</b><em>{{ participant.deckName }} · {{ resultLabel(participant.result) }}</em></span></header>
              <div v-if="participant.deckCards?.length" class="deck-cards">
                <article v-for="card in participantCardGroups(participant)" :key="`${card.zone}-${card.cardId}`"><CardImage :card-id="card.cardId" :legacy-url="cardById.get(card.cardId)?.imageUrl" :alt="cardById.get(card.cardId)?.nameZh || card.cardId" intent="thumb"/><span><b>{{ cardById.get(card.cardId)?.nameZh || card.cardId }}</b><small>{{ card.cardId }} · {{ card.zone }}</small></span><strong>×{{ card.quantity }}</strong></article>
              </div>
              <p v-else class="privacy-note">{{ detail.summary.status === 'ongoing' ? '进行中对局不展示私有构筑。' : '该历史对局没有可靠的完整构筑快照。' }}</p>
            </article>
          </section>

          <section class="fact-timeline">
            <header><div><h3>结构化对局时间线</h3><p>只使用权威事实，不从中文日志猜测卡牌行为。</p></div><b>{{ detail.cardFacts?.length || 0 }} 条</b></header>
            <ol v-if="detail.cardFacts?.length"><li v-for="fact in detail.cardFacts.slice(-120)" :key="`${fact.commandSequence}-${fact.kind}-${fact.cardId}-${fact.cardInstanceId}`"><code>#{{ fact.commandSequence }}</code><span><b>{{ factLabel(fact.kind) }}</b><small>回合 {{ fact.round ?? '—' }} · {{ fact.phase || '—' }}</small></span><span v-if="fact.cardId || fact.relatedCardId"><CardImage :card-id="fact.cardId || fact.relatedCardId || ''" :alt="cardById.get(fact.cardId || fact.relatedCardId || '')?.nameZh || fact.cardId || fact.relatedCardId || '卡牌'" intent="thumb"/><em>{{ cardById.get(fact.cardId || fact.relatedCardId || '')?.nameZh || fact.cardId || fact.relatedCardId }}</em></span><strong v-if="fact.amount">{{ fact.amount > 0 ? '+' : '' }}{{ fact.amount }}</strong></li></ol>
            <div v-else class="empty compact">旧记录尚无结构化卡牌事实；仍可查看赛果和构筑覆盖状态。</div>
          </section>

          <details v-if="detail.replay" class="technical-replay" @toggle="toggleReplay"><summary>技术审计／回放数据</summary><p>原始数据只在管理员主动展开时渲染，不参与列表查询。</p><pre v-if="replayExpanded">{{ JSON.stringify(detail.replay, null, 2) }}</pre></details>
        </template>
        <div v-else class="empty">选择一场对局查看玩家、构筑和时间线</div>
      </section>
    </div>
  </section>
</template>

<style scoped>
.match-admin{display:grid;gap:12px}.module-header,.filter-panel,.metric-strip,.panel-shell{border:1px solid #35424a;background:#101821}.module-header{display:flex;align-items:center;justify-content:space-between;padding:18px 20px}.module-header h2{margin:4px 0}.module-header p{margin:0}.module-header button,.filter-panel input,.filter-panel select,.filter-panel button{border:1px solid #4c5961;background:#080e13;color:#fff;padding:9px;font:700 11px 'Microsoft YaHei'}.view-tabs{display:grid;grid-template-columns:1fr 1fr;border:1px solid #35424a;background:#080e13}.view-tabs button{padding:13px;border:0;border-bottom:3px solid transparent;background:transparent;color:#87949a;font-weight:900}.view-tabs button.active{border-bottom-color:#d4b65d;background:#201b10;color:#f0d579}.filter-panel{display:grid;grid-template-columns:minmax(180px,1.4fr) repeat(5,minmax(125px,1fr)) auto;align-items:end;gap:8px;padding:12px}.filter-panel label{display:flex;min-width:0;flex-direction:column;gap:5px;color:#87949a;font-size:9px;font-weight:900}.filter-panel input,.filter-panel select{box-sizing:border-box;min-width:0;width:100%}.filter-panel .query{border-color:#91752e;background:#2b220d;color:#f1d471}.metric-strip{display:grid;grid-template-columns:repeat(5,1fr);gap:1px;background:#35424a}.metric-strip article{display:flex;min-height:82px;flex-direction:column;justify-content:flex-end;gap:3px;padding:13px;background:#0c141a}.metric-strip b{font-size:21px}.metric-strip span{color:#77858b;font-size:9px}.match-workspace{display:grid;grid-template-columns:minmax(400px,.82fr) minmax(560px,1.18fr);gap:12px;align-items:start}.panel-shell{min-width:0}.match-list{max-height:calc(100vh - 185px);overflow:auto}.match-list>header{position:sticky;z-index:2;top:0;display:flex;justify-content:space-between;padding:13px;background:#0b1218;border-bottom:1px solid #35424a}.match-list>header span{color:#738087;font-size:9px}.match-row{display:grid;width:100%;grid-template-columns:minmax(190px,1fr) 100px;gap:9px;padding:13px;border:0;border-bottom:1px solid #29353c;background:transparent;color:#fff;text-align:left}.match-row:hover,.match-row.selected{background:#172129}.match-row.selected{box-shadow:inset 3px 0 #d0ae4f}.match-identity{display:flex;min-width:0;flex-direction:column;gap:4px}.match-identity small,.match-result small{color:#76848a;font-size:8px}.match-identity b{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.match-identity b em{color:#c8aa52;font-style:normal}.match-identity code{color:#587179;font-size:8px}.match-result{text-align:right}.match-result b,.match-result small{display:block}.match-players{grid-column:1/-1;display:grid;grid-template-columns:1fr 1fr;gap:5px}.match-players i{display:grid;grid-template-columns:25px minmax(0,1fr) auto;align-items:center;gap:6px;padding:5px;border:1px solid #2f3b42;background:#0a1116;font-style:normal}.match-players .l12-card-image{width:25px;height:34px}.match-players em{overflow:hidden;color:#929da0;font-size:8px;font-style:normal;text-overflow:ellipsis;white-space:nowrap}.match-players i[data-result="win"] b{color:#72d1a7}.match-players i[data-result="loss"] b{color:#e7848e}.load-more{width:100%;padding:12px;border:0;background:#182229;color:#d5b85e;font-weight:900}.match-detail{padding:17px}.detail-header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px;padding-bottom:13px;border-bottom:1px solid #35424a}.detail-header h3{margin:5px 0;font-size:18px}.detail-header p{margin:0}.detail-header code{max-width:240px;color:#6e858e;font-size:8px;overflow-wrap:anywhere}.participant-grid{display:grid;grid-template-columns:1fr 1fr;gap:9px;margin-top:12px}.participant-card{min-width:0;padding:12px;border:1px solid #35424a;background:#0a1117}.participant-card>header{display:flex;align-items:center;gap:9px}.participant-card>header .l12-card-image{flex:none;width:42px;height:58px}.participant-card>header span{display:flex;min-width:0;flex-direction:column}.participant-card>header em{color:#829096;font-size:9px;font-style:normal}.deck-cards{display:grid;max-height:310px;gap:4px;margin-top:10px;overflow:auto}.deck-cards>article{display:grid;grid-template-columns:30px minmax(0,1fr) auto;align-items:center;gap:7px;padding:5px;border:1px solid #29363d;background:#0d171d}.deck-cards .l12-card-image{width:30px;height:41px}.deck-cards span{display:flex;min-width:0;flex-direction:column}.deck-cards span b{overflow:hidden;font-size:10px;text-overflow:ellipsis;white-space:nowrap}.deck-cards span small{color:#66767d!important;letter-spacing:0!important}.deck-cards strong{color:#e2c66f}.privacy-note{padding:15px;border:1px dashed #435159;color:#87949a}.fact-timeline{margin-top:12px;padding:12px;border:1px solid #35424a;background:#0a1117}.fact-timeline>header{display:flex;align-items:center;justify-content:space-between}.fact-timeline h3{margin:0}.fact-timeline p{margin:3px 0}.fact-timeline>header>b{color:#d8ba63}.fact-timeline ol{max-height:360px;overflow:auto;margin:10px 0 0;padding:0;list-style:none}.fact-timeline li{display:grid;grid-template-columns:50px minmax(120px,.8fr) minmax(150px,1fr) auto;align-items:center;gap:8px;padding:7px;border-top:1px solid #263239}.fact-timeline li>code{color:#6e858e}.fact-timeline li>span{display:flex;min-width:0;align-items:center;gap:7px}.fact-timeline li>span:nth-child(2){align-items:flex-start;flex-direction:column;gap:1px}.fact-timeline li small{color:#66767d!important;letter-spacing:0!important}.fact-timeline li .l12-card-image{flex:none;width:26px;height:36px}.fact-timeline li em{overflow:hidden;font-size:9px;font-style:normal;text-overflow:ellipsis;white-space:nowrap}.fact-timeline li>strong{color:#e5c76c}.technical-replay{margin-top:12px;padding:12px;border:1px solid #39474e;background:#080e13}.technical-replay summary{cursor:pointer;color:#d6bd70;font-weight:900}.technical-replay pre{max-height:380px;overflow:auto;color:#aeb9b9;font-size:8px;white-space:pre-wrap}.empty{display:grid;min-height:170px;place-items:center;color:#718087}.empty.compact{min-height:90px}.privacy-note{font-size:9px}.match-admin small{color:#d5b85e;font:900 9px monospace;letter-spacing:.12em}
@media(max-width:1450px){.filter-panel{grid-template-columns:repeat(4,minmax(130px,1fr))}.match-workspace{grid-template-columns:minmax(360px,.75fr) minmax(520px,1.25fr)}}
@media(max-width:1050px){.match-workspace{grid-template-columns:1fr}.match-list{max-height:520px}.metric-strip{grid-template-columns:repeat(3,1fr)}}
@media(max-width:720px){.filter-panel{grid-template-columns:1fr 1fr}.participant-grid{grid-template-columns:1fr}.metric-strip{grid-template-columns:1fr 1fr}.detail-header{flex-direction:column}.fact-timeline li{grid-template-columns:42px 1fr}.fact-timeline li>span:nth-child(3){grid-column:2}.match-players{grid-template-columns:1fr}}
</style>
