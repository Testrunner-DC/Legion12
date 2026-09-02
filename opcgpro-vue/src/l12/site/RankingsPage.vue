<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { rankedApi, type RankedLeaderboardEntry } from '@/l12/platform'
const faction = ref('')
const players = ref<RankedLeaderboardEntry[]>([])
const loading = ref(false)
const error = ref('')
const filters = [{ id: '', name: '全服' }, { id: 'order', name: '秩序' }, { id: 'chaos', name: '混沌' }, { id: 'fate', name: '命运' }]
async function load() { loading.value = true; error.value = ''; try { players.value = (await rankedApi.leaderboard(faction.value)).players } catch (cause) { error.value = cause instanceof Error ? cause.message : '排行榜加载失败' } finally { loading.value = false } }
watch(faction, load)
onMounted(load)
</script>
<template>
  <div class="ranking-page">
    <header class="page-head"><div><small>RANKED · CURRENT SEASON</small><h1>排位排行榜</h1><p>仅统计当前赛季排位匹配；休闲、好友、赛事与沙盒均不计入。</p></div><button @click="load">刷新</button></header>
    <nav><button v-for="item in filters" :key="item.id" :class="{ active: faction === item.id }" @click="faction = item.id">{{ item.name }}</button></nav>
    <p v-if="error" class="error">{{ error }}</p>
    <section class="rank-panel"><div class="thead"><span>排名</span><span>玩家</span><span>派系</span><span>段位</span><span>七曜</span><span>战绩</span><span>连胜</span></div><div v-for="row in players" :key="`${row.rank}-${row.username}-${row.faction}`" class="tr"><b>#{{ row.rank }}</b><strong>{{ row.username }}<small v-if="row.title">{{ row.title }}</small></strong><span>{{ row.faction }}</span><span>{{ row.tier }}</span><strong>{{ row.displayValue }}</strong><span>{{ row.wins }}胜 {{ row.losses }}负</span><span>{{ row.winStreak }}</span></div><div v-if="!players.length" class="empty">{{ loading ? '正在读取排位数据…' : '当前筛选下暂无完成定级的玩家' }}</div></section>
  </div>
</template>
<style scoped>
.ranking-page{min-height:100%;padding:28px clamp(16px,3vw,44px) 56px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px}.page-head small{color:#53c3ca;font:900 9px monospace;letter-spacing:.18em}.page-head h1{margin:5px 0;font-size:30px}.page-head p{margin:0;color:#77858b;font-size:11px}.page-head button,nav button{padding:10px 14px;border:1px solid #36434c;background:#091016;color:#879399;font-weight:900}nav{display:flex;width:max-content;margin:20px 0 12px}nav button.active{border-color:#c7a64b;background:#302712;color:#f4d77c}.rank-panel{min-width:780px;border:1px solid #35424a;background:#0d151d}.thead,.tr{display:grid;grid-template-columns:70px 1.5fr .7fr .8fr 1fr .8fr .5fr;align-items:center;min-height:56px;padding:0 18px;border-bottom:1px solid rgba(235,230,216,.09)}.thead{min-height:42px;color:#66757c;font-size:9px;font-weight:900}.tr{font-size:12px}.tr>b{color:#e3c473}.tr strong small{display:block;margin-top:3px;color:#c9a94f;font-size:8px}.empty{display:grid;min-height:360px;place-items:center;color:#738088}.error{padding:10px;border-left:3px solid #b83240;background:#251017;color:#e69aa1}@media(max-width:800px){.ranking-page{overflow:auto;padding:18px 12px}.page-head{align-items:flex-start;flex-direction:column}.rank-panel{min-width:720px}}
</style>
