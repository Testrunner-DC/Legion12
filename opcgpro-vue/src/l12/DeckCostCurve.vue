<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{ values: number[]; expanded?: boolean }>(), { expanded: false })
const maxValue = computed(() => Math.max(1, ...props.values))
function barHeight(value: number) { return `${Math.max(3, value / maxValue.value * 100)}%` }
</script>

<template>
  <div class="deck-cost-curve" :class="{ expanded }" aria-label="费用曲线">
    <i v-for="(value,index) in values" :key="index">
      <span class="bar-track"><span class="bar" :style="{ height: barHeight(value) }"></span></span>
      <b>{{ index === 8 ? '8+' : index }}</b>
      <small>{{ value }}</small>
    </i>
  </div>
</template>

<style scoped>
.deck-cost-curve{display:grid;grid-template-columns:repeat(9,minmax(0,1fr));align-items:stretch;gap:clamp(4px,1vw,14px);height:112px;padding:10px clamp(4px,1vw,14px);overflow:hidden;border-top:1px solid #333;border-bottom:1px solid #333}.deck-cost-curve i{display:grid;min-width:0;grid-template-rows:minmax(0,1fr) 18px 18px;align-items:center;justify-items:center;font-style:normal}.bar-track{display:flex;width:100%;height:100%;align-items:flex-end;justify-content:center;overflow:hidden}.bar{display:block;width:clamp(8px,52%,24px);min-height:3px;background:linear-gradient(#d2b560,#7f6530)}.deck-cost-curve b,.deck-cost-curve small{font-size:12px;line-height:18px}.deck-cost-curve small{color:#8d9692}.deck-cost-curve.expanded{height:174px}.deck-cost-curve.expanded i{grid-template-rows:minmax(0,1fr) 20px 20px}.deck-cost-curve.expanded b,.deck-cost-curve.expanded small{font-size:13px}@media(max-width:620px){.deck-cost-curve{gap:3px;padding-inline:2px}.bar{width:min(70%,18px)}}
</style>
