import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..')
const {chromium}=createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out=process.env.L12_QA_OUT||'D:/GPT/Legion12/artifacts/batch262-maintenance'
fs.mkdirSync(out,{recursive:true})
const entry=`
import {createApp,h,ref} from 'vue'
import Panel from '/src/l12/site/ImmediateMaintenancePanel.vue'
import Ticker from '/src/l12/site/MaintenanceTicker.vue'
import {maintenanceCountdown} from '/src/l12/site/maintenanceCountdown.ts'
import '/src/style.css'
window.__countdown=maintenanceCountdown
const mounted=ref(true);window.__unmount=()=>mounted.value=false
createApp({render:()=>mounted.value?h('main',{style:'max-width:1000px;margin:24px auto;padding:12px'},[h(Ticker),h(Panel)]):h('p','已离开对战区域')}).mount('#app')
`
let browser
const server=await createServer({root,server:{host:'127.0.0.1',port:0},plugins:[{name:'maintenance-fixture',resolveId(id){if(id==='/__maintenance__.js')return id},load(id){if(id==='/__maintenance__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url==='/__maintenance__'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__maintenance__.js"></script>');return}next()})}}]})
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage({viewport:{width:1280,height:900}})
 await page.clock.install()
 const errors=[];page.on('pageerror',e=>errors.push(e.message))
 let version=10,active=false,hours=2,scheduled=false,oldBackend=false,readFails=false,failCommand=false,policyReads=0
 const posts=[]
 const config=()=>({version,versionId:'synthetic-'+version,updatedBy:'合成管理员',updatedAt:new Date().toISOString(),config:{maintenance:{enabled:scheduled,message:'预约计划',startsAt:'2026-01-01T00:00:00Z',endsAt:null}},...(!oldBackend?{immediateMaintenance:{enabled:active,expectedDurationHours:hours}}:{})})
 const policy=()=>({version,maintenance:{enabled:active||scheduled,active:active||scheduled,entryBlocked:active||scheduled,status:active||scheduled?'maintenance':'open',immediateActive:active,immediateExpectedDurationHours:hours,broadcastMessage:active?'当前服务器维护中，预计维护时间为'+hours+'小时。':'',message:'预约计划',startsAt:'2026-01-01T00:00:00Z',endsAt:null,expectedDurationHours:2,advanceBroadcastHours:2}})
 await page.route('**/*',async route=>{
  const url=new URL(route.request().url())
  if(url.hostname!=='127.0.0.1')return route.abort()
  if(url.pathname==='/api/admin/operations/config')return route.fulfill({json:config()})
  if(url.pathname==='/api/operations/effective-policy'){policyReads++;return readFails?route.abort():route.fulfill({json:policy()})}
  if(url.pathname.startsWith('/api/admin/operations/server/maintenance')){
   posts.push({path:url.pathname,body:route.request().postDataJSON()})
   if(failCommand)return route.fulfill({status:409,json:{error:'operations_version_conflict',message:'配置版本已变化'}})
   if(url.pathname.endsWith('/end'))active=false
   else {active=true;hours=posts.at(-1).body.expectedDurationHours}
   version++;return route.fulfill({json:{applied:true,alreadyApplied:false,current:config()}})
  }
  if(url.pathname.startsWith('/api/'))return route.fulfill({json:[]})
  return route.continue()
 })
 const url='http://127.0.0.1:'+server.httpServer.address().port+'/__maintenance__'
 await page.goto(url)
 const begin=page.getByRole('button',{name:'维护服务器',exact:true}),end=page.getByRole('button',{name:'结束即时维护',exact:true})
 await begin.waitFor();await page.waitForFunction(()=>!document.querySelector('.maintenance-begin').disabled)
 const input=page.getByRole('spinbutton')
 for(const invalid of ['0','169','1.5']){await input.fill(invalid);assert(await begin.isDisabled())}
 await input.fill('10');await begin.evaluate(b=>{b.click();b.click()})
 await page.locator('.maintenance-result').filter({hasText:'即时维护已开启'}).waitFor()
 assert.equal(posts.length,1);assert.equal(posts[0].body.expectedDurationHours,10);assert.equal(posts[0].body.expectedVersion,10)
 assert(!('config' in posts[0].body),'Immediate operation must not serialize unrelated edits')
 await page.locator('.maintenance-ticker').waitFor()
 assert((await page.locator('.maintenance-ticker').textContent()).includes('当前服务器维护中，预计维护时间为10小时。'))
 assert(await begin.isDisabled());assert(await input.isDisabled())
 const view=await page.evaluate(()=>window.__countdown({enabled:true,immediateActive:true,immediateExpectedDurationHours:10,message:'旧计划',broadcastMessage:'即时维护',startsAt:'2099-01-01',endsAt:'2000-01-01'}))
 assert.equal(view.phase,'active');assert(view.countdown.includes('进行中对局可继续'))
 await page.screenshot({path:path.join(out,'immediate-desktop.png')})
 await page.setViewportSize({width:390,height:900});await page.screenshot({path:path.join(out,'immediate-mobile.png')})
 assert(await page.locator('.immediate-maintenance-actions').evaluate(e=>e.scrollWidth<=e.clientWidth+1))
 readFails=true;await page.clock.runFor(5100);assert(await page.locator('.maintenance-ticker').isVisible(),'Transient fetch failure hid confirmed notice')
 readFails=false;scheduled=true;await end.click();await page.locator('.maintenance-result').filter({hasText:'预约维护仍在生效'}).waitFor()
 scheduled=false;active=true;version++;await page.clock.runFor(5100);await page.locator('.maintenance-ticker').filter({hasText:'10小时'}).waitFor()
 active=false;version++;await page.clock.runFor(5100);await page.waitForFunction(()=>!document.querySelector('.maintenance-ticker'))
 failCommand=true;await page.getByRole('button',{name:'刷新状态'}).click();await begin.click();await page.locator('.maintenance-result.failed').waitFor();assert(!(await end.isEnabled()))
 oldBackend=true;failCommand=false;await page.reload();await page.getByText('当前后端尚不支持即时维护').waitFor();assert(await begin.isDisabled())
 const before=policyReads;await page.evaluate(()=>window.__unmount());await page.clock.runFor(11000);assert.equal(policyReads,before,'Ticker polling leaked after unmount')
 assert.equal(errors.length,0,errors.join('\n'))
 fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({passed:true,posts:posts.map(p=>({path:p.path,expectedVersion:p.body.expectedVersion})),policyReads},null,2))
 console.log('Maintenance UI passed: dedicated commands, duration bounds, double-click, plan preservation, stale/error/old backend, cross-page polling, read failure, unmount and narrow layout.')
}finally{await browser?.close();await server.close()}
