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
  ['attack hit pause and impact', combat.includes('offset: .58') && combat.includes('const impact = targetElement.animate')],
  ['site and battle modal language', motion.includes('.site-modal-mask > .site-modal') && motion.includes('.l12-prompt-overlay > .prompt-panel')],
  ['ready and rest snapshot handoff', board.includes('<CardStateTransitionLayer') && stateTransition.includes("flush: 'pre', immediate: true") && stateTransition.includes('hiddenTarget.style.visibility')],
  ['state observer is layout neutral', stateTransition.includes('.card-state-transition-layer{display:none!important}') && board.includes('.felt-board :deep(.battlefield-half.my-half){grid-row:3}')],
  ['ready and rest use global timing language', stateTransition.includes('l12AnimationDuration') && stateTransition.includes("cubic-bezier(.22,1,.36,1)") && stateTransition.includes('prefers-reduced-motion: reduce')],
  ['multi-card movement stays per instance', movement.includes('movementCardsForEvent(event)') && visualProjection.includes("event.type === 'move' || event.type === 'attach'")],
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
