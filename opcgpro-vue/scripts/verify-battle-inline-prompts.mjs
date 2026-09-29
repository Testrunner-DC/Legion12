import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const base = process.argv[2] || 'http://127.0.0.1:5193/__l12_battle_preview__'
const viewports = [[320,568],[360,800],[390,844],[430,932],[568,320],[667,375],[844,390],[932,430]]
const screenshotOut = process.env.L12_INLINE_PROMPT_OUT
if (screenshotOut) fs.mkdirSync(screenshotOut, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
let passed = 0

async function open(page, query, mobile = true) {
  await page.goto(`${base}?${mobile ? 'mobile=1&canvas=1&' : ''}${query}`, { waitUntil: 'domcontentloaded' })
  await page.locator('.inline-prompt-controls').waitFor()
}
async function checkBounds(page, selector, width, height) {
  const bounds = await page.locator(selector).boundingBox()
  assert(bounds && bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= width + 1 && bounds.y + bounds.height <= height + 1,
    `${selector} within ${width}x${height}: ${JSON.stringify(bounds)}`)
}
async function sentChoice(page) {
  return page.evaluate(() => window.__sentCommands?.at(-1)?.command)
}

try {
  const context = await browser.newContext()
  const page = await context.newPage()
  page.setDefaultTimeout(10000)
  for (const [width, height] of viewports) {
    await page.setViewportSize({ width, height })
    await open(page, 'field=full&board-target=1&inline-rich=1')
    const targetBar = page.locator('.inline-prompt-controls')
    await checkBounds(page, '.inline-prompt-controls', width, height)
    const targetText = await targetBar.innerText()
    assert(targetText.includes('指定效果目标'), `target ${width}: brief title`)
    assert(!targetText.includes('第1格'), `target ${width}: no numbered slot`)
    await targetBar.getByRole('button', { name: '任务说明' }).click()
    const targetInfo = page.locator('.inline-prompt-info-overlay')
    for (const expected of ['指定效果目标','当前操作：我方','需选择 1 至 2 项','〈同名测试军团〉','确认后']) assert((await targetInfo.innerText()).includes(expected), `target ${width}: ${expected}`)
    assert(await targetInfo.getByRole('button', { name: '返回选择' }).evaluate(element => document.activeElement === element), `target ${width}: focus enters sheet`)
    await checkBounds(page, '.inline-prompt-info-overlay', width, height)
    if (screenshotOut && (width === 320 || width === 568)) await page.screenshot({ path: path.join(screenshotOut, `target-info-${width}x${height}.png`) })
    await targetInfo.getByRole('button', { name: '返回选择' }).click()
    assert(await targetBar.getByRole('button', { name: '任务说明' }).evaluate(element => document.activeElement === element), `target ${width}: focus returns to task`)
    assert(await targetBar.getByRole('button', { name: '确认目标' }).isDisabled(), `target ${width}: selection required`)
    await page.locator('.my-half .formation-slot.targetable').first().click()
    await targetBar.getByRole('button', { name: '任务说明' }).click()
    assert((await targetInfo.innerText()).includes('我方前排左格'), `target ${width}: selected position: ${await targetInfo.innerText()}`)
    await targetInfo.getByRole('button', { name: '返回选择' }).click()
    if (screenshotOut && (width === 320 || width === 568)) await page.screenshot({ path: path.join(screenshotOut, `target-${width}x${height}.png`) })
    assert(await targetBar.getByRole('button', { name: '确认目标' }).isEnabled(), `target ${width}: can submit`)
    if (await targetBar.getByRole('button', { name: '最小化' }).count()) {
      await targetBar.getByRole('button', { name: '最小化' }).click()
      const restore = page.locator('.board-control-restore')
      assert((await restore.getAttribute('aria-label')).includes('需选择 1 至 2 项'), `target ${width}: task retained`)
      await restore.click()
      assert((await targetBar.innerText()).includes('已选 1/2'), `target ${width}: selection retained`)
    }
    await targetBar.getByRole('button', { name: '确认目标' }).click()
    assert.equal((await sentChoice(page))?.type, 'resolvePrompt', `target ${width}: submitted`)
    passed++

    await open(page, 'field=empty&board-slot=1&slot-opponent=1&inline-rich=1')
    await checkBounds(page, '.inline-prompt-controls', width, height)
    await page.locator('.inline-prompt-controls').getByRole('button', { name: '任务说明' }).click()
    const slotText = await page.locator('.inline-prompt-info-overlay').innerText()
    for (const expected of ['选择登场格位','对方前排中格','对方后排右格','即提交选择']) assert(slotText.includes(expected), `slot ${width}: ${expected}`)
    assert(!slotText.includes('第2格'), `slot ${width}: no numbered slot`)
    await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
    await page.locator('.formation-slot.available').first().click()
    assert.equal((await sentChoice(page))?.type, 'resolvePrompt', `slot ${width}: submitted`)
    passed++

    await open(page, 'morale=5&morale-payment=1&payment-actions=1&inline-rich=1')
    await checkBounds(page, '.inline-prompt-controls', width, height)
    const resourceBar = page.locator('.inline-prompt-controls')
    await resourceBar.getByRole('button', { name: '任务说明' }).click()
    const resourceText = await page.locator('.inline-prompt-info-overlay').innerText()
    for (const expected of ['支付士气','需选择 2 项','已选择 0/2','待支付：2枚士气','确认后']) assert(resourceText.includes(expected), `resource ${width}: ${expected}`)
    await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
    assert(await resourceBar.getByRole('button', { name: '确认支付' }).isDisabled(), `resource ${width}: payment required`)
    await resourceBar.getByRole('button', { name: '不发动' }).click()
    assert.deepEqual((await sentChoice(page))?.cardInstanceIds, ['skip'], `resource ${width}: skip`)
    passed++
  }
  await page.setViewportSize({ width: 667, height: 375 })
  await open(page, 'field=full&board-target=1&inline-rich=1&inline-long=1')
  await page.locator('.inline-prompt-controls').getByRole('button', { name: '任务说明' }).click()
  const longBody = page.locator('.inline-prompt-info-body')
  assert(await longBody.evaluate(element => element.scrollHeight > element.clientHeight), 'long task explanation scrolls inside sheet')
  await longBody.evaluate(element => { element.scrollTop = element.scrollHeight })
  const returnLobby = page.getByRole('button', { name: '返回大厅' })
  assert(await returnLobby.evaluate(element => { const box = element.getBoundingClientRect(); return element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)) }), 'return lobby stays reachable while task sheet is open')
  await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
  passed++
  await open(page, 'morale=5&morale-payment=1&payment-return=1&inline-rich=1')
  assert((await page.locator('.inline-prompt-controls').innerText()).includes('返还士气'), 'return is distinct from payment')
  passed++
  await open(page, 'morale=5&morale-payment=1&payment-actions=1&inline-rich=1')
  await page.locator('.my-half .resource-morale-summary').click()
  const picker = page.locator('.mobile-morale-overlay')
  await picker.waitFor()
  const pickerText = await picker.innerText()
  for (const expected of ['支付士气','需选择 2 项','待支付：2枚士气','确认后']) assert(pickerText.includes(expected), `morale picker: ${expected}`)
  if (screenshotOut) await page.screenshot({ path: path.join(screenshotOut, 'morale-picker-667x375.png') })
  await picker.locator('.mobile-morale-choice[aria-disabled="false"]').first().click()
  assert((await picker.innerText()).includes('已选择 1/2'), 'morale picker selected count')
  await picker.getByRole('button', { name: '最小化' }).click()
  const moraleRestore = page.locator('.mobile-morale-restore')
  assert((await moraleRestore.innerText()).includes('需选择 2 项'), 'morale minimized task retained')
  await moraleRestore.click()
  assert((await picker.innerText()).includes('已选择 1/2'), 'morale picker selection retained')
  await picker.getByRole('button', { name: '取消打出' }).click()
  assert.deepEqual((await sentChoice(page))?.cardInstanceIds, ['cancel'], 'morale cancel')
  passed++
  await open(page, 'morale=5&morale-payment=1&inline-rich=1')
  await page.locator('.my-half .resource-morale-summary').click()
  const payable = page.locator('.mobile-morale-overlay .mobile-morale-choice[aria-disabled="false"]')
  await payable.nth(0).click()
  await payable.nth(1).click()
  assert((await page.locator('.mobile-morale-overlay').innerText()).includes('已选择 2/2'), 'two resources selected')
  await page.locator('.mobile-morale-overlay').getByRole('button', { name: '确认支付' }).click()
  assert.equal((await sentChoice(page))?.cardInstanceIds?.length, 2, 'resource payment submits selected IDs')
  passed++
  await open(page, 'field=full&board-target=1&target-mixed=1&target-locked=1&morale=5&inline-rich=1')
  const mixedBar = page.locator('.inline-prompt-controls')
  await mixedBar.getByRole('button', { name: '任务说明' }).click()
  assert((await page.locator('.inline-prompt-info-overlay').innerText()).includes('待支付：1至2个战场对象'), 'mixed payment status')
  assert((await page.locator('.inline-prompt-info-overlay').innerText()).includes('我方前排左格'), 'locked candidate is named')
  await page.locator('.inline-prompt-info-overlay').getByRole('button', { name: '返回选择' }).click()
  await page.locator('.my-half .formation-slot.targetable').first().click()
  assert((await mixedBar.innerText()).includes('已选 1/2'), 'locked candidate cannot be deselected')
  await mixedBar.getByRole('button', { name: '确认费用' }).click()
  assert.equal((await sentChoice(page))?.type, 'resolvePrompt', 'mixed board payment submitted')
  passed++
  for (const [width, height] of [[568,320],[390,844]]) {
    await page.setViewportSize({ width, height })
    await open(page, 'field=full&morale=5&board-target=1&target-mixed-real=1&inline-rich=1')
    const ids = await page.evaluate(() => ({ legion: window.__l12State.game.players[0].field[0][0].instanceId,
      morale: window.__l12State.game.players[0].morale.slice(0,2).map(item => item.instanceId) }))
    const bar = page.locator('.inline-prompt-controls')
    assert(await bar.getByRole('button', { name: '确认费用' }).isDisabled(), `mixed ${width}: no premature submit`)
    await page.locator('.my-half .formation-slot.targetable').first().click()
    assert((await bar.innerText()).includes('已选 1/2'), `mixed ${width}: legion selected`)
    await page.locator('.my-half .resource-morale-summary').click()
    const drawer = page.locator('.mobile-morale-overlay')
    await drawer.waitFor()
    assert((await drawer.innerText()).includes('选择士气'), `mixed ${width}: drawer is interactive`)
    const resources = drawer.locator('.mobile-morale-choice[aria-disabled="false"]')
    assert.equal(await resources.count(), 2, `mixed ${width}: two legal resources`)
    await resources.first().click()
    assert((await drawer.innerText()).includes('已选择 2/2'), `mixed ${width}: merged count`)
    assert(await drawer.getByRole('button', { name: '确认费用' }).isEnabled(), `mixed ${width}: submit enabled`)
    if (screenshotOut) await page.screenshot({ path: path.join(screenshotOut, `mixed-manual-${width}x${height}.png`) })
    assert.equal(await page.evaluate(() => window.__sentCommands?.length ?? 0), 0, `mixed ${width}: no split pre-submit`)
    await drawer.getByRole('button', { name: '确认费用' }).click()
    const submitted = await sentChoice(page)
    assert.equal(submitted?.type, 'resolvePrompt', `mixed ${width}: one board-target command`)
    assert.deepEqual(new Set(submitted.cardInstanceIds), new Set([ids.legion,ids.morale[0]]), `mixed ${width}: legion and morale submitted together`)
    assert.equal(await page.evaluate(() => window.__sentCommands?.length ?? 0), 1, `mixed ${width}: exactly one command`)
    passed++

    await open(page, 'field=full&morale=5&board-target=1&target-mixed-real=1&target-mixed-unique=1&inline-rich=1')
    const unique = await page.evaluate(() => ({ legion: window.__l12State.game.players[0].field[0][0].instanceId,
      morale: window.__l12State.game.players[0].morale[0].instanceId }))
    const lockedBar = page.locator('.inline-prompt-controls')
    assert((await lockedBar.innerText()).includes('已选 1/2'), `unique ${width}: resource preselected`)
    await page.locator('.my-half .resource-morale-summary').click()
    const lockedDrawer = page.locator('.mobile-morale-overlay')
    const lockedResource = lockedDrawer.locator('.mobile-morale-choice[aria-disabled="false"]').first()
    assert.equal(await lockedResource.getAttribute('aria-pressed'), 'true', `unique ${width}: lock visible`)
    await lockedResource.click()
    assert.equal(await lockedResource.getAttribute('aria-pressed'), 'true', `unique ${width}: cannot deselect lock`)
    await lockedDrawer.getByRole('button', { name: '返回对局' }).click()
    await page.locator('.my-half .formation-slot.targetable').first().click()
    assert(await lockedBar.getByRole('button', { name: '确认费用' }).isEnabled(), `unique ${width}: board submit enabled`)
    await page.locator('.my-half .resource-morale-summary').click()
    assert(await lockedDrawer.getByRole('button', { name: '确认费用' }).isEnabled(), `unique ${width}: drawer submit enabled`)
    if (screenshotOut) await page.screenshot({ path: path.join(screenshotOut, `mixed-locked-${width}x${height}.png`) })
    await lockedDrawer.getByRole('button', { name: '确认费用' }).click()
    assert.deepEqual(new Set((await sentChoice(page)).cardInstanceIds), new Set([unique.legion,unique.morale]), `unique ${width}: one merged submission`)
    passed++

    await open(page, 'field=full&morale=5&board-target=1&target-mixed-real=1&target-mixed-cancel=1&inline-rich=1')
    await page.locator('.my-half .resource-morale-summary').click()
    const exitDrawer = page.locator('.mobile-morale-overlay')
    await exitDrawer.getByRole('button', { name: '取消选择' }).click()
    assert.deepEqual((await sentChoice(page))?.cardInstanceIds, ['cancel'], `mixed ${width}: cancel uses current prompt`)
    await open(page, 'field=full&morale=5&board-target=1&target-mixed-real=1&inline-rich=1')
    await page.locator('.my-half .resource-morale-summary').click()
    await page.locator('.mobile-morale-overlay').getByRole('button', { name: '不发动' }).click()
    assert.deepEqual((await sentChoice(page))?.cardInstanceIds, ['skip'], `mixed ${width}: skip uses current prompt`)
    passed++
  }
  await page.setViewportSize({ width: 1440, height: 810 })
  await open(page, 'field=full&board-target=1&inline-rich=1', false)
  await checkBounds(page, '.inline-prompt-controls', 1440, 810)
  assert((await page.locator('.inline-prompt-controls').innerText()).includes('指定效果目标'), 'desktop prompt title')
  passed++
  console.log(JSON.stringify({ status: 'passed', cases: passed, viewports }))
} finally {
  await browser.close()
}
