import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const read = path => readFile(resolve(root, path), 'utf8')
const settings = await read('src/l12/site/L12SettingsModal.vue')
const audioPreferences = await read('src/l12/audioPreferences.ts')
const cardTile = await read('src/l12/CardTile.vue')
const graveyardOverlay = await read('src/l12/game/GraveyardOverlay.vue')
const [viewportCss, viewportTs, battleLayout, mobileDialogLayout, app, archive, decks, deckBrowser, filterSheet, boardComponent, boardMobileStyle, playerMat, prompt, shell, rules, profile, admin, news, home, feedback, battleHub, rankings, tournaments, recovery, globalCss] = await Promise.all([
  read('src/l12/mobileViewport.css'),
  read('src/l12/mobileViewport.ts'),
  read('src/l12/game/battleViewportLayout.ts'),
  read('src/l12/mobileDialogLayout.ts'),
  read('src/App.vue'),
  read('src/l12/CardArchive.vue'),
  read('src/l12/site/DeckLibraryPage.vue'),
  read('src/l12/site/DeckConstructionBrowser.vue'),
  read('src/l12/site/MobileFilterSheet.vue'),
  read('src/l12/game/GameBoard.vue'),
  read('src/l12/game/GameBoard.mobile.css'),
  read('src/l12/game/PlayerMat.vue'),
  read('src/l12/game/PromptOverlay.vue'),
  read('src/l12/site/SiteShell.vue'),
  read('src/l12/site/RuleCenterPage.vue'),
  read('src/l12/site/ProfilePage.vue'),
  read('src/l12/site/AdminPage.vue'),
  read('src/l12/site/NewsPage.vue'),
  read('src/l12/site/OfficialHomePage.vue'),
  read('src/l12/site/GlobalBugFeedback.vue'),
  read('src/l12/site/BattleHubPage.vue'),
  read('src/l12/site/RankingsPage.vue'),
  read('src/l12/site/TournamentCenterPage.vue'),
  read('src/l12/site/AccountRecoveryPage.vue'),
  read('src/style.css'),
])
const board = `${boardComponent}\n${boardMobileStyle}`
const matchRecords = await read('src/l12/MatchRecords.vue')
const deckEditor = await read('src/l12/L12DeckEditor.vue')

const expect = (condition, message) => {
  if (!condition) throw new Error(`mobile responsive contract: ${message}`)
}

expect(!viewportCss.includes('body {\n  transform: rotate(90deg)') && viewportCss.includes('.l12-landscape-surface,#l12-landscape-teleports'), 'only the opted-in route canvas and Teleport host may rotate')
expect(boardComponent.includes('<style scoped src="./GameBoard.mobile.css"></style>') && !boardComponent.includes('.mobile-landscape-board{') && boardMobileStyle.includes('.mobile-landscape-board'), 'mobile battle CSS must remain physically isolated from the desktop component stylesheet')
expect(viewportTs.includes('export function resolveViewportMode(') && !viewportTs.includes('(pointer: coarse)') && !viewportTs.includes('screen.orientation'), 'the viewport runtime must classify geometry without device identity or physical orientation')
expect(viewportTs.includes("mobileLayout === 'on' ? true : mobileLayout === 'off' ? false : geometryMobile") && viewportTs.includes('watch([enabled, () => audioPreferences.mobileLayout], update)'), 'the user mobile-layout preference must override layout classification without taking over physical rotation')
expect(viewportTs.includes('compactLandscape(rotatedWidth, rotatedHeight, previous.rotated)') && !viewportTs.includes('previous.rotated || previous.mobile'), 'forced mobile layout must not relax or couple the independent geometry rotation decision')
expect(battleLayout.includes('export function resolveBattleViewportLayout(') && battleLayout.includes('scale: options.mobile ? 1 : desktopScale') && battleLayout.includes("window.addEventListener('l12-viewport-change', update)"), 'battle viewport classification, scaling and listeners must stay behind one testable layout boundary')
expect(mobileDialogLayout.includes('MOBILE_DIALOG_COVERAGE = 0.75') && mobileDialogLayout.includes('MOBILE_DIALOG_ASPECT_RATIO = 16 / 9') && viewportTs.includes("root.style.setProperty('--l12-mobile-dialog-width'") && viewportCss.includes('width: var(--l12-mobile-dialog-width) !important'), 'mobile dialogs must share a stable 16:9 frame capped to 75% of the safe logical canvas')
expect(graveyardOverlay.includes('class="graveyard-card-name"') && viewportCss.includes('.prompt-card-candidate__name') && viewportCss.includes('.graveyard-card-name') && viewportCss.includes('justify-content: safe center !important'), 'mobile dialog card collections must retain explicit complete names and centre sparse content without stretching dense rows')
expect(settings.includes('v-model="audioPreferences.mobileLayout"') && audioPreferences.includes("mobileLayout: 'auto' | 'on' | 'off'") && audioPreferences.includes('dataset.l12MobileLayoutPreference'), 'mobile layout choice must be exposed and persisted through the shared settings model')
expect(cardTile.includes('container-type:inline-size') && cardTile.includes('--l12-card-stat-font:clamp(6px,11cqw,18px)') && board.includes('var(--l12-card-stat-font, 7px)'), 'card value badges must scale continuously from their own card container rather than viewport-specific fixed sizes')
expect(app.includes('data-l12-landscape-canvas') && !app.includes('l12-rotate-device') && !app.includes('requestLandscapeExperience'), 'immersive compact routes must use the logical canvas without a rotate-device blocker')
expect(archive.includes('MobileFilterSheet') && archive.includes('archive-desktop-filters'), 'card archive must retain search while moving portrait filters into a sheet')
expect(decks.includes('MobileFilterSheet') && decks.includes('plaza-desktop-filters'), 'deck plaza must retain search while moving portrait filters into a sheet')
expect(deckBrowser.includes('MobileFilterSheet') && deckBrowser.includes('construction-desktop-filters') && deckBrowser.includes('grid-template-columns:minmax(0,1fr) auto'), 'shared deck construction viewer must retain search while moving portrait filters into a sheet')
expect(rules.includes('MobileFilterSheet') && rules.includes('rule-desktop-filter') && rules.includes('desktop-popular-keywords'), 'rule center must retain search while moving portrait categories and keyword helpers into a sheet')
expect(filterSheet.includes('@keydown.esc="close"') && filterSheet.includes('env(safe-area-inset-left)') && filterSheet.includes('env(safe-area-inset-right)'), 'shared portrait filter sheet must close by keyboard and respect both horizontal safe areas')
expect(board.includes("const mobileMoralePickerEnabled = computed(() => mobileLandscapeViewport.value)"), 'morale summary must open on mobile even outside a payment prompt')
expect(board.includes(':data-l12-battle-layout="mobileLandscapeViewport ? \'mobile\' : \'desktop\'"') && !board.includes('function updateScale()'), 'the board tree must declare its active layout while delegating viewport math to the shared layout kernel')
expect(board.includes('class="mobile-detail-handle-reservation" aria-hidden="true"') && board.includes('--l12-mobile-left-rail-w:clamp(88px,calc(var(--l12-viewport-height,100vh) * .22),118px)') && board.includes('grid-template-columns:var(--l12-mobile-left-rail-w) minmax(0,1fr) var(--l12-mobile-right-rail-w)') && board.includes('grid-template-rows:var(--l12-mobile-current-disaster-h) var(--l12-mobile-disaster-value-h) calc(var(--l12-mobile-disaster-orb) * 2'), 'the detail handle, current disaster, value, round-card pool and optional extra zones must share one logical-viewport rail allocation')
expect(!board.includes('@media (max-height: 520px)') && !board.includes('@media (min-height: 521px)'), 'logical portrait rotation and physical landscape must not receive different card geometry from physical CSS media height')
expect(board.includes('--l12-mobile-resource-w:clamp(74px,calc(var(--l12-viewport-width,100vw) * .09),92px)') && board.includes('--l12-mobile-hand-h:clamp(64px,calc(var(--l12-viewport-height,100vh) * .16),102px)') && board.includes('--l12-mobile-morale-orb:clamp(12px,calc(var(--l12-viewport-height,100vh) * .022),17px)'), 'live mobile card, hand, resource and morale dimensions must resolve from logical viewport tokens rather than physical vw/vh')
expect(board.includes('mobileMoraleInteractive'), 'morale viewing and payment selection must remain distinct')
expect(board.includes('bottom:56px!important') && board.includes('min-height:42px'), 'selected-card actions must reserve the lane above end turn')
expect(playerMat.includes('<Teleport :to="landscapeTeleportTarget()" :disabled="!mobileLayout">') && playerMat.includes("'mobile-action-dock': mobileLayout") && board.includes(':mobile-layout="mobileLandscapeViewport"') && board.includes(':global(.mobile-action-dock)'), 'field attack and active-ability actions must use the shared logical-canvas mobile action dock instead of remaining clipped inside the battlefield')
expect(prompt.includes('@click="focusChoice(choice); toggle(choice)"') && !prompt.includes('class="response-target-detail"'), 'response-target rows must focus and select through one unobscured control, including Court Magician counter-tactic choices')
expect(shell.includes('overflow-x:clip') && shell.includes('env(safe-area-inset-bottom)') && shell.includes('--mobile-head-h:calc(58px + env(safe-area-inset-top') && shell.includes('height:100dvh'), 'site shell must contain portrait content and reserve dynamic safe areas')
expect(rules.includes('.rule-tabs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));overflow:visible}'), 'portrait rule tabs must wrap instead of requiring horizontal scrolling')
expect(shell.includes('max-height:520px') && feedback.includes('.bug-feedback-trigger{display:none}'), 'compact landscape and portrait feedback must be available without covering page controls')
expect(decks.includes('.page-head h1{font-size:25px}') && profile.includes('.profile-page>header h1{margin:3px 0;font-size:25px}'), 'portrait player pages must use compact heading density')
expect(admin.includes('class="admin-mobile-navigation"') && admin.includes("switchAdminTab((event.target as HTMLSelectElement).value as AdminTab)") && admin.includes('.admin-mobile-navigation{display:none}') && admin.includes('.admin-sidebar{display:none}'), 'portrait administration must use one compact functional module picker while preserving the desktop sidebar')
expect(admin.includes('.account-row.head{display:none}') && admin.includes('data-label="建立时间"') && admin.includes('.effect-filters{display:grid'), 'portrait administration must label stacked account fields and reflow dense workbench filters without horizontal overflow')
expect(decks.includes('.deck-notice{position:static;max-width:none'), 'portrait notices must not cover deck actions')
expect(news.includes('.news-page h1{margin:4px 0;font-size:26px}') && home.includes('.hero-copy h1{font-size:26px;line-height:1.08}'), 'portrait editorial pages must use compact hero typography')
expect(battleHub.includes('.battle-hub{padding:14px 10px 34px}') && battleHub.includes('.mode-panel{padding:14px}'), 'battle lobby must scale its panels and controls together on narrow phones')
expect(rankings.includes('.ranking-page{--ranking-master-avatar:28px;padding:14px 10px 32px}') && rankings.includes('.matrix-grid{grid-auto-rows:52px}'), 'rankings must compact both table rows and matchup matrix cells')
expect(rankings.includes('data-label="最擅长主宰"') && rankings.includes('data-label="最强玩家"') && rankings.includes('@media(max-width:700px)') && rankings.includes('.player-mobile-meta') && rankings.includes('.player-table,.master-table,.honor-table{overflow:visible'), 'portrait rankings must become compact information cards instead of requiring horizontal table scrolling')
expect(tournaments.includes('.tournament-page{padding:14px 10px 34px}') && tournaments.includes('.bracket>section{min-width:190px;padding:8px}'), 'tournament content must preserve bracket proportions while compacting its panels')
expect(tournaments.includes('class="site-toast"') && !tournaments.includes('class="toast"') && tournaments.includes('top:auto;right:22px;bottom:22px;left:auto;transform:none'), 'site notifications must be isolated from the battle toast positioning contract')
expect(shell.includes('class="site-drawer-backdrop"') && shell.includes('aria-controls="site-mobile-drawer"') && shell.includes("event.key === 'Escape'") && shell.includes("document.body.style.overflow = 'hidden'"), 'mobile navigation must provide a modal backdrop, escape close, focus semantics and body scroll lock')
expect(app.includes('maximum-scale=5, user-scalable=yes') && app.includes('locked ? lockedViewport : readableViewport'), 'reading routes must allow zoom while immersive battle and editor routes stay locked')
expect(archive.includes('const renderLimit = ref(60)') && archive.includes('IntersectionObserver') && archive.includes('class="archive-group-nav"') && archive.includes('class="archive-back-to-top"'), 'the archive must group cards, cap its initial DOM, append on intersection and provide a back-to-top action')
expect(matchRecords.includes('class="record-summary-grid"') && matchRecords.includes('class="mobile-replay-inline"') && !matchRecords.includes('mobileReplayNotice'), 'mobile records must expose a complete inline summary without a blocking replay notice')
expect(profile.includes('l12-profile-master-records') && profile.includes('l12-profile-sessions') && profile.includes('class="profile-quick-actions"'), 'mobile profile sections must be remembered and keep frequent actions near the identity summary')
expect(deckEditor.includes('class="portrait-guide"') && deckEditor.includes('l12-deck-editor-portrait-guide'), 'portrait deck-editor entry must explain the landscape workspace without discarding edit state')
for (const [name, source] of [['shell', shell], ['news', news], ['home', home], ['rules', rules], ['rankings', rankings]]) {
  expect(!/@media\s*\(max-width:\s*(720|760|850)px\)/.test(source), `${name} must use the shared 700px compact boundary rather than a legacy primary breakpoint`)
}
expect(recovery.includes('.recovery-card{padding:18px}') && recovery.includes('.recovery-card button{min-height:44px'), 'recovery form must scale the card and primary control together')
expect(globalCss.includes('body .friends-page{padding:14px 10px 34px}') && globalCss.includes('body .friends-page .hero-avatar{width:56px;height:56px'), 'friends page must compact its container and visual anchors together')

console.log('Mobile responsive layout contracts passed.')

