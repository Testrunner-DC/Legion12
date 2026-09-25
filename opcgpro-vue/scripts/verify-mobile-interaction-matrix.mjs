import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5186/__l12_battle_preview__'
const output = process.env.L12_MOBILE_INTERACTION_OUT || path.resolve('..', 'artifacts', 'mobile-interaction-matrix')
const viewports = [{ width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 768 }]
const baseQuery = 'mobile=1&canvas=1&hand=12&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1'
fs.mkdirSync(output, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(8_000)
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse')
      ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} }) : native(query)
  })
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  const load = async (viewport, query) => {
    await page.setViewportSize(viewport)
    await page.goto(`${target}?${baseQuery}&${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
  }
  const assertSafePanel = async (selector, label) => {
    const result = await page.locator(selector).evaluate(element => {
      const rect = element.getBoundingClientRect()
      const controls = [...element.querySelectorAll('button')].filter(button => {
        const style = getComputedStyle(button), r = button.getBoundingClientRect()
        return style.visibility !== 'hidden' && style.display !== 'none' && r.width > 0 && r.height > 0
      }).map(button => {
        const r = button.getBoundingClientRect()
        return r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight
      })
      return {
        inside: rect.left >= 0 && rect.right <= innerWidth && rect.top >= 0 && rect.bottom <= innerHeight,
        controlsInside: controls.every(Boolean),
        horizontalOverflow: element.scrollWidth > element.clientWidth + 1,
      }
    })
    assert.equal(result.inside, true, `${label} must stay inside the viewport`)
    assert.equal(result.controlsInside, true, `${label} controls must stay clickable`)
    assert.equal(result.horizontalOverflow, false, `${label} must not overflow horizontally`)
  }
  const assertHorizontalDecisionBar = async (panel, firstName, secondName, label) => {
    const geometry = await panel.evaluate((element, names) => {
      const buttons = [...element.querySelectorAll('button')]
      const firstButton = buttons.find(button => button.textContent?.trim() === names[0])
      const secondButton = buttons.find(button => button.textContent?.trim() === names[1])
      if (!firstButton || !secondButton) return null
      const panelRect = element.getBoundingClientRect()
      const firstRect = firstButton.getBoundingClientRect()
      const secondRect = secondButton.getBoundingClientRect()
      return {
        panelHeight: panelRect.height,
        centerDelta: Math.abs((firstRect.top + firstRect.height / 2) - (secondRect.top + secondRect.height / 2)),
        ordered: firstRect.right <= secondRect.left,
      }
    }, [firstName, secondName])
    assert.ok(geometry, `${label} must contain both decision buttons`)
    assert.ok(geometry.centerDelta <= 2, `${label} buttons must share one horizontal row`)
    assert.equal(geometry.ordered, true, `${label} buttons must be ordered left-to-right`)
    assert.ok(geometry.panelHeight <= 46, `${label} must stay within 46px; got ${geometry.panelHeight}`)
  }

  for (const viewport of viewports) {
    const suffix = `${viewport.width}x${viewport.height}`

    await load(viewport, 'effect=cost2')
    const effect = page.locator('.prompt-panel')
    await effect.waitFor()
    await assertSafePanel('.prompt-panel', `effect prompt ${suffix}`)
    for (const label of ['消耗1士气', '弃置1张手牌', '不发动'])
      assert.equal(await effect.getByRole('button', { name: label, exact: true }).count(), 1, `effect prompt ${suffix} must show ${label}`)
    await page.screenshot({ path: path.join(output, `effect-${suffix}.png`) })
    await effect.getByRole('button', { name: '不发动', exact: true }).click()
    const effectConfirm = effect.getByRole('button', { name: '确认选择', exact: true })
    assert.equal(await effectConfirm.isEnabled(), true, `effect prompt ${suffix} confirm must enable after decline selection`)
    await effectConfirm.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `effect prompt ${suffix} decline must be clickable`)

    await load(viewport, 'card-choice=1')
    const choices = page.locator('.prompt-panel')
    await choices.waitFor()
    await assertSafePanel('.prompt-panel', `card choice ${suffix}`)
    const candidates = choices.locator('.prompt-card-candidate')
    assert.equal(await candidates.count(), 6, `card choice ${suffix} must render six candidates`)
    await page.screenshot({ path: path.join(output, `card-choice-${suffix}.png`) })
    await candidates.first().click()
    const confirm = choices.getByRole('button', { name: '确认选择', exact: true })
    assert.equal(await confirm.isEnabled(), true, `card choice ${suffix} confirm must enable after selection`)
    await confirm.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `card choice ${suffix} confirm must be clickable`)

    await load(viewport, 'support=1')
    const supportPanel = page.locator('.combat-resolution-panel')
    await supportPanel.waitFor()
    assert.equal(await page.locator('.formation-slot.combat-target').count(), 1, `support ${suffix} must highlight only the attacked legion`)
    assert.equal(await page.locator('.formation-slot.response-target').count(), 0, `support ${suffix} must not mark unrelated or empty slots as response targets`)
    assert.equal(await page.locator('.formation-slot.available').count(), 0, `support ${suffix} must not retain ordinary placement destinations during combat`)
    await assertSafePanel('.combat-resolution-panel', `support controls ${suffix}`)
    assert.equal(await supportPanel.locator('p').count(), 0, `support controls ${suffix} must not include rule explanations`)
    assert.equal(await supportPanel.getByRole('button', { name: '确认支援', exact: true }).count(), 1, `support controls ${suffix} must show confirm support`)
    assert.equal(await supportPanel.getByRole('button', { name: '不支援', exact: true }).count(), 1, `support controls ${suffix} must show decline support`)
    await assertHorizontalDecisionBar(supportPanel, '确认支援', '不支援', `support controls ${suffix}`)
    await page.screenshot({ path: path.join(output, `support-initial-${suffix}.png`) })
    await page.locator('.l12-player-mat.side-my .formation-slot').nth(3).click()
    const confirmSupport = supportPanel.getByRole('button', { name: '确认支援', exact: true })
    assert.equal(await confirmSupport.isEnabled(), true, `support controls ${suffix} must enable after a legal support is selected`)
    await page.screenshot({ path: path.join(output, `support-${suffix}.png`) })
    await confirmSupport.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `support controls ${suffix} confirm must be clickable`)

    await load(viewport, 'support=1')
    const declineSupport = page.locator('.combat-resolution-panel').getByRole('button', { name: '不支援', exact: true })
    await declineSupport.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `support controls ${suffix} decline must be clickable`)

    await load(viewport, 'defense=1')
    const defensePanel = page.locator('.combat-resolution-panel')
    await defensePanel.waitFor()
    assert.equal(await page.locator('.mini-master.combat-target').count(), 1, `defense ${suffix} must highlight only the attacked master`)
    assert.equal(await page.locator('.formation-slot.combat-target').count(), 0, `defense ${suffix} must not mark any formation slot as the attacked master`)
    assert.equal(await page.locator('.formation-slot.response-target').count(), 0, `defense ${suffix} must not mark unrelated or empty slots as response targets`)
    assert.equal(await page.locator('.formation-slot.available').count(), 0, `defense ${suffix} must not retain ordinary placement destinations during combat`)
    await assertSafePanel('.combat-resolution-panel', `defense controls ${suffix}`)
    assert.equal(await defensePanel.locator('p').count(), 0, `defense controls ${suffix} must not include rule explanations`)
    assert.equal(await defensePanel.getByRole('button', { name: '确认抵挡', exact: true }).count(), 1, `defense controls ${suffix} must show confirm defense`)
    assert.equal(await defensePanel.getByRole('button', { name: '不抵挡', exact: true }).count(), 1, `defense controls ${suffix} must show decline defense`)
    await assertHorizontalDecisionBar(defensePanel, '确认抵挡', '不抵挡', `defense controls ${suffix}`)
    await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().click()
    const confirmDefense = defensePanel.getByRole('button', { name: '确认抵挡', exact: true })
    assert.equal(await confirmDefense.isEnabled(), true, `defense controls ${suffix} must enable after a legal hand legion is selected`)
    await page.screenshot({ path: path.join(output, `defense-${suffix}.png`) })
    await confirmDefense.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `defense controls ${suffix} confirm must be clickable`)

    await load(viewport, 'defense=1')
    const declineDefense = page.locator('.combat-resolution-panel').getByRole('button', { name: '不抵挡', exact: true })
    await declineDefense.click()
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `defense controls ${suffix} decline must be clickable`)
  }

  console.log(JSON.stringify({ output, viewports, scenarios: ['effect', 'card-choice', 'support', 'defense'], status: 'passed' }, null, 2))
} finally {
  await browser.close()
}
