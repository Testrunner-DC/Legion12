import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5175/__l12_battle_preview__?mobile=1&hand=24&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1'
const output = process.env.L12_BATTLE_ACCEPTANCE_OUT || 'C:/Users/neptu/Documents/ChatGPT/Legion12/battle-acceptance-current-20260918-r2'

fs.mkdirSync(output, { recursive: true })
console.log('Starting battle acceptance capture')
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(8_000)
  page.setDefaultNavigationTimeout(8_000)
  // Headless Edge does not consistently expose `hover: none` with a touch
  // context.  The fixture's `mobile=1` route is explicitly a phone scenario,
  // so provide the same coarse-touch signal as the real handset test surface.
  await page.addInitScript(() => {
    const nativeMatchMedia = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse') ? ({ matches: true }) : nativeMatchMedia(query)
  })
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())
  const load = async (viewport, query = '') => {
    console.log(`Capturing ${viewport.width}x${viewport.height}`)
    await page.setViewportSize(viewport)
    await page.goto(`${target}${query}`, { waitUntil: 'domcontentloaded' })
    console.log(await page.evaluate(() => ({ size: [innerWidth, innerHeight], coarse: matchMedia('(pointer: coarse) and (hover: none)').matches, board: document.querySelector('.board-viewport')?.getAttribute('data-l12-mobile-landscape') })))
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForTimeout(160)
  }

  await load({ width: 844, height: 390 })
  const standard = await page.evaluate(() => ({
    railTrials: document.querySelectorAll('.mobile-extra-card').length,
    commanderTrials: [...document.querySelectorAll('.battlefield-half .trial-card')].filter(element => {
      const style = getComputedStyle(element)
      const rect = element.getBoundingClientRect()
      return style.display !== 'none' && rect.width > 0 && rect.height > 0
    }).length,
    handCounts: [...document.querySelectorAll('.mobile-hand-count')].map(element => {
      const rect = element.getBoundingClientRect()
      const number = element.querySelector('b')?.getBoundingClientRect()
      return Boolean(number && number.width >= 8 && number.height >= 10 && rect.left >= 0 && rect.right <= innerWidth)
    }),
    moraleTriggers: [...document.querySelectorAll('.mobile-morale-stack-trigger')].map(element => {
      const trigger = element.getBoundingClientRect()
      const stack = element.parentElement?.getBoundingClientRect()
      // The one-pixel border belongs to the stack; the trigger must own its
      // complete inner hit area rather than merely the individual circles.
      return Boolean(stack && trigger.left <= stack.left + 1.1 && trigger.right >= stack.right - 1.1
        && trigger.top <= stack.top + 1.1 && trigger.bottom >= stack.bottom - 1.1)
    }),
  }))
  assert.equal(standard.railTrials, 3, 'fixture must show all three trial cards in the disaster rail')
  assert.equal(standard.commanderTrials, 0, 'trial cards must not remain beside commanders on mobile')
  assert.deepEqual(standard.handCounts, [true, true], 'both hand counts must be visible')
  assert.deepEqual(standard.moraleTriggers, [true, true], 'both full morale stacks must open the picker')
  await page.screenshot({ path: path.join(output, '01-mobile-standard-844x390.png') })

  await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().click()
  const cardDrawer = page.locator('.mobile-card-inspector')
  if (!await cardDrawer.isVisible()) await page.getByRole('button', { name: '展开卡牌详情', exact: true }).click()
  await cardDrawer.waitFor()
  await page.waitForTimeout(250)
  const drawer = await page.evaluate(() => {
    const panel = document.querySelector('.mobile-card-inspector')
    const sharedDetail = panel?.querySelector('[data-card-detail-context="builder"]')
    const handle = document.querySelector('.left-rail > .mobile-card-inspector-handle')
    const panelRect = panel?.getBoundingClientRect()
    const handleRect = handle?.getBoundingClientRect()
    const style = panel && getComputedStyle(panel)
    return {
      opaque: style?.opacity === '1' && style?.backgroundColor === 'rgb(7, 12, 13)',
      independentWidth: Boolean(panelRect && handleRect && panelRect.width >= 250 && panelRect.width > handleRect.width * 2),
      sharedDetail: Boolean(sharedDetail),
      productSections: panel?.querySelectorAll('.archive-decks').length ?? -1,
      horizontalOverflow: Boolean(sharedDetail && sharedDetail.scrollWidth > sharedDetail.clientWidth + 1),
    }
  })
  assert.equal(drawer.opaque, true, 'card drawer must use an opaque surface')
  assert.equal(drawer.independentWidth, true, 'card drawer width must be independent of its rail handle')
  assert.equal(drawer.sharedDetail, true, 'card drawer must render the shared archive detail')
  assert.equal(drawer.productSections, 0, 'battle card drawer must hide catalog-only product/publication records')
  assert.equal(drawer.horizontalOverflow, false, 'shared card detail must not overflow the mobile drawer horizontally')
  await page.screenshot({ path: path.join(output, '02-card-drawer-844x390.png') })

  await load({ width: 568, height: 320 })
  const narrow = await page.evaluate(() => [...document.querySelectorAll('.mobile-hand-count')].map(element => {
    const rect = element.getBoundingClientRect()
    const number = element.querySelector('b')?.getBoundingClientRect()
    return Boolean(number && number.width >= 8 && number.height >= 10 && rect.left >= 0 && rect.right <= innerWidth)
  }))
  assert.deepEqual(narrow, [true, true], '568px landscape must keep both hand counts readable')
  await page.screenshot({ path: path.join(output, '03-narrow-stress-568x320.png') })

  await load({ width: 844, height: 390 })
  const moraleSummary = await page.evaluate(() => [...document.querySelectorAll('.resource-morale-summary')].map(summary => {
    const label = summary.querySelector('.resource-morale-label')
    const icon = label?.querySelector('img')
    const text = label?.querySelector('span')
    return label?.textContent?.trim() === '士气'
      && getComputedStyle(icon).display === 'none'
      && getComputedStyle(text).display !== 'none'
  }))
  assert.deepEqual(moraleSummary, [true, true], 'both morale summaries must show the 士气 text label, not an icon')
  await page.getByRole('button', { name: /士气/ }).first().click()
  await page.locator('.mobile-morale-overlay').waitFor()
  await page.screenshot({ path: path.join(output, '04-morale-panel-844x390.png') })

  await load({ width: 844, height: 390 })
  await page.getByRole('button', { name: '对局记录', exact: true }).click()
  await page.locator('.mobile-record-overlay').waitFor()
  await page.screenshot({ path: path.join(output, '05-match-log-panel-844x390.png') })

  await load({ width: 844, height: 390 })
  await page.locator('.resource-faction-action').last().click()
  await page.locator('.faction-effect-dialog').waitFor()
  await page.screenshot({ path: path.join(output, '06-faction-effect-panel-844x390.png') })

  await load({ width: 844, height: 390 }, '&slot=1')
  await page.locator('.mobile-morale-stack-trigger').last().click()
  await page.locator('.mobile-morale-overlay').waitFor()
  await page.screenshot({ path: path.join(output, '07-morale-payment-panel-844x390.png') })
  await page.getByRole('button', { name: '最小化', exact: true }).click()
  await page.screenshot({ path: path.join(output, '08-morale-payment-minimized-844x390.png') })

  console.log(JSON.stringify({ output, standard, narrow, drawer, moraleSummary }, null, 2))
} finally {
  await browser.close()
}
