import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = process.env.L12_STAGE4B2_REPLAY_SCREENSHOT_DIR
  || path.resolve(root, '../artifacts/stage4b2-replay')
fs.mkdirSync(out, { recursive: true })

// Reuse the established real GamePage/ReplayPage browser harness. The event is
// the persisted shape of one public two-legion swap, including ordered facts.
const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = previous.slice(previous.indexOf('const entry = `') + 'const entry = `'.length,
  previous.indexOf('const isPicker='))
const visibility = fs.readFileSync(path.join(root, 'scripts/verify-referee-admin-visibility.mjs'), 'utf8')
const entryCode = visibility.slice(visibility.indexOf('const entry = setup +'), visibility.indexOf('const server ='))
let entry = new Function('setup', `${entryCode}; return entry`)(setup)
const card = id => ({ InstanceId: id, CardId: 'S01-0401', Name: '同名军团',
  CardType: 'legion', Faction: 'gaotianyuan', BaseTroops: 8000, Troops: 8000, Hidden: false })
const event = { Sequence: 21, Type: 'move', PlayerIndex: 0, Text: '不从原文推断格位',
  Cards: [card('swap-a'), card('swap-b')], PlayerBattlefieldMovement: { Facts: [
    { InstanceId: 'swap-a', BattlefieldPlayerIndex: 0, FromRow: 0, FromSlot: 0, ToRow: 1, ToSlot: 2 },
    { InstanceId: 'swap-b', BattlefieldPlayerIndex: 0, FromRow: 1, FromSlot: 2, ToRow: 0, ToSlot: 0 },
  ] } }
assert(entry.includes('Events:[]'), 'replay fixture changed')
entry = entry.replace('Events:[]', `Events:${JSON.stringify([event])}`)
assert(entry.includes('const routes='), 'replay routes changed')
entry = entry.replace('const routes=',
  "l12State.gmEnabled=params.has('staleGm');l12State.game.recentEvents=replayGameAt(detail,0).recentEvents;const routes=")

const server = await createServer({ root, configLoader: 'runner',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'stage4b2-replay', resolveId(id) { if (id === '/__stage4b2__.js') return id },
    load(id) { if (id === '/__stage4b2__.js') return entry },
    configureServer(dev) { dev.middlewares.use((req, res, next) => {
      if (!req.url?.startsWith('/__stage4b2__?')) return next()
      res.setHeader('Content-Type', 'text/html')
      res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__stage4b2__.js"></script>')
    }) },
  }] })
await server.listen()
const port = server.httpServer.address().port
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const [mode, staleGm, expected] of [
    ['public', false, '下方'], ['referee', false, '下方'], ['admin', false, '下方'],
    ['ordinary', false, '我方'], ['ordinary-match', false, '我方'],
    ['ordinary', true, '我方'], ['ordinary-match', true, '我方'],
  ]) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    try {
      await page.goto(`http://127.0.0.1:${port}/__stage4b2__?mode=${mode}${staleGm ? '&staleGm=1' : ''}`)
      const log = page.locator('.battle-event-log:visible').first()
      await log.waitFor()
      await log.locator('.action-toggle').click()
      const details = await log.locator('.combat-detail').innerText()
      assert(details.includes(`从${expected}前排左格移动至${expected}后排右格`),
        `${mode} staleGm=${staleGm}: incorrect first movement: ${details}`)
      assert(details.includes(`从${expected}后排右格移动至${expected}前排左格`),
        `${mode} staleGm=${staleGm}: incorrect second movement: ${details}`)
      assert(!/undefined|绝密|private-instance|不从原文/.test(await log.innerText()))
      assert.deepEqual(errors, [])
      const screenshot = path.join(out, `${mode}${staleGm ? '-stale-gm' : ''}.png`)
      await page.screenshot({ path: screenshot })
      results.push({ mode, staleGm, expected, screenshot })
    } finally { await page.close() }
  }
  console.log(JSON.stringify(results, null, 2))
} finally { await browser.close(); await server.close() }
