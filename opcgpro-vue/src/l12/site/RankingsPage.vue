<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { masterProfileUrl } from '@/l12/specialAssets'
import RankedIdentityBadge from '@/l12/RankedIdentityBadge.vue'
import RankedMasterTitleRulesModal from './RankedMasterTitleRulesModal.vue'
import MasterMatchupMatrix, { type MasterMatchupMatrixCell, type MasterMatchupMatrixMaster } from './MasterMatchupMatrix.vue'
import {
  platformState,
  rankedApi,
  type RankedAnalytics,
  type RankedLeaderboardEntry,
  type RankedSeasonHistory,
  type RankedSeasonHonorHistory,
} from '@/l12/platform'

type RankingTab = 'players' | 'masters' | 'matchups' | 'history'
type RankingRange = '7d' | '30d' | 'season'
type MasterSort = 'games' | 'winRate' | 'firstWinRate' | 'secondWinRate' | 'usageRate'
type PlayerLeaderboardEntry = RankedLeaderboardEntry & {
  favoriteMasterId?: string | null
  favoriteMasterName?: string | null
}

const faction = ref('')
const range = ref<RankingRange>('season')
const tab = ref<RankingTab>('players')
const search = ref('')
const masterSort = ref<MasterSort>('games')
const players = ref<PlayerLeaderboardEntry[]>([])
const history = ref<RankedSeasonHistory>({ honors: [], factionTotals: [] })
const analytics = ref<RankedAnalytics>({
  range: 'season',
  summary: { matches: 0, placedPlayers: 0, activeMasters: 0 },
  masters: [],
  matchups: [],
})
const loading = ref(false)
const error = ref('')
const masterTitleRulesOpen = ref(false)
const filters = [{ id: '', name: '全服' }, { id: 'order', name: '秩序' }, { id: 'chaos', name: '混沌' }, { id: 'fate', name: '命运' }]
const ranges: Array<{ id: RankingRange; name: string }> = [{ id: '7d', name: '近7天' }, { id: '30d', name: '近30天' }, { id: 'season', name: '本赛季' }]

let disposed = false
let reloadPending = false
let refreshTimer: ReturnType<typeof setTimeout> | undefined
const refreshIntervalMs = 60_000
function scheduleRefresh() {
  clearTimeout(refreshTimer)
  if (!disposed && !document.hidden) refreshTimer = setTimeout(() => { void load() }, refreshIntervalMs)
}
async function load() {
  if (disposed) return
  if (loading.value) { reloadPending = true; return }
  clearTimeout(refreshTimer)
  const requestedFaction = faction.value
  const requestedRange = range.value
  loading.value = true
  error.value = ''
  try {
    const [response, historyResponse] = await Promise.all([rankedApi.leaderboard(requestedFaction, requestedRange), rankedApi.history()])
    if (disposed || requestedFaction !== faction.value || requestedRange !== range.value) return
    players.value = response.players as PlayerLeaderboardEntry[]
    analytics.value = response.analytics
    history.value = historyResponse
  } catch (cause) {
    if (!disposed && requestedFaction === faction.value && requestedRange === range.value)
      error.value = cause instanceof Error ? cause.message : '排行榜加载失败'
  } finally {
    loading.value = false
    if (reloadPending && !disposed) { reloadPending = false; void load() }
    else scheduleRefresh()
  }
}
function onVisibilityChange() {
  clearTimeout(refreshTimer)
  if (!document.hidden) void load()
}

const query = computed(() => search.value.trim().toLocaleLowerCase())
const visiblePlayers = computed(() => query.value
  ? players.value.filter(row => `${row.username} ${row.faction} ${row.tier} ${row.titles.join(' ')} ${row.favoriteMasterName ?? ''}`.toLocaleLowerCase().includes(query.value))
  : players.value)
const visibleMasters = computed(() => {
  const rows = query.value
    ? analytics.value.masters.filter(row => `${row.masterName} ${row.masterId} ${row.strongestPlayer ?? ''} ${row.title ?? ''}`.toLocaleLowerCase().includes(query.value))
    : analytics.value.masters
  return [...rows].sort((left, right) => right[masterSort.value] - left[masterSort.value]
    || right.games - left.games
    || right.winRate - left.winRate
    || left.masterName.localeCompare(right.masterName, 'zh-CN'))
})
const currentMasterTitles = computed(() => new Set(analytics.value.masters.flatMap(row => row.title ? [row.title] : [])))
const titleVariant = (title: string) => title.startsWith('最强') || currentMasterTitles.value.has(title) ? 'master-title' as const : 'faction-title' as const
const matrixMasters = computed(() => visibleMasters.value)
const matrixRows = computed<MasterMatchupMatrixMaster[]>(() => matrixMasters.value.map(row => ({
  id: row.masterId, name: row.masterName, imageUrl: masterProfileUrl(row.masterId), rank: row.rank, winRate: row.winRate / 100,
})))
const matrixCells = computed<MasterMatchupMatrixCell[]>(() => analytics.value.matchups.map(row => ({
  masterId: row.masterId, opponentMasterId: row.opponentMasterId, samples: row.games, winRate: row.winRate / 100,
  firstWins: row.firstWins, firstSamples: row.firstGames, secondWins: row.secondWins, secondSamples: row.secondGames,
})))
const visibleHonors = computed(() => query.value
  ? history.value.honors.filter(row => `${row.seasonName} ${row.title} ${row.winners.map(winner => `${winner.username} ${winner.faction}`).join(' ')}`.toLocaleLowerCase().includes(query.value))
  : history.value.honors)
const factionHonorOrder = ['统御者', '始乱者', '织命者', '代行主君', '混沌领主', '守望天士']
const factionForHonor = (title: string) => ({ 统御者: 'order', 始乱者: 'chaos', 织命者: 'fate', 代行主君: 'order', 混沌领主: 'chaos', 守望天士: 'fate' } as Record<string, string>)[title]
const cardNumberOrder = new Intl.Collator('zh-CN', { numeric: true })
const honorGroups = computed(() => {
  const groups = new Map<string, RankedSeasonHonorHistory[]>()
  for (const row of visibleHonors.value) groups.set(row.title, [...(groups.get(row.title) ?? []), row])
  return [...groups].map(([title, seasons]) => ({
    title, seasons, masterId: seasons.find(row => row.masterId)?.masterId,
    faction: factionForHonor(title) || seasons[0]?.winners[0]?.faction,
  })).sort((left, right) => {
    const leftOrder = factionHonorOrder.indexOf(left.title)
    const rightOrder = factionHonorOrder.indexOf(right.title)
    if (leftOrder >= 0 || rightOrder >= 0) return (leftOrder < 0 ? 99 : leftOrder) - (rightOrder < 0 ? 99 : rightOrder)
    if (left.masterId || right.masterId) return left.masterId && right.masterId
      ? cardNumberOrder.compare(left.masterId, right.masterId) : left.masterId ? -1 : 1
    return cardNumberOrder.compare(left.title, right.title)
  })
})
const visibleFactionTotals = computed(() => query.value
  ? history.value.factionTotals.filter(row => `${row.seasonName} ${row.factions.map(factionRow => `${factionRow.faction} ${factionRow.displayValue}`).join(' ')}`.toLocaleLowerCase().includes(query.value))
  : history.value.factionTotals)
const latestHistorySeason = computed(() => history.value.latestSeasonName
  || history.value.factionTotals[0]?.seasonName || history.value.honors[0]?.seasonName)
const expandedHistorySeason = (seasonName: string) => Boolean(query.value) || seasonName === latestHistorySeason.value
const updatedAt = computed(() => analytics.value.summary.updatedAt
  ? new Date(analytics.value.summary.updatedAt).toLocaleString() : '暂无数据')

function percent(value: number) { return `${value.toFixed(1)}%` }

watch([faction, range], load)
onMounted(() => {
  document.addEventListener('visibilitychange', onVisibilityChange)
  void load()
})
onBeforeUnmount(() => {
  disposed = true
  clearTimeout(refreshTimer)
  document.removeEventListener('visibilitychange', onVisibilityChange)
})
</script>

<template>
  <div class="ranking-page">
    <header class="page-head">
      <div><small>RANKED · CURRENT SEASON</small><h1>排行榜</h1><p>排位数据、主宰表现与对阵关系均由服务端权威统计。</p></div>
      <div class="page-actions"><button :disabled="loading" @click="load">{{ loading ? '读取中…' : '刷新数据' }}</button></div>
    </header>

    <section class="summary-strip">
      <article><small>有效排位</small><strong>{{ analytics.summary.matches }}</strong><span>{{ range === 'season' ? '本赛季' : range === '7d' ? '近7天' : '近30天' }}</span></article>
      <article><small>已定级玩家</small><strong>{{ analytics.summary.placedPlayers }}</strong><span>当前赛季</span></article>
      <article><small>活跃主宰</small><strong>{{ analytics.summary.activeMasters }}</strong><span>统计范围内</span></article>
      <article><small>最近计入对局</small><strong class="updated">{{ updatedAt }}</strong><span>页面可见时每分钟自动刷新</span></article>
    </section>

    <section class="toolbar">
      <div class="tabs"><button :class="{ active: tab === 'players' }" @click="tab = 'players'">玩家榜</button><button :class="{ active: tab === 'masters' }" @click="tab = 'masters'">主宰榜</button><button :class="{ active: tab === 'matchups' }" @click="tab = 'matchups'">对阵一览</button><button :class="{ active: tab === 'history' }" @click="tab = 'history'">历史荣誉</button></div>
      <div class="ranges"><button v-for="item in ranges" :key="item.id" :class="{ active: range === item.id }" :disabled="tab === 'history'" @click="range = item.id">{{ item.name }}</button></div>
      <button class="master-title-rules-button" type="button" @click="masterTitleRulesOpen = true">最强称号规则</button>
      <label v-if="tab === 'masters'" class="master-sort">主宰排序
        <select v-model="masterSort">
          <option value="games">总场次</option><option value="winRate">总胜率</option>
          <option value="firstWinRate">先手胜率</option><option value="secondWinRate">后手胜率</option>
          <option value="usageRate">使用率</option>
        </select>
      </label>
      <input v-model="search" class="ranking-search" :placeholder="tab === 'players' ? '搜索玩家、段位或称号' : tab === 'history' ? '搜索赛季、玩家或称号' : '搜索主宰或最强玩家'">
    </section>

    <nav v-if="tab === 'players'" class="faction-filter"><button v-for="item in filters" :key="item.id" :class="{ active: faction === item.id }" @click="faction = item.id">{{ item.name }}</button></nav>
    <p v-if="error" class="error">{{ error }}</p>

    <section v-if="tab === 'players'" class="rank-panel player-table">
      <div class="thead"><span>排名</span><span>昵称</span><span>阵营</span><span>段位</span><span>称号</span><span>最擅长主宰</span><span>七曜值</span><span>场次</span><span>战绩</span><span>胜率</span></div>
      <div v-for="row in visiblePlayers" :key="`${row.rank}-${row.username}-${row.faction}`" class="tr" :class="[`rank-${Math.min(row.rank, 4)}`, { 'is-me': row.username === platformState.account?.username }]">
        <b data-label="排名">#{{ row.rank }}</b>
        <strong class="player-name" data-label="昵称"><span class="username">{{ row.username }} <i v-if="row.username === platformState.account?.username" class="me-badge">我</i></span></strong>
        <span data-label="阵营">{{ row.faction }}</span><span data-label="段位"><RankedIdentityBadge variant="tier" :faction="row.faction" :label="row.tier"/></span>
        <span class="title-list player-title-cell" data-label="称号"><RankedIdentityBadge v-for="title in row.titles" :key="title" :variant="titleVariant(title)" :faction="row.faction" :label="title"/><span v-if="!row.titles?.length">—</span></span>
        <span v-if="row.favoriteMasterId" class="player-master" data-label="最擅长主宰"><img class="player-master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(row.favoriteMasterId)" :alt="`${row.favoriteMasterName || row.favoriteMasterId}头像`"/><b>{{ row.favoriteMasterName || row.favoriteMasterId }}</b></span><span v-else data-label="最擅长主宰">—</span>
        <strong data-label="七曜值">{{ row.displayValue }}</strong><span data-label="场次">{{ row.wins + row.losses }}</span>
        <span data-label="战绩"><i>{{ row.wins }}</i>胜 <em>{{ row.losses }}</em>负</span><strong data-label="胜率">{{ percent((row.wins + row.losses) ? row.wins * 100 / (row.wins + row.losses) : 0) }}</strong>
        <span class="player-mobile-meta">{{ row.faction }} · {{ row.wins }}胜{{ row.losses }}负 · {{ percent((row.wins + row.losses) ? row.wins * 100 / (row.wins + row.losses) : 0) }} · {{ row.wins + row.losses }}场</span>
        <span class="player-mobile-extras">
          <span class="mobile-title-strip"><RankedIdentityBadge v-for="title in row.titles.slice(0, 2)" :key="title" :variant="titleVariant(title)" :faction="row.faction" :label="title"/><b v-if="row.titles.length > 2">+{{ row.titles.length - 2 }}</b><i v-if="!row.titles.length">暂无称号</i></span>
          <span v-if="row.favoriteMasterId" class="mobile-favorite-master"><img :src="masterProfileUrl(row.favoriteMasterId)" :alt="`${row.favoriteMasterName || row.favoriteMasterId}头像`"/><b>{{ row.favoriteMasterName || row.favoriteMasterId }}</b></span>
        </span>
      </div>
      <div v-if="!visiblePlayers.length" class="empty">{{ loading ? '正在读取排位数据…' : '当前筛选下暂无完成定级的玩家' }}</div>
    </section>

    <section v-else-if="tab === 'masters'" class="rank-panel master-table">
      <div class="thead"><span>排名</span><span>主宰</span><span>最强玩家</span><span>场次</span><span>战绩</span><span>胜率</span><span>使用率</span><span>先手</span><span>后手</span></div>
      <div v-for="(row, index) in visibleMasters" :key="row.masterId" class="tr" :class="`rank-${Math.min(index + 1, 4)}`">
        <b data-label="排名">#{{ index + 1 }}</b>
        <span class="master-card" data-label="主宰"><img class="master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(row.masterId)" :alt="`${row.masterName}头像`"/><strong>{{ row.masterName }}<small>{{ row.masterId }}</small></strong></span>
        <span class="champion" data-label="最强玩家"><RankedIdentityBadge v-if="row.title" variant="master-title" :label="row.title"/><strong>{{ row.strongestPlayer || '尚未产生' }}</strong></span>
        <strong data-label="场次">{{ row.games }}</strong><span data-label="战绩"><i>{{ row.wins }}</i>胜 <em>{{ row.losses }}</em>负</span><b class="rate" data-label="胜率">{{ percent(row.winRate) }}</b><span data-label="使用率">{{ percent(row.usageRate) }}</span>
        <span data-label="先手">{{ percent(row.firstWinRate) }}<small>{{ row.firstWins }}/{{ row.firstGames }}</small></span><span data-label="后手">{{ percent(row.secondWinRate) }}<small>{{ row.secondWins }}/{{ row.secondGames }}</small></span>
      </div>
      <div v-if="!visibleMasters.length" class="empty">{{ loading ? '正在聚合主宰数据…' : '当前范围暂无主宰数据' }}</div>
    </section>

    <section v-else-if="tab === 'history'" class="history-panel">
      <section v-if="visibleFactionTotals.length" class="faction-final-totals" aria-label="历届派系结算数值">
        <header><div><small>FACTION FINALS</small><h2>历届派系结算数值</h2></div></header>
        <div class="faction-total-grid">
          <details v-for="(season, seasonIndex) in visibleFactionTotals" :key="`${season.seasonName}-${seasonIndex}`" :open="expandedHistorySeason(season.seasonName)" class="history-season-details">
            <summary>{{ season.seasonName }}</summary>
            <div class="faction-total-values"><span v-for="factionRow in season.factions" :key="factionRow.faction"><small>{{ factionRow.faction }}</small><b>{{ factionRow.displayValue }}</b></span></div>
          </details>
        </div>
      </section>
      <section v-if="honorGroups.length" class="honor-groups" aria-label="历史称号获得者">
        <article v-for="group in honorGroups" :key="group.title" class="honor-group">
          <header><img v-if="group.masterId" class="honor-master-profile" :src="masterProfileUrl(group.masterId)" :alt="`${group.title}主宰头像`"/><RankedIdentityBadge :variant="titleVariant(group.title)" :faction="group.faction" :label="group.title"/><span>历届获得者</span></header>
          <details v-for="(season, seasonIndex) in group.seasons" :key="`${group.title}-${season.seasonName}-${seasonIndex}`" class="honor-season history-season-details" :open="expandedHistorySeason(season.seasonName)">
            <summary>{{ season.seasonName }}</summary>
            <div class="honor-winners"><span v-for="winner in season.winners" :key="`${winner.username}-${winner.faction}`"><b>{{ winner.username }}</b><em>{{ winner.faction }}</em></span></div>
          </details>
        </article>
      </section>
      <div v-if="!visibleHonors.length && !visibleFactionTotals.length" class="rank-panel empty">{{ query ? '没有符合搜索条件的历史荣誉或派系结算数值' : '尚无已结算的历史赛季荣誉' }}</div>
    </section>

    <section v-else class="matrix-panel">
      <header><div><small>MASTER MATCHUPS</small><h2>主宰对阵一览</h2><p>纵轴为我方、横轴为对方；绿色优势、红色劣势，先后手数据悬停可见。</p></div><span>当前 {{ matrixMasters.length }} 位主宰</span></header>
      <MasterMatchupMatrix :masters="matrixRows" :cells="matrixCells" :empty-text="loading ? '正在生成对阵矩阵…' : '当前范围暂无对阵数据'"/>
    </section>
    <RankedMasterTitleRulesModal v-model="masterTitleRulesOpen"/>
  </div>
</template>

<style scoped>
.ranking-page{min-height:100%;padding:28px clamp(16px,3vw,44px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif;color:#eef1ed}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px}.page-head small,.matrix-panel header small{color:#53c3ca;font:900 14px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0;font-size:30px}.page-head p,.matrix-panel header p{margin:0;color:#77858b;font-size:14px}.page-head button,.toolbar button,.faction-filter button{padding:10px 14px;border:1px solid #36434c;background:#091016;color:#879399;font-weight:900}.page-head button:disabled{opacity:.45}.summary-strip{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin:20px 0 12px}.summary-strip article{display:grid;gap:4px;min-height:86px;padding:14px;border:1px solid #303e48;background:linear-gradient(135deg,#101a23,#0a1016)}.summary-strip small{color:#72828b;font:800 14px monospace}.summary-strip strong{color:#efd375;font-size:24px}.summary-strip strong.updated{font-size:14px}.summary-strip span{color:#697880;font-size:14px}.toolbar{display:grid;grid-template-columns:auto auto minmax(190px,1fr);align-items:center;gap:10px;padding:10px;border:1px solid #2f3b45;background:#0c141d}.tabs,.ranges,.faction-filter{display:flex}.toolbar button.active,.faction-filter button.active{border-color:#c7a64b;background:#392e13;color:#f6d978}.toolbar input{min-width:0;padding:10px 12px;border:1px solid #36434c;background:#070c11;color:#e7ecea}.faction-filter{width:max-content;margin:12px 0}.rank-panel{overflow:hidden;border:1px solid #35424a;background:#0a1118}.thead,.tr{display:grid;align-items:center;min-height:58px;padding:6px 18px;border-bottom:1px solid rgba(235,230,216,.09)}.player-table .thead,.player-table .tr{grid-template-columns:64px 1.6fr .65fr .8fr 1fr .8fr .65fr .45fr}.master-table .thead,.master-table .tr{grid-template-columns:58px 1.35fr 1.25fr .45fr .75fr .55fr .55fr .65fr .65fr}.thead{min-height:42px;padding-top:0;padding-bottom:0;color:#66757c;font-size:14px;font-weight:900}.tr{position:relative;font-size:14px}.tr:hover{background:#111c26}.tr>b:first-child{color:#d9dde1;font-size:15px}.tr.rank-1>b:first-child{color:#ffb239;text-shadow:0 0 12px #ff9f2c99}.tr.rank-2>b:first-child{color:#e1e8ef}.tr.rank-3>b:first-child{color:#c98d63}.tr i{font-style:normal}.tr em{color:#ee6c78;font-style:normal}.player-name{display:grid;justify-items:start;gap:7px}.username{font-size:14px}.title-list{display:flex;flex-direction:column;align-items:flex-start;gap:6px}.title-badge,.champion-title{position:relative;display:inline-flex!important;align-items:center;width:max-content;margin:0!important;border:1px solid #f1bd4a!important;border-radius:5px;background:linear-gradient(135deg,#b47716 0%,#6f3d08 48%,#3a1d02 100%)!important;color:#fff4b5!important;font-weight:900;letter-spacing:.04em;box-shadow:0 0 0 1px #5b3208,0 0 16px #e8a12f78,inset 0 1px #fff1a477;text-shadow:0 1px 2px #000}.title-badge{gap:5px;padding:5px 10px;font-size:14px!important}.title-badge i,.champion-title i{color:#fff0a0;filter:drop-shadow(0 0 4px #ffd047)}.master-card{display:flex;align-items:center;gap:9px}.master-avatar{width:40px;height:40px;border:1px solid #66747b;border-radius:50%;background:#080d11;object-fit:cover}.master-card strong,.master-card small,.champion strong,.master-table .tr>span>small{display:block}.master-card small,.master-table .tr>span>small{margin-top:3px;color:#687880;font:700 14px monospace}.champion{display:grid;justify-items:start;gap:6px}.champion-title{gap:7px;padding:6px 11px;font-size:14px!important}.champion-title i{font-size:14px}.champion>strong{padding-left:2px;color:#f8e4a2}.rate{color:#f0c86a}.matrix-panel{border:1px solid #35424a;background:#091018}.matrix-panel>header{display:flex;align-items:flex-end;justify-content:space-between;padding:16px;border-bottom:1px solid #35424a}.matrix-panel h2{margin:4px 0;font-size:18px}.matrix-panel header>span{color:#809098;font-size:14px}.empty{display:grid;min-height:280px;place-items:center;color:#738088}.error{padding:10px;border-left:3px solid #b83240;background:#251017;color:#e69aa1}
@media(max-width:1050px){.summary-strip{grid-template-columns:1fr 1fr}.toolbar{grid-template-columns:1fr}.tabs,.ranges{width:100%}.tabs button,.ranges button{flex:1}.player-table,.master-table{overflow:auto}.player-table .thead,.player-table .tr{min-width:880px}.master-table .thead,.master-table .tr{min-width:980px}}
@media(max-width:700px){.ranking-page{padding:18px 10px 40px}.page-head{align-items:flex-start;flex-direction:column}.summary-strip{grid-template-columns:1fr 1fr}.summary-strip strong{font-size:19px}.player-name{align-items:flex-start;flex-direction:column}}
.tr.is-me{background:linear-gradient(90deg,#122c32,#111824);box-shadow:inset 3px 0 #55c7ce}.me-badge{display:inline-grid;min-width:18px;height:18px;place-items:center;margin-left:5px;border-radius:50%;background:#55c7ce;color:#061012;font-size:14px;font-style:normal}
.history-panel{display:grid;gap:14px}.faction-final-totals,.honor-group{overflow:hidden;border:1px solid #35424a;background:#0a1118}.faction-final-totals>header,.honor-group>header{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:14px 16px;border-bottom:1px solid rgba(235,230,216,.09)}.faction-final-totals>header small{color:#53c3ca;font:900 12px monospace;letter-spacing:.14em}.faction-final-totals h2,.faction-final-totals h3,.honor-season h3{margin:0}.faction-final-totals h2{margin-top:3px;font-size:18px}.faction-final-totals>header>span,.honor-group>header>span{color:#77858b;font-size:13px}.faction-total-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,480px),1fr));gap:1px;background:#0a1118}.faction-total-grid article{min-width:0;padding:14px 16px;background:#0a1118}.faction-total-grid article>div{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:12px}.faction-total-grid article span{display:grid;min-width:0;gap:4px;padding:10px;border:1px solid #2b3841;background:#0d1720}.faction-total-grid article small{color:#7d8b92}.faction-total-grid article b{overflow-wrap:anywhere;color:#efd375;font-size:17px}.honor-groups{display:grid;gap:12px}.honor-group>header{justify-content:flex-start}.honor-group>header>span{margin-left:auto}.honor-season{display:grid;grid-template-columns:minmax(120px,.35fr) minmax(0,1fr);gap:16px;padding:14px 16px;border-bottom:1px solid rgba(235,230,216,.09)}.honor-season:last-child{border-bottom:0}.honor-season h3{font-size:15px}.honor-winners{display:flex;min-width:0;flex-wrap:wrap;gap:8px}.honor-winners>span{display:flex;min-width:0;align-items:center;gap:7px;padding:7px 9px;border:1px solid #2f3d45;background:#0d1720}.honor-winners b{overflow-wrap:anywhere}.honor-winners em{color:#8e9ba0;font-size:12px;font-style:normal}
.page-actions{display:flex;gap:8px}.page-actions button:first-child{border-color:#a98d3f;color:#efd477}
.player-table .thead,.player-table .tr{grid-template-columns:56px minmax(110px,.9fr) .55fr .65fr minmax(150px,1.25fr) minmax(130px,1fr) .8fr .45fr .68fr .58fr}
.master-avatar{border-radius:0}
.player-table{overflow-x:auto}.player-title-cell{align-content:center}.player-title-cell>span{color:#697880}.player-master{display:flex;align-items:center;gap:8px;min-width:0}.player-master-avatar{width:34px;height:34px;flex:0 0 34px;border:1px solid #66747b;border-radius:0;background:#080d11;object-fit:cover}.player-master b{overflow:hidden;font-size:14px;text-overflow:ellipsis;white-space:nowrap}
@media(max-width:1050px){.player-table .thead,.player-table .tr{min-width:1180px}}
.toolbar{display:flex;flex-wrap:wrap}.toolbar .tabs,.toolbar .ranges{flex:none}.toolbar .ranking-search{flex:0 1 360px;width:clamp(220px,24vw,380px);margin-left:auto}.toolbar .master-title-rules-button{flex:none}

.ranking-page{--ranking-master-avatar:44px}.player-table .tr,.master-table .tr{min-height:68px;padding-block:7px}.master-avatar,.player-master-avatar{box-sizing:border-box;width:var(--ranking-master-avatar);height:var(--ranking-master-avatar);min-width:var(--ranking-master-avatar);max-width:var(--ranking-master-avatar);flex:0 0 var(--ranking-master-avatar);border-radius:0;object-fit:cover}
@media(max-width:520px){.ranking-page{--ranking-master-avatar:28px;padding:14px 10px 32px}.page-head{gap:6px}.page-head small,.matrix-panel header small{font-size:10.5px;letter-spacing:.13em}.page-head h1{margin:3px 0;font-size:24px}.page-head p,.matrix-panel header p{font-size:12px}.page-head button,.toolbar button,.faction-filter button{min-height:44px;padding:7px 9px;font-size:12px}.summary-strip{gap:6px;margin:13px 0 9px}.summary-strip article{min-height:68px;gap:2px;padding:9px}.summary-strip small,.summary-strip span{font-size:11px}.summary-strip strong{font-size:17px}.summary-strip strong.updated{font-size:11px}.toolbar{gap:7px;padding:8px}.toolbar input{min-height:44px;padding:7px 9px;font-size:12px}.toolbar .ranking-search{flex-basis:100%;width:100%;margin-left:0}.faction-filter{max-width:100%;margin:8px 0;overflow-x:auto}.faction-filter button{flex:0 0 auto}.thead,.tr{min-height:45px;padding:5px 10px;font-size:12px}.thead{min-height:34px;font-size:11px}.tr>b:first-child,.username,.title-badge,.champion-title{font-size:12px!important}.title-badge{gap:3px;padding:3px 6px}.master-card small,.master-table .tr>span>small,.player-master b{font-size:11px}.matrix-panel>header{gap:8px;padding:10px}.matrix-panel h2{margin:2px 0;font-size:15px}.matrix-panel header>span{font-size:11px}.player-table .thead,.player-table .tr{min-width:920px}.master-table .thead,.master-table .tr{min-width:820px}.empty{min-height:190px;font-size:12px}.error{padding:8px;font-size:12px}}
@media(max-width:700px){.toolbar .tabs{display:grid;grid-template-columns:1fr 1fr;width:100%}.toolbar .ranges{display:grid;grid-template-columns:repeat(3,1fr);width:100%}.toolbar .master-title-rules-button,.toolbar .ranking-search{width:100%;flex-basis:100%;margin-left:0}.faction-filter{display:grid;width:100%;grid-template-columns:repeat(4,minmax(0,1fr));gap:5px;overflow:visible}.faction-filter button{min-width:0;padding-inline:5px}.player-table,.master-table,.honor-table{overflow:visible;border:0;background:transparent}.player-table .thead,.master-table .thead,.honor-table .thead{display:none}.player-table .tr,.master-table .tr,.honor-table .tr{box-sizing:border-box;display:grid;min-width:0!important;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px 12px;margin-bottom:8px;padding:12px;border:1px solid #35424a;background:#0a1118}.player-table .tr>*,.master-table .tr>*,.honor-table .tr>*{min-width:0;overflow-wrap:anywhere}.player-table .tr>[data-label],.master-table .tr>[data-label],.honor-table .tr>[data-label]{display:block}.player-table .tr>.title-list[data-label],.honor-table .tr>.title-list[data-label]{display:flex;flex-direction:column;align-items:flex-start;gap:6px}.player-table .tr>[data-label]::before,.master-table .tr>[data-label]::before,.honor-table .tr>[data-label]::before{content:attr(data-label);display:block;margin-bottom:4px;color:#65757d;font-size:10px;font-weight:900}.player-table .tr>[data-label="称号"],.player-table .tr>[data-label="最擅长主宰"],.master-table .tr>[data-label="最强玩家"],.honor-table .tr>[data-label="获得称号"]{grid-column:1/-1}.player-table .title-list,.master-table .champion,.honor-table .title-list{align-items:flex-start}.player-table .player-master b{white-space:normal;text-overflow:clip}.player-table .ranked-identity-badge,.master-table .ranked-identity-badge,.honor-table .ranked-identity-badge{max-width:100%}}
.player-mobile-meta,.player-mobile-extras{display:none}
@media(max-width:700px){
  .summary-strip{display:flex;overflow-x:auto;scroll-snap-type:x proximity}.summary-strip article{min-width:118px;min-height:66px;scroll-snap-align:start}
  .toolbar button,.faction-filter button,.toolbar input,.page-head button{min-height:var(--l12-site-hit,44px)}
  .player-table .tr{display:grid;min-height:106px!important;grid-template-columns:36px minmax(0,1fr) auto auto!important;grid-template-rows:26px 20px 34px;gap:3px 7px;margin-bottom:6px;padding:8px!important}
  .player-table .tr> :nth-child(1){grid-area:1/1/3/2;align-self:center;font-size:14px!important}
  .player-table .tr> :nth-child(2){grid-area:1/2/2/3;align-self:center}
  .player-table .tr> :nth-child(4){grid-area:1/3/2/4;align-self:center}
  .player-table .tr> :nth-child(7){grid-area:1/4/2/5;align-self:center;color:#efd375;text-align:right}
  .player-table .tr> :nth-child(3),.player-table .tr> :nth-child(5),.player-table .tr> :nth-child(6),.player-table .tr> :nth-child(8),.player-table .tr> :nth-child(9),.player-table .tr> :nth-child(10){display:none!important}
  .player-table .tr>[data-label]::before{display:none!important}
  .player-mobile-meta{display:block!important;grid-area:2/2/3/5;color:#7f8c92;font-size:11px;white-space:nowrap}
  .player-mobile-extras{display:flex!important;grid-area:3/1/4/5;min-width:0;align-items:center;justify-content:space-between;gap:7px;overflow:hidden}
  .mobile-title-strip{display:flex;min-width:0;align-items:center;gap:4px;overflow-x:auto;scrollbar-width:none}.mobile-title-strip :deep(.ranked-identity-badge){flex:0 0 auto;max-height:28px;font-size:11px}.mobile-title-strip>b,.mobile-title-strip>i{flex:0 0 auto;color:#76848a;font-size:11px;font-style:normal}
  .mobile-favorite-master{display:flex;max-width:38%;flex:0 0 auto;align-items:center;gap:4px}.mobile-favorite-master img{width:26px;height:26px;object-fit:cover}.mobile-favorite-master b{overflow:hidden;font-size:11px;text-overflow:ellipsis;white-space:nowrap}
  .master-table .tr,.honor-table .tr{gap:5px 9px;padding:9px}.master-table .tr>[data-label]::before,.honor-table .tr>[data-label]::before{font-size:11px}
}
@media(max-width:700px){.faction-total-grid{grid-template-columns:1fr}.faction-final-totals>header,.honor-group>header{align-items:flex-start;padding:11px 12px}.faction-total-grid article{padding:12px}.faction-total-grid article>div{gap:5px}.faction-total-grid article span{padding:8px}.faction-total-grid article b{font-size:14px}.honor-season{grid-template-columns:1fr;gap:8px;padding:11px 12px}.honor-winners{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px}.honor-winners>span{justify-content:space-between}}
@media(max-width:420px){.faction-total-grid article>div,.honor-winners{grid-template-columns:1fr}.faction-final-totals>header>span,.honor-group>header>span{font-size:11px}}
.faction-total-grid .history-season-details{min-width:0;padding:0;background:#0a1118}.history-season-details>summary{cursor:pointer;list-style:none;font-size:15px;font-weight:900}.history-season-details>summary::-webkit-details-marker{display:none}.history-season-details>summary::after{content:'▾';float:right;color:#8e9ba0}.history-season-details:not([open])>summary::after{content:'▸'}.faction-total-grid .history-season-details>summary{padding:14px 16px}.faction-total-values{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;padding:0 16px 14px}.faction-total-values span{display:grid;min-width:0;gap:4px;padding:10px;border:1px solid #2b3841;background:#0d1720}.faction-total-values small{color:#7d8b92}.faction-total-values b{overflow-wrap:anywhere;color:#efd375;font-size:17px}.honor-season.history-season-details{display:block}.honor-season>summary{padding:0}.honor-season .honor-winners{margin-top:10px}.honor-master-profile{width:42px;height:42px;flex:0 0 42px;border:1px solid #66747b;object-fit:cover}.honor-group>header>:deep(.ranked-identity-badge){min-width:0}.honor-group>header>span{white-space:nowrap}
@media(max-width:700px){.faction-total-values{gap:5px;padding:0 12px 12px}.faction-total-values span{padding:8px}.faction-total-values b{font-size:14px}.honor-master-profile{width:34px;height:34px;flex-basis:34px}}
@media(max-width:420px){.faction-total-values{grid-template-columns:repeat(3,minmax(0,1fr))}}
</style>

