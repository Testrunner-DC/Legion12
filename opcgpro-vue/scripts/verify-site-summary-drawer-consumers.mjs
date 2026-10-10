import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/site-summary-drawer-${Date.now()}`
assert(!fs.existsSync(output), 'Earlier evidence must not be overwritten')
assert(/^D:[/\\]GPT[/\\]Legion12[/\\]artifacts[/\\]/i.test(output), 'Evidence belongs on the governed D drive')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sources = ['src/l12/site/SiteShell.vue', 'src/l12/site/SeasonSummaryNotice.vue',
  'src/l12/RankedIdentityBadge.vue', 'src/l12/site/uiSystem.css', 'scripts/test-season-summary-notifications.mjs']
const hashes = () => Object.fromEntries(sources.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const baseline = hashes()
// Actual SiteShell and notification consumers; all accounts and API data are synthetic.
// This is Chromium/Edge emulation, not proof of real iOS, IME or nonzero safe areas.
const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
net.l12State.endpoint=location.origin.replace(/^http/,'ws')+'/ws'
platform.platformState.account={id:'synthetic-a',username:'本地合成账号',role:'player',createdAt:'2026-09-01T00:00:00Z',publicHistory:false}
platform.platformState.token='synthetic-token'
platform.authState.initialized=true;platform.authState.verified=true
const normal={id:'summary-a',seasonId:'internal-not-displayed',seasonName:'2026年9月',faction:'秩序',placed:true,rankLabel:'冠冕',factionRank:1,overallRank:2,sevenValue:123456,wins:46,losses:14,winRate:76.7,factionTitle:'统御者',masterTitles:[],titles:['统御者'],availableAt:'2026-09-30T00:00:00Z'}
let current=structuredClone(normal),unread=true,failAck=false
window.__qa={acks:0,reads:0,errors:[],failNextAck(){failAck=true},long(){current={...structuredClone(normal),seasonName:'很长的合成赛季名称用于验证窄屏换行与底部按钮可达性',titles:Array.from({length:20},(_,i)=>'合成历史称号'+i)};unread=true;window.dispatchEvent(new Event('l12-resource-seasonSummaryNotifications'))},show(){current=structuredClone(normal);unread=true;window.dispatchEvent(new Event('l12-resource-seasonSummaryNotifications'))}}
platform.seasonSummaryApi.notifications=async()=>{window.__qa.reads++;return unread?[structuredClone(current)]:[]}
platform.seasonSummaryApi.acknowledge=async()=>{window.__qa.acks++;if(failAck){failAck=false;throw Error('本地合成确认失败')}unread=false}
platform.alternateArtApi.notifications=async()=>[]
platform.telemetryApi.pageView=async()=>{}
const Page={render:()=>h('article',{class:'synthetic-page'},[h('h1','合成普通页面'),h('button',{type:'button'},'页面按钮')])}
const router=createRouter({history:createMemoryHistory(),routes:['/','/me','/cards','/decks','/rules','/battle'].map(p=>({path:p,component:Page,meta:{section:'site'}}))})
await router.push('/me');await router.isReady()
const {default:SiteShell}=await import('/src/l12/site/SiteShell.vue')
const app=createApp({render:()=>h(SiteShell,null,{default:()=>h(RouterView)})}).use(router)
app.mount('#app');window.__qa.unmount=()=>app.unmount();window.__qa.route=()=>router.currentRoute.value.path
`
const server = await createServer({root,configLoader:'runner',cacheDir:path.join(output,'vite-cache'),logLevel:'error',server:{host:'127.0.0.1',port:0},plugins:[{
  name:'site-summary-drawer-consumers',resolveId(id){if(id==='/__site_summary__.js')return id},load(id){if(id==='/__site_summary__.js')return entry},
  configureServer(s){s.middlewares.use('/__site_summary',(_req,res)=>{res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__site_summary__.js"></script></body></html>')})},
}]})
let browser,activePage,checks=0
const results=[]
const issues=[]
const check=(value,message)=>{assert(value,message);checks++}
try {
  await server.listen()
  browser=await chromium.launch({channel:'msedge',headless:true})
  const origin=`http://127.0.0.1:${server.httpServer.address().port}`
  for(const viewport of [{width:320,height:568},{width:390,height:844},{width:568,height:320},{width:700,height:700},{width:701,height:700},{width:1366,height:768}]){
    const page=await browser.newPage({viewport});activePage=page
    const errors=[];page.on('pageerror',e=>errors.push(e.message))
    await page.route('**/*',r=>new URL(r.request().url()).hostname==='127.0.0.1'?r.continue():r.abort())
    await page.goto(origin+'/__site_summary')
    const dialog=page.locator('.season-summary'),confirm=dialog.getByRole('button',{name:'保存并确认',exact:true})
    await dialog.waitFor()
    check(await page.locator('.site-shell[data-l12-ui-system="site-v1"]').count()===1,'actual SiteShell mounted')
    check(await dialog.getAttribute('aria-labelledby')==='season-summary-title','dialog retains accessible label')
    check((await dialog.innerText()).includes('76.7%')&&(await dialog.innerText()).includes('123,456'),'actual summary facts presented')
    check(!(await dialog.innerText()).includes('internal-not-displayed'),'internal season ID not exposed')
    const contained=async phase=>{
      const g=await dialog.evaluate(e=>{const r=e.getBoundingClientRect();return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,scroll:e.scrollWidth,client:e.clientWidth,height:e.clientHeight,scrollHeight:e.scrollHeight}})
      check(g.left>=-1&&g.right<=viewport.width+1&&g.top>=-1&&g.bottom<=viewport.height+1&&g.scroll<=g.client+1,phase+' dialog contained');return g
    }
    await contained('normal')
    await page.evaluate(()=>window.__qa.long())
    await page.getByRole('heading',{name:/很长的合成/}).waitFor()
    const geometry=await contained('long content')
    await confirm.scrollIntoViewIfNeeded()
    const buttonBox=await confirm.boundingBox()
    check(buttonBox.y>=0&&buttonBox.y+buttonBox.height<=viewport.height+1&&buttonBox.height>=44,'confirmation reachable with internal scroll')
    await confirm.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab')
    check(await confirm.evaluate(e=>document.activeElement===e),'keyboard returns to actual confirm action')
    await page.screenshot({path:path.join(output,`summary-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.failNextAck())
    await confirm.click()
    await dialog.waitFor()
    check(await page.evaluate(()=>window.__qa.acks)===1,'failed synthetic acknowledgement attempted once')
    check(await page.getByRole('heading',{name:/很长的合成/}).count()===1,'failed acknowledgement restores unread summary')
    await confirm.click();await dialog.waitFor({state:'detached'})
    check(await page.evaluate(()=>window.__qa.acks)===2,'successful acknowledgement attempted once')
    const menu=page.locator('.site-mobile-head button'),drawer=page.locator('#site-mobile-drawer')
    const mobile=await menu.isVisible()
    if(mobile){
      await menu.click();await drawer.getByRole('link',{name:'主页',exact:true}).waitFor()
      // Allow the real nextTick focus attempt; preserve a failed consumer result
      // while checking other dimensions, instead of stopping at the first one.
      await page.waitForTimeout(250)
      const focus=await drawer.evaluate(e=>({inside:e.contains(document.activeElement),active:document.activeElement?.outerHTML?.slice(0,350),first:e.querySelector('a,button')?.outerHTML?.slice(0,350),firstVisible:!!e.querySelector('a,button')?.getClientRects().length}))
      checks++
      if(!focus.inside)issues.push({viewport,kind:'drawer-focus-entry',focus})
      check(await drawer.getAttribute('role')==='dialog'&&await menu.getAttribute('aria-expanded')==='true','open drawer semantic state')
      check(await page.evaluate(()=>document.body.style.overflow)==='hidden','drawer locks body scroll')
      await page.keyboard.press('Escape')
      await page.waitForFunction(()=>document.querySelector('.site-mobile-head button')===document.activeElement)
      check(await menu.getAttribute('aria-expanded')==='false'&&await drawer.getAttribute('role')===null,'Escape closes drawer and restores trigger focus')
      check(await page.evaluate(()=>document.body.style.overflow)!=='hidden','drawer close restores body scroll')
      await drawer.evaluate(e=>{
        const links=e.querySelectorAll('.site-nav a')
        links[0].hidden=true;links[1].setAttribute('aria-disabled','true')
        links[2].tabIndex=-1;links[3].setAttribute('inert','')
        links[4].setAttribute('data-qa-focus-target','true')
      })
      await menu.click()
      await page.waitForFunction(()=>document.activeElement?.getAttribute('data-qa-focus-target')==='true')
      check(await drawer.evaluate(e=>e.contains(document.activeElement)),'hidden, aria-disabled, negative-tabindex and inert candidates are skipped')
      await page.keyboard.press('Escape')
      await drawer.evaluate(e=>{
        const links=e.querySelectorAll('.site-nav a')
        links[0].hidden=false;links[1].removeAttribute('aria-disabled')
        links[2].removeAttribute('tabindex');links[3].removeAttribute('inert')
        links[4].removeAttribute('data-qa-focus-target')
      })
      await menu.click();await drawer.getByRole('link',{name:'图鉴',exact:true}).click()
      await page.waitForFunction(()=>window.__qa.route()==='/cards')
      check(await menu.getAttribute('aria-expanded')==='false','route change closes drawer')
      await menu.click();await page.waitForFunction(()=>document.body.style.overflow==='hidden')
      await page.screenshot({path:path.join(output,`drawer-${viewport.width}x${viewport.height}.png`)})
      await page.evaluate(()=>window.__qa.unmount())
      check(await page.evaluate(()=>document.body.style.overflow)!=='hidden','unmount restores drawer scroll lock')
    }else{
      check(await drawer.getAttribute('role')===null,'desktop sidebar not a modal')
      await page.screenshot({path:path.join(output,`sidebar-${viewport.width}x${viewport.height}.png`)})
      await page.evaluate(()=>window.__qa.unmount())
    }
    check(errors.length===0,'no browser runtime errors')
    results.push({viewport,mobile,geometry,errors,cumulativeChecks:checks})
    await page.close();activePage=null
  }
  assert.deepEqual(hashes(),baseline,'Product and existing race regression remain unchanged');checks++
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({passed:issues.length===0,checks,results,issues,sourceHashes:baseline,realIosVerified:false,realImeVerified:false,nonzeroSafeAreasVerified:false,productionWrites:0},null,2))
  assert.equal(issues.length,0,'Consumer failures remain; see report.json. Do not call this matrix passed.')
  console.log(JSON.stringify({passed:true,checks,output}))
}catch(error){
  await activePage?.screenshot({path:path.join(output,'failure-diagnostic.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({passed:false,checks,error:String(error),results,sourceHashes:baseline},null,2));throw error
}finally{await browser?.close();await server.close()}
