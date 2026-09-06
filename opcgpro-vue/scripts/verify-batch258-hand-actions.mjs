import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch258-hand-actions'
fs.mkdirSync(out, { recursive: true })
// Reuse the already-sanitized board setup, never a real player's private hand.
const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = previous.slice(previous.indexOf('const entry = `') + 'const entry = `'.length, previous.indexOf('const isPicker='))
const entry = setup + `
import {audioPreferences,syncAudioStore} from '/src/l12/audioPreferences.ts'
const params = new URLSearchParams(location.search)
audioPreferences.cardSize=params.get('size')||'large';audioPreferences.animation='off';syncAudioStore()
const playable=catalog.find(c=>c.cardType===params.get('type'))
if(!playable)throw new Error('Missing card type')
const count=Number(params.get('count')||12)
players[0].hand=Array.from({length:count},(_,j)=>({...card(playable,'qa-hand-'+j),cost:0,playCost:0,minimumPlayCost:0,playBlockedReason:null}))
players[0].handCount=count
if(params.get('gm')==='true'){
 l12State.gmEnabled=true;l12State.game.activePlayer=1
 players[1].hand=players[0].hand.map((c,j)=>({...c,instanceId:'qa-opponent-'+j}));players[1].handCount=count
}
if(params.get('timed')==='true'){
 const acting=l12State.game.activePlayer
 const now=Date.now()
 l12State.rankedClock={serverUtcMs:now,receivedAtMs:now,totalLimitMs:600000,operationLimitMs:90000,reconnectLimitMs:60000,players:[0,1].map(playerIndex=>({
  playerIndex,totalRemainingMs:playerIndex===0?487000:463000,operationRemainingMs:playerIndex===0?78000:71000,
  acting:playerIndex===acting,connected:playerIndex===acting,reconnectRemainingMs:playerIndex===acting?null:43000
 }))}
}
const app=createApp({render:()=>h(GamePage)})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
let browser
const server = await createServer({root,server:{host:'127.0.0.1',port:0},plugins:[{name:'batch258-hand-actions',resolveId(id){if(id==='/__qa__.js')return id},load(id){if(id==='/__qa__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{
 if(req.url?.match(/^\/__qa__(\?|$)/)){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__qa__.js"></script>');return}next()
})}}]})
try {
 await server.listen()
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage()
 const errors=[];const reports=[]
 page.on('pageerror',e=>errors.push(e.message))
 await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
 for(const [width,height,count,size,type,gm=false,timed=false] of [[1920,1080,12,'large','artifact'],[1280,800,12,'large','tactic',false,true],[1440,1000,40,'large','artifact'],[1280,800,40,'large','artifact',false,true],[1280,800,6,'small','artifact'],[1920,1080,12,'large','artifact',true],[1280,800,40,'large','artifact',true,true]]) {
  await page.setViewportSize({width,height})
  await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__qa__?count='+count+'&size='+size+'&type='+type+'&gm='+gm+'&timed='+timed)
  const hand=page.locator(gm?'.board-center>.opponent-hand':'.board-center>.l12-hand:last-child')
  await hand.locator('.hand-card-wrap').last().waitFor()
  await hand.locator('.hand-card-wrap').last().locator('.card-tile').click()
  await hand.locator('.hand-actions button').waitFor()
  await page.screenshot({path:path.join(out,[width,count,size,type,gm,timed].join('-')+'.png')})
  const report=await page.evaluate(gm=>{
   const button=document.querySelector((gm?'.board-center>.opponent-hand':'.board-center>.l12-hand:last-child')+' .hand-actions button')
   const clockElement=document.querySelector((gm?'.opponent-status-lane':'.my-status-lane')+' .board-player-clock')
   if(!(button instanceof HTMLElement)||!(clockElement instanceof HTMLElement))throw new Error('Hand action or player clock missing')
   const rect=button.getBoundingClientRect();const clock=clockElement.getBoundingClientRect()
   const field=document.querySelector('.felt-board').getBoundingClientRect()
   const fieldBottom=Math.max(...[...document.querySelectorAll('.felt-board .formation-slot')].map(e=>e.getBoundingClientRect().bottom))
   const hit=document.elementFromPoint(rect.x+rect.width/2,rect.y+rect.height/2)
   const visibleAtCenter=element=>{if(!(element instanceof HTMLElement))return false;const box=element.getBoundingClientRect();const top=document.elementFromPoint(box.x+box.width/2,box.y+box.height/2);return top===element||element.contains(top)}
   const visibleWithoutGmOverlay=element=>{if(!(element instanceof HTMLElement))return false;const box=element.getBoundingClientRect();const gmPanel=document.querySelector('.gm-panel')?.getBoundingClientRect();return box.left>=0&&box.top>=0&&box.right<=innerWidth&&box.bottom<=innerHeight&&(!gmPanel||box.right<=gmPanel.left||box.left>=gmPanel.right||box.bottom<=gmPanel.top||box.top>=gmPanel.bottom)}
   const playerPanel=document.querySelector('.right-rail .player-panel')
   const endTurn=[...document.querySelectorAll('.right-rail button')].find(element=>element.textContent?.trim()==='结束回合')
   const gmVisibility={clock:visibleWithoutGmOverlay(clockElement),player:visibleAtCenter(playerPanel),endTurn:visibleAtCenter(endTurn)}
   return {button:{x:rect.x,y:rect.y,w:rect.width,h:rect.height},clock:{x:clock.x,y:clock.y,w:clock.width,h:clock.height},clockText:clockElement.textContent,allClockText:[...document.querySelectorAll('.board-player-clock')].map(e=>e.textContent).join(' '),rankedClockPlayers:document.querySelectorAll('.board-player-clock:not(.untimed-clock)').length,gmVisibility,gmCriticalVisible:Object.values(gmVisibility).every(Boolean),fieldContained:fieldBottom<=field.bottom+1,hit:button===hit||button.contains(hit),overlap:rect.left<clock.right&&rect.right>clock.left&&rect.top<clock.bottom&&rect.bottom>clock.top}
  },gm)
  reports.push({width,height,count,size,type,gm,timed,...report})
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({errors,reports},null,2))
 console.log(JSON.stringify({errors,reports},null,2))
 if(errors.length||reports.some(r=>r.overlap||!r.hit||!r.fieldContained)
   ||reports.filter(r=>r.gm).some(r=>!r.gmCriticalVisible)
   ||reports.filter(r=>r.timed).some(r=>r.rankedClockPlayers!==2||!r.clockText.includes('总时')||!r.clockText.includes('本次')||!r.allClockText.includes('重连')))
  throw new Error('Hand action overlaps clock, field is compressed, timed clocks are incomplete, or button cannot receive pointer input')
} finally {await browser?.close();await server.close()}
