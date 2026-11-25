import fs from 'node:fs'
import { chromium } from 'playwright'

const BASE = 'https://legion-12.com/testrun'
const OUT = 'D:/GPT/Legion12/artifacts/a3-table-chat'
fs.mkdirSync(OUT, { recursive: true })

const password = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const deckCode = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-deckcode.txt', 'utf8').trim()
const deckName = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-deckname.txt', 'utf8').trim()

const evidence = { startedAt: new Date().toISOString(), deckName, steps: [] }
const note = (step, data) => evidence.steps.push({ step, ...data })
const bodyText = (page) => page.evaluate(() => document.body.innerText)

async function waitForText(page, needle, timeout = 25000) {
  await page.waitForFunction((value) => document.body.innerText.includes(value), needle, { timeout })
}

// 先等元素挂载，再在截止时间内反复点击；单次点击失败不再直接吃掉整轮。
async function clickButton(page, name, { exact = false, timeout = 25000 } = {}) {
  const target = page.getByRole('button', { name, exact })
  await target.first().waitFor({ state: 'attached', timeout })
  const deadline = Date.now() + timeout
  for (;;) {
    try { await target.first().click({ force: true, timeout: 5000 }); return }
    catch (error) {
      if (Date.now() > deadline) throw new Error('click failed: ' + name + ' :: ' + String(error).slice(0, 120))
      await page.waitForTimeout(700)
    }
  }
}

const browser = await chromium.launch()

async function login(name) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } })
  const page = await context.newPage()
  await page.goto(BASE + '/me', { waitUntil: 'domcontentloaded' })
  await page.fill('form input[autocomplete=username]', name)
  await page.fill('form input[type=password]', password)
  await page.click('form button[type=submit]')
  await page.waitForFunction(() => !document.querySelector('form input[type=password]'), { timeout: 25000 })
  return { context, page, name }
}

async function ensureSavedDeck(page, tag) {
  await page.goto(BASE + '/decks', { waitUntil: 'domcontentloaded' })
  await clickButton(page, '我的牌库', { exact: true })
  await page.waitForTimeout(1200)
  if ((await bodyText(page)).includes(deckName)) return { tag, already: true }
  await clickButton(page, '导入牌库码')
  await page.fill('input[placeholder*="牌库码"]', deckCode)
  await clickButton(page, '确认导入')
  await waitForText(page, deckName)
  return { tag, imported: true }
}

// 确保回到大厅（不在任何房间里）：大厅才同时具备「好友房」页签与「创建新房间」。
async function ensureLobby(page) {
  await page.goto(BASE + '/battle', { waitUntil: 'domcontentloaded' })
  for (let attempt = 0; attempt < 8; attempt++) {
    await page.waitForTimeout(1300)
    if (await page.getByRole('button', { name: '创建新房间' }).count()) return
    // 上一轮可能停在一局未结束的对局里：必须先投降才能离开（这正是残留房间的成因）。
    const surrender = page.getByRole('button', { name: '投降', exact: true })
    if (await surrender.count()) {
      try { await surrender.first().click({ force: true }) } catch { }
      await page.waitForTimeout(1500)
      for (const confirm of ['确认', '确定', '投降']) {
        const button = page.getByRole('button', { name: confirm, exact: true })
        if (await button.count() > 1 || (confirm !== '投降' && await button.count())) {
          try { await button.last().click({ force: true }) } catch { }
          break
        }
      }
      await page.waitForTimeout(2500)
    }
    for (const name of ['关闭房间并返回大厅', '离开房间并返回大厅']) {
      const leaving = page.getByRole('button', { name })
      if (await leaving.count()) { try { await leaving.first().click({ force: true }) } catch { } ; break }
    }
    const tab = page.getByRole('button', { name: '好友房' })
    if (await tab.count()) { try { await tab.first().click({ force: true }) } catch { } }
  }
  throw new Error('could not return to lobby')
}

async function openFriendPanel(page) {
  await ensureLobby(page)
  await page.waitForTimeout(600)
}

try {
  // 同一账号进过某房间后现场控件会变化，复测换号（台账 2026-11-25 已记）。
  const firstPlayer = process.env.L12_A3_P1 || '彩排01'
  const secondPlayer = process.env.L12_A3_P2 || '彩排02'
  const a = await login(firstPlayer)
  const b = await login(secondPlayer)
  if (!process.env.L12_SKIP_DECK_IMPORT) {
    const imports = []
    for (const [tag, page] of [['a', a.page], ['b', b.page]]) {
      try { imports.push(await ensureSavedDeck(page, tag)) }
      catch (error) { imports.push({ tag, error: String(error).slice(0, 120) }) }
    }
    note('deck-import', imports)
  }

  await openFriendPanel(a.page)
  // 建房后必须是空房：若还显示已加入的对手，说明又回到了旧现场，先退出重来。
  for (let attempt = 0; attempt < 3; attempt++) {
    await clickButton(a.page, '创建新房间')
    await waitForText(a.page, '复制房间码')
    if ((await bodyText(a.page)).includes('等待玩家')) break
    for (const name of ['关闭房间并返回大厅', '离开房间并返回大厅']) {
      const leaving = a.page.getByRole('button', { name })
      if (await leaving.count()) { try { await leaving.first().click({ force: true }) } catch { } ; break }
    }
    await openFriendPanel(a.page)
  }
  const code = await a.page.evaluate(() => {
    const button = Array.from(document.querySelectorAll('button')).find((item) => item.textContent.trim() === '复制房间码')
    if (!button) return ''
    const host = button.closest('section,article,div') || button.parentElement
    const found = (host?.innerText || '').match(/\b[A-Z0-9]{6}\b/g) || []
    return found[0] || ''
  })
  note('room', { code })
  if (!code) throw new Error('room code not found')

  await openFriendPanel(b.page)
  await b.page.fill('input[placeholder*="房间码"]', code)
  await clickButton(b.page, '加入对战')
  await waitForText(b.page, 'PLAYER 2')
  note('joined', { ok: true })

  for (const [tag, page] of [['a', a.page], ['b', b.page]]) {
    const slot = page.locator('text=点击封面选择牌库').first()
    if (await slot.count()) { try { await slot.click({ force: true }) } catch { } }
    await page.waitForTimeout(1600)
    const option = page.locator('[role=dialog]').locator('text=' + deckName).first()
    if (await option.count()) { try { await option.click({ force: true }) } catch { } }
    await page.waitForTimeout(600)
    if (await page.getByRole('button', { name: '确认使用' }).count()) {
      await clickButton(page, '确认使用')
    }
    await page.waitForTimeout(1200)
    note('room-deck-' + tag, { picked: (await bodyText(page)).includes(deckName) })
  }

  for (const [tag, page] of [['a', a.page], ['b', b.page]]) {
    const ready = page.getByRole('button', { name: '准备对战', exact: true })
    if (!(await ready.count())) { note('ready-' + tag, { missing: true }); continue }
    await ready.first().waitFor({ state: 'attached', timeout: 15000 })
    const wasDisabled = await ready.first().evaluate((el) => el.disabled)
    await clickButton(page, '准备对战', { exact: true })
    await page.waitForTimeout(2000)
    note('ready-' + tag, { wasDisabled })
  }

  await a.page.waitForFunction(() => !document.body.innerText.includes('友谊战整备室'), { timeout: 45000 })
  note('game-start', { started: true })
  await a.page.screenshot({ path: OUT + '/11-game-a.png' })

  for (const [tag, page] of [['a', a.page], ['b', b.page]]) {
    const opener = page.locator('button[aria-label="打开好友功能"]')
    await opener.first().waitFor({ state: 'attached', timeout: 20000 })
    const attempts = []
    for (let attempt = 0; attempt < 4; attempt++) {
      // 前两次用 Playwright 真实点击，后两次直接在 DOM 上触发 click：
      // 若 DOM 点击能开而真实点击打不开，说明按钮被别的层挡住了（可用性缺陷），不是逻辑没写。
      if (attempt < 1) await opener.first().click({ force: true })
      else await page.evaluate(() => document.querySelector('button[aria-label="打开好友功能"]')?.click())
      await page.waitForTimeout(1400)
      const state = await page.evaluate(() => ({
        open: !!document.querySelector('[data-l12-social-popover]'),
        expanded: document.querySelector('button[aria-label="打开好友功能"]')?.getAttribute('aria-expanded') ?? null,
      }))
      attempts.push(state)
      if (state.open) break
    }
    note('popover-attempts-' + tag, attempts)
    const popover = await page.evaluate(() => {
      const host = document.querySelector('[data-l12-social-popover]')
      return {
        open: !!host,
        tabs: host ? Array.from(host.querySelectorAll('nav button')).map((item) => item.textContent.trim()) : [],
      }
    })
    note('popover-' + tag, popover)
    // 记录按钮中心点到底压着什么：真实点击被吞、DOM 点击有效，说明有元素盖在按钮上。
    const covering = await page.evaluate(() => {
      const button = document.querySelector('button[aria-label="打开好友功能"]')
      if (!button) return { found: false }
      const box = button.getBoundingClientRect()
      const x = box.left + box.width / 2
      const y = box.top + box.height / 2
      const top = document.elementFromPoint(x, y)
      return {
        found: true,
        rect: [Math.round(box.left), Math.round(box.top), Math.round(box.width), Math.round(box.height)],
        topTag: top?.tagName ?? null,
        topClass: (top?.className || '').toString().slice(0, 80),
        isSelf: top === button || button.contains(top),
      }
    })
    note('covering-' + tag, covering)
    const tab = page.getByRole('button', { name: '本桌', exact: true })
    const hasTab = await tab.count()
    if (hasTab) { await tab.first().click({ force: true }); await page.waitForTimeout(1800) }
    note('table-tab-' + tag, { hasTab })
    await page.screenshot({ path: OUT + '/20-table-tab-' + tag + '.png' })
    const chatUi = await page.evaluate(() => {
      const host = document.querySelector('[data-l12-social-popover]')
      return host ? { inputs: host.querySelectorAll('input,textarea').length, text: host.innerText.slice(0, 120) } : null
    })
    note('chat-ui-' + tag, chatUi)
  }
  const token = 'TBL-' + Date.now().toString(36)
  const aPopover = a.page.locator('[data-l12-social-popover]')
  await aPopover.locator('textarea').first().fill(token)
  await aPopover.getByRole('button', { name: '发送', exact: true }).click({ force: true })
  await a.page.waitForTimeout(2500)
  note('sent', { token })

  const deliveredToB = await b.page
    .waitForFunction((value) => document.body.innerText.includes(value), token, { timeout: 25000 })
    .then(() => true).catch(() => false)
  note('received-by-b', { deliveredToB })
  await b.page.screenshot({ path: OUT + '/21-received-b.png' })
  await a.page.screenshot({ path: OUT + '/21-sent-a.png' })

  evidence.ok = evidence.steps.filter((item) => item.step.startsWith('table-tab')).every((item) => item.hasTab)
    && deliveredToB
} catch (error) {
  evidence.error = String(error).slice(0, 500)
} finally {
  fs.writeFileSync(OUT + '/evidence.json', JSON.stringify(evidence, null, 2))
  await browser.close()
}
