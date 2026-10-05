import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/tournament-notice-consumers-${Date.now()}`
assert(!fs.existsSync(output), 'Do not overwrite earlier evidence')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sources = ['src/l12/site/SiteShell.vue', 'src/l12/site/TournamentDetailPage.vue',
  'src/l12/site/TournamentAccountDetail.vue', 'src/l12/site/TournamentJudgeDesk.vue',
  'src/l12/site/TournamentManagementPanel.vue', 'src/l12/site/uiSystem.css',
  'scripts/capture-tournament-center-visual.mjs']
const hashes = () => Object.fromEntries(sources.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const baseline = hashes()
// Reuse the established synthetic tournament fixture, but replace its mocked
// wrapper with the actual SiteShell and its immediate APIs with deferred work.
// No player data, service, WebSocket or production mutation is used.
const fixtureSource = fs.readFileSync(path.join(root, 'scripts/capture-tournament-center-visual.mjs'), 'utf8')
let entry = fixtureSource.match(/const entry = String\.raw`([\s\S]*?)`\r?\n\r?\nlet browser/)[1]
const oldMount = "createApp({render:()=>h('div',{class:'site-shell'},[h('div',{class:'site-content'},[h(RouterView)])])}).use(router).mount('#app')"
assert(entry.includes(oldMount), 'The fixture wrapper changed; review before reuse')
entry = entry.replace(oldMount, `
const {default:SiteShell}=await import('/src/l12/site/SiteShell.vue')
const full=structuredClone(tournament)
let data={...structuredClone(full),rounds:[],finalSwissStandings:[],judgeCases:[]}
window.__qa={pending:null,writes:0,unexpected:[],resolve:null,reject:null}
const deferred=kind=>new Promise((resolve,reject)=>{if(window.__qa.pending)throw Error('Unexpected overlapping fixture work');window.__qa.pending=kind;window.__qa.resolve=resolve;window.__qa.reject=reject})
platform.tournamentApi.getByCode=()=>deferred('load')
platform.tournamentApi.createJudgeCase=()=>{window.__qa.writes++;return deferred('judge')}
platform.tournamentApi.setVisibility=()=>{window.__qa.writes++;return deferred('management')}
platform.alternateArtApi.notifications=async()=>[]
platform.seasonSummaryApi.notifications=async()=>[]
window.__qa.finish=(failure=false)=>{
 const resolve=window.__qa.resolve,reject=window.__qa.reject
 window.__qa.pending=null;window.__qa.resolve=null;window.__qa.reject=null
 failure?reject(Error('本地合成操作失败')):resolve(structuredClone(data))
}
window.__qa.content=()=>{data=structuredClone(full);window.dispatchEvent(new Event('l12-resource-tournaments'))}
window.__qa.failureRoute=()=>router.push('/battle/tournaments/FAIL')
createApp({render:()=>h(SiteShell,null,{default:()=>h(RouterView)})}).use(router).mount('#app')
`)
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(output, 'vite-cache'), logLevel: 'error', server: {host:'127.0.0.1',port:0}, plugins:[{
  name:'tournament-notice-consumer-fixture', resolveId(id){if(id==='/__tournament_notice_entry.js')return id},load(id){if(id==='/__tournament_notice_entry.js')return entry},
  configureServer(s){s.middlewares.use('/__tournament_notice',(_req,res)=>{res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__tournament_notice_entry.js"></script></body></html>')})},
}]})
let browser, activePage, checks = 0
const results = []
const check = (condition, message) => { assert(condition, message); checks++ }
try {
  await server.listen()
  browser = await chromium.launch({channel:'msedge',headless:true})
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  for(const viewport of [{width:320,height:568},{width:390,height:844},{width:568,height:320},{width:1366,height:768}]){
    const page = await browser.newPage({viewport});activePage=page
    const errors=[];page.on('pageerror',e=>errors.push(e.message))
    await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
    await page.goto(origin+'/__tournament_notice?route=/battle/tournaments/L12-AUTUMN')
    const detail=page.locator('.detail-page'), tabs=detail.locator('.tabs')
    const contained=async phase=>{
      const box=await detail.evaluate(e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,scroll:e.scrollWidth,client:e.clientWidth}})
      check(box.left>=-1&&box.right<=viewport.width+1&&box.scroll<=box.client+1,phase+' remains horizontally contained')
      return box
    }
    await detail.getByRole('status').waitFor()
    check(await detail.getByRole('status').innerText()==='正在加载赛事…','actual asynchronous loading consumer')
    check(await page.locator('.site-shell[data-l12-ui-system="site-v1"]').count()===1,'actual SiteShell rather than fixture wrapper')
    check(await detail.evaluate(e=>!!e.closest('.ui-state-scope')),'ordinary route inherits actual shared scope')
    await contained('loading')
    await page.waitForFunction(()=>window.__qa?.pending==='load')
    await page.evaluate(()=>window.__qa.finish())
    await tabs.waitFor()
    check(await detail.locator('.state-panel').count()===0,'loading removed after successful fetch')
    await tabs.getByRole('button',{name:'赛程',exact:true}).click()
    await detail.getByText('赛程尚未生成',{exact:true}).waitFor()
    check(await detail.locator('.schedule [role="alert"]').count()===0,'passive schedule empty is not an error')
    await contained('empty schedule')
    await tabs.getByRole('button',{name:'排名',exact:true}).click()
    await detail.getByText('排名尚未产生',{exact:true}).waitFor()
    await contained('empty standings')
    await tabs.getByRole('button',{name:'裁判台',exact:true}).click()
    await detail.getByText('当前没有可呼叫的桌次',{exact:true}).waitFor()
    await detail.getByText('暂无可见案件',{exact:true}).waitFor()
    check(await detail.locator('.judge-desk .call').count()===0,'no personally callable matches means no call form')
    await contained('empty judge desk')
    await detail.getByText('暂无可见案件',{exact:true}).scrollIntoViewIfNeeded()
    await page.screenshot({path:path.join(output,`empty-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.content())
    await detail.getByRole('status').waitFor()
    await page.waitForFunction(()=>window.__qa.pending==='load')
    await page.evaluate(()=>window.__qa.finish())
    await tabs.waitFor()
    await detail.locator('.judge-desk .call').waitFor()
    check(await detail.getByText('当前没有可呼叫的桌次',{exact:true}).count()===0,'empty changes into callable content')
    await detail.locator('.call select').first().selectOption('m1')
    await detail.locator('.call input').fill('合成裁判请求')
    const call=detail.locator('.call').getByRole('button',{name:'呼叫裁判',exact:true})
    await call.focus();await page.keyboard.press('Enter')
    await page.waitForFunction(()=>window.__qa.pending==='judge')
    check(await call.isDisabled(),'real judge action disabled while work pending')
    await page.evaluate(()=>window.__qa.finish(true))
    const notice=detail.locator('p.notice[role="status"]')
    await notice.waitFor()
    check(await notice.innerText()==='本地合成操作失败'&&await notice.getAttribute('aria-live')==='polite','judge failure emitted through actual parent notice')
    await contained('judge error and populated cases')
    await call.click();await page.waitForFunction(()=>window.__qa.pending==='judge')
    await page.evaluate(()=>window.__qa.finish())
    await page.waitForFunction(()=>document.querySelector('.detail-page p.notice')?.textContent==='裁判请求已提交')
    check(await call.isEnabled(),'judge success clears busy state')
    await tabs.getByRole('button',{name:'主办管理',exact:true}).click()
    const management=detail.locator('.management-panel')
    const visibility=management.locator('.visibility-form select').first()
    await visibility.selectOption('code')
    const save=management.getByRole('button',{name:'保存公开范围',exact:true})
    await save.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab')
    check(await save.evaluate(e=>document.activeElement===e),'actual keyboard focus on management action')
    check(await save.evaluate(e=>getComputedStyle(e).outlineStyle==='solid'&&parseFloat(getComputedStyle(e).outlineWidth)>=2),'shared visible focus preserved')
    await page.keyboard.press('Enter');await page.waitForFunction(()=>window.__qa.pending==='management')
    check(await save.isDisabled(),'real management action disabled while work pending')
    await page.evaluate(()=>window.__qa.finish(true))
    await page.waitForFunction(()=>document.querySelector('.detail-page p.notice')?.textContent==='本地合成操作失败')
    await save.click();await page.waitForFunction(()=>window.__qa.pending==='management')
    await page.evaluate(()=>window.__qa.finish())
    await page.waitForFunction(()=>document.querySelector('.detail-page p.notice')?.textContent==='公开范围已保存')
    check(await notice.getAttribute('role')==='status','management success retains actual shared notice consumer')
    const geometry=await contained('management success')
    await notice.scrollIntoViewIfNeeded()
    await page.screenshot({path:path.join(output,`notice-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.failureRoute())
    await page.waitForFunction(()=>window.__qa.pending==='load')
    await page.evaluate(()=>window.__qa.finish(true))
    const failure=detail.getByRole('alert');await failure.waitFor()
    check(await failure.innerText()==='本地合成操作失败','initial failure uses actual alert branch')
    check(await detail.locator('.tabs').count()===0&&await detail.getByRole('status').count()===0,'initial failure cannot retain loaded or loading branch')
    await contained('initial failure')
    check(await page.evaluate(()=>window.__qa.writes)===4,'four local synthetic actions only')
    check(errors.length===0,'no browser runtime error')
    await page.screenshot({path:path.join(output,`failure-${viewport.width}x${viewport.height}.png`)})
    results.push({viewport,geometry,errors,cumulativeChecks:checks})
    await page.close();activePage=null
  }
  assert.deepEqual(hashes(),baseline,'bound product/fixture sources remain unchanged');checks++
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({passed:true,checks,results,sourceHashes:baseline,realIosVerified:false,realScreenReaderVerified:false,productionWrites:0},null,2))
  console.log(JSON.stringify({passed:true,checks,output}))
} catch(error){
  if(activePage)await activePage.screenshot({path:path.join(output,'failure-diagnostic.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({passed:false,checks,error:String(error),results,sourceHashes:baseline},null,2));throw error
} finally {await browser?.close();await server.close()}
