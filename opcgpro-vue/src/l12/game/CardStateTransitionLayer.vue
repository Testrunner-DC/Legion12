<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { l12AnimationDuration } from '../audioPreferences'
import { landscapeTeleportElement, viewportRect } from '../mobileViewport'
import type { PlayerView } from '../types'
import { claimCardStateTransitions, collectVisualFieldState, createCardStateClaimState, resetCardStateClaimState } from './visualTransitionProjection'

type Transition = {
  key: string
  instanceId: string
  fromTapped: boolean
  toTapped: boolean
  sourceRect: { left: number; top: number; width: number; height: number }
  sourceGhost: HTMLElement
}

const props = withDefaults(defineProps<{
  players: PlayerView[]
  matchId: string
  revision: number
  synchronizing?: boolean
  paused?: boolean
  playbackSpeed?: number | null
}>(), { synchronizing: false, paused: false, playbackSpeed: null })

const active = ref<Transition | null>(null)
const queue: Transition[] = []
const stateClaims = createCardStateClaimState()
let wrapper: HTMLElement | null = null
let animation: Animation | null = null
let hiddenTarget: HTMLElement | null = null
let hiddenTargetVisibility = ''

function cardElement(instanceId: string) {
  return document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
}

function duration() {
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return 80
  const standard = props.playbackSpeed ? Math.round(380 / props.playbackSpeed) : 380
  return l12AnimationDuration(standard, props.playbackSpeed ? 80 : 140)
}

function revealTarget() {
  if (hiddenTarget) hiddenTarget.style.visibility = hiddenTargetVisibility
  hiddenTarget = null
  hiddenTargetVisibility = ''
}

function finish() {
  animation?.cancel()
  animation = null
  // Hand the final frame to the authoritative DOM before removing the ghost.
  // Both operations are synchronous, but this ordering prevents a blank frame
  // at the compositor boundary on slower/mobile renderers.
  revealTarget()
  wrapper?.remove()
  wrapper = null
  active.value = null
  showNext()
}

function cancelActive() {
  animation?.cancel()
  animation = null
  revealTarget()
  wrapper?.remove()
  wrapper = null
  active.value = null
}

function showNext() {
  if (active.value || props.paused || !queue.length) return
  const transition = queue.shift()
  if (!transition) return
  active.value = transition
  void nextTick(() => {
    if (active.value?.key !== transition.key) return
    const target = cardElement(transition.instanceId)
    if (!(target instanceof HTMLElement)) { finish(); return }
    const sourceRect = transition.sourceRect
    const targetRect = viewportRect(target)
    const width = target.offsetWidth || Math.min(sourceRect.width, sourceRect.height * 5 / 7)
    const height = target.offsetHeight || Math.max(sourceRect.height, sourceRect.width * 7 / 5)
    const startX = sourceRect.left + sourceRect.width / 2 - width / 2
    const startY = sourceRect.top + sourceRect.height / 2 - height / 2
    const endX = targetRect.left + targetRect.width / 2 - width / 2
    const endY = targetRect.top + targetRect.height / 2 - height / 2
    const ghost = transition.sourceGhost
    ghost.removeAttribute('id')
    ghost.removeAttribute('data-card-instance-id')
    ghost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'))
    ghost.querySelectorAll('[data-card-instance-id]').forEach(node => node.removeAttribute('data-card-instance-id'))
    ghost.classList.remove('tapped', 'selected')
    Object.assign(ghost.style, { width: '100%', height: '100%', margin: '0', transform: 'none', transition: 'none', visibility: 'visible', pointerEvents: 'none' })
    wrapper = document.createElement('div')
    wrapper.className = 'l12-card-state-transition-ghost'
    wrapper.dataset.visualTransitionKey = transition.key
    wrapper.dataset.stateFrom = transition.fromTapped ? 'rested' : 'active'
    wrapper.dataset.stateTo = transition.toTapped ? 'rested' : 'active'
    Object.assign(wrapper.style, {
      position: 'fixed', left: `${startX}px`, top: `${startY}px`, width: `${width}px`, height: `${height}px`,
      zIndex: '902', pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform',
      filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72))',
    })
    wrapper.appendChild(ghost)
    landscapeTeleportElement()?.appendChild(wrapper)
    hiddenTarget = target
    hiddenTargetVisibility = target.style.visibility
    target.style.visibility = 'hidden'
    const fromAngle = transition.fromTapped ? 90 : 0
    const toAngle = transition.toTapped ? 90 : 0
    const dx = endX - startX
    const dy = endY - startY
    const motionDuration = duration()
    animation = wrapper.animate([
      { transform: `translate3d(0,0,0) rotate(${fromAngle}deg) scale(1)` },
      { transform: `translate3d(${dx * .72}px,${dy * .72}px,0) rotate(${fromAngle + (toAngle - fromAngle) * .72}deg) scale(.96)`, offset: .72 },
      { transform: `translate3d(${dx}px,${dy}px,0) rotate(${toAngle}deg) scale(1)` },
    ], { duration: motionDuration, easing: 'cubic-bezier(.22,1,.36,1)', fill: 'forwards' })
    animation.onfinish = finish
  })
}

watch(() => props.matchId, () => {
  cancelActive(); queue.length = 0
  resetCardStateClaimState(stateClaims, props.revision, collectVisualFieldState(props.players))
}, { flush: 'sync', immediate: true })

watch(() => [props.revision, props.synchronizing, collectVisualFieldState(props.players)] as const, ([revision, synchronizing, next]) => {
  // Recovery snapshots and backward replay seeks establish a new visual
  // baseline. Historical state must never be backfilled as live motion.
  if (synchronizing || (props.playbackSpeed && revision < stateClaims.revision)) {
    cancelActive(); queue.length = 0
    resetCardStateClaimState(stateClaims, revision, next)
    return
  }
  for (const change of claimCardStateTransitions(stateClaims, revision, next)) {
    const instanceId = change.instanceId
    if (active.value?.key === change.transactionKey || queue.some(item => item.key === change.transactionKey)) continue
    const source = cardElement(instanceId)
    if (!(source instanceof HTMLElement)) continue
    const sourceRect = viewportRect(source)
    if (sourceRect.width <= 0 || sourceRect.height <= 0) continue
    // flush:'pre' still sees the old authority state. Snapshot both pixels and
    // geometry now; waiting until nextTick can make source===target and clone
    // the target's temporary visibility:hidden into an invisible ghost.
    const sourceGhost = source.cloneNode(true) as HTMLElement
    sourceGhost.style.visibility = 'visible'
    queue.push({
      key: `${props.matchId}:${change.transactionKey}`,
      instanceId,
      fromTapped: change.fromTapped,
      toTapped: change.toTapped,
      sourceRect: { left: sourceRect.left, top: sourceRect.top, width: sourceRect.width, height: sourceRect.height },
      sourceGhost,
    })
  }
  showNext()
}, { flush: 'pre', immediate: true })

watch(() => props.paused, paused => {
  // 已经开始的展示让位给阻塞弹框，不重排也不重播；尚未开始的仍保留顺序。
  if (paused && active.value) cancelActive()
  if (!paused) showNext()
})

onBeforeUnmount(() => { cancelActive(); queue.length = 0 })
</script>

<template><span class="card-state-transition-layer" data-ui-contract="authoritative-card-state-transition" aria-hidden="true" /></template>

<style scoped>
/* This component observes authoritative card state and draws its ghost in
   document.body.  Its local anchor must never become a grid/flex item. */
.card-state-transition-layer{display:none!important}
</style>
