import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5253/__l12_battle_preview__'
const out = path.resolve(process.env.L12_3A_QA_OUT || '../artifacts/battle-player-information-stage3a')
fs.mkdirSync(out, { recursive: true })

const profiles = [
  [1280, 720, false], [320, 568, true], [360, 800, true],
  [390, 844, true], [430, 932, true], [568, 320, true], [667, 375, true],
]
const results = []
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  for (const [width, height, mobile] of profiles) {
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: mobile })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
      ? route.continue() : route.abort())
    await page.goto(`${base}?canvas=1&${mobile ? 'mobile=1&' : ''}field=full&information-contract=1`)
    const panel = page.locator('.prompt-choice-panel:visible')
    await panel.waitFor()
    await page.evaluate(() => {
      const prompt = window.__l12State.game.prompts[0]
      prompt.presentation.paymentStatus = 'pending'
      prompt.presentation.paymentSummary = '1份资源（士气、神力）；已选择但尚未提交支付'
      prompt.presentation.submissionConsequence = '提交本次声明；费用、对象和效果结果仍由权威结算确定。'
      prompt.presentation.choiceConsequences.cancel = '取消本次发动声明；未提交的选择不再继续。'
    })
    const tag = `${width}x${height}`
    const payment = panel.locator('[data-payment-status="pending"]')
    assert.match(await payment.innerText(), /待支付：.*尚未提交支付/)
    assert.match(await panel.locator('.prompt-submit-consequence').innerText(), /权威结算确定/)
    assert.match((await panel.locator('.prompt-exit-consequence').allInnerTexts()).join(' / '), /未提交的选择不再继续/)
    await payment.scrollIntoViewIfNeeded()
    await page.screenshot({ path: path.join(out, `${tag}-payment.png`) })
    const candidate = panel.locator('.prompt-card-candidate').first()
    if (mobile) await candidate.tap()
    else await candidate.click()
    assert.match(await panel.locator('.prompt-choice-detail').innerText(), /我方前排左格/)
    const box = await panel.boundingBox()
    assert(box && box.x >= -1 && box.y >= -1 && box.x + box.width <= width + 1
      && box.y + box.height <= height + 1, `${tag}: prompt clipped`)
    const controls = await panel.locator('button:visible').evaluateAll(nodes => nodes.map(node => {
      const rect = node.getBoundingClientRect()
      return { width: rect.width, height: rect.height, x: rect.x, y: rect.y,
        right: rect.right, bottom: rect.bottom }
    }))
    for (const control of controls) {
      assert(control.x >= -1 && control.y >= -1 && control.right <= width + 1
        && control.bottom <= height + 1, `${tag}: action outside viewport`)
    }
    await page.screenshot({ path: path.join(out, `${tag}-pending.png`) })
    const minimize = panel.locator('.prompt-minimize')
    if (mobile) await minimize.tap()
    else await minimize.click()
    assert.match(await page.locator('.prompt-minimized-task').innerText(), /费用待支付/)
    await page.screenshot({ path: path.join(out, `${tag}-minimized.png`) })
    assert.equal(errors.length, 0, `${tag}: ${errors.join(' / ')}`)
    results.push({ viewport: tag, mobile, controls: controls.length, errors })
    await context.close()
  }
  fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify(results, null, 2))
  console.log(`Battle player information stage 3A: ${results.length} viewports passed; ${out}`)
} finally {
  await browser.close()
}
