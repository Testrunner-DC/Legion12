import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(root, '../artifacts/card-archive-alternate-art-flow')
fs.mkdirSync(out, { recursive: true })
const require = createRequire(import.meta.url)
const sharp = require(process.env.L12_SHARP || 'sharp')
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const alternateArtPng = await sharp({ create: { width: 500, height: 700, channels: 4, background: { r: 255, g: 0, b: 204, alpha: 1 } } }).png().toBuffer()

const entry = `
import { createApp } from 'vue'
import Archive from '/src/l12/CardArchive.vue'
import { loadCardArchiveCatalog } from '/src/l12/decks.ts'
import { alternateArtApi, authState, platformState } from '/src/l12/platform.ts'
import '/src/style.css'
const cards = await loadCardArchiveCatalog()
const base = cards.find(card => card.id === 'S01-0002')
if (!base) throw new Error('S01-0002 acceptance base card is missing')
const art = { id: 'qa-admin-art', artCode: 'S01-0002OLA', baseCardId: base.id, displayName: base.nameZh, mediaAssetId: 'qa-media', imageUrl: '/api/site/media/qa-admin-art.png', thumbnailUrl: '/api/site/media/qa-admin-art.png', active: true, createdAt: '', updatedAt: '', productId: 'qa-product', productName: 'OL专属', cardImageId: '', builtIn: false, baseCardName: base.nameZh, grantedAt: new Date().toISOString(), grantReason: '管理员派发' }
const params = new URLSearchParams(location.search)
const role = params.get('role') === 'admin' ? 'admin' : 'player'
const delayed = params.get('delayed') === '1'
platformState.account = delayed ? null : { id: 'qa-account', username: role === 'admin' ? '系统管理员' : '已授权玩家', role, createdAt: '', publicHistory: true }
platformState.token = 'qa-token'
authState.initialized = true
authState.verified = !delayed
alternateArtApi.gallery = async () => [art]
let mineCalls = 0
alternateArtApi.mine = async () => { mineCalls += 1; return params.get('owned') === '0' ? [] : [art] }
window.__qa = {
  baseId: base.id,
  artCode: art.artCode,
  mineCalls: () => mineCalls,
  completePlayerAuth: () => {
    platformState.account = { id: 'qa-account', username: '延迟登录玩家', role: 'player', createdAt: '', publicHistory: true }
    authState.verified = true
  },
}
createApp(Archive).mount('#app')
`

const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-card-archive-art'), server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'card-archive-alternate-art-fixture',
  resolveId(id) { if (id === '/__card_archive_art.js') return id },
  load(id) { if (id === '/__card_archive_art.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => {
    if (req.url?.match(/^\/__card_archive_art(?:\?|$)/)) { res.setHeader('Content-Type', 'text/html'); res.end('<div id="app"></div><script type="module" src="/__card_archive_art.js"></script>'); return }
    if (req.url?.startsWith('/api/site/media/qa-admin-art.png')) { res.setHeader('Content-Type', 'image/png'); res.end(alternateArtPng); return }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
    const page = await browser.newPage({ viewport })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
      return route.continue()
    })
    await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__card_archive_art?owned=1`)
    await page.getByRole('tab', { name: '全卡池', exact: true }).waitFor()
    const artCode = await page.evaluate(() => window.__qa.artCode)
    await page.getByPlaceholder('卡名、编号或效果文字').fill(artCode)
    const card = page.locator('.archive-card').first()
    await card.waitFor()
    assert.equal(await card.getByRole('button', { name: '下一版本' }).count(), 1, '授权动态异画没有进入全卡池版本组')
    await card.getByRole('button', { name: '下一版本' }).click()
    await card.locator('.l12-card-image[data-source="sameOrigin"] img').waitFor()
    assert.match(await card.locator('img').getAttribute('src'), /qa-admin-art\.png/, '全卡池版本切换没有显示授权动态异画')
    await card.press('Enter')
    await page.getByRole('dialog').waitFor()
    assert.equal(await page.getByRole('button', { name: '大图上一版本' }).count(), 1, '详情预览没有复用版本切换')
    await page.screenshot({ path: path.join(out, `archive-${viewport.width}.png`), fullPage: true })
    assert.deepEqual(errors, [])
    await page.close()
  }
  const unentitled = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  const unentitledErrors = []
  unentitled.on('pageerror', error => unentitledErrors.push(error.message))
  await unentitled.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })
  await unentitled.goto(`http://127.0.0.1:${server.httpServer.address().port}/__card_archive_art?owned=0`)
  await unentitled.getByRole('tab', { name: '全卡池', exact: true }).waitFor()
  const artCode = await unentitled.evaluate(() => window.__qa.artCode)
  await unentitled.getByPlaceholder('卡名、编号或效果文字').fill(artCode)
  assert.equal(await unentitled.locator('.archive-card').count(), 0, '未授权玩家获得了全卡池私有异画切换入口')
  await unentitled.getByRole('tab', { name: '画廊', exact: true }).click()
  assert.equal(await unentitled.locator('.archive-gallery-card').count(), 1, '公开画廊不应因权限过滤隐藏已启用异画')
  await unentitled.screenshot({ path: path.join(out, 'gallery-unentitled-1280.png'), fullPage: true })
  assert.deepEqual(unentitledErrors, [])
  await unentitled.close()

  const admin = await browser.newPage({ viewport: { width: 1280, height: 900 } })
  await admin.goto(`http://127.0.0.1:${server.httpServer.address().port}/__card_archive_art?role=admin&owned=0`)
  await admin.getByRole('tab', { name: '全卡池', exact: true }).waitFor()
  await admin.getByPlaceholder('卡名、编号或效果文字').fill(await admin.evaluate(() => window.__qa.artCode))
  const adminCard = admin.locator('.archive-card').first()
  await adminCard.waitFor()
  assert.equal(await adminCard.getByRole('button', { name: '下一版本' }).count(), 1, '管理员不能在全卡池预览系统异画资产')
  assert.equal(await admin.evaluate(() => window.__qa.mineCalls()), 0, '管理员预览不应伪装成玩家异画拥有权')
  await admin.screenshot({ path: path.join(out, 'catalog-admin-system-assets-1280.png'), fullPage: true })
  await admin.close()

  const delayed = await browser.newPage({ viewport: { width: 390, height: 844 } })
  await delayed.goto(`http://127.0.0.1:${server.httpServer.address().port}/__card_archive_art?delayed=1&owned=1`)
  await delayed.getByRole('tab', { name: '全卡池', exact: true }).waitFor()
  await delayed.getByPlaceholder('卡名、编号或效果文字').fill(await delayed.evaluate(() => window.__qa.artCode))
  assert.equal(await delayed.locator('.archive-card').count(), 0, '身份未验证时不应暴露玩家异画权益')
  await delayed.evaluate(() => window.__qa.completePlayerAuth())
  const delayedCard = delayed.locator('.archive-card').first()
  await delayedCard.waitFor()
  assert.equal(await delayedCard.getByRole('button', { name: '下一版本' }).count(), 1, '身份恢复后没有重新加载玩家异画权益')
  assert.equal(await delayed.evaluate(() => window.__qa.mineCalls()), 1, '身份恢复后异画权益请求次数不正确')
  await delayed.screenshot({ path: path.join(out, 'catalog-delayed-auth-390.png'), fullPage: true })
  await delayed.close()

  console.log(JSON.stringify({ status: 'passed', viewports: 2, permissionCases: 4, output: out }))
} finally {
  await browser?.close()
  await server.close()
}
