import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import {fileURLToPath} from 'node:url'
import {createRequire} from 'node:module'
import {createServer} from 'vite'

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..')
const {chromium}=createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out=process.env.L12_QA_OUT||'D:/GPT/Legion12/artifacts/batch263-prompts'
fs.mkdirSync(out,{recursive:true})
const entry=`
import {createApp,h,reactive,markRaw,ref} from 'vue'
import PromptOverlay from '/src/l12/game/PromptOverlay.vue'
import {l12State} from '/src/l12/net.ts'
import '/src/style.css'
const player=i=>({playerIndex:i,name:i?'对方':'我方',mulliganDone:true,hand:[],field:[[null,null,null],[null,null,null]],graveyard:[],morale:[],master:{masterId:'',masterName:'合成主宰',hp:10,maxHp:10}})
const game=reactive({matchId:'synthetic-prompts',roomCode:'TEST',you:0,activePlayer:0,firstPlayer:0,phase:'Main',initiativeRolls:[3,2],players:[player(0),player(1)],prompts:[],recentEvents:[]})
window.__sent=[];window.__focus=[]
l12State.socket=markRaw({readyState:1,send:value=>window.__sent.push(JSON.parse(value))})
window.__set=(prompt,cards=[])=>{l12State.pendingAction=false;game.players[0].graveyard=cards;game.prompts=[prompt]}
window.__pending=value=>l12State.pendingAction=value
const inspector=ref(false);window.__inspector=value=>inspector.value=value
createApp({render:()=>h(PromptOverlay,{game,inspectorVisible:inspector.value,onFocusCard:card=>window.__focus.push(card.instanceId)})}).mount('#app')
`
const server=await createServer({root,server:{host:'127.0.0.1',port:0},plugins:[{
 name:'prompt-fixture',resolveId(id){if(id==='/__prompt__.js')return id},load(id){if(id==='/__prompt__.js')return entry},
 configureServer(s){s.middlewares.use((req,res,next)=>{if(req.url==='/__prompt__'){res.setHeader('Content-Type','text/html');res.end('<div id="app"></div><script type="module" src="/__prompt__.js"></script>');return}next()})}
}]})
let browser
try{
 await server.listen();browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage({viewport:{width:1280,height:900}})
 const errors=[];page.on('pageerror',e=>errors.push(e.message))
 await page.route('**/*',route=>{
  const url=new URL(route.request().url())
  if(url.hostname!=='127.0.0.1')return route.abort()
  if(url.pathname.startsWith('/api/'))return route.fulfill({json:[]})
  if(url.pathname.endsWith('manifest.json'))return route.fulfill({json:{schemaVersion:3,cards:{}}})
  return route.continue()
 })
 await page.goto('http://127.0.0.1:'+server.httpServer.address().port+'/__prompt__')
 await page.waitForFunction(()=>Boolean(window.__set))
 let seq=0
 const cards=n=>Array.from({length:n},(_,i)=>({instanceId:'guard-'+i,cardId:'',name:'陵墓守卫 '+(i+1),cardType:'军团',effectText:'合成测试卡',imageUrl:'data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="140"><rect width="100" height="140" fill="#8d702b"/><text x="18" y="72" fill="white">L12</text></svg>')}))
 const set=async(p,c=[])=>{
  const prompt={promptId:'fixture-'+(++seq),playerIndex:0,kind:'card',text:'太阳城阵营效果：将1张<陵墓守卫>从我方墓地活跃登场。',validChoices:[],minChoose:1,maxChoose:1,choiceLabels:{},data:{},activationId:'activation-1',sourceInstanceId:'source-1',sourceCardId:'S01-02C1',step:2,createdRevision:12,controller:0,...p}
  await page.evaluate(({prompt,c})=>window.__set(prompt,c),{prompt,c})
  await page.locator('.prompt-panel').waitFor();return prompt
 }
 const checkBounds=async()=>{
  const b=await page.locator('.prompt-panel').boundingBox();assert(b.x>=-1&&b.y>=-1&&b.x+b.width<=(await page.viewportSize()).width+1)
  assert(await page.locator('.prompt-panel').evaluate(e=>e.scrollWidth<=e.clientWidth+1),'Panel must not overflow horizontally')
 }
 const candidateGroupCentered=async()=>{
  const row=await page.locator('.prompt-choices').boundingBox(),items=await page.locator('.prompt-choices .prompt-card-candidate').all()
  const first=await items[0].boundingBox(),last=await items.at(-1).boundingBox()
  assert(Math.abs((first.x+last.x+last.width)/2-(row.x+row.width/2))<3,'Short card group must be centered')
 }
 const c=cards(3),ids=c.map(x=>x.instanceId)
 await set({validChoices:[...ids,'no'],choiceLabels:{no:'不发动'},data:{displayCardIds:ids.join('|'),cardSelection:'true'}},c)
 await candidateGroupCentered();await checkBounds()
 const decline=page.getByRole('button',{name:'不发动',exact:true}),confirm=page.locator('.prompt-confirm-choice')
 const a=await decline.boundingBox(),b=await confirm.boundingBox()
 assert(a.x<b.x&&Math.abs(a.y-b.y)<2);assert.equal(a.width,b.width);assert.equal(a.height,b.height);assert(a.height>=44)
 await page.screenshot({path:path.join(out,'cards-desktop.png')})
 await decline.click();assert(!(await confirm.isDisabled()));await confirm.click()
 const sent=await page.evaluate(()=>window.__sent.at(-1).command)
 assert.deepEqual(sent.cardInstanceIds,['no']);assert.equal(sent.promptId,'fixture-1');assert.equal(sent.activationId,'activation-1');assert.equal(sent.createdRevision,12)
 assert(await decline.isDisabled());assert(await confirm.isDisabled())
 await set({kind:'response',text:'是否响应堆叠顶部：传奇的拉格纳 - 登场时 若我方主宰血量不高于7，获得冲锋。',validChoices:['guard-0','pass'],choiceLabels:{pass:'不响应'},data:{choiceMode:'instant'}},cards(1))
 await candidateGroupCentered();await page.getByRole('button',{name:'不响应',exact:true}).click()
 assert.deepEqual(await page.evaluate(()=>window.__sent.at(-1).command.cardInstanceIds),['pass'])
 await page.screenshot({path:path.join(out,'response-desktop.png')})
 await set({kind:'option',text:'乔泽',validChoices:['yes','no'],choiceLabels:{yes:'发动',no:'不发动'},data:{uiPattern:'effect-decision',sourceName:'乔泽',effectText:'登场时 可弃置我方战场上1张军团：选择对方1张军团本回合兵力-2000。'}})
 assert((await page.locator('.effect-decision-text').textContent()).includes('本回合兵力-2000'))
 const options=page.locator('.prompt-choices>button');assert.equal(await options.count(),2)
 const yesBox=await options.nth(0).boundingBox(),noBox=await options.nth(1).boundingBox()
 assert(Math.abs(yesBox.y-noBox.y)<2,'Two decision choices must share one centered desktop row')
 assert.equal(yesBox.height,noBox.height)
 await page.screenshot({path:path.join(out,'effect-desktop.png')})
 await set({kind:'option',text:'墓地费用：第1张《渴求死亡的勇士》本次视为几张卡牌',validChoices:['one','two','three'],choiceLabels:{one:'视为1张',two:'视为2张',three:'视为3张'}})
 const rowOptions=page.locator('.prompt-choices>button')
 assert.equal(await rowOptions.count(),3)
 const left=await rowOptions.first().boundingBox(),right=await rowOptions.last().boundingBox(),optionRow=await page.locator('.prompt-choices').boundingBox()
 assert(Math.abs((left.x+right.x+right.width)/2-(optionRow.x+optionRow.width/2))<3)
 await rowOptions.nth(1).click();assert(!(await confirm.isDisabled()))
 await set({kind:'option',text:'墓地费用：第2张《渴求死亡的勇士》本次视为几张卡牌',validChoices:['one','two','three'],choiceLabels:{one:'视为1张',two:'视为2张',three:'视为3张'}})
 assert(await confirm.isDisabled(),'New entity prompt must not reuse the preceding selection')
 await page.screenshot({path:path.join(out,'individual-count-desktop.png')})
 for(const width of [760,390]){
  await page.setViewportSize({width,height:900})
  await set({validChoices:[...ids,'no'],choiceLabels:{no:'不发动'},data:{displayCardIds:ids.join('|'),cardSelection:'true'}},c)
  await checkBounds()
  const row=page.locator('.prompt-choices'),first=page.locator('.prompt-card-candidate').first()
  const r=await row.boundingBox(),f=await first.boundingBox();assert(f.x>=r.x-1,'Overflow must preserve access to first card')
  await row.evaluate(e=>e.scrollLeft=e.scrollWidth)
  const last=await page.locator('.prompt-card-candidate').last().boundingBox();assert(last.x+last.width<=r.x+r.width+1)
  await page.screenshot({path:path.join(out,'cards-'+width+'.png')})
 }
 await page.setViewportSize({width:1280,height:900})
 const many=cards(15)
 await set({validChoices:many.map(x=>x.instanceId),data:{cardSelection:'true'}},many)
 await checkBounds();assert(await page.locator('.prompt-choices').evaluate(e=>e.scrollWidth>e.clientWidth))
 await page.getByRole('button',{name:'最小化弹框',exact:true}).click();await page.getByRole('button',{name:/展开：/}).click()
 assert.equal(await page.locator('.prompt-card-candidate').count(),15)
 for(const viewport of [{width:760,height:480},{width:390,height:540}]){
  await page.setViewportSize(viewport);await page.evaluate(()=>window.__inspector(true))
  await set({kind:'option',text:'合成多段效果',validChoices:['yes','no'],choiceLabels:{yes:'发动',no:'不发动'},data:{uiPattern:'effect-decision',sourceName:'合成来源',effectText:Array(12).fill('登场时 可选择另外1张军团进行1格位移，随后结算本次效果。').join('\n')}})
  await checkBounds()
  assert(await page.locator('.prompt-panel').evaluate(e=>e.scrollHeight<=e.clientHeight+1||['auto','scroll'].includes(getComputedStyle(e).overflowY)),'Tall full text must remain scrollable')
  await page.locator('.prompt-panel').evaluate(e=>e.scrollTop=e.scrollHeight)
  const footer=await page.locator('.prompt-action-footer').boundingBox();assert(footer.y+footer.height<=viewport.height+1,'Footer remains reachable in short viewport')
  await page.screenshot({path:path.join(out,'long-text-'+viewport.width+'.png')})
 }
 assert.deepEqual(errors,[])
 fs.writeFileSync(path.join(out,'result.json'),JSON.stringify({passed:true,views:[1280,760,390],errors},null,2))
 console.log('Prompt UI passed: candidate centering, safe overflow, equal footer actions, bound commands, pending lock, response, full text, per-entity reset, minimize, narrow/short viewport and inspector layouts.')
}finally{await browser?.close();await server.close()}
