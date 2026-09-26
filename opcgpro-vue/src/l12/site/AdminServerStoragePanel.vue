<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, type ServerStorageStatus } from '@/l12/platform'
const emit = defineEmits<{ notice: [value: string] }>()
const status = ref<ServerStorageStatus | null>(null)
const loading = ref(false)
const error = ref('')
function format(bytes: number) { if (!Number.isFinite(bytes) || bytes < 0) return '—'; const units = ['B', 'KB', 'MB', 'GB', 'TB']; let value = bytes; let index = 0; while (value >= 1024 && index < units.length - 1) { value /= 1024; index++ } return `${value >= 10 || index === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[index]}` }
function percent(used: number, total: number) { return total > 0 ? Math.min(100, Math.round(used / total * 100)) : 0 }
const trendSummary = computed(() => {
  const points = status.value?.trend ?? []; if (points.length < 2) return null
  const first = points[0]; const last = points[points.length - 1]; const ids = new Set([...Object.keys(first.volumeUsedPercent), ...Object.keys(last.volumeUsedPercent)])
  let largest = { id: '', delta: 0 }
  ids.forEach(id => { const delta = (last.volumeUsedPercent[id] ?? 0) - (first.volumeUsedPercent[id] ?? 0); if (Math.abs(delta) > Math.abs(largest.delta)) largest = { id, delta } })
  return { samples: points.length, from: first.observedAt, to: last.observedAt, ...largest }
})
function volumeLabel(id: string) { return status.value?.volumes.find(volume => volume.id === id)?.label || '存储卷' }
const freshness = computed(() => {
  const observed = Date.parse(status.value?.observedAt ?? '')
  if (!Number.isFinite(observed)) return { state: 'unknown', label: '采样时间不可用，建议刷新确认' }
  const seconds = Math.max(0, Math.floor((Date.now() - observed) / 1000))
  if (seconds < 120) return { state: 'fresh', label: `${seconds} 秒前采样 · 数据新鲜` }
  const minutes = Math.floor(seconds / 60)
  if (minutes < 5) return { state: 'fresh', label: `${minutes} 分钟前采样 · 仍在新鲜窗口` }
  return { state: 'stale', label: `${minutes} 分钟前采样 · 建议刷新确认` }
})
async function load() {
  loading.value = true; error.value = ''
  try { status.value = await adminApi.serverStorage() }
  catch (cause) { error.value = cause instanceof Error ? cause.message : '服务器存储状态读取失败'; emit('notice', error.value) }
  finally { loading.value = false }
}
onMounted(load)
</script>
<template>
  <section class="storage-panel">
    <header><div><small>SERVER STORAGE</small><h2>服务器状态与存储</h2><p>结论、影响和行动来自服务端容量策略；页面不提供删除或压缩操作。</p></div><button :disabled="loading" @click="load">{{ loading ? '读取中…' : '刷新' }}</button></header>
    <p v-if="error" class="failure" role="status">{{ error }} <button type="button" :disabled="loading" @click="load">重试</button></p>
    <template v-if="status">
      <section class="health-summary" :data-health="status.health"><small>当前结论</small><h3>{{ status.conclusion }}</h3><p>{{ status.impact }}</p><b>{{ status.recommendedAction }}</b><span class="freshness" :data-freshness="freshness.state">采样新鲜度：{{ freshness.label }}</span><span>权威阈值：关注 {{ status.thresholds.warningPercent }}% · 紧急 {{ status.thresholds.criticalPercent }}% · 来源：{{ status.thresholds.source }}</span><span v-if="status.sampleState !== 'complete'">采样覆盖：{{ status.sampleState === 'partial' ? '部分可用' : '暂不可用' }} · {{ status.unavailableSourceCount }} 个来源未完成</span></section>
      <section class="volumes"><article v-for="volume in status.volumes" :key="volume.id"><div><b>{{ volume.label }}</b><span>{{ format(volume.usedBytes) }} / {{ format(volume.totalBytes) }}</span></div><div class="bar"><i :style="{ width: `${percent(volume.usedBytes, volume.totalBytes)}%` }"/></div><small>已用 {{ percent(volume.usedBytes, volume.totalBytes) }}% · 可用 {{ format(volume.freeBytes) }}</small></article><p v-if="!status.volumes.length" class="empty">本次采样没有可用卷信息。</p></section>
      <section class="trend"><h3>本次服务进程趋势</h3><small>{{ status.trendDescription }}</small><p v-if="trendSummary">{{ trendSummary.samples }} 个样本 · {{ new Date(trendSummary.from).toLocaleString() }} 至 {{ new Date(trendSummary.to).toLocaleString() }}<template v-if="trendSummary.id"> · {{ volumeLabel(trendSummary.id) }} {{ trendSummary.delta > 0 ? '上升' : trendSummary.delta < 0 ? '下降' : '持平' }} {{ Math.abs(trendSummary.delta) }} 个百分点</template></p><p v-else>本次服务进程当前只有一次真实采样，暂不判断趋势。</p></section>
      <details class="technical"><summary>占用分类</summary><section class="categories"><article v-for="category in status.categories" :key="category.id"><span><b>{{ category.label }}</b><small>{{ category.available ? '已采样' : '当前不可用' }}</small></span><strong>{{ category.available ? format(category.bytes) : '—' }}</strong></article></section><footer>采样：{{ new Date(status.observedAt).toLocaleString() }} · 服务内存 {{ format(status.workingSetBytes) }}</footer></details>
    </template>
    <p v-else-if="!loading && !error" class="empty">暂无可用的服务器存储数据。</p>
  </section>
</template>
<style scoped>
.storage-panel{padding:22px;border:1px solid #35424a;background:#0e161d}.storage-panel header{display:flex;justify-content:space-between;gap:20px}.storage-panel h2,.storage-panel h3{margin:4px 0 8px}.storage-panel p,.storage-panel small,.storage-panel span{color:#9aa8ad;line-height:1.6}.storage-panel button{min-height:38px;padding:8px 12px;border:1px solid #52616a;background:#101a21;color:#fff}.failure{margin-top:16px;padding:12px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.failure button{margin-left:8px;color:#edd584}.health-summary{margin-top:18px;padding:16px;border:1px solid #2f785e;background:#0d251c}.health-summary[data-health="warning"]{border-color:#8a7030;background:#211b0e}.health-summary[data-health="critical"],.health-summary[data-health="unknown"]{border-color:#84424b;background:#291116}.health-summary>*{display:block}.health-summary span{margin-top:9px;font-size:13px}.health-summary .freshness[data-freshness="stale"],.health-summary .freshness[data-freshness="unknown"]{color:#ef9b85}.volumes{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;margin-top:16px}.volumes article,.categories,.trend,.technical{padding:14px;border:1px solid #35424a;background:#091016}.volumes article>div:first-child{display:flex;justify-content:space-between;gap:10px}.bar{height:8px;margin:12px 0;background:#1b2931}.bar i{display:block;height:100%;background:#d8ba68}.trend,.technical{margin-top:16px}.technical summary{cursor:pointer;color:#d8ba68;font-weight:900}.categories{margin-top:12px}.categories article{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:10px 0;border-top:1px solid #26363e}.categories span{display:grid;gap:3px}.categories strong{color:#d8ba68}.storage-panel footer{margin-top:14px;color:#718087;font-size:13px}.empty{padding:22px;border:1px dashed #35424a;text-align:center}
@media(max-width:760px){.storage-panel header{align-items:stretch;flex-direction:column}.storage-panel header button{width:100%}.volumes{grid-template-columns:1fr}.categories article{align-items:flex-start}}
</style>
