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
