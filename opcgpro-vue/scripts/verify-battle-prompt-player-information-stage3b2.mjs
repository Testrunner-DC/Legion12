import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import fs from 'node:fs'
import path from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5253/__l12_battle_preview__'
const output = path.resolve(process.env.L12_3B2_QA_OUT || '../artifacts/battle-prompt-player-information-stage3b2')
fs.mkdirSync(output, { recursive: true })

const profiles = [
  { width: 1440, height: 900, mobile: false },
  { width: 390, height: 844, mobile: true },
  { width: 568, height: 320, mobile: true },
]
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const profile of profiles) {
    const { width, height, mobile } = profile
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: mobile })
    const page = await context.newPage()
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
      ? route.continue() : route.abort())
    const params = new URLSearchParams({ canvas: '1', field: 'full', 'information-contract': '1' })
    if (mobile) params.set('mobile', '1')
    await page.goto(`${base}?${params}`)
    const panel = page.locator('.prompt-choice-panel:visible')
    await panel.waitFor()
    await page.evaluate(() => {
      const prompt = window.__l12State.game.prompts[0]
      prompt.presentation.situation = '〈测试来源〉选择1张军团。'
      prompt.presentation.instruction = '从双方战场选择1张合法军团。'
      prompt.presentation.waitingSummary = '对手正在选择效果对象'
      prompt.presentation.choiceConsequences[prompt.validChoices[0]] = '选中这张军团后，使其获得1000兵力。'
      prompt.presentation.submissionConsequence = '所选军团获得1000兵力。'
      window.__l12State.game.players[0].name = '私密昵称甲'
      window.__l12State.game.players[1].name = '私密昵称乙'
    })
    await panel.locator('.prompt-submit-consequence').waitFor()
    assert.match(await panel.innerText(), /所选军团获得1000兵力/)
    assert.match(await panel.innerText(), /已支付2士气/)
    assert.doesNotMatch(await panel.innerText(), /私密昵称|权威处理|权威结算|本次声明/)
    const first = panel.locator('.prompt-card-candidate').first()
    if (mobile) await first.tap()
    else await first.click()
    assert.match(await panel.locator('.prompt-choice-detail').innerText(), /我方前排左格/)
    const buttons = await panel.locator('.prompt-action-footer button:visible').evaluateAll(nodes => nodes.map(node => {
      const rect = node.getBoundingClientRect()
      return { text: node.textContent?.trim(), x: rect.x, y: rect.y, width: rect.width, height: rect.height, right: rect.right, bottom: rect.bottom }
    }))
    assert(buttons.length >= 2, 'Expected same-level exit and confirm actions')
    for (const button of buttons) {
      assert(button.width >= 44 && button.height >= 44, `${width}x${height}: action too small: ${button.text}`)
      assert(button.x >= -1 && button.y >= -1 && button.right <= width + 1 && button.bottom <= height + 1,
        `${width}x${height}: action outside viewport: ${button.text}`)
    }
    assert(buttons.every(button => button.width === buttons[0].width && button.height === buttons[0].height),
      `${width}x${height}: same-level actions differ in size`)
    await page.screenshot({ path: path.join(output, `${width}x${height}-target.png`) })

    await page.evaluate(() => {
      const game = window.__l12State.game
      game.prompts = [{
        promptId: 'response-stage3b2', playerIndex: game.you, kind: 'response',
        text: '对手使用〈天诛〉。已选目标：你的前排左格〈同名测试军团〉。是否响应？',
        validChoices: ['pass', 'response-card'], minChoose: 1, maxChoose: 1,
        choiceLabels: { pass: '不响应' },
        presentation: {
          title: '是否响应',
          situation: '对手使用〈天诛〉。已选目标：你的前排左格〈同名测试军团〉。是否响应？',
          instruction: '请选择1张可响应的卡牌，或选择“不响应”。',
          waitingSummary: '对手正在决定是否响应',
          choiceConsequences: { pass: '不打出响应牌。', 'response-card': '打出〈响应牌〉响应当前效果。' },
        },
        data: { choiceMode: 'instant' }, createdRevision: 1, controller: game.you,
      }]
    })
    await panel.getByText('是否响应', { exact: true }).first().waitFor()
    const responseText = await panel.innerText()
    assert.match(responseText, /对手使用〈天诛〉.*你的前排左格〈同名测试军团〉/s)
    assert.doesNotMatch(responseText, /私密昵称|权威处理|处理链/)
    const responseChoice = panel.locator('.prompt-choices button').filter({ hasText: '打出〈响应牌〉响应当前效果。' }).first()
    assert.equal(await responseChoice.locator('span').first().innerText(), '效果选项 2',
      'Missing choice label must use a short fallback instead of the consequence')
    assert.equal(await responseChoice.locator('.choice-consequence').innerText(), '打出〈响应牌〉响应当前效果。')
    await page.screenshot({ path: path.join(output, `${width}x${height}-response.png`) })

    await page.evaluate(() => {
      const game = window.__l12State.game
      game.prompts = []
      game.waitingPrompt = { kind: 'card', playerIndex: 1 - game.you, playerName: '私密正在窥看乙',
        waitingSummary: '私密正在窥看乙 正在选择卡牌' }
    })
    const waiting = page.locator('.prompt-panel:visible').first()
    await waiting.waitFor()
    assert.match(await waiting.innerText(), /对手正在选择卡牌/)
    assert.doesNotMatch(await waiting.innerText(), /私密|窥看/)
    await page.screenshot({ path: path.join(output, `${width}x${height}-waiting.png`) })

    await page.evaluate(() => {
      const game = window.__l12State.game
      game.waitingPrompt = { kind: 'initiative', playerIndex: 1 - game.you, playerName: '私密昵称乙',
        waitingSummary: '私密昵称乙 正在选择先攻或后攻' }
    })
    assert.match(await waiting.innerText(), /对手正在选择先攻或后攻/)
    assert.doesNotMatch(await waiting.innerText(), /私密昵称/)
    await page.screenshot({ path: path.join(output, `${width}x${height}-initiative.png`) })

    await page.evaluate(() => {
      const game = window.__l12State.game
      game.waitingPrompt = null
      game.phase = 'Mulligan'
      game.players[game.you].mulliganDone = false
    })
    const mulligan = page.locator('.mulligan-panel:visible')
    await mulligan.waitFor()
    assert.match(await mulligan.innerText(), /选择要换掉的起始手牌/)
    assert.doesNotMatch(await mulligan.innerText(), /私密昵称/)
    await page.screenshot({ path: path.join(output, `${width}x${height}-mulligan.png`) })

    const paymentParams = new URLSearchParams({ canvas: '1', 'morale-payment': '1', 'inline-rich': '1', 'payment-actions': '1' })
    if (mobile) paymentParams.set('mobile', '1')
    await page.goto(`${base}?${paymentParams}`)
    if (mobile) await page.locator('.resource-payment-controls').getByRole('button', { name: '任务说明' }).click()
    const paymentInfo = page.locator(mobile ? '.inline-prompt-info-body:visible' : '.resource-payment-controls .inline-prompt-copy:visible').first()
    await paymentInfo.waitFor()
    assert.match(await paymentInfo.innerText(), /2枚士气|选择2枚士气/)
    assert.doesNotMatch(await paymentInfo.innerText(), /当前操作：.* · /)
    await page.screenshot({ path: path.join(output, `${width}x${height}-payment.png`) })

    const orderParams = new URLSearchParams({ canvas: '1', 'order-direction-fixture': 'trigger-order' })
    if (mobile) orderParams.set('mobile', '1')
    await page.goto(`${base}?${orderParams}`)
    const order = page.locator('.prompt-choice-panel:visible')
    await order.waitFor()
    assert.match(await order.innerText(), /确认发动顺序/)
    assert.doesNotMatch(await order.innerText(), /私密昵称/)
    await page.screenshot({ path: path.join(output, `${width}x${height}-order.png`) })

    const multiParams = new URLSearchParams({ canvas: '1', 'board-target': '1', 'inline-rich': '1', 'inline-long': '1' })
    if (mobile) multiParams.set('mobile', '1')
    await page.goto(`${base}?${multiParams}`)
    const multiControl = page.locator('.inline-prompt-controls:visible').first()
    await multiControl.waitFor()
    assert.match(await multiControl.innerText(), /已选 0\/2|需选择 1 至 2 项|任务说明/)
    assert.doesNotMatch(await multiControl.innerText(), /当前操作：.* · /)
    await page.screenshot({ path: path.join(output, `${width}x${height}-multi-target.png`) })
    assert.deepEqual(errors, [], `${width}x${height}: browser errors`)
    results.push({ viewport: `${width}x${height}`, buttons: buttons.length, errors })
    await context.close()
  }
  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(results, null, 2))
  console.log(`Stage3B-2 prompt information: ${results.length} viewports passed; ${output}`)
} finally {
  await browser.close()
}
