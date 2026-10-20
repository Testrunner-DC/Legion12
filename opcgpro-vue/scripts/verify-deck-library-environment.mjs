import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_DECK_ENVIRONMENT_OUTPUT || 'D:/GPT/Legion12/artifacts/deck-cover-redesign-20261019/main-integration/environment'
const cacheRoot = process.env.L12_DECK_ENVIRONMENT_CACHE || 'D:/GPT/Legion12/cache/primary/deck-cover-integration/vite-environment'
const cardSizeOnly = process.env.L12_DECK_CARD_SIZE_ONLY === '1'
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  ". 'D:/GPT/Legion12/worktrees/lubu-pingyang-20261001/ops/windows/L12GeneratedStorage.ps1'; "
  + '$null=Assert-L12GeneratedPath -Path $env:L12_DECK_ENVIRONMENT_OUTPUT_RESOLVED -Purpose "deck environment evidence"; '
  + '$null=Assert-L12GeneratedPath -Path $env:L12_DECK_ENVIRONMENT_CACHE_RESOLVED -Purpose "deck environment Vite cache"; '
  + '$null=Assert-L12GeneratedPath -Path $env:TEMP -Purpose "deck environment browser temporary profile"'],
{ env: { ...process.env, L12_DECK_ENVIRONMENT_OUTPUT_RESOLVED: output, L12_DECK_ENVIRONMENT_CACHE_RESOLVED: cacheRoot }, stdio: 'pipe', windowsHide: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })
assert(!fs.existsSync(path.join(output, 'report.json')), 'Never overwrite an acceptance report')
const report = { scope: 'Local actual App/SiteShell/Settings/DeckLibrary SFC consumers with synthetic guest/public decks; not live transport, account sync or card-art CDN proof',
  status: 'running', cardSizeCases: [], pageErrors: [], externalRequestsBlocked: 0, browserClosed: false, serverClosed: false,
  sourceHashes: Object.fromEntries(['src/App.vue', 'src/l12/site/L12SettingsModal.vue', 'src/l12/site/DeckLibraryPage.vue', 'src/l12/audioPreferences.ts', 'src/l12/DeckCover.vue', 'scripts/verify-deck-library-environment.mjs']
    .map(file => [file, createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')])),
  output, cacheRoot, browserTemp: process.env.TEMP }
report.originalEnvironmentChecks=cardSizeOnly?'not-run-by-explicit-card-size-scope':'pending'

const entry = `
import {createApp,h} from 'vue'
import {createRouter,createWebHistory,RouterView} from 'vue-router'
import Library from '/src/l12/site/DeckLibraryPage.vue'
import {deckEnvironmentFromEvidence} from '/src/l12/site/deckEnvironment.ts'
import {platformState,authState,deckLibraryApi} from '/src/l12/platform.ts'
import {loadDeckCatalog,loadOfficialPresetDecks,loadSavedDecks,saveDeck} from '/src/l12/decks.ts'
import {publicSummaryFixture} from '/scripts/deck-summary-browser-fixture.mjs'
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
platformState.token=''
authState.initialized=true
// The real Library may canonicalize its filter query; keep only the test host
// choice across refresh, never inject or restore a card-size preference here.
const cardSizeProbe=new URLSearchParams(location.search).has('card-size-probe')||sessionStorage.getItem('l12:card-size-fixture')==='1'
if(cardSizeProbe){
 sessionStorage.setItem('l12:card-size-fixture','1')
 const preset=(await loadOfficialPresetDecks())[0]
 for(let index=0;index<6;index++){
  const name='尺寸合成牌库 '+index
  if(!loadSavedDecks()[name])await saveDeck({...preset,name,updatedAt:now})
 }
 for(const row of rows)row.deck={...preset,name:row.deck.name,updatedAt:now}
}
deckLibraryApi.summaries=publicSummaryFixture(rows,await loadDeckCatalog())
const router=createRouter({history:createWebHistory(),routes:[
 {path:'/decks',name:'decks',component:Library},
 {path:'/decks/:deckId',name:'public-deck-detail',component:{render:()=>h('main','detail')}},
 {path:'/:pathMatch(.*)*',redirect:'/decks'},
]})
const host=cardSizeProbe?(await import('/src/App.vue')).default:{render:()=>h('main',{class:'site-content',style:{position:'absolute',inset:'0',overflow:'auto'}},h(RouterView))}
createApp(host).use(router).mount('#app')
await router.isReady()
`

const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(cacheRoot, 'vite'),
  server: { host: '127.0.0.1', port: 0 },
  plugins: [{
    name: 'deck-library-environment-fixture',
    resolveId(id) { if (id === '/__deck_library_environment__.js') return id },
    load(id) { if (id === '/__deck_library_environment__.js') return entry },
    configureServer(vite) {
      vite.middlewares.use((request, response, next) => {
        if ((request.url?.startsWith('/decks') || request.url?.startsWith('/__deck_library_environment__')) && !request.url.includes('.js')) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="l12-landscape-teleports"></div><div id="app"></div><script type="module" src="/__deck_library_environment__.js"></script></body></html>')
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
  await page.getByRole('heading', { name:'热门牌库', exact:true }).waitFor()
  await page.getByPlaceholder('搜索牌库名称、作者或主宰').fill('QA')
  await page.waitForFunction(() => document.querySelectorAll('.plaza-grid>article').length === 30)
}

async function assertNoOverflow(page, label) {
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${label} 横向溢出`)
}

async function cardSizeGeometry(page, tab, columns) {
  await page.locator('.deck-tabs').getByRole('button', { name:tab === 'mine' ? '我的牌库' : '公开牌库', exact:true }).click()
  const grid = page.locator(tab === 'mine' ? '.mine-grid' : '.plaza-grid')
  await grid.locator(':scope>article').first().waitFor()
  const value = await grid.evaluate(element => {
    const cover=element.querySelector('.deck-cover'), card=cover?.querySelector('.deck-cover__card')
    if(!cover || !card)throw Error('Real cover and representative card must both exist')
    const box=cover.getBoundingClientRect(), representative=card.getBoundingClientRect()
    return { columns:getComputedStyle(element).gridTemplateColumns.split(' ').length,
      coverWidth:box.width, coverHeight:box.height, representativeWidth:representative.width }
  })
  assert.equal(value.columns, columns, `${tab} actual columns`)
  assert(Math.abs(value.coverWidth/value.coverHeight-12/5)<.02, 'Shared cover keeps its 12:5 ratio')
  return value
}

async function chooseCardSize(page, size, checkRestoredSelection = false) {
  const navigation=page.getByRole('button', { name:'打开导航', exact:true })
  if(await navigation.isVisible())await navigation.click()
  await page.getByRole('button', { name:'设置', exact:true }).click()
  const settings=page.locator('.l12-settings-modal')
  if(checkRestoredSelection)assert.equal(await settings.locator('label').filter({ hasText:'卡牌显示' }).locator('select').inputValue(),size,'Reopened Settings must show the restored selection')
  await settings.locator('label').filter({ hasText:'卡牌显示' }).locator('select').selectOption(size)
  await page.waitForFunction(expected => document.documentElement.dataset.l12CardSize === expected, size)
  assert.equal(await settings.locator('label').filter({ hasText:'卡牌显示' }).locator('select').inputValue(), size)
  await settings.getByRole('button', { name:'关闭设置', exact:true }).click()
}

async function verifyCardSizeConsumers(browser, origin) {
  for(const [width,height] of [[1440,900],[390,844]]){
    const page=await browser.newPage({ viewport:{width,height} })
    page.setDefaultTimeout(10000)
    page.on('pageerror', error => report.pageErrors.push(error.message))
    await page.addInitScript(() => {
      if(!localStorage.getItem('l12-audio-preferences-v1'))localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({musicEnabled:false,sfxEnabled:false,cardSize:'auto'}))
      localStorage.setItem('l12:official-presets:guest-seeded:v1','true')
    })
    await page.route('**/*', route => {
      const url=new URL(route.request().url())
      if(url.origin!==origin){report.externalRequestsBlocked++;return route.abort()}
      if(url.pathname==='/api/operations/effective-policy')return route.fulfill({json:{cardRestrictions:{}}})
      if(url.pathname.endsWith('card-assets.manifest.json'))return route.fulfill({json:{schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{}}})
      return route.continue()
    })
    try{
      await page.goto(origin+'/decks?card-size-probe=1')
      await page.locator('.plaza-grid>article').first().waitFor()
      const measurements=[]
      for(const [size,desktopColumns] of [['small',4],['medium',3],['large',2]]){
        await chooseCardSize(page,size)
        const expected=width<=700?1:desktopColumns
        const item={width,height,size,mine:await cardSizeGeometry(page,'mine',expected),plaza:await cardSizeGeometry(page,'plaza',expected)}
        report.cardSizeCases.push(item)
        await page.screenshot({path:path.join(output,`card-size-${width}-${size}.png`),fullPage:true})
        await page.reload({waitUntil:'domcontentloaded'})
        await page.waitForFunction(expected=>document.documentElement.dataset.l12CardSize===expected,size)
        await page.locator('.plaza-grid>article').first().waitFor()
        item.persisted=await page.evaluate(()=>JSON.parse(localStorage.getItem('l12-audio-preferences-v1')).cardSize)
        assert.equal(item.persisted,size,'Refresh must restore the chosen preference')
        await chooseCardSize(page,size,true)
        item.reloadedMine=await cardSizeGeometry(page,'mine',expected)
        item.reloadedPlaza=await cardSizeGeometry(page,'plaza',expected)
        item.status='passed';measurements.push(item)
      }
      for(const tab of ['mine','plaza']){
        const widths=measurements.map(item=>item[tab].coverWidth)
        const cards=measurements.map(item=>item[tab].representativeWidth)
        if(width>700){assert(widths[0]<widths[1]&&widths[1]<widths[2],`${tab} cover changes with selected size`);assert(cards[0]<cards[1]&&cards[1]<cards[2],`${tab} representative cards change with cover`)}
        else assert(Math.max(...widths)-Math.min(...widths)<1,'Mobile retains one-column cover width')
      }
      await assertNoOverflow(page,`${width} card-size consumer`)
    }catch(error){await page.screenshot({path:path.join(output,`card-size-failure-${width}.png`),fullPage:true}).catch(()=>{});throw error}
    finally{await page.close()}
  }
  assert.deepEqual(report.pageErrors,[])
}

let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless:true, channel:'msedge' })
  if(!cardSizeOnly){
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
  await restored.getByRole('heading', { name:'热门牌库', exact:true }).waitFor()
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
      const title=card.querySelector('.deck-cover__copy b')?.getBoundingClientRect()
      if (!badge || !title) throw Error('封面标题或环境标签缺失，不能跳过重叠核查')
      return badge.left < title.right && badge.right > title.left && badge.top < title.bottom && badge.bottom > title.top
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
  report.originalEnvironmentChecks='passed'
  report.originalEnvironmentProfiles=profiles
  }

  await verifyCardSizeConsumers(browser, `http://127.0.0.1:${port}`)
  report.status='passed'

  console.log(JSON.stringify({ ok:true, output, originalEnvironmentChecks:report.originalEnvironmentChecks, cardSizeCases:report.cardSizeCases.length }, null, 2))
} catch(error) {
  report.status='failed';report.error=String(error?.stack||error)
  throw error
} finally {
  if (browser) { await browser.close(); report.browserClosed=!browser.isConnected() }
  await server.close();report.serverClosed=true
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2),{flag:'wx'})
}
