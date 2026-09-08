import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch290-analytics'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })
const entry = `
import { createApp, h } from 'vue'
import Panel from '/src/l12/site/AdminCardAnalyticsPanel.vue'
import { adminApi } from '/src/l12/platform.ts'
import '/src/style.css'
const coverage={schemaVersion:2,exactFacts:90,inferredFacts:0,partialFacts:0,exactDeckSnapshots:40,inferredDeckSnapshots:0,metrics:[],limitations:['合成验证数据；描述性关联不代表因果。']}
const uncertainty={status:'unknown',method:'none',low:null,high:null,reason:'重复玩家'}
const item={cardId:'S01-0101',sampleSize:40,eligibleSampleSize:80,includedMatches:40,averageQuantity:2,inclusionRate:.5,wins:24,winRate:.6,winRateConfidence:{low:.45,high:.73},baselineWinRate:.4,baselineWinRateConfidence:{low:.2,high:.5},winRateDelta:.2,winRateDeltaConfidence:{low:.05,high:.3},drawnMatches:30,playedMatches:20,drawnSamples:30,playedSamples:20,activatedSamples:19,settledSamples:18,resolvedSamples:16,negatedSamples:1,fizzledSamples:1,activatedCount:32,resolvedCount:29,negatedCount:1,fizzledCount:2,coverage,sampleStructure:{participantSamples:40,distinctMatches:40,distinctPlayers:3,knownPlayerSamples:40,anonymousPlayerSamples:0,maximumPlayerContribution:30,maximumPlayerContributionRate:.75,dependencyStatus:'dependent',uncertainty},comparison:{carriedSamples:32,comparisonSamples:35,insufficientStrata:2,excludedIncludedSamples:8,winRate:.52,delta:.08,weighting:'included-sample',uncertainty}}
window.analyticsCalls=[]
adminApi.cardAnalytics=async query=>{window.analyticsCalls.push(query);return {items:new URLSearchParams(location.search).has('empty')?[]:[item],total:1,summary:{eligibleMatches:80,sampleSize:80,coverage}}}
adminApi.cardAnalyticsDetail=async()=>({summary:item,breakdowns:[{dimension:'effect-version',value:'l12-engine/abcdef123456789012345678901234567890/abcdefgh1234567812345678',sampleSize:40,eligibleSampleSize:80,wins:24,winRate:.6,winRateConfidence:{low:.45,high:.73},baselineWinRate:.5,winRateDelta:.1}],quantityDistribution:[{quantity:2,sampleSize:40,wins:24,winRate:.6}],turnDistribution:[],matchups:[],recentMatches:[],coverage})
createApp({render:()=>h('main',{class:'fixture-scroll',style:{height:'100vh',overflow:'auto'}},[h(Panel)])}).mount('#app')
`
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'ranked-analytics-fixture', resolveId(id) { if (id === '/__analytics__.js') return id },
  load(id) { if (id === '/__analytics__.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => {
    if (req.url?.startsWith('/__analytics__?')) {
      res.setHeader('Content-Type', 'text/html'); res.end('<div id="app"></div><script type="module" src="/__analytics__.js"></script>'); return
    }
    next()
  }) },
}] })
let browser
const results = []
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const width of [1440, 1000, 390]) {
    const page = await browser.newPage({ viewport: { width, height: 1000 } })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__analytics__?fixture=1')
    await page.getByRole('heading', { name: '样本可靠性', exact: true }).waitFor()
    assert.equal(await page.locator('select option[value="friendly"]').count(), 0)
    assert.equal(await page.getByLabel('卡效版本', { exact: true }).inputValue(), 'current')
    assert.match(await page.locator('.card-heading').innerText(), /尚不能确认方向/)
    assert.equal(await page.locator('.card-heading em').getAttribute('data-tone'), 'neutral')
    assert.equal(await page.evaluate(() => window.analyticsCalls[0].mode), 'ranked')
    assert.equal(await page.evaluate(() => window.analyticsCalls[0].effectVersion), 'current')
    const overflow = await page.evaluate(() => {
      const host = document.querySelector('.fixture-scroll')
      return document.documentElement.scrollWidth > innerWidth + 2 || host.scrollWidth > host.clientWidth + 2
    })
    await page.screenshot({ path: path.join(out, 'analytics-' + width + '.png'), fullPage: true })
    assert.equal(overflow, false, 'Dashboard has horizontal page overflow at ' + width)
    await page.getByRole('heading', { name: '样本可靠性', exact: true }).scrollIntoViewIfNeeded()
    await page.screenshot({ path: path.join(out, 'analytics-detail-' + width + '.png') })
    await page.getByRole('heading', { name: '数据质量与覆盖', exact: true }).scrollIntoViewIfNeeded()
    const bottomVisible = await page.getByRole('heading', { name: '数据质量与覆盖', exact: true })
      .evaluate(element => { const rect = element.getBoundingClientRect(); return rect.top >= 0 && rect.bottom <= innerHeight })
    assert.equal(bottomVisible, true, 'Lower dashboard panels must remain reachable by scrolling')
    await page.screenshot({ path: path.join(out, 'analytics-quality-' + width + '.png') })
    assert.deepEqual(errors, [])
    results.push({ width, overflow, errors })
    await page.close()
  }
  const empty = await browser.newPage({ viewport: { width: 1000, height: 900 } })
  await empty.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__analytics__?empty=1')
  await empty.getByText('排位数据不足，尚无卡牌达到当前样本门槛').waitFor()
  await empty.screenshot({ path: path.join(out, 'analytics-empty.png'), fullPage: true })
  console.log('Ranked analytics: current-version default, no other modes, dependency warning, empty state and 3 viewports passed.')
} finally {
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(results, null, 2))
  await browser?.close(); await server.close()
}
