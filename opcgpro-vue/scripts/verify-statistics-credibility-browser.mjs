import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/statistics-credibility-browser')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import PublicDeckDetailPage from '/src/l12/site/PublicDeckDetailPage.vue'
import AdminCardAnalyticsPanel from '/src/l12/site/AdminCardAnalyticsPanel.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {adminApi,platformState,publicDeckApi,rankedApi} from '/src/l12/platform.ts'
import '/src/style.css'

const fixtureMode=new URL(location.href).searchParams.get('fixture')||'rankings'
const originalFetch=window.fetch.bind(window)
const policy={version:1,season:{id:'S-CURRENT',name:'当前赛季',status:'active',startsAt:'2026-10-01T00:00:00Z'},disasterCardIds:[],matchModes:[],defaultRoomConfig:{matchModeId:'ranked',spectating:'public',handVisibility:'request',disasterMode:'season'},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]}
window.fetch=(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.includes('/api/operations/effective-policy'))return Promise.resolve(new Response(JSON.stringify(policy),{status:200,headers:{'Content-Type':'application/json'}}))
 if(url.includes('/api/alternate-arts'))return Promise.resolve(new Response('[]',{status:200,headers:{'Content-Type':'application/json'}}))
 return originalFetch(input,init)
}

const catalog=await loadDeckCatalog()
const pixel='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 14"><rect width="10" height="14" fill="#24333a"/></svg>')
catalog.forEach(card=>{card.imageUrl=pixel})
const masters=catalog.filter(card=>card.cardType==='master').slice(0,3)
window.expectedMasterOrder=[masters[0].nameZh,masters[2].nameZh,masters[1].nameZh]
const preset=(await loadOfficialPresetDecks())[0]
platformState.account={id:'qa-viewer',username:'统计验收者',role:'player',createdAt:'2026-01-01T00:00:00Z',publicHistory:false}

const masterStats=[
 {rank:1,masterId:masters[0].id,masterName:masters[0].nameZh,games:40,wins:24,losses:16,winRate:60,usageRate:70,firstGames:32,firstWins:19,firstWinRate:59.4,secondGames:8,secondWins:5,secondWinRate:62.5,strongestPlayer:'甲',title:'最强'+masters[0].nameZh},
 {rank:2,masterId:masters[1].id,masterName:masters[1].nameZh,games:5,wins:5,losses:0,winRate:100,usageRate:10,firstGames:1,firstWins:1,firstWinRate:100,secondGames:4,secondWins:4,secondWinRate:100,strongestPlayer:null,title:null},
 {rank:3,masterId:masters[2].id,masterName:masters[2].nameZh,games:8,wins:0,losses:8,winRate:0,usageRate:20,firstGames:4,firstWins:0,firstWinRate:0,secondGames:4,secondWins:0,secondWinRate:0,strongestPlayer:null,title:null},
]
const rangeMetadata={
 season:{fromUtc:'2026-10-01T00:00:00Z',untilUtc:'2026-10-06T00:00:00Z',seasonId:'S-CURRENT',seasonName:'当前赛季'},
 '7d':{fromUtc:'2026-09-28T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z',seasonId:null,seasonName:null},
 '30d':{fromUtc:'2026-09-05T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z',seasonId:null,seasonName:null},
}
window.rankingCalls=[]
rankedApi.leaderboard=async(faction,range)=>{
 window.rankingCalls.push({faction,range})
 return {players:[{rank:1,username:'统计验收者',faction:'秩序',tier:'定级',titles:[],favoriteMasterId:masters[0].id,favoriteMasterName:masters[0].nameZh,displayValue:'七曜值 2100',wins:20,losses:10}],rangeLimited:new URLSearchParams(location.search).has('capped'),analytics:{range,summary:{matches:53,placedPlayers:1,activeMasters:3,updatedAt:'2026-10-05T15:00:00Z'},masters:masterStats,matchups:[
  {masterId:masters[0].id,opponentMasterId:masters[1].id,games:35,wins:14,winRate:40,firstGames:10,firstWins:10,secondGames:25,secondWins:4},
  {masterId:masters[1].id,opponentMasterId:masters[2].id,games:5,wins:5,winRate:100,firstGames:1,firstWins:1,secondGames:4,secondWins:4},
 ],...rangeMetadata[range]}}
}
rankedApi.history=async()=>({honors:[],factionTotals:[]})

const publicFixture={id:'qa-public',publicCode:'qa-public',ownerId:'qa-author',author:'匿名作者',deck:{...preset,specialIds:preset.specialIds||[],updatedAt:'2026-10-05T16:00:00Z'},views:8,likes:2,copies:1,liked:false,createdAt:'2026-10-01T00:00:00Z',updatedAt:'2026-10-05T16:00:00Z',seasonCompliant:true,details:{guide:{buildIdea:'',opening:'',keyCards:'',commonSequence:'',substitutions:''},matchups:[],contentRevision:0,versions:[],matchStatistics:{from:'2026-07-07T16:00:00Z',to:'2026-10-05T16:00:00Z',recentDays:90,games:0,sampleStatus:'insufficient',groups:[]},matchBindingStatus:'insufficient',matchBindingMessage:'样本不足：过去 90 天各主宰组合均不足 3 场，暂不展示胜率。'}}
publicDeckApi.get=async()=>publicFixture
publicDeckApi.recordView=async()=>publicFixture

const coverage={schemaVersion:2,supportedKinds:[],exactFacts:80,inferredFacts:0,partialFacts:0,exactDeckSnapshots:40,inferredDeckSnapshots:0,privateDuringActiveMatch:false,metrics:[],limitations:[]}
const cardItem={cardId:catalog.find(card=>card.cardType!=='master').id,sampleSize:40,eligibleSampleSize:80,includedMatches:40,averageQuantity:2,inclusionRate:.5,wins:24,winRate:.6,winRateConfidence:{low:.45,high:.73},exactDrawCoverageSamples:40,gihSamples:40,gihWins:24,gihWinRate:.6,gihWinRateConfidence:{low:.45,high:.73},gnsSamples:40,gnsWins:20,gnsWinRate:.5,gnsWinRateConfidence:{low:.35,high:.65},inHandWinRateDelta:.1,inHandWinRateDeltaConfidence:{low:.01,high:.19},baselineWinRate:.5,baselineWinRateConfidence:{low:.35,high:.65},winRateDelta:.1,winRateDeltaConfidence:{low:.01,high:.19},drawnMatches:20,playedMatches:18,drawnSamples:20,playedSamples:18,activatedSamples:12,settledSamples:10,resolvedSamples:8,negatedSamples:1,fizzledSamples:1,activatedCount:12,resolvedCount:8,negatedCount:1,fizzledCount:1,coverage,sampleStructure:null,comparison:null,usage:{metrics:[]}}
window.cardCalls=[]
adminApi.cardAnalytics=async query=>{window.cardCalls.push({...query});return {items:[cardItem],total:1,page:1,pageSize:20,summary:{eligibleMatches:40,sampleSize:80,coverage}}}
adminApi.cardAnalyticsDetail=async()=>{throw new Error('本夹具不读取单卡详情')}
const masterReport={items:[{masterId:masters[0].id,participantSamples:40,distinctMatches:40,distinctDecks:8,usageRate:.6,deckShare:.6,wins:24,winRate:.6,winRateConfidence:{low:.45,high:.73},averageDurationSeconds:600,firstSamples:20,firstWinRate:.6,secondSamples:20,secondWinRate:.6}],matchups:[],trend:[],selectedMasterId:null,popularDecks:[],cards:null}
window.masterCalls=[]
adminApi.masterAnalytics=async query=>{window.masterCalls.push({...query});if(window.masterCalls.length>1)throw new Error('合成失败');return masterReport}

const component=fixtureMode==='public'?PublicDeckDetailPage:fixtureMode==='admin'?AdminCardAnalyticsPanel:RankingsPage
const router=createRouter({history:createMemoryHistory(),routes:[{name:'public-deck-detail',path:'/decks/:deckId',component},{path:'/:pathMatch(.*)*',component}]})
await router.push(fixtureMode==='public'?'/decks/qa-public':'/')
await router.isReady()
createApp({render:()=>h(component)}).use(router).mount('#app')
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-statistics-credibility-browser'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'statistics-credibility-browser-fixture',
    resolveId(id) { if (id === '/__statistics_credibility__.js') return id },
    load(id) { if (id === '/__statistics_credibility__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__statistics_credibility__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__statistics_credibility__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?fixture=rankings`)
  await page.locator('.statistics-scope').waitFor()
  await page.getByRole('button', { name: '近7天', exact: true }).click()
  await page.waitForFunction(() => window.rankingCalls.at(-1)?.range === '7d')
  const sevenScope = page.locator('.statistics-scope')
  await sevenScope.getByText(/2026\/09\/29/).waitFor()
  assert.match(await sevenScope.innerText(), /2026\/09\/29[\s\S]*2026\/10\/06[\s\S]*UTC\+8/)
  await sevenScope.locator('summary').click()
  assert.match(await sevenScope.innerText(), /近 7 天[\s\S]*可跨赛季/)
  await page.getByRole('button', { name: '近30天', exact: true }).click()
  await page.waitForFunction(() => window.rankingCalls.at(-1)?.range === '30d')
  assert.match(await page.locator('.statistics-scope').innerText(), /2026\/09\/06[\s\S]*2026\/10\/06[\s\S]*UTC\+8/)

  await page.getByRole('button', { name: '主宰榜', exact: true }).click()
  await page.getByLabel('主宰排序').selectOption('winRate')
  const masterRows = page.locator('.master-table .tr')
  const visibleMasterOrder = await masterRows.locator('[data-label="主宰"] strong').evaluateAll(nodes => nodes.map(node => node.childNodes[0].textContent.trim()))
  assert.deepEqual(visibleMasterOrder, await page.evaluate(() => window.expectedMasterOrder))
  assert.equal((await masterRows.nth(1).locator('[data-label="胜率"]').innerText()).trim(), '—')
  assert.equal((await masterRows.nth(2).locator('[data-label="胜率"]').innerText()).trim(), '—')
  await page.getByRole('button', { name: '对阵一览', exact: true }).click()
  const splitTitle = await page.locator('.matrix-cell[title*="共 35 场"]').getAttribute('title')
  assert.match(splitTitle, /先手 10 场（样本不足）[\s\S]*后手 25 场（样本不足）/)
  assert.doesNotMatch(splitTitle, /\d+\/\d+/)
  const lowTitle = await page.locator('.matrix-cell[title*="共 5 场"]').getAttribute('title')
  assert.match(lowTitle, /不足 30 场，仅显示样本/)
  assert.doesNotMatch(lowTitle, /\d+\/\d+/)
  await page.screenshot({ path: path.join(output, 'rankings-authoritative-scope.png'), fullPage: true })

  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?fixture=public`)
  await page.getByRole('button', { name: '对局', exact: true }).click()
  const publicStatistics = page.locator('[data-detail-section="matches"]')
  await publicStatistics.locator('.statistics-scope').waitFor()
  await page.locator('.opening-hand .hand-card').first().waitFor()
  assert.equal(await page.locator('.public-deck-detail .notice').count(), 0, '公开牌库须完成全部初始化，不允许吞掉初始化错误后仅验部分内容')
  const publicCopy = await publicStatistics.innerText()
  assert.match(publicCopy, /样本不足：[\s\S]*不足 3 场/)
  assert.match(publicCopy, /最近 90 天[\s\S]*2026\/07\/08[\s\S]*2026\/10\/06[\s\S]*UTC\+8/)
  assert.doesNotMatch(publicCopy, /可展示|胜 \/ 负 \/ 平|胜率\s*\d/)
  assert.equal(await publicStatistics.locator('.match-stat-list article').count(), 0)
  const publicScope = publicStatistics.locator('.statistics-scope')
  const closedHeight = await publicScope.evaluate(element => element.getBoundingClientRect().height)
  await publicScope.locator('summary').click()
  assert.match(await publicScope.innerText(), /不可变版本[\s\S]*每组至少 3 场[\s\S]*未按先后手、运营规则版本或卡效版本拆分/)
  const openGeometry = await publicScope.evaluate(element => ({
    height: element.getBoundingClientRect().height,
    left: element.getBoundingClientRect().left,
    right: element.getBoundingClientRect().right,
    overflow: document.documentElement.scrollWidth > innerWidth + 1,
  }))
  assert(openGeometry.height > closedHeight, '展开口径后应在正常文档流中增高')
  assert(openGeometry.left >= -1 && openGeometry.right <= 391 && !openGeometry.overflow, '390px 展开口径不得横向溢出')
  await page.screenshot({ path: path.join(output, 'public-insufficient-narrow.png'), fullPage: true })

  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?capped=1`)
  await page.getByText('当前范围无法提供完整排行', { exact: true }).waitFor()
  assert.equal(await page.locator('.summary-strip').count(), 0, '无法证明完整的窗口不能展示部分数据摘要')

  await page.setViewportSize({ width: 1280, height: 900 })
  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?fixture=admin`)
  await page.getByRole('button', { name: '卡牌数据清单', exact: true }).click()
  const cardScope = page.locator('.card-analytics>.statistics-scope')
  await cardScope.waitFor()
  await cardScope.locator('summary').click()
  assert.match(await cardScope.innerText(), /卡效版本：查询时当前版本/)
  const cardCallsBeforeEdit = await page.evaluate(() => window.cardCalls.length)
  await page.getByLabel('卡效版本').selectOption('all')
  assert.equal(await page.locator('.card-analytics>.statistics-scope').count(), 0, '未提交筛选不能改写已应用口径；清除旧结果时口径也必须清除')
  assert.equal(await page.evaluate(() => window.cardCalls.length), cardCallsBeforeEdit, '编辑筛选不应自动冒充已查询')
  await page.getByRole('button', { name: '刷新卡牌清单', exact: true }).click()
  await cardScope.waitFor()
  await cardScope.locator('summary').click()
  assert.match(await cardScope.innerText(), /卡效版本：全部已记录版本/)

  await page.getByRole('button', { name: '主宰', exact: true }).click()
  const masterScope = page.locator('.master-analytics>.statistics-scope')
  await masterScope.waitFor()
  const appliedMasterScope = (await masterScope.locator('.statistics-scope-summary').innerText()).trim()
  await page.locator('.master-analytics').getByRole('button', { name: '近 7 天', exact: true }).click()
  await page.waitForFunction(() => window.masterCalls.length === 2)
  await page.locator('.master-analytics .refresh:not([disabled])').waitFor()
  assert.equal((await masterScope.locator('.statistics-scope-summary').innerText()).trim(), appliedMasterScope, '失败查询必须保留旧响应绑定的口径')
  assert.equal(await page.locator('.master-analytics>.statistics-scope').count(), 1)
  await page.screenshot({ path: path.join(output, 'admin-applied-scope.png'), fullPage: true })

  assert.deepEqual(errors, [])
  const report = {
    status: 'passed',
    scope: 'synthetic browser fixtures for rankings, public deck statistics, admin card and master analytics',
    cases: ['server-authored-7d-30d-window', 'cross-season-copy', 'low-sample-order-and-tooltip', 'public-insufficient-privacy', 'applied-filter-scope', 'failed-query-retains-scope', 'narrow-details-flow', 'capped-window-no-partial-summary'],
    screenshots: fs.readdirSync(output).filter(name => name.endsWith('.png')),
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
