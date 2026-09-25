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
    <header class="hero"><div><small>TOURNAMENTS</small><h1>赛事中心</h1><p>查找赛事、跟进自己的下一步，或创建并运营一场赛事。</p></div><button type="button" @click="switchSection('create')">创建赛事</button></header>
    <nav class="tabs" aria-label="赛事分类"><button v-for="item in ([['discover','发现'],['mine','我的赛事'],['history','历史'],['host','主办管理']] as const)" :key="item[0]" :class="{active: section === item[0]}" @click="switchSection(item[0])">{{ item[1] }}</button></nav>
    <section v-if="section === 'create'" class="panel"><TournamentCreateWizard :platform-version="result.platformVersion" @created="value => open(value.code)" /></section>
    <template v-else>
      <template v-if="section === 'mine' && career">
        <section class="career"><span>参赛 <b>{{ career.participated }}</b></span><span>主办 <b>{{ career.organized }}</b></span><span>执裁 <b>{{ career.refereed }}</b></span><span>战绩 <b>{{ career.wins }}胜 {{ career.losses }}负 {{ career.draws }}平</b></span></section>
        <section class="career-history"><h2>个人赛事履历</h2><button v-for="item in career.items" :key="item.tournamentId" type="button" @click="open(item.code)"><b>{{ item.name }}</b><span>{{ tournamentStatusText(item.status) }} · {{ tournamentFormatText(item.format) }}</span><span>{{ item.wins }}胜 {{ item.losses }}负 {{ item.draws }}平<span v-if="item.finalRank"> · 最终第 {{ item.finalRank }} 名</span></span><small v-if="item.organized">主办</small><small v-else-if="item.refereed">裁判</small><small v-else>参赛者</small></button><footer v-if="career.totalPages > 1" class="pager"><button :disabled="careerPage <= 1" @click="careerPage--">上一页</button><span>{{ careerPage }} / {{ career.totalPages }}</span><button :disabled="careerPage >= career.totalPages" @click="careerPage++">下一页</button></footer></section>
      </template>
      <section class="toolbar"><input v-model="search" type="search" placeholder="搜索赛事名称、代码或主办者"><select v-model="format"><option value="">全部赛制</option><option value="single">单败淘汰</option><option value="swiss">瑞士轮</option><option value="swiss-cut">瑞士轮 + Cut</option></select><select v-model="timeRange"><option value="">全部时间</option><option value="7">未来 7 天</option><option value="30">未来 30 天</option></select><span>共 {{ result.total }} 场</span></section>
      <p v-if="notice" class="notice">{{ notice }}</p>
      <TournamentSummaryList :items="result.items" :loading="loading" @open="open" />
      <footer v-if="result.totalPages > 1" class="pager"><button :disabled="page <= 1" @click="page--">上一页</button><span>{{ page }} / {{ result.totalPages }}</span><button :disabled="page >= result.totalPages" @click="page++">下一页</button></footer>
    </template>
  </main>
</template>

<style scoped>
.hub-page{max-width:1180px;margin:0 auto;padding:32px 24px 64px;display:grid;gap:20px}.hero{display:flex;justify-content:space-between;align-items:end;gap:24px}.hero h1{margin:6px 0}.hero p{margin:0;color:var(--muted)}.tabs{display:flex;gap:6px;border-bottom:1px solid var(--border);overflow:auto}.tabs button{padding:12px;border:0;background:transparent;color:inherit}.tabs .active{border-bottom:2px solid var(--accent)}.panel,.career,.career-history{padding:18px;border:1px solid var(--border);border-radius:12px;background:var(--panel)}.career{display:flex;flex-wrap:wrap;gap:22px}.career-history{display:grid;gap:10px}.career-history h2{margin:0 0 6px}.career-history>button{display:grid;grid-template-columns:minmax(180px,1fr) minmax(150px,.7fr) minmax(150px,.7fr) auto;gap:10px;text-align:left;align-items:center}.career-history small,.career-history span{color:var(--muted)}.toolbar{display:flex;gap:10px;align-items:center}.toolbar input{flex:1}.toolbar span{color:var(--muted)}.pager{display:flex;justify-content:center;align-items:center;gap:12px}.notice{color:var(--danger)}@media(max-width:650px){.hub-page{padding:20px 14px}.hero{align-items:start}.career-history>button{grid-template-columns:1fr}.toolbar{display:grid;grid-template-columns:1fr 1fr}.toolbar input{grid-column:1/-1}}
</style>
