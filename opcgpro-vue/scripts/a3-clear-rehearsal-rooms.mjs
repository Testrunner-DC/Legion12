import fs from 'node:fs'
import { chromium } from 'playwright'

const BASE = 'https://legion-12.com/testrun'
const OUT = 'D:/GPT/Legion12/artifacts/a3-clear-20261126'
fs.mkdirSync(OUT, { recursive: true })
const password = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const accounts = (process.env.L12_CLEAR_ACCOUNTS || '彩排01,彩排02,彩排03,彩排04,彩排05,彩排06,彩排07,彩排08').split(',')

const browser = await chromium.launch()
const report = { startedAt: new Date().toISOString(), accounts: [] }

async function clickAny(page, names) {
  for (const name of names) {
    const button = page.getByRole('button', { name, exact: true })
    if (await button.count()) { try { await button.first().click({ force: true }); return name } catch { } }
  }
  return null
}

for (const name of accounts) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const page = await context.newPage()
  const entry = { account: name, actions: [] }
  try {
    await page.goto(BASE + '/me', { waitUntil: 'domcontentloaded' })
    await page.fill('form input[autocomplete=username]', name)
    await page.fill('form input[type=password]', password)
    await page.click('form button[type=submit]')
    await page.waitForFunction(() => !document.querySelector('form input[type=password]'), { timeout: 30000 })

    await page.goto(BASE + '/game', { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(9000)
    entry.gameScreen = (await page.evaluate(() => document.body.innerText)).replace(/\s+/g, ' ').slice(0, 160)

    for (let attempt = 0; attempt < 3; attempt++) {
      const used = await clickAny(page, ['返回大厅', '返回赛事/大厅', '返回赛事', '投降'])
      if (!used) break
      entry.actions.push('clicked:' + used)
      await page.waitForTimeout(2500)
      for (const confirm of ['确认', '确定']) {
        const button = page.getByRole('button', { name: confirm, exact: true })
        if (await button.count()) { try { await button.last().click({ force: true }); entry.actions.push('confirm:' + confirm) } catch { } }
      }
      await page.waitForTimeout(2000)
      if (page.url().includes('/battle') || page.url().includes('/tournaments')) break
    }

    await page.goto(BASE + '/battle', { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(4000)
    let body = await page.evaluate(() => document.body.innerText)
    if (!body.includes('创建新房间')) {
      const left = await clickAny(page, ['关闭房间并返回大厅', '离开房间并返回大厅', '离开房间'])
      if (left) { entry.actions.push('clicked:' + left); await page.waitForTimeout(3000) }
      const tab = page.getByRole('button', { name: '好友房' })
      if (await tab.count()) { try { await tab.first().click({ force: true }) } catch { } ; await page.waitForTimeout(2000) }
      body = await page.evaluate(() => document.body.innerText)
    }
    entry.lobbyReached = body.includes('创建新房间')
    entry.hasDeckInRoom = null
    entry.battleScreen = body.replace(/\s+/g, ' ').slice(0, 160)
    if (!entry.lobbyReached) await page.screenshot({ path: OUT + '/stuck-' + name + '.png' })
  } catch (error) {
    entry.error = String(error).slice(0, 240)
  }
  report.accounts.push(entry)
  await context.close()
}

await browser.close()
report.summary = {
  total: report.accounts.length,
  lobbyReached: report.accounts.filter((a) => a.lobbyReached).length,
  failed: report.accounts.filter((a) => !a.lobbyReached).map((a) => a.account),
}
fs.writeFileSync(OUT + '/clear.json', JSON.stringify(report, null, 2))
console.log(JSON.stringify(report, null, 2))
