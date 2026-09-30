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
const output = path.resolve(root, '../artifacts/stage3c-disaster-value')
fs.mkdirSync(output, { recursive: true })
const prior = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const setup = prior.slice(prior.indexOf('const entry = `') + 'const entry = `'.length,
  prior.indexOf('const isPicker='))
const visibility = fs.readFileSync(path.join(root, 'scripts/verify-referee-admin-visibility.mjs'), 'utf8')
const entryCode = visibility.slice(visibility.indexOf('const entry = setup +'), visibility.indexOf('const server ='))
let entry = new Function('setup', `${entryCode}; return entry`)(setup)
assert(entry.includes('Events:[]'), 'replay fixture changed')
entry = entry.replace('Events:[]', `Events:${JSON.stringify([
  { Sequence: 21, Type: 'disaster-value', PlayerIndex: null,
    Text: '秘密来源／伪造天灾值 999→1000', Cards: [],
    PlayerDisasterValue: { Before: 4, After: 5 } },
  { Sequence: 22, Type: 'disaster-value', PlayerIndex: null,
    Text: '旧记录天灾值 5→8', Cards: [] },
])}`)
entry = entry.replace('const routes=',
  "l12State.game.recentEvents=replayGameAt(detail,0).recentEvents;const routes=")

const server = await createServer({ root, configLoader: 'runner',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'stage3c-disaster-value', resolveId(id) { if (id === '/__stage3c__.js') return id },
    load(id) { if (id === '/__stage3c__.js') return entry },
    configureServer(dev) { dev.middlewares.use((req, res, next) => {
      if (!req.url?.startsWith('/__stage3c__?')) return next()
      res.setHeader('Content-Type', 'text/html')
      res.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__stage3c__.js"></script>')
    }) },
  }] })
await server.listen()
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const [mode, viewport] of [
    ['public', { width: 1440, height: 900 }],
    ['public', { width: 320, height: 568 }],
    ['public', { width: 360, height: 800 }],
    ['public', { width: 390, height: 844 }],
    ['public', { width: 430, height: 932 }],
    ['public', { width: 568, height: 320 }],
    ['public', { width: 667, height: 375 }],
    ['referee', { width: 1440, height: 900 }],
  ]) {
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    try {
      await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__stage3c__?mode=${mode}`)
      const trigger = page.locator('.mobile-record-trigger:visible')
      if (await trigger.count()) await trigger.first().click()
      const log = page.locator('.battle-event-log:visible').first()
      await log.waitFor().catch(async error => {
        console.error(JSON.stringify({ mode, viewport, errors,
          body: (await page.locator('body').innerText()).slice(0, 1000) }))
        throw error
      })
      const current = log.locator('[data-event-sequence="21"]')
      const legacy = log.locator('[data-event-sequence="22"]')
      assert((await current.innerText()).includes('天灾值 4→5'))
      assert((await legacy.innerText()).includes('详情未记录'))
      assert(!(await log.innerText()).includes('秘密来源'))
      assert(!(await log.innerText()).includes('999'))
      assert.deepEqual(errors, [])
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
      assert(overflow <= 2, `${mode} ${viewport.width}: overflow ${overflow}`)
      await current.scrollIntoViewIfNeeded()
      const screenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`)
      await page.screenshot({ path: screenshot })
      results.push({ mode, viewport, overflow, screenshot })
    } finally { await page.close() }
  }
  console.log(JSON.stringify(results, null, 2))
} finally { await browser.close(); await server.close() }
