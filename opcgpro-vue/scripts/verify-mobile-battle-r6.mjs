import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5191/__l12_battle_preview__'
const output = process.env.L12_R6_OUT || path.resolve('..', 'artifacts', 'batch-mobile-battle-r6')
const viewports = [[667,375],[844,390],[932,430],[1024,768]].map(([width,height])=>({width,height}))
const safeProfiles = [
  {name:'left59',viewport:{width:667,height:375},insets:{top:0,right:0,bottom:21,left:59}},
  {name:'right59',viewport:{width:844,height:390},insets:{top:0,right:59,bottom:21,left:0}},
  {name:'both44',viewport:{width:932,height:430},insets:{top:0,right:44,bottom:21,left:44}},
]
const zero = {top:0,right:0,bottom:0,left:0}
fs.mkdirSync(output,{recursive:true})
const manifest={generatedAt:new Date().toISOString(),target,status:'running',acceptance:{candidateCounts:[1,6,12,20],candidateInteraction:'short tap selects/focuses without opening detail; threshold drag and wheel scroll naturally with invisible scrollbar and stateful edge fades',handCounts:[10,15,20],handInteraction:'touch-compatible horizontal pan plus pointer drag/wheel; invisible scrollbar; edge fades disappear at reached edge',runeCounts:[0,1,5],moralePresentation:'round icon-only controls; first rune row; unavailable click updates one dialog-level reason',actionDock:['打出','进攻','发动','取消'],exceptions:[{layer:'天灾卡面动画',reason:'非阻塞、无选择状态且约0.92秒自动结束；新遮挡会立即中断，因此最小化没有可保存的流程状态'}]},candidates:[],hands:[],runes:[],moraleSafe:[],actions:[],dialogBehavior:[]}

const browser=await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE?{headless:true,executablePath:process.env.L12_CHROMIUM_EXECUTABLE}:{headless:true,channel:'msedge'})
try{
 const context=await browser.newContext()
 const page=await context.newPage()
 const cdp=await context.newCDPSession(page)
 page.setDefaultTimeout(10000)
 await page.addInitScript(()=>{const native=window.matchMedia.bind(window);window.matchMedia=query=>query.includes('pointer: coarse')?{matches:true,media:query,addEventListener(){},removeEventListener(){}}:native(query)})
 await page.route('**/*',route=>['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname)?route.continue():route.abort())
 async function load(viewport,query,insets=zero){
  await page.setViewportSize(viewport);await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets})
  await page.goto(`${target}?mobile=1&${query}`,{waitUntil:'domcontentloaded'});await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
  await page.evaluate(()=>{const p=document.createElement('div');p.style.cssText='position:fixed;visibility:hidden;padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)';document.body.append(p);const s=getComputedStyle(p),v=k=>Math.max(0,parseFloat(s.getPropertyValue(k))||0),vv=visualViewport,left=(vv?.offsetLeft??0)+v('padding-left'),top=(vv?.offsetTop??0)+v('padding-top'),root=document.documentElement;root.style.setProperty('--l12-viewport-left',`${left}px`);root.style.setProperty('--l12-viewport-top',`${top}px`);root.style.setProperty('--l12-viewport-width',`${Math.max(1,(vv?.width??innerWidth)-v('padding-left')-v('padding-right'))}px`);root.style.setProperty('--l12-viewport-height',`${Math.max(1,(vv?.height??innerHeight)-v('padding-top')-v('padding-bottom'))}px`);p.remove();window.dispatchEvent(new Event('l12-viewport-change'))})
 }
 async function assertSafe(label,insets=zero){
  const result=await page.evaluate(({insets})=>{const visible=e=>{const s=getComputedStyle(e),r=e.getBoundingClientRect();return s.display!=='none'&&s.visibility!=='hidden'&&r.width>0&&r.height>0};const clipped=e=>{const r=e.getBoundingClientRect(),v={left:r.left,top:r.top,right:r.right,bottom:r.bottom};for(let p=e.parentElement;p&&p!==document.documentElement;p=p.parentElement){const s=getComputedStyle(p);if(!/(auto|scroll|hidden|clip)/.test(`${s.overflow} ${s.overflowX} ${s.overflowY}`))continue;const q=p.getBoundingClientRect();v.left=Math.max(v.left,q.left);v.top=Math.max(v.top,q.top);v.right=Math.min(v.right,q.right);v.bottom=Math.min(v.bottom,q.bottom)}return v};const vv=visualViewport,safe={left:(vv?.offsetLeft??0)+insets.left,top:(vv?.offsetTop??0)+insets.top,right:(vv?.offsetLeft??0)+(vv?.width??innerWidth)-insets.right,bottom:(vv?.offsetTop??0)+(vv?.height??innerHeight)-insets.bottom};const selector='button,a[href],input,select,textarea,[role="button"],.board-stage,.left-rail,.felt-board,.right-rail,.l12-hand:last-child';const outside=[...document.querySelectorAll(selector)].filter(visible).map(e=>({e,r:clipped(e)})).filter(x=>x.r.right>x.r.left&&x.r.bottom>x.r.top).map(({e,r})=>({tag:e.tagName,className:typeof e.className==='string'?e.className:'',text:(e.getAttribute('aria-label')||e.textContent||'').trim().slice(0,40),rect:r})).filter(x=>x.rect.left<safe.left-1||x.rect.top<safe.top-1||x.rect.right>safe.right+1||x.rect.bottom>safe.bottom+1);return{safe,outside,rootOverflow:document.documentElement.scrollWidth>innerWidth+1}}, {insets})
  assert.equal(result.rootOverflow,false,`${label}: root horizontal overflow`);assert.deepEqual(result.outside,[],`${label}: unsafe elements ${JSON.stringify(result.outside.slice(0,5))}`)
 }
 async function capture(file){await page.screenshot({path:path.join(output,file)})}
 async function touchPan(locator){
  const box=await locator.boundingBox();assert.ok(box,'touch target missing')
  const fromX=box.x+box.width-18,toX=box.x+24,y=box.y+Math.min(28,box.height/2)
  await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:fromX,y}]})
  for(let step=1;step<=6;step++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:fromX+(toX-fromX)*step/6,y}]})
  await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await page.waitForTimeout(80)
  return (await locator.evaluate(e=>e.scrollLeft))>0
 }
 async function assertRoundChoices(label,buttons){
  const result=await buttons.evaluateAll(nodes=>nodes.map(node=>{const r=node.getBoundingClientRect(),style=getComputedStyle(node);return{width:r.width,height:r.height,radius:style.borderRadius,text:(node.textContent||'').trim(),children:[...node.children].map(child=>child.tagName),ariaDisabled:node.getAttribute('aria-disabled')}}))
  assert.ok(result.every(item=>Math.abs(item.width-item.height)<=1&&item.width>=40&&parseFloat(item.radius)>=item.width*.45&&item.text===''&&item.children.every(tag=>tag==='IMG')),`${label}: morale/rune controls are not round icon-only ${JSON.stringify(result)}`)
  return result
 }
 async function sentCommand(){return page.evaluate(()=>window.__sentCommands?.at(-1)??null)}
 async function resetSent(){await page.evaluate(()=>window.__resetSentCommands?.())}
 async function assertDock(label,buttonName,insets=zero){
  const button=page.locator('.card-context-actions').getByRole('button',{name:buttonName,exact:true});await button.waitFor()
  const geometry=await page.evaluate(({buttonName,insets})=>{const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'};const buttons=[...document.querySelectorAll('.card-context-actions button')].filter(visible);const target=buttons.find(e=>e.textContent?.trim()===buttonName),end=[...document.querySelectorAll('.action-panel button')].find(e=>visible(e)&&e.textContent?.includes('结束回合'));const rect=e=>{const r=e.getBoundingClientRect();return{left:r.left,top:r.top,right:r.right,bottom:r.bottom}};return{target:target&&rect(target),end:end&&rect(end),count:buttons.length,width:innerWidth,height:innerHeight}}, {buttonName,insets})
  assert.ok(geometry.target&&geometry.end,`${label}: dock/end missing`);assert.ok(geometry.target.bottom<=geometry.end.top+1,`${label}: action is not above end-turn`);assert.ok(geometry.target.left>=insets.left&&geometry.target.right<=geometry.width-insets.right,`${label}: dock outside safe width`);return button
 }

 for(const viewport of viewports){
  const suffix=`${viewport.width}x${viewport.height}`
  for(const count of [1,6,12,20]){
   await load(viewport,`field=full&hand=10&card-choice=1&choice-count=${count}`)
   const strip=page.locator('.prompt-panel .prompt-card-strip').last();await strip.waitFor();await page.waitForTimeout(100)
   assert.equal(await strip.locator('.prompt-card-candidate__inspect,.response-target-detail').count(),0,`candidate ${count}: per-card detail control remains`)
   const first=strip.locator('.prompt-card-candidate').first();await first.click();assert.equal(await strip.locator('.prompt-card-candidate.selected').count(),1,`candidate ${count}: short tap did not select`);assert.equal(await page.locator('.mobile-card-inspector').count(),0,`candidate ${count}: short tap auto-opened detail`)
   const names=await strip.locator('.prompt-card-candidate__name').evaluateAll(nodes=>nodes.map(e=>({text:e.textContent?.trim(),clientWidth:e.clientWidth,scrollWidth:e.scrollWidth,clientHeight:e.clientHeight,scrollHeight:e.scrollHeight,whiteSpace:getComputedStyle(e).whiteSpace})))
   assert.equal(names.length,count,`candidate ${count}: missing names`);assert.ok(names.every(x=>x.text&&x.scrollWidth<=x.clientWidth+1&&x.scrollHeight<=x.clientHeight+1&&x.whiteSpace!=='nowrap'),`candidate ${count}: name clipped`)
   const metrics=await strip.evaluate(e=>({client:e.clientWidth,scroll:e.scrollWidth,left:e.scrollLeft,moreStart:e.dataset.moreStart,moreEnd:e.dataset.moreEnd,scrollbarWidth:getComputedStyle(e).scrollbarWidth,webkitScrollbar:getComputedStyle(e,'::-webkit-scrollbar').display}))
   assert.ok(metrics.scrollbarWidth==='none'||metrics.webkitScrollbar==='none',`candidate ${count}: visible scrollbar remains ${JSON.stringify(metrics)}`)
   let wheel=false,drag=false,touch=false
   if(metrics.scroll>metrics.client+2){
    assert.equal(metrics.moreStart,'false',`candidate ${count}: start hint should be absent initially`);assert.equal(metrics.moreEnd,'true',`candidate ${count}: missing end hint`);touch=await touchPan(strip);assert.ok(touch,`candidate ${count}: touch pan did not scroll`);await strip.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=0;e.dispatchEvent(new Event('scroll',{bubbles:true}))});await strip.hover();await page.mouse.wheel(0,280);await page.waitForTimeout(80);wheel=(await strip.evaluate(e=>e.scrollLeft))>0;assert.ok(wheel,`candidate ${count}: wheel did not scroll`)
    await strip.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=0;e.dispatchEvent(new Event('scroll',{bubbles:true}))});const box=await strip.boundingBox();await page.mouse.move(box.x+box.width-20,box.y+Math.min(60,box.height/2));await page.mouse.down();await page.mouse.move(box.x+30,box.y+Math.min(60,box.height/2),{steps:8});await page.mouse.up();drag=(await strip.evaluate(e=>e.scrollLeft))>0;assert.ok(drag,`candidate ${count}: pointer drag did not scroll`);assert.equal(await strip.getAttribute('data-more-start'),'true');await strip.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=e.scrollWidth;e.dispatchEvent(new Event('scroll',{bubbles:true}))});await page.waitForTimeout(80);assert.equal(await strip.getAttribute('data-more-end'),'false',`candidate ${count}: end hint remains at end`)
   }
   assert.equal(await strip.locator('.prompt-card-candidate.selected').count(),1,`candidate ${count}: scrolling gesture changed the short-tap selection`)
   await assertSafe(`candidate-${count}-${suffix}`);const file=`candidate-${count}-${suffix}.png`;await capture(file);manifest.candidates.push({count,viewport:suffix,file,wheel,drag,touch,scrollbar:'hidden',edgeHints:'start/end stateful',names:'complete',shortTapSelected:true,detailAutoOpened:false,perCardDetailControls:0,assertions:'passed'})
  }
  for(const count of [10,15,20]){
   await load(viewport,`field=full&hand=${count}`);const hand=page.locator('.board-center>.l12-hand:last-child');await hand.waitFor();await page.waitForTimeout(80)
   const initial=await hand.evaluate(e=>({client:e.clientWidth,scroll:e.scrollWidth,moreStart:e.dataset.moreStart,moreEnd:e.dataset.moreEnd,scrollbarWidth:getComputedStyle(e).scrollbarWidth,webkitScrollbar:getComputedStyle(e,'::-webkit-scrollbar').display,touchAction:getComputedStyle(e).touchAction}));assert.ok(initial.scrollbarWidth==='none'||initial.webkitScrollbar==='none',`hand ${count}: visible scrollbar remains ${JSON.stringify(initial)}`);assert.match(initial.touchAction,/pan-x/,`hand ${count}: horizontal touch panning missing`)
   let wheel=false,drag=false,touch=false
   if(initial.scroll>initial.client+2){assert.equal(initial.moreStart,'false',`hand ${count}: start hint should be absent initially`);assert.equal(initial.moreEnd,'true',`hand ${count}: end hint missing initially`);touch=await touchPan(hand);assert.ok(touch,`hand ${count}: touch pan did not scroll`);await hand.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=0;e.dispatchEvent(new Event('scroll',{bubbles:true}))});await hand.hover();await page.mouse.wheel(0,240);await page.waitForTimeout(50);wheel=(await hand.evaluate(e=>e.scrollLeft))>0;assert.ok(wheel,`hand ${count}: wheel did not scroll`);await hand.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=0;e.dispatchEvent(new Event('scroll',{bubbles:true}))});const box=await hand.boundingBox();await page.mouse.move(box.x+box.width-12,box.y+Math.min(24,box.height/2));await page.mouse.down();await page.mouse.move(box.x+24,box.y+Math.min(24,box.height/2),{steps:8});await page.mouse.up();drag=(await hand.evaluate(e=>e.scrollLeft))>0;assert.ok(drag,`hand ${count}: pointer drag did not scroll`);assert.equal(await hand.getAttribute('data-more-start'),'true');assert.equal(await hand.locator('.hand-card-wrap.selected').count(),0,`hand ${count}: drag selected a card`);await hand.evaluate(e=>{e.style.scrollBehavior='auto';e.scrollLeft=e.scrollWidth;e.dispatchEvent(new Event('scroll',{bubbles:true}))});await page.waitForTimeout(80);assert.equal(await hand.getAttribute('data-more-end'),'false',`hand ${count}: end hint remains at end`)}
   const file=`hand-scroll-${count}-${suffix}.png`;await capture(file);manifest.hands.push({count,viewport:suffix,file,overflow:initial.scroll>initial.client+2,wheel,drag,touch,scrollbar:'hidden',edgeHints:'stateful',assertions:'passed'})
  }
  for(const runeCount of [0,1,5]){
   await load(viewport,`field=full&hand=10&runes=${runeCount}`);await page.locator('.resource-morale-summary').last().click();await page.locator('.mobile-morale-overlay').waitFor()
   const buttons=page.locator('.mobile-rune-row .mobile-morale-choice');assert.equal(await buttons.count(),runeCount)
   await assertRoundChoices(`runes view ${runeCount}`,buttons);assert.equal(await page.locator('.mobile-morale-choice span,.mobile-morale-choice b,.mobile-morale-choice small').count(),0,`runes view ${runeCount}: per-item text remains`)
   if(runeCount){assert.equal(await buttons.first().getAttribute('aria-disabled'),'true',`runes view ${runeCount}: view icon must be aria-disabled`);await buttons.first().click({force:true});await page.locator('.mobile-morale-reason').waitFor();assert.match((await page.locator('.mobile-morale-reason').textContent())??'',/无需选择|尚未获得/)}
   const file=`runes-view-${runeCount}-${suffix}.png`;await capture(file);manifest.runes.push({mode:'view',count:runeCount,viewport:suffix,file,iconOnly:true,assertions:'passed'})
  }
  await load(viewport,'field=full&hand=10&runes=5&morale-payment=1&rune-usable=1');if(await page.locator('.prompt-minimize').count())await page.locator('.prompt-minimize').click();await page.locator('.resource-morale-summary').last().click();const runes=page.locator('.mobile-rune-row .mobile-morale-choice');assert.equal(await runes.count(),5)
  await assertRoundChoices('runes payment',runes);assert.equal(await runes.nth(0).getAttribute('aria-disabled'),'false');assert.equal(await runes.nth(1).getAttribute('aria-disabled'),'true');assert.match((await runes.nth(1).getAttribute('title'))??'',/不能使用|尚未获得/);await runes.nth(1).click({force:true});await page.locator('.mobile-morale-reason').waitFor();assert.match((await page.locator('.mobile-morale-reason').textContent())??'',/不能使用|尚未获得/);assert.equal(await runes.nth(1).getAttribute('aria-pressed'),'false');await runes.nth(0).click();assert.ok((await runes.nth(0).getAttribute('class')).includes('selected'));const morale=page.locator('.mobile-resource-row .mobile-morale-choice[aria-disabled="false"]').first();await morale.click();await assertRoundChoices('morale payment',page.locator('.mobile-resource-row .mobile-morale-choice'));const runeFile=`runes-payment-5-${suffix}.png`;await capture(runeFile);await resetSent();await page.locator('.mobile-morale-overlay').getByRole('button',{name:'确认支付',exact:true}).click();assert.equal((await sentCommand())?.command?.type,'resolvePrompt');manifest.runes.push({mode:'payment',count:5,usable:1,viewport:suffix,file:runeFile,iconOnly:true,disabledReason:'single-line',assertions:'passed'})

  for(const action of ['play','attack','ability']){
   await load(viewport,'field=full&hand=5&action-fixture=1');await resetSent()
   if(action==='play'){
    const card=page.locator('.board-center>.l12-hand:last-child .hand-card-wrap.playable').first();await card.click();const dock=await assertDock(`play-${suffix}`,'打出');const file=`action-play-selected-${suffix}.png`;await capture(file);await dock.click();assert.ok(await page.locator('.my-half .formation-slot.available').count(),`play-${suffix}: slot mode absent`);await page.locator('.my-half .formation-slot.available').first().click();assert.equal((await sentCommand())?.command?.type,'playCard');manifest.actions.push({action:'打出',viewport:suffix,file,command:'playCard',assertions:'passed'})
   }else if(action==='attack'){
    await page.locator('.my-half .formation-slot .card-tile').first().click();const dock=await assertDock(`attack-${suffix}`,'进攻');await dock.click();await page.locator('.board-mode-hint').waitFor();const file=`action-attack-selected-${suffix}.png`;await capture(file);await page.locator('.opponent-half .formation-slot.targetable').first().click();assert.equal((await sentCommand())?.command?.type,'attack');manifest.actions.push({action:'进攻',viewport:suffix,file,command:'attack',assertions:'passed'})
   }else{
    await page.locator('.my-half .formation-slot .card-tile').first().click();const dock=await assertDock(`ability-${suffix}`,'发动');const file=`action-ability-selected-${suffix}.png`;await capture(file);await dock.click();assert.equal((await sentCommand())?.command?.type,'activateAbility');assert.equal(await page.locator('.card-context-actions').count(),0);manifest.actions.push({action:'发动',viewport:suffix,file,command:'activateAbility',assertions:'passed'})
   }
  }
 }

 for(const profile of safeProfiles){
  for(const [action,button] of [['play','打出'],['attack','进攻'],['ability','发动']]){
   await load(profile.viewport,'field=full&hand=5&action-fixture=1&rankedClock=1',profile.insets)
   if(action==='play')await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap.playable').first().click();else await page.locator('.my-half .formation-slot .card-tile').first().click()
   const dock=await assertDock(`${profile.name}-${action}`,button,profile.insets);await assertSafe(`${profile.name}-${action}`,profile.insets);const file=`safe-${profile.name}-action-${action}-${profile.viewport.width}x${profile.viewport.height}.png`;await capture(file)
   if(action==='attack'){await dock.click();const cancel=page.locator('.board-mode-hint').getByRole('button',{name:'取消',exact:true});await cancel.waitFor();await cancel.click();assert.equal(await page.locator('.board-mode-hint').count(),0)}
   manifest.actions.push({action,profile:profile.name,viewport:`${profile.viewport.width}x${profile.viewport.height}`,file,safeArea:true,assertions:'passed'})
  }
  for(const mode of ['view','payment']){
   const query=mode==='view'?'field=full&hand=10&runes=5':'field=full&hand=10&runes=5&morale-payment=1&rune-usable=1'
   await load(profile.viewport,query,profile.insets);if(mode==='payment'&&await page.locator('.prompt-minimize').count())await page.locator('.prompt-minimize').click();await page.locator('.resource-morale-summary').last().click();const overlay=page.locator('.mobile-morale-overlay');await overlay.waitFor()
   const runeButtons=overlay.locator('.mobile-rune-row .mobile-morale-choice');assert.equal(await runeButtons.count(),5);await assertRoundChoices(`${profile.name} ${mode} runes`,runeButtons);await assertRoundChoices(`${profile.name} ${mode} morale`,overlay.locator('.mobile-resource-row .mobile-morale-choice'));assert.equal(await overlay.locator('.mobile-morale-choice span,.mobile-morale-choice b,.mobile-morale-choice small').count(),0)
   const unavailable=overlay.locator('.mobile-rune-row .mobile-morale-choice[aria-disabled="true"]').first();await unavailable.click({force:true});await overlay.locator('.mobile-morale-reason').waitFor();assert.equal(await unavailable.getAttribute('aria-pressed'),'false')
   if(mode==='payment'){const usable=overlay.locator('.mobile-rune-row .mobile-morale-choice[aria-disabled="false"]').first();await usable.click();assert.equal(await usable.getAttribute('aria-pressed'),'true')}
   await assertSafe(`${profile.name}-${mode}-morale`,profile.insets);const file=`safe-${profile.name}-morale-${mode}-${profile.viewport.width}x${profile.viewport.height}.png`;await capture(file);manifest.moraleSafe.push({profile:profile.name,mode,viewport:`${profile.viewport.width}x${profile.viewport.height}`,file,iconOnly:true,reasonSingleLine:true,assertions:'passed'})
  }
 }
 manifest.status='passed';fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2));console.log(JSON.stringify({output,status:manifest.status,candidates:manifest.candidates.length,runes:manifest.runes.length,moraleSafe:manifest.moraleSafe.length,actions:manifest.actions.length},null,2))
}finally{await browser.close()}
