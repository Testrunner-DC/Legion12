import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const out = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/c3-public-browser')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })
const sources = ['src/router/index.ts','src/l12/platform.ts','src/l12/site/TournamentHubPage.vue','src/l12/site/TournamentDetailPage.vue','src/l12/site/TournamentAccountDetail.vue','src/l12/site/TournamentPublicDetail.vue','src/l12/site/TournamentSummaryList.vue','src/l12/site/TournamentManagementPanel.vue','src/l12/site/RankingsPage.vue']
const hashes = () => Object.fromEntries(sources.map(file => [file,crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')]))
const before = hashes()
const entry = String.raw`
import { createApp,h } from 'vue'
import { RouterView } from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
import * as platform from '/src/l12/platform.ts'
const { router } = await import('/src/router/index.ts')
router.removeRoute('me'); router.addRoute({path:'/me',name:'me',component:{render:()=>h('div',{class:'login-fixture'},'登录')}})
window.qaRequests=[]; window.qaPrivatePending=null; window.qaWithdrawn=false
const counts={registered:16,checkedIn:13,active:14,waitlisted:2}
const summary={code:'L12-PUBLIC',name:'公开赛事与很长的中文标题适配检查',organizerName:'公开主办者',status:'running',format:'swiss',phase:'running',maxPlayers:32,startAt:'2026-10-04T10:00:00Z',roundMinutes:50,checkInMinutes:10,counts}
const standing={roundNumber:1,rank:1,name:'公开玩家',wins:1,losses:0,draws:0,byes:0,opponentScore:2,opponentsOpponentScore:3}
const publicDetail={...summary,description:'公开赛事说明，用于检查真实页面加载、切换和登录返回。',registrationVisibility:'public',registrationOpen:false,rules:{ruleset:'现行规则',disasterMode:'season',banList:'',disasterCardIds:[],cardRestrictions:[],deckVisibility:'private',swissRounds:4,cutSize:null,lateGraceMinutes:10,timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60}},participants:[{name:'公开玩家',status:'active'}],rounds:[{number:1,stage:'swiss',status:'completed',matches:[{table:1,playerAName:'公开玩家',playerBName:'公开对手',status:'completed',result:'player-a'}],standings:[standing]}],finalStandings:[standing]}
const privateDetail={...publicDetail,id:'PRIVATE_TOURNAMENT_ID',visibility:'public',organizerAccountId:'qa-user',referees:[],version:5,usesLegacyRoundClock:false,organizerTransferHistory:[],participants:[{accountId:'qa-user',username:'公开玩家',checkedIn:true,dropped:false,eliminated:false,removed:false,registrationBanned:false,seed:1,waitlisted:false}],rounds:[],swissRounds:4,registrationVisibility:'public',finalSwissStandings:[],eliminationBracket:[],postponements:[],judgeCases:[],rules:{...publicDetail.rules,hash:'PRIVATE_HASH',ruleContentHash:'PRIVATE_CONTENT_HASH',capturedAt:''}}
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
 const url=new URL(typeof input==='string'?input:input.url,location.href)
 if(!url.pathname.includes('/api/'))return originalFetch(input,init)
 window.qaRequests.push({path:url.pathname,query:url.search,method:init.method||'GET',authorization:new Headers(init.headers).get('Authorization'),credentials:init.credentials,cache:init.cache,body:init.body})
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json','Cache-Control':'no-store'}})
 if(url.pathname.includes('/api/public/tournaments/summaries'))return json({items:[summary],page:Number(url.searchParams.get('page')||1),pageSize:24,total:25,totalPages:2})
 if(url.pathname.includes('/api/public/tournaments/code/')){
  if(window.qaWithdrawn || url.pathname.endsWith('L12-CODE') || url.pathname.endsWith('L12-MISSING'))return json({message:'赛事暂未公开或不存在'},404)
  const value=structuredClone(publicDetail)
  if(url.pathname.endsWith('L12-STAFF')){value.registrationVisibility='staff';value.participants=[];value.rounds[0].matches[0].playerAName=null;value.rounds[0].matches[0].playerBName=null;value.rounds[0].standings=[];value.finalStandings=[]}
  return json(value)
 }
 if(url.pathname.includes('/api/tournaments/summaries')){
  const value={platformVersion:8,items:[{...summary,id:'PRIVATE_SUMMARY',counts:{...counts,pendingCheckIn:3,dropped:1,removed:0,registrationBanned:0},viewerRole:'organizer',requiresAction:true,version:5,updatedAt:''}],page:1,pageSize:24,total:1,totalPages:1}
  if(window.qaDelayPrivate)return await new Promise(resolve=>{window.qaPrivatePending=()=>resolve(json(value))})
  return json(value)
 }
 if(url.pathname.endsWith('/visibility') && init.method==='PUT'){
  const body=JSON.parse(init.body);privateDetail.visibility=body.visibility;privateDetail.registrationVisibility=body.registrationVisibility;privateDetail.version++;window.qaWithdrawn=body.visibility==='code';return json(privateDetail)
 }
 if(url.pathname.includes('/api/tournaments/code/'))return json(privateDetail)
 if(url.pathname.includes('/api/tournaments/career'))return json({accountId:'qa-user',participated:1,organized:1,refereed:0,wins:1,losses:0,draws:0,items:[],page:1,pageSize:12,total:0,totalPages:0})
 if(url.pathname.includes('/api/decks'))return json([])
 if(url.pathname.includes('/api/friends'))return json([])
 if(url.pathname.includes('/api/rankings/history'))return json({honors:[],factionTotals:[]})
 if(url.pathname.includes('/api/rankings'))return json({players:[{rank:1,username:'公开榜单玩家',faction:'秩序',sevenValue:100,displayValue:'100',tier:'冠冕',titles:[],wins:3,losses:1,winStreak:0}],analytics:{range:url.searchParams.get('range')||'season',summary:{matches:4,placedPlayers:1,activeMasters:0},masters:[],matchups:[]},rangeLimited:false})
 return json({message:'unhandled fixture endpoint'},400)
}
platform.authState.initialized=true; platform.authState.verified=false; platform.platformState.account=null; platform.platformState.token=''
window.qaIdentity=(role='guest')=>{
 if(role==='guest'){platform.authState.verified=false;platform.platformState.account=null;platform.platformState.token='';return}
 platform.platformState.account={id:'qa-user',username:'公开玩家',role,permissions:[],mustChangePassword:false,mustChangeUsername:false};platform.platformState.token='qa-token-'+role;platform.authState.verified=true
}
window.qaPublicAuth=()=>platform.platformRequest('/api/public/tournaments/summaries',{headers:{Authorization:'Bearer forbidden'},credentials:'include'})
window.qaNavigate=target=>router.push(target)
window.qaRoute=()=>router.currentRoute.value.fullPath
window.qaResolvePrivate=()=>{window.qaPrivatePending?.();window.qaPrivatePending=null}
await router.push(new URLSearchParams(location.search).get('target')||'/battle/tournaments');await router.isReady()
createApp({render:()=>h(RouterView)}).use(router).mount('#app');window.qaReady=true
`
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(process.env.TEMP||out,'vite-c3-public'),server:{host:'127.0.0.1',port:0},plugins:[{
 name:'public-browsing-fixture',resolveId:id=>id==='/__c3_fixture__.js'?id:undefined,load:id=>id==='/__c3_fixture__.js'?entry:undefined,
 configureServer(dev){dev.middlewares.use((req,res,next)=>{if(!req.url?.startsWith('/__c3_fixture__?'))return next();res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body,#app{margin:0;min-height:100%;background:#060a0d}</style><div id="app"></div><script type="module" src="/__c3_fixture__.js"></script>')})},
}]})
let browser; const checks=[]; const errors=[]
const sizes=[[320,568],[360,640],[390,844],[430,932],[568,320],[667,375],[740,360],[844,390],[768,1024],[1024,768],[1366,768],[1920,1080],[2560,1440]]
try{
 await server.listen();const port=server.httpServer.address().port;browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage({hasTouch:true});page.on('pageerror',e=>errors.push(e.message))
 await page.route('**/*',r=>new URL(r.request().url()).hostname==='127.0.0.1'?r.continue():r.abort())
 const open=async(target)=>{await page.goto(`http://127.0.0.1:${port}/__c3_fixture__?target=${encodeURIComponent(target)}`);await page.waitForFunction(()=>window.qaReady);await page.locator('.hub-page,.detail-page,.ranking-page,.login-fixture').first().waitFor();await page.waitForTimeout(120)}
 const geometry=async(label)=>{const value=await page.evaluate(()=>({overflow:document.documentElement.scrollWidth-innerWidth,small:innerWidth<=700?[...document.querySelectorAll('main button,main input,main select,.ranking-page button,.ranking-page input,.ranking-page select')].filter(e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&e.getBoundingClientRect().height<43.5).map(e=>e.textContent.trim()):[]}));assert.ok(value.overflow<=1,label+JSON.stringify(value));assert.deepEqual(value.small,[],label+' small controls');checks.push(label)}
 const noGuestPrivate=async()=>{const requests=await page.evaluate(()=>window.qaRequests);assert.equal(requests.filter(r=>r.path.includes('/api/')&&!r.path.includes('/api/public/')&&!r.path.includes('/api/rankings')).length,0);assert.ok(requests.every(r=>!r.authorization));checks.push('guest no personal requests')}
 for(const [w,h]of sizes){
  await page.setViewportSize({width:w,height:h});await open('/battle/tournaments');await page.getByRole('button',{name:'历史赛事',exact:true}).click();await page.waitForTimeout(80);await geometry(`hub ${w}x${h}`);await noGuestPrivate()
  await page.getByRole('button',{name:'下一页',exact:true}).click();await page.waitForTimeout(80);assert.ok((await page.evaluate(()=>window.qaRequests)).some(r=>r.query.includes('page=2')));checks.push(`pagination ${w}`)
  await page.locator('.summary-card').first().click();await page.locator('.public-detail').waitFor();await page.getByRole('button',{name:'赛程',exact:true}).click();await geometry(`schedule ${w}x${h}`)
  await page.getByRole('button',{name:'排名',exact:true}).click();await geometry(`standings ${w}x${h}`)
  await page.getByRole('button',{name:'规则',exact:true}).click();assert.ok((await page.locator('.rules').innerText()).includes('分钟'));assert.ok(!(await page.locator('.rules').innerText()).includes('秒'));await geometry(`rules ${w}x${h}`);await noGuestPrivate()
  if([390,1366,1920].includes(w))await page.screenshot({path:path.join(out,`public-detail-${w}x${h}.png`),fullPage:true})
  await open('/battle/rankings');await page.getByRole('button',{name:'近7天',exact:true}).click();await page.waitForTimeout(80);assert.ok((await page.evaluate(()=>window.qaRequests)).some(r=>r.query.includes('range=7d')));await noGuestPrivate();await geometry(`rankings ${w}x${h}`)
 }
 await open('/battle/tournaments/L12-PUBLIC?ref=share');await page.getByRole('button',{name:'登录参与赛事',exact:true}).click();await page.locator('.login-fixture').waitFor();assert.equal(await page.evaluate(()=>window.qaRoute()),'/me?redirect=/battle/tournaments/L12-PUBLIC?ref=share');checks.push('detail login preserves code and query')
 await open('/battle/tournaments?section=mine');await page.locator('.login-fixture').waitFor();assert.ok((await page.evaluate(()=>window.qaRoute())).includes('section=mine'));await noGuestPrivate();checks.push('private section login preserves destination')
 await open('/battle/tournaments/L12-CODE?ref=share');assert.ok((await page.locator('.public-detail').innerText()).includes('赛事暂未公开'));await noGuestPrivate();checks.push('code only fails closed')
 await page.getByRole('button',{name:'登录后继续',exact:true}).click();await page.locator('.login-fixture').waitFor();assert.equal(await page.evaluate(()=>window.qaRoute()),'/me?redirect=/battle/tournaments/L12-CODE?ref=share');checks.push('code only login preserves private destination and query')
 await page.evaluate(()=>window.qaIdentity('player'));await page.evaluate(()=>window.qaNavigate('/battle/tournaments/L12-CODE?ref=share'));await page.locator('.detail-page:not(.public-detail)').waitFor();assert.ok((await page.evaluate(()=>window.qaRequests)).some(r=>r.path.endsWith('/api/tournaments/code/L12-CODE')&&r.authorization));checks.push('code detail private request happens only after verified login')
 await open('/battle/tournaments/L12-MISSING?ref=share');assert.ok((await page.locator('.public-detail').innerText()).includes('赛事暂未公开'));assert.equal(await page.getByRole('button',{name:'登录后继续',exact:true}).count(),1);await noGuestPrivate();checks.push('missing code has identical generic login affordance')
 await open('/battle/tournaments/L12-STAFF');await page.getByRole('button',{name:'赛程',exact:true}).click();assert.ok(!(await page.locator('.public-detail').innerText()).includes('公开玩家'));await page.getByRole('button',{name:'排名',exact:true}).click();assert.ok((await page.locator('.public-detail').innerText()).includes('仅工作人员可见'));checks.push('staff visibility all branches anonymous')
 await open('/battle/tournaments/L12-PUBLIC');await page.evaluate(()=>{window.qaWithdrawn=true;window.dispatchEvent(new Event('l12-resource-tournaments'))});await page.waitForTimeout(100);assert.ok(!(await page.locator('.public-detail').innerText()).includes('公开主办者'));checks.push('withdrawal clears old content')
 await open('/battle/tournaments');await page.evaluate(()=>window.qaIdentity('admin'));await page.waitForTimeout(100);await page.evaluate(()=>window.qaPublicAuth());const publicRead=(await page.evaluate(()=>window.qaRequests)).filter(r=>r.path.includes('/api/public/')).at(-1);assert.equal(publicRead.authorization,null);assert.equal(publicRead.credentials,'omit');assert.equal(publicRead.cache,'no-store');checks.push('admin public request strips explicit credentials')
 await page.evaluate(()=>{window.qaDelayPrivate=true;window.dispatchEvent(new Event('l12-resource-tournaments'))});await page.waitForFunction(()=>!!window.qaPrivatePending);await page.evaluate(()=>window.qaIdentity('guest'));await page.waitForTimeout(100);await page.evaluate(()=>window.qaResolvePrivate());await page.waitForTimeout(100);assert.equal(await page.getByRole('button',{name:'我的赛事',exact:true}).count(),0);assert.equal(await page.locator('.summary-card .role').count(),0);checks.push('late private list cannot render after logout')
 for(const role of ['player','referee','admin']){
  await open('/battle/tournaments/L12-PUBLIC');await page.evaluate(role=>window.qaIdentity(role),role);await page.locator('.detail-page:not(.public-detail)').waitFor();assert.equal(await page.getByRole('button',{name:'裁判台',exact:true}).count(),1)
  if(role==='admin'){
   await page.getByRole('button',{name:'主办管理',exact:true}).click();await page.getByLabel('赛事可见性').selectOption('code');await page.getByLabel('参赛名单').selectOption('staff');await page.getByRole('button',{name:'保存公开范围',exact:true}).click();await page.waitForTimeout(100)
   const command=(await page.evaluate(()=>window.qaRequests)).find(r=>r.method==='PUT'&&r.path.endsWith('/visibility'));assert.ok(command);const body=JSON.parse(command.body);assert.equal(body.expectedVersion,5);assert.equal(body.visibility,'code');assert.equal(body.registrationVisibility,'staff');assert.ok(body.idempotencyKey);checks.push('organizer actual controls submit one versioned visibility command')
  }
  await page.evaluate(()=>window.qaIdentity('guest'));await page.locator('.public-detail').waitFor();assert.equal(await page.getByRole('button',{name:'裁判台',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:'主办管理',exact:true}).count(),0);checks.push(`${role} detail logout removes staff surfaces`)
 }
 assert.deepEqual(errors,[]);assert.deepEqual(hashes(),before)
 const report={success:true,generatedAt:new Date().toISOString(),checks:checks.length,results:checks,viewports:sizes,errors,sourceHashes:before,limitations:['Chromium Edge browser emulation only; not physical iOS Safari or WeChat keyboard','API payloads are synthetic; server HTTP/security behavior is separately covered by platform tests']}
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2))
}catch(e){fs.writeFileSync(path.join(out,'failure.json'),JSON.stringify({error:String(e.stack),checks,errors,sourceHashes:before},null,2));throw e}finally{await browser?.close();await server.close()}
