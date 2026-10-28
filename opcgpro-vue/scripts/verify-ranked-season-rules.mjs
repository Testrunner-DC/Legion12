import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT
const cacheDir = process.env.L12_QA_CACHE
assert.ok(output && cacheDir && process.env.L12_PWSH, 'explicit governed output/cache and initialized PowerShell are required')
execFileSync(process.env.L12_PWSH, ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_STORAGE_GUARD; @($env:L12_QA_OUT,$env:L12_QA_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) | ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "shared ranked rule cards acceptance" | Out-Null}',
], { env: { ...process.env, L12_STORAGE_GUARD: path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1') }, windowsHide: true, stdio: 'pipe' })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
assert.ok(!fs.existsSync(output) || !fs.readdirSync(output).length, 'do not overwrite previous acceptance evidence')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import BattleHubPage from '/src/l12/site/BattleHubPage.vue'
import TournamentAccountDetail from '/src/l12/site/TournamentAccountDetail.vue'
import TournamentPublicDetail from '/src/l12/site/TournamentPublicDetail.vue'
import RuleCenterPage from '/src/l12/site/RuleCenterPage.vue'
import {authState,platformState} from '/src/l12/platform.ts'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
authState.initialized=true;authState.verified=true;platformState.account=null;platformState.token=''
const surface=new URLSearchParams(location.search).get('surface')||'ranked'
if(new URLSearchParams(location.search).get('long')==='1'){
  const catalog=await loadDeckCatalog();const master=catalog.find(card=>card.id==='S01-01M1')
  if(!master)throw new Error('fixture master missing')
  master.nameZh='阿尔托莉雅·潘德拉贡（主宰专属规则完整名称）'
}
const Component=({account:TournamentAccountDetail,public:TournamentPublicDetail,construction:RuleCenterPage})[surface]||BattleHubPage
if(surface==='account'){platformState.account={id:'rule-owner',username:'主办者',permissions:[]};platformState.token='synthetic-rule-session'}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/battle/tournaments/:code',component:Component},{path:'/:pathMatch(.*)*',component:Component}]})
router.push(surface==='construction'?'/rules?tab=construction':surface==='ranked'?'/battle':'/battle/tournaments/RULE-QA');await router.isReady();createApp({render:()=>h(Component)}).use(router).mount('#app')
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

const initialTournament = () => ({ id: 'rule-tournament', code: 'RULE-QA', version: 1, name: '共享规则验收赛事',
  organizerAccountId: 'rule-owner', organizerName: '主办者', status: 'registration', phase: 'registration-open',
  registrationOpen: true, registrationVisibility: 'public', format: 'swiss', maxPlayers: 16,
  operationMode: 'managed', referees: [], rounds: [], participants: [], finalStandings: [], finalSwissStandings: [],
  postponements: [], rules: { ruleset: '现行规则', disasterMode: 'season', deckVisibility: 'after',
    disasterCardIds: populatedPolicy.disasterCardIds, cardRestrictions: populatedPolicy.cardRestrictions,
    timeControl: ranked.config.timeControl, lateGraceMinutes: 5, banList: '' },
  checkInMinutes: 5, lateGraceMinutes: 5, timeControl: ranked.config.timeControl,
  counts: { registered: 0, pendingCheckIn: 0, checkedIn: 0, active: 0, waitlisted: 0, removed: 0 } })
let tournament = initialTournament()
const privateRequests = []

const server = await createServer({ root, configLoader: 'runner',
  cacheDir, server: { host: '127.0.0.1', port: 0 },
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
    if (pathname.startsWith('/api/tournaments/code/') || pathname.startsWith('/api/public/tournaments/code/')) return route.fulfill({ json: tournament })
    if (pathname === '/api/tournaments/rule-tournament/registrations') {
      tournament = { ...tournament, version: 2, participants: [{ accountId: 'rule-owner', username: '主办者', seed: 1,
        waitlisted: false, removed: false, dropped: false, deck: null }],
      counts: { ...tournament.counts, registered: 1, pendingCheckIn: 1 } }
      return route.fulfill({ json: tournament })
    }
    if (pathname.startsWith('/api/decks')) privateRequests.push(pathname)
    if (pathname === '/api/content') return route.fulfill({ json: { values: {} } })
    return route.fulfill({ status: 404, json: { message: `unexpected ${pathname}` } })
  })

  const results = []
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__ranked_season_rules__?long=1`)
    await page.getByRole('button', { name: '排位规则' }).click()
    const dialog = page.getByRole('dialog', { name: '排位规则' })
    await dialog.getByText('赛季持续周期').waitFor()
    assert.match(await dialog.textContent(), /2026\/10\/01.*08:00.*2027\/01\/01.*08:00.*UTC\+8/s)
    await dialog.getByRole('button', { name: '查看黯陨晨星卡牌详情' }).click()
    await page.getByRole('dialog', { name: '黯陨晨星' }).waitFor()
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    const scoped = dialog.locator('.restriction-group').filter({ has: page.locator('.scope-master') })
    assert.equal(await scoped.count(), 1)
    assert.match(await scoped.innerText(), /S01-01M1[\s\S]*S01-0010[\s\S]*禁止携带（0 张）/)
    assert.equal(await scoped.getByText('S01-0001', { exact: true }).count(), 0)
    const masterButton = scoped.getByRole('button', { name: /主宰详情$/ })
    const masterName = (await masterButton.locator('b').innerText()).replace(/^主宰：/, '')
    await masterButton.click()
    await page.getByRole('dialog', { name: masterName, exact: true }).waitFor()
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    await dialog.getByRole('button', { name: '查看黑胡子蒂奇卡牌详情' }).click()
    await page.getByRole('dialog', { name: '黑胡子蒂奇' }).waitFor()
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    const metrics = await page.evaluate(() => {
      const ruleDialog = document.querySelector('.ranked-rules-modal').getBoundingClientRect()
      const cards = [...document.querySelectorAll('.rules-card-grid button')].map(item => item.getBoundingClientRect())
      return { documentOverflow: document.documentElement.scrollWidth > innerWidth + 1,
        bodyOverflow: document.body.scrollWidth > innerWidth + 1,
        dialogInBounds: ruleDialog.left >= -1 && ruleDialog.right <= innerWidth + 1,
        cardWidths: cards.map(card => card.width), cardHeights: cards.map(card => card.height),
        masterLabels: [...document.querySelectorAll('.scope-master b')].map(node => ({ text: node.textContent, client: node.clientWidth, scroll: node.scrollWidth })) }
    })
    assert.equal(metrics.documentOverflow, false)
    assert.equal(metrics.bodyOverflow, false)
    assert.equal(metrics.dialogInBounds, true)
    assert.ok(metrics.cardWidths.length >= 4 && metrics.cardWidths.every(width => width >= 80))
    assert.ok(metrics.cardHeights.every(height => height >= 44))
    assert.ok(metrics.masterLabels.every(label => label.scroll <= label.client + 1), `master label must wrap: ${JSON.stringify(metrics.masterLabels)}`)
    await page.screenshot({ path: path.join(output, `ranked-season-rules-${viewport.width}x${viewport.height}.png`), fullPage: true })
    results.push({ ...viewport, ...metrics })
  }

  await page.goto(`http://127.0.0.1:${port}/__ranked_season_rules__?empty=1`)
  await page.getByRole('button', { name: '排位规则' }).click()
  const emptyDialog = page.getByRole('dialog', { name: '排位规则' })
  await emptyDialog.getByText('尚未提供天灾卡池，请以已发布的规则为准。', { exact: true }).waitFor()
  await emptyDialog.getByText('无额外禁限卡规则，仍须遵守基础构筑规则。', { exact: true }).waitFor()
  assert.equal(await emptyDialog.locator('.rules-card-grid button').count(), 0)
  for (const surface of ['account', 'public', 'construction']) {
    for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
      tournament = initialTournament(); await page.setViewportSize(viewport)
      await page.goto(`http://127.0.0.1:${port}/__ranked_season_rules__?surface=${surface}&long=1`)
      if (surface === 'account') {
        const join = page.getByRole('button', { name: '报名（暂不提交牌库）', exact: true })
        await join.waitFor(); assert.equal(await page.getByRole('button', { name: '退出赛事', exact: true }).count(), 0)
        await join.click(); await page.getByRole('button', { name: '退出赛事', exact: true }).waitFor()
        assert.match(await page.locator('.tournament-participant-counts').innerText(), /已报名 1 \/ 16[\s\S]*待签到 1[\s\S]*已签到 0/)
      }
      if (surface !== 'construction') await page.getByRole('button', { name: '规则', exact: true }).click()
      const summary = page.locator('[data-ui-contract="rules-card-summary"]')
      await summary.getByRole('button', { name: '查看黯陨晨星卡牌详情' }).waitFor()
      const scoped = summary.locator('.restriction-group').filter({ has: page.locator('.scope-master') })
      assert.match(await scoped.innerText(), /S01-01M1[\s\S]*S01-0010[\s\S]*禁止携带（0 张）/)
      await summary.getByRole('button', { name: '查看黯陨晨星卡牌详情' }).click()
      await page.getByRole('dialog', { name: '黯陨晨星' }).waitFor()
      await page.getByRole('button', { name: '关闭卡牌详情' }).click()
      const geometry = await page.evaluate(() => ({
        overflow: document.documentElement.scrollWidth > innerWidth + 1,
        masterLabels: [...document.querySelectorAll('.scope-master b')].map(node => ({ text: node.textContent, client: node.clientWidth, scroll: node.scrollWidth })),
        controls: [...document.querySelectorAll('.rules-card-grid button,.scope-master')].map(node => {
          const r = node.getBoundingClientRect(); return { left: r.left, right: r.right, width: r.width, height: r.height }
        }),
      }))
      assert.equal(geometry.overflow, false)
      assert.ok(geometry.masterLabels.every(label => label.scroll <= label.client + 1), `master label must wrap: ${JSON.stringify(geometry.masterLabels)}`)
      assert.ok(geometry.controls.length >= 5 && geometry.controls.every(r => r.left >= -1 && r.right <= viewport.width + 1 && r.width >= 44 && r.height >= 44))
      await page.screenshot({ path: path.join(output, `shared-rules-${surface}-${viewport.width}x${viewport.height}.png`), fullPage: true })
      results.push({ surface, ...viewport, ...geometry })
    }
  }
  assert.deepEqual(privateRequests, [], 'rule views and registration must not expand private decks or read the directory')
  assert.deepEqual(pageErrors, [])
  assert.deepEqual(consoleErrors, [], `failed responses: ${failedResponses.join(', ')}`)
  const report = { passed: true, results, pageErrors, consoleErrors,
    scope: 'actual ranked dialog, authenticated/public tournament detail and rule-center construction tab over shared RulesCardSummary and CatalogCardDetails; synthetic APIs; local browser, not physical-device or live-service acceptance' }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
} finally {
  if (browser) await browser.close()
  await server.close()
}
