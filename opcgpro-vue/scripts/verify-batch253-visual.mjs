import path from 'node:path'
import fs from 'node:fs'
import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch253-ui'
fs.mkdirSync(out,{recursive:true})
const qaCardArtwork={
 'S02-06M2':'/assets/l12/special/master/S02-06M2.png',
 'ST02-M1':'/assets/l12/special/master/ST02-M1.png',
 'ST04-M1':'/assets/l12/special/master/ST04-M1.png',
}
const qaCardManifest=JSON.stringify({schemaVersion:3,catalogVersion:'batch299-controlled-art',assetVersion:'batch299-controlled-art',basePath:'/',cards:Object.fromEntries(Object.entries(qaCardArtwork).map(([cardId,url])=>[cardId,{cardId,contentHash:'qa-'+cardId,width:1240,height:1732,orientation:'portrait',variants:{thumbWebp:url,boardWebp:url,detailWebp:url}}]))})
const entry = `
import {createApp,h} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import GamePage from '/src/l12/GamePage.vue'
import SandboxCardPicker from '/src/l12/game/SandboxCardPicker.vue'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import DeckConstructionBrowser from '/src/l12/site/DeckConstructionBrowser.vue'
import SiteShell from '/src/l12/site/SiteShell.vue'
import {friendApi,platformState,rankedApi} from '/src/l12/platform.ts'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
const catalog=await loadDeckCatalog()
const card=(def,id)=>({...def,cardId:def.id,instanceId:id,name:def.nameZh,cardType:def.cardType,cost:def.cost||0,baseTroops:def.troops||0,troops:def.troops||0,tapped:false,disasterLevel:def.disasterLevel||0,summonRound:-1,effectText:def.effect})
const legions=catalog.filter(c=>c.cardType==='legion').slice(0,6)
const masters=catalog.filter(c=>c.cardType==='master').slice(0,2)
const disasters=catalog.filter(c=>c.cardType==='destruction').slice(0,4)
const battleArtwork=['S02-06M2','ST02-M1','ST04-M1'].map(id=>catalog.find(card=>card.id===id)).filter(Boolean)
if(battleArtwork.length!==3)throw new Error('Controlled battle artwork catalog entries are missing')
const battleCard=(def,id)=>{
 const template=legions[battleArtwork.indexOf(def)%legions.length]||legions[0]
 return {...card(template,id),cardId:def.id,name:def.nameZh,effectText:def.effect,isMasterLegion:true}
}
const params=new URLSearchParams(location.search)
const moraleTotal=Math.max(0,Number(params.get('morale')||12))
const moraleActive=Math.max(0,Math.min(moraleTotal,Number(params.get('active')||moraleTotal)))
const special=params.has('special')
const trialMode=params.has('trial')
const relicMode=params.has('relic')
const moraleLockMode=params.has('moraleLock')
const slotMode=params.has('slot')
const effectMode=params.get('effect')
const scoutMode=params.has('scout')
const spectatorMode=params.has('spectator')
const logMode=params.has('log')
const handTotal=Math.max(0,Math.floor(Number(params.get('hand')||6)))
const canopicIds=['S01-0216','S01-0217','S01-0218','S01-0219','S01-0220']
const canopicTrack=canopicIds.map((id,j)=>({...card(catalog.find(c=>c.id===id)||legions[0],'canopic-'+j),completed:j<3}))
const trial=card(catalog.find(c=>c.cardType==='trial')||disasters[0],'trial-wide')
const deckCatalog=catalog.map(item=>item.id===legions[0].id?{...item,nameZh:'超长构筑卡牌名称完整换行验收'}:item)
const deckEntries=[
 {cardId:legions[0].id,quantity:3,section:'main'},
 {cardId:legions[1].id,quantity:2,section:'main'},
 {cardId:(catalog.find(c=>c.cardType==='rune')||legions[2]).id,quantity:8,section:'morale'},
 {cardId:trial.cardId,quantity:1,section:'special'},
 {cardId:masters[0].id,quantity:1,section:'automatic'},
]
const players=[0,1].map(i=>({
 playerIndex:i,name:i?'对方测试长昵称十二军团':'我方测试昵称',deckName:'合成验收',faction:special&&i===0?'taiyangcheng':'otherworld',
 master:{masterId:battleArtwork[i].id,masterName:battleArtwork[i].nameZh,hp:7,maxHp:9},libraryCount:30,
 hand:spectatorMode?[]:Array.from({length:handTotal},(_,j)=>battleCard(battleArtwork[j%battleArtwork.length],i+'hand'+j)),handCount:handTotal,
 morale:Array.from({length:moraleTotal},(_,j)=>({instanceId:i+'morale'+j,cardId:'ST06-C1',tapped:j>=moraleActive,cannotUntapUntilRound:moraleLockMode?(j===0?3:j===1?2:0):0})),
 field:slotMode&&i===0?[[battleCard(battleArtwork[0],'0unit'),battleCard(battleArtwork[1],'0occupied-1'),battleCard(battleArtwork[2],'0occupied-2')],[battleCard(battleArtwork[0],'0occupied-3'),battleCard(battleArtwork[1],'0unit-payment'),battleCard(battleArtwork[2],'0occupied-5')]]:[[battleCard(battleArtwork[i],i+'unit'),null,null],[null,null,null]],
 graveyard:[battleCard(battleArtwork[(i+1)%battleArtwork.length],i+'grave')],mulliganDone:true,
 specialZones:{runes:3,trialLevel:0,godPower:[],trials:trialMode&&i===0?[trial]:[],canopicTrack:special&&i===0?canopicTrack:[]}
}))
if(relicMode)players[0].relic=battleCard(battleArtwork[2],'visual-relic')
l12State.spectating=spectatorMode
l12State.game={matchId:'synthetic-batch253',roomCode:'TEST253',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'all',disasterValue:0,players,sessionDisasters:disasters.map((d,j)=>card(d,'disaster'+j)),prompts:[],effectStack:[],stateHash:'synthetic',playerBadges:[{playerIndex:0,rankLabel:'迷雾旅人',masterTitle:'最强银臂努阿达'},{playerIndex:1,rankLabel:'未定级',masterTitle:'暂无称号'}],recentEvents:Array.from({length:20},(_,j)=>({sequence:j+1,type:j%4===0?'turn-start':j%3===0?'prompt-resolved':'attack',playerIndex:j%2,text:j%4===0?'第 '+(j/4+1)+' 回合 · 回合开始':j%3===0?'选择另外1张军团 → 公开军团':'以公开军团进攻，兵力5000 → 3000',cards:[]}))}
if(logMode){
 const source=battleCard(battleArtwork[2],'log-source'),target=battleCard(battleArtwork[1],'log-target'),hidden={...battleCard(battleArtwork[0],'log-hidden'),name:'绝密手牌',hidden:true}
 l12State.game.recentEvents=[
  {sequence:1,type:'turn-start',playerIndex:0,text:'第 3 回合 · 回合开始',cards:[]},
  {sequence:2,type:'draw',playerIndex:0,text:'〈迦具土〉使我方测试昵称抽取 1 张牌。',cards:[source]},
  {sequence:3,type:'move',playerIndex:0,text:'战术调度使〈荷鲁斯〉位移 1 格。',cards:[target]},
  {sequence:4,type:'draw',playerIndex:0,text:'我方测试昵称受到 1 点伤害并抽取 1 张牌。',cards:[]},
  {sequence:5,type:'move',playerIndex:0,text:'〈荷鲁斯〉先转为活跃再位移 1 格。',cards:[target]},
  {sequence:6,type:'draw',playerIndex:0,text:'恢复 0 点，但抽取 1 张牌。',cards:[]},
  {sequence:7,type:'damage',playerIndex:0,text:'兵力增加 0 点。',cards:[]},
  {sequence:8,type:'effect-failed',playerIndex:0,text:'未选择合法目标，兵力增加 0 点。',cards:[]},
  {sequence:9,type:'reveal',playerIndex:0,text:'检视〈绝密手牌〉后放回。',cards:[hidden]},
  {sequence:10,type:'effect',playerIndex:0,text:'仅结算公开效果。',cards:[source]},
 ]
}
if(slotMode)l12State.game.prompts=[{promptId:'occupied-slot-prompt',playerIndex:0,kind:'slot',text:'选择支付后登场位置',validChoices:['0:0','1:1'],minChoose:1,maxChoose:1,data:{choiceMode:'board-slot',targetPlayerIndex:'0'},choiceLabels:{},createdRevision:1,controller:0},{promptId:'declared-cost-prompt',playerIndex:0,kind:'resource-payment',text:'已声明费用',validChoices:['0unit','0morale0'],minChoose:1,maxChoose:1,data:{choiceMode:'resource-payment'},choiceLabels:{},createdRevision:1,controller:0}]
if(effectMode){
 const choices=effectMode==='cost2'?['pay:morale','pay:discard','no']:effectMode==='cost1'?['pay:morale','no']:['yes','no']
 l12State.game.prompts=[{promptId:'effect-cost-prompt',playerIndex:0,kind:'option',text:'迦具土',validChoices:choices,minChoose:1,maxChoose:1,data:{uiPattern:'effect-decision',effectText:'回合1次 我方军团进攻/被进攻时，可消耗1士气或弃置1张手牌：该军团本回合兵力+2000。'},choiceLabels:{'pay:morale':'消耗1士气','pay:discard':'弃置1张手牌',yes:'发动',no:'不发动'},createdRevision:1,controller:0}]
}
if(scoutMode){
 const first=legions[0],second=legions[1]
 l12State.game.prompts=[{promptId:'scout-view-confirm',playerIndex:0,kind:'option',text:'前线侦查：查看对方手牌',validChoices:['confirm'],minChoose:1,maxChoose:1,data:{action:'scout-view-confirm',layout:'single-row',displayCardIds:'scout-one|scout-two','scout-one:cardId':first.id,'scout-one:name':first.nameZh,'scout-one:cardType':first.cardType,'scout-one:effect':first.effect||'','scout-one:cost':String(first.cost||0),'scout-one:troops':String(first.troops||0),'scout-two:cardId':second.id,'scout-two:name':second.nameZh,'scout-two:cardType':second.cardType,'scout-two:effect':second.effect||'','scout-two:cost':String(second.cost||0),'scout-two:troops':String(second.troops||0)},choiceLabels:{confirm:'已查看，继续'},createdRevision:1,controller:0}]
}
window.__sentCommands=[]
l12State.socket={readyState:WebSocket.OPEN,send:payload=>window.__sentCommands.push(JSON.parse(payload))}
window.__resetSentCommands=()=>{window.__sentCommands=[];l12State.pendingAction=false}
window.__setInspectorPrompt=visible=>{l12State.game.prompts=visible?[{promptId:'inspector-prompt',playerIndex:0,kind:'option',text:'合成来源',validChoices:['yes','no'],minChoose:1,maxChoose:1,choiceLabels:{yes:'发动',no:'不发动'},data:{uiPattern:'effect-decision',sourceName:'合成来源',effectText:'登场时 可发动试炼。',sourceInstanceId:'0unit'},sourceInstanceId:'0unit',sourceCardId:legions[0].id,createdRevision:1,controller:0}]:[]}
l12State.status='online'
l12State.room={roomCode:'TEST253',yourPlayerIndex:0,players:players.map(p=>({...p,connected:true,ready:true,deckIndex:0})),decks:[],started:true}
const isPicker=new URLSearchParams(location.search).has('picker')
const isRanking=new URLSearchParams(location.search).has('ranking')
const isDeckViewer=params.has('deckviewer')
const isInviteFixture=params.has('invite')
const isOutgoingInviteFixture=params.has('outgoingInvite')
const isOnlineFixture=params.has('online')
if(isInviteFixture)l12State.friendInvitation={invitationId:'visual-invite',fromAccountId:'friend-1',fromName:'邀请方测试长昵称',roomCode:'QA299'}
if(isOutgoingInviteFixture)l12State.outgoingFriendInvitation={invitationId:'visual-outgoing-invite',targetAccountId:'friend-outgoing',roomCode:'QA300'}
if(isOnlineFixture){
 platformState.account={id:'me',username:'当前账号',role:'player',createdAt:'2026-01-01',publicHistory:false}
 platformState.token='visual-token'
 friendApi.presence=async()=>[
  {accountId:'me',username:'当前账号',online:true,activity:'idle',canInvite:false,canSpectate:false,friendStatus:'self',friendDirection:'none'},
  {accountId:'friend-playing',username:'已是好友且正在对局',online:true,activity:'playing',roomCode:'PLAY01',canInvite:false,canSpectate:true,friendStatus:'accepted',friendDirection:'none'},
  {accountId:'new-playing',username:'可添加并观战的玩家',online:true,activity:'playing',roomCode:'PLAY02',canInvite:false,canSpectate:true,friendStatus:'none',friendDirection:'none'},
  {accountId:'incoming',username:'发来申请的玩家',online:true,activity:'idle',canInvite:false,canSpectate:false,friendStatus:'pending',friendDirection:'incoming'},
 ]
}
const masterRows=masters.map((m,i)=>({rank:100+i,masterId:m.id,masterName:m.nameZh,games:999,wins:999,losses:0,winRate:100,usageRate:50,firstWinRate:100,secondWinRate:100,firstWins:500,firstGames:500,secondWins:499,secondGames:499,strongestPlayer:'合成测试玩家',title:'最强'+m.nameZh}))
rankedApi.leaderboard=async()=>({players:Array.from({length:4},(_,i)=>({rank:i+1,username:'合成测试长昵称'+i,faction:'命运',tier:'迷雾旅人',titles:['最强银臂努阿达','最强雷神索尔'],favoriteMasterId:masters[0].id,favoriteMasterName:masters[0].nameZh,displayValue:'七曜值 21,945',wins:999,losses:888})),analytics:{range:'season',summary:{matches:999,placedPlayers:4,activeMasters:2},masters:masterRows,matchups:masterRows.flatMap(a=>masterRows.map(b=>({masterId:a.masterId,opponentMasterId:b.masterId,games:999,wins:999,winRate:100,firstWins:500,firstGames:500,secondWins:499,secondGames:499})))}})
rankedApi.history=async()=>[]
const app=createApp({render:()=>isPicker?h(SandboxCardPicker,{title:'GM横卡验收',allowedTypes:['destruction']}):isRanking?h(RankingsPage):isDeckViewer?h(DeckConstructionBrowser,{entries:deckEntries,catalog:deckCatalog,title:'公开牌库完整构筑'}):(isInviteFixture||isOutgoingInviteFixture||isOnlineFixture)?h(SiteShell,null,{default:()=>h('div',{style:'padding:40px'},'非阻塞页面内容仍可见')}):h(GamePage)})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
let browser
const server = await createServer({root,server:{host:'127.0.0.1',port:0,strictPort:false},plugins:[{name:'batch253-synthetic',resolveId(id){if(id==='/__qa__.js')return id},load(id){if(id==='/__qa__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{
 if(req.url?.match(/^\/card-assets\/card-assets\.manifest\.json(\?|$)/)){res.setHeader('Content-Type','application/json');res.end(qaCardManifest);return}
 if(req.url?.match(/^\/__qa__(\?|$)/)){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__qa__.js"></script>');return}next()
})}}]})
try {
 await server.listen()
 const port=server.httpServer.address().port
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage()
 const errors=[]
 page.on('pageerror',e=>errors.push(e.message))
 await page.route('**/*',route=>{const url=new URL(route.request().url());return url.hostname==='127.0.0.1'?route.continue():route.abort()})
 const reports=[]
 for(const [width,height] of [[1920,1080],[1440,1000],[1440,810],[1280,800],[1280,720]]){
  await page.setViewportSize({width,height})
  await page.goto('http://127.0.0.1:'+port+'/__qa__')
  await page.locator('.player-summary').first().waitFor()
  await page.waitForFunction(()=>{
   const images=[...document.querySelectorAll('.hand-card-wrap .l12-card-image__img,.formation-slot .l12-card-image__img')]
   return images.length===8&&images.every(image=>image.complete&&image.naturalWidth>0&&!image.currentSrc.startsWith('data:'))
  },undefined,{timeout:10000}).catch(async error=>{
   const state=await page.evaluate(()=>[...document.querySelectorAll('.hand-card-wrap .l12-card-image,.formation-slot .l12-card-image')].map(image=>({source:image.dataset.source,card:image.closest('[data-card-instance-id]')?.dataset.cardInstanceId,url:image.querySelector('img')?.currentSrc,naturalWidth:image.querySelector('img')?.naturalWidth})))
   throw new Error('Controlled battle artwork did not load: '+JSON.stringify(state),{cause:error})
  })
  await page.waitForTimeout(500)
  await page.screenshot({path:path.join(out,`battle-${width}x${height}.png`)})
  reports.push(await page.evaluate(()=>{
   const box=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,scroll:e.scrollHeight,client:e.clientHeight}}
   const artwork=[...document.querySelectorAll('.hand-card-wrap .l12-card-image,.formation-slot .l12-card-image')]
   return {width:innerWidth,height:innerHeight,summary:box('.player-panel'),rail:box('.right-rail'),log:box('.event-list'),dock:box('.battle-utility-dock'),clock:box('.my-status-lane'),hand:box('.board-center>.l12-hand:last-child'),phase:box('.l12-phase-track'),seam:box('.board-seam'),font:getComputedStyle(document.querySelector('.event-message')).fontSize,artwork:{count:artwork.length,sameOrigin:artwork.filter(image=>image.dataset.source==='sameOrigin').length},opponentBadges:document.querySelectorAll('.opponent-summary .ranked-identity-badge').length,myBadges:document.querySelectorAll('.my-summary .ranked-identity-badge').length,summaryText:document.querySelector('.player-panel').textContent}
  }))
 }
 for(const report of reports){
 if(report.summary.scroll>report.summary.client+1)throw new Error('Player summary clipped at '+report.width)
 if(report.clock.y+report.clock.h>report.hand.y+1)throw new Error('Clock overlaps hand at '+report.width)
 if(report.hand.y+report.hand.h>report.height+1)throw new Error('Hand leaves viewport at '+report.width+'x'+report.height)
 if(report.dock.y+report.dock.h>report.height+1)throw new Error('Utility dock leaves viewport at '+report.width+'x'+report.height)
 assert.deepEqual(report.artwork,{count:8,sameOrigin:8},'all visible face-up battle cards must use controlled real local artwork at '+report.width+'x'+report.height)
 assert.equal(report.opponentBadges,0,'server placeholder rank/title values must render as blank at '+report.width+'x'+report.height)
 assert.equal(report.myBadges,2,'real rank and title badges must remain visible at '+report.width+'x'+report.height)
 assert(!report.summaryText.includes('未定级')&&!report.summaryText.includes('暂无称号'),'player summary must not display rank/title placeholders at '+report.width+'x'+report.height)
 }
 const logReports=[]
 for(const viewport of [{width:1920,height:1080},{width:1440,height:810},{width:1280,height:720}]){
  await page.setViewportSize(viewport)
  await page.goto('http://127.0.0.1:'+port+'/__qa__?log=1')
  await page.locator('[data-event-sequence="10"]').waitFor()
  const logResult=await page.evaluate(()=>{
   const messages=Object.fromEntries([...document.querySelectorAll('[data-event-sequence]')].map(row=>[row.dataset.eventSequence,row.querySelector('.event-message')?.textContent?.trim()||'']))
   const links=[...document.querySelectorAll('.log-card-link')].map(link=>link.textContent?.trim())
   const list=document.querySelector('.event-list')
   return {messages,links,rowCount:Object.keys(messages).length,scrollWidth:list.scrollWidth,clientWidth:list.clientWidth}
  })
  assert.equal(logResult.rowCount,8,'only the standalone zero-change row may be omitted')
  assert.equal(logResult.messages['2'],'我方迦具土：抽取1张牌。','simple draw must use the shared compact result format')
  assert.equal(logResult.messages['3'],'我方战术调度：〈荷鲁斯〉位移1格。','simple movement must use the shared compact result format')
  assert.equal(logResult.messages['4'],'我方受到1点伤害并抽取1张牌。','compound draw must preserve its preceding outcome')
  assert.equal(logResult.messages['5'],'我方〈荷鲁斯〉先转为活跃再位移1格。','compound movement must preserve its preceding outcome')
  assert.equal(logResult.messages['6'],'我方恢复0点，但抽取1张牌。','mixed zero and positive outcomes must remain visible')
  assert.equal(logResult.messages['8'],'我方未选择合法目标，兵力增加0点。','meaningful failed outcomes must remain visible')
  assert(!logResult.messages['7'],'standalone zero-change noise must be omitted')
  assert(!logResult.messages['9'].includes('绝密手牌')&&logResult.messages['9'].includes('隐藏卡牌'),'hidden card identities must be redacted')
  assert(!logResult.messages['10'].includes('迦具土'),'public card metadata absent from authoritative text must not be appended')
  assert(logResult.links.includes('荷鲁斯')&&logResult.links.includes('迦具土'),'full public card names in the current event text must remain clickable')
  assert(logResult.links.every(link=>!/^S(?:T|0\d)-/.test(link||'')),'log links must not expose card IDs')
  assert(logResult.scrollWidth<=logResult.clientWidth+1,'battle log must not overflow horizontally at '+viewport.width+'x'+viewport.height)
  logReports.push({viewport,...logResult})
  await page.screenshot({path:path.join(out,'battle-log-'+viewport.width+'x'+viewport.height+'.png')})
 }
 for(const viewport of [{width:1920,height:1080},{width:1440,height:810},{width:1280,height:720}]){
  await page.setViewportSize(viewport)
  await page.goto('http://127.0.0.1:'+port+'/__qa__?hand=6&relic=1')
  await page.locator('.l12-player-mat.side-my .relic-zone .card-tile').waitFor()
  const surfaces=await page.evaluate(()=>{
   const read=selector=>{const element=document.querySelector(selector),rect=element.getBoundingClientRect(),style=getComputedStyle(element);return {left:rect.left,right:rect.right,top:rect.top,bottom:rect.bottom,width:rect.width,height:rect.height,borderColor:style.borderColor,borderWidth:style.borderWidth,borderRadius:style.borderRadius,boxShadow:style.boxShadow}}
   const hand=read('.board-center>.l12-hand:last-child .hand-card-wrap .card-tile')
   const field=read('.l12-player-mat.side-my .formation-slot .card-tile')
   const slot=read('.l12-player-mat.side-my .formation-slot')
   const relic=read('.l12-player-mat.side-my .relic-zone')
   const relicCard=read('.l12-player-mat.side-my .relic-zone .card-tile')
   const hit=selector=>{const element=document.querySelector(selector),rect=element.getBoundingClientRect();return Boolean(document.elementFromPoint(rect.left+rect.width/2,rect.top+rect.height/2)?.closest(selector)===element)}
   return {hand,field,slot,relic,relicCard,handHit:hit('.board-center>.l12-hand:last-child .hand-card-wrap .card-tile'),fieldHit:hit('.l12-player-mat.side-my .formation-slot .card-tile')}
  })
  for(const [name,surface] of [['hand',surfaces.hand],['field',surfaces.field]]){
   assert.equal(surface.borderColor,'rgba(0, 0, 0, 0)',name+' card must not restore a visible decorative border at '+viewport.width+'x'+viewport.height)
   assert.equal(surface.borderRadius,'0px',name+' card must not restore a decorative rounded frame at '+viewport.width+'x'+viewport.height)
   assert.equal(surface.boxShadow,'none',name+' card must not restore a persistent decorative shadow at '+viewport.width+'x'+viewport.height)
  }
  assert.notEqual(surfaces.slot.borderColor,'rgba(0, 0, 0, 0)','six-slot field boundary must remain visible at '+viewport.width+'x'+viewport.height)
  assert.equal(surfaces.handHit,true,'hand card hit target must survive frame removal at '+viewport.width+'x'+viewport.height)
  assert.equal(surfaces.fieldHit,true,'field legion hit target must survive frame removal at '+viewport.width+'x'+viewport.height)
  assert(Math.abs(surfaces.relic.width/surfaces.relic.height-5/7)<.01,'relic zone must use the 5:7 card ratio at '+viewport.width+'x'+viewport.height)
  assert(Math.abs(surfaces.relicCard.width/surfaces.relicCard.height-5/7)<.01,'relic card surface must use the 5:7 card ratio at '+viewport.width+'x'+viewport.height)
  assert(surfaces.relicCard.left>=surfaces.relic.left-1&&surfaces.relicCard.right<=surfaces.relic.right+1&&surfaces.relicCard.top>=surfaces.relic.top-1&&surfaces.relicCard.bottom<=surfaces.relic.bottom+1,'relic card must stay inside its zone at '+viewport.width+'x'+viewport.height)
  await page.screenshot({path:path.join(out,'frameless-cards-relic-'+viewport.width+'x'+viewport.height+'.png')})
 }
 await page.setViewportSize({width:1920,height:1080})
 for(const handCount of [0,1,6,12,36]){
  await page.goto('http://127.0.0.1:'+port+'/__qa__?hand='+handCount)
  await page.locator('.l12-player-mat.side-my .formation-slot .card-tile').first().waitFor()
  const hand=page.locator('.board-center>.l12-hand:last-child')
  const handCards=hand.locator('.hand-card-wrap')
  assert.equal(await handCards.count(),handCount,'hand fixture must render every known card at count '+handCount)
  const geometry=await page.evaluate(()=>{
   const box=element=>{const rect=element.getBoundingClientRect();return {left:rect.left,right:rect.right,top:rect.top,bottom:rect.bottom,width:rect.width,height:rect.height}}
   const board=document.querySelector('.board-center'),hand=document.querySelector('.board-center>.l12-hand:last-child'),field=document.querySelector('.l12-player-mat.side-my .formation-slot .card-tile')
   const cards=[...hand.querySelectorAll('.hand-card-wrap')]
   const fieldStyle=getComputedStyle(field)
   return {board:box(board),hand:box(hand),field:box(field),fieldCss:{width:fieldStyle.width,height:fieldStyle.height},cards:cards.map(element=>{const style=getComputedStyle(element);return {...box(element),cssWidth:style.width,cssHeight:style.height}}),overflowing:hand.classList.contains('overflowing'),scrollWidth:hand.scrollWidth,clientWidth:hand.clientWidth,zIndex:getComputedStyle(hand).zIndex}
  })
  const scale=geometry.field.width/114.4
  assert(geometry.hand.left-geometry.board.left>=150*scale,'hand must stay inset from the extra-zone side at count '+handCount)
  assert(geometry.board.right-geometry.hand.right>=150*scale,'hand must stay inset from the timer side at count '+handCount)
  assert.equal(geometry.zIndex,'40','hand must remain above overlapping battlefield cards')
  for(const cardBox of geometry.cards){
   assert.equal(cardBox.cssWidth,geometry.fieldCss.width,'hand card CSS width must match a field legion at count '+handCount)
   assert.equal(cardBox.cssHeight,geometry.fieldCss.height,'hand card CSS height must match a field legion at count '+handCount)
  }
  if(handCount===36){
   assert.equal(geometry.overflowing,true,'large hands must switch to horizontal overflow')
   assert(geometry.scrollWidth>geometry.clientWidth,'large hands must preserve all cards in a scrollable safe lane')
  }
  if(handCount===12){
   const topCard=handCards.last()
   const isClickable=await topCard.evaluate(element=>{const rect=element.getBoundingClientRect(),hit=document.elementFromPoint(rect.left+rect.width/2,rect.top+rect.height/2);return Boolean(hit?.closest('.hand-card-wrap')===element)})
   assert.equal(isClickable,true,'top hand card must retain pointer priority over the battlefield')
  }
  await page.screenshot({path:path.join(out,'field-sized-hand-'+handCount+'.png')})
 }
 await page.goto('http://127.0.0.1:'+port+'/__qa__?hand=9&spectator=1')
 await page.locator('.spectator-hand .card-back').first().waitFor()
 const spectatorHands=page.locator('.board-center>.l12-hand.hidden')
 assert.equal(await spectatorHands.count(),2,'spectator must see both hidden hand zones')
 for(let index=0;index<2;index++){
  assert.equal(await spectatorHands.nth(index).locator('.card-back').count(),9,'spectator must see the full authoritative hand count as card backs')
  assert.equal(await spectatorHands.nth(index).locator('.card-tile').count(),0,'spectator hand must not leak card identities')
 }
 await page.screenshot({path:path.join(out,'spectator-both-hands.png')})
 for(const viewport of [{width:1100,height:820},{width:680,height:820}]){
  await page.setViewportSize(viewport)
  await page.goto('http://127.0.0.1:'+port+'/__qa__?deckviewer=1')
  const viewer=page.locator('[data-ui-contract="shared-deck-construction-browser"]')
  await viewer.waitFor()
  assert.equal(await viewer.locator('.construction-grid>button').count(),5,'shared deck viewer must include main, morale, trial and automatic extra cards')
  const options=await viewer.locator('select').first().locator('option').allTextContents()
  for(const label of ['主牌库','士气区','试炼区','自动额外区'])assert(options.includes(label),'deck viewer must expose '+label)
  const names=await viewer.locator('.construction-grid>button>span').evaluateAll(elements=>elements.map(element=>{const rect=element.getBoundingClientRect();return {text:element.textContent?.trim(),height:rect.height,scrollHeight:element.scrollHeight,clientHeight:element.clientHeight,whiteSpace:getComputedStyle(element).whiteSpace,overflowWrap:getComputedStyle(element).overflowWrap}}))
  const longName=names.find(item=>item.text==='超长构筑卡牌名称完整换行验收')
  assert(longName&&longName.height>20&&longName.scrollHeight<=longName.clientHeight+1,'long deck name must wrap to its full stable row instead of clipping')
  assert(names.every(item=>item.whiteSpace==='normal'),'all deck names must permit full wrapping')
  const frames=await viewer.locator('.construction-grid .l12-card-image').evaluateAll(elements=>elements.map(element=>{const style=getComputedStyle(element);return {width:element.getBoundingClientRect().width,borderWidth:style.borderWidth,boxSizing:style.boxSizing}}))
  assert.equal(frames.length,5,'every construction entry must render a card frame')
  assert(frames.every(frame=>frame.width>0&&frame.borderWidth==='1px'&&frame.boxSizing==='border-box'),'all viewer cards must keep their image frame')
  if(viewport.width<=700)assert.equal(await viewer.locator('.construction-workspace>aside').isVisible(),false,'narrow shared viewer must hide only the redundant aside, not construction cards')
  await page.screenshot({path:path.join(out,'shared-deck-viewer-'+viewport.width+'.png')})
 }
 await page.setViewportSize({width:1280,height:800})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?invite=1')
 const invitation=page.locator('.invitation-gate')
 await invitation.waitFor()
 const inviteBounds=await invitation.boundingBox()
 assert(inviteBounds&&Math.abs(1280-(inviteBounds.x+inviteBounds.width)-18)<1.5&&Math.abs(800-(inviteBounds.y+inviteBounds.height)-18)<1.5,'friend invitation must stay at the bottom-right without taking over the page')
 assert.equal(await invitation.locator('.site-modal-mask').count(),0,'friend invitation must not add a modal backdrop')
 await page.screenshot({path:path.join(out,'friend-invitation-nonmodal.png')})
 await page.getByRole('button',{name:'最小化好友对战邀请',exact:true}).click()
 await page.locator('.invitation-minimized').waitFor()
 await page.screenshot({path:path.join(out,'friend-invitation-minimized.png')})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?outgoingInvite=1')
 const outgoingInvitation=page.locator('.outgoing-invitation-gate')
 await outgoingInvitation.waitFor()
 assert.equal(await outgoingInvitation.locator('.site-modal-mask').count(),0,'sent friend invitation must not add a modal backdrop')
 await page.screenshot({path:path.join(out,'friend-invitation-sent.png')})
 await page.getByRole('button',{name:'撤回邀请',exact:true}).click()
 const sentCancellation=await page.evaluate(()=>window.__sentCommands.at(-1))
 assert.equal(sentCancellation.type,'cancelFriendInvitation','sent invitation card must send the cancel command')
 assert.equal(sentCancellation.invitationId,'visual-outgoing-invite','sent invitation card must cancel the exact authoritative invitation id')
 assert.equal(await outgoingInvitation.count(),1,'sent invitation must remain until the matching authoritative event arrives')
 await page.goto('http://127.0.0.1:'+port+'/__qa__?online=1')
 const unreadButton=page.locator('.site-utilities button.has-unread')
 await unreadButton.waitFor()
 assert.equal((await unreadButton.locator('.utility-unread').textContent())?.trim(),'1','sidebar must show the unread incoming friend request count')
 await unreadButton.click()
 const entries=page.locator('.online-entry')
 await entries.first().waitFor()
 assert.equal(await entries.count(),4,'online fixture must render every presence row')
 assert.equal(await page.locator('.online-unread').count(),1,'incoming request row must keep an explicit unread prompt')
 for(const [name,actions] of [['已是好友且正在对局',['好友 · 邀战','观战']],['可添加并观战的玩家',['添加好友','观战']]]){
  const row=entries.filter({hasText:name})
  for(const action of actions)await row.getByRole('button',{name:action,exact:true}).waitFor()
 }
 await page.screenshot({path:path.join(out,'online-adjacent-actions-unread.png')})
 await page.setViewportSize({width:1280,height:900})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?ranking=1')
 await page.locator('.player-table .tr').first().waitFor()
 if(await page.locator('.player-table .thead>span').count()!==10)throw new Error('Player leaderboard must have 10 columns')
 await page.screenshot({path:path.join(out,'rankings-players.png')})
 await page.getByRole('button',{name:'最强称号规则',exact:true}).click()
 await page.getByRole('heading',{name:'“最强”称号规则',exact:true}).waitFor()
 await page.screenshot({path:path.join(out,'rankings-rules.png')})
 await page.keyboard.press('Escape')
 await page.getByRole('button',{name:'对阵一览',exact:true}).click()
 await page.locator('.matrix-cell').first().waitFor()
 const cellHeights=await page.locator('.matrix-cell,.matrix-head,.matrix-row-head').evaluateAll(nodes=>nodes.map(n=>n.getBoundingClientRect().height))
 if(cellHeights.some(height=>Math.abs(height-76)>1))throw new Error('Matrix row height must stay at the enlarged 76px avatar-safe size')
 await page.screenshot({path:path.join(out,'rankings-matrix.png')})
 for(const viewport of [{width:1920,height:1080},{width:760,height:900}]){
  await page.setViewportSize(viewport)
  await page.goto('http://127.0.0.1:'+port+'/__qa__')
  await page.locator('.l12-player-mat.side-my .formation-slot .card-tile').first().hover()
  const inspector=page.locator('[data-ui-contract="selected-card-inspector"]')
  const image=inspector.locator('.inspector-card-image')
  await image.waitFor()
  const normal={outer:await inspector.boundingBox(),image:await image.boundingBox()}
  const tagLayout=await inspector.evaluate(element=>{
   const group=element.querySelector('.inspector-card-tags');if(!group)return null
   const outer=element.getBoundingClientRect(),rect=group.getBoundingClientRect(),style=getComputedStyle(group)
   return {outerCenter:outer.left+outer.width/2,groupCenter:rect.left+rect.width/2,groupWidth:rect.width,justify:style.justifyContent,widthRule:style.width,spanWidths:[...group.querySelectorAll('span')].map(span=>span.getBoundingClientRect().width)}
  })
  assert(tagLayout&&tagLayout.spanWidths.length>1,'Inspector fixture must expose multiple natural-width tags')
  assert(Math.abs(tagLayout.outerCenter-tagLayout.groupCenter)<1.5,`Inspector tag group must stay centered at ${viewport.width}px`)
  assert.equal(tagLayout.justify,'center','Inspector wrapped tags must center within their compact group')
  assert(tagLayout.spanWidths.every(width=>width<tagLayout.groupWidth),'Inspector tags must keep natural widths instead of stretching to the container edges')
  await page.evaluate(()=>window.__setInspectorPrompt(true))
  await page.locator('.prompt-panel').waitFor()
  await inspector.evaluate(element=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))
  const floating={outer:await inspector.boundingBox(),image:await image.boundingBox()}
  for(const key of ['outer','image'])for(const axis of ['x','y','width','height'])assert(Math.abs(normal[key][axis]-floating[key][axis])<1.1,`Inspector ${key}.${axis} must not jump at ${viewport.width}px: ${normal[key][axis]} -> ${floating[key][axis]}`)
  await page.getByRole('button',{name:'最小化弹框',exact:true}).click()
  await page.locator('.prompt-minimized-bar').waitFor()
  await inspector.evaluate(element=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))
  const minimized={outer:await inspector.boundingBox(),image:await image.boundingBox()}
  for(const key of ['outer','image'])for(const axis of ['x','y','width','height'])assert(Math.abs(normal[key][axis]-minimized[key][axis])<1.1,`Minimized prompt must restore inspector ${key}.${axis} at ${viewport.width}px: ${normal[key][axis]} -> ${minimized[key][axis]}`)
  assert(minimized.outer.x>=-1&&minimized.outer.x+minimized.outer.width<=viewport.width+1,'Inspector must stay horizontally reachable')
  await page.screenshot({path:path.join(out,'inspector-stable-'+viewport.width+'.png')})
 }
 await page.setViewportSize({width:1920,height:1080})
 for(const scenario of [
  {name:'morale-1',query:'morale=1&active=1',expected:1,label:'1/1'},
  {name:'morale-low',query:'morale=2&active=2',expected:2,label:'2/2'},
  {name:'morale-4',query:'morale=4&active=4',expected:4,label:'4/4'},
  {name:'morale-5',query:'morale=5&active=5',expected:5,label:'5/5'},
  {name:'morale-8',query:'morale=8&active=8',expected:8,label:'8/8'},
  {name:'morale-10',query:'morale=10&active=10',expected:10,label:'10/10'},
  {name:'morale-11',query:'morale=11&active=11',expected:11,label:'11/11'},
  {name:'morale-12',query:'morale=12&active=12',expected:12,label:'12/12'},
  {name:'morale-lock',query:'morale=3&active=3&moraleLock=1',expected:3,label:'3/3',locked:true},
  {name:'morale-overflow-special',query:'morale=18&active=5&special=1',expected:12,label:'5/18'},
  {name:'morale-trial',query:'morale=3&active=3&trial=1',expected:3,label:'3/3'},
 ]){
  await page.goto('http://127.0.0.1:'+port+'/__qa__?'+scenario.query)
  await page.locator('.resource-zone').first().waitFor()
  const result=await page.locator('.l12-player-mat.side-my').evaluate((mat,expectedLabel)=>{
   const box=selector=>{const element=mat.querySelector(selector);if(!element)return null;const rect=element.getBoundingClientRect();return {left:rect.left,right:rect.right,top:rect.top,bottom:rect.bottom,width:rect.width,height:rect.height,cx:rect.left+rect.width/2,cy:rect.top+rect.height/2}}
   const resource=box('.resource-zone'),piles=box('.mat-piles'),stack=box('.resource-morale-stack')
   const orbs=[...mat.querySelectorAll('.resource-morale-stack .morale-orb')].map(element=>{const rect=element.getBoundingClientRect(),style=getComputedStyle(element);return {left:rect.left,right:rect.right,top:rect.top,bottom:rect.bottom,width:rect.width,height:rect.height,cssWidth:style.width,cssHeight:style.height}})
   const canopics=[...mat.querySelectorAll('.canopic-orb')].map(element=>element.getBoundingClientRect())
   const trial=box('.trial-zone'),relic=box('.relic-zone')
   return {resource,piles,stack,orbs,canopics:canopics.map(rect=>({top:rect.top,bottom:rect.bottom})),trial,relic,label:mat.querySelector('.resource-morale-count')?.textContent?.trim(),expectedLabel}
  },scenario.label)
  assert.equal(result.orbs.length,scenario.expected,scenario.name+' must render at most twelve real morale circles')
  assert.equal(result.label,scenario.label,scenario.name+' must keep the full two-digit morale counter')
  assert(result.orbs.every(orb=>orb.cssWidth==='32px'&&orb.cssHeight==='32px'),scenario.name+' morale circles must keep the 32px design token: '+JSON.stringify(result.orbs))
  assert(Math.abs(result.resource.cy-result.piles.cy)<2,scenario.name+' resource group must dynamically center against piles')
  assert(result.piles.right+8<=result.resource.left,scenario.name+' piles and resources need a visible gap')
  assert(result.orbs.every(orb=>orb.left>=result.stack.left+8&&orb.right<=result.stack.right-8&&orb.top>=result.stack.top+8&&orb.bottom<=result.stack.bottom-8),scenario.name+' morale circles need inner breathing room')
  const rows=Object.values(result.orbs.reduce((grouped,orb)=>{const key=Math.round(orb.top);(grouped[key]??=[]).push(orb);return grouped},{}))
  assert(rows.every(row=>row.length<=3),scenario.name+' morale circles must wrap three per row')
  const fullRow=rows.find(row=>row.length===3)
  if(fullRow)assert(Math.abs((fullRow[0].left+fullRow.at(-1).right)/2-result.stack.cx)<1.5,scenario.name+' fixed three-column morale grid must be centered')
  const moraleScale=result.orbs[0]?.width/32||1
  const fixedGridStart=result.stack.cx-(32*3+10*2)*moraleScale/2
  assert(rows.every(row=>row.every((orb,index)=>Math.abs(orb.left-(fixedGridStart+index*42*moraleScale))<1.5)),scenario.name+' partial morale rows must fill the centered fixed columns from left to right: '+JSON.stringify({fixedGridStart,rows}))
  const opponent=await page.locator('.l12-player-mat.side-opponent .resource-morale-stack').evaluate(stack=>({
   center:(stack.getBoundingClientRect().left+stack.getBoundingClientRect().right)/2,
   orbs:[...stack.querySelectorAll('.morale-orb')].map(orb=>{const rect=orb.getBoundingClientRect();return {left:rect.left,top:rect.top,width:rect.width}}),
  }))
  const opponentScale=opponent.orbs[0].width/32
  assert.equal(opponent.orbs.length,scenario.expected,scenario.name+' opponent count')
  opponent.orbs.forEach((orb,index)=>assert(Math.abs(orb.left-(opponent.center-58*opponentScale+(index%3)*42*opponentScale))<1.5,scenario.name+' opponent fixed columns'))
  if(result.orbs.length>3){
   assert(result.orbs[3].top>result.orbs[0].top,scenario.name+' my morale must wrap down')
   assert(opponent.orbs[3].top<opponent.orbs[0].top,scenario.name+' opponent morale must wrap up')
  }
  if(scenario.locked){
   for(const side of ['side-my','side-opponent']){
    const lock=page.locator('.l12-player-mat.'+side+' [data-ui-contract="active-morale-lock"]')
    assert.equal(await lock.count(),1,scenario.name+' '+side+' must render only the currently effective lock')
    const geometry=await lock.evaluate(element=>{
     const lockRect=element.getBoundingClientRect(),iconRect=element.parentElement.querySelector('img').getBoundingClientRect(),style=getComputedStyle(element)
     return {pointerEvents:style.pointerEvents,title:element.getAttribute('title'),overlapsIcon:lockRect.left<iconRect.right&&lockRect.right>iconRect.left&&lockRect.top<iconRect.bottom&&lockRect.bottom>iconRect.top}
    })
    assert.equal(geometry.pointerEvents,'none',scenario.name+' lock must not intercept morale clicks')
    assert.equal(geometry.title,'本轮重置阶段无法转为活跃',scenario.name+' lock tooltip must describe reset-phase behavior')
    assert.equal(geometry.overlapsIcon,false,scenario.name+' lock must not cover the morale symbol')
   }
  }
  if(scenario.name==='morale-overflow-special'){
   assert.equal(result.canopics.length,5,'five Canopic markers must be present')
   assert(new Set(result.canopics.map(item=>Math.round(item.top))).size===1,'five Canopic markers must stay on one row')
  }
  if(scenario.name==='morale-trial'){
   assert(!(result.trial.left<result.relic.right&&result.trial.right>result.relic.left&&result.trial.top<result.relic.bottom&&result.trial.bottom>result.relic.top),'trial and relic zones must not overlap')
   const progress=await page.locator('.l12-player-mat.side-my .trial-progress').evaluate(element=>{const rect=element.getBoundingClientRect(),style=getComputedStyle(element);return {width:rect.width,height:rect.height,cssWidth:style.width,cssHeight:style.height,minWidth:style.minWidth,minHeight:style.minHeight,whiteSpace:style.whiteSpace,fontVariantNumeric:style.fontVariantNumeric,text:element.textContent?.trim()}})
   assert.equal(progress.cssWidth,'46px','trial progress numeric badge must keep its enlarged design width')
   assert.equal(progress.cssHeight,'46px','trial progress numeric badge must keep its enlarged design height')
   assert.equal(progress.minWidth,'46px','trial progress badge must not squeeze horizontally')
   assert.equal(progress.minHeight,'46px','trial progress badge must not squeeze vertically')
   assert.equal(progress.whiteSpace,'nowrap','trial progress digits must remain on one line')
   assert.equal(progress.text,'0','trial progress fixture must expose the authoritative numeric value')
  }
  await page.screenshot({path:path.join(out,scenario.name+'.png')})
 }
 await page.setViewportSize({width:1920,height:1080})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?slot=1')
 const ownSlots=page.locator('.l12-player-mat.side-my .formation-slot')
 await ownSlots.first().waitFor()
 assert.equal(await ownSlots.filter({has:page.locator('.card-tile')}).count(),6,'occupied-slot regression fixture must keep the target battlefield full')
 assert.equal(await ownSlots.filter({has:page.locator('.card-tile')}).filter({hasNot:page.locator('.missing-card')}).count(),6,'all full-field fixtures must remain real cards')
 assert.equal(await ownSlots.locator('.available').count(),0,'available is a class on the slot itself, not a descendant')
 assert.equal(await page.locator('.l12-player-mat.side-my .formation-slot.available').count(),2,'only authoritative occupied slot choices may highlight')
 await page.getByRole('button',{name:'最小化弹框',exact:true}).click()
 await page.screenshot({path:path.join(out,'occupied-authoritative-slots.png')})
 await ownSlots.nth(2).click()
 assert.equal(await page.evaluate(()=>window.__sentCommands.length),0,'occupied non-candidate slot must not submit')
 for(const index of [0,4]){
  await ownSlots.nth(index).click()
  const sent=await page.evaluate(()=>window.__sentCommands.at(-1))
  assert.equal(sent?.type,'gameAction','occupied authoritative slot click must send a game action')
  assert.equal(sent?.command?.type,'resolvePrompt','occupied authoritative slot click must resolve the slot prompt')
  assert.equal(sent?.command?.choice,index===0?'0:0':'1:1','occupied authoritative slot click must preserve the exact server choice')
  await page.evaluate(()=>window.__resetSentCommands())
 }
 await page.setViewportSize({width:480,height:800})
 for(const effect of [
  {mode:'cost2',costs:['消耗1士气','弃置1张手牌']},
  {mode:'cost1',costs:['消耗1士气']},
 ]){
  await page.goto('http://127.0.0.1:'+port+'/__qa__?effect='+effect.mode)
  const panel=page.locator('.prompt-panel')
  await panel.waitFor()
  const effectText=panel.locator('.effect-decision-text')
  assert.equal((await effectText.textContent())?.trim(),'回合1次 我方军团进攻/被进攻时，可消耗1士气或弃置1张手牌：该军团本回合兵力+2000。','Kagutsuchi prompt must display the complete authoritative effect text')
  const textBounds=await effectText.evaluate(element=>{const rect=element.getBoundingClientRect(),panel=element.closest('.prompt-panel').getBoundingClientRect();return {left:rect.left,right:rect.right,top:rect.top,bottom:rect.bottom,panelLeft:panel.left,panelRight:panel.right,panelTop:panel.top,panelBottom:panel.bottom,scrollWidth:element.scrollWidth,clientWidth:element.clientWidth}})
  assert(textBounds.left>=textBounds.panelLeft-1&&textBounds.right<=textBounds.panelRight+1&&textBounds.top>=textBounds.panelTop-1&&textBounds.bottom<=textBounds.panelBottom+1&&textBounds.scrollWidth<=textBounds.clientWidth+1,'complete Kagutsuchi text must wrap inside the narrow prompt without clipping')
  for(const cost of effect.costs)await panel.getByRole('button',{name:cost,exact:true}).waitFor()
  const footer=panel.locator('.prompt-action-footer')
  await footer.getByRole('button',{name:'不发动',exact:true}).waitFor()
  await footer.getByRole('button',{name:'确认选择',exact:true}).waitFor()
  await panel.getByRole('button',{name:effect.costs[0],exact:true}).click()
  assert.equal(await page.evaluate(()=>window.__sentCommands.length),0,'cost selection must wait for explicit confirmation')
  assert.equal(await footer.getByRole('button',{name:'确认选择',exact:true}).isEnabled(),true,'selected cost must enable explicit confirmation')
  await page.screenshot({path:path.join(out,'effect-'+effect.mode+'.png')})
 }
 await page.goto('http://127.0.0.1:'+port+'/__qa__?effect=pure')
 const purePanel=page.locator('.prompt-panel')
 await purePanel.waitFor()
 assert.equal(await purePanel.locator('.prompt-action-footer').count(),0,'true activate/decline prompt must remain the compact immediate decision')
 await purePanel.getByRole('button',{name:'发动',exact:true}).click()
 const pureSent=await page.evaluate(()=>window.__sentCommands.at(-1))
 assert.equal(pureSent?.command?.type,'resolvePrompt','pure activation choice must still submit immediately')
 await page.setViewportSize({width:1280,height:800})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?scout=1')
 const scoutPanel=page.locator('.prompt-panel')
 await scoutPanel.waitFor()
 const scoutCards=scoutPanel.locator('.prompt-card-candidate')
 assert.equal(await scoutCards.count(),2,'generic option prompt must render every displayCardIds preview card')
 const scoutNames=(await scoutCards.locator('.prompt-card-candidate__name').allTextContents()).map(name=>name.trim())
 assert(scoutNames.every(name=>name&&!/^scout-/.test(name)),'scout preview must use full prompt-projected card names instead of instance ids')
 await scoutCards.first().click()
 assert.equal(await scoutCards.locator('.selected').count(),0,'display-only scout cards must not become prompt selections')
 assert.equal(await page.evaluate(()=>window.__sentCommands.length),0,'viewing a scout card must not submit a game command')
 await page.locator('.card-inspector h2').filter({hasText:scoutNames[0]}).waitFor()
 const scoutContinue=scoutPanel.getByRole('button',{name:'已查看，继续',exact:true})
 await scoutContinue.click()
 assert.equal(await page.evaluate(()=>window.__sentCommands.length),0,'generic option selection must retain explicit final confirmation')
 const scoutConfirm=scoutPanel.getByRole('button',{name:'确认选择',exact:true})
 assert.equal(await scoutConfirm.isEnabled(),true,'authoritative confirm choice must enable final submission without selecting a preview card')
 await page.screenshot({path:path.join(out,'scout-display-cards-confirm.png')})
 await scoutConfirm.click()
 const scoutSent=await page.evaluate(()=>window.__sentCommands.at(-1))
 assert.deepEqual(scoutSent?.command?.cardInstanceIds,['confirm'],'scout acknowledgement must submit only the authoritative confirm choice')
 await page.setViewportSize({width:770,height:850})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?picker=1')
 await page.locator('.picker-card.horizontal').first().waitFor()
 await page.locator('.picker-image').first().click()
 await page.locator('.catalog-detail-mask').waitFor({timeout:3000})
 await page.screenshot({path:path.join(out,'sandbox-landscape.png')})
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({errors,reports,logReports},null,2))
 console.log(JSON.stringify({errors,reports,logReports,out},null,2))
 if(errors.length)process.exitCode=1
} finally {await browser?.close();await server.close()}
