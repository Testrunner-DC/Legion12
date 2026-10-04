import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const source = path.join(root, 'src/l12/game/PromptOverlay.vue')
const sourceSha256 = createHash('sha256').update(fs.readFileSync(source)).digest('hex')
const baseUrl = process.env.L12_PREVIEW_URL || 'http://127.0.0.1:5317/__l12_battle_preview__'
const red = process.argv.includes('--red')
const artifactDir = process.env.L12_GESTURE_ARTIFACT_DIR || ''
if (artifactDir) fs.mkdirSync(artifactDir, { recursive: true })
const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless: true, executablePath: process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless: true, channel: 'msedge' })

async function swipe(cdp, from, to, cancel = false) {
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: from.x, y: from.y }] })
  for (let step = 1; step <= 8; step++) {
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchMove', touchPoints: [{ x: from.x + (to.x - from.x) * step / 8, y: from.y + (to.y - from.y) * step / 8 }],
    })
  }
  await cdp.send('Input.dispatchTouchEvent', { type: cancel ? 'touchCancel' : 'touchEnd', touchPoints: [] })
}

const results = []
const layouts = []
const layoutFailures = []
for (const viewport of (process.argv.includes('--quick') ? [{ width: 320, height: 568 }]
  : process.argv.includes('--landscape') ? [{ width: 844, height: 390 }]
    : [{ width: 320, height: 568 }, { width: 390, height: 844 }, { width: 844, height: 390 }])) {
  const context = await browser.newContext({ viewport, hasTouch: true, deviceScaleFactor: 1 })
  const page = await context.newPage()
  async function captureLayout(fixture) {
    const layout = await page.evaluate(() => {
      const panel = document.querySelector('.l12-prompt-overlay.mobile-safe-overlay .prompt-choice-panel')
      const metrics = element => {
        if (!element) return null
        const rect = element.getBoundingClientRect(), style = getComputedStyle(element)
        return { logical: { x: element.offsetLeft, y: element.offsetTop, width: element.offsetWidth, height: element.offsetHeight },
          physical: { x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) },
          font: style.fontSize, lineHeight: style.lineHeight, gridRows: style.gridTemplateRows,
          maxHeight: style.maxHeight, aspectRatio: style.aspectRatio,
          textOverflow: element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1 }
      }
      return {
        rotated: document.documentElement.dataset.l12Rotated === 'true',
        tokens: Object.fromEntries(['--l12-viewport-width', '--l12-viewport-height', '--l12-mobile-dialog-width', '--l12-mobile-dialog-height', '--l12-dialog-card-w', '--l12-dialog-card-h'].map(key => [key, getComputedStyle(panel).getPropertyValue(key).trim()])),
        panel: metrics(panel), title: metrics(panel?.querySelector('h2')),
        minimize: metrics(panel?.querySelector('.prompt-minimize')),
        body: metrics(panel?.querySelector('.prompt-choice-body')),
        footerButtons: [...(panel?.querySelectorAll('.prompt-action-footer button') ?? [])].map(metrics),
        textOptions: [...(panel?.querySelectorAll('.prompt-choices:not(.prompt-card-strip) > button') ?? [])].map(metrics),
        responseRows: [...(panel?.querySelectorAll('.response-target-row .response-target-select') ?? [])].map(metrics),
        cardOptions: [...(panel?.querySelectorAll('.prompt-choices.prompt-card-strip .prompt-card-candidate') ?? [])].slice(0, 6).map(card => ({
          card: metrics(card), image: metrics(card.querySelector('.l12-card-image')),
          name: metrics(card.querySelector('.prompt-card-candidate__name')),
        })),
        bodyCanScroll: Boolean(panel?.querySelector('.prompt-choice-body') && panel.querySelector('.prompt-choice-body').scrollHeight > panel.querySelector('.prompt-choice-body').clientHeight + 2),
      }
    })
    layouts.push({ viewport, fixture, ...layout })
    const issue = reason => layoutFailures.push(`${fixture} ${viewport.width}x${viewport.height}: ${reason}`)
    const overlap = (a, b) => Math.max(0, Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x))
      * Math.max(0, Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y))
    if (layout.minimize && overlap(layout.title.physical, layout.minimize.physical) > 1) issue('minimize overlaps title')
    if (layout.minimize && Math.min(layout.minimize.physical.width, layout.minimize.physical.height) < 44) issue('minimize physical hit target under 44px')
    if (layout.footerButtons.some(button => Math.min(button.physical.width, button.physical.height) < 44)) issue('footer physical hit target under 44px')
    if (layout.footerButtons.some(button => button.physical.x < -1 || button.physical.y < -1 || button.physical.x + button.physical.width > viewport.width + 1 || button.physical.y + button.physical.height > viewport.height + 1)) issue('footer is not reachable inside viewport')
    if (layout.textOptions.some(button => Math.min(button.physical.width, button.physical.height) < 44)) issue('text option physical hit target under 44px')
    if (layout.responseRows.some(button => Math.min(button.physical.width, button.physical.height) < 44)) issue('response physical hit target under 44px')
    if (layout.textOptions.length > 1 && new Set(layout.textOptions.map(button => button.logical.width)).size > 1) issue('vertical text option widths differ')
    for (const row of new Set(layout.textOptions.map(button => button.logical.y))) {
      if (new Set(layout.textOptions.filter(button => button.logical.y === row).map(button => button.logical.height)).size > 1) issue('horizontal text option heights differ')
    }
    if (layout.responseRows.length > 1 && new Set(layout.responseRows.map(button => button.logical.width)).size > 1) issue('vertical response widths differ')
    if (layout.cardOptions.length > 1 && new Set(layout.cardOptions.map(item => item.card.logical.height)).size > 1) issue('horizontal card heights differ')
    if (layout.cardOptions.some(item => item.image.logical.width < item.card.logical.width - 14 || item.name.logical.width < item.card.logical.width - 14)) issue('card art/name does not fill its peer width')
    if (layout.cardOptions.some(item => item.name.textOverflow)) issue('card name clipped')
    if (artifactDir) await page.screenshot({ path: path.join(artifactDir, `layout-${fixture}-${viewport.width}x${viewport.height}.png`) })
  }
  await page.goto(`${baseUrl}?canvas=1&mobile=1&disaster-choice=1&choice-count=20`, { waitUntil: 'networkidle' })
  await page.locator('.l12-prompt-overlay.disaster-choice .prompt-card-candidate').first().waitFor()
  await captureLayout('disaster')
  await page.evaluate(() => {
    const body = document.querySelector('.l12-prompt-overlay .prompt-choice-body')
    const spacer = document.createElement('div')
    spacer.id = 'gesture-spacer'
    spacer.style.cssText = 'height:500px;min-height:500px;flex:0 0 500px'
    body.append(spacer)
    document.querySelector('.prompt-choices.prompt-card-strip').style.scrollBehavior = 'auto'
  })
  const cdp = await context.newCDPSession(page)
  const rotated = await page.evaluate(() => document.documentElement.dataset.l12Rotated === 'true')
  const strip = page.locator('.l12-prompt-overlay.disaster-choice .prompt-choices.prompt-card-strip')
  if (artifactDir) await page.screenshot({ path: path.join(artifactDir, `disaster-${red ? 'red' : 'green'}-${viewport.width}x${viewport.height}.png`) })
  async function runGesture(origin, locator, direction, cancel = false) {
    await page.evaluate(() => {
      document.querySelector('.prompt-choice-body').scrollTop = 0
      document.querySelector('.prompt-choices.prompt-card-strip').scrollLeft = 80
    })
    await locator.scrollIntoViewIfNeeded()
    const box = await locator.boundingBox()
    if (!box) throw new Error(`Missing ${origin} at ${viewport.width}x${viewport.height}`)
    const from = { x: Math.round(box.x + box.width / 2), y: Math.round(box.y + box.height / 2) }
    const movement = direction === 'vertical'
      ? rotated ? { x: 66, y: 0 } : { x: 0, y: -66 }
      : direction === 'horizontal'
        ? rotated ? { x: 0, y: -66 } : { x: -66, y: 0 }
        : rotated ? { x: 66, y: -42 } : { x: -42, y: -66 }
    const to = { x: from.x + movement.x, y: from.y + movement.y }
    const before = await page.evaluate(() => ({
      body: document.querySelector('.prompt-choice-body').scrollTop,
      strip: document.querySelector('.prompt-choices.prompt-card-strip').scrollLeft,
      history: document.querySelector('.disaster-preparation-history > section > div')?.scrollLeft ?? null,
      stripTouchAction: getComputedStyle(document.querySelector('.prompt-choices.prompt-card-strip')).touchAction,
      selected: document.querySelectorAll('.prompt-card-candidate.selected').length,
    }))
    await swipe(cdp, from, to, cancel)
    await page.waitForTimeout(100)
    const after = await page.evaluate(() => ({
      body: document.querySelector('.prompt-choice-body').scrollTop,
      strip: document.querySelector('.prompt-choices.prompt-card-strip').scrollLeft,
      history: document.querySelector('.disaster-preparation-history > section > div')?.scrollLeft ?? null,
      selected: document.querySelectorAll('.prompt-card-candidate.selected').length,
    }))
    results.push({ viewport, rotated, origin, direction, cancel, before, after,
      bodyDelta: after.body - before.body, stripDelta: after.strip - before.strip,
      historyDelta: before.history === null ? null : after.history - before.history })
  }
  await runGesture('card-image', strip.locator('.prompt-card-candidate .l12-card-image').nth(2), 'vertical')
  await runGesture('card-name', strip.locator('.prompt-card-candidate__name').nth(2), 'vertical')
  await runGesture('card-image', strip.locator('.prompt-card-candidate .l12-card-image').nth(2), 'horizontal')
  await runGesture('card-image', strip.locator('.prompt-card-candidate .l12-card-image').nth(2), 'diagonal')
  await runGesture('card-image', strip.locator('.prompt-card-candidate .l12-card-image').nth(2), 'horizontal', true)
  await strip.locator('.prompt-card-candidate').first().tap()
  results.push({ viewport, rotated, origin: 'tap-after-drag-cancel', selected: await strip.locator('.prompt-card-candidate.selected').count() })
  await strip.locator('.prompt-card-candidate').first().tap()
  await runGesture('body-blank', page.locator('#gesture-spacer'), 'vertical')
  await page.evaluate(() => {
    const state = window.__l12State
    const prompt = state.game.prompts[0]
    state.game.phase = 'DisasterPreparation'
    state.game.bannedDisasters = Array.from({ length: 10 }, (_, index) => ({
      instanceId: `history-${index}`, cardId: prompt.data[`disaster-choice-${index}:cardId`],
      name: prompt.data[`disaster-choice-${index}:name`], cardType: 'disaster', hidden: false,
    }))
  })
  const history = page.locator('.disaster-preparation-history > section > div').first()
  await history.locator('button').first().waitFor()
  await page.evaluate(() => { document.querySelector('.disaster-preparation-history > section > div').scrollLeft = 80 })
  await runGesture('history-card', history.locator('button').nth(2), 'vertical')
  await runGesture('history-card', history.locator('button').nth(2), 'horizontal')
  let immediateSelected = 0
  if (!rotated) {
    await strip.locator('.prompt-card-candidate').first().tap()
    immediateSelected = await strip.locator('.prompt-card-candidate.selected').count()
    results.push({ viewport, rotated, origin: 'tap-during-native-momentum', selected: immediateSelected })
  }
  // Native landscape scrolling can retain momentum after touchend; wait for
  // it to settle before checking that the following deliberate tap selects.
  await page.waitForTimeout(500)
  if (immediateSelected) await strip.locator('.prompt-card-candidate').first().tap()
  await strip.locator('.prompt-card-candidate').first().tap()
  results.push({ viewport, rotated, origin: 'tap-after-history', selected: await strip.locator('.prompt-card-candidate.selected').count() })
  for (const fixture of ['option-fixture', 'response-fixture']) {
    await page.goto(`${baseUrl}?canvas=1&mobile=1&${fixture}=1`, { waitUntil: 'networkidle' })
    await page.locator('.l12-prompt-overlay.mobile-safe-overlay .prompt-choice-panel').waitFor()
    await captureLayout(fixture)
  }
  await page.goto(`${baseUrl}?canvas=1&mobile=1&card-choice=1&choice-count=6`, { waitUntil: 'networkidle' })
  const lifecycleCard = page.locator('.l12-prompt-overlay .prompt-choices.prompt-card-strip .prompt-card-candidate').first()
  await lifecycleCard.tap()
  const selectedBeforeMinimize = await page.locator('.prompt-card-candidate.selected').count()
  await page.locator('.l12-prompt-overlay .prompt-minimize').click()
  const minimized = await page.locator('.l12-prompt-overlay.minimized').count()
  await page.locator('.l12-prompt-overlay .prompt-minimized-bar button').click()
  const selectedAfterExpand = await page.locator('.prompt-card-candidate.selected').count()
  await page.evaluate(() => { window.__l12State.game.prompts[0].promptId = 'fixture-replacement' })
  await page.waitForTimeout(50)
  const selectedAfterReplacement = await page.locator('.prompt-card-candidate.selected').count()
  await lifecycleCard.tap()
  const selectedNewPrompt = await page.locator('.prompt-card-candidate.selected').count()
  results.push({ viewport, rotated, origin: 'minimize-and-prompt-replacement', selectedBeforeMinimize, minimized,
    selectedAfterExpand, selectedAfterReplacement, selectedNewPrompt })
  await page.goto(`${baseUrl}?canvas=1&mobile=1&order-direction-fixture=all-top-bottom`, { waitUntil: 'networkidle' })
  const ordered = page.locator('.l12-prompt-overlay .all-placement-row .prompt-card-candidate')
  const orderBefore = await ordered.evaluateAll(nodes => nodes.map(node => node.getAttribute('aria-label')))
  await page.evaluate(() => { window.__dragTrace = []; for (const type of ['dragstart', 'dragenter', 'dragover', 'drop', 'dragend']) document.addEventListener(type, event => window.__dragTrace.push({ type, target: event.target?.closest?.('.prompt-card-candidate')?.getAttribute('aria-label') || event.target?.className }), true) })
  await ordered.nth(2).dragTo(ordered.first())
  const orderAfter = await ordered.evaluateAll(nodes => nodes.map(node => node.getAttribute('aria-label')))
  results.push({ viewport, rotated, origin: 'placement-drag-reorder', before: orderBefore, after: orderAfter,
    events: await page.evaluate(() => window.__dragTrace) })
  await page.goto(`${baseUrl}?canvas=1&mobile=1&order-direction-fixture=split-top-bottom`, { waitUntil: 'networkidle' })
  await page.locator('.placement-candidates .prompt-card-candidate').first().click()
  await page.locator('.placement-buttons button').first().click()
  results.push({ viewport, rotated, origin: 'placement-assign', topCount: await page.locator('.placement-destination.top .prompt-card-candidate').count() })
  await context.close()
}
const desktopContext = await browser.newContext({ viewport: { width: 1280, height: 800 } })
const desktopPage = await desktopContext.newPage()
await desktopPage.goto(`${baseUrl}?canvas=1&card-choice=1&choice-count=6`, { waitUntil: 'networkidle' })
await desktopPage.locator('.l12-prompt-overlay .prompt-card-candidate').first().waitFor()
const desktopIsolation = await desktopPage.evaluate(() => ({
  mobile: document.documentElement.dataset.l12Mobile,
  mobileOverlay: document.querySelector('.l12-prompt-overlay.mobile-safe-overlay') !== null,
  cardWidth: document.querySelector('.l12-prompt-overlay .prompt-card-candidate').offsetWidth,
  cardHeight: document.querySelector('.l12-prompt-overlay .prompt-card-candidate').offsetHeight,
}))
await desktopContext.close()
await browser.close()
const report = { sourceSha256, mode: red ? 'red' : 'green', results, layouts, layoutFailures, desktopIsolation }
console.log(JSON.stringify(report, null, 2))
if (artifactDir) fs.writeFileSync(path.join(artifactDir, `gesture-${red ? 'red' : 'green'}-${sourceSha256.slice(0, 12)}.json`), JSON.stringify(report, null, 2))
if (!red && (layoutFailures.length || results.some(item => item.direction === 'vertical' && (Math.abs(item.bodyDelta) < 12 || item.after.selected !== item.before.selected)
  || item.direction === 'horizontal' && !item.cancel && (Math.abs(item.origin === 'history-card' ? item.historyDelta : item.stripDelta) < 12 || Math.abs(item.bodyDelta) > 4 || item.after.selected !== item.before.selected)
  || item.direction === 'diagonal' && (Math.abs(item.bodyDelta) < 12 || Math.abs(item.stripDelta) > 4 || item.after.selected !== item.before.selected)
  || item.origin === 'tap-after-drag-cancel' && item.selected !== 1
  || item.origin === 'tap-after-history' && item.selected !== 1
  || item.origin === 'tap-during-native-momentum' && item.selected !== 0
  || item.origin === 'minimize-and-prompt-replacement' && (item.selectedBeforeMinimize !== 1 || item.minimized !== 1 || item.selectedAfterExpand !== 1 || item.selectedAfterReplacement !== 0 || item.selectedNewPrompt !== 1)
  || item.origin === 'placement-drag-reorder' && item.before.join('|') === item.after.join('|')
  || item.origin === 'placement-assign' && item.topCount !== 1)
  || desktopIsolation.mobile === 'true' || desktopIsolation.mobileOverlay || desktopIsolation.cardWidth !== 116 || desktopIsolation.cardHeight !== 224)) process.exitCode = 1
