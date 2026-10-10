import assert from 'node:assert/strict'
import { mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5261/__l12_battle_preview__'
const output = process.env.L12_STAGE4B1_SCREENSHOT_DIR
  || 'C:/Users/neptu/Documents/ChatGPT/Legion12/.cache/stage4b1-browser-20260930'
mkdirSync(output, { recursive: true })

const viewports = [
  { width: 1440, height: 900 },
  { width: 390, height: 844 },
  { width: 568, height: 320 },
]
let assertions = 0
function check(condition, message) { assertions++; assert(condition, message) }

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const results = []
try {
  for (const viewport of viewports) {
    const page = await browser.newPage({ viewport })
    await page.route('**/favicon.ico', route => route.fulfill({ status: 204, body: '' }))
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('console', message => { if (message.type() === 'error') errors.push(`${message.text()} @ ${message.location().url}`) })
    try {
      const mobile = viewport.width < 600
      await page.goto(`${target}?${mobile ? 'mobile=1&canvas=1&' : ''}hand=6`,
        { waitUntil: 'domcontentloaded' })
      await page.locator('.my-half').waitFor()
      await page.evaluate(() => {
        const card = (id, name = '同名军团') => ({
          instanceId: id, cardId: `test-${id}`, name, cardType: 'legion', faction: '测试',
          cost: 1, baseTroops: 3000, troops: 3000, disasterLevel: 0, hidden: false,
        })
        const a = card('stage4b1-a')
        const b = card('stage4b1-b')
        const c = card('stage4b1-c')
        const puppet = card('stage4b1-puppet', '戏法师的傀儡')
        const event = (sequence, type, cards, playerIndex, playerCombat) => ({
          sequence, type, cards, playerIndex, playerCombat,
          text: '后台错误：绝密手牌实例 private-instance-7',
        })
        const game = window.__l12State.game
        game.you = 0
        game.players[0].name = '恶意昵称甲'
        game.players[1].name = '恶意昵称乙'
        game.recentEvents = [
          event(100, 'attack', [a, b], 0),
          event(101, 'attack', [a, b], 0, {
            combatId: 'battle-zero', eventKind: 'attack', outcomeCode: 'declared',
            attackerTroops: 0, defenderTroops: 0,
          }),
          event(102, 'attack', [a, c], 0, {
            combatId: 'battle-support', eventKind: 'attack', outcomeCode: 'declared',
          }),
          event(103, 'combat', [a, b], 0, {
            combatId: 'battle-zero', eventKind: 'combat', outcomeCode: 'defeated',
            targetInstanceId: b.instanceId, defenderTroops: 0,
          }),
          event(104, 'support', [card('stage4b1-support'), c, a], 1, {
            combatId: 'battle-support', eventKind: 'support', outcomeCode: 'supported',
            attackerInstanceId: a.instanceId, targetInstanceId: c.instanceId,
          }),
          event(105, 'attack', [a, b], 0, {
            combatId: 'battle-abort', eventKind: 'attack', outcomeCode: 'declared',
          }),
          event(106, 'attack-aborted', [], 0, {
            combatId: 'battle-abort', eventKind: 'attack-aborted', outcomeCode: 'aborted',
            publicReasonCode: 'target-left',
          }),
          event(107, 'attack', [a, c], 0, {
            combatId: 'battle-invalid', eventKind: 'attack', outcomeCode: 'declared',
          }),
          event(108, 'defense-invalid', [], 1, {
            combatId: 'battle-invalid', eventKind: 'defense-invalid', outcomeCode: 'invalid-support',
            publicReasonCode: 'extra-cost-unpaid', targetInstanceId: 'private-instance-7',
          }),
          event(109, 'attack', [a], 0, {
            combatId: 'battle-zero-damage', eventKind: 'attack', outcomeCode: 'declared',
          }),
          event(110, 'defense', [], 1, {
            combatId: 'battle-zero-damage', eventKind: 'defense', outcomeCode: 'unblocked',
            masterDamage: 0,
          }),
          event(111, 'attack', [a], 0, {
            combatId: 'battle-puppet', eventKind: 'attack', outcomeCode: 'declared',
            attackerInstanceId: a.instanceId,
          }),
          event(112, 'combat', [a, puppet], 0, {
            combatId: 'battle-puppet', eventKind: 'combat', outcomeCode: 'defeated',
            targetInstanceId: puppet.instanceId, defenderTroops: 1000,
          }),
          { ...event(113, 'dice', [a], 0), text: '〈雷霆天怒〉掷骰结果为 1' },
          event(114, 'attack-ended', [a, c], 0, {
            combatId: 'battle-thunder', eventKind: 'attack-aborted', outcomeCode: 'aborted',
            publicReasonCode: 'thunder-roll-failed', attackerInstanceId: a.instanceId,
            targetInstanceId: c.instanceId, attackerTroops: 3000, defenderTroops: 3000,
          }),
          // Duplicate delivery is a real replay/reconnect shape.
          event(103, 'combat', [a, b], 0, {
            combatId: 'battle-zero', eventKind: 'combat', outcomeCode: 'defeated',
            targetInstanceId: b.instanceId, defenderTroops: 0,
          }),
        ]
      })
      const trigger = page.locator('.mobile-record-trigger:visible')
      if (await trigger.count()) await trigger.first().click()
      const log = page.locator('.battle-event-log:visible').first()
      await log.waitFor()
      await log.locator('.combat-summary[data-event-sequence="108"]').count()
      const old = log.locator('.combat-summary[data-event-sequence="100"]')
      const zero = log.locator('.combat-summary[data-event-sequence="101"]')
      const support = log.locator('.combat-summary[data-event-sequence="102"]')
      const abort = log.locator('.combat-summary[data-event-sequence="105"]')
      const invalid = log.locator('.combat-summary[data-event-sequence="107"]')
      const zeroDamage = log.locator('.combat-summary[data-event-sequence="109"]')
      const puppet = log.locator('.combat-summary[data-event-sequence="111"]')
      const thunder = log.locator('[data-event-sequence="114"]')
      check(await log.locator('.combat-summary').count() === 7, 'seven attacks must yield seven summaries')
      check((await old.innerText()).includes('战斗结果未记录'), 'old combat must have an honest result')
      check(await old.locator('.combat-cards b').count() === 0, 'old combat must hide missing numeric values')
      await old.locator('.combat-toggle').click()
      check(await old.locator('.combat-toggle').getAttribute('aria-expanded') === 'true',
        'legacy detail toggle must remain usable')
      check(await zero.locator('.combat-cards b').first().innerText() === '0',
        'authoritative zero must render as 0')
      check(await zero.locator('.combat-cards b').count() === 2
        && await zero.locator('.combat-cards b').nth(1).innerText() === '0',
      'authoritative zero defense value must render as 0')
      check((await zeroDamage.innerText()).includes('−0点'),
        'authoritative zero damage must render while missing damage stays hidden')
      check((await puppet.locator('.combat-cards').innerText()).includes('戏法师的傀儡')
        && !(await puppet.locator('.combat-cards').innerText()).includes('主宰'),
      'retargeted combat summary must show the actual puppet instead of the declared master')
      await puppet.locator('.combat-toggle').click()
      check((await puppet.locator('.combat-detail').innerText()).includes('戏法师的傀儡'),
        'retargeted combat detail must agree with the summary')
      check((await thunder.innerText()).includes('进攻中止')
        && (await thunder.innerText()).includes('雷霆天怒掷骰未满足进攻条件'),
      'low thunder roll must explain the aborted attack')
      check(await log.locator('[data-event-sequence="113"]').count() === 1,
        'the public dice roll must remain one independent record')
      check(await old.locator('.event-badge').count() === 0,
        'old combat without damage must not render a damage badge')
      check((await support.innerText()).includes('完成支援') && !(await support.innerText()).includes('完成抵挡'),
        'support summary must not be called a block')
      await support.locator('.combat-toggle').click()
      check((await support.locator('.combat-detail').innerText()).includes('对手完成支援'),
        'detail must use the current viewer perspective')
      await abort.locator('.combat-toggle').click()
      check((await abort.locator('.combat-detail').innerText()).includes('被进攻军团已离场'),
        'abort detail must show only the public reason')
      await invalid.locator('.combat-toggle').click()
      check((await invalid.locator('.combat-detail').innerText()).includes('未支付额外费用'),
        'invalid support detail must show its safe reason')
      const dom = await log.innerText()
      check(!/undefined|NaN|绝密手牌|private-instance-7|恶意昵称/.test(dom),
        'audit text, private instance and player names must not appear in the log')
      await page.evaluate(() => { window.__l12State.game.you = 1 })
      check((await support.locator('.combat-detail').innerText()).includes('你完成支援'),
        'opposite player view must use 你 for the defender')
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      check(overflow <= 2, `viewport must not overflow horizontally: ${overflow}px`)
      check(errors.length === 0, `browser errors: ${errors.join(' | ')}`)
      await thunder.scrollIntoViewIfNeeded()
      const screenshot = path.join(output, `combat-log-${viewport.width}x${viewport.height}.png`)
      await page.screenshot({ path: screenshot, fullPage: true })
      results.push({ viewport: `${viewport.width}x${viewport.height}`, overflow, screenshot, errors })
    } finally { await page.close() }
  }
  console.log(JSON.stringify({ assertions, results }, null, 2))
} finally { await browser.close() }
