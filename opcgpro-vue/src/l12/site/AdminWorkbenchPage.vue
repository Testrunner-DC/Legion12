<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, hasPermission, type AdminWorkbenchSummary } from '@/l12/platform'
import { visibleAdminSections } from './adminSections'
import AdminGlobalAnalyticsSummary from './AdminGlobalAnalyticsSummary.vue'

const summary = ref<AdminWorkbenchSummary | null>(null)
const loading = ref(true)
const error = ref('')
const modules = computed(() => visibleAdminSections(hasPermission).filter(item => item.id !== 'overview'))

async function load() {
  loading.value = true
  error.value = ''
  try { summary.value = await adminApi.workbenchSummary() }
  catch (cause) { error.value = cause instanceof Error ? cause.message : '工作台摘要暂时不可用' }
  finally { loading.value = false }
}
onMounted(load)
</script>

<template>
  <section class="workbench">
    <header>
      <div><small>CONTROL CENTER</small><h2>工作台</h2><p>待办、异常和最近活动由服务端一次聚合，并按当前账号权限过滤。</p></div>
      <div class="sampling"><span v-if="summary">采样于 {{ new Date(summary.sampledAt).toLocaleString() }}</span><button :disabled="loading" @click="load">{{ loading ? '读取中…' : '刷新摘要' }}</button></div>
    </header>
    <p v-if="error" class="notice" role="status">{{ error }}</p>
    <p v-if="summary?.partial" class="notice" role="status">部分摘要暂时不可用；其余数据仍可操作。不可用区块：{{ summary.unavailableSections.join('、') }}</p>
    <AdminGlobalAnalyticsSummary v-if="hasPermission('admin.analytics.read')" compact heading="全局数据概览"/>
    <section class="summary-section" aria-labelledby="pending-title"><h3 id="pending-title">待处理事项</h3><div class="action-grid"><router-link v-for="item in summary?.pending || []" :key="item.id" :to="item.path" :data-state="item.severity"><small>{{ item.kind }}</small><b>{{ item.count ?? item.label }}</b><span>{{ item.detail }}</span></router-link><p v-if="summary && !summary.pending.length" class="empty">权限范围内没有待办。</p></div></section>
    <section class="summary-section" aria-labelledby="anomaly-title"><h3 id="anomaly-title">运行与异常</h3><div class="action-grid"><router-link v-for="item in summary?.anomalies || []" :key="item.id" :to="item.path" :data-state="item.severity"><small>{{ item.kind }}</small><b>{{ item.label }}</b><span>{{ item.detail }}</span></router-link><p v-if="summary && !summary.anomalies.length" class="empty">权限范围内没有异常摘要。</p></div></section>
    <section class="summary-section" aria-labelledby="activity-title"><h3 id="activity-title">最近活动</h3><div class="activity-list"><router-link v-for="item in summary?.recentActivities || []" :key="item.id" :to="item.path"><span><b>{{ item.label }}</b><small>{{ item.kind }} · {{ item.occurredAt ? new Date(item.occurredAt).toLocaleString() : '本次采样' }}</small></span><em>{{ item.detail }}</em></router-link><p v-if="summary && !summary.recentActivities.length" class="empty">权限范围内暂无最近活动。</p></div></section>
    <section class="summary-section" aria-labelledby="modules-title"><h3 id="modules-title">全部可用任务</h3><div class="module-grid"><router-link v-for="item in modules" :key="item.id" :to="item.path"><small>{{ item.domain }}</small><b>{{ item.icon }} {{ item.label }}</b><span>进入任务</span></router-link></div></section>
  </section>
</template>

<style scoped>
.workbench{display:grid;gap:16px}.workbench>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px;padding:20px;border:1px solid #35424a;background:#101821}.workbench h2{margin:5px 0;font-size:24px}.workbench p{margin:4px 0;color:#87949a;line-height:1.6}.workbench small{color:#d5b85e;font-size:12px;font-weight:900;letter-spacing:.08em}.workbench button{min-height:40px;padding:8px 13px;border:1px solid #52616a;background:#101a21;color:#fff}.sampling{display:grid;justify-items:end;gap:8px;color:#87949a;font-size:12px}.summary-section{display:grid;gap:9px}.summary-section h3{margin:0;color:#c5ced2;font-size:15px}.action-grid,.module-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.action-grid a,.module-grid a,.activity-list a{display:flex;min-width:0;flex-direction:column;gap:8px;padding:18px;border:1px solid #35424a;background:#0e161d;color:#fff;text-decoration:none}.action-grid a:hover,.module-grid a:hover,.activity-list a:hover{border-color:#c1a44e}.action-grid a[data-state="warning"],.action-grid a[data-state="critical"],.action-grid a[data-state="attention"],.action-grid a[data-state="unavailable"]{border-color:#9a583f;background:#21130e}.action-grid b{font-size:25px}.action-grid span,.module-grid span{color:#87949a;font-size:14px}.module-grid{grid-template-columns:repeat(4,minmax(0,1fr))}.module-grid b{overflow-wrap:anywhere}.activity-list{display:grid;gap:8px}.activity-list a{display:grid;grid-template-columns:minmax(160px,.7fr) minmax(0,1fr);align-items:center;padding:12px 16px}.activity-list a span{display:grid;gap:3px}.activity-list em{color:#aeb8bc;font-style:normal;overflow-wrap:anywhere}.notice{padding:10px 12px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.empty{padding:14px;border:1px dashed #35424a}
@media(max-width:1050px){.module-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:700px){.workbench>header{align-items:stretch;flex-direction:column}.sampling{justify-items:stretch}.action-grid,.module-grid{grid-template-columns:1fr}.activity-list a{grid-template-columns:1fr}.workbench button{width:100%}}
</style>
