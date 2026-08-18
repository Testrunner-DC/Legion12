<script setup lang="ts">
import { computed } from 'vue'
import type { Card } from './types'
import { isHorizontalCardType } from './cardPresentation'
const props = defineProps<{ card: Card; selected?: boolean; compact?: boolean }>()
defineEmits<{ select: [] }>()
const displayCost = computed(() => props.card.playCost ?? props.card.currentCost ?? props.card.cost)
const costState = computed(() => displayCost.value < props.card.cost ? 'discounted' : displayCost.value > props.card.cost ? 'increased' : '')
</script>

<template>
  <button class="card-tile" :class="[{ selected, tapped: card.tapped, compact, 'horizontal-card': isHorizontalCardType(card.cardType) }, `type-${card.cardType}`]" @click="$emit('select')">
    <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name" @error="($event.target as HTMLImageElement).style.display='none'" />
    <span v-if="!card.hidden" class="card-cost" :class="costState" :title="displayCost === card.cost ? `印刷费用 ${card.cost}` : `当前费用 ${displayCost}；印刷费用 ${card.cost}`">{{ displayCost }}</span>
    <span v-if="!card.hidden" class="card-name">{{ card.name }}</span>
    <span v-if="!card.hidden && (card.cardType === 'legion' || card.cardId === 'S01-0417' && card.troops > 0)" class="card-power"
      :class="{ boosted: card.troops > card.baseTroops, weakened: card.troops < card.baseTroops }"
      :title="card.troops === card.baseTroops ? `印刷兵力 ${card.baseTroops}` : `当前兵力 ${card.troops}；印刷兵力 ${card.baseTroops}`">{{ card.troops }}</span>
    <span v-if="!card.hidden && card.disasterLevel" class="card-disaster">{{ card.disasterLevel }}</span>
    <span v-if="card.hasStrongAttack" class="card-status status-strong" title="强攻：进攻主宰时额外造成1点伤害">强</span>
    <span v-if="card.hasSureHit" class="card-status status-sure" title="必中：本次进攻不可被抵挡或支援">必</span>
    <span v-if="card.immortalUses && card.immortalUses > 0" class="card-status status-immortal" title="免死">免{{ card.immortalUses }}</span>
    <span v-if="card.hasCharge" class="card-status status-charge" title="冲锋：登场回合可以进攻">冲</span>
  </button>
</template>

<style scoped>
.card-status{position:absolute;z-index:6;right:3px;bottom:3px;display:grid;min-width:18px;height:18px;place-items:center;padding:0 3px;border:1px solid #fff;background:#202524;color:#fff;font-size:8px;font-weight:900;line-height:1}.status-strong{background:#245e38}.status-sure{right:25px;background:#28526d}.status-immortal{right:47px;background:#6e5b20}.status-charge{right:75px;background:#71333a}
.card-cost.discounted{background:#174b31!important;color:#fff!important;border-color:#50b47d!important}.card-cost.increased{background:#651d28!important;color:#fff!important;border-color:#c85a67!important}
</style>
