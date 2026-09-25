import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5190/__l12_battle_preview__'
const output = process.env.L12_MOBILE_COMBAT_OUT || path.resolve('..', 'artifacts', 'batch-mobile-combat-settlement-b2b-c')
const viewports = [{ width: 667, height: 375 }, { width: 844, height: 390 }, { width: 932, height: 430 }, { width: 1024, height: 768 }]
const stageCases = [
  ['AttackerAttackTiming', '进攻宣告', 'declaration'],
  ['CombatDamage', '伤害结算', 'damage'],
  ['FinalizeDeaths', '阵亡结算', 'finalize'],
]
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
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  const load = async (viewport, query) => {
    await page.setViewportSize(viewport)
    await page.goto(`${target}?mobile=1&canvas=1&hand=10&trials=2&myTrials=1&rankedClock=1&${query}`, { waitUntil: 'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
  }
  const rect = selector => page.locator(selector).evaluate(element => {
    const value = element.getBoundingClientRect()
    return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height }
  })
  const inside = async (selector, label, padding = 0) => {
    const value = await rect(selector)
    const viewport = await page.evaluate(() => ({ width: innerWidth, height: innerHeight }))
    assert.ok(value.width > 0 && value.height > 0, `${label} must have a visible box`)
    assert.ok(value.left >= padding - .5 && value.top >= padding - .5 && value.right <= viewport.width - padding + .5 && value.bottom <= viewport.height - padding + .5,
      `${label} must stay inside viewport: ${JSON.stringify(value)}`)
    return value
  }
  const overlaps = (a, b) => a.left < b.right - .5 && a.right > b.left + .5 && a.top < b.bottom - .5 && a.bottom > b.top + .5
  const visibleRect = locator => locator.evaluate(element => {
    const raw = element.getBoundingClientRect()
    const value = { left:raw.left,top:raw.top,right:raw.right,bottom:raw.bottom,width:raw.width,height:raw.height }
    for (let ancestor = element.parentElement; ancestor && ancestor !== document.documentElement; ancestor = ancestor.parentElement) {
      const style = getComputedStyle(ancestor)
      if (!/(auto|scroll|hidden|clip)/.test(`${style.overflow} ${style.overflowX} ${style.overflowY}`)) continue
      const clip = ancestor.getBoundingClientRect()
      value.left = Math.max(value.left, clip.left); value.top = Math.max(value.top, clip.top)
      value.right = Math.min(value.right, clip.right); value.bottom = Math.min(value.bottom, clip.bottom)
    }
    value.width = Math.max(0, value.right - value.left); value.height = Math.max(0, value.bottom - value.top)
    return value
  })
  const noOverlap = async (one, other, label) => {
    const others = page.locator(other)
    const count = await others.count()
    if (!count) return
    const a = await visibleRect(page.locator(one))
    for (let index = 0; index < count; index++) {
      const item = others.nth(index)
      if (!await item.isVisible()) continue
      const b = await visibleRect(item)
      if (b.width <= 0 || b.height <= 0) continue
      assert.equal(overlaps(a, b), false, `${label} ${index + 1} must not overlap: ${JSON.stringify({ a, b })}`)
    }
  }

  for (const viewport of viewports) {
    const suffix = `${viewport.width}x${viewport.height}`
    for (const [stage, label, file] of stageCases) {
      await load(viewport, `combat-stage=${stage}`)
      const combat = page.locator('.combat-presentation--passive')
      await combat.locator('.combat-versus').waitFor()
      assert.equal((await combat.locator('.combat-stage-label').textContent())?.trim(), label, `${stage} ${suffix} must use player-facing stage copy`)
      assert.equal(await combat.locator('.combat-versus').evaluate(element => element.scrollWidth <= element.clientWidth + 1 && element.scrollHeight <= element.clientHeight + 1), true, `${stage} ${suffix} status must not clip`)
      await inside('.combat-presentation--passive .combat-versus', `${stage} ${suffix} combat status`, 2)
      assert.equal(await combat.evaluate(element => Boolean(element.closest('.mobile-battle-dock__context'))), true, `${stage} ${suffix} combat status must use the scrollable context lane`)
      await noOverlap('.combat-presentation--passive .combat-versus', '.left-rail > .mobile-card-inspector-handle', `${stage} ${suffix} detail handle`)
      await noOverlap('.combat-presentation--passive .combat-versus', '.mobile-timed-clocks', `${stage} ${suffix} clocks`)
      await noOverlap('.combat-presentation--passive .combat-versus', '.board-center > .l12-hand:last-child', `${stage} ${suffix} own hand`)
      await noOverlap('.combat-presentation--passive .combat-versus', '.mobile-hand-count', `${stage} ${suffix} PlayerMat hand count`)
      await page.screenshot({ path: path.join(output, `combat-${file}-${suffix}.png`) })
    }

    await load(viewport, 'combat-stage=CombatDamage')
    await page.locator('.mobile-record-trigger').click()
    const record = page.locator('.mobile-record-overlay[aria-label="对局记录"]')
    await record.waitFor()
    const recordText = (await record.innerText()).replace(/\s+/g, '')
    assert.match(recordText, /4500/, `record ${suffix} must keep player-facing attack value`)
    assert.doesNotMatch(recordText, /冻结进攻值/, `record ${suffix} must hide implementation wording`)
    await inside('.mobile-record-overlay[aria-label="对局记录"]', `record ${suffix}`, 4)
    await page.screenshot({ path: path.join(output, `combat-record-${suffix}.png`) })
    await record.getByRole('button', { name: '关闭', exact: true }).click()

    await load(viewport, 'game-over=1')
    const gameOver = page.locator('.game-over')
    await gameOver.waitFor()
    await inside('.game-over', `game over ${suffix}`)
    const gameOverText = (await gameOver.innerText()).replace(/\s+/g, '')
    for (const forbidden of ['双方都离开后关闭房间', '最长保留30分钟', '点击返回后才离开本局', '服务器保留', '结果将保留在此处', 'REV'])
      assert.equal(gameOverText.includes(forbidden), false, `game over ${suffix} must hide ${forbidden}`)
    assert.match(gameOverText, /达成胜利条件/, `game over ${suffix} must retain the player-facing reason`)
    assert.equal(await page.locator('.battle-route-controls:visible').count(), 0, `game over ${suffix} must not retain duplicate route controls above the result`)
    assert.equal(await gameOver.evaluate(element => element.scrollWidth <= element.clientWidth + 1), true, `game over ${suffix} must not scroll horizontally`)
    for (const selector of ['.game-over>p', '.game-over>strong', '.game-over>small', '.game-over>.ranked-result', '.game-over>button:not(.game-over-minimize)'])
      await inside(selector, `${selector} ${suffix}`, 4)
    const details = gameOver.locator('details')
    await details.locator('summary').click()
    assert.equal(await details.evaluate(element => element.scrollWidth <= element.clientWidth + 1 && element.scrollHeight <= element.clientHeight + 1), true, `settlement details ${suffix} must remain readable`)
    await page.screenshot({ path: path.join(output, `game-over-${suffix}.png`) })
    await gameOver.getByRole('button', { name: '返回大厅', exact: true }).click()
    await page.waitForFunction(() => window.__sentCommands.some(command => command.type === 'leaveRoom'))
  }

  console.log(JSON.stringify({ output, viewports, scenarios: ['attack-declaration', 'combat-damage', 'death-finalize', 'record-wording', 'game-over-return'], status: 'passed' }, null, 2))
} finally {
  await browser.close()
}
