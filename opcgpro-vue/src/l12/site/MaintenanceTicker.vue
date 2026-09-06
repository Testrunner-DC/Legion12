<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue'
import { getEffectiveOperationsPolicy } from '@/l12/platform'
import { l12State } from '@/l12/net'

// Mounted only in the shared battle section: every battle subpage keeps receiving
// the public policy even when the lobby component is no longer mounted.
const message = computed(() => {
  const maintenance = l12State.operationsPolicy?.maintenance
  if (!maintenance) return ''
  return maintenance.broadcastMessage || (maintenance.active ? maintenance.message : '')
})
let disposed = false
let refreshing = false
let timer = 0
async function refresh() {
  if (disposed || refreshing || document.hidden) return
  refreshing = true
  try {
    const policy = await getEffectiveOperationsPolicy()
    if (!disposed && policy.version >= (l12State.operationsPolicy?.version ?? 0))
      l12State.operationsPolicy = policy
  } catch {
    // A transient failure must not erase an already confirmed maintenance notice.
  } finally { refreshing = false }
}
function onVisibility() { if (!document.hidden) void refresh() }
onMounted(() => {
  void refresh()
  timer = window.setInterval(() => void refresh(), 5000)
  document.addEventListener('visibilitychange', onVisibility)
})
onBeforeUnmount(() => {
  disposed = true
  window.clearInterval(timer)
  document.removeEventListener('visibilitychange', onVisibility)
})
</script>

<template>
  <aside v-if="message" class="maintenance-ticker" data-ui-contract="battle-maintenance-ticker" role="status" aria-live="polite">
    <span class="maintenance-ticker-label">维护公告</span>
    <span class="maintenance-ticker-accessible">{{ message }}</span>
    <div class="maintenance-ticker-window" aria-hidden="true">
      <div :key="message" class="maintenance-ticker-track"><span>{{ message }}</span><span>{{ message }}</span></div>
    </div>
  </aside>
</template>

<style scoped>
.maintenance-ticker{position:sticky;top:0;z-index:25;display:flex;align-items:center;gap:12px;min-height:44px;padding:8px 16px;border-bottom:1px solid #8d763b;background:#26200f;color:#f3db90;font-size:14px;line-height:1.6}
.maintenance-ticker-label{flex:none;font-weight:800}
.maintenance-ticker-window{flex:1;min-width:0;overflow:hidden}
.maintenance-ticker-track{display:flex;width:max-content;min-width:200%;animation:maintenance-scroll 24s linear infinite}
.maintenance-ticker-track>span{flex:1 0 auto;min-width:50%;padding-right:64px;white-space:nowrap}
.maintenance-ticker:hover .maintenance-ticker-track,.maintenance-ticker:focus-within .maintenance-ticker-track{animation-play-state:paused}
.maintenance-ticker-accessible{position:absolute;width:1px;height:1px;padding:0;overflow:hidden;clip-path:inset(50%);white-space:nowrap}
@keyframes maintenance-scroll{to{transform:translateX(-50%)}}
@media(prefers-reduced-motion:reduce){.maintenance-ticker-track{animation:none;display:block;width:auto;min-width:0}.maintenance-ticker-track>span{display:block;white-space:normal;padding-right:0}.maintenance-ticker-track>span+span{display:none}}
</style>
