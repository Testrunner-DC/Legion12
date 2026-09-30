<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { platformState, seasonSummaryApi, type SeasonSummaryNotification } from '@/l12/platform'
import RankedIdentityBadge from '@/l12/RankedIdentityBadge.vue'

const props = defineProps<{ suspended?: boolean }>()
const notifications = ref<SeasonSummaryNotification[]>([])
const current = computed(() => props.suspended ? null : notifications.value[0] ?? null)
let accountGeneration = 0
let readGeneration = 0
const activeReads = new Map<number, symbol>()
const dirtyGenerations = new Set<number>()
const pendingAcknowledgements = new Map<string, symbol>()
const acknowledgedTombstones = new Set<string>()
const postAcknowledgeRefreshes = new Set<string>()

const notificationKey = (accountId: string, notificationId: string) => `${accountId}:${notificationId}`

async function refresh() {
  if (!platformState.account || !platformState.token) { notifications.value = []; return }
  const requestGeneration = readGeneration
  const accountId = platformState.account.id
  if (activeReads.has(requestGeneration)) { dirtyGenerations.add(requestGeneration); return }
  const request = Symbol('season-summary-read')
  activeReads.set(requestGeneration, request)
  dirtyGenerations.delete(requestGeneration)
  try {
    const result = await seasonSummaryApi.notifications()
    if (requestGeneration !== readGeneration || accountId !== platformState.account?.id) return
    const authoritativeIds = new Set(result.map(item => item.id))
    let requiresPostAcknowledgeRefresh = false
    for (const key of acknowledgedTombstones) {
      if (!key.startsWith(`${accountId}:`) || pendingAcknowledgements.has(key)) continue
      const id = key.slice(accountId.length + 1)
      if (!authoritativeIds.has(id)) {
        acknowledgedTombstones.delete(key)
        postAcknowledgeRefreshes.delete(key)
      } else if (postAcknowledgeRefreshes.delete(key)) requiresPostAcknowledgeRefresh = true
    }
    notifications.value = result.filter(item =>
      !acknowledgedTombstones.has(notificationKey(accountId, item.id)))
    if (requiresPostAcknowledgeRefresh) dirtyGenerations.add(requestGeneration)
  }
  catch { /* A transient read must not dismiss a persistent summary. */ }
  finally {
    if (activeReads.get(requestGeneration) === request) activeReads.delete(requestGeneration)
    const rerunCurrent = dirtyGenerations.delete(requestGeneration)
      && requestGeneration === readGeneration
    const readInvalidated = requestGeneration !== readGeneration
    if ((rerunCurrent || readInvalidated) && platformState.account && platformState.token
        && !document.hidden && !activeReads.has(readGeneration)) void refresh()
  }
}

async function acknowledge() {
  const item = current.value
  const accountId = platformState.account?.id
  if (!item || !accountId) return
  const acknowledgementAccountGeneration = accountGeneration
  const key = notificationKey(accountId, item.id)
  if (pendingAcknowledgements.has(key)) return
  const acknowledgement = Symbol('season-summary-acknowledgement')
  pendingAcknowledgements.set(key, acknowledgement)
  readGeneration += 1
  acknowledgedTombstones.add(key)
  notifications.value = notifications.value.filter(candidate => candidate.id !== item.id)
  try {
    await seasonSummaryApi.acknowledge(item.id)
    if (pendingAcknowledgements.get(key) !== acknowledgement) return
    pendingAcknowledgements.delete(key)
    if (accountId === platformState.account?.id) {
      postAcknowledgeRefreshes.add(key)
      void refresh()
    }
  } catch {
    if (pendingAcknowledgements.get(key) !== acknowledgement) return
    pendingAcknowledgements.delete(key)
    acknowledgedTombstones.delete(key)
    postAcknowledgeRefreshes.delete(key)
    if (accountId !== platformState.account?.id) return
    if (acknowledgementAccountGeneration === accountGeneration
        && !notifications.value.some(candidate => candidate.id === item.id))
      notifications.value = [item, ...notifications.value]
    void refresh()
  }
}

function percent(value?: number | null) { return value == null ? '—' : `${value.toFixed(1)}%` }
function onResource() { void refresh() }
watch(() => platformState.account?.id, () => {
  accountGeneration += 1
  readGeneration += 1
  notifications.value = []
  if (platformState.account && platformState.token) void refresh()
})
onMounted(() => {
  void refresh()
  window.addEventListener('l12-resource-seasonSummaryNotifications', onResource)
})
onBeforeUnmount(() => window.removeEventListener('l12-resource-seasonSummaryNotifications', onResource))
</script>

<template>
  <div v-if="current" class="site-modal-mask season-summary-mask">
    <section class="site-modal season-summary" role="dialog" aria-modal="true" aria-labelledby="season-summary-title">
      <header><div><small>SEASON COMPLETE</small><h2 id="season-summary-title">{{ current.seasonName }} · 赛季总结</h2></div></header>
      <p class="season-summary-lead">这份最终记录已保存到“我的”页面。</p>
      <div class="season-summary-grid">
        <article><span>派系</span><b>{{ current.faction }}</b></article>
        <article><span>最终段位</span><RankedIdentityBadge variant="tier" :faction="current.faction" :label="current.rankLabel"/></article>
        <article><span>派系名次</span><b>{{ current.factionRank ? `第 ${current.factionRank} 名` : (current.placed ? '历史版本未记录' : '未完成定级') }}</b></article>
        <article v-if="current.overallRank"><span>全服名次</span><b>第 {{ current.overallRank }} 名</b></article>
        <article><span>七曜值</span><b>{{ current.sevenValue.toLocaleString() }}</b></article>
        <article><span>胜率</span><b>{{ percent(current.winRate) }}</b></article>
      </div>
      <div v-if="current.titles.length" class="season-summary-titles"><RankedIdentityBadge v-for="title in current.titles" :key="title" :variant="current.masterTitles.includes(title) ? 'master-title' : 'faction-title'" :faction="current.faction" :label="title"/></div>
      <button class="season-summary-confirm" type="button" @click="acknowledge">保存并确认</button>
    </section>
  </div>
</template>

<style scoped>
.site-modal-mask{box-sizing:border-box;position:fixed;z-index:179;inset:0;display:grid;place-items:center;padding:max(20px,env(safe-area-inset-top)) max(20px,env(safe-area-inset-right)) max(20px,env(safe-area-inset-bottom)) max(20px,env(safe-area-inset-left));background:rgba(1,4,7,.78);backdrop-filter:blur(10px)}.site-modal{padding:24px;border:1px solid rgba(235,230,216,.28);background:#111923;box-shadow:0 28px 90px #000;color:#f2f0e9}.site-modal>header{padding-bottom:15px;border-bottom:1px solid rgba(235,230,216,.14)}.site-modal header small{color:#51c5cc;font:900 14px monospace;letter-spacing:.18em}.site-modal h2{margin:4px 0 0;font-size:24px}.season-summary{box-sizing:border-box;width:min(700px,92vw);max-width:100%;max-height:calc(100dvh - max(40px,env(safe-area-inset-top) + env(safe-area-inset-bottom)));overflow-x:hidden;overflow-y:auto}.season-summary-lead{color:#aab5b8}.season-summary-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:9px;margin-top:16px}.season-summary-grid article{min-width:0;padding:13px;border:1px solid #4d3470;background:#100c1c}.season-summary-grid span,.season-summary-grid b{display:block}.season-summary-grid span{color:#9986ac;font-size:13px}.season-summary-grid b{margin-top:6px;overflow-wrap:anywhere;color:#eadbff}.season-summary-titles{display:flex;flex-wrap:wrap;gap:8px;margin-top:14px}.season-summary-confirm{width:100%;min-height:44px;margin-top:20px;padding:12px;border:1px solid #e1c16c;background:#e1c16c;color:#080b0d;font-weight:900}
@media(max-width:700px){.season-summary-mask{align-items:center;padding:max(14px,env(safe-area-inset-top)) max(14px,env(safe-area-inset-right)) max(14px,env(safe-area-inset-bottom)) max(14px,env(safe-area-inset-left))}.season-summary{width:100%;max-height:calc(100dvh - max(28px,env(safe-area-inset-top) + env(safe-area-inset-bottom)))}.season-summary-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:360px){.season-summary-grid{grid-template-columns:1fr}}
</style>
