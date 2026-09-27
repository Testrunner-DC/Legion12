import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_TOURNAMENT_VISUAL_OUT || path.resolve(root, '../artifacts/tournament-center-visual-20260928')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'

localStorage.setItem('l12-auth-token','visual-token')
localStorage.setItem('l12-account',JSON.stringify({id:'qa-organizer',username:'十二军团赛事组',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}))
const platform=await import('/src/l12/platform.ts')
platform.platformState.token='visual-token'
platform.platformState.account={id:'qa-organizer',username:'十二军团赛事组',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}
platform.authState.initialized=true
platform.authState.verified=true

const counts={registered:16,pendingCheckIn:3,checkedIn:13,active:14,waitlisted:2,dropped:1,removed:1,registrationBanned:1}
const standing=(rank,id,name,wins,losses,draws,score)=>({roundNumber:3,rank,accountId:id,username:name,wins,losses,draws,byes:0,opponentScore:score,opponentsOpponentScore:score+6,seed:rank})
const standings=[standing(1,'qa-organizer','十二军团赛事组',3,0,0,8),standing(2,'p2','北境观星者',2,1,0,7),standing(3,'p3','长安夜行人',2,1,0,6),standing(4,'p4','海潮回声',1,1,1,6)]
const participant=(accountId,username,extra={})=>({accountId,username,checkedIn:true,dropped:false,eliminated:false,removed:false,registrationBanned:false,seed:1,waitlisted:false,...extra})
const match=(id,table,a,aName,b,bName,extra={})=>({id,table,playerAAccountId:a,playerAName:aName,playerBAccountId:b,playerBName:bName,roomCode:'ROOM'+table,readyA:true,readyB:true,status:'running',timeExtensionMinutes:0,rulings:[],sourceMatchIds:[],rulesHash:'rules',replayNumber:0,canEnter:false,canSpectate:false,events:[],paused:false,totalPausedSeconds:0,...extra})
const tournament={
 id:'t-visual',code:'L12-AUTUMN',name:'十二军团秋季公开赛',organizerAccountId:'qa-organizer',organizerName:'十二军团赛事组',referees:[{accountId:'friend-1',username:'裁判·白露'}],
 status:'running',format:'swiss-cut',visibility:'public',maxPlayers:32,startAt:'2026-10-03T11:00:00Z',description:'面向全部玩家的公开赛事。当前正在进行瑞士轮，参赛者可在本页完成签到、进入对局并查看权威赛果。',
 rules:{ruleset:'2026 秋季现行规则',disasterMode:'season',banList:'',disasterCardIds:['S01-DS01','S01-DS02','S01-DS10'],cardRestrictions:[],deckVisibility:'after',ruleContentHash:'rule-content',hash:'rules',capturedAt:'2026-09-28T02:00:00Z'},
 roundMinutes:50,checkInMinutes:10,timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},usesLegacyRoundClock:false,counts,
 organizerTransferHistory:[],participants:[
  participant('qa-organizer','十二军团赛事组',{deck:{name:'秩序观星控制',hash:'deck-1',submittedAt:'2026-09-28T01:00:00Z',lockedAt:'2026-09-28T01:10:00Z',masterId:'M1',cardIds:[],moraleIds:[],specialIds:[]}}),
  participant('p2','北境观星者',{deck:{name:'北境构筑',hash:'deck-2',submittedAt:'2026-09-28T01:00:00Z',lockedAt:'2026-09-28T01:10:00Z',masterId:'M2',cardIds:[],moraleIds:[],specialIds:[]}}),
  participant('p3','长安夜行人',{checkedIn:false,waitlisted:true,waitlistPosition:1}),
  participant('p4','海潮回声',{removed:true,registrationBanned:true,removalReason:'工作人员记录'}),
 ],
 rounds:[{id:'round-3',number:3,status:'running',paused:false,totalPausedSeconds:0,stage:'swiss',standings,matches:[
  match('m1',1,'qa-organizer','十二军团赛事组','p2','北境观星者',{canEnter:true}),
  match('m2',2,'p3','长安夜行人','p4','海潮回声',{canSpectate:true,paused:true,pauseReason:'等待裁判复核'}),
  match('m3',3,'p5','群星信使','p6','雨夜守望者',{status:'completed',result:'playerA'}),
 ]}],
 version:12,legacyImported:false,createdAt:'2026-09-01T00:00:00Z',updatedAt:'2026-09-28T02:00:00Z',swissRounds:5,cutSize:8,registrationVisibility:'public',lateGraceMinutes:10,finalSwissStandings:standings,eliminationBracket:[],phase:'running',registrationOpen:false,
 postponements:[{id:'post-1',previousStartAt:'2026-10-03T10:00:00Z',newStartAt:'2026-10-03T11:00:00Z',reason:'避开服务器维护窗口',actorId:'qa-organizer',actorName:'十二军团赛事组',createdAt:'2026-09-27T02:00:00Z'}],
 judgeCases:[
  {id:'case-1',roundNumber:3,matchId:'m2',table:2,category:'rules',urgency:'urgent',status:'open',requesterAccountId:'p3',requesterName:'长安夜行人',playerMessage:'双方对连续触发结算顺序有不同理解，请裁判到桌确认。',createdAt:'2026-09-28T02:00:00Z',updatedAt:'2026-09-28T02:00:00Z',canManage:true},
  {id:'case-2',roundNumber:3,matchId:'m1',table:1,category:'result',urgency:'normal',status:'ruled',requesterAccountId:'qa-organizer',requesterName:'十二军团赛事组',playerMessage:'赛果记录需要复核。',resolution:'维持当前赛果，并补记本桌用时。',createdAt:'2026-09-28T01:00:00Z',updatedAt:'2026-09-28T02:00:00Z',canManage:false},
 ]
}
if(new URLSearchParams(location.search).get('state')==='registration'){
 tournament.status='registration'
 tournament.phase='pre-check-in'
 tournament.registrationOpen=false
}
const summary=(index,extra={})=>({id:'s-'+index,code:'L12-0'+index,name:['十二军团秋季公开赛','新人瑞士轮练习赛','周末单败挑战','城市交流联赛','裁判培训模拟赛','夏季冠军回顾'][index-1],organizerName:index===1?'十二军团赛事组':'社区主办者 '+index,status:index>4?'completed':index===3?'registration':'running',phase:index===3?'registration-open':'running',format:index===3?'single':'swiss',visibility:'public',maxPlayers:index===3?16:32,startAt:'2026-10-0'+index+'T11:00:00Z',counts:{...counts,active:8+index,waitlisted:index%2},viewerRole:index===1?'organizer':index===2?'participant':'viewer',requiresAction:index<=2,version:index,updatedAt:'2026-09-28T02:00:00Z',...extra})
const summaries=Array.from({length:6},(_,index)=>summary(index+1))
platform.tournamentApi.summaries=async query=>({platformVersion:21,items:query.section==='history'?summaries.filter(item=>item.status==='completed'):summaries,page:1,pageSize:24,total:summaries.length,totalPages:1})
platform.tournamentApi.career=async()=>({accountId:'qa-organizer',participated:18,organized:6,refereed:9,wins:31,losses:14,draws:2,items:summaries.slice(0,4).map((item,index)=>({tournamentId:item.id,code:item.code,name:item.name,status:item.status,format:item.format,finalRank:index+1,wins:3-index,losses:index,draws:0,organized:index===0,refereed:index===1})),page:1,pageSize:12,total:4,totalPages:1})
platform.tournamentApi.getByCode=async()=>structuredClone(tournament)
platform.tournamentApi.startCheck=async()=>({canStart:false,blockers:['仍有 3 名正式席位玩家未完成赛前签到'],warnings:['当前有 1 个紧急裁判案件待处理'],eligiblePlayers:13,waitlistedPlayers:2,openJudgeCases:1})
for(const name of ['register','drop','preCheckIn','removeParticipant','setRegistrationBan','nextRound','startRound','pauseRound','pauseMatch','checkIn','setPhase','start','postpone','cancel','complete','requestOrganizerTransfer','decideOrganizerTransfer','createJudgeCase','assignJudgeCase','resolveJudgeCase','appealJudgeCase']) platform.tournamentApi[name]=async()=>structuredClone(tournament)
platform.tournamentApi.exportCsv=async()=>new Blob(['赛事'])
platform.friendApi.friends=async()=>[{accountId:'friend-1',username:'裁判·白露',status:'accepted',direction:'none',createdAt:'2026-08-01T00:00:00Z',online:true},{accountId:'friend-2',username:'赛事协作者·霜降',status:'accepted',direction:'none',createdAt:'2026-08-01T00:00:00Z',online:false}]

const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 const json=value=>new Response(JSON.stringify(value),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/decks'))return json([{name:'秩序观星控制',masterId:'M1',cardIds:[],moraleIds:[],specialIds:[],updatedAt:'2026-09-28T00:00:00Z'}])
 if(url.includes('/api/operations/effective-policy'))return json({version:3,season:{},disasterCardIds:['S01-DS01','S01-DS02','S01-DS03','S01-DS04','S01-DS05','S01-DS06','S01-DS07','S01-DS08','S01-DS10'],matchModes:[],defaultRoomConfig:{},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]})
 return originalFetch(input,init)
}

const [{default:TournamentHubPage},{default:TournamentDetailPage}]=await Promise.all([import('/src/l12/site/TournamentHubPage.vue'),import('/src/l12/site/TournamentDetailPage.vue')])
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/battle/tournaments',component:TournamentHubPage},{path:'/battle/tournaments/:code',component:TournamentDetailPage}]})
const target=new URLSearchParams(location.search).get('route')||'/battle/tournaments'
await router.push(target)
await router.isReady()
createApp({render:()=>h('div',{class:'site-shell'},[h('div',{class:'site-content'},[h(RouterView)])])}).use(router).mount('#app')
`

let browser
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-tournament-center-visual'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'tournament-center-visual-fixture',
    resolveId(id) { if (id === '/__tournament_visual__.js') return id },
    load(id) { if (id === '/__tournament_visual__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__tournament_visual__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><style>html,body,#app{margin:0;min-height:100%;background:#060a0d}.site-content{min-height:100vh}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__tournament_visual__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = {
  desktop: { width: 1440, height: 900 },
  wide: { width: 1920, height: 1080 },
  mobile: { width: 390, height: 844 },
  narrow: { width: 360, height: 780 },
}

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: viewports.desktop, deviceScaleFactor: 1 })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  const open = async (route, viewport, state = '') => {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__tournament_visual__?route=${encodeURIComponent(route)}${state ? `&state=${encodeURIComponent(state)}` : ''}`)
    await page.locator('.hub-page,.detail-page').first().waitFor({ timeout: 15000 })
    await page.waitForTimeout(250)
  }
  const assertGeometry = async label => {
    const geometry = await page.evaluate(() => {
      const overflow = document.documentElement.scrollWidth - innerWidth
      const controls = [...document.querySelectorAll('.hub-page button,.hub-page input,.hub-page select,.detail-page button,.detail-page input,.detail-page select')]
        .filter(element => {
          const style = getComputedStyle(element)
          const input = element instanceof HTMLInputElement ? element.type : ''
          return style.display !== 'none' && style.visibility !== 'hidden' && !['checkbox','radio'].includes(input)
        }).map(element => ({ text: element.getAttribute('aria-label') || element.textContent?.trim() || element.getAttribute('placeholder') || element.tagName, html: element.outerHTML.slice(0, 180), height: element.getBoundingClientRect().height, minHeight: getComputedStyle(element).minHeight, maxHeight: getComputedStyle(element).maxHeight }))
      const tournamentStyle = [...document.querySelectorAll('style')].map(style => style.textContent || '').find(text => text.includes('min-height:48px!important')) || ''
      const ruleAt = tournamentStyle.indexOf('min-height:48px!important')
      return { viewportWidth: innerWidth, compact: matchMedia('(max-width:700px)').matches, ruleSnippet: tournamentStyle.slice(Math.max(0, ruleAt - 100), ruleAt + 100), overflow, tooSmall: innerWidth <= 700 ? controls.filter(item => item.height < 43.5) : [] }
    })
    if (geometry.overflow > 1) throw new Error(`${label}: horizontal overflow ${geometry.overflow}px`)
    if (geometry.tooSmall.length) throw new Error(`${label}: controls below 44px ${JSON.stringify(geometry)}`)
  }
  const shot = async name => {
    await assertGeometry(name)
    await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: true })
    console.log(`captured ${name}`)
  }
  const clickTab = async text => {
    await page.getByRole('button', { name: text, exact: true }).click()
    await page.waitForTimeout(120)
  }

  await open('/battle/tournaments', viewports.desktop); await shot('01-hub-discover-1440x900')
  await page.setViewportSize(viewports.wide); await shot('02-hub-discover-1920x1080')
  await page.setViewportSize(viewports.mobile); await shot('03-hub-discover-390x844')
  await clickTab('我的赛事'); await shot('04-hub-mine-career-390x844')
  await page.getByRole('button', { name: '创建赛事', exact: true }).click(); await page.waitForTimeout(300); await shot('05-create-basic-390x844')
  await clickTab('下一步'); await clickTab('下一步'); await clickTab('下一步'); await shot('06-create-ranked-timing-390x844')
  await clickTab('下一步'); await shot('07-create-preview-390x844')

  await open('/battle/tournaments/L12-AUTUMN', viewports.desktop); await shot('08-detail-overview-1440x900')
  await page.setViewportSize(viewports.mobile); await clickTab('赛程'); await shot('09-detail-schedule-390x844')
  await clickTab('排名'); await shot('10-detail-standings-390x844')
  await clickTab('参赛者'); await shot('11-detail-participants-390x844')
  await clickTab('裁判台'); await shot('12-detail-judge-390x844')
  await open('/battle/tournaments/L12-AUTUMN', viewports.mobile, 'registration'); await clickTab('主办管理'); await page.getByRole('button', { name: '执行开赛检查', exact: true }).click(); await page.waitForTimeout(150); await shot('13-detail-management-390x844')
  await page.setViewportSize(viewports.narrow); await shot('14-detail-management-360x780')

  if (errors.length) throw new Error(`page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'index.json'), JSON.stringify({ generatedAt: new Date().toISOString(), screenshots: fs.readdirSync(output).filter(name => name.endsWith('.png')), assertions: ['no-horizontal-overflow', 'mobile-controls-at-least-44px', 'no-page-errors'] }, null, 2))
  console.log(JSON.stringify({ output, screenshots: fs.readdirSync(output).filter(name => name.endsWith('.png')).length }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
