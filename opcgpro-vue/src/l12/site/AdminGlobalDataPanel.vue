<script setup lang="ts">
import { ref } from 'vue'
import { type AdminGlobalAnalyticsReport } from '@/l12/platform'
import AdminGlobalAnalyticsSummary from './AdminGlobalAnalyticsSummary.vue'

const report = ref<AdminGlobalAnalyticsReport | null>(null)
function number(value?: number | null) {
  return value == null ? '—' : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}
</script>

<template>
  <section class="global-data">
    <header class="module-head"><div><small>GLOBAL ANALYTICS</small><h2>全局数据</h2><p>按自然日查看活跃、对局、在线与访问历史；当天数据会随新样本更新。</p></div></header>
    <AdminGlobalAnalyticsSummary heading="关键指标" @loaded="report = $event"/>
    <section v-if="report" class="history panel"><header><h3>逐日历史</h3><span>可按原始日记录追溯</span></header><div class="table"><div class="row head"><b>日期</b><b>DAU / WAU / MAU</b><b>对局 日 / 周 / 月</b><b>平均 / 峰值在线</b><b>新增 / 回访</b><b>PV</b></div><div v-for="day in [...report.days].reverse()" :key="day.date" class="row"><span>{{ day.date }}</span><span>{{ day.dailyActiveUsers }} / {{ day.weeklyActiveUsers }} / {{ day.monthlyActiveUsers }}</span><span>{{ day.dailyMatches }} / {{ day.weeklyMatches }} / {{ day.monthlyMatches }}</span><span>{{ number(day.averageOnline) }} / {{ day.peakOnline }}</span><span>{{ day.newUsers }} / {{ day.returningUsers }}</span><span>{{ day.pageViews }}</span></div></div></section>
    <section v-if="report" class="panel pages"><header><h3>页面访问量</h3><span>路径已移除查询参数，不记录玩家填写内容</span></header><article v-for="item in report.pageViews" :key="item.path"><code>{{ item.path }}</code><b>{{ item.views }}</b></article><p v-if="!report.pageViews.length" class="empty">所选日期暂无页面访问记录</p></section>
  </section>
</template>

<style scoped>
.global-data{--line:#33434d;--panel:#0e171f;display:grid;gap:12px;color:#f2f3ef}.module-head,.panel{border:1px solid var(--line);background:var(--panel)}.module-head{padding:20px}.module-head small{color:#dfc46e;font-weight:900;letter-spacing:.12em}.module-head h2{margin:5px 0;font-size:28px}.module-head p{margin:0;color:#8f9da2}.panel{padding:14px}.panel>header{display:flex;justify-content:space-between;gap:12px;padding-bottom:10px;border-bottom:1px solid #2e3d45}.panel h3{margin:0}.panel header span{display:block;margin-top:5px;color:#819097;font-size:12px}.table{overflow:auto}.row{display:grid;min-width:780px;grid-template-columns:110px repeat(5,1fr);gap:8px;padding:10px;border-bottom:1px solid #293840;font-size:12px}.row.head{color:#d8c16f}.pages article{display:flex;justify-content:space-between;gap:12px;padding:9px;border-bottom:1px solid #293840}.pages code{min-width:0;color:#9cccd0;overflow-wrap:anywhere}.pages b{color:#dfc46e}.empty{margin:10px 0 0;padding:14px;border:1px dashed #40515b;color:#84949a}
</style>
