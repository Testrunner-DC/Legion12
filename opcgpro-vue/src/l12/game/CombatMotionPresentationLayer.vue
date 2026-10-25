<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, watch } from 'vue'
import { l12AnimationDuration, l12BattleMotionDisabled } from '../audioPreferences'
import { landscapeTeleportElement, viewportLayoutRect, viewportRect } from '../mobileViewport'
import type { ActionEvent, Card } from '../types'
import { snapshotDecodedAuthoritativeCardVisual } from './authoritativeCardVisualSnapshot'
import { battleMotionStrength } from './visualTransitionProjection'
import type { PresentationReservation, PresentationSequenceCoordinator } from './presentationSequenceCoordinator'

const props = withDefaults(defineProps<{
  events: ActionEvent[]; matchId: string; revision: number
  playbackSpeed?: number | null; paused?: boolean; synchronizing?: boolean
  sequenceCoordinator: PresentationSequenceCoordinator
}>(), { playbackSpeed: null, paused: false, synchronizing: false })
const emit = defineEmits<{ busyChange: [busy: boolean]; presentationChange: [busy: boolean] }>()

type CardSnapshot = { ghost: HTMLElement; rect: DOMRect; pixelsReady: boolean }
type CapturedCard = CardSnapshot & { card: Card; eventIndex: number; snapshot: CardSnapshot }
type DefeatJob = { event: ActionEvent; captured: CapturedCard[]; confirmedDefeatIds: Set<string> }
type CombatPresentation = {
  captured?: CapturedCard; visualKey: string; generation: number; reservation: PresentationReservation
  release?: (presented?: boolean) => void; closed: boolean; started: boolean
  animations: Set<Animation>; wrapper?: HTMLElement
  restoreFrame?: () => void
}

let initialized = false
let lastSequence = 0
let lastRevision = 0
let playbackGeneration = 0
const animations = new Set<Animation>()
const overlays = new Set<HTMLElement>()
const fieldSnapshots = new Map<string, CardSnapshot>()
const defeatedInstances = new Set<string>()
const concealedInstances = new Set<string>()
const presentations = new Set<CombatPresentation>()
let preparationCount = 0
let recoilSerial = 0

function presentationDuration(standardMs: number, liveMinimumMs: number, replayMinimumMs = liveMinimumMs) {
  if (!props.playbackSpeed) return l12AnimationDuration(standardMs, liveMinimumMs)
  return Math.max(replayMinimumMs, Math.round(l12AnimationDuration(standardMs, replayMinimumMs) / props.playbackSpeed))
}

function notifyBusy() {
  // Keep the existing replay clock contract; teaching also needs the live
  // preparation interval before an animation has a mounted DOM element.
  emit('busyChange', Boolean(props.playbackSpeed && animations.size))
  emit('presentationChange', Boolean(preparationCount || animations.size))
}

function cardElement(instanceId?: string) {
  if (!instanceId) return null
  return document.querySelector(`[data-l12-game-stage] .formation-slot [data-card-instance-id="${CSS.escape(instanceId)}"]`)
}

function masterElement(playerIndex: number) {
  return document.querySelector(`[data-l12-game-stage] .mini-master[data-card-instance-id="master-${playerIndex}"]`)
}

function cardSnapshotRect(element: HTMLElement) {
  return viewportLayoutRect(element)
}

function zoneElement(zone: string, playerIndex: number) {
  return document.querySelector(`[data-l12-game-stage] [data-player-index="${playerIndex}"] [data-l12-zone="${zone}"]`)
}

function remember(animation: Animation, presentation: CombatPresentation) {
  animations.add(animation)
  presentation.animations.add(animation)
  notifyBusy()
  const cleanup = () => {
    animations.delete(animation)
    presentation.animations.delete(animation)
    notifyBusy()
  }
  animation.addEventListener('finish', cleanup, { once: true })
  animation.addEventListener('cancel', cleanup, { once: true })
}

function snapshotCard(element: HTMLElement): CardSnapshot {
  const ghost = element.cloneNode(true) as HTMLElement
  return { ghost, rect: cardSnapshotRect(element), pixelsReady: snapshotDecodedAuthoritativeCardVisual(element, ghost) }
}

function refreshFieldSnapshots() {
  const elements = document.querySelectorAll('[data-l12-game-stage] .formation-slot [data-card-instance-id]')
  for (const element of elements) {
    if (!(element instanceof HTMLElement)) continue
    const instanceId = element.dataset.cardInstanceId
    if (!instanceId) continue
    if (concealedInstances.has(instanceId)) { fieldSnapshots.delete(instanceId); continue }
    const snapshot = snapshotCard(element)
    if (snapshot.pixelsReady) fieldSnapshots.set(instanceId, snapshot)
    else fieldSnapshots.delete(instanceId)
    defeatedInstances.delete(instanceId)
  }
  for (const presentation of [...presentations]) {
    if (presentation.wrapper && presentation.captured
        && cardElement(presentation.captured.card.instanceId)) finishPresentation(presentation, false)
  }
}

function cardFaceLoaded(event: Event) {
  const image = event.target
  if (!(image instanceof HTMLImageElement)) return
  const element = image.closest('[data-l12-game-stage] .formation-slot [data-card-instance-id]')
  if (!(element instanceof HTMLElement)) return
  const instanceId = element.dataset.cardInstanceId
  const generation = playbackGeneration
  // CardImage may finish after the authority revision's nextTick capture.
  // Refresh only the still-current public DOM, never a retired image/match.
  void nextTick().then(() => {
    if (!instanceId || generation !== playbackGeneration || !element.isConnected
        || cardElement(instanceId) !== element || !element.contains(image)
        || concealedInstances.has(instanceId) || props.synchronizing) return
    const snapshot = snapshotCard(element)
    if (snapshot.pixelsReady) fieldSnapshots.set(instanceId, snapshot)
  })
}

function captureCards(event: ActionEvent) {
  return (event.cards ?? []).flatMap((card, eventIndex) => {
    // A previously public snapshot is not permission to expose a currently
    // concealed instance. Preserve original event indexes after filtering.
    if (card.hidden) {
      concealedInstances.add(card.instanceId)
      fieldSnapshots.delete(card.instanceId)
      for (const presentation of [...presentations])
        if (presentation.captured?.card.instanceId === card.instanceId) finishPresentation(presentation, false)
      return []
    }
    concealedInstances.delete(card.instanceId)
    const live = cardElement(card.instanceId)
    if (live instanceof HTMLElement) {
      const snapshot = snapshotCard(live)
      if (snapshot.pixelsReady) fieldSnapshots.set(card.instanceId, snapshot)
      else fieldSnapshots.delete(card.instanceId)
      return [{ ...snapshot, card, eventIndex, snapshot }]
    }
    const snapshot = fieldSnapshots.get(card.instanceId)
    // The cached canvas owns decoded pixels; cloning a canvas node would erase
    // those pixels. A confirmed defeat consumes this one snapshot, deduplicated
    // before mounting, while a reentered instance receives a new snapshot.
    return snapshot ? [{ ...snapshot, card, eventIndex, snapshot }] : []
  })
}

function animatePowerBadge(element: HTMLElement, presentation: CombatPresentation) {
  const badge = element.querySelector('.card-power, .master-health')
  if (!(badge instanceof HTMLElement)) return
  // These two badges use different anchors: battlefield power is centered
  // with translateX(-50%), while master health is right/bottom anchored with
  // no transform. Brightness must not replace either badge's actual pose.
  const base = getComputedStyle(badge).transform || 'none'
  const animation = badge.animate([
    { transform: base, filter: 'brightness(1)' },
    { transform: base, filter: 'brightness(1.65) saturate(.7)', offset: .38 },
    { transform: base, filter: 'brightness(1)' },
  ], { duration: presentationDuration(280, 80), easing: 'linear' })
  remember(animation, presentation)
}

function animateSurvivor(element: HTMLElement, presentation: CombatPresentation, damage: number) {
  // Animate the authoritative card itself, after its previous visual owner
  // hands off. No duplicate card and no second attack/rest animation.
  const base = getComputedStyle(element).transform || 'none'
  const strength = battleMotionStrength(damage)
  const previousFrameMarker = element.dataset.l12CombatRecoil
  const frameMarker = `${presentation.generation}:${++recoilSerial}`
  element.dataset.l12CombatRecoil = frameMarker
  presentation.restoreFrame = () => {
    if (element.dataset.l12CombatRecoil !== frameMarker) return
    if (previousFrameMarker === undefined) delete element.dataset.l12CombatRecoil
    else element.dataset.l12CombatRecoil = previousFrameMarker
  }
  const pose = (distance: number, elevation: number, pitch: number) =>
    `${base === 'none' ? '' : base + ' '}perspective(900px) translate3d(0,${distance}px,${elevation}px) rotateX(${pitch}deg)`
  const frames = l12BattleMotionDisabled() ? [{ transform: base }, { transform: base }] : [
    { transform: base },
    { transform: pose(3 + 11 * strength, 6 + 28 * strength, -4 - 12 * strength),
      filter: 'brightness(1.42) saturate(.72)', offset: .30 },
    { transform: pose(2 + 7 * strength, 3 + 14 * strength, -2 - 6 * strength),
      filter: 'brightness(.88) saturate(.8)', offset: .62 },
    { transform: base },
  ]
  remember(element.animate(frames, { duration: presentationDuration(280, 80), easing: 'linear' }), presentation)
  animatePowerBadge(element, presentation)
}

function masterDamageValue(event: ActionEvent) {
  if (event.type !== 'defense' || event.playerCombat?.eventKind !== 'defense'
      || event.playerCombat.outcomeCode !== 'unblocked') return null
  const damage = event.playerCombat.masterDamage
  return typeof damage === 'number' && Number.isFinite(damage) && damage > 0 ? damage : null
}

function animateMasterHit(element: HTMLElement, presentation: CombatPresentation, damage: number) {
  const base = getComputedStyle(element).transform || 'none'
  const strength = battleMotionStrength(damage * 2500)
  const direction = element.closest('.side-opponent') ? -1 : 1
  const previousFrameMarker = element.dataset.l12CombatRecoil
  const frameMarker = `${presentation.generation}:${++recoilSerial}`
  element.dataset.l12CombatRecoil = frameMarker
  presentation.restoreFrame = () => {
    if (element.dataset.l12CombatRecoil !== frameMarker) return
    if (previousFrameMarker === undefined) delete element.dataset.l12CombatRecoil
    else element.dataset.l12CombatRecoil = previousFrameMarker
  }
  const pose = (x: number, y: number, elevation: number, tilt: number) =>
    `${base === 'none' ? '' : base + ' '}perspective(900px) translate3d(${x}px,${y}px,${elevation}px) rotateZ(${tilt}deg)`
  const frames = l12BattleMotionDisabled() ? [{ transform: base }, { transform: base }] : [
    { transform: base, filter: 'brightness(1)' },
    { transform: pose(direction * (5 + 10 * strength), 3 + 7 * strength, 4 + 12 * strength,
      direction * (1 + 2 * strength)), filter: 'brightness(1.55) saturate(.62)', offset: .28 },
    { transform: pose(direction * (3 + 6 * strength), 2 + 4 * strength, 2 + 6 * strength,
      direction * (.5 + strength)), filter: 'brightness(.82) saturate(.72)', offset: .62 },
    { transform: base, filter: 'brightness(1)' },
  ]
  remember(element.animate(frames,
    { duration: presentationDuration(280, 80), easing: 'linear' }), presentation)
  animatePowerBadge(element, presentation)
}

function combatDamageValue(event: ActionEvent, index: number) {
  if (event.type !== 'combat') return null
  const text = `${event.text ?? ''} ${event.effectText ?? ''}`
  if (index === 1) {
    const defender = text.match(/(?:造成|承受)\s*(\d+)\s*点战斗伤害/)
    if (defender) return Number(defender[1])
  }
  if (index === 0 && !text.includes('进攻无损')) {
    const counter = text.match(/防守军团以当前兵力\s*(\d+)\s*反击/)
    if (counter) return Number(counter[1])
  }
  return null
}

function defeatLabel(event: ActionEvent, index: number) {
  const damage = combatDamageValue(event, index)
  if (damage !== null) return `-${damage}`
  return /击杀|消灭/.test(`${event.text ?? ''} ${event.effectText ?? ''}`) ? '击杀' : '阵亡'
}

function defeatOwner(event: ActionEvent, card: Card, index: number) {
  if (card.ownerIndex !== undefined) return card.ownerIndex
  // Combat cards are ordered attacker, defender. Public event snapshots do
  // not always carry ownerIndex, so the defender belongs to the other player;
  // a standalone leave event already names its owning player.
  if (event.type === 'combat' && index === 1 && event.playerIndex !== undefined) return 1 - event.playerIndex
  return event.playerIndex ?? 0
}

function prepareDefeat(captured: CapturedCard, event: ActionEvent, index: number, presentation: CombatPresentation) {
  const wrapper = document.createElement('div')
  const ghost = captured.ghost
  wrapper.className = 'l12-combat-defeat-ghost'
  wrapper.dataset.cardInstanceId = captured.card.instanceId
  Object.assign(wrapper.style, {
    position: 'fixed', left: `${captured.rect.left}px`, top: `${captured.rect.top}px`,
    width: `${captured.rect.width}px`, height: `${captured.rect.height}px`, zIndex: '901',
    pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform, opacity, filter',
    visibility: 'hidden', border: '0', outline: 'none', background: 'transparent', boxShadow: 'none',
    transform: `rotate(${captured.card.tapped ? 90 : 0}deg)`,
  })
  ghost.removeAttribute('id')
  ghost.removeAttribute('data-card-instance-id')
  ghost.querySelectorAll('[id],[data-card-instance-id]').forEach(node => {
    node.removeAttribute('id'); node.removeAttribute('data-card-instance-id')
  })
  ghost.classList.remove('tapped', 'selected')
  ghost.dataset.l12CombatAnimationFace = 'true'
  Object.assign(ghost.style, {
    width: '100%', height: '100%', margin: '0', pointerEvents: 'none', visibility: 'visible',
    transform: 'none', transition: 'none', border: '0', outline: 'none', background: 'transparent', boxShadow: 'none', appearance: 'none',
  })
  const power = ghost.querySelector('.card-power')
  if (power instanceof HTMLElement) {
    power.textContent = '0'
    power.classList.remove('boosted')
    power.classList.add('weakened')
  }
  const damage = document.createElement('b')
  damage.className = 'l12-defeat-damage'
  damage.textContent = defeatLabel(event, index)
  Object.assign(damage.style, {
    position: 'absolute', zIndex: '2', left: '50%', top: '-34px', minWidth: '54px',
    padding: '5px 8px', border: '1px solid #f3d7d9', background: '#7e1822', color: '#fff',
    boxShadow: '0 5px 16px #000,0 0 12px rgba(221,53,66,.72)',
    fontFamily: "'Microsoft YaHei','微软雅黑',sans-serif", fontSize: '20px', fontWeight: '900',
    lineHeight: '1', textAlign: 'center', transform: 'translateX(-50%)',
  })
  wrapper.append(ghost, damage)
  landscapeTeleportElement()?.appendChild(wrapper)
  overlays.add(wrapper)
  presentation.wrapper = wrapper
  return { wrapper, damage }
}

function animateDefeat(captured: CapturedCard, event: ActionEvent, index: number,
  presentation: CombatPresentation, wrapper: HTMLElement, damage: HTMLElement) {
  const owner = defeatOwner(event, captured.card, index)
  const graveElement = zoneElement('graveyard', owner)
  const graveRect = graveElement ? viewportRect(graveElement) : null
  const dx = graveRect ? graveRect.left + graveRect.width / 2 - (captured.rect.left + captured.rect.width / 2) : 0
  const dy = graveRect ? graveRect.top + graveRect.height / 2 - (captured.rect.top + captured.rect.height / 2) : 18
  const duration = presentationDuration(920, 260, 160)
  const angle = captured.card.tapped ? 90 : 0
  const start = `translate3d(0,0,0) rotate(${angle}deg) scale(1)`
  const end = `translate3d(${dx}px,${dy}px,0) rotate(${angle}deg) scale(.44)`
  const frames = l12BattleMotionDisabled() ? [
    { transform: start, opacity: 1 }, { transform: end, opacity: 0 },
  ] : [
    { transform: start, opacity: 1, filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72)) grayscale(0) brightness(1)' },
    { transform: `perspective(900px) translate3d(0,-5px,24px) rotateX(-10deg) rotate(${angle}deg) scale(1.04)`,
      opacity: 1, filter: 'drop-shadow(0 15px 13px rgba(0,0,0,.64)) grayscale(0) brightness(1.45)', offset: .14 },
    { transform: `perspective(900px) translate3d(0,2px,10px) rotateX(7deg) rotate(${angle}deg) scale(1)`,
      opacity: 1, filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72)) grayscale(.48) brightness(.88)', offset: .52 },
    { transform: end, opacity: 0, filter: 'grayscale(1) brightness(.5)' },
  ]
  const animation = wrapper.animate(frames, { duration, easing: 'cubic-bezier(.3,.58,.28,1)', fill: 'forwards' })
  const damageAnimation = damage.animate([
    { transform: 'translateX(-50%) translateY(8px)', opacity: 0 },
    { transform: 'translateX(-50%) translateY(0)', opacity: 1, offset: .12 },
    { transform: 'translateX(-50%) translateY(0)', opacity: 1, offset: .48 },
    { transform: 'translateX(-50%) translateY(-7px)', opacity: 0, offset: .68 },
    { transform: 'translateX(-50%) translateY(-7px)', opacity: 0 },
  ], { duration, easing: 'ease-out', fill: 'forwards' })
  remember(animation, presentation)
  remember(damageAnimation, presentation)
}

function isDefeatLeave(event: ActionEvent) {
  if (event.type !== 'leave') return false
  const text = `${event.text ?? ''} ${event.effectText ?? ''}`
  if (/即将阵亡|代替承受|返回手牌|回到手牌|位移|移动|转移|弃置/.test(text)) return false
  return /阵亡(?:（等待触发完成后进入墓地）)?|被.+击杀|击杀|消灭/.test(text)
}

function finishPresentation(presentation: CombatPresentation, presented: boolean) {
  if (presentation.closed) return
  presentation.closed = true
  presentation.animations.forEach(animation => animation.cancel())
  presentation.animations.clear()
  presentation.restoreFrame?.()
  presentation.restoreFrame = undefined
  const wrapper = presentation.wrapper
  if (wrapper) {
    overlays.delete(wrapper)
    wrapper.remove()
    const captured = presentation.captured
    if (captured && fieldSnapshots.get(captured.card.instanceId) === captured.snapshot)
      fieldSnapshots.delete(captured.card.instanceId)
  }
  presentations.delete(presentation)
  preparationCount = Math.max(0, preparationCount - 1)
  if (presentation.release) presentation.release(presented)
  else presentation.reservation.cancel()
  notifyBusy()
}

function presentDefeats(job: DefeatJob) {
  for (const captured of job.captured) {
    const index = captured.eventIndex
    const reservation = props.sequenceCoordinator.reserve(job.event.sequence, 40, { visualKey: captured.card.instanceId })
    reservation.setPaused(props.paused)
    const presentation: CombatPresentation = {
      captured, visualKey: captured.card.instanceId, generation: playbackGeneration,
      reservation, closed: false, started: false, animations: new Set(),
    }
    presentations.add(presentation)
    preparationCount++
    notifyBusy()
    void nextTick(async () => {
      if (presentation.closed || presentation.generation !== playbackGeneration) return
      const survivor = cardElement(captured.card.instanceId)
      const damage = combatDamageValue(job.event, index)
      const isSurvivor = survivor instanceof HTMLElement && damage !== null && damage > 0
      const isDefeat = !survivor && captured.pixelsReady && !defeatedInstances.has(captured.card.instanceId)
        && (job.event.type !== 'combat' || job.confirmedDefeatIds.has(captured.card.instanceId))
      if (!isSurvivor && !isDefeat) { finishPresentation(presentation, false); return }
      if (isDefeat) defeatedInstances.add(captured.card.instanceId)
      const defeat = isDefeat ? prepareDefeat(captured, job.event, index, presentation) : null
      reservation.prepareVisualOwner({
        // Survivors keep the real authoritative DOM as their only visual. A
        // previous holder disposes its lease in this same coordinator handoff.
        show: () => { if (defeat && !presentation.closed && !cardElement(captured.card.instanceId)) defeat.wrapper.style.visibility = 'visible' },
        hide: () => { if (defeat) defeat.wrapper.style.visibility = 'hidden' },
        dispose: () => finishPresentation(presentation, false),
      })
      const release = await reservation.waitUntilGranted()
      if (presentation.closed || presentation.generation !== playbackGeneration) { release(false); return }
      presentation.release = release
      const current = cardElement(captured.card.instanceId)
      const currentHidden = current instanceof HTMLElement
        && (getComputedStyle(current).display === 'none' || getComputedStyle(current).visibility === 'hidden')
      if (props.paused || props.synchronizing || concealedInstances.has(captured.card.instanceId)
          || (isSurvivor ? current !== survivor || currentHidden : Boolean(current))) {
        finishPresentation(presentation, false); return
      }
      presentation.started = true
      try {
        if (isSurvivor && survivor instanceof HTMLElement && damage !== null) animateSurvivor(survivor, presentation, damage)
        else if (defeat) animateDefeat(captured, job.event, index, presentation, defeat.wrapper, defeat.damage)
        await Promise.all([...presentation.animations].map(animation => animation.finished.catch(() => {})))
        finishPresentation(presentation, true)
      } catch {
        finishPresentation(presentation, false)
      }
    }).catch(() => finishPresentation(presentation, false))
  }
}

function presentMasterHit(event: ActionEvent) {
  const playerIndex = event.playerIndex
  const damage = masterDamageValue(event)
  if (damage === null || playerIndex === undefined || !Number.isInteger(playerIndex)) return
  const visualKey = `master-${playerIndex}`
  const reservation = props.sequenceCoordinator.reserve(event.sequence, 40, { visualKey })
  reservation.setPaused(props.paused)
  const presentation: CombatPresentation = {
    visualKey, generation: playbackGeneration, reservation,
    closed: false, started: false, animations: new Set(),
  }
  presentations.add(presentation)
  preparationCount++
  notifyBusy()
  void nextTick(async () => {
    if (presentation.closed || presentation.generation !== playbackGeneration) return
    const target = masterElement(playerIndex)
    if (!(target instanceof HTMLElement)) { finishPresentation(presentation, false); return }
    reservation.prepareVisualOwner({
      // A surviving master is its own only visual owner; the coordinator still
      // serializes it with the attack/rest transaction and later presentations.
      show: () => {}, hide: () => {}, dispose: () => finishPresentation(presentation, false),
    })
    const release = await reservation.waitUntilGranted()
    if (presentation.closed || presentation.generation !== playbackGeneration) { release(false); return }
    presentation.release = release
    const current = masterElement(playerIndex)
    const hidden = current instanceof HTMLElement
      && (getComputedStyle(current).display === 'none' || getComputedStyle(current).visibility === 'hidden')
    if (props.paused || props.synchronizing || current !== target || hidden) {
      finishPresentation(presentation, false); return
    }
    presentation.started = true
    animateMasterHit(target, presentation, damage)
    await Promise.all([...presentation.animations].map(animation => animation.finished.catch(() => {})))
    finishPresentation(presentation, true)
  }).catch(() => finishPresentation(presentation, false))
}

function reset() {
  playbackGeneration++
  for (const presentation of [...presentations]) finishPresentation(presentation, false)
  preparationCount = 0
  animations.forEach(animation => animation.cancel())
  animations.clear()
  overlays.forEach(element => element.remove())
  overlays.clear()
  fieldSnapshots.clear()
  defeatedInstances.clear()
  concealedInstances.clear()
  initialized = false
  lastSequence = 0
  lastRevision = 0
  notifyBusy()
}

watch(() => props.matchId, reset, { flush: 'sync' })
watch(() => [props.revision, props.synchronizing, props.events.map(event => event.sequence).join(','), props.matchId] as const, () => {
  const highest = Math.max(0, ...props.events.map(event => event.sequence))
  if (!initialized || props.synchronizing || props.revision < lastRevision) {
    reset()
    initialized = true
    lastSequence = highest
    lastRevision = props.revision
    const generation = playbackGeneration
    void nextTick().then(() => { if (generation === playbackGeneration) refreshFieldSnapshots() })
    return
  }
  lastRevision = props.revision
  const fresh = props.events.filter(event => event.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence)
  const confirmedDefeatIds = new Set(fresh.filter(isDefeatLeave)
    .flatMap(event => (event.cards ?? []).map(card => card.instanceId)))
  const jobs: DefeatJob[] = []
  for (const event of fresh) {
    // The state layer owns a normal attack as one combined lunge/rest
    // transaction. This layer only presents damage and confirmed defeats.
    if (event.type === 'combat' || isDefeatLeave(event)) jobs.push({ event, captured: captureCards(event), confirmedDefeatIds })
    if (masterDamageValue(event) !== null) presentMasterHit(event)
    lastSequence = Math.max(lastSequence, event.sequence)
  }
  // Deduplicate confirmed combat deaths before reserving: an earlier leave
  // reservation without an owner must not hide the prepared combat snapshot
  // while another card's hit is still playing.
  const combatDefeats = new Set(jobs.filter(job => job.event.type === 'combat')
    .flatMap(job => job.captured.filter(captured => confirmedDefeatIds.has(captured.card.instanceId))
      .map(captured => captured.card.instanceId)))
  jobs
    .sort((left, right) => Number(right.event.type === 'combat') - Number(left.event.type === 'combat')
      || left.event.sequence - right.event.sequence)
    .forEach(job => {
      if (job.event.type !== 'combat') job.captured = job.captured.filter(captured => !combatDefeats.has(captured.card.instanceId))
      presentDefeats(job)
    })
  const generation = playbackGeneration
  void nextTick().then(() => { if (generation === playbackGeneration) refreshFieldSnapshots() })
}, { immediate: true, flush: 'pre' })
watch(() => props.paused, paused => {
  for (const presentation of [...presentations]) {
    presentation.reservation.setPaused(paused)
    if (paused && presentation.started) finishPresentation(presentation, false)
  }
})
function viewportChanged() {
  // Motion overlays live in the transformed logical-canvas host, so browser
  // toolbar/safe-area jitter moves the host and the board together. Cancelling
  // active WAAPI jobs here caused a visible blink and could discard a queued
  // defeat; only refresh snapshots for later authoritative events.
  const generation = playbackGeneration
  void nextTick().then(() => { if (generation === playbackGeneration) refreshFieldSnapshots() })
}
onMounted(() => {
  window.addEventListener('l12-viewport-change', viewportChanged)
  document.addEventListener('load', cardFaceLoaded, true)
  viewportChanged()
})
onBeforeUnmount(() => {
  window.removeEventListener('l12-viewport-change', viewportChanged)
  document.removeEventListener('load', cardFaceLoaded, true)
  reset(); emit('busyChange', false); emit('presentationChange', false)
})
</script>

<template></template>

<style scoped>
:global([data-l12-combat-animation-face="true"]::after){display:none!important}
:global([data-l12-combat-recoil]){border-color:transparent!important;outline:none!important;box-shadow:none!important;background:transparent!important}
:global([data-l12-combat-recoil]::after){display:none!important}
</style>
