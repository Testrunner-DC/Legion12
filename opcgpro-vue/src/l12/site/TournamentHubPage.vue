<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { tournamentApi, type TournamentCareer, type TournamentSummaryPage } from '@/l12/platform'
import { tournamentFormatText, tournamentStatusText } from '@/l12/tournamentLabels'
import TournamentCreateWizard from './TournamentCreateWizard.vue'
import TournamentSummaryList from './TournamentSummaryList.vue'

type Section = 'discover' | 'mine' | 'history' | 'host' | 'create'
const route = useRoute(); const router = useRouter()
const section = ref<Section>('discover'); const search = ref(''); const format = ref(''); const timeRange = ref('')
const loading = ref(false); const notice = ref(''); const page = ref(1)
const result = ref<TournamentSummaryPage>({ platformVersion: 0, items: [], page: 1, pageSize: 24, total: 0, totalPages: 0 })
const career = ref<TournamentCareer | null>(null)
const careerPage = ref(1)
let searchTimer: number | undefined
const onResource = () => { void load() }
const onVisibility = () => { if (document.visibilityState === 'visible') void load() }

async function load() {
  if (section.value === 'create') return
  loading.value = true; notice.value = ''
  try { const now = new Date(); const days = Number(timeRange.value); result.value = await tournamentApi.summaries({ section: section.value, search: search.value.trim(), format: format.value, page: page.value, pageSize: 24, startFrom: days ? now.toISOString() : undefined, startTo: days ? new Date(now.getTime() + days * 86_400_000).toISOString() : undefined }) }
  catch (error) { notice.value = error instanceof Error ? error.message : '赛事加载失败' }
  finally { loading.value = false }
}
async function loadCareer() {
  try { career.value = await tournamentApi.career({ page: careerPage.value, pageSize: 12 }) }
  catch (error) { notice.value = error instanceof Error ? error.message : '赛事履历加载失败' }
}
function switchSection(value: Section) { section.value = value; page.value = 1 }
function open(code: string) { void router.push(`/battle/tournaments/${encodeURIComponent(code)}`) }
watch([section, format, timeRange, page], load)
watch(careerPage, loadCareer)
watch(search, () => { window.clearTimeout(searchTimer); searchTimer = window.setTimeout(() => { page.value = 1; void load() }, 250) })
onMounted(async () => {
  window.addEventListener('l12-resource-tournaments', onResource); document.addEventListener('visibilitychange', onVisibility)
  if (typeof route.query.code === 'string' && route.query.code) { open(route.query.code); return }
  await Promise.all([load(), loadCareer()])
})
onBeforeUnmount(() => { window.removeEventListener('l12-resource-tournaments', onResource); document.removeEventListener('visibilitychange', onVisibility); window.clearTimeout(searchTimer) })
</script>

<template>
  <main class="hub-page">
    <header class="page-head">
      <div><small>TOURNAMENT CENTER</small><h1>赛事中心</h1><p>发现赛事、跟进自己的下一步，或创建并运营一场赛事。</p></div>
      <button class="primary create-button" type="button" @click="switchSection('create')">创建赛事</button>
    </header>
    <nav class="section-tabs" aria-label="赛事分类"><button v-for="item in ([['discover','发现赛事'],['mine','我的赛事'],['history','历史赛事'],['host','主办管理']] as const)" :key="item[0]" :class="{active: section === item[0]}" :aria-current="section === item[0] ? 'page' : undefined" @click="switchSection(item[0])">{{ item[1] }}</button></nav>
    <section v-if="section === 'create'" class="panel create-panel"><TournamentCreateWizard :platform-version="result.platformVersion" @created="value => open(value.code)" /></section>
    <template v-else>
      <template v-if="section === 'mine' && career">
        <section class="career" aria-label="赛事生涯概览"><article><small>参赛</small><b>{{ career.participated }}</b><span>累计赛事</span></article><article><small>主办</small><b>{{ career.organized }}</b><span>运营赛事</span></article><article><small>执裁</small><b>{{ career.refereed }}</b><span>裁判记录</span></article><article><small>总战绩</small><b>{{ career.wins }}胜 {{ career.losses }}负 {{ career.draws }}平</b><span>已完成对局</span></article></section>
        <section class="career-history panel"><header><div><small>CAREER</small><h2>个人赛事履历</h2></div><span>共 {{ career.total }} 条</span></header><button v-for="item in career.items" :key="item.tournamentId" type="button" @click="open(item.code)"><span class="career-name"><b>{{ item.name }}</b><small v-if="item.organized">主办</small><small v-else-if="item.refereed">裁判</small><small v-else>参赛者</small></span><span><small>状态与赛制</small>{{ tournamentStatusText(item.status) }} · {{ tournamentFormatText(item.format) }}</span><span><small>个人战绩</small>{{ item.wins }}胜 {{ item.losses }}负 {{ item.draws }}平<span v-if="item.finalRank"> · 最终第 {{ item.finalRank }} 名</span></span><b class="career-open" aria-hidden="true">→</b></button><footer v-if="career.totalPages > 1" class="pager"><button :disabled="careerPage <= 1" @click="careerPage--">上一页</button><span>第 {{ careerPage }} / {{ career.totalPages }} 页</span><button :disabled="careerPage >= career.totalPages" @click="careerPage++">下一页</button></footer></section>
      </template>
      <section class="filter-panel panel"><div class="filter-heading"><div><small>FILTERS</small><h2>{{ section === 'discover' ? '查找赛事' : section === 'mine' ? '我的当前赛事' : section === 'history' ? '历史赛事' : '我主办的赛事' }}</h2></div><b>共 {{ result.total }} 场</b></div><div class="toolbar"><label class="search-field"><span>搜索</span><input v-model="search" type="search" placeholder="赛事名称、代码或主办者"></label><label><span>赛制</span><select v-model="format"><option value="">全部赛制</option><option value="single">单败淘汰</option><option value="swiss">瑞士轮</option><option value="swiss-cut">瑞士轮 + Cut</option></select></label><label><span>时间</span><select v-model="timeRange"><option value="">全部时间</option><option value="7">未来 7 天</option><option value="30">未来 30 天</option></select></label></div></section>
      <p v-if="notice" class="notice" role="status" aria-live="polite">{{ notice }}</p>
      <TournamentSummaryList :items="result.items" :loading="loading" @open="open" />
      <footer v-if="result.totalPages > 1" class="pager"><button :disabled="page <= 1" @click="page--">上一页</button><span>第 {{ page }} / {{ result.totalPages }} 页</span><button :disabled="page >= result.totalPages" @click="page++">下一页</button></footer>
    </template>
  </main>
</template>

<style scoped>
.hub-page{box-sizing:border-box;max-width:1320px;margin:0 auto;padding:30px clamp(18px,3vw,42px) 64px;display:grid;gap:18px;font-family:'Microsoft YaHei','微软雅黑',sans-serif;color:var(--l12-ui-text)}.page-head{display:flex;align-items:flex-end;justify-content:space-between;gap:24px;padding-bottom:18px;border-bottom:1px solid var(--l12-ui-line)}.page-head h1{margin:5px 0;font-size:clamp(28px,3vw,38px);line-height:1.15}.page-head p{max-width:680px;margin:0;color:var(--l12-ui-text-muted);font-size:14px}.page-head small,.filter-heading small,.career-history header small{color:var(--l12-ui-info);font:900 12px ui-monospace,monospace;letter-spacing:.16em}.hub-page button{min-height:40px;border:1px solid var(--l12-ui-line-strong);border-radius:var(--l12-ui-radius-sm);background:var(--l12-ui-control);color:var(--l12-ui-text-soft);font:800 14px inherit;cursor:pointer}.hub-page button:disabled{cursor:not-allowed;opacity:.45}.create-button{min-width:126px;padding:0 20px}.section-tabs{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:5px;padding:5px;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-md);background:rgba(7,13,18,.86)}.section-tabs button{border-color:transparent;background:transparent}.section-tabs button:hover{background:var(--l12-ui-panel-raised);color:var(--l12-ui-text)}.section-tabs .active{border-color:var(--l12-ui-accent-line);background:#302713;color:#f2d779}.panel{padding:18px;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-md);background:linear-gradient(145deg,var(--l12-ui-panel-raised),var(--l12-ui-panel));box-shadow:var(--l12-ui-shadow-panel)}.create-panel{padding:clamp(16px,3vw,28px)}.career{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}.career article{display:grid;min-height:94px;gap:4px;padding:14px;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-sm);background:linear-gradient(145deg,var(--l12-ui-panel-raised),var(--l12-ui-panel))}.career small,.career span{color:var(--l12-ui-text-muted);font-size:12px}.career b{color:var(--l12-ui-accent);font-size:23px}.career article:last-child b{font-size:18px}.career-history{display:grid;gap:8px}.career-history>header,.filter-heading{display:flex;align-items:flex-end;justify-content:space-between;gap:14px;margin-bottom:4px}.career-history h2,.filter-heading h2{margin:3px 0 0;font-size:18px}.career-history>header>span,.filter-heading>b{color:var(--l12-ui-text-muted);font-size:13px}.career-history>button{display:grid;grid-template-columns:minmax(180px,1.1fr) minmax(150px,.75fr) minmax(170px,.8fr) 30px;gap:14px;align-items:center;padding:12px 14px;text-align:left}.career-history>button:hover{border-color:var(--l12-ui-accent-line);background:var(--l12-ui-panel-raised)}.career-history>button>span{display:grid;gap:3px;overflow-wrap:anywhere;color:var(--l12-ui-text-soft);font-weight:700}.career-history button small{color:var(--l12-ui-text-muted);font-size:11px;font-weight:700}.career-name{grid-template-columns:1fr auto!important;align-items:center}.career-name small{padding:3px 6px;border:1px solid #32767a;border-radius:999px;color:#87dce0!important}.career-open{color:var(--l12-ui-accent);font-size:19px}.filter-panel{display:grid;gap:14px}.toolbar{display:grid;grid-template-columns:minmax(240px,1fr) minmax(160px,.35fr) minmax(160px,.35fr);gap:10px}.toolbar label{display:grid;gap:6px}.toolbar label>span{color:var(--l12-ui-text-muted);font-size:12px;font-weight:800}.toolbar input,.toolbar select{width:100%;min-height:42px;padding:9px 11px}.notice{margin:0;padding:12px 14px;border-left:3px solid var(--l12-ui-danger);background:#261218;color:#efb1b7}.pager{display:flex;justify-content:center;align-items:center;gap:12px}.pager button{min-width:92px}.pager span{color:var(--l12-ui-text-muted);font-size:13px}
@media(max-width:900px){.career{grid-template-columns:1fr 1fr}.career-history>button{grid-template-columns:1fr 1fr}.career-open{display:none}}
@media(max-width:700px){.hub-page{padding:18px 10px calc(42px + env(safe-area-inset-bottom));gap:12px}.page-head{align-items:flex-start;flex-direction:column;padding-bottom:14px}.page-head h1{font-size:28px}.create-button{width:100%;min-height:44px}.section-tabs{grid-template-columns:1fr 1fr}.section-tabs button,.hub-page button{min-height:44px}.toolbar input{min-height:48px}.toolbar select{height:48px;min-height:48px!important}.panel{padding:14px;box-shadow:none}.career{display:flex;gap:7px;overflow-x:auto;scroll-snap-type:x proximity}.career article{min-width:122px;min-height:82px;scroll-snap-align:start}.career-history>header,.filter-heading{align-items:flex-start}.career-history>button{grid-template-columns:1fr;padding:12px}.career-history>button>span{min-width:0}.career-name{grid-template-columns:1fr auto!important}.toolbar{grid-template-columns:1fr 1fr}.search-field{grid-column:1/-1}.pager{justify-content:space-between}.pager button{min-width:88px}}
@media(max-width:390px){.toolbar{grid-template-columns:1fr}.search-field{grid-column:auto}.career-history>header>span{display:none}}
</style>
