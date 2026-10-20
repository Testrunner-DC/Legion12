import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync, spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { computed, nextTick as vueNextTick, reactive, ref, watch } from 'vue'

const front = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const gamePageSource = fs.readFileSync(path.join(front, 'src/l12/GamePage.vue'), 'utf8').replace(/\r\n/g, '\n')

function actualFunctions(sfc, names, dependencies) {
  const setup = parse(sfc).descriptor.scriptSetup.content
  const tree = ts.createSourceFile('GamePage.setup.ts', setup, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const selected = names.map(name => {
    const node = tree.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
    assert(node, 'Actual GamePage setup function missing: ' + name)
    return node.getText(tree)
  }).join('\n')
  const js = ts.transpileModule(selected, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
    reportDiagnostics: true,
  })
  assert.deepEqual((js.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error), [])
  return Function(...Object.keys(dependencies), js.outputText + '\nreturn {' + names.join(',') + '}')(...Object.values(dependencies))
}

function createActualFixture(renderBoundary = vueNextTick, source = gamePageSource) {
  const l12State = reactive({
    status: 'online',
    game: { matchId: 'match-a', phase: 'Main' },
  })
  const game = computed(() => l12State.game)
  const tutorial = ref(null)
  const tutorialPresentationBusy = ref(false)
  const gameOverMinimized = ref(false)
  const { createGameOverPresentationHandoff } = actualFunctions(
    source,
    ['createGameOverPresentationHandoff'],
    { computed, game, gameOverMinimized, nextTick: renderBoundary, ref, tutorial, tutorialPresentationBusy, watch },
  )
  const handoff = createGameOverPresentationHandoff()
  return { game, gameOverMinimized, handoff, l12State, tutorialPresentationBusy }
}

async function settleVue() {
  await vueNextTick()
  await vueNextTick()
  await vueNextTick()
}

async function verifyActualGamePageContract(source = gamePageSource) {
  assert.match(source, /<GameBoard :key="game\.matchId"/)
  assert.match(source, /@presentation-change="battlePresentationListener"/)

  const fixture = createActualFixture(vueNextTick, source)
  const firstMatchHandler = fixture.handoff.presentationListener.value
  firstMatchHandler(true)
  fixture.l12State.game = { matchId: 'match-a', phase: 'GameOver' }
  await settleVue()
  assert.equal(fixture.handoff.resultReady.value, false, 'actual GamePage must wait while the final presentation is busy')
  firstMatchHandler(false)
  await settleVue()
  assert.equal(fixture.handoff.resultReady.value, true, 'actual GamePage must show the result after the same-match presentation becomes idle')

  firstMatchHandler(true)
  await settleVue()
  assert.equal(fixture.handoff.resultReady.value, true, 'the accepted result must stay latched after unrelated late busy')
  fixture.l12State.status = 'offline'
  await settleVue()
  assert.equal(fixture.handoff.resultReady.value, true, 'temporary disconnect must preserve the submitted result')
  fixture.l12State.status = 'online'
  fixture.l12State.game = { matchId: 'match-a', phase: 'GameOver' }
  await settleVue()
  assert.equal(fixture.handoff.resultReady.value, true, 'same-match reconnect snapshot must preserve the submitted result')

  const gates = []
  const boundaryFixture = createActualFixture(() => new Promise(resolve => gates.push(resolve)), source)
  const boundaryHandler = boundaryFixture.handoff.presentationListener.value
  boundaryFixture.l12State.game = { matchId: 'match-a', phase: 'GameOver' }
  await vueNextTick()
  assert.equal(gates.length, 1, 'actual GamePage must wait at the render boundary before arming')
  assert.equal(boundaryFixture.handoff.resultReady.value, false)
  boundaryHandler(true)
  gates.shift()()
  await settleVue()
  assert.equal(boundaryFixture.handoff.resultReady.value, false, 'same-snapshot GameBoard busy must win before the result latches')
  boundaryHandler(false)
  await settleVue()
  assert.equal(boundaryFixture.handoff.resultReady.value, true)

  const staleGates = []
  const staleFixture = createActualFixture(() => new Promise(resolve => staleGates.push(resolve)), source)
  const oldHandler = staleFixture.handoff.presentationListener.value
  staleFixture.l12State.game = { matchId: 'match-a', phase: 'GameOver' }
  await vueNextTick()
  assert.equal(staleGates.length, 1)
  staleFixture.l12State.game = { matchId: 'match-b', phase: 'Main' }
  await settleVue()
  const newHandler = staleFixture.handoff.presentationListener.value
  assert.notEqual(newHandler, oldHandler, 'a keyed new match must receive a match-bound listener')
  oldHandler(true)
  oldHandler(false)
  staleGates.shift()()
  await settleVue()
  assert.equal(staleFixture.handoff.resultReady.value, false, 'old handler and old render continuation must not arm the new match')
  newHandler(true)
  staleFixture.l12State.game = { matchId: 'match-b', phase: 'GameOver' }
  await vueNextTick()
  assert.equal(staleGates.length, 1)
  staleGates.shift()()
  await settleVue()
  assert.equal(staleFixture.handoff.resultReady.value, false, 'the current match handler must still block its own terminal result')
  newHandler(false)
  await settleVue()
  assert.equal(staleFixture.handoff.resultReady.value, true)

  const busyInterleave = createActualFixture(vueNextTick, source)
  const busyOldHandler = busyInterleave.handoff.presentationListener.value
  busyInterleave.l12State.game = { matchId: 'match-b', phase: 'Main' }
  await settleVue()
  const busyCurrentHandler = busyInterleave.handoff.presentationListener.value
  busyCurrentHandler(true)
  busyInterleave.l12State.game = { matchId: 'match-b', phase: 'GameOver' }
  await settleVue()
  assert.equal(busyInterleave.handoff.resultReady.value, false)
  busyOldHandler(false)
  await settleVue()
  assert.equal(busyInterleave.handoff.resultReady.value, false,
    'old-match idle after the current terminal handoff is armed must not release a busy current match')
  busyCurrentHandler(false)
  await settleVue()
  assert.equal(busyInterleave.handoff.resultReady.value, true)

  const idleGates = []
  const idleInterleave = createActualFixture(() => new Promise(resolve => idleGates.push(resolve)), source)
  const idleOldHandler = idleInterleave.handoff.presentationListener.value
  idleInterleave.l12State.game = { matchId: 'match-b', phase: 'Main' }
  await settleVue()
  const idleCurrentHandler = idleInterleave.handoff.presentationListener.value
  idleInterleave.l12State.game = { matchId: 'match-b', phase: 'GameOver' }
  await vueNextTick()
  assert.equal(idleGates.length, 1)
  idleOldHandler(true)
  idleGates.shift()()
  await settleVue()
  assert.equal(idleInterleave.handoff.resultReady.value, true,
    'old-match busy during the current idle render boundary must not suppress the current result')
  idleCurrentHandler(true)
  await settleVue()
  assert.equal(idleInterleave.handoff.resultReady.value, true, 'the current result remains latched after handoff')
  return true
}

await verifyActualGamePageContract()
const missingSourceGuard = gamePageSource.replace(
  '    if (!matchId || game.value?.matchId !== matchId) return\n',
  '',
)
assert.notEqual(missingSourceGuard, gamePageSource)
await assert.rejects(
  verifyActualGamePageContract(missingSourceGuard),
  /old-match busy during the current idle render boundary/,
  'removing the actual source-identity guard must make the stale busy interleave RED',
)
const reboundOldListener = gamePageSource.replace(
  '    return (busy: boolean) => updatePresentationBusy(matchId, busy)',
  "    return (busy: boolean) => updatePresentationBusy(game.value?.matchId ?? '', busy)",
)
assert.notEqual(reboundOldListener, gamePageSource)
await assert.rejects(
  verifyActualGamePageContract(reboundOldListener),
  /old-match idle after the current terminal handoff is armed/,
  'binding an old listener to the current match must make the stale idle interleave RED',
)
export const gameOverPresentationHandoffContractVerified = true
async function runBrowserContract() {
const require = createRequire(import.meta.url)
const { chromium } = require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/spectator-game-over-presentation-handoff'
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp')
const browserEnv = { ...process.env, L12_QA_OUT: output, L12_WORK_CACHE: cache,
  TEMP: temp, TMP: temp, TMPDIR: temp,
  L12_INITIALIZER: path.join(front, '../ops/windows/Initialize-L12BuildEnvironment.ps1'),
  L12_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') }
execFileSync(browserEnv.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'),
  ['-NoProfile', '-NonInteractive', '-Command',
    '. $env:L12_INITIALIZER -CacheRoot $env:L12_WORK_CACHE | Out-Null; . $env:L12_GUARD; '
      + '@($env:L12_QA_OUT,$env:L12_WORK_CACHE,$env:TEMP) | ForEach-Object { '
      + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "spectator GameOver browser contract" }'],
  { stdio: 'pipe', env: browserEnv })
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(temp, { recursive: true })
Object.assign(process.env, { TEMP: temp, TMP: temp, TMPDIR: temp })
const port = 5198
const server = spawn(process.execPath, [path.join(front, 'scripts/preview-l12-battle-layout.mjs'), '--port', String(port)], {
  cwd: front, env: { ...browserEnv, L12_PREVIEW_PORT: String(port) }, stdio: ['ignore', 'pipe', 'pipe'],
})
let serverLog = ''
server.stdout.on('data', chunk => { serverLog += chunk })
server.stderr.on('data', chunk => { serverLog += chunk })
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
async function waitServer() {
  for (let index = 0; index < 200; index++) {
    if (server.exitCode !== null) throw new Error('preview server exited: ' + server.exitCode + '\n' + serverLog)
    try { if ((await fetch('http://127.0.0.1:' + port + '/__l12_battle_preview__')).ok) return } catch {}
    await delay(100)
  }
  throw new Error('preview server did not start\n' + serverLog)
}
let browser
let report = { status: 'running' }
try {
  await waitServer()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 916, height: 891 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const origin = 'http://127.0.0.1:' + port + '/__l12_battle_preview__'

  await page.goto(origin + '?canvas=1&death=combat', { waitUntil: 'domcontentloaded' })
  await page.locator('.l12-combat-defeat-ghost').waitFor()
  await page.evaluate(() => {
    const fixture = window.__battleDockFixture
    const shown = fixture.state.game.players[0].field.flat().find(Boolean)
    const sequence = Math.max(0, ...fixture.state.game.recentEvents.map(event => event.sequence || 0)) + 1
    fixture.state.game.recentEvents = [...fixture.state.game.recentEvents,
      { sequence, type: 'reveal', playerIndex: 1, text: '对方展示〈' + shown.name + '〉', cards: [shown] }]
    fixture.state.game.phase = 'GameOver'
    fixture.state.game.winner = 0
    fixture.state.game.winnerReason = 'HTSeed的主宰因天羽的进攻血量降至0'
    fixture.state.game.revision = (Number(fixture.state.game.revision) || 0) + 1
  })
  await page.locator('.public-reveal-animation').waitFor()
  await page.waitForTimeout(35)
  const duringFinalPresentation = await page.evaluate(() => ({
    resultCount: document.querySelectorAll('[data-ui-contract="manual-game-over-exit"]').length,
    routeVisible: [...document.querySelectorAll('.battle-route-controls')].some(node => node.getClientRects().length),
    combatGhosts: document.querySelectorAll('.l12-combat-defeat-ghost').length,
    publicReveals: document.querySelectorAll('.public-reveal-animation').length,
  }))
  await page.screenshot({ path: path.join(output, 'green-result-waits-for-final-presentation.png'), fullPage: true })

  await page.waitForFunction(() => document.querySelectorAll('.l12-combat-defeat-ghost,.public-reveal-animation,.zone-card-movement').length === 0,
    null, { timeout: 5000 })
  const result = page.locator('[data-ui-contract="manual-game-over-exit"]')
  await result.waitFor()
  const afterHandoff = await page.evaluate(() => ({
    resultCount: document.querySelectorAll('[data-ui-contract="manual-game-over-exit"]').length,
    routeVisible: [...document.querySelectorAll('.battle-route-controls')].some(node => node.getClientRects().length),
    transients: document.querySelectorAll('.l12-combat-defeat-ghost,.public-reveal-animation,.zone-card-movement').length,
  }))
  await page.screenshot({ path: path.join(output, 'green-result-after-presentation-handoff.png'), fullPage: true })
  await page.setViewportSize({ width: 568, height: 320 })
  await page.goto(origin + '?canvas=1&mobile=1&game-over=win', { waitUntil: 'domcontentloaded' })
  const mobileResult = page.locator('[data-ui-contract="manual-game-over-exit"]')
  await mobileResult.waitFor()
  await mobileResult.getByRole('button', { name: '最小化对局结果' }).click()
  await mobileResult.waitFor({ state: 'detached' })
  const minimized = {
    resultCount: await page.locator('[data-ui-contract="manual-game-over-exit"]').count(),
    restoreCount: await page.getByRole('button', { name: '恢复对局结果' }).count(),
    routeVisible: await page.locator('.battle-route-controls:visible').count(),
  }
  await page.getByRole('button', { name: '恢复对局结果' }).click()
  const restored = await page.locator('[data-ui-contract="manual-game-over-exit"]').count()
  await page.setViewportSize({ width: 916, height: 891 })

  await page.goto(origin + '?canvas=1&game-over=win', { waitUntil: 'domcontentloaded' })
  await page.locator('[data-ui-contract="manual-game-over-exit"]').waitFor()
  const baselineGameOver = {
    resultCount: await page.locator('[data-ui-contract="manual-game-over-exit"]').count(),
    transients: await page.locator('.l12-combat-defeat-ghost,.public-reveal-animation,.zone-card-movement').count(),
  }

  await page.goto(origin + '?canvas=1', { waitUntil: 'domcontentloaded' })
  await page.waitForFunction(() => Boolean(window.__osirisFixture?.start))
  await page.evaluate(() => window.__osirisFixture.start({ gameOver: true }))
  await page.locator('.osiris-victory-sequence').waitFor()
  const duringOsiris = {
    osirisCount: await page.locator('.osiris-victory-sequence').count(),
    resultCount: await page.locator('[data-ui-contract="manual-game-over-exit"]').count(),
  }
  await page.locator('.osiris-victory-sequence').waitFor({ state: 'detached', timeout: 10000 })
  await page.locator('[data-ui-contract="manual-game-over-exit"]').waitFor()
  const afterOsiris = { resultCount: await page.locator('[data-ui-contract="manual-game-over-exit"]').count() }

  Object.assign(report, { errors, duringFinalPresentation, afterHandoff, minimized, restored,
    baselineGameOver, duringOsiris, afterOsiris })
  assert.deepEqual(errors, [])
  assert.equal(duringFinalPresentation.combatGhosts > 0, true)
  assert.equal(duringFinalPresentation.publicReveals > 0, true)
  assert.equal(duringFinalPresentation.resultCount, 0, 'result must wait for the final authoritative presentation')
  assert.equal(duringFinalPresentation.routeVisible, true, 'existing return route must remain reachable during handoff')
  assert.deepEqual(afterHandoff, { resultCount: 1, routeVisible: false, transients: 0 })
  assert.deepEqual(minimized, { resultCount: 0, restoreCount: 1, routeVisible: 1 })
  assert.equal(restored, 1)
  assert.deepEqual(baselineGameOver, { resultCount: 1, transients: 0 })
  assert.deepEqual(duringOsiris, { osirisCount: 1, resultCount: 0 })
  assert.deepEqual(afterOsiris, { resultCount: 1 })
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'
  report.error = error instanceof Error ? { name: error.name, message: error.message, stack: error.stack } : String(error)
  throw error
} finally {
  await browser?.close()
  if (server.exitCode === null) {
    await new Promise(resolve => {
      const force = setTimeout(() => {
        if (server.exitCode === null) server.kill('SIGKILL')
        resolve()
      }, 3000)
      server.once('exit', () => { clearTimeout(force); resolve() })
      server.kill('SIGTERM')
    })
  }
  report.serverLog = serverLog
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
}
}
if (process.argv.includes('--browser')) await runBrowserContract()
