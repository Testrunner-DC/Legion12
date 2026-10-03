import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const fixturePath = process.env.L12_REAL_ENGINE_FIXTURE
if (!fixturePath) throw new Error('L12_REAL_ENGINE_FIXTURE is required')
const defaultRunId = new Date().toISOString().replaceAll(':','-').replaceAll('.','-')
const output = process.env.L12_AUTHORITY_STATE_OUT
  || path.join('D:/GPT/Legion12/artifacts/battle-animation-recurrence-20261003', `authoritative-card-state-${defaultRunId}`)
const fixtureBytes = fs.readFileSync(fixturePath)
const fixtureSha256 = crypto.createHash('sha256').update(fixtureBytes).digest('hex')
const fixture = JSON.parse(fixtureBytes.toString('utf8'))
const inMemoryEntryCandidate = process.env.L12_IN_MEMORY_ENTRY_CANDIDATE === '1'
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite existing evidence directory: ${output}`)
fs.mkdirSync(output, { recursive:true })

const entry = `
import { createApp, reactive, ref } from 'vue'
import GameBoard from '/src/l12/game/GameBoard.vue'
import { useLandscapeViewport } from '/src/l12/mobileViewport'
import '/src/style.css'
import '/src/l12/mobileViewport.css'
import '/src/l12/motion.css'
const fixture = ${JSON.stringify(fixture)}
const params = new URLSearchParams(location.search)
const chain = fixture[params.get('chain')]
const game = reactive(structuredClone(chain[0].player))
useLandscapeViewport(ref(params.get('landscape') === '1'))
const creations = []
const animationStarts = []
const samples = []
new MutationObserver(records => {
  for (const record of records) for (const node of record.addedNodes) {
    if (!(node instanceof HTMLElement)) continue
    for (const element of [node, ...node.querySelectorAll('*')]) {
      if (!(element instanceof HTMLElement)) continue
      if (element.matches('.l12-card-state-transition-ghost')) creations.push({
        type:'state', key:element.dataset.visualTransitionKey,
        from:element.dataset.stateFrom, to:element.dataset.stateTo,
        kind:element.dataset.motionKind,
      })
      if (element.matches('.l12-zone-flight-ghost,.zone-card-movement')) creations.push({
        type:'movement', key:element.dataset.movementKey,
        instanceId:element.dataset.movementInstanceId,
        from:element.dataset.movementFrom, to:element.dataset.movementTo,
      })
      if (element.matches('.public-reveal-animation')) {
        const creation = {type:'reveal',kind:element.dataset.presentationKind,images:[]}
        creations.push(creation)
        queueMicrotask(()=>{creation.images=[...element.querySelectorAll('img')].map(image=>image.alt)})
      }
    }
  }
}).observe(document.documentElement,{subtree:true,childList:true})
document.addEventListener('animationstart', event => {
  const element = event.target
  if (!(element instanceof HTMLElement)) return
  animationStarts.push({
    animationName:event.animationName,
    instanceId:element.closest('[data-card-instance-id]')?.dataset.cardInstanceId ?? null,
  })
}, true)
const angle = element => {
  if (!(element instanceof HTMLElement)) return null
  const transform = getComputedStyle(element).transform
  if (!transform || transform === 'none') return 0
  const match = transform.match(/^matrix\\(([^)]+)\\)$/)
  if (!match) return null
  const [a,b] = match[1].split(',').map(Number)
  return (Math.atan2(b,a)*180/Math.PI+360)%360
}
const visible = element => {
  if (!(element instanceof HTMLElement)) return false
  const visual = element.matches('.zone-card-movement') ? element.querySelector('.moving-card') : element
  if (!(visual instanceof HTMLElement)) return false
  const style = getComputedStyle(visual)
  const rect = visual.getBoundingClientRect()
  return style.display !== 'none' && style.visibility !== 'hidden'
    && Number(style.opacity || 1) > 0 && rect.width > 0 && rect.height > 0
}
const sampler = setInterval(() => {
  const get = id => document.querySelector('[data-card-instance-id="'+id+'"]')
  const authority = {
    source:get('ankh-resolved-source') || get('ankh-negated-source') || get('ankh-cancel-source'),
    guard:get('ankh-resolved-guard') || get('ankh-negated-guard') || get('ankh-cancel-guard'),
    attacker:get('real-attacker'), defender:get('rested-defender'),
    galahad:get('real-galahad'),
  }
  const ghosts = [...document.querySelectorAll('.l12-card-state-transition-ghost')]
  const movementGhosts = [...document.querySelectorAll('.l12-zone-flight-ghost,.zone-card-movement')]
  const stateGhostStates = ghosts.map(element => ({
    key:element.dataset.visualTransitionKey,visible:visible(element),
    imageDecoded:Boolean(element.querySelector('img')?.complete && element.querySelector('img')?.naturalWidth),
  }))
  const movementGhostStates = movementGhosts.map(element => ({
    instanceId:element.dataset.movementInstanceId,
    visible:visible(element),
    imageDecoded:Boolean(element.querySelector('img')?.complete && element.querySelector('img')?.naturalWidth),
  }))
  const hidden = Object.values(authority).filter(element => element instanceof HTMLElement
    && getComputedStyle(element).visibility === 'hidden').map(element => element.dataset.cardInstanceId)
  samples.push({
    source:angle(authority.source), guard:angle(authority.guard),
    attacker:angle(authority.attacker), defender:angle(authority.defender),
    galahad:angle(authority.galahad),
    galahadVisible:visible(authority.galahad),
    galahadImageDecoded:Boolean(authority.galahad?.querySelector('img')?.complete
      && authority.galahad.querySelector('img')?.naturalWidth),
    ghosts:ghosts.map(angle), stateGhosts:stateGhostStates.length, movementGhosts:movementGhostStates.length,
    stateGhostStates,movementGhostStates,hidden,
    hiddenWithoutMatchingGhost:hidden.filter(instanceId =>
      !stateGhostStates.some(ghost => ghost.visible && ghost.imageDecoded && ghost.key?.includes(':'+instanceId+':'))
      && !movementGhostStates.some(ghost => ghost.visible && ghost.imageDecoded && ghost.instanceId === instanceId)),
  })
}, 16)
window.__authorityState = {
  length:chain.length,
  label:index=>chain[index].label,
  apply:index=>Object.assign(game,structuredClone(chain[index].player)),
  report:()=>({creations:structuredClone(creations),animationStarts:structuredClone(animationStarts),samples:structuredClone(samples)}),
  stop:()=>clearInterval(sampler),
}
createApp(GameBoard,{game,readOnly:true}).mount('#app')
`
const html = '<!doctype html><html><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,minimum-scale=1,user-scalable=no,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__authority_state.js"></script></body></html>'
const plugin = {
  name:'authoritative-card-state-verifier',
  enforce:'pre',
  transform(code,id) {
    if (!inMemoryEntryCandidate || !id.replaceAll('\\','/').endsWith('/src/l12/game/GameBoard.vue')) return
    const before = ':viewer-player-index="game.you" :paused="passivePresentationPaused" :playback-speed="replayPlaybackSpeed"'
    const after = ':viewer-player-index="game.you" :playback-speed="replayPlaybackSpeed"'
    if (!code.includes(before)) throw new Error('ZoneMovementPresentationLayer pause binding not found')
    return code.replace(before,after)
  },
  resolveId(id) { if (id === '/__authority_state.js') return id },
  load(id) { if (id === '/__authority_state.js') return entry },
  configureServer(server) {
    server.middlewares.use('/__authority-state',(_request,response)=>{
      response.setHeader('Content-Type','text/html; charset=utf-8')
      response.end(html)
    })
  },
}
const server = await createServer({root,cacheDir:path.join(output,'vite-cache'),plugins:[plugin],
  server:{host:'127.0.0.1',port:0},logLevel:'error'})
const profiles = [
  {name:'desktop',viewport:{width:1366,height:768},landscape:false,reduced:false},
  {name:'mobile-portrait-rotated',viewport:{width:390,height:844},landscape:true,mobile:true,reduced:false},
  {name:'desktop-reduced',viewport:{width:1366,height:768},landscape:false,reduced:true},
  {name:'mobile-portrait-rotated-reduced',viewport:{width:390,height:844},landscape:true,mobile:true,reduced:true},
]
const chains = ['readyResolved','readyNegated','readyCancelled','attack',
  ...(fixture.galahadEntry ? ['galahadEntry'] : [])]
const results = []
let browser
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({channel:'msedge',headless:true})
  for (const profile of profiles) for (const chain of chains) {
    const context = await browser.newContext({viewport:profile.viewport,isMobile:profile.mobile,hasTouch:profile.mobile})
    const page = await context.newPage()
    if (profile.reduced) await page.emulateMedia({reducedMotion:'reduce'})
    const errors = []
    page.on('pageerror',error=>errors.push(error.message))
    await page.goto(`http://127.0.0.1:${port}/__authority-state?chain=${chain}&landscape=${profile.landscape?'1':'0'}`,{waitUntil:'networkidle'})
    await page.waitForSelector('[data-l12-game-stage]')
    const length = await page.evaluate(()=>window.__authorityState.length)
    const steps = []
    for (let index=1;index<length;index++) {
      const before = await page.evaluate(()=>{
        const report=window.__authorityState.report()
        return {creations:report.creations.length,animations:report.animationStarts.length,samples:report.samples.length}
      })
      const label = await page.evaluate(index=>window.__authorityState.label(index),index)
      await page.evaluate(index=>window.__authorityState.apply(index),index)
      await page.waitForTimeout(profile.reduced ? 260 : 900)
      const current = await page.evaluate(()=>window.__authorityState.report())
      steps.push({
        index,label,
        newCreations:current.creations.slice(before.creations),
        newAnimationStarts:current.animationStarts.slice(before.animations),
        samples:current.samples.slice(before.samples),
      })
    }
    const report = await page.evaluate(()=>{window.__authorityState.stop();return window.__authorityState.report()})
    const screenshot = path.join(output,`${profile.name}-${chain}.png`)
    await page.screenshot({path:screenshot,fullPage:true})
    results.push({profile:profile.name,chain,reduced:profile.reduced,steps,report,errors,screenshot})
    await context.close()
  }

  const stateFacts = result => result.steps.flatMap(step => step.newCreations
    .filter(item => item.type === 'state').map(item => ({...item,label:step.label})))
  const cssStateStarts = result => result.steps.flatMap(step => step.newAnimationStarts)
    .filter(item => item.animationName === 'l12-rest-settle')
  const step = (result,label) => result.steps.find(item => item.label === label)
  const ghostAngles = value => value.samples.flatMap(sample => sample.ghosts).filter(angle => angle !== null)
  let assertions = 0
  const ok = (value,message) => { assert.ok(value,message); assertions += 1 }
  for (const result of results) {
    ok(result.errors.length === 0, `${result.profile}/${result.chain}: no page errors`)
    ok(cssStateStarts(result).length === 0, `${result.profile}/${result.chain}: CSS cannot own tapped state motion`)
    ok(result.steps.every(item => item.samples.every(sample => sample.hiddenWithoutMatchingGhost.length === 0)),
      `${result.profile}/${result.chain}: every hidden authority card has its own visible state or movement ghost`)
    const facts = stateFacts(result)
    if (result.chain === 'readyResolved') {
      ok(facts.length === 2, `${result.profile}: resolved ready has exactly cost-rest and resolved-ready motion`)
      ok(facts[0].label === 'cost-paid-first-response' && facts[0].from === 'active' && facts[0].to === 'rested',
        `${result.profile}: paid rest occurs at its authoritative revision`)
      ok(facts[1].label === 'authority-response-pass-2' && facts[1].from === 'rested' && facts[1].to === 'active',
        `${result.profile}: target ready waits for the resolved authority revision`)
      const restAngles = ghostAngles(step(result,'cost-paid-first-response'))
      const readyAngles = ghostAngles(step(result,'authority-response-pass-2'))
      ok(restAngles.some(angle => angle < 20) && restAngles.some(angle => angle > 45), `${result.profile}: rest ghost visibly turns toward rested`)
      ok(readyAngles.some(angle => angle > 70) && readyAngles.some(angle => angle < 45), `${result.profile}: ready ghost visibly turns toward active`)
    } else if (result.chain === 'readyNegated') {
      ok(facts.length === 1 && facts[0].label === 'cost-paid-first-response'
        && facts[0].from === 'active' && facts[0].to === 'rested', `${result.profile}: negation preserves only the paid source rest`)
      ok(step(result,'negated').samples.every(sample => sample.guard === null || Math.abs(sample.guard - 90) < 1),
        `${result.profile}: negated target never flashes active`)
    } else if (result.chain === 'readyCancelled') {
      ok(facts.length === 0, `${result.profile}: cancelled declaration has no state motion`)
      ok(result.steps.every(item => item.samples.every(sample => sample.guard === null || Math.abs(sample.guard - 90) < 1)),
        `${result.profile}: cancelled target remains rested`)
    } else if (result.chain === 'attack') {
      ok(facts.length === 1 && facts[0].label === 'attack-committed-rested' && facts[0].kind === 'attack-rest',
        `${result.profile}: attack commits exactly one combined attack-rest motion`)
      ok(result.steps.filter(item => item.label !== 'attack-committed-rested')
        .every(item => item.newCreations.every(fact => fact.type !== 'state')), `${result.profile}: retransmit/response close cannot replay attack rest`)
      const attackAngles = ghostAngles(step(result,'attack-committed-rested'))
      ok(attackAngles.some(angle => angle < 20) && attackAngles.some(angle => angle > 45), `${result.profile}: attacker visibly turns to rested at commit`)
      for (const label of ['attack-committed-rested','same-snapshot-retransmission','attack-response-pass-1']) {
        const current = step(result,label)
        if (current) ok(current.samples.every(sample => sample.defender === null || Math.abs(sample.defender - 90) < 1),
          `${result.profile}: rested defender stays rested during ${label}`)
      }
    } else if (result.chain === 'galahadEntry') {
      const movements = result.steps.flatMap(item => item.newCreations)
        .filter(item => item.type === 'movement' && item.instanceId === 'real-galahad')
      ok(movements.length === 1 && movements[0].from === 'hand' && movements[0].to === 'field',
        `${result.profile}: Galahad enters the field exactly once`)
      const entrySamples = step(result,'entry-committed-declaration').samples
      ok(entrySamples.some(sample => sample.movementGhostStates.some(ghost =>
        ghost.instanceId === 'real-galahad' && ghost.visible && ghost.imageDecoded)),
      `${result.profile}: Galahad entry has a visible decoded instance-matched movement ghost`)
      ok(facts.length === 1 && facts[0].label === 'entry-trial-cost-paid'
        && facts[0].from === 'active' && facts[0].to === 'rested',
        `${result.profile}: Galahad trial cost rests once at payment`)
      const galahadFullCardReveals = result.steps.flatMap(item => item.newCreations)
        .filter(item => item.type === 'reveal' && item.kind === 'full-card' && item.images.includes('加拉哈德'))
      ok(galahadFullCardReveals.length === 0, `${result.profile}: entry continuation never flashes a second Galahad card`)
      const retransmit = step(result,'entry-same-revision-retransmission')
      ok(retransmit.newCreations.every(item => item.type !== 'state'
        && !(item.type === 'movement' && item.instanceId === 'real-galahad')),
        `${result.profile}: same-revision entry retransmission creates no motion`)
      const finalSamples = retransmit.samples
      ok(finalSamples.some(sample => sample.galahadVisible && sample.galahadImageDecoded),
        `${result.profile}: Galahad authority card remains visible with decoded art after rune follow-up`)
    }
  }
  const reportPath = path.join(output,'report.json')
  fs.writeFileSync(reportPath,JSON.stringify({
    schema:1,fixturePath:path.resolve(fixturePath),fixtureSha256,sourceRoot:root,inMemoryEntryCandidate,
    sourceFingerprint:crypto.createHash('sha256').update([
      fs.readFileSync(path.join(root,'src/l12/game/GameBoard.vue')),
      fs.readFileSync(path.join(root,'src/l12/motion.css')),
      fs.readFileSync(path.join(root,'src/l12/game/CardStateTransitionLayer.vue')),
      fs.readFileSync(path.join(root,'src/l12/game/visualTransitionProjection.ts')),
    ].map(buffer=>buffer.toString('base64')).join('\n')).digest('hex'),
    assertions,results,
  },null,2))
  console.log(`Authoritative card state transitions passed: ${assertions} assertions across ${results.length} runs`)
  console.log(reportPath)
} catch (error) {
  fs.writeFileSync(path.join(output,'failure-report.json'),JSON.stringify({
    schema:1,status:'failed',fixturePath:path.resolve(fixturePath),fixtureSha256,sourceRoot:root,inMemoryEntryCandidate,
    error:error instanceof Error ? {name:error.name,message:error.message,stack:error.stack} : String(error),
    results,
  },null,2))
  throw error
} finally {
  await browser?.close()
  await server.close()
}
