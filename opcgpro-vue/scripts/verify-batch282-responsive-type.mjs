import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch282-responsive-type'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(out, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import GamePage from '/src/l12/GamePage.vue'
import L12DeckEditor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
const catalog=await loadDeckCatalog()
const definition=id=>catalog.find(card=>card.id===id)||catalog.find(card=>card.cardType==='legion')
const card=(def,id)=>({...def,cardId:def.id,instanceId:id,name:def.nameZh,effectText:def.effect,baseTroops:def.troops||0,troops:def.troops||0,tapped:false,summonRound:-1,disasterLevel:def.disasterLevel||0})
const legions=catalog.filter(item=>item.cardType==='legion').slice(0,6)
const masters=catalog.filter(item=>item.cardType==='master').slice(0,2)
const disasters=catalog.filter(item=>item.cardType==='destruction').slice(0,4)
const players=[0,1].map(index=>({playerIndex:index,name:index?'对方长昵称验收玩家':'我方长昵称验收玩家',deckName:'响应式排版验收',faction:index?'tianting':'otherworld',master:{masterId:masters[index]?.id||'ST06-M1',masterName:masters[index]?.nameZh||'银臂努阿达',hp:8,maxHp:9},libraryCount:30,hand:legions.map((item,i)=>card(item,index+'-hand-'+i)),handCount:6,temporaryMorale:0,morale:[{instanceId:index+'-normal',cardId:'ST06-C1',tapped:false}],field:[[card(legions[0],index+'-unit'),null,null],[null,null,null]],graveyard:[card(legions[1],index+'-grave')],mulliganDone:true,specialZones:{runes:0,trialLevel:0,godPower:[],trials:[]}}))
l12State.game={matchId:'batch282-responsive',roomCode:'TYPE82',you:0,revision:1,activePlayer:0,firstPlayer:0,diceWinner:0,initiativeRolls:[6,3],phase:'Main',round:3,turnSerial:5,disasterMode:'all',disasterValue:4,players,sessionDisasters:disasters.map((item,i)=>card(item,'disaster-'+i)),prompts:[],effectStack:[],stateHash:'batch282',recentEvents:Array.from({length:16},(_,i)=>({sequence:i+1,type:i%4===0?'turn-start':'attack',playerIndex:i%2,text:i%4===0?'第 '+(i/4+1)+' 回合 · 回合开始':'公开军团发动进攻，兵力5000 → 3000',cards:[]}))}
l12State.status='online';l12State.spectating=false;l12State.gmEnabled=false
l12State.room={roomCode:'TYPE82',yourPlayerIndex:0,players:players.map(player=>({...player,connected:true,ready:true,deckIndex:0})),decks:[],started:true}
const deckMode=new URLSearchParams(location.search).has('deck')
const rootComponent={render:()=>h(deckMode?L12DeckEditor:GamePage)}
const app=createApp(rootComponent)
app.use(createRouter({history:createMemoryHistory(),routes:[{path:'/',component:rootComponent},{path:'/decks',component:rootComponent}]}))
app.mount('#app')
`

let browser
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0, strictPort: false }, plugins: [{
  name: 'batch282-responsive-fixture',
  resolveId(id) { if (id === '/__batch282__.js') return id },
  load(id) { if (id === '/__batch282__.js') return entry },
  configureServer(devServer) { devServer.middlewares.use((request, response, next) => {
    if (request.url?.match(/^\/__batch282__(\?|$)/)) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__batch282__.js"></script>')
      return
    }
    next()
  }) },
}] })

const viewports = [
  { width: 1920, height: 1080 },
  { width: 1440, height: 810 },
  { width: 1280, height: 720 },
  { width: 760, height: 900 },
  { width: 390, height: 844 },
]
const reports = []
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__batch282__`)
    await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().waitFor()
    await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().hover()
    await page.locator('.inspector-effect').waitFor()
    await page.waitForTimeout(350)
    const battle = await page.evaluate(() => {
      const box = selector => { const element = document.querySelector(selector); if (!element) return null; const rect = element.getBoundingClientRect(); return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height, clientWidth: element.clientWidth, scrollWidth: element.scrollWidth } }
      const effect = document.querySelector('.inspector-effect')
      const tags = [...document.querySelectorAll('.inspector-card-tags span')].map(tag => ({ text: tag.textContent?.trim(), ...box('.inspector-card-tags') }))
      return { effect: box('.inspector-effect'), tags, bodyScrollWidth: document.documentElement.scrollWidth, copySize: getComputedStyle(document.querySelector('.event-message')).fontSize, effectSize: getComputedStyle(effect).fontSize }
    })
    assert(battle.effect, `selected-card effect missing at ${viewport.width}x${viewport.height}`)
    console.log(JSON.stringify({ viewport, battle }))
    assert(battle.effect.scrollWidth <= battle.effect.clientWidth + 1, `selected-card effect prose must wrap without horizontal overflow at ${viewport.width}x${viewport.height}`)
    assert(battle.tags.length >= 2, `selected-card metadata tags must remain visible at ${viewport.width}x${viewport.height}`)
    assert(battle.bodyScrollWidth <= viewport.width + 1, `battle must not create document overflow at ${viewport.width}x${viewport.height}`)
    await page.screenshot({ path: path.join(out, `battle-${viewport.width}x${viewport.height}.png`) })

    await page.goto(`http://127.0.0.1:${port}/__batch282__?deck=1`)
    await page.locator('.builder-card-detail').waitFor()
    const deck = await page.evaluate(() => {
      const rect = selector => { const element = document.querySelector(selector); const value = element.getBoundingClientRect(); return { left: value.left, right: value.right, top: value.top, bottom: value.bottom, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth } }
      return { topbar: rect('.deck-builder-topbar'), grid: rect('.deck-builder-grid'), shell: rect('.deck-builder-shell'), bodyScrollWidth: document.documentElement.scrollWidth, effectWrap: getComputedStyle(document.querySelector('.builder-card-detail p')).whiteSpace }
    })
    assert(deck.grid.top >= deck.topbar.bottom - 1, `deck content must start below the topbar at ${viewport.width}x${viewport.height}`)
    assert(deck.shell.scrollWidth <= deck.shell.clientWidth + 1, `deck builder must contain horizontal overflow at ${viewport.width}x${viewport.height}`)
    assert(deck.bodyScrollWidth <= viewport.width + 1, `deck builder must not create document overflow at ${viewport.width}x${viewport.height}`)
    assert.equal(deck.effectWrap, 'pre-line', 'deck effect prose must preserve authoritative line breaks')
    await page.screenshot({ path: path.join(out, `deck-${viewport.width}x${viewport.height}.png`), fullPage: false })
    reports.push({ ...viewport, battle, deck })
  }
  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ reports, errors }, null, 2))
  console.log(JSON.stringify({ viewports, out, errors }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
