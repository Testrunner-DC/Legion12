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
fs.mkdirSync(output, { recursive: true })

const harnessHtml = `<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module">import { createApp } from 'vue';import Harness from '/scripts/fixtures/BattleVisualTransitionHarness.vue';import '/src/style.css';import '/src/l12/motion.css';createApp(Harness).mount('#app')</script></body></html>`
const harnessPlugin = {
  name: 'battle-visual-transition-harness',
  configureServer(server) {
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
  const box = await locator.boundingBox()
  if (!box) throw new Error(`Cannot capture ${name}: moving element has no bounding box`)
  const viewport = page.viewportSize()
  const padding = 12
  const x = Math.max(0, box.x - padding)
  const y = Math.max(0, box.y - padding)
  const width = Math.min((viewport?.width ?? box.x + box.width + padding) - x, box.width + padding * 2)
  const height = Math.min((viewport?.height ?? box.y + box.height + padding) - y, box.height + padding * 2)
  // A fixed-position board can be wider than a phone viewport. The assertions
  // still validate that off-viewport transition, while the full-frame capture
  // remains the honest mobile evidence; only skip an impossible close-up crop.
  if (width <= 0 || height <= 0) return
  await page.screenshot({ path: file, clip: { x, y, width, height } })
  report.screenshots.push(file)
}
async function invoke(page, method) { await page.evaluate(name => window.__visualTransitionHarness[name](), method) }
async function visibleCount(page, selector) { return page.locator(selector).count() }

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
    await invoke(page, 'tap')
    await page.waitForTimeout(40)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 1, `${profile.name}: active→rested must have one transition ghost`)
    ok(await page.locator('[data-card-instance-id="mover-1"]').evaluate(node => node.style.visibility === 'hidden'), `${profile.name}: authority target must hand off invisibly during active→rested`)
    await shot(page, `${profile.name}-01-active-to-rested-mid`)
    await page.waitForTimeout(460)
    ok(await page.locator('[data-card-instance-id="mover-1"].tapped').count() === 1, `${profile.name}: final rested DOM must match authority`)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 0, `${profile.name}: active→rested ghost must clean up`)

    await invoke(page, 'ready')
    await page.waitForTimeout(40)
    ok(await visibleCount(page, '.l12-card-state-transition-ghost') === 1, `${profile.name}: rested→active must animate`)
    await page.waitForTimeout(460)
    ok(await page.locator('[data-card-instance-id="mover-1"]:not(.tapped)').count() === 1, `${profile.name}: final active DOM must match authority`)

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
        const node = document.querySelector('.zone-card-movement')
        const current = node?.getAttribute('data-movement-instance-id') ?? ''
        if (current && current !== last) window.__movementTrace.push(current)
        last = current
      }).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['data-movement-instance-id'] })
    })
    await invoke(page, 'millMany')
    await page.waitForTimeout(70)
    ok(await page.locator('.zone-card-movement[data-movement-instance-id="mill-card-1"][data-movement-from="library"][data-movement-to="graveyard"]').count() === 1, `${profile.name}: first milled card must start from library in authority order`)
    ok(await page.locator('.zone-card-movement .moving-card').isVisible(), `${profile.name}: library discard card-back must be visibly rendered`)
    ok((await page.locator('.zone-card-movement img').getAttribute('src'))?.includes('card-back-official.png'), `${profile.name}: library-origin flight must use the official main-deck back`)
    await shot(page, `${profile.name}-06-library-mill-cardback-mid`)
    await elementShot(page, page.locator('.zone-card-movement .moving-card'), `${profile.name}-06b-library-mill-cardback-closeup`)
    await page.waitForFunction(() => document.querySelector('.zone-card-movement')?.getAttribute('data-movement-instance-id') === 'mill-card-2', null, { timeout:1800 })
    ok(await page.locator('.zone-card-movement[data-movement-instance-id="mill-card-2"]').count() === 1, `${profile.name}: second milled card must wait for the first`)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: mill cards must never overlap`)
    await page.waitForFunction(() => document.querySelector('.zone-card-movement')?.getAttribute('data-movement-instance-id') === 'mill-card-3', null, { timeout:1800 })
    ok(await page.locator('.zone-card-movement[data-movement-instance-id="mill-card-3"]').count() === 1, `${profile.name}: third milled card must wait for the second`)
    await page.waitForFunction(() => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').length === 0, null, { timeout:1800 })
    ok(JSON.stringify(await page.evaluate(() => window.__movementTrace.slice(0, 3))) === JSON.stringify(['mill-card-1', 'mill-card-2', 'mill-card-3']), `${profile.name}: multi-mill trace must preserve event-local card order`)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: multi-mill queue must finish without overlap`)
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '4'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="磨牌三"]').count() === 1, `${profile.name}: final graveyard count and top card must match the three ordered mills`)

    await invoke(page, 'returnMilled')
    await page.waitForTimeout(70)
    ok(await page.locator('.l12-zone-flight-ghost[data-movement-instance-id="mill-card-1"],.zone-card-movement[data-movement-instance-id="mill-card-1"]').count() === 1, `${profile.name}: first grave return must follow authority order`)
    ok(await page.locator('.l12-zone-flight-ghost,.zone-card-movement .moving-card').first().isVisible(), `${profile.name}: grave return must be visibly rendered`)
    await shot(page, `${profile.name}-07-grave-return-first-mid`)
    await elementShot(page, page.locator('.l12-zone-flight-ghost,.zone-card-movement .moving-card').first(), `${profile.name}-07b-grave-return-first-closeup`)
    await page.waitForFunction(() => document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')?.getAttribute('data-movement-instance-id') === 'mill-card-2', null, { timeout:1800 })
    ok(await page.locator('.l12-zone-flight-ghost[data-movement-instance-id="mill-card-2"],.zone-card-movement[data-movement-instance-id="mill-card-2"]').count() === 1, `${profile.name}: second grave return must wait for the first`)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: grave returns must never overlap`)
    await page.waitForFunction(() => document.querySelector('.l12-zone-flight-ghost,.zone-card-movement')?.getAttribute('data-movement-instance-id') === 'mill-card-3', null, { timeout:1800 })
    ok(await page.locator('.l12-zone-flight-ghost[data-movement-instance-id="mill-card-3"],.zone-card-movement[data-movement-instance-id="mill-card-3"]').count() === 1, `${profile.name}: third grave return must wait for the second`)
    await page.waitForFunction(() => document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement').length === 0, null, { timeout:1800 })
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '1'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt^="磨牌"]').count() === 0, `${profile.name}: returned cards must leave the final graveyard without changing the unrelated card`)

    await invoke(page, 'duplicateDiscardSnapshots')
    await page.waitForTimeout(70)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 1, `${profile.name}: rapid discard snapshot must start exactly one movement`)
    await page.waitForTimeout(520)
    ok(await visibleCount(page, '.l12-zone-flight-ghost,.zone-card-movement') === 0, `${profile.name}: reentrant snapshots must not replay the same discard fact`)
    await invoke(page, 'returnDuplicateCard')
    await page.waitForTimeout(70)
    ok(await page.locator('.l12-zone-flight-ghost[data-movement-instance-id="duplicate-discard"],.zone-card-movement[data-movement-instance-id="duplicate-discard"]').count() === 1, `${profile.name}: a later real movement of the same instance must not be deduplicated`)
    await page.waitForTimeout(520)
    ok(await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] .pile-count').textContent() === '1'
      && await page.locator('[data-player-index="0"] [data-l12-zone="graveyard"] img[alt="佣兵部队"]').count() === 0, `${profile.name}: real second movement must match the final library state`)
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
