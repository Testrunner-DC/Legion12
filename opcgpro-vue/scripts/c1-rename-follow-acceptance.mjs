import fs from 'node:fs'
import { chromium } from 'playwright'

// 改名跟随验收（用户 2026-11-26「都改」）：双人对局里改一方昵称后，对方棋盘上的名字必须跟随。
// 关键点：① 改名只影响「下一次广播」，脚本必须在改名后制造一次真实广播；
// ② 自助改名保留当前会话，审批改名会吊销其它会话，所以优先用仍有一次自助机会的账号；
// ③ 收尾要把昵称改回原值（走申请 + 管理员审批）。
const BASE = 'https://legion-12.com/testrun'
const OUT = 'D:/GPT/Legion12/artifacts/c1-rename-follow'
fs.mkdirSync(OUT, { recursive: true })
const rehearsalPassword = fs.readFileSync('D:/GPT/Legion12/artifacts/rehearsal-secret.txt', 'utf8').trim()
const adminPassword = fs.readFileSync('D:/GPT/Legion12/artifacts/testrun-admin-secret.txt', 'utf8').trim()

const evidence = { startedAt: new Date().toISOString(), steps: [], frames: [], boardText: {} }
const note = (step, data) => evidence.steps.push({ step, at: new Date().toISOString(), ...data })
const bodyText = (page) => page.evaluate(() => document.body.innerText.replace(/\s+/g, ' '))
const countOf = (text, needle) => text.split(needle).length - 1
const browser = await chromium.launch()

async function login(name, tapFrames = false) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const page = await context.newPage()
  if (tapFrames) {
    page.on('websocket', (socket) => {
      const keep = (direction) => (frame) => {
        const text = String(frame.payload)
        if (!text.includes('room')) return
        evidence.frames.push({ direction, at: Date.now(), text: text.slice(0, 700) })
        if (evidence.frames.length > 40) evidence.frames.shift()
      }
      socket.on('framesent', keep('out'))
      socket.on('framereceived', keep('in'))
    })
  }
  await page.goto(BASE + '/me', { waitUntil: 'domcontentloaded' })
  await page.fill('form input[autocomplete=username]', name)
  await page.fill('form input[type=password]', rehearsalPassword)
  await page.click('form button[type=submit]')
  await page.waitForFunction(() => !document.querySelector('form input[type=password]'), { timeout: 30000 })
  return page
}

async function clickIf(page, name) {
  const button = page.getByRole('button', { name, exact: true })
  if (!(await button.count())) return false
  try { await button.first().click({ force: true }); return true } catch { return false }
}

// 推进对局里的必选步骤（跳过演出 / 选择先攻 / 禁用天灾），同时天然产生一次房间或对局广播。
async function advance(page) {
  const acted = []
  if (await clickIf(page, '跳过演出')) acted.push('skip-presentation')
  for (const choice of ['选择先攻', '选择后攻']) {
    if (await clickIf(page, choice)) { acted.push(choice); break }
  }
  const dialog = page.locator('[role=dialog]').first()
  if (await dialog.count()) {
    const tiles = dialog.locator('img')
    const total = await Math.min(await tiles.count(), 6)
    for (let index = 0; index < total; index++) {
      try { await tiles.nth(index).click({ force: true }) } catch { }
      await page.waitForTimeout(350)
      const confirm = page.getByRole('button', { name: '确认选择', exact: true })
      if (await confirm.count() && !(await confirm.first().evaluate((element) => element.disabled))) {
        await confirm.first().click({ force: true }); acted.push('ban-' + index); break
      }
    }
  }
  return acted
}

async function usernameStatus(page) {
  return page.evaluate(async ({ base }) => {
    const key = Object.keys(localStorage).find((item) => item.startsWith('l12-auth-token'))
    const token = key ? localStorage.getItem(key) : ''
    if (!token) return null
    const response = await fetch(base + '/api/auth/username-change-status', { headers: { Authorization: 'Bearer ' + token } })
    return response.ok ? response.json() : null
  }, { base: BASE })
}

async function renameViaPage(page, newName) {
  return page.evaluate(async ({ base, newName, password }) => {
    const key = Object.keys(localStorage).find((item) => item.startsWith('l12-auth-token'))
    const token = key ? localStorage.getItem(key) : ''
    if (!token) return { ok: false, reason: 'no-token' }
    const post = async (path, payload) => {
      const response = await fetch(base + path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
        body: JSON.stringify(payload),
      })
      return { status: response.status, json: await response.json().catch(() => null) }
    }
    const selfService = await post('/api/auth/username-change', { currentPassword: password, newUsername: newName })
    if (selfService.status === 200) return { ok: true, path: 'self-service', selfService }
    const applied = await post('/api/auth/username-change-requests', { newUsername: newName, reason: '测试服验收：对局中改名，验证棋盘显示名跟随' })
    return { ok: false, needReview: applied.status === 200, selfService, applied }
  }, { base: BASE, newName, password: rehearsalPassword })
}

async function adminToken() {
  const response = await fetch(BASE + '/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: 'Admin', password: adminPassword }),
  })
  return (await response.json()).token
}

async function approve(token, requestedName) {
  const rows = await fetch(BASE + '/api/admin/username-change-requests?status=pending', {
    headers: { Authorization: 'Bearer ' + token },
  }).then((response) => response.json())
  const target = (Array.isArray(rows) ? rows : []).find((row) => row.requestedUsername === requestedName)
  if (!target) return { ok: false, reason: 'request-not-found' }
  const review = await fetch(BASE + '/api/admin/username-change-requests/' + target.id + '/review', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
    body: JSON.stringify({ approve: true, note: '测试服验收收尾：恢复彩排账号原名' }),
  })
  return { ok: review.status === 200, status: review.status }
}

const CREATE_ROOM_LABELS = ['创建新房间', '创建好友房']
async function hasCreateRoom(page) {
  for (const name of CREATE_ROOM_LABELS) if (await page.getByRole('button', { name }).count()) return true
  return false
}
async function ensureLobby(page) {
  await page.goto(BASE + '/battle', { waitUntil: 'domcontentloaded' })
  for (let attempt = 0; attempt < 8; attempt++) {
    await page.waitForTimeout(1300)
    if (await hasCreateRoom(page)) return
    await advance(page)
    if (await clickIf(page, '投降')) {
      await page.waitForTimeout(1200)
      for (const confirm of ['确认投降', '确认', '确定']) if (await clickIf(page, confirm)) break
      await page.waitForTimeout(2500)
    }
    for (const leaving of ['关闭房间并返回大厅', '离开房间并返回大厅']) {
      if (await clickIf(page, leaving)) break
    }
    if (await clickIf(page, '返回大厅')) await page.waitForTimeout(2000)
    await clickIf(page, 'ROOM好友房')
  }
  throw new Error('could not return to lobby')
}

try {
  const players = ['彩排01', '彩排02']
  const a = await login(players[0])
  const b = await login(players[1], true)
  const statuses = { [players[0]]: await usernameStatus(a), [players[1]]: await usernameStatus(b) }
  note('username-change-status', statuses)
  // 选仍有一次自助机会的一方改名：自助改名保留当前会话，不会把这局踢掉。
  const freeOrder = [players[1], players[0]].filter((name) => {
    const status = statuses[name]
    const used = status?.selfServiceUsed ?? status?.selfServiceChangeCount ?? null
    return status && (used === 0 || status?.canSelfServiceChange === true)
  })
  const subject = freeOrder[0] ?? players[1]
  note('rename-subject', { subject, freeCandidates: freeOrder })

  await ensureLobby(a)
  let code = ''
  for (let attempt = 0; attempt < 3 && !code; attempt++) {
    for (const name of CREATE_ROOM_LABELS) if (await clickIf(a, name)) break
    await a.waitForFunction(() => document.body.innerText.includes('复制房间码'), { timeout: 20000 }).catch(() => { })
    code = await a.evaluate(() => {
      const button = Array.from(document.querySelectorAll('button')).find((item) => item.textContent.trim() === '复制房间码')
      if (!button) return ''
      const host = button.closest('section,article,div') || button.parentElement
      const found = (host?.innerText || '').match(/\b[A-Z0-9]{6}\b/g) || []
      return found[0] || ''
    })
    if (!code) await ensureLobby(a)
  }
  note('room', { code })
  if (!code) throw new Error('room code not found')

  await ensureLobby(b)
  await b.fill('input[placeholder*="房间码"]', code)
  await clickIf(b, '加入对战')
  await b.waitForFunction(() => document.body.innerText.includes('PLAYER 2'), { timeout: 25000 })
  note('joined', { ok: true })

  for (const [tag, page] of [['a', a], ['b', b]]) {
    const slot = page.locator('text=点击封面选择牌库').first()
    if (await slot.count()) { try { await slot.click({ force: true }) } catch { } }
    await page.waitForTimeout(1500)
    const dialog = page.locator('[role=dialog]').first()
    const option = dialog.locator('text=张主牌').first()
    if (await option.count()) { try { await option.click({ force: true }) } catch { } }
    await page.waitForTimeout(500)
    if (await clickIf(page, '确认使用')) await page.waitForTimeout(1000)
    note('room-deck-' + tag, { ok: true })
  }
  for (const [tag, page] of [['a', a], ['b', b]]) note('ready-' + tag, { ok: await clickIf(page, '准备对战') })

  await a.waitForFunction(() => !document.body.innerText.includes('友谊战整备室'), { timeout: 60000 })
  await a.waitForTimeout(3000)
  note('game-started', { url: a.url() })
  for (const page of [a, b]) await advance(page)
  await a.waitForTimeout(3000)

  const textBefore = await bodyText(a)
  const subjectBefore = await bodyText(subject === players[0] ? a : b)
  evidence.boardText.before = {
    nameOnOpponentBoard: countOf(textBefore, subject),
    nameOnOwnBoard: countOf(subjectBefore, subject),
  }
  await a.screenshot({ path: OUT + '/10-before.png' })

  const newName = subject + '改'
  const renamePage = subject === players[0] ? a : b
  const rename = await renameViaPage(renamePage, newName)
  note('rename', { subject, newName, ...rename })
  if (!rename.ok && rename.needReview) note('rename-approve', await approve(await adminToken(), newName))

  // 改名只影响下一次广播：让另一方继续推进，逼出一帧新的房间/对局状态。
  for (const page of [a, b]) await advance(page)
  await a.waitForTimeout(2000)

  const watcherPage = subject === players[0] ? b : a
  const sawNew = await watcherPage
    .waitForFunction((value) => document.body.innerText.includes(value), newName, { timeout: 30000 })
    .then(() => true).catch(() => false)
  const textAfter = await bodyText(a)
  const watcherAfter = await bodyText(watcherPage)
  evidence.boardText.after = {
    newOnOpponentBoard: countOf(textAfter, newName),
    oldOnOpponentBoard: countOf(textAfter, subject),
    newOnWatcherBoard: countOf(watcherAfter, newName),
  }
  note('board-follow', { sawNew, watcher: subject === players[0] ? players[1] : players[0] })
  evidence.framesWithNewName = evidence.frames.filter((frame) => frame.text.includes(newName)).length
  await a.screenshot({ path: OUT + '/11-after.png' })
  await watcherPage.screenshot({ path: OUT + '/12-after-watcher.png' })

  // 收尾：把昵称改回原值（自助机会已用掉，走申请 + 管理员审批）。
  const restoreApply = await renameViaPage(renamePage, subject)
  note('restore-apply', restoreApply)
  if (restoreApply.needReview) note('restore-approve', await approve(await adminToken(), subject))
  const relogin = await fetch(BASE + '/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: subject, password: rehearsalPassword }),
  })
  evidence.restore = { loginAsOriginalName: relogin.status }
  note('restore-login', { status: relogin.status })

  evidence.ok = sawNew && relogin.status === 200
} catch (error) {
  evidence.error = String(error).slice(0, 700)
} finally {
  fs.writeFileSync(OUT + '/evidence.json', JSON.stringify(evidence, null, 2))
  await browser.close()
}
