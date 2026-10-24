import assert from 'node:assert/strict'
import fs from 'node:fs'
import { spawnSync } from 'node:child_process'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const browserChannel = process.env.L12_BROWSER_CHANNEL || 'msedge'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/card-image-recovery-20261025/browser-r1'
const cache = process.env.L12_VITE_CACHE_DIR || 'D:/GPT/Legion12/cache/primary/card-image-recovery-vite-20261025'
const storageGuard = spawnSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
  ['-NoProfile', '-Command', ". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; @($env:L12_QA_OUT,(Join-Path $env:L12_QA_OUT 'downloads'),$env:TEMP,$env:L12_VITE_CACHE_DIR) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose 'card image recovery browser' }"],
  { env: { ...process.env, L12_QA_OUT: output, L12_VITE_CACHE_DIR: cache }, encoding: 'utf8', windowsHide: true })
assert.equal(storageGuard.status, 0, storageGuard.stderr || 'Card image browser paths must resolve to D/E')
assert(!fs.existsSync(path.join(output, 'report.json')), 'Never overwrite a prior acceptance report')
fs.mkdirSync(output, { recursive: true })
const ts = require('typescript')
const sharp = require('sharp')
const archiveSource = fs.readFileSync(path.join(root, 'src/l12/CardArchive.vue'), 'utf8')
const archiveScript = archiveSource.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)[1]
const archiveAst = ts.createSourceFile('CardArchive.ts', archiveScript, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const listSelector = archiveAst.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'archiveListImageUrl')
assert(listSelector, 'Use the actual archive list selector')
const listSelectorCode = ts.transpileModule(listSelector.getText(archiveAst), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const uploadedOriginal = `/api/site/media/${'a'.repeat(32)}/desktop/${'b'.repeat(64)}.webp`
const uploadedThumb = `/api/site/media/${'a'.repeat(32)}/thumbnail/${'c'.repeat(64)}.webp`

const cards = Object.fromEntries([
  ['CARD-A', 'a'],
  ['CARD-B', 'b'],
  ['CARD-C', 'c'],
  ['ROUND-FALLBACK', 'round'],
  ['RECOVER', 'recover'],
  ['RECOVER-SECOND', 'recover-second'],
  ['LANDSCAPE', 'landscape'],
].map(([cardId, stem]) => [cardId, {
  cardId,
  contentHash: `qa-${stem}`,
  width: stem === 'landscape' ? 960 : 744,
  height: stem === 'landscape' ? 600 : 1039,
  orientation: stem === 'landscape' ? 'landscape' : 'portrait',
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
import DeckCover from '/src/l12/DeckCover.vue'
import '/src/style.css'

const detailVisible = ref(false)
const roundVisible = ref(false)
const cardId = ref('CARD-A')
const fit = ref('contain')
const legacyUrl = ref(undefined)
const recoveryVisible = ref(false), recoveryOffscreen = ref(false), recoveryId = ref('RECOVER'), thumbnailsVisible = ref(false)
const cornersVisible = ref(false)
${listSelectorCode}
const uploadedCard = { id: 'CARD-A', imageUrl: ${JSON.stringify(uploadedOriginal)}, thumbnailUrl: ${JSON.stringify(uploadedThumb)} }
const cornerCatalog = ['A', 'B', 'C'].map((stem, index) => ({ id: 'CARD-' + stem, nameZh: stem, cardType: ['legion', 'tactic', 'artifact'][index], cost: index + 1, imageUrl: undefined }))

window.__cardHarness = {
  async showDetail(value = true) { detailVisible.value = value; await nextTick() },
  async showRound(value = true) { roundVisible.value = value; await nextTick() },
  async setCard(value) { cardId.value = value; await nextTick() },
  async setFit(value) { fit.value = value; await nextTick() },
  async setLegacyUrl(value) { legacyUrl.value = value; await nextTick() },
  async showRecovery(value = true) { recoveryVisible.value = value; await nextTick() },
  async moveRecoveryOffscreen(value) { recoveryOffscreen.value = value; await nextTick() },
  async setRecoveryId(value) { recoveryId.value = value; await nextTick() },
  async showThumbnails() { thumbnailsVisible.value = true; await nextTick() },
  async showCorners() { thumbnailsVisible.value = false; cornersVisible.value = true; await nextTick() },
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
      recoveryVisible.value ? h('section', { id: 'recovery-slot', style: { position: 'fixed', top: '8px', left: '8px', width: '100px', height: '140px', transform: recoveryOffscreen.value ? 'translateY(200vh)' : 'none' } }, [h(CardImage, {
        class: 'recovery-image', cardId: recoveryId.value, alt: 'recovery fixture', intent: 'thumb', eager: true,
      })]) : null,
      thumbnailsVisible.value ? h('section', { id: 'thumbnail-slot' }, [
        h(CardImage, { class: 'archive-thumb-probe', cardId: uploadedCard.id, legacyUrl: archiveListImageUrl(uploadedCard), alt: 'thumbnail probe', intent: 'thumb', eager: true }),
        h(CardImage, { class: 'archive-detail-probe', cardId: uploadedCard.id, legacyUrl: uploadedCard.imageUrl, alt: 'detail probe', intent: 'detail', eager: true }),
      ]) : null,
      cornersVisible.value ? h('section', { id: 'corner-probes', style: { position: 'fixed', top: '16px', left: '16px', width: '784px', height: '288px', display: 'flex', gap: '12px', zIndex: 10 } }, [
        h('div', { style: { width: '100px', flex: 'none' } }, [
          h('div', { style: { width: '100px', height: '140px' } }, [h(CardImage, { class: 'corner-portrait', cardId: 'CARD-A', alt: 'unframed portrait', intent: 'thumb', eager: true })]),
          h('div', { style: { width: '100px', height: '140px', marginTop: '8px' } }, [h(CardImage, { class: 'corner-zero', cardId: 'CARD-A', alt: 'zero override control', intent: 'thumb', eager: true, style: { '--l12-card-image-radius': '0px' } })]),
        ]),
        h('div', { style: { width: '160px', flex: 'none' } }, [
          h('div', { style: { width: '160px', height: '100px' } }, [h(CardImage, { class: 'corner-landscape', cardId: 'LANDSCAPE', alt: 'unframed landscape', intent: 'thumb', eager: true })]),
          h('div', { style: { width: '96px', height: '96px', marginTop: '8px' } }, [h(RoundCardImage, { class: 'corner-round', cardId: 'ROUND-FALLBACK' })]),
        ]),
        h('div', { class: 'corner-deck-cover', style: { width: '500px', flex: 'none' } }, [h(DeckCover, { name: 'synthetic corner cover', catalog: cornerCatalog, coverCardIds: cornerCatalog.map(card => card.id) })]),
      ]) : null,
    ])
  },
}).mount('#app')
`

const requestCounts = new Map()
const requestTimeline = []
let recoveryAvailable = false
function countRequest(pathname) {
  const next = (requestCounts.get(pathname) ?? 0) + 1
  requestCounts.set(pathname, next)
  requestTimeline.push({ event: 'request', pathname, count: next, at: Date.now(), recoveryAvailable })
  return next
}
function svg(stem) {
  const color = stem.startsWith('a-') ? '#8f2836' : stem.startsWith('b-') ? '#176070' : '#665319'
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${stem.startsWith('landscape') ? 960 : 744}" height="${stem.startsWith('landscape') ? 600 : 1039}"><rect width="100%" height="100%" fill="${color}"/><text x="50%" y="50%" fill="white" font-size="96" text-anchor="middle">${stem}</text></svg>`
}

const server = await createServer({
  root,
  cacheDir: cache,
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
        response.once('finish', () => {
          if (url.pathname.startsWith('/__card_image__/')) requestTimeline.push({ event: 'response', pathname: url.pathname, status: response.statusCode, at: Date.now(), recoveryAvailable })
        })
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
          const failThisRequest = (stem.startsWith('recover') && !recoveryAvailable) || requestNumber <= failBeforeSuccess
          const delay = stem === 'c-high' ? 420 : stem.endsWith('-high') ? 140 : stem.startsWith('b-') ? 90 : 12
          setTimeout(() => {
            response.setHeader('Cache-Control', 'no-store')
            if (failThisRequest) {
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
        if (url.pathname === uploadedOriginal || url.pathname === uploadedThumb) {
          countRequest(url.pathname)
          response.setHeader('Content-Type', 'image/svg+xml')
          response.setHeader('Cache-Control', 'public, max-age=31536000, immutable')
          response.end(svg(url.pathname === uploadedThumb ? 'synthetic-thumbnail' : 'synthetic-original'))
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

let browser, activePage
const stage = process.env.L12_CARD_IMAGE_QA_STAGE || 'full'
assert(['full', 'recovery', 'pair', 'retirement'].includes(stage), 'Unknown QA stage')
const report = { status: 'running', output, browserChannel, stage, requestedProfiles: [], finishedProfiles: [], profiles: [], checkpoints: [], pairs: [], errors: [], assertions: 0 }
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
  browser = await chromium.launch({ headless: true, channel: browserChannel, downloadsPath: path.join(output, 'downloads') })

  const profiles = [
    { name: 'desktop-60hz', intervalMs: 16.67, viewport: { width: 1920, height: 1080 } },
    { name: 'desktop-120hz', intervalMs: 8.33, viewport: { width: 1920, height: 1080 } },
    { name: 'mobile-landscape-120hz', intervalMs: 8.33, viewport: { width: 844, height: 390 }, isMobile: true, hasTouch: true },
  ]
  const selectedProfile = process.env.L12_CARD_IMAGE_QA_PROFILE
  assert(selectedProfile === undefined || profiles.some(profile => profile.name === selectedProfile), 'Empty, unknown or duplicate profile selection is refused')
  if (stage === 'retirement') assert.equal(selectedProfile, 'desktop-60hz', 'Retirement diagnosis is one desktop profile only')
  report.requestedProfiles = profiles.filter(profile => selectedProfile === undefined || profile.name === selectedProfile).map(profile => profile.name)
  for (const profile of profiles.filter(profile => !selectedProfile || profile.name === selectedProfile)) {
    requestCounts.clear()
    requestTimeline.length = 0
    recoveryAvailable = false
    const context = await browser.newContext({ viewport: profile.viewport, isMobile: profile.isMobile, hasTouch: profile.hasTouch })
    const page = await context.newPage()
    activePage = page
    const pageErrors = []
    page.on('pageerror', error => pageErrors.push(error.message))
    await page.goto(`http://127.0.0.1:${port}/__card_image_stability__`, { waitUntil: 'networkidle' })
    await page.waitForFunction(() => document.querySelector('.primer-image img')?.naturalWidth > 0)

    if (stage === 'retirement') {
      const retirementPairs = []
      for (const variant of ['baseline', 'clear-retired-img']) {
        const diagnosticContext = variant === 'baseline' ? context : await browser.newContext({ viewport: profile.viewport })
        const diagnosticPage = variant === 'baseline' ? page : await diagnosticContext.newPage()
        const initiators = []
        const diagnosticSession = await diagnosticContext.newCDPSession(diagnosticPage)
        diagnosticSession.on('Network.requestWillBeSent', event => {
          if (new URL(event.request.url).pathname.startsWith('/__card_image__/recover')) {
            initiators.push({ requestId: event.requestId, url: event.request.url, wallTime: event.wallTime,
              timestamp: event.timestamp, initiator: event.initiator })
          }
        })
        await diagnosticSession.send('Network.enable')
        activePage = diagnosticPage
        if (variant !== 'baseline') {
          requestCounts.clear(); requestTimeline.length = 0; recoveryAvailable = false
          await diagnosticPage.goto(`http://127.0.0.1:${port}/__card_image_stability__`, { waitUntil: 'networkidle' })
          await diagnosticPage.waitForFunction(() => document.querySelector('.primer-image img')?.naturalWidth > 0)
        }
        try {
          await diagnosticPage.evaluate(clearRetired => {
            const original = Node.prototype.removeChild
            const ids = new WeakMap(), known = new WeakSet(), trace = []
            let nextId = 0
            const capture = (reason, image) => {
              if (!ids.has(image)) ids.set(image, ++nextId)
              known.add(image)
              const root = document.querySelector('.recovery-image'), state = root?.__vueParentComponent?.setupState
              trace.push({ reason, at: Date.now(), node: ids.get(image), connected: image.isConnected,
                src: image.getAttribute('src'), srcset: image.getAttribute('srcset'), currentSrc: image.currentSrc,
                naturalWidth: image.naturalWidth, sourceIndex: state?.sourceIndex, renderKey: state?.renderKey,
                isCurrent: root?.querySelector('img') === image })
            }
            const captureEvent = event => {
              const image = event.target
              if (image instanceof HTMLImageElement && (known.has(image) || image.closest('#recovery-slot'))) capture(event.type, image)
            }
            const wrapped = function (child) {
              const retired = child instanceof HTMLImageElement && this instanceof HTMLPictureElement && this.closest('#recovery-slot')
              if (retired) {
                capture('before-remove', child)
                if (clearRetired) { child.removeAttribute('src'); child.removeAttribute('srcset'); capture('cleared-before-remove', child) }
              }
              const result = original.call(this, child)
              if (retired) {
                capture('after-remove', child)
                queueMicrotask(() => capture('after-remove-microtask', child))
              }
              return result
            }
            Node.prototype.removeChild = wrapped
            document.addEventListener('load', captureEvent, true)
            document.addEventListener('error', captureEvent, true)
            window.__retirementTrace = trace
            window.__restoreRetirementHook = () => {
              if (Node.prototype.removeChild === wrapped) Node.prototype.removeChild = original
              document.removeEventListener('load', captureEvent, true)
              document.removeEventListener('error', captureEvent, true)
            }
          }, variant === 'clear-retired-img')
          await diagnosticPage.evaluate(async () => { await window.__cardHarness.moveRecoveryOffscreen(true); await window.__cardHarness.showRecovery() })
          await diagnosticPage.waitForFunction(() => document.querySelector('.recovery-image')?.dataset.source === 'placeholder')
          await diagnosticPage.waitForTimeout(100)
          const failedRequests = requestCounts.get('/__card_image__/recover-low.svg') ?? 0
          check(failedRequests === 3, `${profile.name}/${variant}: original image retry budget remains three attempts (actual ${failedRequests})`)
          recoveryAvailable = true
          await diagnosticPage.evaluate(() => window.dispatchEvent(new Event('online')))
          await diagnosticPage.waitForTimeout(350)
          const offscreenRequests = requestCounts.get('/__card_image__/recover-low.svg') ?? 0
          check(offscreenRequests === 3, `${profile.name}/${variant}: online must not wake an offscreen failed image (actual ${offscreenRequests})`)
          await diagnosticPage.evaluate(() => window.__cardHarness.moveRecoveryOffscreen(false))
          await diagnosticPage.waitForFunction(() => document.querySelector('.recovery-image img')?.currentSrc.includes('/recover-low.svg')
            && document.querySelector('.recovery-image img')?.naturalWidth > 0)
          const restoredRequests = requestCounts.get('/__card_image__/recover-low.svg') ?? 0
          check(restoredRequests === 4, `${profile.name}/${variant}: visible recovery makes exactly one additional request (actual ${restoredRequests})`)
          await diagnosticPage.evaluate(() => { for (let i = 0; i < 5; i++) window.dispatchEvent(new Event('online')) })
          await diagnosticPage.waitForTimeout(350)
          const repeatedOnlineRequests = requestCounts.get('/__card_image__/recover-low.svg') ?? 0
          check(repeatedOnlineRequests === 4, `${profile.name}/${variant}: repeated online does not reload successful pixels (actual ${repeatedOnlineRequests})`)
          retirementPairs.push({ variant, failedRequests, offscreenRequests, restoredRequests, repeatedOnlineRequests,
            timeline: [...requestTimeline], initiators, trace: await diagnosticPage.evaluate(() => window.__retirementTrace) })
        } finally {
          await diagnosticPage.evaluate(() => window.__restoreRetirementHook?.()).catch(() => {})
          await diagnosticSession.detach().catch(() => {})
          await diagnosticContext.close()
        }
      }
      check(retirementPairs[0].failedRequests === 4 && retirementPairs[1].failedRequests === 3,
        `${profile.name}: cleaning only retired img attributes must remove the extra pre-online request`)
      report.profiles.push({ name: profile.name, retirementPairs })
      report.finishedProfiles.push(profile.name)
      continue
    }

    let upgrade = null, switchResult = null, alternateResult = null, roundResult = null
    if (stage === 'full') {
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

    upgrade = await page.evaluate(() => {
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
    switchResult = await page.evaluate(() => ({
      finalSrc: document.querySelector('.detail-image img')?.currentSrc ?? '',
      staleReadyFrames: window.__switchFrames.filter(frame => frame.ready > 0 && frame.src.includes('/a-')).length,
    }))
    check(switchResult.finalSrc.includes('/b-high.svg'), `${profile.name}: true cardId change did not reach CARD-B detail art`)
    check(switchResult.staleReadyFrames === 0, `${profile.name}: CARD-A remained visible after CARD-B switch for ${switchResult.staleReadyFrames} samples`)

    await page.evaluate(() => window.__cardHarness.setLegacyUrl('/api/site/media/alt-b.svg'))
    await page.waitForFunction(() => document.querySelector('.detail-image img')?.currentSrc.includes('/api/site/media/alt-b.svg')
      && document.querySelector('.detail-image img')?.naturalWidth > 0)
    alternateResult = await page.evaluate(() => ({
      src: document.querySelector('.detail-image img')?.currentSrc ?? '',
      source: document.querySelector('.detail-image')?.dataset.source,
    }))
    check(alternateResult.src.includes('/api/site/media/alt-b.svg') && alternateResult.source === 'sameOrigin', `${profile.name}: trusted source change did not replace the prior manifest art`)

    await page.evaluate(() => window.__cardHarness.showRound(true))
    await page.waitForFunction(() => document.querySelector('.round-image .l12-card-image__img')?.naturalWidth > 0)
    roundResult = await page.evaluate(() => ({
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
    }
    if (stage !== 'pair') {
    await page.evaluate(() => {
      window.__recoveryTrace = []
      const capture = reason => {
        const root = document.querySelector('.recovery-image'), image = root?.querySelector('img')
        const state = root?.__vueParentComponent?.setupState
        window.__recoveryTrace.push({ reason, at: performance.now(), source: root?.dataset.source,
          src: image?.getAttribute('src'), currentSrc: image?.currentSrc, naturalWidth: image?.naturalWidth,
          renderKey: state?.renderKey, sourceIndex: state?.sourceIndex })
      }
      window.addEventListener('online', () => capture('online'))
      new MutationObserver(records => {
        if (records.some(record => record.target.closest?.('#recovery-slot')
          || [...record.addedNodes].some(node => node.id === 'recovery-slot'))) capture('DOM-mutation')
      }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['src', 'data-source'] })
    })
    await page.evaluate(async () => { await window.__cardHarness.moveRecoveryOffscreen(true); await window.__cardHarness.showRecovery() })
    await page.waitForFunction(() => document.querySelector('.recovery-image')?.dataset.source === 'placeholder')
    check(requestCounts.get('/__card_image__/recover-low.svg') === 3, `${profile.name}: original image retry budget remains three attempts`)
    recoveryAvailable = true
    await page.evaluate(() => window.dispatchEvent(new Event('online')))
    await page.waitForTimeout(350)
    check(requestCounts.get('/__card_image__/recover-low.svg') === 3, `${profile.name}: online must not wake an offscreen failed image`)
    await page.evaluate(() => window.__cardHarness.moveRecoveryOffscreen(false))
    await page.waitForFunction(() => document.querySelector('.recovery-image img')?.currentSrc.includes('/recover-low.svg')
      && document.querySelector('.recovery-image img')?.naturalWidth > 0)
    report.checkpoints.push({ profile: profile.name, step: 'visible-recovery', requests: Object.fromEntries(requestCounts), trace: await page.evaluate(() => window.__recoveryTrace) })
    check(requestCounts.get('/__card_image__/recover-low.svg') === 4, `${profile.name}: visible failed image retries original URL exactly once after recovery (actual ${requestCounts.get('/__card_image__/recover-low.svg')})`)
    await page.evaluate(() => { for (let i = 0; i < 5; i++) window.dispatchEvent(new Event('online')) })
    await page.waitForTimeout(350)
    report.checkpoints.push({ profile: profile.name, step: 'repeated-online', requests: Object.fromEntries(requestCounts), trace: await page.evaluate(() => window.__recoveryTrace) })
    check(requestCounts.get('/__card_image__/recover-low.svg') === 4, `${profile.name}: successful recovery must not rerequest on repeated online (actual ${requestCounts.get('/__card_image__/recover-low.svg')})`)
    recoveryAvailable = false
    await page.evaluate(() => window.__cardHarness.setRecoveryId('RECOVER-SECOND'))
    await page.waitForFunction(() => document.querySelector('.recovery-image')?.dataset.source === 'placeholder')
    recoveryAvailable = true
    await page.evaluate(async () => { window.dispatchEvent(new Event('online')); await window.__cardHarness.showRecovery(false) })
    await page.waitForTimeout(350)
    check(requestCounts.get('/__card_image__/recover-second-low.svg') === 3, `${profile.name}: unmount cancels queued recovery before its image request`)
    await page.evaluate(() => window.__cardHarness.showThumbnails())
    await page.waitForFunction(() => [...document.querySelectorAll('#thumbnail-slot img')].length === 2
      && [...document.querySelectorAll('#thumbnail-slot img')].every(image => image.naturalWidth > 0))
    check((await page.locator('.archive-thumb-probe img').getAttribute('src')) === uploadedThumb, `${profile.name}: actual archive list selector uses trusted thumbnail URL`)
    check((await page.locator('.archive-detail-probe img').getAttribute('src')) === uploadedOriginal, `${profile.name}: detail retains original image URL`)
    await page.screenshot({ path: path.join(output, `${profile.name}-recovery-and-thumbnail.png`) })
    }
    await page.evaluate(() => window.__cardHarness.showCorners())
    await page.waitForFunction(() => [...document.querySelectorAll('#corner-probes img')].length === 7
      && [...document.querySelectorAll('#corner-probes img')].every(image => image.naturalWidth > 0))
    const coverIds = await page.locator('.corner-deck-cover .deck-cover').getAttribute('data-cover-cards')
    check(coverIds === 'CARD-A,CARD-B,CARD-C', `${profile.name}: real DeckCover retains all three requested representative IDs`)
    check(await page.locator('.corner-deck-cover .deck-cover__card img').count() === 3, `${profile.name}: real DeckCover has three decoded card images`)
    const panel = page.locator('#corner-probes')
    const panelRect = await panel.boundingBox()
    assert(panelRect)
    const panelBuffer = await panel.screenshot({ omitBackground: true })
    const pixels = await sharp(panelBuffer).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
    const cornerChecks = []
    for (const [name, selector, rounded] of [
      ['portrait', '.corner-portrait img', true], ['landscape', '.corner-landscape img', true],
      ['deck-cover-overflow-visible', '.corner-deck-cover .deck-cover__card:nth-child(2) img', true],
      ['zero-override-control', '.corner-zero img', false], ['round-outer-mask', '.corner-round', true],
    ]) {
      const target = page.locator(selector)
      const geometry = await target.evaluate(element => {
        const rect = element.getBoundingClientRect(), style = getComputedStyle(element)
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
          radius: style.borderTopLeftRadius, overflow: style.overflow, clipPath: style.clipPath,
          currentSrc: element.currentSrc || element.querySelector('img')?.currentSrc }
      })
      const scaleX = pixels.info.width / panelRect.width, scaleY = pixels.info.height / panelRect.height
      // The whole panel is integer-aligned. Pick pixel centers strictly inside the real target,
      // not the outside row introduced by rounding a fractional locator screenshot outward.
      const x = Math.floor((geometry.x - panelRect.x) * scaleX + .5)
      const y = Math.floor((geometry.y - panelRect.y) * scaleY + .5)
      // DeckCover's right card overlaps its middle card. The quarter edge is unoccluded.
      const edgeX = Math.floor((geometry.x - panelRect.x + geometry.width * (name === 'round-outer-mask' ? .5 : .25)) * scaleX)
      const sample = (column, row) => Array.from(pixels.data.subarray((row * pixels.info.width + column) * 4, (row * pixels.info.width + column) * 4 + 4))
      const corner = sample(x, y), edge = sample(edgeX, y)
      const difference = corner.reduce((sum, channel, index) => sum + Math.abs(channel - edge[index]), 0)
      const expectedEdge = geometry.currentSrc?.includes('/a-low.svg') ? [143, 40, 54]
        : geometry.currentSrc?.includes('/b-low.svg') ? [23, 96, 112] : [102, 83, 25]
      check(expectedEdge.every((channel, index) => Math.abs(channel - edge[index]) < 10), `${profile.name}: ${name} edge sample must be the actual decoded fixture, not an occluding card/background`)
      check(rounded ? difference > 20 : difference < 10, `${profile.name}: ${name} must have an actual ${rounded ? 'masked' : 'square'} decoded-image corner (difference ${difference})`)
      if (name !== 'round-outer-mask') check(rounded ? geometry.radius !== '0px' : geometry.radius === '0px', `${profile.name}: ${name} radius override is effective`)
      cornerChecks.push({ name, ...geometry, panelRect, panelPixels: { width: pixels.info.width, height: pixels.info.height }, sample: { x, y, edgeX, corner, edge }, cornerEdgeDifference: difference })
      if (name === 'deck-cover-overflow-visible' && stage === 'pair') {
        const saved = await target.evaluate(element => {
          const style = getComputedStyle(element)
          return { overflow: element.style.overflow, clipPath: element.style.clipPath,
            radius: style.getPropertyValue('--l12-card-image-radius').trim() || style.getPropertyValue('--l12-card-image-default-radius').trim() }
        })
        const pair = { profile: profile.name, original: cornerChecks.at(-1), variants: [] }
        report.pairs.push(pair)
        try {
          for (const variant of ['overflow-hidden', 'clip-path']) {
            await target.evaluate((element, { variant, saved }) => {
              element.style.overflow = variant === 'overflow-hidden' ? 'hidden' : saved.overflow
              element.style.clipPath = variant === 'clip-path' ? `inset(0 round ${saved.radius})` : saved.clipPath
            }, { variant, saved })
            const variantPixels = await sharp(await panel.screenshot({ path: path.join(output, `${profile.name}-deck-${variant}.png`), omitBackground: true }))
              .ensureAlpha().raw().toBuffer({ resolveWithObject: true })
            const point = column => Array.from(variantPixels.data.subarray((y * variantPixels.info.width + column) * 4, (y * variantPixels.info.width + column) * 4 + 4))
            const variantCorner = point(x), variantEdge = point(edgeX)
            const variantDifference = variantCorner.reduce((sum, channel, index) => sum + Math.abs(channel - variantEdge[index]), 0)
            const variantState = await target.evaluate(element => {
              const style = getComputedStyle(element), rect = element.getBoundingClientRect()
              return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, overflow: style.overflow, clipPath: style.clipPath }
            })
            pair.variants.push({ variant, ...variantState, sample: { x, y, edgeX, corner: variantCorner, edge: variantEdge }, cornerEdgeDifference: variantDifference })
            check(expectedEdge.every((channel, index) => Math.abs(channel - variantEdge[index]) < 10), `${profile.name}: paired ${variant} edge remains the same decoded image`)
            check(variantDifference > 20, `${profile.name}: paired ${variant} must actually mask the corner without lowering the threshold`)
          }
        } finally {
          await target.evaluate((element, saved) => { element.style.overflow = saved.overflow; element.style.clipPath = saved.clipPath }, saved)
        }
      }
    }
    await page.locator('#corner-probes').screenshot({ path: path.join(output, `${profile.name}-shared-corners.png`) })
    check(pageErrors.length === 0, `${profile.name}: browser errors: ${pageErrors.join('; ')}`)

    const requests = Object.fromEntries([...requestCounts.entries()])
    check(requests['/card-assets/card-assets.manifest.json'] === 1, `${profile.name}: manifest should be shared (received ${requests['/card-assets/card-assets.manifest.json'] ?? 0} requests)`)
    report.profiles.push({ name: profile.name, upgrade, switchResult, alternateResult, roundResult, cornerChecks, requests, pageErrors })
    report.finishedProfiles.push(profile.name)
    await context.close()
  }

  report.status = report.errors.length ? 'failed' : 'passed'
} catch (error) {
  report.status = 'failed'
  report.errors.push(error instanceof Error ? error.stack || error.message : String(error))
} finally {
  report.lastRequests = Object.fromEntries(requestCounts)
  report.lastRequestTimeline = [...requestTimeline]
  try { report.lastRecoveryTrace = await activePage?.evaluate(() => window.__recoveryTrace || []) } catch { /* Closed successful profile already owns its checkpoints. */ }
  await browser?.close()
  await server.close()
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
}

if (report.errors.length) {
  console.error(`Card image stability verification failed:\n- ${report.errors.join('\n- ')}\nReport: ${path.join(output, 'report.json')}`)
  process.exit(1)
}
console.log(`Card image stability verification passed: ${report.assertions} assertions across ${report.profiles.length} profiles. Report: ${path.join(output, 'report.json')}`)
