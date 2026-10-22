import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { createServer } from 'vite'

const root = fileURLToPath(new URL('../', import.meta.url))
const output = process.env.L12_QA_OUT
const cache = path.join(process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary', 'home-partner-link-20261023')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
assert.ok(output && path.isAbsolute(output), 'Explicit absolute L12_QA_OUT required')
assert.ok(process.env.L12_PWSH && path.isAbsolute(process.env.L12_PWSH), 'Initialize the governed build environment first')
const quote = value => `'${value.replaceAll("'", "''")}'`
const guard = spawnSync(process.env.L12_PWSH, ['-NoProfile', '-NonInteractive', '-Command', [
  `. ${quote(path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1'))}`,
  ...[output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value => {
    assert.ok(value && path.isAbsolute(value), 'Explicit absolute governed temp roots required')
    return `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'home partner actual SFC preview'`
  }),
].join('\n')], { encoding: 'utf8', windowsHide: true })
assert.equal(guard.status, 0, `Physical output guard failed: ${guard.stderr || guard.stdout}`)
assert.equal(fs.existsSync(path.join(output, 'report.json')), false, 'Never replace accepted preview evidence')
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(path.join(cache, 'temp'), { recursive: true })
process.env.TEMP = process.env.TMP = process.env.TMPDIR = path.join(cache, 'temp')
const target = 'https://legion-12.shmxwd.com/community/index.html'
const files = ['src/l12/site/OfficialHomePage.vue', 'src/l12/site/SiteShell.vue', 'public/images/partners/ka-league.png']
const sha256 = value => createHash('sha256').update(value).digest('hex').toUpperCase()
const sourceHashes = Object.fromEntries(files.map(file => [file, sha256(fs.readFileSync(path.join(root, file)))]))
assert.equal(sourceHashes['public/images/partners/ka-league.png'], '7B4904A5B1A280323A28B2657FFFBC2655EF8F8CE3D4145017E8F7CB673A24A5', 'Logo must be a byte-identical copy of the supplied image')

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
const platform=await import('/src/l12/platform.ts')
platform.platformState.account=null;platform.platformState.token=''
platform.authState.initialized=true;platform.authState.verified=false
const calls=[]
platform.siteContentApi.home=async()=>{calls.push('local-home-fixture');return {composition:JSON.stringify({version:1,heroSlides:[],notices:[]}),legal:'{}',news:[],videos:[],products:[],media:[]}}
platform.telemetryApi.pageView=async()=>{calls.push('local-page-view-fixture')}
platform.alternateArtApi.notifications=async()=>[]
platform.seasonSummaryApi.notifications=async()=>[]
platform.friendApi.presence=async()=>[]
const [{default:Home},{default:Shell}]=await Promise.all([import('/src/l12/site/OfficialHomePage.vue'),import('/src/l12/site/SiteShell.vue')])
const empty={render:()=>null}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:Home},{path:'/:pathMatch(.*)*',component:empty}]})
await router.push('/');await router.isReady()
createApp({render:()=>h(Shell,{},()=>h(RouterView))}).use(router).mount('#app')
window.__partnerPreview={calls,clicks:[]}
`
const server = await createServer({
  root, configLoader: 'runner', cacheDir: path.join(cache, 'vite'),
  server: { host: '127.0.0.1', port: 0, hmr: false },
  plugins: [{
    name: 'home-partner-link-local-preview',
    resolveId(id) { if (id === '/__home_partner.js') return id },
    load(id) { if (id === '/__home_partner.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use('/__home_partner_page', (_request, response) => {
        response.setHeader('Content-Type', 'text/html; charset=utf-8')
        response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__home_partner.js"></script></body></html>')
      })
    },
  }],
})
const results = [], errors = [], forbiddenRequests = []
const report = { status: 'running', sourceHashes, scriptSha256: sha256(fs.readFileSync(fileURLToPath(import.meta.url))), target, results, errors, forbiddenRequests,
  scope: 'Actual OfficialHomePage + SiteShell SFC and supplied logo; locally controlled empty published content, no real account/API or partner-site navigation',
}
let browser
try {
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, reducedMotion: 'reduce' })
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.origin !== origin || url.pathname.startsWith('/api/')) {
      forbiddenRequests.push(`${route.request().method()} ${url.origin}${url.pathname}`)
      return route.abort()
    }
    return route.continue()
  })
  await page.goto(`${origin}/__home_partner_page`, { waitUntil: 'networkidle' })
  const partner = page.getByRole('link', { name: 'KA盟合作站（新窗口打开）', exact: true })
  await partner.waitFor()
  const logo = partner.locator('img')
  await logo.evaluate(image => image.decode())
  assert.equal(await partner.count(), 1)
  assert.equal(await partner.getAttribute('href'), target)
  assert.equal(await partner.getAttribute('target'), '_blank')
  assert.deepEqual(new Set((await partner.getAttribute('rel')).split(/\s+/)), new Set(['noopener', 'noreferrer']))
  assert.equal(await page.locator('.footer-partner-label').innerText(), '合作站')
  assert.equal(await page.locator('.footer-partner-label').count(), 1)
  const imageBytes = await page.request.get(new URL(await logo.getAttribute('src'), origin).href)
  assert.equal(imageBytes.status(), 200)
  assert.equal(sha256(await imageBytes.body()), sourceHashes['public/images/partners/ka-league.png'])
  const viewports = [[1920,1080],[1366,768],[1024,768],[844,390],[390,844],[320,700]]
  for (const [width, height] of viewports) {
    await page.setViewportSize({ width, height })
    await page.locator('.official-footer').scrollIntoViewIfNeeded()
    const geometry = await page.locator('.official-footer').evaluate(footer => {
      const rect = element => { const box = element.getBoundingClientRect(); return { left: box.left, right: box.right, top: box.top, bottom: box.bottom, width: box.width, height: box.height } }
      const partner = footer.querySelector('.footer-partner'), logo = partner.querySelector('img')
      const content = document.querySelector('.site-content')
      return { footer: rect(footer), brand: rect(footer.querySelector('.footer-brand')), label: rect(footer.querySelector('.footer-partner-label')), partner: rect(partner), legal: rect(footer.querySelector('.footer-legal')),
        overflow: content.scrollWidth > content.clientWidth + 1, filter: getComputedStyle(logo).filter,
        naturalWidth: logo.naturalWidth, naturalHeight: logo.naturalHeight, objectFit: getComputedStyle(logo).objectFit,
      }
    })
    assert.equal(geometry.overflow, false, `page overflow at ${width}x${height}`)
    assert.equal(geometry.filter, 'none', 'Partner logo must retain original colors, not inherit white brand filter')
    assert.equal(geometry.objectFit, 'contain')
    assert.equal(geometry.naturalWidth, 800); assert.equal(geometry.naturalHeight, 800)
    assert.ok(geometry.partner.width >= 44 && geometry.partner.height >= 44, `link target at ${width}x${height}`)
    assert.ok(geometry.label.right <= geometry.partner.left, `shared label must precede all partner logos at ${width}x${height}`)
    assert.ok(geometry.partner.left >= geometry.footer.left && geometry.partner.right <= geometry.footer.right, `partner escaped footer at ${width}x${height}`)
    assert.ok(geometry.partner.left >= geometry.brand.right || geometry.partner.top >= geometry.brand.bottom, `partner overlaps original brand at ${width}x${height}`)
    assert.ok(geometry.legal.left >= geometry.partner.right || geometry.legal.top >= geometry.partner.bottom, `partner overlaps copyright at ${width}x${height}`)
    await partner.focus()
    assert.equal(await partner.evaluate(element => document.activeElement === element && parseFloat(getComputedStyle(element).outlineWidth) >= 2), true)
    if (width === 1920 || width === 390) {
      await partner.evaluate(element => element.blur())
      await page.locator('.official-footer').screenshot({ path: path.join(output, `footer-${width}x${height}.png`) })
      if (width === 1920) {
        await page.locator('.footer-identities').screenshot({ path: path.join(output, 'brand-and-partner.png') })
        await page.screenshot({ path: path.join(output, 'home-bottom-desktop.png') })
      }
    }
    const futurePartners = await page.locator('.footer-partner-links').evaluate(links => {
      const rect = element => { const box = element.getBoundingClientRect(); return { left: box.left, right: box.right, top: box.top, bottom: box.bottom } }
      const original = links.firstElementChild
      const clones = [original.cloneNode(true), original.cloneNode(true)]
      for (const clone of clones) links.append(clone)
      const footer = links.closest('.official-footer')
      const geometry = { footer: rect(footer), label: rect(footer.querySelector('.footer-partner-label')), legal: rect(footer.querySelector('.footer-legal')), logos: [...links.children].map(rect), labelCount: footer.querySelectorAll('.footer-partner-label').length,
        overflow: document.querySelector('.site-content').scrollWidth > document.querySelector('.site-content').clientWidth + 1 }
      for (const clone of clones) clone.remove()
      return geometry
    })
    assert.equal(futurePartners.labelCount, 1, 'Future partners share one label')
    assert.equal(futurePartners.overflow, false, `future partner overflow at ${width}x${height}`)
    for (const [index, box] of futurePartners.logos.entries()) {
      assert.ok(box.left >= futurePartners.label.right && box.right <= futurePartners.footer.right, `future partner containment at ${width}x${height}`)
      assert.ok(futurePartners.legal.left >= box.right || futurePartners.legal.top >= box.bottom, `future partner overlaps copyright at ${width}x${height}`)
      if (index) {
        const previous = futurePartners.logos[index - 1]
        assert.ok(box.left >= previous.right || box.top >= previous.bottom, `future partners extend right then wrap at ${width}x${height}`)
      }
    }
    results.push({ viewport: { width, height }, geometry, keyboardFocus: true, futurePartners })
  }
  // Exercise native activation, but prevent external navigation in the fixture.
  await partner.evaluate(element => element.addEventListener('click', event => {
    event.preventDefault(); window.__partnerPreview.clicks.push({ href: element.href, target: element.target })
  }))
  await partner.click()
  await partner.focus(); await page.keyboard.press('Enter')
  const activations = await page.evaluate(() => window.__partnerPreview.clicks)
  assert.equal(activations.length, 2)
  assert.ok(activations.every(value => value.href === target && value.target === '_blank'))
  assert.deepEqual(errors, [])
  assert.deepEqual(forbiddenRequests, [])
  for (const file of files) assert.equal(sha256(fs.readFileSync(path.join(root, file))), sourceHashes[file], `Source changed during preview: ${file}`)
  report.status = 'passed'; report.activations = activations
  report.screenshots = ['footer-1920x1080.png','footer-390x844.png','brand-and-partner.png','home-bottom-desktop.png']
  console.log(JSON.stringify({ status: report.status, output, viewports: results.length, screenshots: report.screenshots, sourceHashes }, null, 2))
} catch (error) {
  report.status = 'failed'; report.error = error.stack || String(error)
  throw error
} finally {
  fs.writeFileSync(path.join(output, report.status === 'passed' ? 'report.json' : `failure-${Date.now()}.json`), JSON.stringify(report, null, 2))
  await browser?.close(); await server.close()
}
