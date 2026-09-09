import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = 'D:/GPT/Legion12/artifacts/batch302-controls'
fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp,h,ref} from 'vue'
import {createRouter,createMemoryHistory} from 'vue-router'
import Hub from '/src/l12/site/BattleHubPage.vue'
import Paged from '/src/l12/site/PagedCollection.vue'
import {rankedApi} from '/src/l12/platform.ts'
import '/src/style.css'
const factions=[{id:'order',name:'秩序',color:'#ccc',tiers:[]},{id:'chaos',name:'混沌',color:'#777',tiers:[]},{id:'fate',name:'命运',color:'#abc',tiers:[]}]
let faction='秩序';window.__posts=[]
rankedApi.overview=async()=>({profile:{faction,placed:true,tier:'测试段位',displayValue:'七曜值 12,345',titles:[]},factionTotals:{},config:{factions,placementMatches:5},history:[]})
rankedApi.selectFaction=async(id)=>{window.__posts.push(id);faction=factions.find(x=>x.id===id).name;return {faction}}
const rows=ref(Array.from({length:23},(_,i)=>({id:i,label:'记录 '+(i+1)})));window.__shrink=()=>rows.value=rows.value.slice(0,2)
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:all(.*)',component:Hub}]})
createApp({render:()=>h('main',[h('section',{id:'paging'},h(Paged,{items:rows.value},{default:({items})=>items.map(x=>h('p',{'data-row':x.id},x.label))})),h(Hub)])}).use(router).mount('#app')
`
let browser
const server = await createServer({ root, server:{host:'127.0.0.1',port:0}, plugins:[{name:'controls-fixture',resolveId(id){if(id==='/__controls__.js')return id},load(id){if(id==='/__controls__.js')return entry},configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url==='/__controls__'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__controls__.js"></script>');return}next()})}}] })
try {
  await server.listen(); browser=await chromium.launch({headless:true,channel:'msedge'})
  const page=await browser.newPage({viewport:{width:1280,height:900}})
  const errors=[];page.on('pageerror',error=>errors.push(error.message))
  await page.route('**/*',async route=>{
    const url=new URL(route.request().url())
    if(url.hostname!=='127.0.0.1')return route.abort()
    if(url.pathname==='/api/operations/effective-policy')return route.fulfill({json:{version:1,season:{id:'season',name:'本赛季'},maintenance:{enabled:false,entryBlocked:false},defaultRoomConfig:{spectating:'public',handVisibility:'request',disasterMode:'all'},cardRestrictions:[]}})
    if(url.pathname.startsWith('/api/'))return route.fulfill({json:[]})
    return route.continue()
  })
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__controls__`)
  await page.getByRole('button',{name:'改选派系',exact:true}).waitFor()
  assert.equal(await page.locator('#paging [data-row]').count(),10)
  await page.locator('#paging').getByText('下一页',{exact:true}).click()
  assert.equal(await page.locator('#paging [data-row]').first().textContent(),'记录 11')
  await page.locator('#paging').getByText('下一页',{exact:true}).click()
  assert.equal(await page.locator('#paging [data-row]').count(),3)
  await page.evaluate(()=>window.__shrink())
  await page.waitForFunction(()=>document.querySelectorAll('#paging [data-row]').length===2)
  await page.getByRole('button',{name:'改选派系',exact:true}).click()
  await page.getByRole('button',{name:'返回，不更改',exact:true}).click()
  assert.deepEqual(await page.evaluate(()=>window.__posts),[])
  await page.getByRole('button',{name:'改选派系',exact:true}).click()
  await page.getByRole('button',{name:'混沌',exact:true}).click()
  await page.getByRole('dialog',{name:'确认改为混沌？'}).waitFor()
  await page.getByRole('button',{name:'取消，保留当前派系',exact:true}).click()
  assert.deepEqual(await page.evaluate(()=>window.__posts),[])
  for(const width of [1280,390]){
    await page.setViewportSize({width,height:900})
    await page.getByRole('button',{name:'混沌',exact:true}).click()
    const dialog=page.getByRole('dialog',{name:'确认改为混沌？'})
    const box=await dialog.boundingBox();assert.ok(box.x>=0&&box.x+box.width<=width)
    await page.screenshot({path:path.join(out,`faction-${width}.png`)})
    await page.getByRole('button',{name:'取消，保留当前派系',exact:true}).click()
  }
  await page.getByRole('button',{name:'混沌',exact:true}).click()
  await page.getByRole('button',{name:'确认清零并更改',exact:true}).click()
  await page.waitForFunction(()=>window.__posts.length===1)
  assert.deepEqual(await page.evaluate(()=>window.__posts),['chaos'])
  await page.getByRole('button',{name:'改选派系',exact:true}).waitFor()
  assert.deepEqual(errors,[])
  console.log('分页与派系真实浏览器验证通过：翻页、缩短列表、返回、取消、确认单次提交及1280/390视口')
} finally {await browser?.close();await server.close()}
