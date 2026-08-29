<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import CardImage from '../CardImage.vue'
import type { ActionEvent, Card } from '../types'

type Zone = 'hand' | 'library' | 'field' | 'graveyard' | 'relic' | 'center'
type AnchorRect = { x: number; y: number; width: number; height: number }
type Movement = {
  sequence: number
  playerIndex: number
  label: string
  from: Zone
  to: Zone
  card?: Card
  concealed: boolean
  covered: boolean
  fromRect: AnchorRect
  toRect: AnchorRect
}

const props = withDefaults(defineProps<{
  events: ActionEvent[]
  matchId: string
  viewerPlayerIndex: number
  paused?: boolean
}>(), { paused: false })

const active = ref<Movement | null>(null)
const queue: Movement[] = []
let initialized = false
let lastSequence = 0
let timer: ReturnType<typeof setTimeout> | null = null

function textSource(text: string): Zone {
  if (/从墓地|墓地中|墓地的/.test(text)) return 'graveyard'
  if (/从牌库|牌库中|牌库顶|牌库底/.test(text)) return 'library'
  if (/从手牌|手牌中|弃置.*手牌/.test(text)) return 'hand'
  if (/从圣物区|圣物区的/.test(text)) return 'relic'
  return 'field'
}

function movementFromEvent(event: ActionEvent, fromRect: AnchorRect, toRect: AnchorRect): Movement | null {
  let from: Zone
  let to: Zone
  let label: string
  if (event.type === 'counter-set') {
    from = 'hand'; to = 'field'; label = '盖伏'
  } else if (event.type === 'play') {
    from = 'hand'; to = event.cards?.[0]?.cardType === 'artifact' ? 'relic'
      : event.cards?.[0]?.cardType === 'legion' ? 'field' : 'center'; label = '打出'
  } else if (event.type === 'put' || event.type === 'enter') {
    from = textSource(event.text); to = 'field'; label = '登场'
  } else if (event.type === 'move') {
    from = 'field'; to = 'field'; label = '位移'
  } else if (event.type === 'grave' || event.type === 'discard') {
    from = textSource(event.text); to = 'graveyard'; label = event.type === 'discard' ? '弃置' : '入墓'
  } else if (event.type === 'return') {
    from = event.text.includes('从墓地') ? 'graveyard' : event.text.includes('从圣物区') ? 'relic' : 'field'
    to = event.text.includes('手牌') ? 'hand' : event.text.includes('圣物区') ? 'relic' : 'library'
    label = '返回'
  } else if (event.type === 'search') {
    from = event.text.includes('墓地') ? 'graveyard' : 'library'; to = 'hand'; label = '加入手牌'
  } else return null

  const cards = event.cards ?? []
  const card = event.type === 'move' ? cards.at(-1) : cards[0]
  const concealed = !card || card.identityKnown === false
  return {
    sequence: event.sequence,
    playerIndex: event.playerIndex ?? props.viewerPlayerIndex,
    label,
    from,
    to,
    card,
    concealed,
    covered: card?.hidden === true,
    fromRect,
    toRect,
  }
}

function elementRect(element: Element | null): AnchorRect | null {
  if (!element) return null
  const rect = element.getBoundingClientRect()
  if (rect.width <= 0 || rect.height <= 0) return null
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2, width: rect.width, height: rect.height }
}
function cardElement(instanceId?: string) {
  if (!instanceId) return null
  const root = document.querySelector('[data-l12-game-stage]')
  return root?.querySelector(`[data-card-instance-id="${CSS.escape(instanceId)}"]`) ?? null
}
function zoneElement(zone: Zone, playerIndex: number) {
  if (zone === 'center') return null
  const root = document.querySelector('[data-l12-game-stage]')
  return root?.querySelector(`[data-player-index="${playerIndex}"][data-l12-zone="${zone}"]`)
    ?? root?.querySelector(`[data-player-index="${playerIndex}"] [data-l12-zone="${zone}"]`)
    ?? null
}
function fallbackRect(zone: Zone, playerIndex: number): AnchorRect {
  if (zone === 'center') return { x: innerWidth / 2, y: innerHeight / 2, width: 72, height: 101 }
  const mine = playerIndex === props.viewerPlayerIndex
  const x = zone === 'library' || zone === 'graveyard' ? innerWidth * .9 : zone === 'relic' ? innerWidth * .12 : innerWidth * .5
  const y = zone === 'hand' ? innerHeight * (mine ? .92 : .08) : innerHeight * (mine ? .68 : .32)
  return { x, y, width: 72, height: 101 }
}
function resolveRect(zone: Zone, playerIndex: number, instanceId?: string) {
  return elementRect(cardElement(instanceId)) ?? elementRect(zoneElement(zone, playerIndex)) ?? fallbackRect(zone, playerIndex)
}

const motionStyle = computed(() => {
  if (!active.value) return {}
  const start = active.value.fromRect
  const finish = active.value.toRect
  const distance = Math.hypot(finish.x - start.x, finish.y - start.y)
  const arc = Math.min(110, Math.max(30, distance * .18))
  return {
    '--move-from-x': `${start.x}px`,
    '--move-from-y': `${start.y}px`,
    '--move-mid-x': `${(start.x + finish.x) / 2}px`,
    '--move-mid-y': `${(start.y + finish.y) / 2 - arc}px`,
    '--move-to-x': `${finish.x}px`,
    '--move-to-y': `${finish.y}px`,
    '--move-start-scale': `${Math.max(.72, Math.min(1.2, start.width / 72))}`,
  }
})

let hiddenTarget: HTMLElement | null = null
let hiddenTargetVisibility = ''
function revealTarget() {
  if (!hiddenTarget) return
  hiddenTarget.style.visibility = hiddenTargetVisibility
  hiddenTarget = null
  hiddenTargetVisibility = ''
}

function showNext() {
  if (active.value || props.paused || queue.length === 0) return
  active.value = queue.shift() ?? null
  if (!active.value) return
  const destination = cardElement(active.value.card?.instanceId)
  if (destination instanceof HTMLElement) {
    hiddenTarget = destination
    hiddenTargetVisibility = destination.style.visibility
    destination.style.visibility = 'hidden'
  }
  timer = setTimeout(() => {
    revealTarget()
    active.value = null
    timer = null
    showNext()
  }, 920)
}

function reset() {
  if (timer) clearTimeout(timer)
  timer = null
  revealTarget()
  active.value = null
  queue.length = 0
  initialized = false
  lastSequence = 0
}

watch(() => props.matchId, reset, { flush: 'sync' })
watch(() => props.events.map(event => event.sequence).join(','), async () => {
  const highest = Math.max(0, ...props.events.map(event => event.sequence))
  if (!initialized) {
    initialized = true
    lastSequence = highest
    return
  }
  const fresh = props.events.filter(item => item.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence)
  const starts = fresh.map(event => {
    const draft = movementFromEvent(event, fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex))
    return draft ? resolveRect(draft.from, draft.playerIndex, draft.card?.instanceId) : null
  })
  await nextTick()
  for (const [index, event] of fresh.entries()) {
    const draft = movementFromEvent(event, fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex))
    const movement = draft && starts[index]
      ? movementFromEvent(event, starts[index]!, resolveRect(draft.to, draft.playerIndex, draft.card?.instanceId))
      : null
    const previous = queue.at(-1) ?? active.value
    const repeated = movement && previous && movement.card?.instanceId
      && movement.card.instanceId === previous.card?.instanceId
      && movement.sequence - previous.sequence <= 2
      && movement.to === previous.to
    if (movement && !repeated) queue.push(movement)
    lastSequence = Math.max(lastSequence, event.sequence)
  }
  showNext()
}, { immediate: true })
watch(() => props.paused, paused => {
  if (!paused) showNext()
})
onBeforeUnmount(reset)
</script>

<template>
  <Teleport to="body">
    <div v-if="active" :key="active.sequence" class="zone-card-movement" :style="motionStyle"
      data-ui-contract="authoritative-zone-card-movement" aria-hidden="true">
      <div class="moving-card" :class="{ concealed: active.concealed, covered: active.covered }">
        <img v-if="active.concealed" src="/assets/l12/card-back-official.png" alt="" />
        <CardImage v-else-if="active.card" :card-id="active.card.cardId" :legacy-url="active.card.imageUrl"
          :alt="active.card.name" intent="board" eager />
        <small>{{ active.label }}</small>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.zone-card-movement{position:fixed;z-index:2147482988;left:0;top:0;width:0;height:0;pointer-events:none}.moving-card{position:absolute;width:72px;height:101px;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0);animation:l12-zone-card-flight .9s cubic-bezier(.22,.76,.2,1) both;filter:drop-shadow(0 12px 14px #000);will-change:transform,opacity}.moving-card>img,.moving-card :deep(.l12-card-image){width:100%;height:100%;object-fit:contain}.moving-card.concealed>img{object-fit:cover;border:1px solid #d6c488}.moving-card small{position:absolute;left:50%;bottom:-21px;transform:translateX(-50%);padding:3px 7px;border:1px solid #d6bd70;background:#090c0dec;color:#f5edcf;font-size:9px;font-weight:900;letter-spacing:.12em;white-space:nowrap}.moving-card::after{content:'';position:absolute;inset:-8px;border:1px solid rgba(225,196,104,.65);opacity:0;animation:l12-zone-card-pulse .9s ease-out}
.moving-card.covered:not(.concealed){filter:grayscale(.45) brightness(.72) drop-shadow(0 12px 14px #000)}
@keyframes l12-zone-card-flight{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-start-scale)) rotate(0)}52%{opacity:1;transform:translate3d(calc(var(--move-mid-x) - 36px),calc(var(--move-mid-y) - 50px),0) scale(1.08) rotate(-2deg)}88%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(1) rotate(0)}100%{opacity:0;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(1)}}
@keyframes l12-zone-card-pulse{45%{opacity:0;transform:scale(.9)}72%{opacity:.9}100%{opacity:0;transform:scale(1.24)}}
@media(max-width:700px){.moving-card{width:56px;height:79px}.moving-card small{bottom:-18px;font-size:8px}}
@media(prefers-reduced-motion:reduce){.moving-card{animation-duration:.12s}.moving-card::after{animation:none}}
</style>
