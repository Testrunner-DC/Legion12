import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(process.env.L12_QA_OUTPUT || path.join(root, '../artifacts/deck-library-pagination-import'))
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const presets=await loadOfficialPresetDecks()
const base=presets.find(deck=>deck.cardIds.length>=40)||presets[0]
const requested=Math.max(0,Math.min(61,Number(new URL(location.href).searchParams.get('count')||61)))
const now=new Date().toISOString()
const rows=Array.from({length:requested},(_,index)=>({
 id:'qa-public-'+index,publicCode:'QP'+String(index+1).padStart(6,'0'),ownerId:'owner-'+index,author:'分页作者 '+String(index+1).padStart(2,'0'),
 deck:{...base,name:'QA Public '+String(index+1).padStart(3,'0'),specialIds:base.specialIds||[],updatedAt:now,publicationId:'qa-public-'+index,publicationVersion:1},
 views:index,likes:index,copies:index,liked:false,createdAt:now,updatedAt:now,seasonCompliant:true,
}))
platformState.account=null
publicDeckApi.list=async()=>rows
publicDeckApi.recordView=async()=>rows[0]
const router=createRouter({history:createWebHistory(),routes:[
 {path:'/decks',name:'decks',component:Library},
 {path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>h('main',{id:'detail-fixture'},'detail')}},
 {path:'/:pathMatch(.*)*',redirect:'/decks'},
]})
window.__qaRouter=router
createApp({render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-deck-library-pagination-import'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-library-pagination-import-fixture',
    resolveId(id) { if (id === '/__deck_library_pagination_import__.js') return id },
    load(id) { if (id === '/__deck_library_pagination_import__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__deck_library_pagination_import__')) && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_library_pagination_import__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const activeTab = page => page.locator('.deck-tabs button.active')
const publicCards = page => page.locator('.plaza-grid>article')
const mineCards = page => page.locator('.mine-grid>article')
const pagination = (page, label) => page.getByRole('navigation', { name: label })

async function prepare(page) {
  await page.addInitScript(() => localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true'))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{} } })
    return route.continue()
  })
}

async function seedMine(page, count) {
  await page.evaluate(async requested => {
    const { loadOfficialPresetDecks } = await import('/src/l12/decks.ts')
    const presets = await loadOfficialPresetDecks()
    const base = presets.find(deck => deck.cardIds.length >= 40) || presets[0]
    const rows = Object.fromEntries(Array.from({ length: requested }, (_, index) => {
      const name = 'QA Mine ' + String(index + 1).padStart(3, '0')
      return [name, { ...base, name, specialIds: base.specialIds || [], updatedAt: new Date(Date.now() - index * 1000).toISOString() }]
    }))
    localStorage.setItem('l12-custom-decks-v1', JSON.stringify(rows))
    localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true')
  }, count)
}

async function assertNoOverflow(page, label) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${label} 横向溢出`)
}

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })

  const boundaryPage = await browser.newPage({ viewport: { width: 1366, height: 768 } })
  await prepare(boundaryPage)
  for (const count of [0, 1, 29, 30, 31, 60, 61]) {
    await boundaryPage.goto(`http://127.0.0.1:${port}/decks?count=${count}`)
    await boundaryPage.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA Public')
    await boundaryPage.waitForFunction(expected => document.querySelectorAll('.plaza-grid>article').length === Math.min(expected, 30), count)
    assert.equal(await publicCards(boundaryPage).count(), Math.min(count, 30), `公开牌库 ${count} 条第一页数量错误`)
    assert.equal(await pagination(boundaryPage, '公开牌库分页').count(), count > 30 ? 1 : 0)
    if (count > 30) {
      await pagination(boundaryPage, '公开牌库分页').getByRole('button', { name: '下一页' }).click()
      assert.equal(await publicCards(boundaryPage).count(), Math.min(30, count - 30), `公开牌库 ${count} 条第二页数量错误`)
      if (count > 60) {
        await pagination(boundaryPage, '公开牌库分页').getByRole('button', { name: '下一页' }).click()
        assert.equal(await publicCards(boundaryPage).count(), 1, '公开牌库 61 条第三页数量错误')
      }
    }
  }

  await boundaryPage.goto(`http://127.0.0.1:${port}/decks?tab=mine&count=61`)
  for (const count of [0, 1, 29, 30, 31, 60, 61]) {
    await seedMine(boundaryPage, count)
    await boundaryPage.reload()
    await boundaryPage.getByPlaceholder('按牌库名称搜索').fill('QA Mine')
    await boundaryPage.waitForFunction(expected => document.querySelector('.mine-toolbar span')?.textContent?.includes(expected + ' 个结果'), count)
    assert.equal(await mineCards(boundaryPage).count(), Math.min(count, 30), `我的牌库 ${count} 条第一页数量错误`)
    assert.equal(await pagination(boundaryPage, '我的牌库分页').count(), count > 30 ? 1 : 0)
    if (count > 30) {
      await pagination(boundaryPage, '我的牌库分页').getByRole('button', { name:'下一页' }).click()
      assert.equal(await mineCards(boundaryPage).count(), Math.min(30, count - 30), `我的牌库 ${count} 条第二页数量错误`)
      if (count > 60) {
        await pagination(boundaryPage, '我的牌库分页').getByRole('button', { name:'下一页' }).click()
        assert.equal(await mineCards(boundaryPage).count(), 1, '我的牌库 61 条第三页数量错误')
      }
    }
  }
  await seedMine(boundaryPage, 61)
  await boundaryPage.reload()
  await boundaryPage.getByPlaceholder('按牌库名称搜索').fill('QA Mine')
  await boundaryPage.waitForFunction(() => document.querySelectorAll('.mine-grid>article').length === 30)
  assert.equal(await mineCards(boundaryPage).count(), 30)
  const minePager = pagination(boundaryPage, '我的牌库分页')
  await minePager.getByRole('button', { name: '下一页' }).click()
  await minePager.getByRole('button', { name: '下一页' }).click()
  assert.equal(await mineCards(boundaryPage).count(), 1)
  boundaryPage.once('dialog', dialog => dialog.accept())
  await mineCards(boundaryPage).first().getByRole('button', { name: '删除', exact: true }).click()
  await boundaryPage.waitForFunction(() => document.querySelectorAll('.mine-grid>article').length === 30)
  assert.equal(await minePager.getByRole('button', { name: '2', exact: true }).getAttribute('aria-current'), 'page', '删除导致越界后应夹紧到最后有效页')

  await minePager.getByRole('button', { name: '1', exact: true }).click()
  await boundaryPage.getByPlaceholder('按牌库名称搜索').fill('QA Mine 060')
  assert.equal(await mineCards(boundaryPage).count(), 1, '我的牌库筛选后应回到第一页并显示结果')
  await boundaryPage.getByPlaceholder('按牌库名称搜索').fill('QA Mine')
  await minePager.getByRole('button', { name: '下一页' }).click()
  await boundaryPage.getByRole('button', { name: '公开牌库', exact: true }).first().click()
  await boundaryPage.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA Public')
  const plazaPager = pagination(boundaryPage, '公开牌库分页')
  await plazaPager.getByRole('button', { name: '下一页' }).click()
  await plazaPager.getByRole('button', { name: '下一页' }).click()
  await boundaryPage.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA Public 061')
  assert.equal(await publicCards(boundaryPage).count(), 1, '公开牌库筛选后应回到第一页并显示结果')
  await boundaryPage.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA Public')
  await plazaPager.getByRole('button', { name: '下一页' }).click()
  await plazaPager.getByRole('button', { name: '下一页' }).click()
  await boundaryPage.getByRole('button', { name: '我的牌库', exact: true }).click()
  assert.equal(await minePager.getByRole('button', { name: '2', exact: true }).getAttribute('aria-current'), 'page', '标签切换不得覆盖我的牌库页码')
  await boundaryPage.getByRole('button', { name: '公开牌库', exact: true }).first().click()
  assert.equal(await plazaPager.getByRole('button', { name: '3', exact: true }).getAttribute('aria-current'), 'page', '标签切换不得覆盖公开牌库页码')

  await boundaryPage.goto(`http://127.0.0.1:${port}/decks`)
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '公开牌库')
  assert.equal(await boundaryPage.locator('.page-head small').innerText(), 'DECKS')
  assert.equal(await boundaryPage.locator('.page-head h1').innerText(), '牌库管理')
  await boundaryPage.goto(`http://127.0.0.1:${port}/decks?tab=plaza`)
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '公开牌库')
  await boundaryPage.goto(`http://127.0.0.1:${port}/decks?tab=mine`)
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '我的牌库')
  await boundaryPage.goto(`http://127.0.0.1:${port}/decks?tab=invalid`)
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '公开牌库')
  await boundaryPage.evaluate(async () => window.__qaRouter.push({ path:'/decks', query:{ tab:'mine' } }))
  await boundaryPage.getByPlaceholder('按牌库名称搜索').waitFor()
  await boundaryPage.evaluate(async () => window.__qaRouter.push({ path:'/decks', query:{ q:'QA Public' } }))
  await boundaryPage.getByPlaceholder('搜索牌库名称、作者或主宰').waitFor()
  await boundaryPage.goBack()
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '我的牌库')
  await boundaryPage.goForward()
  assert.equal((await activeTab(boundaryPage).innerText()).trim(), '公开牌库')

  await boundaryPage.goto(`http://127.0.0.1:${port}/decks?tab=mine`)
  await seedMine(boundaryPage, 1)
  await boundaryPage.reload()
  const importButton = boundaryPage.getByRole('button', { name: '导入牌库码', exact: true })
  await importButton.click()
  const importDialog = boundaryPage.getByRole('dialog', { name: '导入牌库码' })
  const importInput = importDialog.getByRole('textbox', { name:'牌库码', exact:true })
  assert.equal(await importInput.evaluate(element => element === document.activeElement), true, '打开导入弹框后输入框应获得焦点')
  await importInput.press('Enter')
  assert.match(await importDialog.getByRole('alert').innerText(), /请粘贴牌库码/)
  await importInput.fill('L12D2-INVALID')
  await importInput.press('Enter')
  assert.ok((await importDialog.getByRole('alert').innerText()).length > 2, '非法牌库码应显示错误')
  await importInput.press('Escape')
  assert.equal(await importDialog.count(), 0)
  assert.equal(await importButton.evaluate(element => element === document.activeElement), true, 'Esc 关闭后焦点应返回触发按钮')
  await importButton.click()
  await importDialog.getByRole('button', { name: '关闭导入牌库码' }).click()
  assert.equal(await importDialog.count(), 0)
  await importButton.click()
  await boundaryPage.locator('.modal-mask').click({ position:{ x:2, y:2 } })
  assert.equal(await importDialog.count(), 0, '点击遮罩应关闭导入弹框')
  const validCode = await boundaryPage.evaluate(async () => {
    const { loadOfficialPresetDecks } = await import('/src/l12/decks.ts')
    const { encodeDeckCode } = await import('/src/l12/site/deckShare.ts')
    const deck = (await loadOfficialPresetDecks()).find(item => item.cardIds.length >= 40)
    return encodeDeckCode({ ...deck, name:'QA Imported', specialIds:deck.specialIds || [], updatedAt:new Date().toISOString() })
  })
  await importButton.click()
  await importDialog.getByRole('textbox', { name:'牌库码', exact:true }).fill(validCode)
  await importDialog.getByRole('textbox', { name:'牌库码', exact:true }).press('Enter')
  await boundaryPage.getByText('已导入《QA Imported》', { exact:true }).waitFor()
  assert.equal(await importDialog.count(), 0, '合法牌库码导入成功后应关闭弹框')
  assert.equal(await mineCards(boundaryPage).filter({ hasText:'QA Imported' }).count(), 1)
  await boundaryPage.close()

  const profiles = [[1920,1080],[1366,768],[430,932],[390,844]]
  const report = []
  for (const [width, height] of profiles) {
    const page = await browser.newPage({ viewport: { width, height } })
    await prepare(page)
    await page.goto(`http://127.0.0.1:${port}/decks?count=61`)
    await page.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA Public')
    await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 30)
    await assertNoOverflow(page, `${width}x${height} 公开牌库`)
    await page.screenshot({ path:path.join(output,`public-${width}x${height}.png`),fullPage:true })
    await seedMine(page, 31)
    await page.goto(`http://127.0.0.1:${port}/decks?tab=mine&count=61`)
    await page.getByPlaceholder('按牌库名称搜索').fill('QA Mine')
    await page.waitForFunction(() => document.querySelectorAll('.mine-grid>article').length === 30)
    await assertNoOverflow(page, `${width}x${height} 我的牌库`)
    await page.screenshot({ path:path.join(output,`mine-${width}x${height}.png`),fullPage:true })
    await page.getByRole('button', { name:'导入牌库码', exact:true }).click()
    await assertNoOverflow(page, `${width}x${height} 导入弹框`)
    const dialogBox = await page.getByRole('dialog', { name:'导入牌库码' }).boundingBox()
    assert.ok(dialogBox && dialogBox.y >= 0 && dialogBox.y + dialogBox.height <= height + 1, `${width}x${height} 导入弹框被固定高度截断`)
    await page.screenshot({ path:path.join(output,`import-${width}x${height}.png`),fullPage:false })
    report.push({ viewport:`${width}x${height}`, dialogBox })
    await page.close()
  }
  const cacheFaults = []
  const faultProfiles = [[360,800],[390,844],[430,932],[768,1024],[1366,768],[1920,1080]]
  const preset = JSON.parse(fs.readFileSync(path.join(root, 'public/data/l12/preset-decks.s1.json'), 'utf8').replace(/^\uFEFF/, ''))[0]
  const readableRaw = JSON.stringify({ [preset.name]: { ...preset, id: 'fault-stable', revision: 1, specialIds: preset.specialIds ?? [], updatedAt: '2026-10-07T00:00:00Z' } })
  for (const [width,height] of faultProfiles) {
    for (const fault of ['unknown', 'corrupt', 'quota', 'read-denied']) {
      const page = await browser.newPage({ viewport:{width,height} })
      const pageErrors = [], privatePosts = []
      page.on('pageerror', error => pageErrors.push(error.message))
      page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname.startsWith('/api/decks')) privatePosts.push(request.url()) })
      await prepare(page)
      const raw = fault === 'unknown' ? JSON.stringify({ format:'l12-deck-cache', schema:99 }) : fault === 'corrupt' ? '{broken-cache' : readableRaw
      await page.addInitScript(({raw,fault}) => {
        const key = 'l12-custom-decks-v1'
        localStorage.setItem(key, raw)
        const originalGet = Storage.prototype.getItem
        const originalSet = Storage.prototype.setItem
        window.__cacheFaultRaw = () => originalGet.call(localStorage, key)
        if (fault === 'quota') Storage.prototype.setItem = function(name,value) {
          if (name === key) throw new DOMException('合成本机Quota失败', 'QuotaExceededError')
          return originalSet.call(this,name,value)
        }
        if (fault === 'read-denied') Storage.prototype.getItem = function(name) {
          if (name === key) throw new DOMException('合成本机读取拒绝', 'SecurityError')
          return originalGet.call(this,name)
        }
      }, {raw,fault})
      await page.goto(`http://127.0.0.1:${port}/decks?count=31`)
      await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length > 0)
      await page.getByText(/牌库缓存|本机牌库缓存/).first().waitFor()
      assert.equal(await page.evaluate(() => window.__cacheFaultRaw()), raw, 'Fault keeps original raw exactly')
      assert.deepEqual(privatePosts, [], 'Initialization cannot create a private deck to repair a fault')
      assert.deepEqual(pageErrors, [], 'An unavailable cache must not blank setup or leave unhandled rejections')
      await assertNoOverflow(page, `${width}x${height} ${fault}`)
      await page.screenshot({ path:path.join(output,`cache-${fault}-${width}x${height}.png`),fullPage:true })
      cacheFaults.push({ viewport:`${width}x${height}`, fault, rawPreserved:true, publicVisible:true, privatePosts:0, pageErrors })
      await page.close()
    }
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status:'passed', profiles:report, cacheFaults, platform:'desktop Edge synthetic viewports; no iOS/WeChat keyboard claim' }, null, 2))
  console.log(JSON.stringify({ status:'passed', screenshots:fs.readdirSync(output).filter(name=>name.endsWith('.png')).length, output }))
} finally {
  await browser?.close()
  await server.close()
}
