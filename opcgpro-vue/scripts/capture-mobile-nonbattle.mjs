import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/mobile-nonbattle-review')
const assertionOnly = process.env.L12_QA_ASSERT_ONLY === '1'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/mobileViewport.css'

localStorage.setItem('l12-auth-token','qa-token')
localStorage.setItem('l12-account',JSON.stringify({id:'qa-player',username:'移动端验收玩家',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}))

const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-token'
platform.platformState.account={id:'qa-player',username:'移动端验收玩家',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false
const routeRecovery=await import('/src/chunkRecovery.ts')
window.__qaRouteRecovery={begin:routeRecovery.beginRouteNavigation,finish:routeRecovery.finishRouteNavigation,fail:routeRecovery.showRouteNavigationError}

const net=await import('/src/l12/net.ts')
net.l12State.status='online'

const stat=(games,wins,losses,draws,firstGames,firstWins,secondGames,secondWins)=>({games,wins,losses,draws,firstGames,firstWins,secondGames,secondWins})
platform.playerApi.statistics=async()=>({
 overall:stat(128,77,48,3,65,41,63,36),ranked:stat(86,52,33,1,43,28,43,24),updatedAt:'2026-09-21T08:00:00Z',
 masters:['梅杰德','阿斯加德','西芙','太阳城'].map((masterName,index)=>({masterId:'M'+index,masterName,overall:stat(32-index*2,20-index,11,1,16,10,16-index*2,10-index),ranked:stat(22-index,14-index,8,0,11,7,11-index,7-index)}))
})
const overview={
 profile:{accountId:'qa-player',username:'移动端验收玩家',seasonId:'S2026-2',faction:'秩序',sevenValue:2380,displayValue:'七曜值 2380',placementPlayed:10,placementWins:7,placed:true,wins:52,losses:33,winStreak:3,lossStreak:0,tier:'璀璨群星',tierIndex:6,factionRank:3,titles:['秩序先锋','最强梅杰德'],rankLabel:'璀璨群星 · 秩序第 3',selectedMasterTitle:'最强梅杰德',masterTitles:['最强梅杰德','最强阿斯加德']},
 factionTotals:{秩序:36},config:{placementMatches:10,placementMaximum:10,broadcastEnabled:true,factions:[],masterTitles:[],timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},broadcast:{displaySeconds:16,lobbyDelaySeconds:3,intervalSeconds:15,winStreakThreshold:5,streakEndedThreshold:5,minimumTierIndex:0,winStreakEnabled:true,streakEndedEnabled:true,highestTierEnabled:true,factionTitleEnabled:true,masterTitleEnabled:true}},history:[]
}
platform.rankedApi.overview=async()=>overview
const {loadDeckCatalog}=await import('/src/l12/decks.ts')
const catalog=await loadDeckCatalog()
const masters=catalog.filter(card=>card.cardType==='master').slice(0,4)
const masterStats=masters.map((master,index)=>({rank:index+1,masterId:master.id,masterName:master.nameZh,games:38-index,wins:25-index,losses:13,winRate:65.8-index,usageRate:25,firstWinRate:64,secondWinRate:67,firstWins:12,firstGames:19,secondWins:13,secondGames:19,strongestPlayer:'长昵称验收玩家'+index,title:'最强'+master.nameZh}))
platform.rankedApi.leaderboard=async()=>({players:Array.from({length:8},(_,index)=>({rank:index+1,username:'移动端长昵称玩家'+index,faction:['秩序','混沌','命运'][index%3],tier:'定级段位名称',titles:['派系主题称号','最强'+(masters[index%masters.length]?.nameZh||'主宰')],favoriteMasterId:masters[index%masters.length]?.id,favoriteMasterName:masters[index%masters.length]?.nameZh,displayValue:'七曜值 '+(2100-index*45),wins:22-index,losses:11+index})),analytics:{range:'season',summary:{matches:84,placedPlayers:19,activeMasters:masters.length,updatedAt:'2026-09-21T08:00:00Z'},masters:masterStats,matchups:masterStats.flatMap(left=>masterStats.map(right=>({masterId:left.masterId,opponentMasterId:right.masterId,games:8,wins:5,winRate:62.5,firstWins:3,firstGames:4,secondWins:2,secondGames:4})))}})
platform.rankedApi.history=async()=>[{seasonId:'S2026-1',seasonName:'第一赛季长名称',username:'赛季荣誉玩家',faction:'秩序',tier:'赛季最高段位',displayValue:'七曜值 2450',titles:['秩序冠首','最强'+(masters[0]?.nameZh||'主宰')]}]
platform.sessionApi.list=async()=>[
 {id:'session-current-long-identifier',createdAt:'2026-09-20T09:00:00Z',expiresAt:'2026-10-20T09:00:00Z',current:true,authStrength:'password',permissionVersion:3},
 {id:'session-tablet-long-identifier',createdAt:'2026-09-19T09:00:00Z',expiresAt:'2026-10-19T09:00:00Z',current:false,authStrength:'password',permissionVersion:3}
]
platform.usernameChangeApi.status=async()=>({freeRenameAvailable:true,freeRenameUsed:0})
platform.emailApi.capability=async()=>({enabled:false,mailConfigured:false})

const coverData='data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300"%3E%3Crect width="400" height="300" fill="%230b2830"/%3E%3Ccircle cx="35" cy="150" r="28" fill="%23d9b65f"/%3E%3Ccircle cx="365" cy="150" r="28" fill="%2352c4cb"/%3E%3C/svg%3E'
const article=(id,kind,index)=>({id,title:'第一弹补充包「诸神黄昏」发售公告与移动端长标题换行验收 '+index,summary:'这是一段用于验收移动端换行与截断的资讯摘要，长度 deliberately 拉长以观察两行以上的排布效果。',body:'正文',category:'公告',coverUrl:coverData,link:'',slug:id,pinned:index===0,status:'published',hasUnpublishedChanges:false,createdAt:'2026-09-01T08:00:00Z',updatedAt:'2026-09-01T08:00:00Z',publishedAt:'2026-09-0'+index+'T08:00:00Z',author:'十二军团官方',updatedBy:'admin',revision:1,kind,sortOrder:index})
const newsArticles=Array.from({length:6},(_,i)=>article('news-'+i,'news',i+1))
platform.articleApi.list=async({kind}={})=>kind==='news'?newsArticles:Array.from({length:3},(_,i)=>article(kind+'-'+i,kind,i+1))
platform.siteContentApi.categories=async()=>['公告','活动','攻略']
platform.siteContentApi.home=async()=>({composition:JSON.stringify({version:1,heroSlides:[],notices:[{id:'n1',enabled:true,text:'《十二军团》第一弹现已发售，移动端体验持续优化中',link:''}],newsEyebrow:'NEWS',newsTitle:'资讯一览',newsDescription:'',videoEyebrow:'VIDEO',videoTitle:'最新视频',videoDescription:'',productEyebrow:'PRODUCTS',productTitle:'产品上新',productDescription:''}),legal:'{}',news:newsArticles.slice(0,4),videos:[],products:[],media:[]})

const tournament={id:'tour-001',code:'QA2026',name:'九月社区月赛——移动端显示验收特别长名称赛事',organizerAccountId:'qa-player',organizerName:'移动端验收玩家',referees:[],status:'registration',format:'swiss',visibility:'public',maxPlayers:32,startAt:'2026-09-30T12:00:00Z',description:'用于验收赛事中心在移动端竖屏下的卡片排布、按钮换行与信息层级。',rules:{ruleset:'standard',disasterMode:'all',banList:'',disasterCardIds:[],cardRestrictions:[],deckVisibility:'after',hash:'hash',capturedAt:'2026-09-01T00:00:00Z'},roundMinutes:50,checkInMinutes:10,participants:Array.from({length:5},(_,i)=>({accountId:'p'+i,username:'参赛选手长昵称'+i,checkedIn:i%2===0,dropped:false,eliminated:false,seed:i+1})),rounds:[],version:3,legacyImported:false,createdAt:'2026-09-01T00:00:00Z',updatedAt:'2026-09-10T00:00:00Z',swissRounds:5,registrationVisibility:'public',lateGraceMinutes:10,finalSwissStandings:[],eliminationBracket:[]}
platform.tournamentApi.list=async()=>({platformVersion:1,items:[tournament]})

const friend=(i,online)=>({accountId:'friend-'+i,username:'好友长昵称玩家'+i,status:'accepted',direction:'none',createdAt:'2026-08-01T00:00:00Z',online})
platform.friendApi.friends=async()=>Array.from({length:6},(_,i)=>friend(i,i<3))
platform.friendApi.requests=async()=>[{accountId:'req-1',username:'申请者长昵称一号',status:'pending',direction:'incoming',createdAt:'2026-09-20T00:00:00Z'}]
platform.friendApi.blocked=async()=>[{accountId:'blk-1',username:'被屏蔽玩家一号',status:'blocked',direction:'none',createdAt:'2026-08-15T00:00:00Z'}]
platform.friendApi.presence=async()=>Array.from({length:6},(_,i)=>({accountId:'friend-'+i,username:'好友长昵称玩家'+i,online:i<3,activity:i===0?'playing':i===1?'inRoom':'idle',roomCode:i===1?'ABC123':undefined,canInvite:i===2,canSpectate:i===0,friendStatus:'accepted',friendDirection:'none'}))
platform.friendApi.players=async()=>[{accountId:'stranger-1',username:'搜索结果玩家一号',status:'none',direction:'none',createdAt:'2026-09-01T00:00:00Z'}]
platform.publicDeckApi.list=async()=>[]
platform.alternateArtApi.gallery=async()=>[]

const {createRuleCenterDraft,createRulingsDraft}=await import('/src/l12/data/ruleCenterData.ts')
const publishedRuleCenter=createRuleCenterDraft()
for(const collection of ['coreBlocks','quickStart','terms','tournament','versions'])for(const item of publishedRuleCenter[collection])item.status='published'
const publishedRulings=createRulingsDraft().map(item=>({...item,status:'published'}))
const sampleMatches=Array.from({length:6},(_,i)=>({matchId:'MATCH-QA-2026092'+i+'-LONG-IDENTIFIER',roomCode:'QA00'+i,player0:'移动端验收玩家',player1:'对手长昵称玩家'+i,deck0:'秩序梅杰德控制长名称构筑',deck1:'混沌阿斯加德快攻长名称构筑',startedUtc:'2026-09-2'+i+'T10:00:00Z',endedUtc:'2026-09-2'+i+'T10:32:00Z',winner:i%2,finalHash:'hash',commandCount:182}))
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/content?'))return json({values:{'rules.notice':'','rules.center':JSON.stringify(publishedRuleCenter),'rules.rulings':JSON.stringify({schemaVersion:2,entries:publishedRulings})},observedAt:new Date().toISOString(),nextRuleTransitionAt:null})
 if(url.includes('/api/content/rules.center'))return json({key:'rules.center',value:JSON.stringify(createRuleCenterDraft())})
 if(url.includes('/api/content/rules.rulings'))return json({key:'rules.rulings',value:JSON.stringify(createRulingsDraft())})
 if(url.includes('/api/content/rules.notice'))return json({key:'rules.notice',value:''})
 if(url.includes('/api/operations/effective-policy'))return json({},503)
 if(url.includes('/api/matches'))return json(sampleMatches)
 return originalFetch(input,init)
}

const [
 {default:SiteShell},
 {default:OfficialHomePage},{default:NewsPage},{default:RuleCenterPage},{default:BattleHubPage},
 {default:TournamentHubPage},{default:DeckLibraryPage},{default:FriendsPage},{default:RankingsPage},
 {default:ProfilePage},{default:CardArchive},{default:MatchRecords},
]=await Promise.all([
 import('/src/l12/site/SiteShell.vue'),
 import('/src/l12/site/OfficialHomePage.vue'),import('/src/l12/site/NewsPage.vue'),import('/src/l12/site/RuleCenterPage.vue'),import('/src/l12/site/BattleHubPage.vue'),
 import('/src/l12/site/TournamentHubPage.vue'),import('/src/l12/site/DeckLibraryPage.vue'),import('/src/l12/site/FriendsPage.vue'),import('/src/l12/site/RankingsPage.vue'),
 import('/src/l12/site/ProfilePage.vue'),import('/src/l12/CardArchive.vue'),import('/src/l12/MatchRecords.vue'),
])
const routes=[
 {path:'/',component:OfficialHomePage},
 {path:'/news',component:NewsPage},
 {path:'/news/:articleId',component:NewsPage},
 {path:'/rules',component:RuleCenterPage},
 {path:'/battle',component:BattleHubPage,meta:{section:'battle'}},
 {path:'/battle/tournaments',component:TournamentHubPage,meta:{section:'battle'}},
 {path:'/decks',component:DeckLibraryPage},
 {path:'/battle/friends',component:FriendsPage,meta:{section:'battle'}},
 {path:'/cards',component:CardArchive},
 {path:'/battle/rankings',component:RankingsPage,meta:{section:'battle'}},
 {path:'/me',component:ProfilePage},
 {path:'/battle/records',component:MatchRecords,meta:{section:'battle'}},
 {path:'/:pathMatch(.*)*',redirect:'/'},
]
const router=createRouter({history:createMemoryHistory(),routes})
const target=new URLSearchParams(location.search).get('route')||'/'
await router.push(target)
await router.isReady()
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

let browser
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-mobile-nonbattle-review'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'mobile-nonbattle-review-fixture',
    resolveId(id) { if (id === '/__mobile_review__.js') return id },
    load(id) { if (id === '/__mobile_review__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__mobile_review__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<head><meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover"></head><style>html,body,#app{margin:0;min-height:100%;background:#080d11}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__mobile_review__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const routesToCapture = [
  { route: '/', name: 'home', wait: '.official-home' },
  { route: '/news', name: 'news', wait: '.news-page, .article-list, main' },
  { route: '/rules', name: 'rules', wait: '.rule-tools input' },
  { route: '/cards', name: 'cards', wait: '.archive-grid, .card-archive' },
  { route: '/decks', name: 'decks', wait: '.deck-page' },
  { route: '/battle', name: 'battle-hub', wait: '.battle-hub' },
  { route: '/battle/rankings', name: 'rankings', wait: '.rankings-page, main' },
  { route: '/battle/friends', name: 'friends', wait: '.friends-page' },
  { route: '/battle/tournaments', name: 'tournaments', wait: '.hub-page' },
  { route: '/battle/records', name: 'records', wait: '.match-records, .records-header, main' },
  { route: '/me', name: 'profile', wait: '.profile-page, main' },
]

const viewports = [
  { width: 390, height: 844, label: '390x844' },
  { width: 360, height: 780, label: '360x780' },
  { width: 430, height: 932, label: '430x932' },
  { width: 700, height: 900, label: '700x900' },
  { width: 701, height: 900, label: '701x900' },
  { width: 759, height: 900, label: '759x900' },
  { width: 760, height: 900, label: '760x900' },
  { width: 761, height: 900, label: '761x900' },
  { width: 849, height: 900, label: '849x900' },
  { width: 850, height: 900, label: '850x900' },
  { width: 851, height: 900, label: '851x900' },
]

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const context = await browser.newContext({ deviceScaleFactor: 2, hasTouch: true, isMobile: true })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  const assertMounted = async (selector, label) => {
    await page.locator(selector).first().waitFor({ timeout: 15000 })
    const mounted = await page.locator('.site-content').evaluate(element => {
      const box = element.getBoundingClientRect()
      const visibleChild = [...element.children].some(child => {
        const childBox = child.getBoundingClientRect()
        const style = getComputedStyle(child)
        return style.display !== 'none' && style.visibility !== 'hidden' && childBox.width > 0 && childBox.height > 0
      })
      return box.width > 0 && box.height > 0 && visibleChild
    })
    if (!mounted) throw new Error(`${label}: route left an empty site content surface`)
  }
  const clickNavigation = async (label, selector) => {
    const link = page.locator('.site-sidebar .site-nav a').filter({ hasText: label }).first()
    await link.waitFor({ state: 'visible', timeout: 5000 })
    await link.click()
    await assertMounted(selector, label)
  }

  // 同一标签页连续点击真实侧边栏，覆盖组件卸载/重挂载与导航状态；不得依赖刷新恢复。
  await page.setViewportSize({ width: 1366, height: 768 })
  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2F`)
  await assertMounted('.official-home', '主页')
  await page.evaluate(() => window.__qaRouteRecovery.begin('/news'))
  await page.locator('#l12-route-navigation').waitFor({ timeout: 2000 })
  if (!(await page.locator('#l12-route-navigation').innerText()).includes('正在打开页面'))
    throw new Error('slow route did not expose a visible loading state')
  await page.evaluate(() => window.__qaRouteRecovery.finish('/news'))
  if (await page.locator('#l12-route-navigation').count()) throw new Error('completed route retained its loading surface')
  await page.evaluate(() => window.__qaRouteRecovery.fail('/decks'))
  const failureText = await page.locator('#l12-route-navigation').innerText()
  if (!failureText.includes('页面未能打开') || !failureText.includes('重新加载') || !failureText.includes('返回主页'))
    throw new Error('failed route did not expose retry and home actions')
  await page.evaluate(() => { window.__qaRouteRecovery.begin('/'); window.__qaRouteRecovery.finish('/') })
  const mainNavigation = [
    ['资讯', '.news-page'], ['对战', '.battle-hub'], ['牌库', '.deck-page'],
    ['图鉴', '.archive-grid, .card-archive'], ['规则', '.rule-tools input'], ['我的', '.profile-page, main'],
  ]
  for (const [label, selector] of mainNavigation) {
    await clickNavigation('主页', '.official-home')
    await clickNavigation(label, selector)
  }
  await clickNavigation('主页', '.official-home')
  await clickNavigation('对战', '.battle-hub')
  for (const [label, selector] of [
    ['赛事', '.hub-page'], ['排行', '.rankings-page, main'],
    ['好友', '.friends-page'], ['对局', '.match-records, .records-header, main'],
  ]) await clickNavigation(label, selector)

  // 4:3 边缘标记封面在各档窄屏的一览与详情均必须保留自然比例和左右两端。
  const assertNaturalImageRatio = async (selector, label) => {
    const image = page.locator(selector).first()
    await image.waitFor({ timeout: 15000 })
    await image.evaluate(element => element.complete && element.naturalWidth > 0
      ? true
      : new Promise(resolve => element.addEventListener('load', () => resolve(true), { once: true })))
    const result = await image.evaluate(element => {
      const box = element.getBoundingClientRect()
      const naturalRatio = element.naturalWidth / element.naturalHeight
      const renderedRatio = box.width / box.height
      return {
        fits: box.left >= -1 && box.right <= document.documentElement.clientWidth + 1,
        ratioError: Math.abs(renderedRatio - naturalRatio) / naturalRatio,
      }
    })
    if (!result.fits || result.ratioError > 0.03)
      throw new Error(`${label}: cover was clipped or distorted (${JSON.stringify(result)})`)
  }
  for (const viewport of [
    { width: 320, height: 568, label: '320x568' },
    { width: 360, height: 780, label: '360x780' },
    { width: 390, height: 844, label: '390x844' },
    { width: 430, height: 932, label: '430x932' },
    { width: 700, height: 900, label: '700x900' },
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fnews`)
    await assertNaturalImageRatio('.news-list .list-entry>img', `移动资讯一览封面 ${viewport.label}`)
    if (viewport.width === 390)
      await page.screenshot({ path: path.join(output, 'news-list-no-crop-390x844.png'), fullPage: true })
    await page.locator('.news-list .list-entry').first().click()
    await assertNaturalImageRatio('.news-detail .detail-cover', `移动资讯详情封面 ${viewport.label}`)
    if (viewport.width === 390)
      await page.screenshot({ path: path.join(output, 'news-detail-no-crop-390x844.png'), fullPage: true })
  }

  if (!assertionOnly) {
    for (const viewport of viewports) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height })
      for (const item of routesToCapture) {
        await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=${encodeURIComponent(item.route)}`)
        try { await page.locator(item.wait).first().waitFor({ timeout: 9000 }) } catch { errors.push(`wait timeout: ${item.name}@${viewport.label}`) }
        await page.waitForTimeout(1000)
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)
        await page.screenshot({ path: path.join(output, `${item.name}-${viewport.label}${overflow ? '-OVERFLOW' : ''}.png`), fullPage: true })
        console.log(`captured ${item.name} @ ${viewport.label}${overflow ? ' (overflow)' : ''}`)
      }
    }
  }

  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fcards`)
  await page.locator('.archive-card').first().waitFor({ timeout: 15000 })
  const initialArchiveCards = await page.locator('.archive-card').count()
  const initialArchiveImages = await page.locator('.archive-card .l12-card-image').count()
  if (initialArchiveCards <= 60) throw new Error(`archive must preserve the complete continuous card list: ${initialArchiveCards}`)
  if (initialArchiveImages >= initialArchiveCards) throw new Error(`mobile archive eagerly created every card image: ${initialArchiveImages}/${initialArchiveCards}`)
  await page.evaluate(() => {
    const scroller = document.querySelector('.site-content')
    if (scroller instanceof HTMLElement) scroller.scrollTop = scroller.scrollHeight
  })
  await page.waitForTimeout(500)
  const afterScrollArchiveImages = await page.locator('.archive-card .l12-card-image').count()
  if (afterScrollArchiveImages <= initialArchiveImages) throw new Error(`mobile archive did not create more images while scrolling: ${initialArchiveImages} -> ${afterScrollArchiveImages}`)
  if ((await page.locator('.archive-card .mobile-deferred-card-image').count()) !== initialArchiveCards)
    throw new Error('mobile archive changed the original card-slot count while deferring images')
  for (const viewport of [
    { width: 320, height: 568 },
    { width: 390, height: 844 },
    { width: 430, height: 932 },
    { width: 520, height: 844 },
    { width: 700, height: 900 },
    { width: 701, height: 900 },
  ]) {
    await page.setViewportSize(viewport)
    const geometry = await page.locator('.archive-workspace').evaluate(element => {
      const workspace = element.getBoundingClientRect()
      const gridElement = element.querySelector('.archive-grid')
      const grid = gridElement?.getBoundingClientRect()
      const card = element.querySelector('.archive-card')?.getBoundingClientRect()
      const columns = gridElement ? getComputedStyle(gridElement).gridTemplateColumns.split(/\s+/).filter(Boolean).length : 0
      return { workspace: workspace.width, grid: grid?.width ?? 0, card: card?.width ?? 0, columns }
    })
    if (geometry.grid < geometry.workspace - 2 || geometry.card < 90)
      throw new Error(`archive mobile grid retained a hidden detail column at ${viewport.width}x${viewport.height}: ${JSON.stringify(geometry)}`)
    const expectedColumns = geometry.workspace <= 380 ? 2 : geometry.workspace <= 580 ? 3 : 4
    if (geometry.columns !== expectedColumns)
      throw new Error(`archive mobile grid expected ${expectedColumns} columns for ${geometry.workspace}px of usable width at ${viewport.width}x${viewport.height}, got ${geometry.columns}: ${JSON.stringify(geometry)}`)
    await page.screenshot({ path: path.join(output, `archive-columns-${viewport.width}x${viewport.height}-${geometry.columns}col.png`) })
  }

  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fbattle%2Frankings`)
  await page.locator('.player-table .tr').first().waitFor({ timeout: 15000 })
  await page.evaluate(() => {
    const row = document.querySelector('.player-table .tr')
    const scroller = document.querySelector('.site-content')
    if (!(row instanceof HTMLElement) || !(scroller instanceof HTMLElement)) return
    scroller.scrollTop += row.getBoundingClientRect().top - scroller.getBoundingClientRect().top - 8
  })
  await page.waitForTimeout(150)
  const visiblePlayerRows = await page.locator('.player-table .tr').evaluateAll(rows => rows.filter(row => {
    const box = row.getBoundingClientRect()
    const scroller = document.querySelector('.site-content')
    const viewport = scroller?.getBoundingClientRect()
    return box.top >= (viewport?.top ?? 0) && box.bottom <= (viewport?.bottom ?? innerHeight)
  }).length)
  if (visiblePlayerRows < 4) throw new Error(`compact ranking shows only ${visiblePlayerRows} complete player rows`)

  await page.setViewportSize({ width: 700, height: 900 })
  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fdecks`)
  await page.setViewportSize({ width: 700, height: 900 })
  await page.waitForTimeout(100)
  if ((await page.locator('.site-mobile-head:visible').count()) < 1) throw new Error('700px must use compact site navigation')
  await page.setViewportSize({ width: 701, height: 900 })
  await page.waitForTimeout(100)
  if ((await page.locator('.site-mobile-head:visible').count()) > 0) throw new Error('701px must leave compact site navigation')
  for (const viewport of [{ width: 701, height: 360 }, { width: 844, height: 390 }, { width: 1024, height: 600 }, { width: 1920, height: 600 }]) {
    await page.setViewportSize(viewport)
    await page.waitForTimeout(100)
    if ((await page.locator('.site-mobile-head:visible').count()) !== 1)
      throw new Error(`${viewport.width}x${viewport.height} must use compact navigation by usable height`)
  }

  // 交互状态：导航抽屉、设置弹窗、在线人数弹窗（390 宽）
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fdecks`)
  await page.locator('.deck-page').waitFor({ timeout: 15000 })
  await page.waitForTimeout(800)
  await page.locator('.site-mobile-head button').click()
  await page.waitForTimeout(400)
  await page.screenshot({ path: path.join(output, 'nav-drawer-390x844.png'), fullPage: false })
  await page.locator('.site-sidebar .site-utilities button').first().click()
  await page.waitForTimeout(500)
  await page.screenshot({ path: path.join(output, 'settings-modal-390x844.png'), fullPage: false })

  await page.goto(`http://127.0.0.1:${port}/__mobile_review__?route=%2Fbattle%2Frecords`)
  await page.locator('.records-list>button').first().waitFor({ timeout: 15000 })
  await page.locator('.records-list>button').first().click()
  await page.waitForTimeout(300)
  await page.screenshot({ path: path.join(output, 'records-detail-390x844.png'), fullPage: true })

  if (errors.length) throw new Error(`mobile nonbattle page errors: ${errors.join(' | ')}`)
  console.log(JSON.stringify({ output, errors }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}

