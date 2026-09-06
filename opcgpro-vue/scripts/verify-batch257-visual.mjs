import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || path.join(process.env.TEMP || root, 'l12-batch257-ui')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import ProfilePage from '/src/l12/site/ProfilePage.vue'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import BattleHubPage from '/src/l12/site/BattleHubPage.vue'
import SiteShell from '/src/l12/site/SiteShell.vue'
import { platformState, rankedApi } from '/src/l12/platform.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'

platformState.account = { id: 'qa-player', username: '视觉验收玩家', role: 'player', mustChangePassword: false }
platformState.token = ''
l12State.status = 'online'

const factions = [
  { id: 'order', name: '秩序', color: '#d5dd45', icon: '', firstTitle: '秩序第一', topFiveTitle: '秩序前五', tiers: [] },
  { id: 'chaos', name: '混沌', color: '#bb65dc', icon: '', firstTitle: '混沌第一', topFiveTitle: '混沌前五', tiers: [] },
  { id: 'fate', name: '命运', color: '#4fd3aa', icon: '', firstTitle: '命运第一', topFiveTitle: '命运前五', tiers: [] },
]
const masters = [
  { rank: 1, masterId: 'ST06-M1', masterName: '银臂努阿达', games: 31, wins: 20, losses: 11, winRate: 64.5, usageRate: 40, firstWinRate: 66, secondWinRate: 63, firstWins: 10, firstGames: 15, secondWins: 10, secondGames: 16, strongestPlayer: '玩家甲', title: '最强银臂努阿达' },
  { rank: 2, masterId: 'ST01-M1', masterName: '杨戬', games: 22, wins: 11, losses: 11, winRate: 50, usageRate: 30, firstWinRate: 50, secondWinRate: 50, firstWins: 6, firstGames: 12, secondWins: 5, secondGames: 10, strongestPlayer: '玩家乙', title: '最强杨戬' },
  { rank: 3, masterId: 'ST02-M1', masterName: '孟婆', games: 12, wins: 5, losses: 7, winRate: 41.7, usageRate: 30, firstWinRate: 40, secondWinRate: 43, firstWins: 2, firstGames: 5, secondWins: 3, secondGames: 7, strongestPlayer: '玩家丙', title: '最强孟婆' },
]
const profile = {
  accountId: 'qa-player', username: '视觉验收玩家', seasonId: 'qa-season', faction: '秩序', sevenValue: 10642,
  displayValue: '七曜值 10,642', placementPlayed: 5, placementWins: 3, placed: true, wins: 18, losses: 9,
  winStreak: 2, lossStreak: 0, tier: '比蒙银臂', tierIndex: 2, factionRank: 8, title: '最强银臂努阿达',
  titles: ['最强银臂努阿达'], rankLabel: '比蒙银臂', selectedMasterTitle: '最强银臂努阿达',
  masterTitles: ['最强银臂努阿达'],
}
const rankedOverview = {
  profile, factionTotals: { 秩序: 10642, 混沌: 21858, 命运: 57131 },
  config: { placementMatches: 5, placementMaximum: 15000, broadcastEnabled: true, factions, masterTitles: [] }, history: [],
}
rankedApi.overview = async () => rankedOverview
rankedApi.history = async () => []
rankedApi.leaderboard = async () => ({
  players: [], masterChampions: [], analytics: {
    range: 'season', summary: { matches: 65, placedPlayers: 3, activeMasters: 3 }, masters,
    matchups: [
      { masterId: 'ST06-M1', opponentMasterId: 'ST01-M1', games: 7, wins: 5, winRate: 71.4, firstGames: 4, firstWins: 3, secondGames: 3, secondWins: 2 },
      { masterId: 'ST01-M1', opponentMasterId: 'ST06-M1', games: 7, wins: 2, winRate: 28.6, firstGames: 3, firstWins: 1, secondGames: 4, secondWins: 1 },
    ],
  },
})

const now = Date.now()
const active = new URLSearchParams(location.search).has('active')
l12State.operationsPolicy = {
  version: 257, season: { id: 'qa-season', name: '第0赛季 · 始源', status: 'active' }, disasterCardIds: [],
  matchModes: [], defaultRoomConfig: { matchModeId: 'friendly', spectating: 'public', handVisibility: 'request', disasterMode: 'all' },
  seasonDisasterModeAvailable: true, cardRestrictions: [], defaultPresetDeckIds: [], announcements: [],
  maintenance: active
    ? { enabled: true, active: true, entryBlocked: true, status: 'maintenance', message: '服务器维护中', broadcastMessage: '维护进行中', advanceBroadcastHours: 24, expectedDurationHours: 2 }
    : { enabled: true, active: false, entryBlocked: false, status: 'upcoming', message: '计划维护', broadcastMessage: '计划维护', startsAt: new Date(now + 7_200_000).toISOString(), endsAt: new Date(now + 14_400_000).toISOString(), advanceBroadcastHours: 24, expectedDurationHours: 2 },
}

const query = new URLSearchParams(location.search)
const component = query.has('profile') ? ProfilePage : query.has('ranking') ? RankingsPage : BattleHubPage
const routes = [
  { path: '/', component: { render: () => h('div') } },
  { path: '/battle', component: { render: () => h('div') }, meta: { section: 'battle', requiresAccount: true } },
  { path: '/battle/rankings', component: RankingsPage },
  { path: '/battle/records', component: { render: () => h('div') } },
  { path: '/battle/tournaments', component: { render: () => h('div') } },
  { path: '/battle/friends', component: { render: () => h('div') } },
  { path: '/decks', component: { render: () => h('div') } },
]
const router = createRouter({ history: createMemoryHistory(), routes })
if (query.has('shell')) await router.push('/battle')
const app = createApp({ render: () => query.has('shell') ? h(SiteShell, null, { default: () => h('div') }) : h(component) })
app.use(router)
app.mount('#app')
`

let browser
const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'batch257-synthetic',
    resolveId(id) { if (id === '/__batch257_qa__.js') return id },
    load(id) { if (id === '/__batch257_qa__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__batch257_qa__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__batch257_qa__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

function closeEnough(values, tolerance = 1) {
  return values.every(value => Math.abs(value - values[0]) <= tolerance)
}

try {
  await server.listen()
  const port = server.httpServer.address().port
  const base = `http://127.0.0.1:${port}/__batch257_qa__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  for (const [width, height] of [[1280, 900], [760, 900], [390, 844]]) {
    await page.setViewportSize({ width, height })
    await page.goto(`${base}?profile=1`)
    await page.locator('.title-manager').waitFor()
    const layout = await page.locator('.title-manager').evaluate(element => {
      const button = element.querySelector('.title-manager-heading button')
      const manager = element.getBoundingClientRect()
      const control = button.getBoundingClientRect()
      return { managerRight: manager.right, controlRight: control.right, managerWidth: manager.width, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth }
    })
    if (width > 420 && layout.managerRight - layout.controlRight > 22) throw new Error(`Title rules button is not aligned to the container right at ${width}px`)
    if (layout.scrollWidth > layout.clientWidth + 1) throw new Error(`Title manager overflows at ${width}px`)
    await page.screenshot({ path: path.join(output, `profile-title-${width}.png`), fullPage: true })
  }

  await page.setViewportSize({ width: 1280, height: 900 })
  await page.goto(`${base}?ranking=1`)
  await page.getByRole('button', { name: '对阵一览', exact: true }).click()
  await page.locator('.matrix-cell').first().waitFor()
  const matrix = await page.evaluate(() => ({
    firstHeading: document.querySelector('.matrix-grid')?.firstElementChild?.textContent?.trim(),
    headerWidths: [...document.querySelectorAll('.matrix-head')].map(element => element.getBoundingClientRect().width),
    rowWidths: [...document.querySelectorAll('.matrix-row-head')].map(element => element.getBoundingClientRect().width),
    emptyLabels: [...document.querySelectorAll('.matrix-cell')].filter(element => element.getAttribute('title') === '暂无对局').map(element => element.textContent.replace(/\s+/g, ' ').trim()),
    heights: [...document.querySelectorAll('.matrix-rank-cell,.matrix-row-head,.matrix-cell')].map(element => element.getBoundingClientRect().height),
  }))
  if (matrix.firstHeading !== '排名') throw new Error('Ranking is not the first matchup column')
  if (!closeEnough([...matrix.headerWidths, ...matrix.rowWidths]) || Math.abs(matrix.headerWidths[0] - 104) > 1) throw new Error('Master matchup columns are not equal 104px columns')
  if (!matrix.emptyLabels.length || matrix.emptyLabels.some(label => label.replace(/\s+/g, '') !== '等待更多对局')) throw new Error('Empty matchup cells do not show the required two-line label')
  if (matrix.heights.some(height => Math.abs(height - 62) > 1)) throw new Error(`Matchup rows changed from 62px: ${matrix.heights.join(', ')}`)
  await page.screenshot({ path: path.join(output, 'rankings-matrix-1280.png'), fullPage: true })

  await page.goto(base)
  await page.locator('.maintenance-banner.scheduled').waitFor()
  const scheduledBefore = await page.locator('.maintenance-banner strong').textContent()
  await page.waitForTimeout(1100)
  const scheduledAfter = await page.locator('.maintenance-banner strong').textContent()
  if (scheduledBefore === scheduledAfter) throw new Error('Scheduled maintenance countdown did not update')
  await page.screenshot({ path: path.join(output, 'maintenance-scheduled.png'), fullPage: true })

  await page.goto(`${base}?active=1`)
  await page.locator('.maintenance-banner.active').waitFor()
  const activeText = (await page.locator('.maintenance-banner').textContent()).replace(/\s+/g, ' ').trim()
  if (!activeText.includes('服务器维护中') || !activeText.includes('结束时间待定')) throw new Error('Active maintenance without endsAt is not explained')
  await page.screenshot({ path: path.join(output, 'maintenance-active-open-ended.png'), fullPage: true })

  await page.setViewportSize({ width: 1280, height: 800 })
  await page.goto(`${base}?shell=1`)
  const battleNav = await page.locator('.site-nav a').allTextContents()
  if (battleNav.join('|') !== '主页|大厅|赛事|牌库|排行|好友|对局') throw new Error(`Battle navigation labels are incorrect: ${battleNav.join('|')}`)
  await page.screenshot({ path: path.join(output, 'battle-navigation-1280.png'), fullPage: true })

  if (errors.length) throw new Error(`Page errors: ${errors.join(' | ')}`)
  console.log(JSON.stringify({ output, profileWidths: [1280, 760, 390], matrix, scheduledBefore, scheduledAfter, activeText }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
