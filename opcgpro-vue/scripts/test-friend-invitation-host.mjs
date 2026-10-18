import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const front = path.resolve(import.meta.dirname, '..')
const app = fs.readFileSync(path.join(front, 'src/App.vue'), 'utf8')
const shell = fs.readFileSync(path.join(front, 'src/l12/site/SiteShell.vue'), 'utf8')
const sharedPath = path.join(front, 'src/l12/site/FriendChallengeNotifications.vue')
const shared = fs.existsSync(sharedPath) ? fs.readFileSync(sharedPath, 'utf8') : ''
const out = process.env.L12_FRIEND_INVITE_OUTPUT
const cacheDir = process.env.L12_VITE_CACHE
const sha256 = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex').toUpperCase()

if (out) {
  assert(cacheDir, 'focused Vite cache must be explicitly selected when browser evidence is requested')
  execFileSync(process.env.L12_PWSH || 'pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command',
    '. $env:L12_STORAGE_GUARD; $null=Assert-L12GeneratedPath -Path $env:L12_FRIEND_INVITE_OUTPUT -Purpose "friend invitation browser evidence"; $null=Assert-L12GeneratedPath -Path $env:L12_VITE_CACHE -Purpose "friend invitation Vite cache"'], {
    stdio: 'pipe',
    env: { ...process.env, L12_STORAGE_GUARD: path.join(front, '../ops/windows/L12GeneratedStorage.ps1') },
  })
}

try {
  assert(fs.existsSync(sharedPath), 'RED: the friend challenge notification has no shared owner outside SiteShell')
  assert(app.includes('FriendChallengeNotifications'), 'RED: App does not host friend challenge notifications for the immersive editor')
  assert(!shell.includes('class="invitation-stack'), 'RED: SiteShell still privately owns the friend challenge notification')
  assert(app.includes('!immersive.value || adaptiveEditor.value'), 'shared host must cover ordinary site and editor, without expanding to other immersive routes')
  assert(shared.includes('connectedAccountId') && shared.includes('invitation.invitationId'), 'shared host must bind displayed actions to the connected account and exact invitation id')
} catch (error) {
  if (out) {
    fs.mkdirSync(out, { recursive: true })
    fs.writeFileSync(path.join(out, 'red.json'), `${JSON.stringify({
      status: 'RED',
      bugId: 'BUG-20260910-0ac784d3',
      error: error instanceof Error ? error.message : String(error),
      sourceSha256: { 'src/App.vue': sha256(path.join(front, 'src/App.vue')), 'src/l12/site/SiteShell.vue': sha256(path.join(front, 'src/l12/site/SiteShell.vue')) },
    }, null, 2)}\n`)
  }
  throw error
}

console.log('PASS friend invitation shared-host static contract')

if (out) {
  assert(cacheDir && path.resolve(cacheDir).toLowerCase().startsWith(path.resolve('D:/GPT/Legion12/cache/primary/friend-invitation-host-20261018').toLowerCase()),
    'focused Vite cache must stay inside the approved physical D cache root')
  const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT
    || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
  const files = ['src/App.vue', 'src/l12/site/SiteShell.vue', 'src/l12/site/FriendChallengeNotifications.vue',
    'src/l12/L12DeckEditor.vue', 'src/l12/net.ts', 'src/l12/platform.ts', 'src/l12/mobileViewport.ts']
  const report = {
    status: 'RUNNING',
    bugId: 'BUG-20260910-0ac784d3',
    cases: [],
    dialogs: [],
    errors: [],
    sourceSha256: Object.fromEntries(files.map(file => [file, sha256(path.join(front, file))])),
    limitations: [
      'Synthetic public invitation and acknowledgement messages are delivered to the real current App/component consumer; no production account, server, room or private deck is used.',
      'Headless Edge viewports are representative browser geometry, not physical iOS or Android hardware.',
      'The focused run proves the existing editor DOM and unsaved name survive notification-only actions; it does not publish or save a deck.',
    ],
  }
  const entry = [
    "import {createApp,markRaw} from 'vue'",
    "import {createRouter,createMemoryHistory} from 'vue-router'",
    "import App from '/src/App.vue'",
    "import Editor from '/src/l12/L12DeckEditor.vue'",
    "import {platformState,authState} from '/src/l12/platform.ts'",
    "import {l12State} from '/src/l12/net.ts'",
    "import '/src/style.css'",
    "platformState.account={id:'account-a',username:'账号A',role:'player',permissions:[]};platformState.token='synthetic-token';authState.initialized=true;authState.verified=false",
    "const marker={template:'<main class=\"route-marker\"><input value=\"ordinary-site\"></main>'}",
    "const immersive={template:'<main class=\"immersive-marker\">immersive</main>'}",
    "const router=createRouter({history:createMemoryHistory(),routes:[{path:'/deck-editor',component:Editor,meta:{immersive:true,landscapeCanvas:true,editorAdaptiveCanvas:true}},{path:'/battle',component:marker},{path:'/site',component:marker},{path:'/game',component:immersive,meta:{immersive:true,landscapeCanvas:true}},{path:'/replay',component:immersive,meta:{immersive:true,replay:true}}]})",
    "await router.push('/deck-editor');const pushCalls=[];const originalPush=router.push.bind(router);router.push=to=>{pushCalls.push(typeof to==='string'?to:JSON.stringify(to));return originalPush(to)}",
    "createApp(App).use(router).mount('#app')",
    "const sent=[];const socket=markRaw({readyState:WebSocket.OPEN,send:raw=>sent.push(JSON.parse(raw)),close(){}})",
    "const connect=id=>{platformState.account={id,username:id,role:'player',permissions:[]};l12State.accountId=id;l12State.status='online';l12State.socket=socket}",
    "connect('account-a')",
    "window.__friendQa={router,pushCalls,sent,l12State,platformState,connect,incoming:(id='incoming-a')=>{l12State.friendInvitation={invitationId:id,roomCode:'QA1234',fromAccountId:'friend-a',fromName:'邀请方测试昵称'}},outgoing:(id='outgoing-a')=>{l12State.outgoingFriendInvitation={invitationId:id,roomCode:'QA5678',targetAccountId:'friend-a'}},withdrawIncoming:()=>{l12State.friendInvitation=null},withdrawOutgoing:()=>{l12State.outgoingFriendInvitation=null},disconnect:()=>{l12State.status='offline';l12State.accountId='';l12State.socket=null},switchPlatform:id=>{platformState.account={id,username:id,role:'player',permissions:[]}},roomCreated:id=>window.dispatchEvent(new CustomEvent('l12-friend-room-created',{detail:{invitationId:id}}))}",
  ].join('\n')
  const server = await createServer({
    root: front,
    configLoader: 'runner',
    cacheDir,
    server: { host: '127.0.0.1', port: 0, strictPort: false },
    plugins: [{
      name: 'friend-invitation-host-fixture',
      resolveId(id) { if (id === '/__friend_invitation_host__.js') return id },
      load(id) { if (id === '/__friend_invitation_host__.js') return entry },
      configureServer(vite) {
        vite.middlewares.use((request, response, next) => {
          if (new URL(request.url || '/', 'http://fixture').pathname !== '/__friend_invitation_host__') return next()
          response.setHeader('Content-Type', 'text/html')
          response.end('<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"></head><body><div id="app"></div><script type="module" src="/__friend_invitation_host__.js"></script></body></html>')
        })
      },
    }],
  })
  let browser
  const cases = async (name, work) => {
    await work()
    report.cases.push(name)
    console.log('PASS ' + report.cases.length + ': ' + name)
  }
  try {
    await server.listen()
    browser = await chromium.launch({ headless: true, channel: 'msedge' })
    const port = server.httpServer.address().port
    for (const viewport of [{ width: 1366, height: 768 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
      const label = viewport.width + 'x' + viewport.height
      const context = await browser.newContext({ viewport, hasTouch: viewport.width <= 844, isMobile: viewport.width < 700 })
      await context.addInitScript(() => localStorage.setItem('l12-audio-preferences-v1', JSON.stringify({
        musicEnabled: false, sfxEnabled: false, mobileLayout: 'auto', cardSize: 'medium', animation: 'off',
      })))
      await context.route('**/*', async route => {
        const request = route.request()
        const url = new URL(request.url())
        if (url.hostname !== '127.0.0.1') return route.abort()
        if (url.pathname === '/api/decks/summaries') return route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 30, generation: 1, permissionVersion: 1, catalogVersion: 'A'.repeat(64), policyVersion: 1, facets: { masters: [], legal: 0, illegal: 0 } } })
        if (url.pathname === '/api/decks') return route.fulfill({ json: [] })
        if (url.pathname === '/api/operations/effective-policy') return route.fulfill({ json: { cardRestrictions: [], defaultPresetDeckIds: [], ranked: {}, season: {} } })
        if (url.pathname === '/api/auth/me') return route.fulfill({ json: { id: 'account-a', username: '账号A', role: 'player', permissions: [] } })
        if (url.pathname === '/api/friends/overview') return route.fulfill({ json: { friends: [], requests: [], blocked: [] } })
        if (url.pathname.includes('notifications')) return route.fulfill({ json: { items: [], nextCursor: null } })
        if (url.pathname.startsWith('/api/telemetry/')) return route.fulfill({ status: 204 })
        if (url.pathname.startsWith('/api/')) return route.fulfill({ json: [] })
        return route.continue()
      })
      const page = await context.newPage()
      page.setDefaultTimeout(12_000)
      page.on('pageerror', error => report.errors.push(label + ' pageerror: ' + (error.stack || error.message)))
      page.on('console', message => { if (message.type() === 'error') report.errors.push(label + ' console: ' + message.text()) })
      await page.goto('http://127.0.0.1:' + port + '/__friend_invitation_host__')
      const draft = page.locator('.deck-builder-topbar input')
      await draft.waitFor()
      await draft.fill('邀请期间未保存草稿')
      await page.evaluate(() => window.__friendQa.incoming('incoming-' + innerWidth))
      const incoming = page.locator('.invitation-gate')
      await incoming.waitFor()
      await cases(label + ' editor hosts exactly one nonmodal invitation without rebuilding draft', async () => {
        assert.equal(await page.locator('.invitation-stack').count(), 1)
        assert.equal(await incoming.getByRole('dialog').getAttribute('aria-modal'), 'false')
        assert.equal(await draft.inputValue(), '邀请期间未保存草稿')
        assert.equal(await page.locator('.deck-builder-shell').count(), 1)
        const bounds = await page.locator('.invitation-stack').boundingBox()
        assert(bounds && bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= viewport.width + 1 && bounds.y + bounds.height <= viewport.height + 1)
        await page.screenshot({ animations: 'disabled', path: path.join(out, 'editor-invitation-' + label + '.png') })
      })
      await page.getByRole('button', { name: '最小化好友对战邀请', exact: true }).click()
      await page.locator('.invitation-minimized').waitFor()
      await page.evaluate(() => window.__friendQa.incoming('incoming-' + innerWidth))
      await page.waitForTimeout(50)
      await cases(label + ' repeated authoritative message preserves minimized editor and draft', async () => {
        assert.equal(await page.locator('.invitation-minimized').count(), 1)
        assert.equal(await draft.inputValue(), '邀请期间未保存草稿')
      })
      await page.locator('.invitation-minimized').click()
      await page.getByRole('button', { name: '拒绝', exact: true }).click()
      await cases(label + ' reject sends the exact existing command without navigation', async () => {
        assert.deepEqual(await page.evaluate(() => window.__friendQa.sent.at(-1)), { type: 'resolveFriendInvitation', invitationId: 'incoming-' + viewport.width, accept: false })
        assert.equal(await page.evaluate(() => window.__friendQa.router.currentRoute.value.path), '/deck-editor')
        assert.equal(await draft.inputValue(), '邀请期间未保存草稿')
      })
      await page.evaluate(() => window.__friendQa.outgoing('outgoing-' + innerWidth))
      const outgoing = page.locator('.outgoing-invitation-gate')
      await outgoing.waitFor()
      await page.getByRole('button', { name: '撤回邀请', exact: true }).click()
      await cases(label + ' outgoing cancel keeps the card until matching authority withdrawal', async () => {
        assert.deepEqual(await page.evaluate(() => window.__friendQa.sent.at(-1)), { type: 'cancelFriendInvitation', invitationId: 'outgoing-' + viewport.width })
        assert.equal(await outgoing.count(), 1)
        const pushes = await page.evaluate(() => window.__friendQa.pushCalls.length)
        await page.evaluate(() => window.__friendQa.withdrawOutgoing())
        await outgoing.waitFor({ state: 'detached' })
        await page.evaluate(id => window.__friendQa.roomCreated(id), 'outgoing-' + viewport.width)
        await page.waitForTimeout(50)
        assert.equal(await page.evaluate(() => window.__friendQa.router.currentRoute.value.path), '/deck-editor')
        assert.equal(await page.evaluate(() => window.__friendQa.pushCalls.length), pushes)
      })
      await page.evaluate(() => window.__friendQa.incoming('same-tick-old-account'))
      await incoming.waitFor()
      await page.evaluate(() => {
        window.__friendQa.switchPlatform('account-b')
        window.__friendQa.switchPlatform('account-a')
      })
      await page.waitForTimeout(0)
      await cases(label + ' same-tick A-B-A retires the stale owner and a later invitation can bind', async () => {
        assert.equal(await incoming.count(), 0)
        await page.evaluate(() => window.__friendQa.incoming('same-tick-new-account'))
        await incoming.waitFor()
        assert.equal(await incoming.getByText('邀请方测试昵称', { exact: true }).count(), 1)
        await page.evaluate(() => window.__friendQa.withdrawIncoming())
        await incoming.waitFor({ state: 'detached' })
      })
      await page.evaluate(() => window.__friendQa.incoming('old-account'))
      await incoming.waitFor()
      await page.evaluate(() => window.__friendQa.switchPlatform('account-b'))
      await incoming.waitFor({ state: 'detached' })
      await page.evaluate(() => window.__friendQa.connect('account-b'))
      await page.waitForTimeout(50)
      await page.evaluate(() => window.__friendQa.connect('account-a'))
      await page.waitForTimeout(50)
      await cases(label + ' account A-B-A switch never rebinds the stale invitation', async () => assert.equal(await incoming.count(), 0))
      await page.evaluate(() => window.__friendQa.connect('account-b'))
      await page.evaluate(() => window.__friendQa.incoming('new-account'))
      await incoming.waitFor()
      await page.evaluate(() => window.__friendQa.disconnect())
      await incoming.waitFor({ state: 'detached' })
      await page.evaluate(() => window.__friendQa.connect('account-b'))
      await incoming.waitFor()
      await cases(label + ' disconnect hides and same-account reconnect restores only its bound invitation', async () => {
        assert.equal(await incoming.getByText('邀请方测试昵称', { exact: true }).count(), 1)
      })
      await page.evaluate(() => window.__friendQa.withdrawIncoming())
      await incoming.waitFor({ state: 'detached' })
      await draft.fill('邀请期间未保存草稿')
      await page.evaluate(() => window.__friendQa.incoming('accept-current'))
      await incoming.waitFor()
      await page.getByRole('button', { name: '接受并进入房间', exact: true }).click()
      await page.evaluate(() => window.__friendQa.roomCreated('wrong-id'))
      await page.waitForTimeout(50)
      await cases(label + ' accept uses exact id and only matching created-room acknowledgement navigates', async () => {
        assert.deepEqual(await page.evaluate(() => window.__friendQa.sent.at(-1)), { type: 'resolveFriendInvitation', invitationId: 'accept-current', accept: true })
        assert.equal(await page.evaluate(() => window.__friendQa.router.currentRoute.value.path), '/deck-editor')
        assert.equal(await draft.inputValue(), '邀请期间未保存草稿')
        page.once('dialog', async dialog => {
          report.dialogs.push({ viewport: label, type: dialog.type(), message: dialog.message() })
          assert(dialog.message().includes('当前修改尚未保存'))
          await dialog.accept()
        })
        await page.evaluate(() => window.__friendQa.roomCreated('accept-current'))
        await page.waitForFunction(() => window.__friendQa.router.currentRoute.value.path === '/battle').catch(async error => {
          const state = await page.evaluate(() => ({
            route: window.__friendQa.router.currentRoute.value.path,
            pushCalls: window.__friendQa.pushCalls,
            accountId: window.__friendQa.l12State.accountId,
            platformAccountId: window.__friendQa.platformState.account?.id,
            status: window.__friendQa.l12State.status,
          }))
          throw new Error('Matching created-room event did not navigate: ' + JSON.stringify(state), { cause: error })
        })
        const pushes = await page.evaluate(() => window.__friendQa.pushCalls.length)
        await page.evaluate(() => window.__friendQa.roomCreated('accept-current'))
        await page.waitForTimeout(50)
        assert.equal(await page.evaluate(() => window.__friendQa.pushCalls.length), pushes)
      })
      if (viewport.width === 1366) {
        await page.evaluate(async () => { await window.__friendQa.router.push('/site'); window.__friendQa.connect('account-b'); window.__friendQa.incoming('site-one') })
        await incoming.waitFor()
        await cases('ordinary site has one shared notification owner', async () => assert.equal(await page.locator('.invitation-stack').count(), 1))
        await page.evaluate(async () => { await window.__friendQa.router.push('/game'); window.__friendQa.incoming('game-hidden') })
        await page.waitForTimeout(50)
        await cases('immersive game does not gain the editor notification', async () => assert.equal(await page.locator('.invitation-stack').count(), 0))
        await page.evaluate(async () => { await window.__friendQa.router.push('/replay'); window.__friendQa.incoming('replay-hidden') })
        await page.waitForTimeout(50)
        await cases('read-only replay does not gain the editor notification', async () => assert.equal(await page.locator('.invitation-stack').count(), 0))
      }
      await context.close()
    }
    assert.deepEqual(report.errors, [], 'focused browser run must not hide page or console errors')
    report.status = 'GREEN'
  } catch (error) {
    report.status = 'FAILED'
    report.failure = error instanceof Error ? error.stack || error.message : String(error)
    throw error
  } finally {
    await browser?.close()
    await server.close()
    fs.mkdirSync(out, { recursive: true })
    fs.writeFileSync(path.join(out, 'result.json'), JSON.stringify(report, null, 2) + '\n')
    const resolvedCache = path.resolve(cacheDir)
    const allowedCache = path.resolve('D:/GPT/Legion12/cache/primary/friend-invitation-host-20261018')
    if (resolvedCache.startsWith(allowedCache + path.sep)) fs.rmSync(resolvedCache, { recursive: true, force: true })
  }
}

