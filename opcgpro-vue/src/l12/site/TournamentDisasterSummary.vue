<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import VisibleDisasterSummary from './VisibleDisasterSummary.vue'
const props = defineProps<{ code: string; rounds: Array<{ number: number; matches: Array<{ table: number; status: string; result?: string | null }> }> }>()
const selected = ref('')
const tables = computed(() => props.rounds.flatMap(round => round.matches
  .filter(match => match.status === 'completed' && match.result !== 'bye')
  .map(match => ({ key: `${round.number}/${match.table}`, round: round.number, table: match.table }))))
watch([() => props.code, () => JSON.stringify(tables.value)], () => { selected.value = '' }, { flush: 'sync' })
const path = computed(() => { const item = tables.value.find(row => row.key === selected.value)
  return item ? `/api/public/tournaments/code/${encodeURIComponent(props.code)}/rounds/${item.round}/tables/${item.table}/disasters` : '' })
</script>
<template><section class="tournament-disasters" aria-label="赛事天灾禁选记录"><label>已结束桌次<select v-model="selected"><option value="">选择桌次查看天灾记录</option><option v-for="item in tables" :key="item.key" :value="item.key">第{{ item.round }}轮 · 第{{ item.table }}桌</option></select></label><VisibleDisasterSummary v-if="path" :key="path" :path="path" /><p v-if="!tables.length">暂无已结束桌次。轮空、人工赛果或未绑定真实对局不生成禁选样本。</p></section></template>
<style scoped>.tournament-disasters{min-width:0;padding:14px;border:1px solid #34434d;background:#101820;color:#e8e5db}.tournament-disasters label{display:grid;gap:8px}.tournament-disasters select,.tournament-disasters option{min-height:44px;max-width:100%;padding:8px;background:#101820;color:#e8e5db;border:1px solid #4a5d67}.tournament-disasters p{font-size:12px;color:#a7b4b8;line-height:1.65}.tournament-disasters .visible-disasters{margin-top:12px}</style>
