import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {execFileSync} from 'node:child_process'
import {createRequire} from 'node:module'
import {createHash} from 'node:crypto'
import {createServer} from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

const front=path.resolve(import.meta.dirname,'..'),repo=path.dirname(front)
const output=process.env.L12_QA_OUT,cache=process.env.L12_QA_CACHE
assert(output&&cache&&path.isAbsolute(output)&&path.isAbsolute(cache))
const q=value=>`'${value.replaceAll("'","''")}'`
execFileSync(process.env.L12_PWSH||'pwsh.exe',['-NoProfile','-Command',`. ${q(path.join(repo,'ops/windows/L12GeneratedStorage.ps1'))};Assert-L12GeneratedPath -Path ${q(output)}|Out-Null;Assert-L12GeneratedPath -Path ${q(cache)}|Out-Null`])
fs.mkdirSync(output,{recursive:true});fs.mkdirSync(cache,{recursive:true})
const require=createRequire(import.meta.url)
const {chromium}=require(process.env.L12_PLAYWRIGHT||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const entry=`
import {createApp,defineComponent,h,ref} from 'vue'
import '/src/style.css'
import BattleEventLog from '/src/l12/game/BattleEventLog.vue'
import {applyAudioPreferences} from '/src/l12/audioPreferences.ts'
const card={instanceId:'synthetic-a',cardId:'SYNTHETIC',name:'合成军团名称用于换行检查',cardType:'legion',faction:'秩序',cost:2,baseTroops:4000,troops:4000,disasterLevel:0,hidden:false}
const events=Array.from({length:18},(_,index)=>({sequence:index+1,type:'put',text:'登场',cards:[card],playerIndex:index%2,effectText:'每当此军团进攻时，获得兵力加成。'.repeat(4)}))
events.push({sequence:20,type:'attack',text:'进攻',cards:[card],playerIndex:0,playerCombat:{combatId:'synthetic-combat',eventKind:'attack',outcomeCode:'declared',attackerTroops:4000}})
events.push({sequence:21,type:'defense',text:'未抵挡',cards:[],playerIndex:1,playerCombat:{combatId:'synthetic-combat',eventKind:'defense',outcomeCode:'unblocked',masterDamage:1}})
const frozen=JSON.stringify(events);let focus=0
window.__fontQa={unchanged:()=>JSON.stringify(events)===frozen,focus:()=>focus}
createApp(defineComponent({setup(){const font=ref('standard');return()=>h('main',{style:'width:min(330px,calc(100vw - 24px));margin:12px auto;color:#eee'},[
 h('label',['正文文字 ',h('select',{value:font.value,onChange:event=>{font.value=event.target.value;applyAudioPreferences({fontSize:font.value})}},['standard','large','extra-large'].map(value=>h('option',{value},value)))]),
 h('div',{style:'height:260px;display:flex;margin-top:12px;min-height:0;--l12-board-copy:13px'},[h(BattleEventLog,{events,you:0,names:['合成甲','合成乙'],onFocus:()=>focus++})])
])}})).mount('#app')`
const server=await createServer({configFile:false,root:front,cacheDir:path.join(cache,'vite'),plugins:[vue(),tailwindcss(),{
 name:'battle-log-font-qa',resolveId(id){if(id==='/__font-entry.js')return '\0font-entry'},load(id){if(id==='\0font-entry')return entry},configureServer(instance){instance.middlewares.use((request,response,next)=>{if(request.url==='/__font-qa'){response.setHeader('content-type','text/html');response.end('<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/__font-entry.js"></script>');return}next()})}
}],resolve:{alias:{'@':path.join(front,'src')}},server:{host:'127.0.0.1',port:0},logLevel:'warn'})
let browser
const report={schema:1,status:'RUNNING',scope:'actual BattleEventLog SFC and real preference API; synthetic events, not full board/OS device',checks:[],errors:[],sourceSha256:{}}
for(const leaf of ['src/l12/game/BattleEventLog.vue','src/l12/site/L12SettingsModal.vue','scripts/test-font-size-preferences.mjs','scripts/verify-battle-log-fonts.mjs'])report.sourceSha256[leaf]=createHash('sha256').update(fs.readFileSync(path.join(front,leaf))).digest('hex')
try{
 await server.listen();browser=await chromium.launchPersistentContext(path.join(cache,'profile'),{channel:'msedge',headless:true})
 await browser.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
 const page=await browser.newPage();page.on('pageerror',error=>report.errors.push(error.message))
 for(const viewport of [{width:1366,height:900},{width:390,height:844},{width:844,height:390}]){
  await page.setViewportSize(viewport);await page.goto(server.resolvedUrls.local[0]+'__font-qa')
  await page.locator('.event-message').first().waitFor()
  const sizes=[]
  for(const [value,multiplier]of [['standard',1],['large',1.12],['extra-large',1.24]]){
   await page.getByRole('combobox').selectOption(value)
   const facts=await page.evaluate(()=>{
    const body=document.querySelector('.event-message'),combat=document.querySelector('.combat-cards'),toggle=document.querySelector('.combat-toggle'),list=document.querySelector('.event-list')
    const overflow=[...document.querySelectorAll('.event-message,.combat-cards')].some(element=>element.scrollWidth>element.clientWidth+1)
    list.scrollTop=list.scrollHeight
    const box=list.getBoundingClientRect(),last=list.lastElementChild.getBoundingClientRect()
    return{body:parseFloat(getComputedStyle(body).fontSize),combat:parseFloat(getComputedStyle(combat).fontSize),toggle:parseFloat(getComputedStyle(toggle).fontSize),overflow,lastReachable:last.bottom<=box.bottom+1&&last.top<box.bottom}
   })
   assert(Math.abs(facts.body-13*multiplier)<.02);assert(Math.abs(facts.combat-13*multiplier)<.02);assert.equal(facts.toggle,13)
   assert(!facts.overflow&&facts.lastReachable);assert(await page.evaluate(()=>window.__fontQa.unchanged()))
   sizes.push({value,...facts})
  }
  await page.locator('.combat-toggle').click()
  const link=page.locator('.combat-cards .log-card-link').first();await link.scrollIntoViewIfNeeded();await link.click()
  assert.equal(await page.evaluate(()=>window.__fontQa.focus()),1)
  await page.screenshot({path:path.join(output,'log-font-'+viewport.width+'x'+viewport.height+'.png')})
  report.checks.push({...viewport,sizes,detailsAndCardFocus:true})
 }
 assert.deepEqual(report.errors,[]);report.status='PASSED'
}catch(error){report.status='FAILED';report.error=String(error.stack||error);throw error}
finally{if(browser)await browser.close();await server.close();report.closed=true;fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2)+'\n')}
console.log('Actual battle log font acceptance passed: 3 sizes x 3 tiers, no clipping, stable controls, reachable final row and card focus')
