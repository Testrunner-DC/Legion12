import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'

// Default mode is memory-only: no browser, server, generated files or network.
const root = fileURLToPath(new URL('../', import.meta.url))
const require = createRequire(path.join(root, 'package.json'))
const ts = require('typescript')
const { computed, reactive } = require('vue')
const vueRouter = require('vue-router')
const paths = {
  profile: 'src/l12/site/ProfilePage.vue',
  navigation: 'src/l12/site/adminSections.ts',
  router: 'src/router/index.ts',
  sandbox: 'src/l12/site/SandboxPage.vue',
  game: 'src/l12/GamePage.vue',
  platform: 'src/l12/platform.ts',
  shell: 'src/l12/site/SiteShell.vue',
}
const raw = Object.fromEntries(Object.entries(paths).map(([key, relative]) => [key, readFileSync(path.join(root, relative))]))
const sources = Object.fromEntries(Object.entries(raw).map(([key, bytes]) => [key, bytes.toString('utf8').replace(/\r\n?/g, '\n')]))
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex').toUpperCase()
const sourceHashes = Object.fromEntries(Object.entries(raw).map(([key, bytes]) => [paths[key], sha256(bytes)]))
const script = source => source.match(/<script setup[^>]*>([\s\S]*?)<\/script>/)?.[1] ?? source
const tree = (source, file = 'fixture.ts') => ts.createSourceFile(file, script(source), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
function findNode(source, predicate) {
  const ast = tree(source)
  let result
  function visit(node) { if (predicate(node)) result = node; else ts.forEachChild(node, visit) }
  visit(ast)
  assert.ok(result, 'Expected named source declaration')
  return result.getText(ast)
}
function initializer(source, name) {
  const declaration = findNode(source, node => ts.isVariableDeclaration(node) && node.name.getText() === name)
  return declaration.slice(declaration.indexOf('=') + 1).trim()
}
const functionText = (source, name) => findNode(source, node => ts.isFunctionDeclaration(node) && node.name?.text === name)
function evaluate(source, imports = {}, globals = {}) {
  const module = { exports: {} }
  const javascript = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
  vm.runInNewContext(javascript, {
    module, exports: module.exports,
    require: id => { assert.ok(Object.hasOwn(imports, id), `Unexpected import: ${id}`); return imports[id] },
    console, setTimeout, clearTimeout, ...globals,
  })
  return module.exports
}
const accountState = reactive({ account: null })
const authentication = reactive({ verified: false })
const hasPermission = evaluate(`${functionText(sources.platform, 'hasPermission')}\nexports.permission = hasPermission`, {}, {
  authState: authentication, platformState: accountState,
}).permission
const canAccessAdmin = vm.runInNewContext(initializer(sources.platform, 'canAccessAdmin'), {
  computed, authState: authentication, platformState: accountState,
})
const profileAccess = vm.runInNewContext(initializer(sources.profile, 'canUseSandbox'), {
  computed, canAccessAdmin, hasPermission,
})
const sandboxAccess = vm.runInNewContext(initializer(sources.sandbox, 'canUseSandbox'), {
  computed, platformState: accountState, hasPermission,
})
const navigation = evaluate(sources.navigation).adminSections
const cases = []
async function check(name, action) { await action(); cases.push(name) }
function setIdentity({ role = 'admin', permissions = ['admin.operations.write'], verified = true, guest = false, ...extra } = {}) {
  accountState.account = guest ? null : { id: 'synthetic-sandbox-entry', username: '本地布局验收账号', role, permissions, ...extra }
  authentication.verified = verified
}
function makeRouter() {
  return evaluate(sources.router.replaceAll('import.meta.env.BASE_URL', "'/'"), {
    'vue-router': { ...vueRouter, createWebHistory: () => vueRouter.createMemoryHistory() },
    '@/l12/platform': { authState: authentication, canAccessAdmin, platformState: accountState, refreshCurrentAccount: async () => accountState.account },
    '@/chunkRecovery': { beginRouteNavigation() {}, finishRouteNavigation() {}, handleChunkLoadError: () => false, showRouteNavigationError() {} },
    '@/l12/site/adminSections': { adminSections: navigation },
    ...Object.fromEntries([...sources.router.matchAll(/import\('([^']+)'\)/g)].map(([, id]) => [id, { default: { render: () => null } }])),
  }, { window: { setTimeout, clearTimeout } }).router
}

for (const [name, identity, expectedAdmin, expectedSandbox] of [
  ['guest', { guest: true, verified: false }, false, false],
  ['unverified admin', { verified: false }, false, false],
  ['ordinary player', { role: 'player', permissions: [] }, false, false],
  ['player with stray operations permission', { role: 'player' }, false, false],
  ['admin without operations write', { permissions: ['admin.operations.read'] }, true, false],
  ['authorized admin', {}, true, true],
]) {
  await check(`permission matrix: ${name}`, () => {
    setIdentity(identity)
    assert.equal(canAccessAdmin.value, expectedAdmin)
    assert.equal(profileAccess.value, expectedSandbox)
    assert.equal(sandboxAccess.value, expectedSandbox)
  })
}
await check('permission revocation, identity switch and logout update both consumers', () => {
  setIdentity(); assert.equal(profileAccess.value, true)
  accountState.account.permissions = []; assert.equal(profileAccess.value, false); assert.equal(sandboxAccess.value, false)
  setIdentity(); assert.equal(profileAccess.value, true)
  authentication.verified = false; assert.equal(profileAccess.value, false); assert.equal(sandboxAccess.value, false)
  setIdentity({ role: 'player' }); assert.equal(profileAccess.value, false)
  accountState.account = null; assert.equal(profileAccess.value, false); assert.equal(sandboxAccess.value, false)
})
await check('all shared admin navigation consumers omit sandbox, preserve match archive', () => {
  assert.equal(navigation.some(item => item.id === 'sandbox' || item.path.includes('/sandbox')), false)
  assert.ok(navigation.some(item => item.id === 'matches'))
})
for (const input of ['/sandbox?source=profile#setup', '/admin/operations/sandbox?source=old#setup', '/admin?section=sandbox&source=legacy#setup']) {
  await check(`actual router compatibility: ${input}`, async () => {
    setIdentity()
    const router = makeRouter()
    await router.push(input)
    const route = router.currentRoute.value
    assert.equal(route.path, '/sandbox')
    assert.equal(route.name, 'sandbox')
    assert.equal(route.query.source, input.includes('legacy') ? 'legacy' : input.includes('old') ? 'old' : 'profile')
    assert.equal(route.query.section, undefined)
    assert.equal(route.hash, '#setup')
    assert.equal(route.meta.requiresAdmin, true)
    assert.equal(route.meta.landscapeCanvas, true)
    assert.equal(route.matched.length, 1, 'sandbox cannot remain inside AdminPage')
  })
}
for (const identity of [{ guest: true, verified: false }, { verified: false }, { role: 'player' }, { mustChangeUsername: true }, { mustChangePassword: true }]) {
  for (const input of ['/sandbox?probe=denied#setup', '/admin/operations/sandbox?probe=denied#setup', '/admin?section=sandbox&probe=denied#setup']) {
    await check(`actual route denial: ${JSON.stringify(identity)} at ${input}`, async () => {
      setIdentity(identity)
      const router = makeRouter()
      await router.push(input)
      assert.equal(router.currentRoute.value.path, '/me')
      assert.match(router.currentRoute.value.query.redirect, /sandbox/)
      if (identity.mustChangeUsername) assert.equal(router.currentRoute.value.query.reason, 'username-change-required')
      if (identity.mustChangePassword) assert.equal(router.currentRoute.value.query.reason, 'password-change-required')
    })
  }
}
await check('other legacy admin sections retain query/hash and permissions', async () => {
  setIdentity()
  const router = makeRouter()
  await router.push('/admin?section=operations&source=existing#settings')
  assert.equal(router.currentRoute.value.path, '/admin/operations/config')
  assert.equal(router.currentRoute.value.query.source, 'existing')
  assert.equal(router.currentRoute.value.query.section, undefined)
  assert.equal(router.currentRoute.value.hash, '#settings')
})
for (const [room, phase, expected, leaves] of [
  [{ sandbox: true }, 'GameOver', '/sandbox', 1],
  [{ sandbox: true }, 'Main', '/sandbox', 1],
  [{ tournamentCode: '测试 / Cut' }, 'GameOver', `/battle/tournaments/${encodeURIComponent('测试 / Cut')}`, 1],
  [{}, 'GameOver', '/lobby', 1],
  [{}, 'Main', '/lobby', 0],
]) {
  await check(`actual game return: ${JSON.stringify(room)} / ${phase}`, () => {
    const destinations = []; let leaveCount = 0
    const { returnToLobby } = evaluate(`${functionText(sources.game, 'returnToLobby')}\nexports.returnToLobby = returnToLobby`, {}, {
      tutorial: { value: null }, tutorialTransition: { value: null }, game: { value: { phase } },
      l12State: { room, spectating: false }, router: { push: to => destinations.push(to) }, leaveRoom: () => { leaveCount++ },
    })
    returnToLobby()
    assert.deepEqual(destinations, [expected]); assert.equal(leaveCount, leaves)
  })
}
for (const [key, relative] of Object.entries(paths)) assert.equal(sha256(readFileSync(path.join(root, relative))), sha256(raw[key]), `Source changed during verification: ${relative}`)
console.log(JSON.stringify({ status: 'passed', mode: 'memory-only', cases: cases.length, sourceHashes, checks: cases }, null, 2))

// Opt-in only, after Main grants the shared browser slot. Uses actual product
// ProfilePage/SandboxPage/router; only API data and shell container are synthetic.
if (process.argv.includes('--browser')) await verifyBrowser()

async function verifyBrowser() {
  const output = process.env.L12_QA_OUT
  assert.ok(output && path.isAbsolute(output), 'Browser evidence requires explicit absolute L12_QA_OUT')
  const cache = path.join(process.env.L12_WORK_CACHE || 'D:/GPT/Legion12/cache/primary', 'profile-sandbox-entry-20261022')
  const powershell = process.env.L12_PWSH
  assert.ok(powershell && path.isAbsolute(powershell), 'Run the shared build initializer first (L12_PWSH/TEMP/TMP)')
  const quote = value => `'${value.replaceAll("'", "''")}'`
  for (const value of [process.env.TEMP, process.env.TMP, process.env.TMPDIR]) assert.ok(value && path.isAbsolute(value), 'Explicit governed temp root required')
  const guard = spawnSync(powershell, ['-NoProfile', '-NonInteractive', '-Command', [
    `. ${quote(path.resolve(root, '../ops/windows/L12GeneratedStorage.ps1'))}`,
    ...[output, cache, process.env.TEMP, process.env.TMP, process.env.TMPDIR].map(value => `$null=Assert-L12GeneratedPath -Path ${quote(value)} -Purpose 'profile sandbox browser acceptance'`),
  ].join('\n')], { encoding: 'utf8', windowsHide: true })
  assert.equal(guard.status, 0, `Physical output guard rejected browser run: ${guard.stderr || guard.stdout}`)
  mkdirSync(output, { recursive: true }); mkdirSync(cache, { recursive: true })
  const { createServer } = await import('vite')
  const { default: vue } = await import('@vitejs/plugin-vue')
  const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const shellStyle = [...sources.shell.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map(match => match[1]).join('\n')
  assert.ok(shellStyle.includes('.site-content'), 'Actual shell layout styles required')
  const entry = `
import {createApp,h,nextTick} from 'vue'
import {RouterView} from 'vue-router'
import '/src/style.css'
import '/src/l12/site/uiSystem.css'
import '/__profile_sandbox_shell.css'
const platform=await import('/src/l12/platform.ts')
const net=await import('/src/l12/net.ts')
net.l12State.status='online'
const permissions=['admin.operations.read','admin.operations.write']
const account={id:'synthetic-sandbox-entry',username:'本地验收管理员',role:'admin',createdAt:'2026-10-22T00:00:00Z',publicHistory:false,permissions,permissionVersion:1}
function identity(value={}){
 platform.platformState.account=value.guest?null:{...account,...value}
 platform.platformState.token=value.guest?'':'synthetic-local-token'
 platform.authState.initialized=true;platform.authState.refreshing=false;platform.authState.verified=value.verified!==false&&!value.guest
}
identity()
const stat={games:128,wins:77,losses:51,draws:0,firstGames:64,firstWins:38,secondGames:64,secondWins:39}
platform.playerApi.statistics=async(range='season')=>({range,overall:stat,ranked:stat,masters:[],untilUtc:'2026-10-22T00:00:00Z'})
platform.rankedApi.overview=async()=>({profile:{...account,accountId:account.id,placed:false,faction:'',sevenValue:0,placementPlayed:0,placementWins:0,wins:0,losses:0,winStreak:0,tier:'定级中',rankLabel:'定级中',titles:[],masterTitles:[],selectedMasterTitle:null},history:[],config:{placementMatches:10}})
platform.sessionApi.list=async()=>[]
platform.usernameChangeApi.status=async()=>({freeRenameAvailable:true,freeRenameUsed:0})
platform.adminApi.workbenchSummary=async()=>({sampledAt:'2026-10-22T00:00:00Z',pending:[],anomalies:[],recentActivities:[],partial:false,unavailableSections:[]})
window.history.replaceState(null,'','/me')
const {router}=await import('/src/router/index.ts')
const app=createApp({render:()=>h('div',{class:'site-shell','data-l12-ui-system':'site-v1'},[
 h('aside',{class:'site-sidebar'},'十二军团'),h('header',{class:'site-mobile-head'},'我的 · 本地布局验收'),
 h('main',{class:'site-content ui-state-scope'},h(RouterView)),
])})
app.use(router);await router.isReady();app.mount('#app')
window.__sandboxEntryQA={router,platform,identity,async update(value){identity(value);await nextTick()},async navigate(to){await router.push(to);await nextTick()}}
`
  const server = await createServer({
    configFile: false, root, cacheDir: path.join(cache, 'vite'),
    plugins: [vue(), {
      name: 'profile-sandbox-entry-local-fixture',
      resolveId(id) { if (['/__profile_sandbox_entry.js', '/__profile_sandbox_shell.css'].includes(id)) return id },
      load(id) { if (id === '/__profile_sandbox_entry.js') return entry; if (id === '/__profile_sandbox_shell.css') return shellStyle },
      configureServer(devServer) {
        devServer.middlewares.use((request, response, next) => {
          if (request.url?.startsWith('/__profile_sandbox_entry__')) {
            response.setHeader('Content-Type', 'text/html; charset=utf-8')
            response.end('<div id="app"></div><script type="module" src="/__profile_sandbox_entry.js"></script>')
          } else next()
        })
      },
    }],
    resolve: { alias: { '@': path.join(root, 'src') } },
    define: { 'import.meta.env.VITE_APP_VERSION': JSON.stringify('local-sandbox-entry-qa') },
    server: { host: '127.0.0.1', port: 0, hmr: false, strictPort: false },
  })
  let browser
  const errors = [], unexpectedRequests = [], results = []
  const report = { mode: 'actual-sfc-local-fixture', status: 'running', cases: cases.length, sourceHashes, results, errors, unexpectedRequests }
  try {
    await server.listen()
    const origin = `http://127.0.0.1:${server.httpServer.address().port}`
    browser = await chromium.launch({ headless: true, channel: 'msedge', env: { ...process.env, TEMP: path.join(cache, 'temp'), TMP: path.join(cache, 'temp') } })
    const page = await browser.newPage()
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => {
      const url = new URL(route.request().url())
      if (url.pathname.startsWith('/api/')) {
        const value = url.pathname === '/api/auth/mfa/capability' ? { enrollmentEnabled: false }
          : url.pathname === '/api/auth/sessions/current' && route.request().method() === 'DELETE' ? { revokedCount: 1 }
          : null
        if (value) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) })
        unexpectedRequests.push(`${route.request().method()} ${url.pathname}`)
        return route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"fixture_unexpected_request"}' })
      }
      if (/\/data\/l12\/cards\.(s1|lookup|st)\.json$/.test(url.pathname)) return route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
      if (url.origin !== origin) { unexpectedRequests.push(url.origin + url.pathname); return route.abort() }
      return route.continue()
    })
    await page.goto(`${origin}/__profile_sandbox_entry__`)
    await page.locator('.identity .record-chip').waitFor()
    const viewports = [
      [320,700],[360,780],[361,780],[390,844],[520,800],[521,800],[700,800],[701,800],
      [844,390],[850,800],[851,800],[1024,320],[1100,800],[1101,800],[1280,800],[1440,900],[1920,1080],
    ]
    for (const [width, height] of viewports) {
      await page.setViewportSize({ width, height })
      await page.evaluate(() => window.__sandboxEntryQA.update({ username: '长昵称边界验收号' }))
      const geometry = await page.locator('.identity').evaluate(element => {
        const rect = node => { const box = node.getBoundingClientRect(); return { left: box.left, right: box.right, top: box.top, bottom: box.bottom, width: box.width, height: box.height } }
        const links = [...element.querySelectorAll('.profile-action')]
        const content = document.querySelector('.site-content')
        const copy = element.querySelector('.identity-copy')
        return { card: rect(element), copy: rect(copy), links: links.map(rect), overflow: content.scrollWidth > content.clientWidth + 1, nicknameOverflow: copy.scrollWidth > copy.clientWidth + 1 }
      })
      assert.equal(geometry.links.length, 2)
      assert.equal(geometry.overflow, false, `page overflow at ${width}x${height}`)
      assert.equal(geometry.nicknameOverflow, false, `nickname overflow at ${width}x${height}`)
      for (const link of geometry.links) {
        assert.ok(link.height >= 44 && link.width >= 44, `touch target at ${width}x${height}`)
        assert.ok(link.left >= geometry.card.left && link.right <= geometry.card.right, `action outside identity at ${width}x${height}`)
        assert.ok(link.right <= geometry.copy.left || link.left >= geometry.copy.right || link.top >= geometry.copy.bottom || link.bottom <= geometry.copy.top, `action overlaps identity at ${width}x${height}`)
      }
      if (width <= 360) assert.ok(geometry.links[1].top >= geometry.links[0].bottom)
      else assert.equal(geometry.links[0].top, geometry.links[1].top, `peer actions not aligned at ${width}x${height}`)
      await page.locator('.sandbox-button').focus()
      assert.equal(await page.locator('.sandbox-button').evaluate(element => document.activeElement === element && parseFloat(getComputedStyle(element).outlineWidth) >= 2), true)
      await page.keyboard.press('Tab')
      assert.equal(await page.locator('.admin-button').evaluate(element => document.activeElement === element), true)
      if ([320,390,844,1440].includes(width)) await page.locator('.identity').screenshot({ path: path.join(output, `identity-${width}x${height}.png`) })
      results.push({ viewport: { width, height }, geometry })
    }
    await page.setViewportSize({ width: 390, height: 844 })
    for (const [identity, admin, sandbox] of [[{},1,1],[{permissions:['admin.operations.read']},1,0],[{role:'player'},0,0],[{verified:false},0,0],[{guest:true},0,0]]) {
      await page.evaluate(value => window.__sandboxEntryQA.update(value), identity)
      assert.equal(await page.locator('.admin-button').count(), admin)
      assert.equal(await page.locator('.sandbox-button').count(), sandbox)
    }
    await page.evaluate(() => window.__sandboxEntryQA.update({}))
    await page.locator('.sandbox-button').click()
    await page.locator('.sandbox-page').waitFor()
    assert.equal(await page.locator('.admin-page').count(), 0, 'sandbox still uses admin shell')
    assert.equal(await page.locator('.sandbox-page footer a').getAttribute('href'), '/me')
    await page.locator('.sandbox-page footer a').click()
    await page.locator('.identity-actions').waitFor()
    for (const old of ['/admin/operations/sandbox?source=old#setup','/admin?section=sandbox&source=old#setup']) {
      await page.evaluate(value => window.__sandboxEntryQA.navigate(value), old)
      await page.locator('.sandbox-page').waitFor()
      assert.equal(await page.evaluate(() => window.__sandboxEntryQA.router.currentRoute.value.fullPath), '/sandbox?source=old#setup')
    }
    await page.evaluate(() => window.__sandboxEntryQA.update({ permissions: ['admin.operations.read'] }))
    assert.equal(await page.locator('.sandbox-page footer button').isDisabled(), true)
    assert.equal(await page.locator('.sandbox-deck>button:enabled').count(), 0)
    await page.locator('.sandbox-page footer a').click()
    await page.locator('.identity').waitFor()
    await page.evaluate(() => window.__sandboxEntryQA.update({}))
    await page.locator('.admin-button').click()
    await page.locator('.admin-shell').waitFor()
    assert.equal(await page.locator('.admin-sidebar a[href*="sandbox"],.admin-mobile-navigation option[value*="sandbox"],.workbench a[href*="sandbox"]').count(), 0)
    await page.evaluate(() => window.__sandboxEntryQA.navigate('/me?section=security'))
    const logoutResponse = page.waitForResponse(response => new URL(response.url()).pathname === '/api/auth/sessions/current'
      && response.request().method() === 'DELETE', { timeout: 5_000 })
    await page.locator('.logout').click()
    assert.equal((await logoutResponse).status(), 200, 'real synthetic session revocation must complete successfully')
    await page.locator('.identity-actions').waitFor({ state: 'detached', timeout: 5_000 })
    assert.equal(await page.locator('.identity-actions').count(), 0)
    await page.evaluate(() => window.__sandboxEntryQA.navigate('/sandbox'))
    assert.equal(await page.evaluate(() => window.__sandboxEntryQA.router.currentRoute.value.path), '/me')
    assert.deepEqual(errors, [], 'browser runtime errors')
    assert.deepEqual(unexpectedRequests, [], 'all network must stay in the bounded synthetic fixture')
    for (const [key, relative] of Object.entries(paths)) assert.equal(sha256(readFileSync(path.join(root, relative))), sha256(raw[key]), `Source changed during browser run: ${relative}`)
    report.status = 'passed'
    report.browserChecks = ['17 viewport identity layouts', 'keyboard focus and tab order', '5 live permission states', 'sandbox navigation and return', 'both legacy links preserve query/hash', 'live permission revocation disables sandbox', 'all admin entry consumers removed', 'real sign-out hides entries and denies route']
    console.log(JSON.stringify({ status: report.status, output, viewports: results.length, browserChecks: report.browserChecks }, null, 2))
  } catch (error) {
    report.status = 'failed'; report.failure = error.stack || String(error)
    throw error
  } finally {
    writeFileSync(path.join(output, report.status === 'passed' ? 'browser-report.json' : `browser-failure-${Date.now()}.json`), JSON.stringify(report, null, 2))
    await browser?.close(); await server.close()
  }
}
