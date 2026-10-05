import fs from 'node:fs'
import path from 'node:path'
import assert from 'node:assert/strict'
import { pathToFileURL } from 'node:url'

// Reuse the real App/editor, route, synthetic account, assets and network
// isolation of the portrait fixture. Fail closed if that fixture changes its
// runner boundary; do not maintain a second editor or mocked scroll behavior.
const front=path.resolve(import.meta.dirname,'..')
const originalFile=path.join(front,'scripts/verify-deck-editor-portrait.mjs')
const original=fs.readFileSync(originalFile,'utf8').replaceAll('\r\n','\n')
const marker='try{\n await server.listen();browser=await chromium.launch'
assert.equal(original.split(marker).length,2,'Known real-consumer fixture boundary')
let source=original.slice(0,original.indexOf(marker))
source=source.replace("import { createServer } from 'vite'",`import { createServer } from '${pathToFileURL(path.join(front,'node_modules/vite/dist/node/index.js'))}'`)
source=source.replace("const front=path.resolve(import.meta.dirname,'..'),repo=path.dirname(front),assets=",`const front=${JSON.stringify(front)},repo=path.dirname(front),assets=`)
source=source.replaceAll('import.meta.filename',JSON.stringify(originalFile))
source=source.replace('hasTouch:true,isMobile:width<1000','hasTouch:false,isMobile:false')
source=source.replace("cacheDir:'D:/GPT/Legion12/cache/e4-implementation/vite'","cacheDir:'D:/GPT/Legion12/cache/editor-scroll-acceptance/vite'")
assert(!source.includes('import.meta.dirname'))
process.env.L12_E4_OUTPUT=process.env.L12_EDITOR_SCROLL_OUTPUT||path.join('D:/GPT/Legion12/artifacts/editor-short-scroll-20261005',new Date().toISOString().replace(/[:.]/g,'-'))
source+=`
report.scroll=[]
report.scrollRunnerSha=hash(fs.readFileSync(${JSON.stringify(import.meta.filename)}))
report.limitations.push('Desktop native wheel in Edge browser automation, not real laptop hardware or iOS Safari. Short-screen feedback intentionally stays hidden.')
async function visiblePoint(locator,edge=false){
 return locator.evaluate((n,edge)=>{
  const v={left:0,top:0,right:innerWidth,bottom:innerHeight},rows=[]
  for(let p=n;p;p=p.parentElement){const c=getComputedStyle(p),r=p.getBoundingClientRect();rows.push({tag:p.tagName,class:p.className,top:p.scrollTop,height:p.clientHeight,total:p.scrollHeight,overflow:c.overflowY});if(p===n||['hidden','clip','auto','scroll'].includes(c.overflowY)){v.top=Math.max(v.top,r.top);v.bottom=Math.min(v.bottom,r.bottom)}if(p===n||['hidden','clip','auto','scroll'].includes(c.overflowX)){v.left=Math.max(v.left,r.left);v.right=Math.min(v.right,r.right)}}
  const x=edge?v.right-5:v.left+(v.right-v.left)*.45,y=v.top+(v.bottom-v.top)*.6,hit=document.elementFromPoint(x,y)
  return {rect:v,rows,x,y,hit:{tag:hit?.tagName,class:hit?.className},owned:!!hit&&(n===hit||n.contains(hit))}
 },edge)
}
async function nativeWheel(page,edge=false){
 const catalog=page.locator('.deck-catalog'),surface=await page.locator('.deck-card-grid').evaluate(n=>['auto','scroll'].includes(getComputedStyle(n).overflowY))?page.locator('.deck-card-grid'):catalog,point=await visiblePoint(surface,edge)
 check(point.rect.right-point.rect.left>20&&point.rect.bottom-point.rect.top>20,'Card catalog has no physical wheel area')
 check(point.owned,'Wheel hit lies outside actual catalog')
 const before=await catalog.evaluate(n=>({top:n.scrollTop,grid:n.querySelector('.deck-card-grid').scrollTop}))
 await page.mouse.move(point.x,point.y);await page.mouse.wheel(0,650);await pause(300)
 const after=await catalog.evaluate(n=>({top:n.scrollTop,grid:n.querySelector('.deck-card-grid').scrollTop}))
 check(after.top>before.top+20||after.grid>before.grid+20,'Native wheel did not scroll catalog or card grid')
 const cardPoint=await visiblePoint(page.locator('.deck-card-grid'))
 check(cardPoint.rect.bottom-cardPoint.rect.top>20,'Card pool remains clipped after real wheel input')
 return {point,before,after,cardPoint}
}
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const sizes=process.env.L12_EDITOR_SCROLL_QUICK==='1'?[[1024,600],[1024,500],[844,390]]:[[1920,1080],[1366,768],[1366,600],[1280,720],[1180,640],[1024,768],[1024,600],[1024,500],[844,390],[821,375],[820,1000],[390,844]]
 for(const [width,height] of sizes)for(const cardSize of (process.env.L12_EDITOR_SCROLL_QUICK==='1'?['medium']:['small','medium','large'])){
  const {page,context}=await open(width,height,cardSize),r={name:width+'x'+height+'-'+cardSize,tasks:[]};report.scroll.push(r)
  const portrait=await page.locator('.portrait-editor').count()>0
  await page.locator('.catalog-tabs button').nth(1).click()
  await task(r,'physical-containment-and-pane-isolation',async()=>{const g=await geometry(page);check(g.bodyWidth<=g.clientWidth+1,'Body width overflow');if(!portrait){const a=await page.locator('.deck-center-column').boundingBox(),b=await page.locator('.deck-list').boundingBox();check(a.x+a.width<=b.x+1,'Center scroll invades right sidebar')}return g})
  if(!portrait){
   await task(r,'native-wheel-from-card-or-filter',async()=>nativeWheel(page))
   await task(r,'native-wheel-from-padding',async()=>{await page.locator('.deck-catalog,.deck-card-grid').evaluateAll(nodes=>nodes.forEach(n=>n.scrollTop=0));return nativeWheel(page,true)})
   await task(r,'last-card-reachable-by-native-wheel',async()=>{
    const surface=await page.locator('.deck-card-grid').evaluate(n=>['auto','scroll'].includes(getComputedStyle(n).overflowY))?page.locator('.deck-card-grid'):page.locator('.deck-catalog'),point=await visiblePoint(surface);await page.mouse.move(point.x,point.y)
    for(let i=0;i<18;i++){const last=await visiblePoint(page.locator('.deck-card').last());if(last.owned&&last.rect.bottom-last.rect.top>50)break;await page.mouse.wheel(0,1800);await pause(90)}
    const last=await visiblePoint(page.locator('.deck-card').last());check(last.owned&&last.rect.bottom-last.rect.top>50,'Final card cannot be reached by native wheel');return last
   })
  }else await task(r,'portrait-owner-unchanged',async()=>{check(await page.locator('.deck-catalog').evaluate(n=>getComputedStyle(n).overflowY)==='visible','Portrait inherited landscape scroll');const grid=page.locator('.deck-builder-grid');check(await grid.evaluate(n=>n.scrollHeight>n.clientHeight+100),'Portrait owner lost content');return {portrait:true}})
  await shot(page,r.name+'-cards')
  await task(r,'filters-add-remove-and-copy-limit',async()=>{
   const q=page.locator('.filter-search input');await q.fill('佣兵部队');await pause(90)
   const card=page.locator('.deck-card[data-card-id="S01-0002"]');check(Number(await card.locator('.pool-count-controls strong').textContent())===3,'Official preset count changed');check(await card.getByRole('button',{name:'增加一张',exact:true}).isDisabled(),'Fourth mercenary allowed')
   await card.getByRole('button',{name:'减少一张',exact:true}).click();check(Number(await card.locator('.pool-count-controls strong').textContent())===2,'Remove unreachable');await card.getByRole('button',{name:'增加一张',exact:true}).click();check(Number(await card.locator('.pool-count-controls strong').textContent())===3,'Add unreachable');await q.fill('');return {copyLimit:3}
  })
  await task(r,'stats-opening-hand-and-return',async()=>{
   const mobile=await page.locator('.deck-mobile-nav button').first().isVisible()
   if(mobile)await page.locator('.deck-mobile-nav button').filter({hasText:'统计 / 起手'}).click();else await page.locator('.workspace-tabs button').filter({hasText:'统计'}).click()
   check(await page.locator('[data-editor-workspace="stats"]').isVisible(),'Stats unreachable');await page.locator('.workspace-tabs button').filter({hasText:'起手'}).click();await page.getByRole('button',{name:'重新试抽',exact:true}).click();check(await page.locator('.editor-opening-hand article').count()===6,'Hand redraw changed');if(mobile)await page.locator('.deck-mobile-nav button').filter({hasText:/^牌库$/}).click();else await page.locator('.workspace-tabs button').filter({hasText:'牌库'}).click();check(await page.locator('.deck-catalog').isVisible(),'Pool return failed');return {handCopies:6}
  })
  await task(r,'short-screen-feedback-policy-preserved',async()=>{if(height<=520)check(!await page.locator('.bug-feedback-trigger').isVisible(),'Rejected feedback-entry change introduced');return {shortScreenHidden:height<=520}})
  await shot(page,r.name+'-return');await context.close()
 }
}catch(error){report.failures.push({scope:'runner',error:error.message})}
finally{
 if(report.errors.length||report.external.length||report.apiWrites.length)report.failures.push({scope:'isolation',errors:report.errors,external:report.external,apiWrites:report.apiWrites})
 report.sourceAfter=fingerprints();report.sourceUnchanged=JSON.stringify(report.sourceBefore)===JSON.stringify(report.sourceAfter);report.finishedAt=new Date().toISOString();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));await browser?.close();await server.close();console.log(JSON.stringify({profiles:report.scroll.length,tasks:report.scroll.flatMap(r=>r.tasks).length,failures:report.failures,sourceUnchanged:report.sourceUnchanged,out},null,2));process.exitCode=report.failures.length||!report.sourceUnchanged?1:0
}
`
await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`).catch(error=>{console.error(error.message);process.exitCode=1})
