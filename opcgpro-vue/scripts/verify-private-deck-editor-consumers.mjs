import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const out = process.env.L12_PRIVATE_EDITOR_OUTPUT || path.join('D:/GPT/Legion12/artifacts/deck-private-editor-20261007', new Date().toISOString().replace(/[:.]/g, '-'))
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const files = ['src/l12/L12DeckEditor.vue', 'src/l12/site/RankingsPage.vue', 'src/l12/decks.ts', 'src/l12/platform.ts',
  'src/l12/deckCacheStorage.ts', 'src/l12/deckCacheCodec.ts', 'scripts/test-private-deck-editor-consumers.mjs',
  'scripts/check-deck-editor-reference-flow.mjs', 'scripts/check-statistics-credibility.mjs', 'scripts/check-ui-contracts.mjs']
const hashes = () => Object.fromEntries(files.map(file => [file, crypto.createHash('sha256').update(fs.readFileSync(path.join(front, file))).digest('hex')]))
const report = { sourceBefore: hashes(), sizes: [], cases: [], errors: [], dialogs: [], externalBlocked: 0,
  limitations: ['Real Edge browser and native Web Locks/localStorage with a synthetic HTTP authority; not real production SQLite.',
    'Browser viewports are not physical iOS Safari, WeChat IME or nonzero hardware safe areas.', 'Card-image fallbacks are not CDN completeness evidence.'], productionWrites: 0 }
const preset = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/preset-decks.s1.json'), 'utf8').replace(/^\uFEFF/, ''))[0]
const restrictedCardId = preset.cardIds.find(id => preset.cardIds.filter(value => value === id).length === 3)
assert.equal(preset.masterId, 'S01-01M1', 'seasonal evidence must use the real official Tianting prebuilt')
const entry = `
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import App from '/src/App.vue'
import Editor from '/src/l12/L12DeckEditor.vue'
import {platformState,authState} from '/src/l12/platform.ts'
import {l12State} from '/src/l12/net.ts'
import {loadSavedDecks} from '/src/l12/decks.ts'
import '/src/style.css'
platformState.account={id:'private-editor-qa',username:'本地验收',role:'player',permissions:[]};platformState.token='synthetic-only-token';authState.initialized=true;authState.verified=true
l12State.endpoint='ws://'+location.host
window.__privateEditor={platformState,loadSavedDecks,locks:0}
const nativeLock=navigator.locks.request.bind(navigator.locks)
navigator.locks.request=(...args)=>{window.__privateEditor.locks++;return nativeLock(...args)}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/deck-editor',component:Editor,meta:{immersive:true,landscapeCanvas:true,editorAdaptiveCanvas:true}},{path:'/decks',component:{template:'<p>返回我的牌库</p>'}},{name:'public-deck-detail',path:'/decks/:deckId',component:{template:'<p>本地公开牌库链接</p>'}}]})
await router.push('/deck-editor?deckId=qa-01&returnTo='+encodeURIComponent('/decks?tab=mine'))
createApp(App).use(router).mount('#app')
`
const server = await createServer({ root: front, configLoader: 'runner', cacheDir: process.env.L12_VITE_CACHE || 'D:/GPT/Legion12/cache/private-editor-browser/vite',
  server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'private-editor-browser-fixture',
    resolveId(id) { if (id === '/__private_editor__.js') return id }, load(id) { if (id === '/__private_editor__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (new URL(request.url || '/', 'http://fixture').pathname !== '/__private_editor__') return next()
      response.setHeader('Content-Type', 'text/html'); response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__private_editor__.js"></script></body></html>')
    }) },
  }] })
let browser, currentPage
const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
async function check(name, work) { report.currentCase = name; await work(); report.cases.push(name); console.log(`PASS browser ${report.cases.length}: ${name}`) }
function makeDecks() {
  return new Map(Array.from({ length: 75 }, (_, index) => {
    const id = `qa-${String(index + 1).padStart(2, '0')}`
    return [id, { ...preset, id, revision: 1, name: `验收牌库 ${String(index + 1).padStart(2, '0')}`,
      publicationId: null, publicationVersion: null, specialIds: preset.specialIds ?? [], benchIds: [],
      alternateArtSelections: {}, alternateArtCopies: {}, updatedAt: '2026-10-07T12:00:00Z' }]
  }))
}
async function fixture(width, height, options = {}) {
  const context = await browser.newContext({ viewport: { width, height }, hasTouch: true, isMobile: width < 820 })
  await context.addInitScript(() => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ musicEnabled: false, sfxEnabled: false, mobileLayout: 'auto', cardSize: 'medium', animation: 'off' })))
  const state = { decks: makeDecks(), reads: [], writes: [], publications: [], publicWrites: [], hold: null, pending: null }
  await context.routeWebSocket(/.*/, socket => socket.close())
  await context.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method()
    if (url.hostname !== '127.0.0.1') { report.externalBlocked++; return route.abort() }
    if (url.pathname === '/api/public-decks' && method === 'POST') {
      const input = request.postDataJSON(), id = 'synthetic-season-public', publicCode = 'SEASONCODE12'
      const published = { id, publicCode, ownerId: 'private-editor-qa', author: '本地验收',
        deck: { ...input.deck, publicationId: id, publicationVersion: 1 }, views: 0, likes: 0, copies: 0,
        liked: false, createdAt: '2026-10-08T00:00:00Z', updatedAt: '2026-10-08T00:00:00Z' }
      state.publicWrites.push({ method, body: input }); state.publications.push(published)
      return route.fulfill({ json: published })
    }
    if (url.pathname === '/api/public-decks' && method === 'GET') return route.fulfill({ json: state.publications })
    if (url.pathname === '/api/public-decks/SEASONCODE12') return route.fulfill({ json: state.publications.at(-1) })
    if (url.pathname === '/api/decks' && method === 'POST') {
      const input = request.postDataJSON(), id = `qa-copy-${state.decks.size + 1}`
      const updated = { ...input, id, revision: 1 }
      state.writes.push({ method, id, body: input }); state.decks.set(id, updated)
      return route.fulfill({ json: updated })
    }
    if (url.pathname === '/api/decks') { state.reads.push(url.pathname); return route.fulfill({ status: 500, json: { message: 'full collection forbidden by fixture' } }) }
    if (url.pathname === '/api/decks/summaries') {
      state.reads.push(url.pathname + url.search)
      const page = Number(url.searchParams.get('page')), pageSize = Number(url.searchParams.get('pageSize'))
      const keyword = url.searchParams.get('keyword') || ''
      const exactName = url.searchParams.get('exactName'), publicationId = url.searchParams.get('publicationId')
      const all = [...state.decks.values()].filter(deck => deck.name.includes(keyword)
        && (!exactName || deck.name.trim().toUpperCase() === exactName.trim().toUpperCase())
        && (!publicationId || deck.publicationId === publicationId))
      const items = all.slice((page - 1) * pageSize, page * pageSize).map(deck => ({ id: deck.id, revision: deck.revision, name: deck.name,
        masterId: deck.masterId, updatedAt: deck.updatedAt, publicationId: null, publicationVersion: null,
        counts: { main: deck.cardIds.length, uncountedMain: 0, morale: deck.moraleIds.length, special: deck.specialIds.length, bench: 0 }, legal: true, legalityReason: null }))
      return route.fulfill({ json: { items, total: all.length, page, pageSize, generation: 7, permissionVersion: 1,
        catalogVersion: 'A'.repeat(64), policyVersion: 2, facets: { masters: all.length ? [{ masterId: preset.masterId, count: all.length }] : [], legal: all.length, illegal: 0 } } })
    }
    if (url.pathname.startsWith('/api/decks/by-id/')) {
      const id = decodeURIComponent(url.pathname.split('/').at(-1)), deck = state.decks.get(id)
      if (method === 'GET') {
        state.reads.push(url.pathname + url.search)
        if (state.hold === id) { state.pending = route; return }
        if (!deck) return route.fulfill({ status: 404, json: { message: '牌库已删除' } })
        if (Number(url.searchParams.get('expectedRevision')) !== deck.revision)
          return route.fulfill({ status: 409, json: { code: 'deck_revision_conflict', message: '牌库已被其他操作更新', currentRevision: deck.revision } })
        return route.fulfill({ json: deck })
      }
      const input = request.postData() ? request.postDataJSON() : null
      state.writes.push({ method, id, body: input })
      const expected = method === 'DELETE' ? Number(url.searchParams.get('expectedRevision')) : input.expectedRevision
      if (!deck || expected !== deck.revision)
        return route.fulfill({ status: 409, json: { code: 'deck_revision_conflict', message: '牌库已被其他操作更新', currentRevision: deck?.revision ?? 1 } })
      if (method === 'DELETE') { state.decks.delete(id); return route.fulfill({ json: { deleted: true, id } }) }
      const updated = { ...input.deck, id, revision: deck.revision + 1 }; state.decks.set(id, updated)
      return route.fulfill({ json: updated })
    }
    if (url.pathname === '/api/operations/effective-policy') return options.policyUnavailable
      ? route.fulfill({ status: 503, json: { message: '合成赛季策略不可用' } })
      : route.fulfill({ json: { cardRestrictions: options.maxCopies === undefined ? []
        : [{ cardId: restrictedCardId, maxCopies: options.maxCopies, reason: '浏览器验收赛季规则' }], defaultPresetDeckIds: [], ranked: {}, season: {} } })
    if (url.pathname === '/api/auth/me') return route.fulfill({ json: { id: 'private-editor-qa', username: '本地验收', role: 'player', permissions: [] } })
    if (url.pathname === '/api/friends/overview') return route.fulfill({ json: { friends: [], requests: [], blocked: [] } })
    if (url.pathname.includes('notifications')) return route.fulfill({ json: { items: [], nextCursor: null } })
    if (url.pathname.startsWith('/api/telemetry/')) return route.fulfill({ status: 204 })
    if (url.pathname.startsWith('/api/')) return route.fulfill({ json: [] })
    if (url.pathname.startsWith('/card-assets/')) {
      const assets = path.resolve('D:/L12-assets/published/current'), file = path.resolve(assets, decodeURIComponent(url.pathname.slice('/card-assets/'.length)))
      if (file.startsWith(assets + path.sep) && fs.existsSync(file)) return route.fulfill({ path: file })
      return route.abort()
    }
    return route.continue()
  })
  const page = await context.newPage(); page.setDefaultTimeout(8000)
  page.on('dialog', dialog => report.dialogs.push({ type: dialog.type(), message: dialog.message() }))
  page.on('pageerror', error => report.errors.push(error.stack || error.message))
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__private_editor__`)
  await page.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '验收牌库 01')
  const input = page.locator('.deck-builder-topbar input')
  const triggers = page.locator('.mobile-saved-decks-nav-trigger,.mobile-saved-decks-side-trigger')
  let mobile = false
  for (let i = 0; i < await triggers.count(); i++) if (await triggers.nth(i).isVisible()) {
    await triggers.nth(i).click(); mobile = true; break
  }
  const directory = page.locator(mobile ? '.mobile-saved-decks-dialog' : '.saved-decks-panel')
  const rows = directory.locator(mobile ? '.mobile-saved-decks-list>button' : '.saved-list article>button:first-child')
  return { context, page, state, input, directory, rows, mobile }
}
try {
  await server.listen(); browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const [width, height] of process.env.L12_EDITOR_ADVANCED_ONLY ? [] : [[1920, 1080], [1366, 768], [1024, 500], [844, 390], [390, 844], [320, 568]]) {
    const { context, page, state, input, directory, rows, mobile } = await fixture(width, height)
    currentPage = page
    const size = `${width}x${height}`
    await check(`${size}: bounded metadata and one real body, no full collection`, async () => {
      assert.equal(state.reads.filter(value => value === '/api/decks').length, 0)
      assert.equal(state.reads.filter(value => value.startsWith('/api/decks/by-id/')).length, 1)
      assert.equal(await rows.count(), 30)
      assert.equal(await page.evaluate(() => window.__privateEditor.locks > 0), true, 'actual native Web Lock path must execute')
      assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('l12-custom-decks-v1:private-editor-qa')).format), 'l12-deck-cache')
    })
    await check(`${size}: paging leaves the current deck intact and every row reachable`, async () => {
      await directory.getByRole('button', { name: '下一页', exact: true }).click()
      await rows.first().filter({ hasText: '验收牌库 31' }).waitFor()
      assert.equal(await input.inputValue(), '验收牌库 01')
      await directory.getByRole('button', { name: '下一页', exact: true }).click()
      await rows.first().filter({ hasText: '验收牌库 61' }).waitFor(); assert.equal(await rows.count(), 15)
      const last = rows.last(); await last.scrollIntoViewIfNeeded()
      assert.equal(await last.evaluate(element => { const r = element.getBoundingClientRect(); return r.bottom <= innerHeight + 1 && r.top >= 0 }), true)
      await page.screenshot({ animations: 'disabled', timeout: 15000, path: path.join(out, `${size}-directory.png`) })
      await last.click(); if (mobile) await directory.waitFor({ state: 'hidden' })
      await page.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '验收牌库 75')
    })
    await check(`${size}: statistics and opening hand remain actionable`, async () => {
      const nav = page.locator('.deck-mobile-nav button').filter({ hasText: '统计 / 起手' })
      if (await nav.isVisible()) await nav.click(); else await page.locator('.workspace-tabs button').filter({ hasText: '统计' }).click()
      assert.equal(await page.locator('[data-editor-workspace="stats"]').isVisible(), true)
      await page.locator('.workspace-tabs button').filter({ hasText: '起手' }).click()
      await page.getByRole('button', { name: '重新试抽', exact: true }).click()
      assert.equal(await page.locator('.editor-opening-hand article').count(), 6)
    })
    await check(`${size}: dialog containment and original side-column separation`, async () => {
      if (mobile) {
        await page.locator('.mobile-saved-decks-nav-trigger,.mobile-saved-decks-side-trigger').filter({ visible: true }).first().click()
        const geometry = await directory.evaluate(element => { const r = element.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: innerWidth, height: innerHeight } })
        assert.ok(geometry.left >= -1 && geometry.right <= geometry.width + 1 && geometry.top >= -1 && geometry.bottom <= geometry.height + 1)
        await directory.getByRole('button', { name: '关闭已保存牌库', exact: true }).click()
      }
      const geometry = await page.evaluate(() => ({ client: document.documentElement.clientWidth, full: document.documentElement.scrollWidth }))
      assert.ok(geometry.full <= geometry.client + 1)
    })
    await page.screenshot({ animations: 'disabled', timeout: 15000, path: path.join(out, `${size}-workspace.png`) })
    report.sizes.push({ width, height, mobile, apiReads: state.reads, writes: state.writes, nativeLocks: await page.evaluate(() => window.__privateEditor.locks) })
    await context.close()
  }
  const advanced = await fixture(1440, 900)
  currentPage = advanced.page
  const b = await advanced.context.newPage(); b.setDefaultTimeout(8000)
  b.on('pageerror', error => report.errors.push(error.stack || error.message))
  await b.goto(`http://127.0.0.1:${server.httpServer.address().port}/__private_editor__`)
  await b.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '验收牌库 01')
  await check('native shared cache: stale tab and restored old draft cannot overwrite confirmed content', async () => {
    await b.locator('.deck-builder-topbar input').fill('B保留的旧草稿')
    await b.getByRole('button', { name: '暂存草稿', exact: true }).click()
    await b.getByText('已暂存到本机', { exact: false }).waitFor()
    await advanced.input.fill('A已确认的新内容')
    await advanced.page.getByRole('button', { name: '保存牌库', exact: true }).click()
    await advanced.page.getByText('已保存〈A已确认的新内容〉', { exact: false }).waitFor()
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('牌库保存失败', { exact: false }).waitFor()
    assert.equal(advanced.state.decks.get('qa-01').revision, 2)
    assert.equal(advanced.state.decks.get('qa-01').name, 'A已确认的新内容')
    await b.reload(); await b.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === 'A已确认的新内容')
    await b.getByRole('button', { name: '恢复草稿', exact: true }).click()
    assert.equal(await b.locator('.deck-builder-topbar input').inputValue(), 'B保留的旧草稿')
    await b.getByRole('button', { name: '保存牌库', exact: true }).click()
    await b.getByText('牌库保存失败', { exact: false }).waitFor()
    assert.equal(advanced.state.decks.get('qa-01').revision, 2)
    assert.deepEqual(advanced.state.writes.map(item => item.body.expectedRevision), [1, 1, 1])
    assert.equal(await b.evaluate(() => Object.values(window.__privateEditor.loadSavedDecks()).find(deck => deck.id === 'qa-01').name), 'A已确认的新内容')
    await b.screenshot({ animations: 'disabled', timeout: 15000, path: path.join(out, 'actual-stale-draft-conflict.png') })
  })
  await check('real late body after editing cannot change the current document or local cache', async () => {
    advanced.state.hold = 'qa-02'
    await advanced.directory.locator('.saved-list article>button:first-child').filter({ hasText: '验收牌库 02' }).click()
    for (let attempt = 0; !advanced.state.pending && attempt < 80; attempt++) await pause(25)
    assert.ok(advanced.state.pending, 'selected HTTP body is actually pending')
    await advanced.input.fill('读取期间的新修改')
    const pending = advanced.state.pending; advanced.state.pending = null; advanced.state.hold = null
    await pending.fulfill({ json: advanced.state.decks.get('qa-02') }); await pause(150)
    assert.equal(await advanced.input.inputValue(), '读取期间的新修改')
    assert.equal(await advanced.page.evaluate(() => Object.values(window.__privateEditor.loadSavedDecks()).some(deck => deck.id === 'qa-02')), false)
  })
  await check('actual storage quota failure preserves the prior envelope and editor', async () => {
    const before = await advanced.page.evaluate(() => localStorage.getItem('l12-custom-decks-v1:private-editor-qa'))
    advanced.page.once('dialog', dialog => dialog.accept())
    await advanced.page.evaluate(() => {
      const original = Storage.prototype.setItem
      window.__privateEditor.restoreStorage = () => { Storage.prototype.setItem = original }
      Storage.prototype.setItem = function(key, value) {
        if (key === 'l12-custom-decks-v1:private-editor-qa') throw new DOMException('Synthetic quota', 'QuotaExceededError')
        return original.call(this, key, value)
      }
    })
    await advanced.directory.locator('.saved-list article>button:first-child').filter({ hasText: '验收牌库 03' }).click()
    await advanced.page.getByText('本机牌库缓存写入失败', { exact: false }).waitFor()
    assert.equal(await advanced.input.inputValue(), '读取期间的新修改')
    assert.equal(await advanced.page.evaluate(() => localStorage.getItem('l12-custom-decks-v1:private-editor-qa')), before)
    await advanced.page.evaluate(() => window.__privateEditor.restoreStorage())
  })
  await check('actual save-as checks an uncached off-page name and creates one independent identity', async () => {
    const existing = advanced.state.decks.get('qa-75')
    existing.name = '读取期间的新修改 副本'
    assert.equal(await advanced.page.evaluate(() => Object.values(window.__privateEditor.loadSavedDecks()).some(deck => deck.id === 'qa-75')), false)
    const previousReads = advanced.state.reads.length, previousWrites = advanced.state.writes.length
    await advanced.page.getByRole('button', { name: '另存为牌库', exact: true }).click()
    await advanced.page.getByText('已另存为〈读取期间的新修改 副本 2〉', { exact: false }).waitFor()
    assert.equal(advanced.state.writes.length, previousWrites + 1)
    const write = advanced.state.writes.at(-1)
    assert.equal(write.method, 'POST'); assert.equal(write.body.name, '读取期间的新修改 副本 2')
    assert.notEqual(write.id, 'qa-01'); assert.equal(existing.name, '读取期间的新修改 副本')
    const names = advanced.state.reads.slice(previousReads).filter(value => value.includes('exactName='))
    assert.equal(names.length, 2); assert.ok(names.every(value => value.includes('pageSize=1') && value.includes('page=1')))
    await advanced.page.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '读取期间的新修改 副本 2'
      && !document.querySelector('.deck-builder-topbar .primary')?.disabled)
    report.saveAsState = await advanced.page.evaluate(() => ({ fonts: document.fonts.status, animations: document.getAnimations().length,
      ready: document.readyState, visibility: document.visibilityState, name: document.querySelector('.deck-builder-topbar input')?.value }))
    await advanced.page.bringToFront()
    await advanced.page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    assert.equal(await advanced.page.evaluate(() => document.visibilityState), 'visible')
    await advanced.page.screenshot({ animations: 'disabled', timeout: 15000, path: path.join(out, 'actual-save-as-off-page-name.png') })
  })
  await check('actual active-deck deletion resets only the unchanged document without a false preserved-draft message', async () => {
    const savedId = advanced.state.writes.at(-1).id
    await advanced.page.getByRole('button', { name: '删除牌库', exact: true }).click()
    await advanced.page.getByRole('button', { name: '继续删除', exact: true }).click()
    await advanced.page.getByText('已删除〈读取期间的新修改 副本 2〉', { exact: true }).waitFor()
    assert.equal(await advanced.input.inputValue(), '新牌库')
    assert.equal(advanced.state.decks.has(savedId), false)
    assert.equal(await advanced.page.getByText('当前修改仍保留', { exact: false }).count(), 0)
  })
  await advanced.context.close()
  const identity = await fixture(1440, 900)
  currentPage = identity.page
  await check('real same-account token refresh invalidates pending body but preserves the working document', async () => {
    identity.state.hold = 'qa-02'
    await identity.rows.filter({ hasText: '验收牌库 02' }).click()
    for (let attempt = 0; !identity.state.pending && attempt < 80; attempt++) await pause(25)
    assert.ok(identity.state.pending)
    await identity.input.fill('令牌刷新仍保留我的修改')
    await identity.page.evaluate(() => { window.__privateEditor.platformState.token = 'synthetic-refreshed-token' })
    const pending = identity.state.pending; identity.state.pending = null; identity.state.hold = null
    await pending.fulfill({ json: identity.state.decks.get('qa-02') })
    await identity.page.waitForFunction(() => document.querySelector('.deck-builder-topbar input')?.value === '令牌刷新仍保留我的修改')
    await pause(150)
    assert.equal(await identity.page.evaluate(() => Object.values(window.__privateEditor.loadSavedDecks()).some(deck => deck.id === 'qa-02')), false)
  })
  await check('real account A-B-A preserves the original draft and cannot apply a late old body', async () => {
    identity.page.once('dialog', dialog => dialog.accept())
    identity.state.hold = 'qa-03'
    await identity.directory.locator('.saved-list article>button:first-child').filter({ hasText: '验收牌库 03' }).click()
    for (let attempt = 0; !identity.state.pending && attempt < 80; attempt++) await pause(25)
    assert.ok(identity.state.pending)
    await identity.page.evaluate(() => {
      const previous = window.__privateEditor.platformState.account
      window.__privateEditor.platformState.account = { ...previous, id: 'synthetic-other-owner' }
      window.__privateEditor.platformState.account = previous
    })
    const pending = identity.state.pending; identity.state.pending = null; identity.state.hold = null
    await pending.fulfill({ json: identity.state.decks.get('qa-03') }); await pause(150)
    assert.equal(await identity.input.inputValue(), '新牌库')
    assert.equal(await identity.page.evaluate(() => Object.values(window.__privateEditor.loadSavedDecks()).some(deck => deck.id === 'qa-03')), false)
    await identity.page.getByRole('button', { name: '恢复草稿', exact: true }).click()
    assert.equal(await identity.input.inputValue(), '令牌刷新仍保留我的修改')
  })
  await identity.context.close()
  const seasonalResults = []
  for (const [width, height] of [[1920, 1080], [844, 390], [390, 844]]) for (const maxCopies of [0, 1]) {
    const seasonal = await fixture(width, height, { maxCopies })
    currentPage = seasonal.page
    const { page, state, directory, mobile } = seasonal
    if (mobile) await directory.getByRole('button', { name: '关闭已保存牌库', exact: true }).click()
    const name = `${width}x${height}-season-limit-${maxCopies}`
    const pane = async value => {
      const more = page.getByRole('button', { name: '更多操作', exact: true })
      if (await more.isVisible() && await more.getAttribute('aria-expanded') === 'true') await more.click()
      const nav = page.locator('.deck-mobile-nav')
      if (await nav.isVisible()) await nav.getByRole('button', { name: value === 'pool' ? '牌库' : /^牌表/, exact: value === 'pool' }).click()
      else if (value === 'pool') await page.locator('.workspace-tabs').getByRole('button', { name: '牌库', exact: true }).click()
      if (value === 'pool') await page.locator('.catalog-tabs').getByRole('button', { name: '主牌库', exact: true }).click()
    }
    const action = async label => {
      const target = page.getByRole('button', { name: label, exact: true })
      if (!await target.isVisible()) await page.getByRole('button', { name: '更多操作', exact: true }).click()
      await target.scrollIntoViewIfNeeded(); assert.equal(await target.isEnabled(), true)
      assert.equal(await target.evaluate(button => { const r = button.getBoundingClientRect();
        const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return button === hit || button.contains(hit) }), true,
      `${label} must be physically reachable`)
      await target.click()
    }
    const mainRow = () => page.locator('[data-deck-section="main"] .deck-entry-row').filter({ has: page.locator('small').filter({ hasText: restrictedCardId }) }).first()
    let geometry
    await check(`${name}: season advisory fits and pool/deck remain reachable`, async () => {
      await page.locator('[data-season-advisory]').filter({ hasText: '排位不可用' }).waitFor()
      assert.match(await page.locator('.deck-list footer').textContent(), /基础构筑合法/)
      await pane('pool')
      await page.getByLabel('本赛季排位').selectOption(maxCopies ? 'restricted' : 'banned')
      const pool = page.locator(`.deck-card[data-card-id="${restrictedCardId}"]`).first()
      await pool.scrollIntoViewIfNeeded(); assert.equal(await pool.isVisible(), true)
      assert.match(await pool.locator('.entry-issue').innerText(), /本赛季排位/)
      await page.getByLabel('本赛季排位').selectOption('all')
      await pane('deck'); await mainRow().scrollIntoViewIfNeeded()
      assert.match(await mainRow().locator('em').innerText(), /本赛季排位/)
      geometry = await page.evaluate(() => {
        const read = selector => { const e = document.querySelector(selector), r = e.getBoundingClientRect();
          return { x: r.x, y: r.y, right: r.right, bottom: r.bottom, height: r.height, width: r.width,
            scrollWidth: e.scrollWidth, clientWidth: e.clientWidth } }
        return { viewport: { width: innerWidth, height: innerHeight }, advisory: read('[data-season-advisory]'), grid: read('.deck-builder-grid') }
      })
      assert(geometry.grid.height > 80, 'the advisory must leave usable editor workspace height')
      assert(geometry.grid.bottom <= height + 1 && geometry.grid.right <= width + 1)
      assert(geometry.advisory.scrollWidth <= geometry.advisory.clientWidth + 1)
      assert(geometry.advisory.bottom <= geometry.grid.y + 1, 'advisory must not overlap the editable workspace')
      await page.screenshot({ animations: 'disabled', path: path.join(out, `${name}-advisory.png`) })
    })
    await check(`${name}: original plus controls reach inherent three copies but cannot add a fourth`, async () => {
      const row = mainRow()
      await row.getByRole('button', { name: '减少一张', exact: true }).click()
      await row.getByRole('button', { name: '减少一张', exact: true }).click()
      assert.equal(await row.locator('strong').innerText(), '×1')
      await row.getByRole('button', { name: '增加一张', exact: true }).click()
      await row.getByRole('button', { name: '增加一张', exact: true }).click()
      assert.equal(await row.locator('strong').innerText(), '×3')
      assert.equal(await row.getByRole('button', { name: '增加一张', exact: true }).isDisabled(), true)
    })
    await check(`${name}: real save/save-as/public HTTP payloads retain the seasonal cards`, async () => {
      await action('保存牌库'); await page.getByText('已保存〈验收牌库 01〉', { exact: false }).waitFor()
      assert.deepEqual([...state.writes.at(-1).body.deck.cardIds].sort(), [...preset.cardIds].sort())
      await action('另存为牌库'); await page.getByText('已另存为〈验收牌库 01 副本〉', { exact: false }).waitFor()
      assert.equal(state.writes.at(-1).method, 'POST')
      assert.equal(state.writes.at(-1).body.id, undefined)
      assert.deepEqual([...state.writes.at(-1).body.cardIds].sort(), [...preset.cardIds].sort())
      await action('公开牌库'); await page.getByText('已公开〈验收牌库 01 副本〉', { exact: false }).waitFor()
      assert.equal(state.publicWrites.length, 1)
      assert.deepEqual([...state.publicWrites[0].body.deck.cardIds].sort(), [...preset.cardIds].sort())
      assert.equal(state.writes.at(-1).body.deck.publicationId, 'synthetic-season-public')
      await page.screenshot({ animations: 'disabled', path: path.join(out, `${name}-published.png`) })
    })
    await check(`${name}: actual canvas produces a PNG through the real image action`, async () => {
      await action('生成牌库图')
      const image = page.locator('.deck-image-dialog>img'); await image.waitFor({ timeout: 20000 })
      const actualImage = await image.evaluate(async element => {
        await element.decode(); const blob = await (await fetch(element.src)).blob()
        return { width: element.naturalWidth, height: element.naturalHeight, type: blob.type, bytes: blob.size,
          signature: [...new Uint8Array(await blob.slice(0, 8).arrayBuffer())] }
      })
      assert.equal(actualImage.type, 'image/png'); assert.equal(actualImage.width, 1920); assert.equal(actualImage.height, 1080)
      assert.deepEqual(actualImage.signature, [137, 80, 78, 71, 13, 10, 26, 10]); assert(actualImage.bytes > 10000)
      await image.screenshot({ path: path.join(out, `${name}-canvas.png`) })
      seasonalResults.push({ width, height, maxCopies, geometry, actualImage, writes: state.writes, publicWrites: state.publicWrites })
      await page.locator('.deck-image-dialog>header').getByRole('button', { name: '关闭', exact: true }).click()
    })
    await check(`${name}: an actual 39-card edit still disables save/copy/public actions`, async () => {
      await pane('deck'); await mainRow().getByRole('button', { name: '减少一张', exact: true }).click()
      const before = [state.writes.length, state.publicWrites.length]
      assert.equal(await page.getByRole('button', { name: '保存牌库', exact: true }).isDisabled(), true)
      if (!await page.getByRole('button', { name: '另存为牌库', exact: true }).isVisible())
        await page.getByRole('button', { name: '更多操作', exact: true }).click()
      for (const label of ['另存为牌库', '更新公开牌库', '生成牌库图'])
        assert.equal(await page.getByRole('button', { name: label, exact: true }).isDisabled(), true)
      assert.deepEqual([state.writes.length, state.publicWrites.length], before)
      assert.match(await page.locator('.deck-list footer').textContent(), /39/)
    })
    await seasonal.context.close()
  }
  for (const [width, height] of [[1920, 1080], [844, 390], [390, 844]]) {
    const unavailable = await fixture(width, height, { policyUnavailable: true })
    currentPage = unavailable.page
    if (unavailable.mobile) await unavailable.directory.getByRole('button', { name: '关闭已保存牌库', exact: true }).click()
    await check(`${width}x${height}: policy 503 stays unconfirmed while actual base-valid save succeeds`, async () => {
      await unavailable.page.locator('[data-season-advisory]').filter({ hasText: '待确认' }).waitFor()
      assert.equal(await unavailable.page.getByRole('button', { name: '保存牌库', exact: true }).isEnabled(), true)
      await unavailable.page.getByRole('button', { name: '保存牌库', exact: true }).click()
      await unavailable.page.getByText('已保存〈验收牌库 01〉', { exact: false }).waitFor()
      assert.equal(unavailable.state.writes.length, 1)
      assert.deepEqual([...unavailable.state.writes[0].body.deck.cardIds].sort(), [...preset.cardIds].sort())
      await unavailable.page.screenshot({ animations: 'disabled', path: path.join(out, `${width}x${height}-policy-unavailable.png`) })
    })
    await unavailable.context.close()
  }
  report.seasonalResults = seasonalResults
  report.sourceAfter = hashes(); assert.deepEqual(report.sourceAfter, report.sourceBefore)
  assert.deepEqual(report.errors, [])
  console.log(`Private editor real-browser: ${report.cases.length} checks, ${report.sizes.length} viewports, errors=0`)
} catch (error) {
  report.failure = String(error.stack)
  if (currentPage && !currentPage.isClosed()) {
    await currentPage.screenshot({ path: path.join(out, 'failed-screen.png') }).catch(() => undefined)
    report.failureGeometry = await currentPage.locator('.saved-decks-panel,.saved-list,.saved-directory-search,.saved-directory-pages').evaluateAll(elements => elements.map(element => {
      const r = element.getBoundingClientRect(), c = getComputedStyle(element)
      return { className: element.className, top: r.top, bottom: r.bottom, height: r.height, clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight, overflow: c.overflowY }
    })).catch(() => [])
  }
  throw error
}
finally { report.sourceAfter = hashes(); report.sourceUnchanged = JSON.stringify(report.sourceAfter) === JSON.stringify(report.sourceBefore);
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2)); await browser?.close(); await server.close() }
