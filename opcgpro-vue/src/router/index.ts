import { createRouter, createWebHistory } from 'vue-router'
import { canAccessAdmin, platformState, refreshCurrentAccount } from '@/l12/platform'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: () => import('@/l12/site/OfficialHomePage.vue') },
    { path: '/news', name: 'news', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/rules', name: 'rules', component: () => import('@/l12/site/RuleCenterPage.vue') },
    { path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleHubPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/lobby', redirect: '/battle' },
    { path: '/battle/tournaments', name: 'tournaments', component: () => import('@/l12/site/TournamentCenterPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/decks', name: 'decks', component: () => import('@/l12/site/DeckLibraryPage.vue') },
    { path: '/battle/friends', name: 'friends', component: () => import('@/l12/site/FriendsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/cards', name: 'cards', component: () => import('@/l12/CardArchive.vue') },
    { path: '/battle/rankings', name: 'rankings', component: () => import('@/l12/site/RankingsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/me', name: 'me', component: () => import('@/l12/site/ProfilePage.vue') },
    { path: '/auth/recovery', name: 'account-recovery', component: () => import('@/l12/site/AccountRecoveryPage.vue') },
    { path: '/admin', name: 'admin', component: () => import('@/l12/site/AdminPage.vue'), meta: { requiresAdmin: true } },
    { path: '/admin/matches/:matchId/replay', name: 'admin-match-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAdmin: true } },
    { path: '/battle/records', name: 'records', component: () => import('@/l12/MatchRecords.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/records/replay/json', name: 'json-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAccount: true } },
    { path: '/battle/records/replay/:matchId', name: 'match-replay', component: () => import('@/l12/ReplayPage.vue'), meta: { immersive: true, replay: true, requiresAccount: true } },
    { path: '/sandbox', name: 'sandbox', component: () => import('@/l12/site/SandboxPage.vue') },
    { path: '/deck-editor', component: () => import('@/l12/L12DeckEditor.vue'), meta: { immersive: true } },
    { path: '/game', component: () => import('@/l12/GamePage.vue'), meta: { immersive: true } },
    { path: '/lobby', redirect: '/battle' },
    { path: '/tournaments', redirect: '/battle/tournaments' },
    { path: '/friends', redirect: '/battle/friends' },
    { path: '/rankings', redirect: '/battle/rankings' },
    { path: '/records', redirect: '/battle/records' },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach(async to => {
  if (to.meta.requiresAdmin !== true && to.meta.requiresAccount !== true) return true
  try {
    await Promise.race([
      refreshCurrentAccount({ force: true }),
      new Promise<void>(resolve => window.setTimeout(resolve, 3_000)),
    ])
  } catch { /* 权限校验不可用时保持失败关闭。 */ }
  if (!platformState.account) return { name: 'me', query: { redirect: to.fullPath } }
  if (platformState.account.mustChangePassword && to.name !== 'me')
    return { name: 'me', query: { redirect: to.fullPath, reason: 'password-change-required' } }
  if (to.meta.requiresAdmin !== true) return true
  if (!canAccessAdmin.value) return { name: 'me', query: { redirect: to.fullPath } }
  return true
})
