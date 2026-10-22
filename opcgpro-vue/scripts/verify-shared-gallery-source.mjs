import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT
const cache = path.join(process.env.L12_WORK_CACHE || '', 'shared-gallery-source-20261023')
assert.ok(output && path.isAbsolute(output) && process.env.L12_PWSH)
const quote = value => `'${value.replaceAll("'", "''")}'`
const guard = spawnSync(process.env.L12_PWSH, ['-NoProfile', '-Command', [
  `. ${quote(path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1'))}`,
  ...[output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value => {
    assert.ok(value && path.isAbsolute(value))
    return `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'shared gallery actual SFC acceptance'`
  }),
].join('\n')], { encoding: 'utf8', windowsHide: true })
assert.equal(guard.status, 0, guard.stderr || guard.stdout)
assert.equal(fs.existsSync(path.join(output, 'report.json')), false)
fs.mkdirSync(output, { recursive: true })
const sha = value => createHash('sha256').update(value).digest('hex')
const leaves = ['src/l12/deploymentBase.ts', 'src/l12/cardAssets.ts', 'src/l12/platform.ts',
  'src/l12/site/AdminAlternateArtsPanel.vue', '../ops/server/legion12-testrun-path.nginx']
const hashes = () => Object.fromEntries(leaves.map(file => [file, sha(fs.readFileSync(path.join(root, file)))]))
const report = { status: 'RUNNING', sourceBefore: hashes(), cases: [], errors: [], forbiddenRequests: [],
  scope: 'Actual admin SFC and CardImage, synthetic admin APIs/media; no real login, nginx activation, business write or deployment' }
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let browser, server
try {
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const base of ['/', '/testrun/']) {
    const entry = `
import {createApp,h} from 'vue'
import '/src/style.css'
import Panel from '/src/l12/site/AdminAlternateArtsPanel.vue'
import CardImage from '/src/l12/CardImage.vue'
import {adminApi,alternateArtApi,platformState,authState} from '/src/l12/platform.ts'
import {l12State} from '/src/l12/net.ts'
const calls=[]
for(const name of ['alternateArtProducts','alternateArtRarities','siteCategories','alternateArtGrants','alternateArtAwardRules','alternateArts'])
  adminApi[name]=async()=>{calls.push(name);return []}
adminApi.searchAlternateArts=async()=>{calls.push('searchAlternateArts');return {items:[],total:0,page:1,pageSize:20}}
platformState.account={id:'synthetic-admin',username:'QA',role:'admin',permissions:['admin.content.read','admin.content.draft']}
platformState.token='synthetic-test-only';authState.verified=true;authState.initialized=true
const endpoint=new URL('${base}ws',location.origin);endpoint.protocol='ws:';l12State.endpoint=endpoint.href
createApp({render:()=>h('main',{},[h(Panel),h('div',{class:'media-proof',style:'width:64px;height:90px'},[h(CardImage,{cardId:'QA',legacyUrl:'/api/site/media/fixture/desktop/card.png',alt:'图片路径验证'})])])}).mount('#app')
window.__sharedGallery={calls}
await Promise.all([alternateArtApi.gallery(),alternateArtApi.presentationCatalog()])
window.__sharedGallery.publicReadDone=true
`
    server = await createServer({ root, base, configLoader: 'runner', cacheDir: path.join(cache, base === '/' ? 'formal' : 'test'),
      server: { host: '127.0.0.1', port: 0, hmr: false }, plugins: [{ name: 'shared-gallery-local-sfc',
        resolveId(id) { if (id.endsWith('/__shared_gallery.js')) return id },
        load(id) { if (id.endsWith('/__shared_gallery.js')) return entry },
        configureServer(vite) { vite.middlewares.use((req, res, next) => {
          if (!req.url?.split('?')[0].endsWith('/__shared_gallery_page')) return next()
          res.setHeader('Content-Type', 'text/html; charset=utf-8')
          res.end(`<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="${base}__shared_gallery.js"></script>`)
        }) },
      }] })
    await server.listen()
    const origin = `http://127.0.0.1:${server.httpServer.address().port}`
    const page = await browser.newPage()
    page.on('pageerror', error => report.errors.push(error.message))
    const mediaRequests = [], publicRequests = []
    await page.route('**/*', async route => {
      const url = new URL(route.request().url())
      if (url.origin !== origin) { report.forbiddenRequests.push(url.origin + url.pathname); return route.abort() }
      if (url.pathname.endsWith('/api/site/media/fixture/desktop/card.png')) {
        mediaRequests.push(url.pathname)
        return route.fulfill({ contentType: 'image/png', body: fs.readFileSync(path.join(root, 'public/images/partners/ka-league.png')) })
      }
      if (['/api/alternate-arts','/api/gallery/presentation'].includes(url.pathname)) {
        const headers=await route.request().allHeaders()
        assert.equal(headers.authorization, undefined);assert.equal(headers.cookie, undefined)
        assert.equal(route.request().method(),'GET')
        publicRequests.push(url.pathname)
        return route.fulfill({contentType:'application/json',body:JSON.stringify(url.pathname==='/api/alternate-arts'?[]:{regularCards:[],filterProducts:[]})})
      }
      if (url.pathname.includes('/api/')) { report.forbiddenRequests.push(url.pathname); return route.abort() }
      return route.continue()
    })
    await page.goto(`${origin}${base}__shared_gallery_page`, { waitUntil: 'networkidle' })
    await page.locator('.alternate-admin').waitFor()
    await page.waitForFunction(()=>window.__sharedGallery?.publicReadDone===true)
    await page.locator('.media-proof img').evaluate(image => image.decode())
    const shared = base === '/testrun/'
    assert.deepEqual(mediaRequests, ['/api/site/media/fixture/desktop/card.png'])
    assert.deepEqual([...publicRequests].sort(),['/api/alternate-arts','/api/gallery/presentation'])
    const calls = await page.evaluate(() => [...window.__sharedGallery.calls])
    assert.ok(shared ? calls.includes('alternateArtGrants') : calls.includes('searchAlternateArts'))
    if (shared) {
      for (const name of ['alternateArtProducts','alternateArtRarities','siteCategories','searchAlternateArts']) assert.equal(calls.includes(name), false)
      assert.equal(await page.locator('.workspace-tabs').count(), 0)
      assert.equal(await page.locator('.gallery-workspace').count(), 0)
      assert.equal(await page.locator('.rights-workspace').count(), 1)
      const link = page.getByRole('link', { name: '打开资料库管理', exact: true })
      assert.equal(await link.getAttribute('href'), '/admin/content/alternate-arts')
      assert.equal(await link.getAttribute('target'), '_blank')
      assert.equal(await link.getAttribute('rel'), 'noopener noreferrer')
      await link.evaluate(element => element.addEventListener('click', event => { event.preventDefault(); window.__sharedLinkClicks=(window.__sharedLinkClicks||0)+1 }))
      await link.focus(); await page.keyboard.press('Enter')
      assert.equal(await page.evaluate(() => window.__sharedLinkClicks), 1)
      await page.getByRole('button', { name: '刷新当前资料', exact: true }).click()
      await page.waitForFunction(() => window.__sharedGallery.calls.filter(name => name === 'alternateArtGrants').length === 2)
      for (const viewport of [{width:1440,height:900},{width:390,height:844}]) {
        await page.setViewportSize(viewport)
        const box = await link.boundingBox()
        assert.ok(box.width >= 44 && box.height >= 44 && box.x >= 0 && box.x + box.width <= viewport.width + 1)
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false)
        await page.screenshot({ path: path.join(output, `test-${viewport.width}.png`), fullPage: true })
      }
    } else {
      assert.equal(await page.locator('.workspace-tabs button').count(), 3)
      assert.equal(await page.locator('.shared-gallery-source').count(), 0)
    }
    report.cases.push({ base, calls, mediaRequests, publicRequests, sharedAdminEntry: shared, actualCardImageDecoded: true })
    await page.close(); await server.close(); server = null
  }
  assert.deepEqual(report.errors, [])
  assert.deepEqual(report.forbiddenRequests, [])
  report.sourceAfter = hashes(); assert.deepEqual(report.sourceAfter, report.sourceBefore)
  report.status = 'LOCAL_SFC_PASSED_NOT_DEPLOYED'
} catch (error) { report.status = 'FAILED'; report.failure = error.stack; throw error }
finally {
  if (browser) await browser.close()
  if (server) await server.close()
  report.ownedBrowserAndViteClosed = true
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report, null, 2))
}
