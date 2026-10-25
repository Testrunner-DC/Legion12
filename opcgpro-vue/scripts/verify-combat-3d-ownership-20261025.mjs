// Real CombatMotion SFC, coordinator and logical viewport. Public synthetic
// facts isolate the presentation contract; this is not an engine/replay run.
import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import vue from '@vitejs/plugin-vue'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..'), repo = path.dirname(root)
const output = process.env.L12_COMBAT_3D_OUT
  || 'D:/GPT/Legion12/artifacts/combat-3d-ownership-20261025/browser-r1'
const cache = process.env.L12_VITE_CACHE_DIR || 'D:/GPT/Legion12/cache/primary/combat-3d-vite-20261025'
const pwsh = process.env.L12_PWSH
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
const quote = value => `'${String(value).replaceAll("'", "''")}'`
const placement = spawnSync(pwsh, ['-NoProfile','-NonInteractive','-Command',
  `$ErrorActionPreference='Stop'; . ${quote(path.join(repo,'ops/windows/L12GeneratedStorage.ps1'))}; `
  + [output,cache,process.env.TEMP,process.env.TMP,process.env.TMPDIR].map(value => {
    if (!value) throw new Error('Initialize governed build environment first')
    return `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'isolated Combat 3D acceptance';`
  }).join(' ')], {encoding:'utf8',timeout:8000,windowsHide:true})
if (placement.status !== 0) throw new Error(`Generated storage guard failed: ${placement.stderr || placement.stdout}`)
if (fs.existsSync(output)) throw new Error(`Preserve prior evidence: ${output}`)
fs.mkdirSync(output,{recursive:true})
const pinnedFiles = ['src/l12/game/CombatMotionPresentationLayer.vue',
  'scripts/test-combat-3d-ownership-20261025.mjs','scripts/verify-combat-3d-ownership-20261025.mjs',
  'src/l12/game/presentationSequenceCoordinator.ts','src/l12/game/authoritativeCardVisualSnapshot.ts','src/l12/game/visualTransitionProjection.ts',
  'src/l12/mobileViewport.ts','src/l12/mobileViewport.css','src/style.css',
  'src/l12/CardTile.vue','src/l12/CardImage.vue','src/l12/cardAssets.ts']
const pins = () => Object.fromEntries(pinnedFiles.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')]))
const sourceBefore = pins()
assert.match(process.env.L12_COMBAT_PRODUCT_SHA || '', /^[a-f0-9]{64}$/, 'explicit tested product SHA required')
assert.equal(sourceBefore[pinnedFiles[0]],process.env.L12_COMBAT_PRODUCT_SHA)
assert.match(process.env.L12_COMBAT_TEST_SHA || '', /^[a-f0-9]{64}$/, 'explicit tested regression SHA required')
assert.equal(sourceBefore[pinnedFiles[1]],process.env.L12_COMBAT_TEST_SHA)
const art = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="140"><rect x="2" y="2" width="96" height="136" rx="5" fill="#163e4b" stroke="#e6d3a5" stroke-width="3"/><path d="M12 110L50 16L88 110Z" fill="#ce8f42"/><circle cx="50" cy="73" r="16" fill="#151f30"/></svg>')
const entry = `
import { createApp, h, nextTick, ref } from 'vue'
import CombatMotionPresentationLayer from '/src/l12/game/CombatMotionPresentationLayer.vue'
import CardTile from '/src/l12/CardTile.vue'
import { createPresentationSequenceCoordinator } from '/src/l12/game/presentationSequenceCoordinator'
import { audioPreferences } from '/src/l12/audioPreferences'
import { useLandscapeViewport, viewportLayoutRect } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
audioPreferences.animation = 'standard'
audioPreferences.mobileLayout = 'auto'
const events=ref([]), revision=ref(1), paused=ref(false), synchronizing=ref(false), present=ref(true)
const coordinator=createPresentationSequenceCoordinator()
let componentDebug=null
let sequence=0, phase='idle', phaseStart=performance.now(), sampling=true
const authority=id=>document.querySelector('[data-l12-game-stage] .formation-slot [data-card-instance-id="'+id+'"]')
const ghosts=()=>[...document.querySelectorAll('.l12-combat-defeat-ghost')]
const visible=node=>{
  if(!(node instanceof HTMLElement))return false
  const style=getComputedStyle(node),rect=node.getBoundingClientRect()
  return style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity)>0&&rect.width>0&&rect.height>0
}
const angle=node=>{const m=new DOMMatrixReadOnly(getComputedStyle(node).transform);return(Math.atan2(m.b,m.a)*180/Math.PI+360)%360}
const publicCard=id=>({instanceId:id,cardId:'qa-public',name:'合成公开军团',cardType:'legion',faction:'qa',
  cost:4,baseTroops:5000,troops:5000,disasterLevel:0,summonRound:0,
  imageUrl:'/api/site/media/combat-3d-qa.svg',hidden:false,tapped:id==='unit'})
const roots=()=>[authority('unit'),authority('target'),...ghosts()].filter(Boolean)
const records=[],samples=[],seen=new WeakSet()
function readFrame(){
  const owners=id=>Number(visible(authority(id)))+ghosts().filter(node=>node.dataset.cardInstanceId===id&&visible(node)).length
  return {time:performance.now(),phase,elapsed:performance.now()-phaseStart,unitOwners:owners('unit'),targetOwners:owners('target'),
    ghosts:ghosts().length,canvases:document.querySelectorAll('.l12-combat-defeat-ghost canvas[data-l12-pixel-snapshot="true"]').length}
}
function sample(){
  samples.push(readFrame())
  for(const node of roots())for(const animation of node.getAnimations()){
    if(seen.has(animation))continue
    seen.add(animation)
    records.push({instanceId:node.dataset.cardInstanceId,kind:node.classList.contains('l12-combat-defeat-ghost')?'defeat':'survivor',
      duration:animation.effect.getTiming().duration,frames:animation.effect.getKeyframes(),time:performance.now()})
  }
  if(sampling)requestAnimationFrame(sample)
}
function mark(value){phase=value;phaseStart=performance.now()}
async function emit(text,{death=false,onlyLeave=false,hidden=false}={}){
  mark(death?'death':'survivor')
  const cards=[publicCard('unit'),publicCard('target')]
  const fresh=[]
  if(death){const card={...cards[1],hidden};fresh.push({sequence:++sequence,type:'leave',playerIndex:1,text:'合成公开军团阵亡',cards:[card]})}
  if(!onlyLeave)fresh.push({sequence:++sequence,type:'combat',playerIndex:0,text,cards})
  events.value=[...events.value,...fresh];if(death)present.value=false;revision.value++
  await nextTick()
}
function details(node){
  const animation=node?.getAnimations()[0]
  if(!animation||animation.currentTime<35||animation.currentTime>Math.min(360,animation.effect.getTiming().duration*.65))return null
  const style=getComputedStyle(node),canvas=node.querySelector('canvas[data-l12-pixel-snapshot="true"]')
  const face=node.querySelector('[data-l12-combat-animation-face="true"]'),faceStyle=face?getComputedStyle(face):null
  const rect=node.getBoundingClientRect(),pixels=canvas?.getBoundingClientRect()
  const rgba=canvas?[...canvas.getContext('2d').getImageData(Math.floor(canvas.width/2),Math.floor(canvas.height/2),1,1).data]:null
  return {currentTime:animation.currentTime,matrix:style.transform,duration:animation.effect.getTiming().duration,
    kind:node.classList.contains('l12-combat-defeat-ghost')?'defeat':'survivor',imageDecoded:[...node.querySelectorAll('img')].every(img=>img.complete&&img.naturalWidth>0),
    canvasCount:node.querySelectorAll('canvas[data-l12-pixel-snapshot="true"]').length,
    canvasSize:canvas?{width:canvas.width,height:canvas.height}:null,rgba,
    imageCount:node.querySelectorAll('img').length,filter:style.filter,
    realCardFrame:{border:[style.borderTopWidth,style.borderRightWidth,style.borderBottomWidth,style.borderLeftWidth],
      borderColors:[style.borderTopColor,style.borderRightColor,style.borderBottomColor,style.borderLeftColor],
      outline:style.outlineStyle,shadow:style.boxShadow,after:getComputedStyle(node,'::after').display},
    extraFrame:faceStyle?{border:[faceStyle.borderTopWidth,faceStyle.borderRightWidth,faceStyle.borderBottomWidth,faceStyle.borderLeftWidth],
      background:faceStyle.backgroundColor,outline:faceStyle.outlineStyle,shadow:faceStyle.boxShadow,after:getComputedStyle(face,'::after').display}:null,
    pixelCenterError:pixels?Math.hypot(pixels.left+pixels.width/2-rect.left-rect.width/2,pixels.top+pixels.height/2-rect.top-rect.height/2):null,
    pixelsInsideCanvas:pixels?pixels.left>=0&&pixels.top>=0&&pixels.right<=innerWidth&&pixels.bottom<=innerHeight:null,
    frame:readFrame()}
}
window.__combat3d={
  hit:(damage=2000)=>emit('造成 0 点战斗伤害；防守军团以当前兵力 '+damage+' 反击'),
  noDamage:()=>emit('进攻无损；造成 0 点战斗伤害'),
  death:()=>emit('进攻无损；造成 3000 点战斗伤害',{death:true}),
  kill:()=>emit('',{death:true,onlyLeave:true}),
  hideDeath:()=>emit('',{death:true,onlyLeave:true,hidden:true}),
  async pause(value){paused.value=value;await nextTick()},
  async duplicate(){events.value=[...events.value,...events.value.slice(-1)];revision.value++;await nextTick()},
  async restore(){
    mark('restore');present.value=true;revision.value++;await nextTick()
    // CardImage can replace its initial resolving/placeholder IMG after this
    // render. Do not mistake decode() on that old node for decoded source art.
    for(let attempt=0;attempt<120;attempt++){
      const card=authority('target'),image=card?.querySelector('img')
      if(image?.complete&&image.naturalWidth>0&&image.offsetWidth>0&&image.offsetHeight>0
          &&image.currentSrc.includes('/api/site/media/combat-3d-qa.svg')){
        await image.decode();await nextTick()
        if(card===authority('target')&&image===card.querySelector('img'))
          return {src:image.currentSrc,width:image.offsetWidth,height:image.offsetHeight,naturalWidth:image.naturalWidth,stableIdentity:true,
            snapshots:[...(componentDebug?.fieldSnapshots?.keys()||[])],defeated:[...(componentDebug?.defeatedInstances||[])]}
      }
      await new Promise(resolve=>requestAnimationFrame(resolve))
    }
    throw new Error('Restored current CardImage did not expose decoded positive-area source art')
  },
  async sync(){mark('sync');synchronizing.value=true;revision.value++;await nextTick();synchronizing.value=false;await nextTick()},
  middle(kind){return details(kind==='defeat'?ghosts().find(visible):authority('unit'))},
  read(){return {records,samples,frame:readFrame(),ghosts:ghosts().length,
    running:roots().reduce((count,node)=>count+node.getAnimations().filter(animation=>animation.playState==='running').length,0),
    angle:angle(authority('unit')),visible:visible(authority('unit')),ownedUnit:coordinator.ownsVisualKey('unit'),ownedTarget:coordinator.ownsVisualKey('target'),
    rotated:document.documentElement.dataset.l12Rotated,frameMarker:authority('unit')?.dataset.l12CombatRecoil,
    snapshots:[...(componentDebug?.fieldSnapshots?.entries()||[])].map(([id,item])=>({id,pixelsReady:item.pixelsReady})),
    defeated:[...(componentDebug?.defeatedInstances||[])],concealed:[...(componentDebug?.concealedInstances||[])],
    preparationCount:componentDebug?.preparationCount}},
  stop(){sampling=false},
}
const slot=(id,left)=>h('div',{class:'formation-slot',style:{position:'absolute',left:left+'px',top:'115px',width:'100px',height:'140px',padding:0,border:0,display:'block'}},[
  h(CardTile,{card:publicCard(id),selected:true,
    style:{width:'100px',height:'140px',padding:0,transform:id==='unit'?'rotate(90deg)':'rotate(0deg)',transformOrigin:'center',transition:'none'}}),
])
createApp({setup(){
  useLandscapeViewport(ref(true))
  return()=>h('div',{class:'l12-landscape-surface'},[
    h('div',{class:'board-stage','data-l12-game-stage':'',style:{position:'relative',width:'100%',height:'100%',overflow:'visible'}},[
      slot('unit',180),present.value?slot('target',460):null,
      h('div',{'data-player-index':'0'},[h('div',{'data-l12-zone':'graveyard',style:{position:'absolute',left:'20px',top:'280px',width:'70px',height:'70px'}})]),
      h('div',{'data-player-index':'1'},[h('div',{'data-l12-zone':'graveyard',style:{position:'absolute',left:'650px',top:'280px',width:'70px',height:'70px'}})]),
    ]),
    h(CombatMotionPresentationLayer,{events:events.value,matchId:'isolated-combat-3d',revision:revision.value,
      onVnodeMounted:vnode=>{componentDebug=vnode.component.setupState},
      paused:paused.value,synchronizing:synchronizing.value,sequenceCoordinator:coordinator}),
  ])
}}).mount('#app')
await nextTick();await Promise.all([...document.querySelectorAll('img')].map(img=>img.decode()))
// The real component can now capture decoded live cards at the next public fact.
requestAnimationFrame(sample);window.__combat3dReady=true
`
const html='<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><div id="l12-landscape-teleports"></div><script type="module" src="/__combat_3d.js"></script></body></html>'
const plugin={name:'isolated-combat-3d-acceptance',
  resolveId(id){if(id==='/__combat_3d.js')return id},load(id){if(id==='/__combat_3d.js')return entry},
  configureServer(server){server.middlewares.use('/__combat-3d',(_request,response)=>{
    response.setHeader('Content-Type','text/html; charset=utf-8');response.end(html)
  })},
}
const profiles=[{name:'desktop-1366',viewport:{width:1366,height:768},rotated:false},
  {name:'portrait-390-rotated',viewport:{width:390,height:844},rotated:true}]
const results=[],errors=[],findings=[],loaded=new Set(),started=Date.now()
let server,browser,activePage
function checkOwners(result){assert(result.samples.every(frame=>frame.unitOwners===1&&frame.targetOwners<=1),'natural rAF: exactly one real survivor and never duplicate victim')}
function checkTerminal(result){assert.equal(result.running,0);assert.equal(result.ghosts,0);assert.equal(result.visible,true)
  assert(Math.abs(result.angle-90)<.01,'rested authority retains its terminal 90 degree pose');checkOwners(result)
  assert.equal(result.ownedUnit,false);assert.equal(result.ownedTarget,false);assert.equal(result.frameMarker,undefined)}
try{
  server=await createServer({root,configFile:false,cacheDir:cache,plugins:[vue(),plugin],
    resolve:{alias:{'@':path.join(root,'src')}},server:{host:'127.0.0.1',port:0,hmr:false,watch:null},logLevel:'error'})
  await server.listen();const address=server.httpServer.address()
  browser=await chromium.launch({channel:process.env.L12_BROWSER_CHANNEL||'msedge',headless:true,timeout:8000,downloadsPath:path.join(cache,'downloads')})
  for(const profile of profiles.filter(item=>!process.env.L12_COMBAT_CASES||process.env.L12_COMBAT_CASES.split(',').includes(item.name))){
    const context=await browser.newContext({viewport:profile.viewport,deviceScaleFactor:1,isMobile:profile.rotated,hasTouch:profile.rotated,reducedMotion:'reduce'})
    const page=activePage=await context.newPage();page.setDefaultTimeout(3500)
    page.on('pageerror',error=>errors.push(String(error)))
    await page.route('**/*',route=>{
      const url=route.request().url()
      if(url.startsWith('http://127.0.0.1:')&&new URL(url).pathname==='/api/site/media/combat-3d-qa.svg')
        return route.fulfill({status:200,contentType:'image/svg+xml',body:decodeURIComponent(art.split(',').slice(1).join(','))})
      if(url.startsWith('http://127.0.0.1:')||url.startsWith('data:')){loaded.add(url.split('?')[0]);return route.continue()}
      return route.abort()
    })
    await page.goto(`http://127.0.0.1:${address.port}/__combat-3d`,{waitUntil:'networkidle',timeout:10000})
    await page.waitForFunction(()=>window.__combat3dReady)
    const initial=await page.evaluate(()=>window.__combat3d.read());assert.equal(initial.rotated,String(profile.rotated));checkTerminal(initial)
    const result={profile,rows:[],passed:false};results.push(result)
    async function waitDone(count){
      await page.waitForFunction(count=>{const state=window.__combat3d.read();return state.records.length>=count&&state.running===0&&state.ghosts===0},count)
      const state=await page.evaluate(()=>window.__combat3d.read());checkTerminal(state);assert.equal(state.records.length,count);return state
    }
    async function middle(kind,name){
      await page.waitForFunction(kind=>Boolean(window.__combat3d.middle(kind)),kind)
      const value=await page.evaluate(kind=>window.__combat3d.middle(kind),kind)
      assert(value&&value.matrix.startsWith('matrix3d('),'actual native middle frame has 3D projection')
      assert.equal(value.frame.unitOwners,1);assert(value.frame.targetOwners<=1)
      await page.screenshot({path:path.join(output,`${profile.name}-${name}.png`)})
      return value
    }
    await page.evaluate(()=>window.__combat3d.hit())
    const hit=await middle('survivor','survivor-rested'),hitEnd=await waitDone(1)
    assert.equal(hit.duration,280);assert.equal(hit.imageDecoded,true);assert(hit.imageCount>0);assert.equal(hit.canvasCount,0);assert.equal(hit.frame.ghosts,0)
    if(hit.realCardFrame.borderColors.some(color=>color!=='rgba(0, 0, 0, 0)')||hit.realCardFrame.outline!=='none'||hit.realCardFrame.shadow!=='none'||hit.realCardFrame.after!=='none')
      findings.push({profile:profile.name,kind:'survivor-ui-frame-still-visible',computed:hit.realCardFrame})
    result.rows.push({name:'real-rested-survivor-3d-no-clone',middle:hit,record:hitEnd.records[0]})
    await page.evaluate(()=>window.__combat3d.duplicate());await page.waitForTimeout(100)
    assert.equal(await page.evaluate(()=>window.__combat3d.read().records.length),1)
    result.rows.push({name:'duplicate-same-sequence-no-replay',passed:true})
    await page.evaluate(async()=>{await window.__combat3d.pause(true);await window.__combat3d.hit()})
    await page.waitForTimeout(110)
    const held=await page.evaluate(()=>window.__combat3d.read());assert.equal(held.records.length,1);assert.equal(held.running,0);assert.equal(held.frame.unitOwners,1)
    await page.evaluate(()=>window.__combat3d.pause(false));await middle('survivor','unpaused-hit');await waitDone(2)
    result.rows.push({name:'prepared-paused-real-card-static-single-unpause',held:{angle:held.angle,running:held.running,ghosts:held.ghosts}})
    await page.evaluate(()=>window.__combat3d.noDamage());await page.waitForTimeout(110)
    assert.equal(await page.evaluate(()=>window.__combat3d.read().records.length),2)
    result.rows.push({name:'zero-damage-no-loss-no-invented-hit',passed:true})
    const strengths=[]
    for(const [damage,count] of [[1000,3],[10000,4]]){
      await page.evaluate(damage=>window.__combat3d.hit(damage),damage)
      await middle('survivor','hit-strength-'+damage)
      const end=await waitDone(count),record=end.records.at(-1)
      const pose=record.frames.find(frame=>frame.offset===.28).transform.match(/translate3d\(0(?:px)?,\s*([-.\d]+)px,\s*([-.\d]+)px\)/)
      assert(pose);strengths.push({damage,distance:+pose[1],elevation:+pose[2],duration:record.duration})
    }
    assert(strengths[1].distance>strengths[0].distance&&strengths[1].elevation>strengths[0].elevation)
    assert(strengths.every(item=>item.duration===280))
    result.rows.push({name:'confirmed-damage-strength-without-extra-time-or-card',strengths})
    await page.evaluate(()=>window.__combat3d.death())
    const death=await middle('defeat','confirmed-death'),deathEnd=await waitDone(5)
    assert.equal(death.duration,920);assert.equal(death.canvasCount,1);assert.equal(death.imageCount,0)
    assert(death.canvasSize.width>0&&death.canvasSize.height>0)
    assert(death.rgba[3]===255&&Math.abs(death.rgba[0]-21)<=2&&Math.abs(death.rgba[1]-31)<=2&&Math.abs(death.rgba[2]-48)<=2,'actual canvas holds source card pixels, not an empty cloned canvas')
    assert(death.pixelCenterError<2);assert.equal(death.pixelsInsideCanvas,true)
    assert(death.extraFrame.border.every(width=>parseFloat(width)===0));assert.equal(death.extraFrame.background,'rgba(0, 0, 0, 0)')
    assert.equal(death.extraFrame.outline,'none');assert.equal(death.extraFrame.shadow,'none');assert.equal(death.extraFrame.after,'none')
    const earlyDeath=deathEnd.samples.filter(frame=>frame.phase==='death'&&frame.elapsed<450)
    assert(earlyDeath.length>3&&earlyDeath.every(frame=>frame.targetOwners===1&&frame.canvases===1),'prepared and active death has exactly one decoded canvas owner')
    result.rows.push({name:'confirmed-death-one-decoded-pixel-owner-3d-frame-free',middle:death,record:deathEnd.records.at(-1)})
    const restoredSource=await page.evaluate(()=>window.__combat3d.restore());await page.evaluate(()=>window.__combat3d.kill())
    await middle('defeat','cancel-by-sync');await page.evaluate(()=>window.__combat3d.sync());await page.waitForTimeout(110)
    const recovered=await page.evaluate(()=>window.__combat3d.read());checkTerminal(recovered)
    assert.equal(recovered.records.length,6)
    result.rows.push({name:'synchronization-cancels-active-death-owner',restoredSource,passed:true})
    await page.evaluate(()=>window.__combat3d.restore());await page.evaluate(()=>window.__combat3d.hideDeath());await page.waitForTimeout(110)
    const hidden=await page.evaluate(()=>window.__combat3d.read());checkTerminal(hidden);assert.equal(hidden.records.length,6)
    result.rows.push({name:'hidden-card-never-reuses-cached-public-face',passed:true})
    await page.evaluate(()=>window.__combat3d.stop());result.samples=hidden.samples;result.records=hidden.records;result.passed=true
    await context.close();activePage=null
  }
  assert.equal(errors.length,0,errors.join('\n'))
  assert(![...loaded].some(url=>/\/(GameBoard|PromptOverlay|CardStateTransitionLayer)\.vue/.test(url)),'isolation does not import another writer layer')
}catch(error){
  errors.push(error.stack||String(error));process.exitCode=1
  if(activePage)try{const partial=await activePage.evaluate(()=>window.__combat3d?.read());if(partial)results.push({partial})}catch{}
}finally{
  await browser?.close();await server?.close()
  const sourceAfter=pins();if(JSON.stringify(sourceBefore)!==JSON.stringify(sourceAfter)){errors.push('source changed during acceptance');process.exitCode=1}
  const report={suite:'real CombatMotion isolated browser 3D/decoded-canvas ownership',
    scope:'Synthetic public authority facts + actual SFC/coordinator/viewport; no prototype patch/time seek/duration override; not full engine/GameBoard/replay/physical phone',
    output,cache,temp:process.env.TEMP,sourceBefore,sourceAfter,elapsedMs:Date.now()-started,
    results,errors,findings,loaded:[...loaded],behaviorPassed:results.filter(item=>item.passed).length===2&&errors.length===0,
    visualFrameAccepted:findings.length===0,passed:results.filter(item=>item.passed).length===2&&errors.length===0&&findings.length===0}
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2))
  console.log(JSON.stringify({passed:report.passed,behaviorPassed:report.behaviorPassed,visualFrameAccepted:report.visualFrameAccepted,
    profiles:results.filter(item=>item.passed).length,errors,findings,elapsedMs:report.elapsedMs,output},null,2))
}
