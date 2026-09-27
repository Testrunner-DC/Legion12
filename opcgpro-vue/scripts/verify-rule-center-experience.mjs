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

const center=createRuleCenterDraft()
for(const collection of ['coreBlocks','quickStart','terms','tournament','versions'])
  for(const item of center[collection])item.status='published'
const rulings=createRulingsDraft().map((item,index)=>({...item,status:'published',supersedes:index===0?['OLD-RULING']:item.supersedes}))
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
 {id:'center-draft',page:'',topic:'对局准备',chapter:'待审核资料',text:'待审核正文',status:'pending'},
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
adminApi.saveContentDraft=async(key,value)=>({...contentView(key),draftValue:value,version:8})
adminApi.previewContent=async()=>({items:[]})
adminApi.publishRuleItem=async(key)=>contentView(key)
platformState.account={id:'qa-admin',username:'验收管理员',role:'admin',createdAt:'2026-01-01',publicHistory:false,permissions:['admin.content.draft','admin.content.publish']}
authState.initialized=true
authState.verified=true

const mode=new URLSearchParams(location.search).get('fixture')||'player'
const component={render:()=>mode==='admin'?h(AdminRuleRulingsPanel):h(RuleCenterPage)}
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
  { width: 1920, height: 1080, input: 'mouse' },
  { width: 1366, height: 768, input: 'mouse' },
  { width: 1024, height: 768, input: 'keyboard' },
  { width: 430, height: 932, input: 'touch' },
  { width: 390, height: 844, input: 'touch' },
]
const suffix = viewport => `${viewport.width}x${viewport.height}`

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
    assert.equal(await page.locator('.material-grid button').count(), 6, `material entry count changed at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `home-${suffix(viewport)}.png`), fullPage: true })

    if (viewport.input === 'keyboard') {
      await page.locator('.material-grid button').first().focus()
      await page.keyboard.press('Enter')
      await page.locator('.rule-tools').waitFor()
      await page.getByRole('button', { name: '规则资料首页' }).focus()
      await page.keyboard.press('Enter')
      await page.locator('.rules-home-lead').waitFor()
    } else if (viewport.input === 'touch') {
      await page.getByRole('button', { name: /常见问题/ }).tap()
      await page.locator('.faq-search-panel').waitFor()
      await page.locator('.faq-search-row .mobile-filter-trigger').tap()
      const dialog = page.getByRole('dialog', { name: '常见问题筛选' })
      await dialog.waitFor()
      assert.equal(await dialog.evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `filter sheet overflows at ${suffix(viewport)}`)
      await page.keyboard.press('Escape')
      assert.equal(await dialog.count(), 0, `filter sheet did not close at ${suffix(viewport)}`)
    } else {
      await page.getByRole('button', { name: /常见问题/ }).click()
      await page.locator('.faq-search-panel').waitFor()
    }

    if (viewport.input !== 'keyboard') {
      const questions = page.locator('.faq-question')
      if (await questions.count()) {
        await questions.first().click()
        assert.equal(await questions.first().getAttribute('aria-expanded'), 'true', `question did not expand at ${suffix(viewport)}`)
        await page.getByRole('button', { name: '全部收起' }).click()
        assert.equal(await questions.first().getAttribute('aria-expanded'), 'false', `collapse all failed at ${suffix(viewport)}`)
      }
      await page.getByRole('button', { name: '单卡问答' }).click()
      await page.locator('.faq-product-section').waitFor()
      const products = page.locator('.product-grid button')
      if (await products.count()) await products.first().click()
      assert.equal(catalogRequests.length, 0, `product browsing eagerly loaded card catalog at ${suffix(viewport)}`)
      await page.locator('.faq-search-row input').fill('S01')
      await page.waitForFunction(() => performance.getEntriesByType('resource').some(entry => entry.name.includes('/data/l12/cards.s1.json')))
      assert(catalogRequests.length >= 3, `card search did not load catalog on demand at ${suffix(viewport)}`)
      assert.equal(imageRequests.length, 0, `card Q&A loaded card images at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `card-qa-${suffix(viewport)}.png`), fullPage: true })
    }
    assert.deepEqual(errors, [], `page errors at ${suffix(viewport)}`)
    report.push({ viewport, apiRequests: ruleFetches.length, catalogRequests: catalogRequests.length, imageRequests: imageRequests.length })
    await context.close()
  }

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
    assert.match(decodeURIComponent(deepLink.url()), new RegExp('product=' + selected), 'product query state did not survive history navigation')
  }
  await deepLink.close()

  const admin = await browser.newPage({ viewport: { width: 1366, height: 900 } })
  await admin.goto(base + '?fixture=admin')
  await admin.locator('.workspace-tabs').waitFor()
  assert.equal(await admin.locator('.workspace-tabs button').count(), 4, 'admin must expose four workspaces')
  const draftCard = admin.locator('.admin-item-card').first()
  await draftCard.locator('summary').click()
  assert.equal(await draftCard.locator('.admin-item-preview').count(), 1, 'draft object lost its inline preview')
  assert.equal(await draftCard.locator('.item-actions').getByRole('button', { name: '保存此项' }).count(), 1, 'save action is not adjacent to draft object')
  assert.equal(await draftCard.locator('.item-actions').getByRole('button', { name: '审核并发布此项' }).count(), 1, 'publish action is not adjacent to draft object')
  await admin.getByRole('button', { name: /已发布/ }).click()
  const publishedCard = admin.locator('.admin-item-card').first()
  await publishedCard.locator('summary').click()
  assert.equal(await publishedCard.locator('.item-actions').getByRole('button', { name: '退回修改' }).count(), 1, 'return action is not adjacent to published object')
  await admin.getByRole('button', { name: /^来源/ }).click()
  await admin.locator('.source-list').waitFor()
  await admin.getByRole('button', { name: /历史与已替代/ }).click()
  await admin.locator('.history-workspace').waitFor()
  await admin.screenshot({ path: path.join(output, 'admin-history-1366x900.png'), fullPage: true })
  await admin.close()

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, report }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length, screenshots: viewports.length * 2 + 1 }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
