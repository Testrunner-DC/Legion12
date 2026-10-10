import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/rule-center-experience')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = String.raw`
import {createApp,h} from 'vue'
import {createWebHistory,createRouter} from 'vue-router'
import RuleCenterPage from '/src/l12/site/RuleCenterPage.vue'
import AdminRuleRulingsPanel from '/src/l12/site/AdminRuleRulingsPanel.vue'
import {createRuleCenterDraft,createRulingsDraft} from '/src/l12/data/ruleCenterData.ts'
import {adminApi,authState,platformState} from '/src/l12/platform.ts'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'

const center=createRuleCenterDraft()
for(const collection of ['coreBlocks','quickStart','terms','tournament','versions'])
  for(const item of center[collection])item.status='published'
const ruleImage='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="960" height="360" viewBox="0 0 960 360"><rect width="960" height="360" fill="#0f2730"/><path d="M80 280L300 90l160 120 150-120 270 190" fill="none" stroke="#d8bc69" stroke-width="24"/></svg>')
center.coreBlocks.unshift(
 {id:'core-fixture-001',page:'',topic:'对局准备',chapter:'同章多节验收',text:'第一子板块',status:'published',mediaAssetId:'rule-media-001',image:{id:'rule-media-001',altText:'规则示意图',desktopUrl:ruleImage,mobileUrl:ruleImage,thumbnailUrl:ruleImage,desktopWidth:960,desktopHeight:360,mobileWidth:960,mobileHeight:360}},
 {id:'core-fixture-002',page:'',topic:'回合流程',chapter:'同章多节验收',text:'第二子板块',status:'published'},
 {id:'core-fixture-003',page:'',topic:'对局准备',chapter:'目录筛选验收',text:'SECOND UNIQUE NEEDLE',status:'published'},
)
const rulingSeeds=createRulingsDraft()
const generalSeed=rulingSeeds.find(item=>item.scope==='general')
const generalOrderingFixtures=[
 {...generalSeed,id:'GENERAL-SORT-TAG',question:'较新的通用裁定',recordedAt:'2026-09-30',tags:['排序共同词'],status:'published'},
 {...generalSeed,id:'GENERAL-SORT-QUESTION',question:'排序共同词出现在较旧问题中',recordedAt:'2026-01-01',tags:[],status:'published'},
]
const rulings=[...rulingSeeds.map((item,index)=>({...item,status:'published',supersedes:index===0?['OLD-RULING']:item.supersedes})),...generalOrderingFixtures]
const privateDraft={...rulings[0],id:'PRIVATE-DRAFT',question:'PRIVATE-DRAFT',answer:'PRIVATE-DRAFT',status:'pending'}
const operations={version:7,season:{name:'验收赛季',status:'active'},defaultRoomConfig:{matchModeId:'standard',disasterMode:'all'},cardRestrictions:[]}
const originalFetch=window.fetch.bind(window)
window.__ruleFetches=[]
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 window.__ruleFetches.push(url)
 if(url.includes('/api/content?'))return new Response(JSON.stringify({values:{'rules.notice':'规则资料验收公告','rules.center':JSON.stringify(center),'rules.rulings':JSON.stringify({schemaVersion:2,entries:[...rulings,privateDraft]})},observedAt:new Date().toISOString(),nextRuleTransitionAt:null}),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/operations/effective-policy'))return new Response(JSON.stringify(operations),{status:200,headers:{'Content-Type':'application/json'}})
 return originalFetch(input,init)
}

const liveRuling={...rulings[2],status:'published'}
const changedRuling={...rulings[3],status:'published',question:rulings[3].question+'（公开版）'}
const scheduledRuling={...rulings[4],status:'published',effectiveAt:'2099-01-01T00:00:00+08:00'}
const adminDrafts=[
  {...rulings[1],id:'ADMIN-DRAFT',question:'待审核裁定',status:'pending'},
  liveRuling,
  {...changedRuling,question:changedRuling.question+'（草稿修订）'},
  scheduledRuling,
  {...rulings[5],id:'ADMIN-OLD',question:'已替代裁定',status:'superseded'},
]
const adminPublished=[liveRuling,changedRuling,scheduledRuling,{...adminDrafts[4],status:'published'}]
const centerDraft={schemaVersion:2,coreBlocks:[
 {id:'center-draft',page:'',topic:'对局准备',chapter:'待审核资料',text:'待审核正文',status:'pending',mediaAssetId:'rule-media-001'},
 {id:'center-draft-2',page:'',topic:'回合流程',chapter:'待审核资料',text:'同章第二子板块',status:'pending'},
 {id:'center-live',page:'',topic:'回合流程',chapter:'已发布资料',text:'已发布正文',status:'published'},
],quickStart:[],terms:[],tournament:[],versions:[]}
const centerPublished={schemaVersion:2,coreBlocks:[
 {id:'center-live',page:'',topic:'回合流程',chapter:'已发布资料',text:'已发布正文',status:'published'},
],quickStart:[],terms:[],tournament:[],versions:[]}
const contentView=(key)=>key==='rules.rulings'
 ? {key,draftValue:JSON.stringify({schemaVersion:2,entries:adminDrafts}),publishedValue:JSON.stringify({schemaVersion:2,entries:adminPublished}),version:4}
 : {key,draftValue:JSON.stringify(centerDraft),publishedValue:JSON.stringify(centerPublished),version:6}
adminApi.getContent=async key=>contentView(key)
adminApi.contentBatches=async()=>[]
adminApi.siteMedia=async()=>[{id:'rule-media-001',kind:'rule',altText:'规则示意图',contentHash:'rule-media-content-hash',thumbnailUrl:ruleImage,desktopUrl:ruleImage,mobileUrl:ruleImage,desktopWidth:960,desktopHeight:360,mobileWidth:960,mobileHeight:360}]
window.__savedDrafts=[]
adminApi.saveContentDraft=async(key,value)=>{window.__savedDrafts.push({key,value});return {...contentView(key),draftValue:value,version:8}}
adminApi.previewContent=async()=>({items:[]})
adminApi.publishRuleItem=async(key)=>contentView(key)
platformState.account={id:'qa-admin',username:'验收管理员',role:'admin',createdAt:'2026-01-01',publicHistory:false,permissions:['admin.content.draft','admin.content.publish']}
authState.initialized=true
authState.verified=true

const mode=new URLSearchParams(location.search).get('fixture')||'player'
const component={render:()=>h('div',{class:'site-shell'},h('main',{class:'site-content'},mode==='admin'?h(AdminRuleRulingsPanel):h(RuleCenterPage)))}
const router=createRouter({history:createWebHistory(),routes:[{path:'/:pathMatch(.*)*',component:{render:()=>null}}]})
createApp(component).use(router).mount('#app')
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-rule-center-experience'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'rule-center-experience-fixture',
    resolveId(id) { if (id === '/__rule_center__.js') return id },
    load(id) { if (id === '/__rule_center__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__rule_center__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#fff}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__rule_center__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 3440, height: 1440, input: 'mouse' },
  { width: 1920, height: 1080, input: 'mouse' },
  { width: 1366, height: 768, input: 'mouse' },
  { width: 1024, height: 768, input: 'keyboard' },
  { width: 430, height: 932, input: 'touch' },
  { width: 390, height: 844, input: 'touch' },
  { width: 844, height: 390, input: 'touch' },
  { width: 568, height: 320, input: 'touch' },
]
const adminViewports = [
  { width: 3440, height: 1440 },
  { width: 1366, height: 900 },
  { width: 1024, height: 768 },
  { width: 430, height: 932 },
  { width: 390, height: 844 },
  { width: 844, height: 390 },
]
const expectedProductOrder = ['第2季|典藏版', '第2季|伟大试炼', '第1季|典藏版', '第1季|天御·再临', '第1季|天御']
const suffix = viewport => `${viewport.width}x${viewport.height}`
const expectedCardRulingOrder = [
  'RULING-20260922-FENIAN-REPEAT',
  'RULING-20260922-SIWA-KABA',
  'RULING-20260917-LIVE-COST',
  'RULING-20260902-FAITH-ZEALOT',
  'RULING-20260902-HELEN',
  'RULING-20260902-HOREMHEB',
  'RULING-20260902-LI-JING',
  'RULING-20260902-PTOLEMY',
  'RULING-20260902-THUNDER',
]
const visibleRulingIds = page => page.locator('.faq-list article').evaluateAll(nodes =>
  nodes.map(node => node.id.replace('rule-entry-', '')))

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  const base = `http://127.0.0.1:${port}/__rule_center__`
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const report = []

  for (const viewport of viewports) {
    const context = await browser.newContext({ viewport, hasTouch: viewport.input === 'touch' })
    const page = await context.newPage()
    const errors = []
    const apiRequests = []
    const catalogRequests = []
    const imageRequests = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('request', request => {
      const url = request.url()
      if (url.includes('/api/content?') || url.includes('/api/operations/effective-policy')) apiRequests.push(url)
      if (/\/data\/l12\/cards\.(s1|lookup|st)\.json/.test(url)) catalogRequests.push(url)
      if (request.resourceType() === 'image') imageRequests.push(url)
    })
    await page.goto(base)
    await page.locator('.rules-home-lead').waitFor()
    assert.equal(await page.getByText('PRIVATE-DRAFT').count(), 0, `pending content leaked at ${suffix(viewport)}`)
    const ruleFetches = await page.evaluate(() => window.__ruleFetches.filter(url => url.includes('/api/content?') || url.includes('/api/operations/effective-policy')))
    assert.equal(ruleFetches.length, 2, `initial rule request budget changed at ${suffix(viewport)}`)
    assert.equal(catalogRequests.length, 0, `home loaded card catalog at ${suffix(viewport)}`)
    assert.equal(imageRequests.length, 0, `home loaded card images at ${suffix(viewport)}`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false, `home overflows at ${suffix(viewport)}`)
    assert((await page.locator('.rules-page').boundingBox()).width <= 1681, `home is unbounded at ${suffix(viewport)}`)
    assert.equal(await page.locator('.material-grid button').count(), 6, `material entry count changed at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `home-${suffix(viewport)}.png`), fullPage: true })

    const coreEntry = page.getByRole('button', { name: /核心规则/ })
    if (viewport.input === 'touch') await coreEntry.tap()
    else if (viewport.input === 'keyboard') { await coreEntry.focus(); await page.keyboard.press('Enter') }
    else await coreEntry.click()
    const chapterNav = page.getByRole('navigation', { name: '规则手册章节目录' })
    await chapterNav.waitFor()
    assert.equal(await chapterNav.getByRole('button').filter({ hasText: '同章多节验收' }).count(), 1,
      `same-chapter blocks did not collapse into one ToC entry at ${suffix(viewport)}`)
    assert.match(await chapterNav.getByRole('button').filter({ hasText: '同章多节验收' }).innerText(), /2\s*节/,
      `same-chapter ToC count changed at ${suffix(viewport)}`)
    await chapterNav.getByRole('button').filter({ hasText: '目录筛选验收' }).click()
    await page.waitForFunction(() => new URL(location.href).searchParams.get('entry') === 'core-fixture-003')
    assert.equal(await page.locator('#rule-entry-core-fixture-003').count(), 1,
      `chapter jump did not target its first block at ${suffix(viewport)}`)
    await page.locator('.rule-tools input').fill('SECOND UNIQUE NEEDLE')
    assert.equal(await chapterNav.getByRole('button').count(), 1,
      `filtered ToC did not follow current results at ${suffix(viewport)}`)
    assert.equal(await chapterNav.getByText('目录筛选验收', { exact: true }).count(), 1,
      `filtered ToC kept the wrong chapter at ${suffix(viewport)}`)
    await page.locator('.rule-tools input').fill('')
    assert.equal(await page.locator('.rule-block-image img[alt="规则示意图"]').count(), 1,
      `core rule media was not rendered at ${suffix(viewport)}`)
    const ruleImageBox = await page.locator('.rule-block-image img[alt="规则示意图"]').boundingBox()
    const ruleArticleBox = await page.locator('#rule-entry-core-fixture-001').boundingBox()
    assert(ruleImageBox && Math.abs(ruleImageBox.width / ruleImageBox.height - 8 / 3) < 0.05,
      `core rule media aspect ratio changed at ${suffix(viewport)}`)
    assert(ruleImageBox && ruleArticleBox && ruleImageBox.width <= ruleArticleBox.width,
      `core rule media escaped its content block at ${suffix(viewport)}`)
    assert(ruleImageBox && ruleImageBox.width <= viewport.width - 20,
      `core rule media exceeded the viewport at ${suffix(viewport)} (${ruleImageBox?.width}px image, ${ruleArticleBox?.width}px article)`)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
      `core rules overflow at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `core-rules-${suffix(viewport)}.png`), fullPage: true })
    await page.getByRole('button', { name: '规则资料首页' }).click()
    await page.locator('.rules-home-lead').waitFor()

    if (viewport.input === 'keyboard') {
      await page.locator('.material-grid button').first().focus()
      await page.keyboard.press('Enter')
      await page.locator('.rule-tools').waitFor()
      await page.getByRole('button', { name: '规则资料首页' }).focus()
      await page.keyboard.press('Enter')
      await page.locator('.rules-home-lead').waitFor()
      await page.getByRole('button', { name: /常见问题/ }).focus()
      await page.keyboard.press('Enter')
      await page.locator('.faq-search-panel').waitFor()
    } else if (viewport.input === 'touch') {
      await page.getByRole('button', { name: /常见问题/ }).tap()
      await page.locator('.faq-search-panel').waitFor()
      if (viewport.width <= 700) {
        await page.locator('.faq-search-row .mobile-filter-trigger').tap()
        const dialog = page.getByRole('dialog', { name: '常见问题筛选' })
        await dialog.waitFor()
        assert.equal(await dialog.evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `filter sheet overflows at ${suffix(viewport)}`)
        await page.keyboard.press('Escape')
        assert.equal(await dialog.count(), 0, `filter sheet did not close at ${suffix(viewport)}`)
      }
    } else {
      await page.getByRole('button', { name: /常见问题/ }).click()
      await page.locator('.faq-search-panel').waitFor()
    }

    const generalBlankIds = await visibleRulingIds(page)
    assert(generalBlankIds.indexOf('GENERAL-SORT-TAG') < generalBlankIds.indexOf('GENERAL-SORT-QUESTION'),
      `general blank-query date ordering changed at ${suffix(viewport)}`)
    await page.locator('.faq-search-row input').fill('排序共同词')
    assert.deepEqual(await visibleRulingIds(page), ['GENERAL-SORT-QUESTION', 'GENERAL-SORT-TAG'],
      `general score ordering changed at ${suffix(viewport)}`)
    await page.locator('.faq-search-row input').fill('')

    const questions = page.locator('.faq-question')
    if (await questions.count()) {
      if (viewport.input === 'keyboard') {
        await questions.first().focus()
        await page.keyboard.press('Enter')
      } else await questions.first().click()
      assert.equal(await questions.first().getAttribute('aria-expanded'), 'true', `question did not expand at ${suffix(viewport)}`)
      await page.getByRole('button', { name: '全部收起' }).click()
      assert.equal(await questions.first().getAttribute('aria-expanded'), 'false', `collapse all failed at ${suffix(viewport)}`)
    }
    const cardMode = page.getByRole('button', { name: '单卡问答' })
    if (viewport.input === 'keyboard') { await cardMode.focus(); await page.keyboard.press('Enter') }
    else if (viewport.input === 'touch') await cardMode.tap()
    else await cardMode.click()
    await page.locator('.faq-product-section').waitFor()
    await page.waitForFunction(() => performance.getEntriesByType('resource').some(entry => entry.name.includes('/data/l12/cards.s1.json')))
    assert(catalogRequests.length >= 3, `card workspace did not load title metadata on demand at ${suffix(viewport)}`)
    assert.equal(imageRequests.length, 0, `collapsed card Q&A eagerly loaded card images at ${suffix(viewport)}`)
    await page.getByText('S02-06S5·芬尼亚传奇·裁定', { exact: true }).waitFor()
    assert.deepEqual(await visibleRulingIds(page), expectedCardRulingOrder,
      `card rulings are not in complete descending card-number order at ${suffix(viewport)}`)
    const products = page.locator('.product-grid button')
    assert.deepEqual(await products.locator('b').allTextContents(), expectedProductOrder.slice(0, 4),
      `collapsed products are not newest-first at ${suffix(viewport)}`)
    const productMore = page.getByRole('button', { name: /展开更多产品/ })
    assert.equal(await productMore.getAttribute('aria-expanded'), 'false', `product disclosure state is wrong at ${suffix(viewport)}`)
    await productMore.click()
    assert.deepEqual(await products.locator('b').allTextContents(), expectedProductOrder,
      `expanded products are not newest-first at ${suffix(viewport)}`)
    const oldestProduct = products.filter({ has: page.getByText('第1季|天御', { exact: true }) })
    await oldestProduct.click()
    await page.getByRole('button', { name: '收起产品' }).click()
    assert.equal(await products.filter({ has: page.getByText('第1季|天御', { exact: true }) }).count(), 1,
      `selected product disappeared when collapsed at ${suffix(viewport)}`)
    assert.equal(await page.locator('.active-filters').getByText('产品：第1季|天御', { exact: true }).count(), 1,
      `active product summary is missing at ${suffix(viewport)}`)
    assert.equal(imageRequests.length, 0, `product browsing loaded card images at ${suffix(viewport)}`)
    await page.locator('.active-filters').getByRole('button', { name: '清除筛选' }).click()
    assert.equal(await page.locator('.active-filters').count(), 0, `filter reset failed at ${suffix(viewport)}`)
    const fenian = page.locator('#rule-entry-RULING-20260922-FENIAN-REPEAT')
    await fenian.locator('.faq-question').click()
    const cardArt = fenian.getByRole('button', { name: '查看芬尼亚传奇卡牌详情' })
    await cardArt.waitFor()
    await cardArt.locator('img').waitFor()
    await cardArt.click()
    const details = page.getByRole('dialog', { name: '芬尼亚传奇' })
    await details.waitFor()
    assert.equal(await details.locator('[data-card-detail-context="catalog"]').count(), 1,
      `card ruling did not reuse catalog detail content at ${suffix(viewport)}`)
    await details.getByRole('button', { name: '关闭卡牌详情' }).click()
    assert.equal(await details.count(), 0, `card detail did not close at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `card-qa-${suffix(viewport)}.png`), fullPage: true })
    assert.deepEqual(errors, [], `page errors at ${suffix(viewport)}`)
    report.push({ viewport, apiRequests: ruleFetches.length, catalogRequests: catalogRequests.length, imageRequests: imageRequests.length })
    await context.close()
  }

  const directCardContext = await browser.newContext({ viewport: { width: 1366, height: 768 } })
  const directCard = await directCardContext.newPage()
  const directCatalogRequests = []
  const directImageRequests = []
  directCard.on('request', request => {
    const url = request.url()
    if (/\/data\/l12\/cards\.(s1|lookup|st)\.json/.test(url)) directCatalogRequests.push(url)
    if (request.resourceType() === 'image') directImageRequests.push(url)
  })
  const assertSingleCatalogLoad = label => {
    for (const file of ['s1', 'lookup', 'st'])
      assert.equal(directCatalogRequests.filter(url => url.includes(`/data/l12/cards.${file}.json`)).length, 1,
        `${label} loaded cards.${file}.json more or less than once`)
  }
  await directCard.goto(base + '?tab=faq&mode=card')
  await directCard.getByText('S02-06S5·芬尼亚传奇·裁定', { exact: true }).waitFor()
  assert.deepEqual(await visibleRulingIds(directCard), expectedCardRulingOrder,
    'direct card FAQ entry is not in complete descending card-number order')
  assertSingleCatalogLoad('direct card FAQ entry')
  assert.equal(directImageRequests.length, 0, 'direct card FAQ entry eagerly loaded card images')
  await directCard.locator('.faq-search-row input').fill('待审核草稿')
  assert.deepEqual(await visibleRulingIds(directCard), expectedCardRulingOrder.slice(0, 2),
    'search changed the relative order of identified card rulings')
  await directCard.locator('.faq-search-row input').fill('芬尼亚传奇')
  await directCard.getByText('S02-06S5·芬尼亚传奇·裁定', { exact: true }).waitFor()
  assert.equal(await directCard.locator('.faq-list article').count(), 1, 'direct card FAQ name search did not narrow results')
  await directCard.locator('.faq-search-row input').fill('')
  await directCard.waitForFunction(expected => document.querySelectorAll('.faq-list article').length === expected,
    expectedCardRulingOrder.length)
  await directCard.waitForFunction(() => !new URL(location.href).searchParams.has('q'))
  directCatalogRequests.length = 0
  directImageRequests.length = 0
  await directCard.reload()
  await directCard.getByText('S02-06S5·芬尼亚传奇·裁定', { exact: true }).waitFor()
  assert.deepEqual(await visibleRulingIds(directCard), expectedCardRulingOrder,
    'refreshed card FAQ entry is not in complete descending card-number order')
  assertSingleCatalogLoad('refreshed card FAQ entry')
  assert.equal(directImageRequests.length, 0, 'refreshed card FAQ entry eagerly loaded card images')
  await directCard.locator('.faq-search-row input').fill('芬尼亚传奇')
  await directCard.getByText('S02-06S5·芬尼亚传奇·裁定', { exact: true }).waitFor()
  assert.equal(await directCard.locator('.faq-list article').count(), 1, 'refreshed card FAQ name search did not narrow results')
  await directCard.screenshot({ path: path.join(output, 'card-qa-direct-refresh-1366x768.png'), fullPage: true })
  await directCardContext.close()

  const deepLink = await browser.newPage({ viewport: { width: 1366, height: 768 } })
  await deepLink.goto(base + '?entry=OLD-RULING')
  await deepLink.locator('.faq-list article.open').waitFor()
  assert.match(deepLink.url(), /tab=faq/, 'entry-only deep link did not infer FAQ')
  assert.match(deepLink.url(), /entry=RULING-/, 'superseded deep link did not resolve replacement')
  await deepLink.reload()
  await deepLink.locator('.faq-list article.open').waitFor()
  await deepLink.getByRole('button', { name: '单卡问答' }).click()
  const firstProduct = deepLink.locator('.product-grid button').first()
  if (await firstProduct.count()) {
    await firstProduct.click()
    const selected = await firstProduct.locator('b').innerText()
    assert.match(decodeURIComponent(deepLink.url()), new RegExp('product=' + selected), 'product query state was not written')
    await deepLink.goBack()
    await deepLink.goForward()
    await deepLink.waitForURL(url => url.searchParams.get('product') === selected)
    await deepLink.locator('.product-grid button.active').waitFor()
    assert.match(decodeURIComponent(deepLink.url()), new RegExp('product=' + selected), 'product query state did not survive history navigation')
  }
  await deepLink.close()

  for (const viewport of adminViewports) {
    const context = await browser.newContext({ viewport, hasTouch: viewport.width < 900 })
    const admin = await context.newPage()
    await admin.goto(base + '?fixture=admin')
    await admin.locator('.workspace-tabs').waitFor()
    assert.equal(await admin.locator('.workspace-tabs button').count(), 4, `admin must expose four workspaces at ${suffix(viewport)}`)
    assert.equal(await admin.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
      `admin overflows at ${suffix(viewport)}`)
    assert((await admin.locator('.ruling-admin').boundingBox()).width <= 1681,
      `admin is unbounded at ${suffix(viewport)}`)

    assert.equal(await admin.locator('.center-create-bar button').count(), 5,
      `admin did not expose all five rule material creation actions at ${suffix(viewport)}`)
    const draftCenterCards = admin.locator('.center-editor')
    assert((await draftCenterCards.count()) >= 2, `admin did not show multiple blocks in one chapter at ${suffix(viewport)}`)
    const centerDraftCard = draftCenterCards.first()
    await centerDraftCard.locator(':scope > summary').click()
    assert.equal(await centerDraftCard.getByText('子板块图片（可选）', { exact: true }).count(), 1,
      `core block media editor is missing at ${suffix(viewport)}`)
    assert.equal(await centerDraftCard.getByRole('button', { name: '删除', exact: true }).count(), 1,
      `draft rule block delete action is missing at ${suffix(viewport)}`)
    assert.equal(await centerDraftCard.getByRole('button', { name: '下移', exact: true }).count(), 1,
      `draft rule block reorder action is missing at ${suffix(viewport)}`)
    assert.equal(await centerDraftCard.getByText(/稳定 ID|页码|栏目|主题/).count(), 0,
      `removed rule fields leaked back into the editor at ${suffix(viewport)}`)

    const draftCard = centerDraftCard
    assert.equal(await draftCard.locator('.admin-item-preview').count(), 1,
      `draft object lost its inline preview at ${suffix(viewport)}`)
    assert.equal(await draftCard.locator('.item-actions').getByRole('button', { name: '保存此项' }).count(), 1,
      `save action is not adjacent to draft object at ${suffix(viewport)}`)
    assert.equal(await draftCard.locator('.item-actions').getByRole('button', { name: '审核并发布此项' }).count(), 1,
      `publish action is not adjacent to draft object at ${suffix(viewport)}`)

    await admin.getByRole('button', { name: /已发布/ }).click()
    const publishedCenterCard = admin.locator('.center-editor').first()
    await publishedCenterCard.locator(':scope > summary').click()
    assert.equal(await publishedCenterCard.locator('input, textarea, select').count(), 0,
      `published center object remained editable at ${suffix(viewport)}`)
    assert.equal(await publishedCenterCard.getByRole('button', { name: '保存此项' }).count(), 0,
      `published center object exposed save at ${suffix(viewport)}`)
    assert.equal(await publishedCenterCard.getByRole('button', { name: '审核并发布此项' }).count(), 0,
      `published center object exposed publish at ${suffix(viewport)}`)
    assert.equal(await publishedCenterCard.locator('.item-actions').getByRole('button', { name: '退回修改' }).count(), 1,
      `return action is not adjacent to published center object at ${suffix(viewport)}`)
    assert.equal(await publishedCenterCard.getByRole('button', { name: '删除并取消公开' }).count(), 1,
      `published rule block cannot be deleted from public content at ${suffix(viewport)}`)

    const publishedRulingCard = admin.locator('.ruling-editor').first()
    await publishedRulingCard.locator(':scope > summary').click()
    assert.equal(await publishedRulingCard.locator('input, textarea, select').count(), 0,
      `published ruling remained editable at ${suffix(viewport)}`)
    assert.equal(await publishedRulingCard.getByRole('button', { name: '保存此项' }).count(), 0,
      `published ruling exposed save at ${suffix(viewport)}`)
    assert.equal(await publishedRulingCard.getByRole('button', { name: '审核并发布此项' }).count(), 0,
      `published ruling exposed publish at ${suffix(viewport)}`)
    assert.equal(await publishedRulingCard.locator('.item-actions').getByRole('button', { name: '退回修改' }).count(), 1,
      `return action is not adjacent to published ruling at ${suffix(viewport)}`)

    if (viewport.width === 1024) {
      const returnedRulingId = await publishedRulingCard.getAttribute('id')
      await publishedRulingCard.locator('.item-actions').getByRole('button', { name: '退回修改' }).click()
      const returnedRuling = admin.locator(`#${returnedRulingId}`)
      await returnedRuling.waitFor()
      assert.equal(await returnedRuling.getAttribute('open'), '', 'returned ruling was not opened in drafts')
      assert((await returnedRuling.locator('input, textarea, select').count()) > 0,
        'returned ruling did not regain draft editors')
      assert.equal(await admin.locator('.workspace-tabs button.active').getByText(/待审核/).count(), 1,
        'ruling return action did not switch to drafts')
    }

    if (viewport.width === 1366) {
      const returnedId = await publishedCenterCard.getAttribute('id')
      await publishedCenterCard.locator('.item-actions').getByRole('button', { name: '退回修改' }).click()
      const returnedCard = admin.locator(`#${returnedId}`)
      await returnedCard.waitFor()
      assert.equal(await returnedCard.getAttribute('open'), '', 'returned object was not opened in drafts')
      assert((await returnedCard.locator('input, textarea, select').count()) > 0,
        'returned object did not regain draft editors')
      assert.equal(await admin.locator('.workspace-tabs button.active').getByText(/待审核/).count(), 1,
        'return action did not switch to drafts')

      const rulingEditor = admin.locator('.ruling-editor').filter({ hasText: 'ADMIN-DRAFT' })
      await rulingEditor.locator(':scope > summary').click()
      await rulingEditor.getByText('锡瓦的卡巴', { exact: true }).waitFor()
      assert.equal(await rulingEditor.getByText('第1季|天御', { exact: true }).count(), 1, 'admin did not derive linked-card products')
      await rulingEditor.getByRole('button', { name: '查看锡瓦的卡巴卡牌详情' }).click()
      const adminDetails = admin.getByRole('dialog', { name: '锡瓦的卡巴' })
      await adminDetails.waitFor()
      assert.equal(await adminDetails.locator('[data-card-detail-context="catalog"]').count(), 1, 'admin did not reuse catalog detail component')
      await adminDetails.getByRole('button', { name: '关闭卡牌详情' }).click()
      await admin.screenshot({ path: path.join(output, 'admin-card-ruling-1366x900.png'), fullPage: true })
      await rulingEditor.locator('.item-actions').getByRole('button', { name: '保存此项' }).click()
      const savedProducts = await admin.evaluate(() => {
        const saved = window.__savedDrafts.filter(item => item.key === 'rules.rulings').at(-1)
        return JSON.parse(saved.value).entries.find(item => item.id === 'ADMIN-DRAFT').productIds
      })
      assert.deepEqual(savedProducts, ['第1季|天御', '第1季|天御·再临', '第1季|典藏版'], 'admin save did not persist derived products')
      await admin.getByRole('button', { name: /^来源/ }).click()
      await admin.locator('.source-list').waitFor()
      await admin.getByRole('button', { name: /历史与已替代/ }).click()
      await admin.locator('.history-workspace').waitFor()
      await admin.screenshot({ path: path.join(output, 'admin-history-1366x900.png'), fullPage: true })
    } else await admin.screenshot({ path: path.join(output, `admin-published-${suffix(viewport)}.png`), fullPage: true })
    await context.close()
  }

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, adminViewports, report }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length, adminViewports: adminViewports.length,
    screenshots: viewports.length * 3 + adminViewports.length + 2 }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
