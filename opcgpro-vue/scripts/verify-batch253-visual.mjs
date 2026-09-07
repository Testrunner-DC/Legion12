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
const canopicIds=['S01-0216','S01-0217','S01-0218','S01-0219','S01-0220']
const canopicTrack=canopicIds.map((id,j)=>({...card(catalog.find(c=>c.id===id)||legions[0],'canopic-'+j),completed:j<3}))
const trial=card(catalog.find(c=>c.cardType==='trial')||disasters[0],'trial-wide')
const players=[0,1].map(i=>({playerIndex:i,name:i?'对方测试长昵称十二军团':'我方测试昵称',deckName:'合成验收',faction:special&&i===0?'taiyangcheng':'otherworld',master:{masterId:masters[i]?.id||'ST06-M1',masterName:masters[i]?.nameZh||'银臂努阿达',hp:7,maxHp:9},libraryCount:30,hand:legions.map((d,j)=>card(d,i+'hand'+j)),handCount:6,morale:Array.from({length:moraleTotal},(_,j)=>({instanceId:i+'morale'+j,cardId:'ST06-C1',tapped:j>=moraleActive})),field:[[card(legions[0],i+'unit'),null,null],[null,null,null]],graveyard:[card(legions[1],i+'grave')],mulliganDone:true,specialZones:{runes:3,trialLevel:0,godPower:[],trials:trialMode&&i===0?[trial]:[],canopicTrack:special&&i===0?canopicTrack:[]}}))
l12State.game={matchId:'synthetic-batch253',roomCode:'TEST253',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'all',disasterValue:0,players,sessionDisasters:disasters.map((d,j)=>card(d,'disaster'+j)),prompts:[],effectStack:[],stateHash:'synthetic',playerBadges:[{playerIndex:0,rankLabel:'迷雾旅人',masterTitle:'最强银臂努阿达'},{playerIndex:1,rankLabel:'',masterTitle:''}],recentEvents:Array.from({length:20},(_,j)=>({sequence:j+1,type:j%4===0?'turn-start':j%3===0?'prompt-resolved':'attack',playerIndex:j%2,text:j%4===0?'第 '+(j/4+1)+' 回合 · 回合开始':j%3===0?'选择另外1张军团 → 公开军团':'以公开军团进攻，兵力5000 → 3000',cards:[]}))}
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
  {name:'morale-low',query:'morale=2&active=2',expected:2,label:'2/2'},
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
  assert(rows.every(row=>Math.abs((row[0].left+row.at(-1).right)/2-result.stack.cx)<1.5),scenario.name+' every morale row must be horizontally centered')
  if(scenario.name==='morale-overflow-special'){
   assert.equal(result.canopics.length,5,'five Canopic markers must be present')
   assert(new Set(result.canopics.map(item=>Math.round(item.top))).size===1,'five Canopic markers must stay on one row')
  }
  if(scenario.name==='morale-trial'){
   assert(!(result.trial.left<result.relic.right&&result.trial.right>result.relic.left&&result.trial.top<result.relic.bottom&&result.trial.bottom>result.relic.top),'trial and relic zones must not overlap')
  }
  await page.screenshot({path:path.join(out,scenario.name+'.png')})
 }
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
