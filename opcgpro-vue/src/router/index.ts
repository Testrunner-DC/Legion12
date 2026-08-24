import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: () => import('@/l12/site/OfficialHomePage.vue') },
    { path: '/news', name: 'news', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/rules', name: 'rules', component: () => import('@/l12/site/RuleCenterPage.vue') },
    { path: '/battle', name: 'battle', component: () => import('@/l12/site/BattleLandingPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/lobby', name: 'lobby', component: () => import('@/l12/site/BattleHubPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/battle/tournaments', name: 'tournaments', component: () => import('@/l12/site/TournamentCenterPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/decks', name: 'decks', component: () => import('@/l12/site/DeckLibraryPage.vue') },
    { path: '/battle/friends', name: 'friends', component: () => import('@/l12/site/FriendsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/cards', name: 'cards', component: () => import('@/l12/CardArchive.vue') },
    { path: '/battle/rankings', name: 'rankings', component: () => import('@/l12/site/RankingsPage.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/me', name: 'me', component: () => import('@/l12/site/ProfilePage.vue') },
    { path: '/admin', name: 'admin', component: () => import('@/l12/site/AdminPage.vue') },
    { path: '/battle/records', name: 'records', component: () => import('@/l12/MatchRecords.vue'), meta: { section: 'battle', requiresAccount: true } },
    { path: '/sandbox', name: 'sandbox', component: () => import('@/l12/site/SandboxPage.vue') },
    { path: '/deck-editor', component: () => import('@/l12/L12DeckEditor.vue'), meta: { immersive: true } },
    { path: '/game', component: () => import('@/l12/GamePage.vue'), meta: { immersive: true } },
    { path: '/lobby', redirect: '/battle/lobby' },
    { path: '/tournaments', redirect: '/battle/tournaments' },
    { path: '/friends', redirect: '/battle/friends' },
    { path: '/rankings', redirect: '/battle/rankings' },
    { path: '/records', redirect: '/battle/records' },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})
