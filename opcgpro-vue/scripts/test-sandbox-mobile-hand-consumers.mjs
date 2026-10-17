import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const out = process.env.L12_QA_OUT
const attackOnly = process.argv.includes('--attack-only')
if (!out || !process.env.L12_VITE_CACHE_DIR) throw new Error('EXPLICIT_GOVERNED_OUTPUT_REQUIRED')
if (attackOnly) {
  const quote = value => `'${value.replaceAll("'", "''")}'`
  execFileSync('pwsh', ['-NoProfile', '-Command', `. ${quote(path.join(root, '../ops/windows/L12GeneratedStorage.ps1'))}; `
    + [out, process.env.L12_VITE_CACHE_DIR, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value => {
      assert.ok(value, 'Initialize-L12BuildEnvironment must set every browser temp root')
      return `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'sandbox dual attack QA'`
    }).join('; ')], { stdio: 'pipe' })
}
fs.mkdirSync(out, { recursive: true })
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const original = fs.readFileSync(path.join(root, 'scripts/verify-batch253-visual.mjs'), 'utf8')
const marker = 'const entry = `', begin = original.indexOf(marker) + marker.length
const end = original.indexOf('const isPicker=', begin)
assert.ok(begin >= marker.length && end > begin)
const entry = original.slice(begin, end) + `
import {ref} from 'vue'
import {useLandscapeViewport} from '/src/l12/mobileViewport.ts'
import '/src/l12/mobileViewport.css'
const scenario=params.get('case')||'gm'
l12State.gmEnabled=scenario==='gm';l12State.spectating=['observer','referee'].includes(scenario)
l12State.observerView=scenario==='referee'?'referee':'public'
l12State.game.activePlayer=1;l12State.game.recentEvents=[]
const cards=[0,1].map(i=>({...card(legions[i],'sandbox-actor'+i+'-hand'),cost:0,currentCost:0,tapped:false,hidden:false,summonRound:-1}))
for(const i of [0,1])Object.assign(l12State.game.players[i],{hand:[cards[i]],handCount:1,spendableResourceCount:2,field:[[card(legions[2],'actor'+i+'-blocker'),null,null],[null,null,null]]})
if(scenario==='player')delete l12State.game.players[1].hand
if(scenario==='observer')l12State.game.players.forEach(p=>delete p.hand)
window.__commitPlay=()=>{const g=l12State.game,p=g.players[1];p.hand=[];p.handCount=0;p.field[1][0]=cards[1];g.revision=2;l12State.pendingAction=false}
window.__commitMove=()=>{const g=l12State.game,p=g.players[1];p.field[1][0]=null;p.field[1][1]=cards[1];g.revision=3;g.prompts=[{promptId:'actor0-response',playerIndex:0,kind:'option',text:'响应确认',validChoices:['yes','no'],minChoose:1,maxChoose:1,data:{uiPattern:'effect-decision',effectText:'由另一位玩家响应。'},choiceLabels:{yes:'发动',no:'不发动'},createdRevision:3,controller:0}];l12State.pendingAction=false}
window.__afterPrompt=()=>{l12State.game.prompts=[];l12State.game.activePlayer=0;l12State.game.revision=4;l12State.pendingAction=false}
if(params.has('attack-only')){
 const actor=Number(params.get('actor'))
 if(![0,1].includes(actor)||scenario!=='gm')throw new Error('Invalid dual-control attack fixture')
 l12State.gmEnabled=true;l12State.spectating=false;l12State.pendingAction=false
 l12State.room.sandbox=true;l12State.game.disasterMode='custom'
 l12State.game.phase='Main';l12State.game.activePlayer=actor;l12State.game.prompts=[]
 for(const i of [0,1])l12State.game.players[i].field=[[{...battleCard(battleArtwork[i],'actor'+i+'-attacker'),tapped:false,summonRound:-1},null,null],[null,null,null]]
 const source='actor'+actor+'-attacker',target='actor'+(1-actor)+'-attacker'
 l12State.game.legalAttackTargets={[source]:[target]}
 window.__attackFixtureFacts={actor,gmEnabled:l12State.gmEnabled,sandbox:l12State.room.sandbox,disasterMode:l12State.game.disasterMode,activePlayer:l12State.game.activePlayer,observer:l12State.game.you,source,target,legalAttackTargets:l12State.game.legalAttackTargets,sourceCard:l12State.game.players[actor].field[0][0],targetCard:l12State.game.players[1-actor].field[0][0]}
}
const app=createApp({setup(){useLandscapeViewport(ref(true));return()=>h(GamePage)}})
app.use(createRouter({history:createMemoryHistory(),routes:[]}));app.mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: process.env.L12_VITE_CACHE_DIR,
  logLevel: 'error', server: { host: '127.0.0.1', port: 0 }, plugins: [{
    name: 'sandbox-mobile-hand-consumers',
    resolveId(id) { if (id === '/__sandbox_mobile.js') return id },
    load(id) { if (id === '/__sandbox_mobile.js') return entry },
    configureServer(vite) { vite.middlewares.use((req, res, next) => {
      if (!req.url?.startsWith('/__sandbox_mobile__')) return next()
      res.setHeader('Content-Type', 'text/html; charset=utf-8')
      res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="l12-landscape-teleports"></div><div id="app" class="l12-landscape-surface"></div><script type="module" src="/__sandbox_mobile.js"></script>')
    }) },
  }] })
let browser, page
const cases = [], errors = []
const stable = envelope => { const { requestId, ...body } = envelope; return body }
async function verifyDualAttack(url, tap) {
  const sha256 = filename => createHash('sha256').update(fs.readFileSync(filename)).digest('hex').toUpperCase()
  const artwork = Object.fromEntries(['S02-06M2', 'ST02-M1', 'ST04-M1'].map(id => [id, `/assets/l12/special/master/${id}.png`]))
  const manifest = { schemaVersion: 3, catalogVersion: 'sandbox-dual-attack-local-art', assetVersion: 'sandbox-dual-attack-local-art', basePath: '/',
    cards: Object.fromEntries(Object.entries(artwork).map(([cardId, image]) => [cardId, { cardId,
      contentHash: sha256(path.join(root, 'public', image)), width: 720, height: 720, orientation: 'portrait',
      variants: { thumbWebp: image, boardWebp: image, detailWebp: image } }])) }
  const settings = { displaySeconds: 16, lobbyDelaySeconds: 0, intervalSeconds: 60, winStreakThreshold: 5,
    streakEndedThreshold: 5, minimumTierIndex: 0, winStreakEnabled: false, streakEndedEnabled: false,
    highestTierEnabled: false, factionTitleEnabled: false, masterTitleEnabled: false }
  const attackCases = []
  for (const actor of [0, 1]) {
    const context = await browser.newContext({ viewport: { width: 844, height: 390 }, hasTouch: true })
    page = await context.newPage()
    page.setDefaultTimeout(9000)
    const item = { actor, viewport: { width: 844, height: 390 }, input: 'browser touchscreen taps at verified owned physical centres',
      errors: [], failedRequests: [], httpErrors: [], externalRequests: [], taps: [], screenshots: [] }
    page.on('pageerror', error => item.errors.push(`pageerror: ${error.message}`))
    page.on('console', message => { if (message.type() === 'error') item.errors.push(`console: ${message.text()}`) })
    page.on('requestfailed', request => item.failedRequests.push({ url: request.url(), error: request.failure()?.errorText }))
    page.on('response', response => { if (response.status() >= 400) item.httpErrors.push({ url: response.url(), status: response.status() }) })
    await page.route('**/*', route => {
      if (new URL(route.request().url()).hostname === '127.0.0.1') return route.continue()
      item.externalRequests.push(route.request().url()); return route.abort()
    })
    await page.route('**/card-assets/card-assets.manifest.json*', route => route.fulfill({ json: manifest }))
    await page.route('**/api/ranked/broadcasts/settings', route => route.fulfill({ json: settings }))
    await page.addInitScript(() => {
      window.__attackTouchEvents = []
      for (const type of ['touchstart', 'pointerdown', 'click']) document.addEventListener(type, event => {
        if (!(event.target instanceof Element)) return
        const hit = event.target.closest('.formation-slot,.card-context-actions button')
        if (hit) window.__attackTouchEvents.push({ type, trusted: event.isTrusted, pointerType: event.pointerType ?? null,
          instanceId: hit.querySelector('[data-card-instance-id]')?.getAttribute('data-card-instance-id') ?? null,
          label: hit instanceof HTMLButtonElement ? hit.textContent.trim() : null })
      }, true)
    })
    try {
      await page.goto(`${url}?case=gm&attack-only=1&actor=${actor}`, { waitUntil: 'networkidle' })
      await page.locator('.board-viewport[data-l12-battle-layout="mobile"]').waitFor()
      item.fixture = await page.evaluate(() => window.__attackFixtureFacts)
      assert.equal(item.fixture.gmEnabled, true)
      assert.equal(item.fixture.sandbox, true)
      assert.equal(item.fixture.disasterMode, 'custom')
      assert.equal(item.fixture.actor, actor)
      assert.equal(item.fixture.activePlayer, actor)
      assert.equal(item.fixture.observer, 0, 'both actors must retain the same observing seat')
      assert.deepEqual(item.fixture.legalAttackTargets, { [item.fixture.source]: [item.fixture.target] })
      assert.equal(await page.locator('.gm-panel').count(), 0, 'GM panel must be collapsed by default without test input')
      const sourceSide = actor === 0 ? '.side-my' : '.side-opponent'
      const targetSide = actor === 0 ? '.side-opponent' : '.side-my'
      const source = page.locator(`.l12-player-mat${sourceSide} [data-card-instance-id="${item.fixture.source}"]`)
      item.taps.push({ step: 'select-controlled-legion', ...await tap(source, `actor${actor} source`) })
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'selecting a source must not submit')
      const attack = page.locator('.card-context-actions.field-actions').getByRole('button', { name: '进攻', exact: true })
      assert.equal(await attack.isEnabled(), true)
      item.taps.push({ step: 'arm-attack', ...await tap(attack, `actor${actor} attack`) })
      assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'arming attack must wait for a target')
      const target = page.locator(`.l12-player-mat${targetSide} .formation-slot.targetable`)
        .filter({ has: page.locator(`[data-card-instance-id="${item.fixture.target}"]`) })
      assert.equal(await target.count(), 1, 'exactly the projected opposing legion must be a legal target')
      item.screenshots.push(`actor${actor}-armed-844x390.png`)
      await page.screenshot({ path: path.join(out, item.screenshots.at(-1)) })
      item.taps.push({ step: 'select-legal-target', ...await tap(target, `actor${actor} target`) })
      await page.waitForTimeout(650)
      item.commands = await page.evaluate(() => window.__sentCommands)
      assert.equal(item.commands.length, 1, 'target selection must submit exactly one command, with no delayed duplicate')
      assert.ok(typeof item.commands[0].requestId === 'string' && item.commands[0].requestId.length > 0)
      assert.deepEqual(stable(item.commands[0]), { type: 'sandboxAction', actingPlayerIndex: actor,
        command: { type: 'attack', cardInstanceId: item.fixture.source, target: { type: 'legion', instanceId: item.fixture.target } } })
      item.touchEvents = await page.evaluate(() => window.__attackTouchEvents)
      const expectedTouches = [
        { instanceId: item.fixture.source, label: null }, { instanceId: null, label: '进攻' },
        { instanceId: item.fixture.target, label: null },
      ]
      assert.deepEqual(item.touchEvents.filter(event => event.type === 'touchstart').map(({ instanceId, label, trusted }) => ({ instanceId, label, trusted })),
        expectedTouches.map(event => ({ ...event, trusted: true })), 'all three steps must be trusted browser touch input on the exact source/button/target')
      assert.ok(item.touchEvents.filter(event => event.type === 'pointerdown').every(event => event.trusted && event.pointerType === 'touch'))
      assert.equal(await page.locator('.gm-panel').count(), 0, 'attack chain must not open the GM panel')
      item.screenshots.push(`actor${actor}-submitted-844x390.png`)
      await page.screenshot({ path: path.join(out, item.screenshots.at(-1)) })
      assert.deepEqual(item.errors, [])
      assert.deepEqual(item.failedRequests, [])
      assert.deepEqual(item.httpErrors, [])
      assert.deepEqual(item.externalRequests, [])
      item.passed = true
    } catch (error) {
      item.passed = false; item.error = error.stack || String(error)
      item.commandsAtFailure = await page.evaluate(() => window.__sentCommands)
      item.touchEventsAtFailure = await page.evaluate(() => window.__attackTouchEvents)
      item.screenshots.push(`actor${actor}-failure.png`)
      await page.screenshot({ path: path.join(out, item.screenshots.at(-1)) })
    } finally { attackCases.push(item); await context.close() }
  }
  const report = { schemaVersion: 1, status: attackCases.every(item => item.passed) ? 'passed' : 'failed',
    mode: 'attack-only', generatedAt: new Date().toISOString(), url,
    source: { head: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(),
      scriptSha256: sha256(fileURLToPath(import.meta.url)), gameBoardSha256: sha256(path.join(root, 'src/l12/game/GameBoard.vue')),
      netSha256: sha256(path.join(root, 'src/l12/net.ts')), gmPanelSha256: sha256(path.join(root, 'src/l12/game/GMPanel.vue')) },
    runtime: { temp: process.env.TEMP, tmp: process.env.TMP, tmpdir: process.env.TMPDIR, viteCache: process.env.L12_VITE_CACHE_DIR },
    controlledArtwork: Object.entries(artwork).map(([cardId, url]) => ({ cardId, url, sha256: sha256(path.join(root, 'public', url)) })),
    localApiRoutes: { '/api/ranked/broadcasts/settings': settings }, cases: attackCases,
    limits: ['Current real SFCs and native browser touchscreen input at 844x390, not a physical tablet or the original unknown private match.',
      'Each actor has a fresh context and a sanitized GM/custom sandbox fixture; source and legal target projection are provided by the fixture, not an engine execution.',
      'Only a captured outbound sandboxAction is asserted. No real WebSocket ACK, combat acceptance, damage, or battle result is simulated or claimed.',
      'No fixture commit/ACK helpers are invoked. Default hand/control/recovery/privacy scenarios are preserved but not rerun in attack-only mode.',
      'Fixed exact accepted same-origin artwork/manifest is declared, with no generic replacement or external production/test access.'] }
  fs.writeFileSync(path.join(out, 'attack-receipt.json'), JSON.stringify(report, null, 2))
  console.log(`Sandbox dual attack only: ${attackCases.filter(item => item.passed).length}/${attackCases.length} passed`)
  if (report.status !== 'passed') process.exitCode = 1
}
try {
  await server.listen()
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  page = await browser.newPage({ hasTouch: true })
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
  const url = `http://127.0.0.1:${server.httpServer.address().port}/__sandbox_mobile__`
  const hand = player => page.locator(`.l12-hand[data-player-index="${player}"]`)
  const tap = async (locator, label) => {
    await locator.scrollIntoViewIfNeeded()
    const hit = await locator.evaluate(element => { const r = element.getBoundingClientRect(); return {
      x: r.x + r.width / 2, y: r.y + r.height / 2,
      owned: element.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)),
      rect: {x:r.x,y:r.y,width:r.width,height:r.height},
      hit: document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)?.outerHTML.slice(0, 400),
    } })
    assert.equal(hit.owned, true, label + ' must own its physical touch centre: ' + JSON.stringify(hit))
    await page.touchscreen.tap(hit.x, hit.y)
    return hit
  }
  if (attackOnly) await verifyDualAttack(url, tap)
  else {
  for (const [width, height] of [[844,390],[740,360],[390,844]]) {
    await page.setViewportSize({ width, height }); await page.goto(url)
    await page.locator('.board-viewport[data-l12-battle-layout="mobile"]').waitFor()
    assert.equal(await page.locator('.gm-panel').count(), 0)
    assert.equal(await hand(1).isVisible(), true)
    assert.equal(await hand(0).isVisible(), false)
    await tap(hand(1).locator('[data-card-instance-id="sandbox-actor1-hand"]'), 'actor1 hand')
    await tap(page.locator('.card-context-actions.hand-actions').getByRole('button', { name:'打出', exact:true }), 'actor1 play')
    const slots = page.locator('.l12-player-mat.side-opponent .formation-slot')
    assert.equal(await slots.count(), 6)
    await tap(slots.nth(0), 'actor1 legal slot')
    let sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[0]), {type:'sandboxAction',actingPlayerIndex:1,command:{type:'playCard',cardInstanceId:'sandbox-actor1-hand',row:1,slot:0,targetPlayerIndex:1}})
    await page.evaluate(() => window.__commitPlay())
    await tap(slots.nth(0).locator('[data-card-instance-id="sandbox-actor1-hand"]'), 'actor1 field card')
    await tap(page.locator('.card-context-actions.field-actions').getByRole('button', { name:'移动', exact:true }), 'actor1 move')
    await tap(slots.nth(1), 'actor1 neighbouring slot')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[1]), {type:'sandboxAction',actingPlayerIndex:1,command:{type:'move',cardInstanceId:'sandbox-actor1-hand',row:1,slot:1}})
    await page.evaluate(() => window.__commitMove())
    const prompt = page.locator('.l12-prompt-overlay .prompt-panel[role="dialog"]')
    await prompt.waitFor()
    assert.equal(await hand(0).isVisible(), true, 'a prompt switches control back to actor0')
    assert.equal(await hand(1).isVisible(), false)
    await page.screenshot({ path:path.join(out, `actor0-prompt-${width}x${height}.png`) })
    await tap(prompt.getByRole('button', { name:'发动', exact:true }), 'actor0 response')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.deepEqual(stable(sent[2]), {type:'sandboxAction',actingPlayerIndex:0,command:{cardInstanceIds:['yes'],type:'resolvePrompt',promptId:'actor0-response',createdRevision:3,controller:0}})
    await page.evaluate(() => window.__afterPrompt())
    await tap(hand(0).locator('[data-card-instance-id="sandbox-actor0-hand"]'), 'actor0 hand')
    await tap(page.locator('.card-context-actions.hand-actions').getByRole('button', { name:'打出', exact:true }), 'actor0 play')
    await tap(page.locator('.l12-player-mat.side-my .formation-slot').nth(4), 'actor0 legal slot')
    sent = await page.evaluate(() => window.__sentCommands)
    assert.equal(sent.length, 4)
    assert.deepEqual(stable(sent[3]), {type:'sandboxAction',actingPlayerIndex:0,command:{type:'playCard',cardInstanceId:'sandbox-actor0-hand',row:1,slot:1,targetPlayerIndex:0}})
    cases.push({width,height,scenario:'gm',nativeTouch:true,commands:sent.map(stable),passed:true})
  }
  await page.setViewportSize({ width:844, height:390 })
  for (const scenario of ['player','observer','referee']) {
    await page.goto(url+'?case='+scenario)
    await page.locator('.board-viewport').waitFor()
    assert.equal(await hand(0).isVisible(), true)
    assert.equal(await hand(1).isVisible(), scenario === 'referee')
    if (scenario === 'observer') assert.equal(await page.locator('.l12-hand [data-card-instance-id]').count(),0)
    if (scenario === 'player') assert.equal(await hand(1).locator('[data-card-instance-id]').count(),0)
    if (scenario === 'referee') assert.equal(await page.locator('.board-viewport.referee-both-hands').count(),1)
    assert.equal(await page.locator('.card-context-actions.hand-actions').count(),0)
    assert.equal((await page.evaluate(() => window.__sentCommands)).length,0)
    cases.push({scenario,ordinaryPrivacyRetained:true,passed:true})
  }
  await page.setViewportSize({ width:1366, height:768 }); await page.goto(url)
  await page.locator('.board-viewport[data-l12-battle-layout="desktop"]').waitFor()
  assert.equal(await hand(0).isVisible(), true); assert.equal(await hand(1).isVisible(), true)
  cases.push({scenario:'desktop-gm',bothHandsRetained:true,passed:true})
  assert.deepEqual(errors,[])
  fs.writeFileSync(path.join(out,'receipt.json'),JSON.stringify({status:'passed',cases,errors,
    scope:'Real current SFCs and native Edge touch with synthetic public state/socket acknowledgments; not engine execution, live a355, iOS, WeChat or the original private report'},null,2))
  console.log(`Sandbox mobile hand consumers: ${cases.length}/${cases.length} passed`)
  }
} catch(error) {
  await page?.screenshot({path:path.join(out,'failure.png')}).catch(()=>{})
  fs.writeFileSync(path.join(out,'failure.json'),JSON.stringify({cases,errors,error:String(error.stack)},null,2));throw error
} finally { await browser?.close(); await server.close() }
