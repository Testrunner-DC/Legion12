<script setup lang="ts">
import { useDisasterRead, type PublicDisasterStatistics } from '@/l12/disasterAnalytics'
import type { DeckCard } from '@/l12/decks'
const props = defineProps<{ reference: string; readToken: string; cards: DeckCard[] }>()
const { value: report, pending, error, load } = useDisasterRead<PublicDisasterStatistics>(() =>
  props.reference && props.readToken ? `/api/public-decks/${encodeURIComponent(props.reference)}/disasters?expectedReadToken=${encodeURIComponent(props.readToken)}` : '')
const name = (id: string) => props.cards.find(card => card.id === id)?.nameZh || id
const rate = (value?: number | null) => typeof value === 'number' ? `${(value * 100).toFixed(1)}%` : '—'
</script>
<template><section class="public-disasters" aria-label="公开构筑天灾禁用统计"><header><h3>天灾禁用统计</h3><button :disabled="pending" @click="load">{{ pending ? '读取中…' : report ? '刷新' : '查看统计' }}</button></header><p v-if="error" role="alert">{{ error }}</p><template v-if="report"><p v-if="report.status === 'insufficient'">可用对局不足 {{ report.minimumMatches }} 场，暂不展示分布；历史未知不等于零禁用。</p><template v-else><p>{{ report.usableMatches }} 场可用对局</p><ul><li v-for="item in report.items" :key="item.cardId"><b>{{ name(item.cardId) }}</b><span>{{ item.bans }} / {{ item.opportunities }} 次机会 · {{ rate(item.banRate) }}</span></li></ul><p v-if="!report.items.length">暂无达到展示门槛的公开禁用分布。</p></template></template><p>固定最近90个UTC日、真实绑定此公开牌库各版本的有效排位，仅展示公开禁用；不包含玩家私密选牌。不足3场的分组隐藏，相关性不代表因果。</p></section></template>
<style scoped>.public-disasters{min-width:0;padding:14px;border:1px solid #34434d;background:#101820;color:#e8e5db}.public-disasters header,.public-disasters li{display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px}.public-disasters h3{margin:0}.public-disasters button{min-height:44px;padding:8px 12px;border:1px solid #4a5d67;background:#15212a;color:#e8e5db}.public-disasters p{font-size:12px;line-height:1.65;color:#a7b4b8;overflow-wrap:anywhere}.public-disasters ul{list-style:none;padding:0}.public-disasters li{padding:8px 0;border-bottom:1px solid #34434d;overflow-wrap:anywhere}</style>
