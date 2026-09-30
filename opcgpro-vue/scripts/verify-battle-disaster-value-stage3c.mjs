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
// The standalone fixture bypasses App.vue, which normally owns this global
// stylesheet. Without it the logical viewport reports rotated=true while its
// Teleport host remains an unrotated zero-height div.
assert(entry.includes("import '/src/style.css'"), 'global fixture entry changed')
entry = entry.replace("import '/src/style.css'",
  "import '/src/style.css'\nimport '/src/l12/mobileViewport.css'")
assert(entry.includes('Events:[]'), 'replay fixture changed')
entry = entry.replace('Events:[]', `Events:${JSON.stringify([
  ...Array.from({ length: 18 }, (_, index) => ({
    Sequence: index + 1, Type: 'disaster-value', PlayerIndex: null,
    Text: '旧式占位记录', Cards: [],
    PlayerDisasterValue: { Before: index, After: index + 1 },
  })),
  { Sequence: 21, Type: 'disaster-value', PlayerIndex: null,
    Text: '秘密来源／伪造天灾值 999→1000', Cards: [],
    PlayerDisasterValue: { Before: 4, After: 5 } },
  { Sequence: 22, Type: 'disaster-value', PlayerIndex: null,
    Text: '旧记录天灾值 5→8', Cards: [] },
  { Sequence: 23, Type: 'turn-start', PlayerIndex: 0,
    Text: '第 4 回合', Cards: [], PlayerLogGroupId: 'turn:4', PlayerLogTiming: 'turn-start' },
  { Sequence: 24, Type: 'draw', PlayerIndex: 0,
    Text: '抽取 2 张牌', Cards: [], PlayerLogGroupId: 'turn:4', PlayerLogTiming: 'turn-start' },
  { Sequence: 25, Type: 'disaster-value', PlayerIndex: null,
    Text: '秘密来源／伪造天灾值 999→1000', Cards: [],
    PlayerDisasterValue: { Before: 5, After: 6 },
    PlayerLogGroupId: 'turn:4', PlayerLogTiming: 'turn-start' },
])}`)
entry = entry.replace('const routes=',
  `l12State.game.recentEvents=replayGameAt(detail,0).recentEvents;
  if(mode==='player')l12State.rankedClock={serverUtcMs:Date.now(),receivedAtMs:Date.now(),
    totalLimitMs:900000,operationLimitMs:45000,reconnectLimitMs:30000,
    players:[{playerIndex:0,totalRemainingMs:600000,operationRemainingMs:35000,acting:true,connected:true},
      {playerIndex:1,totalRemainingMs:620000,operationRemainingMs:45000,acting:false,connected:true}]};
  const routes=`)

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
const textRects = locator => locator.evaluate(node => {
  const range = document.createRange()
  range.selectNodeContents(node)
  return [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0)
    .map(({ left, top, right, bottom }) => ({ left, top, right, bottom }))
})
try {
  for (const [mode, viewport] of [
    ['public', { width: 1440, height: 900 }],
    ['public', { width: 320, height: 568 }],
    ['public', { width: 360, height: 800 }],
    ['public', { width: 390, height: 844 }],
    ['public', { width: 430, height: 932 }],
    ['public', { width: 568, height: 320 }],
    ['public', { width: 667, height: 375 }],
    ['player', { width: 320, height: 568 }],
    ['player', { width: 360, height: 800 }],
    ['player', { width: 390, height: 844 }],
    ['player', { width: 430, height: 932 }],
    ['player', { width: 568, height: 320 }],
    ['ordinary', { width: 320, height: 568 }],
    ['ordinary', { width: 360, height: 800 }],
    ['ordinary', { width: 390, height: 844 }],
    ['ordinary', { width: 430, height: 932 }],
    ['ordinary', { width: 568, height: 320 }],
    ['ordinary', { width: 1440, height: 900 }],
    ['referee', { width: 1440, height: 900 }],
  ]) {
    if (process.argv[2] && process.argv[2] !== `${mode}-${viewport.width}x${viewport.height}`) continue
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    try {
      await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__stage3c__?mode=${mode}`)
      if (mode === 'ordinary' && viewport.width < 700) {
        const blocked = page.locator('.replay-mobile-blocked')
        await blocked.waitFor()
        assert((await blocked.innerText()).includes('请到电脑端查看回放'))
        assert.equal(await page.locator('.replay-controls,.mobile-record-overlay').count(), 0,
          'mobile replay remains blocked; playback and battle record cannot overlap')
        const back = await blocked.locator('button').evaluate(node => {
          const box = node.getBoundingClientRect()
          const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2)
          return { left: box.left, top: box.top, right: box.right, bottom: box.bottom,
            hit: hit === node || node.contains(hit) }
        })
        assert(back.hit && back.left >= 0 && back.top >= 0
          && back.right <= viewport.width && back.bottom <= viewport.height,
        `${viewport.width}: blocked replay return is clipped or not hit-testable`)
        const screenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`)
        await page.screenshot({ path: screenshot })
        results.push({ mode, viewport, blocked: true, back, screenshot })
        continue
      }
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
      const summary = log.locator('[data-event-sequence="24"]')
      assert((await current.innerText()).includes('天灾值 4→5'))
      assert((await legacy.innerText()).includes('详情未记录'))
      assert((await summary.innerText()).includes('回合开始，抽取2张牌，天灾值 5→6'))
      assert(!(await log.innerText()).includes('秘密来源'))
      assert(!(await log.innerText()).includes('999'))
      assert.deepEqual(errors, [])
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)
      assert(overflow <= 2, `${mode} ${viewport.width}: overflow ${overflow}`)
      if (viewport.width < 700) {
        const list = page.locator('.mobile-record-overlay .event-list')
        await list.evaluate(node => { node.scrollTop = 0 })
        assert.equal(await list.evaluate(node => node.scrollTop), 0,
          `${viewport.width}: record list did not scroll to its start`)
      }
      await current.scrollIntoViewIfNeeded()
      const geometry = await page.evaluate(() => {
        const rect = node => {
          if (!(node instanceof HTMLElement)) return null
          const { left, top, right, bottom, width, height } = node.getBoundingClientRect()
          return { left, top, right, bottom, width, height }
        }
        const select = selector => rect(document.querySelector(selector))
        const button = node => {
          const box = rect(node)
          if (!box) return null
          const centerX = (box.left + box.right) / 2
          const centerY = (box.top + box.bottom) / 2
          const hit = document.elementFromPoint(centerX, centerY)
          return { text: node.textContent?.trim(), box, hit: hit === node || node.contains(hit) }
        }
        const list = document.querySelector('.mobile-record-overlay .event-list')
        return {
          rotated: document.documentElement.dataset.l12Rotated,
          hostTransform: getComputedStyle(document.querySelector('#l12-landscape-teleports')).transform,
          host: select('#l12-landscape-teleports'),
          overlay: select('.mobile-record-overlay'),
          current: select('.mobile-record-overlay [data-event-sequence="21"] .event-message'),
          route: select('.battle-route-controls'),
          routeButtons: [...document.querySelectorAll('.battle-route-controls button')].map(button),
          closeButtons: [...document.querySelectorAll('.mobile-record-overlay header button')].map(button),
          dock: select('.mobile-battle-dock'),
          utility: select('.mobile-battle-dock__utility'),
          clock: select('.mobile-timed-clocks'),
          clockParts: [...document.querySelectorAll('.mobile-timed-clocks .mobile-rail-clock')].map(rect),
          action: select('.mobile-battle-dock__primary'),
          actionButtons: [...document.querySelectorAll('.mobile-battle-dock__primary button')].map(button),
          playback: button(document.querySelector('.replay-controls .play')),
          replayReturn: button(document.querySelector('.replay-route-controls button')),
          logicalRecordWidth: document.querySelector('.mobile-record-overlay')?.offsetWidth ?? 0,
          dialogWidth: parseFloat(getComputedStyle(document.documentElement)
            .getPropertyValue('--l12-mobile-dialog-width')),
          listBox: rect(list),
          list: list ? { scrollHeight: list.scrollHeight, clientHeight: list.clientHeight,
            scrollTop: list.scrollTop } : null,
        }
      })
      const inside = (outer, inner) => outer && inner && inner.left >= outer.left - 1
        && inner.top >= outer.top - 1 && inner.right <= outer.right + 1
        && inner.bottom <= outer.bottom + 1
      const overlap = (left, right) => left && right
        ? Math.max(0, Math.min(left.right, right.right) - Math.max(left.left, right.left))
          * Math.max(0, Math.min(left.bottom, right.bottom) - Math.max(left.top, right.top))
        : 0
      const screen = { left: 0, top: 0, right: viewport.width, bottom: viewport.height }
      if (viewport.width < 700) {
        assert.equal(geometry.rotated, String(viewport.height > viewport.width))
        assert(geometry.hostTransform !== 'none', `${viewport.width}: mobile canvas CSS missing`)
        assert(inside(screen, geometry.host), `${viewport.width}: Teleport host outside physical screen`)
        assert(inside(screen, geometry.overlay), `${viewport.width}: record overlay outside physical screen`)
        assert(inside(geometry.overlay, geometry.current), `${viewport.width}: current sentence clipped`)
        assert(geometry.list?.scrollHeight > geometry.list?.clientHeight,
          `${viewport.width}: fixture did not exercise record scrolling`)
        assert(geometry.list.scrollTop > 0,
          `${viewport.width}: event list did not scroll to the settled value`)
        assert(geometry.routeButtons.every(item => inside(screen, item.box) && item.hit),
          `${viewport.width}: return/surrender button outside screen or not hit-testable`)
        assert(geometry.closeButtons.every(item => inside(geometry.overlay, item.box) && item.hit),
          `${viewport.width}: record action outside dialog or not hit-testable`)
        assert(geometry.actionButtons.every(item => inside(screen, item.box) && item.hit),
          `${viewport.width}: primary action outside screen or not hit-testable`)
        for (const [name, box] of [
          ['route', geometry.route], ['dock', geometry.dock],
          ['utility', geometry.utility], ['clock', geometry.clock],
          ['action', geometry.action],
          ...geometry.routeButtons.map((item, index) => [`route button ${index}`, item.box]),
          ...geometry.actionButtons.map((item, index) => [`action button ${index}`, item.box]),
        ]) assert.equal(overlap(geometry.overlay, box), 0,
          `${viewport.width}: record frame overlaps ${name}`)
        const availableAxis = geometry.rotated === 'true'
          ? geometry.dock.top - geometry.utility.bottom
          : geometry.dock.left - geometry.utility.right
        assert(geometry.logicalRecordWidth >= Math.min(geometry.dialogWidth, availableAxis) * .95,
          `${viewport.width}: record frame does not use the available reading width`)
        const glyphs = await textRects(current.locator('.event-message'))
        assert(glyphs.length && glyphs.every(box => inside(geometry.listBox, box)
          && inside(geometry.overlay, box)),
        `${viewport.width}: current sentence glyphs are physically clipped`)
        if (mode === 'player') {
          assert.equal(geometry.routeButtons.length, 2, 'player must have return and surrender')
          assert(inside(screen, geometry.clock), `${viewport.width}: timed clock outside screen`)
          assert(geometry.clockParts.length === 2
            && geometry.clockParts.every(item => inside(geometry.clock, item)),
          `${viewport.width}: timed clock parts clipped`)
        }
      } else if (mode === 'ordinary') {
        assert(geometry.playback?.hit && inside(screen, geometry.playback.box),
          'desktop replay playback is clipped or not hit-testable')
        assert(geometry.replayReturn?.hit && inside(screen, geometry.replayReturn.box),
          'desktop replay return is clipped or not hit-testable')
      }
      await legacy.scrollIntoViewIfNeeded()
      const legacyBox = await page.locator('.mobile-record-overlay [data-event-sequence="22"] .event-message')
        .evaluate(node => {
          const { left, top, right, bottom } = node.getBoundingClientRect()
          return { left, top, right, bottom }
        }).catch(() => null)
      if (viewport.width < 700)
        assert(inside(geometry.overlay, legacyBox), `${viewport.width}: legacy sentence clipped after scroll`)
      if (viewport.width < 700) {
        const glyphs = await textRects(legacy.locator('.event-message'))
        assert(glyphs.length && glyphs.every(box => inside(geometry.listBox, box)
          && inside(geometry.overlay, box)),
        `${viewport.width}: legacy sentence glyphs are physically clipped`)
      }
      await summary.scrollIntoViewIfNeeded()
      const summaryBox = await page.locator('.mobile-record-overlay [data-event-sequence="24"] .event-message')
        .evaluate(node => {
          const { left, top, right, bottom } = node.getBoundingClientRect()
          return { left, top, right, bottom }
        }).catch(() => null)
      if (viewport.width < 700)
        assert(inside(geometry.overlay, summaryBox), `${viewport.width}: complete turn sentence clipped after scroll`)
      if (viewport.width < 700) {
        const glyphs = await textRects(summary.locator('.event-message'))
        assert(glyphs.length && glyphs.every(box => inside(geometry.listBox, box)
          && inside(geometry.overlay, box)),
        `${viewport.width}: complete turn sentence glyphs are physically clipped`)
      }
      const screenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`)
      await page.screenshot({ path: screenshot })
      results.push({ mode, viewport, overflow, geometry, legacyBox, summaryBox, screenshot })
    } finally { await page.close() }
  }
  const evidence = path.join(output, 'physical-geometry.json')
  fs.writeFileSync(evidence, JSON.stringify(results, null, 2))
  console.log(JSON.stringify({ cases: results.length, screenshots: results.map(result => result.screenshot),
    geometry: evidence }, null, 2))
} finally { await browser.close(); await server.close() }
