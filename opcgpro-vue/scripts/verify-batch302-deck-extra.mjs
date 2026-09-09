import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const {chromium} = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out='D:/GPT/Legion12/artifacts/batch302-deck-extra'; fs.mkdirSync(out,{recursive:true})
const entry=`
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Editor from '/src/l12/L12DeckEditor.vue'
import {loadDeckCatalog} from '/src/l12/decks.ts'
import '/src/style.css'
const cards=await loadDeckCatalog()
const master=cards.find(x=>x.cardType==='master'&&x.faction==='otherworld')
const trial=cards.find(x=>x.cardType==='trial'&&x.faction==='otherworld')
const legion=cards.find(x=>x.cardType==='legion'&&x.faction==='otherworld')
if(!master||!trial||!legion)throw Error('fixture catalog missing')
localStorage.setItem('l12-custom-decks-v1',JSON.stringify({'QA额外区':{name:'QA额外区',masterId:master.id,cardIds:[legion.id],moraleIds:[],specialIds:[trial.id],updatedAt:new Date().toISOString()}}))
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:all(.*)',component:Editor}]})
await router.push('/decks/editor?deck=QA额外区')
createApp(Editor).use(router).mount('#app')
`
let browser
const server=await createServer({root,server:{host:'127.0.0.1',port:0},plugins:[{name:'extra-fixture',resolveId(id){if(id==='/__extra__.js')return id},load(id){if(id==='/__extra__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url==='/__extra__'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__extra__.js"></script>');return}next()})}}]})
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message))
 await page.route('**/*',route=>{
  const url=new URL(route.request().url())
  if(url.hostname!=='127.0.0.1')return route.abort()
  if(url.pathname.endsWith('card-assets.manifest.json'))return route.fulfill({json:{schemaVersion:3,catalogVersion:'qa',assetVersion:'qa',basePath:'/',cards:{'S02-06S6':{cardId:'S02-06S6',contentHash:'qa',width:1732,height:1240,orientation:'landscape',variants:{thumbWebp:'/__qa-card.svg',boardWebp:'/__qa-card.svg',detailWebp:'/__qa-card.svg'}}}}})
  if(url.pathname==='/__qa-card.svg')return route.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="1732" height="1240"><rect width="1732" height="1240" fill="#354b48"/><path d="M0 0L1732 1240M1732 0L0 1240" stroke="#6aa" stroke-width="60"/></svg>'})
  return route.continue()
 })
 await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__extra__`)
 await page.locator('.deck-extra-entries .deck-entry-row').first().waitFor()
 await page.locator('.deck-extra-entries picture[data-orientation="landscape"]').waitFor()
 await page.locator('.deck-extra-entries .deck-entry-row').first().click()
 await page.locator('.builder-card-detail .archive-detail-image.horizontal').waitFor()
 for(const width of [1600,1280,768]){
  await page.setViewportSize({width,height:1000})
  const rows=await page.locator('.deck-extra-entries .deck-entry-row').evaluateAll(nodes=>nodes.map(row=>{
   const art=row.querySelector('picture'),name=row.querySelector('div b'),button=row.querySelector('button'),box=row.getBoundingClientRect(),n=name.getBoundingClientRect(),b=button.getBoundingClientRect()
   return {position:getComputedStyle(art).position,transform:getComputedStyle(art).transform,width:n.width,clear:n.right<=b.left,left:n.left>=box.left,right:b.right<=box.right}
  }))
  assert.ok(rows.every(x=>x.position==='absolute'&&x.transform==='none'&&x.width>30&&x.clear&&x.left&&x.right),JSON.stringify(rows))
  const detail=await page.locator('.builder-card-detail .archive-detail-image.horizontal').evaluate(image=>{
    const frame=image.getBoundingClientRect(),host=image.parentElement.getBoundingClientRect()
    return {left:frame.left>=host.left,right:frame.right<=host.right,width:frame.width,height:frame.height}
  })
  assert.ok(detail.left&&detail.right&&Math.abs(detail.width/detail.height-1.6)<.04,JSON.stringify(detail))
  await page.screenshot({path:path.join(out,`editor-${width}.png`),fullPage:true})
 }
 assert.deepEqual(errors,[])
 console.log('额外区主牌库同样式验证通过：1600/1280/768，横卡背景不参与行布局，卡名与按钮不重叠')
}finally{await browser?.close();await server.close()}
