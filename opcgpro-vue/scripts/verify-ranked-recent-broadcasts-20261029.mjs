import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {createHash} from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {createRequire} from 'node:module'
import {createServer} from 'vite'
const repo=path.resolve(import.meta.dirname,'../..'),front=path.join(repo,'opcgpro-vue')
const out=process.env.L12_RECENT_BROADCAST_OUT,cache='D:/GPT/Legion12/cache/primary',temp=path.join(cache,'t','br29'),viteCache=path.join(cache,'vite','br29')
assert(out&&path.isAbsolute(out),'Explicit absolute D evidence root required')
assert(!fs.existsSync(out),'Preserve previous acceptance evidence')
execFileSync('pwsh.exe',['-NoProfile','-NonInteractive','-Command',
  '& $env:L12_INIT -CacheRoot $env:L12_CACHE | Out-Null; . $env:L12_GUARD; '
  +'@($env:L12_OUT,$env:TEMP,$env:L12_VITE) | ForEach-Object {$null=Assert-L12GeneratedPath -Path $_ -Purpose "recent broadcast QA"}'],
  {env:{...process.env,L12_INIT:path.join(repo,'ops/windows/Initialize-L12BuildEnvironment.ps1'),
    L12_GUARD:path.join(repo,'ops/windows/L12GeneratedStorage.ps1'),L12_CACHE:cache,L12_OUT:out,L12_VITE:viteCache,TEMP:temp,TMP:temp,TMPDIR:temp},stdio:'pipe'})
fs.mkdirSync(out,{recursive:true});fs.mkdirSync(temp,{recursive:true});Object.assign(process.env,{TEMP:temp,TMP:temp,TMPDIR:temp})
const require=createRequire(import.meta.url)
const {chromium}=require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const product=path.join(front,'src/l12/site/BattleHubPage.vue')
const pinnedFiles=[product,...['src/l12/site/RankedBroadcastTicker.vue','src/l12/site/rankedBroadcastPlayback.ts',
  'scripts/test-ranked-recent-broadcasts-20261029.mjs','scripts/verify-ranked-recent-broadcasts-20261029.mjs'].map(file=>path.join(front,file))]
const pins=()=>Object.fromEntries(pinnedFiles.map(file=>[path.relative(repo,file),createHash('sha256').update(fs.readFileSync(file)).digest('hex')]))
const policy={version:1,season:{id:'S01',name:'第1赛季·启幕'},announcements:[],maintenance:{entryBlocked:false},cardRestrictions:[],defaultRoomConfig:{spectating:'public',handVisibility:'request',disasterMode:'all'}}
const virtual={
  '/__recent_platform__.js':`import {reactive} from 'vue';export const platformState=reactive({account:null,token:''});
    export const normalizeRankedConfig=x=>x;export const getEffectiveOperationsPolicy=async()=>(${JSON.stringify(policy)});
    export const rankedApi={broadcasts:async limit=>{const response=await fetch('/__fixture/broadcasts?limit='+limit);if(!response.ok)throw Error('fixture read failed');return response.json()},
    overview:async()=>({profile:{faction:'混沌',placed:true,tier:'破败先锋Ⅲ',displayValue:'七曜值 31,427'},config:{factions:[]},factionTotals:{}})};`,
  '/__recent_net__.js':`import {reactive} from 'vue';export const l12State=reactive({status:'online',room:null,game:null,matchmaking:null,matchFound:null,notice:'',operationsPolicy:null});
    export const connect=async()=>{},cancelMatchmaking=()=>{},createRoom=()=>{},joinMatchmaking=()=>{},joinRoom=()=>{},leaveRoom=()=>{},selectCustomDeck=()=>{},setReady=()=>{},spectateRoom=()=>{},updateRoomOptions=()=>{};`,
  '/__recent_decks__.js':`const deck={id:'qa',revision:1,name:'汉堡王',masterId:'S02-06M2',cardIds:['S02-0604','S01-0417','S02-0610'],moraleIds:[],specialIds:[]};
    export const SELECTED_DECK_KEY='fixture-selection',L12_DECK_SELECTION_SCOPES=['ranked','casual','friendly','sandbox-player','sandbox-opponent'];
    const decks={'汉堡王':deck},selected=new Map(L12_DECK_SELECTION_SCOPES.map(scope=>[scope,'汉堡王'])),requireScope=scope=>{if(!L12_DECK_SELECTION_SCOPES.includes(scope))throw Error('unexpected deck scope')};
    export const initializeDeckCache=async()=>{},loadSavedDecks=()=>decks,hasSavedDeckSelection=scope=>{requireScope(scope);return selected.has(scope)};
    export const loadSavedDecksState=()=>({status:'ready',decks}),loadSelectedDeckName=(scope,available)=>{requireScope(scope);return selected.get(scope)??Object.keys(available)[0]??''},saveSelectedDeckName=async(scope,name)=>{requireScope(scope);if(!Object.hasOwn(decks,name))throw Error('absent fixture deck');selected.set(scope,name)},
      ensureOfficialPrebuiltDecks=async()=>({'汉堡王':deck}),loadDeckCatalog=async()=>[],loadPrivateDeckBody=async()=>deck,
      deckErrorBelongsToCurrentAccount=()=>false,validateDeck=()=>'',deckCountSummary=()=>({label:'43'});`,
  '/__recent_entry__.js':`import {createApp} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router';
    import Hub from '/src/l12/site/BattleHubPage.vue';import {rankedBroadcastPlayback} from '/src/l12/site/rankedBroadcastPlayback.ts';
    import {l12State as actualConnection} from '/src/l12/net.ts';actualConnection.endpoint='ws://'+location.host+'/ws';
    import '/src/style.css';const router=createRouter({history:createMemoryHistory(),routes:[{path:'/lobby',component:Hub}]});await router.push('/lobby');await router.isReady();
    const app=createApp(Hub);app.use(router);app.mount('#app');window.__recentQA={playback:rankedBroadcastPlayback,unmount:()=>app.unmount()};`,
}
const messages=[
  '【混沌】逐日者 已取得 8 连胜',
  '【秩序】青山 终结了 渡鸦 的 10 连胜',
  '【命运】星海 晋升至 冠冕',
  '<img src=x onerror=alert(1)> '+ '长名字与广播内容'.repeat(22),
  '【混沌】白夜 获得称号「孙悟空之主」',
  '不得出现的第六条',
  '不得出现的第七条',
]
const payload=messages.map((message,index)=>({id:'b-'+index,matchId:'m-'+index,message,
  eventType:['win-streak','streak-ended','highest-tier','unknown','master-champion-S02-01M1'][index%5],
  createdAt:index===3?'bad-date':new Date(Date.UTC(2026,9,29,15,48-index)).toISOString()}))
let responseRows=payload,responseStatus=200
const requests=[],report={sources:{before:pins()},cases:[],errors:[],cleanup:{},boundary:'Actual BattleHub SFC and browser rendering with local synthetic public API/identity/deck fixtures; no online writes, no actual matchmaking or physical device claim.'}
const html='<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__recent_entry__.js"></script></body></html>'
const server=await createServer({root:front,cacheDir:viteCache,configLoader:'runner',logLevel:'error',server:{host:'127.0.0.1',port:0},plugins:[{
  name:'recent-broadcast-real-sfc',enforce:'pre',resolveId:id=>id in virtual?id:null,load:id=>virtual[id],
  transform(source,id){if(id.split('?')[0].replaceAll('\\','/')!==product.replaceAll('\\','/'))return null
    return {code:source.replaceAll("from '@/l12/platform'","from '/__recent_platform__.js'").replaceAll("from '@/l12/net'","from '/__recent_net__.js'").replaceAll("from '@/l12/decks'","from '/__recent_decks__.js'"),map:null}},
  configureServer(vite){vite.middlewares.use((req,res,next)=>{
    const url=new URL(req.url,'http://fixture')
    if(url.pathname==='/__recent_broadcasts'){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(html);return}
    if(url.pathname==='/__fixture/broadcasts'){requests.push(url.search);res.statusCode=responseStatus;res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responseRows));return}
    if(url.pathname==='/api/ranked/broadcasts/settings'){requests.push(url.pathname);res.setHeader('Content-Type','application/json');res.end(JSON.stringify({displaySeconds:16,lobbyDelaySeconds:3,intervalSeconds:15}));return}
    if(url.pathname.startsWith('/api/')){requests.push(url.pathname);res.setHeader('Content-Type','application/json');res.end('[]');return}
    next()
  })},
}]})
let browser,fatal
const check=(value,message)=>assert.ok(value,message)
try{
  await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
  report.browser=browser.version()
  for(const width of [1440,390,320]){
    responseRows=payload;responseStatus=200
    const context=await browser.newContext({viewport:{width,height:1000}}),page=await context.newPage()
    page.on('pageerror',error=>report.errors.push(error.message))
    await page.route('**/*',route=>new URL(route.request().url()).origin==='http://127.0.0.1:'+server.httpServer.address().port?route.continue():route.abort())
    try{
      await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__recent_broadcasts',{waitUntil:'networkidle'})
      const history=page.locator('[data-ui-contract="ranked-recent-five-broadcasts"]')
      await history.locator('li').nth(4).waitFor()
      assert.deepEqual(await history.locator('li>p').allTextContents(),messages.slice(0,5))
      check(await history.locator('li img').count()===0,'message markup is literal text')
      const geometry=await history.evaluate(root=>{
        const box=root.getBoundingClientRect(),rows=[...root.querySelectorAll('li')]
        return {overflow:document.documentElement.scrollWidth>innerWidth,
          allContained:rows.every(row=>{const rect=row.getBoundingClientRect();return rect.left>=box.left-1&&rect.right<=box.right+1&&rect.top>=box.top-1&&rect.bottom<=box.bottom+1}),
          localScroll:root.scrollHeight>root.clientHeight+1,overflowY:getComputedStyle(root).overflowY,
          afterDeck:root.previousElementSibling?.classList.contains('deck-selection-block--match'),
          messageOverflow:rows.some(row=>row.querySelector('p').scrollWidth>row.querySelector('p').clientWidth+1)}
      })
      check(!geometry.overflow&&geometry.allContained&&!geometry.localScroll&&!geometry.messageOverflow&&geometry.afterDeck,'five rows must fit directly below the deck without local scroll')
      await page.screenshot({path:path.join(out,'ranked-'+width+'.png'),fullPage:true})
      await history.screenshot({path:path.join(out,'history-'+width+'.png')})
      await page.getByRole('button',{name:'休闲匹配',exact:true}).click();await history.waitFor({state:'detached'})
      await page.getByRole('button',{name:'排位匹配',exact:true}).click();await history.locator('li').nth(4).waitFor()
      responseStatus=503
      await page.evaluate(()=>{window.__recentQA.playback.claim={broadcast:{id:'failed-refresh'}}})
      await history.getByText('广播暂时无法加载').waitFor()
      assert.equal(await history.locator('li').count(),5,'failed refresh retains last successful public rows')
      responseStatus=200;responseRows=payload.slice(0,2)
      await history.getByRole('button',{name:'重试',exact:true}).click()
      await page.waitForFunction(()=>document.querySelectorAll('.ranked-broadcast-history li').length===2)
      responseRows=[]
      await page.evaluate(()=>{window.__recentQA.playback.claim={broadcast:{id:'empty-refresh'}}})
      await history.getByText('暂无排位广播').waitFor()
      const afterUnmount=requests.length
      await page.evaluate(()=>window.__recentQA.unmount())
      await page.waitForTimeout(100)
      assert.equal(requests.length,afterUnmount,'unmount does not initiate another read')
      report.cases.push({width,rows:5,geometry,modeSwitch:true,failureRetainsRows:true,retryRows:2,empty:true,unmountNoRead:true})
    }finally{await context.close()}
  }
  check(!requests.some(request=>request.includes('/claim')||request.includes('/complete')),'history must never claim or complete a broadcast')
  assert.equal(requests.filter(request=>request==='/api/ranked/broadcasts/settings').length,3,'the unchanged top ticker reads settings only from this isolated fixture')
  report.requests=requests
  const raw=execFileSync(process.execPath,[path.join(front,'scripts/test-ranked-recent-broadcasts-20261029.mjs')],{cwd:front,encoding:'utf8'})
  report.lifecycle=JSON.parse(raw)
  assert.equal(report.errors.length,0,report.errors.join('\n'))
}catch(error){fatal=error;report.errors.push(error.stack||String(error))}
finally{
  if(browser){await browser.close();report.cleanup.browserClosed=true}
  await server.close();report.cleanup.serverClosed=true
  fs.rmSync(viteCache,{recursive:true,force:true});report.cleanup.cacheRemoved=!fs.existsSync(viteCache)
  report.sources.after=pins()
  if(JSON.stringify(report.sources.before)!==JSON.stringify(report.sources.after))report.errors.push('Source pins changed during acceptance')
  fs.writeFileSync(path.join(out,'result.json'),JSON.stringify(report,null,2))
}
if(fatal)throw fatal
assert.equal(report.errors.length,0,report.errors.join('\n'))
console.log(JSON.stringify({views:report.cases.length,lifecycle:report.lifecycle.scenarios,assertions:report.lifecycle.assertions,errors:report.errors.length}))
