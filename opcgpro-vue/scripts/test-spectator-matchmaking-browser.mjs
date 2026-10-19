import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT
if (!out || !process.env.L12_VITE_CACHE_DIR) throw new Error('EXPLICIT_D_E_OUTPUT_AND_CACHE_REQUIRED')
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const fixtureSource = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const marker = 'const entry = `'
const begin = fixtureSource.indexOf(marker) + marker.length
const end = fixtureSource.indexOf('const isPicker=', begin)
assert.ok(begin >= marker.length && end > begin)
const entry = fixtureSource.slice(begin, end) + `
import {nextTick,ref} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
import {loadOfficialPresetDecks,validateDeck} from '/src/l12/decks.ts'
const presets=await loadOfficialPresetDecks()
const preset=presets.find(deck=>!validateDeck(deck,catalog,[]))
if(!preset)throw new Error('Real catalog-valid preset required')
const body={...preset,id:'qa-observer-deck',revision:1,name:'合成观战牌库',updatedAt:'2026-10-17T00:00:00Z'}
const requests=[]
const policy={version:1,maintenance:{entryBlocked:false},season:{name:'合成赛季'},defaultRoomConfig:{spectating:'public',handVisibility:'request',disasterMode:'all'},cardRestrictions:[],defaultPresetDeckIds:[],announcements:[]}
const nativeFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=new URL(typeof input==='string'?input:input.url,location.href)
 if(!url.pathname.startsWith('/api/'))return nativeFetch(input,init)
 requests.push({path:url.pathname,method:init?.method||'GET'})
 let result={}
 if(url.pathname==='/api/operations/effective-policy')result=policy
 else if(url.pathname==='/api/decks/summaries')result={items:[{id:body.id,revision:1,name:body.name,masterId:body.masterId,updatedAt:body.updatedAt,publicationId:null,publicationVersion:null,counts:{main:40,uncountedMain:0,morale:8,special:0,bench:0},legal:true,legalityReason:null}],total:1,page:1,pageSize:30,generation:1,permissionVersion:1,catalogVersion:'A'.repeat(64),policyVersion:1,facets:{masters:[{masterId:body.masterId,count:1}],legal:1,illegal:0}}
 else if(url.pathname==='/api/decks/by-id/'+body.id)result=body
 return new Response(JSON.stringify(result),{status:200,headers:{'Content-Type':'application/json'}})
}
rankedApi.overview=async()=>({config:{factions:[],timeControl:{totalTimeSeconds:900,operationTimeSeconds:60,reconnectGraceSeconds:60,disasterDecisionSeconds:30,mulliganDecisionSeconds:30}},profile:{faction:'order',tier:'定级',titles:[],displayValue:'合成'},factionTotals:{}})
platformState.account={id:'qa-observer',username:'合成观战者',role:'player'}
platformState.token='synthetic-local-only'
await nextTick()
localStorage.setItem('l12-custom-decks-v1:qa-observer',JSON.stringify({[body.name]:body}))
for(const mode of ['ranked','casual'])localStorage.setItem('l12-selected-custom-deck:qa-observer:'+mode,body.id)
l12State.spectating=true;l12State.observerView=params.get('role')==='referee'?'referee':'public';l12State.gmEnabled=false
l12State.room.yourPlayerIndex=null
l12State.room.tournamentId=params.get('role')==='tournament'?'qa-tournament':undefined
l12State.game.you=0;l12State.game.recentEvents=[];l12State.game.players.forEach(p=>p.hand=[])
l12State.operationsPolicy=policy
l12State.recoveryPhase='snapshot-acknowledged'
l12State.socket.send=payload=>{
 const message=JSON.parse(payload);window.__sentCommands.push(message)
 if(message.type==='joinMatchmaking')l12State.matchmaking={queued:true,mode:message.mode}
 if(message.type==='cancelMatchmaking')l12State.matchmaking=null
}
const observedGame=l12State.game
window.__qa={requests,body,observation:()=>({matchId:l12State.game?.matchId,spectating:l12State.spectating,queued:!!l12State.matchmaking?.queued}),pair:async()=>{
 l12State.matchmaking=null;l12State.matchFound={roomCode:'OWN111',matchId:'own-new',mode:'casual'};l12State.game=null;l12State.spectating=false
 await nextTick()
 l12State.room={...l12State.room,roomCode:'OWN111',yourPlayerIndex:0}
 l12State.game={...observedGame,matchId:'own-new',roomCode:'OWN111',you:0};l12State.matchFound=null
}}
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h(GamePage)}})
app.use(createRouter({history:createMemoryHistory(),routes:[{path:'/game',component:GamePage}]}));app.mount('#app')
`
const html = '<meta name="viewport" content="width=device-width,initial-scale=1"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__spectator_matchmaking.js"></script>'
const server = await createServer({ root, configLoader: 'runner', cacheDir: process.env.L12_VITE_CACHE_DIR,
  logLevel: 'error', server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'spectator-matchmaking-consumer',
    resolveId(id) { if (id === '/__spectator_matchmaking.js') return id },
    load(id) { if (id === '/__spectator_matchmaking.js') return entry },
    configureServer(vite) { vite.middlewares.use((req, res, next) => {
      if (!req.url?.match(/^\/__spectator_matchmaking__(\?|$)/)) return next()
      res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(html)
    }) },
  }] })
let browser
const cases = [], errors = []
try {
  await server.listen()
  const base = `http://127.0.0.1:${server.httpServer.address().port}/__spectator_matchmaking__?spectator`
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage()
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const click = async locator => {
    await locator.scrollIntoViewIfNeeded()
    const hit = await locator.evaluate(element => { const r = element.getBoundingClientRect(); const stack=document.elementsFromPoint(r.x+r.width/2,r.y+r.height/2); return {owned:element.contains(stack[0]),target:element.outerHTML,rect:r.toJSON(),stack:stack.slice(0,5).map(item=>item.outerHTML.slice(0,240))} })
    assert.equal(hit.owned, true, 'Physical centre must belong to the intended action: '+JSON.stringify(hit))
    await locator.click()
  }
  const open = async () => {
    await click(page.locator('.battle-route-controls').getByRole('button', { name: '设置', exact: true }))
    await click(page.getByRole('button', { name: /^(排队匹配|管理匹配)$/ }))
    const dialog = page.getByRole('dialog', { name: '边观战边匹配', exact: true })
    await dialog.waitFor(); return dialog
  }
  for (const [width,height] of [[1366,768],[844,390],[390,844]]) {
    await page.setViewportSize({width,height})
    await page.goto(base)
    await page.locator('.board-viewport').waitFor()
    await page.evaluate(()=>{window.__observedBoard=document.querySelector('.board-viewport')})
    const panel=await open()
    assert.equal(await panel.getByText('友谊战整备室', {exact:true}).count(),0)
    assert.equal(await panel.getByRole('button',{name:'好友房',exact:true}).count(),0)
    await click(panel.getByRole('button',{name:'排位规则',exact:true}))
    const rules=page.getByRole('dialog',{name:'排位规则',exact:true})
    await rules.waitFor()
    await click(rules.getByRole('button',{name:'我知道了',exact:true}))
    await rules.waitFor({state:'detached'})
    await click(panel.getByRole('button',{name:'休闲匹配',exact:true}))
    await panel.getByText('符合当前模式规则',{exact:true}).waitFor()
    await click(panel.getByRole('button',{name:/^更换牌库：/}))
    const selector=page.getByRole('dialog',{name:'休闲匹配更换牌库',exact:true})
    await selector.waitFor()
    await click(selector.getByRole('button',{name:'取消更换牌库',exact:true}))
    await selector.waitFor({state:'detached'})
    await click(panel.getByRole('button',{name:/^更换牌库：/}))
    await selector.waitFor()
    await click(selector.locator('.selector-list>button').first())
    await click(selector.getByRole('button',{name:'确认使用',exact:true}))
    await selector.waitFor({state:'detached'})
    await click(panel.getByRole('button',{name:'开始休闲匹配',exact:true}))
    await panel.getByRole('button',{name:'取消休闲匹配',exact:true}).waitFor()
    assert.equal(await page.evaluate(()=>document.querySelector('.board-viewport')===window.__observedBoard),true,'Opening nested dialogs and queuing must not recreate the observed board')
    assert.equal(await page.locator('.battle-route-controls').evaluate(element=>element.inert),true)
    await page.screenshot({path:path.join(out,`queued-${width}x${height}.png`)})
    await click(panel.getByRole('button',{name:'返回观战',exact:true}))
    assert.deepEqual(await page.evaluate(()=>window.__qa.observation()),{matchId:'synthetic-batch253',spectating:true,queued:true})
    const reopened=await open()
    assert.equal(await reopened.getByRole('button',{name:'休闲匹配',exact:true}).getAttribute('class'),'active','Reopening a casual queue must retain its mode')
    await click(reopened.getByRole('button',{name:'取消休闲匹配',exact:true}))
    assert.equal((await page.evaluate(()=>window.__qa.observation())).spectating,true)
    await click(reopened.getByRole('button',{name:'休闲匹配',exact:true}))
    await click(reopened.getByRole('button',{name:'开始休闲匹配',exact:true}))
    await page.evaluate(()=>window.__qa.pair())
    await panel.waitFor({state:'detached'})
    assert.deepEqual(await page.evaluate(()=>window.__qa.observation()),{matchId:'own-new',spectating:false,queued:false})
    const commands=await page.evaluate(()=>window.__sentCommands)
    assert.deepEqual(commands.map(item=>item.type),['joinMatchmaking','cancelMatchmaking','joinMatchmaking'])
    assert.equal(commands[0].mode,'casual')
    assert.equal(commands[0].deck.id,'qa-observer-deck')
    cases.push({width,height,passed:true,commands:commands.map(item=>({type:item.type,mode:item.mode})),physicalHit:true,nestedRules:true,nestedDeckConfirm:true,observedBoardRetained:true})
  }
  for (const role of ['referee','tournament']) {
    await page.goto(base+'&role='+role)
    await click(page.locator('.battle-route-controls').getByRole('button',{name:'设置',exact:true}))
    assert.equal(await page.getByRole('button',{name:/^(排队匹配|管理匹配)$/}).count(),0)
    cases.push({role,passed:true})
  }
  assert.deepEqual(errors,[])
  fs.writeFileSync(path.join(out,'receipt.json'),JSON.stringify({status:'passed',cases,errors,scope:'Real SFC/Edge local synthetic HTTP/socket consumers; not live server, real iOS or WeChat'},null,2))
  console.log(`Spectator matching browser consumers: ${cases.length}/${cases.length} passed`)
} catch(error) {
  if(browser)await browser.contexts()[0]?.pages()[0]?.screenshot({path:path.join(out,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(out,'failure.json'),JSON.stringify({cases,errors,error:String(error.stack)},null,2));throw error
} finally { await browser?.close(); await server.close() }
