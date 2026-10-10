import assert from 'node:assert/strict'
import { createRequire } from 'node:module'
import path from 'node:path'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import { createApp, h } from 'vue'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import { platformState, alternateArtApi } from '/src/l12/platform.ts'
import '/src/style.css'
platformState.account = { id: 'draft-a', username: '草稿测试', role: 'player', createdAt: '2026-10-02', publicHistory: true }
alternateArtApi.mine = async () => []
const router = createRouter({ history: createMemoryHistory(), routes: [
  { path: '/deck-editor', component: Editor },
  { path: '/decks', component: { render: () => h('div', { id: 'left-editor' }, '已离开编辑器') } },
] })
await router.push('/deck-editor')
createApp({ render: () => h(RouterView) }).use(router).mount('#app')
window.__draftQa = { router, platformState }
`
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'deck-editor-draft-fixture',
  resolveId(id) { if (id === '/__draft_qa__.js') return id },
  load(id) { if (id === '/__draft_qa__.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => {
    if (request.url?.startsWith('/__draft_qa__') && !request.url.includes('.js')) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__draft_qa__.js"></script>')
      return
    }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  const url = `http://127.0.0.1:${server.httpServer.address().port}/__draft_qa__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    const requestUrl = new URL(route.request().url())
    if (requestUrl.hostname !== '127.0.0.1') return route.abort()
    if (requestUrl.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
    return route.continue()
  })

  await page.goto(url)
  await page.locator('.deck-builder-grid').waitFor()
  await page.locator('.deck-builder-topbar input').fill('未完成草稿')
  assert.equal(await page.getByRole('button', { name: '保存牌库' }).isDisabled(), true)
  await page.getByRole('button', { name: '暂存草稿' }).click()
  await page.getByText('已暂存到本机').waitFor()
  const keyA = 'l12:deck-editor-draft:v1:account%3Adraft-a'
  assert.ok(await page.evaluate(key => localStorage.getItem(key), keyA))
  assert.equal(await page.locator('.draft-state').innerText(), '本地草稿')

  await page.reload()
  await page.locator('.deck-builder-grid').waitFor()
  await page.getByRole('button', { name: '恢复草稿' }).click()
  assert.equal(await page.locator('.deck-builder-topbar input').inputValue(), '未完成草稿')
  await page.locator('.deck-builder-topbar input').fill('尚未保存的修改')
  page.once('dialog', dialog => dialog.dismiss())
  await page.getByRole('button', { name: '返回上一级' }).click()
  assert.equal(await page.locator('#left-editor').count(), 0)
  page.once('dialog', dialog => dialog.accept())
  await page.getByRole('button', { name: '返回上一级' }).click()
  await page.locator('#left-editor').waitFor()

  await page.goto(url)
  await page.locator('.deck-builder-grid').waitFor()
  await page.evaluate(() => { window.__draftQa.platformState.account = { ...window.__draftQa.platformState.account, id: 'draft-b' } })
  assert.equal(await page.getByRole('button', { name: '恢复草稿' }).count(), 0)
  assert.ok(await page.evaluate(key => localStorage.getItem(key), keyA))

  await page.setViewportSize({ width: 390, height: 844 })
  await page.getByRole('button', { name: '更多操作' }).click()
  assert.equal(await page.getByRole('button', { name: '暂存草稿' }).isVisible(), true)
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true)
  assert.deepEqual(errors, [])
  console.log('Deck editor browser draft passed: incomplete save, restore, leave guard, account switch and 390px action visibility.')
} finally {
  await browser?.close()
  await server.close()
}
