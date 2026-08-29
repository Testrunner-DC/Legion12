<script setup lang="ts">
import { nextTick, onBeforeUnmount, watch } from 'vue'
import type { ActionEvent, Card } from '../types'

const props = defineProps<{ events: ActionEvent[]; matchId: string }>()

type CapturedCard = { card: Card; element: HTMLElement; rect: DOMRect }

let initialized = false
let lastSequence = 0
const animations = new Set<Animation>()
const overlays = new Set<HTMLElement>()

function cardElement(instanceId?: string) {
  if (!instanceId) return null
  return document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
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

function animateAttack(event: ActionEvent) {
  const attacker = cardElement(event.cards?.[0]?.instanceId)?.closest('.formation-slot') as HTMLElement | null
  if (!attacker) return
  const source = attacker.getBoundingClientRect()
  const targetCard = cardElement(event.cards?.[1]?.instanceId)
  const targetPlayer = event.playerIndex === undefined ? undefined : 1 - event.playerIndex
  const target = targetCard?.getBoundingClientRect()
    ?? (targetPlayer === undefined ? null : zoneElement('master', targetPlayer)?.getBoundingClientRect())
  if (!target) return
  const dx = target.left + target.width / 2 - (source.left + source.width / 2)
  const dy = target.top + target.height / 2 - (source.top + source.height / 2)
  const distance = Math.max(1, Math.hypot(dx, dy))
  const step = Math.min(18, distance * .09)
  const animation = attacker.animate([
    { transform: 'translate3d(0,0,0)' },
    { transform: `translate3d(${dx / distance * step}px,${dy / distance * step}px,0)`, offset: .48 },
    { transform: 'translate3d(0,0,0)' },
  ], { duration: 360, easing: 'cubic-bezier(.25,.72,.35,1)' })
  remember(animation)
}

function captureCombat(event: ActionEvent) {
  return (event.cards ?? []).flatMap(card => {
    const element = cardElement(card.instanceId)
    return element instanceof HTMLElement ? [{ card, element, rect: element.getBoundingClientRect() }] : []
  })
}

function animatePowerBadge(element: HTMLElement) {
  const badge = element.querySelector('.card-power')
  if (!(badge instanceof HTMLElement)) return
  const animation = badge.animate([
    { transform: 'translateX(-50%) scale(1)', filter: 'brightness(1)' },
    { transform: 'translateX(-50%) scale(1.14)', filter: 'brightness(1.55)', offset: .45 },
    { transform: 'translateX(-50%) scale(1)', filter: 'brightness(1)' },
  ], { duration: 280, easing: 'ease-out' })
  remember(animation)
}

function animateDefeat(captured: CapturedCard, event: ActionEvent) {
  const wrapper = document.createElement('div')
  const ghost = captured.element.cloneNode(true) as HTMLElement
  wrapper.className = 'l12-combat-defeat-ghost'
  Object.assign(wrapper.style, {
    position: 'fixed', left: `${captured.rect.left}px`, top: `${captured.rect.top}px`,
    width: `${captured.rect.width}px`, height: `${captured.rect.height}px`, zIndex: '2147482987',
    pointerEvents: 'none', transformOrigin: 'center', willChange: 'transform, opacity',
  })
  Object.assign(ghost.style, { width: '100%', height: '100%', margin: '0', pointerEvents: 'none' })
  const badge = ghost.querySelector('.card-power')
  if (badge instanceof HTMLElement) {
    badge.textContent = '0'
    badge.classList.remove('boosted')
    badge.classList.add('weakened')
  }
  wrapper.appendChild(ghost)
  document.body.appendChild(wrapper)
  overlays.add(wrapper)

  const owner = captured.card.ownerIndex ?? event.playerIndex ?? 0
  const graveRect = zoneElement('graveyard', owner)?.getBoundingClientRect()
  const dx = graveRect ? graveRect.left + graveRect.width / 2 - (captured.rect.left + captured.rect.width / 2) : 0
  const dy = graveRect ? graveRect.top + graveRect.height / 2 - (captured.rect.top + captured.rect.height / 2) : 12
  const animation = wrapper.animate([
    { transform: 'translate3d(0,0,0) scale(1)', opacity: 1, filter: 'grayscale(0)' },
    { transform: 'translate3d(0,0,0) scale(1)', opacity: 1, filter: 'grayscale(.35)', offset: .3 },
    { transform: `translate3d(${dx}px,${dy}px,0) scale(.48)`, opacity: 0, filter: 'grayscale(1)' },
  ], { duration: 500, easing: 'cubic-bezier(.3,.6,.3,1)', fill: 'forwards' })
  remember(animation)
  animation.addEventListener('finish', () => { overlays.delete(wrapper); wrapper.remove() }, { once: true })
}

async function playCombat(event: ActionEvent, captured: CapturedCard[]) {
  await nextTick()
  for (const item of captured) {
    const survivor = cardElement(item.card.instanceId)
    if (survivor instanceof HTMLElement) animatePowerBadge(survivor)
    else animateDefeat(item, event)
  }
}

function reset() {
  animations.forEach(animation => animation.cancel())
  animations.clear()
  overlays.forEach(element => element.remove())
  overlays.clear()
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
  const fresh = props.events.filter(event => event.sequence > lastSequence).sort((a, b) => a.sequence - b.sequence)
  for (const event of fresh) {
    if (event.type === 'attack') animateAttack(event)
    if (event.type === 'combat') void playCombat(event, captureCombat(event))
    lastSequence = Math.max(lastSequence, event.sequence)
  }
}, { immediate: true })
onBeforeUnmount(reset)
</script>

<template></template>
