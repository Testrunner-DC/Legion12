import assert from 'node:assert/strict'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const browserChannel = process.env.L12_BROWSER_CHANNEL || 'msedge'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || fs.mkdtempSync(path.join(os.tmpdir(), 'l12-card-image-stability-'))
fs.mkdirSync(output, { recursive: true })

const cards = Object.fromEntries([
  ['CARD-A', 'a'],
  ['CARD-B', 'b'],
  ['CARD-C', 'c'],
  ['ROUND-FALLBACK', 'round'],
].map(([cardId, stem]) => [cardId, {
  cardId,
  contentHash: `qa-${stem}`,
  width: 744,
  height: 1039,
  orientation: 'portrait',
  variants: {
    thumbWebp: `/__card_image__/${stem}-low.svg`,
    boardWebp: `/__card_image__/${stem}-low.svg`,
    detailWebp: `/__card_image__/${stem}-high.svg`,
  },
}]))
const manifest = JSON.stringify({
  schemaVersion: 3,
  catalogVersion: 'card-image-stability-qa',
  assetVersion: 'card-image-stability-qa',
  basePath: '/',
  cards,
})

const entry = `
import { createApp, h, nextTick, ref } from 'vue'
import CardImage from '/src/l12/CardImage.vue'
import RoundCardImage from '/src/l12/RoundCardImage.vue'
import '/src/style.css'

const detailVisible = ref(false)
const roundVisible = ref(false)
const cardId = ref('CARD-A')
const fit = ref('contain')
const legacyUrl = ref(undefined)

window.__cardHarness = {
  async showDetail(value = true) { detailVisible.value = value; await nextTick() },
  async showRound(value = true) { roundVisible.value = value; await nextTick() },
  async setCard(value) { cardId.value = value; await nextTick() },
  async setFit(value) { fit.value = value; await nextTick() },
  async setLegacyUrl(value) { legacyUrl.value = value; await nextTick() },
}

createApp({
  setup() {
    return () => h('main', { id: 'card-image-stability-harness' }, [
      h('section', { id: 'primer-slot' }, [h(CardImage, {
        class: 'primer-image', cardId: 'CARD-A', alt: 'primer', intent: 'board', eager: true,
      })]),
      h('section', { id: 'detail-slot' }, detailVisible.value ? [h(CardImage, {
        class: 'detail-image', cardId: cardId.value, legacyUrl: legacyUrl.value,
        alt: cardId.value, intent: 'detail', eager: true, fit: fit.value,
      })] : []),
      h('section', { id: 'round-slot' }, roundVisible.value ? [h(RoundCardImage, {
        class: 'round-image', cardId: 'ROUND-FALLBACK', legacyUrl: undefined,
      })] : []),
    ])
  },
}).mount('#app')
`

const requestCounts = new Map()
function countRequest(pathname) {
  const next = (requestCounts.get(pathname) ?? 0) + 1
  requestCounts.set(pathname, next)
  return next
}
function svg(stem) {
  const color = stem.startsWith('a-') ? '#8f2836' : stem.startsWith('b-') ? '#176070' : '#665319'
  return `<svg xmlns="http://www.w3.org/2000/svg" width="744" height="1039"><rect width="100%" height="100%" fill="${color}"/><text x="50%" y="50%" fill="white" font-size="96" text-anchor="middle">${stem}</text></svg>`
}

const server = await createServer({
  root,
  configLoader: 'runner',
  server: { host: '127.0.0.1', port: 0 },
  logLevel: 'error',
  plugins: [{
    name: 'card-image-stability-harness',
    resolveId(id) { if (id === '/__card_image_stability__.js') return id },
    load(id) { if (id === '/__card_image_stability__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        const url = new URL(request.url || '/', 'http://127.0.0.1')
        if (url.pathname === '/card-assets/card-assets.manifest.json') {
          countRequest(url.pathname)
          response.setHeader('Content-Type', 'application/json')
          response.end(manifest)
          return
        }
        if (url.pathname.startsWith('/__card_image__/')) {
          const stem = path.basename(url.pathname, '.svg')
          const requestNumber = countRequest(url.pathname)
          const failBeforeSuccess = stem === 'a-high' ? 2 : 0
          const delay = stem === 'c-high' ? 420 : stem.endsWith('-high') ? 140 : stem.startsWith('b-') ? 90 : 12
          setTimeout(() => {
            response.setHeader('Cache-Control', 'no-store')
            if (requestNumber <= failBeforeSuccess) {
              response.statusCode = 503
              response.end('transient card image failure')
              return
            }
            response.setHeader('Content-Type', 'image/svg+xml')
            response.setHeader('Cache-Control', 'public, max-age=31536000, immutable')
            response.end(svg(stem))
          }, delay)
          return
        }
        if (url.pathname === '/api/site/media/alt-b.svg') {
          countRequest(url.pathname)
          setTimeout(() => {
            response.setHeader('Content-Type', 'image/svg+xml')
            response.setHeader('Cache-Control', 'public, max-age=31536000, immutable')
            response.end(svg('alternate-b'))
          }, 90)
          return
        }
        if (url.pathname === '/__card_image_stability__') {
          response.setHeader('Content-Type', 'text/html; charset=utf-8')
          response.end('<style>body{margin:0;background:#15191a;color:white}main{display:flex;gap:24px;padding:24px}section{width:180px;height:252px;background:#090d0e}#round-slot{width:96px;height:96px}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__card_image_stability__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

let browser
const report = { status: 'running', output, browserChannel, profiles: [], errors: [], assertions: 0 }
function check(value, message) {
  try {
    assert.ok(value, message)
    report.assertions += 1
  } catch (error) {
    report.errors.push(error instanceof Error ? error.message : String(error))
  }
}

try {
  await server.listen()
  const address = server.httpServer.address()
  const port = typeof address === 'object' && address ? address.port : 0
  browser = await chromium.launch({ headless: true, channel: browserChannel })

  for (const profile of [
    { name: 'desktop-60hz', intervalMs: 16.67, viewport: { width: 1920, height: 1080 } },
    { name: 'desktop-120hz', intervalMs: 8.33, viewport: { width: 1920, height: 1080 } },
    { name: 'mobile-landscape-120hz', intervalMs: 8.33, viewport: { width: 844, height: 390 }, isMobile: true, hasTouch: true },
  ]) {
    requestCounts.clear()
    const context = await browser.newContext({ viewport: profile.viewport, isMobile: profile.isMobile, hasTouch: profile.hasTouch })
    const page = await context.newPage()
    const pageErrors = []
    page.on('pageerror', error => pageErrors.push(error.message))
    await page.goto(`http://127.0.0.1:${port}/__card_image_stability__`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => document.querySelector('.primer-image img')?.naturalWidth > 0)

    await page.evaluate(intervalMs => {
      window.__stability = {
        frames: [],
        mutations: [],
        detailLowLoadedAt: null,
        firstDetailImage: null,
      }
      document.addEventListener('load', event => {
        const image = event.target
        if (image instanceof HTMLImageElement && image.closest('.detail-image')
          && image.currentSrc.includes('/a-low.svg') && window.__stability.detailLowLoadedAt === null)
          window.__stability.detailLowLoadedAt = performance.now()
      }, true)
      new MutationObserver(records => {
        for (const record of records) {
          for (const node of record.removedNodes) {
            if (node instanceof HTMLImageElement && node.matches('.l12-card-image__img'))
              window.__stability.mutations.push({ type: 'remove', at: performance.now(), src: node.currentSrc })
          }
          for (const node of record.addedNodes) {
            if (node instanceof HTMLImageElement && node.matches('.l12-card-image__img'))
              window.__stability.mutations.push({ type: 'add', at: performance.now(), src: node.currentSrc })
          }
        }
      }).observe(document.querySelector('#detail-slot'), { subtree: true, childList: true })
      const startedAt = performance.now()
      const sample = () => {
        const image = document.querySelector('.detail-image img')
        if (image && !window.__stability.firstDetailImage) window.__stability.firstDetailImage = image
        window.__stability.frames.push({
          at: performance.now(),
          elapsedMs: performance.now() - startedAt,
          exists: Boolean(image),
          ready: image?.naturalWidth ?? 0,
          src: image?.currentSrc ?? '',
          sameNode: !image || window.__stability.firstDetailImage === image,
        })
        if (performance.now() - startedAt < 1_350) setTimeout(sample, intervalMs)
      }
      sample()
    }, profile.intervalMs)

    await page.evaluate(() => window.__cardHarness.showDetail(true))
    await page.waitForTimeout(190)
    await page.screenshot({ path: path.join(output, `${profile.name}-upgrade-mid.png`) })
    const highReady = await page.waitForFunction(() => document.querySelector('.detail-image img')?.currentSrc.includes('/a-high.svg')
      && document.querySelector('.detail-image img')?.naturalWidth > 0, undefined, { timeout: 3_000 })
      .then(() => true, () => false)
    await page.waitForTimeout(250)

    const upgrade = await page.evaluate(() => {
      const state = window.__stability
      const afterLowLoad = state.detailLowLoadedAt === null ? [] : state.frames.filter(frame => frame.at >= state.detailLowLoadedAt)
      return {
        mutationCount: state.mutations.length,
        removedImageCount: state.mutations.filter(item => item.type === 'remove').length,
        blackFramesAfterLowLoad: afterLowLoad.filter(frame => frame.exists && frame.ready === 0).length,
        unstableNodeFrames: state.frames.filter(frame => frame.exists && !frame.sameNode).length,
        lowLoadedAt: state.detailLowLoadedAt,
        finalSrc: document.querySelector('.detail-image img')?.currentSrc ?? '',
      }
    })
    upgrade.highResolutionRequests = requestCounts.get('/__card_image__/a-high.svg') ?? 0

    check(upgrade.lowLoadedAt !== null, `${profile.name}: detail low-resolution image must load before upgrade`)
    check(upgrade.removedImageCount === 0, `${profile.name}: same-URL refresh/detail upgrade replaced the live <img> ${upgrade.removedImageCount} times`)
    check(upgrade.blackFramesAfterLowLoad === 0, `${profile.name}: detail upgrade cleared successful pixels for ${upgrade.blackFramesAfterLowLoad} samples`)
    check(upgrade.unstableNodeFrames === 0, `${profile.name}: detail upgrade changed node identity for ${upgrade.unstableNodeFrames} samples`)
    check(highReady && upgrade.finalSrc.includes('/a-high.svg'), `${profile.name}: decoded high-resolution image did not take over`)
    check(upgrade.highResolutionRequests === 3, `${profile.name}: transient high-resolution source should make exactly three attempts before takeover (received ${upgrade.highResolutionRequests})`)

    const beforeFit = await page.evaluate(() => {
      const image = document.querySelector('.detail-image img')
      window.__fitIdentity = image
      return image?.currentSrc ?? ''
    })
    await page.evaluate(() => window.__cardHarness.setFit('cover'))
    await page.waitForTimeout(50)
    const fitStable = await page.evaluate(() => window.__fitIdentity === document.querySelector('.detail-image img'))
    check(fitStable && beforeFit.includes('/a-high.svg'), `${profile.name}: fit-only update must retain the loaded image node`)

    await page.evaluate(() => window.__cardHarness.setCard('CARD-B'))
    await page.evaluate(() => {
      window.__switchFrames = []
      const sample = () => {
        const image = document.querySelector('.detail-image img')
        window.__switchFrames.push({ src: image?.currentSrc ?? '', ready: image?.naturalWidth ?? 0 })
        if (window.__switchFrames.length < 24) setTimeout(sample, 8)
      }
      sample()
    })
    await page.waitForFunction(() => document.querySelector('.detail-image img')?.currentSrc.includes('/b-high.svg')
      && document.querySelector('.detail-image img')?.naturalWidth > 0, undefined, { timeout: 2_000 })
    const switchResult = await page.evaluate(() => ({
      finalSrc: document.querySelector('.detail-image img')?.currentSrc ?? '',
      staleReadyFrames: window.__switchFrames.filter(frame => frame.ready > 0 && frame.src.includes('/a-')).length,
    }))
    check(switchResult.finalSrc.includes('/b-high.svg'), `${profile.name}: true cardId change did not reach CARD-B detail art`)
    check(switchResult.staleReadyFrames === 0, `${profile.name}: CARD-A remained visible after CARD-B switch for ${switchResult.staleReadyFrames} samples`)

    await page.evaluate(() => window.__cardHarness.setLegacyUrl('/api/site/media/alt-b.svg'))
    await page.waitForFunction(() => document.querySelector('.detail-image img')?.currentSrc.includes('/api/site/media/alt-b.svg')
      && document.querySelector('.detail-image img')?.naturalWidth > 0)
    const alternateResult = await page.evaluate(() => ({
      src: document.querySelector('.detail-image img')?.currentSrc ?? '',
      source: document.querySelector('.detail-image')?.dataset.source,
    }))
    check(alternateResult.src.includes('/api/site/media/alt-b.svg') && alternateResult.source === 'sameOrigin', `${profile.name}: trusted source change did not replace the prior manifest art`)

    await page.evaluate(() => window.__cardHarness.showRound(true))
    await page.waitForFunction(() => document.querySelector('.round-image .l12-card-image__img')?.naturalWidth > 0)
    const roundResult = await page.evaluate(() => ({
      source: document.querySelector('.round-image .l12-card-image')?.dataset.source,
      src: document.querySelector('.round-image .l12-card-image__img')?.currentSrc ?? '',
    }))
    check(roundResult.source === 'sameOrigin' && roundResult.src.includes('/round-low.svg'), `${profile.name}: RoundCard fallback did not use stable CardImage art`)

    await page.evaluate(async () => {
      await window.__cardHarness.setLegacyUrl(undefined)
      await window.__cardHarness.setCard('CARD-C')
    })
    await page.waitForFunction(() => document.querySelector('.detail-image img')?.currentSrc.includes('/c-low.svg')
      && document.querySelector('.detail-image img')?.naturalWidth > 0)
    await page.waitForTimeout(30)
    await page.evaluate(() => window.__cardHarness.showDetail(false))
    await page.waitForTimeout(520)
    check(await page.locator('.detail-image').count() === 0, `${profile.name}: unmounted detail image was recreated by a late preload callback`)
    check(pageErrors.length === 0, `${profile.name}: browser errors: ${pageErrors.join('; ')}`)

    const requests = Object.fromEntries([...requestCounts.entries()])
    check(requests['/card-assets/card-assets.manifest.json'] === 1, `${profile.name}: manifest should be shared (received ${requests['/card-assets/card-assets.manifest.json'] ?? 0} requests)`)
    report.profiles.push({ name: profile.name, upgrade, switchResult, alternateResult, roundResult, requests, pageErrors })
    await context.close()
  }

  report.status = report.errors.length ? 'failed' : 'passed'
} catch (error) {
  report.status = 'failed'
  report.errors.push(error instanceof Error ? error.stack || error.message : String(error))
} finally {
  await browser?.close()
  await server.close()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
}

if (report.errors.length) {
  console.error(`Card image stability verification failed:\n- ${report.errors.join('\n- ')}\nReport: ${path.join(output, 'report.json')}`)
  process.exit(1)
}
console.log(`Card image stability verification passed: ${report.assertions} assertions across ${report.profiles.length} profiles. Report: ${path.join(output, 'report.json')}`)
