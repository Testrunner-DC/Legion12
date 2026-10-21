import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import net from 'node:net'
import { spawn, execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import sharp from 'sharp'
import { closeNotificationProbeResources as closeProbeResources } from './verify-notification-real-chain.mjs'

const front = path.resolve(fileURLToPath(new URL('..', import.meta.url)))
const repo = path.dirname(front)
const hostRoot = process.env.L12_GALLERY_HOST_ROOT
assert(hostRoot && path.isAbsolute(hostRoot), 'explicit owned synthetic host is required')
const owner = JSON.parse(fs.readFileSync(path.join(hostRoot, 'owned-gallery-host.json'), 'utf8'))
assert.equal(owner.suite, 'gallery-real-http-v1')
assert.equal(owner.syntheticRuntimeOnly, true)
const digest = value => crypto.createHash('sha256').update(value).digest('hex')
assert.equal(digest(fs.readFileSync(path.join(hostRoot, 'GrandUMIServer.dll'))).toUpperCase(), owner.backendSha256)
const output = process.env.L12_QA_OUT
assert(output && path.isAbsolute(output), 'explicit absolute L12_QA_OUT is required')
const flow = process.env.L12_GALLERY_FLOW || 'standalone'
assert(['standalone', 'product-draft'].includes(flow), 'Unknown gallery flow')
const cache = process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary/gallery-real-http-20261021'
const env = { ...process.env, L12_TASK_REPO: repo, L12_TASK_OUT: output, L12_TASK_HOST: hostRoot, L12_TASK_CACHE: cache }
execFileSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
  ['-NoProfile', '-NonInteractive', '-Command', '$ErrorActionPreference="Stop"; . "$env:L12_TASK_REPO/ops/windows/L12GeneratedStorage.ps1"; '
    + '@($env:L12_TASK_OUT,$env:L12_TASK_HOST,$env:L12_TASK_CACHE) | ForEach-Object { Assert-L12GeneratedPath -Path $_ | Out-Null }; '
    + '. "$env:L12_TASK_REPO/ops/windows/Initialize-L12BuildEnvironment.ps1" -CacheRoot $env:L12_TASK_CACHE | Out-Null'], { env, stdio: 'pipe' })
Object.assign(process.env, { TEMP: path.join(cache, 'temp'), TMP: path.join(cache, 'temp'), TMPDIR: path.join(cache, 'temp') })
assert(!fs.existsSync(output) || !fs.readdirSync(output).length, 'Refusing to overwrite non-empty gallery evidence')
fs.mkdirSync(output, { recursive: true })
const socket = net.createServer()
await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve))
const backendPort = socket.address().port
await new Promise(resolve => socket.close(resolve))
const backendOrigin = `http://127.0.0.1:${backendPort}`
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
let backend, vite, browser, latestPage
let backendLog = '', stage = 'start-backend'
const backendPids = [], errors = [], requests = []
const report = { status: 'running', flow, scope: 'actual admin/gallery components with real local HTTP and synthetic SQLite runtime; no production/test deployment', hostRoot }
async function startBackend() {
  const serverEnv = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('L12_')))
  backendLog = ''
  backend = spawn('C:/Program Files/dotnet/dotnet.exe', [path.join(hostRoot, 'GrandUMIServer.dll'), String(backendPort)],
    { cwd: hostRoot, env: { ...serverEnv, L12_LISTEN_HOST: '127.0.0.1' }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  backendPids.push(backend.pid)
  backend.stdout.on('data', value => { backendLog += value })
  backend.stderr.on('data', value => { backendLog += value })
  for (let index = 0; index < 150; index++) {
    if (backend.exitCode !== null || backend.signalCode !== null) throw new Error('Owned backend exited before ready')
    if (backendLog.includes(`HTTP: http://127.0.0.1:${backendPort}`)) {
      const response = await fetch(backendOrigin + '/health')
      if (response.ok) { assert.equal((await response.json()).cards, 324); return }
    }
    await delay(100)
  }
  throw new Error('Owned backend startup exceeded 15 seconds')
}
async function stopBackend() {
  if (!backend || backend.exitCode !== null || backend.signalCode !== null) return
  const stopped = new Promise(resolve => backend.once('exit', resolve))
  backend.kill()
  await Promise.race([stopped, delay(5000).then(() => { throw new Error('Owned backend did not exit') })])
}
const entry = `
import {createApp,h} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router';
import '/src/style.css';import AdminPanel from '/src/l12/site/AdminAlternateArtsPanel.vue';
import CardArchive from '/src/l12/CardArchive.vue';
import {login,initializeAuth,platformState,alternateArtApi,adminApi,articleApi} from '/src/l12/platform';
import {l12State} from '/src/l12/net';
l12State.endpoint='ws://'+location.host+'/ws';
const view=new URLSearchParams(location.search).get('view')||'admin';
if(view==='admin'){if(platformState.token)await initializeAuth();else await login('Admin','L12master');}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:{render:()=>null}}]});
await router.push('/');const app=createApp({render:()=>h('main',{style:'max-width:1200px;margin:20px auto'},[
 h(view==='admin'?AdminPanel:CardArchive,{onNotice:message=>{document.getElementById('notice').textContent=message}})])});
app.use(router);await router.isReady();app.mount('#app');
window.__galleryHttpQa={readPublic:()=>alternateArtApi.gallery(),readAdminProducts:()=>adminApi.alternateArtProducts(),readPublicProducts:()=>articleApi.list({kind:'product'}),ready:true};
`
async function open(page, view = 'admin') {
  await page.goto(vite.resolvedUrls.local[0] + '__gallery_real_http__?view=' + view)
  await page.waitForFunction(() => Boolean(window.__galleryHttpQa?.ready))
}
async function upload(page, container, color) {
  const bytes = await sharp({ create: { width: 600, height: 840, channels: 3, background: color } }).png().toBuffer()
  await container.locator('input[type=file]').setInputFiles({ name: 'synthetic-card.png', mimeType: 'image/png', buffer: bytes })
  const uploaded = page.waitForResponse(response => response.url().includes('/api/admin/site/media') && response.request().method() === 'POST')
  await container.getByRole('button', { name: '上传图片', exact: true }).click()
  const response = await uploaded
  assert.equal(response.status(), 200, 'real multipart upload succeeds')
  const media = await response.json()
  assert(media.id)
  return media
}
try {
  await startBackend()
  stage = 'start-native-ui'
  vite = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
    resolve: { alias: { '@': path.join(front, 'src') } },
    plugins: [vue(), tailwindcss(), { name: 'real-gallery-acceptance-entry', configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (req.url?.startsWith('/__gallery_real_http__')) {
          res.setHeader('Content-Type', 'text/html; charset=utf-8')
          res.end('<div id="app"></div><output id="notice" role="status"></output><script type="module" src="/@gallery-real-http-entry"></script>'); return
        }
        next()
      })
    }, resolveId(id) { if (id === '/@gallery-real-http-entry') return '\0gallery-real-http-entry' },
      load(id) { if (id === '\0gallery-real-http-entry') return entry } }],
    server: { host: '127.0.0.1', port: 0, proxy: { '/api': backendOrigin, '/health': backendOrigin } } })
  await vite.listen()
  const require = createRequire(import.meta.url)
  const { chromium } = require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  const context = await browser.newContext({ viewport: { width: 1366, height: 900 } })
  await context.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const page = await context.newPage()
  latestPage = page
  page.setDefaultTimeout(10_000)
  page.on('pageerror', error => errors.push(error.message))
  page.on('response', response => { const url = new URL(response.url()); if (url.pathname.startsWith('/api/')) requests.push({ method: response.request().method(), path: url.pathname, status: response.status() }) })
  await open(page)
  if (flow === 'product-draft') {
    stage = 'product-open-existing-card'
    const baseline = (await page.evaluate(() => window.__galleryHttpQa.readPublic())).find(item => item.builtIn)
    assert(baseline, 'A real built-in gallery entry must exist')
    const code = baseline.artCode
    await page.getByLabel('编号', { exact: true }).fill(code)
    await page.getByRole('button', { name: '查询', exact: true }).click()
    const row = page.locator('.gallery-list article').filter({ hasText: code })
    await row.getByRole('button', { name: '编辑资料', exact: true }).click()
    const editor = page.locator('.art-editor')
    await editor.getByRole('button', { name: '新建产品', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '新建产品', exact: true })
    const title = '隔离合成产品-' + crypto.randomUUID().slice(0, 8)
    await dialog.getByLabel('产品名称', { exact: true }).fill(title)
    const category = dialog.getByRole('combobox')
    assert(await category.inputValue(), 'Real server must supply the product category')
    stage = 'product-create-draft-through-ui'
    const savedResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/api/admin/articles' && response.request().method() === 'POST')
    await dialog.getByRole('button', { name: '保存草稿并选用', exact: true }).click()
    const saved = await savedResponse
    assert.equal(saved.status(), 200)
    const product = await saved.json()
    assert.equal(product.kind, 'product'); assert.equal(product.status, 'draft'); assert.equal(product.title, title)
    const productId = 'site:' + product.id
    await dialog.waitFor({ state: 'hidden' })
    const selected = editor.locator('.metadata-choice label').filter({ hasText: title }).locator('input[type=checkbox]')
    assert.equal(await selected.isChecked(), true, 'Created product must be selected in the current card editor')
    await editor.getByRole('button', { name: '保存卡片资料', exact: true }).click()
    await page.locator('.admin-risk-dialog').getByRole('button', { name: '保存并同步画廊', exact: true }).click()
    await editor.waitFor({ state: 'hidden' })
    stage = 'product-reload-and-restart-persistence'
    await stopBackend(); await startBackend(); await open(page)
    const products = await page.evaluate(() => window.__galleryHttpQa.readAdminProducts())
    assert.equal(products.find(item => item.id === productId)?.status, 'draft')
    await page.getByLabel('编号', { exact: true }).fill(code)
    await page.getByRole('button', { name: '查询', exact: true }).click()
    const persistedRow = page.locator('.gallery-list article').filter({ hasText: code })
    await persistedRow.getByText(title, { exact: false }).waitFor()
    await persistedRow.getByRole('button', { name: '编辑资料', exact: true }).click()
    assert.equal(await page.locator('.art-editor .metadata-choice label').filter({ hasText: title }).locator('input[type=checkbox]').isChecked(), true)
    const publicContext = await browser.newContext({ viewport: { width: 1366, height: 900 } })
    await publicContext.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    const publicPage = await publicContext.newPage()
    publicPage.on('pageerror', error => errors.push(error.message))
    await open(publicPage, 'gallery')
    const publicProducts = await publicPage.evaluate(() => window.__galleryHttpQa.readPublicProducts())
    assert.equal(publicProducts.some(item => item.id === product.id), false, 'Draft cannot be published as a product')
    const publicArt = (await publicPage.evaluate(() => window.__galleryHttpQa.readPublic())).find(item => item.id === baseline.id)
    assert(publicArt)
    assert.equal(publicArt.products.some(item => item.id === productId), false, 'Draft attribution must remain hidden')
    assert.deepEqual(publicArt.products, baseline.products, 'Existing public product attribution must stay intact')
    assert.deepEqual(errors, [])
    await page.screenshot({ path: path.join(output, 'product-draft-persisted-selection.png'), fullPage: true })
    Object.assign(report, { status: 'passed', actualProductDraftCreates: 1, actualMultipartUploads: 0,
      selectedInCurrentEditor: true, persistedAfterOwnedRestart: true, publicProductAbsent: true,
      publicGalleryAttributionAbsent: true, originalPublicAttributionUnchanged: true, backendSha256: owner.backendSha256 })
  } else {
  stage = 'create-through-ui'
  await page.getByRole('button', { name: '上传新卡 / 异画' }).click()
  const editor = page.locator('.art-editor')
  await editor.getByLabel('条目类型').selectOption('gallery-card')
  const code = 'COLLAB-HTTP-' + crypto.randomUUID().slice(0, 8).toUpperCase()
  await editor.getByLabel('编号', { exact: true }).fill(code)
  await editor.getByLabel('联动卡名称').fill('合成HTTP展示卡')
  await editor.getByLabel('画师', { exact: true }).fill('合成HTTP画师')
  await editor.getByLabel('联动卡卡效').fill('仅用于隔离验收，不参与对局。')
  const firstMedia = await upload(page, editor, '#127e88')
  await editor.getByRole('button', { name: '保存卡片资料', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '保存并同步画廊', exact: true }).click()
  await editor.waitFor({ state: 'hidden' })
  let art = (await page.evaluate(() => window.__galleryHttpQa.readPublic())).find(item => item.artCode === code)
  assert(art && art.artist === '合成HTTP画师' && art.mediaAssetId === firstMedia.id)
  const artId = art.id
  Object.assign(report, { artId, artCode: code, firstMediaId: firstMedia.id, createdThroughRealUi: true })
  stage = 'reload-and-replace-through-ui'
  await open(page)
  await page.getByLabel('编号', { exact: true }).fill(code)
  await page.getByRole('button', { name: '查询', exact: true }).click()
  const row = page.locator('.gallery-list article').filter({ hasText: code })
  await row.getByRole('button', { name: '更换卡图', exact: true }).click()
  const imageDialog = page.getByRole('dialog', { name: '更换卡图', exact: true })
  const secondMedia = await upload(page, imageDialog, '#8b4fa0')
  assert.notEqual(secondMedia.id, firstMedia.id)
  await imageDialog.getByRole('button', { name: '确认更换', exact: true }).click()
  await page.locator('.admin-risk-dialog').getByRole('button', { name: '确认更换卡图', exact: true }).click()
  await imageDialog.waitFor({ state: 'hidden' })
  art = (await page.evaluate(() => window.__galleryHttpQa.readPublic())).find(item => item.id === artId)
  assert.equal(art.mediaAssetId, secondMedia.id)
  assert.equal(art.artist, '合成HTTP画师')
  Object.assign(report, { secondMediaId: secondMedia.id, replacedThroughRealUi: true })
  const imageResponse = await fetch(backendOrigin + art.imageUrl)
  assert.equal(imageResponse.status, 200)
  assert.match(imageResponse.headers.get('content-type'), /image\/webp/)
  const imageBytes = Buffer.from(await imageResponse.arrayBuffer())
  assert.equal((await sharp(imageBytes).metadata()).width, 600)
  stage = 'restart-and-read-persisted-art'
  await stopBackend(); await startBackend()
  await open(page)
  art = (await page.evaluate(() => window.__galleryHttpQa.readPublic())).find(item => item.id === artId)
  assert.equal(art.mediaAssetId, secondMedia.id)
  assert.equal(art.artist, '合成HTTP画师')
  report.persistenceAfterOwnedRestart = true
  stage = 'public-native-display'
  const publicContext = await browser.newContext({ viewport: { width: 1366, height: 900 } })
  await publicContext.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const publicPage = await publicContext.newPage()
  publicPage.on('pageerror', error => errors.push(error.message))
  await open(publicPage, 'gallery')
  await publicPage.getByRole('tab', { name: '画廊', exact: true }).click()
  await publicPage.getByPlaceholder('卡名、编号或效果文字').fill(code)
  const card = publicPage.locator('.archive-card').filter({ hasText: '合成HTTP展示卡' })
  await card.click()
  await publicPage.getByText('合成HTTP画师', { exact: true }).waitFor()
  const actualImage = publicPage.locator('.archive-detail-image img').first()
  await actualImage.evaluate(async image => { await image.decode(); if (!image.naturalWidth) throw new Error('public card image did not decode') })
  await publicPage.screenshot({ path: path.join(output, 'public-real-uploaded-art.png'), fullPage: true })
  assert.deepEqual(errors, [])
  Object.assign(report, { status: 'passed', artId, artCode: code, firstMediaId: firstMedia.id, secondMediaId: secondMedia.id,
    actualMultipartUploads: 2, persistenceAfterOwnedRestart: true, publicAnonymousComponentAndImageDecoded: true,
    desktopImageSha256: digest(imageBytes), backendSha256: owner.backendSha256 })
  }
} catch (error) {
  report.status = 'failed'; report.stage = stage; report.error = error instanceof Error ? error.message : String(error)
  await latestPage?.screenshot({ path: path.join(output, 'failed-stage.png'), fullPage: true }).catch(() => {})
  throw error
} finally {
  const cleanup = await Promise.allSettled([closeProbeResources(browser, vite), Promise.resolve().then(() => stopBackend())])
  report.cleanupResourceFailures = [...(cleanup[0].status === 'fulfilled' ? cleanup[0].value : ['browser-vite']), ...(cleanup[1].status === 'rejected' ? ['backend'] : [])]
  if (report.cleanupResourceFailures.length) { report.status = 'failed'; process.exitCode = 1 }
  Object.assign(report, { errors, requests, backendPids, backendExited: !backend || backend.exitCode !== null || backend.signalCode !== null,
    browserClosed: !browser || !browser.isConnected(), viteClosed: !vite?.httpServer?.listening,
    credentialValuesRecorded: false, productionOrRemoteRequests: 0 })
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report))
}
