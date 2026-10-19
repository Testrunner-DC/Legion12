import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {spawnSync} from 'node:child_process'
import {createRequire} from 'node:module'
import {createServer} from 'vite'
const root=path.resolve(import.meta.dirname,'..'),output=process.env.L12_COVER_CONSISTENCY_OUTPUT,cache=process.env.L12_COVER_CONSISTENCY_CACHE
assert.ok(output&&cache&&process.env.L12_COVER_CONSISTENCY_GUARDED==='1','Guard explicit D/E output/cache before running')
const baseline=process.argv.includes('--baseline'),overrides=new Map()
if(baseline)for(const p of ['src/l12/DeckCover.vue','src/l12/site/BattleHubPage.vue']){
 const r=spawnSync('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/git/cmd/git.exe',['--no-lazy-fetch','-c','safe.directory='+path.dirname(root),'-C',path.dirname(root),'show','07018ff60bb3119b13b86cb313ae5eed9057538a:opcgpro-vue/'+p],{encoding:'utf8'})
 assert.equal(r.status,0);overrides.set(path.join(root,p).replaceAll('\\','/'),r.stdout)
}
fs.mkdirSync(output,{recursive:true})
const {chromium}=createRequire(import.meta.url)('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const policy={version:1,season:{id:'qa',name:'合成验收'},maintenance:{entryBlocked:false,active:false},defaultPresetDeckIds:[],cardRestrictions:[],disasterCardIds:[],announcements:[],defaultRoomConfig:{spectating:'public',handVisibility:'request',disasterMode:'all'}}
const entry=`
import {createApp,h} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router'
import Cover from '/src/l12/DeckCover.vue';import Hub from '/src/l12/site/BattleHubPage.vue'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,ensureOfficialPrebuiltDecks,saveSelectedDeckName} from '/src/l12/decks.ts'
import {rankedApi,platformState} from '/src/l12/platform.ts';import {l12State} from '/src/l12/net.ts';import '/src/style.css'
platformState.account=null;platformState.token=null;rankedApi.overview=async()=>{throw new Error('合成预览')}
const cards=await loadDeckCatalog(),saved=await ensureOfficialPrebuiltDecks(),byId=new Map(cards.map(c=>[c.id,c]))
const deck=Object.values(saved).find(d=>['legion','tactic','artifact'].every(t=>d.cardIds.some(id=>byId.get(id)?.cardType===t)))||Object.values(saved)[0]
for(const scope of ['ranked','casual','friendly'])saveSelectedDeckName(scope,deck.name)
const mode=new URLSearchParams(location.search).get('mode')||'gallery'
if(mode==='room')l12State.room={roomCode:'QA1234',yourPlayerIndex:0,decks:[],players:[{playerIndex:0,name:'合成甲',connected:true},{playerIndex:1,name:'合成乙',connected:true}],options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}}]});await router.push({path:'/',query:mode==='editor'?{deck:deck.name}:{}})
createApp({render:()=>h('main',{style:{boxSizing:'border-box',width:mode==='narrow'?'min(360px,100%)':'100%',padding:mode==='editor'?'0':'20px',maxWidth:mode==='editor'?'none':'1300px',margin:'auto'}},mode==='gallery'?[h('h1','共享横幅与代表牌'),h('div',{style:{display:'flex',flexWrap:'wrap',gap:'20px'}},['regular','featured','compact'].flatMap(variant=>[180,280,420].map(w=>h('article',{style:{width:w+'px',maxWidth:'100%'}},[h('p',variant+' / '+w+'px'),h(Cover,{variant,name:'预组·三张代表牌',masterId:deck.masterId,catalog:cards,sourceCardIds:deck.cardIds,context:'合成展示',meta:'40张主牌',...(variant==='featured'?{metaPrefix:'S02'}:{})})]))))]:h(mode==='editor'?Editor:Hub,{matchmakingOnly:mode==='narrow'}))}).use(router).mount('#app');window.__coverReady=true
`
const server=await createServer({root,configLoader:'runner',cacheDir:cache,server:{host:'127.0.0.1',port:0},plugins:[{name:'cover-consistency',enforce:'pre',resolveId(id){if(id==='/__cover__.js')return id},load(id){if(id==='/__cover__.js')return entry;return overrides.get(id.replaceAll('\\','/'))},configureServer(v){v.middlewares.use((req,res,next)=>{if(new URL(req.url||'/','http://fixture').pathname!=='/__cover__')return next();res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#080d12;color:#eee"><div id="app"></div><script type="module" src="/__cover__.js"></script>')})}}]})
const tracked=['src/l12/DeckCover.vue','src/l12/site/BattleHubPage.vue','src/l12/L12DeckEditor.vue','src/l12/deckCover.ts','scripts/verify-deck-cover-consistency.mjs']
const hashes=()=>Object.fromEntries(tracked.map(p=>[p,crypto.createHash('sha256').update(overrides.get(path.join(root,p).replaceAll('\\','/'))??fs.readFileSync(path.join(root,p))).digest('hex')]))
const report={baseline,sourceBefore:hashes(),cases:[],failures:[],pageErrors:[],productionWrites:0,scope:'actual shared Cover/BattleHub/Editor, synthetic guest state; not physical mobile or live accounts'};let browser
try{
 await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
 const cases=[['gallery',1440,1100],['hub',1440,1000],['room',1440,1000],['hub',390,844],['room',390,844],['narrow',844,390],...baseline?[]:[['editor',1440,1000],['editor',390,844]]]
 for(const [mode,width,height] of cases.filter(c=>!process.argv.includes('--editor-only')||c[0]==='editor')){
  const ctx=await browser.newContext({viewport:{width,height},reducedMotion:'reduce'});await ctx.routeWebSocket(/.*/,s=>s.close())
  await ctx.route('**/*',r=>{const u=new URL(r.request().url());if(u.hostname!=='127.0.0.1')return r.abort();if(u.pathname.startsWith('/api/'))return r.fulfill({json:u.pathname.includes('effective-policy')?policy:[]});if(u.pathname.startsWith('/card-assets/')){const b=path.resolve('D:/L12-assets/published/current'),f=path.resolve(b,decodeURIComponent(u.pathname.slice(13)));assert.ok(f.startsWith(b+path.sep));return fs.existsSync(f)?r.fulfill({path:f}):r.abort()}return r.continue()})
  const page=await ctx.newPage();page.on('pageerror',e=>report.pageErrors.push(e.message));await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__cover__?mode='+mode)
  await page.waitForFunction(()=>window.__coverReady&&document.querySelector('.deck-cover')?.getAttribute('data-cover-cards')?.split(',').length===3)
  if(mode==='editor'){
   await page.waitForFunction(()=>document.querySelector('.deck-list-header .deck-cover')?.getAttribute('data-cover-cards')?.split(',').length===3)
   if(width<700)await page.locator('.deck-mobile-nav').getByRole('button',{name:/^牌表/}).click()
  }
  await page.waitForFunction(()=>[...document.querySelectorAll('.deck-cover__master-banner,.deck-cover__card img')].filter(i=>{const r=i.getBoundingClientRect();return r.width>0&&r.height>0&&r.bottom>0&&r.top<innerHeight&&r.right>0&&r.left<innerWidth&&getComputedStyle(i).visibility!=='hidden'}).every(i=>i.complete&&i.naturalWidth>0))
  const geometry=await page.locator('.deck-cover').evaluateAll(es=>es.filter(e=>e.getClientRects().length).map(e=>{const r=e.getBoundingClientRect(),t=e.querySelector('.deck-cover__copy').getBoundingClientRect();return{variant:e.dataset.coverVariant,w:r.width,h:r.height,ratio:r.width/r.height,titleFont:Number.parseFloat(getComputedStyle(e.querySelector('.deck-cover__copy b')).fontSize),copyLeft:Number.parseFloat(getComputedStyle(e.querySelector('.deck-cover__copy')).left),textInside:t.top>=r.top&&t.bottom<=r.bottom,cards:[...e.querySelectorAll('.deck-cover__card')].map(c=>{const x=c.getBoundingClientRect();return{x:x.left-r.left,y:x.top-r.top,w:x.width,h:x.height,right:x.right-r.left,bottom:x.bottom-r.top}}),overflow:document.documentElement.scrollWidth>innerWidth+1}}))
  assert.ok(geometry.length,'the tested consumer must expose a visible cover')
  for(const g of geometry){if(Math.abs(g.ratio-2.4)>.04)report.failures.push(mode+' '+width+' '+g.variant+' ratio='+g.ratio);if(!g.textInside)report.failures.push(mode+' '+width+' clipped text');if(g.overflow)report.failures.push(mode+' '+width+' page overflow');if(!g.cards.every(c=>c.y>=0&&c.y<g.h*.6&&c.bottom>g.h*.9&&c.bottom<g.h*1.4))report.failures.push(mode+' '+width+' non-bottom cards');if(!baseline&&g.w<=242){assert.equal(g.titleFont,12);assert.equal(g.copyLeft,8)}}
  if(!baseline&&mode==='gallery'){
   const previous=JSON.parse(fs.readFileSync(path.join(output,'../baseline/report.json'),'utf8')).cases.find(c=>c.mode==='gallery').geometry
   for(let i=0;i<geometry.length;i++)for(let n=0;n<3;n++){assert.ok(geometry[i].cards[n].right>previous[i].cards[n].right,'each representative card must move right');assert.ok(geometry[i].cards[n].y>previous[i].cards[n].y,'each representative card must move down')}
  }
  const selectorCase=!['gallery','editor'].includes(mode)
  if(selectorCase){await page.getByRole('button',{name:'更换牌库',exact:true}).click();await page.getByRole('dialog').waitFor();await page.getByRole('button',{name:'取消',exact:true}).click()}
  await page.screenshot({path:path.join(output,mode+'-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'});report.cases.push({mode,width,height,geometry,actualSelectorOpenAndCancel:selectorCase});await ctx.close()
 }
 assert.deepEqual(report.pageErrors,[]);if(baseline)assert.ok(report.failures.length>0);else assert.deepEqual(report.failures,[])
 console.log(JSON.stringify({baseline,cases:report.cases.length,failures:report.failures,pageErrors:report.pageErrors.length}))
}catch(error){report.fatal=error.stack||String(error);throw error}
finally{report.sourceAfter=hashes();report.sourcesStable=JSON.stringify(report.sourceBefore)===JSON.stringify(report.sourceAfter);fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));await browser?.close();await server.close();if(!report.sourcesStable)process.exitCode=1}
