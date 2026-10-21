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

const output = requiredEnvironment('L12_QA_OUT')
const cacheDir = requiredEnvironment('L12_QA_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
assert.ok(path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + '@($env:L12_ACCEPTANCE_OUT,$env:L12_ACCEPTANCE_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) '
  + '| ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "editor replay mobile reachability" | Out-Null}',
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
if (!commit) throw new Error('L12_SOURCE_COMMIT is required so the short browser PATH does not need Git')
const progressPath = path.join(output, 'progress.ndjson')
const appendProgress = value => fs.appendFileSync(progressPath,
  `${JSON.stringify({ schemaVersion: 1, sourceSha, commit, at: new Date().toISOString(), ...value })}\n`)

const require = createRequire(import.meta.url)
const playwrightRoot = process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'
const { chromium } = require(playwrightRoot)

const entry = String.raw`
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import App from '/src/App.vue'
import Editor from '/src/l12/L12DeckEditor.vue'
import MatchRecords from '/src/l12/MatchRecords.vue'
import Replay from '/src/l12/ReplayPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {alternateArtApi,authState,platformState} from '/src/l12/platform.ts'
import {audioPreferences,syncAudioStore} from '/src/l12/audioPreferences.ts'
import '/src/style.css'

audioPreferences.musicEnabled=false
audioPreferences.sfxEnabled=false
audioPreferences.mobileLayout='auto'
audioPreferences.animation='off'
syncAudioStore()
platformState.token=''
platformState.account=null
authState.initialized=true
authState.verified=false
authState.refreshing=false
alternateArtApi.mine=async()=>[]

const preset=(await loadOfficialPresetDecks()).find(deck=>deck.cardIds.length>=40)
if(!preset)throw new Error('A complete preset deck is required for the editor fixture')
const deck={...preset,name:'移动端编辑验收牌库',id:'qa-editor-deck',revision:1,cardIds:[...preset.cardIds],moraleIds:[...preset.moraleIds],specialIds:[...(preset.specialIds||[])],benchIds:[...(preset.benchIds||[])],updatedAt:'2026-10-21T00:00:00Z'}
localStorage.setItem('l12-custom-decks-v1',JSON.stringify({[deck.name]:deck}))
localStorage.setItem('l12-official-decks-seeded-v1','true')

const match={matchId:'qa-match-mobile',roomCode:'QA12',player0:'验收玩家甲',player1:'验收玩家乙',deck0:'甲牌库',deck1:'乙牌库',startedUtc:'2026-10-21T08:00:00Z',endedUtc:'2026-10-21T08:12:03Z',winner:0,finalHash:'qa-final',error:null,commandCount:18}
window.__qaFileClicks=0
window.__qaReplayDetailRequests=[]
const originalInputClick=HTMLInputElement.prototype.click
HTMLInputElement.prototype.click=function(){if(this.type==='file'){window.__qaFileClicks++;return}return originalInputClick.call(this)}
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
  const url=String(typeof input==='string'?input:input.url)
  const parsed=new URL(url,location.origin)
  const method=String(init.method||'GET').toUpperCase()
  const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
  if(parsed.pathname==='/api/matches'&&method==='GET')return json([match])
  if(parsed.pathname==='/api/matches/'+match.matchId){window.__qaReplayDetailRequests.push(parsed.pathname);return json({match,commands:[]})}
  if(parsed.pathname.includes('/api/me/matches/')&&parsed.pathname.endsWith('/disasters'))return json({coverage:'unknown',mode:'none',status:'complete',visibility:'self',decisions:[],visibleCompositionCardIds:[]})
  if(parsed.pathname==='/api/operations/effective-policy')return json({cardRestrictions:[],ranked:{},season:{}})
  if(parsed.pathname==='/api/operations/maintenance')return json({active:false,scheduled:[]})
  if(parsed.pathname==='/api/friends/overview')return json({friends:[],requests:[],blocked:[]})
  if(parsed.pathname.includes('notifications'))return json({items:[],nextCursor:null})
  if(parsed.pathname==='/api/telemetry/page-view')return json({})
  if(parsed.pathname==='/api/auth/logout')return json({})
  if(parsed.pathname.startsWith('/api/'))return json([])
  return originalFetch(input,init)
}

const Returned={render:()=>h('main',{'data-qa':'returned-decks'},[h('h1','我的牌库')])}
const routes=[
 {name:'deck-editor',path:'/deck-editor',component:Editor,meta:{immersive:true,landscapeCanvas:true,editorAdaptiveCanvas:true}},
 {name:'public-decks',path:'/decks',component:Returned},
 {name:'records',path:'/battle/records',component:MatchRecords,meta:{section:'battle'}},
 {name:'match-replay',path:'/battle/records/replay/:matchId',component:Replay,meta:{immersive:true,replay:true,section:'battle'}},
 {name:'json-replay',path:'/battle/records/replay/json',component:Replay,meta:{immersive:true,replay:true,section:'battle'}},
]
const router=createRouter({history:createMemoryHistory(),routes})
const target=new URLSearchParams(location.search).get('route')||'/deck-editor'
await router.push(target)
await router.isReady()
window.__qa={router,deck,match}
createApp(App).use(router).mount('#app')
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'editor-replay-mobile-reachability-fixture',
    resolveId(id) { if (id === '/__editor_replay_mobile__.js') return id },
    load(id) { if (id === '/__editor_replay_mobile__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__editor_replay_mobile__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__editor_replay_mobile__.js"></script></body></html>')
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
const flows = [
  { id: 'editor-card-add', area: 'editor', purpose: '选择主牌并增加一张' },
  { id: 'editor-card-detail', area: 'editor', purpose: '打开并关闭卡牌详情' },
  { id: 'editor-save-return', area: 'editor', purpose: '保存牌库并返回我的牌库' },
  { id: 'records-play-entry', area: 'replay', purpose: '移动端播放入口给出电脑端提示' },
  { id: 'records-json-entry', area: 'replay', purpose: '移动端本地 JSON 入口不打开文件选择器' },
  { id: 'direct-replay-block-return', area: 'replay', purpose: '直达回放不加载数据且可返回记录' },
]
const report = {
  schemaVersion: 1, sourceSha, commit, status: 'running',
  fixtureTransport: 'local Vite static preview with synthetic local data and mocked component APIs; no HTTP business or replay server validation',
  productBoundary: 'Both target viewports are mobile replay experiences. Playback, pause, progress and speed controls must be absent; the desktop-only blocker and return path must remain reachable.',
  usabilityFloorPx: 38, flows, viewports, matrix: [], boundaries: [], failures: [], pageErrors: [], requestFailures: [],
}
let browser

function failureMessage(error) { return error instanceof Error ? error.message : String(error) }
function recordFailure(test, error) {
  const failure = { test, message: failureMessage(error) }
  report.failures.push(failure)
  appendProgress({ stage: 'case-failed', ...failure })
}
async function settle(page) { await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))) }
async function geometry(locator) {
  await locator.scrollIntoViewIfNeeded()
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const style = getComputedStyle(element)
    const x = Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2))
    const y = Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2))
    const hit = document.elementFromPoint(x, y)
    return {
      label: element.getAttribute('aria-label') || element.textContent?.trim().replace(/\s+/g, ' ').slice(0, 100) || '',
      width: rect.width, height: rect.height, left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom,
      displayed: style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
      inViewport: rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth,
      hit: hit === element || element.contains(hit),
      disabled: Boolean(element.disabled || element.getAttribute('aria-disabled') === 'true'), x, y,
    }
  })
}
function assertReachable(value, label, { allowDisabled = false, minimum = 38 } = {}) {
  assert.equal(value.displayed, true, `${label} must be displayed`)
  assert.equal(value.inViewport, true, `${label} must be reachable after scroll`)
  assert.equal(value.hit, true, `${label} center must receive the touch`)
  if (!allowDisabled) assert.equal(value.disabled, false, `${label} must be enabled`)
  assert.ok(value.width >= minimum && value.height >= minimum,
    `${label} touch target must meet the existing ${minimum}x${minimum} floor; got ${value.width.toFixed(1)}x${value.height.toFixed(1)}`)
}
function assertAllReachable(entries) {
  const issues = []
  for (const [value, label, options] of entries) {
    try { assertReachable(value, label, options) }
    catch (error) { issues.push(failureMessage(error)) }
  }
  if (issues.length) throw new Error(issues.join(' | '))
}
async function touch(locator, label, options) {
  const value = await geometry(locator)
  assertReachable(value, label, options)
  await locator.tap()
  await settle(locator.page())
  return value
}
async function openFixture(page, port, route) {
  await page.goto(`http://127.0.0.1:${port}/__editor_replay_mobile__?route=${encodeURIComponent(route)}`)
  await page.waitForFunction(() => document.querySelector('#app')?.childElementCount >= 2, undefined, { timeout: 20_000 })
  await settle(page)
}
async function openEditor(page, port) {
  const route = '/deck-editor?deck='+encodeURIComponent('移动端编辑验收牌库')+'&returnTo='+encodeURIComponent('/decks?tab=mine')
  await openFixture(page, port, route)
  await page.locator('.deck-builder-grid').waitFor({ state: 'visible', timeout: 20_000 })
}
async function selectMainPool(page) {
  const button = page.locator('.catalog-tabs button').filter({ hasText: /^主牌库$/ })
  const buttonGeometry = await touch(button, '主牌库分类', { minimum: 0 })
  await page.locator('.deck-catalog .pool-count-controls').first().waitFor({ state: 'visible' })
  return buttonGeometry
}
async function caseEditorAdd(page, port) {
  await openEditor(page, port)
  const tabGeometry = await selectMainPool(page)
  const add = page.locator('.deck-catalog .pool-count-controls button[aria-label="增加一张"]:not([disabled])').first()
  await add.waitFor({ state: 'visible' })
  const counter = add.locator('xpath=..').locator('strong')
  const containment = await add.evaluate(element => {
    const control = element.closest('.pool-count-controls')
    const card = element.closest('.deck-card')
    const rect = node => { const value = node.getBoundingClientRect(); return { left: value.left, right: value.right, width: value.width, height: value.height } }
    return {
      card: rect(card), control: rect(control), add: rect(element), counter: rect(control.querySelector('strong')),
      gridTemplateColumns: getComputedStyle(control).gridTemplateColumns,
      controlClientWidth: control.clientWidth, controlScrollWidth: control.scrollWidth,
      counterClientWidth: control.querySelector('strong').clientWidth, counterScrollWidth: control.querySelector('strong').scrollWidth,
    }
  })
  const before = Number((await counter.innerText()).trim())
  const addGeometry = await touch(add, '卡池增加一张', { minimum: 0 })
  const after = Number((await counter.innerText()).trim())
  assert.equal(after, before + 1, '增加一张必须立即更新对应卡牌数量')
  const disabled = page.locator('.deck-catalog .pool-count-controls button[aria-label="减少一张"]:disabled').first()
  const disabledGeometry = await geometry(disabled)
  assert.equal(disabledGeometry.disabled, true, '零张卡牌的减少按钮必须保持禁用')
  const disabledCounter = disabled.locator('xpath=..').locator('strong')
  const disabledBefore = await disabledCounter.innerText()
  await disabled.evaluate(element => element.click())
  assert.equal(await disabledCounter.innerText(), disabledBefore, '禁用的减少按钮不得改变数量')
  assert.ok(containment.controlScrollWidth <= containment.controlClientWidth + 1, `数量控件不得横向溢出：${JSON.stringify(containment)}`)
  assert.ok(containment.counterScrollWidth <= containment.counterClientWidth, `数量文字不得裁切：${JSON.stringify(containment)}`)
  assert.ok(containment.add.left >= containment.control.left - 1 && containment.add.right <= containment.control.right + 1,
    `加牌按钮必须包含在数量控件内：${JSON.stringify(containment)}`)
  try {
    assertAllReachable([[tabGeometry, '主牌库分类'], [addGeometry, '卡池增加一张'], [disabledGeometry, '禁用的减少一张', { allowDisabled: true }]])
  } catch (error) {
    throw new Error(`${failureMessage(error)}；数量控件=${JSON.stringify(containment)}`)
  }
  return { tabGeometry, addGeometry, disabledGeometry, containment, disabledBefore, before, after }
}
async function caseEditorDetail(page, port) {
  await openEditor(page, port)
  await selectMainPool(page)
  const card = page.locator('.deck-catalog .deck-card').first()
  const cardGeometry = await touch(card, '卡池卡牌详情入口', { minimum: 0 })
  const dialog = page.getByRole('dialog', { name: '卡牌详情' })
  await dialog.waitFor({ state: 'visible' })
  const title = await dialog.locator('header b').innerText()
  const close = page.getByRole('button', { name: '关闭卡牌详情', exact: true })
  const closeGeometry = await touch(close, '关闭卡牌详情', { minimum: 0 })
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await dialog.count(), 0, '卡牌详情关闭后不应残留遮罩')
  const tabGeometry = await geometry(page.locator('.catalog-tabs button').filter({ hasText: /^主牌库$/ }))
  assertAllReachable([[tabGeometry, '主牌库分类'], [cardGeometry, '卡池卡牌详情入口'], [closeGeometry, '关闭卡牌详情']])
  return { cardGeometry, closeGeometry, tabGeometry, title }
}
async function caseEditorSaveReturn(page, port) {
  await openEditor(page, port)
  const save = page.getByRole('button', { name: '保存牌库', exact: true })
  const saveGeometry = await touch(save, '保存牌库', { minimum: 0 })
  const notice = page.locator('.deck-operation-notice').first()
  await notice.waitFor({ state: 'visible' })
  const feedback = await notice.innerText()
  assert.match(feedback, /已保存/)
  const back = page.getByRole('button', { name: '← 返回上一级', exact: true })
  const backGeometry = await touch(back, '返回上一级', { minimum: 0 })
  await page.locator('[data-qa="returned-decks"]').waitFor({ state: 'visible' })
  const route = await page.evaluate(() => window.__qa.router.currentRoute.value.fullPath)
  assert.equal(route, '/decks?tab=mine')
  assertAllReachable([[saveGeometry, '保存牌库'], [backGeometry, '返回上一级']])
  return { saveGeometry, backGeometry, feedback, route }
}
async function openSelectedRecord(page, port) {
  await openFixture(page, port, '/battle/records?selected=qa-match-mobile')
  await page.locator('.record-detail').waitFor({ state: 'visible', timeout: 15_000 })
}
async function caseRecordsPlay(page, port) {
  await openSelectedRecord(page, port)
  const play = page.getByRole('button', { name: '播放回放', exact: true })
  const playGeometry = await touch(play, '播放回放')
  const error = page.locator('.records-error')
  await error.waitFor({ state: 'visible' })
  assert.equal((await error.innerText()).trim(), '请到电脑端查看回放')
  const route = await page.evaluate(() => window.__qa.router.currentRoute.value.fullPath)
  assert.match(route, /^\/battle\/records/)
  return { playGeometry, feedback: await error.innerText(), route }
}
async function caseRecordsJson(page, port) {
  await openFixture(page, port, '/battle/records')
  await page.getByRole('button', { name: '打开 JSON 回放', exact: true }).waitFor({ state: 'visible' })
  const open = page.getByRole('button', { name: '打开 JSON 回放', exact: true })
  const openGeometry = await touch(open, '打开 JSON 回放')
  const error = page.locator('.records-error')
  await error.waitFor({ state: 'visible' })
  const fileClicks = await page.evaluate(() => window.__qaFileClicks)
  assert.equal(fileClicks, 0, '移动端本地回放入口不得打开文件选择器')
  assert.equal((await error.innerText()).trim(), '请到电脑端查看回放')
  return { openGeometry, fileClicks, feedback: await error.innerText() }
}
async function caseDirectReplay(page, port) {
  await openFixture(page, port, '/battle/records/replay/qa-match-mobile')
  const blocker = page.locator('.replay-mobile-blocked')
  await blocker.waitFor({ state: 'visible', timeout: 15_000 })
  assert.match(await blocker.innerText(), /请到电脑端查看回放/)
  assert.equal(await page.locator('.replay-controls').count(), 0, '移动端禁用回放时不得渲染播放、进度或倍速控件')
  const detailRequests = await page.evaluate(() => window.__qaReplayDetailRequests.length)
  assert.equal(detailRequests, 0, '移动端直达回放不得请求回放详情')
  const settingsGeometry = await geometry(page.getByRole('button', { name: '设置', exact: true }))
  const back = page.getByRole('button', { name: '返回对局记录', exact: true })
  const backGeometry = await touch(back, '返回对局记录', { minimum: 0 })
  await page.locator('.match-records').waitFor({ state: 'visible', timeout: 15_000 })
  const route = await page.evaluate(() => window.__qa.router.currentRoute.value.fullPath)
  assert.equal(route, '/battle/records')
  assertAllReachable([[settingsGeometry, '回放禁用页设置'], [backGeometry, '返回对局记录']])
  return { blocker: await blocker.innerText().catch(() => '请到电脑端查看回放'), settingsGeometry, backGeometry, detailRequests, route, playbackControls: 0 }
}

const caseHandlers = {
  'editor-card-add': caseEditorAdd,
  'editor-card-detail': caseEditorDetail,
  'editor-save-return': caseEditorSaveReturn,
  'records-play-entry': caseRecordsPlay,
  'records-json-entry': caseRecordsJson,
  'direct-replay-block-return': caseDirectReplay,
}

try {
  appendProgress({ stage: 'started', matrix: { flows, viewports }, fixtureTransport: report.fixtureTransport })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of viewports) {
    const context = await browser.newContext({ viewport, deviceScaleFactor: 2, hasTouch: true, isMobile: true })
    const page = await context.newPage()
    page.setDefaultTimeout(10_000)
    page.on('pageerror', error => report.pageErrors.push({ viewport: viewport.label, message: error.message }))
    page.on('requestfailed', request => {
      if (new URL(request.url()).hostname !== '127.0.0.1') return
      report.requestFailures.push({ viewport: viewport.label, request: `${request.method()} ${request.url()}`, error: request.failure()?.errorText || 'failed' })
    })
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (url.pathname.startsWith('/card-assets/')) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#13242a"/></svg>' })
      return route.continue()
    })
    for (const flow of flows) {
      const test = `${flow.id}-${viewport.label}`
      appendProgress({ stage: 'case-started', test, purpose: flow.purpose })
      try {
        const evidence = await caseHandlers[flow.id](page, port)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'passed', evidence })
        appendProgress({ stage: 'case-passed', test })
        await page.screenshot({ path: path.join(output, `${test}.png`) })
      } catch (error) {
        recordFailure(test, error)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'failed', message: failureMessage(error) })
        await page.screenshot({ path: path.join(output, `${test}-failed.png`) }).catch(() => {})
      }
    }
    await context.close()
  }
  {
    const test = 'wide-fine-editor-controls-1366x768'
    appendProgress({ stage: 'case-started', test, purpose: '移动修复不得改变宽屏精细指针控件尺寸' })
    const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, deviceScaleFactor: 1, hasTouch: false, isMobile: false })
    const page = await context.newPage()
    page.setDefaultTimeout(10_000)
    page.on('pageerror', error => report.pageErrors.push({ viewport: '1366x768-fine', message: error.message }))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (url.pathname.startsWith('/card-assets/')) return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#13242a"/></svg>' })
      return route.continue()
    })
    try {
      await openEditor(page, port)
      const fineTab = page.locator('.catalog-tabs button').filter({ hasText: /^主牌库$/ })
      const tabGeometry = await geometry(fineTab)
      assertReachable(tabGeometry, '宽屏fine主牌库分类', { minimum: 0 })
      await fineTab.click()
      await page.locator('.deck-catalog .pool-count-controls').first().waitFor({ state: 'visible' })
      const addGeometry = await geometry(page.locator('.deck-catalog .pool-count-controls button[aria-label="增加一张"]:not([disabled])').first())
      const backGeometry = await geometry(page.getByRole('button', { name: '← 返回上一级', exact: true }))
      assert.ok(Math.abs(tabGeometry.height - 32) <= 1, `宽屏fine分类按钮应保留32px，实测${tabGeometry.height}`)
      assert.ok(Math.abs(addGeometry.height - 30) <= 1, `宽屏fine加牌按钮应保留30px，实测${addGeometry.height}`)
      assert.ok(backGeometry.height >= 42, `宽屏fine返回按钮不应缩小，实测${backGeometry.height}`)
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, '宽屏fine不应出现页面横向溢出')
      const evidence = { tabGeometry, addGeometry, backGeometry }
      report.boundaries.push({ test, status: 'passed', evidence })
      appendProgress({ stage: 'case-passed', test })
      await page.screenshot({ path: path.join(output, `${test}.png`) })
    } catch (error) {
      recordFailure(test, error)
      report.boundaries.push({ test, status: 'failed', message: failureMessage(error) })
      await page.screenshot({ path: path.join(output, `${test}-failed.png`) }).catch(() => {})
    } finally { await context.close() }
  }
  if (report.pageErrors.length) report.failures.push({ test: 'page-errors', message: JSON.stringify(report.pageErrors) })
  if (report.requestFailures.length) report.failures.push({ test: 'request-failures', message: JSON.stringify(report.requestFailures) })
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
  appendProgress({ stage: 'completed', status: report.status, cases: report.matrix.length, failures: report.failures.length })
}

console.log(JSON.stringify({ status: report.status, output, sourceSha, cases: report.matrix.length, failures: report.failures }, null, 2))
if (report.status !== 'passed') process.exitCode = 1
