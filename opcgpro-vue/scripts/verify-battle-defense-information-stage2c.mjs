import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5237/__l12_battle_preview__'
const output = path.resolve('artifacts/stage2c-defense-information')
fs.mkdirSync(output, { recursive: true })
const viewports = [
  { width: 1280, height: 720, mobile: false },
  { width: 320, height: 568, mobile: true },
  { width: 360, height: 800, mobile: true },
  { width: 390, height: 844, mobile: true },
  { width: 430, height: 932, mobile: true },
  { width: 568, height: 320, mobile: true },
  { width: 667, height: 375, mobile: true },
]
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
async function visibleHitbox(locator, label) {
  const evidence = await locator.evaluate(button => {
    const rect = button.getBoundingClientRect()
    const box = node => {
      const value = node.getBoundingClientRect()
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom }
    }
    const clippedBy = []
    for (let parent = button.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent)
      if (!/(hidden|clip|auto|scroll)/.test(`${style.overflowX} ${style.overflowY}`)) continue
      const bound = parent.getBoundingClientRect()
      if (rect.left < bound.left - 1 || rect.top < bound.top - 1 || rect.right > bound.right + 1 || rect.bottom > bound.bottom + 1)
        clippedBy.push({ className: parent.className, rect: box(parent), overflow: `${style.overflowX}/${style.overflowY}` })
    }
    const center = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2)
    return { rect: box(button), clippedBy, hit: button.contains(center),
      insideViewport: rect.width > 0 && rect.height > 0 && rect.left >= -1 && rect.top >= -1 && rect.right <= innerWidth + 1 && rect.bottom <= innerHeight + 1 }
  })
  assert.ok(evidence.insideViewport && evidence.clippedBy.length === 0 && evidence.hit,
    `${label}: control must be initially visible and hit-testable without auto-scroll: ${JSON.stringify(evidence)}`)
  return evidence
}
async function visibleExplanationTrigger(page, viewport, label) {
  const trigger = page.locator('.combat-decision-info-trigger')
  const hitbox = await visibleHitbox(trigger, label)
  const evidence = await trigger.evaluate(button => {
    const rect = button.getBoundingClientRect()
    const style = getComputedStyle(button)
    const visualCover = [...document.querySelectorAll('.combat-presentation, .combat-versus')].flatMap(element => {
      const cover = element.getBoundingClientRect()
      const overlapWidth = Math.max(0, Math.min(rect.right, cover.right) - Math.max(rect.left, cover.left))
      const overlapHeight = Math.max(0, Math.min(rect.bottom, cover.bottom) - Math.max(rect.top, cover.top))
      return overlapWidth * overlapHeight > 1 ? [{ className: element.className, overlapWidth, overlapHeight }] : []
    })
    return { lane: button.parentElement?.className, display: style.display, visibility: style.visibility,
      opacity: Number(style.opacity), visualCover }
  })
  assert.ok(evidence.display !== 'none' && evidence.visibility === 'visible' && evidence.opacity > 0,
    `${label}: explanation entry must be visually rendered: ${JSON.stringify(evidence)}`)
  if (viewport.mobile) {
    assert.match(evidence.lane, /mobile-battle-dock__tools/, `${label}: explanation entry needs its own dock row`)
    assert.deepEqual(evidence.visualCover, [], `${label}: combat values must not paint over explanation entry`)
  }
  return { ...hitbox, ...evidence }
}
async function visibleDecisionButtons(panel, decision, label) {
  const confirm = panel.getByRole('button', { name: `确认${decision}` })
  const decline = panel.getByRole('button', { name: `不${decision}` })
  const first = await visibleHitbox(confirm, `${label} confirm`)
  const second = await visibleHitbox(decline, `${label} decline`)
  assert.ok(first.rect.bottom <= second.rect.top + 1 || second.rect.bottom <= first.rect.top + 1
    || first.rect.right <= second.rect.left + 1 || second.rect.right <= first.rect.left + 1,
    `${label}: decision controls must not overlap`)
}
try {
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => {
    if (message.type() === 'error' || /\[Vue warn\]/.test(message.text()))
      errors.push(`${message.text()} @ ${message.location().url}`)
  })
  page.setDefaultTimeout(10000)
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.host === '127.0.0.1:8080' && url.pathname === '/api/ranked/broadcasts/settings')
      return route.fulfill({ status: 200, contentType: 'application/json', body: '{}' })
    return ['127.0.0.1', 'localhost'].includes(url.hostname) ? route.continue() : route.abort()
  })
  for (const viewport of viewports) for (const kind of ['defense', 'support']) {
    await page.setViewportSize(viewport)
    const query = `${viewport.mobile ? 'mobile=1&canvas=1&' : ''}hand=6&field=full&${kind}=1`
    await page.goto(`${target}?${query}`, { waitUntil: 'domcontentloaded' })
    const panel = page.locator('.combat-resolution-panel')
    await panel.waitFor()
    const decision = kind === 'defense' ? '抵挡' : '支援'
    assert.equal(await panel.getByRole('button', { name: `不${decision}` }).count(), 1)
    await visibleDecisionButtons(panel, decision, `${viewport.width}x${viewport.height} ${kind} initial`)
    const triggerEvidence = await visibleExplanationTrigger(page, viewport, `${viewport.width}x${viewport.height} ${kind} explanation trigger`)
    const details = page.locator('.combat-decision-info-panel')
    await page.screenshot({ path: path.join(output, `${kind}-${viewport.width}x${viewport.height}-before.png`) })
    await page.locator('.combat-decision-info-trigger').click()
    await details.waitFor()
    assert.equal(await page.evaluate(() => document.activeElement?.textContent?.trim()), '返回选择')
    const text = await details.innerText()
    assert.match(text, /进攻值/)
    assert.match(text, /已选/)
    assert.match(text, /立即提交放弃/)
    assert.match(text, kind === 'defense' ? /弃置这些军团/ : /支援军团阵亡/)
    const geometry = await page.evaluate(() => {
      const selector = document.querySelector('.combat-decision-info-panel') || document.querySelector('.combat-resolution-panel')
      const rect = selector.getBoundingClientRect()
      const root = document.documentElement
      return { rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom },
        scrollWidth: root.scrollWidth, viewportWidth: innerWidth, scrollHeight: selector.scrollHeight, clientHeight: selector.clientHeight }
    })
    assert.ok(geometry.scrollWidth <= geometry.viewportWidth + 1, `${viewport.width}x${viewport.height} ${kind}: horizontal overflow ${JSON.stringify(geometry)}`)
    assert.ok(geometry.rect.left >= -1 && geometry.rect.right <= viewport.width + 1 && geometry.rect.top >= -1 && geometry.rect.bottom <= viewport.height + 1,
      `${viewport.width}x${viewport.height} ${kind}: explanation outside viewport ${JSON.stringify(geometry)}`)
    await page.screenshot({ path: path.join(output, `${kind}-${viewport.width}x${viewport.height}.png`) })
    await details.getByRole('button', { name: '返回选择' }).click()
    assert.ok(await page.locator('.combat-decision-info-trigger').evaluate(node => node === document.activeElement), 'closing explanation must restore focus')
    if (viewport.mobile) {
      await page.locator('.combat-decision-minimize').click()
      await page.locator('.combat-decision-restore').click()
      assert.equal(await panel.getByRole('button', { name: `不${decision}` }).count(), 1)
      await visibleDecisionButtons(panel, decision, `${viewport.width}x${viewport.height} ${kind} restored`)
    }
    if (kind === 'defense') {
      await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').first().click()
      const expected = await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap.selected').count()
      assert.equal(expected, 1, 'selected blocker must be retained before decline')
    } else {
      await page.locator('.my-half .formation-slot').nth(3).click()
      await page.locator('.combat-decision-info-trigger').click()
      assert.match(await page.locator('.combat-decision-info-panel').innerText(), /已选 1 张支援军团/)
      await page.locator('.combat-decision-info-panel').getByRole('button', { name: '返回选择' }).click()
    }
    await visibleDecisionButtons(panel, decision, `${viewport.width}x${viewport.height} ${kind} before decline`)
    await panel.getByRole('button', { name: `不${decision}` }).click()
    const command = await page.evaluate(() => window.__sentCommands.at(-1)?.command)
    assert.equal(command?.type, 'resolveDefense')
    assert.deepEqual(command.cardInstanceIds, [], `${kind} decline must submit an empty choice despite local selection`)
    results.push({ viewport: `${viewport.width}x${viewport.height}`, kind, geometry, triggerEvidence })
  }
  await page.setViewportSize({ width: 1280, height: 720 })
  for (const kind of ['defense', 'support']) {
    await page.goto(`${target}?field=full&hand=6&${kind}=1&easy-defense=1&richard-tax=1`, { waitUntil: 'domcontentloaded' })
    await page.locator('.combat-resolution-panel').waitFor()
    await page.locator('.combat-decision-info-trigger').click()
    assert.match(await page.locator('.combat-decision-info-panel').innerText(), /额外弃置 1 张手牌/)
    await page.locator('.combat-decision-info-panel').getByRole('button', { name: '返回选择' }).click()
    const chosen = kind === 'defense'
      ? await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').first().getAttribute('data-flip-id')
      : 'fixture-support-back-row'
    if (kind === 'defense') await page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').first().click()
    else await page.locator('.my-half .formation-slot').nth(3).click()
    await page.locator('.combat-resolution-panel .primary').click()
    const command = await page.evaluate(() => window.__sentCommands.at(-1)?.command)
    assert.deepEqual(command.cardInstanceIds, [chosen], `${kind} confirm must submit the chosen instance`)
  }
  for (const observer of ['spectator', 'referee']) {
    await page.goto(`${target}?field=full&hand=6&defense=1&${observer}=1`, { waitUntil: 'domcontentloaded' })
    await page.locator('.combat-versus').waitFor()
    assert.equal(await page.locator('.combat-decision-info-trigger').count(), 0, `${observer} must not receive private defense controls`)
    assert.equal(await page.locator('.combat-resolution-panel').count(), 0, `${observer} must not receive private defense choices`)
  }
  assert.deepEqual(errors, [], 'browser page errors')
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ results, errors }, null, 2))
  console.log(`Stage2C browser: ${results.length}/${results.length} decisions passed`)
} finally {
  await browser.close()
}
