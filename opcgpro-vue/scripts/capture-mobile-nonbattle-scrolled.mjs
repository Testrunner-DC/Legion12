// Scrolled captures: reuses the mocked entry from capture-mobile-nonbattle.mjs,
// scrolls .site-content step by step and screenshots each position at 390x844.
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/mobile-nonbattle-review/scrolled')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const source = fs.readFileSync(path.join(root, 'scripts/capture-mobile-nonbattle.mjs'), 'utf8')
const start = source.indexOf('const entry = `') + 'const entry = `'.length
const end = source.indexOf('`\n\nlet browser')
const entry = source.slice(start, end)

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-mobile-nonbattle-review'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'mobile-nonbattle-scrolled-fixture',
    resolveId(id) { if (id === '/__mrs__.js') return id },
    load(id) { if (id === '/__mrs__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__mrs__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<head><meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover"></head><style>html,body,#app{margin:0;min-height:100%;background:#080d11}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__mrs__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})
await server.listen()
const port = server.httpServer.address().port
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const page = await (await browser.newContext({ deviceScaleFactor: 2, hasTouch: true, isMobile: true })).newPage()
await page.setViewportSize({ width: 390, height: 844 })
page.on('pageerror', error => console.log('PAGEERROR', error.message))

const routes = [
  ['home', '/', '.official-home'],
  ['news', '/news', '.news-page'],
  ['rules', '/rules', '.rule-tools input'],
  ['cards', '/cards', '.archive-grid'],
  ['decks', '/decks', '.deck-page'],
  ['battle-hub', '/battle', '.battle-hub'],
  ['rankings', '/battle/rankings', '.ranking-page'],
  ['friends', '/battle/friends', '.friends-page'],
  ['tournaments', '/battle/tournaments', '.tournament-page'],
  ['records', '/battle/records', '.match-records, .records-header, main'],
  ['profile', '/me', '.profile-page, main'],
]

for (const [name, route, wait] of routes) {
  await page.goto(`http://127.0.0.1:${port}/__mrs__?route=${encodeURIComponent(route)}`)
  try { await page.locator(wait).first().waitFor({ timeout: 9000 }) } catch { console.log('wait timeout', name) }
  await page.waitForTimeout(900)
  const metrics = await page.evaluate(() => {
    const scroller = document.querySelector('.site-content')
    return scroller ? { height: scroller.clientHeight, total: scroller.scrollHeight } : null
  })
  if (!metrics) { console.log('no scroller', name); continue }
  const positions = []
  for (let y = metrics.height; y < metrics.total - 8 && positions.length < 4; y += metrics.height) positions.push(y)
  positions.forEach((y, index) => console.log(`${name}: scroll ${index + 1}/${positions.length} -> ${y} (total ${metrics.total})`))
  for (const [index, y] of positions.entries()) {
    await page.evaluate(top => { document.querySelector('.site-content').scrollTop = top }, y)
    await page.waitForTimeout(500)
    await page.screenshot({ path: path.join(output, `${name}-s${index + 1}.png`) })
  }
}

// 卡组库「公开牌库」标签
await page.goto(`http://127.0.0.1:${port}/__mrs__?route=${encodeURIComponent('/decks')}`)
await page.locator('.deck-page').waitFor({ timeout: 9000 })
await page.waitForTimeout(800)
const plazaTab = page.locator('.deck-tabs button', { hasText: '公开牌库' })
if (await plazaTab.count()) {
  await plazaTab.first().click()
  await page.waitForTimeout(900)
  await page.screenshot({ path: path.join(output, 'decks-plaza.png') })
  await page.evaluate(() => { const s = document.querySelector('.site-content'); s.scrollTop = s.clientHeight })
  await page.waitForTimeout(500)
  await page.screenshot({ path: path.join(output, 'decks-plaza-s1.png') })
}

// 图鉴筛选抽屉
await page.goto(`http://127.0.0.1:${port}/__mrs__?route=${encodeURIComponent('/cards')}`)
await page.locator('.archive-grid').waitFor({ timeout: 9000 })
await page.waitForTimeout(800)
const filterTrigger = page.locator('.mobile-filter-trigger')
if (await filterTrigger.count()) {
  await filterTrigger.first().click()
  await page.waitForTimeout(600)
  await page.screenshot({ path: path.join(output, 'cards-filter-sheet.png') })
}

// 对战大厅「好友房」与「单人」标签
await page.goto(`http://127.0.0.1:${port}/__mrs__?route=${encodeURIComponent('/battle')}`)
await page.locator('.battle-hub').waitFor({ timeout: 9000 })
await page.waitForTimeout(900)
for (const label of ['好友房', '单人']) {
  const tab = page.locator('.mode-tabs button', { hasText: label })
  if (await tab.count()) {
    await tab.first().click()
    await page.waitForTimeout(800)
    await page.screenshot({ path: path.join(output, `battle-hub-${label}.png`) })
  }
}

// 对局记录：选中一场
await page.goto(`http://127.0.0.1:${port}/__mrs__?route=${encodeURIComponent('/battle/records')}`)
await page.waitForTimeout(2500)
const recordRow = page.locator('main button, .match-records button').first()
if (await recordRow.count()) { await recordRow.click().catch(() => {}); await page.waitForTimeout(700) }
await page.screenshot({ path: path.join(output, 'records-selected.png') })

await browser.close()
await server.close()
console.log('done ->', output)

