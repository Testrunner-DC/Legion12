import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { gunzipSync } from 'node:zlib'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const fixturePath = process.env.L12_REAL_ENGINE_FIXTURE
  || path.join(root,'scripts/fixtures/battle-authority-engine-v5.json.gz.b64')
if (!fs.existsSync(fixturePath)) throw new Error(`Fixture not found: ${fixturePath}`)
const storedFixture = fs.readFileSync(fixturePath)
const fixtureBytes = fixturePath.endsWith('.gz.b64')
  ? gunzipSync(Buffer.from(storedFixture.toString('utf8').trim(),'base64'))
  : storedFixture
const fixtureSha256 = crypto.createHash('sha256').update(fixtureBytes).digest('hex')
const fixture = JSON.parse(fixtureBytes.toString('utf8'))
const runId = new Date().toISOString().replaceAll(':','-').replaceAll('.','-')
const output = process.env.L12_EXTRA_RELIC_STATE_OUT
  || path.join('D:/GPT/Legion12/artifacts/battle-animation-recurrence-20261003',`extra-relic-state-${runId}`)
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite existing evidence directory: ${output}`)
fs.mkdirSync(output,{recursive:true})

const entry = `
import { createApp, reactive, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { useLandscapeViewport } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'
const fixture=${JSON.stringify(fixture)}
const params=new URLSearchParams(location.search)
const chain=fixture[params.get('chain')]
const start=params.get('start')==='last'?chain.length-1:0
const snapshot=index=>structuredClone(chain[index].player)
const game=reactive(snapshot(start))
useLandscapeViewport(ref(params.get('landscape')==='1'))
const creations=[]
const samples=[]
new MutationObserver(records=>{
  for(const record of records)for(const node of record.addedNodes){
    if(!(node instanceof HTMLElement))continue
    for(const element of [node,...node.querySelectorAll('*')]){
      if(!(element instanceof HTMLElement)||!element.matches('.l12-card-state-transition-ghost'))continue
      creations.push({key:element.dataset.visualTransitionKey,from:element.dataset.stateFrom,to:element.dataset.stateTo})
    }
  }
}).observe(document.documentElement,{subtree:true,childList:true})
const visible=element=>{
  if(!(element instanceof HTMLElement))return false
  const style=getComputedStyle(element)
  const rect=element.getBoundingClientRect()
  return style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity||1)>0&&rect.width>0&&rect.height>0
}
const decoded=element=>Boolean(element?.querySelector('img')?.complete&&element.querySelector('img')?.naturalWidth)
const angle=element=>{
  if(!(element instanceof HTMLElement))return null
  const transform=getComputedStyle(element).transform
  if(!transform||transform==='none')return 0
  const match=transform.match(/^matrix\\(([^)]+)\\)$/)
  if(!match)return null
  const [a,b]=match[1].split(',').map(Number)
  return (Math.atan2(b,a)*180/Math.PI+360)%360
}
const sampler=setInterval(()=>{
  const source=document.querySelector('[data-card-instance-id="ankh-resolved-extra-source"]')
  const guard=document.querySelector('[data-card-instance-id="ankh-resolved-extra-guard"]')
  const ghosts=[...document.querySelectorAll('.l12-card-state-transition-ghost')]
  const ghostStates=ghosts.map(element=>({
    key:element.dataset.visualTransitionKey,visible:visible(element),decoded:decoded(element),angle:angle(element),
  }))
  const hidden=[source,guard].filter(element=>element instanceof HTMLElement
    &&getComputedStyle(element).visibility==='hidden').map(element=>element.dataset.cardInstanceId)
  samples.push({
    sourceAngle:angle(source),guardAngle:angle(guard),
    sourceWithinExtraRelic:Boolean(source?.closest('.extra-relic')),
    sourceVisible:visible(source),sourceDecoded:decoded(source),
    ghostStates,hidden,
    hiddenWithoutOwnGhost:hidden.filter(instanceId=>!ghostStates.some(ghost=>
      ghost.visible&&ghost.decoded&&ghost.key?.includes(':'+instanceId+':'))),
  })
},16)
window.__extraRelicState={
  start,length:chain.length,label:index=>chain[index].label,
  apply:index=>Object.assign(game,snapshot(index)),
  report:()=>({creations:structuredClone(creations),samples:structuredClone(samples)}),
  stop:()=>clearInterval(sampler),
}
createApp(GameBoard,{game,readOnly:true}).mount('#app')
`
const html='<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__extra_relic_state.js"></script></body></html>'
const plugin={
  name:'extra-relic-state-verifier',
  resolveId(id){if(id==='/__extra_relic_state.js')return id},
  load(id){if(id==='/__extra_relic_state.js')return entry},
  configureServer(server){server.middlewares.use('/__extra-relic-state',(_request,response)=>{
    response.setHeader('Content-Type','text/html; charset=utf-8');response.end(html)
  })},
}
const server=await createServer({root,cacheDir:path.join(output,'vite-cache'),plugins:[plugin],
  server:{host:'127.0.0.1',port:0},logLevel:'error'})
const profiles=[
  {name:'desktop',viewport:{width:1366,height:768},landscape:false},
  {name:'mobile-portrait-rotated',viewport:{width:390,height:844},landscape:true,mobile:true},
]
const results=[]
let browser
try{
  await server.listen()
  const port=server.httpServer.address().port
  browser=await chromium.launch({channel:'msedge',headless:true})
  for(const profile of profiles){
    for(const testCase of [
      {name:'resolved',chain:'readyResolvedExtraRelic',start:'first'},
      {name:'cancelled',chain:'readyCancelled',start:'first'},
      {name:'recovery',chain:'readyResolvedExtraRelic',start:'last'},
    ]){
      const context=await browser.newContext({viewport:profile.viewport,isMobile:profile.mobile,hasTouch:profile.mobile})
      const page=await context.newPage()
      const errors=[]
      page.on('pageerror',error=>errors.push(error.message))
      await page.goto(`http://127.0.0.1:${port}/__extra-relic-state?chain=${testCase.chain}&start=${testCase.start}&landscape=${profile.landscape?'1':'0'}`,{waitUntil:'networkidle'})
      await page.waitForSelector('[data-l12-game-stage]')
      const bounds=await page.evaluate(()=>({start:window.__extraRelicState.start,length:window.__extraRelicState.length}))
      const steps=[]
      for(let index=bounds.start+1;index<bounds.length;index++){
        const before=await page.evaluate(()=>{const report=window.__extraRelicState.report();return{creations:report.creations.length,samples:report.samples.length}})
        const label=await page.evaluate(index=>window.__extraRelicState.label(index),index)
        await page.evaluate(index=>window.__extraRelicState.apply(index),index)
        await page.waitForTimeout(900)
        const current=await page.evaluate(()=>window.__extraRelicState.report())
        steps.push({label,newCreations:current.creations.slice(before.creations),samples:current.samples.slice(before.samples)})
      }
      if(testCase.name==='recovery')await page.waitForTimeout(500)
      const report=await page.evaluate(()=>{window.__extraRelicState.stop();return window.__extraRelicState.report()})
      const screenshot=path.join(output,`${profile.name}-${testCase.name}.png`)
      await page.screenshot({path:screenshot,fullPage:true})
      results.push({profile:profile.name,...testCase,steps,report,errors,screenshot})
      await context.close()
    }
  }
  let assertions=0
  const ok=(value,message)=>{assert.ok(value,message);assertions+=1}
  for(const result of results){
    ok(result.errors.length===0,`${result.profile}/${result.name}: no page errors`)
    ok(result.report.samples.every(sample=>sample.hiddenWithoutOwnGhost.length===0),
      `${result.profile}/${result.name}: hidden authority cards have their own decoded visible ghost`)
    if(result.name==='resolved'){
      const facts=result.steps.flatMap(step=>step.newCreations.map(fact=>({...fact,label:step.label})))
      ok(facts.length===2,`${result.profile}: extra relic cost-rest and target-ready animate exactly once each`)
      ok(facts[0].label==='cost-paid-first-response'&&facts[0].key.includes(':ankh-resolved-extra-source:')
        &&facts[0].from==='active'&&facts[0].to==='rested',`${result.profile}: extra relic rests at paid-cost revision`)
      ok(facts[1].label==='authority-response-pass-2'&&facts[1].key.includes(':ankh-resolved-extra-guard:')
        &&facts[1].from==='rested'&&facts[1].to==='active',`${result.profile}: target readies only at resolved revision`)
      const paid=result.steps.find(step=>step.label==='cost-paid-first-response')
      const resolved=result.steps.find(step=>step.label==='authority-response-pass-2')
      const paidAngles=paid.samples.flatMap(sample=>sample.ghostStates.map(ghost=>ghost.angle)).filter(value=>value!==null)
      const readyAngles=resolved.samples.flatMap(sample=>sample.ghostStates.map(ghost=>ghost.angle)).filter(value=>value!==null)
      ok(paidAngles.some(value=>value<20)&&paidAngles.some(value=>value>45),`${result.profile}: extra relic visibly turns to rested`)
      ok(readyAngles.some(value=>value>70)&&readyAngles.some(value=>value<45),`${result.profile}: target visibly turns to active`)
      ok(result.steps.at(-1).newCreations.length===0,`${result.profile}: same-revision retransmission cannot replay state motion`)
      ok(result.report.samples.some(sample=>sample.sourceWithinExtraRelic&&sample.sourceVisible&&sample.sourceDecoded),
        `${result.profile}: extra relic authority DOM remains visible and decoded in its overlay slot`)
    }else{
      ok(result.report.creations.length===0,`${result.profile}/${result.name}: no historical or cancelled state motion`)
    }
  }
  const reportPath=path.join(output,'report.json')
  fs.writeFileSync(reportPath,JSON.stringify({schema:1,status:'passed',fixturePath:path.resolve(fixturePath),fixtureSha256,
    sourceFingerprint:crypto.createHash('sha256').update([
      fs.readFileSync(path.join(root,'src/l12/game/visualTransitionProjection.ts')),
      fs.readFileSync(path.join(root,'src/l12/game/CardStateTransitionLayer.vue')),
      fs.readFileSync(path.join(root,'src/l12/game/PlayerMat.vue')),
    ].map(buffer=>buffer.toString('base64')).join('\n')).digest('hex'),
    assertions,results,
  },null,2))
  console.log(`Extra relic state transitions passed: ${assertions} assertions across ${results.length} runs`)
  console.log(reportPath)
}catch(error){
  fs.writeFileSync(path.join(output,'failure-report.json'),JSON.stringify({schema:1,status:'failed',fixtureSha256,
    error:error instanceof Error?{name:error.name,message:error.message,stack:error.stack}:String(error),results},null,2))
  throw error
}finally{
  await browser?.close()
  await server.close()
}
