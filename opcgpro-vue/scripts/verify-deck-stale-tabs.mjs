import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

// Real Vue editors and real clicks; only the HTTP authority is a deterministic CAS fixture.
// Complements, never replaces, the real server/API and deployment checks.
const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(process.env.L12_QA_OUTPUT || path.join(root, '../artifacts/deck-stale-tabs'))
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import { createApp, h } from 'vue'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import Detail from '/src/l12/site/PublicDeckDetailPage.vue'
import { loadOfficialPresetDecks, saveDeck, loadSavedDecks } from '/src/l12/decks.ts'
import { platformState, publicDeckApi, alternateArtApi } from '/src/l12/platform.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'
const guest = new URL(location.href).searchParams.has('guest')
platformState.account = guest ? null : { id: 'tabs-qa', username: '双页面验收', role: 'player', createdAt: '2026-10-03', publicHistory: true }
platformState.token = guest ? '' : 'fixture-only-token'
l12State.endpoint = 'ws://' + location.host
alternateArtApi.mine = async () => []
publicDeckApi.list = async () => []
const preset = (await loadOfficialPresetDecks())[0]
const seed = { ...preset, id: 'tabs-deck', revision: 1, name: '双页面原内容', updatedAt: '2026-10-03T00:00:00Z' }
if (guest && !localStorage.getItem('l12-custom-decks-v1'))
  localStorage.setItem('l12-custom-decks-v1', JSON.stringify({ [seed.name]: seed }))
window.__tabsQa = { seed, saveDeck, loadSavedDecks }
const router = createRouter({ history: createMemoryHistory(), routes: [
  { path: '/deck-editor', component: Editor },
  { path: '/decks', name: 'decks', component: Library },
  { path: '/decks/:deckId', name: 'public-deck-detail', component: Detail },
] })
await router.push(new URL(location.href).searchParams.has('library') ? '/decks?tab=plaza' : '/deck-editor?deckId=tabs-deck')
createApp({ render: () => h(RouterView) }).use(router).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(root, '.tmp/vite-stale-tabs'), server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'deck-stale-tabs-fixture',
  resolveId(id) { if (id === '/__tabs_qa__.js') return id },
  load(id) { if (id === '/__tabs_qa__.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => {
    if (req.url?.startsWith('/__tabs_qa__') && !req.url.includes('.js')) {
      res.setHeader('Content-Type', 'text/html')
      res.end('<div id="app"></div><script type="module" src="/__tabs_qa__.js"></script>')
    } else next()
  }) },
}] })
let browser
try {
  await server.listen()
  const url = `http://127.0.0.1:${server.httpServer.address().port}/__tabs_qa__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const errors = [], report = []
  for (const guest of [false, true]) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } })
    let authority
    const requests = []
    await context.route('**/*', async route => {
      const request = route.request(), endpoint = new URL(request.url())
      if (endpoint.hostname !== '127.0.0.1') return route.abort()
      if (endpoint.pathname.endsWith('card-assets.manifest.json'))
        return route.fulfill({ json: { schemaVersion: 3, catalogVersion: 'qa', assetVersion: 'qa', basePath: '/', cards: {} } })
      if (endpoint.pathname === '/api/operations/effective-policy')
        return route.fulfill({ json: { season: { name: '验收赛季' }, cardRestrictions: [], defaultPresetDeckIds: [] } })
      if (endpoint.pathname === '/api/decks' && request.method() === 'GET') {
        if (!authority) {
          const presets = JSON.parse(fs.readFileSync(path.join(root, 'public/data/l12/preset-decks.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
          authority = { ...presets[0], id: 'tabs-deck', revision: 1, name: '双页面原内容', updatedAt: '2026-10-03T00:00:00Z' }
        }
        return route.fulfill({ json: authority ? [authority] : [] })
      }
      if (endpoint.pathname.startsWith('/api/decks') && request.method() !== 'GET') {
        const body = request.postDataJSON()
        requests.push({ method: request.method(), revision: body?.expectedRevision })
        if (request.method() === 'POST') {
          authority = { ...body, id: 'copied-deck', revision: 1 }
          return route.fulfill({ json: authority })
        }
        if (!authority || body.expectedRevision !== authority.revision)
          return route.fulfill({ status: 409, json: { code: 'deck_revision_conflict', message: '牌库已被其他操作更新，请重新打开' } })
        authority = { ...body.deck, revision: authority.revision + 1 }
        return route.fulfill({ json: authority })
      }
      if (endpoint.pathname.startsWith('/api/')) return route.fulfill({ json: {} })
      return route.continue()
    })
    const a = await context.newPage(), b = await context.newPage()
    for (const page of [a, b]) page.on('pageerror', error => errors.push(error.message))
    const target = `${url}${guest ? '?guest' : ''}`
    await a.goto(target)
    await a.locator('.deck-builder-topbar input').waitFor()
    await a.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '双页面原内容')
    await b.goto(target)
    await b.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '双页面原内容')
    assert.equal(await b.getByRole('button', { name: '保存牌库', exact: true }).isEnabled(), true)
    await b.locator('.deck-builder-topbar input').fill('B的旧草稿')
    await b.getByRole('button', { name: '暂存草稿', exact: true }).click()
    await b.getByText('已暂存到本机', { exact: false }).waitFor()
    await a.locator('.deck-builder-topbar input').fill('A的已确认内容')
    await a.getByRole('button', { name: '保存牌库', exact: true }).click()
    await a.getByText('已保存〈A的已确认内容〉', { exact: false }).waitFor()
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('牌库保存失败', { exact: false }).waitFor()
    await b.screenshot({ path: path.join(out, `${guest ? 'guest' : 'account'}-stale-save.png`), fullPage: true })
    // Recreate a draft from the still stale B editor, then refresh: cache now contains A revision 2.
    await b.getByRole('button', { name: '暂存草稿', exact: true }).click()
    await b.reload()
    await b.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === 'A的已确认内容')
    await b.getByRole('button', { name: '恢复草稿', exact: true }).click()
    assert.equal(await b.locator('.deck-builder-topbar input').inputValue(), 'B的旧草稿')
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('牌库保存失败', { exact: false }).waitFor()
    assert.equal(guest ? await b.evaluate(() => Object.values(window.__tabsQa.loadSavedDecks())[0].name) : authority.name, 'A的已确认内容')
    if (!guest) assert.deepEqual(requests.map(request => request.revision), [1, 1, 1], '旧标签和恢复的旧草稿都必须发送原版本1，不能借用版本2')
    await b.setViewportSize({ width: 390, height: 844 })
    await b.screenshot({ path: path.join(out, `${guest ? 'guest' : 'account'}-draft-conflict-mobile.png`), fullPage: true })
    report.push({ mode: guest ? 'guest' : 'account', staleSave: 'rejected', restoredDraft: 'rejected', authority: 'A preserved', requests })
    if (guest) await b.evaluate(() => localStorage.setItem('l12-custom-decks-v1', '{}'))
    else authority = null
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('牌库保存失败', { exact: false }).waitFor()
    assert.equal(guest ? await b.evaluate(() => Object.keys(window.__tabsQa.loadSavedDecks()).length) : Number(Boolean(authority)), 0,
      '已删除的基线不得由旧草稿变成新建请求复活')
    if (!guest) assert.equal(requests.at(-1).method, 'PUT')
    report.push({ mode: guest ? 'guest' : 'account', deletedDraftSave: 'rejected' })
    await b.setViewportSize({ width: 1440, height: 900 })
    await b.evaluate(guest => {
      const owner = guest ? 'guest:session' : 'account:tabs-qa'
      const storage = guest ? sessionStorage : localStorage
      storage.setItem(`l12:deck-editor-draft:v1:${encodeURIComponent(owner)}`, JSON.stringify({
        schema: 1, owner, savedAt: '2026-10-02', baseDeckName: '双页面原内容',
        deck: { ...window.__tabsQa.seed, id: undefined, revision: undefined, name: '旧版草稿' },
      }))
    }, guest)
    await b.getByRole('button', { name: '恢复草稿', exact: true }).click()
    const writes = requests.length
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('无法确认这份草稿的原牌库版本，请另存为牌库', { exact: true }).waitFor()
    assert.equal(requests.length, writes, '旧schema草稿不能通过普通保存发POST创建')
    assert.equal(guest ? await b.evaluate(() => Object.keys(window.__tabsQa.loadSavedDecks()).length) : Number(Boolean(authority)), 0)
    if (!guest) {
      await b.getByRole('button', { name: '公开牌库', exact: true }).click()
      await b.getByText('无法确认这份草稿的原牌库版本，请另存为牌库', { exact: true }).waitFor()
      assert.equal(requests.length, writes, '公开操作的隐式保存也不能为旧schema草稿发POST')
    }
    await b.getByRole('button', { name: '另存为牌库', exact: true }).click()
    await b.getByText('已另存为〈', { exact: false }).waitFor()
    report.push({ mode: guest ? 'guest' : 'account', legacyDraft: 'save rejected; explicit save-as passed' })
    // Official listing → actual detail → actual copy; community UUID fallback remains forbidden.
    const library = await context.newPage()
    library.on('pageerror', error => errors.push(error.message))
    await library.goto(`${url}?library${guest ? '&guest' : ''}`)
    await library.locator('.plaza-grid .plaza-summary').first().click()
    await library.getByRole('button', { name: '复制到我的牌库', exact: true }).click()
    await library.getByText('已复制《', { exact: false }).waitFor()
    assert.equal(await library.getByText('该牌库暂时没有可用的详情地址').count(), 0)
    await library.screenshot({ path: path.join(out, `${guest ? 'guest' : 'account'}-preset-copy.png`), fullPage: true })
    report.push({ mode: guest ? 'guest' : 'account', presetDetailAndCopy: 'passed' })
    await context.close()
  }
  assert.deepEqual(errors, [])
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ report, errors }, null, 2))
  console.log('Real editor dual-tab / restored-draft conflicts and preset detail/copy passed (account + guest). HTTP CAS is a deterministic fixture; not a deployment receipt.')
} finally {
  await browser?.close()
  await server.close()
}
