<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, getEffectiveOperationsPolicy, type OperationsConfigView } from '@/l12/platform'
import { l12State } from '@/l12/net'

const current = ref<OperationsConfigView | null>(null)
const hours = ref(2)
const busy = ref(false)
const notice = ref('')
const failed = ref(false)
const active = computed(() => current.value?.immediateMaintenance?.enabled === true)
const supported = computed(() => current.value?.immediateMaintenance !== undefined)
const validHours = computed(() => Number.isInteger(hours.value) && hours.value >= 1 && hours.value <= 168)

async function refresh() {
  if (busy.value) return
  busy.value = true
  try {
    current.value = await adminApi.operationsConfig()
    if (current.value.immediateMaintenance) hours.value = current.value.immediateMaintenance.expectedDurationHours
    failed.value = false
  } catch (error) {
    failed.value = true
    notice.value = error instanceof Error ? error.message : '维护状态读取失败，请刷新后重试'
  } finally { busy.value = false }
}
async function changeMaintenance(begin: boolean) {
  if (busy.value || !current.value || !supported.value || (begin && !validHours.value)) return
  busy.value = true
  notice.value = ''
  failed.value = false
  try {
    const result = begin
      ? await adminApi.beginImmediateMaintenance(hours.value, '管理员开启即时维护，允许进行中对局完成', current.value.version)
      : await adminApi.endImmediateMaintenance('管理员结束即时维护', current.value.version)
    current.value = result.current
    notice.value = begin
      ? `即时维护已开启：新对局已关闭，进行中的对局可以打完。预计维护 ${result.current.immediateMaintenance?.expectedDurationHours ?? hours.value} 小时。`
      : '即时维护已结束；原预约维护计划保持不变。'
    // Failed read-back must not misreport a successfully applied command.
    try {
      const policy = await getEffectiveOperationsPolicy()
      if (policy.version >= (l12State.operationsPolicy?.version ?? 0)) l12State.operationsPolicy = policy
      if (!begin && policy.maintenance.entryBlocked) notice.value += '预约维护仍在生效，新对局暂未开放。'
      else if (!begin) notice.value += '新对局已恢复开放。'
    } catch { notice.value += '实时策略读取暂不可用，请刷新确认入口状态。' }
  } catch (error) {
    failed.value = true
    notice.value = `${error instanceof Error ? error.message : '维护操作失败'}；请刷新状态后再操作。`
  } finally { busy.value = false }
}
onMounted(refresh)
</script>

<template>
  <section class="immediate-maintenance" data-ui-contract="immediate-maintenance-independent">
    <header><div><h3>立即维护</h3><p>只关闭新开对局，现有对局可继续和重连；网站、后台保持可用。</p></div><button type="button" :disabled="busy" @click="refresh">刷新状态</button></header>
    <p class="maintenance-state">当前状态：{{ !current ? '正在读取' : active ? '即时维护中 · 新对局已关闭' : '即时维护未开启' }}</p>
    <p v-if="current && !supported" class="maintenance-note">当前后端尚不支持即时维护，请先部署匹配版本。</p>
    <div class="immediate-maintenance-actions">
      <label>预计维护时长（小时）<input v-model.number="hours" type="number" min="1" max="168" step="1" :disabled="busy || active"/></label>
      <button type="button" class="maintenance-begin" :disabled="busy || !current || !supported || active || !validHours" @click="changeMaintenance(true)">{{ busy ? '处理中…' : '维护服务器' }}</button>
      <button type="button" :disabled="busy || !supported || !active" @click="changeMaintenance(false)">结束即时维护</button>
    </div>
    <p class="maintenance-note">预计时长仅用于广播，到时不会自动开放。与下方预约维护独立，不保存或覆盖本页其他编辑；结束即时维护不会取消预约计划。操作后保存预约区时如提示版本已变化，请先刷新配置。</p>
    <p v-if="notice" class="maintenance-result" :class="{ failed }" role="status" aria-live="polite">{{ notice }}</p>
  </section>
</template>

<style scoped>
.immediate-maintenance{padding:18px;border:1px solid #8e7540;background:#131b1e;margin-bottom:18px;color:#e7e8e1;font-size:14px;line-height:1.65}
.immediate-maintenance header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}
.immediate-maintenance h3{margin:0;font-size:20px;font-weight:800}.immediate-maintenance p{margin:8px 0}
.maintenance-state{color:#efcf7c;font-weight:700}.maintenance-note{color:#a1adad}
.immediate-maintenance-actions{display:flex;align-items:flex-end;flex-wrap:wrap;gap:12px;margin:16px 0}
.immediate-maintenance-actions label{display:flex;flex-direction:column;gap:6px;min-width:0}
.immediate-maintenance input{width:200px;max-width:100%;padding:10px;border:1px solid #647071;background:#0b1114;color:#fff}
.immediate-maintenance button{padding:10px 16px;min-height:42px;border:1px solid #718183;background:#15282b;color:#edf5ed;font-weight:700}
.immediate-maintenance button.maintenance-begin{border-color:#be9852;background:#3b2b16;color:#ffe2a4}
.immediate-maintenance button:focus-visible{outline:2px solid #70d5dd;outline-offset:2px}
.maintenance-result{padding:12px;border-left:3px solid #5bc5ac;background:#132c29}.maintenance-result.failed{border-color:#d67c7c;background:#361b22}
@media(max-width:600px){.immediate-maintenance header{flex-wrap:wrap}.immediate-maintenance-actions>*{width:100%}.immediate-maintenance input{width:100%}}
</style>
