import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const scriptFile = fileURLToPath(import.meta.url)
const front = path.resolve(path.dirname(scriptFile), '..')
const worktree = path.resolve(front, '..')
const output = process.env.L12_FREE_ACTION_GATE_OUT
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp', 'free-action-presentation-gate-20261024')

assert.ok(output && path.isAbsolute(output), 'L12_FREE_ACTION_GATE_OUT must be an absolute controlled path')
assert.ok(path.isAbsolute(cache), 'L12_WORK_CACHE must be an absolute controlled path')
assert.equal(fs.existsSync(output), false, `refusing to overwrite evidence: ${output}`)

const guardEnv = {
  ...process.env,
  L12_FREE_ACTION_GATE_OUT: output,
  L12_WORK_CACHE: cache,
  TEMP: temp,
  TMP: temp,
  TMPDIR: temp,
  L12_INITIALIZER: path.join(worktree, 'ops/windows/Initialize-L12BuildEnvironment.ps1'),
  L12_GUARD: path.join(worktree, 'ops/windows/L12GeneratedStorage.ps1'),
}
execFileSync(guardEnv.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'), [
  '-NoProfile', '-NonInteractive', '-Command',
  '& $env:L12_INITIALIZER -CacheRoot $env:L12_WORK_CACHE | Out-Null; . $env:L12_GUARD; '
    + '@($env:L12_FREE_ACTION_GATE_OUT,$env:L12_WORK_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "free-action presentation gate QA" }',
], { stdio: 'pipe', env: guardEnv })
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(temp, { recursive: true })
Object.assign(process.env, { TEMP: temp, TMP: temp, TMPDIR: temp })

const harnessHtml = `<!doctype html>
<html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head>
<body><div id="app"></div><script type="module" src="/__free_action_gate_fixture__.js"></script></body></html>`

const harnessSource = String.raw`
import { createApp, h, nextTick, reactive, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { l12State } from '/src/l12/net.ts'
import { useLandscapeViewport } from '/src/l12/mobileViewport.ts'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'

const params = new URLSearchParams(location.search)
const actor = Number(params.get('actor') || 0) === 1 ? 1 : 0
const spectator = params.get('spectator') === '1'
const mobile = params.get('mobile') === '1'
if (mobile) {
  const nativeMatchMedia = window.matchMedia.bind(window)
  window.matchMedia = query => query === '(pointer: coarse) and (hover: none)'
    ? { matches:true, media:query, addEventListener(){}, removeEventListener(){}, addListener(){}, removeListener(){}, dispatchEvent(){ return false } }
    : nativeMatchMedia(query)
}

const face = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#183d49"/><path d="M25 40h200v270H25z" fill="#d5bc70"/><path d="M35 50h180v250H35z" fill="#10262e"/><text x="125" y="175" text-anchor="middle" fill="#fff2c7" font-size="26">GATE QA</text></svg>')
const makeCard = (instanceId, name, cardType = 'legion') => ({
  instanceId, name, cardId: cardType === 'tactic' ? 'QA-TACTIC' : 'QA-LEGION', cardType,
  faction:'otherworld', cost:0, currentCost:0, playCost:0, minimumPlayCost:0,
  baseTroops:cardType === 'legion' ? 3000 : 0, troops:cardType === 'legion' ? 3000 : 0,
  disasterLevel:0, tapped:false, summonRound:0, imageUrl:face,
})
const tactic = makeCard('gate-tactic-' + actor, '事务门闩测试战术', 'tactic')
const followupTactic = makeCard('gate-followup-' + actor, '事务门闩后续战术', 'tactic')
const makePlayer = playerIndex => ({
  playerIndex, name:'测试玩家' + (playerIndex + 1), deckName:'事务门闩', faction:'otherworld',
  master:{ masterId:'QA-MASTER-' + playerIndex, masterName:'测试主宰' + (playerIndex + 1), masterImageUrl:face, hp:8, maxHp:8 },
  libraryCount:20, libraryTop:null,
  hand:playerIndex === actor && !spectator ? [tactic, followupTactic] : [],
  handCount:playerIndex === actor ? 2 : 0,
  morale:[], spendableResourceCount:3,
  field:[[makeCard('gate-unit-' + playerIndex, '测试军团' + (playerIndex + 1)), null, null], [null, null, null]],
  graveyard:[], graveyardCount:0, resolving:[],
  specialZones:{ runes:0, trialLevel:0, godPower:[], trials:[] }, mulliganDone:true,
})
const game = reactive({
  matchId:'free-action-gate-match', roomCode:'GATE', you:actor, revision:1,
  activePlayer:actor, firstPlayer:0, diceWinner:0, initiativeRolls:[5, 2], phase:'Main', round:2, turnSerial:2,
  disasterMode:'none', disasterValue:0, prompts:[], effectStack:[], players:[makePlayer(0), makePlayer(1)],
  recentEvents:[], legalAttackTargets:{}, stateHash:'free-action-gate-1',
})
const sent = []
const fakeSocket = {
  readyState:WebSocket.OPEN,
  send(raw) { sent.push(JSON.parse(raw)) },
  close() {},
}
Object.assign(l12State, {
  socket:fakeSocket, status:'online', recoveryPhase:'snapshot-acknowledged', game,
  spectating:spectator, observerView:spectator ? 'public' : 'player', pendingAction:false,
  gmEnabled:false, refereeControlsEnabled:false, leavingRoom:false,
})

const mounted = ref(true)
const presentationBusy = ref(false)
const busyTrace = []
const transientStarts = []
let sequence = 0
function recordTransient(node) {
  if (!(node instanceof Element)) return
  const elements = [node, ...node.querySelectorAll('.zone-card-movement,.public-reveal-animation,.l12-card-state-transition-ghost,.l12-combat-defeat-ghost')]
  for (const element of elements) {
    if (!element.matches('.zone-card-movement,.public-reveal-animation,.l12-card-state-transition-ghost,.l12-combat-defeat-ghost')) continue
    transientStarts.push({
      kind:element.classList.contains('zone-card-movement') ? 'zone'
        : element.classList.contains('public-reveal-animation') ? 'reveal'
          : element.classList.contains('l12-card-state-transition-ghost') ? 'state' : 'combat',
      key:element.getAttribute('data-movement-key') || element.getAttribute('data-visual-transition-key') || '',
      at:performance.now(),
    })
  }
}
const observer = new MutationObserver(records => {
  for (const record of records) for (const node of record.addedNodes) recordTransient(node)
})
observer.observe(document.documentElement, { childList:true, subtree:true })

function nextEvents(withPrompt) {
  const groupId = 'gate-play-' + (sequence + 1)
  const play = {
    sequence:++sequence, type:'play', playerIndex:actor, text:'测试玩家打出〈事务门闩测试战术〉',
    originZone:'hand', playerLogGroupId:groupId, playerLogTiming:'activation', cards:[{...tactic}],
  }
  const effect = {
    sequence:++sequence, type:'effect-result', playerIndex:actor,
    text:'〈事务门闩测试战术〉效果结算', effectText:'权威回包已到达，但本次事务展示尚未结束。',
    effectSceneId:groupId + ':effect', effectResultStatus:'resolved',
    playerLogGroupId:groupId, playerLogTiming:'activation', cards:[{...tactic}],
  }
  const prompt = {
    promptId:'gate-prompt-' + game.revision, playerIndex:actor, kind:'option',
    text:'继续处理本次权威事务', validChoices:['yes','no'], minChoose:1, maxChoose:1,
    choiceLabels:{ yes:'继续结算', no:'不发动' },
    data:{ uiPattern:'effect-decision', effectText:'该提示属于当前事务的合法续步，不是下一次自由操作。' },
    createdRevision:game.revision + 1, controller:actor,
  }
  return { play, effect, prompt:withPrompt ? prompt : null }
}
function applyPlayResponse(withPrompt = false) {
  const packet = nextEvents(withPrompt)
  const player = game.players[actor]
  player.hand = [followupTactic]
  player.handCount = 1
  player.resolving = [tactic]
  game.prompts = packet.prompt ? [packet.prompt] : []
  game.revision += 1
  game.stateHash = 'free-action-gate-' + game.revision
  game.recentEvents = [...game.recentEvents, packet.play, packet.effect]
  // Synthetic authority-state delivery mirrors only the observable ordering:
  // state/revision/events and request completion become visible in one turn.
  // This does not execute the production inbound decoder or a server ACK.  The
  // RED is still decided by the actual GameBoard render/command boundary and
  // actual net.gameAction, not by a hand-set presentation-busy flag.
  l12State.pendingAction = false
  return { revision:game.revision, sequences:[packet.play.sequence, packet.effect.sequence], promptId:packet.prompt?.promptId || null }
}
function applyDefenseResponse() {
  const packet = nextEvents(false)
  const player = game.players[actor]
  player.hand = [followupTactic]
  player.handCount = 1
  player.resolving = [tactic]
  game.prompts = []
  game.phase = 'Defense'
  game.activePlayer = 1 - actor
  game.pendingDefense = {
    attackerPlayer:1 - actor,
    attackerInstanceId:'gate-unit-' + (1 - actor),
    target:{ type:'master' }, stage:'DefenseChoice', attackValue:3000,
  }
  game.revision += 1
  game.stateHash = 'free-action-gate-' + game.revision
  game.recentEvents = [...game.recentEvents, packet.play, packet.effect]
  l12State.pendingAction = false
  return { revision:game.revision, sequences:[packet.play.sequence, packet.effect.sequence] }
}
function applyPromptResponse() {
  game.prompts = []
  game.revision += 1
  game.stateHash = 'free-action-gate-' + game.revision
  l12State.pendingAction = false
}
function applySpectatorEvent() {
  const shown = game.players[actor].field[0][0]
  const event = {
    sequence:++sequence, type:'effect-result', playerIndex:actor,
    text:'观战权威事务展示', effectText:'观战者应按相同顺序只看到一次事务展示。',
    effectSceneId:'spectator-gate-' + sequence, effectResultStatus:'resolved', cards:[{...shown}],
  }
  game.revision += 1
  game.stateHash = 'free-action-gate-' + game.revision
  game.recentEvents = [...game.recentEvents, event]
  return { revision:game.revision, sequence:event.sequence }
}
function retransmit() {
  game.recentEvents = [...game.recentEvents]
  game.stateHash = String(game.stateHash)
}
function reconnectSameSnapshot() {
  l12State.status = 'connecting'
  l12State.recoveryPhase = 'snapshot-received'
  queueMicrotask(() => {
    game.recentEvents = [...game.recentEvents]
    l12State.status = 'online'
    l12State.recoveryPhase = 'snapshot-acknowledged'
  })
}
function resetMatch() {
  game.matchId = 'free-action-gate-next-match'
  game.revision = 1
  game.recentEvents = []
  game.prompts = []
  game.stateHash = 'free-action-gate-next-1'
  l12State.pendingAction = false
}
function leaveRoom() {
  mounted.value = false
  l12State.pendingAction = false
  l12State.leavingRoom = true
  l12State.game = null
  l12State.socket = null
  observer.disconnect()
}
function onPresentationChange(value) {
  presentationBusy.value = Boolean(value)
  busyTrace.push({ value:Boolean(value), at:performance.now() })
}
function snapshot() {
  return {
    actor, spectator, revision:game.revision, matchId:game.matchId,
    pendingAction:l12State.pendingAction, presentationBusy:presentationBusy.value,
    sent:sent.map(message => ({ type:message.type, command:message.command })),
    busyTrace:[...busyTrace], transientStarts:[...transientStarts],
    activeTransients:document.querySelectorAll('.zone-card-movement,.public-reveal-animation,.l12-card-state-transition-ghost,.l12-combat-defeat-ghost').length,
  }
}
Object.assign(window, { __freeActionGateFixture:{
  applyPlayResponse, applyDefenseResponse, applyPromptResponse, applySpectatorEvent, retransmit,
  reconnectSameSnapshot, resetMatch, leaveRoom, snapshot,
} })

const Root = {
  setup() {
    if (mobile) useLandscapeViewport(ref(true))
    return () => h('div', { class:mobile ? 'l12-landscape-surface' : undefined }, [
      mobile ? h('div', { id:'l12-landscape-teleports' }) : null,
      mounted.value ? h(GameBoard, {
        game, readOnly:spectator, spectatorLiveView:spectator,
        onPresentationChange,
      }) : null,
    ])
  },
}
createApp(Root).mount('#app')
await nextTick()
`

const fixturePlugin = {
  name: 'free-action-presentation-gate-fixture',
  resolveId(id) {
    if (id === '/__free_action_gate_fixture__.js') return '\0free-action-presentation-gate-fixture'
  },
  load(id) {
    if (id === '\0free-action-presentation-gate-fixture') return harnessSource
  },
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      if (request.url?.split('?')[0] === '/__free_action_presentation_gate__') {
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end(harnessHtml)
        return
      }
      if (request.url?.split('?')[0] === '/api/alternate-arts') {
        response.setHeader('Content-Type', 'application/json')
        response.end('[]')
        return
      }
      if (request.url?.split('?')[0]?.startsWith('/api/site/media/')) {
        response.setHeader('Content-Type', 'image/svg+xml')
        response.end('<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#183d49"/><text x="125" y="175" text-anchor="middle" fill="white">QA</text></svg>')
        return
      }
      next()
    })
  },
}

const server = await createServer({
  root: front,
  cacheDir:path.join(output, 'vite-cache'),
  configLoader:'runner',
  plugins:[fixturePlugin],
  server:{ host:'127.0.0.1', port:0 },
  logLevel:'error',
})
function sha256(file) {
  return createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
}
const sourceBindings = Object.fromEntries([
  ['qa', scriptFile],
  ['gameBoard', path.join(front, 'src/l12/game/GameBoard.vue')],
  ['net', path.join(front, 'src/l12/net.ts')],
  ['zoneMovement', path.join(front, 'src/l12/game/ZoneMovementPresentationLayer.vue')],
  ['sequenceCoordinator', path.join(front, 'src/l12/game/presentationSequenceCoordinator.ts')],
  ['visualTransitionProjection', path.join(front, 'src/l12/game/visualTransitionProjection.ts')],
].map(([name, file]) => [name, { path:file, sha256:sha256(file) }]))
const report = {
  contract:'free-action-presentation-gate-20261024',
  expectedCurrentResult:'RED until GameBoard gates free commands on real presentation completion',
  evidenceModel:{
    actual:['GameBoard component rendering and controls','net.gameAction dispatch','ZoneMovement/PublicReveal presentation ownership','Vue render and command-entry boundaries'],
    synthetic:['authority state/revision/event delivery','pendingAction completion','WebSocket send capture','retransmit/reconnect/match-reset/leave triggers'],
    excludedClaims:[
      'No production inbound WebSocket decoder or server ACK is exercised.',
      'No full production race or recovery correctness is established by this focused fixture.',
      'The mobile-sized profiles use pointer clicks, not real touch input.',
      'reduced-motion and spectator orientation with game.you=1 are not covered in this lease.',
      'Passing this focused RED does not establish player/spectator end-to-end ordering parity.',
    ],
  },
  sourceBindings,
  profiles:[], failures:[], checks:[], screenshots:[], assertions:0,
}
const transientSelector = '.zone-card-movement,.public-reveal-animation,.l12-card-state-transition-ghost,.l12-combat-defeat-ghost'
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
let currentProgress = null
function enterStage(stage) {
  if (currentProgress) currentProgress.stage = stage
}
function markCommandPath(name) {
  if (currentProgress) currentProgress.commandPaths[name] = true
}
function recordCheck(passed, message, actual, expected) {
  report.checks.push({
    profile:currentProgress?.profile ?? 'bootstrap', scenario:currentProgress?.scenario ?? 'bootstrap',
    stage:currentProgress?.stage ?? 'bootstrap', passed, message, actual, expected,
  })
  if (currentProgress) currentProgress.lastCheck = message
}
function ok(value, message) {
  const passed = Boolean(value)
  recordCheck(passed, message, value, true)
  assert.ok(value, message)
  report.assertions += 1
}
function equal(actual, expected, message) {
  const passed = Object.is(actual, expected)
  recordCheck(passed, message, actual, expected)
  assert.equal(actual, expected, message)
  report.assertions += 1
}
async function fixture(page, method, ...args) {
  return page.evaluate(({ method, args }) => window.__freeActionGateFixture[method](...args), { method, args })
}
async function snapshot(page) { return fixture(page, 'snapshot') }
async function screenshot(page, name) {
  const target = path.join(output, `${name}.png`)
  await page.screenshot({ path:target, fullPage:true })
  report.screenshots.push(target)
}
async function waitForIdle(page) {
  await page.waitForFunction(selector => {
    const state = window.__freeActionGateFixture.snapshot()
    return document.querySelectorAll(selector).length === 0 && !state.presentationBusy
  }, transientSelector, { timeout:8_000 })
}
async function sendPlayThroughActualControls(page, actor, profile) {
  enterStage('initial-real-command')
  const card = page.locator(`[data-l12-zone="hand"][data-player-index="${actor}"] .card-tile[data-card-instance-id="gate-tactic-${actor}"]`)
  await card.waitFor({ state:'visible' })
  await card.click()
  const play = page.locator(`[data-tutorial-action="play-card"][data-tutorial-card-instance="gate-tactic-${actor}"]`)
  await play.waitFor({ state:'visible' })
  await play.click()
  const state = await snapshot(page)
  equal(state.sent.length, 1, `${profile}: actual hand play control must dispatch exactly one command`)
  equal(state.sent[0].command?.type, 'playCard', `${profile}: first real command must be playCard`)
  equal(state.pendingAction, true, `${profile}: first real command must own pendingAction until the response`)
}
async function inspectFreeAction(page, required = true) {
  const control = page.locator('[data-tutorial-action="end-turn"]')
  if (!await control.count()) {
    if (required) await control.waitFor({ state:'attached' })
    else return { control:null, absent:true, disabled:true }
  }
  await control.waitFor({ state:'attached' })
  return { control, absent:false, disabled:await control.isDisabled() }
}
async function inspectFollowupPlay(page, actor) {
  const card = page.locator(`[data-l12-zone="hand"][data-player-index="${actor}"] .card-tile[data-card-instance-id="gate-followup-${actor}"]`)
  await card.waitFor({ state:'visible' })
  await card.click()
  const action = page.locator(`[data-tutorial-action="play-card"][data-tutorial-card-instance="gate-followup-${actor}"]`)
  return { action, count:await action.count() }
}
async function dispatchClick(control) {
  await control.evaluate(node => node.dispatchEvent(new MouseEvent('click', { bubbles:true, cancelable:true })))
}
async function playerRaceScenario(page, profile) {
  await sendPlayThroughActualControls(page, profile.actor, profile.name)
  enterStage('synthetic-authority-delivery')
  const response = await fixture(page, 'applyPlayResponse', false)
  await page.locator(transientSelector).first().waitFor({ state:'attached', timeout:3_000 })
  const active = await snapshot(page)
  ok(active.presentationBusy, `${profile.name}: real authority response must raise the production presentation signal`)
  equal(active.sent.length, 1, `${profile.name}: response itself cannot synthesize a second command`)

  enterStage('synthetic-retransmit-no-replay')
  await fixture(page, 'retransmit')
  const startsBefore = (await snapshot(page)).transientStarts.length
  await delay(70)
  equal((await snapshot(page)).transientStarts.length, startsBefore,
    `${profile.name}: same-revision retransmit cannot replay the active transaction`)

  enterStage('active-presentation-non-end-turn-ui-gate')
  const followupDuring = await inspectFollowupPlay(page, profile.actor)
  equal(followupDuring.count, 0,
    `${profile.name}: a second playable hand card cannot expose play while presentation is active`)

  enterStage('active-presentation-ui-gate')
  const during = await inspectFreeAction(page)
  ok(during.disabled, `${profile.name}: end-turn must stay visibly disabled while the response presentation is active`)
  enterStage('active-presentation-command-gate')
  markCommandPath('activePresentationFreeCommand')
  await dispatchClick(during.control)
  await delay(40)
  equal((await snapshot(page)).sent.length, 1,
    `${profile.name}: actual GameBoard command entry must reject a second free command before presentation completion`)
  await screenshot(page, `green-${profile.name}-race-gated`)

  enterStage('post-presentation-release')
  await waitForIdle(page)
  const followupAfter = page.locator(`[data-tutorial-action="play-card"][data-tutorial-card-instance="gate-followup-${profile.actor}"]`)
  await followupAfter.waitFor({ state:'visible' })
  equal(await followupAfter.isDisabled(), false,
    `${profile.name}: non-endTurn play must release after the presentation becomes idle`)
  const after = await inspectFreeAction(page)
  equal(after.disabled, false, `${profile.name}: end-turn must release after the presentation becomes idle`)
  await after.control.click()
  await delay(40)
  const released = await snapshot(page)
  equal(released.sent.length, 2, `${profile.name}: exactly one next free command must dispatch after completion`)
  equal(released.sent[1].command?.type, 'endTurn', `${profile.name}: released command must be the clicked endTurn`)
  return { response, busyTrace:released.busyTrace, transientStarts:released.transientStarts }
}
async function playerDefenseContinuationScenario(page, profile) {
  await sendPlayThroughActualControls(page, profile.actor, profile.name + '/defense-continuation')
  enterStage('synthetic-authority-delivery-with-defense-continuation')
  const response = await fixture(page, 'applyDefenseResponse')
  await page.locator(transientSelector).first().waitFor({ state:'attached', timeout:3_000 })
  ok((await snapshot(page)).presentationBusy,
    `${profile.name}: defense continuation fixture must retain active presentation ownership`)
  enterStage('defense-continuation')
  const decline = page.locator('[data-tutorial-action="decline-defense"]')
  await decline.waitFor({ state:'visible' })
  equal(await decline.isDisabled(), false,
    `${profile.name}: authoritative defense continuation must not deadlock behind presentation`)
  await screenshot(page, `green-${profile.name}-defense-continuation`)
  await decline.click()
  await delay(40)
  const continued = await snapshot(page)
  equal(continued.sent.length, 2, `${profile.name}: defense continuation must dispatch exactly once`)
  equal(continued.sent[1].command?.type, 'resolveDefense',
    `${profile.name}: restricted continuation must dispatch resolveDefense`)
  return { response, final:continued }
}
async function playerPromptScenario(page, profile) {
  await sendPlayThroughActualControls(page, profile.actor, profile.name + '/prompt')
  enterStage('synthetic-authority-delivery-with-prompt')
  const response = await fixture(page, 'applyPlayResponse', true)
  const prompt = page.locator(`[data-tutorial-prompt-id="${response.promptId}"]`)
  await prompt.waitFor({ state:'visible' })
  equal(await page.locator(transientSelector).count(), 0,
    `${profile.name}: a prompt-paused future presentation cannot paint underneath the authoritative prompt`)
  ok((await snapshot(page)).presentationBusy,
    `${profile.name}: the paused future reservation must continue to own the transaction gate`)

  enterStage('prompt-paused-ui-gate')
  const unrelated = await inspectFreeAction(page, false)
  ok(unrelated.absent || unrelated.disabled,
    `${profile.name}: unrelated end-turn must be absent or visibly disabled during prompt continuation`)
  if (currentProgress) currentProgress.promptPausedFreeActionSurface = unrelated.absent ? 'absent' : 'disabled'
  if (unrelated.control) {
    enterStage('prompt-paused-command-gate')
    markCommandPath('promptPausedFreeCommand')
    await dispatchClick(unrelated.control)
  }
  equal((await snapshot(page)).sent.length, 1,
    `${profile.name}: prompt state must reject an unrelated free command`)

  enterStage('prompt-own-continuation')
  const continueChoice = prompt.locator('[data-tutorial-choice="yes"]')
  await continueChoice.waitFor({ state:'visible' })
  equal(await continueChoice.isDisabled(), false,
    `${profile.name}: the prompt's own authoritative continuation must remain operable`)
  await continueChoice.click()
  await delay(40)
  const continued = await snapshot(page)
  equal(continued.sent.length, 2, `${profile.name}: prompt continuation must dispatch exactly once`)
  equal(continued.sent[1].command?.type, 'resolvePrompt', `${profile.name}: continuation must be resolvePrompt, not a free command`)

  enterStage('synthetic-prompt-response-and-resume')
  await fixture(page, 'applyPromptResponse')
  await page.locator(transientSelector).first().waitFor({ state:'attached', timeout:3_000 })
  const resumed = await inspectFreeAction(page)
  ok(resumed.disabled, `${profile.name}: free action must remain gated after prompt resolution while presentation resumes`)
  enterStage('resumed-presentation-command-gate')
  markCommandPath('resumedPresentationFreeCommand')
  await dispatchClick(resumed.control)
  equal((await snapshot(page)).sent.length, 2,
    `${profile.name}: resumed presentation must still reject a free command`)
  await screenshot(page, `green-${profile.name}-prompt-resumed-gated`)

  enterStage('post-prompt-transaction-release')
  await waitForIdle(page)
  const released = await inspectFreeAction(page)
  equal(released.disabled, false, `${profile.name}: prompt transaction must release only after its resumed presentation finishes`)
  await released.control.click()
  await delay(40)
  equal((await snapshot(page)).sent.length, 3,
    `${profile.name}: one next free command must dispatch after the prompt transaction completes`)
  return { response, final:await snapshot(page) }
}
async function spectatorScenario(page, profile) {
  enterStage('spectator-action-surface')
  equal(await page.locator('[data-tutorial-action="end-turn"],[data-tutorial-action="play-card"],[data-tutorial-action="attack"]').count(), 0,
    `${profile.name}: spectator must expose no player action surface`)
  enterStage('synthetic-spectator-live-event')
  const event = await fixture(page, 'applySpectatorEvent')
  await page.locator(transientSelector).first().waitFor({ state:'attached', timeout:3_000 })
  const live = await snapshot(page)
  ok(live.presentationBusy, `${profile.name}: spectator live transaction must use the same presentation lifecycle`)
  ok(live.transientStarts.length >= 1, `${profile.name}: spectator must see the live authoritative transaction`)
  await screenshot(page, `green-${profile.name}-spectator-live`)
  const starts = live.transientStarts.length
  enterStage('synthetic-spectator-retransmit')
  await fixture(page, 'retransmit')
  await delay(70)
  equal((await snapshot(page)).transientStarts.length, starts,
    `${profile.name}: spectator same-revision retransmit cannot replay the transaction`)

  enterStage('synthetic-spectator-reconnect')
  await fixture(page, 'reconnectSameSnapshot')
  await page.waitForFunction(() => window.__freeActionGateFixture.snapshot().matchId === 'free-action-gate-match'
    && !window.__freeActionGateFixture.snapshot().pendingAction)
  await delay(100)
  equal((await snapshot(page)).transientStarts.length, starts,
    `${profile.name}: spectator reconnect snapshot cannot replay historical presentation`)

  enterStage('synthetic-match-reset')
  await fixture(page, 'resetMatch')
  await page.waitForFunction(selector => document.querySelectorAll(selector).length === 0, transientSelector)
  const reset = await snapshot(page)
  equal(reset.presentationBusy, false, `${profile.name}: match reset must clear active and queued presentation ownership`)

  enterStage('synthetic-leave-cleanup')
  await fixture(page, 'applySpectatorEvent')
  await page.locator(transientSelector).first().waitFor({ state:'attached', timeout:3_000 })
  await fixture(page, 'leaveRoom')
  await page.waitForFunction(selector => document.querySelectorAll(selector).length === 0, transientSelector)
  equal(await page.locator('[data-l12-game-stage]').count(), 0, `${profile.name}: leave-room must unmount the board`)
  equal((await snapshot(page)).pendingAction, false, `${profile.name}: leave-room must not retain an interaction gate`)
  return { event, reset, final:await snapshot(page) }
}

const identities = [
  { id:'player-0', actor:0, spectator:false },
  { id:'player-1', actor:1, spectator:false },
  { id:'spectator', actor:0, spectator:true },
]
const viewports = [
  { id:'desktop', viewport:{ width:1440, height:900 }, mobile:false },
  { id:'wide-844x390', viewport:{ width:844, height:390 }, mobile:true },
  { id:'portrait-390x844', viewport:{ width:390, height:844 }, mobile:true },
]

let browser
try {
  await server.listen()
  const address = server.httpServer?.address()
  assert.ok(address && typeof address === 'object')
  const base = `http://127.0.0.1:${address.port}/__free_action_presentation_gate__`
  browser = await chromium.launch({ headless:true, channel:'msedge' })

  for (const identity of identities) for (const viewport of viewports) {
    const profile = { ...identity, ...viewport, name:`${identity.id}-${viewport.id}` }
    const scenarios = identity.spectator ? ['spectator']
      : identity.actor === 0 && viewport.id === 'desktop' ? ['race', 'prompt', 'defense-continuation'] : ['race', 'prompt']
    for (const scenario of scenarios) {
      currentProgress = {
        profile:profile.name, scenario, stage:'browser-bootstrap', lastCheck:null,
        commandPaths:{
          activePresentationFreeCommand:false,
          promptPausedFreeCommand:false,
          resumedPresentationFreeCommand:false,
        },
      }
      const context = await browser.newContext({ viewport:viewport.viewport, reducedMotion:'no-preference' })
      const page = await context.newPage()
      const browserErrors = []
      page.setDefaultTimeout(10_000)
      page.on('pageerror', error => browserErrors.push(error.message))
      page.on('console', message => {
        if (message.type() === 'error') browserErrors.push(message.text())
      })
      await page.route('**/*', route => {
        const requestUrl = route.request().url()
        if (requestUrl.startsWith('data:')) { route.continue(); return }
        const url = new URL(requestUrl)
        if (url.pathname === '/api/alternate-arts') {
          route.fulfill({ status:200, contentType:'application/json', body:'[]' })
          return
        }
        if (url.pathname.startsWith('/api/site/media/')) {
          route.fulfill({ status:200, contentType:'image/svg+xml', body:'<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#183d49"/><text x="125" y="175" text-anchor="middle" fill="white">QA</text></svg>' })
          return
        }
        if (['127.0.0.1', 'localhost'].includes(url.hostname)) { route.continue(); return }
        route.abort()
      })
      const query = new URLSearchParams({ actor:String(identity.actor) })
      if (identity.spectator) query.set('spectator', '1')
      if (viewport.mobile) query.set('mobile', '1')
      try {
        await page.goto(`${base}?${query}`, { waitUntil:'domcontentloaded' })
        await page.locator('[data-l12-game-stage]').waitFor({ state:'visible' })
        let detail
        if (scenario === 'race') detail = await playerRaceScenario(page, profile)
        else if (scenario === 'prompt') detail = await playerPromptScenario(page, profile)
        else if (scenario === 'defense-continuation') detail = await playerDefenseContinuationScenario(page, profile)
        else detail = await spectatorScenario(page, profile)
        enterStage('browser-errors')
        equal(browserErrors.length, 0, `${profile.name}/${scenario}: browser errors`)
        report.profiles.push({ profile:profile.name, scenario, passed:true, detail, progress:{ ...currentProgress, commandPaths:{ ...currentProgress.commandPaths } } })
      } catch (error) {
        const name = `red-${profile.name}-${scenario}`
        try { await screenshot(page, name) } catch {}
        const state = await snapshot(page).catch(() => null)
        const progress = { ...currentProgress, commandPaths:{ ...currentProgress.commandPaths } }
        report.failures.push({
          profile:profile.name, scenario, stage:progress.stage, lastCheck:progress.lastCheck,
          commandPaths:progress.commandPaths,
          commandPathDisclosure:progress.stage.endsWith('ui-gate')
            ? 'The UI assertion failed before the following command-path probe; that command path was not executed.'
            : 'See commandPaths for the command probes actually executed before failure.',
          error:String(error), stack:error?.stack, browserErrors, state,
        })
        report.profiles.push({ profile:profile.name, scenario, passed:false, progress })
      } finally {
        await context.close()
        currentProgress = null
      }
    }
  }

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ ...report, passed:report.failures.length === 0 }, null, 2))
  assert.deepEqual(report.failures, [],
    `free-action presentation gate remains RED in ${report.failures.length} scenario(s):\n`
      + report.failures.map(failure => `${failure.profile}/${failure.scenario}: ${failure.error}`).join('\n'))
  console.log(JSON.stringify({ passed:true, assertions:report.assertions, profiles:report.profiles.length }, null, 2))
} catch (error) {
  fs.writeFileSync(path.join(output, 'failure.json'), JSON.stringify({ ...report, passed:false, fatal:String(error), stack:error?.stack }, null, 2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}
