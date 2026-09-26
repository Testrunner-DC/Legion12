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
import { alternateArtApi, platformState } from '/src/l12/platform.ts'
import '/src/style.css'
const cards = await loadCardArchiveCatalog()
const base = cards.find(card => !card.archiveBaseCardId && card.cardType === 'legion') || cards[0]
const art = { id: 'qa-admin-art', artCode: 'QA-ADMIN-ART', baseCardId: base.id, displayName: base.nameZh, mediaAssetId: 'qa-media', imageUrl: '/api/site/media/qa-admin-art.png', thumbnailUrl: '/api/site/media/qa-admin-art.png', active: true, createdAt: '', updatedAt: '', productId: 'qa-product', productName: 'QA 活动', cardImageId: '', builtIn: false, baseCardName: base.nameZh, grantedAt: new Date().toISOString(), grantReason: '管理员派发' }
platformState.account = { id: 'qa-player', username: '已授权玩家', role: 'player', createdAt: '', publicHistory: true }
platformState.token = 'qa-token'
alternateArtApi.gallery = async () => [art]
alternateArtApi.mine = async () => new URLSearchParams(location.search).get('owned') === '0' ? [] : [art]
window.__qa = { baseId: base.id, artCode: art.artCode }
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
  console.log(JSON.stringify({ status: 'passed', viewports: 2, permissionCases: 2, output: out }))
} finally {
  await browser?.close()
  await server.close()
}
