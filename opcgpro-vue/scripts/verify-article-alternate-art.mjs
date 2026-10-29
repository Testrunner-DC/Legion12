import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const front = fileURLToPath(new URL('../', import.meta.url)), repo = path.dirname(front)
const output = process.env.L12_ARTICLE_ART_OUTPUT, cache = process.env.L12_WORK_CACHE, pwsh = process.env.L12_PWSH
const availabilityOnly = process.env.L12_ARTICLE_ART_AVAILABILITY_ONLY === '1'
assert.ok([output, cache, pwsh].every(value => value && path.isAbsolute(value)))
const quote = value => "'" + value.replaceAll("'", "''") + "'"
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. ' + quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1')) + '\n' +
  [output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value =>
    '$null=Assert-L12GeneratedPath -Path ' + quote(value) + " -Purpose 'article artwork browser acceptance'").join('\n')],
{ stdio: 'pipe', windowsHide: true, timeout: 15000 })
fs.mkdirSync(output, { recursive: true })
const watched = ['src/l12/site/ArticleDocumentEditor.vue', 'src/l12/site/ArticleContentRenderer.vue',
  'src/l12/site/articleBlocks.ts', 'src/l12/site/articleCardArt.ts', 'scripts/verify-article-alternate-art.mjs']
const hashes = () => Object.fromEntries(watched.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(front, file))).digest('hex')]))
const publicCards = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/cards.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
const base = publicCards.find(card => card.id === 'S01-0101')
assert.ok(base)
const imageUrl = '/api/site/media/article-synthetic/desktop/' + 'a'.repeat(64) + '.webp'
const arts = [{ id: 'official:article-art', artCode: 'ARTICLE-BUILTIN', baseCardId: base.id, displayName: '公开系统异画',
  active: true, builtIn: true, cardImageId: 'S01-0101b', imageUrl: '', thumbnailUrl: '', kind: 'alternate-art' },
{ id: 'article-custom', artCode: 'ARTICLE-CUSTOM', baseCardId: base.id, displayName: '公开上传异画',
  active: true, builtIn: false, imageUrl, thumbnailUrl: imageUrl, kind: 'alternate-art' }]
const entry = `import {createApp,h,ref} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/site/ArticleDocumentEditor.vue'
import Renderer from '/src/l12/site/ArticleContentRenderer.vue'
import {l12State} from '/src/l12/net'
import '/src/style.css'
l12State.endpoint=location.origin.replace(/^http/,'ws')+'/ws'
const body=ref('旧文章正文'),mode=ref('edit'),epoch=ref(0)
window.__articleArt={body,mode,epoch}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}}]})
await router.push('/')
createApp({render:()=>h('main',{style:{maxWidth:'1000px',margin:'auto',padding:'10px'}},mode.value==='edit'?h(Editor,{key:epoch.value,modelValue:body.value,'onUpdate:modelValue':value=>body.value=value}):h(Renderer,{body:body.value}))}).use(router).mount('#app')`
const report = { schema: 1, status: 'running', sourceBefore: hashes(), cases: [], runtimeErrors: [],
  blockedRequests: [], businessWrites: 0, externalNetworkCalls: 0, lifecycle: { browserClosed: false, serverClosed: false },
  limits: ['Local synthetic public gallery and article only; backend persistence is a separate HTTP fixture.',
    'No production API, real player article, publishing or deck/artwork rights mutation.',
    'Synthetic uploaded artwork pixels are fixture-generated, not an original art asset.'] }
let server, context
let galleryUnavailable = false
try {
  server = await createServer({ root: front, configLoader: 'runner', cacheDir: path.join(cache, 'vite-art'),
    server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'article-art-local-fixture',
      resolveId(id) { if (id === '/__article_art.js') return id }, load(id) { if (id === '/__article_art.js') return entry },
      configureServer(vite) { vite.middlewares.use((req, res, next) => {
        if (req.url?.split('?')[0] !== '/__article_art') return next()
        res.setHeader('Content-Type', 'text/html')
        res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#080d12;color:#eee"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__article_art.js"></script>')
      }) } }] })
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT ||
    'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  context = await chromium.launchPersistentContext(path.join(cache, 'browser-art-profile'), {
    channel: 'msedge', headless: true, reducedMotion: 'reduce', viewport: { width: 1440, height: 1000 },
    env: { ...process.env, TEMP: process.env.TEMP, TMP: process.env.TMP } })
  await context.routeWebSocket(/.*/, socket => socket.close())
  await context.route('**/*', route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) { report.externalNetworkCalls++; return route.abort() }
    if (request.method() !== 'GET') { report.businessWrites++; return route.abort() }
    if (url.pathname === '/api/alternate-arts') return galleryUnavailable
      ? route.fulfill({ status: 503, json: { error: 'synthetic public gallery outage' } }) : route.fulfill({ json: arts })
    if (url.pathname === imageUrl) return route.fulfill({ contentType: 'image/svg+xml',
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="500" height="700"><rect width="500" height="700" fill="#603b84"/><text x="50" y="350" fill="#fff" font-size="40">ARTICLE CUSTOM</text></svg>' })
    if (url.pathname.startsWith('/api/')) { report.blockedRequests.push(url.pathname); return route.abort() }
    if (url.pathname.startsWith('/card-assets/')) {
      const root = path.resolve('D:/L12-assets/published/current'), asset = path.resolve(root, decodeURIComponent(url.pathname.slice(13)))
      return asset.startsWith(root + path.sep) && fs.existsSync(asset) ? route.fulfill({ path: asset }) : route.abort()
    }
    return route.continue()
  })
  for (const [width, height] of availabilityOnly ? [[390, 844]] : [[1440, 1000], [390, 844], [667, 375]]) {
    const page = await context.newPage()
    page.setDefaultTimeout(16000)
    page.on('pageerror', error => report.runtimeErrors.push(error.message))
    await page.setViewportSize({ width, height })
    await page.goto(origin + '/__article_art', { waitUntil: 'domcontentloaded' })
    await page.locator('.editor-canvas p').waitFor()
    assert.equal(await page.locator('.editor-canvas').innerText(), '旧文章正文')
    if (availabilityOnly) {
      await page.getByRole('button', { name: '＋ 单卡', exact: true }).click()
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill('ARTICLE-BUILTIN')
      assert.equal(await page.locator('.single-card-result-card').count(), 1)
      await page.locator('.single-card-picker').getByRole('button', { name: '关闭', exact: true }).click()
      galleryUnavailable = true
      await page.getByRole('button', { name: '＋ 单卡', exact: true }).click()
      await page.getByRole('heading', { name: '插入资讯单卡（异画未载入）', exact: true }).waitFor()
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill('ARTICLE-')
      assert.equal(await page.locator('.single-card-result-card').count(), 0, 'Old artwork options cannot survive gallery failure')
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(base.id)
      await page.locator('.single-card-result-card').first().getByRole('button', { name: '选择', exact: true }).click()
      await page.getByRole('button', { name: '插入单卡', exact: true }).click()
      const degraded = await page.evaluate(() => JSON.parse(window.__articleArt.body.value).blocks.filter(block => block.type === 'card'))
      assert.equal(degraded.length, 1)
      assert.equal(degraded[0].cardId, base.id)
      assert.equal(degraded[0].alternateArtId, undefined)
      galleryUnavailable = false
      await page.locator('.editor-canvas p').last().click()
      await page.getByRole('button', { name: '＋ 单卡', exact: true }).click()
      await page.getByRole('heading', { name: '插入资讯单卡 / 异画', exact: true }).waitFor()
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill('ARTICLE-CUSTOM')
      await page.locator('.single-card-result-card').getByRole('button', { name: '选择', exact: true }).click()
      await page.getByRole('button', { name: '插入单卡', exact: true }).click()
      const recovered = await page.evaluate(() => JSON.parse(window.__articleArt.body.value).blocks.filter(block => block.type === 'card'))
      assert.equal(recovered[1].alternateArtId, arts[1].id)
      await page.screenshot({ path: path.join(output, 'availability-recovered-390x844.png'), fullPage: true })
      report.cases.push({ viewport: { width, height }, mode: 'availability-only', gallery503Injected: true,
        oldArtworkRetired: true, degradedBaseCardInserted: degraded[0], retriedArtworkInserted: recovered[1] })
      await page.close()
      continue
    }
    for (const [index, term] of [base.id, 'ARTICLE-BUILTIN', 'ARTICLE-CUSTOM'].entries()) {
      await page.locator('.editor-canvas p').last().click()
      await page.getByRole('button', { name: '＋ 单卡', exact: true }).click()
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(term)
      const options = page.locator('.single-card-result-card')
      const candidate = index === 0 ? options.filter({ hasNotText: '异画' }).first() : options.first()
      await candidate.getByRole('button', { name: '选择', exact: true }).click()
      await page.locator('.editor-dialog textarea').fill('备注' + index)
      await page.getByRole('button', { name: '插入单卡', exact: true }).click()
      await page.waitForFunction(count => JSON.parse(window.__articleArt.body.value).blocks.filter(block => block.type === 'card').length === count, index + 1)
    }
    const saved = await page.evaluate(() => window.__articleArt.body.value)
    const cards = JSON.parse(saved).blocks.filter(block => block.type === 'card')
    assert.deepEqual(cards.map(card => card.cardId), [base.id, base.id, base.id])
    assert.deepEqual(cards.map(card => card.alternateArtId || ''), ['', arts[0].id, arts[1].id])
    assert.deepEqual(cards.map(card => card.caption), ['备注0', '备注1', '备注2'])
    await page.evaluate(() => window.__articleArt.epoch.value++)
    await page.locator('[data-alternate-art-id="article-custom"]').waitFor()
    await page.locator('[data-alternate-art-id="article-custom"] [data-edit-embed]').click()
    await page.locator('.editor-dialog textarea').fill('重开修改备注')
    await page.getByRole('button', { name: '保存单卡', exact: true }).click()
    assert.equal(await page.evaluate(() => JSON.parse(window.__articleArt.body.value).blocks.filter(block => block.type === 'card')[2].alternateArtId), arts[1].id)
    await page.getByRole('button', { name: '预览文章', exact: true }).click()
    await page.locator('.article-card-figure').nth(2).waitFor()
    await page.waitForFunction(() => [...document.querySelectorAll('.article-card-embed img')].every(img => img.naturalWidth > 0))
    const geometry = await page.locator('.article-card-row').evaluate(row => {
      const positions = [...row.querySelectorAll('.article-card-figure')].map(card => { const r = card.getBoundingClientRect(); return { x: r.x, y: r.y, width: r.width } })
      return { positions, overflow: document.documentElement.scrollWidth > innerWidth + 1 }
    })
    assert.equal(geometry.positions.length, 3)
    assert.ok(geometry.positions.every(position => Math.abs(position.y - geometry.positions[0].y) <= 1))
    assert.equal(geometry.overflow, false)
    const previewImages = await page.locator('.article-card-embed img').evaluateAll(images => images.map(img => img.currentSrc))
    assert.ok(previewImages[2].endsWith(imageUrl))
    await page.locator('.article-card-embed').nth(2).click()
    await page.locator('.catalog-detail-dialog').waitFor()
    await page.waitForFunction(url => document.querySelector('.catalog-detail-dialog .l12-card-image img')?.currentSrc.endsWith(url), imageUrl)
    await page.getByRole('button', { name: '关闭卡牌详情', exact: true }).click()
    await page.screenshot({ path: path.join(output, `article-art-${width}x${height}.png`), fullPage: true })
    await page.evaluate(() => window.__articleArt.mode.value = 'read')
    await page.waitForFunction(url => document.querySelectorAll('.article-card-embed img')[2]?.currentSrc.endsWith(url), imageUrl)
    report.cases.push({ viewport: { width, height }, cards, geometry, previewImages,
      reloadedCaptionArtworkPreserved: true, previewAndReaderArtworkMatch: true, detailArtworkMatches: true })
    fs.writeFileSync(path.join(output, `saved-body-${width}x${height}.json`), await page.evaluate(() => window.__articleArt.body.value))
    await page.close()
  }
  assert.deepEqual(report.runtimeErrors, [])
  assert.deepEqual(report.blockedRequests, [])
  assert.equal(report.businessWrites, 0); assert.equal(report.externalNetworkCalls, 0)
  report.sourceAfter = hashes(); assert.deepEqual(report.sourceBefore, report.sourceAfter)
  report.status = 'passed'
} catch (error) { report.status = 'failed'; report.error = error.stack; throw error }
finally {
  if (context) { await context.close(); report.lifecycle.browserClosed = true }
  if (server) { await server.close(); report.lifecycle.serverClosed = true }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: report.status, cases: report.cases.length, report: path.join(output, 'report.json') }))
}
