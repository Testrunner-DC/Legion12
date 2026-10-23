import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import vm from 'node:vm'
import crypto from 'node:crypto'
import ts from 'typescript'
import { ref, computed } from 'vue'
import { parse as parseSfc } from '@vue/compiler-sfc'
import { parse as parseTemplate } from '@vue/compiler-dom'

const repo = path.resolve(import.meta.dirname, '../..')
const front = path.join(repo, 'opcgpro-vue')
const editorPath = path.join(front, 'src/l12/L12DeckEditor.vue')
const source = fs.readFileSync(editorPath, 'utf8')
const sfc = parseSfc(source, { filename: editorPath })
assert.deepEqual(sfc.errors, [])
const script = sfc.descriptor.scriptSetup.content
const template = parseTemplate(sfc.descriptor.template.content)
const walk = node => [node, ...(node.children ?? []).flatMap(walk)]
const attr = (node, name) => node.props?.find(prop => prop.type === 6 && prop.name === name)?.value?.content
const cls = (node, name) => (attr(node, 'class') ?? '').split(/\s+/).includes(name)
const directive = (node, name, arg) => node.props?.find(prop => prop.type === 7 && prop.name === name && prop.arg?.content === arg)?.exp?.content
const results = []
const sha = bytes => crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase()
async function check(name, work) {
  try { const details = await work(); results.push({ name, status: 'passed', ...details }); console.log('PASS ' + name) }
  catch (error) { results.push({ name, status: 'failed', error: error.stack }); console.log('FAIL ' + name + ': ' + error.message); process.exitCode = 1 }
}

// Exercise the actual SFC functions, not a second implementation of the loader.
function loaderHarness() {
  const parsed = ts.createSourceFile('editor.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const variables = new Set(['ownedAlternateArts', 'ownedArtRequest', 'ownedArtLoading', 'ownedArtError',
    'ownedArtAccountId', 'ownedArtRead', 'editorAccountEpoch', 'editorAlive'])
  const functions = new Set(['refreshOwnedAlternateArts', 'onAlternateArtResource'])
  const selected = parsed.statements.filter(node => ts.isFunctionDeclaration(node) ? functions.has(node.name?.text)
    : ts.isVariableStatement(node) && node.declarationList.declarations.some(decl => variables.has(decl.name.getText(parsed))))
  const code = selected.map(node => node.getText(parsed)).join('\n')
  const platformState = { account: { id: 'owner-a' }, token: 'token-a' }
  const pending = []
  const context = vm.createContext({ ref, computed, platformState,
    alternateArtApi: { mine: () => new Promise((resolve, reject) => pending.push({ resolve, reject })) } })
  const compiled = ts.transpileModule(code + `\n;globalThis.qa = { refreshOwnedAlternateArts,
    onAlternateArtResource, ownedAlternateArts, ownedArtLoading, ownedArtError,
    stop: () => { editorAlive = false; ownedArtRequest++ },
    accountEpoch: () => editorAccountEpoch++ };`, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  vm.runInContext(compiled, context)
  return { ...context.qa, platformState, pending }
}
const artA = { id: 'catalog-alt:S01-01M1A', baseCardId: 'S01-01M1' }
const settle = async () => { await Promise.resolve(); await Promise.resolve() }

if (!process.argv.includes('--browser')) {
  await check('master alternate art is discoverable in the actual pool without opening details', () => {
    const pool = walk(template).find(node => cls(node, 'deck-card-grid') && directive(node, 'if') === "catalogTab === 'master'")
    assert(pool)
    assert.match(pool.loc.source, /in masterAppearances/)
    assert.match(pool.loc.source, /data-appearance-id/)
    assert.match(pool.loc.source, /chooseMasterAppearance\(entry\)/)
    assert.match(pool.loc.source, /entry\.art\?\.cardImageId/)
    const banner = walk(template).find(node => attr(node, 'data-deck-section') === 'master')
    assert.match(banner.loc.source, /selectedMasterArt\?\.cardImageId/)
    assert.match(script, /function chooseMasterAppearance[\s\S]*?chooseMaster\(entry\.card\.id\)[\s\S]*?selectAlternateArt\(entry\.card\.id, entry\.art\?\.id \?\? ''\)/)
  })
  await check('an in-place grant refreshes via the existing resource event and removes the listener on unmount', async () => {
    assert.match(script, /window\.addEventListener\('l12-resource-alternateArtNotifications', onAlternateArtResource\)/)
    assert.match(script, /window\.removeEventListener\('l12-resource-alternateArtNotifications', onAlternateArtResource\)/)
    const h = loaderHarness()
    const first = h.refreshOwnedAlternateArts(); h.pending.shift().resolve([]); await first
    h.onAlternateArtResource(); assert.equal(h.pending.length, 1)
    h.pending.shift().resolve([artA]); await settle()
    assert.equal(h.ownedAlternateArts.value[0].id, artA.id)
  })
  await check('failed mine reads have visible retry and retain this account last confirmed data', async () => {
    const status = walk(template).find(node => cls(node, 'alternate-art-status'))
    assert(status); assert.match(status.loc.source, /ownedArtError/)
    assert(walk(status).some(node => node.tag === 'button' && directive(node, 'on', 'click') === 'refreshOwnedAlternateArts()'))
    const h = loaderHarness()
    const first = h.refreshOwnedAlternateArts(); h.pending.shift().resolve([artA]); await first
    const failed = h.refreshOwnedAlternateArts(); h.pending.shift().reject(new Error('synthetic unavailable')); await failed
    assert.equal(h.ownedAlternateArts.value[0].id, artA.id)
    assert(h.ownedArtError.value); assert.equal(h.ownedArtLoading.value, false)
    const retry = h.refreshOwnedAlternateArts(); h.pending.shift().resolve([artA]); await retry
    assert.equal(h.ownedArtError.value, '')
  })
  await check('account/token/request epochs, late failure and unmount cannot leak or overwrite data', async () => {
    const h = loaderHarness()
    const first = h.refreshOwnedAlternateArts(); h.pending.shift().resolve([artA]); await first
    const older = h.refreshOwnedAlternateArts(), old = h.pending.shift()
    await h.refreshOwnedAlternateArts(); await h.refreshOwnedAlternateArts()
    assert.equal(h.pending.length, 0, 'Invalidations coalesce until the in-flight GET settles')
    old.resolve([artA]); await older
    assert.equal(h.pending.length, 1, 'A fresh read must follow the settled stale response')
    h.pending.shift().resolve([]); await settle(); assert.equal(h.ownedAlternateArts.value.length, 0)
    const oldToken = h.refreshOwnedAlternateArts(), lateToken = h.pending.shift()
    h.platformState.token = 'rotated-token'
    lateToken.resolve([artA]); await oldToken; assert.equal(h.ownedAlternateArts.value.length, 0)
    h.platformState.account = { id: 'owner-b' }; h.platformState.token = 'token-b'; h.accountEpoch()
    const switched = h.refreshOwnedAlternateArts(); assert.equal(h.ownedAlternateArts.value.length, 0)
    h.pending.shift().reject(new Error('b unavailable')); await switched
    assert.equal(h.ownedAlternateArts.value.length, 0); assert(h.ownedArtError.value)
    const pending = h.refreshOwnedAlternateArts(), afterStop = h.pending.shift()
    h.stop(); afterStop.resolve([artA]); await pending; assert.equal(h.ownedAlternateArts.value.length, 0)
    h.platformState.account = null; h.platformState.token = null
    await h.refreshOwnedAlternateArts(); assert.equal(h.ownedAlternateArts.value.length, 0)
    assert.equal(h.ownedArtLoading.value, false)
  })
  console.log(JSON.stringify({ scope: 'actual-sfc-template-and-loader', editorSha256: sha(source), results }, null, 2))
} else {
  await browserAcceptance()
}

async function browserAcceptance() {
  const { spawn, execFileSync } = await import('node:child_process')
  const { createRequire } = await import('node:module')
  const { createServer } = await import('vite')
  const { default: vue } = await import('@vitejs/plugin-vue')
  const { default: tailwindcss } = await import('@tailwindcss/vite')
  const { default: net } = await import('node:net')
  const out = process.env.L12_QA_OUT, cache = process.env.L12_WORK_CACHE
  const host = process.env.L12_MASTER_HOST, assets = process.env.L12_MASTER_ART_ASSETS
  assert([out, cache, host, assets].every(value => value && path.isAbsolute(value)))
  const guardEnv = { ...process.env, L12_PROBE_REPO: repo, L12_PROBE_OUT: out, L12_PROBE_CACHE: cache, L12_PROBE_HOST: host }
  execFileSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile', '-NonInteractive', '-Command', '. "$env:L12_PROBE_REPO/ops/windows/L12GeneratedStorage.ps1"; '
      + '@($env:L12_PROBE_OUT,$env:L12_PROBE_CACHE,$env:L12_PROBE_HOST) | ForEach-Object {$null=Assert-L12GeneratedPath -Path $_ -Purpose "master art editor refresh acceptance"}'],
    { env: guardEnv, windowsHide: true, stdio: 'pipe' })
  assert(!fs.existsSync(out) || fs.readdirSync(out).length === 0, 'Do not overwrite evidence')
  fs.mkdirSync(out, { recursive: true }); fs.mkdirSync(cache, { recursive: true })
  const expectedDll = '908E146B9A92D52A0FA6E3B5CAD8260A07443515D82E35AA528204A41365321B'
  assert.equal(sha(fs.readFileSync(path.join(host, 'GrandUMIServer.dll'))), expectedDll)
  const owner = JSON.parse(fs.readFileSync(path.join(host, 'owned-gallery-host.json'), 'utf8'))
  assert.equal(owner.syntheticRuntimeOnly, true); assert.equal(owner.backendSha256, expectedDll)
  const manifestBytes = fs.readFileSync(path.join(assets, 'card-assets.manifest.json'))
  assert.equal(sha(manifestBytes), 'BE756F5E72647FEF7B128665296C305AE7BE213743B7B0195AECCC09E17990CD')
  const manifest = JSON.parse(manifestBytes), material = manifest.cards['S01-01M1A']
  const assetFiles = new Map([['/card-assets/card-assets.manifest.json', manifestBytes]])
  for (const relative of Object.values(material.variants)) {
    assert(relative.startsWith('cards/20260912-batch308/S01-01M1A/3fc3fc13fec9558bd7ef/'))
    assetFiles.set('/card-assets/' + relative, fs.readFileSync(path.join(assets, relative)))
  }
  const report = { status: 'running', editorBefore: sha(source), backendSha256: expectedDll, cases: results,
    limits: ['Local synthetic accounts only; no production or test server/account access.',
      'Real existing WebSocket resource delivery for the in-place grant; explicit HTTP-failure/late-response fixtures are labelled separately.',
      '390x844 is browser touch emulation, not a physical phone.', 'Only the named trusted master-art material is served.'],
    requests: [], errors: [], resources: [], cleanup: {} }
  const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
  const reserve = net.createServer(); await new Promise(resolve => reserve.listen(0, '127.0.0.1', resolve))
  const port = reserve.address().port; await new Promise(resolve => reserve.close(resolve))
  const origin = `http://127.0.0.1:${port}`
  let backend, vite, browser, latestPage
  const log = [], contexts = [], grants = []
  const api = async (method, pathname, token, body) => {
    const response = await fetch(origin + pathname, { method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined })
    report.requests.push({ method, pathname, status: response.status })
    assert(response.ok, `${method} ${pathname} status ${response.status}`)
    return response.status === 204 ? null : response.json()
  }
  const entry = `import {createApp} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router';
import App from '/src/App.vue';import Editor from '/src/l12/L12DeckEditor.vue';
import * as p from '/src/l12/platform';import * as n from '/src/l12/net';import '/src/style.css';
const q=new URLSearchParams(location.search);n.l12State.endpoint='ws://'+location.host+'/ws';
if(p.platformState.token)await p.initializeAuth();else await p.login(q.get('user'),'SyntheticOnly123!');
const router=createRouter({history:createMemoryHistory(),routes:[
{path:'/deck-editor',component:Editor,meta:{immersive:true,landscapeCanvas:true,editorAdaptiveCanvas:true}},
{path:'/decks',component:{template:'<p>本地牌库</p>'}},{name:'public-deck-detail',path:'/decks/:deckId',component:{render:()=>null}},
{path:'/:pathMatch(.*)*',component:{render:()=>null}}]});
await router.push('/deck-editor?deckId='+encodeURIComponent(q.get('deck')));createApp(App).use(router).mount('#app');
window.__artQa={p,n,router,ready:true};`
  try {
    const cleanEnv = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith('L12_')))
    backend = spawn('C:/Program Files/dotnet/dotnet.exe', [path.join(host, 'GrandUMIServer.dll'), String(port)],
      { cwd: host, env: { ...cleanEnv, L12_LISTEN_HOST: '127.0.0.1' }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
    report.backendPid = backend.pid; report.backendPort = port
    backend.stdout.on('data', bytes => log.push(String(bytes))); backend.stderr.on('data', bytes => log.push(String(bytes)))
    for (let count = 0; count < 150; count++) {
      assert.equal(backend.exitCode, null)
      if (log.join('').includes('HTTP: http://127.0.0.1:' + port)) break
      await delay(100)
    }
    const admin = await api('POST', '/api/auth/login', null, { username: 'Admin', password: 'L12master' })
    const a = await api('POST', '/api/auth/login', null, { username: 'ArtOwnerQa', password: 'SyntheticOnly123!' })
    const b = await api('POST', '/api/auth/login', null, { username: 'ArtOtherQa', password: 'SyntheticOnly123!' })
    assert.equal(a.account.role, 'player'); assert.equal(b.account.role, 'player')
    const art = (await api('GET', '/api/alternate-arts')).find(row => row.id === 'catalog-alt:S01-01M1A')
    assert(art); assert.equal(art.baseCardId, 'S01-01M1')
    const deck = (await api('GET', '/api/decks', a.token)).find(row => row.name === '主宰异画验收')
    assert(deck); assert.equal(deck.masterId, art.baseCardId)
    const activeGrants = async () => (await api('GET', '/api/admin/alternate-art-grants?username=ArtOwnerQa', admin.token))
      .filter(row => row.alternateArtId === art.id && !row.revokedAt)
    for (const grant of await activeGrants()) await api('DELETE', '/api/admin/alternate-art-grants/' + grant.id, admin.token)
    const grant = async () => {
      const result = await api('POST', '/api/admin/alternate-art-grants', admin.token,
        { alternateArtId: art.id, accountId: a.account.id, sourceKind: 'manual', sourceReference: 'synthetic-editor-refresh' })
      grants.push(result.id); return result
    }
    vite = await createServer({ configFile: false, root: front, cacheDir: path.join(cache, 'vite'),
      resolve: { alias: { '@': path.join(front, 'src') } }, plugins: [vue(), tailwindcss(), {
        name: 'master-art-refresh-isolated-entry', resolveId(id) { if (id === '/@master-art-refresh-entry') return '\0master-art-refresh-entry' },
        load(id) { if (id === '\0master-art-refresh-entry') return entry }, configureServer(server) {
          server.middlewares.use((request, response, next) => {
            const pathname = new URL(request.url || '/', 'http://127.0.0.1').pathname
            const bytes = assetFiles.get(pathname)
            if (bytes) { response.setHeader('Content-Type', pathname.endsWith('.json') ? 'application/json' : pathname.endsWith('.avif') ? 'image/avif' : 'image/webp'); response.end(bytes); return }
            if (!pathname.startsWith('/__master_art_refresh__')) return next()
            response.setHeader('Content-Type', 'text/html; charset=utf-8')
            response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/@master-art-refresh-entry"></script></body></html>')
          })
        },
      }], server: { host: '127.0.0.1', port: 0, proxy: { '/api': origin, '/ws': { target: origin, ws: true } } } })
    await vite.listen(); report.frontendPort = new URL(vite.resolvedUrls.local[0]).port
    const require = createRequire(import.meta.url)
    const { chromium } = require('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
    browser = await chromium.launch({ channel: 'msedge', headless: true })
    const open = async (width, height) => {
      const ctx = await browser.newContext({ viewport: { width, height }, hasTouch: true, isMobile: width < 820 })
      const controls = { mode: 'pass', held: [], mineReads: 0 }; contexts.push({ ctx, controls })
      await ctx.addInitScript(() => {
        localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({ musicEnabled: false, sfxEnabled: false, mobileLayout: 'auto', animation: 'off' }))
        window.__artResources = []
        window.addEventListener('l12-resource-alternateArtNotifications', event => window.__artResources.push(event.detail))
      })
      await ctx.route('**/*', async route => {
        const url = new URL(route.request().url())
        if (url.hostname !== '127.0.0.1') return route.abort()
        if (url.pathname === '/api/me/alternate-arts') {
          controls.mineReads++
          if (controls.mode === 'fail') return route.fulfill({ status: 503, contentType: 'application/json', body: '{"message":"synthetic mine read failure"}' })
          if (controls.mode === 'hold-once') {
            controls.mode = 'pass'
            const response = await route.fetch()
            let complete
            const completed = new Promise(resolve => { complete = resolve })
            await new Promise(resolve => controls.held.push({ release: resolve, completed }))
            try { await route.fulfill({ response }) } catch { /* Context may close after an intentionally held read. */ }
            finally { complete() }
            return
          }
        }
        return route.continue()
      })
      const page = await ctx.newPage(); latestPage = page; page.setDefaultTimeout(12000)
      page.on('pageerror', error => report.errors.push(error.message)); page.on('dialog', dialog => dialog.dismiss())
      await page.addLocatorHandler(page.locator('.alternate-art-notification-mask'), async overlay => overlay.getByRole('button', { name: '确认', exact: true }).tap())
      await page.goto(vite.resolvedUrls.local[0] + '__master_art_refresh__?user=ArtOwnerQa&deck=' + deck.id)
      await page.waitForFunction(() => window.__artQa?.ready && window.__artQa.n.l12State.status === 'online')
      await page.locator('.deck-builder-grid').waitFor()
      return { page, ctx, controls, width, height }
    }
    const showMasters = async f => {
      if (f.width < 820) await f.page.locator('.deck-mobile-nav button').filter({ hasText: /^牌库$/ }).tap()
      await f.page.locator('.catalog-tabs button').filter({ hasText: /^主宰$/ }).tap()
    }
    const alt = f => f.page.locator('.deck-card-grid .deck-card[data-card-id="S01-01M1"][data-appearance-id="catalog-alt:S01-01M1A"]')
    const emitFixture = f => f.page.evaluate(() => window.dispatchEvent(new CustomEvent('l12-resource-alternateArtNotifications', { detail: { fixture: true } })))
    const waitHeld = async f => { for (let n = 0; n < 100 && !f.controls.held.length; n++) await delay(50); assert.equal(f.controls.held.length, 1) }
    const save = async f => {
      const response = f.page.waitForResponse(response => /\/api\/decks\/by-id\/[^/?]+$/.test(new URL(response.url()).pathname) && response.request().method() === 'PUT')
      await f.page.locator('.deck-builder-topbar button.primary').tap()
      const savedResponse = await response; assert.equal(savedResponse.status(), 200)
      const saved = await savedResponse.json()
      assert.equal(saved.id, deck.id); assert.equal(saved.masterId, art.baseCardId)
      assert.equal(saved.alternateArtSelections[art.baseCardId], art.id)
      return { savedDeckId: saved.id, revision: saved.revision, baseCardId: saved.masterId, appearanceId: saved.alternateArtSelections[art.baseCardId] }
    }
    const pixels = async (f, locator) => {
      const image = locator.locator('img').first(); await image.waitFor()
      await image.evaluate(image => image.decode())
      const url = await image.getAttribute('src'); assert(url.includes('/S01-01M1A/3fc3fc13fec9558bd7ef/'))
      const response = await f.ctx.request.get(new URL(url, f.page.url()).href); assert.equal(response.status(), 200)
      const bytes = await response.body(), pathname = new URL(url, f.page.url()).pathname
      assert.equal(sha(bytes), sha(assetFiles.get(pathname)))
      return { url, sha256: sha(bytes) }
    }
    const desktop = await open(1440, 900)
    await check('desktop actual in-place grant resource arrives and exposes a directly selectable master art', async () => {
      await showMasters(desktop); assert.equal(await alt(desktop).count(), 0)
      const timeOrigin = await desktop.page.evaluate(() => performance.timeOrigin)
      const before = await desktop.page.evaluate(() => window.__artResources.length)
      await grant()
      await alt(desktop).waitFor()
      const resources = await desktop.page.evaluate(before => window.__artResources.slice(before), before)
      assert(resources.some(row => row?.revision && !row.fallback && !row.fixture), 'Must see an actual server resource revision, not a synthetic event or fallback')
      assert.equal(await desktop.page.evaluate(() => performance.timeOrigin), timeOrigin)
      await alt(desktop).scrollIntoViewIfNeeded()
      const media = await pixels(desktop, alt(desktop))
      await desktop.page.screenshot({ path: path.join(out, 'desktop-direct-master-art.png'), fullPage: true })
      await desktop.page.locator('.deck-card-grid .deck-card[data-card-id="S01-01M1"][data-appearance-id="original"] .choose-special').tap()
      await showMasters(desktop)
      await alt(desktop).getByRole('button', { name: '使用异画', exact: true }).tap()
      const saved = await save(desktop)
      await desktop.page.reload(); await desktop.page.locator('.deck-builder-grid').waitFor()
      const banner = desktop.page.locator('[data-deck-section="master"] .deck-entry-row')
      await pixels(desktop, banner)
      await showMasters(desktop); assert.match(await alt(desktop).getAttribute('class'), /chosen/)
      report.resources.push(...resources)
      return { ...saved, media, noPageReloadDuringGrant: true, serverResources: resources }
    })
    await check('desktop injected mine failure is visible, preserves confirmed art and real retry recovers', async () => {
      desktop.controls.mode = 'fail'; await emitFixture(desktop)
      const status = desktop.page.locator('.alternate-art-status')
      await status.getByRole('button', { name: '重试读取异画', exact: true }).waitFor()
      await alt(desktop).waitFor()
      await desktop.page.screenshot({ path: path.join(out, 'desktop-mine-failure.png'), fullPage: true })
      const reads = desktop.controls.mineReads; desktop.controls.mode = 'pass'
      await status.getByRole('button', { name: '重试读取异画', exact: true }).tap()
      await status.waitFor({ state: 'hidden' }); assert(desktop.controls.mineReads > reads)
      await alt(desktop).waitFor()
      return { fixture: 'HTTP 503 on mine only; normal retry button uses real backend' }
    })
    await check('actual grant during an in-flight empty read discards old body and re-reads after settle', async () => {
      for (const row of await activeGrants()) await api('DELETE', '/api/admin/alternate-art-grants/' + row.id, admin.token)
      // Revocation currently does not emit a resource update; do not invent one.
      // Explicitly refresh this fixture reset before testing a real grant event.
      await emitFixture(desktop)
      await alt(desktop).waitFor({ state: 'detached' })
      desktop.controls.mode = 'hold-once'; await emitFixture(desktop); await waitHeld(desktop)
      const before = await desktop.page.evaluate(() => window.__artResources.length)
      await grant()
      await desktop.page.waitForFunction(before => window.__artResources.slice(before).some(row => row?.revision && !row.fallback && !row.fixture), before)
      const reads = desktop.controls.mineReads
      const held = desktop.controls.held.shift(); held.release(); await held.completed
      await alt(desktop).waitFor()
      assert(desktop.controls.mineReads > reads, 'Must perform a new actual HTTP read after the held empty response settles')
      return { fixture: 'hold one real empty mine response; actual grant resource arrives; release; actual new mine exposes art', actualGrantPush: true, revocationPushNotClaimed: true }
    })
    await check('switching accounts during a held response and a failed read never exposes previous owner art', async () => {
      await grant(); await alt(desktop).waitFor()
      desktop.controls.mode = 'hold-once'; await emitFixture(desktop); await waitHeld(desktop)
      desktop.controls.mode = 'fail'
      await desktop.page.evaluate(async () => { await window.__artQa.p.logout(); await window.__artQa.p.login('ArtOtherQa', 'SyntheticOnly123!') })
      await desktop.page.waitForFunction(() => window.__artQa.p.platformState.account?.username === 'ArtOtherQa')
      await desktop.page.locator('.alternate-art-status').getByRole('button', { name: '重试读取异画', exact: true }).waitFor()
      await showMasters(desktop); assert.equal(await alt(desktop).count(), 0)
      const held = desktop.controls.held.shift(); held.release(); await held.completed
      await desktop.page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
      assert.equal(await alt(desktop).count(), 0)
      await desktop.page.screenshot({ path: path.join(out, 'other-account-no-art.png'), fullPage: true })
      desktop.controls.mode = 'pass'
      await desktop.page.getByRole('button', { name: '重试读取异画', exact: true }).tap()
      await desktop.page.locator('.alternate-art-status').waitFor({ state: 'hidden' })
      assert.equal(await alt(desktop).count(), 0)
      return { fixture: 'old owner response held; new account HTTP 503; then actual empty mine retry' }
    })
    const mobile = await open(390, 844)
    await check('mobile touch pool selects art directly, saves and reloads with truthful error retry', async () => {
      await showMasters(mobile); await alt(mobile).waitFor(); await alt(mobile).scrollIntoViewIfNeeded()
      await pixels(mobile, alt(mobile))
      await mobile.page.screenshot({ path: path.join(out, 'mobile-direct-master-art.png'), fullPage: true })
      // Select original then alternate through visible buttons to exercise a real change.
      await mobile.page.locator('.deck-card-grid .deck-card[data-card-id="S01-01M1"][data-appearance-id="original"] .choose-special').tap()
      await showMasters(mobile); await alt(mobile).getByRole('button', { name: '使用异画', exact: true }).tap()
      const saved = await save(mobile)
      await mobile.page.reload(); await mobile.page.locator('.deck-builder-grid').waitFor()
      await showMasters(mobile); assert.match(await alt(mobile).getAttribute('class'), /chosen/)
      mobile.controls.mode = 'fail'; await emitFixture(mobile)
      const retry = mobile.page.getByRole('button', { name: '重试读取异画', exact: true }); await retry.waitFor()
      const box = await retry.boundingBox(); assert(box && box.width > 0 && box.height >= 44 && box.x >= 0 && box.x + box.width <= 390 && box.y >= 0 && box.y + box.height <= 844)
      assert(await mobile.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
      await mobile.page.screenshot({ path: path.join(out, 'mobile-mine-failure.png'), fullPage: true })
      mobile.controls.mode = 'pass'; await retry.tap(); await mobile.page.locator('.alternate-art-status').waitFor({ state: 'hidden' })
      return { ...saved, viewport: { width: 390, height: 844 }, touchEmulation: true, retryBox: box }
    })
    assert.equal(results.filter(row => row.status === 'failed').length, 0)
    assert.deepEqual(report.errors, [])
    report.status = 'passed'
    for (const row of await activeGrants()) await api('DELETE', '/api/admin/alternate-art-grants/' + row.id, admin.token)
  } catch (error) {
    report.status = 'failed'; report.error = error.stack; process.exitCode = 1
    if (latestPage && !latestPage.isClosed()) await latestPage.screenshot({ path: path.join(out, 'failure.png'), fullPage: true }).catch(() => {})
  } finally {
    for (const { controls } of contexts) for (const pending of controls.held) pending.release()
    try { await browser?.close(); report.cleanup.browser = 'closed' } catch (error) { report.cleanup.browser = error.message; process.exitCode = 1 }
    try { await vite?.close(); report.cleanup.vite = 'closed' } catch (error) { report.cleanup.vite = error.message; process.exitCode = 1 }
    if (backend && backend.exitCode === null && backend.signalCode === null) {
      const exited = new Promise(resolve => backend.once('exit', resolve)); backend.kill()
      await Promise.race([exited, delay(5000)])
    }
    report.cleanup.backend = backend?.exitCode !== null || backend?.signalCode !== null ? 'exited' : 'still-running'
    if (report.cleanup.backend === 'still-running') process.exitCode = 1
    report.editorAfter = sha(fs.readFileSync(editorPath)); report.qaSha256 = sha(fs.readFileSync(import.meta.filename))
    if (report.editorBefore !== report.editorAfter) { report.status = 'source-changed-during-test'; process.exitCode = 1 }
    fs.writeFileSync(path.join(out, 'backend.log'), log.join(''))
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2))
    console.log(JSON.stringify({ status: report.status, passed: results.filter(row => row.status === 'passed').length, cleanup: report.cleanup, evidence: out }))
  }
}
