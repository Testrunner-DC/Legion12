import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/batch-a-verification')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

// 模拟卡图资源清单：横版卡标记 landscape，其余 portrait，供 CardImage 按清单方向旋转。
// 构筑目录还会合并服务端的第二赛季卡池，验收 mock 必须覆盖它，不能只覆盖 public 目录。
const landscapeIds = new Set(['ST06-S1', 'ST-DS01', 'ST-DS02', 'ST-DS03', 'S02-06S4', 'S02-06S6'])
const manifestCards = {}
const catalogFiles = [
  path.join(root, 'public/data/l12/cards.s1.json'),
  path.join(root, 'public/data/l12/cards.st.json'),
  path.join(root, '../服务端WebSocket/TwelveLegions/Data/cards.s2.json'),
]
for (const file of catalogFiles) {
  const cards = JSON.parse(fs.readFileSync(file, 'utf8'))
  for (const card of cards) {
    manifestCards[card.id] = { cardId: card.id, contentHash: `qa-${card.id}`, width: 750, height: 1050, orientation: landscapeIds.has(card.id) ? 'landscape' : 'portrait', variants: { thumbWebp: `qa-${card.id}/thumb.webp`, boardWebp: `qa-${card.id}/board.webp`, detailWebp: `qa-${card.id}/detail.webp` } }
  }
}
const cardManifest = { schemaVersion: 3, catalogVersion: 'qa-batch-a', assetVersion: 'qa-batch-a', basePath: '/card-assets', cards: manifestCards }

const entry = `
import {createApp,h} from 'vue'
import {createMemoryHistory,createRouter,RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/mobileViewport.css'

localStorage.setItem('l12-auth-token','qa-token')
localStorage.setItem('l12-account',JSON.stringify({id:'qa-player',username:'批次A验收玩家',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}))

const platform=await import('/src/l12/platform.ts')
platform.platformState.token='qa-token'
platform.platformState.account={id:'qa-player',username:'批次A验收玩家',role:'player',createdAt:'2026-08-01T00:00:00Z',publicHistory:true,permissionVersion:3}
platform.authState.initialized=true
platform.authState.verified=true
platform.authState.refreshing=false
const net=await import('/src/l12/net.ts')
net.l12State.status='online'
platform.sessionApi.list=async()=>[]
platform.usernameChangeApi.status=async()=>({freeRenameAvailable:true,freeRenameUsed:0})
platform.emailApi.capability=async()=>({enabled:false,mailConfigured:false})
platform.friendApi.friends=async()=>[]
platform.friendApi.requests=async()=>[]
platform.friendApi.blocked=async()=>[]
platform.friendApi.presence=async()=>[]
platform.friendApi.players=async()=>[]
platform.alternateArtApi.gallery=async()=>[]

const {loadDeckCatalog}=await import('/src/l12/decks.ts')
const catalog=await loadDeckCatalog()
const masters=catalog.filter(card=>card.cardType==='master')
const morale=catalog.filter(card=>card.cardType==='morale').slice(0,2)
const trials=catalog.filter(card=>card.cardType==='trial').slice(0,2)
const main=catalog.filter(card=>!['master','morale','trial','destruction'].includes(card.cardType)).slice(0,12)

// A1：多称号玩家（3 个称号）验收逐行纵排
platform.rankedApi.leaderboard=async()=>({players:Array.from({length:6},(_,index)=>({rank:index+1,username:'验收玩家'+index,faction:'秩序',tier:'定级段位',titles:index===0?['秩序先锋','赛季冠首','最强'+(masters[0]?.nameZh||'主宰')]:['派系称号','最强'+(masters[index%masters.length]?.nameZh||'主宰')],favoriteMasterId:masters[0]?.id,favoriteMasterName:masters[0]?.nameZh,displayValue:'七曜值 '+(2100-index*45),wins:22-index,losses:11+index})),analytics:{range:'season',summary:{matches:84,placedPlayers:19,activeMasters:masters.length,updatedAt:'2026-09-21T08:00:00Z'},masters:[],matchups:[]}})
platform.rankedApi.history=async()=>[{seasonId:'S2026-1',seasonName:'第一赛季',username:'荣誉玩家',faction:'秩序',tier:'赛季段位',displayValue:'七曜值 2450',titles:['秩序冠首','最强'+(masters[0]?.nameZh||'主宰'),'远征先锋']}]

// A4：正文含超宽横图、竖图、普通图，均用内联 SVG 保证固有尺寸
const svg=(w,h,label,color)=>'data:image/svg+xml;utf8,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="'+w+'" height="'+h+'"><rect width="'+w+'" height="'+h+'" fill="'+color+'"/><text x="40" y="'+(h/2)+'" font-size="'+Math.min(120,h/3)+'" fill="#ffffff" font-family="sans-serif">'+label+'</text></svg>')
const media=[
 {id:'m-wide',altText:'超宽横图',desktopUrl:svg(2400,600,'超宽横图 2400x600','#28463c'),mobileUrl:svg(1280,320,'超宽横图移动 1280x320','#28463c'),thumbnailUrl:svg(600,150,'超宽横图','#28463c'),desktopWidth:2400,desktopHeight:600,mobileWidth:1280,mobileHeight:320},
 {id:'m-tall',altText:'竖图',desktopUrl:svg(800,1600,'竖图 800x1600','#463c28'),mobileUrl:svg(640,1280,'竖图移动','#463c28'),thumbnailUrl:svg(300,600,'竖图','#463c28'),desktopWidth:800,desktopHeight:1600,mobileWidth:640,mobileHeight:1280},
 {id:'m-normal',altText:'普通横图',desktopUrl:svg(1200,800,'普通横图 1200x800','#3c2846'),mobileUrl:svg(1200,800,'普通横图','#3c2846'),thumbnailUrl:svg(600,400,'普通横图','#3c2846'),desktopWidth:1200,desktopHeight:800,mobileWidth:1200,mobileHeight:800},
]
const body=JSON.stringify({format:'l12-blocks',version:1,blocks:[
 {id:'b1',type:'paragraph',text:'第一段正文，用于观察图文混排。'},
 {id:'b2',type:'image',mediaAssetId:'m-wide',alt:'超宽横图',caption:'超宽横图说明'},
 {id:'b3',type:'paragraph',text:'第二段正文。'},
 {id:'b4',type:'image',mediaAssetId:'m-tall',alt:'竖图',caption:''},
 {id:'b5',type:'image',mediaAssetId:'m-normal',alt:'普通横图',caption:'普通横图说明'},
]})
const article=(id,index)=>({id,title:'批次A验收资讯 '+index,summary:'摘要',body,index,category:'公告',coverUrl:'',link:'',slug:id,pinned:false,status:'published',hasUnpublishedChanges:false,createdAt:'2026-09-01T08:00:00Z',updatedAt:'2026-09-01T08:00:00Z',publishedAt:'2026-09-02T08:00:00Z',author:'十二军团官方',updatedBy:'admin',revision:1,kind:'news',sortOrder:index,bodyMedia:media})
const newsArticles=[article('news-a4',1),article('news-b',2)]
platform.articleApi.list=async()=>newsArticles
platform.siteContentApi.categories=async()=>['公告']
platform.siteContentApi.home=async()=>({composition:JSON.stringify({version:1,heroSlides:[],notices:[],newsEyebrow:'NEWS',newsTitle:'资讯一览',newsDescription:'',videoEyebrow:'VIDEO',videoTitle:'最新视频',videoDescription:'',productEyebrow:'PRODUCTS',productTitle:'产品上新',productDescription:''}),legal:'{}',news:[],videos:[],products:[],media:[]})

// A2：公开牌库含横版试炼卡的构筑
const publishedDeck={id:'pub-a2',ownerId:'someone',deck:{name:'横卡验收构筑',masterId:masters[0]?.id||'',cardIds:main.map(c=>c.id),moraleIds:morale.map(c=>c.id),specialIds:trials.map(c=>c.id),updatedAt:'2026-09-20T08:00:00Z'},author:'验收作者',views:12,likes:3,copies:1,liked:false,createdAt:'2026-09-10T08:00:00Z',updatedAt:'2026-09-20T08:00:00Z'}
platform.publicDeckApi.list=async()=>[publishedDeck]
platform.publicDeckApi.recordView=async()=>publishedDeck
platform.publicDeckApi.recordCopy=async()=>publishedDeck
platform.publicDeckApi.toggleLike=async()=>publishedDeck

const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}})
 if(url.includes('/api/operations/effective-policy'))return json({},503)
 if(url.includes('/api/matches'))return json([])
 return originalFetch(input,init)
}

const [
 {default:SiteShell},
 {default:NewsPage},{default:DeckLibraryPage},{default:RankingsPage},
]=await Promise.all([
 import('/src/l12/site/SiteShell.vue'),
 import('/src/l12/site/NewsPage.vue'),import('/src/l12/site/DeckLibraryPage.vue'),import('/src/l12/site/RankingsPage.vue'),
])
const routes=[
 {path:'/',redirect:'/news'},
 {path:'/news',component:NewsPage},
 {path:'/news/:articleId',component:NewsPage},
 {path:'/decks',component:DeckLibraryPage},
 {path:'/battle/rankings',component:RankingsPage,meta:{section:'battle'}},
 {path:'/:pathMatch(.*)*',redirect:'/'},
]
const router=createRouter({history:createMemoryHistory(),routes})
const target=new URLSearchParams(location.search).get('route')||'/'
await router.push(target)
await router.isReady()
createApp({render:()=>h(SiteShell,{},()=>h(RouterView))}).use(router).mount('#app')
`

let browser
const failures = []
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(root, '.tmp', 'vite-batch-a-verification'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'batch-a-verification-fixture',
    resolveId(id) { if (id === '/__batch_a__.js') return id },
    load(id) { if (id === '/__batch_a__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        const requestUrl = new URL(request.url || '/', 'http://fixture.local')
        if (requestUrl.pathname === '/card-assets/card-assets.manifest.json') {
          response.setHeader('Content-Type', 'application/json')
          response.end(JSON.stringify(cardManifest))
          return
        }
        if (request.url?.match(/^\/__batch_a__(\?|$)/)) {
          response.setHeader('Content-Type', 'text/html')
          response.end('<head><meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover"></head><style>html,body,#app{margin:0;min-height:100%;background:#080d11}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__batch_a__.js"></script>')
          return
        }
        next()
      })
    },
  }],
})

const viewports = [
  { width: 360, height: 780, label: '360x780' },
  { width: 390, height: 844, label: '390x844' },
  { width: 1280, height: 900, label: '1280x900' },
  { width: 1440, height: 900, label: '1440x900' },
]

const check = (ok, message) => { if (!ok) failures.push(message); console.log(`${ok ? 'PASS' : 'FAIL'} ${message}`) }

try {
  await server.listen()
  const address = server.httpServer.address()
  const port = typeof address === 'object' && address ? address.port : 0
  browser = await chromium.launch(process.env.L12_BROWSER_CHANNEL ? { channel: process.env.L12_BROWSER_CHANNEL } : {})
  const page = await browser.newPage()
  page.on('pageerror', error => failures.push(`pageerror: ${error.message}`))
  const goto = route => page.goto(`http://127.0.0.1:${port}/__batch_a__?route=${encodeURIComponent(route)}`)
  const noHorizontalOverflow = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })

    // A1 排行榜称号逐行纵排
    await goto('/battle/rankings')
    await page.locator('.ranking-page .player-table .tr').first().waitFor({ timeout: 15000 })
    const titleLayout = await page.evaluate(() => {
      const row = document.querySelector('.player-table .tr .title-list')
      if (!row) return { found: false }
      const rects = [...row.querySelectorAll('.ranked-identity-badge')].map(el => el.getBoundingClientRect())
      return { found: true, count: rects.length, sameColumn: rects.every(r => Math.abs(r.left - rects[0].left) < 3), increasingRows: rects.every((r, i) => i === 0 || r.top > rects[i - 1].top + 2), debug: rects.map(r => `${Math.round(r.left)},${Math.round(r.top)}`) }
    })
    if (!(titleLayout.found && titleLayout.count >= 3 && titleLayout.sameColumn && titleLayout.increasingRows)) console.log('A1 debug', JSON.stringify(titleLayout))
    check(titleLayout.found && titleLayout.count >= 3 && titleLayout.sameColumn && titleLayout.increasingRows, `A1 多称号逐行纵排 @${viewport.label}（${titleLayout.count ?? 0} 枚）`)
    check(await noHorizontalOverflow(), `A1 排行榜无横向溢出 @${viewport.label}`)
    await page.screenshot({ path: path.join(output, `rankings-${viewport.label}.png`), fullPage: true })

    // A2 公开牌库构筑横卡
    await goto('/decks')
    await page.locator('.deck-tabs button', { hasText: '公开牌库' }).click()
    await page.locator('.plaza-grid article').first().waitFor({ timeout: 15000 })
    await page.locator('.plaza-grid article', { hasText: '横卡验收构筑' }).locator('footer button', { hasText: '查看构筑' }).click()
    await page.locator('.deck-detail .construction-grid > button').first().waitFor({ timeout: 15000 })
    const landscapeCheck = await page.evaluate(() => {
      // 5/7 是未旋转构筑卡格的实测比例；横卡图片本身会在格子内旋转，
      // 由下面独立的 CardImage 清单方向断言覆盖，不能把两类几何结果混为一谈。
      const cards = [...document.querySelectorAll('.construction-grid > button .l12-card-image')]
      const portraitBoxes = cards.filter(el => !el.classList.contains('landscape-thumbnail-image'))
      const ratios = portraitBoxes.map(el => {
        const r = el.getBoundingClientRect()
        return r.height > 0 ? r.width / r.height : 0
      })
      const rotated = document.querySelectorAll('.construction-grid .l12-card-image.landscape-thumbnail-image').length
      const labels = [...document.querySelectorAll('.construction-grid > button small')].map(el => el.textContent)
      return { count: cards.length, portraitCount: portraitBoxes.length, allPortraitBox: ratios.length > 0 && ratios.every(r => Math.abs(r - 5 / 7) < 0.06), rotated, labels }
    })
    console.log('A2 debug', JSON.stringify(landscapeCheck.labels))
    check(landscapeCheck.count > 0 && landscapeCheck.allPortraitBox, `A2 未旋转构筑卡格为 5/7 @${viewport.label}（${landscapeCheck.portraitCount ?? 0} 格）`)
    check(landscapeCheck.rotated > 0, `A2 横卡由 CardImage 清单方向旋转 @${viewport.label}（${landscapeCheck.rotated} 张）`)
    check(await noHorizontalOverflow(), `A2 构筑详情无横向溢出 @${viewport.label}`)
    await page.screenshot({ path: path.join(output, `decks-detail-${viewport.label}.png`), fullPage: true })

    // A4 资讯正文图片
    await goto('/news')
    await page.locator('.news-list .list-entry').first().click()
    await page.locator('.news-detail .article-content img').first().waitFor({ timeout: 15000 })
    await page.waitForFunction(() => [...document.querySelectorAll('.article-content img')].every(img => img.complete && img.naturalWidth > 0), null, { timeout: 15000 })
    const imageCheck = await page.evaluate(() => {
      const viewportWidth = document.documentElement.clientWidth
      return [...document.querySelectorAll('.article-content img')].map(img => {
        const r = img.getBoundingClientRect()
        const expectedRatio = img.naturalWidth / img.naturalHeight
        const actualRatio = r.height > 0 ? r.width / r.height : 0
        return { width: r.width, fits: r.right <= viewportWidth + 1 && r.left >= -1, ratioKept: Math.abs(actualRatio - expectedRatio) / expectedRatio < 0.03 }
      })
    })
    check(imageCheck.length === 3 && imageCheck.every(item => item.fits), `A4 正文图不越出视口 @${viewport.label}（${imageCheck.map(i => Math.round(i.width)).join('/')}px）`)
    check(imageCheck.every(item => item.ratioKept), `A4 正文图等比不裁切 @${viewport.label}`)
    check(await noHorizontalOverflow(), `A4 资讯详情无横向滚动 @${viewport.label}`)
    await page.screenshot({ path: path.join(output, `news-detail-${viewport.label}.png`), fullPage: true })

    // A4 点击放大（仅 390 一档）：打开桌面交付图、Esc 关闭、焦点恢复
    if (viewport.width === 390) {
      const trigger = page.locator('.article-content .image-zoom-trigger').first()
      await trigger.click()
      await page.locator('.article-image-zoom__img').waitFor({ timeout: 5000 })
      const zoomSrc = await page.locator('.article-image-zoom__img').getAttribute('src')
      check(Boolean(zoomSrc && zoomSrc.includes('2400')), 'A4 放大查看桌面交付图')
      await page.screenshot({ path: path.join(output, 'news-zoom-390x844.png'), fullPage: false })
      await page.keyboard.press('Escape')
      await page.locator('.article-image-zoom').waitFor({ state: 'detached', timeout: 5000 })
      const focusBack = await page.evaluate(() => document.activeElement?.classList.contains('image-zoom-trigger'))
      check(focusBack, 'A4 放大关闭后焦点恢复')
    }
  }
} catch (error) {
  failures.push(`fatal: ${error instanceof Error ? error.message : String(error)}`)
} finally {
  await browser?.close()
  await server.close()
}

if (failures.length) {
  console.error(`批次 A 验收失败：\n- ${failures.join('\n- ')}`)
  process.exit(1)
}
console.log(`批次 A 验收通过，截图输出：${output}`)

