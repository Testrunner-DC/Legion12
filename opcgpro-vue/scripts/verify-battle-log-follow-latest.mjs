import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

const front = path.resolve(import.meta.dirname, '..'), repo = path.dirname(front)
const output = process.env.L12_QA_OUT, cache = process.env.L12_QA_CACHE
assert(output && cache && path.isAbsolute(output) && path.isAbsolute(cache), 'explicit D/E output and cache required')
const q = value => `'${value.replaceAll("'", "''")}'`
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  `. ${q(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; Assert-L12GeneratedPath -Path ${q(output)} | Out-Null; Assert-L12GeneratedPath -Path ${q(cache)} | Out-Null`])
fs.mkdirSync(output, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import { createApp, defineComponent, h, ref } from 'vue'
import '/src/style.css'
import BattleEventLog from '/src/l12/game/BattleEventLog.vue'
const events = ref(Array.from({ length: 35 }, (_, index) => ({ sequence: index + 1, type: 'draw',
 text: '抽取1张牌', playerIndex: index % 2, cards: [] })))
const height = ref(240), shown = ref(true)
const card = { instanceId: 'synthetic-attacker', cardId: 'SYNTHETIC', name: '合成军团', cardType: 'legion', troops: 4000 }
const combat = (sequence, type, kind, outcome) => ({ sequence, type, text: kind, cards: [card], playerIndex: 0,
 playerCombat: { combatId: 'synthetic-combat', eventKind: kind, outcomeCode: outcome, attackerTroops: 4000 } })
window.__logQa = {
 append() { events.value.push({ sequence: events.value.at(-1).sequence + 1, type: 'draw', text: '抽取1张牌', cards: [], playerIndex: 0 }) },
 combat() { events.value.push(combat(events.value.at(-1).sequence + 1, 'attack', 'attack', 'declared')) },
 result() { events.value.push(combat(events.value.at(-1).sequence + 1, 'defense', 'defense', 'unblocked')) },
 height(value) { height.value = value }, show(value) { shown.value = value },
}
createApp(defineComponent({ setup() { return () => h('main', { style: 'padding:16px;color:#eee' }, [
 h('div', { style: { display: shown.value ? 'flex' : 'none', height: height.value + 'px', width: 'min(340px,calc(100vw - 32px))', minHeight: 0 } },
 [h(BattleEventLog, { events: events.value, you: 0, names: ['合成甲', '合成乙'] })]),
]) } })).mount('#app')`
const server = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
  plugins: [vue(), tailwindcss(), {
    name: 'battle-log-follow-qa',
    resolveId(id) { if (id === '/__log-follow.js') return '\0log-follow' },
    load(id) { if (id === '\0log-follow') return entry },
    configureServer(instance) { instance.middlewares.use((request, response, next) => {
      if (request.url !== '/__log-follow') return next()
      response.setHeader('content-type', 'text/html')
      response.end('<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/__log-follow.js"></script>')
    }) },
  }], server: { host: '127.0.0.1', port: 0 }, resolve: { alias: { '@': path.join(front, 'src') } }, logLevel: 'warn' })
let browser
const report = { schema: 1, status: 'running', scope: 'actual BattleEventLog SFC and log projection, native browser layout/scroll; synthetic events, not live game/network or physical-device acceptance',
  sourceSha256: {}, cases: [], errors: [] }
for (const leaf of ['src/l12/game/BattleEventLog.vue', 'scripts/test-battle-log-follow-latest.mjs', 'scripts/test-battle-log-view-model.mjs', 'scripts/verify-battle-log-follow-latest.mjs']) {
  report.sourceSha256[leaf] = createHash('sha256').update(fs.readFileSync(path.join(front, leaf))).digest('hex')
}
try {
  await server.listen()
  browser = await chromium.launchPersistentContext(path.join(cache, 'profile'), { channel: 'msedge', headless: true })
  await browser.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const page = await browser.newPage()
  page.on('pageerror', error => report.errors.push(error.message))
  const atBottom = async () => page.evaluate(() => { const node = document.querySelector('.event-list'); return node.scrollHeight - node.scrollTop - node.clientHeight <= 2 })
  const waitBottom = () => page.waitForFunction(() => { const node = document.querySelector('.event-list'); return node && node.scrollHeight - node.scrollTop - node.clientHeight <= 2 })
  for (const viewport of [{ width: 1366, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    await page.setViewportSize(viewport)
    await page.goto(server.resolvedUrls.local[0] + '__log-follow')
    await page.locator('.event-list').waitFor(); await waitBottom()
    await page.evaluate(() => window.__logQa.append()); await waitBottom()
    await page.evaluate(() => window.__logQa.combat()); await waitBottom()
    await page.locator('.combat-toggle').click(); await waitBottom()
    const sequence = await page.locator('.combat-summary').last().getAttribute('data-event-sequence')
    await page.evaluate(() => window.__logQa.result()); await waitBottom()
    assert.equal(await page.locator('.combat-summary').last().getAttribute('data-event-sequence'), sequence)
    const box = await page.locator('.event-list').boundingBox()
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.wheel(0, -260)
    await page.locator('.jump-latest').waitFor()
    // Wait for native wheel movement to settle before comparing history position.
    await page.waitForFunction(() => document.querySelector('.event-list').scrollTop < document.querySelector('.event-list').scrollHeight - document.querySelector('.event-list').clientHeight - 40)
    await page.waitForTimeout(100)
    const oldTop = await page.locator('.event-list').evaluate(node => node.scrollTop)
    await page.evaluate(() => { window.__logQa.append(); window.__logQa.append() })
    await page.waitForTimeout(60)
    assert(Math.abs(await page.locator('.event-list').evaluate(node => node.scrollTop) - oldTop) <= 2)
    assert.equal(await atBottom(), false)
    await page.locator('.jump-latest').click(); await waitBottom()
    await page.evaluate(() => window.__logQa.height(160)); await waitBottom()
    await page.evaluate(() => { window.__logQa.show(false); window.__logQa.append() })
    await page.waitForTimeout(40)
    await page.evaluate(() => window.__logQa.show(true)); await waitBottom()
    await page.locator('.event-list').focus(); await page.keyboard.press('Home')
    await page.locator('.jump-latest').waitFor()
    await page.waitForFunction(() => document.querySelector('.event-list').scrollTop === 0)
    await page.evaluate(() => window.__logQa.append()); await page.waitForTimeout(40)
    assert.equal(await page.locator('.event-list').evaluate(node => node.scrollTop), 0)
    await page.keyboard.press('End'); await waitBottom()
    await page.evaluate(() => window.__logQa.append()); await waitBottom()
    await page.screenshot({ path: path.join(output, `latest-${viewport.width}x${viewport.height}.png`) })
    report.cases.push({ ...viewport, mountAppend: true, mergedCombat: true, manualWheelPreserved: true,
      explicitLatest: true, resizeAndHiddenReopen: true, keyboardHistoryAndResume: true })
  }
  assert.deepEqual(report.errors, [])
  report.status = 'passed'
} catch (error) { report.status = 'failed'; report.error = String(error.stack || error); throw error }
finally {
  if (browser) await browser.close()
  await server.close()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n')
}
console.log('Battle log follow acceptance passed: three viewports, native scroll, merged summary, history and lifecycle')
