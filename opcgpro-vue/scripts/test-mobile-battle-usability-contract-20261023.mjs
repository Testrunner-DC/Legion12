import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { hasPersistentMobilePlayerIdentities, hasSharedBattleCardDetails, hasProportionalMobileActionSize } from './battle-usability-template-contract.mjs'

const root = fileURLToPath(new URL('../', import.meta.url))
const read = path => readFile(new URL(path, `file:///${root.replaceAll('\\', '/')}/`), 'utf8')
const [board, mobile, dock] = await Promise.all([
  read('src/l12/game/GameBoard.vue'),
  read('src/l12/game/GameBoard.mobile.css'),
  read('src/l12/game/MobileBattleDock.css'),
])

assert.match(board, /alternateArtApi\.gallery\(\)/,
  'battle details must load the existing public alternate-art catalog once per board')
assert.match(board, /art\.active[\s\S]*?art\.kind !== 'gallery-card'[\s\S]*?art\.baseCardId\.toLowerCase\(\) === card\.cardId\.toLowerCase\(\)/,
  'only active public art for the visible base card may contribute metadata')
assert.match(board, /presentationImageIdentity\(image\) === visibleImage/,
  'alternate-art metadata must require an exact normalized presentation-image identity')
assert.match(board, /artist: alternateArt\?\.artist \|\| undefined/)
assert.match(board, /collaboration: alternateArt\?\.collaboration \|\| undefined/)
assert.doesNotMatch(board, /mobilePlayerDetailsOpen|openMobilePlayerDetails|mobile-player-details-overlay/,
  'mobile player identity must not be hidden behind a secondary dialog')

const mobileSummary = board.match(/<div v-if="mobileLandscapeViewport" class="mobile-player-summary-strip"[\s\S]*?<\/div>/)?.[0] ?? ''
assert.equal((mobileSummary.match(/<BattlePlayerIdentity/g) ?? []).length, 2,
  'both player identities must remain expanded in the mobile dock')
assert.equal((mobileSummary.match(/show-master-details/g) ?? []).length, 2,
  'both persistent player identities must include master details')

assert.match(dock, /--l12-mobile-left-rail-w:max\(78px,calc\(var\(--l12-mobile-master-w\) \* 1\.3\)\)/)
assert.match(dock, /--l12-mobile-dock-width:clamp\(112px,calc\(var\(--l12-viewport-width,100vw\) \* \.15\),132px\)/)
assert.match(mobile, /--l12-mobile-relic-w:calc\(var\(--l12-mobile-master-w\) \* \.9\)/)
assert.match(mobile, /--l12-mobile-pile-w:calc\(var\(--l12-mobile-master-w\) \* \.7\)/)
assert.match(mobile, /--l12-mobile-action-h:clamp\(40px,[^;]+,44px\)/,
  'primary mobile actions must grow beyond the former 30–38px range')
assert.match(mobile, /hand-card-wrap\.selected\) \{ transform:none !important; \}/,
  'selected hand cards must remain in their reserved hand row')
assert.match(dock, /mobile-player-summary-strip[\s\S]*?battle-player-identity/,
  'persistent player summaries need bounded compact dock geometry')
assert.match(dock, /combat-resolution-panel \.l12-actions > button \{[\s\S]*?min-height:42px !important; height:42px !important;/,
  'high-frequency combat decisions need a larger effective touch surface')

let contractCases = 0
const check = (predicate, source, expected, name) => {
  assert.equal(predicate(source), expected, name)
  contractCases++
}
const replace = (source, before, after) => {
  assert.ok(source.includes(before), `missing mutation anchor: ${before}`)
  return source.replace(before, after)
}
const changeSummary = (before, after) => replace(board, mobileSummary, replace(mobileSummary, before, after))
check(hasPersistentMobilePlayerIdentities, board, true, 'real persistent summaries')
check(hasPersistentMobilePlayerIdentities, replace(board, 'class="mobile-player-summary-strip"', 'hidden class="mobile-player-summary-strip"'), false, 'hidden summary rejected')
check(hasPersistentMobilePlayerIdentities, replace(board, 'class="mobile-player-summary-strip"', 'v-show="mobilePlayerDetailsOpen" class="mobile-player-summary-strip"'), false, 'second dialog gate rejected')
check(hasPersistentMobilePlayerIdentities, changeSummary(':player="viewEnemy"', ':player="viewMe"'), false, 'duplicate own identity rejected')
check(hasPersistentMobilePlayerIdentities, changeSummary('show-master-details />', ':show-master-details="false" />'), false, 'missing master facts rejected')
check(hasPersistentMobilePlayerIdentities, changeSummary(':connection-label="connectionLabel(viewEnemy.playerIndex)"', ':connection-label="connectionLabel(viewMe.playerIndex)"'), false, 'wrong peer connection rejected')
check(hasSharedBattleCardDetails, board, true, 'real shared detail with metadata intersection')
check(hasSharedBattleCardDetails, replace(board, "type BattleDetailCard = DeckCard & { artist?: string }", 'type BattleDetailCard = { artist?: string }'), false, 'parallel detail type rejected')
check(hasSharedBattleCardDetails, replace(board, 'computed<BattleDetailCard | null>', 'computed<DeckCard | null>'), true, 'direct original DeckCard shape remains valid')
check(hasSharedBattleCardDetails, replace(board, ':show-catalog-only="false"', ':show-catalog-only="true"'), false, 'catalog-only metadata leak rejected')
check(hasSharedBattleCardDetails, replace(board, '<CardDetailContent :card="focusDetailCard"', '<CardDetailContent :card="anotherCard"'), false, 'parallel card source rejected')
check(hasProportionalMobileActionSize, mobile, true, 'real proportional 40–44px size')
check(hasProportionalMobileActionSize, replace(mobile, '--l12-mobile-action-h:clamp(40px,calc(var(--l12-viewport-height,100vh) * .11),44px)', '--l12-mobile-action-h:34px'), false, 'fixed small action size rejected')
check(hasProportionalMobileActionSize, replace(mobile, 'clamp(40px,calc(var(--l12-viewport-height,100vh) * .11),44px)', 'clamp(30px,calc(var(--l12-viewport-height,100vh) * .09),38px)'), false, 'old small proportional size rejected')
check(hasProportionalMobileActionSize, `${mobile}\n.x {--l12-mobile-action-h:20px;}`, false, 'later override rejected')
console.log(`mobile battle usability source contract: ok; ${contractCases} semantic guards`)
