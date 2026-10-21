import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import net from 'node:net'
import path from 'node:path'
import { spawn, execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { pathToFileURL } from 'node:url'

const repo = path.resolve(import.meta.dirname, '../..')
const front = path.join(repo, 'opcgpro-vue')
const cacheParent = process.env.L12_MOBILE_EFFECT_CACHE
const output = process.env.L12_MOBILE_EFFECT_OUTPUT
const accepted = process.env.L12_ACCEPTED_SERVER_ROOT
const hostAssembly = process.env.L12_MOBILE_EFFECT_HOST_DLL
const pwsh = process.env.L12_PWSH || 'pwsh'
const expectedServerSha = process.env.L12_EXPECTED_SERVER_SHA?.trim().toUpperCase()
const expectedHostSha = process.env.L12_EXPECTED_HOST_SHA?.trim().toUpperCase()
for (const value of [cacheParent, output, accepted, hostAssembly])
  assert(value && path.isAbsolute(value), 'Explicit absolute governed roots and host DLL are required')
for (const [name, value] of [['TEMP', process.env.TEMP], ['TMP', process.env.TMP]])
  assert(value && path.isAbsolute(value) && /^[DE]:[\\/]/i.test(value), `${name} must be an explicit D/E generated-data path`)
assert.match(expectedServerSha || '', /^[A-F0-9]{64}$/, 'Explicit accepted server SHA-256 is required')
assert.match(expectedHostSha || '', /^[A-F0-9]{64}$/, 'Explicit test host SHA-256 is required')
assert(!fs.existsSync(path.join(output, 'report.json')), 'Refusing to overwrite prior evidence')
assert(!fs.existsSync(output) || fs.readdirSync(output).length === 0,
  'Evidence output must be new or empty; old output is never reused')

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
assert.equal(sha256(fs.readFileSync(path.join(accepted, 'GrandUMIServer.dll'))), expectedServerSha,
  'Accepted server assembly drifted from the explicitly approved build')
assert.equal(sha256(fs.readFileSync(hostAssembly)), expectedHostSha,
  'Test-only host assembly drifted from the explicitly approved build')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  `. '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' -CacheRoot $env:L12_MOBILE_EFFECT_CACHE -DependencyCacheRoot 'D:/GPT/Legion12/cache/primary' | Out-Null; `
  + `. '${repo}/ops/windows/L12GeneratedStorage.ps1'; `
  + `foreach($p in @($env:L12_MOBILE_EFFECT_CACHE,$env:L12_MOBILE_EFFECT_OUTPUT,$env:L12_ACCEPTED_SERVER_ROOT,(Split-Path -Parent $env:L12_MOBILE_EFFECT_HOST_DLL),$env:TEMP,$env:TMP)){`
  + `$null=Assert-L12GeneratedPath -Path $p -Purpose 'mobile mandatory target full chain'}`],
{ env: process.env, stdio: 'pipe', windowsHide: true })
fs.mkdirSync(cacheParent, { recursive: true })
fs.mkdirSync(output, { recursive: true })

for (const file of [path.join(accepted, 'GrandUMIServer.dll'), hostAssembly]) {
  const stat = fs.lstatSync(file)
  assert(stat.isFile() && !stat.isSymbolicLink(), `Linked or non-file dependency rejected: ${file}`)
}

const requireFront = createRequire(path.join(front, 'package.json'))
const { createServer } = await import(pathToFileURL(requireFront.resolve('vite')).href)
const { chromium } = requireFront('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))
const viewports = [
  { name: 'portrait', width: 390, height: 844 },
  { name: 'landscape', width: 844, height: 390 },
]
const report = {
  schema: 1,
  status: 'RUNNING',
  scope: 'Local test-only ordinary-room Ankh Stele mandatory own-target chain; no deployment or product mutation.',
  acceptedServerSha: expectedServerSha,
  hostAssemblySha: expectedHostSha,
  sourceHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).trim(),
  sourceDirty: execFileSync('git', ['status', '--short'], { cwd: repo, encoding: 'utf8' }).trim().split(/\r?\n/).filter(Boolean),
  sourceBefore: {
    fixtureHost: sha256(fs.readFileSync(path.join(repo, 'ops/qa/LocalPressureHost/Program.cs'))),
    runner: sha256(fs.readFileSync(import.meta.filename)),
  },
  storageBudget: { maximumBytesPerCase: 512 * 1024 * 1024 },
  boundaries: [
    'Real local ordinary-room App/WebSocket flow with native touch and same-request authoritative acknowledgements.',
    'The server and test-host hashes must be supplied from an independently accepted build; this runner does not bless a candidate.',
    'Each viewport owns a new bounded D-drive root and removes it only after browsers, Vite and host stop cleanly.',
  ],
  cases: [],
}

function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer()
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const address = server.address()
      server.close(error => error ? reject(error) : resolve(address.port))
    })
  })
}

function startOwned(executable, args, options, logPath) {
  const log = fs.openSync(logPath, 'wx')
  const child = spawn(executable, args, { ...options, windowsHide: true, stdio: ['ignore', log, log] })
  fs.closeSync(log)
  child.done = new Promise(resolve => {
    child.once('error', error => resolve({ error: error.code || error.message }))
    child.once('exit', (code, signal) => resolve({ code, signal }))
  })
  return child
}

function inspectOwnedTree(root) {
  let bytes = 0
  const links = []
  const visit = current => {
    const stat = fs.lstatSync(current)
    if (stat.isSymbolicLink()) { links.push(current); return }
    if (stat.isFile()) { bytes += stat.size; return }
    if (stat.isDirectory()) for (const name of fs.readdirSync(current)) visit(path.join(current, name))
  }
  visit(root)
  return { bytes, links }
}

function removeOwnedRoot(root, expectedRunId) {
  const ownerFile = path.join(root, 'owner.json')
  const owner = JSON.parse(fs.readFileSync(ownerFile, 'utf8'))
  assert.equal(owner.kind, 'l12-local-real-pressure-v1')
  assert.equal(owner.runId, expectedRunId)
  assert.equal(path.dirname(root), path.resolve(cacheParent))
  const usage = inspectOwnedTree(root)
  assert.deepEqual(usage.links, [], 'Owned run root contains a link and is not safe to remove')
  assert(usage.bytes <= report.storageBudget.maximumBytesPerCase,
    `Owned run root exceeded 512 MiB: ${usage.bytes}`)
  fs.rmSync(root, { recursive: true, force: false })
  assert(!fs.existsSync(root), 'Owned run root remained after bounded cleanup')
  return usage
}

async function waitJson(file, child, timeout = 60_000) {
  const deadline = Date.now() + timeout
  while (Date.now() < deadline) {
    if (child.exitCode !== null || child.signalCode !== null)
      throw new Error(`Owned host exited before ${path.basename(file)}`)
    if (fs.existsSync(file)) return JSON.parse(fs.readFileSync(file, 'utf8'))
    await delay(80)
  }
  throw new Error(`Timed out waiting for ${path.basename(file)}`)
}

async function login(baseUrl, username, password) {
  const response = await fetch(new URL('/api/auth/login', baseUrl), {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ username, password }), signal: AbortSignal.timeout(10_000),
  })
  assert.equal(response.status, 200, `Login failed for ${username}`)
  const result = await response.json()
  assert(result.token && result.account?.id, 'Authenticated account payload required')
  return result
}

function captureTransport(page, entries) {
  page.on('websocket', socket => {
    socket.on('framesent', frame => {
      try {
        const value = JSON.parse(String(frame.payload))
        if (value.type === 'gameAction') entries.push({ direction: 'sent', type: value.type,
          requestId: value.requestId, command: value.command })
      } catch { /* Non-JSON frames are outside this protocol proof. */ }
    })
    socket.on('framereceived', frame => {
      try {
        const wire = JSON.parse(String(frame.payload))
        const value = wire.type === 'gameStateCardFrameV1' ? wire.payload : wire
        const deltaRequestId = wire.type === 'gameStateDelta'
          ? wire.changes?.find(change => change.path?.length === 1 && change.path[0] === 'requestId')?.value : null
        if (value.type === 'gameState' || wire.type === 'gameStateDelta') entries.push({
          direction: 'received', type: 'gameState', wireType: wire.type,
          requestId: value.requestId || deltaRequestId,
          revision: value.state?.revision ?? wire.revision,
        })
        else if (value.type === 'actionRejected') entries.push({ direction: 'received', type: value.type,
          requestId: value.requestId, message: value.message })
      } catch { /* Non-JSON frames are outside this protocol proof. */ }
    })
  })
}

async function observe(page) {
  return page.evaluate(async () => {
    const { l12State: state } = await import('/src/l12/net.ts')
    const game = state.game
    return {
      status: state.status, pending: state.pendingAction, roomCode: state.room?.roomCode,
      sandbox: state.room?.sandbox, gm: state.gmEnabled, matchId: game?.matchId,
      revision: game?.revision, phase: game?.phase, you: game?.you,
      prompts: game?.prompts || [], recentEvents: game?.recentEvents || [],
      self: game?.players.find(player => player.playerIndex === game.you),
      enemy: game?.players.find(player => player.playerIndex !== game.you),
    }
  })
}

async function waitState(page, predicate, label, timeout = 15_000) {
  const deadline = Date.now() + timeout
  let last
  while (Date.now() < deadline) {
    const state = last = await observe(page)
    if (predicate(state)) return state
    await delay(70)
  }
  const summary = last ? JSON.stringify({ status: last.status, pending: last.pending, revision: last.revision,
    you: last.you, prompts: last.prompts.map(prompt => ({ playerIndex: prompt.playerIndex,
      kind: prompt.kind, validChoices: prompt.validChoices })) }) : 'unavailable'
  throw new Error(`State deadline: ${label}; last=${summary}`)
}

async function touch(page, locator, label) {
  await locator.waitFor({ state: 'visible' })
  await locator.scrollIntoViewIfNeeded()
  const deadline = Date.now() + 10_000
  let point = null
  while (!point && Date.now() < deadline) {
    if (await locator.isEnabled()) point = await locator.evaluate(element => {
      const rect = element.getBoundingClientRect()
      for (const fx of [.18, .42, .68, .84]) for (const fy of [.2, .45, .7, .84]) {
        const x = rect.left + rect.width * fx, y = rect.top + rect.height * fy
        if (x >= 0 && y >= 0 && x <= innerWidth && y <= innerHeight
            && element.contains(document.elementFromPoint(x, y))) return { x, y }
      }
      return null
    })
    if (!point) await delay(70)
  }
  if (!point) {
    const obstruction = await locator.evaluate(element => {
      const rect = element.getBoundingClientRect()
      return [[.18, .2], [.5, .5], [.84, .84]].map(([fx, fy]) => {
        const x = rect.left + rect.width * fx, y = rect.top + rect.height * fy
        const hit = document.elementFromPoint(x, y)
        return { x, y, tag: hit?.tagName?.toLowerCase() ?? null,
          className: typeof hit?.className === 'string' ? hit.className.slice(0, 160) : null,
          ariaLabel: hit?.getAttribute?.('aria-label') ?? null,
          tutorialResource: hit?.getAttribute?.('data-tutorial-payment-resource') ?? null }
      })
    })
    throw new Error(`No visible unobstructed touch point: ${label}; elementFromPoint=${JSON.stringify(obstruction)}`)
  }
  const target = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    return { tag: element.tagName.toLowerCase(), text: (element.textContent || '').trim().slice(0, 120),
      ariaLabel: element.getAttribute('aria-label'), choice: element.getAttribute('data-tutorial-choice'),
      action: element.getAttribute('data-tutorial-action'), instanceId: element.getAttribute('data-tutorial-card-instance'),
      rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom } }
  })
  await page.touchscreen.tap(point.x, point.y)
  return { label, input: 'touchscreen', viewport: await page.viewportSize(), point, target }
}

async function waitAck(entries, start, predicate, beforeRevision, label) {
  const deadline = Date.now() + 15_000
  while (Date.now() < deadline) {
    const sent = entries.slice(start).find(entry => entry.direction === 'sent'
      && entry.type === 'gameAction' && typeof entry.requestId === 'string'
      && entry.requestId.length > 0 && predicate(entry.command || {}))
    const ack = sent && entries.slice(start).find(entry => entry.direction === 'received'
      && entry.type === 'gameState' && entry.requestId === sent.requestId
      && Number(entry.revision) > Number(beforeRevision))
    if (sent && ack) return { requestId: sent.requestId, command: sent.command,
      beforeRevision, ackRevision: ack.revision, wireType: ack.wireType }
    await delay(60)
  }
  throw new Error(`No same-request authoritative ACK with revision advance: ${label}`)
}

async function selectPromptChoice(page, choice, touches) {
  const before = await observe(page)
  const prompt = before.prompts.find(item => item.playerIndex === before.you)
  assert(prompt?.validChoices?.includes(choice), `Prompt does not allow ${choice}`)
  touches.push(await touch(page, page.locator(`[data-tutorial-choice=${JSON.stringify(choice)}]`).filter({ visible: true }).first(),
    `choose ${choice}`))
  await delay(80)
  const afterChoice = await observe(page)
  if (!afterChoice.pending && afterChoice.prompts.some(item => item.promptId === prompt.promptId))
    touches.push(await touch(page, page.locator('[data-tutorial-action="confirm-prompt"]').filter({ visible: true }).first(),
      `confirm ${choice}`))
  return { before, prompt }
}

async function passAllCurrentResponses(actor, guest, result, stage) {
  const pages = [actor, guest]
  const transports = [result.transport.actor, result.transport.guest]
  const passes = []
  const completedPrompts = new Set()
  const deadline = Date.now() + 15_000
  while (passes.length < 2 && Date.now() < deadline) {
    const states = [await observe(actor), await observe(guest)]
    const playerIndex = states.findIndex((state, index) => state.prompts.some(prompt =>
      prompt.playerIndex === index && prompt.kind === 'response' && prompt.validChoices?.includes('pass')
        && !completedPrompts.has(prompt.promptId)))
    if (playerIndex < 0) { await delay(70); continue }
    const page = pages[playerIndex]
    const state = states[playerIndex]
    const start = transports[playerIndex].length
    const passButton = page.locator('[data-tutorial-choice="pass"]').filter({ visible: true }).first()
    result.responseControlGeometries.push({ stage, playerIndex,
      geometry: await assertControlInsideViewport(page, passButton, `${stage} player ${playerIndex + 1} pass`) })
    const selection = await selectPromptChoice(page, 'pass', result.touches)
    const ack = await waitAck(transports[playerIndex], start,
      command => command.type === 'resolvePrompt' && command.promptId === selection.prompt.promptId
        && (command.choice === 'pass' || command.cardInstanceIds?.includes('pass')),
      state.revision, `${stage} player ${playerIndex + 1} pass`)
    const receipt = { stage, playerIndex, promptId: selection.prompt.promptId, ack }
    completedPrompts.add(selection.prompt.promptId)
    passes.push(receipt)
    result.acknowledgements.push(ack)
    result.responsePasses.push(receipt)
  }
  assert.equal(passes.length, 2, `Both explicit response passes must be observed: ${stage}`)
  return passes
}

async function assertControlInsideViewport(page, locator, label) {
  const geometry = await locator.evaluate(element => {
    const rect = element.getBoundingClientRect()
    return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom,
      width: rect.width, height: rect.height, viewportWidth: innerWidth, viewportHeight: innerHeight }
  })
  assert(geometry.width > 0 && geometry.height > 0, `${label} has no hit box`)
  assert(geometry.left >= 0 && geometry.top >= 0
    && geometry.right <= geometry.viewportWidth && geometry.bottom <= geometry.viewportHeight,
  `${label} is clipped outside the mobile viewport`)
  return geometry
}

async function openAuthenticatedPage(context, uiBase, loginResult, endpoint, transport) {
  await context.addInitScript(({ token, account, endpoint }) => {
    localStorage.setItem('l12-auth-token', token)
    localStorage.setItem('l12-account', JSON.stringify(account))
    localStorage.setItem('l12-nickname', account.username)
    localStorage.setItem('l12-endpoint', endpoint)
  }, { token: loginResult.token, account: loginResult.account, endpoint })
  const page = await context.newPage()
  page.setDefaultTimeout(12_000)
  captureTransport(page, transport)
  await page.goto(new URL('/game', uiBase).toString(), { waitUntil: 'domcontentloaded' })
  return page
}

function canRemoveMandatoryOwnedRoot(result) {
  return Array.isArray(result.cleanupErrors) && result.cleanupErrors.length === 0
    && result.hostExit?.code === 0 && result.gracefulHostStop === true
    && result.forcedStop !== true
}

async function closeOwnedUi(actorContext, guestContext, vite, result) {
  result.cleanupErrors ??= []
  for (const [name, resource] of [['actor browser', actorContext], ['guest browser', guestContext], ['Vite', vite]]) {
    if (!resource) continue
    try {
      const closed = await Promise.race([
        Promise.resolve().then(() => resource.close()).then(() => true),
        delay(8000).then(() => false),
      ])
      assert(closed, `${name} close timed out`)
      if (name === 'Vite') assert(!resource.httpServer?.listening, 'Vite still listening after close')
      else assert(!resource.browser()?.isConnected(), `${name} still connected after close`)
    } catch (error) { result.cleanupErrors.push(`${name}: ${String(error?.message || error)}`) }
  }
}

async function runCase(viewport) {
  const runId = crypto.randomUUID().replaceAll('-', '')
  const root = path.join(cacheParent, `${viewport.name}-${runId}`)
  const caseOutput = path.join(output, viewport.name)
  fs.mkdirSync(root, { recursive: false })
  fs.mkdirSync(caseOutput, { recursive: false })
  const owner = { schema: 1, kind: 'l12-local-real-pressure-v1', runId, cacheParent,
    serverSha256: expectedServerSha }
  fs.writeFileSync(path.join(root, 'owner.json'), JSON.stringify(owner), { flag: 'wx' })
  const password = crypto.randomBytes(32).toString('base64url')
  const uiPort = await freePort()
  const cleanEnv = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('L12_')))
  const host = startOwned('C:/Program Files/dotnet/dotnet.exe', [hostAssembly], {
    cwd: root,
    env: { ...cleanEnv,
      PATH: path.join(accepted, 'runtimes/win-x64/native') + path.delimiter + (cleanEnv.PATH || cleanEnv.Path || ''),
      L12_LOCAL_QUALIFICATION_ROOT: root, L12_ACCEPTED_SERVER_ROOT: accepted,
      L12_LISTEN_HOST: '127.0.0.1', L12_LOCAL_DIAGNOSTIC_NONCE: crypto.randomBytes(16).toString('hex'),
      L12_LOCAL_EFFECT_FIXTURE: 'ankh-mandatory-target-v1', L12_LOCAL_EFFECT_PASSWORD: password,
      L12_ALLOWED_ORIGINS: `http://127.0.0.1:${uiPort}` },
  }, path.join(caseOutput, 'host.log'))
  let vite, actorContext, guestContext
  const result = { name: viewport.name, viewport: { width: viewport.width, height: viewport.height },
    runId, hostPid: host.pid, touches: [], transport: { actor: [], guest: [] }, acknowledgements: [],
    responsePasses: [], responseControlGeometries: [], errors: [] }
  try {
    const ready = await waitJson(path.join(root, 'ready.json'), host)
    assert.equal(ready.pid, host.pid)
    assert.equal(ready.serverSha256, expectedServerSha)
    assert.equal(ready.syntheticHistoryMatches, 0)
    assert.equal(ready.effectFixture?.kind, 'ankh-mandatory-target-v1')
    assert.equal(ready.effectFixture?.forcedManualResponsePasses, true)
    assert(!JSON.stringify(ready).includes(password), 'Fixture password must never enter ready evidence')
    const baseUrl = new URL(ready.baseUrl)
    assert.equal(baseUrl.hostname, '127.0.0.1')
    const endpoint = `ws://127.0.0.1:${baseUrl.port}/ws`
    const actorLogin = await login(baseUrl, ready.effectFixture.hostUsername, password)
    const guestLogin = await login(baseUrl, ready.effectFixture.guestUsername, password)

    process.env.VITE_WS_URL = endpoint
    process.env.L12_VITE_CACHE_DIR = path.join(root, 'vite')
    vite = await createServer({ root: front, configLoader: 'runner', cacheDir: path.join(root, 'vite'),
      server: { host: '127.0.0.1', port: uiPort, strictPort: true } })
    await vite.listen()
    const uiBase = `http://127.0.0.1:${uiPort}`
    actorContext = await chromium.launchPersistentContext(path.join(root, 'profile-actor'), {
      headless: true, channel: 'msedge', hasTouch: true, isMobile: true,
      viewport: { width: viewport.width, height: viewport.height },
    })
    guestContext = await chromium.launchPersistentContext(path.join(root, 'profile-guest'), {
      headless: true, channel: 'msedge', hasTouch: true, isMobile: true,
      viewport: { width: viewport.width, height: viewport.height },
    })
    const actor = await openAuthenticatedPage(actorContext, uiBase, actorLogin, endpoint, result.transport.actor)
    const guest = await openAuthenticatedPage(guestContext, uiBase, guestLogin, endpoint, result.transport.guest)
    actor.on('pageerror', error => result.errors.push(`actor:${error.message}`))
    guest.on('pageerror', error => result.errors.push(`guest:${error.message}`))

    const initial = await waitState(actor, state => state.status === 'online'
      && state.matchId === ready.effectFixture.matchId && state.roomCode === ready.effectFixture.roomCode,
    'actor recovered ordinary fixture')
    const guestInitial = await waitState(guest, state => state.status === 'online'
      && state.matchId === ready.effectFixture.matchId && state.roomCode === ready.effectFixture.roomCode,
    'guest recovered ordinary fixture')
    assert.equal(initial.gm, false); assert.equal(initial.sandbox, false)
    assert.equal(guestInitial.gm, false); assert.equal(guestInitial.sandbox, false)
    assert.equal(initial.you, 0); assert.equal(guestInitial.you, 1)
    const source = initial.self.hand.find(card => card.instanceId === ready.effectFixture.sourceInstanceId)
    const target = initial.self.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)
    assert.equal(source?.cardId, 'S01-0215')
    assert.equal(target?.cardId, 'S01-0212')
    assert.equal(initial.self.relic, null)
    assert.equal(source.cost, ready.effectFixture.sourcePrintedCost)
    assert.equal(JSON.stringify(guestInitial.enemy).includes(ready.effectFixture.sourceInstanceId), false,
      'Opponent projection leaked a private hand instance')
    const initialMorale = new Map(initial.self.morale.map(card => [card.instanceId, card]))
    for (const id of ready.effectFixture.ordinaryMoraleInstanceIds) assert.equal(initialMorale.get(id)?.tapped, false)
    result.initial = { actorRevision: initial.revision, guestRevision: guestInitial.revision,
      actorGm: initial.gm, actorSandbox: initial.sandbox,
      guestGm: guestInitial.gm, guestSandbox: guestInitial.sandbox,
      sourceCardId: source.cardId, sourceCost: source.cost, targetBaseTroops: target.baseTroops,
      targetTroops: target.troops, ordinaryMoraleInstanceIds: ready.effectFixture.ordinaryMoraleInstanceIds }
    await actor.screenshot({ path: path.join(caseOutput, 'initial.png') })

    const playStart = result.transport.actor.length
    result.touches.push(await touch(actor,
      actor.locator(`.hand-card-wrap[data-tutorial-card-instance="${ready.effectFixture.sourceInstanceId}"]`).first(),
      'select Ankh Stele in hand'))
    const playButton = actor.locator(`[data-tutorial-action="play-card"][data-tutorial-card-instance="${ready.effectFixture.sourceInstanceId}"]`).filter({ visible: true }).first()
    result.playButtonGeometry = await assertControlInsideViewport(actor, playButton, 'Ankh Stele play button')
    result.touches.push(await touch(actor, playButton, 'play Ankh Stele'))
    result.acknowledgements.push(await waitAck(result.transport.actor, playStart,
      command => command.type === 'playCard' && command.cardInstanceId === ready.effectFixture.sourceInstanceId,
      initial.revision, 'play Ankh Stele from hand'))

    const isAnkhPaymentPrompt = prompt => prompt.playerIndex === 0
      && (prompt.kind === 'resource-payment' || prompt.data?.choiceMode === 'resource-payment')
      && prompt.data?.cardInstanceId === ready.effectFixture.sourceInstanceId
      && JSON.stringify([...prompt.validChoices].sort()) === JSON.stringify([
        ...ready.effectFixture.ordinaryMoraleInstanceIds,
        ready.effectFixture.targetInstanceId,
        'cancel',
      ].sort())
    const paymentState = await waitState(actor, state => !state.pending
      && state.prompts.some(isAnkhPaymentPrompt),
    'Ankh Stele ordinary morale payment')
    const paymentPrompt = paymentState.prompts.find(isAnkhPaymentPrompt)
    assert.equal(paymentPrompt.minChoose, ready.effectFixture.sourcePrintedCost)
    assert.equal(paymentPrompt.maxChoose, ready.effectFixture.sourcePrintedCost)
    assert(paymentPrompt.validChoices.includes(ready.effectFixture.targetInstanceId),
      'Tomb Guard must remain a legal virtual morale choice so the test can prove it was preserved')
    for (const id of ready.effectFixture.ordinaryMoraleInstanceIds)
      assert(paymentPrompt.validChoices.includes(id), `Ordinary morale missing from payment: ${id}`)
    const paymentStart = result.transport.actor.length
    const mobileMoraleTrigger = actor.locator('[data-player-index="0"] .mobile-morale-stack-trigger').filter({ visible: true }).first()
    let paymentScope = actor
    if (await mobileMoraleTrigger.count() && await mobileMoraleTrigger.isEnabled()) {
      result.touches.push(await touch(actor, mobileMoraleTrigger, 'open actual mobile morale picker'))
      const mobileMoraleOverlay = actor.locator('.mobile-morale-overlay').filter({ visible: true }).first()
      await mobileMoraleOverlay.waitFor({ state: 'visible' })
      paymentScope = mobileMoraleOverlay
    }
    for (const id of ready.effectFixture.ordinaryMoraleInstanceIds) {
      const choice = paymentScope.locator(`[data-tutorial-payment-resource="${id}"]`).filter({ visible: true }).first()
      await choice.waitFor({ state: 'visible' })
      await assertControlInsideViewport(actor, choice, `ordinary morale ${id}`)
      result.touches.push(await touch(actor, choice, `pay ordinary morale ${id}`))
    }
    const confirmPayment = actor.locator('[data-tutorial-action="confirm-payment"]').filter({ visible: true }).first()
    result.paymentButtonGeometry = await assertControlInsideViewport(actor, confirmPayment, 'confirm ordinary morale payment')
    result.touches.push(await touch(actor, confirmPayment, 'confirm ordinary morale payment'))
    const paymentAck = await waitAck(result.transport.actor, paymentStart,
      command => command.type === 'resolvePrompt' && command.promptId === paymentPrompt.promptId
        && ready.effectFixture.ordinaryMoraleInstanceIds.every(id => command.cardInstanceIds?.includes(id))
        && !command.cardInstanceIds?.includes(ready.effectFixture.targetInstanceId),
      paymentState.revision, 'pay only ordinary morale')
    result.acknowledgements.push(paymentAck)
    result.payment = { promptId: paymentPrompt.promptId, validChoices: paymentPrompt.validChoices,
      selected: paymentAck.command.cardInstanceIds, preservedVirtualMorale: ready.effectFixture.targetInstanceId }

    // A public enter effect declares its target before opening its response
    // window. Payment itself does not create a separate card-play window.
    const isAnkhTargetPrompt = prompt => prompt.playerIndex === 0
      && prompt.kind === 'field-legion'
      && prompt.sourceInstanceId === ready.effectFixture.sourceInstanceId
      && prompt.validChoices?.includes(ready.effectFixture.targetInstanceId)
    const targetState = await waitState(actor, state => !state.pending
      && state.prompts.some(isAnkhTargetPrompt),
    'mandatory own Tomb Guard target prompt')
    const targetPrompt = targetState.prompts.find(isAnkhTargetPrompt)
    const declineAliases = ['skip', 'mode:none', 'decline', 'refuse', 'cancel']
    assert(declineAliases.every(choice => !targetPrompt.validChoices.includes(choice)),
      'Mandatory target prompt exposed a decline alias')
    const targetControls = actor.locator('.board-target-controls').filter({ visible: true }).first()
    await targetControls.waitFor({ state: 'visible' })
    const oldSkipButtons = targetControls.getByRole('button', { name: /不发动|跳过|放弃|取消/ })
    assert.equal(await oldSkipButtons.count(), 0, 'Legacy skip/cancel UI remains on the mandatory target prompt')
    const minimize = targetControls.locator('.board-control-minimize').filter({ visible: true }).first()
    if (await minimize.count()) {
      result.touches.push(await touch(actor, minimize, 'minimize mandatory target controls'))
      const restore = actor.locator('.board-control-restore').filter({ visible: true }).first()
      await restore.waitFor({ state: 'visible' })
      await assertControlInsideViewport(actor, restore, 'restore mandatory target controls')
      result.touches.push(await touch(actor, restore, 'restore mandatory target controls'))
      const restored = await observe(actor)
      assert.equal(restored.revision, targetState.revision, 'Minimize and restore mutated authoritative state')
      assert(restored.prompts.some(prompt => prompt.promptId === targetPrompt.promptId),
        'Mandatory target prompt disappeared after minimize and restore')
    }
    await actor.screenshot({ path: path.join(caseOutput, 'mandatory-target.png') })
    const targetStart = result.transport.actor.length
    const targetCard = actor.locator(`[data-player-index="0"] [data-tutorial-card-instance="${ready.effectFixture.targetInstanceId}"]`).first()
    result.touches.push(await touch(actor, targetCard, 'select own public Tomb Guard'))
    const confirmTarget = actor.getByRole('button', { name: '确认目标', exact: true }).filter({ visible: true }).first()
    result.targetButtonGeometry = await assertControlInsideViewport(actor, confirmTarget, 'confirm mandatory target')
    result.touches.push(await touch(actor, confirmTarget, 'confirm mandatory target'))
    result.acknowledgements.push(await waitAck(result.transport.actor, targetStart,
      command => command.type === 'resolvePrompt' && command.promptId === targetPrompt.promptId
        && command.cardInstanceIds?.includes(ready.effectFixture.targetInstanceId),
      targetState.revision, 'confirm mandatory own target'))

    const effectPasses = await passAllCurrentResponses(actor, guest, result, 'enter-effect')
    assert.deepEqual(effectPasses.map(entry => entry.playerIndex).sort(), [0, 1],
      'Both players must explicitly pass the enter-effect response window')
    const settled = await waitState(actor, state => !state.pending && !state.prompts.length
      && state.self.relic?.instanceId === ready.effectFixture.sourceInstanceId
      && state.self.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)?.troops
        === result.initial.targetTroops + ready.effectFixture.expectedTargetTroopsDelta,
    'Ankh Stele mandatory target settlement', 20_000)
    const settledTarget = settled.self.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)
    const settledMorale = new Map(settled.self.morale.map(card => [card.instanceId, card]))
    for (const id of ready.effectFixture.ordinaryMoraleInstanceIds) assert.equal(settledMorale.get(id)?.tapped, true)
    assert.equal(settledTarget.tapped, false, 'Preserved Tomb Guard was incorrectly used as payment')
    assert.equal(settled.self.graveyard.some(card => card.instanceId === ready.effectFixture.targetInstanceId), false)
    const log = settled.recentEvents.find(event => String(event.text || '').includes('安卡神碑')
      && (String(event.text || '').includes('+2000') || String(event.text || '').includes('2000')))
    assert(log, 'Player projection must retain the public Ankh Stele +2000-related log')
    result.settlement = { revision: settled.revision, sourceZone: 'relic',
      sourceInstanceId: settled.self.relic.instanceId, targetBaseTroops: settledTarget.baseTroops,
      targetTroops: settledTarget.troops, deltaFromInitial: settledTarget.troops - result.initial.targetTroops,
      targetTapped: settledTarget.tapped,
      ordinaryMorale: ready.effectFixture.ordinaryMoraleInstanceIds.map(id => ({ instanceId: id, tapped: settledMorale.get(id)?.tapped })),
      responsePasses: result.responsePasses.map(({ stage, playerIndex, promptId }) => ({ stage, playerIndex, promptId })),
      log: log.text }
    assert.equal(effectPasses.length, 2)
    assert(result.touches.every(entry => entry.input === 'touchscreen'))
    assert.equal(result.errors.length, 0)
    await actor.screenshot({ path: path.join(caseOutput, 'settled.png') })
    result.status = 'PASS'
    result.ready = { roomCode: ready.effectFixture.roomCode, matchId: ready.effectFixture.matchId,
      fixtureKind: ready.effectFixture.kind, syntheticHistoryMatches: ready.syntheticHistoryMatches }
  } catch (error) {
    result.failure = { name: error?.name || 'Error', message: String(error?.message || error).slice(0, 2000) }
    if (actorContext) {
      try {
        const pages = actorContext.pages()
        const gamePage = pages.find(page => /\/game(?:[?#]|$)/.test(page.url()))
        if (gamePage) await gamePage.screenshot({ path: path.join(caseOutput, 'failure.png') })
      } catch (captureError) { result.errors.push(`failure-capture:${String(captureError?.message || captureError)}`) }
    }
    throw error
  } finally {
    await closeOwnedUi(actorContext, guestContext, vite, result)
    try {
      fs.writeFileSync(path.join(root, 'stop'), '', { flag: 'a' })
      const stopped = await Promise.race([host.done, delay(15_000).then(() => null)])
      if (!stopped) {
        host.kill(); result.forcedStop = true
        assert(await Promise.race([host.done, delay(5000).then(() => null)]), 'Owned host did not exit after termination')
      }
      result.hostExit = stopped || await host.done
      result.gracefulHostStop = fs.existsSync(path.join(root, 'stopped.json'))
      assert.equal(result.forcedStop, undefined, 'Owned fixture host required forced termination')
      assert.equal(result.hostExit.code, 0, 'Owned fixture host did not exit cleanly')
      assert.equal(result.gracefulHostStop, true, 'Owned fixture host missed stopped receipt')
    } catch (error) { result.cleanupErrors.push(`host: ${String(error?.message || error)}`) }
    try {
      if (canRemoveMandatoryOwnedRoot(result)) result.ownedRootUsage = removeOwnedRoot(root, runId)
      else result.ownedRootRetained = true
    }
    catch (error) { result.cleanupErrors.push(`owned root: ${String(error?.message || error)}`) }
    if (result.cleanupErrors.length) result.status = 'FAIL'
    fs.writeFileSync(path.join(caseOutput, 'case.json'), JSON.stringify(result, null, 2), { flag: 'wx' })
    assert.equal(result.cleanupErrors.length, 0, result.cleanupErrors.join('; '))
  }
  return result
}

try {
  for (const viewport of viewports) report.cases.push(await runCase(viewport))
  assert(report.cases.every(item => item.status === 'PASS'))
  report.status = 'PASS'
} catch (error) {
  report.status = 'FAIL'
  report.error = { name: error?.name || 'Error', message: String(error?.message || error).slice(0, 500) }
  process.exitCode = 1
} finally {
  report.sourceAfter = {
    fixtureHost: sha256(fs.readFileSync(path.join(repo, 'ops/qa/LocalPressureHost/Program.cs'))),
    runner: sha256(fs.readFileSync(import.meta.filename)),
  }
  if (JSON.stringify(report.sourceAfter) !== JSON.stringify(report.sourceBefore)) {
    report.status = 'FAIL'
    report.error = { name: 'SourceDrift', message: 'Fixture or runner source changed during acceptance' }
    process.exitCode = 1
  }
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
}
