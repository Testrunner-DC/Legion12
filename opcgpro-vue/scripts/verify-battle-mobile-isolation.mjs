import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5180/__l12_battle_preview__'
const out = process.env.L12_MOBILE_ISOLATION_OUT || path.resolve('..', 'artifacts', 'batch-rework-mobile-isolation')
const mobileViewports = [[667, 375], [740, 360], [844, 390], [852, 393], [915, 412], [932, 430], [1024, 768]]
const desktopViewports = [[1366, 768], [1440, 900], [1920, 1080]]
const checkDesktop = process.env.L12_CHECK_DESKTOP !== '0'
const checkMobile = process.env.L12_CHECK_MOBILE !== '0'
const mobileOffset = Math.max(0, Number(process.env.L12_MOBILE_OFFSET ?? 0))
const mobileLimit = Math.max(1, Number(process.env.L12_MOBILE_LIMIT ?? mobileViewports.length))
const selectedMobileViewports = mobileViewports.slice(mobileOffset, mobileOffset + mobileLimit)
const query = '?mobile=1&special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15'
fs.mkdirSync(out, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  if (checkDesktop) {
  const desktop = await browser.newContext()
  const desktopPage = await desktop.newPage()
    desktopPage.setDefaultTimeout(5_000)
    for (const [width, height] of desktopViewports) {
    await desktopPage.setViewportSize({ width, height })
    await desktopPage.goto(`${target}?special=1&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1&hand=15`, { waitUntil: 'domcontentloaded' })
    await desktopPage.locator('.felt-board').waitFor()
    assert.equal(await desktopPage.locator('.mobile-record-trigger').count(), 0, `desktop ${width}×${height} entered the mobile layout`)
    assert.equal(await desktopPage.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `desktop ${width}×${height} has horizontal overflow`)
    await desktopPage.screenshot({ path: path.join(out, `desktop-${width}x${height}.png`) })
    }
    await desktop.close()
  }

  if (checkMobile) {
  const mobile = await browser.newContext()
  await mobile.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse')
      ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} }) : native(query)
  })
  const mobilePage = await mobile.newPage()
  mobilePage.setDefaultTimeout(5_000)
  for (const [width, height] of selectedMobileViewports) {
    await mobilePage.setViewportSize({ width, height })
    await mobilePage.goto(`${target}${query}`, { waitUntil: 'domcontentloaded' })
    for (const selector of ['.felt-board', '.mobile-record-trigger', '.mobile-timed-clocks', '.formation-slot', '.pile', '.mini-master', '.relic-zone', '.board-center>.l12-hand:last-child'])
      await mobilePage.locator(selector).first().waitFor()
    const result = await mobilePage.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      cards: [...document.querySelectorAll('.card-tile')].every(card => { const r = card.getBoundingClientRect(); return r.width > 10 && r.height > 10 }),
      controls: ['.mobile-record-trigger', '.mobile-timed-clocks', '.action-panel button'].map(selector => { const r = document.querySelector(selector)?.getBoundingClientRect(); return Boolean(r && r.width > 0 && r.height > 0 && r.right >= 0 && r.left <= innerWidth && r.bottom >= 0 && r.top <= innerHeight) }),
      critical: ['.mobile-hand-count', '.resource-morale-count', '.resource-faction-action', '.mat-piles .deck', '.mat-piles .graveyard', '.mobile-record-trigger', '.mobile-timed-clocks', '.action-panel button'].flatMap(selector => [...document.querySelectorAll(selector)].map(element => {
        const r = element.getBoundingClientRect()
        return { selector, text: element.textContent?.trim(), inside: r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight, clipped: element.scrollWidth > element.clientWidth + 1 }
      })),
      intersections: (() => {
        const box = selector => document.querySelector(selector)?.getBoundingClientRect()
        const overlaps = (a, b) => Boolean(a && b && a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top)
        const left = box('.left-rail'), right = box('.right-rail'), hand = box('.board-center>.l12-hand:last-child')
        return {
          leftMaster: [...document.querySelectorAll('.mini-master,.relic-zone,.mobile-hand-count')].filter(item => overlaps(left, item.getBoundingClientRect())).map(item => item.className),
          rightResources: [...document.querySelectorAll('.resource-zone,.mat-piles')].some(item => overlaps(right, item.getBoundingClientRect())),
          handAction: overlaps(hand, box('.action-panel')),
        }
      })(),
    }))
    assert.equal(result.overflow, false, `mobile ${width}×${height} has horizontal overflow`)
    assert.equal(result.cards, true, `mobile ${width}×${height} has unreadable card geometry`)
    assert.equal(result.controls.every(Boolean), true, `mobile ${width}×${height} places a persistent control outside the viewport`)
    assert.equal(result.critical.every(item => item.inside && !item.clipped), true, `mobile ${width}×${height} clips a critical UI item: ${JSON.stringify(result.critical.filter(item => !item.inside || item.clipped))}`)
    assert.equal(result.intersections.leftMaster.length === 0 && result.intersections.rightResources === false && result.intersections.handAction === false, true, `mobile ${width}×${height} has protected-area intrusion: ${JSON.stringify(result.intersections)}`)
    await mobilePage.screenshot({ path: path.join(out, `mobile-${width}x${height}.png`) })
  }
  await mobile.close()
  }
  console.log(JSON.stringify({ out, desktopViewports: checkDesktop ? desktopViewports : [], mobileViewports: checkMobile ? selectedMobileViewports : [], status: 'passed' }, null, 2))
} finally { await browser.close() }
