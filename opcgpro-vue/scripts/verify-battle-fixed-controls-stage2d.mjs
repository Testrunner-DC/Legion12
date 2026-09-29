import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5241/__l12_battle_preview__'
const output = path.resolve('artifacts/stage2d-fixed-controls')
fs.mkdirSync(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const matrix = []

async function geometry(locator) {
  return locator.evaluate(button => {
    const rect = button.getBoundingClientRect()
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
    const style = getComputedStyle(button)
    const clipped = []
    for (let parent = button.parentElement; parent; parent = parent.parentElement) {
      const css = getComputedStyle(parent)
      if (!/(hidden|clip|scroll|auto)/.test(`${css.overflowX} ${css.overflowY}`)) continue
      const bound = parent.getBoundingClientRect()
      if (rect.left < bound.left - 1 || rect.top < bound.top - 1 || rect.right > bound.right + 1 || rect.bottom > bound.bottom + 1)
        clipped.push(parent.className)
    }
    return { rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom },
      hit: button === hit || button.contains(hit), clipped, display: style.display,
      visibility: style.visibility, opacity: style.opacity,
      inside: rect.width > 0 && rect.height > 0 && rect.left >= -1 && rect.top >= -1 && rect.right <= innerWidth + 1 && rect.bottom <= innerHeight + 1 }
  })
}
async function assertRoute(page, label, spectator) {
  for (const name of spectator ? ['返回大厅'] : ['返回大厅', '投降']) {
    const button = page.getByRole('button', { name, exact: true }).first()
    const evidence = await geometry(button)
    assert.ok(evidence.hit && evidence.inside && !evidence.clipped.length && evidence.display !== 'none' && evidence.visibility === 'visible' && Number(evidence.opacity) > 0,
      `${label} ${name}: ${JSON.stringify(evidence)}`)
    // Capture a real pointer click without navigating away or surrendering the fixture.
    await button.evaluate(node => node.addEventListener('click', event => {
      event.preventDefault(); event.stopImmediatePropagation()
      window.__stage2dRouteClicks = (window.__stage2dRouteClicks || 0) + 1
    }, { capture: true, once: true }))
    const before = await page.evaluate(() => window.__stage2dRouteClicks || 0)
    await button.click()
    assert.equal(await page.evaluate(() => window.__stage2dRouteClicks || 0), before + 1, `${label} ${name}: real click did not arrive`)
  }
}
async function assertBoardBlocked(page, label) {
  const field = page.locator('.formation-slot').first()
  const evidence = await field.evaluate(node => {
    const rect = node.getBoundingClientRect()
    const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
    return { blocked: hit !== node && !node.contains(hit), hitClass: typeof hit?.className === 'string' ? hit.className : hit?.tagName }
  })
  assert.equal(evidence.blocked, true, `${label}: backdrop allowed a battlefield hit: ${JSON.stringify(evidence)}`)
}
async function check(page, label, spectator) {
  await assertRoute(page, label, spectator)
  await assertBoardBlocked(page, label)
  await page.screenshot({ path: path.join(output, `${label}.png`) })
  matrix.push(label)
}
async function checkOsiris(page, label, spectator) {
  await page.waitForFunction(() => Boolean(window.__osirisFixture?.start))
  await page.evaluate(() => window.__osirisFixture.start())
  const sequence = page.locator('.osiris-victory-sequence')
  await sequence.waitFor()
  const layers = await page.evaluate(() => ({
    sequence: Number(getComputedStyle(document.querySelector('.osiris-victory-sequence')).zIndex),
    route: Number(getComputedStyle(document.querySelector('.battle-route-controls')).zIndex),
  }))
  assert.ok(layers.route > layers.sequence, `${label}: route must paint above victory: ${JSON.stringify(layers)}`)
  await check(page, `${label}-victory`, spectator)
  const handle = page.locator('.mobile-card-inspector-handle-global')
  if (await handle.count()) assert.equal(await handle.evaluate(node => getComputedStyle(node).visibility), 'hidden', `${label}: inspector handle must not bypass victory blocker`)
  return sequence
}

try {
  const page = await browser.newPage()
  page.setDefaultTimeout(9000)
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.host === '127.0.0.1:8080' && url.pathname === '/api/ranked/broadcasts/settings')
      return route.fulfill({ status: 200, contentType: 'application/json', body: '{}' })
    return ['127.0.0.1', 'localhost'].includes(url.hostname) ? route.continue() : route.abort()
  })
  const profiles = [
    { name: 'desktop-1280', width: 1280, height: 720 },
    { name: 'wide-1920', width: 1920, height: 1080 },
    { name: 'desktop-font-125', width: 1366, height: 768, zoom: 1.25 },
    { name: 'mobile-320', width: 320, height: 568, mobile: true },
    { name: 'mobile-568', width: 568, height: 320, mobile: true },
    { name: 'mobile-667', width: 667, height: 375, mobile: true },
    { name: 'mobile-font-125', width: 844, height: 390, mobile: true, textScale: true },
    { name: 'mobile-844-safe', width: 844, height: 390, mobile: true, safe: true },
  ]
  for (const profile of profiles) {
    await page.setViewportSize({ width: profile.width, height: profile.height })
    const query = `${profile.mobile ? 'canvas=1&mobile=1&' : ''}field=full&hand=6&modalFixture=1`
    async function adjust() {
      if (profile.safe) await page.addStyleTag({ content: 'html { --l12-viewport-left:44px !important; --l12-viewport-width:756px !important; --l12-viewport-height:369px !important; }' })
      if (profile.zoom) await page.locator('html').evaluate((node, zoom) => { node.style.zoom = String(zoom) }, profile.zoom)
      if (profile.textScale) await page.addStyleTag({ content: '#l12-landscape-teleports .mobile-battle-dock__route button { font-size:13px !important; }' })
    }
    await page.goto(`${origin}?${query}`, { waitUntil: 'domcontentloaded' })
    await adjust()
    await page.getByRole('button', { name: '打开对局设置', exact: true }).click()
    await check(page, `${profile.name}-settings`, false)
    await page.getByRole('button', { name: '关闭设置', exact: true }).click()
    await page.getByRole('button', { name: '打开好友功能', exact: true }).click()
    await check(page, `${profile.name}-friends`, false)
    await page.getByRole('button', { name: '关闭好友功能', exact: true }).click()
    await page.getByRole('button', { name: '打开对局工具', exact: true }).click()
    await check(page, `${profile.name}-tools`, false)
    await page.getByRole('button', { name: '关闭对局工具', exact: true }).click()
    if (profile.name === 'mobile-568') {
      await page.getByRole('button', { name: '打开对局工具', exact: true }).click()
      await page.locator('.tools-dialog').getByRole('button', { name: /Bug反馈/ }).click()
      await page.locator('.bug-feedback-dialog').waitFor()
      await check(page, `${profile.name}-feedback`, false)
      await page.locator('.bug-feedback-dialog').getByRole('button', { name: '取消', exact: true }).click()
    }
    await page.locator('.mini-master').last().click()
    await check(page, `${profile.name}-master`, false)
    await page.locator('.master-close').click()
    await page.locator('.graveyard').last().click()
    await check(page, `${profile.name}-graveyard`, false)
    await page.getByRole('button', { name: '关闭墓地' }).click()
    await page.locator('.resource-faction-action').last().click()
    await check(page, `${profile.name}-faction`, false)
    await page.locator('.faction-close').click()
    if (profile.mobile) {
      await page.locator('.mobile-record-trigger').click()
      await check(page, `${profile.name}-record`, false)
      await page.locator('.mobile-record-overlay').getByRole('button', { name: '关闭' }).click()
    }
    for (const role of ['spectator', 'referee']) {
      await page.goto(`${origin}?${query}&${role}=1`, { waitUntil: 'domcontentloaded' })
      await adjust()
      await page.locator('.mini-master').last().click()
      await check(page, `${profile.name}-${role}-master`, true)
    }
    for (const role of ['player', 'spectator', 'referee']) {
      await page.goto(`${origin}?${query}${role === 'player' ? '' : `&${role}=1`}`, { waitUntil: 'domcontentloaded' })
      await adjust()
      const sequence = await checkOsiris(page, `${profile.name}-${role}`, role !== 'player')
      if (profile.name === 'mobile-568' && role === 'player') {
        await sequence.waitFor({ state: 'detached', timeout: 10000 })
        await assertRoute(page, `${profile.name}-victory-complete`, false)
        assert.equal(await page.locator('.osiris-victory-sequence').count(), 0, 'victory blocker must retire after the animation')
        const handle = page.locator('.mobile-card-inspector-handle-global')
        if (await handle.count()) assert.notEqual(await handle.evaluate(node => getComputedStyle(node).visibility), 'hidden', 'inspector handle must recover after victory')
        matrix.push(`${profile.name}-victory-complete`)
      }
    }
  }
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto(`${origin}?field=full&hand=6&modalFixture=1`, { waitUntil: 'domcontentloaded' })
  await page.waitForFunction(() => Boolean(window.__osirisFixture?.start))
  await page.evaluate(() => window.__osirisFixture.start({ gameOver: true }))
  await page.locator('.osiris-victory-sequence').waitFor()
  await assertRoute(page, 'desktop-game-over-victory', true)
  await assertBoardBlocked(page, 'desktop-game-over-victory')
  await page.screenshot({ path: path.join(output, 'desktop-game-over-victory.png') })
  matrix.push('desktop-game-over-victory')
  await page.locator('.osiris-victory-sequence').waitFor({ state: 'detached', timeout: 10000 })
  const resultExit = page.locator('.game-over').getByRole('button', { name: '返回大厅', exact: true })
  await resultExit.waitFor()
  const resultHit = await geometry(resultExit)
  assert.ok(resultHit.hit && resultHit.inside, `game-over return must recover after victory: ${JSON.stringify(resultHit)}`)
  matrix.push('desktop-game-over-victory-complete')
  await page.setViewportSize({ width: 568, height: 320 })
  await page.goto(`${origin}?canvas=1&mobile=1&field=full&hand=6&modalFixture=1`, { waitUntil: 'domcontentloaded' })
  await page.waitForFunction(() => Boolean(window.__osirisFixture?.start))
  await page.evaluate(() => window.__osirisFixture.start({ gameOver: true }))
  await page.locator('.osiris-victory-sequence').waitFor()
  await assertRoute(page, 'mobile-game-over-victory', true)
  await assertBoardBlocked(page, 'mobile-game-over-victory')
  await page.screenshot({ path: path.join(output, 'mobile-game-over-victory.png') })
  matrix.push('mobile-game-over-victory')
  await page.locator('.osiris-victory-sequence').waitFor({ state: 'detached', timeout: 10000 })
  const mobileResultExit = page.locator('.game-over').getByRole('button', { name: '返回大厅', exact: true })
  await mobileResultExit.waitFor()
  const mobileResultHit = await geometry(mobileResultExit)
  assert.ok(mobileResultHit.hit && mobileResultHit.inside, `mobile game-over return must recover: ${JSON.stringify(mobileResultHit)}`)
  matrix.push('mobile-game-over-victory-complete')
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto(`${origin}?field=full&hand=6&modalFixture=1&effect=cost2`, { waitUntil: 'domcontentloaded' })
  await page.locator('.l12-prompt-overlay').waitFor()
  await check(page, 'desktop-prompt', false)
  await page.getByRole('button', { name: '打开对局设置', exact: true }).evaluate(node => node.click())
  await page.locator('.battle-settings-mask').waitFor()
  await check(page, 'desktop-prompt-plus-settings', false)
  await page.setViewportSize({ width: 568, height: 320 })
  await page.goto(`${origin}?canvas=1&mobile=1&field=full&hand=6&modalFixture=1&effect=cost2`, { waitUntil: 'domcontentloaded' })
  await page.locator('.l12-prompt-overlay').waitFor()
  await check(page, 'mobile-prompt', false)
  await page.getByRole('button', { name: '打开对局设置', exact: true }).evaluate(node => node.click())
  await page.locator('.battle-settings-mask').waitFor()
  await check(page, 'mobile-prompt-plus-settings', false)
  fs.writeFileSync(path.join(output, 'matrix.json'), JSON.stringify({ matrix }, null, 2))
  console.log(`Stage2D fixed controls: ${matrix.length} covered masks`)
} finally {
  await browser.close()
}
