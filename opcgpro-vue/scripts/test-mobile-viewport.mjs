import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const source = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
let entry = source.slice(source.indexOf('const entry = `') + 15, source.indexOf('\n`', source.indexOf('const entry = `')))
entry = `import {ref} from 'vue';import Editor from '/src/l12/L12DeckEditor.vue';import {audioPreferences} from '/src/l12/audioPreferences.ts';import {useLandscapeViewport,viewportRect} from '/src/l12/mobileViewport.ts';import '/src/l12/mobileViewport.css';window.qaRect=viewportRect;window.qaMobileLayout=value=>audioPreferences.mobileLayout=value;const qaEnabled=ref(true);window.qaExit=()=>qaEnabled.value=false;\n` + entry.replace('createApp({render:', 'createApp({setup(){useLandscapeViewport(qaEnabled)},render:').replace('()=>isPicker?', "()=>params.has('editor')?h(Editor):isPicker?")
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-mobile-test'), server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'mobile-fixture', resolveId(id) { if(id==='/__mobile__.js')return id }, load(id) { if(id==='/__mobile__.js')return entry }, configureServer(s) { s.middlewares.use((req,res,next)=>{if(req.url?.startsWith('/__mobile__')&&!req.url.includes('.js')){res.setHeader('Content-Type','text/html');res.end('<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__mobile__.js"></script>');return}next()}) } }] })
entry=entry.replace('window.__sentCommands=[]',`if(params.has('response'))l12State.game.prompts=[{promptId:'response-target-qa',playerIndex:0,kind:'response-target',text:'选择要响应的效果',validChoices:['stack-a','stack-b'],minChoose:1,maxChoose:1,data:{'stack-a':'同名来源：第一段\\n公开目标：军团甲','stack-b':'同名来源：第二段\\n公开目标：军团乙','stack-a:cardId':legions[0].id,'stack-b:cardId':legions[0].id,'stack-a:name':'同名来源','stack-b:name':'同名来源','stack-a:effect':'完整来源效果','stack-b:effect':'完整来源效果'},choiceLabels:{},createdRevision:1,controller:0}];window.__sentCommands=[]`)
let browser
entry=entry.replace('window.__sentCommands=[]', `if(params.has('response')){const p=l12State.game.prompts[0];l12State.game.players[0].field[0][1]={...l12State.game.players[0].field[0][0],instanceId:'same-name-second'};p.data.responseTargetIds=JSON.stringify(['0unit','same-name-second']);p.data['stack-a:responseTargetIds']=JSON.stringify(['0unit']);p.data['stack-b:responseTargetIds']=JSON.stringify(['same-name-second']);}window.__sentCommands=[]`)
entry=entry.replace("l12State.status='online'", `if(params.has('mobileAction')){l12State.game.players[0].field[0][0].abilities=[{id:'mobile-active-rest',label:'主动休整',enabled:true}];l12State.game.legalAttackTargets={'0unit':['master']}}l12State.status='online'`)
entry=entry.replace('if(relicMode)players[0].relic=', `if(params.has('railStress')){const railTrials=side=>Array.from({length:3},(_,j)=>({...trial,instanceId:'rail-'+side+'-'+j,hidden:false,trialCompleted:j===0,trialProgress:j+1}));players[0].specialZones.trials=railTrials('my');players[1].specialZones.trials=railTrials('opponent')}if(relicMode)players[0].relic=`)
try {
  await server.listen()
  browser = await chromium.launch({headless:true,channel:'msedge'})
  const page = await browser.newPage()
  const errors=[]
  page.on('pageerror',error=>errors.push(error.message))
  await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
  const results=[]
  const out=path.resolve(root,'../artifacts/mobile-viewport')
  fs.mkdirSync(out,{recursive:true})
  for (const size of [
    {width:1280,height:720,mobile:false,rotated:false},
    {width:1280,height:480,mobile:false,rotated:false},
    {width:1366,height:768,mobile:false,rotated:false},
    {width:1366,height:1024,mobile:true,rotated:false},
    {width:1024,height:768,mobile:true,rotated:false},
    {width:768,height:1024,mobile:true,rotated:true},
    {width:800,height:800,mobile:false,rotated:false},
    {width:390,height:844,mobile:true,rotated:true},
    {width:375,height:667,mobile:true,rotated:true},
    {width:844,height:390,mobile:true,rotated:false},
    {width:740,height:360,mobile:true,rotated:false},
  ]) {
    await page.setViewportSize(size)
    await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__`)
    await page.locator('.board-stage').waitFor()
    await page.waitForTimeout(150)
    const result=await page.evaluate(()=>{
      const board=document.querySelector('.board-viewport'),stage=document.querySelector('.board-stage'),rect=document.body.getBoundingClientRect(),boardRect=window.qaRect(board),stageRect=window.qaRect(stage),style=getComputedStyle(board)
      const logicalRect=selector=>{const element=document.querySelector(selector);if(!element)return null;const value=window.qaRect(element);return {left:value.left,top:value.top,right:value.right,bottom:value.bottom,width:value.width,height:value.height}}
      const geometry=Object.fromEntries(Object.entries({master:'.my-half .mini-master',relic:'.my-half .relic-zone .card-tile',fieldCard:'.my-half .formation-slot .card-tile',fieldSlot:'.my-half .formation-slot',pile:'.my-half .mat-piles .pile',resource:'.my-half .resource-zone',handCard:'.board-center>.l12-hand:last-child .card-tile',rightRail:'.right-rail'}).map(([key,selector])=>[key,logicalRect(selector)]))
      const regions=Object.fromEntries(Object.entries({leftRail:'.left-rail',center:'.board-center',rightRail:'.right-rail',felt:'.felt-board',hand:'.board-center>.l12-hand:last-child',opponentHalf:'.opponent-half',myHalf:'.my-half'}).map(([key,selector])=>[key,logicalRect(selector)]))
      const playerGroups=side=>['commander-zone','battle-zone','mat-piles','resource-zone'].map(name=>logicalRect(`.${side}-half .${name}`))
      const stat=document.querySelector('.my-half .formation-slot .card-cost,.my-half .formation-slot .card-power')
      return {mode:document.documentElement.dataset.l12Viewport,mobile:document.documentElement.dataset.l12Mobile,rotated:document.documentElement.dataset.l12Rotated,body:{left:rect.left,top:rect.top,right:rect.right,bottom:rect.bottom},board:{left:boardRect.left,top:boardRect.top,right:boardRect.right,bottom:boardRect.bottom},stage:{left:stageRect.left,top:stageRect.top,right:stageRect.right,bottom:stageRect.bottom},overflowX:style.overflowX,overflowY:style.overflowY,scale:getComputedStyle(stage).transform,rail:logicalRect('.left-rail'),railItems:['.mobile-card-inspector-handle-global','.current-disaster-panel','.mobile-current-disaster-value','.session-disaster-panel'].map(logicalRect),regions,playerGroups:{opponent:playerGroups('opponent'),my:playerGroups('my')},geometry,statFont:stat?parseFloat(getComputedStyle(stat).fontSize):null}
    })
    assert.ok(result.body.left>=-1&&result.body.top>=-1&&result.body.right<=size.width+1&&result.body.bottom<=size.height+1,JSON.stringify(result))
    assert.equal(result.mode,'landscape')
    assert.equal(result.rotated,String(size.rotated))
    assert.equal(result.mobile,String(size.mobile))
    assert.equal(result.overflowX,'hidden')
    assert.equal(result.overflowY,'hidden')
    assert.ok(result.stage.left>=result.board.left-1&&result.stage.top>=result.board.top-1&&result.stage.right<=result.board.right+1&&result.stage.bottom<=result.board.bottom+1,JSON.stringify(result))
    if(size.mobile){
      const inside=(inner,outer,tolerance=1)=>inner&&outer&&inner.left>=outer.left-tolerance&&inner.top>=outer.top-tolerance&&inner.right<=outer.right+tolerance&&inner.bottom<=outer.bottom+tolerance
      const disjoint=(first,second,tolerance=1)=>first.right<=second.left+tolerance||second.right<=first.left+tolerance||first.bottom<=second.top+tolerance||second.bottom<=first.top+tolerance
      const items=result.railItems.filter(Boolean)
      assert.equal(items.length,4,`mobile disaster rail inventory is incomplete: ${JSON.stringify(result)}`)
      for(const item of items)assert.ok(item.left>=result.rail.left-3&&item.right<=result.rail.right+3&&item.top>=result.stage.top-3&&item.bottom<=result.stage.bottom+3,`mobile disaster rail item escaped its allocation: ${JSON.stringify(result)}`)
      for(let index=1;index<items.length;index++)assert.ok(items[index-1].bottom<=items[index].top+1,`mobile disaster rail items overlap: ${JSON.stringify(result)}`)
      assert.ok(result.regions.leftRail.right<=result.regions.center.left+1&&result.regions.center.right<=result.regions.rightRail.left+1,`mobile outer columns overlap: ${JSON.stringify(result.regions)}`)
      assert.ok(inside(result.regions.felt,result.regions.center)&&inside(result.regions.hand,result.regions.center),`felt or hand escaped the center allocation: ${JSON.stringify(result.regions)}`)
      assert.ok(result.regions.opponentHalf.bottom<=result.regions.myHalf.top+1,`player battlefield halves overlap: ${JSON.stringify(result.regions)}`)
      assert.ok(result.regions.felt.bottom<=result.regions.hand.top+1,`battlefield overlaps the hand lane: ${JSON.stringify(result.regions)}`)
      for(const [side,groups] of Object.entries(result.playerGroups)){
        const half=result.regions[side==='my'?'myHalf':'opponentHalf']
        assert.ok(groups.every(group=>inside(group,half,2)),`${side} battlefield group escaped its half: ${JSON.stringify({half,groups})}`)
        for(let first=0;first<groups.length;first++)for(let second=first+1;second<groups.length;second++)assert.ok(disjoint(groups[first],groups[second]),`${side} battlefield groups overlap: ${JSON.stringify(groups)}`)
      }
    }
    // Body Teleport: a logical fixed button must hit-test at its rotated DOM rect.
    await page.evaluate(()=>{const b=document.createElement('button');b.id='qa-fixed';b.style.cssText='position:fixed;left:120px;top:90px;width:80px;height:40px;z-index:2147483647';b.textContent='点选目标';b.onclick=()=>b.dataset.clicked='yes';document.querySelector('#l12-landscape-teleports').append(b)})
    await page.locator('#qa-fixed').click()
    assert.equal(await page.locator('#qa-fixed').getAttribute('data-clicked'),'yes')
    const logical=await page.locator('#qa-fixed').evaluate(el=>{const r=window.qaRect(el);return{x:r.x,y:r.y,w:r.width,h:r.height}})
    assert.deepEqual(logical,{x:120,y:90,w:80,h:40})
    if(size.width<size.height){
      await page.evaluate(()=>{const input=document.createElement('input');input.id='qa-input';document.body.append(input);input.focus()})
      await page.waitForTimeout(50)
      assert.equal(await page.locator('html').getAttribute('data-l12-rotated'),'true')
      await page.locator('#qa-input').fill('键盘输入测试')
      await page.locator('#qa-input').evaluate(el=>el.blur())
      await page.waitForFunction(()=>document.documentElement.dataset.l12Rotated==='true')
      assert.equal(await page.locator('html').getAttribute('data-l12-viewport'),'landscape')
    }
    results.push({size,...result,logical})
    await page.locator('#qa-fixed').evaluate(el=>el.remove())
    const settingsButton=page.getByRole('button',{name:'打开对局设置',exact:true})
    if(await settingsButton.count()) {
      await settingsButton.click()
      await page.locator('.l12-settings-modal').waitFor()
      await page.locator('.l12-settings-modal header button').click()
    }
    await page.screenshot({path:path.join(out,`battle-${size.width}-${size.height}.png`)})
  }
  const physicalLandscape=results.find(item=>item.size.width===844&&item.size.height===390)
  const logicalLandscape=results.find(item=>item.size.width===390&&item.size.height===844)
  assert.ok(physicalLandscape&&logicalLandscape)
  assert.ok(Math.abs(physicalLandscape.rail.width-logicalLandscape.rail.width)<=1,`physical and logical landscape rails diverged: ${JSON.stringify({physicalLandscape:physicalLandscape.rail,logicalLandscape:logicalLandscape.rail})}`)
  for(const key of Object.keys(physicalLandscape.geometry)){
    const physical=physicalLandscape.geometry[key],logical=logicalLandscape.geometry[key]
    if(!physical&&!logical)continue
    assert.ok(physical&&logical,`physical/logical landscape inventory diverged for ${key}`)
    assert.ok(Math.abs(physical.width-logical.width)<=1&&Math.abs(physical.height-logical.height)<=1,`physical/logical landscape geometry diverged for ${key}: ${JSON.stringify({physical,logical})}`)
  }
  assert.ok(Math.abs(physicalLandscape.statFont-logicalLandscape.statFont)<=.1,`physical/logical landscape card value typography diverged: ${JSON.stringify({physical:physicalLandscape.statFont,logical:logicalLandscape.statFont})}`)
  const railStressResults=[]
  for(const size of [{width:844,height:390},{width:390,height:844}]){
    await page.setViewportSize(size)
    await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?railStress=1`)
    await page.locator('.mobile-extra-zone-opponent').waitFor()
    const stressedRail=await page.evaluate(()=>{
      const stage=window.qaRect(document.querySelector('.board-stage'))
      const selectors=['.mobile-card-inspector-handle-global','.mobile-extra-zone-opponent','.left-disaster-row','.mobile-extra-zone-my']
      return {stage,items:selectors.map(selector=>{const element=document.querySelector(selector);const value=window.qaRect(element);return {selector,left:value.left,top:value.top,right:value.right,bottom:value.bottom,width:value.width,height:value.height}})}
    })
    for(let index=1;index<stressedRail.items.length;index++)assert.ok(stressedRail.items[index-1].bottom<=stressedRail.items[index].top+1,`stressed disaster rail overlaps: ${JSON.stringify(stressedRail)}`)
    assert.ok(stressedRail.items.every(item=>item.left>=stressedRail.stage.left-1&&item.right<=stressedRail.stage.right+1&&item.top>=stressedRail.stage.top-1&&item.bottom<=stressedRail.stage.bottom+1),`stressed disaster rail escaped the safe rectangle: ${JSON.stringify(stressedRail)}`)
    railStressResults.push(stressedRail.items)
    await page.screenshot({path:path.join(out,`rail-stress-${size.width}-${size.height}.png`)})
  }
  for(let index=0;index<railStressResults[0].length;index++)assert.ok(Math.abs(railStressResults[0][index].width-railStressResults[1][index].width)<=1&&Math.abs(railStressResults[0][index].height-railStressResults[1][index].height)<=1,`physical/logical stressed rail geometry diverged: ${JSON.stringify({physical:railStressResults[0],logical:railStressResults[1]})}`)
  // Layout preference and physical rotation are deliberately independent.
  await page.setViewportSize({width:844,height:390})
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__`)
  await page.evaluate(()=>window.qaMobileLayout('off'))
  await page.waitForFunction(()=>document.documentElement.dataset.l12Mobile==='false')
  assert.equal(await page.locator('html').getAttribute('data-l12-rotated'),'false')
  await page.setViewportSize({width:1280,height:720})
  await page.evaluate(()=>window.qaMobileLayout('on'))
  await page.waitForFunction(()=>document.documentElement.dataset.l12Mobile==='true')
  assert.equal(await page.locator('html').getAttribute('data-l12-rotated'),'false')
  await page.evaluate(()=>window.qaMobileLayout('auto'))
  await page.waitForFunction(()=>document.documentElement.dataset.l12Mobile==='false')
  const actionPage = await browser.newPage()
  await actionPage.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse')
      ? ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} }) : native(query)
  })
  await actionPage.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
  await actionPage.setViewportSize({width:844,height:390})
  await actionPage.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?mobileAction=1`)
  await actionPage.locator('[data-l12-mobile-landscape="true"]').waitFor()
  await actionPage.locator('.my-half .formation-slot .card-tile').first().click()
  const activeDock = actionPage.locator('.mobile-action-dock')
  await activeDock.waitFor()
  await activeDock.getByRole('button',{name:'发动',exact:true}).click()
  await actionPage.waitForFunction(()=>window.__sentCommands.some(message=>message.command?.type==='activateAbility'&&message.command?.ability==='mobile-active-rest'))
  await actionPage.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?mobileAction=1`)
  await actionPage.locator('[data-l12-mobile-landscape="true"]').waitFor()
  await actionPage.locator('.my-half .formation-slot .card-tile').first().click()
  const attackDock = actionPage.locator('.mobile-action-dock')
  await attackDock.getByRole('button',{name:'进攻',exact:true}).click()
  await actionPage.locator('.board-mode-hint').waitFor()
  await actionPage.locator('.board-mode-hint').getByRole('button',{name:'取消',exact:true}).click()
  assert.equal(await actionPage.locator('.mobile-action-dock').count(),0)
  await actionPage.close()
  await page.setViewportSize({width:390,height:844})
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?effect=cost2`)
  await page.locator('.prompt-panel').waitFor()
  const prompt=await page.locator('.l12-prompt-overlay').evaluate(el=>{const r=el.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height}})
  const logicalPrompt=await page.locator('.l12-prompt-overlay').evaluate(el=>{const r=window.qaRect(el);return{x:r.x,y:r.y,w:r.width,h:r.height}})
  assert.deepEqual(logicalPrompt,{x:0,y:0,w:844,h:390})
  await page.locator('.prompt-minimize').first().click()
  const minimizedRestore=page.locator('.prompt-minimized-bar button').first()
  const minimizedRect=await minimizedRestore.evaluate(el=>{const r=el.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height}})
  assert.ok(minimizedRect.x>=0&&minimizedRect.y>=0&&minimizedRect.x+minimizedRect.w<=390&&minimizedRect.y+minimizedRect.h<=844,JSON.stringify(minimizedRect))
  await minimizedRestore.click()
  await page.locator('.prompt-panel').waitFor()
  await page.screenshot({path:path.join(out,'portrait-prompt.png')})
  await page.getByRole('button',{name:'消耗1士气',exact:true}).click()
  await page.locator('.prompt-confirm-choice').click()
  assert.ok(await page.evaluate(()=>window.__sentCommands.length>0))
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?response=1`)
  await page.locator('.response-target-row').first().waitFor()
  assert.equal(await page.locator('.response-target-row').count(),2)
  assert.equal(await page.locator('.response-target-detail').count(),0)
  assert.match(await page.locator('.response-target-select').nth(0).innerText(),/第一段\n公开目标：军团甲/)
  assert.match(await page.locator('.response-target-select').nth(1).innerText(),/第二段\n公开目标：军团乙/)
  await page.locator('.response-target-select').nth(1).click()
  await page.locator('.prompt-minimize').first().click()
  await page.waitForFunction(()=>document.querySelectorAll('.formation-slot.response-target').length===1)
  // Verify the highlight against the actual card DOM, not its shared display name.
  const highlightedMarkup=await page.locator('.formation-slot.response-target').evaluate(el=>el.outerHTML)
  assert.ok(highlightedMarkup.includes('same-name-second'),highlightedMarkup)
  await page.locator('.formation-slot.response-target').scrollIntoViewIfNeeded()
  await page.screenshot({path:path.join(out,'portrait-response-highlight.png')})
  await page.locator('.prompt-minimized-bar button').first().click()
  await page.waitForFunction(()=>document.querySelectorAll('.formation-slot.response-target').length===0)
  await page.screenshot({path:path.join(out,'portrait-response-target.png')})
  await page.locator('.prompt-confirm-choice').click()
  assert.ok(await page.evaluate(()=>JSON.stringify(window.__sentCommands).includes('stack-b')))
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__mobile__?editor=1`)
  await page.locator('.deck-builder-grid').waitFor()
  assert.equal(await page.locator('html').getAttribute('data-l12-viewport'),'landscape')
  assert.equal(await page.locator('html').getAttribute('data-l12-rotated'),'true')
  assert.equal(await page.locator('.deck-builder-grid').evaluate(el=>getComputedStyle(el).gridTemplateColumns.split(' ').length),3)
  assert.equal(await page.locator('.deck-builder-shell').evaluate(el=>el.scrollWidth<=el.clientWidth+1),true)
  await page.locator('.deck-builder-topbar input').fill('移动端牌库输入')
  assert.equal(await page.locator('.deck-builder-topbar input').inputValue(),'移动端牌库输入')
  await page.locator('.deck-builder-topbar input').evaluate(el=>el.blur())
  await page.waitForFunction(()=>document.documentElement.dataset.l12Rotated==='true')
  assert.equal(await page.locator('html').getAttribute('data-l12-viewport'),'landscape')
  await page.setViewportSize({width:844,height:390})
  await page.waitForFunction(()=>document.documentElement.dataset.l12Rotated==='false')
  assert.equal(await page.locator('.deck-builder-topbar input').inputValue(),'移动端牌库输入')
  assert.equal(await page.locator('html').getAttribute('data-l12-mobile'),'true')
  await page.setViewportSize({width:390,height:844})
  await page.waitForFunction(()=>document.documentElement.dataset.l12Rotated==='true')
  assert.equal(await page.locator('.deck-builder-topbar input').inputValue(),'移动端牌库输入')
  await page.screenshot({path:path.join(out,'portrait-editor.png')})
  await page.evaluate(()=>window.qaExit())
  await page.waitForTimeout(50)
  assert.equal(await page.locator('html').getAttribute('data-l12-viewport'),null)
  assert.equal(await page.locator('body').evaluate(el=>getComputedStyle(el).transform),'none')
  assert.deepEqual(errors,[])
  console.log(JSON.stringify(results,null,2))
  console.log('Viewport canvas: 11 viewport profiles fit the whole battle board, logical Teleport hit testing, inverse coordinates, prompt minimize/restore, deck landscape, portrait/landscape transition and state retention passed; screenshots: '+out)
} finally { await browser?.close();await server.close() }
