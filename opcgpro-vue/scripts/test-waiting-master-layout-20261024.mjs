import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')

const board = read('src/l12/game/GameBoard.vue')
const overlay = read('src/l12/game/PromptOverlay.vue')
const playerMat = read('src/l12/game/PlayerMat.vue')
const mobileViewport = read('src/l12/mobileViewport.css')
const motion = read('src/l12/motion.css')
const browserVerify = read('scripts/verify-waiting-master-layout-20261024.mjs')

assert.match(board, /const passiveWaitTarget = ref<HTMLElement \| null>\(null\)/,
  'the board must own the passive-wait anchor')
assert.match(board, /ref="passiveWaitTarget" class="passive-wait-slot"/,
  'the passive-wait anchor must live inside the felt board')
assert.match(board, /:passive-wait-target="passiveWaitTarget"/,
  'PromptOverlay must receive the board-local passive-wait anchor')
assert.match(board, /\.passive-wait-slot\{[^}]*right:8px;[^}]*top:50%;[^}]*pointer-events:none/,
  'the passive-wait anchor must occupy the right centre safe gap without taking input')

assert.match(overlay, /passiveWaitTarget\?: HTMLElement \| null/,
  'PromptOverlay must accept the board-local passive-wait target')
assert.match(overlay, /const passiveWaiting = computed\(/,
  'pure waiting must be classified separately from actionable prompts')
assert.match(overlay, /!prompt\.value[^\n]*!isDisasterPreparation\.value[^\n]*!isMulliganPhase\.value/,
  'actionable prompts, disaster preparation, and mulligan must not enter passive waiting')
assert.match(overlay, /'passive-waiting': passiveWaiting/,
  'pure waiting must receive its own non-modal presentation class')
assert.match(overlay, /\.l12-prompt-overlay\.passive-waiting\{[^}]*background:transparent!important;[^}]*backdrop-filter:none;[^}]*pointer-events:none/,
  'pure waiting must have no mask, blur, or input interception')
assert.match(overlay, /v-if="!passiveWaiting" class="prompt-minimize"/,
  'a passive status card must not expose modal minimise controls')
assert.match(overlay, /<section v-if="minimized && !passiveWaiting" class="prompt-minimized-bar" role="status">/,
  'an actionable minimised prompt must retain its single expand bar; pure waiting is not minimised')
assert(overlay.includes('<button :aria-label="`展开：${overlayTitle}`"'),
  'the existing actionable expand control must retain its accessible context')
assert.match(overlay, /class="prompt-panel waiting-panel" :class="\{ 'passive-wait-panel': passiveWaiting \}"/,
  'the pure wait card must expose a named non-modal panel class')

assert.match(mobileViewport, /\.l12-prompt-overlay:not\(\.minimized\):not\(\.passive-waiting\)/,
  'viewport-sized prompt overlays must explicitly exclude pure waiting')
assert.match(mobileViewport, /\.prompt-panel:not\(\.passive-wait-panel\)/,
  'shared mobile dialog geometry must explicitly exclude the pure wait card')
assert.match(mobileViewport, /\.waiting-panel:not\(\.passive-wait-panel\)/,
  'ordinary waiting dialogs must keep their shared mobile dialog geometry')
assert.match(motion, /\.l12-prompt-overlay:not\(\.passive-waiting\) > \.prompt-panel/,
  'pure waiting must be excluded from the shared modal entrance motion')
assert.match(motion, /\.mobile-safe-overlay:not\(\.passive-waiting\) > :not\(button\)/,
  'the mobile modal entrance branch must also exclude pure waiting')
assert(!motion.includes('.l12-prompt-overlay > .prompt-panel'),
  'a broad secondary prompt selector must not restore modal motion to pure waiting')
assert(!motion.includes('.mobile-safe-overlay > :not(button)'),
  'a broad secondary mobile selector must not restore modal motion to pure waiting')

assert.doesNotMatch(playerMat, /主宰已登场|master-away/,
  'a deployed master slot must not show an away/deployed message')
assert.match(playerMat, /v-if="!player\.master\.deployedAsLegion" class="master-art"/,
  'the master art must remain a separately transformable layer')
assert.match(playerMat, /\.master-column \.mini-master\.tapped\{transform:none!important\}/,
  'resting a master must not rotate the name and health container')
assert.match(playerMat, /\.mini-master\.tapped :deep\(\.master-art\)\{[^}]*width:calc\(100cqw \* 5 \/ 7\);[^}]*height:100cqw;[^}]*transform:rotate\(90deg\)/,
  'only the art may rotate, using the same contained geometry principle as relics')
assert.doesNotMatch(playerMat, /\.master-art\)\{[^}]*transition:transform/,
  'master art must not pass through an uncontained intermediate rotation')

assert.match(browserVerify, /const scope = process\.env\.L12_QA_SCOPE/,
  'browser verification must require an explicit execution scope')
assert.match(browserVerify, /assert\(\['full', 'geometry'\]\.includes\(scope\)/,
  'browser verification must reject empty or unknown scopes')

console.log('waiting/master layout source contract passed')
