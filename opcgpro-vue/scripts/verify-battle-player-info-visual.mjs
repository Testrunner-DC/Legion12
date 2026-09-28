import assert from 'node:assert/strict'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5199/__l12_battle_preview__'
const viewports = [
  { width: 1280, height: 720, mobile: false },
  { width: 667, height: 375, mobile: true },
]

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const page = await browser.newPage()
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`${target}?${viewport.mobile ? 'mobile=1&canvas=1&' : ''}hand=6`, { waitUntil: 'domcontentloaded' })
    await page.locator('.my-half').waitFor()
    await page.evaluate(() => {
      const state = window.__l12State
      state.game.phase = 'Mulligan'
      state.game.players[0].mulliganDone = false
      state.game.players[1].mulliganDone = false
    })
    const mulligan = page.locator('.mulligan-panel')
    await mulligan.waitFor()
    assert.match(await mulligan.innerText(), /选择要换掉的起始手牌/)
    assert.match(await mulligan.innerText(), /不选牌直接确认会保留全部起始手牌/)
    assert.match(await mulligan.locator('.prompt-action-footer button').innerText(), /保留全部手牌/)
    const footerFits = await mulligan.locator('.prompt-action-footer button').evaluate(button => button.scrollWidth <= button.clientWidth + 1)
    assert(footerFits, `${viewport.width}x${viewport.height}: mulligan action text clips`)

    await page.evaluate(() => {
      const state = window.__l12State
      state.game.phase = 'Main'
      state.game.players[0].mulliganDone = true
      state.game.prompts = [{
        promptId: 'fixture-player-info-payment', playerIndex: 0, kind: 'option',
        text: '选择目标', validChoices: ['yes', 'cancel'], minChoose: 1, maxChoose: 1,
        choiceLabels: { yes: '继续', cancel: '取消' }, data: {}, createdRevision: 1, controller: 0,
        presentation: {
          title: '测试费用状态', situation: '费用已经发生', instruction: '选择下一步',
          waitingSummary: '等待选择', choiceConsequences: { cancel: '返回选择，不继续结算。' },
          paymentStatus: 'paid', paymentSummary: '已弃置1张手牌作为费用；目标失效时不返还。',
          submissionConsequence: '确认后继续结算。',
        },
      }]
    })
    const prompt = page.locator('.prompt-choice-panel:visible').first()
    await prompt.waitFor()
    assert.match(await prompt.locator('[data-payment-status="paid"]').innerText(), /已弃置1张手牌/)
    assert.match(await prompt.locator('.prompt-submit-consequence').innerText(), /确认后继续结算/)
    assert.match(await prompt.locator('.choice-consequence').innerText(), /返回选择，不继续结算/)
    await prompt.locator('.prompt-minimize').click()
    assert.match(await page.locator('.prompt-minimized-task').innerText(), /选择下一步/)
  }
  console.log('Battle player information visual checks passed at desktop and mobile viewports')
} finally {
  await browser.close()
}
