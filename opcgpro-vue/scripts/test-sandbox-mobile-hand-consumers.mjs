import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT
if (!out || !process.env.L12_VITE_CACHE_DIR) throw new Error('EXPLICIT_GOVERNED_OUTPUT_REQUIRED')
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const original = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const marker = 'const entry = `', begin = original.indexOf(marker) + marker.length
const end = original.indexOf('const isPicker=', begin)
assert.ok(begin >= marker.length && end > begin)
const entry = original.slice(begin, end) + `
import {ref} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
const scenario=params.get('case')||'gm'
l12State.gmEnabled=scenario==='gm';l12State.spectating=['observer','referee'].includes(scenario)
l12State.observerView=scenario==='referee'?'referee':'public'
l12State.game.activePlayer=1;l12State.game.recentEvents=[]
const cards=[0,1].map(i=>({...card(legions[i],'sandbox-actor'+i+'-hand'),cost:0,currentCost:0,tapped:false,hidden:false,summonRound:-1}))
for(const i of [0,1])Object.assign(l12State.game.players[i],{hand:[cards[i]],handCount:1,spendableResourceCount:2,field:[[card(legions[2],'actor'+i+'-blocker'),null,null],[null,null,null]]})
if(scenario==='player')delete l12State.game.players[1].hand
if(scenario==='observer')l12State.game.players.forEach(p=>delete p.hand)
window.__commitPlay=()=>{const g=l12State.game,p=g.players[1];p.hand=[];p.handCount=0;p.field[1][0]=cards[1];g.revision=2;l12State.pendingAction=false}
window.__commitMove=()=>{const g=l12State.game,p=g.players[1];p.field[1][0]=null;p.field[1][1]=cards[1];g.revision=3;g.prompts=[{promptId:'actor0-response',playerIndex:0,kind:'option',text:'响应确认',validChoices:['yes','no'],minChoose:1,maxChoose:1,data:{uiPattern:'effect-decision',effectText:'由另一位玩家响应。'},choiceLabels:{yes:'发动',no:'不发动'},createdRevision:3,controller:0}];l12State.pendingAction=false}
window.__afterPrompt=()=>{l12State.game.prompts=[];l12State.game.activePlayer=0;l12State.game.revision=4;l12State.pendingAction=false}
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h(GamePage)}})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: process.env.L12_VITE_CACHE_DIR,
  logLevel: 'error', server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'sandbox-mobile-hand-consumers',
    resolveId(id) { if (id === '/__sandbox_mobile.js') return id },
    load(id) { if (id === '/__sandbox_mobile.js') return entry },
    configureServer(vite) { vite.middlewares.use((req, res, next) => {
      if (!req.url?.startsWith('/__sandbox_mobile__')) return next()
      res.setHeader('Content-Type', 'text/html; charset=utf-8')
      res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__sandbox_mobile.js"></script>')
    }) },
  }] })
let browser, page
const cases = [], errors = []
const stable = envelope => { const { requestId, ...body } = envelope; return body }
try {
  await server.listen()
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  page = await browser.newPage({ hasTouch: true })
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const url = `http://127.0.0.1:${server.httpServer.address().port}/__sandbox_mobile__`
  const hand = player => page.locator(`.l12-hand[data-player-index="${player}"]`)
  const tap = async (locator, label) => {
    await locator.scrollIntoViewIfNeeded()
    const hit = await locator.evaluate(element => { const r = element.getBoundingClientRect(); return {
      x: r.x + r.width / 2, y: r.y + r.height / 2,
      owned: element.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)),
      rect: {x:r.x,y:r.y,width:r.width,height:r.height},
      hit: document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.outerHTML.slice(0, 400),
    } })
    assert.equal(hit.owned, true, label + ' must own its physical touch centre: ' + JSON.stringify(hit))
    await page.touchscreen.tap(hit.x, hit.y)
  }
  for (const [width, height] of [[844,390],[740,360],[390,844]]) {
    await page.setViewportSize({ width, height }); await page.goto(url)
    await page.locator('.board-viewport[data-l12-battle-layout="mobile"]').waitFor()
    assert.equal(await page.locator('.gm-panel').count(), 0)
    assert.equal(await hand(1).isVisible(), true)
    assert.equal(await hand(0).isVisible(), false)
    await tap(hand(1).locator('[data-card-instance-id="sandbox-actor1-hand"]'), 'actor1 hand')
    await tap(page.locator('.card-context-actions.hand-actions').getByRole('button', { name:'打出', exact:true }), 'actor1 play')
    const slots = page.locator('.l12-player-mat.side-opponent .formation-slot')
    assert.equal(await slots.count(), 6)
    await tap(slots.nth(0), 'actor1 legal slot')
    let sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[0]), {type:'sandboxAction',actingPlayerIndex:1,command:{type:'playCard',cardInstanceId:'sandbox-actor1-hand',row:1,slot:0,targetPlayerIndex:1}})
    await page.evaluate(() => window.__commitPlay())
    await tap(slots.nth(0).locator('[data-card-instance-id="sandbox-actor1-hand"]'), 'actor1 field card')
    await tap(page.locator('.card-context-actions.field-actions').getByRole('button', { name:'移动', exact:true }), 'actor1 move')
    await tap(slots.nth(1), 'actor1 neighbouring slot')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[1]), {type:'sandboxAction',actingPlayerIndex:1,command:{type:'move',cardInstanceId:'sandbox-actor1-hand',row:1,slot:1}})
    await page.evaluate(() => window.__commitMove())
    const prompt = page.locator('.l12-prompt-overlay .prompt-panel[role="dialog"]')
    await prompt.waitFor()
    assert.equal(await hand(0).isVisible(), true, 'a prompt switches control back to actor0')
    assert.equal(await hand(1).isVisible(), false)
    await page.screenshot({ path:path.join(out, `actor0-prompt-${width}x${height}.png`) })
    await tap(prompt.getByRole('button', { name:'发动', exact:true }), 'actor0 response')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[2]), {type:'sandboxAction',actingPlayerIndex:0,command:{cardInstanceIds:['yes'],type:'resolvePrompt',promptId:'actor0-response',createdRevision:3,controller:0}})
    await page.evaluate(() => window.__afterPrompt())
    await tap(hand(0).locator('[data-card-instance-id="sandbox-actor0-hand"]'), 'actor0 hand')
    await tap(page.locator('.card-context-actions.hand-actions').getByRole('button', { name:'打出', exact:true }), 'actor0 play')
    await tap(page.locator('.l12-player-mat.side-my .formation-slot').nth(4), 'actor0 legal slot')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.equal(sent.length, 4)
    assert.deepEqual(stable(sent[3]), {type:'sandboxAction',actingPlayerIndex:0,command:{type:'playCard',cardInstanceId:'sandbox-actor0-hand',row:1,slot:1,targetPlayerIndex:0}})
    cases.push({width,height,scenario:'gm',nativeTouch:true,commands:sent.map(stable),passed:true})
  }
  await page.setViewportSize({ width:844, height:390 })
  for (const scenario of ['player','observer','referee']) {
    await page.goto(url+'?case='+scenario)
    await page.locator('.board-viewport').waitFor()
    assert.equal(await hand(0).isVisible(), true)
    assert.equal(await hand(1).isVisible(), scenario === 'referee')
    if (scenario === 'observer') assert.equal(await page.locator('.l12-hand [data-card-instance-id]').count(),0)
    if (scenario === 'player') assert.equal(await hand(1).locator('[data-card-instance-id]').count(),0)
    if (scenario === 'referee') assert.equal(await page.locator('.board-viewport.referee-both-hands').count(),1)
    assert.equal(await page.locator('.card-context-actions.hand-actions').count(),0)
    assert.equal((await page.evaluate(() => window.__sentCommands)).length,0)
    cases.push({scenario,ordinaryPrivacyRetained:true,passed:true})
  }
  await page.setViewportSize({ width:1366, height:768 }); await page.goto(url)
  await page.locator('.board-viewport[data-l12-battle-layout="desktop"]').waitFor()
  assert.equal(await hand(0).isVisible(), true); assert.equal(await hand(1).isVisible(), true)
  cases.push({scenario:'desktop-gm',bothHandsRetained:true,passed:true})
  assert.deepEqual(errors,[])
  fs.writeFileSync(path.join(out,'receipt.json'),JSON.stringify({status:'passed',cases,errors,
    scope:'Real current SFCs and native Edge touch with synthetic public state/socket acknowledgments; not engine execution, live a355, iOS, WeChat or the original private report'},null,2))
  console.log(`Sandbox mobile hand consumers: ${cases.length}/${cases.length} passed`)
} catch(error) {
  await page?.screenshot({path:path.join(out,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(out,'failure.json'),JSON.stringify({cases,errors,error:String(error.stack)},null,2));throw error
} finally { await browser?.close(); await server.close() }
