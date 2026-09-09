<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, watch } from 'vue'
import { l12AnimationDuration } from '../audioPreferences'
import { viewportRect } from '../mobileViewport'
import type { ActionEvent, Card } from '../types'

const props = defineProps<{ events: ActionEvent[]; matchId: string }>()

type CardSnapshot = { ghost: HTMLElement; rect: DOMRect }
type CapturedCard = CardSnapshot & { card: Card }
type DefeatJob = { event: ActionEvent; captured: CapturedCard[]; confirmedDefeatIds: Set<string> }

let initialized = false
let lastSequence = 0
let playbackGeneration = 0
const animations = new Set<Animation>()
const overlays = new Set<HTMLElement>()
const fieldSnapshots = new Map<string, CardSnapshot>()
const defeatedInstances = new Set<string>()

function cardElement(instanceId?: string) {
  if (!instanceId) return null
  return document.querySelector(`[data-l12-game-stage] .formation-slot [data-card-instance-id="${CSS.escape(instanceId)}"]`)
}

function zoneElement(zone: string, playerIndex: number) {
  return document.querySelector(`[data-l12-game-stage] [data-player-index="${playerIndex}"] [data-l12-zone="${zone}"]`)
}

function remember(animation: Animation) {
  animations.add(animation)
  const cleanup = () => animations.delete(animation)
  animation.addEventListener('finish', cleanup, { once: true })
  animation.addEventListener('cancel', cleanup, { once: true })
}

function refreshFieldSnapshots() {
  const elements = document.querySelectorAll('[data-l12-game-stage] .formation-slot [data-card-instance-id]')
  for (const element of elements) {
    if (!(element instanceof HTMLElement)) continue
    const instanceId = element.dataset.cardInstanceId
    if (!instanceId) continue
    fieldSnapshots.set(instanceId, { ghost: element.cloneNode(true) as HTMLElement, rect: viewportRect(element) })
    defeatedInstances.delete(instanceId)
  }
}

function captureCards(event: ActionEvent) {
  return (event.cards ?? []).flatMap(card => {
    const live = cardElement(card.instanceId)
    if (live instanceof HTMLElement) {
      const snapshot = { ghost: live.cloneNode(true) as HTMLElement, rect: viewportRect(live) }
      fieldSnapshots.set(card.instanceId, snapshot)
      return [{ card, ghost: snapshot.ghost.cloneNode(true) as HTMLElement, rect: snapshot.rect }]
    }
    const snapshot = fieldSnapshots.get(card.instanceId)
    return snapshot ? [{ card, ghost: snapshot.ghost.cloneNode(true) as HTMLElement, rect: snapshot.rect }] : []
  })
}

function animateAttack(event: ActionEvent) {
  const attacker = cardElement(event.cards?.[0]?.instanceId)?.closest('.formation-slot') as HTMLElement | null
  if (!attacker) return
  const source = viewportRect(attacker)
  const targetCard = cardElement(event.cards?.[1]?.instanceId)
  const targetPlayer = event.playerIndex === undefined ? undefined : 1 - event.playerIndex
  const targetElement = targetCard ?? (targetPlayer === undefined ? null : zoneElement('master', targetPlayer))
  const target = targetElement ? viewportRect(targetElement) : null
  if (!target) return
  const dx = target.left + target.width / 2 - (source.left + source.width / 2)
  const dy = target.top + target.height / 2 - (source.top + source.height / 2)
  const distance = Math.max(1, Math.hypot(dx, dy))
  const step = Math.min(18, distance * .09)
  const animation = attacker.animate([
    { transform: 'translate3d(0,0,0)' },
    { transform: `translate3d(${dx / distance * step}px,${dy / distance * step}px,0)`, offset: .48 },
    { transform: 'translate3d(0,0,0)' },
  ], { duration: l12AnimationDuration(360, 24), easing: 'cubic-bezier(.25,.72,.35,1)' })
  remember(animation)
}

function animatePowerBadge(element: HTMLElement) {
  const badge = element.querySelector('.card-power')
  if (!(badge instanceof HTMLElement)) return
  const animation = badge.animate([
    { transform: 'translateX(-50%) scale(1)', filter: 'brightness(1)' },
    { transform: 'translateX(-50%) scale(1.14)', filter: 'brightness(1.55)', offset: .45 },
    { transform: 'translateX(-50%) scale(1)', filter: 'brightness(1)' },
  ], { duration: l12AnimationDuration(280, 80), easing: 'ease-out' })
  remember(animation)
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

function animateDefeat(captured: CapturedCard, event: ActionEvent, index: number) {
  if (defeatedInstances.has(captured.card.instanceId)) return
  defeatedInstances.add(captured.card.instanceId)
  const wrapper = document.createElement('div')
  const ghost = captured.ghost
  wrapper.className = 'l12-combat-defeat-ghost'
  wrapper.dataset.cardInstanceId = captured.card.instanceId
  Object.assign(wrapper.style, {
    position: 'fixed', left: `${captured.rect.left}px`, top: `${captured.rect.top}px`,
    width: `${captured.rect.width}px`, height: `${captured.rect.height}px`, zIndex: '901',
    pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform, opacity, filter',
  })
  Object.assign(ghost.style, { width: '100%', height: '100%', margin: '0', pointerEvents: 'none' })
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
  document.body.appendChild(wrapper)
  overlays.add(wrapper)

  const owner = captured.card.ownerIndex ?? event.playerIndex ?? 0
  const graveElement = zoneElement('graveyard', owner)
  const graveRect = graveElement ? viewportRect(graveElement) : null
  const dx = graveRect ? graveRect.left + graveRect.width / 2 - (captured.rect.left + captured.rect.width / 2) : 0
  const dy = graveRect ? graveRect.top + graveRect.height / 2 - (captured.rect.top + captured.rect.height / 2) : 18
  const duration = l12AnimationDuration(920, 260)
  const animation = wrapper.animate([
    { transform: 'translate3d(0,0,0) scale(1)', opacity: 1, filter: 'grayscale(0) brightness(1)' },
    { transform: 'translate3d(0,0,0) scale(1.06)', opacity: 1, filter: 'grayscale(0) brightness(1.45)', offset: .14 },
    { transform: 'translate3d(0,0,0) scale(1)', opacity: 1, filter: 'grayscale(.48) brightness(.88)', offset: .52 },
    { transform: `translate3d(${dx}px,${dy}px,0) scale(.44)`, opacity: 0, filter: 'grayscale(1) brightness(.5)' },
  ], { duration, easing: 'cubic-bezier(.3,.58,.28,1)', fill: 'forwards' })
  const damageAnimation = damage.animate([
    { transform: 'translateX(-50%) translateY(8px)', opacity: 0 },
    { transform: 'translateX(-50%) translateY(0)', opacity: 1, offset: .12 },
    { transform: 'translateX(-50%) translateY(0)', opacity: 1, offset: .48 },
    { transform: 'translateX(-50%) translateY(-7px)', opacity: 0, offset: .68 },
    { transform: 'translateX(-50%) translateY(-7px)', opacity: 0 },
  ], { duration, easing: 'ease-out', fill: 'forwards' })
  remember(animation)
  remember(damageAnimation)
  animation.addEventListener('finish', () => {
    overlays.delete(wrapper)
    wrapper.remove()
    fieldSnapshots.delete(captured.card.instanceId)
  }, { once: true })
}

function isDefeatLeave(event: ActionEvent) {
  if (event.type !== 'leave') return false
  const text = `${event.text ?? ''} ${event.effectText ?? ''}`
  if (/即将阵亡|代替承受|返回手牌|回到手牌|位移|移动|转移|弃置/.test(text)) return false
  return /阵亡(?:（等待触发完成后进入墓地）)?|被.+击杀|击杀|消灭/.test(text)
}

function presentDefeats(job: DefeatJob) {
  job.captured.forEach((captured, index) => {
    const survivor = cardElement(captured.card.instanceId)
    if (survivor instanceof HTMLElement) animatePowerBadge(survivor)
    else if (job.event.type !== 'combat' || job.confirmedDefeatIds.has(captured.card.instanceId))
      animateDefeat(captured, job.event, index)
  })
}

function reset() {
  playbackGeneration++
  animations.forEach(animation => animation.cancel())
  animations.clear()
  overlays.forEach(element => element.remove())
  overlays.clear()
  fieldSnapshots.clear()
  defeatedInstances.clear()
  initialized = false
  lastSequence = 0
}

watch(() => props.matchId, reset, { flush: 'sync' })
watch(() => props.events.map(event => event.sequence).join(','), () => {
  const highest = Math.max(0, ...props.events.map(event => event.sequence))
  if (!initialized) {
    initialized = true
    lastSequence = highest
    void nextTick().then(refreshFieldSnapshots)
    return
  }
  const fresh = props.events.filter(event => event.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence)
  const confirmedDefeatIds = new Set(fresh.filter(isDefeatLeave)
    .flatMap(event => (event.cards ?? []).map(card => card.instanceId)))
  const jobs: DefeatJob[] = []
  for (const event of fresh) {
    if (event.type === 'attack') animateAttack(event)
    if (event.type === 'combat' || isDefeatLeave(event)) jobs.push({ event, captured: captureCards(event), confirmedDefeatIds })
    lastSequence = Math.max(lastSequence, event.sequence)
  }
  const generation = playbackGeneration
  void nextTick().then(() => {
    if (generation !== playbackGeneration) return
    jobs
      .sort((left, right) => Number(right.event.type === 'combat') - Number(left.event.type === 'combat')
        || left.event.sequence - right.event.sequence)
      .forEach(presentDefeats)
    refreshFieldSnapshots()
  })
}, { immediate: true, flush: 'pre' })
function viewportChanged() {
  const consumed = lastSequence
  const wasInitialized = initialized
  reset()
  lastSequence = consumed
  initialized = wasInitialized
  void nextTick().then(refreshFieldSnapshots)
}
onMounted(() => { window.addEventListener('l12-viewport-change', viewportChanged); void nextTick().then(refreshFieldSnapshots) })
onBeforeUnmount(() => { window.removeEventListener('l12-viewport-change', viewportChanged); reset() })
</script>

<template></template>
