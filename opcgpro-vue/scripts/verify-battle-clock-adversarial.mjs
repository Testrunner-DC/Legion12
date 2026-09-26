import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5191/__l12_battle_preview__'
const output = process.env.L12_CLOCK_ADVERSARIAL_OUT || path.resolve('artifacts', 'battle-clock-adversarial')
fs.mkdirSync(output, { recursive: true })

const desktop = [{ width: 1280, height: 720 }, { width: 1366, height: 768 }, { width: 1600, height: 900 }, { width: 1920, height: 1080 }, { width: 2560, height: 1080 }, { width: 3440, height: 1440 }]
const mobile = [{ width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 576 }]
const desktopZooms = [0.8, 0.9, 1, 1.1, 1.25, 1.5]
const scenarios = [
  ['normal', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&totalMs=3599999&operationMs=599999'],
  ['reconnect', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&disconnected=0&reconnectMs=119999'],
  ['preparation', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&clockPhase=Mulligan&operationMs=90000'],
  ['defense-dialog', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&defense=1'],
  ['support-dialog', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&support=1'],
  ['choice-modal', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&card-choice=1&choice-count=20'],
  ['gm-tools', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&gm=1'],
  ['record-overlay', 'field=full&markers=5&piles=40&hand=20&rankedClock=1'],
  ['game-over', 'field=full&markers=5&piles=40&hand=20&rankedClock=1&game-over=win'],
]
const report = { target, assertions: 0, screenshots: [], scenarios: [], status: 'running' }
const ok = (value, message) => { assert.ok(value, message); report.assertions += 1 }
const intersects = (a,b) => a.left < b.right-3 && a.right > b.left+3 && a.top < b.bottom-3 && a.bottom > b.top+3

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(12_000)
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  async function geometry(label, mobileMode) {
    const value = await page.evaluate(mobileMode => {
      const rect = node => { const r = node?.getBoundingClientRect(); return r && { left:r.left, top:r.top, right:r.right, bottom:r.bottom, width:r.width, height:r.height } }
      const visible = node => { const r=node?.getBoundingClientRect(); return Boolean(r && r.width > 0 && r.height > 0) }
      const clockNodes = [...document.querySelectorAll(mobileMode ? '.mobile-timed-clocks .player-turn-clock' : '.board-player-clock')].filter(visible)
      const clocks = clockNodes.map(rect)
      const clockClipping = clockNodes.flatMap((clock, clockIndex) => [...clock.querySelectorAll('strong,span,small,b')].filter(visible).filter(node => node.scrollWidth > node.clientWidth + 1 || node.scrollHeight > node.clientHeight + 1).map(node => ({ clockIndex, tag:node.tagName, text:node.textContent?.trim(), scrollWidth:node.scrollWidth, clientWidth:node.clientWidth, scrollHeight:node.scrollHeight, clientHeight:node.clientHeight })))
      const forbidden = [...document.querySelectorAll(mobileMode ? '.battle-dock-context,.battle-dock-primary,.mobile-record-overlay' : '.battlefield-half,.mat-piles,.resource-zone,.l12-hand,.record-log,.action-panel')].filter(visible).map(rect)
      const tracks = [...document.querySelectorAll('.board-status-lane')].filter(visible).map(rect)
      const hands = [...document.querySelectorAll('.board-center>.l12-hand')].filter(visible).map(rect).sort((a,b)=>a.top-b.top)
      const felt = rect(document.querySelector('.felt-board'))
      const rightRail = rect(document.querySelector('.right-rail'))
      const intersects = (a,b) => a.left < b.right-3 && a.right > b.left+3 && a.top < b.bottom-3 && a.bottom > b.top+3
      const overlapArea = (a,b) => Math.max(0,Math.min(a.right,b.right)-Math.max(a.left,b.left))*Math.max(0,Math.min(a.bottom,b.bottom)-Math.max(a.top,b.top))
      const root=getComputedStyle(document.documentElement),number=name=>parseFloat(root.getPropertyValue(name))||0
      const safe=mobileMode?{left:number('--l12-viewport-left'),top:number('--l12-viewport-top')}:{left:0,top:0}
      safe.right=mobileMode?safe.left+number('--l12-viewport-width'):innerWidth; safe.bottom=mobileMode?safe.top+number('--l12-viewport-height'):innerHeight
      const labels=[...document.querySelectorAll(mobileMode ? '.mobile-timed-clocks .player-turn-clock' : '.board-player-clock')].filter(visible).map(node=>node.textContent?.replace(/\s+/g,' ').trim())
      const buttons=[...document.querySelectorAll('.right-rail button,.battle-dock-root button,.combat-resolution-panel button,.mobile-record-overlay button,.game-over button')].filter(node=>visible(node)&&!node.disabled).map(node=>({node,rect:rect(node)})).filter(item=>item.rect)
      const blocked=buttons.filter(({node,rect:r})=>{const hit=document.elementFromPoint((r.left+r.right)/2,(r.top+r.bottom)/2);return hit && hit!==node && !node.contains(hit)}).map(({node})=>node.textContent?.trim()||node.getAttribute('aria-label'))
      const exactOverlaps = felt && rightRail ? clocks.flatMap((clock,index)=>[{index,target:'felt',area:overlapArea(clock,felt)},{index,target:'rightRail',area:overlapArea(clock,rightRail)}]) : []
      return {clocks,clockClipping,tracks,hands,felt,rightRail,forbidden,labels,safe,exactOverlaps,overlaps:clocks.flatMap(clock=>forbidden.filter(item=>intersects(clock,item))),blocked,modal:Boolean(document.querySelector('[aria-modal="true"]')),overflow:{x:document.documentElement.scrollWidth>innerWidth+1,y:document.documentElement.scrollHeight>innerHeight+1}}
    }, mobileMode)
    ok(value.clocks.length === 2, `${label}: expected exactly two visible clocks, got ${value.clocks.length}`)
    ok(value.clockClipping.length === 0, `${label}: ranked clock content is clipped ${JSON.stringify(value.clockClipping)}`)
    ok(value.clocks.every(r => r.left >= value.safe.left-1 && r.top >= value.safe.top-1 && r.right <= value.safe.right+1 && r.bottom <= value.safe.bottom+1), `${label}: clock leaves safe viewport ${JSON.stringify(value)}`)
    if (!mobileMode) {
      ok(value.tracks.length === 2 && value.hands.length === 2, `${label}: expected two desktop tracks and hand lanes ${JSON.stringify(value)}`)
      ok(Math.abs(value.tracks[0].left-value.tracks[1].left)<=1 && Math.abs(value.tracks[0].width-value.tracks[1].width)<=1, `${label}: opponent/my clock tracks are not geometrically aligned ${JSON.stringify(value.tracks)}`)
      ok(value.clocks.every((clock,index)=>clock.left>=value.tracks[index].left-1 && clock.right<=value.tracks[index].right+1 && clock.top>=value.tracks[index].top-1 && clock.bottom<=value.tracks[index].bottom+1), `${label}: a clock escapes its external track ${JSON.stringify(value)}`)
      ok(value.clocks.every((clock,index)=>Math.abs(clock.width-value.tracks[index].width)<=1), `${label}: a ranked clock does not fill its 196px safe track ${JSON.stringify(value)}`)
      ok(Math.abs(value.clocks[0].left-value.clocks[1].left)<=1 && Math.abs(value.clocks[0].width-value.clocks[1].width)<=1 && Math.abs(value.clocks[0].height-value.clocks[1].height)<=1, `${label}: the two clocks are not geometrically mirrored ${JSON.stringify(value.clocks)}`)
      ok(value.felt && value.rightRail, `${label}: missing battlefield or right rail geometry ${JSON.stringify(value)}`)
      ok(Math.abs(value.felt.right-value.rightRail.left)<=1, `${label}: battlefield does not join the right rail ${JSON.stringify(value)}`)
      ok(value.clocks.every(clock=>Math.abs(clock.right-value.rightRail.left)<=1), `${label}: clock right edge does not join the right rail ${JSON.stringify(value)}`)
      ok(value.exactOverlaps.every(item=>item.area<=0.01), `${label}: clock has non-zero overlap with battlefield or right rail ${JSON.stringify(value.exactOverlaps)}`)
      ok(value.clocks.every(clock=>!intersects(clock,value.felt)&&!intersects(clock,value.rightRail)), `${label}: clock enters battlefield or right rail ${JSON.stringify(value)}`)
      ok(Math.abs(value.clocks[0].bottom-value.felt.top)<=1, `${label}: opponent clock bottom is not attached to the battlefield top ${JSON.stringify(value)}`)
      ok(Math.abs(value.clocks[1].top-value.felt.bottom)<=1, `${label}: my clock top is not attached to the battlefield bottom ${JSON.stringify(value)}`)
    }
    if(!value.modal) ok(value.overlaps.length === 0, `${label}: clock intrudes into hand/log/action/dialog lane ${JSON.stringify(value)}`)
    ok(!value.overflow.x && !value.overflow.y, `${label}: document overflow ${JSON.stringify(value.overflow)}`)
    if(!value.modal) ok(value.blocked.length === 0, `${label}: visible buttons lose their centre hit target ${JSON.stringify(value.blocked)}`)
    return value
  }

  for (const mobileMode of [false, true]) {
    for (const viewport of mobileMode ? mobile : desktop) {
      for (const [name, query] of scenarios) {
        await page.setViewportSize(viewport)
        await page.goto(`${target}?${mobileMode?'mobile=1&canvas=1&':''}${query}`, { waitUntil: 'domcontentloaded' })
        if (mobileMode) await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
        if (name === 'record-overlay' && mobileMode) await page.locator('.mobile-record-trigger').click()
        await page.waitForTimeout(300)
        const label = `${mobileMode?'mobile':'desktop'} ${viewport.width}x${viewport.height} ${name}`
        const value = await geometry(label, mobileMode)
        if (name === 'normal') {
          ok(value.labels.some(text => text.includes('60:')), `${label}: long total duration is not rendered with stable minute digits ${JSON.stringify(value.labels)}`)
          ok(value.labels.some(text => text.includes('10:')), `${label}: long operation duration is not rendered ${JSON.stringify(value.labels)}`)
        }
        if (name === 'reconnect') ok(value.labels.some(text => text.includes('重连')), `${label}: reconnect timer missing`)
        if (name === 'preparation') ok(value.labels.some(text => text.includes('准备')), `${label}: preparation state missing`)
        if (['normal','reconnect','defense-dialog','record-overlay'].includes(name)) {
          const file = `${mobileMode?'mobile':'desktop'}-${viewport.width}x${viewport.height}-${name}.png`
          await page.screenshot({ path:path.join(output,file) })
          report.screenshots.push(file)
        }
        report.scenarios.push({ label, labels:value.labels, clocks:value.clocks, tracks:value.tracks, felt:value.felt, rightRail:value.rightRail })
      }
    }
  }

  // Browser zoom changes the effective CSS viewport. Exercise the complete
  // supported zoom ladder from the 1920x1080 desktop anchor; the resulting
  // 1280x720 through 2400x1350 CSS viewports remain in the desktop branch.
  const zoomViewport = { width:1920, height:1080 }
  for (const zoom of desktopZooms) {
      const effective = { width:Math.round(zoomViewport.width/zoom), height:Math.round(zoomViewport.height/zoom) }
      await page.setViewportSize(effective)
      await page.goto(`${target}?field=full&markers=5&piles=40&hand=20&rankedClock=1&totalMs=3599999&operationMs=599999`, { waitUntil:'domcontentloaded' })
      await page.waitForTimeout(80)
      const label = `desktop physical ${zoomViewport.width}x${zoomViewport.height} zoom ${Math.round(zoom*100)}% effective ${effective.width}x${effective.height}`
      const value = await geometry(label, false)
      report.scenarios.push({ label, zoom, physicalViewport:zoomViewport, effectiveViewport:effective, clocks:value.clocks, tracks:value.tracks, felt:value.felt, rightRail:value.rightRail })
  }

  // Ranked clocks expose the densest content. Stress unusually long minute
  // fields, simultaneous reconnect labels and the preparation transition in
  // the narrowest supported desktop track.
  await page.setViewportSize({ width:1280, height:720 })
  await page.goto(`${target}?field=full&markers=5&piles=40&hand=40&rankedClock=1&totalMs=359999999&operationMs=359999999`, { waitUntil:'domcontentloaded' })
  await page.locator('.board-player-clock').first().waitFor()
  await page.waitForFunction(() => Boolean(window.__clockFixture))
  await page.evaluate(() => { window.__clockFixture.disconnect(0, 359999999); window.__clockFixture.disconnect(1, 359999999) })
  await page.waitForTimeout(300)
  let rankedStress = await geometry('desktop 1280x720 ranked simultaneous reconnect max digits', false)
  ok(rankedStress.labels.every(text => text.includes('重连') && text.includes('6000:')), `ranked reconnect stress did not render the complete long values ${JSON.stringify(rankedStress.labels)}`)
  await page.screenshot({ path:path.join(output,'desktop-1280x720-ranked-max-digits-reconnect.png') })
  report.screenshots.push('desktop-1280x720-ranked-max-digits-reconnect.png')
  await page.evaluate(() => { window.__clockFixture.reconnect(0); window.__clockFixture.reconnect(1); window.__clockFixture.setPhase('Mulligan',0); window.__clockFixture.setDurations(359999999,359999999) })
  await page.waitForTimeout(300)
  rankedStress = await geometry('desktop 1280x720 ranked preparation max digits', false)
  ok(rankedStress.labels.some(text => text.includes('准备操作') && text.includes('6000:')), `ranked preparation stress did not render the complete long values ${JSON.stringify(rankedStress.labels)}`)
  await page.screenshot({ path:path.join(output,'desktop-1280x720-ranked-max-digits-preparation.png') })
  report.screenshots.push('desktop-1280x720-ranked-max-digits-preparation.png')

  // Phase/reconnect recovery and digit-boundary transitions must update in
  // place without allocating a new overlay or losing the action hit layer.
  await page.setViewportSize({ width:844, height:390 })
  await page.goto(`${target}?mobile=1&canvas=1&field=full&rankedClock=1&totalMs=61000&operationMs=1000`, { waitUntil:'domcontentloaded' })
  await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
  await page.evaluate(() => window.__clockFixture.disconnect(0, 61000))
  await page.waitForTimeout(300)
  ok((await page.locator('.mobile-timed-clocks').innerText()).includes('重连'), 'continuous state: disconnect did not expose reconnect clock')
  await page.evaluate(() => { window.__clockFixture.reconnect(0); window.__clockFixture.setPhase('Mulligan',0) })
  await page.waitForTimeout(300)
  ok((await page.locator('.mobile-timed-clocks').innerText()).includes('准备'), 'continuous state: reconnect/phase transition did not recover preparation clock')
  for (let width=667; width<=1024; width+=17) {
    await page.setViewportSize({ width, height:Math.round(width*.46) })
    await page.evaluate(() => window.dispatchEvent(new Event('resize')))
    await page.waitForTimeout(18)
    await geometry(`continuous resize ${width}`, true)
  }

  // Desktop boundary sweep catches regressions caused by a single exact media
  // query: both clocks must stay in mirrored external rows while width and
  // height cross the common laptop and ultrawide thresholds continuously.
  await page.goto(`${target}?field=full&markers=5&piles=40&hand=20&rankedClock=1&totalMs=3599999&operationMs=599999`, { waitUntil:'domcontentloaded' })
  for (const viewport of [
    {width:1280,height:720},{width:1299,height:720},{width:1366,height:768},
    {width:1599,height:720},{width:1600,height:800},{width:1919,height:900},
    {width:1920,height:1080},{width:2559,height:1080},{width:2560,height:1080},
    {width:3439,height:1080},{width:3440,height:1440},
  ]) {
    await page.setViewportSize(viewport)
    await page.evaluate(() => window.dispatchEvent(new Event('resize')))
    await page.waitForTimeout(24)
    await geometry(`desktop continuous resize ${viewport.width}x${viewport.height}`, false)
  }

  report.status = 'passed'
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2))
  console.log(`Battle clock adversarial verification passed: ${report.assertions} assertions, ${report.screenshots.length} screenshots`)
} finally {
  await browser.close()
}
