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
assert(out && cache, 'Explicit governed roots are required')
process.env.L12_TUTORIAL_REAL_OUTPUT = out
process.env.L12_TUTORIAL_REAL_CACHE = cache
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  `. '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' | Out-Null; . '${repo}/ops/windows/L12GeneratedStorage.ps1'; foreach($p in @($env:L12_TUTORIAL_REAL_OUTPUT,$env:L12_TUTORIAL_REAL_CACHE,$env:TEMP)){$null=Assert-L12GeneratedPath -Path $p -Purpose 'independent real tutorial UI'}`], { stdio: 'pipe' })
fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const report = { status: 'RUNNING', cases: [], transport: [], errors: [], boundary: 'Actual local server, real account/WS, unchanged App/router/GameBoard; UI clicks only for gameplay. No test/production deployment.' }
const boundSources = ['opcgpro-vue/src/App.vue','opcgpro-vue/src/l12/GamePage.vue','opcgpro-vue/src/l12/net.ts','opcgpro-vue/src/l12/site/TutorialPage.vue','opcgpro-vue/src/l12/tutorial/TutorialGuidanceOverlay.vue','opcgpro-vue/src/l12/tutorial/tutorialProgress.ts','opcgpro-vue/src/l12/tutorial/tutorialCourse.ts','opcgpro-vue/src/l12/tutorial/tutorialGuidance.ts','opcgpro-vue/src/l12/site/BattleHubPage.vue','opcgpro-vue/src/l12/game/GameBoard.vue','opcgpro-vue/src/l12/game/GameActions.vue','opcgpro-vue/src/l12/game/HandArea.vue','opcgpro-vue/src/l12/game/PlayerMat.vue','opcgpro-vue/src/l12/game/PromptOverlay.vue']
const hashes = () => Object.fromEntries(boundSources.map(name=>[name,crypto.createHash('sha256').update(fs.readFileSync(path.join(repo,name))).digest('hex').toUpperCase()]))
boundSources.push('opcgpro-vue/src/l12/tutorial/tutorialResponseGuidance.ts','opcgpro-vue/src/l12/game/BattleOverlayPortal.vue',
  'opcgpro-vue/src/l12/notifications/browserNotifications.ts','opcgpro-vue/src/l12/notifications/realtimeNotificationPolicy.ts',
  'opcgpro-vue/src/l12/notifications/useRealtimeNotifications.ts','opcgpro-vue/src/l12/site/L12SettingsModal.vue',
  'opcgpro-vue/src/l12/site/FriendRequestNotifications.vue')
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
fs.cpSync(path.join(repo, '服务端WebSocket/bin/Release/net10.0'), hostRoot, { recursive: true,
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
    matchId: s.game?.matchId, prompts: s.game?.prompts || [],
    self: s.game?.players.find(p=>p.playerIndex===s.game.you), defense: s.game?.pendingDefense,
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
  else await page.mouse.click(point.x,point.y)
}
async function resolveOwnPrompt() {
  const state = await observe()
  const prompt = state.prompts.find(p => p.playerIndex === 0)
  if (!prompt || state.pending) return false
  if ((prompt.kind || '').includes('resource')) {
    const mobile = page.locator('.mobile-morale-choice:not(.unavailable):not(.selected)')
    const orb = page.locator('[data-player-index="0"] .morale-orb.payable:not(.selected)')
    for (let i=0;i<prompt.minChoose;i++) {
    if (await mobile.count()) await tap(mobile.first(), 'select payment')
    else if (await orb.count()) await tap(orb.first(), 'select payment orb')
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
    await tap(page.getByRole('button', { name: /确认支付|确认费用|确认选择/ }).filter({ visible: true }).first(), 'confirm payment')
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
  vite=await createServer({root:front,configLoader:'runner',cacheDir:path.join(cache,'vite'),server:{host:'127.0.0.1',port:uiPort,strictPort:true}})
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
    socket.on('framereceived',frame=>{try{const value=JSON.parse(String(frame.payload));if(value.type==='gameState'&&value.tutorial) report.transport.push({direction:'received',type:value.type,requestId:value.requestId,revision:value.state?.revision,tutorial:value.tutorial});else if(['tutorialRejected','actionRejected','tutorialRestartRequired','roomLeft'].includes(value.type))report.transport.push({direction:'received',type:value.type,requestId:value.requestId})}catch{}})
  })

  await page.goto('http://127.0.0.1:'+uiPort+'/battle')
  await page.evaluate(async()=>{
    const p=await import('/src/l12/platform.ts')
    await p.register('TQA'+Math.random().toString(36).slice(2,8),'Qa'+crypto.randomUUID()+'!')
    const{router}=await import('/src/router/index.ts');await router.push('/battle')
  })
  await waitState(s=>s.status==='online','real authenticated connection')
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
      assert.equal(await panel.getByRole('button').count(),1,'Each introduction step has one main action')
      assert(!/我想了解|回到主线|再看卡牌|再认识/.test(text),'Introduction must stay linear')
      if(['手牌来自牌库','士气用来支付费用'].includes(title)){
        assert((await panel.locator('.resource-flow').innerText()).includes('→'),'Resources need an origin-to-use explanation')
        await page.screenshot({path:path.join(out,viewport.width+'-intro-'+i+'.png')})
      }
      await tap(panel.getByRole('button',{name:i===4?'进入战场，继续主线':'继续主线',exact:true}),'read mainline '+i)
    }
    assert.deepEqual(titles,['欢迎，主宰者','认识三种卡牌','手牌来自牌库','士气用来支付费用','看清数值，再开始实操'])
    return titles
  }
  const guide=()=>page.locator('[data-ui-contract="tutorial-authoritative-guidance"]')
  async function inspectGuide(viewport,label) {
    const shell=guide();await shell.waitFor()
    const h2=await shell.locator('h2').innerText()
    const resultLine=await shell.locator('.tutorial-result-line').innerText()
    assert(resultLine.trim(),'Every practical step keeps one necessary cost or result line')
    assert.equal(await shell.locator('.tutorial-result-line').count(),1)
    assert.equal(await shell.locator('.tutorial-help,.tutorial-cause-and-result').count(),0)
    assert(!/DTO|matchId|权威|部署|法力|召唤/.test(await shell.innerText()),'No internal implementation language in teaching')
    const bounds=await shell.boundingBox()
    assert(bounds&&bounds.x>=-1&&bounds.y>=-1&&bounds.x+bounds.width<=viewport.width+1&&bounds.y+bounds.height<=viewport.height+1,
      'Guide must fit physical viewport '+label)
    const logicalWidth=await shell.evaluate(e=>e.clientWidth)
    assert(logicalWidth>=280,'Readable guide width '+label)
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
    return {title:h2,resultLine,logicalWidth,bounds,geometry,unavailable:Boolean(unavailable)}
  }
  async function readScene(viewport,scene,readings) {
    for(let i=0;i<10;i++) {
      const shell=guide();await shell.waitFor()
      if(await shell.getAttribute('data-tutorial-reading')!=='true')return
      const record=await inspectGuide(viewport,scene+' reading '+i)
      if(['card','morale','card-source','defense','attack-result'].includes(await shell.getAttribute('data-tutorial-guide-id')))
        await page.screenshot({path:path.join(out,viewport.width+'-'+scene+'-'+(await shell.getAttribute('data-tutorial-guide-id'))+'.png')})
      readings.push(record)
      await tap(shell.getByRole('button',{name:'继续',exact:true}),'read explanation '+record.title)
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
  report.mode=settingsOnly?'actual-settings-only':'continuous-course-and-actual-settings'
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
  for(const viewport of [{width:1366,height:768},{width:844,height:390},{width:390,height:844}]) {
    await page.setViewportSize(viewport)
    if(settingsOnly){await inspectNotificationSettings(viewport);report.cases.push({viewport,scope:'settings-only; no gameplay'});continue}
    if(viewport.width!==1366) {
      await tap(page.getByRole('button',{name:'重新学习第一章',exact:true}),'repeat coherent chapter')
    }
    const titles=await readIntroduction(viewport)
    const chapter={viewport,introduction:titles,scenes:[]}
    for(const [index,scene] of ['deploy','move','combat'].entries()) {
      const initial=await waitState(s=>s.tutorial?.lessonId===scene&&!s.pending,'start '+scene)
      await page.waitForURL('**/game')
      assert.equal(initial.gm,false)
      const readings=[]
      await readScene(viewport,scene,readings)
      const beforeAction=await inspectGuide(viewport,scene+' action')
      const practice=scene==='deploy'?initial.self.hand.find(c=>c.cardId==='S01-0116')
        :scene==='move'?initial.self.field[1][1]:initial.self.field[0][0]
      const instance=practice.instanceId
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
      const final=await settleTo(s=>s.tutorial?.status==='completed','complete '+scene)
      assert(final.revision>initial.revision)
      assert.equal(final.learning?.reviewedScenes?.length||0,index,
        'Authoritative action completion must not silently mark explanation learned')
      const result=await inspectGuide(viewport,scene+' real result')
      await page.screenshot({path:path.join(out,viewport.width+'-'+scene+'-result.png')})
      const transportStart=report.transport.length
      await tap(guide().getByRole('button',{name:'继续主线',exact:true}),'review result and continue mainline')
      if(index<2) {
        const next=await waitState(s=>s.tutorial?.lessonId===['deploy','move','combat'][index+1]&&!s.pending,'continuous next scene')
        assert.equal(next.learning.reviewedScenes.length,index+1)
        const handoff=report.transport.slice(transportStart)
        const leave=handoff.findIndex(e=>e.direction==='sent'&&e.type==='leaveRoom')
        const ack=handoff.findIndex(e=>e.direction==='received'&&e.type==='roomLeft')
        const create=handoff.findIndex(e=>e.direction==='sent'&&e.type==='createTutorial')
        assert(leave>=0&&ack>leave&&create>ack,'Next scene only after actual roomLeft, not route/timer')
      }else{
        await waitState(s=>!s.room&&!s.matchId&&!s.pending,'final roomLeft')
        await page.waitForURL('**/battle/tutorial')
        await page.getByRole('heading',{name:'现在，把刚才的行动串起来',exact:true}).waitFor()
        assert.equal((await observe()).learning.reviewedScenes.length,3)
        assert.equal(await page.locator('.course-path li').filter({hasText:'尚未开放'}).count(),1)
      }
      chapter.scenes.push({scene,initialRevision:initial.revision,finalRevision:final.revision,
        beforeAction,readings,result,status:'reviewed-and-continued'})
      console.log('PASS continuous guided UI '+viewport.width+'x'+viewport.height+' '+scene)
    }
    await tap(page.getByRole('button',{name:/^继续主线：卡牌效果与响应$/}),'continue second chapter')
    const responseStart=await waitState(s=>s.tutorial?.lessonId==='response'&&!s.pending,'response chapter')
    await page.waitForURL('**/game')
    const responseReadings=[]
    const responseLegion=responseStart.self.hand.find(c=>c.cardId==='S01-0116')
    const responseCounter=responseStart.self.hand.find(c=>c.cardId==='S01-0016')
    assert(responseLegion&&responseCounter)
    assert.equal(responseStart.learning.responseReviewed,undefined)
    for(const [stage,card,row] of [['response-legion',responseLegion,0],['response-cover',responseCounter,1]]) {
      await waitState(s=>s.tutorial?.stepId===stage&&!s.pending,stage)
      await readScene(viewport,stage,responseReadings)
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
    assert.equal(prepared.self.morale.filter(c=>c.tapped).length-responseStart.self.morale.filter(c=>c.tapped).length,3)
    await readScene(viewport,'response-end-turn',responseReadings)
    await tap(await visibleAction('end-turn'),'response end turn')
    const counterWindow=await waitState(s=>s.tutorial?.stepId==='response-choose'&&!s.pending,'real counter window')
    await readScene(viewport,'response-choose',responseReadings)
    await inspectGuide(viewport,'counter real target')
    await choosePromptCard(counterWindow.prompts.find(p=>p.playerIndex===0).promptId,responseCounter.instanceId,'choose covered counter')
    const discardState=await waitState(s=>s.tutorial?.stepId==='response-discard'&&!s.pending,'discard payment')
    await readScene(viewport,'response-discard',responseReadings)
    const discardPrompt=discardState.prompts.find(p=>p.playerIndex===0&&p.kind==='discard-cost')
    const discardCard=discardState.self.hand.find(c=>discardPrompt.validChoices.includes(c.instanceId))
    assert(discardCard)
    await inspectGuide(viewport,'discard real cost')
    await choosePromptCard(discardPrompt.promptId,discardCard.instanceId,'choose discard')
    const responseFinal=await settleTo(s=>s.tutorial?.status==='completed','response actual negation')
    assert.equal(responseFinal.learning.responseReviewed,undefined,'Action alone must not record response learning')
    assert.equal(responseFinal.self.field.flat().find(c=>c?.instanceId===responseLegion.instanceId).troops,responseLegion.troops)
    assert(responseFinal.self.graveyard.some(c=>c.instanceId===responseCounter.instanceId))
    assert(responseFinal.self.graveyard.some(c=>c.instanceId===discardCard.instanceId))
    const responseResult=await inspectGuide(viewport,'response result review')
    await page.screenshot({path:path.join(out,viewport.width+'-response-result.png')})
    await tap(guide().getByRole('button',{name:'继续主线',exact:true}),'review response outcome')
    await waitState(s=>!s.room&&!s.matchId&&!s.pending&&s.learning.responseReviewed===true,'response real roomLeft and review')
    await page.waitForURL('**/battle/tutorial')
    chapter.response={initialRevision:responseStart.revision,finalRevision:responseFinal.revision,
      readings:responseReadings,result:responseResult,status:'reviewed-and-continued'}
    await inspectNotificationSettings(viewport)
    console.log('PASS continuous response UI '+viewport.width+'x'+viewport.height)
    report.cases.push(chapter)
  }
  assert.deepEqual(report.errors,[])
  report.sourceAfter=hashes()
  assert.deepEqual(report.sourceAfter,report.sourceBefore,'Product source changed during acceptance')
  report.status='GREEN'
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
  if(report.status!=='GREEN'||!report.hostExited) process.exitCode=1
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n')
  console.log(JSON.stringify({status:report.status,cases:report.cases.length,failure:report.failure,hostExited:report.hostExited}))
}
