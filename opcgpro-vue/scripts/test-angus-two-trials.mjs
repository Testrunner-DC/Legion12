import assert from 'node:assert/strict'
import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = path.resolve(root, '../artifacts/angus-two-trials')
fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp,h,reactive} from 'vue';import PlayerMat from '/src/l12/game/PlayerMat.vue';import '/src/style.css';
const trial=(id,name,progress,done)=>({instanceId:id,cardId:id==='first'?'S02-06S6':'ST06-S1',name,cardType:'trial',tapped:false,summonRound:0,trialProgress:progress,trialCompleted:done,abilities:[{id:'completeTrial',label:'完成试炼',enabled:progress===8&&!done}]});
const p=reactive({playerIndex:0,name:'合成测试',deckName:'双试炼',faction:'otherworld',master:{masterId:'S02-06M2',masterName:'安格斯·麦·奥格',hp:8,maxHp:8},libraryCount:30,hand:[],handCount:0,morale:[],field:[[null,null,null],[null,null,null]],graveyard:[],mulliganDone:true,specialZones:{runes:2,trialLevel:0,trialCapacity:2,godPower:[],trials:[trial('first','十字军东征',8,true),trial('second','天空之城',0,false)]}});
window.qaAdvance=()=>p.specialZones.trials=[p.specialZones.trials[0],trial('second','天空之城',8,false)];window.qaActions=[];
createApp({render:()=>h(PlayerMat,{player:p,side:'my',controllable:true,actionsEnabled:true,round:2,active:true,onAbility:(card,id)=>window.qaActions.push({instanceId:card.instanceId,id})})}).mount('#app');`
const server = await createServer({ root, server: { host:'127.0.0.1', port:0 }, plugins:[{name:'angus-fixture',resolveId(id){if(id==='/__angus__.js')return id},load(id){if(id==='/__angus__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url==='/__angus__'){res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><style>#app{height:600px;max-width:1200px;margin:20px auto}</style><div id="app"></div><script type="module" src="/__angus__.js"></script>');return}next()})}}] })
let browser
try {
  await server.listen()
  browser = await chromium.launch({channel:'msedge',headless:true})
  const page = await browser.newPage({viewport:{width:1280,height:800}})
  const errors=[];page.on('pageerror',e=>errors.push(e.message))
  await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__angus__`)
  const second=page.locator('.trial-card').nth(1)
  await second.click()
  const action=page.getByRole('button',{name:'完成试炼',exact:true})
  assert.equal(await action.isDisabled(),true)
  await page.evaluate(()=>window.qaAdvance())
  await page.waitForFunction(()=>!document.querySelector('.card-ability-list button:disabled') && window.qaActions.length===0).catch(()=>{})
  assert.equal(await action.isEnabled(),true,'open second-trial dialog refreshes with authoritative snapshot')
  await page.screenshot({path:path.join(out,'second-trial.png'),fullPage:true})
  await action.click()
  assert.deepEqual(await page.evaluate(()=>window.qaActions),[{instanceId:'second',id:'completeTrial'}])
  assert.deepEqual(errors,[])
  console.log('Angus two-trial UI passed: second card clickable, dynamic snapshot refresh, exact instance submission.')
} finally { await browser?.close(); await server.close() }
