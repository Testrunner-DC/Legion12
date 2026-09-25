import { createRouter, createWebHistory } from 'vue-router'
import { authState, canAccessAdmin, platformState, refreshCurrentAccount } from '@/l12/platform'
import {
  beginRouteNavigation,
  finishRouteNavigation,
  handleChunkLoadError,
  showRouteNavigationError,
} from '@/chunkRecovery'
import { adminSections } from '@/l12/site/adminSections'

export const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/', name: 'home', component: () => import('@/l12/site/OfficialHomePage.vue') },
    { path: '/news', name: 'news', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/news/:articleId', name: 'news-detail', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/products', name: 'products', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/videos', name: 'videos', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/rules', name: 'rules', component: () => import('@/l12/site/RuleCenterPage.vue') },
    { path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleHubPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/lobby', redirect: '/battle' },
    { path: '/battle/tournaments', name: 'tournaments', component: () => import('@/l12/site/TournamentHubPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/tournaments/:code', name: 'tournament-detail', component: () => import('@/l12/site/TournamentDetailPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/decks', name: 'decks', component: () => import('@/l12/site/DeckLibraryPage.vue') },
    { path: '/decks/:deckId', name: 'public-deck-detail', component: () => import('@/l12/site/PublicDeckDetailPage.vue') },
    { path: '/battle/friends', name: 'friends', component: () => import('@/l12/site/FriendsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/cards', name: 'cards', component: () => import('@/l12/CardArchive.vue') },
    { path: '/battle/rankings', name: 'rankings', component: () => import('@/l12/site/RankingsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/me', name: 'me', component: () => import('@/l12/site/ProfilePage.vue') },
    { path: '/auth/recovery', name: 'account-recovery', component: () => import('@/l12/site/AccountRecoveryPage.vue') },
    {
      path: '/admin', component: () => import('@/l12/site/AdminPage.vue'), meta: { requiresAdmin: true },
      children: [
        { path: '', name: 'admin', component: () => import('@/l12/site/AdminWorkbenchPage.vue'), meta: { adminSection: 'overview' } },
        { path: 'users/accounts', name: 'admin-accounts', component: () => import('@/l12/site/AdminAccountsPage.vue'), meta: { adminSection: 'accounts' } },
        { path: 'users/accounts/:accountId', name: 'admin-account-detail', component: () => import('@/l12/site/AdminAccountDetailPage.vue'), meta: { adminSection: 'accounts' } },
        { path: 'users/renames', name: 'admin-renames', component: () => import('@/l12/site/AdminUsernameChangeRequestsPanel.vue'), meta: { adminSection: 'username-requests' } },
        { path: 'users/bugs/:bugId?', name: 'admin-bugs', component: () => import('@/l12/site/AdminBugsPage.vue'), meta: { adminSection: 'bugs' } },
        { path: 'content/site', name: 'admin-content', component: () => import('@/l12/site/AdminSiteContentPanel.vue'), meta: { adminSection: 'content' } },
        { path: 'content/rules', name: 'admin-rules', component: () => import('@/l12/site/AdminRuleRulingsPanel.vue'), meta: { adminSection: 'rules' } },
        { path: 'content/effects/:cardId?', name: 'admin-effects', component: () => import('@/l12/site/AdminEffectsPage.vue'), meta: { adminSection: 'effects' } },
        { path: 'content/alternate-arts', name: 'admin-alternate-arts', component: () => import('@/l12/site/AdminAlternateArtsPanel.vue'), meta: { adminSection: 'alternate-arts' } },
        { path: 'matches/archive/:matchId?', name: 'admin-matches', component: () => import('@/l12/site/AdminMatchesPage.vue'), meta: { adminSection: 'matches' } },
        { path: 'matches/governance', name: 'admin-match-governance', component: () => import('@/l12/site/AdminMatchGovernancePanel.vue'), meta: { adminSection: 'match-governance' } },
        { path: 'matches/integrity', name: 'admin-integrity', component: () => import('@/l12/site/AdminRankedIntegrityPanel.vue'), meta: { adminSection: 'integrity' } },
        { path: 'matches/tournaments', name: 'admin-tournaments', component: () => import('@/l12/site/AdminTournamentWorkbench.vue'), props: { adminMode: true, embedded: true }, meta: { adminSection: 'tournaments' } },
        { path: 'operations/config', name: 'admin-operations', component: () => import('@/l12/site/AdminOperationsPanel.vue'), meta: { adminSection: 'operations' } },
        { path: 'operations/global', name: 'admin-global-data', component: () => import('@/l12/site/AdminGlobalDataPanel.vue'), meta: { adminSection: 'global-data' } },
        { path: 'operations/cards', name: 'admin-card-analytics', component: () => import('@/l12/site/AdminCardAnalyticsPage.vue'), meta: { adminSection: 'card-analytics' } },
        { path: 'system/releases', name: 'admin-releases', component: () => import('@/l12/site/AdminReleasesPage.vue'), meta: { adminSection: 'releases' } },
        { path: 'system/security', name: 'admin-security', component: () => import('@/l12/site/AdminSecurityPage.vue'), meta: { adminSection: 'security' } },
        { path: 'system/storage', name: 'admin-storage', component: () => import('@/l12/site/AdminServerStoragePanel.vue'), meta: { adminSection: 'storage' } },
        { path: 'system/commands', name: 'admin-commands', component: () => import('@/l12/site/AdminCommandsPage.vue'), meta: { adminSection: 'commands' } },
        { path: 'system/audit', name: 'admin-audit', component: () => import('@/l12/site/AdminAuditPage.vue'), meta: { adminSection: 'audit' } },
      ],
    },
    { path: '/admin/matches/:matchId/replay', name: 'admin-match-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAdmin: true } },
    { path: '/battle/records', name: 'records', component: () => import('@/l12/MatchRecords.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/records/replay/json', name: 'json-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAccount: true } },
    { path: '/battle/records/replay/:matchId', name: 'match-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAccount: true } },
    { path: '/sandbox', name: 'sandbox', component: () => import('@/l12/site/SandboxPage.vue'), meta: { landscapeCanvas: true } },
    { path: '/deck-editor', component: () => import('@/l12/L12DeckEditor.vue'), meta: { immersive: true, landscapeCanvas: true } },
    { path: '/game', component: () => import('@/l12/GamePage.vue'), meta: { immersive: true, landscapeCanvas: true } },
    { path: '/lobby', redirect: '/battle' },
    { path: '/tournaments', redirect: '/battle/tournaments' },
    { path: '/friends', redirect: '/battle/friends' },
    { path: '/rankings', redirect: '/battle/rankings' },
    { path: '/records', redirect: '/battle/records' },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.onError((error, to) => {
  finishRouteNavigation(to.fullPath)
  if (!handleChunkLoadError(error, 'router', to.fullPath)) showRouteNavigationError(to.fullPath)
})

router.afterEach(to => { finishRouteNavigation(to.fullPath) })

router.beforeEach(async to => {
  beginRouteNavigation(to.fullPath)
  if (to.name === 'admin' && typeof to.query.section === 'string') {
    const legacy = adminSections.find(item => item.id === to.query.section)
    if (legacy) {
      const query = { ...to.query }
      delete query.section
      return { path: legacy.path, query, hash: to.hash, replace: true }
    }
  }
  if (authState.verified && platformState.account?.mustChangeUsername && to.name !== 'me')
    return { name: 'me', query: { redirect: to.fullPath, reason: 'username-change-required' } }
  if (to.meta.requiresAdmin !== true && to.meta.requiresAccount !== true) return true
  let timeout: number | undefined
  try {
    await Promise.race([
      // Navigation is not a new login. Reuse the already verified identity; a forced
      // refresh temporarily clears verified and tears down the live game socket.
      refreshCurrentAccount(),
      new Promise<void>(resolve => { timeout = window.setTimeout(resolve, 3_000) }),
    ])
  } catch { /* 权限校验不可用时保持失败关闭。 */ }
  finally { window.clearTimeout(timeout) }
  if (!authState.verified || !platformState.account) return { name: 'me', query: { redirect: to.fullPath } }
  if (platformState.account.mustChangeUsername && to.name !== 'me')
    return { name: 'me', query: { redirect: to.fullPath, reason: 'username-change-required' } }
  if (platformState.account.mustChangePassword && to.name !== 'me')
    return { name: 'me', query: { redirect: to.fullPath, reason: 'password-change-required' } }
  if (to.meta.requiresAdmin !== true) return true
  if (!canAccessAdmin.value) return { name: 'me', query: { redirect: to.fullPath } }
  return true
})
