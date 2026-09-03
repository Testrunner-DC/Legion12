<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { rankedApi, type RankedLeaderboardEntry, type RankedMasterChampion } from '@/l12/platform'
const faction = ref('')
const players = ref<RankedLeaderboardEntry[]>([])
const masterChampions = ref<RankedMasterChampion[]>([])
const loading = ref(false)
const error = ref('')
const filters = [{ id: '', name: '全服' }, { id: 'order', name: '秩序' }, { id: 'chaos', name: '混沌' }, { id: 'fate', name: '命运' }]
async function load() { loading.value = true; error.value = ''; try { const response = await rankedApi.leaderboard(faction.value); players.value = response.players; masterChampions.value = response.masterChampions } catch (cause) { error.value = cause instanceof Error ? cause.message : '排行榜加载失败' } finally { loading.value = false } }
watch(faction, load)
onMounted(load)
</script>
<template>
  <div class="ranking-page">
    <header class="page-head"><div><small>RANKED · CURRENT SEASON</small><h1>排位排行榜</h1><p>仅统计当前赛季排位匹配；休闲、好友、赛事与沙盒均不计入。</p></div><button @click="load">刷新</button></header>
    <nav><button v-for="item in filters" :key="item.id" :class="{ active: faction === item.id }" @click="faction = item.id">{{ item.name }}</button></nav>
    <p v-if="error" class="error">{{ error }}</p>
    <section v-if="masterChampions.length" class="master-champions"><header><small>MASTER CHAMPIONS</small><h2>主宰最强玩家</h2><p>按使用该主宰参加过排位的已定级玩家七曜值评定。</p></header><div><article v-for="row in masterChampions" :key="row.masterId"><small>{{ row.masterId }}</small><b>{{ row.title }}</b><strong>{{ row.username }}</strong><span>{{ row.masterName }} · {{ row.displayValue }}</span><em>{{ row.wins }}胜 / {{ row.games }}场</em></article></div></section>
    <section class="rank-panel"><div class="thead"><span>排名</span><span>玩家</span><span>派系</span><span>段位</span><span>七曜</span><span>战绩</span><span>连胜</span></div><div v-for="row in players" :key="`${row.rank}-${row.username}-${row.faction}`" class="tr"><b>#{{ row.rank }}</b><strong>{{ row.username }}<span v-if="row.titles?.length" class="title-list"><small v-for="title in row.titles" :key="title">{{ title }}</small></span><small v-else-if="row.title">{{ row.title }}</small></strong><span>{{ row.faction }}</span><span>{{ row.tier }}</span><strong>{{ row.displayValue }}</strong><span>{{ row.wins }}胜 {{ row.losses }}负</span><span>{{ row.winStreak }}</span></div><div v-if="!players.length" class="empty">{{ loading ? '正在读取排位数据…' : '当前筛选下暂无完成定级的玩家' }}</div></section>
  </div>
</template>
<style scoped>
.ranking-page{min-height:100%;padding:28px clamp(16px,3vw,44px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px}.page-head small{color:#53c3ca;font:900 9px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0;font-size:30px}.page-head p{margin:0;color:#77858b;font-size:11px}.page-head button,nav button{padding:10px 14px;border:1px solid #36434c;background:#091016;color:#879399;font-weight:900}nav{display:flex;width:max-content;margin:20px 0 12px}nav button.active{border-color:#c7a64b;background:#302712;color:#f4d77c}.master-champions{margin-bottom:12px;padding:16px;border:1px solid #4c4632;background:#101611}.master-champions header small{color:#d2b85f;font:900 9px monospace;letter-spacing:.16em}.master-champions h2{margin:4px 0;font-size:18px}.master-champions header p{margin:0 0 12px;color:#7f8d90;font-size:10px}.master-champions>div{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:7px}.master-champions article{display:flex;flex-direction:column;gap:3px;padding:10px;border:1px solid #303b3d;background:#091016}.master-champions article>small{color:#69777d;font:700 8px monospace}.master-champions article>b{color:#e6c86d;font-size:12px}.master-champions article>strong{font-size:11px}.master-champions article>span,.master-champions article>em{color:#849196;font-size:9px;font-style:normal}.rank-panel{min-width:780px;border:1px solid #35424a;background:#0d151d}.thead,.tr{display:grid;grid-template-columns:70px 1.5fr .7fr .8fr 1fr .8fr .5fr;align-items:center;min-height:56px;padding:0 18px;border-bottom:1px solid rgba(235,230,216,.09)}.thead{min-height:42px;color:#66757c;font-size:9px;font-weight:900}.tr{font-size:12px}.tr>b{color:#e3c473}.tr strong small{display:block;margin-top:3px;color:#c9a94f;font-size:8px}.title-list{display:flex;flex-wrap:wrap;gap:4px}.title-list small{padding:2px 5px;border:1px solid #5f512c;background:#211d11}.empty{display:grid;min-height:360px;place-items:center;color:#738088}.error{padding:10px;border-left:3px solid #b83240;background:#251017;color:#e69aa1}@media(max-width:800px){.ranking-page{overflow:auto;padding:18px 12px}.page-head{align-items:flex-start;flex-direction:column}.rank-panel{min-width:720px}}
</style>
