import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'

// Native UI dispatch only. The existing preview records socket.send and resets
// pendingAction every 200ms; neither event is a server receipt or a settlement.
const repo = path.resolve(import.meta.dirname, '../..')
const front = path.join(repo, 'opcgpro-vue')
const project = path.resolve(repo, '../..')
const fixturePath = path.join(project, 'artifacts/bug-closure-continuation-20261011/next-authority-batch-20261016/closeout/intake-current-20261023-card-effect/deep-response-ui/net-r1/engine-fixture.json')
const fixturePin = '7D1F53F85A1EB262D5365A8CE27103A13C8C44290D6A9D6EDDE0A22CDB511FE7'
const pins = {
  'scripts/preview-l12-battle-layout.mjs': '488B580796B12F885041DCB79B410A23B094092639673FC6897CECCE64520EEE',
  'scripts/verify-batch253-visual.mjs': '24D1FD62D9B0293959490BAF71843DCDBF5627D79ED603159F14E14151387DF5',
  'src/l12/GamePage.vue': '6C8B3D29A9BB3A92C326A2A26BAD76C6292336300E2A0B4C7ADA8144A813F681',
  'src/l12/game/GameBoard.vue': '93EE8760835D2BF666FEA350D69FC7F4815F80B792EA9E325B21F1A213BF2BD1',
  'src/l12/game/PromptOverlay.vue': '416EDF615990055263E7090F59D622938A0804F1E1E447F11D99AFFFA90E4A90',
  'src/l12/game/GmPanel.vue': '99BCB0BF84AFA01388A494B08277B0B28B671681C8B008B297AD69452CD39968',
  'src/l12/net.ts': '22E9B0DC986CCDA0420EE8B025EC06B52D294DC8BFE6B7F4897C251955B26DC0',
}
const sha = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
const fileSha = file => sha(fs.readFileSync(file))
const sources = () => Object.fromEntries(Object.keys(pins).map(file => [file, fileSha(path.join(front, file))]))
const [base, output, cache] = process.argv.slice(2)
assert.equal(process.argv.length, 5, 'Expected existing preview URL, new evidence directory and new cache directory')
const url = new URL(base)
assert.equal(url.origin, 'http://127.0.0.1:5297')
assert.equal(url.pathname, '/__l12_battle_preview__')
assert.equal(url.search + url.hash + url.username + url.password, '')
for (const [selected, allowed] of [
  [output, path.join(project, 'artifacts/player-gm-same-window-20261024')],
  [cache, path.join(project, 'cache/primary/player-gm-same-window-20261024')],
]) {
  assert.ok(path.isAbsolute(selected) && path.resolve(selected).startsWith(allowed + path.sep), 'Output outside the named QA family')
  assert.equal(fs.existsSync(selected), false, 'Refusing an existing run directory')
}
assert.equal(fileSha(fixturePath), fixturePin, 'The reviewed synthetic fixture changed')
assert.deepEqual(sources(), pins, 'Reviewed UI/preview sources changed; review before changing pins')
const fixture = JSON.parse(fs.readFileSync(fixturePath, 'utf8'))
assert.equal(fixture.status, 'ACTUAL_HANDLE_CHAIN_PASSED')
assert.equal(fixture.realConnectionTakeover, false)
const frames = fixture.frames.filter(frame => frame.sequence === 21)
assert.equal(frames.length, 1)
const frame = frames[0], snapshot = frame.before[0]
assert.equal(frame.actor, 0)
assert.equal(frame.depth, 6)
assert.equal(snapshot.you, 0)
assert.equal(snapshot.revision, 21)
assert.equal(snapshot.effectStack.length, 6)
assert.equal(snapshot.players[0].master.hp, 8)
assert.equal(snapshot.prompts.length, 1)
const prompt = snapshot.prompts[0]
assert.equal(prompt.promptId, 'prompt-20')
assert.equal(prompt.kind, 'response')
assert.equal(prompt.playerIndex, 0)
assert.equal(prompt.data.choiceMode, 'instant')
assert.deepEqual(prompt.validChoices, ['deep-counter-0-2', 'pass'])
assert.equal(frame.command.type, 'resolvePrompt')
assert.equal(frame.command.promptId, prompt.promptId)
assert.deepEqual(frame.command.cardInstanceIds, ['pass'])
// The C# command serializes unused fields as null. Preserve the exact frontend
// prompt-binding presence (missing -> undefined -> omitted), not those unused fields.
const playerCommand = { type: 'resolvePrompt', promptId: prompt.promptId, cardInstanceIds: ['pass'] }
for (const key of ['activationId', 'sourceInstanceId', 'sourceCardId', 'step', 'createdRevision', 'controller']) {
  if (Object.hasOwn(prompt, key)) playerCommand[key] = prompt[key]
  assert.deepEqual(playerCommand[key] ?? null, frame.command[key] ?? null, `Reviewed command binding ${key}`)
}
for (const [key, value] of Object.entries(frame.command))
  if (value !== null) assert.deepEqual(playerCommand[key], value, `Reviewed command field ${key}`)

const pwsh = process.env.L12_PWSH
assert.ok(pwsh && path.isAbsolute(pwsh) && fs.statSync(pwsh).isFile(), 'Select L12_PWSH with the shared initializer')
const temp = path.join(cache, 'tmp')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  '. $env:L12_STORAGE_GUARD; foreach($p in @($env:L12_UI_QA_OUT,$env:L12_UI_QA_CACHE,$env:L12_UI_QA_TEMP)) { '
  + '$null=Assert-L12GeneratedPath -Path $p -Purpose "player GM same-window UI QA" }'], {
  env: { ...process.env, L12_STORAGE_GUARD: path.join(repo, 'ops/windows/L12GeneratedStorage.ps1'),
    L12_UI_QA_OUT: output, L12_UI_QA_CACHE: cache, L12_UI_QA_TEMP: temp },
  stdio: 'pipe', timeout: 15000, windowsHide: true,
})
fs.mkdirSync(output, { recursive: true })
fs.mkdirSync(temp, { recursive: true })
process.env.TEMP = temp
process.env.TMP = temp
process.env.TMPDIR = temp

const require = createRequire(path.join(front, 'package.json'))
const reportPath = path.join(output, 'report.json')
const report = {
  schema: 1, status: 'RUNNING', bugId: 'BUG-20260912-83eac06e',
  fixture: { path: fixturePath, sha256: fixturePin, sequence: 21, revision: 21, actor: 0, depth: 6, promptId: prompt.promptId },
  sourcesBefore: sources(), qaSha256: fileSha(import.meta.filename),
  viewport: { width: 1440, height: 900 }, input: 'native desktop mouse and keyboard',
  syntheticCreatorContext: 'qa-player-gm-same-window-owner',
  liveServerAuthorizationTested: false, realServerAck: false, authoritativeSettlementTested: false,
  originalPlayerReplay: false, originalBugFixed: false, physicalDeviceAccepted: false,
  boundary: 'Pinned synthetic engine recipient projection; current real GamePage/GameBoard/PromptOverlay/GmPanel/net handlers. Both branches use the same synthetic sandbox-creator capability, not a real authenticated session. Existing preview fake socket captures envelopes and resets pendingAction every 200ms without a server ACK. No checkpoint restore, backend state advancement, refresh recovery, production or long-soak claim. Artwork, broadcast settings and the empty public alternate-art catalog are controlled fixtures.',
  branches: [], pageErrors: [], controlledPublicReads: [], blockedRequests: [], errors: [], browserClosed: false,
}
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), { flag: 'wx' })
const write = () => fs.writeFileSync(reportPath, JSON.stringify(report, null, 2))
const settle = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
let context

// Same bounded hit-testing algorithm as qa-deep-response-ui-20261023.mjs.
// Do not replace native input with locator.click(force), DOM click or scrollIntoView.
async function hit(page, locator, branch) {
  assert.equal(await locator.count(), 1, 'Expected one exact control')
  await locator.waitFor({ state: 'visible' })
  assert.equal(await locator.isEnabled(), true, 'The current control must be enabled')
  const inspect = () => locator.evaluate(node => {
    const r = node.getBoundingClientRect(), x = r.x + r.width / 2, y = r.y + r.height / 2
    const top = document.elementFromPoint(x, y), scrollOwners = []
    for (let parent = node.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent), b = parent.getBoundingClientRect()
      if (/(auto|scroll)/.test(style.overflowY) && parent.clientHeight > 8 && parent.scrollHeight > parent.clientHeight + 1) {
        const px = b.x + b.width / 2, py = b.y + Math.min(b.height / 2, Math.max(1, innerHeight - b.y - 1))
        const owner = document.elementFromPoint(px, py)
        if (px >= 0 && px < innerWidth && py >= 0 && py < innerHeight && owner && parent.contains(owner))
          scrollOwners.push({ x: px, y: py, direction: y > b.bottom ? 1 : -1 })
      }
    }
    return { x, y, width: r.width, height: r.height,
      within: x >= 0 && x < innerWidth && y >= 0 && y < innerHeight,
      owned: !!top && node.contains(top), scrollOwners,
      text: node.textContent?.trim().slice(0, 100), hitTag: top?.tagName, hitClass: top?.className }
  })
  let point = await inspect()
  const attempt = { initial: point, wheels: [], automaticScrolling: false }
  branch.hits.push(attempt)
  write()
  for (let index = 0; !(point.within && point.owned) && point.scrollOwners.length && index < 8; index++) {
    const owner = point.scrollOwners[0], deltaY = owner.direction * 180
    await page.mouse.move(owner.x, owner.y)
    await page.mouse.wheel(0, deltaY)
    await settle(page)
    point = await inspect()
    attempt.wheels.push({ ...owner, deltaY, after: point })
    write()
  }
  attempt.final = point
  write()
  assert.ok(point.within && point.owned && point.width > 0 && point.height > 0, 'Control centre is clipped or covered')
  await page.mouse.click(point.x, point.y)
  await settle(page)
}

async function facts(page) {
  return page.evaluate(() => {
    const state = window.__battleDockFixture.state
    return {
      capability: { gmEnabled: state.gmEnabled, spectating: state.spectating, status: state.status,
        accountId: state.accountId, sessionId: state.sessionId, sandbox: state.room.sandbox,
        roomCode: state.room.roomCode, yourPlayerIndex: state.room.yourPlayerIndex },
      game: JSON.parse(JSON.stringify(state.game)),
      sent: JSON.parse(JSON.stringify(window.__sentCommands)),
      pendingAction: state.pendingAction,
    }
  })
}

function unchanged(observed, baseline, noSend = true) {
  assert.deepEqual(observed.capability, baseline.capability, 'Creator capability or observing player changed')
  assert.deepEqual(observed.game, baseline.game, 'The fixture response window or game state changed')
  if (noSend) assert.deepEqual(observed.sent, [], 'Viewing/minimizing/editing must not send a command')
}

function exactEnvelope(envelope, expected) {
  assert.equal(typeof envelope.requestId, 'string')
  assert.ok(envelope.requestId.length > 0)
  const { requestId, ...body } = envelope
  assert.deepEqual(body, expected, 'Unexpected command, actor, target, binding or envelope field')
}

try {
  const { chromium } = require(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const definitions = ['cards.s1.json', 'cards.s2.json', 'cards.st.json'].flatMap(name =>
    JSON.parse(fs.readFileSync(path.join(repo, '服务端WebSocket/TwelveLegions/Data', name))))
  const art = '/assets/l12/special/master/S02-06M2.png'
  const artHash = fileSha(path.join(front, 'public', art))
  const manifest = { schemaVersion: 3, catalogVersion: 'player-gm-ui-only', assetVersion: 'player-gm-ui-only', basePath: '/',
    cards: Object.fromEntries(definitions.map(card => [card.id, { cardId: card.id, contentHash: artHash,
      width: 720, height: 720, orientation: 'portrait', variants: { thumbWebp: art, boardWebp: art, detailWebp: art } }])) }
  report.controlledArtworkSha256 = artHash
  context = await chromium.launchPersistentContext(path.join(cache, 'profile'), {
    channel: 'msedge', headless: true, viewport: report.viewport,
  })
  await context.route('**/*', route => {
    const request = route.request(), requested = new URL(request.url())
    if (requested.origin === url.origin && requested.pathname === '/card-assets/card-assets.manifest.json')
      return route.fulfill({ json: manifest })
    if (requested.origin === 'http://127.0.0.1:8080' && request.method() === 'GET'
      && requested.pathname === '/api/ranked/broadcasts/settings' && !requested.search)
      return route.fulfill({ json: { displaySeconds: 16, lobbyDelaySeconds: 0, intervalSeconds: 60,
        winStreakThreshold: 5, streakEndedThreshold: 5, minimumTierIndex: 0, winStreakEnabled: false,
        streakEndedEnabled: false, highestTierEnabled: false, factionTitleEnabled: false, masterTitleEnabled: false } })
    if (requested.origin === 'http://127.0.0.1:8080' && request.method() === 'GET'
      && requested.pathname === '/api/alternate-arts' && !requested.search) {
      // alternateArtApi.gallery(): AlternateArt[], not the admin search-page DTO.
      report.controlledPublicReads.push({ method: 'GET', origin: requested.origin, path: requested.pathname })
      return route.fulfill({ status: 200, json: [] })
    }
    if (requested.origin === url.origin && request.method() === 'GET') return route.continue()
    report.blockedRequests.push({ method: request.method(), origin: requested.origin, path: requested.pathname })
    return route.abort()
  })

  for (const name of ['player-pass', 'gm-set-life']) {
    const branch = { name, hits: [], status: 'RUNNING' }
    report.branches.push(branch)
    const page = await context.newPage()
    page.setDefaultTimeout(8000)
    page.on('pageerror', error => { report.pageErrors.push({ branch: name, message: error.message }); write() })
    const response = await page.goto(base + '?canvas=1&morale=3&hand=1', { waitUntil: 'domcontentloaded' })
    assert.equal(response?.ok(), true)
    await page.waitForFunction(() => !!window.__battleDockFixture?.state?.game)
    await page.evaluate(({ snapshot, owner }) => {
      const state = window.__battleDockFixture.state
      // Retain the preview's object references so its timer cannot restore a different board.
      for (const key of Object.keys(state.game)) delete state.game[key]
      Object.assign(state.game, snapshot)
      Object.assign(state.room, { sandbox: true, roomCode: snapshot.roomCode, yourPlayerIndex: 0,
        players: snapshot.players.map(player => ({ playerIndex: player.playerIndex, name: player.name,
          connected: true, ready: true, deckIndex: 0 })) })
      state.accountId = owner
      state.sessionId = owner
      state.gmEnabled = true
      state.spectating = false
      state.status = 'online'
      // Setup only, before either branch. Never simulate a command ACK or an after-state.
      window.__resetSentCommands()
    }, { snapshot, owner: report.syntheticCreatorContext })
    await settle(page)
    const baseline = await facts(page)
    assert.deepEqual(baseline.game, snapshot)
    assert.equal(baseline.capability.gmEnabled, true)
    assert.equal(baseline.capability.spectating, false)
    assert.equal(baseline.capability.yourPlayerIndex, 0)
    assert.equal(baseline.capability.sandbox, true)
    assert.deepEqual(baseline.sent, [])
    branch.startProjectionSha256 = sha(JSON.stringify(baseline.game))
    branch.capability = baseline.capability
    assert.equal(branch.startProjectionSha256, report.branches[0].startProjectionSha256)

    // The desktop GM panel initially opens. Normalize both branches with native
    // minimize/close/restore operations, without changing capability or the window.
    const minimize = page.locator('.l12-prompt-overlay .prompt-minimize')
    await hit(page, minimize, branch)
    unchanged(await facts(page), baseline)
    const closeGm = page.locator('.gm-panel button[aria-label="收起 GM 面板"]')
    if (await closeGm.count()) await hit(page, closeGm, branch)
    unchanged(await facts(page), baseline)
    await hit(page, page.locator('.prompt-minimized-bar button'), branch)
    unchanged(await facts(page), baseline)
    await page.screenshot({ path: path.join(output, name + '-before.png') })

    if (name === 'player-pass') {
      await hit(page, page.locator('[data-tutorial-prompt-id="prompt-20"] [data-tutorial-choice="pass"]'), branch)
    } else {
      await hit(page, minimize, branch)
      unchanged(await facts(page), baseline)
      branch.minimizeSentCommands = 0
      await hit(page, page.locator('.gm-open'), branch)
      unchanged(await facts(page), baseline)
      const button = page.getByRole('button', { name: '设置主宰血量', exact: true })
      const input = button.locator('..').locator('input[type="number"]')
      await hit(page, input, branch)
      await page.keyboard.press('ControlOrMeta+A')
      await page.keyboard.type('7')
      await settle(page)
      assert.equal(await input.inputValue(), '7')
      unchanged(await facts(page), baseline)
      await hit(page, button, branch)
    }
    const observed = await facts(page)
    unchanged(observed, baseline, false)
    assert.equal(observed.sent.length, 1, 'One native command must produce exactly one envelope')
    const expected = name === 'player-pass'
      ? { type: 'sandboxAction', actingPlayerIndex: 0, command: playerCommand }
      : { type: 'gmAction', command: { type: 'setLife', targetPlayer: 0, value: 7 } }
    exactEnvelope(observed.sent[0], expected)
    branch.envelope = observed.sent[0]
    branch.pendingActionObservationOnly = observed.pendingAction
    branch.projectedStateUnchangedWithoutAck = true
    await page.screenshot({ path: path.join(output, name + '-after-send-no-ack.png') })
    // Bounded duplicate-observation interval, not an ACK wait or a performance SLO.
    await page.waitForTimeout(250)
    assert.equal((await facts(page)).sent.length, 1)
    branch.status = 'NATIVE_UI_EXACT_ENVELOPE_VERIFIED_NO_SERVER_ACK'
    await page.close()
    write()
  }
  assert.deepEqual(report.pageErrors, [])
  assert.deepEqual(report.blockedRequests, [])
  assert.equal(report.branches.length, 2)
  report.status = 'CURRENT_UI_SAME_WINDOW_DISPATCH_PASSED_NO_SERVER_ACK'
} catch (error) {
  report.status = 'FAILED'
  report.errors.push(String(error.stack || error))
  process.exitCode = 1
} finally {
  if (context) {
    let timer
    try {
      await Promise.race([context.close(), new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('Browser close exceeded 15 seconds; inspect owned processes')), 15000)
      })])
      report.browserClosed = true
    } catch (error) {
      report.status = 'FAILED'
      report.errors.push(String(error))
      process.exitCode = 1
    } finally { clearTimeout(timer) }
  }
  try {
    report.sourcesAfter = sources()
    assert.deepEqual(report.sourcesAfter, report.sourcesBefore)
    assert.equal(fileSha(fixturePath), fixturePin)
    assert.equal(fileSha(import.meta.filename), report.qaSha256)
  } catch (error) {
    report.status = 'FAILED'
    report.errors.push(String(error))
    process.exitCode = 1
  }
  write()
}
