<script setup lang="ts">
import { computed } from 'vue'
import type { Card } from './types'
import { isHorizontalCardType } from './cardPresentation'
import { roundCardUrl } from './specialAssets'
const props = defineProps<{ card: Card; selected?: boolean; compact?: boolean }>()
defineEmits<{ select: []; focusCard: [card: Card] }>()
const displayCost = computed(() => props.card.playCost ?? props.card.currentCost ?? props.card.cost)
const costState = computed(() => displayCost.value < props.card.cost ? 'discounted' : displayCost.value > props.card.cost ? 'increased' : '')
const isBattlefieldLegion = computed(() => props.card.cardType === 'legion' || props.card.isMasterLegion === true || props.card.cardId === 'S01-0417' && props.card.troops > 0)
const displayBaseTroops = computed(() => props.card.displayBaseTroops ?? props.card.baseTroops)
const attachedGroups = computed(() => {
  const groups = new Map<string, { card: Card; count: number }>()
  for (const card of props.card.attachedCards ?? []) {
    const current = groups.get(card.cardId)
    if (current) current.count += 1
    else groups.set(card.cardId, { card, count: 1 })
  }
  return [...groups.values()]
})
</script>

<template>
  <button class="card-tile" :class="[{ selected, tapped: card.tapped, compact, 'horizontal-card': isHorizontalCardType(card.cardType) }, `type-${card.cardType}`]" @click="$emit('select')">
    <img v-if="card.imageUrl" :src="card.imageUrl" :alt="card.name" @error="($event.target as HTMLImageElement).style.display='none'" />
    <span v-if="!card.hidden && card.hasPrintedCost !== false" class="card-cost" :class="costState" :title="displayCost === card.cost ? `印刷费用 ${card.cost}` : `当前费用 ${displayCost}；印刷费用 ${card.cost}`">{{ displayCost }}</span>
    <span v-if="!card.hidden && card.activeKeywords?.length" class="card-keyword-stack" aria-label="当前生效关键词">
      <b v-for="keyword in card.activeKeywords" :key="keyword" class="card-keyword">{{ keyword }}</b>
    </span>
    <span v-if="!card.hidden" class="card-name">{{ card.name }}</span>
    <span v-if="!card.hidden && isBattlefieldLegion" class="card-power"
      :class="{ boosted: card.troops > displayBaseTroops, weakened: card.troops < displayBaseTroops }"
      :title="card.troops === displayBaseTroops ? `当前兵力 ${card.troops}` : `当前兵力 ${card.troops}；比较基准 ${displayBaseTroops}`">{{ card.troops }}</span>
    <span v-if="!card.hidden && card.disasterLevel" class="card-disaster">{{ card.disasterLevel }}</span>
    <span v-if="!card.hidden && attachedGroups.length" class="attached-card-orbs" aria-label="叠放卡牌">
      <span v-for="group in attachedGroups" :key="group.card.cardId" class="attached-card-orb" role="button" tabindex="0"
        :title="`${group.card.name}${group.count > 1 ? ` ×${group.count}` : ''}`"
        @mouseenter.stop="$emit('focusCard', group.card)" @focus.stop="$emit('focusCard', group.card)"
        @click.stop="$emit('focusCard', group.card)" @keyup.enter.stop="$emit('focusCard', group.card)">
        <img v-if="roundCardUrl(group.card.cardId, group.card.imageUrl)" :src="roundCardUrl(group.card.cardId, group.card.imageUrl)" :alt="group.card.name" />
        <i v-else>{{ group.card.name.slice(0, 1) }}</i>
        <b v-if="group.count > 1">{{ group.count }}</b>
      </span>
    </span>
  </button>
</template>

<style scoped>
.card-keyword-stack{position:absolute;z-index:8;left:3px;top:25px;display:flex;max-width:calc(100% - 6px);flex-direction:column;align-items:flex-start;gap:2px;pointer-events:none}.card-keyword{display:block;max-width:100%;padding:2px 5px;overflow:hidden;border:1px solid rgba(255,255,255,.86);border-radius:2px;background:rgba(17,24,24,.94);box-shadow:0 1px 4px rgba(0,0,0,.72);color:#fff;font-family:"Microsoft YaHei",sans-serif;font-size:8px;font-weight:900;line-height:1.15;text-overflow:ellipsis;white-space:nowrap}.card-tile.compact .card-keyword-stack{top:21px;gap:1px}.card-tile.compact .card-keyword{padding:1px 3px;font-size:7px}
.card-cost.discounted{background:#174b31!important;color:#fff!important;border-color:#50b47d!important}.card-cost.increased{background:#651d28!important;color:#fff!important;border-color:#c85a67!important}
.attached-card-orbs{position:absolute;z-index:9;right:3px;bottom:27px;display:flex;max-width:calc(100% - 6px);flex-direction:row-reverse;align-items:center;gap:1px;pointer-events:auto}.attached-card-orb{position:relative;display:grid;width:22px;height:22px;min-width:22px;place-items:center;padding:0;overflow:visible;border:1px solid #d8d2bd;border-radius:50%;background:#090b0c;box-shadow:0 2px 7px #000;cursor:pointer}.attached-card-orb img{position:static!important;inset:auto!important;width:100%!important;height:100%!important;border-radius:50%;background:#090b0c;object-fit:cover;object-position:center 14%;transform:scale(1.08)}.attached-card-orb i{color:#eee;font-size:8px;font-style:normal;font-weight:900}.attached-card-orb b{position:absolute;z-index:2;left:50%;top:-9px;display:grid;min-width:15px;height:15px;place-items:center;padding:0 3px;border:1px solid #f0e7c8;border-radius:8px;background:#111;color:#fff;font-size:8px;line-height:1;transform:translateX(-50%)}.attached-card-orb:hover,.attached-card-orb:focus-visible{border-color:#72d9df;box-shadow:0 0 9px rgba(112,217,223,.8);outline:none}
.attached-card-orb{border-color:#f0f2ef;box-shadow:0 0 0 2px rgba(194,199,196,.72),0 2px 7px #000}.attached-card-orbs{gap:3px}
</style>
