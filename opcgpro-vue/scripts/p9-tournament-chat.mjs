import fs from 'node:fs'
import { chromium } from 'playwright'

// 清单P9：赛事房内选手↔选手聊天真实往返（此前只有服务端扇出单测与本桌页签存在性证据）。
// 前置由 b1-tournament-prep.mjs 建立：赛事 code + 正在进行的桌次 roomCode。
const BASE = 'https://legion-12.com/testrun'
const OUT = 'D:/GPT/Legion12/artifacts/p9-tournament-chat'
fs.mkdirSync(OUT, { recursive: true })
const password = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const prep = JSON.parse(fs.readFileSync('D:/GPT/Legion12/artifacts/a3-clear-20261126/b1-prep.json', 'utf8'))
const tournamentCode = prep.code
const evidence = { startedAt: new Date().toISOString(), tournamentCode, match: prep.match, steps: [] }
const note = (step, data) => evidence.steps.push({ step, at: new Date().toISOString(), ...data })
const bodyText = (page) => page.evaluate(() => document.body.innerText.replace(/\s+/g, ' '))
const browser = await chromium.launch()

async function login(name) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const page = await context.newPage()
  await page.goto(BASE + '/me', { waitUntil: 'domcontentloaded' })
  await page.fill('form input[autocomplete=username]', name)
  await page.fill('form input[type=password]', password)
  await page.click('form button[type=submit]')
  await page.waitForFunction(() => !document.querySelector('form input[type=password]'), { timeout: 30000 })
  return page
}
const clickIf = async (page, name, exact = true) => {
  const button = page.getByRole('button', { name, exact })
  if (!(await button.count())) return false
  try { await button.first().click({ force: true }); return true } catch { return false }
}

try {
  const a = await login('彩排01')
  const b = await login('彩排02')
  for (const [tag, page] of [['a', a], ['b', b]]) {
    await page.goto(BASE + '/battle/tournaments/' + tournamentCode, { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(6000)
    const tabs = await page.evaluate(() => Array.from(document.querySelectorAll('button')).map(b => (b.textContent || '').trim()).filter(Boolean))
    note('detail-tabs-' + tag, { tabs: tabs.slice(0, 16) })
    if (!(await clickIf(page, '赛程'))) await clickIf(page, '赛程与桌次')
    await page.waitForTimeout(2500)
    const entered = await clickIf(page, '进入对局')
    note('enter-room-' + tag, { entered })
    await page.waitForTimeout(8000)
    note('after-enter-' + tag, { url: page.url(), text: (await bodyText(page)).slice(0, 160) })
    await page.screenshot({ path: OUT + '/10-entered-' + tag + '.png' })
  }
  evidence.gameUrls = [a.url(), b.url()]

  // 先等到真正开局：A 先进房时只显示「等待对手」，此时没有 matchId，本桌页签按设计不出现。
  for (const [tag, page] of [['a', a], ['b', b]]) {
    const started = await page.waitForFunction(() => document.body.innerText.includes('投降'), null, { timeout: 90000 })
      .then(() => true).catch(() => false)
    note('match-started-' + tag, { started, url: page.url() })
  }
  // 开局后的先后攻提示层会盖住顶栏（2026-11-25 的教训），先回答再开弹层。
  for (const [tag, page] of [['a', a], ['b', b]]) {
    const acted = []
    if (await clickIf(page, '跳过演出')) acted.push('skip')
    for (const choice of ['选择先攻', '选择后攻']) { if (await clickIf(page, choice)) { acted.push(choice); break } }
    note('answer-init' + tag, { acted })
    await page.waitForTimeout(2500)
  }

  // 本桌频道：弹层 → 本桌页签 → 发送 → 对端收取
  const token = 'P9-' + Date.now().toString(36)
  for (const [tag, page] of [['a', a], ['b', b]]) {
    const opener = page.locator('button[aria-label="打开好友功能"]')
    await opener.first().waitFor({ state: 'attached', timeout: 20000 })
    for (let attempt = 0; attempt < 4; attempt++) {
      if (attempt < 2) { try { await opener.first().click({ force: true }) } catch { } }
      else { try { await page.evaluate(() => document.querySelector('button[aria-label="打开好友功能"]')?.click()) } catch { } }
      await page.waitForTimeout(1400)
      if (await page.locator('[data-l12-social-popover]').count()) break
    }
    note('popover-' + tag, { open: await page.locator('[data-l12-social-popover]').count(),
      tabs: await page.evaluate(() => Array.from(document.querySelectorAll('[data-l12-social-popover] nav button')).map(b => (b.textContent || '').trim())) })
    const hasTab = await page.getByRole('button', { name: '本桌', exact: true }).count()
    if (hasTab) { await page.getByRole('button', { name: '本桌', exact: true }).first().click({ force: true }); await page.waitForTimeout(1500) }
    note('table-tab-' + tag, { hasTab })
  }
  const popover = a.locator('[data-l12-social-popover]')
  const openerA = a.locator('button[aria-label="打开好友功能"]')
  if (!(await popover.count())) { try { await openerA.first().click({ force: true }) } catch { } ; await a.waitForTimeout(1500) }
  if (await a.getByRole('button', { name: '本桌', exact: true }).count()) {
    await a.getByRole('button', { name: '本桌', exact: true }).first().click({ force: true })
    await a.waitForTimeout(1500)
  }
  note('composer', { popover: await popover.count(), textarea: await popover.locator('textarea').count() })
  await popover.locator('textarea').first().fill(token)
  await popover.getByRole('button', { name: '发送', exact: true }).click({ force: true })
  await a.waitForTimeout(1200)
  const deliveredToB = await b.waitForFunction((value) => document.body.innerText.includes(value), token, { timeout: 25000 })
    .then(() => true).catch(() => false)
  note('sent', { token, deliveredToB })
  await a.screenshot({ path: OUT + '/20-sent-a.png' })
  await b.screenshot({ path: OUT + '/21-received-b.png' })
  if (!deliveredToB) {
    note('b-text', { text: (await bodyText(b)).slice(0, 300) })
  }
  evidence.ok = deliveredToB
} catch (error) {
  evidence.error = String(error).slice(0, 600)
} finally {
  fs.writeFileSync(OUT + '/evidence.json', JSON.stringify(evidence, null, 2))
  await browser.close()
}