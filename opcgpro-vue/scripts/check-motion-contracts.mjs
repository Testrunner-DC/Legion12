import fs from 'node:fs'
import path from 'node:path'

const root = process.cwd()
const read = file => fs.readFileSync(path.join(root, file), 'utf8')
const motion = read('src/l12/motion.css')
const main = read('src/main.ts')
const app = read('src/App.vue')
const hand = read('src/l12/game/HandArea.vue')
const movement = read('src/l12/game/ZoneMovementPresentationLayer.vue')
const combat = read('src/l12/game/CombatMotionPresentationLayer.vue')
const stateTransition = read('src/l12/game/CardStateTransitionLayer.vue')
const visualProjection = read('src/l12/game/visualTransitionProjection.ts')
const mobileViewport = read('src/l12/mobileViewport.ts')
const board = read('src/l12/game/GameBoard.vue')
const tile = read('src/l12/CardTile.vue')

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
  ['attack hit pause and impact', combat.includes('offset: .58') && combat.includes('const impact = impactElement.animate')],
  ['site and battle modal language', motion.includes('.site-modal-mask > .site-modal') && motion.includes('.l12-prompt-overlay > .prompt-panel')],
  ['ready and rest snapshot handoff', board.includes('<CardStateTransitionLayer') && stateTransition.includes("flush: 'pre', immediate: true")
    && stateTransition.includes('const sourceGhost = source.cloneNode(true)') && stateTransition.includes('sourceGhost.style.visibility = \'visible\'')
    && stateTransition.includes('sourceRect: { left: sourceRect.left') && stateTransition.indexOf('wrapper.appendChild(ghost)') < stateTransition.indexOf("target.style.visibility = 'hidden'")],
  ['ready and rest never clone hidden target state', !stateTransition.includes('source: HTMLElement')
    && stateTransition.indexOf('revealTarget()', stateTransition.indexOf('function finish()'))
      < stateTransition.indexOf('wrapper?.remove()', stateTransition.indexOf('function finish()'))],
  ['ready and rest use revision-scoped authority transactions', board.includes(':revision="game.revision"')
    && stateTransition.includes('claimCardStateTransitions(stateClaims, revision, next)')
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
    && stateTransition.includes('landscapeTeleportElement()?.appendChild(wrapper)')
    && movement.includes('landscapeTeleportElement()?.appendChild(wrapper)')
    && combat.includes('landscapeTeleportElement()?.appendChild(wrapper)')],
  ['visual viewport jitter preserves claimed motion', !movement.includes("addEventListener('l12-viewport-change'")
    && combat.includes('function viewportChanged()')
    && !combat.slice(combat.indexOf('function viewportChanged()'), combat.indexOf('onMounted(', combat.indexOf('function viewportChanged()'))).includes('reset()')],
  ['rested card geometry is normalized before ghost rotation', movement.includes("const quarterTurn = element.classList.contains('tapped')")
    && movement.includes('width: quarterTurn ? rect.height : rect.width')
    && movement.includes("transformOrigin: 'center'")
    && combat.includes('function cardSnapshotRect(element: HTMLElement)')
    && combat.includes('rect: cardSnapshotRect(element)')],
  ['combat defender defeat resolves the opposite graveyard without owner metadata', combat.includes("event.type === 'combat' && index === 1")
    && combat.includes('return 1 - event.playerIndex')
    && combat.includes('const owner = defeatOwner(event, captured.card, index)')],
  ['combat impact preserves authority card rotation', combat.includes("targetCard?.closest('.formation-slot') ?? targetAnchor")
    && !combat.includes('const impact = targetElement.animate')],
  ['attachment target uses stable instance identity', tile.includes('data-attached-card-instance-ids') && movement.includes('attachmentElement') && movement.includes('draft.attachment && !destination')],
  ['private-zone source hints survive prompt removal', movement.includes('sourceZoneHints') && movement.includes('collectPromptSourceZoneHints') && visualProjection.includes("key.endsWith(':zone')")],
  ['effect card art includes authority source only', board.includes('isCardEffectPresentationEvent(event)') && board.includes('presentationCards(event)') && visualProjection.includes('event.effectSceneId') && visualProjection.includes('event.cards?.slice(0, 1)')],
  ['blocking prompt cancels only active presentation', stateTransition.includes('if (paused && active.value) cancelActive()') && movement.includes('if (paused && active.value) cancelActiveMovement()')],
]

const failures = checks.filter(([, ok]) => !ok)
if (failures.length) {
  for (const [name] of failures) console.error(`FAIL ${name}`)
  process.exit(1)
}
console.log(`L12 motion contracts passed: ${checks.length}/${checks.length}`)
