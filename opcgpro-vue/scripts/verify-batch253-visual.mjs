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
const entry = `
import {createApp,h} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import GamePage from '/src/l12/GamePage.vue'
import SandboxCardPicker from '/src/l12/game/SandboxCardPicker.vue'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import {rankedApi} from '/src/l12/platform.ts'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
const catalog=await loadDeckCatalog()
const card=(def,id)=>({...def,cardId:def.id,instanceId:id,name:def.nameZh,cardType:def.cardType,cost:def.cost||0,baseTroops:def.troops||0,troops:def.troops||0,tapped:false,disasterLevel:def.disasterLevel||0,summonRound:-1,effectText:def.effect})
const legions=catalog.filter(c=>c.cardType==='legion').slice(0,6)
const masters=catalog.filter(c=>c.cardType==='master').slice(0,2)
const disasters=catalog.filter(c=>c.cardType==='destruction').slice(0,4)
const params=new URLSearchParams(location.search)
const moraleTotal=Math.max(0,Number(params.get('morale')||12))
const moraleActive=Math.max(0,Math.min(moraleTotal,Number(params.get('active')||moraleTotal)))
const special=params.has('special')
const trialMode=params.has('trial')
const slotMode=params.has('slot')
const effectMode=params.get('effect')
const canopicIds=['S01-0216','S01-0217','S01-0218','S01-0219','S01-0220']
const canopicTrack=canopicIds.map((id,j)=>({...card(catalog.find(c=>c.id===id)||legions[0],'canopic-'+j),completed:j<3}))
const trial=card(catalog.find(c=>c.cardType==='trial')||disasters[0],'trial-wide')
const players=[0,1].map(i=>({playerIndex:i,name:i?'对方测试长昵称十二军团':'我方测试昵称',deckName:'合成验收',faction:special&&i===0?'taiyangcheng':'otherworld',master:{masterId:masters[i]?.id||'ST06-M1',masterName:masters[i]?.nameZh||'银臂努阿达',hp:7,maxHp:9},libraryCount:30,hand:legions.map((d,j)=>card(d,i+'hand'+j)),handCount:6,morale:Array.from({length:moraleTotal},(_,j)=>({instanceId:i+'morale'+j,cardId:'ST06-C1',tapped:j>=moraleActive})),field:slotMode&&i===0?[[card(legions[0],'0unit'),card(legions[1],'0occupied-1'),card(legions[2],'0occupied-2')],[card(legions[3],'0occupied-3'),card(legions[4],'0unit-payment'),card(legions[5],'0occupied-5')]]:[[card(legions[0],i+'unit'),null,null],[null,null,null]],graveyard:[card(legions[1],i+'grave')],mulliganDone:true,specialZones:{runes:3,trialLevel:0,godPower:[],trials:trialMode&&i===0?[trial]:[],canopicTrack:special&&i===0?canopicTrack:[]}}))
l12State.game={matchId:'synthetic-batch253',roomCode:'TEST253',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'all',disasterValue:0,players,sessionDisasters:disasters.map((d,j)=>card(d,'disaster'+j)),prompts:[],effectStack:[],stateHash:'synthetic',playerBadges:[{playerIndex:0,rankLabel:'迷雾旅人',masterTitle:'最强银臂努阿达'},{playerIndex:1,rankLabel:'',masterTitle:''}],recentEvents:Array.from({length:20},(_,j)=>({sequence:j+1,type:j%4===0?'turn-start':j%3===0?'prompt-resolved':'attack',playerIndex:j%2,text:j%4===0?'第 '+(j/4+1)+' 回合 · 回合开始':j%3===0?'选择另外1张军团 → 公开军团':'以公开军团进攻，兵力5000 → 3000',cards:[]}))}
if(slotMode)l12State.game.prompts=[{promptId:'occupied-slot-prompt',playerIndex:0,kind:'slot',text:'选择支付后登场位置',validChoices:['0:0','1:1'],minChoose:1,maxChoose:1,data:{choiceMode:'board-slot',targetPlayerIndex:'0'},choiceLabels:{},createdRevision:1,controller:0},{promptId:'declared-cost-prompt',playerIndex:0,kind:'resource-payment',text:'已声明费用',validChoices:['0unit','0morale0'],minChoose:1,maxChoose:1,data:{choiceMode:'resource-payment'},choiceLabels:{},createdRevision:1,controller:0}]
if(effectMode){
 const choices=effectMode==='cost2'?['pay:morale','pay:discard','no']:effectMode==='cost1'?['pay:morale','no']:['yes','no']
 l12State.game.prompts=[{promptId:'effect-cost-prompt',playerIndex:0,kind:'option',text:'迦具土',validChoices:choices,minChoose:1,maxChoose:1,data:{uiPattern:'effect-decision',effectText:'回合1次 我方军团进攻/被进攻时，可消耗1士气或弃置1张手牌：该军团本回合兵力+2000。'},choiceLabels:{'pay:morale':'消耗1士气','pay:discard':'弃置1张手牌',yes:'发动',no:'不发动'},createdRevision:1,controller:0}]
}
window.__sentCommands=[]
l12State.socket={readyState:WebSocket.OPEN,send:payload=>window.__sentCommands.push(JSON.parse(payload))}
window.__resetSentCommands=()=>{window.__sentCommands=[];l12State.pendingAction=false}
window.__setInspectorPrompt=visible=>{l12State.game.prompts=visible?[{promptId:'inspector-prompt',playerIndex:0,kind:'option',text:'合成来源',validChoices:['yes','no'],minChoose:1,maxChoose:1,choiceLabels:{yes:'发动',no:'不发动'},data:{uiPattern:'effect-decision',sourceName:'合成来源',effectText:'登场时 可发动试炼。',sourceInstanceId:'0unit'},sourceInstanceId:'0unit',sourceCardId:legions[0].id,createdRevision:1,controller:0}]:[]}
l12State.status='online'
l12State.room={roomCode:'TEST253',yourPlayerIndex:0,players:players.map(p=>({...p,connected:true,ready:true,deckIndex:0})),decks:[],started:true}
const isPicker=new URLSearchParams(location.search).has('picker')
const isRanking=new URLSearchParams(location.search).has('ranking')
const masterRows=masters.map((m,i)=>({rank:100+i,masterId:m.id,masterName:m.nameZh,games:999,wins:999,losses:0,winRate:100,usageRate:50,firstWinRate:100,secondWinRate:100,firstWins:500,firstGames:500,secondWins:499,secondGames:499,strongestPlayer:'合成测试玩家',title:'最强'+m.nameZh}))
rankedApi.leaderboard=async()=>({players:Array.from({length:4},(_,i)=>({rank:i+1,username:'合成测试长昵称'+i,faction:'命运',tier:'迷雾旅人',titles:['最强银臂努阿达','最强雷神索尔'],favoriteMasterId:masters[0].id,favoriteMasterName:masters[0].nameZh,displayValue:'七曜值 21,945',wins:999,losses:888})),analytics:{range:'season',summary:{matches:999,placedPlayers:4,activeMasters:2},masters:masterRows,matchups:masterRows.flatMap(a=>masterRows.map(b=>({masterId:a.masterId,opponentMasterId:b.masterId,games:999,wins:999,winRate:100,firstWins:500,firstGames:500,secondWins:499,secondGames:499})))}})
rankedApi.history=async()=>[]
const app=createApp({render:()=>isPicker?h(SandboxCardPicker,{title:'GM横卡验收',allowedTypes:['destruction']}):isRanking?h(RankingsPage):h(GamePage)})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
let browser
const server = await createServer({root,server:{host:'127.0.0.1',port:0,strictPort:false},plugins:[{name:'batch253-synthetic',resolveId(id){if(id==='/__qa__.js')return id},load(id){if(id==='/__qa__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{
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
  await page.waitForTimeout(500)
  await page.screenshot({path:path.join(out,`battle-${width}x${height}.png`)})
  reports.push(await page.evaluate(()=>{
   const box=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,scroll:e.scrollHeight,client:e.clientHeight}}
   return {width:innerWidth,height:innerHeight,summary:box('.player-panel'),rail:box('.right-rail'),log:box('.event-list'),dock:box('.battle-utility-dock'),clock:box('.my-status-lane'),hand:box('.board-center>.l12-hand:last-child'),phase:box('.l12-phase-track'),seam:box('.board-seam'),font:getComputedStyle(document.querySelector('.event-message')).fontSize}
  }))
 }
 for(const report of reports){
 if(report.summary.scroll>report.summary.client+1)throw new Error('Player summary clipped at '+report.width)
 if(report.clock.y+report.clock.h>report.hand.y+1)throw new Error('Clock overlaps hand at '+report.width)
 if(report.hand.y+report.hand.h>report.height+1)throw new Error('Hand leaves viewport at '+report.width+'x'+report.height)
 if(report.dock.y+report.dock.h>report.height+1)throw new Error('Utility dock leaves viewport at '+report.width+'x'+report.height)
 }
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
  {name:'morale-12',query:'morale=12&active=12',expected:12,label:'12/12'},
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
  if(scenario.name==='morale-overflow-special'){
   assert.equal(result.canopics.length,5,'five Canopic markers must be present')
   assert(new Set(result.canopics.map(item=>Math.round(item.top))).size===1,'five Canopic markers must stay on one row')
  }
  if(scenario.name==='morale-trial'){
   assert(!(result.trial.left<result.relic.right&&result.trial.right>result.relic.left&&result.trial.top<result.relic.bottom&&result.trial.bottom>result.relic.top),'trial and relic zones must not overlap')
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
 await page.setViewportSize({width:770,height:850})
 await page.goto('http://127.0.0.1:'+port+'/__qa__?picker=1')
 await page.locator('.picker-card.horizontal').first().waitFor()
 await page.locator('.picker-image').first().click()
 await page.locator('.catalog-detail-mask').waitFor({timeout:3000})
 await page.screenshot({path:path.join(out,'sandbox-landscape.png')})
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({errors,reports},null,2))
 console.log(JSON.stringify({errors,reports,out},null,2))
 if(errors.length)process.exitCode=1
} finally {await browser?.close();await server.close()}
