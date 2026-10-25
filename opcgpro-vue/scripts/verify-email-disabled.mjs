import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'node:http'

// Run only through a caller that validates the physical D output/temp roots.
const out = process.env.L12_EMAIL_QA_OUT
const dist = process.env.L12_EMAIL_QA_DIST
assert.ok(out && dist && process.env.TEMP && process.env.TMP && process.env.TMPDIR)
assert.ok(fs.existsSync(path.join(dist, 'index.html')))
assert.ok(!fs.existsSync(out) || !fs.readdirSync(out).length, 'do not overwrite evidence')
fs.mkdirSync(out, { recursive: true })
const sources = ['ProfilePage.vue', 'AccountRecoveryPage.vue']
const pins = () => Object.fromEntries(sources.map(file => [file, crypto.createHash('sha256')
  .update(fs.readFileSync(new URL('../src/l12/site/' + file, import.meta.url))).digest('hex')]))
const before = pins()
const types = { '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.html': 'text/html',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.avif': 'image/avif', '.woff2': 'font/woff2' }
const server = createServer((request, response) => {
  const filename = path.resolve(dist, '.' + decodeURIComponent(new URL(request.url, 'http://synthetic.invalid').pathname))
  if (!filename.startsWith(path.resolve(dist) + path.sep)) { response.writeHead(403).end(); return }
  const file = fs.existsSync(filename) && fs.statSync(filename).isFile() ? filename : path.join(dist, 'index.html')
  response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' })
  fs.createReadStream(file).pipe(response)
})
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const report = { status: 'running', mode: 'built app/router/SFC; synthetic API; no production requests',
  sourcePins: before, rows: [], errors: [], pageErrors: [] }
let browser
try {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  const origin = 'http://127.0.0.1:' + server.address().port
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }, { width: 844, height: 390 }]) {
    const context = await browser.newContext({ viewport, hasTouch: viewport.width !== 1440 })
    const account = { id: 'synthetic-email-off', username: 'synthetic', role: 'player', publicHistory: false,
      createdAt: '2026-10-25T00:00:00Z', permissions: [], permissionVersion: 1 }
    const calls = []
    await context.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (!url.pathname.includes('/api/')) return route.continue()
      calls.push(url.pathname)
      let value = {}, status = 200
      if (url.pathname.endsWith('/auth/me')) {
        if (route.request().headers().authorization) value = account
        else { status = 401; value = { code: 'authentication_required', message: '合成匿名请求' } }
      } else if (url.pathname.endsWith('/auth/login')) value = { account, token: 'synthetic-email-off-token' }
      else if (url.pathname.endsWith('/auth/username-change-status')) value = { freeRenameAvailable: false, freeRenameUsed: 1 }
      else if (url.pathname.endsWith('/auth/mfa/capability')) value = { enrollmentEnabled: false }
      else if (/email|password\/(forgot|reset)/.test(url.pathname)) value = { enabled: true, mailConfigured: true }
      else if (/notifications|sessions|alternate-arts/.test(url.pathname)) value = []
      else if (url.pathname.endsWith('/content')) value = { values: {} }
      return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    })
    const page = await context.newPage()
    page.on('pageerror', error => report.pageErrors.push(error.message))
    try {
      await page.goto(origin + '/me?section=security')
      await page.locator('input[autocomplete="username"]').waitFor()
      assert.equal(await page.locator('a[href*="/auth/recovery"], .email-manager, input[type="email"]').count(), 0)
      await page.locator('input[autocomplete="username"]').fill('synthetic')
      await page.locator('input[autocomplete="current-password"]').first().fill('synthetic-password')
      await page.locator('button[type="submit"]').first().click()
      await page.getByRole('button', { name: '修改密码', exact: true }).waitFor()
      assert.equal(await page.locator('.email-manager, input[type="email"]').count(), 0)
      const savedToken = await page.evaluate(() => localStorage.getItem('l12-auth-token'))
      assert.equal(savedToken, 'synthetic-email-off-token')
      for (const mode of ['request', 'verify-email', 'reset-password']) {
        await page.goto(origin + '/auth/recovery?mode=' + mode + '#token=synthetic-retired-token')
        await page.getByRole('heading', { name: '邮箱功能已停用', exact: true }).waitFor()
        assert.equal(new URL(page.url()).hash, '')
        assert.equal(await page.locator('.recovery-card input, .recovery-card form, .recovery-card button').count(), 0)
        assert.equal(await page.evaluate(() => localStorage.getItem('l12-auth-token')), savedToken)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false)
        const back = page.getByRole('link', { name: /返回登录 \/ 我的/ })
        const rect = await back.boundingBox()
        assert.ok(rect && rect.height >= 38 && rect.x >= 0 && rect.x + rect.width <= viewport.width + 1)
        await page.screenshot({ path: path.join(out, viewport.width + '-' + mode + '.png') })
        await back.click()
        await page.getByRole('heading', { name: '我的', exact: true }).waitFor()
        report.rows.push({ viewport, mode, fragmentCleared: true, sessionPreserved: true, returnReached: true })
      }
      assert.equal(calls.filter(value => /email|password\/(forgot|reset)/.test(value)).length, 0)
      assert.equal(calls.filter(value => value.includes('logout')).length, 0)
    } finally { await context.close() }
  }
  assert.deepEqual(report.pageErrors, [])
  assert.deepEqual(pins(), before)
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'; report.errors.push(error.stack || String(error)); process.exitCode = 1
} finally {
  await browser?.close()
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  fs.writeFileSync(path.join(out, 'browser-results.json'), JSON.stringify(report, null, 2))
}
console.log(JSON.stringify({ status: report.status, cases: report.rows.length, errors: report.errors, pageErrors: report.pageErrors }, null, 2))
