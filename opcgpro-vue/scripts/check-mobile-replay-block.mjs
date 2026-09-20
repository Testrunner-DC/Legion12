import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const read = path => readFile(resolve(root, path), 'utf8')
const [viewport, board, records, replay, prompt, hand, tile] = await Promise.all([
  read('src/l12/mobileViewport.ts'),
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

expect(viewport.includes('export function isMobileDeviceExperience()'), 'mobile eligibility must have a stable device entry')
expect(board.includes('mobileDeviceExperience.value = isMobileDeviceExperience()'), 'GameBoard must capture mobile eligibility at mount')
expect(board.includes('mobileDeviceExperience.value && viewport.width >= viewport.height'), 'phone geometry must require the landscape tier')
expect(!board.includes('viewport.height >= 300 && viewport.height <= 430'), 'browser chrome height must not switch the board mode')
for (const handler of ['selectHandFor', 'selectPublicCardFor', 'inspectActiveDisaster']) {
  const match = board.match(new RegExp(`function ${handler}[\\s\\S]*?\\n}`, 'm'))?.[0] ?? ''
  expect(!match.includes('mobileInspectorOpen.value = true'), `${handler} must not auto-open card details`)
}
expect(board.includes('class="mobile-card-inspector-handle mobile-card-inspector-handle-global"') && board.includes("CardDetailContent :card=\"focusDetailCard\""), 'card details must use the global persistent handle and CardDetailContent')
expect(board.includes(':mobile-layout="mobileLandscapeViewport"'), 'mobile-safe modal layout must be passed to board overlays')
expect(prompt.includes("'mobile-safe-overlay': mobileLayout"), 'prompt overlay must receive the mobile safe rectangle')
expect(records.includes("error.value = '请到电脑端查看回放'"), 'record actions must state the desktop-only replay notice')
expect(records.includes('mobileReplayNotice.value = true') && records.includes('role="alertdialog"'), 'mobile replay actions must show an in-viewport dismissible notice')
expect(records.includes('function openReplayImport()') && records.includes('if (mobileReplayBlocked) return blockMobileReplay()'), 'JSON replay import must be blocked before opening a picker')
expect(replay.includes('const mobileReplayBlocked = isMobileDeviceExperience()'), 'direct replay route must detect mobile before loading')
expect(replay.includes('if (!mobileReplayBlocked) void loadReplay()'), 'direct replay route must not request data on mobile')
expect(replay.includes('v-if="mobileReplayBlocked" class="replay-mobile-blocked"') && replay.includes('请到电脑端查看回放'), 'direct replay route must render the blocker page')
expect(!hand.includes('mobileLandscapeViewport') && !tile.includes('mobileLandscapeViewport'), 'mobile geometry must not be implemented as a global hand/card scale')
console.log('Mobile replay blocking and board-isolation contracts passed.')
