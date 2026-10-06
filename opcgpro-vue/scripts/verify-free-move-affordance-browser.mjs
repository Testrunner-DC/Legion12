import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/card-effects-intake-20261007/browser'
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })
const entry = `
import {createApp,h,ref} from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import {l12State} from '/src/l12/net.ts'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
const params=new URLSearchParams(location.search),actor=Number(params.get('actor')||0),scenario=params.get('scenario')||'tenka'
const card=(id,instance,name,type='legion')=>({cardId:id,instanceId:instance,name,cardType:type,faction:'takamagahara',cost:2,baseTroops:4000,troops:4000,tapped:false,hidden:false,summonRound:-1,effectText:'合成界面验收',abilities:[],activeKeywords:[]})
const players=[0,1].map(i=>({playerIndex:i,name:'合成玩家'+i,deckName:'合成构筑',faction:'takamagahara',master:{masterId:'S01-04M1',masterName:'天照',hp:8,maxHp:8},libraryCount:20,hand:[],handCount:0,morale:[],spendableResourceCount:0,field:[[null,null,null],[null,null,null]],graveyard:[],mulliganDone:true,specialZones:{runes:0,trialLevel:0,godPower:[],trials:[]}}))
const mover=card('S02-0402','mover','免费移动合成军团')
mover.ruleActions=scenario==='consumed'?[]:[{id:'freeMove',label:'免费位移',text:'合成权威免费移动',enabled:true,targetKeys:scenario==='hippolyta'?['1:0']:['0:1','1:0']}]
players[actor].field[0][0]=mover
if(scenario==='sunwu') {players[actor].hand=[{...card('S01-0016','free-defense','绝对防御','tactic'),faction:'universal',isCounterTactic:true,playCost:0,minimumPlayCost:0}];players[actor].handCount=1}
l12State.game={matchId:'synthetic-free-move-ui',roomCode:'LOCAL',you:actor,revision:1,activePlayer:actor,firstPlayer:actor,diceWinner:actor,initiativeRolls:[6,3],phase:'Main',round:2,turnSerial:3,disasterMode:'none',disasterValue:0,players,prompts:[],effectStack:[],recentEvents:[],legalAttackTargets:{},stateHash:'synthetic'}
l12State.room={roomCode:'LOCAL',yourPlayerIndex:actor,players:players.map(p=>({...p,connected:true,ready:true,deckIndex:0})),decks:[],started:true}
l12State.status='online';l12State.pendingAction=false;l12State.spectating=false;l12State.gmEnabled=false
window.sent=[]
l12State.socket={readyState:WebSocket.OPEN,send:payload=>window.sent.push(JSON.parse(payload))}
window.consumeGrant=()=>{mover.ruleActions=[];l12State.game.players[actor].field[0][0].ruleActions=[];l12State.pendingAction=false;l12State.game.revision++}
createApp({setup(){useLandscapeViewport(ref(true))},render:()=>h(GameBoard,{game:l12State.game})}).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner',
  cacheDir: path.join(output, 'vite-cache'), server: { host: '127.0.0.1', port: 0 },
  plugins: [{ name: 'free-movement-real-components',
    resolveId(id) { if (id === '/__free_move__.js') return id },
    load(id) { if (id === '/__free_move__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (request.url?.startsWith('/__free_move__') && !request.url.includes('.js')) {
        response.setHeader('Content-Type', 'text/html')
        response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__free_move__.js"></script>')
        return
      }
      next()
    }) },
  }],
})
let browser
const results = []
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1920, height: 1080 }, { width: 844, height: 390 }, { width: 390, height: 844 }])
  for (const actor of [0, 1])
  for (const scenario of ['tenka', 'hippolyta', 'consumed', 'sunwu']) {
    console.log('Browser case ' + viewport.width + 'x' + viewport.height + ' actor ' + actor + ' ' + scenario)
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', e => errors.push(e.message))
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__free_move__?actor=' + actor + '&scenario=' + scenario)
    await page.locator('.my-half [data-card-instance-id="mover"]').waitFor()
    if (scenario === 'sunwu') {
      await page.locator('.l12-hand [data-card-instance-id="free-defense"]').click()
      await page.getByRole('button', { name: '打出', exact: true }).click()
      const slots = page.locator('.my-half .formation-slot.available')
      assert.equal(await slots.count(), 3, 'free counter must have three legal back-row positions')
      await slots.first().click()
      const sent = await page.evaluate(() => window.sent)
      assert.equal(sent.length, 1)
      assert.equal(sent[0].command.type, 'playCard')
      assert.equal(sent[0].command.cardInstanceId, 'free-defense')
      assert.equal(sent[0].command.row, 1)
    } else {
      await page.locator('.my-half .formation-slot').filter({ has: page.locator('[data-card-instance-id="mover"]') }).click()
      const free = page.getByRole('button', { name: '免费位移', exact: true })
      assert.equal(await free.count(), scenario === 'consumed' ? 0 : 1)
      assert.equal(await page.getByRole('button', { name: '移动', exact: true }).count(), 0)
      if (scenario !== 'consumed') {
        await free.click()
        const slots = page.locator('.my-half .formation-slot.available')
        assert.equal(await slots.count(), scenario === 'tenka' ? 2 : 1)
        await slots.first().click()
        const sent = await page.evaluate(() => window.sent)
        assert.equal(sent.length, 1)
        assert.equal(sent[0].command.type, 'move')
        assert.equal(sent[0].command.cardInstanceId, 'mover')
        assert.equal(sent[0].command.row, scenario === 'tenka' ? 0 : 1)
        assert.equal(sent[0].command.slot, scenario === 'tenka' ? 1 : 0)
        await page.evaluate(() => window.consumeGrant())
        await page.locator('.my-half .formation-slot').filter({ has: page.locator('[data-card-instance-id="mover"]') }).click()
        assert.equal(await free.count(), 0, 'consumed grant must not leave a second clickable free move')
      }
    }
    assert.deepEqual(errors, [])
    if (actor === 0 && scenario === 'tenka') await page.screenshot({ path: path.join(output, viewport.width + 'x' + viewport.height + '.png') })
    results.push({ viewport, actor, scenario, passed: true })
    await page.close()
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ passed: true, results,
    scope: 'Real GameBoard, PlayerMat and HandArea with synthetic authority snapshots and captured outgoing commands; not a live server match' }, null, 2))
  console.log('Free movement and zero-cost counter real-component browser cases: ' + results.length + '/' + results.length)
} finally {
  await browser?.close()
  await server.close()
}
