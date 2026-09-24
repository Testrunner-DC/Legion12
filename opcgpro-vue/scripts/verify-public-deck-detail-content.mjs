import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/public-deck-detail-content')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import PublicDeckDetailPage from '/src/l12/site/PublicDeckDetailPage.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const catalog=await loadDeckCatalog()
const placeholder='data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 250 350"><rect width="250" height="350" fill="#18252b"/><path d="M30 40h190v270H30z" fill="none" stroke="#9f8750" stroke-width="5"/><text x="125" y="180" fill="#e8d59c" text-anchor="middle" font-size="28">十二军团</text></svg>')
catalog.forEach(card=>{card.imageUrl=placeholder})
const preset=(await loadOfficialPresetDecks())[0]
const changed=[...preset.cardIds]
changed.splice(0,1,changed.find(id=>id!==changed[0])||changed[0])
const masters=catalog.filter(card=>card.cardType==='master')
const now='2026-09-24T06:00:00Z'
const fixture={id:'qa',ownerId:'author',author:'示例牌库作者',deck:{...preset,specialIds:preset.specialIds||[],updatedAt:now},views:128,likes:32,copies:19,liked:false,createdAt:'2026-09-20T06:00:00Z',updatedAt:now,seasonCompliant:true,details:{guide:{buildIdea:'通过低费军团建立前排，再利用关键战术保护核心单位并逐步扩大资源差。',opening:'优先保留两张低费军团与一张可互动战术；缺少前排时应积极调度。',keyCards:'核心主宰能力负责资源转换，关键军团提供持续站场，反制牌留给对手的主要展开。',commonSequence:'第一回合建立前排，第二回合补充资源并保留响应窗口，第三回合根据对手区域决定推进或控场。',substitutions:'环境偏快时增加低费军团；控制较多时替换为具备进场价值或墓地价值的牌。'},matchups:masters.slice(0,3).map((master,index)=>({opponentMasterId:master.id,notes:'观察对手第 '+(index+1)+' 回合资源，避免把全部单位投入同一轮交换。',keyCards:'保留即时互动与能跨过主战线的关键牌。',suggestedSwaps:'后手可减少一张高费牌，换入低费保护。'})),contentRevision:3,contentUpdatedAt:now,versions:[{version:3,name:preset.name,deck:{...preset,specialIds:preset.specialIds||[],updatedAt:now},createdAt:now,changes:[{section:'main',cardId:preset.cardIds[0],previousQuantity:1,currentQuantity:2}]},{version:2,name:preset.name,deck:{...preset,cardIds:changed,specialIds:preset.specialIds||[],updatedAt:'2026-09-22T06:00:00Z'},createdAt:'2026-09-22T06:00:00Z',changes:[{section:'main',cardId:preset.cardIds[0],previousQuantity:0,currentQuantity:1}]},{version:1,name:preset.name,deck:{...preset,specialIds:preset.specialIds||[],updatedAt:'2026-09-20T06:00:00Z'},createdAt:'2026-09-20T06:00:00Z',changes:[]}],matches:[],matchBindingStatus:'unavailable',matchBindingMessage:'尚无可证明绑定到该公开牌库版本的对局记录；不会用作者总战绩替代。'}}
publicDeckApi.get=async()=>fixture
publicDeckApi.recordView=async()=>fixture
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/decks/:deckId',component:PublicDeckDetailPage},{path:'/decks',component:{template:'<div>decks</div>'}}]})
await router.push('/decks/qa')
await router.isReady()
createApp({render:()=>h(RouterView)}).use(router).mount('#app')
`

let browser
const server = await createServer({
  root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-public-deck-detail-content'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'public-deck-detail-content-fixture',
    resolveId(id) { if (id === '/__public_deck_detail__.js') return id },
    load(id) { if (id === '/__public_deck_detail__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (request.url?.match(/^\/__public_deck_detail__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__public_deck_detail__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 1280, height: 720 }, { width: 1440, height: 900 }, { width: 1920, height: 1080 },
  { width: 360, height: 780 }, { width: 390, height: 844 }, { width: 844, height: 390 },
]
const suffix = viewport => `${viewport.width}x${viewport.height}`

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
    await page.goto(`http://127.0.0.1:${port}/__public_deck_detail__`)
    await page.getByRole('button', { name: '指南', exact: true }).waitFor()
    for (const tab of ['指南', '对局建议', '版本', '对局', '起手']) {
      await page.getByRole('button', { name: tab, exact: true }).click()
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
        `${tab} page overflows at ${suffix(viewport)}`)
    }
    await page.getByRole('button', { name: '指南', exact: true }).click()
    assert.match(await page.locator('[data-detail-section="guide"]').innerText(), /构筑思路[\s\S]*起手建议[\s\S]*常见展开/)
    await page.screenshot({ path: path.join(output, `guide-${suffix(viewport)}.png`), fullPage: true })
    await page.getByRole('button', { name: '起手', exact: true }).click()
    assert.equal(await page.locator('.opening-hand article').count(), 6, `opening hand count mismatch at ${suffix(viewport)}`)
    await page.screenshot({ path: path.join(output, `hands-${suffix(viewport)}.png`), fullPage: true })
    report.push({ viewport, guideHeight: await page.locator('.content-panel').evaluate(element => element.scrollHeight) })
  }
  assert.equal(errors.length, 0, `page errors: ${errors.join(' | ')}`)
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify({ status: 'passed', viewports, report, errors }, null, 2))
  console.log(JSON.stringify({ status: 'passed', output, viewports: viewports.length, screenshots: viewports.length * 2 }, null, 2))
} finally {
  await browser?.close()
  await server.close()
}
