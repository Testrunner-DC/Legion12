import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/e1-global-states-${Date.now()}`
assert(!fs.existsSync(output), 'Never overwrite evidence')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const bound = ['src/App.vue', 'src/l12/site/siteUiStateScope.ts', 'src/l12/site/uiSystem.css',
  'src/l12/site/GlobalBugFeedback.vue', 'src/l12/site/FriendRequestNotifications.vue',
  'src/l12/site/RankedIntegrityNotice.vue', 'src/l12/site/RankedAppealForm.vue']
const hashSources = () => Object.fromEntries(bound.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const before = hashSources()
const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
const originalFetch=window.fetch.bind(window)
window.__qa={unexpected:[],diagnosticReads:0,friendWrites:0,integrityWrites:0,bugWrites:0,pending:null,friend:true,integrity:true}
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.endsWith('/health')||url.endsWith('/api/auth/me')){
  window.__qa.diagnosticReads++
  return new Response(JSON.stringify(url.endsWith('/health')?{status:'ok',maintenance:false}:{id:'synthetic-self',username:'本地测试'}),{status:200,headers:{'content-type':'application/json'}})
 }
 if(url.includes('/api/')){
  if(url.endsWith('/api/bugs')&&init?.method==='POST'){
   window.__qa.bugWrites++
   return new Promise(resolve=>window.__qa.pending=()=>resolve(new Response(JSON.stringify({id:'synthetic-report'}),{status:200,headers:{'content-type':'application/json'}})))
  }
  window.__qa.unexpected.push(url);throw Error('Unexpected API request')
 }
 return originalFetch(input,init)
}
const platform=await import('/src/l12/platform.ts')
const integrity=await import('/src/l12/rankedIntegrity.ts')
platform.authState.initialized=true;platform.authState.verified=true
platform.platformState.account={id:'synthetic-self',username:'本地测试'};platform.platformState.token=''
platform.friendApi.overview=async()=>({friends:[],blocked:[],requests:window.__qa.friend?[{accountId:'synthetic-peer',username:'本地申请',direction:'incoming',createdAt:'2026-10-05T00:00:00Z'}]:[]})
platform.friendApi.resolve=async()=>{window.__qa.friendWrites++;await new Promise(resolve=>window.__qa.pending=resolve);window.__qa.friend=false}
platform.friendApi.block=async()=>{throw Error('Unrequested block')}
integrity.integrityApi.notifications=async()=>({items:window.__qa.integrity?[{id:'synthetic-notice',decisionId:'synthetic-decision',outcome:'insufficient',reason:'本地状态测试，不是真实处罚',decidedAt:'2026-10-05T00:00:00Z',scoreDelta:0,appealGuidance:'可查看处理记录',acknowledged:false}]:[]})
integrity.integrityApi.acknowledge=async()=>{window.__qa.integrityWrites++;await new Promise(resolve=>window.__qa.pending=resolve);window.__qa.integrity=false}
const {default:Feedback}=await import('/src/l12/site/GlobalBugFeedback.vue')
const {default:Friends}=await import('/src/l12/site/FriendRequestNotifications.vue')
const {default:Integrity}=await import('/src/l12/site/RankedIntegrityNotice.vue')
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/',component:{render:()=>null}},
 {path:'/admin',meta:{requiresAdmin:true},component:{render:()=>null}},
 {path:'/game',meta:{immersive:true,landscapeCanvas:true},component:{render:()=>null}},
 {path:'/sandbox',meta:{landscapeCanvas:true},component:{render:()=>null}},
 {path:'/deck-editor',meta:{landscapeCanvas:true,editorAdaptiveCanvas:true},component:{render:()=>null}},
 {path:'/me',component:{render:()=>null}}
]})
await router.push('/');await router.isReady()
window.__qa.navigate=path=>router.push(path)
window.__qa.finish=()=>{const finish=window.__qa.pending;window.__qa.pending=null;finish?.()}
window.__qa.mode=async(kind)=>{
 window.__qa.friend=kind==='friend';window.__qa.integrity=kind==='integrity'
 const friends=await import('/src/l12/friendResource.ts')
 await friends.refreshFriendResource()
 window.dispatchEvent(new Event('l12-integrity-changed'))
}
// Same App-level ownership and Teleport destination; the actual three consumers
// are mounted, not rewritten templates. Full App/game geometry is not simulated.
createApp({render:()=>h('main',[h('div',{id:'l12-landscape-teleports'}),h(Feedback),h(Friends),h(Integrity)])}).use(router).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(output, 'vite-cache'), logLevel: 'error', server: {host:'127.0.0.1',port:0}, plugins:[{
  name:'site-global-state-fixture',resolveId(id){if(id==='/__global_scope_entry.js')return id},load(id){if(id==='/__global_scope_entry.js')return entry},
  configureServer(s){s.middlewares.use('/__global_states',(_req,res)=>{res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__global_scope_entry.js"></script></body></html>')})},
}]})
let browser,activePage,checks=0
const results=[]
const check=(condition,message)=>{assert(condition,message);checks++}
const rect=locator=>locator.evaluate(e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}})
const focus=async(locator,page)=>{await locator.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');return locator.evaluate(e=>({color:getComputedStyle(e).outlineColor,style:getComputedStyle(e).outlineStyle,width:getComputedStyle(e).outlineWidth}))}
try {
  await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
  const origin=`http://127.0.0.1:${server.httpServer.address().port}`
  for(const viewport of [{width:320,height:568},{width:390,height:844},{width:700,height:768},{width:701,height:768},{width:844,height:390},{width:1920,height:1080}]){
    const page=await browser.newPage({viewport});activePage=page
    const errors=[];page.on('pageerror',e=>errors.push(e.message))
    await page.goto(origin+'/__global_states')
    await page.locator('.friend-request-dialog').waitFor();await page.locator('.integrity-notice').waitFor()
    const globalRoots=['.friend-request-dialog','.integrity-notice']
    for(const selector of globalRoots){
      const rootNode=page.locator(selector)
      check(await rootNode.evaluate(e=>e.classList.contains('ui-state-scope')),selector+' has ordinary-route states outside Shell')
      const beforeRect=await rect(rootNode)
      await rootNode.evaluate(e=>e.classList.remove('ui-state-scope'))
      assert.deepEqual(await rect(rootNode),beforeRect,'state-only adoption preserves root geometry');checks++
      await rootNode.evaluate(e=>e.classList.add('ui-state-scope'))
      const focused=await focus(rootNode.locator('button').first(),page)
      check(focused.color==='rgb(85, 199, 206)'&&focused.style==='solid'&&focused.width==='2px',selector+' actual keyboard focus shares state')
      check(await rootNode.getAttribute('aria-modal')==='false',selector+' remains non-modal')
    }
    check(await page.locator('.bug-feedback-trigger').count()===0,'global floating feedback trigger is absent')
    await page.screenshot({path:path.join(output,`notifications-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.mode('friend'));await page.locator('.integrity-notice').waitFor({state:'detached'})
    const friend=page.locator('.friend-request-dialog')
    await friend.getByRole('button',{name:'拒绝',exact:true}).click()
    await friend.locator('button:disabled').first().waitFor()
    check(await friend.locator('button:disabled').count()===3,'all same-level request buttons busy')
    check(await friend.locator('button').evaluateAll(es=>es.every(e=>getComputedStyle(e).backgroundColor==='rgb(21, 29, 34)'&&getComputedStyle(e).opacity==='1')),'friend busy state including colored accept')
    await friend.locator('button').evaluateAll(es=>es.forEach(e=>e.click()))
    check(await page.evaluate(()=>window.__qa.friendWrites)===1,'native disabled request actions cannot duplicate')
    await page.screenshot({path:path.join(output,`friend-busy-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.finish());await friend.waitFor({state:'detached'})
    await page.evaluate(()=>window.__qa.mode('integrity'));await page.locator('.integrity-notice').waitFor()
    const notice=page.locator('.integrity-notice')
    await notice.getByRole('button',{name:'我要申诉',exact:true}).click()
    const emptyAppeal=notice.getByRole('button',{name:'提交申诉',exact:true})
    check(await emptyAppeal.isDisabled(),'empty appeal preserves native validation')
    check(await emptyAppeal.evaluate(e=>getComputedStyle(e).backgroundColor)==='rgb(21, 29, 34)','nested actual appeal inherits detached states')
    await notice.getByRole('button',{name:'收起申诉',exact:true}).click()
    await notice.getByRole('button',{name:'我知道了',exact:true}).click()
    await notice.getByRole('button',{name:'确认中…',exact:true}).waitFor()
    check(await notice.locator('footer button:disabled').count()===2,'actual acknowledgment retains busy locks')
    await notice.locator('footer button').evaluateAll(es=>es.forEach(e=>e.click()))
    check(await page.evaluate(()=>window.__qa.integrityWrites)===1,'no duplicate acknowledgment')
    await page.evaluate(()=>window.__qa.finish());await notice.waitFor({state:'detached'})
    await page.evaluate(()=>window.dispatchEvent(new Event('l12-open-bug-feedback')))
    const feedback=page.locator('.bug-feedback-dialog');await feedback.waitFor()
    const feedbackRect=await rect(feedback)
    check(feedbackRect.x>=0&&feedbackRect.x+feedbackRect.width<=viewport.width+1,'feedback stays horizontally contained')
    check(feedbackRect.y>=19&&feedbackRect.y+feedbackRect.height<=viewport.height-19,'feedback fits visible height with margin')
    check(await feedback.evaluate(e=>e.classList.contains('ui-state-scope')),'actual feedback Teleport participates')
    const input=feedback.locator('textarea').first()
    check((await focus(input,page)).color==='rgb(85, 199, 206)','actual feedback input focus')
    await input.fill('纯本地合成反馈')
    await feedback.getByRole('button',{name:'提交反馈',exact:true}).click()
    check(await feedback.evaluate(e=>e.scrollWidth<=e.clientWidth+1),'feedback does not need horizontal scrolling')
    await feedback.getByRole('button',{name:'提交中…',exact:true}).waitFor()
    const pending=feedback.getByRole('button',{name:'提交中…',exact:true})
    check(await pending.isDisabled()&&await pending.evaluate(e=>getComputedStyle(e).backgroundColor)==='rgb(21, 29, 34)','actual feedback request busy state')
    await page.waitForFunction(()=>window.__qa.bugWrites===1&&window.__qa.pending)
    await pending.evaluate(e=>e.click());check(await page.evaluate(()=>window.__qa.bugWrites)===1,'one local feedback POST only')
    await page.screenshot({path:path.join(output,`feedback-busy-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.finish());await feedback.getByRole('button',{name:'提交反馈',exact:true}).waitFor()
    for(const route of ['/admin','/game','/sandbox','/deck-editor','/']){
      await page.evaluate(path=>window.__qa.navigate(path),route)
      const ordinary=route==='/admin'||route==='/'
      check(await feedback.evaluate(e=>e.classList.contains('ui-state-scope'))===ordinary,'route-aware feedback scope '+route)
      await page.evaluate(()=>window.__qa.mode('friend'))
      await friend.waitFor()
      check(await friend.evaluate(e=>e.classList.contains('ui-state-scope'))===ordinary,'route-aware friend scope '+route)
      await page.evaluate(()=>window.__qa.mode('integrity'))
      await friend.waitFor({state:'detached'});await notice.waitFor()
      check(await notice.evaluate(e=>e.classList.contains('ui-state-scope'))===ordinary,'route-aware integrity scope '+route)
      check(await notice.getAttribute('aria-modal')==='false','nonmodal semantics retained across routes')
    }
    await feedback.getByRole('button',{name:'取消',exact:true}).click();await feedback.waitFor({state:'detached'})
    check(errors.length===0&&await page.evaluate(()=>window.__qa.unexpected.length)===0,'no page errors or unmocked API')
    results.push({viewport,checks,errors,localWrites:await page.evaluate(()=>({friend:window.__qa.friendWrites,integrity:window.__qa.integrityWrites,bug:window.__qa.bugWrites}))})
    await page.close()
  }
  assert.deepEqual(hashSources(),before,'source remained stable')
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',checks,results,sourceHashes:before,sourceAfter:hashSources(),realIosVerified:false,actualAppMounted:false,actualConsumersMounted:true,actualGameplayGeometryTested:false,liveApiWrites:0},null,2))
  console.log(`Global site states passed: ${checks}; evidence ${output}`)
}catch(error){
  await activePage?.screenshot({path:path.join(output,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'failed',checks,results,error:String(error),sourceHashes:before},null,2));throw error
}finally{await browser?.close();await server.close()}
