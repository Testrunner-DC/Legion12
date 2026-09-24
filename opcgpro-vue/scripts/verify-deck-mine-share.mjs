import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import jsQR from 'jsqr'
import sharp from 'sharp'

const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(root, '../artifacts/deck-mine-share')
fs.mkdirSync(out, { recursive:true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Page from '/src/l12/site/DeckLibraryPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi,alternateArtApi} from '/src/l12/platform.ts'
import '/src/style.css'
const base={...(await loadOfficialPresetDecks())[0],specialIds:[],updatedAt:new Date().toISOString()}
const published={...base,name:'已公开分享牌库',publicationId:'qa-public',publicationVersion:3}
const privateDeck={...base,name:'未公开私人牌库'}
const copied={...base,name:'公开牌库副本',publicationId:null,publicationVersion:null}
platformState.account={id:'author',username:'验收作者',role:'player'}
platformState.token='qa-token'
window.__decks={[published.name]:published,[privateDeck.name]:privateDeck,[copied.name]:copied}
window.__publicGets=[]
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(input)
 if(url.includes('/api/decks'))return new Response(JSON.stringify(Object.values(window.__decks)),{headers:{'Content-Type':'application/json'}})
 return originalFetch(input,init)
}
const publicEntry={id:'qa-public',ownerId:'author',author:'验收作者',deck:{...published},views:1,likes:0,copies:0,liked:false,createdAt:'',updatedAt:''}
publicDeckApi.list=async()=>[publicEntry]
publicDeckApi.get=async id=>{window.__publicGets.push(id);if(id!=='qa-public')throw Error('missing');return publicEntry}
alternateArtApi.mine=async()=>[]
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/decks',component:Page},{name:'public-deck-detail',path:'/decks/:deckId',component:{render:()=>null}},{path:'/deck-editor',component:{render:()=>null}}]})
await router.push('/decks?tab=mine')
createApp(Page).use(router).mount('#app')
`
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp','vite-mine-share'),server:{host:'127.0.0.1',port:0},plugins:[{
 name:'mine-share-fixture',resolveId(id){if(id==='/__mine_share.js')return id},load(id){if(id==='/__mine_share.js')return entry},
 configureServer(vite){vite.middlewares.use((req,res,next)=>{if(req.url==='/__mine_share'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__mine_share.js"></script>')}else next()})}
}]})

async function scan(file) {
  const raw=await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true})
  return jsQR(new Uint8ClampedArray(raw.data),raw.info.width,raw.info.height)?.data ?? ''
}

let browser
try {
  await server.listen()
  browser=await chromium.launch({headless:true,channel:'msedge'})
  const page=await browser.newPage({viewport:{width:1440,height:900}})
  const errors=[];page.on('pageerror',error=>errors.push(error.message))
  await page.route('**/*',route=>{const url=new URL(route.request().url());if(url.hostname!=='127.0.0.1')return route.abort();if(url.pathname.endsWith('card-assets.manifest.json'))return route.fulfill({json:{schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{}}});return route.continue()})
  const port=server.httpServer.address().port
  await page.goto(`http://127.0.0.1:${port}/__mine_share`)
  await page.locator('.mine-grid>article').first().waitFor()
  for (const [deckName,fileName,expectedQr] of [['已公开分享牌库','published',`http://127.0.0.1:${port}/decks/qa-public`],['未公开私人牌库','private',''],['公开牌库副本','copy','']]) {
    const card=page.locator('.mine-grid>article').filter({hasText:deckName}).first()
    await card.getByRole('button',{name:'生成牌库图',exact:true}).click()
    await page.locator('.image-preview').waitFor()
    const file=path.join(out,`${fileName}-share.png`)
    await page.screenshot({path:file,fullPage:true})
    assert.equal(await scan(file),expectedQr,`${deckName}二维码状态不正确`)
    await page.locator('.image-preview>header').getByRole('button').click()
  }
  assert.deepEqual(await page.evaluate(()=>window.__publicGets),['qa-public'],'只有带明确来源的牌库应核验公开实体')
  assert.deepEqual(errors,[])
  console.log(JSON.stringify({status:'passed',screenshots:3,publicQr:'decoded',privateQr:'absent',copyQr:'absent',output:out}))
} finally { await browser?.close();await server.close() }
