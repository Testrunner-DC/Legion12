<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ActionEvent } from '../types'
import {
  actionPresentationDurations,
  actionPresentationFromEvent,
  type ActionPresentation,
} from './actionPresentation'
import { playL12ActionSound, primeL12ActionAudio } from './useL12ActionAudio'

const props = withDefaults(defineProps<{
  events: ActionEvent[]
  matchId: string
  playerNames?: string[]
  paused?: boolean
}>(), { playerNames: () => [], paused: false })

const active = ref<ActionPresentation | null>(null)
const queue: ActionPresentation[] = []
let lastSequence = 0
let initialized = false
let timer: ReturnType<typeof setTimeout> | null = null

function resetPresentation() {
  if (timer) clearTimeout(timer)
  timer = null
  active.value = null
  queue.length = 0
  lastSequence = 0
  initialized = false
}

const sideLabel = computed(() => {
  if (active.value?.playerIndex === undefined || active.value.playerIndex === null) return ''
  if (props.playerNames[active.value.playerIndex]) return props.playerNames[active.value.playerIndex]
  if (active.value.playerIndex === 0) return '玩家 A'
  if (active.value.playerIndex === 1) return '玩家 B'
  return ''
})
const actionSymbol = computed(() => active.value?.kind === 'turn-start' ? '启' : active.value?.kind === 'turn-end' ? '终' : '主')

function showNext() {
  if (active.value || props.paused || !queue.length) return
  active.value = queue.shift() ?? null
  if (!active.value) return
  playL12ActionSound(active.value.kind)
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => {
    active.value = null
    timer = null
    showNext()
  }, actionPresentationDurations[active.value.kind])
}

watch(() => props.matchId, resetPresentation, { flush: 'sync' })

watch(() => props.events.map(event => event.sequence).join(','), () => {
  const highest = Math.max(0, ...props.events.map(event => event.sequence))
  if (!initialized) {
    lastSequence = highest
    initialized = true
    return
  }
  for (const event of props.events.filter(item => item.sequence > lastSequence).sort((left, right) => left.sequence - right.sequence)) {
    const presentation = actionPresentationFromEvent(event)
    const previous = queue.at(-1) ?? active.value
    const repeatedPhase = presentation && previous && presentation.kind === previous.kind
      && presentation.text === previous.text
    if (presentation && !repeatedPhase) queue.push(presentation)
    lastSequence = Math.max(lastSequence, event.sequence)
  }
  showNext()
}, { immediate: true })

watch(() => props.paused, paused => {
  if (paused && active.value) {
    if (timer) clearTimeout(timer)
    timer = null
    active.value = null
  }
  if (!paused) showNext()
})

onMounted(() => {
  window.addEventListener('pointerdown', primeL12ActionAudio, { once: true })
  window.addEventListener('keydown', primeL12ActionAudio, { once: true })
})

onBeforeUnmount(() => {
  window.removeEventListener('pointerdown', primeL12ActionAudio)
  window.removeEventListener('keydown', primeL12ActionAudio)
  resetPresentation()
})
</script>

<template>
  <Teleport to="body">
    <Transition name="l12-action-presentation" mode="out-in">
      <div v-if="active" :key="active.sequence" class="l12-action-presentation" :class="`kind-${active.kind}`"
        data-ui-contract="authoritative-action-presentation" aria-live="polite">
        <i class="action-flare" aria-hidden="true" />
        <div class="action-symbol" aria-hidden="true"><span>{{ actionSymbol }}</span></div>
        <div class="action-copy">
          <small>{{ sideLabel }}<template v-if="sideLabel"> · </template>{{ active.label }}</small>
          <strong>{{ active.text }}</strong>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.l12-action-presentation{--action-color:#d5bc70;position:fixed;z-index:2147482990;left:50%;top:15%;display:flex;max-width:min(620px,calc(100vw - 28px));align-items:center;gap:10px;padding:8px 13px;border:1px solid color-mix(in srgb,var(--action-color) 72%,#fff 10%);background:linear-gradient(100deg,rgba(5,8,10,.97),rgba(17,20,22,.92));box-shadow:0 12px 34px rgba(0,0,0,.78),0 0 20px color-mix(in srgb,var(--action-color) 30%,transparent);transform:translateX(-50%);color:#fff;pointer-events:none;isolation:isolate;overflow:hidden}.action-flare{position:absolute;z-index:-1;inset:-80% 55% -80% -20%;background:linear-gradient(90deg,transparent,color-mix(in srgb,var(--action-color) 26%,transparent),transparent);transform:skewX(-18deg);animation:l12-action-flare .72s ease-out}.action-symbol{display:grid;width:43px;height:43px;flex:0 0 43px;place-items:center;border:1px solid color-mix(in srgb,var(--action-color) 76%,#fff 10%);border-radius:50%;background:radial-gradient(circle,color-mix(in srgb,var(--action-color) 30%,#121719),#06090b 70%);box-shadow:inset 0 0 12px color-mix(in srgb,var(--action-color) 24%,transparent);color:color-mix(in srgb,var(--action-color) 85%,#fff 25%);font-size:19px;font-weight:900}.action-copy{display:grid;min-width:0;gap:2px}.action-copy small{color:var(--action-color);font:900 8px/1.2 monospace;letter-spacing:.14em}.action-copy strong{display:block;overflow:hidden;color:#f4f2eb;font-size:11px;line-height:1.4;text-overflow:ellipsis;white-space:nowrap}.l12-action-presentation-enter-active{animation:l12-action-in .2s ease-out}.l12-action-presentation-leave-active{transition:opacity .16s ease,transform .16s ease,filter .16s ease}.l12-action-presentation-leave-to{opacity:0;filter:blur(3px);transform:translate(-50%,-8px)}
@keyframes l12-action-in{from{opacity:0;filter:blur(4px);transform:translate(-50%,-14px) scale(.94)}to{opacity:1;filter:none;transform:translateX(-50%) scale(1)}}
@keyframes l12-action-flare{from{transform:translateX(-40%) skewX(-18deg)}to{transform:translateX(260%) skewX(-18deg)}}
@media(max-width:700px){.l12-action-presentation{top:10%;max-width:calc(100vw - 16px);padding:6px 9px}.action-copy strong{max-width:72vw;font-size:10px}.action-symbol{width:37px;height:37px;flex-basis:37px;font-size:16px}}
@media(prefers-reduced-motion:reduce){.l12-action-presentation-enter-active,.action-flare{animation:none}.l12-action-presentation-leave-active{transition:opacity .01s linear}}
.l12-action-presentation{z-index:900}
</style>
