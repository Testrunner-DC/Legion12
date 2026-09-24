import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require=createRequire(import.meta.url)
const {chromium}=require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const browser=await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE?{headless:true,executablePath:process.env.L12_CHROMIUM_EXECUTABLE}:{headless:true,channel:'msedge'})
const page=await browser.newPage(), cdp=await page.context().newCDPSession(page)
const target=process.argv[2] || 'http://127.0.0.1:5197/__l12_battle_preview__'
const out=path.resolve('../artifacts/mobile-action-dock/overlays')
fs.mkdirSync(out,{recursive:true})
const results=[],errors=[]
page.on('pageerror',e=>errors.push(e.message))
page.setDefaultTimeout(8000)
await page.route('**/*',r=>['127.0.0.1','localhost'].includes(new URL(r.request().url()).hostname)?r.continue():r.abort())
let label=''
async function load(query=''){
 await page.goto(`${target}?canvas=1&mobile=1&field=full&hand=10&modalFixture=1&rankedClock=1&${query}`,{waitUntil:'domcontentloaded'})
 await page.locator('.mobile-battle-dock').waitFor()
}
async function check(panel,name){
 await panel.waitFor()
 const report=await panel.evaluate(e=>{
  const r=e.getBoundingClientRect(),v=document.querySelector('#l12-landscape-teleports').getBoundingClientRect()
  return {inside:r.left>=v.left-1&&r.top>=v.top-1&&r.right<=v.right+1&&r.bottom<=v.bottom+1,overflow:document.documentElement.scrollWidth>innerWidth+1||document.documentElement.scrollHeight>innerHeight+1}
 })
 assert.equal(report.inside,true,`${label} ${name} outside safe canvas`);assert.equal(report.overflow,false)
 await page.screenshot({path:path.join(out,`${label}-${name}.png`)})
 results.push({label,name,...report})
}
async function hit(locator){
 for(let i=0;i<await locator.count();i++){
  const e=locator.nth(i);if(!await e.isVisible())continue
  await e.scrollIntoViewIfNeeded()
  assert.ok(await e.evaluate(e=>{const r=e.getBoundingClientRect(),h=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return h===e||e.contains(h)}),`intercepted ${await e.textContent()}`)
  if(await e.isEnabled())await e.click({trial:true})
 }
}
try{
 for(const [width,height] of [[740,360],[844,390],[1024,600],[1024,768]]){
  label=`${width}x${height}`;await page.setViewportSize({width,height})
  if(process.env.L12_CHROMIUM_EXECUTABLE)await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets:{left:44,right:44,top:0,bottom:21}})
  await load()
  assert.equal(await page.locator('.bug-feedback-trigger').isVisible(),false)
  await page.getByRole('button',{name:'打开对局设置',exact:true}).click()
  await check(page.locator('.l12-settings-modal'),'settings');await hit(page.locator('.l12-settings-modal button,.l12-settings-modal select'))
  await page.getByRole('button',{name:'关闭设置',exact:true}).click()
  await page.getByRole('button',{name:'打开好友功能',exact:true}).click()
  await check(page.locator('.friends-shell'),'friends');await page.getByRole('button',{name:'关闭好友功能',exact:true}).click()
  await page.getByRole('button',{name:'打开对局工具',exact:true}).click()
  await check(page.locator('.tools-dialog'),'tools');await hit(page.locator('.tools-dialog button'))
  await page.locator('.tools-dialog').getByRole('button',{name:/Bug反馈/}).click()
  await check(page.locator('.bug-feedback-dialog'),'feedback')
  const input=page.locator('.bug-feedback-dialog textarea').first()
  await input.fill('本地合成验收，不提交。');await input.focus()
  await page.setViewportSize({width,height:height-40})
  assert.equal(await input.inputValue(),'本地合成验收，不提交。');assert.equal(await input.evaluate(e=>e===document.activeElement),true)
  await check(page.locator('.bug-feedback-dialog'),'feedback-toolbar-resize')
  await hit(page.locator('.bug-feedback-dialog footer button'))
  await page.locator('.bug-feedback-dialog').getByRole('button',{name:'取消',exact:true}).click()
  await page.setViewportSize({width,height})
  await page.locator('.mini-master').last().click();await page.locator('.master-minimize').click()
  await page.locator('.graveyard').last().click();await page.getByRole('button',{name:'最小化墓地'}).click()
  await page.locator('.resource-faction-action').last().click();await page.locator('.faction-minimize').click()
  assert.equal(await page.locator('.mobile-battle-dock__context .mobile-safe-overlay.minimized').count(),3)
  await hit(page.locator('.mobile-battle-dock__context button'));await check(page.locator('.mobile-battle-dock'),'three-restores')
  await page.locator('.master-minimized button').click();await page.locator('.master-close').click()
  await page.locator('.graveyard-minimized button').click();await page.getByRole('button',{name:'关闭墓地'}).click()
  await page.locator('.faction-minimized-bar button').click();await page.locator('.faction-close').click()
  await load('gm=1');await check(page.locator('.gm-panel'),'gm');await page.locator('.gm-panel>header button').click()
  await hit(page.locator('.gm-open'));await page.locator('.gm-open').click();await page.locator('.gm-panel>header button').click()
  await load('spectator=1');assert.equal(await page.locator('.action-panel').count(),0);assert.equal(await page.locator('.surrender').count(),0)
  await hit(page.locator('.mobile-battle-dock button'));await check(page.locator('.mobile-battle-dock'),'spectator')
  await load('disaster-chain=1');await page.evaluate(()=>window.__disasterFixture.openPrompt());await check(page.locator('.prompt-panel'),'disaster')
  await page.locator('.prompt-minimize').click();await hit(page.locator('.mobile-battle-dock button'))
  await load('board-slot=1&field=empty');await page.locator('.formation-slot.available').first().click();await check(page.locator('.mobile-battle-dock'),'slot')
  await load('effect=cost2');await check(page.locator('.prompt-panel'),'cost-alternatives');await hit(page.locator('.prompt-panel button'))
  await load('scout=1');await check(page.locator('.prompt-panel'),'library-scout');await hit(page.locator('.prompt-panel button'))
  console.log(`${label} overlays passed`)
 }
 label='transitions';await page.setViewportSize({width:844,height:390});await load('action-fixture=1')
 await page.locator('.my-half .formation-slot .card-tile').first().click()
 for(const size of [{width:1920,height:1080},{width:844,height:390},{width:1920,height:600},{width:780,height:360}]){
  await page.setViewportSize(size);await page.waitForTimeout(100)
  assert.equal(await page.locator('.battle-utility-dock').count(),1)
  assert.equal(await page.locator('.card-context-actions').count(),1)
  await hit(page.locator('.card-context-actions button'))
 }
 await page.evaluate(()=>{const state=window.__battleDockFixture.state;state.game.waitingPrompt={playerIndex:1,playerName:'合成对手',kind:'response'};state.rankedClock.players[1].connected=false;state.rankedClock.players[1].reconnectRemainingMs=45000})
 await check(page.locator('.waiting-panel'),'waiting-disconnected');await page.locator('.prompt-minimize').click()
 await page.getByRole('button',{name:'打开对局工具',exact:true}).click();await check(page.locator('.tools-dialog'),'waiting-tools');await page.getByRole('button',{name:'关闭对局工具',exact:true}).click()
 await page.evaluate(()=>{const state=window.__battleDockFixture.state;state.game.waitingPrompt=null;state.rankedClock.players[1].connected=true})
 await page.locator('.my-half .formation-slot .card-tile').first().click();await hit(page.locator('.mobile-battle-dock button'))
 assert.deepEqual(errors,[])
}catch(e){errors.push(e.stack);await page.screenshot({path:path.join(out,`${label}-failure.png`)});throw e}
finally{fs.writeFileSync(path.join(out,'manifest.json'),JSON.stringify({status:errors.length?'failed':'passed',results,errors},null,2));await browser.close()}
