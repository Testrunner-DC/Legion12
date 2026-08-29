<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import CardImage from '../CardImage.vue'
import type { ActionEvent, Card } from '../types'

type Zone = 'hand' | 'library' | 'field' | 'graveyard' | 'relic' | 'center'
type Movement = {
  sequence: number
  playerIndex: number
  label: string
  from: Zone
  to: Zone
  card?: Card
  concealed: boolean
  covered: boolean
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
  if (text.includes('墓地')) return 'graveyard'
  if (text.includes('牌库')) return 'library'
  if (text.includes('手牌')) return 'hand'
  if (text.includes('圣物区')) return 'relic'
  return 'field'
}

function movementFromEvent(event: ActionEvent): Movement | null {
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
    from = textSource(event.text.includes('从墓地') ? '墓地' : '战场')
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
  }
}

function side(zone: Zone, playerIndex: number) {
  if (zone === 'center') return { x: '50vw', y: '50vh' }
  const mine = playerIndex === props.viewerPlayerIndex
  const y: Record<Exclude<Zone, 'center'>, [string, string]> = {
    hand: ['91vh', '9vh'],
    library: ['78vh', '22vh'],
    field: ['68vh', '32vh'],
    graveyard: ['69vh', '31vh'],
    relic: ['72vh', '28vh'],
  }
  const x: Record<Exclude<Zone, 'center'>, string> = {
    hand: '50vw', library: '91vw', field: '50vw', graveyard: '91vw', relic: '11vw',
  }
  return { x: x[zone], y: y[zone][mine ? 0 : 1] }
}

const motionStyle = computed(() => {
  if (!active.value) return {}
  const start = side(active.value.from, active.value.playerIndex)
  const finish = side(active.value.to, active.value.playerIndex)
  if (active.value.from === 'field' && active.value.to === 'field') {
    start.x = active.value.playerIndex === props.viewerPlayerIndex ? '40vw' : '60vw'
    finish.x = active.value.playerIndex === props.viewerPlayerIndex ? '60vw' : '40vw'
  }
  return {
    '--move-from-x': start.x,
    '--move-from-y': start.y,
    '--move-to-x': finish.x,
    '--move-to-y': finish.y,
  }
})

function showNext() {
  if (active.value || props.paused || queue.length === 0) return
  active.value = queue.shift() ?? null
  if (!active.value) return
  timer = setTimeout(() => {
    active.value = null
    timer = null
    showNext()
  }, 920)
}

function reset() {
  if (timer) clearTimeout(timer)
  timer = null
  active.value = null
  queue.length = 0
  initialized = false
  lastSequence = 0
}

watch(() => props.matchId, reset, { flush: 'sync' })
watch(() => props.events.map(event => event.sequence).join(','), () => {
  const highest = Math.max(0, ...props.events.map(event => event.sequence))
  if (!initialized) {
    initialized = true
    lastSequence = highest
    return
  }
  for (const event of props.events.filter(item => item.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence)) {
    const movement = movementFromEvent(event)
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
.zone-card-movement{position:fixed;z-index:2147482988;left:0;top:0;width:0;height:0;pointer-events:none}.moving-card{position:absolute;width:72px;height:101px;transform:translate(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px));animation:l12-zone-card-flight .9s cubic-bezier(.2,.72,.24,1) both;filter:drop-shadow(0 12px 14px #000)}.moving-card>img,.moving-card :deep(.l12-card-image){width:100%;height:100%;object-fit:contain}.moving-card.concealed>img{object-fit:cover;border:1px solid #d6c488}.moving-card small{position:absolute;left:50%;bottom:-21px;transform:translateX(-50%);padding:3px 7px;border:1px solid #d6bd70;background:#090c0dec;color:#f5edcf;font-size:9px;font-weight:900;letter-spacing:.12em;white-space:nowrap}.moving-card::after{content:'';position:absolute;inset:-8px;border:1px solid rgba(225,196,104,.65);opacity:0;animation:l12-zone-card-pulse .9s ease-out}
.moving-card.covered:not(.concealed){filter:grayscale(.45) brightness(.72) drop-shadow(0 12px 14px #000)}
@keyframes l12-zone-card-flight{0%{opacity:0;transform:translate(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px)) scale(.72) rotate(-8deg)}18%{opacity:1}72%{opacity:1;transform:translate(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px)) scale(1.06) rotate(2deg)}100%{opacity:0;transform:translate(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px)) scale(.92)}}
@keyframes l12-zone-card-pulse{45%{opacity:0;transform:scale(.9)}72%{opacity:.9}100%{opacity:0;transform:scale(1.24)}}
@media(max-width:700px){.moving-card{width:56px;height:79px}.moving-card small{bottom:-18px;font-size:8px}}
@media(prefers-reduced-motion:reduce){.moving-card{animation-duration:.12s}.moving-card::after{animation:none}}
</style>
