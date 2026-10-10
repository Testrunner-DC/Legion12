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
  const assertSafeDialog = async (selector, label) => {
    const result = await page.locator(selector).evaluate(element => {
      const r = element.getBoundingClientRect()
      const controls = [...element.querySelectorAll('button')].map(button => {
        const b = button.getBoundingClientRect()
        return b.width > 0 && b.height > 0 && b.left >= 0 && b.right <= innerWidth && b.top >= 0 && b.bottom <= innerHeight
      })
      return { inside: r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight, controls: controls.every(Boolean) }
    })
    assert.equal(result.inside, true, `${label} must stay inside the visual viewport`)
    assert.equal(result.controls, true, `${label} controls must remain clickable`)
  }
  const openAbilityDialog = async (selectedStatePath) => {
    await page.locator('.l12-player-mat.side-my .formation-slot .card-tile').first().click()
    const mobileActivate = page.locator('.mobile-action-dock').getByRole('button', { name: '发动', exact: true })
    const fieldActivate = page.locator('.l12-player-mat.side-my .field-actions').getByRole('button', { name: '发动', exact: true })
    const activate = await mobileActivate.count() ? mobileActivate : fieldActivate
    await activate.waitFor()
    const dock = await page.evaluate(() => {
      const action = [...document.querySelectorAll('button')].find(button => button.textContent?.trim() === '发动' && button.closest('.mobile-action-dock, .field-actions'))
      const endTurn = [...document.querySelectorAll('button')].find(button => button.textContent?.trim() === '结束回合')
      if (!action || !endTurn) return null
      const a = action.getBoundingClientRect()
      const e = endTurn.getBoundingClientRect()
      return {
        above: a.bottom <= e.top + 1,
        aligned: Math.min(a.right, e.right) - Math.max(a.left, e.left) > 0,
        action: { left: a.left, right: a.right, top: a.top, bottom: a.bottom },
        endTurn: { left: e.left, right: e.right, top: e.top, bottom: e.bottom },
      }
    })
    assert.ok(dock, 'card action dock and end-turn control must both exist')
    assert.equal(dock.above, true, 'card actions must be vertically above the end-turn control')
    assert.equal(dock.aligned, true, 'card actions must share the end-turn control column')
    if (selectedStatePath) await page.screenshot({ path: selectedStatePath })
    await activate.click()
    await page.locator('.faction-effect-dialog').waitFor()
  }
  const load = async (viewport, query = '') => {
    console.log(`Capturing ${viewport.width}x${viewport.height}`)
    await page.setViewportSize(viewport)
    await page.goto(`${target}${query}`, { waitUntil: 'domcontentloaded' })
    console.log(await page.evaluate(() => ({ size: [innerWidth, innerHeight], coarse: matchMedia('(pointer: coarse) and (hover: none)').matches, board: document.querySelector('.board-viewport')?.getAttribute('data-l12-mobile-landscape') })))
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForTimeout(160)
  }

  await load({ width: 844, height: 390 }, '&modalFixture=1')
  const standard = await page.evaluate(() => ({
    railTrials: document.querySelectorAll('.mobile-extra-card').length,
    commanderTrials: [...document.querySelectorAll('.battlefield-half .trial-card')].filter(element => {
      const style = getComputedStyle(element)
      const rect = element.getBoundingClientRect()
      return style.display !== 'none' && rect.width > 0 && rect.height > 0
    }).length,
    handCounts: (() => {
      const elements = [...document.querySelectorAll('.battlefield-half .mobile-hand-count')]
      const labels = elements.map(element => element.getAttribute('aria-label') ?? '')
      return document.querySelectorAll('.mobile-enemy-hand-count').length === 0
        && elements.length === 2
        && labels.filter(label => /^对手手牌 \d+ 张$/.test(label)).length === 1
        && labels.filter(label => /^我方手牌 \d+ 张$/.test(label)).length === 1
        && elements.every(element => { const rect = element.getBoundingClientRect(); return rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight && element.scrollWidth <= element.clientWidth + 1 })
    })(),
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
  assert.equal(standard.handCounts, true, 'opponent hand count must be visible')
  assert.deepEqual(standard.moraleTriggers, [true, true], 'both full morale stacks must open the picker')
  await page.screenshot({ path: path.join(output, '01-mobile-standard-844x390.png') })

  await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().click()
  const cardDrawer = page.locator('.mobile-card-inspector')
  assert.equal(await cardDrawer.isVisible(), false, 'a mobile card tap must not auto-open the detail drawer')
  await page.getByRole('button', { name: '展开卡牌详情', exact: true }).click()
  await cardDrawer.waitFor()
  await page.waitForTimeout(250)
  const drawer = await page.evaluate(() => {
    const panel = document.querySelector('.mobile-card-inspector')
    const sharedDetail = panel?.querySelector('[data-card-detail-context="builder"]')
    const handle = document.querySelector('.mobile-card-inspector-handle-global, .left-rail > .mobile-card-inspector-handle')
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
  const narrow = await page.evaluate(() => [...document.querySelectorAll('.battlefield-half .mobile-hand-count')].every(element => { const rect = element.getBoundingClientRect(); return rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight && element.scrollWidth <= element.clientWidth + 1 }))
  assert.equal(narrow, true, '568px landscape must keep both PlayerMat hand counts readable')
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

  await load({ width: 844, height: 390 }, '&modalFixture=1')
  await page.locator('.resource-faction-action').last().click()
  await page.locator('.faction-effect-dialog').waitFor()
  await assertSafeDialog('.faction-effect-dialog', 'faction dialog')
  await page.screenshot({ path: path.join(output, '06-faction-effect-panel-844x390.png') })
  await page.getByRole('button', { name: '最小化弹框' }).click()
  await page.locator('.faction-minimized-bar button').click()
  await page.getByRole('button', { name: '关闭' }).click()

  await openAbilityDialog(path.join(output, '11a-card-action-dock-844x390.png'))
  await assertSafeDialog('.faction-effect-dialog', 'ability card dialog')
  await page.screenshot({ path: path.join(output, '11-ability-card-panel-844x390.png') })
  await page.getByRole('button', { name: '关闭' }).click()

  await page.locator('.mini-master').last().click()
  await page.locator('.master-dialog').waitFor()
  await assertSafeDialog('.master-dialog', 'master dialog')
  await page.screenshot({ path: path.join(output, '09-master-panel-844x390.png') })
  await page.getByRole('button', { name: '关闭' }).click()

  await page.locator('.mat-piles .graveyard').last().click()
  await page.locator('.graveyard-window').waitFor()
  await assertSafeDialog('.graveyard-window', 'graveyard dialog')
  await page.screenshot({ path: path.join(output, '10-graveyard-panel-844x390.png') })
  const graveyardCards = await page.locator('.graveyard-card-entry').count()
  console.log({ graveyardCards, graveyardHeading: await page.locator('.graveyard-window h3').textContent() })
  assert.equal(graveyardCards, 8, 'graveyard fixture must show eight cards')

  await load({ width: 844, height: 390 }, '&slot=1')
  await page.locator('.mobile-morale-stack-trigger').last().click()
  await page.locator('.mobile-morale-overlay').waitFor()
  await page.screenshot({ path: path.join(output, '07-morale-payment-panel-844x390.png') })
  await page.locator('.mobile-morale-overlay').getByRole('button', { name: '最小化', exact: true }).click()
  await page.screenshot({ path: path.join(output, '08-morale-payment-minimized-844x390.png') })

  for (const viewport of [{ width: 667, height: 375 }, { width: 932, height: 430 }]) {
    const suffix = `${viewport.width}x${viewport.height}`
    await load(viewport, '&modalFixture=1')
    await page.locator('.resource-faction-action').last().click()
    await page.locator('.faction-effect-dialog').waitFor()
    await assertSafeDialog('.faction-effect-dialog', `faction dialog ${suffix}`)
    await page.screenshot({ path: path.join(output, `12-faction-effect-panel-${suffix}.png`) })
    await page.getByRole('button', { name: '关闭' }).click()

    await openAbilityDialog(path.join(output, `13a-card-action-dock-${suffix}.png`))
    await assertSafeDialog('.faction-effect-dialog', `ability card dialog ${suffix}`)
    await page.screenshot({ path: path.join(output, `13-ability-card-panel-${suffix}.png`) })
    await page.getByRole('button', { name: '关闭' }).click()

    await load(viewport, '&slot=1')
    await page.locator('.mobile-morale-stack-trigger').last().click()
    await page.locator('.mobile-morale-overlay').waitFor()
    await assertSafeDialog('.mobile-morale-overlay', `morale payment dialog ${suffix}`)
    await page.screenshot({ path: path.join(output, `14-morale-payment-panel-${suffix}.png`) })
    await page.locator('.mobile-morale-overlay').getByRole('button', { name: '最小化', exact: true }).click()
  }

  console.log(JSON.stringify({ output, standard, narrow, drawer, moraleSummary }, null, 2))
} finally {
  await browser.close()
}
