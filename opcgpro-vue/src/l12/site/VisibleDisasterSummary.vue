<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { useDisasterRead, type VisibleDisasterSetup } from '@/l12/disasterAnalytics'
const props = defineProps<{ path: string }>()
const cards = ref<DeckCard[]>([])
const { value: summary, pending, error, load } = useDisasterRead<VisibleDisasterSetup>(() => props.path)
onMounted(async () => { try { cards.value = await loadDeckCatalog() } catch { /* IDs remain a truthful fallback. */ } })
const name = (id?: string | null) => id ? cards.value.find(card => card.id === id)?.nameZh || id : '未记录'
const step = (index: number) => ({ 0: '先手第1禁', 1: '后手禁用', 2: '先手第2禁', 4: '先手选择', 5: '后手选择' } as Record<number, string>)[index] || '未记录'
</script>
<template>
  <section class="visible-disasters" aria-label="天灾禁选记录"><button :disabled="pending" @click="load">{{ pending ? '读取中…' : summary ? '刷新天灾记录' : '查看天灾禁选记录' }}</button><p v-if="error" role="alert">{{ error }}</p><template v-if="summary"><p v-if="summary.coverage === 'unknown'">历史天灾记录未知，不代表本局没有禁选。</p><p v-else-if="summary.mode === 'random' || summary.mode === 'none'">本模式没有玩家自主禁选。</p><p v-else-if="summary.status !== 'complete'">准备未完成，仅展示已记录的成功决定。</p><ol><li v-for="decision in summary.decisions" :key="decision.step"><b>{{ step(decision.step) }}</b>：{{ name(decision.chosenCardId) }}<small v-if="decision.origin === 'server-setup-timeout'">（超时代选）</small><details v-if="decision.ownCandidateCardIds.length"><summary>本人当时的候选</summary>{{ decision.ownCandidateCardIds.map(name).join('、') }}</details></li></ol><p v-if="summary.visibleCompositionCardIds.length">{{ summary.visibility === 'self' ? '已公开或本人选入' : '准备阶段公开' }}：{{ summary.visibleCompositionCardIds.map(name).join('、') }}</p><p>不展示对手未公开的候选、选择或完整天灾构成；此处也不是实际触发记录。</p></template></section>
</template>
<style scoped>
.visible-disasters{min-width:0;padding:12px;border:1px solid #34434d;background:#101820;color:#e8e5db}.visible-disasters button{min-height:44px;padding:8px 12px;border:1px solid #4a5d67;background:#15212a;color:#e8e5db}.visible-disasters p,.visible-disasters li{line-height:1.65;overflow-wrap:anywhere}.visible-disasters p,.visible-disasters small{font-size:12px;color:#a7b4b8}.visible-disasters ol{padding-left:20px}.visible-disasters summary{cursor:pointer;min-height:32px}.visible-disasters details{padding:4px 0}
</style>
