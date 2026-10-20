import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'
import { createHash } from 'node:crypto'

// Entry wrapper must validate these physical D roots with L12GeneratedStorage.
const input = process.env.L12_FEEDBACK_FIXTURES
const out = process.env.L12_FEEDBACK_UI_OUT
assert(input && out && process.env.L12_VITE_CACHE_DIR, 'Explicit governed roots required')
assert(/^[DE]:[\\/]/i.test(fs.realpathSync(input)), 'Fixtures must not resolve to C')
fs.mkdirSync(out, { recursive: true })
assert(/^[DE]:[\\/]/i.test(fs.realpathSync(out)), 'Evidence must not resolve to C')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const port = Number(process.env.L12_FEEDBACK_PORT || 5197)
const base = `http://127.0.0.1:${port}`
const report = { scope: 'Actual GamePage components, synthetic real-engine recipient snapshots; not live transport or physical devices', cases: [], pageErrors: [], externalRequestsBlocked: 0 }
const server = spawn(process.execPath, ['scripts/preview-l12-battle-layout.mjs', '--port', String(port)], { cwd: path.resolve(import.meta.dirname, '..'), env: process.env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true })
let serverLog = ''
server.stdout.on('data', data => { serverLog += data })
server.stderr.on('data', data => { serverLog += data })
let browser
try {
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Error(`Preview unavailable: ${serverLog.slice(-2000)}`)), 45000)
    const ready = data => { if (String(data).includes('L12 battle layout preview:')) { clearTimeout(timer); server.stdout.off('data', ready); resolve() } }
    server.stdout.on('data', ready)
    server.once('exit', code => { clearTimeout(timer); reject(Error(`Preview exited ${code}`)) })
  })
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const files = fs.readdirSync(input).filter(name => /^(mengpo-selected-targets|oiran-log)-[01]-(False|True)\.json$/.test(name)).sort()
  assert.equal(files.length, 8, 'Both actors, real restore and live state required')
  for (const [width, height] of [[1366, 768], [844, 390]]) {
    const context = await browser.newContext({ viewport: { width, height } })
    await context.route('**/*', route => {
      if (route.request().url().startsWith(`${base}/`)) return route.continue()
      report.externalRequestsBlocked++
      return route.abort()
    })
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    page.on('pageerror', error => report.pageErrors.push(error.message))
    for (const file of files) {
      const raw = fs.readFileSync(path.join(input, file))
      const fixture = JSON.parse(raw)
      for (const view of ['owner', 'opponent', 'spectator', 'referee']) {
        const item = { file, view, width, height, fixtureSha256: createHash('sha256').update(raw).digest('hex'), engineSha256: fixture.engineSha256, commit: fixture.commit }
        try {
          const flags = `${width < 1000 ? '&mobile=1&canvas=1' : ''}${view === 'spectator' ? '&spectator=1' : view === 'referee' ? '&referee=1' : ''}`
          await page.goto(`${base}/__l12_battle_preview__?field=empty${flags}`, { waitUntil: 'domcontentloaded' })
          await page.waitForFunction(() => Boolean(window.__l12State?.game))
          await page.evaluate(({ snapshot, view }) => {
            const state = window.__l12State
            // Preserve the preview's shared object; its keepalive must not replace our input.
            Object.assign(state.game, snapshot)
            state.spectating = ['spectator', 'referee'].includes(view)
            state.observerView = view === 'referee' ? 'referee' : 'public'
          }, { snapshot: fixture.views[view], view })
          await page.waitForFunction(({ revision, you }) => window.__l12State.game.revision === revision && window.__l12State.game.you === you, fixture.views[view])
          if (fixture.scenario === 'mengpo-selected-targets' && fixture.views[view].prompts.some(prompt => prompt.kind === 'response')) {
            const target = page.locator(`.formation-slot[data-tutorial-card-instance="${fixture.expected.targetId}"]`)
            await target.waitFor()
            await page.waitForFunction(id => document.querySelector(`.formation-slot[data-tutorial-card-instance="${id}"]`)?.classList.contains('response-target'), fixture.expected.targetId)
            assert.equal(await page.locator('.formation-slot.response-target').count(), 1, 'Only the selected target is highlighted')
            const responseText = page.locator('.effect-decision-text')
            assert((await responseText.innerText()).includes(fixture.expected.targetName), 'Actual response dialog names the selected target')
            item.responseHighlight = true
          }
          const minimize = page.getByRole('button', { name: '最小化弹框', exact: true })
          if (await minimize.isVisible()) await minimize.click()
          if (width < 1000) await page.getByRole('button', { name: '对局记录', exact: true }).click()
          const log = page.locator(width < 1000 ? '.mobile-record-overlay .battle-event-log' : '.record-log .battle-event-log')
          await log.waitFor({ state: 'visible' })
          item.collapsedText = await log.innerText()
          for (const toggle of await log.locator('.action-toggle[aria-expanded="false"]').all()) await toggle.click()
          const text = await log.innerText()
          if (fixture.scenario === 'mengpo-selected-targets') {
            assert(text.includes('已选目标') && text.includes(fixture.expected.targetName), 'Named selected target is readable in the actual log')
          } else {
            assert(text.includes(fixture.expected.cardName) && text.includes('加入手牌'), 'Revealed/added identity is readable in the actual log')
          }
          item.expandedText = text
          const namedRow = log.locator('.battle-event').filter({ hasText: fixture.expected.targetName || fixture.expected.cardName }).last()
          await namedRow.scrollIntoViewIfNeeded()
          const rowBox = await namedRow.boundingBox()
          const listBox = await log.locator('.event-list').boundingBox()
          assert(rowBox && listBox && rowBox.y >= listBox.y - 1 && rowBox.y + rowBox.height <= listBox.y + listBox.height + 1, 'Named log row can be scrolled fully into the visible list')
          const hit = await namedRow.evaluate(element => {
            const box = element.getBoundingClientRect()
            const at = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
            return Boolean(at && (element.contains(at) || at === element))
          })
          assert(hit, 'Named log row is not covered by another overlay')
          item.namedRowReadable = true
          item.eventRows = await log.locator('[data-event-sequence]').count()
          await page.evaluate(snapshot => Object.assign(window.__l12State.game, snapshot), fixture.views[view])
          await page.waitForTimeout(240)
          assert.equal(await log.locator('[data-event-sequence]').count(), item.eventRows, 'Repeated snapshot does not duplicate log rows')
          if (fixture.actor === 0 && !fixture.restored) await page.screenshot({ path: path.join(out, `${fixture.scenario}-${view}-${width}.png`) })
          item.status = 'passed'
        } catch (error) {
          item.status = 'failed'; item.error = error.message
          await page.screenshot({ path: path.join(out, `failure-${file}-${view}-${width}.png`) }).catch(() => {})
        }
        report.cases.push(item)
        console.log(`${item.status} ${file} ${view} ${width}`)
        if (item.status === 'failed') throw Error(`Stopped at first concrete UI failure: ${item.error}`)
      }
    }
    await context.close()
  }
  report.status = report.cases.length === 64 && report.cases.every(item => item.status === 'passed') && !report.pageErrors.length ? 'passed' : 'failed'
} finally {
  if (browser) await browser.close()
  server.kill()
  await new Promise(resolve => { if (server.exitCode !== null) resolve(); else { server.once('exit', resolve); setTimeout(resolve, 5000).unref() } })
  report.serverLog = serverLog
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
}
console.log(JSON.stringify({ status: report.status, cases: report.cases.length, failures: report.cases.filter(item => item.status !== 'passed').length, pageErrors: report.pageErrors }))
if (report.status !== 'passed') process.exitCode = 1
