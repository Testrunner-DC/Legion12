import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = path.resolve(root, '../artifacts/deck-editor-reference-flow')
fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import '/src/style.css'
const [cards,presets]=await Promise.all([loadDeckCatalog(),loadOfficialPresetDecks()])
const deck=presets.find(item=>item.cardIds.length>=40)
if(!deck)throw Error('fixture deck missing')
const bench=deck.cardIds.slice(0,2)
localStorage.setItem('l12-custom-decks-v1',JSON.stringify({'编辑流程验收':{...deck,name:'编辑流程验收',benchIds:bench,updatedAt:new Date().toISOString()}}))
localStorage.setItem('l12:official-presets:guest-seeded:v1','true')
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:all(.*)',component:Editor}]})
await router.push('/deck-editor?deck=编辑流程验收')
createApp(Editor).use(router).mount('#app')
`

const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'deck-editor-reference-fixture',
  resolveId(id) { if (id === '/__deck_editor_reference__.js') return id },
  load(id) { if (id === '/__deck_editor_reference__.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => {
    if (request.url === '/__deck_editor_reference__') {
      response.setHeader('Content-Type', 'text/html')
      response.end('<div id="app"></div><script type="module" src="/__deck_editor_reference__.js"></script>')
      return
    }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })
  const port = server.httpServer.address().port
  const profiles = [
    ['wide-1920', 1920, 1080], ['wide-1440', 1440, 900], ['wide-1280', 1280, 720],
    ['phone-390', 390, 844], ['phone-360', 360, 780], ['short-landscape', 844, 390],
  ]
  for (const [name, width, height] of profiles) {
    await page.setViewportSize({ width, height })
    await page.goto(`http://127.0.0.1:${port}/__deck_editor_reference__`)
    await page.locator('.deck-builder-grid').waitFor()
    const narrow = width <= 820
    if (narrow) {
      await page.getByRole('button', { name: /牌表/ }).click()
      await page.locator('[data-deck-section="bench"]').waitFor()
      assert.equal(await page.locator('.deck-center-column').isVisible(), false)
      await page.screenshot({ path: path.join(out, `${name}-deck.png`), fullPage: true })
      await page.getByRole('button', { name: '统计 / 起手' }).click()
      await page.getByRole('button', { name: 'Hand 起手' }).click()
      assert.equal(await page.locator('[data-editor-workspace="hand"]').isVisible(), true)
      await page.screenshot({ path: path.join(out, `${name}-hand.png`), fullPage: true })
      await page.getByRole('button', { name: '卡池' }).click()
      await page.locator('.catalog-tabs').getByRole('button', { name: '主牌库' }).click()
      await page.getByRole('button', { name: /^筛选/ }).click()
      const sheet = await page.locator('.catalog-filter-bar.mobile-open').boundingBox()
      assert.ok(sheet && sheet.y >= 0 && sheet.y + sheet.height <= height + 1, `${name} 筛选面板越界`)
      await page.getByRole('button', { name: '关闭筛选' }).click()
    } else {
      await page.getByRole('button', { name: 'Stats 统计' }).click()
      assert.equal(await page.locator('[data-editor-workspace="stats"]').isVisible(), true)
      assert.equal(await page.locator('.deck-list').isVisible(), true)
      await page.screenshot({ path: path.join(out, `${name}-stats.png`), fullPage: true })
      await page.getByRole('button', { name: 'Hand 起手' }).click()
      assert.equal(await page.locator('.editor-opening-hand article').count(), 6)
      await page.screenshot({ path: path.join(out, `${name}-hand.png`), fullPage: true })
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)
    assert.equal(overflow, false, `${name} 出现页面级横向溢出`)
  }
  assert.deepEqual(errors, [])
  console.log(JSON.stringify({ status: 'passed', viewports: profiles.length, screenshots: profiles.length * 2, output: out }))
} finally {
  await browser?.close()
  await server.close()
}
