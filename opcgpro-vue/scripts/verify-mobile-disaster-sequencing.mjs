import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const scriptFile = fileURLToPath(import.meta.url)
const front = path.resolve(path.dirname(scriptFile), '..')
const target = process.argv.slice(2).find(value => /^https?:\/\//.test(value))
  || 'http://127.0.0.1:5190/__l12_battle_preview__'
const output = process.env.L12_MOBILE_DISASTER_OUT
if (!output || !path.isAbsolute(output)) throw new Error('L12_MOBILE_DISASTER_OUT must be an absolute governed D/E path')
const guard = path.resolve(front, '..', 'ops/windows/L12GeneratedStorage.ps1')
execFileSync(process.env.L12_PWSH || (process.platform === 'win32' ? 'pwsh.exe' : 'pwsh'), [
  '-NoProfile', '-NonInteractive', '-Command',
  `. '${guard.replaceAll("'", "''")}'; Assert-L12GeneratedPath -Path $env:L12_MOBILE_DISASTER_OUT -Purpose 'disaster visual acceptance' | Out-Null`,
], { stdio:'pipe', env:process.env })
const sha256 = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
const canonicalAssetRootRaw = process.env.L12_DISASTER_CARD_ASSET_ROOT?.trim()
const canonicalAssetLock = path.resolve(front, '..', 'ops/ci/l12-card-assets-input.lock.json')
const canonicalAssetUrl = '/__l12-disaster-qualification__/S01-DS02-original.webp'
const canonicalDisasterAsset = (() => {
  if (!canonicalAssetRootRaw) return null
  assert(path.isAbsolute(canonicalAssetRootRaw), 'L12_DISASTER_CARD_ASSET_ROOT must be an absolute path')
  const root = fs.realpathSync(canonicalAssetRootRaw)
  const lock = JSON.parse(fs.readFileSync(canonicalAssetLock, 'utf8'))
  const locked = lock.files?.find(entry => entry.role === 'current'
    && /(^|\/)S01-DS02\/[^/]+\/original\.webp$/i.test(entry.path?.replaceAll('\\', '/')))
  assert(locked, 'Canonical input lock must bind the current S01-DS02 original.webp')
  assert.equal(locked.size, 385354, 'Canonical S01-DS02 byte size changed in the input lock')
  assert.equal(locked.sha256?.toLowerCase(), '0b54c57ce0de5849089278b9d8ac559657a00e752e288e89dfb082ef61e87d89',
    'Canonical S01-DS02 SHA changed in the input lock')
  const file = fs.realpathSync(path.join(root, locked.path))
  const relative = path.relative(root, file)
  assert(relative && !relative.startsWith('..') && !path.isAbsolute(relative),
    'Canonical S01-DS02 input must remain under L12_DISASTER_CARD_ASSET_ROOT')
  assert.equal(fs.statSync(file).size, locked.size, 'Canonical S01-DS02 input byte size does not match the lock')
  assert.equal(sha256(file).toLowerCase(), locked.sha256.toLowerCase(),
    'Canonical S01-DS02 input SHA does not match the lock')
  return {
    root,
    path:file,
    relativePath:locked.path,
    bytes:locked.size,
    sha256:locked.sha256.toUpperCase(),
    expectedNaturalWidth:1732,
    expectedNaturalHeight:1240,
    testUrl:canonicalAssetUrl,
  }
})()
const sourceBindings = {
  zoneMovement:path.join(front, 'src/l12/game/ZoneMovementPresentationLayer.vue'),
  preview:path.join(front, 'scripts/preview-l12-battle-layout.mjs'),
}
const expectedSourceSha256 = {
  zoneMovement:'AB9AA6110E44B841C0E3B4B48203DB649F2540BED5EA8AD65CA974F1648C3E8A',
  preview:'488B580796B12F885041DCB79B410A23B094092639673FC6897CECCE64520EEE',
}
for (const [name, file] of Object.entries(sourceBindings))
  assert.equal(sha256(file), expectedSourceSha256[name], `${name} source SHA changed before disaster candidate verification`)
const sourcesBefore = Object.fromEntries(Object.entries(sourceBindings).map(([name, file]) => [name, {
  path:file, sha256:sha256(file), bytes:fs.statSync(file).size,
}]))
const viewports = [{ width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 768 }]
const query = 'mobile=1&hand=10&trials=2&myTrials=1&disaster-chain=1'
const executableCases = [
  'self-desktop-center-landscape', 'opponent-desktop-center-landscape', 'spectator-desktop-center-landscape',
  'self-mobile-wide-center-landscape', 'opponent-mobile-wide-center-landscape', 'spectator-mobile-wide-center-landscape',
  'self-rotated-portrait-center-landscape', 'opponent-rotated-portrait-center-landscape', 'spectator-rotated-portrait-center-landscape',
  'no-trigger-full-reveal-once', 'triggered-effect-after-reveal-release', 'delayed-face-static-cardback-privacy',
  'reduced-motion-essential-handoff', 'animation-off-essential-handoff', 'same-fact-retransmit-no-replay',
  'match-switch-cancels-old-and-baselines-history', 'prompt-before-defers', 'prompt-interrupt-consumes-without-replay',
]
const caseFilterIndex = process.argv.indexOf('--case-filter')
const hasCaseFilterEnv = Object.hasOwn(process.env, 'L12_DISASTER_CASE_FILTER')
const hasCaseFilterFlag = caseFilterIndex >= 0
const hasExplicitCaseFilter = hasCaseFilterEnv || hasCaseFilterFlag
const caseFilterRaw = hasCaseFilterEnv
  ? process.env.L12_DISASTER_CASE_FILTER
  : hasCaseFilterFlag ? process.argv[caseFilterIndex + 1] : undefined
assert(!hasExplicitCaseFilter || Boolean(caseFilterRaw?.trim()),
  'Explicit disaster case filter must contain at least one named case')
const selectedCases = hasExplicitCaseFilter
  ? caseFilterRaw.split(',').map(value => value.trim()).filter(Boolean)
  : [...executableCases]
assert(selectedCases.length > 0, 'At least one named disaster case is required')
for (const caseName of selectedCases)
  assert(executableCases.includes(caseName), `Unknown disaster case filter: ${caseName}`)
const selectedCaseSet = new Set(selectedCases)
const caseEnabled = caseName => selectedCaseSet.has(caseName)
const redChecks = []
function redCheck(caseName, passed, actual, expected) {
  redChecks.push({ caseName, passed:Boolean(passed), actual, expected })
}
// These shipped, card-id-specific round faces are controlled local decoder
// inputs for the presentation test. They do not claim that the full-resolution
// card-art manifest or CDN payload has been validated here.
const controlledDisasterArtwork = {
  'S01-DS01': '/assets/l12/special/round/S01-DS01.png',
  'S01-DS02': canonicalDisasterAsset?.testUrl ?? '/assets/l12/special/round/S01-DS02.png',
}
const controlledDisasterManifest = JSON.stringify({
  schemaVersion: 3,
  catalogVersion: 'mobile-disaster-sequencing-controlled-art',
  assetVersion: 'mobile-disaster-sequencing-controlled-art',
  basePath: '/',
  cards: Object.fromEntries(Object.entries(controlledDisasterArtwork).map(([cardId, url]) => [cardId, {
    cardId,
    contentHash: `controlled-local-${cardId}`,
    width: cardId === 'S01-DS02' && canonicalDisasterAsset ? canonicalDisasterAsset.expectedNaturalWidth : 720,
    height: cardId === 'S01-DS02' && canonicalDisasterAsset ? canonicalDisasterAsset.expectedNaturalHeight : 720,
    orientation: cardId === 'S01-DS02' && canonicalDisasterAsset ? 'landscape' : 'portrait',
    variants: { thumbWebp: url, boardWebp: url, detailWebp: url },
  }])),
})
fs.mkdirSync(output, { recursive: true })

const requestedCases = [...selectedCases]
const actualCases = []
const frameworkFailures = []
const markCaseStarted = caseName => {
  if (!actualCases.includes(caseName)) actualCases.push(caseName)
}
const report = {
  schema:1,
  contract:'disaster-center-reveal-presentation-20261024',
  expectedCurrentResult:'GREEN candidate: official back prepares privately, then centers an 8:5 readable face before settling once',
  executableCases,
  selectedCases,
  requestedCases,
  actualCases,
  sources:{ before:sourcesBefore, after:null },
  checks:redChecks,
  assertions:0,
  failures:[],
  frameworkFailures,
  output,
  viewports,
  scenarios: ['current-disaster', 'no-trigger-reveal', 'triggered-reveal', 'deferred-behind-prompt', 'interrupted-no-replay'],
  canonicalAssetQualification:canonicalDisasterAsset && {
    relativePath:canonicalDisasterAsset.relativePath,
    bytes:canonicalDisasterAsset.bytes,
    sha256:canonicalDisasterAsset.sha256,
    expectedNaturalWidth:canonicalDisasterAsset.expectedNaturalWidth,
    expectedNaturalHeight:canonicalDisasterAsset.expectedNaturalHeight,
    testUrl:canonicalDisasterAsset.testUrl,
  },
  cleanup:{ browserClosed:false },
  passed:false,
}
let browser
try {
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(8_000)
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = media => media.includes('pointer: coarse')
      ? ({ matches: true, media, addEventListener() {}, removeEventListener() {} }) : native(media)
  })
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (!['127.0.0.1', 'localhost'].includes(url.hostname)) return route.abort()
    if (url.pathname === '/card-assets/card-assets.manifest.json')
      return route.fulfill({ contentType: 'application/json', body: controlledDisasterManifest })
    if (canonicalDisasterAsset && url.pathname === canonicalDisasterAsset.testUrl)
      return route.fulfill({ contentType:'image/webp', path:canonicalDisasterAsset.path })
    return route.continue()
  })

  const load = async (viewport, scenarioQuery = query) => {
    await page.setViewportSize(viewport)
    await page.goto(`${target}?${scenarioQuery}`, { waitUntil: 'domcontentloaded' })
    if (new URLSearchParams(scenarioQuery).has('mobile'))
      await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    else await page.locator('[data-l12-game-stage]').waitFor()
    await page.waitForFunction(() => Boolean(window.__disasterFixture))
  }
  const assertInsideViewport = async (selector, label) => {
    const rect = await page.locator(selector).evaluate(element => {
      const value = element.getBoundingClientRect()
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height }
    })
    assert.ok(rect.left >= -0.5 && rect.top >= -0.5 && rect.right <= (await page.evaluate(() => innerWidth)) + 0.5 && rect.bottom <= (await page.evaluate(() => innerHeight)) + 0.5,
      `${label} must stay inside the visual viewport: ${JSON.stringify(rect)}`)
  }
  const assertDisasterRail = async suffix => {
    const result = await page.locator('.left-disaster-row').evaluate(element => {
      const strip = element.querySelector('.session-disaster-strip')
      const buttons = [...element.querySelectorAll('.session-disaster-strip button')]
      const current = element.querySelector('.current-disaster-card')
      const copy = element.querySelector('.mobile-current-disaster-copy')
      const currentImage = current?.querySelector('img')
      const value = element.querySelector('.mobile-current-disaster-value')
      if (!strip || !current || !copy || !currentImage || !value) return null
      const stripRect = strip.getBoundingClientRect()
      const currentRect = current.getBoundingClientRect()
      const currentImageRect = currentImage.getBoundingClientRect()
      const copyRect = copy.getBoundingClientRect()
      const valueRect = value.getBoundingClientRect()
      const currentStyle = getComputedStyle(current)
      const currentImageStyle = getComputedStyle(currentImage)
      const copyStyle = getComputedStyle(copy)
      const valueStyle = getComputedStyle(value)
      return {
        stripRect: { left: stripRect.left, top: stripRect.top, right: stripRect.right, bottom: stripRect.bottom },
        stripOverflow: getComputedStyle(strip).overflow,
        buttons: buttons.map(button => {
          const rect = button.getBoundingClientRect()
          return {
            rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height },
            inside: rect.left >= stripRect.left - .5 && rect.right <= stripRect.right + .5 && rect.top >= stripRect.top - .5 && rect.bottom <= stripRect.bottom + .5,
            clipped: getComputedStyle(button).overflow === 'hidden',
            round: Math.abs(rect.width - rect.height) <= 1,
          }
        }),
        currentInside: currentRect.left >= 0 && currentRect.top >= 0 && currentRect.right <= innerWidth && currentRect.bottom <= innerHeight,
        currentVisible: currentRect.width > 20 && currentRect.height > 20 && currentStyle.display !== 'none' && currentStyle.visibility !== 'hidden',
        currentEnabled: !current.disabled,
        currentImageVisible: currentImageRect.width > 20 && currentImageRect.height > 20 && currentImageStyle.display !== 'none' && currentImageStyle.visibility !== 'hidden',
        currentAlt: currentImage.getAttribute('alt')?.trim(),
        copyHidden: copyRect.width === 0 && copyRect.height === 0 && copyStyle.display === 'none',
        valueVisible: valueRect.width > 20 && valueRect.height > 12 && valueStyle.display !== 'none' && valueStyle.visibility !== 'hidden',
        valueInside: valueRect.left >= 0 && valueRect.top >= 0 && valueRect.right <= innerWidth && valueRect.bottom <= innerHeight,
        valueLabel: value.getAttribute('aria-label'),
        valueText: value.textContent?.replace(/\s+/g, ' ').trim(),
      }
    })
    assert.ok(result, `disaster rail ${suffix} must exist`)
    assert.equal(result.stripOverflow, 'hidden', `disaster circles ${suffix} must be clipped by their rail`)
    assert.equal(result.buttons.length, 4, `disaster rail ${suffix} must render four circular slots`)
    if (!result.buttons.every(item => item.inside && item.clipped && item.round)) console.error(JSON.stringify({ suffix, result }, null, 2))
    assert.equal(result.buttons.every(item => item.inside && item.clipped && item.round), true, `disaster circles ${suffix} must remain round and inside the rail`)
    assert.equal(result.currentInside, true, `current disaster ${suffix} must stay in the viewport`)
    assert.equal(result.currentVisible, true, `current disaster card ${suffix} must be visible`)
    assert.equal(result.currentEnabled, true, `current disaster card ${suffix} must be reachable`)
    assert.equal(result.currentImageVisible, true, `current disaster card image ${suffix} must be readable`)
    assert.ok((result.currentAlt ?? '').length > 0, `current disaster card ${suffix} must retain an accessible name`)
    assert.equal(result.copyHidden, true, `redundant current disaster copy ${suffix} must stay hidden in the mobile layout`)
    assert.equal(result.valueVisible, true, `current disaster value ${suffix} must be readable`)
    assert.equal(result.valueInside, true, `current disaster value ${suffix} must stay in the viewport`)
    assert.equal(result.valueLabel, '当前天灾值', `current disaster value ${suffix} must retain its label`)
    assert.match(result.valueText ?? '', /天灾值\s*\d+/, `current disaster value ${suffix} must show its numeric value`)
  }

  const observeDisasterStarts = async () => page.evaluate(() => {
    window.__disasterPresentationStarts = []
    const seen = new Set()
    const scan = () => document.querySelectorAll('.zone-card-movement').forEach(node => {
      const key = node.getAttribute('data-movement-key') ?? ''
      if (key && !seen.has(key)) {
        seen.add(key)
        window.__disasterPresentationStarts.push({ key, at:performance.now() })
      }
    })
    new MutationObserver(scan).observe(document.body, { childList:true, subtree:true, attributes:true })
    scan()
  })
  const armDisasterFrameCapture = async () => page.evaluate(() => {
    window.__disasterFrameCaptureObserver?.disconnect()
    const state = {
      status:'armed',
      scans:0,
      armedAt:performance.now(),
      pausedAt:null,
      rootCount:0,
      animatedCount:0,
      animationCount:0,
    }
    window.__disasterFrameCapture = state
    const scan = () => {
      state.scans += 1
      const roots = [...document.querySelectorAll('.zone-card-movement, .l12-disaster-reveal-holder')]
      const root = roots.at(-1)
      const animated = root?.matches('.l12-disaster-reveal-holder') ? root : root?.querySelector('.moving-card')
      state.rootCount = roots.length
      state.animatedCount = animated instanceof HTMLElement ? 1 : 0
      if (!(root instanceof HTMLElement) || !(animated instanceof HTMLElement)) return
      const animations = root.getAnimations({ subtree:true })
      const flight = animated.getAnimations()[0]
      state.animationCount = animations.length
      if (!flight?.effect) {
        state.status = 'missing-animation'
        return
      }
      for (const animation of animations) animation.pause()
      state.status = 'paused'
      state.pausedAt = performance.now()
      window.__disasterFrameCaptureObserver?.disconnect()
    }
    const observer = new MutationObserver(scan)
    window.__disasterFrameCaptureObserver = observer
    observer.observe(document.body, { childList:true, subtree:true, attributes:true })
    scan()
  })
  const waitForArmedDisasterFrame = async () => {
    try {
      await page.waitForFunction(() => window.__disasterFrameCapture?.status === 'paused', null,
        { timeout:1_500, polling:'raf' })
    } catch { /* sampleDisasterFrame records the exact missing shape below. */ }
  }
  const assertRootMotionReady = async caseName => {
    const state = await page.evaluate(() => {
      const root = [...document.querySelectorAll('.zone-card-movement')].at(-1)
      const animated = root?.matches('.l12-disaster-reveal-holder') ? root : root?.querySelector('.moving-card')
      const style = animated instanceof HTMLElement ? getComputedStyle(animated) : null
      const flight = animated instanceof HTMLElement ? animated.getAnimations()[0] : null
      return {
        easeToken:getComputedStyle(document.documentElement).getPropertyValue('--l12-ease-standard').trim(),
        rootPresent:animated instanceof HTMLElement,
        carrier:root?.matches('.l12-disaster-reveal-holder') ? 'waapi-holder' : 'css-moving-card',
        animationName:style?.animationName ?? null,
        animationDuration:style?.animationDuration ?? null,
        animationCount:animated instanceof HTMLElement ? animated.getAnimations().length : 0,
        hasFlightEffect:Boolean(flight?.effect),
      }
    })
    const ready = Boolean(state.easeToken) && state.rootPresent
      && (state.carrier === 'waapi-holder' || state.animationName !== 'none')
      && state.animationCount > 0 && state.hasFlightEffect
    redCheck(`${caseName}:root-motion-ready`, ready, state,
      { easeToken:'non-empty', rootPresent:true, carrier:'CSS animation or WAAPI holder',
        animationCount:'> 0', hasFlightEffect:true })
    assert.ok(ready, `${caseName} requires the production motion token and root flight before frame sampling: ${JSON.stringify(state)}`)
  }
  const assertDisasterTargetReady = async caseName => {
    const state = await page.evaluate(() => {
      const root = document.querySelector('[data-l12-battle-layout]')
      const targets = [...(root?.querySelectorAll('button.current-disaster-card[data-l12-zone="disaster"]') ?? [])]
      const rect = targets.length === 1 ? targets[0].getBoundingClientRect() : null
      const finiteRect = rect ? [rect.left, rect.top, rect.right, rect.bottom, rect.width, rect.height]
        .every(value => Number.isFinite(value)) : false
      return {
        count:targets.length,
        rect:rect && { left:rect.left, top:rect.top, right:rect.right, bottom:rect.bottom,
          width:rect.width, height:rect.height, centerX:rect.left + rect.width / 2, centerY:rect.top + rect.height / 2 },
        finiteRect,
        usable:Boolean(rect && finiteRect && rect.width > 0 && rect.height > 0),
      }
    })
    const ready = state.count === 1 && state.usable
    redCheck(`${caseName}:target-ready`, ready, state, { count:1, finiteRect:true, usable:true })
    assert.ok(ready, `${caseName} requires exactly one usable authoritative current-disaster target: ${JSON.stringify(state)}`)
  }
  const sampleDisasterFrame = async progress => page.evaluate(async progressValue => {
    const roots = [...document.querySelectorAll('.zone-card-movement')]
    const root = roots.at(-1)
    const animated = root?.matches('.l12-disaster-reveal-holder') ? root : root?.querySelector('.moving-card')
    const motion = {
      reduced:matchMedia('(prefers-reduced-motion: reduce)').matches,
      query:new URLSearchParams(location.search).get('motion') ?? 'default',
      capture:window.__disasterFrameCapture ?? null,
    }
    const describe = element => element instanceof HTMLElement ? {
      tag:element.tagName,
      className:element.className,
      animationName:getComputedStyle(element).animationName,
      animationDuration:getComputedStyle(element).animationDuration,
      animationDelay:getComputedStyle(element).animationDelay,
      animationPlayState:getComputedStyle(element).animationPlayState,
      connected:element.isConnected,
      html:element.outerHTML.slice(0, 2_000),
    } : null
    const dom = {
      rootCount:roots.length,
      animatedCount:animated instanceof HTMLElement ? 1 : 0,
      revealCount:root?.querySelectorAll('.disaster-reveal-card').length ?? 0,
      root:describe(root),
      animated:describe(animated),
    }
    const viewport = { width:innerWidth, height:innerHeight }
    if (!(root instanceof HTMLElement) || !(animated instanceof HTMLElement))
      return { missing:true, reason:!root ? 'missing-root' : 'missing-moving-card', progress:progressValue, viewport, motion, dom }
    const animations = root.getAnimations({ subtree:true })
    const flight = animated.getAnimations()[0]
    const animationState = animations.map(animation => ({
      playState:animation.playState,
      currentTime:Number(animation.currentTime ?? 0),
      duration:Number(animation.effect?.getComputedTiming().duration ?? 0),
      keyframes:animation.effect?.getKeyframes().length ?? 0,
    }))
    if (!flight?.effect) return { missingAnimation:true, reason:'missing-flight-effect', progress:progressValue,
      viewport, motion, dom, animations:animationState }
    for (const animation of animations) {
      const duration = Number(animation.effect?.getComputedTiming().duration ?? 0)
      animation.pause()
      animation.currentTime = duration * progressValue
    }
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    const rect = animated.getBoundingClientRect()
    const style = getComputedStyle(animated)
    const back = root.querySelector('.disaster-reveal-back')
    const front = root.querySelector('.disaster-reveal-front')
    const backStyle = back instanceof HTMLImageElement ? getComputedStyle(back) : null
    const frontStyle = front instanceof HTMLImageElement ? getComputedStyle(front) : null
    const target = document.querySelector('[data-l12-battle-layout] button.current-disaster-card[data-l12-zone="disaster"]')
    const targetRect = target?.getBoundingClientRect()
    const action = document.querySelector('[data-tutorial-action="end-turn"]')
    return {
      missing:false,
      duration:Number(flight.effect.getComputedTiming().duration),
      keyframes:flight.effect.getKeyframes().length,
      rect:{ left:rect.left, top:rect.top, right:rect.right, bottom:rect.bottom, width:rect.width, height:rect.height,
        centerX:rect.left + rect.width / 2, centerY:rect.top + rect.height / 2 },
      cssBox:{ width:Number.parseFloat(style.width), height:Number.parseFloat(style.height) },
      viewport,
      motion,
      dom,
      animations:animationState,
      backOpacity:back ? Number.parseFloat(getComputedStyle(back).opacity) : null,
      frontOpacity:front ? Number.parseFloat(getComputedStyle(front).opacity) : null,
      backObjectFit:backStyle?.objectFit ?? null,
      frontObjectFit:frontStyle?.objectFit ?? null,
      frontNaturalWidth:front instanceof HTMLImageElement ? front.naturalWidth : null,
      frontNaturalHeight:front instanceof HTMLImageElement ? front.naturalHeight : null,
      targetVisibility:target instanceof HTMLElement ? getComputedStyle(target).visibility : 'missing',
      targetRect:targetRect && { centerX:targetRect.left + targetRect.width / 2, centerY:targetRect.top + targetRect.height / 2,
        width:targetRect.width, height:targetRect.height },
      actionPresent:action instanceof HTMLButtonElement,
      actionDisabled:action instanceof HTMLButtonElement
        ? action.disabled || action.getAttribute('aria-disabled') === 'true' : null,
    }
  }, progress)
  const resumeDisaster = async () => page.evaluate(() => {
    window.__disasterFrameCaptureObserver?.disconnect()
    const root = document.querySelector('.zone-card-movement')
    const animations = root instanceof HTMLElement ? root.getAnimations({ subtree:true }) : []
    for (const animation of animations) { animation.playbackRate = 12; animation.play() }
  })
  const usableFrame = frame => frame && !frame.missing && !frame.missingAnimation && frame.rect && frame.viewport
  const insideViewport = frame => frame.rect.left >= -.5 && frame.rect.top >= -.5
    && frame.rect.right <= frame.viewport.width + .5 && frame.rect.bottom <= frame.viewport.height + .5
  const centerDistance = frame => Math.hypot(frame.rect.centerX - frame.viewport.width / 2,
    frame.rect.centerY - frame.viewport.height / 2)
  const targetDistance = frame => frame.targetRect
    ? Math.hypot(frame.rect.centerX - frame.targetRect.centerX, frame.rect.centerY - frame.targetRect.centerY)
    : Number.POSITIVE_INFINITY

  const redProfiles = [
    { name:'self-desktop-center-landscape', viewer:0, role:'player', viewport:{ width:1366, height:768 } },
    { name:'opponent-desktop-center-landscape', viewer:1, role:'player', viewport:{ width:1366, height:768 } },
    { name:'spectator-desktop-center-landscape', viewer:0, role:'spectator', viewport:{ width:1366, height:768 } },
    { name:'self-mobile-wide-center-landscape', viewer:0, role:'player', mobile:true, viewport:{ width:844, height:390 } },
    { name:'opponent-mobile-wide-center-landscape', viewer:1, role:'player', mobile:true, viewport:{ width:844, height:390 } },
    { name:'spectator-mobile-wide-center-landscape', viewer:0, role:'spectator', mobile:true, viewport:{ width:844, height:390 } },
    { name:'self-rotated-portrait-center-landscape', viewer:0, role:'player', mobile:true, canvas:true, viewport:{ width:390, height:844 } },
    { name:'opponent-rotated-portrait-center-landscape', viewer:1, role:'player', mobile:true, canvas:true, viewport:{ width:390, height:844 } },
    { name:'spectator-rotated-portrait-center-landscape', viewer:0, role:'spectator', mobile:true, canvas:true, viewport:{ width:390, height:844 } },
  ]
  const canonicalQualificationProfiles = new Set([
    'self-desktop-center-landscape',
    'spectator-mobile-wide-center-landscape',
  ])
  await page.emulateMedia({ reducedMotion:'no-preference' })
  for (const profile of redProfiles) {
    if (!caseEnabled(profile.name)) continue
    markCaseStarted(profile.name)
    const profileQuery = new URLSearchParams({ hand:'10', trials:'2', myTrials:'1', 'disaster-chain':'1',
      'disaster-viewer':String(profile.viewer), 'disaster-role':profile.role })
    if (profile.mobile) profileQuery.set('mobile', '1')
    if (profile.canvas) profileQuery.set('canvas', '1')
    await load(profile.viewport, profileQuery.toString())
    await assertDisasterTargetReady(profile.name)
    await observeDisasterStarts()
    await armDisasterFrameCapture()
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await waitForArmedDisasterFrame()
    await page.locator('.zone-card-movement .disaster-reveal-card').waitFor({ state:'visible' })
    await assertRootMotionReady(profile.name)
    const center = await sampleDisasterFrame(.45)
    redCheck(`${profile.name}:sample-ready`, usableFrame(center), center,
      { missing:false, missingAnimation:false, motion:{ reduced:false, query:'default' } })
    if (!usableFrame(center)) {
      await page.screenshot({ path:path.join(output, `${profile.name}-missing-sample.png`) })
      continue
    }
    const centerLimit = Math.min(center.viewport.width, center.viewport.height) * .12
    redCheck(profile.name, centerDistance(center) <= centerLimit, centerDistance(center), `<= ${centerLimit}`)
    redCheck(`${profile.name}:8-by-5`, Math.abs(center.cssBox.width / center.cssBox.height - 1.6) <= .04,
      center.cssBox, 'CSS box aspect approximately 8:5')
    redCheck(`${profile.name}:inside`, insideViewport(center), center.rect, 'fully inside visual viewport')
    redCheck(`${profile.name}:target-covered`, center.targetVisibility === 'hidden', center.targetVisibility, 'hidden')
    redCheck(`${profile.name}:flip-has-pixels`, Number(center.backOpacity) + Number(center.frontOpacity) > .2,
      { back:center.backOpacity, front:center.frontOpacity }, 'combined opacity > .2')
    if (profile.role === 'spectator')
      redCheck(`${profile.name}:no-action`, !center.actionPresent, center.actionPresent, false)
    else redCheck(`${profile.name}:action-locked`, center.actionPresent && center.actionDisabled === true,
      { present:center.actionPresent, disabled:center.actionDisabled }, { present:true, disabled:true })
    const faceHold = await sampleDisasterFrame(.68)
    redCheck(`${profile.name}:face-sample-ready`, usableFrame(faceHold), faceHold,
      { missing:false, missingAnimation:false })
    if (usableFrame(faceHold)) redCheck(`${profile.name}:readable-face-hold`, Number(faceHold.frontOpacity) >= .9
      && centerDistance(faceHold) <= centerLimit, { opacity:faceHold.frontOpacity, distance:centerDistance(faceHold) },
      { opacity:'>= .9', distance:`<= ${centerLimit}` })
    if (canonicalDisasterAsset && canonicalQualificationProfiles.has(profile.name) && usableFrame(faceHold))
      redCheck(`${profile.name}:canonical-full-card-contain`, center.backObjectFit === 'contain'
        && faceHold.frontObjectFit === 'contain'
        && faceHold.frontNaturalWidth === canonicalDisasterAsset.expectedNaturalWidth
        && faceHold.frontNaturalHeight === canonicalDisasterAsset.expectedNaturalHeight,
      {
        sha256:canonicalDisasterAsset.sha256,
        backObjectFit:center.backObjectFit,
        frontObjectFit:faceHold.frontObjectFit,
        naturalWidth:faceHold.frontNaturalWidth,
        naturalHeight:faceHold.frontNaturalHeight,
      }, {
        sha256:canonicalDisasterAsset.sha256,
        backObjectFit:'contain',
        frontObjectFit:'contain',
        naturalWidth:canonicalDisasterAsset.expectedNaturalWidth,
        naturalHeight:canonicalDisasterAsset.expectedNaturalHeight,
      })
    if (usableFrame(faceHold))
      await page.screenshot({ path:path.join(output, `${profile.name}-face-hold.png`) })
    const landing = await sampleDisasterFrame(.92)
    redCheck(`${profile.name}:landing-sample-ready`, usableFrame(landing), landing,
      { missing:false, missingAnimation:false })
    if (usableFrame(landing)) redCheck(`${profile.name}:landing`, targetDistance(landing) <= Math.max(24,
      Math.hypot(landing.targetRect?.width ?? 0, landing.targetRect?.height ?? 0) * .4),
      targetDistance(landing), 'near current disaster target')
    await resumeDisaster()
    await page.locator('.zone-card-movement').waitFor({ state:'detached' })
    const settled = await page.evaluate(() => {
      const targets = [...document.querySelectorAll('[data-l12-battle-layout] button.current-disaster-card[data-l12-zone="disaster"]')]
      const target = targets.length === 1 ? targets[0] : null
      const rect = target?.getBoundingClientRect()
      return {
        starts:window.__disasterPresentationStarts.length,
        targetCount:targets.length,
        targetUsable:Boolean(rect && Number.isFinite(rect.width) && Number.isFinite(rect.height)
          && rect.width > 0 && rect.height > 0),
        targetVisibility:target instanceof HTMLElement ? getComputedStyle(target).visibility : 'missing',
        actionDisabled:document.querySelector('[data-tutorial-action="end-turn"]')?.disabled ?? null,
      }
    })
    redCheck(`${profile.name}:once`, settled.starts === 1, settled.starts, 1)
    redCheck(`${profile.name}:settled-target-ready`, settled.targetCount === 1 && settled.targetUsable,
      { count:settled.targetCount, usable:settled.targetUsable }, { count:1, usable:true })
    redCheck(`${profile.name}:target-released`, settled.targetVisibility === 'visible', settled.targetVisibility, 'visible')
    if (profile.role === 'player') redCheck(`${profile.name}:action-released`, settled.actionDisabled === false,
      settled.actionDisabled, false)
  }

  if (caseEnabled('delayed-face-static-cardback-privacy')) {
    markCaseStarted('delayed-face-static-cardback-privacy')
    let releaseDelayedFace
    const delayedFaceGate = new Promise(resolve => { releaseDelayedFace = resolve })
    await page.route('**/*QA-DISASTER-DELAY*', async route => {
      await delayedFaceGate
      await route.fulfill({ contentType:'image/svg+xml', body:'<svg xmlns="http://www.w3.org/2000/svg" width="800" height="500"><rect width="800" height="500" fill="#311018"/><text x="400" y="250" text-anchor="middle" fill="#fff">DISASTER QA</text></svg>' })
    })
    await load({ width:1366, height:768 }, 'hand=10&disaster-chain=1&disaster-delayed-face=1&disaster-viewer=0')
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await page.waitForTimeout(120)
    const preparation = await page.evaluate(() => {
      const roots = [...document.querySelectorAll('.zone-card-movement, .l12-disaster-reveal-holder')]
      const visibleBacks = roots.flatMap(root => [...root.querySelectorAll('.disaster-reveal-back')])
        .filter(node => getComputedStyle(node).visibility !== 'hidden' && getComputedStyle(node).opacity !== '0')
      const target = document.querySelector('[data-l12-battle-layout] button.current-disaster-card[data-l12-zone="disaster"]')
      const action = document.querySelector('[data-tutorial-action="end-turn"]')
      return {
        roots:roots.length,
        visibleOfficialBacks:visibleBacks.filter(node => node.getAttribute('src')?.includes('card-back-disaster.png')).length,
        targetVisibility:target instanceof HTMLElement ? getComputedStyle(target).visibility : 'missing',
        actionDisabled:action instanceof HTMLButtonElement
          ? action.disabled || action.getAttribute('aria-disabled') === 'true' : null,
        movingAnimations:roots.flatMap(root => [...root.getAnimations({ subtree:true })])
          .filter(animation => animation.playState === 'running' && Number(animation.currentTime ?? 0) > 0).length,
      }
    })
    redCheck('delayed-face-static-cardback-privacy', preparation.visibleOfficialBacks === 1
      && preparation.targetVisibility === 'hidden' && preparation.movingAnimations === 0,
    preparation, { visibleOfficialBacks:1, targetVisibility:'hidden', movingAnimations:0 })
    redCheck('delayed-face-action-locked', preparation.actionDisabled === true, preparation.actionDisabled, true)
    await page.screenshot({ path:path.join(output, 'delayed-face-static-cardback-privacy.png') })
    releaseDelayedFace()
    await page.locator('.zone-card-movement .disaster-reveal-card').waitFor({ state:'visible' })
    await resumeDisaster()
    await page.locator('.zone-card-movement').waitFor({ state:'detached' })
    await page.unroute('**/*QA-DISASTER-DELAY*')
  }

  for (const accessibility of [
    { name:'reduced-motion-essential-handoff', query:'hand=10&disaster-chain=1&disaster-viewer=0', reducedMotion:'reduce' },
    { name:'animation-off-essential-handoff', query:'hand=10&disaster-chain=1&disaster-viewer=0&motion=off', reducedMotion:'no-preference' },
  ]) {
    if (!caseEnabled(accessibility.name)) continue
    markCaseStarted(accessibility.name)
    await page.emulateMedia({ reducedMotion:accessibility.reducedMotion })
    await load({ width:1366, height:768 }, accessibility.query)
    await assertDisasterTargetReady(accessibility.name)
    await armDisasterFrameCapture()
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await waitForArmedDisasterFrame()
    await page.locator('.zone-card-movement .disaster-reveal-card').waitFor({ state:'visible' })
    const frame = await sampleDisasterFrame(.5)
    redCheck(`${accessibility.name}:sample-ready`, usableFrame(frame), frame,
      { missing:false, missingAnimation:false })
    if (usableFrame(frame)) {
      redCheck(accessibility.name, frame.keyframes === 2, frame.keyframes, 2)
      redCheck(`${accessibility.name}:target-covered`, frame.targetVisibility === 'hidden', frame.targetVisibility, 'hidden')
    }
    await resumeDisaster()
    await page.locator('.zone-card-movement').waitFor({ state:'detached' })
  }
  await page.emulateMedia({ reducedMotion:'no-preference' })

  if (caseEnabled('same-fact-retransmit-no-replay')) {
    markCaseStarted('same-fact-retransmit-no-replay')
    await load({ width:1366, height:768 }, 'hand=10&disaster-chain=1&disaster-viewer=0')
    await observeDisasterStarts()
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await page.locator('.zone-card-movement .disaster-reveal-card').waitFor({ state:'visible' })
    await resumeDisaster()
    await page.locator('.zone-card-movement').waitFor({ state:'detached' })
    await page.evaluate(() => { window.__disasterFixture.rematerialize(); window.__disasterFixture.revise() })
    await page.waitForTimeout(220)
    const retransmit = await page.evaluate(() => ({ starts:window.__disasterPresentationStarts.length,
      visibleMovements:document.querySelectorAll('.zone-card-movement').length }))
    redCheck('same-fact-retransmit-no-replay', retransmit.starts === 1 && retransmit.visibleMovements === 0,
      retransmit, { starts:1, visibleMovements:0 })
  }

  if (caseEnabled('match-switch-cancels-old-and-baselines-history')) {
    markCaseStarted('match-switch-cancels-old-and-baselines-history')
    await load({ width:1366, height:768 }, 'hand=10&disaster-chain=1&disaster-viewer=0')
    await observeDisasterStarts()
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await page.locator('.zone-card-movement .disaster-reveal-card').waitFor({ state:'visible' })
    await page.evaluate(() => window.__disasterFixture.switchMatch())
    await page.locator('.zone-card-movement').waitFor({ state:'detached' })
    await page.waitForTimeout(160)
    const switched = await page.evaluate(() => ({ starts:window.__disasterPresentationStarts.length,
      visibleMovements:document.querySelectorAll('.zone-card-movement').length,
      state:window.__disasterFixture.state() }))
    redCheck('match-switch-cancels-old-and-baselines-history', switched.starts === 1 && switched.visibleMovements === 0
      && switched.state.events.length === 1, switched, { starts:1, visibleMovements:0, historicalEvents:1 })
  }

  const legacyScenarioEnabled = ['no-trigger-full-reveal-once', 'triggered-effect-after-reveal-release',
    'prompt-before-defers', 'prompt-interrupt-consumes-without-replay'].some(caseEnabled)
  const legacyViewports = caseFilterRaw ? [{ width:844, height:390 }] : viewports
  for (const viewport of legacyScenarioEnabled ? legacyViewports : []) {
    const suffix = `${viewport.width}x${viewport.height}`

    await load(viewport)
    await assertDisasterRail(suffix)
    await page.screenshot({ path: path.join(output, `current-disaster-${suffix}.png`) })

    // No-trigger reveal: the authoritative disaster event must still flip from
    // the disaster back to the public face.
    if (caseEnabled('no-trigger-full-reveal-once')) {
      markCaseStarted('no-trigger-full-reveal-once')
      await page.evaluate(() => window.__disasterFixture.emitReveal(false))
      const noTriggerFlip = page.locator('.zone-card-movement .disaster-reveal-card')
      await noTriggerFlip.waitFor({ state: 'visible' })
      assert.equal(await noTriggerFlip.locator('.disaster-reveal-back').count(), 1, `no-trigger ${suffix} must include the disaster back`)
      assert.equal(await noTriggerFlip.locator('.disaster-reveal-front').count(), 1, `no-trigger ${suffix} must include the disaster face`)
      await assertInsideViewport('.zone-card-movement .disaster-reveal-card', `no-trigger reveal ${suffix}`)
      await page.screenshot({ path: path.join(output, `no-trigger-reveal-${suffix}.png`) })
      await noTriggerFlip.waitFor({ state: 'detached' })
      redCheck('no-trigger-full-reveal-once', true, { back:1, front:1 }, { back:1, front:1 })
    }

    // Triggered reveal: the effect presentation must not cover the flip.  It
    // may begin only after the flip's presentation lane is free.
    if (caseEnabled('triggered-effect-after-reveal-release')) {
      markCaseStarted('triggered-effect-after-reveal-release')
      await load(viewport)
      await page.evaluate(() => window.__disasterFixture.emitReveal(true))
      const triggeredFlip = page.locator('.zone-card-movement .disaster-reveal-card')
      await triggeredFlip.waitFor({ state: 'visible' })
      assert.equal(await page.locator('.public-reveal-animation').count(), 0, `triggered reveal ${suffix} effect must wait for the flip`)
      await page.screenshot({ path: path.join(output, `triggered-reveal-${suffix}.png`) })
      await triggeredFlip.waitFor({ state: 'detached' })
      const triggeredEffect = page.locator('.public-reveal-animation')
      await triggeredEffect.waitFor({ state: 'visible' })
      await assertInsideViewport('.public-reveal-animation', `triggered effect ${suffix}`)
      await page.screenshot({ path: path.join(output, `triggered-effect-${suffix}.png`) })
      redCheck('triggered-effect-after-reveal-release', true, 'effect visible after reveal detached', 'ordered')
    }

    // Existing modal: the reveal is queued and starts only after the modal is
    // gone.  The modal's actual buttons remain usable while it owns the screen.
    if (caseEnabled('prompt-before-defers')) {
      markCaseStarted('prompt-before-defers')
      await load(viewport)
      await page.evaluate(() => window.__disasterFixture.openPrompt())
      const prompt = page.locator('.prompt-panel')
      await prompt.waitFor({ state: 'visible' })
      await assertInsideViewport('.prompt-panel', `pre-existing prompt ${suffix}`)
      await page.evaluate(() => window.__disasterFixture.emitReveal(false))
      await page.waitForTimeout(650)
      assert.equal(await page.locator('.zone-card-movement').count(), 0, `queued reveal ${suffix} must not start below an existing prompt`)
      await page.screenshot({ path: path.join(output, `waiting-behind-prompt-${suffix}.png`) })
      await prompt.getByRole('button', { name: '不发动', exact: true }).click()
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `prompt ${suffix} controls must remain usable`)
      await page.evaluate(() => window.__disasterFixture.closePrompt())
      await page.locator('.prompt-panel').waitFor({ state: 'detached' })
      const deferredFlip = page.locator('.zone-card-movement .disaster-reveal-card')
      await deferredFlip.waitFor({ state: 'visible' })
      await page.screenshot({ path: path.join(output, `deferred-after-prompt-${suffix}.png`) })
      await deferredFlip.waitFor({ state: 'detached' })
      redCheck('prompt-before-defers', true, 'prompt interactive; reveal deferred then shown', 'ordered')
    }

    // New obstruction: an already-running flip yields immediately and is
    // consumed.  Closing the obstruction must not replay it.
    if (caseEnabled('prompt-interrupt-consumes-without-replay')) {
      markCaseStarted('prompt-interrupt-consumes-without-replay')
      await load(viewport)
      await page.evaluate(() => window.__disasterFixture.emitReveal(false))
      const interruptedFlip = page.locator('.zone-card-movement .disaster-reveal-card')
      await interruptedFlip.waitFor({ state: 'visible' })
      await page.evaluate(() => window.__disasterFixture.openPrompt())
      await page.locator('.prompt-panel').waitFor({ state: 'visible' })
      await interruptedFlip.waitFor({ state: 'detached' })
      await page.screenshot({ path: path.join(output, `interrupted-by-prompt-${suffix}.png`) })
      await page.evaluate(() => window.__disasterFixture.closePrompt())
      await page.locator('.prompt-panel').waitFor({ state: 'detached' })
      await page.waitForTimeout(900)
      assert.equal(await page.locator('.zone-card-movement .disaster-reveal-card').count(), 0, `interrupted reveal ${suffix} must not replay after the prompt closes`)
      redCheck('prompt-interrupt-consumes-without-replay', true, 0, 0)
    }
  }

} catch (error) {
  frameworkFailures.push({
    name:error?.name ?? 'Error',
    message:error?.message ?? String(error),
    stack:error?.stack ?? null,
  })
} finally {
  if (browser) {
    try {
      await browser.close()
      report.cleanup.browserClosed = true
    } catch (error) {
      frameworkFailures.push({
        name:error?.name ?? 'BrowserCloseError',
        message:`Browser close failed: ${error?.message ?? String(error)}`,
        stack:error?.stack ?? null,
      })
    }
  }
  const sourcesAfter = Object.fromEntries(Object.entries(sourceBindings).map(([name, file]) => [name, {
    path:file, sha256:sha256(file), bytes:fs.statSync(file).size,
  }]))
  report.sources.after = sourcesAfter
  for (const name of Object.keys(sourcesBefore)) {
    if (sourcesAfter[name].sha256 !== sourcesBefore[name].sha256) frameworkFailures.push({
      name:'SourceChangedError',
      message:`${name} changed during disaster evidence capture`,
      expected:sourcesBefore[name].sha256,
      actual:sourcesAfter[name].sha256,
    })
  }
  report.assertions = redChecks.filter(check => check.passed).length
  report.failures = redChecks.filter(check => !check.passed)
  report.passed = report.failures.length === 0 && frameworkFailures.length === 0
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
}
console.log(JSON.stringify({ output, executableCases:executableCases.length, requestedCases, actualCases,
  checks:redChecks.length, assertions:report.assertions, failures:report.failures.length,
  frameworkFailures:frameworkFailures.length, browserClosed:report.cleanup.browserClosed,
  status:report.passed ? 'passed' : frameworkFailures.length ? 'framework-failure' : 'failed' }, null, 2))
if (!report.passed) process.exitCode = 1
