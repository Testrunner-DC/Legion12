import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT
assert(output, 'L12_QA_OUT must name a governed D/E acceptance directory')

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')

const viewports = [
  { width: 320, height: 568 },
  { width: 390, height: 844 },
  { width: 760, height: 900 },
  { width: 568, height: 320 },
  { width: 667, height: 375 },
  { width: 761, height: 640 },
  { width: 844, height: 390 },
  { width: 900, height: 641 },
  { width: 901, height: 640 },
  { width: 901, height: 641 },
  { width: 1024, height: 600 },
  { width: 1024, height: 768 },
  { width: 1366, height: 768 },
  { width: 1920, height: 1080 },
]

const fixtureCards = [
  { cardId: 'S01-0001', orientation: 'portrait', width: 500, height: 700, asset: '/__card_archive_fit__/portrait.svg' },
  { cardId: 'S01-DS01', orientation: 'landscape', width: 800, height: 500, asset: '/__card_archive_fit__/landscape.svg' },
]

function directionalSvg({ width, height, orientation }) {
  const marker = Math.round(Math.min(width, height) * 0.14)
  const label = orientation === 'portrait' ? 'PORTRAIT 5:7' : 'LANDSCAPE 8:5'
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">
    <rect width="${width}" height="${height}" fill="#111820"/>
    <rect x="3" y="3" width="${width - 6}" height="${height - 6}" fill="none" stroke="#f5f0df" stroke-width="6"/>
    <path d="M0 ${marker}V0H${marker}" fill="none" stroke="#ff4655" stroke-width="${marker / 3}"/>
    <path d="M${width - marker} 0H${width}V${marker}" fill="none" stroke="#52e071" stroke-width="${marker / 3}"/>
    <path d="M0 ${height - marker}V${height}H${marker}" fill="none" stroke="#45a3ff" stroke-width="${marker / 3}"/>
    <path d="M${width - marker} ${height}H${width}V${height - marker}" fill="none" stroke="#ffd34d" stroke-width="${marker / 3}"/>
    <text x="50%" y="47%" dominant-baseline="middle" text-anchor="middle" fill="#f5f0df" font-family="Arial,sans-serif" font-size="${marker * .72}" font-weight="700">${label}</text>
    <text x="50%" y="58%" dominant-baseline="middle" text-anchor="middle" fill="#71dce4" font-family="Arial,sans-serif" font-size="${marker * .38}">ALL FOUR CORNERS VISIBLE</text>
  </svg>`
}

const fixtureAssets = new Map(fixtureCards.map(card => [card.asset, directionalSvg(card)]))
const fixtureManifest = JSON.stringify({
  schemaVersion: 3,
  catalogVersion: 'card-archive-detail-fit',
  assetVersion: 'card-archive-detail-fit',
  basePath: '/',
  cards: Object.fromEntries(fixtureCards.map(card => [card.cardId, {
    cardId: card.cardId,
    contentHash: `fixture-${card.cardId}`,
    width: card.width,
    height: card.height,
    orientation: card.orientation,
    variants: {
      originalWebp: card.asset,
      thumbWebp: card.asset,
      boardWebp: card.asset,
      detailWebp: card.asset,
    },
  }])),
})

fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import CardArchive from '/src/l12/CardArchive.vue'
import '/src/style.css'
const app=createApp({render:()=>h(CardArchive)})
app.use(createRouter({history:createMemoryHistory(),routes:[{path:'/',component:CardArchive}]}))
app.mount('#app')
`

const server = await createServer({
  root,
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'card-archive-detail-fit-fixture',
    resolveId(id) { if (id === '/__card_archive_detail_fit__.js') return id },
    load(id) { if (id === '/__card_archive_detail_fit__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__card_archive_detail_fit__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{width:100%;height:100%;margin:0;background:#05090b}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__card_archive_detail_fit__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

function inside(inner, outer, tolerance = 1.5) {
  return inner.left >= outer.left - tolerance && inner.top >= outer.top - tolerance
    && inner.right <= outer.right + tolerance && inner.bottom <= outer.bottom + tolerance
}

function overlaps(first, second, tolerance = 1.5) {
  return first.left < second.right - tolerance && first.right > second.left + tolerance
    && first.top < second.bottom - tolerance && first.bottom > second.top + tolerance
}

async function openCardDetail(page, orientation) {
  const card = orientation === 'landscape'
    ? page.locator('.archive-card.landscape-thumbnail').first()
    : page.locator('.archive-card:not(.landscape-thumbnail)').first()
  await card.waitFor()
  await card.scrollIntoViewIfNeeded()
  const viewport = page.viewportSize()
  assert(viewport)
  if (viewport.width <= 900 || viewport.height <= 640) await card.click()
  else await card.locator('.archive-image-open').dblclick()
  const modal = page.locator('.archive-modal')
  await modal.waitFor()
  const fixture = fixtureCards.find(card => card.orientation === orientation)
  assert(fixture)
  await page.waitForFunction(expected => {
    const image = document.querySelector('.archive-modal-image .l12-card-image__img')
    const picture = image?.closest('.l12-card-image')
    return image instanceof HTMLImageElement && image.complete && image.naturalWidth > 0
      && picture?.dataset.source === 'sameOrigin'
      && picture.dataset.orientation === expected.orientation
      && image.naturalWidth === expected.width && image.naturalHeight === expected.height
      && new URL(image.currentSrc || image.src).pathname === expected.asset
  }, fixture)
  return modal
}

async function measure(page, modal, viewport, orientation) {
  const geometry = await modal.evaluate((element, args) => {
    const image = element.querySelector('.archive-modal-image')
    const bitmap = element.querySelector('.archive-modal-image .l12-card-image__img')
    const detail = element.querySelector('.archive-modal-detail')
    const close = element.querySelector('.archive-modal-close')
    if (!(image instanceof HTMLElement) || !(bitmap instanceof HTMLImageElement)
      || !(detail instanceof HTMLElement) || !(close instanceof HTMLElement)) return null
    const rect = node => {
      const value = node.getBoundingClientRect()
      return { left: value.left, top: value.top, right: value.right, bottom: value.bottom,
        width: value.width, height: value.height }
    }
    const style = getComputedStyle(bitmap)
    const modalStyle = getComputedStyle(element)
    const modalRect = rect(element)
    const bitmapRect = rect(bitmap)
    const scale = Math.min(bitmapRect.width / bitmap.naturalWidth, bitmapRect.height / bitmap.naturalHeight)
    const paintedWidth = bitmap.naturalWidth * scale
    const paintedHeight = bitmap.naturalHeight * scale
    const painted = {
      left: bitmapRect.left + (bitmapRect.width - paintedWidth) / 2,
      top: bitmapRect.top + (bitmapRect.height - paintedHeight) / 2,
      right: bitmapRect.left + (bitmapRect.width + paintedWidth) / 2,
      bottom: bitmapRect.top + (bitmapRect.height + paintedHeight) / 2,
      width: paintedWidth,
      height: paintedHeight,
    }
    return {
      viewport: args.viewport,
      orientation: args.orientation,
      modal: modalRect,
      modalInner: {
        left: modalRect.left + parseFloat(modalStyle.paddingLeft),
        top: modalRect.top + parseFloat(modalStyle.paddingTop),
        right: modalRect.right - parseFloat(modalStyle.paddingRight),
        bottom: modalRect.bottom - parseFloat(modalStyle.paddingBottom),
      },
      image: rect(image),
      bitmap: bitmapRect,
      painted,
      close: rect(close),
      detail: rect(detail),
      documentScrollY: window.scrollY,
      modalScrollTop: element.scrollTop,
      modalScrollHeight: element.scrollHeight,
      modalClientHeight: element.clientHeight,
      detailScrollHeight: detail.scrollHeight,
      detailClientHeight: detail.clientHeight,
      naturalWidth: bitmap.naturalWidth,
      naturalHeight: bitmap.naturalHeight,
      source: bitmap.closest('.l12-card-image')?.dataset.source,
      manifestOrientation: bitmap.closest('.l12-card-image')?.dataset.orientation,
      assetPath: new URL(bitmap.currentSrc || bitmap.src).pathname,
      objectFit: style.objectFit,
      overflow: getComputedStyle(element).overflow,
    }
  }, { viewport, orientation })
  assert(geometry, `${viewport.width}x${viewport.height} ${orientation}: modal geometry unavailable`)
  const viewportRect = { left: 0, top: 0, right: viewport.width, bottom: viewport.height }
  assert(inside(geometry.modal, viewportRect), `${viewport.width}x${viewport.height} ${orientation}: modal leaves viewport ${JSON.stringify(geometry.modal)}`)
  assert(inside(geometry.image, viewportRect), `${viewport.width}x${viewport.height} ${orientation}: card image leaves viewport ${JSON.stringify(geometry)}`)
  assert(inside(geometry.image, geometry.modal), `${viewport.width}x${viewport.height} ${orientation}: card image is clipped by modal`)
  assert(inside(geometry.image, geometry.modalInner), `${viewport.width}x${viewport.height} ${orientation}: card image invades modal padding`)
  assert(!overlaps(geometry.image, geometry.close), `${viewport.width}x${viewport.height} ${orientation}: card image overlaps close control`)
  assert(inside(geometry.close, geometry.modal), `${viewport.width}x${viewport.height} ${orientation}: close control leaves modal`)
  assert.equal(geometry.documentScrollY, 0, `${viewport.width}x${viewport.height} ${orientation}: opening details must not require page scroll`)
  assert.equal(geometry.modalScrollTop, 0, `${viewport.width}x${viewport.height} ${orientation}: card must be visible before modal scrolling`)
  assert.equal(geometry.objectFit, 'contain', `${viewport.width}x${viewport.height} ${orientation}: bitmap must preserve complete artwork`)
  const fixture = fixtureCards.find(card => card.orientation === orientation)
  assert(fixture)
  assert.equal(geometry.source, 'sameOrigin', `${viewport.width}x${viewport.height} ${orientation}: controlled bitmap source was not loaded`)
  assert.equal(geometry.manifestOrientation, orientation, `${viewport.width}x${viewport.height} ${orientation}: manifest orientation mismatch`)
  assert.equal(geometry.assetPath, fixture.asset, `${viewport.width}x${viewport.height} ${orientation}: wrong directional bitmap loaded`)
  assert.equal(geometry.naturalWidth, fixture.width, `${viewport.width}x${viewport.height} ${orientation}: intrinsic width mismatch`)
  assert.equal(geometry.naturalHeight, fixture.height, `${viewport.width}x${viewport.height} ${orientation}: intrinsic height mismatch`)
  assert(inside(geometry.painted, geometry.bitmap), `${viewport.width}x${viewport.height} ${orientation}: contained bitmap paint leaves its element`)
  assert(geometry.image.width >= 1 && geometry.image.height >= 1,
    `${viewport.width}x${viewport.height} ${orientation}: card image collapsed`)
  const renderedRatio = geometry.image.width / geometry.image.height
  const expectedRatio = orientation === 'landscape' ? 8 / 5 : 5 / 7
  assert(Math.abs(renderedRatio - expectedRatio) < 0.025,
    `${viewport.width}x${viewport.height} ${orientation}: card frame ratio drifted (${renderedRatio})`)
  return geometry
}

async function verifyDetailReachable(modal, viewport, orientation) {
  const result = await modal.locator('.archive-modal-detail').evaluate(async detail => {
    const last = detail.lastElementChild
    const maximum = Math.max(0, detail.scrollHeight - detail.clientHeight)
    detail.scrollTop = detail.scrollHeight
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    const detailRect = detail.getBoundingClientRect()
    const lastRect = last?.getBoundingClientRect()
    const value = {
      maximum,
      reached: detail.scrollTop,
      lastBottom: lastRect?.bottom ?? detailRect.bottom,
      detailBottom: detailRect.bottom,
    }
    detail.scrollTop = 0
    return value
  })
  assert(Math.abs(result.maximum - result.reached) <= 2,
    `${viewport.width}x${viewport.height} ${orientation}: detail cannot reach its scroll end`)
  assert(result.lastBottom <= result.detailBottom + 2,
    `${viewport.width}x${viewport.height} ${orientation}: final detail content remains unreachable`)
  return result
}

let browser
const report = { status: 'running', viewports, cases: [], errors: [] }
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: viewports[0] })
  page.on('pageerror', error => report.errors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (fixtureAssets.has(url.pathname))
      return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: fixtureAssets.get(url.pathname) })
    if (url.pathname === '/card-assets/card-assets.manifest.json')
      return route.fulfill({ status: 200, contentType: 'application/json', body: fixtureManifest })
    if (url.pathname === '/api/alternate-arts')
      return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    if (url.pathname === '/api/gallery/presentation')
      return route.fulfill({ status: 200, contentType: 'application/json', body: '{"regularCards":[],"filterProducts":[]}' })
    return url.hostname === '127.0.0.1' ? route.continue() : route.abort()
  })

  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    for (const orientation of ['portrait', 'landscape']) {
      await page.goto(`http://127.0.0.1:${port}/__card_archive_detail_fit__`)
      await page.locator('.archive-card').first().waitFor()
      const modal = await openCardDetail(page, orientation)
      const geometry = await measure(page, modal, viewport, orientation)
      const screenshot = `${viewport.width}x${viewport.height}-${orientation}.png`
      await page.screenshot({ path: path.join(output, screenshot) })
      const detailReachability = await verifyDetailReachable(modal, viewport, orientation)
      report.cases.push({ viewport, orientation, screenshot, geometry, detailReachability })
      await modal.locator('.archive-modal-close').click()
      await modal.waitFor({ state: 'hidden' })
    }
  }
  assert.equal(report.errors.length, 0, `page errors: ${report.errors.join(' | ')}`)
  report.status = 'passed'
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  console.log(`Card archive detail fit passed: ${report.cases.length} cases across ${viewports.length} viewports. Evidence: ${output}`)
} catch (error) {
  report.status = 'failed'
  report.failure = error instanceof Error ? error.stack : String(error)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}
