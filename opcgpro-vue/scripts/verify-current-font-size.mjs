import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import { buildBattlePreviewDocument } from './lib/battlePreviewDocument.mjs'

const root = path.resolve(import.meta.dirname, '..')
const output = process.env.L12_FONT_UI_OUTPUT || 'D:/GPT/Legion12/artifacts/font-size-preferences-20261020/ui/r1'
const cacheRoot = process.env.L12_FONT_UI_CACHE || 'D:/GPT/Legion12/cache/primary/font-size-preferences-20261020/ui'
const profile = path.join(cacheRoot, path.basename(output))
const envResult = execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile','-NonInteractive','-Command',
  '. $env:L12_FONT_GUARD; $physical=@{}; foreach($name in @("L12_FONT_UI_OUTPUT","L12_FONT_UI_CACHE","L12_FONT_PROFILE")){$physical[$name]=Assert-L12GeneratedPath ([Environment]::GetEnvironmentVariable($name)) -Purpose "font UI"}; '
  + '. $env:L12_FONT_INIT -CacheRoot $env:L12_FONT_UI_CACHE|Out-Null; $null=Assert-L12GeneratedPath $env:TEMP -Purpose "font UI temp"; '
  + '[Console]::WriteLine("FONT_ENV="+(@{physical=$physical;TEMP=$env:TEMP;TMP=$env:TMP;TMPDIR=$env:TMPDIR;L12_PWSH=$env:L12_PWSH}|ConvertTo-Json -Compress))'],
  {encoding:'utf8',windowsHide:true,env:{...process.env,L12_FONT_UI_OUTPUT:output,L12_FONT_UI_CACHE:cacheRoot,L12_FONT_PROFILE:profile,
    L12_FONT_GUARD:path.resolve(root,'../ops/windows/L12GeneratedStorage.ps1'),L12_FONT_INIT:path.resolve(root,'../ops/windows/Initialize-L12BuildEnvironment.ps1')}})
const placement=JSON.parse(envResult.split(/\r?\n/).find(line=>line.startsWith('FONT_ENV=')).slice(9))
for(const name of ['TEMP','TMP','TMPDIR','L12_PWSH'])process.env[name]=placement[name]
assert(!fs.existsSync(output)&&!fs.existsSync(profile),'Do not overwrite evidence or reuse a browser profile')
fs.mkdirSync(output,{recursive:true})
const sha=value=>createHash('sha256').update(value).digest('hex').toUpperCase()
const inputs=['src/App.vue','src/l12/GamePage.vue','src/l12/game/GameBoard.vue','src/l12/game/PromptOverlay.vue','src/l12/CardDetailContent.vue','src/l12/site/L12SettingsModal.vue','src/l12/audioPreferences.ts','src/style.css','src/l12/mobileViewport.css','scripts/verify-current-hand-card-size.mjs','scripts/verify-current-font-size.mjs']
const hashes=()=>Object.fromEntries(inputs.map(file=>[file,sha(fs.readFileSync(path.join(root,file)))]))
const report={schema:1,status:'running',placement,output,cacheRoot,profile,sourceHashes:hashes(),cases:[],pageErrors:[],browserClosed:true,serverClosed:false,
  scope:'Actual App/history /game + Settings + GamePage + CardDetailContent + PromptOverlay. Synthetic public known-hand/read-model fixtures; no live WS/ACK, physical phone, account sync or card-art claim. No CSS injection, force clicks or direct preference writes.',
  baselineScope:'Only the exact global style.css from 1922c9cd is served as a separate source module; current SFC logic and fixture remain the same. This is typography A/B, not the entire old application.'}
const oldCss=execFileSync('git',['show','1922c9cdbee8307f4020c782dae00344e2088a40:opcgpro-vue/src/style.css'],{cwd:root,encoding:'utf8'})
report.baselineStyleSha256=sha(oldCss)
const previous=fs.readFileSync(path.join(root,'scripts/verify-batch253-visual.mjs'),'utf8')
const setup=previous.slice(previous.indexOf('const entry = `')+'const entry = `'.length,previous.indexOf('const isPicker='))
  .replace('createRouter,createMemoryHistory','createRouter,createWebHistory')
const hand=fs.readFileSync(path.join(root,'scripts/verify-current-hand-card-size.mjs'),'utf8')
const extra=hand.match(/const entry = setup \+ `([\s\S]*?)`\r?\nconst document/)
assert(extra,'Keep the existing actual App/history hand host boundary')
const enhance=`
const prose=players[0].hand.at(-1).effectText||'公开的卡牌效果正文。'
players[0].hand.at(-1).effectText=Array(12).fill(prose).join('\\n')
`
const entry=(setup+extra[1].replace('const initialGame=',enhance+'const initialGame='))+`
window.__fontProbe={showPrompt(){window.__setInspectorPrompt(true);const p=l12State.game.prompts[0];p.validChoices=['mode:one','mode:two'];p.choiceLabels={'mode:one':'执行第一段','mode:two':'执行第二段'};p.data.effectText=Array(3).fill(prose).join('\\n')},clearPrompt(){l12State.game.prompts=[];l12State.pendingAction=false;window.__sentCommands=[]}}
`
const html=buildBattlePreviewDocument(fs.readFileSync(path.join(root,'index.html'),'utf8'))
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(cacheRoot,'vite'),server:{host:'127.0.0.1',port:0},plugins:[{
  name:'current-font-size-consumers',resolveId(id){if(['/__l12_battle_preview__.js','/__font_baseline_entry__.js','/__font_baseline__.css'].includes(id))return id},
  load(id){if(id==='/__font_baseline__.css')return oldCss;if(id==='/__l12_battle_preview__.js')return entry;if(id==='/__font_baseline_entry__.js')return entry.replace("import '/src/style.css'", "import '/__font_baseline__.css'")},
  configureServer(vite){vite.middlewares.use((req,res,next)=>{if(req.url?.split('?')[0]==='/game'){res.setHeader('Content-Type','text/html');res.end(req.url.includes('baseline=1')?html.replace('/__l12_battle_preview__.js','/__font_baseline_entry__.js'):html);return}next()})},
}]})
const {chromium}=createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT||'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
let context,browser,deadline
const save=()=>fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2))
async function native(locator,touch=false){await locator.waitFor({state:'visible'});await locator.scrollIntoViewIfNeeded();assert(await locator.evaluate(el=>{const r=el.getBoundingClientRect(),p=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return Boolean(p&&el.contains(p))}),'Native target must be visible/unobstructed');if(touch)await locator.tap();else await locator.click()}
async function choose(page,size,restored=false){await native(page.getByRole('button',{name:'打开对局设置',exact:true}));const modal=page.locator('.l12-settings-modal'),select=modal.locator('label').filter({hasText:'正文文字'}).locator('select');await select.waitFor();if(restored)assert.equal(await select.inputValue(),size);else await select.selectOption(size);await page.waitForFunction(size=>document.documentElement.dataset.l12FontSize===size,size);await native(modal.getByRole('button',{name:'关闭设置',exact:true}));await modal.waitFor({state:'detached'});await page.waitForTimeout(350)}
async function detail(page,width){await native(page.locator('.l12-hand[data-player-index="0"] .hand-card-wrap').last().locator('.card-tile'),width<1000);if(width<1000&&!await page.locator('.mobile-card-inspector').isVisible())await native(page.getByRole('button',{name:'展开卡牌详情',exact:true}));const box=page.locator(width<1000?'.mobile-card-inspector':'.card-inspector').filter({visible:true}).first();await box.locator('.card-detail-copy .l12-effect-body').waitFor();return box}
async function metrics(page,box){return {font:await box.locator('.l12-effect-body').first().evaluate(el=>parseFloat(getComputedStyle(el).fontSize)),geometry:await page.evaluate(()=>{const rect=selector=>{const el=document.querySelector(selector),r=el?.getBoundingClientRect();return r?{width:r.width,height:r.height}:null};return {board:rect('.felt-board'),handCard:rect('.l12-hand[data-player-index="0"] .card-tile'),settingsButton:rect('[aria-label="打开对局设置"]'),rootFont:getComputedStyle(document.documentElement).fontSize}}),image:await box.locator('.archive-detail-image').evaluate(el=>{const r=el.getBoundingClientRect();return {width:r.width,height:r.height}})}}
async function scrollProof(page,text){const handle=await text.evaluateHandle(el=>{for(let p=el;p;p=p.parentElement){const s=getComputedStyle(p);if(/auto|scroll/.test(s.overflowY)&&p.scrollHeight>p.clientHeight+3)return p}return null});const el=handle.asElement();if(!el){await handle.dispose();return {needed:false}}const before=await el.evaluate(node=>node.scrollTop),r=await el.boundingBox();await page.mouse.move(r.x+r.width*.75,r.y+r.height*.7);await page.mouse.wheel(0,600);await page.waitForTimeout(150);const after=await el.evaluate(node=>node.scrollTop);await handle.dispose();assert(after>before,'Native wheel must actually scroll overflowing prose');return {needed:true,before,after}}
function sameGeometry(a,b){for(const key of ['board','handCard','settingsButton'])for(const axis of ['width','height'])assert(Math.abs(a.geometry[key][axis]-b.geometry[key][axis])<.8,`${key}/${axis} must not resize with font`);assert.equal(a.geometry.rootFont,b.geometry.rootFont);for(const axis of ['width','height'])assert(Math.abs(a.image[axis]-b.image[axis])<.8,'Card artwork must retain its dimensions')}
async function prompt(page,width){if(width<1000&&await page.locator('.mobile-card-inspector').isVisible())await native(page.locator('.mobile-card-inspector').getByRole('button',{name:'收起',exact:true}));await page.evaluate(()=>window.__fontProbe.showPrompt());const panel=page.locator('.prompt-panel');await panel.waitFor({state:'visible'});const font=await panel.locator('.effect-decision-text').evaluate(el=>parseFloat(getComputedStyle(el).fontSize));const scrolling=await scrollProof(page,panel.locator('.effect-decision-text'));await native(panel.getByRole('button',{name:'最小化弹框',exact:true}));await native(page.locator('.prompt-minimized-bar button'));await native(panel.locator('[data-tutorial-choice="mode:one"]'));const confirm=panel.locator('[data-tutorial-action="confirm-prompt"]');const outer=await confirm.evaluate(el=>{const r=el.getBoundingClientRect();return {width:r.width,height:r.height}});await native(confirm,width<1000);const sent=await page.evaluate(()=>window.__sentCommands);assert.equal(sent.length,1);assert.equal(sent[0].command.type,'resolvePrompt');assert.equal(sent[0].command.promptId,'inspector-prompt');await page.evaluate(()=>window.__fontProbe.clearPrompt());return {font,scrolling,confirmOuter:outer,sentType:sent[0].command.type,commands:sent.length}}
try{
  await server.listen();const origin=`http://127.0.0.1:${server.httpServer.address().port}`;report.origin=origin
  deadline=setTimeout(()=>{report.error='240 second deadline';void context?.close()},240000)
  for(const [width,height] of [[1366,768],[844,390]]){
    context=await chromium.launchPersistentContext(path.join(profile,String(width)),{headless:true,channel:'msedge',viewport:{width,height},hasTouch:width<1000,serviceWorkers:'block',env:process.env});browser=context.browser();report.browserClosed=false
    await context.addInitScript(()=>{if(!localStorage.getItem('l12-audio-preferences-v1'))localStorage.setItem('l12-audio-preferences-v1',JSON.stringify({musicEnabled:false,sfxEnabled:false}));localStorage.setItem('l12:official-presets:guest-seeded:v1','true')})
    await context.route('**/*',route=>{const url=new URL(route.request().url());if(url.origin!==origin)return route.abort();if(url.pathname.startsWith('/api/'))return route.fulfill({json:{cardRestrictions:{}}});if(url.pathname.endsWith('card-assets.manifest.json'))return route.fulfill({json:{schemaVersion:3,catalogVersion:'layout',assetVersion:'layout',basePath:'/',cards:{}}});return route.continue()})
    const page=context.pages()[0];page.setDefaultTimeout(7000);page.on('pageerror',error=>report.pageErrors.push(error.message))
    await page.goto(origin+'/game?baseline=1');await page.locator('.l12-hand[data-player-index="0"] .card-tile').last().waitFor();await page.waitForTimeout(600)
    const oldDetail=await detail(page,width),old=await metrics(page,oldDetail);old.prompt=await prompt(page,width);report.cases.push({width,baseline:true,old});save()
    await page.goto(origin+'/game');await page.locator('.l12-hand[data-player-index="0"] .card-tile').last().waitFor()
    let standard
    for(const [size,factor] of [['standard',1],['large',1.12],['extra-large',1.24]]){
      const item={width,height,size,status:'running'};report.cases.push(item)
      const instance=await page.locator('.game-page').elementHandle();await choose(page,size);assert(await instance.evaluate(el=>el.isConnected&&el===document.querySelector('.game-page')),'Setting must retain same GamePage instance');await instance.dispose()
      const box=await detail(page,width);item.detail=await metrics(page,box);item.scrolling=await scrollProof(page,box.locator('.l12-effect-body').first());item.screenshot=path.join(output,`font-${width}-${size}.png`);await page.screenshot({path:item.screenshot,fullPage:true});item.prompt=await prompt(page,width)
      if(size==='standard'){standard=item;assert(Math.abs(item.detail.font-old.font)<.01,'Standard detail font equals actual old source');assert(Math.abs(item.prompt.font-old.prompt.font)<.01,'Standard prompt font equals actual old source');sameGeometry(old,item.detail)}
      else {assert(Math.abs(item.detail.font/standard.detail.font-factor)<.01,'Detail font follows native setting');assert(Math.abs(item.prompt.font/standard.prompt.font-factor)<.01,'Prompt font follows native setting');sameGeometry(standard.detail,item.detail);assert.deepEqual(item.prompt.confirmOuter,standard.prompt.confirmOuter)}
      await page.reload();await page.locator('.l12-hand[data-player-index="0"] .card-tile').last().waitFor();await choose(page,size,true);const refreshed=await detail(page,width);item.refreshed=await metrics(page,refreshed);assert(Math.abs(item.refreshed.font-item.detail.font)<.01,'Refreshed font persists');item.status='passed';save()
      if(width<1000)await native(page.locator('.mobile-card-inspector').getByRole('button',{name:'收起',exact:true}))
    }
    await context.close();report.browserClosed=!browser.isConnected();assert(report.browserClosed);context=null
  }
  assert.deepEqual(report.pageErrors,[]);report.sourceHashesAfter=hashes();assert.deepEqual(report.sourceHashesAfter,report.sourceHashes);report.status='passed'
}catch(error){report.status='failed';report.error=String(error?.stack||error);await context?.pages()[0]?.screenshot({path:path.join(output,'failure.png'),fullPage:true}).catch(()=>{})}
finally{clearTimeout(deadline);if(context){await context.close().catch(()=>{});report.browserClosed=!browser?.isConnected()}await server.close();report.serverClosed=!server.httpServer?.listening;save()}
console.log(JSON.stringify({status:report.status,cases:report.cases.length,error:report.error,output,browserClosed:report.browserClosed,serverClosed:report.serverClosed},null,2));if(report.status!=='passed')process.exitCode=1
