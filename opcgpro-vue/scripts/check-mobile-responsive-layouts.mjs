import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const read = path => readFile(resolve(root, path), 'utf8')
const [viewportCss, viewportTs, app, archive, decks, board, playerMat, prompt, shell, rules, profile, news, home, feedback, battleHub, rankings, tournaments, recovery, globalCss] = await Promise.all([
  read('src/l12/mobileViewport.css'),
  read('src/l12/mobileViewport.ts'),
  read('src/App.vue'),
  read('src/l12/CardArchive.vue'),
  read('src/l12/site/DeckLibraryPage.vue'),
  read('src/l12/game/GameBoard.vue'),
  read('src/l12/game/PlayerMat.vue'),
  read('src/l12/game/PromptOverlay.vue'),
  read('src/l12/site/SiteShell.vue'),
  read('src/l12/site/RuleCenterPage.vue'),
  read('src/l12/site/ProfilePage.vue'),
  read('src/l12/site/NewsPage.vue'),
  read('src/l12/site/OfficialHomePage.vue'),
  read('src/l12/site/GlobalBugFeedback.vue'),
  read('src/l12/site/BattleHubPage.vue'),
  read('src/l12/site/RankingsPage.vue'),
  read('src/l12/site/TournamentCenterPage.vue'),
  read('src/l12/site/AccountRecoveryPage.vue'),
  read('src/style.css'),
])

const expect = (condition, message) => {
  if (!condition) throw new Error(`mobile responsive contract: ${message}`)
}

expect(!viewportCss.includes('transform: rotate(90deg)'), 'the application body must never be CSS-rotated')
expect(viewportTs.includes('const rotated = false'), 'the viewport runtime must keep one coordinate system')
expect(app.includes('l12-rotate-device') && app.includes('requestLandscapeExperience'), 'immersive portrait fallback must guide the player to real landscape')
expect(archive.includes('MobileFilterSheet') && archive.includes('archive-desktop-filters'), 'card archive must retain search while moving portrait filters into a sheet')
expect(decks.includes('MobileFilterSheet') && decks.includes('plaza-desktop-filters'), 'deck plaza must retain search while moving portrait filters into a sheet')
expect(board.includes("const mobileMoralePickerEnabled = computed(() => mobileLandscapeViewport.value)"), 'morale summary must open on mobile even outside a payment prompt')
expect(board.includes('mobileMoraleInteractive'), 'morale viewing and payment selection must remain distinct')
expect(board.includes('bottom:56px!important') && board.includes('min-height:42px'), 'selected-card actions must reserve the lane above end turn')
expect(playerMat.includes('<Teleport to="body" :disabled="!mobileLayout">') && playerMat.includes("'mobile-action-dock': mobileLayout") && board.includes(':mobile-layout="mobileLandscapeViewport"') && board.includes(':global(.mobile-action-dock)'), 'field attack and active-ability actions must use the shared body-level mobile action dock instead of remaining clipped inside the scaled battlefield')
expect(prompt.includes('@click="focusChoice(choice); toggle(choice)"') && !prompt.includes('class="response-target-detail"'), 'response-target rows must focus and select through one unobscured control, including Court Magician counter-tactic choices')
expect(shell.includes('overflow-x:clip') && shell.includes('env(safe-area-inset-bottom)'), 'site shell must contain portrait content and reserve the home indicator area')
expect(rules.includes('.rule-tabs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));overflow:visible}'), 'portrait rule tabs must wrap instead of requiring horizontal scrolling')
expect(shell.includes('max-height:520px') && feedback.includes('.bug-feedback-trigger{display:none}'), 'compact landscape and portrait feedback must be available without covering page controls')
expect(decks.includes('.page-head h1{font-size:25px}') && profile.includes('.profile-page>header h1{margin:3px 0;font-size:25px}'), 'portrait player pages must use compact heading density')
expect(decks.includes('.deck-notice{position:static;max-width:none'), 'portrait notices must not cover deck actions')
expect(news.includes('.news-page h1{margin:4px 0;font-size:26px}') && home.includes('.hero-copy h1{font-size:26px;line-height:1.08}'), 'portrait editorial pages must use compact hero typography')
expect(battleHub.includes('.battle-hub{padding:14px 10px 34px}') && battleHub.includes('.mode-panel{padding:14px}'), 'battle lobby must scale its panels and controls together on narrow phones')
expect(rankings.includes('.ranking-page{--ranking-master-avatar:28px;padding:14px 10px 32px}') && rankings.includes('.matrix-grid{grid-auto-rows:52px}'), 'rankings must compact both table rows and matchup matrix cells')
expect(tournaments.includes('.tournament-page{padding:14px 10px 34px}') && tournaments.includes('.bracket>section{min-width:190px;padding:8px}'), 'tournament content must preserve bracket proportions while compacting its panels')
expect(recovery.includes('.recovery-card{padding:18px}') && recovery.includes('.recovery-card button{min-height:42px'), 'recovery form must scale the card and primary control together')
expect(globalCss.includes('body .friends-page{padding:14px 10px 34px}') && globalCss.includes('body .friends-page .hero-avatar{width:56px;height:56px'), 'friends page must compact its container and visual anchors together')

console.log('Mobile responsive layout contracts passed.')
