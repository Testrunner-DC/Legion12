// Run only after the authorized testrun release. This is a one-scene smoke,
// not a replacement for the local, three-viewport, complete-course acceptance.
// Required: EXPECTED_COMMIT=<full lowercase SHA>, L12_TUTORIAL_DEPLOYED_OUTPUT=<fresh governed D directory>.
import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { execFileSync } from 'node:child_process'

const BASE = 'https://legion-12.com/testrun/'
const expected = process.env.EXPECTED_COMMIT || ''
assert.match(expected, /^[a-f0-9]{40}$/, 'EXPECTED_COMMIT must be a full lowercase Git SHA')
const output = process.env.L12_TUTORIAL_DEPLOYED_OUTPUT || ''
assert.match(output, /^D:[\\/]/i, 'An explicit governed D output is required')
assert(!fs.existsSync(output), 'Use a fresh output directory; prior evidence is never overwritten')
const repo = path.resolve(import.meta.dirname, '../..')
const pwsh = process.env.L12_PWSH || 'pwsh'
const guard = paths => execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command',
  `. '${path.join(repo, 'ops/windows/L12GeneratedStorage.ps1').replaceAll("'", "''")}'; `
  + '$paths=ConvertFrom-Json $env:L12_DEPLOYED_GUARD_PATHS; foreach($p in $paths){$null=Assert-L12GeneratedPath -Path $p -Purpose "public testrun tutorial smoke"}'],
{ env: { ...process.env, L12_DEPLOYED_GUARD_PATHS: JSON.stringify(paths) }, stdio: 'pipe' })
guard([output])
fs.mkdirSync(output, { recursive: true })
const temporary = path.join(output, 'browser-temporary')
guard([temporary])
fs.mkdirSync(temporary)
// Playwright creates its own temporary profile in the Node parent, before
// launching the browser; child-process env alone would be too late.
process.env.TEMP = temporary
process.env.TMP = temporary
process.env.TMPDIR = temporary
const require = createRequire(new URL('../package.json', import.meta.url))
const { chromium } = require(process.env.L12_PLAYWRIGHT_MODULE
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const report = { status: 'RUNNING', expectedCommit: expected, base: BASE, startedAt: new Date().toISOString(),
  boundary: 'One synthetic ordinary account, compiled testrun UI, actual deployment/payment/roomLeft handoff only. No source imports, injected progress, GM, production API, maintenance, or full-course claim. Shared same-origin /card-assets/ is read-only.',
  syntheticAccountCreated: false, accountRetained: false, pageErrorCount: 0, consoleErrorCount: 0,
  rejectedNetwork: [], transport: [], lessonsObserved: [], officialImages: [], browserExited: false }
let browserServer, browser, context, page, browserProcess, stage = 'health-before', completed = false
let failureCode = '', ownedRoom = false, registered = false
const pause = ms => new Promise(resolve => setTimeout(resolve, ms))
const wait = async (test, label, timeout = 20_000) => {
  const until = Date.now() + timeout
  while (Date.now() < until) { if (await test()) return; await pause(100) }
  throw new Error(label)
}
function allowed(url, method = 'GET') {
  if (url === 'about:blank' || /^(data|blob):/.test(url)) return true
  const value = new URL(url)
  if (!['https:', 'wss:'].includes(value.protocol) || value.hostname !== 'legion-12.com' || value.port) return false
  if (value.pathname.startsWith('/testrun/')) return true
  // The shipped card component deliberately shares the immutable official pool.
  return value.protocol === 'https:' && method === 'GET' && value.pathname.startsWith('/card-assets/')
}
async function health() {
  const response = await fetch(BASE + 'health', { redirect: 'error', signal: AbortSignal.timeout(15_000),
    headers: { 'cache-control': 'no-cache' } })
  assert.equal(response.status, 200, 'test health must return 200')
  const value = await response.json()
  assert.equal(value.service, 'twelve-legions')
  assert.equal(value.serverVersion, expected)
  assert.equal(value.engineVersion, 'l12-engine/' + expected)
  assert.equal(value.status, 'ok')
  assert.equal(value.maintenance, false)
  return { serverVersion: value.serverVersion, engineVersion: value.engineVersion, maintenance: false }
}
async function click(locator) {
  const target = locator.filter({ visible: true }).first()
  await target.waitFor({ state: 'visible' })
  await target.scrollIntoViewIfNeeded()
  await wait(() => target.isEnabled(), 'control not enabled')
  await target.click() // No force, DOM dispatch, or bypass of the real pointer target.
}
async function images(selector) {
  const locator = page.locator(selector)
  await wait(async () => locator.evaluateAll(nodes => nodes.length > 0 && nodes.every(image => {
    const url = new URL(image.currentSrc || image.src, location.href)
    return image.complete && image.naturalWidth > 0 && url.origin === 'https://legion-12.com'
      && /^\/(testrun\/)?card-assets\//.test(url.pathname) && !/card-back|placeholder/.test(url.pathname)
  })), 'official card image did not load', 30_000)
  const observations = await locator.evaluateAll(nodes => nodes.map(image => ({
    pathname: new URL(image.currentSrc || image.src, location.href).pathname,
    width: image.naturalWidth, height: image.naturalHeight,
  })))
  report.officialImages.push(...observations)
}
async function readGuidance() {
  const guide = page.locator('[data-ui-contract="tutorial-authoritative-guidance"]')
  for (let budget = 0; budget < 12; budget++) {
    await guide.waitFor({ state: 'visible' })
    if (await guide.getAttribute('data-tutorial-reading') !== 'true') return
    const before = await guide.getAttribute('data-tutorial-guide-id')
    const cardImage = page.locator('.tutorial-detail-mask .l12-card-image__img')
    if (await cardImage.count()) await images('.tutorial-detail-mask .l12-card-image__img')
    await click(guide.getByRole('button', { name: '下一步', exact: true }))
    await page.waitForFunction(id => {
      const node = document.querySelector('[data-ui-contract="tutorial-authoritative-guidance"]')
      return node?.getAttribute('data-tutorial-guide-id') !== id || node?.getAttribute('data-tutorial-reading') !== 'true'
    }, before)
  }
  throw new Error('reading exceeded bounded budget')
}
function receive(raw) {
  let packet
  try { packet = JSON.parse(String(raw)) } catch { return }
  const message = packet.type === 'gameStateCardFrameV1' ? packet.payload : packet
  if (!message || typeof message !== 'object') return
  if (message.type === 'session') {
    report.session = { ordinaryIdentityBound: Boolean(message.accountId && message.sessionId),
      tutorialAvailable: message.tutorialAvailable === true, requestIds: message.capabilities?.requestIds === true }
  }
  const tutorial = message.tutorial
  if (tutorial && typeof tutorial.lessonId === 'string') {
    if (!report.lessonsObserved.some(item => item.lessonId === tutorial.lessonId && item.status === tutorial.status))
      report.lessonsObserved.push({ lessonId: tutorial.lessonId, version: tutorial.version, status: tutorial.status })
    if (tutorial.lessonId === 'deploy' && tutorial.status === 'completed') report.authoritativeDeployCompleted = true
  }
  if (message.type === 'roomState') {
    ownedRoom = Boolean(message.started && tutorial)
    if (tutorial?.lessonId === 'move') report.authoritativeNextRoom = true
  }
  if (message.type === 'roomLeft') { ownedRoom = false; report.roomLeftAcknowledgements = (report.roomLeftAcknowledgements || 0) + 1 }
  if (['tutorialRejected', 'tutorialRestartRequired', 'actionRejected', 'authenticationRequired'].includes(message.type))
    failureCode = message.type // Never persist server text, tokens, account IDs, or raw frames.
}
const deadline = setTimeout(() => { failureCode = 'overall-deadline'; void browserServer?.kill() }, 240_000)
try {
  report.healthBefore = await health()
  stage = 'browser-launch'
  browserServer = await chromium.launchServer({ channel: 'msedge', headless: true,
    downloadsPath: temporary, env: { ...process.env, TEMP: temporary, TMP: temporary, TMPDIR: temporary },
    args: ['--disable-breakpad', '--disable-crash-reporter'] })
  browserProcess = browserServer.process()
  report.browserPid = browserProcess.pid
  const profileArg = browserProcess.spawnargs.find(argument => argument.startsWith('--user-data-dir='))
  assert(profileArg, 'Owned browser profile must be identifiable')
  const profile = profileArg.slice('--user-data-dir='.length)
  guard([profile])
  assert(path.relative(temporary, profile) && !path.relative(temporary, profile).startsWith('..'), 'Browser profile must stay under the guarded temporary root')
  report.profilePath = profile
  browser = await chromium.connect(browserServer.wsEndpoint())
  // An incognito context keeps the ordinary consumer's login/localStorage in
  // memory. No persistent context, storageState, tracing, HAR, or video is saved.
  context = await browser.newContext({ viewport: { width: 1366, height: 900 }, serviceWorkers: 'block', acceptDownloads: false })
  await context.route('**/*', route => {
    const request = route.request()
    if (allowed(request.url(), request.method())) return route.continue()
    report.rejectedNetwork.push({ method: request.method(), pathname: new URL(request.url()).pathname })
    return route.abort('blockedbyclient')
  })
  await context.routeWebSocket('**/*', socket => {
    if (socket.url() !== 'wss://legion-12.com/testrun/ws') {
      report.rejectedNetwork.push({ method: 'WS', pathname: new URL(socket.url()).pathname })
      socket.close(); return
    }
    socket.connectToServer() // Transparent; never inject or replace a frame.
  })
  page = await context.newPage()
  page.setDefaultTimeout(15_000)
  page.on('pageerror', () => { report.pageErrorCount++ })
  page.on('console', item => { if (item.type() === 'error') report.consoleErrorCount++ })
  page.on('dialog', dialog => dialog.dismiss())
  page.on('websocket', socket => {
    socket.on('framereceived', frame => receive(frame.payload))
    socket.on('framesent', frame => {
      let value
      try { value = JSON.parse(String(frame.payload)) } catch { return }
      if (value.type === 'createTutorial') report.transport.push({ type: value.type, lessonId: value.lessonId })
      if (value.type === 'leaveRoom') report.transport.push({ type: value.type })
      if (value.type === 'gameAction') report.transport.push({ type: value.type, command: value.command?.type,
        paymentChoices: value.command?.cardInstanceIds?.length ?? null })
    })
  })
  stage = 'ordinary-registration'
  await page.goto(BASE + 'me', { waitUntil: 'domcontentloaded' })
  assert(new URL(page.url()).href.startsWith(BASE))
  report.compiledEntry = await page.locator('script[type="module"][src]').evaluateAll(nodes => nodes.map(node => new URL(node.src).pathname))
  assert(report.compiledEntry.length > 0 && report.compiledEntry.every(entry => /^\/testrun\/assets\/.+\.js$/.test(entry)),
    'Smoke must exercise compiled testrun assets, never a source/dev server')
  await click(page.locator('.auth-tabs').getByRole('button', { name: '注册', exact: true }))
  const form = page.locator('form.auth-form')
  const username = 'TQA' + crypto.randomBytes(4).toString('hex')
  let password = 'Qa!' + crypto.randomBytes(24).toString('base64url')
  await form.getByLabel('用户名', { exact: true }).fill(username)
  await form.getByLabel('密码', { exact: true }).fill(password)
  const [registeredResponse] = await Promise.all([
    page.waitForResponse(response => response.url() === BASE + 'api/auth/register' && response.request().method() === 'POST'),
    click(form.getByRole('button', { name: '建立账号', exact: true })),
  ])
  assert.equal(registeredResponse.status(), 200, 'ordinary registration must succeed without retries')
  password = ''
  registered = true
  report.syntheticAccountCreated = true; report.accountRetained = true
  await form.waitFor({ state: 'hidden' })
  stage = 'compiled-tutorial-entry'
  await page.goto(BASE + 'battle/tutorial', { waitUntil: 'domcontentloaded' })
  await wait(() => Boolean(report.session?.ordinaryIdentityBound && report.session?.tutorialAvailable), 'ordinary WS tutorial capability')
  const intro = page.locator('[data-tutorial-introduction]')
  const titles = []
  for (let index = 0; index < 5; index++) {
    await intro.waitFor({ state: 'visible' })
    titles.push(await intro.locator('h2').innerText())
    if (await intro.locator('.intro-card-native-image,.l12-card-image__img').count())
      await images('[data-tutorial-introduction] .intro-card-native-image,[data-tutorial-introduction] .l12-card-image__img')
    await click(intro.locator('.conversation-choices').getByRole('button', { name: index === 4 ? '进入战场' : '下一步', exact: true }))
  }
  assert.equal(new Set(titles).size, 5)
  report.introductionSteps = titles.length
  await page.waitForURL(BASE + 'game')
  await readGuidance()
  const guide = page.locator('[data-ui-contract="tutorial-authoritative-guidance"]')
  assert.equal(await guide.getAttribute('data-tutorial-scene'), 'deploy')
  // Resolve the displayed practice identity once, then bind every operation to
  // that exact instance. No private deck inspection or game-state injection.
  const hand = page.locator('.hand-card-wrap[data-tutorial-card-instance]')
  let practice
  for (let index = 0; index < await hand.count(); index++) {
    const candidate = hand.nth(index)
    if (await candidate.locator('img[alt="食铁兽"]').count()) { practice = candidate; break }
  }
  assert(practice, 'The real known practice card must be rendered')
  const instance = await practice.getAttribute('data-tutorial-card-instance')
  assert(instance && instance.length <= 128)
  await images('.hand-card-wrap[data-tutorial-card-instance="' + instance + '"] .l12-card-image__img')
  stage = 'actual-deploy-and-payment'
  await click(practice)
  const playAction = page.locator('[data-tutorial-action="play-card"][data-tutorial-card-instance="' + instance + '"]').filter({ visible: true })
  // Ordinary desktop legion selection may enter placement directly; the
  // player's existing confirmation preference determines whether this exists.
  if (await playAction.count()) await click(playAction)
  await click(page.locator('[data-player-index="0"] .formation-slot.available'))
  for (let budget = 0; budget < 12; budget++) {
    if (report.authoritativeNextRoom) break
    const payment = page.locator('[data-tutorial-action="confirm-payment"]').filter({ visible: true }).first()
    if (await payment.count()) {
      const choices = page.locator('[data-player-index="0"] .morale-orb.payable:not(.selected)')
      if (!await payment.isEnabled() && await choices.count()) await click(choices.last())
      else if (await payment.isEnabled()) await click(payment)
    }
    await pause(200)
  }
  await wait(() => Boolean(report.authoritativeNextRoom), 'real automatic next room', 30_000)
  await wait(async () => await guide.getAttribute('data-tutorial-scene') === 'move', 'compiled next-scene consumer')
  assert(report.transport.some(item => item.type === 'gameAction' && item.command === 'playCard'))
  assert.equal(report.transport.filter(item => item.type === 'createTutorial' && item.lessonId === 'deploy').length, 1)
  assert.equal(report.transport.filter(item => item.type === 'createTutorial' && item.lessonId === 'move').length, 1)
  assert.equal(report.roomLeftAcknowledgements, 1)
  const learned = await page.evaluate(() => {
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index)
      if (key?.startsWith('l12-tutorial-course:v2:account:')) {
        const value = JSON.parse(localStorage.getItem(key))
        return { introductionRead: value.introductionRead === true, reviewedScenes: value.reviewedScenes }
      }
    }
    return null
  })
  assert.deepEqual(learned, { introductionRead: true, reviewedScenes: ['deploy'] })
  report.progress = learned
  report.paymentScope = 'A real playCard was accepted, authoritative deploy progress persisted by the ordinary consumer, and a single roomLeft opened move; engine payment is not bypassed or separately simulated.'
  await page.screenshot({ path: path.join(output, 'deployed-first-scene-next-room.png') })
  stage = 'owned-room-exit'
  await click(page.getByRole('button', { name: '返回教学主线', exact: true }))
  await page.waitForURL(BASE + 'battle/tutorial')
  await wait(() => !ownedRoom && report.roomLeftAcknowledgements === 2, 'own smoke roomLeft cleanup')
  assert.equal(report.pageErrorCount, 0)
  assert.equal(report.consoleErrorCount, 0)
  assert.deepEqual(report.rejectedNetwork, [])
  assert.equal(failureCode, '')
  report.healthAfter = await health()
  completed = true
} catch {
  // Do not serialize exceptions or Playwright call logs: a failed fill/request
  // can contain credentials. The stage and fixed error category are sufficient.
  report.failure = { stage, code: failureCode || 'assertion-or-ui-deadline' }
  process.exitCode = 1
} finally {
  clearTimeout(deadline)
  if (ownedRoom && page && !page.isClosed()) {
    try { await click(page.getByRole('button', { name: '返回教学主线', exact: true })); await wait(() => !ownedRoom, 'cleanup roomLeft', 8_000) }
    catch { report.roomCleanupIncomplete = true }
  }
  try { await context?.close() } catch { report.contextCloseFailed = true }
  try { await browser?.close() } catch { /* launchServer below still owns shutdown */ }
  if (browserServer) {
    const closed = await Promise.race([browserServer.close().then(() => true, () => false), pause(10_000).then(() => false)])
    if (!closed) { try { await browserServer.kill() } catch { report.browserKillFailed = true } }
    await wait(() => browserProcess.exitCode !== null || browserProcess.signalCode !== null, 'owned browser exit', 10_000).catch(() => {})
    report.browserExited = browserProcess.exitCode !== null || browserProcess.signalCode !== null
  }
  report.accountRetained = registered // No account deletion or privileged cleanup.
  report.finishedAt = new Date().toISOString()
  report.status = completed && report.browserExited && !report.roomCleanupIncomplete ? 'GREEN' : 'FAILED'
  if (report.status !== 'GREEN') process.exitCode = 1
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' })
  console.log(`Deployed tutorial smoke: ${report.status}; report: ${path.join(output, 'report.json')}`)
}
