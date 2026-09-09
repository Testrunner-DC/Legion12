import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch302-analytics'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = `
import { createApp, h } from 'vue'
import Panel from '/src/l12/site/AdminCardAnalyticsPanel.vue'
import { adminApi } from '/src/l12/platform.ts'
import '/src/style.css'
const coverage={schemaVersion:2,supportedKinds:[],exactFacts:90,inferredFacts:0,partialFacts:0,exactDeckSnapshots:40,inferredDeckSnapshots:0,privateDuringActiveMatch:false,metrics:[],limitations:['合成验证数据；描述性关联不代表因果。']}
const uncertainty={status:'available',method:'wilson',low:.02,high:.14,reason:null}
const makeItem=(cardId,index)=>({cardId,sampleSize:index,eligibleSampleSize:80,includedMatches:index,averageQuantity:2,inclusionRate:index/100,wins:Math.ceil(index/2),winRate:.5,winRateConfidence:{low:.35,high:.65},baselineWinRate:.42,baselineWinRateConfidence:{low:.3,high:.55},winRateDelta:.08,winRateDeltaConfidence:{low:.02,high:.14},drawnMatches:index,playedMatches:index,drawnSamples:index,playedSamples:index,activatedSamples:index,settledSamples:index,resolvedSamples:index,negatedSamples:0,fizzledSamples:0,activatedCount:index,resolvedCount:index,negatedCount:0,fizzledCount:0,coverage,sampleStructure:{participantSamples:index,distinctMatches:index,distinctPlayers:index,knownPlayerSamples:index,anonymousPlayerSamples:0,maximumPlayerContribution:1,maximumPlayerContributionRate:1/index,dependencyStatus:'available',uncertainty},comparison:{carriedSamples:index,comparisonSamples:index,insufficientStrata:0,excludedIncludedSamples:0,winRate:.42,delta:.08,weighting:'included-sample',uncertainty},usage:{metrics:['draw','play','activation','settlement'].map(metric=>({metric,observedParticipantSamples:index,eventCount:index,exactFacts:index,inferredFacts:0,partialFacts:0,eligibleSamples:index,coverageStatus:'complete'}))}})
const listItems=Array.from({length:13},(_,index)=>makeItem('QA-'+String(index+1).padStart(3,'0'),index+31))
window.listCalls=[];window.detailCalls=[]
adminApi.cardAnalytics=async query=>{window.listCalls.push({...query});const offset=query.cursor?7:0;return {items:listItems.slice(offset,query.cursor?13:7),total:13,nextCursor:query.cursor?null:'page-2',summary:{eligibleMatches:80,sampleSize:80,coverage}}}
adminApi.cardAnalyticsDetail=async(cardId,query)=>{window.detailCalls.push({cardId,...query});const item=makeItem(cardId,40);return {summary:item,breakdowns:[{dimension:'season',value:query.seasonId||'all',sampleSize:40,eligibleSampleSize:80,wins:20,winRate:.5,winRateConfidence:{low:.35,high:.65},baselineWinRate:.42,baselineWinRateConfidence:{low:.3,high:.55},winRateDelta:.08,winRateDeltaConfidence:{low:.02,high:.14}}],quantityDistribution:[{quantity:2,sampleSize:40,wins:20,winRate:.5}],turnDistribution:[{turn:2,firstDrawSamples:20,firstPlaySamples:10}],matchups:[],recentMatches:[],coverage}}
createApp({render:()=>h('main',{class:'fixture',style:{width:'100%',minHeight:'100vh'}},[h(Panel)])}).mount('#app')
`

const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'batch302-analytics-fixture',
  resolveId(id) { if (id === '/__batch302__.js') return id },
  load(id) { if (id === '/__batch302__.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => {
    if (req.url?.match(/^\/__batch302__(?:\?|$)/)) {
      res.setHeader('Content-Type', 'text/html')
      res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__batch302__.js"></script>')
      return
    }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.pathname === '/api/operations/effective-policy') return route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({ version: 1, season: { id: 'S302', name: '合成赛季', status: 'active', startsAt: '2026-09-01T00:00:00Z' }, disasterCardIds: [], matchModes: [], defaultRoomConfig: { matchModeId: 'ranked', spectating: 'public', handVisibility: 'request', disasterMode: 'season' }, seasonDisasterModeAvailable: true, cardRestrictions: [], defaultPresetDeckIds: [], maintenance: { enabled: false, active: false, entryBlocked: false, status: 'open', message: '', broadcastMessage: '', advanceBroadcastHours: 0, expectedDurationHours: 0 }, announcements: [] }),
    })
    return url.hostname === '127.0.0.1' ? route.continue() : route.abort()
  })
  await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__batch302__')
  await page.getByRole('button', { name: /从 GM 卡牌图鉴选择/ }).waitFor()
  assert.equal(await page.getByText('选择一张卡查看事实仪表盘').count(), 1, 'single-card view must not auto-select a list row')

  await page.getByRole('button', { name: /从 GM 卡牌图鉴选择/ }).click()
  await page.getByRole('dialog', { name: '选择要分析的卡牌' }).waitFor()
  await page.locator('.picker-card-actions').first().getByRole('button', { name: '选择', exact: true }).click()
  await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()
  await page.screenshot({ path: path.join(out, 'single-desktop.png'), fullPage: true })

  const master = page.getByLabel('1. 使用方主宰')
  await master.selectOption({ index: 1 })
  await page.getByText('选择一张卡查看事实仪表盘').waitFor()
  await page.getByRole('button', { name: '查询这张卡' }).click()
  await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()

  await page.getByRole('button', { name: '全部', exact: true }).click()
  await page.getByText('选择一张卡查看事实仪表盘').waitFor()
  await page.getByRole('button', { name: /本赛季 · 合成赛季/ }).click()
  await page.getByRole('button', { name: '查询这张卡' }).click()
  await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()
  const seasonCall = await page.evaluate(() => window.detailCalls.at(-1))
  assert.equal(seasonCall.seasonId, 'S302')
  assert.equal(seasonCall.from, '')
  assert.equal(seasonCall.to, '')

  await page.getByRole('button', { name: '近 7 天', exact: true }).click()
  await page.getByRole('button', { name: '查询这张卡' }).click()
  await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()
  const sevenDayCall = await page.evaluate(() => window.detailCalls.at(-1))
  assert.equal((new Date(sevenDayCall.to + 'T00:00:00') - new Date(sevenDayCall.from + 'T00:00:00')) / 86400000, 6)
  assert.equal(sevenDayCall.seasonId, '')

  await page.getByRole('button', { name: '卡牌数据清单', exact: true }).click()
  await page.getByText('完整筛选清单').waitFor()
  await page.waitForFunction(() => window.listCalls.length >= 2)
  const listCalls = await page.evaluate(() => window.listCalls.slice(-2))
  assert.equal(listCalls[0].cursor, undefined)
  assert.equal(listCalls[1].cursor, 'page-2')
  assert.match(await page.locator('.card-row').first().innerText(), /QA-013/, 'default sort must cover the complete two-page result')
  assert.equal(await page.locator('.card-row').count(), 10)
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  assert.equal(await page.locator('.card-row').count(), 3)
  await page.screenshot({ path: path.join(out, 'list-desktop.png'), fullPage: true })

  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: '单卡仪表盘', exact: true }).click()
  await page.getByRole('button', { name: '查询这张卡' }).click()
  await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2)
  assert.equal(overflow, false, 'narrow single-card dashboard must not overflow the page')
  await page.screenshot({ path: path.join(out, 'single-narrow.png'), fullPage: true })
  assert.deepEqual(errors, [])
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ detailCalls: await page.evaluate(() => window.detailCalls), listCalls: await page.evaluate(() => window.listCalls), overflow, errors }, null, 2))
  console.log('BATCH302 analytics picker, stale-state, ranges, complete sorting, pagination and viewports passed.')
} finally {
  await browser?.close()
  await server.close()
}
