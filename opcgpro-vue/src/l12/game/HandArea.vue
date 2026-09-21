<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import CardTile from '../CardTile.vue'
import type { Card } from '../types'
import { landscapeTeleportTarget } from '../mobileViewport'

const props = defineProps<{
  cards?: Card[]
  count?: number
  hidden?: boolean
  selectedIds?: string[]
  playableIds?: string[]
  showPlayAction?: boolean
  confirmAllPlayable?: boolean
  mobileLayout?: boolean
  dimUnplayable?: boolean
  playerIndex?: number
}>()
const emit = defineEmits<{ select: [card: Card]; focus: [card: Card]; play: [card: Card] }>()
const handElement = ref<HTMLElement | null>(null)
const handWidth = ref(900)
const moreAtStart = ref(false)
const moreAtEnd = ref(false)
let resizeObserver: ResizeObserver | null = null
let handDrag: { pointerId: number; startX: number; startScroll: number; moved: boolean } | null = null
let suppressNextClick = false
const cardCount = computed(() => props.hidden ? (props.count ?? 0) : (props.cards?.length ?? 0))
// Confirmed batch-299 layout: hand cards match battlefield legion dimensions.
const cardWidth = computed(() => 114.4)
const minimumStep = computed(() => 30)
const maximumStep = computed(() => 90)
const fanStep = computed(() => {
  if (cardCount.value <= 1) return 0
  return Math.max(minimumStep.value, Math.min(maximumStep.value, (handWidth.value - cardWidth.value) / (cardCount.value - 1)))
})
const fanTotalWidth = computed(() => cardCount.value <= 1 ? cardWidth.value : cardWidth.value + fanStep.value * (cardCount.value - 1))
const isOverflowing = computed(() => fanTotalWidth.value > handWidth.value + 1)
function updateOverflowEdges() {
  const element = handElement.value
  if (!element || !props.mobileLayout) {
    moreAtStart.value = false
    moreAtEnd.value = false
    return
  }
  const overflowing = element.scrollWidth > element.clientWidth + 2
  moreAtStart.value = overflowing && element.scrollLeft > 2
  moreAtEnd.value = overflowing && element.scrollLeft + element.clientWidth < element.scrollWidth - 2
}
function onHandWheel(event: WheelEvent) {
  const element = handElement.value
  if (!props.mobileLayout || !element || element.scrollWidth <= element.clientWidth + 2) return
  const delta = Math.abs(event.deltaX) > Math.abs(event.deltaY) ? event.deltaX : event.deltaY
  if (!delta) return
  event.preventDefault()
  element.scrollLeft += delta
  updateOverflowEdges()
}
function onHandPointerDown(event: PointerEvent) {
  const element = handElement.value
  if (!props.mobileLayout || event.button !== 0 || !element || element.scrollWidth <= element.clientWidth + 2) return
  handDrag = { pointerId: event.pointerId, startX: event.clientX, startScroll: element.scrollLeft, moved: false }
}
function onHandPointerMove(event: PointerEvent) {
  const element = handElement.value
  if (!element || !handDrag || handDrag.pointerId !== event.pointerId) return
  if (!handDrag.moved && Math.abs(event.clientX - handDrag.startX) < 5) return
  if (!handDrag.moved) {
    handDrag.moved = true
    element.setPointerCapture?.(event.pointerId)
  }
  event.preventDefault()
  element.scrollLeft = handDrag.startScroll - (event.clientX - handDrag.startX)
  updateOverflowEdges()
}
function endHandPointer(event: PointerEvent) {
  const element = handElement.value
  if (!element || !handDrag || handDrag.pointerId !== event.pointerId) return
  if (element.hasPointerCapture?.(event.pointerId)) element.releasePointerCapture?.(event.pointerId)
  suppressNextClick = handDrag.moved
  handDrag = null
  updateOverflowEdges()
  if (suppressNextClick) setTimeout(() => { suppressNextClick = false }, 0)
}
function onHandClickCapture(event: MouseEvent) {
  if (!suppressNextClick) return
  event.preventDefault()
  event.stopImmediatePropagation()
  suppressNextClick = false
}
function fanStyle(index: number, count: number) {
  const offset = index - (count - 1) / 2
  const radius = Math.max(0.5, (count - 1) / 2)
  const centerLift = Math.max(0, radius - Math.abs(offset))
  const angleLimit = count >= 12 ? 4 : 7
  return {
    '--fan-angle': `${Math.max(-angleLimit, Math.min(angleLimit, offset * 1.35))}deg`,
    '--fan-lift': `${Math.min(8, centerLift * 2.2)}px`,
    '--fan-shift': `${index === 0 ? 0 : fanStep.value - cardWidth.value}px`,
  }
}
onMounted(() => {
  if (!handElement.value) return
  resizeObserver = new ResizeObserver(entries => {
    handWidth.value = entries[0]?.contentRect.width ?? handWidth.value
    updateOverflowEdges()
  })
  resizeObserver.observe(handElement.value)
  nextTick(updateOverflowEdges)
})
watch(() => `${props.mobileLayout}:${cardCount.value}`, () => nextTick(updateOverflowEdges))
onBeforeUnmount(() => resizeObserver?.disconnect())
</script>

<template>
  <div ref="handElement" class="l12-hand" data-l12-zone="hand" data-ui-contract="field-sized-safe-hand" :data-player-index="playerIndex"
    :data-more-start="moreAtStart" :data-more-end="moreAtEnd" :class="{ hidden, 'playability-active': dimUnplayable, overflowing: isOverflowing, 'mobile-layout': mobileLayout }"
    @scroll="updateOverflowEdges" @wheel="onHandWheel" @pointerdown="onHandPointerDown" @pointermove="onHandPointerMove"
    @pointerup="endHandPointer" @pointercancel="endHandPointer" @click.capture="onHandClickCapture">
    <template v-if="hidden">
      <div v-for="index in count || 0" :key="index" class="card-back" :style="fanStyle(index - 1, count || 0)"><i>XII</i></div>
    </template>
    <div v-for="(card, index) in cards" v-else :key="card.instanceId" class="hand-card-wrap" :style="fanStyle(index, cards?.length || 0)"
      :class="{ playable: playableIds?.includes(card.instanceId), selected: selectedIds?.includes(card.instanceId) }">
      <Teleport :to="landscapeTeleportTarget()" :disabled="!mobileLayout">
        <div v-if="showPlayAction && (confirmAllPlayable || card.cardType !== 'legion') && selectedIds?.includes(card.instanceId) && playableIds?.includes(card.instanceId)"
          class="card-context-actions hand-actions" :class="{ 'mobile-action-dock': mobileLayout }">
          <button @click.stop="emit('play', card)">打出</button>
        </div>
      </Teleport>
      <CardTile :card="card" :selected="selectedIds?.includes(card.instanceId)"
        @select="emit('select', card)" @mouseenter="emit('focus', card)" />
    </div>
  </div>
</template>

<style scoped>
/* Reserve real layout space for the selected card's action, fan lift and tilt.
   The clock is in the adjacent normal-flow lane, not an overlay to out-z-index. */
.l12-hand{box-sizing:border-box;padding-inline:8px}.l12-hand:not(.hidden){height:160px;padding-top:18px;box-sizing:border-box}.l12-hand.opponent-hand:not(.hidden){padding-top:0;padding-bottom:18px;align-items:flex-start}
.l12-hand>.hand-card-wrap,.l12-hand>.card-back{margin-left:var(--fan-shift,0)}
.l12-hand>.hand-card-wrap:not(:first-child),.l12-hand>.card-back:not(:first-child){margin-left:calc(var(--fan-shift,0px) - 7px)}
.l12-hand.overflowing{justify-content:flex-start;overflow-x:auto;overflow-y:hidden;scrollbar-color:#5f6866 #111516;scrollbar-width:thin}
.l12-hand.overflowing>.hand-card-wrap,.l12-hand.overflowing>.card-back{flex:none}
.l12-hand.overflowing .hand-actions{top:auto;bottom:calc(100% + 4px)}
.l12-hand.overflowing.opponent-hand .hand-actions{top:calc(100% + 4px);bottom:auto}
.l12-hand .hand-card-wrap{width:114.4px;height:160.6px;flex-basis:114.4px}.l12-hand .hand-card-wrap .card-tile{width:114.4px;height:160.6px;flex-basis:114.4px;border:1px solid transparent;border-radius:0;box-shadow:none}.l12-hand .hand-card-wrap .card-tile:hover{border-color:transparent;box-shadow:none}.l12-hand .hand-card-wrap.playable::after{content:'';position:absolute;z-index:14;left:50%;bottom:2px;width:22px;height:3px;background:#62c5cc;box-shadow:0 0 6px rgba(70,185,195,.72);transform:translateX(-50%);pointer-events:none}.l12-hand .hand-card-wrap.selected .card-tile,.l12-hand .hand-card-wrap .card-tile:focus-visible{border-color:transparent;box-shadow:none;outline:2px solid #f4f0df;outline-offset:1px}.l12-hand.hidden .card-back{box-sizing:border-box;width:114.4px;height:160.6px}
.l12-hand.mobile-layout{scrollbar-width:none;touch-action:pan-x;overscroll-behavior-inline:contain}
.l12-hand.mobile-layout::-webkit-scrollbar{display:none}
.l12-hand.mobile-layout[data-more-start="false"][data-more-end="true"]{box-shadow:inset -14px 0 12px -12px #75d5de}
.l12-hand.mobile-layout[data-more-start="true"][data-more-end="false"]{box-shadow:inset 14px 0 12px -12px #75d5de}
.l12-hand.mobile-layout[data-more-start="true"][data-more-end="true"]{box-shadow:inset 14px 0 12px -12px #75d5de,inset -14px 0 12px -12px #75d5de}
</style>
