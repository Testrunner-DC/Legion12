import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..'), repo = path.resolve(root, '..')
const out = process.env.L12_START_CHECK_OUT, cache = process.env.L12_START_CHECK_CACHE
assert(out && cache && process.env.L12_PWSH && process.env.TEMP && process.env.TMP && process.env.TMPDIR, 'guarded D output, cache and initialized temp required')
execFileSync(process.env.L12_PWSH, ['-NoProfile','-NonInteractive','-Command', '$ErrorActionPreference="Stop"; . $env:L12_GUARD; @($env:L12_TEST_OUT,$env:L12_TEST_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) | ForEach-Object { Assert-L12GeneratedPath $_ | Out-Null }'], {env:{...process.env,L12_GUARD:path.join(repo,'ops/windows/L12GeneratedStorage.ps1'),L12_TEST_OUT:out,L12_TEST_CACHE:cache},windowsHide:true})
assert(!fs.existsSync(out), 'do not overwrite evidence'); fs.mkdirSync(out,{recursive:true})
const require=createRequire(import.meta.url), {chromium}=require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry=String.raw`
import {createApp,h,reactive,ref} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
const platform=await import('/src/l12/platform.ts')
platform.platformState.account={id:'owner',username:'主办',role:'player'};platform.platformState.token='token-a'
platform.friendApi.friends=async()=>[]
platform.friendApi.presence=async()=>[]
platform.alternateArtApi.notifications=async()=>[]
platform.seasonSummaryApi.notifications=async()=>[]
let requests=[], starts=0
platform.tournamentApi.startCheck=id=>new Promise((resolve,reject)=>requests.push({id,resolve,reject}))
platform.tournamentApi.start=async()=>{starts++;return {...state.tournament,status:'running'}}
const [{default:Panel},{default:SiteShell}]=await Promise.all([import('/src/l12/site/TournamentManagementPanel.vue'),import('/src/l12/site/SiteShell.vue')])
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:{},meta:{section:'battle'}}]});await router.push('/event/a');await router.isReady()
const state=reactive({tournament:{id:'a',version:1,status:'registration',phase:'pre-check-in',format:'swiss-cut',swissRounds:3,cutSize:4,referees:[],visibility:'public',registrationVisibility:'public',operationMode:'managed'},accountId:'owner',canOrganize:true})
const show=ref(true)
window.__qa={state,requests,reply:(index,value)=>requests[index].resolve(value),hide:()=>show.value=false,route:()=>router.push('/event/b'),routeAba:async()=>{await router.push('/event/b');await router.push('/event/a')},accountAba:()=>{const old=platform.platformState.account;platform.platformState.account={id:'other'};platform.platformState.account=old},switchAccount:()=>{platform.platformState.account={id:'other'}},switchToken:()=>{platform.platformState.token='token-b'},aba:()=>{platform.platformState.token='other';platform.platformState.token='token-a'},starts:()=>starts}
createApp({render:()=>h(SiteShell,null,{default:()=>show.value?h(Panel,{...state}):h('div','卸载')})}).use(router).mount('#app')
`
const server=await createServer({root,configLoader:'runner',cacheDir:cache,logLevel:'error',server:{host:'127.0.0.1',port:0},plugins:[{name:'start-check-fixture',resolveId(id){if(id==='/__check_entry.js')return id},load(id){if(id==='/__check_entry.js')return entry},configureServer(s){s.middlewares.use('/__check',(_q,r)=>{r.setHeader('Content-Type','text/html');r.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/__check_entry.js"></script>')})}}]})
let browser, page; const checks=[], failures=[]
const result=(overrides={})=>({tournamentVersion:1,eligiblePlayers:4,checkedInWaitlistedPlayers:1,format:'swiss-cut',swissRounds:3,cutSize:4,canStart:true,blockers:[],warnings:[],waitlistedPlayers:2,openJudgeCases:0,...overrides})
try {
 await server.listen();browser=await chromium.launch({channel:'msedge',headless:true});const url='http://127.0.0.1:'+server.httpServer.address().port+'/__check'
 for(const viewport of [{width:390,height:844},{width:1440,height:900}]){
  page=await browser.newPage({viewport});const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto(url);await page.locator('.management-panel').waitFor();
  const check=page.getByRole('button',{name:'执行开赛检查'}), start=page.getByRole('button',{name:'正式开赛'});
  assert(await start.isDisabled());await check.click();await page.evaluate(v=>window.__qa.reply(0,v),result());
  await page.waitForFunction(()=>document.querySelector('.start-check')?.textContent.includes('候补已签到'));
  let text=await page.locator('.start-check').innerText();assert(text.includes('正式已签到 4'));assert(text.includes('候补已签到 1'));assert(text.includes('瑞士轮 3'));assert(text.includes('晋级人数 4'));assert(await start.isEnabled());
  assert.equal(await page.locator('.site-shell .site-content .management-panel').count(),1);await page.locator('.start-check').scrollIntoViewIfNeeded();const box=await page.locator('.start-check').boundingBox();assert(box.x>=0&&box.x+box.width<=viewport.width+1);const layout=await page.locator('.start-check').evaluate(e=>({overflow:e.scrollWidth-e.clientWidth,font:getComputedStyle(e).fontFamily}));assert(layout.overflow<=1);assert(/yahei|微软雅黑|sans-serif/i.test(layout.font));for(const b of [check,start]){const r=await b.boundingBox();assert(r.width>0&&r.height>0);if(viewport.width<=700)assert(r.width>=38&&r.height>=38);assert(await b.evaluate(e=>e.scrollWidth<=e.clientWidth+1))}await page.screenshot({path:path.join(out,'check-'+viewport.width+'.png')});
  await page.evaluate(()=>window.__qa.state.tournament.version=2);assert(await start.isDisabled());
  await check.click();await page.evaluate(()=>window.__qa.switchAccount());await page.evaluate(v=>window.__qa.reply(1,v),result({tournamentVersion:2}));await page.waitForTimeout(50);assert(await start.isDisabled());assert.equal(await page.evaluate(()=>window.__qa.starts()),0);assert.deepEqual(errors,[]);checks.push({viewport,box});await page.close();page=null
 }
 for(const variant of ['version','event','permission','token-aba','account-aba','route-aba','unmount','concurrent','legacy','single']){
  page=await browser.newPage({viewport:{width:390,height:844}});await page.goto(url);await page.locator('.management-panel').waitFor()
  const check=page.getByRole('button',{name:'执行开赛检查'}), start=page.getByRole('button',{name:'正式开赛'})
  await check.click()
  if(variant==='version')await page.evaluate(()=>window.__qa.state.tournament.version=2)
  if(variant==='event')await page.evaluate(()=>window.__qa.state.tournament={...window.__qa.state.tournament,id:'b'})
  if(variant==='permission')await page.evaluate(()=>window.__qa.state.canOrganize=false)
  if(variant==='token-aba')await page.evaluate(()=>window.__qa.aba())
  if(variant==='account-aba')await page.evaluate(()=>window.__qa.accountAba())
  if(variant==='route-aba')await page.evaluate(()=>window.__qa.routeAba())
  if(variant==='unmount')await page.evaluate(()=>window.__qa.hide())
  if(variant==='concurrent'){
   await check.click();await page.evaluate(v=>window.__qa.reply(1,v),result({eligiblePlayers:7}));await page.waitForFunction(()=>document.querySelector('.start-check')?.textContent.includes('正式已签到 7'))
   await page.evaluate(v=>window.__qa.reply(0,v),result({eligiblePlayers:2}));await page.waitForTimeout(30)
   assert((await page.locator('.start-check').innerText()).includes('正式已签到 7'));assert(await start.isEnabled())
  }else if(variant==='single'){
   await page.evaluate(v=>window.__qa.reply(0,v),result({format:'single',swissRounds:null,cutSize:null}));await page.waitForFunction(()=>document.querySelector('.start-check'))
   const text=await page.locator('.start-check').innerText();assert(!text.includes('瑞士轮'));assert(!text.includes('晋级人数'));assert(await start.isEnabled())
  }else{
   const v=variant==='legacy'?{canStart:true,eligiblePlayers:2,blockers:[],warnings:[]}:result()
   await page.evaluate(v=>window.__qa.reply(0,v),v);await page.waitForTimeout(40)
   assert.equal(await page.locator('.start-check').count(),0,variant+' ignores obsolete/incomplete reply')
   if(!['permission','unmount'].includes(variant))assert(await start.isDisabled(),variant+' prevents start')
  }
  assert.equal(await page.evaluate(()=>window.__qa.starts()),0);checks.push({variant,passed:true});await page.close();page=null
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({passed:true,checks,fixture:'actual SiteShell and management SFC with synthetic API, no App or real WebSocket',realMobile:false},null,2));console.log(JSON.stringify({passed:true,checks}))
}catch(e){if(page)await page.screenshot({path:path.join(out,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(out,'failure.json'),JSON.stringify({error:String(e),checks},null,2));throw e}finally{await browser?.close();await server.close()}