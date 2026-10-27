import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_QA_OUT
const cacheDir = process.env.L12_QA_CACHE
const pwsh = process.env.L12_PWSH
assert.ok(output && path.isAbsolute(output), 'L12_QA_OUT must be an absolute governed D/E path')
assert.ok(cacheDir && path.isAbsolute(cacheDir), 'L12_QA_CACHE must be an absolute governed D/E path')
assert.ok(pwsh && path.isAbsolute(pwsh) && fs.existsSync(pwsh), 'L12_PWSH must be initialized')
const guard = path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_STORAGE_GUARD; '
  + 'Assert-L12GeneratedPath -Path $env:L12_ACCEPTANCE_OUT -Purpose "single card picker reachability evidence" | Out-Null; '
  + 'Assert-L12GeneratedPath -Path $env:L12_ACCEPTANCE_CACHE -Purpose "single card picker reachability cache" | Out-Null; '
  + 'foreach($name in @("TEMP","TMP","TMPDIR")){'
  + '$value=[Environment]::GetEnvironmentVariable($name,"Process"); '
  + 'if([string]::IsNullOrWhiteSpace($value)){throw "L12_TEMP_PATH_MISSING: $name"}; '
  + 'Assert-L12GeneratedPath -Path $value -Purpose "single card picker $name" | Out-Null}',
], { env: { ...process.env, L12_STORAGE_GUARD: guard, L12_ACCEPTANCE_OUT: output,
  L12_ACCEPTANCE_CACHE: cacheDir }, stdio: 'pipe', timeout: 15_000, windowsHide: true })
if (fs.existsSync(output)) assert.deepEqual(fs.readdirSync(output), [], 'acceptance output must be empty')
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(cacheDir, { recursive: true })

const sourcePath = path.join(root, 'src/l12/SingleCardPicker.vue')
const sourceSha256 = createHash('sha256').update(fs.readFileSync(sourcePath)).digest('hex').toUpperCase()
const entry = `
import {createApp,h} from 'vue'
import '/src/style.css'
import SingleCardPicker from '/src/l12/SingleCardPicker.vue'
const detail={id:'qa-alpha',number:'QA-001',nameZh:'Alpha 神话军团',cardType:'Legion',faction:'tianting',product:'QA 产品',cost:2,effect:'用于单卡选择器可达性验收。'}
const items=[
 {id:'qa-alpha',cardId:'qa-alpha',number:'QA-001',name:'Alpha 神话军团',nameZh:'Alpha 神话军团',cardType:'Legion',faction:'tianting',product:'QA 产品',cost:2,effect:detail.effect,detailCard:detail},
 {id:'qa-beta',cardId:'qa-beta',number:'QA-002',name:'Beta 天灾',nameZh:'Beta 天灾',cardType:'Disaster',faction:'disaster',product:'QA 产品二',disasterLevel:4,effect:'另一张受控卡。'},
]
window.__pickerQa={selected:'',closed:false}
createApp({render:()=>h('main',[h(SingleCardPicker,{title:'选择验收卡牌',items,onSelect:item=>{window.__pickerQa.selected=item.id},onClose:()=>{window.__pickerQa.closed=true}})])}).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir,
  server: { host: '127.0.0.1', port: 0, hmr: false }, plugins: [{
    name: 'single-card-picker-reachability',
    resolveId(id) { if (id === '/__single_card_picker_qa__.js') return id },
    load(id) { if (id === '/__single_card_picker_qa__.js') return entry },
    configureServer(vite) { vite.middlewares.use((request, response, next) => {
      if (!request.url?.match(/^\/__single_card_picker_qa__(\?|$)/)) return next()
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end('<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,viewport-fit=cover"><script>document.documentElement.dataset.l12Mobile=String(innerWidth<=900)</script><div id="app"></div><div id="l12-landscape-teleports"></div><script type="module" src="/__single_card_picker_qa__.js"></script>')
    }) },
  }] })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let browser
const report = { status: 'RUNNING', sourceSha256, scope: 'actual SingleCardPicker SFC with local controlled items; no API, login, business write or deployment', cases: [], errors: [], forbiddenRequests: [] }
const viewports = [{ width: 390, height: 844 }, { width: 844, height: 390 }, { width: 1440, height: 900 }]
const twoFrames = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
const geometry = locator => locator.evaluate(element => {
  const rect = element.getBoundingClientRect()
  const centerX = Math.min(innerWidth - 1, Math.max(0, rect.left + rect.width / 2))
  const centerY = Math.min(innerHeight - 1, Math.max(0, rect.top + rect.height / 2))
  const hit = document.elementFromPoint(centerX, centerY)
  return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height,
    inViewport: rect.left >= -1 && rect.right <= innerWidth + 1 && rect.top >= -1 && rect.bottom <= innerHeight + 1,
    hit: hit === element || element.contains(hit) }
})
try {
  await server.listen()
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const viewport of viewports) {
    const page = await browser.newPage({ viewport })
    page.on('pageerror', error => report.errors.push(`${viewport.width}x${viewport.height}: ${error.message}`))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.origin === origin) return route.continue()
      report.forbiddenRequests.push(url.href)
      return route.abort()
    })
    await page.goto(`${origin}/__single_card_picker_qa__`, { waitUntil: 'networkidle' })
    const dialog = page.locator('.single-card-picker')
    const filters = page.locator('.single-card-filters')
    const search = filters.locator('input[type=search]')
    await dialog.waitFor({ state: 'visible' })
    await twoFrames(page)
    const initial = await page.evaluate(() => {
      const read = selector => { const element = document.querySelector(selector); const rect = element.getBoundingClientRect(); return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height } }
      const strip = document.querySelector('.single-card-filters')
      return { dialog: read('.single-card-picker'), filters: { ...read('.single-card-filters'), scrollLeft: strip.scrollLeft,
        clientWidth: strip.clientWidth, scrollWidth: strip.scrollWidth, paddingLeft: getComputedStyle(strip).paddingLeft,
        paddingRight: getComputedStyle(strip).paddingRight }, search: read('.single-card-filters input[type=search]') }
    })
    assert.ok(initial.dialog.left >= -1 && initial.dialog.right <= viewport.width + 1,
      `dialog escaped ${viewport.width}x${viewport.height}: ${JSON.stringify(initial)}`)
    const controls = filters.locator('input,select')
    const controlMetrics = []
    for (let index = 0; index < await controls.count(); index++) {
      const control = controls.nth(index)
      await control.evaluate(element => element.scrollIntoView({ block: 'nearest', inline: 'center' }))
      await twoFrames(page)
      const measured = await geometry(control)
      assert.ok(measured.inViewport && measured.hit, `filter ${index} unreachable at ${viewport.width}x${viewport.height}: ${JSON.stringify(measured)}`)
      assert.ok(measured.height >= (viewport.width <= 900 ? 44 : 38), `filter ${index} too short at ${viewport.width}x${viewport.height}`)
      controlMetrics.push(measured)
    }
    const arrows = page.locator('[data-l12-scroll-arrows] button')
    const scrollRange = await filters.evaluate(element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
      overflowing: element.scrollWidth > element.clientWidth + 1 }))
    let arrowScroll
    if (scrollRange.overflowing) {
      await arrows.first().waitFor({ state: 'visible' })
      assert.equal(await arrows.count(), 2)
      const rewind = []
      for (let attempt = 0; attempt < 12; attempt++) {
        const left = await filters.evaluate(element => element.scrollLeft)
        rewind.push(left)
        if (left <= 1) break
        assert.equal(await arrows.nth(0).isDisabled(), false,
          `previous arrow disabled before reaching start at ${viewport.width}x${viewport.height}`)
        await arrows.nth(0).click()
        await twoFrames(page)
      }
      const before = await filters.evaluate(element => element.scrollLeft)
      assert.ok(before <= 1, `filter strip did not reach its start at ${viewport.width}x${viewport.height}: ${rewind.join(' -> ')}`)
      assert.equal(await arrows.nth(1).isDisabled(), false, `next arrow disabled at start at ${viewport.width}x${viewport.height}`)
      await arrows.nth(1).click()
      await twoFrames(page)
      const afterNext = await filters.evaluate(element => element.scrollLeft)
      assert.ok(afterNext > before, `next arrow did not move filters at ${viewport.width}x${viewport.height}`)
      await arrows.nth(0).click()
      await twoFrames(page)
      const afterPrevious = await filters.evaluate(element => element.scrollLeft)
      assert.ok(afterPrevious < afterNext, `previous arrow did not move filters at ${viewport.width}x${viewport.height}`)
      arrowScroll = { applicable: true, ...scrollRange, rewind, before, afterNext, afterPrevious }
    } else {
      const arrowCount = await arrows.count()
      assert.equal(await page.locator('[data-l12-scroll-arrows]:visible').count(), 0,
        `unneeded arrows remained visible at ${viewport.width}x${viewport.height}`)
      arrowScroll = { applicable: false, ...scrollRange, arrowCount,
        reason: 'filter row fits without horizontal overflow' }
    }
    await search.fill('Alpha')
    assert.equal(await page.locator('.single-card-result-card').count(), 1)
    await page.getByRole('button', { name: '查看Alpha 神话军团详情' }).click()
    await page.locator('.catalog-detail-dialog').waitFor({ state: 'visible' })
    await page.getByRole('button', { name: '关闭卡牌详情' }).click()
    await dialog.waitFor({ state: 'visible' })
    await page.getByRole('button', { name: '选择', exact: true }).click()
    assert.equal(await page.evaluate(() => window.__pickerQa.selected), 'qa-alpha')
    await page.screenshot({ path: path.join(output, `picker-${viewport.width}x${viewport.height}.png`), fullPage: true })
    report.cases.push({ viewport, initial, controlMetrics, arrowScroll })
    await page.close()
  }
  assert.deepEqual(report.errors, [])
  assert.deepEqual(report.forbiddenRequests, [])
  report.status = 'PASSED'
} catch (error) {
  report.status = 'FAILED'
  report.failure = error.stack
  throw error
} finally {
  if (browser) await browser.close()
  await server.close()
  report.browserClosed = true
  report.serverClosed = true
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: report.status, cases: report.cases.length, output, browserClosed: report.browserClosed, serverClosed: report.serverClosed }, null, 2))
}
