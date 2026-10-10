import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require=createRequire(import.meta.url)
const { chromium }=require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target=process.argv[2]||'http://127.0.0.1:5191/__l12_battle_preview__'
const output=process.env.L12_R64_OUT||path.resolve('..','artifacts','batch-mobile-battle-r6-4','scroll')
const viewports=[[667,375],[844,390],[932,430],[1024,768]].map(([width,height])=>({width,height}))
fs.mkdirSync(output,{recursive:true})
const manifest={generatedAt:new Date().toISOString(),target,status:'running',acceptance:{viewports,states:['start','middle','end'],subjects:['candidate-20','hand-20'],scrollbar:'hidden',cue:'inset fade only; no arrow, outline, rail or hit target'},results:[]}
const browser=await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE?{headless:true,executablePath:process.env.L12_CHROMIUM_EXECUTABLE}:{headless:true,channel:'msedge'})
try{
 const context=await browser.newContext();const page=await context.newPage();const cdp=await context.newCDPSession(page);page.setDefaultTimeout(10000)
 await page.addInitScript(()=>{const native=window.matchMedia.bind(window);window.matchMedia=q=>q.includes('pointer: coarse')?{matches:true,media:q,addEventListener(){},removeEventListener(){}}:native(q)})
 await page.route('**/*',route=>['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname)?route.continue():route.abort())
 async function load(viewport,query){await page.setViewportSize(viewport);await page.goto(`${target}?mobile=1&${query}`,{waitUntil:'domcontentloaded'});await page.locator('[data-l12-mobile-landscape="true"]').waitFor();await page.waitForTimeout(80)}
 async function audit(locator,label){
  const data=await locator.evaluate(e=>{const style=getComputedStyle(e),after=getComputedStyle(e,'::after');return{client:e.clientWidth,scroll:e.scrollWidth,left:e.scrollLeft,max:e.scrollWidth-e.clientWidth,moreStart:e.dataset.moreStart,moreEnd:e.dataset.moreEnd,scrollbarWidth:style.scrollbarWidth,webkitScrollbar:getComputedStyle(e,'::-webkit-scrollbar').display,outlineStyle:style.outlineStyle,afterContent:after.content,afterDisplay:after.display,boxShadow:style.boxShadow,touchAction:style.touchAction}})
  assert.ok(data.scroll>data.client+2,`${label}: fixture does not overflow`);assert.ok(data.scrollbarWidth==='none'||data.webkitScrollbar==='none',`${label}: visible scrollbar ${JSON.stringify(data)}`);assert.equal(data.outlineStyle,'none',`${label}: outline/rail remains`);assert.ok(['none','normal',''].includes(data.afterContent.replaceAll('"','')),`${label}: generated arrow remains ${JSON.stringify(data)}`);assert.match(data.touchAction,/pan-x/,`${label}: natural touch pan missing`);return data
 }
 async function setPosition(locator,ratio){await locator.evaluate((e,ratio)=>{e.style.scrollBehavior='auto';e.scrollLeft=(e.scrollWidth-e.clientWidth)*ratio;e.dispatchEvent(new Event('scroll',{bubbles:true}))},ratio);await page.waitForTimeout(60)}
 async function touchPan(locator){const box=await locator.boundingBox();assert.ok(box);const fromX=box.x+box.width-18,toX=box.x+24,y=box.y+Math.min(28,box.height/2);await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:fromX,y}]});for(let i=1;i<=6;i++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:fromX+(toX-fromX)*i/6,y}]});await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await page.waitForTimeout(80);return(await locator.evaluate(e=>e.scrollLeft))>0}
 async function pointerDrag(locator){await setPosition(locator,0);const box=await locator.boundingBox();assert.ok(box);const y=box.y+Math.min(28,box.height/2);await page.mouse.move(box.x+box.width-16,y);await page.mouse.down();await page.mouse.move(box.x+24,y,{steps:8});await page.mouse.up();return(await locator.evaluate(e=>e.scrollLeft))>0}
 for(const viewport of viewports){
  const suffix=`${viewport.width}x${viewport.height}`
  for(const subject of ['candidate-20','hand-20']){
   await load(viewport,subject==='candidate-20'?'field=full&hand=10&card-choice=1&choice-count=20':'field=full&hand=20')
   const locator=subject==='candidate-20'?page.locator('.prompt-panel .prompt-card-strip').last():page.locator('.board-center>.l12-hand:last-child')
   await locator.waitFor();if(subject==='candidate-20'){await locator.locator('.prompt-card-candidate').first().click();assert.equal(await locator.locator('.prompt-card-candidate.selected').count(),1);assert.equal(await page.locator('.mobile-card-inspector').count(),0)}
   for(const [state,ratio,start,end] of [['start',0,'false','true'],['middle',.5,'true','true'],['end',1,'true','false']]){
    await setPosition(locator,ratio);const data=await audit(locator,`${subject}-${state}-${suffix}`);assert.equal(data.moreStart,start);assert.equal(data.moreEnd,end);assert.notEqual(data.boxShadow,'none',`${subject}-${state}-${suffix}: edge fade missing`);const file=`${subject}-${state}-${suffix}.png`;await page.screenshot({path:path.join(output,file)});manifest.results.push({subject,state,viewport:suffix,file,metrics:data,assertions:'passed'})
   }
   await setPosition(locator,0);assert.ok(await touchPan(locator),`${subject}-${suffix}: touch pan failed`);await setPosition(locator,0);await locator.hover();await page.mouse.wheel(0,240);await page.waitForTimeout(60);assert.ok((await locator.evaluate(e=>e.scrollLeft))>0,`${subject}-${suffix}: wheel failed`);assert.ok(await pointerDrag(locator),`${subject}-${suffix}: pointer drag failed`)
   if(subject==='candidate-20')assert.equal(await locator.locator('.prompt-card-candidate.selected').count(),1,`${subject}-${suffix}: drag changed selection`);else assert.equal(await locator.locator('.hand-card-wrap.selected').count(),0,`${subject}-${suffix}: drag selected a hand card`)
  }
 }
 manifest.status='passed';fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2));console.log(JSON.stringify({output,status:manifest.status,results:manifest.results.length,screenshots:manifest.results.length},null,2))
}finally{await browser.close()}
