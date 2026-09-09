<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { platformState } from '../platform'
import { integrityApi, integrityLabel, type IntegrityNotification, type IntegrityAppeal } from '../rankedIntegrity'
import RankedAppealForm from './RankedAppealForm.vue'
const items = ref<IntegrityNotification[]>([]); const appeals = ref<IntegrityAppeal[]>([])
const cursor = ref<string | null>(); const appealCursor = ref<string | null>()
const loading = ref(false); const notice = ref(''); const selected = ref('')
let generation = 0
const statusLabel = (value: string) => ({ open: '待处理', reviewing: '复核中', answered: '已回复', closed: '已结案' }[value] || value)
async function load(more = false) {
  const account = platformState.account?.id; if (!account || loading.value) return
  const version = generation; loading.value = true; notice.value = ''
  try {
    const page = await integrityApi.notifications(more ? cursor.value || undefined : undefined)
    if (version !== generation || account !== platformState.account?.id) return
    items.value = more ? [...items.value, ...page.items] : page.items; cursor.value = page.nextCursor
    if (!more) {
      const history = await integrityApi.appeals()
      if (version !== generation || account !== platformState.account?.id) return
      appeals.value = history.items; appealCursor.value = history.nextCursor
    }
  } catch (cause) { if (version === generation) notice.value = cause instanceof Error ? cause.message : '加载失败，请重试' }
  finally { loading.value = false }
}
async function moreAppeals() {
  if (loading.value || !appealCursor.value) return
  const version = generation; loading.value = true
  try { const page = await integrityApi.appeals(appealCursor.value); if (version === generation) { appeals.value.push(...page.items); appealCursor.value = page.nextCursor } }
  catch (cause) { if (version === generation) notice.value = cause instanceof Error ? cause.message : '加载失败' }
  finally { loading.value = false }
}
watch(() => platformState.account?.id, () => { generation++; items.value = []; appeals.value = []; cursor.value = null; appealCursor.value = null; selected.value = ''; void load() })
function changed() { void load() }
onMounted(() => { void load(); window.addEventListener('l12-integrity-changed', changed) })
onBeforeUnmount(() => { generation++; window.removeEventListener('l12-integrity-changed', changed) })
</script>
<template>
  <section v-if="platformState.account" class="penalty-history" data-ui-contract="ranked-penalty-history">
    <header><div><h2>判罚历史与申诉</h2><p>仅本人可见。保留处置、撤销及收益调整记录；申诉不会自动解除限制。</p></div><button :disabled="loading" @click="load()">刷新</button></header>
    <p v-if="notice" role="alert">{{ notice }}</p>
    <div class="history-scroll"><article v-for="item in items" :key="item.id">
      <h3>{{ integrityLabel(item.outcome) }} <small>{{ new Date(item.decidedAt).toLocaleString() }}</small></h3>
      <p>{{ item.reason }}</p><p v-if="item.scoreDelta">七曜值调整 {{ item.scoreDelta > 0 ? '+' : '' }}{{ item.scoreDelta }}</p>
      <p v-if="item.restrictionUntil">排位限制截至 {{ new Date(item.restrictionUntil).toLocaleString() }}</p>
      <details><summary>关联对局（{{ item.matchIds.length }}）</summary><span v-for="id in item.matchIds" :key="id" class="match-id">{{ id }}</span></details>
      <button v-if="item.decisionId" @click="selected = selected === item.id ? '' : item.id">{{ selected === item.id ? '收起' : '我要申诉' }}</button>
      <RankedAppealForm v-if="selected === item.id" :key="item.decisionId" :decision-id="item.decisionId"/>
    </article><p v-if="!loading && !items.length">暂无处置记录。</p></div>
    <button v-if="cursor" :disabled="loading" @click="load(true)">加载更多记录</button>
    <h3>我的申诉</h3><div class="history-scroll"><article v-for="appeal in appeals" :key="appeal.id"><h4>{{ statusLabel(appeal.status) }} · {{ new Date(appeal.createdAt).toLocaleString() }}</h4><p>{{ appeal.statement }}</p><p v-if="appeal.reply"><b>管理员回复：</b>{{ appeal.reply }}</p></article><p v-if="!appeals.length">暂无申诉。</p></div>
    <button v-if="appealCursor" :disabled="loading" @click="moreAppeals">加载更多申诉</button>
  </section>
</template>
<style scoped>
.penalty-history{margin:12px 0;padding:20px;border:1px solid #4f5e65;background:#111b24;color:#e5eceb;overflow-wrap:anywhere}.penalty-history header{display:flex;justify-content:space-between;align-items:start;gap:14px}.penalty-history h2{margin:0;font-size:20px}.penalty-history h3{font-size:16px}.penalty-history small{font-weight:400;color:#9fb0ba}.penalty-history p{line-height:1.65;white-space:pre-wrap}.penalty-history header p{color:#a8b7be}.history-scroll{max-height:480px;overflow:auto;overscroll-behavior:contain}.penalty-history article{padding:14px 0;border-top:1px solid #354954}.penalty-history button{margin-top:8px;min-height:40px;padding:8px 14px;border:1px solid #6c6449;background:#282819;color:#eddfb4;font:inherit}.penalty-history button:disabled{opacity:.5}.match-id{display:block;color:#aabfc9;font-size:13px}
</style>
