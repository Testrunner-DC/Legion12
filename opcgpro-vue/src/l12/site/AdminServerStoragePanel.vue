<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { adminApi, hasPermission, type ServerStorageStatus } from '@/l12/platform'

const emit = defineEmits<{ notice: [value: string] }>()
const router = useRouter()
const status = ref<ServerStorageStatus | null>(null)
const loading = ref(false)
const error = ref('')
// The last successful sample stays on screen when a read fails: an empty panel is worse than a
// clearly dated one.
const lastSuccessAt = ref('')

function format(bytes: number) {
  if (!Number.isFinite(bytes) || bytes < 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = bytes
  let index = 0
  while (value >= 1024 && index < units.length - 1) { value /= 1024; index++ }
  return `${value >= 10 || index === 0 ? Math.round(value) : value.toFixed(1)} ${units[index]}`
}
function percent(used: number, total: number) { return total > 0 ? Math.min(100, Math.round(used / total * 100)) : 0 }
function pad(value: number) { return value.toString().padStart(2, '0') }
function formatUtc(value: string) {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) return '—'
  return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`
}
function formatLocal(value: string) {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) return '—'
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

// Server thresholds are the single source of truth; 70/85 are only the fallback when the payload
// omits them.
const warningPercent = computed(() => status.value?.thresholds?.warningPercent ?? 70)
const criticalPercent = computed(() => status.value?.thresholds?.criticalPercent ?? 85)
function toneOf(usedPercent: number): 'ok' | 'warn' | 'critical' {
  if (usedPercent >= criticalPercent.value) return 'critical'
  if (usedPercent >= warningPercent.value) return 'warn'
  return 'ok'
}
function toneLabel(tone: 'ok' | 'warn' | 'critical') {
  return tone === 'critical' ? '接近满载' : tone === 'warn' ? '偏高' : '正常'
}
const volumes = computed(() => [...(status.value?.volumes ?? [])]
  .map(volume => ({ ...volume, usedPercent: percent(volume.usedBytes, volume.totalBytes) }))
  .sort((left, right) => right.usedPercent - left.usedPercent))
const tightestVolumeId = computed(() => volumes.value.length > 1 ? volumes.value[0].id : '')

const trendSummary = computed(() => {
  const points = status.value?.trend ?? []
  if (points.length < 2) return null
  const first = points[0]
  const last = points[points.length - 1]
  const ids = new Set([...Object.keys(first.volumeUsedPercent), ...Object.keys(last.volumeUsedPercent)])
  let largest = { id: '', delta: 0 }
  ids.forEach(id => {
    const delta = (last.volumeUsedPercent[id] ?? 0) - (first.volumeUsedPercent[id] ?? 0)
    if (Math.abs(delta) > Math.abs(largest.delta)) largest = { id, delta }
  })
  return { samples: points.length, from: first.observedAt, to: last.observedAt, ...largest }
})
function volumeLabel(id: string) { return status.value?.volumes.find(volume => volume.id === id)?.label || '存储卷' }

const canReadRuntime = computed(() => hasPermission('admin.runtime.read'))
const sampledLabel = computed(() => {
  const observed = status.value?.observedAt
  if (!observed) return ''
  return `服务器采样 UTC ${formatUtc(observed)} ｜ 本地 ${formatLocal(observed)}`
})
const refreshLabel = computed(() => {
  if (loading.value) return '刷新中…'
  return lastSuccessAt.value ? `刷新（数据 ${formatLocal(lastSuccessAt.value)}）` : '刷新'
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    status.value = await adminApi.serverStorage()
    lastSuccessAt.value = status.value.observedAt
  } catch (cause) {
    const message = cause instanceof Error ? cause.message : '服务器存储状态读取失败'
    error.value = lastSuccessAt.value
      ? `本次读取失败：${message} ｜ 以下为 ${formatUtc(lastSuccessAt.value)} 的数据`
      : `本次读取失败：${message}`
    emit('notice', message)
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>
<template>
  <section class="storage-panel">
    <header>
      <div>
        <small>SERVER STORAGE</small>
        <h2>服务器状态与存储</h2>
        <p>结论、影响和行动来自服务端容量策略；页面不提供删除或压缩操作。</p>
      </div>
      <div class="header-actions">
        <button v-if="canReadRuntime" type="button" class="ghost" @click="router.push({ name: 'admin-operations' })">→ 后端运行状态</button>
        <button type="button" :disabled="loading" @click="load">{{ refreshLabel }}</button>
      </div>
    </header>
    <p v-if="error" class="failure" role="status">{{ error }} <button type="button" :disabled="loading" @click="load">重试</button></p>
    <template v-if="status">
      <section class="health-summary" :data-health="status.health"><small>当前结论</small><h3>{{ status.conclusion }}</h3><p>{{ status.impact }}</p><b>{{ status.recommendedAction }}</b></section>
      <section class="volumes">
        <article v-for="volume in volumes" :key="volume.id" :class="`tone-${toneOf(volume.usedPercent)}`">
          <div>
            <b>{{ volume.label }}</b>
            <span v-if="volume.id === tightestVolumeId" class="tightest">最紧张</span>
            <span class="usage">{{ format(volume.usedBytes) }} / {{ format(volume.totalBytes) }}（{{ volume.usedPercent }}%）· {{ toneLabel(toneOf(volume.usedPercent)) }}</span>
          </div>
          <div class="bar" role="presentation"><i :style="{ width: `${volume.usedPercent}%` }"></i></div>
          <small>剩余 {{ format(volume.freeBytes) }}</small>
        </article>
      </section>
      <section class="trend"><h3>本次服务进程趋势</h3><small>{{ status.trendDescription }}</small><p v-if="trendSummary">{{ trendSummary.samples }} 个样本 · {{ volumeLabel(trendSummary.id) }} 变化 {{ trendSummary.delta > 0 ? '+' : '' }}{{ trendSummary.delta }}%</p><p v-else>样本不足，暂不给出趋势结论。</p></section>
      <details class="technical"><summary>占用分类</summary><section class="categories"><article v-for="category in status.categories" :key="category.id"><span><b>{{ category.label }}</b></span><span class="bytes">{{ category.available ? format(category.bytes) : '不可用' }}</span></article></section></details>
    </template>
    <p v-else-if="!loading && !error" class="empty">暂无可用的服务器存储数据。</p>
    <footer class="sampled">
      <span v-if="sampledLabel">{{ sampledLabel }}</span>
      <span>分类大小缓存约 30 秒，连续刷新可能得到同一份数据。</span>
    </footer>
  </section>
</template>
<style scoped>
.storage-panel{padding:22px;border:1px solid #35424a;background:#0e161d}
.storage-panel header{display:flex;justify-content:space-between;gap:20px}
.header-actions{display:flex;flex-direction:column;gap:8px;align-items:flex-end}
.storage-panel h2,.storage-panel h3{margin:0;color:#e7e2d3;font-family:inherit}
.storage-panel small{color:#879092}
.storage-panel p{margin:8px 0 0;color:#b9c2c6}
.storage-panel button{padding:8px 14px;border:1px solid #4c5a63;background:#16222b;color:#dfe6e9;font-family:inherit;cursor:pointer}
.storage-panel button:disabled{opacity:.6;cursor:default}
.storage-panel button.ghost{background:transparent}
.failure{margin:14px 0;padding:10px 12px;border:1px solid #9b3e49;background:rgba(38,10,14,.6);color:#f2b7bd}
.health-summary{margin-top:16px;padding:14px;border:1px solid #35424a;background:#101a22}
.health-summary h3{margin:6px 0;font-size:18px}
.volumes{display:grid;gap:12px;margin-top:16px;grid-template-columns:repeat(auto-fit,minmax(240px,1fr))}
.volumes article{padding:12px;border:1px solid #35424a;background:#101a22;display:flex;flex-direction:column;gap:8px}
.volumes article.tone-warn{border-color:#8a6b25}
.volumes article.tone-critical{border-color:#9b3e49;background:rgba(38,10,14,.55)}
.volumes b{color:#e7e2d3}
.tightest{margin-left:8px;padding:1px 6px;border:1px solid #8a6b25;color:#d8ba68;font-size:12px}
.usage{display:block;color:#b9c2c6;font-variant-numeric:tabular-nums}
.bar{height:6px;background:#1b2831;border-radius:3px;overflow:hidden}
.bar i{display:block;height:100%;background:#6f8f7a}
.tone-warn .bar i{background:#d8ba68}
.tone-critical .bar i{background:#e07a5f}
.categories{display:grid;gap:6px;margin-top:8px}
.categories article{display:flex;justify-content:space-between;gap:12px;color:#b9c2c6}
.bytes{font-variant-numeric:tabular-nums}
.sampled{display:flex;flex-wrap:wrap;gap:6px 16px;margin-top:16px;color:#879092;font-size:13px;font-variant-numeric:tabular-nums}
@media(max-width:760px){.storage-panel header{align-items:stretch;flex-direction:column}.header-actions{flex-direction:column;align-items:stretch}.header-actions button{width:100%}.volumes{grid-template-columns:1fr}}
</style>
