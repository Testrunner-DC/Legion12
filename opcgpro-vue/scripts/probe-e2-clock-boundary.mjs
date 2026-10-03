import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const base = process.argv[2] || 'http://127.0.0.1:5298/__l12_battle_preview__'
const output = process.env.L12_E2_CLOCK_PROBE_OUT
const allowClipping = process.env.L12_E2_CLOCK_ALLOW_CLIPPING === '1'
const baselinePath = process.env.L12_E2_CLOCK_BASELINE_REPORT
assert(output && path.isAbsolute(output), 'L12_E2_CLOCK_PROBE_OUT must be a fresh absolute path')
assert(!fs.existsSync(output), `Refusing existing output directory: ${output}`)
fs.mkdirSync(output, { recursive: true })

const profiles = [
  { name: 'desktop-1920x1080', width: 1920, height: 1080, mobile: false },
  { name: 'desktop-2560x1440', width: 2560, height: 1440, mobile: false },
  { name: 'desktop-3440x1440', width: 3440, height: 1440, mobile: false },
  { name: 'mobile-844x390', width: 844, height: 390, mobile: true },
  { name: 'mobile-740x360', width: 740, height: 360, mobile: true },
  { name: 'mobile-667x375', width: 667, height: 375, mobile: true },
  { name: 'mobile-640x320-safe', width: 640, height: 320, mobile: true, safe: true },
  { name: 'mobile-600x320-safe', width: 600, height: 320, mobile: true, safe: true },
  { name: 'mobile-568x320', width: 568, height: 320, mobile: true },
  { name: 'mobile-390x844', width: 390, height: 844, mobile: true },
  { name: 'mobile-360x640', width: 360, height: 640, mobile: true },
  { name: 'mobile-320x568', width: 320, height: 568, mobile: true },
]
const modes = [
  { name: 'default', ranked: true, query: 'rankedClock=1&totalMs=1500000&operationMs=240000&disconnected=1&reconnectMs=240000', expected: ['25:00', '04:00'] },
  { name: 'legal-maximum', ranked: true, query: 'rankedClock=1&totalMs=7200000&operationMs=900000&disconnected=1&reconnectMs=900000', expected: ['120:00', '15:00'] },
  { name: 'reconnect-actor', ranked: true, query: 'rankedClock=1&totalMs=1500000&operationMs=240000&disconnected=0&reconnectMs=240000', expected: ['25:00', '04:00'] },
  { name: 'actor-swapped', ranked: true, query: 'rankedClock=1&totalMs=1500000&operationMs=240000&activePlayer=1', expected: ['25:00', '04:00'] },
  { name: 'preparation-hidden-total', ranked: true, query: 'rankedClock=1&totalMs=1500000&operationMs=240000&disconnected=0&reconnectMs=240000&clockPhase=Mulligan', expected: ['04:00'] },
  { name: 'unranked', ranked: false, query: '', expected: [] },
]
const cssPath = path.join(root, 'src/l12/game/MobileBattleDock.css')
const scriptPath = fileURLToPath(import.meta.url)
const sha256 = filename => crypto.createHash('sha256').update(fs.readFileSync(filename)).digest('hex')
const sourceFile = relativePath => ({ path: relativePath, sha256: sha256(path.join(root, relativePath)) })
const report = {
  schema: 2, status: 'running', allowClipping, base,
  authority: { totalTimeSecondsDefault: 1500, operationTimeSecondsDefault: 240, reconnectGraceSecondsDefault: 240, totalTimeSecondsMax: 7200, operationTimeSecondsMax: 900, reconnectGraceSecondsMax: 900 },
  source: {
    css: { path: path.relative(root, cssPath).replaceAll('\\', '/'), sha256: sha256(cssPath) },
    verifier: { path: path.relative(root, scriptPath).replaceAll('\\', '/'), sha256: sha256(scriptPath) },
    clockComponent: sourceFile('src/l12/game/PlayerTurnClock.vue'),
    battleHost: sourceFile('src/l12/game/GameBoard.vue'),
    previewFixture: sourceFile('scripts/preview-l12-battle-layout.mjs'),
    defaultAuthority: sourceFile('src/l12/platform.ts'),
    limitAuthority: sourceFile('src/l12/site/SeasonConfigurationEditor.vue'),
  },
  scenarios: [], interactions: [], errors: [], summary: {},
}
const baseline = baselinePath ? JSON.parse(fs.readFileSync(baselinePath, 'utf8')) : null
const baselineByKey = new Map((baseline?.scenarios ?? []).map(item => [`${item.profile}/${item.mode}`, item]))

function writeReport() {
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  fs.writeFileSync(path.join(output, 'report.md'), [
    '# 667×375 ranked clock text containment', '', `Status: ${report.status}`, `Source CSS SHA: ${report.source.css.sha256}`, `Verifier SHA: ${report.source.verifier.sha256}`,
    `Scenarios: ${report.scenarios.length}`, `Clipped text nodes after per-clock scrolling: ${report.summary.clipped ?? 'pending'}`, `Initially outside the existing tools scrollport: ${report.summary.initialAncestorClipped ?? 'pending'}`, `Text overlaps: ${report.summary.overlaps ?? 'pending'}`, `Max geometry delta: ${report.summary.maxGeometryDelta ?? 'pending'}px`, '',
    ...report.scenarios.map(item => `- ${item.profile}/${item.mode}: clipped ${item.clipped.length}, overlaps ${item.overlaps.length}, geometry delta ${item.geometryDelta ?? 0}px`),
    ...(report.errors.length ? ['', '## Errors', ...report.errors.map(error => `- ${error}`)] : []),
  ].join('\n'))
}
writeReport()

function params(profile, mode) {
  const value = new URLSearchParams({ field: 'full', hand: '40', markers: '5', piles: '40' })
  if (profile.mobile) { value.set('mobile', '1'); value.set('canvas', '1') }
  if (profile.safe) value.set('safe', '1')
  for (const [key, entry] of new URLSearchParams(mode.query)) value.set(key, entry)
  return value.toString()
}

async function measure(page, profile, mode) {
  return page.evaluate(async ({ profile, mode }) => {
    const roundedRect = element => {
      if (!element) return null
      const box = element.getBoundingClientRect()
      return Object.fromEntries(['x', 'y', 'width', 'height', 'right', 'bottom'].map(key => [key, Math.round(box[key] * 100) / 100]))
    }
    const intersection = (left, right) => {
      if (!left || !right) return 0
      return Math.max(0, Math.min(left.right, right.right) - Math.max(left.x, right.x))
        * Math.max(0, Math.min(left.bottom, right.bottom) - Math.max(left.y, right.y))
    }
    const clippedByAncestor = element => {
      const box = element.getBoundingClientRect()
      for (let parent = element.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent)
        const clipsX = ['hidden', 'clip', 'auto', 'scroll'].includes(style.overflowX)
        const clipsY = ['hidden', 'clip', 'auto', 'scroll'].includes(style.overflowY)
        if (!clipsX && !clipsY) continue
        const parentBox = parent.getBoundingClientRect()
        if ((clipsX && (box.left < parentBox.left - 1 || box.right > parentBox.right + 1))
          || (clipsY && (box.top < parentBox.top - 1 || box.bottom > parentBox.bottom + 1)))
          return { tag: parent.tagName, className: parent.className, clipsX, clipsY, rect: roundedRect(parent) }
      }
      return null
    }
    const mobileClocks = [...document.querySelectorAll('.mobile-rail-clock')]
    const desktopClocks = [...document.querySelectorAll('.board-status-lane .board-player-clock')]
      .filter((element) => {
        const rect = element.getBoundingClientRect()
        const style = getComputedStyle(element)
        return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0
      })
    const clocks = profile.mobile ? mobileClocks : desktopClocks
    const rects = {
      stage: roundedRect(document.querySelector('[data-l12-game-stage]')),
      felt: roundedRect(document.querySelector('.felt-board')),
      opponentMat: roundedRect(document.querySelector('.opponent-half')),
      myMat: roundedRect(document.querySelector('.my-half')),
      rightRail: roundedRect(document.querySelector('.right-rail')),
      dock: roundedRect(document.querySelector('.mobile-battle-dock')),
      opponentClock: roundedRect(document.querySelector('.mobile-rail-clock.side-opponent')),
      myClock: roundedRect(document.querySelector('.mobile-rail-clock.side-my')),
    }
    const initialAncestorClipped = profile.mobile
      ? mobileClocks.flatMap(clock => [...clock.querySelectorAll('strong,small,b')]).filter(element => clippedByAncestor(element)).length
      : 0
    const toolScroller = document.querySelector('.mobile-battle-dock__tools')
    const initialToolScrollTop = toolScroller?.scrollTop ?? 0
    const scrollSamples = []
    if (profile.mobile) {
      for (const [clockIndex, clock] of mobileClocks.entries()) {
        clock.scrollIntoView({ block: 'nearest', inline: 'nearest' })
        await new Promise(resolve => requestAnimationFrame(() => resolve()))
        const textNodes = [...clock.querySelectorAll('strong,small,b')].map(element => {
          const style = getComputedStyle(element)
          const rect = roundedRect(element)
          const range = document.createRange()
          range.selectNodeContents(element)
          const textRect = roundedRect(range)
          const containingRect = roundedRect(element.closest('span') ?? element)
          const glyphClipped = textRect.x < containingRect.x - 1 || textRect.right > containingRect.right + 1
            || textRect.y < containingRect.y - 1 || textRect.bottom > containingRect.bottom + 1
          const clippedAncestor = clippedByAncestor(element)
          return {
            clockIndex, tag: element.tagName, text: element.textContent?.trim() ?? '', rect,
            scrollWidth: element.scrollWidth, clientWidth: element.clientWidth, scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
            fontSize: style.fontSize, lineHeight: style.lineHeight, opacity: style.opacity, visibility: style.visibility, textRect, containingRect, glyphClipped,
            clippedByAncestor: clippedAncestor,
            clipped: glyphClipped || Boolean(clippedAncestor) || style.visibility === 'hidden' || Number(style.opacity) === 0,
          }
        })
        const spanNodes = [...clock.querySelectorAll('span')].map(element => {
          const clippedAncestor = clippedByAncestor(element)
          return {
            clockIndex, tag: element.tagName, text: element.textContent?.trim() ?? '', rect: roundedRect(element),
            scrollWidth: element.scrollWidth, clientWidth: element.clientWidth, scrollHeight: element.scrollHeight, clientHeight: element.clientHeight,
            clippedByAncestor: clippedAncestor,
            clipped: element.scrollWidth > element.clientWidth || element.scrollHeight > element.clientHeight || Boolean(clippedAncestor),
          }
        })
        const overlaps = [...clock.querySelectorAll('span')].flatMap((span, spanIndex) => {
          const small = span.querySelector('small')
          const bold = span.querySelector('b')
          const area = intersection(small?.getBoundingClientRect(), bold?.getBoundingClientRect())
          return area > 1 ? [{ clockIndex, spanIndex, area: Math.round(area * 100) / 100, label: small?.textContent?.trim(), value: bold?.textContent?.trim() }] : []
        })
        scrollSamples.push({ clockIndex, toolScrollTop: toolScroller?.scrollTop ?? 0, clockRect: roundedRect(clock), textNodes, spanNodes, overlaps })
      }
      if (toolScroller) toolScroller.scrollTop = initialToolScrollTop
      await new Promise(resolve => requestAnimationFrame(() => resolve()))
    }
    const textNodes = scrollSamples.flatMap(sample => sample.textNodes)
    const spanNodes = scrollSamples.flatMap(sample => sample.spanNodes)
    const overlaps = scrollSamples.flatMap(sample => sample.overlaps)
    const desktopClockCount = desktopClocks.length
    return {
      profile: profile.name, mode: mode.name, viewport: { width: innerWidth, height: innerHeight }, rects, initialToolScrollTop, initialAncestorClipped, scrollSamples, textNodes, spanNodes, overlaps,
      clipped: [...textNodes, ...spanNodes].filter(entry => entry.clipped),
      clockText: clocks.map(clock => clock.textContent?.replace(/\s+/g, ' ').trim() ?? ''),
      clockBindings: mobileClocks.map(clock => ({ className: clock.className, playerIndex: clock.getAttribute('data-player-index') })),
      mobileClockCount: mobileClocks.length, desktopClockCount,
      pageOverflow: { x: document.documentElement.scrollWidth - innerWidth, y: document.documentElement.scrollHeight - innerHeight },
    }
  }, { profile, mode })
}

function geometryDelta(current, prior) {
  if (!prior) {
    assert(!baseline, `${current.profile}/${current.mode}: missing baseline scenario`)
    return 0
  }
  let delta = 0
  for (const key of ['stage', 'felt', 'opponentMat', 'myMat', 'rightRail', 'dock', 'opponentClock', 'myClock']) {
    assert.equal(Boolean(current.rects[key]), Boolean(prior.rects?.[key]), `${current.profile}/${current.mode}: ${key} rect presence changed`)
    if (!current.rects[key]) continue
    for (const field of ['x', 'y', 'width', 'height']) delta = Math.max(delta, Math.abs(current.rects[key][field] - prior.rects[key][field]))
  }
  return Math.round(delta * 100) / 100
}

async function centerHit(page, selector) {
  return page.locator(selector).first().evaluate(element => {
    const box = element.getBoundingClientRect()
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
    return Boolean(hit && (hit === element || element.contains(hit)))
  })
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const profile of profiles) {
    const context = await browser.newContext({ viewport: { width: profile.width, height: profile.height }, hasTouch: profile.mobile })
    const page = await context.newPage()
    page.setDefaultTimeout(12_000)
    const pageErrors = []
    page.on('pageerror', error => pageErrors.push(error.message))
    for (const mode of modes) {
      await page.goto(`${base}?${params(profile, mode)}`, { waitUntil: 'domcontentloaded' })
      await page.locator('.game-page').waitFor()
      const result = await measure(page, profile, mode)
      result.geometryDelta = geometryDelta(result, baselineByKey.get(`${profile.name}/${mode.name}`))
      assert(result.geometryDelta <= 1, `${profile.name}/${mode.name}: geometry changed ${result.geometryDelta}px`)
      assert.equal(result.overlaps.length, 0, `${profile.name}/${mode.name}: label/value overlap ${JSON.stringify(result.overlaps)}`)
      assert.deepEqual(pageErrors, [], `${profile.name}/${mode.name}: page errors`)
      assert(result.pageOverflow.x <= 1 && result.pageOverflow.y <= 1, `${profile.name}/${mode.name}: page overflow ${JSON.stringify(result.pageOverflow)}`)
      for (const expected of mode.expected) assert(result.clockText.some(text => text.includes(expected)), `${profile.name}/${mode.name}: missing ${expected}`)
      if (profile.mobile) {
        assert.equal(result.mobileClockCount, mode.ranked ? 2 : 0, `${profile.name}/${mode.name}: mobile clocks`)
        assert.equal(result.desktopClockCount, 0, `${profile.name}/${mode.name}: hidden desktop clocks must stay hidden`)
        if (mode.ranked) {
          assert(result.rects.opponentClock && result.rects.myClock, `${profile.name}/${mode.name}: both clock rects required`)
          assert(result.clockBindings[0]?.className.includes('side-opponent'), `${profile.name}/${mode.name}: opponent clock binding`)
          assert(result.clockBindings[1]?.className.includes('side-my'), `${profile.name}/${mode.name}: my clock binding`)
        }
      }
      if (!allowClipping) assert.equal(result.clipped.length, 0, `${profile.name}/${mode.name}: clipped text ${JSON.stringify(result.clipped)}`)
      report.scenarios.push(result)
      if (profile.name === 'mobile-667x375' && ['default', 'legal-maximum', 'reconnect-actor', 'actor-swapped', 'preparation-hidden-total', 'unranked'].includes(mode.name)) {
        await page.screenshot({ path: path.join(output, `${mode.name}-667x375.png`) })
        if (['default', 'legal-maximum'].includes(mode.name)) {
          for (const [clockIndex, side] of ['opponent', 'my'].entries()) {
            await page.locator('.mobile-rail-clock').nth(clockIndex).evaluate(element => element.scrollIntoView({ block: 'nearest', inline: 'nearest' }))
            await page.screenshot({ path: path.join(output, `${mode.name}-${side}-clock-visible-667x375.png`) })
          }
          await page.locator('.mobile-battle-dock__tools').evaluate(element => { element.scrollTop = 0 })
        }
      }
    }
    if (profile.name === 'mobile-667x375') {
      await page.goto(`${base}?${params(profile, modes[0])}`, { waitUntil: 'domcontentloaded' })
      await page.getByRole('button', { name: '对局记录', exact: true }).click()
      await page.locator('.mobile-record-overlay').getByRole('button', { name: '最小化' }).click()
      assert(await centerHit(page, '.mobile-record-restore'), 'record restore must be topmost')
      await page.locator('.mobile-record-restore').click()
      await page.locator('.mobile-record-overlay').getByRole('button', { name: '关闭' }).click()
      page.once('dialog', dialog => dialog.dismiss())
      await page.getByRole('button', { name: '投降', exact: true }).click()
      assert(await centerHit(page, '.battle-route-controls [aria-label="返回大厅"]'), 'return must remain topmost')
      const toolsScroller = page.locator('.mobile-battle-dock__tools')
      await toolsScroller.hover()
      const scrollStart = await toolsScroller.evaluate(element => element.scrollTop)
      await page.mouse.wheel(0, 500)
      await page.waitForTimeout(80)
      const scrollDown = await toolsScroller.evaluate(element => element.scrollTop)
      assert(scrollDown > scrollStart, 'clock tools must respond to downward wheel scrolling')
      await page.mouse.wheel(0, -500)
      await page.waitForTimeout(80)
      const scrollUp = await toolsScroller.evaluate(element => element.scrollTop)
      assert(scrollUp < scrollDown, 'clock tools must respond to upward wheel scrolling')
      const touchSession = await context.newCDPSession(page)
      const toolsBox = await toolsScroller.boundingBox()
      assert(toolsBox && toolsBox.height > 32, 'clock tools must have a touchable scrollport')
      const touchX = toolsBox.x + toolsBox.width / 2
      const touchTop = toolsBox.y + 8
      const touchBottom = toolsBox.y + toolsBox.height - 8
      async function swipeTools(fromY, toY) {
        await touchSession.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: touchX, y: fromY }] })
        for (let step = 1; step <= 6; step++) {
          await touchSession.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: touchX, y: fromY + (toY - fromY) * step / 6 }] })
          await page.waitForTimeout(40)
        }
        await touchSession.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
        await page.waitForTimeout(150)
        return toolsScroller.evaluate(element => element.scrollTop)
      }
      const touchScrollStart = await toolsScroller.evaluate(element => element.scrollTop)
      const touchScrollDown = await swipeTools(touchBottom, touchTop)
      assert(touchScrollDown > touchScrollStart, 'clock tools must respond to upward touch swipe')
      const touchScrollUp = await swipeTools(touchTop, touchBottom)
      assert(touchScrollUp < touchScrollDown, 'clock tools must respond to downward touch swipe')
      await touchSession.detach()
      report.interactions.push({ profile: profile.name, actions: ['record-open', 'record-minimize', 'record-restore', 'record-close', 'surrender-dismiss', 'return-hit', 'clock-tools-wheel-down', 'clock-tools-wheel-up', 'clock-tools-touch-down', 'clock-tools-touch-up'], scroll: { start: scrollStart, down: scrollDown, up: scrollUp }, touchScroll: { start: touchScrollStart, down: touchScrollDown, up: touchScrollUp } })
    }
    await context.close()
    writeReport()
  }
  report.summary = {
    clipped: report.scenarios.reduce((total, item) => total + item.clipped.length, 0),
    initialAncestorClipped: report.scenarios.reduce((total, item) => total + item.initialAncestorClipped, 0),
    overlaps: report.scenarios.reduce((total, item) => total + item.overlaps.length, 0),
    maxGeometryDelta: Math.max(0, ...report.scenarios.map(item => item.geometryDelta)),
    scenarios: report.scenarios.length,
    interactions: report.interactions.length,
  }
  for (const item of Object.values(report.source)) assert.equal(sha256(path.join(root, item.path)), item.sha256, `source changed during probe: ${item.path}`)
  report.status = allowClipping ? (baseline ? 'stress-observed' : 'baseline-captured') : 'passed'
} catch (error) {
  report.status = 'failed'
  report.errors.push(error instanceof Error ? (error.stack ?? error.message) : String(error))
  throw error
} finally {
  await browser.close()
  writeReport()
}

console.log(`Clock boundary ${report.status}: ${report.summary.scenarios} scenarios, ${report.summary.clipped} clipped, ${report.summary.overlaps} overlaps, max geometry delta ${report.summary.maxGeometryDelta}px`)
