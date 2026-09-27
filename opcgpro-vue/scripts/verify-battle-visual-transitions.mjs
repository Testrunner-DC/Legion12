import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = path.join(root, 'artifacts', 'battle-visual-transitions')
fs.rmSync(output, { recursive: true, force: true })
fs.mkdirSync(output, { recursive: true })
const harnessFaceIds = ['S01-01M1', 'S01-01M2', 'S01-02M1', 'S01-02M3', 'S01-03M1', 'S01-03M2', 'S01-04M1', 'S01-04M2']
const harnessFaceRedirects = new Map(harnessFaceIds.map(cardId => [
  `/api/site/media/visual-transition/${cardId}.png`,
  `/assets/l12/special/master/${cardId}.png`,
]))

const harnessHtml = `<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module">import { createApp } from 'vue';import Harness from '/scripts/fixtures/BattleVisualTransitionHarness.vue';import '/src/style.css';import '/src/l12/motion.css';createApp(Harness).mount('#app')</script></body></html>`
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
async function expectTrace(page, profile, expected, label) {
  ok(JSON.stringify(await page.evaluate(() => window.__movementTrace.slice())) === JSON.stringify(expected), `${profile}: ${label} must preserve event-local order`)
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

try {
  await server.listen()
  const address = server.httpServer.address()
  const port = typeof address === 'object' && address ? address.port : 0
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const profile of [
    { name: 'desktop', viewport: { width: 1920, height: 1080 } },
    { name: 'mobile-landscape', viewport: { width: 844, height: 390 }, isMobile: true, hasTouch: true },
  ]) {
    const context = await browser.newContext({ viewport: profile.viewport, isMobile: profile.isMobile, hasTouch: profile.hasTouch })
    const page = await context.newPage()
    page.on('pageerror', error => report.errors.push(`${profile.name}: ${error.message}`))
    await page.goto(`http://127.0.0.1:${port}/__battle-visual-transitions`, { waitUntil: 'networkidle' })
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
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: rapid discard snapshot must start exactly one movement`)
    await page.waitForTimeout(520)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: reentrant snapshots must not replay the same discard fact`)
    await invoke(page, 'returnDuplicateCard')
    await expectAuthorityFaceFlight(page, profile.name, 'duplicate-discard', 'S01-04M2', 'single grave-to-library-bottom return')
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: a later real movement of the same instance must not be deduplicated`)
    await waitForMovementQueue(page)
    await expectConcealedLibraryPile(page, profile.name, 'single bottom return')
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '1'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="佣兵部队"]').count() === 0, `${profile.name}: real second movement must match the final library state`)

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
    report.profiles.push(profile.name)
    await context.close()
  }
  ok(report.errors.length === 0, `browser errors: ${report.errors.join('; ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ ...report, status: 'passed' }, null, 2))
  console.log(`Battle visual transitions browser verification passed: ${report.assertions} assertions, ${report.screenshots.length} frames`)
} finally {
  await browser?.close()
  await server.close()
}
