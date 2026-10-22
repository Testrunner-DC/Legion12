import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..', '..')
const projectRoot = path.join(root, 'opcgpro-vue')
const artifactRoot = process.env.L12_ARTIFACT_ROOT
  || 'D:/GPT/Legion12/artifacts/gallery-product-filter-concurrency-20261022'
const cacheRoot = process.env.L12_CACHE_ROOT
  || 'D:/GPT/Legion12/cache/primary/gallery-product-filter-concurrency-20261022'
fs.mkdirSync(artifactRoot, { recursive: true })
fs.mkdirSync(cacheRoot, { recursive: true })

const panelPath = 'opcgpro-vue/src/l12/site/AdminAlternateArtsPanel.vue'
const testPath = 'opcgpro-vue/scripts/test-gallery-product-filter-concurrency.mjs'
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const panelSource = read(panelPath)
assert.match(panelSource, /const productFilterPending = ref<Record<string, \{ request: number; value: boolean \}>>\(\{\}\)/,
  '产品筛选必须维护逐产品待处理状态')
assert.match(panelSource, /productFilterPending\.value\[product\.id\]\?\.request !== request/,
  '过期响应必须在写回产品状态前被拒绝')
assert.match(panelSource, /:disabled="!canWrite \|\| productFilterPending\[item\.id\] !== undefined"/,
  '保存中的产品必须独立禁用')

const entry = `
import { createApp } from 'vue'
import '/src/style.css'
import AdminAlternateArtsPanel from '/src/l12/site/AdminAlternateArtsPanel.vue'
import { adminApi, authState, platformState } from '/src/l12/platform.ts'
const now = new Date().toISOString()
let products = [
  { id: 'product-a', name: '产品甲', active: true, createdAt: now, updatedAt: now, categoryName: '卡牌系列', source: 'catalog', status: 'active', showInGalleryFilter: true },
  { id: 'product-b', name: '产品乙', active: true, createdAt: now, updatedAt: now, categoryName: '卡牌系列', source: 'catalog', status: 'active', showInGalleryFilter: true },
]
const pending = []
const calls = []
adminApi.alternateArtProducts = async () => products
adminApi.alternateArtRarities = async () => []
adminApi.siteCategories = async () => []
adminApi.searchAlternateArts = async () => ({ items: [], total: 0, page: 1, pageSize: 20 })
adminApi.saveGalleryProductFilterVisibility = draft => {
  calls.push(structuredClone(draft))
  return new Promise((resolve, reject) => pending.push({ draft: structuredClone(draft), resolve, reject }))
}
authState.verified = true
platformState.token = 'qa-token'
platformState.account = { id: 'qa-admin', username: 'QA', role: 'admin', createdAt: '', publicHistory: false, permissions: ['admin.content.draft'] }
window.__qa = {
  calls,
  pendingCount: () => pending.length,
  products: () => structuredClone(products),
  resolve: (index, value, updateServer = true) => {
    const request = pending[index]
    const current = products.find(item => item.id === request.draft.productId)
    const saved = { ...current, showInGalleryFilter: value ?? request.draft.showInGalleryFilter }
    if (updateServer) products = products.map(item => item.id === saved.id ? saved : item)
    request.resolve(saved)
  },
  reject: (index, message) => pending[index].reject(new Error(message)),
}
createApp(AdminAlternateArtsPanel, { onNotice: value => { window.__lastNotice = value } }).mount('#app')
`

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const vite = await createServer({
  root: projectRoot,
  configLoader: 'runner',
  cacheDir: path.join(cacheRoot, 'vite'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'gallery-product-filter-concurrency-acceptance',
    resolveId(id) { if (id === '/__gallery_filter_concurrency.js') return id },
    load(id) { if (id === '/__gallery_filter_concurrency.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((req, res, next) => {
        if (new URL(req.url || '/', 'http://127.0.0.1').pathname === '/__gallery_filter_concurrency') {
          res.setHeader('Content-Type', 'text/html; charset=utf-8')
          res.end('<div id="app"></div><script type="module" src="/__gallery_filter_concurrency.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

let browser
try {
  await vite.listen()
  const port = vite.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  const pageErrors = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({
      json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} },
    })
    return route.continue()
  })
  await page.goto(`http://127.0.0.1:${port}/__gallery_filter_concurrency`)
  await page.getByRole('button', { name: '管理产品筛选', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '管理玩家端产品筛选' })
  await dialog.waitFor()
  const productA = dialog.getByRole('checkbox', { name: /产品甲/ })
  const productB = dialog.getByRole('checkbox', { name: /产品乙/ })

  await productA.uncheck()
  await assert.rejects(() => productA.check({ timeout: 500 }), /disabled|not enabled/i,
    '同一产品保存中仍允许第二次用户点击')
  assert.equal(await page.evaluate(() => window.__qa.pendingCount()), 1, '同一产品双击产生了重复保存')
  assert.equal(await productB.isDisabled(), false, '产品甲保存时错误禁用了产品乙')

  await productB.uncheck()
  assert.equal(await page.evaluate(() => window.__qa.pendingCount()), 2, '不同产品没有并行保存')
  await page.evaluate(() => window.__qa.resolve(1, false))
  await page.waitForFunction(() => [...document.querySelectorAll('.product-filter-list label')]
    .find(label => label.textContent.includes('产品乙'))?.querySelector('input')?.disabled === false)
  assert.equal(await productB.isDisabled(), false, '产品乙成功后没有独立释放busy')
  assert.equal(await productB.isChecked(), false, '产品乙成功状态没有写回')
  assert.equal(await productA.isDisabled(), true, '产品乙成功错误释放了产品甲busy')

  await page.evaluate(() => window.__qa.reject(0, '注入：产品甲保存失败'))
  await page.waitForFunction(() => window.__lastNotice === '注入：产品甲保存失败')
  await page.waitForFunction(() => [...document.querySelectorAll('.product-filter-list label')]
    .find(label => label.textContent.includes('产品甲'))?.querySelector('input')?.disabled === false)
  assert.equal(await productA.isDisabled(), false, '产品甲失败后没有释放busy')
  assert.equal(await productA.isChecked(), true, '产品甲失败后没有恢复已保存状态')

  await productA.evaluate(input => { input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })) })
  await productA.evaluate(input => { input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })) })
  assert.equal(await page.evaluate(() => window.__qa.pendingCount()), 4, '迟到回复测试没有建立两个同产品请求')
  await page.evaluate(() => window.__qa.resolve(3, true))
  await page.waitForFunction(() => window.__qa.products().find(item => item.id === 'product-a').showInGalleryFilter === true)
  await page.evaluate(() => window.__qa.resolve(2, false, false))
  await page.waitForTimeout(50)
  assert.equal(await productA.isChecked(), true, '旧成功回复晚到后覆盖了最新UI')
  assert.equal(await page.evaluate(() => window.__qa.products().find(item => item.id === 'product-a').showInGalleryFilter), true,
    '服务端最终值没有保持最新请求')

  await page.screenshot({ path: path.join(artifactRoot, 'product-filter-concurrency-1440x900.png'), fullPage: true })
  assert.deepEqual(pageErrors, [], '真实SFC回归出现页面异常')
  const sha256 = relative => crypto.createHash('sha256').update(read(relative)).digest('hex').toUpperCase()
  const report = {
    status: 'passed',
    cases: { sameProductBusy: true, failureReleasesBusy: true, differentProductsParallel: true, staleReplyIgnored: true },
    calls: await page.evaluate(() => window.__qa.calls),
    sourceSha256: { [panelPath]: sha256(panelPath), [testPath]: sha256(testPath) },
  }
  const reportPath = path.join(artifactRoot, 'report.json')
  fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`)
  console.log(JSON.stringify({ ...report, reportPath }))
  await page.close()
} finally {
  await browser?.close()
  await vite.close()
}
