import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { parse } from '@vue/compiler-sfc'

const root = path.resolve(import.meta.dirname, '..')
const beforeMode = process.argv.includes('--baseline')
const base = 'D:/GPT/Legion12/artifacts/e1-ranking-states-20261005'
const output = path.resolve(process.env.L12_QA_OUT || path.join(base, `${beforeMode ? 'before' : 'after'}-${Date.now()}`))
assert(output.toLowerCase().startsWith(path.resolve(base).toLowerCase() + path.sep), 'Only leased evidence descendants are allowed')
assert(!fs.existsSync(output), 'Do not overwrite prior evidence')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sourceFiles = ['src/l12/site/RankingsPage.vue', 'src/l12/site/SiteShell.vue', 'src/l12/site/uiSystem.css',
  'src/l12/site/UiButton.vue', 'src/l12/site/UiNotice.vue', 'src/l12/site/StatisticsScope.vue',
  'src/l12/site/MasterMatchupMatrix.vue', 'src/l12/site/RankedMasterTitleRulesModal.vue',
  'src/l12/platform.ts', 'src/l12/site/siteUiStateScope.ts', 'scripts/test-ranking-refresh.mjs',
  'scripts/verify-e1-ranking-states.mjs']
const sha = value => crypto.createHash('sha256').update(value).digest('hex')
const hashes = () => Object.fromEntries(sourceFiles.map(file => [file, sha(fs.readFileSync(path.join(root, file)))]))
const sourceHashes = hashes()
const rankingSource = fs.readFileSync(path.join(root, sourceFiles[0]), 'utf8')
const descriptor = parse(rankingSource).descriptor
const sourceContract = { script: sha(descriptor.scriptSetup.content), styles: descriptor.styles.map(s => sha(s.content)),
  events: [...descriptor.template.content.matchAll(/@[\w.:-]+="[^"]*"/g)].map(m => m[0]),
  templateSansStateAttributes: sha(descriptor.template.content
    .replace(/ :aria-pressed="(?:tab === '[^']+'|range === item.id|faction === item.id)"/g, '')
    .replace(/ :aria-busy="loading \|\| undefined"/g, '')
    .replace(/(<p v-if="error" class="error") role="alert" aria-live="assertive"/, '$1')
    .replace(/\r\n/g, '\n')) }
const baselinePath = process.env.L12_QA_BASELINE
const baseline = baselinePath ? JSON.parse(fs.readFileSync(baselinePath, 'utf8')) : null
if (!beforeMode) {
  assert(baseline?.passed && baseline.phase === 'before', 'Supply actual passed before evidence')
  const {templateSansStateAttributes:beforeTemplate,...beforeContract}=baseline.sourceContract
  const {templateSansStateAttributes:afterTemplate,...afterContract}=sourceContract
  assert.deepEqual(afterContract,beforeContract,'Original script/style/event bytes remain identical')
  // The retained r1 before runner hashed mixed CRLF/LF bytes. Its clean 94b36966
  // template, normalized ONLY for CRLF, was independently read from Git. No
  // ordinary whitespace, structure, expressions or copy are ignored.
  const retainedR1 = baseline.sourceHashes[sourceFiles[0]] === '2fe3b17614f03bdcb4acf0f0511d49d29201d6bd608b41f6fbe67407cdb73a37'
    && beforeTemplate === '90a8923fccecd87ec535da8bc5572603db8cb4838527dac6ed23c55f436afb6f'
  const expectedTemplate=retainedR1?'959d2c11f429a90383dbbaa161f3ad68ee9a333def04c384475a3be6b02589b5':beforeTemplate
  assert.equal(afterTemplate,expectedTemplate,'Only enumerated state attributes and CRLF normalization may differ')
}

// Actual SiteShell, RankingsPage and shared primitives; every API result is frozen synthetic data.
const entry = String.raw`
import {createApp,h,ref} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
net.l12State.endpoint=location.origin.replace(/^http/,'ws')+'/ws'
platform.platformState.account=null;platform.platformState.token=''
platform.authState.initialized=true;platform.authState.verified=false
platform.alternateArtApi.notifications=async()=>[]
platform.seasonSummaryApi.notifications=async()=>[]
platform.telemetryApi.pageView=async()=>{}
const long='本地合成很长中文玩家与称号用于验证窄屏容纳不改排行榜布局'
const summary={matches:53,placedPlayers:1,activeMasters:1,updatedAt:'2026-10-05T15:00:00Z'}
const player={rank:1,username:long,faction:'秩序',tier:'定级',titles:[],displayValue:'2100',wins:20,losses:10}
const master={rank:1,masterId:'S01-M001',masterName:'合成主宰',games:29,wins:14,losses:15,winRate:48.3,usageRate:100,firstGames:14,firstWins:7,firstWinRate:50,secondGames:15,secondWins:7,secondWinRate:46.7,strongestPlayer:long,title:null}
const windows={season:{fromUtc:'2026-10-01T00:00:00Z',untilUtc:'2026-10-06T00:00:00Z',seasonName:'当前赛季'},'7d':{fromUtc:'2026-09-28T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z'},'30d':{fromUtc:'2026-09-05T16:00:00Z',untilUtc:'2026-10-05T16:00:00Z'}}
const normalHistory={honors:[{seasonName:'2026年9月',title:'统御者',winners:[{username:long,faction:'秩序'}]}],factionTotals:[],latestSeasonName:'2026年9月'}
let hold=true,pending=[],mode='normal'
window.__qa={calls:[],historyCalls:0,primitiveClicks:0,unexpectedApi:[],mode(value,held=false){mode=value;hold=held},release(value='normal'){hold=false;for(const q of pending.splice(0)){if(value==='error')q.reject(Error(long.repeat(6)));else q.resolve(response(q.range,value))}}}
function response(range,kind){return structuredClone({players:kind==='empty'||kind==='capped'?[]:[player],rangeLimited:kind==='capped',analytics:{range,...windows[range],summary,masters:kind==='empty'?[]:[master],matchups:[]}})}
platform.rankedApi.leaderboard=(faction,range)=>{window.__qa.calls.push({faction,range});if(hold)return new Promise((resolve,reject)=>pending.push({range,resolve,reject}));if(mode==='error')return Promise.reject(Error(long.repeat(6)));return Promise.resolve(response(range,mode))}
platform.rankedApi.history=async()=>{window.__qa.historyCalls++;return structuredClone(mode==='empty'?{honors:[],factionTotals:[]}:normalHistory)}
const policy={version:1,season:{id:'synthetic-season',name:'当前赛季',status:'active',startsAt:'2026-10-01T00:00:00Z'},disasterCardIds:[],matchModes:[],defaultRoomConfig:{matchModeId:'ranked',spectating:'public',handVisibility:'request',disasterMode:'season'},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]}
const fetchOriginal=window.fetch.bind(window)
window.fetch=(input,init)=>{const url=new URL(typeof input==='string'?input:input.url,location.href);if(url.pathname==='/api/operations/effective-policy')return Promise.resolve(new Response(JSON.stringify(policy),{status:200,headers:{'Content-Type':'application/json'}}));if(url.pathname.startsWith('/api/')){window.__qa.unexpectedApi.push(url.pathname);return Promise.reject(Error('Unstubbed synthetic API'))}return fetchOriginal(input,init)}
const {default:RankingsPage}=await import('/src/l12/site/RankingsPage.vue')
const {default:SiteShell}=await import('/src/l12/site/SiteShell.vue')
const {default:UiButton}=await import('/src/l12/site/UiButton.vue')
const {default:UiNotice}=await import('/src/l12/site/UiNotice.vue')
const busy=ref(true)
const Primitives={render:()=>h('section',{id:'primitive-contract'},[h(UiButton,{busy:busy.value,onClick:()=>window.__qa.primitiveClicks++},()=>long),h(UiNotice,{kind:'empty'},()=> '合成共享安静空态'),h(UiNotice,{kind:'error'},()=> '合成共享错误公告')])}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/rankings',component:RankingsPage,meta:{section:'site'}},{path:'/:pathMatch(.*)*',component:RankingsPage,meta:{section:'site'}}]})
await router.push('/rankings');await router.isReady()
const app=createApp({render:()=>h(SiteShell,null,{default:()=>[h(RouterView),h(Primitives)]})}).use(router)
app.mount('#app');window.__qa.unmount=()=>app.unmount();window.__qa.primitiveReady=()=>busy.value=false
`
const server = await createServer({ root, configFile: false, cacheDir: path.join(output, 'vite-cache'), logLevel: 'error',
  define: { 'import.meta.env.VITE_APP_VERSION': JSON.stringify('synthetic-ranking-states') },
  resolve: { alias: { '@': path.join(root, 'src') } }, server: { host: '127.0.0.1', port: 0 },
  plugins: [vue(), tailwindcss(), { name: 'e1-ranking-states-fixture', resolveId(id) { if (id === '/__e1_rankings__.js') return id },
    load(id) { if (id === '/__e1_rankings__.js') return entry }, configureServer(s) { s.middlewares.use('/__e1_rankings', (_req, res) => {
      res.setHeader('Content-Type', 'text/html'); res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module" src="/__e1_rankings__.js"></script></body></html>')
    }) } }] })
let browser, activePage, checks = 0
const results = [], geometry = {}, screenshots = [], issues = []
const check = (condition, message) => { assert(condition, message); checks++ }
const viewportList = [[1920,1080],[1440,900],[1024,500],[844,390],[390,844],[320,568]]
const snapshot = async (page, key) => {
  geometry[key] = await page.locator('.ranking-page').evaluate(root => ({
    page: { width: root.getBoundingClientRect().width, scroll: root.scrollWidth },
    nodes: [...root.querySelectorAll('button,input,select,.empty,.error,.thead,.tr,.summary-strip,dialog[open]')].filter(e => e.getClientRects().length).map(e => {
      const r=e.getBoundingClientRect(),s=getComputedStyle(e);return {tag:e.tagName,class:e.className,text:e.textContent.trim(),x:r.x,y:r.y,width:r.width,height:r.height,fontSize:s.fontSize,padding:s.padding,border:s.borderWidth,display:s.display}
    }) }))
  if (baseline) { assert.deepEqual(geometry[key], baseline.geometry[key], `Unchanged geometry/copy: ${key}`); checks++ }
}
try {
  await server.listen(); browser=await chromium.launch({ channel:'msedge',headless:true })
  const origin=`http://127.0.0.1:${server.httpServer.address().port}`
  for (const [width,height] of viewportList) {
    const page=await browser.newPage({viewport:{width,height}}); activePage=page
    const errors=[], blocked=[];page.on('pageerror',e=>errors.push(e.message))
    await page.route('**/*',route=>{const url=new URL(route.request().url());if(url.hostname!=='127.0.0.1'){blocked.push(url.origin);return route.abort()}if(/\.(png|webp|jpg|svg)(\?|$)/i.test(url.pathname))return route.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="44" height="44"><rect width="44" height="44" fill="#24333a"/></svg>'});return route.continue()})
    await page.goto(origin+'/__e1_rankings');await page.locator('.ranking-page').waitFor()
    check(await page.locator('.site-content.ui-state-scope .ranking-page').count()===1,'Actual RankingsPage inherits actual SiteShell state scope')
    const refresh=()=>page.locator('.ranking-page .page-actions button').last()
    check(await refresh().isDisabled(),'Loading refresh disabled')
    check((await refresh().innerText())==='读取中…','Original loading copy')
    check(await refresh().getAttribute('aria-busy')===(beforeMode?null:'true'),'Busy semantic matches actual loading')
    const initialCalls=await page.evaluate(()=>window.__qa.calls.length)
    const box=await refresh().boundingBox();await page.mouse.click(box.x+box.width/2,box.y+box.height/2)
    await refresh().evaluate(e=>e.click())
    check(await page.evaluate(()=>window.__qa.calls.length)===initialCalls,'Disabled refresh submits no duplicate request')
    check((await page.locator('.player-table .empty').innerText())==='正在读取排位数据…','Loading empty copy unchanged')
    await snapshot(page,`${width}x${height}-loading`)
    await page.evaluate(()=>window.__qa.release());await refresh().filter({hasText:'刷新数据'}).waitFor()
    await page.waitForFunction(()=>!document.querySelector('.ranking-page .page-actions button:last-child').disabled)
    check(await refresh().getAttribute('aria-busy')===null,'Idle refresh does not report busy')
    const selected=async(selector,text)=>{const buttons=page.locator(selector);check(await buttons.locator('.active').count()===0,'No nested active fake control');check(await buttons.filter({hasText:text}).evaluate(e=>e.classList.contains('active')),'Original active class');if(!beforeMode){check(await buttons.filter({hasText:text}).getAttribute('aria-pressed')==='true','Selected control pressed');check((await buttons.evaluateAll(es=>es.filter(e=>e.getAttribute('aria-pressed')==='true').length))===1,'Exactly one pressed control per group')}}
    await selected('.tabs button','玩家榜');await selected('.ranges button','本赛季');await selected('.faction-filter button','全服')
    await snapshot(page,`${width}x${height}-normal`)
    const focusButton=page.locator('.tabs button').first();await focusButton.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab')
    const focus=await focusButton.evaluate(e=>({active:document.activeElement===e,visible:e.matches(':focus-visible'),width:getComputedStyle(e).outlineWidth,style:getComputedStyle(e).outlineStyle}))
    check(focus.active&&focus.visible&&parseFloat(focus.width)>=2&&focus.style==='solid','Actual keyboard focus outline preserved')
    const focusShot=`focus-${width}x${height}.png`;await page.screenshot({path:path.join(output,focusShot)});screenshots.push(focusShot)
    await page.keyboard.press('Tab');await page.keyboard.press('Enter');await selected('.tabs button','主宰榜')
    await page.getByRole('button',{name:'玩家榜',exact:true}).click()
    await page.locator('.ranges button').filter({hasText:'近7天'}).click();await page.waitForFunction(()=>window.__qa.calls.at(-1)?.range==='7d')
    await page.waitForFunction(()=>!document.querySelector('.ranking-page .page-actions button:last-child').disabled)
    await selected('.ranges button','近7天')
    await page.locator('.faction-filter button').filter({hasText:'混沌'}).click();await page.waitForFunction(()=>window.__qa.calls.at(-1)?.faction==='chaos')
    await page.waitForFunction(()=>!document.querySelector('.ranking-page .page-actions button:last-child').disabled)
    await selected('.faction-filter button','混沌')
    await page.getByRole('button',{name:'统计口径',exact:true}).click();const dialog=page.locator('.ranking-statistics-dialog')
    check((await dialog.innerText()).includes('近 7 天')&&(await dialog.innerText()).includes('可跨赛季'),'Original scope copy remains response-bound')
    const dialogBox=await dialog.boundingBox();check(dialogBox.x>=-1&&dialogBox.y>=-1&&dialogBox.x+dialogBox.width<=width+1&&dialogBox.y+dialogBox.height<=height+1,'Scope dialog remains contained')
    await snapshot(page,`${width}x${height}-scope`)
    const scopeShot=`scope-${width}x${height}.png`;await page.screenshot({path:path.join(output,scopeShot)});screenshots.push(scopeShot)
    await page.keyboard.press('Escape');check(!await dialog.isVisible(),'Original Escape protocol');check(await page.getByRole('button',{name:'统计口径',exact:true}).evaluate(e=>document.activeElement===e),'Dialog returns trigger focus')
    await page.getByRole('button',{name:'历史荣誉',exact:true}).click();await selected('.tabs button','历史荣誉')
    check(await page.locator('.ranges button:disabled').count()===3,'History range controls stay disabled')
    const historyCalls=await page.evaluate(()=>window.__qa.calls.length);await page.locator('.ranges button').first().evaluate(e=>e.click());check(await page.evaluate(()=>window.__qa.calls.length)===historyCalls,'History disabled range makes no request')
    await snapshot(page,`${width}x${height}-history`)
    await page.getByRole('button',{name:'玩家榜',exact:true}).click();await page.evaluate(()=>window.__qa.mode('error'));await refresh().click()
    const error=page.locator('.ranking-page>p.error');await error.waitFor()
    check((await error.innerText()).length>100,'Long Chinese error fixture reaches actual consumer')
    check(await error.getAttribute('role')===(beforeMode?null:'alert'),'Error alert semantics')
    check(await error.getAttribute('aria-live')===(beforeMode?null:'assertive'),'Error live semantics')
    check(await error.evaluate(e=>e.scrollWidth<=e.clientWidth+1),'Long Chinese error itself has no horizontal overflow')
    check(await page.locator('.player-table .tr').count()===1,'Failed query preserves last good rows')
    await snapshot(page,`${width}x${height}-error`)
    const scrollBeforeShot=await error.evaluate(e=>{const rows=[];for(;e;e=e.parentElement)rows.push([e.scrollLeft,e.scrollTop]);return rows})
    const errorShot=`error-${width}x${height}.png`;await error.screenshot({path:path.join(output,errorShot)});screenshots.push(errorShot)
    await error.evaluate((e,rows)=>{for(let i=0;e;e=e.parentElement,i++){e.scrollLeft=rows[i][0];e.scrollTop=rows[i][1]}},scrollBeforeShot)
    await page.evaluate(()=>window.__qa.mode('empty'));await refresh().click();await page.getByText('当前筛选下暂无完成定级的玩家',{exact:true}).waitFor()
    check(await page.locator('.player-table .tr').count()===0,'Actual successful empty response has no fake rows')
    check(await page.locator('.ranking-page .empty[role],.ranking-page .empty[aria-live]').count()===0,'Empty remains quiet')
    await snapshot(page,`${width}x${height}-empty-players`)
    await page.getByRole('button',{name:'主宰榜',exact:true}).click();check((await page.locator('.master-table .empty').innerText())==='当前范围暂无主宰数据','Master empty copy unchanged')
    await snapshot(page,`${width}x${height}-empty-masters`)
    await page.getByRole('button',{name:'历史荣誉',exact:true}).click();check((await page.locator('.history-panel .empty').innerText())==='尚无已结算的历史赛季荣誉','History empty copy unchanged')
    await snapshot(page,`${width}x${height}-empty-history`)
    await page.getByRole('button',{name:'对阵一览',exact:true}).click();check((await page.locator('.matrix-panel').innerText()).includes('当前范围暂无对阵数据'),'Matrix empty copy unchanged')
    await snapshot(page,`${width}x${height}-empty-matrix`)
    await page.getByRole('button',{name:'玩家榜',exact:true}).click();await page.evaluate(()=>window.__qa.mode('capped'));await refresh().click();await page.getByText('当前范围无法提供完整排行',{exact:true}).waitFor()
    check(await page.locator('.summary-strip').count()===0,'Capped range does not advertise partial summary')
    check(await page.locator('.ranking-page>p.error').getAttribute('role')==='alert','Existing capped alert retained')
    await snapshot(page,`${width}x${height}-capped`)
    const shot=`states-${width}x${height}.png`;await page.screenshot({path:path.join(output,shot),fullPage:true});screenshots.push(shot)
    for(const name of ['玩家榜','主宰榜','对阵一览','历史荣誉']){await page.locator('.tabs button').filter({hasText:name}).click();await selected('.tabs button',name)}
    await page.getByRole('button',{name:'玩家榜',exact:true}).click()
    for(const name of ['近7天','近30天','本赛季']){await page.locator('.ranges button').filter({hasText:name}).click();await page.waitForFunction(()=>!document.querySelector('.ranking-page .page-actions button:last-child').disabled);await selected('.ranges button',name)}
    for(const name of ['全服','秩序','混沌','命运']){await page.locator('.faction-filter button').filter({hasText:name}).click();await page.waitForFunction(()=>!document.querySelector('.ranking-page .page-actions button:last-child').disabled);await selected('.faction-filter button',name)}
    const spaceTab=page.locator('.tabs button').filter({hasText:'主宰榜'});await spaceTab.focus();await page.keyboard.press('Space');await selected('.tabs button','主宰榜')
    check(await page.locator('#primitive-contract .ui-button').isDisabled(),'Actual shared busy primitive disables its native button')
    await page.locator('#primitive-contract .ui-button').evaluate(e=>e.click());check(await page.evaluate(()=>window.__qa.primitiveClicks)===0,'Shared busy button submits nothing')
    await page.evaluate(()=>window.__qa.primitiveReady());await page.locator('#primitive-contract .ui-button').click();check(await page.evaluate(()=>window.__qa.primitiveClicks)===1,'Shared ready click once')
    check(await page.locator('#primitive-contract [data-ui-kind="empty"]').getAttribute('role')===null,'Shared empty notice quiet')
    check(await page.locator('#primitive-contract [data-ui-kind="error"]').getAttribute('role')==='alert','Shared error notice alert')
    const containment=await page.locator('.ranking-page').evaluate(e=>({left:e.getBoundingClientRect().left,right:e.getBoundingClientRect().right,scroll:e.scrollWidth,client:e.clientWidth,documentScroll:document.documentElement.scrollWidth}))
    check(containment.scroll<=containment.client+1,'Long Chinese state remains in ranking page width')
    check(await page.evaluate(()=>window.__qa.unexpectedApi.length)===0,'Only explicit frozen synthetic APIs are used')
    check(errors.length===0,'No browser runtime errors')
    results.push({viewport:{width,height},focus,containment,errors,blockedOrigins:blocked,calls:await page.evaluate(()=>window.__qa.calls),checks})
    await page.evaluate(()=>window.__qa.unmount());await page.close();activePage=null
  }
  assert.deepEqual(hashes(),sourceHashes,'Every measured source stayed stable');checks++
  if(baseline)for(const file of sourceFiles.filter(f=>f!==sourceFiles[0]&&f!=='scripts/verify-e1-ranking-states.mjs'))check(sourceHashes[file]===baseline.sourceHashes[file],`Unleased dependency unchanged: ${file}`)
  const report={passed:true,phase:beforeMode?'before':'after',checks,results,geometry,screenshots,sourceHashes,sourceContract,
    baselinePath:baselinePath||null,baselineSha:baselinePath?sha(fs.readFileSync(baselinePath)):null,fixtureEntrySha:sha(entry),
    priorRunnerHash:baseline?.sourceHashes['scripts/verify-e1-ranking-states.mjs']||null,
    templateComparison:'Only enumerated ARIA additions removed; only CRLF normalized. Retained r1 runner entity and initial raw-template mismatch preserved.',
    fixturePolicy:'Frozen synthetic API; actual SiteShell/RankingsPage/UiButton/UiNotice; loopback browser only',
    issues,productScope:'Only ranking state ARIA attributes; original DOM, classes, script, style, events and copy preserved',
    actualProductionWrites:0,realIosVerified:false,realImeVerified:false,realScreenReaderVerified:false}
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({passed:true,checks,output}))
}catch(error){await activePage?.screenshot({path:path.join(output,'failure.png')}).catch(()=>{});fs.writeFileSync(path.join(output,'failure.json'),JSON.stringify({passed:false,phase:beforeMode?'before':'after',checks,error:String(error),results,geometry,sourceHashes,sourceContract},null,2));throw error}
finally{await browser?.close();await server.close()}
