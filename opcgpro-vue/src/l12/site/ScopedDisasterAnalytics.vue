<script setup lang="ts">
import { ref } from 'vue'
import type { DeckCard } from '@/l12/decks'
import { scopedDisasterPath, useDisasterRead, type DisasterAnalyticsScope, type ScopedDisasterReport } from '@/l12/disasterAnalytics'
const props = defineProps<{ scope: DisasterAnalyticsScope; cardId?: string; deckSignature?: string; cards: DeckCard[]; title?: string }>()
const mode = ref('')
const origin = ref('')
const pool = ref('')
const { value: report, pending, error, load } = useDisasterRead<ScopedDisasterReport>(() => scopedDisasterPath(
  { ...props.scope, disasterMode: mode.value, disasterOrigin: origin.value, disasterPoolId: pool.value }, props.cardId, props.deckSignature))
const name = (id: string) => props.cards.find(card => card.id === id)?.nameZh || id
const percent = (rate?: number | null) => typeof rate === 'number' ? `${(rate * 100).toFixed(1)}%` : '—'
const step = (index: number) => ({ 0: '先手第1禁', 1: '后手禁用', 2: '先手第2禁', 4: '先手选择', 5: '后手选择' } as Record<number, string>)[index] || '未知步骤'
</script>
<template>
  <section class="scoped-disasters" aria-label="构筑天灾禁选分析">
    <header><h3>{{ title || '携带该卡的天灾禁选分析' }}</h3><button :disabled="pending" @click="load">{{ pending ? '读取中…' : report ? '刷新禁选分析' : '查看禁选分析' }}</button></header>
    <div class="filters"><label>天灾模式<select v-model="mode"><option value="">全部</option><option value="all">全部天灾</option><option value="season">赛季天灾</option><option value="random">随机天灾</option><option value="none">无天灾</option></select></label><label>决定来源<select v-model="origin"><option value="">全部</option><option value="player">玩家选择</option><option value="server-setup-timeout">超时代选</option></select></label><label>卡池快照<select v-model="pool"><option value="">全部</option><option v-for="item in report?.pools || []" :key="item.id" :value="item.id">{{ item.id.slice(0, 8) }} · {{ item.cardIds.length }}张</option><option v-if="pool && !report" :value="pool">已选卡池</option></select></label></div>
    <p v-if="error" role="alert">{{ error }}</p>
    <template v-if="report"><p>{{ report.coverage.participantSamples }} 份参赛方 / {{ report.coverage.distinctMatches }} 场；禁选可用 {{ report.coverage.usableSamples }} 份，历史未知 {{ report.coverage.unknownSamples }} 份，准备未完成 {{ report.coverage.incompleteSamples }} 份，无自主禁选 {{ report.coverage.notApplicableSamples }} 份。</p><p>沿用上方时间、版本、主宰和先后手范围，只统计实际携带该卡或完全一致构筑的参赛方。覆盖数不随天灾表筛选改变；镜像双方不是独立对局。</p><p v-if="report.privatePickGroupsHidden">当前权限仅展示公开禁用分布，未返回私密选牌机会。</p><div class="table-scroll" tabindex="0"><table v-if="report.items.length"><thead><tr><th>步骤</th><th>天灾</th><th>来源</th><th>次数 / 机会</th><th>比例</th><th>选择后胜率</th></tr></thead><tbody><tr v-for="row in report.items" :key="`${row.cardId}-${row.step}-${row.origin}`"><td>{{ step(row.step) }}</td><td>{{ name(row.cardId) }}</td><td>{{ row.origin === 'player' ? '玩家选择' : '超时代选' }}</td><td>{{ row.selections }} / {{ row.opportunities }}</td><td>{{ percent(row.selectionRate) }}</td><td>{{ percent(row.selectedWinRate) }}</td></tr></tbody></table><p v-else>当前范围没有可展示的禁选分布；未知不等于没有禁选。</p></div><p>比例按当时实际机会计算；选择后不足30份不显示胜率，相关性不代表因果。{{ report.poolsTruncated ? '卡池过多，仅列最近100个，请缩小范围。' : '' }}</p></template>
  </section>
</template>
<style scoped>
.scoped-disasters{min-width:0;margin:12px 0;padding:14px;border:1px solid #3b4a53;background:#101820;color:#e8e5db}.scoped-disasters header{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px}.scoped-disasters h3{margin:0}.scoped-disasters button,.scoped-disasters select{min-height:44px;padding:8px;background:#15212a;color:#e8e5db;border:1px solid #4a5d67}.scoped-disasters option{background:#101820;color:#e8e5db}.filters{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:12px}.filters label{display:grid;gap:5px;min-width:0}.filters select{max-width:100%;min-width:0}.scoped-disasters p{font-size:12px;line-height:1.65;color:#a7b4b8;overflow-wrap:anywhere}.table-scroll{max-width:100%;overflow:auto}.table-scroll table{width:100%;min-width:520px;border-collapse:collapse;font-size:12px}.table-scroll td,.table-scroll th{padding:8px;text-align:left;border-bottom:1px solid #354650}@media(max-width:700px){.filters{grid-template-columns:minmax(0,1fr)}}
</style>
