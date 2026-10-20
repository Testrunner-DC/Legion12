import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'
import { createServer } from 'vite'
import { buildBattlePreviewDocument } from './lib/battlePreviewDocument.mjs'

// This is a local SFC consumer probe, not a server, WebSocket or artwork test.
const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_HAND_SIZE_OUTPUT || 'D:/GPT/Legion12/artifacts/hand-card-size-current-20261020/consumer-r1'
const cacheRoot = process.env.L12_HAND_SIZE_CACHE || 'D:/GPT/Legion12/cache/primary/hand-card-size-current-20261020'
const profileRoot = path.join(cacheRoot, path.basename(output))
const storageScript = path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1')
const initializer = path.resolve(root, '../ops/windows/Initialize-L12BuildEnvironment.ps1')
const bootstrap = execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_HAND_STORAGE; '
  + '$physical=@{}; foreach($name in @("L12_HAND_SIZE_OUTPUT","L12_HAND_SIZE_CACHE","L12_HAND_PROFILE"))'
  + '{$physical[$name]=Assert-L12GeneratedPath -Path ([Environment]::GetEnvironmentVariable($name)) -Purpose "hand size consumer"}; '
  + '. $env:L12_HAND_INITIALIZER -CacheRoot $env:L12_HAND_SIZE_CACHE | Out-Null; '
  + '$null=Assert-L12GeneratedPath -Path $env:TEMP -Purpose "hand size temporary data"; '
  + '$selected=@{}; foreach($name in @("TEMP","TMP","TMPDIR","L12_PWSH")){$selected[$name]=[Environment]::GetEnvironmentVariable($name)}; '
  + '[Console]::WriteLine("L12_ENV="+(@{environment=$selected;physical=$physical}|ConvertTo-Json -Compress))'], {
  encoding: 'utf8', windowsHide: true,
  env: { ...process.env, L12_HAND_STORAGE: storageScript, L12_HAND_INITIALIZER: initializer,
    L12_HAND_SIZE_OUTPUT: output, L12_HAND_SIZE_CACHE: cacheRoot, L12_HAND_PROFILE: profileRoot },
})
const placement = JSON.parse(bootstrap.split(/\r?\n/).find(line => line.startsWith('L12_ENV=')).slice(8))
Object.assign(process.env, placement.environment)
assert(!fs.existsSync(output), 'Never overwrite any previous acceptance directory')
assert(!fs.existsSync(profileRoot), 'Use a fresh explicit governed browser profile')
fs.mkdirSync(output, { recursive: true })
const sha = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase()
const files = ['src/App.vue', 'src/l12/GamePage.vue', 'src/l12/game/GameBoard.vue', 'src/l12/game/HandArea.vue',
  'src/l12/site/L12SettingsModal.vue', 'src/l12/audioPreferences.ts', 'src/style.css', 'src/l12/mobileViewport.css',
  'scripts/verify-batch253-visual.mjs', 'scripts/verify-current-hand-card-size.mjs']
const hashes = () => Object.fromEntries(files.map(file => [file, sha(fs.readFileSync(path.join(root, file)))]))
const report = { schema: 1, status: 'running', startedUtc: new Date().toISOString(),
  scope: 'Actual App + ordinary Settings + GamePage SFCs, public-preset known own hand, local synthetic projection. No live WebSocket/ACK/server match, physical phone, CDN artwork, account synchronization or original private-game claim.',
  output, cacheRoot, profileRoot, placement, sourceHashes: hashes(), cases: [], violations: [], pageErrors: [],
  externalRequestsBlocked: 0, localApiRequestsStubbed: 0, browserClosed: true, serverClosed: false,
  historicDesign: 'BATCH299 later accepted field-sized hand and safe containment; old 64/74/88 numbers are observations only, never hard expectations.',
}
function check(condition, message) { if (!condition) report.violations.push(message) }
const previous = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const fixtureStart = previous.indexOf('const entry = `') + 'const entry = `'.length
const fixtureEnd = previous.indexOf('const isPicker=', fixtureStart)
assert(fixtureStart >= 'const entry = `'.length && fixtureEnd > fixtureStart, 'Existing sanitized fixture boundary must remain exact')
const setup = previous.slice(fixtureStart, fixtureEnd).replace('createRouter,createMemoryHistory', 'createRouter,createWebHistory')
const entry = setup + `
import App from '/src/App.vue'
import {nextTick} from 'vue'
import {authState} from '/src/l12/platform.ts'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
const preset=(await loadOfficialPresetDecks())[0]
const handDefs=preset.cardIds.map(id=>catalog.find(def=>def.id===id)).filter(def=>def?.cardType==='legion').slice(0,6)
if(handDefs.length!==6)throw new Error('Public preset must provide six actual known hand cards')
players[0].hand=handDefs.map((def,index)=>card(def,'hand-size-own-'+index));players[0].handCount=6
players[0].faction=catalog.find(def=>def.id===preset.masterId)?.faction||players[0].faction
players[1].hand=[];players[1].handCount=6
l12State.game.recentEvents=[]
const initialGame=JSON.parse(JSON.stringify(l12State.game)),initialRoom=JSON.parse(JSON.stringify(l12State.room))
// App's real guest lifecycle disconnects old sessions. Publish the synthetic
// initial read model only after mounting; no preferences are changed here.
l12State.socket=null;platformState.account=null;platformState.token='';authState.initialized=true;authState.verified=false
const router=createRouter({history:createWebHistory(),routes:[{path:'/game',component:GamePage,meta:{immersive:true,landscapeCanvas:true}}]})
createApp(App).use(router).mount('#app')
await router.isReady();await nextTick()
l12State.status='online';l12State.room=initialRoom;l12State.game=initialGame
l12State.socket={readyState:WebSocket.OPEN,send:payload=>window.__sentCommands.push(JSON.parse(payload)),close(){}}
window.__handFixture={presetName:preset.name,knownHandIds:handDefs.map(def=>def.id),matchId:initialGame.matchId}
`
const document = buildBattlePreviewDocument(fs.readFileSync(path.join(root, 'index.html'), 'utf8'))
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(cacheRoot, 'vite'),
  server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'current-hand-size-consumer',
    resolveId(id) { if (id === '/__l12_battle_preview__.js') return id },
    load(id) { if (id === '/__l12_battle_preview__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (request.url?.split('?')[0] === '/game') { response.setHeader('Content-Type', 'text/html'); response.end(document); return }
      next()
    }) },
  }] })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let context
let browser
let deadline
const save = () => fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))

async function settings(page, size, restored = false) {
  await page.getByRole('button', { name: '打开对局设置', exact: true }).click()
  const modal = page.locator('.l12-settings-modal')
  const select = modal.locator('label').filter({ hasText: '卡牌显示' }).locator('select')
  await select.waitFor({ state: 'visible' })
  if (restored) assert.equal(await select.inputValue(), size, 'Refreshed native setting must retain its value')
  else await select.selectOption(size)
  assert.equal(await select.inputValue(), size)
  await page.waitForFunction(value => document.documentElement.dataset.l12CardSize === value, size)
  await modal.getByRole('button', { name: '关闭设置', exact: true }).click()
  await modal.waitFor({ state: 'detached' })
  await page.waitForTimeout(450)
}

async function geometry(page) {
  return page.evaluate(() => {
    const serialize = r => ({ x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom })
    function visibleRect(element) {
      let r = element.getBoundingClientRect(), left = Math.max(0, r.left), top = Math.max(0, r.top), right = Math.min(innerWidth, r.right), bottom = Math.min(innerHeight, r.bottom)
      for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent), p = parent.getBoundingClientRect()
        if (/(hidden|auto|scroll|clip)/.test(style.overflowX)) { left = Math.max(left, p.left); right = Math.min(right, p.right) }
        if (/(hidden|auto|scroll|clip)/.test(style.overflowY)) { top = Math.max(top, p.top); bottom = Math.min(bottom, p.bottom) }
      }
      return { x: left, y: top, width: Math.max(0, right-left), height: Math.max(0, bottom-top) }
    }
    const hand = document.querySelector('.l12-hand[data-player-index="0"]'), wraps = [...hand.querySelectorAll('.hand-card-wrap')]
    return { selectedSize: document.documentElement.dataset.l12CardSize,
      persistedSize: JSON.parse(localStorage.getItem('l12-audio-preferences-v1') || '{}').cardSize,
      mobile: document.documentElement.dataset.l12Mobile, viewport: { width: innerWidth, height: innerHeight },
      fixture: window.__handFixture, hand: serialize(hand.getBoundingClientRect()), cards: wraps.map(wrap => {
        const tile = wrap.querySelector('.card-tile'), css = getComputedStyle(wrap), tileCss = getComputedStyle(tile), r = tile.getBoundingClientRect(), hit = document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)
        return { computed: { width: css.width, height: css.height, flexBasis: css.flexBasis, tileWidth: tileCss.width, tileHeight: tileCss.height },
          rect: serialize(r), visibleRect: visibleRect(tile), centerHit: Boolean(hit && tile.contains(hit)) }
      }) }
  })
}

async function clickHand(page, touch) {
  const wrap = page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').last(), tile = wrap.locator('.card-tile')
  await tile.scrollIntoViewIfNeeded()
  const before = await wrap.evaluate(element => element.classList.contains('selected'))
  const hit = await tile.evaluate(element => { const r=element.getBoundingClientRect(), p=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2); return Boolean(p&&element.contains(p)) })
  assert(hit, 'Actual hand card center must be unobstructed, not a forced click')
  if (touch) await tile.tap(); else await tile.click()
  await page.waitForFunction(({ before }) => document.querySelector('.l12-hand[data-player-index="0"] .hand-card-wrap:last-child').classList.contains('selected') !== before, { before })
  assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'Inspecting/selecting a hand card must not send a command')
  return { method: touch ? 'native-tap' : 'native-click', centerHit: hit, selectionToggled: true, commandsSent: 0 }
}

try {
  await server.listen()
  report.origin = `http://127.0.0.1:${server.httpServer.address().port}`
  // Bound the entire browser task. Cleanup is in finally, including real RED.
  deadline = setTimeout(() => { report.violations.push('240 second task deadline'); void context?.close() }, 240_000)
  for (const [width, height, sizes, touch] of [[1366,768,['small','medium','large'],false],[844,390,['large'],true]]) {
    context = await chromium.launchPersistentContext(path.join(profileRoot, String(width)), { headless: true, channel: 'msedge', viewport: { width, height }, hasTouch: touch, serviceWorkers: 'block', env: process.env })
    browser = context.browser(); report.browserClosed = false
    await context.addInitScript(() => {
      // Disable sound only on a fresh synthetic profile, never set cardSize.
      if (!localStorage.getItem('l12-audio-preferences-v1')) localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ musicEnabled: false, sfxEnabled: false }))
      localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true')
    })
    await context.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.origin !== report.origin) { report.externalRequestsBlocked++; return route.abort() }
      if (url.pathname.startsWith('/api/')) { report.localApiRequestsStubbed++; return route.fulfill({ json: { cardRestrictions: {} } }) }
      if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'synthetic-layout', assetVersion: 'synthetic-layout', basePath: '/', cards: {} } })
      return route.continue()
    })
    const page = context.pages()[0] || await context.newPage()
    page.setDefaultTimeout(10_000)
    page.on('pageerror', error => report.pageErrors.push(error.message))
    await page.goto(`${report.origin}/game`, { waitUntil: 'domcontentloaded' })
    await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').last().waitFor()
    await page.waitForTimeout(900)
    const baseline = await geometry(page)
    report.cases.push({ width, height, size: 'auto', baseline: true, measured: baseline })
    for (const size of sizes) {
      const item = { width, height, size, status: 'measuring' }; report.cases.push(item)
      await settings(page, size)
      item.beforeRefresh = await geometry(page)
      check(item.beforeRefresh.cards.length === 6, `${width}/${size}: six known own cards`)
      check(item.beforeRefresh.cards.every(card => card.visibleRect.width > 1 && card.visibleRect.height > 1), `${width}/${size}: own cards visible in clipped viewport`)
      item.click = await clickHand(page, touch)
      item.screenshot = path.join(output, `hand-${width}-${size}.png`)
      await page.screenshot({ path: item.screenshot, fullPage: true })
      await page.reload({ waitUntil: 'domcontentloaded' })
      await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').last().waitFor()
      await settings(page, size, true)
      item.afterRefresh = await geometry(page)
      assert.equal(item.afterRefresh.persistedSize, size)
      assert.deepEqual(item.beforeRefresh.cards.map(card => card.computed), item.afterRefresh.cards.map(card => card.computed), 'Computed hand boxes must persist after refresh')
      item.refreshClick = await clickHand(page, touch)
      item.status = 'measured'; save()
    }
    if (width === 1366) {
      const measured = report.cases.filter(item => item.width === width && !item.baseline)
      const widths = measured.map(item => Number.parseFloat(item.beforeRefresh.cards[0].computed.tileWidth))
      report.desktopSizeComparison = { order: sizes, computedTileWidths: widths,
        rawRectWidths: measured.map(item => item.beforeRefresh.cards[0].rect.width), baselineAutoWidth: baseline.cards[0].computed.tileWidth }
      check(widths[0] < widths[1] && widths[1] < widths[2], 'Native small/medium/large setting must actually change GamePage hand card width monotonically; no old pixel constants asserted')
    }
    await context.close(); report.browserClosed = !browser.isConnected(); assert(report.browserClosed, 'Persistent browser actually disconnected'); context = null
  }
  check(report.pageErrors.length === 0, 'No page exception allowed')
  assert.deepEqual(hashes(), report.sourceHashes, 'Read-only product and tool inputs must remain unchanged')
  report.status = report.violations.length ? 'PRODUCT_RED' : 'passed'
} catch (error) {
  report.status = 'failed'; report.error = String(error?.stack || error)
  await context?.pages()[0]?.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {})
} finally {
  clearTimeout(deadline)
  if (context) { await context.close().catch(error => report.violations.push(`Browser cleanup: ${error.message}`)); report.browserClosed = !browser?.isConnected() }
  await server.close(); report.serverClosed = !server.httpServer?.listening
  report.endedUtc = new Date().toISOString(); save()
}
console.log(JSON.stringify({ status: report.status, cases: report.cases.length, violations: report.violations, output, browserClosed: report.browserClosed, serverClosed: report.serverClosed }, null, 2))
if (report.status !== 'passed' || !report.browserClosed || !report.serverClosed) process.exitCode = 1
