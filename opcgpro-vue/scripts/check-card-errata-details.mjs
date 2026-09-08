import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const CARD_ID = 'S02-06M2'
const CARD_NAME = '安格斯·麦·奥格'
const PRODUCT = 'ST06|彼界阵营预组'
const PRODUCT_LABEL = `${PRODUCT}（勘误收录）`
const OLD_EFFECT = '规则上，可完成的试炼数量增加1张。\n每完成1次试炼，可获得1符文。\n回合1次 当我方成功发动战术效果时，试炼+1。'
const NEW_EFFECT = '规则上，可完成的试炼数量增加1张。\n我方 回合1次 推进试炼进度时，可获得1符文。\n回合1次 当我方成功发动战术效果时，试炼+1。'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repoRoot = path.resolve(root, '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch294-errata'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')

const errataSource = read('src/l12/data/cardErrata.ts')
const archiveSource = read('src/l12/CardArchive.vue')
const editorSource = read('src/l12/L12DeckEditor.vue')
const lookup = JSON.parse(read('public/data/l12/cards.lookup.json'))
const serverCards = JSON.parse(fs.readFileSync(path.join(repoRoot, '服务端WebSocket/TwelveLegions/Data/cards.s2.json'), 'utf8'))
const products = JSON.parse(fs.readFileSync(path.join(repoRoot, '服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'), 'utf8'))
const productEntry = products.cards.find(card => card.cardId === CARD_ID)
const lookupCard = lookup.find(card => card.cardNo === CARD_ID)
const serverCard = serverCards.find(card => card.id === CARD_ID)
assert(productEntry, `${CARD_ID} product entry missing`)
assert.deepEqual(productEntry.products, ['第2季|伟大试炼', '第2季|典藏版', PRODUCT], 'Angus must retain both season products and add exactly one ST06 product')
assert.equal(productEntry.products.filter(product => product === PRODUCT).length, 1, 'ST06 product must not be duplicated')
assert.equal(serverCard?.effect, NEW_EFFECT, 'server catalog must contain the approved current Angus effect')
assert.equal(lookupCard?.effectText, NEW_EFFECT, 'frontend catalog must contain the approved current Angus effect')
assert.equal(lookupCard?.effectText, serverCard?.effect, 'frontend and server Angus effects must remain identical')
assert(errataSource.includes(`cardId: '${CARD_ID}'`) && errataSource.includes(`sourceProduct: '${PRODUCT}'`), 'errata metadata must target the exact card and product')
assert(errataSource.includes("previousEffect: '规则上，可完成的试炼数量增加1张。\\n每完成1次试炼，可获得1符文。\\n回合1次 当我方成功发动战术效果时，试炼+1。'"), 'errata metadata must preserve the exact previous effect')
assert.equal((errataSource.match(/^\s+id: '[^']+'/gm) ?? []).length, 1, 'errata module must contain exactly one record')
assert.equal((archiveSource.match(/#catalog-extra/g) ?? []).length, 2, 'archive panel and modal must both use the guarded catalog slot')
assert(archiveSource.includes('withErrataProductLabels') && archiveSource.includes('（勘误收录）'), 'archive must annotate only errata products for display')
assert(!editorSource.includes('cardErrata') && !editorSource.includes('勘误记录'), 'deck editor must not import or render errata metadata')

fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import CardArchive from '/src/l12/CardArchive.vue'
import L12DeckEditor from '/src/l12/L12DeckEditor.vue'
import '/src/style.css'
const mode=new URLSearchParams(location.search).get('mode')||'archive'
const rootComponent={render:()=>h(mode==='deck'?L12DeckEditor:CardArchive)}
const app=createApp(rootComponent)
app.use(createRouter({history:createMemoryHistory(),routes:[{path:'/',component:rootComponent},{path:'/decks',component:rootComponent}]}))
app.mount('#app')
`

let browser
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0, strictPort: false }, plugins: [{
  name: 'card-errata-detail-fixture',
  resolveId(id) { if (id === '/__card_errata_detail__.js') return id },
  load(id) { if (id === '/__card_errata_detail__.js') return entry },
  configureServer(devServer) { devServer.middlewares.use((request, response, next) => {
    if (request.url?.match(/^\/__card_errata_detail__(\?|$)/)) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<style>html,body,#app{width:100%;height:100%;margin:0;background:#05090b}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__card_errata_detail__.js"></script>')
      return
    }
    next()
  }) },
}] })

function normalizedText(text) {
  return text.replace(/\r\n/g, '\n').trim()
}

async function chooseAngus(page) {
  const productSelect = page.locator('.archive-toolbar label').filter({ hasText: '收录产品' }).locator('select')
  await productSelect.selectOption(PRODUCT)
  const card = page.locator('.archive-card').filter({ hasText: CARD_NAME }).first()
  await card.waitFor()
  await card.click()
  return card
}

async function verifyDetail(scope) {
  const current = scope.locator('.card-detail-copy > .archive-effect:not([data-card-errata]) .l12-effect-body')
  const productLines = await scope.locator('.card-detail-copy > .archive-decks p').allInnerTexts()
  const errata = scope.locator('[data-card-errata]')
  assert.equal(normalizedText(await current.innerText()), NEW_EFFECT, 'current effect must use the approved new text')
  assert.deepEqual(productLines, ['第2季|伟大试炼', '第2季|典藏版', PRODUCT_LABEL], 'detail must preserve two season products and annotate the ST06 errata inclusion once')
  assert.equal(await errata.count(), 1, 'detail must contain one errata section')
  assert.equal(normalizedText(await errata.locator('.l12-effect-body').innerText()), OLD_EFFECT, 'errata section must preserve the exact old text')
  assert(await scope.evaluate(element => {
    const products = element.querySelector('.card-detail-copy > .archive-decks')
    const errata = element.querySelector('[data-card-errata]')
    return Boolean(products && errata && (products.compareDocumentPosition(errata) & Node.DOCUMENT_POSITION_FOLLOWING))
  }), 'errata section must follow the products section')
}

async function verifyNarrowModalScroll(page, modal, screenshotName) {
  const detail = modal.locator('.archive-modal-detail')
  await detail.evaluate(element => { element.scrollTop = element.scrollHeight })
  await page.waitForTimeout(50)
  const [modalBox, detailBox, errataBox, closeBox] = await Promise.all([
    modal.boundingBox(),
    detail.boundingBox(),
    modal.locator('[data-card-errata] .l12-effect-body').boundingBox(),
    modal.locator('.archive-modal-close').boundingBox(),
  ])
  assert(modalBox && detailBox && errataBox && closeBox, 'narrow modal elements must remain measurable after scrolling')
  assert(errataBox.y >= detailBox.y - 1 && errataBox.y + errataBox.height <= detailBox.y + detailBox.height + 1,
    'the complete old effect must be reachable inside the modal detail scroller')
  assert(closeBox.y >= modalBox.y && closeBox.y + closeBox.height <= modalBox.y + modalBox.height,
    'the close button must remain inside the modal after scrolling the errata text')
  await page.screenshot({ path: path.join(out, screenshotName) })
  await modal.locator('.archive-modal-close').click()
  assert.equal(await page.locator('.archive-modal').count(), 0, 'the narrow modal close button must remain usable after scrolling')
}

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } })
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  await page.goto(`http://127.0.0.1:${port}/__card_errata_detail__?mode=archive`)
  const card = await chooseAngus(page)
  const panel = page.locator('.archive-detail')
  await panel.locator('[data-card-errata]').waitFor()
  await verifyDetail(panel)
  await page.screenshot({ path: path.join(out, 'angus-archive-panel-1920x1080.png') })

  await card.locator('.archive-image-open').dblclick()
  const modal = page.locator('.archive-modal')
  await modal.waitFor()
  await verifyDetail(modal)
  await page.screenshot({ path: path.join(out, 'angus-archive-modal-1920x1080.png') })
  await modal.locator('.archive-modal-close').click()

  await page.setViewportSize({ width: 760, height: 900 })
  await page.goto(`http://127.0.0.1:${port}/__card_errata_detail__?mode=archive`)
  const narrowCard = await chooseAngus(page)
  await narrowCard.locator('.archive-image-open').dblclick()
  const narrowModal = page.locator('.archive-modal')
  await narrowModal.waitFor()
  await verifyDetail(narrowModal)
  const narrowBox = await narrowModal.boundingBox()
  assert(narrowBox && narrowBox.width <= 760 && narrowBox.height <= 900, 'narrow errata modal must remain inside the viewport')
  await page.screenshot({ path: path.join(out, 'angus-archive-modal-760x900.png') })
  await verifyNarrowModalScroll(page, narrowModal, 'angus-archive-modal-760x900-bottom.png')

  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(`http://127.0.0.1:${port}/__card_errata_detail__?mode=archive`)
  const phoneCard = await chooseAngus(page)
  await phoneCard.locator('.archive-image-open').dblclick()
  const phoneModal = page.locator('.archive-modal')
  await phoneModal.waitFor()
  await verifyDetail(phoneModal)
  const phoneBox = await phoneModal.boundingBox()
  assert(phoneBox && phoneBox.width <= 390 && phoneBox.height <= 844, 'phone errata modal must remain inside the viewport')
  await verifyNarrowModalScroll(page, phoneModal, 'angus-archive-modal-390x844-bottom.png')

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto(`http://127.0.0.1:${port}/__card_errata_detail__?mode=deck`)
  const editorCard = page.locator('.deck-card').filter({ hasText: CARD_NAME }).first()
  await editorCard.click()
  const editorDetail = page.locator('.builder-card-detail')
  const editorText = normalizedText(await editorDetail.innerText())
  assert(editorText.includes(NEW_EFFECT), 'deck editor must show the approved current effect')
  assert(!editorText.includes('每完成1次试炼'), 'deck editor must not show the old effect')
  for (const forbidden of ['收录产品', '勘误记录', '勘误收录', PRODUCT]) assert(!editorText.includes(forbidden), `deck editor leaked ${forbidden}`)
  await page.screenshot({ path: path.join(out, 'angus-deck-editor-1920x1080.png') })

  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ cardId: CARD_ID, products: productEntry.products, newEffect: NEW_EFFECT, oldEffect: OLD_EFFECT, errors }, null, 2))
  console.log(`Angus errata detail passed: real multi-product filter, panel/modal old-text record, editor isolation. Screenshots: ${out}`)
} finally {
  await browser?.close()
  await server.close()
}
