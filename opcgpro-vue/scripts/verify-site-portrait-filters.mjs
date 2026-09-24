import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/site-portrait-filters')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter} from 'vue-router'
import DeckConstructionBrowser from '/src/l12/site/DeckConstructionBrowser.vue'
import RuleCenterPage from '/src/l12/site/RuleCenterPage.vue'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import {createRuleCenterDraft,createRulingsDraft} from '/src/l12/data/ruleCenterData.ts'
import {rankedApi} from '/src/l12/platform.ts'
import '/src/style.css'
const publishedRuleCenter=createRuleCenterDraft()
for(const collection of ['coreBlocks','quickStart','terms','tournament','versions'])for(const item of publishedRuleCenter[collection])item.status='published'
const publishedRulings=createRulingsDraft().map(item=>({...item,status:'published'}))
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.includes('/api/content?'))return new Response(JSON.stringify({values:{'rules.notice':'','rules.center':JSON.stringify(publishedRuleCenter),'rules.rulings':JSON.stringify({schemaVersion:2,entries:publishedRulings})},observedAt:new Date().toISOString(),nextRuleTransitionAt:null}),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/content/rules.center'))return new Response(JSON.stringify({key:'rules.center',value:JSON.stringify(createRuleCenterDraft())}),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/content/rules.rulings'))return new Response(JSON.stringify({key:'rules.rulings',value:JSON.stringify(createRulingsDraft())}),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/content/rules.notice'))return new Response(JSON.stringify({key:'rules.notice',value:''}),{status:200,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/operations/effective-policy'))return new Response('{}',{status:503,headers:{'Content-Type':'application/json'}})
 return originalFetch(input,init)
}
const catalog=await loadDeckCatalog()
const preferred=['legion','tactic','master','rune','trial','artifact']
const picked=preferred.map(type=>catalog.find(card=>card.cardType===type)).filter(Boolean)
const entries=picked.flatMap((card,index)=>[{cardId:card.id,quantity:index%3+1,section:index%3===0?'main':index%3===1?'morale':'extra'}])
const masters=catalog.filter(card=>card.cardType==='master').slice(0,4)
const masterStats=masters.map((master,index)=>({rank:index+1,masterId:master.id,masterName:master.nameZh,games:38-index,wins:25-index,losses:13,winRate:65.8-index,usageRate:25,firstWinRate:64,secondWinRate:67,firstWins:12,firstGames:19,secondWins:13,secondGames:19,strongestPlayer:'长昵称验收玩家'+index,title:'最强'+master.nameZh}))
rankedApi.leaderboard=async()=>({players:Array.from({length:5},(_,index)=>({rank:index+1,username:'移动端长昵称玩家'+index,faction:['秩序','混沌','命运'][index%3],tier:'定级段位名称',titles:['派系主题称号','最强'+(masters[index%masters.length]?.nameZh||'主宰')],favoriteMasterId:masters[index%masters.length]?.id,favoriteMasterName:masters[index%masters.length]?.nameZh,displayValue:'七曜值 '+(2100-index*45),wins:22-index,losses:11+index})),analytics:{range:'season',summary:{matches:84,placedPlayers:19,activeMasters:masters.length,updatedAt:'2026-09-21T08:00:00Z'},masters:masterStats,matchups:masterStats.flatMap(left=>masterStats.map(right=>({masterId:left.masterId,opponentMasterId:right.masterId,games:8,wins:5,winRate:62.5,firstWins:3,firstGames:4,secondWins:2,secondGames:4})))}})
rankedApi.history=async()=>[{seasonId:'S2026-1',seasonName:'第一赛季长名称',username:'赛季荣誉玩家',faction:'秩序',tier:'赛季最高段位',displayValue:'七曜值 2450',titles:['秩序冠首','最强'+(masters[0]?.nameZh||'主宰')]}]
const mode=new URLSearchParams(location.search).get('mode')||'deck'
const component={render:()=>mode==='rules'?h(RuleCenterPage):mode==='rankings'?h(RankingsPage):h('main',{class:'qa-page'},[h(DeckConstructionBrowser,{entries,catalog,title:'公开牌库完整构筑验收'})])}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:{render:()=>null}}]})
createApp(component).use(router).mount('#app')
`

let browser
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-site-portrait-filters'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'site-portrait-filter-fixture',
    resolveId(id) { if (id === '/__site_portrait__.js') return id },
    load(id) { if (id === '/__site_portrait__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__site_portrait__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11}.qa-page{box-sizing:border-box;min-height:100vh;padding:14px 12px}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__site_portrait__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 320, height: 700 },
  { width: 360, height: 780 },
  { width: 390, height: 844 },
  { width: 430, height: 932 },
  { width: 700, height: 900 },
  { width: 701, height: 900 },
  { width: 768, height: 1024 },
]

function suffix(viewport) { return `${viewport.width}x${viewport.height}` }

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  const report = []
  for (const viewport of viewports) {
    await page.setViewportSize(viewport)
    const mobile = viewport.width <= 700

    await page.goto(`http://127.0.0.1:${port}/__site_portrait__?mode=deck`)
    await page.locator('.construction-grid>button').first().waitFor()
    const deckBase = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      triggerVisible: getComputedStyle(document.querySelector('.mobile-filter-trigger')).display !== 'none',
      desktopVisible: getComputedStyle(document.querySelector('.construction-desktop-filters')).display !== 'none',
      navColumns: getComputedStyle(document.querySelector('.construction-browser>nav')).gridTemplateColumns,
    }))
    assert.equal(deckBase.overflow, false, `deck page overflows at ${suffix(viewport)}`)
    assert.equal(deckBase.triggerVisible, mobile, `deck mobile filter breakpoint mismatch at ${suffix(viewport)}`)
    assert.equal(deckBase.desktopVisible, !mobile, `deck desktop filters breakpoint mismatch at ${suffix(viewport)}`)
    if (mobile) {
      await page.locator('.mobile-filter-trigger').click()
      const deckDialog = page.getByRole('dialog', { name: '构筑筛选' })
      await deckDialog.waitFor()
      const sheet = await deckDialog.evaluate(element => {
        const rect = element.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, overflow: element.scrollWidth > element.clientWidth + 1 }
      })
      assert(sheet.left >= -1 && sheet.right <= viewport.width + 1 && sheet.top >= -1 && sheet.bottom <= viewport.height + 1, `deck filter sheet leaves safe viewport at ${suffix(viewport)}`)
      assert.equal(sheet.overflow, false, `deck filter sheet overflows at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `deck-filter-open-${suffix(viewport)}.png`) })
      await deckDialog.locator('select').first().selectOption({ index: 1 })
      await page.getByRole('button', { name: /查看 .* 种卡牌/ }).click()
      assert.match(await page.locator('.mobile-filter-trigger').innerText(), /筛选\s+1/, `deck filter count missing at ${suffix(viewport)}`)
    }
    await page.screenshot({ path: path.join(output, `deck-${suffix(viewport)}.png`), fullPage: true })

    await page.goto(`http://127.0.0.1:${port}/__site_portrait__?mode=rules`)
    await page.locator('.rule-tools input').waitFor()
    await page.locator('.rule-layout article').first().waitFor()
    const rulesBase = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      triggerVisible: getComputedStyle(document.querySelector('.rule-tools .mobile-filter-trigger')).display !== 'none',
      desktopVisible: getComputedStyle(document.querySelector('.rule-tools .rule-desktop-filter')).display !== 'none',
    }))
    assert.equal(rulesBase.overflow, false, `rules page overflows at ${suffix(viewport)}`)
    assert.equal(rulesBase.triggerVisible, mobile, `rules mobile filter breakpoint mismatch at ${suffix(viewport)}`)
    assert.equal(rulesBase.desktopVisible, !mobile, `rules desktop filter breakpoint mismatch at ${suffix(viewport)}`)
    if (mobile) {
      await page.locator('.rule-tools .mobile-filter-trigger').click()
      const dialog = page.getByRole('dialog', { name: '规则章节筛选' })
      await dialog.waitFor()
      const sheetOverflow = await dialog.evaluate(element => element.scrollWidth > element.clientWidth + 1)
      assert.equal(sheetOverflow, false, `rules filter sheet overflows at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `rules-filter-open-${suffix(viewport)}.png`) })
      await dialog.getByRole('button', { name: '关闭筛选' }).click()
      assert.equal(await dialog.count(), 0, `rules filter sheet did not close at ${suffix(viewport)}`)

      await page.getByRole('button', { name: /FAQ/ }).click()
      await page.locator('.faq-search-row input').waitFor()
      assert.equal(await page.locator('.desktop-popular-keywords').isVisible(), false, `FAQ keywords must collapse at ${suffix(viewport)}`)
      await page.locator('.faq-search-row .mobile-filter-trigger').click()
      const faqDialog = page.getByRole('dialog', { name: '规则主题筛选' })
      await faqDialog.waitFor()
      assert.equal(await faqDialog.locator('.mobile-popular-keywords').isVisible(), true, `FAQ keywords missing from filter sheet at ${suffix(viewport)}`)
      assert.equal(await faqDialog.evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `FAQ filter sheet overflows at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `faq-filter-open-${suffix(viewport)}.png`) })
      await faqDialog.getByRole('button', { name: /查看 .* 项结果/ }).click()
    }
    await page.screenshot({ path: path.join(output, `rules-${suffix(viewport)}.png`), fullPage: true })

    await page.goto(`http://127.0.0.1:${port}/__site_portrait__?mode=rankings`)
    await page.locator('.player-table .tr').first().waitFor()
    const rankingCards = viewport.width <= 850
    const rankingsBase = await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      tableOverflow: document.querySelector('.player-table').scrollWidth > document.querySelector('.player-table').clientWidth + 1,
      labeledCells: document.querySelectorAll('.player-table .tr [data-label]').length,
    }))
    assert.equal(rankingsBase.overflow, false, `rankings page overflows at ${suffix(viewport)}`)
    if (rankingCards) {
      assert.equal(rankingsBase.tableOverflow, false, `mobile player ranking still requires horizontal scroll at ${suffix(viewport)}`)
      assert(rankingsBase.labeledCells >= 10, `mobile player ranking lost field labels at ${suffix(viewport)}`)
    }
    await page.screenshot({ path: path.join(output, `rankings-players-${suffix(viewport)}.png`), fullPage: true })
    if (rankingCards) {
      await page.getByRole('button', { name: '主宰榜', exact: true }).click()
      await page.locator('.master-table .tr').first().waitFor()
      assert.equal(await page.locator('.master-table').evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `mobile master ranking still requires horizontal scroll at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `rankings-masters-${suffix(viewport)}.png`), fullPage: true })
      await page.getByRole('button', { name: '历史荣誉', exact: true }).click()
      await page.locator('.honor-table .tr').first().waitFor()
      assert.equal(await page.locator('.honor-table').evaluate(element => element.scrollWidth > element.clientWidth + 1), false, `mobile honor ranking still requires horizontal scroll at ${suffix(viewport)}`)
      await page.screenshot({ path: path.join(output, `rankings-history-${suffix(viewport)}.png`), fullPage: true })
    }
    report.push({ viewport, deck: deckBase, rules: rulesBase, rankings: rankingsBase })
  }
  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, report, errors }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length, screenshots: viewports.length * 3 + viewports.filter(viewport => viewport.width <= 700).length * 3 + viewports.filter(viewport => viewport.width <= 850).length * 2 }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
