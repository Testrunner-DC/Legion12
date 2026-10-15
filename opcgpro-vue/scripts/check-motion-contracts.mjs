import fs from 'node:fs'
import path from 'node:path'

const root = process.cwd()
const read = file => fs.readFileSync(path.join(root, file), 'utf8').replace(/\r\n?/g, '\n')
const motion = read('src/l12/motion.css')
const main = read('src/main.ts')
const app = read('src/App.vue')
const hand = read('src/l12/game/HandArea.vue')
const movement = read('src/l12/game/ZoneMovementPresentationLayer.vue')
const combat = read('src/l12/game/CombatMotionPresentationLayer.vue')
const stateTransition = read('src/l12/game/CardStateTransitionLayer.vue')
const visibilityLease = read('src/l12/game/authoritativeCardVisibility.ts')
const sharedPixelCapture = read('src/l12/game/authoritativeCardVisualSnapshot.ts')
const visualProjection = read('src/l12/game/visualTransitionProjection.ts')
const coordinator = read('src/l12/game/presentationSequenceCoordinator.ts')
const mobileViewport = read('src/l12/mobileViewport.ts')
const board = read('src/l12/game/GameBoard.vue')
const tile = read('src/l12/CardTile.vue')
const between = (source, start, end) => {
  const first = source.indexOf(start)
  const last = source.indexOf(end, first + start.length)
  return first >= 0 && last > first ? source.slice(first, last) : ''
}
const statePreparation = between(stateTransition, 'function createPreparedTransition(', '\nfunction beginTransition(')
const statePixelCapture = sharedPixelCapture
const stateFinalization = between(stateTransition, 'function finalizeActive(', '\nfunction cancelActive(')
const zonePreparation = between(movement, 'function createMovementHolder(', '\nfunction showNext(')
const zoneTargetCover = between(movement, 'function coverTarget(', '\nfunction disposeMovementHolder(')
const stateTargetCover = between(stateTransition, 'function coverTransitionTarget(', '\nlet wrapper:')
const cloneStyleIsVisible = source => /Object\.assign\(sourceGhost\.style,\s*\{[^}]*visibility:\s*'visible'/.test(source)
const stateLayerMarkup = board.slice(board.indexOf('<CardStateTransitionLayer'),
  board.indexOf('/>', board.indexOf('<CardStateTransitionLayer')) + 2)
const zoneLayerMarkup = board.slice(board.indexOf('<ZoneMovementPresentationLayer'),
  board.indexOf('/>', board.indexOf('<ZoneMovementPresentationLayer')) + 2)

const checks = [
  ['motion tokens imported once', main.includes("import './l12/motion.css'")],
  ['five duration tokens', [1, 2, 3, 4, 5].every(n => motion.includes(`--l12-dur-${n}:`))],
  ['preference scale consumed', motion.includes('var(--l12-animation-scale, 1)')],
  ['reduced motion fallback', motion.includes('prefers-reduced-motion: reduce')],
  ['dead motion duration removed', !app.includes('--l12-motion-duration')],
  ['immersive and site route transitions', app.includes('name="page-fade"') && app.includes('name="page-slide"')],
  ['hand FLIP anchors', hand.includes('useFlip') && hand.includes('data-flip-id')],
  ['public hand-add presentation preserved', movement.includes('publicHandAddCaption') && movement.includes("event.type === 'reveal' && /加入手牌/")],
  ['zone flight arc and settle', movement.includes('const lift =') && movement.includes('offset: .85')],
  ['attack lunge and rest share one visible transaction', statePreparation.includes("holderWrapper.dataset.motionKind = transition.attackSequence === undefined ? 'state' : 'attack-rest'")
    && stateTransition.includes('offset: .38') && stateTransition.includes('offset: .56')
    && !combat.includes("event.type === 'attack' &&") && !combat.includes("event.type === 'attack') animateAttack")],
  ['authoritative state layer solely owns ready and rest motion', !motion.includes('l12-rest-settle')
    && !motion.includes('.formation-slot .card-tile.tapped')
    && stateLayerMarkup.includes(':paused="modalPresentationPaused"')
    && !stateLayerMarkup.includes(':paused="passivePresentationPaused"')],
  ['authoritative zone movement cannot replay after a blocking prompt',
    zoneLayerMarkup.includes(':paused="modalPresentationPaused"')
    && !zoneLayerMarkup.includes(':paused="passivePresentationPaused"')],
  ['authority boundaries invalidate pending public presentations before late grants',
    board.includes('function resetCardPresentations()')
    && board.includes('presentationEpoch++')
    && board.includes('if (epoch !== presentationEpoch) {\n    release()\n    return\n  }')
    && board.includes('publicRevealWaitingReservation?.cancel()')
    && board.includes('publicRevealWaiting = false')
    && board.includes('publicRevealQueue.length = 0')
    && board.includes('diceRevealQueue.length = 0')
    && board.includes('cardPresentationCoordinator.reset()')
    && board.includes('lastPublicRevealSequence.value = baseline')
    && board.includes('lastHiddenRevealSequence.value = baseline')
    && board.includes('lastDiceSequence.value = baseline')],
  ['site and battle modal language', motion.includes('.site-modal-mask > .site-modal') && motion.includes('.l12-prompt-overlay > .prompt-panel')],
  ['ready and rest snapshot handoff', board.includes('<CardStateTransitionLayer') && stateTransition.includes("flush: 'pre', immediate: true")
    && stateTransition.includes('const sourceGhost = source.cloneNode(true)') && stateTransition.includes('sourceGhost.style.visibility = \'visible\'')
    && stateTransition.includes('sourceRect: { left: sourceRect.left') && stateTransition.includes('acquireAuthoritativeCardVisibility(target)')],
  ['zone movement clones never inherit a temporary hidden authority lease',
    cloneStyleIsVisible(zonePreparation)
    && zonePreparation.includes("sourceGhost.classList.remove('tapped', 'selected')")
    && zonePreparation.indexOf("visibility: 'visible'") < zonePreparation.indexOf('wrapper.appendChild(sourceGhost)')
    && zonePreparation.includes("visibility: 'hidden'")
    && movement.includes('const sourceGhost = publicHolder instanceof HTMLElement')
    && movement.includes('usesGraveyardAnchor ? genericPublicSourceGhost(draft.from) : publicHolder.cloneNode(true)')
    && movement.includes('publicHolder instanceof HTMLElement && !usesGraveyardAnchor')
    && movement.includes('snapshotDecodedAuthoritativeCardVisual(publicHolder, sourceGhost)')
    && movement.indexOf('snapshotDecodedAuthoritativeCardVisual(publicHolder, sourceGhost)')
      < movement.indexOf('if (start.ghost) createMovementHolder(draft, start.ghost)')],
  ['ready and rest never clone hidden target state', !stateTransition.includes('source: HTMLElement')
    && stateTransition.includes("sourceGhost.style.visibility = 'visible'")
    && cloneStyleIsVisible(statePreparation)
    && statePreparation.includes('revealTransitionTarget(prepared)')
    && statePreparation.indexOf('revealTransitionTarget(prepared)', statePreparation.indexOf('dispose:'))
      < statePreparation.indexOf('holderWrapper.remove()', statePreparation.indexOf('dispose:'))
    && stateFinalization.indexOf('settleElementGeometry(target)') < stateFinalization.indexOf('transition?.presentationRelease?.(')],
  ['ready and rest use revision-scoped authority transactions', board.includes(':revision="game.revision"')
    && board.includes(':events="game.recentEvents ?? []"')
    && stateTransition.includes('claimCardStateTransitions(stateClaims, revision, next, props.events)')
    && visualProjection.includes('if (revision <= state.revision) return []')
    && visualProjection.includes('transactionKey: `${revision}:${change.instanceId}')],
  ['state observer is layout neutral', stateTransition.includes('.card-state-transition-layer{display:none!important}') && board.includes('.felt-board :deep(.battlefield-half.my-half){grid-row:3}')],
  ['ready and rest use global timing language', stateTransition.includes('l12AnimationDuration') && stateTransition.includes("cubic-bezier(.22,1,.36,1)") && stateTransition.includes('prefers-reduced-motion: reduce')],
  ['multi-card movement stays per instance', movement.includes('movementCardsForEvent(event)') && visualProjection.includes("event.type === 'move' || event.type === 'attach'")],
  ['cross-source movement descriptions share one authority transaction', movement.includes('beginMovementTransactionBatch(movementClaims, revision, authoritativeZones)')
    && movement.includes('claimMovementTransaction(movementClaims, transactionBatch')
    && movement.includes('finalizeMovementTransactionBatch(movementClaims, transactionBatch, authoritativeZones)')
    && movement.includes('card, transactionBatch.cursor')
    && visualProjection.includes('cursor: new Map(state.zones)')
    && visualProjection.includes('`${revision}:${fact.instanceId}:${fact.from}>${fact.to}`')
    && visualProjection.includes('current !== undefined && current !== fact.from')],
  ['movement geometry only reuses a card in its semantic zone', movement.includes('function cardElementInZone(instanceId: string | undefined, zone: Zone)')
    && movement.includes("element.closest('[data-l12-zone]')?.getAttribute('data-l12-zone') === zone")
    && movement.includes('cardElementInZone(movement.card?.instanceId, movement.to)')
    && movement.includes('cardElementInZone(draft.card?.instanceId, draft.from)')],
  ['imperative motion shares the logical mobile canvas', mobileViewport.includes('export function landscapeTeleportElement()')
    && statePreparation.includes('const host = landscapeTeleportElement()')
    && statePreparation.includes('host.appendChild(holderWrapper)')
    && zonePreparation.includes('const host = landscapeTeleportElement()')
    && zonePreparation.includes('host.appendChild(wrapper)')
    && combat.includes('landscapeTeleportElement()?.appendChild(wrapper)')],
  ['visual viewport jitter preserves claimed motion', !movement.includes("addEventListener('l12-viewport-change'")
    && combat.includes('function viewportChanged()')
    && !combat.slice(combat.indexOf('function viewportChanged()'), combat.indexOf('onMounted(', combat.indexOf('function viewportChanged()'))).includes('reset()')],
  ['card geometry uses stable layout size instead of transient transformed bounds', mobileViewport.includes('export function viewportLayoutRect(element: HTMLElement)')
    && mobileViewport.includes('const width = element.offsetWidth * scaleX')
    && movement.includes('const rect = viewportLayoutRect(element)')
    && movement.includes("transformOrigin: 'center'")
    && combat.includes('function cardSnapshotRect(element: HTMLElement)')
    && combat.includes('return viewportLayoutRect(element)')
    && stateTransition.includes('const sourceRect = viewportLayoutRect(source)')],
  ['covered authority cards finish CSS motion before ghost handoff', mobileViewport.includes('export function settleElementGeometry(element: HTMLElement)')
    && mobileViewport.includes('element.getAnimations()')
    && stateTransition.includes('settleElementGeometry(target)')
    && movement.includes('settleElementGeometry(destination)')],
  ['combat defender defeat resolves the opposite graveyard without owner metadata', combat.includes("event.type === 'combat' && index === 1")
    && combat.includes('return 1 - event.playerIndex')
    && combat.includes('const owner = defeatOwner(event, captured.card, index)')],
  ['combat layer cannot replay a second ordinary attack animation', !combat.includes('function animateAttack(')
    && combat.includes('state layer owns a normal attack as one combined lunge/rest')],
  ['attachment target uses stable instance identity', tile.includes('data-attached-card-instance-ids') && movement.includes('attachmentElement') && movement.includes('draft.attachment && !destination')],
  ['private-zone source hints survive prompt removal', movement.includes('sourceZoneHints') && movement.includes('collectPromptSourceZoneHints') && visualProjection.includes("key.endsWith(':zone')")],
  ['effect card art includes authority source only', board.includes('isCardEffectPresentationEvent(event)') && board.includes('presentationCards(event)') && visualProjection.includes('event.effectSceneId') && visualProjection.includes('event.cards?.slice(0, 1)')],
  ['overlapping presentation layers share one visibility lease', movement.includes("from './authoritativeCardVisibility'")
    && stateTransition.includes("from './authoritativeCardVisibility'")
    && movement.includes('coverTarget(movement, destination)')
    && zoneTargetCover.includes('acquireAuthoritativeCardVisibility(target)')
    && stateTransition.includes('acquireAuthoritativeCardVisibility(target)')
    && !movement.includes('hiddenTargetVisibility') && !stateTransition.includes('hiddenTargetVisibility')
    && visibilityLease.includes('new WeakMap<HTMLElement, VisibilityLeaseState>()')
    && visibilityLease.includes('if (current.count > 0) return')
    && stateTransition.includes('function finalizeActive(generation: number, advance: boolean)')
    && stateTransition.includes('generation !== activeGeneration')
    && stateTransition.includes('animation.oncancel = () => finalize(false)')
    && stateTransition.includes('settled.then(() => finalize(true), () => finalize(false))')
    && movement.includes('activeGhostAnimation.oncancel = finish')
    && movement.includes('activeGhostAnimation.oncancel = null')
    && movement.includes('timer = setTimeout(finish')],
  ['entry effects continue one stable movement transaction without a second full card',
    visualProjection.includes('playerLogGroupId') && visualProjection.includes("timing !== 'enter' && timing !== 'promotion-enter'")
    && visualProjection.includes("source.cardType !== 'legion'")
    && visualProjection.includes("sourceEvidence === 'fallback'")
    && visualProjection.includes("sourceEvidence === 'play-contract'")
    && visualProjection.includes("event.playerLogTiming !== 'enter'")
    && visualProjection.includes("from !== 'hand' && from !== 'resolving'")
    && movement.includes('draft.sourceEvidence')
    && movement.includes("event.type === 'play'")
    && movement.includes('draft.entryTransactionKey = entryTransactionKey ?? undefined')
    && movement.includes('registerEntryMovementTransaction(movement.entryTransactionKey)')
    && movement.includes('registerStartedPresentation(movement)')
    && movement.indexOf('activeGhostAnimation = wrapper.animate') < movement.indexOf('registerStartedPresentation(movement)')
    && board.includes('cardPresentationCoordinator.ownsEntryMovementTransaction')
    && board.includes('cardPresentationCoordinator.ownsPresentationFact')
    && board.includes('const continuation = Boolean(entryTransactionKey || presentationFacts)')
    && board.includes('cards: continuation ? [] : next.cards')
    && board.includes("presentationFacts ? 'fact-continuation' : entryTransactionKey ? 'entry-continuation' : 'full-card'")],
  ['blocking prompt cancels only started presentation while preparation retains sequence', stateTransition.includes('if (paused && active.value && animation) cancelActive()')
    && stateTransition.includes('await waitUntilUnpaused(preparation.signal)')
    && movement.includes('if (paused && active.value) cancelActiveMovement()')],
  ['state transition only grants decoded visual ownership and registers facts after actual WAAPI starts',
    stateTransition.includes('function waitForDecodedStateImage(image: HTMLImageElement, signal: AbortSignal, timeoutMs = 1800)')
    && statePreparation.includes('prepared.visualReady = waitForDecodedStateFace(holderWrapper, visualPreparation.signal)')
    && statePreparation.includes('if (!decoded || visualPreparation.signal.aborted)')
    && statePreparation.indexOf('if (!decoded || visualPreparation.signal.aborted)')
      < statePreparation.lastIndexOf('prepared.presentationReservation.prepareVisualOwner(visualOwner)')
    && stateTransition.includes('const sourcePixelsReady = snapshotDecodedAuthoritativeCardVisual(source, sourceGhost)')
    && stateTransition.includes("from './authoritativeCardVisualSnapshot'")
    && movement.includes("from './authoritativeCardVisualSnapshot'")
    && statePreparation.includes('if (sourcePixelsReady) {')
    && statePreparation.includes('prepared.visualReady = Promise.resolve(true)')
    && statePixelCapture.includes('!source.complete || source.naturalWidth <= 0 || source.naturalHeight <= 0')
    && statePixelCapture.includes('const layoutWidth = source.offsetWidth')
    && statePixelCapture.includes('const layoutHeight = source.offsetHeight')
    && statePixelCapture.includes('context.drawImage(source, x, y, drawWidth, drawHeight)')
    && statePixelCapture.includes('if (captures.some(capture => !capture)) return false')
    && statePixelCapture.includes('capture!.clone.replaceWith(capture!.canvas)')
    && stateTransition.includes('if (!await transition.visualReady)')
    && stateTransition.indexOf('if (!await transition.visualReady)') < stateTransition.indexOf('animation = wrapper.animate(frames')
    && stateTransition.includes('if (!wrapper.isConnected || !stateFaceIsDecoded(wrapper)) { finalize(true); return }')
    && stateTransition.indexOf('animation = wrapper.animate(frames')
      < stateTransition.indexOf('props.sequenceCoordinator.registerPresentationFact(transition.presentationFactSequence)')
    && stateTransition.includes('activePreparation?.abort()')
    && stateTransition.includes('preparation?.abort()')],
  ['zone movement never hides authority before its exact face is decoded',
    movement.includes('waitForDecodedMovementFace(root)')
    && movement.includes(".zone-card-movement:not(.movement-ready) .moving-card{visibility:hidden;animation-play-state:paused}")
    && movement.includes('viewportGeneration === presentationGeneration')
    && movement.includes('activePreparationToken === preparationToken')
    && movement.includes('if (movement.preparedImageUrl === CARD_IMAGE_PLACEHOLDER)')
    && movement.includes('reservations[index]?.cancel()')
    && movement.includes('if (!root || !await waitForDecodedMovementFace(root) || !ownsActivePresentation())')
    && movement.indexOf('if (!coverDestination())', movement.indexOf('if (!root || !await waitForDecodedMovementFace(root)'))
      < movement.indexOf('activeEventFaceReady.value = true', movement.indexOf('if (!root || !await waitForDecodedMovementFace(root)'))
    && movement.indexOf('activeEventFaceReady.value = true', movement.indexOf('if (!root || !await waitForDecodedMovementFace(root)'))
      < movement.indexOf('registerStartedPresentation(active.value)', movement.indexOf('if (!root || !await waitForDecodedMovementFace(root)'))
    && movement.includes('activePreparationToken++\n  if (timer) clearTimeout(timer)')],
  ['zone movement preparation is bounded and generation-cancellable without leaking busy state',
    movement.includes('const pendingPreparationBatches = new Set<PreparationBatch>()')
    && movement.includes('function cancelPreparationBatches()')
    && movement.includes('pendingPreparationBatches.clear()\n  preparationCount = 0')
    && movement.includes("resolveCardAssetUrls(card.cardId, card.imageUrl, 'board'), controller.signal, 1800")
    && movement.includes('function waitForImage(url: string, signal: AbortSignal, timeoutMs = 1800)')
    && movement.includes('const overallTimeout = setTimeout(cancel, 4200)')
    && movement.includes('if (controller.signal.aborted || parentSignal.aborted) return CARD_IMAGE_PLACEHOLDER')
    && movement.indexOf('if (controller.signal.aborted || parentSignal.aborted) return CARD_IMAGE_PLACEHOLDER')
      < movement.indexOf('preparedImageUrls.set(key, url)')
    && movement.includes('function waitForReservationGrant(reservation: PresentationReservation, signal: AbortSignal)')
    && movement.includes('if (parentSignal.aborted) return CARD_IMAGE_PLACEHOLDER')
    && movement.includes('function cancelPendingBatchReservations(reservations: PresentationReservation[])')
    && movement.includes('cancelPendingBatchReservations(reservations)\n    finishPreparationBatch(preparation)')
    && movement.includes('if (!batch || !pendingPreparationBatches.delete(batch)) return')
    && movement.includes('cancelPreparationBatches()\n  cancelActiveMovement()')],
  ['presentation fact ledger resets on match, recovery, and backward replay boundaries',
    board.includes('matchId !== presentationBoundaryMatchId || synchronizing')
    && board.includes('revision < presentationBoundaryRevision')
    && board.includes('if (resetFacts) {\n    resetCardPresentations()')
    && board.includes('cardPresentationCoordinator.reset()')
    && coordinator.includes('presentationFacts.clear()')
    && coordinator.includes('entryMovementTransactions.clear()')],
  ['legacy state changes share ordering without treating a watermark as an explicit fact',
    stateTransition.includes('props.sequenceCoordinator.reserve(change.orderingSequence, 30,')
    && stateTransition.includes('{ visualKey: instanceId }')
    && visualProjection.includes('orderingSequence: event.sequence')
    && visualProjection.includes('freshEvents.at(-1)?.sequence ?? state.lastEventSequence')
    && visualProjection.includes('presentationFactSequence: event.sequence')
    && !stateTransition.includes('if (!transition.presentationReservation)')],
  ['visual successor inherits its lease before predecessor disposal and no motion is coalesced',
    coordinator.includes('activeVisualOwners') && coordinator.includes('settleReleasedVisualOwner')
    && coordinator.indexOf('owner.show()') < coordinator.indexOf('previous.owner.dispose()')
    && coordinator.includes('current.item.released') && coordinator.includes('owner.dispose()')
    && !coordinator.includes('presentedVisualTransactions') && !coordinator.includes('motionRequired')],
  ['only actually mounted prompts block spectator card presentations',
    board.includes("const promptOverlayMounted = computed(() => !props.readOnly || props.game.phase === 'DisasterPreparation')")
    && board.includes('const visibleBoardPromptIds = computed(() => !props.readOnly && !boardControlMinimized.value')
    && board.includes('visibleBoardPromptId.value') && board.includes('|| visiblePromptModal.value')
    && board.includes('<PromptOverlay v-if="promptOverlayMounted"')
    && board.includes(':suppressed-prompt-ids="activeBoardPromptIds"')
    && board.includes('const inlinePromptIds = new Set(activeBoardPromptIds.value)')
    && board.includes('!inlinePromptIds.has(item.promptId)')
    && board.includes('&& routedPromptModal.value && !promptMinimized.value')],
  ['replacement DOM is covered before the prior target lease is released',
    [zoneTargetCover, stateTargetCover].every(source => source.includes('coveredTarget === target')
      && source.includes('const releaseNext = acquireAuthoritativeCardVisibility(target)')
      && source.includes('const releasePrevious =') && source.includes('releasePrevious?.()')
      && source.indexOf('acquireAuthoritativeCardVisibility(target)') < source.indexOf('releasePrevious?.()'))],
  ['movement terminal posture belongs to its action and discarded successors cannot retain owners',
    movement.includes('movement.toRotation = movementTerminalTapped(movement.to, movement.card,')
    && visualProjection.includes('publicPlacement?.instanceId === eventCard.instanceId')
    && visualProjection.includes('return placementTapped ?? eventCard?.tapped ?? destinationTapped')
    && coordinator.includes('!item.cancelled && !item.released && item.visualKey === visualKey')],
  ['match-only changes establish a coherent baseline before the next real action',
    [stateTransition, movement].every(source => source.includes('let awaitingMatchBaseline = true')
      && source.includes("props.events.map(event => event.sequence).join(','), props.matchId] as const")
      && source.includes('if (awaitingMatchBaseline) {\n    awaitingMatchBaseline = false')
      && source.indexOf('if (awaitingMatchBaseline) {') < source.indexOf('if (synchronizing ||'))
    && stateTransition.includes('Cancel immediately; baseline only after the complete prop tuple arrives.')
    && movement.includes('Rebase once from the coherent pre-render tuple.')],
]

const failures = checks.filter(([, ok]) => !ok)
if (failures.length) {
  for (const [name] of failures) console.error(`FAIL ${name}`)
  process.exit(1)
}
console.log(`L12 motion contracts passed: ${checks.length}/${checks.length}`)
