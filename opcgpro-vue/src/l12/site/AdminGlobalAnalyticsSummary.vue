<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, type AdminGlobalAnalyticsDay, type AdminGlobalAnalyticsReport } from '@/l12/platform'

type TrendMetricKey = keyof Pick<AdminGlobalAnalyticsDay,
  'dailyActiveUsers'|'weeklyActiveUsers'|'monthlyActiveUsers'|'dailyMatches'|'weeklyMatches'|'monthlyMatches'|
  'averageOnline'|'peakOnline'|'newUsers'|'returningUsers'|'pageViews'>

withDefaults(defineProps<{ heading?: string; compact?: boolean }>(), {
  heading: '全局数据概览',
  compact: false,
})
const emit = defineEmits<{ loaded: [report: AdminGlobalAnalyticsReport] }>()
const report = ref<AdminGlobalAnalyticsReport | null>(null)
const loading = ref(true)
const error = ref('')
const today = new Date()
const dateText = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
const from = ref(dateText(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29)))
const to = ref(dateText(today))
const latest = computed(() => report.value?.days.at(-1))
const trendMetric = ref<TrendMetricKey>('dailyActiveUsers')
const trendMetrics: Array<{ key: TrendMetricKey; label: string; unit: string }> = [
  { key: 'dailyActiveUsers', label: 'DAU', unit: '人' },
  { key: 'weeklyActiveUsers', label: 'WAU', unit: '人' },
  { key: 'monthlyActiveUsers', label: 'MAU', unit: '人' },
  { key: 'dailyMatches', label: '日对局', unit: '场' },
  { key: 'weeklyMatches', label: '周对局', unit: '场' },
  { key: 'monthlyMatches', label: '月对局', unit: '场' },
  { key: 'averageOnline', label: '平均在线', unit: '人' },
  { key: 'peakOnline', label: '最高在线', unit: '人' },
  { key: 'newUsers', label: '新增用户', unit: '人' },
  { key: 'returningUsers', label: '回访用户', unit: '人' },
  { key: 'pageViews', label: 'PV', unit: '次' },
]
const selectedMetric = computed(() => trendMetrics.find(metric => metric.key === trendMetric.value)!)
const trendPoints = computed(() => (report.value?.days ?? []).map(day => {
  const raw = day[trendMetric.value]
  return { date: day.date, value: typeof raw === 'number' && Number.isFinite(raw) ? Math.max(0, raw) : null }
}))
const maximum = computed(() => Math.max(0, ...trendPoints.value.map(point => point.value ?? 0)))
function number(value?: number | null) {
  return value == null ? '—' : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}
function dateTime(value?: string | null) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '暂无样本'
}
function barHeight(value: number | null) {
  if (value == null || maximum.value <= 0) return '0%'
  return `${Math.min(100, value / maximum.value * 100)}%`
}
async function load() {
  loading.value = true
  error.value = ''
  try {
    const next = await adminApi.globalAnalytics({ from: from.value, to: to.value })
    report.value = next
    emit('loaded', next)
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '全局数据加载失败'
  } finally { loading.value = false }
}
onMounted(load)
</script>

<template>
  <section class="analytics-summary" :class="{ compact }" aria-labelledby="global-summary-title">
    <header>
      <div><h3 id="global-summary-title">{{ heading }}</h3><span v-if="report">{{ report.fromDate }} 至 {{ report.toDate }}</span></div>
      <form class="filters" @submit.prevent="load">
        <label>开始<input v-model="from" type="date" required/></label>
        <label>结束<input v-model="to" type="date" required/></label>
        <button type="submit" :disabled="loading">{{ loading ? '读取中…' : '查询' }}</button>
      </form>
    </header>
    <p v-if="error" class="state error" role="status">{{ error }} <button type="button" @click="load">重试</button></p>
    <p v-else-if="loading && !report" class="state" role="status">正在读取全局数据…</p>
    <div v-else-if="latest" class="metric-grid">
      <article title="当日有登录活动记录的去重账号"><small>DAU / WAU / MAU</small><b>{{ latest.dailyActiveUsers }} / {{ latest.weeklyActiveUsers }} / {{ latest.monthlyActiveUsers }}</b><span>日 / 近 7 日 / 近 30 日活跃账号</span></article>
      <article title="以对局结束日计数，仅含正常结束且无错误的对局"><small>日 / 周 / 月对局</small><b>{{ latest.dailyMatches }} / {{ latest.weeklyMatches }} / {{ latest.monthlyMatches }}</b><span>当日 / 近 7 日 / 近 30 日</span></article>
      <article title="在线人数按每分钟最多保留一份样本"><small>平均 / 最高在线</small><b>{{ number(latest.averageOnline) }} / {{ latest.peakOnline }}</b><span>峰值发生：{{ dateTime(latest.peakOnlineAt) }}</span></article>
      <article title="新增以账号注册日为准；回访为当日活跃且注册日更早的账号"><small>新增 / 回访 / PV</small><b>{{ latest.newUsers }} / {{ latest.returningUsers }} / {{ latest.pageViews }}</b><span>账号活动与页面访问分别计数</span></article>
    </div>
    <p v-else class="state empty">所选日期没有逐日样本。</p>
    <section v-if="report" class="trend-panel">
      <header><div><h4>活跃趋势</h4><span>{{ report.fromDate }} 至 {{ report.toDate }} · 单位：{{ selectedMetric.unit }}</span></div></header>
      <div class="metric-switch" role="group" aria-label="趋势指标">
        <button v-for="metric in trendMetrics" :key="metric.key" type="button" :aria-pressed="trendMetric === metric.key" :class="{ active: trendMetric === metric.key }" @click="trendMetric = metric.key">{{ metric.label }}</button>
      </div>
      <div v-if="report.days.length" class="bars" :aria-label="`${selectedMetric.label}逐日趋势，单位${selectedMetric.unit}`">
        <article v-for="point in trendPoints" :key="point.date" :data-zero="point.value === 0" :data-missing="point.value == null">
          <div class="bar-track"><i :style="{ height: barHeight(point.value) }"></i></div>
          <b>{{ number(point.value) }}</b><small>{{ point.date.slice(5) }}</small>
        </article>
      </div>
      <p v-else class="state empty">所选日期没有逐日样本。</p>
    </section>
  </section>
</template>

<style scoped>
.analytics-summary{display:grid;gap:12px;padding:16px;border:1px solid #33434d;background:#0e171f;color:#f2f3ef}.analytics-summary>header{display:flex;align-items:end;justify-content:space-between;gap:16px}.analytics-summary h3,.analytics-summary h4{margin:0}.analytics-summary header span{display:block;margin-top:5px;color:#819097;font-size:12px}.filters{display:flex;align-items:end;gap:8px}.filters label{display:grid;gap:5px;color:#9aa7ab;font-size:12px;font-weight:900}.filters input,.filters button,.state button{box-sizing:border-box;min-height:39px;border:1px solid #53636d;background:#071016;color:#fff;padding:8px 10px}.filters button,.state button{color:#ebd47d;font-weight:900}.metric-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}.metric-grid article{display:grid;min-width:0;min-height:110px;align-content:end;gap:5px;padding:14px;border:1px solid #33434d;background:#091117}.metric-grid small,.metric-grid span{color:#84949a;font-size:12px}.metric-grid b{font-size:20px;overflow-wrap:anywhere}.state{margin:0;padding:14px;border:1px dashed #40515b;color:#9aa7ab}.state.error{border-style:solid;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584}.state button{min-height:32px;margin-left:8px;padding:5px 9px}.trend-panel{padding-top:4px;border-top:1px solid #2e3d45}.trend-panel>header{padding-top:8px}.metric-switch{display:flex;flex-wrap:wrap;gap:6px;padding:12px 0 2px}.metric-switch button{box-sizing:border-box;min-height:36px;padding:7px 11px;border:1px solid #53636d;background:#071016;color:#aeb9bc;font-weight:900}.metric-switch button.active{border-color:#dfc46e;background:#302710;color:#f2d77f}.metric-switch button:focus-visible{outline:2px solid #64c8ce;outline-offset:1px}.bars{display:flex;height:230px;align-items:stretch;gap:5px;padding-top:14px;overflow:auto}.bars article{display:grid;flex:1;min-width:38px;height:100%;grid-template-rows:minmax(100px,1fr) auto auto;gap:4px;text-align:center}.bar-track{display:flex;min-height:0;align-items:end;border-bottom:1px solid #40515b;background:linear-gradient(180deg,transparent,#0a1218)}.bars i{display:block;width:100%;background:linear-gradient(#68c7cd,#b89b47)}.bars article[data-zero="true"] .bar-track::after{content:'0';width:100%;padding-bottom:3px;color:#66767d;font-size:10px}.bars article[data-missing="true"] .bar-track::after{content:'缺';width:100%;padding-bottom:3px;color:#a87979;font-size:10px}.bars b{font-size:11px}.bars small{color:#738188;font-size:10px}.compact{padding:14px}.compact .metric-grid article{min-height:96px}.compact .bars{height:200px}
@media(max-width:1000px){.metric-grid{grid-template-columns:1fr 1fr}.analytics-summary>header{align-items:start;flex-direction:column}.filters{width:100%;flex-wrap:wrap}}
@media(max-width:700px){.metric-switch{display:grid;grid-template-columns:repeat(3,minmax(0,1fr))}.metric-switch button{min-width:0;padding:7px 5px;white-space:normal}.bars{height:200px}}
@media(max-width:560px){.metric-grid{grid-template-columns:1fr}.filters{display:grid;grid-template-columns:1fr 1fr}.filters label,.filters input{width:100%;min-width:0}.filters button{grid-column:1/-1;width:100%}}
@media(max-width:430px){.metric-switch{grid-template-columns:repeat(2,minmax(0,1fr))}}
</style>
