import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = path.resolve(root, '../artifacts/public-deck-library-flow')
fs.mkdirSync(out, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const [cards,presets]=await Promise.all([loadDeckCatalog(),loadOfficialPresetDecks()])
const target=presets.find(deck=>deck.cardIds.length>=40)
if(!target)throw Error('fixture deck missing')
const now=new Date().toISOString()
const rows=Array.from({length:60},(_,index)=>{
 const source=presets[index%presets.length]||target
 const deck=index===7?target:source
 return {id:'community-'+index,ownerId:'owner-'+index,author:index===7?'组合验收作者':'公开作者'+index,deck:{...deck,name:index===7?'组合目标牌库':'公开牌库 '+String(index+1).padStart(2,'0'),specialIds:deck.specialIds||[],updatedAt:now,publicationId:'community-'+index,publicationVersion:1},views:index*9,likes:index,copies:60-index,liked:false,createdAt:now,updatedAt:now,seasonCompliant:index===7||index%2===0,seasonComplianceReason:index===7?'符合当前赛季构筑要求':'验收夹具'}
})
platformState.account=null
publicDeckApi.list=async()=>rows
const router=createRouter({history:createWebHistory(),routes:[{path:'/decks',name:'decks',component:Library},{path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>h('main',{id:'detail-fixture'},h('h1','公开牌库详情验收占位'))}},{path:'/:pathMatch(.*)*',redirect:'/decks'}]})
window.__qaRouter=router
createApp({render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'public-deck-library-flow-fixture',
  resolveId(id) { if (id === '/__public_deck_library_flow__.js') return id },
  load(id) { if (id === '/__public_deck_library_flow__.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => {
    if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__public_deck_library_flow__')) && !request.url.includes('.js')) {
      response.setHeader('Content-Type', 'text/html')
      response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__public_deck_library_flow__.js"></script>')
      return
    }
    next()
  }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const port = server.httpServer.address().port
  const profiles = [['phone-390x844',390,844],['landscape-844x390',844,390],['desktop-1440x900',1440,900]]
  const report = []
  for (const [name,width,height] of profiles) {
    const page = await browser.newPage({ viewport: { width, height } })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.hostname !== '127.0.0.1') return route.abort()
      if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{} } })
      return route.continue()
    })
    await page.goto(`http://127.0.0.1:${port}/decks?tab=plaza`)
    await page.locator('.plaza-grid article').first().waitFor()
    const targetCardName = await page.evaluate(async () => {
      const decks = await import('/src/l12/decks.ts')
      const presets = await decks.loadOfficialPresetDecks()
      const cards = await decks.loadDeckCatalog()
      const target = presets.find(deck => deck.cardIds.length >= 40)
      return cards.find(card => card.id === target.cardIds[0]).nameZh
    })
    await page.locator('.plaza-toolbar>input').fill('组合目标')
    if (width <= 700) {
      await page.getByRole('button', { name: /^筛选/ }).click()
      const sheet = page.getByRole('dialog', { name: '牌库筛选与排序' })
      await sheet.getByLabel('主宰').selectOption({ index: 1 })
      await sheet.getByLabel('阵营').selectOption({ index: 1 })
      await sheet.getByLabel('赛季合法性').selectOption('legal')
      await sheet.getByLabel('更新时间').selectOption('365')
      await sheet.getByLabel('排序').selectOption('name')
      await page.locator('.mobile-filter-sheet .card-picker-button').click()
    } else {
      await page.getByLabel('按主宰筛选').selectOption({ index: 1 })
      await page.getByLabel('按阵营筛选').selectOption({ index: 1 })
      await page.getByLabel('按合法性筛选').selectOption('legal')
      await page.getByLabel('按更新时间筛选').selectOption('365')
      await page.getByLabel('排序').selectOption('name')
      await page.getByRole('button', { name: '选择包含卡牌' }).click()
    }
    const picker = page.getByRole('dialog', { name: '选择公开牌库必须包含的卡牌' })
    await picker.waitFor()
    await picker.getByPlaceholder('搜索卡名、编号或效果文字').fill(targetCardName)
    const targetResult = picker.locator('.single-card-result-card').filter({ hasText: targetCardName }).first()
    await targetResult.getByRole('button', { name: '选择', exact: true }).click()
    if (width <= 700) await page.getByRole('dialog', { name: '牌库筛选与排序' }).getByRole('button', { name: /查看/ }).click()
    await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 1)
    assert.match(page.url(), /tab=plaza/)
    assert.match(decodeURIComponent(page.url()), /q=组合目标/)
    assert.match(page.url(), /card=/)
    assert.equal(await page.locator('.plaza-grid>article').count(), 1)
    assert.equal(await page.locator('.plaza-grid>article').innerText().then(value => value.includes('组合目标牌库')), true)
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${name} 组合筛选页面横向溢出`)
    await page.screenshot({ path:path.join(out,`${name}-combined-filters.png`),fullPage:true })

    const filteredUrl = page.url()
    await page.evaluate(async () => { await window.__qaRouter.push({path:'/decks',query:{tab:'plaza',q:'不存在的牌库'}}) })
    await page.getByText('没有符合筛选条件的公开牌库', { exact:true }).waitFor()
    await page.goBack()
    await page.waitForURL(filteredUrl)
    await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 1)
    await page.goForward()
    await page.getByText('没有符合筛选条件的公开牌库', { exact:true }).waitFor()
    await page.goBack()
    await page.waitForURL(filteredUrl)

    await page.getByRole('button', { name:'清除筛选', exact:true }).last().click()
    await page.locator('.plaza-toolbar>input').fill('')
    await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length >= 50)
    await page.locator('.site-content').evaluate(element => element.scrollTo({ top: Math.min(1200, element.scrollHeight - element.clientHeight) }))
    await page.waitForTimeout(100)
    const before = await page.locator('.site-content').evaluate(element => element.scrollTop)
    assert.ok(before > 100, `${name} 列表没有形成可验证滚动距离`)
    await page.locator('.plaza-summary').nth(10).click()
    await page.locator('#detail-fixture').waitFor()
    const expectedRestore = await page.evaluate(() => Number(sessionStorage.getItem('l12:deck-library:scroll:/decks?tab=plaza')))
    assert.ok(expectedRestore > 100, `${name} 没有保存进入详情时的列表位置`)
    await page.goBack()
    await page.locator('.plaza-grid>article').first().waitFor()
    await page.waitForTimeout(500)
    const restored = await page.locator('.site-content').evaluate(element => element.scrollTop)
    assert.ok(Math.abs(restored - expectedRestore) <= 4, `${name} 返回公开牌库后没有恢复滚动位置`)
    await page.screenshot({ path:path.join(out,`${name}-scroll-restored.png`),fullPage:false })
    assert.deepEqual(errors, [], `${name} 页面异常`)
    report.push({ viewport:`${width}x${height}`, before, expectedRestore, restored, url:page.url() })
    await page.close()
  }
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({status:'passed',profiles:report},null,2))
  console.log(JSON.stringify({status:'passed',viewports:profiles.length,screenshots:fs.readdirSync(out).filter(name=>name.endsWith('.png')).length,output:out}))
} finally {
  await browser?.close()
  await server.close()
}
