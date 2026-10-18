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
const boundSources = ['opcgpro-vue/src/App.vue','opcgpro-vue/src/l12/GamePage.vue','opcgpro-vue/src/l12/net.ts','opcgpro-vue/src/l12/site/TutorialPage.vue','opcgpro-vue/src/l12/tutorial/TutorialGuidanceOverlay.vue','opcgpro-vue/src/l12/tutorial/tutorialProgress.ts']
const hashes = () => Object.fromEntries(boundSources.map(name=>[name,crypto.createHash('sha256').update(fs.readFileSync(path.join(repo,name))).digest('hex').toUpperCase()]))
report.sourceBefore = hashes()
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
    self: s.game?.players[0], defense: s.game?.pendingDefense }
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
  if ((prompt.kind || '').includes('resource') || (prompt.kind || '').includes('cost')) {
    const mobile = page.locator('.mobile-morale-choice:not(.unavailable):not(.selected)')
    const orb = page.locator('[data-player-index="0"] .morale-orb.payable:not(.selected)')
    if (await mobile.count()) await tap(mobile.first(), 'select payment')
    else if (await orb.count()) await tap(orb.first(), 'select payment orb')
    else {
      const option = page.locator('.prompt-choices button:not(.decline-action):not([disabled])').first()
      await tap(option, 'select payment option')
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
  page=await context.newPage(); page.setDefaultTimeout(10000)
  page.on('dialog',dialog=>dialog.accept())
  page.on('pageerror',error=>report.errors.push(error.message))
  page.on('websocket',socket=>{
    socket.on('framesent',frame=>{try{const value=JSON.parse(String(frame.payload));if(['createTutorial','gameAction','leaveRoom'].includes(value.type)) report.transport.push({direction:'sent',type:value.type,requestId:value.requestId,lessonId:value.lessonId,command:value.command?.type})}catch{}})
    socket.on('framereceived',frame=>{try{const value=JSON.parse(String(frame.payload));if(value.type==='gameState'&&value.tutorial) report.transport.push({direction:'received',type:value.type,requestId:value.requestId,revision:value.state?.revision,tutorial:value.tutorial});else if(['tutorialRejected','actionRejected','tutorialRestartRequired','roomLeft'].includes(value.type))report.transport.push({direction:'received',type:value.type,requestId:value.requestId})}catch{}})
  })
  await page.goto(`http://127.0.0.1:${uiPort}/battle/tutorial`)
  await page.evaluate(async()=>{const p=await import('/src/l12/platform.ts');await p.register('TQA'+Math.random().toString(36).slice(2,8),'Qa'+crypto.randomUUID()+'!');const{router}=await import('/src/router/index.ts');await router.push('/battle/tutorial')})
  await waitState(s=>s.status==='online','real authenticated connection')
  if(process.env.L12_TUTORIAL_IDENTITY_ONLY==='true') {
    await page.evaluate(async()=>{const p=await import('/src/l12/platform.ts');await p.register('TQB'+Math.random().toString(36).slice(2,8),'Qb'+crypto.randomUUID()+'!')})
    await page.getByText('账号刚刚切换，请刷新页面后开始教学。',{exact:true}).waitFor()
    for(const button of await page.locator('.lesson-grid button').all()) assert(await button.isDisabled(),'Account mismatch must not create a lesson through the old connection')
    assert.equal(report.transport.filter(item=>item.direction==='sent'&&item.type==='createTutorial').length,0)
    await page.reload()
    await waitState(s=>s.status==='online','new account reconnect')
    await page.locator('.lesson-grid button').first().waitFor()
    assert(await page.locator('.lesson-grid button').first().isEnabled(),'Reload must establish the current account connection')
    assert.equal(await page.locator('.lesson-grid article').getByText('已完成',{exact:true}).count(),0)
    report.cases.push({case:'real account switch refuses old socket and reloads current identity',passed:true})
  }
  for(const viewport of [{width:1366,height:768},{width:844,height:390},{width:390,height:844}]) {
    await page.setViewportSize(viewport)
    for(const [index,lesson] of ['deploy','move','combat'].entries()) {
      if(process.env.L12_TUTORIAL_IDENTITY_ONLY==='true'||process.env.L12_TUTORIAL_ONLY_LESSON&&lesson!==process.env.L12_TUTORIAL_ONLY_LESSON) continue
      await tap(page.locator('.lesson-grid article').nth(index).getByRole('button'), 'start '+lesson)
      let initial=await waitState(s=>s.tutorial?.lessonId===lesson&&!s.pending,'start '+lesson)
      await page.waitForURL('**/game')
      assert.equal(initial.gm,false)
      await page.getByRole('button',{name:'最小化教学说明'}).waitFor()
      const bounds=await page.locator('.tutorial-guidance').boundingBox()
      assert(bounds&&bounds.x>=0&&bounds.y>=0&&bounds.x+bounds.width<=viewport.width+1&&bounds.y+bounds.height<=viewport.height+1,'Tutorial guidance must fit physical viewport')
      const logicalWidth=await page.locator('.tutorial-guidance').evaluate(element=>element.clientWidth)
      assert(logicalWidth>=280,'Teaching copy must have a readable width, not a narrow vertical column')
      await page.screenshot({path:path.join(out,`${viewport.width}-${lesson}-started.png`)})
      await tap(page.getByRole('button',{name:'最小化教学说明'}),'minimize guidance')
      if(lesson==='deploy') {
        const card=initial.self.hand.find(c=>c.cardId==='S01-0116')
        await tap(page.locator(`[data-l12-zone="hand"][data-player-index="0"] [data-card-instance-id="${card.instanceId}"]`),'select Xi Shi')
        const play=page.getByRole('button',{name:'打出',exact:true}).filter({visible:true}).first()
        if(await play.count()) await tap(play,'arm deployment')
        await tap(page.locator('[data-player-index="0"] .formation-slot.available').first(),'deployment cell')
      } else if(lesson==='move') {
        const card=initial.self.field[1][1]
        await tap(page.locator(`[data-player-index="0"] .formation [data-card-instance-id="${card.instanceId}"]`),'select field Xi Shi')
        await tap(page.getByRole('button',{name:'移动',exact:true}).filter({visible:true}).first(),'ordinary move')
        await tap(page.locator('[data-player-index="0"] .formation-slot.available').first(),'move destination')
      } else {
        const card=initial.self.field[0][0]
        await tap(page.locator(`[data-player-index="0"] .formation [data-card-instance-id="${card.instanceId}"]`),'select attacker')
        await tap(page.getByRole('button',{name:'进攻',exact:true}).filter({visible:true}).first(),'attack')
        await tap(page.locator('[data-player-index="1"] .mini-master.targetable'),'attack enemy master')
        await settleTo(s=>s.tutorial?.stepId==='end-turn','own attack')
        await tap(page.getByRole('button',{name:'结束回合',exact:true}).filter({visible:true}).first(),'end turn')
        await settleTo(s=>s.tutorial?.stepId==='defend','opponent attack')
        await tap(page.getByRole('button',{name:'不抵挡',exact:true}).filter({visible:true}).first(),'decline defense')
      }
      const final=await settleTo(s=>s.tutorial?.status==='completed','complete '+lesson)
      assert(final.revision>initial.revision)
      report.cases.push({viewport,lesson,initialRevision:initial.revision,finalRevision:final.revision,status:'completed',guidance:bounds,guidanceLogicalWidth:logicalWidth})
      console.log(`PASS real UI ${viewport.width}x${viewport.height} ${lesson}`)
      await page.screenshot({path:path.join(out,`${viewport.width}-${lesson}-completed.png`)})
      await tap(page.getByRole('button',{name:'返回教学目录',exact:true}),'leave tutorial')
      await waitState(s=>!s.room&&!s.matchId&&!s.pending,'roomLeft')
      await page.waitForURL('**/battle/tutorial')
      assert(await page.locator('.lesson-grid article').nth(index).getByText('已完成',{exact:true}).isVisible())
    }
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
