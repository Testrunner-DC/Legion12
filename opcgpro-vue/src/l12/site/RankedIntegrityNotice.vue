<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { platformState } from '../platform'
import { integrityApi, integrityLabel, type IntegrityNotification } from '../rankedIntegrity'
import RankedAppealForm from './RankedAppealForm.vue'
const items = ref<IntegrityNotification[]>([])
const current = computed(() => items.value[0])
const busy = ref(false); const error = ref(''); const appealing = ref(false)
let generation = 0; let reading = false; let timer = 0
async function refresh() {
  const account = platformState.account?.id
  if (!account || platformState.account?.mustChangePassword || platformState.account?.mustChangeUsername || reading || busy.value || document.hidden) return
  const version = generation; reading = true
  try {
    const page = await integrityApi.notifications(undefined, true)
    if (version === generation && account === platformState.account?.id) items.value = page.items
  } catch { /* Preserve unacknowledged notice on transient failures. */ }
  finally { reading = false }
}
async function acknowledge() {
  const item = current.value; const account = platformState.account?.id; const version = generation
  if (!item || busy.value) return
  busy.value = true; error.value = ''
  try {
    await integrityApi.acknowledge(item.id)
    if (version !== generation || account !== platformState.account?.id) return
    items.value = items.value.filter(row => row.id !== item.id); appealing.value = false
    window.dispatchEvent(new Event('l12-integrity-changed'))
  } catch (cause) { if (version === generation) error.value = cause instanceof Error ? cause.message : '确认失败，请重试' }
  finally { busy.value = false; void refresh() }
}
watch(() => platformState.account?.id, () => { generation++; items.value = []; error.value = ''; appealing.value = false; void refresh() })
watch(() => current.value?.id, () => { appealing.value = false })
function changed() { void refresh() }
onMounted(() => { void refresh(); timer = window.setInterval(changed, 20000); window.addEventListener('focus', changed); document.addEventListener('visibilitychange', changed); window.addEventListener('l12-integrity-changed', changed) })
onBeforeUnmount(() => { generation++; window.clearInterval(timer); window.removeEventListener('focus', changed); document.removeEventListener('visibilitychange', changed); window.removeEventListener('l12-integrity-changed', changed) })
</script>
<template>
  <Teleport to="body"><section v-if="current" class="integrity-notice" role="dialog" aria-modal="false" aria-labelledby="integrity-notice-title" data-ui-contract="ranked-integrity-result-notice">
    <h2 id="integrity-notice-title">排位处理结果 <small v-if="items.length > 1">另有 {{ items.length - 1 }} 条</small></h2>
    <strong>{{ integrityLabel(current.outcome) }}</strong><time>{{ new Date(current.decidedAt).toLocaleString() }}</time>
    <p>{{ current.reason }}</p>
    <p v-if="current.scoreDelta">本次七曜值调整：{{ current.scoreDelta > 0 ? '+' : '' }}{{ current.scoreDelta }}</p>
    <p v-if="current.restrictionUntil">排位限制截至：{{ new Date(current.restrictionUntil).toLocaleString() }}</p>
    <p>{{ current.appealGuidance }}</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <RankedAppealForm v-if="appealing" :key="current.decisionId" :decision-id="current.decisionId"/>
    <footer><button v-if="current.decisionId" :disabled="busy" @click="appealing = !appealing">{{ appealing ? '收起申诉' : '我要申诉' }}</button><router-link to="/me">判罚历史</router-link><button :disabled="busy" @click="acknowledge">{{ busy ? '确认中…' : '我知道了' }}</button></footer>
  </section></Teleport>
</template>
<style scoped>
.integrity-notice{position:fixed;z-index:4700;right:20px;bottom:20px;box-sizing:border-box;width:min(460px,calc(100vw - 40px));max-height:calc(100dvh - 40px);overflow:auto;padding:22px;border:1px solid #c9a45c;background:#111b25;color:#e9eee9;box-shadow:0 14px 48px #000b;font-family:var(--l12-font-family,'Microsoft YaHei',sans-serif);font-size:14px;line-height:1.65;overflow-wrap:anywhere}.integrity-notice h2{display:flex;gap:12px;justify-content:space-between;margin:0 0 14px;font-size:20px}.integrity-notice small{font-size:13px;color:#a8b7c0}.integrity-notice strong{color:#eed083;font-size:17px}.integrity-notice time{display:block;color:#a2b2bf}.integrity-notice p{white-space:pre-wrap}.integrity-notice footer{display:flex;align-items:center;justify-content:flex-end;flex-wrap:wrap;gap:10px;margin-top:16px}.integrity-notice button,.integrity-notice a{min-height:40px;box-sizing:border-box;padding:8px 12px;border:1px solid #796840;background:#302816;color:#f0dca0;text-decoration:none;font:inherit}.integrity-notice button:disabled{opacity:.55}
</style>
