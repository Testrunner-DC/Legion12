import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5180/__l12_battle_preview__?mobile=1'
const out = process.env.L12_TARGET_SLOT_OUT || path.resolve('..','artifacts','batch-mobile-target-slot-b2b-a')
const viewports = [[667,375],[844,390],[932,430],[1024,768]]
fs.mkdirSync(out,{recursive:true})
const browser=await chromium.launch({headless:true,channel:'msedge'})
try {
 const context=await browser.newContext();const page=await context.newPage();page.setDefaultTimeout(8000)
 await page.addInitScript(()=>{const native=window.matchMedia.bind(window);window.matchMedia=query=>query.includes('pointer: coarse')?({matches:true,media:query,addEventListener(){},removeEventListener(){}}):native(query)})
 for(const [width,height] of viewports){
  await page.setViewportSize({width,height})
  for(const [kind,selector] of [['board-target','.formation-slot.targetable'],['board-slot','.formation-slot.available']]){
   await page.goto(`${target}&${kind}=1`,{waitUntil:'domcontentloaded'});await page.locator(selector).first().waitFor()
   const bar=page.locator('.board-target-controls');const before=await bar.boundingBox();assert(before&&before.x>=0&&before.y>=0&&before.x+before.width<=width&&before.y+before.height<=height,`${kind} bar safe ${width}`)
   await page.screenshot({path:path.join(out,`${kind}-initial-${width}x${height}.png`)})
   const targetBox=await page.locator(selector).first().boundingBox();assert(targetBox && !(before.x<targetBox.x+targetBox.width&&before.x+before.width>targetBox.x&&before.y<targetBox.y+targetBox.height&&before.y+before.height>targetBox.y),`${kind} bar does not cover target ${width}`)
   await page.locator(selector).first().click();await page.screenshot({path:path.join(out,`${kind}-selected-${width}x${height}.png`)})
   if(kind==='board-target')await bar.getByRole('button',{name:/确认/}).click()
   const sent=await page.evaluate(()=>window.__sentCommands?.length||0);assert(sent>0,`${kind} command sent ${width}`)
   await page.goto(`${target}&${kind}=1`,{waitUntil:'domcontentloaded'});await bar.waitFor()
   await bar.getByRole('button',{name:kind==='board-target'?'不发动':'取消'}).click()
   const skipped=await page.evaluate(()=>{const sent=window.__sentCommands?.at(-1);return Boolean(sent?.type==='gameAction'&&sent.command?.type==='resolvePrompt'&&JSON.stringify(sent.command.choice??sent.command).includes('skip'))});assert.equal(skipped,true,`${kind} skip command sent ${width}`)
   await page.screenshot({path:path.join(out,`${kind}-${kind==='board-target'?'skip':'cancel'}-${width}x${height}.png`)})
  }
 }
 console.log(JSON.stringify({out,viewports,status:'passed'}))
}finally{await browser.close()}
