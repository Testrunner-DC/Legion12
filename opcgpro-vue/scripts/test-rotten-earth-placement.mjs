import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import net from 'node:net'
import { spawn, execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createHash, randomUUID } from 'node:crypto'

const root = path.resolve(import.meta.dirname, '..')
const repo = path.dirname(root)
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = process.env.L12_ROTTEN_OUT
const cache = process.env.L12_WORK_CACHE
const pwsh = process.env.L12_PWSH
assert.ok(out && cache && pwsh, 'Explicit governed output/cache/PowerShell paths are required')
const temp = path.join(cache, 't')
const viteCache = path.join(cache, 'vite-rotten-earth')
const profile = path.join(temp, `rotten-browser-${randomUUID()}`)
const quote = value => `'${value.replaceAll("'", "''")}'`
execFileSync(pwsh, ['-NoProfile', '-Command', `. ${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; ${[out, temp, viteCache, profile].map(p => `$null=Assert-L12GeneratedPath -Path ${quote(p)} -Purpose 'rotten-earth actual SFC QA'`).join('; ')}`], { encoding: 'utf8', windowsHide: true })
assert.equal(fs.existsSync(path.join(out, 'result.json')), false, 'Never overwrite previous QA evidence')
fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(temp, { recursive: true })
const sourceFiles = ['src/l12/game/GameBoard.vue', 'src/l12/game/PlayerMat.vue', 'src/l12/types.ts'].map(p => path.join(root, p))
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex')
const pins = sourceFiles.map(file => ({ file, sha256: hash(file) }))
const socket = net.createServer()
await new Promise((resolve, reject) => { socket.once('error', reject); socket.listen(0, '127.0.0.1', resolve) })
const port = socket.address().port
await new Promise(resolve => socket.close(resolve))
const base = `http://127.0.0.1:${port}/__l12_battle_preview__`
const logs = []
const server = spawn(process.execPath, [path.join(root, 'scripts/preview-l12-battle-layout.mjs'), '--port', String(port)], {
  cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  env: { ...process.env, L12_VITE_CACHE_DIR: viteCache },
})
server.stdout.on('data', chunk => logs.push(chunk.toString()))
server.stderr.on('data', chunk => logs.push(chunk.toString()))
const serverExited = new Promise(resolve => server.once('exit', (code, signal) => resolve({ code, signal })))
const results = [], pageErrors = [], consoleErrors = [], blockedExternal = []
let context, page, failure, serverExit, profileRemoved = false
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const slot = (actor, row, index) => `.formation-slot[data-tutorial-player-index="${actor}"][data-tutorial-row="${row}"][data-tutorial-slot="${index}"]:visible`
async function ready() {
  for (let attempt = 0; attempt < 150; attempt++) {
    if (server.exitCode !== null) throw new Error(`Owned preview exited before ready: ${server.exitCode}`)
    try { if ((await fetch(base)).ok) return } catch {}
    await delay(100)
  }
  throw new Error('Owned preview readiness deadline exceeded')
}
async function configure(actor, kind, rows = [0], observer = false) {
  await page.goto(`${base}?canvas=1&hand=3${context._rottenTouch ? '&mobile=1' : ''}${observer ? '&spectator=1' : ''}`, { waitUntil: 'domcontentloaded' })
  await page.locator('.formation-slot').first().waitFor({ state: 'attached' })
  await page.evaluate(async ({ actor, kind, rows, observer }) => {
    const { loadDeckCatalog } = await import('/src/l12/decks.ts')
    const catalog = await loadDeckCatalog()
    const state = window.__l12State, game = state.game
    const make = (id, instanceId) => {
      const definition = catalog.find(card => card.id === id)
      if (!definition) throw new Error(`Synthetic catalog card missing: ${id}`)
      return { ...definition, cardId: id, instanceId, name: definition.nameZh, cost: definition.cost || 0,
        playCost: 0, minimumPlayCost: 0, cardType: definition.cardType, effectText: definition.effect,
        troops: definition.troops || 0, baseTroops: definition.troops || 0, ownerIndex: actor,
        tapped: false, hidden: false, summonRound: -1 }
    }
    game.you = actor; game.activePlayer = actor; game.phase = 'Main'; game.prompts = []
    game.effectStack = []; game.pendingDefense = null; game.recentEvents = []; game.lastAction = null
    game.activeDisaster = make(rows === null ? 'S01-DS02' : 'S01-DS03', 'rotten-public-disaster')
    if (rows === null) delete game.legionPlacementRows
    else game.legionPlacementRows = rows
    for (const player of game.players) { player.field = [[null, null, null], [null, null, null]]; player.hand = []; player.handCount = 0 }
    const owner = game.players[actor]
    owner.hand = [make(kind === 'counter' ? 'S01-0018' : 'ST03-09', 'rotten-hand')]; owner.handCount = 1
    if (kind === 'legion') owner.field[1][1] = { ...make('S01-0018', 'rotten-covered-counter'), hidden: true, isCounterTactic: true }
    state.spectating = observer; state.pendingAction = false
    window.__sentCommands.length = 0
    window.__rottenPointers = []
    document.addEventListener('pointerup', event => window.__rottenPointers.push(event.pointerType), { passive: true })
  }, { actor, kind, rows, observer })
  await page.waitForFunction(({ actor, observer }) => window.__l12State.game.you === actor && window.__l12State.spectating === observer, { actor, observer })
}
async function interact(locator) { if (context._rottenTouch) await locator.tap(); else await locator.click() }
async function arm() {
  await interact(page.locator('.hand-card-wrap[data-flip-id="rotten-hand"] .card-tile:visible'))
  // Desktop selection arms a legion directly; the mobile path requires explicit confirmation.
  if (context._rottenTouch)
    await interact(page.locator('[data-tutorial-action="play-card"][data-tutorial-card-instance="rotten-hand"]:visible'))
}
async function sent() { return page.evaluate(() => window.__sentCommands) }
async function assertAvailable(actor, row, expected) {
  await page.waitForFunction(({ actor, row, expected }) => document.querySelectorAll(`.formation-slot.available[data-tutorial-player-index="${actor}"][data-tutorial-row="${row}"]`).length === expected, { actor, row, expected })
}
async function checkPacket(actor, row, index) {
  const packets = await sent(); assert.equal(packets.length, 1, 'one native action must produce exactly one command')
  assert.equal(packets[0].type, 'gameAction'); assert.equal(packets[0].command.type, 'playCard')
  assert.equal(packets[0].command.row, row); assert.equal(packets[0].command.slot, index)
  assert.equal(packets[0].command.targetPlayerIndex, actor)
}
try {
  await ready()
  for (const variant of [{ name: 'wide', width: 1440, height: 900, touch: false }, { name: 'touch', width: 844, height: 390, touch: true }]) {
    context = await chromium.launchPersistentContext(profile, { headless: true, channel: 'msedge', viewport: { width: variant.width, height: variant.height }, hasTouch: variant.touch })
    context._rottenTouch = variant.touch
    await context.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname === '127.0.0.1' && url.port === String(port)) return route.continue()
      blockedExternal.push(url.origin + url.pathname); return route.abort()
    })
    page = await context.newPage(); page.setDefaultTimeout(12000)
    page.on('pageerror', error => pageErrors.push(error.message))
    page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()) })
    for (const actor of [0, 1]) {
      await configure(actor, 'legion'); await arm()
      await assertAvailable(actor, 0, 3); await assertAvailable(actor, 1, 0)
      if ((variant.name === 'wide' && actor === 0) || (variant.name === 'touch' && actor === 1))
        await page.screenshot({ path: path.join(out, `${variant.name}-player-${actor}-restricted.png`) })
      await interact(page.locator(slot(actor, 1, 0))); assert.equal((await sent()).length, 0, 'rear empty slot must not dispatch')
      await interact(page.locator(slot(actor, 1, 1))); assert.equal((await sent()).length, 0, 'rear replaceable counter must not dispatch')
      await interact(page.locator(slot(actor, 0, 0))); await checkPacket(actor, 0, 0)
      const pointers = await page.evaluate(() => window.__rottenPointers)
      assert.ok(pointers.includes(variant.touch ? 'touch' : 'mouse'), 'actual native input path must be recorded')
      results.push({ viewport: variant.name, actor, case: 'restricted-legion-empty-and-counter-rear-blocked-front-sent', pointers })

      await configure(actor, 'counter'); await arm()
      await assertAvailable(actor, 0, 0); await assertAvailable(actor, 1, 3)
      await interact(page.locator(slot(actor, 1, 0))); await checkPacket(actor, 1, 0)
      results.push({ viewport: variant.name, actor, case: 'counter-cover-rear-still-allowed' })

      await configure(actor, 'legion', null); await arm()
      await assertAvailable(actor, 0, 3); await assertAvailable(actor, 1, 3)
      await interact(page.locator(slot(actor, 1, 0))); await checkPacket(actor, 1, 0)
      results.push({ viewport: variant.name, actor, case: 'older-snapshot-missing-field-compatibility' })

      await configure(actor, 'legion'); await arm(); await assertAvailable(actor, 1, 0)
      await page.evaluate(() => { window.__l12State.game.legionPlacementRows = [0, 1]; window.__l12State.game.activeDisaster = null })
      await assertAvailable(actor, 1, 3)
      await interact(page.locator(slot(actor, 1, 0))); await checkPacket(actor, 1, 0)
      results.push({ viewport: variant.name, actor, case: 'same-mounted-sfc-authority-updates-after-disaster-change' })

      await configure(actor, 'legion', [0], true)
      assert.equal(await page.locator('.formation-slot.available').count(), 0)
      await interact(page.locator(slot(actor, 0, 0))); await interact(page.locator(slot(actor, 1, 0)))
      assert.equal((await sent()).length, 0, 'observer must not dispatch placement')
      results.push({ viewport: variant.name, actor, case: 'observer-read-only' })
      console.log(`Rotten earth actual SFC ${variant.name}/player${actor}: 5 cases passed`)
    }
    await context.close(); context = null; page = null
  }
  assert.deepEqual(pageErrors, [], 'actual component runtime must have no uncaught errors')
  for (const pin of pins) assert.equal(hash(pin.file), pin.sha256, 'source changed while QA ran')
} catch (error) {
  failure = error
  if (page) await page.screenshot({ path: path.join(out, 'failure.png') }).catch(() => {})
} finally {
  if (context) await context.close().catch(() => {})
  server.kill('SIGTERM')
  serverExit = await Promise.race([serverExited, delay(10000).then(() => null)])
  if (!serverExit) { server.kill('SIGKILL'); serverExit = await serverExited }
  if (fs.existsSync(profile)) {
    const real = fs.realpathSync(profile), parent = fs.realpathSync(temp)
    assert.ok(real.startsWith(parent + path.sep) && /^rotten-browser-[0-9a-f-]{36}$/.test(path.basename(real)))
    fs.rmSync(real, { recursive: true }); profileRemoved = true
  }
  fs.writeFileSync(path.join(out, 'server.log'), logs.join(''), { flag: 'wx' })
  fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify({ passed: !failure, failure: failure?.stack,
    scope: 'Real GameBoard/PlayerMat in Edge; synthetic snapshot and mock websocket dispatch only, not real server ACK or physical device',
    pins, results, pageErrors, consoleErrors, blockedExternal, serverPid: server.pid, serverExit, profileRemoved,
  }, null, 2), { flag: 'wx' })
}
if (failure) throw failure
console.log(JSON.stringify({ passed: true, cases: results.length, out, serverExit, profileRemoved }))
