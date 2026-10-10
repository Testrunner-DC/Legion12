import assert from 'node:assert/strict'
import { mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5261/__l12_battle_preview__'
const output = process.env.L12_STAGE4B2_SCREENSHOT_DIR
  || 'C:/Users/neptu/Documents/ChatGPT/Legion12/.cache/stage4b2-browser-20260930'
mkdirSync(output, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 568, height: 320 }]) {
    const page = await browser.newPage({ viewport })
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
    try {
      const mobile = viewport.width < 600
      await page.goto(`${target}?${mobile ? 'mobile=1&canvas=1&' : ''}hand=6`, { waitUntil: 'domcontentloaded' })
      await page.locator('.my-half').waitFor()
      await page.evaluate(() => {
        const card = (id, name) => ({ instanceId: id, cardId: `test-${id}`, name,
          cardType: 'legion', faction: '测试', cost: 1, baseTroops: 3000, troops: 3000, hidden: false })
        const a = card('move-a', '同名军团')
        const b = card('move-b', '同名军团')
        const fact = (id, fromRow, fromSlot, toRow, toSlot) => ({ instanceId: id,
          battlefieldPlayerIndex: 0, fromRow, fromSlot, toRow, toSlot })
        const game = window.__l12State.game
        game.you = 0
        game.recentEvents = [
          { sequence: 100, type: 'move', playerIndex: 0, cards: [a, b], text: '不使用原文位置',
            playerBattlefieldMovement: { facts: [fact('move-a', 0, 0, 1, 1), fact('move-b', 1, 1, 0, 0)] } },
          { sequence: 101, type: 'move', playerIndex: 1, cards: [a], text: '旧记录前排左格到后排右格' },
          { sequence: 102, type: 'move', playerIndex: 0, cards: [card('private', '秘密军团')],
            text: '不可显示秘密格位', playerBattlefieldMovement: { facts: [fact('missing', 0, 0, 1, 2)] } },
        ]
      })
      const trigger = page.locator('.mobile-record-trigger:visible')
      if (await trigger.count()) await trigger.first().click()
      const log = page.locator('.battle-event-log:visible').first()
      await log.waitFor()
      const swap = log.locator('[data-event-sequence="100"]')
      assert.equal(await swap.count(), 1)
      await swap.locator('.action-toggle').click()
      const detail = await swap.locator('.combat-detail').innerText()
      assert(detail.includes('从我方前排左格移动至我方后排中格'))
      assert(detail.includes('从我方后排中格移动至我方前排左格'))
      assert.equal(await swap.locator('.combat-detail .battle-event').count(), 2)
      assert(!(await log.innerText()).includes('秘密格位'))
      assert(!(await log.innerText()).includes('旧记录前排左格到后排右格'))
      await page.evaluate(() => { window.__l12State.game.you = 1 })
      assert((await swap.locator('.combat-detail').innerText()).includes('从对方前排左格移动至对方后排中格'))
      await page.evaluate(() => { window.__l12State.game.you = 0; window.__l12State.gmEnabled = true })
      assert((await swap.locator('.combat-detail').innerText()).includes('从下方前排左格移动至下方后排中格'),
        'neutral GM view must use board-relative labels')
      await page.evaluate(() => { window.__l12State.gmEnabled = false })
      assert((await swap.locator('.combat-detail').innerText()).includes('从我方前排左格移动至我方后排中格'),
        'player view must recover its relative labels')
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      assert(overflow <= 2, `horizontal overflow ${overflow}px`)
      assert.deepEqual(errors, [])
      const screenshot = path.join(output, `movement-log-${viewport.width}x${viewport.height}.png`)
      await page.screenshot({ path: screenshot, fullPage: true })
      results.push({ viewport: `${viewport.width}x${viewport.height}`, overflow, screenshot })
    } finally { await page.close() }
  }
  console.log(JSON.stringify(results, null, 2))
} finally { await browser.close() }
