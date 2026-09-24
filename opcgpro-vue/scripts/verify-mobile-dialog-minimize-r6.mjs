import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const target = process.argv[2] || 'http://127.0.0.1:5191/__l12_battle_preview__'
const output = process.env.L12_R6_DIALOG_OUT || path.resolve('..', 'artifacts', 'batch-mobile-battle-r6', 'dialog-minimize')
const viewports = [[667,375],[844,390],[932,430],[1024,768]].map(([width,height]) => ({ width, height }))
const zeroInsets={top:0,right:0,bottom:0,left:0}
const safeProfiles=[
  {name:'left-59-bottom-21',viewport:{width:667,height:375},insets:{top:0,right:0,bottom:21,left:59}},
  {name:'right-59-bottom-21',viewport:{width:844,height:390},insets:{top:0,right:59,bottom:21,left:0}},
  {name:'both-44-bottom-21',viewport:{width:932,height:430},insets:{top:0,right:44,bottom:21,left:44}},
]
const cases = [
  ['card-detail','field=full&markers=5&piles=40&hand=10','card-detail'],
  ['candidate','field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=12','prompt'],
  ['master','field=full&markers=5&piles=40&hand=10&modalFixture=1','master'],
  ['graveyard','field=full&markers=5&piles=40&hand=10&modalFixture=1','graveyard'],
  ['faction','field=full&markers=5&piles=40&hand=10&modalFixture=1','faction'],
  ['card-effect','field=full&markers=5&piles=40&hand=10&modalFixture=1','ability'],
  ['morale-view','field=full&markers=5&piles=40&hand=10&runes=5','morale-view'],
  ['morale-payment','field=full&markers=5&piles=40&hand=10&runes=5&morale-payment=1&rune-usable=1','morale-payment'],
  ['board-target','field=full&markers=5&piles=40&hand=10&board-target=1','board-target'],
  ['board-slot','field=empty&markers=5&piles=40&hand=10&board-slot=1','board-slot'],
  ['support','field=full&markers=5&piles=40&hand=10&support=1','combat'],
  ['defense','field=full&markers=5&piles=40&hand=10&defense=1','combat'],
  ['record','field=full&markers=5&piles=40&hand=10','record'],
  ['settlement','field=full&markers=5&piles=40&hand=10&game-over=win','settlement'],
  ['gm','field=full&markers=5&piles=40&hand=10&gm=1','gm'],
  ['disaster-waiting','field=full&markers=5&piles=40&hand=10&disaster-chain=1','disaster-waiting'],
]
const focusCases=[
  ...[1,6,12,20].map(count=>[`candidate-${count}`,`field=full&markers=5&piles=40&hand=10&card-choice=1&choice-count=${count}`,'candidate']),
  ['graveyard-8','field=full&markers=5&piles=8&hand=10&modalFixture=1','graveyard'],
  ['graveyard-40','field=full&markers=5&piles=40&hand=10&modalFixture=1','graveyard'],
  ['master-focus','field=full&markers=5&piles=40&hand=10&modalFixture=1','master'],
  ['faction-focus','field=full&markers=5&piles=40&hand=10&modalFixture=1','faction'],
  ['card-effect-focus','field=full&markers=5&piles=40&hand=10&modalFixture=1','ability'],
]

fs.mkdirSync(output,{ recursive:true })
const manifest = {
  generatedAt:new Date().toISOString(), target, status:'running', viewports,
  exception:{ layer:'天灾卡面动画', minimizable:false, reason:'约0.92秒自动结束的非阻塞展示，没有选择、倒计时或可恢复流程状态；动画被新遮挡时按需求自然中止且不排队。天灾等待提示属于阻塞流程，已列入本矩阵并可最小化。' },
  results:[], safeResults:[], focusResults:[],
}

const browser = await chromium.launch(process.env.L12_CHROMIUM_EXECUTABLE
  ? { headless:true, executablePath:process.env.L12_CHROMIUM_EXECUTABLE }
  : { headless:true, channel:'msedge' })
try {
  const context = await browser.newContext()
  const page = await context.newPage()
  const cdp = await context.newCDPSession(page)
  page.setDefaultTimeout(10_000)
  await page.addInitScript(() => {
    const native = window.matchMedia.bind(window)
    window.matchMedia = query => query.includes('pointer: coarse')
      ? { matches:true, media:query, addEventListener(){}, removeEventListener(){} } : native(query)
  })
  await page.route('**/*', route => ['127.0.0.1','localhost'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  async function load(viewport, query, insets=zeroInsets) {
    await page.setViewportSize(viewport)
    await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets})
    await page.goto(`${target}?canvas=1&mobile=1&${query}`, { waitUntil:'domcontentloaded' })
    await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
    await page.waitForTimeout(80)
  }
  async function snap(file) { await page.screenshot({ path:path.join(output,file) }) }
  async function assertBoardVisible(label) {
    const board = page.locator('.felt-board')
    await board.waitFor()
    const box = await board.boundingBox()
    assert.ok(box && box.width > 80 && box.height > 80, `${label}: battlefield is not observable`)
  }
  async function assertVisibleInsideViewport(locator, label) {
    await locator.waitFor()
    const result = await locator.evaluate(element => {
      const r=element.getBoundingClientRect(), root=getComputedStyle(document.documentElement), number=name=>parseFloat(root.getPropertyValue(name))||0
      const safe={left:number('--l12-viewport-left'),top:number('--l12-viewport-top')}; safe.right=safe.left+number('--l12-viewport-width'); safe.bottom=safe.top+number('--l12-viewport-height')
      return {rect:{left:r.left,top:r.top,right:r.right,bottom:r.bottom},safe}
    })
    assert.ok(result.rect.left >= result.safe.left-1 && result.rect.top >= result.safe.top-1 && result.rect.right <= result.safe.right+1 && result.rect.bottom <= result.safe.bottom+1, `${label}: restore control outside viewport ${JSON.stringify(result)}`)
  }
  async function assertInspector(label) {
    const drawer=page.locator('.mobile-card-inspector'); await drawer.waitFor(); await page.waitForTimeout(40)
    const result=await drawer.evaluate(element => {
      const root=getComputedStyle(document.documentElement), number=name=>parseFloat(root.getPropertyValue(name))||0
      const safe={left:number('--l12-viewport-left'),top:number('--l12-viewport-top')}
      safe.right=safe.left+number('--l12-viewport-width'); safe.bottom=safe.top+number('--l12-viewport-height')
      const rect=node=>{const r=node?.getBoundingClientRect();return r&&{left:r.left,top:r.top,right:r.right,bottom:r.bottom,width:r.width,height:r.height}}
      const visible=node=>{const r=node?.getBoundingClientRect();return Boolean(r&&r.width>0&&r.height>0)}
      const nodes={
        drawer:element, eyebrow:element.querySelector('header small'), title:element.querySelector('header h2'), close:element.querySelector('header button'),
        image:element.querySelector('.archive-detail-image'), copy:element.querySelector('.card-detail-copy'), effect:element.querySelector('.l12-effect-body'),
      }
      const geometries=Object.fromEntries(Object.entries(nodes).map(([name,node])=>[name,rect(node)]))
      const horizontalOverflows=Object.entries(nodes).filter(([,node])=>node&&visible(node)&&node.scrollWidth>node.clientWidth+1).map(([name,node])=>({name,client:node.clientWidth,scroll:node.scrollWidth}))
      const outside=Object.entries(geometries).filter(([,r])=>r&&(r.left<safe.left-1||r.top<safe.top-1||r.right>safe.right+1||r.bottom>safe.bottom+1)).map(([name,r])=>({name,...r}))
      const drawerArea=(geometries.drawer?.width||1)*(geometries.drawer?.height||1), imageArea=(geometries.image?.width||0)*(geometries.image?.height||0)
      const effectVisible=Boolean(geometries.effect&&geometries.copy&&geometries.effect.bottom>geometries.copy.top&&geometries.effect.top<geometries.copy.bottom)
      return {safe,geometries,horizontalOverflows,outside,imageShare:imageArea/drawerArea,effectVisible,title:nodes.title?.textContent?.trim()||'',effect:nodes.effect?.textContent?.trim()||''}
    })
    assert.deepEqual(result.outside,[],`${label}: detail geometry escapes safe viewport ${JSON.stringify(result)}`)
    assert.deepEqual(result.horizontalOverflows,[],`${label}: detail has horizontal clipping ${JSON.stringify(result.horizontalOverflows)}`)
    assert.ok(result.title&&result.title!=='选择一张卡牌',`${label}: detail has no real focused-card title`)
    assert.ok(result.effect.length>0&&result.effectVisible,`${label}: first-screen card text is not visible`)
    assert.ok(result.imageShare<=.30,`${label}: reference card image exceeds 30% of drawer area (${result.imageShare})`)
    return result
  }
  async function inspectFromGrave(label) {
    const graveTrigger=page.locator('.graveyard').last()
    if (!await graveTrigger.count() || !await graveTrigger.isVisible()) return false
    await graveTrigger.click()
    const grave=page.locator('.graveyard-window'); await grave.waitFor()
    const card=grave.locator('.graveyard-card-entry .card-tile').first()
    if (!await card.count()) { await grave.getByRole('button',{name:'关闭墓地'}).click(); return false }
    await card.click()
    assert.equal(await page.locator('.mobile-card-inspector').count(),0,`${label}: graveyard card click must focus without auto-opening detail`)
    const handle=page.locator('.mobile-card-inspector-handle-global'); await handle.waitFor(); await handle.click()
    await page.locator('.mobile-card-inspector').waitFor()
    return {detail:await assertInspector(label), cleanup:async()=>{await page.locator('.mobile-card-inspector').getByRole('button',{name:'收起'}).click(); await page.locator('.mobile-card-inspector').waitFor({state:'detached'}); await grave.getByRole('button',{name:'关闭墓地'}).click()}}
  }
  async function inspectFromBoard(label) {
    const card=page.locator('.my-half .formation-slot .card-tile').first(); await card.click({force:true})
    const handle=page.locator('.mobile-card-inspector-handle'); await handle.click()
    await page.locator('.mobile-card-inspector').waitFor()
    return {detail:await assertInspector(label),cleanup:async()=>{await page.locator('.mobile-card-inspector').getByRole('button',{name:'收起'}).click();await page.locator('.mobile-card-inspector').waitFor({state:'detached'})}}
  }
  async function assertSourceCardDetail(name,state,label){
    let trigger=null
    if(name==='candidate')trigger=state.expanded.locator('.prompt-card-candidate').first()
    if(name==='graveyard')trigger=state.expanded.locator('.graveyard-card-entry .card-tile').first()
    if(!trigger||!await trigger.count())return null
    const triggerClass=await trigger.getAttribute('class')||''
    if(name==='candidate'&&!triggerClass.includes('selected'))await trigger.click()
    else if(name==='graveyard')await trigger.click()
    assert.equal(await page.locator('.mobile-card-inspector').count(),0,`${label}: source card click must not auto-open detail`)
    const handle=page.locator('.mobile-card-inspector-handle-global'); await handle.waitFor(); await handle.click()
    const detail=await assertInspector(`${label} source card`)
    await page.locator('.mobile-card-inspector').getByRole('button',{name:'收起'}).click(); await page.locator('.mobile-card-inspector').waitFor({state:'detached'}); await state.expanded.waitFor()
    if(name==='candidate')assert.equal(await state.expanded.locator('.prompt-card-candidate.selected').count(),1,`${label}: candidate selection must survive detail open/close`)
    return detail
  }

  async function open(kind) {
    if (kind === 'card-detail') {
      await page.locator('.my-half .formation-slot .card-tile').first().click()
      await page.locator('.mobile-card-inspector-handle').click()
      return { expanded:page.locator('.mobile-card-inspector'), minimize:page.locator('.mobile-card-inspector').getByRole('button',{name:'收起'}), restore:page.locator('.mobile-card-inspector-handle') }
    }
    if (kind === 'prompt' || kind === 'disaster-waiting') {
      if (kind === 'disaster-waiting') await page.evaluate(() => window.__disasterFixture.openPrompt())
      const panel=page.locator('.prompt-panel'); await panel.waitFor()
      if (kind === 'prompt') { await panel.locator('.prompt-card-candidate__name').first().click(); await page.waitForTimeout(60) }
      if(kind==='prompt'){
        assert.equal(await page.locator('.mobile-card-inspector').count(),0,'candidate click must not auto-open detail')
        assert.equal(await panel.locator('.prompt-card-candidate__inspect,.response-target-detail').count(),0,'candidate prompt must not expose per-card detail controls')
        assert.equal(await panel.getByRole('button',{name:/^(详情|来源详情)$/}).count(),0,'candidate prompt must not expose per-card detail-labelled buttons')
      }
      const selectedCount=kind === 'prompt' ? await page.locator('.prompt-card-candidate.selected').count() : 0
      if (kind === 'prompt') {
        const candidate=panel.locator('.prompt-card-candidate').first()
        assert.equal(selectedCount,1,`candidate selection did not register before minimization: ${await candidate.getAttribute('class')} / ${await candidate.getAttribute('aria-disabled')} / ${await candidate.textContent()}`)
      }
      return { expanded:panel, minimize:panel.locator('.prompt-minimize'), restore:page.locator('.prompt-minimized-bar button'), retained:async() => kind !== 'prompt' || assert.equal(await page.locator('.prompt-card-candidate.selected').count(),selectedCount) }
    }
    if (kind === 'master') {
      await page.locator('.mini-master').last().click(); const panel=page.locator('.master-dialog'); await panel.waitFor()
      return { expanded:panel, minimize:panel.locator('.master-minimize'), restore:page.locator('.master-minimized button') }
    }
    if (kind === 'graveyard') {
      await page.locator('.graveyard').last().click(); const panel=page.locator('.graveyard-window'); await panel.waitFor()
      return { expanded:panel, minimize:panel.getByRole('button',{name:'最小化墓地'}), restore:page.locator('.graveyard-minimized button'), skipGrave:true }
    }
    if (kind === 'faction' || kind === 'ability') {
      if (kind === 'faction') await page.locator('.resource-faction-action').last().click()
      else { await page.locator('.my-half .formation-slot .card-tile').first().click(); await page.locator('.card-context-actions').getByRole('button',{name:'发动',exact:true}).click() }
      const panel=page.locator('.faction-effect-dialog'); await panel.waitFor()
      return { expanded:panel, minimize:panel.locator('.faction-minimize'), restore:page.locator('.faction-minimized-bar button') }
    }
    if (kind === 'morale-view' || kind === 'morale-payment') {
      if (kind === 'morale-payment' && await page.locator('.prompt-minimize').count()) await page.locator('.prompt-minimize').click()
      await page.locator('.resource-morale-summary').last().click(); const panel=page.locator('.mobile-morale-overlay'); await panel.waitFor()
      if (kind === 'morale-payment') await panel.locator('.mobile-morale-choice[aria-disabled="false"]').first().click()
      return { expanded:panel, minimize:panel.getByRole('button',{name:'最小化',exact:true}), restore:page.locator('.mobile-morale-restore'), retained:async() => kind !== 'morale-payment' || assert.equal(await panel.locator('.mobile-morale-choice.selected').count(),1) }
    }
    if (kind === 'board-target' || kind === 'board-slot') {
      const panel=page.locator('.board-target-controls'); await panel.waitFor()
      const target=kind === 'board-target' ? page.locator('.formation-slot.targetable').first() : page.locator('.formation-slot.available').first()
      await target.click()
      const selected=kind === 'board-target' ? '.formation-slot.prompt-selected' : '.formation-slot.prompt-slot-selected'
      const count=await page.locator(selected).count()
      return { expanded:panel, minimize:panel.locator('.board-control-minimize'), restore:page.locator('.board-control-restore'), retained:async() => assert.equal(await page.locator(selected).count(),count) }
    }
    if (kind === 'combat') {
      const panel=page.locator('.combat-resolution-panel'); await panel.waitFor()
      return { expanded:panel, minimize:panel.locator('.combat-decision-minimize'), restore:page.locator('.combat-decision-restore') }
    }
    if (kind === 'record') {
      await page.locator('.mobile-record-trigger').click(); const panel=page.locator('.mobile-record-overlay:not(.mobile-morale-overlay)'); await panel.waitFor()
      return { expanded:panel, minimize:panel.getByRole('button',{name:'最小化',exact:true}), restore:page.locator('.mobile-record-restore') }
    }
    if (kind === 'settlement') {
      const panel=page.locator('.game-over'); await panel.waitFor()
      return { expanded:panel, minimize:panel.locator('.game-over-minimize'), restore:page.locator('.game-over-restore') }
    }
    if (kind === 'gm') {
      const panel=page.locator('.gm-panel'); await panel.waitFor()
      return { expanded:panel, minimize:panel.locator('> header button'), restore:page.locator('.gm-open') }
    }
    throw new Error(`unknown dialog ${kind}`)
  }

  for (const viewport of viewports) {
    const suffix=`${viewport.width}x${viewport.height}`
    for (const [name,query,kind] of cases) {
      await load(viewport,query)
      const state=await open(kind)
      await state.expanded.waitFor(); const sourceDetail=await assertSourceCardDetail(name,state,`${name} ${suffix}`); const expanded=`${name}-expanded-${suffix}.png`; await snap(expanded)
      await state.minimize.click(); await state.restore.waitFor(); await assertVisibleInsideViewport(state.restore,`${name} ${suffix}`); await assertBoardVisible(`${name} ${suffix}`)
      const minimized=`${name}-minimized-${suffix}.png`; await snap(minimized)
      let inspection=!state.skipGrave?await inspectFromGrave(`${name} ${suffix}`):false
      if (!inspection) inspection=await inspectFromBoard(`${name} ${suffix}`)
      assert.ok(inspection,`${name} ${suffix}: no card-detail inspection while minimized`)
      const inspect=`${name}-minimized-inspection-${suffix}.png`; await snap(inspect)
      await inspection.cleanup()
      assert.equal(await page.locator('.mobile-card-inspector').count(),0,`${name} ${suffix}: detail must close before source restoration`)
      await state.restore.waitFor(); await assertVisibleInsideViewport(state.restore,`${name} ${suffix} after detail close`)
      await state.restore.click(); await state.expanded.waitFor(); if (state.retained) await state.retained()
      const restored=`${name}-restored-${suffix}.png`; await snap(restored)
      manifest.results.push({name,kind,viewport:suffix,expanded,minimized,inspection:inspect,restored,sceneVisible:true,sourceCardDetail:sourceDetail,cardDetailWhileMinimized:true,detailGeometry:inspection.detail,stateRetained:true,detailClosedBeforeRestore:true,assertions:'passed'})
    }
  }
  for(const viewport of viewports){
    const suffix=`${viewport.width}x${viewport.height}`
    for(const [name,query,kind] of focusCases){
      await load(viewport,query)
      const state=await open(kind==='candidate'?'prompt':kind); await state.expanded.waitFor()
      let trigger
      if(kind==='candidate')trigger=state.expanded.locator('.prompt-card-candidate').first()
      else if(kind==='graveyard')trigger=state.expanded.locator('.graveyard-card-entry .card-tile').first()
      else trigger=state.expanded.locator('.l12-card-image').first()
      await trigger.waitFor()
      if(kind!=='candidate')await trigger.click()
      assert.equal(await page.locator('.mobile-card-inspector').count(),0,`${name} ${suffix}: card click must focus without auto-opening detail`)
      assert.equal(await state.expanded.locator('.prompt-card-candidate__inspect,.response-target-detail').count(),0,`${name} ${suffix}: per-card detail control remains`)
      const selectedBefore=kind==='candidate'?await state.expanded.locator('.prompt-card-candidate.selected').count():null
      if(kind==='candidate')assert.equal(selectedBefore,1,`${name} ${suffix}: short click must select exactly one candidate`)
      const focused=`${name}-focused-${suffix}.png`; await snap(focused)
      const handle=page.locator('.mobile-card-inspector-handle-global'); await handle.waitFor(); await assertVisibleInsideViewport(handle,`${name} ${suffix} handle`); await handle.click()
      const detail=await assertInspector(`${name} ${suffix}`); const detailFile=`${name}-detail-${suffix}.png`; await snap(detailFile)
      await page.locator('.mobile-card-inspector').getByRole('button',{name:'收起'}).click(); await page.locator('.mobile-card-inspector').waitFor({state:'detached'}); await state.expanded.waitFor()
      if(kind==='candidate')assert.equal(await state.expanded.locator('.prompt-card-candidate.selected').count(),selectedBefore,`${name} ${suffix}: candidate selection changed after detail close`)
      const closed=`${name}-closed-${suffix}.png`; await snap(closed)
      manifest.focusResults.push({name,kind,viewport:suffix,focused,detail:detailFile,closed,detailGeometry:detail,autoOpened:false,selectionRetained:true,perCardDetailControls:0,assertions:'passed'})
    }
  }
  const safeCaseNames=new Set(['candidate','graveyard','support','defense'])
  for(const profile of safeProfiles){
    const suffix=`${profile.name}-${profile.viewport.width}x${profile.viewport.height}`
    for(const [name,query,kind] of cases.filter(([caseName])=>safeCaseNames.has(caseName))){
      await load(profile.viewport,query,profile.insets)
      const state=await open(kind); await state.expanded.waitFor(); const sourceDetail=await assertSourceCardDetail(name,state,`safe ${name} ${suffix}`)
      const expanded=`safe-${name}-expanded-${suffix}.png`; await snap(expanded)
      await state.minimize.click(); await state.restore.waitFor(); await assertVisibleInsideViewport(state.restore,`safe ${name} ${suffix}`)
      const minimized=`safe-${name}-minimized-${suffix}.png`; await snap(minimized)
      let inspection=!state.skipGrave?await inspectFromGrave(`safe ${name} ${suffix}`):false
      if(!inspection)inspection=await inspectFromBoard(`safe ${name} ${suffix}`)
      const inspect=`safe-${name}-minimized-inspection-${suffix}.png`; await snap(inspect)
      await inspection.cleanup(); assert.equal(await page.locator('.mobile-card-inspector').count(),0,`safe ${name} ${suffix}: detail must close before source restoration`)
      await state.restore.waitFor(); await assertVisibleInsideViewport(state.restore,`safe ${name} ${suffix} after detail close`)
      await state.restore.click(); await state.expanded.waitFor(); if(state.retained)await state.retained()
      const restored=`safe-${name}-restored-${suffix}.png`; await snap(restored)
      manifest.safeResults.push({profile:profile.name,insets:profile.insets,name,kind,viewport:`${profile.viewport.width}x${profile.viewport.height}`,expanded,minimized,inspection:inspect,restored,sourceCardDetail:sourceDetail,detailGeometry:inspection.detail,stateRetained:true,detailClosedBeforeRestore:true,assertions:'passed'})
    }
  }
  await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets:zeroInsets})
  manifest.status='passed'
  fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2))
  console.log(JSON.stringify({output,status:manifest.status,cases:cases.length,viewports:viewports.length,results:manifest.results.length,safeResults:manifest.safeResults.length,focusResults:manifest.focusResults.length,screenshots:(manifest.results.length+manifest.safeResults.length)*4+manifest.focusResults.length*3,exception:manifest.exception.layer},null,2))
} finally { await browser.close() }
