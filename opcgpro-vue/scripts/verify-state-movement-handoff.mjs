import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_HANDOFF_OUT
if (!output) throw new Error('L12_HANDOFF_OUT is required')
const reportPath = path.join(output, 'report.json')
if (fs.existsSync(reportPath)) throw new Error(`Refusing to overwrite evidence: ${reportPath}`)
fs.mkdirSync(output, { recursive:true })
const runnerPath = fileURLToPath(import.meta.url)
const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
const runnerBefore = sha256(fs.readFileSync(runnerPath))
const boundFiles = [
  'src/l12/game/ZoneMovementPresentationLayer.vue',
  'src/l12/game/CardStateTransitionLayer.vue',
  'src/l12/game/authoritativeCardVisibility.ts',
  'src/l12/mobileViewport.ts',
  'scripts/fixtures/BattleVisualTransitionHarness.vue',
]
const bind = () => Object.fromEntries(boundFiles.map(relative => {
  const absolutePath = path.join(root, relative)
  const bytes = fs.readFileSync(absolutePath)
  return [relative, { absolutePath, sha256:sha256(bytes), bytes:bytes.length }]
}))
const sourcesBefore = bind()
const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";createApp(Harness).mount("#app")</script></body></html>'
const plugin = { name:'state-movement-handoff', configureServer(server) {
  server.middlewares.use('/__state-movement-handoff', async (_request,response) => {
    response.setHeader('Content-Type','text/html; charset=utf-8')
    response.end(await server.transformIndexHtml('/__state-movement-handoff',html))
  })
} }
const profiles = [
  {name:'desktop-wide',viewport:{width:1366,height:768},mobile:false,landscape:false},
  {name:'mobile-landscape',viewport:{width:844,height:390},mobile:true,landscape:true},
]
const results=[], errors=[]
const check=(value,message)=>{if(!value)errors.push(message)}
const server=await createServer({root,cacheDir:path.join(output,'vite-cache'),plugins:[plugin],server:{host:'127.0.0.1',port:0},logLevel:'error'})
let browser
try {
  await server.listen()
  const port=server.httpServer.address().port
  browser=await chromium.launch({channel:'msedge',headless:true})
  for(const profile of profiles){
    const context=await browser.newContext({viewport:profile.viewport,isMobile:profile.mobile,hasTouch:profile.mobile,reducedMotion:'no-preference'})
    const page=await context.newPage(),pageErrors=[]
    page.on('pageerror',error=>pageErrors.push(error.message))
    await page.addInitScript(()=>localStorage.setItem('l12-audio-preferences-v1',JSON.stringify({animation:'standard',mobileLayout:'auto'})))
    await page.goto(`http://127.0.0.1:${port}/__state-movement-handoff?viewer=0&landscape=${profile.landscape?'1':'0'}`,{waitUntil:'networkidle'})
    await page.waitForSelector('[data-l12-game-stage]')
    const layout=await page.evaluate(()=>({viewport:document.documentElement.dataset.l12Viewport,
      mobile:document.documentElement.dataset.l12Mobile,rotated:document.documentElement.dataset.l12Rotated,
      logicalWidth:getComputedStyle(document.documentElement).getPropertyValue('--l12-viewport-width').trim(),
      logicalHeight:getComputedStyle(document.documentElement).getPropertyValue('--l12-viewport-height').trim()}))
    const sourceBefore=await page.locator('[data-card-instance-id="mover-1"]').boundingBox()
    await page.evaluate(()=>window.__visualTransitionHarness.beginAttackRestHandoff())
    const stateGhost=page.locator('.l12-card-state-transition-ghost[data-motion-kind="attack-rest"]')
    await stateGhost.waitFor({state:'visible',timeout:3000})
    const authorityHidden=await page.locator('[data-card-instance-id="mover-1"]').evaluate(node=>getComputedStyle(node).visibility==='hidden')
    await page.evaluate(()=>window.__visualTransitionHarness.returnDuringAttackRest())
    await page.waitForFunction(()=>document.querySelector('[data-movement-instance-id="mover-1"]'),null,{timeout:6000})
    const movement=page.locator('[data-movement-instance-id="mover-1"]')
    await page.waitForFunction(()=>getComputedStyle(document.querySelector('[data-movement-instance-id="mover-1"]')).visibility==='visible',null,{timeout:3000})
    const evidence=await movement.evaluate(node=>{
      const child=node.firstElementChild, image=node.querySelector('img'), rect=node.getBoundingClientRect(), childRect=child?.getBoundingClientRect()
      return {wrapperVisibility:getComputedStyle(node).visibility,childVisibility:child?getComputedStyle(child).visibility:null,
        imageComplete:image?.complete??false,naturalWidth:image?.naturalWidth??0,
        wrapperRect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height},
        childRect:childRect&&{x:childRect.x,y:childRect.y,width:childRect.width,height:childRect.height},
        parentId:node.parentElement?.id??null,key:node.getAttribute('data-movement-key')}
    })
    await page.screenshot({path:path.join(output,`${profile.name}-state-to-move.png`)})
    await page.waitForFunction(()=>!document.querySelector('[data-movement-instance-id="mover-1"]'),null,{timeout:4000})
    const settled=await page.locator('[data-card-instance-id="mover-1"]').evaluate(node=>({visibility:getComputedStyle(node).visibility,zone:node.closest('[data-l12-zone]')?.getAttribute('data-l12-zone')}))
    results.push({profile,layout,sourceBefore,authorityHidden,evidence,settled,pageErrors})
    const prefix=profile.name
    check(pageErrors.length===0,`${prefix}: page errors ${pageErrors.join('; ')}`)
    check(Boolean(sourceBefore&&sourceBefore.width>20&&sourceBefore.height>20),`${prefix}: source card box was not real ${JSON.stringify(sourceBefore)}`)
    check(authorityHidden,`${prefix}: fixture did not reproduce the overlapping authority visibility lease`)
    check(evidence.wrapperVisibility==='visible'&&evidence.childVisibility==='visible',`${prefix}: movement clone inherited hidden visibility ${JSON.stringify(evidence)}`)
    check(evidence.key?.includes('field>hand'),`${prefix}: wrong movement identity ${evidence.key}`)
    check(evidence.imageComplete&&evidence.naturalWidth>0,`${prefix}: movement clone image was not decoded`)
    check(Boolean(evidence.childRect&&evidence.childRect.width>20&&evidence.childRect.height>20),`${prefix}: movement clone collapsed ${JSON.stringify(evidence.childRect)}`)
    check(settled.visibility==='visible'&&settled.zone==='hand',`${prefix}: authority handoff failed ${JSON.stringify(settled)}`)
    if(profile.mobile){
      check(layout.viewport==='landscape'&&layout.mobile==='true',`${prefix}: mobile layout hook inactive ${JSON.stringify(layout)}`)
      check(layout.logicalWidth==='844px'&&layout.logicalHeight==='390px',`${prefix}: wrong logical canvas ${JSON.stringify(layout)}`)
      check(evidence.parentId==='l12-landscape-teleports',`${prefix}: ghost was outside logical animation canvas`)
    }
    await context.close()
  }
} finally { await browser?.close();await server.close() }
const sourcesAfter=bind(),runnerAfter=sha256(fs.readFileSync(runnerPath))
for(const relative of boundFiles)check(sourcesAfter[relative].sha256===sourcesBefore[relative].sha256,`source changed during evidence: ${relative}`)
check(runnerAfter===runnerBefore,'runner changed during evidence')
const report={schema:1,status:errors.length?'red':'green',runner:{path:runnerPath,beforeSha256:runnerBefore,afterSha256:runnerAfter,unchanged:runnerBefore===runnerAfter},sources:{before:sourcesBefore,after:sourcesAfter},errors,results}
fs.writeFileSync(reportPath,JSON.stringify(report,null,2))
console.log(JSON.stringify({status:report.status,reportPath,errors,results},null,2))
if(errors.length)process.exitCode=1
