import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'

const scriptPath = fileURLToPath(import.meta.url)
const appRoot = path.resolve(path.dirname(scriptPath), '..')
const repoRoot = path.resolve(appRoot, '..')
const qaRoot = path.resolve(process.env.L12_QA_OUT || '')
const viteCache = path.resolve(process.env.L12_VITE_CACHE_DIR || '')
const phase = process.env.L12_QA_PHASE || 'current'
if (!process.env.L12_QA_OUT) throw new Error('L12_QA_OUT is required')
if (!process.env.L12_VITE_CACHE_DIR) throw new Error('L12_VITE_CACHE_DIR is required')
if (!/^[DE]:[\\/]/i.test(qaRoot)) throw new Error(`L12_QA_OUT must resolve to D/E: ${qaRoot}`)
if (!/^[DE]:[\\/]/i.test(viteCache)) throw new Error(`L12_VITE_CACHE_DIR must resolve to D/E: ${viteCache}`)
if (!/^[a-z0-9-]+$/i.test(phase)) throw new Error(`Invalid L12_QA_PHASE: ${phase}`)
const phaseRoot = path.join(qaRoot, phase)
fs.mkdirSync(phaseRoot, { recursive: true })

const requireFromApp = createRequire(path.join(appRoot, 'package.json'))
const { createServer } = await import(pathToFileURL(requireFromApp.resolve('vite')).href)
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const { buildBattlePreviewDocument } = await import(pathToFileURL(path.join(appRoot, 'scripts/lib/battlePreviewDocument.mjs')).href)
const sha256 = input => crypto.createHash('sha256').update(input).digest('hex').toUpperCase()
const fileSha256 = filename => sha256(fs.readFileSync(filename))
const runGit = args => execFileSync('git', args, { cwd: repoRoot, encoding: 'utf8', env: { ...process.env, GIT_NO_LAZY_FETCH: '1' } }).trim()
const currentHead = runGit(['rev-parse', 'HEAD'])
const promptOverlayPath = path.join(appRoot, 'src/l12/game/PromptOverlay.vue')
const promptOverlaySource = fs.readFileSync(promptOverlayPath, 'utf8')

const productionIndexHtml = fs.readFileSync(path.join(appRoot, 'index.html'), 'utf8')
const previewDocument = buildBattlePreviewDocument(productionIndexHtml)
const fixtureSource = fs.readFileSync(path.join(appRoot, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const marker = 'const entry = `'
const fixtureStart = fixtureSource.indexOf(marker) + marker.length
const fixtureEnd = fixtureSource.indexOf('const isPicker=', fixtureStart)
assert.ok(fixtureStart >= marker.length && fixtureEnd > fixtureStart, 'sanitized GamePage fixture must remain available')
const entry = fixtureSource.slice(fixtureStart, fixtureEnd) + `
import {ref} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
const placementMode=params.get('mode')||'all-bottom'
const placementCount=Number(params.get('count')||2)
if(!['all-bottom','all-top-bottom'].includes(placementMode))throw new Error('Unsupported placement mode '+placementMode)
if(!Number.isInteger(placementCount)||placementCount<2||placementCount>24)throw new Error('Invalid placement count '+placementCount)
const definitions=['S02-0620','S01-0003'].map(id=>catalog.find(item=>item.id===id))
if(definitions.some(item=>!item))throw new Error('Missing exact public placement card definitions')
const ids=Array.from({length:placementCount},(_,index)=>'placement-'+index)
const promptData={action:placementMode==='all-bottom'?'s2-rune-power-bottom-order':'reorder-order',placementMode,layout:'single-row',displayCardIds:ids.join('|'),sourceName:'牌库排序'}
ids.forEach((id,index)=>{const definition=definitions[index%definitions.length];promptData[id+':cardId']=definition.id;promptData[id+':name']=definition.nameZh;promptData[id+':cardType']=definition.cardType;promptData[id+':cost']=String(definition.cost||0);promptData[id+':troops']=String(definition.troops||0)})
l12State.gmEnabled=false;l12State.spectating=false;l12State.observerView='public';l12State.pendingAction=false
l12State.game.you=0;l12State.game.activePlayer=0;l12State.game.phase='Main';l12State.game.round=3;l12State.game.revision=17;l12State.game.recentEvents=[]
for(const player of l12State.game.players){player.hand=[];player.handCount=0;player.field=[[null,null,null],[null,null,null]]}
l12State.game.legalAttackTargets={}
l12State.game.prompts=[{promptId:'placement-centering-'+placementMode+'-'+placementCount,playerIndex:0,kind:'order',text:'调整卡牌顺序后放回牌库。',validChoices:ids,minChoose:placementCount,maxChoose:placementCount,continuation:'card-effect',stackItemId:'placement-centering-stack',sourceInstanceId:'placement-centering-source',sourceCardId:'S02-0620',createdRevision:17,controller:0,data:promptData}]
l12State.game.stateHash='placement-centering-'+placementMode+'-'+placementCount
window.__fixtureFacts={placementMode,placementCount,ids,prompt:l12State.game.prompts[0]}
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h('div',{class:'l12-landscape-surface','data-l12-landscape-canvas':''},[h(GamePage)])}})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`

const controlledArtwork = { 'S02-0620': '/assets/l12/special/master/S02-06M2.png', 'S01-0003': '/assets/l12/special/master/ST02-M1.png' }
const cardManifest = JSON.stringify({
  schemaVersion: 3, catalogVersion: 'placement-centering-controlled-local-art', assetVersion: 'placement-centering-controlled-local-art', basePath: '/',
  cards: Object.fromEntries(Object.entries(controlledArtwork).map(([cardId, url]) => [cardId, { cardId, contentHash: `qa-${cardId}`, width: 1240, height: 1732, orientation: 'portrait', variants: { thumbWebp: url, boardWebp: url, detailWebp: url } }])),
})
const server = await createServer({
  root: appRoot, configLoader: 'runner', cacheDir: viteCache, logLevel: 'error', server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'placement-centering-consumers',
    resolveId(id) { if (id === '/__l12_battle_preview__.js') return id },
    load(id) { if (id === '/__l12_battle_preview__.js') return entry },
    configureServer(vite) { vite.middlewares.use((req, res, next) => {
      if (req.url?.match(/^\/card-assets\/card-assets\.manifest\.json(\?|$)/)) { res.setHeader('Content-Type', 'application/json; charset=utf-8'); res.end(cardManifest); return }
      if (req.url?.match(/^\/__placement_centering__(\?|$)/)) { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(previewDocument); return }
      next()
    }) },
  }],
})

const viewports = [
  { id: 'desktop-1366x768', width: 1366, height: 768 },
  { id: 'landscape-844x390', width: 844, height: 390 },
  { id: 'portrait-390x844', width: 390, height: 844 },
]
const placementModes = ['all-bottom', 'all-top-bottom']
const expectedViewportMeta = 'width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover'
const cases = []
const pageErrors = []
const blockedExternalRequests = []
const rounded = value => Math.round(value * 100) / 100
const roundedRect = rect => Object.fromEntries(Object.entries(rect).map(([key, value]) => [key, rounded(value)]))
const normalizeGeometry = value => JSON.parse(JSON.stringify(value, (_key, item) => typeof item === 'number' ? rounded(item) : item))
const stableEnvelope = envelope => { if (!envelope) return envelope; const { requestId, ...stable } = envelope; return stable }
const physicalCenterClick = async (page, locator, label) => {
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  const hit = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect(); const x = rect.left + rect.width / 2; const y = rect.top + rect.height / 2; const top = document.elementFromPoint(x, y)
    return { x, y, owned: Boolean(top && element.contains(top)), hitTag: top?.tagName ?? null, hitClass: top instanceof HTMLElement ? top.className : null, rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height } }
  })
  assert.equal(hit.owned, true, `${label} must own its physical centre: ${JSON.stringify(hit)}`)
  await page.mouse.click(hit.x, hit.y)
  return { ...hit, rect: roundedRect(hit.rect) }
}
const surfaceFacts = page => page.evaluate(() => {
  const surface = document.querySelector('.l12-landscape-surface'); const rect = surface?.getBoundingClientRect()
  return { viewport: { width: innerWidth, height: innerHeight }, viewportMeta: document.querySelector('meta[name="viewport"]')?.getAttribute('content') ?? null, surfaceCount: document.querySelectorAll('.l12-landscape-surface').length, surfaceHasGamePage: Boolean(surface?.querySelector('.game-page')), surfaceDataAttribute: surface?.hasAttribute('data-l12-landscape-canvas') ?? false, surfaceRect: rect ? { left: rect.left, top: rect.top, width: rect.width, height: rect.height } : null, teleportHostCount: document.querySelectorAll('#l12-landscape-teleports').length, viewportMode: { kind: document.documentElement.dataset.l12Viewport ?? null, mobile: document.documentElement.dataset.l12Mobile ?? null, rotated: document.documentElement.dataset.l12Rotated ?? null } }
})
const assertSurface = (facts, viewport) => {
  assert.deepEqual(facts.viewport, { width: viewport.width, height: viewport.height }); assert.equal(facts.viewportMeta, expectedViewportMeta); assert.equal(facts.surfaceCount, 1); assert.equal(facts.surfaceHasGamePage, true); assert.equal(facts.surfaceDataAttribute, true); assert.equal(facts.teleportHostCount, 1); assert.equal(facts.viewportMode.kind, 'landscape')
  assert.equal(facts.viewportMode.mobile, viewport.id === 'desktop-1366x768' ? 'false' : 'true'); assert.equal(facts.viewportMode.rotated, viewport.id === 'portrait-390x844' ? 'true' : 'false')
}
const rowGeometry = row => row.evaluate(element => {
  const rowRect = element.getBoundingClientRect(); const candidates = [...element.querySelectorAll('.prompt-card-candidate')]; const candidateRects = candidates.map(candidate => candidate.getBoundingClientRect()); const rotated = document.documentElement.dataset.l12Rotated === 'true'; const start = rect => rotated ? rect.top : rect.left; const end = rect => rotated ? rect.bottom : rect.right; const rowStart = start(rowRect); const rowEnd = end(rowRect); const unionStart = Math.min(...candidateRects.map(start)); const unionEnd = Math.max(...candidateRects.map(end)); const first = candidateRects.at(0); const last = candidateRects.at(-1); const firstStyle = candidates[0] ? getComputedStyle(candidates[0]) : null; const lastStyle = candidates.at(-1) ? getComputedStyle(candidates.at(-1)) : null
  return { rotated, axis: rotated ? 'physical-y/logical-inline' : 'physical-x/logical-inline', row: { left: rowRect.left, right: rowRect.right, top: rowRect.top, bottom: rowRect.bottom, width: rowRect.width, height: rowRect.height, inlineStart: rowStart, inlineEnd: rowEnd, inlineSize: rowEnd - rowStart }, union: { inlineStart: unionStart, inlineEnd: unionEnd, inlineSize: unionEnd - unionStart, inlineCenter: (unionStart + unionEnd) / 2 }, rowInlineCenter: (rowStart + rowEnd) / 2, centerDeltaPx: Math.abs((unionStart + unionEnd) / 2 - (rowStart + rowEnd) / 2), scrollLeft: element.scrollLeft, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth, first: first && { inlineStart: start(first), inlineEnd: end(first), inlineSize: end(first) - start(first) }, last: last && { inlineStart: start(last), inlineEnd: end(last), inlineSize: end(last) - start(last) }, firstFullyVisible: Boolean(first && start(first) >= rowStart - 1 && end(first) <= rowEnd + 1), lastFullyVisible: Boolean(last && start(last) >= rowStart - 1 && end(last) <= rowEnd + 1), computed: { justifyContent: getComputedStyle(element).justifyContent, scrollBehavior: getComputedStyle(element).scrollBehavior, firstMarginInlineStart: firstStyle?.marginInlineStart ?? null, lastMarginInlineEnd: lastStyle?.marginInlineEnd ?? null } }
})
const frameGeometry = page => page.evaluate(() => {
  const rect = selector => { const value = document.querySelector(selector)?.getBoundingClientRect(); return value ? { left: value.left, top: value.top, width: value.width, height: value.height } : null }
  return { panel: rect('.prompt-choice-panel'), workspace: rect('.all-placement-workspace'), row: rect('.all-placement-row') }
})
const maxFrameDelta = (before, after) => Math.max(...Object.keys(before).flatMap(key => Object.keys(before[key] ?? {}).map(field => Math.abs((before[key]?.[field] ?? 0) - (after[key]?.[field] ?? 0)))))
const framePartDelta = (before, after, key) => Math.max(...Object.keys(before[key] ?? {}).map(field => Math.abs((before[key]?.[field] ?? 0) - (after[key]?.[field] ?? 0))))

async function runShortCase(page, viewport, mode, baseUrl) {
  const id = `short-${mode}-${viewport.id}`
  const result = { id, kind: 'short-centered-native-submit', passed: false, viewport: { width: viewport.width, height: viewport.height }, placementMode: mode, screenshot: `${phase}/${id}.png`, serverAcknowledgment: false }
  try {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.goto(`${baseUrl}?mode=${mode}&count=2`, { waitUntil: 'domcontentloaded' })
    const workspace = page.locator('.all-placement-workspace')
    await workspace.waitFor({ state: 'visible' })
    const row = workspace.locator('.all-placement-row')
    const cards = row.locator('.prompt-card-candidate')
    assert.equal(await cards.count(), 2)
    result.surface = await surfaceFacts(page)
    assertSurface(result.surface, viewport)
    result.geometryBeforeHover = normalizeGeometry(await rowGeometry(row))
    const frameBefore = await frameGeometry(page)
    const firstRect = await cards.nth(0).boundingBox()
    assert.ok(firstRect, 'first candidate must have a physical box')
    await page.mouse.move(firstRect.x + firstRect.width / 2, firstRect.y + firstRect.height / 2)
    await page.waitForTimeout(80)
    const frameAfter = await frameGeometry(page)
    result.hoverStability = { before: normalizeGeometry(frameBefore), after: normalizeGeometry(frameAfter), panelMaxDeltaPx: rounded(framePartDelta(frameBefore, frameAfter, 'panel')), allFramesMaxDeltaPx: rounded(maxFrameDelta(frameBefore, frameAfter)), tolerancePx: 1 }
    assert.ok(result.geometryBeforeHover.union.inlineSize < result.geometryBeforeHover.row.inlineSize, 'short row must fit without overflow')
    assert.ok(result.geometryBeforeHover.centerDeltaPx <= 2, `short ${mode} row is not centered: ${JSON.stringify(result.geometryBeforeHover)}`)
    assert.ok(result.hoverStability.panelMaxDeltaPx <= result.hoverStability.tolerancePx, `hover changed outer prompt geometry: ${JSON.stringify(result.hoverStability)}`)
    if (viewport.id === 'desktop-1366x768') assert.ok(result.hoverStability.allFramesMaxDeltaPx <= result.hoverStability.tolerancePx, `desktop reserved detail geometry moved: ${JSON.stringify(result.hoverStability)}`)
    result.orderBefore = await cards.locator('.prompt-card-candidate__name').allTextContents()
    result.clicks = []
    result.clicks.push({ step: 'select-first', ...(await physicalCenterClick(page, cards.nth(0), `${id} first candidate`)) })
    assert.equal(await cards.nth(0).getAttribute('aria-pressed'), 'true')
    result.clicks.push({ step: 'swap-with-second', ...(await physicalCenterClick(page, cards.nth(1), `${id} second candidate`)) })
    result.orderAfterSwap = await cards.locator('.prompt-card-candidate__name').allTextContents()
    assert.deepEqual(result.orderBefore, ['符文之力', '攻城投石车'])
    assert.deepEqual(result.orderAfterSwap, ['攻城投石车', '符文之力'])
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'native swap must not submit early')
    await page.screenshot({ path: path.join(phaseRoot, `${id}.png`) })
    const destination = mode === 'all-top-bottom' ? 'top' : 'bottom'
    const buttonName = destination === 'top' ? '全部放回顶部' : '全部放回底部'
    const confirm = page.locator('.prompt-action-footer').getByRole('button', { name: buttonName, exact: true })
    result.clicks.push({ step: `confirm-${destination}`, ...(await physicalCenterClick(page, confirm, `${id} ${buttonName}`)) })
    await page.waitForFunction(() => window.__sentCommands.length === 1)
    const commands = await page.evaluate(() => window.__sentCommands)
    result.actualCommands = commands
    result.actualCommand = stableEnvelope(commands[0])
    result.uniqueResolvePromptCount = commands.filter(item => item.type === 'gameAction' && item.command?.type === 'resolvePrompt').length
    result.expectedCommand = { type: 'gameAction', command: { topCardInstanceIds: destination === 'top' ? ['placement-1', 'placement-0'] : [], bottomCardInstanceIds: destination === 'bottom' ? ['placement-1', 'placement-0'] : [], type: 'resolvePrompt', promptId: `placement-centering-${mode}-2`, sourceInstanceId: 'placement-centering-source', sourceCardId: 'S02-0620', createdRevision: 17, controller: 0 } }
    assert.equal(commands.length, 1)
    assert.equal(result.uniqueResolvePromptCount, 1)
    assert.deepEqual(result.actualCommand, result.expectedCommand)
    result.passed = true
  } catch (error) {
    result.error = String(error?.stack || error)
    await page.screenshot({ path: path.join(phaseRoot, `${id}-failure.png`) }).catch(() => undefined)
  }
  cases.push(result)
}

async function runOverflowCase(page, viewport, mode, baseUrl) {
  const id = `overflow-${mode}-${viewport.id}`
  const result = { id, kind: 'overflow-end-reachability', passed: false, viewport: { width: viewport.width, height: viewport.height }, placementMode: mode, screenshot: `${phase}/${id}.png` }
  try {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await page.goto(`${baseUrl}?mode=${mode}&count=12`, { waitUntil: 'domcontentloaded' })
    const workspace = page.locator('.all-placement-workspace')
    await workspace.waitFor({ state: 'visible' })
    const row = workspace.locator('.all-placement-row')
    const cards = row.locator('.prompt-card-candidate')
    assert.equal(await cards.count(), 12)
    result.surface = await surfaceFacts(page)
    assertSurface(result.surface, viewport)
    await row.evaluate(element => { element.scrollLeft = 0 })
    result.start = normalizeGeometry(await rowGeometry(row))
    assert.ok(result.start.scrollWidth > result.start.clientWidth + 1, `overflow fixture must overflow: ${JSON.stringify(result.start)}`)
    assert.equal(result.start.firstFullyVisible, true, `first item must be fully reachable: ${JSON.stringify(result.start)}`)
    await row.evaluate(element => { element.scrollTo({ left: element.scrollWidth, behavior: 'instant' }) })
    await page.waitForFunction(element => Math.abs(element.scrollLeft - (element.scrollWidth - element.clientWidth)) <= 1, await row.elementHandle())
    result.end = normalizeGeometry(await rowGeometry(row))
    const expectedEnd = result.end.scrollWidth - result.end.clientWidth
    result.endScrollDeltaPx = rounded(Math.abs(result.end.scrollLeft - expectedEnd))
    assert.ok(result.endScrollDeltaPx <= 1, `row must reach its end: ${JSON.stringify(result.end)}`)
    assert.equal(result.end.lastFullyVisible, true, `last item must be fully reachable: ${JSON.stringify(result.end)}`)
    await page.screenshot({ path: path.join(phaseRoot, `${id}.png`) })
    assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'reachability must not send a command')
    result.passed = true
  } catch (error) {
    result.error = String(error?.stack || error)
    await page.screenshot({ path: path.join(phaseRoot, `${id}-failure.png`) }).catch(() => undefined)
  }
  cases.push(result)
}

let browser
try {
  await server.listen()
  const baseUrl = `http://127.0.0.1:${server.httpServer.address().port}/__placement_centering__`
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  const page = await browser.newPage({ viewport: { width: viewports[0].width, height: viewports[0].height } })
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname === '127.0.0.1') return route.continue()
    blockedExternalRequests.push({ method: route.request().method(), url: `${url.protocol}//${url.host}${url.pathname}` })
    return route.abort()
  })
  for (const viewport of viewports) {
    for (const mode of placementModes) await runShortCase(page, viewport, mode, baseUrl)
    for (const mode of placementModes) await runOverflowCase(page, viewport, mode, baseUrl)
  }
} finally {
  await browser?.close().catch(() => undefined)
  await server.close().catch(() => undefined)
}

const sourceMatchLines = (relativePath, pattern) => {
  const absolutePath = path.join(repoRoot, relativePath)
  return fs.readFileSync(absolutePath, 'utf8').split(/\r?\n/).flatMap((text, index) => pattern.test(text) ? [{ line: index + 1, text: text.trim() }] : [])
}
const placementSourceEvidence = {
  sharedFactory: {
    file: '服务端WebSocket/TwelveLegions/L12LibraryPlacementTransactions.cs',
    sha256: fileSha256(path.join(repoRoot, '服务端WebSocket/TwelveLegions/L12LibraryPlacementTransactions.cs')),
    matches: sourceMatchLines('服务端WebSocket/TwelveLegions/L12LibraryPlacementTransactions.cs', /CreateLibraryPlacementPrompt|all-top-bottom|all-bottom/),
  },
  allTopBottomEntry: {
    cardId: 'S02-05M2',
    cardName: '普罗米修斯',
    file: '服务端WebSocket/TwelveLegions/L12S2FactionEffects.cs',
    matches: sourceMatchLines('服务端WebSocket/TwelveLegions/L12S2FactionEffects.cs', /S02-05M2|BeginAllTopBottomReorder\(item, "prometheus"/),
  },
  allBottomEntry: {
    cardId: 'S02-0620',
    cardName: '符文之力',
    file: '服务端WebSocket/TwelveLegions/L12S2FactionEffects.cs',
    matches: sourceMatchLines('服务端WebSocket/TwelveLegions/L12S2FactionEffects.cs', /S02-0620|PromptRunePowerBottomOrder|s2-rune-power-bottom-order/),
  },
  sharedFrontendConsumer: {
    file: 'opcgpro-vue/src/l12/game/PromptOverlay.vue',
    matches: sourceMatchLines('opcgpro-vue/src/l12/game/PromptOverlay.vue', /placementMode.*all-top-bottom|placementMode.*all-bottom|all-placement-row/),
  },
}

const report = {
  schemaVersion: 1,
  status: cases.length === 12 && cases.every(item => item.passed) && pageErrors.length === 0 ? 'passed' : 'failed',
  generatedAt: new Date().toISOString(),
  phase,
  source: { currentHead, promptOverlaySha256: fileSha256(promptOverlayPath), scriptSha256: fileSha256(scriptPath), promptOverlayWorktreeBlob: runGit(['hash-object', promptOverlayPath]) },
  consumerContract: {
    fixture: 'real GamePage and PromptOverlay mounted in the production viewport document and landscape surface',
    placementModes, viewports, shortCardCount: 2, overflowCardCount: 12, centeredTolerancePx: 2, hoverFrameTolerancePx: 1,
    sourceLayoutMarkers: {
      sharedWorkspace: promptOverlaySource.includes("placementMode === 'all-top-bottom' || placementMode === 'all-bottom'"),
      sharedRow: promptOverlaySource.includes('class="prompt-card-strip all-placement-row"'),
      allTopBottomConfirm: promptOverlaySource.includes("placementMode === 'all-top-bottom'"),
      allBottomConfirm: promptOverlaySource.includes("placementMode === 'all-bottom'"),
    },
  },
  placementSourceEvidence,
  cases,
  pageErrors,
  blockedExternalRequests,
  validationLimits: [
    'The fixture synthesizes public placement prompts and mounts the real current GamePage and PromptOverlay; it does not replay a historical private match.',
    'Commands are sent to the fixture socket and are not acknowledged by a server. The run does not claim engine execution, deployment, or a business API round trip.',
    'The 1366x768, 844x390, and 390x844 Edge viewports are browser geometry checks, not physical-device certification.',
    'Controlled same-origin local artwork is used; exact card identities and names come from the current public catalog and prompt projection.',
    'Overflow reachability is checked by the row native scroll range and complete first/last card geometry; native pointer clicks are used for reorder and submit.',
  ],
  evidenceFiles: [`${phase}/report.json`, ...cases.map(item => item.screenshot), ...cases.filter(item => item.error).map(item => `${phase}/${item.id}-failure.png`)],
}
fs.writeFileSync(path.join(phaseRoot, 'report.json'), JSON.stringify(report, null, 2))
if (phase === 'green') {
  const focusedRedReportPath = path.join(qaRoot, 'red-focused', 'report.json')
  const redReportPath = fs.existsSync(focusedRedReportPath) ? focusedRedReportPath : path.join(qaRoot, 'red', 'report.json')
  const redReport = fs.existsSync(redReportPath) ? JSON.parse(fs.readFileSync(redReportPath, 'utf8')) : null
  const aggregate = {
    schemaVersion: 1, status: report.status, generatedAt: report.generatedAt, source: report.source,
    redBaseline: redReport ? { status: redReport.status, generatedAt: redReport.generatedAt, promptOverlaySha256: redReport.source?.promptOverlaySha256 ?? null, failedCaseIds: redReport.cases.filter(item => !item.passed).map(item => item.id), report: path.relative(qaRoot, redReportPath).replaceAll('\\', '/') } : null,
    greenVerification: { status: report.status, passedCaseIds: report.cases.filter(item => item.passed).map(item => item.id), report: 'green/report.json' },
    validationLimits: report.validationLimits,
    evidenceFiles: ['red/report.json', ...(fs.existsSync(focusedRedReportPath) ? ['red-focused/report.json'] : []), 'green/report.json', ...report.evidenceFiles.filter(item => item.endsWith('.png'))],
  }
  fs.writeFileSync(path.join(qaRoot, 'report.json'), JSON.stringify(aggregate, null, 2))
}
console.log(`Placement centering consumers (${phase}): ${cases.filter(item => item.passed).length}/${cases.length} passed; status=${report.status}`)
for (const item of cases) console.log(`${item.id}: ${item.passed ? 'passed' : 'failed'}${item.error ? ` | ${item.error.split('\n')[0]}` : ''}`)
if (report.status !== 'passed') process.exitCode = 1
