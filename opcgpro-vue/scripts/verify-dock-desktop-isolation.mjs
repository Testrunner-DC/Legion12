import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require=createRequire(import.meta.url)
const {chromium}=require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const browser=await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE ? {headless:true,executablePath:process.env.L12_CHROMIUM_EXECUTABLE}:{headless:true,channel:'msedge'})
const out=path.resolve('../artifacts/mobile-action-dock/desktop')
fs.mkdirSync(out,{recursive:true})
const baseline=process.argv[2] || 'http://127.0.0.1:5198/__l12_battle_preview__'
const candidate=process.argv[3] || 'http://127.0.0.1:5197/__l12_battle_preview__'
const selectors=['.board-stage','.felt-board','.left-rail','.right-rail','.player-panel','.battle-route-controls','.battle-route-controls button','.selected-card-utility-slot','.battle-utility-dock','.battle-utility-dock button','.action-panel','.action-panel button','.board-center>.l12-hand:last-child','.formation-slot','.mini-master','.resource-zone','.mat-piles']
const results=[]
try {
 for(const [width,height] of [[1280,720],[1366,768],[1440,900],[1920,1080],[1920,600]]){
  const snapshots=[]
  for(const [kind,url] of [['baseline',baseline],['candidate',candidate]]){
   const page=await browser.newPage({viewport:{width,height}})
   await page.route('**/*',r=>['127.0.0.1','localhost'].includes(new URL(r.request().url()).hostname)?r.continue():r.abort())
   await page.goto(`${url}?canvas=1&field=full&hand=10&action-fixture=1`,{waitUntil:'domcontentloaded'})
   await page.locator('[data-l12-battle-layout="desktop"]').waitFor()
   await page.waitForTimeout(100)
   assert.equal(await page.locator('.mobile-battle-dock').count(),0)
   const snapshot=await page.evaluate(selectors=>Object.fromEntries(selectors.map(s=>[s,[...document.querySelectorAll(s)].map(e=>{const r=e.getBoundingClientRect(),c=getComputedStyle(e);return {x:r.x,y:r.y,w:r.width,h:r.height,position:c.position,transform:c.transform,display:c.display,fontSize:c.fontSize,pointerEvents:c.pointerEvents,zIndex:c.zIndex}})])),selectors)
   await page.screenshot({path:path.join(out,`${kind}-${width}x${height}.png`)})
   snapshots.push(snapshot);await page.close()
  }
  assert.deepEqual(snapshots[1],snapshots[0],`desktop geometry changed ${width}x${height}`)
  results.push({width,height,selectors:selectors.length,status:'identical'})
 }
 fs.writeFileSync(path.join(out,'manifest.json'),JSON.stringify({baseline,candidate,results},null,2))
 console.log(JSON.stringify(results))
}finally{await browser.close()}
