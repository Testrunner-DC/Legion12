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
const output = path.resolve(root, '../artifacts/stage4c1-paid-cost')
fs.mkdirSync(output, { recursive: true })
const prior = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = prior.slice(prior.indexOf('const entry = `') + 'const entry = `'.length,
  prior.indexOf('const isPicker='))
const visibility = fs.readFileSync(path.join(root, 'scripts/verify-referee-admin-visibility.mjs'), 'utf8')
const entryCode = visibility.slice(visibility.indexOf('const entry = setup +'), visibility.indexOf('const server ='))
let entry = new Function('setup', `${entryCode}; return entry`)(setup)
const publicCard = { InstanceId: 'public-paid', CardId: 'S01-01C1', Name: '士气·天廷',
  CardType: 'rune', Faction: 'tianting', Hidden: false }
const hiddenCard = { ...publicCard, InstanceId: 'hidden-paid', Hidden: true }
const group = (id, sequence, cards, outcome) => [
  { Sequence: sequence, Type: 'effect-activation', PlayerIndex: 0,
    Text: '发动主动效果', Cards: cards,
    PlayerLogGroupId: id, PlayerLogTiming: 'active' },
  { Sequence: sequence + 1, Type: 'effect-result', PlayerIndex: 0,
    Text: '效果被无效', Cards: cards, PlayerLogGroupId: id,
    PlayerLogTiming: 'active', EffectResultStatus: 'negated',
    ...(outcome ? { PlayerLogSemantic: { SourceInstanceId: cards[0].InstanceId,
      ActionLabel: '效果结果', OutcomeLabel: outcome } } : {}),
  },
]
const events = [
  ...group('paid-public', 21, [publicCard], '已支付费用：消耗2士气'),
  ...group('paid-legacy', 23, [{ ...publicCard, InstanceId: 'legacy-paid' }], null),
  ...group('paid-hidden', 25, [hiddenCard], '已支付费用：消耗2士气'),
  ...group('paid-empty', 27, [{ ...publicCard, InstanceId: 'empty-paid' }], '已支付费用：  '),
]
assert(entry.includes('Events:[]'), 'replay fixture changed')
entry = entry.replace('Events:[]', `Events:${JSON.stringify(events)}`)
entry = entry.replace('const routes=',
  "l12State.gmEnabled=params.has('staleGm');l12State.game.recentEvents=replayGameAt(detail,0).recentEvents;const routes=")

const server = await createServer({ root, configLoader: 'runner',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'stage4c1-paid-cost', resolveId(id) { if (id === '/__stage4c1__.js') return id },
    load(id) { if (id === '/__stage4c1__.js') return entry },
    configureServer(dev) { dev.middlewares.use((req, res, next) => {
      if (!req.url?.startsWith('/__stage4c1__?')) return next()
      res.setHeader('Content-Type', 'text/html')
      res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__stage4c1__.js"></script>')
    }) },
  }] })
await server.listen()
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const [mode, viewport, staleGm = false] of [
    ['public', { width: 1440, height: 900 }],
    ['public', { width: 390, height: 844 }],
    ['public', { width: 568, height: 320 }],
    ['referee', { width: 1440, height: 900 }],
    ['ordinary', { width: 1440, height: 900 }],
    ['ordinary-match', { width: 1440, height: 900 }],
    ['admin', { width: 1440, height: 900 }],
    ['ordinary', { width: 1440, height: 900 }, true],
  ]) {
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    try {
      await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__stage4c1__?mode=${mode}${staleGm ? '&staleGm=1' : ''}`)
      const trigger = page.locator('.mobile-record-trigger:visible')
      if (await trigger.count()) await trigger.first().click()
      const log = page.locator('.battle-event-log:visible').first()
      await log.waitFor()
      const paid = log.locator('[data-event-sequence="21"]')
      assert.equal(await paid.locator('.event-badge').innerText(), '费用已支付')
      await paid.scrollIntoViewIfNeeded()
      const basename = `${mode}${staleGm ? '-stale' : ''}-${viewport.width}x${viewport.height}`
      const screenshot = path.join(output, `${basename}-summary.png`)
      await page.screenshot({ path: screenshot })
      await paid.locator('.action-toggle').click()
      assert((await paid.locator('.combat-detail').innerText()).includes('已支付费用：消耗2士气'))
      const legacy = log.locator('[data-event-sequence="23"]')
      assert.equal(await legacy.locator('.event-badge').count(), 0)
      assert.equal(await log.locator('[data-event-sequence="25"]').count(), 0)
      const empty = log.locator('[data-event-sequence="27"]')
      assert.equal(await empty.locator('.event-badge').count(), 0)
      await empty.locator('.action-toggle').click()
      assert(!(await empty.locator('.combat-detail').innerText()).includes('已支付费用：'))
      assert.deepEqual(errors, [])
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
      assert(overflow <= 2, `${mode} ${viewport.width}: overflow ${overflow}`)
      await paid.locator('.combat-detail').scrollIntoViewIfNeeded()
      await page.screenshot({ path: path.join(output, `${basename}-detail.png`) })
      results.push({ mode, staleGm, viewport, overflow, screenshot })
    } finally { await page.close() }
  }
  console.log(JSON.stringify(results, null, 2))
} finally { await browser.close(); await server.close() }
