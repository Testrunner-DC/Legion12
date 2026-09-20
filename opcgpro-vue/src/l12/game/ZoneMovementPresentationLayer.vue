<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { l12AnimationDuration } from '../audioPreferences'
import { visibleViewport, viewportRect } from '../mobileViewport'
import { CARD_IMAGE_PLACEHOLDER, resolveCardAssetUrls } from '../cardAssets'
import type { ActionEvent, Card } from '../types'

type Zone = 'hand' | 'library' | 'field' | 'graveyard' | 'relic' | 'master' | 'center' | 'disaster'
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
  sourceGhost?: HTMLElement
  preparedImageUrl?: string
  disasterReveal?: boolean
}

const props = withDefaults(defineProps<{
  events: ActionEvent[]
  matchId: string
  viewerPlayerIndex: number
  paused?: boolean
  playbackSpeed?: number | null
}>(), { paused: false, playbackSpeed: null })
const emit = defineEmits<{ busyChange: [busy: boolean] }>()

const active = ref<Movement | null>(null)
const queue: Movement[] = []
let initialized = false
let lastSequence = 0
let timer: ReturnType<typeof setTimeout> | null = null
let preparationCount = 0
const preparedImageUrls = new Map<string, string>()

function notifyBusy() {
  emit('busyChange', Boolean(props.playbackSpeed && (active.value || queue.length || preparationCount)))
}

function replayDuration(standardMs: number, liveMinimumMs: number, replayMinimumMs = liveMinimumMs) {
  if (!props.playbackSpeed) return l12AnimationDuration(standardMs, liveMinimumMs)
  return Math.max(replayMinimumMs, Math.round(l12AnimationDuration(standardMs, replayMinimumMs) / props.playbackSpeed))
}

function waitForImage(url: string) {
  return new Promise<boolean>(resolve => {
    const image = new Image()
    image.decoding = 'async'
    image.onload = () => resolve(true)
    image.onerror = () => resolve(false)
    image.src = url
  })
}

async function prepareMovementImage(card: Card) {
  const key = `${card.cardId}\n${card.imageUrl ?? ''}`
  const cached = preparedImageUrls.get(key)
  if (cached) return cached
  const candidates = (await resolveCardAssetUrls(card.cardId, card.imageUrl, 'board'))
    .filter(url => url !== CARD_IMAGE_PLACEHOLDER)
  for (const url of candidates) {
    if (!await waitForImage(url)) continue
    preparedImageUrls.set(key, url)
    return url
  }
  // The placeholder is a confirmed last resort here, never an unresolved first frame.
  return CARD_IMAGE_PLACEHOLDER
}

function textSource(text: string): Zone {
  if (/从墓地|墓地中|墓地的/.test(text)) return 'graveyard'
  if (/从牌库|牌库中|牌库顶|牌库底/.test(text)) return 'library'
  if (/从手牌|手牌中|弃置.*手牌/.test(text)) return 'hand'
  if (/从圣物区|圣物区的/.test(text)) return 'relic'
  if (/从主宰区|主宰区的/.test(text)) return 'master'
  return 'field'
}

function isMovementIdentityConcealed(event: ActionEvent, card?: Card) {
  // identityKnown is meaningful only for a card that is still covered. Normal
  // authoritative event cards keep the model default false even after their
  // identity has become public (for example, an opponent playing from hand).
  if (!card || event.type === 'counter-set') return true
  if (!card.cardId || card.cardId === 'hidden-card') return true
  return card.hidden === true && card.identityKnown !== true
}

function movementFromEvent(event: ActionEvent, fromRect: AnchorRect, toRect: AnchorRect): Movement | null {
  // Combat deaths already keep the exact battlefield visual until it reaches the
  // owner's graveyard. Do not create a second card when delayed death triggers finish.
  if (event.type === 'grave' && event.text.includes('阵亡触发已完成')) return null
  let from: Zone
  let to: Zone
  let label: string
  if (event.type === 'disaster-reveal') {
    // A disaster without a triggered effect still has a public reveal moment.
    // Keep it in the card-movement queue so it receives the same presentation
    // ordering as every other authoritative card action.
    from = 'disaster'; to = 'disaster'; label = '天灾翻开'
  } else if (event.type === 'counter-set') {
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
    to = event.text.includes('主宰区') ? 'master' : event.text.includes('手牌') ? 'hand' : event.text.includes('圣物区') ? 'relic' : 'library'
    label = '返回'
  } else if (event.type === 'search') {
    from = event.text.includes('墓地') ? 'graveyard' : 'library'; to = 'hand'; label = '加入手牌'
  } else return null

  const cards = event.cards ?? []
  const card = event.type === 'move' ? cards.at(-1) : cards[0]
  const concealed = isMovementIdentityConcealed(event, card)
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
    disasterReveal: event.type === 'disaster-reveal',
  }
}

function elementRect(element: Element | null): AnchorRect | null {
  if (!element) return null
  const rect = viewportRect(element)
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
  if (zone === 'disaster') return root?.querySelector('[data-l12-zone="disaster"]') ?? null
  return root?.querySelector(`[data-player-index="${playerIndex}"][data-l12-zone="${zone}"]`)
    ?? root?.querySelector(`[data-player-index="${playerIndex}"] [data-l12-zone="${zone}"]`)
    ?? null
}
function fallbackRect(zone: Zone, playerIndex: number): AnchorRect {
  const { width: innerWidth, height: innerHeight } = visibleViewport()
  if (zone === 'center') return { x: innerWidth / 2, y: innerHeight / 2, width: 72, height: 101 }
  const mine = playerIndex === props.viewerPlayerIndex
  const x = zone === 'library' || zone === 'graveyard' ? innerWidth * .9 : zone === 'relic' ? innerWidth * .12 : innerWidth * .5
  const y = zone === 'hand' ? innerHeight * (mine ? .92 : .08) : innerHeight * (mine ? .68 : .32)
  return { x, y, width: 72, height: 101 }
}
function resolveRect(zone: Zone, playerIndex: number, instanceId?: string) {
  return elementRect(cardElement(instanceId)) ?? elementRect(zoneElement(zone, playerIndex)) ?? fallbackRect(zone, playerIndex)
}

function movementDuration(movement: Movement) {
  if (!movement.sourceGhost) return replayDuration(440, 180, 100)
  const distance = Math.hypot(movement.toRect.x - movement.fromRect.x, movement.toRect.y - movement.fromRect.y)
  return replayDuration(Math.round(Math.min(500, Math.max(340, 320 + distance * .16))), 180, 100)
}

const motionStyle = computed(() => {
  if (!active.value) return {}
  const start = active.value.fromRect
  const finish = active.value.toRect
  return {
    '--move-from-x': `${start.x}px`,
    '--move-from-y': `${start.y}px`,
    '--move-to-x': `${finish.x}px`,
    '--move-to-y': `${finish.y}px`,
    '--move-from-scale': `${Math.max(.55, Math.min(1.45, start.width / 72))}`,
    '--move-to-scale': `${Math.max(.55, Math.min(1.45, finish.width / 72))}`,
    '--move-duration': `${movementDuration(active.value)}ms`,
  }
})

let hiddenTarget: HTMLElement | null = null
let hiddenTargetVisibility = ''
let activeGhostWrapper: HTMLElement | null = null
let activeGhostAnimation: Animation | null = null
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
  notifyBusy()
  const destination = cardElement(active.value.card?.instanceId)
  if (destination instanceof HTMLElement) {
    hiddenTarget = destination
    hiddenTargetVisibility = destination.style.visibility
    destination.style.visibility = 'hidden'
  }
  const finish = () => {
    if (!active.value) return
    if (timer) clearTimeout(timer)
    activeGhostAnimation?.cancel()
    activeGhostAnimation = null
    activeGhostWrapper?.remove()
    activeGhostWrapper = null
    revealTarget()
    active.value = null
    timer = null
    showNext()
    notifyBusy()
  }
  if (active.value.sourceGhost) {
    const source = active.value.fromRect
    const target = active.value.toRect
    const wrapper = document.createElement('div')
    wrapper.className = 'l12-zone-flight-ghost'
    Object.assign(wrapper.style, {
      position: 'fixed', left: `${source.x - source.width / 2}px`, top: `${source.y - source.height / 2}px`,
      width: `${source.width}px`, height: `${source.height}px`, zIndex: '902', pointerEvents: 'none',
      transformOrigin: 'left top', willChange: 'transform, opacity', filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72))',
    })
    const ghost = active.value.sourceGhost
    Object.assign(ghost.style, { width: '100%', height: '100%', margin: '0', pointerEvents: 'none' })
    ghost.removeAttribute('id')
    ghost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'))
    wrapper.appendChild(ghost)
    document.body.appendChild(wrapper)
    activeGhostWrapper = wrapper
    const dx = target.x - source.x
    const dy = target.y - source.y
    const scaleX = Math.max(.45, Math.min(1.8, target.width / Math.max(1, source.width)))
    const scaleY = Math.max(.45, Math.min(1.8, target.height / Math.max(1, source.height)))
    const duration = movementDuration(active.value)
    activeGhostAnimation = wrapper.animate([
      { transform: 'translate3d(0,0,0) scale(1)', opacity: 1 },
      { transform: `translate3d(${dx}px,${dy}px,0) scale(${scaleX},${scaleY})`, opacity: 1 },
    ], { duration, easing: 'cubic-bezier(.24,.72,.28,1)', fill: 'forwards' })
    activeGhostAnimation.onfinish = finish
    activeGhostAnimation.oncancel = () => {
      activeGhostWrapper?.remove()
      activeGhostWrapper = null
    }
    timer = setTimeout(finish, duration + replayDuration(80, 20))
    return
  }
  const duration = movementDuration(active.value)
  timer = setTimeout(finish, duration + replayDuration(20, 10))
}

function cancelActiveMovement() {
  if (timer) clearTimeout(timer)
  timer = null
  activeGhostAnimation?.cancel()
  activeGhostAnimation = null
  activeGhostWrapper?.remove()
  activeGhostWrapper = null
  revealTarget()
  active.value = null
  notifyBusy()
}

let viewportGeneration = 0
function viewportChanged() {
  viewportGeneration++
  cancelActiveMovement()
  queue.length = 0
  notifyBusy()
  lastSequence = Math.max(lastSequence, ...props.events.map(event => event.sequence))
}
function reset() {
  viewportGeneration++
  cancelActiveMovement()
  queue.length = 0
  initialized = false
  lastSequence = 0
  notifyBusy()
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
  const hasMovement = fresh.some(event => movementFromEvent(event, fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex)))
  if (hasMovement) {
    preparationCount++
    notifyBusy()
  }
  const generation = viewportGeneration
  const starts = fresh.map(event => {
    const draft = movementFromEvent(event, fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex))
    if (!draft) return null
    const source = cardElement(draft.card?.instanceId)
    return {
      rect: elementRect(source) ?? resolveRect(draft.from, draft.playerIndex, draft.card?.instanceId),
      ghost: source instanceof HTMLElement ? source.cloneNode(true) as HTMLElement : undefined,
    }
  })
  await nextTick()
  if (generation !== viewportGeneration) {
    if (hasMovement) preparationCount--
    notifyBusy()
    return
  }
  for (const [index, event] of fresh.entries()) {
    const draft = movementFromEvent(event, fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex))
    const movement = draft && starts[index]
      ? movementFromEvent(event, starts[index]!.rect, resolveRect(draft.to, draft.playerIndex, draft.card?.instanceId))
      : null
    if (movement) movement.sourceGhost = starts[index]?.ghost
    if (movement && !movement.sourceGhost && !movement.concealed && movement.card) {
      movement.preparedImageUrl = await prepareMovementImage(movement.card)
    }
    if (generation !== viewportGeneration) {
      if (hasMovement) preparationCount--
      notifyBusy()
      return
    }
    const previous = queue.at(-1) ?? active.value
    const repeated = movement && previous && movement.card?.instanceId
      && movement.card.instanceId === previous.card?.instanceId
      && movement.sequence - previous.sequence <= 2
      && movement.to === previous.to
    if (movement && !repeated) queue.push(movement)
    lastSequence = Math.max(lastSequence, event.sequence)
  }
  if (hasMovement) preparationCount--
  notifyBusy()
  showNext()
}, { immediate: true })
watch(() => props.paused, paused => {
  // An animation that has already become visible must yield to a newly opened
  // modal. It is presentation only, so do not replay it after the modal closes.
  if (paused && active.value) cancelActiveMovement()
  if (!paused) showNext()
})
onMounted(() => window.addEventListener('l12-viewport-change', viewportChanged))
onBeforeUnmount(() => { window.removeEventListener('l12-viewport-change', viewportChanged); reset(); emit('busyChange', false) })
</script>

<template>
  <Teleport to="body">
    <div v-if="active && !active.sourceGhost" :key="active.sequence" class="zone-card-movement" :style="motionStyle"
      data-ui-contract="authoritative-zone-card-movement" aria-hidden="true">
      <div class="moving-card" data-essential-motion :class="{ concealed: active.concealed, covered: active.covered, 'disaster-reveal': active.disasterReveal }">
        <template v-if="active.disasterReveal && active.preparedImageUrl">
          <span class="disaster-reveal-card">
            <img class="disaster-reveal-back" src="/assets/l12/card-back-disaster.png" alt="" />
            <img class="disaster-reveal-front" :src="active.preparedImageUrl" :alt="active.card?.name || ''" />
          </span>
        </template>
        <img v-else-if="active.concealed" src="/assets/l12/card-back-official.png" alt="" />
        <img v-else-if="active.preparedImageUrl" :src="active.preparedImageUrl" :alt="active.card?.name || ''" />
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.zone-card-movement{position:fixed;z-index:2147482988;left:0;top:0;width:0;height:0;pointer-events:none}.moving-card{position:absolute;width:72px;height:101px;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0);animation:l12-zone-card-flight var(--move-duration,.44s) cubic-bezier(.24,.72,.28,1) both;filter:drop-shadow(0 8px 10px rgba(0,0,0,.72));will-change:transform,opacity}.moving-card>img,.moving-card :deep(.l12-card-image){width:100%;height:100%;object-fit:contain}.moving-card.concealed>img{object-fit:cover;border:1px solid #d6c488}
.moving-card.covered:not(.concealed){filter:grayscale(.45) brightness(.72) drop-shadow(0 12px 14px #000)}
.disaster-reveal-card{position:relative;display:block;width:100%;height:100%;perspective:800px;transform-style:preserve-3d}.disaster-reveal-card>img{position:absolute;inset:0;width:100%;height:100%;backface-visibility:hidden}.disaster-reveal-back{object-fit:cover;animation:l12-disaster-card-back var(--move-duration,.44s) ease-in both}.disaster-reveal-front{animation:l12-disaster-card-front var(--move-duration,.44s) ease-out both}
@keyframes l12-zone-card-flight{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-from-scale))}100%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-to-scale))}}
@keyframes l12-disaster-card-back{0%,42%{opacity:1;transform:rotateY(0)}58%,100%{opacity:0;transform:rotateY(90deg)}}
@keyframes l12-disaster-card-front{0%,42%{opacity:0;transform:rotateY(-90deg)}58%,100%{opacity:1;transform:rotateY(0)}}
@media(max-width:700px){.moving-card{width:56px;height:79px}.moving-card small{bottom:-18px;font-size:var(--l12-board-copy,13px)}}
.zone-card-movement{z-index:902}
</style>
