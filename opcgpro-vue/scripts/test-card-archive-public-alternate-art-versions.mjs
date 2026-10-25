import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..', '..')
const projectRoot = path.join(root, 'opcgpro-vue')
const artifactRoot = process.env.L12_ARTIFACT_ROOT
  || 'D:/GPT/Legion12/artifacts/card-archive-public-alternate-art-versions-20261022'
const cacheRoot = process.env.L12_CACHE_ROOT
  || 'D:/GPT/Legion12/cache/primary/card-archive-public-alternate-art-versions-20261022'
fs.mkdirSync(artifactRoot, { recursive: true })
fs.mkdirSync(cacheRoot, { recursive: true })

const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const archivePath = 'opcgpro-vue/src/l12/CardArchive.vue'
const testPath = 'opcgpro-vue/scripts/test-card-archive-public-alternate-art-versions.mjs'
const archive = read(archivePath)
const serverDirectory = path.join(root, '服务端WebSocket', 'TwelveLegions')
const serverSource = fs.readdirSync(serverDirectory, { withFileTypes: true })
  .filter(entry => entry.isFile() && /^L12WebSocketServer(?:\..+)?\.cs$/.test(entry.name))
  .sort((left, right) => left.name.localeCompare(right.name, 'en'))
  .map(entry => fs.readFileSync(path.join(serverDirectory, entry.name), 'utf8'))
  .join('\n')
const store = read('服务端WebSocket/TwelveLegions/L12PlatformStore.AlternateArts.cs')

assert.match(archive,
  /function alternateArtArchiveName\(baseName: string, artName: string\)[\s\S]*?localeCompare\(normalizedBase,[\s\S]*?\? normalizedBase[\s\S]*?: `\$\{normalizedBase\} · \$\{normalizedArt\}`/,
  '异画名称必须在异画名等于原卡名时去重，并保留真正不同的异画名')
assert.match(archive,
  /nameZh: gallery \? alternateArtArchiveName\(base\.nameZh, art\.displayName\) : base\.nameZh/,
  '画廊投影必须使用异画名称去重规则')
assert.match(archive, /const catalogAlternateArts = computed\(\(\) => galleryArts\.value\)/,
  '全卡池版本组必须使用公开启用异画集合')
assert.doesNotMatch(archive, /alternateArtApi\.mine\(|ownedArts|authState|platformState/,
  '图鉴公开版本浏览不得再由玩家权益或管理员身份过滤')
assert.match(archive, /archiveBaseCardId: base\.id/,
  '公开异画仍须绑定原卡身份，进入同一版本组')
assert.match(serverSource,
  /MapGet\("\/api\/alternate-arts", \(\) => Results\.Ok\(_platform\.AlternateArts\(\)\)\)/,
  '公开版本集合必须继续来自只返回启用异画的公开接口')
assert.match(store,
  /public IReadOnlyList<L12AlternateArtView> AlternateArts\(bool includeInactive = false\)[\s\S]*?_data\.AlternateArts\.Where\(row => includeInactive \|\| row\.Active\)/,
  '公开接口必须排除未启用异画')
assert.match(store, /private Dictionary<string, string> SanitizeOwnedAlternateArtSelections[\s\S]*?owned\.Contains\(art\.Id\)/,
  '构筑异画选择必须继续校验玩家权益')
assert.match(store, /ResolveOwnedAlternateArtUrls[\s\S]*?owned\.Contains\(art\.Id\)/,
  '对战卡图解析必须继续校验玩家权益')

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR42mNkYPj/n4GBgYGJAQoAHgQCAQ9/d+AAAAAASUVORK5CYII=', 'base64')
const entry = `
import { createApp } from 'vue'
import Archive from '/src/l12/CardArchive.vue'
import { loadCardArchiveCatalog } from '/src/l12/decks.ts'
import { l12State } from '/src/l12/net.ts'
import '/src/style.css'
const cards = await loadCardArchiveCatalog()
const base = cards.find(card => card.id === 'S01-0002')
if (!base) throw new Error('S01-0002 acceptance base card is missing')
await fetch('/__qa-base-name?value=' + encodeURIComponent(base.nameZh))
l12State.endpoint = 'ws://' + location.host
window.__qa = { baseName: base.nameZh, sameCode: 'QA-SAME-NAME', distinctCode: 'QA-DISTINCT-NAME' }
createApp(Archive).mount('#app')
`

let publicCatalogRequests = 0
let ownedRequests = 0
let sameDisplayName = ''
const vite = await createServer({
  root: projectRoot,
  configLoader: 'runner',
  cacheDir: path.join(cacheRoot, 'vite'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'public-alternate-art-archive-acceptance',
    resolveId(id) { if (id === '/__public_alternate_art_archive.js') return id },
    load(id) { if (id === '/__public_alternate_art_archive.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((req, res, next) => {
        const pathname = new URL(req.url || '/', 'http://127.0.0.1').pathname
        if (pathname === '/__public_alternate_art_archive') {
          res.setHeader('Content-Type', 'text/html; charset=utf-8')
          res.end('<div id="app"></div><script type="module" src="/__public_alternate_art_archive.js"></script>')
          return
        }
        if (pathname === '/__qa-base-name') {
          sameDisplayName = new URL(req.url || '/', 'http://127.0.0.1').searchParams.get('value') || ''
          res.statusCode = 204
          res.end()
          return
        }
        if (pathname === '/api/alternate-arts') {
          publicCatalogRequests += 1
          res.setHeader('Content-Type', 'application/json; charset=utf-8')
          res.end(JSON.stringify([
            { id: 'qa-same', artCode: 'QA-SAME-NAME', baseCardId: 'S01-0002', displayName: sameDisplayName, mediaAssetId: 'qa-same-media', imageUrl: '/api/site/media/qa-same.png', thumbnailUrl: '/api/site/media/qa-same.png', active: true, createdAt: '', updatedAt: '', kind: 'alternate-art', builtIn: false, products: [{ id: 'early', name: '先发产品', releaseOrder: 1 }] },
            { id: 'qa-distinct', artCode: 'QA-DISTINCT-NAME', baseCardId: 'S01-0002', displayName: '限定异画名', mediaAssetId: 'qa-distinct-media', imageUrl: '/api/site/media/qa-distinct.png', thumbnailUrl: '/api/site/media/qa-distinct.png', active: true, createdAt: '', updatedAt: '', kind: 'alternate-art', builtIn: false, products: [{ id: 'late', name: '后发产品', releaseOrder: 2 }] },
          ]))
          return
        }
        if (pathname === '/api/gallery/presentation') {
          res.setHeader('Content-Type', 'application/json; charset=utf-8')
          res.end(JSON.stringify({ regularCards: [], filterProducts: [{ id: 'early', name: '先发产品', releaseOrder: 1 }, { id: 'late', name: '后发产品', releaseOrder: 2 }] }))
          return
        }
        if (pathname === '/api/me/alternate-arts') {
          ownedRequests += 1
          res.statusCode = 401
          res.end()
          return
        }
        if (pathname === '/api/site/media/qa-same.png' || pathname === '/api/site/media/qa-distinct.png') {
          res.setHeader('Content-Type', 'image/png')
          res.end(png)
          return
        }
        next()
      })
    },
  }],
})

let browser
const cases = []
try {
  await vite.listen()
  const port = vite.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 }]) {
    const page = await browser.newPage({ viewport })
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
    await page.goto(`http://127.0.0.1:${port}/__public_alternate_art_archive`)
    await page.getByRole('tab', { name: '全卡池', exact: true }).waitFor()
    const qa = await page.evaluate(() => window.__qa)

    await page.getByPlaceholder('卡名、编号或效果文字').fill(qa.sameCode)
    const catalogCard = page.locator('.archive-card').first()
    await catalogCard.waitFor()
    assert.equal(await catalogCard.getByRole('button', { name: '下一版本' }).count(), 1,
      '未登录玩家没有获得公开异画版本切换入口')
    await catalogCard.getByRole('button', { name: '下一版本' }).click()
    await catalogCard.locator('img[src*="/api/site/media/qa-"]').waitFor()
    assert.match(await catalogCard.locator('.archive-version-count').textContent(), /^2\/3$/,
      '全卡池没有把两个公开异画与原卡组成三个版本')

    await page.getByRole('tab', { name: '画廊', exact: true }).click()
    await page.getByPlaceholder('卡名、编号或效果文字').fill(qa.sameCode)
    const sameGalleryCard = page.locator('.archive-gallery-card').first()
    await sameGalleryCard.waitFor()
    const sameText = (await sameGalleryCard.textContent()) || ''
    assert.ok(sameText.includes(qa.baseName), '同名异画没有显示原卡名')
    assert.ok(!sameText.includes(`${qa.baseName} · ${qa.baseName}`), '同名异画仍重复显示名称')

    await page.getByPlaceholder('卡名、编号或效果文字').fill(qa.distinctCode)
    const distinctGalleryCard = page.locator('.archive-gallery-card').first()
    await distinctGalleryCard.waitFor()
    assert.ok(((await distinctGalleryCard.textContent()) || '').includes(`${qa.baseName} · 限定异画名`),
      '真正不同的异画名没有保留')
    await page.getByPlaceholder('卡名、编号或效果文字').fill('QA-')
    assert.match(await page.locator('.archive-gallery-card').first().innerText(), /QA-SAME-NAME/, '画廊未默认产品优先排序')
    for (let index = 1; index <= 6; index++) {
      const code = `ST0${index}-C1st`
      await page.getByPlaceholder('卡名、编号或效果文字').fill(code)
      const morale = page.locator('.archive-gallery-card')
      await morale.waitFor()
      assert.equal(await morale.count(), 1, `${code} 未唯一收录为异画`)
      assert.match(await morale.innerText(), /异画 · 士气/)
    }
    await page.getByRole('tab', { name: '全卡池', exact: true }).click()
    for (let index = 1; index <= 6; index++) {
      await page.getByPlaceholder('卡名、编号或效果文字').fill(`ST0${index}-C1st`)
      const morale = page.locator('.archive-card')
      await morale.waitFor()
      assert.equal(await morale.count(), 1, '士气异画没有合入唯一对应士气版本组')
      assert.equal(await morale.getByRole('button', { name: '下一版本', exact: true }).count(), 1)
    }
    await page.screenshot({ path: path.join(artifactRoot, `public-alternate-art-${viewport.width}x${viewport.height}.png`), fullPage: true })
    assert.deepEqual(pageErrors, [], `视口 ${viewport.width}x${viewport.height} 出现页面异常`)
    cases.push({ viewport, publicVersionSwitch: true, duplicateNameSuppressed: true, distinctNamePreserved: true, productFirstOrder: true, sixStMoraleVariantsGrouped: true })
    await page.close()
  }
  assert.equal(ownedRequests, 0, '公开图鉴不应请求玩家异画权益接口')
  assert.equal(publicCatalogRequests, 2, '每个验收页面应只请求一次公开异画目录')

  const sha256 = relative => crypto.createHash('sha256').update(read(relative)).digest('hex').toUpperCase()
  const report = {
    status: 'passed',
    cases,
    requests: { publicCatalogRequests, ownedRequests },
    boundaries: {
      inactiveArtExcludedByPublicApi: true,
      deckAndMatchOwnershipGuardsPreserved: true,
      productionWrite: false,
      serverStarted: false,
    },
    sourceSha256: { [archivePath]: sha256(archivePath), [testPath]: sha256(testPath) },
  }
  const reportPath = path.join(artifactRoot, 'report.json')
  fs.writeFileSync(reportPath, `${JSON.stringify(report, null, 2)}\n`)
  console.log(JSON.stringify({ ...report, reportPath }))
} finally {
  await browser?.close()
  await vite.close()
}
