<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { platformRequest } from '@/l12/platform'

interface RankingMatch { player0: string; player1: string; startedUtc: string; endedUtc: string; winner: number | null; master0: string; master1: string; firstPlayer: number }
interface PlayerRow { name: string; games: number; wins: number; losses: number }
interface MasterGame { master0: string; master1: string; winner: number | null; firstPlayer: number }

const tab = ref<'players' | 'masters'>('players')
const masterView = ref<'table' | 'matrix'>('table')
const period = ref<'7' | '30' | 'all'>('30')
const loading = ref(false)
const error = ref('')
const summaries = ref<RankingMatch[]>([])
const masterGames = ref<MasterGame[]>([])
const masterNames = ref<string[]>([])

onMounted(async () => {
  loading.value = true
  try {
    summaries.value = await platformRequest<RankingMatch[]>('/api/rankings?limit=500')
    masterGames.value = summaries.value.filter(match => match.master0 && match.master1)
    masterNames.value = [...new Set(masterGames.value.flatMap(match => [match.master0, match.master1]))]
  } catch (reason) { error.value = reason instanceof Error ? reason.message : '排行榜加载失败' } finally { loading.value = false }
})

const cutoff = computed(() => period.value === 'all' ? 0 : Date.now() - Number(period.value) * 86400000)
const periodMatches = computed(() => summaries.value.filter(match => match.endedUtc && new Date(match.startedUtc).getTime() >= cutoff.value))
const players = computed<PlayerRow[]>(() => {
  const map = new Map<string, PlayerRow>()
  periodMatches.value.forEach(match => [match.player0, match.player1].forEach((name, index) => {
    const row = map.get(name) || { name, games: 0, wins: 0, losses: 0 }; row.games++; if (match.winner === index) row.wins++; else if (match.winner !== null && match.winner !== undefined) row.losses++; map.set(name, row)
  }))
  return [...map.values()].sort((a,b) => b.wins / Math.max(1,b.games) - a.wins / Math.max(1,a.games) || b.games - a.games)
})
const masterRows = computed(() => masterNames.value.map(name => {
  const games = masterGames.value.filter(game => game.master0 === name || game.master1 === name)
  const wins = games.filter(game => (game.master0 === name ? 0 : 1) === game.winner).length
  const firstGames = games.filter(game => (game.master0 === name ? 0 : 1) === game.firstPlayer)
  const firstWins = firstGames.filter(game => (game.master0 === name ? 0 : 1) === game.winner).length
  const secondGames = games.filter(game => (game.master0 === name ? 0 : 1) !== game.firstPlayer)
  const secondWins = secondGames.filter(game => (game.master0 === name ? 0 : 1) === game.winner).length
  return { name, games: games.length, wins, losses: games.length - wins, rate: games.length ? wins / games.length : null, firstRate: firstGames.length ? firstWins / firstGames.length : null, secondRate: secondGames.length ? secondWins / secondGames.length : null }
}).filter(row => row.games > 0).sort((a,b) => (b.rate ?? 0) - (a.rate ?? 0) || b.games - a.games))
const matrixMasters = computed(() => masterRows.value.slice(0, 20).map(row => row.name))
function matrixCell(row: string, column: string) {
  if (row === column) return null
  const games = masterGames.value.filter(game => (game.master0 === row && game.master1 === column) || (game.master1 === row && game.master0 === column))
  if (!games.length) return null
  const wins = games.filter(game => (game.master0 === row ? 0 : 1) === game.winner).length
  return { games: games.length, rate: wins / games.length }
}
const percent = (value: number | null) => value === null ? '—' : `${(value * 100).toFixed(1)}%`
const refreshPage = () => window.location.reload()
</script>

<template>
  <div class="ranking-page">
    <header class="page-head"><div><small>RANKINGS</small><h1>排行榜</h1><p>仅统计服务器中已完成的真实对局；不生成模拟排名数据。</p></div><div class="period"><button v-for="value in ['7','30','all'] as const" :key="value" :class="{ active: period === value }" @click="period = value">{{ value === 'all' ? '全部' : `近 ${value} 天` }}</button><button @click="refreshPage">刷新</button></div></header>
    <div class="ranking-tabs"><button :class="{ active: tab === 'players' }" @click="tab = 'players'">玩家排行榜</button><button :class="{ active: tab === 'masters' }" @click="tab = 'masters'">主宰排行榜</button></div>
    <p v-if="error" class="error">{{ error }}</p>
    <section v-if="tab === 'players'" class="rank-panel"><header><span>有效对局 <b>{{ periodMatches.length }}</b></span><span>当前仅统计胜负与胜率</span></header><div v-if="players.length" class="rank-table player-table"><div class="thead"><span>排名</span><span>玩家</span><span>场次</span><span>战绩</span><span>胜率</span></div><div v-for="(row,index) in players" :key="row.name" class="tr"><b>#{{ index + 1 }}</b><strong>{{ row.name }}</strong><span>{{ row.games }}</span><span><i>{{ row.wins }}</i> - <em>{{ row.losses }}</em></span><strong>{{ percent(row.wins / row.games) }}</strong></div></div><div v-else class="empty">{{ loading ? '正在统计对局…' : '当前时间范围内没有已完成对局' }}</div></section>
    <section v-else class="rank-panel"><div class="master-tools"><div><button :class="{ active: masterView === 'table' }" @click="masterView = 'table'">综合榜</button><button :class="{ active: masterView === 'matrix' }" @click="masterView = 'matrix'">对阵一图流</button></div><span>矩阵最多展示综合场次前 20 名主宰</span></div><div v-if="masterView === 'table' && masterRows.length" class="rank-table master-table"><div class="thead"><span>排名</span><span>主宰</span><span>场次</span><span>战绩</span><span>胜率</span><span>先攻</span><span>后攻</span></div><div v-for="(row,index) in masterRows" :key="row.name" class="tr"><b>#{{ index + 1 }}</b><strong>{{ row.name }}</strong><span>{{ row.games }}</span><span><i>{{ row.wins }}</i> - <em>{{ row.losses }}</em></span><strong>{{ percent(row.rate) }}</strong><span>{{ percent(row.firstRate) }}</span><span>{{ percent(row.secondRate) }}</span></div></div><div v-else-if="masterView === 'table'" class="empty">{{ loading ? '正在读取主宰数据…' : '没有可统计的主宰对局' }}</div><div v-else-if="matrixMasters.length" class="matrix-wrap"><table><thead><tr><th>我方 ↓<br/>对手 →</th><th v-for="master in matrixMasters" :key="master">{{ master }}</th></tr></thead><tbody><tr v-for="master in matrixMasters" :key="master"><th>{{ master }}</th><td v-for="opponent in matrixMasters" :key="opponent" :class="matrixCell(master,opponent) ? (matrixCell(master,opponent)!.rate > .55 ? 'good' : matrixCell(master,opponent)!.rate < .45 ? 'bad' : 'even') : ''"><template v-if="matrixCell(master,opponent)"><b>{{ percent(matrixCell(master,opponent)!.rate) }}</b><span>{{ matrixCell(master,opponent)!.games }} 场</span></template><span v-else>—</span></td></tr></tbody></table></div><div v-else class="empty">没有足够数据生成主宰对阵矩阵</div></section>
  </div>
</template>

<style scoped>
.ranking-page{min-height:100%;padding:28px clamp(16px,3vw,44px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px}.page-head small{color:#53c3ca;font:900 9px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0;font-size:30px}.page-head p{margin:0;color:#77858b;font-size:11px}.period,.ranking-tabs,.master-tools>div{display:flex;border:1px solid #36434c;background:#091016}.period button,.ranking-tabs button,.master-tools button{padding:10px 14px;border:0;background:transparent;color:#79868d;font-size:10px;font-weight:900}.period button.active,.ranking-tabs button.active,.master-tools button.active{background:#9f2834;color:#fff}.ranking-tabs{width:max-content;margin:20px 0 12px}.rank-panel{border:1px solid #35424a;background:#0d151d}.rank-panel>header,.master-tools{display:flex;align-items:center;justify-content:space-between;padding:14px 16px;border-bottom:1px solid #35424a;color:#758289;font-size:10px}.rank-panel>header b{color:#e3c474}.master-tools span{font-size:9px}.rank-table{min-width:760px}.thead,.tr{display:grid;align-items:center;min-height:54px;padding:0 18px;border-bottom:1px solid rgba(235,230,216,.09)}.player-table .thead,.player-table .tr{grid-template-columns:80px 1.5fr .7fr 1fr .8fr}.master-table .thead,.master-table .tr{grid-template-columns:70px 1.4fr .6fr .8fr .7fr .7fr .7fr}.thead{min-height:42px;color:#66757c;font-size:9px;font-weight:900}.tr{font-size:12px}.tr>b{color:#e3c473}.tr i{color:#51d3a0;font-style:normal}.tr em{color:#ef6873;font-style:normal}.empty{display:grid;min-height:420px;place-items:center;color:#738088;font-size:12px}.error{padding:10px;border-left:3px solid #b83240;background:#251017;color:#e69aa1}.matrix-wrap{max-width:100%;max-height:calc(100vh - 250px);overflow:auto}.matrix-wrap table{border-collapse:collapse;font-size:9px;white-space:nowrap}.matrix-wrap th,.matrix-wrap td{min-width:94px;height:64px;padding:7px;border:1px solid #27343c;text-align:center}.matrix-wrap th{position:sticky;z-index:2;left:0;background:#121c27;color:#d7dde0}.matrix-wrap thead th{z-index:3;top:0}.matrix-wrap thead th:first-child{z-index:4}.matrix-wrap td{background:#0c151d;color:#647179}.matrix-wrap td.good{background:#07382f;color:#70e6bd}.matrix-wrap td.bad{background:#39131e;color:#ff8994}.matrix-wrap td.even{background:#1a2330;color:#e4e7e8}.matrix-wrap td b,.matrix-wrap td span{display:block}.matrix-wrap td span{margin-top:4px;font-size:8px;opacity:.7}
@media(max-width:700px){.ranking-page{padding:18px 12px 48px}.page-head{align-items:flex-start;flex-direction:column}.period{width:100%;overflow:auto}.period button{flex:1}.ranking-tabs{width:100%}.ranking-tabs button{flex:1}.rank-panel{overflow:auto}.master-tools{align-items:flex-start;flex-direction:column;gap:10px}.rank-table{min-width:680px}}
</style>
