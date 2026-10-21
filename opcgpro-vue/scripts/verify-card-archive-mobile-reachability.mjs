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
const repo = path.resolve(root, '..')

function requiredEnvironment(name) {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`${name} is required`)
  return path.resolve(value)
}

const output = requiredEnvironment('L12_ARCHIVE_MOBILE_OUT')
const cacheDir = requiredEnvironment('L12_ARCHIVE_MOBILE_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
assert.ok(path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + '@($env:L12_ACCEPTANCE_OUT,$env:L12_ACCEPTANCE_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) '
  + '| ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "card archive mobile reachability" | Out-Null}',
], {
  env: { ...process.env, L12_STORAGE_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'), L12_ACCEPTANCE_OUT: output, L12_ACCEPTANCE_CACHE: cacheDir },
  stdio: 'pipe', timeout: 15_000, windowsHide: true,
})
if (fs.existsSync(output) && fs.readdirSync(output).length)
  throw new Error(`Refusing to overwrite non-empty evidence directory: ${output}`)
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(cacheDir, { recursive: true })

const sourceSha = crypto.createHash('sha256').update(fs.readFileSync(scriptPath)).digest('hex')
const commit = process.env.L12_SOURCE_COMMIT?.trim()
if (!commit) throw new Error('L12_SOURCE_COMMIT is required')
const progressPath = path.join(output, 'progress.ndjson')
const appendProgress = value => fs.appendFileSync(progressPath,
  `${JSON.stringify({ schemaVersion: 1, sourceSha, commit, at: new Date().toISOString(), ...value })}\n`)

const require = createRequire(import.meta.url)
const playwrightRoot = process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
const { chromium } = require(playwrightRoot)

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import {cardTypeFilterKey} from '/src/l12/cardPresentation.ts'
import {groupArchiveCards} from '/src/l12/cardArchiveVersions.ts'
import {loadCardArchiveCatalog} from '/src/l12/decks.ts'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'

const account={id:'qa-archive-player',username:'图鉴移动验收者',role:'player',createdAt:'2026-10-01T00:00:00Z',publicHistory:true,permissionVersion:3}
localStorage.setItem('l12-auth-token','qa-archive-token')
localStorage.setItem('l12-account',JSON.stringify(account))
const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-archive-token'
platform.platformState.account=account
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false
platform.friendApi.presence=async()=>[]
platform.alternateArtApi.notifications=async()=>[]

const catalog=await loadCardArchiveCatalog()
const groups=groupArchiveCards(catalog)
const combinations=new Map()
for(const group of groups)for(const version of group.versions)for(const product of version.products||[]){
 const key=JSON.stringify([cardTypeFilterKey(version.cardType),product])
 if(!combinations.has(key))combinations.set(key,new Set())
 combinations.get(key).add(group.logicalId)
}
const target=[...combinations.entries()].map(([key,ids])=>({key,ids,count:ids.size})).filter(item=>item.count>=2).sort((a,b)=>b.count-a.count)[0]
if(!target)throw new Error('No type/product filter pair with at least two logical cards')
const [targetType,targetProduct]=JSON.parse(target.key)
const filtered=groups.filter(group=>group.versions.some(card=>cardTypeFilterKey(card.cardType)===targetType&&(card.products||[]).includes(targetProduct)))
  .sort((a,b)=>a.defaultVersion.nameZh.localeCompare(b.defaultVersion.nameZh,'zh-CN'))
const base=groups.find(group=>group.versions.length>1)?.defaultVersion||catalog[0]
if(!base)throw new Error('Card archive fixture requires a base card')
const art={id:'qa-owned-art',kind:'alternate-art',artCode:base.id+'QA',baseCardId:base.id,displayName:'移动权益异画',mediaAssetId:'qa-owned-media',imageUrl:'/api/site/media/qa-owned.svg',thumbnailUrl:'/api/site/media/qa-owned.svg',active:true,createdAt:'2026-10-22T00:00:00Z',updatedAt:'2026-10-22T00:00:00Z',productId:'qa-product',productName:targetProduct,products:[{id:'qa-product',name:targetProduct}],rarities:[{id:'qa-rarity',name:'移动验收罕贵'}],cardImageId:'',builtIn:false,baseCardName:base.nameZh,grantedAt:'2026-10-22T00:00:00Z',grantReason:'移动验收'}
const gallery={id:'qa-gallery-card',kind:'gallery-card',artCode:'QA-GALLERY-001',baseCardId:'',displayName:'移动联动画廊卡',mediaAssetId:'qa-gallery-media',imageUrl:'/api/site/media/qa-gallery.svg',thumbnailUrl:'/api/site/media/qa-gallery.svg',active:true,createdAt:'2026-10-22T00:00:00Z',updatedAt:'2026-10-22T00:00:00Z',productId:'qa-product',productName:targetProduct,products:[{id:'qa-product',name:targetProduct}],rarities:[{id:'qa-rarity',name:'联动罕贵'}],cardImageId:'',builtIn:false,effect:'联动展示卡效文本',artist:'移动验收画师'}
platform.alternateArtApi.gallery=async()=>[art,gallery]
platform.alternateArtApi.mine=async()=>[art]

const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
 const parsed=new URL(String(typeof input==='string'?input:input.url),location.origin)
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(parsed.pathname==='/api/operations/effective-policy')return json({version:1,season:{},disasterCardIds:[],matchModes:[],defaultRoomConfig:{},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]})
 if(parsed.pathname.startsWith('/api/'))return json([])
 return originalFetch(input,init)
}

const [{default:SiteShell},{default:CardArchive}]=await Promise.all([import('/src/l12/site/SiteShell.vue'),import('/src/l12/CardArchive.vue')])
const Stub={render:()=>h('main','占位页面')}
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/',component:Stub},{path:'/cards',component:CardArchive},{path:'/battle',component:Stub,meta:{section:'battle'}},{path:'/decks',component:Stub},
]})
await router.push('/cards')
await router.isReady()
window.__archiveQa={filter:{type:targetType,product:targetProduct,expectedCount:filtered.length,firstName:filtered[0].defaultVersion.nameZh,total:groups.length},galleryName:gallery.displayName,baseName:base.nameZh,router}
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

const server = await createServer({
  root, configLoader: 'runner', cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'card-archive-mobile-reachability-fixture',
    resolveId(id) { if (id === '/__card_archive_mobile__.js') return id },
    load(id) { if (id === '/__card_archive_mobile__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__card_archive_mobile__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__card_archive_mobile__.js"></script></body></html>')
          return
        }
        next()
      })
    },
  }],
})

const acceptanceMode = process.env.L12_ARCHIVE_REACHABILITY_MODE?.trim() || 'mobile'
assert.ok(['mobile', 'wide-only'].includes(acceptanceMode), `Unsupported L12_ARCHIVE_REACHABILITY_MODE: ${acceptanceMode}`)
const wideOnly = acceptanceMode === 'wide-only'
const viewports = wideOnly ? [
  { width: 1366, height: 900, label: '1366x900-fine-mouse' },
] : [
  { width: 390, height: 844, label: '390x844' },
  { width: 844, height: 390, label: '844x390' },
]
const mobileFlows = [
  { id: 'filter-reset', purpose: '筛选抽屉选择类型/产品/排序，应用核结果，再由摘要重开并重置' },
  { id: 'catalog-detail', purpose: '右侧详情栏隐藏时，以单次真实触摸打开多版本原卡详情、切换版本并关闭恢复焦点' },
  { id: 'gallery-detail', purpose: '切换画廊，以单次真实触摸打开独立联动卡详情并关闭恢复焦点，不创建独立版本系统' },
]
const flows = wideOnly ? [
  { id: 'wide-catalog-boundary', purpose: '桌面侧栏保持可见；单击仅选中，Enter与双击继续打开共享详情，原卡版本切换保持' },
] : mobileFlows
const report = {
  schemaVersion: 1, sourceSha, commit, status: 'prepared', acceptanceMode,
  fixtureTransport: 'actual SiteShell and CardArchive with the real catalog; synthetic alternate-art gallery and entitlement APIs; no real service writes',
  interactionBoundary: wideOnly
    ? 'The wide-only boundary uses a 1366x900 fine-pointer mouse context. It explicitly exercises click, Enter and dblclick without touchscreen emulation.'
    : 'All buttons/cards use Playwright touchscreen tap. Native selects are first tapped and then changed with selectOption because headless browsers do not expose the operating-system picker. dblclick and Enter are forbidden.',
  usabilityFloorPx: 38, advisoryTargetPx: 44, viewports, flows, matrix: [], advisories: [], failures: [], pageErrors: [], requestFailures: [],
}
let browser

function failureMessage(error) { return error instanceof Error ? error.message : String(error) }
function recordFailure(test, stage, error) {
  const failure = { test, stage, message: failureMessage(error) }
  report.failures.push(failure)
  appendProgress({ stage: 'case-failed', ...failure })
}
async function settle(page) { await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))) }
async function geometry(locator) {
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const style = getComputedStyle(element)
    return {
      x: rect.x, y: rect.y, width: rect.width, height: rect.height,
      computedHeight: style.height, computedMinHeight: style.minHeight,
      ancestors: [...element.parentElement?.parentElement?.classList ?? [], ...element.parentElement?.classList ?? []],
    }
  })
}
async function tapControl(locator, label) {
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  const box = await geometry(locator)
  if (box.width < 44 || box.height < 44) report.advisories.push({ label, viewport: locator.page().viewportSize(), geometry: box, recommendation: '可考虑提升至44px；当前达到既有38px最低可达性门槛' })
  assert.ok(box.width >= 38 && box.height >= 38, `${label} touch target is ${box.width.toFixed(1)}x${box.height.toFixed(1)}`)
  await locator.tap()
  await settle(locator.page())
  return box
}
async function selectNative(select, value, label) {
  await select.waitFor({ state: 'visible' })
  await select.scrollIntoViewIfNeeded()
  const box = await geometry(select)
  assert.ok(box.width >= 38 && box.height >= 38, `${label} select target is ${box.width.toFixed(1)}x${box.height.toFixed(1)}; ${JSON.stringify(box)}`)
  await select.tap()
  await select.selectOption(value)
  await settle(select.page())
  return box
}
async function assertNoPageOverflow(page, label) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  assert.ok(overflow <= 1, `${label} horizontal overflow is ${overflow}px`)
}
async function openArchive(page, port) {
  await page.goto(`http://127.0.0.1:${port}/__card_archive_mobile__`)
  await page.locator('.card-archive').waitFor({ state: 'visible', timeout: 15_000 })
  await page.locator('.archive-card').first().waitFor({ state: 'visible', timeout: 15_000 })
  await assertNoPageOverflow(page, 'archive initial')
}

async function filterResetFlow(page, port) {
  await openArchive(page, port)
  const expected = await page.evaluate(() => window.__archiveQa.filter)
  const touches = {}
  const trigger = page.locator('.archive-toolbar .mobile-filter-trigger')
  touches.open = await tapControl(trigger, '打开图鉴筛选')
  const dialog = page.getByRole('dialog', { name: '图鉴筛选与排序' })
  await dialog.waitFor({ state: 'visible' })
  assert.equal(await dialog.getByRole('button', { name: '关闭筛选', exact: true }).evaluate(element => document.activeElement === element), true, '筛选抽屉打开后焦点未进入关闭按钮')
  touches.type = await selectNative(dialog.getByLabel('类型'), expected.type, '类型')
  touches.product = await selectNative(dialog.getByLabel('收录产品'), expected.product, '收录产品')
  touches.sort = await selectNative(dialog.getByLabel('排序'), 'name', '排序')
  const apply = dialog.getByRole('button', { name: /查看 \d+ 张结果/ })
  touches.apply = await tapControl(apply, '应用图鉴筛选')
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await trigger.evaluate(element => document.activeElement === element), true, '筛选抽屉首次关闭后未把焦点还给筛选入口')
  assert.equal(Number(await page.locator('.archive-count b').innerText()), expected.expectedCount, '筛选后的结果计数不正确')
  const visibleCards = page.locator('.archive-grid>.archive-card')
  assert.equal(await visibleCards.count(), expected.expectedCount, '筛选后的卡片数量与计数不一致')
  assert.equal((await visibleCards.first().locator(':scope>span').innerText()).trim(), expected.firstName, '名称排序后的首张卡不正确')
  const summary = page.locator('.archive-filter-summary')
  touches.reopen = await tapControl(summary, '筛选摘要重开')
  await dialog.waitFor({ state: 'visible' })
  touches.reset = await tapControl(dialog.getByRole('button', { name: '重置', exact: true }), '重置图鉴筛选')
  assert.equal(await dialog.getByLabel('类型').inputValue(), 'all')
  assert.equal(await dialog.getByLabel('收录产品').inputValue(), 'all')
  assert.equal(await dialog.getByLabel('排序').inputValue(), 'number')
  touches.resetApply = await tapControl(dialog.getByRole('button', { name: /查看 \d+ 张结果/ }), '应用重置结果')
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await trigger.evaluate(element => document.activeElement === element), true, '筛选摘要因重置消失后未把焦点恢复到持久筛选入口')
  assert.equal(Number(await page.locator('.archive-count b').innerText()), expected.total, '重置后总数未恢复')
  assert.equal(await page.locator('.archive-filter-summary').count(), 0, '重置后仍显示活动筛选摘要')
  assert.equal(await page.locator('.mobile-filter-mask:visible,[role="dialog"]:visible').count(), 0, '筛选关闭后仍有遮罩或弹框')
  await assertNoPageOverflow(page, 'filter reset final')
  return { touches, expected, stateReadback: { filteredCount: expected.expectedCount, resetCount: expected.total, summaryCleared: true } }
}

async function galleryDetailFlow(page, port) {
  await openArchive(page, port)
  const galleryName = await page.evaluate(() => window.__archiveQa.galleryName)
  const touches = {}
  touches.galleryTab = await tapControl(page.getByRole('tab', { name: '画廊', exact: true }), '画廊标签')
  const card = page.locator('.archive-gallery-card').filter({ hasText: galleryName })
  await card.waitFor({ state: 'visible' })
  touches.card = await tapControl(card, '画廊卡片单次触摸')
  assert.equal(await card.evaluate(element => element.classList.contains('selected')), true, '单次触摸没有选中画廊卡片')
  await page.waitForTimeout(300)
  const dialog = page.getByRole('dialog', { name: /.+/ })
  assert.equal(await dialog.count(), 1, '画廊卡片单次真实触摸没有打开详情；不得以双击或Enter替代')
  await dialog.waitFor({ state: 'visible' })
  const close = dialog.getByRole('button', { name: '关闭卡牌详情', exact: true })
  assert.equal(await close.evaluate(element => document.activeElement === element), true, '卡牌详情打开后焦点未进入关闭按钮')
  assert.equal(await dialog.getByRole('button', { name: '大图下一版本', exact: true }).count(), 0, '独立联动画廊卡不应获得另一套版本切换入口')
  touches.close = await tapControl(close, '关闭卡牌详情')
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await card.evaluate(element => document.activeElement === element), true, '卡牌详情关闭后未恢复到实际触发卡片')
  assert.equal(await page.locator('.archive-modal-backdrop:visible,[role="dialog"]:visible').count(), 0, '详情关闭后仍有遮罩或弹框')
  await assertNoPageOverflow(page, 'gallery detail final')
  return { touches, galleryName, stateReadback: { openedBySingleTap: true, independentVersionSystemAbsent: true, focusRestored: true } }
}

async function catalogDetailFlow(page, port) {
  await openArchive(page, port)
  const baseName = await page.evaluate(() => window.__archiveQa.baseName)
  const card = page.locator('.archive-grid>.archive-card').filter({ hasText: baseName }).first()
  const touches = {}
  touches.card = await tapControl(card, '全卡池卡片单次触摸')
  assert.equal(await card.evaluate(element => element.classList.contains('selected')), true, '单次触摸没有选中全卡池卡片')
  const dialog = page.getByRole('dialog', { name: /.+/ })
  assert.equal(await dialog.count(), 1, '全卡池卡片单次真实触摸没有打开详情；不得以双击或Enter替代')
  await dialog.waitFor({ state: 'visible' })
  const close = dialog.getByRole('button', { name: '关闭卡牌详情', exact: true })
  assert.equal(await close.evaluate(element => document.activeElement === element), true, '全卡池详情打开后焦点未进入关闭按钮')
  const next = dialog.getByRole('button', { name: '大图下一版本', exact: true })
  assert.equal(await next.count(), 1, '多版本原卡详情没有沿用既有版本切换入口')
  const before = await dialog.locator('.archive-modal-version-count').innerText()
  touches.nextVersion = await tapControl(next, '全卡池详情下一版本')
  assert.notEqual(await dialog.locator('.archive-modal-version-count').innerText(), before, '多版本原卡切换后计数未变化')
  touches.close = await tapControl(close, '关闭全卡池卡牌详情')
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await card.evaluate(element => document.activeElement === element), true, '全卡池详情关闭后未恢复到实际触发卡片')
  assert.equal(await page.locator('.archive-modal-backdrop:visible,[role="dialog"]:visible').count(), 0, '全卡池详情关闭后仍有遮罩或弹框')
  await assertNoPageOverflow(page, 'catalog detail final')
  return { touches, baseName, stateReadback: { openedBySingleTap: true, versionChanged: true, focusRestored: true } }
}

async function wideCatalogBoundaryFlow(page, port) {
  await openArchive(page, port)
  const baseName = await page.evaluate(() => window.__archiveQa.baseName)
  const card = page.locator('.archive-grid>.archive-card').filter({ hasText: baseName }).first()
  const sidebar = page.locator('.archive-detail')
  await sidebar.waitFor({ state: 'visible' })
  assert.equal(await page.locator('.archive-toolbar .mobile-filter-trigger').isVisible(), false, '宽屏不应显示移动筛选入口')
  assert.equal(await page.locator('.archive-desktop-filters').isVisible(), true, '宽屏桌面筛选区不可见')

  await card.click()
  await settle(page)
  assert.equal(await card.evaluate(element => element.classList.contains('selected')), true, '宽屏单击没有选中原卡')
  assert.equal(await page.locator('.archive-modal-backdrop:visible').count(), 0, '宽屏单击错误打开了详情弹窗')
  assert.equal(await sidebar.getByText(baseName, { exact: true }).count(), 1, '宽屏单击后右侧详情栏没有显示所选原卡')

  await card.focus()
  await card.press('Enter')
  await settle(page)
  let dialog = page.getByRole('dialog', { name: /.+/ })
  await dialog.waitFor({ state: 'visible' })
  const next = dialog.getByRole('button', { name: '大图下一版本', exact: true })
  assert.equal(await next.count(), 1, '宽屏 Enter 详情没有保留原卡版本切换')
  const before = await dialog.locator('.archive-modal-version-count').innerText()
  await next.click()
  await settle(page)
  assert.notEqual(await dialog.locator('.archive-modal-version-count').innerText(), before, '宽屏原卡版本切换后计数未变化')
  await dialog.getByRole('button', { name: '关闭卡牌详情', exact: true }).click()
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await card.evaluate(element => document.activeElement === element), true, 'Enter 详情关闭后未恢复到原卡')

  await card.locator('.archive-image-open').dblclick()
  await settle(page)
  dialog = page.getByRole('dialog', { name: /.+/ })
  await dialog.waitFor({ state: 'visible' })
  assert.equal(await dialog.getByRole('button', { name: '大图下一版本', exact: true }).count(), 1, '宽屏双击详情没有保留原卡版本切换')
  await dialog.getByRole('button', { name: '关闭卡牌详情', exact: true }).click()
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await page.locator('.archive-modal-backdrop:visible').count(), 0, '宽屏双击详情关闭后仍有遮罩')
  await assertNoPageOverflow(page, 'wide catalog boundary final')
  return {
    baseName,
    geometry: { card: await geometry(card), sidebar: await geometry(sidebar) },
    stateReadback: { sidebarVisible: true, clickSelectedWithoutModal: true, enterOpenedDetail: true, dblclickOpenedDetail: true, versionChanged: true, enterFocusRestored: true },
  }
}

const handlers = { 'filter-reset': filterResetFlow, 'catalog-detail': catalogDetailFlow, 'gallery-detail': galleryDetailFlow, 'wide-catalog-boundary': wideCatalogBoundaryFlow }

try {
  report.status = 'running'
  appendProgress({ stage: 'started', matrix: { flows, viewports }, fixtureTransport: report.fixtureTransport })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of viewports) {
    for (const flow of flows) {
      const test = `${flow.id}-${viewport.label}`
      const context = await browser.newContext(wideOnly
        ? { viewport, deviceScaleFactor: 1, hasTouch: false, isMobile: false }
        : { viewport, deviceScaleFactor: 2, hasTouch: true, isMobile: true })
      const page = await context.newPage()
      page.setDefaultTimeout(10_000)
      page.on('pageerror', error => report.pageErrors.push({ test, message: error.message }))
      page.on('requestfailed', request => {
        if (new URL(request.url()).hostname !== '127.0.0.1') return
        report.requestFailures.push({ test, request: `${request.method()} ${request.url()}`, error: request.failure()?.errorText || 'failed' })
      })
      await page.route('**/*', route => {
        const url = new URL(route.request().url())
        if (url.hostname !== '127.0.0.1') return route.abort()
        if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
        if (url.pathname.startsWith('/card-assets/') || url.pathname.startsWith('/api/site/media/')) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#17313a"/></svg>' })
        return route.continue()
      })
      appendProgress({ stage: 'case-started', test, purpose: flow.purpose })
      try {
        const evidence = await handlers[flow.id](page, port)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'passed', evidence })
        appendProgress({ stage: 'case-passed', test })
        await page.screenshot({ path: path.join(output, `${test}.png`), fullPage: true })
      } catch (error) {
        recordFailure(test, flow.id, error)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'failed', stage: flow.id, message: failureMessage(error) })
        await page.screenshot({ path: path.join(output, `${test}-failed.png`), fullPage: true }).catch(() => {})
      } finally { await context.close() }
    }
  }
  if (report.pageErrors.length) report.failures.push({ test: 'page-errors', stage: 'browser', message: JSON.stringify(report.pageErrors) })
  if (report.requestFailures.length) report.failures.push({ test: 'request-failures', stage: 'browser', message: JSON.stringify(report.requestFailures) })
  report.status = report.failures.length ? 'failed' : 'passed'
} catch (error) {
  recordFailure('harness', 'harness', error)
  report.status = 'failed'
} finally {
  report.cleanupResourceFailures = await closeProbeResources(browser, server)
  for (const resource of report.cleanupResourceFailures) recordFailure(`cleanup-${resource}`, 'cleanup', new Error('Owned resource failed to close'))
  if (report.cleanupResourceFailures.length) report.status = 'failed'
  report.completedAt = new Date().toISOString()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
  appendProgress({ stage: 'completed', status: report.status, cases: report.matrix.length, failures: report.failures.length })
}

console.log(JSON.stringify({ status: report.status, output, sourceSha, cases: report.matrix.length, failures: report.failures }, null, 2))
if (report.status !== 'passed') process.exitCode = 1
