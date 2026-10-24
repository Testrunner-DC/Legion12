import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import net from 'node:net'
import path from 'node:path'
import { spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const outputRoot = path.resolve(process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/match-opening-presentation-20261024')
if (/^c:/i.test(outputRoot)) throw new Error(`L12_OUTPUT_ON_C: ${outputRoot}`)
fs.mkdirSync(outputRoot, { recursive: true })
const screenshotRoot = path.join(outputRoot, 'screenshots')
fs.mkdirSync(screenshotRoot, { recursive: true })

const tracked = [
  'src/l12/specialAssets.ts', 'src/l12/DeckCover.vue', 'src/l12/game/PromptOverlay.vue',
  'scripts/test-deck-cover.mjs', 'scripts/test-match-opening-presentation.mjs',
  'scripts/check-ui-contracts.mjs', 'scripts/preview-l12-battle-layout.mjs',
  'scripts/verify-match-opening-presentation-20261024.mjs',
]
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex').toUpperCase()
const sourceBefore = Object.fromEntries(tracked.map(file => [file, sha(file)]))
const previewOriginalSha256 = '1BCD1A07E3F453C3FEAF1C0927F735B3E8DBB6E8D3025E467B801F5AB72EA548'
const requested = []
const finished = []
const failed = []
const browserErrors = []
const caseFilter = new Set((process.env.L12_QA_CASES || '').split(',').map(value => value.trim()).filter(Boolean))
let previewProcess
let browser

async function freePort() {
  const server = net.createServer()
  await new Promise((resolve, reject) => server.listen(0, '127.0.0.1', error => error ? reject(error) : resolve()))
  const address = server.address()
  const port = typeof address === 'object' && address ? address.port : 0
  await new Promise(resolve => server.close(resolve))
  if (!port) throw new Error('Unable to reserve a local preview port')
  return port
}

async function startPreview(port) {
  const child = spawn(process.execPath, ['scripts/preview-l12-battle-layout.mjs', '--port', String(port)], {
    cwd: root, env: process.env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
  })
  previewProcess = child
  let output = ''
  await new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`Preview start timeout\n${output}`)), 30000)
    const consume = chunk => {
      output += chunk.toString()
      if (output.includes('L12 battle layout preview:')) { clearTimeout(timeout); resolve() }
    }
    child.stdout.on('data', consume)
    child.stderr.on('data', consume)
    child.once('exit', code => { clearTimeout(timeout); reject(new Error(`Preview exited ${code}\n${output}`)) })
  })
  return output
}

async function stopPreview() {
  if (!previewProcess || previewProcess.exitCode !== null) return
  previewProcess.kill('SIGINT')
  await Promise.race([
    new Promise(resolve => previewProcess.once('exit', resolve)),
    new Promise(resolve => setTimeout(resolve, 5000)),
  ])
  if (previewProcess.exitCode === null) previewProcess.kill('SIGKILL')
}

async function runCase(name, task) {
  if (caseFilter.size && !caseFilter.has(name)) return
  requested.push(name)
  try {
    const detail = await task()
    finished.push({ name, detail })
  } catch (error) {
    failed.push({ name, error: error instanceof Error ? `${error.name}: ${error.message}` : String(error) })
  }
}

function query(values) {
  const params = new URLSearchParams({ 'opening-fixture': '1', ...values })
  return params.toString()
}

async function openingPage(baseUrl, viewport, values = {}, reducedMotion = 'no-preference') {
  const context = await browser.newContext({ viewport, reducedMotion })
  const page = await context.newPage()
  page.on('pageerror', error => browserErrors.push({ type: 'pageerror', message: error.message }))
  page.on('console', message => { if (message.type() === 'error') browserErrors.push({ type: 'console', message: message.text() }) })
  page.on('requestfailed', request => browserErrors.push({ type: 'requestfailed', message: `${request.method()} ${request.url()} ${request.failure()?.errorText || ''}` }))
  await page.goto(`${baseUrl}?${query(values)}`, { waitUntil: 'domcontentloaded', timeout: 30000 })
  await page.waitForFunction(() => Boolean(window.__openingFixture), null, { timeout: 20000 })
  return { context, page }
}

async function waitForArtwork(page) {
  await page.waitForFunction(async () => {
    const images = [...document.querySelectorAll('.initiative-master-banner,.initiative-master-profile')]
    if (images.length < 2) return false
    await Promise.all(images.map(image => image.decode?.().catch(() => {}) ?? Promise.resolve()))
    return images.every(image => image.complete && image.naturalWidth > 0)
  }, null, { timeout: 15000 })
}

async function stageSnapshot(page) {
  return page.evaluate(() => {
    const stage = document.querySelector('[data-ui-contract="match-opening-stage"]')
    if (!(stage instanceof HTMLElement)) throw new Error('opening stage missing')
    const articles = [...stage.querySelectorAll('article')]
    const panel = stage.closest('.prompt-panel,.waiting-panel')
    const actionButtons = [...document.querySelectorAll('.initiative-skip:not(:disabled), [data-tutorial-choice]:not(:disabled), .prompt-confirm-choice:not(:disabled)')]
    const rect = element => {
      const value = element.getBoundingClientRect()
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height }
    }
    return {
      stage: rect(stage), panel: panel ? rect(panel) : null,
      articleClasses: articles.map(article => article.className),
      articleRects: articles.map(rect),
      actions: actionButtons.map(button => ({ text: button.textContent?.trim(), ...rect(button) })),
      viewport: { width: innerWidth, height: innerHeight },
      documentOverflow: { x: document.documentElement.scrollWidth - innerWidth, y: document.documentElement.scrollHeight - innerHeight },
      rotated: document.documentElement.dataset.l12Rotated || 'false',
    }
  })
}

const viewports = [
  { key: 'desktop', width: 1366, height: 768, mobile: false },
  { key: 'short-landscape', width: 844, height: 390, mobile: true },
  { key: 'portrait-rotated', width: 390, height: 844, mobile: true },
]

const port = await freePort()
const baseUrl = `http://127.0.0.1:${port}/__l12_battle_preview__`
try {
  await startPreview(port)
  const require = createRequire(import.meta.url)
  const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const browserExecutable = process.env.L12_CHROMIUM_EXECUTABLE
  browser = await chromium.launch(browserExecutable
    ? { headless: true, executablePath: browserExecutable }
    : { headless: true, channel: 'msedge' })

  for (const viewport of viewports) for (const you of [0, 1]) for (const role of ['owner', 'waiting']) {
    const name = `layout-${viewport.key}-you${you}-${role}`
    await runCase(name, async () => {
      const { context, page } = await openingPage(baseUrl, { width: viewport.width, height: viewport.height }, {
        'opening-you': String(you), 'opening-role': role, 'opening-rolls': 'valid',
        ...(viewport.mobile ? { mobile: '1', canvas: '1' } : {}),
      })
      try {
        await page.waitForSelector('[data-ui-contract="match-opening-stage"]', { state: 'visible' })
        await waitForArtwork(page)
        const snapshot = await stageSnapshot(page)
        assert.deepEqual(snapshot.articleClasses.map(value => String(value).includes('mine') ? 'mine' : String(value).includes('opponent') ? 'opponent' : 'unknown'), ['opponent', 'mine'])
        assert.ok(snapshot.stage.width > 180 && snapshot.stage.height > 90)
        assert.ok(snapshot.panel && snapshot.panel.width > 260 && snapshot.panel.height > 140)
        assert.ok(snapshot.actions.every(action => action.width >= 43.5 && action.height >= 43.5), `undersized action: ${JSON.stringify(snapshot.actions)}`)
        if (role === 'owner') {
          await page.locator('[data-tutorial-choice="first"]').click()
          await page.locator('.prompt-confirm-choice').click()
          const commands = await page.evaluate(() => window.__sentCommands)
          assert.ok(commands.some(command => JSON.stringify(command).includes('first')), 'initiative choice did not use the real command chain')
        } else {
          assert.equal(await page.locator('[data-tutorial-choice="first"]').count(), 0)
          await page.waitForSelector('.waiting-panel')
        }
        if ((you === 0 && role === 'owner') || (you === 1 && role === 'waiting')) {
          await page.waitForSelector('.initiative-opening-stage.settled', { timeout: 2500 })
          await page.screenshot({ path: path.join(screenshotRoot, `${name}.png`) })
        }
        return snapshot
      } finally { await context.close() }
    })
  }

  await runCase('skip-waits-for-authoritative-rolls-and-revision-does-not-reenter', async () => {
    const { context, page } = await openingPage(baseUrl, { width: 1366, height: 768 }, { 'opening-rolls': 'zero' })
    try {
      await page.locator('.initiative-skip').click()
      await page.waitForTimeout(120)
      assert.equal(await page.locator('.initiative-opening-stage.settled').count(), 0)
      assert.match(await page.locator('.initiative-status').innerText(), /掷骰中/)
      await page.evaluate(() => window.__openingFixture.setRolls([6, 3]))
      await page.waitForSelector('.initiative-opening-stage.settled')
      const settledText = await page.locator('.initiative-status').innerText()
      await page.evaluate(() => window.__openingFixture.revise())
      await page.waitForTimeout(180)
      assert.equal(await page.locator('.initiative-opening-stage.settled').count(), 1)
      assert.equal(await page.locator('.initiative-status').innerText(), settledText)
      return { settledText }
    } finally { await context.close() }
  })

  await runCase('cross-match-lifecycle-and-initial-later-phase', async () => {
    const first = await openingPage(baseUrl, { width: 1366, height: 768 }, { 'opening-rolls': 'zero' })
    try {
      await first.page.evaluate(() => window.__openingFixture.nextMatch('opening-B', 'Initiative', { rolls: [0, 0], winner: 0, firstPlayer: 0 }))
      const samples = []
      for (let index = 0; index < 8; index += 1) {
        samples.push(await first.page.locator('.initiative-die').allInnerTexts())
        await first.page.waitForTimeout(95)
      }
      assert.ok(new Set(samples.map(value => value.join('|'))).size > 1, 'B match dice stopped after old-match cleanup')
      await first.page.evaluate(() => window.__openingFixture.nextMatch('opening-C', 'DisasterPreparation', { rolls: [6, 3], winner: 1, firstPlayer: 1 }))
      await first.page.waitForTimeout(80)
      assert.equal(await first.page.locator('.initiative-resolution-message').count(), 0, 'cross-match later phase fabricated a result')
    } finally { await first.context.close() }
    const later = await openingPage(baseUrl, { width: 844, height: 390 }, { 'opening-phase': 'Mulligan', mobile: '1', canvas: '1' })
    try {
      assert.equal(await later.page.locator('[data-ui-contract="match-opening-stage"]').count(), 0)
      assert.equal(await later.page.locator('.initiative-resolution-message').count(), 0)
      return { crossMatchDice: 'active', initialLaterPhase: 'no-backfill' }
    } finally { await later.context.close() }
  })

  await runCase('independent-result-visible-click-through-clear-and-unmount', async () => {
    const { context, page } = await openingPage(baseUrl, { width: 1366, height: 768 }, { 'opening-rolls': 'valid', 'opening-underlay': '1' })
    try {
      await page.waitForSelector('.initiative-opening-stage.settled', { timeout: 2500 })
      const resultObserved = await page.evaluate(() => new Promise(resolve => {
        const current = () => document.querySelector('.initiative-resolution-message')
        const observer = new MutationObserver(() => {
          const node = current()
          if (!node) return
          observer.disconnect()
          clearTimeout(timeout)
          resolve({ text: node.textContent?.trim() || '', connected: node.isConnected })
        })
        observer.observe(document.documentElement, { childList: true, subtree: true })
        const timeout = setTimeout(() => {
          observer.disconnect()
          resolve({ text: '', connected: Boolean(current()) })
        }, 1000)
        window.__openingFixture.sameMatchPhase('Main')
        const node = current()
        if (node) {
          observer.disconnect()
          clearTimeout(timeout)
          resolve({ text: node.textContent?.trim() || '', connected: node.isConnected })
        }
      }))
      assert.ok(resultObserved.connected && resultObserved.text, `result not observed: ${JSON.stringify(resultObserved)}`)
      await page.waitForTimeout(180)
      const resultFrame = await page.evaluate(() => {
        const node = document.querySelector('.initiative-resolution-message')
        if (!(node instanceof HTMLElement)) return null
        const style = getComputedStyle(node)
        const hit = document.elementFromPoint(innerWidth / 2, innerHeight / 2)
        const rect = node.getBoundingClientRect()
        return { text: node.textContent?.trim() || '', opacity: Number(style.opacity), pointerEvents: style.pointerEvents, position: style.position, zIndex: style.zIndex,
          backgroundColor: style.backgroundColor, rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height },
          hitId: hit?.id || '', connected: node.isConnected }
      })
      assert.ok(resultFrame?.connected && resultFrame.opacity > 0 && resultFrame.pointerEvents === 'none' && resultFrame.position === 'fixed', `result frame invalid: ${JSON.stringify(resultFrame)}`)
      assert.ok(resultFrame.rect.left < 683 && resultFrame.rect.right > 683 && resultFrame.rect.top < 384 && resultFrame.rect.bottom > 384, `result not centered over the action: ${JSON.stringify(resultFrame.rect)}`)
      assert.equal(resultFrame.hitId, 'opening-underlay', 'result did not expose the underlying action to hit testing')
      assert.equal(await page.locator('.l12-prompt-overlay').count(), 0, 'result retained the modal overlay')
      await page.mouse.click(683, 384)
      assert.equal(await page.evaluate(() => window.__openingUnderlayClicks), 1, 'result blocked the underlying action')
      await page.screenshot({ path: path.join(screenshotRoot, 'result-visible-click-through.png') })
      await page.waitForTimeout(700)
      assert.equal(await page.locator('.initiative-resolution-message').count(), 0)
      await page.evaluate(() => window.__openingFixture.nextMatch('opening-D', 'Initiative', { rolls: [6, 3], winner: 0, firstPlayer: 0 }))
      await page.waitForSelector('.initiative-opening-stage.settled', { timeout: 2500 })
      await page.evaluate(() => window.__openingFixture.sameMatchPhase('Main'))
      await page.waitForSelector('.initiative-resolution-message', { state: 'visible', timeout: 500 })
      await page.evaluate(() => window.__openingFixture.nextMatch('opening-E', 'DisasterPreparation', { rolls: [6, 3], winner: 1, firstPlayer: 1 }))
      await page.waitForTimeout(80)
      assert.equal(await page.locator('.initiative-resolution-message').count(), 0, 'new match did not clear the old result')
      await page.evaluate(() => window.__openingFixture.nextMatch('opening-F', 'Initiative', { rolls: [6, 3], winner: 0, firstPlayer: 0 }))
      await page.waitForSelector('.initiative-opening-stage.settled', { timeout: 2500 })
      await page.evaluate(() => window.__openingFixture.sameMatchPhase('Main'))
      await page.waitForSelector('.initiative-resolution-message', { state: 'visible', timeout: 500 })
      await page.evaluate(() => window.__openingFixture.unmount())
      await page.waitForTimeout(40)
      assert.equal(await page.locator('.initiative-resolution-message').count(), 0, 'unmount left the teleported result behind')
      return { resultMs: 650, clicks: 1, overlayDuringResult: 0, resultObserved, resultFrame }
    } finally { await context.close() }
  })

  for (const motion of [
    { key: 'standard', reduced: 'no-preference', expected: [1050, 1550], css: [430, 520] },
    { key: 'fast', reduced: 'no-preference', expected: [520, 920], css: [220, 310] },
    { key: 'off', reduced: 'no-preference', expected: [0, 180], css: [0, 1] },
    { key: 'standard', reduced: 'reduce', expected: [0, 180], css: [0, 0] },
  ]) {
    const name = `motion-${motion.key}-${motion.reduced}`
    await runCase(name, async () => {
      const { context, page } = await openingPage(baseUrl, { width: 1366, height: 768 }, { 'opening-rolls': 'zero', motion: motion.key }, motion.reduced)
      try {
        await page.waitForSelector('[data-ui-contract="match-opening-stage"]')
        const animationDurations = await page.evaluate(() => [...document.querySelectorAll('.initiative-opening-stage article')].flatMap(element => element.getAnimations().map(animation => Number(animation.effect?.getTiming().duration) || 0)))
        const computedMotion = await page.evaluate(() => [...document.querySelectorAll('.initiative-opening-stage article,.initiative-opening-stage .initiative-die b,.initiative-opening-stage .initiative-clash')].map(element => {
          const style = getComputedStyle(element)
          return { animationName: style.animationName, animationDuration: style.animationDuration, transitionDuration: style.transitionDuration }
        }))
        const initialFaces = []
        if (motion.key === 'off' || motion.reduced === 'reduce') {
          for (let sample = 0; sample < 3; sample += 1) {
            initialFaces.push(await page.locator('.initiative-die em').allTextContents())
            await page.waitForTimeout(110)
          }
          assert.deepEqual(initialFaces[1], initialFaces[0], 'disabled motion still randomized dice')
          assert.deepEqual(initialFaces[2], initialFaces[0], 'disabled motion still randomized dice')
          assert.ok(computedMotion.every(style => style.animationName === 'none' || style.animationDuration.split(',').every(value => Number.parseFloat(value) === 0)), `computed animation remained active: ${JSON.stringify(computedMotion)}`)
          assert.ok(computedMotion.every(style => style.transitionDuration.split(',').every(value => Number.parseFloat(value) === 0)), `computed transition remained active: ${JSON.stringify(computedMotion)}`)
        }
        if (motion.css[1] === 0) assert.equal(animationDurations.length, 0)
        else if (motion.css[0] === 0) assert.ok(animationDurations.length === 0 || animationDurations.some(duration => duration <= motion.css[1]), `unexpected CSS durations ${animationDurations}`)
        else assert.ok(animationDurations.some(duration => duration >= motion.css[0] && duration <= motion.css[1]), `unexpected CSS durations ${animationDurations}`)
        const startedAt = Date.now()
        await page.evaluate(() => window.__openingFixture.nextMatch(`motion-${Date.now()}`, 'Initiative', { rolls: [6, 3], winner: 0, firstPlayer: 0 }))
        await page.waitForSelector('.initiative-opening-stage.settled', { timeout: 2200 })
        const elapsed = Date.now() - startedAt
        assert.ok(elapsed >= motion.expected[0] && elapsed <= motion.expected[1], `${name} settled in ${elapsed}ms`)
        const authoritativeFaces = await page.locator('.initiative-opening-stage article').evaluateAll(nodes => nodes.map(node => ({
          role: node.classList.contains('mine') ? 'mine' : 'opponent',
          face: node.querySelector('.initiative-die em')?.textContent?.trim() || '',
        })))
        assert.deepEqual(authoritativeFaces, [{ role: 'opponent', face: '3 点' }, { role: 'mine', face: '6 点' }])
        return { elapsed, animationDurations, computedMotion, initialFaces, authoritativeFaces }
      } finally { await context.close() }
    })
  }
} catch (error) {
  failed.push({ name: 'framework', error: error instanceof Error ? `${error.name}: ${error.message}` : String(error) })
} finally {
  if (browser) await browser.close().catch(() => {})
  await stopPreview().catch(() => {})
  const sourceAfter = Object.fromEntries(tracked.map(file => [file, sha(file)]))
  const report = {
    schemaVersion: 1,
    status: failed.length ? 'FAILED' : 'PASSED',
    scope: 'Current real GamePage/PromptOverlay SFC with sanitized local authority fixtures. No live server ACK, real account, physical phone, production write, persistence or deployment claim.',
    requestedCount: requested.length,
    finishedCount: finished.length,
    failedCount: failed.length,
    requested, finished, failed, browserErrors,
    sourceBefore, sourceAfter,
    sourceStable: JSON.stringify(sourceBefore) === JSON.stringify(sourceAfter),
    previewOriginalSha256,
    generatedAtUtc: new Date().toISOString(),
  }
  fs.writeFileSync(path.join(outputRoot, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: report.status, requested: requested.length, finished: finished.length, failed: failed.length, browserErrors: browserErrors.length, report: path.join(outputRoot, 'report.json') }))
}

if (failed.length) process.exit(1)
