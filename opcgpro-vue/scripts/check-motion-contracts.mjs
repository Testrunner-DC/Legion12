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
]

const failures = checks.filter(([, ok]) => !ok)
if (failures.length) {
  for (const [name] of failures) console.error(`FAIL ${name}`)
  process.exit(1)
}
console.log(`L12 motion contracts passed: ${checks.length}/${checks.length}`)
