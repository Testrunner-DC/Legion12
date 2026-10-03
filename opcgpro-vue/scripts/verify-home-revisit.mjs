import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_HOME_REVISIT_OUT || path.join('D:/GPT/Legion12/artifacts', `home-revisit-${new Date().toISOString().replaceAll(':','-')}`)
assert.ok(!fs.existsSync(output), 'Never overwrite existing evidence')
fs.mkdirSync(output, { recursive:true })
const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
const params=new URLSearchParams(location.search), scenario=params.get('scenario')||'guest-empty'
const platform=await import('/src/l12/platform.ts'), net=await import('/src/l12/net.ts')
const calls=[], writes=[], unexpected=[]
const realFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.includes('/api/')){unexpected.push(url);throw Error('Unexpected fixture API: '+url)}
 return realFetch(input,init)
}
const deck=(name,date)=>({id:'fixture-'+name,revision:1,name,masterId:'S01-01M1',cardIds:[],moraleIds:[],specialIds:[],updatedAt:date})
localStorage.setItem('l12-custom-decks-v1:a',JSON.stringify({old:deck('旧牌库','2026-10-01T00:00:00Z'),latest:deck('最近牌库','2026-10-02T00:00:00Z')}))
localStorage.setItem('l12-custom-decks-v1:b',JSON.stringify({b:deck('第二账号牌库','2026-10-02T00:00:00Z')}))
localStorage.setItem('l12-selected-custom-deck:a','unchanged-selection')
const deckBefore=localStorage.getItem('l12-custom-decks-v1:a')
function account(id,verified=true){
 platform.platformState.account=id?{id,username:'本地验收账号',role:'player',createdAt:'2026-10-01T00:00:00Z',publicHistory:false}:null
 platform.platformState.token=id?'fixture-token-'+id:'';platform.authState.initialized=true;platform.authState.verified=verified
}
account(scenario.startsWith('guest')||scenario==='cached-failure'?null:'a',scenario!=='unverified')
const image='data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="1600" height="900"%3E%3Crect width="1600" height="900" fill="%23152b34"/%3E%3C/svg%3E'
const article=(id,kind)=>({id,kind,title:'已发布'+kind+'内容',summary:'已发布摘要',body:'正文',category:'公告',coverUrl:image,link:kind==='video'?'/videos':'',slug:id,status:'published',pinned:false,createdAt:'2026-10-01T00:00:00Z',updatedAt:'2026-10-01T00:00:00Z',publishedAt:'2026-10-01T00:00:00Z',revision:1})
const empty={composition:JSON.stringify({version:1,heroSlides:[],notices:[]}),legal:'{}',news:[],videos:[],products:[],media:[]}
const published={...empty,news:[article('news-fixture','news')],videos:[article('video-fixture','video')],products:[article('product-fixture','product')],composition:JSON.stringify({version:1,heroSlides:[{id:'hero',enabled:true,eyebrow:'',title:'已发布主视觉',summary:'',footer:'',href:'/news/news-fixture',mediaAssetId:'hero-media'}],notices:[{id:'notice',enabled:true,label:'已发布通知',href:'/news/news-fixture',tone:'dark'}]}),media:[{id:'hero-media',desktopUrl:image,mobileUrl:image,thumbnailUrl:image,altText:'已发布主视觉'}]}
let failHome=scenario==='cached-failure'||scenario==='retry'
if(scenario==='cached-failure')localStorage.setItem('l12-home-published-v2',JSON.stringify({version:2,payload:published}))
platform.siteContentApi.home=async()=>{calls.push('public-home');if(failHome)throw Error('fixture unavailable');return scenario.includes('published')?published:empty}
platform.telemetryApi.pageView=async()=>{calls.push('public-page-view')}
platform.alternateArtApi.notifications=async()=>{calls.push('existing-shell-art-notifications');return []}
platform.seasonSummaryApi.notifications=async()=>{calls.push('existing-shell-season-notifications');return []}
platform.friendApi.presence=async()=>{calls.push('existing-shell-presence');return []}
platform.tournamentApi.summaries=async(query)=>{calls.push('tournament-'+query.section);return {platformVersion:1,items:[],page:1,pageSize:24,total:0,totalPages:0}}
platform.tournamentApi.career=async()=>{calls.push('tournament-career');return {participated:0,organized:0,refereed:0,wins:0,losses:0,draws:0,total:0,items:[]}}
if(scenario==='storage-blocked'){
 const get=Storage.prototype.getItem, set=Storage.prototype.setItem
 Storage.prototype.getItem=function(key){if(key.startsWith('l12-custom-decks')||key.startsWith('l12-home-published'))throw Error('blocked');return get.call(this,key)}
 Storage.prototype.setItem=function(key,value){if(key.startsWith('l12-home-published'))throw Error('quota');return set.call(this,key,value)}
}
Object.assign(net.l12State,{accountId:'a',status:'online',recoveryPhase:scenario==='unconfirmed'?'opening-websocket':'snapshot-acknowledged',leavingRoom:false,
 game:scenario==='continue'||scenario==='unconfirmed'||scenario==='spectate'?{matchId:'fixture-match',phase:'Main'}:null,spectating:scenario==='spectate'})
const [{default:Home},{default:Shell},{default:Tournaments}]=await Promise.all([import('/src/l12/site/OfficialHomePage.vue'),import('/src/l12/site/SiteShell.vue'),import('/src/l12/site/TournamentHubPage.vue')])
const stub={template:'<main class="route-destination">导航目的页</main>'}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:Home},{path:'/battle/tournaments',component:Tournaments},{path:'/decks',component:stub},{path:'/game',component:stub},{path:'/news/:id?',component:stub},{path:'/videos',component:stub},{path:'/products',component:stub}]})
await router.push('/');await router.isReady()
createApp({render:()=>h(Shell,{},()=>h(RouterView))}).use(router).mount('#app')
window.__homeFixture={calls,unexpected,writes,account,go:href=>router.push(href),route:()=>router.currentRoute.value.fullPath,
 recover:()=>{failHome=false;window.dispatchEvent(new Event('online'))},
 cacheUnchanged:()=>scenario==='storage-blocked'||(localStorage.getItem('l12-custom-decks-v1:a')===deckBefore&&localStorage.getItem('l12-selected-custom-deck:a')==='unchanged-selection')}
`
const server = await createServer({root,cacheDir:path.join(output,'vite-cache'),server:{host:'127.0.0.1',port:0},logLevel:'error',plugins:[{
  name:'home-revisit-fixture',resolveId:id=>id==='/__home_revisit.js'?id:null,
  load:id=>id==='/__home_revisit.js'?entry:null,
  configureServer(server){server.middlewares.use('/__home_revisit_page',(_request,response)=>{
    response.setHeader('Content-Type','text/html');response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__home_revisit.js"></script></body></html>')
  })},
}]})
let browser, assertions=0
const results=[]
const ok=(value,message)=>{assert.ok(value,message);assertions++}
const profiles=[{width:1920,height:1080},{width:2560,height:1080},{width:320,height:568},{width:360,height:640},{width:390,height:844},{width:428,height:926},{width:844,height:390}]
try {
  await server.listen();browser=await chromium.launch({channel:'msedge',headless:true})
  const base=`http://127.0.0.1:${server.httpServer.address().port}/__home_revisit_page`
  for (const [index,profile] of profiles.entries()) {
    const scenarios=index===0?['guest-empty','guest-published','account','unverified','switch','storage-blocked','continue','unconfirmed','spectate','cached-failure','retry']:['guest-empty','guest-published','account']
    for (const scenario of scenarios) {
      const context=await browser.newContext({viewport:profile,isMobile:profile.width<900,hasTouch:profile.width<900})
      const page=await context.newPage(), errors=[]
      page.on('pageerror',error=>errors.push(error.message))
      await page.goto(`${base}?scenario=${scenario}`,{waitUntil:'networkidle'})
      if(scenario==='retry'){
        ok(await page.locator('.official-home-loading').count()===1,'failed initial request remains loading, not false empty')
        await page.evaluate(()=>window.__homeFixture.recover())
      }
      await page.waitForSelector('.official-home')
      if(!scenario.startsWith('guest')&&scenario!=='unverified'&&scenario!=='cached-failure') await page.waitForSelector('.home-revisit-actions')
      const body=await page.locator('.official-home').innerText()
      ok(!/占位|范例|2026[./]00/.test(body),`${scenario}: no fake player content`)
      const hasPersonal=await page.locator('.home-revisit-actions').count()
      ok(hasPersonal===(!scenario.startsWith('guest')&&scenario!=='unverified'&&scenario!=='cached-failure'?1:0),`${scenario}: verified-only shortcuts`)
      if(scenario.startsWith('guest'))ok((await page.evaluate(()=>window.__homeFixture.calls)).every(call=>call.startsWith('public-')),'guest makes zero personal requests')
      if(scenario==='guest-published'||scenario==='cached-failure')ok(body.includes('已发布主视觉')&&body.includes('已发布通知')&&body.includes('已发布news内容'),'published content retained')
      else ok(body.includes('暂无资讯')&&body.includes('暂无视频')&&body.includes('暂无产品'),'genuine empty sections')
      if(hasPersonal){
        if(scenario==='storage-blocked')ok(body.includes('查看与编辑自己的牌库')&&!body.includes('最近保存'),'blocked local cache has usable generic deck link')
        else ok(body.includes('最近保存：最近牌库'),'most recent own cache label, not cloud availability claim')
      }
      ok(await page.locator('.home-revisit-action.continue-game').count()===(['continue','spectate'].includes(scenario)?1:0),'continue only acknowledged ongoing connection')
      if(scenario==='switch'){
        await page.evaluate(()=>window.__homeFixture.account('b'))
        await page.waitForFunction(()=>document.querySelector('.home-revisit-actions')?.textContent.includes('第二账号牌库'))
        ok(!(await page.locator('.home-revisit-actions').innerText()).includes('最近保存：最近牌库'),'switch clears old account label')
        await page.evaluate(()=>window.__homeFixture.account(null))
        await page.waitForFunction(()=>!document.querySelector('.home-revisit-actions'))
        ok(await page.locator('.home-revisit-actions').count()===0,'logout removes all personal shortcuts')
      }
      if(scenario==='account'){
        const tournamentsLink=page.getByRole('link',{name:'我的赛事 查看参赛与主办进度'})
        if(profile.width===1920){await tournamentsLink.focus();await page.keyboard.press('Enter')}
        else await tournamentsLink.click()
        await page.waitForSelector('.hub-page')
        ok(await page.evaluate(()=>window.__homeFixture.route())==='/battle/tournaments?section=mine','my tournaments actual route')
        ok(await page.getByRole('button',{name:'我的赛事',exact:true}).getAttribute('aria-current')==='page','mine tab is truly active')
        const calls=await page.evaluate(()=>window.__homeFixture.calls)
        ok(calls.filter(call=>call.startsWith('tournament-')&&call!=='tournament-career').join(',')==='tournament-mine','initial mine query without duplicate discover request')
        await page.getByRole('button',{name:'发现赛事',exact:true}).click()
        await page.waitForFunction(()=>window.__homeFixture.route().includes('section=discover'))
        await page.evaluate(()=>window.__homeFixture.go('/battle/tournaments?section=mine'))
        await page.waitForFunction(()=>document.querySelector('.section-tabs [aria-current="page"]')?.textContent==='我的赛事')
        ok(true,'query navigation restores correct section')
        await page.evaluate(()=>window.__homeFixture.go('/'));await page.waitForSelector('.home-revisit-actions')
        await page.getByRole('link',{name:/我的牌库 最近保存/}).click()
        ok(await page.evaluate(()=>window.__homeFixture.route())==='/decks?tab=mine','deck shortcut stays in own library')
        await page.evaluate(()=>window.__homeFixture.go('/'));await page.waitForSelector('.home-revisit-actions')
      }
      if(['continue','spectate'].includes(scenario)){
        ok((await page.locator('.continue-game').innerText()).includes(scenario==='spectate'?'继续观战':'继续对局'),'correct continue label')
        await page.locator('.continue-game').click()
        ok(await page.evaluate(()=>window.__homeFixture.route())==='/game','continue only navigates after explicit click')
        await page.evaluate(()=>window.__homeFixture.go('/'));await page.waitForSelector('.home-revisit-actions')
      }
      const geometry=await page.evaluate(()=>({viewport:innerWidth,scroll:document.documentElement.scrollWidth,
        actions:[...document.querySelectorAll('.home-revisit-action')].map(element=>{const r=element.getBoundingClientRect();return {left:r.left,right:r.right,width:r.width,height:r.height}})}))
      ok(geometry.scroll<=geometry.viewport+1,`${scenario}/${profile.width}: no horizontal page overflow`)
      ok(geometry.actions.every(r=>r.width>0&&r.height>=48&&r.left>=-1&&r.right<=geometry.viewport+1),'usable action bounds')
      ok(await page.evaluate(()=>window.__homeFixture.cacheUnchanged()),'home never writes deck content or battle selection')
      ok((await page.evaluate(()=>window.__homeFixture.unexpected)).length===0,'no new data API or unexpected network')
      ok(errors.length===0,'no page errors')
      const screenshot=path.join(output,`${profile.width}x${profile.height}-${scenario}.png`)
      await page.screenshot({path:screenshot,fullPage:true})
      results.push({profile,scenario,geometry,errors,screenshot,calls:await page.evaluate(()=>window.__homeFixture.calls)})
      await context.close()
    }
  }
  const sourceFiles=['src/l12/site/OfficialHomePage.vue','src/l12/site/HomeRevisitActions.vue','src/l12/site/homeRevisit.ts','src/l12/site/homePublishedCache.ts','src/l12/site/TournamentHubPage.vue','src/l12/site/tournamentHubNavigation.ts']
  const sourceHashes=Object.fromEntries(sourceFiles.map(file=>[file,crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')]))
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',generatedAt:new Date().toISOString(),assertions,sourceHashes,results},null,2))
  console.log(`Home revisit browser passed: ${assertions} assertions / ${results.length} cases`)
  console.log(path.join(output,'report.json'))
} catch(error) {
  fs.writeFileSync(path.join(output,'failure-report.json'),JSON.stringify({status:'failed',assertions,error:String(error?.stack||error),results},null,2));throw error
} finally {await browser?.close();await server.close()}
