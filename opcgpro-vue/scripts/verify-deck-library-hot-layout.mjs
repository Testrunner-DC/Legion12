import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(root, '../artifacts/deck-library-hot-layout')
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
const now=new Date().toISOString()
const rows=Array.from({length:30},(_,index)=>({
 id:'hot-'+index,publicCode:'HOT'+String(index+1).padStart(6,'0'),ownerId:'owner-'+index,author:'热门作者 '+String(index+1).padStart(2,'0'),
 deck:{...base,name:'热门牌库 '+String(index+1).padStart(2,'0'),specialIds:base.specialIds||[],updatedAt:now,publicationId:'hot-'+index,publicationVersion:1},
 views:3000-index*17,likes:300-index,copies:200-index,liked:false,createdAt:now,updatedAt:now,seasonCompliant:true,
}))
platformState.account=null
publicDeckApi.list=async()=>rows
const router=createRouter({history:createWebHistory(),routes:[
 {path:'/decks',name:'decks',component:Library},
 {path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>h('main',{id:'detail-fixture'},'detail')}},
 {path:'/:pathMatch(.*)*',redirect:'/decks'},
]})
createApp({render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-deck-library-hot-layout'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-library-hot-layout-fixture',
    resolveId(id) { if (id === '/__deck_library_hot_layout__.js') return id },
    load(id) { if (id === '/__deck_library_hot_layout__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__deck_library_hot_layout__')) && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_library_hot_layout__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

async function prepare(page) {
  await page.addInitScript(() => localStorage.setItem('l12:official-presets:guest-seeded:v1', 'true'))
  await page.route('**/*', route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') return route.abort()
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json: { schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{} } })
    return route.continue()
  })
}

async function transformOf(row) {
  return row.evaluate(element => getComputedStyle(element).transform)
}

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } })
  await prepare(page)
  await page.goto(`http://127.0.0.1:${port}/decks`)
  await page.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()

  const hot = page.locator('.hot-decks')
  const rows = hot.locator('.hot-deck-track')
  assert.equal(await rows.count(), 2, '热门卡组必须严格保留两行')
  assert.equal(await rows.nth(0).evaluate(element => getComputedStyle(element).animationDirection), 'normal')
  assert.equal(await rows.nth(1).evaluate(element => getComputedStyle(element).animationDirection), 'reverse')
  assert.equal(await hot.locator('.hot-deck-loop').count(), 4, '每行仅包含一组内容及一组无障碍隐藏副本')
  assert.ok(await hot.getByRole('button', { name:/查看热门卡组/ }).count() >= 8)

  const before = await transformOf(rows.first())
  await page.waitForTimeout(250)
  const after = await transformOf(rows.first())
  assert.notEqual(after, before, '热门卡组应自动播放')
  await hot.hover()
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'paused', '鼠标悬停应暂停')
  await page.mouse.move(1, 1)
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'running', '鼠标移出应恢复')

  const firstHotDeck = hot.getByRole('button', { name:/查看热门卡组/ }).first()
  await firstHotDeck.focus()
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'paused', '键盘焦点进入应暂停')
  await page.getByPlaceholder('搜索牌库名称、作者或主宰').focus()
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'running', '键盘焦点离开应恢复')
  await hot.dispatchEvent('pointerdown', { pointerType:'touch', pointerId:1, isPrimary:true })
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'paused', '触控按下应暂停')
  await hot.dispatchEvent('pointerup', { pointerType:'touch', pointerId:1, isPrimary:true })
  assert.equal(await rows.first().evaluate(element => getComputedStyle(element).animationPlayState), 'running', '触控结束应恢复')

  const firstCard = page.locator('.plaza-grid>article').first()
  assert.equal(await firstCard.locator('.plaza-card-stats').count(), 1, '牌库盒子应把统计集中在独立信息区')
  assert.equal(await firstCard.locator('.plaza-card-actions').count(), 1, '牌库盒子应把操作集中在独立操作区')
  assert.equal(await firstCard.locator('.plaza-summary .deck-profile__copy b').evaluate(element => getComputedStyle(element).order), '1')
  assert.equal(await firstCard.locator('.plaza-summary .deck-profile__copy small').evaluate(element => getComputedStyle(element).order), '2')
  const hotBox = await hot.boundingBox()
  assert.ok(hotBox)
  await page.mouse.move(hotBox.x + 12, hotBox.y + 12)
  await page.waitForFunction(() => document.querySelector('.hot-decks')?.classList.contains('paused'))
  await firstHotDeck.click()
  await page.locator('#detail-fixture').waitFor()
  await page.goBack()
  await page.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()
  await page.close()

  const profiles = [[1920,1080],[1366,768],[430,932],[390,844]]
  const report = []
  for (const [width, height] of profiles) {
    const view = await browser.newPage({ viewport:{ width, height } })
    await prepare(view)
    await view.goto(`http://127.0.0.1:${port}/decks`)
    await view.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()
    const geometry = await view.evaluate(() => {
      const section = document.querySelector('.hot-decks')
      const rowBoxes = [...document.querySelectorAll('.hot-deck-viewport')].map(element => element.getBoundingClientRect())
      const cards = [...document.querySelectorAll('.plaza-grid>article')].slice(0, 6).map(element => element.getBoundingClientRect())
      return {
        documentFits: document.documentElement.scrollWidth <= innerWidth + 1,
        rows: rowBoxes.map(box => ({ x:box.x, y:box.y, width:box.width, height:box.height })),
        section: section ? { x:section.getBoundingClientRect().x, width:section.getBoundingClientRect().width } : null,
        cards: cards.map(box => ({ x:box.x, y:box.y, width:box.width, height:box.height })),
      }
    })
    assert.equal(geometry.documentFits, true, `${width}x${height} 页面横向溢出`)
    assert.equal(geometry.rows.length, 2, `${width}x${height} 热门卡组行数错误`)
    assert.ok(geometry.rows.every(row => row.x >= -1 && row.x + row.width <= width + 1), `${width}x${height} 轮播视口越界`)
    assert.ok(geometry.cards.every(card => card.x >= -1 && card.x + card.width <= width + 1), `${width}x${height} 牌库盒子越界`)
    await view.screenshot({ path:path.join(output,`deck-library-${width}x${height}.png`), fullPage:true })
    report.push({ viewport:`${width}x${height}`, ...geometry })
    await view.close()
  }

  const reduced = await browser.newPage({ viewport:{ width:390, height:844 }, reducedMotion:'reduce' })
  await prepare(reduced)
  await reduced.goto(`http://127.0.0.1:${port}/decks`)
  await reduced.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()
  assert.equal(await reduced.locator('.hot-deck-track').first().evaluate(element => getComputedStyle(element).animationName), 'none', '减少动态时应停止自动轮播')
  assert.equal(await reduced.locator('.hot-deck-loop[aria-hidden="true"]').first().evaluate(element => getComputedStyle(element).display), 'none', '减少动态时不应保留重复副本')
  await reduced.screenshot({ path:path.join(output,'deck-library-390x844-reduced-motion.png'), fullPage:true })
  await reduced.close()

  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status:'passed', profiles:report }, null, 2))
  console.log(JSON.stringify({ status:'passed', screenshots:5, output }))
} finally {
  await browser?.close()
  await server.close()
}
