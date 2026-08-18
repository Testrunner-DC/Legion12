import { createRouter, createWebHistory } from 'vue-router'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: () => import('@/l12/site/OfficialHomePage.vue') },
    { path: '/news', name: 'news', component: () => import('@/l12/site/NewsPage.vue') },
    { path: '/rules', name: 'rules', component: () => import('@/l12/site/RuleCenterPage.vue') },
    { path: '/lobby', name: 'lobby', component: () => import('@/l12/site/BattleHubPage.vue') },
    { path: '/tournaments', name: 'tournaments', component: () => import('@/l12/site/TournamentCenterPage.vue') },
    { path: '/decks', name: 'decks', component: () => import('@/l12/site/DeckLibraryPage.vue') },
    { path: '/friends', name: 'friends', component: () => import('@/l12/site/FriendsPage.vue') },
    { path: '/cards', name: 'cards', component: () => import('@/l12/CardArchive.vue') },
    { path: '/rankings', name: 'rankings', component: () => import('@/l12/site/RankingsPage.vue') },
    { path: '/me', name: 'me', component: () => import('@/l12/site/ProfilePage.vue') },
    { path: '/admin', name: 'admin', component: () => import('@/l12/site/AdminPage.vue') },
    { path: '/records', name: 'records', component: () => import('@/l12/MatchRecords.vue') },
    { path: '/sandbox', name: 'sandbox', component: () => import('@/l12/site/SandboxPage.vue') },
    { path: '/deck-editor', component: () => import('@/l12/L12DeckEditor.vue'), meta: { immersive: true } },
    { path: '/game', component: () => import('@/l12/GamePage.vue'), meta: { immersive: true } },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})
