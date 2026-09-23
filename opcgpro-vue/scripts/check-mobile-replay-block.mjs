import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const read = path => readFile(resolve(root, path), 'utf8')
const [viewport, battleLayout, board, records, replay, prompt, hand, tile] = await Promise.all([
  read('src/l12/mobileViewport.ts'),
  read('src/l12/game/battleViewportLayout.ts'),
  read('src/l12/game/GameBoard.vue'),
  read('src/l12/MatchRecords.vue'),
  read('src/l12/ReplayPage.vue'),
  read('src/l12/game/PromptOverlay.vue'),
  read('src/l12/game/HandArea.vue'),
  read('src/l12/CardTile.vue'),
])
const expect = (condition, message) => {
  if (!condition) throw new Error(`mobile replay / board contract: ${message}`)
}

expect(viewport.includes('export function resolveViewportMode('), 'viewport classification must have a deterministic shared entry')
expect(viewport.includes('export function isMobileViewportExperience()'), 'mobile eligibility must read the shared viewport mode')
expect(!viewport.includes('(pointer: coarse)') && !viewport.includes('screen.orientation'), 'viewport classification must not depend on device identity or physical orientation locks')
expect(battleLayout.includes('const mobile = isMobileViewportExperience()') && battleLayout.includes('mobileLandscapeViewport.value = resolved.mobile'), 'GameBoard must follow the current logical viewport mode through the shared battle layout boundary')
expect(!board.includes('viewport.height >= 300 && viewport.height <= 430'), 'browser chrome height must not switch the board mode')
for (const handler of ['selectHandFor', 'selectPublicCardFor', 'inspectActiveDisaster']) {
  const match = board.match(new RegExp(`function ${handler}[\\s\\S]*?\\n}`, 'm'))?.[0] ?? ''
  expect(!match.includes('mobileInspectorOpen.value = true'), `${handler} must not auto-open card details`)
}
expect(board.includes('class="mobile-card-inspector-handle mobile-card-inspector-handle-global"') && board.includes("CardDetailContent :card=\"focusDetailCard\""), 'card details must use the global persistent handle and CardDetailContent')
expect(board.includes(':mobile-layout="mobileLandscapeViewport"'), 'mobile-safe modal layout must be passed to board overlays')
expect(prompt.includes("'mobile-safe-overlay': mobileLayout"), 'prompt overlay must receive the mobile safe rectangle')
expect(records.includes("error.value = '请到电脑端查看回放'"), 'record actions must state the desktop-only replay notice')
expect(records.includes('class="mobile-replay-inline"') && !records.includes('role="alertdialog"'), 'mobile replay actions must use an inline summary notice instead of a blocking modal')
expect(records.includes('function openReplayImport()') && records.includes('if (mobileReplayBlocked) return blockMobileReplay()'), 'JSON replay import must be blocked before opening a picker')
expect(replay.includes('const mobileReplayBlocked = isMobileDeviceExperience()'), 'direct replay route must detect mobile before loading')
expect(replay.includes('if (!mobileReplayBlocked) void loadReplay()'), 'direct replay route must not request data on mobile')
expect(replay.includes('v-if="mobileReplayBlocked" class="replay-mobile-blocked"') && replay.includes('请到电脑端查看回放'), 'direct replay route must render the blocker page')
expect(!hand.includes('mobileLandscapeViewport') && !tile.includes('mobileLandscapeViewport'), 'mobile geometry must not be implemented as a global hand/card scale')
console.log('Mobile replay blocking and board-isolation contracts passed.')
