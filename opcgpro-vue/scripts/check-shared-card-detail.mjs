import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch293'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')

const sharedSource = read('src/l12/CardDetailContent.vue')
const archiveSource = read('src/l12/CardArchive.vue')
const editorSource = read('src/l12/L12DeckEditor.vue')
assert(sharedSource.includes("showCatalogOnly ? 'catalog' : 'builder'"), 'shared detail must expose its rendering context')
assert(sharedSource.includes('<template v-if="showCatalogOnly">'), 'catalog-only content must have one shared visibility boundary')
assert(sharedSource.includes('<slot name="catalog-extra"/>'), 'future catalog-only records need a guarded extension point')
assert(archiveSource.includes('<CardDetailContent :card="selectedDetailCard">')
  && archiveSource.includes('withErrataProductLabels(selected.value, selectedErrata.value)'), 'archive panel must use the shared detail with display-only product annotations')
assert(archiveSource.includes('layout="modal"') && archiveSource.includes('#image-overlay'), 'archive modal must reuse shared detail and preserve version controls')
assert(editorSource.includes(':show-catalog-only="false"'), 'deck editor must explicitly hide catalog-only details')
assert(!editorSource.includes('class="builder-card-tags"'), 'deck editor must not keep a parallel detail renderer')

fs.mkdirSync(out, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import CardArchive from '/src/l12/CardArchive.vue'
import CardDetailContent from '/src/l12/CardDetailContent.vue'
import L12DeckEditor from '/src/l12/L12DeckEditor.vue'
import '/src/style.css'
const mode=new URLSearchParams(location.search).get('mode')||'contract'
const sample={id:'TEST-01',number:'TEST-01',nameZh:'共享详情压力测试',cardType:'legion',product:'ST06',products:['ST06|彼界阵营预组'],faction:'otherworld',cost:8,troops:12000,hp:12,disasterLevel:8,trialValue:3,rarity:'L',traits:['持续','登场时'],profession:'领军',effect:'第一行效果文本\\n第二行效果文本'}
const contract={render:()=>h('main',{class:'detail-contract'},[
  h('section',{class:'archive-detail contract-detail'},[h(CardDetailContent,{card:sample},{'catalog-extra':()=>h('section',{class:'test-errata'},'勘误记录')})]),
  h('section',{class:'archive-detail contract-detail'},[h(CardDetailContent,{card:sample,showCatalogOnly:false},{'catalog-extra':()=>h('section',{class:'test-errata'},'勘误记录')})]),
])}
const rootComponent={render:()=>h(mode==='archive'?CardArchive:mode==='deck'?L12DeckEditor:contract)}
const app=createApp(rootComponent)
app.use(createRouter({history:createMemoryHistory(),routes:[{path:'/',component:rootComponent},{path:'/decks',component:rootComponent}]}))
app.mount('#app')
`

let browser
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0, strictPort: false }, plugins: [{
  name: 'shared-card-detail-fixture',
  resolveId(id) { if (id === '/__shared_card_detail__.js') return id },
  load(id) { if (id === '/__shared_card_detail__.js') return entry },
  configureServer(devServer) { devServer.middlewares.use((request, response, next) => {
    if (request.url?.match(/^\/__shared_card_detail__(\?|$)/)) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<style>html,body,#app{width:100%;height:100%;margin:0;background:#05090b}.detail-contract{display:grid;grid-template-columns:repeat(2,minmax(0,320px));gap:24px;padding:24px}.contract-detail{display:block!important;height:720px;box-sizing:border-box}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__shared_card_detail__.js"></script>')
      return
    }
    next()
  }) },
}] })

const viewports = [
  { width: 1920, height: 1080 },
  { width: 1440, height: 900 },
  { width: 1280, height: 720 },
  { width: 760, height: 900 },
  { width: 390, height: 844 },
]
const reports = []
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  await page.setViewportSize({ width: 1280, height: 800 })
  await page.goto(`http://127.0.0.1:${port}/__shared_card_detail__?mode=contract`)
  const catalogContract = page.locator('[data-card-detail-context="catalog"]')
  const builderContract = page.locator('[data-card-detail-context="builder"]')
  await catalogContract.waitFor()
  assert.equal(await catalogContract.locator('.archive-decks').count(), 1, 'catalog detail must show products')
  assert.equal(await catalogContract.locator('.test-errata').count(), 1, 'catalog detail must render catalog-only extensions')
  assert.equal(await builderContract.locator('.archive-decks').count(), 0, 'builder detail must hide products')
  assert.equal(await builderContract.locator('.test-errata').count(), 0, 'builder detail must hide catalog-only extensions')
  const contractText = await builderContract.innerText()
  for (const expected of ['军团', '领军', 'L', '费用', '8', '兵力', '12000', '血量', '12', '天灾等级', '试炼值', '第一行效果文本', '第二行效果文本']) assert(contractText.includes(expected), `builder detail missing ${expected}`)
  assert(!contractText.includes('ST06'), 'builder detail must not expose product metadata')
  assert.equal(await builderContract.locator('.l12-effect-body').evaluate(element => getComputedStyle(element).whiteSpace), 'pre-wrap', 'shared effect text must preserve line breaks')
  await page.screenshot({ path: path.join(out, 'shared-context-boundary.png') })

  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__shared_card_detail__?mode=deck`)
    const detail = page.locator('.builder-card-detail')
    await detail.locator('[data-card-detail-context="builder"]').waitFor()
    const deck = await detail.evaluate(element => ({
      display: getComputedStyle(element).display,
      overflowX: getComputedStyle(element).overflowX,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
      text: element.textContent || '',
      tags: element.querySelectorAll('.archive-tags span').length,
      values: element.querySelectorAll('dl dd').length,
      productSections: element.querySelectorAll('.archive-decks').length,
    }))
    assert.notEqual(deck.display, 'none', `builder detail hidden at ${viewport.width}x${viewport.height}`)
    assert.equal(deck.productSections, 0, `builder product section leaked at ${viewport.width}x${viewport.height}`)
    assert(!deck.text.includes('收录产品'), `builder product label leaked at ${viewport.width}x${viewport.height}`)
    assert(deck.tags >= 1, `builder card type/traits missing at ${viewport.width}x${viewport.height}`)
    assert(deck.values >= 1, `builder numeric values missing at ${viewport.width}x${viewport.height}`)
    assert(deck.scrollWidth <= deck.clientWidth + 1, `builder detail overflows horizontally at ${viewport.width}x${viewport.height}`)
    assert.equal(deck.overflowX, 'hidden', `builder detail must contain horizontal overflow at ${viewport.width}x${viewport.height}`)
    const scroll = await detail.evaluate(element => {
      element.scrollTop = element.scrollHeight
      return { clientHeight: element.clientHeight, scrollHeight: element.scrollHeight, scrollTop: element.scrollTop }
    })
    if (scroll.scrollHeight > scroll.clientHeight + 1) assert(scroll.scrollTop > 0, `builder detail must scroll internally at ${viewport.width}x${viewport.height}`)
    await detail.evaluate(element => { element.scrollTop = 0 })
    await page.screenshot({ path: path.join(out, `deck-${viewport.width}x${viewport.height}.png`) })
    reports.push({ viewport, deck, scroll })
  }

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto(`http://127.0.0.1:${port}/__shared_card_detail__?mode=deck`)
  const builderDetail = page.locator('.builder-card-detail')
  await builderDetail.waitFor()
  const angus = page.locator('.deck-card').filter({ hasText: '安格斯·麦·奥格' }).first()
  await angus.click()
  assert((await builderDetail.innerText()).includes('血量'), 'builder master detail must retain HP')
  assert(!(await builderDetail.innerText()).includes('收录产品'), 'builder master detail must not expose products')
  await angus.locator('.choose-special').click()
  await page.locator('.catalog-tabs button').filter({ hasText: '额外卡牌' }).click()
  const trial = page.locator('.deck-card').first()
  await trial.click()
  assert((await builderDetail.innerText()).includes('试炼卡'), 'builder trial detail must retain its card type')
  await page.locator('.catalog-tabs button').filter({ hasText: '主牌库' }).click()
  const mainCards = page.locator('.deck-card')
  let legionFound = false
  for (let index = 0; index < Math.min(await mainCards.count(), 24); index++) {
    await mainCards.nth(index).click()
    const text = await builderDetail.innerText()
    if (text.includes('军团') && text.includes('兵力')) { legionFound = true; break }
  }
  assert(legionFound, 'builder main pool must expose legion type and troop value through the shared detail')
  assert.equal(await builderDetail.locator('.archive-decks').count(), 0, 'builder card types must share the product boundary')
  await page.screenshot({ path: path.join(out, 'deck-type-coverage-1920x1080.png') })

  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto(`http://127.0.0.1:${port}/__shared_card_detail__?mode=archive`)
  const archivePanel = page.locator('.archive-detail [data-card-detail-context="catalog"]')
  await archivePanel.waitFor()
  assert.equal(await archivePanel.locator('.archive-decks').count(), 1, 'archive panel must retain products')
  assert((await archivePanel.locator('.archive-tags span').count()) >= 1, 'archive panel must retain metadata tags')
  const versionCard = page.locator('.archive-card:has(.archive-version-count)').first()
  await versionCard.locator('.archive-image-open').dblclick()
  const modal = page.locator('.archive-modal')
  await modal.waitFor()
  assert.equal(await modal.locator('#archive-modal-title').count(), 1, 'archive modal accessible title must survive sharing')
  assert.equal(await modal.locator('[data-card-detail-context="catalog"] .archive-decks').count(), 1, 'archive modal must retain products')
  const beforeVersion = await modal.locator('.archive-number').innerText()
  await modal.locator('.archive-modal-version-arrow.next').click()
  assert.notEqual(await modal.locator('.archive-number').innerText(), beforeVersion, 'archive modal version switch must update detail identity')
  await page.screenshot({ path: path.join(out, 'archive-modal-1920x1080.png') })
  await modal.locator('.archive-modal-close').click()

  await page.setViewportSize({ width: 760, height: 900 })
  await page.goto(`http://127.0.0.1:${port}/__shared_card_detail__?mode=archive`)
  const narrowVersionCard = page.locator('.archive-card:has(.archive-version-count)').first()
  await narrowVersionCard.locator('.archive-image-open').dblclick()
  await page.locator('.archive-modal [data-card-detail-layout="modal"]').waitFor()
  const modalBox = await page.locator('.archive-modal').boundingBox()
  assert(modalBox && modalBox.width <= 760 && modalBox.height <= 900, 'narrow archive modal must stay within viewport')
  await page.screenshot({ path: path.join(out, 'archive-modal-760x900.png') })

  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify({ reports, errors }, null, 2))
  console.log(`Shared card detail passed: ${viewports.length} deck viewports, archive panel/modal/version switch, catalog-only boundary. Screenshots: ${out}`)
} finally {
  await browser?.close()
  await server.close()
}
