import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(
  process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const fixture = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const marker = 'const entry = ' + String.fromCharCode(96)
let entry = fixture.slice(fixture.indexOf(marker) + marker.length, fixture.indexOf('\n' + String.fromCharCode(96), fixture.indexOf(marker)))
entry = "import {ref} from 'vue';import {useLandscapeViewport,viewportRect} from '/src/l12/mobileViewport.ts';import '/src/l12/mobileViewport.css';window.qaRect=viewportRect;\n"
  + entry.replace('createApp({render:', 'createApp({setup(){useLandscapeViewport(ref(true))},render:')
    .replace("l12State.status='online'", "l12State.gmEnabled=params.has('gm');l12State.rankedClock={serverUtcMs:Date.now(),receivedAtMs:Date.now(),totalLimitMs:1800000,operationLimitMs:60000,reconnectLimitMs:120000,players:[0,1].map(playerIndex=>({playerIndex,totalRemainingMs:1200000,operationRemainingMs:45000,acting:playerIndex===0,connected:true}))};l12State.status='online'")

const server = await createServer({
  root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-a3-layout'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{ name: 'a3-layout-fixture',
    resolveId(id) { if (id === '/__a3__.js') return id },
    load(id) { if (id === '/__a3__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if (request.url?.startsWith('/__a3__') && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__a3__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const overlap = (a, b) => Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left))
  * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top))
let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1'
    ? route.continue() : route.abort())
  const cases = [
    { name: 'wide timed desktop', width: 1920, height: 1080, mobile: false, gm: false },
    { name: 'narrow timed desktop', width: 1280, height: 720, mobile: false, gm: false },
    { name: 'GM right dock', width: 1920, height: 1400, mobile: false, gm: true },
    { name: 'portrait timed mobile', width: 390, height: 844, mobile: true, gm: false },
    { name: 'landscape timed mobile', width: 844, height: 390, mobile: true, gm: false },
  ]
  for (const input of cases) {
    await page.setViewportSize({ width: input.width, height: input.height })
    await page.goto('http://127.0.0.1:' + server.httpServer.address().port + '/__a3__' + (input.gm ? '?gm=1' : ''))
    await page.locator('.board-stage').waitFor()
    if (input.mobile) await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    else await page.locator('.board-center.timed-board').waitFor()
    const result = await page.evaluate(() => {
      const box = selector => {
        const element = document.querySelector(selector)
        if (!element) return null
        const rect = window.qaRect(element)
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
      }
      return {
        mobile: document.documentElement.dataset.l12Mobile,
        board: box('.board-viewport'), center: box('.board-center'),
        rightRail: box('.right-rail'), felt: box('.felt-board'),
        opponentClock: box('.opponent-status-lane .board-player-clock'),
        myClock: box('.my-status-lane .board-player-clock'),
        opponentHand: box('.board-center>.l12-hand:first-child'),
        myHand: box('.board-center>.l12-hand:last-child'),
        mobileClocks: box('.mobile-timed-clocks'),
      }
    })
    assert.equal(result.mobile, String(input.mobile), input.name)
    assert(result.center && result.rightRail && result.felt && result.board, input.name + ' missing board regions')
    if (input.mobile) {
      assert(result.mobileClocks, input.name + ' missing timed mobile controls')
      assert(result.center.right <= result.rightRail.left + 2, input.name + ' center intrudes into right rail')
      assert.equal(overlap(result.mobileClocks, result.felt), 0, input.name + ' clocks cover battlefield')
    } else {
      assert(result.opponentClock && result.myClock && result.opponentHand && result.myHand, input.name + ' missing timed hand/clock regions')
      assert(result.center.right <= result.rightRail.left + 2, input.name + ' center intrudes into right rail')
      assert.equal(overlap(result.opponentClock, result.opponentHand), 0, input.name + ' opponent clock covers hand')
      assert.equal(overlap(result.myClock, result.myHand), 0, input.name + ' player clock covers hand')
      assert.equal(overlap(result.opponentClock, result.felt), 0, input.name + ' opponent clock covers battlefield')
      assert.equal(overlap(result.myClock, result.felt), 0, input.name + ' player clock covers battlefield')
      if (input.gm) assert(result.board.right <= input.width - 343, 'GM dock must reserve the right-side space')
    }
    assert.deepEqual(errors, [], input.name + ' emitted page errors')
  }
  console.log('A3 browser layout: timed narrow/wide desktop, GM dock, portrait/landscape mobile passed.')
} finally {
  await browser?.close()
  await server.close()
}
