import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/ranked-season-rules')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import BattleHubPage from '/src/l12/site/BattleHubPage.vue'
import {authState,platformState} from '/src/l12/platform.ts'
import '/src/style.css'
authState.initialized=true;authState.verified=true;platformState.account=null;platformState.token=''
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:BattleHubPage}]})
router.push('/battle');await router.isReady();createApp({render:()=>h(BattleHubPage)}).use(router).mount('#app')
`

const tier = (name, minimum) => ({ name, minimum, baseDelta: 100, winStreakCap: 50,
  lossProtectionCap: 40, ratingGapCap: 30, streakTerminationReward: 20, color: '#d7ba63', icon: '◆' })
const ranked = {
  profile: { accountId: '', username: '游客', seasonId: 'S01', faction: '秩序', sevenValue: 1234,
    displayValue: '七曜值 1,234', placementPlayed: 5, placementWins: 3, placed: true, wins: 8,
    losses: 4, winStreak: 2, lossStreak: 0, tier: '精英', tierIndex: 2, factionRank: 3,
    titles: [], rankLabel: '精英', masterTitles: [] },
  factionTotals: { 秩序: 12, 混沌: 9, 命运: 7 }, history: [],
  config: { placementMatches: 5, placementMaximum: 29999, broadcastEnabled: true,
    factions: ['order', 'chaos', 'fate'].map((id, index) => ({ id, name: ['秩序', '混沌', '命运'][index],
      color: '#d7ba63', icon: '◆', firstTitle: '派系第一', topFiveTitle: '派系前五',
      tiers: ['初阶', '进阶', '精英', '统领', '冠冕'].map((name, tierIndex) => tier(name, tierIndex * 30000)) })),
    masterTitles: [], timeControl: { totalTimeSeconds: 1500, operationTimeSeconds: 240,
      reconnectGraceSeconds: 240, disasterDecisionSeconds: 60, mulliganDecisionSeconds: 60 },
    broadcast: { displaySeconds: 16, lobbyDelaySeconds: 3, intervalSeconds: 15,
      winStreakThreshold: 5, streakEndedThreshold: 5, minimumTierIndex: 0, winStreakEnabled: true,
      streakEndedEnabled: true, highestTierEnabled: true, factionTitleEnabled: true,
      masterTitleEnabled: true } }
}
const populatedPolicy = { version: 12, season: { id: 'S01', name: '跨年验收赛季', status: 'active',
  startsAt: '2026-10-01T00:00:00Z', endsAt: '2027-01-01T00:00:00Z' },
  disasterCardIds: ['S01-DS01', 'S01-DS10'],
  cardRestrictions: [
    { cardId: 'S01-0001', maxCopies: 1, reason: '赛季限制' },
    { cardId: 'S01-0010', masterId: 'S01-01M1', maxCopies: 0, reason: '主宰专属限制' },
  ], defaultPresetDeckIds: [], matchModes: [], featureFlags: {},
  defaultRoomConfig: { matchModeId: 'friendly', spectating: 'public', handVisibility: 'request', disasterMode: 'all' },
  maintenance: { enabled: false, message: '', advanceBroadcastHours: 0, expectedDurationHours: 0,
    effectiveState: 'open', entryBlocked: false }, announcements: [] }

const server = await createServer({ root, configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-ranked-season-rules'), server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'ranked-season-rules-fixture',
    resolveId(id) { if (id === '/__ranked_season_rules__.js') return id },
    load(id) { if (id === '/__ranked_season_rules__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (request.url?.match(/^\/__ranked_season_rules__(\?|$)/)) {
        response.setHeader('Content-Type', 'text/html')
        response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__ranked_season_rules__.js"></script>')
        return
      }
      next()
    }) }
  }] })

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, timezoneId: 'Asia/Shanghai' })
  const page = await context.newPage()
  const pageErrors = []; const consoleErrors = []; const failedResponses = []
  page.on('pageerror', error => pageErrors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()) })
  page.on('response', response => { if (response.status() >= 400) failedResponses.push(`${response.status()} ${response.url()}`) })
  await page.route('**/card-assets/card-assets.manifest.json*', route => route.fulfill({ json: {
    schemaVersion: 3, catalogVersion: 'fixture', assetVersion: 'fixture', basePath: '/assets/l12', cards: {
      'S01-DS01': { cardId: 'S01-DS01', contentHash: 'fixture', width: 1024, height: 1024,
        orientation: 'landscape', variants: { thumbWebp: 'special/round/S01-DS01.png',
          boardWebp: 'special/round/S01-DS01.png', detailWebp: 'special/round/S01-DS01.png' } },
      'S01-DS10': { cardId: 'S01-DS10', contentHash: 'fixture', width: 1024, height: 1024,
        orientation: 'landscape', variants: { thumbWebp: 'special/round/S01-DS10.png',
          boardWebp: 'special/round/S01-DS10.png', detailWebp: 'special/round/S01-DS10.png' } },
      'S01-0001': { cardId: 'S01-0001', contentHash: 'fixture', width: 744, height: 1039,
        orientation: 'portrait', variants: { thumbWebp: 'card-back-official.png',
          boardWebp: 'card-back-official.png', detailWebp: 'card-back-official.png' } },
      'S01-0010': { cardId: 'S01-0010', contentHash: 'fixture', width: 744, height: 1039,
        orientation: 'portrait', variants: { thumbWebp: 'card-back-official.png',
          boardWebp: 'card-back-official.png', detailWebp: 'card-back-official.png' } },
    },
  } }))
  await page.route('**/api/**', route => {
    const pathname = new URL(route.request().url()).pathname
    if (pathname === '/api/operations/effective-policy') {
      const empty = page.url().includes('empty=1')
      return route.fulfill({ json: empty ? { ...populatedPolicy, disasterCardIds: [], cardRestrictions: [] } : populatedPolicy })
    }
    if (pathname === '/api/ranked/me') return route.fulfill({ json: ranked })
    if (pathname === '/api/ranked/broadcasts/settings') return route.fulfill({ json: ranked.config.broadcast })
    if (pathname === '/api/ranked/broadcasts') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404, json: { message: `unexpected ${pathname}` } })
  })

  const results = []
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__ranked_season_rules__`)
    await page.getByRole('button', { name: '排位规则' }).click()
    const dialog = page.getByRole('dialog', { name: '排位规则' })
    await dialog.getByText('赛季持续周期').waitFor()
    assert.match(await dialog.textContent(), /2026\/10\/01.*08:00.*2027\/01\/01.*08:00.*UTC\+8/s)
    await dialog.getByRole('button', { name: '查看黯陨晨星卡牌详情' }).click()
    await page.getByRole('dialog', { name: '黯陨晨星' }).waitFor()
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    await dialog.getByRole('button', { name: '查看黑胡子蒂奇卡牌详情' }).click()
    await page.getByRole('dialog', { name: '黑胡子蒂奇' }).waitFor()
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    const metrics = await page.evaluate(() => {
      const ruleDialog = document.querySelector('.ranked-rules-modal').getBoundingClientRect()
      const cards = [...document.querySelectorAll('.season-card-grid button')].map(item => item.getBoundingClientRect())
      return { documentOverflow: document.documentElement.scrollWidth > innerWidth + 1,
        bodyOverflow: document.body.scrollWidth > innerWidth + 1,
        dialogInBounds: ruleDialog.left >= -1 && ruleDialog.right <= innerWidth + 1,
        cardWidths: cards.map(card => card.width), cardHeights: cards.map(card => card.height) }
    })
    assert.equal(metrics.documentOverflow, false)
    assert.equal(metrics.bodyOverflow, false)
    assert.equal(metrics.dialogInBounds, true)
    assert.ok(metrics.cardWidths.every(width => width >= 80))
    assert.ok(metrics.cardHeights.every(height => height >= 44))
    await page.screenshot({ path: path.join(output, `ranked-season-rules-${viewport.width}x${viewport.height}.png`), fullPage: true })
    results.push({ ...viewport, ...metrics })
  }

  await page.goto(`http://127.0.0.1:${port}/__ranked_season_rules__?empty=1`)
  await page.getByRole('button', { name: '排位规则' }).click()
  const emptyDialog = page.getByRole('dialog', { name: '排位规则' })
  assert.equal(await emptyDialog.getByText('本赛季天灾', { exact: true }).count(), 0)
  assert.equal(await emptyDialog.getByText('本赛季禁限卡', { exact: true }).count(), 0)
  assert.deepEqual(pageErrors, [])
  assert.deepEqual(consoleErrors, [], `failed responses: ${failedResponses.join(', ')}`)
  console.log(JSON.stringify({ passed: true, results }, null, 2))
} finally {
  if (browser) await browser.close()
  await server.close()
}
