import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(root, '../artifacts/deck-library-hot-layout')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const presets=await loadOfficialPresetDecks()
const base=presets.find(deck=>deck.cardIds.length>=40)||presets[0]
const now=new Date().toISOString()
const hotItemsPerRow=Math.max(1,Number(new URLSearchParams(location.search).get('qaHotItems'))||8)
const rows=Array.from({length:hotItemsPerRow*2+2},(_,index)=>({
 id:'hot-'+index,...(index<2?{}:{publicCode:'HOT'+String(index+1).padStart(6,'0')}),ownerId:'owner-'+index,author:'热门作者 '+String(index+1).padStart(2,'0'),
 deck:{...base,name:(index<2?'无短码牌库 ':'热门牌库 ')+String(index+1).padStart(2,'0'),specialIds:base.specialIds||[],updatedAt:now,publicationId:'hot-'+index,publicationVersion:1},
 views:5000-index*17,likes:500-index,copies:400-index,liked:false,createdAt:now,updatedAt:now,seasonCompliant:true,
}))
platformState.account=null
publicDeckApi.list=async()=>rows
const Detail={props:['deckId'],render(){return h('main',{id:'detail-fixture','data-deck-id':this.deckId},'detail:'+this.deckId)}}
const router=createRouter({history:createWebHistory(),routes:[
 {path:'/decks',name:'decks',component:Library},
 {path:'/decks/:deckId',name:'public-deck-detail',component:Detail,props:true},
 {path:'/:pathMatch(.*)*',redirect:'/decks'},
]})
window.__qaRouter=router
createApp({render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-deck-library-hot-layout'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-library-hot-layout-fixture',
    resolveId(id) { if (id === '/__deck_library_hot_layout__.js') return id },
    load(id) { if (id === '/__deck_library_hot_layout__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__deck_library_hot_layout__')) && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_library_hot_layout__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

async function prepare(page) {
  await page.addInitScript(() => localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true'))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{} } })
    return route.continue()
  })
}

async function waitForLibrary(page) {
  await page.getByRole('heading', { name:'热门牌库', exact:true }).waitFor()
  await page.locator('.hot-deck-track[data-motion-ready="true"]').nth(1).waitFor()
}

async function waitForTargetSpeeds(page) {
  await page.waitForFunction(speeds => [...document.querySelectorAll('.hot-deck-track')].every((track, index) => {
    const duration = Number.parseFloat(getComputedStyle(track).animationDuration)
    const loopWidth = track.querySelector('.hot-deck-loop:not([aria-hidden="true"])')?.getBoundingClientRect().width || 0
    return loopWidth > 0 && Math.abs(loopWidth / duration - speeds[index]) < .05
  }), targetPixelsPerSecond)
}

async function freezeTracks(page, translatedCopy = false) {
  await page.evaluate(copy => {
    for (const track of document.querySelectorAll('.hot-deck-track')) {
      const firstLoop = track.querySelector('.hot-deck-loop')
      track.style.animation = 'none'
      track.style.transform = copy && firstLoop ? `translateX(-${firstLoop.getBoundingClientRect().width}px)` : 'translateX(0)'
    }
  }, translatedCopy)
}

async function assertDetailRoute(page, expectedCode) {
  await page.locator('#detail-fixture').waitFor()
  assert.equal(new URL(page.url()).pathname, `/decks/${expectedCode}`)
  assert.equal(await page.locator('#detail-fixture').getAttribute('data-deck-id'), expectedCode)
}

async function backToLibrary(page) {
  await page.goBack()
  await waitForLibrary(page)
}

const closeTo = (actual, expected, tolerance = 0.02) => Math.abs(actual - expected) <= tolerance
const targetPixelsPerSecond = [48, 46]

async function measureTrackMotion(tracks, sampleMs = 600) {
  return tracks.evaluateAll(async (elements, delay) => {
    const translateX = element => new DOMMatrixReadOnly(getComputedStyle(element).transform).m41
    const before = elements.map(translateX)
    const startedAt = performance.now()
    await new Promise(resolve => setTimeout(resolve, delay))
    const elapsedSeconds = (performance.now() - startedAt) / 1000
    const after = elements.map(translateX)
    return elements.map((element, index) => {
      const durationSeconds = Number.parseFloat(getComputedStyle(element).animationDuration)
      const loopWidth = element.querySelector('.hot-deck-loop')?.getBoundingClientRect().width || 0
      return {
        durationSeconds,
        loopWidth,
        calculatedPixelsPerSecond: loopWidth / durationSeconds,
        observedPixelsPerSecond: Math.abs(after[index] - before[index]) / elapsedSeconds,
      }
    })
  }, sampleMs)
}

function assertTrackMotion(metrics, viewport) {
  assert.equal(metrics.length, 2, `${viewport} 必须测得两条轨道`)
  metrics.forEach((metric, index) => {
    const targetSpeed = targetPixelsPerSecond[index]
    assert.ok(closeTo(metric.durationSeconds, metric.loopWidth / targetSpeed, .03), `${viewport} 第 ${index + 1} 行周期未按 ${metric.loopWidth}px / ${targetSpeed}px/s 动态计算，实际 ${metric.durationSeconds}s`)
    assert.ok(closeTo(metric.calculatedPixelsPerSecond, targetSpeed, .05), `${viewport} 第 ${index + 1} 行计算速度应稳定为 ${targetSpeed}px/s，实际 ${metric.calculatedPixelsPerSecond}px/s`)
    assert.ok(closeTo(metric.observedPixelsPerSecond, targetSpeed, Math.max(2, targetSpeed * .08)), `${viewport} 第 ${index + 1} 行浏览器实测速率应接近 ${targetSpeed}px/s，实际 ${metric.observedPixelsPerSecond}px/s`)
  })
}

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } })
  await prepare(page)
  await page.goto(`http://127.0.0.1:${port}/decks`)
  await waitForLibrary(page)

  const hot = page.locator('.hot-decks')
  const tracks = hot.locator('.hot-deck-track')
  assert.equal(await tracks.count(), 2, '热门卡组必须严格保留两条独立轨道')
  assert.equal(await tracks.nth(0).locator('.hot-deck-loop').count(), 2)
  assert.equal(await tracks.nth(1).locator('.hot-deck-loop').count(), 2)
  const topology = await tracks.evaluateAll(elements => ({
    distinctParents: elements[0]?.parentElement !== elements[1]?.parentElement,
    loopWidths: elements.map(track => [...track.children].map(loop => loop.getBoundingClientRect().width)),
  }))
  assert.equal(topology.distinctParents, true, '两行必须位于各自独立的轮播视口和轨道')
  assert.ok(topology.loopWidths.every(widths => widths.length === 2 && widths[0] > 0 && Math.abs(widths[0] - widths[1]) < 0.5), '每条轨道必须由两个等宽循环独立闭合')
  assert.equal(await tracks.nth(0).evaluate(element => getComputedStyle(element).animationDirection), 'normal')
  assert.equal(await tracks.nth(1).evaluate(element => getComputedStyle(element).animationDirection), 'reverse')
  const wideMotion = await measureTrackMotion(tracks)
  assertTrackMotion(wideMotion, '1366x768')
  console.log(JSON.stringify({ checkpoint:'hot-deck-motion', viewport:'1366x768', tracks:wideMotion }))
  await page.setViewportSize({ width:390, height:844 })
  await waitForTargetSpeeds(page)
  assertTrackMotion(await measureTrackMotion(tracks), '1366→390 ResizeObserver')
  await page.setViewportSize({ width:1366, height:768 })
  await waitForTargetSpeeds(page)
  assertTrackMotion(await measureTrackMotion(tracks), '390→1366 ResizeObserver')
  assert.equal(await hot.getByRole('button', { name:/查看热门卡组/ }).count(), 0, '旧外显名称不得保留')
  const frame = await hot.evaluate(element => {
    const style = getComputedStyle(element)
    return { backgroundColor:style.backgroundColor, backgroundImage:style.backgroundImage, borderTop:style.borderTopWidth, borderBottom:style.borderBottomWidth }
  })
  assert.match(frame.backgroundColor, /rgba\(0, 0, 0, 0\)|transparent/)
  assert.equal(frame.backgroundImage, 'none')
  assert.equal(frame.borderTop, '0px')
  assert.equal(frame.borderBottom, '0px')
  assert.equal(await hot.getByText(/自动播放|已暂停/).count(), 0, '播放状态文字不得外显')
  assert.equal(await hot.locator('button[data-public-code=""]').count(), 0, '缺少 publicCode 的条目不得进入热门列表')
  assert.equal(await hot.getByText(/无短码牌库/).count(), 0, '高热度但不可路由的条目不得显示为坏入口')

  const transformsBefore = await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).transform))
  await page.waitForTimeout(250)
  const transformsAfter = await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).transform))
  assert.notEqual(transformsAfter[0], transformsBefore[0], '首行应自动播放')
  assert.notEqual(transformsAfter[1], transformsBefore[1], '次行应独立自动播放')
  const rowViewports = hot.locator('.hot-deck-viewport')
  await rowViewports.nth(0).hover()
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['paused','running'], '鼠标悬停首行时只能暂停首行')
  await rowViewports.nth(1).hover()
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','paused'], '鼠标移入次行时应恢复首行并只暂停次行')
  await page.mouse.move(1, 1)
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','running'], '鼠标移出应恢复两行')

  const firstHotDeck = hot.locator('button[data-hot-row="1"][data-hot-copy="1"]').first()
  await firstHotDeck.focus()
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['paused','running'], '键盘焦点进入首行时只能暂停首行')
  const secondHotDeck = hot.locator('button[data-hot-row="2"][data-hot-copy="1"]').first()
  await secondHotDeck.focus()
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','paused'], '键盘焦点切换到次行时只能暂停次行')
  await page.getByPlaceholder('搜索牌库名称、作者或主宰').focus()
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','running'], '键盘焦点离开应恢复两行')
  await rowViewports.nth(0).dispatchEvent('pointerdown', { pointerType:'touch', pointerId:1, isPrimary:true })
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['paused','running'], '触控首行时只能暂停首行')
  await rowViewports.nth(0).dispatchEvent('pointerup', { pointerType:'touch', pointerId:1, isPrimary:true })
  assert.deepEqual(await tracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','running'], '触控结束应恢复对应行')

  await freezeTracks(page)
  await page.evaluate(() => {
    window.__qaOriginalRouterPush = window.__qaRouter.push
    window.__qaRouter.push = async () => { throw new Error('qa navigation failure') }
  })
  await firstHotDeck.click()
  await page.getByText('无法打开该牌库详情，请稍后重试', { exact:true }).waitFor()
  await page.evaluate(() => { window.__qaRouter.push = window.__qaOriginalRouterPush })

  await freezeTracks(page)
  const rowOneOriginal = hot.locator('button[data-hot-row="1"][data-hot-copy="1"]').first()
  const rowOneCode = await rowOneOriginal.getAttribute('data-public-code')
  assert.ok(rowOneCode)
  await rowOneOriginal.click()
  await assertDetailRoute(page, rowOneCode)
  await backToLibrary(page)
  await freezeTracks(page)
  const rowTwoOriginal = page.locator('.hot-decks button[data-hot-row="2"][data-hot-copy="1"]').first()
  const rowTwoCode = await rowTwoOriginal.getAttribute('data-public-code')
  assert.ok(rowTwoCode && rowTwoCode !== rowOneCode, '两行原始项应指向不同真实短码')
  await rowTwoOriginal.click()
  await assertDetailRoute(page, rowTwoCode)
  await backToLibrary(page)
  await freezeTracks(page, true)
  const copiedLoopItem = page.locator('.hot-decks button[data-hot-row="1"][data-hot-copy="2"]').first()
  assert.equal(await copiedLoopItem.getAttribute('data-public-code'), rowOneCode, '循环副本必须携带原项同一短码')
  await copiedLoopItem.click()
  await assertDetailRoute(page, rowOneCode)
  await backToLibrary(page)
  await freezeTracks(page)
  const keyboardItem = page.locator('.hot-decks button[data-hot-row="2"][data-hot-copy="1"]').nth(1)
  const keyboardCode = await keyboardItem.getAttribute('data-public-code')
  await keyboardItem.focus()
  await keyboardItem.press('Enter')
  await assertDetailRoute(page, keyboardCode)
  await page.close()

  const touchPage = await browser.newPage({ viewport:{ width:390, height:844 }, hasTouch:true })
  await prepare(touchPage)
  await touchPage.goto(`http://127.0.0.1:${port}/decks`)
  await waitForLibrary(touchPage)
  const touchTracks = touchPage.locator('.hot-deck-track')
  const touchMotion = await measureTrackMotion(touchTracks)
  assertTrackMotion(touchMotion, '390x844')
  console.log(JSON.stringify({ checkpoint:'hot-deck-motion', viewport:'390x844', tracks:touchMotion }))
  const touchViewports = touchPage.locator('.hot-deck-viewport')
  await touchViewports.nth(0).dispatchEvent('pointerdown', { pointerType:'touch', pointerId:11, isPrimary:true })
  assert.deepEqual(await touchTracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['paused','running'], '移动端触控首行时只能暂停首行')
  await touchViewports.nth(0).dispatchEvent('pointerup', { pointerType:'touch', pointerId:11, isPrimary:true })
  assert.deepEqual(await touchTracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','running'], '移动端首行触控结束后应恢复两行')
  await touchViewports.nth(1).dispatchEvent('pointerdown', { pointerType:'touch', pointerId:12, isPrimary:true })
  assert.deepEqual(await touchTracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','paused'], '移动端触控次行时只能暂停次行')
  await touchViewports.nth(1).dispatchEvent('pointerup', { pointerType:'touch', pointerId:12, isPrimary:true })
  assert.deepEqual(await touchTracks.evaluateAll(elements => elements.map(element => getComputedStyle(element).animationPlayState)), ['running','running'], '移动端次行触控结束后应恢复两行')
  await freezeTracks(touchPage)
  const touchItem = touchPage.locator('.hot-decks button[data-hot-row="1"][data-hot-copy="1"]').first()
  const touchCode = await touchItem.getAttribute('data-public-code')
  await touchItem.tap()
  await assertDetailRoute(touchPage, touchCode)
  await touchPage.close()

  const profiles = [
    { width:1920, height:1080, itemsPerRow:8 },
    { width:1366, height:768, itemsPerRow:8 },
    { width:430, height:932, itemsPerRow:8 },
    { width:390, height:844, itemsPerRow:8 },
    { width:1366, height:768, itemsPerRow:2 },
    { width:390, height:844, itemsPerRow:2 },
  ]
  const report = []
  for (const { width, height, itemsPerRow } of profiles) {
    const scenario = `${itemsPerRow}项/行 ${width}x${height}`
    const view = await browser.newPage({ viewport:{ width, height }, hasTouch:width <= 700 })
    await prepare(view)
    await view.goto(`http://127.0.0.1:${port}/decks?qaHotItems=${itemsPerRow}`)
    await waitForLibrary(view)
    const motion = await measureTrackMotion(view.locator('.hot-deck-track'))
    assertTrackMotion(motion, scenario)
    const geometry = await view.evaluate(() => {
      const section = document.querySelector('.hot-decks')
      const rowBoxes = [...document.querySelectorAll('.hot-deck-viewport')].map(element => element.getBoundingClientRect())
      const loops = [...document.querySelectorAll('.hot-deck-track')].map(track => [...track.querySelectorAll(':scope > .hot-deck-loop')])
      const cards = [...document.querySelectorAll('.plaza-grid>article')].slice(0, 6).map(element => element.getBoundingClientRect())
      const sectionStyle = section ? getComputedStyle(section) : null
      return {
        documentFits: document.documentElement.scrollWidth <= innerWidth + 1,
        rows: rowBoxes.map(box => ({ x:box.x, y:box.y, width:box.width, height:box.height })),
        itemCounts: loops.map(row => row[0]?.children.length || 0),
        loopWidths: loops.map(row => row.map(loop => loop.getBoundingClientRect().width)),
        section: section ? { x:section.getBoundingClientRect().x, width:section.getBoundingClientRect().width, backgroundColor:sectionStyle.backgroundColor, borderTop:sectionStyle.borderTopWidth, borderBottom:sectionStyle.borderBottomWidth } : null,
        cards: cards.map(box => ({ x:box.x, y:box.y, width:box.width, height:box.height })),
      }
    })
    assert.equal(geometry.documentFits, true, `${scenario} 页面横向溢出`)
    assert.equal(geometry.rows.length, 2, `${scenario} 热门牌库行数错误`)
    assert.deepEqual(geometry.itemCounts, [itemsPerRow, itemsPerRow], `${scenario} 数据样本数量错误`)
    assert.ok(geometry.loopWidths.every(widths => widths.length === 2 && widths[0] > 0 && Math.abs(widths[0] - widths[1]) < .5), `${scenario} 两份循环必须等宽连续闭合`)
    assert.ok(geometry.rows.every(row => row.x >= -1 && row.x + row.width <= width + 1), `${scenario} 轮播视口越界`)
    const expectedRowHeight = width <= 700 ? 74 : 102
    assert.ok(geometry.rows.every(row => Math.abs(row.height - expectedRowHeight) < 1), width <= 700
      ? `${scenario} 移动端热门牌库尺寸不应跟随宽屏放大`
      : `${scenario} 宽屏热门牌库容器未按约30%增大`)
    assert.ok(geometry.cards.every(card => card.x >= -1 && card.x + card.width <= width + 1), `${scenario} 牌库盒子越界`)
    const screenshotName = itemsPerRow === 8 ? `deck-library-${width}x${height}.png` : `deck-library-${itemsPerRow}-items-${width}x${height}.png`
    await view.screenshot({ path:path.join(output,screenshotName), fullPage:true })
    report.push({ viewport:`${width}x${height}`, itemsPerRow, motion, ...geometry })
    await view.close()
  }

  const reduced = await browser.newPage({ viewport:{ width:390, height:844 }, reducedMotion:'reduce' })
  await prepare(reduced)
  await reduced.goto(`http://127.0.0.1:${port}/decks`)
  await waitForLibrary(reduced)
  assert.equal(await reduced.locator('.hot-deck-track').first().evaluate(element => getComputedStyle(element).animationName), 'none', '减少动态时应停止自动轮播')
  assert.equal(await reduced.locator('.hot-deck-loop[aria-hidden="true"]').first().evaluate(element => getComputedStyle(element).display), 'none', '减少动态时不应保留重复副本')
  await reduced.screenshot({ path:path.join(output,'deck-library-390x844-reduced-motion.png'), fullPage:true })
  await reduced.close()

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status:'passed', profiles:report }, null, 2))
  console.log(JSON.stringify({ status:'passed', screenshots:profiles.length + 1, output }))
} finally {
  await browser?.close()
  await server.close()
}
