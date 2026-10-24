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
const output = process.env.L12_LEGION_ENTRY_OUT
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp', 'legion-entry-presentation-20261024')
const singleOwnerOnly = process.env.L12_SINGLE_OWNER_ONLY === '1'
const frameTimelineOnly = process.env.L12_FRAME_TIMELINE_ONLY === '1'
const naturalFrameTimelineOnly = process.env.L12_NATURAL_FRAME_TIMELINE_ONLY === '1'

if (!output || !path.isAbsolute(output)) throw new Error('L12_LEGION_ENTRY_OUT must be an absolute controlled path')
if (!path.isAbsolute(cache)) throw new Error('L12_WORK_CACHE must be an absolute controlled path')
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite evidence: ${output}`)

// The repository initializer is a script with a param block, so invoke it with
// &, then dot-source only the generated-storage helpers. This verifies every
// selected output root after resolving ancestor junctions before any mkdir.
const guardEnv = {
  ...process.env,
  L12_LEGION_ENTRY_OUT:output,
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
    + '@($env:L12_LEGION_ENTRY_OUT,$env:L12_WORK_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "legion entry presentation QA" }',
], { stdio:'pipe', env:guardEnv })
fs.mkdirSync(output, { recursive:true })
fs.mkdirSync(temp, { recursive:true })
Object.assign(process.env, { TEMP:temp, TMP:temp, TMPDIR:temp })

const sha256 = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
const boundFiles = {
  qa:scriptFile,
  fixture:path.join(front, 'scripts/fixtures/BattleVisualTransitionHarness.vue'),
  gameBoard:path.join(front, 'src/l12/game/GameBoard.vue'),
  zoneMovement:path.join(front, 'src/l12/game/ZoneMovementPresentationLayer.vue'),
  sequenceCoordinator:path.join(front, 'src/l12/game/presentationSequenceCoordinator.ts'),
  visualTransitionProjection:path.join(front, 'src/l12/game/visualTransitionProjection.ts'),
  cardStateTransition:path.join(front, 'src/l12/game/CardStateTransitionLayer.vue'),
  authoritativeVisibility:path.join(front, 'src/l12/game/authoritativeCardVisibility.ts'),
  mobileViewport:path.join(front, 'src/l12/mobileViewport.ts'),
  audioPreferences:path.join(front, 'src/l12/audioPreferences.ts'),
}
const bindSources = () => Object.fromEntries(Object.entries(boundFiles)
  .map(([name, file]) => [name, { path:file, sha256:sha256(file), bytes:fs.statSync(file).size }]))
const sourcesBefore = bindSources()

const report = {
  schema:1,
  contract:'legion-entry-presentation-20261024',
  expectedCurrentResult:'RED: the current direct zone flight has no authoritative center-focus phase or troop-scaled landing impact',
  leaseBaseline:{ fixtureSha256:'FCC3972BD321F7E530B872B39D4A526DFBA434751C8801029A8B1EFE95C344A8' },
  evidenceModel:{
    actual:[
      'GameBoard and ZoneMovementPresentationLayer rendering',
      'presentationChange busy lifecycle and accepted free-action UI gate',
      'presentationSequenceCoordinator ordering and authority visibility leases',
      'WAAPI/CSS animation frame geometry sampled at explicit progress values',
    ],
    synthetic:[
      'authority state, revision, event, retransmit, match-switch and image-delay delivery',
      'test card art and troop values',
    ],
    excludedClaims:[
      'No production inbound WebSocket decoder or server ACK is exercised.',
      'No real network recovery path is exercised.',
      'Mobile contexts use Playwright touch emulation; this is not physical-device touch evidence.',
      'This focused RED does not establish complete production player/spectator end-to-end parity.',
    ],
  },
  sources:{ before:sourcesBefore, after:null },
  plannedMatrix:{
    core:'viewer=1 self, viewer=0 projected opponent, spectator; each at desktop, mobile landscape and rotated portrait',
    boundaries:'preparation, reduced motion, animation off, retransmit, later revision, active match switch and next-match live event',
  },
  checks:[], profiles:[], failures:[], screenshots:[], resourceDiagnostics:[], assertions:0,
  cleanup:{ browserClosed:false, serverClosed:false },
}
let current = { profile:'bootstrap', scenario:'bootstrap', stage:'bootstrap' }
function stage(value) { current.stage = value }
function record(passed, message, actual, expected) {
  const check = { ...current, passed:Boolean(passed), message, actual, expected }
  report.checks.push(check)
  if (passed) report.assertions += 1
  else report.failures.push(check)
  return Boolean(passed)
}
const equal = (actual, expected, message) => record(Object.is(actual, expected), message, actual, expected)
const ok = (value, message, actual = value, expected = true) => record(Boolean(value), message, actual, expected)
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))

const harnessHtml = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";createApp(Harness).mount("#app")</script></body></html>'
function fixtureSvg(label) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#10262e"/><rect x="12" y="12" width="226" height="326" rx="8" fill="none" stroke="#d5bc70" stroke-width="8"/><text x="125" y="168" text-anchor="middle" fill="#fff2c7" font-size="24">${label}</text><text x="125" y="205" text-anchor="middle" fill="#8fd4df" font-size="18">ENTRY QA</text></svg>`
}
const fixturePlugin = {
  name:'legion-entry-presentation-fixture',
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const url = request.url?.split('?')[0] ?? ''
      if (url === '/api/alternate-arts') {
        response.setHeader('Content-Type', 'application/json')
        response.end('[]')
        return
      }
      const match = url.match(/^\/api\/site\/media\/visual-transition\/([^/]+)\.png$/)
      if (match) {
        response.setHeader('Content-Type', 'image/svg+xml')
        response.end(fixtureSvg(match[1]))
        return
      }
      next()
    })
    server.middlewares.use('/__legion-entry-presentation', async (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(await server.transformIndexHtml('/__legion-entry-presentation', harnessHtml))
    })
  },
}

const server = await createServer({
  root:front,
  cacheDir:path.join(output, 'vite-cache'),
  configLoader:'runner',
  plugins:[fixturePlugin],
  server:{ host:'127.0.0.1', port:0 },
  logLevel:'error',
})
let browser
let port

const coreProfiles = [
  { name:'self-desktop', viewer:1, interactive:true, viewport:{ width:1366, height:768 } },
  { name:'opponent-desktop', viewer:0, interactive:true, privateHand:true, viewport:{ width:1366, height:768 } },
  { name:'spectator-desktop', viewer:0, spectator:true, viewport:{ width:1366, height:768 } },
  { name:'self-mobile-wide', viewer:1, interactive:true, landscape:true, mobile:true, viewport:{ width:844, height:390 } },
  { name:'opponent-mobile-wide', viewer:0, interactive:true, privateHand:true, landscape:true, mobile:true, viewport:{ width:844, height:390 } },
  { name:'spectator-mobile-wide', viewer:0, spectator:true, landscape:true, mobile:true, viewport:{ width:844, height:390 } },
  { name:'self-mobile-portrait', viewer:1, interactive:true, landscape:true, mobile:true, viewport:{ width:390, height:844 } },
  { name:'opponent-mobile-portrait', viewer:0, interactive:true, privateHand:true, landscape:true, mobile:true, viewport:{ width:390, height:844 } },
  { name:'spectator-mobile-portrait', viewer:0, spectator:true, landscape:true, mobile:true, viewport:{ width:390, height:844 } },
]

function pageUrl(profile) {
  const params = new URLSearchParams({ viewer:String(profile.viewer ?? 0) })
  if (profile.interactive) params.set('interactive', '1')
  if (profile.privateHand) params.set('private-hand', '1')
  if (profile.spectator) params.set('spectator', '1')
  if (profile.landscape) params.set('landscape', '1')
  return `http://127.0.0.1:${port}/__legion-entry-presentation?${params}`
}
async function openProfile(profile, options = {}) {
  const context = await browser.newContext({
    viewport:profile.viewport,
    isMobile:Boolean(profile.mobile),
    hasTouch:Boolean(profile.mobile),
    reducedMotion:options.reducedMotion ?? 'no-preference',
  })
  const page = await context.newPage()
  const errors = []
  const resourceDiagnostics = { profile:profile.name, consoleErrors:[], requestFailures:[] }
  report.resourceDiagnostics.push(resourceDiagnostics)
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error') resourceDiagnostics.consoleErrors.push(message.text())
  })
  page.on('requestfailed', request => resourceDiagnostics.requestFailures.push({
    url:request.url(),
    errorText:request.failure()?.errorText ?? 'unknown request failure',
  }))
  const animation = options.animation ?? 'standard'
  await page.addInitScript(value => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({
    animation:value, mobileLayout:'auto', cardSize:'medium', fontSize:'standard',
  })), animation)
  await page.goto(pageUrl(profile), { waitUntil:'networkidle' })
  await page.waitForSelector('[data-l12-game-stage]')
  return { context, page, errors }
}
async function invoke(page, method, ...args) {
  return page.evaluate(({ method, args }) => window.__visualTransitionHarness[method](...args), { method, args })
}
async function presentation(page) { return invoke(page, 'presentationSnapshot') }
function movementSelector(instanceId) {
  return `.l12-zone-flight-ghost[data-movement-instance-id="${instanceId}"],.zone-card-movement[data-movement-instance-id="${instanceId}"]`
}
async function waitForVisibleMovement(page, instanceId, timeout = 5_000) {
  await page.waitForFunction(selector => {
    const root = document.querySelector(selector)
    if (!(root instanceof HTMLElement)) return false
    const animated = root.classList.contains('zone-card-movement') ? root.querySelector('.moving-card') : root
    const image = root.querySelector('img')
    return animated instanceof HTMLElement && getComputedStyle(animated).visibility !== 'hidden'
      && (!image || image.complete && image.naturalWidth > 0)
      && animated.getAnimations().length > 0
  }, movementSelector(instanceId), { timeout })
}
async function sampleFrame(page, instanceId, progress) {
  return page.evaluate(async ({ selector, instanceId, progress }) => {
    const root = document.querySelector(selector)
    if (!(root instanceof HTMLElement)) return { missing:true }
    const animated = root.classList.contains('zone-card-movement') ? root.querySelector('.moving-card') : root
    if (!(animated instanceof HTMLElement)) return { missing:true }
    const animation = animated.getAnimations()[0]
    if (!animation?.effect) return { missingAnimation:true }
    const duration = Number(animation.effect.getComputedTiming().duration)
    const impact = root.querySelector('[data-entry-impact]')
    const impactAnimation = impact?.getAnimations()[0]
    const impactDuration = impactAnimation?.effect
      ? Number(impactAnimation.effect.getComputedTiming().duration)
      : null
    animation.pause()
    animation.currentTime = duration * progress
    if (impactAnimation && impactDuration != null) {
      impactAnimation.pause()
      impactAnimation.currentTime = impactDuration * progress
    }
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    const rect = animated.getBoundingClientRect()
    const impactRect = impact?.getBoundingClientRect()
    const impactStyle = impact ? getComputedStyle(impact) : null
    const target = document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
    const targetRect = target?.getBoundingClientRect()
    const action = document.querySelector('[data-tutorial-action="end-turn"]')
    const style = getComputedStyle(root)
    return {
      missing:false,
      duration,
      flightKeyframeCount:animation.effect.getKeyframes().length,
      rect:{ x:rect.x, y:rect.y, width:rect.width, height:rect.height, centerX:rect.left + rect.width / 2, centerY:rect.top + rect.height / 2 },
      targetRect:targetRect && { x:targetRect.x, y:targetRect.y, width:targetRect.width, height:targetRect.height,
        centerX:targetRect.left + targetRect.width / 2, centerY:targetRect.top + targetRect.height / 2 },
      viewport:{ width:innerWidth, height:innerHeight },
      rootVisibility:getComputedStyle(root).visibility,
      targetVisibility:target instanceof HTMLElement ? getComputedStyle(target).visibility : 'missing',
      actionPresent:action instanceof HTMLButtonElement,
      actionDisabled:action instanceof HTMLButtonElement ? action.disabled || action.getAttribute('aria-disabled') === 'true' : null,
      entryPresentation:root.dataset.entryPresentation ?? '',
      impactTier:root.dataset.entryImpactTier ?? '',
      impactStrength:style.getPropertyValue('--entry-impact-strength').trim(),
      impactNode:Boolean(root.querySelector('[data-entry-impact]')),
      impactAnimationDuration:impactDuration,
      impactOpacity:impactStyle ? Number.parseFloat(impactStyle.opacity) : 0,
      impactPointerEvents:impactStyle?.pointerEvents ?? '',
      impactRect:impactRect && { x:impactRect.x, y:impactRect.y, width:impactRect.width, height:impactRect.height,
        centerX:impactRect.left + impactRect.width / 2, centerY:impactRect.top + impactRect.height / 2 },
      renderPath:root.classList.contains('zone-card-movement') ? 'template' : 'holder',
    }
  }, { selector:movementSelector(instanceId), instanceId, progress })
}
async function resumeMovement(page, instanceId) {
  await page.evaluate(selector => {
    const root = document.querySelector(selector)
    const animated = root?.classList.contains('zone-card-movement') ? root.querySelector('.moving-card') : root
    const animation = animated?.getAnimations()[0]
    if (animation) { animation.playbackRate = 12; animation.play() }
  }, movementSelector(instanceId))
}
async function waitForIdle(page, timeout = 8_000) {
  await page.waitForFunction(() => {
    const state = window.__visualTransitionHarness.presentationSnapshot()
    return !state.busy && !document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')
  }, null, { timeout })
}
async function shot(page, name) {
  const target = path.join(output, `${name}.png`)
  await page.screenshot({ path:target, fullPage:true })
  report.screenshots.push(target)
}
function centerDistance(frame) {
  return Math.hypot(frame.rect.centerX - frame.viewport.width / 2, frame.rect.centerY - frame.viewport.height / 2)
}
function targetDistance(frame) {
  if (!frame.targetRect) return Number.POSITIVE_INFINITY
  return Math.hypot(frame.rect.centerX - frame.targetRect.centerX, frame.rect.centerY - frame.targetRect.centerY)
}
function impactTargetDistance(frame) {
  if (!frame.impactRect || !frame.targetRect) return Number.POSITIVE_INFINITY
  return Math.hypot(frame.impactRect.centerX - frame.targetRect.centerX,
    frame.impactRect.centerY - frame.targetRect.centerY)
}
function frameInsideViewport(frame) {
  const { rect, viewport } = frame
  return rect.x >= -1 && rect.y >= -1 && rect.x + rect.width <= viewport.width + 1 && rect.y + rect.height <= viewport.height + 1
}
async function freeActionState(page) {
  return page.evaluate(() => {
    const action = document.querySelector('[data-tutorial-action="end-turn"]')
    return { present:action instanceof HTMLButtonElement,
      disabled:action instanceof HTMLButtonElement ? action.disabled || action.getAttribute('aria-disabled') === 'true' : null }
  })
}
async function preparationVisualState(page, instanceId) {
  return page.evaluate(selector => {
    const roots = [...document.querySelectorAll(selector)].filter(node => node instanceof HTMLElement
      && getComputedStyle(node).visibility !== 'hidden')
    const details = roots.map(root => {
      const animated = root.classList.contains('zone-card-movement') ? root.querySelector('.moving-card') : root
      const images = [...root.querySelectorAll('img')]
      const hasAuthoritativeFace = images.some(image => /QA-LIGHT\.png/i.test(image.currentSrc || image.src)
        || image.alt === '轻装登场军团')
      const movingAnimations = animated instanceof HTMLElement
        ? animated.getAnimations().filter(animation => animation.playState === 'running'
          && Number(animation.currentTime ?? 0) > 0)
        : []
      return {
        rootClass:root.className,
        from:root.dataset.movementFrom ?? '',
        cardBack:Boolean(root.querySelector('.card-back')),
        hasAuthoritativeFace,
        movingAnimationCount:movingAnimations.length,
        transform:animated instanceof HTMLElement ? getComputedStyle(animated).transform : 'missing',
      }
    })
    return {
      visibleCount:roots.length,
      safe:details.length > 0 && details.every(item => item.rootClass.includes('l12-zone-flight-ghost')
        && item.from === 'hand' && item.cardBack && !item.hasAuthoritativeFace
        && item.movingAnimationCount === 0 && (item.transform === 'none' || item.transform === 'matrix(1, 0, 0, 1, 0, 0)')),
      details,
    }
  }, movementSelector(instanceId))
}
async function observeMovementStarts(page) {
  await page.evaluate(() => {
    window.__legionEntryStarts = []
    const seen = new Set()
    const scan = () => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').forEach(node => {
      const key = node.getAttribute('data-movement-key') ?? ''
      const instanceId = node.getAttribute('data-movement-instance-id') ?? ''
      if (key && !seen.has(key)) { seen.add(key); window.__legionEntryStarts.push({ key, instanceId, at:performance.now() }) }
    })
    new MutationObserver(scan).observe(document.body, { childList:true, subtree:true, attributes:true })
    scan()
  })
}
async function run(name, scenario, fn) {
  current = { profile:name, scenario, stage:'setup' }
  const startFailures = report.failures.length
  try { await fn() }
  catch (error) { record(false, `${name}/${scenario}: unexpected runner error`, error?.stack || String(error), 'no error') }
  report.profiles.push({ name, scenario, passed:report.failures.length === startFailures })
}

async function coreChoreography(profile) {
  const { context, page, errors } = await openProfile(profile)
  try {
    if (profile.interactive) {
      stage('precondition-free-action')
      const before = await freeActionState(page)
      equal(before.present, true, `${profile.name}: real end-turn control must exist before the transaction`)
      equal(before.disabled, false, `${profile.name}: free action must start enabled`)
    }
    await invoke(page, 'resetPresentationTrace')
    await invoke(page, 'setReplay', .5)
    await observeMovementStarts(page)
    stage('authority-entry')
    await invoke(page, 'playTriggeredEntrant')
    await waitForVisibleMovement(page, 'triggered-entrant')

    stage('center-frame')
    const center = await sampleFrame(page, 'triggered-entrant', .45)
    equal(center.entryPresentation, 'legion-center-land', `${profile.name}: claimed legion entry must identify the center-to-land choreography`)
    ok(centerDistance(center) <= Math.min(center.viewport.width, center.viewport.height) * .12,
      `${profile.name}: the sampled center phase must be centered in the visible viewport`, centerDistance(center), `<= ${Math.min(center.viewport.width, center.viewport.height) * .12}`)
    ok(frameInsideViewport(center), `${profile.name}: center card must remain fully visible`, center.rect, 'inside viewport')
    ok(Boolean(center.targetRect) && center.rect.width >= center.targetRect.width * 1.12,
      `${profile.name}: center phase must visibly enlarge the legion`, { center:center.rect, target:center.targetRect }, 'center width >= target width * 1.12')
    equal((await presentation(page)).busy, true, `${profile.name}: preparation/center presentation must retain busy ownership`)
    equal(center.targetVisibility, 'hidden', `${profile.name}: settled authority target must not flash beneath the center card`)
    if (profile.interactive) {
      equal(center.actionPresent, true, `${profile.name}: free action control must remain mounted during presentation`)
      equal(center.actionDisabled, true, `${profile.name}: free action must remain disabled during the center phase`)
    } else equal(center.actionPresent, false, `${profile.name}: spectator must expose no end-turn surface`)
    if (profile.name === 'self-desktop' || profile.name === 'spectator-mobile-portrait')
      await shot(page, `${profile.name}-center-frame`)

    stage('landing-frame')
    const landing = await sampleFrame(page, 'triggered-entrant', .90)
    ok(targetDistance(landing) <= Math.max(24, Math.hypot(landing.targetRect?.width ?? 0, landing.targetRect?.height ?? 0) * .35),
      `${profile.name}: the landing frame must converge on the authoritative slot`, targetDistance(landing), 'near target')
    ok(['light','medium','heavy'].includes(landing.impactTier), `${profile.name}: landing must expose a public troop-derived impact tier`, landing.impactTier, 'light|medium|heavy')
    ok(landing.impactNode && landing.impactAnimationDuration === landing.duration,
      `${profile.name}: landing impact must be a real animation synchronized to the flight duration`,
      { node:landing.impactNode, impactDuration:landing.impactAnimationDuration, flightDuration:landing.duration },
      { node:true, equalDuration:true })
    ok(landing.impactOpacity > 0, `${profile.name}: sampled landing impact must be visibly rendered`, landing.impactOpacity, '> 0')
    equal(landing.impactPointerEvents, 'none', `${profile.name}: landing impact must never intercept interaction`)
    ok(impactTargetDistance(landing) <= Math.max(30, Math.hypot(landing.targetRect?.width ?? 0, landing.targetRect?.height ?? 0) * .45),
      `${profile.name}: visible impact must be local to the authoritative landing slot`, impactTargetDistance(landing), 'near target')
    equal((await presentation(page)).busy, true, `${profile.name}: landing has not finished and cannot release busy ownership`)
    if (profile.interactive) equal((await freeActionState(page)).disabled, true,
      `${profile.name}: free action must remain disabled before landing completion`)
    if (profile.name === 'self-desktop') await shot(page, `${profile.name}-landing-frame`)

    stage('settled-release')
    await resumeMovement(page, 'triggered-entrant')
    await waitForIdle(page)
    const starts = await page.evaluate(() => window.__legionEntryStarts.slice())
    equal(starts.filter(item => item.instanceId === 'triggered-entrant').length, 1,
      `${profile.name}: one authority migration must start one entry animation`)
    const targetVisibility = await page.locator('[data-card-instance-id="triggered-entrant"]').evaluate(node => getComputedStyle(node).visibility)
    equal(targetVisibility, 'visible', `${profile.name}: authority card must be visible after the handoff`)
    const trace = (await presentation(page)).trace
    ok(trace.some(item => item.value === true) && trace.at(-1)?.value === false,
      `${profile.name}: busy trace must cover the transaction and release only at idle`, trace, 'contains true and ends false')
    if (profile.interactive) equal((await freeActionState(page)).disabled, false,
      `${profile.name}: free action must release after the complete landing handoff`)
    ok(errors.length === 0, `${profile.name}: page errors`, errors, [])
  } finally { await context.close() }
}

async function visibleTransactionSurfaces(page, instanceId) {
  return page.evaluate(instanceId => {
    const visible = node => {
      if (!(node instanceof HTMLElement)) return false
      const style = getComputedStyle(node)
      const rect = node.getBoundingClientRect()
      return style.display !== 'none' && style.visibility !== 'hidden'
        && Number.parseFloat(style.opacity || '1') > 0 && rect.width > 0 && rect.height > 0
    }
    const movementRoots = [...document.querySelectorAll(
      `.l12-zone-flight-ghost[data-movement-instance-id="${CSS.escape(instanceId)}"],.zone-card-movement[data-movement-instance-id="${CSS.escape(instanceId)}"]`,
    )].filter(root => visible(root.classList.contains('zone-card-movement')
      ? root.querySelector('.moving-card') : root))
    const authority = [...document.querySelectorAll(
      `[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`,
    )].filter(visible)
    const reveal = document.querySelector('.public-reveal-animation')
    const revealCards = reveal ? [...reveal.querySelectorAll('.public-reveal-cards .l12-card-image')].filter(visible) : []
    return {
      movementCount:movementRoots.length,
      authorityCount:authority.length,
      revealVisible:visible(reveal),
      revealCardCount:revealCards.length,
      revealKind:reveal?.getAttribute('data-presentation-kind') ?? '',
      visibleCardFaces:movementRoots.length + authority.length + revealCards.length,
    }
  }, instanceId)
}

async function startTransactionFrameTimeline(page, instanceId, cardName) {
  await page.evaluate(({ instanceId, cardName }) => {
    const visible = node => {
      if (!(node instanceof HTMLElement)) return false
      const style = getComputedStyle(node)
      const rect = node.getBoundingClientRect()
      return style.display !== 'none' && style.visibility !== 'hidden'
        && Number.parseFloat(style.opacity || '1') > 0 && rect.width > 0 && rect.height > 0
    }
    const frames = []
    let running = true
    const sample = now => {
      const movement = [...document.querySelectorAll(
        `.l12-zone-flight-ghost[data-movement-instance-id="${CSS.escape(instanceId)}"],.zone-card-movement[data-movement-instance-id="${CSS.escape(instanceId)}"]`,
      )].filter(root => visible(root.classList.contains('zone-card-movement')
        ? root.querySelector('.moving-card') : root)).length
      const authority = [...document.querySelectorAll(
        `[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`,
      )].filter(visible).length
      const reveal = [...document.querySelectorAll('.public-reveal-cards .l12-card-image')]
        .filter(root => {
          const image = root.matches('img') ? root : root.querySelector('img')
          return visible(root) && image?.getAttribute('alt') === cardName
        }).length
      frames.push({ at:now, movement, authority, reveal, total:movement + authority + reveal })
      if (running) requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
    window.__l12TransactionFrameTimeline = {
      stop() { running = false; return frames.map(frame => ({ ...frame })) },
    }
  }, { instanceId, cardName })
}

async function stopTransactionFrameTimeline(page) {
  return page.evaluate(() => window.__l12TransactionFrameTimeline?.stop() ?? [])
}

async function waitForNaturalMovementMidpoint(page, instanceId) {
  await page.waitForFunction(selector => {
    const root = document.querySelector(selector)
    const animated = root?.classList.contains('zone-card-movement') ? root.querySelector('.moving-card') : root
    if (!(animated instanceof HTMLElement)) return false
    const progress = animated.getAnimations()[0]?.effect?.getComputedTiming().progress
    return typeof progress === 'number' && progress >= .35 && progress <= .8
  }, movementSelector(instanceId), { timeout:5_000 })
}

async function singleOwnerScenario(profile, kind, options = {}) {
  const { context, page, errors } = await openProfile(profile)
  const instanceId = kind === 'artifact' ? 'entry-artifact' : 'triggered-entrant'
  const cardName = kind === 'artifact' ? '登场圣物' : '加拉哈德'
  try {
    await invoke(page, 'setReplay', options.naturalTimeline ? null : .5)
    await observeMovementStarts(page)
    if (options.frameTimeline) await startTransactionFrameTimeline(page, instanceId, cardName)
    stage(`${kind}-movement`)
    await invoke(page, kind === 'artifact' ? 'playEntryArtifact' : 'playTriggeredEntrant')
    await waitForVisibleMovement(page, instanceId)
    const moving = options.naturalTimeline ? null : await sampleFrame(page, instanceId, .45)
    if (options.naturalTimeline) await waitForNaturalMovementMidpoint(page, instanceId)
    const movementSurfaces = await visibleTransactionSurfaces(page, instanceId)
    equal(options.naturalTimeline ? movementSurfaces.authorityCount : moving.targetVisibility,
      options.naturalTimeline ? 0 : 'hidden',
      `${profile.name}/${kind}: authority destination stays hidden during movement ownership`)
    equal(movementSurfaces.visibleCardFaces, 1,
      `${profile.name}/${kind}: hand-to-destination movement exposes exactly one card face`, movementSurfaces, 'one visible face')
    if (options.naturalTimeline && kind === 'legion') await shot(page, 'natural-single-owner-self-legion-midflight')
    else if (options.frameTimeline && kind === 'legion') await shot(page, 'single-owner-self-legion-center-frame')

    stage(`${kind}-effect-result`)
    if (!options.naturalTimeline) await resumeMovement(page, instanceId)
    await page.waitForSelector('.public-reveal-animation', { state:'visible', timeout:5_000 })
    const continuation = await visibleTransactionSurfaces(page, instanceId)
    equal(continuation.authorityCount, 1,
      `${profile.name}/${kind}: settled authority card is visible during result text`)
    equal(continuation.revealCardCount, 0,
      `${profile.name}/${kind}: the same play transaction must not present a second full card`, continuation, 'text-only continuation')
    equal(continuation.revealKind, 'entry-continuation',
      `${profile.name}/${kind}: the result is classified as the movement transaction continuation`)
    if (options.naturalTimeline && kind === 'artifact') await shot(page, 'natural-single-owner-self-artifact-result')
    else if (options.frameTimeline && kind === 'artifact') await shot(page, 'single-owner-self-artifact-result-frame')
    if (options.frameTimeline) {
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
      const frames = await stopTransactionFrameTimeline(page)
      const occupiedFrames = frames.filter(frame => frame.total > 0)
      const elapsedMs = frames.length > 1 ? frames.at(-1).at - frames[0].at : 0
      ok(frames.length >= 3, `${profile.name}/${kind}: frame timeline spans the authority snapshot through handoff`, frames.length, '>= 3')
      if (options.naturalTimeline) ok(elapsedMs >= 300,
        `${profile.name}/${kind}: natural timeline spans real elapsed animation time without seeking`,
        { elapsedMs, samples:frames.length }, 'elapsed >= 300ms')
      ok(occupiedFrames.some(frame => frame.movement === 1)
        && occupiedFrames.some(frame => frame.authority === 1),
      `${profile.name}/${kind}: timeline observes both movement ownership and settled authority`, occupiedFrames, 'movement then authority')
      ok(occupiedFrames.every(frame => frame.total <= 1),
        `${profile.name}/${kind}: every rendered frame has at most one face for the exact instance`,
        occupiedFrames.filter(frame => frame.total > 1), [])
    }
    await waitForIdle(page)
    const starts = await page.evaluate(instanceId => window.__legionEntryStarts
      .filter(item => item.instanceId === instanceId).length, instanceId)
    equal(starts, 1, `${profile.name}/${kind}: one transaction starts exactly one movement owner`)
    ok(errors.length === 0, `${profile.name}/${kind}: page errors`, errors, [])
  } finally { await context.close() }
}

async function strengthScenario(tier) {
  const profile = { name:`strength-${tier}`, viewer:1, interactive:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile)
  try {
    await invoke(page, 'setReplay', .5)
    await invoke(page, 'playImpactEntrant', tier)
    const instanceId = `${tier}-impact-entrant`
    await waitForVisibleMovement(page, instanceId)
    stage('impact-sample')
    const frame = await sampleFrame(page, instanceId, .92)
    equal(frame.impactTier, tier, `${profile.name}: public troop value must select the expected impact tier`)
    const numericStrength = Number(frame.impactStrength)
    ok(Number.isFinite(numericStrength) && numericStrength > 0,
      `${profile.name}: impact strength must be exposed as a finite CSS parameter`, frame.impactStrength, '> 0')
    await resumeMovement(page, instanceId)
    await waitForIdle(page)
    ok(errors.length === 0, `${profile.name}: page errors`, errors, [])
    return numericStrength
  } finally { await context.close() }
}

async function templateFallbackScenario() {
  const profile = { name:'template-fallback', viewer:1, interactive:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile)
  try {
    await invoke(page, 'setReplay', .5)
    await observeMovementStarts(page)
    await invoke(page, 'playTemplateFallbackEntrant')
    await waitForVisibleMovement(page, 'template-fallback-entrant')
    stage('template-center-frame')
    const center = await sampleFrame(page, 'template-fallback-entrant', .45)
    equal(center.renderPath, 'template', 'template-fallback: public resolving source with no clone must exercise template rendering')
    equal(center.entryPresentation, 'legion-center-land', 'template-fallback: template path must retain legion entry choreography')
    ok(centerDistance(center) <= Math.min(center.viewport.width, center.viewport.height) * .12,
      'template-fallback: template card must hold at visible center', centerDistance(center), 'near viewport center')
    equal(center.targetVisibility, 'hidden', 'template-fallback: authority target must remain covered during center hold')
    stage('template-landing-frame')
    const landing = await sampleFrame(page, 'template-fallback-entrant', .90)
    ok(targetDistance(landing) <= Math.max(24, Math.hypot(landing.targetRect?.width ?? 0, landing.targetRect?.height ?? 0) * .35),
      'template-fallback: template flight must converge on its authoritative slot', targetDistance(landing), 'near target')
    ok(landing.impactAnimationDuration === landing.duration && landing.impactOpacity > 0,
      'template-fallback: impact must start with the prepared flight and be visibly sampled at landing',
      { impactDuration:landing.impactAnimationDuration, flightDuration:landing.duration, opacity:landing.impactOpacity },
      { equalDuration:true, opacity:'> 0' })
    equal(landing.impactPointerEvents, 'none', 'template-fallback: visible landing impact must be pointer-transparent')
    ok(impactTargetDistance(landing) <= Math.max(30, Math.hypot(landing.targetRect?.width ?? 0, landing.targetRect?.height ?? 0) * .45),
      'template-fallback: impact must remain local to the landing slot', impactTargetDistance(landing), 'near target')
    equal((await presentation(page)).busy, true, 'template-fallback: template landing must retain presentation ownership')
    equal((await freeActionState(page)).disabled, true, 'template-fallback: free action must remain locked through landing')
    await resumeMovement(page, 'template-fallback-entrant')
    await waitForIdle(page)
    const starts = await page.evaluate(() => window.__legionEntryStarts.filter(item => item.instanceId === 'template-fallback-entrant').length)
    equal(starts, 1, 'template-fallback: one public transaction must produce one template entry animation')
    equal((await freeActionState(page)).disabled, false, 'template-fallback: free action releases after the complete handoff')
    ok(errors.length === 0, 'template-fallback: page errors', errors, [])
  } finally { await context.close() }
}

async function preparationScenario() {
  const profile = { name:'delayed-preparation', viewer:0, interactive:true, privateHand:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile)
  let releaseAsset
  const assetGate = new Promise(resolve => { releaseAsset = resolve })
  await page.route('**/api/site/media/visual-transition/QA-LIGHT.png', async route => {
    await assetGate
    await route.fulfill({ contentType:'image/svg+xml', body:fixtureSvg('QA-LIGHT') })
  })
  try {
    await invoke(page, 'resetPresentationTrace')
    await invoke(page, 'playImpactEntrant', 'light')
    stage('asset-preparation')
    await page.waitForFunction(() => window.__visualTransitionHarness.presentationSnapshot().busy)
    equal((await presentation(page)).busy, true, 'delayed-preparation: image preparation must own busy before visible motion')
    equal((await freeActionState(page)).disabled, true, 'delayed-preparation: free action cannot release while the public face is unresolved')
    const preparationVisual = await preparationVisualState(page, 'light-impact-entrant')
    ok(preparationVisual.safe,
      'delayed-preparation: unresolved public art may show only the static privacy-safe hand card back before motion starts',
      preparationVisual, { visibleCount:'at least one', from:'hand', cardBack:true, authoritativeFace:false, movingAnimationCount:0 })
    releaseAsset()
    await waitForVisibleMovement(page, 'light-impact-entrant')
    equal((await presentation(page)).busy, true, 'delayed-preparation: busy must remain continuous from preparation into motion')
    await resumeMovement(page, 'light-impact-entrant')
    await waitForIdle(page)
    equal((await freeActionState(page)).disabled, false, 'delayed-preparation: free action releases only after prepared motion settles')
    ok(errors.length === 0, 'delayed-preparation: page errors', errors, [])
  } finally { releaseAsset?.(); await context.close() }
}

async function accessibilityScenario(name, options) {
  const profile = { name, viewer:1, interactive:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile, options)
  try {
    await observeMovementStarts(page)
    await invoke(page, 'playTriggeredEntrant')
    await waitForVisibleMovement(page, 'triggered-entrant')
    stage('essential-handoff')
    const frame = await sampleFrame(page, 'triggered-entrant', .90)
    equal((await presentation(page)).busy, true, `${name}: handoff must retain busy until completion`)
    equal((await freeActionState(page)).disabled, true, `${name}: handoff cannot release free action early`)
    const disabled = options.animation === 'off'
    equal(frame.impactNode, !disabled, `${name}: only explicit off suppresses decorative impact`)
    if (disabled) equal(frame.flightKeyframeCount, 2, `${name}: off retains only source and target keyframes`)
    else ok(frame.flightKeyframeCount > 2, `${name}: standard/fast retain the real flight under system reduce`, frame.flightKeyframeCount, '> 2')
    await resumeMovement(page, 'triggered-entrant')
    await waitForIdle(page)
    const starts = await page.evaluate(() => window.__legionEntryStarts.slice())
    equal(starts.filter(item => item.instanceId === 'triggered-entrant').length, 1,
      `${name}: accessibility preference must still perform exactly one identity handoff`)
    equal((await freeActionState(page)).disabled, false, `${name}: action releases after simplified handoff`)
    ok(errors.length === 0, `${name}: page errors`, errors, [])
  } finally { await context.close() }
}

async function retransmitScenario() {
  const profile = { name:'retransmit', viewer:0, interactive:true, privateHand:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile)
  try {
    await observeMovementStarts(page)
    await invoke(page, 'playTriggeredEntrant')
    await waitForVisibleMovement(page, 'triggered-entrant')
    await resumeMovement(page, 'triggered-entrant')
    await waitForIdle(page)
    stage('same-revision-rematerialization')
    await invoke(page, 'rematerializeCurrentSnapshot')
    await delay(180)
    await invoke(page, 'advanceRevisionWithoutEntry')
    await delay(180)
    const starts = await page.evaluate(() => window.__legionEntryStarts.slice())
    equal(starts.filter(item => item.instanceId === 'triggered-entrant').length, 1,
      'retransmit: same sequence at equal or later revision must not replay the settled entry')
    equal((await presentation(page)).busy, false, 'retransmit: rematerialization must not reacquire busy')
    ok(errors.length === 0, 'retransmit: page errors', errors, [])
  } finally { await context.close() }
}

async function matchSwitchScenario() {
  const profile = { name:'match-switch', viewer:0, interactive:true, privateHand:true, viewport:{ width:1366, height:768 } }
  const { context, page, errors } = await openProfile(profile)
  try {
    await observeMovementStarts(page)
    await invoke(page, 'setReplay', .5)
    await invoke(page, 'playImpactEntrant', 'heavy')
    await waitForVisibleMovement(page, 'heavy-impact-entrant')
    await sampleFrame(page, 'heavy-impact-entrant', .45)
    stage('switch-active-match')
    await invoke(page, 'switchMatchWithHistoricalEntry')
    await page.waitForFunction(() => !document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')
      && !window.__visualTransitionHarness.presentationSnapshot().busy)
    const historicalStarts = await page.evaluate(() => window.__legionEntryStarts.filter(item => item.instanceId === 'historical-match-entrant').length)
    equal(historicalStarts, 0, 'match-switch: a new-match historical snapshot establishes a baseline and never backfills entry motion')
    equal(await page.locator('[data-card-instance-id="historical-match-entrant"]').evaluate(node => getComputedStyle(node).visibility), 'visible',
      'match-switch: cancelling old visual ownership must leave the new authoritative board visible')
    stage('next-match-live-entry')
    await invoke(page, 'playNextMatchEntrant')
    await waitForVisibleMovement(page, 'next-match-entrant')
    await resumeMovement(page, 'next-match-entrant')
    await waitForIdle(page)
    const nextStarts = await page.evaluate(() => window.__legionEntryStarts.filter(item => item.instanceId === 'next-match-entrant').length)
    equal(nextStarts, 1, 'match-switch: the next live entry in the new match must still present exactly once')
    ok(errors.length === 0, 'match-switch: page errors', errors, [])
  } finally { await context.close() }
}

try {
  await server.listen()
  port = server.httpServer.address().port
  browser = await chromium.launch({ channel:'msedge', headless:true })
  if (naturalFrameTimelineOnly) {
    const profile = coreProfiles.find(profile => profile.name === 'self-desktop')
    await run('self-desktop', 'natural-single-owner-legion-frame-timeline',
      () => singleOwnerScenario(profile, 'legion', { frameTimeline:true, naturalTimeline:true }))
    await run('self-desktop', 'natural-single-owner-artifact-frame-timeline',
      () => singleOwnerScenario(profile, 'artifact', { frameTimeline:true, naturalTimeline:true }))
  } else if (frameTimelineOnly) {
    const profile = coreProfiles.find(profile => profile.name === 'self-desktop')
    await run('self-desktop', 'single-owner-legion-frame-timeline',
      () => singleOwnerScenario(profile, 'legion', { frameTimeline:true }))
    await run('self-desktop', 'single-owner-artifact-frame-timeline',
      () => singleOwnerScenario(profile, 'artifact', { frameTimeline:true }))
  } else if (singleOwnerOnly) {
    const focusedProfiles = coreProfiles.filter(profile =>
      ['self-desktop', 'opponent-desktop', 'spectator-desktop'].includes(profile.name))
    for (const profile of focusedProfiles) {
      await run(profile.name, 'single-owner-legion', () => singleOwnerScenario(profile, 'legion'))
      await run(profile.name, 'single-owner-artifact', () => singleOwnerScenario(profile, 'artifact'))
    }
  } else {
    for (const profile of coreProfiles)
      await run(profile.name, 'core-choreography', () => coreChoreography(profile))
    let lightStrength
    let heavyStrength
    await run('strength-light', 'troop-impact', async () => { lightStrength = await strengthScenario('light') })
    await run('strength-heavy', 'troop-impact', async () => { heavyStrength = await strengthScenario('heavy') })
    current = { profile:'strength-comparison', scenario:'troop-impact', stage:'cross-tier' }
    ok(Number.isFinite(lightStrength) && Number.isFinite(heavyStrength) && heavyStrength > lightStrength,
      'troop-impact: heavy public troop tier must produce a stronger local impact than light tier',
      { lightStrength, heavyStrength }, 'heavy > light')
    await run('template-fallback', 'template-render-path', templateFallbackScenario)
    await run('delayed-preparation', 'preparation', preparationScenario)
    await run('system-reduce-standard', 'animation-preference', () => accessibilityScenario('system-reduce-standard', { reducedMotion:'reduce' }))
    await run('system-reduce-fast', 'animation-preference', () => accessibilityScenario('system-reduce-fast', { reducedMotion:'reduce', animation:'fast' }))
    await run('system-reduce-off', 'animation-preference', () => accessibilityScenario('system-reduce-off', { reducedMotion:'reduce', animation:'off' }))
    await run('animation-off', 'accessibility', () => accessibilityScenario('animation-off', { animation:'off' }))
    await run('retransmit', 'dedupe', retransmitScenario)
    await run('match-switch', 'authority-boundary', matchSwitchScenario)
  }
} catch (error) {
  current = { profile:'runner', scenario:'infrastructure', stage:'top-level' }
  record(false, 'legion entry runner must complete without infrastructure error', error?.stack || String(error), 'no error')
} finally {
  if (browser) { await browser.close(); report.cleanup.browserClosed = true }
  await server.close()
  report.cleanup.serverClosed = server.httpServer?.listening === false
}

report.sources.after = bindSources()
current = { profile:'source-binding', scenario:'integrity', stage:'post-run' }
for (const name of Object.keys(report.sources.before))
  equal(report.sources.after[name].sha256, report.sources.before[name].sha256,
    `source-binding: ${name} must not change during evidence capture`)
ok(report.cleanup.browserClosed && report.cleanup.serverClosed,
  'cleanup: browser and local Vite listener must close before report completion', report.cleanup, { browserClosed:true, serverClosed:true })
report.resourceSummary = {
  consoleErrors:report.resourceDiagnostics.reduce((count, item) => count + item.consoleErrors.length, 0),
  requestFailures:report.resourceDiagnostics.reduce((count, item) => count + item.requestFailures.length, 0),
  samples:report.resourceDiagnostics.flatMap(item => [
    ...item.consoleErrors.map(message => ({ profile:item.profile, type:'console-error', message })),
    ...item.requestFailures.map(failure => ({ profile:item.profile, type:'request-failure', ...failure })),
  ]).slice(0, 20),
}
report.passed = report.failures.length === 0
report.finishedAt = new Date().toISOString()
const reportPath = path.join(output, 'report.json')
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2))
console.log(JSON.stringify({ passed:report.passed, checks:report.checks.length, assertions:report.assertions,
  failures:report.failures.length, profiles:report.profiles.length, reportPath }, null, 2))
if (!report.passed) process.exitCode = 1
