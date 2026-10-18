import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const out = process.env.L12_FRIENDLY_PREFERENCE_OUTPUT
const cache = process.env.L12_FRIENDLY_PREFERENCE_CACHE
assert(out && cache, 'Select explicit guarded evidence and cache roots')
const profile = path.join(cache, 'profile')
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_GUARD; . $env:L12_INITIALIZER | Out-Null; foreach($p in @($env:L12_FRIENDLY_PREFERENCE_OUTPUT,$env:L12_FRIENDLY_PREFERENCE_CACHE,$env:TEMP)){ $null=Assert-L12GeneratedPath -Path $p -Purpose "friendly mode focused test" }'],
{ stdio: 'pipe', env: { ...process.env, L12_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1'),
  L12_INITIALIZER: path.join(front, '../ops/windows/Initialize-L12BuildEnvironment.ps1') } })
assert(process.env.TEMP && !path.resolve(process.env.TEMP).toLowerCase().startsWith('c:'), 'Initialize this process TEMP before browser launch')
fs.mkdirSync(out, { recursive: true })
const files = ['src/l12/site/BattleHubPage.vue', 'src/l12/friendlyRoomPreferences.ts', 'scripts/test-friendly-room-preferences-ui.mjs']
const hashes = () => Object.fromEntries(files.filter(file => fs.existsSync(path.join(front, file))).map(file =>
  [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, file))).digest('hex').toUpperCase()]))
const report = { status: 'RUNNING', sourceBefore: hashes(), cases: [], errors: [], profile, temp: process.env.TEMP,
  boundaries: ['Real unmodified BattleHub SFC mounted by Vue with synthetic dependency ports; real native select change and remount.',
    'No server, WebSocket acknowledgement, actual room or per-match disaster preparation is simulated as accepted.',
    'One desktop browser viewport; no layout, card art or physical mobile claim.'] }
const stateId = '\0friendly-preference-state'
const stateCode = `
import {reactive} from 'vue'
export const sent=[]
export const platformState=reactive({account:{id:'account-a',username:'账号A'},token:'synthetic-a'})
export const l12State=reactive({status:'online',room:null,game:null,notice:'',spectating:false,operationsPolicy:null})
export const policy=mode=>({version:1,defaultRoomConfig:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:mode},cardRestrictions:[],disasterCardIds:[],defaultPresetDeckIds:[],maintenance:{entryBlocked:false},season:{},announcements:[]})
export const control={defaultMode:'all',defer:false,requests:0,pending:[]}
export function getEffectiveOperationsPolicy(){control.requests++;return control.defer?new Promise(resolve=>control.pending.push(resolve)):Promise.resolve(policy(control.defaultMode))}
export const rankedApi={overview:()=>new Promise(()=>{})}
export const normalizeRankedConfig=value=>value
export const connect=async()=>{l12State.status='online'}
export const createRoom=options=>sent.push({type:'createRoom',options:JSON.parse(JSON.stringify(options))})
export const updateRoomOptions=options=>sent.push({type:'updateRoomOptions',options:JSON.parse(JSON.stringify(options))})
export const joinRoom=roomCode=>sent.push({type:'joinRoom',roomCode})
export const spectateRoom=roomCode=>sent.push({type:'spectateRoom',roomCode})
export const leaveRoom=()=>sent.push({type:'leaveRoom'}),cancelMatchmaking=()=>{},joinMatchmaking=()=>{},selectCustomDeck=()=>{},setReady=()=>{}
export const L12_DECK_SELECTION_SCOPES=['ranked','casual','friendly','sandbox-player','sandbox-opponent']
export const loadSavedDecksState=()=>({status:'ready',decks:{}}),loadSelectedDeckName=()=>'',saveSelectedDeckName=()=>{}
export const ensureOfficialPrebuiltDecks=async()=>({}),loadDeckCatalog=async()=>[],loadPrivateDeckBody=async()=>{throw Error('unexpected private body read')}
export const deckErrorBelongsToCurrentAccount=()=>true,deckCountSummary=()=>({label:'0'}),validateDeck=()=>''
export const SELECTED_DECK_KEY='fixture-deck-key'
`
const entry = `
import {createApp,defineComponent,h,ref,nextTick} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import BattleHub from '/src/l12/site/BattleHubPage.vue'
import * as fixture from 'friendly-preference-state'
const generation=ref(0),router=createRouter({history:createMemoryHistory(),routes:[{path:'/battle',component:{render:()=>null}},{path:'/me',component:{render:()=>null}},{path:'/decks',component:{render:()=>null}}]})
await router.push('/battle')
createApp(defineComponent({setup:()=>()=>h(BattleHub,{key:generation.value})})).use(router).mount('#app')
window.__friendlyQa={...fixture,remount:async()=>{generation.value++;await nextTick()},identity:id=>{fixture.platformState.account=id?{id,username:id}:null;fixture.platformState.token=id?'synthetic-'+id:''},resolve:mode=>{const pending=fixture.control.pending.splice(0);for(const resolve of pending)resolve(fixture.policy(mode))},room:mode=>{fixture.l12State.room={roomCode:'QA0001',yourPlayerIndex:0,started:false,players:[{playerIndex:0,name:'合成房主',connected:true,ready:false}],options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:mode,useCardRestrictions:false}}}}
`
const server = await createServer({ root: front, configLoader: 'runner', cacheDir: path.join(cache, 'vite'),
  server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'friendly-mode-preference-fixture', enforce: 'pre',
    resolveId(id, importer) {
      if (id === 'friendly-preference-state') return stateId
      if (id === '/__friendly_preference__.js') return id
      if (!importer?.replaceAll('\\', '/').includes('/src/l12/site/BattleHubPage.vue')) return
      if (/\/(net|decks|platform)(\.ts)?$/.test(id)) return stateId
      if (id.endsWith('.vue')) return '\0friendly-preference-component'
    },
    load(id) {
      if (id === stateId) return stateCode
      if (id === '\0friendly-preference-component') return 'export default {render:()=>null}'
      if (id === '/__friendly_preference__.js') return entry
    },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (new URL(request.url || '/', 'http://fixture').pathname !== '/__friendly_preference__') return next()
      response.setHeader('Content-Type', 'text/html'); response.end('<!doctype html><html><body><div id="app"></div><script type="module" src="/__friendly_preference__.js"></script></body></html>')
    }) },
  }] })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let context, page
const check = async (name, work) => { await work(); report.cases.push(name); console.log('PASS ' + name) }
const createMode = () => page.locator('.friendly-panel .room-settings select').last()
async function showFriendly() { await page.getByRole('button', { name: '好友房', exact: true }).click(); await createMode().waitFor() }
async function remount() { await page.evaluate(() => { window.__friendlyQa.l12State.room=null; return window.__friendlyQa.remount() }); await showFriendly() }
try {
  await server.listen()
  context = await chromium.launchPersistentContext(profile, { headless: true, channel: 'msedge', viewport: { width: 1366, height: 768 } })
  await context.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  page = await context.newPage(); page.setDefaultTimeout(8000)
  page.on('pageerror', error => report.errors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') report.errors.push(message.text()) })
  await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__friendly_preference__')
  await showFriendly()
  await page.waitForFunction(() => window.__friendlyQa.control.requests >= 1)
  await check('active account native selection survives actual SFC remount', async () => {
    assert.equal(await createMode().inputValue(), 'all')
    await createMode().selectOption('random'); assert.equal(await createMode().inputValue(), 'random')
    await remount()
    report.reentryMode = await createMode().inputValue()
    assert.equal(report.reentryMode, 'random', 'RED: re-entry resets chosen mode to operations default')
  })
  await check('all four legal modes persist without a room or network success', async () => {
    for (const mode of ['all','random','season','none']) { await createMode().selectOption(mode); await remount(); assert.equal(await createMode().inputValue(), mode) }
    assert.deepEqual(await page.evaluate(() => window.__friendlyQa.sent), [])
  })
  await check('account A B anonymous ABA restore synchronously without cross-scope writes', async () => {
    await createMode().selectOption('season')
    await page.evaluate(() => window.__friendlyQa.identity('account-b')); assert.equal(await createMode().inputValue(), 'all')
    await createMode().selectOption('none')
    await page.evaluate(() => window.__friendlyQa.identity(null)); assert.equal(await createMode().inputValue(), 'all')
    await createMode().selectOption('random')
    await page.evaluate(() => window.__friendlyQa.identity('account-a')); assert.equal(await createMode().inputValue(), 'season')
    await page.evaluate(() => { window.__friendlyQa.identity('account-b'); window.__friendlyQa.identity('account-a') }); assert.equal(await createMode().inputValue(), 'season')
    await page.evaluate(() => window.__friendlyQa.identity('account-b')); assert.equal(await createMode().inputValue(), 'none')
    await page.evaluate(() => window.__friendlyQa.identity(null)); assert.equal(await createMode().inputValue(), 'random')
  })
  await check('untouched new scope follows operations default', async () => {
    await page.evaluate(() => { window.__friendlyQa.control.defaultMode='season'; window.__friendlyQa.l12State.operationsPolicy=window.__friendlyQa.policy('season'); window.__friendlyQa.identity('fresh-default') })
    await remount(); assert.equal(await createMode().inputValue(), 'season')
  })
  await check('late operations default does not overwrite a manual choice', async () => {
    await page.evaluate(() => { window.__friendlyQa.control.defer=true; window.__friendlyQa.l12State.operationsPolicy=null; window.__friendlyQa.identity('fresh-late') })
    await remount(); await page.waitForFunction(() => window.__friendlyQa.control.pending.length > 0)
    await createMode().selectOption('random')
    await page.evaluate(() => window.__friendlyQa.resolve('none')); assert.equal(await createMode().inputValue(), 'random')
  })
  await check('late old identity defaults cannot overwrite restored preference after ABA', async () => {
    await page.evaluate(() => { window.__friendlyQa.identity('account-b'); window.__friendlyQa.identity('account-a'); window.__friendlyQa.resolve('all'); window.__friendlyQa.l12State.operationsPolicy=window.__friendlyQa.policy('all') })
    assert.equal(await createMode().inputValue(), 'season')
  })
  await check('guest join broadcast leave and next create retain own mode without unmount; explicit room change persists', async () => {
    const before = await page.evaluate(() => JSON.stringify(localStorage))
    await page.locator('.join-row input').fill('QA0001')
    await page.getByRole('button', { name: '加入对战', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__friendlyQa.sent.at(-1).type), 'joinRoom')
    await page.evaluate(() => { window.__friendlyHubElement=document.querySelector('.battle-hub'); window.__friendlyQa.room('none'); window.__friendlyQa.l12State.room.yourPlayerIndex=1 })
    assert.equal(await page.locator('.room-rule-editor').count(), 0)
    assert.equal(await page.evaluate(() => JSON.stringify(localStorage)), before)
    await page.getByRole('button', { name: '离开房间并返回大厅', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__friendlyQa.sent.at(-1).type), 'leaveRoom')
    await page.evaluate(() => { window.__friendlyQa.l12State.room=null })
    assert.equal(await page.evaluate(() => document.querySelector('.battle-hub') === window.__friendlyHubElement), true)
    assert.equal(await createMode().inputValue(), 'season')
    await page.getByRole('button', { name: '创建新房间', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__friendlyQa.sent.at(-1).type), 'createRoom')
    assert.equal(await page.evaluate(() => window.__friendlyQa.sent.at(-1).options.disasterMode), 'season')
    assert.equal(await page.evaluate(() => JSON.stringify(localStorage)), before)
    await page.evaluate(() => window.__friendlyQa.room('none'))
    const roomMode = page.locator('.room-rule-editor select').last(); assert.equal(await roomMode.inputValue(), 'none')
    assert.equal(await page.evaluate(() => JSON.stringify(localStorage)), before)
    await roomMode.selectOption('random')
    await page.getByRole('button', { name: '保存房间规则', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__friendlyQa.sent.at(-1).options.disasterMode), 'random')
    await remount(); assert.equal(await createMode().inputValue(), 'random')
  })
  await check('quota failure keeps manual choice live and protects it from late default', async () => {
    await page.evaluate(() => { window.__friendlyQa.identity('quota'); Storage.prototype.__qaSet=Storage.prototype.setItem; Storage.prototype.setItem=function(){throw new DOMException('synthetic quota','QuotaExceededError')} })
    await createMode().selectOption('none')
    await page.evaluate(() => { window.__friendlyQa.l12State.operationsPolicy=window.__friendlyQa.policy('all'); Storage.prototype.setItem=Storage.prototype.__qaSet })
    assert.equal(await createMode().inputValue(), 'none')
  })
  await page.screenshot({ path: path.join(out, 'friendly-mode.png') })
  assert.deepEqual(report.errors, [])
  report.status='GREEN'
} catch (error) {
  report.status='RED'; report.failure=error.stack || String(error)
  if (page) await page.screenshot({ path: path.join(out, 'failure.png'), timeout: 8000 }).catch(() => {})
  throw error
} finally {
  report.sourceAfter=hashes()
  try { assert.deepEqual(report.sourceAfter,report.sourceBefore) } finally {
    fs.writeFileSync(path.join(out,'report.json'), JSON.stringify(report,null,2)+'\n')
    await context?.close(); await server.close()
  }
}
