import path from 'node:path'
import fs from 'node:fs'
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
const players=[0,1].map(i=>({playerIndex:i,name:i?'对方测试长昵称十二军团':'我方测试昵称',deckName:'合成验收',faction:'otherworld',master:{masterId:masters[i]?.id||'ST06-M1',masterName:masters[i]?.nameZh||'银臂努阿达',hp:7,maxHp:9},libraryCount:30,hand:legions.map((d,j)=>card(d,i+'hand'+j)),handCount:6,morale:Array.from({length:12},(_,j)=>({instanceId:i+'morale'+j,cardId:'ST06-C1',tapped:j%3===0})),field:[[card(legions[0],i+'unit'),null,null],[null,null,null]],graveyard:[card(legions[1],i+'grave')],mulliganDone:true,specialZones:{runes:3,trialLevel:0,godPower:[],trials:[]}}))
l12State.game={matchId:'synthetic-batch253',roomCode:'TEST253',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'all',disasterValue:0,players,sessionDisasters:disasters.map((d,j)=>card(d,'disaster'+j)),prompts:[],effectStack:[],stateHash:'synthetic',playerBadges:[{playerIndex:0,rankLabel:'迷雾旅人',masterTitle:'最强银臂努阿达'},{playerIndex:1,rankLabel:'',masterTitle:''}],recentEvents:Array.from({length:20},(_,j)=>({sequence:j+1,type:j%4===0?'turn-start':j%3===0?'prompt-resolved':'attack',playerIndex:j%2,text:j%4===0?'第 '+(j/4+1)+' 回合 · 回合开始':j%3===0?'选择另外1张军团 → 公开军团':'以公开军团进攻，兵力5000 → 3000',cards:[]}))}
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
 for(const [width,height] of [[1920,1080],[1440,1000],[1280,800]]){
  await page.setViewportSize({width,height})
  await page.goto('http://127.0.0.1:'+port+'/__qa__')
  await page.locator('.player-summary').first().waitFor()
  await page.waitForTimeout(500)
  await page.screenshot({path:path.join(out,'battle-'+width+'.png')})
  reports.push(await page.evaluate(()=>{
   const box=s=>{const e=document.querySelector(s);if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height,scroll:e.scrollHeight,client:e.clientHeight}}
   return {width:innerWidth,summary:box('.player-panel'),rail:box('.right-rail'),log:box('.event-list'),dock:box('.battle-utility-dock'),clock:box('.my-status-lane'),hand:box('.board-center>.l12-hand:last-child'),phase:box('.l12-phase-track'),seam:box('.board-seam'),font:getComputedStyle(document.querySelector('.event-message')).fontSize}
  }))
 }
 for(const report of reports){
  if(report.summary.scroll>report.summary.client+1)throw new Error('Player summary clipped at '+report.width)
  if(report.clock.y+report.clock.h>report.hand.y+1)throw new Error('Clock overlaps hand at '+report.width)
  if(report.phase.y<report.seam.y-1||report.phase.y+report.phase.h>report.seam.y+report.seam.h+1)throw new Error('Phase escapes seam at '+report.width)
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
 if(cellHeights.some(height=>Math.abs(height-62)>1))throw new Error('Matrix row height changed')
 await page.screenshot({path:path.join(out,'rankings-matrix.png')})
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
