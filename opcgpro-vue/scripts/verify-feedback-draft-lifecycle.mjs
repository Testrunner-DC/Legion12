import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/feedback-draft-${Date.now()}`
assert(!fs.existsSync(output), 'Never overwrite evidence')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const bound = ['src/l12/site/GlobalBugFeedback.vue', 'src/l12/platform.ts', 'src/l12/net.ts']
const hashes = () => Object.fromEntries(bound.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const before = hashes()
const entry = `
import {createApp,h,ref} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import '/src/style.css'
window.__qa={bodies:[],diagnosticPending:[],pendingPost:null,holdDiagnostic:false,unexpected:[]}
const originalFetch=window.fetch.bind(window)
const response=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'content-type':'application/json'}})
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.endsWith('/health')||url.endsWith('/api/auth/me')){
  const complete=()=>response(url.endsWith('/health')?{status:'ok',maintenance:false}:{id:'synthetic-self',username:'合成账号'})
  if(window.__qa.holdDiagnostic)return new Promise(resolve=>window.__qa.diagnosticPending.push(()=>resolve(complete())))
  return complete()
 }
 if(url.endsWith('/api/bugs')&&init?.method==='POST'){
  window.__qa.bodies.push(JSON.parse(init.body))
  return new Promise(resolve=>window.__qa.pendingPost=ok=>resolve(ok?response({id:'synthetic-report'}):response({error:'本地合成提交失败'},400)))
 }
 if(url.includes('/api/')){window.__qa.unexpected.push(url);throw Error('Unexpected API')}
 return originalFetch(input,init)
}
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
platform.authState.initialized=true;platform.authState.verified=true
platform.platformState.account={id:'synthetic-self',username:'合成账号'}
platform.platformState.token=''
net.l12State.room={roomCode:'SYNTHETIC-OLD'};net.l12State.game={matchId:'synthetic-match-old'}
const {default:Feedback}=await import('/src/l12/site/GlobalBugFeedback.vue')
const router=createRouter({history:createMemoryHistory(),routes:['/me','/elsewhere'].map(path=>({path,component:{render:()=>null}}))})
await router.push('/me');await router.isReady()
window.__qa.navigate=()=>router.push('/elsewhere')
window.__qa.context=()=>{net.l12State.room={roomCode:'SYNTHETIC-NEW'};net.l12State.game={matchId:'synthetic-match-new'}}
window.__qa.account=()=>{platform.platformState.account={id:'synthetic-next',username:'另一合成账号'}}
window.__qa.token=()=>{platform.platformState.token='synthetic-renewed-token'}
window.__qa.accountRoundtrip=()=>{platform.platformState.account={id:'synthetic-next',username:'另一合成账号'};platform.platformState.account={id:'synthetic-self',username:'合成账号'}}
window.__qa.finishDiagnostic=()=>{window.__qa.holdDiagnostic=false;window.__qa.diagnosticPending.splice(0).forEach(done=>done())}
window.__qa.finish=ok=>{const done=window.__qa.pendingPost;window.__qa.pendingPost=null;done?.(ok)}
const shown=ref(true)
window.__qa.mount=value=>shown.value=value
createApp({render:()=>shown.value?h(Feedback):null}).use(router).mount('#app')
`
const server = await createServer({root,configLoader:'runner',cacheDir:path.join(output,'vite-cache'),logLevel:'error',server:{host:'127.0.0.1',port:0},plugins:[{
  name:'feedback-draft-fixture',resolveId(id){if(id==='/__feedback_entry.js')return id},load(id){if(id==='/__feedback_entry.js')return entry},
  configureServer(s){s.middlewares.use('/__feedback',(_req,res)=>{res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__feedback_entry.js"></script></body></html>')})},
}]})
const results=[]
let browser,activePage,checks=0
const check=(condition,message)=>{assert(condition,message);checks++}
try {
  await server.listen()
  browser=await chromium.launch({channel:'msedge',headless:true})
  const origin=`http://127.0.0.1:${server.httpServer.address().port}`
  for(const viewport of [{width:390,height:844},{width:1920,height:1080}]){
    for(const scenario of ['success','failure','reopen-success','reopen-failure','edit-pending','identity-before-post','token-before-post','identity-roundtrip-before-post','context-before-post','identity-after-post','token-after-post','unmount-before-post','unmount-after-post']){
      const page=await browser.newPage({viewport});activePage=page
      const errors=[]
      page.on('pageerror',error=>errors.push(error.message))
      await page.route('**/*',route=>new URL(route.request().url()).origin===origin?route.continue():route.abort())
      await page.goto(origin+'/__feedback')
      await page.waitForFunction(()=>window.__qa?.mount)
      const open=async()=>{await page.evaluate(()=>window.dispatchEvent(new Event('l12-open-bug-feedback')));await page.locator('.bug-feedback-dialog').waitFor()}
      const dialog=page.locator('.bug-feedback-dialog')
      const input=dialog.locator('textarea').first()
      await open();await input.fill('原始合成反馈')
      if(scenario.endsWith('before-post'))await page.evaluate(()=>window.__qa.holdDiagnostic=true)
      await dialog.getByRole('button',{name:'提交反馈',exact:true}).click()
      await dialog.getByRole('button',{name:'提交中…',exact:true}).waitFor()
      await dialog.getByRole('button',{name:'提交中…',exact:true}).evaluate(e=>{e.click();e.click()})
      if(scenario.endsWith('before-post')){
        await page.waitForFunction(()=>window.__qa.diagnosticPending.length===2)
        if(scenario==='identity-before-post')await page.evaluate(()=>window.__qa.account())
        if(scenario==='token-before-post')await page.evaluate(()=>window.__qa.token())
        if(scenario==='identity-roundtrip-before-post'){await page.evaluate(()=>window.__qa.accountRoundtrip());await input.fill('切回账号的新草稿')}
        if(scenario==='unmount-before-post')await page.evaluate(()=>window.__qa.mount(false))
        if(scenario==='context-before-post'){await page.evaluate(()=>window.__qa.navigate());await page.evaluate(()=>window.__qa.context())}
        await page.evaluate(()=>window.__qa.finishDiagnostic())
        if(scenario!=='context-before-post'){
          if(scenario!=='unmount-before-post')await dialog.getByRole('button',{name:'提交反馈',exact:true}).waitFor()
          else await dialog.waitFor({state:'detached'})
          await page.waitForTimeout(120)
          check(await page.evaluate(()=>window.__qa.bodies.length)===0,'identity change or unmount during diagnostics cannot POST')
          if(scenario==='token-before-post')check(await input.inputValue()==='原始合成反馈','token renewal retains same-account draft')
          if(scenario==='identity-roundtrip-before-post')check(await input.inputValue()==='切回账号的新草稿','account roundtrip cannot revive old request')
        }
      }
      const blocked=scenario.endsWith('before-post')&&scenario!=='context-before-post'
      if(!blocked){
        await page.waitForFunction(()=>window.__qa.bodies.length===1&&window.__qa.pendingPost)
        check(await page.evaluate(()=>window.__qa.bodies.length)===1,'busy and programmatic duplicate clicks send only one POST')
        if(scenario.startsWith('reopen')){await dialog.locator('header button').click();await dialog.waitFor({state:'detached'});await open();await input.fill('重开后的新草稿')}
        if(scenario==='edit-pending')await input.fill('同弹框修改的新草稿')
        if(scenario==='identity-after-post'){await page.evaluate(()=>window.__qa.account());check(await input.inputValue()==='','draft does not leak across account switch');await input.fill('新账号的新草稿')}
        if(scenario==='token-after-post'){await page.evaluate(()=>window.__qa.token());check(await input.inputValue()==='原始合成反馈','token renewal does not clear existing text');await input.fill('新会话的新草稿')}
        if(scenario==='unmount-after-post'){await page.evaluate(()=>window.__qa.mount(false));await dialog.waitFor({state:'detached'});await page.evaluate(()=>window.__qa.mount(true));await open();await input.fill('新组件的新草稿')}
        await page.evaluate(ok=>window.__qa.finish(ok),!scenario.endsWith('failure')&&scenario!=='failure')
        await dialog.getByRole('button',{name:'提交反馈',exact:true}).waitFor()
        if(scenario==='success'||scenario==='context-before-post'){
          check(await input.inputValue()==='','unchanged successful draft clears')
          check((await dialog.locator('.bug-message').textContent()).includes('已提交'),'unchanged successful draft receives receipt')
        }else if(scenario==='failure'){
          check(await input.inputValue()==='原始合成反馈','failed original draft remains recoverable')
          check(await dialog.locator('.bug-message').count()===1,'failed original draft receives error')
        }else{
          const expected=scenario.startsWith('reopen')?'重开后的新草稿':scenario==='edit-pending'?'同弹框修改的新草稿':scenario==='identity-after-post'?'新账号的新草稿':scenario==='token-after-post'?'新会话的新草稿':'新组件的新草稿'
          check(await input.inputValue()===expected,'old completion cannot clear newer draft')
          check(await dialog.locator('.bug-message').count()===0,'old completion cannot overwrite newer message')
        }
        const sent=await page.evaluate(()=>window.__qa.bodies[0])
        check(sent.description.includes('原始合成反馈')&&!sent.description.includes('新草稿'),'POST is bound to original submitted text')
        check(sent.page==='/me'&&sent.roomCode==='SYNTHETIC-OLD'&&sent.matchId==='synthetic-match-old','POST retains original page/room/match')
        check(sent.clientDiagnostic.currentRoute==='/me'&&sent.clientDiagnostic.roomCode==='SYNTHETIC-OLD'&&sent.clientDiagnostic.matchId==='synthetic-match-old','diagnostic context agrees with frozen submission')
      }
      check(errors.length===0&&await page.evaluate(()=>window.__qa.unexpected.length)===0,'no page errors or unmocked API')
      if(scenario==='reopen-success')await page.screenshot({path:path.join(output,`draft-preserved-${viewport.width}.png`)})
      results.push({viewport,scenario,errors,posts:await page.evaluate(()=>window.__qa.bodies.length)})
      await page.close()
    }
  }
  assert.deepEqual(hashes(),before,'source remained stable')
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',checks,results,sourceHashes:before,sourceAfter:hashes(),liveApiWrites:0,realIosVerified:false},null,2))
  console.log(`Feedback lifecycle passed: ${checks}; evidence ${output}`)
}catch(error){
  await activePage?.screenshot({path:path.join(output,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'failed',checks,results,error:String(error),sourceHashes:before},null,2));throw error
}finally{await browser?.close();await server.close()}
