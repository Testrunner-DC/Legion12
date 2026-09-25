import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const require=createRequire(import.meta.url)
const sharp=require(process.env.L12_SHARP||'sharp')

const root=path.resolve(import.meta.dirname,'..')
const out=path.resolve(root,'../artifacts/deck-alternate-art-flow')
fs.mkdirSync(out,{recursive:true})
const alternateArtPng=await sharp({create:{width:500,height:700,channels:4,background:{r:255,g:0,b:204,alpha:1}}}).png().toBuffer()
const {chromium}=require(process.env.L12_PLAYWRIGHT||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry=`
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog,loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,alternateArtApi,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const [cards,presets]=await Promise.all([loadDeckCatalog(),loadOfficialPresetDecks()])
const base=presets.find(deck=>deck.cardIds.length>=40)
if(!base)throw Error('fixture deck missing')
const counts=base.cardIds.reduce((map,id)=>(map[id]=(map[id]||0)+1,map),{})
const mixedId=Object.keys(counts).find(id=>counts[id]>=2)||base.cardIds[0]
const arts=[...new Set(base.cardIds)].map(id=>({id:'qa-art-'+id,artCode:'QA-'+id,baseCardId:id,displayName:'验收异画',mediaAssetId:'qa',imageUrl:'/api/site/media/qa-alt.png?id='+encodeURIComponent(id),thumbnailUrl:'/api/site/media/qa-alt.png?id='+encodeURIComponent(id),active:true,createdAt:'',updatedAt:'',builtIn:false,cardImageId:'qa-image-'+id}))
const alternateArtCopies=Object.fromEntries(Object.entries(counts).map(([id,count])=>[id,Array(count).fill('qa-art-'+id)]))
if(alternateArtCopies[mixedId].length>=2)alternateArtCopies[mixedId][0]=''
const saved={...base,name:'逐副本异画验收牌库',publicationId:'qa-public',publicationVersion:2,alternateArtCopies,updatedAt:new Date().toISOString()}
platformState.account={id:'author',username:'验收作者',role:'player',createdAt:'',publicHistory:true}
alternateArtApi.mine=async()=>arts
publicDeckApi.get=async()=>({id:'qa-public',ownerId:'author',author:'验收作者',deck:{...saved},views:1,likes:0,copies:0,liked:false,createdAt:'',updatedAt:''})
localStorage.setItem('l12-custom-decks-v1:author',JSON.stringify({[saved.name]:saved}))
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/deck-editor',component:Editor},{name:'public-deck-detail',path:'/decks/:deckId',component:{render:()=>null}}]})
await router.push('/deck-editor?deck='+encodeURIComponent(saved.name)+'&published=qa-public')
window.__qa={mixedId,total:base.cardIds.length}
createApp(Editor).use(router).mount('#app')
`
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp','vite-alt-art-flow'),server:{host:'127.0.0.1',port:0},plugins:[{
 name:'alternate-art-flow-fixture',resolveId(id){if(id==='/__alternate_art_flow.js')return id},load(id){if(id==='/__alternate_art_flow.js')return entry},
 configureServer(vite){vite.middlewares.use((req,res,next)=>{if(req.url==='/__alternate_art_flow'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__alternate_art_flow.js"></script>');return}if(req.url?.startsWith('/api/site/media/qa-alt.png')){res.setHeader('Content-Type','image/png');res.end(alternateArtPng);return}next()})}
}]})

let browser
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage({viewport:{width:1440,height:900}});const errors=[];page.on('pageerror',error=>errors.push(error.message))
 await page.route('**/*',route=>{const url=new URL(route.request().url());if(url.hostname!=='127.0.0.1')return route.abort();if(url.pathname.endsWith('card-assets.manifest.json'))return route.fulfill({json:{schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{}}});return route.continue()})
 await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__alternate_art_flow`)
 await page.locator('.deck-builder-grid').waitFor()
 await page.locator('.catalog-tabs').getByRole('button',{name:'主牌库',exact:true}).click()
 const total=await page.evaluate(()=>window.__qa.total)
 const customPoolCard=page.locator('.deck-card.alternate-art-card').first()
 await customPoolCard.waitFor()
 await customPoolCard.locator('.l12-card-image[data-source="sameOrigin"] img').waitFor()
 assert.match(await customPoolCard.locator('img').getAttribute('src'),/\/api\/site\/media\/qa-alt\.png/,'自主上传异画仍未在完整卡池使用媒体图')
 const mixedId=await page.evaluate(()=>window.__qa.mixedId)
 const originalPool=page.locator(`.deck-card[data-card-id="${mixedId}"][data-appearance-id="original"]`)
 const customPool=page.locator(`.deck-card[data-card-id="${mixedId}"][data-appearance-id="qa-art-${mixedId}"]`)
 const beforeOriginal=Number(await originalPool.locator('.pool-count-controls strong').innerText())
 const beforeCustom=Number(await customPool.locator('.pool-count-controls strong').innerText())
 await customPool.getByRole('button',{name:'减少一张'}).click()
 await originalPool.getByRole('button',{name:'增加一张'}).click()
 assert.equal(Number(await originalPool.locator('.pool-count-controls strong').innerText()),beforeOriginal+1,'切回原画没有更新逐副本数量')
 assert.equal(Number(await customPool.locator('.pool-count-controls strong').innerText()),beforeCustom-1,'自主上传异画减少没有更新逐副本数量')
 await originalPool.getByRole('button',{name:'减少一张'}).click()
 await customPool.getByRole('button',{name:'增加一张'}).click()
 assert.equal(Number(await customPool.locator('.pool-count-controls strong').innerText()),beforeCustom,'切回自主上传异画失败')
 assert.match(await page.locator('[data-deck-section="main"]>header').innerText(),new RegExp(`${total}\\/40`),'当前牌表总数错误')
 assert.ok(await page.locator('[data-deck-section="main"] .alternate-art-banner').count()>0,'当前牌表没有异画横幅')
 const customBanner=page.locator('[data-deck-section="main"] .alternate-art-banner').first()
 await customBanner.locator('.l12-card-image[data-source="sameOrigin"] img').waitFor()
 assert.match(await customBanner.locator('img').getAttribute('src'),/\/api\/site\/media\/qa-alt\.png/,'牌表横幅没有显示自主上传异画')
 await page.screenshot({path:path.join(out,'deck-list-mixed-art-1440x900.png'),fullPage:true})
 await page.getByRole('button',{name:'起手',exact:true}).click()
 await page.locator('.editor-opening-hand article').first().waitFor()
 const handLabels=await page.locator('.editor-opening-hand small').allTextContents()
 assert.equal(handLabels.length,6)
 assert.ok(handLabels.every(label=>label.includes('原画')||label.includes('验收异画')),'起手存在未标明实际画面的副本')
 assert.ok(handLabels.some(label=>label.includes('验收异画')),'起手仍全部显示为原画')
 await page.screenshot({path:path.join(out,'opening-hand-art-1440x900.png'),fullPage:true})
 await page.getByRole('button',{name:'生成牌库图',exact:true}).click()
 await page.locator('.deck-image-dialog').waitFor({timeout:15000})
 const shareFile=path.join(out,'share-image-art-1440x900.png')
 await page.screenshot({path:shareFile,fullPage:true})
 const raw=await sharp(shareFile).removeAlpha().raw().toBuffer({resolveWithObject:true})
 let magenta=0;for(let i=0;i<raw.data.length;i+=3)if(raw.data[i]>220&&raw.data[i+1]<45&&raw.data[i+2]>170)magenta++
 assert.ok(magenta>1000,'分享图没有渲染已保存的异画卡面')
 assert.deepEqual(errors,[])
 console.log(JSON.stringify({status:'passed',deckCopies:total,handCopies:6,magentaPixels:magenta,screenshots:3,output:out}))
}finally{await browser?.close();await server.close()}
