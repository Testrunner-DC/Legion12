import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5186/__l12_battle_preview__'
const output = process.env.L12_MOBILE_INTERACTION_OUT || path.resolve('..', 'artifacts', 'mobile-interaction-matrix')
const viewports = [
  { width: 390, height: 844 }, { width: 320, height: 568 },
  { width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 768 },
  { width: 844, height: 390, safeInsets: { top: 0, right: 0, bottom: 21, left: 59 } },
  { width: 568, height: 320, safeInsets: { top: 0, right: 44, bottom: 21, left: 44 } },
]
const baseQuery = 'mobile=1&canvas=1&hand=12&trials=2&myTrials=1&fieldIndicators=1&rankedClock=1'
const scrollProofs = []
fs.mkdirSync(output, { recursive: true })

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const context = await browser.newContext({ hasTouch: true })
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  let nativeSafeAreaOverride = true
  try { await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets: { top: 0, right: 0, bottom: 0, left: 0 } }) }
  catch (error) {
    if (!String(error).includes('setSafeAreaInsetsOverride')) throw error
    nativeSafeAreaOverride = false
    console.warn('Native safe-area override unavailable; safe-area profiles remain unverified by this runner.')
  }
  page.setDefaultTimeout(8_000)
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse')
      ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} }) : native(query)
  })
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  const load = async (viewport, query) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    if (nativeSafeAreaOverride) await cdp.send('Emulation.setSafeAreaInsetsOverride', { insets: viewport.safeInsets ?? { top: 0, right: 0, bottom: 0, left: 0 } })
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
  const initialButtonGeometry = locator => locator.evaluate(button => {
    const rect = button.getBoundingClientRect()
    const clip = { left: Math.max(0, rect.left), right: Math.min(innerWidth, rect.right), top: Math.max(0, rect.top), bottom: Math.min(innerHeight, rect.bottom) }
    for (let ancestor = button.parentElement; ancestor; ancestor = ancestor.parentElement) {
      const style = getComputedStyle(ancestor)
      if (!/(auto|scroll|hidden|clip)/.test(`${style.overflow} ${style.overflowX} ${style.overflowY}`)) continue
      const bounds = ancestor.getBoundingClientRect()
      clip.left = Math.max(clip.left, bounds.left); clip.right = Math.min(clip.right, bounds.right)
      clip.top = Math.max(clip.top, bounds.top); clip.bottom = Math.min(clip.bottom, bounds.bottom)
    }
    const width = Math.max(0, clip.right - clip.left), height = Math.max(0, clip.bottom - clip.top)
    const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2
    const hit = document.elementFromPoint(x, y)
    return { x, y, disabled: button.disabled, visibleRatio: rect.width * rect.height ? width * height / (rect.width * rect.height) : 0, hit: hit === button || button.contains(hit) }
  })
  const nativeDecisionTap = async (locator, label) => {
    const initial = await initialButtonGeometry(locator)
    assert.ok(initial.visibleRatio >= .999, `${label} must be fully visible before any automatic scrolling: ${JSON.stringify(initial)}`)
    assert.equal(initial.hit, true, `${label} initial center must belong to the actual button`)
    // locator.click/tap may programmatically scroll overflow:hidden ancestors,
    // concealing an initial crop. Coordinate touch must use the observed point.
    await page.touchscreen.tap(initial.x, initial.y)
  }
  const nativeContextReveal = async (locator, label) => {
    const before = await initialButtonGeometry(locator)
    const contextLane = page.locator('.mobile-battle-dock__context')
    const start = await contextLane.evaluate(element => element.scrollTop)
    const wheelSteps = []
    for (let attempt = 0; attempt < 8; attempt++) {
      const current = await initialButtonGeometry(locator)
      if (current.visibleRatio >= .999 && current.hit) break
      const box = await contextLane.boundingBox()
      const rotated = await page.evaluate(() => document.documentElement.dataset.l12Rotated === 'true')
      const offset = rotated ? box.x + box.width / 2 - current.x : current.y - (box.y + box.height / 2)
      const delta = Math.sign(offset || 1) * Math.min(80, Math.max(24, Math.abs(offset)))
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
      await page.mouse.wheel(0, delta)
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
      wheelSteps.push({ delta, scrollTop: await contextLane.evaluate(element => element.scrollTop) })
    }
    const after = await initialButtonGeometry(locator)
    const end = await contextLane.evaluate(element => element.scrollTop)
    scrollProofs.push({ label, before, after, start, end, wheelSteps, input: wheelSteps.length ? 'native-wheel' : 'initially-visible' })
    fs.writeFileSync(path.join(output, 'native-scroll-evidence.json'), JSON.stringify({ scrollProofs }, null, 2))
    assert.ok(after.visibleRatio >= .999 && after.hit, `${label} must become completely reachable using only native context scrolling: ${JSON.stringify({ before, after, start, end, wheelSteps })}`)
    await page.screenshot({ path: path.join(output, `${label.replace(/[^a-zA-Z0-9-]/g, '-')}.png`) })
  }
  const assertHorizontalDecisionBar = async (panel, firstName, secondName, label) => {
    const geometry = await panel.evaluate((element, names) => {
      const buttons = [...element.querySelectorAll('button')]
      const firstButton = buttons.find(button => button.textContent?.trim() === names[0])
      const secondButton = buttons.find(button => button.textContent?.trim() === names[1])
      if (!firstButton || !secondButton) return null
      const actor = element.querySelector('.action-actor-badge')
      return {
        actorText: actor?.textContent?.trim(),
        // Offset geometry remains in logical canvas coordinates when portrait
        // mobile rendering rotates the complete surface by ninety degrees.
        panelHeight: element.offsetHeight,
        actorHeight: actor?.offsetHeight ?? 0,
        decisionHeight: firstButton.offsetHeight,
        actorAboveDecisions: Boolean(actor && actor.offsetTop + actor.offsetHeight <= Math.min(firstButton.offsetTop, secondButton.offsetTop)),
        centerDelta: Math.abs(firstButton.offsetTop - secondButton.offsetTop),
        ordered: firstButton.offsetLeft + firstButton.offsetWidth <= secondButton.offsetLeft,
        sameHitBox: firstButton.offsetWidth === secondButton.offsetWidth && firstButton.offsetHeight === secondButton.offsetHeight,
      }
    }, [firstName, secondName])
    assert.ok(geometry, `${label} must contain both decision buttons`)
    assert.ok(geometry.centerDelta <= 2, `${label} buttons must share one horizontal row`)
    assert.equal(geometry.ordered, true, `${label} buttons must be ordered left-to-right`)
    assert.equal(geometry.actorText, '你在防守', `${label} must retain its actor information`)
    assert.equal(geometry.actorAboveDecisions, true, `${label} actor must occupy a separate complete row`)
    assert.equal(geometry.sameHitBox, true, `${label} buttons must preserve equal hit boxes`)
    assert.ok(geometry.decisionHeight >= 40 && geometry.decisionHeight <= 44,
      `${label} must preserve the enlarged mobile decision hit-box height: ${JSON.stringify(geometry)}`)
    assert.ok(geometry.panelHeight <= geometry.actorHeight + geometry.decisionHeight + 12, `${label} must contain only the actor, one decision row and bounded spacing: ${JSON.stringify(geometry)}`)
    for (const name of [firstName, secondName]) {
      const initial = await initialButtonGeometry(panel.getByRole('button', { name, exact: true }))
      assert.ok(initial.visibleRatio >= .999 && initial.hit, `${label}/${name} must be initially unoccluded: ${JSON.stringify(initial)}`)
    }
  }

  for (const viewport of viewports) {
    if (viewport.safeInsets && !nativeSafeAreaOverride) continue
    const suffix = `${viewport.width}x${viewport.height}${viewport.safeInsets ? '-safe' : ''}`

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
    assert.equal(await supportPanel.locator('p:visible').count(), 0, `support controls ${suffix} must not display rule explanations`)
    assert.equal(await supportPanel.getByRole('button', { name: '确认支援', exact: true }).count(), 1, `support controls ${suffix} must show confirm support`)
    assert.equal(await supportPanel.getByRole('button', { name: '不支援', exact: true }).count(), 1, `support controls ${suffix} must show decline support`)
    await assertHorizontalDecisionBar(supportPanel, '确认支援', '不支援', `support controls ${suffix}`)
    await page.screenshot({ path: path.join(output, `support-initial-${suffix}.png`) })
    await page.locator('.l12-player-mat.side-my .formation-slot').nth(3).click()
    const confirmSupport = supportPanel.getByRole('button', { name: '确认支援', exact: true })
    assert.equal(await confirmSupport.isEnabled(), true, `support controls ${suffix} must enable after a legal support is selected`)
    await page.screenshot({ path: path.join(output, `support-${suffix}.png`) })
    await nativeDecisionTap(confirmSupport, `support controls ${suffix} confirm`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `support controls ${suffix} confirm must be clickable`)

    await load(viewport, 'support=1')
    const declineSupport = page.locator('.combat-resolution-panel').getByRole('button', { name: '不支援', exact: true })
    await nativeDecisionTap(declineSupport, `support controls ${suffix} decline`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `support controls ${suffix} decline must be clickable`)
    assert.deepEqual(await page.evaluate(() => window.__sentCommands[0].command), { type: 'resolveDefense', cardInstanceIds: [], supportInstanceId: null }, `support controls ${suffix} decline must send the original empty-defense command`)

    await load(viewport, 'defense=1')
    const defensePanel = page.locator('.combat-resolution-panel')
    await defensePanel.waitFor()
    assert.equal(await page.locator('.mini-master.combat-target').count(), 1, `defense ${suffix} must highlight only the attacked master`)
    assert.equal(await page.locator('.formation-slot.combat-target').count(), 0, `defense ${suffix} must not mark any formation slot as the attacked master`)
    assert.equal(await page.locator('.formation-slot.response-target').count(), 0, `defense ${suffix} must not mark unrelated or empty slots as response targets`)
    assert.equal(await page.locator('.formation-slot.available').count(), 0, `defense ${suffix} must not retain ordinary placement destinations during combat`)
    await assertSafePanel('.combat-resolution-panel', `defense controls ${suffix}`)
    assert.equal(await defensePanel.locator('p:visible').count(), 0, `defense controls ${suffix} must not display rule explanations`)
    assert.equal(await defensePanel.getByRole('button', { name: '确认抵挡', exact: true }).count(), 1, `defense controls ${suffix} must show confirm defense`)
    assert.equal(await defensePanel.getByRole('button', { name: '不抵挡', exact: true }).count(), 1, `defense controls ${suffix} must show decline defense`)
    await assertHorizontalDecisionBar(defensePanel, '确认抵挡', '不抵挡', `defense controls ${suffix}`)
    await page.locator('.board-center>.l12-hand:last-child .hand-card-wrap').first().click()
    const confirmDefense = defensePanel.getByRole('button', { name: '确认抵挡', exact: true })
    assert.equal(await confirmDefense.isEnabled(), true, `defense controls ${suffix} must enable after a legal hand legion is selected`)
    await page.screenshot({ path: path.join(output, `defense-${suffix}.png`) })
    await nativeDecisionTap(confirmDefense, `defense controls ${suffix} confirm`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `defense controls ${suffix} confirm must be clickable`)

    await load(viewport, 'defense=1')
    const declineDefense = page.locator('.combat-resolution-panel').getByRole('button', { name: '不抵挡', exact: true })
    await nativeDecisionTap(declineDefense, `defense controls ${suffix} decline`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1, `defense controls ${suffix} decline must be clickable`)
    assert.deepEqual(await page.evaluate(() => window.__sentCommands[0].command), { type: 'resolveDefense', cardInstanceIds: [] }, `defense controls ${suffix} decline must send the original empty-defense command`)

    await load(viewport, 'field=full&board-target=1')
    await page.locator('.formation-slot.targetable').first().click()
    const targetConfirm = page.locator('.inline-prompt-controls .primary')
    await nativeContextReveal(targetConfirm, `target-confirm-${suffix}`)
    await nativeDecisionTap(targetConfirm, `target confirm ${suffix}`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1)
    assert.equal(await page.evaluate(() => window.__sentCommands[0].command.type), 'resolvePrompt')

    for (const [kind, query, control, name] of [
      ['target-decline', 'field=full&board-target=1', '.inline-prompt-controls', '不发动'],
      ['slot-cancel', 'field=empty&board-slot=1', '.board-slot-controls', '取消'],
      ['resource-cancel', 'morale-payment=1&payment-actions=1', '.resource-payment-controls', '取消打出'],
    ]) {
      await load(viewport, query)
      const cancel = page.locator(control).getByRole('button', { name, exact: true })
      await nativeContextReveal(cancel, `${kind}-${suffix}`)
      await nativeDecisionTap(cancel, `${kind} ${suffix}`)
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 1)
      const sent = await page.evaluate(() => window.__sentCommands[0].command)
      assert.equal(sent.type, 'resolvePrompt')
      assert.deepEqual(sent.cardInstanceIds, [kind === 'resource-cancel' ? 'cancel' : 'skip'])
    }
    if (viewport.width === 390 || viewport.width === 320) {
      await load(viewport, 'gm=1&field=empty')
      const gmExpand = page.locator('.gm-open')
      if (await gmExpand.count()) await gmExpand.click()
      await page.locator('.gm-panel .gm-hand button').first().click()
      await page.locator('.gm-panel').getByRole('button', { name: '无视费用打出所选手牌', exact: true }).click()
      const gmMinimize = page.locator('.gm-placement-controls').getByRole('button', { name: '最小化', exact: true })
      await nativeContextReveal(gmMinimize, `gm-minimize-${suffix}`)
      await nativeDecisionTap(gmMinimize, `gm minimize ${suffix}`)
      await nativeDecisionTap(page.locator('.board-control-restore'), `gm restore ${suffix}`)
      const gmCancel = page.locator('.gm-placement-controls').getByRole('button', { name: '取消', exact: true })
      await nativeContextReveal(gmCancel, `gm-cancel-${suffix}`)
      await nativeDecisionTap(gmCancel, `gm cancel ${suffix}`)
      assert.equal(await page.locator('.gm-placement-controls').count(), 0, 'GM cancel must clear only the local placement request')
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'arming, minimizing and cancelling GM placement must not send a game mutation')

      await load(viewport, 'morale-payment=1&payment-actions=1')
      await page.locator('.resource-morale-summary').last().click()
      const resources = page.locator('.mobile-resource-row .mobile-morale-choice[aria-disabled="false"]')
      await resources.nth(0).click(); await resources.nth(1).click()
      await nativeDecisionTap(page.locator('.mobile-morale-overlay').getByRole('button', { name: '最小化', exact: true }), `resource minimize ${suffix}`)
      const resourceRestore = page.locator('.mobile-morale-restore')
      await nativeContextReveal(resourceRestore, `resource-restore-${suffix}`)
      await nativeDecisionTap(resourceRestore, `resource restore ${suffix}`)
      assert.equal(await page.locator('.mobile-morale-choice.selected').count(), 2, 'resource choices must survive minimize/restore')
      await nativeDecisionTap(page.locator('.mobile-morale-overlay').getByRole('button', { name: '确认支付', exact: true }), `resource confirm ${suffix}`)
      const payment = await page.evaluate(() => window.__sentCommands[0].command)
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 1)
      assert.equal(payment.type, 'resolvePrompt')
      assert.equal(payment.cardInstanceIds.length, 2, 'resource confirmation must preserve the exact two selected costs')
      assert.equal(payment.cardInstanceIds.some(id => ['skip', 'cancel'].includes(id)), false)
    }
  }

  fs.writeFileSync(path.join(output, 'native-scroll-evidence.json'), JSON.stringify({ scrollProofs }, null, 2))
  console.log(JSON.stringify({ output, viewports: viewports.filter(viewport => nativeSafeAreaOverride || !viewport.safeInsets), unverifiedSafeAreaProfiles: nativeSafeAreaOverride ? [] : viewports.filter(viewport => viewport.safeInsets), scenarios: ['effect', 'card-choice', 'support', 'defense', 'target-confirm', 'target-decline', 'slot-cancel', 'resource-cancel', 'gm-placement-cancel-restore', 'resource-payment-confirm-restore'], status: nativeSafeAreaOverride ? 'passed' : 'passed-with-native-safe-area-unavailable' }, null, 2))
} finally {
  await browser.close()
}
