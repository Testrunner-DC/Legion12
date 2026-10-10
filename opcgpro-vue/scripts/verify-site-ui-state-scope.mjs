import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT || `D:/GPT/Legion12/artifacts/e1-site-state-scope-${Date.now()}`
assert(!fs.existsSync(output), 'Never overwrite evidence')
fs.mkdirSync(output, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const bound = ['src/l12/site/SiteShell.vue', 'src/l12/site/siteUiStateScope.ts', 'src/l12/site/uiSystem.css',
  ...['AdminAccountsPage','AdminAlternateArtsPanel','RankedMasterTitleRulesModal','ArticleContentRenderer','BattleHubPage','DeckSnapshotViewer']
    .map(name=>`src/l12/site/${name}.vue`)]
const hashSources = () => Object.fromEntries(bound.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const before = hashSources()
const entry = `
import {createApp,h,ref} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.includes('/api/')){window.__qa.unexpected++;throw Error('Unexpected API request')}
 return originalFetch(input,init)
}
window.__qa={unexpected:0,clicks:0,submits:0}
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
window.__qa.invitation=side=>{
 net.l12State.friendInvitation=side==='incoming'?{invitationId:'incoming-id',roomCode:'ABC123',fromAccountId:'synthetic-peer',fromName:'本地邀请'}:null
 net.l12State.outgoingFriendInvitation=side==='outgoing'?{invitationId:'outgoing-id',roomCode:'XYZ123',targetAccountId:'synthetic-peer'}:null
}
platform.authState.initialized=true;platform.authState.verified=true
platform.platformState.account=null;platform.platformState.token=''
platform.telemetryApi.pageView=async()=>{}
const {default:Shell}=await import('/src/l12/site/SiteShell.vue')
const {default:Notice}=await import('/src/l12/site/UiNotice.vue')
const {default:Rules}=await import('/src/l12/site/RankedMasterTitleRulesModal.vue')
const {default:Snapshot}=await import('/src/l12/site/DeckSnapshotViewer.vue')
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/',component:{render:()=>null}},
 {path:'/sandbox',meta:{landscapeCanvas:true},component:{render:()=>null}},
 {path:'/immersive',meta:{immersive:true},component:{render:()=>null}}
]})
await router.push('/');await router.isReady()
window.__qa.navigate=path=>router.push(path)
createApp({setup(){const selected=ref(false),dialog=ref('');window.__qa.showDialog=value=>dialog.value=value;return()=>h(Shell,null,{default:()=>h('section',{class:'legacy-fixture'},[
 h('h1','旧组件状态验收'),
 h('button',{id:'legacy-danger',class:'danger',disabled:true,onClick:()=>window.__qa.clicks++},'不可执行'),
 h('button',{id:'legacy-toggle','aria-pressed':selected.value,onClick:()=>selected.value=!selected.value},'切换状态'),
 h('form',{onSubmit:e=>{e.preventDefault();window.__qa.submits++}},[
 h('input',{id:'legacy-input',required:true,'aria-label':'输入'}),
 h('input',{id:'legacy-invalid','aria-invalid':true,'aria-label':'格式错误'}),
 h('button',{type:'submit'},'提交')]),
 h('article',{class:'ui-card','aria-selected':selected.value,tabindex:0},'卡片'),
 ...['info','success','warning','error','empty'].map(kind=>h(Notice,{kind},()=>kind==='empty'?'暂无内容':'操作状态')),
 h(Rules,{modelValue:dialog.value==='rules','onUpdate:modelValue':()=>dialog.value=''}),
 dialog.value==='snapshot'?h(Snapshot,{entries:[],catalog:[],title:'无主宰快照',onClose:()=>dialog.value=''}):null
 ])})}}).use(router).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(output, 'vite-cache'), logLevel: 'error', server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'site-ui-states-fixture', resolveId(id) { if (id === '/__ui_scope_entry.js') return id }, load(id) { if (id === '/__ui_scope_entry.js') return entry },
  configureServer(s) { s.middlewares.use('/__site_states', (_req, res) => { res.setHeader('Content-Type', 'text/html'); res.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>.legacy-fixture{padding:20px;display:grid;gap:12px}.legacy-fixture button{padding:10px;border:1px solid #555}.legacy-fixture button:disabled{opacity:.4;background:red;color:white}.legacy-fixture input{padding:12px}.legacy-fixture form{display:grid;gap:8px}</style></head><body><div id="app"></div><script type="module" src="/__ui_scope_entry.js"></script></body></html>') }) },
}] })
const results = []; const diagnostics = []; let browser; let activePage; let checks = 0
const check = (condition, message) => { assert(condition, message); checks++ }
try {
  await server.listen(); browser = await chromium.launch({ channel: 'msedge', headless: true })
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  for (const viewport of [{width:320,height:568},{width:390,height:844},{width:700,height:768},{width:701,height:768},{width:844,height:390},{width:1920,height:1080}]) {
    const page = await browser.newPage({ viewport }); const errors = []
    activePage = page
    page.on('pageerror', error => { errors.push(error.message); diagnostics.push({kind:'pageerror',message:error.message}) })
    page.on('console', event => { if(event.type()==='error') diagnostics.push({kind:'console',message:event.text()}) })
    page.on('requestfailed', request => diagnostics.push({kind:'requestfailed',url:request.url(),message:request.failure()?.errorText}))
    await page.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort())
    await page.goto(`${origin}/__site_states`); await page.locator('#legacy-danger').waitFor()
    check(await page.locator('.site-content').evaluate(e=>e.classList.contains('ui-state-scope')), 'ordinary page participates')
    const paint = () => page.locator('#legacy-danger').evaluate(e=>{const s=getComputedStyle(e);return {background:s.backgroundColor,opacity:s.opacity,cursor:s.cursor,width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height}})
    const state = await paint()
    await page.locator('.site-content').evaluate(e=>e.classList.remove('ui-state-scope'))
    const baseline = await paint()
    await page.locator('.site-content').evaluate(e=>e.classList.add('ui-state-scope'))
    check(state.background === 'rgb(21, 29, 34)' && state.opacity === '1' && state.cursor === 'not-allowed', 'legacy danger disabled shares state')
    await page.locator('#legacy-danger').evaluate(e=>e.click()); check(await page.evaluate(()=>window.__qa.clicks)===0, 'native disabled remains inert')
    await page.locator('#legacy-toggle').focus(); await page.keyboard.press('Tab'); await page.keyboard.press('Shift+Tab')
    check(await page.locator('#legacy-toggle').evaluate(e=>getComputedStyle(e).outlineColor)==='rgb(85, 199, 206)', 'legacy control keyboard focus visible')
    await page.locator('#legacy-toggle').click(); check(await page.locator('#legacy-toggle').getAttribute('aria-pressed')==='true', 'toggle semantics unchanged')
    await page.locator('#legacy-input').fill('测试'); await page.locator('#legacy-input').press('Enter'); check(await page.evaluate(()=>window.__qa.submits)===1, 'native form submits once')
    check(await page.locator('#legacy-invalid').evaluate(e=>getComputedStyle(e).borderTopColor)==='rgb(212, 107, 116)', 'invalid input shares state')
    check(await page.locator('[data-ui-kind=error]').getAttribute('role')==='alert' && await page.locator('[data-ui-kind=empty]').getAttribute('role')===null, 'error and quiet empty semantics')
    check(await page.locator('.site-content').evaluate(e=>e.scrollWidth<=e.clientWidth+1), 'no horizontal overflow')
    await page.screenshot({path:path.join(output,`site-${viewport.width}x${viewport.height}.png`)})
    await page.evaluate(()=>window.__qa.showDialog('rules'))
    const rules=page.getByRole('dialog',{name:'“最强”称号规则'})
    await rules.waitFor()
    const closeRules=rules.getByRole('button',{name:'关闭最强主宰称号规则'})
    await closeRules.focus(); await page.keyboard.press('Tab'); await page.keyboard.press('Shift+Tab')
    check(await closeRules.evaluate(e=>getComputedStyle(e).outlineColor)==='rgb(85, 199, 206)', 'actual detached title dialog focus shares state')
    await page.screenshot({path:path.join(output,`rules-${viewport.width}x${viewport.height}.png`)})
    await page.keyboard.press('Escape'); check(await rules.count()===0, 'actual title dialog Escape remains usable')
    await page.evaluate(()=>window.__qa.showDialog('snapshot'))
    const snapshot=page.getByRole('dialog',{name:'无主宰快照'}); await snapshot.waitFor()
    check(await snapshot.locator('footer button:disabled').count()===3, 'actual invalid snapshot actions remain disabled')
    check(await snapshot.locator('footer button:disabled').evaluateAll(es=>es.every(e=>getComputedStyle(e).backgroundColor==='rgb(21, 29, 34)' && getComputedStyle(e).opacity==='1')), 'actual detached snapshot primary disabled shares state')
    await snapshot.locator('footer button').evaluateAll(es=>es.forEach(e=>e.click()))
    check(await page.evaluate(()=>window.__qa.unexpected)===0, 'invalid snapshot cannot send save requests')
    await page.screenshot({path:path.join(output,`snapshot-${viewport.width}x${viewport.height}.png`)})
    await snapshot.getByRole('button',{name:'关闭构筑'}).click(); check(await snapshot.count()===0, 'actual snapshot remains closable')
    for(const side of ['incoming','outgoing']) {
      await page.evaluate(side=>window.__qa.invitation(side),side)
      const card=page.locator(side==='incoming'?'.invitation-gate':'.outgoing-invitation-gate'); await card.waitFor()
      const geometry=await page.locator('.invitation-stack').evaluate(e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return {right:innerWidth-r.right,bottom:innerHeight-r.bottom,top:r.top,left:r.left,position:s.position,z:Number(s.zIndex),pointer:s.pointerEvents}})
      check(geometry.position==='fixed' && geometry.z===160 && Math.abs(geometry.right-18)<1 && Math.abs(geometry.bottom-18)<1, 'A3 actual invitation retains right/bottom placement and layer')
      check(geometry.top>=17 && geometry.left>=17, 'A3 invitation stays inside short and narrow viewport')
      check(geometry.pointer==='none' && await page.locator('.site-modal-mask').count()===0, 'A3 invitation does not install blocking backdrop')
      if(side==='incoming') check(await card.getByRole('dialog').getAttribute('aria-modal')==='false', 'A3 incoming invitation remains non-modal')
      await page.screenshot({path:path.join(output,`invitation-${side}-${viewport.width}x${viewport.height}.png`)})
      await card.getByRole('button',{name:side==='incoming'?'最小化好友对战邀请':'最小化已发送对战邀请'}).click()
      check(await card.locator('.invitation-minimized').count()===1, 'A3 invitation can minimize')
      await card.locator('.invitation-minimized').click()
      check(await card.locator('.invitation-modal').count()===1, 'A3 invitation can expand')
    }
    await page.evaluate(()=>window.__qa.invitation(null))
    for (const route of ['/sandbox', '/immersive']) {
      await page.evaluate(path=>window.__qa.navigate(path), route)
      check(!(await page.locator('.site-content').evaluate(e=>e.classList.contains('ui-state-scope'))), 'canvas and immersive contents isolated')
      const isolated = await paint(); check(isolated.background===baseline.background && isolated.opacity===baseline.opacity && isolated.cursor===baseline.cursor, 'non-site disabled appearance unchanged')
      check(Math.abs(isolated.width-state.width)<1 && Math.abs(isolated.height-state.height)<1, 'state adoption does not change geometry')
    }
    await page.evaluate(()=>window.__qa.navigate('/'))
    check((await paint()).background==='rgb(21, 29, 34)', 'route restoration reapplies state')
    check(errors.length===0 && await page.evaluate(()=>window.__qa.unexpected)===0, 'no page errors or unexpected APIs')
    results.push({viewport,checks,errors}); await page.close()
  }
  assert.deepEqual(hashSources(), before, 'source remained stable')
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',checks,results,sourceHashes:before,realIosVerified:false},null,2))
  console.log(`Site UI state scope passed: ${checks}; evidence ${output}`)
} catch(error) {
  if(activePage) await activePage.screenshot({path:path.join(output,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'failed',checks,results,error:String(error),diagnostics,sourceHashes:before},null,2)); throw error
} finally { await browser?.close(); await server.close() }
