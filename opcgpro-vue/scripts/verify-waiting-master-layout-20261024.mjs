import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import net from 'node:net'
import path from 'node:path'
import { execFileSync, spawn } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = path.resolve(process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/waiting-master-layout-20261024')
const screenshots = path.join(out, 'screenshots')
const repo = path.resolve(root, '..')
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
  '. "$env:L12_STORAGE_GUARD"; @($env:L12_QA_OUT,$env:L12_QA_SCREENSHOTS,$env:TEMP) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose "waiting and master layout browser QA" }'], {
  env: { ...process.env, L12_STORAGE_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'), L12_QA_OUT: out, L12_QA_SCREENSHOTS: screenshots },
  stdio: 'pipe', windowsHide: true,
})
fs.mkdirSync(out, { recursive: true })
fs.mkdirSync(screenshots, { recursive: true })

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const scope = process.env.L12_QA_SCOPE
assert(['full', 'geometry'].includes(scope), 'L12_QA_SCOPE must be exactly full or geometry')
const tracked = [
  'src/l12/game/GameBoard.vue', 'src/l12/game/PromptOverlay.vue', 'src/l12/game/PlayerMat.vue',
  'src/l12/mobileViewport.css', 'src/l12/motion.css',
  'scripts/test-waiting-master-layout-20261024.mjs', 'scripts/verify-waiting-master-layout-20261024.mjs',
]
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex').toUpperCase()
const before = Object.fromEntries(tracked.map(file => [file, sha(file)]))
const failures = []
const results = []
let preview
let browser

async function freePort() {
  const server = net.createServer()
  await new Promise((resolve, reject) => server.listen(0, '127.0.0.1', error => error ? reject(error) : resolve()))
  const address = server.address()
  await new Promise(resolve => server.close(resolve))
  if (!address || typeof address === 'string') throw new Error('preview port unavailable')
  return address.port
}

async function startPreview(port) {
  preview = spawn(process.execPath, ['scripts/preview-l12-battle-layout.mjs', '--port', String(port)], {
    cwd: root, env: process.env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
  })
  let output = ''
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`preview timeout\n${output}`)), 30000)
    const consume = chunk => {
      output += chunk.toString()
      if (output.includes('L12 battle layout preview:')) { clearTimeout(timer); resolve() }
    }
    preview.stdout.on('data', consume)
    preview.stderr.on('data', consume)
    preview.once('exit', code => { clearTimeout(timer); reject(new Error(`preview exited ${code}\n${output}`)) })
  })
}

async function stopPreview() {
  if (!preview || preview.exitCode !== null) return
  preview.kill('SIGINT')
  await Promise.race([new Promise(resolve => preview.once('exit', resolve)), new Promise(resolve => setTimeout(resolve, 5000))])
  if (preview.exitCode === null) preview.kill('SIGKILL')
}

async function run(name, task) {
  try { results.push({ name, detail: await task() }) }
  catch (error) { failures.push({ name, error: error instanceof Error ? `${error.name}: ${error.message}` : String(error) }) }
}

function rect(value) {
  return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height }
}

function contained(inner, outer, tolerance = 1) {
  return inner.left >= outer.left - tolerance && inner.top >= outer.top - tolerance
    && inner.right <= outer.right + tolerance && inner.bottom <= outer.bottom + tolerance
}

async function fixturePage(base, viewport, mobile = false) {
  const context = await browser.newContext({ viewport, hasTouch: mobile })
  const page = await context.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => {
    if (response.status() >= 400) errors.push(`HTTP ${response.status()} ${response.url()}`)
  })
  page.on('console', message => {
    if (message.type() === 'error' && !message.text().startsWith('Failed to load resource:')) errors.push(message.text())
  })
  await page.goto(`${base}?canvas=1&field=full&hand=7${mobile ? '&mobile=1' : ''}`, { waitUntil: 'domcontentloaded', timeout: 30000 })
  await page.waitForFunction(() => Boolean(window.__l12State?.game), null, { timeout: 20000 })
  return { context, page, errors }
}

async function setPassiveWait(page) {
  await page.evaluate(() => {
    const state = window.__l12State
    state.game.phase = 'Main'
    state.game.activePlayer = 1
    state.game.prompts = []
    state.game.waitingPrompt = {
      playerIndex: 1, playerName: '对手', kind: 'effect-target',
      waitingSummary: '对手正在选择效果对象',
    }
  })
  await page.locator('.l12-prompt-overlay.passive-waiting').waitFor()
}

const port = await freePort()
const base = `http://127.0.0.1:${port}/__l12_battle_preview__`
try {
  await startPreview(port)
  browser = await chromium.launch({ headless: true, channel: 'msedge' })

  await run('desktop pure wait is board-local and non-blocking', async () => {
    const { context, page, errors } = await fixturePage(base, { width: 1558, height: 1126 })
    await setPassiveWait(page)
    const detail = await page.evaluate(() => {
      const rectangle = value => ({ left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height })
      const overlay = document.querySelector('.l12-prompt-overlay.passive-waiting')
      const panel = overlay?.querySelector('.waiting-panel')
      const anchor = document.querySelector('.passive-wait-slot')
      const felt = document.querySelector('.felt-board')
      const target = document.querySelector('.my-half .formation-slot .card-tile')
      if (!(overlay instanceof HTMLElement) || !(panel instanceof HTMLElement)
        || !(anchor instanceof HTMLElement) || !(felt instanceof HTMLElement) || !(target instanceof HTMLElement)) throw new Error('desktop fixture elements missing')
      const targetRect = target.getBoundingClientRect()
      const hit = document.elementFromPoint(targetRect.left + targetRect.width / 2, targetRect.top + targetRect.height / 2)
      return {
        parentIsAnchor: overlay.parentElement === anchor,
        className: overlay.className,
        offsetParentClass: overlay.offsetParent?.className || '',
        overlay: { ...rectangle(overlay.getBoundingClientRect()), background: getComputedStyle(overlay).backgroundColor,
          backdrop: getComputedStyle(overlay).backdropFilter, pointerEvents: getComputedStyle(overlay).pointerEvents,
          position: getComputedStyle(overlay).position, widthRule: getComputedStyle(overlay).width, heightRule: getComputedStyle(overlay).height,
          inset: getComputedStyle(overlay).inset },
        panel: rectangle(panel.getBoundingClientRect()), anchor: rectangle(anchor.getBoundingClientRect()), felt: rectangle(felt.getBoundingClientRect()),
        minimizeCount: overlay.querySelectorAll('.prompt-minimize').length,
        targetHit: Boolean(hit && (hit === target || target.contains(hit))),
      }
    })
    await page.screenshot({ path: path.join(screenshots, 'desktop-passive-wait.png'), fullPage: true })
    assert(detail.parentIsAnchor)
    assert.equal(detail.overlay.background, 'rgba(0, 0, 0, 0)')
    assert.equal(detail.overlay.backdrop, 'none')
    assert.equal(detail.overlay.pointerEvents, 'none')
    assert.equal(detail.minimizeCount, 0)
    assert(detail.targetHit)
    assert(contained(detail.panel, detail.anchor), `desktop passive wait escaped its slot: ${JSON.stringify(detail)}`)
    assert(contained(detail.anchor, detail.felt))
    assert.deepEqual(errors, [])
    await context.close()
    return detail
  })

  if (scope === 'full') await run('an owner choice still uses the actionable overlay', async () => {
    const { context, page, errors } = await fixturePage(base, { width: 1280, height: 720 })
    await page.evaluate(() => {
      const state = window.__l12State
      state.game.phase = 'Main'
      state.game.waitingPrompt = { playerIndex: 1, playerName: '对手', kind: 'effect-target' }
      state.game.prompts = [{ promptId: 'owner-choice', playerIndex: 0, controller: 0, kind: 'option', text: '请选择',
        validChoices: ['yes', 'no'], minChoose: 1, maxChoose: 1, choiceLabels: { yes: '确认', no: '取消' }, data: {}, createdRevision: 1 }]
    })
    const overlay = page.locator('.l12-prompt-overlay:not(.passive-waiting)')
    await overlay.waitFor()
    assert.equal(await overlay.locator('.prompt-panel').count(), 1)
    assert.equal(await overlay.evaluate(node => node.parentElement?.classList.contains('passive-wait-slot')), false)
    assert.notEqual(await overlay.evaluate(node => getComputedStyle(node).pointerEvents), 'none')
    assert.deepEqual(errors, [])
    await context.close()
    return { actionable: true }
  })

  await run('phone portrait logical landscape pure wait remains board-local and non-blocking', async () => {
    const { context, page, errors } = await fixturePage(base, { width: 390, height: 844 }, true)
    await setPassiveWait(page)
    const detail = await page.evaluate(() => {
      const rectangle = value => ({ left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height })
      const overlay = document.querySelector('.l12-prompt-overlay.passive-waiting')
      const panel = overlay?.querySelector('.waiting-panel')
      const anchor = document.querySelector('.passive-wait-slot')
      const felt = document.querySelector('.felt-board')
      const target = document.querySelector('.my-half .formation-slot .card-tile')
      if (!(overlay instanceof HTMLElement) || !(panel instanceof HTMLElement)
        || !(anchor instanceof HTMLElement) || !(felt instanceof HTMLElement) || !(target instanceof HTMLElement)) throw new Error('mobile passive-wait fixture elements missing')
      const targetRect = target.getBoundingClientRect()
      const hit = document.elementFromPoint(targetRect.left + targetRect.width / 2, targetRect.top + targetRect.height / 2)
      return {
        rotated: document.documentElement.dataset.l12Rotated,
        parentIsAnchor: overlay.parentElement === anchor,
        className: overlay.className,
        offsetParentClass: overlay.offsetParent?.className || '',
        overlay: { ...rectangle(overlay.getBoundingClientRect()), background: getComputedStyle(overlay).backgroundColor,
          backdrop: getComputedStyle(overlay).backdropFilter, pointerEvents: getComputedStyle(overlay).pointerEvents,
          position: getComputedStyle(overlay).position, widthRule: getComputedStyle(overlay).width, heightRule: getComputedStyle(overlay).height,
          inset: getComputedStyle(overlay).inset },
        panel: rectangle(panel.getBoundingClientRect()), anchor: rectangle(anchor.getBoundingClientRect()), felt: rectangle(felt.getBoundingClientRect()),
        targetHit: Boolean(hit && (hit === target || target.contains(hit))),
      }
    })
    await page.screenshot({ path: path.join(screenshots, 'phone-portrait-passive-wait.png'), fullPage: true })
    assert.equal(detail.rotated, 'true')
    assert(detail.parentIsAnchor)
    assert.equal(detail.overlay.background, 'rgba(0, 0, 0, 0)')
    assert.equal(detail.overlay.backdrop, 'none')
    assert.equal(detail.overlay.pointerEvents, 'none')
    assert(detail.targetHit)
    assert(contained(detail.panel, detail.anchor), `mobile passive wait escaped its slot: ${JSON.stringify(detail)}`)
    assert(contained(detail.anchor, detail.felt))
    assert.deepEqual(errors, [])
    await context.close()
    return detail
  })

  if (scope === 'full') await run('deployed master retains only name and health in its zone', async () => {
    const { context, page, errors } = await fixturePage(base, { width: 1558, height: 1126 })
    await page.evaluate(() => {
      for (const player of window.__l12State.game.players) {
        player.master.masterName = player.playerIndex === 0 ? '孙悟空' : '测试主宰'
        player.master.deployedAsLegion = true
        player.master.hp = 6
        player.master.maxHp = 8
      }
    })
    const masters = page.locator('.battlefield-half .mini-master')
    assert.equal(await masters.count(), 2)
    const detail = await masters.evaluateAll(nodes => nodes.map(node => ({
      text: node.textContent?.replace(/\s+/g, ' ').trim(),
      artCount: node.querySelectorAll('.master-art').length,
      name: node.querySelector('.master-name')?.textContent?.trim(),
      health: node.querySelector('.master-health')?.textContent?.replace(/\s+/g, '').trim(),
    })))
    assert(detail.every(item => item.artCount === 0 && item.name && item.health === '6/8'))
    assert(detail.every(item => !item.text?.includes('已登场')))
    const myMaster = page.locator('.my-half .mini-master')
    await myMaster.hover()
    assert.match(await page.locator('.card-inspector').innerText(), /孙悟空/)
    assert.deepEqual(errors, [])
    await page.screenshot({ path: path.join(screenshots, 'desktop-master-deployed.png'), fullPage: true })
    await myMaster.click()
    await page.locator('.master-dialog').waitFor()
    await context.close()
    return { masters: detail, hoverFocused: true, clickOpenedDetail: true }
  })

  for (const profile of [
    { name: 'desktop', viewport: { width: 1558, height: 1126 }, mobile: false },
    { name: 'phone-portrait', viewport: { width: 390, height: 844 }, mobile: true },
  ]) await run(`${profile.name} master rest and ready geometry remains contained with upright labels`, async () => {
    const { context, page, errors } = await fixturePage(base, profile.viewport, profile.mobile)
    await page.evaluate(() => {
      for (const player of window.__l12State.game.players) {
        player.master.deployedAsLegion = false
        player.master.tapped = true
      }
    })
    await page.locator('.battlefield-half .mini-master.tapped .master-art').first().waitFor()
    const detail = await page.locator('.battlefield-half .mini-master.tapped').evaluateAll(nodes => {
      const rectangle = value => ({ left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height })
      return nodes.map(node => {
      const art = node.querySelector('.master-art')
      const name = node.querySelector('.master-name')
      const health = node.querySelector('.master-health')
      if (!(art instanceof HTMLElement) || !(name instanceof HTMLElement) || !(health instanceof HTMLElement)) throw new Error('rested master layers missing')
      return {
        outer: rectangle(node.getBoundingClientRect()), art: rectangle(art.getBoundingClientRect()),
        name: rectangle(name.getBoundingClientRect()), health: rectangle(health.getBoundingClientRect()),
        outerTransform: getComputedStyle(node).transform, artTransform: getComputedStyle(art).transform,
        nameTransform: getComputedStyle(name).transform, healthTransform: getComputedStyle(health).transform,
      }
    })})
    assert(detail.every(item => contained(item.art, item.outer)), `rested master art escaped its zone: ${JSON.stringify(detail)}`)
    assert(detail.every(item => contained(item.name, item.outer) && contained(item.health, item.outer)))
    assert(detail.every(item => item.outerTransform === 'none' && item.artTransform !== 'none'))
    assert(detail.every(item => item.nameTransform === 'none' && item.healthTransform === 'none'))
    assert.deepEqual(errors, [])
    await page.screenshot({ path: path.join(screenshots, `${profile.name}-master-rested.png`), fullPage: true })
    await page.evaluate(() => {
      for (const player of window.__l12State.game.players) player.master.tapped = false
    })
    await page.locator('.battlefield-half .mini-master:not(.tapped) .master-art').first().waitFor()
    const ready = await page.locator('.battlefield-half .mini-master:not(.tapped)').evaluateAll(nodes => {
      const rectangle = value => ({ left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height })
      return nodes.map(node => {
        const art = node.querySelector('.master-art')
        const name = node.querySelector('.master-name')
        const health = node.querySelector('.master-health')
        if (!(art instanceof HTMLElement) || !(name instanceof HTMLElement) || !(health instanceof HTMLElement)) throw new Error('ready master layers missing')
        return {
          outer: rectangle(node.getBoundingClientRect()), art: rectangle(art.getBoundingClientRect()),
          name: rectangle(name.getBoundingClientRect()), health: rectangle(health.getBoundingClientRect()),
          outerTransform: getComputedStyle(node).transform, artTransform: getComputedStyle(art).transform,
          nameTransform: getComputedStyle(name).transform, healthTransform: getComputedStyle(health).transform,
        }
      })
    })
    assert(ready.every(item => contained(item.art, item.outer)), `ready master art escaped its zone: ${JSON.stringify(ready)}`)
    assert(ready.every(item => contained(item.name, item.outer) && contained(item.health, item.outer)))
    assert(ready.every(item => item.outerTransform === 'none' && item.artTransform === 'none'))
    assert(ready.every(item => item.nameTransform === 'none' && item.healthTransform === 'none'))
    assert.deepEqual(errors, [])
    await page.screenshot({ path: path.join(screenshots, `${profile.name}-master-ready.png`), fullPage: true })
    await context.close()
    return { rested: detail, ready }
  })
} finally {
  if (browser) await browser.close()
  await stopPreview()
  const after = Object.fromEntries(tracked.map(file => [file, sha(file)]))
  const report = { status: failures.length ? 'failed' : 'passed', results, failures, sourceUnchanged: JSON.stringify(before) === JSON.stringify(after), before, after }
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
  assert(report.sourceUnchanged, 'verification mutated tracked source')
}

assert.deepEqual(failures, [])
console.log(`waiting/master layout browser verification passed (${results.length} cases)`)
