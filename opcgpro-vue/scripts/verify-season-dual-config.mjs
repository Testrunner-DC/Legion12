import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/season-dual-config')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h, ref } from 'vue'
import AdminOperationsPanel from '/src/l12/site/AdminOperationsPanel.vue'
import { authState, platformState } from '/src/l12/platform.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'
const readonly = new URLSearchParams(location.search).has('readonly')
platformState.account = { id:'qa-admin', username:'双槽验收管理员', role:'admin', permissions:readonly?['admin.operations.read']:['admin.operations.read','admin.operations.write'] }
platformState.token='fixture'; authState.initialized=true; authState.verified=true
l12State.endpoint=location.origin.replace('http','ws')+'/ws'
const notice=ref('')
createApp({render:()=>h('main',{style:'box-sizing:border-box;max-width:1500px;margin:12px auto;padding:0 8px'},[
 h('p',{id:'notice'},notice.value),h(AdminOperationsPanel,{onNotice:value=>notice.value=value})
])}).mount('#app')
`

const server = await createServer({ root, configLoader:'runner', server:{host:'127.0.0.1',port:0}, plugins:[{
  name:'season-dual-config-fixture',
  resolveId(id){ if(id==='/__season_dual__.js') return id },
  load(id){ if(id==='/__season_dual__.js') return entry },
  configureServer(vite){ vite.middlewares.use((request,response,next)=>{
    if(request.url?.split('?')[0]==='/__season_dual__'){response.setHeader('Content-Type','text/html');response.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__season_dual__.js"></script>');return} next()
  })},
}]})

const tier=(name,minimum)=>({name,minimum,baseDelta:100,winStreakCap:50,lossProtectionCap:40,ratingGapCap:30,streakTerminationReward:20,color:'#d7ba63',icon:'◆'})
const ranked=()=>({placementMatches:5,placementMaximum:19999,broadcastEnabled:true,
  factions:['order','chaos','fate'].map((id,i)=>({id,name:['秩序超长派系称号','混沌超长派系称号','命运超长派系称号'][i],color:'#d7ba63',icon:'◆',firstTitle:'第一名称号超长文本',topFiveTitle:'第二至五名称号超长文本',tiers:['新星','星火','群星','璀璨','永恒'].map((name,j)=>tier(name,j*10000))})),
  masterTitles:[{masterId:'S01-0001',masterName:'具有很长显示名称的验收主宰',title:'最强玩家称号长文本'}],
  timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},
  broadcast:{displaySeconds:16,lobbyDelaySeconds:3,intervalSeconds:15,winStreakThreshold:5,streakEndedThreshold:5,minimumTierIndex:0,winStreakEnabled:true,streakEndedEnabled:true,highestTierEnabled:true,factionTitleEnabled:true,masterTitleEnabled:true}})
const scoped=(suffix)=>({disasterPool:{cardIds:['S01-DS01','S01-DS02','S01-DS03','S01-DS04','S01-DS05','S01-DS06','S01-DS07','S01-DS08','S01-DS10'],annihilationLocked:true},cardRestrictions:[],defaultPresetDeckIds:['preset-'+suffix],ranked:ranked()})
const definition=(slot,id,name,revision)=>({definitionId:'definition-'+slot,seasonId:id,name,startsAt:slot==='current'?'2026-09-01T00:00:17.123Z':'2027-01-01T00:00:29.456Z',endsAt:slot==='current'?'2026-12-31T16:00:31.789Z':'2027-04-30T16:00:43.654Z',configuration:scoped(slot),lifecycleStatus:slot==='current'?'active':'draft',revision,createdBy:'qa-admin',createdAt:'2026-09-01T00:00:00Z',updatedBy:'qa-admin',updatedAt:'2026-09-30T00:00:00Z'})
const operationsConfig={season:{id:'S01',name:'权威当前赛季',status:'active',startsAt:'2026-09-01T00:00:00Z',endsAt:'2026-12-31T16:00:00Z'},disasterPool:{cardIds:['S01-DS01','S01-DS10'],annihilationLocked:true},cardRestrictions:[],defaultPresetDeckIds:['authoritative-global'],matchModes:[{id:'casual',name:'休闲',enabled:true},{id:'ranked',name:'排位',enabled:true}],defaultRoomConfig:{matchModeId:'casual',spectating:'public',handVisibility:'request',disasterMode:'all'},featureFlags:{spectating:true},maintenance:{enabled:true,message:'预约维护长文本',startsAt:'2026-10-01T00:00:00Z',endsAt:'2026-10-01T02:00:00Z',advanceBroadcastHours:2,expectedDurationHours:2},announcements:[{id:'a1',content:'第一条长期公告',enabled:true,sortOrder:0},{id:'a2',content:'第二条长期公告',enabled:true,sortOrder:1}]}
let current=definition('current','S01','当前赛季超长名称用于浏览器验收',3)
let next=definition('next','S02','下赛季草稿超长名称用于浏览器验收',7)
let operationsVersion=41, genericVersion=91, createdDraftSequence=0, failApply='', failPreview='', previewDelay=false, failCreate=false, failDelete=false, activationGeneration=0
const requests=[]
const clone=value=>value===undefined?undefined:JSON.parse(JSON.stringify(value))
const error=(status,message,code)=>({status,json:{message,code,correlationId:'qa-correlation'}})

let browser
try{
 await server.listen(); const port=server.httpServer.address().port
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const context=await browser.newContext({viewport:{width:1366,height:768},timezoneId:'Asia/Shanghai'})
 const page=await context.newPage(); const pageErrors=[],consoleErrors=[],expectedHttpErrors=[]
 page.on('pageerror',e=>pageErrors.push(e.message)); page.on('console',m=>{if(m.type()==='error')(/Failed to load resource/.test(m.text())?expectedHttpErrors:consoleErrors).push(m.text())})
 await page.route('**/*',async route=>{
  const req=route.request(),url=new URL(req.url()); if(url.hostname!=='127.0.0.1')return route.abort()
  if(url.pathname.startsWith('/data/l12/'))return route.fulfill({json:[]})
  if(!url.pathname.startsWith('/api/'))return route.continue()
  let body; try{body=req.postData()?req.postDataJSON():undefined}catch{}
  requests.push({method:req.method(),path:url.pathname,body:clone(body),headers:req.headers()})
  if(url.pathname==='/api/admin/operations/config'&&req.method()==='GET')return route.fulfill({json:{version:genericVersion,versionId:'global-'+genericVersion,config:clone(operationsConfig),updatedBy:'qa',updatedAt:'2026-09-30T00:00:00Z',immediateMaintenance:{enabled:false,expectedDurationHours:2}}})
  if(url.pathname==='/api/admin/operations/config/history')return route.fulfill({json:[{id:'history-90',version:90,action:'apply',config:clone(operationsConfig),actorId:'qa',actorName:'验收管理员',reason:'保留版本回滚能力',createdAt:'2026-09-29T00:00:00Z'}]})
  if(url.pathname==='/api/admin/runtime/status')return route.fulfill({json:{serviceVersion:'qa-runtime-precision',observedAt:'2026-09-30T00:00:00Z',cardCount:324,onlineAccountCount:12,webSocketConnectionCount:10,roomCount:4,activeGameCount:3,cdn:{name:'卡图 CDN',configured:true,state:'healthy',detail:'可用',observedAt:'2026-09-30T00:00:00Z'},httpPerformance:{windowSeconds:300,slowRequestThresholdMilliseconds:800,minimumSamples:20,minimumReadSamples:10,minimumMutationSamples:5,sampleCount:50,readSampleCount:40,mutationSampleCount:10,diagnosticRequestCount:2,inFlight:1,peakInFlight:4,averageDurationMilliseconds:25,p95LatencyBand:'25-50ms',slowRequestCount:0,slowRequestPercent:0,rateLimitedCount:0,rateLimitedPercent:0,serverErrorCount:0,serverErrorPercent:0,expectedUnavailableCount:0,expectedUnavailablePercent:0,clientCancelledCount:0,clientCancelledPercent:0,sampleSufficient:true,withinBudget:true,budgetFailures:[]}}})
  if(url.pathname==='/api/ranked/broadcasts')return route.fulfill({json:[{id:'broadcast-long',matchId:'match',eventType:'streak',message:'这是用于测试换行的非常非常长的排位快讯消息',createdAt:'2026-09-30T00:00:00Z'}]})
  if(url.pathname==='/api/admin/seasons'&&req.method()==='GET')return route.fulfill({json:{current:clone(current),next:next?clone(next):undefined,archives:[],automaticActivationEnabled:false,operationsVersion}})
  const activationMatch=url.pathname.match(/^\/api\/admin\/seasons\/draft\/([^/]+)\/(activation-preview|arm|disarm)$/)
  if(activationMatch&&activationMatch[2]==='activation-preview'&&req.method()==='POST'){
    const token=`impact-${activationMatch[1]}-${body.expectedCurrentRevision}-${body.expectedDraftRevision}-${body.expectedVersion}`
    return route.fulfill({json:{valid:true,observedAt:'2026-09-30T00:10:00Z',definitionId:activationMatch[1],currentRevision:body.expectedCurrentRevision,draftRevision:body.expectedDraftRevision,operationsVersion:body.expectedVersion,planStatus:next?.activationPlan?.status||'unarmed',planGeneration:next?.activationPlan?.generation||0,leaseState:next?.activationPlan?.leaseState||'free',readiness:{seasonId:current.seasonId,activeMatches:0,pendingSettlements:0,appliedReconciliationFailures:0,quarantinedSettlements:0,ready:true},settlementParticipantCount:128,historyRecordCount:128,summaryNotificationCount:128,currentToHistory:{seasonId:current.seasonId,seasonName:current.name,fromStatus:'active',toStatus:'history'},nextToCurrent:{seasonId:next.seasonId,seasonName:next.name,fromStatus:'draft',toStatus:'active'},rankedAdmissionImpact:'fence-after-arm-at-scheduled-time',rankedAdmissionFencesAt:next.startsAt,blockingCodes:[],suggestedActionCodes:['preview-and-arm'],previewToken:token}})
  }
  if(activationMatch&&activationMatch[2]==='arm'&&req.method()==='POST'){
    assert.match(body.impactPreviewToken,/^impact-/);activationGeneration++;next={...next,revision:next.revision+1,activationPlan:{status:'armed',generation:activationGeneration,scheduledAt:next.startsAt,armedAt:'2026-09-30T00:11:00Z',armedCurrentRevision:current.revision,armedDraftRevision:next.revision+1,armedOperationsVersion:operationsVersion,intentMask:'12345678…cdef',disarmGuardToken:`disarm-guard-${activationGeneration}`,leaseState:'free',attemptCount:0,suggestedActionCode:'wait-for-scheduled-cutover'}};return route.fulfill({json:clone(next)})
  }
  if(activationMatch&&activationMatch[2]==='disarm'&&req.method()==='POST'){
    assert.equal(body.expectedDraftRevision,next.revision);assert.equal(body.expectedPlanGeneration,next.activationPlan.generation);assert.equal(body.disarmGuardToken,next.activationPlan.disarmGuardToken)
    next={...next,revision:next.revision+1,activationPlan:{...next.activationPlan,status:'disarmed',generation:(next.activationPlan?.generation||0)+1,disarmGuardToken:'disarm-guard-disarmed',leaseState:'free',suggestedActionCode:'preview-and-arm'}};return route.fulfill({json:clone(next)})
  }
  const match=url.pathname.match(/^\/api\/admin\/seasons\/([^/]+)(\/preview)?$/)
  if(match&&match[2]&&req.method()==='POST'){
    if(failPreview){const kind=failPreview;failPreview='';return route.fulfill(error(409,kind==='revision'?'赛季槽版本过期':'运营版本过期',kind))}
    if(previewDelay){previewDelay=false;await new Promise(resolve=>setTimeout(resolve,250))}
    const slot=match[1]===current.definitionId?'current':'next',source=slot==='current'?current:next
    const normalized=clone(body.draft);normalized.name=normalized.name.trim()+' · 服务端规范化'
    return route.fulfill({json:{valid:true,slot,currentRevision:source.revision,nextRevision:source.revision+1,operationsVersion,normalized,changes:['name','configuration'],warnings:['一条很长的验收警告用于确认移动端能够正常换行且不会横向溢出'],previewToken:`token-${slot}-${source.revision}-${operationsVersion}`}})
  }
  if(match&&!match[2]&&req.method()==='PUT'){
    if(failApply){const kind=failApply;if(kind!=='network-once')failApply='';else failApply='';return route.fulfill(kind==='network-once'?error(503,'临时网络失败','temporary'):error(409,'预览令牌已过期','stale-preview'))}
    const slot=match[1]===current.definitionId?'current':'next',source=slot==='current'?current:next
    const updated={...source,...clone(body.draft),revision:source.revision+1,updatedAt:'2026-09-30T01:00:00Z'}
    if(slot==='current'){current=updated;operationsVersion++}else next=updated
    return route.fulfill({json:{applied:true,slot,definition:clone(updated),previousRevision:source.revision,operationsVersion,changes:['name']}})
  }
  if(url.pathname==='/api/admin/seasons/draft'&&req.method()==='POST'){
    if(failCreate){failCreate=false;return route.fulfill(error(409,'草稿槽已变化','draft-conflict'))}
    createdDraftSequence++;next={...definition(`created-${createdDraftSequence}`,'',`服务器创建的真实空槽草稿 ${createdDraftSequence}`,1),startsAt:null,endsAt:null};return route.fulfill({json:clone(next)})
  }
  if(url.pathname.startsWith('/api/admin/seasons/draft/')&&req.method()==='DELETE'){
    if(failDelete){failDelete=false;return route.fulfill(error(409,'删除版本冲突','delete-conflict'))}
    next=undefined;current={...current,revision:current.revision+1};return route.fulfill({status:204,body:''})
  }
  if(url.pathname==='/api/admin/operations/config/preview')return route.fulfill({json:{valid:true,currentVersion:genericVersion,nextVersion:genericVersion+1,normalized:clone(body.config),changes:['featureFlags'],warnings:[]}})
  if(url.pathname==='/api/admin/operations/config'&&req.method()==='PUT'){genericVersion++;return route.fulfill({json:{applied:true,current:{version:genericVersion,versionId:'global-'+genericVersion,config:clone(body.config),updatedBy:'qa',updatedAt:'2026-09-30T02:00:00Z'},historyEntry:{},changes:['featureFlags']}})}
  if(url.pathname==='/api/admin/operations/config/rollback')return route.fulfill({json:{applied:true,current:{version:genericVersion+1,versionId:'rollback',config:clone(operationsConfig),updatedBy:'qa',updatedAt:'2026-09-30T02:00:00Z'},historyEntry:{},changes:[]}})
  return route.fulfill({status:404,json:{message:'unexpected '+req.method()+' '+url.pathname}})
 })

 const url=`http://127.0.0.1:${port}/__season_dual__`
 await page.goto(url); await page.getByLabel('赛季 ID').waitFor()
 const currentId=page.getByLabel('赛季 ID'),currentStart=page.getByLabel('开始时间',{exact:true}),name=page.getByLabel('名称',{exact:true}),reason=page.getByPlaceholder('此槽位的变更理由（必填）')
 assert.equal(await currentId.isEditable(),false);assert.equal(await currentStart.isEditable(),false);assert.equal(await name.isEditable(),true)

 // 切季影响预览、预约和取消与配置预览完全分离。
 const activationReason=page.getByPlaceholder('预约或取消预约的理由（必填）')
 await page.getByRole('button',{name:'预览切季影响'}).click();await page.getByText('切季影响已锁定').waitFor();assert.match(await page.locator('.impact-preview').textContent(),/128.*结算参与者.*128.*赛季总结通知/s)
 await activationReason.fill('预约赛季自动切换');await page.getByRole('button',{name:'确认预约'}).click();let activationDialog=page.getByRole('dialog');await activationDialog.waitFor();assert.match(await activationDialog.textContent(),/结算 128 名参与者/);await activationDialog.getByRole('button',{name:'确认预约'}).click();await page.getByText('已预约',{exact:true}).waitFor()
 const armedRequest=requests.filter(x=>x.path.endsWith('/arm')).at(-1);assert.equal(armedRequest.body.expectedCurrentRevision,current.revision);assert.equal(armedRequest.body.expectedDraftRevision,next.revision-1);assert.equal(armedRequest.body.expectedVersion,operationsVersion)
 await activationReason.fill('漂移前的取消意图');await page.getByRole('button',{name:'取消预约'}).click();activationDialog=page.getByRole('dialog');await activationDialog.waitFor();assert.match(await activationDialog.textContent(),/g1.*12345678…cdef/s)
 activationGeneration++;next={...next,revision:next.revision+1,activationPlan:{...next.activationPlan,generation:activationGeneration,intentMask:'87654321…fedc',disarmGuardToken:`disarm-guard-${activationGeneration}`}}
 await page.locator('.operations-version button').evaluate(button=>button.click());await activationDialog.waitFor({state:'detached'});await page.locator('#notice').getByText(/高风险确认已关闭/).waitFor()
 await activationReason.fill('操作冻结后的精确计划');await page.getByRole('button',{name:'取消预约'}).click();activationDialog=page.getByRole('dialog');await activationDialog.waitFor();assert.match(await activationDialog.textContent(),/g2.*87654321…fedc/s);await activationDialog.getByRole('button',{name:'确认取消预约'}).click();await page.getByText('已取消',{exact:true}).waitFor();const disarmRequest=requests.filter(x=>x.path.endsWith('/disarm')).at(-1);assert.equal(disarmRequest.body.expectedPlanGeneration,2);assert.equal(disarmRequest.body.disarmGuardToken,'disarm-guard-2')

 // 三个 section 与双槽草稿、理由相互隔离。
 await name.fill(' current local name ');await reason.fill('当前槽理由')
 await page.getByRole('button',{name:/排位与七曜/}).click();await page.getByLabel('定级场次').fill('9')
 await page.getByRole('button',{name:/构筑规则/}).click();await page.getByLabel(/每行一个官方预组/).fill('current-deck-local')
 await page.getByRole('button',{name:/下赛季草稿/}).click();await page.getByRole('button',{name:/赛季与天灾/}).click();await page.getByLabel('名称',{exact:true}).fill('next local name');await reason.fill('下赛季槽理由')
 await page.getByRole('button',{name:/排位与七曜/}).click();await page.getByLabel('定级场次').fill('12')
 await page.getByRole('button',{name:/构筑规则/}).click();await page.getByLabel(/每行一个官方预组/).fill('next-deck-local')
 await page.getByRole('button',{name:/当前赛季/}).click();assert.equal(await page.getByLabel(/每行一个官方预组/).inputValue(),'current-deck-local');await page.getByRole('button',{name:/排位与七曜/}).click();assert.equal(await page.getByLabel('定级场次').inputValue(),'9');await page.getByRole('button',{name:/赛季与天灾/}).click();assert.equal(await name.inputValue(),' current local name ');assert.equal(await reason.inputValue(),'当前槽理由')
 await page.getByRole('button',{name:/下赛季草稿/}).click();assert.equal(await name.inputValue(),'next local name');assert.equal(await reason.inputValue(),'下赛季槽理由')
 await page.getByRole('button',{name:'预览赛季定义'}).click();await page.locator('.preview-box').getByText(/预览通过/).waitFor();await page.getByRole('button',{name:'保存下赛季草稿'}).click();let nextSaveDialog=page.getByRole('dialog');await nextSaveDialog.waitFor();assert.match(await nextSaveDialog.textContent(),/仅保存草稿，不切换赛季/);await nextSaveDialog.getByRole('button',{name:'取消'}).click();await name.fill('next local name')

 // 后台 refresh 发现新版时保留本地；显式丢弃才采用远端。
 current={...current,name:'服务端刷新后的当前名称',revision:4};operationsVersion=42
 await page.getByRole('button',{name:/当前赛季/}).click();await page.getByRole('button',{name:'刷新',exact:true}).click();await page.getByText(/本地编辑已保留/).waitFor();assert.equal(await name.inputValue(),' current local name ')
 await page.getByRole('button',{name:'刷新并丢弃本地编辑'}).click();await page.waitForFunction(()=>[...document.querySelectorAll('input')].some(input=>input.value==='服务端刷新后的当前名称'));assert.equal(await name.inputValue(),'服务端刷新后的当前名称')
 await reason.fill('只填写理由也必须保留');await page.getByRole('button',{name:'刷新',exact:true}).click();assert.equal(await reason.inputValue(),'只填写理由也必须保留');await reason.fill('')
 previewDelay=true;const delayed=page.waitForResponse(response=>response.url().endsWith('/preview'));await page.getByRole('button',{name:'预览赛季定义'}).click();await name.fill('迟到响应之后的新输入');await delayed;await page.getByText(/迟到响应已忽略/).waitFor();assert.equal(await page.locator('.preview-box').count(),0);assert.equal(await name.inputValue(),'迟到响应之后的新输入')
 await name.fill('冻结前名称');await reason.fill('冻结预览验收')
 await page.getByRole('button',{name:'预览赛季定义'}).click();await page.locator('.preview-box').getByText(/预览通过/).waitFor()
 let p=requests.filter(x=>x.path.endsWith('/preview')).at(-1);assert.equal(p.path,'/api/admin/seasons/definition-current/preview');assert.equal(p.body.expectedRevision,4);assert.equal(p.body.expectedVersion,42)
 assert.equal(p.body.draft.startsAt,'2026-09-01T00:00:17.123Z');assert.equal(p.body.draft.endsAt,'2026-12-31T16:00:31.789Z')
 assert.equal(await name.inputValue(),'冻结前名称 · 服务端规范化')
 await name.fill('预览后普通编辑');await page.getByRole('button',{name:'保存当前配置'}).click();assert.equal(await page.getByRole('dialog').count(),0);assert.match(await page.locator('#notice').textContent(),/重新预览/)
 await page.getByRole('button',{name:'预览赛季定义'}).click();await page.locator('.preview-box').getByText(/预览通过/).waitFor();const frozenName=await name.inputValue()
 await page.getByRole('button',{name:'保存当前配置'}).click();const applyDialog=page.getByRole('dialog');await applyDialog.waitFor();assert.match(await applyDialog.textContent(),/current.*r4\/v42/s);assert.match(await applyDialog.textContent(),/立即影响之后创建的对局/)
 await name.evaluate(el=>{el.value='弹窗后的表单篡改';el.dispatchEvent(new Event('input',{bubbles:true}))})
 failApply='network-once';await applyDialog.getByRole('button',{name:'确认保存'}).click();await applyDialog.getByRole('alert').waitFor();let puts=requests.filter(x=>x.method==='PUT'&&x.path==='/api/admin/seasons/definition-current');const firstKey=puts.at(-1).body.idempotencyKey;assert.equal(puts.at(-1).body.draft.name,frozenName);assert.equal(puts.at(-1).body.previewToken,'token-current-4-42')
 await applyDialog.getByRole('button',{name:'确认保存'}).click();await applyDialog.waitFor({state:'detached'});puts=requests.filter(x=>x.method==='PUT'&&x.path==='/api/admin/seasons/definition-current');assert.equal(puts.at(-1).body.idempotencyKey,firstKey,'network retry must reuse key');assert.equal(puts.at(-1).body.draft.name,frozenName)

 // 新提交意图换 key；stale preview 409 清预览、保留本槽且不污染 next。
 await name.fill('第二次提交');await reason.fill('第二次提交理由');await page.getByRole('button',{name:'预览赛季定义'}).click();await page.getByRole('button',{name:'保存当前配置'}).click();failApply='preview';await page.getByRole('dialog').getByRole('button',{name:'确认保存'}).click();await page.getByRole('dialog').getByRole('alert').waitFor();const secondKey=requests.filter(x=>x.method==='PUT'&&x.path.includes('definition-current')).at(-1).body.idempotencyKey;assert.notEqual(secondKey,firstKey);await page.getByRole('dialog').getByRole('button',{name:'取消'}).click();await page.getByText(/本地编辑已保留/).waitFor();assert.equal(await page.locator('.preview-box').count(),0);assert.match(await name.inputValue(),/第二次提交/)
 await page.getByRole('button',{name:/下赛季草稿/}).click();assert.equal(await name.inputValue(),'next local name','current conflict must not pollute next')

 // stale slot 与 stale operations 两类 preview 409。
 failPreview='revision';await page.getByRole('button',{name:'预览赛季定义'}).click();await page.getByText(/本地编辑已保留/).waitFor();assert.equal(await name.inputValue(),'next local name')
 await page.getByRole('button',{name:'刷新并丢弃本地编辑'}).click();await page.waitForFunction(()=>[...document.querySelectorAll('input')].some(input=>input.value==='下赛季草稿超长名称用于浏览器验收'))
 await name.fill('next ops conflict local');failPreview='operations';await page.getByRole('button',{name:'预览赛季定义'}).click();await page.getByText(/本地编辑已保留/).waitFor();assert.equal(await name.inputValue(),'next ops conflict local')
 next=undefined;current={...current,revision:current.revision+1};await page.getByRole('button',{name:'刷新',exact:true}).click();await page.getByText(/服务器已删除下赛季草稿/).waitFor();assert.equal(await name.inputValue(),'next ops conflict local');await page.getByRole('button',{name:'刷新并丢弃本地编辑'}).click();await page.getByText(/尚未创建下赛季草稿/).waitFor()

 // 空 next 的 create/delete 都须风险确认，且确认前不得发请求。
 await page.getByPlaceholder('创建草稿理由（必填）').fill('创建真实草稿');const createsBefore=requests.filter(x=>x.path==='/api/admin/seasons/draft'&&x.method==='POST').length;await page.getByRole('button',{name:'创建下赛季草稿'}).click();let dialog=page.getByRole('dialog');await dialog.waitFor();assert.equal(requests.filter(x=>x.path==='/api/admin/seasons/draft'&&x.method==='POST').length,createsBefore);assert.match(await dialog.textContent(),/next.*不静默复制.*创建真实草稿/s);await dialog.getByRole('button',{name:'确认创建草稿'}).click();await dialog.waitFor({state:'detached'});assert.equal(await page.getByLabel('赛季 ID').inputValue(),'');assert.equal(await page.getByLabel('开始时间',{exact:true}).inputValue(),'','服务端 null 时间必须保持空白，不得显示 1970');const createReq=requests.filter(x=>x.path==='/api/admin/seasons/draft'&&x.method==='POST').at(-1);assert.equal(createReq.body.expectedCurrentRevision,current.revision);assert.equal(createReq.body.expectedVersion,operationsVersion);assert.ok(createReq.body.idempotencyKey)

 // A1 删除/重建不推进 operationsVersion，且新旧草稿 revision 都可为 1；definitionId 必须参与身份冲突判定。
 const oldDraftId=next.definitionId,identityVersion=operationsVersion
 await name.fill('旧定义上的本地脏草稿');await reason.fill('旧定义理由')
 current={...current,revision:current.revision+1};next={...definition('created-external-dirty','','外部重建后的新定义',1),startsAt:null,endsAt:null}
 assert.notEqual(next.definitionId,oldDraftId);assert.equal(next.revision,1);assert.equal(operationsVersion,identityVersion)
 await page.getByRole('button',{name:'刷新',exact:true}).click();await page.getByText(/服务器已用新定义/).waitFor();assert.equal(await name.inputValue(),'旧定义上的本地脏草稿');assert.equal(await reason.inputValue(),'旧定义理由')
 await page.getByRole('button',{name:'刷新并丢弃本地编辑'}).click();await page.waitForFunction(()=>[...document.querySelectorAll('input')].some(input=>input.value==='外部重建后的新定义'));assert.equal(await name.inputValue(),'外部重建后的新定义');const discardedToId=next.definitionId
 current={...current,revision:current.revision+1};next={...definition('created-external-clean','','干净状态自动切换的新定义',1),startsAt:null,endsAt:null}
 assert.notEqual(next.definitionId,discardedToId);assert.equal(operationsVersion,identityVersion)
 await page.getByRole('button',{name:'刷新',exact:true}).click();await page.waitForFunction(()=>[...document.querySelectorAll('input')].some(input=>input.value==='干净状态自动切换的新定义'));assert.equal(await name.inputValue(),'干净状态自动切换的新定义');assert.equal(await page.getByRole('button',{name:'刷新并丢弃本地编辑'}).count(),0)
 await page.getByLabel('赛季 ID').fill('S-IDENTITY-NEW');await name.fill('新定义提交');await reason.fill('新身份预览应用')
 await page.getByRole('button',{name:'预览赛季定义'}).click();await page.locator('.preview-box').getByText(/预览通过/).waitFor();const identityPreview=requests.filter(x=>x.path.endsWith('/preview')).at(-1);assert.equal(identityPreview.path,`/api/admin/seasons/${next.definitionId}/preview`)
 await page.getByRole('button',{name:'保存下赛季草稿'}).click();dialog=page.getByRole('dialog');await dialog.getByRole('button',{name:'确认保存'}).click();await dialog.waitFor({state:'detached'});const identityApply=requests.filter(x=>x.method==='PUT'&&x.path.startsWith('/api/admin/seasons/')).at(-1);assert.equal(identityApply.path,`/api/admin/seasons/${next.definitionId}`)

 await reason.fill('删除草稿验收');const deleteExpectedRevision=next.revision,deleteExpectedVersion=operationsVersion,deletesBefore=requests.filter(x=>x.method==='DELETE').length;await page.getByRole('button',{name:'删除下赛季草稿'}).click();dialog=page.getByRole('dialog');await dialog.waitFor();assert.equal(requests.filter(x=>x.method==='DELETE').length,deletesBefore);assert.match(await dialog.textContent(),/只删除草稿.*当前赛季不受影响/s);await dialog.getByRole('button',{name:'确认删除'}).click();await dialog.waitFor({state:'detached'});const deleteReq=requests.filter(x=>x.method==='DELETE').at(-1);assert.equal(deleteReq.body.expectedRevision,deleteExpectedRevision);assert.equal(deleteReq.body.expectedVersion,deleteExpectedVersion);assert.equal(deleteReq.body.reason,'删除草稿验收');assert.ok(deleteReq.body.idempotencyKey);assert.equal(operationsVersion,deleteExpectedVersion,'delete 不得伪造 operationsVersion 递增')
 await page.getByText(/尚未创建下赛季草稿/).waitFor();await page.getByPlaceholder('创建草稿理由（必填）').fill('删除后重建');await page.getByRole('button',{name:'创建下赛季草稿'}).click();await page.getByRole('dialog').getByRole('button',{name:'确认创建草稿'}).click();const recreateReq=requests.filter(x=>x.path==='/api/admin/seasons/draft'&&x.method==='POST').at(-1);assert.equal(recreateReq.body.expectedCurrentRevision,current.revision,'delete 后 create 必须使用刷新后的 current revision');assert.equal(recreateReq.body.expectedVersion,operationsVersion)

 // 全局写不回归：赛季操作期间无 generic PUT/旧 ranked；全局保存携带未被赛季 UI 改写的权威字段。
 assert.equal(requests.filter(x=>x.path==='/api/admin/ranked/config').length,0)
 assert.equal(requests.filter(x=>x.path==='/api/admin/operations/config'&&x.method==='PUT').length,0)
 await page.getByRole('button',{name:'功能开关'}).click();await page.locator('textarea').fill('spectating=false');await page.getByPlaceholder('变更或回滚理由（必填）').fill('全局开关验收');await page.getByRole('button',{name:'预览差异'}).click();await page.getByRole('button',{name:'保存配置'}).click();await page.getByRole('dialog').getByRole('button',{name:'确认应用配置'}).click();const globalPut=requests.filter(x=>x.path==='/api/admin/operations/config'&&x.method==='PUT').at(-1);assert.equal(globalPut.body.config.season.id,operationsConfig.season.id);assert.equal(globalPut.body.config.season.name,operationsConfig.season.name);assert.equal(+new Date(globalPut.body.config.season.startsAt),+new Date(operationsConfig.season.startsAt));assert.equal(+new Date(globalPut.body.config.season.endsAt),+new Date(operationsConfig.season.endsAt));assert.deepEqual(globalPut.body.config.disasterPool,operationsConfig.disasterPool);assert.deepEqual(globalPut.body.config.defaultPresetDeckIds,operationsConfig.defaultPresetDeckIds)

 // 原运营面板非赛季能力必须保持真实可交互。
 await page.getByRole('button',{name:/对战与房间/}).click();const rankedMode=page.locator('article').filter({hasText:'排位'}).locator('input[type=checkbox]');await rankedMode.uncheck();await page.getByLabel('观战权限').selectOption('friends');assert.equal(await page.getByLabel('观战权限').inputValue(),'friends')
 await page.getByRole('button',{name:/长期公告/}).click();assert.equal(await page.locator('.announcement-row').count(),2);await page.getByRole('button',{name:'＋ 新增公告'}).click();assert.equal(await page.locator('.announcement-row').count(),3);const third=page.locator('.announcement-row').nth(2);await third.getByPlaceholder('输入长期公告内容').fill('新增公告交互保留');await third.getByLabel('开始时间（可选）').fill('2026-10-02T10:30');await third.getByRole('button',{name:'上移'}).click();await page.locator('.announcement-row').nth(1).getByRole('button',{name:'删除'}).click();assert.equal(await page.locator('.announcement-row').count(),2)
 await page.getByRole('button',{name:/维护与启服/}).click();await page.getByText('立即维护',{exact:true}).waitFor();const maintenancePlan=page.getByRole('group',{name:'预约维护计划'});await maintenancePlan.getByLabel('提前广播（小时）').fill('12');await maintenancePlan.getByLabel('预计维护时长（小时）').fill('6');assert.equal(await maintenancePlan.getByLabel('预计维护时长（小时）').inputValue(),'6');await page.getByPlaceholder('变更或回滚理由（必填）').fill('版本回滚验收')
 await page.getByRole('button',{name:/版本与状态/}).click();await page.getByText('qa-runtime-precision',{exact:true}).waitFor();await page.getByText('保留版本回滚能力',{exact:true}).waitFor();await page.getByRole('button',{name:'回滚到此版本'}).click();await page.getByRole('dialog').waitFor();assert.match(await page.getByRole('dialog').textContent(),/v90.*history-90/s);await page.getByRole('dialog').getByRole('button',{name:'取消'}).click()

 // 切季完成后计划归属 current；界面必须显示最近完成而不是误报未预约。
 current={...current,activationPlan:{status:'completed',generation:9,scheduledAt:'2026-09-30T00:30:00Z',armedAt:'2026-09-30T00:10:00Z',armedCurrentRevision:current.revision-1,armedDraftRevision:current.revision,armedOperationsVersion:operationsVersion,intentMask:'completed…plan',disarmGuardToken:'completed-guard',leaseState:'free',attemptCount:1,lastAttemptAt:'2026-09-30T00:30:00Z',suggestedActionCode:'no-action-required',completedAt:'2026-09-30T00:30:01Z'}};next=undefined
 await page.goto(url);await page.getByText('已完成',{exact:true}).waitFor();await page.getByText(/最近一次自动切季已完成/).waitFor();const completedPlan=page.locator('[data-ui-contract="completed-season-activation"]');await completedPlan.waitFor();assert.match(await page.locator('.plan-grid').textContent(),/g9.*1 次/s);assert.match(await completedPlan.textContent(),/完成时间.*2026.*8:30:01/s);assert.equal(await page.getByRole('button',{name:'取消预约'}).count(),0)

 // 只读权限。
 next=definition('next','S-READONLY','服务器创建的真实空槽草稿',2)
 await page.goto(url+'?readonly=1');await page.getByLabel('赛季 ID').waitFor();assert.equal(await page.getByLabel('名称',{exact:true}).isDisabled(),true);assert.equal(await page.getByRole('button',{name:'预览赛季定义'}).isDisabled(),true);await page.getByRole('button',{name:/下赛季草稿/}).click();await page.getByText(/服务器创建的真实空槽草稿/).waitFor()

 // 四档真实点击、弹框、触控尺寸与横向溢出。
 next=definition('next','S03','下赛季极长名称用于四档移动端布局与弹窗验收',2)
 const viewportResults=[]
 for(const viewport of [{width:1920,height:1080},{width:1366,height:768},{width:430,height:932},{width:390,height:844}]){
  await page.setViewportSize(viewport);await page.goto(url);await page.getByLabel('赛季 ID').waitFor();await page.getByRole('button',{name:/下赛季草稿/}).click();await page.getByRole('button',{name:/排位与七曜/}).click();await page.getByLabel('定级场次').click();await page.getByRole('button',{name:/构筑规则/}).click();await page.getByLabel(/每行一个官方预组/).click();await page.getByRole('button',{name:/赛季与天灾/}).click();await reason.fill('四档弹框验收');await page.getByRole('button',{name:'删除下赛季草稿'}).click();await page.getByRole('dialog').waitFor()
  const metrics=await page.evaluate(()=>{const dialog=document.querySelector('dialog').getBoundingClientRect(),buttons=[...document.querySelectorAll('.season-slot-switcher button,.config-actions button,dialog button')].filter(x=>x.offsetParent).map(x=>x.getBoundingClientRect());return{docOverflow:document.documentElement.scrollWidth>innerWidth+1,bodyOverflow:document.body.scrollWidth>innerWidth+1,dialogInBounds:dialog.left>=-1&&dialog.right<=innerWidth+1&&dialog.top>=-1&&dialog.bottom<=innerHeight+1,minButtonHeight:Math.min(...buttons.map(x=>x.height))}})
  assert.equal(metrics.docOverflow,false);assert.equal(metrics.bodyOverflow,false);assert.equal(metrics.dialogInBounds,true);assert.ok(metrics.minButtonHeight>=43)
  await page.screenshot({path:path.join(output,`season-dual-${viewport.width}x${viewport.height}.png`),fullPage:true});viewportResults.push({...viewport,...metrics});await page.getByRole('dialog').getByRole('button',{name:'取消'}).click()
 }
 assert.deepEqual(pageErrors,[]);assert.deepEqual(consoleErrors,[]);assert.equal(expectedHttpErrors.length,4,'only the deliberately injected 503/409 responses may reach Chromium resource logging')
 const report={passed:true,pageErrors,applicationConsoleErrors:consoleErrors,expectedInjectedHttpErrors:expectedHttpErrors.length,requestCounts:{catalog:requests.filter(x=>x.path==='/api/admin/seasons').length,preview:requests.filter(x=>x.path.endsWith('/preview')).length,activationPreview:requests.filter(x=>x.path.endsWith('/activation-preview')).length,arm:requests.filter(x=>x.path.endsWith('/arm')).length,disarm:requests.filter(x=>x.path.endsWith('/disarm')).length,apply:requests.filter(x=>x.method==='PUT'&&x.path.startsWith('/api/admin/seasons/')).length,create:requests.filter(x=>x.path==='/api/admin/seasons/draft'&&x.method==='POST').length,delete:requests.filter(x=>x.method==='DELETE').length,legacyRanked:requests.filter(x=>x.path==='/api/admin/ranked/config').length},viewportResults,applySamples:puts.slice(-2).map(x=>x.body),create:createReq.body,delete:deleteReq.body}
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({passed:report.passed,pageErrors,applicationConsoleErrors:consoleErrors,expectedInjectedHttpErrors:expectedHttpErrors.length,requestCounts:report.requestCounts,viewportResults},null,2))
}finally{await browser?.close();await server.close()}
