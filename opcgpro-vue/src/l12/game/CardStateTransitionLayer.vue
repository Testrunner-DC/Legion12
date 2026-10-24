<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { l12AnimationDuration, l12BattleMotionDisabled } from '../audioPreferences'
import { landscapeTeleportElement, settleElementGeometry, viewportLayoutRect } from '../mobileViewport'
import type { ActionEvent, PlayerView } from '../types'
import { acquireAuthoritativeCardVisibility } from './authoritativeCardVisibility'
import { snapshotDecodedAuthoritativeCardVisual } from './authoritativeCardVisualSnapshot'
import { claimCardStateTransitions, collectVisualFieldState, createCardStateClaimState, resetCardStateClaimState } from './visualTransitionProjection'
import type { PreparedVisualOwner, PresentationReservation, PresentationSequenceCoordinator } from './presentationSequenceCoordinator'

type Transition = {
  key: string
  instanceId: string
  fromTapped: boolean
  toTapped: boolean
  sourceRect: { left: number; top: number; width: number; height: number }
  sourceGhost: HTMLElement
  attackSequence?: number
  attackTargetInstanceId?: string
  attackTargetPlayerIndex?: number
  presentationFactSequence?: number
  presentationReservation: PresentationReservation
  presentationRelease?: (presented?: boolean) => void
  holderWrapper: HTMLElement
  visualOwner: PreparedVisualOwner
  coveredTarget?: HTMLElement
  releaseTargetVisibility?: () => void
  presentationStarted?: boolean
  visualPreparation: AbortController
  visualReady: Promise<boolean>
}

const props = withDefaults(defineProps<{
  players: PlayerView[]
  events: ActionEvent[]
  matchId: string
  revision: number
  synchronizing?: boolean
  paused?: boolean
  playbackSpeed?: number | null
  sequenceCoordinator: PresentationSequenceCoordinator
}>(), { synchronizing: false, paused: false, playbackSpeed: null })

const active = ref<Transition | null>(null)
const queue: Transition[] = []
const stateClaims = createCardStateClaimState()
let awaitingMatchBaseline = true

function revealTransitionTarget(transition: Transition) {
  transition.releaseTargetVisibility?.()
  transition.releaseTargetVisibility = undefined
  transition.coveredTarget = undefined
}

function coverTransitionTarget(transition: Transition, target: HTMLElement) {
  if (transition.coveredTarget === target && transition.releaseTargetVisibility) return
  const releaseNext = acquireAuthoritativeCardVisibility(target)
  const releasePrevious = transition.releaseTargetVisibility
  transition.coveredTarget = target
  transition.releaseTargetVisibility = releaseNext
  releasePrevious?.()
}
let wrapper: HTMLElement | null = null
let animation: Animation | null = null
let finalizing = false
let activeGeneration = 0
let starting: Transition | null = null
let activePreparation: AbortController | null = null

function cardElement(instanceId: string) {
  return document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
}

function attackTargetElement(transition: Transition) {
  if (transition.attackTargetInstanceId) return cardElement(transition.attackTargetInstanceId)
  if (transition.attackTargetPlayerIndex === undefined) return null
  return document.querySelector(`[data-l12-game-stage] [data-player-index="${transition.attackTargetPlayerIndex}"] [data-l12-zone="master"]`)
}

function duration() {
  if (l12BattleMotionDisabled()) return 80
  const standard = props.playbackSpeed ? Math.round(380 / props.playbackSpeed) : 380
  return l12AnimationDuration(standard, props.playbackSpeed ? 80 : 140)
}

function stateFaceIsDecoded(root: ParentNode) {
  const images = [...root.querySelectorAll('img')]
  if (images.length) return images.every(image => image.complete && image.naturalWidth > 0)
  return Boolean(root.querySelector('canvas[data-l12-pixel-snapshot="true"]'))
    && !root.querySelector('.l12-card-image__resolving')
}

function waitForDecodedStateImage(image: HTMLImageElement, signal: AbortSignal, timeoutMs = 1800) {
  return new Promise<boolean>(resolve => {
    let settled = false
    const finish = (ready: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timeout)
      signal.removeEventListener('abort', aborted)
      image.removeEventListener('load', inspect)
      image.removeEventListener('error', failed)
      resolve(ready)
    }
    const aborted = () => finish(false)
    const failed = () => finish(false)
    const inspect = () => {
      if (!image.complete) return
      if (image.naturalWidth <= 0) { finish(false); return }
      if (typeof image.decode !== 'function') { finish(true); return }
      void image.decode().then(() => finish(image.complete && image.naturalWidth > 0), failed)
    }
    const timeout = setTimeout(failed, timeoutMs)
    signal.addEventListener('abort', aborted, { once:true })
    image.addEventListener('load', inspect, { once:true })
    image.addEventListener('error', failed, { once:true })
    if (signal.aborted) aborted()
    else inspect()
  })
}

async function waitForDecodedStateFace(root: ParentNode, signal: AbortSignal) {
  const images = [...root.querySelectorAll('img')]
  return images.length > 0
    && (await Promise.all(images.map(image => waitForDecodedStateImage(image, signal)))).every(Boolean)
}

function waitUntilUnpaused(signal: AbortSignal) {
  if (!props.paused) return Promise.resolve(true)
  return new Promise<boolean>(resolve => {
    let settled = false
    const finish = (ready: boolean) => {
      if (settled) return
      settled = true
      stop()
      signal.removeEventListener('abort', aborted)
      resolve(ready)
    }
    const aborted = () => finish(false)
    const stop = watch(() => props.paused, paused => { if (!paused) finish(true) })
    signal.addEventListener('abort', aborted, { once:true })
    if (signal.aborted) aborted()
  })
}

function finalizeActive(generation: number, advance: boolean) {
  if (generation !== activeGeneration || finalizing) return
  finalizing = true
  // Invalidate every promise/event callback owned by this animation before
  // cancelling it. A late completion can therefore never finalize or advance
  // a newer queued transition.
  activeGeneration += 1
  const preparation = activePreparation
  activePreparation = null
  preparation?.abort()
  const currentAnimation = animation
  animation = null
  if (currentAnimation) {
    currentAnimation.onfinish = null
    currentAnimation.oncancel = null
    try { currentAnimation.commitStyles() } catch { /* A cancelled animation has no frame to retain. */ }
    currentAnimation.cancel()
  }
  const transition = active.value
  transition?.visualPreparation.abort()
  const target = transition ? cardElement(transition.instanceId) : null
  if (target instanceof HTMLElement) {
    // Finish the authority CSS state while its holder still owns the pixels.
    // Releasing the lease afterwards cannot trigger a second visible turn.
    settleElementGeometry(target)
  }
  // Hand the final frame to the authoritative DOM before removing the ghost.
  // Both operations are synchronous, but this ordering prevents a blank frame
  // at the compositor boundary on slower/mobile renderers.
  wrapper = null
  transition?.presentationRelease?.(Boolean(transition.presentationStarted))
  active.value = null
  finalizing = false
  if (advance) showNext()
}

function cancelActive() {
  if (!active.value && !animation && !wrapper) return
  finalizeActive(activeGeneration, false)
}

function createPreparedTransition(transition: Omit<Transition,
  'holderWrapper' | 'visualOwner' | 'visualPreparation' | 'visualReady'>,
  sourceGhost: HTMLElement, sourcePixelsReady: boolean): Transition | null {
  const host = landscapeTeleportElement()
  if (!host) return null
  sourceGhost.removeAttribute('id')
  sourceGhost.removeAttribute('data-card-instance-id')
  sourceGhost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'))
  sourceGhost.querySelectorAll('[data-card-instance-id]').forEach(node => node.removeAttribute('data-card-instance-id'))
  sourceGhost.classList.remove('tapped', 'selected')
  Object.assign(sourceGhost.style, {
    width: '100%', height: '100%', margin: '0', transform: 'none', transition: 'none',
    visibility: 'visible', pointerEvents: 'none',
  })
  const holderWrapper = document.createElement('div')
  holderWrapper.className = 'l12-card-state-transition-ghost'
  holderWrapper.dataset.visualTransitionKey = transition.key
  holderWrapper.dataset.stateFrom = transition.fromTapped ? 'rested' : 'active'
  holderWrapper.dataset.stateTo = transition.toTapped ? 'rested' : 'active'
  holderWrapper.dataset.motionKind = transition.attackSequence === undefined ? 'state' : 'attack-rest'
  if (transition.attackSequence !== undefined) holderWrapper.dataset.attackSequence = String(transition.attackSequence)
  Object.assign(holderWrapper.style, {
    position: 'fixed', left: `${transition.sourceRect.left}px`, top: `${transition.sourceRect.top}px`,
    width: `${transition.sourceRect.width}px`, height: `${transition.sourceRect.height}px`,
    zIndex: '902', pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform',
    filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72))', visibility: 'hidden',
    transform: `rotate(${transition.fromTapped ? 90 : 0}deg)`,
  })
  holderWrapper.appendChild(sourceGhost)
  host.appendChild(holderWrapper)
  const prepared = transition as Transition
  prepared.holderWrapper = holderWrapper
  const visualOwner: PreparedVisualOwner = {
    show: () => {
      const target = cardElement(prepared.instanceId)
      if (target instanceof HTMLElement) coverTransitionTarget(prepared, target)
      holderWrapper.style.visibility = 'visible'
    },
    hide: () => {
      holderWrapper.style.visibility = 'hidden'
      revealTransitionTarget(prepared)
    },
    dispose: () => {
      // Reveal the already-settled authority before removing its previous-state
      // holder. Both mutations occur before the browser can paint a gap.
      revealTransitionTarget(prepared)
      holderWrapper.remove()
    },
  }
  prepared.visualOwner = visualOwner
  const visualPreparation = new AbortController()
  prepared.visualPreparation = visualPreparation
  if (sourcePixelsReady) {
    prepared.presentationReservation.prepareVisualOwner(visualOwner)
    prepared.visualReady = Promise.resolve(true)
    return prepared
  }
  let paintBoundaryPassed = false
  requestAnimationFrame(() => { paintBoundaryPassed = true })
  // A detached clone reports its already-decoded source image as incomplete.
  // Attach it hidden, then wait for the clone itself to decode. A predecessor
  // owner (for example hand→field) remains visible while this happens. With no
  // predecessor, never replay after a painted final authority state: expose the
  // settled authority conservatively instead of turning it back to old pixels.
  prepared.visualReady = waitForDecodedStateFace(holderWrapper, visualPreparation.signal).then(decoded => {
    if (!decoded || visualPreparation.signal.aborted) {
      holderWrapper.remove()
      return false
    }
    const target = cardElement(prepared.instanceId)
    const targetVisible = target instanceof HTMLElement
      && getComputedStyle(target).display !== 'none'
      && getComputedStyle(target).visibility !== 'hidden'
      && target.getBoundingClientRect().width > 0 && target.getBoundingClientRect().height > 0
    const targetAlreadyFinal = target instanceof HTMLElement
      && target.classList.contains('tapped') === prepared.toTapped
    if (paintBoundaryPassed && targetVisible && targetAlreadyFinal && !props.paused) {
      holderWrapper.remove()
      return false
    }
    prepared.presentationReservation.prepareVisualOwner(visualOwner)
    return true
  }).catch(() => {
    holderWrapper.remove()
    return false
  })
  return prepared
}

function beginTransition(transition: Transition) {
  const generation = ++activeGeneration
  active.value = transition
  const preparation = new AbortController()
  activePreparation?.abort()
  activePreparation = preparation
  void nextTick(async () => {
    if (generation !== activeGeneration || active.value?.key !== transition.key) return
    const finalize = (advance: boolean) => finalizeActive(generation, advance)
    try {
      if (!await transition.visualReady) {
        if (generation === activeGeneration && active.value?.key === transition.key) finalize(true)
        return
      }
      // visualReady is the single decode gate. Re-running image.decode() after
      // the holder has taken ownership can stall a fully decoded clone for
      // seconds, leaving a static old state before the motion begins.
      if (!stateFaceIsDecoded(transition.sourceGhost)) {
        if (generation === activeGeneration && active.value?.key === transition.key) finalize(true)
        return
      }
      if (!await waitUntilUnpaused(preparation.signal)
          || generation !== activeGeneration || active.value?.key !== transition.key) return
      if (activePreparation === preparation) activePreparation = null
      const target = cardElement(transition.instanceId)
      if (!(target instanceof HTMLElement)) { finalize(true); return }
      const sourceRect = transition.sourceRect
      // The ghost owns the visible state turn. Finish the covered authority
      // node's transition and settle animation without waiting on CSS timers.
      settleElementGeometry(target)
      const targetRect = viewportLayoutRect(target)
      const width = targetRect.width || Math.min(sourceRect.width, sourceRect.height * 5 / 7)
      const height = targetRect.height || Math.max(sourceRect.height, sourceRect.width * 7 / 5)
      const startX = sourceRect.left + sourceRect.width / 2 - width / 2
      const startY = sourceRect.top + sourceRect.height / 2 - height / 2
      const endX = targetRect.left + targetRect.width / 2 - width / 2
      const endY = targetRect.top + targetRect.height / 2 - height / 2
      wrapper = transition.holderWrapper
      Object.assign(wrapper.style, {
        position: 'fixed', left: `${startX}px`, top: `${startY}px`, width: `${width}px`, height: `${height}px`,
        zIndex: '902', pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform',
        filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72))',
      })
      const fromAngle = transition.fromTapped ? 90 : 0
      const toAngle = transition.toTapped ? 90 : 0
      const dx = endX - startX
      const dy = endY - startY
      const motionDuration = duration()
      const attackTarget = attackTargetElement(transition)
      const attackRect = attackTarget instanceof HTMLElement ? viewportLayoutRect(attackTarget) : null
      const attackDx = attackRect ? attackRect.left + attackRect.width / 2 - (sourceRect.left + sourceRect.width / 2) : 0
      const attackDy = attackRect ? attackRect.top + attackRect.height / 2 - (sourceRect.top + sourceRect.height / 2) : 0
      const attackDistance = Math.hypot(attackDx, attackDy)
      // Keep the lunge readable even when opposing slots overlap closely in the
      // compact board projection. This is an impact gesture, so a short pass
      // through the target center is preferable to an imperceptible twitch.
      const attackStep = attackDistance > 0
        ? Math.min(30, Math.max(12, attackDistance * .1))
        : 0
      const lungeX = attackDistance > 0 ? attackDx / attackDistance * attackStep : 0
      const lungeY = attackDistance > 0 ? attackDy / attackDistance * attackStep : 0
      const frames = transition.attackSequence === undefined ? [
        { transform: `translate3d(0,0,0) rotate(${fromAngle}deg) scale(1)` },
        { transform: `translate3d(${dx * .72}px,${dy * .72}px,0) rotate(${fromAngle + (toAngle - fromAngle) * .72}deg) scale(.96)`, offset: .72 },
        { transform: `translate3d(${dx}px,${dy}px,0) rotate(${toAngle}deg) scale(1)` },
      ] : [
        { transform: `translate3d(0,0,0) rotate(${fromAngle}deg) scale(1)`, easing: 'cubic-bezier(.22,1,.36,1)' },
        { transform: `translate3d(${lungeX}px,${lungeY}px,0) rotate(${fromAngle + (toAngle - fromAngle) * .5}deg) scale(1.03)`, offset: .38 },
        { transform: `translate3d(${lungeX}px,${lungeY}px,0) rotate(${fromAngle + (toAngle - fromAngle) * .78}deg) scale(1)`, offset: .56, easing: 'cubic-bezier(.22,1,.36,1)' },
        { transform: `translate3d(${dx}px,${dy}px,0) rotate(${toAngle}deg) scale(1)` },
      ]
      // Attack keyframe offsets are transaction phases (lunge, contact, settle),
      // so keep the overall timeline linear and ease within those phases.
      animation = wrapper.animate(frames, {
        duration: motionDuration,
        easing: transition.attackSequence === undefined ? 'cubic-bezier(.22,1,.36,1)' : 'linear',
        fill: 'forwards',
      })
      transition.presentationStarted = true
      const settled = animation.finished
      animation.onfinish = () => finalize(true)
      animation.oncancel = () => finalize(false)
      if (!wrapper.isConnected || !stateFaceIsDecoded(wrapper)) { finalize(true); return }
      if (transition.presentationFactSequence !== undefined)
        props.sequenceCoordinator.registerPresentationFact(transition.presentationFactSequence)
      void settled.then(() => finalize(true), () => finalize(false))
    } catch {
      finalize(true)
    }
  })
}

function showNext() {
  if (active.value || starting || props.paused || !queue.length) return
  const transition = queue.shift()
  if (!transition) return
  starting = transition
  transition.presentationReservation.setPaused(props.paused)
  void transition.presentationReservation.waitUntilGranted().then(release => {
    if (starting !== transition) {
      release(false)
      return
    }
    starting = null
    transition.presentationRelease = release
    beginTransition(transition)
  })
}

function cancelQueuedTransitions() {
  if (starting) {
    starting.visualPreparation.abort()
    starting.holderWrapper.remove()
    starting.presentationReservation.cancel()
  }
  starting = null
  for (const transition of queue) {
    transition.visualPreparation.abort()
    transition.holderWrapper.remove()
    transition.presentationReservation.cancel()
  }
  queue.length = 0
}

watch(() => props.matchId, () => {
  cancelActive(); cancelQueuedTransitions()
  // sync prop watchers can see the new match id before the new revision.
  // Cancel immediately; baseline only after the complete prop tuple arrives.
  awaitingMatchBaseline = true
}, { flush: 'sync', immediate: true })

watch(() => [props.revision, props.synchronizing, collectVisualFieldState(props.players), props.events.map(event => event.sequence).join(','), props.matchId] as const, ([revision, synchronizing, next]) => {
  if (awaitingMatchBaseline) {
    awaitingMatchBaseline = false
    resetCardStateClaimState(stateClaims, revision, next,
      Math.max(0, ...props.events.map(event => event.sequence)))
    return
  }
  // Recovery snapshots and backward replay seeks establish a new visual
  // baseline. Historical state must never be backfilled as live motion.
  if (synchronizing || (props.playbackSpeed && revision < stateClaims.revision)) {
    cancelActive(); cancelQueuedTransitions()
    resetCardStateClaimState(stateClaims, revision, next,
      Math.max(0, ...props.events.map(event => event.sequence)))
    return
  }
  for (const change of claimCardStateTransitions(stateClaims, revision, next, props.events)) {
    const instanceId = change.instanceId
    const transitionKey = `${props.matchId}:${change.transactionKey}`
    if (active.value?.key === transitionKey || starting?.key === transitionKey
      || queue.some(item => item.key === transitionKey)) continue
    const source = cardElement(instanceId)
    if (!(source instanceof HTMLElement)) continue
    const sourceRect = viewportLayoutRect(source)
    if (sourceRect.width <= 0 || sourceRect.height <= 0) continue
    // flush:'pre' still sees the old authority state. Snapshot both pixels and
    // geometry now; waiting until nextTick can make source===target and clone
    // the target's temporary visibility:hidden into an invisible ghost.
    const sourceGhost = source.cloneNode(true) as HTMLElement
    sourceGhost.style.visibility = 'visible'
    const sourcePixelsReady = snapshotDecodedAuthoritativeCardVisual(source, sourceGhost)
    const presentationReservation = props.sequenceCoordinator.reserve(change.orderingSequence, 30,
      { visualKey: instanceId })
    presentationReservation.setPaused(props.paused)
    const transition = createPreparedTransition({
      key: transitionKey,
      instanceId,
      fromTapped: change.fromTapped,
      toTapped: change.toTapped,
      sourceRect: { left: sourceRect.left, top: sourceRect.top, width: sourceRect.width, height: sourceRect.height },
      sourceGhost,
      attackSequence: change.attackSequence,
      attackTargetInstanceId: change.attackTargetInstanceId,
      attackTargetPlayerIndex: change.attackTargetPlayerIndex,
      presentationFactSequence: change.presentationFactSequence,
      presentationReservation,
    }, sourceGhost, sourcePixelsReady)
    if (transition) queue.push(transition)
    else presentationReservation.cancel()
  }
  showNext()
}, { flush: 'pre', immediate: true })

watch(() => props.paused, paused => {
  // 已经开始的展示让位给阻塞弹框，不重排也不重播；尚未开始的仍保留顺序。
  if (paused && active.value && animation) cancelActive()
  starting?.presentationReservation?.setPaused(paused)
  for (const transition of queue) transition.presentationReservation?.setPaused(paused)
  if (!paused) showNext()
})

onBeforeUnmount(() => { cancelActive(); cancelQueuedTransitions() })
</script>

<template><span class="card-state-transition-layer" data-ui-contract="authoritative-card-state-transition" aria-hidden="true" /></template>

<style scoped>
/* This component observes authoritative card state and draws its ghost in the
   shared logical-canvas host. Its local anchor must never become a grid item. */
.card-state-transition-layer{display:none!important}
</style>
