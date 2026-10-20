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
import {createApp,h,Transition} from 'vue';import {createRouter,createMemoryHistory,RouterView} from 'vue-router'
import Cover from '/src/l12/DeckCover.vue';import Hub from '/src/l12/site/BattleHubPage.vue'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,ensureOfficialPrebuiltDecks,saveSelectedDeckName} from '/src/l12/decks.ts'
import {rankedApi,platformState} from '/src/l12/platform.ts';import {l12State} from '/src/l12/net.ts';import '/src/style.css'
platformState.account=null;platformState.token=null
rankedApi.overview=async()=>({profile:{accountId:'qa',username:'合成玩家',seasonId:'qa',faction:'秩序',sevenValue:1234,displayValue:'七曜值 1,234',placementPlayed:5,placementWins:3,placed:true,wins:8,losses:4,winStreak:2,lossStreak:0,tier:'精英',tierIndex:2,factionRank:3,titles:[],rankLabel:'精英',masterTitles:[]},factionTotals:{秩序:371500,混沌:280500,命运:298500},config:{placementMatches:5,placementMaximum:29999,broadcastEnabled:true,factions:[['order','秩序','#58c6cd'],['chaos','混沌','#d35b69'],['fate','命运','#a78bdc']].map(([id,name,color])=>({id,name,color,icon:'◆',firstTitle:name+'冠首',topFiveTitle:name+'先锋',tiers:[]})),masterTitles:[]},history:[]})
const cards=await loadDeckCatalog(),saved=await ensureOfficialPrebuiltDecks(),byId=new Map(cards.map(c=>[c.id,c]))
const deck=Object.values(saved).find(d=>['legion','tactic','artifact'].every(t=>d.cardIds.some(id=>byId.get(id)?.cardType===t)))||Object.values(saved)[0]
for(const scope of ['ranked','casual','friendly'])saveSelectedDeckName(scope,deck.name)
const mode=new URLSearchParams(location.search).get('mode')||'gallery'
if(mode==='room')l12State.room={roomCode:'QA1234',yourPlayerIndex:0,decks:[],players:[{playerIndex:0,name:'合成甲',connected:true},{playerIndex:1,name:'合成乙',connected:true}],options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}}
const routes=mode==='route'?[{path:'/battle',component:Hub,props:{initialTab:'match'}},{path:'/battle/tutorial',component:Hub,props:{initialTab:'tutorial'}}]:[{path:'/',component:{render:()=>null}}]
const router=createRouter({history:createMemoryHistory(),routes});await router.push(mode==='route'?'/battle/tutorial':{path:'/',query:mode==='editor'?{deck:deck.name}:{}});if(mode==='route'){window.__routeTestRouter=router;window.__fixtureL12State=l12State}
const keyedRouteHost=()=>h(RouterView,null,{default:({Component,route})=>h(Transition,{name:'page-slide'},()=>h(Component,{key:route.path}))})
createApp({render:()=>h('main',{style:{boxSizing:'border-box',width:mode==='narrow'?'min(360px,100%)':'100%',padding:mode==='editor'?'0':'20px',maxWidth:mode==='editor'?'none':'1300px',margin:'auto'}},mode==='route'?keyedRouteHost():mode==='gallery'?[h('h1','共享横幅与代表牌'),h('div',{style:{display:'flex',flexWrap:'wrap',gap:'20px'}},['regular','featured','compact'].flatMap(variant=>[180,280,420].map(w=>h('article',{style:{width:w+'px',maxWidth:'100%'}},[h('p',variant+' / '+w+'px'),h(Cover,{variant,name:'预组·三张代表牌',masterId:deck.masterId,catalog:cards,sourceCardIds:deck.cardIds,context:'合成展示',meta:'40张主牌',...(variant==='featured'?{metaPrefix:'S02'}:{})})]))))]:h(mode==='editor'?Editor:Hub,{matchmakingOnly:mode==='narrow'}))}).use(router).mount('#app');window.__coverReady=true
`
const server=await createServer({root,configLoader:'runner',cacheDir:cache,server:{host:'127.0.0.1',port:0},plugins:[{name:'cover-consistency',enforce:'pre',resolveId(id){if(id==='/__cover__.js')return id},load(id){if(id==='/__cover__.js')return entry;return overrides.get(id.replaceAll('\\','/'))},configureServer(v){v.middlewares.use((req,res,next)=>{if(new URL(req.url||'/','http://fixture').pathname!=='/__cover__')return next();res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#080d12;color:#eee"><div id="app"></div><script type="module" src="/__cover__.js"></script>')})}}]})
const tracked=['src/l12/DeckCover.vue','src/l12/site/BattleHubPage.vue','src/l12/L12DeckEditor.vue','src/l12/deckCover.ts','src/router/index.ts','scripts/test-deck-cover.mjs','scripts/test-tutorial-contract.mjs','scripts/verify-deck-cover-consistency.mjs',...['1_w','1_m','2_w','2_m','3_w','3_m'].map(name=>'src/assets/l12/battle/Match_'+name+'.png')]
const hashes=()=>Object.fromEntries(tracked.map(p=>[p,crypto.createHash('sha256').update(overrides.get(path.join(root,p).replaceAll('\\','/'))??fs.readFileSync(path.join(root,p))).digest('hex')]))
const report={baseline,sourceBefore:hashes(),cases:[],failures:[],pageErrors:[],productionWrites:0,scope:'actual shared Cover/BattleHub/Editor with App-equivalent path-keyed RouterView transition; synthetic guest state; not physical mobile or live accounts'};let browser
try{
 await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
 const cases=[['gallery',1440,1100],...baseline?[]:[['route',1440,1000]],['hub',1440,1000],['friendly',1440,1000],['room',1440,1000],['hub',390,844],['friendly',390,844],['room',390,844],['hub',844,390],['friendly',844,390],['room',844,390],['narrow',844,390],...baseline?[]:[['editor',1440,1000],['editor',390,844]]]
 for(const [mode,width,height] of cases.filter(c=>!process.argv.includes('--editor-only')||c[0]==='editor')){
  const touch=width<=844&&!['gallery','editor'].includes(mode);const ctx=await browser.newContext({viewport:{width,height},reducedMotion:'reduce',hasTouch:touch});await ctx.routeWebSocket(/.*/,s=>s.close())
  await ctx.route('**/*',r=>{const u=new URL(r.request().url());if(u.hostname!=='127.0.0.1')return r.abort();if(u.pathname.startsWith('/api/'))return r.fulfill({json:u.pathname.includes('effective-policy')?policy:[]});if(u.pathname.startsWith('/card-assets/')){const b=path.resolve('D:/L12-assets/published/current'),f=path.resolve(b,decodeURIComponent(u.pathname.slice(13)));assert.ok(f.startsWith(b+path.sep));return fs.existsSync(f)?r.fulfill({path:f}):r.abort()}return r.continue()})
  const artImageRequests=[];const page=await ctx.newPage();page.on('pageerror',e=>report.pageErrors.push(e.message));page.on('request',r=>{if(r.resourceType()==='image'&&r.url().includes('Match_'))artImageRequests.push(decodeURIComponent(new URL(r.url()).pathname))});await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__cover__?mode='+mode);await page.waitForFunction(()=>window.__coverReady)
  if(mode==='route'){
   await page.locator('.tutorial-page.embedded').waitFor();assert.equal(await page.locator('.mode-tabs>button.active').getAttribute('data-lobby-art-slot'),'tutorial','legacy tutorial URL must open the third lobby tab');assert.equal(await page.locator('.battle-hub').count(),1)
   await page.evaluate(()=>{window.__firstRouteHub=document.querySelector('.battle-hub')});await page.evaluate(()=>window.__routeTestRouter.push('/battle'));await page.waitForFunction(()=>document.querySelectorAll('.battle-hub').length===1&&document.querySelector('.mode-tabs>button.active')?.dataset.lobbyArtSlot==='match')
   assert.equal(await page.evaluate(()=>document.querySelector('.battle-hub')!==window.__firstRouteHub),true,'App path key must remount the lobby across route paths')
   await page.getByRole('button',{name:'好友房',exact:true}).click();const roomDraft=page.getByLabel('房间码');await roomDraft.fill('QA1234');await roomDraft.focus();await page.getByRole('button',{name:'教学',exact:true}).click();await page.locator('.tutorial-page.embedded').waitFor();assert.notEqual(await page.evaluate(()=>document.activeElement?.tagName),'INPUT','hidden room draft must not retain focus');await page.getByRole('button',{name:'好友房',exact:true}).click();assert.equal(await page.getByLabel('房间码').inputValue(),'QA1234','same-lobby tab roundtrip must retain the unsubmitted room draft')
   await page.evaluate(()=>window.__routeTestRouter.push('/battle/tutorial'));await page.waitForFunction(()=>document.querySelectorAll('.battle-hub').length===1&&document.querySelector('.tutorial-page.embedded'));assert.equal(await page.locator('.mode-tabs>button.active').getAttribute('data-lobby-art-slot'),'tutorial');assert.equal(await page.evaluate(()=>Boolean(window.__fixtureL12State.pendingAction||window.__fixtureL12State.room||window.__fixtureL12State.game)),false,'route mount must not automatically start a tutorial')
   await page.evaluate(()=>window.__routeTestRouter.push('/battle'));await page.waitForFunction(()=>document.querySelectorAll('.battle-hub').length===1&&document.querySelector('.mode-tabs>button.active')?.dataset.lobbyArtSlot==='match');await page.evaluate(()=>window.__routeTestRouter.push('/battle/tutorial'));await page.waitForFunction(()=>document.querySelectorAll('.battle-hub').length===1&&document.querySelector('.tutorial-page.embedded'));assert.equal(await page.locator('.mode-tabs>button').count(),3);await page.screenshot({path:path.join(output,'tutorial-route-roundtrip-1440x1000.png'),fullPage:true,animations:'disabled'});report.cases.push({mode,width,height,routeRoundtrip:true,appPathKeyed:true,sameLobbyTabDraftRetained:true,singleActiveLobby:true,automaticTutorialStarts:0,productionWrites:0});await ctx.close();continue
  }
  if(mode==='friendly'){await page.getByRole('button',{name:'好友房',exact:true}).click();await page.mouse.move(0,0)}
  let modeArtStates=[],tutorialLobbyEmbedded=false
  if(!baseline&&['hub','friendly'].includes(mode)){
   const tabs=page.locator('.mode-tabs>button');assert.equal(await tabs.count(),3,'lobby must retain three functional illustrated modes')
   await page.waitForFunction(()=>[...document.querySelectorAll('.mode-tab-art img')].length===3&&[...document.querySelectorAll('.mode-tab-art img')].every(img=>img.complete&&img.naturalWidth>0))
   await page.waitForTimeout(400)
   modeArtStates=await tabs.evaluateAll(buttons=>buttons.map(button=>{const art=button.querySelector('.mode-tab-art'),img=art.querySelector('img');return{slot:button.dataset.lobbyArtSlot,label:button.querySelector('small')?.textContent,active:button.classList.contains('active'),filter:getComputedStyle(art).filter,currentSrc:img.currentSrc}}))
   assert.deepEqual(modeArtStates.map(item=>item.label),['MATCH','ROOM','TUTORIAL'])
   assert.equal(modeArtStates.filter(item=>item.active).length,1,'exactly one visible lobby mode must be selected')
   for(const state of modeArtStates)state.active?assert.equal(state.filter,'none','selected mode art must keep color'):assert.ok(state.filter.includes('grayscale(1)'),'unselected mode art must be gray')
   const sizeSuffix=width<=700?'_m.png':'_w.png';assert.ok(modeArtStates.every(item=>new URL(item.currentSrc).pathname.endsWith(sizeSuffix)),'each rendered mode must select only the viewport-sized asset')
   const actualArt=[...new Set(artImageRequests.map(value=>path.basename(value)))].sort();assert.deepEqual(actualArt,[1,2,3].map(index=>'Match_'+index+sizeSuffix),'image network must load only the three matching viewport assets')
   const inactive=page.locator('.mode-tabs>button:not(.active)').first();await inactive.hover();assert.ok((await inactive.locator('.mode-tab-art').evaluate(e=>getComputedStyle(e).filter)).includes('grayscale(1)'),'hover must keep unselected art gray');await inactive.focus();assert.ok((await inactive.locator('.mode-tab-art').evaluate(e=>getComputedStyle(e).filter)).includes('grayscale(1)'),'keyboard focus must keep unselected art gray');await page.evaluate(()=>document.activeElement instanceof HTMLElement&&document.activeElement.blur());await page.mouse.move(0,0)
   await page.getByRole('button',{name:'教学',exact:true}).click();await page.mouse.move(0,0)
   assert.equal(await page.locator('.mode-tabs>button').count(),3,'teaching must keep all three lobby modes visible')
   assert.equal(await page.locator('.page-head').count(),1,'teaching must keep the shared lobby heading visible')
   assert.equal(await page.locator('.tutorial-page.embedded').count(),1,'teaching must render inside the current lobby content area')
   assert.equal(await page.locator('.tutorial-return').count(),0,'teaching must not restore a separate full-page return bar')
   assert.equal(await page.locator('.mode-tabs>button.active').getAttribute('data-lobby-art-slot'),'tutorial','teaching must remain the selected lobby mode')
   tutorialLobbyEmbedded=true
   if(mode==='hub')await page.screenshot({path:path.join(output,'tutorial-lobby-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'})
   await page.getByRole('button',{name:mode==='friendly'?'好友房':'匹配',exact:true}).click();await page.mouse.move(0,0)
  }
  await page.waitForFunction(()=>window.__coverReady&&document.querySelector('.deck-cover')?.getAttribute('data-cover-cards')?.split(',').length===3)
  if(mode==='editor'){
   await page.waitForFunction(()=>document.querySelector('.deck-list-header .deck-cover')?.getAttribute('data-cover-cards')?.split(',').length===3)
   if(width<700)await page.locator('.deck-mobile-nav').getByRole('button',{name:/^牌表/}).click()
  }
  await page.waitForFunction(()=>[...document.querySelectorAll('.deck-cover__master-banner,.deck-cover__card img')].filter(i=>{const r=i.getBoundingClientRect();return r.width>0&&r.height>0&&r.bottom>0&&r.top<innerHeight&&r.right>0&&r.left<innerWidth&&getComputedStyle(i).visibility!=='hidden'}).every(i=>i.complete&&i.naturalWidth>0))
  const geometry=await page.locator('.deck-cover').evaluateAll(es=>es.filter(e=>e.getClientRects().length).map(e=>{const r=e.getBoundingClientRect(),t=e.querySelector('.deck-cover__copy').getBoundingClientRect();return{variant:e.dataset.coverVariant,w:r.width,h:r.height,ratio:r.width/r.height,titleFont:Number.parseFloat(getComputedStyle(e.querySelector('.deck-cover__copy b')).fontSize),copyLeft:Number.parseFloat(getComputedStyle(e.querySelector('.deck-cover__copy')).left),textInside:t.top>=r.top&&t.bottom<=r.bottom,cards:[...e.querySelectorAll('.deck-cover__card')].map(c=>{const x=c.getBoundingClientRect();return{x:x.left-r.left,y:x.top-r.top,w:x.width,h:x.height,right:x.right-r.left,bottom:x.bottom-r.top}}),overflow:document.documentElement.scrollWidth>innerWidth+1}}))
  assert.ok(geometry.length,'the tested consumer must expose a visible cover')
  for(const g of geometry){if(Math.abs(g.ratio-2.4)>.04)report.failures.push(mode+' '+width+' '+g.variant+' ratio='+g.ratio);if(!g.textInside)report.failures.push(mode+' '+width+' clipped text');if(g.overflow)report.failures.push(mode+' '+width+' page overflow');if(!g.cards.every(c=>c.y>=0&&c.y<g.h*.6&&c.bottom>g.h*.9&&c.bottom<g.h*1.4))report.failures.push(mode+' '+width+' non-bottom cards');if(!baseline&&g.w<=242){assert.equal(g.titleFont,12);assert.equal(g.copyLeft,8)}}
  if(!baseline&&['hub','friendly','room'].includes(mode)){
   const trigger=page.getByRole('button',{name:/^更换牌库：/});assert.equal(await trigger.count(),1,'each lobby state must expose one cover selector')
   if(mode==='hub')assert.ok((await trigger.boundingBox()).y>(await page.getByRole('button',{name:/^开始(?:排位|休闲)匹配$/}).boundingBox()).y,'match deck selector must follow primary controls')
   if(mode==='friendly')assert.ok((await trigger.boundingBox()).y>(await page.locator('.room-settings-block').boundingBox()).y,'friendly deck selector must follow room settings')
   const integration=await page.locator('.deck-selection-block').evaluate(e=>{const s=getComputedStyle(e),h=e.querySelector(':scope>header').getBoundingClientRect(),t=e.querySelector('.deck-selection-trigger').getBoundingClientRect();return{background:s.backgroundImage,backgroundColor:s.backgroundColor,borderLeft:s.borderLeftWidth,borderRight:s.borderRightWidth,borderBottom:s.borderBottomWidth,borderTop:s.borderTopWidth,header:{right:h.right,bottom:h.bottom},trigger:{left:t.left,top:t.top}}})
   assert.equal(integration.background,'none','deck selector must not restore a standalone panel gradient')
   assert.equal(integration.backgroundColor,'rgba(0, 0, 0, 0)','deck selector must inherit its owning panel background')
   assert.deepEqual([integration.borderLeft,integration.borderRight,integration.borderBottom],['0px','0px','0px'],'deck selector must not draw an independent outer frame')
   assert.notEqual(integration.borderTop,'0px','deck selector keeps one divider inside its owning panel')
   if(width>700)assert.ok(integration.trigger.left>integration.header.right,'desktop deck cover must sit right of status copy')
   else assert.ok(integration.trigger.top>=integration.header.bottom,'mobile deck cover must stack below status copy')
  }
  if(!baseline&&mode==='hub'){
   const totals=page.locator('[data-ui-contract="faction-totals-above-public-match"] article');assert.equal(await totals.count(),3,'ranked lobby must retain all three faction totals')
   assert.deepEqual((await totals.allTextContents()).map(value=>value.replace(/\s/g,'')),['秩序七曜值371,500','混沌七曜值280,500','命运七曜值298,500'])
   for(const name of ['排位规则','改选派系','排位匹配','休闲匹配','开始排位匹配'])assert.equal(await page.getByRole('button',{name,exact:true}).count(),1,'ranked lobby must retain '+name)
  }
  if(!baseline&&mode==='friendly'){
   for(const name of ['创建新房间','加入对战','直接观战'])assert.equal(await page.getByRole('button',{name,exact:true}).count(),1,'friendly lobby must retain '+name)
   assert.equal(await page.locator('.room-settings-block select').count(),4,'friendly creation must retain all four room rules')
  }
  if(!baseline&&mode==='room'){
   for(const name of ['复制房间码','保存房间规则','准备对战'])assert.equal(await page.getByRole('button',{name,exact:true}).count(),1,'active room must retain '+name)
   assert.equal(await page.locator('.room-rule-editor select').count(),4,'active room must retain all four editable room rules')
  }
  if(!baseline&&['hub','friendly'].includes(mode)&&width>700){const tabs=page.locator('.mode-tabs>button');for(const tab of await tabs.all())assert.ok((await tab.boundingBox()).height>=110,'desktop mode tabs must be functional illustrated blocks')}
  if(!baseline&&mode==='gallery'){
   for(const g of geometry){assert.ok(g.cards[0].right>=g.w,'rightmost representative card must reach the cover edge');assert.ok(g.cards[0].right<=g.w*1.03,'rightmost representative card must not crowd beyond the cover edge');for(const card of g.cards){assert.ok(card.bottom>g.h&&card.bottom<=g.h*1.1,'representative card must retain only slight bottom clipping')}}
  }
  await page.evaluate(()=>scrollTo(0,0))
  await page.screenshot({path:path.join(output,mode+'-'+width+'x'+height+'.png'),fullPage:true,animations:'disabled'})
  if(!baseline&&['hub','friendly'].includes(mode))await page.locator('.mode-tabs').screenshot({path:path.join(output,mode+'-modes-'+width+'x'+height+'.png'),animations:'disabled'})
  if(!baseline&&['hub','friendly','room'].includes(mode))await page.locator('.deck-selection-block').screenshot({path:path.join(output,mode+'-deck-'+width+'x'+height+'.png'),animations:'disabled'})
  const selectorCase=!['gallery','editor'].includes(mode)
  if(selectorCase){const trigger=page.getByRole('button',{name:/^更换牌库：/});if(mode==='hub'&&width===1440){await trigger.focus();await page.keyboard.press('Enter');report.keyboardSelector=true}else if(touch){await trigger.tap();report.touchSelectors=(report.touchSelectors||0)+1}else await trigger.click();await page.getByRole('dialog').waitFor();await page.getByRole('button',{name:'取消',exact:true}).click()}
  report.cases.push({mode,width,height,geometry,modeArtStates,artImageRequests,tutorialLobbyEmbedded,actualSelectorOpenAndCancel:selectorCase});await ctx.close()
 }
 assert.deepEqual(report.pageErrors,[]);if(baseline)assert.ok(report.failures.length>0);else assert.deepEqual(report.failures,[])
 console.log(JSON.stringify({baseline,cases:report.cases.length,failures:report.failures,pageErrors:report.pageErrors.length}))
}catch(error){report.fatal=error.stack||String(error);throw error}
finally{report.sourceAfter=hashes();report.sourcesStable=JSON.stringify(report.sourceBefore)===JSON.stringify(report.sourceAfter);fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));await browser?.close();await server.close();if(!report.sourcesStable)process.exitCode=1}
