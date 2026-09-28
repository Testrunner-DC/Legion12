import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(root, '../artifacts/deck-library-environment')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {deckEnvironmentFromEvidence} from '/src/l12/site/deckEnvironment.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'

const products={
 st01:'ST01|天廷阵营预组',st02:'ST02|太阳城阵营预组',st03:'ST03|阿斯加德阵营预组',
 st04:'ST04|高天原阵营预组',st05:'ST05|奥林匹斯阵营预组',st06:'ST06|彼界阵营预组',
}
const evidence=(cardPool,product)=>[{cardPool,products:[product]}]
window.__environmentCases={
 s01:deckEnvironmentFromEvidence(evidence('S01','第1季|天御')),
 s02:deckEnvironmentFromEvidence(evidence('S02','第2季|典藏版')),
 st01:deckEnvironmentFromEvidence(evidence('STS1',products.st01)),
 st02:deckEnvironmentFromEvidence(evidence('STS1',products.st02)),
 st03:deckEnvironmentFromEvidence(evidence('STS1',products.st03)),
 st04:deckEnvironmentFromEvidence(evidence('STS1',products.st04)),
 st05:deckEnvironmentFromEvidence(evidence('STS1',products.st05)),
 st06:deckEnvironmentFromEvidence(evidence('STS1',products.st06)),
 mixed:deckEnvironmentFromEvidence([...evidence('S01','第1季|天御'),...evidence('S02','第2季|典藏版'),...evidence('STS1',products.st06)]),
 errata:deckEnvironmentFromEvidence(evidence('S02','ST05|奥林匹斯阵营预组（勘误收录）')),
 futureSt:deckEnvironmentFromEvidence(evidence('STS2','ST07|未来阵营预组')),
 unknownCard:deckEnvironmentFromEvidence([{products:[]}]),
 empty:deckEnvironmentFromEvidence([]),
}

const now=new Date().toISOString()
const makeDeck=(name,cardIds)=>({name,masterId:cardIds[0]||'',cardIds:cardIds.slice(1),moraleIds:[],specialIds:[],updatedAt:now})
const makeEntry=(name,cardIds,index,score=0)=>({
 id:'qa-'+index,publicCode:'ENV'+String(index).padStart(6,'0'),ownerId:'owner-'+index,author:'QA 环境作者',
 deck:{...makeDeck(name,cardIds),publicationId:'qa-'+index,publicationVersion:1},
 views:score,likes:score,copies:score,liked:false,createdAt:now,updatedAt:now,seasonCompliant:true,
})
const rows=[]
for(let index=0;index<36;index++) rows.push(makeEntry('QA 1.0 '+String(index+1).padStart(2,'0'),['S01-0001'],index,100-index))
rows.push(makeEntry('QA 2.0 原生',['S02-01M1A'],36,900))
rows.push(makeEntry('QA 2.0 勘误仍属旧环境',['S02-05M1'],37,890))
;['ST01-01','ST02-01','ST03-01','ST04-01','ST05-01','ST06-01'].forEach((cardId,index)=>rows.push(makeEntry('QA 2.5 ST0'+(index+1),[cardId],38+index,880-index)))
rows.push(makeEntry('QA 环境待定 未知卡',['QA-UNKNOWN'],44,870))
rows.push(makeEntry('QA 环境待定 空牌库',[],45,860))
platformState.account=null
publicDeckApi.list=async()=>rows
const router=createRouter({history:createWebHistory(),routes:[
 {path:'/decks',name:'decks',component:Library},
 {path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>h('main','detail')}},
 {path:'/:pathMatch(.*)*',redirect:'/decks'},
]})
createApp({render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-deck-library-environment'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-library-environment-fixture',
    resolveId(id) { if (id === '/__deck_library_environment__.js') return id },
    load(id) { if (id === '/__deck_library_environment__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__deck_library_environment__')) && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_library_environment__.js"></script>')
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
    if (url.pathname.endsWith('card-assets.manifest.json')) return route.fulfill({ json:{ schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{} } })
    return route.continue()
  })
}

async function waitForLibrary(page) {
  await page.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()
  await page.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA')
  await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 30)
}

async function assertNoOverflow(page, label) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${label} 横向溢出`)
}

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless:true, channel:'msedge' })
  const page = await browser.newPage({ viewport:{ width:1440,height:900 } })
  await prepare(page)
  await page.goto(`http://127.0.0.1:${port}/decks`)
  await waitForLibrary(page)

  assert.deepEqual(await page.evaluate(() => window.__environmentCases), {
    s01:'1.0',s02:'2.0',st01:'2.5',st02:'2.5',st03:'2.5',st04:'2.5',st05:'2.5',st06:'2.5',
    mixed:'2.5',errata:'2.0',futureSt:'pending',unknownCard:'pending',empty:'pending',
  })
  assert.equal(await page.locator('.plaza-grid>article').count(), 30, '公开牌库每页最多 30 个')
  await page.getByRole('navigation', { name:'公开牌库分页' }).getByRole('button', { name:'下一页' }).click()
  assert.equal(await page.locator('.plaza-grid>article').count(), 16)
  await page.getByLabel('按环境筛选').selectOption('2.0')
  assert.equal(await page.getByRole('navigation', { name:'公开牌库分页' }).count(), 0, '环境筛选后应重置到第一页')
  assert.equal(await page.locator('.plaza-grid>article').count(), 2)
  assert.match((await page.locator('.plaza-result-line').innerText()).replace(/\s+/g, ' '), /^2 个牌库/)
  assert.deepEqual(await page.locator('.plaza-grid .deck-environment-badge').allTextContents(), ['环境 2.0','环境 2.0'])
  assert.ok(new URL(page.url()).searchParams.get('environment') === '2.0', '环境筛选应写入 URL')
  await page.getByRole('button', { name:'清除筛选', exact:true }).last().click()
  assert.equal(await page.getByLabel('按环境筛选').inputValue(), 'all')
  assert.equal(new URL(page.url()).searchParams.has('environment'), false, '清除筛选应清除 URL 环境参数')
  await page.close()

  const restored = await browser.newPage({ viewport:{ width:1440,height:900 } })
  await prepare(restored)
  await restored.goto(`http://127.0.0.1:${port}/decks?q=QA&environment=2.5`)
  await restored.getByRole('heading', { name:'热门卡组', exact:true }).waitFor()
  await restored.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 6)
  assert.equal(await restored.getByLabel('按环境筛选').inputValue(), '2.5', '刷新后应恢复环境筛选')
  assert.deepEqual(await restored.locator('.plaza-grid .deck-environment-badge').allTextContents(), Array(6).fill('环境 2.5'))
  const hotLabels = await restored.locator('.hot-decks .deck-environment-badge').allTextContents()
  assert.ok(hotLabels.includes('环境 2.0') && hotLabels.includes('环境 2.5') && hotLabels.includes('环境待定'), '热门卡组应使用同一套环境标识')
  await restored.close()

  const profiles = [[360,800],[390,844],[1440,900],[2560,1080]]
  for (const [width,height] of profiles) {
    const view = await browser.newPage({ viewport:{ width,height }, hasTouch:width<=390 })
    await prepare(view)
    await view.goto(`http://127.0.0.1:${port}/decks`)
    await waitForLibrary(view)
    await view.evaluate(() => document.querySelectorAll('.hot-deck-track').forEach(track => { track.style.animation='none'; track.style.transform='none' }))
    const overlaps = await view.evaluate(() => [...document.querySelectorAll('.plaza-summary,.hot-deck-loop>button')].slice(0,12).map(card => {
      const badge=card.querySelector('.deck-environment-badge')?.getBoundingClientRect()
      const title=card.querySelector('.deck-profile__copy b')?.getBoundingClientRect()
      return Boolean(badge && title && badge.left < title.right && badge.right > title.left && badge.top < title.bottom && badge.bottom > title.top)
    }))
    assert.equal(overlaps.some(Boolean), false, `${width}x${height} 环境标识不得遮挡标题`)
    await assertNoOverflow(view, `${width}x${height}`)
    if (width <= 390) {
      await view.getByRole('button', { name:/^筛选/ }).click()
      const dialog=view.getByRole('dialog', { name:'牌库筛选与排序' })
      const environmentField=dialog.locator('label').filter({ hasText:/^环境/ })
      await environmentField.waitFor()
      await environmentField.locator('select').selectOption('2.5')
      await dialog.getByRole('button', { name:/查看 6 个牌库/ }).click()
      await view.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 6)
      assert.equal(await view.locator('.plaza-grid .deck-environment-badge').count(), 6)
    }
    await view.screenshot({ path:path.join(output, `environment-${width}x${height}.png`), fullPage:true })
    await view.close()
  }

  console.log(JSON.stringify({ ok:true, output, profiles }, null, 2))
} finally {
  if (browser) await browser.close()
  await server.close()
}
