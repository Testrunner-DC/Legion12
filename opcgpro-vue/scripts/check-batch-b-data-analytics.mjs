import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || path.join(root, 'artifacts', 'batch-b-data-analytics')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = `
import { createApp, h, ref } from 'vue'
import GlobalPanel from '/src/l12/site/AdminGlobalDataPanel.vue'
import MasterPanel from '/src/l12/site/AdminMasterAnalyticsPanel.vue'
import { adminApi } from '/src/l12/platform.ts'
import '/src/style.css'
const coverage={schemaVersion:1,supportedKinds:[],exactFacts:100,inferredFacts:0,partialFacts:0,exactDeckSnapshots:80,inferredDeckSnapshots:0,privateDuringActiveMatch:false,metrics:[],limitations:[]}
const card={cardId:'S01-0101',sampleSize:80,eligibleSampleSize:120,includedMatches:40,averageQuantity:2,inclusionRate:.66,wins:48,winRate:.6,winRateConfidence:{low:.49,high:.70},exactDrawCoverageSamples:80,gihSamples:40,gihWins:26,gihWinRate:.65,gihWinRateConfidence:{low:.49,high:.78},gnsSamples:40,gnsWins:18,gnsWinRate:.45,gnsWinRateConfidence:{low:.31,high:.60},inHandWinRateDelta:.2,inHandWinRateDeltaConfidence:{low:.01,high:.38},drawnMatches:40,playedMatches:30,drawnSamples:40,playedSamples:30,activatedSamples:20,settledSamples:20,resolvedSamples:18,negatedSamples:1,fizzledSamples:1,activatedCount:20,resolvedCount:18,negatedCount:1,fizzledCount:1,coverage}
adminApi.globalAnalytics=async()=>({fromDate:'2026-08-25',toDate:'2026-09-23',days:Array.from({length:30},(_,i)=>({date:'2026-09-'+String(i+1).padStart(2,'0'),dailyActiveUsers:20+i,weeklyActiveUsers:80+i,monthlyActiveUsers:150+i,dailyMatches:12+i,weeklyMatches:90+i,monthlyMatches:300+i,averageOnline:5.2,peakOnline:12,peakOnlineAt:'2026-09-23T12:30:00Z',newUsers:2,returningUsers:18+i,pageViews:200+i*3})),pageViews:[{path:'/cards',views:900},{path:'/battle',views:700},{path:'/decks',views:500}]})
adminApi.masterAnalytics=async query=>({items:[{masterId:'S01-0001',participantSamples:120,distinctMatches:60,distinctDecks:8,usageRate:.55,deckShare:.5,wins:68,winRate:.567,winRateConfidence:{low:.48,high:.65},averageDurationSeconds:510,firstSamples:60,firstWinRate:.58,secondSamples:60,secondWinRate:.55},{masterId:'S01-0002',participantSamples:98,distinctMatches:49,distinctDecks:6,usageRate:.45,deckShare:.5,wins:47,winRate:.48,winRateConfidence:{low:.38,high:.58},averageDurationSeconds:470,firstSamples:49,firstWinRate:.51,secondSamples:49,secondWinRate:.45}],matchups:[{masterId:'S01-0001',opponentMasterId:'S01-0002',samples:60,wins:34,winRate:.567},{masterId:'S01-0002',opponentMasterId:'S01-0001',samples:60,wins:26,winRate:.433}],trend:query.masterId?Array.from({length:14},(_,i)=>({date:'2026-09-'+String(i+1).padStart(2,'0'),samples:30+i,wins:17+i,winRate:(17+i)/(30+i)})):[],selectedMasterId:query.masterId||null,popularDecks:query.masterId?[{signature:'qa',samples:42,wins:25,winRate:.595,cards:[{cardId:'S01-0101',quantity:3,section:'main'},{cardId:'S01-0102',quantity:2,section:'main'}]}]:[],cards:query.masterId?{items:[card],total:1,page:1,pageSize:20,summary:{eligibleMatches:60,sampleSize:120,coverage}}:null})
const App={setup(){const view=ref('global');return()=>h('main',{style:{minHeight:'100vh',background:'#071016',padding:'12px'}},[h('nav',[h('button',{onClick:()=>view.value='global'},'全局数据'),h('button',{onClick:()=>view.value='master'},'主宰数据')]),view.value==='global'?h(GlobalPanel):h(MasterPanel)])}}
createApp(App).mount('#app')
`
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'batch-b-analytics-fixture', resolveId(id) { if (id === '/__batch_b__.js') return id },
  load(id) { if (id === '/__batch_b__.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => {
    if (req.url?.match(/^\/__batch_b__(?:\?|$)/)) { res.setHeader('Content-Type', 'text/html'); res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__batch_b__.js"></script>'); return }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const width of [1280, 390]) {
    const page = await browser.newPage({ viewport: { width, height: 900 } })
    const errors = []; page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.pathname === '/api/operations/effective-policy') return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ season: { id: 'S1', name: '测试赛季' } }) })
      return url.hostname === '127.0.0.1' ? route.continue() : route.abort()
    })
    await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__batch_b__')
    await page.getByRole('heading', { name: '全局数据' }).waitFor()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2), false)
    await page.screenshot({ path: path.join(out, 'global-' + width + '.png'), fullPage: true })
    await page.getByRole('button', { name: '主宰数据' }).click()
    await page.getByRole('heading', { name: '主宰总览' }).waitFor()
    await page.locator('.master-list>button').first().click()
    await page.getByRole('heading', { name: '热门构筑' }).waitFor()
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 2), false)
    await page.screenshot({ path: path.join(out, 'master-' + width + '.png'), fullPage: true })
    assert.deepEqual(errors, [])
    await page.close()
  }
  console.log('Batch B global/master analytics viewports and drill-down passed.')
} finally { await browser?.close(); await server.close() }
