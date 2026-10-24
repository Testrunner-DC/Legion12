import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const scriptFile = fileURLToPath(import.meta.url)
const front = path.resolve(path.dirname(scriptFile), '..')
const worktree = path.resolve(front, '..')
const output = process.env.L12_PUBLIC_REVEAL_OUT
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp', 'public-reveal-continuous-budget-20261025')

if (!output || !path.isAbsolute(output))
  throw new Error('L12_PUBLIC_REVEAL_OUT must be an absolute controlled path')
if (!path.isAbsolute(cache)) throw new Error('L12_WORK_CACHE must be an absolute controlled path')
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite evidence: ${output}`)

const guardEnv = {
  ...process.env,
  L12_PUBLIC_REVEAL_OUT:output,
  L12_WORK_CACHE:cache,
  TEMP:temp,
  TMP:temp,
  TMPDIR:temp,
  L12_INITIALIZER:path.join(worktree, 'ops/windows/Initialize-L12BuildEnvironment.ps1'),
  L12_GUARD:path.join(worktree, 'ops/windows/L12GeneratedStorage.ps1'),
}
execFileSync(guardEnv.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'), [
  '-NoProfile', '-NonInteractive', '-Command',
  '& $env:L12_INITIALIZER -CacheRoot $env:L12_WORK_CACHE | Out-Null; . $env:L12_GUARD; '
    + '@($env:L12_PUBLIC_REVEAL_OUT,$env:L12_WORK_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "public reveal continuous budget QA" }',
], { stdio:'pipe', env:guardEnv })
fs.mkdirSync(output, { recursive:true })
fs.mkdirSync(temp, { recursive:true })
Object.assign(process.env, { TEMP:temp, TMP:temp, TMPDIR:temp })

const sha256 = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
const fixture = path.join(front, 'scripts/fixtures/BattleVisualTransitionHarness.vue')
const boundFiles = {
  qa:scriptFile,
  fixture,
  gameBoard:path.join(front, 'src/l12/game/GameBoard.vue'),
  publicRevealPresentation:path.join(front, 'src/l12/game/publicRevealPresentation.ts'),
  sequenceCoordinator:path.join(front, 'src/l12/game/presentationSequenceCoordinator.ts'),
  visualProjection:path.join(front, 'src/l12/game/visualTransitionProjection.ts'),
  audioPreferences:path.join(front, 'src/l12/audioPreferences.ts'),
  packageLock:path.join(front, 'package-lock.json'),
}
const bindSources = () => Object.fromEntries(Object.entries(boundFiles)
  .map(([name, file]) => [name, { path:file, sha256:sha256(file), bytes:fs.statSync(file).size }]))
const sourcesBefore = bindSources()

const marker = 'const api = {'
const injection = String.raw`
const continuousRevealCard = card('continuous-reveal-card', '连续公开军团', 'S01-0212', false, faceUrl('S01-0212'))
const continuousOtherCard = card('continuous-other-card', '独立效果来源', 'S02-0604', false, faceUrl('S02-0604'))
const continuousAbilityId = 'S01-0419:ability:play:97db2728de7886de'
function continuousRevealEvent(): Omit<ActionEvent, 'sequence'> {
  return {
    type:'reveal', playerIndex:1,
    text:'花魁的馈赠展示〈连续公开军团〉并加入手牌',
    effectText:'花魁的馈赠展示〈连续公开军团〉并加入手牌',
    effectAbilityId:continuousAbilityId,
    effectSceneId:continuousAbilityId + ':presentation:reveal-add',
    cards:[continuousRevealCard],
  }
}
function continuousSearchEvent(): Omit<ActionEvent, 'sequence'> {
  return {
    type:'search', playerIndex:1,
    text:'花魁的馈赠将〈连续公开军团〉加入手牌',
    effectText:'花魁的馈赠将〈连续公开军团〉加入手牌',
    effectAbilityId:continuousAbilityId,
    effectSceneId:continuousAbilityId + ':presentation:search-add',
    cards:[continuousRevealCard],
  }
}
const api = {
  continuousPairSameBatch() { publishBatch([continuousRevealEvent(), continuousSearchEvent()]) },
  continuousInterleavedReservation() {
    publishBatch([continuousRevealEvent(), {
      type:'effect-result', playerIndex:1, text:'独立效果完成', effectText:'独立效果完成',
      effectSceneId:'qa:independent:presentation:result', effectResultStatus:'resolved', cards:[continuousOtherCard],
    }, continuousSearchEvent()])
  },
  continuousParentOnly() { publishBatch([continuousRevealEvent()]) },
  continuousChildOnly() { publishBatch([continuousSearchEvent()]) },
  continuousPromptParent() {
    game.prompts = [prompt('continuous-public-reveal-block')]
    publishBatch([continuousRevealEvent()])
  },
  continuousPromptChild() { publishBatch([continuousSearchEvent()]) },
  continuousClosePrompt() { game.prompts = []; game.revision += 1 },
  continuousBeginRecovery() {
    l12State.status = 'connecting'
    l12State.recoveryPhase = 'snapshot-received'
  },
  continuousRecoverHistoricalPair() {
    const historicalEvents = [continuousRevealEvent(), continuousSearchEvent()]
      .map(event => ({ ...event, sequence:++sequence }))
    game.recentEvents = [...(game.recentEvents ?? []), ...historicalEvents]
    game.revision += 1
    game.stateHash = 'visual-' + game.revision
  },
  continuousFinishRecovery() {
    l12State.status = 'online'
    l12State.recoveryPhase = 'snapshot-acknowledged'
  },
  continuousEvidence() {
    return JSON.parse(JSON.stringify({ matchId:game.matchId, revision:game.revision, recentEvents:game.recentEvents }))
  },
`
const fixturePlugin = {
  name:'public-reveal-continuous-budget-fixture',
  enforce:'pre',
  transform(source, id) {
    if (id.split('?')[0].replaceAll('\\', '/') !== fixture.replaceAll('\\', '/')) return null
    const count = source.split(marker).length - 1
    if (count !== 1) throw new Error(`Harness API marker count changed: ${count}`)
    return { code:source.replace(marker, injection), map:null }
  },
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const url = request.url?.split('?')[0] ?? ''
      if (url === '/api/site/media/visual-transition/S01-0212.png'
        || url === '/api/site/media/visual-transition/S02-0604.png') {
        response.setHeader('Content-Type', 'image/svg+xml')
        response.end('<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#35162e"/><rect x="12" y="12" width="226" height="326" fill="none" stroke="#d5bc70" stroke-width="8"/><text x="125" y="175" text-anchor="middle" fill="#fff2c7" font-size="22">CONTINUOUS</text></svg>')
        return
      }
      next()
    })
    server.middlewares.use('/__public-reveal-continuous-budget', async (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(await server.transformIndexHtml('/__public-reveal-continuous-budget', harnessHtml))
    })
  },
}
const harnessHtml = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";createApp(Harness).mount("#app")</script></body></html>'

const report = {
  schema:1,
  contract:'public-reveal-continuous-budget-20261025',
  sources:{ before:sourcesBefore, after:null },
  checks:[],
  scenarios:[],
  errors:[],
  cleanup:{ browserClosed:false, serverClosed:false, cacheRemoved:false },
}
function record(passed, scenario, message, actual, expected) {
  const check = { passed:Boolean(passed), scenario, message, actual, expected }
  report.checks.push(check)
  if (!passed) report.errors.push(`${scenario}: ${message}`)
  return Boolean(passed)
}
const equal = (scenario, actual, expected, message) => record(Object.is(actual, expected), scenario, message, actual, expected)
const ok = (scenario, value, message, actual = value, expected = true) => record(Boolean(value), scenario, message, actual, expected)

const server = await createServer({
  root:front,
  cacheDir:path.join(cache, 'vite', 'public-reveal-continuous-budget-20261025'),
  configLoader:'runner',
  plugins:[fixturePlugin],
  server:{ host:'127.0.0.1', port:0 },
  logLevel:'error',
})
let browser
let port

async function openScenario(name, animation = 'off', spectator = false) {
  const context = await browser.newContext({ viewport:{ width:1366, height:844 } })
  await context.addInitScript(value => {
    localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ animation:value }))
  }, animation)
  const page = await context.newPage()
  page.on('pageerror', error => report.errors.push(`${name}: pageerror ${error.message}`))
  const query = spectator ? '?viewer=0&spectator=1' : '?viewer=0&interactive=1'
  await page.goto(`http://127.0.0.1:${port}/__public-reveal-continuous-budget${query}`, { waitUntil:'networkidle' })
  await page.locator('[data-l12-game-stage]').waitFor()
  await page.evaluate(() => {
    window.__continuousMounts = []
    const seen = new WeakSet()
    const scan = () => document.querySelectorAll('.public-reveal-animation').forEach(node => {
      if (seen.has(node)) return
      seen.add(node)
      window.__continuousMounts.push({
        at:performance.now(),
        text:node.querySelector('strong')?.textContent ?? '',
        alt:node.querySelector('img')?.getAttribute('alt') ?? '',
        kind:node.getAttribute('data-presentation-kind'),
      })
    })
    window.__continuousObserver = new MutationObserver(scan)
    window.__continuousObserver.observe(document.body, { childList:true, subtree:true })
    scan()
  })
  return { context, page }
}
async function mounts(page) { return page.evaluate(() => [...window.__continuousMounts]) }
async function waitForMounts(page, count, timeout = 6000) {
  await page.waitForFunction(expected => window.__continuousMounts.length >= expected, count, { timeout })
}
async function waitForIdle(page, timeout = 9000) {
  await page.waitForFunction(() => !document.querySelector('.public-reveal-animation'), null, { timeout })
}
async function closeScenario(handle) {
  await handle.page.evaluate(() => window.__continuousObserver?.disconnect()).catch(() => {})
  await handle.context.close()
}

async function sameBatchScenario(animation, spectator = false) {
  const name = `${spectator ? 'spectator-' : ''}same-batch-${animation}`
  const expectedBudget = animation === 'standard' ? 3000 : animation === 'fast' ? 1650 : 700
  const handle = await openScenario(name, animation, spectator)
  try {
    const started = Date.now()
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousPairSameBatch())
    await waitForMounts(handle.page, 1)
    const first = (await mounts(handle.page))[0]
    ok(name, first.text.includes('展示〈连续公开军团〉') && first.text.includes('将〈连续公开军团〉加入手牌'),
      'one shared DOM keeps both authoritative texts', first.text, 'both texts')
    ok(name, first.text.includes('\n') && !first.text.includes('\\n'),
      'the two authoritative texts use a real rendered line break', JSON.stringify(first.text), 'newline without literal \\n')
    equal(name, first.alt, '连续公开军团', 'the shared presentation keeps the public card face')
    equal(name, first.kind, 'full-card', 'the merged parent retains full-card presentation semantics')
    await waitForIdle(handle.page, expectedBudget + 1100)
    const elapsed = Date.now() - started
    const finalMounts = await mounts(handle.page)
    equal(name, finalMounts.length, 1, 'the proven same-batch pair mounts one presentation')
    ok(name, elapsed >= Math.max(450, expectedBudget - 260), 'the shared presentation keeps a readable full budget', elapsed, `>=${Math.max(450, expectedBudget - 260)}`)
    ok(name, elapsed < expectedBudget + 1050, 'the child does not add a second full budget', elapsed, `<${expectedBudget + 1050}`)
    const beforeRetransmit = finalMounts.length
    await handle.page.evaluate(() => window.__visualTransitionHarness.rematerializeCurrentSnapshot())
    await handle.page.waitForTimeout(900)
    equal(name, (await mounts(handle.page)).length, beforeRetransmit,
      'same-sequence retransmission does not replay the shared presentation')
    report.scenarios.push({ name, elapsed, mounts:finalMounts })
  } finally { await closeScenario(handle) }
}

async function interleavedScenario() {
  const name = 'interleaved-independent-reservation'
  const handle = await openScenario(name)
  try {
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousInterleavedReservation())
    await waitForMounts(handle.page, 3, 5000)
    await waitForIdle(handle.page, 5000)
    const trace = await mounts(handle.page)
    equal(name, trace.length, 3, 'an unrelated reservation prevents pair folding across it')
    equal(name, trace[0].alt, '连续公开军团', 'the parent remains first')
    equal(name, trace[1].alt, '独立效果来源', 'the unrelated authority presentation keeps its sequence')
    equal(name, trace[2].alt, '连续公开军团', 'the unproven child falls back to a full independent card')
    report.scenarios.push({ name, mounts:trace })
  } finally { await closeScenario(handle) }
}

async function lateScenario(animation = 'off') {
  const name = `late-child-separate-snapshot-${animation}`
  const expectedFull = animation === 'standard' ? 3000 : animation === 'fast' ? 1650 : 700
  const expectedTail = animation === 'standard' ? 900 : animation === 'fast' ? 495 : 260
  const handle = await openScenario(name, animation)
  try {
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousParentOnly())
    await waitForMounts(handle.page, 1)
    await waitForIdle(handle.page, expectedFull + 1100)
    const childStarted = Date.now()
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousChildOnly())
    await waitForMounts(handle.page, 2)
    await waitForIdle(handle.page, 2600)
    const childElapsed = Date.now() - childStarted
    const trace = await mounts(handle.page)
    equal(name, trace.length, 2, 'a late child cannot retroactively join a released parent')
    equal(name, trace[1].alt, '', 'the proven late child retains its prior text-only fallback')
    ok(name, childElapsed >= Math.max(180, expectedTail - 180),
      'the text-only child keeps a readable short-tail budget', childElapsed, `>=${Math.max(180, expectedTail - 180)}`)
    ok(name, childElapsed < expectedTail + 700,
      'the text-only child does not consume another full-card budget', childElapsed, `<${expectedTail + 700}`)
    report.scenarios.push({ name, childElapsed, mounts:trace })
  } finally { await closeScenario(handle) }
}

async function promptBoundaryScenario() {
  const name = 'prompt-boundary-separate-snapshots'
  const handle = await openScenario(name)
  try {
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousPromptParent())
    await handle.page.waitForTimeout(180)
    equal(name, (await mounts(handle.page)).length, 0, 'the visible real prompt pauses the not-started parent')
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousPromptChild())
    await handle.page.waitForTimeout(180)
    equal(name, (await mounts(handle.page)).length, 0, 'the prompt also pauses the later child snapshot')
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousClosePrompt())
    await waitForMounts(handle.page, 2, 5000)
    await waitForIdle(handle.page, 5000)
    const trace = await mounts(handle.page)
    equal(name, trace.length, 2, 'events separated by a real interaction boundary do not fold')
    equal(name, trace[1].alt, '', 'the proven post-boundary child remains text-only without sharing a lease')
    report.scenarios.push({ name, mounts:trace })
  } finally { await closeScenario(handle) }
}

async function recoveryScenario() {
  const name = 'recovery-baseline-does-not-replay'
  const handle = await openScenario(name)
  try {
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousBeginRecovery())
    await handle.page.waitForTimeout(100)
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousRecoverHistoricalPair())
    await handle.page.waitForTimeout(100)
    await handle.page.evaluate(() => window.__visualTransitionHarness.continuousFinishRecovery())
    await handle.page.waitForTimeout(1800)
    equal(name, (await mounts(handle.page)).length, 0, 'recovery establishes a baseline without replaying historical pair events')
    report.scenarios.push({ name, mounts:await mounts(handle.page) })
  } finally { await closeScenario(handle) }
}

let runError
try {
  await server.listen()
  const address = server.httpServer.address()
  if (!address || typeof address === 'string') throw new Error('QA server did not expose a TCP port')
  port = address.port
  browser = await chromium.launch({ channel:process.env.L12_BROWSER_CHANNEL || 'msedge', headless:true })
  report.browserVersion = browser.version()
  const focusedTailAndRecovery = process.argv.includes('--tail-and-recovery')
  if (focusedTailAndRecovery) {
    for (const animation of ['standard', 'fast', 'off']) await lateScenario(animation)
    await recoveryScenario()
  } else {
    for (const animation of ['standard', 'fast', 'off']) await sameBatchScenario(animation)
    await sameBatchScenario('off', true)
    await interleavedScenario()
    await lateScenario()
    await promptBoundaryScenario()
    await recoveryScenario()
  }
  assert.equal(report.errors.length, 0, report.errors.join('\n'))
} catch (error) {
  runError = error
  report.errors.push(error instanceof Error ? `${error.name}: ${error.message}` : String(error))
} finally {
  if (browser) {
    await browser.close().catch(error => report.errors.push(`browser close: ${error.message}`))
    report.cleanup.browserClosed = true
  }
  await server.close().catch(error => report.errors.push(`server close: ${error.message}`))
  report.cleanup.serverClosed = true
  report.sources.after = bindSources()
  equal('source-pin', JSON.stringify(report.sources.after), JSON.stringify(report.sources.before),
    'the actual SFC run does not mutate any bound dependency source')
  const viteCache = path.join(cache, 'vite', 'public-reveal-continuous-budget-20261025')
  fs.rmSync(viteCache, { recursive:true, force:true })
  report.cleanup.cacheRemoved = !fs.existsSync(viteCache)
  fs.writeFileSync(path.join(output, 'result.json'), `${JSON.stringify(report, null, 2)}\n`)
}

if (runError) throw runError
console.log(JSON.stringify({ contract:report.contract, checks:report.checks.length, scenarios:report.scenarios.length }))
