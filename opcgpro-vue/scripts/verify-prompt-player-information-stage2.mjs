import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5199/__l12_battle_preview__'
const out = path.resolve('../artifacts/battle-player-information-stage2-prompt')
fs.mkdirSync(out, { recursive: true })
const profiles = [
  [1280, 720, false], [320, 568, true], [360, 800, true],
  [390, 844, true], [430, 932, true], [568, 320, true], [667, 375, true],
]
async function touchScroll(page, locator) {
  const box = await locator.boundingBox()
  assert(box)
  const client = await page.context().newCDPSession(page)
  try {
    await client.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 1 })
    for (const [axis, direction] of [['x', -1], ['x', 1], ['y', -1], ['y', 1]]) {
      await locator.evaluate(node => { node.scrollTop = 0 })
      const origin = { x: box.x + box.width / 2, y: box.y + box.height / 2 }
      const span = Math.min(100, (axis === 'x' ? box.width : box.height) * .8)
      const start = { x: origin.x - (axis === 'x' ? direction * span / 2 : 0),
        y: origin.y - (axis === 'y' ? direction * span / 2 : 0), id: 1 }
      await client.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [start] })
      await page.waitForTimeout(35)
      for (let step = 1; step <= 8; step++) {
        const point = { x: start.x + (axis === 'x' ? direction * span * step / 8 : 0),
          y: start.y + (axis === 'y' ? direction * span * step / 8 : 0), id: 1 }
        await client.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [point] })
        await page.waitForTimeout(16)
      }
      await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
      await page.waitForTimeout(100)
      if (await locator.evaluate(node => node.scrollTop) > 2) return true
    }
    return false
  } finally {
    await client.detach()
  }
}
const results = []
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const [width, height, mobile] of profiles) {
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: mobile })
    const page = await context.newPage()
    page.setDefaultTimeout(5000)
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
      ? route.continue() : route.abort())
    await page.goto(`${base}?canvas=1&${mobile ? 'mobile=1&' : ''}field=full&information-contract=1`)
    const panel = page.locator('.prompt-choice-panel:visible')
    await panel.waitFor()
    const stageBefore = await page.locator('.board-stage').boundingBox()
    assert(stageBefore)
    const tag = `${width}x${height}`
    await page.screenshot({ path: path.join(out, `${tag}-open.png`) })

    assert.match(await panel.locator('.prompt-actor').innerText(), /当前操作.*我方/)
    assert.match(await panel.locator('.prompt-selection-range').innerText(), /选择.*1.*项/)
    assert.match(await panel.locator('.prompt-instruction').innerText(), /同名卡按位置与当前兵力区分/)
    assert.match(await panel.locator('[data-payment-status="paid"]').innerText(), /已支付2士气/)
    assert.match(await panel.locator('.prompt-submit-consequence').innerText(), /实际结果以权威结算为准/)
    assert.match(await panel.locator('.prompt-exit-consequence').allInnerTexts().then(items => items.join(' / ')), /取消整次发动/)

    const candidates = panel.locator('.prompt-card-candidate')
    assert.equal(await candidates.count(), 3)
    let touchScrolled = false
    if (mobile) {
      const body = panel.locator('.prompt-choice-body')
      assert(await body.evaluate(node => node.scrollHeight > node.clientHeight), `${tag}: expected scrollable choice body`)
      touchScrolled = await touchScroll(page, body)
      assert(touchScrolled, `${tag}: real touch gesture did not scroll choice body`)
    }
    if (mobile) await candidates.nth(0).tap()
    else await candidates.nth(0).click()
    const firstDetail = await panel.locator('.prompt-choice-detail').innerText()
    assert.match(firstDetail, /我方前排左格/)
    assert.match(firstDetail, /兵力 6000/)
    assert.match(firstDetail, /确认后继续结算/)
    if (mobile) await candidates.nth(1).tap({ force: true })
    else await candidates.nth(1).click({ force: true })
    const blocked = await panel.locator('.prompt-choice-detail').innerText()
    assert.match(blocked, /我方前排中格/)
    assert.match(blocked, /已休整，不能成为本次效果目标/)
    const unavailable = await candidates.nth(1).getAttribute('aria-label')
    assert.match(unavailable ?? '', /已休整，不能成为本次效果目标/)
    await panel.locator('.prompt-action-footer').scrollIntoViewIfNeeded()

    const geometry = await panel.evaluate(element => {
      const frame = element.getBoundingClientRect()
      const body = element.querySelector('.prompt-choice-body')
      const footer = element.querySelector('.prompt-action-footer')
      const controls = [...element.querySelectorAll('.prompt-minimize,.prompt-action-footer button')].map(node => {
        const r = node.getBoundingClientRect()
        return { label: node.getAttribute('aria-label') || node.textContent?.trim(), x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom }
      })
      return {
        frame: { x: frame.x, y: frame.y, right: frame.right, bottom: frame.bottom },
        body: body ? { clientHeight: body.clientHeight, scrollHeight: body.scrollHeight } : null,
        footer: footer ? { y: footer.getBoundingClientRect().y, bottom: footer.getBoundingClientRect().bottom } : null,
        controls,
        pageScrollWidth: document.documentElement.scrollWidth,
        pageClientWidth: document.documentElement.clientWidth,
      }
    })
    assert(geometry.pageScrollWidth <= geometry.pageClientWidth + 1, `${tag}: page horizontal overflow`)
    assert(geometry.frame.x >= -1 && geometry.frame.right <= width + 1
      && geometry.frame.y >= -1 && geometry.frame.bottom <= height + 1, `${tag}: prompt outside viewport`)
    for (const control of geometry.controls) {
      if (mobile) assert(control.width >= 44 && control.height >= 44, `${tag}: small target ${control.label}`)
      assert(control.x >= -1 && control.right <= width + 1 && control.y >= -1 && control.bottom <= height + 1,
        `${tag}: hidden target ${control.label}`)
    }
    const peerButtons = geometry.controls.filter(control => control.label !== '最小化弹框')
    for (const control of peerButtons) {
      assert(Math.abs(control.width - peerButtons[0].width) <= 1 && Math.abs(control.height - peerButtons[0].height) <= 1,
        `${tag}: unequal footer actions`)
      for (const other of peerButtons) {
        if (other === control) continue
        assert(control.right <= other.x + 1 || other.right <= control.x + 1
          || control.bottom <= other.y + 1 || other.bottom <= control.y + 1,
        `${tag}: footer actions overlap`)
      }
    }
    if (geometry.body && geometry.body.scrollHeight > geometry.body.clientHeight)
      await panel.locator('.prompt-choice-body').evaluate(body => { body.scrollTop = body.scrollHeight })
    await page.screenshot({ path: path.join(out, `${tag}-detail.png`) })

    if (mobile) await panel.locator('.prompt-minimize').tap()
    else await panel.locator('.prompt-minimize').click()
    const expand = page.locator('.prompt-minimized-bar button')
    await expand.waitFor()
    assert.match(await page.locator('.prompt-minimized-task').innerText(), /双方战场选择1张/)
    assert.equal(await expand.evaluate(node => document.activeElement === node), true, `${tag}: focus not moved to expand`)
    const stageAfter = await page.locator('.board-stage').boundingBox()
    assert(stageAfter && Math.abs(stageAfter.x - stageBefore.x) < 1
      && Math.abs(stageAfter.y - stageBefore.y) < 1
      && Math.abs(stageAfter.width - stageBefore.width) < 1
      && Math.abs(stageAfter.height - stageBefore.height) < 1, `${tag}: board geometry changed`)
    await page.screenshot({ path: path.join(out, `${tag}-minimized.png`) })
    if (mobile) await expand.tap()
    else await expand.click()
    await panel.waitFor()
    assert.equal(await panel.locator('.prompt-minimize').evaluate(node => document.activeElement === node), true,
      `${tag}: focus not restored to prompt`)
    await page.evaluate(() => {
      window.__stage2PromptCopy = JSON.parse(JSON.stringify(window.__l12State.game.prompts[0]))
      window.__l12State.game.prompts = []
    })
    await panel.waitFor({ state: 'hidden' })
    const focusTarget = page.locator('.board-stage button').first()
    await focusTarget.focus()
    await page.evaluate(() => { window.__l12State.game.prompts = [window.__stage2PromptCopy] })
    await panel.waitFor()
    await page.evaluate(() => { window.__l12State.game.prompts = [] })
    await panel.waitFor({ state: 'hidden' })
    assert(await focusTarget.evaluate(node => document.activeElement === node), `${tag}: focus not restored after prompt completion`)
    assert.equal(errors.length, 0, `${tag}: page errors: ${errors.join(' / ')}`)
    results.push({ viewport: tag, mobile, touchScrolled, peerActionsEqualAndSeparate: true,
      focusRestoredAfterMinimizeAndCompletion: true, geometry, errors })
    await context.close()
  }
  fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify(results, null, 2))
  console.log(`Prompt information stage 2: ${results.length} viewports passed; screenshots and results at ${out}`)
} finally {
  await browser.close()
}
