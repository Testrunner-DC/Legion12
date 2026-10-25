import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer as createHttpServer } from 'node:http'
import { createServer as createViteServer } from 'vite'

// The invoking governed PowerShell entry validates these physical D roots first.
const out = process.env.L12_STORAGE_QA_OUT
const cache = process.env.L12_VITE_CACHE_DIR
assert.ok(out && cache && process.env.TEMP, 'explicit governed output/cache/temp roots required')
const root = path.resolve(import.meta.dirname, '..')
const leaves = ['src/l12/browserStorage.ts', 'src/l12/platform.ts', 'src/l12/net.ts',
  'src/l12/decks.ts', 'src/l12/site/ProfilePage.vue', 'src/l12/site/DeckLibraryPage.vue',
  'src/l12/site/OfficialHomePage.vue', 'src/l12/site/homePublishedCache.ts']
const pins = () => Object.fromEntries(leaves.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const before = pins()
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const dist = process.env.L12_STORAGE_QA_DIST
let server
if (dist) {
  assert.ok(path.isAbsolute(dist) && fs.existsSync(path.join(dist, 'index.html')))
  const types = { '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
    '.html': 'text/html', '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp',
    '.jpg': 'image/jpeg', '.avif': 'image/avif', '.woff2': 'font/woff2' }
  const httpServer = createHttpServer((request, response) => {
    const pathname = decodeURIComponent(new URL(request.url, 'http://synthetic.invalid').pathname)
    const filename = path.resolve(dist, '.' + pathname)
    if (!filename.startsWith(path.resolve(dist) + path.sep) && filename !== path.resolve(dist)) {
      response.writeHead(403).end(); return
    }
    const file = fs.existsSync(filename) && fs.statSync(filename).isFile() ? filename : path.join(dist, 'index.html')
    response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' })
    fs.createReadStream(file).pipe(response)
  })
  server = { httpServer, listen: () => new Promise(resolve => httpServer.listen(0, '127.0.0.1', resolve)),
    close: () => new Promise((resolve, reject) => httpServer.close(error => error ? reject(error) : resolve())) }
} else server = await createViteServer({ root, configLoader: 'runner', cacheDir: cache,
  server: { host: '127.0.0.1', port: 0 }, clearScreen: false })
const report = { schema: 1, status: 'running', mode: 'real app/router/components; synthetic API only',
  artifactMode: dist ? 'built-production-static' : 'vite-development',
  sourcePins: before, rows: [], errors: [], pageErrors: [], productionRequests: 0 }
let browser
const home = { composition: JSON.stringify({ heroSlides: [{ id: 'synthetic-home',
  title: '公开首页可正常阅读', summary: '仅用于本地存储故障验收', enabled: true }] }),
  legal: '{}', news: [], videos: [], products: [], media: [] }
async function contextFor(viewport, mode, control = { homeAvailable: true }) {
  const context = await browser.newContext({ viewport })
  await context.route('**/*', route => {
    const url = new URL(route.request().url())
    if (!['127.0.0.1', 'localhost'].includes(url.hostname)) return route.abort()
    if (url.pathname.includes('/api/')) {
      let value = { message: 'controlled unavailable' }, status = 503
      if (url.pathname.endsWith('/api/site/home') && control.homeAvailable) { value = home; status = 200 }
      if (url.pathname.endsWith('/api/auth/login')) {
        value = { token: 'synthetic-browser-token', account: { id: 'synthetic-browser',
          username: 'synthetic-browser', role: 'player', createdAt: '', publicHistory: false, permissions: [] } }
        status = 200
      }
      return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
    }
    return route.continue()
  })
  await context.addInitScript(mode => {
    if (mode === 'getter-denied') {
      for (const name of ['localStorage', 'sessionStorage']) Object.defineProperty(window, name, {
        configurable: true, get() {
          const error = new DOMException('synthetic policy denial', 'SecurityError')
          error.stack = new Error(`storage-denied: ${name}`).stack
          throw error
        },
      })
    } else if (mode === 'read-denied') {
      Storage.prototype.getItem = () => { throw new DOMException('synthetic read denial', 'SecurityError') }
    } else if (mode === 'quota') {
      Storage.prototype.setItem = () => { throw new DOMException('synthetic quota', 'QuotaExceededError') }
    }
  }, mode)
  return context
}
try {
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
    for (const mode of ['getter-denied', 'read-denied']) {
      const context = await contextFor(viewport, mode)
      try {
        for (const route of ['/', '/cards', '/decks', '/me']) {
          const page = await context.newPage(), errors = []
          page.on('pageerror', error => { errors.push(error.message); report.pageErrors.push(error.stack || error.message) })
          await page.goto(origin + route, { waitUntil: 'domcontentloaded' })
          await page.locator('#app .site-shell').waitFor({ timeout: 15_000 })
          if (route === '/me') await page.locator('input[autocomplete="username"]').waitFor()
          else await page.locator('h1').first().waitFor()
          await page.waitForTimeout(150)
          const dom = await page.evaluate(() => ({ text: document.body.innerText.slice(0, 1200),
            mounted: document.querySelector('#app')?.children.length > 0,
            overflow: document.documentElement.scrollWidth > innerWidth + 1 }))
          report.rows.push({ viewport, mode, route, ...dom, errors })
          assert.equal(dom.mounted, true, `${mode} ${route} mounts`)
          assert.equal(dom.overflow, false, `${mode} ${route} containment`)
          assert.deepEqual(errors, [], `${mode} ${route} script errors`)
          if (route === '/me' || route === '/') await page.screenshot({
            path: path.join(out, `${viewport.width}-${mode}-${route === '/' ? 'home' : 'login'}.png`),
          })
          await page.close()
        }
      } finally { await context.close() }
    }
    const context = await contextFor(viewport, 'quota')
    try {
      const page = await context.newPage(), errors = []
      page.on('pageerror', error => { errors.push(error.message); report.pageErrors.push(error.stack || error.message) })
      await page.goto(origin + '/me', { waitUntil: 'domcontentloaded' })
      await page.locator('input[autocomplete="username"]').fill('synthetic-browser')
      await page.locator('input[autocomplete="current-password"]').first().fill('synthetic-only')
      await page.locator('button[type="submit"]').first().click()
      await page.getByText('当前浏览器无法保存登录状态，请允许网站存储后重试。', { exact: true }).waitFor()
      assert.equal(await page.locator('input[autocomplete="username"]').isVisible(), true)
      assert.deepEqual(errors, [])
      report.rows.push({ viewport, mode: 'quota', route: '/me', loginRemainsGuest: true,
        truthfulPersistenceError: true, errors })
      await page.screenshot({ path: path.join(out, `${viewport.width}-quota-login.png`) })
    } finally { await context.close() }
    const control = { homeAvailable: false }
    const failedHomeContext = await contextFor(viewport, 'home-failure', control)
    try {
      const page = await failedHomeContext.newPage(), errors = []
      page.on('pageerror', error => { errors.push(error.message); report.pageErrors.push(error.stack || error.message) })
      await page.goto(origin + '/', { waitUntil: 'domcontentloaded' })
      await page.getByRole('heading', { name: '官网内容暂时无法加载' }).waitFor()
      await page.screenshot({ path: path.join(out, `${viewport.width}-first-home-failure.png`) })
      control.homeAvailable = true
      await page.getByRole('button', { name: '重新加载' }).click()
      await page.getByRole('heading', { name: '公开首页可正常阅读' }).waitFor()
      assert.deepEqual(errors, [])
      assert.equal(await page.getByRole('heading', { name: '官网内容暂时无法加载' }).count(), 0)
      report.rows.push({ viewport, mode: 'home-first-503-retry-200', route: '/', realRetryClick: true, errors })
    } finally { await failedHomeContext.close() }
  }
  assert.deepEqual(pins(), before, 'product sources remained frozen throughout the run')
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'
  report.errors.push(error.stack || String(error))
  process.exitCode = 1
} finally {
  await browser?.close()
  await server.close()
  fs.writeFileSync(path.join(out, 'browser-results.json'), JSON.stringify(report, null, 2))
}
console.log(JSON.stringify({ status: report.status, cases: report.rows.length, errors: report.errors, pageErrors: report.pageErrors }, null, 2))
