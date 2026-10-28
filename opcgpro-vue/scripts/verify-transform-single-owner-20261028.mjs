import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const script = fileURLToPath(import.meta.url)
const front = path.resolve(path.dirname(script), '..')
const repo = path.dirname(front)
const output = process.env.L12_TRANSFORM_OUT
const cache = 'D:/GPT/Legion12/cache/primary'
const temp = path.join(cache, 't', 'tr28')
if (!output || !path.isAbsolute(output)) throw new Error('L12_TRANSFORM_OUT must be absolute')
if (fs.existsSync(output)) throw new Error('Refusing to replace evidence: ' + output)
execFileSync('pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
  '& $env:L12_INIT -CacheRoot $env:L12_CACHE | Out-Null; . $env:L12_GUARD; '
  + '@($env:L12_OUT,$env:L12_CACHE,$env:TEMP) | ForEach-Object { $null=Assert-L12GeneratedPath -Path $_ -Purpose "transform ownership QA" }'],
{ env:{ ...process.env, L12_INIT:path.join(repo, 'ops/windows/Initialize-L12BuildEnvironment.ps1'),
  L12_GUARD:path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'), L12_CACHE:cache, L12_OUT:output,
  TEMP:temp, TMP:temp, TMPDIR:temp }, stdio:'pipe' })
fs.mkdirSync(output, { recursive:true })
fs.mkdirSync(temp, { recursive:true })
Object.assign(process.env, { TEMP:temp, TMP:temp, TMPDIR:temp })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const fixture = path.join(front, 'scripts/fixtures/BattleVisualTransitionHarness.vue')
const files = [script, fixture, ...['ZoneMovementPresentationLayer.vue', 'visualTransitionProjection.ts',
  'CardStateTransitionLayer.vue', 'PlayerMat.vue', 'GameBoard.vue', 'presentationSequenceCoordinator.ts',
  'authoritativeCardVisibility.ts'].map(name => path.join(front, 'src/l12/game', name))]
const pins = () => Object.fromEntries(files.map(file => [path.relative(repo, file),
  createHash('sha256').update(fs.readFileSync(file)).digest('hex')]))
const report = { schema:1, contract:'transform-single-owner-20261028', sources:{ before:pins() },
  scenarios:[], errors:[], cleanup:{} }
const injection = String.raw`
const transformSeat = Number(harnessParams.get('seat') || 0)
const transformRelic = harnessParams.get('shape') === 'relic'
const transformInitialEntry = harnessParams.get('shape') === 'entry' || transformRelic
const transformCard = { ...card('qa-transform-real-' + transformSeat, transformRelic ? '草薙剑' : '孙悟空',
  transformRelic ? 'S01-0417' : 'S02-01M1', harnessParams.get('rested') !== '0',
  faceUrl(transformRelic ? 'S01-0417' : 'S02-01M1'), 4000),
  cardType:transformRelic ? 'artifact' : 'master', isMasterLegion:!transformRelic }
for (const participant of game.players) {
  participant.field = [[null,null,null],[null,null,null]]
  participant.hand = []; participant.handCount = 0; participant.graveyard = []; participant.graveyardCount = 0
  participant.master.masterImageUrl = faceUrl(participant.master.masterId)
}
const transformPlayer = game.players[transformSeat]!
Object.assign(transformPlayer.master, { masterId:'S02-01M1', masterName:'孙悟空',
  masterImageUrl:faceUrl('S02-01M1'), deployedAsLegion:!transformInitialEntry, tapped:false })
if (transformRelic) transformPlayer.relic = { ...transformCard }
else if (!transformInitialEntry) transformPlayer.field[0][0] = { ...transformCard }
Object.assign(l12State, { game, status:'online', recoveryPhase:'snapshot-acknowledged' })
function transformCommit(to: 'master' | 'field', tapped = true) {
  const moving = { ...transformCard, tapped:to === 'field' ? false : tapped }
  transformPlayer.field = [[to === 'field' ? moving : null,null,null],[null,null,null]]
  if (transformRelic) transformPlayer.relic = null
  else Object.assign(transformPlayer.master, { deployedAsLegion:to === 'field', tapped:to === 'master' && tapped })
  const event: ActionEvent = { sequence:++sequence, type:to === 'field' ? 'put' : 'return', playerIndex:transformSeat,
    originZone:harnessParams.get('structured') === '1' ? to === 'field' ? transformRelic ? 'relic' : 'master' : 'field' : undefined,
    text:to === 'field' ? transformRelic ? '草薙剑从圣物区作为军团登场' : '孙悟空从主宰区作为军团活跃登场' : '孙悟空进攻后返回主宰区',
    cards:[moving] }
  game.recentEvents = [...(game.recentEvents ?? []), event]
  game.revision += 1
  game.stateHash = 'transform-' + game.revision
}
const api = {
  transformCommit,
  transformRepeatSnapshot() { game.recentEvents = game.recentEvents?.map(event => ({ ...event, cards:event.cards?.map(card => ({ ...card })) })); game.revision += 1 },
  transformBeginRecovery() { l12State.status = 'connecting'; l12State.recoveryPhase = 'snapshot-received' },
  transformFinishRecovery() { l12State.status = 'online'; l12State.recoveryPhase = 'snapshot-acknowledged' },
`
const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body><div id="app"></div><script type="module">import {createApp} from "vue";import Harness from "/scripts/fixtures/BattleVisualTransitionHarness.vue";import "/src/style.css";import "/src/l12/mobileViewport.css";import "/src/l12/motion.css";window.__transformApp=createApp(Harness);window.__transformApp.mount("#app")</script></body></html>'
const plugin = {
  name:'transform-single-owner-fixture', enforce:'pre',
  transform(source, id) {
    if (id.split('?')[0].replaceAll('\\', '/') !== fixture.replaceAll('\\', '/')) return null
    assert.equal(source.split('const api = {').length, 2)
    return { code:source.replace('const api = {', injection), map:null }
  },
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      if (request.url?.startsWith('/api/site/media/visual-transition/')) {
        response.setHeader('Content-Type','image/svg+xml')
        response.end('<svg xmlns="http://www.w3.org/2000/svg" width="250" height="350"><rect width="250" height="350" fill="#b99145"/><rect x="15" y="15" width="220" height="320" fill="#234e65"/><path d="M30 30h90v100H30z" fill="#edc85b"/><text x="125" y="230" text-anchor="middle" fill="white" font-size="26">TRANSFORM</text></svg>')
      } else next()
    })
    server.middlewares.use('/__transform-owner', async (_req,res) => {
      res.setHeader('Content-Type','text/html; charset=utf-8')
      res.end(await server.transformIndexHtml('/__transform-owner',html))
    })
  },
}
const viteCache = path.join(cache, 'vite', 'tr28')
const server = await createServer({ root:front, cacheDir:viteCache, configLoader:'runner', plugins:[plugin],
  server:{ host:'127.0.0.1',port:0 }, logLevel:'error' })
let browser
let port
function check(value, message, scenario) {
  if (!value) report.errors.push(scenario + ': ' + message)
}
async function open(options = {}) {
  const context = await browser.newContext({ viewport:options.portrait ? { width:390,height:844 } : { width:1366,height:844 } })
  await context.addInitScript(animation => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({animation})), options.animation || 'standard')
  const page = await context.newPage()
  page.on('pageerror',error => report.errors.push('pageerror: '+error.message))
  const params = new URLSearchParams({ viewer:String(options.viewer ?? 0), seat:String(options.seat ?? 0),
    shape:options.shape || 'return', rested:options.rested === false ? '0' : '1',
    ...(options.spectator ? {spectator:'1'} : {interactive:'1'}), ...(options.portrait ? {landscape:'1'} : {}) })
  await page.goto(`http://127.0.0.1:${port}/__transform-owner?${params}`,{waitUntil:'networkidle'})
  await page.locator('[data-l12-game-stage]').waitFor()
  await page.waitForFunction(seat => {
    const card=document.querySelector('[data-card-instance-id="qa-transform-real-'+seat+'"] img')
      || document.querySelector('[data-card-instance-id="master-'+seat+'"] img')
    return card instanceof HTMLImageElement && card.complete && card.naturalWidth>0
  },options.seat ?? 0)
  return {context,page}
}
async function trace(page, seat, action, duration=1500) {
  return page.evaluate(async ({seat,action,duration}) => {
    const visible = node => {
      if (!(node instanceof Element)) return false
      for(let parent=node;parent;parent=parent.parentElement) {
        const style=getComputedStyle(parent)
        if(style.display==='none'||style.visibility==='hidden'||Number(style.opacity)<.01||style.clipPath==='inset(50%)')return false
      }
      const rect=node.getBoundingClientRect()
      return rect.width>0&&rect.height>0
    }
    const hasPixels = node => node && [...node.querySelectorAll('img,canvas')].some(image => visible(image)
      && (image instanceof HTMLCanvasElement ? image.width>0&&image.height>0 : image.complete&&image.naturalWidth>0))
    const id='qa-transform-real-'+seat
    const frames=[]
    const keys=new Set()
    let firstInvalid=null
    const relic=new URLSearchParams(location.search).get('shape')==='relic'
    const started=performance.now()
    const sample=()=>{
      const master=document.querySelector('[data-card-instance-id="master-'+seat+'"]')
      const field=[...document.querySelectorAll('[data-l12-zone="field"] [data-card-instance-id="'+id+'"],[data-l12-zone="relic"] [data-card-instance-id="'+id+'"]')].find(visible)
      const ghosts=[...document.querySelectorAll('[data-movement-instance-id="'+id+'"]')].filter(hasPixels)
      const states=[...document.querySelectorAll('.l12-card-state-transition-ghost')].filter(node =>
        (node.dataset.visualTransitionKey?.includes(id)||node.dataset.visualTransitionKey?.includes('master-'+seat))&&hasPixels(node))
      ghosts.forEach(node=>keys.add(node.dataset.movementKey))
      const art=master?.querySelector('.master-visual-body')
      const masterPixels=!relic&&hasPixels(art)?1:0
      const fieldPixels=hasPixels(field)?1:0
      const metadata=[master?.querySelector('.master-name'),master?.querySelector('.master-health')]
      frames.push({at:performance.now()-started,master:masterPixels,field:fieldPixels,ghost:ghosts.length,state:states.length,
        count:masterPixels+fieldPixels+ghosts.length+states.length,
        metadata:metadata.every(node=>visible(node)&&getComputedStyle(node).transform==='none'),
        moving:ghosts.filter(node=>node.getAnimations().some(animation=>animation.playState==='running')).length})
      if(!firstInvalid&&frames.at(-1).count!==1) firstInvalid={frame:frames.at(-1),
        ghosts:[...document.querySelectorAll('[data-movement-instance-id="'+id+'"]')].map(node=>({html:node.outerHTML.slice(0,6000),
          faces:[...node.querySelectorAll('img,canvas')].map(face=>({tag:face.tagName,visible:visible(face),rect:face.getBoundingClientRect().toJSON(),visibility:getComputedStyle(face).visibility,display:getComputedStyle(face).display,opacity:getComputedStyle(face).opacity}))}))}
    }
    sample()
    const api=window.__visualTransitionHarness
    if(action==='return')api.transformCommit('master',true)
    else if(action==='activeReturn')api.transformCommit('master',false)
    else if(action==='entry')api.transformCommit('field')
    else if(action==='duplicate')api.transformRepeatSnapshot()
    else if(action==='chain'){api.transformCommit('master',true);api.transformCommit('field')}
    else if(action==='cancel'){api.transformCommit('master',true);setTimeout(()=>api.openPrompt(),60)}
    else if(action==='unmount'){api.transformCommit('master',true);setTimeout(()=>window.__transformApp.unmount(),60)}
    await new Promise(resolve=>{const tick=()=>{sample();if(performance.now()-started<duration)requestAnimationFrame(tick);else resolve()};requestAnimationFrame(tick)})
    return {frames,movementKeys:[...keys],maxVisible:Math.max(...frames.map(frame=>frame.count)),
      minVisible:Math.min(...frames.map(frame=>frame.count)),ghostFrames:frames.filter(frame=>frame.ghost).length,
      stateFrames:frames.filter(frame=>frame.state).length,movingFrames:frames.filter(frame=>frame.moving).length,
      metadataStable:frames.every(frame=>frame.metadata),firstInvalid}
  }, {seat,action,duration})
}

async function sampleAction(page,seat,action,duration=1500) {
  return trace(page,seat,action,duration)
}

let fatal
try {
  await server.listen(); port=server.httpServer.address().port
  browser=await chromium.launch({channel:process.env.L12_BROWSER_CHANNEL||'msedge',headless:true})
  report.browser=browser.version()
  const onlyRed=process.argv.includes('--red')
  const profiles=process.argv.includes('--boundaries') ? [] : onlyRed ? [{name:'return-rested-red',seat:0}] : process.argv.includes('--focus') ? [{name:'opponent-fast',seat:1,animation:'fast'}] : [
    {name:'owner-standard',seat:0}, {name:'opponent-fast',seat:1,animation:'fast'},
    {name:'spectator-off',seat:1,spectator:true,animation:'off'},
    {name:'portrait-owner',seat:0,portrait:true,animation:'fast'},
    {name:'portrait-spectator',seat:1,portrait:true,spectator:true,animation:'fast'},
  ]
  for(const profile of profiles) {
    const {context,page}=await open(profile)
    try {
      const result=await sampleAction(page,profile.seat,'return')
      report.scenarios.push({name:profile.name,...result})
      check(result.maxVisible===1,'one physical face per rendered frame; actual max '+result.maxVisible,profile.name)
      check(result.minVisible===1,'no blank physical handoff; actual min '+result.minVisible,profile.name)
      check(result.ghostFrames>0,'the real movement must actually play',profile.name)
      check(result.metadataStable,'persistent master metadata stays visible and upright',profile.name)
      check(result.movementKeys.length===1,'one movement per authority event',profile.name)
      if(!onlyRed) {
        const duplicate=await sampleAction(page,profile.seat,'duplicate',500)
        report.scenarios.push({name:profile.name+'-retransmission',...duplicate})
        check(duplicate.maxVisible===1&&duplicate.minVisible===1&&duplicate.movementKeys.length===0,
          'retransmitted events must not create a new physical owner',profile.name)
        const entry=await sampleAction(page,profile.seat,'entry')
        report.scenarios.push({name:profile.name+'-redeploy',...entry})
        check(entry.maxVisible===1&&entry.minVisible===1&&entry.movementKeys.length===1&&entry.ghostFrames>0,
          'a real later deployment preserves one visible physical owner and plays once',profile.name)
        check(entry.metadataStable,'deployment never clones or hides the persistent metadata',profile.name)
      }
      if(onlyRed || report.errors.length) await page.screenshot({path:path.join(output,profile.name+'-settled.png')})
    } finally { await context.close() }
  }
  if(!onlyRed) {
    for(const options of [{name:'same-snapshot-return-redeploy',action:'chain'},
      {name:'active-return',action:'activeReturn',rested:false},
      {name:'master-initial-entry',action:'entry',shape:'entry'},
      {name:'relic-legion-positive-control',action:'entry',shape:'relic'}]) {
      const {context,page}=await open(options)
      try {
        const result=await sampleAction(page,0,options.action,2000)
        report.scenarios.push({name:options.name,...result})
        check(result.maxVisible===1&&result.minVisible===1,'physical handoff has exactly one face in every frame',options.name)
        check(result.movementKeys.length===(options.action==='chain'?2:1)&&result.ghostFrames>0,'each real migration actually plays once',options.name)
        check(result.stateFrames===0,'a migration terminal orientation must not create an extra inferred state performance',options.name)
        check(result.metadataStable,'persistent metadata remains stable',options.name)
      }finally{await context.close()}
    }
    if(!process.argv.includes('--focus')&&!process.argv.includes('--boundaries')) {
      const {context,page}=await open({animation:'standard'})
      try {
        await page.evaluate(()=>window.__visualTransitionHarness.openPrompt())
        await page.locator('.l12-prompt-overlay').waitFor()
        const paused=await sampleAction(page,0,'return',600)
        report.scenarios.push({name:'prompt-defers-return',...paused})
        check(paused.movingFrames===0&&paused.maxVisible===1&&paused.minVisible===1,'paused movement keeps exactly one static owner without starting its flight','prompt')
        await page.evaluate(()=>window.__visualTransitionHarness.closePrompt())
        const resumed=await sampleAction(page,0,'none',1000)
        report.scenarios.push({name:'prompt-resumes-once',...resumed})
        check(resumed.ghostFrames>0&&resumed.movementKeys.length===1&&resumed.maxVisible===1&&resumed.minVisible===1,
          'unpaused return takes one physical owner and settles once','prompt')
      }finally{await context.close()}
      const recovery=await open()
      try {
        await recovery.page.evaluate(()=>window.__visualTransitionHarness.transformBeginRecovery())
        await recovery.page.waitForTimeout(80)
        const historical=await sampleAction(recovery.page,0,'return',400)
        await recovery.page.evaluate(()=>window.__visualTransitionHarness.transformFinishRecovery())
        const acknowledged=await sampleAction(recovery.page,0,'none',600)
        report.scenarios.push({name:'recovery-baseline',historical,acknowledged})
        check(historical.movementKeys.length===0&&acknowledged.movementKeys.length===0&&historical.maxVisible===1&&acknowledged.maxVisible===1,
          'historical authority recovery never replays the migration','recovery')
      }finally{await recovery.context.close()}
      const cancellation=await open()
      try {
        const interrupted=await sampleAction(cancellation.page,0,'cancel',1000)
        await cancellation.page.evaluate(()=>window.__visualTransitionHarness.closePrompt())
        const settled=await sampleAction(cancellation.page,0,'none',600)
        report.scenarios.push({name:'cancel-active-return',interrupted,settled})
        check(interrupted.ghostFrames>0&&interrupted.maxVisible===1&&interrupted.minVisible===1&&settled.ghostFrames===0&&settled.minVisible===1,
          'cancel releases the visible card exactly once and does not replay it','cancel')
      }finally{await cancellation.context.close()}
      const unmount=await open()
      try {
        const interrupted=await sampleAction(unmount.page,0,'unmount',500)
        const remaining=await unmount.page.locator('.l12-zone-flight-ghost,.l12-card-state-transition-ghost,.zone-card-movement').count()
        report.scenarios.push({name:'unmount-active-return',interrupted,remaining})
        check(interrupted.ghostFrames>0&&remaining===0,'unmount leaves no visual holder or ghost','unmount')
      }finally{await unmount.context.close()}
    }
  }
} catch(error) {fatal=error;report.errors.push(error.stack||String(error))}
finally {
  if(browser){await browser.close();report.cleanup.browserClosed=true}
  await server.close();report.cleanup.serverClosed=true
  report.sources.after=pins()
  check(JSON.stringify(report.sources.before)===JSON.stringify(report.sources.after),'source pins changed during run','pins')
  // This exact disposable cache root was guarded before creation; retain no new full output tree.
  if(fs.existsSync(viteCache)){fs.rmSync(viteCache,{recursive:true,force:true})}
  report.cleanup.cacheRemoved=!fs.existsSync(viteCache)
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(report,null,2))
}
if(fatal)throw fatal
assert.equal(report.errors.length,0,report.errors.join('\n'))
console.log(JSON.stringify({scenarios:report.scenarios.length,errors:report.errors.length}))
