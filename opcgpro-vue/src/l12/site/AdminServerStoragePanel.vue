<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type ServerStorageStatus } from '@/l12/platform'

const emit = defineEmits<{ notice: [value: string] }>()
const status = ref<ServerStorageStatus | null>(null)
const loading = ref(false)
function format(bytes: number) {
  if (!Number.isFinite(bytes) || bytes < 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = bytes; let index = 0
  while (value >= 1024 && index < units.length - 1) { value /= 1024; index += 1 }
  return `${value >= 10 || index === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[index]}`
}
function percent(used: number, total: number) { return total > 0 ? Math.min(100, Math.round(used / total * 100)) : 0 }
async function load() {
  loading.value = true
  try { status.value = await adminApi.serverStorage() }
  catch (error) { emit('notice', error instanceof Error ? error.message : '服务器存储状态读取失败') }
  finally { loading.value = false }
}
onMounted(load)
</script>

<template>
  <section class="storage-panel">
    <header><div><small>SERVER STORAGE</small><h2>服务器状态与存储</h2><p>只读监控。分类大小会缓存约 30 秒，页面不提供删除或压缩操作。</p></div><button @click="load">{{ loading ? '读取中…' : '刷新' }}</button></header>
    <template v-if="status">
      <section class="volumes"><article v-for="volume in status.volumes" :key="volume.mountPoint"><div><b>{{ volume.mountPoint }}</b><span>{{ format(volume.usedBytes) }} / {{ format(volume.totalBytes) }}</span></div><div class="bar"><i :style="{ width: `${percent(volume.usedBytes, volume.totalBytes)}%` }"/></div><small>已用 {{ percent(volume.usedBytes, volume.totalBytes) }}% · 可用 {{ format(volume.freeBytes) }}</small></article></section>
      <section class="categories"><h3>已跟踪占用方向</h3><article v-for="category in status.categories" :key="category.id"><span><b>{{ category.label }}</b><small>{{ category.available ? category.path : '当前环境未挂载' }}</small></span><strong>{{ category.available ? format(category.bytes) : '—' }}</strong></article></section>
      <footer>采样：{{ new Date(status.observedAt).toLocaleString() }} · 进程内存 {{ format(status.workingSetBytes) }}</footer>
    </template>
    <p v-else-if="!loading" class="empty">暂无可用的服务器存储数据。</p>
  </section>
</template>

<style scoped>
.storage-panel{padding:22px;border:1px solid #35424a;background:#0e161d}.storage-panel header{display:flex;justify-content:space-between;gap:20px}.storage-panel h2,.storage-panel h3{margin:4px 0 8px}.storage-panel p,.storage-panel small,.storage-panel span{color:#9aa8ad;line-height:1.6}.storage-panel button{min-height:38px;padding:8px 12px;border:1px solid #52616a;background:#101a21;color:#fff}.volumes{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px;margin-top:20px}.volumes article,.categories{padding:14px;border:1px solid #35424a;background:#091016}.volumes article>div:first-child{display:flex;justify-content:space-between;gap:10px}.bar{height:8px;margin:12px 0;background:#1b2931}.bar i{display:block;height:100%;background:#d8ba68}.categories{margin-top:16px}.categories article{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:10px 0;border-top:1px solid #26363e}.categories span{display:grid;gap:3px}.categories strong{color:#d8ba68}.storage-panel footer{margin-top:14px;color:#718087;font-size:13px}.empty{padding:22px;text-align:center}@media(max-width:760px){.storage-panel header{align-items:flex-start;flex-direction:column}}
</style>
