import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/feedback-settings-${Date.now()}`
assert(!fs.existsSync(output), 'Never overwrite evidence')
fs.mkdirSync(output, { recursive: true })
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8').replace(/\r\n?/g, '\n')
const sources = {
  global: read('src/l12/site/GlobalBugFeedback.vue'), settings: read('src/l12/site/L12SettingsModal.vue'),
  shell: read('src/l12/site/SiteShell.vue'), game: read('src/l12/GamePage.vue'), replay: read('src/l12/ReplayPage.vue'),
  editor: read('src/l12/L12DeckEditor.vue'), profile: read('src/l12/site/ProfilePage.vue'),
  home: read('src/l12/site/HomeRevisitActions.vue'), dock: read('src/l12/game/BattleUtilityDock.vue'),
}
assert(!sources.global.includes('bug-feedback-trigger') && !sources.shell.includes('mobile-feedback-utility'))
assert(!sources.profile.includes('feedback-banner') && !sources.dock.includes('Bug反馈'))
for (const source of [sources.shell, sources.game, sources.replay, sources.editor]) {
  assert(source.includes('<L12SettingsModal') && source.includes('@feedback='), 'Every player surface must route feedback through Settings')
}
assert(sources.game.includes('v-if="l12State.spectating"') && sources.replay.match(/@click="openReplaySettings"/g)?.length === 2)
assert(sources.home.includes('<nav v-if="canContinue"') && !sources.home.includes('我的牌库') && !sources.home.includes('我的赛事'))
assert(sources.game.includes('--l12-battle-fixed-controls-z,5100') && sources.replay.includes('--l12-battle-fixed-controls-z,5100') && sources.global.includes('z-index:5000'))

const bound = Object.keys(sources).map(key => ({ key, hash: crypto.createHash('sha256').update(sources[key]).digest('hex') }))
const entry = `
import {createApp,h,ref,nextTick} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import Settings from '/src/l12/site/L12SettingsModal.vue'
import Feedback from '/src/l12/site/GlobalBugFeedback.vue'
import {closeSettingsAndRestore,openBugFeedbackFromSettings} from '/src/l12/site/bugFeedbackEntry.ts'
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.endsWith('/health'))return new Response(JSON.stringify({status:'ok',maintenance:false}),{status:200,headers:{'content-type':'application/json'}})
 if(url.endsWith('/api/auth/me'))return new Response(JSON.stringify({id:'feedback-a',username:'反馈甲'}),{status:200,headers:{'content-type':'application/json'}})
 if(url.endsWith('/api/bugs')&&init?.method==='POST')return new Response(JSON.stringify({message:'本地合成提交失败'}),{status:503,headers:{'content-type':'application/json'}})
 return originalFetch(input,init)
}
const platform=await import('/src/l12/platform.ts')
platform.authState.initialized=true;platform.authState.verified=true
platform.platformState.account={id:'feedback-a',username:'反馈甲'};platform.platformState.token='token-a'
window.__feedbackQa={switchAccount(){platform.platformState.account={id:'feedback-b',username:'反馈乙'};platform.platformState.token='token-b'},guest(){platform.platformState.account=null;platform.platformState.token=''}}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}}]})
await router.push('/');await router.isReady()
const Root={setup(){const open=ref(false),trigger=ref(null);const close=()=>closeSettingsAndRestore(()=>{open.value=false},trigger.value);const feedback=()=>openBugFeedbackFromSettings(()=>{open.value=false},trigger.value);return()=>h('main',[h('button',{ref:trigger,type:'button',onClick:()=>{open.value=true}},'设置'),open.value?h(Settings,{onClose:close,onFeedback:feedback}):null,h(Feedback)])}}
createApp(Root).use(router).mount('#app')
`
const server = await createServer({ root, configLoader:'runner', cacheDir:path.join(output,'vite-cache'), logLevel:'error', server:{host:'127.0.0.1',port:0}, plugins:[{
  name:'feedback-settings-fixture', resolveId(id){if(id==='/__feedback_settings.js')return id}, load(id){if(id==='/__feedback_settings.js')return entry},
  configureServer(vite){vite.middlewares.use((request,response,next)=>{if(new URL(request.url,'http://localhost').pathname!=='/__feedback_settings')return next();response.setHeader('Content-Type','text/html');response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__feedback_settings.js"></script></body></html>')})},
}]})
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const profiles=[{width:320,height:568},{width:390,height:844},{width:700,height:768},{width:701,height:768},{width:844,height:390},{width:1920,height:1080}]
let browser, activePage
const results=[]
try{
  await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
  const origin=`http://127.0.0.1:${server.httpServer.address().port}`
  for(const viewport of profiles){
    const page=await browser.newPage({viewport});activePage=page;const errors=[];page.on('pageerror',error=>errors.push(error.message))
    await page.goto(origin+'/__feedback_settings',{waitUntil:'networkidle'})
    const trigger=page.getByRole('button',{name:'设置',exact:true});await trigger.click()
    const settings=page.locator('.l12-settings-modal');const settingsClose=settings.getByRole('button',{name:'关闭设置',exact:true})
    assert.equal(await page.locator('[aria-modal="true"]').count(),1);assert.equal(await settingsClose.evaluate(element=>element===document.activeElement),true)
    const feedbackEntry=settings.getByRole('button',{name:'反馈 Bug',exact:true});await feedbackEntry.focus();await page.keyboard.press('Tab');assert.equal(await settingsClose.evaluate(element=>element===document.activeElement),true)
    await feedbackEntry.click()
    const dialog=page.locator('.bug-feedback-dialog');await dialog.waitFor()
    assert.equal(await page.locator('.l12-settings-modal').count(),0);assert.equal(await page.locator('[aria-modal="true"]').count(),1)
    const bug=dialog.locator('textarea').first();assert.equal(await bug.evaluate(element=>element===document.activeElement),true)
    const submitButton=dialog.getByRole('button',{name:'提交反馈',exact:true});await submitButton.focus();await page.keyboard.press('Tab');assert.equal(await dialog.getByRole('button',{name:'关闭反馈',exact:true}).evaluate(element=>element===document.activeElement),true);await bug.focus()
    await bug.fill('失败后必须保留的草稿');await submitButton.click()
    await dialog.getByText('本地合成提交失败').waitFor();assert.equal(await bug.inputValue(),'失败后必须保留的草稿')
    await page.evaluate(()=>window.__feedbackQa.switchAccount());assert.equal(await bug.inputValue(),'')
    await page.keyboard.press('Escape');await dialog.waitFor({state:'detached'});assert.equal(await trigger.evaluate(element=>element===document.activeElement),true)
    await page.evaluate(()=>window.__feedbackQa.guest());await trigger.click();await page.locator('.l12-settings-modal').getByRole('button',{name:'反馈 Bug',exact:true}).click()
    await page.getByText('提交身份：匿名玩家').waitFor();await page.keyboard.press('Escape')
    assert.equal(errors.length,0,errors.join('\n'));assert.equal(await page.locator('.bug-feedback-trigger,.mobile-feedback-utility,.feedback-banner').count(),0)
    await page.screenshot({path:path.join(output,`feedback-settings-${viewport.width}x${viewport.height}.png`)})
    results.push({viewport,settingsToFeedback:true,singleModal:true,settingsTabLoop:true,feedbackTabLoop:true,escapeFocusRestore:true,failureDraft:true,identityIsolation:true,guestIdentity:true})
    await page.close()
  }
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',profiles:results,sourceHashes:bound,liveWrites:0},null,2))
  console.log(`Feedback Settings entry passed at ${profiles.length} viewports; evidence ${output}`)
}catch(error){
  await activePage?.screenshot({path:path.join(output,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'failure-report.json'),JSON.stringify({status:'failed',profiles:results,sourceHashes:bound,error:String(error?.stack||error),liveWrites:0},null,2))
  throw error
}finally{await activePage?.close().catch(()=>{});await browser?.close();await server.close()}
