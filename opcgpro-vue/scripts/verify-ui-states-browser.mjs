import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || 'D:/GPT/Legion12/artifacts/e1-ui-states-20261002'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })
const entry = `
import {createApp,h,ref} from 'vue'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
import UiButton from '/src/l12/site/UiButton.vue'
import UiNotice from '/src/l12/site/UiNotice.vue'
import RiskDialog from '/src/l12/site/AdminRiskActionDialog.vue'
import FilterSheet from '/src/l12/site/MobileFilterSheet.vue'
createApp({setup(){
 const selected=ref(false),risk=ref(false),filter=ref(false),busy=ref(false),value=ref(''),error=ref('')
 window.__ui={clicks:0,submits:0,cancels:0,resets:0}
 window.__ui.setBusy=v=>busy.value=v
 window.__ui.setError=v=>error.value=v
 return()=>h('div',{class:'site-shell'},h('main',{class:'site-content ui-fixture ui-state-scope'},[
  h('h1','六类组件状态验收'),
  h('div',{class:'ui-action-row'},[
   h(UiButton,{selected:selected.value,onClick:()=>selected.value=!selected.value},()=> '选择'),
   h(UiButton,{tone:'primary',onClick:()=>window.__ui.clicks++},()=> '普通确认'),
   h(UiButton,{tone:'danger',disabled:true,onClick:()=>window.__ui.clicks++},()=> '不可用危险动作'),
   h(UiButton,{busy:true,onClick:()=>window.__ui.clicks++},()=> '处理中…'),
   h(UiButton,{tone:'warning'},()=> '同级按钮的极长名称需要完整换行显示而不能把相邻按钮推离容器'),
  ]),
  h('form',{onSubmit:e=>{e.preventDefault();window.__ui.submits++}},[
   h('label',['输入',h('input',{required:true,value:value.value,onInput:e=>value.value=e.target.value})]),
   h(UiButton,{type:'submit',tone:'primary'},()=> '提交表单'),
   h('label',['错误输入',h('input',{'aria-invalid':true,'aria-describedby':'field-error'})]),
   h('span',{id:'field-error'},'内容格式不正确'),
   h('label',['禁用输入',h('input',{disabled:true,value:'不可修改'})]),
  ]),
  h('a',{href:'#ui-panel'},'键盘可达的普通链接'),
  h('article',{id:'ui-panel',class:'ui-card',tabindex:0,'aria-selected':selected.value},[h('h2','卡片容器'),h('p','保留内容比例，不固定卡片高度。')]),
  ...['info','success','warning','error','empty'].map(kind=>h(UiNotice,{kind},()=>kind==='empty'?'暂无内容':'本次操作状态 '+kind)),
  h(UiButton,{id:'risk-trigger',onClick:()=>risk.value=true},()=> '打开危险操作'),
  h(FilterSheet,{modelValue:filter.value,'onUpdate:modelValue':v=>filter.value=v,alwaysVisible:true,title:'筛选条件',onReset:()=>window.__ui.resets++},()=>[
   h('label',['类型',h('select',[h('option','全部'),h('option','军团')])]),
   h('p','很长的筛选说明 '.repeat(120)),
  ]),
  risk.value?h(RiskDialog,{title:'删除对象',impact:'删除后无法继续使用此对象。',busy:busy.value,error:error.value,confirmLabel:'确认删除',onCancel:()=>{risk.value=false;window.__ui.cancels++},onConfirm:()=>window.__ui.clicks++}):null,
 ]))
}}).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(root, '.tmp/vite-ui-states'), server: {host:'127.0.0.1',port:0}, plugins:[{
 name:'ui-states-fixture',resolveId(id){if(id==='/__ui_states__.js')return id},load(id){if(id==='/__ui_states__.js')return entry},
 configureServer(s){s.middlewares.use((request,response,next)=>{if(request.url==='/__ui_states__'){
  response.setHeader('Content-Type','text/html');response.end('<style>html,body{margin:0}.ui-fixture{box-sizing:border-box;max-width:1100px;padding:20px;margin:auto;display:grid;gap:16px}.ui-fixture form{display:grid;gap:10px}.ui-fixture label{display:grid;gap:6px}.ui-fixture input{padding:10px}.ui-fixture .ui-card{padding:16px} .battle-sentinel{padding:12px}.battle-sentinel button{opacity:.45;background:#492f31;color:#eee;border:1px solid #895158}</style><div id="app"></div><div class="site-shell"><main class="site-content"><section class="battle-sentinel"><button disabled>未迁移沙盒禁用态</button></section></main></div><script type="module" src="/__ui_states__.js"></script>');return}next()})},
}]})
await server.listen()
const address=server.httpServer.address(), origin=`http://127.0.0.1:${address.port}`
const viewports=[{width:320,height:568},{width:360,height:640},{width:390,height:844},{width:430,height:932},{width:700,height:768},{width:701,height:768},{width:844,height:390},{width:1440,height:900},{width:2560,height:1440}]
let browser
const report=[]
const fits = async (page, selector) => page.locator(selector).evaluateAll(elements=>elements.every(element=>element.scrollWidth<=element.clientWidth+2))
try{
 browser=await chromium.launch({headless:true,channel:'msedge'})
 for(const viewport of viewports){
  const page=await browser.newPage({viewport}), errors=[]
  page.on('pageerror',e=>errors.push(e.message))
  await page.goto(`${origin}/__ui_states__`)
  await page.getByRole('heading',{name:'六类组件状态验收'}).waitFor()
  const isolation=await page.locator('.battle-sentinel button').evaluate(e=>{
   const paint=()=>{const s=getComputedStyle(e);return{opacity:s.opacity,background:s.backgroundColor,border:s.borderColor}}
   const current=paint(),removed=[]
   for(const sheet of document.styleSheets){for(let i=sheet.cssRules.length-1;i>=0;i--){const rule=sheet.cssRules[i];if(rule.name==='l12-ui-states'){removed.push({sheet,index:i,text:rule.cssText});sheet.deleteRule(i)}}}
   const baseline=paint()
   for(const rule of removed.reverse())rule.sheet.insertRule(rule.text,rule.index)
   return{current,baseline}
  })
  assert.deepEqual(isolation.current,isolation.baseline,'opt-in state layer cannot repaint non-migrated sandbox controls')
  const buttons=page.locator('.ui-action-row>.ui-button')
  const sizes=await buttons.evaluateAll(elements=>elements.map(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height})))
  assert(sizes.every(s=>Math.abs(s.height-sizes[0].height)<1 && Math.abs(s.width-sizes[0].width)<1),'parallel controls keep equal dimensions')
  assert(await fits(page,'.ui-button'),'button text fits')
  assert.equal(await page.getByRole('button',{name:'普通确认',exact:true}).getAttribute('aria-pressed'),null,'non-toggle buttons are not announced as toggles')
  await page.getByRole('button',{name:'选择',exact:true}).click()
  assert.equal(await page.getByRole('button',{name:'选择',exact:true}).getAttribute('aria-pressed'),'true')
  assert.equal(await page.locator('.ui-card').getAttribute('aria-selected'),'true')
  const unavailable=page.getByRole('button',{name:'不可用危险动作',exact:true})
  const unavailableBounds=await unavailable.boundingBox()
  await page.mouse.click(unavailableBounds.x+unavailableBounds.width/2,unavailableBounds.y+unavailableBounds.height/2)
  assert.equal(await page.evaluate(()=>window.__ui.clicks),0,'native disabled guard prevents action')
  const disabledPaint=await unavailable.evaluate(e=>{const s=getComputedStyle(e);return{background:s.backgroundColor,color:s.color,opacity:s.opacity,cursor:s.cursor}})
  assert.equal(disabledPaint.opacity,'1');assert.equal(disabledPaint.cursor,'not-allowed')
  assert.equal(disabledPaint.background,'rgb(21, 29, 34)','disabled paint wins over danger')
  assert.equal(await page.locator('[data-ui-kind="error"]').getAttribute('role'),'alert')
  assert.equal(await page.locator('[data-ui-kind="empty"]').getAttribute('role'),null,'empty state does not shout on each rerender')
  const link=page.getByRole('link',{name:'键盘可达的普通链接'})
  await link.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab')
  const focus=await link.evaluate(e=>{const s=getComputedStyle(e);return{outline:s.outlineWidth,color:s.outlineColor}})
  assert.equal(focus.outline,'2px');assert.equal(focus.color,'rgb(85, 199, 206)')
  await page.locator('input[required]').fill('测试输入');await page.locator('input[required]').press('Enter')
  assert.equal(await page.evaluate(()=>window.__ui.submits),1,'native form submit remains single')
  await page.getByRole('button',{name:'打开危险操作',exact:true}).click()
  const dialog=page.getByRole('dialog',{name:'删除对象'}),cancel=dialog.getByRole('button',{name:'取消',exact:true}),confirm=dialog.getByRole('button',{name:'确认删除',exact:true})
  await dialog.waitFor();await cancel.focus();await page.keyboard.press('Shift+Tab')
  assert(await confirm.evaluate(e=>e===document.activeElement),'native modal focus wraps backward')
  await page.keyboard.press('Tab');assert(await cancel.evaluate(e=>e===document.activeElement),'modal focus wraps forward')
  const pair=await dialog.locator('footer button').evaluateAll(elements=>elements.map(e=>e.getBoundingClientRect().height))
  assert(Math.abs(pair[0]-pair[1])<1,'danger peers equal heights')
  await page.evaluate(()=>window.__ui.setError('删除失败，请重试。'))
  await dialog.getByRole('alert').waitFor()
  await page.evaluate(()=>window.__ui.setBusy(true))
  await page.getByRole('button',{name:'正在执行…'}).waitFor()
  await page.keyboard.press('Escape');assert(await dialog.isVisible(),'busy modal cannot cancel')
  await page.evaluate(()=>window.__ui.setBusy(false))
  await page.screenshot({path:path.join(output,`risk-${viewport.width}x${viewport.height}.png`)})
  await page.keyboard.press('Escape');await dialog.waitFor({state:'hidden'})
  assert(await page.locator('#risk-trigger').evaluate(e=>e===document.activeElement),'modal restores trigger focus')
  await page.getByRole('button',{name:'筛选',exact:true}).click()
  const filter=page.getByRole('dialog',{name:'筛选条件'})
  await filter.getByRole('button',{name:'关闭筛选'}).focus();await page.keyboard.press('Shift+Tab')
  assert(await filter.getByRole('button',{name:'查看结果'}).evaluate(e=>e===document.activeElement),'teleported filter traps focus')
  await page.keyboard.press('Tab');assert(await filter.getByRole('button',{name:'关闭筛选'}).evaluate(e=>e===document.activeElement))
  await filter.getByRole('button',{name:'重置',exact:true}).click();assert.equal(await page.evaluate(()=>window.__ui.resets),1)
  const bounds=await filter.locator('.mobile-filter-sheet').boundingBox()
  assert(bounds.x>=-1&&bounds.y>=-1&&bounds.x+bounds.width<=viewport.width+1&&bounds.y+bounds.height<=viewport.height+1,'filter stays in viewport')
  assert(await fits(page,'.mobile-filter-sheet'),'filter content cannot overflow horizontally')
  await page.screenshot({path:path.join(output,`filter-${viewport.width}x${viewport.height}.png`)})
  await page.keyboard.press('Escape');await filter.waitFor({state:'hidden'})
  assert(await page.getByRole('button',{name:'筛选',exact:true}).evaluate(e=>e===document.activeElement))
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1),false,'no viewport horizontal overflow')
  assert.deepEqual(errors,[])
  report.push({viewport,sizes,disabledPaint,focus,errors})
  await page.screenshot({path:path.join(output,`states-${viewport.width}x${viewport.height}.png`),fullPage:true})
  await page.close()
 }
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify({status:'passed',report},null,2))
 console.log(JSON.stringify({status:'passed',viewports:report.length,output}))
}finally{await browser?.close();await server.close()}
