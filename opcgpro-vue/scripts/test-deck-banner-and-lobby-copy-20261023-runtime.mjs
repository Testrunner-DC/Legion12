import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import net from 'node:net'
import { spawn, execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import sharp from 'sharp'

const repo = path.resolve(import.meta.dirname, '../..'), front = path.join(repo, 'opcgpro-vue')
const out = process.env.L12_QA_OUT, cache = process.env.L12_WORK_CACHE, hot = process.env.L12_MASTER_ART_HOT
const playerFlowOnly = process.env.L12_MASTER_PLAYER_FLOW_ONLY === '1'
const assetRoot = process.env.L12_MASTER_ART_ASSETS
assert(out && cache && hot && [out, cache, hot].every(path.isAbsolute), 'Explicit absolute governed paths required')
const expectedDll = '908E146B9A92D52A0FA6E3B5CAD8260A07443515D82E35AA528204A41365321B'
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
assert.equal(sha(fs.readFileSync(path.join(hot, 'GrandUMIServer.dll'))), expectedDll, 'Only the Main-approved 2eda hot assembly')
const env = { ...process.env, L12_PROBE_REPO: repo, L12_PROBE_OUT: out, L12_PROBE_CACHE: cache }
execFileSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
  ['-NoProfile', '-NonInteractive', '-Command', '. "$env:L12_PROBE_REPO/ops/windows/L12GeneratedStorage.ps1"; '
    + '@($env:L12_PROBE_OUT,$env:L12_PROBE_CACHE) | ForEach-Object {$null=Assert-L12GeneratedPath -Path $_ -Purpose "master alternate-art isolated acceptance"}'],
  { env, windowsHide: true, stdio: 'pipe' })
assert(!fs.existsSync(out) || fs.readdirSync(out).length === 0, 'Never overwrite previous acceptance evidence')
fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
const host = path.join(cache, 'host')
const resumeFrom = process.env.L12_MASTER_QA_RESUME_REPORT
const prior = resumeFrom ? JSON.parse(fs.readFileSync(resumeFrom, 'utf8')) : null
if (prior) {
  assert.equal(prior.backendSha256, expectedDll)
  for (const file of productSourceFiles()) assert.equal(prior.sourceAfter[file], sha(fs.readFileSync(path.join(front, file))))
  const ownership = JSON.parse(fs.readFileSync(path.join(host, 'owned-gallery-host.json'), 'utf8'))
  assert.equal(ownership.syntheticRuntimeOnly, true); assert.equal(ownership.backendSha256, expectedDll)
  assert.equal(sha(fs.readFileSync(path.join(host, 'GrandUMIServer.dll'))), expectedDll)
} else {
  assert(!fs.existsSync(host), 'A fresh owned host is required; never reuse or delete an unknown runtime')
  fs.mkdirSync(host)
}
function productSourceFiles() { return ['src/l12/L12DeckEditor.vue', 'src/l12/site/BattleHubPage.vue'] }
const copied = []
const copy = relative => {
  const target = path.join(host, relative); fs.mkdirSync(path.dirname(target), { recursive: true })
  fs.copyFileSync(path.join(hot, relative), target); copied.push(relative)
}
if (!prior) {
  for (const file of fs.readdirSync(hot)) if (/\.dll$|\.deps\.json$|\.runtimeconfig\.json$/i.test(file)) copy(file)
  for (const file of fs.readdirSync(path.join(hot, 'TwelveLegions/Data'))) if (file.endsWith('.json')) copy(path.join('TwelveLegions/Data', file))
  for (const file of fs.readdirSync(path.join(hot, 'runtimes/win-x64/native'))) if (file.endsWith('.dll')) copy(path.join('runtimes/win-x64/native', file))
  assert(!fs.existsSync(path.join(host, 'runtime')), 'No database, WAL or old runtime may enter the fresh host')
  fs.writeFileSync(path.join(host, 'owned-gallery-host.json'), JSON.stringify({ suite: 'gallery-real-http-v1', syntheticRuntimeOnly: true,
    backendSha256: expectedDll, source: hot, preparation: 'new synthetic host, not an earlier acceptance claim', copied }, null, 2))
}
const sourceFiles = ['src/l12/L12DeckEditor.vue', 'src/l12/site/BattleHubPage.vue', 'scripts/test-deck-banner-and-lobby-copy-20261023-runtime.mjs']
const sourceHashes = () => Object.fromEntries(sourceFiles.map(file => [file, sha(fs.readFileSync(path.join(front, file)))]))
const report = { status: 'running', stage: 'start', backendSha256: expectedDll, sourceBefore: sourceHashes(), cases: [],
  requests: [], errors: [], cleanup: {}, limits: ['Synthetic local accounts and fresh SQLite only; no online services.',
    'Chromium touch emulation is not a physical phone.', 'One explicit synthetic season restriction is injected only into browser policy display responses.'] }
const assetFiles = new Map()
if (playerFlowOnly) {
  assert(assetRoot && path.isAbsolute(assetRoot))
  const manifestBytes = fs.readFileSync(path.join(assetRoot, 'card-assets.manifest.json'))
  assert.equal(sha(manifestBytes), 'BE756F5E72647FEF7B128665296C305AE7BE213743B7B0195AECCC09E17990CD')
  const manifest = JSON.parse(manifestBytes), item = manifest.cards['S01-01M1A']
  assert.equal(item.baseCardId, 'S01-01M1'); assert.equal(item.cardType, 'master')
  assert.equal(item.contentHash, '3fc3fc13fec9558bd7efb15d69a8042f6930f0843eeaacc0e4738cde2defde86')
  assetFiles.set('/card-assets/card-assets.manifest.json', { bytes: manifestBytes, type: 'application/json' })
  report.trustedAssets = { manifestSha256: sha(manifestBytes), cardId: item.cardId, contentHash: item.contentHash, files: [] }
  for (const [key, relative] of Object.entries(item.variants)) {
    assert(relative.startsWith('cards/20260912-batch308/S01-01M1A/3fc3fc13fec9558bd7ef/'))
    const bytes = fs.readFileSync(path.join(assetRoot, relative)); assert.equal(bytes.length, item.bytes[key])
    assetFiles.set('/card-assets/' + relative, { bytes, type: relative.endsWith('.avif') ? 'image/avif' : 'image/webp' })
    report.trustedAssets.files.push({ relative, sha256: sha(bytes), bytes: bytes.length })
  }
  report.limits = ['Pure local player-button chain; no online services or rights-product changes.', 'Only the named real master-art directory is served from the trusted manifest; other card images may fall back.']
}
if (prior) report.reusedEvidence = { path: resumeFrom, sha256: sha(fs.readFileSync(resumeFrom)), cases: prior.cases, earlier: prior.reusedEvidence }
const previouslyPassed = name => {
  for (let proof = report.reusedEvidence; proof; proof = proof.earlier)
    if (proof.cases.some(test => test.name === name && test.status === 'passed')) return true
  return false
}
const log = [], children = []
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const reserve = net.createServer(); await new Promise(resolve => reserve.listen(0, '127.0.0.1', resolve))
const port = reserve.address().port; await new Promise(resolve => reserve.close(resolve))
const origin = `http://127.0.0.1:${port}`
let backend, vite, browser, latestPage
const password = 'SyntheticOnly123!'
const api = async (method, route, token, body) => {
  assert(route.startsWith('/api/'))
  const response = await fetch(origin + route, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}) },
  body: body ? body instanceof FormData ? body : JSON.stringify(body) : undefined })
  const text = await response.text(); report.requests.push({ method, path: route, status: response.status })
  assert(response.ok, `${method} ${route}: ${response.status} ${text.slice(0, 160)}`)
  return text ? JSON.parse(text) : null
}
const check = async (name, work) => { report.stage = name; const details = await work(); report.cases.push({ name, status: 'passed', ...details }); console.log('PASS ' + name) }
const entry = `
import {createApp} from 'vue'; import {createRouter,createMemoryHistory} from 'vue-router';
import App from '/src/App.vue';import Editor from '/src/l12/L12DeckEditor.vue';import Hub from '/src/l12/site/BattleHubPage.vue';
import Game from '/src/l12/GamePage.vue';import * as p from '/src/l12/platform';import * as n from '/src/l12/net';import * as d from '/src/l12/decks';import '/src/style.css';
const q=new URLSearchParams(location.search); n.l12State.endpoint='ws://'+location.host+'/ws';
if(p.platformState.token)await p.initializeAuth();else await p.login(q.get('user'),'SyntheticOnly123!');
const router=createRouter({history:createMemoryHistory(),routes:[
{path:'/deck-editor',component:Editor,meta:{immersive:true,landscapeCanvas:true,editorAdaptiveCanvas:true}},
{path:'/hub-match',component:Hub,props:{initialTab:'match'}},{path:'/hub-friendly',component:Hub,props:{initialTab:'friendly'}},
{path:'/game',component:Game,meta:{immersive:true,landscapeCanvas:true}},
{path:'/decks',component:{template:'<p>本地牌库</p>'}},{name:'public-deck-detail',path:'/decks/:deckId',component:{render:()=>null}},
{path:'/:pathMatch(.*)*',component:{render:()=>null}}]});
await router.push('/deck-editor?deckId='+encodeURIComponent(q.get('deck')||''));createApp(App).use(router).mount('#app');
window.__masterQa={p,n,d,router,ready:true};
`
const require = createRequire(import.meta.url)
const { chromium } = require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
async function context(width, height, restrictedCard) {
  const displayPolicy = { enabled: Boolean(restrictedCard) }
  const ctx = await browser.newContext({ viewport: { width, height }, hasTouch: true, isMobile: width < 820 })
  await ctx.addInitScript(() => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ musicEnabled: false, sfxEnabled: false, mobileLayout: 'auto', animation: 'off' })))
  if (playerFlowOnly) await ctx.addInitScript(() => {
    window.__masterWire = []
    const native = WebSocket.prototype.send
    WebSocket.prototype.send = function (...args) {
      try {
        const message = typeof args[0] === 'string' ? JSON.parse(args[0]) : null
        if (message?.type === 'selectCustomDeck') window.__masterWire.push({ type: message.type,
          deck: { id: message.deck?.id, revision: message.deck?.revision, masterId: message.deck?.masterId,
            alternateArtSelections: message.deck?.alternateArtSelections }, stack: new Error().stack })
      } catch { /* Observation never changes the native transport contract. */ }
      return Reflect.apply(native, this, args)
    }
  })
  await ctx.route('**/*', async route => {
    const url = new URL(route.request().url())
    if (url.hostname !== '127.0.0.1') { report.externalBlocked = (report.externalBlocked || 0) + 1; return route.abort() }
    if (url.pathname === '/api/operations/effective-policy' && displayPolicy.enabled) {
      const response = await route.fetch(); const policy = await response.json()
      policy.cardRestrictions = [{ cardId: restrictedCard, maxCopies: 0, reason: '仅本地展示验收禁用' }]
      return route.fulfill({ response, json: policy })
    }
    return route.continue()
  })
  const page = await ctx.newPage(); latestPage = page; page.setDefaultTimeout(12_000)
  await page.addLocatorHandler(page.locator('.alternate-art-notification-mask'), async overlay => {
    await overlay.getByRole('button', { name: '确认', exact: true }).tap()
    report.grantNotificationsAcknowledged = (report.grantNotificationsAcknowledged || 0) + 1
  })
  page.on('pageerror', error => report.errors.push(error.message))
  page.on('dialog', dialog => dialog.dismiss())
  return { ctx, page, width, height, displayPolicy }
}
async function open(f, user, deck) {
  latestPage = f.page
  await f.page.goto(vite.resolvedUrls.local[0] + '__master_art__?user=' + user + '&deck=' + deck.id)
  await f.page.waitForFunction(() => window.__masterQa?.ready)
  await f.page.locator('.deck-builder-grid').waitFor()
}
async function masterSelector(f, expect = true) {
  const page = f.page
  latestPage = page
  if (f.width < 820) await page.locator('.deck-mobile-nav button').filter({ hasText: '牌表' }).tap()
  const banner = page.locator('[data-deck-section="master"] .deck-entry-row')
  await banner.scrollIntoViewIfNeeded(); await banner.tap()
  const area = f.width < 820 ? page.getByRole('dialog', { name: '卡牌详情', exact: true }) : page.locator('.deck-detail-panel')
  if (f.width < 820) await area.waitFor()
  const select = area.locator('.alternate-art-selector select')
  if (expect) await select.waitFor()
  return { area, select }
}
async function closeDetails(f) {
  const modal = f.page.getByRole('dialog', { name: '卡牌详情', exact: true })
  if (await modal.isVisible()) await modal.getByRole('button', { name: '关闭卡牌详情' }).tap()
}
async function save(f) {
  await closeDetails(f)
  const response = f.page.waitForResponse(response => /\/api\/decks(?:\/by-id\/[^/?]+)?$/.test(new URL(response.url()).pathname)
    && ['POST', 'PUT'].includes(response.request().method()))
  await f.page.getByRole('button', { name: '保存牌库', exact: true }).tap()
  const saved = await response; assert.equal(saved.status(), 200); return saved.json()
}
async function lobby(f, tab) {
  latestPage = f.page
  // The synthetic ban tests display only, not ranked deck admission.
  f.displayPolicy.enabled = false
  const freshPolicy = f.page.waitForResponse(response => new URL(response.url()).pathname === '/api/operations/effective-policy')
  await f.page.evaluate(tab => window.__masterQa.router.push('/hub-' + tab), tab)
  assert.equal((await freshPolicy).status(), 200)
  const trigger = f.page.locator('.deck-selection-trigger'); await trigger.waitFor()
  assert.equal(await trigger.count(), 1)
  assert(!/点击当前牌库封面即可更换/.test(await trigger.innerText()))
  await trigger.tap()
  const dialog = f.page.getByRole('dialog', { name: /更换牌库/ }); await dialog.waitFor()
  await dialog.locator('.selector-list>button').first().tap()
  await dialog.locator('footer button.confirm').tap(); await dialog.waitFor({ state: 'hidden' })
  assert((await trigger.innerText()).includes('点击封面更换牌库'))
  await f.page.screenshot({ path: path.join(out, `lobby-${tab}-${f.width}.png`), fullPage: true })
}
async function play(owner, peer, ownerDeck, peerDeck, expectedImage, label) {
  await lobby(owner, 'friendly')
  await owner.page.evaluate(() => window.__masterQa.n.connect())
  await owner.page.evaluate(() => window.__masterQa.n.createRoom({ matchModeId: 'friendly', spectating: 'public', handVisibility: 'request', disasterMode: 'none', useCardRestrictions: false }))
  await owner.page.waitForFunction(() => window.__masterQa.n.l12State.room?.roomCode)
  const code = await owner.page.evaluate(() => window.__masterQa.n.l12State.room.roomCode)
  await peer.page.evaluate(async code => { await window.__masterQa.n.connect(); window.__masterQa.n.joinRoom(code) }, code)
  await owner.page.waitForFunction(() => window.__masterQa.n.l12State.room?.players.length === 2)
  await peer.page.evaluate(deck => window.__masterQa.n.selectCustomDeck(deck), peerDeck)
  await owner.page.evaluate(deck => window.__masterQa.n.selectCustomDeck(deck), ownerDeck)
  await owner.page.getByRole('button', { name: '准备对战', exact: true }).tap()
  const trigger = owner.page.locator('.deck-selection-trigger')
  await trigger.waitFor({ state: 'visible' }); await owner.page.waitForFunction(() => document.querySelector('.deck-selection-trigger')?.disabled)
  assert(await trigger.isDisabled())
  assert((await owner.page.locator('.deck-selection-hint').innerText()).includes('已准备，取消准备后可更换'))
  await trigger.tap({ force: true }); assert.equal(await owner.page.locator('.saved-deck-selector').count(), 0)
  await owner.page.screenshot({ path: path.join(out, `room-ready-${label}.png`), fullPage: true })
  await peer.page.evaluate(() => window.__masterQa.n.setReady(true))
  for (const f of [owner, peer]) {
    await f.page.waitForFunction(() => Boolean(window.__masterQa.n.l12State.game))
    const image = await f.page.evaluate(() => window.__masterQa.n.l12State.game.players[0].master.masterImageUrl)
    assert.equal(image, expectedImage, 'Both recipients receive the granted master appearance')
    await f.page.evaluate(() => window.__masterQa.router.push('/game'))
    const card = f.page.locator('.mini-master[data-tutorial-player-index="0"] .l12-card-image')
    await card.waitFor(); await card.locator('img').evaluate(async image => { await image.decode(); assertImage(image) ;function assertImage(i){if(!i.naturalWidth)throw Error('Master image has not decoded')} })
    const displayed = { kind: await card.getAttribute('data-source'), url: await card.locator('img').getAttribute('src') }
    report.masterAppearances ??= []; report.masterAppearances.push({ scenario: label, viewer: f === owner ? 'owner' : 'opponent', expectedImage, displayed })
    if (expectedImage.startsWith('/api/site/media/')) {
      assert(displayed.url.startsWith('/api/site/media/')); assert.notEqual(displayed.kind, 'placeholder')
    }
  }
  await owner.page.screenshot({ path: path.join(out, `game-master-${label}.png`), fullPage: true })
  // Finish the actual local match before requesting departure; leave is not surrender.
  await owner.page.evaluate(() => window.__masterQa.n.gameAction({ type: 'surrender' }))
  await owner.page.waitForFunction(() => window.__masterQa.n.l12State.game?.phase === 'GameOver')
  await owner.page.evaluate(() => window.__masterQa.n.leaveRoom())
  await owner.page.waitForFunction(() => !window.__masterQa.n.l12State.room)
  await peer.page.evaluate(() => window.__masterQa.n.leaveRoom())
  await peer.page.waitForFunction(() => !window.__masterQa.n.l12State.room)
  return { image: expectedImage, twoRecipients: true, nativeReadyAndDisabledTap: true }
}
async function exactPlayerLobby(f, deck) {
  latestPage = f.page
  await f.page.evaluate(() => window.__masterQa.router.push('/hub-friendly'))
  const trigger = f.page.locator('.deck-selection-trigger'); await trigger.waitFor(); await trigger.tap()
  const dialog = f.page.getByRole('dialog', { name: /更换牌库/ }); await dialog.waitFor()
  const row = dialog.locator('.selector-list>button').filter({ hasText: deck.name })
  await row.waitFor({ state: 'visible' })
  assert.equal(await row.count(), 1, 'Select the exact saved deck, never the first row')
  await row.tap(); await dialog.getByRole('button', { name: '确认使用', exact: true }).tap()
  await dialog.waitFor({ state: 'hidden' })
  assert.equal(await trigger.getAttribute('aria-label'), '更换牌库：' + deck.name)
}
async function purePlayerChain({ desktop, peer, a, b, deckA, deckB, art, admin }) {
  assert.equal(a.account.role, 'player')
  const grant = await api('POST', '/api/admin/alternate-art-grants', admin.token,
    { alternateArtId: art.id, accountId: a.account.id, sourceKind: 'manual', sourceReference: 'synthetic-pure-player-chain' })
  assert((await api('GET', '/api/me/alternate-arts', a.token)).some(item => item.id === art.id))
  assert(!(await api('GET', '/api/me/alternate-arts', b.token)).some(item => item.id === art.id))
  await open(desktop, 'ArtOwnerQa', deckA)
  await (await masterSelector(desktop)).select.selectOption(art.id)
  const saved = await save(desktop)
  assert.equal(saved.id, deckA.id); assert.equal(saved.alternateArtSelections[art.baseCardId], art.id)
  await desktop.page.reload(); await desktop.page.locator('.deck-builder-grid').waitFor()
  assert.equal(await (await masterSelector(desktop)).select.inputValue(), art.id)
  await exactPlayerLobby(desktop, saved); await exactPlayerLobby(peer, deckB)
  // Only normal rendered controls below. No QA selectCustomDeck/createRoom/joinRoom/ready calls.
  await desktop.page.locator('.room-settings select').nth(3).selectOption('none')
  await desktop.page.getByRole('button', { name: '创建新房间', exact: true }).tap()
  const roomCode = desktop.page.locator('.room-code code'); await roomCode.waitFor()
  const code = (await roomCode.innerText()).trim()
  await peer.page.getByRole('textbox', { name: '房间码', exact: true }).fill(code)
  await peer.page.getByRole('button', { name: '加入对战', exact: true }).tap()
  await desktop.page.waitForFunction(() => window.__masterQa.n.l12State.room?.players.length === 2)
  await desktop.page.getByRole('button', { name: '准备对战', exact: true }).tap()
  await desktop.page.waitForFunction(() => document.querySelector('.deck-selection-trigger')?.disabled)
  await peer.page.getByRole('button', { name: '准备对战', exact: true }).tap()
  const projections = []
  for (const [f, expected] of [[desktop, saved], [peer, deckB]]) {
    await f.page.waitForFunction(() => Boolean(window.__masterQa.n.l12State.game))
    const submissions = await f.page.evaluate(() => window.__masterWire)
    const submitted = submissions.at(-1); assert(submitted)
    assert.equal(submitted.deck.id, expected.id); assert.equal(submitted.deck.revision, expected.revision)
    assert.match(submitted.stack, /runRoomDeckSync/, 'The actual lobby consumer, not a test helper, submitted this deck')
    if (f === desktop) assert.equal(submitted.deck.alternateArtSelections[art.baseCardId], art.id)
    const master = await f.page.evaluate(() => window.__masterQa.n.l12State.game.players[0].master)
    assert.equal(master.masterId, saved.masterId); assert.equal(master.masterImageUrl, 'l12-card-id:' + art.cardImageId)
    await f.page.evaluate(() => window.__masterQa.router.push('/game'))
    const card = f.page.locator('.mini-master[data-tutorial-player-index="0"] .l12-card-image')
    await card.waitFor()
    await f.page.waitForFunction(() => document.querySelector('.mini-master[data-tutorial-player-index="0"] .l12-card-image')?.dataset.source === 'sameOrigin')
    const image = card.locator('img'); await image.evaluate(image => image.decode())
    const displayUrl = await image.getAttribute('src')
    assert(displayUrl.includes('/S01-01M1A/3fc3fc13fec9558bd7ef/board-480.webp'))
    const response = await f.ctx.request.get(new URL(displayUrl, f.page.url()).href)
    assert.equal(response.status(), 200)
    const bytes = await response.body(); assert.equal(sha(bytes), '763B5C2F957A5A648E49441C4F07B6277D255A5E651DCD2F32B55C557ADBE3D4')
    const minimize = f.page.getByRole('button', { name: '最小化弹框', exact: true })
    if (await minimize.count()) await minimize.first().tap()
    await f.page.screenshot({ path: path.join(out, `pure-player-master-${f === desktop ? 'owner' : 'peer'}.png`), fullPage: true })
    projections.push({ viewer: f === desktop ? 'owner' : 'peer', submitted: submitted.deck, consumerStack: submitted.stack,
      masterId: master.masterId, imageUrl: master.masterImageUrl, displayedUrl: displayUrl, displayedSha256: sha(bytes) })
  }
  // Cleanup is separate from the tested player deck-selection chain.
  await desktop.page.evaluate(() => window.__masterQa.n.gameAction({ type: 'surrender' }))
  await desktop.page.waitForFunction(() => window.__masterQa.n.l12State.game?.phase === 'GameOver')
  for (const f of [desktop, peer]) {
    await f.page.evaluate(() => window.__masterQa.n.leaveRoom())
    await f.page.waitForFunction(() => !window.__masterQa.n.l12State.room)
  }
  await api('DELETE', '/api/admin/alternate-art-grants/' + grant.id, admin.token)
  return { savedDeckId: saved.id, savedDeckRevision: saved.revision, masterArt: art.id, nativeDeckSubmissionConsumer: 'runRoomDeckSync',
    testHelperDeckSubmissions: 0, projections }
}
try {
  const cleanEnv = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('L12_')))
  backend = spawn('C:/Program Files/dotnet/dotnet.exe', [path.join(host, 'GrandUMIServer.dll'), String(port)],
    { cwd: host, env: { ...cleanEnv, L12_LISTEN_HOST: '127.0.0.1', DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false' }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  children.push(backend.pid); backend.stdout.on('data', value => log.push(String(value))); backend.stderr.on('data', value => log.push(String(value)))
  for (let count = 0; count < 150; count++) {
    assert(backend.exitCode === null, 'Owned local server exited before startup')
    if (log.join('').includes('HTTP: http://127.0.0.1:' + port)) break
    await delay(100)
  }
  assert.equal((await (await fetch(origin + '/health')).json()).cards, 324)
  const admin = await api('POST', '/api/auth/login', null, { username: 'Admin', password: 'L12master' })
  const a = await api('POST', prior ? '/api/auth/login' : '/api/auth/register', null, { username: 'ArtOwnerQa', password })
  const b = await api('POST', prior ? '/api/auth/login' : '/api/auth/register', null, { username: 'ArtOtherQa', password })
  const presets = JSON.parse(fs.readFileSync(path.join(front, 'public/data/l12/preset-decks.s1.json'), 'utf8').replace(/^\uFEFF/, ''))
  const arts = await api('GET', '/api/alternate-arts')
  const art = arts.find(art => art.builtIn && presets.some(deck => deck.masterId === art.baseCardId))
  assert(art, 'Choose an actual master alternate art, never an arbitrary first artwork')
  const preset = presets.find(deck => deck.masterId === art.baseCardId), restricted = preset.cardIds[0]
  const seed = { ...preset, name: '主宰异画验收', alternateArtSelections: {}, alternateArtCopies: {} }
  const deckA = prior ? (await api('GET', '/api/decks', a.token)).find(deck => deck.name === seed.name) : await api('POST', '/api/decks', a.token, seed)
  const deckB = prior ? (await api('GET', '/api/decks', b.token)).find(deck => deck.name === seed.name) : await api('POST', '/api/decks', b.token, seed)
  assert(deckA && deckB)
  if (!prior) await check('ungranted identity cannot be selected through forged saved preference', async () => {
    assert(!(await api('GET', '/api/me/alternate-arts', a.token)).some(row => row.id === art.id))
    const forged = await api('PUT', '/api/decks/by-id/' + deckB.id, b.token,
      { expectedRevision: deckB.revision, deck: { ...deckB, alternateArtSelections: { [art.baseCardId]: art.id } } })
    assert(!forged.alternateArtSelections?.[art.baseCardId]); Object.assign(deckB, forged)
  })
  vite = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'), resolve: { alias: { '@': path.join(front, 'src') } },
    plugins: [vue(), tailwindcss(), { name: 'master-art-isolated-entry', resolveId(id) { if (id === '/@master-art-entry') return '\0master-art-entry' },
      load(id) { if (id === '\0master-art-entry') return entry }, configureServer(server) { server.middlewares.use((request, response, next) => {
        const pathname = new URL(request.url || '/', 'http://127.0.0.1').pathname
        const asset = assetFiles.get(pathname)
        if (asset) { response.setHeader('Content-Type', asset.type); response.end(asset.bytes); return }
        if (!request.url?.startsWith('/__master_art__')) return next()
        response.setHeader('Content-Type', 'text/html; charset=utf-8'); response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@master-art-entry"></script></body></html>')
      }) } }], server: { host: '127.0.0.1', port: 0, proxy: { '/api': origin, '/ws': { target: origin, ws: true } } } })
  await vite.listen(); browser = await chromium.launch({ channel: 'msedge', headless: true })
  const playerWidth = playerFlowOnly ? Number(process.env.L12_MASTER_PLAYER_WIDTH || '1440') : 1440
  assert([1440, 390].includes(playerWidth))
  const playerHeight = playerWidth === 390 ? 844 : 900
  const desktop = await context(playerWidth, playerHeight, playerFlowOnly ? null : restricted), peer = await context(1280, 800)
  await open(desktop, 'ArtOwnerQa', deckA); await open(peer, 'ArtOtherQa', deckB)
  if (playerFlowOnly) {
    report.playerViewport = { width: playerWidth, height: playerHeight }
    await check(`granted player saved deck reaches both game views through real lobby buttons at ${playerWidth}x${playerHeight}`, () =>
      purePlayerChain({ desktop, peer, a, b, deckA, deckB, art, admin }))
  } else {
  if (!prior) await check('actual master picker absent before grant', async () => {
    const { select } = await masterSelector(desktop, false); assert.equal(await select.count(), 0)
  })
  const grant = prior ? (await api('GET', '/api/admin/alternate-art-grants?username=ArtOwnerQa', admin.token)).find(grant => grant.alternateArtId === art.id && !grant.revoked) : await api('POST', '/api/admin/alternate-art-grants', admin.token,
    { alternateArtId: art.id, accountId: a.account.id, sourceKind: 'manual', sourceReference: 'synthetic-master-art-qa' })
  assert(grant)
  if (!prior) await check('real grant is present only in owner mine projection', async () => {
    assert((await api('GET', '/api/me/alternate-arts', a.token)).some(row => row.id === art.id))
    assert(!(await api('GET', '/api/me/alternate-arts', b.token)).some(row => row.id === art.id))
  })
  let savedA = prior ? deckA : null
  if (!prior) await check('desktop granted master selection saves and survives real reload', async () => {
    await open(desktop, 'ArtOwnerQa', deckA)
    const { select } = await masterSelector(desktop); await select.selectOption(art.id)
    savedA = await save(desktop); assert.equal(savedA.alternateArtSelections[art.baseCardId], art.id)
    await desktop.page.reload(); await desktop.page.locator('.deck-builder-grid').waitFor()
    assert.equal(await (await masterSelector(desktop)).select.inputValue(), art.id)
    await desktop.page.screenshot({ path: path.join(out, 'master-selector-desktop.png'), fullPage: true })
  })
  if (!prior) await check('real component sidebar hides warnings while pool retains them', async () => {
    assert.equal(await desktop.page.locator('[data-deck-section="main"] .deck-entry-row.invalid,[data-deck-section="main"] .deck-entry-row em').count(), 0)
    await desktop.page.locator('.catalog-tabs button').filter({ hasText: '主牌库' }).tap()
    await desktop.page.locator('.deck-card[data-card-id="' + restricted + '"] .entry-issue').first().waitFor()
  })
  if (!previouslyPassed('desktop matchmaking deck cover still opens selector without duplicate copy'))
    await check('desktop matchmaking deck cover still opens selector without duplicate copy', () => lobby(desktop, 'match'))
  if (!previouslyPassed('built-in master appearance reaches actual two-player game'))
    await check('built-in master appearance reaches actual two-player game', () => play(desktop, peer, savedA, deckB, 'l12-card-id:' + art.cardImageId, 'builtin'))
  if (!previouslyPassed('live account switch removes other accounts master-art choices')) await check('live account switch removes other accounts master-art choices', async () => {
    await desktop.page.evaluate(() => window.__masterQa.router.push('/deck-editor'))
    await desktop.page.evaluate(async () => { await window.__masterQa.p.login('ArtOtherQa', 'SyntheticOnly123!') })
    await desktop.page.waitForFunction(() => window.__masterQa.p.platformState.account?.username === 'ArtOtherQa')
    await desktop.page.waitForFunction(id => ![...document.querySelectorAll('.alternate-art-selector option')].some(node => node.value === id), art.id)
  })
  report.stage = 'prepare uploaded master media'
  let custom = arts.find(row => row.artCode === 'QA-MASTER-CUSTOM')
  if (!custom) {
  const pixels = await sharp({ create: { width: 500, height: 700, channels: 3, background: '#21518d' } }).png().toBuffer()
  const policy = (await api('GET', '/api/admin/site/media/policies', admin.token)).find(policy => policy.kind === 'card-art')
  assert(policy)
  const form = new FormData()
  for (const [key, value] of Object.entries({ kind: 'card-art', altText: '合成主宰图', desktopAltText: '合成主宰图',
    mobileAltText: '合成主宰图', thumbnailAltText: '合成主宰图', independentVariants: 'false', focalX: '0.5', focalY: '0.5' })) form.append(key, value)
  form.append('original', new Blob([pixels], { type: 'image/png' }), 'synthetic-master.png')
  for (const key of ['desktop', 'mobile', 'thumbnail']) {
    const width = policy.flexibleDimensions ? (key === 'thumbnail' ? 240 : 500) : policy[key + 'Width']
    const height = policy.flexibleDimensions ? (key === 'thumbnail' ? 336 : 700) : policy[key + 'Height']
    const variant = await sharp(pixels).resize(width, height).webp().toBuffer()
    form.append(key, new Blob([variant], { type: 'image/webp' }), key + '.webp')
  }
  const media = await api('POST', '/api/admin/site/media', admin.token, form)
  custom = await api('PUT', '/api/admin/alternate-arts', admin.token,
    { artCode: 'QA-MASTER-CUSTOM', baseCardId: art.baseCardId, displayName: '合成主宰异画', mediaAssetId: media.id, kind: 'alternate-art', active: true })
  }
  let customGrant = (await api('GET', '/api/admin/alternate-art-grants?username=ArtOwnerQa', admin.token)).find(row => row.alternateArtId === custom.id)
  if (!customGrant) customGrant = await api('POST', '/api/admin/alternate-art-grants', admin.token,
    { alternateArtId: custom.id, accountId: a.account.id, sourceKind: 'manual', sourceReference: 'synthetic-master-custom-qa' })
  const mobile = await context(390, 844, restricted)
  await open(mobile, 'ArtOwnerQa', savedA)
  if (!previouslyPassed('touch master picker selects uploaded art and persists through reload')) await check('touch master picker selects uploaded art and persists through reload', async () => {
    const { select } = await masterSelector(mobile); assert.equal(await select.inputValue(), art.id)
    await select.selectOption(custom.id); savedA = await save(mobile)
    assert.equal(savedA.alternateArtSelections[art.baseCardId], custom.id)
    await mobile.page.reload(); await mobile.page.locator('.deck-builder-grid').waitFor()
    const { select: reloaded } = await masterSelector(mobile); assert.equal(await reloaded.inputValue(), custom.id)
    await mobile.page.screenshot({ path: path.join(out, 'master-selector-mobile.png'), fullPage: true }); await closeDetails(mobile)
  })
  if (!previouslyPassed('touch lobby keeps cover action and actual ready restriction'))
    await check('touch lobby keeps cover action and actual ready restriction', () => lobby(mobile, 'match'))
  if (!previouslyPassed('uploaded master art reaches actual two-player game and image decodes'))
    await check('uploaded master art reaches actual two-player game and image decodes', () => play(mobile, peer, savedA, deckB, custom.imageUrl, 'uploaded'))
  await check('revoked master entitlement disappears and cannot be forged back on save', async () => {
    if ((await api('GET', '/api/me/alternate-arts', a.token)).some(row => row.id === custom.id))
      await api('DELETE', '/api/admin/alternate-art-grants/' + customGrant.id, admin.token)
    assert(!(await api('GET', '/api/me/alternate-arts', a.token)).some(row => row.id === custom.id))
    await open(mobile, 'ArtOwnerQa', savedA); const { select } = await masterSelector(mobile)
    assert.equal(await select.locator('option[value="' + custom.id + '"]').count(), 0)
    assert.equal(await select.inputValue(), '')
    savedA = await save(mobile)
    assert(!savedA.alternateArtSelections?.[art.baseCardId])
    const sanitized = await api('PUT', '/api/decks/by-id/' + savedA.id, a.token, { expectedRevision: savedA.revision,
      deck: { ...savedA, alternateArtSelections: { [art.baseCardId]: custom.id } } })
    assert(!sanitized.alternateArtSelections?.[art.baseCardId])
    await mobile.page.screenshot({ path: path.join(out, 'master-revoked-mobile.png'), fullPage: true })
  })
  await api('DELETE', '/api/admin/alternate-art-grants/' + grant.id, admin.token)
  }
  assert.deepEqual(report.errors, []); assert.deepEqual(report.sourceBefore, sourceHashes())
  report.status = 'passed'
} catch (error) {
  report.status = 'failed'; report.error = error.stack || error.message
  if (latestPage && !latestPage.isClosed()) await latestPage.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {})
  process.exitCode = 1
} finally {
  try { await browser?.close(); report.cleanup.browser = 'closed' } catch (error) { report.cleanup.browser = error.message; process.exitCode = 1 }
  try { await vite?.close(); report.cleanup.vite = 'closed' } catch (error) { report.cleanup.vite = error.message; process.exitCode = 1 }
  if (backend && backend.exitCode === null && backend.signalCode === null) {
    const exited = new Promise(resolve => backend.once('exit', resolve)); backend.kill()
    await Promise.race([exited, delay(5000)]); report.cleanup.backend = backend.exitCode !== null || backend.signalCode !== null ? 'exited' : 'still-running'
  } else report.cleanup.backend = 'not-running'
  report.cleanup.ownedPids = children; report.sourceAfter = sourceHashes()
  if (report.cleanup.backend === 'still-running') process.exitCode = 1
  fs.writeFileSync(path.join(out, 'backend.log'), log.join(''))
  fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
  console.log(JSON.stringify({ status: report.status, stage: report.stage, passed: report.cases.length, cleanup: report.cleanup, evidence: out }))
}
