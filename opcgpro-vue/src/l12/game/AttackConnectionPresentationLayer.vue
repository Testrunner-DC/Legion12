<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import type { ActionEvent, GameState } from '../types'
import { landscapeTeleportTarget, viewportLayoutRect, viewportRect, visibleViewport } from '../mobileViewport'
import {
  attackConnectionGeometry,
  attackConnectionSourceColor,
  claimAttackConnection,
  createAttackConnectionClaimState,
  type AttackConnectionGeometry,
} from './attackConnectionPresentation'

const props = withDefaults(defineProps<{
  pendingDefense?: GameState['pendingDefense']
  events: ActionEvent[]
  matchId: string
  revision: number
  viewerPlayerIndex: number
  synchronizing?: boolean
  paused?: boolean
}>(), { synchronizing: false, paused: false })

type Presentation = AttackConnectionGeometry & {
  key: string
  intro: boolean
  resolving: boolean
  sourceColor: string
  canvasWidth: number
  canvasHeight: number
  occluder: { x: number; y: number; width: number; height: number } | null
}

const presentation = ref<Presentation | null>(null)
const claimState = createAttackConnectionClaimState()
const definitionId = useId().replace(/[^a-zA-Z0-9_-]/g, '')
const gradientId = `l12-attack-connection-gradient-${definitionId}`
const markerId = `l12-attack-connection-arrow-${definitionId}`
const occlusionMaskId = `l12-attack-connection-occlusion-${definitionId}`
let frame = 0
let introTimer: ReturnType<typeof setTimeout> | null = null
let introKey: string | null = null

function setIntro(key: string) {
  if (introTimer) clearTimeout(introTimer)
  introKey = key
  introTimer = setTimeout(() => {
    introTimer = null
    introKey = null
    if (presentation.value?.key === key) presentation.value = { ...presentation.value, intro: false }
  }, 480)
}

function cardElement(instanceId: string) {
  return document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
}

function targetElement(pending: NonNullable<GameState['pendingDefense']>) {
  if (pending.target.type !== 'master' && pending.target.instanceId) return cardElement(pending.target.instanceId)
  const targetPlayerIndex = 1 - pending.attackerPlayer
  return document.querySelector(`[data-l12-game-stage] [data-player-index="${targetPlayerIndex}"] [data-card-instance-id="master-${targetPlayerIndex}"]`)
}

function combatInformationOccluder() {
  const information = Array.from(document.querySelectorAll('.combat-versus'))
    .find(element => element instanceof HTMLElement && element.getClientRects().length > 0)
  if (!(information instanceof HTMLElement)) return null
  const rect = viewportRect(information)
  const padding = 5
  return {
    x: rect.left - padding,
    y: rect.top - padding,
    width: rect.width + padding * 2,
    height: rect.height + padding * 2,
  }
}

function measure(key: string, intro: boolean) {
  if (intro) setIntro(key)
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(() => {
    const pending = props.pendingDefense
    if (!pending) { presentation.value = null; return }
    const source = cardElement(pending.attackerInstanceId)
    const target = targetElement(pending)
    if (!(source instanceof HTMLElement) || !(target instanceof HTMLElement)) {
      presentation.value = null
      return
    }
    const viewport = visibleViewport()
    const geometry = attackConnectionGeometry(viewportLayoutRect(source), viewportLayoutRect(target), viewport.width)
    if (!geometry) { presentation.value = null; return }
    presentation.value = {
      ...geometry,
      key,
      intro: introKey === key,
      resolving: !['AttackerAttackTiming', 'DefenderAttackTiming', 'DefenseChoice'].includes(pending.stage),
      sourceColor: attackConnectionSourceColor(pending.attackerPlayer, props.viewerPlayerIndex),
      canvasWidth: viewport.width,
      canvasHeight: viewport.height,
      occluder: combatInformationOccluder(),
    }
  })
}

function update() {
  const claim = claimAttackConnection(claimState, {
    matchId: props.matchId,
    revision: props.revision,
    synchronizing: props.synchronizing,
    events: props.events,
    pending: props.pendingDefense,
  })
  if (claim.kind === 'none') {
    cancelAnimationFrame(frame)
    if (introTimer) clearTimeout(introTimer)
    introTimer = null
    introKey = null
    presentation.value = null
    return
  }
  const pending = props.pendingDefense!
  const targetKey = pending.target.type === 'master' ? 'master' : pending.target.instanceId
  const key = claim.kind === 'intro'
    ? `${props.matchId}:${claim.sequence}`
    : presentation.value?.key ?? `${props.matchId}:baseline:${pending.attackerInstanceId}:${targetKey}`
  void nextTick().then(() => measure(key, claim.kind === 'intro'))
}

function refreshGeometry() {
  if (!presentation.value) return
  measure(presentation.value.key, false)
}

watch(() => [
  props.matchId,
  props.revision,
  props.synchronizing,
  props.events.map(event => event.sequence).join(','),
  props.pendingDefense?.attackerInstanceId,
  props.pendingDefense?.target.type,
  props.pendingDefense?.target.instanceId,
  props.pendingDefense?.stage,
] as const, update, { flush: 'post', immediate: true })

onMounted(() => {
  window.addEventListener('resize', refreshGeometry)
  window.addEventListener('l12-viewport-change', refreshGeometry)
})
onBeforeUnmount(() => {
  cancelAnimationFrame(frame)
  if (introTimer) clearTimeout(introTimer)
  window.removeEventListener('resize', refreshGeometry)
  window.removeEventListener('l12-viewport-change', refreshGeometry)
})
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <svg v-if="presentation" :key="presentation.key" class="attack-connection-layer"
      :class="{ 'is-intro': presentation.intro, 'is-resolving': presentation.resolving, 'is-paused': paused }"
      data-ui-contract="authoritative-attack-connection" :data-attack-presentation-key="presentation.key"
      aria-hidden="true">
      <defs>
        <linearGradient :id="gradientId" gradientUnits="userSpaceOnUse"
          :x1="presentation.startX" :y1="presentation.startY" :x2="presentation.endX" :y2="presentation.endY">
          <stop offset="0" :stop-color="presentation.sourceColor" />
          <stop offset="1" stop-color="#f2bd68" />
        </linearGradient>
        <marker :id="markerId" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="5" markerHeight="5" orient="auto">
          <path d="M 0 0 L 8 4 L 0 8 z" fill="#f2bd68" />
        </marker>
        <mask :id="occlusionMaskId" maskUnits="userSpaceOnUse" x="0" y="0"
          :width="presentation.canvasWidth" :height="presentation.canvasHeight">
          <rect x="0" y="0" :width="presentation.canvasWidth" :height="presentation.canvasHeight" fill="white" />
          <rect v-if="presentation.occluder" class="attack-connection-occluder"
            :x="presentation.occluder.x" :y="presentation.occluder.y"
            :width="presentation.occluder.width" :height="presentation.occluder.height" fill="black" />
        </mask>
      </defs>
      <path class="attack-connection-glow" :d="presentation.path" pathLength="1"
        :stroke="`url(#${gradientId})`" :mask="`url(#${occlusionMaskId})`" />
      <path class="attack-connection-core" :d="presentation.path" pathLength="1"
        :stroke="`url(#${gradientId})`" :marker-end="`url(#${markerId})`" :mask="`url(#${occlusionMaskId})`" />
      <circle class="attack-connection-source" :cx="presentation.startX" :cy="presentation.startY" r="7"
        :style="{ color: presentation.sourceColor }" />
      <circle class="attack-connection-target" :cx="presentation.endX" :cy="presentation.endY" r="8" />
      <circle v-if="presentation.intro" class="attack-connection-orb" r="4" :mask="`url(#${occlusionMaskId})`">
        <animateMotion :path="presentation.path" dur="260ms" fill="freeze" />
      </circle>
    </svg>
  </Teleport>
</template>

<style scoped>
.attack-connection-layer{position:fixed;z-index:700;inset:0;width:var(--l12-viewport-width,100vw);height:var(--l12-viewport-height,100vh);overflow:visible;pointer-events:none!important;opacity:1;transition:opacity .18s ease}
.attack-connection-core,.attack-connection-glow{fill:none;stroke-linecap:round;stroke-linejoin:round;stroke-dasharray:1;stroke-dashoffset:0;vector-effect:non-scaling-stroke}
.attack-connection-core{stroke-width:4;opacity:.24}
.attack-connection-glow{stroke-width:12;opacity:.07;filter:blur(3px)}
.attack-connection-source{fill:rgba(8,12,13,.82);stroke:currentColor;stroke-width:2;filter:drop-shadow(0 0 5px currentColor);transform-box:fill-box;transform-origin:center}
.attack-connection-target{fill:rgba(8,12,13,.72);stroke:#f2bd68;stroke-width:2;filter:drop-shadow(0 0 5px rgba(242,189,104,.9));transform-box:fill-box;transform-origin:center}
.attack-connection-orb{fill:#fff4d0;filter:drop-shadow(0 0 5px #f2bd68);opacity:0;animation:l12-attack-orb .34s linear forwards}
.attack-connection-layer.is-intro .attack-connection-core{animation:l12-attack-connection-draw .46s cubic-bezier(.22,1,.36,1) both}
.attack-connection-layer.is-intro .attack-connection-glow{animation:l12-attack-connection-glow .46s ease-out both}
.attack-connection-layer.is-intro .attack-connection-source{animation:l12-attack-source-pulse .38s ease-out both}
.attack-connection-layer.is-intro .attack-connection-target{animation:l12-attack-target-pulse .46s ease-out both}
.attack-connection-layer.is-resolving{opacity:0}
.attack-connection-layer.is-paused *{animation-play-state:paused!important}
.attack-connection-layer.is-paused .attack-connection-orb{visibility:hidden}
@keyframes l12-attack-connection-draw{0%{stroke-dashoffset:1;opacity:0}38%{stroke-dashoffset:0;opacity:.95}100%{stroke-dashoffset:0;opacity:.24}}
@keyframes l12-attack-connection-glow{0%{stroke-dashoffset:1;opacity:0}42%{stroke-dashoffset:0;opacity:.34}100%{stroke-dashoffset:0;opacity:.07}}
@keyframes l12-attack-source-pulse{0%{opacity:0;transform:scale(.45)}42%{opacity:1;transform:scale(1.5)}100%{opacity:1;transform:scale(1)}}
@keyframes l12-attack-target-pulse{0%,30%{opacity:0;transform:scale(.55)}62%{opacity:1;transform:scale(1.45)}100%{opacity:1;transform:scale(1)}}
@keyframes l12-attack-orb{0%,8%{opacity:0}20%,72%{opacity:1}100%{opacity:0}}
@media(prefers-reduced-motion:reduce){
  .attack-connection-layer.is-intro :is(.attack-connection-core,.attack-connection-source,.attack-connection-target){animation:none}
  .attack-connection-glow,.attack-connection-orb{display:none}
}
</style>
