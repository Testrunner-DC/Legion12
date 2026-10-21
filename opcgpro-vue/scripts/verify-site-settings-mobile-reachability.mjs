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

const output = requiredEnvironment('L12_SETTINGS_MOBILE_OUT')
const cacheDir = requiredEnvironment('L12_SETTINGS_MOBILE_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
assert.ok(path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + '@($env:L12_ACCEPTANCE_OUT,$env:L12_ACCEPTANCE_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) '
  + '| ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "site settings mobile reachability" | Out-Null}',
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
import {createMemoryHistory,createRouter} from 'vue-router'
import '/src/style.css'
import '/src/l12/motion.css'
import '/src/l12/site/uiSystem.css'

const preferenceA={musicEnabled:true,musicVolume:.35,sfxEnabled:true,sfxVolume:.7,cardSize:'auto',animation:'standard',mobileLayout:'auto',fontSize:'standard'}
const preferenceB={musicEnabled:false,musicVolume:.18,sfxEnabled:true,sfxVolume:.25,cardSize:'small',animation:'fast',mobileLayout:'off'}
const accountA={id:'qa-settings-a',username:'设置验收甲',role:'player',createdAt:'2026-10-01T00:00:00Z',publicHistory:true,permissionVersion:3,audioPreferences:preferenceA}
const accountB={id:'qa-settings-b',username:'设置验收乙',role:'player',createdAt:'2026-10-02T00:00:00Z',publicHistory:true,permissionVersion:3,audioPreferences:preferenceB}
const accountKey='l12-account',tokenKey='l12-auth-token'
if(!localStorage.getItem(accountKey)){
 localStorage.setItem(accountKey,JSON.stringify(accountA));localStorage.setItem(tokenKey,'qa-settings-token-a')
 localStorage.setItem('l12-audio-preferences-v1',JSON.stringify({...preferenceA,cardSize:'large',fontSize:'extra-large'}))
}
const storedServer=JSON.parse(sessionStorage.getItem('qa-settings-server')||'null')||{a:accountA,b:accountB}
const calls=JSON.parse(sessionStorage.getItem('qa-settings-calls')||'[]')
const saveServer=()=>sessionStorage.setItem('qa-settings-server',JSON.stringify(storedServer))
const saveCalls=()=>sessionStorage.setItem('qa-settings-calls',JSON.stringify(calls))
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
 const parsed=new URL(String(typeof input==='string'?input:input.url),location.origin)
 const method=String(init.method||'GET').toUpperCase()
 const headers=new Headers(init.headers||{})
 const authorization=headers.get('Authorization')||''
 const token=authorization.replace(/^Bearer\s+/,'')
 const owner=token.endsWith('-b')?'b':'a'
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(parsed.pathname==='/api/auth/me'&&method==='GET')return json(storedServer[owner])
 if(parsed.pathname==='/api/auth/audio-preferences'&&method==='PUT'){
  const body=JSON.parse(String(init.body||'{}'))
  calls.push({method,path:parsed.pathname,authorization,body});storedServer[owner]={...storedServer[owner],audioPreferences:body};saveServer();saveCalls();return json(body)
 }
 if(parsed.pathname==='/api/operations/effective-policy')return json({version:1,season:{},disasterCardIds:[],matchModes:[],defaultRoomConfig:{},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]})
 if(parsed.pathname.includes('/alternate-art/notifications'))return json([])
 if(parsed.pathname.includes('/notifications'))return json({items:[],nextCursor:null})
 if(parsed.pathname==='/api/friends/presence')return json([])
 if(parsed.pathname==='/api/telemetry/page-view')return json({})
 if(parsed.pathname.startsWith('/api/'))return json([])
 return originalFetch(input,init)
}

const platform=await import('/src/l12/platform.ts')
await platform.initializeAuth()
// Settings ownership is exercised with a verified account projection, while
// this isolated UI probe deliberately keeps the realtime game channel closed.
platform.authState.verified=false
platform.authState.refreshing=false
const [{default:App},{audioPreferences}]=await Promise.all([import('/src/App.vue'),import('/src/l12/audioPreferences.ts')])
const {useAudioStore}=await import('/src/store/audioStore.ts')
const {resolveViewportMode}=await import('/src/l12/mobileViewport.ts')
const Probe={render:()=>h('main',{class:'settings-qa-page'},[
 h('h1','设置移动验收'),
 h('p',{class:'l12-effect-body','data-qa':'effect-copy'},'这是一段用于核对正文文字倍率的卡效正文。'),
 h('div',{class:'l12-pop','data-qa':'motion-probe'},'动画样例'),
 h('div',{class:'archive-grid','data-qa':'card-grid'},Array.from({length:8},(_,index)=>h('i',{key:index},String(index+1))))
])}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:Probe}]})
await router.push('/');await router.isReady()
window.__settingsQa={
 audioPreferences,
 snapshot:()=>({
  accountId:platform.platformState.account?.id||'',token:platform.platformState.token,
  preferences:{...audioPreferences},store:useAudioStore.getState(),
  local:JSON.parse(localStorage.getItem('l12-audio-preferences-v1')||'null'),
  root:{...document.documentElement.dataset},
  styles:{fontScale:getComputedStyle(document.documentElement).getPropertyValue('--l12-font-scale').trim(),animationScale:getComputedStyle(document.documentElement).getPropertyValue('--l12-animation-scale').trim(),cardScale:getComputedStyle(document.documentElement).getPropertyValue('--l12-card-scale').trim()},
  calls:JSON.parse(sessionStorage.getItem('qa-settings-calls')||'[]'),server:JSON.parse(sessionStorage.getItem('qa-settings-server')||'null')
 }),
 layout:(choice)=>resolveViewportMode(innerWidth,innerHeight,undefined,choice),
 switchAccount:()=>{
  const server=JSON.parse(sessionStorage.getItem('qa-settings-server')||'null')||storedServer
  localStorage.setItem(accountKey,JSON.stringify(server.b));localStorage.setItem(tokenKey,'qa-settings-token-b')
  platform.platformState.token='qa-settings-token-b';platform.platformState.account=structuredClone(server.b)
  platform.authState.verified=false;platform.authState.refreshing=false
 },
 ready:true,
}
createApp(App).use(router).mount('#app')
`

const server = await createServer({
  root, configLoader: 'runner', cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'site-settings-mobile-reachability-fixture',
    resolveId(id) { if (id === '/__site_settings_mobile__.js') return id },
    load(id) { if (id === '/__site_settings_mobile__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__site_settings_mobile__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__site_settings_mobile__.js"></script></body></html>')
          return
        }
        next()
      })
    },
  }],
})

const acceptanceMode = process.env.L12_SETTINGS_REACHABILITY_MODE?.trim() || 'mobile'
assert.ok(['mobile', 'wide-only'].includes(acceptanceMode), `Unsupported L12_SETTINGS_REACHABILITY_MODE: ${acceptanceMode}`)
const wideOnly = acceptanceMode === 'wide-only'
const viewports = wideOnly ? [
  { width: 1366, height: 900, label: '1366x900-fine-mouse' },
] : [
  { width: 390, height: 844, label: '390x844' },
  { width: 844, height: 390, label: '844x390' },
]
const mobileFlows = [
  { id: 'preferences-persistence', purpose: '逐项修改显示、文字、布局、动画与声音，核即时DOM、账号保存、关闭重开和刷新' },
  { id: 'account-ownership', purpose: '切换到另一账号后核服务端投影覆盖旧账号本地状态，并只写入当前账号' },
]
const flows = wideOnly ? [
  { id: 'wide-settings-boundary', purpose: '宽屏fine鼠标保持34px关闭按钮，并验证遮罩关闭和焦点归还' },
] : mobileFlows
const report = {
  schemaVersion: 1, sourceSha, commit, status: 'prepared', acceptanceMode,
  fixtureTransport: 'actual App, SiteShell and L12SettingsModal with synthetic local auth/preference APIs; realtime game channel intentionally closed; no real service writes',
  interactionBoundary: wideOnly
    ? 'The wide-only boundary uses a 1366x900 fine-pointer mouse context. Desktop close remains 34x34 by contract and is not evaluated against the mobile 38px floor.'
    : 'All visible settings buttons and native selects are touched. Native selects are first tapped and then changed with selectOption because headless browsers do not expose the operating-system picker. Range values are changed through real touchscreen coordinates; their label hit areas, not the browser thumb glyph, are measured.',
  ownershipBoundary: 'Account switching checks the current server preference projection and save ownership. It does not add a requirement that anonymous local preferences or every setting be isolated under a per-account localStorage key.',
  usabilityFloorPx: 38, advisoryTargetPx: 44, viewports, flows, matrix: [], advisories: [], namedRisks: [], failures: [], pageErrors: [], requestFailures: [],
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
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  return locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    const style = getComputedStyle(element)
    const x = Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2))
    const y = Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2))
    const hit = document.elementFromPoint(x, y)
    return {
      width: rect.width, height: rect.height, left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom,
      displayed: style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0,
      inViewport: rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth,
      hit: hit === element || element.contains(hit), disabled: Boolean(element.disabled || element.getAttribute('aria-disabled') === 'true'), x, y,
    }
  })
}
function inspectReachability(value, label, issues, { minimum = 38, allowDisabled = false } = {}) {
  if (!value.displayed) issues.push(`${label} is not displayed`)
  if (!value.inViewport) issues.push(`${label} is outside the viewport after scrolling`)
  if (!value.hit) issues.push(`${label} center does not receive touch`)
  if (!allowDisabled && value.disabled) issues.push(`${label} is disabled`)
  if (value.width < minimum || value.height < minimum)
    issues.push(`${label} touch target is ${value.width.toFixed(1)}x${value.height.toFixed(1)}, below ${minimum}x${minimum}`)
  if (value.width < 44 || value.height < 44)
    report.advisories.push({ label, geometry: value, recommendation: '可考虑提升至44px；低于38px时同时列为失败' })
}
async function tapControl(locator, label, issues, options) {
  const value = await geometry(locator)
  inspectReachability(value, label, issues, options)
  await locator.tap()
  await settle(locator.page())
  return value
}
async function selectNative(locator, value, label, issues) {
  const size = await geometry(locator)
  inspectReachability(size, label, issues)
  await locator.tap()
  await locator.selectOption(value)
  await settle(locator.page())
  assert.equal(await locator.inputValue(), value, `${label} did not keep the selected value`)
  return size
}
async function setRangeByTouch(locator, ratio, label, issues) {
  const labelBox = await geometry(locator.locator('xpath=ancestor::label[1]'))
  inspectReachability(labelBox, `${label}标签`, issues)
  const inputBox = await locator.boundingBox()
  if (!inputBox) throw new Error(`${label} has no rendered range track`)
  await locator.page().touchscreen.tap(inputBox.x + Math.max(2, Math.min(inputBox.width - 2, inputBox.width * ratio)), inputBox.y + inputBox.height / 2)
  await settle(locator.page())
  return { label: labelBox, input: inputBox, value: Number(await locator.inputValue()) }
}
async function assertNoPageOverflow(page, label) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  assert.ok(overflow <= 1, `${label} horizontal overflow is ${overflow}px`)
}
async function waitForReady(page, port) {
  await page.goto(`http://127.0.0.1:${port}/__site_settings_mobile__`)
  await page.waitForFunction(() => window.__settingsQa?.ready)
  await page.locator('.site-shell').waitFor({ state: 'visible' })
  await page.locator('[data-qa="effect-copy"]').waitFor({ state: 'visible' })
}
async function openSettings(page, touches, issues) {
  const menu = page.getByRole('button', { name: '打开导航', exact: true })
  if (await menu.isVisible().catch(() => false)) touches.menu = await tapControl(menu, '打开导航', issues)
  const opener = page.locator('.site-utilities button[aria-label="设置"]:visible')
  touches.settings = await tapControl(opener, '设置入口', issues)
  const dialog = page.getByRole('dialog', { name: '设置', exact: true })
  await dialog.waitFor({ state: 'visible' })
  const close = dialog.getByRole('button', { name: '关闭设置', exact: true })
  assert.equal(await close.evaluate(element => document.activeElement === element), true, '设置打开后焦点未进入关闭按钮')
  return { dialog, close, opener }
}
async function closeSettings(page, opened, touches, issues) {
  touches.close = await tapControl(opened.close, '关闭设置', issues)
  await opened.dialog.waitFor({ state: 'detached' })
  assert.equal(await page.locator('.site-modal-mask:visible,[role="dialog"]:visible').count(), 0, '设置关闭后仍有遮罩或弹框')
  const focus = await page.evaluate(() => ({ tag: document.activeElement?.tagName || '', label: document.activeElement?.getAttribute('aria-label') || '' }))
  const mobileMenuVisible = await page.getByRole('button', { name: '打开导航', exact: true }).isVisible().catch(() => false)
  if (mobileMenuVisible) assert.deepEqual(focus, { tag: 'BUTTON', label: '打开导航' }, '移动抽屉关闭后焦点未回到菜单入口')
  else assert.deepEqual(focus, { tag: 'BUTTON', label: '设置' }, '设置关闭后焦点未回到设置入口')
  return focus
}
async function preferenceFlow(page, port) {
  await waitForReady(page, port)
  const issues = [], touches = {}
  const initial = await page.evaluate(() => window.__settingsQa.snapshot())
  assert.equal(initial.accountId, 'qa-settings-a')
  assert.equal(initial.preferences.cardSize, 'auto', '账号投影未覆盖预置的过期本地卡牌尺寸')
  assert.equal(initial.preferences.fontSize, 'standard', '账号投影未覆盖预置的过期本地正文尺寸')
  const baseline = await page.evaluate(() => ({
    effect: parseFloat(getComputedStyle(document.querySelector('[data-qa="effect-copy"]')).fontSize),
    grid: getComputedStyle(document.querySelector('[data-qa="card-grid"]')).gridTemplateColumns,
  }))
  let opened = await openSettings(page, touches, issues)
  const dialog = opened.dialog
  const rows = name => dialog.locator('.setting-row').filter({ hasText: name })
  touches.cardSize = await selectNative(rows('卡牌显示').locator('select'), 'large', '卡牌显示', issues)
  touches.fontSize = await selectNative(rows('正文文字').locator('select'), 'extra-large', '正文文字', issues)
  touches.mobileLayout = await selectNative(rows('移动布局').locator('select'), 'off', '移动布局', issues)
  touches.animation = await selectNative(rows('对局动画').locator('select'), 'off', '对局动画', issues)
  const musicRange = rows('游戏音乐').locator('input[type="range"]')
  const sfxRange = rows('游戏音效').locator('input[type="range"]')
  touches.musicRange = await setRangeByTouch(musicRange, .72, '音乐音量', issues)
  touches.sfxRange = await setRangeByTouch(sfxRange, .28, '音效音量', issues)
  touches.musicToggle = await tapControl(rows('游戏音乐').getByRole('button'), '游戏音乐开关', issues)
  touches.sfxToggle = await tapControl(rows('游戏音效').getByRole('button'), '游戏音效开关', issues)
  assert.equal(await musicRange.isDisabled(), true, '关闭音乐后音乐滑杆未禁用')
  assert.equal(await sfxRange.isDisabled(), true, '关闭音效后音效滑杆未禁用')
  await page.waitForTimeout(850)
  const changed = await page.evaluate(() => ({...window.__settingsQa.snapshot(),effect:parseFloat(getComputedStyle(document.querySelector('[data-qa="effect-copy"]')).fontSize),grid:getComputedStyle(document.querySelector('[data-qa="card-grid"]')).gridTemplateColumns,motion:getComputedStyle(document.querySelector('[data-qa="motion-probe"]')).animationDuration,layout:window.__settingsQa.layout('off')}))
  assert.equal(changed.preferences.cardSize, 'large')
  assert.equal(changed.root.l12CardSize, 'large')
  assert.equal(changed.styles.cardScale, '1.16')
  assert.notEqual(changed.grid, baseline.grid, '卡牌显示设置未改变实际卡牌网格')
  assert.equal(changed.preferences.fontSize, 'extra-large')
  assert.equal(changed.root.l12FontSize, 'extra-large')
  assert.equal(changed.styles.fontScale, '1.24')
  assert.ok(changed.effect > baseline.effect * 1.2, `正文文字未按1.24倍率放大：${baseline.effect} -> ${changed.effect}`)
  assert.equal(changed.preferences.mobileLayout, 'off')
  assert.equal(changed.root.l12MobileLayoutPreference, 'off')
  assert.equal(changed.layout.mobile, false)
  assert.equal(changed.preferences.animation, 'off')
  assert.equal(changed.styles.animationScale, '0')
  assert.ok(changed.motion.split(',').every(value => Number.parseFloat(value) <= .0000011), `动画关闭后实际动画时长仍为 ${changed.motion}`)
  assert.equal(changed.preferences.musicEnabled, false)
  assert.equal(changed.preferences.sfxEnabled, false)
  assert.equal(changed.store.bgmVolume, 0)
  assert.equal(changed.store.sfxVolume, 0)
  assert.deepEqual(changed.local, changed.preferences, 'localStorage 未保存当前完整设置')
  assert.equal(changed.calls.length, 1, '同一批设置修改应经防抖只保存一次')
  assert.equal(changed.calls[0].authorization, 'Bearer qa-settings-token-a')
  assert.deepEqual(changed.calls[0].body, changed.preferences)
  const focus = await closeSettings(page, opened, touches, issues)
  opened = await openSettings(page, touches, issues)
  assert.equal(await opened.dialog.locator('.setting-row').filter({ hasText: '卡牌显示' }).locator('select').inputValue(), 'large')
  assert.equal(await opened.dialog.locator('.setting-row').filter({ hasText: '正文文字' }).locator('select').inputValue(), 'extra-large')
  assert.equal(await opened.dialog.locator('.setting-row').filter({ hasText: '移动布局' }).locator('select').inputValue(), 'off')
  assert.equal(await opened.dialog.locator('.setting-row').filter({ hasText: '对局动画' }).locator('select').inputValue(), 'off')
  await closeSettings(page, opened, touches, issues)
  await page.reload()
  await page.waitForFunction(() => window.__settingsQa?.ready)
  await page.waitForTimeout(100)
  const refreshed = await page.evaluate(() => window.__settingsQa.snapshot())
  assert.equal(refreshed.accountId, 'qa-settings-a')
  assert.deepEqual(refreshed.preferences, changed.preferences, '刷新后未恢复服务端确认的当前账号设置')
  assert.deepEqual(refreshed.local, changed.preferences, '刷新后的本地设置与账号投影不一致')
  assert.equal(refreshed.calls.length, 1, '刷新账号投影不应生成新的保存请求')
  await assertNoPageOverflow(page, 'settings preference flow final')
  return { touches, focus, reachabilityIssues: issues, baseline, changed: { preferences: changed.preferences, root: changed.root, styles: changed.styles, grid: changed.grid, effect: changed.effect, motion: changed.motion, layout: changed.layout, save: changed.calls[0] }, refreshed: { preferences: refreshed.preferences, local: refreshed.local, calls: refreshed.calls.length } }
}
async function accountOwnershipFlow(page, port) {
  await waitForReady(page, port)
  const issues = [], touches = {}
  await page.evaluate(() => window.__settingsQa.switchAccount())
  await page.waitForFunction(() => window.__settingsQa.snapshot().accountId === 'qa-settings-b' && window.__settingsQa.snapshot().preferences.cardSize === 'small')
  const projected = await page.evaluate(() => window.__settingsQa.snapshot())
  assert.equal(projected.preferences.musicEnabled, false)
  assert.equal(projected.preferences.musicVolume, .18)
  assert.equal(projected.preferences.sfxEnabled, true)
  assert.equal(projected.preferences.sfxVolume, .25)
  assert.equal(projected.preferences.cardSize, 'small')
  assert.equal(projected.preferences.animation, 'fast')
  assert.equal(projected.preferences.mobileLayout, 'off')
  assert.equal(projected.preferences.fontSize, 'standard', '旧账号的较大正文不能泄漏给缺少fontSize字段的账号')
  assert.deepEqual(projected.local, projected.preferences, '账号切换后本地投影未更新为当前账号设置')
  const opened = await openSettings(page, touches, issues)
  const sfxRow = opened.dialog.locator('.setting-row').filter({ hasText: '游戏音效' })
  touches.sfxRange = await setRangeByTouch(sfxRow.locator('input[type="range"]'), .82, '账号乙音效音量', issues)
  await page.waitForTimeout(850)
  const saved = await page.evaluate(() => window.__settingsQa.snapshot())
  const bCalls = saved.calls.filter(call => call.authorization === 'Bearer qa-settings-token-b')
  assert.equal(bCalls.length, 1, '账号乙修改应且只应写入一次当前账号')
  assert.equal(saved.calls.filter(call => call.authorization === 'Bearer qa-settings-token-a').length, 0, '账号切换后不得再写入旧账号')
  assert.equal(saved.server.a.audioPreferences.sfxVolume, .7, '账号乙修改污染了账号甲的服务端投影')
  assert.equal(saved.server.b.audioPreferences.sfxVolume, saved.preferences.sfxVolume, '账号乙设置未写入账号乙服务端投影')
  const focus = await closeSettings(page, opened, touches, issues)
  await page.reload()
  await page.waitForFunction(() => window.__settingsQa?.ready)
  const refreshed = await page.evaluate(() => window.__settingsQa.snapshot())
  assert.equal(refreshed.accountId, 'qa-settings-b')
  assert.deepEqual(refreshed.preferences, saved.preferences, '账号乙刷新后未恢复自身设置')
  assert.equal(refreshed.preferences.fontSize, 'standard')
  await assertNoPageOverflow(page, 'settings ownership flow final')
  return { touches, focus, reachabilityIssues: issues, projected: { preferences: projected.preferences, local: projected.local }, saved: { preferences: saved.preferences, calls: saved.calls, server: saved.server }, refreshed: { accountId: refreshed.accountId, preferences: refreshed.preferences } }
}

async function wideSettingsBoundaryFlow(page, port) {
  await waitForReady(page, port)
  const menu = page.getByRole('button', { name: '打开导航', exact: true })
  assert.equal(await menu.isVisible().catch(() => false), false, '1366宽屏不应显示移动导航入口')
  const opener = page.locator('.site-utilities button[aria-label="设置"]:visible')
  await opener.click()
  await settle(page)
  let dialog = page.getByRole('dialog', { name: '设置', exact: true })
  await dialog.waitFor({ state: 'visible' })
  let close = dialog.getByRole('button', { name: '关闭设置', exact: true })
  const closeGeometry = await geometry(close)
  assert.ok(Math.abs(closeGeometry.width - 34) <= .1 && Math.abs(closeGeometry.height - 34) <= .1,
    `宽屏设置关闭按钮应保持34x34，实际为${closeGeometry.width}x${closeGeometry.height}`)
  assert.equal(await close.evaluate(element => document.activeElement === element), true, '宽屏设置打开后焦点未进入关闭按钮')
  assert.equal(await page.locator('.site-modal-mask:visible').count(), 1, '宽屏设置遮罩不可见')
  await close.click()
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await page.locator('.site-modal-mask:visible,[role="dialog"]:visible').count(), 0, '宽屏设置关闭后仍有遮罩或弹框')
  assert.equal(await opener.evaluate(element => document.activeElement === element), true, '宽屏设置按钮关闭后未恢复焦点')

  await opener.click()
  await settle(page)
  dialog = page.getByRole('dialog', { name: '设置', exact: true })
  await dialog.waitFor({ state: 'visible' })
  close = dialog.getByRole('button', { name: '关闭设置', exact: true })
  const mask = page.locator('.site-modal-mask:visible')
  await mask.click({ position: { x: 3, y: 3 } })
  await dialog.waitFor({ state: 'detached' })
  assert.equal(await page.locator('.site-modal-mask:visible,[role="dialog"]:visible').count(), 0, '点击宽屏遮罩后仍有遮罩或弹框')
  assert.equal(await opener.evaluate(element => document.activeElement === element), true, '宽屏遮罩关闭后未恢复到设置入口')
  await assertNoPageOverflow(page, 'wide settings boundary final')
  return { closeGeometry, stateReadback: { mobileMenuHidden: true, desktopClose34: true, closeButtonFocusRestored: true, maskDismissed: true, maskFocusRestored: true } }
}

const handlers = { 'preferences-persistence': preferenceFlow, 'account-ownership': accountOwnershipFlow, 'wide-settings-boundary': wideSettingsBoundaryFlow }

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
      page.setDefaultTimeout(12_000)
      page.on('pageerror', error => report.pageErrors.push({ test, message: error.message }))
      page.on('requestfailed', request => {
        const url = new URL(request.url())
        if (url.hostname !== '127.0.0.1' || url.pathname.startsWith('/audio/')) return
        report.requestFailures.push({ test, request: `${request.method()} ${request.url()}`, error: request.failure()?.errorText || 'failed' })
      })
      await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
      appendProgress({ stage: 'case-started', test, purpose: flow.purpose })
      try {
        const evidence = await handlers[flow.id](page, port)
        const risk = !wideOnly && evidence.reachabilityIssues.length ? {
          test, name: 'settings-close-touch-target-below-existing-floor', category: 'minimum-touch-target', severity: 'product-gap',
          issues: evidence.reachabilityIssues,
        } : null
        if (risk) report.namedRisks.push(risk)
        const status = risk ? 'passed-with-product-gap' : 'passed'
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status, evidence })
        appendProgress({ stage: status, test, risk: risk?.name })
        await page.screenshot({ path: path.join(output, `${test}.png`), fullPage: true })
      } catch (error) {
        recordFailure(test, flow.id, error)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'failed', stage: flow.id, message: failureMessage(error) })
        await page.screenshot({ path: path.join(output, `${test}-failed.png`), fullPage: true }).catch(() => {})
      } finally { await context.close() }
    }
  }
  if (report.namedRisks.length) report.failures.push({
    test: 'settings-close-touch-target', stage: 'reachability',
    message: `设置弹框关闭按钮低于既有38px触控底线；${report.namedRisks.length}个完整设置链均已继续完成，其余行为与状态证据未被该缺口遮蔽。`,
    risk: 'settings-close-touch-target-below-existing-floor',
  })
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
