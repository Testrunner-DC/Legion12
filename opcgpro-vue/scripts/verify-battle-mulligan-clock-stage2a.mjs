import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5211/__l12_battle_preview__'
const out = path.resolve('../artifacts/battle-player-information-stage2a')
fs.mkdirSync(out, { recursive: true })

const profiles = [
  [1280, 720, false], [320, 568, true], [360, 800, true],
  [390, 844, true], [430, 932, true], [568, 320, true], [667, 375, true],
]

function insideViewport(rect, width, height, tag) {
  assert(rect, `${tag}: missing bounds`)
  assert(rect.x >= -1 && rect.y >= -1 && rect.x + rect.width <= width + 1
    && rect.y + rect.height <= height + 1, `${tag}: outside viewport ${JSON.stringify(rect)}`)
}

async function assertCenterHit(locator, tag) {
  const hit = await locator.evaluate(node => {
    const rect = node.getBoundingClientRect()
    const target = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
    return Boolean(target && (target === node || node.contains(target)))
  })
  assert(hit, `${tag}: center point is covered by another element`)
}

async function assertDocumentFits(page, tag) {
  const metrics = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    document: document.documentElement.scrollWidth,
    body: document.body.scrollWidth,
  }))
  assert(metrics.document <= metrics.viewport + 1 && metrics.body <= metrics.viewport + 1,
    `${tag}: horizontal document overflow ${JSON.stringify(metrics)}`)
}

async function assertVisibleSummary(locator, pattern, tag) {
  const state = await locator.evaluate(node => {
    const style = getComputedStyle(node)
    const rect = node.getBoundingClientRect()
    return { text: node.textContent?.trim() || '', display: style.display, visibility: style.visibility,
      opacity: Number(style.opacity), width: rect.width, height: rect.height }
  })
  assert(state.display !== 'none' && state.visibility !== 'hidden' && state.opacity > 0
    && state.width > 0 && state.height > 0, `${tag}: summary is not visibly rendered`)
  assert.match(state.text, pattern, `${tag}: visible summary does not describe the current task`)
}

async function assertRenderedContentFits(locator, tag) {
  const metrics = await locator.evaluate(node => {
    const box = node.getBoundingClientRect()
    const range = document.createRange()
    range.selectNodeContents(node)
    const content = range.getBoundingClientRect()
    const parent = node.parentElement?.getBoundingClientRect()
    return { box: { left: box.left, top: box.top, right: box.right, bottom: box.bottom },
      content: { left: content.left, top: content.top, right: content.right, bottom: content.bottom },
      parent: parent ? { left: parent.left, top: parent.top, right: parent.right, bottom: parent.bottom } : null }
  })
  const within = (inner, outer) => inner.left >= outer.left - 1 && inner.top >= outer.top - 1
    && inner.right <= outer.right + 1 && inner.bottom <= outer.bottom + 1
  assert(within(metrics.content, metrics.box), `${tag}: rendered text is clipped ${JSON.stringify(metrics)}`)
  if (metrics.parent) assert(within(metrics.box, metrics.parent), `${tag}: element exceeds clipping parent ${JSON.stringify(metrics)}`)
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const [width, height, mobile] of profiles) {
    const tag = `${width}x${height}`
    const context = await browser.newContext({ viewport: { width, height }, hasTouch: mobile })
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(new URL(route.request().url()).hostname)
      ? route.continue() : route.abort())
    await page.goto(`${base}?canvas=1&${mobile ? 'mobile=1&' : ''}field=full&hand=15&rankedClock=1&clockPhase=Mulligan`)
    await page.waitForFunction(() => Boolean(window.__l12State?.game && window.__clockFixture))
    await page.evaluate(() => {
      const state = window.__l12State
      state.game.players[0].mulliganDone = false
      state.game.players[1].mulliganDone = false
      state.game.players[0].name = '我方测试玩家甲乙丙丁'
      state.game.players[1].name = '对手测试玩家甲乙丙丁'
      state.rankedClock.players.forEach(player => { player.acting = true })
      state.rankedClock.receivedAtMs = Date.now()
    })

    const panel = page.locator('.mulligan-panel:visible')
    await panel.waitFor()
    assert.match(await panel.locator('.prompt-instruction').innerText(), /已选 0 张.*未选牌则保留全部.*排位调度超时保留原手牌/)
    assert.match((await page.locator('.l12-actions').allTextContents()).join(' '), /已选 0 张.*若对手尚未完成.*排位调度限时/)
    assert.match(await panel.locator('.setup-decision-clock').innerText(), /先攻玩家准备.*本次剩余/)
    const footerButton = panel.locator('.prompt-action-footer button')
    await footerButton.scrollIntoViewIfNeeded()
    assert.match(await footerButton.innerText(), /保留全部手牌/)
    await page.evaluate(() => window.__resetSentCommands())
    await footerButton.evaluate(node => { node.click(); node.click() })
    let sent = await page.evaluate(() => window.__sentCommands.at(-1))
    assert.equal(sent?.command?.type, 'mulligan', `${tag}: zero-selection mulligan command missing`)
    assert.deepEqual(sent?.command?.cardInstanceIds, [], `${tag}: zero-selection must submit an empty card list`)
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 1,
      `${tag}: pending mulligan must block duplicate submission`)
    await page.evaluate(() => window.__resetSentCommands())

    const first = panel.locator('.prompt-card-candidate').first()
    if (mobile) await first.tap()
    else await first.click()
    assert.match(await panel.locator('.prompt-instruction').innerText(), /已选 1 张/)
    assert.match(await panel.locator('.prompt-action-footer').innerText(), /已选 1 张\s*换掉所选 1 张/)
    if (mobile) await first.tap()
    else await first.click()
    assert.match(await panel.locator('.prompt-action-footer').innerText(), /已选 0 张\s*保留全部手牌/)
    if (mobile) await first.tap()
    else await first.click()
    const panelBox = await panel.boundingBox()
    insideViewport(panelBox, width, height, `${tag} mulligan panel`)
    await footerButton.scrollIntoViewIfNeeded()
    insideViewport(await footerButton.boundingBox(), width, height, `${tag} mulligan confirm`)
    await assertCenterHit(footerButton, `${tag} mulligan confirm`)
    await assertDocumentFits(page, `${tag} mulligan`)
    await page.screenshot({ path: path.join(out, `${tag}-mulligan.png`) })

    const minimize = panel.locator('.prompt-minimize')
    if (mobile) await minimize.tap()
    else await minimize.click()
    const expand = page.locator('.prompt-minimized-bar button')
    await expand.waitFor()
    await assertVisibleSummary(mobile ? expand : page.locator('.prompt-minimized-task'),
      mobile ? /已选\s*1张/ : /已选 1 张待换/, `${tag} minimized task`)
    await page.screenshot({ path: path.join(out, `${tag}-minimized.png`) })
    insideViewport(await page.locator('.l12-prompt-overlay.minimized').boundingBox(), width, height, `${tag} minimized panel`)
    const expandBox = await expand.boundingBox()
    insideViewport(expandBox, width, height, `${tag} expand`)
    await assertCenterHit(expand, `${tag} expand`)
    if (mobile) {
      await assertRenderedContentFits(expand, `${tag} minimized task`)
      const minimizedClock = page.locator('.prompt-minimized-bar .setup-decision-clock:visible')
      await minimizedClock.waitFor()
      insideViewport(await minimizedClock.boundingBox(), width, height, `${tag} minimized setup clock`)
      await assertRenderedContentFits(minimizedClock.locator('strong'), `${tag} minimized setup clock text`)
    }
    await assertDocumentFits(page, `${tag} minimized`)
    if (mobile) assert(expandBox.width >= 44 && expandBox.height >= 44, `${tag}: expand hit area below 44px`)
    if (mobile) await expand.tap()
    else await expand.click()
    await panel.waitFor()
    assert(await minimize.evaluate(node => document.activeElement === node), `${tag}: focus not restored`)

    await page.evaluate(() => window.__resetSentCommands())
    if (mobile) await footerButton.tap()
    else await footerButton.click()
    sent = await page.evaluate(() => window.__sentCommands.at(-1))
    assert.equal(sent?.command?.type, 'mulligan', `${tag}: selected mulligan command missing`)
    assert.equal(sent?.command?.cardInstanceIds?.length, 1, `${tag}: selected mulligan payload must contain one card`)
    await page.evaluate(() => {
      window.__resetSentCommands()
      const state = window.__l12State
      state.game.players[0].mulliganDone = true
      state.rankedClock.players[0].acting = false
      state.rankedClock.players[1].acting = true
      state.rankedClock.receivedAtMs = Date.now()
    })
    await panel.waitFor({ state: 'hidden' })
    const waiting = page.locator('.waiting-panel:visible')
    await waiting.waitFor()
    assert.match(await waiting.innerText(), /已确认调度，等待对手/)
    assert.match(await waiting.locator('.setup-decision-clock').innerText(), /后攻玩家准备.*本次剩余/)
    await page.evaluate(() => window.__clockFixture.disconnect(1, 119000))
    assert.match(await waiting.locator('.setup-decision-clock').innerText(), /断线时继续计时/)
    await page.screenshot({ path: path.join(out, `${tag}-waiting-disconnected.png`) })

    await page.evaluate(() => {
      window.__clockFixture.reconnect(1)
      window.__clockFixture.setPhase('Main', 0)
      const players = window.__l12State.rankedClock.players
      players[0].acting = false
      players[1].acting = true
    })
    await waiting.waitFor({ state: 'hidden' })
    const clocks = page.locator('.player-turn-clock:visible')
    assert(await clocks.count() >= 2, `${tag}: both player clocks missing`)
    const clockText = (await clocks.allInnerTexts()).join(' ')
    assert.match(clockText, /对手行动中[\s\S]*总时剩余[\s\S]*本次剩余/)
    assert.match(clockText, /我方等待行动/)
    for (const clock of await clocks.all()) {
      insideViewport(await clock.boundingBox(), width, height, `${tag} player clock`)
      const clipping = await clock.evaluate(node => {
        const parent = node.getBoundingClientRect()
        return [...node.querySelectorAll(':scope > strong,:scope > span')].flatMap(child => {
          const rect = child.getBoundingClientRect()
          const clipped = rect.left < parent.left - 1 || rect.top < parent.top - 1
            || rect.right > parent.right + 1 || rect.bottom > parent.bottom + 1
          return clipped ? [{ tag: child.tagName, text: child.textContent?.trim(),
            parent: { left: parent.left, top: parent.top, right: parent.right, bottom: parent.bottom },
            rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom } }] : []
        })
      })
      assert.equal(clipping.length, 0, `${tag}: player clock text is clipped ${JSON.stringify(clipping)}`)
    }
    await assertDocumentFits(page, `${tag} player clocks`)
    await page.evaluate(() => window.__clockFixture.disconnect(0, 119000))
    const disconnectedText = (await clocks.allInnerTexts()).join(' ')
    assert.match(disconnectedText, /重连剩余/)

    await page.evaluate(() => {
      const now = new Date()
      const deadline = new Date(now.getTime() + 5000)
      window.__l12State.game.prompts = [{
        promptId: 'stage2a-auto-close', playerIndex: 0, kind: 'option', text: '选择 1 个合法响应目标',
        validChoices: ['yes', 'no'], minChoose: 1, maxChoose: 1,
        choiceLabels: { yes: '发动', no: '不发动' }, createdRevision: 1, controller: 0,
        autoClose: { deadlineUtc: deadline.toISOString(), serverNowUtc: now.toISOString() },
      }]
    })
    const genericPanel = page.locator('.prompt-panel:visible').filter({ hasText: '选择 1 个合法响应目标' })
    await genericPanel.waitFor()
    const genericMinimize = genericPanel.locator('.prompt-minimize')
    if (mobile) await genericMinimize.tap()
    else await genericMinimize.click()
    const genericExpand = page.locator('.prompt-minimized-bar button:visible')
    await genericExpand.waitFor()
    await assertVisibleSummary(mobile ? genericExpand : page.locator('.prompt-minimized-task'),
      mobile ? /选择\s*1项/ : /需选择 1 项/, `${tag} generic minimized task`)
    const timer = page.locator('.prompt-minimized-bar .prompt-auto-close:visible')
    await timer.waitFor()
    assert.match(await timer.innerText(), mobile ? /^\d+秒$/ : /秒后自动关闭/)
    insideViewport(await timer.boundingBox(), width, height, `${tag} minimized auto-close timer`)
    insideViewport(await genericExpand.boundingBox(), width, height, `${tag} generic expand`)
    await assertCenterHit(genericExpand, `${tag} generic expand`)
    if (mobile) {
      await assertRenderedContentFits(genericExpand, `${tag} generic minimized task`)
      await assertRenderedContentFits(timer, `${tag} minimized auto-close timer`)
    }
    await assertDocumentFits(page, `${tag} generic minimized`)
    if (mobile) await genericExpand.tap()
    else await genericExpand.click()
    await genericPanel.waitFor()
    if (mobile) {
      await page.evaluate(() => {
        const prompt = window.__l12State.game.prompts[0]
        prompt.minChoose = 0
        prompt.maxChoose = 2
      })
      await genericMinimize.tap()
      await assertVisibleSummary(genericExpand, /至多\s*2项/, `${tag} optional-range minimized task`)
      await assertRenderedContentFits(genericExpand, `${tag} optional-range minimized task`)
      await genericExpand.tap()
      await genericPanel.waitFor()
      await page.evaluate(() => {
        const prompt = window.__l12State.game.prompts[0]
        prompt.minChoose = 0
        prompt.maxChoose = 0
      })
      await genericMinimize.tap()
      await assertVisibleSummary(genericExpand, /确认\s*信息/, `${tag} information-confirm minimized task`)
      await assertRenderedContentFits(genericExpand, `${tag} information-confirm minimized task`)
      await genericExpand.tap()
      await genericPanel.waitFor()
    }
    await page.evaluate(() => { window.__l12State.game.prompts = [] })
    assert.equal(errors.length, 0, `${tag}: page errors: ${errors.join(' / ')}`)
    results.push({ viewport: tag, mobile, selected: 1, waiting: true, disconnected: true, clocks: await clocks.count(), errors })
    await context.close()
  }
  fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify(results, null, 2))
  console.log(`Battle mulligan and clock stage 2A: ${results.length} viewports passed; evidence at ${out}`)
} finally {
  await browser.close()
}
