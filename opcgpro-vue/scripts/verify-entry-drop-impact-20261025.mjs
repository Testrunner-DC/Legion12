import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const front = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repo = path.dirname(front)
const output = process.env.L12_ENTRY_DROP_OUT
assert.ok(output && path.isAbsolute(output), 'explicit L12_ENTRY_DROP_OUT is required')
assert.ok(!fs.existsSync(output), 'never overwrite a previous run')
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 'temp/entry-drop-impact-20261025')
execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
  '& $env:L12_INIT -CacheRoot $env:L12_CACHE | Out-Null; . $env:L12_GUARD; '
  + '@($env:L12_OUT,$env:L12_CACHE,$env:L12_TEMP) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose "entry drop QA" }',
], { env: { ...process.env, L12_INIT:path.join(repo, 'ops/windows/Initialize-L12BuildEnvironment.ps1'),
  L12_GUARD:path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'), L12_OUT:output, L12_CACHE:cache, L12_TEMP:temp } })
fs.mkdirSync(output, { recursive:true })
fs.mkdirSync(temp, { recursive:true })
Object.assign(process.env, { TEMP:temp, TMP:temp, TMPDIR:temp })
const files = ['src/l12/game/ZoneMovementPresentationLayer.vue', 'src/l12/game/PromptOverlay.vue',
  'src/l12/game/GameBoard.vue', 'scripts/fixtures/BattleVisualTransitionHarness.vue',
  'scripts/verify-entry-drop-impact-20261025.mjs']
const pins = () => Object.fromEntries(files.map(file => [file, createHash('sha256')
  .update(fs.readFileSync(path.join(front, file))).digest('hex')]))
const report = { schema:1, before:pins(), after:null, profiles:[], pageErrors:[], resourceErrors:[],
  model:'Real GameBoard/Zone layer and natural rAF; synthetic public troops/art/authority; no server ACK or physical device claim',
  cleanup:{ browserClosed:false, serverClosed:false } }
const html = '<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";createApp(Harness).mount("#app")</script></body></html>'
const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" rx="12" fill="#142e35"/><rect x="10" y="10" width="230" height="330" rx="9" fill="none" stroke="#dfbc60" stroke-width="6"/><path d="M40 220L125 70L210 220Z" fill="#529b9e"/><text x="125" y="285" fill="white" text-anchor="middle" font-size="24">ENTRY QA</text></svg>'
const plugin = { name:'entry-drop-fixture', configureServer(server) {
  server.middlewares.use((request, response, next) => {
    const url = request.url?.split('?')[0]
    if (url?.startsWith('/api/site/media/visual-transition/')) {
      response.setHeader('Content-Type', 'image/svg+xml'); response.end(svg); return
    }
    if (url === '/api/alternate-arts') {
      response.setHeader('Content-Type', 'application/json'); response.end('[]'); return
    }
    if (url === '/__entry-drop') {
      void server.transformIndexHtml(url, html).then(value => {
        response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end(value)
      }); return
    }
    next()
  })
} }
const server = await createServer({ root:front, cacheDir:path.join(output, 'vite-cache'),
  configLoader:'runner', plugins:[plugin], server:{ host:'127.0.0.1', port:0 }, logLevel:'error' })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless:true, channel:'msedge' })
  const address = server.httpServer.address()
  for (const profile of [
    { name:'desktop-heavy', width:1366, height:768, tier:'heavy', motion:'standard' },
    { name:'landscape-heavy', width:844, height:390, tier:'heavy', motion:'standard', mobile:true },
    { name:'rotated-heavy', width:390, height:844, tier:'heavy', motion:'standard', mobile:true },
    { name:'fast-heavy', width:1366, height:768, tier:'heavy', motion:'fast' },
    { name:'off-heavy', width:1366, height:768, tier:'heavy', motion:'off' },
    { name:'desktop-light', width:1366, height:768, tier:'light', motion:'standard' },
  ].filter(profile => !process.env.L12_ENTRY_DROP_CASES || process.env.L12_ENTRY_DROP_CASES.split(',').includes(profile.name))) {
    const context = await browser.newContext({ viewport:{ width:profile.width, height:profile.height },
      isMobile:Boolean(profile.mobile), hasTouch:Boolean(profile.mobile), reducedMotion:'reduce' })
    try {
      const page = await context.newPage()
      page.on('pageerror', error => report.pageErrors.push({ profile:profile.name, message:error.message }))
      page.on('console', message => { if (message.type() === 'error') report.resourceErrors.push({ profile:profile.name, message:message.text() }) })
      await page.route('http://127.0.0.1:8080/api/alternate-arts**', route => route.fulfill({ status:200, body:'[]', contentType:'application/json' }))
      await page.addInitScript(motion => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ animation:motion, mobileLayout:'auto' })), profile.motion)
      await page.addInitScript(() => {
        const original = Element.prototype.animate
        window.__entryNativeAnimationErrors = []
        window.__entryNativeAnimationCalls = []
        Element.prototype.animate = function (...args) {
          window.__entryNativeAnimationCalls.push({ target:this.className, frames:args[0], timing:args[1] })
          try { return original.apply(this, args) }
          catch (error) {
            window.__entryNativeAnimationErrors.push({ message:error.message, stack:error.stack,
              target:this.outerHTML.slice(0, 300), frames:args[0], timing:args[1] })
            throw error
          }
        }
      })
      await page.goto(`http://127.0.0.1:${address.port}/__entry-drop?viewer=1&interactive=1${profile.mobile ? '&landscape=1' : ''}`, { waitUntil:'networkidle' })
      await page.waitForSelector('[data-l12-game-board]')
      // Isolate choreography from cold-source image preparation. The fixture
      // inserts its new hand card only one rAF before play; warm the exact same
      // synthetic face, without modifying authority or animation timing.
      await page.evaluate(async tier => {
        const image = new Image()
        image.src = `/api/site/media/visual-transition/QA-${tier.toUpperCase()}.png`
        await image.decode()
        if (!image.naturalWidth) throw new Error('fixture face did not decode')
      }, profile.tier)
      await page.evaluate(async tier => {
        const { l12State } = await import('/src/l12/net.ts')
        const baseline = JSON.parse(JSON.stringify(l12State.game))
        const entrant = { instanceId:`${tier}-impact-entrant`, name:'物理落位合成军团',
          cardId:`QA-${tier.toUpperCase()}`, cardType:'legion', faction:'otherworld', cost:1,
          baseTroops:tier === 'heavy' ? 8000 : 1000, troops:tier === 'heavy' ? 8000 : 1000,
          tapped:false, summonRound:0, disasterLevel:0,
          imageUrl:`/api/site/media/visual-transition/QA-${tier.toUpperCase()}.png` }
        baseline.players[1].hand = [entrant]
        baseline.players[1].handCount = 1
        baseline.revision++
        window.__entryDropBaseline = baseline
        window.__visualTransitionHarness.applyAuthoritySnapshot(baseline)
      }, profile.tier)
      await page.waitForFunction(tier => {
        const image = document.querySelector(`[data-card-instance-id="${tier}-impact-entrant"] img`)
        return image?.complete && image.naturalWidth > 0 && image.getBoundingClientRect().width > 0
      }, profile.tier, { timeout:5000 })
      const result = await page.evaluate(async ({ tier, name }) => {
        const board = document.querySelector('[data-l12-game-board]')
        const stage = document.querySelector('.board-stage')
        const sentinel = document.createElement('div')
        sentinel.dataset.l12GameBoard = ''
        Object.assign(sentinel.style, { position:'fixed', width:'10px', height:'10px', left:'0', top:'0', transform:'rotate(8deg)' })
        document.body.appendChild(sentinel)
        const sentinelBefore = sentinel.getBoundingClientRect().toJSON()
        const stageTransform = getComputedStyle(stage).transform
        const frames = []
        let running = true
        let firstDecoded = null
        let notifyDecoded
        const decoded = new Promise(resolve => { notifyDecoded = resolve })
        const instanceId = `${tier}-impact-entrant`
        const sample = at => {
          const ghost = document.querySelector(`[data-movement-instance-id="${instanceId}"]`)
          const animated = ghost?.classList.contains('zone-card-movement') ? ghost.querySelector('.moving-card') : ghost
          const image = ghost?.querySelector('canvas[data-l12-pixel-snapshot],img')
          const sourceDecoded = image instanceof HTMLCanvasElement ? image.width > 0 && image.height > 0
            : Boolean(image?.complete && image.naturalWidth > 0)
          if (sourceDecoded && firstDecoded === null) { firstDecoded = at; notifyDecoded() }
          const translate = getComputedStyle(board).translate
          const halves = [...board.querySelectorAll('.battlefield-half')].map(node => {
            const rect = node.getBoundingClientRect(); return { x:rect.x, y:rect.y }
          })
          frames.push({ at, translate, halves, decoded:sourceDecoded,
            transform:animated ? getComputedStyle(animated).transform : null,
            boardAnimations:board.getAnimations().length,
            stageTransform:getComputedStyle(stage).transform })
          if (running) requestAnimationFrame(sample)
        }
        requestAnimationFrame(sample)
        const played = structuredClone(window.__entryDropBaseline)
        const entrant = played.players[1].hand[0]
        played.players[1].hand = []
        played.players[1].handCount = 0
        played.players[1].field[1][2] = entrant
        played.revision++
        played.recentEvents = [{ sequence:1, type:'play', playerIndex:1, text:'玩家B打出合成军团',
          originZone:'hand', playerLogGroupId:`play:${instanceId}`, playerLogTiming:'enter', cards:[entrant] }]
        window.__visualTransitionHarness.applyAuthoritySnapshot(played)
        await Promise.race([decoded, new Promise(resolve => setTimeout(resolve, 5000))])
        await new Promise(resolve => setTimeout(resolve, 1500))
        running = false
        const after = { translate:getComputedStyle(board).translate, animations:board.getAnimations().length,
          stageTransform:getComputedStyle(stage).transform, sentinel:sentinel.getBoundingClientRect().toJSON(),
          remainingGhosts:document.querySelectorAll(`[data-movement-instance-id="${instanceId}"]`).length }
        after.authorityImages = [...document.querySelectorAll(`[data-card-instance-id="${instanceId}"] img`)]
          .map(image => ({ src:image.currentSrc || image.src, complete:image.complete, naturalWidth:image.naturalWidth }))
        after.ledger = document.querySelector('[data-ui-contract="zone-movement-reservation-ledger"]')?.outerHTML
        after.presentation = window.__visualTransitionHarness.presentationSnapshot()
        after.nativeAnimationErrors = window.__entryNativeAnimationErrors
        after.nativeAnimationCalls = window.__entryNativeAnimationCalls
        window.__visualTransitionHarness.rematerializeCurrentSnapshot()
        await new Promise(resolve => setTimeout(resolve, 100))
        after.retransmitAnimations = board.getAnimations().length
        sentinel.remove()
        return { name, warmedSyntheticFace:true, frames, firstDecoded, stageTransform, sentinelBefore, after }
      }, profile)
      report.profiles.push(result)
      await page.screenshot({ path:path.join(output, `${profile.name}-settled.png`) })
      assert.ok(result.frames.some(frame => frame.decoded), `${profile.name}: no decoded moving card; invalid measurement`)
      assert.ok(result.frames.every(frame => frame.stageTransform === result.stageTransform), `${profile.name}: stage scale/rotation changed`)
      assert.deepEqual(result.after.sentinel, result.sentinelBefore, `${profile.name}: unrelated board moved`)
      assert.equal(result.after.animations, 0, `${profile.name}: board animation leaked`)
      assert.equal(result.after.retransmitAnimations, 0, `${profile.name}: retransmission shook the field`)
      assert.equal(result.after.remainingGhosts, 0, `${profile.name}: ghost remained`)
      if (profile.motion !== 'off') assert.ok(result.frames.some(frame => frame.transform?.startsWith('matrix3d(')), `${profile.name}: no depth/drop transform`)
      const displaced = result.frames.filter(frame => frame.translate !== 'none' && frame.translate !== '0px' && frame.translate !== '0px 0px')
      if (profile.tier === 'heavy' && profile.motion !== 'off') {
        assert.ok(displaced.length >= 2, `${profile.name}: whole field never visibly shook`)
        const first = result.frames.find(frame => frame.halves.length === 2)
        assert.ok(displaced.some(frame => frame.halves.length === 2
          && Math.hypot(frame.halves[0].x - first.halves[0].x, frame.halves[0].y - first.halves[0].y) > 2
          && Math.abs((frame.halves[0].x - first.halves[0].x) - (frame.halves[1].x - first.halves[1].x)) < .5
          && Math.abs((frame.halves[0].y - first.halves[0].y) - (frame.halves[1].y - first.halves[1].y)) < .5), `${profile.name}: both battlefield halves did not move together`)
      } else assert.equal(displaced.length, 0, `${profile.name}: off/light unexpectedly shook the field`)
      console.log(`${profile.name}: natural drop/whole-field/lifecycle passed (${result.frames.length} frames)`)
    } finally { await context.close() }
  }
  assert.deepEqual(report.pageErrors, [])
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'; report.error = error.stack || String(error); process.exitCode = 1
} finally {
  if (browser) { await browser.close(); report.cleanup.browserClosed = true }
  await server.close(); report.cleanup.serverClosed = true
  report.after = pins()
  report.sourceStable = JSON.stringify(report.before) === JSON.stringify(report.after)
  if (!report.sourceStable) { report.status = 'failed-source-changed'; process.exitCode = 1 }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(`${report.status}: ${path.join(output, 'report.json')}`)
}
