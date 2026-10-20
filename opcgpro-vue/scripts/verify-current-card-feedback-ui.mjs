import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { spawn } from 'node:child_process'
import { createHash } from 'node:crypto'

// Entry wrapper must validate these physical D roots with L12GeneratedStorage.
const input = process.env.L12_FEEDBACK_FIXTURES
const out = process.env.L12_FEEDBACK_UI_OUT
assert(input && out && process.env.L12_VITE_CACHE_DIR, 'Explicit governed roots required')
assert(/^[DE]:[\\/]/i.test(fs.realpathSync(input)), 'Fixtures must not resolve to C')
fs.mkdirSync(out, { recursive: true })
assert(/^[DE]:[\\/]/i.test(fs.realpathSync(out)), 'Evidence must not resolve to C')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const port = Number(process.env.L12_FEEDBACK_PORT || 5197)
const base = `http://127.0.0.1:${port}`
const courtOnly = process.env.L12_COURT_MOVE_UI_ONLY === '1'
const report = { scope: 'Actual GamePage components, synthetic real-engine recipient snapshots; not live transport or physical devices', cases: [], pageErrors: [], externalRequestsBlocked: 0 }
if(courtOnly)report.scope='Actual GamePage native movement and payment consumers using pinned real-engine synthetic projections; captured net serialization and manually published next read model, NOT live WebSocket, ACK or end-to-end server linkage. Existing MengPo/Oiran 64 cases not rerun.'
if(courtOnly)report.sourceHashes=Object.fromEntries(['src/l12/GamePage.vue','src/l12/game/GameBoard.vue','src/l12/game/PlayerMat.vue','src/l12/net.ts','scripts/probe-battle-log-qianyang.ps1','scripts/verify-current-card-feedback-ui.mjs'].map(file=>[file,createHash('sha256').update(fs.readFileSync(path.resolve(import.meta.dirname,'..',file))).digest('hex')]))
const server = spawn(process.execPath, ['scripts/preview-l12-battle-layout.mjs', '--port', String(port)], { cwd: path.resolve(import.meta.dirname, '..'), env: process.env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true })
let serverLog = ''
server.stdout.on('data', data => { serverLog += data })
server.stderr.on('data', data => { serverLog += data })
let browser

async function nativeClick(locator, label) {
  await locator.waitFor({state:'visible'})
  await locator.scrollIntoViewIfNeeded()
  assert(await locator.evaluate(element=>{
    const r=element.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2
    const hit=document.elementFromPoint(x,y)
    return x>=0&&y>=0&&x<innerWidth&&y<innerHeight&&Boolean(hit&&element.contains(hit))
  }),`${label} center must be visible and unobstructed`)
  await locator.click()
}

async function reachableControl(locator) {
  for(let index=0;index<await locator.count();index++){
    const option=locator.nth(index)
    if(await option.evaluate(element=>{const r=element.getBoundingClientRect(),at=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);return r.width>0&&r.height>0&&Boolean(at&&element.contains(at))}))return option
  }
  return null
}

async function verifyCourtMovement() {
  const files=fs.readdirSync(input).filter(name=>/^court-move-[01]-(False|True)\.json$/.test(name)).sort()
  assert.equal(files.length,4,'Both actors and real payment-window V2 restore required')
  for(const [width,height] of [[1366,768],[844,390]]){
    const context=await browser.newContext({viewport:{width,height}})
    await context.route('**/*',route=>{
      if(route.request().url().startsWith(`${base}/`))return route.continue()
      report.externalRequestsBlocked++;return route.abort()
    })
    const page=await context.newPage();page.setDefaultTimeout(8000)
    page.on('pageerror',error=>report.pageErrors.push(error.message))
    try{
      for(const file of files){
        const raw=fs.readFileSync(path.join(input,file)),fixture=JSON.parse(raw)
        assert.equal(fixture.engineSha256,'A8756BC87B9E0238BAC64C41F94050EB5520B45789651F7B160E3A19592B24DA')
        assert.equal(fixture.initial.you,fixture.actor)
        const item={file,actor:fixture.actor,restored:fixture.restored,width,height,fixtureSha256:createHash('sha256').update(raw).digest('hex'),engineSha256:fixture.engineSha256,commit:fixture.commit,steps:[]}
        report.cases.push(item)
        try{
          await page.goto(`${base}/__l12_battle_preview__?field=empty${width<1000?'&mobile=1&canvas=1':''}`,{waitUntil:'domcontentloaded'})
          await page.waitForFunction(()=>Boolean(window.__l12State?.game))
          await page.evaluate(({snapshot,actor})=>{
            const state=window.__l12State
            Object.assign(state.game,snapshot)
            state.room.yourPlayerIndex=actor
            state.spectating=false;state.gmEnabled=false;state.pendingAction=false
            window.__sentCommands=[]
          },{snapshot:fixture.initial,actor:fixture.actor})
          const source=page.locator(`.formation-slot[data-tutorial-card-instance="${fixture.expected.sourceId}"]`)
          await nativeClick(source,'Court Magician source')
          await nativeClick(page.locator(`[data-tutorial-action="move"][data-tutorial-card-instance="${fixture.expected.sourceId}"]:visible`),'Move action')
          const legal=page.locator(`.formation-slot.available[data-tutorial-player-index="${fixture.actor}"]`)
          await page.waitForFunction(actor=>document.querySelectorAll(`.formation-slot.available[data-tutorial-player-index="${actor}"]`).length===2,fixture.actor)
          item.legalSlots=await legal.evaluateAll(nodes=>nodes.map(node=>node.dataset.tutorialRow+':'+node.dataset.tutorialSlot).sort())
          assert.deepEqual(item.legalSlots,[...fixture.expected.legalSlots].sort(),'Only the two legal empty adjacent destinations are offered')
          item.steps.push('native source and move; exact two visible legal destinations')
          await page.screenshot({path:path.join(out,`court-${fixture.actor}-${fixture.restored}-${width}-destinations.png`)})
          const destination=page.locator(`.formation-slot[data-tutorial-player-index="${fixture.actor}"][data-tutorial-row="${fixture.expected.row}"][data-tutorial-slot="${fixture.expected.slot}"]`)
          await nativeClick(destination,'Legal move destination')
          await page.waitForFunction(()=>window.__sentCommands.length===1)
          const move=await page.evaluate(()=>window.__sentCommands[0])
          assert.equal(move.type,'gameAction');assert.equal(move.command.type,'move');assert.equal(move.command.cardInstanceId,fixture.expected.sourceId)
          assert.equal(move.command.row,fixture.expected.row);assert.equal(move.command.slot,fixture.expected.slot);assert.equal(typeof move.requestId,'string');assert(move.requestId)
          item.move=move
          // This is an explicit local read-model delivery, not a network ACK.
          await page.evaluate(snapshot=>{Object.assign(window.__l12State.game,snapshot);window.__l12State.pendingAction=false},fixture.payment)
          const prompt=fixture.payment.prompts[0]
          assert.equal(prompt.kind,'resource-payment');assert.equal(prompt.validChoices.includes(fixture.expected.paymentId),true)
          await page.locator('[data-tutorial-action="confirm-payment"]:visible').waitFor()
          const paymentOptions=page.locator(`[data-tutorial-payment-resource="${fixture.expected.paymentId}"]:visible`)
          let paymentChoice=await reachableControl(paymentOptions)
          if(!paymentChoice&&width<1000){
            await nativeClick(page.locator(`[data-tutorial-anchor="morale"][data-tutorial-player-index="${fixture.actor}"]`).getByRole('button',{name:'打开士气选择',exact:true}),'Existing own mobile morale picker')
            await page.locator('.mobile-morale-overlay').waitFor({state:'visible'})
            paymentChoice=await reachableControl(paymentOptions)
            item.steps.push('native mobile morale picker opened')
          }
          assert(paymentChoice,'The exact legal payment resource must be natively reachable')
          await nativeClick(paymentChoice,'Exact payment resource')
          assert.equal(await page.evaluate(()=>window.__sentCommands.length),1,'Selection alone does not submit payment')
          await page.screenshot({path:path.join(out,`court-${fixture.actor}-${fixture.restored}-${width}-payment.png`)})
          const confirm=await reachableControl(page.locator('[data-tutorial-action="confirm-payment"]:visible'))
          assert(confirm,'Native payment confirmation must be reachable')
          await nativeClick(confirm,'Confirm payment')
          await page.waitForFunction(()=>window.__sentCommands.length===2)
          const payment=await page.evaluate(()=>window.__sentCommands[1])
          assert.equal(payment.type,'gameAction');assert.equal(payment.command.type,'resolvePrompt');assert.equal(payment.command.promptId,prompt.promptId)
          assert.deepEqual(payment.command.cardInstanceIds,[fixture.expected.paymentId])
          for(const field of ['createdRevision','controller','activationId','step','sourceInstanceId','sourceCardId'])assert.equal(payment.command[field],prompt[field],`Actual prompt binding: ${field}`)
          assert.equal(prompt.playerIndex,fixture.actor);assert.equal(typeof payment.requestId,'string');assert(payment.requestId)
          item.payment=payment
          await page.evaluate(snapshot=>{Object.assign(window.__l12State.game,snapshot);window.__l12State.pendingAction=false},fixture.completed)
          await page.waitForFunction(({actor,sourceId})=>window.__l12State.game.players[actor].field[0][1]?.instanceId===sourceId,{actor:fixture.actor,sourceId:fixture.expected.sourceId})
          assert.equal(await destination.getAttribute('data-tutorial-card-instance'),fixture.expected.sourceId)
          await page.waitForTimeout(650)
          assert.equal(await page.evaluate(()=>window.__sentCommands.length),2,'No duplicate move or payment submission')
          item.steps.push('exact move serialized; real payment/V2 projection consumed; exact payment serialized; real completed projection displayed')
          await page.screenshot({path:path.join(out,`court-${fixture.actor}-${fixture.restored}-${width}-completed.png`)})
          item.status='passed';console.log(`passed ${file} ${width}`)
        }catch(error){item.status='failed';item.error=String(error?.stack||error);await page.screenshot({path:path.join(out,`court-failure-${file}-${width}.png`)}).catch(()=>{});throw error}
      }
    }finally{await context.close()}
  }
  report.status=report.cases.length===8&&report.cases.every(item=>item.status==='passed')&&!report.pageErrors.length?'passed':'failed'
}

try {
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Error(`Preview unavailable: ${serverLog.slice(-2000)}`)), 45000)
    const ready = data => { if (String(data).includes('L12 battle layout preview:')) { clearTimeout(timer); server.stdout.off('data', ready); resolve() } }
    server.stdout.on('data', ready)
    server.once('exit', code => { clearTimeout(timer); reject(Error(`Preview exited ${code}`)) })
  })
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  if(courtOnly)await verifyCourtMovement()
  else{
  const files = fs.readdirSync(input).filter(name => /^(mengpo-selected-targets|oiran-log)-[01]-(False|True)\.json$/.test(name)).sort()
  assert.equal(files.length, 8, 'Both actors, real restore and live state required')
  for (const [width, height] of [[1366, 768], [844, 390]]) {
    const context = await browser.newContext({ viewport: { width, height } })
    await context.route('**/*', route => {
      if (route.request().url().startsWith(`${base}/`)) return route.continue()
      report.externalRequestsBlocked++
      return route.abort()
    })
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    page.on('pageerror', error => report.pageErrors.push(error.message))
    for (const file of files) {
      const raw = fs.readFileSync(path.join(input, file))
      const fixture = JSON.parse(raw)
      for (const view of ['owner', 'opponent', 'spectator', 'referee']) {
        const item = { file, view, width, height, fixtureSha256: createHash('sha256').update(raw).digest('hex'), engineSha256: fixture.engineSha256, commit: fixture.commit }
        try {
          const flags = `${width < 1000 ? '&mobile=1&canvas=1' : ''}${view === 'spectator' ? '&spectator=1' : view === 'referee' ? '&referee=1' : ''}`
          await page.goto(`${base}/__l12_battle_preview__?field=empty${flags}`, { waitUntil: 'domcontentloaded' })
          await page.waitForFunction(() => Boolean(window.__l12State?.game))
          await page.evaluate(({ snapshot, view }) => {
            const state = window.__l12State
            // Preserve the preview's shared object; its keepalive must not replace our input.
            Object.assign(state.game, snapshot)
            state.spectating = ['spectator', 'referee'].includes(view)
            state.observerView = view === 'referee' ? 'referee' : 'public'
          }, { snapshot: fixture.views[view], view })
          await page.waitForFunction(({ revision, you }) => window.__l12State.game.revision === revision && window.__l12State.game.you === you, fixture.views[view])
          if (fixture.scenario === 'mengpo-selected-targets' && fixture.views[view].prompts.some(prompt => prompt.kind === 'response')) {
            const target = page.locator(`.formation-slot[data-tutorial-card-instance="${fixture.expected.targetId}"]`)
            await target.waitFor()
            await page.waitForFunction(id => document.querySelector(`.formation-slot[data-tutorial-card-instance="${id}"]`)?.classList.contains('response-target'), fixture.expected.targetId)
            assert.equal(await page.locator('.formation-slot.response-target').count(), 1, 'Only the selected target is highlighted')
            const responseText = page.locator('.effect-decision-text')
            assert((await responseText.innerText()).includes(fixture.expected.targetName), 'Actual response dialog names the selected target')
            item.responseHighlight = true
          }
          const minimize = page.getByRole('button', { name: '最小化弹框', exact: true })
          if (await minimize.isVisible()) await minimize.click()
          if (width < 1000) await page.getByRole('button', { name: '对局记录', exact: true }).click()
          const log = page.locator(width < 1000 ? '.mobile-record-overlay .battle-event-log' : '.record-log .battle-event-log')
          await log.waitFor({ state: 'visible' })
          item.collapsedText = await log.innerText()
          for (const toggle of await log.locator('.action-toggle[aria-expanded="false"]').all()) await toggle.click()
          const text = await log.innerText()
          if (fixture.scenario === 'mengpo-selected-targets') {
            assert(text.includes('已选目标') && text.includes(fixture.expected.targetName), 'Named selected target is readable in the actual log')
          } else {
            assert(text.includes(fixture.expected.cardName) && text.includes('加入手牌'), 'Revealed/added identity is readable in the actual log')
          }
          item.expandedText = text
          const namedRow = log.locator('.battle-event').filter({ hasText: fixture.expected.targetName || fixture.expected.cardName }).last()
          await namedRow.scrollIntoViewIfNeeded()
          const rowBox = await namedRow.boundingBox()
          const listBox = await log.locator('.event-list').boundingBox()
          assert(rowBox && listBox && rowBox.y >= listBox.y - 1 && rowBox.y + rowBox.height <= listBox.y + listBox.height + 1, 'Named log row can be scrolled fully into the visible list')
          const hit = await namedRow.evaluate(element => {
            const box = element.getBoundingClientRect()
            const at = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)
            return Boolean(at && (element.contains(at) || at === element))
          })
          assert(hit, 'Named log row is not covered by another overlay')
          item.namedRowReadable = true
          item.eventRows = await log.locator('[data-event-sequence]').count()
          await page.evaluate(snapshot => Object.assign(window.__l12State.game, snapshot), fixture.views[view])
          await page.waitForTimeout(240)
          assert.equal(await log.locator('[data-event-sequence]').count(), item.eventRows, 'Repeated snapshot does not duplicate log rows')
          if (fixture.actor === 0 && !fixture.restored) await page.screenshot({ path: path.join(out, `${fixture.scenario}-${view}-${width}.png`) })
          item.status = 'passed'
        } catch (error) {
          item.status = 'failed'; item.error = error.message
          await page.screenshot({ path: path.join(out, `failure-${file}-${view}-${width}.png`) }).catch(() => {})
        }
        report.cases.push(item)
        console.log(`${item.status} ${file} ${view} ${width}`)
        if (item.status === 'failed') throw Error(`Stopped at first concrete UI failure: ${item.error}`)
      }
    }
    await context.close()
  }
  report.status = report.cases.length === 64 && report.cases.every(item => item.status === 'passed') && !report.pageErrors.length ? 'passed' : 'failed'
  }
} catch(error) {
  report.status='failed';report.error=String(error?.stack||error)
  throw error
} finally {
  if (browser) {await browser.close();report.browserClosed=!browser.isConnected()}
  server.kill()
  await new Promise(resolve => { if (server.exitCode !== null) resolve(); else { server.once('exit', resolve); setTimeout(resolve, 5000).unref() } })
  report.serverExited=server.exitCode!==null||server.signalCode!==null
  report.serverPid=server.pid;report.serverExitCode=server.exitCode;report.serverSignalCode=server.signalCode
  report.serverLog = serverLog
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
}
console.log(JSON.stringify({ status: report.status, cases: report.cases.length, failures: report.cases.filter(item => item.status !== 'passed').length, pageErrors: report.pageErrors }))
if (report.status !== 'passed') process.exitCode = 1
