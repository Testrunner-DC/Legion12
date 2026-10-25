import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { gunzipSync } from 'node:zlib'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const scriptFile = fileURLToPath(import.meta.url)
const front = path.resolve(path.dirname(scriptFile), '..')
const worktree = path.resolve(front, '..')
const output = process.env.L12_ENTRY_CHOICE_OUT
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp', 'entry-choice-single-owner-20261025')
const expectation = String(process.env.L12_ENTRY_CHOICE_EXPECT || 'red').trim().toLowerCase()
const perspective = process.env.L12_ENTRY_CHOICE_VIEW || 'owner'
const profileKey = process.env.L12_ENTRY_CHOICE_PROFILE || 'desktop'
assert.ok(['owner','spectator'].includes(perspective), 'unsupported stored snapshot perspective')
const profile = {desktop:{width:1366,height:768,mobile:false},'short-landscape':{width:844,height:390,mobile:true},
  'portrait-rotated':{width:390,height:844,mobile:true}}[profileKey]
assert.ok(profile, 'unsupported measured viewport')
const fixturePath = path.join(front, 'scripts/fixtures/battle-authority-engine-v5.json.gz.b64')

assert.ok(output && path.isAbsolute(output), 'L12_ENTRY_CHOICE_OUT must be an absolute controlled path')
assert.ok(path.isAbsolute(cache), 'L12_WORK_CACHE must be an absolute controlled path')
assert.ok(['red', 'green'].includes(expectation), `unsupported L12_ENTRY_CHOICE_EXPECT: ${expectation}`)
assert.ok(!fs.existsSync(output), `Refusing to overwrite evidence: ${output}`)

const guardEnv = {
  ...process.env,
  L12_ENTRY_CHOICE_OUT:output,
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
    + '@($env:L12_ENTRY_CHOICE_OUT,$env:L12_WORK_CACHE,$env:TEMP) | ForEach-Object { '
    + '$null=Assert-L12GeneratedPath -Path $_ -Purpose "entry choice single visual owner QA" }',
], { stdio:'pipe', env:guardEnv })
fs.mkdirSync(output, { recursive:true })
fs.mkdirSync(temp, { recursive:true })
Object.assign(process.env, { TEMP:temp, TMP:temp, TMPDIR:temp })

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
const fixtureContainer = fs.readFileSync(fixturePath)
const fixtureBytes = gunzipSync(Buffer.from(fixtureContainer.toString('utf8').trim(), 'base64'))
const fixture = JSON.parse(fixtureBytes.toString('utf8'))
const fixtureOriginCommit = execFileSync('git', [
  'log', '-1', '--format=%H', '--', path.relative(worktree, fixturePath),
], { cwd:worktree, encoding:'utf8' }).trim()
const chain = fixture.galahadEntry
assert.ok(Array.isArray(chain), 'actual command-generated galahadEntry chain missing')
const frame = label => {
  const found = chain.find(item => item.label === label)
  const snapshot=found?.[perspective==='spectator'?'spectator':'player']
  assert.ok(snapshot, `missing actual ${perspective} frame: ${label}`)
  return structuredClone(snapshot)
}
const baseline = frame('entry-baseline-hand')
const committed = frame('entry-committed-declaration')
const sequenceLabels = [
  'entry-committed-declaration',
  'entry-trial-cost-paid',
  'entry-followup-1-response-stack-response',
  'entry-followup-2-response-stack-response',
  'entry-followup-3-option-pending-activation',
  'entry-followup-4-response-stack-response',
  'entry-followup-5-response-stack-response',
  'entry-settled',
  'entry-same-revision-retransmission',
]
const sequence = sequenceLabels.map(label => ({ label, snapshot:frame(label) }))
const trialCostPaid = sequence.find(item => item.label === 'entry-trial-cost-paid')?.snapshot
const instanceId = 'real-galahad'
const cardName = '加拉哈德'
const prompt = chain.find(item=>item.label==='entry-committed-declaration').player.prompts?.[0]
assert.equal(prompt?.sourceInstanceId, instanceId, 'actual choice prompt is not bound to Galahad')
assert.equal(prompt?.kind, 'option', 'actual Galahad frame is not the entry effect choice')
assert.equal(chain.find(item=>item.label==='entry-trial-cost-paid').player.prompts?.[0]?.data?.sourceInstanceId, instanceId,
  'actual response prompt is not bound to Galahad through prompt.data.sourceInstanceId')
if(perspective==='owner') assert.ok((baseline.players?.[0]?.hand ?? []).some(card => card.instanceId === instanceId),
  'actual baseline does not contain Galahad in hand')
else assert.equal(baseline.players?.[0]?.hand,undefined,'stored public spectator projection must not expose the private hand')
assert.ok((committed.players?.[0]?.field ?? []).flat().some(card => card?.instanceId === instanceId),
  'actual committed frame does not contain Galahad on the field')
const snapshotCards = committed.players.flatMap(player => [
  ...(player.hand ?? []), ...(player.field ?? []).flat().filter(Boolean),
  ...(player.graveyard ?? []), ...(player.resolving ?? []),
  ...[player.relic, ...(player.extraRelics ?? [])].filter(Boolean),
])
const sameIdentityInstances = snapshotCards.filter(card => card.cardId === prompt.sourceCardId && card.name === cardName)
assert.deepEqual(sameIdentityInstances.map(card => card.instanceId), [instanceId],
  'actual snapshot does not uniquely bind the Galahad visual identity to one instance')

const boundFiles = {
  qa:scriptFile,
  fixture:path.join(front, 'scripts/fixtures/BattleVisualTransitionHarness.vue'),
  engineFixture:fixturePath,
  gameBoard:path.join(front, 'src/l12/game/GameBoard.vue'),
  promptOverlay:path.join(front, 'src/l12/game/PromptOverlay.vue'),
  zoneMovement:path.join(front, 'src/l12/game/ZoneMovementPresentationLayer.vue'),
  authoritativeVisibility:path.join(front, 'src/l12/game/authoritativeCardVisibility.ts'),
}
const bindSources = () => Object.fromEntries(Object.entries(boundFiles).map(([name, file]) => [name, {
  path:file,
  sha256:sha256(fs.readFileSync(file)),
  bytes:fs.statSync(file).size,
}]))
const sourcesBefore = bindSources()

const report = {
  schema:1,
  contract:'entry-choice-single-visual-owner-20261025',
  expectation,
  scope:`Actual command-generated Galahad ${perspective} snapshots mounted into the real GameBoard. No live socket, server ACK, persistence, physical device or production write.`,
  perspective, profile:{key:profileKey,...profile},
  fixture:{
    path:fixturePath,
    containerSha256:sha256(fixtureContainer),
    payloadSha256:sha256(fixtureBytes),
    generatedFrom:fixture.generatedFrom,
    fixtureOriginCommit,
    chain:'galahadEntry',
    baseline:'entry-baseline-hand',
    sequence:sequenceLabels,
    deliveryTiming:'Stored engine snapshots delivered in original order with a synthetic 140ms client gap; animation runs continuously without pause or seek.',
    instanceId,
    promptId:prompt.promptId,
    promptSourceInstanceId:prompt.sourceInstanceId,
    uniqueSnapshotIdentityInstances:sameIdentityInstances.map(card => card.instanceId),
  },
  interaction:{ clicks:0, manualInspectorOpen:false, animationSeek:false, animationPause:false, mousePark:null },
  delivered:[],
  validity:{ promptDomVisible:false, spectatorPromptsAbsent:false, decodedVisibleSource:false, movementVisible:false,
    sourceEvidence:null, promptEvidence:null, movementEvidence:null },
  frames:[],
  worstFrame:null,
  screenshot:null,
  pageErrors:[],
  consoleErrors:[],
  requestFailures:[],
  sources:{ before:sourcesBefore, after:null, stable:false },
  cleanup:{ browserClosed:false, serverClosed:false },
  result:null,
}

const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";createApp(Harness).mount("#app")</script></body></html>'
const fixtureSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#10262e"/><rect x="12" y="12" width="226" height="326" rx="8" fill="none" stroke="#d5bc70" stroke-width="8"/><text x="125" y="168" text-anchor="middle" fill="#fff2c7" font-size="24">加拉哈德</text><text x="125" y="205" text-anchor="middle" fill="#8fd4df" font-size="18">REAL SNAPSHOT</text></svg>`
const fixturePlugin = {
  name:'entry-choice-single-owner-fixture',
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const raw = request.url?.split('?')[0] ?? ''
      let decoded = raw
      try { decoded = decodeURIComponent(raw) } catch {}
      if (raw === '/api/alternate-arts') {
        response.setHeader('Content-Type', 'application/json')
        response.end('[]')
        return
      }
      if (raw === '/card-assets/card-assets.manifest.json') {
        response.setHeader('Content-Type', 'application/json')
        response.end(JSON.stringify({
          schemaVersion:3,
          catalogVersion:'entry-choice-diagnostic',
          assetVersion:'entry-choice-diagnostic',
          basePath:'/card-assets',
          cards:{
            'S02-0604':{
              cardId:'S02-0604', contentHash:'entry-choice-galahad', width:250, height:350,
              orientation:'portrait',
              variants:{ originalWebp:'galahad.svg', thumbWebp:'galahad.svg', boardWebp:'galahad.svg', detailWebp:'galahad.svg' },
            },
          },
        }))
        return
      }
      if (raw === '/card-assets/galahad.svg' || raw === '/assets/l12/card-back-official.png') {
        response.setHeader('Content-Type', 'image/svg+xml')
        response.end(fixtureSvg)
        return
      }
      if (decoded.endsWith('/加拉哈德.png')) {
        response.setHeader('Content-Type', 'image/svg+xml')
        response.end(fixtureSvg)
        return
      }
      next()
    })
    server.middlewares.use('/__entry-choice-single-owner', async (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(await server.transformIndexHtml('/__entry-choice-single-owner', html))
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
let context

try {
  await server.listen()
  const address = server.httpServer?.address()
  assert.ok(address && typeof address === 'object', 'Vite did not expose a local port')
  browser = await chromium.launch({ headless:true, channel:'msedge' })
  context = await browser.newContext({ viewport:{ width:profile.width, height:profile.height },
    isMobile:profile.mobile,hasTouch:profile.mobile })
  const page = await context.newPage()
  await page.route('http://127.0.0.1:8080/api/alternate-arts**', route => route.fulfill({
    status:200, contentType:'application/json', body:'[]',
  }))
  page.on('pageerror', error => report.pageErrors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push(message.text()) })
  page.on('requestfailed', request => report.requestFailures.push({
    url:request.url(),
    errorText:request.failure()?.errorText ?? 'unknown request failure',
  }))
  await page.addInitScript(() => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({
    animation:'standard', mobileLayout:'auto', cardSize:'medium', fontSize:'standard',
  })))
  await page.goto(`http://127.0.0.1:${address.port}/__entry-choice-single-owner?viewer=0&interactive=1${perspective==='spectator'?'&spectator=1':''}${profile.mobile?'&landscape=1':''}`, {
    waitUntil:'networkidle', timeout:30_000,
  })
  await page.waitForSelector('[data-l12-game-stage]')
  const mousePark = await page.evaluate(() => {
    const candidates = [[2, 2], [innerWidth - 2, 2], [2, innerHeight - 2], [innerWidth - 2, innerHeight - 2]]
    return candidates.find(([x, y]) => !document.elementFromPoint(x, y)?.closest(
      '[data-card-instance-id],.l12-card-image,.prompt-card-candidate,[data-ui-contract="selected-card-inspector"]',
    )) ?? [2, 2]
  })
  await page.mouse.move(mousePark[0], mousePark[1])
  report.interaction.mousePark = mousePark
  await page.evaluate(snapshot => window.__visualTransitionHarness.applyAuthoritySnapshot(snapshot), baseline)
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))

  const baselineVisible = await page.evaluate(({ instanceId, cardName }) => {
    const unique = [...document.querySelectorAll(`img[alt="${CSS.escape(cardName)}"]`)]
    return { images:unique.length, authority:document.querySelectorAll(`[data-card-instance-id="${CSS.escape(instanceId)}"]`).length }
  }, { instanceId, cardName })
  assert.ok(baselineVisible.images <= 1, `baseline already contains duplicate Galahad images: ${JSON.stringify(baselineVisible)}`)

  await page.evaluate(({ instanceId, cardName }) => {
    const intersect = (left, right) => ({
      left:Math.max(left.left, right.left), top:Math.max(left.top, right.top),
      right:Math.min(left.right, right.right), bottom:Math.min(left.bottom, right.bottom),
    })
    const describe = node => {
      if (!(node instanceof Element)) return String(node)
      const id = node.id ? `#${node.id}` : ''
      const classes = [...node.classList].slice(0, 5).map(value => `.${value}`).join('')
      return `${node.tagName.toLowerCase()}${id}${classes}`
    }
    const measureVisibility = node => {
      if (!(node instanceof Element)) return { visible:false, rejection:'not-element', rect:null, chain:[] }
      let rect = node.getBoundingClientRect()
      if (rect.width <= 0 || rect.height <= 0) return {
        visible:false, rejection:'target-zero-rect', rect:null,
        chain:[{ node:describe(node), display:getComputedStyle(node).display, rect:{ width:rect.width, height:rect.height } }],
      }
      let opacity = 1
      const chain = []
      for (let current = node; current instanceof Element; current = current.parentElement) {
        const style = getComputedStyle(current)
        const currentRect = current.getBoundingClientRect()
        const step = {
          node:describe(current), display:style.display, visibility:style.visibility,
          opacity:style.opacity, overflowX:style.overflowX, overflowY:style.overflowY,
          rect:{ left:currentRect.left, top:currentRect.top, right:currentRect.right,
            bottom:currentRect.bottom, width:currentRect.width, height:currentRect.height },
          clipApplied:false,
        }
        chain.push(step)
        if (style.display === 'none') return { visible:false, rejection:`display-none:${step.node}`, rect:null, chain }
        if (style.visibility === 'hidden' || style.visibility === 'collapse') return {
          visible:false, rejection:`visibility-${style.visibility}:${step.node}`, rect:null, chain,
        }
        const ownOpacity = Number.parseFloat(style.opacity || '1')
        if (Number.isFinite(ownOpacity)) opacity *= ownOpacity
        if (opacity <= .01) return { visible:false, rejection:`opacity:${step.node}`, rect:null, chain }
        const clips = current !== node && style.display !== 'contents'
          && /(hidden|clip|auto|scroll)/.test(`${style.overflowX} ${style.overflowY}`)
        if (clips) {
          step.clipApplied = true
          if (currentRect.width <= 0 || currentRect.height <= 0) return {
            visible:false, rejection:`zero-clip-ancestor:${step.node}`, rect:null, chain,
          }
          rect = intersect(rect, currentRect)
          if (rect.right <= rect.left || rect.bottom <= rect.top) return {
            visible:false, rejection:`ancestor-clip:${step.node}`, rect:null, chain,
          }
        }
      }
      rect = intersect(rect, { left:0, top:0, right:innerWidth, bottom:innerHeight })
      if (rect.right <= rect.left || rect.bottom <= rect.top) return {
        visible:false, rejection:'viewport-clip', rect:null, chain,
      }
      return { visible:true, rejection:null,
        rect:{ left:rect.left, top:rect.top, right:rect.right, bottom:rect.bottom,
          width:rect.right - rect.left, height:rect.bottom - rect.top, opacity }, chain }
    }
    const classify = visual => {
      const movement = visual.closest('.l12-zone-flight-ghost,.zone-card-movement')
      if (movement) return { kind:'movement', instanceId:movement.getAttribute('data-movement-instance-id') ?? '' }
      const authority = visual.closest(`[data-card-instance-id="${CSS.escape(instanceId)}"]`)
      if (authority) return { kind:'authority', instanceId }
      if (visual.closest('.public-reveal-cards')) return { kind:'publicReveal', instanceId }
      if (visual.closest('.l12-prompt-overlay')) return { kind:'prompt', instanceId }
      if (visual.closest('[data-ui-contract="selected-card-inspector"]')) return { kind:'inspector-auto-focus', instanceId }
      if (visual.closest('.mobile-card-inspector')) return { kind:'mobile-inspector', instanceId }
      return { kind:'unclassified-same-name', instanceId:'' }
    }
    const frames = []
    let running = true
    let latest = null
    let peak = null
    const sample = at => {
      const candidates = [...document.querySelectorAll('img,canvas[data-l12-pixel-snapshot="true"]')]
        .filter(visual => visual.getAttribute('alt') === cardName)
        .map(visual => ({ visual, measurement:measureVisibility(visual) }))
        .map(item => ({
          ...classify(item.visual),
          rect:item.measurement.rect,
          visibility:item.measurement,
          elementType:item.visual.tagName.toLowerCase(),
          src:item.visual instanceof HTMLImageElement
            ? item.visual.currentSrc || item.visual.src
            : `pixel-snapshot:${item.visual.width}x${item.visual.height}`,
          decoded:item.visual instanceof HTMLImageElement
            ? item.visual.complete && item.visual.naturalWidth > 0
            : item.visual instanceof HTMLCanvasElement && item.visual.dataset.l12PixelSnapshot === 'true'
              && item.visual.width > 0 && item.visual.height > 0,
          naturalWidth:item.visual instanceof HTMLImageElement ? item.visual.naturalWidth : item.visual.width,
          naturalHeight:item.visual instanceof HTMLImageElement ? item.visual.naturalHeight : item.visual.height,
        }))
      const surfaces = candidates.filter(item => item.visibility.visible && item.decoded)
      const promptRoot = document.querySelector('.l12-prompt-overlay')
      const promptMeasurement = promptRoot ? measureVisibility(promptRoot) : {
        visible:false, rejection:'prompt-dom-absent', rect:null, chain:[],
      }
      latest = {
        at,
        phase:window.__entryChoicePhase ?? 'baseline',
        candidates,
        surfaces,
        total:surfaces.length,
        promptVisible:promptMeasurement.visible,
        promptMeasurement,
        promptId:promptRoot?.getAttribute('data-tutorial-prompt-id') ?? '',
      }
      frames.push(latest)
      if (!peak || latest.total > peak.total) peak = latest
      if (running) requestAnimationFrame(sample)
    }
    requestAnimationFrame(sample)
    window.__entryChoiceVisualTimeline = {
      stop() { running = false; return frames.map(item => structuredClone(item)) },
      latest() { return latest ? structuredClone(latest) : null },
      peak() { return peak ? structuredClone(peak) : null },
      measure(selector) {
        const node = document.querySelector(selector)
        return { exists:Boolean(node), measurement:node ? measureVisibility(node) : null }
      },
    }
  }, { instanceId, cardName })

  let duplicateCaptured = false
  const screenshot = path.join(output, `${profileKey}-${perspective}-entry-choice-worst.png`)
  for (const item of sequence) {
    await page.evaluate(({ label, snapshot }) => {
      window.__entryChoicePhase = label
      window.__visualTransitionHarness.applyAuthoritySnapshot(snapshot)
    }, item)
    report.delivered.push({
      label:item.label,
      revision:item.snapshot.revision,
      promptId:item.snapshot.prompts?.[0]?.promptId ?? null,
      waitingKind:item.snapshot.waitingPrompt?.kind ?? null,
    })
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    if (item.label === 'entry-committed-declaration' && perspective==='owner') {
      await page.waitForSelector(`.l12-prompt-overlay[data-tutorial-prompt-id="${prompt.promptId}"]`, {
        state:'visible', timeout:5_000,
      })
      await page.waitForFunction(promptId => window.__entryChoiceVisualTimeline.measure(
        `.l12-prompt-overlay[data-tutorial-prompt-id="${CSS.escape(promptId)}"]`,
      ).measurement?.visible, prompt.promptId, { timeout:1_000, polling:'raf' })
      report.validity.promptEvidence = await page.evaluate(promptId => {
        const selector = `.l12-prompt-overlay[data-tutorial-prompt-id="${CSS.escape(promptId)}"]`
        return window.__entryChoiceVisualTimeline.measure(selector)
      }, prompt.promptId)
      report.validity.promptDomVisible = Boolean(report.validity.promptEvidence?.exists
        && report.validity.promptEvidence?.measurement?.visible)
      assert.ok(report.validity.promptDomVisible,
        `prompt DOM exists but visibility measurement rejected it: ${JSON.stringify(report.validity.promptEvidence)}`)
    }
    if(item.label==='entry-committed-declaration' && perspective==='spectator'){
      report.validity.spectatorPromptsAbsent=await page.locator('.l12-prompt-overlay').count()===0
      assert.ok(report.validity.spectatorPromptsAbsent,'public spectator must not receive the private choice overlay')
    }
    if (item.label === 'entry-trial-cost-paid') {
      const responsePromptId = item.snapshot.prompts?.[0]?.promptId
      if(perspective==='owner') await page.waitForSelector(`.l12-prompt-overlay[data-tutorial-prompt-id="${responsePromptId}"]`, {
        state:'visible', timeout:5_000,
      })
      await page.waitForFunction(name => {
        const matching = [...document.querySelectorAll('img,canvas[data-l12-pixel-snapshot="true"]')]
          .filter(visual => visual.getAttribute('alt') === name)
        const latest = window.__entryChoiceVisualTimeline.latest()
        const ready = visual => visual instanceof HTMLImageElement
          ? visual.complete && visual.naturalWidth > 0
          : visual instanceof HTMLCanvasElement && visual.dataset.l12PixelSnapshot === 'true'
            && visual.width > 0 && visual.height > 0
        return matching.length > 0 && matching.every(ready)
          && latest?.candidates?.some(item => item.decoded && item.visibility?.visible)
      }, cardName, { timeout:5_000 })
      report.validity.sourceEvidence = await page.evaluate(name => {
        const latest = window.__entryChoiceVisualTimeline.latest()
        return {
          phase:latest?.phase ?? null,
          candidates:(latest?.candidates ?? []).filter(item => item.decoded && item.visibility?.visible),
          exactAltCount:[...document.querySelectorAll('img,canvas[data-l12-pixel-snapshot="true"]')]
            .filter(visual => visual.getAttribute('alt') === name).length,
        }
      }, cardName)
      report.validity.decodedVisibleSource = (report.validity.sourceEvidence?.candidates?.length ?? 0) > 0
      assert.ok(report.validity.decodedVisibleSource,
        `no decoded, measured-visible Galahad source: ${JSON.stringify(report.validity.sourceEvidence)}`)
    }
    await page.waitForTimeout(140)
    if (!duplicateCaptured) {
      const duplicateNow = await page.evaluate(() => (window.__entryChoiceVisualTimeline.latest()?.total ?? 0) > 1)
      if (duplicateNow) {
        await page.screenshot({ path:screenshot, fullPage:true })
        duplicateCaptured = true
        report.screenshot = { path:screenshot, kind:'duplicate-current-frame', synchronizedToWorstFrame:false,
          capturedPhase:item.label }
      }
    }
  }
  if (!duplicateCaptured) {
    await page.waitForFunction(() => (window.__entryChoiceVisualTimeline.latest()?.total ?? 0) > 1,
      null, { timeout:1_500, polling:10 }).then(async () => {
      await page.screenshot({ path:screenshot, fullPage:true })
      duplicateCaptured = true
      const latest = await page.evaluate(() => window.__entryChoiceVisualTimeline.latest())
      report.screenshot = { path:screenshot, kind:'duplicate-current-frame', synchronizedToWorstFrame:false,
        capturedPhase:latest?.phase ?? null }
    }).catch(() => {})
  }
  await page.waitForTimeout(1_500)
  const frames = await page.evaluate(() => window.__entryChoiceVisualTimeline.stop())
  report.frames = frames
  report.worstFrame = frames.reduce((worst, item) => !worst || item.total > worst.total ? item : worst, null)
  const movementFrames = frames.filter(item => item.surfaces.some(surface => surface.kind === 'movement'
    && surface.instanceId === instanceId))
  report.validity.movementVisible = movementFrames.length > 0
  report.validity.movementEvidence = movementFrames.length ? {
    count:movementFrames.length,
    first:movementFrames[0],
    last:movementFrames.at(-1),
  } : null
  if (!duplicateCaptured) {
    await page.screenshot({ path:screenshot, fullPage:true })
    report.screenshot = { path:screenshot, kind:'final-state-not-worst-frame', synchronizedToWorstFrame:false,
      capturedPhase:frames.at(-1)?.phase ?? null }
  }

  const maxVisible = report.worstFrame?.total ?? 0
  const kinds = report.worstFrame?.surfaces?.map(item => item.kind) ?? []
  const promptGate=perspective==='owner'?report.validity.promptDomVisible:report.validity.spectatorPromptsAbsent
  const validMeasurement = promptGate && report.validity.decodedVisibleSource
    && report.validity.movementVisible
  report.result = {
    status:!validMeasurement ? 'INVALID_MEASUREMENT'
      : maxVisible > 1 ? 'RED_DUPLICATE_CONFIRMED' : 'GREEN_SINGLE_VISUAL_OWNER',
    maxVisible,
    kinds,
    phase:report.worstFrame?.phase ?? null,
    duplicateCaptured,
    promptVisible:Boolean(report.worstFrame?.promptVisible),
    exactInstanceProvenBy:'actual prompt.sourceInstanceId + unique snapshot cardId/name + authority/movement instance dataset',
  }
  assert.ok(validMeasurement, `measurement validity gate failed: ${JSON.stringify(report.validity)}`)
  if (expectation === 'red') {
    assert.ok(maxVisible > 1, `expected duplicate visual owner was not reproduced: ${JSON.stringify(report.result)}`)
    assert.ok(kinds.includes('inspector-auto-focus'), `duplicate did not include the auto-focused inspector: ${JSON.stringify(report.worstFrame)}`)
    assert.ok(kinds.some(kind => kind === 'movement' || kind === 'authority'),
      `duplicate did not include an authoritative entry surface: ${JSON.stringify(report.worstFrame)}`)
  } else {
    assert.ok(maxVisible <= 1, `same instance had multiple visible owners: ${JSON.stringify(report.worstFrame)}`)
  }
  assert.equal(report.interaction.clicks, 0)
  assert.equal(report.interaction.manualInspectorOpen, false)
  assert.equal(report.interaction.animationSeek, false)
  assert.equal(report.interaction.animationPause, false)
  assert.deepEqual(report.pageErrors, [])
} catch (error) {
  const promptGate=perspective==='owner'?report.validity.promptDomVisible:report.validity.spectatorPromptsAbsent
  const validityPassed = promptGate && report.validity.decodedVisibleSource
    && report.validity.movementVisible
  report.result = {
    ...(report.result ?? {}),
    status:report.result?.status ?? (validityPassed ? 'RUNNER_FAILED' : 'INVALID_MEASUREMENT'),
    runnerError:error?.stack || String(error),
  }
  process.exitCode = 1
} finally {
  if (context) await context.close().catch(() => {})
  if (browser) {
    await browser.close().catch(() => {})
    report.cleanup.browserClosed = true
  }
  await server.close().catch(() => {})
  report.cleanup.serverClosed = true
  report.sources.after = bindSources()
  report.sources.stable = JSON.stringify(report.sources.before) === JSON.stringify(report.sources.after)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({
    status:report.result?.status ?? 'FAILED',
    maxVisible:report.result?.maxVisible ?? null,
    kinds:report.result?.kinds ?? [],
    sourceStable:report.sources.stable,
    output,
  }))
}
