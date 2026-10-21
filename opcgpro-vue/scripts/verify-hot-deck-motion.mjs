import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import { parse } from '@vue/compiler-sfc'

// Reuses the summary fixture and real component from verify-deck-library-hot-layout.
const front = path.resolve(import.meta.dirname, '..')
const baseline = process.argv.includes('--baseline')
const source = baseline ? execFileSync('git', ['show', 'HEAD:opcgpro-vue/src/l12/site/DeckLibraryPage.vue'], { cwd: front, encoding: 'utf8' }) : fs.readFileSync(path.join(front, 'src/l12/site/DeckLibraryPage.vue'), 'utf8')
const appSource = fs.readFileSync(path.join(front, 'src/App.vue'), 'utf8')
const appStyles = parse(appSource).descriptor.styles.map(style => style.content).join('\n')
const output = process.env.L12_HOT_DECK_MOTION_OUTPUT || 'D:/GPT/Legion12/artifacts/hot-deck-motion-20261021'
const cache = process.env.L12_HOT_DECK_MOTION_CACHE || 'D:/GPT/Legion12/cache/primary/hot-deck-motion-20261021/vite'
const env = { ...process.env, L12_MOTION_OUTPUT: output, L12_MOTION_CACHE: cache,
  L12_MOTION_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') }
execFileSync(env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-Command',
  '. $env:L12_MOTION_GUARD; @($env:L12_MOTION_OUTPUT,$env:L12_MOTION_CACHE) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose "actual hot deck motion UI" }'], { env, stdio: 'pipe' })
fs.mkdirSync(output, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,deckLibraryApi} from '/src/l12/platform.ts'
import {publicSummaryFixture} from '/scripts/deck-summary-browser-fixture.mjs'
import '/src/style.css'
import '/src/l12/motion.css'
import '/src/l12/site/uiSystem.css'
import '/__app_motion_scope__.css'
const presets=await loadOfficialPresetDecks(), base=presets.find(deck=>deck.cardIds.length>=40)||presets[0]
const now=new Date().toISOString()
const rows=Array.from({length:16},(_,index)=>({id:'motion-'+index,publicCode:'MOTION'+String(index+1).padStart(6,'0'),ownerId:'synthetic-'+index,author:'本地合成作者 '+index,
 deck:{...base,name:'热门牌库 '+String(index+1).padStart(2,'0'),specialIds:base.specialIds||[],updatedAt:now,publicationId:'motion-'+index,publicationVersion:1},
 views:5000-index*17,likes:500-index,copies:400-index,liked:false,createdAt:now,updatedAt:now,seasonCompliant:true}))
platformState.account=null
deckLibraryApi.summaries=publicSummaryFixture(rows,await loadDeckCatalog())
const Detail={props:['deckId'],render(){return h('main',{id:'detail-fixture','data-deck-id':this.deckId},'detail:'+this.deckId)}}
const router=createRouter({history:createWebHistory(),routes:[{path:'/decks',component:Library},{path:'/decks/:deckId',name:'public-deck-detail',component:Detail,props:true},{path:'/deck-editor',component:{render:()=>null}}]})
window.__qaRouter=router
createApp({render:()=>h('main',{id:'motion-host',class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto',display:location.search.includes('hiddenLayout')?'none':undefined}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`
const server = await createServer({ root: front, configLoader: 'runner', cacheDir: cache,
  server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'hot-deck-motion-fixture', enforce: 'pre',
    resolveId(id) { if (id === '/__hot_deck_motion__.js' || id === '/__app_motion_scope__.css') return id },
    load(id) {
      if (id === '/__hot_deck_motion__.js') return entry
      if (id === '/__app_motion_scope__.css') return appStyles
      if (baseline && id === path.join(front, 'src/l12/site/DeckLibraryPage.vue').replaceAll('\\', '/')) return source
    },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (request.url?.startsWith('/decks') && !request.url.includes('.js')) {
        response.setHeader('Content-Type', 'text/html')
        response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__hot_deck_motion__.js"></script>')
      } else next()
    }) },
  }] })
const report = { completed: false, baseline, sourceSha256: createHash('sha256').update(source).digest('hex'),
  appSourceSha256: createHash('sha256').update(appSource).digest('hex'), animationPreference: 'off', actualAppGlobalStyleIncluded: true,
  scope: 'local synthetic summary, actual DeckLibraryPage/DeckCover and original detail router; no backend or statistics traffic', scenarios: [], errors: [] }
async function prepare(page, noObserver = false) {
  page.on('pageerror', error => report.errors.push(error.message))
  await page.addInitScript(disable => {
    localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true')
    localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ animation: 'off', musicEnabled: false, sfxEnabled: false }))
    if (disable) window.ResizeObserver = undefined
  }, noObserver)
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.startsWith('/api/')) return route.fulfill({ json: {} })
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })
}
async function ready(page) {
  assert.equal(await page.locator('html').getAttribute('data-l12-animation'), 'off', 'exercise the actual global application animation-off preference')
  await page.locator('.hot-deck-track[data-motion-ready="true"]').nth(1).waitFor()
  assert.equal(await page.locator('.hot-deck-viewport').count(), 2, 'retain exactly two popular rows')
  for (const row of [0, 1]) assert.deepEqual(await page.locator('.hot-deck-loop:not([aria-hidden])').nth(row).locator(':scope > button').evaluateAll(items => items.map(item => item.dataset.publicCode)),
    Array.from({ length: 8 }, (_, index) => 'MOTION' + String(index * 2 + row + 1).padStart(6, '0')), 'retain odd/even rank order')
}
async function geometry(page, row = 0) {
  return page.locator('.hot-deck-viewport').nth(row).evaluate(viewport => {
    const track = viewport.querySelector('.hot-deck-track'), loop = track.firstElementChild
    const style = getComputedStyle(track), box = viewport.getBoundingClientRect()
    return { width: box.width, height: box.height, loopWidth: loop.getBoundingClientRect().width,
      trackWidth: track.getBoundingClientRect().width, duration: parseFloat(style.animationDuration),
      animation: style.animationName, playState: style.animationPlayState, x: new DOMMatrixReadOnly(style.transform).m41,
      scrollbar: getComputedStyle(viewport).scrollbarWidth, webkitScrollbar: getComputedStyle(viewport, '::-webkit-scrollbar').display,
      horizontalBarHeight: viewport.offsetHeight - viewport.clientHeight,
      cardTops: [...loop.children].map(item => item.getBoundingClientRect().top) }
  })
}
async function motion(page, ms = 450, row = 0) {
  return page.locator('.hot-deck-track').nth(row).evaluate(async (track, delay) => {
    const x = () => new DOMMatrixReadOnly(getComputedStyle(track).transform).m41
    const before = x(), start = performance.now(), width = track.firstElementChild.getBoundingClientRect().width
    await new Promise(resolve => setTimeout(resolve, delay))
    const after = x(), elapsed = (performance.now() - start) / 1000
    const distance = ((getComputedStyle(track).animationDirection === 'reverse' ? after - before : before - after) + width) % width
    return { before, after, elapsed, distance, speed: distance / elapsed }
  }, ms)
}
function moving(value, label, row = 0) { const speed = [48, 46][row]; assert.ok(Math.abs(value.speed - speed) < 5, `${label}: expected ${speed}px/s, got ${JSON.stringify(value)}`) }
function stopped(value, label) { assert.ok(Math.abs(value.after - value.before) < .5, `${label}: ${JSON.stringify(value)}`) }
function layout(value) {
  assert.ok(value.cardTops.every(top => Math.abs(top - value.cardTops[0]) < .5), 'all covers share one line')
  assert.equal(value.horizontalBarHeight, 0, 'no visible horizontal drag bar')
  assert.ok(Math.abs(value.trackWidth - 2 * value.loopWidth) < 1, 'two identical loops must meet without a seam gap')
}
async function keyboardAll(page) {
  await page.mouse.move(0, 0)
  for (const row of [0, 1]) {
    await page.locator('.hot-deck-loop:not([aria-hidden])').nth(row).locator(':scope > button').first().focus()
    await page.waitForTimeout(30)
    for (let index = 0; index < 8; index++) {
      const focus = await page.locator('.hot-deck-viewport').nth(row).evaluate(viewport => {
      const target = document.activeElement, box = target.getBoundingClientRect(), clip = viewport.getBoundingClientRect()
      return { code: target.dataset.publicCode, visible: box.left >= clip.left - 1 && box.right <= clip.right + 1 }
    })
      assert.equal(focus.code, 'MOTION' + String(index * 2 + row + 1).padStart(6, '0'))
      assert.ok(focus.visible, 'keyboard must reveal every clipped item: ' + JSON.stringify(focus))
      if (index < 7) await page.keyboard.press('Tab')
      await page.waitForTimeout(30)
    }
    stopped(await motion(page, 200, row), 'keyboard row pause')
    await page.keyboard.press('Tab')
    await page.waitForTimeout(50)
    assert.equal(await page.locator('.hot-deck-viewport').nth(row).evaluate(view => view.scrollLeft), 0, 'leaving keyboard focus resets native scroll before motion resumes')
  }
}
let browser
try {
  await server.listen()
  const url = `http://127.0.0.1:${server.httpServer.address().port}/decks`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    const context = await browser.newContext({ viewport, hasTouch: true, reducedMotion: 'no-preference' })
    const page = await context.newPage(); await prepare(page); await page.goto(url); await ready(page)
    console.log(JSON.stringify({ checkpoint: 'normal-start', viewport }))
    await page.mouse.move(0, 0)
    const rows = []
    for (const row of [0, 1]) {
      const metrics = await geometry(page, row); layout(metrics)
      const hoverDurations = await page.locator('.hot-deck-track').nth(row).evaluate(track => ({
        cover: getComputedStyle(track.querySelector('.deck-cover')).transitionDuration,
        fan: getComputedStyle(track.querySelector('.deck-cover__cards')).transitionDuration,
      }))
      assert.ok(hoverDurations.cover.split(',').every(value => Number.parseFloat(value) >= .29), 'cover hover must not inherit battle animation-off duration')
      assert.ok(hoverDurations.fan.split(',').every(value => Number.parseFloat(value) >= .29), 'fan hover must not inherit battle animation-off duration')
      assert.ok(Math.abs(metrics.loopWidth / metrics.duration - [48, 46][row]) < .05)
      const normal = await motion(page, 450, row); moving(normal, 'normal motion', row)
      await page.locator('.hot-deck-track').nth(row).evaluate(track => { const animation = track.getAnimations()[0]; animation.currentTime = Number.parseFloat(getComputedStyle(track).animationDuration) * 1000 - 180 })
      const seam = await motion(page, 450, row); moving(seam, 'loop boundary', row)
      assert.ok(row === 0 ? seam.after > seam.before : seam.after < seam.before, 'must really cross each loop boundary')
      await page.locator('.hot-deck-viewport').nth(row).hover(); await page.waitForTimeout(50)
      const hover = await motion(page, 200, row); stopped(hover, 'hover pause')
      moving(await motion(page, 300, 1 - row), 'other row must remain independent', 1 - row)
      await page.mouse.move(0, 0); await page.waitForTimeout(50)
      const hoverResume = await motion(page, 450, row); moving(hoverResume, 'hover release', row)
      rows.push({ row: row + 1, metrics, normal, seam, hover, hoverResume })
    }
    await keyboardAll(page)
    for (const row of [0, 1]) { rows[row].focusResume = await motion(page, 450, row); moving(rows[row].focusResume, 'focus release', row) }
    const cdp = await context.newCDPSession(page)
    for (const row of [0, 1]) {
      await page.locator('.hot-deck-viewport').nth(row).scrollIntoViewIfNeeded()
      const clip = await page.locator('.hot-deck-viewport').nth(row).boundingBox()
      const x = Math.round(clip.x + 45), y = Math.round(clip.y + Math.min(clip.height / 2, 40))
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] })
      await page.waitForTimeout(40); const touch = await motion(page, 200, row); stopped(touch, 'touch pause')
      moving(await motion(page, 250, 1 - row), 'other row must move during touch', 1 - row)
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: Math.max(10, Math.round(clip.y - 30)) }] })
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
      await page.mouse.move(0, 0); await page.waitForTimeout(80)
      const touchResume = await motion(page, 450, row); moving(touchResume, 'touch outside release', row)
      rows[row].touch = touch; rows[row].touchResume = touchResume
    }
    if (viewport.width === 1366) {
      await page.locator('.hot-decks').evaluate(section => section.scrollIntoView({ block: 'center' }))
      const clips = await page.locator('.hot-deck-viewport').evaluateAll(items => items.map(item => { const box = item.getBoundingClientRect(); return { x: Math.round(box.x + 45), y: Math.round(box.y + box.height / 2) } }))
      const one = { ...clips[0], id: 1 }, two = { ...clips[1], id: 2 }
      // CDP touchEnd releases the entire touch set. Validate partial release via
      // the real DOM PointerEvent path; native single-finger CDP stays above.
      await page.locator('.hot-deck-viewport').nth(0).dispatchEvent('pointerdown', { pointerType: 'touch', pointerId: 101 })
      await page.locator('.hot-deck-viewport').nth(1).dispatchEvent('pointerdown', { pointerType: 'touch', pointerId: 102 })
      for (const row of [0, 1]) stopped(await motion(page, 180, row), 'both touch rows paused')
      await page.evaluate(() => window.dispatchEvent(new PointerEvent('pointerup', { pointerType: 'touch', pointerId: 101 })))
      moving(await motion(page, 350, 0), 'released first finger row')
      stopped(await motion(page, 200, 1), 'second finger still held')
      await page.evaluate(() => window.dispatchEvent(new PointerEvent('pointerup', { pointerType: 'touch', pointerId: 102 })))
      moving(await motion(page, 350, 1), 'released second finger row', 1)
      rows[0].independentTwoFingerDomPointerRelease = true
      rows[0].nativeTwoFingerSplitReleaseVerified = false
    }
    await page.getByRole('heading', { name: '热门牌库', exact: true }).scrollIntoViewIfNeeded()
    await page.screenshot({ path: path.join(output, `normal-${viewport.width}x${viewport.height}.png`), fullPage: false })
    const scenario = { viewport, preference: 'normal', rows, all16KeyboardReachable: true }
    report.scenarios.push(scenario)
    for (const copy of [1, 2]) {
      await page.mouse.move(0, 0)
      await page.locator('.hot-deck-viewport').first().scrollIntoViewIfNeeded()
      await page.locator('.hot-deck-track').first().evaluate((track, copy) => { track.getAnimations()[0].currentTime = copy === 1 ? 0 : Number.parseFloat(getComputedStyle(track).animationDuration) * 1000 - 30 }, copy)
      const clip = await page.locator('.hot-deck-viewport').first().boundingBox()
      await page.mouse.move(clip.x + 30, clip.y + 30); await page.waitForTimeout(350)
      const first = page.locator(`.hot-deck-loop > button[data-hot-row="1"][data-hot-copy="${copy}"]`).first()
      const box = await first.boundingBox()
      const x = box.x + Math.min(box.width / 2, 100), y = box.y + box.height / 2
      assert.equal(await page.evaluate(({ x, y }) => document.elementFromPoint(x, y)?.closest('button')?.dataset.publicCode, { x, y }), 'MOTION000001')
      await page.mouse.click(x, y); await page.locator('#detail-fixture').waitFor()
      assert.equal(new URL(page.url()).pathname, '/decks/MOTION000001')
      assert.equal(await page.locator('#detail-fixture').getAttribute('data-deck-id'), 'MOTION000001')
      await page.goBack(); await ready(page); await page.mouse.move(0, 0)
    }
    scenario.originalAndCloneDetailRoute = '/decks/MOTION000001'
    moving(await motion(page), 'route return')
    await page.getByRole('button', { name: '我的牌库', exact: true }).click()
    await page.locator('.deck-tabs').getByRole('button', { name: '公开牌库', exact: true }).click()
    await ready(page); await page.mouse.move(0, 0); moving(await motion(page), 'plaza DOM remount')
    scenario.routeReturnAndTabRemount = true
    await context.close()
  }
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 }, reducedMotion: 'no-preference' })
  const page = await context.newPage(); await prepare(page, true); await page.goto(url + '?hiddenLayout=1')
  await page.waitForFunction(() => document.querySelectorAll('.hot-deck-loop:not([aria-hidden]) > button').length === 16)
  assert.equal(await page.locator('.hot-deck-track').first().getAttribute('data-motion-ready'), null, 'hidden zero-width mount must await a valid measurement')
  await page.evaluate(() => { document.querySelector('#motion-host').style.display = ''; document.dispatchEvent(new Event('visibilitychange')) })
  await ready(page); await page.mouse.move(0, 0); moving(await motion(page), 'zero-width foreground recovery without ResizeObserver')
  const resize = []
  for (const viewport of [{ width: 390, height: 844 }, { width: 844, height: 390 }, { width: 1366, height: 768 }]) {
    await page.setViewportSize(viewport); await page.waitForTimeout(80)
    for (const row of [0, 1]) {
      const metrics = await geometry(page, row); layout(metrics); assert.ok(Math.abs(metrics.loopWidth / metrics.duration - [48, 46][row]) < .05)
      const observed = await motion(page, 450, row); moving(observed, 'resize without observer', row); resize.push({ viewport, row: row + 1, metrics, observed })
    }
  }
  report.scenarios.push({ hiddenZeroWidthMount: true, resizeObserverAvailable: false, visibilityRecovery: true, resize })
  await context.close()
  for (const viewport of [{ width: 1366, height: 768 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    const reduced = await browser.newContext({ viewport, reducedMotion: 'reduce' })
    const page = await reduced.newPage(); await prepare(page); await page.goto(url); await ready(page)
    const rows = []
    for (const row of [0, 1]) {
      const metrics = await geometry(page, row)
      assert.equal(metrics.animation, 'none'); assert.equal(metrics.scrollbar, 'none'); assert.equal(metrics.webkitScrollbar, 'none'); assert.equal(metrics.horizontalBarHeight, 0)
      assert.equal(await page.locator('.hot-deck-loop[aria-hidden]').nth(row).evaluate(loop => getComputedStyle(loop).display), 'none')
      stopped(await motion(page, 450, row), 'reduced motion must remain static')
      rows.push({ row: row + 1, metrics })
    }
    await keyboardAll(page)
    for (const row of [0, 1]) stopped(await motion(page, 250, row), 'reduced motion must not restart after keyboard release')
    await page.screenshot({ path: path.join(output, `reduced-${viewport.width}x${viewport.height}.png`), fullPage: false })
    report.scenarios.push({ viewport, preference: 'reduce', rows, all16KeyboardReachable: true, autoPlaybackForced: false })
    await reduced.close()
  }
  assert.deepEqual(report.errors, [])
  report.completed = true
} catch (error) {
  report.failure = error.stack || String(error)
  throw error
} finally {
  await browser?.close(); await server.close()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ completed: report.completed, scenarios: report.scenarios.length, output, errors: report.errors }))
}
