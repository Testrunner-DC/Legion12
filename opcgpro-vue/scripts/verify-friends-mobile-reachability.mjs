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

const output = requiredEnvironment('L12_FRIENDS_MOBILE_OUT')
const cacheDir = requiredEnvironment('L12_FRIENDS_MOBILE_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
assert.ok(path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + '@($env:L12_ACCEPTANCE_OUT,$env:L12_ACCEPTANCE_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) '
  + '| ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "friends mobile reachability" | Out-Null}',
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
import '/src/style.css'
import '/src/l12/site/uiSystem.css'

const account={id:'qa-owner',username:'好友页验收者',role:'player',createdAt:'2026-10-01T00:00:00Z',publicHistory:true,permissionVersion:3}
localStorage.setItem('l12-auth-token','qa-friends-token')
localStorage.setItem('l12-account',JSON.stringify(account))
const platform=await import('/src/l12/platform.ts')
const {chatResource}=await import('/src/l12/chatResource.ts')
const {resetFriendResource}=await import('/src/l12/friendResource.ts')
platform.platformState.token='qa-friends-token'
platform.platformState.account=account
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false
chatResource.connect(account.id,false,null)
resetFriendResource(account.id)

const calls=[]
const stamp='2026-10-22T00:00:00Z'
const friend=(accountId,username)=>({accountId,username,status:'accepted',direction:'none',createdAt:stamp,online:true})
const request=(accountId,username,direction)=>({accountId,username,status:'pending',direction,createdAt:stamp,online:true})
const blocked=(accountId,username)=>({accountId,username,status:'blocked',direction:'none',createdAt:stamp,online:false})
const state={
 friends:[friend('friend-invite','邀请玩家'),friend('friend-watch','观战玩家')],
 requests:[request('incoming-accept','接受玩家','incoming'),request('incoming-reject','拒绝玩家','incoming')],
 blocked:[],
}
const clone=value=>structuredClone(value)
const requireFixture=(ok,message)=>{if(!ok)throw new Error('Fixture contract: '+message)}
const overview=()=>clone(state)
const findPlayer=accountId=>[...state.friends,...state.requests,...state.blocked].find(item=>item.accountId===accountId)
platform.friendApi.overview=async()=>{calls.push({action:'overview'});return overview()}
platform.friendApi.presence=async()=>[]
platform.friendApi.players=async search=>{
 requireFixture(search==='搜索目标','exact search keyword')
 calls.push({action:'players',search})
 const accountId='search-target',username='搜索目标'
 const existing=findPlayer(accountId)
 return [existing?clone(existing):{accountId,username,status:'none',direction:'none',createdAt:stamp,online:true}]
}
platform.friendApi.request=async accountId=>{
 requireFixture(accountId==='search-target','exact outgoing request target')
 calls.push({action:'request',accountId})
 if(!findPlayer(accountId))state.requests.push(request(accountId,'搜索目标','outgoing'))
 return {message:'好友申请已发送'}
}
platform.friendApi.resolve=async(requesterId,accept)=>{
 requireFixture((requesterId==='incoming-accept'&&accept===true)||(requesterId==='incoming-reject'&&accept===false),'exact accept/reject target and decision')
 calls.push({action:'resolve',requesterId,accept})
 const item=state.requests.find(entry=>entry.accountId===requesterId)
 state.requests=state.requests.filter(entry=>entry.accountId!==requesterId)
 if(accept&&item)state.friends.push(friend(item.accountId,item.username))
 return {message:accept?'已接受好友申请':'已拒绝好友申请'}
}
platform.friendApi.remove=async accountId=>{calls.push({action:'remove',accountId});state.friends=state.friends.filter(item=>item.accountId!==accountId)}
platform.friendApi.block=async accountId=>{
 requireFixture(accountId==='friend-watch','exact block target')
 calls.push({action:'block',accountId})
 const item=findPlayer(accountId)
 state.friends=state.friends.filter(entry=>entry.accountId!==accountId)
 state.requests=state.requests.filter(entry=>entry.accountId!==accountId)
 if(!state.blocked.some(entry=>entry.accountId===accountId))state.blocked.push(blocked(accountId,item?.username||'已屏蔽玩家'))
 return {message:'玩家已屏蔽'}
}
platform.friendApi.unblock=async accountId=>{requireFixture(accountId==='friend-watch','exact unblock target');calls.push({action:'unblock',accountId});state.blocked=state.blocked.filter(item=>item.accountId!==accountId)}
platform.alternateArtApi.notifications=async()=>[]

const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
 const parsed=new URL(String(typeof input==='string'?input:input.url),location.origin)
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(parsed.pathname==='/api/operations/effective-policy')return json({version:1,season:{},disasterCardIds:[],matchModes:[],defaultRoomConfig:{},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]})
 if(parsed.pathname.startsWith('/api/'))return json([])
 return originalFetch(input,init)
}

const [{default:SiteShell},{default:FriendsPage}]=await Promise.all([import('/src/l12/site/SiteShell.vue'),import('/src/l12/site/FriendsPage.vue')])
const friendsNet=await import('/__qa_friends_net__.js')
const Stub={render:()=>h('main','占位页面')}
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/',component:Stub},{path:'/battle',component:Stub,meta:{section:'battle'}},{path:'/decks',component:Stub},{path:'/battle/rankings',component:Stub,meta:{section:'battle'}},
 {path:'/battle/friends',component:FriendsPage,meta:{section:'battle'}},{path:'/battle/tournaments',component:Stub,meta:{section:'battle'}},{path:'/battle/records',component:Stub,meta:{section:'battle'}},
]})
await router.push('/battle/friends')
await router.isReady()
window.__friendsQa={calls,router,snapshot:()=>overview(),setNetOffline:()=>{friendsNet.l12State.status='offline'}}
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

const qaNet = String.raw`
import {reactive} from 'vue'
export const l12State=reactive({
 status:'online',notice:'',presence:[
  {accountId:'friend-invite',username:'邀请玩家',online:true,activity:'lobby',canInvite:true,canSpectate:false,roomCode:null,actionReason:''},
  {accountId:'friend-watch',username:'观战玩家',online:true,activity:'playing',canInvite:false,canSpectate:true,roomCode:'WATCH-ROOM',actionReason:''},
  {accountId:'incoming-accept',username:'接受玩家',online:true,activity:'lobby',canInvite:true,canSpectate:false,roomCode:null,actionReason:''},
 ],
})
export async function connect(){window.__friendsQa.calls.push({action:'connect'});await new Promise(resolve=>setTimeout(resolve,120));l12State.status='online'}
export function inviteFriend(accountId){if(accountId!=='friend-invite')throw new Error('Exact invite target required');window.__friendsQa.calls.push({action:'inviteFriend',accountId})}
export function spectateRoom(roomCode){if(roomCode!=='WATCH-ROOM')throw new Error('Exact spectator room required');window.__friendsQa.calls.push({action:'spectateRoom',roomCode})}
`

const server = await createServer({
  root, configLoader: 'runner', cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'friends-mobile-reachability-fixture', enforce: 'pre',
    transform(code, id) {
      if (id.includes('?') || !id.endsWith('/src/l12/site/FriendsPage.vue')) return
      const anchor = "from '@/l12/net'"
      assert.equal(code.split(anchor).length, 2, 'FriendsPage net import changed')
      return code.replace(anchor, "from '/__qa_friends_net__.js'")
    },
    resolveId(id) {
      if (id === '/__friends_mobile__.js' || id === '/__qa_friends_net__.js') return id
    },
    load(id) {
      if (id === '/__friends_mobile__.js') return entry
      if (id === '/__qa_friends_net__.js') return qaNet
    },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__friends_mobile__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__friends_mobile__.js"></script></body></html>')
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
  { id: 'requests', purpose: '搜索申请、出站等待以及两条入站申请的接受和拒绝' },
  { id: 'relationship-actions', purpose: '邀请、观战、屏蔽确认、自动进入屏蔽页并取消屏蔽' },
]
const report = {
  schemaVersion: 1, sourceSha, commit, status: 'prepared',
  fixtureTransport: 'actual SiteShell, FriendsPage and ChatPanel over local Vite; synthetic stateful friend API and page-local net bridge; no real service writes',
  interactionBoundary: 'All tabs and actions use Playwright touchscreen tap; text search uses the real input. Native confirm is accepted through the browser dialog event and recorded.',
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
  const box = await locator.boundingBox()
  if (!box) throw new Error('control has no rendered box')
  return { x: box.x, y: box.y, width: box.width, height: box.height }
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
async function tapTwiceWhileBusy(locator, label) {
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  const box = await geometry(locator)
  if (box.width < 44 || box.height < 44) report.advisories.push({ label, viewport: locator.page().viewportSize(), geometry: box, recommendation: '可考虑提升至44px；当前达到既有38px最低可达性门槛' })
  assert.ok(box.width >= 38 && box.height >= 38, `${label} touch target is ${box.width.toFixed(1)}x${box.height.toFixed(1)}`)
  const point = { x: box.x + box.width / 2, y: box.y + box.height / 2 }
  await Promise.all([locator.page().touchscreen.tap(point.x, point.y), locator.page().touchscreen.tap(point.x, point.y)])
  await settle(locator.page())
  return box
}
async function assertNoPageOverflow(page, label) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  assert.ok(overflow <= 1, `${label} horizontal overflow is ${overflow}px`)
}
async function openFriends(page, port) {
  await page.goto(`http://127.0.0.1:${port}/__friends_mobile__`)
  await page.locator('.friends-page').waitFor({ state: 'visible', timeout: 15_000 })
  await page.getByRole('heading', { name: '好友', exact: true }).waitFor()
  await page.getByText('邀请玩家', { exact: true }).first().waitFor()
  await assertNoPageOverflow(page, 'friends initial')
}
const articleFor = (page, name) => page.locator('article').filter({ has: page.getByText(name, { exact: true }) })

async function requestsFlow(page, port) {
  await openFriends(page, port)
  const touches = {}
  touches.addTab = await tapControl(page.getByRole('button', { name: '添加好友', exact: true }), '添加好友标签')
  const input = page.getByPlaceholder('输入完整或部分用户名')
  await input.fill('搜索目标')
  touches.search = await tapControl(page.getByRole('button', { name: '搜索玩家', exact: true }), '搜索玩家')
  const result = articleFor(page, '搜索目标')
  await result.waitFor({ state: 'visible' })
  touches.request = await tapControl(result.getByRole('button', { name: '申请好友', exact: true }), '申请好友')
  await result.getByText('申请已发送', { exact: true }).waitFor()
  touches.requestsTab = await tapControl(page.locator('.tabs').getByRole('button', { name: /^申请\s+2$/ }), '申请标签')
  await page.getByText('等待对方处理', { exact: true }).waitFor()
  const accepting = articleFor(page, '接受玩家')
  touches.accept = await tapControl(accepting.getByRole('button', { name: '接受', exact: true }), '接受好友申请')
  await accepting.waitFor({ state: 'detached' })
  const rejecting = articleFor(page, '拒绝玩家')
  touches.reject = await tapControl(rejecting.getByRole('button', { name: '拒绝', exact: true }), '拒绝好友申请')
  await rejecting.waitFor({ state: 'detached' })
  await page.locator('.tabs').getByRole('button', { name: /^申请\s+0$/ }).waitFor()
  const snapshot = await page.evaluate(() => ({ calls: window.__friendsQa.calls, state: window.__friendsQa.snapshot() }))
  assert.deepEqual(snapshot.calls.filter(call => ['players', 'request', 'resolve'].includes(call.action)).map(call => call.action), ['players', 'request', 'players', 'resolve', 'resolve'])
  assert.ok(snapshot.state.friends.some(item => item.accountId === 'incoming-accept'))
  assert.ok(snapshot.state.requests.some(item => item.accountId === 'search-target' && item.direction === 'outgoing'))
  assert.equal(snapshot.state.requests.some(item => item.accountId === 'incoming-reject'), false)
  assert.equal(snapshot.state.friends.some(item => item.accountId === 'incoming-reject'), false, 'Rejected requester must not become a friend')
  assert.deepEqual(snapshot.calls.filter(call => call.action === 'resolve').map(call => [call.requesterId, call.accept]), [['incoming-accept', true], ['incoming-reject', false]])
  assert.deepEqual(snapshot.calls.filter(call => call.action === 'players').map(call => call.search), ['搜索目标', '搜索目标'])
  await assertNoPageOverflow(page, 'requests final')
  return { touches, stateReadback: { accepted: true, rejectedRemoved: true, outgoingWaiting: true, actionOrder: snapshot.calls.map(call => call.action) } }
}

async function relationshipActionsFlow(page, port) {
  await openFriends(page, port)
  const touches = {}
  const inviteRow = page.locator('.friend-center aside>button').filter({ hasText: '邀请玩家' })
  touches.inviteRow = await tapControl(inviteRow, '邀请玩家行')
  const invite = page.getByRole('button', { name: '邀请对战', exact: true })
  const chatLayout = await page.evaluate(() => {
    const chat=document.querySelector('.chat-panel')?.getBoundingClientRect(),actions=document.querySelector('.friend-actions')?.getBoundingClientRect(),scroller=document.querySelector('.site-content')
    return {chat:chat&&{top:chat.top,bottom:chat.bottom},actions:actions&&{top:actions.top,bottom:actions.bottom},scrollHeight:scroller?.scrollHeight||0,clientHeight:scroller?.clientHeight||0,overlap:Boolean(chat&&actions&&chat.bottom>actions.top+1)}
  })
  assert.equal(chatLayout.overlap, false, 'ChatPanel overlaps friend actions')
  await page.evaluate(() => window.__friendsQa.setNetOffline())
  touches.invite = await tapTwiceWhileBusy(invite, '邀请对战')
  await page.getByText('正在向 邀请玩家 发送对战邀请…', { exact: true }).waitFor()
  const inviteCalls = await page.evaluate(() => window.__friendsQa.calls.filter(call => ['connect', 'inviteFriend'].includes(call.action)))
  assert.deepEqual(inviteCalls.map(call => call.action), ['connect', 'inviteFriend'], '重复触摸绕过邀请动作门闩')
  const focusAfterInvite = await page.evaluate(() => ({ tag: document.activeElement?.tagName || '', text: document.activeElement?.textContent?.trim().slice(0, 80) || '' }))
  const watchRow = page.locator('.friend-center aside>button').filter({ hasText: '观战玩家' })
  touches.watchRow = await tapControl(watchRow, '观战玩家行')
  const watch = page.getByRole('button', { name: '进入观战', exact: true })
  touches.watch = await tapControl(watch, '进入观战')
  await page.getByText('正在进入 观战玩家 的对局…', { exact: true }).waitFor()
  const dialogs=[]
  page.once('dialog', async dialog => { dialogs.push(dialog.message()); await dialog.accept() })
  touches.block = await tapControl(page.locator('.friend-actions').getByRole('button', { name: '屏蔽', exact: true }), '屏蔽好友')
  await page.locator('.blocked-panel').waitFor({ state: 'visible' })
  assert.equal(dialogs.length, 1, '屏蔽确认未通过真实浏览器对话框')
  assert.match(dialogs[0], /观战玩家/)
  assert.equal(await page.locator('.site-modal-mask:visible,[role="dialog"]:visible').count(), 0, '屏蔽确认关闭后仍有遮罩或弹框')
  await page.getByText('观战玩家', { exact: true }).waitFor()
  touches.unblock = await tapControl(articleFor(page, '观战玩家').getByRole('button', { name: '取消屏蔽', exact: true }), '取消屏蔽')
  await page.getByText('已取消屏蔽 观战玩家', { exact: true }).waitFor()
  await page.getByText('暂无已屏蔽玩家', { exact: true }).waitFor()
  const snapshot = await page.evaluate(() => ({ calls: window.__friendsQa.calls, state: window.__friendsQa.snapshot() }))
  assert.deepEqual(snapshot.calls.filter(call => ['inviteFriend', 'spectateRoom', 'block', 'unblock'].includes(call.action)).map(call => call.action), ['inviteFriend', 'spectateRoom', 'block', 'unblock'])
  assert.equal(snapshot.state.friends.some(item => item.accountId === 'friend-watch'), false)
  assert.equal(snapshot.state.blocked.length, 0)
  assert.deepEqual(snapshot.calls.filter(call => call.action === 'inviteFriend').map(call => call.accountId), ['friend-invite'])
  assert.deepEqual(snapshot.calls.filter(call => call.action === 'spectateRoom').map(call => call.roomCode), ['WATCH-ROOM'])
  assert.deepEqual(snapshot.calls.filter(call => ['block', 'unblock'].includes(call.action)).map(call => call.accountId), ['friend-watch', 'friend-watch'])
  await assertNoPageOverflow(page, 'relationship actions final')
  const focusAfterConfirm = await page.evaluate(() => ({ tag: document.activeElement?.tagName || '', text: document.activeElement?.textContent?.trim().slice(0, 80) || '' }))
  return { touches, chatLayout, dialogs, focusObservation: { afterInvite: focusAfterInvite, afterNativeConfirmAndTabChange: focusAfterConfirm }, stateReadback: { blockedThenUnblocked: true, removedFromFriends: true, actionOrder: snapshot.calls.map(call => call.action) } }
}

const handlers = { requests: requestsFlow, 'relationship-actions': relationshipActionsFlow }

try {
  report.status = 'running'
  appendProgress({ stage: 'started', matrix: { flows, viewports }, fixtureTransport: report.fixtureTransport })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of viewports) {
    for (const flow of flows) {
      const test = `${flow.id}-${viewport.label}`
      const context = await browser.newContext({ viewport, deviceScaleFactor: 2, hasTouch: true, isMobile: true })
      const page = await context.newPage()
      page.setDefaultTimeout(10_000)
      page.on('pageerror', error => report.pageErrors.push({ test, message: error.message }))
      page.on('requestfailed', request => {
        if (new URL(request.url()).hostname !== '127.0.0.1') return
        report.requestFailures.push({ test, request: `${request.method()} ${request.url()}`, error: request.failure()?.errorText || 'failed' })
      })
      await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
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
