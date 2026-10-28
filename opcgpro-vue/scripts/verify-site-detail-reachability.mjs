import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import { closeNotificationProbeResources as closeProbeResources } from './verify-notification-real-chain.mjs'

const scriptPath = fileURLToPath(import.meta.url)
const root = path.resolve(path.dirname(scriptPath), '..')

function requiredEnvironment(name) {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is required`)
  return path.resolve(value)
}

const output = requiredEnvironment('L12_QA_OUT')
const cacheDir = requiredEnvironment('L12_QA_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
const storageGuard = path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + 'Assert-L12GeneratedPath -Path $env:L12_ACCEPTANCE_OUT -Purpose "site detail reachability acceptance" | Out-Null; '
  + 'Assert-L12GeneratedPath -Path $env:L12_ACCEPTANCE_CACHE -Purpose "site detail reachability cache" | Out-Null; '
  + 'foreach($name in @("TEMP","TMP","TMPDIR")){'
  + '$value=[Environment]::GetEnvironmentVariable($name,"Process"); '
  + 'if([string]::IsNullOrWhiteSpace($value)){throw "L12_TEMP_PATH_MISSING: $name"}; '
  + 'Assert-L12GeneratedPath -Path $value -Purpose "site detail reachability $name" | Out-Null}',
], {
  env: { ...process.env, L12_STORAGE_GUARD: storageGuard, L12_ACCEPTANCE_OUT: output, L12_ACCEPTANCE_CACHE: cacheDir },
  stdio: 'pipe', timeout: 15_000, windowsHide: true,
})
if (fs.existsSync(output) && fs.readdirSync(output).length)
  throw new Error(`Refusing to overwrite non-empty evidence directory: ${output}`)
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(cacheDir, { recursive: true })

const sourceSha = crypto.createHash('sha256').update(fs.readFileSync(scriptPath)).digest('hex')
const progressPath = path.join(output, 'progress.ndjson')
const appendProgress = value => fs.appendFileSync(progressPath, `${JSON.stringify({ schemaVersion: 1, sourceSha, at: new Date().toISOString(), ...value })}\n`)

const require = createRequire(import.meta.url)
const playwrightRoot = process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
const { chromium } = require(playwrightRoot)

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/mobileViewport.css'

localStorage.setItem('l12-auth-token','qa-token')
localStorage.setItem('l12-account',JSON.stringify({id:'qa-author',username:'移动验收作者',role:'player',createdAt:'2026-10-21T00:00:00Z',publicHistory:true,permissionVersion:1}))
const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-token'
platform.platformState.account={id:'qa-author',username:'移动验收作者',role:'player',createdAt:'2026-10-21T00:00:00Z',publicHistory:true,permissionVersion:1}
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false

const cover='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 450"><rect width="800" height="450" fill="#10262e"/><path d="M0 360L300 80l500 310" fill="none" stroke="#c9a950" stroke-width="12"/><text x="45" y="95" fill="#fff" font-size="48">十二军团</text></svg>')
const article=(id,kind)=>({id,title:(kind==='product'?'第一弹补充包产品详情':'赛事教学视频详情'),summary:'移动端列表进入详情的验收内容。',body:'这里是应当能够从列表进入阅读的完整正文。',category:kind==='product'?'产品':'视频',coverUrl:cover,link:'/__linked_detail__?kind='+kind,slug:id,pinned:false,status:'published',hasUnpublishedChanges:false,createdAt:'2026-10-21T00:00:00Z',updatedAt:'2026-10-21T00:00:00Z',publishedAt:'2026-10-21T00:00:00Z',author:'十二军团官方',videoAuthorName:kind==='video'?'官方频道':'',updatedBy:'admin',revision:1,kind,sortOrder:1})
platform.articleApi.list=async({kind}={})=>[article(kind+'-detail',kind)]
platform.articleApi.summaries=async query=>(await platform.articleApi.list(query)).map(({body,bodyMedia,...row})=>({...row,bodyPreview:body}))
platform.articleApi.get=async id=>article(id,'news')
platform.siteContentApi.categories=async kind=>[{id:kind+'-category',kind,name:kind==='product'?'产品':'视频',sortOrder:1,createdAt:'',updatedAt:''}]

const originalFetch=window.fetch.bind(window)
window.__qaRequests=[]
window.fetch=async(input,init={})=>{
  const url=String(typeof input==='string'?input:input.url)
  const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
  if(url.includes('/api/auth/password/forgot')){window.__qaRequests.push({kind:'forgot',body:init.body});return json({message:'如邮箱已绑定并完成验证，重置邮件已发送'})}
  if(url.includes('/api/auth/password/reset')){window.__qaRequests.push({kind:'reset',body:init.body});return json({message:'密码已重置'})}
  if(url.includes('/api/auth/logout'))return json({})
  if(url.includes('/api/alternate-arts'))return json([])
  if(url.includes('/api/telemetry/page-view'))return json({})
  if(url.includes('/api/me/alternate-art-grant-notifications'))return json([])
  if(url.includes('/api/me/season-summary-notifications'))return json([])
  if(url.includes('/api/decks/summaries')){const query=new URL(url).searchParams;window.__qaRequests.push({kind:'deck-summaries',url});return json({items:[],total:0,page:Number(query.get('page')||1),pageSize:Number(query.get('pageSize')||20),generation:1,permissionVersion:1,catalogVersion:'A'.repeat(64),policyVersion:1,facets:{masters:[],legal:0,illegal:0}})}
  if(url.endsWith('/api/decks')&&String(init.method||'GET').toUpperCase()==='POST'){
    const submitted=JSON.parse(String(init.body||'{}'))
    window.__qaRequests.push({kind:'deck-save',body:submitted})
    return json({...submitted,id:crypto.randomUUID(),revision:1,updatedAt:new Date().toISOString()})
  }
  return originalFetch(input,init)
}

const {loadDeckCatalog,loadOfficialPresetDecks}=await import('/src/l12/decks.ts')
const catalog=await loadDeckCatalog()
const placeholder='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 250 350"><rect width="250" height="350" fill="#18252b"/><text x="125" y="180" fill="#e8d59c" text-anchor="middle" font-size="24">十二军团</text></svg>')
catalog.forEach(card=>{card.imageUrl=placeholder})
const preset=(await loadOfficialPresetDecks())[0]
const now='2026-10-21T00:00:00Z'
const generation={id:'qa-public',publicCode:'qa-public',readToken:'b'.repeat(64),catalogVersion:'qa-catalog',policyVersion:1}
const counts={main:preset.cardIds.length,uncountedMain:0,morale:preset.moraleIds.length,special:(preset.specialIds||[]).length,bench:(preset.benchIds||[]).length}
const deck={...preset,name:'公开牌库移动端入口验收',publicationId:'qa-public',publicationVersion:1,specialIds:preset.specialIds||[],updatedAt:now}
const current=()=>({summary:{id:'qa-public',source:'public',name:deck.name,masterId:deck.masterId,masterName:catalog.find(card=>card.id===deck.masterId)?.nameZh||'主宰',faction:'order',author:'移动验收作者',publicCode:'qa-public',publicationVersion:1,createdAt:now,updatedAt:now,counts,legal:true,legalityReason:null,environment:{status:'known',value:'2.5',reason:null},views:12,likes:3,copies:2,viewerLiked:false,canEdit:true,readToken:null},version:1,deck,guide:{buildIdea:'',opening:'',keyCards:'',commonSequence:'',substitutions:''},matchups:[],contentRevision:0,contentUpdatedAt:null,readToken:generation.readToken,catalogVersion:generation.catalogVersion,policyVersion:generation.policyVersion})
platform.publicDeckApi.get=async()=>{throw new Error('legacy detail API must not be called')}
platform.publicDeckReadApi.current=async()=>current()
platform.publicDeckReadApi.versions=async(_reference,page,pageSize,pin)=>({...generation,items:[],total:0,page,pageSize,canEdit:true})
platform.publicDeckReadApi.statistics=async(_reference,page,pageSize,pin)=>({...generation,from:now,to:now,recentDays:90,games:0,sampleStatus:'empty',groups:[],total:0,page,pageSize})
platform.publicDeckApi.counter=async(_reference,kind)=>({id:'qa-public',publicCode:'qa-public',views:kind==='view'?13:13,likes:3,copies:kind==='copy'?3:2,viewerLiked:false,canEdit:true})

const [{default:SiteShell},{default:NewsPage},{default:PublicDeckDetailPage},{default:AccountRecoveryPage}]=await Promise.all([
  import('/src/l12/site/SiteShell.vue'),import('/src/l12/site/NewsPage.vue'),import('/src/l12/site/PublicDeckDetailPage.vue'),import('/src/l12/site/AccountRecoveryPage.vue')
])
const EditorLanding={render:()=>h('main',{'data-qa':'deck-editor-landing'},[h('h1','牌库编辑器入口已打开')])}
const Home={render:()=>h('main',[h('h1','我的')])}
const routes=[
 {name:'home',path:'/',component:Home},
 {name:'products',path:'/products',component:NewsPage},
 {name:'videos',path:'/videos',component:NewsPage},
 {path:'/news',component:Home},{path:'/battle',component:Home},{path:'/cards',component:Home},{path:'/rules',component:Home},
 {name:'public-decks',path:'/decks',component:Home},
 {name:'public-deck-detail',path:'/decks/:deckId',component:PublicDeckDetailPage},
 {name:'deck-editor',path:'/deck-editor',component:EditorLanding},
 {name:'account-recovery',path:'/auth/recovery',component:AccountRecoveryPage},
 {name:'me',path:'/me',component:Home},
]
const router=createRouter({history:createMemoryHistory(),routes})
const target=new URLSearchParams(location.search).get('route')||'/products'
await router.push(target)
await router.isReady()
window.__qaRouter=router
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'site-detail-reachability-fixture',
    resolveId(id) { if (id === '/__site_detail_reachability__.js') return id },
    load(id) { if (id === '/__site_detail_reachability__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.startsWith('/__linked_detail__')) {
          const kind = new URL(request.url, 'http://127.0.0.1').searchParams.get('kind')
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<main data-qa="linked-detail"><h1>'+(kind === 'product' ? '产品详情已到达' : '视频详情已到达')+'</h1></main>')
          return
        }
        if (request.url?.match(/^\/__site_detail_reachability__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<head><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#fff}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__site_detail_reachability__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 390, height: 844, label: '390x844' },
  { width: 844, height: 390, label: '844x390' },
]
const flows = ['products-detail', 'videos-detail', 'public-deck-actions', 'account-recovery']
const report = { schemaVersion: 1, sourceSha, status: 'running', matrix: [], boundaries: [], failures: [], consoleErrors: [], requestFailures: [], fixtureTransport: 'local Vite + local static data + mocked component APIs only' }
let browser

function recordFailure(test, error) {
  const failure = { test, message: error instanceof Error ? error.message : String(error) }
  report.failures.push(failure)
  appendProgress({ stage: 'case-failed', ...failure })
}

async function controlGeometry(locator) {
  await locator.scrollIntoViewIfNeeded()
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const center = document.elementFromPoint(Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2)), Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2)))
    const style = getComputedStyle(element)
    return {
      tag: element.tagName.toLowerCase(), width: rect.width, height: rect.height,
      displayed: style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
      inViewport: rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth,
      hit: center === element || element.contains(center),
    }
  })
}

function assertTouchable(geometry, label) {
  assert.equal(geometry.displayed, true, `${label} must be displayed`)
  assert.equal(geometry.inViewport, true, `${label} must be reachable after scroll`)
  assert.equal(geometry.hit, true, `${label} center must receive the touch`)
  assert.ok(geometry.width >= 38 && geometry.height >= 38, `${label} touch target must meet the existing 38x38 usability floor; got ${geometry.width.toFixed(1)}x${geometry.height.toFixed(1)}`)
}

function touchIssue(geometry, label) {
  try { assertTouchable(geometry, label); return null }
  catch (error) { return error instanceof Error ? error.message : String(error) }
}

async function openFixture(page, port, route) {
  await page.goto(`http://127.0.0.1:${port}/__site_detail_reachability__?route=${encodeURIComponent(route)}`)
  await page.locator('#app > *').first().waitFor({ state: 'visible', timeout: 15000 })
}

async function testListDetail(page, port, kind, viewport) {
  const route = kind === 'product' ? '/products' : '/videos'
  await openFixture(page, port, route)
  const entry = page.locator(kind === 'product' ? '.list-entry a[href]' : 'a.video-card[href]').first()
  await entry.waitFor({ state: 'visible' })
  const geometry = await controlGeometry(entry)
  await entry.tap()
  await page.locator('[data-qa="linked-detail"]').waitFor({ state: 'visible', timeout: 5000 })
  const reached = await page.locator('[data-qa="linked-detail"]').innerText()
  assert.match(reached, kind === 'product' ? /产品详情已到达/ : /视频详情已到达/)
  assertTouchable(geometry, `${kind} detail entry`)
  return { geometry, reached, url: page.url() }
}

async function testDeckActions(page, port) {
  await openFixture(page, port, '/decks/qa-public')
  const edit = page.getByRole('button', { name: '编辑牌库', exact: true })
  await edit.waitFor({ state: 'visible', timeout: 15000 })
  const editGeometry = await controlGeometry(edit)
  await edit.tap()
  await page.waitForTimeout(500)
  if (!await page.locator('[data-qa="deck-editor-landing"]').count()) {
    const feedback = await page.locator('.notice').allInnerTexts()
    const requests = await page.evaluate(() => window.__qaRequests)
    throw new Error(`编辑牌库未进入编辑器；页面反馈=${feedback.join(' | ') || '无'}；夹具请求=${JSON.stringify(requests)}`)
  }
  const editRoute = await page.evaluate(() => window.__qaRouter.currentRoute.value.fullPath)

  await openFixture(page, port, '/decks/qa-public')
  const copy = page.getByRole('button', { name: '复制到我的牌库', exact: true })
  await copy.waitFor({ state: 'visible', timeout: 15000 })
  const copyGeometry = await controlGeometry(copy)
  await copy.tap()
  await page.getByText(/已复制《.*》到我的牌库/).waitFor({ timeout: 10000 })
  const copyFeedback = await page.locator('.notice').first().innerText()
  const issues = [touchIssue(copyGeometry, '复制到我的牌库'), touchIssue(editGeometry, '编辑牌库')].filter(Boolean)
  assert.equal(issues.length, 0, `${issues.join('; ')}; copy feedback=${copyFeedback}; edit route=${editRoute}`)
  return { copyFeedback, editRoute, copyGeometry, editGeometry }
}

async function testRecovery(page, port) {
  await openFixture(page, port, '/auth/recovery?mode=reset-password#token=synthetic-retired-token')
  await page.getByRole('heading', { name: '邮箱功能已停用', exact: true }).waitFor()
  assert.equal(await page.locator('.recovery-card input, .recovery-card button, .recovery-card form').count(), 0)
  assert.equal(await page.evaluate(() => location.hash), '')
  const requests = await page.evaluate(() => window.__qaRequests)
  assert.equal(requests.filter(item => item.kind === 'forgot' || item.kind === 'reset').length, 0)
  const back = page.getByRole('link', { name: /返回登录 \/ 我的/ })
  const backGeometry = await controlGeometry(back)
  await back.tap()
  await page.getByRole('heading', { name: '我的', exact: true }).waitFor({ timeout: 5000 })
  const backRoute = await page.evaluate(() => window.__qaRouter.currentRoute.value.fullPath)
  assert.equal(touchIssue(backGeometry, '返回登录 / 我的'), null)
  return { disabled: true, recoveryRequests: 0, fragmentRemoved: true, backGeometry, backRoute }
}
async function testResponsiveBoundary(page, port, profile, target) {
  if (target === 'product-link') {
    await openFixture(page, port, '/products')
    const link = page.locator('.list-entry a[href]').first()
    await link.waitFor({ state: 'visible' })
    const geometry = await controlGeometry(link)
    if (profile.hasTouch) await link.tap(); else await link.click()
    await page.locator('[data-qa="linked-detail"]').waitFor({ state: 'visible', timeout: 5000 })
    return { geometry, reached: await page.locator('[data-qa="linked-detail"]').innerText() }
  }
  await openFixture(page, port, '/auth/recovery')
  const link = page.getByRole('link', { name: /返回登录 \/ 我的/ })
  await link.waitFor({ state: 'visible' })
  const geometry = await controlGeometry(link)
  if (profile.hasTouch) await link.tap(); else await link.click()
  await page.getByRole('heading', { name: '我的', exact: true }).waitFor({ timeout: 5000 })
  return { geometry, reached: await page.evaluate(() => window.__qaRouter.currentRoute.value.fullPath) }
}

try {
  appendProgress({ stage: 'started', matrix: { flows, viewports } })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const context = await browser.newContext({ deviceScaleFactor: 2, hasTouch: true, isMobile: true })
  const page = await context.newPage()
  page.on('pageerror', error => report.consoleErrors.push(error.message))
  page.on('requestfailed', request => report.requestFailures.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText || 'failed'}`))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    for (const flow of flows) {
      const test = `${flow}-${viewport.label}`
      appendProgress({ stage: 'case-started', test })
      try {
        const evidence = flow === 'products-detail' ? await testListDetail(page, port, 'product', viewport)
          : flow === 'videos-detail' ? await testListDetail(page, port, 'video', viewport)
            : flow === 'public-deck-actions' ? await testDeckActions(page, port)
              : await testRecovery(page, port)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `${test} must not horizontally overflow`)
        report.matrix.push({ test, status: 'passed', evidence })
        appendProgress({ stage: 'case-passed', test })
      } catch (error) {
        recordFailure(test, error)
        report.matrix.push({ test, status: 'failed', message: error instanceof Error ? error.message : String(error) })
        await page.screenshot({ path: path.join(output, `${test}-failed.png`), fullPage: true }).catch(() => {})
      }
    }
  }
  const boundaryProfiles = [
    { label: 'wide-fine', width: 844, height: 390, hasTouch: false, isMobile: false, expanded: false },
    { label: 'narrow-fine', width: 390, height: 844, hasTouch: false, isMobile: false, expanded: true },
    { label: 'wide-coarse', width: 844, height: 390, hasTouch: true, isMobile: true, expanded: true },
  ]
  for (const profile of boundaryProfiles) {
    const boundaryContext = await browser.newContext({ deviceScaleFactor: 2, hasTouch: profile.hasTouch, isMobile: profile.isMobile })
    const boundaryPage = await boundaryContext.newPage()
    boundaryPage.on('pageerror', error => report.consoleErrors.push(error.message))
    boundaryPage.on('requestfailed', request => report.requestFailures.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText || 'failed'}`))
    await boundaryPage.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    await boundaryPage.setViewportSize({ width: profile.width, height: profile.height })
    for (const target of ['product-link', 'recovery-return']) {
      const test = `boundary-${profile.label}-${target}`
      appendProgress({ stage: 'case-started', test })
      try {
        const evidence = await testResponsiveBoundary(boundaryPage, port, profile, target)
        const issue = touchIssue(evidence.geometry, `${profile.label} ${target}`)
        if (profile.expanded) assert.equal(issue, null, `${profile.label} ${target} must apply the 38px narrow/coarse target`)
        else assert.ok(evidence.geometry.height < 38, `${profile.label} ${target} must retain the compact fine-pointer link; got ${evidence.geometry.height.toFixed(1)}px`)
        report.boundaries.push({ test, status: 'passed', evidence })
        appendProgress({ stage: 'case-passed', test })
      } catch (error) {
        recordFailure(test, error)
        report.boundaries.push({ test, status: 'failed', message: error instanceof Error ? error.message : String(error) })
        await boundaryPage.screenshot({ path: path.join(output, `${test}-failed.png`), fullPage: true }).catch(() => {})
      }
    }
    await boundaryContext.close()
  }
  assert.equal(report.consoleErrors.length, 0, `page errors: ${report.consoleErrors.join(' | ')}`)
  report.status = report.failures.length ? 'failed' : 'passed'
} catch (error) {
  recordFailure('harness', error)
  report.status = 'failed'
} finally {
  report.cleanupResourceFailures = await closeProbeResources(browser, server)
  for (const resource of report.cleanupResourceFailures) recordFailure(`cleanup-${resource}`, new Error('Owned resource failed to close'))
  if (report.cleanupResourceFailures.length) report.status = 'failed'
  report.completedAt = new Date().toISOString()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  appendProgress({ stage: 'completed', status: report.status, failures: report.failures.length })
}

console.log(JSON.stringify({ status: report.status, output, sourceSha, cases: report.matrix.length, failures: report.failures.length }, null, 2))
if (report.status !== 'passed') process.exitCode = 1
