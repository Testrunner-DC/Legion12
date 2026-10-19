import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import net from 'node:net'
import { spawn, execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'
import assert from 'node:assert/strict'

const repo = path.resolve(import.meta.dirname, '../..')
const front = path.join(repo, 'opcgpro-vue')
const out = process.env.L12_TUTORIAL_UI_OUTPUT || process.env.L12_TUTORIAL_REAL_OUTPUT
const cache = process.env.L12_TUTORIAL_UI_CACHE || process.env.L12_TUTORIAL_REAL_CACHE
const serverSource = process.env.L12_TUTORIAL_SERVER_SOURCE || path.join(repo, '服务端WebSocket/bin/Release/net10.0')
assert(out && cache, 'Explicit governed roots are required')
process.env.L12_TUTORIAL_REAL_OUTPUT = out
process.env.L12_TUTORIAL_REAL_CACHE = cache
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  `. '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' | Out-Null; . '${repo}/ops/windows/L12GeneratedStorage.ps1'; foreach($p in @($env:L12_TUTORIAL_REAL_OUTPUT,$env:L12_TUTORIAL_REAL_CACHE,$env:TEMP)){$null=Assert-L12GeneratedPath -Path $p -Purpose 'independent real tutorial UI'}`], { stdio: 'pipe' })
fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const report = { status: 'RUNNING', cases: [], transport: [], projectedObservations: [], errors: [], boundary: 'Actual local server, real account/WS, unchanged App/router/GameBoard; UI clicks only for gameplay. Completion is passively observed after the actual net consumer restores full/delta/card frames. No test/production deployment.' }
const firstChapterOnly = process.env.L12_TUTORIAL_FIRST_CHAPTER_ONLY === '1'
let visualCardManifest = null
if (process.env.L12_TUTORIAL_VISUAL_SLICE_ONLY === '1' || firstChapterOnly || process.env.L12_TUTORIAL_REAL_CARD_ART === '1') {
  const manifestResponse = await fetch('https://legion-12.com/card-assets/card-assets.manifest.json')
  assert(manifestResponse.ok, 'Official public card asset manifest must be readable')
  const manifest = await manifestResponse.json()
  assert(manifest?.cards?.['S01-0104'], 'Official public manifest must contain Han Xin')
  const deckSnapshot = JSON.parse(fs.readFileSync(path.join(repo,'服务端WebSocket/TwelveLegions/Data/tutorial-decks.v1.json'),'utf8'))
  const selectedIds = new Set(['S01-0104','S01-0117','ST01-10','S01-01C1','S01-0116','S01-0016','S01-0008',
    ...deckSnapshot.decks.flatMap(deck=>[deck.masterId,...deck.cardIds,...deck.moraleIds,...deck.specialIds])])
  for(const name of ['cards.s1.json','cards.s2.json','cards.st.json']) {
    const cards=JSON.parse(fs.readFileSync(path.join(repo,'服务端WebSocket/TwelveLegions/Data',name),'utf8'))
    for(const card of cards) if(card.cardType==='destruction') selectedIds.add(card.id)
  }
  visualCardManifest = { schemaVersion: 3, catalogVersion: manifest.catalogVersion, assetVersion: manifest.assetVersion,
    basePath: '/card-assets', cards: Object.fromEntries([...selectedIds].filter(id=>manifest.cards[id]).map(id=>[id,manifest.cards[id]])) }
  process.env.VITE_L12_CARD_ASSET_CDN = 'https://legion-12.com/card-assets'
  report.visualCardAsset = { cardId: 'S01-0104', contentHash: manifest.cards['S01-0104'].contentHash,
    source: 'official public card asset manifest; bounded introduction and two frozen public decks', entries: Object.keys(visualCardManifest.cards).length }
}
const boundSources = ['opcgpro-vue/src/App.vue','opcgpro-vue/src/l12/GamePage.vue','opcgpro-vue/src/l12/net.ts','opcgpro-vue/src/l12/site/TutorialPage.vue','opcgpro-vue/src/l12/tutorial/TutorialGuidanceOverlay.vue','opcgpro-vue/src/l12/tutorial/tutorialProgress.ts','opcgpro-vue/src/l12/tutorial/tutorialCourse.ts','opcgpro-vue/src/l12/tutorial/tutorialGuidance.ts','opcgpro-vue/src/l12/site/BattleHubPage.vue','opcgpro-vue/src/l12/game/GameBoard.vue','opcgpro-vue/src/l12/game/GameActions.vue','opcgpro-vue/src/l12/game/HandArea.vue','opcgpro-vue/src/l12/game/PlayerMat.vue','opcgpro-vue/src/l12/game/PromptOverlay.vue']
const hashes = () => Object.fromEntries(boundSources.map(name=>[name,crypto.createHash('sha256').update(fs.readFileSync(path.join(repo,name))).digest('hex').toUpperCase()]))
boundSources.push('opcgpro-vue/src/l12/tutorial/tutorialResponseGuidance.ts','opcgpro-vue/src/l12/game/BattleOverlayPortal.vue',
  'opcgpro-vue/src/l12/notifications/browserNotifications.ts','opcgpro-vue/src/l12/notifications/realtimeNotificationPolicy.ts',
  'opcgpro-vue/src/l12/notifications/useRealtimeNotifications.ts','opcgpro-vue/src/l12/site/L12SettingsModal.vue',
  'opcgpro-vue/src/l12/site/FriendRequestNotifications.vue')
boundSources.push('opcgpro-vue/scripts/test-tutorial-ui.mjs','服务端WebSocket/TwelveLegions/L12RoomManager.Tutorial.cs',
  '服务端WebSocket/TwelveLegions/Data/tutorial-decks.v1.json','opcgpro-vue/src/l12/tutorial/tutorialFinalChapter.ts')
report.sourceBefore = hashes()
report.paymentChecks=[]
report.notificationSettings=[]
report.backendAtStart = { boundary: 'Local built server binary SHA below; this UI test is not a Release, deployed-identity, or full-backend-suite proof.' }
const requireFront = createRequire(path.join(front, 'package.json'))
const { createServer } = await import(pathToFileURL(requireFront.resolve('vite')))
const { chromium } = requireFront('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const serverPort = 14641, uiPort = 14640
for (const port of [serverPort, uiPort]) await new Promise((resolve, reject) => {
  const probe = net.createServer(); probe.once('error', reject); probe.listen(port, '127.0.0.1', () => probe.close(resolve))
})
const hostRoot = path.join(cache, 'host')
assert(!fs.existsSync(hostRoot), 'Never reuse an old runtime or overwrite a prior attempt')
fs.cpSync(serverSource, hostRoot, { recursive: true,
  filter: source => path.basename(source) !== 'runtime' })
const dll = path.join(hostRoot, 'GrandUMIServer.dll')
report.dllSha256 = crypto.createHash('sha256').update(fs.readFileSync(dll)).digest('hex').toUpperCase()
const hostLog = fs.openSync(path.join(out, 'local-host.log'), 'wx')
const host = spawn('C:/Program Files/dotnet/dotnet.exe', [dll, String(serverPort)], {
  cwd: hostRoot, windowsHide: true, stdio: ['ignore', hostLog, hostLog], env: { ...process.env,
    L12_LISTEN_HOST: '127.0.0.1', L12_ALLOWED_ORIGINS: `http://127.0.0.1:${uiPort}`,
    L12_TUTORIAL_ENABLED: 'true', L12_CHAT_ENABLED: 'false' } })
let hostFailure, vite, context, page, hostDone=false
host.on('error', error => { hostFailure = error })
host.on('exit',()=>{hostDone=true})
const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
const observe = () => page.evaluate(async () => {
  const { l12State: s } = await import('/src/l12/net.ts')
  return { status: s.status, pending: s.pendingAction, notice: s.notice, room: s.room?.roomCode,
    tutorial: s.tutorial, gm: s.gmEnabled, phase: s.game?.phase, revision: s.game?.revision,
    winner: s.game?.winner, legalAttackTargets: s.game?.legalAttackTargets,
    matchId: s.game?.matchId, prompts: s.game?.prompts || [],
    self: s.game?.players.find(p=>p.playerIndex===s.game.you), defense: s.game?.pendingDefense,
    enemy: s.game?.players.find(p=>p.playerIndex!==s.game.you),
    learning: JSON.parse(localStorage.getItem('l12-tutorial-course:v2:account:'+encodeURIComponent(s.accountId||'')) || 'null') }
})
async function waitState(predicate, label, timeout = 12000) {
  const end = Date.now() + timeout
  while (Date.now() < end) { const state = await observe(); if (predicate(state)) return state; await pause(80) }
  throw new Error('State deadline: ' + label)
}
async function tap(locator, label) {
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  let point=null
  const deadline=Date.now()+10000
  while(Date.now()<deadline&&!point) {
    if(!await locator.isEnabled()){await pause(80);continue}
    point = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    for (const fx of [.12,.3,.55,.8]) for (const fy of [.15,.4,.65,.85]) {
      const x = rect.left + rect.width*fx, y = rect.top + rect.height*fy
      if (x < 0 || y < 0 || x > innerWidth || y > innerHeight) continue
      if (element.contains(document.elementFromPoint(x,y))) return {x,y}
    }
    return null
    })
    if(!point) await pause(80)
  }
  assert(point, 'No visible, unobstructed touch point: ' + label)
  if ((await page.viewportSize()).width < 1000) await page.touchscreen.tap(point.x,point.y)
  else {
    // Match an actual desktop interaction: hovering a candidate opens its
    // existing preview, so re-hit-test after hover layout settles, before press.
    for(let attempt=0;attempt<8;attempt++) {
      await page.mouse.move(point.x,point.y)
      await pause(100)
      const next=await locator.evaluate(element=>{
        const r=element.getBoundingClientRect(),x=r.left+r.width*.55,y=r.top+r.height*.4
        return x>=0&&y>=0&&x<=innerWidth&&y<=innerHeight&&element.contains(document.elementFromPoint(x,y))?{x,y}:null
      })
      assert(next,'Target became obstructed after hover: '+label)
      if(Math.abs(next.x-point.x)<1&&Math.abs(next.y-point.y)<1)break
      point=next
      if(attempt===7)throw new Error('Target did not settle after hover: '+label)
    }
    await page.mouse.down(); await page.mouse.up()
  }
}
async function resolveOwnPrompt() {
  const state = await observe()
  const prompt = state.prompts.find(p => p.playerIndex === 0)
  if (!prompt || state.pending) return false
  if ((prompt.kind || '').includes('resource') || ['resource-return', 'resource-payment', 'resource-selection'].includes(prompt.data?.choiceMode)) {
    const mobile = page.locator('.mobile-morale-choice:not(.unavailable):not(.selected)')
    const orb = page.locator('[data-player-index="0"] .morale-orb.payable:not(.selected)')
    const picker=page.locator('[data-player-index="0"] .mobile-morale-stack-trigger')
    if(await picker.count()&&!await mobile.count()) {
      await tap(picker,'open actual mobile morale picker')
      await mobile.first().waitFor({state:'visible'})
    }
    for (let i=0;i<prompt.minChoose;i++) {
    if (await mobile.count()) await tap(mobile.first(), 'select payment')
    else if (await orb.count()) {
      let candidate=null
      for(let index=(await orb.count())-1;index>=0;index--) {
        const item=orb.nth(index)
        if(await item.evaluate(element=>{
          const r=element.getBoundingClientRect()
          return [.15,.4,.65,.85].some(fy=>[.12,.3,.55,.8].some(fx=>{
            const x=r.left+r.width*fx,y=r.top+r.height*fy
            return x>=0&&y>=0&&x<=innerWidth&&y<=innerHeight&&element.contains(document.elementFromPoint(x,y))
          }))
        })){candidate=item;break}
      }
      assert(candidate,'At least one real board payment orb must be touchable')
      await tap(candidate, 'select payment orb')
    }
    else {
      const option = page.locator('.prompt-choices button:not(.decline-action):not([disabled])').first()
      await tap(option, 'select payment option')
    }
    if(prompt.minChoose>1 && i===0) {
      await page.waitForFunction(()=>document.querySelector('[data-ui-contract="tutorial-authoritative-guidance"] h2')?.textContent==='选择亮起的士气')
      const selected=await page.locator('[data-tutorial-payment-resource][aria-pressed="true"]').evaluateAll(nodes=>({
        nodes:nodes.length,unique:new Set(nodes.map(node=>node.getAttribute('data-tutorial-payment-resource'))).size,
      }))
      assert.equal(selected.unique,1)
      const confirm=page.locator('[data-tutorial-action="confirm-payment"]').filter({visible:true})
      assert(await confirm.count()>0)
      assert(await confirm.first().isDisabled(),'One selected resource cannot confirm a two-resource cost')
      report.paymentChecks.push({viewport:await page.viewportSize(),minimum:prompt.minChoose,selected,title:'选择亮起的士气'})
    }
    }
    await tap(page.getByRole('button', { name: /确认支付|确认费用|确认选择|确认返还/ }).filter({ visible: true }).first(), 'confirm payment')
    return true
  }
  if (prompt.validChoices?.includes('pass')) {
    await tap(page.getByRole('button', { name: /不响应|不发动|让过/ }).filter({ visible: true }).first(), 'pass response')
    const confirm = page.getByRole('button', { name: /确认选择/ }).filter({ visible: true }).first()
    if (await confirm.count() && await confirm.isEnabled()) await tap(confirm, 'confirm response')
    return true
  }
  throw new Error('Unsupported real prompt in acceptance: ' + prompt.kind)
}
async function settleTo(predicate, label) {
  for (let i=0;i<80;i++) {
    const state = await observe()
    if (!state.pending && predicate(state)) return state
    if (!state.pending && state.prompts.some(p=>p.playerIndex===0)) await resolveOwnPrompt()
    else await pause(120)
  }
  throw new Error('Could not settle: ' + label)
}
async function completedFrame(scene, matchId, transportStart) {
  const deadline=Date.now()+20000
  while(Date.now()<deadline) {
    report.projectedObservations=await page.evaluate(()=>window.__tutorialProjectionObservations||[])
    const completion=report.projectedObservations.find(frame=>frame.kind==='completed'
      &&frame.tutorial?.lessonId===scene&&frame.tutorial?.status==='completed'
      &&frame.result?.matchId===matchId)
    if(completion) return completion
    const current=await observe()
    if(current.matchId===matchId&&!current.pending&&current.prompts.some(prompt=>prompt.playerIndex===0)) await resolveOwnPrompt()
    else await pause(80)
  }
  throw new Error('No authoritative completed frame: '+scene)
}
try {
  const readyDeadline = Date.now()+30000
  let healthy = false
  while (Date.now()<readyDeadline) {
    if (hostFailure || host.exitCode!==null) throw hostFailure || new Error('Local host exited')
    try { const response=await fetch(`http://127.0.0.1:${serverPort}/health`); healthy=response.ok; if(healthy) break } catch {}
    await pause(150)
  }
  assert(healthy, 'Local health did not become ready')
  process.env.VITE_WS_URL=`ws://127.0.0.1:${serverPort}/ws`
  process.env.L12_VITE_CACHE_DIR=path.join(cache,'vite')
  vite=await createServer({root:front,configLoader:'runner',cacheDir:path.join(cache,'vite'),
    plugins: visualCardManifest ? [{ name: 'tutorial-visual-card-manifest', configureServer(server) {
      server.middlewares.use('/card-assets/card-assets.manifest.json',(_request,response)=>{
        response.setHeader('content-type','application/json; charset=utf-8')
        response.end(JSON.stringify(visualCardManifest))
      })
    } }] : [], server:{host:'127.0.0.1',port:uiPort,strictPort:true}})
  await vite.listen()
  context=await chromium.launchPersistentContext(path.join(cache,'profile'),{headless:true,channel:'msedge',hasTouch:true,viewport:{width:1366,height:768}})
  await context.addInitScript(()=>{
    window.__notificationQA={requests:0,shown:[]}
    class QaNotification {
      static permission='default'
      static async requestPermission(){window.__notificationQA.requests++;this.permission='granted';return 'granted'}
      constructor(title,options){window.__notificationQA.shown.push({title,options})}
      close(){}
    }
    Object.defineProperty(window,'Notification',{value:QaNotification,configurable:true})
  })
  page=await context.newPage(); page.setDefaultTimeout(10000)
  page.on('dialog',dialog=>dialog.accept())
  page.on('pageerror',error=>report.errors.push(error.message))
  page.on('websocket',socket=>{
    socket.on('framesent',frame=>{try{const value=JSON.parse(String(frame.payload));if(['createTutorial','gameAction','leaveRoom'].includes(value.type)) report.transport.push({direction:'sent',type:value.type,requestId:value.requestId,lessonId:value.lessonId,command:value.command?.type})}catch{}})
    socket.on('framereceived',frame=>{try{const value=JSON.parse(String(frame.payload));if(value.type==='gameState'&&value.tutorial) {
      const self=value.state?.players?.find(player=>player.playerIndex===value.state.you)||value.state?.players?.[0]
      report.transport.push({direction:'received',type:value.type,requestId:value.requestId,revision:value.state?.revision,tutorial:value.tutorial,
        result:value.tutorial.status==='completed'?{matchId:value.state?.matchId,activeMorale:self?.morale?.filter(card=>!card.tapped).length,
          handCount:self?.handCount??self?.hand?.length,masterHp:self?.master?.hp,
          field:self?.field?.map(row=>row.map(card=>card?{instanceId:card.instanceId,cardId:card.cardId,tapped:card.tapped,troops:card.troops}:null)),
          graveyard:self?.graveyard?.map(card=>({instanceId:card.instanceId,cardId:card.cardId}))}:undefined})
    }else if(['tutorialRejected','actionRejected','tutorialRestartRequired','roomLeft'].includes(value.type))report.transport.push({direction:'received',type:value.type,requestId:value.requestId})}catch{}})
  })

  await page.goto('http://127.0.0.1:'+uiPort+'/battle')
  await page.evaluate(async()=>{
    const p=await import('/src/l12/platform.ts')
    await p.register('TQA'+Math.random().toString(36).slice(2,8),'Qa'+crypto.randomUUID()+'!')
    const{router}=await import('/src/router/index.ts');await router.push('/battle')
  })
  await waitState(s=>s.status==='online','real authenticated connection')
  await page.evaluate(async()=>{
    const moduleSource=await (await fetch('/src/l12/net.ts')).text()
    const vueModule=moduleSource.match(/from\s+["']([^"']*\/vue\.js[^"']*)["']/)?.[1]
    if(!vueModule) throw new Error('Cannot locate the actual net consumer Vue dependency')
    const {watch}=await import(vueModule)
    const {l12State:s}=await import('/src/l12/net.ts')
    const observations=[]
    window.__tutorialProjectionObservations=observations
    // Passive observers only. Never send commands, change progress or bypass presentation.
    watch(()=>s.tutorial,tutorial=>{
      if(tutorial?.status!=='completed'||tutorial.matchId!==s.game?.matchId) return
      const self=s.game.players.find(player=>player.playerIndex===s.game.you)
      observations.push(JSON.parse(JSON.stringify({kind:'completed',order:observations.length,revision:s.game.revision,
        tutorial:{lessonId:tutorial.lessonId,status:tutorial.status,stepId:tutorial.stepId},
        result:{matchId:s.game.matchId,phase:s.game.phase,winner:s.game.winner,activeMorale:self.morale.filter(card=>!card.tapped).length,
          handCount:self.handCount??self.hand?.length,masterHp:self.master.hp,
          field:self.field.map(row=>row.map(card=>card?{instanceId:card.instanceId,cardId:card.cardId,tapped:card.tapped,troops:card.troops}:null)),
          graveyard:self.graveyard.map(card=>({instanceId:card.instanceId,cardId:card.cardId}))}})))
    },{flush:'sync'})
    watch(()=>s.leavingRoom,leaving=>{
      if(leaving) observations.push({kind:'leavingRoom',order:observations.length,matchId:s.game?.matchId,
        lessonId:s.tutorial?.lessonId,status:s.tutorial?.status})
    },{flush:'sync'})
  })
  await tap(page.locator('.mode-tabs').getByRole('button',{name:'教学',exact:true}), 'inline teaching tab')
  assert.equal(await page.getByRole('link',{name:'进入教学目录',exact:true}).count(),0)
  assert(await page.locator('.tutorial-page.embedded [data-tutorial-introduction]').isVisible(),
    'Teaching tab must show mainline dialogue directly, not a directory link')

  async function readIntroduction(viewport) {
    const panel=page.locator('[data-tutorial-introduction]')
    await panel.waitFor()
    const titles=[]
    for(let i=0;i<5;i++){
      const title=await panel.locator('h2').innerText();titles.push(title)
      const text=await panel.innerText()
      assert(!/部署|法力|召唤/.test(text),'Teaching must use Twelve Legions terminology')
      assert.equal(await panel.locator('.conversation-choices').getByRole('button').count(),1,'Each introduction step has one main action')
      assert(!/我想了解|回到主线|再看卡牌|再认识/.test(text),'Introduction must stay linear')
      await page.waitForFunction(()=>[...document.querySelectorAll('[data-tutorial-introduction] .intro-card img')]
        .every(image=>image.complete&&image.naturalWidth>0&&image.closest('.l12-card-image')?.dataset.source!=='placeholder'))
      const layout=await panel.evaluate(element=>{
        const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom}}
        return {panel:rect(element),cards:[...element.querySelectorAll('.intro-card')].map(rect),
          notice:document.querySelector('.tutorial-notice[role="alert"]')?.textContent.trim()||''}
      })
      assert.equal(layout.notice,'','Successful prior practice must not leave a red failure notice on the introduction')
      for(const card of layout.cards) assert(card.x>=layout.panel.x-1&&card.right<=layout.panel.right+1,'Introduction cards fit their panel')
      await page.screenshot({path:path.join(out,viewport.width+'-introduction-'+i+'.png')})
      report.cases.push({scope:'introduction-visual',viewport,step:i,title,layout})
      if(['手牌来自牌库','士气用来支付费用'].includes(title)){
        assert((await panel.locator('.resource-flow').innerText()).includes('→'),'Resources need an origin-to-use explanation')
        await page.screenshot({path:path.join(out,viewport.width+'-intro-'+i+'.png')})
      }
      await tap(panel.locator('.conversation-choices').getByRole('button',{name:i===4?'进入战场':'下一步',exact:true}),'read mainline '+i)
    }
    assert.deepEqual(titles,['欢迎，主宰者','认识三种卡牌','手牌来自牌库','士气用来支付费用','看清数值，再开始实操'])
    return titles
  }
  const guide=()=>page.locator('[data-ui-contract="tutorial-authoritative-guidance"]')
  async function inspectGuide(viewport,label) {
    const shell=guide();await shell.waitFor()
    const h2=await shell.locator('h2').innerText()
    const body=await shell.locator('.tutorial-guidance > p').first().innerText()
    assert(body.trim(),'Every practical step has one concise actionable explanation')
    assert.equal(await shell.locator('.tutorial-result-line').count(),0,'Do not repeat cost/result in a second explanatory block')
    assert.equal(await shell.locator('.tutorial-help,.tutorial-cause-and-result').count(),0)
    assert(!/DTO|matchId|权威|部署|法力|召唤/.test(await shell.innerText()),'No internal implementation language in teaching')
    const bounds=await shell.boundingBox()
    assert(bounds&&bounds.x>=-1&&bounds.y>=-1&&bounds.x+bounds.width<=viewport.width+1&&bounds.y+bounds.height<=viewport.height+1,
      'Guide must fit physical viewport '+label)
    const logicalWidth=await shell.evaluate(e=>e.clientWidth)
    assert(logicalWidth>=280,'Readable guide width '+label)
    if(await shell.getAttribute('data-tutorial-scene')==='preparation'&&viewport.width<1000) {
      const promptBounds=await page.locator('.l12-prompt-overlay:not(.minimized) .prompt-panel').boundingBox()
      assert(promptBounds,'The actual preparation choices remain visible')
      const overlapX=Math.min(bounds.x+bounds.width,promptBounds.x+promptBounds.width)-Math.max(bounds.x,promptBounds.x)
      const overlapY=Math.min(bounds.y+bounds.height,promptBounds.y+promptBounds.height)-Math.max(bounds.y,promptBounds.y)
      assert(overlapX<=2||overlapY<=2,'Preparation explanation must not obscure its cards or controls')
    }
    // Read-only geometry, not test control: arrows and focus never receive pointer input.
    const geometry=await page.locator('.tutorial-guide-layer').evaluate(e=>({
      pointerEvents:getComputedStyle(e).pointerEvents,
      rings:e.querySelectorAll('.tutorial-focus-ring').length,
      arrow:e.querySelector('.tutorial-guide-arrow > path')?.getAttribute('d')||null
    }))
    assert.equal(geometry.pointerEvents,'none')
    const unavailable=await shell.locator('.tutorial-anchor-unavailable').count()
    if(!unavailable && await shell.getAttribute('data-tutorial-anchor-expected')==='true') {
      await page.waitForFunction(()=>Boolean(document.querySelector('.tutorial-focus-ring')&&document.querySelector('.tutorial-guide-arrow > path')),null,{timeout:2000})
      geometry.rings=await page.locator('.tutorial-focus-ring').count()
      geometry.arrow=await page.locator('.tutorial-guide-arrow > path').getAttribute('d')
      assert(geometry.rings>0&&geometry.arrow,'Visible reference requires a real highlight and arrow '+label)
    }
    let details=null
    if((await shell.getAttribute('class'))?.includes('detail-mode')) {
      await page.locator('.catalog-detail-mask.tutorial-detail-mask').waitFor()
      const expectedFields=(await shell.getAttribute('data-tutorial-detail-fields')).split('|').filter(Boolean)
      assert(expectedFields.includes('费用')&&expectedFields.includes('效果'),'A card introduction must highlight its actual cost and effect')
      await page.waitForFunction(()=>{
        const image=document.querySelector('.tutorial-detail-mask .archive-modal-image .l12-card-image__img')
        return image?.complete&&image.naturalWidth>0&&!image.closest('.placeholder')
      })
      details=await page.evaluate(()=>{
        const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}}
        const dialog=document.querySelector('.tutorial-detail-mask .catalog-detail-dialog')
        return {dialog:rect(dialog),fields:[...dialog.querySelectorAll('.tutorial-detail-focus')].map(e=>({text:e.textContent.trim(),...rect(e)})),
          labels:[...dialog.querySelectorAll('dt.tutorial-detail-focus')].map(e=>e.textContent.trim())
            .concat(dialog.querySelector('.archive-effect.tutorial-detail-focus')?['效果']:[]),
          allLabels:[...dialog.querySelectorAll('dt')].map(e=>e.textContent.trim()),
          image:rect(dialog.querySelector('.archive-modal-image')),guide:rect(document.querySelector('.tutorial-guidance-shell'))}
      })
      assert.deepEqual(details.labels.toSorted(),expectedFields.toSorted(),'Highlight every requested real card field; do not invent troops for a tactic')
      if(!expectedFields.includes('兵力')) assert(!details.allLabels.includes('兵力'),'A tactic must not gain a fictitious zero-troops field')
      assert.equal(details.fields.length,expectedFields.reduce((count,field)=>count+(field==='效果'?1:2),0),'Every numeric field includes its label and value')
      for(const field of [...details.fields,details.image]) {
        assert(field.x>=details.dialog.x-1&&field.y>=details.dialog.y-1
          &&field.right<=details.dialog.right+1&&field.bottom<=details.dialog.bottom+1,'Every teaching detail field and whole card image must fit its dialog')
        assert(field.x>=-1&&field.y>=-1&&field.right<=viewport.width+1&&field.bottom<=viewport.height+1,'Teaching details must not be clipped by the physical viewport')
        const overlapX=Math.min(field.right,details.guide.right)-Math.max(field.x,details.guide.x)
        const overlapY=Math.min(field.bottom,details.guide.bottom)-Math.max(field.y,details.guide.y)
        assert(overlapX<=2||overlapY<=2,'The guide must not cover the card image or highlighted fields')
      }
      const mutations=await page.evaluate(async()=>{
        let count=0
        const observer=new MutationObserver(records=>{count+=records.filter(record=>record.target.closest('.tutorial-detail-mask')).length})
        observer.observe(document.body,{subtree:true,attributes:true,attributeFilter:['class']})
        await new Promise(resolve=>setTimeout(resolve,240));observer.disconnect();return count
      })
      assert(mutations<=3,'Settled field highlights must not repeatedly remove/re-add themselves')
      details.settledHighlightMutations=mutations
    }
    return {title:h2,body,logicalWidth,bounds,geometry,unavailable:Boolean(unavailable),details}
  }
  async function readScene(viewport,scene,readings) {
    for(let i=0;i<10;i++) {
      const shell=guide();await shell.waitFor()
      if(await shell.getAttribute('data-tutorial-reading')!=='true')return
      const record=await inspectGuide(viewport,scene+' reading '+i)
      if(['card','morale','card-source','defense','attack-result'].includes(await shell.getAttribute('data-tutorial-guide-id'))
        || scene.startsWith('preparation') || scene==='finale' || (await shell.getAttribute('class'))?.includes('detail-mode'))
        await page.screenshot({path:path.join(out,viewport.width+'-'+scene+'-'+(await shell.getAttribute('data-tutorial-guide-id'))+'-'+readings.length+'.png')})
      readings.push(record)
      const previous=await shell.getAttribute('data-tutorial-guide-id')
      await tap(shell.getByRole('button',{name:'下一步',exact:true}),'read explanation '+record.title)
      await page.waitForFunction(id=>{
        const shell=document.querySelector('[data-ui-contract="tutorial-authoritative-guidance"]')
        return shell?.getAttribute('data-tutorial-guide-id')!==id||shell?.getAttribute('data-tutorial-reading')!=='true'
      },previous)
    }
    throw new Error('Unbounded scene dialogue')
  }
  async function visibleAction(action,instanceId) {
    const selector='[data-tutorial-action="'+action+'"]'+(instanceId?'[data-tutorial-card-instance="'+instanceId+'"]':'')
    return page.locator(selector).filter({visible:true}).first()
  }
  async function choosePromptCard(promptId,instanceId,label) {
    await tap(page.locator('[data-tutorial-choice="'+instanceId+'"]').filter({visible:true}).first(),label)
    const deadline=Date.now()+10000
    while(Date.now()<deadline) {
      const state=await observe()
      if(state.pending||!state.prompts.some(p=>p.promptId===promptId)) return
      const confirm=page.locator('[data-tutorial-action="confirm-prompt"]').filter({visible:true}).first()
      if(await confirm.count()&&await confirm.isEnabled()) {await tap(confirm,'confirm '+label);return}
      await pause(80)
    }
    throw new Error('Chosen prompt neither submitted nor exposed its confirmation: '+label)
  }
  const settingsOnly=process.env.L12_NOTIFICATION_SETTINGS_ONLY==='1'
  const visualSliceOnly=process.env.L12_TUTORIAL_VISUAL_SLICE_ONLY==='1'
  report.mode=settingsOnly?'actual-settings-only':visualSliceOnly?'real-component-visual-slice':firstChapterOnly?'actual-first-chapter-auto-handoff':'continuous-course-and-actual-settings'
  async function inspectNotificationSettings(viewport) {
    assert.equal(await page.evaluate(()=>window.__notificationQA.requests),viewport.width===1366?0:1,
      'Permission must not be requested by import, connection, route changes or teaching')
    const mobileNavigation=page.getByRole('button',{name:'打开导航',exact:true}).filter({visible:true})
    if(await mobileNavigation.count()) await tap(mobileNavigation,'open existing mobile navigation')
    await tap(page.getByRole('button',{name:'设置',exact:true}).filter({visible:true}).first(),'open notification settings')
    const settings=page.locator('[data-ui-contract="l12-settings-all-features"]')
    const notificationRow=settings.locator('.setting-row').filter({hasText:'网页通知'})
    assert((await notificationRow.innerText()).includes('仅限此浏览器'))
    await tap(notificationRow.getByRole('button',{name:'开启此浏览器通知',exact:true}),'explicitly enable browser notifications')
    await page.waitForFunction(()=>window.__notificationQA.requests===1)
    await notificationRow.getByRole('button',{name:'关闭此浏览器通知',exact:true}).waitFor()
    const style=await settings.evaluate(async element=>{
      const mask=element.closest('.site-modal-mask')||element
      const animations=mask.getAnimations({subtree:true}).filter(animation=>Number.isFinite(animation.effect?.getComputedTiming().iterations))
      await Promise.all(animations.map(animation=>animation.finished.catch(()=>{})))
      return {maskOpacity:getComputedStyle(mask).opacity,background:getComputedStyle(element).backgroundColor}
    })
    assert(Number(style.maskOpacity)>=.99,'Capture only the settled notification settings, not its fade-in')
    await page.screenshot({path:path.join(out,viewport.width+'-notification-settings.png')})
    const toggleBounds=await notificationRow.getByRole('button').boundingBox()
    assert(toggleBounds&&toggleBounds.x>=0&&toggleBounds.y>=0&&toggleBounds.x+toggleBounds.width<=viewport.width+1
      &&toggleBounds.y+toggleBounds.height<=viewport.height+1,'Notification toggle must fit and be reachable')
    await tap(notificationRow.getByRole('button',{name:'关闭此浏览器通知',exact:true}),'disable browser notifications')
    assert.equal(await page.evaluate(()=>window.__notificationQA.requests),1,'Disabling or re-enabling granted permission never requests it again')
    await tap(settings.getByRole('button',{name:'关闭设置',exact:true}),'close notification settings')
    report.notificationSettings.push({viewport,explicitPermissionRequests:1,toggleBounds,style,
      scope:'real settings UI with controlled Notification API; not actual OS delivery or four live server event chains'})
  }
  if(visualSliceOnly) {
    const desktop={width:1366,height:768}
    const visualViewports=[desktop,{width:390,height:844},{width:844,height:390}]
    const settledShellLayout=async viewport=>{
      const before=await page.evaluate(()=>{
        const sidebar=document.querySelector('.site-sidebar'),content=document.querySelector('.site-content'),root=document.querySelector('.site-shell')
        const rect=e=>{const r=e?.getBoundingClientRect();return r?{x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}:null}
        return {root:rect(root),sidebar:rect(sidebar),content:rect(content),sidebarTransform:sidebar?getComputedStyle(sidebar).transform:null,
          sidebarTransition:sidebar?getComputedStyle(sidebar).transition:null,sidebarClass:sidebar?.className}
      })
      if(viewport.width<=700||viewport.height<=640) await page.waitForFunction(()=>document.querySelector('.site-sidebar')?.getBoundingClientRect().right<=1)
      const after=await page.evaluate(()=>{
        const sidebar=document.querySelector('.site-sidebar'),content=document.querySelector('.site-content'),root=document.querySelector('.site-shell')
        const rect=e=>{const r=e?.getBoundingClientRect();return r?{x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}:null}
        return {root:rect(root),sidebar:rect(sidebar),content:rect(content),sidebarTransform:sidebar?getComputedStyle(sidebar).transform:null,
          sidebarTransition:sidebar?getComputedStyle(sidebar).transition:null,sidebarClass:sidebar?.className}
      })
      return {before,after}
    }
    for(const viewport of visualViewports) {
      await page.setViewportSize(viewport)
      await page.locator('[data-tutorial-introduction]').waitFor()
      const shellLayout=await settledShellLayout(viewport)
      await page.waitForFunction(()=>[...document.querySelectorAll('.intro-card-native-image')].every(image=>image.complete&&image.naturalWidth>0))
      const layout=await page.evaluate(()=>{
        const box=selector=>{const e=document.querySelector(selector);const r=e?.getBoundingClientRect();return r?{x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}:null}
        const content=document.querySelector('.site-content')
        return {documentClientWidth:document.documentElement.clientWidth,documentScrollWidth:document.documentElement.scrollWidth,
          windowScrollX:scrollX,contentScrollLeft:content?.scrollLeft,contentClientWidth:content?.clientWidth,contentScrollWidth:content?.scrollWidth,
          hub:box('.battle-hub'),tutorial:box('.tutorial-page'),conversation:box('.tutorial-conversation')}
      })
      report.cases.push({scope:'goal-layout-observation',viewport,layout,shellLayout})
      await page.screenshot({path:path.join(out,viewport.width+'x'+viewport.height+'-goal-introduction.png')})
    }
    await page.setViewportSize(desktop)
    await settledShellLayout(desktop)
    const panel=page.locator('[data-tutorial-introduction]')
    await tap(panel.locator('.conversation-choices').getByRole('button',{name:'下一步',exact:true}),'open card-type introduction')
    await panel.getByRole('button',{name:'查看韩信卡牌详情',exact:true}).waitFor()
    const visualCases=[]
    for(const viewport of visualViewports) {
      const existing=page.getByRole('button',{name:'关闭卡牌详情',exact:true})
      if(await existing.count()) await tap(existing,'close details before viewport switch')
      await page.setViewportSize(viewport)
      await settledShellLayout(viewport)
      await tap(panel.getByRole('button',{name:'查看韩信卡牌详情',exact:true}),'open real shared card details '+viewport.width)
      await page.locator('.catalog-detail-dialog').waitFor()
      await page.waitForFunction(()=>document.querySelectorAll('.catalog-detail-dialog .tutorial-intro-detail-focus').length>=5)
      await page.waitForFunction(()=>{
        const image=document.querySelector('.catalog-detail-dialog .l12-card-image__img')
        return image?.complete&&image.naturalWidth>0&&image.closest('.l12-card-image')?.dataset.source!=='placeholder'
      })
      const layout=await page.evaluate(()=>{
        const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}}
        const dialog=document.querySelector('.catalog-detail-dialog')
        const focused=[...document.querySelectorAll('.catalog-detail-dialog .tutorial-intro-detail-focus')]
        return {dialog:rect(dialog),focus:focused.map(e=>({text:e.textContent.trim(),...rect(e)}))}
      })
      assert(layout.focus.some(field=>field.text==='费用')&&layout.focus.some(field=>field.text==='兵力')
        &&layout.focus.some(field=>field.text.includes('效果')),'Cost, troops and effect are highlighted')
      assert(layout.dialog.x>=0&&layout.dialog.y>=0&&layout.dialog.right<=viewport.width+1&&layout.dialog.bottom<=viewport.height+1,
        'Shared details fit the viewport')
      assert(layout.focus.every(field=>field.y>=layout.dialog.y-1&&field.bottom<=layout.dialog.bottom+1),
        'Every focused field is actually visible inside the shared details viewport')
      const screenshot=path.join(out,viewport.width+'x'+viewport.height+'-card-details-focus.png')
      await page.screenshot({path:screenshot})
      visualCases.push({viewport,screenshot,layout})
    }
    await tap(page.getByRole('button',{name:'关闭卡牌详情',exact:true}),'close short-landscape card details')
    await tap(panel.locator('.conversation-choices').getByRole('button',{name:'下一步',exact:true}),'continue from short-landscape card introduction')
    assert.equal(await panel.locator('.guide-speaker h2').innerText(),'手牌来自牌库')
    report.visualInteractionChecks={shortLandscapeClose:'touch-reachable',shortLandscapeNext:'touch-reachable',nextTitle:'手牌来自牌库'}
    report.cases.push({scope:'Visual slice only: real shared card details for a card in the frozen public Chang\u2019e deck; no gameplay-chain claim.',visualCases})
  } else for(const viewport of [{width:1366,height:768},{width:844,height:390},{width:390,height:844}]) {
    await page.setViewportSize(viewport)
    if(settingsOnly){await inspectNotificationSettings(viewport);report.cases.push({viewport,scope:'settings-only; no gameplay'});continue}
    if(viewport.width!==1366) {
      await tap(page.getByRole('button',{name:'重新学习第一章',exact:true}),'repeat coherent chapter')
    }
    const titles=await readIntroduction(viewport)
    const chapter={viewport,introduction:titles,scenes:[]}
    for(const [index,scene] of ['deploy','move','combat'].entries()) {
      const initial=await waitState(s=>s.tutorial?.lessonId===scene&&!s.pending,'start '+scene)
      const transportStart=report.transport.length
      await page.waitForURL('**/game')
      assert.equal(initial.gm,false)
      assert.equal(initial.self.master.masterName,'嫦娥')
      assert(initial.self.hand.length<=3,'Every first-chapter practice must use only a few real hand cards, not hide a larger hand')
      const readings=[]
      await readScene(viewport,scene,readings)
      const beforeAction=await inspectGuide(viewport,scene+' action')
      const practice=scene==='deploy'?initial.self.hand.find(c=>c.cardId==='ST01-05')
        :scene==='move'?initial.self.field[1][1]:initial.self.field[0][0]
      assert.equal(practice?.cardId,'ST01-05','Use the practice card from the frozen public Chang\u2019e deck')
      const instance=practice.instanceId
      await page.screenshot({path:path.join(out,viewport.width+'-'+scene+'-action.png')})
      if(scene==='deploy'||scene==='move'){
        await tap(page.locator(scene==='deploy'
          ?'.hand-card-wrap[data-tutorial-card-instance="'+instance+'"]'
          :'[data-tutorial-anchor="field-slot"][data-tutorial-card-instance="'+instance+'"]'), 'select practice '+scene)
        const action=await visibleAction(scene==='deploy'?'play-card':'move',instance)
        if(await action.count())await tap(action,'choose '+scene+' action')
        await page.locator('[data-player-index="0"] .formation-slot.available').first().waitFor()
        await tap(page.locator('[data-player-index="0"] .formation-slot.available').first(),'legal destination')
      } else {
        await tap(page.locator('[data-tutorial-anchor="field-slot"][data-tutorial-card-instance="'+instance+'"]'),'select attacker')
        await tap(await visibleAction('attack',instance),'attack')
        await tap(page.locator('[data-player-index="1"] .mini-master.targetable'),'attack enemy master')
        await settleTo(s=>s.tutorial?.stepId==='end-turn','own attack')
        await readScene(viewport,scene,readings)
        await tap(await visibleAction('end-turn'),'end turn')
        await settleTo(s=>s.tutorial?.stepId==='defend','opponent attack')
        await readScene(viewport,scene,readings)
        await inspectGuide(viewport,'real defense decision')
        await tap(page.getByRole('button',{name:'不抵挡',exact:true}).filter({visible:true}).first(),'decline defense')
      }
      const final=await completedFrame(scene,initial.matchId,transportStart)
      assert(final.revision>initial.revision)
      if(index<2) {
        const next=await waitState(s=>s.tutorial?.lessonId===['deploy','move','combat'][index+1]&&!s.pending,'continuous next scene')
        assert.equal(next.learning.reviewedScenes.length,index+1)
        const handoff=report.transport.slice(transportStart)
        const leave=handoff.findIndex(e=>e.direction==='sent'&&e.type==='leaveRoom')
        const ack=handoff.findIndex(e=>e.direction==='received'&&e.type==='roomLeft')
        const create=handoff.findIndex(e=>e.direction==='sent'&&e.type==='createTutorial')
        report.projectedObservations=await page.evaluate(()=>window.__tutorialProjectionObservations||[])
        const leaving=report.projectedObservations.filter(event=>event.kind==='leavingRoom'&&event.matchId===initial.matchId)
        assert.equal(leaving.length,1,'One automatic leave for this exact completed match')
        assert.equal(leaving[0].status,'completed')
        assert(leaving[0].order>final.order,'Completion is observed before automatic room retirement')
        assert(leave>=0&&ack>leave&&create>ack,'Auto next scene follows actual roomLeft')
        assert.equal(handoff.filter(e=>e.direction==='sent'&&e.type==='leaveRoom').length,1,'Only one leave request for one completed exercise')
        assert.equal(handoff.filter(e=>e.direction==='sent'&&e.type==='createTutorial').length,1,'Only one next exercise without an extra confirmation')
      }else{
        await waitState(s=>s.tutorial?.lessonId==='response'&&!s.pending,'first chapter auto continues to response')
        assert.equal((await observe()).learning.reviewedScenes.length,3)
      }
      chapter.scenes.push({scene,initialRevision:initial.revision,finalRevision:final.revision,
        beforeAction,readings,result:final.result,status:'authoritatively-completed-and-auto-continued'})
      console.log('PASS continuous guided UI '+viewport.width+'x'+viewport.height+' '+scene)
    }
    if(firstChapterOnly) {
      await tap(guide().locator('summary'),'open tutorial auxiliary actions')
      await tap(guide().getByRole('button',{name:'退出教学',exact:true}),'exit before second chapter')
      await waitState(s=>!s.room&&!s.matchId&&!s.pending,'first-chapter-only exit')
      report.cases.push(chapter); continue
    }
    const responseStart=await waitState(s=>s.tutorial?.lessonId==='response'&&!s.pending,'response chapter')
    const responseTransportStart=report.transport.length
    await page.waitForURL('**/game')
    const responseReadings=[]
    assert.equal(responseStart.tutorial.version,2)
    assert.equal(responseStart.tutorial.recipeId,'public-response-v1')
    assert.equal(responseStart.self.hand.length,2,'Public response starts with only two necessary hand cards')
    assert.equal(responseStart.self.field.flat().filter(Boolean).length,0)
    assert.equal(responseStart.tutorial.focus.legionInstanceId,null,'Future library identity is not disclosed')
    const responseCounter=responseStart.self.hand.find(c=>c.cardId==='ST01-10')
    assert(responseCounter)
    assert.equal(responseStart.learning.responseReviewed,undefined)
    await readScene(viewport,'response-artifact',responseReadings)
    await tap(page.locator('[data-player-index="0"] [data-l12-zone="relic"]'),'open Shanhe abilities')
    await tap(page.locator('[data-tutorial-ability="artifactSearch"]').filter({visible:true}),'activate actual artifact search')
    const costState=await settleTo(s=>s.tutorial?.stepId==='response-search-cost','artifact cost')
    await readScene(viewport,'response-search-cost',responseReadings)
    const discardPrompt=costState.prompts.find(p=>p.playerIndex===0&&p.kind==='active-target'
      &&p.sourceInstanceId===costState.tutorial.focus.artifactInstanceId&&p.presentation?.paymentStatus==='pending')
    const discardCard=costState.self.hand.find(c=>c.instanceId===costState.tutorial.focus.costCardInstanceId)
    assert(discardCard&&discardPrompt?.validChoices.includes(discardCard.instanceId))
    await choosePromptCard(discardPrompt.promptId,discardCard.instanceId,'pay actual discard cost')
    const searchState=await settleTo(s=>s.tutorial?.stepId==='response-search-pick','real search')
    await readScene(viewport,'response-search-pick',responseReadings)
    const legionId=searchState.tutorial.focus.legionInstanceId
    const searchPrompt=searchState.prompts.find(p=>p.playerIndex===0&&p.validChoices.includes(legionId))
    assert(searchPrompt.validChoices.includes(legionId))
    assert.equal(searchPrompt.data[legionId+':cardId'],'ST01-08')
    await choosePromptCard(searchPrompt.promptId,legionId,'select Nie from actual search')
    await settleTo(s=>s.tutorial?.stepId==='response-search-order','remaining library order')
    await readScene(viewport,'response-search-order',responseReadings)
    await tap(page.locator('[data-tutorial-action="place-all-top"]').filter({visible:true}),'return actual remainder to top')
    for(const [stage,card,row] of [['response-cover',responseCounter,1]]) {
      await waitState(s=>s.tutorial?.stepId===stage&&!s.pending,stage)
      await readScene(viewport,stage,responseReadings)
      await page.locator('.tutorial-detail-mask').waitFor({state:'detached'})
      await inspectGuide(viewport,stage+' select')
      await tap(page.locator('.hand-card-wrap[data-tutorial-card-instance="'+card.instanceId+'"]'),'response card '+card.name)
      const action=await visibleAction('play-card',card.instanceId)
      if(await action.count())await tap(action,'play '+card.name)
      const slot=page.locator('[data-player-index="0"] .formation-slot.available[data-tutorial-row="'+row+'"]')
      await slot.first().waitFor()
      await tap(slot.first(),'response legal position')
      await settleTo(s=>s.tutorial?.stepId!==stage,stage+' paid')
    }
    const prepared=await waitState(s=>s.tutorial?.stepId==='response-end-turn'&&!s.pending,'response covered')
    const covered=prepared.self.field.flat().find(c=>c?.instanceId===responseCounter.instanceId)
    assert(covered?.hidden&&covered.identityKnown,'Own covered identity remains visible to its owner')
    assert.equal(prepared.self.morale.filter(c=>c.tapped).length-responseStart.self.morale.filter(c=>c.tapped).length,2)
    await readScene(viewport,'response-end-turn',responseReadings)
    await tap(await visibleAction('end-turn'),'response end turn')
    const defenseState=await settleTo(s=>s.tutorial?.stepId==='response-defend','real defense before response')
    await readScene(viewport,'response-defend',responseReadings)
    await tap(await visibleAction('decline-defense'),'retain hand and take actual attack')
    const counterWindow=await settleTo(s=>s.tutorial?.stepId==='response-choose','real after-attack counter window')
    assert.equal(counterWindow.self.master.hp,defenseState.self.master.hp-1,'Attack resolves before Dark Passage response')
    await readScene(viewport,'response-choose',responseReadings)
    await inspectGuide(viewport,'counter real target')
    const usePrompt=counterWindow.prompts.find(p=>p.playerIndex===0)
    await choosePromptCard(usePrompt.promptId,usePrompt.validChoices.includes('mode:use')?'mode:use':responseCounter.instanceId,'activate covered counter')
    await waitState(s=>s.tutorial?.stepId==='response-return'&&!s.pending,'return payment')
    await readScene(viewport,'response-return',responseReadings)
    assert.match(await guide().innerText(),/返还|返回士气牌库/)
    assert.doesNotMatch(await guide().innerText(),/确认支付后，所选士气转为休整/)
    await page.screenshot({path:path.join(out,viewport.width+'-response-return-payment.png')})
    await resolveOwnPrompt()
    const summonState=await settleTo(s=>s.tutorial?.stepId==='response-summon','hand legion choice')
    await readScene(viewport,'response-summon',responseReadings)
    await choosePromptCard(summonState.prompts.find(p=>p.playerIndex===0).promptId,legionId,'select hand Nie')
    await settleTo(s=>s.tutorial?.stepId==='response-position','entry position')
    await readScene(viewport,'response-position',responseReadings)
    await tap(page.locator('[data-player-index="0"] .formation-slot.available[data-tutorial-row="0"]').first(),'choose real empty front slot')
    const responseFinal=await completedFrame('response',responseStart.matchId,responseTransportStart)
    assert.equal(responseFinal.result.field.flat().find(c=>c?.instanceId===legionId).tapped,false)
    assert.equal(responseFinal.result.masterHp,defenseState.self.master.hp-1,'Counter does not erase committed attack damage')
    assert(responseFinal.result.graveyard.some(c=>c.instanceId===responseCounter.instanceId))
    assert(responseFinal.result.graveyard.some(c=>c.instanceId===discardCard.instanceId))
    await waitState(s=>s.tutorial?.lessonId==='preparation'&&!s.pending&&s.learning.responseReviewed===true,'response real roomLeft and preparation')
    chapter.response={initialRevision:responseStart.revision,finalRevision:responseFinal.revision,
      readings:responseReadings,result:responseFinal.result,status:'authoritatively-completed-and-auto-continued'}
    console.log('PASS continuous response UI '+viewport.width+'x'+viewport.height)
    const preparationStart=await observe(), preparationReadings=[], preparationPrompts=[]
    assert.equal(preparationStart.gm,false)
    assert.equal(preparationStart.tutorial.version,1)
    for(let budget=0;budget<16;budget++) {
      const state=await waitState(s=>!s.pending,'preparation current step')
      if(state.tutorial?.lessonId!=='preparation')break
      await readScene(viewport,'preparation-'+(state.prompts.find(p=>p.playerIndex===0)?.kind||state.phase),preparationReadings)
      const prompt=state.prompts.find(p=>p.playerIndex===0)
      if(prompt?.kind.startsWith('disaster-')) {
        await page.waitForFunction(()=>{
          const images=[...document.querySelectorAll('.l12-prompt-overlay .l12-card-image__img')]
          return images.length>0&&images.every(image=>image.complete&&image.naturalWidth>0&&!image.closest('.placeholder'))
        },null,{timeout:12000})
      }
      if(state.phase==='Mulligan'&&!state.self.mulliganDone) {
        assert.equal(state.self.hand.length,6)
        await tap(await visibleAction('confirm-mulligan'),'keep actual opening hand')
      } else if(prompt) {
        preparationPrompts.push(prompt.kind)
        if(prompt.kind==='disaster-reveal') {
          await tap(page.locator('[data-tutorial-prompt-id="'+prompt.promptId+'"] [data-tutorial-action="confirm-prompt"]').filter({visible:true}),'confirm public disaster cards')
        } else {
          assert(['initiative','disaster-ban','disaster-pick'].includes(prompt.kind),'Only real declared preparation prompts')
          const choice=prompt.kind==='initiative'&&prompt.validChoices.includes('first')?'first':prompt.validChoices[0]
          await choosePromptCard(prompt.promptId,choice,'preparation '+prompt.kind)
        }
      }
      await waitState(s=>!s.pending&&(s.tutorial?.lessonId!=='preparation'||s.phase!==state.phase
        ||s.prompts.find(p=>p.playerIndex===0)?.promptId!==prompt?.promptId||s.self?.mulliganDone!==state.self.mulliganDone),'preparation advanced')
      if(budget===15)throw new Error('Preparation exceeded its real decision budget')
    }
    const finaleStart=await waitState(s=>s.tutorial?.lessonId==='finale'&&!s.pending&&s.learning.preparationReviewed===true,'preparation auto continues to finale')
    assert(preparationPrompts.includes('initiative'))
    for(const kind of ['disaster-ban','disaster-reveal','disaster-pick'])assert(preparationPrompts.includes(kind),'Student actually completed '+kind)
    assert.equal(finaleStart.enemy.master.hp,2)
    assert(finaleStart.self.hand.length<=3,'Finale uses a genuinely small hand, not hidden extra cards')
    const finaleReadings=[], finaleTransportStart=report.transport.length
    await readScene(viewport,'finale',finaleReadings)
    let playerAttacks=0
    for(let budget=0;budget<12;budget++) {
      const state=await observe()
      if(state.phase==='GameOver')break
      if(state.pending){await pause(80);continue}
      if(state.prompts.some(p=>p.playerIndex===0)){await resolveOwnPrompt();continue}
      const id=Object.keys(state.legalAttackTargets||{}).find(id=>state.legalAttackTargets[id].includes('master'))
      assert(id,'The legal finale must expose a real next attack')
      await inspectGuide(viewport,'finale independent attack '+playerAttacks)
      await tap(page.locator('[data-tutorial-anchor="field-slot"][data-tutorial-card-instance="'+id+'"]').filter({visible:true}).first(),'select legal finale attacker')
      await tap(await visibleAction('attack',id),'finale attack')
      await tap(page.locator('[data-player-index="1"] .mini-master.targetable'),'finale target master')
      playerAttacks++
      await settleTo(s=>s.phase==='GameOver'||s.phase==='Main'&&Object.keys(s.legalAttackTargets||{}).length>0,'finale attack committed')
    }
    const finaleFinal=await completedFrame('finale',finaleStart.matchId,finaleTransportStart)
    assert.equal(finaleFinal.result.phase,'GameOver');assert.equal(finaleFinal.result.winner,0)
    assert.equal(playerAttacks,2,'Player, not a script, performs the final two attacks')
    const savedFinal=await waitState(s=>s.learning.finaleReviewed===true&&s.phase==='GameOver','final result and account-local progress')
    assert.equal(savedFinal.room,finaleStart.room,'Victory remains visible until the existing final exit')
    assert.equal(report.transport.slice(finaleTransportStart).filter(e=>e.direction==='sent'&&e.type==='leaveRoom').length,0)
    await page.getByRole('dialog',{name:'对局结果',exact:true}).waitFor()
    await page.screenshot({path:path.join(out,viewport.width+'-finale-victory.png')})
    await tap(page.getByRole('button',{name:'完成教学',exact:true}),'finish actual course')
    await waitState(s=>!s.room&&!s.matchId&&!s.pending,'final course roomLeft')
    await page.waitForURL('**/battle/tutorial')
    await page.getByRole('heading',{name:'你已完成新手教学',exact:true}).waitFor()
    chapter.preparation={readings:preparationReadings,prompts:preparationPrompts,status:'actual-preparation-completed'}
    chapter.finale={readings:finaleReadings,playerAttacks,result:finaleFinal.result,status:'actual-victory-and-saved-progress'}
    await inspectNotificationSettings(viewport)
    console.log('PASS continuous full course '+viewport.width+'x'+viewport.height)
    report.cases.push(chapter)
  }
  assert.deepEqual(report.errors,[])
  report.sourceAfter=hashes()
  assert.deepEqual(report.sourceAfter,report.sourceBefore,'Product source changed during acceptance')
  report.status=visualSliceOnly?'CAPTURED':'GREEN'
} catch(error) {
  report.status='FAILED'; report.failure=String(error.message)
  if(page){try{report.lastObservation=await observe();report.visibleButtons=await page.getByRole('button').allTextContents();await page.screenshot({path:path.join(out,'failure.png')})}catch{}}
  process.exitCode=1
} finally {
  fs.writeFileSync(path.join(out,'report-before-cleanup.json'),JSON.stringify(report,null,2)+'\n')
  if(page) await page.goto('about:blank',{waitUntil:'commit',timeout:5000}).catch(()=>{})
  if(context) {
    report.browserClosed=await Promise.race([context.close().then(()=>true,()=>false),pause(8000).then(()=>false)])
    if(!report.browserClosed){
      report.cleanupWarning='Graceful owned QA browser close timed out; exact-profile process cleanup used'
      try {
        const cleanup=JSON.parse(execFileSync(process.env.L12_PWSH||'pwsh',['-NoProfile','-NonInteractive','-Command',
          `$ownedProfile=[IO.Path]::GetFullPath($env:L12_OWNED_QA_PROFILE); $roots=@(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | Where-Object {$_.ParentProcessId -eq [int]$env:L12_OWNED_QA_PARENT -and $_.CommandLine -and $_.CommandLine.Contains($ownedProfile)}); foreach($p in $roots){Stop-Process -Id $p.ProcessId -ErrorAction Stop}; $left=@(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($ownedProfile)}); @{stopped=@($roots | ForEach-Object {$_.ProcessId});remaining=@($left | ForEach-Object {$_.ProcessId})} | ConvertTo-Json -Compress`],
          {encoding:'utf8',timeout:12000,env:{...process.env,L12_OWNED_QA_PROFILE:path.join(cache,'profile'),L12_OWNED_QA_PARENT:String(process.pid)}}))
        report.browserCleanup=cleanup
        report.browserClosed=cleanup.stopped.length>0&&cleanup.remaining.length===0
      }catch(error){report.browserCleanupError=String(error.message)}
      if(!report.browserClosed) report.status='FAILED'
    }
  }
  if(vite) await Promise.race([vite.close().catch(()=>{}),pause(8000)])
  if(!hostDone) host.kill()
  if(!hostDone) await Promise.race([new Promise(resolve=>host.once('exit',resolve)),pause(5000)])
  fs.closeSync(hostLog)
  report.hostExited=hostDone
  if(!['GREEN','CAPTURED'].includes(report.status)||!report.hostExited) process.exitCode=1
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n')
  console.log(JSON.stringify({status:report.status,cases:report.cases.length,failure:report.failure,hostExited:report.hostExited}))
}
