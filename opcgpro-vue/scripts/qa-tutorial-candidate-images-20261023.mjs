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
assert(out && cache && path.isAbsolute(out) && path.isAbsolute(cache), 'Explicit absolute governed roots are required')
assert(!fs.existsSync(path.join(out,'report.json')), 'Refusing to overwrite a prior tutorial report')
process.env.L12_TUTORIAL_REAL_OUTPUT = out
process.env.L12_TUTORIAL_REAL_CACHE = cache
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  `$ErrorActionPreference='Stop'; . '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' | Out-Null; . '${repo}/ops/windows/L12GeneratedStorage.ps1'; foreach($p in @($env:L12_TUTORIAL_REAL_OUTPUT,$env:L12_TUTORIAL_REAL_CACHE,$env:TEMP)){$null=Assert-L12GeneratedPath -Path $p -Purpose 'independent real tutorial UI'}`], { stdio: 'pipe' })
// A child PowerShell initializer cannot change this Node process's environment.
// Apply the already-validated cache to this process and all future native children.
process.env.TEMP = cache; process.env.TMP = cache; process.env.TMPDIR = cache
fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const report = { status: 'RUNNING', cases: [], transport: [], projectedObservations: [], errors: [], boundary: 'Actual local server, real account/WS, unchanged App/router/GameBoard; UI clicks only for gameplay. Completion is passively observed after the actual net consumer restores full/delta/card frames. No test/production deployment.' }
report.candidateImageChecks=[]
report.imageResponses=[]
report.imageRequestFailures=[]
const firstChapterOnly = process.env.L12_TUTORIAL_FIRST_CHAPTER_ONLY === '1'
const effectSliceOnly = process.env.L12_TUTORIAL_EFFECT_SLICE_ONLY === '1'
const effectViewportName = process.env.L12_TUTORIAL_EFFECT_VIEWPORT
if(effectSliceOnly) {
  assert(['portrait','landscape'].includes(effectViewportName),'Effect slice requires one explicit independent portrait/landscape run')
  assert(!firstChapterOnly&&process.env.L12_TUTORIAL_VISUAL_SLICE_ONLY!=='1'&&process.env.L12_NOTIFICATION_SETTINGS_ONLY!=='1','Effect slice cannot silently mix other modes')
}
const effectViewports = effectViewportName==='portrait'?[{width:390,height:844}]:[{width:844,height:390}]
if(effectSliceOnly) report.effectViewport=effectViewportName
if(effectSliceOnly) report.effectEvidenceBoundary='The owner-browser chain proves native touch, exact submitted identities/order, same-request authoritative acceptance, public placement count, and ordinary owner projection. It does not directly observe final private library order or the opponent recipient projection. Named engine regressions are mapped separately by their actual assertions; public-event identity protection is not a full recipient-projection test.'
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
boundSources.push('opcgpro-vue/src/l12/mobileViewport.css','opcgpro-vue/src/l12/mobileViewport.ts',
  'opcgpro-vue/src/l12/game/PromptCardCandidate.vue','opcgpro-vue/scripts/qa-tutorial-candidate-images-20261023.mjs')
report.sourceBefore = hashes()
report.paymentChecks=[]
report.effectReceipts=[]
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
function planWindowsHostRuntimeCopy(runtimeTargets,runtimeInventory) {
  const safeRelative=value=>{
    const relative=String(value||'').replaceAll('\\','/')
    assert(relative&&!path.posix.isAbsolute(relative)&&!/^[a-z]:/i.test(relative),'Illegal runtime path: '+value)
    assert(path.posix.normalize(relative)===relative&&!relative.split('/').includes('..'),'Illegal runtime path: '+value)
    return relative
  }
  const targets=runtimeTargets.map(asset=>({...asset,relative:safeRelative(asset.relative)}))
  for(const asset of targets) assert(asset.relative.startsWith('runtimes/'+asset.rid+'/'),
    'Runtime target path does not match its RID: '+asset.relative)
  const declaredRids=new Set(targets.map(asset=>asset.rid).filter(Boolean))
  const declaredWindowsAssets=new Set(targets.filter(asset=>asset.rid==='win-x64').map(asset=>asset.relative))
  const selectedRuntimeFiles=targets.filter(asset=>asset.rid==='win-x64'&&asset.assetType==='native'
    &&path.posix.extname(asset.relative).toLowerCase()==='.dll').map(asset=>asset.relative).sort()
  assert(selectedRuntimeFiles.length>=2,'Bounded Windows host copy requires its declared win-x64 native DLLs')
  for(const raw of runtimeInventory) {
    const entry={...raw,relative:safeRelative(raw.relative)}
    assert(entry.kind!=='link','Linked host input is forbidden: '+entry.relative)
    const parts=entry.relative.split('/')
    if(parts[0]==='runtimes'&&parts.length===2)
      assert(entry.kind==='directory'&&declaredRids.has(parts[1]),'Unknown runtime directory: '+entry.relative)
    if(entry.kind==='file'&&entry.relative.startsWith('runtimes/win-x64/'))
      assert(declaredWindowsAssets.has(entry.relative),'Unknown win-x64 runtime asset: '+entry.relative)
  }
  return {selectedRuntimeFiles,declaredRids:[...declaredRids].sort()}
}
const hostCopyMode=process.env.L12_TUTORIAL_HOST_COPY_MODE||'full'
if(hostCopyMode==='win-x64') {
  const normalizedSource=path.resolve(serverSource)
  const depsPath=path.join(normalizedSource,'GrandUMIServer.deps.json')
  const deps=JSON.parse(fs.readFileSync(depsPath,'utf8'))
  const target=Object.values(deps.targets||{})[0]
  assert(target,'GrandUMIServer deps target is required for a bounded Windows host copy')
  const runtimeTargets=Object.values(target).flatMap(library=>Object.entries(library.runtimeTargets||{})
    .map(([relative,detail])=>({relative,rid:detail.rid,assetType:detail.assetType})))
  const runtimesRoot=path.join(normalizedSource,'runtimes')
  const runtimeInventory=[]
  for(const entry of fs.readdirSync(runtimesRoot,{withFileTypes:true})) {
    runtimeInventory.push({relative:'runtimes/'+entry.name,kind:entry.isSymbolicLink()?'link':entry.isDirectory()?'directory':'file'})
  }
  const winRoot=path.join(runtimesRoot,'win-x64')
  const walkFiles=(root,inventory=null)=>fs.readdirSync(root,{withFileTypes:true}).flatMap(entry=>{
    const full=path.join(root,entry.name)
    if(inventory)inventory.push({relative:path.relative(normalizedSource,full).replaceAll(path.sep,'/'),
      kind:entry.isSymbolicLink()?'link':entry.isDirectory()?'directory':'file'})
    return entry.isDirectory()?walkFiles(full,inventory):[full]
  })
  walkFiles(winRoot,runtimeInventory)
  const runtimePlan=planWindowsHostRuntimeCopy(runtimeTargets,runtimeInventory)
  const selectedRuntimeFiles=new Set(runtimePlan.selectedRuntimeFiles.map(relative=>relative.replaceAll('/',path.sep)))
  fs.cpSync(normalizedSource,hostRoot,{recursive:true,filter:source=>{
    const stat=fs.lstatSync(source)
    assert(!stat.isSymbolicLink(),'Linked host input is forbidden: '+source)
    const relative=path.relative(normalizedSource,source)
    if(!relative)return true
    if(relative==='runtime'||relative.startsWith('runtime'+path.sep))return false
    if(relative==='runtimes')return true
    if(relative.startsWith('runtimes'+path.sep)) {
      if(stat.isDirectory())return [...selectedRuntimeFiles].some(file=>file===relative||file.startsWith(relative+path.sep))
      return selectedRuntimeFiles.has(relative)
    }
    return path.extname(relative).toLowerCase()!=='.pdb'
  }})
  const copied=walkFiles(hostRoot).map(file=>({relative:path.relative(hostRoot,file),bytes:fs.statSync(file).size,
    sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()}))
  assert.deepEqual(copied.filter(file=>file.relative.startsWith('runtimes'+path.sep)).map(file=>file.relative).sort(),
    [...selectedRuntimeFiles].sort(),'Only deps-declared win-x64 native DLLs may enter the bounded host')
  for(const file of copied) assert.equal(file.sha256,
    crypto.createHash('sha256').update(fs.readFileSync(path.join(normalizedSource,file.relative))).digest('hex').toUpperCase(),
    'Copied host file hash mismatch: '+file.relative)
  report.hostCopy={mode:hostCopyMode,files:copied.length,bytes:copied.reduce((sum,file)=>sum+file.bytes,0),
    runtimeFiles:[...selectedRuntimeFiles].sort(),manifest:copied}
} else {
  assert.equal(hostCopyMode,'full','Unknown tutorial host copy mode')
  fs.cpSync(serverSource, hostRoot, { recursive: true,
    filter: source => path.basename(source) !== 'runtime' })
  report.hostCopy={mode:hostCopyMode,boundary:'Original full-copy behavior preserved'}
}
const dll = path.join(hostRoot, 'GrandUMIServer.dll')
report.dllSha256 = crypto.createHash('sha256').update(fs.readFileSync(dll)).digest('hex').toUpperCase()
report.sourceCommit=execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim()
const serverProductVersion=execFileSync(process.env.L12_PWSH || 'pwsh',['-NoProfile','-NonInteractive','-Command',
  `(Get-Item -LiteralPath '${dll.replaceAll("'", "''")}').VersionInfo.ProductVersion`],{encoding:'utf8'}).trim()
const serverBindingPath=process.env.L12_TUTORIAL_SERVER_BINDING_FILE
const serverBinding=serverBindingPath?JSON.parse(fs.readFileSync(serverBindingPath,'utf8')):null
const boundServerSources=serverBinding?.sources?.map(source=>({path:source.path,expectedSha256:source.sha256,
  actualSha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(repo,source.path))).digest('hex').toUpperCase()}))||[]
report.backendAtStart={...report.backendAtStart,serverSource,dllSha256:report.dllSha256,productVersion:serverProductVersion,
  bindingFile:serverBindingPath||null,bindingBoundary:serverBinding?.sourceBoundary||null,boundServerSources,
  recipeBinding:process.env.L12_TUTORIAL_SERVER_RECIPE_BINDING||null}
if(effectSliceOnly) {
  const expectedCommit=process.env.L12_TUTORIAL_SERVER_COMMIT
  assert(expectedCommit,'Effect slice requires an explicit server commit binding')
  assert(serverProductVersion.includes(expectedCommit),'Server binary does not match its declared source commit')
  assert(serverBinding?.serverDll===report.dllSha256,'Server binary does not match the governed dirty-batch binding')
  assert(boundServerSources.every(source=>source.actualSha256===source.expectedSha256),
    'Current source files do not match the governed dirty-batch binding')
}
const hostLog = fs.openSync(path.join(out, 'local-host.log'), 'wx')
const host = spawn('C:/Program Files/dotnet/dotnet.exe', [dll, String(serverPort)], {
  cwd: hostRoot, windowsHide: true, stdio: ['ignore', hostLog, hostLog], env: { ...process.env,
    L12_LISTEN_HOST: '127.0.0.1', L12_ALLOWED_ORIGINS: `http://127.0.0.1:${uiPort}`,
    L12_TUTORIAL_ENABLED: 'true', L12_CHAT_ENABLED: 'false' } })
report.ownedResources={parentPid:process.pid,hostPid:host.pid,hostRoot,profile:path.join(cache,'profile'),serverPort,uiPort}
let hostFailure, vite, context, page, hostDone=false
host.on('error', error => { hostFailure = error })
host.on('exit',()=>{hostDone=true})
const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
const observe = () => page.evaluate(async () => {
  const { l12State: s } = await import('/src/l12/net.ts')
  return { status: s.status, pending: s.pendingAction, notice: s.notice, room: s.room?.roomCode,
    recoveryPhase: s.recoveryPhase, connectionIssue: s.connectionIssue,
    accountId: s.accountId, sessionId: s.sessionId, connectionGeneration: s.connectionGeneration,
    gamePageVisible: Boolean(document.querySelector('.game-page')?.getClientRects().length),
    loadingState: document.querySelector('.missing-game')?.getAttribute('data-state') || null,
    tutorial: s.tutorial, gm: s.gmEnabled, phase: s.game?.phase, revision: s.game?.revision,
    winner: s.game?.winner, legalAttackTargets: s.game?.legalAttackTargets,
    matchId: s.game?.matchId, prompts: s.game?.prompts || [], recentEvents: s.game?.recentEvents || [],
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
  const viewport=await page.viewportSize()
  const target=await locator.evaluate(element=>({tag:element.tagName.toLowerCase(),role:element.getAttribute('role'),
    ariaLabel:element.getAttribute('aria-label'),text:(element.textContent||'').trim().slice(0,160),
    tutorialChoice:element.getAttribute('data-tutorial-choice'),
    tutorialPaymentResource:element.getAttribute('data-tutorial-payment-resource'),
    tutorialCardInstance:element.getAttribute('data-tutorial-card-instance'),
    tutorialRow:element.getAttribute('data-tutorial-row'),tutorialSlot:element.getAttribute('data-tutorial-slot'),
    tutorialAction:element.getAttribute('data-tutorial-action')}))
  const input=viewport.width < 1000?'touchscreen':'mouse'
  if (input==='touchscreen') await page.touchscreen.tap(point.x,point.y)
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
  return {label,input,viewport,point,target}
}
async function resolveOwnPrompt() {
  const state = await observe()
  const prompt = state.prompts.find(p => p.playerIndex === 0)
  if (!prompt || state.pending) return false
  if ((prompt.kind || '').includes('resource') || ['resource-return', 'resource-payment', 'resource-selection'].includes(prompt.data?.choiceMode)) {
    const selectionTouches=[]
    const mobile = page.locator('.mobile-morale-choice:not(.unavailable):not(.selected)')
    const orb = page.locator('[data-player-index="0"] .morale-orb.payable:not(.selected)')
    const picker=page.locator('[data-player-index="0"] .mobile-morale-stack-trigger')
    if(await picker.count()&&!await mobile.count()) {
      await tap(picker,'open actual mobile morale picker')
      await mobile.first().waitFor({state:'visible'})
    }
    for (let i=0;i<prompt.minChoose;i++) {
    if (await mobile.count()) selectionTouches.push(await tap(mobile.first(), 'select payment'))
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
      selectionTouches.push(await tap(candidate, 'select payment orb'))
    }
    else {
      const option = page.locator('.prompt-choices button:not(.decline-action):not([disabled])').first()
      selectionTouches.push(await tap(option, 'select payment option'))
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
    const confirmTouch=await tap(page.getByRole('button', { name: /确认支付|确认费用|确认选择|确认返还/ }).filter({ visible: true }).first(), 'confirm payment')
    return {promptId:prompt.promptId,kind:prompt.kind,selectionTouches,confirmTouch}
  }
  if (prompt.validChoices?.includes('pass')) {
    await tap(page.getByRole('button', { name: /不响应|不发动|让过/ }).filter({ visible: true }).first(), 'pass response')
    const confirm = page.getByRole('button', { name: /确认选择/ }).filter({ visible: true }).first()
    if (await confirm.count() && await confirm.isEnabled()) await tap(confirm, 'confirm response')
    return true
  }
  throw new Error('Unsupported real prompt in acceptance: ' + prompt.kind)
}
async function waitCommandAck(transportStart, predicate, beforeRevision, label) {
  const deadline=Date.now()+12000
  while(Date.now()<deadline) {
    const sent=report.transport.slice(transportStart).find(entry=>entry.direction==='sent'&&entry.type==='gameAction'&&predicate(entry))
    const ack=sent&&typeof sent.requestId==='string'&&sent.requestId.length>0&&report.transport.slice(transportStart).find(entry=>entry.direction==='received'&&entry.type==='gameState'
      &&entry.requestId===sent.requestId&&entry.revision>beforeRevision)
    if(sent&&ack)return {requestId:sent.requestId,command:sent.command,promptId:sent.promptId,selectedValues:sent.cardInstanceIds??[],
      topCardInstanceIds:sent.topCardInstanceIds??[],bottomCardInstanceIds:sent.bottomCardInstanceIds??[],
      beforeRevision,ackRevision:ack.revision,tutorial:ack.tutorial}
    await pause(60)
  }
  throw new Error('No same-request authoritative ACK with revision advance: '+label)
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
  page.on('response',async response=>{
    const url=response.url()
    if(!/\/card-assets\/|\.(?:avif|webp|png|jpe?g)(?:\?|$)/i.test(url))return
    report.imageResponses.push({url,status:response.status(),ok:response.ok(),fromServiceWorker:response.fromServiceWorker(),
      resourceType:response.request().resourceType(),finished:await response.finished().then(()=>true,()=>false)})
  })
  page.on('requestfailed',request=>{
    const url=request.url()
    if(/\/card-assets\/|\.(?:avif|webp|png|jpe?g)(?:\?|$)/i.test(url))
      report.imageRequestFailures.push({url,resourceType:request.resourceType(),failure:request.failure()})
  })
  page.on('websocket',socket=>{
    socket.on('framesent',frame=>{try{const value=JSON.parse(String(frame.payload));if(['createTutorial','gameAction','leaveRoom','joinMatchmaking','createRoom','joinRoom'].includes(value.type)) report.transport.push({direction:'sent',type:value.type,requestId:value.requestId,lessonId:value.lessonId,command:value.command?.type,
      promptId:value.command?.promptId,cardInstanceId:value.command?.cardInstanceId,cardInstanceIds:value.command?.cardInstanceIds,
      topCardInstanceIds:value.command?.topCardInstanceIds,bottomCardInstanceIds:value.command?.bottomCardInstanceIds,
      choice:value.command?.choice,row:value.command?.row,slot:value.command?.slot,ability:value.command?.ability,target:value.command?.target})}catch{}})
    socket.on('framereceived',frame=>{try{const wire=JSON.parse(String(frame.payload))
      const value=wire.type==='gameStateCardFrameV1'?wire.payload:wire
      const deltaRequestId=wire.type==='gameStateDelta'?wire.changes?.find(change=>change.path?.length===1&&change.path[0]==='requestId')?.value:null
      if(value.type==='gameState'||wire.type==='gameStateDelta') {
      const self=value.state?.players?.find(player=>player.playerIndex===value.state.you)||value.state?.players?.[0]
      report.transport.push({direction:'received',type:'gameState',wireType:wire.type,requestId:value.requestId||deltaRequestId,
        revision:value.state?.revision??wire.revision,tutorial:value.tutorial,
        result:value.tutorial?.status==='completed'?{matchId:value.state?.matchId,activeMorale:self?.morale?.filter(card=>!card.tapped).length,
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
          morale:self.morale.map(card=>({instanceId:card.instanceId,cardId:card.cardId,tapped:card.tapped})),
          moraleDeck:(self.moraleDeck??[]).map(card=>({instanceId:card.instanceId,cardId:card.cardId,tapped:card.tapped})),
          handCount:self.handCount??self.hand?.length,masterHp:self.master.hp,
          field:self.field.map(row=>row.map(card=>card?{instanceId:card.instanceId,cardId:card.cardId,tapped:card.tapped,troops:card.troops}:null)),
          graveyard:self.graveyard.map(card=>({instanceId:card.instanceId,cardId:card.cardId}))}})))
    },{flush:'sync'})
    watch(()=>s.leavingRoom,leaving=>{
      if(leaving) observations.push({kind:'leavingRoom',order:observations.length,matchId:s.game?.matchId,
        lessonId:s.tutorial?.lessonId,status:s.tutorial?.status})
    },{flush:'sync'})
  })
  const unfinished=await waitState(s=>s.status==='online'&&!s.room&&!s.matchId&&!s.pending,'unfinished-course normal lobby')
  assert.notEqual(unfinished.learning?.finaleReviewed,true,'Use the new ordinary account without inventing completed teaching progress')
  const entrySwitchStart=report.transport.length
  for(const name of ['匹配','好友房']) {
    const entry=page.locator('.mode-tabs').getByRole('button',{name,exact:true})
    assert(await entry.isEnabled(),'Unfinished teaching must not lock the '+name+' entry')
    await tap(entry,'choose '+name+' before completing teaching without starting a normal game')
    assert.match(await entry.getAttribute('class')||'',/\bactive\b/)
    await page.getByRole('heading',{name:name==='匹配'?'公开匹配':'创建、加入或观战房间',exact:true}).waitFor()
  }
  assert.equal(report.transport.slice(entrySwitchStart).filter(e=>e.direction==='sent'
    &&['joinMatchmaking','createRoom','joinRoom'].includes(e.type)).length,0,'Choosing ordinary lobby modes must not automatically start a game')
  const afterEntrySwitch=await observe()
  assert(!afterEntrySwitch.room&&!afterEntrySwitch.matchId&&!afterEntrySwitch.pending)
  report.unfinishedCourseModeEntries={learningCompleted:false,selectable:['匹配','好友房'],ordinaryGameRequests:0,
    boundary:'Actual enabled mode tabs and rendered entry panels only; no normal room, queue, deck preparation or gameplay is created.'}
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
  async function waitTutorialSceneReady(lessonId,label) {
    await page.waitForURL('**/game')
    const state=await waitState(s=>s.status==='online'&&s.recoveryPhase==='snapshot-acknowledged'
      &&s.connectionIssue==='none'&&!s.pending&&s.gamePageVisible&&s.tutorial?.lessonId===lessonId
      &&s.tutorial.matchId===s.matchId&&s.tutorial.roomCode===s.room
      &&s.tutorial.accountId===s.accountId&&s.tutorial.sessionId===s.sessionId
      &&s.tutorial.connectionGeneration===s.connectionGeneration,label)
    await guide().waitFor({state:'visible'})
    return state
  }
  async function inspectCandidateImages(guideId) {
    const selector='[data-tutorial-prompt-id] .prompt-card-candidate'
    const capture=()=>page.locator(selector).evaluateAll(candidates=>{
      const rect=node=>{const r=node.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom}}
      const intersection=(a,b)=>{const width=Math.max(0,Math.min(a.right,b.right)-Math.max(a.x,b.x));const height=Math.max(0,Math.min(a.bottom,b.bottom)-Math.max(a.y,b.y));return {width,height,area:width*height,ratio:a.width*a.height?width*height/(a.width*a.height):0}}
      return candidates.map((candidate,index)=>{
        const picture=candidate.querySelector('.l12-card-image')
        const image=picture?.querySelector('img')
        const nameNode=candidate.querySelector('.prompt-card-candidate__name')
        const candidateRect=rect(candidate),pictureRect=picture?rect(picture):null,imageRect=image?rect(image):null,nameRect=nameNode?rect(nameNode):null
        const ancestors=[]
        for(let node=candidate.parentElement;node&&node!==document.documentElement;node=node.parentElement){
          const style=getComputedStyle(node)
          if(['hidden','clip','auto','scroll'].includes(style.overflow)||['hidden','clip','auto','scroll'].includes(style.overflowX)||['hidden','clip','auto','scroll'].includes(style.overflowY))
            ancestors.push({className:node.className,rect:rect(node),overflow:style.overflow,overflowX:style.overflowX,overflowY:style.overflowY,
              intersection:intersection(candidateRect,rect(node)),pictureIntersection:pictureRect?intersection(pictureRect,rect(node)):null,
              nameIntersection:nameRect?intersection(nameRect,rect(node)):null,clientWidth:node.clientWidth,clientHeight:node.clientHeight,
              scrollWidth:node.scrollWidth,scrollHeight:node.scrollHeight,scrollLeft:node.scrollLeft,scrollTop:node.scrollTop})
        }
        const style=image?getComputedStyle(image):null
        return {index,name:nameNode?.textContent?.trim()||'',candidateRect,pictureRect,imageRect,nameRect,
          pictureSource:picture?.dataset.source||null,pictureOrientation:picture?.dataset.orientation||null,imgPresent:Boolean(image),
          complete:image?.complete??false,naturalWidth:image?.naturalWidth??0,naturalHeight:image?.naturalHeight??0,
          src:image?.getAttribute('src')||null,currentSrc:image?.currentSrc||null,display:style?.display||null,
          visibility:style?.visibility||null,opacity:style?.opacity||null,objectFit:style?.objectFit||null,
          viewportIntersection:intersection(candidateRect,{x:0,y:0,right:innerWidth,bottom:innerHeight,width:innerWidth,height:innerHeight}),ancestors}
      })
    })
    const before=await capture()
    await page.screenshot({path:path.join(out,'390-'+guideId+'-before-image-completion.png')})
    let completion='ready'
    try {
      await page.waitForFunction(selector=>{
        const candidates=[...document.querySelectorAll(selector)]
        return candidates.length>0&&candidates.every(candidate=>{
          const picture=candidate.querySelector('.l12-card-image')
          const image=picture?.querySelector('img')
          return picture?.dataset.source&&picture.dataset.source!=='placeholder'&&image?.complete&&image.naturalWidth>0
        })
      },selector,{timeout:12000})
    } catch { completion='timeout' }
    const after=await capture()
    await page.screenshot({path:path.join(out,'390-'+guideId+'-after-image-completion.png')})
    const check={guideId,completion,before,after,imageResponses:report.imageResponses.filter(entry=>
      [...new Set([...before,...after].flatMap(item=>[item.src,item.currentSrc]).filter(Boolean))].includes(entry.url))}
    report.candidateImageChecks.push(check)
    assert.equal(completion,'ready','Candidate images must complete and decode in '+guideId)
    assert(after.every(item=>item.imgPresent&&item.complete&&item.naturalWidth>0&&item.pictureSource!=='placeholder'),
      'Every candidate must expose decoded non-placeholder pixels in '+guideId)
    const reachable=[]
    for(let index=0;index<after.length;index++) {
      // Sorting deliberately keeps operation/direction text in the same scroll body.
      // Check every card after revealing it, not that all scroll content is simultaneous.
      if(guideId==='response-search-result') await page.locator(selector).nth(index).scrollIntoViewIfNeeded()
      const item=(await capture())[index]
      reachable.push(item)
      assert(item.pictureRect?.width>24&&item.pictureRect?.height>24,'Real card art remains nonzero in '+guideId)
      const viewport=await page.viewportSize()
      for(const rect of [item.pictureRect,item.nameRect])
        assert(rect&&rect.x>=-1&&rect.y>=-1&&rect.right<=viewport.width+1&&rect.bottom<=viewport.height+1,
          'Revealed card art and name fit the physical viewport in '+guideId)
      for(const ancestor of item.ancestors) {
        assert(ancestor.pictureIntersection?.ratio>=0.98,'Complete candidate art is visible inside '+ancestor.className+' in '+guideId)
        assert(ancestor.nameIntersection?.ratio>=0.98,'Complete candidate name is visible inside '+ancestor.className+' in '+guideId)
      }
    }
    check.reachable=reachable
    check.boundary='Art/name containment and decoded pixels in actual teaching; sorting cards revealed through browser scrollIntoViewIfNeeded. Subsequent gameplay uses native touch. This does not prove user pan gestures or real mobile browser toolbar/safe-area behavior.'
    return check
  }
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
    assert.equal(await page.locator('.catalog-detail-mask').count(),0,'Practical teaching must never automatically open or reopen card details after page, card or DOM changes')
    const details={automaticModal:false,actualAnchor:null}
    const guideId=await shell.getAttribute('data-tutorial-guide-id')
    const state=await observe()
    const instanceId=guideId==='card'?state.self.hand.find(card=>card.cardId==='ST01-05')?.instanceId
      :guideId==='response-entry-card'?state.tutorial.focus.legionInstanceId
        :guideId==='response-cover-card'?state.tutorial.focus.counterInstanceId:null
    let selector=null
    let choiceAnchor=false,promptId=null
    if(instanceId) selector=state.self.field.flat().some(card=>card?.instanceId===instanceId)
      ?'[data-tutorial-player-index="0"] [data-tutorial-anchor="field-slot"][data-tutorial-card-instance="'+instanceId+'"]'
      :'.hand-card-wrap[data-tutorial-card-instance="'+instanceId+'"]'
    if(guideId==='response-artifact-card') {
      assert.equal(state.self.relic.instanceId,state.tutorial.focus.artifactInstanceId,'The highlighted relic is the actual current recipe instance')
      selector='[data-tutorial-player-index="0"] [data-l12-zone="relic"]'
    }
    if(['response-search-result','response-entry-card'].includes(guideId)) {
      const ordering=guideId==='response-search-result'
      const prompt=state.prompts.find(prompt=>prompt.playerIndex===state.self.playerIndex&&prompt.kind===(ordering?'order':'hand-card'))
      assert(prompt,'Use the actual current owner interaction layer')
      const sourceId=ordering?state.tutorial.focus.artifactInstanceId:state.tutorial.focus.counterInstanceId
      const sources=[prompt.sourceInstanceId,prompt.data?.sourceInstanceId].filter(value=>value!==null&&value!==undefined&&value!=='')
      assert(sources.length&&sources.every(value=>value===sourceId),'The current interaction keeps the exact recipe source')
      const prefix='[data-tutorial-prompt-id="'+prompt.promptId.replaceAll('\\','\\\\').replaceAll('"','\\"')+'"]'
      if(ordering) {assert.equal(prompt.data.placementMode,'all-top-bottom');selector=prefix+' [data-tutorial-action="place-all-top"]'}
      else {assert(prompt.validChoices.includes(instanceId));selector=prefix+' [data-tutorial-choice="'+instanceId+'"]';choiceAnchor=true}
      promptId=prompt.promptId
    }
    if(selector) {
      const actual=page.locator(selector).filter({visible:true}).first()
      await actual.waitFor()
      await page.waitForFunction(()=>!document.querySelector('[data-ui-contract="tutorial-authoritative-guidance"] .tutorial-anchor-unavailable'),null,{timeout:2000})
      assert.equal(await shell.locator('.tutorial-anchor-unavailable').count(),0,'The practical card anchor must exist without opening details')
      if(instanceId) assert.equal(await actual.getAttribute(choiceAnchor?'data-tutorial-choice':'data-tutorial-card-instance'),instanceId)
      details.actualAnchor={selector,instanceId,promptId,rect:await actual.boundingBox()}
      if(['response-search-result','response-entry-card'].includes(guideId))
        details.candidateImages=await inspectCandidateImages(guideId)
      await page.screenshot({path:path.join(out,viewport.width+'-'+guideId+'-actual-anchor.png')})
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
    const choiceTouch=await tap(page.locator('[data-tutorial-choice="'+instanceId+'"]').filter({visible:true}).first(),label)
    const deadline=Date.now()+10000
    while(Date.now()<deadline) {
      const state=await observe()
      if(state.pending||!state.prompts.some(p=>p.promptId===promptId)) return {choiceTouch,confirmTouch:null}
      const confirm=page.locator('[data-tutorial-action="confirm-prompt"]').filter({visible:true}).first()
      if(await confirm.count()&&await confirm.isEnabled()) {const confirmTouch=await tap(confirm,'confirm '+label);return {choiceTouch,confirmTouch}}
      await pause(80)
    }
    throw new Error('Chosen prompt neither submitted nor exposed its confirmation: '+label)
  }
  const settingsOnly=process.env.L12_NOTIFICATION_SETTINGS_ONLY==='1'
  const visualSliceOnly=process.env.L12_TUTORIAL_VISUAL_SLICE_ONLY==='1'
  report.mode=settingsOnly?'actual-settings-only':visualSliceOnly?'real-component-visual-slice':effectSliceOnly?'mobile-effect-full-chain-slice':firstChapterOnly?'actual-first-chapter-auto-handoff':'continuous-course-and-actual-settings'
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
  } else for(const [viewportIndex,viewport] of (effectSliceOnly
    ? effectViewports
    : [{width:1366,height:768},{width:844,height:390},{width:390,height:844}]).entries()) {
    await page.setViewportSize(viewport)
    if(settingsOnly){await inspectNotificationSettings(viewport);report.cases.push({viewport,scope:'settings-only; no gameplay'});continue}
    if(viewportIndex>0) {
      if(new URL(page.url()).pathname==='/battle') await tap(page.locator('.mode-tabs').getByRole('button',{name:'教学',exact:true}),'reopen the course after completion CTA')
      await tap(page.getByRole('button',{name:'重新学习第一章',exact:true}),'repeat coherent chapter')
    }
    const titles=await readIntroduction(viewport)
    const chapter={viewport,introduction:titles,scenes:[]}
    for(const [index,scene] of ['deploy','move','combat'].entries()) {
      const initial=await waitTutorialSceneReady(scene,'ready '+scene)
      const transportStart=report.transport.length
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
    const responseStart=await waitTutorialSceneReady('response','ready response chapter')
    const responseTransportStart=report.transport.length
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
    const discardTransportStart=report.transport.length
    const discardInteraction=await choosePromptCard(discardPrompt.promptId,discardCard.instanceId,'pay actual discard cost')
    const searchState=await settleTo(s=>s.tutorial?.stepId==='response-search-pick','real search')
    const discardAck=await waitCommandAck(discardTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===discardPrompt.promptId,costState.revision,'Shanhe hand discard cost')
    assert(searchState.self.graveyard.some(card=>card.instanceId===discardCard.instanceId),'Paid hand card reaches authoritative graveyard')
    assert.deepEqual(discardAck.selectedValues,[discardCard.instanceId],'Exact touched hand cost is sent to the server')
    report.effectReceipts.push({type:'hand-card-cost',abilityId:'S01-0117:ability:active:ba48403c4da1e24c',viewport,touchStart:discardInteraction.choiceTouch,
      prompt:{promptId:discardPrompt.promptId,kind:discardPrompt.kind,minChoose:discardPrompt.minChoose,maxChoose:discardPrompt.maxChoose},
      ack:discardAck,readback:{revision:searchState.revision,stepId:searchState.tutorial.stepId,
        selectedCard:{instanceId:discardCard.instanceId,cardId:discardCard.cardId},zone:'graveyard',
        present:searchState.self.graveyard.some(card=>card.instanceId===discardCard.instanceId),nextPromptKind:searchState.prompts.find(p=>p.playerIndex===0)?.kind}})
    await readScene(viewport,'response-search-pick',responseReadings)
    const legionId=searchState.tutorial.focus.legionInstanceId
    const searchPrompt=searchState.prompts.find(p=>p.playerIndex===0&&p.validChoices.includes(legionId))
    assert(searchPrompt.validChoices.includes(legionId))
    assert.equal(searchPrompt.data[legionId+':cardId'],'ST01-08')
    const handBeforeSearch=searchState.self.hand.map(card=>card.instanceId)
    const searchTransportStart=report.transport.length
    const searchInteraction=await choosePromptCard(searchPrompt.promptId,legionId,'select Nie from actual search')
    const orderState=await settleTo(s=>s.tutorial?.stepId==='response-search-order','remaining library order')
    const searchAck=await waitCommandAck(searchTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===searchPrompt.promptId,searchState.revision,'Shanhe private library search choice')
    assert.deepEqual(searchAck.selectedValues,[legionId],'Search sends the exact touched private library card')
    assert(!handBeforeSearch.includes(legionId)&&orderState.self.hand.some(card=>card.instanceId===legionId),
      'The exact searched legion enters the owner hand after authoritative ACK')
    assert(orderState.self.relic?.instanceId===searchState.tutorial.focus.artifactInstanceId&&orderState.self.relic.tapped,
      'Shanhe source remains tapped after the search step')
    report.effectReceipts.push({type:'library-search-pick',abilityId:'S01-0117:ability:active:ba48403c4da1e24c',viewport,
      touchStart:searchInteraction.choiceTouch,prompt:{promptId:searchPrompt.promptId,kind:searchPrompt.kind,
        minChoose:searchPrompt.minChoose,maxChoose:searchPrompt.maxChoose,validChoices:searchPrompt.validChoices},ack:searchAck,
      readback:{revision:orderState.revision,stepId:orderState.tutorial.stepId,selectedCardInstanceId:legionId,
        selectedCardId:orderState.self.hand.find(card=>card.instanceId===legionId)?.cardId,zone:'hand',sourceTapped:true,
        nextPromptKind:orderState.prompts.find(prompt=>prompt.playerIndex===0)?.kind}})
    await readScene(viewport,'response-search-order',responseReadings)
    const orderPrompt=orderState.prompts.find(prompt=>prompt.playerIndex===0&&prompt.kind==='order')
    assert(orderPrompt&&orderPrompt.data?.placementMode==='all-top-bottom','Shanhe remainder uses the private all-top-or-bottom order prompt')
    const originalOrder=[...orderPrompt.validChoices]
    assert(originalOrder.length>=2&&new Set(originalOrder).size===originalOrder.length,'Shanhe order prompt carries the distinct remaining cards')
    assert(originalOrder.every(id=>orderPrompt.data[id+':cardId']),'Owner prompt carries private card detail for every remainder')
    assert.equal(orderState.self.libraryTop??null,null,'Ordinary player projection does not expose the private library top')
    const orderCards=page.locator('.all-placement-row .prompt-card-candidate')
    assert.equal(await orderCards.count(),originalOrder.length,'Every private remainder has one visible order card')
    const firstSwapTouch=await tap(orderCards.nth(0),'select first remainder for a real order swap')
    const secondSwapTouch=await tap(orderCards.nth(1),'swap first two remainders by touch')
    const submittedOrder=[originalOrder[1],originalOrder[0],...originalOrder.slice(2)]
    const orderTransportStart=report.transport.length
    const orderConfirmTouch=await tap(page.locator('[data-tutorial-action="place-all-top"]').filter({visible:true}),'return reordered actual remainder to top')
    const coverState=await waitState(s=>s.tutorial?.stepId==='response-cover'&&!s.pending,'response cover after authoritative library order')
    const orderAck=await waitCommandAck(orderTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===orderPrompt.promptId,orderState.revision,'Shanhe private library order placement')
    assert.deepEqual(orderAck.topCardInstanceIds,submittedOrder,'Submitted top order matches the exact touch-swapped order')
    assert.deepEqual(orderAck.bottomCardInstanceIds,[],'No remainder is submitted to the library bottom')
    assert.deepEqual([...orderAck.topCardInstanceIds].sort(),[...originalOrder].sort(),'Placement preserves the full inspected remainder set')
    assert(!coverState.prompts.some(prompt=>prompt.promptId===orderPrompt.promptId),'Accepted private order prompt is closed')
    assert(coverState.self.hand.some(card=>card.instanceId===legionId),'Searched legion stays in the owner hand after placement')
    assert(originalOrder.every(id=>!coverState.self.hand.some(card=>card.instanceId===id)
      &&!coverState.self.graveyard.some(card=>card.instanceId===id)
      &&!coverState.self.field.flat().some(card=>card?.instanceId===id)),'Every unselected inspected card remains outside public and hand zones')
    assert(coverState.self.relic?.instanceId===searchState.tutorial.focus.artifactInstanceId&&coverState.self.relic.tapped,
      'Shanhe source remains tapped after the full search and order chain')
    const reorderEvent=[...coverState.recentEvents].reverse().find(event=>event.type==='reorder')
    assert(reorderEvent&&reorderEvent.text===`将 ${originalOrder.length} 张牌放回牌库顶部、0 张牌放回牌库底部`,
      'Authoritative event confirms the accepted placement count and destination')
    assert.equal(coverState.self.libraryTop??null,null,'Final ordinary projection still keeps the exact library order private')
    report.effectReceipts.push({type:'library-order-placement',abilityId:'S01-0117:ability:active:ba48403c4da1e24c',viewport,
      touchStart:firstSwapTouch,swapTouch:secondSwapTouch,confirmation:orderConfirmTouch,
      prompt:{promptId:orderPrompt.promptId,kind:orderPrompt.kind,placementMode:orderPrompt.data.placementMode,
        minChoose:orderPrompt.minChoose,maxChoose:orderPrompt.maxChoose,originalOrder},ack:orderAck,
      readback:{revision:coverState.revision,stepId:coverState.tutorial.stepId,submittedOrder,destination:'library-top',
        preservedSet:true,sourceTapped:true,reorderEvent:{type:reorderEvent.type,text:reorderEvent.text},
        ownerProjectionLibraryTopHidden:true,serverReceiptBoundary:'Submitted order and accepted count; the browser projection does not expose the authoritative private library order.',
        opponentProjectionBoundary:'Not observed in this one-client UI chain.',
        combinedEngineEvidence:['NewSystemsTests.ShanheShejituReusesObservingStarsAllTopBottomOrdering',
          'LibraryPlacementTransactionTests.RestoredOrderCompletesExactlyOnceAndDoesNotRevealCards']}})
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
    const returnState=await waitState(s=>s.tutorial?.stepId==='response-return'&&!s.pending,'return payment')
    const returnPrompt=returnState.prompts.find(p=>p.playerIndex===0)
    assert(returnPrompt?.kind==='target-morale'&&returnPrompt?.data?.choiceMode==='resource-selection'
      &&returnPrompt.sourceInstanceId===responseCounter.instanceId,
    'Dark Passage return cost must use its bound target-morale resource selection')
    await readScene(viewport,'response-return',responseReadings)
    assert.match(await guide().innerText(),/返还|返回士气牌库/)
    assert.doesNotMatch(await guide().innerText(),/确认支付后，所选士气转为休整/)
    await page.screenshot({path:path.join(out,viewport.width+'-response-return-payment.png')})
    const returnReady=await observe()
    assert.equal(returnReady.revision,returnState.revision,'Reading the return explanation must not mutate authoritative gameplay')
    assert(returnReady.prompts.some(prompt=>prompt.promptId===returnPrompt.promptId),'Return prompt remains active until the player acts')
    const returnTransportStart=report.transport.length
    const returnInteraction=await resolveOwnPrompt()
    const summonState=await settleTo(s=>s.tutorial?.stepId==='response-summon','hand legion choice')
    const returnAck=await waitCommandAck(returnTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===returnPrompt.promptId,returnState.revision,'Dark Passage morale return')
    const returnedIds=returnInteraction.selectionTouches.map(touch=>touch.target.tutorialPaymentResource).filter(Boolean)
    assert.equal(returnedIds.length,returnPrompt.minChoose,'Every returned morale is tied to its touched instance')
    assert.deepEqual(returnAck.selectedValues,returnedIds,'Declared return matches the exact touched resource instances')
    assert(returnedIds.every(id=>summonState.self.morale.some(card=>card.instanceId===id)),
      'Return selection remains an uncommitted declaration until all Dark Passage targets are valid')
    const returnReceipt={type:'resource-return',abilityId:'ST01-10:ability:reaction:6c595d8cfc22fc59',viewport,touchStart:returnInteraction.selectionTouches[0],
      confirmation:returnInteraction.confirmTouch,
      prompt:{promptId:returnPrompt.promptId,kind:returnPrompt.kind,minChoose:returnPrompt.minChoose,maxChoose:returnPrompt.maxChoose,
        choiceMode:returnPrompt.data?.choiceMode},ack:returnAck,
      declarationReadback:{revision:summonState.revision,stepId:summonState.tutorial.stepId,returnedIds,
        selectedStillPresentBeforeCommit:returnedIds.every(id=>summonState.self.morale.some(card=>card.instanceId===id)),
        nextPromptKind:summonState.prompts.find(p=>p.playerIndex===0)?.kind}}
    await readScene(viewport,'response-summon',responseReadings)
    const handPrompt=summonState.prompts.find(p=>p.playerIndex===0)
    const handTransportStart=report.transport.length
    const handInteraction=await choosePromptCard(handPrompt.promptId,legionId,'select hand Nie')
    const positionState=await settleTo(s=>s.tutorial?.stepId==='response-position','entry position')
    const handAck=await waitCommandAck(handTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===handPrompt.promptId,summonState.revision,'Dark Passage hand legion choice')
    assert.deepEqual(handAck.selectedValues,[legionId],'Hand declaration contains the exact touched legion')
    await readScene(viewport,'response-position',responseReadings)
    const slotPrompt=positionState.prompts.find(p=>p.playerIndex===0)
    const slotTransportStart=report.transport.length
    const slotTouch=await tap(page.locator('[data-player-index="0"] .formation-slot.available[data-tutorial-row="0"]').first(),'choose real empty front slot')
    const responseFinal=await completedFrame('response',responseStart.matchId,responseTransportStart)
    const slotAck=await waitCommandAck(slotTransportStart,entry=>entry.command==='resolvePrompt'
      &&entry.promptId===slotPrompt.promptId,positionState.revision,'Dark Passage entry slot')
    assert.equal(responseFinal.result.field.flat().find(c=>c?.instanceId===legionId).tapped,false)
    assert.equal(responseFinal.result.masterHp,defenseState.self.master.hp-1,'Counter does not erase committed attack damage')
    assert(responseFinal.result.graveyard.some(c=>c.instanceId===responseCounter.instanceId))
    assert(responseFinal.result.graveyard.some(c=>c.instanceId===discardCard.instanceId))
    assert(returnedIds.every(id=>!responseFinal.result.morale.some(card=>card.instanceId===id)
      &&responseFinal.result.moraleDeck.some(card=>card.instanceId===id)),
    'Committed Dark Passage cost leaves morale and returns the exact selected card to its authoritative deck')
    returnReceipt.readback={revision:responseFinal.revision,stepId:responseFinal.tutorial.stepId,returnedIds,
      remainingMorale:responseFinal.result.morale,returnedToMoraleDeck:responseFinal.result.moraleDeck.filter(card=>returnedIds.includes(card.instanceId)),
      status:responseFinal.tutorial.status}
    report.effectReceipts.push(returnReceipt)
    const entered=responseFinal.result.field.flat().find(c=>c?.instanceId===legionId)
    const selectedRow=Number(slotTouch.target.tutorialRow),selectedSlot=Number(slotTouch.target.tutorialSlot)
    assert(slotTouch.target.tutorialRow!==null&&slotTouch.target.tutorialSlot!==null,'Slot touch exposes its actual board coordinate')
    assert.equal(responseFinal.result.field[selectedRow]?.[selectedSlot]?.instanceId,legionId,'Legion enters the precise touched slot')
    report.effectReceipts.push({type:'hand-card-to-board-slot',abilityId:'ST01-10:ability:reaction:6c595d8cfc22fc59',viewport,touchStart:handInteraction.choiceTouch,
      slotTouch,handPrompt:{promptId:handPrompt.promptId,kind:handPrompt.kind},slotPrompt:{promptId:slotPrompt.promptId,kind:slotPrompt.kind},
      acknowledgements:[handAck,slotAck],readback:{revision:responseFinal.revision,stepId:responseFinal.tutorial.stepId,
        selectedCardInstanceId:legionId,enteredCard:entered,row:selectedRow,slot:selectedSlot,zone:'field',active:entered?.tapped===false,status:responseFinal.tutorial.status}})
    await waitState(s=>s.tutorial?.lessonId==='preparation'&&!s.pending&&s.learning.responseReviewed===true,'response real roomLeft and preparation')
    chapter.response={initialRevision:responseStart.revision,finalRevision:responseFinal.revision,
      readings:responseReadings,result:responseFinal.result,status:'authoritatively-completed-and-auto-continued'}
    console.log('PASS continuous response UI '+viewport.width+'x'+viewport.height)
    if(effectSliceOnly) {
      await tap(guide().locator('summary'),'open tutorial auxiliary actions after effect slice')
      await tap(guide().getByRole('button',{name:'退出教学',exact:true}),'exit after mobile effect slice')
      await waitState(s=>!s.room&&!s.matchId&&!s.pending,'mobile-effect-slice exit')
      report.cases.push(chapter); continue
    }
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
    const finaleExitTransportStart=report.transport.length
    await tap(page.getByRole('button',{name:'完成教学',exact:true}),'finish actual course')
    await waitState(s=>!s.room&&!s.matchId&&!s.pending,'final course roomLeft')
    const finalExit=report.transport.slice(finaleExitTransportStart)
    const finalLeave=finalExit.findIndex(e=>e.direction==='sent'&&e.type==='leaveRoom')
    const finalLeft=finalExit.findIndex(e=>e.direction==='received'&&e.type==='roomLeft')
    assert(finalLeave>=0&&finalLeft>finalLeave,'Completed teaching must receive actual roomLeft after its leave request')
    await page.waitForURL('**/battle/tutorial')
    await page.getByRole('heading',{name:'你已完成新手教学',exact:true}).waitFor()
    const firstGameLink=page.getByRole('link',{name:'前往对战',exact:true})
    assert.equal(await firstGameLink.getAttribute('href'),'/battle')
    await tap(firstGameLink,'leave completed teaching for the normal lobby')
    await page.waitForURL('**/battle')
    await waitState(s=>!s.room&&!s.matchId&&!s.pending,'normal lobby without tutorial room')
    assert.equal(report.transport.slice(finaleTransportStart).filter(e=>e.direction==='sent'&&['joinMatchmaking','createRoom','joinRoom'].includes(e.type)).length,0,'Completion CTA does not automatically create or queue a normal game')
    chapter.handoff={route:'/battle',roomCleared:true,roomLeftReceived:true,automaticNormalGameRequests:0}
    chapter.preparation={readings:preparationReadings,prompts:preparationPrompts,status:'actual-preparation-completed'}
    chapter.finale={readings:finaleReadings,playerAttacks,result:finaleFinal.result,status:'actual-victory-and-saved-progress'}
    await inspectNotificationSettings(viewport)
    console.log('PASS continuous full course '+viewport.width+'x'+viewport.height)
    report.cases.push(chapter)
  }
  assert.deepEqual(report.errors,[])
  if(effectSliceOnly) {
    assert.equal(report.effectReceipts.length,5,'Five exact effect interactions in this explicit independent mobile direction')
    for(const {width} of effectViewports) assert.deepEqual(report.effectReceipts.filter(row=>row.viewport.width===width).map(row=>row.type).sort(),
      ['hand-card-cost','hand-card-to-board-slot','library-order-placement','library-search-pick','resource-return'])
    assert(report.effectReceipts.every(row=>row.touchStart.input==='touchscreen'),'Every accepted effect interaction begins with native touch')
  }
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
  report.viteClosed=!vite
  if(vite) {
    const result=await Promise.race([
      Promise.resolve().then(()=>vite.close()).then(()=>({closed:true}),error=>({closed:false,error:String(error.message)})),
      pause(8000).then(()=>({closed:false,error:'Vite close exceeded 8 seconds'})),
    ])
    report.viteClosed=result.closed&&!vite.httpServer?.listening
    if(!report.viteClosed){report.viteCleanupError=result.error||'Vite remains listening after close';report.status='FAILED'}
  }
  if(!hostDone) host.kill()
  if(!hostDone) await Promise.race([new Promise(resolve=>host.once('exit',resolve)),pause(5000)])
  fs.closeSync(hostLog)
  report.hostExited=hostDone
  if(!['GREEN','CAPTURED'].includes(report.status)||!report.hostExited) process.exitCode=1
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n')
  console.log(JSON.stringify({status:report.status,cases:report.cases.length,failure:report.failure,hostExited:report.hostExited}))
}
