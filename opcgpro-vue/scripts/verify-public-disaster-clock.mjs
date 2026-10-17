import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_PUBLIC_CLOCK_OUT
assert(output && /^[DE]:[\\/]/i.test(output), 'A governed D/E output must be selected and checked by the caller')
assert(!fs.existsSync(output), 'Do not overwrite earlier evidence')
fs.mkdirSync(output, { recursive: true })
const sha = value => createHash('sha256').update(value).digest('hex')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const server = spawn(process.execPath, [path.join(root, 'scripts/preview-l12-battle-layout.mjs'), '--port', '5441'], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
const report = { status: 'RUNNING', sources: Object.fromEntries(['src/l12/game/PromptOverlay.vue', 'src/l12/game/SetupDecisionClock.vue', 'scripts/verify-public-disaster-clock.mjs', 'scripts/preview-l12-battle-layout.mjs'].map(file => [file, sha(fs.readFileSync(path.join(root, file)))])), cases: [], previewLog: '', scope: 'Real GamePage components with synthetic projected prompts/clocks, not engine transport or real-device evidence' }
let browser
try {
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Bounded preview startup expired')), 20_000)
    server.once('exit', code => { clearTimeout(timer); reject(new Error(`Preview exited ${code}`)) })
    server.stdout.on('data', chunk => { report.previewLog += chunk.toString(); if (report.previewLog.includes('L12 battle layout preview:')) { clearTimeout(timer); resolve() } })
    server.stderr.on('data', chunk => { report.previewLog += chunk.toString() })
  })
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1366, height: 768 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
    for (const actor of [0, 1]) {
      const mobile = viewport.width < 1000
      const context = await browser.newContext({ viewport, hasTouch: mobile })
      const page = await context.newPage()
      page.setDefaultTimeout(4000)
      const cell = { viewport, actor, errors: [], external: [], stubbedLoopbackApi: [] }; report.cases.push(cell)
      page.on('pageerror', error => cell.errors.push(error.message))
      await page.route('**/*', route => {
        const url = new URL(route.request().url())
        if (url.hostname === '127.0.0.1' && url.pathname.startsWith('/api/')) { cell.stubbedLoopbackApi.push(url.pathname); return route.fulfill({ status: 200, contentType: 'application/json', body: '{}' }) }
        if (!route.request().url().startsWith('http://127.0.0.1:5441/') && !route.request().url().startsWith('data:')) { cell.external.push(new URL(route.request().url()).origin); return route.abort() }
        return route.continue()
      })
      await page.goto(`http://127.0.0.1:5441/__l12_battle_preview__?canvas=1&disaster-choice=1&rankedClock=1${mobile ? '&mobile=1' : ''}`)
      await page.locator('.l12-prompt-overlay').waitFor()
      await page.evaluate(actor => {
        const state = window.__l12State, game = state.game, old = game.prompts[0], previewCardId = old.validChoices[0]
        game.phase = 'DisasterPreparation'; game.disasterPreparationStep = 3; game.you = actor; game.firstPlayer = 0
        game.prompts = [{ ...old, promptId: `public-${actor}`, playerIndex: actor, controller: actor, kind: 'disaster-reveal', text: '随机公开天灾', minChoose: 0, maxChoose: 0, validChoices: [], data: { ...old.data, displayCardIds: '', previewCardId, previewPresentation: 'information-card' } }]
        game.waitingPrompt = null; state.rankedClock.operationLimitMs = 60_000; state.rankedClock.receivedAtMs = Date.now()
        for (const player of state.rankedClock.players) { player.acting = true; player.connected = true; player.operationRemainingMs = 43_000 }
      }, actor)
      const overlay = page.locator('.l12-prompt-overlay'), clock = overlay.locator('.setup-decision-clock')
      await clock.waitFor({ state: 'visible' })
      assert((await clock.innerText()).includes(actor === 0 ? '先攻玩家准备' : '后攻玩家准备'))
      const first = Number((await clock.locator('strong').innerText()).slice(-2))
      await page.waitForFunction(initial => Number(document.querySelector('.l12-prompt-overlay .setup-decision-clock strong')?.textContent?.slice(-2)) < initial, first)
      const second = Number((await clock.locator('strong').innerText()).slice(-2))
      assert(first - second >= 1 && first - second <= 4)
      const commandCount = await page.evaluate(() => window.__sentCommands.length)
      await overlay.locator('.prompt-minimize').click(); assert(await clock.isVisible())
      await overlay.locator('.prompt-minimized-bar button').click(); assert(await clock.isVisible())
      cell.screenshot = path.join(output, `${viewport.width}x${viewport.height}-${actor}.png`)
      await page.screenshot({ path: cell.screenshot }); cell.screenshotSha256 = sha(fs.readFileSync(cell.screenshot))
      await page.evaluate(() => { const state = window.__l12State; for (const player of state.rankedClock.players) player.operationRemainingMs = 0; state.rankedClock.receivedAtMs = Date.now() })
      await page.waitForFunction(() => document.querySelector('.l12-prompt-overlay .setup-decision-clock strong')?.textContent === '00:00')
      assert.equal(await page.evaluate(() => window.__sentCommands.length), commandCount, 'Client timer must not invent timeout commands')
      await page.evaluate(actor => { const state = window.__l12State; state.game.prompts = []; state.game.waitingPrompt = { playerIndex: 1 - actor, playerName: '合成对手', kind: 'disaster-reveal' }; for (const player of state.rankedClock.players) { player.acting = player.playerIndex !== actor; player.operationRemainingMs = 20_000 } state.rankedClock.receivedAtMs = Date.now() }, actor)
      await overlay.locator('.waiting-panel').waitFor(); assert(await clock.isVisible())
      assert((await clock.innerText()).includes(actor === 0 ? '后攻玩家准备' : '先攻玩家准备'))
      await page.evaluate(() => { window.__l12State.rankedClock.operationLimitMs = 0 })
      await clock.waitFor({ state: 'detached' })
      assert.equal(await page.evaluate(() => window.__sentCommands.length), commandCount)
      assert.deepEqual(cell.errors, []); assert.deepEqual(cell.external, [])
      Object.assign(cell, { status: 'PASS', firstSeconds: first, laterSeconds: second, minimizedClock: true, waitingClock: true, clientTimeoutCommands: 0, noRankedBudgetHidden: true })
      await context.close()
    }
  }
  report.status = 'PASS'
} catch (error) {
  report.status = 'FAIL'; report.error = String(error.stack || error); process.exitCode = 1
} finally {
  if (browser) await browser.close()
  server.kill()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: report.status, cases: report.cases.length, error: report.error, report: path.join(output, 'report.json') }))
}
