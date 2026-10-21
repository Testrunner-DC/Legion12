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
const expectedServerSha = '58FB53CB440704B3632CF137A2804241B568494C65333DFCFD80D72BF33503B1'
for (const value of [cacheParent, output, accepted, hostAssembly])
  assert(value && path.isAbsolute(value), 'Explicit absolute governed roots and host DLL are required')
assert(!fs.existsSync(path.join(output, 'report.json')), 'Refusing to overwrite prior evidence')

const sha256 = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
assert.equal(sha256(fs.readFileSync(path.join(accepted, 'GrandUMIServer.dll'))), expectedServerSha,
  'Accepted server assembly drifted from the approved 58FB build')
execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  `. '${repo}/ops/windows/Initialize-L12BuildEnvironment.ps1' -CacheRoot $env:L12_MOBILE_EFFECT_CACHE -DependencyCacheRoot 'D:/GPT/Legion12/cache/primary' | Out-Null; `
  + `. '${repo}/ops/windows/L12GeneratedStorage.ps1'; `
  + `foreach($p in @($env:L12_MOBILE_EFFECT_CACHE,$env:L12_MOBILE_EFFECT_OUTPUT,$env:L12_ACCEPTED_SERVER_ROOT,(Split-Path -Parent $env:L12_MOBILE_EFFECT_HOST_DLL),$env:TEMP)){`
  + `$null=Assert-L12GeneratedPath -Path $p -Purpose 'mobile public target full chain'}`],
{ env: process.env, stdio: 'pipe', windowsHide: true })
fs.mkdirSync(cacheParent, { recursive: true })
fs.mkdirSync(output, { recursive: true })

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
  scope: 'Local test-only ordinary-room Merlin public-target chain; no deployment or product mutation.',
  acceptedServerSha: expectedServerSha,
  hostAssemblySha: sha256(fs.readFileSync(hostAssembly)),
  sourceHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).trim(),
  sourceDirty: execFileSync('git', ['status', '--short'], { cwd: repo, encoding: 'utf8' }).trim().split(/\r?\n/).filter(Boolean),
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
  assert(point, `No visible unobstructed touch point: ${label}`)
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
      L12_LOCAL_EFFECT_FIXTURE: 'merlin-public-target-v1', L12_LOCAL_EFFECT_PASSWORD: password,
      L12_ALLOWED_ORIGINS: `http://127.0.0.1:${uiPort}` },
  }, path.join(caseOutput, 'host.log'))
  let vite, actorContext, guestContext
  const result = { name: viewport.name, viewport: { width: viewport.width, height: viewport.height },
    runId, hostPid: host.pid, touches: [], transport: { actor: [], guest: [] }, acknowledgements: [], errors: [] }
  try {
    const ready = await waitJson(path.join(root, 'ready.json'), host)
    assert.equal(ready.pid, host.pid)
    assert.equal(ready.serverSha256, expectedServerSha)
    assert.equal(ready.syntheticHistoryMatches, 0)
    assert.equal(ready.effectFixture?.kind, 'merlin-public-target-v1')
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
    result.initial = { actorRevision: initial.revision, guestRevision: guestInitial.revision,
      actorGm: initial.gm, actorSandbox: initial.sandbox,
      guestGm: guestInitial.gm, guestSandbox: guestInitial.sandbox,
      actorRunes: initial.self.specialZones.runes,
      targetTroops: initial.enemy.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)?.troops }
    await actor.screenshot({ path: path.join(caseOutput, 'initial.png') })

    const activateStart = result.transport.actor.length
    result.touches.push(await touch(actor,
      actor.locator(`[data-player-index="0"] [data-tutorial-card-instance="${ready.effectFixture.sourceInstanceId}"]`).first(),
      'select Merlin'))
    result.touches.push(await touch(actor, actor.getByRole('button', { name: '发动', exact: true }).filter({ visible: true }).first(),
      'activate Merlin'))
    result.acknowledgements.push(await waitAck(result.transport.actor, activateStart,
      command => command.type === 'activateAbility' && command.cardInstanceId === ready.effectFixture.sourceInstanceId,
      initial.revision, 'activate Merlin'))

    const modeState = await waitState(actor, state => !state.pending
      && state.prompts.some(prompt => prompt.playerIndex === 0 && prompt.validChoices?.includes('mode:debuff')),
    'Merlin mode prompt')
    const modeStart = result.transport.actor.length
    const mode = await selectPromptChoice(actor, 'mode:debuff', result.touches)
    result.acknowledgements.push(await waitAck(result.transport.actor, modeStart,
      command => command.type === 'resolvePrompt' && command.promptId === mode.prompt.promptId
        && command.cardInstanceIds?.includes('mode:debuff'), modeState.revision, 'choose Merlin debuff mode'))

    const targetState = await waitState(actor, state => !state.pending
      && state.prompts.some(prompt => prompt.playerIndex === 0
        && prompt.validChoices?.includes(ready.effectFixture.targetInstanceId)), 'public enemy target prompt')
    const targetPrompt = targetState.prompts.find(prompt => prompt.playerIndex === 0
      && prompt.validChoices?.includes(ready.effectFixture.targetInstanceId))
    const targetStart = result.transport.actor.length
    result.touches.push(await touch(actor,
      actor.locator(`[data-player-index="1"] [data-tutorial-card-instance="${ready.effectFixture.targetInstanceId}"]`).first(),
      'select public enemy legion'))
    result.touches.push(await touch(actor, actor.getByRole('button', { name: '确认目标', exact: true }).filter({ visible: true }).first(),
      'confirm public enemy target'))
    result.acknowledgements.push(await waitAck(result.transport.actor, targetStart,
      command => command.type === 'resolvePrompt' && command.promptId === targetPrompt.promptId
        && command.cardInstanceIds?.includes(ready.effectFixture.targetInstanceId),
      targetState.revision, 'confirm public enemy target'))

    const responseState = await waitState(guest, state => !state.pending
      && state.prompts.some(prompt => prompt.playerIndex === 1 && prompt.kind === 'response'
        && prompt.validChoices?.includes(ready.effectFixture.responseInstanceId)
        && prompt.validChoices?.includes('pass')), 'opponent response prompt')
    const responseStart = result.transport.guest.length
    const response = await selectPromptChoice(guest, 'pass', result.touches)
    result.acknowledgements.push(await waitAck(result.transport.guest, responseStart,
      command => command.type === 'resolvePrompt' && command.promptId === response.prompt.promptId
        && (command.choice === 'pass' || command.cardInstanceIds?.includes('pass')),
      responseState.revision, 'opponent pass response'))

    const settled = await waitState(actor, state => !state.pending && !state.prompts.length
      && state.self.specialZones.runes === 0
      && state.self.field.flat().find(card => card?.instanceId === ready.effectFixture.sourceInstanceId)?.tapped === true
      && state.enemy.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)?.troops
        === result.initial.targetTroops + ready.effectFixture.expectedTargetTroopsDelta,
    'Merlin settlement', 20_000)
    const merlin = settled.self.field.flat().find(card => card?.instanceId === ready.effectFixture.sourceInstanceId)
    const target = settled.enemy.field.flat().find(card => card?.instanceId === ready.effectFixture.targetInstanceId)
    const log = settled.recentEvents.find(event => String(event.text || '').includes('梅林使〈')
      && String(event.text || '').includes('-3000'))
    assert(log, 'Player projection must retain the Merlin -3000 settlement log')
    result.settlement = { revision: settled.revision, runes: settled.self.specialZones.runes,
      merlinTapped: merlin.tapped, targetBaseTroops: target.baseTroops,
      targetTroops: target.troops, deltaFromInitial: target.troops - result.initial.targetTroops,
      log: log.text }
    assert.equal(result.acknowledgements.length, 4)
    assert(result.touches.every(entry => entry.input === 'touchscreen'))
    assert.equal(result.errors.length, 0)
    await actor.screenshot({ path: path.join(caseOutput, 'settled.png') })
    result.status = 'PASS'
    result.ready = { roomCode: ready.effectFixture.roomCode, matchId: ready.effectFixture.matchId,
      fixtureKind: ready.effectFixture.kind, syntheticHistoryMatches: ready.syntheticHistoryMatches }
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
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2), { flag: 'wx' })
}
