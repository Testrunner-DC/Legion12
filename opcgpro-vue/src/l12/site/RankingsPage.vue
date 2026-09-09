<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { masterProfileUrl } from '@/l12/specialAssets'
import RankedIdentityBadge from '@/l12/RankedIdentityBadge.vue'
import RankedMasterTitleRulesModal from './RankedMasterTitleRulesModal.vue'
import {
  platformState,
  rankedApi,
  type RankedAnalytics,
  type RankedLeaderboardEntry,
  type RankedMasterStats,
  type RankedMatchupStats,
  type RankedSeasonHonor,
} from '@/l12/platform'

type RankingTab = 'players' | 'masters' | 'matchups' | 'history'
type RankingRange = '7d' | '30d' | 'season'
type PlayerLeaderboardEntry = RankedLeaderboardEntry & {
  favoriteMasterId?: string | null
  favoriteMasterName?: string | null
}

const faction = ref('')
const range = ref<RankingRange>('season')
const tab = ref<RankingTab>('players')
const search = ref('')
const players = ref<PlayerLeaderboardEntry[]>([])
const honors = ref<RankedSeasonHonor[]>([])
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
    const [response, history] = await Promise.all([rankedApi.leaderboard(requestedFaction, requestedRange), rankedApi.history()])
    if (disposed || requestedFaction !== faction.value || requestedRange !== range.value) return
    players.value = response.players as PlayerLeaderboardEntry[]
    analytics.value = response.analytics
    honors.value = history
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
const visibleMasters = computed(() => query.value
  ? analytics.value.masters.filter(row => `${row.masterName} ${row.masterId} ${row.strongestPlayer ?? ''} ${row.title ?? ''}`.toLocaleLowerCase().includes(query.value))
  : analytics.value.masters)
const matrixMasters = computed(() => visibleMasters.value)
const matrixMasterColumnWidth = '银臂努阿达'.length * 14 + 44
const visibleHonors = computed(() => {
  const factionName = filters.find(item => item.id === faction.value)?.name
  const rows = faction.value ? honors.value.filter(row => row.faction === factionName) : honors.value
  return query.value ? rows.filter(row => `${row.seasonName} ${row.username} ${row.faction} ${row.tier} ${row.titles.join(' ')}`.toLocaleLowerCase().includes(query.value)) : rows
})
const matchupIndex = computed(() => new Map(analytics.value.matchups.map(row => [`${row.masterId}|${row.opponentMasterId}`, row])))
const updatedAt = computed(() => analytics.value.summary.updatedAt
  ? new Date(analytics.value.summary.updatedAt).toLocaleString() : '暂无数据')

function matchup(masterId: string, opponentId: string): RankedMatchupStats | undefined {
  return matchupIndex.value.get(`${masterId}|${opponentId}`)
}
function percent(value: number) { return `${value.toFixed(1)}%` }
function cellTone(row: RankedMasterStats, opponent: RankedMasterStats) {
  if (row.masterId === opponent.masterId) return 'mirror'
  const value = matchup(row.masterId, opponent.masterId)?.winRate
  if (value === undefined) return 'no-data'
  return value > 50 ? 'advantage' : value < 50 ? 'disadvantage' : 'even'
}

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
      <input v-model="search" class="ranking-search" :placeholder="tab === 'players' ? '搜索玩家、段位或称号' : tab === 'history' ? '搜索赛季、玩家或称号' : '搜索主宰或最强玩家'">
    </section>

    <nav v-if="tab === 'players' || tab === 'history'" class="faction-filter"><button v-for="item in filters" :key="item.id" :class="{ active: faction === item.id }" @click="faction = item.id">{{ item.name }}</button></nav>
    <p v-if="error" class="error">{{ error }}</p>

    <section v-if="tab === 'players'" class="rank-panel player-table">
      <div class="thead"><span>排名</span><span>昵称</span><span>阵营</span><span>段位</span><span>称号</span><span>最擅长主宰</span><span>七曜值</span><span>场次</span><span>战绩</span><span>胜率</span></div>
      <div v-for="row in visiblePlayers" :key="`${row.rank}-${row.username}-${row.faction}`" class="tr" :class="[`rank-${Math.min(row.rank, 4)}`, { 'is-me': row.username === platformState.account?.username }]">
        <b>#{{ row.rank }}</b>
        <strong class="player-name"><span class="username">{{ row.username }} <i v-if="row.username === platformState.account?.username" class="me-badge">我</i></span></strong>
        <span>{{ row.faction }}</span><span>{{ row.tier }}</span>
        <span class="title-list player-title-cell"><RankedIdentityBadge v-for="title in row.titles" :key="title" class="title-badge" :label="title"/><span v-if="!row.titles?.length">—</span></span>
        <span v-if="row.favoriteMasterId" class="player-master"><img class="player-master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(row.favoriteMasterId)" :alt="`${row.favoriteMasterName || row.favoriteMasterId}头像`"/><b>{{ row.favoriteMasterName || row.favoriteMasterId }}</b></span><span v-else>—</span>
        <strong>{{ row.displayValue }}</strong><span>{{ row.wins + row.losses }}</span>
        <span><i>{{ row.wins }}</i>胜 <em>{{ row.losses }}</em>负</span><strong>{{ percent((row.wins + row.losses) ? row.wins * 100 / (row.wins + row.losses) : 0) }}</strong>
      </div>
      <div v-if="!visiblePlayers.length" class="empty">{{ loading ? '正在读取排位数据…' : '当前筛选下暂无完成定级的玩家' }}</div>
    </section>

    <section v-else-if="tab === 'masters'" class="rank-panel master-table">
      <div class="thead"><span>排名</span><span>主宰</span><span>最强玩家</span><span>场次</span><span>战绩</span><span>胜率</span><span>使用率</span><span>先手</span><span>后手</span></div>
      <div v-for="row in visibleMasters" :key="row.masterId" class="tr" :class="`rank-${Math.min(row.rank, 4)}`">
        <b>#{{ row.rank }}</b>
        <span class="master-card"><img class="master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(row.masterId)" :alt="`${row.masterName}头像`"/><strong>{{ row.masterName }}<small>{{ row.masterId }}</small></strong></span>
        <span class="champion"><b v-if="row.title" class="champion-title"><i>♛</i><span>{{ row.title }}</span></b><strong>{{ row.strongestPlayer || '尚未产生' }}</strong></span>
        <strong>{{ row.games }}</strong><span><i>{{ row.wins }}</i>胜 <em>{{ row.losses }}</em>负</span><b class="rate">{{ percent(row.winRate) }}</b><span>{{ percent(row.usageRate) }}</span>
        <span>{{ percent(row.firstWinRate) }}<small>{{ row.firstWins }}/{{ row.firstGames }}</small></span><span>{{ percent(row.secondWinRate) }}<small>{{ row.secondWins }}/{{ row.secondGames }}</small></span>
      </div>
      <div v-if="!visibleMasters.length" class="empty">{{ loading ? '正在聚合主宰数据…' : '当前范围暂无主宰数据' }}</div>
    </section>

    <section v-else-if="tab === 'history'" class="rank-panel honor-table">
      <div class="thead"><span>赛季</span><span>获奖玩家</span><span>派系</span><span>赛季段位</span><span>赛季七曜值</span><span>获得称号</span></div>
      <div v-for="row in visibleHonors" :key="`${row.seasonId}-${row.username}-${row.titles.join('|')}`" class="tr">
        <strong>{{ row.seasonName }}<small>{{ row.seasonId }}</small></strong><b>{{ row.username }}</b><span>{{ row.faction }}</span><span>{{ row.tier }}</span><strong>{{ row.displayValue }}</strong><span class="title-list"><RankedIdentityBadge v-for="title in row.titles" :key="title" class="title-badge" :label="title"/></span>
      </div>
      <div v-if="!visibleHonors.length" class="empty">尚无已经结算并冻结的历史赛季称号</div>
    </section>

    <section v-else class="matrix-panel">
      <header><div><small>MASTER MATCHUPS</small><h2>主宰对阵一览</h2><p>纵轴为我方、横轴为对方；绿色优势、红色劣势，先后手数据悬停可见。</p></div><span>当前 {{ matrixMasters.length }} 位主宰</span></header>
      <div v-if="matrixMasters.length" class="matrix-scroll">
        <div class="matrix-grid" :style="{ gridTemplateColumns: `64px repeat(${matrixMasters.length + 1}, ${matrixMasterColumnWidth}px)` }">
          <div class="matrix-rank-head">排名</div>
          <div class="matrix-corner">我方 ↓<br>对方 →</div>
          <div v-for="column in matrixMasters" :key="`head-${column.masterId}`" class="matrix-head"><img class="matrix-master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(column.masterId)" :alt="`${column.masterName}头像`"/><span>{{ column.masterName }}</span></div>
          <template v-for="row in matrixMasters" :key="`row-${row.masterId}`">
            <div class="matrix-rank-cell"><b>#{{ row.rank }}</b><span>{{ percent(row.winRate) }}</span></div>
            <div class="matrix-row-head"><img class="matrix-master-avatar" data-ui-contract="ranking-master-avatar" :src="masterProfileUrl(row.masterId)" :alt="`${row.masterName}头像`"/><b>{{ row.masterName }}</b></div>
            <div v-for="column in matrixMasters" :key="`${row.masterId}-${column.masterId}`" class="matrix-cell" :class="cellTone(row, column)" :title="row.masterId === column.masterId ? '同主宰镜像' : matchup(row.masterId, column.masterId) ? `共 ${matchup(row.masterId, column.masterId)!.games} 场；先手 ${matchup(row.masterId, column.masterId)!.firstWins}/${matchup(row.masterId, column.masterId)!.firstGames}；后手 ${matchup(row.masterId, column.masterId)!.secondWins}/${matchup(row.masterId, column.masterId)!.secondGames}` : '暂无对局'">
              <template v-if="row.masterId === column.masterId"><b>镜像</b></template>
              <template v-else-if="matchup(row.masterId, column.masterId)"><b>{{ percent(matchup(row.masterId, column.masterId)!.winRate) }}</b><span>{{ matchup(row.masterId, column.masterId)!.wins }} / {{ matchup(row.masterId, column.masterId)!.games }}</span></template>
              <template v-else><b>等待</b><span>更多对局</span></template>
            </div>
          </template>
        </div>
      </div>
      <div v-else class="empty">{{ loading ? '正在生成对阵矩阵…' : '当前范围暂无对阵数据' }}</div>
    </section>
    <RankedMasterTitleRulesModal v-model="masterTitleRulesOpen"/>
  </div>
</template>

<style scoped>
.ranking-page{min-height:100%;padding:28px clamp(16px,3vw,44px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif;color:#eef1ed}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px}.page-head small,.matrix-panel header small{color:#53c3ca;font:900 14px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0;font-size:30px}.page-head p,.matrix-panel header p{margin:0;color:#77858b;font-size:14px}.page-head button,.toolbar button,.faction-filter button{padding:10px 14px;border:1px solid #36434c;background:#091016;color:#879399;font-weight:900}.page-head button:disabled{opacity:.45}.summary-strip{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;margin:20px 0 12px}.summary-strip article{display:grid;gap:4px;min-height:86px;padding:14px;border:1px solid #303e48;background:linear-gradient(135deg,#101a23,#0a1016)}.summary-strip small{color:#72828b;font:800 14px monospace}.summary-strip strong{color:#efd375;font-size:24px}.summary-strip strong.updated{font-size:14px}.summary-strip span{color:#697880;font-size:14px}.toolbar{display:grid;grid-template-columns:auto auto minmax(190px,1fr);align-items:center;gap:10px;padding:10px;border:1px solid #2f3b45;background:#0c141d}.tabs,.ranges,.faction-filter{display:flex}.toolbar button.active,.faction-filter button.active{border-color:#c7a64b;background:#392e13;color:#f6d978}.toolbar input{min-width:0;padding:10px 12px;border:1px solid #36434c;background:#070c11;color:#e7ecea}.faction-filter{width:max-content;margin:12px 0}.rank-panel{overflow:hidden;border:1px solid #35424a;background:#0a1118}.thead,.tr{display:grid;align-items:center;min-height:58px;padding:6px 18px;border-bottom:1px solid rgba(235,230,216,.09)}.player-table .thead,.player-table .tr{grid-template-columns:64px 1.6fr .65fr .8fr 1fr .8fr .65fr .45fr}.master-table .thead,.master-table .tr{grid-template-columns:58px 1.35fr 1.25fr .45fr .75fr .55fr .55fr .65fr .65fr}.thead{min-height:42px;padding-top:0;padding-bottom:0;color:#66757c;font-size:14px;font-weight:900}.tr{position:relative;font-size:14px}.tr:hover{background:#111c26}.tr>b:first-child{color:#d9dde1;font-size:15px}.tr.rank-1>b:first-child{color:#ffb239;text-shadow:0 0 12px #ff9f2c99}.tr.rank-2>b:first-child{color:#e1e8ef}.tr.rank-3>b:first-child{color:#c98d63}.tr i{font-style:normal}.tr em{color:#ee6c78;font-style:normal}.player-name{display:grid;justify-items:start;gap:7px}.username{font-size:14px}.title-list{display:flex;flex-wrap:wrap;gap:6px}.title-badge,.champion-title{position:relative;display:inline-flex!important;align-items:center;width:max-content;margin:0!important;border:1px solid #f1bd4a!important;border-radius:5px;background:linear-gradient(135deg,#b47716 0%,#6f3d08 48%,#3a1d02 100%)!important;color:#fff4b5!important;font-weight:900;letter-spacing:.04em;box-shadow:0 0 0 1px #5b3208,0 0 16px #e8a12f78,inset 0 1px #fff1a477;text-shadow:0 1px 2px #000}.title-badge{gap:5px;padding:5px 10px;font-size:14px!important}.title-badge i,.champion-title i{color:#fff0a0;filter:drop-shadow(0 0 4px #ffd047)}.master-card{display:flex;align-items:center;gap:9px}.master-avatar{width:40px;height:40px;border:1px solid #66747b;border-radius:50%;background:#080d11;object-fit:cover}.master-card strong,.master-card small,.champion strong,.master-table .tr>span>small{display:block}.master-card small,.master-table .tr>span>small{margin-top:3px;color:#687880;font:700 14px monospace}.champion{display:grid;justify-items:start;gap:6px}.champion-title{gap:7px;padding:6px 11px;font-size:14px!important}.champion-title i{font-size:14px}.champion>strong{padding-left:2px;color:#f8e4a2}.rate{color:#f0c86a}.matrix-panel{border:1px solid #35424a;background:#091018}.matrix-panel>header{display:flex;align-items:flex-end;justify-content:space-between;padding:16px;border-bottom:1px solid #35424a}.matrix-panel h2{margin:4px 0;font-size:18px}.matrix-panel header>span{color:#809098;font-size:14px}.matrix-scroll{max-height:68vh;overflow:auto}.matrix-grid{display:grid;grid-auto-rows:62px;width:max-content;min-width:100%}.matrix-rank-head,.matrix-rank-cell,.matrix-corner,.matrix-head,.matrix-row-head,.matrix-cell{box-sizing:border-box;height:62px;min-height:62px;max-height:62px;overflow:hidden;border-right:1px solid #27343e;border-bottom:1px solid #27343e}.matrix-rank-head{position:sticky;z-index:7;top:0;left:0;display:grid;place-items:center;background:#101b27;color:#758994;font-size:14px}.matrix-corner{position:sticky;z-index:6;top:0;left:64px;display:grid;place-items:center;background:#101b27;color:#758994;font-size:14px}.matrix-head{position:sticky;z-index:4;top:0;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;background:#101b27}.matrix-master-avatar{width:30px;height:30px;border:1px solid #58666e;border-radius:50%;background:#080d11;object-fit:cover}.matrix-head span{max-width:96px;overflow:hidden;color:#c3ccd0;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.matrix-rank-cell{position:sticky;z-index:5;left:0;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;background:#0d1720}.matrix-rank-cell b{color:#e7c864}.matrix-rank-cell span{color:#829098}.matrix-row-head{position:sticky;z-index:3;left:64px;display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;padding:3px;background:#101b27}.matrix-row-head .matrix-master-avatar{width:30px;height:30px}.matrix-row-head b{max-width:96px;overflow:hidden;font-size:14px;text-overflow:ellipsis;white-space:nowrap}.matrix-cell{display:flex;align-items:center;flex-direction:column;justify-content:center;gap:3px;background:#101923}.matrix-cell b{font-size:14px}.matrix-cell span{color:#8a989e;font-size:14px}.matrix-cell.advantage{background:#0b352d}.matrix-cell.advantage b{color:#62e6b4}.matrix-cell.disadvantage{background:#36131e}.matrix-cell.disadvantage b{color:#ff8494}.matrix-cell.even{background:#2d2b17}.matrix-cell.even b{color:#ead56e}.matrix-cell.mirror{background:#121923;color:#53636c}.empty{display:grid;min-height:280px;place-items:center;color:#738088}.error{padding:10px;border-left:3px solid #b83240;background:#251017;color:#e69aa1}
@media(max-width:1050px){.summary-strip{grid-template-columns:1fr 1fr}.toolbar{grid-template-columns:1fr}.tabs,.ranges{width:100%}.tabs button,.ranges button{flex:1}.player-table,.master-table{overflow:auto}.player-table .thead,.player-table .tr{min-width:880px}.master-table .thead,.master-table .tr{min-width:980px}}
@media(max-width:700px){.ranking-page{padding:18px 10px 40px}.page-head{align-items:flex-start;flex-direction:column}.summary-strip{grid-template-columns:1fr 1fr}.summary-strip strong{font-size:19px}.player-name{align-items:flex-start;flex-direction:column}.matrix-scroll{max-height:72vh}}
.tr.is-me{background:linear-gradient(90deg,#122c32,#111824);box-shadow:inset 3px 0 #55c7ce}.me-badge{display:inline-grid;min-width:18px;height:18px;place-items:center;margin-left:5px;border-radius:50%;background:#55c7ce;color:#061012;font-size:14px;font-style:normal}
.honor-table .thead,.honor-table .tr{grid-template-columns:1.1fr 1fr .6fr .7fr .9fr 1.7fr}.honor-table .tr>strong:first-child small{display:block;margin-top:4px;color:#687880;font:700 14px monospace}.honor-table .title-list{display:flex;flex-wrap:wrap;gap:6px}
.page-actions{display:flex;gap:8px}.page-actions button:first-child{border-color:#a98d3f;color:#efd477}
.player-table .thead,.player-table .tr{grid-template-columns:56px minmax(110px,.9fr) .55fr .65fr minmax(150px,1.25fr) minmax(130px,1fr) .8fr .45fr .68fr .58fr}
.master-avatar,.matrix-master-avatar{border-radius:0}
.player-table{overflow-x:auto}.player-title-cell{align-content:center}.player-title-cell>span{color:#697880}.player-master{display:flex;align-items:center;gap:8px;min-width:0}.player-master-avatar{width:34px;height:34px;flex:0 0 34px;border:1px solid #66747b;border-radius:0;background:#080d11;object-fit:cover}.player-master b{overflow:hidden;font-size:14px;text-overflow:ellipsis;white-space:nowrap}
@media(max-width:1050px){.player-table .thead,.player-table .tr{min-width:1180px}}
.toolbar{display:flex;flex-wrap:wrap}.toolbar .tabs,.toolbar .ranges{flex:none}.toolbar .ranking-search{flex:0 1 360px;width:clamp(220px,24vw,380px);margin-left:auto}.toolbar .master-title-rules-button{flex:none}
.matrix-rank-cell span,.matrix-cell span,.matrix-cell b{max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.matrix-head span,.matrix-row-head b{max-width:96px}
.ranking-page{--ranking-master-avatar:44px}.player-table .tr,.master-table .tr{min-height:68px;padding-block:7px}.master-avatar,.player-master-avatar,.matrix-master-avatar,.matrix-row-head .matrix-master-avatar{box-sizing:border-box;width:var(--ranking-master-avatar);height:var(--ranking-master-avatar);min-width:var(--ranking-master-avatar);max-width:var(--ranking-master-avatar);flex:0 0 var(--ranking-master-avatar);border-radius:0;object-fit:cover}.matrix-grid{grid-auto-rows:76px}.matrix-rank-head,.matrix-rank-cell,.matrix-corner,.matrix-head,.matrix-row-head,.matrix-cell{height:76px;min-height:76px;max-height:76px}.matrix-head,.matrix-row-head{gap:5px}.matrix-head span,.matrix-row-head b{max-width:calc(100% - 8px)}
</style>
