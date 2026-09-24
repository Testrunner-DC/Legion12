import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/admin-profile-p0')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })
// Reuse the existing approved responsive fixture; these are explicitly test identities,
// never production evidence or data inserted into a backend.
const previous = fs.readFileSync(path.join(root, 'scripts/verify-profile-admin-responsive.mjs'), 'utf8')
const fixture = previous.split('const entry = `')[1].split('const mode=new URLSearchParams')[0]
const entry = fixture.replace('createMemoryHistory,createRouter', 'createWebHistory,createRouter') + `
const role=new URLSearchParams(location.search).get('qaRole')||'admin'
if(role==='guest'){platform.platformState.account=null;platform.platformState.token='';localStorage.clear()}
if(role==='player') platform.platformState.account={...platform.platformState.account,role:'player',permissions:[]}
window.__qa={calls:[],active:0,peak:0,fail:new URLSearchParams(location.search).get('qaFail')||'',delay:30}
const track=(name,fn)=>async(...args)=>{
 const qa=window.__qa;const call=name==='statistics'?name+':'+(args[0]||'season'):name;qa.calls.push(call);qa.active++;qa.peak=Math.max(qa.peak,qa.active)
 try {await new Promise(resolve=>setTimeout(resolve,qa.delay));if(qa.fail===name||qa.fail===call)throw new Error('验收夹具：请求失败');return await fn(...args)} finally {qa.active--}
}
for(const [object,key,name] of [[platform.playerApi,'statistics','statistics'],[platform.rankedApi,'overview','ranked'],[platform.sessionApi,'list','sessions'],[platform.usernameChangeApi,'status','rename'],[platform.adminApi,'accounts','accounts'],[platform.adminApi,'bugs','bugs']])object[key]=track(name,object[key])
platform.alternateArtApi.mine=track('arts',async()=>[])
platform.adminApi.revokeSessions=track('revoke',async()=>{throw new Error('验收夹具：撤销请求失败，请重试')})
const integrity=await import('/src/l12/rankedIntegrity.ts')
integrity.integrityApi.notifications=track('notifications',async()=>({items:[]}))
integrity.integrityApi.appeals=track('appeals',async()=>({items:[]}))
window.__qaPlatform=platform
const Profile=(await import('/src/l12/site/ProfilePage.vue')).default
const Admin=(await import('/src/l12/site/AdminPage.vue')).default
const router=createRouter({history:createWebHistory(),routes:[{path:'/me',component:Profile},{path:'/admin',component:Admin},{path:'/:pathMatch(.*)*',component:{render:()=>null}}]})
window.__qaRouter=router
await router.isReady().catch(()=>{})
`
// router.isReady must run after installation starts its initial navigation.
const boot = entry.replace('await router.isReady().catch(()=>{})', `
const app=createApp({render:()=>h('main',{class:'site-content'},[h(RouterView)])})
app.use(router);await router.isReady();app.mount('#app')`)
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp/vite-admin-profile-p0'),server:{host:'127.0.0.1',port:0},plugins:[{
 name:'admin-profile-p0-acceptance',resolveId(id){if(id==='/__p0__.js')return id},load(id){if(id==='/__p0__.js')return boot},
 configureServer(dev){dev.middlewares.use((req,res,next)=>{
  if(/^\/(me|admin)(\?|$)/.test(req.url||'')){res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body,#app{margin:0;background:#080d11;color:#eee;height:100%;}.site-content{position:absolute;inset:0;overflow:auto}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__p0__.js"></script>');return}next()
 })}
}]})
const viewports=[{width:1440,height:900},{width:1280,height:720},{width:390,height:844},{width:844,height:390}]
let browser
const report=[];const errors=[]
const navigationProbe={profile:false,admin:false,reproduced:false}
try{
 await server.listen();const base='http://127.0.0.1:'+server.httpServer.address().port
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage()
 page.on('pageerror',error=>errors.push(error.message))
 await page.route('**/*',async route=>{
  const url=new URL(route.request().url())
  if(url.hostname!=='127.0.0.1')return route.abort()
  if(url.pathname==='/api/matches'||url.pathname==='/api/auth/mfa/capability'){
   await page.evaluate(name=>{const qa=window.__qa;qa.calls.push(name);qa.active++;qa.peak=Math.max(qa.peak,qa.active)},url.pathname==='/api/matches'?'matches':'mfa')
   await new Promise(resolve=>setTimeout(resolve,30))
   await page.evaluate(()=>window.__qa.active--)
   return route.fulfill({json:url.pathname==='/api/matches'?[]:{enrollmentEnabled:false}})
  }
  if(url.pathname.startsWith('/api/'))return route.fulfill({status:404,json:{message:'验收夹具没有配置此接口'}})
  return route.continue()
 })
 async function settled(){await page.waitForFunction(()=>window.__qa&&window.__qa.active===0&&!document.body.innerText.includes('正在加载当前分区'));await page.waitForTimeout(90)}
 async function evidence(name){
  await settled()
  const geometry=await page.evaluate(()=>{const el=document.querySelector('.site-content');return{overflow:el.scrollWidth>el.clientWidth+1,windowOverflow:document.documentElement.scrollWidth>innerWidth+1}})
  assert(!geometry.overflow&&!geometry.windowOverflow, name+' overflows')
  const counts=await page.evaluate(()=>({calls:window.__qa.calls,peak:window.__qa.peak}))
  const screenshot=path.join(output,name+'.png');await page.screenshot({path:screenshot})
  report.push({name,...geometry,...counts,screenshot});return counts
 }
 for(const viewport of viewports){
  await page.setViewportSize(viewport);const size=viewport.width+'x'+viewport.height
  for(const section of ['overview','performance','collection','security']){
   await page.goto(base+'/me?qaRole=guest&section='+section);await page.locator('.auth-form').waitFor();const counts=await evidence('guest-'+section+'-'+size);assert.equal(counts.calls.length,0)
  }
  for(const [section,names] of [['overview',['ranked','statistics:season']],['performance',['statistics:season','notifications','appeals']],['collection',['arts','ranked']],['security',['sessions','rename','mfa']]]){
   await page.goto(base+'/me?qaRole=player&section='+section);await page.locator('.profile-section-nav').waitFor();const counts=await evidence('player-'+section+'-'+size);assert.deepEqual([...counts.calls].sort(),[...names].sort());assert(counts.peak<=3)
   assert.equal(await page.locator('.admin-button').count(),0)
  }
  await page.goto(base+'/me?qaRole=player&section=performance&range=season');await settled()
  assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'128')
  await page.getByRole('button',{name:'近 7 天',exact:true}).click();await settled();assert(page.url().includes('range=7d'))
  assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'7')
  assert((await page.locator('.master-records article').first().textContent()).includes('整体 3胜 1负 0平'))
  await evidence('performance-7d-'+size)
  await page.getByRole('button',{name:'近 30 天',exact:true}).click();await settled();assert(page.url().includes('range=30d'))
  assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'30')
  assert((await page.locator('.master-records article').first().textContent()).includes('整体 10胜 6负 0平'))
  await evidence('performance-30d-'+size)
  await page.goBack();await settled();assert(page.url().includes('range=7d'));assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'7')
  await page.goForward();await settled();assert(page.url().includes('range=30d'));assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'30')
  await page.goto(base+'/admin?qaRole=admin&section=overview');await page.locator('.admin-shell').waitFor();assert.equal((await evidence('admin-overview-'+size)).calls.length,0)
  assert.equal(await page.locator('.overview-card').count(),4)
  await page.goto(base+'/admin?qaRole=admin&section=releases');await page.getByText('无权访问此模块').waitFor();assert.equal((await evidence('admin-denied-'+size)).calls.length,0)
  assert.equal(await page.locator('.admin-mobile-navigation option[value=releases]').count(),0)
  await page.goto(base+'/admin?qaRole=player&section=accounts');await page.getByText('需要管理员权限').waitFor();assert.equal((await evidence('player-admin-denied-'+size)).calls.length,0)
  await page.goto(base+'/admin?qaRole=admin&section=accounts');await page.locator('.account-row .revoke').first().waitFor();assert.deepEqual((await evidence('admin-accounts-'+size)).calls,['accounts'])
  const trigger=page.locator('.account-row .revoke').first();await trigger.click();await page.locator('dialog[open]').waitFor();await evidence('risk-dialog-'+size)
  for(let i=0;i<5;i++){await page.keyboard.press('Tab');assert(await page.evaluate(()=>document.querySelector('dialog').contains(document.activeElement)))}
  await page.keyboard.press('Escape');assert.equal(await page.locator('dialog').count(),0);assert(await trigger.evaluate(el=>el===document.activeElement))
  await trigger.click();await page.evaluate(()=>window.__qa.delay=250);await page.getByRole('button',{name:'撤销全部会话',exact:true}).evaluate(el=>{el.click();el.click()});await page.keyboard.press('Escape');assert.equal(await page.locator('dialog[open]').count(),1);await page.getByRole('alert').filter({hasText:'验收夹具：撤销请求失败'}).waitFor();assert.equal(await page.locator('dialog[open]').count(),1);await page.getByRole('button',{name:'取消',exact:true}).click();assert.equal(await page.evaluate(()=>window.__qa.calls.filter(x=>x==='revoke').length),1);await page.evaluate(()=>window.__qa.delay=30)
  await page.evaluate(()=>window.__qaRouter.push('/admin?section=bugs'));await page.getByRole('heading',{name:'Bug 反馈',exact:true}).waitFor();await settled();await page.goBack();await page.locator('.account-row .revoke').first().waitFor();await settled();assert.equal(await page.evaluate(()=>window.__qa.calls.filter(x=>x==='accounts').length),1)
  await page.goto(base+'/me?qaRole=player&section=security');await settled();await page.locator('.session-manager').evaluate(el=>el.open=true)
  await page.mouse.move(viewport.width/2,Math.min(500,viewport.height-50));await page.mouse.wheel(0,180);await page.waitForTimeout(150)
  const top=await page.locator('.site-content').evaluate(el=>el.scrollTop)
  const navBox=await page.getByRole('button',{name:'总览',exact:true}).boundingBox();await page.mouse.click(navBox.x+navBox.width/2,navBox.y+navBox.height/2)
  await settled();await page.goBack();await settled();assert(await page.locator('.site-content').evaluate((el,expected)=>Math.abs(el.scrollTop-Math.min(expected,el.scrollHeight-el.clientHeight))<=1,top),'back/clamped scroll '+size)
  await page.reload();await settled();assert(await page.locator('.site-content').evaluate((el,expected)=>Math.abs(el.scrollTop-Math.min(expected,el.scrollHeight-el.clientHeight))<=1,top),'reload/clamped scroll '+size)
  await page.goForward();await settled();assert(page.url().includes('section=overview'));await evidence('history-restored-'+size)
  assert.equal(await page.evaluate(()=>window.__qa.calls.filter(x=>x==='sessions').length),1)
 }
 await page.goto(base+'/me?qaRole=player&section=security&qaFail=sessions');await settled()
 assert(await page.getByRole('alert').filter({hasText:'验收夹具：请求失败'}).isVisible())
 await page.evaluate(()=>window.__qa.fail='');await page.getByRole('button',{name:'重试',exact:true}).click();await settled()
 assert.equal(await page.evaluate(()=>window.__qa.calls.filter(x=>x==='sessions').length),2)
 assert.equal(await page.evaluate(()=>window.__qa.calls.filter(x=>x==='rename').length),1)
 await page.goto(base+'/me?qaRole=player&section=collection');await settled()
 await page.evaluate(()=>{window.__qa.delay=150;window.__qa.calls=[];window.__qa.peak=0;window.__qaRouter.push('/me?section=security')})
 await page.waitForFunction(()=>window.__qa.active>0)
 await page.evaluate(async()=>{await window.__qaRouter.push('/me?section=overview');await window.__qaRouter.push('/me?section=collection')});await settled()
 assert((await page.evaluate(()=>window.__qa.peak))<=3,'rapid section transitions exceed budget')
 assert.equal(await page.evaluate(()=>window.__qa.calls.includes('statistics:season')),false,'abandoned queued section must not load')
 await page.evaluate(()=>{window.__qaRouter.push('/me?section=overview')});await page.waitForFunction(()=>window.__qa.active>0)
 await page.evaluate(()=>{window.__qaPlatform.platformState.account=null;window.__qaPlatform.platformState.token=''})
 await settled();await page.locator('.auth-form').waitFor();assert.equal(await page.locator('.identity,.performance-panel,.profile-grid').count(),0)
 await evidence('account-switch-during-read')
 await page.goto(base+'/me?qaRole=player&qaEmpty=1&section=performance&range=7d');await settled()
 assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'0')
 assert.equal(await page.locator('.master-records article').count(),0);await page.getByText('完成对局后，这里会按主宰展示战绩。').waitFor()
 await evidence('performance-empty')
 await page.goto(base+'/me?qaRole=player&section=performance&range=season');await settled();await page.evaluate(()=>{window.__qa.delay=250})
 await page.getByRole('button',{name:'近 7 天',exact:true}).click();await page.waitForFunction(()=>window.__qa.active>0)
 await page.getByRole('button',{name:'近 30 天',exact:true}).click();await settled()
 assert(page.url().includes('range=30d'));assert.equal(await page.locator('.stats article').first().locator('b').textContent(),'30')
 assert((await page.locator('.master-records article').first().textContent()).includes('整体 10胜 6负 0平'))
 await evidence('performance-slow-range-switch');await page.evaluate(()=>{window.__qa.delay=30})
 await page.goto(base+'/me?qaRole=player&section=overview');await settled();await page.getByRole('button',{name:'收藏与偏好',exact:true}).click()
 await page.waitForURL('**/me?*section=collection*');await page.locator('.profile-grid').waitFor();navigationProbe.profile=true
 await page.setViewportSize({width:1440,height:900});await page.goto(base+'/admin?qaRole=admin&section=overview');await settled()
 await page.locator('.admin-sidebar button').filter({hasText:'账号与会话'}).click();await page.waitForURL('**/admin?section=accounts');await page.locator('.account-row .revoke').first().waitFor();navigationProbe.admin=true
 navigationProbe.reproduced=!(navigationProbe.profile&&navigationProbe.admin)
 assert.deepEqual(errors,[])
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',evidenceType:'Existing local test fixtures in Microsoft Edge; real account acceptance deferred to Main',viewports,navigationProbe,report,errors},null,2))
 console.log(JSON.stringify({status:'passed',output,screenshots:report.length,errors},null,2))
}finally{await browser?.close();await server.close()}
