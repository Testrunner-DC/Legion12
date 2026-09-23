<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, type AdminGlobalAnalyticsReport } from '@/l12/platform'

const emit = defineEmits<{ notice: [message: string] }>()
const report = ref<AdminGlobalAnalyticsReport | null>(null)
const loading = ref(false)
const today = new Date()
const dateText = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
const from = ref(dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29)))
const to = ref(dateText(today))
const latest = computed(() => report.value?.days.at(-1))
const maximum = computed(() => Math.max(1, ...(report.value?.days.map(day => day.dailyActiveUsers) || [1])))
function number(value?: number | null) { return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value || 0) }
function dateTime(value?: string | null) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '暂无样本' }
async function load() {
  loading.value = true
  try { report.value = await adminApi.globalAnalytics({ from: from.value, to: to.value }) }
  catch (error) { emit('notice', error instanceof Error ? error.message : '全局数据加载失败') }
  finally { loading.value = false }
}
onMounted(load)
</script>

<template>
  <section class="global-data">
    <header class="module-head"><div><small>GLOBAL ANALYTICS</small><h2>全局数据</h2><p>按自然日留存活跃、对局、在线与访问历史；当天数据会随新样本更新。</p></div><div class="filters"><label>开始<input v-model="from" type="date"/></label><label>结束<input v-model="to" type="date"/></label><button :disabled="loading" @click="load">{{ loading ? '读取中…' : '查询' }}</button></div></header>
    <div v-if="latest" class="metric-grid">
      <article title="当日有登录活动记录的去重账号"><small>DAU / WAU / MAU</small><b>{{ latest.dailyActiveUsers }} / {{ latest.weeklyActiveUsers }} / {{ latest.monthlyActiveUsers }}</b><span>日 / 近7日 / 近30日去重活跃账号</span></article>
      <article title="以对局结束日计数，仅含正常结束且无错误的对局"><small>日 / 周 / 月对局</small><b>{{ latest.dailyMatches }} / {{ latest.weeklyMatches }} / {{ latest.monthlyMatches }}</b><span>当日 / 近7日 / 近30日</span></article>
      <article title="在线人数按每分钟最多保留一份样本"><small>平均 / 最高在线</small><b>{{ number(latest.averageOnline) }} / {{ latest.peakOnline }}</b><span>峰值发生：{{ dateTime(latest.peakOnlineAt) }}</span></article>
      <article title="新增以账号注册日为准；老用户为当日活跃且注册日更早的账号"><small>新增 / 老用户 / PV</small><b>{{ latest.newUsers }} / {{ latest.returningUsers }} / {{ latest.pageViews }}</b><span>账号与页面访问采用独立口径</span></article>
    </div>
    <section v-if="report" class="trend panel">
      <header><h3>每日活跃趋势</h3><span>{{ report.fromDate }} 至 {{ report.toDate }}</span></header>
      <div class="bars" aria-label="每日活跃用户趋势"><article v-for="day in report.days" :key="day.date"><i :style="{ height: `${Math.max(3, day.dailyActiveUsers / maximum * 100)}%` }"></i><b>{{ day.dailyActiveUsers }}</b><small>{{ day.date.slice(5) }}</small></article></div>
    </section>
    <section v-if="report" class="history panel"><header><h3>逐日历史</h3><span>可按原始日记录追溯</span></header><div class="table"><div class="row head"><b>日期</b><b>DAU / WAU / MAU</b><b>对局 日 / 周 / 月</b><b>平均 / 峰值在线</b><b>新增 / 老用户</b><b>PV</b></div><div v-for="day in [...report.days].reverse()" :key="day.date" class="row"><span>{{ day.date }}</span><span>{{ day.dailyActiveUsers }} / {{ day.weeklyActiveUsers }} / {{ day.monthlyActiveUsers }}</span><span>{{ day.dailyMatches }} / {{ day.weeklyMatches }} / {{ day.monthlyMatches }}</span><span>{{ number(day.averageOnline) }} / {{ day.peakOnline }}</span><span>{{ day.newUsers }} / {{ day.returningUsers }}</span><span>{{ day.pageViews }}</span></div></div></section>
    <section v-if="report" class="panel pages"><header><h3>页面访问量</h3><span>路径已移除查询参数，不记录玩家填写内容</span></header><article v-for="item in report.pageViews" :key="item.path"><code>{{ item.path }}</code><b>{{ item.views }}</b></article><p v-if="!report.pageViews.length">所选日期暂无页面访问记录</p></section>
  </section>
</template>

<style scoped>
.global-data{--line:#33434d;--panel:#0e171f;display:grid;gap:12px;color:#f2f3ef}.module-head,.panel,.metric-grid article{border:1px solid var(--line);background:var(--panel)}.module-head{display:flex;align-items:end;justify-content:space-between;gap:20px;padding:20px}.module-head small{color:#dfc46e;font-weight:900;letter-spacing:.12em}.module-head h2{margin:5px 0;font-size:28px}.module-head p{margin:0;color:#8f9da2}.filters{display:flex;align-items:end;gap:8px}.filters label{display:grid;gap:5px;color:#9aa7ab;font-size:12px;font-weight:900}.filters input,.filters button{box-sizing:border-box;min-height:39px;border:1px solid #53636d;background:#071016;color:#fff;padding:8px 10px}.filters button{color:#ebd47d;font-weight:900}.metric-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.metric-grid article{display:grid;min-height:110px;align-content:end;gap:5px;padding:14px}.metric-grid small,.metric-grid span{color:#84949a;font-size:12px}.metric-grid b{font-size:20px}.panel{padding:14px}.panel>header{display:flex;justify-content:space-between;gap:12px;padding-bottom:10px;border-bottom:1px solid #2e3d45}.panel h3{margin:0}.panel header span{color:#819097;font-size:12px}.bars{display:flex;height:210px;align-items:end;gap:5px;padding-top:18px;overflow:auto}.bars article{display:grid;flex:1;min-width:34px;height:100%;grid-template-rows:1fr auto auto;align-items:end;text-align:center}.bars i{display:block;min-height:3px;background:linear-gradient(#68c7cd,#b89b47)}.bars b{font-size:11px}.bars small{color:#738188;font-size:10px}.table{overflow:auto}.row{display:grid;min-width:780px;grid-template-columns:110px repeat(5,1fr);gap:8px;padding:10px;border-bottom:1px solid #293840;font-size:12px}.row.head{color:#d8c16f}.pages article{display:flex;justify-content:space-between;padding:9px;border-bottom:1px solid #293840}.pages code{color:#9cccd0}.pages b{color:#dfc46e}@media(max-width:900px){.module-head{align-items:start;flex-direction:column}.metric-grid{grid-template-columns:1fr 1fr}.filters{width:100%;flex-wrap:wrap}}@media(max-width:520px){.metric-grid{grid-template-columns:1fr}.filters{display:grid;grid-template-columns:1fr 1fr}.filters label,.filters input{width:100%;min-width:0}.filters button{grid-column:1/-1;width:100%}}
</style>
