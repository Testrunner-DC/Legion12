import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const current = process.env.L12_CURRENT_BATTLE_URL || 'http://127.0.0.1:5177/__l12_battle_preview__'
const baseline = process.env.L12_BASELINE_BATTLE_URL || 'http://127.0.0.1:5178/__l12_battle_preview__'
const out = process.env.L12_DESKTOP_RESTORE_OUT || 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/desktop-battle-restore-20260920'
fs.mkdirSync(out, { recursive: true })

const desktop = [{ width: 1920, height: 1080 }, { width: 1600, height: 900 }, { width: 1366, height: 768 }]
const mobile = [{ width: 932, height: 430 }, { width: 844, height: 390 }]
const selectors = ['.board-stage', '.stage-layout', '.left-rail', '.phase-column', '.felt-board', '.opponent-half', '.my-half', '.opponent-hand', '.right-rail', '.record-log', '.action-panel', '.session-disaster-panel', '.current-disaster-panel']
const rects = async page => page.evaluate(selectors => Object.fromEntries(selectors.map(selector => {
  const node = document.querySelector(selector)
  const rect = node?.getBoundingClientRect()
  return [selector, rect && { x: rect.x, y: rect.y, width: rect.width, height: rect.height }]
})), selectors)
const overflow = async page => page.evaluate(() => ({ x: document.documentElement.scrollWidth > innerWidth + 1, y: document.documentElement.scrollHeight > innerHeight + 1 }))
const closeEnough = (actual, expected, label) => {
  assert(actual && expected, `${label} missing`)
  for (const key of ['x', 'y', 'width', 'height']) assert(Math.abs(actual[key] - expected[key]) <= 1.1, `${label}.${key} desktop baseline drift: ${actual[key]} vs ${expected[key]}`)
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext()
  const before = await context.newPage()
  const after = await context.newPage()
  for (const viewport of desktop) {
    await before.setViewportSize(viewport); await after.setViewportSize(viewport)
    await before.goto(`${baseline}?hand=15&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1`, { waitUntil: 'domcontentloaded' })
    await after.goto(`${current}?hand=15&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1`, { waitUntil: 'domcontentloaded' })
    assert.equal(await after.locator('[data-l12-mobile-landscape="true"]').count(), 0, `desktop ${viewport.width}x${viewport.height} must not enter mobile root`)
    assert.deepEqual(await overflow(after), { x: false, y: false }, `desktop ${viewport.width}x${viewport.height} overflows`)
    const expected = await rects(before), actual = await rects(after)
    for (const selector of selectors) closeEnough(actual[selector], expected[selector], `${viewport.width}x${viewport.height} ${selector}`)
    await before.screenshot({ path: path.join(out, `baseline-${viewport.width}x${viewport.height}.png`) })
    await after.screenshot({ path: path.join(out, `restored-${viewport.width}x${viewport.height}.png`) })
  }
  await context.close()
  const mobileContext = await browser.newContext()
  const page = await mobileContext.newPage()
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse') ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} }) : native(query)
  })
  for (const viewport of mobile) {
    await page.setViewportSize(viewport)
    await page.goto(`${current}?mobile=1&hand=15&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    assert.deepEqual(await overflow(page), { x: false, y: false }, `mobile ${viewport.width}x${viewport.height} overflows`)
    assert(await page.locator('.mobile-record-trigger').count() > 0, `mobile ${viewport.width}x${viewport.height} lacks record entry`)
    assert(await page.locator('.mobile-timed-clocks').count() > 0, `mobile ${viewport.width}x${viewport.height} lacks persistent clocks`)
    await page.screenshot({ path: path.join(out, `mobile-${viewport.width}x${viewport.height}.png`) })
  }
  await mobileContext.close()
  console.log(JSON.stringify({ out, desktop, mobile, status: 'passed' }, null, 2))
} finally { await browser.close() }
