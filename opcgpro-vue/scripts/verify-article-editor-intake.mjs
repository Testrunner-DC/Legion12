import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {execFileSync} from 'node:child_process'
import {createRequire} from 'node:module'
import {fileURLToPath} from 'node:url'
import {createServer} from 'vite'

// Actual product SFC regression with synthetic localhost data, no save/publish API.
const front = fileURLToPath(new URL('../', import.meta.url)), repo = path.dirname(front)
const output = process.env.L12_ARTICLE_INTAKE_OUTPUT, cache = process.env.L12_WORK_CACHE, pwsh = process.env.L12_PWSH
assert.ok(output && cache && pwsh && [output, cache, pwsh].every(path.isAbsolute))
const quote = value => "'" + value.replaceAll("'", "''") + "'"
const guarded = [output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR]
assert.ok(guarded.every(value => value && /^D:[\\/]/i.test(value)), 'Explicit D-only roots required')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. ' + quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1')) + '\n'
  + guarded.map(value => '$null=Assert-L12GeneratedPath -Path ' + quote(value) + " -Purpose 'article intake diagnostic'").join('\n')],
{stdio: 'pipe', timeout: 15000, windowsHide: true})
fs.mkdirSync(output, {recursive: true}); fs.mkdirSync(cache, {recursive: true})
const watched = ['src/l12/site/ArticleDocumentEditor.vue', 'src/l12/site/PublicDeckPicker.vue',
  'src/l12/site/ArticleContentRenderer.vue', 'src/l12/site/articleBlocks.ts', 'src/l12/DeckCover.vue',
  'scripts/verify-article-editor-intake.mjs']
const hashes = () => Object.fromEntries(watched.map(relative => [relative,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(front, relative))).digest('hex').toUpperCase()]))
const catalog = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/cards.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
const selectedCards = catalog.filter(card => card.cardType === 'legion').slice(0, 6)
const master = catalog.find(card => card.cardType === 'master')
assert.equal(selectedCards.length, 6); assert.ok(master)
const longName = '同名公开牌库' + '长名称边界'.repeat(3) + '测试甲'
assert.equal(longName.length, 24, 'Fixture name must respect the current 24-character limit')
const candidates = ['23456789ABCD', '23456789ABCF'].map((code, index) => ({
  id: 'synthetic-intake-' + index, source: 'public', name: longName, publicCode: code,
  masterId: master.id, masterName: master.nameZh, faction: master.faction,
  author: '公共牌库作者共同前缀' + (index === 0 ? '甲' : '乙'),
  publicationVersion: null, createdAt: '2026-10-20T00:00:00Z',
  updatedAt: index === 0 ? '2026-10-21T01:00:00Z' : '2026-10-22T02:00:00Z',
  counts: {main: 50, uncountedMain: 0, morale: 20, special: 3, bench: 5},
  legal: true, legalityReason: null, environment: {status: 'known', value: '2.5', reason: null},
  views: 0, likes: 0, copies: 0, viewerLiked: false, canEdit: false,
  readToken: String(index + 1).repeat(64), coverCardIds: selectedCards.slice(0, 3).map(card => card.id),
}))
assert.ok(candidates.every(candidate => candidate.author.length === 11), 'Fixture authors must respect the current 11-character limit')
const entry = [
  "import {createApp,h,ref} from 'vue'",
  "import {createRouter,createMemoryHistory} from 'vue-router'",
  "import {l12State} from '/src/l12/net'",
  "import Editor from '/src/l12/site/ArticleDocumentEditor.vue'",
  "import Renderer from '/src/l12/site/ArticleContentRenderer.vue'",
  "import '/src/style.css'",
  "l12State.endpoint=location.origin.replace(/^http/,'ws')+'/ws'",
  "const body=ref(JSON.stringify({format:'l12-blocks',version:1,blocks:[{id:'intake-text',type:'paragraph',text:'合成资讯正文',align:'left',marks:[]}]}))",
  "const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}},{path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>null}}]})",
  "const probe=ref(null),media=[{id:'intake-image',altText:'分隔图片',desktopUrl:'/__intake-image.svg',mobileUrl:'/__intake-image.svg',thumbnailUrl:'/__intake-image.svg',desktopWidth:32,desktopHeight:24,mobileWidth:32,mobileHeight:24}]",
  "await router.push('/');window.__intake={body,probe}",
  "createApp({render:()=>h('main',{style:{maxWidth:'1000px',margin:'auto',padding:'10px'}},probe.value===null?h(Editor,{modelValue:body.value,'onUpdate:modelValue':value=>body.value=value}):h(Renderer,{body:probe.value,media}))}).use(router).mount('#app')",
].join('\n')
const report = {schema: 1, status: 'running', scope: 'actual editor toolbar intake and public picker, two viewports only',
  sourceBefore: hashes(), pureChecks: [], cases: [], runtimeErrors: [], blockedRequests: [], apiReads: [],
  businessWrites: 0, externalNetworkCalls: 0, lifecycle: {browserClosed: false, serverClosed: false},
  limits: ['Synthetic public summaries, no original reported article or player deck.',
    'Current UI-path evidence, not proof of the original historical article or player deck.',
    'No backend build, publication, online requests or persistence acceptance.']}
const cardBlock = index => ({id: 'probe-card-' + index, type: 'card', cardId: selectedCards[index].id})
const textBlock = (id, text = '', type = 'paragraph') => ({id, type, text, marks: [], align: 'left'})
function pureBoundaries() {
  const source = fs.readFileSync(path.join(front, 'src/l12/site/ArticleContentRenderer.vue'), 'utf8')
  const from = source.indexOf('const renderItems = computed'), to = source.indexOf('const selectedCard =', from)
  assert.ok(from >= 0 && to > from, 'Run the actual renderer grouping consumer, not a copied algorithm')
  const ts = createRequire(import.meta.url)('typescript')
  const actual = new Function('computed', 'document', ts.transpileModule(source.slice(from, to), {
    compilerOptions: {target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None},
  }).outputText + '\nreturn renderItems')
  const check = (name, blocks, expected) => {
    const original = JSON.stringify(blocks)
    const items = actual(compute => compute(), {value: {blocks}})
    assert.deepEqual(items.map(item => item.kind === 'cards' ? 'cards:' + item.cards.length : item.block.type), expected, name)
    assert.equal(JSON.stringify(blocks), original, 'Rendering must not modify article data: ' + name)
    report.pureChecks.push(name)
  }
  for (const count of [3, 4, 6]) check('blank paragraphs bridge ' + count + ' cards',
    Array.from({length: count}, (_, index) => [cardBlock(index), textBlock('blank-' + index, ' \t\n\u00a0')]).flat(),
    ['cards:' + count, 'paragraph'])
  check('leading and trailing blank paragraphs remain', [textBlock('leading'), cardBlock(0), textBlock('trailing')],
    ['paragraph', 'cards:1', 'paragraph'])
  check('multiple blank paragraphs bridge cards', [cardBlock(0), textBlock('blank-a'), textBlock('blank-b', '\n'), cardBlock(1)], ['cards:2'])
  check('blank-only article remains', [textBlock('blank-only')], ['paragraph'])
  const separators = [
    ...['paragraph', 'h2', 'h3', 'quote', 'bulletList', 'orderedList'].map(type => textBlock('separator-' + type, '真实文字分隔', type)),
    {id: 'separator-image', type: 'image', mediaAssetId: 'intake-image', alt: '', caption: ''},
    {id: 'separator-deck', type: 'publicDeck', reference: candidates[0].publicCode},
    {id: 'separator-divider', type: 'divider'}, textBlock('zero-width-is-content', '\u200b'),
  ]
  for (const separator of separators) check('explicit separator ' + separator.id,
    [cardBlock(0), textBlock('before'), separator, textBlock('after'), cardBlock(1)],
    ['cards:1', 'paragraph', separator.type, 'paragraph', 'cards:1'])
}
const geometryOf = element => ({
  groups: [...element.querySelectorAll('.article-card-row')].map(row => ({
    cards: row.querySelectorAll('.article-card-figure').length,
    positions: [...row.querySelectorAll('.article-card-figure')].map(card => {
      const box = card.getBoundingClientRect(); return {x: box.x, y: box.y, width: box.width, height: box.height}
    }),
  })), horizontalOverflow: element.scrollWidth > element.clientWidth + 1,
})
let server, context
try {
  pureBoundaries()
  server = await createServer({root: front, configLoader: 'runner', cacheDir: path.join(cache, 'vite'),
    server: {host: '127.0.0.1', port: 0}, plugins: [{
      name: 'article-intake-local-fixture',
      resolveId(id) {if (id === '/__article_intake.js') return id},
      load(id) {if (id === '/__article_intake.js') return entry},
      configureServer(vite) {vite.middlewares.use((req, res, next) => {
        if (new URL(req.url || '/', 'http://127.0.0.1').pathname !== '/__article_intake') return next()
        res.setHeader('Content-Type', 'text/html')
        res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><body style="margin:0;background:#080d12;color:#eee"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__article_intake.js"></script>')
      })},
    }]})
  await server.listen()
  const origin = 'http://127.0.0.1:' + server.httpServer.address().port
  const {chromium} = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  context = await chromium.launchPersistentContext(path.join(cache, 'browser-profile'), {
    channel: 'msedge', headless: true, hasTouch: true, reducedMotion: 'reduce',
    viewport: {width: 1440, height: 1000}, env: {...process.env, TEMP: process.env.TEMP, TMP: process.env.TMP}})
  await context.routeWebSocket(/.*/, socket => socket.close())
  await context.route('**/*', route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) {report.blockedRequests.push({kind: 'nonlocal', host: url.host}); return route.abort()}
    if (request.method() !== 'GET') {report.businessWrites++; return route.abort()}
    if (url.pathname === '/api/deck-library/summaries') {
      report.apiReads.push({path: url.pathname, source: url.searchParams.get('source')})
      return route.fulfill({json: {items: candidates, total: 2, page: Number(url.searchParams.get('page') || '1'),
        pageSize: Number(url.searchParams.get('pageSize') || '12'), generation: 'synthetic-intake',
        catalogVersion: 'synthetic-intake', policyVersion: 1, sourceAvailability: {public: 'available', official: 'available'},
        facets: {sources: [], masters: [], factions: [], environments: [], cards: [], legal: 2, illegal: 0}}})
    }
    if (url.pathname === '/api/public-decks/' + candidates[0].publicCode + '/current') {
      report.apiReads.push({path: url.pathname, syntheticStatus: 404})
      return route.fulfill({status: 404, json: {error: 'synthetic separator deck unavailable'}})
    }
    if (url.pathname === '/__intake-image.svg') return route.fulfill({contentType: 'image/svg+xml',
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="32" height="24"><rect width="32" height="24" fill="#4c8a91"/></svg>'})
    if (url.pathname.startsWith('/api/')) {report.blockedRequests.push({kind: 'unexpected-api', path: url.pathname}); return route.abort()}
    if (url.pathname.startsWith('/card-assets/')) {
      const assetRoot = path.resolve('D:/L12-assets/published/current')
      const asset = path.resolve(assetRoot, decodeURIComponent(url.pathname.slice('/card-assets/'.length)))
      if (asset.startsWith(assetRoot + path.sep) && fs.existsSync(asset)) return route.fulfill({path: asset})
      return route.abort()
    }
    return route.continue()
  })
  for (const [width, height] of [[1440, 1000], [390, 844]]) {
    const page = await context.newPage()
    page.setDefaultTimeout(12000)
    page.on('pageerror', error => report.runtimeErrors.push(error.message))
    await page.setViewportSize({width, height})
    await page.goto(origin + '/__article_intake', {waitUntil: 'domcontentloaded'})
    await page.locator('.editor-canvas p').waitFor()
    const insertionBodies = [], cardCases = []
    const currentCase = {viewport: {width, height}, actualToolbarInsertions: 0, cardCases}
    report.cases.push(currentCase)
    for (const [index, card] of selectedCards.entries()) {
      await page.locator('.editor-canvas p').last().click()
      await page.getByRole('button', {name: '＋ 单卡', exact: true}).click()
      await page.getByPlaceholder('搜索卡名、编号或效果文字').fill(card.id)
      await page.locator('.single-card-result-card').filter({hasText: card.nameZh})
        .getByRole('button', {name: '选择', exact: true}).click()
      await page.getByRole('button', {name: '插入单卡', exact: true}).click()
      await page.waitForFunction(count => JSON.parse(window.__intake.body.value).blocks.filter(block => block.type === 'card').length === count, index + 1)
      insertionBodies.push(await page.evaluate(() => JSON.parse(window.__intake.body.value)))
      currentCase.actualToolbarInsertions = index + 1
      if (![3, 4, 6].includes(index + 1)) continue
      const count = index + 1, savedBody = await page.evaluate(() => window.__intake.body.value)
      fs.writeFileSync(path.join(output, 'saved-body-' + count + '-' + width + 'x' + height + '.json'), savedBody, {flag: 'wx'})
      await page.getByRole('button', {name: '预览文章', exact: true}).click()
      await page.locator('.article-card-figure').nth(count - 1).waitFor()
      const geometry = await page.locator('.article-content').evaluate(geometryOf)
      const rowCounts = []
      for (const position of geometry.groups.flatMap(group => group.positions)) {
        const row = rowCounts.find(row => Math.abs(row.y - position.y) < 1)
        if (row) row.count++; else rowCounts.push({y: position.y, count: 1})
      }
      cardCases.push({count, geometry, rowCounts})
      assert.deepEqual(geometry.groups.map(group => group.cards), [count], 'Whitespace must not split the toolbar card group')
      assert.deepEqual(rowCounts.map(row => row.count), count === 3 ? [3] : count === 4 ? [3, 1] : [3, 3])
      assert.equal(geometry.horizontalOverflow, false)
      await page.screenshot({path: path.join(output, 'card-preview-' + count + '-' + width + 'x' + height + '.png'), fullPage: true})
      await page.getByRole('button', {name: '返回编辑', exact: true}).click()
    }
    currentCase.insertionBlockTypes = insertionBodies.map(body => body.blocks.map(block => block.type === 'paragraph'
        ? 'paragraph:' + (block.text ? 'text' : 'empty') : block.type))
    currentCase.cardGeometry = cardCases[0].geometry
    await page.getByRole('button', {name: '＋ 公共牌库', exact: true}).click()
    const picker = page.getByRole('dialog', {name: '插入公共牌库', exact: true})
    await picker.locator('.public-deck-picker-option').nth(1).waitFor()
    const candidateUi = await picker.locator('.public-deck-picker-option').evaluateAll((elements, expected) => elements.map((element, index) => {
      const full = expected[index]
      const textInfo = selector => {
        const node = element.querySelector(selector)
        return {text: node?.textContent || '', clipped: Boolean(node && node.scrollWidth > node.clientWidth + 1),
          title: node?.getAttribute('title') || ''}
      }
      return {name: textInfo('.public-deck-picker-copy b'), author: textInfo('.public-deck-picker-copy span'),
        accessibleLabel: element.getAttribute('aria-label'), text: element.innerText,
        publicCodePresent: element.innerText.includes(full.publicCode),
        copyOverflow: element.querySelector('.public-deck-picker-copy').scrollWidth > element.querySelector('.public-deck-picker-copy').clientWidth + 1,
        authorInAccessibleLabel: element.getAttribute('aria-label').includes(full.author),
        codeInAccessibleLabel: element.getAttribute('aria-label').includes(full.publicCode)}
    }), candidates.map(({name, author, publicCode}) => ({name, author, publicCode})))
    await page.screenshot({path: path.join(output, 'deck-candidates-' + width + 'x' + height + '.png'), fullPage: true})
    currentCase.candidateUi = candidateUi
    assert.equal(new Set(candidateUi.map(item => item.accessibleLabel)).size, 2)
    for (const [index, item] of candidateUi.entries()) {
      assert.equal(item.name.text, candidates[index].name)
      assert.ok(item.author.text.includes(candidates[index].author))
      assert.ok(!item.name.clipped && !item.author.clipped && !item.copyOverflow)
      assert.ok(item.publicCodePresent && item.authorInAccessibleLabel && item.codeInAccessibleLabel)
    }
    const pickerGeometry = await picker.evaluate(element => ({scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
      right: element.getBoundingClientRect().right, viewportWidth: innerWidth}))
    currentCase.pickerGeometry = pickerGeometry
    assert.ok(pickerGeometry.scrollWidth <= pickerGeometry.clientWidth + 1 && pickerGeometry.right <= width + 1)
    await picker.locator('.public-deck-picker-option').nth(1).focus()
    await page.keyboard.press('Enter')
    await page.waitForFunction(reference => JSON.parse(window.__intake.body.value).blocks.some(block => block.type === 'publicDeck' && block.reference === reference), candidates[1].publicCode)
    currentCase.keyboardSelectedReference = candidates[1].publicCode
    const boundaryBlocks = [cardBlock(0), textBlock('bridge', ' \n\u00a0'), cardBlock(1),
      textBlock('visible-text', '真实文字分隔'), cardBlock(2),
      {id: 'visible-image', type: 'image', mediaAssetId: 'intake-image', alt: '分隔图片', caption: ''}, cardBlock(3),
      {id: 'visible-deck', type: 'publicDeck', reference: candidates[0].publicCode}, cardBlock(4),
      {id: 'visible-divider', type: 'divider'}, cardBlock(5)]
    const boundaryBody = JSON.stringify({format: 'l12-blocks', version: 1, blocks: boundaryBlocks})
    await page.evaluate(body => {window.__intake.probe.value = body}, boundaryBody)
    await page.locator('.article-card-figure').nth(5).waitFor()
    await page.waitForFunction(() => document.querySelector('.image-zoom-trigger img')?.naturalWidth > 0)
    const boundaryGeometry = await page.locator('.article-content').evaluate(geometryOf)
    assert.deepEqual(boundaryGeometry.groups.map(group => group.cards), [2, 1, 1, 1, 1])
    assert.equal(boundaryGeometry.horizontalOverflow, false)
    assert.equal(await page.locator('.article-content p').innerText(), '真实文字分隔')
    assert.equal(await page.locator('.image-zoom-trigger').count(), 1)
    assert.equal(await page.locator('.article-deck-embed').count(), 1)
    assert.equal(await page.locator('.article-content hr').count(), 1)
    assert.equal(await page.evaluate(() => window.__intake.probe.value), boundaryBody)
    currentCase.explicitSeparators = {geometry: boundaryGeometry, articleBodyUnchanged: true, text: 1, image: 1, publicDeck: 1, divider: 1}
    await page.screenshot({path: path.join(output, 'explicit-separators-' + width + 'x' + height + '.png'), fullPage: true})
    await page.close()
  }
  assert.deepEqual(report.runtimeErrors, [])
  assert.equal(report.businessWrites, 0)
  assert.equal(report.blockedRequests.filter(item => item.kind === 'unexpected-api').length, 0)
  report.status = 'GREEN'
} catch (error) {
  report.status = 'HARNESS_FAILED'; report.failure = {name: error.name, message: error.message, stack: error.stack}
  process.exitCode = 1
} finally {
  try {if (context) {await context.close(); report.lifecycle.browserClosed = true}}
  catch (error) {report.lifecycle.browserCloseError = error.message; process.exitCode = 1}
  try {if (server) {await server.close(); report.lifecycle.serverClosed = true}}
  catch (error) {report.lifecycle.serverCloseError = error.message; process.exitCode = 1}
  report.sourceAfter = hashes()
  report.sourceStable = JSON.stringify(report.sourceBefore) === JSON.stringify(report.sourceAfter)
  if (!report.sourceStable) {report.status = 'SOURCE_CHANGED'; process.exitCode = 1}
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2), {flag: 'wx'})
}
console.log(JSON.stringify({status: report.status, cases: report.cases.length, output, lifecycle: report.lifecycle,
  sourceStable: report.sourceStable, failure: report.failure?.message}))
