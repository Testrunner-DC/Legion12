<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card } from '../types'

const props = defineProps<{
  cards?: Card[]
  count?: number
  hidden?: boolean
  selectedIds?: string[]
  playableIds?: string[]
  showPlayAction?: boolean
  dimUnplayable?: boolean
  playerIndex?: number
}>()
const emit = defineEmits<{ select: [card: Card]; focus: [card: Card]; play: [card: Card] }>()
const handElement = ref<HTMLElement | null>(null)
const handWidth = ref(900)
let resizeObserver: ResizeObserver | null = null
const cardCount = computed(() => props.hidden ? (props.count ?? 0) : (props.cards?.length ?? 0))
const cardWidth = computed(() => props.hidden ? 60 : 86)
const minimumStep = computed(() => props.hidden ? 20 : 26)
const maximumStep = computed(() => props.hidden ? 34 : 76)
const fanStep = computed(() => {
  if (cardCount.value <= 1) return 0
  return Math.max(minimumStep.value, Math.min(maximumStep.value, (handWidth.value - cardWidth.value) / (cardCount.value - 1)))
})
const fanTotalWidth = computed(() => cardCount.value <= 1 ? cardWidth.value : cardWidth.value + fanStep.value * (cardCount.value - 1))
const isOverflowing = computed(() => fanTotalWidth.value > handWidth.value + 1)
function fanStyle(index: number, count: number) {
  const offset = index - (count - 1) / 2
  const radius = Math.max(0.5, (count - 1) / 2)
  const centerLift = Math.max(0, radius - Math.abs(offset))
  return {
    '--fan-angle': `${Math.max(-7, Math.min(7, offset * 1.35))}deg`,
    '--fan-lift': `${Math.min(8, centerLift * 2.2)}px`,
    '--fan-shift': `${index === 0 ? 0 : fanStep.value - cardWidth.value}px`,
  }
}
onMounted(() => {
  if (!handElement.value) return
  resizeObserver = new ResizeObserver(entries => { handWidth.value = entries[0]?.contentRect.width ?? handWidth.value })
  resizeObserver.observe(handElement.value)
})
onBeforeUnmount(() => resizeObserver?.disconnect())
</script>

<template>
  <div ref="handElement" class="l12-hand" data-l12-zone="hand" :data-player-index="playerIndex" :class="{ hidden, 'playability-active': dimUnplayable, overflowing: isOverflowing }">
    <template v-if="hidden">
      <div v-for="index in count || 0" :key="index" class="card-back" :style="fanStyle(index - 1, count || 0)"><i>XII</i></div>
    </template>
    <div v-for="(card, index) in cards" v-else :key="card.instanceId" class="hand-card-wrap" :style="fanStyle(index, cards?.length || 0)"
      :class="{ playable: playableIds?.includes(card.instanceId), selected: selectedIds?.includes(card.instanceId) }">
      <div v-if="showPlayAction && card.cardType !== 'legion' && selectedIds?.includes(card.instanceId) && playableIds?.includes(card.instanceId)"
        class="card-context-actions hand-actions">
        <button @click.stop="emit('play', card)">打出</button>
      </div>
      <CardTile :card="card" :selected="selectedIds?.includes(card.instanceId)"
        @select="emit('select', card)" @mouseenter="emit('focus', card)" />
    </div>
  </div>
</template>

<style scoped>
.l12-hand>.hand-card-wrap,.l12-hand>.card-back{margin-left:var(--fan-shift,0)}
.l12-hand:not(.hidden)>.hand-card-wrap:not(:first-child){margin-left:calc(var(--fan-shift,0px) - 7px)}
.l12-hand.overflowing{justify-content:flex-start;overflow-x:auto;overflow-y:hidden;scrollbar-color:#5f6866 #111516;scrollbar-width:thin}
.l12-hand.overflowing>.hand-card-wrap,.l12-hand.overflowing>.card-back{flex:none}
.l12-hand.overflowing .hand-actions{top:3px;bottom:auto}
</style>
