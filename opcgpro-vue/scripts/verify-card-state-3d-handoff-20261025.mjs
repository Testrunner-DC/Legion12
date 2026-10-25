// Bounded, isolated acceptance of the real CardState layer and its original
// coordinator/viewport helpers. Synthetic public facts, not a game-rule replay.
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
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const repo = path.dirname(root)
const output = process.env.L12_CARD_STATE_3D_OUT
  || 'D:/GPT/Legion12/artifacts/card-state-3d-handoff-20261025/browser-r1'
const cache = process.env.L12_VITE_CACHE_DIR
  || 'D:/GPT/Legion12/cache/primary/card-state-3d-vite-20261025'
const pwsh = process.env.L12_PWSH
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
const quote = value => `'${String(value).replaceAll("'", "''")}'`
const placement = spawnSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  `$ErrorActionPreference='Stop'; . ${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}; `
  + [output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value => {
    if (!value) throw new Error('Initialize the governed build environment before this acceptance run')
    return `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'CardState isolated browser acceptance';`
  }).join(' ')], { encoding: 'utf8', timeout: 8000, windowsHide: true })
if (placement.status !== 0) throw new Error(`Generated storage guard failed: ${placement.stderr || placement.stdout}`)
if (fs.existsSync(output)) throw new Error(`Preserve existing evidence: ${output}`)
fs.mkdirSync(output, { recursive: true })
const pinnedFiles = [
  'src/l12/game/CardStateTransitionLayer.vue',
  'src/l12/game/presentationSequenceCoordinator.ts',
  'src/l12/game/authoritativeCardVisibility.ts',
  'src/l12/game/authoritativeCardVisualSnapshot.ts',
  'src/l12/game/visualTransitionProjection.ts',
  'src/l12/mobileViewport.ts', 'src/l12/mobileViewport.css', 'src/style.css',
  'scripts/test-card-state-3d-handoff-20261025.mjs',
  'scripts/verify-card-state-3d-handoff-20261025.mjs',
]
const pins = () => Object.fromEntries(pinnedFiles.map(file => [file,
  crypto.createHash('sha256').update(fs.readFileSync(path.join(root, file))).digest('hex')]))
const sourceBefore = pins()
assert.match(process.env.L12_CARD_STATE_PRODUCT_SHA || '', /^[a-f0-9]{64}$/, 'explicit tested product SHA required')
assert.equal(sourceBefore[pinnedFiles[0]], process.env.L12_CARD_STATE_PRODUCT_SHA)
assert.match(process.env.L12_CARD_STATE_TEST_SHA || '', /^[a-f0-9]{64}$/, 'explicit tested regression SHA required')
assert.equal(sourceBefore[pinnedFiles[8]], process.env.L12_CARD_STATE_TEST_SHA)

const art = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="140"><rect x="2" y="2" width="96" height="136" rx="5" fill="#163e4b" stroke="#e6d3a5" stroke-width="3"/><path d="M12 110L50 16L88 110Z" fill="#ce8f42"/><circle cx="50" cy="73" r="16" fill="#151f30"/></svg>')
const entry = `
import { createApp, h, nextTick, ref } from 'vue'
import CardStateTransitionLayer from '/src/l12/game/CardStateTransitionLayer.vue'
import { createPresentationSequenceCoordinator } from '/src/l12/game/presentationSequenceCoordinator'
import { audioPreferences } from '/src/l12/audioPreferences'
import { useLandscapeViewport, viewportLayoutRect } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
audioPreferences.animation = 'standard'
audioPreferences.mobileLayout = 'auto'
const players = ref([
  { playerIndex:0, field:[[{instanceId:'unit',tapped:false},null,null],[null,null,null]], master:{tapped:false} },
  { playerIndex:1, field:[[{instanceId:'target-unit',tapped:false},null,null],[null,null,null]], master:{tapped:false} },
])
const events = ref([]), revision = ref(1), paused = ref(false)
let sequence = 0
const coordinator = createPresentationSequenceCoordinator()
const card = () => players.value[0].field[0][0]
const authority = () => document.querySelector('[data-l12-game-stage] [data-card-instance-id="unit"]')
const holders = () => [...document.querySelectorAll('.l12-card-state-transition-ghost')]
const visible = element => {
  if (!(element instanceof HTMLElement)) return false
  const style = getComputedStyle(element), rect = element.getBoundingClientRect()
  return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0 && rect.width > 0 && rect.height > 0
}
const border = element => {
  const style = getComputedStyle(element)
  return { widths:[style.borderTopWidth,style.borderRightWidth,style.borderBottomWidth,style.borderLeftWidth],
    outline:style.outlineStyle, background:style.backgroundColor, shadow:style.boxShadow }
}
const angle = element => {
  const matrix = new DOMMatrixReadOnly(getComputedStyle(element).transform)
  return (Math.atan2(matrix.b, matrix.a) * 180 / Math.PI + 360) % 360
}
const seen = new WeakSet(), records = [], samples = []
let sampling = true
function sample() {
  const nodes = holders(), own = authority(), shown = nodes.filter(visible)
  samples.push({time:performance.now(), owners:shown.length + Number(visible(own)), holders:shown.length,
    authorityVisible:visible(own), authorityAngle:angle(own)})
  for (const node of nodes) for (const animation of node.getAnimations()) {
    if (seen.has(animation)) continue
    seen.add(animation)
    records.push({key:node.dataset.visualTransitionKey, from:node.dataset.stateFrom, to:node.dataset.stateTo,
      kind:node.dataset.motionKind, duration:animation.effect.getTiming().duration,
      frames:animation.effect.getKeyframes(), start:performance.now()})
  }
  if (sampling) requestAnimationFrame(sample)
}
function fact(fromTapped, toTapped) {
  return {sequence:++sequence, type:'cardStateTransition', playerCardStateTransition:{instanceId:'unit',fromTapped,toTapped}}
}
window.__cardState3d = {
  async fire(toTapped, attack = false, troops = 5000) {
    const fromTapped = card().tapped
    if (fromTapped === toTapped) throw new Error('Need a true authority transition')
    const fresh = attack ? [{sequence:++sequence,type:'attack',playerIndex:0,cards:[{instanceId:'unit',hidden:false,troops},{instanceId:'target-unit'}]}] : []
    fresh.push(fact(fromTapped,toTapped))
    events.value = [...events.value,...fresh]
    card().tapped = toTapped
    revision.value++
    await nextTick()
  },
  async rapid() {
    if (card().tapped) throw new Error('Rapid rest/ready requires active baseline')
    events.value = [...events.value,fact(false,true),fact(true,false)]
    revision.value++
    await nextTick()
  },
  async pause(value) { paused.value = value; await nextTick() },
  async repeat() {
    players.value = structuredClone(JSON.parse(JSON.stringify(players.value)))
    events.value = [...events.value]
    await nextTick()
  },
  read() {
    const own = authority(), rect = viewportLayoutRect(own)
    return {records,samples, holderCount:holders().length, angle:angle(own), visible:visible(own),
      authorityBorder:border(own), tapped:card().tapped, rotated:document.documentElement.dataset.l12Rotated,
      logicalRect:{left:rect.left,top:rect.top,width:rect.width,height:rect.height}}
  },
  middle() {
    const node = holders().find(visible), animation = node?.getAnimations()[0]
    if (!animation || animation.currentTime < 45 || animation.currentTime > 280) return null
    const clone = node.querySelector('[data-l12-card-state-animation-face="true"]')
    const style = getComputedStyle(node), canvas = clone.querySelector('canvas[data-l12-pixel-snapshot="true"]')
    const rect = viewportLayoutRect(node), a = authority(), center = viewportLayoutRect(a)
    const holderRect = node.getBoundingClientRect(), pixelsRect = canvas?.getBoundingClientRect()
    return {currentTime:animation.currentTime, matrix:style.transform, filter:style.filter,
      holder:border(node), clone:border(clone), after:getComputedStyle(clone,'::after').display,
      visibleOwners:holders().filter(visible).length + Number(visible(a)),
      decodedPixels:Boolean(canvas?.width && canvas?.height),
      imageBorderPixel:canvas ? [...canvas.getContext('2d').getImageData(3,70,1,1).data] : null,
      pixelCenterError:pixelsRect ? Math.hypot(pixelsRect.left + pixelsRect.width/2 - holderRect.left - holderRect.width/2,
        pixelsRect.top + pixelsRect.height/2 - holderRect.top - holderRect.height/2) : null,
      pixelsInsideCanvas:Boolean(pixelsRect && pixelsRect.left >= 0 && pixelsRect.top >= 0
        && pixelsRect.right <= innerWidth && pixelsRect.bottom <= innerHeight),
      from:node.dataset.stateFrom, to:node.dataset.stateTo, kind:node.dataset.motionKind,
      logicalRect:{left:rect.left,top:rect.top,width:rect.width,height:rect.height},
      authorityCenter:{x:center.left+center.width/2,y:center.top+center.height/2}}
  },
  pausedRead() {
    const node = holders().find(visible)
    return {visibleHolders:holders().filter(visible).length, authorityVisible:visible(authority()),
      animations:node?.getAnimations().length ?? 0, angle:node ? angle(node) : null,
      records:records.length}
  },
  stop() { sampling = false },
}
createApp({
  setup() {
    useLandscapeViewport(ref(true))
    return () => h('div',{class:'l12-landscape-surface'},[
      h('div',{class:'board-stage', 'data-l12-game-stage':'',
        style:{position:'relative',width:'100%',height:'100%',overflow:'visible'}},[
        h('div',{style:{position:'absolute',left:'180px',top:'125px',width:'100px',height:'140px'}},[
        h('button',{class:['card-tile','selected',{tapped:card().tapped}], 'data-card-instance-id':'unit',
          style:{width:'100px',height:'140px',padding:0,
            transform:card().tapped ? 'rotate(90deg)' : 'rotate(0deg)',transformOrigin:'center',transition:'none'}},
          [h('img',{src:${JSON.stringify(art)},alt:'合成公开卡面',style:{width:'100%',height:'100%',borderRadius:'5px',objectFit:'contain'}})])]),
        h('div',{style:{position:'absolute',left:'480px',top:'125px',width:'100px',height:'140px'}},[
        h('button',{class:'card-tile', 'data-card-instance-id':'target-unit',
          style:{width:'100px',height:'140px',padding:0}},
          [h('img',{src:${JSON.stringify(art)},alt:'合成公开目标',style:{width:'100%',height:'100%',objectFit:'contain'}})])]),
      ]),
      h(CardStateTransitionLayer,{players:players.value,events:events.value,matchId:'isolated-card-state-3d',
        revision:revision.value,paused:paused.value,sequenceCoordinator:coordinator}),
    ])
  },
}).mount('#app')
await nextTick()
await Promise.all([...document.querySelectorAll('img')].map(img=>img.decode()))
requestAnimationFrame(sample)
window.__cardState3dReady = true
`
const html = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><div id="l12-landscape-teleports"></div><script type="module" src="/__card_state_3d.js"></script></body></html>'
const plugin = {
  name:'isolated-card-state-3d-acceptance',
  resolveId(id) { if (id === '/__card_state_3d.js') return id },
  load(id) { if (id === '/__card_state_3d.js') return entry },
  configureServer(server) {
    server.middlewares.use('/__card-state-3d', (_request, response) => {
      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(html)
    })
  },
}
const profiles = [
  {name:'desktop-1366',viewport:{width:1366,height:768},rotated:false},
  {name:'portrait-390-rotated',viewport:{width:390,height:844},rotated:true},
]
const results = [], errors = [], loaded = new Set()
const started = Date.now()
let server, browser
const clearFrame = (value, label) => {
  assert(value.widths.every(width => Number.parseFloat(width) === 0), `${label}: extra border`)
  assert.equal(value.outline, 'none', `${label}: outline`)
  assert.equal(value.background, 'rgba(0, 0, 0, 0)', `${label}: backdrop`)
  assert.equal(value.shadow, 'none', `${label}: selection shadow`)
}
const validateEnd = (end, tapped) => {
  assert.equal(end.holderCount, 0)
  assert.equal(end.visible, true)
  assert.equal(end.tapped, tapped)
  assert(Math.abs(end.angle - (tapped ? 90 : 0)) < .01, 'exact terminal angle')
  assert(end.authorityBorder.widths.some(width => Number.parseFloat(width) > 0), 'real UI frame preserved')
  assert(end.samples.every(sample => sample.owners === 1), 'every sampled frame has exactly one visible card owner')
  assert(Math.abs(end.logicalRect.left - 180) < 1 && Math.abs(end.logicalRect.top - 125) < 1,
    'final logical canvas position remains stable')
}
try {
  server = await createServer({root,configFile:false,cacheDir:cache,plugins:[vue(),plugin],
    resolve:{alias:{'@':path.join(root,'src')}},
    server:{host:'127.0.0.1',port:0,hmr:false,watch:null}, logLevel:'error'})
  await server.listen()
  const address = server.httpServer.address()
  browser = await chromium.launch({channel:process.env.L12_BROWSER_CHANNEL || 'msedge',headless:true,timeout:8000,downloadsPath:path.join(cache,'downloads')})
  for (const profile of profiles) {
    const context = await browser.newContext({viewport:profile.viewport,deviceScaleFactor:1,
      isMobile:profile.rotated,hasTouch:profile.rotated,reducedMotion:'reduce'})
    const page = await context.newPage()
    page.setDefaultTimeout(3500)
    page.on('pageerror', error=>errors.push(String(error)))
    await page.route('**/*', route => {
      const url = route.request().url()
      if (url.startsWith('http://127.0.0.1:') || url.startsWith('data:')) {
        loaded.add(url.split('?')[0]); return route.continue()
      }
      return route.abort()
    })
    await page.goto(`http://127.0.0.1:${address.port}/__card-state-3d`,{waitUntil:'networkidle',timeout:10000})
    await page.waitForFunction(()=>window.__cardState3dReady)
    const initial = await page.evaluate(()=>window.__cardState3d.read())
    assert.equal(initial.rotated, String(profile.rotated))
    validateEnd(initial, false)
    const rows = []
    async function finish(to, minCount) {
      await page.waitForFunction(count=>window.__cardState3d.read().records.length >= count
        && window.__cardState3d.read().holderCount === 0, minCount)
      const end = await page.evaluate(()=>window.__cardState3d.read())
      validateEnd(end, to)
      assert.equal(end.records.length, minCount, 'no repeated state animation')
      return end
    }
    async function transition(name, to, attack = false, troops = 5000) {
      const before = await page.evaluate(()=>window.__cardState3d.read().records.length)
      await page.evaluate(({to,attack,troops})=>window.__cardState3d.fire(to,attack,troops), {to,attack,troops})
      await page.waitForFunction(()=>Boolean(window.__cardState3d.middle()))
      const middle = await page.evaluate(()=>window.__cardState3d.middle())
      assert(middle, 'actual middle frame available')
      assert(middle.matrix.startsWith('matrix3d('), 'actual computed 3D middle pose')
      assert.equal(middle.visibleOwners, 1)
      assert.equal(middle.decodedPixels, true)
      assert(middle.imageBorderPixel[3] > 0, 'printed image border retained in pixel snapshot')
      assert(middle.pixelCenterError < 2, 'rendered snapshot stays centered on its actual holder')
      assert.equal(middle.pixelsInsideCanvas,true, 'rendered card pixels stay inside the physical canvas')
      clearFrame(middle.holder,'holder'); clearFrame(middle.clone,'clone')
      assert.equal(middle.after,'none')
      assert(middle.filter.includes('drop-shadow('), 'spatial drop shadow retained')
      await page.screenshot({path:path.join(output,`${profile.name}-${name}.png`)})
      const end = await finish(to,before+1), record = end.records.at(-1)
      assert.equal(record.duration,380, 'original duration; OS reduced motion does not override explicit game preference')
      assert(record.frames.some(frame=>/perspective\(/.test(frame.transform) && /rotateX\(/.test(frame.transform)))
      if (attack) {
        assert.equal(record.kind,'attack-rest')
        assert(record.frames.some(frame=>frame.offset === .38) && record.frames.some(frame=>frame.offset === .56))
      }
      rows.push({name,middle,record,terminal:{angle:end.angle,logicalRect:end.logicalRect}})
    }
    await transition('rest',true)
    await transition('ready',false)
    await transition('attack-rest',true,true)
    await transition('ready-before-pause',false)
    const beforePause = await page.evaluate(()=>window.__cardState3d.read().records.length)
    await page.evaluate(async()=>{await window.__cardState3d.pause(true);await window.__cardState3d.fire(true)})
    await page.waitForTimeout(110)
    const held = await page.evaluate(()=>window.__cardState3d.pausedRead())
    assert.equal(held.visibleHolders,1); assert.equal(held.authorityVisible,false)
    assert.equal(held.animations,0); assert.equal(held.angle,0); assert.equal(held.records,beforePause)
    await page.screenshot({path:path.join(output,`${profile.name}-paused.png`)})
    await page.evaluate(()=>window.__cardState3d.pause(false))
    const pausedEnd = await finish(true,beforePause+1)
    await page.evaluate(async()=>{await window.__cardState3d.pause(true);await window.__cardState3d.pause(false);await window.__cardState3d.repeat()})
    await page.waitForTimeout(100)
    assert.equal(await page.evaluate(()=>window.__cardState3d.read().records.length),beforePause+1)
    rows.push({name:'paused-static-unpause-once-no-replay',held,terminalAngle:pausedEnd.angle})
    await transition('ready-before-rapid',false)
    const beforeRapid = await page.evaluate(()=>window.__cardState3d.read().records.length)
    await page.evaluate(()=>window.__cardState3d.rapid())
    const rapidEnd = await finish(false,beforeRapid+2)
    const pair = rapidEnd.records.slice(-2)
    assert.deepEqual(pair.map(item=>[item.from,item.to]),[['active','rested'],['rested','active']])
    await page.evaluate(()=>window.__cardState3d.repeat())
    await page.waitForTimeout(100)
    assert.equal(await page.evaluate(()=>window.__cardState3d.read().records.length),beforeRapid+2)
    rows.push({name:'true-rest-ready-two-facts-single-owner',pair,terminalAngle:rapidEnd.angle})
    await transition('attack-1000',true,true,1000)
    const weak=rows.at(-1).record.frames.find(frame=>frame.offset===.38)
    await transition('ready-before-heavy-attack',false)
    await transition('attack-10000',true,true,10000)
    const strong=rows.at(-1).record.frames.find(frame=>frame.offset===.38)
    const displacement=frame=>frame.transform.match(/translate3d\(([-.\d]+)px,\s*([-.\d]+)px,\s*([-.\d]+)px\)/).slice(1).map(Number)
    const low=displacement(weak),high=displacement(strong)
    assert(Math.hypot(high[0],high[1])>Math.hypot(low[0],low[1])&&high[2]>low[2],
      'higher public attack-time troops actually increase forward/depth motion')
    rows.push({name:'public-attack-strength-no-extra-time-or-card',low,high,passed:true})
    await page.evaluate(()=>window.__cardState3d.stop())
    const all = await page.evaluate(()=>window.__cardState3d.read())
    results.push({profile,passed:true,rows,samples:all.samples,animations:all.records.length})
    await context.close()
  }
  assert.equal(errors.length,0,errors.join('\n'))
  assert(![...loaded].some(url=>/\/(GameBoard|PromptOverlay)\.vue/.test(url)), 'no other writer component imported')
} catch (error) {
  errors.push(error.stack || String(error))
  process.exitCode = 1
} finally {
  await browser?.close()
  await server?.close()
  const sourceAfter = pins()
  if (JSON.stringify(sourceBefore) !== JSON.stringify(sourceAfter)) {
    errors.push('Source pins changed during acceptance'); process.exitCode = 1
  }
  const report = {suite:'real CardState layer isolated browser 3D/ownership handoff',
    scope:'Synthetic public authority facts + real CardState/coordinator/viewport, not full GameBoard/engine/replay/physical phone',
    output,cache,temp:process.env.TEMP,elapsedMs:Date.now()-started,
    sourceBefore,sourceAfter,results,errors,loaded:[...loaded],passed:results.length===2 && errors.length===0}
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2))
  console.log(JSON.stringify({passed:report.passed,profiles:results.length,errors,elapsedMs:report.elapsedMs,output},null,2))
}
