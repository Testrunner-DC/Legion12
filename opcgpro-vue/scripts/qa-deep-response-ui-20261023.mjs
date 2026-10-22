import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import {createRequire} from 'node:module'
import {execFileSync} from 'node:child_process'

const [base, fixturePath, output, cache] = process.argv.slice(2)
assert.equal(process.argv.length, 6, 'Expected existing preview URL, engine fixture, output and cache roots')
const repo = path.resolve(import.meta.dirname, '../..')
const front = path.join(repo, 'opcgpro-vue')
const allowed = path.resolve(repo, '../../artifacts/bug-closure-continuation-20261011/next-authority-batch-20261016/closeout/intake-current-20261023-card-effect/deep-response-ui')
assert.equal(new URL(base).hostname, '127.0.0.1')
assert.ok([fixturePath, output, cache].every(value => path.isAbsolute(value)))
assert.ok(path.resolve(output).startsWith(allowed + path.sep), 'Output must remain in the approved evidence family')
const quote = value => value.replaceAll("'", "''")
execFileSync(process.env.L12_PWSH || 'pwsh', ['-NoProfile', '-NonInteractive', '-Command',
  `. '${quote(path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'))}'; foreach($p in @('${quote(output)}','${quote(cache)}')){$null=Assert-L12GeneratedPath -Path $p -Purpose 'deep response browser QA'}`], {stdio:'pipe'})
for (const root of [output, cache]) {
  fs.mkdirSync(root, {recursive:true})
  assert.equal(fs.readdirSync(root).length, 0, 'Refusing to overwrite an earlier run')
}
process.env.TEMP = path.join(cache, 'tmp')
process.env.TMP = process.env.TEMP
process.env.TMPDIR = process.env.TEMP
fs.mkdirSync(process.env.TEMP)
const require = createRequire(path.join(front, 'package.json'))
const ts = require('typescript')
const copyCode = ts.transpileModule(fs.readFileSync(path.join(front, 'src/l12/game/promptPlayerCopy.ts'), 'utf8'),
  {compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText
const copy = await import(`data:text/javascript;base64,${Buffer.from(copyCode).toString('base64')}`)
const {chromium} = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const fixture = JSON.parse(fs.readFileSync(fixturePath, 'utf8'))
assert.equal(fixture.status, 'ACTUAL_HANDLE_CHAIN_PASSED')
assert.equal(fixture.maximumDepth, 6)
assert.equal(fixture.restoredBeforeFive, true)
assert.equal(fixture.realConnectionTakeover, false)
assert.equal(fixture.frames.length, 10)
const onlySequence = process.env.L12_DEEP_UI_SEQUENCE ? Number(process.env.L12_DEEP_UI_SEQUENCE) : null
assert.ok(onlySequence === null || fixture.frames.some(frame => frame.sequence === onlySequence))
const frames = onlySequence === null ? fixture.frames : fixture.frames.filter(frame => frame.sequence === onlySequence)
const sourceFiles = ['src/l12/game/GameBoard.vue', 'src/l12/game/PromptOverlay.vue', 'src/l12/game/promptPlayerCopy.ts', 'scripts/qa-deep-response-ui-20261023.mjs']
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()
const sourceHashes = () => Object.fromEntries(sourceFiles.map(file => [file, hash(path.join(front, file))]))
const report = {
  schema:1, status:'RUNNING', engineFixtureSha256:hash(fixturePath), sourcesBefore:sourceHashes(),
  engineCommands:fixture.commandCount, maximumDepth:6, restoredBeforeFive:true, realConnectionTakeover:false,
  onlySequence, automaticScrolling:false,
  cases:[], pageErrors:[], externalRequests:[], mockedBroadcastSettingsReads:[], errors:[],
  boundary:'Actual engine recipient snapshots and previously accepted exact prompt commands drive the unchanged GamePage/GameBoard/PromptOverlay through the existing local battle preview. Native physical mouse/touch hit tests, not a live engine/socket ACK, new connection takeover, original player replay or real-device certification. Existing preview clears synthetic pendingAction; ranked timeout attribution is not tested. Artwork manifest and broadcast settings are controlled fixtures, not artwork/broadcast tests.',
}
const write = () => fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2))
const definitions = ['cards.s1.json','cards.s2.json','cards.st.json'].flatMap(name =>
  JSON.parse(fs.readFileSync(path.join(repo, '服务端WebSocket/TwelveLegions/Data', name))))
const art = '/assets/l12/special/master/S02-06M2.png'
const manifest = JSON.stringify({schemaVersion:3,catalogVersion:'deep-response-qa',assetVersion:'deep-response-qa',basePath:'/',
  cards:Object.fromEntries(definitions.map(card => [card.id,{cardId:card.id,contentHash:'qa-'+card.id,width:1240,height:1732,
    orientation:'portrait',variants:{thumbWebp:art,boardWebp:art,detailWebp:art}}]))})
let context, page

async function applySnapshot(snapshot, viewer) {
  await page.evaluate(({snapshot, viewer}) => {
    const state = window.__battleDockFixture.state
    Object.assign(state.game, snapshot)
    state.gmEnabled = false
    state.spectating = false
    state.status = 'online'
    state.room.yourPlayerIndex = viewer
    state.room.roomCode = snapshot.roomCode
    state.room.players = snapshot.players.map(player => ({playerIndex:player.playerIndex,name:player.name,
      connected:true,ready:true,deckIndex:0}))
    window.__resetSentCommands()
  }, {snapshot, viewer})
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
}

async function geometry() {
  return page.evaluate(() => {
    const box = node => {
      if (!node) return null
      const r = node.getBoundingClientRect()
      return {x:r.x,y:r.y,width:r.width,height:r.height,scrollHeight:node.scrollHeight,clientHeight:node.clientHeight}
    }
    return {width:innerWidth,height:innerHeight,pendingAction:window.__battleDockFixture.state.pendingAction,
      promptIds:window.__battleDockFixture.state.game.prompts.map(prompt => prompt.promptId),
      overlay:box(document.querySelector('.l12-prompt-overlay')),
      panel:box(document.querySelector('.prompt-choice-panel')),
      header:box(document.querySelector('.prompt-choice-panel>header')),
      body:box(document.querySelector('.prompt-choice-body')),
      footer:box(document.querySelector('.prompt-action-footer')),
      minimized:!!document.querySelector('.prompt-minimized-bar'),
      waiting:!!document.querySelector('.l12-prompt-overlay.waiting'),
      controls:[...document.querySelectorAll('.l12-prompt-overlay button')].map(node =>
        ({text:node.textContent.trim().slice(0,120),choice:node.dataset.tutorialChoice ?? null,
          disabled:node.disabled,box:box(node)}))}
  })
}

async function hit(locator, touch) {
  await locator.waitFor({state:'visible'})
  assert.equal(await locator.isEnabled(), true, 'Current authoritative choice must be enabled')
  const inspect = () => locator.evaluate(node => {
    const rect = element => {
      const r = element.getBoundingClientRect()
      return {x:r.x,y:r.y,width:r.width,height:r.height,scrollHeight:element.scrollHeight,
        clientHeight:element.clientHeight,scrollTop:element.scrollTop}
    }
    const r = node.getBoundingClientRect(), x=r.x+r.width/2, y=r.y+r.height/2
    const top = document.elementFromPoint(x,y), clipAncestors=[], scrollOwners=[]
    for(let parent=node.parentElement;parent;parent=parent.parentElement) {
      const style=getComputedStyle(parent), box=rect(parent)
      if (/(hidden|clip|auto|scroll)/.test(style.overflowX+' '+style.overflowY) || style.clipPath!=='none')
        clipAncestors.push({tag:parent.tagName,className:parent.className,overflowX:style.overflowX,
          overflowY:style.overflowY,clipPath:style.clipPath,box})
      if (/(auto|scroll)/.test(style.overflowY) && parent.clientHeight>8
        && parent.scrollHeight>parent.clientHeight+1) {
        const px=box.x+box.width/2, py=box.y+Math.min(box.height/2,Math.max(1,innerHeight-box.y-1))
        const owner=document.elementFromPoint(px,py)
        if(px>=0&&px<innerWidth&&py>=0&&py<innerHeight&&owner&&parent.contains(owner))
          scrollOwners.push({x:px,y:py,direction:y>box.y+box.height?1:-1,box})
      }
    }
    return {x,y,width:r.width,height:r.height,within:x>=0&&x<innerWidth&&y>=0&&y<innerHeight,
      owned:!!top&&node.contains(top),hitElement:top?{tag:top.tagName,className:top.className}:null,
      button:rect(node),clipAncestors,scrollOwners}
  })
  let point=await inspect()
  const attempt={touch,initial:point,scrollActions:[],automaticScrolling:false}
  report.lastHitAttempt=attempt
  write()
  for(let count=0;!(point.within&&point.owned)&&point.scrollOwners.length&&count<8;count++) {
    const owner=point.scrollOwners[0], delta=owner.direction*180
    // Real browser wheel input only. No scrollIntoView, forced click or DOM scroll assignment.
    await page.mouse.move(owner.x,owner.y)
    await page.mouse.wheel(0,delta)
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
    point=await inspect()
    attempt.scrollActions.push({kind:'native-mouse-wheel',x:owner.x,y:owner.y,deltaY:delta,after:point})
    write()
  }
  attempt.final=point
  write()
  assert.ok(point.within && point.owned, 'Actual control centre must be reachable: '+JSON.stringify(point))
  if (touch) await page.touchscreen.tap(point.x, point.y)
  else await page.mouse.click(point.x, point.y)
  return {...point, initial:attempt.initial, scrollActions:attempt.scrollActions, automaticScrolling:false}
}

try {
  context = await chromium.launchPersistentContext(path.join(cache, 'profile'), {
    channel:'msedge', headless:true, hasTouch:true, viewport:{width:1440,height:900},
  })
  page = await context.newPage()
  page.setDefaultTimeout(8000)
  page.on('pageerror', error => report.pageErrors.push(error.message))
  const origin = new URL(base).origin
  await page.route('**/*', route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin === origin && url.pathname === '/card-assets/card-assets.manifest.json')
      return route.fulfill({status:200,contentType:'application/json',body:manifest})
    if (url.origin === 'http://127.0.0.1:8080' && request.method() === 'GET'
      && !url.search && url.pathname === '/api/ranked/broadcasts/settings') {
      report.mockedBroadcastSettingsReads.push(url.pathname)
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify({displaySeconds:16,
        lobbyDelaySeconds:3,intervalSeconds:15,winStreakThreshold:5,streakEndedThreshold:5,minimumTierIndex:0,
        winStreakEnabled:true,streakEndedEnabled:true,highestTierEnabled:true,factionTitleEnabled:true,masterTitleEnabled:true})})
    }
    if (url.origin === origin || url.protocol === 'data:') return route.continue()
    report.externalRequests.push(url.origin+url.pathname)
    return route.abort()
  })

  for (const size of [{width:1440,height:900,mobile:false},{width:667,height:375,mobile:true}]) {
    await page.setViewportSize({width:size.width,height:size.height})
    await page.goto(base+'?canvas=1&morale=3&hand=1'+(size.mobile?'&mobile=1':''), {waitUntil:'domcontentloaded'})
    await page.waitForFunction(() => !!window.__battleDockFixture?.state?.game)
    for (const frame of frames) {
      for (const viewer of [1-frame.actor, frame.actor]) {
        const snapshot = frame.before[viewer]
        assert.equal(snapshot.you, viewer)
        assert.equal(snapshot.effectStack.length, frame.depth)
        await applySnapshot(snapshot, viewer)
        const sample = {size,sequence:frame.sequence,viewer,actor:frame.actor,depth:frame.depth,
          kind:frame.promptKind,restoredBeforeFive:frame.restoredBeforeFive,before:await geometry(),hits:[]}
        report.cases.push(sample)
        write()
        if (viewer !== frame.actor) {
          assert.equal(snapshot.prompts.length, 0, 'Opponent projection must not contain private actor choices')
          assert.equal(await page.locator('.l12-prompt-overlay [data-tutorial-choice]').count(), 0)
          sample.opponentPrivateChoicesAbsent = true
          continue
        }
        const prompt = snapshot.prompts.find(item => item.promptId === frame.promptId)
        assert.ok(prompt)
        const choice = frame.command.cardInstanceIds[0]
        assert.ok(prompt.validChoices.includes(choice))
        await page.locator(`[data-tutorial-prompt-id="${prompt.promptId}"]`).waitFor({state:'visible'})
        const expectedNarrative = copy.promptSituationCopy(prompt)
        assert.equal(await page.locator('.prompt-choice-panel>header .effect-decision-text').count(), 0)
        assert.equal(await page.locator('.prompt-choice-body>.prompt-situation').innerText(),
          expectedNarrative, 'The unchanged shared copy oracle preserves the full effect narrative and order')
        assert.ok(sample.before.body.height > 0, 'The user-scrollable choice body must retain positive height')
        if (prompt.kind === 'response') {
          assert.ok(prompt.validChoices.includes('pass'))
          assert.equal(await page.locator('[data-tutorial-choice="pass"]').count(), 1)
          sample.passExists = true
          sample.hits.push(await hit(page.locator('.prompt-choice-panel .prompt-minimize'), size.mobile))
          assert.equal(await page.locator('.prompt-minimized-bar button').count(), 1)
          sample.hits.push(await hit(page.locator('.prompt-minimized-bar button'), size.mobile))
          await page.locator(`[data-tutorial-prompt-id="${prompt.promptId}"] .prompt-choice-panel`).waitFor({state:'visible'})
          assert.equal(await page.locator('.prompt-choice-body>.prompt-situation').innerText(), expectedNarrative)
          assert.equal(await page.evaluate(() => window.__sentCommands.length), 0, 'Minimize/restore must not submit a game command')
          sample.minimizeRestore = true
        }
        if (frame.sequence === 12 || frame.sequence === 16 || frame.sequence === 18 || frame.sequence === 19 || frame.sequence === 20)
          await page.screenshot({path:path.join(output,`${size.width}x${size.height}-seq${frame.sequence}-depth${frame.depth}.png`)})
        if (prompt.kind === 'response-target') {
          const targets = prompt.validChoices.filter(id => id !== 'cancel')
          const index = targets.indexOf(choice)
          assert.ok(index >= 0)
          sample.hits.push(await hit(page.locator('.response-target-row').nth(index).getByRole('button'), size.mobile))
        } else sample.hits.push(await hit(page.locator(`[data-tutorial-choice="${choice}"]`), size.mobile))
        if (prompt.data?.choiceMode !== 'instant')
          sample.hits.push(await hit(page.locator('[data-tutorial-action="confirm-prompt"]'), size.mobile))
        const sent = await page.evaluate(() => window.__sentCommands.filter(item => item.type === 'gameAction'))
        assert.equal(sent.length, 1, 'One native operation must submit exactly one game command')
        const actual = sent[0].command
        for (const key of ['type','promptId','activationId','sourceInstanceId','sourceCardId','step','createdRevision','controller'])
          assert.equal(actual[key] ?? null, frame.command[key] ?? null, `Exact accepted command binding ${key}`)
        assert.deepEqual(actual.cardInstanceIds, frame.command.cardInstanceIds)
        sample.exactAcceptedCommand = actual
        sample.after = await geometry()
        sample.status = 'PASSED'
        if (frame.sequence === 20)
          await page.screenshot({path:path.join(output,`${size.width}x${size.height}-seq20-pass-reachable.png`)})
        write()
      }
    }
    for (const viewer of [0,1]) {
      await applySnapshot(fixture.final[viewer], viewer)
      assert.equal(fixture.final[viewer].effectStack.length, 0)
      assert.equal(await page.locator('.l12-prompt-overlay:visible').count(), 0)
    }
  }
  assert.deepEqual(report.pageErrors, [])
  assert.deepEqual(report.externalRequests, [])
  report.sourcesAfter = sourceHashes()
  assert.deepEqual(report.sourcesAfter, report.sourcesBefore)
  report.status = 'PASSED'
} catch (error) {
  report.status = 'FAILED'
  report.errors.push(error.stack ?? String(error))
  if (page) {
    try { report.failureGeometry = await geometry(); await page.screenshot({path:path.join(output,'failure.png')}) }
    catch (captureError) { report.errors.push('Capture: '+captureError.message) }
  }
  process.exitCode = 1
} finally {
  await context?.close()
  report.ownedBrowserClosed = true
  write()
}
console.log(JSON.stringify({status:report.status,cases:report.cases.length,output,
  errors:report.errors.map(error => error.split('\n')[0])}))
