import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/statistics-credibility-browser')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })
const proofFiles = ['src/l12/site/RankingsPage.vue', 'src/l12/L12DeckEditor.vue', 'src/l12/site/PublicDeckDetailPage.vue',
  'src/l12/platform.ts', 'src/l12/site/publicDeckRead.ts', 'scripts/check-statistics-credibility.mjs', 'scripts/check-ui-contracts.mjs']
const hashes = () => Object.fromEntries(proofFiles.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const proof = { sourceBefore: hashes(), rankingScenarios: [], errors: [], externalBlocked: 0, productionWrites: 0,
  scope: 'headless localhost synthetic HTTP/API fixtures; not physical mobile or production data' }

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import PublicDeckDetailPage from '/src/l12/site/PublicDeckDetailPage.vue'
import AdminCardAnalyticsPanel from '/src/l12/site/AdminCardAnalyticsPanel.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {adminApi,platformState,publicDeckApi,publicDeckReadApi,rankedApi} from '/src/l12/platform.ts'
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
window.expectedMasterOrder=[masters[1].nameZh,masters[0].nameZh,masters[2].nameZh]
window.masterIds=masters.map(master=>master.id)
const preset=(await loadOfficialPresetDecks())[0]
platformState.account={id:'qa-viewer',username:'统计验收者',role:'player',createdAt:'2026-01-01T00:00:00Z',publicHistory:false}

const masterStats=[
 {rank:1,masterId:masters[0].id,masterName:masters[0].nameZh,games:30,wins:15,losses:15,winRate:50,usageRate:70,firstGames:0,firstWins:0,firstWinRate:0,secondGames:30,secondWins:15,secondWinRate:50,strongestPlayer:'甲',title:'最强'+masters[0].nameZh},
 {rank:2,masterId:masters[1].id,masterName:masters[1].nameZh,games:1,wins:1,losses:0,winRate:100,usageRate:10,firstGames:1,firstWins:1,firstWinRate:100,secondGames:0,secondWins:0,secondWinRate:0,strongestPlayer:null,title:null},
 {rank:3,masterId:masters[2].id,masterName:masters[2].nameZh,games:29,wins:0,losses:29,winRate:0,usageRate:20,firstGames:14,firstWins:0,firstWinRate:0,secondGames:15,secondWins:0,secondWinRate:0,strongestPlayer:null,title:null},
]
window.setRankingMasters=rows=>masterStats.splice(0,masterStats.length,...rows)
window.initialRankingMasters=structuredClone(masterStats)
const rangeMetadata={
 season:{fromUtc:'2026-10-01T00:00:00Z',untilUtc:'2026-10-06T00:00:00Z',seasonId:'S-CURRENT',seasonName:'当前赛季'},
 '7d':{fromUtc:'2026-09-28T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z',seasonId:null,seasonName:null},
 '30d':{fromUtc:'2026-09-05T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z',seasonId:null,seasonName:null},
}
window.rankingCalls=[]
rankedApi.leaderboard=async(faction,range)=>{
 window.rankingCalls.push({faction,range})
 return {players:[{rank:1,username:'统计验收者',faction:'秩序',tier:'定级',titles:[],favoriteMasterId:masters[0].id,favoriteMasterName:masters[0].nameZh,displayValue:'七曜值 2100',wins:20,losses:10}],rangeLimited:new URLSearchParams(location.search).has('capped'),analytics:{range,summary:{matches:53,placedPlayers:1,activeMasters:3,updatedAt:'2026-10-05T15:00:00Z'},masters:masterStats,matchups:[
  {masterId:masters[0].id,opponentMasterId:masters[1].id,games:30,wins:0,winRate:0,firstGames:0,firstWins:0,secondGames:30,secondWins:0},
  {masterId:masters[0].id,opponentMasterId:masters[2].id,games:29,wins:29,winRate:100,firstGames:1,firstWins:1,secondGames:28,secondWins:28},
  {masterId:masters[1].id,opponentMasterId:masters[0].id,games:1,wins:0,winRate:0,firstGames:0,firstWins:0,secondGames:1,secondWins:0},
  {masterId:masters[1].id,opponentMasterId:masters[2].id,games:2,wins:1,winRate:50,firstGames:1,firstWins:1,secondGames:1,secondWins:0},
  {masterId:masters[2].id,opponentMasterId:masters[0].id,games:30,wins:30,winRate:100,firstGames:30,firstWins:30,secondGames:0,secondWins:0},
  {masterId:masters[2].id,opponentMasterId:masters[1].id,games:0,wins:0,winRate:0,firstGames:0,firstWins:0,secondGames:0,secondWins:0},
 ],...rangeMetadata[range]}}
}
rankedApi.history=async()=>({honors:[],factionTotals:[]})

const publicGeneration={id:'qa-public',publicCode:'qa-public',readToken:'a'.repeat(64),catalogVersion:'synthetic-catalog',policyVersion:1}
const publicCounts={main:preset.cardIds.length,uncountedMain:0,morale:preset.moraleIds.length,special:(preset.specialIds||[]).length,bench:0}
const publicEnvironment={status:'available',value:'1.0',reason:null}
const publicSummary={id:'qa-public',source:'public',name:preset.name,masterId:preset.masterId,
 masterName:catalog.find(card=>card.id===preset.masterId).nameZh,faction:catalog.find(card=>card.id===preset.masterId).faction,
 author:'匿名作者',publicCode:'qa-public',publicationVersion:null,createdAt:'2026-10-01T00:00:00Z',updatedAt:'2026-10-05T16:00:00Z',
 counts:publicCounts,legal:true,legalityReason:null,environment:publicEnvironment,views:8,likes:2,copies:1,viewerLiked:false,canEdit:false,readToken:null}
publicDeckReadApi.current=async()=>({summary:publicSummary,version:1,
 deck:{name:preset.name,masterId:preset.masterId,cardIds:preset.cardIds,moraleIds:preset.moraleIds,specialIds:preset.specialIds||[],updatedAt:'2026-10-05T16:00:00Z'},
 guide:{buildIdea:'',opening:'',keyCards:'',commonSequence:'',substitutions:''},matchups:[],contentRevision:0,contentUpdatedAt:null,
 readToken:publicGeneration.readToken,catalogVersion:publicGeneration.catalogVersion,policyVersion:publicGeneration.policyVersion})
publicDeckReadApi.versions=async(reference,page,pageSize)=>({...publicGeneration,items:[{version:1,name:preset.name,masterId:preset.masterId,
 createdAt:'2026-10-01T00:00:00Z',counts:publicCounts,legal:true,legalityReason:null,environment:publicEnvironment,changes:[]}],total:1,page,pageSize,canEdit:false})
publicDeckReadApi.statistics=async(reference,page,pageSize)=>({...publicGeneration,from:'2026-07-07T16:00:00Z',to:'2026-10-05T16:00:00Z',
 recentDays:90,games:0,sampleStatus:'insufficient',groups:[],total:0,page,pageSize})
publicDeckApi.counter=async()=>({id:'qa-public',publicCode:'qa-public',views:9,likes:2,copies:1,viewerLiked:false,canEdit:false})

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
  cacheDir: process.env.L12_VITE_CACHE || path.join(root, '.tmp', 'vite-statistics-credibility-browser'),
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

let browser, currentPage
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  currentPage = page; page.setDefaultTimeout(8000)
  const errors = proof.errors
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    if (new URL(route.request().url()).hostname === '127.0.0.1') return route.continue()
    proof.externalBlocked++; return route.abort()
  })

  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?fixture=rankings`)
  await page.getByRole('button', { name: '统计口径', exact: true }).waitFor()
  assert.equal(await page.locator('.ranking-page>.statistics-scope').count(), 0, '排行榜口径不应再单独占一行')
  const actionOrder = await page.locator('.page-actions button').allTextContents()
  assert.deepEqual(actionOrder, ['统计口径', '刷新数据'])
  await page.getByRole('button', { name: '近7天', exact: true }).click()
  await page.waitForFunction(() => window.rankingCalls.at(-1)?.range === '7d')
  await page.getByRole('button', { name: '统计口径', exact: true }).click()
  const sevenScope = page.getByRole('dialog', { name: '统计口径' })
  await sevenScope.getByText(/2026\/09\/29/).waitFor()
  assert.match(await sevenScope.innerText(), /2026\/09\/29[\s\S]*2026\/10\/06[\s\S]*UTC\+8/)
  assert.match(await sevenScope.innerText(), /近 7 天[\s\S]*可跨赛季/)
  await page.keyboard.press('Escape')
  assert.equal(await sevenScope.isVisible(), false)
  assert.equal(await page.getByRole('button', { name: '统计口径', exact: true }).evaluate(button => button === document.activeElement), true)
  await page.getByRole('button', { name: '近30天', exact: true }).click()
  await page.waitForFunction(() => window.rankingCalls.at(-1)?.range === '30d')
  await page.getByRole('button', { name: '统计口径', exact: true }).click()
  assert.match(await sevenScope.innerText(), /2026\/09\/06[\s\S]*2026\/10\/06[\s\S]*UTC\+8/)
  for (const [width, height] of [[1920,1080], [1440,900], [390,844], [360,780], [844,390]]) {
    await page.setViewportSize({ width, height })
    const bounds = await sevenScope.boundingBox()
    assert(bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= width + 1 && bounds.y + bounds.height <= height + 1)
    assert.equal(await page.getByRole('button', { name: '关闭统计口径' }).isVisible(), true)
    assert.equal(await sevenScope.evaluate(dialog => dialog.scrollWidth <= dialog.clientWidth + 1), true)
    await page.screenshot({ path: path.join(output, `ranking-scope-dialog-${width}x${height}.png`), fullPage: true })
  }
  await page.getByRole('button', { name: '关闭统计口径' }).click()
  await page.setViewportSize({ width: 1440, height: 900 })

  await page.getByRole('button', { name: '主宰榜', exact: true }).click()
  await page.getByLabel('主宰排序').selectOption('winRate')
  const masterRows = page.locator('.master-table .tr')
  const visibleMasterOrder = await masterRows.locator('[data-label="主宰"] strong').evaluateAll(nodes => nodes.map(node => node.childNodes[0].textContent.trim()))
  assert.deepEqual(visibleMasterOrder, await page.evaluate(() => window.expectedMasterOrder))
  assert.deepEqual(await masterRows.locator('[data-label="胜率"]').allTextContents(), ['100.0%', '50.0%', '0.0%'])
  assert.match(await masterRows.nth(1).locator('[data-label="先手"]').innerText(), /^—\s*0\/0$/)
  assert.match(await masterRows.nth(0).locator('[data-label="后手"]').innerText(), /^—\s*0\/0$/)
  const masterFixture = (id, name, games, winRate, firstGames = games, firstWinRate = winRate,
    secondGames = games, secondWinRate = winRate) => ({ rank: 99, masterId: id, masterName: name,
    games, wins: games * winRate / 100, losses: games * (100 - winRate) / 100, winRate, usageRate: games,
    firstGames, firstWins: firstGames * firstWinRate / 100, firstWinRate,
    secondGames, secondWins: secondGames * secondWinRate / 100, secondWinRate, strongestPlayer: null, title: null })
  const rankingScenarios = proof.rankingScenarios
  const rankingSource = await page.evaluate(() => ({ rows: [...window.masterIds], scope: window.rankingCalls.at(-1) }))
  const [masterA, masterB, masterC] = rankingSource.rows
  const checkRankingOrder = async (name, fixture, sort, expected) => {
    await page.evaluate(rows => window.setRankingMasters(rows), fixture)
    await page.getByRole('button', { name: '刷新数据', exact: true }).click()
    await page.locator('.page-actions button:not([disabled])').filter({ hasText: '刷新数据' }).waitFor()
    await page.getByLabel('主宰排序').selectOption(sort)
    assert.deepEqual(await masterRows.locator('[data-label="主宰"] small').allTextContents(), expected, name)
    const scope = await page.evaluate(() => window.rankingCalls.at(-1))
    assert.deepEqual(scope, rankingSource.scope, 'sorting must preserve requested source/scope')
    rankingScenarios.push(name)
    console.log(`PASS ranking browser ${rankingScenarios.length}: ${name}`)
  }
  const equalFour = [masterFixture(masterA, '同4场0%', 4, 0), masterFixture(masterB, '同4场50%', 4, 50)]
  await checkRankingOrder('equal-four-50-before-0', equalFour, 'winRate', [masterB, masterA])
  const crossThreshold = [masterFixture(masterA, '大样本25%', 40, 25), masterFixture(masterB, '小样本75%', 4, 75)]
  for (const sort of ['winRate', 'firstWinRate', 'secondWinRate'])
    await checkRankingOrder(`percentage-before-sample-${sort}`, crossThreshold, sort, [masterB, masterA])
  const initiatives = [masterFixture(masterA, '先手高', 40, 50, 20, 100, 20, 0),
    masterFixture(masterB, '后手高', 40, 50, 20, 0, 20, 100), masterFixture(masterC, '均衡', 40, 50, 20, 50, 20, 50)]
  await checkRankingOrder('first-rate-selected', initiatives, 'firstWinRate', [masterA, masterC, masterB])
  await checkRankingOrder('second-rate-selected', initiatives, 'secondWinRate', [masterB, masterC, masterA])
  for (const [sort, label] of [['firstWinRate', '先手'], ['secondWinRate', '后手']]) {
    const missing = masterFixture(masterA, '缺对应样本', 100, 80, sort === 'firstWinRate' ? 0 : 100, 100,
      sort === 'secondWinRate' ? 0 : 100, 100)
    await checkRankingOrder(`known-zero-before-missing-${sort}`, [missing, masterFixture(masterB, '真实0%', 4, 0),
      masterFixture(masterC, '真实50%', 4, 50)], sort, [masterC, masterB, masterA])
    assert.match(await masterRows.nth(1).locator(`[data-label="${label}"]`).innerText(), /^0\.0%/)
    assert.match(await masterRows.nth(2).locator(`[data-label="${label}"]`).innerText(), /^—\s*0\/0$/)
  }
  const ties = [masterFixture(masterA, '乙', 4, 50), masterFixture(masterB, '甲', 4, 50), masterFixture(masterC, '大样本', 40, 50)]
  const tieNames = ties.slice(0, 2).sort((left, right) => left.masterName.localeCompare(right.masterName, 'zh-CN')).map(row => row.masterId)
  for (const sort of ['winRate', 'firstWinRate', 'secondWinRate'])
    await checkRankingOrder(`rate-tie-original-order-${sort}`, ties, sort, [masterC, ...tieNames])
  await checkRankingOrder('restore-original-percent-order', await page.evaluate(() => window.initialRankingMasters),
    'winRate', [masterB, masterA, masterC])
  await page.getByRole('button', { name: '对阵一览', exact: true }).click()
  const [firstMaster, secondMaster, thirdMaster] = await page.evaluate(() => window.masterIds)
  const matchupCell = (masterId, opponentMasterId) => page.locator(`.matrix-cell[data-master-id="${masterId}"][data-opponent-master-id="${opponentMasterId}"]`)
  const fullLoss = matchupCell(firstMaster, secondMaster)
  const lowWin29 = matchupCell(firstMaster, thirdMaster)
  const lowLoss1 = matchupCell(secondMaster, firstMaster)
  const lowEven = matchupCell(secondMaster, thirdMaster)
  const fullWin = matchupCell(thirdMaster, firstMaster)
  const zeroSample = matchupCell(thirdMaster, secondMaster)
  assert.deepEqual(await Promise.all([fullLoss, lowWin29, lowLoss1, lowEven, fullWin, zeroSample].map(cell => cell.locator('b').innerText())), ['0.0%', '100.0%', '0.0%', '50.0%', '100.0%', '—'])
  assert.match(await fullLoss.getAttribute('class'), /disadvantage/)
  assert.doesNotMatch(await fullLoss.getAttribute('class'), /low-sample/)
  assert.match(await lowWin29.getAttribute('class'), /advantage[\s\S]*low-sample/)
  assert.match(await lowLoss1.getAttribute('class'), /disadvantage[\s\S]*low-sample/)
  assert.match(await lowEven.getAttribute('class'), /even[\s\S]*low-sample/)
  assert.match(await fullWin.getAttribute('class'), /advantage/)
  assert.doesNotMatch(await fullWin.getAttribute('class'), /low-sample/)
  assert.match(await zeroSample.getAttribute('class'), /no-data/)
  assert.doesNotMatch(await zeroSample.getAttribute('class'), /low-sample|advantage|disadvantage|even/)
  assert.match(await lowWin29.getAttribute('title'), /共 29 场（不足 30 场，仅供参考）[；;]先手 1\/1（100\.0%）[；;]后手 28\/28（100\.0%）/)
  assert.match(await lowLoss1.getAttribute('title'), /共 1 场（不足 30 场，仅供参考）[；;]先手 暂无对局[；;]后手 0\/1（0\.0%）/)
  assert.match(await fullLoss.getAttribute('title'), /共 30 场[；;]先手 暂无对局[；;]后手 0\/30（0\.0%）/)
  assert.equal(await zeroSample.getAttribute('title'), '暂无对局')
  const backgroundLuminance = async cell => {
    const color = await cell.evaluate(element => getComputedStyle(element).backgroundColor)
    const [red, green, blue] = color.match(/\d+(?:\.\d+)?/g).slice(0, 3).map(Number)
    return red * .2126 + green * .7152 + blue * .0722
  }
  assert((await backgroundLuminance(lowWin29)) > (await backgroundLuminance(fullWin)), '低样本优势色应比足量样本浅')
  assert((await backgroundLuminance(lowLoss1)) > (await backgroundLuminance(fullLoss)), '低样本劣势色应比足量样本浅')
  assert.match(await page.locator('.matrix-panel>header p').innerText(), /有对局即显示胜率[\s\S]*少于 30 场[\s\S]*较浅强弱色/)
  await page.screenshot({ path: path.join(output, 'rankings-low-sample-wide.png'), fullPage: true })

  await page.setViewportSize({ width: 390, height: 844 })
  assert.equal(await page.locator('.master-matchup-matrix').evaluate(element => element.scrollWidth > element.clientWidth), true, '窄屏矩阵应在自身内横向滚动')
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, '窄屏页面不应横向溢出')
  await page.screenshot({ path: path.join(output, 'rankings-low-sample-narrow.png'), fullPage: true })
  await page.goto(`http://127.0.0.1:${port}/__statistics_credibility__?fixture=public`)
  await page.getByRole('button', { name: '对局', exact: true }).click()
  const publicStatistics = page.locator('[data-detail-section="matches"]')
  await publicStatistics.locator('.statistics-scope').waitFor()
  await page.locator('.opening-hand .hand-card').first().waitFor()
  assert.equal(await page.locator('.public-deck-detail .notice').count(), 0, '公开牌库须完成全部初始化，不允许吞掉初始化错误后仅验部分内容')
  const publicCopy = await publicStatistics.innerText()
  assert.match(publicCopy, /样本不足：[\s\S]*(?:不足 3 场|没有达到 3 场门槛)/)
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
    rankingScenarios,
    cases: ['server-authored-7d-30d-window', 'cross-season-copy', 'low-sample-1-29-30-boundaries', 'zero-sample-no-fake-rate', 'matchup-0-50-100-tones', 'low-sample-muted-color-depth', 'initiative-tooltip', 'rankings-wide-narrow-layout', 'public-insufficient-privacy', 'applied-filter-scope', 'failed-query-retains-scope', 'narrow-details-flow', 'capped-window-no-partial-summary'],
    screenshots: fs.readdirSync(output).filter(name => name.endsWith('.png')),
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  proof.status = 'passed'
  console.log(JSON.stringify(report, null, 2))
} catch (error) {
  proof.status = 'failed'; proof.failure = String(error.stack || error)
  if (currentPage && !currentPage.isClosed()) {
    proof.visibleText = await currentPage.locator('#app').innerText().catch(() => '')
    await currentPage.screenshot({ path: path.join(output, 'failed-screen.png'), fullPage: true }).catch(() => undefined)
  }
  throw error
} finally {
  proof.sourceAfter = hashes(); proof.sourceUnchanged = JSON.stringify(proof.sourceBefore) === JSON.stringify(proof.sourceAfter)
  fs.writeFileSync(path.join(output, 'run.json'), JSON.stringify(proof, null, 2))
  await browser?.close()
  await server.close()
}
