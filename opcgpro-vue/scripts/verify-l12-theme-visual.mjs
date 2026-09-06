import assert from 'node:assert/strict'
import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/batch261-theme'
fs.mkdirSync(output, { recursive: true })
// Reuse sanitized account/policy fixtures; never connect to a real account or room.
const fixture = fs.readFileSync(path.join(root, 'scripts/verify-batch257-visual.mjs'), 'utf8')
const start = fixture.indexOf('const entry = `') + 'const entry = `'.length
const end = fixture.indexOf('const query = new URLSearchParams')
assert(start > 15 && end > start, 'Shared synthetic fixture boundaries changed')
const entry = fixture.slice(start, end) + `
import { Teleport } from 'vue'
const query = new URLSearchParams(location.search)
if (!query.has('profile') && !query.has('ranking')) {
 l12State.room = {roomCode:'THEME261',yourPlayerIndex:0,started:false,players:[{playerIndex:0,name:'合成样式测试',connected:true,ready:false}],decks:[],options:{matchModeId:'friendly',spectating:'public',handVisibility:'request',disasterMode:'all',useCardRestrictions:false}}
}
const component = query.has('profile') ? ProfilePage : query.has('ranking') ? RankingsPage : BattleHubPage
const router = createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>h('div')}}]})
const app = createApp({render:()=>h('div', [h('style','.theme-legacy-probe{font:900 14px monospace!important}'),h(component),h(Teleport,{to:'body'},h('section',{id:'theme-probe',style:'position:fixed;right:12px;bottom:12px;padding:12px;background:#10181e;z-index:9999;max-width:90vw'},[
 h('span',{style:'font:400 14px Georgia'},'常规黑体 Aa 12'),h('strong',{style:'font:900 14px monospace'},'粗体黑体 Aa 12'),
 h('select',{'aria-label':'传送弹框选项'},[h('optgroup',{label:'分组'},[h('option',{value:'a'},'第一选项'),h('option',{value:'b',disabled:true},'禁用选项')])]),
 h('select',{disabled:true,'aria-label':'禁用控件'},[h('option','禁用控件')]),h('pre',{class:'theme-legacy-probe'},'普通界面代码标识')
]))])})
app.use(router);app.mount('#app')
`
let browser
const server = await createServer({root, server:{host:'127.0.0.1',port:0}, plugins:[{
 name:'l12-theme-fixture',resolveId(id){if(id==='/__theme__.js')return id},load(id){if(id==='/__theme__.js')return entry},
 configureServer(vite){vite.middlewares.use((req,res,next)=>{if(req.url?.startsWith('/__theme__?')){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__theme__.js"></script>');return}next()})}
}]})
const reports=[]
try {
 await server.listen()
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage({colorScheme:'light'})
 const errors=[]
 page.on('pageerror',error=>errors.push(error.message))
 await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
 for(const [mode,width,height] of [['room',1280,900],['room',760,1000],['room',390,1000],['profile',1280,900],['ranking',1280,900]]){
  await page.setViewportSize({width,height})
  await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__theme__?'+mode+'=1')
  await page.locator('#theme-probe').waitFor()
  if(mode==='room'){
   const select=page.locator('.room-rule-editor select').first()
   await select.waitFor();await select.focus();await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter')
   assert.equal(await select.inputValue(),'true','Native keyboard/v-model selection broke')
   await select.press('Alt+ArrowDown');await page.screenshot({path:path.join(output,`${mode}-${width}-open.png`)})
   await page.keyboard.press('Escape')
  }
  const result=await page.evaluate(()=>{
   const textNodes=[...document.querySelectorAll('body *')].filter(e=>e.getClientRects().length&&[...e.childNodes].some(n=>n.nodeType===Node.TEXT_NODE&&n.textContent.trim()))
   const fonts=[...new Set(textNodes.map(e=>getComputedStyle(e).fontFamily))]
   const controls=[...document.querySelectorAll('select,option,optgroup')].map(e=>({tag:e.tagName,bg:getComputedStyle(e).backgroundColor,color:getComputedStyle(e).color,scheme:getComputedStyle(e).colorScheme}))
   const boxes=[...document.querySelectorAll('.room-rule-editor select')].map(e=>{const a=e.getBoundingClientRect(),b=e.parentElement.getBoundingClientRect();return {contained:a.left>=b.left-1&&a.right<=b.right+1,height:a.height}})
   return {fonts,controls,boxes,weights:[getComputedStyle(document.querySelector('#theme-probe span')).fontWeight,getComputedStyle(document.querySelector('#theme-probe strong')).fontWeight]}
  })
  reports.push({mode,width,...result})
  await page.screenshot({path:path.join(output,`${mode}-${width}.png`)})
  assert(result.fonts.every(font=>font.includes('sans-serif')&&!/Georgia|monospace|XiaoWei|Times/i.test(font)),JSON.stringify(result.fonts))
  assert(result.controls.every(c=>c.scheme==='dark'&&!['rgb(255, 255, 255)','rgba(0, 0, 0, 0)'].includes(c.bg)),'Native options lack dark theme')
  assert.deepEqual(result.weights,['400','900'],'Font policy changed weights')
  assert(result.boxes.every(b=>b.contained&&b.height>=30),'Room dropdown clips or is too short')
 }
 assert.equal(errors.length,0,errors.join('\n'))
 console.log('L12 theme visual: 5/5 page/viewport cases; keyboard, Teleport, options, disabled, fonts and containment passed.')
} finally {
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(reports,null,2))
 await browser?.close();await server.close()
}
