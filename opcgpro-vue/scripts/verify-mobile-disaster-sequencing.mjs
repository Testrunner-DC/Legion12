import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5190/__l12_battle_preview__'
const output = process.env.L12_MOBILE_DISASTER_OUT || path.resolve('..', 'artifacts', 'batch-mobile-disaster-b2b-b')
const viewports = [{ width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 768 }]
const query = 'mobile=1&hand=10&trials=2&myTrials=1&disaster-chain=1'
// These shipped, card-id-specific round faces are controlled local decoder
// inputs for the presentation test. They do not claim that the full-resolution
// card-art manifest or CDN payload has been validated here.
const controlledDisasterArtwork = {
  'S01-DS01': '/assets/l12/special/round/S01-DS01.png',
  'S01-DS02': '/assets/l12/special/round/S01-DS02.png',
}
const controlledDisasterManifest = JSON.stringify({
  schemaVersion: 3,
  catalogVersion: 'mobile-disaster-sequencing-controlled-art',
  assetVersion: 'mobile-disaster-sequencing-controlled-art',
  basePath: '/',
  cards: Object.fromEntries(Object.entries(controlledDisasterArtwork).map(([cardId, url]) => [cardId, {
    cardId,
    contentHash: `controlled-local-${cardId}`,
    width: 720,
    height: 720,
    orientation: 'portrait',
    variants: { thumbWebp: url, boardWebp: url, detailWebp: url },
  }])),
})
fs.mkdirSync(output, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(8_000)
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = media => media.includes('pointer: coarse')
      ? ({ matches: true, media, addEventListener() {}, removeEventListener() {} }) : native(media)
  })
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (!['127.0.0.1', 'localhost'].includes(url.hostname)) return route.abort()
    if (url.pathname === '/card-assets/card-assets.manifest.json')
      return route.fulfill({ contentType: 'application/json', body: controlledDisasterManifest })
    return route.continue()
  })

  const load = async viewport => {
    await page.setViewportSize(viewport)
    await page.goto(`${target}?${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForFunction(() => Boolean(window.__disasterFixture))
  }
  const assertInsideViewport = async (selector, label) => {
    const rect = await page.locator(selector).evaluate(element => {
      const value = element.getBoundingClientRect()
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height }
    })
    assert.ok(rect.left >= -0.5 && rect.top >= -0.5 && rect.right <= (await page.evaluate(() => innerWidth)) + 0.5 && rect.bottom <= (await page.evaluate(() => innerHeight)) + 0.5,
      `${label} must stay inside the visual viewport: ${JSON.stringify(rect)}`)
  }
  const assertDisasterRail = async suffix => {
    const result = await page.locator('.left-disaster-row').evaluate(element => {
      const strip = element.querySelector('.session-disaster-strip')
      const buttons = [...element.querySelectorAll('.session-disaster-strip button')]
      const current = element.querySelector('.current-disaster-card')
      const copy = element.querySelector('.mobile-current-disaster-copy')
      const currentImage = current?.querySelector('img')
      const value = element.querySelector('.mobile-current-disaster-value')
      if (!strip || !current || !copy || !currentImage || !value) return null
      const stripRect = strip.getBoundingClientRect()
      const currentRect = current.getBoundingClientRect()
      const currentImageRect = currentImage.getBoundingClientRect()
      const copyRect = copy.getBoundingClientRect()
      const valueRect = value.getBoundingClientRect()
      const currentStyle = getComputedStyle(current)
      const currentImageStyle = getComputedStyle(currentImage)
      const copyStyle = getComputedStyle(copy)
      const valueStyle = getComputedStyle(value)
      return {
        stripRect: { left: stripRect.left, top: stripRect.top, right: stripRect.right, bottom: stripRect.bottom },
        stripOverflow: getComputedStyle(strip).overflow,
        buttons: buttons.map(button => {
          const rect = button.getBoundingClientRect()
          return {
            rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height },
            inside: rect.left >= stripRect.left - .5 && rect.right <= stripRect.right + .5 && rect.top >= stripRect.top - .5 && rect.bottom <= stripRect.bottom + .5,
            clipped: getComputedStyle(button).overflow === 'hidden',
            round: Math.abs(rect.width - rect.height) <= 1,
          }
        }),
        currentInside: currentRect.left >= 0 && currentRect.top >= 0 && currentRect.right <= innerWidth && currentRect.bottom <= innerHeight,
        currentVisible: currentRect.width > 20 && currentRect.height > 20 && currentStyle.display !== 'none' && currentStyle.visibility !== 'hidden',
        currentEnabled: !current.disabled,
        currentImageVisible: currentImageRect.width > 20 && currentImageRect.height > 20 && currentImageStyle.display !== 'none' && currentImageStyle.visibility !== 'hidden',
        currentAlt: currentImage.getAttribute('alt')?.trim(),
        copyHidden: copyRect.width === 0 && copyRect.height === 0 && copyStyle.display === 'none',
        valueVisible: valueRect.width > 20 && valueRect.height > 12 && valueStyle.display !== 'none' && valueStyle.visibility !== 'hidden',
        valueInside: valueRect.left >= 0 && valueRect.top >= 0 && valueRect.right <= innerWidth && valueRect.bottom <= innerHeight,
        valueLabel: value.getAttribute('aria-label'),
        valueText: value.textContent?.replace(/\s+/g, ' ').trim(),
      }
    })
    assert.ok(result, `disaster rail ${suffix} must exist`)
    assert.equal(result.stripOverflow, 'hidden', `disaster circles ${suffix} must be clipped by their rail`)
    assert.equal(result.buttons.length, 4, `disaster rail ${suffix} must render four circular slots`)
    if (!result.buttons.every(item => item.inside && item.clipped && item.round)) console.error(JSON.stringify({ suffix, result }, null, 2))
    assert.equal(result.buttons.every(item => item.inside && item.clipped && item.round), true, `disaster circles ${suffix} must remain round and inside the rail`)
    assert.equal(result.currentInside, true, `current disaster ${suffix} must stay in the viewport`)
    assert.equal(result.currentVisible, true, `current disaster card ${suffix} must be visible`)
    assert.equal(result.currentEnabled, true, `current disaster card ${suffix} must be reachable`)
    assert.equal(result.currentImageVisible, true, `current disaster card image ${suffix} must be readable`)
    assert.ok((result.currentAlt ?? '').length > 0, `current disaster card ${suffix} must retain an accessible name`)
    assert.equal(result.copyHidden, true, `redundant current disaster copy ${suffix} must stay hidden in the mobile layout`)
    assert.equal(result.valueVisible, true, `current disaster value ${suffix} must be readable`)
    assert.equal(result.valueInside, true, `current disaster value ${suffix} must stay in the viewport`)
    assert.equal(result.valueLabel, '当前天灾值', `current disaster value ${suffix} must retain its label`)
    assert.match(result.valueText ?? '', /天灾值\s*\d+/, `current disaster value ${suffix} must show its numeric value`)
  }

  for (const viewport of viewports) {
    const suffix = `${viewport.width}x${viewport.height}`

    await load(viewport)
    await assertDisasterRail(suffix)
    await page.screenshot({ path: path.join(output, `current-disaster-${suffix}.png`) })

    // No-trigger reveal: the authoritative disaster event must still flip from
    // the disaster back to the public face.
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    const noTriggerFlip = page.locator('.zone-card-movement .disaster-reveal-card')
    await noTriggerFlip.waitFor({ state: 'visible' })
    assert.equal(await noTriggerFlip.locator('.disaster-reveal-back').count(), 1, `no-trigger ${suffix} must include the disaster back`)
    assert.equal(await noTriggerFlip.locator('.disaster-reveal-front').count(), 1, `no-trigger ${suffix} must include the disaster face`)
    await assertInsideViewport('.zone-card-movement .disaster-reveal-card', `no-trigger reveal ${suffix}`)
    await page.screenshot({ path: path.join(output, `no-trigger-reveal-${suffix}.png`) })
    await noTriggerFlip.waitFor({ state: 'detached' })

    // Triggered reveal: the effect presentation must not cover the flip.  It
    // may begin only after the flip's presentation lane is free.
    await load(viewport)
    await page.evaluate(() => window.__disasterFixture.emitReveal(true))
    const triggeredFlip = page.locator('.zone-card-movement .disaster-reveal-card')
    await triggeredFlip.waitFor({ state: 'visible' })
    assert.equal(await page.locator('.public-reveal-animation').count(), 0, `triggered reveal ${suffix} effect must wait for the flip`)
    await page.screenshot({ path: path.join(output, `triggered-reveal-${suffix}.png`) })
    await triggeredFlip.waitFor({ state: 'detached' })
    const triggeredEffect = page.locator('.public-reveal-animation')
    await triggeredEffect.waitFor({ state: 'visible' })
    await assertInsideViewport('.public-reveal-animation', `triggered effect ${suffix}`)
    await page.screenshot({ path: path.join(output, `triggered-effect-${suffix}.png`) })

    // Existing modal: the reveal is queued and starts only after the modal is
    // gone.  The modal's actual buttons remain usable while it owns the screen.
    await load(viewport)
    await page.evaluate(() => window.__disasterFixture.openPrompt())
    const prompt = page.locator('.prompt-panel')
    await prompt.waitFor({ state: 'visible' })
    await assertInsideViewport('.prompt-panel', `pre-existing prompt ${suffix}`)
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    await page.waitForTimeout(650)
    assert.equal(await page.locator('.zone-card-movement').count(), 0, `queued reveal ${suffix} must not start below an existing prompt`)
    await page.screenshot({ path: path.join(output, `waiting-behind-prompt-${suffix}.png`) })
    await prompt.getByRole('button', { name: '不发动', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `prompt ${suffix} controls must remain usable`)
    await page.evaluate(() => window.__disasterFixture.closePrompt())
    await page.locator('.prompt-panel').waitFor({ state: 'detached' })
    const deferredFlip = page.locator('.zone-card-movement .disaster-reveal-card')
    await deferredFlip.waitFor({ state: 'visible' })
    await page.screenshot({ path: path.join(output, `deferred-after-prompt-${suffix}.png`) })
    await deferredFlip.waitFor({ state: 'detached' })

    // New obstruction: an already-running flip yields immediately and is
    // consumed.  Closing the obstruction must not replay it.
    await load(viewport)
    await page.evaluate(() => window.__disasterFixture.emitReveal(false))
    const interruptedFlip = page.locator('.zone-card-movement .disaster-reveal-card')
    await interruptedFlip.waitFor({ state: 'visible' })
    await page.evaluate(() => window.__disasterFixture.openPrompt())
    await page.locator('.prompt-panel').waitFor({ state: 'visible' })
    await interruptedFlip.waitFor({ state: 'detached' })
    await page.screenshot({ path: path.join(output, `interrupted-by-prompt-${suffix}.png`) })
    await page.evaluate(() => window.__disasterFixture.closePrompt())
    await page.locator('.prompt-panel').waitFor({ state: 'detached' })
    await page.waitForTimeout(900)
    assert.equal(await page.locator('.zone-card-movement .disaster-reveal-card').count(), 0, `interrupted reveal ${suffix} must not replay after the prompt closes`)
  }

  console.log(JSON.stringify({
    output,
    viewports,
    scenarios: ['current-disaster', 'no-trigger-reveal', 'triggered-reveal', 'deferred-behind-prompt', 'interrupted-no-replay'],
    status: 'passed',
  }, null, 2))
} finally {
  await browser.close()
}
