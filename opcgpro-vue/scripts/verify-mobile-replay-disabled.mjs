import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5185'
const output = process.env.L12_MOBILE_REPLAY_OUT || path.resolve('..', 'artifacts', 'mobile-replay-disabled')
fs.mkdirSync(output, { recursive: true })
const account = {
  id: 'mobile-replay-qa', username: '回放验收账号', role: 'player', permissions: [],
  mustChangeUsername: false, mustChangePassword: false, createdAt: '2026-09-20T00:00:00Z',
}
const match = {
  matchId: 'mobile-replay-qa-match', roomCode: 'QA-MOBILE', startedUtc: '2026-09-20T00:00:00Z',
  endedUtc: '2026-09-20T00:10:00Z', player0: '甲', player1: '乙', winner: 0, commandCount: 8,
}

async function seed(context) {
  await context.addInitScript(({ account }) => {
    localStorage.setItem('l12-account', JSON.stringify(account))
    localStorage.setItem('l12-auth-token', 'mobile-replay-qa-token')
    localStorage.setItem('l12-nickname', account.username)
  }, { account })
}

async function mockApi(page, requests) {
  await page.route('**/api/**', route => {
    const url = new URL(route.request().url())
    requests.push(url.pathname + url.search)
    const body = url.pathname === '/api/auth/me' ? account
      : url.pathname === '/api/matches' ? [match]
        : { message: 'desktop replay request reached the API' }
    const status = url.pathname.startsWith('/api/matches/') ? 503 : 200
    return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
  })
}

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
try {
  const mobile = await browser.newContext({ viewport: { width: 844, height: 390 }, isMobile: true, hasTouch: true })
  await seed(mobile)
  const page = await mobile.newPage()
  page.setDefaultTimeout(8_000)
  const requests = []
  await mockApi(page, requests)

  await page.goto(`${origin}/battle/records?selected=${match.matchId}`, { waitUntil: 'domcontentloaded' })
  await page.getByRole('button', { name: '播放回放', exact: true }).click()
  const notice = page.locator('.records-error').getByText('请到电脑端查看回放', { exact: true })
  await notice.waitFor()
  assert.equal(new URL(page.url()).pathname, '/battle/records', 'mobile play must not navigate')
  assert.equal(requests.some(url => url.startsWith(`/api/matches/${match.matchId}`)), false, 'mobile play must not fetch replay detail')
  await page.screenshot({ path: path.join(output, 'mobile-records-blocked-844x390.png') })

  let chooserOpened = false
  page.once('filechooser', () => { chooserOpened = true })
  await page.getByRole('button', { name: '打开 JSON 回放', exact: true }).click()
  await notice.waitFor()
  await page.waitForTimeout(250)
  assert.equal(chooserOpened, false, 'mobile JSON replay action must not open a file chooser')
  assert.equal(new URL(page.url()).pathname, '/battle/records', 'mobile JSON replay action must remain on records')

  requests.length = 0
  await page.goto(`${origin}/battle/records/replay/${match.matchId}`, { waitUntil: 'domcontentloaded' })
  await page.getByText('请到电脑端查看回放', { exact: true }).waitFor()
  assert.equal(await page.locator('.board-viewport').count(), 0, 'mobile direct replay route must not mount GameBoard')
  assert.equal(requests.some(url => url.startsWith(`/api/matches/${match.matchId}`)), false, 'mobile direct replay route must not fetch replay detail')
  await page.screenshot({ path: path.join(output, 'mobile-direct-replay-blocked-844x390.png') })

  requests.length = 0
  await page.goto(`${origin}/battle/records/replay/json`, { waitUntil: 'domcontentloaded' })
  await page.getByText('请到电脑端查看回放', { exact: true }).waitFor()
  assert.equal(await page.locator('.board-viewport').count(), 0, 'mobile JSON replay route must not mount GameBoard')
  assert.equal(requests.some(url => url.startsWith('/api/matches/')), false, 'mobile JSON replay route must not fetch replay detail')
  await mobile.close()

  const desktop = await browser.newContext({ viewport: { width: 1366, height: 768 } })
  await seed(desktop)
  const desktopPage = await desktop.newPage()
  const desktopRequests = []
  await mockApi(desktopPage, desktopRequests)
  await desktopPage.goto(`${origin}/battle/records/replay/${match.matchId}`, { waitUntil: 'domcontentloaded' })
  await desktopPage.waitForTimeout(350)
  assert.equal(await desktopPage.getByText('请到电脑端查看回放', { exact: true }).count(), 0, 'desktop route must not show the mobile blocker')
  assert.equal(desktopRequests.some(url => url.startsWith(`/api/matches/${match.matchId}`)), true, 'desktop route must retain replay loading')
  await desktop.close()

  console.log('Mobile replay disablement passed: record actions stay put, JSON picker stays closed, direct routes do not fetch or mount a board, and desktop loading remains enabled.')
} finally {
  await browser.close()
}
