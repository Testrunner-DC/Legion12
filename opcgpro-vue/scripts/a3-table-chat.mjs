import fs from 'node:fs'
import { chromium } from 'playwright'
const BASE = 'https://legion-12.com/testrun'
const OUT = 'D:/GPT/Legion12/artifacts/a3-table-chat'
fs.mkdirSync(OUT, { recursive: true })
const pw = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const evidence = { startedAt: new Date().toISOString(), steps: [] }
const note = (step, data) => evidence.steps.push({ step, ...data })
const browser = await chromium.launch()

async function player(name) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } })
  const page = await context.newPage()
  await page.goto(BASE + '/me', { waitUntil: 'domcontentloaded' })
  await page.fill('form input[autocomplete=username]', name)
  await page.fill('form input[type=password]', pw)
  await page.click('form button[type=submit]')
  await page.waitForFunction(() => !document.querySelector('form input[type=password]'), { timeout: 25000 })
  await page.goto(BASE + '/battle', { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)
  for (let i = 0; i < 3; i++) {
    try { await page.getByRole('button', { name: '好友房' }).click({ force: true, timeout: 8000 }); break }
    catch { await page.waitForTimeout(1200) }
  }
  await page.waitForTimeout(1200)
  return { context, page, name }
}
const bodyText = async (page) => page.evaluate(() => document.body.innerText)

try {
  const a = await player('彩排01')
  const close = a.page.getByRole('button', { name: '关闭房间并返回大厅' })
  if (await close.count()) { await close.first().click({ force: true }); await a.page.waitForTimeout(1200) }
  await a.page.getByRole('button', { name: '好友房' }).click({ force: true })
  await a.page.waitForTimeout(1000)
  await a.page.getByRole('button', { name: '创建新房间' }).click({ force: true })
  await a.page.waitForTimeout(2500)
  const lines = (await bodyText(a.page)).split('\n').map((s) => s.trim())
  const at = lines.indexOf('复制房间码')
  const code = at > 0 ? lines[at - 1] : ''
  note('room', { code })
  if (!code) throw new Error('no room code')

  const b = await player('彩排02')
  await b.page.fill('input[placeholder*="房间码"]', code)
  await b.page.getByRole('button', { name: '加入对战' }).click({ force: true })
  await b.page.waitForTimeout(3000)
  note('joined', { bHasPlayer2: (await bodyText(b.page)).includes('PLAYER 2') })
  await a.page.screenshot({ path: OUT + '/10-room-both.png' })

  // 探针：房间牌库槽为空导致「准备对战」disabled，必须先选房内牌库。
  const picker = {}
  for (const [tag, p] of [['a', a], ['b', b]]) {
    const slot = p.page.locator('text=点击封面选择牌库').first()
    const has = await slot.count()
    if (has) { try { await slot.click({ force: true }) } catch { } }
    await p.page.waitForTimeout(2000)
    const dump = await p.page.evaluate(() => ({
      buttons: [...new Set(Array.from(document.querySelectorAll('button')).filter(b => b.offsetParent).map(b => b.textContent.trim()).filter(t => t && t.length <= 24))],
      dialog: (document.querySelector('[role=dialog]')?.innerText || '').slice(0, 400),
    }))
    picker[tag] = { hasSlot: has, ...dump }
    await p.page.screenshot({ path: OUT + '/30-deck-picker-' + tag + '.png' })
  }
  note('deck-picker', picker)
  // 准备握手：逐个点击并回读状态，确认真的变成「已准备」而不是点击无效。
  const readyState = {}
  for (const [tag, p] of [['a', a], ['b', b]]) {
    const ready = p.page.getByRole('button', { name: '准备对战' })
    const count = await ready.count()
    if (count) {
      const info = await ready.first().evaluate((el) => ({ disabled: el.disabled, text: el.textContent.trim() }))
      await ready.first().click({ force: true })
      await p.page.waitForTimeout(2500)
      const after = await p.page.evaluate(() => document.body.innerText)
      readyState[tag] = { before: info, ready: after.includes('已准备'), cancelButton: after.includes('取消准备') }
    } else {
      readyState[tag] = { missing: true }
    }
  }
  note('ready', readyState)
  await a.page.waitForTimeout(6000)
  const inGame = (await bodyText(a.page)).includes('对局')
  note('game-start', { text: (await bodyText(a.page)).slice(0, 200) })
  await a.page.screenshot({ path: OUT + '/11-game-a.png' })
  await b.page.screenshot({ path: OUT + '/11-game-b.png' })

  // 打开「好友与聊天」并切到「本桌」
  const opened = []
  for (const [tag, p] of [['a', a], ['b', b]]) {
    const btn = p.page.locator('button[aria-label="打开好友功能"]')
    if (await btn.count()) { await btn.first().click({ force: true }); await p.page.waitForTimeout(1500) }
    const tab = p.page.getByRole('button', { name: '本桌', exact: true })
    const hasTab = await tab.count()
    if (hasTab) { await tab.first().click({ force: true }); await p.page.waitForTimeout(1500) }
    opened.push({ tag, hasTab })
    await p.page.screenshot({ path: OUT + '/20-table-tab-' + tag + '.png' })
  }
  note('table-tab', { opened })
  evidence.ok = opened.every((o) => o.hasTab)
} catch (error) {
  evidence.error = String(error).slice(0, 500)
} finally {
  fs.writeFileSync(OUT + '/evidence.json', JSON.stringify(evidence, null, 2))
  await browser.close()
}
