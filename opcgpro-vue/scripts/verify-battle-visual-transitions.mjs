import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_BATTLE_VISUAL_OUT || path.join(root, 'artifacts', 'battle-visual-transitions')
fs.rmSync(output, { recursive: true, force: true })
fs.mkdirSync(output, { recursive: true })
const harnessFaceIds = ['S01-01M1', 'S01-01M2', 'S01-02M1', 'S01-02M3', 'S01-03M1', 'S01-03M2', 'S01-04M1', 'S01-04M2']
const harnessFaceRedirects = new Map(harnessFaceIds.map(cardId => [
  `/api/site/media/visual-transition/${cardId}.png`,
  `/assets/l12/special/master/${cardId}.png`,
]))

const harnessHtml = `<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module">import { createApp } from 'vue';import Harness from '/scripts/fixtures/BattleVisualTransitionHarness.vue';import '/src/style.css';import '/src/l12/mobileViewport.css';import '/src/l12/motion.css';createApp(Harness).mount('#app')</script></body></html>`
const harnessPlugin = {
  name: 'battle-visual-transition-harness',
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const target = harnessFaceRedirects.get(request.url?.split('?')[0] ?? '')
      if (!target) return next()
      response.statusCode = 302
      response.setHeader('Location', target)
      response.end()
    })
    server.middlewares.use('/__battle-visual-transitions', async (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(await server.transformIndexHtml('/__battle-visual-transitions', harnessHtml))
    })
  },
}

const server = await createServer({ root, plugins: [harnessPlugin], server: { host: '127.0.0.1', port: 0 }, logLevel: 'error' })
let browser
const report = { assertions: 0, screenshots: [], errors: [], profiles: [] }
function ok(value, message) { assert.ok(value, message); report.assertions += 1 }
async function shot(page, name) { const file = path.join(output, `${name}.png`); await page.screenshot({ path: file }); report.screenshots.push(file) }
async function elementShot(page, locator, name) {
  const file = path.join(output, `${name}.png`)
  let box = await locator.boundingBox()
  if (!box) throw new Error(`Cannot capture ${name}: moving element has no bounding box`)
  const viewport = page.viewportSize()
  if (viewport && (box.x + box.width <= 0 || box.y + box.height <= 0 || box.x >= viewport.width || box.y >= viewport.height)) {
    // Compact layout intentionally keeps a fixed-width board. Freeze only the
    // evidence frame and move this same already-asserted flight DOM into view;
    // the queue timer still owns cleanup and the path assertions ran first.
    await locator.evaluate(node => {
      node.getAnimations({ subtree:true }).forEach(animation => animation.pause())
      const host = node.closest('.zone-card-movement')
      if (host instanceof HTMLElement) {
        document.body.appendChild(host)
        Object.assign(host.style, { position:'fixed', left:'0', top:'0', transform:'none' })
      }
      node.style.animation = 'none'
      node.style.transform = 'translate3d(24px,24px,0)'
      node.style.opacity = '1'
    })
    box = await locator.boundingBox()
    if (!box) throw new Error(`Cannot capture ${name}: frozen moving element has no bounding box`)
  }
  const padding = 12
  const x = Math.max(0, box.x - padding)
  const y = Math.max(0, box.y - padding)
  const width = Math.min((viewport?.width ?? box.x + box.width + padding) - x, box.width + padding * 2)
  const height = Math.min((viewport?.height ?? box.y + box.height + padding) - y, box.height + padding * 2)
  if (width <= 0 || height <= 0) throw new Error(`Cannot capture ${name}: evidence crop is outside the viewport`)
  await page.screenshot({ path: file, clip: { x, y, width, height } })
  report.screenshots.push(file)
}
async function invoke(page, method) { await page.evaluate(name => window.__visualTransitionHarness[name](), method) }
async function visibleCount(page, selector) { return page.locator(selector).count() }
async function expectAuthorityFaceFlight(page, profile, instanceId, cardId, label) {
  const selector = `.zone-card-movement[data-movement-instance-id="${instanceId}"]`
  await page.waitForFunction(id => document.querySelector('.zone-card-movement')?.getAttribute('data-movement-instance-id') === id,
    instanceId, { timeout:1800 })
  const movement = page.locator(selector)
  ok(await movement.count() === 1, `${profile}: ${label} must use the event-card renderer instead of cloning final DOM`)
  ok(await movement.locator('.moving-card').isVisible(), `${profile}: ${label} face must be visibly rendered`)
  const source = await movement.locator('img').getAttribute('src')
  ok(source?.includes(`/api/site/media/visual-transition/${cardId}.png`), `${profile}: ${label} must use its corresponding authority face asset`)
  ok(!source?.includes('card-back-official.png'), `${profile}: ${label} must never regress to the library back while flying`)
}
async function waitForMovementQueue(page) {
  await page.waitForFunction(() => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').length === 0,
    null, { timeout:2200 })
}
async function expectConcealedLibraryPile(page, profile, label) {
  const library = page.locator('[data-player-index="0"] [data-l12-zone="library"]')
  const sources = await library.locator('img').evaluateAll(nodes => nodes.map(node => node.getAttribute('src') ?? ''))
  const cssBack = await library.locator('.pile-card.card-back').count() === 1
  const imageBack = sources.length > 0 && sources.every(source => source.includes('card-back-official.png'))
  ok(cssBack || imageBack, `${profile}: ${label} must settle into the board's concealed library pile`)
  ok(sources.every(source => !source.includes('/api/site/media/visual-transition/')), `${profile}: ${label} must not expose an authority face after settling`)
}
async function resetMovementTrace(page) { await page.evaluate(() => { window.__movementTrace = [] }) }
async function waitForMovementTrace(page, count = 1) {
  await page.waitForFunction(expected => (window.__movementTrace?.length ?? 0) >= expected, count, { timeout:6000 })
}
async function expectTrace(page, profile, expected, label) {
  const actual = await page.evaluate(() => window.__movementTrace.slice())
  ok(JSON.stringify(actual) === JSON.stringify(expected), `${profile}: ${label} must preserve event-local order: ${actual.join(',')}`)
}
async function zoneMovementGeometry(page, instanceId, from, to) {
  const selector = `[data-movement-instance-id="${instanceId}"][data-movement-from="${from}"][data-movement-to="${to}"]`
  await page.waitForSelector(selector, { state:'attached', timeout:3000 })
  return page.locator(selector).evaluate(node => ({
    from: node.getAttribute('data-movement-from'),
    to: node.getAttribute('data-movement-to'),
    fromX: Number(node.getAttribute('data-movement-from-x')),
    fromY: Number(node.getAttribute('data-movement-from-y')),
    toX: Number(node.getAttribute('data-movement-to-x')),
    toY: Number(node.getAttribute('data-movement-to-y')),
  }))
}
async function resetMovementGeometryTrace(page) {
  await page.evaluate(() => {
    window.__movementGeometryTrace = []
    window.__movementGeometryKeys = new Set()
    if (window.__movementGeometryObserver) return
    const capture = () => {
      document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').forEach(node => {
        const key = node.getAttribute('data-movement-key') ?? ''
        if (!key || window.__movementGeometryKeys.has(key)) return
        window.__movementGeometryKeys.add(key)
        window.__movementGeometryTrace.push({
          instanceId: node.getAttribute('data-movement-instance-id'),
          from: node.getAttribute('data-movement-from'),
          to: node.getAttribute('data-movement-to'),
          fromX: Number(node.getAttribute('data-movement-from-x')),
          fromY: Number(node.getAttribute('data-movement-from-y')),
          toX: Number(node.getAttribute('data-movement-to-x')),
          toY: Number(node.getAttribute('data-movement-to-y')),
          renderer: node.classList.contains('zone-card-movement') ? 'event-face' : 'source-clone',
          imageSrc: node.querySelector('.moving-card > img')?.getAttribute('src') ?? '',
        })
      })
    }
    window.__movementGeometryObserver = new MutationObserver(capture)
    window.__movementGeometryObserver.observe(document.body, { childList:true, subtree:true })
  })
}
async function movementGeometryTrace(page, count) {
  await page.waitForFunction(expected => (window.__movementGeometryTrace?.length ?? 0) >= expected, count, { timeout:6000 })
  return page.evaluate(() => window.__movementGeometryTrace.slice())
}
async function zoneCenters(page, playerIndex, instanceId) {
  return page.evaluate(({ playerIndex, instanceId }) => {
    const center = selector => {
      const rect = document.querySelector(selector)?.getBoundingClientRect()
      return rect ? { x:rect.left + rect.width / 2, y:rect.top + rect.height / 2, width:rect.width, height:rect.height } : null
    }
    return {
      field: center(`[data-player-index="${playerIndex}"] [data-l12-zone="field"] [data-card-instance-id="${CSS.escape(instanceId)}"]`),
      graveyard: center(`[data-player-index="${playerIndex}"] [data-l12-zone="graveyard"]`),
    }
  }, { playerIndex, instanceId })
}
function expectNear(actual, expected, tolerance, message) {
  ok(Boolean(expected) && Math.hypot(actual.x - expected.x, actual.y - expected.y) <= tolerance,
    `${message}: actual ${actual.x.toFixed(1)},${actual.y.toFixed(1)} expected ${expected?.x.toFixed(1)},${expected?.y.toFixed(1)}`)
}
async function startStateContinuityProbe(page, instanceId, durationMs = 520) {
  await page.evaluate(({ instanceId, durationMs }) => {
    const samples = []
    window.__stateContinuitySamples = samples
    const started = performance.now()
    const visible = element => {
      if (!(element instanceof HTMLElement)) return false
      const style = getComputedStyle(element)
      const rect = element.getBoundingClientRect()
      return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity || 1) > 0
        && rect.width > 0 && rect.height > 0
    }
    const tick = () => {
      const ghost = document.querySelector('.l12-card-state-transition-ghost')
      const target = document.querySelector(`[data-l12-game-stage] [data-card-instance-id="${CSS.escape(instanceId)}"]`)
      samples.push({
        ghost: visible(ghost) && visible(ghost?.firstElementChild),
        target: visible(target),
        direction: ghost instanceof HTMLElement ? `${ghost.dataset.stateFrom}>${ghost.dataset.stateTo}` : '',
      })
      if (performance.now() - started < durationMs) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  }, { instanceId, durationMs })
}
async function stateContinuitySamples(page) {
  return page.evaluate(() => window.__stateContinuitySamples ?? [])
}
function assertStateContinuity(samples, profile, label) {
  ok(samples.length >= 8, `${profile}: ${label} must collect multiple rendered frames`)
  ok(samples.every(sample => sample.ghost || sample.target), `${profile}: ${label} must never render a blank frame between ghost and authority target`)
  ok(samples.some(sample => sample.ghost && !sample.target), `${profile}: ${label} must visibly hand authority to a transition ghost`)
  ok(samples.some(sample => !sample.ghost && sample.target), `${profile}: ${label} must visibly settle on the authority target`)
}
async function resetStateTransitionTrace(page) {
  await page.evaluate(() => {
    window.__stateTransitionTrace = []
    if (window.__stateTransitionObserver) return
    let last = ''
    window.__stateTransitionObserver = new MutationObserver(() => {
      const node = document.querySelector('.l12-card-state-transition-ghost')
      const key = node?.getAttribute('data-visual-transition-key') ?? ''
      if (key && key !== last) window.__stateTransitionTrace.push(`${node.dataset.stateFrom}>${node.dataset.stateTo}`)
      last = key
    })
    window.__stateTransitionObserver.observe(document.body, { childList:true, subtree:true })
  })
}
async function startCardAngleProbe(page, instanceId, durationMs = 520) {
  await page.evaluate(({ instanceId, durationMs }) => {
    const samples = []
    window.__cardAngleSamples = samples
    const started = performance.now()
    const tick = () => {
      const element = document.querySelector(`[data-card-instance-id="${CSS.escape(instanceId)}"]`)
      if (element instanceof HTMLElement) {
        const matrix = new DOMMatrixReadOnly(getComputedStyle(element).transform)
        samples.push(Math.atan2(matrix.b, matrix.a) * 180 / Math.PI)
      }
      if (performance.now() - started < durationMs) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  }, { instanceId, durationMs })
}
function nearAngle(angle, expected) {
  const normalized = ((angle % 360) + 360) % 360
  const target = ((expected % 360) + 360) % 360
  return Math.min(Math.abs(normalized - target), 360 - Math.abs(normalized - target)) <= 4
}
async function setAnimationProgress(locator, progress) {
  await locator.evaluate(async (node, value) => {
    for (let frame = 0; frame < 10 && node.getAnimations().length === 0; frame++) {
      await new Promise(resolve => requestAnimationFrame(resolve))
    }
    const animation = node.getAnimations()[0]
    if (!animation) throw new Error('expected an active movement animation')
    const duration = Number(animation.effect?.getComputedTiming().duration ?? 0)
    animation.pause()
    animation.currentTime = duration * value
  }, progress)
  await locator.page().waitForTimeout(32)
}
async function resumeAnimation(locator) {
  await locator.evaluate(node => node.getAnimations().forEach(animation => animation.play()))
}
async function physicalCenter(locator) {
  const box = await locator.boundingBox()
  if (!box) throw new Error('expected a measurable animation element')
  return { x:box.x + box.width / 2, y:box.y + box.height / 2 }
}
function centerDistance(left, right) { return Math.hypot(left.x - right.x, left.y - right.y) }
async function physicalSize(locator) {
  const box = await locator.boundingBox()
  if (!box) throw new Error('expected a measurable animation element')
  return { width:box.width, height:box.height, ratio:box.width / Math.max(1, box.height) }
}
function assertSameSize(left, right, profile, label, tolerance = 1.5) {
  const widthDrift = Math.abs(left.width - right.width)
  const heightDrift = Math.abs(left.height - right.height)
  const ratioDrift = Math.abs(left.ratio - right.ratio)
  ok(widthDrift <= tolerance && heightDrift <= tolerance && ratioDrift <= .04,
    `${profile}: ${label} size must match; drift=${widthDrift.toFixed(2)}x${heightDrift.toFixed(2)}px ratio=${ratioDrift.toFixed(3)}`)
}
async function assertGhostGeometry(locator, profile, label) {
  const geometry = await locator.evaluate(node => {
    const matrix = new DOMMatrixReadOnly(getComputedStyle(node).transform)
    const logicalWidth = Math.abs(matrix.a) * node.offsetWidth + Math.abs(matrix.c) * node.offsetHeight
    const logicalHeight = Math.abs(matrix.b) * node.offsetWidth + Math.abs(matrix.d) * node.offsetHeight
    const rotated = document.documentElement.dataset.l12Rotated === 'true'
    const rect = node.getBoundingClientRect()
    return {
      scaleX:Math.hypot(matrix.a, matrix.b), scaleY:Math.hypot(matrix.c, matrix.d),
      expectedWidth:rotated ? logicalHeight : logicalWidth,
      expectedHeight:rotated ? logicalWidth : logicalHeight,
      actualWidth:rect.width, actualHeight:rect.height,
    }
  })
  const scaleDrift = Math.abs(geometry.scaleX - geometry.scaleY)
  const widthDrift = Math.abs(geometry.actualWidth - geometry.expectedWidth)
  const heightDrift = Math.abs(geometry.actualHeight - geometry.expectedHeight)
  const actualRatio = geometry.actualWidth / Math.max(1, geometry.actualHeight)
  const expectedRatio = geometry.expectedWidth / Math.max(1, geometry.expectedHeight)
  ok(scaleDrift <= Math.max(.025, Math.max(geometry.scaleX, geometry.scaleY) * .025)
      && widthDrift <= 1 && heightDrift <= 1 && Math.abs(actualRatio - expectedRatio) <= .025,
    `${profile}: ${label} must preserve width/height/aspect; axes=${geometry.scaleX.toFixed(3)},${geometry.scaleY.toFixed(3)} size-drift=${widthDrift.toFixed(2)}x${heightDrift.toFixed(2)}px`)
}
async function finishAnimationFast(locator) {
  await locator.evaluate(node => node.getAnimations().forEach(animation => {
    animation.playbackRate = 20
    animation.play()
  }))
}
async function verifyStateGeometry(page, profile, method, from, to, jitter = false) {
  await invoke(page, method)
  const ghost = page.locator(`.l12-card-state-transition-ghost[data-state-from="${from}"][data-state-to="${to}"]`)
  await ghost.waitFor({ state:'attached', timeout:1500 })
  await setAnimationProgress(ghost, .5)
  await assertGhostGeometry(ghost, profile, `${from}→${to} at 50%`)
  if (jitter) {
    const key = await ghost.getAttribute('data-visual-transition-key')
    const before = await physicalSize(ghost)
    await page.evaluate(() => window.dispatchEvent(new Event('l12-viewport-change')))
    await page.waitForTimeout(32)
    ok(await ghost.count() === 1 && await ghost.getAttribute('data-visual-transition-key') === key,
      `${profile}: viewport jitter must preserve the in-flight ${from}→${to} state ghost`)
    assertSameSize(await physicalSize(ghost), before, profile, `${from}→${to} viewport-jitter frame`, .5)
  }
  await setAnimationProgress(ghost, .999)
  await assertGhostGeometry(ghost, profile, `${from}→${to} at 99.9%`)
  const ghostEnd = await physicalSize(ghost)
  const target = page.locator('[data-card-instance-id="mover-1"]')
  const targetEnd = await physicalSize(target)
  assertSameSize(ghostEnd, targetEnd, profile, `${from}→${to} final ghost/authority handoff`)
  await finishAnimationFast(ghost)
  await page.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout:1500 })
  assertSameSize(await physicalSize(target), targetEnd, profile, `${from}→${to} post-handoff authority`)
  ok(await target.evaluate(node => node.style.transition === ''), `${profile}: ${from}→${to} must restore authority transition style`)
}
async function verifyMovementGeometry(page, profile, jitter = false) {
  await invoke(page, 'move')
  await page.waitForFunction(() => (document.querySelector('.l12-zone-flight-ghost')?.getAnimations().length ?? 0) > 0,
    null, { timeout:1500 })
  const ghost = page.locator('.l12-zone-flight-ghost')
  await setAnimationProgress(ghost, .5)
  await assertGhostGeometry(ghost, profile, 'state→movement flight at 50%')
  if (jitter) {
    const key = await ghost.getAttribute('data-movement-key')
    const before = await physicalSize(ghost)
    await page.evaluate(() => window.dispatchEvent(new Event('l12-viewport-change')))
    await page.waitForTimeout(32)
    ok(await ghost.count() === 1 && await ghost.getAttribute('data-movement-key') === key,
      `${profile}: viewport jitter must preserve the state→movement flight`)
    assertSameSize(await physicalSize(ghost), before, profile, 'state→movement viewport-jitter frame', .5)
  }
  await setAnimationProgress(ghost, .999)
  await assertGhostGeometry(ghost, profile, 'state→movement flight at 99.9%')
  const ghostEnd = await physicalSize(ghost)
  const target = page.locator('[data-card-instance-id="mover-1"]')
  const targetEnd = await physicalSize(target)
  assertSameSize(ghostEnd, targetEnd, profile, 'state→movement final ghost/authority handoff')
  await finishAnimationFast(ghost)
  await waitForMovementQueue(page)
  assertSameSize(await physicalSize(target), targetEnd, profile, 'state→movement post-handoff authority')
  ok(await target.evaluate(node => node.style.transition === ''), `${profile}: movement must restore authority transition style`)
}

try {
  await server.listen()
  const address = server.httpServer.address()
  const port = typeof address === 'object' && address ? address.port : 0
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const profile of (process.env.L12_VISUAL_ROTATED_ONLY ? [] : [
    { name: 'desktop', viewport: { width: 1920, height: 1080 }, viewer: 0 },
    { name: 'mobile-landscape', viewport: { width: 844, height: 390 }, isMobile: true, hasTouch: true, viewer: 0 },
  ])) {
    const context = await browser.newContext({ viewport: profile.viewport, isMobile: profile.isMobile, hasTouch: profile.hasTouch })
    const page = await context.newPage()
    page.on('pageerror', error => report.errors.push(`${profile.name}: ${error.message}`))
    await page.goto(`http://127.0.0.1:${port}/__battle-visual-transitions?viewer=${profile.viewer}`, { waitUntil: 'networkidle' })
    await page.waitForSelector('[data-l12-game-stage]')
    await startStateContinuityProbe(page, 'mover-1')
    await invoke(page, 'tap')
    await page.waitForTimeout(40)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 1, `${profile.name}: active→rested must have one transition ghost`)
    ok(await page.locator('.l12-card-state-transition-ghost').evaluate(node => getComputedStyle(node.firstElementChild).visibility === 'visible'), `${profile.name}: active→rested ghost card must not inherit hidden authority visibility`)
    ok(await page.locator('.l12-card-state-transition-ghost[data-state-from="active"][data-state-to="rested"]').count() === 1, `${profile.name}: active→rested direction must stay explicit`)
    ok(await page.locator('[data-card-instance-id="mover-1"]').evaluate(node => node.style.visibility === 'hidden'), `${profile.name}: authority target must hand off invisibly during active→rested`)
    await shot(page, `${profile.name}-01-active-to-rested-mid`)
    await page.waitForTimeout(460)
    assertStateContinuity(await stateContinuitySamples(page), profile.name, 'active→rested')
    ok(await page.locator('[data-card-instance-id="mover-1"].tapped').count() === 1, `${profile.name}: final rested DOM must match authority`)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0, `${profile.name}: active→rested ghost must clean up`)

    await startStateContinuityProbe(page, 'mover-1')
    await invoke(page, 'ready')
    await page.waitForTimeout(40)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 1, `${profile.name}: rested→active must animate`)
    ok(await page.locator('.l12-card-state-transition-ghost').evaluate(node => getComputedStyle(node.firstElementChild).visibility === 'visible'), `${profile.name}: rested→active ghost card must not inherit hidden authority visibility`)
    ok(await page.locator('.l12-card-state-transition-ghost[data-state-from="rested"][data-state-to="active"]').count() === 1, `${profile.name}: rested→active direction must stay explicit`)
    await shot(page, `${profile.name}-01b-rested-to-active-mid`)
    await page.waitForTimeout(460)
    assertStateContinuity(await stateContinuitySamples(page), profile.name, 'rested→active')
    ok(await page.locator('[data-card-instance-id="mover-1"]:not(.tapped)').count() === 1, `${profile.name}: final active DOM must match authority`)

    await startStateContinuityProbe(page, 'mover-1', 1100)
    await invoke(page, 'tap')
    await page.waitForTimeout(70)
    await invoke(page, 'ready')
    await page.waitForTimeout(360)
    ok(await page.locator('.l12-card-state-transition-ghost[data-state-from="rested"][data-state-to="active"]').count() === 1, `${profile.name}: rapid reverse must queue and visibly start rested→active after active→rested`)
    ok(await page.locator('.l12-card-state-transition-ghost').evaluate(node => getComputedStyle(node.firstElementChild).visibility === 'visible'), `${profile.name}: rapid reverse ghost must remain visible even when captured from a hidden authority target`)
    await shot(page, `${profile.name}-01c-rapid-rest-ready-handoff`)
    await page.waitForTimeout(680)
    const rapidSamples = await stateContinuitySamples(page)
    assertStateContinuity(rapidSamples, profile.name, 'rapid active→rested→active')
    ok(new Set(rapidSamples.map(sample => sample.direction).filter(Boolean)).has('active>rested')
      && new Set(rapidSamples.map(sample => sample.direction).filter(Boolean)).has('rested>active'), `${profile.name}: rapid reverse must render both authoritative directions`)
    ok(await page.locator('[data-card-instance-id="mover-1"]:not(.tapped)').count() === 1
      && await visibleCount(page, '.l12-card-state-transition-ghost') === 0, `${profile.name}: rapid reverse must settle once on the active authority DOM`)

    await resetStateTransitionTrace(page)
    await invoke(page, 'attackWithDuplicateRestSnapshots')
    const combinedAttack = page.locator('.l12-card-state-transition-ghost[data-motion-kind="attack-rest"]')
    await combinedAttack.waitFor({ state:'attached', timeout:1500 })
    ok(await combinedAttack.count() === 1, `${profile.name}: one attack transaction must create one combined attack-rest ghost`)
    ok(await combinedAttack.getAttribute('data-attack-sequence') !== null, `${profile.name}: combined attack-rest ghost must retain its authority attack sequence`)
    ok(await page.locator('[data-card-instance-id="mover-1"]').evaluate(node => node.closest('.formation-slot')?.getAnimations().length ?? -1) === 0,
      `${profile.name}: combat layer must not animate the hidden authority slot a second time`)
    const authorityCenter = await physicalCenter(page.locator('[data-card-instance-id="mover-1"]'))
    await setAnimationProgress(combinedAttack, .48)
    const lungeCenter = await physicalCenter(combinedAttack)
    ok(centerDistance(authorityCenter, lungeCenter) >= 6,
      `${profile.name}: combined attack-rest ghost must visibly lunge toward the defender, drift=${centerDistance(authorityCenter, lungeCenter).toFixed(1)}px`)
    await assertGhostGeometry(combinedAttack, profile.name, 'combined attack-rest at 48%')
    await shot(page, `${profile.name}-01d-combined-attack-rest-mid`)
    await finishAnimationFast(combinedAttack)
    await page.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout:1500 })
    let stateTrace = await page.evaluate(() => window.__stateTransitionTrace.slice())
    ok(stateTrace.filter(direction => direction === 'active>rested').length === 1, `${profile.name}: one attack transaction must rest its attacker exactly once: ${stateTrace.join(',')}`)
    ok(!stateTrace.includes('rested>active'), `${profile.name}: same-revision object replacement must not fabricate a ready transition`)
    ok(await page.locator('[data-card-instance-id="mover-1"].tapped').count() === 1, `${profile.name}: duplicate attack snapshots must settle on the rested authority state`)

    await invoke(page, 'ready')
    await page.waitForTimeout(500)
    await startCardAngleProbe(page, 'rested-defender')
    await invoke(page, 'attackWithDuplicateRestSnapshots')
    await page.waitForTimeout(1300)
    stateTrace = await page.evaluate(() => window.__stateTransitionTrace.slice())
    ok(stateTrace.filter(direction => direction === 'active>rested').length === 2, `${profile.name}: a later real ready→attack transaction must still animate once`)
    const restedAngles = await page.evaluate(() => window.__cardAngleSamples.slice())
    ok(restedAngles.length >= 8 && restedAngles.every(angle => nearAngle(angle, 90)), `${profile.name}: rested defender must preserve its authority angle through every attack-impact sample`)

    await startCardAngleProbe(page, 'active-defender')
    await invoke(page, 'attackActiveTarget')
    await page.waitForTimeout(560)
    const activeAngles = await page.evaluate(() => window.__cardAngleSamples.slice())
    ok(activeAngles.length >= 8 && activeAngles.every(angle => nearAngle(angle, 0)), `${profile.name}: active defender must preserve its authority angle during the combined attacker animation`)

    await resetStateTransitionTrace(page)
    await invoke(page, 'reconnectWithHistoricalReady')
    await page.waitForTimeout(180)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0, `${profile.name}: reconnect baseline must not backfill a historical ready animation`)
    ok(await page.locator('[data-card-instance-id="mover-1"]:not(.tapped)').count() === 1, `${profile.name}: reconnect baseline must expose only the current authority state`)
    await invoke(page, 'reconnectWithHistoricalAttack')
    await page.waitForTimeout(180)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0,
      `${profile.name}: reconnect attack history must establish a baseline without replaying attack-rest motion`)

    await page.evaluate(() => window.__visualTransitionHarness.setReplay(8))
    await invoke(page, 'replaySeekBackwardWithHistoricalAttack')
    await page.waitForTimeout(180)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0,
      `${profile.name}: backward replay seek must establish a baseline without replaying attack-rest motion`)
    await page.evaluate(() => window.__visualTransitionHarness.setReplay(null))

    await invoke(page, 'move')
    await page.waitForTimeout(60)
    ok(await visibleCount(page, '.l12-zone-flight-ghost') === 1, `${profile.name}: field move must retain a moving visual`)
    await shot(page, `${profile.name}-02-field-move-mid`)
    await page.waitForTimeout(560)
    ok(await page.locator('[data-card-instance-id="mover-1"]').count() === 1, `${profile.name}: moved instance must remain unique`)

    await invoke(page, 'swap')
    await page.waitForTimeout(60)
    ok(await visibleCount(page, '.l12-zone-flight-ghost') === 1, `${profile.name}: same-frame multi-move must start a tracked queue`)
    await page.waitForTimeout(1050)
    ok(await page.locator('[data-card-instance-id="mover-1"]').count() === 1 && await page.locator('[data-card-instance-id="mover-2"]').count() === 1, `${profile.name}: both swapped instances must survive without replacement keys`)

    await invoke(page, 'attach')
    await page.waitForTimeout(60)
    ok(await visibleCount(page, '.l12-zone-flight-ghost') === 1, `${profile.name}: hand→attach must fly from the real source`)
    await shot(page, `${profile.name}-03-attach-mid`)
    await page.waitForTimeout(560)
    ok(await page.locator('[data-attached-card-instance-ids~="hand-squire"]').count() === 1, `${profile.name}: attachment target must take over by instance identity`)

    await invoke(page, 'prepareRobin')
    await page.waitForTimeout(30)
    await invoke(page, 'robin')
    await page.waitForTimeout(60)
    ok(await visibleCount(page, '.l12-zone-flight-ghost') === 1, `${profile.name}: Robin Hood summon must retain the selected Squire visual`)
    await page.waitForTimeout(560)
    ok(await page.locator('[data-card-instance-id="robin-squire"]').count() === 1, `${profile.name}: Robin Hood destination must own the same instance`)

    await invoke(page, 'triggerNuada')
    await page.waitForTimeout(80)
    ok(await page.locator('.public-reveal-animation img[alt="银臂努阿达"]').count() === 1, `${profile.name}: Nuada self-trigger must enter shared card-art presentation`)
    ok(await page.locator('.public-reveal-animation img[alt="侍从骑士"]').count() === 0, `${profile.name}: effect target must not masquerade as a source card`)
    await shot(page, `${profile.name}-04-nuada-trigger`)
    await invoke(page, 'openPrompt')
    await page.waitForTimeout(320)
    ok(await page.locator('.public-reveal-animation').count() === 0, `${profile.name}: blocking prompt must cover and cancel an already-started presentation`)
    await invoke(page, 'closePrompt')
    await page.waitForTimeout(160)
    ok(await page.locator('.public-reveal-animation').count() === 0, `${profile.name}: interrupted presentation must not requeue`)

    await page.evaluate(() => window.__visualTransitionHarness.setReplay(8))
    await invoke(page, 'effectThenMove')
    await page.waitForTimeout(90)
    ok(await page.locator('.public-reveal-animation img[alt="银臂努阿达"]').count() === 1, `${profile.name}: lower-sequence effect source must present first`)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: higher-sequence movement must not overtake effect source`)
    await page.waitForTimeout(230)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: movement must continue after source release`)
    await page.waitForTimeout(650)

    await invoke(page, 'openPrompt')
    await invoke(page, 'effectThenMove')
    await page.waitForTimeout(90)
    ok(await page.locator('.public-reveal-animation').count() === 0 && await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: modal must pause the shared sequence lane`)
    await invoke(page, 'closePrompt')
    await page.waitForTimeout(180)
    ok(await page.locator('.public-reveal-animation img[alt="银臂努阿达"]').count() === 1, `${profile.name}: modal resume must restart at the lower sequence without deadlock`)
    await page.waitForTimeout(1050)

    await invoke(page, 'costLeave')
    await page.locator('.l12-zone-flight-ghost,.zone-card-movement').first().waitFor({ state:'visible', timeout:1500 })
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: ordinary field leave cost must retain a moving card visual`)
    await shot(page, `${profile.name}-05-field-cost-leave-mid`)
    await page.waitForTimeout(650)
    ok(await page.locator('[data-card-instance-id="mover-2"]').count() <= 1, `${profile.name}: leave transition must not duplicate the instance`)

    await page.evaluate(() => window.__visualTransitionHarness.setReplay(null))
    await page.evaluate(() => {
      window.__movementTrace = []
      let last = ''
      new MutationObserver(() => {
        const node = document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')
        const current = node?.getAttribute('data-movement-instance-id') ?? ''
        if (current && current !== last) window.__movementTrace.push(current)
        last = current
      }).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['data-movement-instance-id'] })
    })
    await resetMovementTrace(page)
    await invoke(page, 'millOneForTop')
    await expectAuthorityFaceFlight(page, profile.name, 'top-single', 'S01-01M1', 'single mill')
    await shot(page, `${profile.name}-06-single-mill-face-mid`)
    await elementShot(page, page.locator('.zone-card-movement .moving-card'), `${profile.name}-06b-single-mill-face-closeup`)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['top-single'], 'single mill')

    await resetMovementTrace(page)
    await invoke(page, 'returnOneTop')
    await expectAuthorityFaceFlight(page, profile.name, 'top-single', 'S01-01M1', 'single grave-to-library-top return')
    await shot(page, `${profile.name}-07-single-return-top-face-mid`)
    await elementShot(page, page.locator('.zone-card-movement .moving-card'), `${profile.name}-07b-single-return-top-face-closeup`)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['top-single'], 'single grave-to-library-top return')
    await expectConcealedLibraryPile(page, profile.name, 'single top return')

    const topMany = [['top-card-1', 'S01-01M2'], ['top-card-2', 'S01-02M1'], ['top-card-3', 'S01-02M3']]
    await resetMovementTrace(page)
    await invoke(page, 'millManyForTop')
    for (const [instanceId, cardId] of topMany) {
      await expectAuthorityFaceFlight(page, profile.name, instanceId, cardId, `multi-mill ${instanceId}`)
      ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: multi-mill cards must never overlap`)
      if (instanceId === 'top-card-1') await shot(page, `${profile.name}-08-multi-mill-face-mid`)
    }
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, topMany.map(([instanceId]) => instanceId), 'multi-mill')

    await resetMovementTrace(page)
    await invoke(page, 'returnManyTop')
    for (const [instanceId, cardId] of topMany) {
      await expectAuthorityFaceFlight(page, profile.name, instanceId, cardId, `multi grave-to-library-top return ${instanceId}`)
      ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: top-return cards must never overlap`)
      if (instanceId === 'top-card-1') {
        await shot(page, `${profile.name}-09-multi-return-top-face-mid`)
        await elementShot(page, page.locator('.zone-card-movement .moving-card'), `${profile.name}-09b-multi-return-top-face-closeup`)
      }
    }
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, topMany.map(([instanceId]) => instanceId), 'multi grave-to-library-top return')
    await expectConcealedLibraryPile(page, profile.name, 'multi top return')

    const bottomMany = [['mill-card-1', 'S01-03M1'], ['mill-card-2', 'S01-03M2'], ['mill-card-3', 'S01-04M1']]
    await resetMovementTrace(page)
    await invoke(page, 'millMany')
    for (const [instanceId, cardId] of bottomMany) {
      await expectAuthorityFaceFlight(page, profile.name, instanceId, cardId, `bottom-batch mill ${instanceId}`)
      ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: bottom-batch mill cards must never overlap`)
      if (instanceId === 'mill-card-1') await shot(page, `${profile.name}-10-multi-mill-for-bottom-face-mid`)
    }
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, bottomMany.map(([instanceId]) => instanceId), 'bottom-batch multi-mill')
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '4'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="磨牌三"]').count() === 1, `${profile.name}: final graveyard count and top card must match the ordered mills`)

    await resetMovementTrace(page)
    await invoke(page, 'returnMilled')
    for (const [instanceId, cardId] of bottomMany) {
      await expectAuthorityFaceFlight(page, profile.name, instanceId, cardId, `multi grave-to-library-bottom return ${instanceId}`)
      ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: bottom-return cards must never overlap`)
      if (instanceId === 'mill-card-1') {
        await shot(page, `${profile.name}-11-multi-return-bottom-face-mid`)
        await elementShot(page, page.locator('.zone-card-movement .moving-card'), `${profile.name}-11b-multi-return-bottom-face-closeup`)
      }
    }
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, bottomMany.map(([instanceId]) => instanceId), 'multi grave-to-library-bottom return')
    await expectConcealedLibraryPile(page, profile.name, 'multi bottom return')
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '1'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt^="磨牌"]').count() === 0, `${profile.name}: returned cards must leave the final graveyard without changing the unrelated card`)

    await invoke(page, 'duplicateDiscardSnapshots')
    await page.waitForTimeout(70)
    const rapidDiscardCount = await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement')
    ok(rapidDiscardCount === 1, `${profile.name}: rapid discard snapshot must start exactly one movement (found ${rapidDiscardCount})`)
    await page.waitForTimeout(520)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: reentrant snapshots must not replay the same discard fact`)
    await invoke(page, 'returnDuplicateCard')
    await expectAuthorityFaceFlight(page, profile.name, 'duplicate-discard', 'S01-04M2', 'single grave-to-library-bottom return')
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: a later real movement of the same instance must not be deduplicated`)
    await waitForMovementQueue(page)
    await expectConcealedLibraryPile(page, profile.name, 'single bottom return')
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '1'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="佣兵部队"]').count() === 0, `${profile.name}: real second movement must match the final library state`)

    await resetMovementTrace(page)
    await invoke(page, 'playPlainEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['plain-entrant'], 'ordinary legion entry')

    await resetMovementTrace(page)
    await resetMovementGeometryTrace(page)
    const sameRevisionAnchors = await zoneCenters(page, 1, 'plain-entrant')
    await invoke(page, 'sameRevisionLeaveAndReenter')
    if (profile.name === 'desktop') {
      await zoneMovementGeometry(page, 'plain-entrant', 'field', 'graveyard')
      await shot(page, 'desktop-12-same-revision-field-to-grave-mid')
      await zoneMovementGeometry(page, 'plain-entrant', 'graveyard', 'field')
      await shot(page, 'desktop-13-same-revision-grave-to-field-mid')
    }
    const sameRevisionGeometry = await movementGeometryTrace(page, 2)
    const [sameRevisionLeave, sameRevisionReenter] = sameRevisionGeometry
    ok(sameRevisionLeave.from === 'field' && sameRevisionLeave.to === 'graveyard', `${profile.name}: same-revision first leg must be field>graveyard`)
    ok(Math.hypot(sameRevisionLeave.toX - sameRevisionLeave.fromX, sameRevisionLeave.toY - sameRevisionLeave.fromY) > 40,
      `${profile.name}: same-revision first leg must have a non-zero route`)
    expectNear({ x:sameRevisionLeave.fromX, y:sameRevisionLeave.fromY }, sameRevisionAnchors.field, 4,
      `${profile.name}: same-revision first leg must start at the field card`)
    expectNear({ x:sameRevisionLeave.toX, y:sameRevisionLeave.toY }, sameRevisionAnchors.graveyard, 4,
      `${profile.name}: same-revision first leg must end at the graveyard anchor`)
    ok(sameRevisionReenter.from === 'graveyard' && sameRevisionReenter.to === 'field', `${profile.name}: same-revision second leg must be graveyard>field`)
    ok(sameRevisionReenter.renderer === 'event-face'
      && sameRevisionReenter.imageSrc.includes('/api/site/media/visual-transition/S01-02M1.png'),
    `${profile.name}: same-revision re-entry must render the public event face from the graveyard anchor`)
    ok(Math.hypot(sameRevisionReenter.toX - sameRevisionReenter.fromX, sameRevisionReenter.toY - sameRevisionReenter.fromY) > 40,
      `${profile.name}: same-revision second leg must have a non-zero route`)
    expectNear({ x:sameRevisionReenter.fromX, y:sameRevisionReenter.fromY }, sameRevisionAnchors.graveyard, 4,
      `${profile.name}: same-revision second leg must start at the graveyard anchor`)
    expectNear({ x:sameRevisionReenter.toX, y:sameRevisionReenter.toY }, sameRevisionAnchors.field, 4,
      `${profile.name}: same-revision second leg must end at the field card`)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['plain-entrant', 'plain-entrant'], 'same-revision field-to-grave-to-field chain')

    await resetMovementTrace(page)
    await invoke(page, 'leavePlainEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['plain-entrant'], 'adjacent-revision field-to-grave transition')
    await resetMovementTrace(page)
    await invoke(page, 'reenterPlainEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['plain-entrant'], 'adjacent-revision grave-to-field transition')

    await resetMovementTrace(page)
    await invoke(page, 'interleavedDifferentInstanceChain')
    await waitForMovementTrace(page, 3)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['plain-entrant', 'interleaved-entrant', 'plain-entrant'], 'same-revision interleaved instance chain')

    await resetMovementTrace(page)
    await invoke(page, 'playTriggeredEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['triggered-entrant'], 'play plus enter-effect descriptions of one authority migration')
    await page.waitForFunction(() => !document.querySelector('.public-reveal-animation'), null, { timeout:5000 })
    await resetMovementTrace(page)
    await invoke(page, 'repeatTriggeredEnterSnapshot')
    await page.waitForTimeout(700)
    ok((await page.evaluate(() => window.__movementTrace.slice())).length === 0
      && await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0,
    `${profile.name}: response/object refresh at a later revision must not replay an already settled entry`)

    await resetMovementTrace(page)
    await invoke(page, 'playNegatedEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['negated-entrant'], 'current-view entry with a negated trigger')
    await page.waitForFunction(() => !document.querySelector('.public-reveal-animation'), null, { timeout:5000 })

    await page.evaluate(() => window.__visualTransitionHarness.setReplay(8))
    await resetMovementTrace(page)
    await invoke(page, 'freeTriggeredEntrant')
    await waitForMovementTrace(page)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['free-entrant'], 'replay-speed free entry with put and enter descriptions')
    await page.evaluate(() => window.__visualTransitionHarness.setReplay(null))

    await resetMovementTrace(page)
    await invoke(page, 'rapidDifferentEntrants')
    await waitForMovementTrace(page, 2)
    await waitForMovementQueue(page)
    await expectTrace(page, profile.name, ['rapid-entrant-a', 'rapid-entrant-b'], 'rapid different-instance entries')

    await page.evaluate(() => { window.__movementTrace = [] })
    await invoke(page, 'pharaohFestivalChain')
    await page.waitForFunction(() => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').length > 0, null, { timeout:1500 })
    await page.waitForTimeout(2400)
    await page.waitForFunction(() => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').length === 0, null, { timeout:1500 })
    const festivalTrace = await page.evaluate(() => window.__movementTrace.filter(id => id.startsWith('pharaoh-') || id.startsWith('festival-')))
    ok(festivalTrace.filter(id => id === 'pharaoh-festival').length === 1, `${profile.name}: Pharaoh Festival hand play must animate once: ${festivalTrace.join(',')}`)
    ok(festivalTrace.filter(id => id === 'festival-hand-card').length === 1, `${profile.name}: Pharaoh Festival hand choice must animate once`)
    ok(festivalTrace.filter(id => id === 'festival-grave-card').length === 1, `${profile.name}: Pharaoh Festival grave choice must animate once`)
    ok(festivalTrace.length === 3, `${profile.name}: rapid Festival snapshots must not reclaim an already queued movement: ${festivalTrace.join(',')}`)
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="卡诺匹斯罐 一"]').count() === 1, `${profile.name}: Festival discard must settle in graveyard exactly once`)

    await invoke(page, 'beginFinnOptionalReady')
    await page.waitForTimeout(80)
    await invoke(page, 'declineFinnOptionalReady')
    await page.waitForTimeout(160)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0, `${profile.name}: declining optional ready must not preview or roll back a false state transition`)
    ok(await page.locator('[data-card-instance-id="finn-optional-ready"].tapped').count() === 1, `${profile.name}: declining optional ready must preserve the rested authority state`)
    if (profile.name === 'desktop') {
      const opponentPage = await context.newPage()
      opponentPage.on('pageerror', error => report.errors.push(`opponent-view: ${error.message}`))
      await opponentPage.goto(`http://127.0.0.1:${port}/__battle-visual-transitions?viewer=1`, { waitUntil:'networkidle' })
      await opponentPage.waitForSelector('[data-l12-game-stage]')
      await opponentPage.evaluate(() => {
        window.__movementTrace = []
        let last = ''
        new MutationObserver(() => {
          const node = document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')
          const current = node?.getAttribute('data-movement-instance-id') ?? ''
          if (current && current !== last) window.__movementTrace.push(current)
          last = current
        }).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['data-movement-instance-id'] })
      })
      await invoke(opponentPage, 'playNegatedEntrant')
      await waitForMovementTrace(opponentPage)
      await waitForMovementQueue(opponentPage)
      await expectTrace(opponentPage, 'opponent-view', ['negated-entrant'], 'opposite-player entry with a negated trigger')
      await opponentPage.close()
    }
    report.profiles.push(profile.name)
    await context.close()
  }
  for (const profile of [
    { name:'mobile-landscape-geometry', viewport:{ width:844, height:390 } },
    { name:'mobile-portrait-geometry', viewport:{ width:390, height:844 } },
  ]) {
    const context = await browser.newContext({ viewport:profile.viewport, isMobile:true, hasTouch:true })
    const page = await context.newPage()
    page.on('pageerror', error => report.errors.push(`${profile.name}: ${error.message}`))
    await page.goto(`http://127.0.0.1:${port}/__battle-visual-transitions?viewer=0&landscape=1`, { waitUntil:'networkidle' })
    await page.waitForSelector('[data-l12-game-stage]')
    await page.evaluate(() => window.__visualTransitionHarness.setReplay(.1))
    await page.waitForTimeout(32)
    await verifyStateGeometry(page, profile.name, 'tap', 'active', 'rested', true)
    await verifyStateGeometry(page, profile.name, 'ready', 'rested', 'active')
    // No delay is inserted between this state handoff and the movement event:
    // this is the production chain that previously captured a transitional box.
    await verifyStateGeometry(page, profile.name, 'tap', 'active', 'rested')
    await verifyMovementGeometry(page, profile.name, true)
    await shot(page, `${profile.name}-state-movement-size-handoff`)
    report.profiles.push(profile.name)
    await context.close()
  }
  const rotatedContext = await browser.newContext({ viewport:{ width:390, height:844 }, isMobile:true, hasTouch:true })
  const rotatedPage = await rotatedContext.newPage()
  rotatedPage.on('pageerror', error => report.errors.push(`mobile-portrait-rotated: ${error.message}`))
  await rotatedPage.goto(`http://127.0.0.1:${port}/__battle-visual-transitions?viewer=0&landscape=1`, { waitUntil:'networkidle' })
  await rotatedPage.waitForSelector('html[data-l12-rotated="true"] [data-l12-game-stage]')
  ok(await rotatedPage.locator('#l12-landscape-teleports').count() === 1,
    'mobile-portrait-rotated: logical canvas must expose one shared animation host')

  await resetStateTransitionTrace(rotatedPage)
  await invoke(rotatedPage, 'tap')
  const rotatedStateGhost = rotatedPage.locator('.l12-card-state-transition-ghost')
  await rotatedStateGhost.waitFor({ state:'attached', timeout:1500 })
  ok(await rotatedStateGhost.evaluate(node => node.parentElement?.id) === 'l12-landscape-teleports',
    'mobile-portrait-rotated: state ghost must share the transformed logical canvas')
  await setAnimationProgress(rotatedStateGhost, 0)
  const rotatedStateStart = await physicalCenter(rotatedStateGhost)
  const rotatedStateTarget = await physicalCenter(rotatedPage.locator('[data-card-instance-id="mover-1"]'))
  ok(centerDistance(rotatedStateStart, rotatedStateTarget) <= 6,
    `mobile-portrait-rotated: state ghost must start on the physical card, drift=${centerDistance(rotatedStateStart, rotatedStateTarget).toFixed(1)}px`)
  await shot(rotatedPage, 'mobile-portrait-rotated-14-state-geometry')
  await resumeAnimation(rotatedStateGhost)
  await rotatedPage.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout:1500 })
  const rotatedStateTrace = await rotatedPage.evaluate(() => window.__stateTransitionTrace.slice())
  ok(rotatedStateTrace.filter(direction => direction === 'active>rested').length === 1,
    `mobile-portrait-rotated: one authority state change must play once: ${rotatedStateTrace.join(',')}`)

  await invoke(rotatedPage, 'ready')
  await rotatedPage.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout:1500 })
  await resetStateTransitionTrace(rotatedPage)
  await invoke(rotatedPage, 'attackWithDuplicateRestSnapshots')
  const rotatedAttackGhost = rotatedPage.locator('.l12-card-state-transition-ghost[data-motion-kind="attack-rest"]')
  await rotatedAttackGhost.waitFor({ state:'attached', timeout:1500 })
  ok(await rotatedAttackGhost.evaluate(node => node.parentElement?.id) === 'l12-landscape-teleports',
    'mobile-portrait-rotated: combined attack-rest ghost must share the transformed logical canvas')
  const rotatedAttackKey = await rotatedAttackGhost.getAttribute('data-attack-sequence')
  const rotatedAttackAuthority = await physicalCenter(rotatedPage.locator('[data-card-instance-id="mover-1"]'))
  await setAnimationProgress(rotatedAttackGhost, .48)
  const rotatedAttackLunge = await physicalCenter(rotatedAttackGhost)
  ok(centerDistance(rotatedAttackAuthority, rotatedAttackLunge) >= 6,
    'mobile-portrait-rotated: combined attack-rest ghost must visibly lunge toward the defender')
  await assertGhostGeometry(rotatedAttackGhost, 'mobile-portrait-rotated', 'combined attack-rest at 48%')
  await rotatedPage.evaluate(() => window.dispatchEvent(new Event('l12-viewport-change')))
  await rotatedPage.waitForTimeout(32)
  ok(await rotatedAttackGhost.count() === 1 && await rotatedAttackGhost.getAttribute('data-attack-sequence') === rotatedAttackKey,
    'mobile-portrait-rotated: viewport jitter must not blink away or replay an active attack-rest transaction')
  await shot(rotatedPage, 'mobile-portrait-rotated-14b-attack-rest')
  await finishAnimationFast(rotatedAttackGhost)
  await rotatedPage.waitForFunction(() => !document.querySelector('.l12-card-state-transition-ghost'), null, { timeout:1500 })
  const rotatedAttackTrace = await rotatedPage.evaluate(() => window.__stateTransitionTrace.slice())
  ok(rotatedAttackTrace.filter(direction => direction === 'active>rested').length === 1,
    `mobile-portrait-rotated: one attack transaction must play one combined rest animation: ${rotatedAttackTrace.join(',')}`)

  await invoke(rotatedPage, 'move')
  const rotatedMoveGhost = rotatedPage.locator('.l12-zone-flight-ghost')
  await rotatedMoveGhost.waitFor({ state:'attached', timeout:1500 })
  ok(await rotatedMoveGhost.evaluate(node => node.parentElement?.id) === 'l12-landscape-teleports',
    'mobile-portrait-rotated: cloned zone flight must share the transformed logical canvas')
  await setAnimationProgress(rotatedMoveGhost, .999)
  const rotatedMoveEnd = await physicalCenter(rotatedMoveGhost)
  const rotatedMoveTarget = await physicalCenter(rotatedPage.locator('[data-card-instance-id="mover-1"]'))
  ok(centerDistance(rotatedMoveEnd, rotatedMoveTarget) <= 10,
    `mobile-portrait-rotated: zone flight must settle on the physical card, drift=${centerDistance(rotatedMoveEnd, rotatedMoveTarget).toFixed(1)}px, actual=${rotatedMoveEnd.x.toFixed(1)},${rotatedMoveEnd.y.toFixed(1)}, target=${rotatedMoveTarget.x.toFixed(1)},${rotatedMoveTarget.y.toFixed(1)}`)
  await shot(rotatedPage, 'mobile-portrait-rotated-15-zone-geometry')
  await resumeAnimation(rotatedMoveGhost)
  await waitForMovementQueue(rotatedPage)

  await rotatedPage.evaluate(() => {
    window.__rotatedMovementTrace = []
    window.__rotatedMovementKeys = new Set()
    new MutationObserver(() => {
      document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').forEach(node => {
        const key = node.getAttribute('data-movement-key') ?? ''
        if (!key || window.__rotatedMovementKeys.has(key)) return
        window.__rotatedMovementKeys.add(key)
        window.__rotatedMovementTrace.push(node.getAttribute('data-movement-instance-id'))
      })
    }).observe(document.body, { childList:true, subtree:true })
  })
  await rotatedPage.evaluate(() => window.__visualTransitionHarness.setReplay(.1))
  await rotatedPage.waitForTimeout(32)
  await invoke(rotatedPage, 'swap')
  await rotatedPage.waitForFunction(() => (document.querySelector('.l12-zone-flight-ghost')?.getAnimations().length ?? 0) > 0,
    null, { timeout:1500 })
  const jitterGhost = rotatedPage.locator('.l12-zone-flight-ghost')
  const jitterMovementKey = await jitterGhost.getAttribute('data-movement-key')
  // Freeze a known in-flight frame so this assertion measures cancellation,
  // not whether the naturally short animation happened to finish first.
  await setAnimationProgress(jitterGhost, .25)
  await rotatedPage.evaluate(() => window.dispatchEvent(new Event('l12-viewport-change')))
  await rotatedPage.waitForTimeout(32)
  ok(await jitterGhost.count() === 1 && await jitterGhost.getAttribute('data-movement-key') === jitterMovementKey,
    'mobile-portrait-rotated: harmless visual-viewport jitter must not blink away an active flight')
  await rotatedPage.evaluate(() => window.__visualTransitionHarness.setReplay(null))
  await jitterGhost.evaluate(node => node.getAnimations().forEach(animation => {
    animation.playbackRate = 10
    animation.play()
  }))
  await waitForMovementQueue(rotatedPage)
  const jitterTrace = await rotatedPage.evaluate(() => window.__rotatedMovementTrace.slice())
  ok(JSON.stringify(jitterTrace) === JSON.stringify(['mover-1', 'mover-2']),
    `mobile-portrait-rotated: viewport jitter must not drop or replay queued instance movements: ${jitterTrace.join(',')}`)

  await invoke(rotatedPage, 'defeatRestedDefender')
  const rotatedDefeatGhost = rotatedPage.locator('.l12-combat-defeat-ghost')
  await rotatedDefeatGhost.waitFor({ state:'attached', timeout:1500 })
  ok(await rotatedDefeatGhost.evaluate(node => node.parentElement?.id) === 'l12-landscape-teleports',
    'mobile-portrait-rotated: combat defeat ghost must share the transformed logical canvas')
  await setAnimationProgress(rotatedDefeatGhost, .999)
  const rotatedDefeatEnd = await physicalCenter(rotatedDefeatGhost)
  const rotatedGraveTarget = await physicalCenter(rotatedPage.locator('[data-player-index="1"] [data-l12-zone="graveyard"]'))
  ok(centerDistance(rotatedDefeatEnd, rotatedGraveTarget) <= 12,
    `mobile-portrait-rotated: defeat flight must settle on the physical graveyard, drift=${centerDistance(rotatedDefeatEnd, rotatedGraveTarget).toFixed(1)}px, actual=${rotatedDefeatEnd.x.toFixed(1)},${rotatedDefeatEnd.y.toFixed(1)}, target=${rotatedGraveTarget.x.toFixed(1)},${rotatedGraveTarget.y.toFixed(1)}`)
  await shot(rotatedPage, 'mobile-portrait-rotated-16-defeat-geometry')
  await resumeAnimation(rotatedDefeatGhost)
  await rotatedPage.waitForFunction(() => !document.querySelector('.l12-combat-defeat-ghost'), null, { timeout:1800 })

  await rotatedPage.evaluate(() => { window.__rotatedMovementTrace = []; window.__rotatedMovementKeys = new Set() })
  await invoke(rotatedPage, 'playTriggeredEntrant')
  await rotatedPage.waitForFunction(() => (window.__rotatedMovementTrace?.length ?? 0) >= 1, null, { timeout:2500 })
  await waitForMovementQueue(rotatedPage)
  const rotatedEntryTrace = await rotatedPage.evaluate(() => window.__rotatedMovementTrace.slice())
  ok(JSON.stringify(rotatedEntryTrace) === JSON.stringify(['triggered-entrant']),
    `mobile-portrait-rotated: play plus enter/effect descriptions must animate once: ${rotatedEntryTrace.join(',')}`)
  await rotatedPage.waitForFunction(() => !document.querySelector('.public-reveal-animation'), null, { timeout:5000 })

  await rotatedPage.evaluate(() => { window.__rotatedMovementTrace = []; window.__rotatedMovementKeys = new Set() })
  await invoke(rotatedPage, 'millManyForTop')
  await rotatedPage.waitForFunction(() => (window.__rotatedMovementTrace?.length ?? 0) >= 3, null, { timeout:5000 })
  await waitForMovementQueue(rotatedPage)
  const rotatedMillTrace = await rotatedPage.evaluate(() => window.__rotatedMovementTrace.slice())
  ok(JSON.stringify(rotatedMillTrace) === JSON.stringify(['top-card-1', 'top-card-2', 'top-card-3']),
    `mobile-portrait-rotated: batch mill must play each authority card once: ${rotatedMillTrace.join(',')}`)

  await invoke(rotatedPage, 'beginFinnOptionalReady')
  await rotatedPage.waitForTimeout(80)
  await invoke(rotatedPage, 'declineFinnOptionalReady')
  await rotatedPage.waitForTimeout(180)
  ok(await visibleCount(rotatedPage, '.l12-card-state-transition-ghost') === 0,
    'mobile-portrait-rotated: declining optional ready must not flash a false transition')
  await invoke(rotatedPage, 'reconnectWithHistoricalReady')
  await rotatedPage.waitForTimeout(180)
  ok(await visibleCount(rotatedPage, '.l12-card-state-transition-ghost,.l12-zone-flight-ghost,.zone-card-movement') === 0,
    'mobile-portrait-rotated: reconnect must establish a baseline without replaying historical motion')
  await invoke(rotatedPage, 'reconnectWithHistoricalAttack')
  await rotatedPage.waitForTimeout(180)
  ok(await visibleCount(rotatedPage, '.l12-card-state-transition-ghost') === 0,
    'mobile-portrait-rotated: reconnect attack history must not replay attack-rest motion')
  await rotatedPage.evaluate(() => window.__visualTransitionHarness.setReplay(8))
  await invoke(rotatedPage, 'replaySeekBackwardWithHistoricalAttack')
  await rotatedPage.waitForTimeout(180)
  ok(await visibleCount(rotatedPage, '.l12-card-state-transition-ghost') === 0,
    'mobile-portrait-rotated: backward replay seek must not replay attack-rest motion')
  await rotatedPage.evaluate(() => window.__visualTransitionHarness.setReplay(null))
  report.profiles.push('mobile-portrait-rotated')
  await rotatedContext.close()

  const reducedContext = await browser.newContext({ viewport:{ width:1366, height:768 }, reducedMotion:'reduce' })
  const reducedPage = await reducedContext.newPage()
  reducedPage.on('pageerror', error => report.errors.push(`reduced-motion: ${error.message}`))
  await reducedPage.goto(`http://127.0.0.1:${port}/__battle-visual-transitions?viewer=0`, { waitUntil:'networkidle' })
  await reducedPage.waitForSelector('[data-l12-game-stage]')
  await resetStateTransitionTrace(reducedPage)
  await startCardAngleProbe(reducedPage, 'rested-defender', 360)
  await invoke(reducedPage, 'attackWithDuplicateRestSnapshots')
  await reducedPage.waitForTimeout(480)
  const reducedStateTrace = await reducedPage.evaluate(() => window.__stateTransitionTrace.slice())
  ok(reducedStateTrace.filter(direction => direction === 'active>rested').length === 1
    && !reducedStateTrace.includes('rested>active'), 'reduced-motion: one attack authority transaction must still be claimed exactly once')
  const reducedAngles = await reducedPage.evaluate(() => window.__cardAngleSamples.slice())
  ok(reducedAngles.length >= 6 && reducedAngles.every(angle => nearAngle(angle, 90)), 'reduced-motion: rested defender must preserve its authority angle')
  await reducedPage.evaluate(() => {
    window.__movementTrace = []
    let last = ''
    new MutationObserver(() => {
      const node = document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')
      const current = node?.getAttribute('data-movement-instance-id') ?? ''
      if (current && current !== last) window.__movementTrace.push(current)
      last = current
    }).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['data-movement-instance-id'] })
  })
  await invoke(reducedPage, 'playTriggeredEntrant')
  await waitForMovementTrace(reducedPage)
  await waitForMovementQueue(reducedPage)
  await expectTrace(reducedPage, 'reduced-motion', ['triggered-entrant'], 'entry transaction under reduced motion')
  report.profiles.push('reduced-motion')
  await reducedContext.close()
  ok(report.errors.length === 0, `browser errors: ${report.errors.join('; ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ ...report, status: 'passed' }, null, 2))
  console.log(`Battle visual transitions browser verification passed: ${report.assertions} assertions, ${report.screenshots.length} frames`)
} finally {
  await browser?.close()
  await server.close()
}
