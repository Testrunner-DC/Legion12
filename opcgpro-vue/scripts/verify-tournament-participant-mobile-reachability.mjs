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

const output = requiredEnvironment('L12_TOURNAMENT_PARTICIPANT_OUT')
const cacheDir = requiredEnvironment('L12_TOURNAMENT_PARTICIPANT_CACHE')
for (const candidate of [process.env.TEMP, process.env.TMP, process.env.TMPDIR])
  if (!candidate) throw new Error('TEMP, TMP and TMPDIR must be initialized')
const pwsh = requiredEnvironment('L12_PWSH')
assert.ok(path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be an existing absolute executable selected by the shared initializer')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '$ErrorActionPreference="Stop"; . $env:L12_STORAGE_GUARD; '
  + '@($env:L12_ACCEPTANCE_OUT,$env:L12_ACCEPTANCE_CACHE,$env:TEMP,$env:TMP,$env:TMPDIR) '
  + '| ForEach-Object {Assert-L12GeneratedPath -Path $_ -Purpose "tournament participant mobile reachability" | Out-Null}',
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

const params=new URLSearchParams(location.search)
const scenario=params.get('scenario')||'active'
const account={id:'qa-player',username:'移动参赛者',role:'player',createdAt:'2026-10-01T00:00:00Z',publicHistory:true,permissionVersion:3}
localStorage.setItem('l12-auth-token','qa-participant-token')
localStorage.setItem('l12-account',JSON.stringify(account))

const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-participant-token'
platform.platformState.account=account
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false
platform.friendApi.presence=async()=>[]
platform.alternateArtApi.notifications=async()=>[]

const calls=[]
const participant=(extra={})=>({accountId:account.id,username:account.username,checkedIn:false,dropped:false,eliminated:false,removed:false,registrationBanned:false,seed:2,waitlisted:false,...extra})
const opponent={accountId:'qa-opponent',username:'对手玩家',checkedIn:true,dropped:false,eliminated:false,removed:false,registrationBanned:false,seed:1,waitlisted:false,deck:{name:'对手牌组',hash:'opponent-hash',submittedAt:'2026-10-22T01:00:00Z',lockedAt:'2026-10-22T01:00:00Z',masterId:'S01-M02',cardIds:[],moraleIds:[],specialIds:[]}}
const counts=()=>({registered:state.participants.filter(item=>!item.waitlisted).length,pendingCheckIn:state.participants.filter(item=>!item.waitlisted&&!item.deck).length,checkedIn:state.participants.filter(item=>Boolean(item.deck)).length,active:state.participants.filter(item=>!item.waitlisted&&!item.dropped&&!item.removed).length,waitlisted:state.participants.filter(item=>item.waitlisted).length,dropped:state.participants.filter(item=>item.dropped).length,removed:state.participants.filter(item=>item.removed).length,registrationBanned:state.participants.filter(item=>item.registrationBanned).length})
let state={
 id:'qa-tournament',code:'QA-MOBILE',name:'移动端参赛操作验收赛',organizerAccountId:'qa-organizer',organizerName:'验收主办者',referees:[],status:'registration',phase:'pre-check-in',format:'swiss',visibility:'public',registrationVisibility:'public',registrationOpen:true,maxPlayers:scenario==='waitlist'?1:8,startAt:'2026-10-22T08:00:00Z',description:'合成状态仅验证普通参赛者的真实页面触控与状态回读。',
 rules:{ruleset:'2026-current',disasterMode:'all',banList:'',disasterCardIds:[],cardRestrictions:[],deckVisibility:'after',ruleContentHash:'rule-content',hash:'rules-hash',capturedAt:'2026-10-22T00:00:00Z'},
 timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},roundMinutes:50,checkInMinutes:10,lateGraceMinutes:5,usesLegacyRoundClock:false,
 participants:scenario==='waitlist'?[opponent]:[],rounds:[],version:1,legacyImported:false,createdAt:'2026-10-22T00:00:00Z',updatedAt:'2026-10-22T00:00:00Z',swissRounds:3,cutSize:null,finalSwissStandings:[],eliminationBracket:[],postponements:[],judgeCases:[],organizerTransferHistory:[]
}
state.counts=counts()
const reply=()=>structuredClone(state)
const advance=()=>{state.version++;state.updatedAt=new Date().toISOString();state.counts=counts();return reply()}
const requireFixture=(ok,message)=>{if(!ok)throw new Error('Fixture contract: '+message)}
const requireMutation=(id,version)=>{requireFixture(id===state.id,'tournament id');requireFixture(version===state.version,'current version')}
platform.tournamentApi.getByCode=async code=>{requireFixture(code===state.code,'tournament code');calls.push({action:'getByCode',code});return reply()}
platform.tournamentApi.register=async(id,version)=>{
 requireMutation(id,version)
 requireFixture(state.status==='registration'&&!state.participants.some(item=>item.accountId===account.id),'registration eligibility')
 calls.push({action:'register',id,version})
 const waitlisted=scenario==='waitlist'
 state.participants.push(participant(waitlisted?{waitlisted:true,waitlistPosition:1}:{}))
 return advance()
}
platform.tournamentApi.drop=async(id,version)=>{
 requireMutation(id,version)
 requireFixture(state.participants.some(item=>item.accountId===account.id),'drop participant')
 calls.push({action:'drop',id,version})
 state.participants=state.participants.filter(item=>item.accountId!==account.id)
 return advance()
}
platform.tournamentApi.preCheckIn=async(id,version,deckName,deckCode,deckId)=>{
 requireMutation(id,version)
 requireFixture(state.status==='registration'&&deckId==='deck-mobile'&&deckName==='移动秩序牌组'&&deckCode==='','pre-checkin deck identity')
 calls.push({action:'preCheckIn',id,version,deckName,deckCode,deckId})
 const me=state.participants.find(item=>item.accountId===account.id)
 me.deck={name:deckName,hash:'qa-deck-hash',submittedAt:new Date().toISOString(),lockedAt:new Date().toISOString(),masterId:'S01-M01',cardIds:[],moraleIds:[],specialIds:[]}
 return advance()
}
platform.tournamentApi.checkIn=async(id,roundNumber,version,unused,ready)=>{
 requireMutation(id,version)
 requireFixture(roundNumber===1&&ready===true&&unused===undefined&&state.rounds[0]?.status==='checkin','round checkin arguments')
 calls.push({action:'checkIn',id,roundNumber,version,ready})
 const match=state.rounds[0].matches[0];match.readyA=ready;match.canEnter=false;match.status='waiting'
 return advance()
}
function organizerOpensCheckin(){
 requireFixture(state.status==='registration'&&Boolean(state.participants.find(item=>item.accountId===account.id)?.deck),'organizer opens after locked deck')
 calls.push({action:'fixture-organizer-opens-checkin'})
 state.participants.push(opponent);state.status='running';state.phase='running';state.registrationOpen=false
 state.rounds=[{id:'round-1',number:1,status:'checkin',paused:false,totalPausedSeconds:0,stage:'swiss',standings:[],matches:[{id:'match-1',table:1,playerAAccountId:account.id,playerAName:account.username,playerBAccountId:opponent.accountId,playerBName:opponent.username,roomCode:'QA-ROOM',readyA:false,readyB:true,status:'waiting',timeExtensionMinutes:0,rulings:[],sourceMatchIds:[],rulesHash:'rules-hash',replayNumber:0,canEnter:false,canSpectate:false,events:[],paused:false,totalPausedSeconds:0}]}]
 advance();window.dispatchEvent(new Event('l12-resource-tournaments'))
}
function organizerStartsRound(){
 requireFixture(state.rounds[0]?.status==='checkin'&&state.rounds[0].matches[0].readyA,'organizer starts after player ready')
 calls.push({action:'fixture-organizer-starts-round'})
 state.rounds[0].status='running';state.rounds[0].matches[0].status='running';state.rounds[0].matches[0].canEnter=true
 advance();window.dispatchEvent(new Event('l12-resource-tournaments'))
}
platform.tournamentApi.exportCsv=async()=>new Blob(['qa'])

const catalogVersion='A'.repeat(64)
const deck={id:'deck-mobile',revision:2,name:'移动秩序牌组',masterId:'S01-M01',updatedAt:'2026-10-22T00:00:00Z',publicationId:null,publicationVersion:null,counts:{main:40,uncountedMain:0,morale:10,special:0,bench:0},legal:true,legalityReason:null}
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init={})=>{
 const parsed=new URL(String(typeof input==='string'?input:input.url),location.origin)
 const method=String(init.method||'GET').toUpperCase()
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(parsed.pathname==='/api/decks/summaries'&&method==='GET'){calls.push({action:'deck-summary-read',keyword:parsed.searchParams.get('keyword')||''});return json({items:[deck],total:1,page:Number(parsed.searchParams.get('page')||1),pageSize:Number(parsed.searchParams.get('pageSize')||30),generation:1,permissionVersion:3,catalogVersion,policyVersion:1,facets:{masters:[{masterId:deck.masterId,count:1}],legal:1,illegal:0}})}
 if(parsed.pathname==='/api/operations/effective-policy')return json({version:1,season:{},disasterCardIds:[],matchModes:[],defaultRoomConfig:{},seasonDisasterModeAvailable:true,cardRestrictions:[],defaultPresetDeckIds:[],maintenance:{enabled:false,active:false,entryBlocked:false,status:'open',message:'',broadcastMessage:'',advanceBroadcastHours:0,expectedDurationHours:0},announcements:[]})
 if(parsed.pathname==='/api/content')return json({values:{},observedAt:new Date().toISOString(),nextRuleTransitionAt:null})
 if(parsed.pathname==='/api/friends/overview')return json({friends:[],requests:[],blocked:[]})
 if(parsed.pathname.startsWith('/api/'))return json([])
 return originalFetch(input,init)
}

const [{default:SiteShell},{default:TournamentDetailPage}]=await Promise.all([import('/src/l12/site/SiteShell.vue'),import('/src/l12/site/TournamentDetailPage.vue')])
const Game={render:()=>h('main',{'data-qa':'game-route'},'已进入赛事对局')}
const Hub={render:()=>h('main',{'data-qa':'tournament-hub'},'赛事中心')}
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/battle/tournaments',component:Hub,meta:{section:'battle'}},
 {path:'/battle/tournaments/:code',component:TournamentDetailPage,meta:{section:'battle'}},
 {path:'/game',component:Game,meta:{immersive:true,section:'battle'}},
]})
await router.push('/battle/tournaments/QA-MOBILE')
await router.isReady()
window.__tournamentQa={calls,router,snapshot:()=>structuredClone(state),scenario,organizerOpensCheckin,organizerStartsRound}
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

const qaNet = String.raw`
import {reactive} from 'vue'
export const l12State=reactive({room:null,game:null,notice:'',presence:[],status:'offline',connectionIssue:null,recoveryPhase:null})
export async function connect(){window.__tournamentQa.calls.push({action:'connect'})}
export function enterTournamentMatch(tournamentId,matchId){const state=window.__tournamentQa.snapshot();if(tournamentId!==state.id||matchId!=='match-1'||state.rounds[0]?.matches[0].status!=='running'||!state.rounds[0].matches[0].canEnter)throw new Error('Exact ready match dispatch required');window.__tournamentQa.calls.push({action:'enterTournamentMatch',tournamentId,matchId});l12State.room={roomCode:'QA-ROOM'};l12State.game={tournamentMatchId:matchId}}
export function spectateTournamentMatch(tournamentId,matchId){window.__tournamentQa.calls.push({action:'spectateTournamentMatch',tournamentId,matchId})}
`

const server = await createServer({
  root, configLoader: 'runner', cacheDir,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'tournament-participant-mobile-reachability-fixture', enforce: 'pre',
    transform(code, id) {
      if (id.includes('?') || !id.endsWith('/src/l12/site/TournamentAccountDetail.vue')) return
      const anchor = "from '@/l12/net'"
      assert.equal(code.split(anchor).length, 2, 'TournamentAccountDetail net import changed')
      return code.replace(anchor, "from '/__qa_tournament_net__.js'")
    },
    resolveId(id) {
      if (id === '/__tournament_participant_mobile__.js' || id === '/__qa_tournament_net__.js') return id
    },
    load(id) {
      if (id === '/__tournament_participant_mobile__.js') return entry
      if (id === '/__qa_tournament_net__.js') return qaNet
    },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__tournament_participant_mobile__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<!doctype html><html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__tournament_participant_mobile__.js"></script></body></html>')
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
  { id: 'active-participant', purpose: '报名、选择账号牌库、赛前签到锁牌、本轮签到并进入对局' },
  { id: 'waitlist-drop', purpose: '满员报名进入候补，并经两次真实触摸确认退出赛事' },
]
const report = {
  schemaVersion: 1, sourceSha, commit, status: 'running',
  fixtureTransport: 'actual SiteShell and TournamentAccountDetail over local Vite; synthetic stateful tournament/deck APIs; no real service writes',
  interactionBoundary: 'Every action button uses Playwright touchscreen tap. Native select uses selectOption after a real tap because headless browsers do not expose the operating-system picker.',
  phaseBoundary: 'Participant APIs only register, lock a deck or check in. Separate named fixture-organizer events supply round creation/start responses through the real resource refresh listener. Entry verifies exact room-handler dispatch and synthetic ready navigation, not real net/WebSocket handshake.',
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
async function assertNoPageOverflow(page, label) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
  assert.ok(overflow <= 1, `${label} horizontal overflow is ${overflow}px`)
}
async function openScenario(page, port, scenario) {
  await page.goto(`http://127.0.0.1:${port}/__tournament_participant_mobile__?scenario=${scenario}`)
  await page.locator('.detail-page').waitFor({ state: 'visible', timeout: 15_000 })
  await page.getByRole('heading', { name: '移动端参赛操作验收赛', exact: true }).waitFor()
  await assertNoPageOverflow(page, `${scenario} initial`)
}

async function activeParticipantFlow(page, port) {
  await openScenario(page, port, 'active')
  const touches = {}
  touches.register = await tapControl(page.getByRole('button', { name: '报名（暂不提交牌库）', exact: true }), '报名')
  await page.getByText('报名成功；牌组将在赛前签到时锁定', { exact: true }).waitFor()
  const search = page.getByLabel('搜索账号牌库')
  await search.fill('移动秩序')
  touches.search = await tapControl(page.getByRole('button', { name: '搜索', exact: true }), '搜索牌库')
  const select = page.getByLabel('选择账号牌库')
  await select.waitFor({ state: 'visible' })
  await select.scrollIntoViewIfNeeded()
  const selectGeometry = await geometry(select)
  if (selectGeometry.width < 44 || selectGeometry.height < 44) report.advisories.push({ label: '选择账号牌库', viewport: page.viewportSize(), geometry: selectGeometry, recommendation: '可考虑提升至44px；当前达到既有38px最低可达性门槛' })
  assert.ok(selectGeometry.width >= 38 && selectGeometry.height >= 38, `选择账号牌库 touch target is ${selectGeometry.width.toFixed(1)}x${selectGeometry.height.toFixed(1)}`)
  await select.tap()
  await select.selectOption('deck-mobile')
  touches.deckSelect = selectGeometry
  touches.preCheckIn = await tapControl(page.getByRole('button', { name: '签到并锁定牌库', exact: true }), '赛前签到锁牌')
  await page.getByText('牌库快照已锁定，赛前签到完成', { exact: true }).waitFor()
  const afterLock = await page.evaluate(() => window.__tournamentQa.snapshot())
  assert.equal(afterLock.status, 'registration', 'Participant locking a deck does not start a tournament')
  assert.equal(afterLock.rounds.length, 0)
  await page.evaluate(() => window.__tournamentQa.organizerOpensCheckin())
  touches.schedule = await tapControl(page.getByRole('button', { name: '赛程', exact: true }), '赛程标签')
  touches.roundCheckIn = await tapControl(page.getByRole('button', { name: '本轮签到', exact: true }), '本轮签到')
  await page.getByText('本轮到场已确认', { exact: true }).waitFor()
  assert.equal((await page.evaluate(() => window.__tournamentQa.snapshot())).rounds[0].matches[0].canEnter, false, 'Check-in alone must not open the room')
  assert.equal(await page.getByRole('button', { name: '进入对局', exact: true }).count(), 0)
  await page.evaluate(() => window.__tournamentQa.organizerStartsRound())
  const enter = page.getByRole('button', { name: '进入对局', exact: true })
  touches.enter = await tapControl(enter, '进入对局')
  await page.locator('[data-qa="game-route"]').waitFor({ state: 'visible' })
  const snapshot = await page.evaluate(() => ({ calls: window.__tournamentQa.calls, route: window.__tournamentQa.router.currentRoute.value.fullPath, state: window.__tournamentQa.snapshot() }))
  assert.deepEqual(snapshot.calls.filter(call => ['register', 'preCheckIn', 'checkIn', 'enterTournamentMatch'].includes(call.action)).map(call => call.action), ['register', 'preCheckIn', 'checkIn', 'enterTournamentMatch'])
  assert.equal(snapshot.route, '/game')
  assert.equal(snapshot.state.participants.find(item => item.accountId === 'qa-player')?.deck?.name, '移动秩序牌组')
  assert.equal(snapshot.state.rounds[0].matches[0].readyA, true)
  assert.equal(snapshot.state.rounds[0].matches[0].status, 'running')
  assert.ok(snapshot.calls.some(call => call.action === 'deck-summary-read' && call.keyword === '移动秩序'), 'Search must send the exact keyword to the real summary consumer')
  assert.deepEqual(snapshot.calls.filter(call => call.action.startsWith('fixture-organizer-')).map(call => call.action), ['fixture-organizer-opens-checkin', 'fixture-organizer-starts-round'])
  await assertNoPageOverflow(page, 'active participant final')
  return { touches, stateReadback: { route: snapshot.route, lockedDeck: '移动秩序牌组', roundReady: true, actionOrder: snapshot.calls.map(call => call.action) } }
}

async function waitlistDropFlow(page, port) {
  await openScenario(page, port, 'waitlist')
  const touches = {}
  touches.register = await tapControl(page.getByRole('button', { name: '报名（暂不提交牌库）', exact: true }), '候补报名')
  await page.getByText('当前候补第 1 位，正式席位释放后会自动递补。', { exact: true }).waitFor()
  const drop = page.getByRole('button', { name: '退出赛事', exact: true })
  touches.dropArm = await tapControl(drop, '退出赛事')
  await page.getByText('再次点击确认退出赛事；正式席位释放后会自动递补候补玩家', { exact: true }).waitFor()
  touches.dropConfirm = await tapControl(page.getByRole('button', { name: '确认退出赛事', exact: true }), '确认退出赛事')
  await page.getByText('已退出赛事；候补将自动递补', { exact: true }).waitFor()
  await page.getByRole('button', { name: '报名（暂不提交牌库）', exact: true }).waitFor()
  const snapshot = await page.evaluate(() => ({ calls: window.__tournamentQa.calls, state: window.__tournamentQa.snapshot() }))
  assert.deepEqual(snapshot.calls.filter(call => ['register', 'drop'].includes(call.action)).map(call => call.action), ['register', 'drop'])
  assert.equal(snapshot.state.participants.some(item => item.accountId === 'qa-player'), false)
  assert.equal(snapshot.state.counts.waitlisted, 0)
  await assertNoPageOverflow(page, 'waitlist drop final')
  return { touches, stateReadback: { participantPresent: false, waitlisted: 0, actionOrder: snapshot.calls.map(call => call.action) } }
}

const handlers = { 'active-participant': activeParticipantFlow, 'waitlist-drop': waitlistDropFlow }

try {
  appendProgress({ stage: 'started', matrix: { flows, viewports }, fixtureTransport: report.fixtureTransport })
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of viewports) {
    for (const flow of flows) {
      const test = `${flow.id}-${viewport.label}`
      let stage = 'context'
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
        stage = flow.id
        const evidence = await handlers[flow.id](page, port)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'passed', evidence })
        appendProgress({ stage: 'case-passed', test })
        await page.screenshot({ path: path.join(output, `${test}.png`), fullPage: true })
      } catch (error) {
        recordFailure(test, stage, error)
        report.matrix.push({ test, flow: flow.id, viewport: viewport.label, status: 'failed', stage, message: failureMessage(error) })
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
