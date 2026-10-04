import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import './test-site-ui-state-scope.mjs'

const root=path.resolve(import.meta.dirname,'..')
const require=createRequire(import.meta.url)
const {createSSRApp,h}=require('vue')
const {renderToString}=require('@vue/server-renderer')
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp/vite-ui-primitives'),optimizeDeps:{noDiscovery:true,entries:[]},server:{middlewareMode:true}})
let checks=0
const ok=(condition,message)=>{assert(condition,message);checks++}
try{
 const Button=(await server.ssrLoadModule('/src/l12/site/UiButton.vue')).default
 const Notice=(await server.ssrLoadModule('/src/l12/site/UiNotice.vue')).default
 const render=(component,props,slots)=>renderToString(createSSRApp({render:()=>h(component,props,slots)}))
 const normal=await render(Button,{id:'native-fallthrough',title:'操作'},()=> '确认')
 ok(normal.includes('type="button"'),'default button does not implicitly submit')
 ok(normal.includes('id="native-fallthrough"')&&normal.includes('title="操作"'),'native attributes survive component boundary')
 ok(!normal.includes('aria-pressed')&&!normal.includes('disabled'),'non-toggle normal action has no false toggle or disabled semantics')
 const submit=await render(Button,{type:'submit',tone:'primary'},()=> '提交')
 ok(submit.includes('type="submit"')&&submit.includes('data-ui-tone="primary"'),'explicit form and primary intent remain')
 for(const selected of [true,false]){
  const html=await render(Button,{selected},()=> '切换')
  ok(html.includes(`aria-pressed="${selected}"`),'toggle state remains explicit')
 }
 for(const props of [{disabled:true,tone:'danger'},{busy:true,tone:'primary'}]){
  const html=await render(Button,props,()=> '不可执行')
  ok(/\sdisabled(?:\s|>)/.test(html),'unavailable and busy use native disabled')
 }
 const busy=await render(Button,{busy:true},()=> '处理中')
 ok(busy.includes('aria-busy="true"'),'busy announced on actual control')
 for(const kind of ['info','success','warning','error','empty']){
  const html=await render(Notice,{kind},{default:()=> '状态',action:()=>h(Button,null,()=> '重试')})
  ok(html.includes(`data-ui-kind="${kind}"`)&&html.includes('ui-notice-action'),'kind and recovery action survive')
  ok(kind==='empty'? !html.includes('role=')&&!html.includes('aria-live=')
   : html.includes(`role="${kind==='error'?'alert':'status'}"`),'empty is quiet and error is alert')
 }
 const css=fs.readFileSync(path.join(root,'src/l12/site/uiSystem.css'),'utf8')
 ok(!/\.(?:board-stage|felt-board|board-player-clock|l12-hand|deck-editor)\s*\{/.test(css),'site primitives do not introduce board geometry overrides')
 ok(css.includes('@layer l12-ui-states')&&css.includes('.ui-button:disabled'),'unavailable state has a dedicated bounded priority layer')
 console.log(`UI primitives passed: ${checks}/${checks}`)
}finally{await server.close()}
