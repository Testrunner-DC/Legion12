import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(root, '../artifacts/deck-mine-actions')
fs.mkdirSync(out, {recursive:true})
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry = `
import {createApp} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Page from '/src/l12/site/DeckLibraryPage.vue'
import {loadOfficialPresetDecks} from '/src/l12/decks.ts'
import {platformState,publicDeckApi} from '/src/l12/platform.ts'
import '/src/style.css'
const deck={...(await loadOfficialPresetDecks())[0],name:'我的牌库动作验收',updatedAt:new Date().toISOString(),publicationId:'qa-public',publicationVersion:1}
platformState.account={id:'author',username:'验收作者',role:'player'}
platformState.token='qa-token'
window.__decks={[deck.name]:deck}
window.__clipboard=''
Object.defineProperty(navigator,'clipboard',{value:{writeText:async text=>window.__clipboard=text}})
const originalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(input)
 if(url.includes('/api/decks')){
   if(init?.method==='PUT'){const value=JSON.parse(init.body);window.__decks[value.name]=value;return new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json'}})}
   if(init?.method==='DELETE'){const name=decodeURIComponent(url.split('/api/decks/')[1]);delete window.__decks[name];return new Response('{}',{headers:{'Content-Type':'application/json'}})}
   return new Response(JSON.stringify(Object.values(window.__decks)),{headers:{'Content-Type':'application/json'}})
 }
 return originalFetch(input,init)
}
publicDeckApi.list=async()=>[]
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/decks',component:Page},{path:'/deck-editor',component:{template:'<div/>'}}]})
await router.push('/decks?tab=mine')
createApp(Page).use(router).mount('#app')
`
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp','vite-mine-actions'),server:{host:'127.0.0.1',port:0},plugins:[{
 name:'mine-actions-fixture',resolveId(id){if(id==='/__mine_actions.js')return id},load(id){if(id==='/__mine_actions.js')return entry},
 configureServer(vite){vite.middlewares.use((req,res,next)=>{if(req.url==='/__mine_actions'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__mine_actions.js"></script>')}else next()})}
}]})
let browser
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage();const errors=[];page.on('pageerror',error=>errors.push(error.message))
 for(const [width,height] of [[1440,900],[390,844],[844,390]]){
  await page.setViewportSize({width,height});await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mine_actions`)
  const card=page.locator('.mine-grid>article').first();await card.waitFor()
  if(await card.locator('summary').isVisible())await card.locator('summary').click()
  for(const name of ['复制牌库','复制牌库码','删除'])assert.equal(await card.getByRole('button',{name,exact:true}).isVisible(),true)
  await card.scrollIntoViewIfNeeded()
  await page.screenshot({path:path.join(out,`mine-actions-${width}x${height}.png`),fullPage:true})
  await card.getByRole('button',{name:'复制牌库码',exact:true}).click()
  assert.ok(await page.evaluate(()=>window.__clipboard.length>20))
  await card.getByRole('button',{name:'复制牌库',exact:true}).click()
  await page.waitForFunction(()=>Object.keys(window.__decks).length===2)
  assert.equal(await page.evaluate(()=>Object.values(window.__decks).filter(d=>d.name.includes('副本')).every(d=>!d.publicationId&&!d.publicationVersion)),true)
  const copy=page.locator('.mine-grid>article').filter({hasText:'副本'})
  if(await copy.locator('summary').isVisible())await copy.locator('summary').click()
  page.once('dialog',dialog=>dialog.dismiss());await copy.getByRole('button',{name:'删除',exact:true}).click()
  assert.equal(await page.locator('.mine-grid>article').count(),2,'取消删除保留牌库')
  page.once('dialog',dialog=>dialog.accept());await copy.getByRole('button',{name:'删除',exact:true}).click()
  await page.waitForFunction(()=>Object.keys(window.__decks).length===1)
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false)
 }
 assert.deepEqual(errors,[])
 console.log(JSON.stringify({status:'passed',viewports:3,actions:'copy deck, copy code, delete cancel/confirm',output:out}))
}finally{await browser?.close();await server.close()}
