<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { l12AnimationDuration, l12BattleMotionDisabled } from '../audioPreferences'
import { landscapeTeleportElement, landscapeTeleportTarget, settleElementGeometry, viewportLayoutRect, visibleViewport } from '../mobileViewport'
import { CARD_IMAGE_PLACEHOLDER, resolveCardAssetUrls } from '../cardAssets'
import type { ActionEvent, Card, PlayerView, Prompt } from '../types'
import { acquireAuthoritativeCardVisibility, resolveAuthoritativeCardVisibilitySurface } from './authoritativeCardVisibility'
import { snapshotDecodedAuthoritativeCardVisual } from './authoritativeCardVisualSnapshot'
import { beginMovementTransactionBatch, claimFreshMovementEvents, claimMovementTransaction, collectKnownCardZones, collectPromptSourceZoneHints, createMovementClaimState, entryMovementTransactionKey, finalizeMovementTransactionBatch, isAuthoritativePublicFaceMovement, isCombatDefeatLeaveEvent, isMovementCardConcealed, isSupersededLeaveEvent, leaveMovementDestination, movementCardsForEvent, movementFactKey, movementTerminalTapped, resetMovementClaimState, resolveMovementSourceZone, type MovementSourceEvidence, type VisualZone } from './visualTransitionProjection'
import type { PreparedVisualOwner, PresentationReservation, PresentationSequenceCoordinator } from './presentationSequenceCoordinator'

type Zone = VisualZone
type AnchorRect = { x: number; y: number; width: number; height: number }
type LegionEntryImpactTier = 'light' | 'medium' | 'heavy'
type Movement = {
  key: string
  sequence: number
  playerIndex: number
  label: string
  from: Zone
  to: Zone
  sourceEvidence: MovementSourceEvidence
  card?: Card
  publicPlacement?: ActionEvent['playerPublicPlacement']
  authoritativeFace: boolean
  concealed: boolean
  covered: boolean
  fromRect: AnchorRect
  toRect: AnchorRect
  fromRotation?: number
  toRotation?: number
  sourceGhost?: HTMLElement
  sourceWasGenericBack?: boolean
  preparedFaceGhost?: HTMLElement
  preparedImageUrl?: string
  disasterReveal?: boolean
  disasterSimplified?: boolean
  caption?: string
  attachment?: boolean
  entryTransactionKey?: string
  presentationRelease?: (presented?: boolean) => void
  presentationReservation?: PresentationReservation
  holderWrapper?: HTMLElement
  visualOwner?: PreparedVisualOwner
  coveredTarget?: HTMLElement
  coveredVisibilitySurface?: HTMLElement | null
  releaseTargetVisibility?: () => void
  presentationStarted?: boolean
  destinationObserver?: MutationObserver
  playedCard: boolean
  legionEntry: boolean
  entrySimplified: boolean
  entryImpactTier?: LegionEntryImpactTier
  entryImpactStrength: number
  entryImpactNode?: HTMLElement
  entryImpactAnimation?: Animation
  entryBoardAnimation?: Animation
  entryGhostImpactAnimation?: Animation
}

const props = withDefaults(defineProps<{
  events: ActionEvent[]
  players: PlayerView[]
  prompts: Prompt[]
  matchId: string
  revision: number
  viewerPlayerIndex: number
  synchronizing?: boolean
  paused?: boolean
  playbackSpeed?: number | null
  sequenceCoordinator: PresentationSequenceCoordinator
}>(), { synchronizing: false, paused: false, playbackSpeed: null })
const emit = defineEmits<{ busyChange: [busy: boolean] }>()

const active = ref<Movement | null>(null)
const presentationRoot = ref<HTMLElement | null>(null)
const reservationCount = ref(0)
const queue: Movement[] = []
const movementClaims = createMovementClaimState()
let timer: ReturnType<typeof setTimeout> | null = null
let preparationCount = 0
let viewportGeneration = 0
let awaitingMatchBaseline = true
let activePreparationToken = 0
type PreparationBatch = { generation: number; controller: AbortController }
const pendingPreparationBatches = new Set<PreparationBatch>()
const activeEventFaceReady = ref(false)
const preparedImageUrls = new Map<string, string>()
const sourceZoneHints = new Map<string, Zone>()
const pendingReservations = new Set<PresentationReservation>()
const pendingMovementHolders = new Set<Movement>()

watch(() => props.prompts, prompts => {
  // 私密区域身份只在选择期间可见。保留本次选择的区域锚点，供紧随其后的权威移动事件使用。
  for (const [instanceId, zone] of collectPromptSourceZoneHints(prompts)) sourceZoneHints.set(instanceId, zone)
}, { deep: true, immediate: true })
watch(() => collectKnownCardZones(props.players), (next, previous) => {
  // Capture the authoritative pre-update zone while its DOM still exists. This
  // also covers same-name copies because instance identity, not card name, is used.
  for (const [instanceId, zone] of previous ?? next) sourceZoneHints.set(instanceId, zone)
}, { flush: 'pre', immediate: true })

function notifyBusy() {
  // The parent also coordinates live card reveals with modal prompts.  Report
  // the real presentation state in every mode; replay is only one consumer.
  emit('busyChange', Boolean(active.value || queue.length || preparationCount))
}

function beginPreparationBatch(generation: number) {
  const batch: PreparationBatch = { generation, controller: new AbortController() }
  pendingPreparationBatches.add(batch)
  preparationCount = pendingPreparationBatches.size
  notifyBusy()
  return batch
}

function finishPreparationBatch(batch: PreparationBatch | null) {
  if (!batch || !pendingPreparationBatches.delete(batch)) return
  batch.controller.abort()
  preparationCount = pendingPreparationBatches.size
  notifyBusy()
}

function cancelPreparationBatches() {
  for (const batch of pendingPreparationBatches) batch.controller.abort()
  pendingPreparationBatches.clear()
  preparationCount = 0
}

function replayDuration(standardMs: number, liveMinimumMs: number, replayMinimumMs = liveMinimumMs) {
  if (!props.playbackSpeed) return l12AnimationDuration(standardMs, liveMinimumMs)
  return Math.max(replayMinimumMs, Math.round(l12AnimationDuration(standardMs, replayMinimumMs) / props.playbackSpeed))
}

function waitForBoundedResult<T>(promise: Promise<T>, signal: AbortSignal, timeoutMs: number) {
  return new Promise<T | undefined>(resolve => {
    let settled = false
    const finish = (value: T | undefined) => {
      if (settled) return
      settled = true
      clearTimeout(timeout)
      signal.removeEventListener('abort', aborted)
      resolve(value)
    }
    const aborted = () => finish(undefined)
    const timeout = setTimeout(aborted, timeoutMs)
    signal.addEventListener('abort', aborted, { once:true })
    if (signal.aborted) aborted()
    else void promise.then(value => finish(value), () => finish(undefined))
  })
}

function waitForImage(url: string, signal: AbortSignal, timeoutMs = 1800) {
  return new Promise<boolean>(resolve => {
    let settled = false
    const image = new Image()
    image.decoding = 'async'
    const finish = (ready: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timeout)
      signal.removeEventListener('abort', aborted)
      image.onload = null
      image.onerror = null
      if (!ready) image.src = ''
      resolve(ready)
    }
    const aborted = () => finish(false)
    const timeout = setTimeout(aborted, timeoutMs)
    signal.addEventListener('abort', aborted, { once:true })
    image.onload = () => finish(true)
    image.onerror = () => finish(false)
    if (signal.aborted) {
      aborted()
      return
    }
    image.src = url
  })
}

async function prepareMovementImage(card: Card, parentSignal: AbortSignal) {
  const key = `${card.cardId}\n${card.imageUrl ?? ''}`
  const cached = preparedImageUrls.get(key)
  if (cached) return cached
  if (parentSignal.aborted) return CARD_IMAGE_PLACEHOLDER
  const controller = new AbortController()
  const cancel = () => controller.abort()
  parentSignal.addEventListener('abort', cancel, { once:true })
  const overallTimeout = setTimeout(cancel, 4200)
  try {
    // Manifest fetches are shared and cannot be aborted here. Bound the await,
    // then fall back to the authority-provided URL while the late promise is
    // ignored. The generation signal prevents late work from caching a face.
    const resolved = await waitForBoundedResult(
      resolveCardAssetUrls(card.cardId, card.imageUrl, 'board'), controller.signal, 1800)
    if (controller.signal.aborted) return CARD_IMAGE_PLACEHOLDER
    const direct = card.imageUrl && card.imageUrl !== CARD_IMAGE_PLACEHOLDER ? [card.imageUrl] : []
    const candidates = [...new Set([...(resolved ?? []), ...direct])]
      .filter(url => url !== CARD_IMAGE_PLACEHOLDER)
    for (const url of candidates) {
      if (!await waitForImage(url, controller.signal)) continue
      if (controller.signal.aborted || parentSignal.aborted) return CARD_IMAGE_PLACEHOLDER
      preparedImageUrls.set(key, url)
      return url
    }
    // The placeholder is a confirmed last resort here, never an unresolved first frame.
    return CARD_IMAGE_PLACEHOLDER
  } finally {
    clearTimeout(overallTimeout)
    parentSignal.removeEventListener('abort', cancel)
    controller.abort()
  }
}

function waitForReservationGrant(reservation: PresentationReservation, signal: AbortSignal) {
  return new Promise<((presented?: boolean) => void) | null>(resolve => {
    let settled = false
    const finish = (release: ((presented?: boolean) => void) | null) => {
      if (settled) {
        release?.(false)
        return
      }
      settled = true
      signal.removeEventListener('abort', aborted)
      resolve(release)
    }
    const aborted = () => finish(null)
    signal.addEventListener('abort', aborted, { once:true })
    if (signal.aborted) aborted()
    else void reservation.waitUntilGranted().then(release => {
      if (signal.aborted) {
        release(false)
        finish(null)
      } else finish(release)
    })
  })
}

function cancelPendingBatchReservations(reservations: PresentationReservation[]) {
  for (const reservation of reservations) {
    if (!pendingReservations.has(reservation)) continue
    reservation.cancel()
    pendingReservations.delete(reservation)
  }
}

function waitForDecodedImage(image: HTMLImageElement, timeoutMs = 1800) {
  return new Promise<boolean>(resolve => {
    let settled = false
    const finish = (ready: boolean) => {
      if (settled) return
      settled = true
      clearTimeout(timeout)
      image.removeEventListener('load', decode)
      image.removeEventListener('error', failed)
      resolve(ready)
    }
    const failed = () => finish(false)
    const decode = () => {
      if (!image.complete || image.naturalWidth <= 0) {
        if (image.complete) finish(false)
        return
      }
      if (typeof image.decode !== 'function') {
        finish(true)
        return
      }
      void image.decode().then(() => finish(image.complete && image.naturalWidth > 0), failed)
    }
    const timeout = setTimeout(failed, timeoutMs)
    image.addEventListener('load', decode, { once:true })
    image.addEventListener('error', failed, { once:true })
    decode()
  })
}

async function waitForDecodedMovementFace(root: ParentNode) {
  const images = [...root.querySelectorAll('img')]
  return images.length === 0 || (await Promise.all(images.map(image => waitForDecodedImage(image)))).every(Boolean)
}

function movementFaceIsDecoded(root: ParentNode) {
  const images = [...root.querySelectorAll('img')]
  return images.length === 0 || images.every(image => image.complete && image.naturalWidth > 0)
}

function textSource(text: string): Zone {
  if (/从墓地|墓地中|墓地的/.test(text)) return 'graveyard'
  if (/从牌库|牌库中|牌库顶|牌库底/.test(text)) return 'library'
  if (/从手牌|手牌中|弃置.*手牌/.test(text)) return 'hand'
  if (/从圣物区|圣物区的/.test(text)) return 'relic'
  if (/从主宰区|主宰区的/.test(text)) return 'master'
  return 'field'
}

function publicHandAddCaption(event: ActionEvent, card: Card) {
  const otherPublicCard = (event.cards ?? []).find(candidate => candidate !== card && !candidate.hidden && candidate.name)
  const namedSources = [...`${event.text} ${event.effectText ?? ''}`.matchAll(/〈([^〉]+)〉/g)]
    .map(match => match[1])
    .filter(name => name !== card.name)
  const plainSource = event.text.match(/^([^〈〉：]{1,24}?)(?:展示|确认|将)/)?.[1]?.trim()
  const source = otherPublicCard?.name ?? namedSources[0] ?? plainSource
  return source ? `〈${card.name}〉因〈${source}〉加入手牌` : `〈${card.name}〉加入手牌`
}

function simplifiedEntryMotion() {
  return l12BattleMotionDisabled()
}

function legionEntryImpact(card: Card | undefined): { tier?: LegionEntryImpactTier; strength: number } {
  const rawTroops = card?.troops
  const troops = rawTroops == null ? card?.baseTroops : rawTroops
  // Impact is presentation-only and may use public authority data. Never infer
  // a missing value from private zones, card text, cost, or card identity.
  if (typeof troops !== 'number' || !Number.isFinite(troops) || troops <= 0) return { strength:0 }
  if (troops < 3000) return { tier:'light', strength:.35 }
  if (troops < 6000) return { tier:'medium', strength:.65 }
  return { tier:'heavy', strength:1 }
}

function isLegionEntryMovement(event: ActionEvent, card: Card | undefined, from: Zone, to: Zone,
  concealed: boolean) {
  return !concealed && card?.cardType === 'legion' && to === 'field' && from !== 'field'
    && (event.type === 'play' || event.type === 'put' || event.type === 'enter')
}

function authoritativeSourceZone(event: ActionEvent, card: Card | undefined, fallback: Zone,
  batchCursor?: Map<string, VisualZone>) {
  const instanceId = card?.instanceId
  return resolveMovementSourceZone(event.originZone, instanceId, fallback,
    instanceId ? batchCursor?.get(instanceId) ?? movementClaims.zones.get(instanceId) : undefined,
    instanceId ? sourceZoneHints.get(instanceId) : undefined)
}

function movementFromEvent(event: ActionEvent, cardIndex: number, fromRect: AnchorRect, toRect: AnchorRect,
  selectedCard?: Card, batchCursor?: Map<string, VisualZone>): Movement | null {
  // Combat deaths already keep the exact battlefield visual until it reaches the
  // owner's graveyard. Do not create a second card when delayed death triggers finish.
  if (event.type === 'grave' && event.text.includes('阵亡触发已完成')) return null
  let from: Zone
  let to: Zone
  let label: string
  let sourceEvidence: MovementSourceEvidence = 'fallback'
  const sourceZone = (fallback: Zone) => {
    const resolved = authoritativeSourceZone(event, selectedCard, fallback, batchCursor)
    sourceEvidence = resolved.evidence
    return resolved.zone
  }
  if (event.type === 'disaster-reveal') {
    // A disaster without a triggered effect still has a public reveal moment.
    // Keep it in the card-movement queue so it receives the same presentation
    // ordering as every other authoritative card action.
    from = 'disaster'; to = 'disaster'; label = '天灾翻开'
  } else if (event.type === 'reveal' && /加入手牌/.test(event.text)) {
    const revealed = (event.cards ?? []).find(card => !isMovementCardConcealed(event, card))
    if (!revealed) return null
    from = textSource(event.text); to = 'hand'; label = '加入手牌'
  } else if (event.type === 'counter-set') {
    from = 'hand'; to = 'field'; label = '盖伏'
  } else if (event.type === 'play') {
    from = sourceZone('hand'); to = event.cards?.[0]?.cardType === 'artifact' ? 'relic'
      : event.cards?.[0]?.cardType === 'legion' ? 'field' : 'center'; label = '打出'
  } else if (event.type === 'put' || event.type === 'enter') {
    const describedSource = textSource(event.text)
    from = sourceZone(describedSource === 'field' ? 'resolving' : describedSource); to = 'field'; label = '登场'
  } else if (event.type === 'move') {
    from = 'field'; to = 'field'; label = '位移'
  } else if (event.type === 'attach') {
    from = sourceZone('resolving'); to = 'attached'; label = '叠放'
  } else if (event.type === 'grave' || event.type === 'discard') {
    from = sourceZone(textSource(event.text)); to = 'graveyard'; label = event.type === 'discard' ? '弃置' : '入墓'
  } else if (event.type === 'mill') {
    from = 'library'; to = 'graveyard'; label = '弃置'
  } else if (event.type === 'return') {
    from = sourceZone(event.text.includes('从墓地') ? 'graveyard' : event.text.includes('从圣物区') ? 'relic' : 'field')
    to = event.text.includes('主宰区') ? 'master' : event.text.includes('手牌') ? 'hand' : event.text.includes('圣物区') ? 'relic' : 'library'
    label = '返回'
  } else if (event.type === 'leave') {
    // Ordinary authoritative field departures used to disappear between two
    // snapshots. Combat defeat owns its own motion layer; non-defeat costs,
    // discards, removals and replacements use this shared zone lane.
    from = sourceZone('field')
    to = leaveMovementDestination(event)
    label = /费用/.test(event.text) ? '支付费用' : '离场'
  } else return null

  const cards = event.cards ?? []
  const card = selectedCard ?? (event.type === 'move' ? cards.at(-1)
    : event.type === 'reveal' ? cards.find(candidate => !isMovementCardConcealed(event, candidate))
    : cards[0])
  const authoritativeFace = isAuthoritativePublicFaceMovement(event, from, to)
  const concealed = isMovementCardConcealed(event, card, from, to)
  const legionEntry = isLegionEntryMovement(event, card, from, to, concealed)
  const entryImpact: { tier?:LegionEntryImpactTier; strength:number } = legionEntry
    ? legionEntryImpact(card)
    : { strength:0 }
  return {
    key: movementFactKey(event, cardIndex, card, from, to),
    sequence: event.sequence,
    playerIndex: event.playerIndex ?? props.viewerPlayerIndex,
    label,
    from,
    to,
    sourceEvidence,
    card,
    publicPlacement: event.playerPublicPlacement,
    authoritativeFace,
    concealed,
    covered: card?.hidden === true,
    fromRect,
    toRect,
    disasterReveal: event.type === 'disaster-reveal',
    disasterSimplified:event.type === 'disaster-reveal' && simplifiedEntryMotion(),
    caption: event.type === 'reveal' && card ? publicHandAddCaption(event, card) : undefined,
    attachment: event.type === 'attach',
    playedCard: event.type === 'play' && !concealed && Boolean(card?.instanceId),
    legionEntry,
    entrySimplified:legionEntry && simplifiedEntryMotion(),
    entryImpactTier:entryImpact.tier,
    entryImpactStrength:entryImpact.strength,
  }
}

function elementRect(element: Element | null): AnchorRect | null {
  if (!(element instanceof HTMLElement)) return null
  const rect = viewportLayoutRect(element)
  if (rect.width <= 0 || rect.height <= 0) return null
  return {
    x: rect.left + rect.width / 2,
    y: rect.top + rect.height / 2,
    width: rect.width,
    height: rect.height,
  }
}
function cardElementInZone(instanceId: string | undefined, zone: Zone) {
  if (!instanceId) return null
  const root = document.querySelector('[data-l12-game-stage]')
  const boardMatches = [...(root?.querySelectorAll(`[data-card-instance-id="${CSS.escape(instanceId)}"]`) ?? [])]
  const publicOverlayMatches = zone === 'graveyard'
    ? [...document.querySelectorAll(`.graveyard-overlay [data-card-instance-id="${CSS.escape(instanceId)}"]`)]
    : []
  return [...boardMatches, ...publicOverlayMatches]
    .find(element => element.closest('[data-l12-zone]')?.getAttribute('data-l12-zone') === zone
      || (zone === 'graveyard' && Boolean(element.closest('.graveyard-overlay')))) ?? null
}
function attachmentElement(instanceId?: string) {
  if (!instanceId) return null
  const root = document.querySelector('[data-l12-game-stage]')
  return [...(root?.querySelectorAll<HTMLElement>('[data-attached-card-instance-ids]') ?? [])]
    .find(element => (element.dataset.attachedCardInstanceIds ?? '').split(/\s+/).includes(instanceId)) ?? null
}
function disasterDestinationElement() {
  const root = document.querySelector('[data-l12-battle-layout]')
  const targets = root?.querySelectorAll<HTMLElement>('button.current-disaster-card[data-l12-zone="disaster"]')
  return targets?.length === 1 ? targets[0] ?? null : null
}
function destinationElement(movement: Movement) {
  return movement.disasterReveal ? disasterDestinationElement()
    : movement.attachment ? attachmentElement(movement.card?.instanceId)
    : cardElementInZone(movement.card?.instanceId, movement.to)
}
function elementRotation(element: Element | null) {
  return element?.classList.contains('tapped') ? 90 : 0
}
function zoneElement(zone: Zone, playerIndex: number) {
  if (zone === 'center' || zone === 'attached') return null
  const root = document.querySelector('[data-l12-game-stage]')
  if (zone === 'disaster') return root?.querySelector('[data-l12-zone="disaster"]') ?? null
  return root?.querySelector(`[data-player-index="${playerIndex}"][data-l12-zone="${zone}"]`)
    ?? root?.querySelector(`[data-player-index="${playerIndex}"] [data-l12-zone="${zone}"]`)
    ?? null
}

function publicSourceHolder(zone: Zone, playerIndex: number) {
  const sourceZone = zoneElement(zone, playerIndex)
  if (!(sourceZone instanceof HTMLElement)) return null
  // Spectator hands expose count-only card backs. Clone only that already
  // public pixel source; never synthesize the newly public event face while it
  // is still standing in for an unknown private hand instance.
  if (zone === 'hand') return [...sourceZone.querySelectorAll<HTMLElement>(':scope > .card-back')].at(-1) ?? null
  // The visible graveyard pile is a public face, not a generic back. It stays
  // in the board through the pre-update capture even when a teleported public
  // GraveyardOverlay child has already reconciled its card list.
  if (zone === 'graveyard') return sourceZone.querySelector<HTMLElement>('.pile-card')
  return sourceZone.querySelector<HTMLElement>('.card-back')
}
function genericPublicSourceGhost(zone: Zone) {
  const ghost = document.createElement('div')
  ghost.className = zone === 'graveyard' ? 'pile-card card-back' : 'card-back'
  ghost.setAttribute('aria-hidden', 'true')
  return ghost
}
function disasterPublicSourceGhost() {
  const ghost = document.createElement('span')
  ghost.className = 'disaster-reveal-card'
  const back = document.createElement('img')
  back.className = 'disaster-reveal-back'
  back.src = '/assets/l12/card-back-disaster.png'
  back.alt = ''
  ghost.appendChild(back)
  return ghost
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
  return elementRect(zone === 'attached' ? attachmentElement(instanceId) : cardElementInZone(instanceId, zone))
    ?? elementRect(zoneElement(zone, playerIndex)) ?? fallbackRect(zone, playerIndex)
}

function legionEntryFocusRect(): AnchorRect {
  // Every anchor used by this layer is expressed in the logical landscape
  // canvas. On a rotated portrait device the Teleport host performs the one
  // physical rotation; applying another x/y swap here would create drift.
  const viewport = visibleViewport()
  const width = Math.max(96, Math.min(132, viewport.width * .12, viewport.height * .28))
  return { x:viewport.width / 2, y:viewport.height / 2, width, height:width * 7 / 5 }
}

function disasterRevealFocusRect(): AnchorRect {
  // Anchors stay in the logical landscape canvas. Keep the horizontal 8:5
  // reveal fully inside low-height phones as well as wide desktop boards.
  const viewport = visibleViewport()
  const width = Math.max(180, Math.min(360, viewport.width * .42, viewport.height * .72))
  return { x:viewport.width / 2, y:viewport.height / 2, width, height:width * 5 / 8 }
}

function entryTransform(dx: number, dy: number, scaleX: number, scaleY: number, rotation: number) {
  return `translate3d(${dx}px,${dy}px,0) scale(${scaleX},${scaleY}) rotate(${rotation}deg)`
}

function entryDropTransform(dx: number, dy: number, elevation: number, pitch: number,
  scaleX: number, scaleY: number, rotation: number) {
  return `perspective(900px) translate3d(${dx}px,${dy}px,${elevation}px) rotateX(${pitch}deg) scale(${scaleX},${scaleY}) rotate(${rotation}deg)`
}

function legionEntryFrames(movement: Movement): Keyframe[] {
  const source = movement.fromRect
  const target = movement.toRect
  const focus = legionEntryFocusRect()
  const centerDx = focus.x - source.x
  const centerDy = focus.y - source.y
  const targetDx = target.x - source.x
  const targetDy = target.y - source.y
  const focusScaleX = Math.max(.45, Math.min(2.4, focus.width / Math.max(1, source.width)))
  const focusScaleY = Math.max(.45, Math.min(2.4, focus.height / Math.max(1, source.height)))
  const targetScaleX = Math.max(.45, Math.min(1.8, target.width / Math.max(1, source.width)))
  const targetScaleY = Math.max(.45, Math.min(1.8, target.height / Math.max(1, source.height)))
  const impact = movement.entryImpactStrength
  const impactScale = 1 - impact * .035
  const impactSquash = 1 - impact * .10
  const lift = 72 + impact * 48
  const elevation = 80 + impact * 60
  return [
    { transform:entryTransform(0, 0, 1, 1, movement.fromRotation ?? 0), opacity:1, offset:0 },
    { transform:entryTransform(centerDx, centerDy, focusScaleX * .96, focusScaleY * .96, 0), opacity:1,
      easing:'cubic-bezier(.22,1,.36,1)', offset:.30 },
    { transform:entryTransform(centerDx, centerDy, focusScaleX, focusScaleY, 0), opacity:1, offset:.40 },
    { transform:entryTransform(centerDx, centerDy, focusScaleX, focusScaleY, 0), opacity:1,
      easing:'cubic-bezier(.2,.8,.3,1)', offset:.55 },
    { transform:entryDropTransform(targetDx, targetDy - lift, elevation, -18, targetScaleX,
      targetScaleY, movement.toRotation ?? 0), opacity:1,
      filter:'drop-shadow(0 34px 22px rgba(0,0,0,.5))',
      easing:'cubic-bezier(.55,0,.9,.45)', offset:.65 },
    { transform:entryDropTransform(targetDx, targetDy, 0, 0, targetScaleX * impactScale,
      targetScaleY * impactSquash, movement.toRotation ?? 0), opacity:1,
      filter:'drop-shadow(0 2px 3px rgba(0,0,0,.85))', offset:.82 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX, targetScaleY, movement.toRotation ?? 0),
      filter:'drop-shadow(0 8px 10px rgba(0,0,0,.72))', opacity:1, offset:.92 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX, targetScaleY, movement.toRotation ?? 0),
      filter:'drop-shadow(0 8px 10px rgba(0,0,0,.72))', opacity:1, offset:1 },
  ]
}

function playedCardFrames(movement: Movement): Keyframe[] {
  const source = movement.fromRect
  const target = movement.toRect
  const focus = legionEntryFocusRect()
  const centerDx = focus.x - source.x
  const centerDy = focus.y - source.y
  const targetDx = target.x - source.x
  const targetDy = target.y - source.y
  const focusScaleX = Math.max(.45, Math.min(2.4, focus.width / Math.max(1, source.width)))
  const focusScaleY = Math.max(.45, Math.min(2.4, focus.height / Math.max(1, source.height)))
  const targetScaleX = Math.max(.45, Math.min(1.8, target.width / Math.max(1, source.width)))
  const targetScaleY = Math.max(.45, Math.min(1.8, target.height / Math.max(1, source.height)))
  return [
    { transform:entryTransform(0, 0, 1, 1, movement.fromRotation ?? 0), opacity:1, offset:0 },
    { transform:entryDropTransform(centerDx, centerDy, 26, -6, focusScaleX, focusScaleY, 0), opacity:1,
      filter:'drop-shadow(0 20px 16px rgba(0,0,0,.62))', easing:'cubic-bezier(.2,.72,.2,1)', offset:.36 },
    { transform:entryDropTransform(centerDx, centerDy, 26, -6, focusScaleX, focusScaleY, 0), opacity:1,
      filter:'drop-shadow(0 20px 16px rgba(0,0,0,.62))', offset:.58 },
    { transform:entryDropTransform(targetDx, targetDy, 0, 0, targetScaleX * .985, targetScaleY * .97,
      movement.toRotation ?? 0), opacity:1, filter:'drop-shadow(0 2px 3px rgba(0,0,0,.85))', offset:.88 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX, targetScaleY, movement.toRotation ?? 0),
      opacity:1, filter:'drop-shadow(0 8px 10px rgba(0,0,0,.72))', offset:1 },
  ]
}

function disasterRevealFrames(movement: Movement): Keyframe[] {
  const source = movement.fromRect
  const target = movement.toRect
  const focus = disasterRevealFocusRect()
  const centerDx = focus.x - source.x
  const centerDy = focus.y - source.y
  const targetDx = target.x - source.x
  const targetDy = target.y - source.y
  const sourceScaleX = Math.max(.12, Math.min(1.2, source.width / Math.max(1, focus.width)))
  const sourceScaleY = Math.max(.12, Math.min(1.2, source.height / Math.max(1, focus.height)))
  const targetScaleX = Math.max(.12, Math.min(1.2, target.width / Math.max(1, focus.width)))
  const targetScaleY = Math.max(.12, Math.min(1.2, target.height / Math.max(1, focus.height)))
  if (movement.disasterSimplified) return [
    { transform:entryTransform(0, 0, sourceScaleX, sourceScaleY, 0), opacity:1 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX, targetScaleY, 0), opacity:1 },
  ]
  return [
    { transform:entryTransform(0, 0, sourceScaleX, sourceScaleY, 0), opacity:1, offset:0 },
    { transform:entryTransform(centerDx, centerDy, .96, .96, 0), opacity:1,
      easing:'cubic-bezier(.22,1,.36,1)', offset:.26 },
    { transform:entryTransform(centerDx, centerDy, 1, 1, 0), opacity:1, offset:.38 },
    { transform:entryTransform(centerDx, centerDy, 1, 1, 0), opacity:1,
      easing:'cubic-bezier(.4,0,1,1)', offset:.72 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX * 1.04, targetScaleY * 1.04, 0),
      opacity:1, offset:.90 },
    { transform:entryTransform(targetDx, targetDy, targetScaleX, targetScaleY, 0), opacity:1, offset:1 },
  ]
}

function movementDuration(movement: Movement) {
  if (movement.disasterReveal)
    return movement.disasterSimplified ? replayDuration(130, 80, 60) : replayDuration(1040, 520, 260)
  if (movement.legionEntry) {
    if (movement.entrySimplified) return replayDuration(130, 80, 60)
    return replayDuration(820, 320, 160)
  }
  if (movement.playedCard) return replayDuration(680, 280, 140)
  const queueScale = queue.length >= 2 ? .8 : 1
  if (!movement.sourceGhost) return Math.round(replayDuration(440, 180, 100) * queueScale)
  const distance = Math.hypot(movement.toRect.x - movement.fromRect.x, movement.toRect.y - movement.fromRect.y)
  return Math.round(replayDuration(Math.round(Math.min(500, Math.max(340, 320 + distance * .16))), 180, 100) * queueScale)
}

const motionStyle = computed(() => {
  if (!active.value) return {}
  const start = active.value.fromRect
  const finish = active.value.toRect
  const style: Record<string, string> = {
    '--move-from-x': `${start.x}px`,
    '--move-from-y': `${start.y}px`,
    '--move-to-x': `${finish.x}px`,
    '--move-to-y': `${finish.y}px`,
    '--move-from-scale': `${Math.max(.55, Math.min(1.45, start.width / 72))}`,
    '--move-to-scale': `${Math.max(.55, Math.min(1.45, finish.width / 72))}`,
    '--move-contact-scale': `${Math.max(.55, Math.min(1.45, finish.width / 72)) * .97}`,
    '--move-flight-contact-scale': `${Math.max(.55, Math.min(1.45, finish.width / 72)) * .98}`,
    '--move-duration': `${movementDuration(active.value)}ms`,
    '--move-from-rotation': `${active.value.fromRotation ?? 0}deg`,
    '--move-to-rotation': `${active.value.toRotation ?? 0}deg`,
  }
  if (active.value.legionEntry) {
    const focus = legionEntryFocusRect()
    const toScale = Math.max(.55, Math.min(1.45, finish.width / 72))
    const impact = active.value.entryImpactStrength
    style['--move-center-x'] = `${focus.x}px`
    style['--move-center-y'] = `${focus.y}px`
    style['--move-center-scale'] = `${Math.max(.55, Math.min(1.85, focus.width / 72))}`
    style['--move-impact-scale'] = `${toScale * (1 - impact * .035)}`
    style['--move-drop-lift'] = `${72 + impact * 48}px`
    style['--move-drop-elevation'] = `${80 + impact * 60}px`
    style['--move-impact-squash'] = `${toScale * (1 - impact * .10)}`
    style['--entry-impact-strength'] = `${impact}`
    style['--entry-impact-opacity'] = `${.28 + impact * .42}`
    style['--entry-impact-mid-scale'] = `${.68 + impact * .12}`
    style['--entry-impact-end-scale'] = `${1.08 + impact * .42}`
  }
  if (active.value.playedCard) {
    const focus = legionEntryFocusRect()
    style['--move-center-x'] = `${focus.x}px`
    style['--move-center-y'] = `${focus.y}px`
    style['--move-center-scale'] = `${Math.max(.55, Math.min(1.85, focus.width / 72))}`
  }
  return style
})

let activeGhostWrapper: HTMLElement | null = null
let activeGhostAnimation: Animation | null = null
function revealTarget(movement: Movement | null = active.value) {
  movement?.destinationObserver?.disconnect()
  if (movement) movement.destinationObserver = undefined
  movement?.releaseTargetVisibility?.()
  if (movement) {
    movement.releaseTargetVisibility = undefined
    movement.coveredTarget = undefined
    movement.coveredVisibilitySurface = undefined
  }
}
function observeDestination(movement: Movement) {
  movement.destinationObserver?.disconnect()
  movement.destinationObserver = undefined
  const coverCurrent = () => {
    const destination = destinationElement(movement)
    if (destination instanceof HTMLElement) coverTarget(movement, destination)
  }
  coverCurrent()
  const root = document.querySelector('[data-l12-game-stage]')
  if (!(root instanceof HTMLElement) || typeof MutationObserver === 'undefined') return
  const observer = new MutationObserver(coverCurrent)
  // PlayerMat keys formation slots, not the CardTile inside them. A reused
  // destination can therefore become this instance through an attribute-only
  // patch. Observe that identity attribute explicitly; never observe style,
  // because acquiring the visibility lease writes style and would self-loop.
  observer.observe(root, { childList:true, subtree:true, attributes:true,
    attributeFilter:['data-card-instance-id'] })
  movement.destinationObserver = observer
}
function coverTarget(movement: Movement, target: HTMLElement) {
  const visibilitySurface = resolveAuthoritativeCardVisibilitySurface(target)
  if (movement.coveredTarget === target
    && movement.coveredVisibilitySurface === visibilitySurface
    && movement.releaseTargetVisibility) return
  // A keyed battlefield card can receive a new DOM node after a row/slot move.
  // Cover the replacement before releasing the old node so authority never
  // flashes between a prepared holder and its eventual destination.
  const releaseNext = acquireAuthoritativeCardVisibility(target)
  const releasePrevious = movement.releaseTargetVisibility
  movement.coveredTarget = target
  movement.coveredVisibilitySurface = visibilitySurface
  movement.releaseTargetVisibility = releaseNext
  releasePrevious?.()
}

function cleanupEntryImpact(movement: Movement) {
  movement.entryGhostImpactAnimation?.cancel()
  movement.entryGhostImpactAnimation = undefined
  movement.entryBoardAnimation?.cancel()
  movement.entryBoardAnimation = undefined
  if (movement.entryImpactAnimation) {
    movement.entryImpactAnimation.onfinish = null
    movement.entryImpactAnimation.oncancel = null
    movement.entryImpactAnimation.cancel()
    movement.entryImpactAnimation = undefined
  }
  movement.entryImpactNode?.remove()
  movement.entryImpactNode = undefined
}

function startEntryBoardImpact(movement: Movement, duration: number) {
  if (!movement.legionEntry || movement.entrySimplified || movement.entryImpactTier !== 'heavy') return
  // Animate only this board's inner surface. Individual translate leaves the
  // stage scale and the mobile landscape rotation untouched.
  const board = presentationRoot.value?.closest<HTMLElement>('[data-l12-game-board]')
  if (!board) return
  movement.entryBoardAnimation?.cancel()
  const frames: Keyframe[] = [
    { translate:'0px 0px', offset:0 },
    { translate:'0px 0px', offset:.80 },
    { translate:'0px 5px', offset:.82 },
    { translate:'-7px -3px', offset:.86 },
    { translate:'3px 1px', offset:.92 },
    { translate:'0px 0px', offset:1 },
  ]
  const timing: KeyframeAnimationOptions = { duration, easing:'linear', composite:'add' }
  movement.entryBoardAnimation = board.animate(frames, timing)
  if (movement.holderWrapper) {
    // The flying face lives outside the scaled board. Once it touches down it
    // must share the same camera displacement, not float over a shaking slot.
    const scale = board.offsetWidth > 0 ? viewportLayoutRect(board).width / board.offsetWidth : 1
    movement.entryGhostImpactAnimation = movement.holderWrapper.animate(frames.map(frame => ({
      offset:frame.offset,
      translate:String(frame.translate).split(' ').map(value => `${Number.parseFloat(value) * scale}px`).join(' '),
    })), timing)
  }
}

function createEntryImpactNode(movement: Movement) {
  if (!movement.legionEntry || movement.entrySimplified || !movement.entryImpactTier) return null
  const node = document.createElement('span')
  node.dataset.entryImpact = movement.entryImpactTier
  node.setAttribute('aria-hidden', 'true')
  Object.assign(node.style, {
    position:'absolute', inset:'-18%', zIndex:'0', borderRadius:'50%', pointerEvents:'none', opacity:'0',
    border:'2px solid rgba(222,191,104,.9)', boxShadow:'0 0 16px rgba(222,191,104,.72)',
    transform:'scale(.45)', transformOrigin:'center', willChange:'transform, opacity',
  })
  movement.entryImpactNode = node
  return node
}

function startEntryImpact(movement: Movement, duration: number) {
  startEntryBoardImpact(movement, duration)
  const node = movement.entryImpactNode
  if (!node || movement.entrySimplified || movement.entryImpactStrength <= 0) return
  const strength = movement.entryImpactStrength
  movement.entryImpactAnimation = node.animate([
    { opacity:0, transform:'scale(.45)', offset:0 },
    { opacity:0, transform:'scale(.45)', offset:.66 },
    { opacity:.28 + strength * .42, transform:`scale(${.68 + strength * .12})`, offset:.78 },
    { opacity:0, transform:`scale(${1.08 + strength * .42})`, offset:1 },
  ], { duration, easing:'linear', fill:'forwards' })
}

function disposeMovementHolder(movement: Movement) {
  cleanupEntryImpact(movement)
  if (movement.presentationReservation) movement.presentationReservation.cancel()
  else movement.visualOwner?.dispose()
}

function createMovementHolder(movement: Movement, sourceGhost: HTMLElement) {
  const host = landscapeTeleportElement()
  if (!host || !movementFaceIsDecoded(sourceGhost)) return false
  sourceGhost.classList.remove('tapped', 'selected')
  sourceGhost.dataset.l12ZoneAnimationFace = 'true'
  Object.assign(sourceGhost.style, {
    width: '100%', height: '100%', margin: '0', transform: 'none', transition: 'none',
    visibility: 'visible', pointerEvents: 'none',
    border: '0', outline: 'none', boxShadow: 'none', background: 'transparent',
  })
  sourceGhost.removeAttribute('id')
  sourceGhost.removeAttribute('data-card-instance-id')
  sourceGhost.querySelectorAll('[id]').forEach(node => node.removeAttribute('id'))
  sourceGhost.querySelectorAll('[data-card-instance-id]').forEach(node => node.removeAttribute('data-card-instance-id'))
  const source = movement.fromRect
  const disasterFocus = movement.disasterReveal ? disasterRevealFocusRect() : null
  const holderWidth = disasterFocus?.width ?? source.width
  const holderHeight = disasterFocus?.height ?? source.height
  const wrapper = document.createElement('div')
  wrapper.className = movement.disasterReveal
    ? 'l12-zone-flight-ghost l12-disaster-reveal-holder'
    : 'l12-zone-flight-ghost'
  wrapper.dataset.movementKey = movement.key
  wrapper.dataset.movementInstanceId = movement.card?.instanceId ?? ''
  wrapper.dataset.movementFrom = movement.from
  wrapper.dataset.movementTo = movement.to
  wrapper.dataset.movementFromX = `${source.x}`
  wrapper.dataset.movementFromY = `${source.y}`
  if (movement.disasterReveal) {
    wrapper.dataset.disasterReveal = 'true'
    wrapper.dataset.disasterMotion = movement.disasterSimplified ? 'handoff' : 'center-hold'
    wrapper.dataset.uiContract = 'authoritative-zone-card-movement'
  }
  if (movement.legionEntry) {
    wrapper.dataset.entryPresentation = 'legion-center-land'
    if (movement.entryImpactTier) wrapper.dataset.entryImpactTier = movement.entryImpactTier
    wrapper.style.setProperty('--entry-impact-strength', `${movement.entryImpactStrength}`)
  }
  Object.assign(wrapper.style, {
    position: 'fixed', left: `${source.x - holderWidth / 2}px`, top: `${source.y - holderHeight / 2}px`,
    width: `${holderWidth}px`, height: `${holderHeight}px`, zIndex: '902', pointerEvents: 'none',
    // A descendant may override inherited visibility, and the flight animates
    // opacity itself. Clip all prepared pixels until this owner is shown.
    visibility: 'hidden', clipPath: 'inset(50%)', transformOrigin: 'center', willChange: 'transform, opacity',
    transform: movement.disasterReveal
      ? `scale(${Math.max(.12, Math.min(1.2, source.width / Math.max(1, holderWidth)))},${Math.max(.12, Math.min(1.2, source.height / Math.max(1, holderHeight)))})`
      : `rotate(${movement.fromRotation ?? 0}deg)`,
    filter: 'drop-shadow(0 8px 10px rgba(0,0,0,.72))',
  })
  if (movement.disasterReveal) wrapper.style.setProperty('--move-duration', `${movementDuration(movement)}ms`)
  const impactNode = createEntryImpactNode(movement)
  if (impactNode) wrapper.appendChild(impactNode)
  sourceGhost.style.position = 'relative'
  sourceGhost.style.zIndex = '1'
  wrapper.appendChild(sourceGhost)
  if (movement.caption) {
    const caption = document.createElement('span')
    caption.className = 'l12-zone-flight-caption'
    caption.textContent = movement.caption
    Object.assign(caption.style, {
      position: 'absolute', left: '50%', bottom: 'calc(100% + 7px)', transform: 'translateX(-50%)',
      width: 'max-content', maxWidth: '240px', padding: '3px 7px', border: '1px solid #8cc6d2',
      background: 'rgba(7,16,20,.94)', color: '#eef6f5', fontSize: '12px', lineHeight: '1.35',
      whiteSpace: 'normal', textAlign: 'center', overflowWrap: 'anywhere',
    })
    wrapper.appendChild(caption)
  }
  host.appendChild(wrapper)
  movement.holderWrapper = wrapper
  const visualKey = movement.card?.instanceId ?? movement.key
  const owner: PreparedVisualOwner = {
    show: () => {
      const destination = destinationElement(movement)
      if (destination instanceof HTMLElement) coverTarget(movement, destination)
      observeDestination(movement)
      wrapper.style.clipPath = 'none'
      wrapper.style.visibility = 'visible'
    },
    hide: () => {
      wrapper.style.clipPath = 'inset(50%)'
      wrapper.style.visibility = 'hidden'
      revealTarget(movement)
    },
    dispose: () => {
      cleanupEntryImpact(movement)
      revealTarget(movement)
      wrapper.remove()
      if (movement.holderWrapper === wrapper) movement.holderWrapper = undefined
      if (movement.visualOwner === owner) movement.visualOwner = undefined
      pendingMovementHolders.delete(movement)
    },
  }
  movement.visualOwner = owner
  pendingMovementHolders.add(movement)
  movement.presentationReservation?.prepareVisualOwner(owner)
  return true
}

function stampMovementLandingGeometry(movement: Movement) {
  const wrapper = movement.holderWrapper
  if (!wrapper) return
  wrapper.dataset.movementToX = `${movement.toRect.x}`
  wrapper.dataset.movementToY = `${movement.toRect.y}`
  wrapper.dataset.movementToWidth = `${movement.toRect.width}`
  wrapper.dataset.movementToHeight = `${movement.toRect.height}`
  wrapper.dataset.movementToRotation = `${movement.toRotation ?? 0}`
}

function showNext() {
  if (active.value || props.paused || queue.length === 0) return
  active.value = queue.shift() ?? null
  if (!active.value) return
  const movement = active.value
  activeEventFaceReady.value = false
  const presentationKey = movement.key
  const presentationSequence = movement.sequence
  const presentationGeneration = viewportGeneration
  const preparationToken = ++activePreparationToken
  const ownsActivePresentation = () => active.value?.key === presentationKey
    && viewportGeneration === presentationGeneration
    && activePreparationToken === preparationToken
  const registerStartedPresentation = (movement: Movement) => {
    if (movement.entryTransactionKey)
      props.sequenceCoordinator.registerEntryMovementTransaction(movement.entryTransactionKey)
    props.sequenceCoordinator.registerPresentationFact(presentationSequence)
  }
  notifyBusy()
  const coverDestination = () => {
    if (!ownsActivePresentation()) return false
    const destination = destinationElement(movement)
    if (!(destination instanceof HTMLElement)) return true
    coverTarget(movement, destination)
    // The flight owns the visible transform. If a preceding state change left
    // this authority node mid-motion, finish it while covered for handoff.
    try {
      settleElementGeometry(destination)
      return true
    } catch {
      revealTarget()
      return false
    }
  }
  const finish = () => {
    if (!ownsActivePresentation()) return
    activePreparationToken++
    if (timer) clearTimeout(timer)
    cleanupEntryImpact(movement)
    if (activeGhostAnimation) {
      activeGhostAnimation.onfinish = null
      activeGhostAnimation.oncancel = null
      try { activeGhostAnimation.commitStyles() } catch { /* A cancelled animation has no frame to retain. */ }
      activeGhostAnimation.cancel()
    }
    activeGhostAnimation = null
    const destination = destinationElement(movement)
    if (destination instanceof HTMLElement) settleElementGeometry(destination)
    activeGhostWrapper = null
    // Template-rendered flights have no prepared visual owner to release their
    // destination lease. Cloned holders remain owned by the coordinator so a
    // successor can take over without revealing authority between actions.
    if (!movement.visualOwner) revealTarget(movement)
    const release = movement.presentationRelease
    movement.presentationRelease = undefined
    release?.(Boolean(movement.presentationStarted))
    active.value = null
    activeEventFaceReady.value = false
    timer = null
    showNext()
    notifyBusy()
  }
  try {
    const ghost = movement.sourceGhost
    if (ghost) {
      const source = movement.fromRect
      const target = movement.toRect
      const wrapper = movement.holderWrapper
      if (!wrapper || !movement.visualOwner) { finish(); return }
      stampMovementLandingGeometry(movement)
      if (movement.preparedFaceGhost) {
        ghost.replaceWith(movement.preparedFaceGhost)
        movement.sourceGhost = movement.preparedFaceGhost
      }
      activeGhostWrapper = wrapper
      void waitForDecodedMovementFace(wrapper).then(decoded => {
        if (!ownsActivePresentation()) return
        if (!decoded || !coverDestination()) {
          finish()
          return
        }
        const dx = target.x - source.x
        const dy = target.y - source.y
        const scaleX = Math.max(.45, Math.min(1.8, target.width / Math.max(1, source.width)))
        const scaleY = Math.max(.45, Math.min(1.8, target.height / Math.max(1, source.height)))
        const lift = -Math.min(40, Math.hypot(dx, dy) * .08)
        const tilt = Math.max(-3, Math.min(3, dx * .01))
        const duration = movementDuration(movement)
        wrapper.style.setProperty('--move-duration', `${duration}ms`)
        const directFrames = [
          { transform: `translate3d(0,0,0) scale(1) rotate(${movement.fromRotation ?? 0}deg)`, opacity:1 },
          { transform: `translate3d(${dx}px,${dy}px,0) scale(${scaleX},${scaleY}) rotate(${movement.toRotation ?? 0}deg)`, opacity:1 },
        ]
        const frames = movement.disasterReveal
          ? disasterRevealFrames(movement)
          : movement.legionEntry
          ? movement.entrySimplified ? directFrames : legionEntryFrames(movement)
          : [
              { transform: `translate3d(0,0,0) scale(1) rotate(${movement.fromRotation ?? 0}deg)`, opacity: 1 },
              { transform: `translate3d(${dx / 2}px,${dy / 2 + lift}px,0) scale(${(1 + scaleX) / 2},${(1 + scaleY) / 2}) rotate(${((movement.fromRotation ?? 0) + (movement.toRotation ?? 0)) / 2 + tilt}deg)`, opacity: 1, offset: .5 },
              { transform: `translate3d(${dx}px,${dy}px,0) scale(${scaleX * .99},${scaleY * .98}) rotate(${movement.toRotation ?? 0}deg)`, opacity: 1, offset: .88 },
              { transform: `translate3d(${dx}px,${dy}px,0) scale(${scaleX},${scaleY}) rotate(${movement.toRotation ?? 0}deg)`, opacity: 1 },
            ]
        if (movement.playedCard && !movement.legionEntry && !movement.disasterReveal)
          frames.splice(0, frames.length, ...playedCardFrames(movement))
        if (movement.disasterReveal) wrapper.classList.add('zone-card-movement', 'movement-ready')
        activeGhostAnimation = wrapper.animate(frames,
          { duration, easing: movement.legionEntry || movement.disasterReveal || movement.playedCard ? 'linear' : 'cubic-bezier(.2,.72,.2,1)', fill: 'forwards' })
        startEntryImpact(movement, duration)
        movement.presentationStarted = true
        registerStartedPresentation(movement)
        activeGhostAnimation.onfinish = finish
        activeGhostAnimation.oncancel = finish
        timer = setTimeout(finish, duration + replayDuration(80, 20))
      }).catch(() => finish())
      return
    }
    const duration = movementDuration(active.value)
    void nextTick(async () => {
      if (active.value?.key !== presentationKey) return
      const escapedKey = CSS.escape(presentationKey)
      const root = document.querySelector(`[data-movement-key="${escapedKey}"]`)
      if (!root || !await waitForDecodedMovementFace(root) || !ownsActivePresentation()) {
        if (ownsActivePresentation()) finish()
        return
      }
      if (!coverDestination()) {
        finish()
        return
      }
      activeEventFaceReady.value = true
      active.value.presentationStarted = true
      registerStartedPresentation(active.value)
      startEntryBoardImpact(active.value, duration)
      timer = setTimeout(finish, duration + replayDuration(20, 10))
    }).catch(() => finish())
  } catch {
    finish()
  }
}

function cancelActiveMovement() {
  activePreparationToken++
  if (timer) clearTimeout(timer)
  timer = null
  if (activeGhostAnimation) {
    activeGhostAnimation.onfinish = null
    activeGhostAnimation.oncancel = null
    try { activeGhostAnimation.commitStyles() } catch { /* A cancelled animation has no frame to retain. */ }
    activeGhostAnimation.cancel()
  }
  activeGhostAnimation = null
  const movement = active.value
  if (movement) cleanupEntryImpact(movement)
  if (movement) {
    const destination = destinationElement(movement)
    if (destination instanceof HTMLElement) settleElementGeometry(destination)
  }
  activeGhostWrapper = null
  if (movement && !movement.visualOwner) revealTarget(movement)
  const release = movement?.presentationRelease
  if (movement) movement.presentationRelease = undefined
  release?.(Boolean(movement?.presentationStarted))
  active.value = null
  activeEventFaceReady.value = false
  notifyBusy()
}
function reset() {
  viewportGeneration++
  cancelPreparationBatches()
  cancelActiveMovement()
  for (const movement of queue) {
    cleanupEntryImpact(movement)
    const release = movement.presentationRelease
    movement.presentationRelease = undefined
    release?.(false)
  }
  queue.length = 0
  for (const movement of [...pendingMovementHolders]) disposeMovementHolder(movement)
  resetMovementClaimState(movementClaims, Math.max(0, ...props.events.map(event => event.sequence)),
    props.revision, collectKnownCardZones(props.players))
  sourceZoneHints.clear()
  for (const reservation of pendingReservations) reservation.cancel()
  pendingReservations.clear()
  props.sequenceCoordinator.clearEntryMovementTransactions()
  props.sequenceCoordinator.clearPresentationFacts()
  for (const [instanceId, zone] of collectKnownCardZones(props.players)) sourceZoneHints.set(instanceId, zone)
  for (const [instanceId, zone] of collectPromptSourceZoneHints(props.prompts)) sourceZoneHints.set(instanceId, zone)
  notifyBusy()
}

watch(() => props.matchId, () => {
  reset()
  // reset cancels old work synchronously, but props.revision can still be the
  // previous match's value here. Rebase once from the coherent pre-render tuple.
  awaitingMatchBaseline = true
}, { flush: 'sync', immediate: true })
watch(() => [props.revision, props.synchronizing, props.events.map(event => event.sequence).join(','), props.matchId] as const, async ([revision, synchronizing]) => {
  if (awaitingMatchBaseline) {
    awaitingMatchBaseline = false
    reset()
    return
  }
  if (synchronizing || (props.playbackSpeed && revision < movementClaims.zoneRevision)) {
    reset()
    return
  }
  // This advances the cursor synchronously, before the first await below. A
  // rapid next snapshot therefore cannot reclaim events still being prepared.
  const fresh = claimFreshMovementEvents(props.events, movementClaims)
  const authoritativeZones = collectKnownCardZones(props.players)
  const transactionBatch = beginMovementTransactionBatch(movementClaims, revision, authoritativeZones)
  const drafts: Array<{ event: ActionEvent; cardIndex: number; draft: Movement }> = []
  if (transactionBatch) for (const event of fresh) {
    if (isSupersededLeaveEvent(event, fresh) || isCombatDefeatLeaveEvent(event)) continue
    const cards = movementCardsForEvent(event)
      .filter(card => event.type !== 'reveal' || !isMovementCardConcealed(event, card))
    for (const [cardIndex, card] of cards.entries()) {
      const draft = movementFromEvent(event, cardIndex,
        fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex),
        fallbackRect('center', event.playerIndex ?? props.viewerPlayerIndex), card, transactionBatch.cursor)
      if (!draft) continue
      const claimed = claimMovementTransaction(movementClaims, transactionBatch, {
        key: draft.key,
        sequence: draft.sequence,
        cardIndex,
        instanceId: draft.card?.instanceId ?? `cardless-${draft.sequence}-${cardIndex}`,
        from: draft.from,
        to: draft.to,
        allowSameZone: event.type === 'move' || event.type === 'disaster-reveal',
      })
      if (claimed) {
        // Opponent/spectator projections deliberately omit private hand
        // instances. A public legion/artifact `play` with a stable enter identity
        // and its matching public destination is itself an authoritative movement
        // contract; generic `put` layout fallbacks never receive this evidence.
        const entrySourceEvidence = draft.sourceEvidence === 'fallback' && event.type === 'play'
          ? 'play-contract'
          : draft.sourceEvidence
        const entryTransactionKey = entryMovementTransactionKey(event, draft.from, draft.to, entrySourceEvidence)
        draft.entryTransactionKey = entryTransactionKey ?? undefined
        drafts.push({ event, cardIndex, draft })
      }
    }
  }
  if (transactionBatch) finalizeMovementTransactionBatch(movementClaims, transactionBatch, authoritativeZones)
  const reservations = drafts.map(({ event, draft }) => {
    const visualKey = draft.card?.instanceId
    const reservation = props.sequenceCoordinator.reserve(event.sequence, 20, { visualKey })
    reservation.setPaused(props.paused)
    draft.presentationReservation = reservation
    pendingReservations.add(reservation)
    return reservation
  })
  reservationCount.value += reservations.length
  const hasMovement = drafts.length > 0
  const generation = viewportGeneration
  const preparation = hasMovement ? beginPreparationBatch(generation) : null
  const starts = drafts.map(({ draft }) => {
    // A disaster is revealed from its dedicated deck/active-disaster anchor,
    // not cloned from the newly visible session thumbnail.  Using that
    // thumbnail as a source would skip the card-back/front flip entirely.
    // Public library-boundary events present the authoritative event face.
    // Their source instance may already have disappeared, while cardElement
    // can now resolve to the final graveyard face or library back. Never clone
    // that post-update DOM in reverse; start from the real zone anchor instead.
    const source = draft.disasterReveal || draft.authoritativeFace ? null
      : draft.from === 'attached' ? attachmentElement(draft.card?.instanceId)
      : cardElementInZone(draft.card?.instanceId, draft.from)
    const publicHolder = draft.disasterReveal ? disasterDestinationElement()
      : source instanceof HTMLElement ? source : publicSourceHolder(draft.from, draft.playerIndex)
    const usesGraveyardAnchor = !(source instanceof HTMLElement) && draft.from === 'graveyard'
      && publicHolder instanceof HTMLElement
    // A closed graveyard only exposes its current pile top, which may be a
    // different instance. Borrow that node's public geometry, never its face.
    const sourceGhost = draft.disasterReveal ? disasterPublicSourceGhost()
      : publicHolder instanceof HTMLElement
      ? usesGraveyardAnchor ? genericPublicSourceGhost(draft.from) : publicHolder.cloneNode(true) as HTMLElement
      : undefined
    if (sourceGhost && publicHolder instanceof HTMLElement && !usesGraveyardAnchor && !draft.disasterReveal)
      snapshotDecodedAuthoritativeCardVisual(publicHolder, sourceGhost)
    const start = {
      rect: elementRect(publicHolder) ?? resolveRect(draft.from, draft.playerIndex,
        draft.disasterReveal || draft.authoritativeFace ? undefined : draft.card?.instanceId),
      ghost: sourceGhost,
      rotation: elementRotation(publicHolder),
      // Any zone-level fallback is only an anchor, even when the pile happens
      // to expose a public face: the pile top may be a different grave card.
      // Replace it with the authority event card before motion starts.
      genericBack: !draft.disasterReveal && !(source instanceof HTMLElement) && publicHolder instanceof HTMLElement,
    }
    draft.fromRect = start.rect
    draft.fromRotation = start.rotation
    draft.sourceGhost = start.ghost
    draft.sourceWasGenericBack = start.genericBack
    if (start.ghost && !draft.disasterReveal) createMovementHolder(draft, start.ghost)
    return start
  })
  try {
    await nextTick()
    if (generation !== viewportGeneration || preparation?.controller.signal.aborted) {
      for (const reservation of reservations) {
        reservation.cancel()
        pendingReservations.delete(reservation)
      }
      return
    }
    for (const [index, { draft }] of drafts.entries()) {
      const destination = destinationElement(draft)
    // An attach event may contain host and source in either order. Only cards that
    // actually became attached receive a movement; the host keeps its stable DOM.
      if ((draft.attachment || draft.disasterReveal) && !destination) {
        reservations[index]?.cancel()
        if (reservations[index]) pendingReservations.delete(reservations[index]!)
        continue
      }
    // Keep the exact transaction-normalized from/to identity accepted above.
    // Re-deriving it after nextTick would read the final snapshot and collapse
    // a same-revision field→grave→field chain back into field→field.
      const movement = starts[index] ? draft : null
      if (movement) {
        movement.fromRect = starts[index]!.rect
        movement.toRect = elementRect(destination)
          ?? resolveRect(draft.to, draft.playerIndex, draft.disasterReveal ? undefined : draft.card?.instanceId)
        movement.sourceGhost = starts[index]?.ghost
        movement.fromRotation = starts[index]?.rotation ?? 0
        movement.toRotation = movementTerminalTapped(movement.to, movement.card,
          movement.publicPlacement, elementRotation(destination) === 90) ? 90 : 0
        stampMovementLandingGeometry(movement)
      }
      if (movement?.disasterReveal && movement.sourceGhost && !movement.holderWrapper) {
        if (!await waitForDecodedMovementFace(movement.sourceGhost)
          || !createMovementHolder(movement, movement.sourceGhost)) {
          reservations[index]?.cancel()
          if (reservations[index]) pendingReservations.delete(reservations[index]!)
          continue
        }
      }
      // Destination DOM only exists after the authoritative snapshot render.
      // Refreshing the already-bound owner acquires the visibility lease only
      // when this reservation is the current, unpaused visual owner.  An inline
      // choice therefore keeps the real card visible and clickable.
      if (movement?.holderWrapper && movement.visualOwner)
        movement.presentationReservation?.prepareVisualOwner(movement.visualOwner)
      if (movement && (movement.disasterReveal || !movement.sourceGhost || movement.sourceWasGenericBack)
        && !movement.concealed && movement.card && preparation) {
        movement.preparedImageUrl = await prepareMovementImage(movement.card, preparation.controller.signal)
        if (generation !== viewportGeneration || preparation.controller.signal.aborted) {
          for (const reservation of reservations.slice(index)) {
            reservation.cancel()
            pendingReservations.delete(reservation)
          }
          return
        }
      // A public face that failed back to the official card back is not a
      // successfully presented authority fact. Keep the settled authority
      // card visible and let the later result use its conservative fallback.
        if (movement.preparedImageUrl === CARD_IMAGE_PLACEHOLDER) {
          revealTarget(movement)
          disposeMovementHolder(movement)
          reservations[index]?.cancel()
          if (reservations[index]) pendingReservations.delete(reservations[index]!)
          if (movement.card.instanceId) sourceZoneHints.delete(movement.card.instanceId)
          continue
        }
        if (movement.disasterReveal && movement.sourceGhost) {
          const front = document.createElement('img')
          front.className = 'disaster-reveal-front'
          front.src = movement.preparedImageUrl
          front.alt = movement.card.name ?? ''
          movement.sourceGhost.appendChild(front)
        } else if (movement.sourceWasGenericBack && movement.sourceGhost) {
          const preparedFace = movement.sourceGhost.cloneNode(false) as HTMLElement
          preparedFace.classList.remove('card-back', 'tapped', 'selected')
          preparedFace.dataset.l12ZoneAnimationFace = 'true'
          Object.assign(preparedFace.style, {
            width: '100%', height: '100%', margin: '0', transform: 'none', transition: 'none',
            visibility: 'visible', pointerEvents: 'none',
            border: '0', outline: 'none', boxShadow: 'none', background: 'transparent',
          })
          const image = document.createElement('img')
          image.className = 'l12-card-image'
          image.src = movement.preparedImageUrl
          image.alt = movement.card.name ?? ''
          Object.assign(image.style, { width: '100%', height: '100%', objectFit: 'contain' })
          preparedFace.appendChild(image)
          if (!await waitForDecodedMovementFace(preparedFace)) {
            revealTarget(movement)
            disposeMovementHolder(movement)
            reservations[index]?.cancel()
            if (reservations[index]) pendingReservations.delete(reservations[index]!)
            continue
          }
          movement.preparedFaceGhost = preparedFace
        }
      }
      if (generation !== viewportGeneration || preparation?.controller.signal.aborted) {
        if (movement) {
          revealTarget(movement)
          disposeMovementHolder(movement)
        }
        for (const reservation of reservations.slice(index)) {
          reservation.cancel()
          pendingReservations.delete(reservation)
        }
        return
      }
      if (movement && preparation) {
        const reservation = reservations[index]!
        const release = await waitForReservationGrant(reservation, preparation.controller.signal)
        pendingReservations.delete(reservation)
        if (!release) {
          revealTarget(movement)
          disposeMovementHolder(movement)
          reservation.cancel()
          return
        }
        if (generation !== viewportGeneration) release(false)
        else {
          movement.presentationRelease = release
          queue.push(movement)
          pendingMovementHolders.delete(movement)
          showNext()
        }
      } else {
        reservations[index]?.cancel()
        if (reservations[index]) pendingReservations.delete(reservations[index]!)
      }
      if (movement?.card?.instanceId) sourceZoneHints.delete(movement.card.instanceId)
    }
  } finally {
    // Only reservations still owned by this preparation batch are pending.
    // Granted movements were removed before being handed to active/queue and
    // retain their normal presentation release callback.
    cancelPendingBatchReservations(reservations)
    finishPreparationBatch(preparation)
    showNext()
  }
}, { immediate: true })
watch(() => props.paused, paused => {
  // An animation that has already become visible must yield to a newly opened
  // modal. It is presentation only, so do not replay it after the modal closes.
  if (paused && active.value) cancelActiveMovement()
  for (const reservation of pendingReservations) reservation.setPaused(paused)
  if (!paused) showNext()
})
onBeforeUnmount(() => { reset(); emit('busyChange', false) })
</script>

<template>
  <span ref="presentationRoot" class="zone-movement-presentation-layer" data-ui-contract="zone-movement-reservation-ledger"
    :data-movement-reservation-count="reservationCount" aria-hidden="true" />
  <Teleport :to="landscapeTeleportTarget()">
    <div v-if="active && !active.sourceGhost" :key="active.key" class="zone-card-movement"
      :class="{ 'movement-ready': activeEventFaceReady }" :style="motionStyle"
      data-ui-contract="authoritative-zone-card-movement" :data-movement-key="active.key"
      :data-movement-instance-id="active.card?.instanceId" :data-movement-from="active.from" :data-movement-to="active.to"
      :data-entry-presentation="active.legionEntry ? 'legion-center-land' : undefined"
      :data-play-presentation="active.playedCard ? 'center-focus' : undefined"
      :data-entry-impact-tier="active.entryImpactTier"
      :data-movement-from-x="active.fromRect.x" :data-movement-from-y="active.fromRect.y"
      :data-movement-to-x="active.toRect.x" :data-movement-to-y="active.toRect.y"
      aria-hidden="true">
      <div class="moving-card" data-essential-motion :class="{ concealed: active.concealed, covered: active.covered,
        'disaster-reveal': active.disasterReveal, 'legion-entry': active.legionEntry, 'played-card': active.playedCard,
        'entry-simplified': active.entrySimplified }">
        <span v-if="active.legionEntry && !active.entrySimplified && active.entryImpactTier"
          class="legion-entry-impact" :data-entry-impact="active.entryImpactTier" aria-hidden="true" />
        <small v-if="active.caption" class="movement-caption">{{ active.caption }}</small>
        <template v-if="active.disasterReveal && active.preparedImageUrl">
          <span class="disaster-reveal-card">
            <img class="disaster-reveal-back" src="/assets/l12/card-back-disaster.png" alt="" />
            <img class="disaster-reveal-front" :src="active.preparedImageUrl" :alt="active.card?.name || ''" />
          </span>
        </template>
        <img v-else-if="active.concealed" :src="CARD_IMAGE_PLACEHOLDER" alt="" />
        <img v-else-if="active.preparedImageUrl" :src="active.preparedImageUrl" :alt="active.card?.name || ''" />
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.zone-movement-presentation-layer{display:none!important}
:global([data-l12-zone-animation-face="true"]::after){display:none!important}
.zone-card-movement{position:fixed;z-index:2147482988;left:0;top:0;width:0;height:0;pointer-events:none}.moving-card{position:absolute;width:72px;height:101px;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0);animation:l12-zone-card-flight var(--move-duration,.44s) var(--l12-ease-standard) both;filter:drop-shadow(0 8px 10px rgba(0,0,0,.72));will-change:transform,opacity}.moving-card>img,.moving-card :deep(.l12-card-image){width:100%;height:100%;object-fit:contain}.moving-card.concealed>img{object-fit:cover;border:1px solid #d6c488}
.zone-card-movement:not(.movement-ready) .moving-card{visibility:hidden;animation-play-state:paused}.zone-card-movement.movement-ready .moving-card{visibility:visible;animation-play-state:running}
.moving-card.covered:not(.concealed){filter:grayscale(.45) brightness(.72) drop-shadow(0 12px 14px #000)}
.moving-card.legion-entry:not(.entry-simplified){animation-name:l12-legion-entry-flight;animation-timing-function:linear}
.moving-card.legion-entry.entry-simplified{animation-name:l12-legion-entry-handoff;animation-timing-function:linear}
.moving-card.played-card:not(.legion-entry){animation-name:l12-played-card-flight;animation-timing-function:linear}
.legion-entry-impact{position:absolute;inset:-18%;z-index:-1;border:2px solid rgba(222,191,104,.9);border-radius:50%;box-shadow:0 0 16px rgba(222,191,104,.72);opacity:0;pointer-events:none;transform:scale(.45);transform-origin:center;animation:l12-legion-entry-impact var(--move-duration,.82s) linear both}
.zone-card-movement:not(.movement-ready) .legion-entry-impact{animation-play-state:paused}.zone-card-movement.movement-ready .legion-entry-impact{animation-play-state:running}
.movement-caption{position:absolute;z-index:2;left:50%;bottom:calc(100% + 7px);width:max-content;max-width:240px;transform:translateX(-50%);padding:3px 7px;border:1px solid #8cc6d2;background:rgba(7,16,20,.94);color:#eef6f5;font-size:12px;line-height:1.35;text-align:center;white-space:normal;overflow-wrap:anywhere}
.disaster-reveal-card{position:relative;display:block;width:100%;height:100%;perspective:800px;transform-style:preserve-3d}.disaster-reveal-card>img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;backface-visibility:hidden}.disaster-reveal-back{animation:l12-disaster-card-back var(--move-duration,.44s) ease-in both}.disaster-reveal-front{animation:l12-disaster-card-front var(--move-duration,.44s) ease-out both}
:global(.l12-disaster-reveal-holder .disaster-reveal-card){position:relative;display:block;width:100%!important;height:100%!important;perspective:900px;transform-style:preserve-3d}
:global(.l12-disaster-reveal-holder .disaster-reveal-card>img){position:absolute!important;inset:0;width:100%!important;height:100%!important;margin:0!important;object-fit:contain!important;backface-visibility:hidden;animation-play-state:paused!important}
:global(.l12-disaster-reveal-holder:not(.movement-ready) .disaster-reveal-back){opacity:1;transform:rotateY(0)}
:global(.l12-disaster-reveal-holder:not(.movement-ready) .disaster-reveal-front){opacity:0;transform:rotateY(-90deg)}
:global(.l12-disaster-reveal-holder.movement-ready .disaster-reveal-back){animation:l12-disaster-card-back var(--move-duration,1.04s) linear both!important}
:global(.l12-disaster-reveal-holder.movement-ready .disaster-reveal-front){animation:l12-disaster-card-front var(--move-duration,1.04s) linear both!important}
@keyframes l12-zone-card-flight{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-from-scale)) rotate(var(--move-from-rotation,0deg))}88%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-flight-contact-scale)) rotate(var(--move-to-rotation,0deg))}100%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-to-scale)) rotate(var(--move-to-rotation,0deg))}}
@keyframes l12-played-card-flight{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-from-scale)) rotate(var(--move-from-rotation,0deg))}36%,58%{opacity:1;transform:perspective(900px) translate3d(calc(var(--move-center-x) - 36px),calc(var(--move-center-y) - 50px),26px) rotateX(-6deg) scale(var(--move-center-scale)) rotate(0);filter:drop-shadow(0 20px 16px rgba(0,0,0,.62))}88%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-contact-scale)) rotate(var(--move-to-rotation,0deg));filter:drop-shadow(0 2px 3px rgba(0,0,0,.85))}100%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-to-scale)) rotate(var(--move-to-rotation,0deg))}}
@keyframes l12-legion-entry-flight{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-from-scale)) rotate(var(--move-from-rotation,0deg))}30%{opacity:1;transform:translate3d(calc(var(--move-center-x) - 36px),calc(var(--move-center-y) - 50px),0) scale(calc(var(--move-center-scale) * .96)) rotate(0)}40%,55%{opacity:1;transform:translate3d(calc(var(--move-center-x) - 36px),calc(var(--move-center-y) - 50px),0) scale(var(--move-center-scale)) rotate(0)}65%{opacity:1;transform:perspective(900px) translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px - var(--move-drop-lift)),var(--move-drop-elevation)) rotateX(-18deg) scale(var(--move-to-scale)) rotate(var(--move-to-rotation,0deg));filter:drop-shadow(0 34px 22px rgba(0,0,0,.5));animation-timing-function:cubic-bezier(.55,0,.9,.45)}82%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-impact-scale),var(--move-impact-squash)) rotate(var(--move-to-rotation,0deg))}92%,100%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-to-scale)) rotate(var(--move-to-rotation,0deg))}}
@keyframes l12-legion-entry-handoff{0%{opacity:1;transform:translate3d(calc(var(--move-from-x) - 36px),calc(var(--move-from-y) - 50px),0) scale(var(--move-from-scale)) rotate(var(--move-from-rotation,0deg))}100%{opacity:1;transform:translate3d(calc(var(--move-to-x) - 36px),calc(var(--move-to-y) - 50px),0) scale(var(--move-to-scale)) rotate(var(--move-to-rotation,0deg))}}
@keyframes l12-legion-entry-impact{0%,66%{opacity:0;transform:scale(.45)}78%{opacity:var(--entry-impact-opacity,0);transform:scale(var(--entry-impact-mid-scale,.68))}100%{opacity:0;transform:scale(var(--entry-impact-end-scale,1.08))}}
@keyframes l12-disaster-card-back{0%,42%{opacity:1;transform:rotateY(0)}58%,100%{opacity:0;transform:rotateY(90deg)}}
@keyframes l12-disaster-card-front{0%,42%{opacity:0;transform:rotateY(-90deg)}58%,100%{opacity:1;transform:rotateY(0)}}
@media(max-width:700px){.moving-card{width:56px;height:79px}.moving-card small{bottom:-18px;font-size:var(--l12-board-copy,13px)}}
.zone-card-movement{z-index:902}
</style>
