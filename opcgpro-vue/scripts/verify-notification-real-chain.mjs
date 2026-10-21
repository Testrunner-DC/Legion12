import assert from 'node:assert/strict'
import path from 'node:path'
import fs from 'node:fs'
import {createRequire} from 'node:module'
import {createServer} from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import {execFileSync} from 'node:child_process'
import {assertLocalPressureTarget} from './local-pressure-protocol.mjs'

export async function closeNotificationProbeResources(browser, vite) {
  const results = await Promise.allSettled([
    Promise.resolve().then(() => browser?.close()),
    Promise.resolve().then(() => vite?.close()),
  ])
  return results.flatMap((result, index) => result.status === 'rejected' ? [index === 0 ? 'browser' : 'vite'] : [])
}

// Called only by the guarded fresh-host owner; no remote endpoint or standalone CLI.
export async function verifyNotificationServerChain({baseUrl, identities, outputRoot, cacheRoot}) {
  assertLocalPressureTarget(baseUrl)
  assert.equal(identities.length, 2)
  const repo = path.resolve(import.meta.dirname, '../..'), front = path.join(repo, 'opcgpro-vue')
  const env = {...process.env, L12_NOTICE_OUT: outputRoot, L12_NOTICE_CACHE: cacheRoot, L12_NOTICE_REPO: repo}
  execFileSync(process.env.L12_PWSH || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe',
    ['-NoProfile', '-Command', '. "$env:L12_NOTICE_REPO/ops/windows/L12GeneratedStorage.ps1"; '
      + '@($env:L12_NOTICE_OUT,$env:L12_NOTICE_CACHE,$env:TEMP) | ForEach-Object {$null=Assert-L12GeneratedPath -Path $_ -Purpose "notification local real chain"}'],
    {env, windowsHide: true, stdio: 'pipe'})
  const state = {status: 'starting', scope: 'actual App/net/resources and real local server; native OS toast/click not accepted',
    productionTouched: false, testServiceTouched: false, stage: 'start', realServerEvents: [], osToastVerified: false,
    osClickVerified: false, humanAudibilityVerified: false, permissionPromptsAccepted: false}
  const errors = []
  let vite, browser
  const entry = `
import {createApp,h} from 'vue'; import {createRouter,createMemoryHistory} from 'vue-router';
import App from '/src/App.vue'; import '/src/style.css';
import * as platform from '/src/l12/platform'; import * as net from '/src/l12/net';
import {friendResource} from '/src/l12/friendResource'; import {chatResource} from '/src/l12/chatResource';
await platform.initializeAuth();
const View={render:()=>h('button',{id:'notification-prime'},'开始本地通知验证')};
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:View,meta:{immersive:true,editorAdaptiveCanvas:true}}]});
await router.push('/qa-notification');const app=createApp(App);app.use(router);await router.isReady();app.mount('#app');
window.__noticeChain={platform,net,chatResource,friendResource,ready:true,snapshot:()=>({
online:net.l12State.status==='online',friendLoaded:friendResource.loaded,chatEnabled:chatResource.state.enabled,
signals:window.__noticeObservation.signals,tones:window.__noticeObservation.tones,
invited:Boolean(net.l12State.friendInvitation),matched:Boolean(net.l12State.matchFound),
room:Boolean(net.l12State.room),unread:Object.values(chatResource.state.threads).reduce((n,t)=>n+t.unread,0),
permission:window.Notification?.permission??'unsupported',secure:window.isSecureContext})};
`
  try {
    vite = await createServer({configFile: false, root: front, cacheDir: path.join(cacheRoot, 'notification-vite'),
      resolve: {alias: {'@': path.join(front, 'src')}}, plugins: [vue(), tailwindcss(), {
        name: 'local-notification-observation', enforce: 'pre',
        transform(code, id) {
          if (id.split('?')[0].endsWith('/notifications/useRealtimeNotifications.ts')) {
            const anchor = 'const now = Date.now()'
            assert.equal(code.split(anchor).length, 2)
            return code.replace(anchor, "window.__noticeObservation?.signals.push(kind);" + anchor)
          }
          if (id.split('?')[0].endsWith('/game/useL12ActionAudio.ts')) {
            const anchor = 'oscillator.start(start)'
            assert.equal(code.split(anchor).length, 2)
            return code.replace(anchor, anchor + ";window.__noticeObservation?.tones.push({state:current.state});")
          }
        },
        configureServer(server) {server.middlewares.use((req, res, next) => {
          if (req.url?.startsWith('/__notification_chain__')) {
            res.setHeader('Content-Type', 'text/html; charset=utf-8')
            res.end('<div id="app"></div><script type="module" src="/@notification-chain"></script>'); return
          }
          next()
        })},
        resolveId(id) {if (id === '/@notification-chain') return '\0notification-chain'},
        load(id) {if (id === '\0notification-chain') return entry},
      }], server: {host: '127.0.0.1', port: 0, proxy: {'/api': baseUrl, '/health': baseUrl,
        '/ws': {target: baseUrl, ws: true}}}})
    await vite.listen()
    const origin = vite.resolvedUrls.local[0]
    const {chromium} = createRequire(import.meta.url)('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
    browser = await chromium.launch({channel: 'msedge', headless: true})
    const pages = []
    for (const identity of identities) {
      const context = await browser.newContext({viewport: {width: 1366, height: 800}})
      await context.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
      await context.addInitScript(({token, account}) => {
        localStorage.setItem('l12-auth-token', token); localStorage.setItem('l12-account', JSON.stringify(account))
        localStorage.setItem('l12-endpoint', 'ws://' + location.host + '/ws')
        window.__noticeObservation = {signals: [], tones: []}
      }, {token: identity.token, account: identity.accountView})
      const page = await context.newPage(); pages.push(page)
      page.on('pageerror', error => {
        let message = error.message
        for (const identity of identities) for (const value of [identity.id, identity.token, identity.username]) message = message.replaceAll(value, '[synthetic-redacted]')
        errors.push({name: error.name, message: message.replace(/[a-f0-9]{64}/gi, '[digest-redacted]').slice(0, 240)})
      })
      await page.goto(origin + '__notification_chain__')
      await page.waitForFunction(() => window.__noticeChain?.ready && window.__noticeChain.snapshot().online
        && window.__noticeChain.snapshot().friendLoaded && window.__noticeChain.snapshot().chatEnabled)
      await page.locator('#notification-prime').click()
    }
    const [a, b] = pages
    const wait = ms => new Promise(resolve => setTimeout(resolve, ms))
    let previousTones = 0
    async function received(kind, expectedSignals = 1) {
      await a.waitForFunction(({kind,count}) => window.__noticeChain.snapshot().signals.filter(value=>value===kind).length >= count,
        {kind,count:expectedSignals}, {timeout: 10000})
      const snapshot = await a.evaluate(() => window.__noticeChain.snapshot())
      assert.equal(snapshot.signals.filter(value => value === kind).length, expectedSignals)
      assert.ok(snapshot.tones.length > previousTones && snapshot.tones.every(value => value.state === 'running'))
      previousTones = snapshot.tones.length
      state.realServerEvents.push({kind, signalOrdinal: expectedSignals, nativeOscillatorStarts: snapshot.tones.length, unreadAtSignal: snapshot.unread})
      await wait(3600)
    }
    state.stage = 'friend-request'
    await b.evaluate(id => window.__noticeChain.platform.friendApi.request(id), identities[0].id)
    await received('friend_request')
    await a.getByRole('button', {name: '通过', exact: true}).click()
    await a.waitForFunction(() => !window.__noticeChain.friendResource.requests.length)
    state.stage = 'friend-challenge'
    await b.evaluate(id => window.__noticeChain.net.inviteFriend(id), identities[0].id)
    await received('friend_challenge')
    await a.evaluate(() => {const q=window.__noticeChain;q.net.resolveFriendInvitation(q.net.l12State.friendInvitation.invitationId,false)})
    await a.waitForFunction(() => !window.__noticeChain.snapshot().invited)
    state.stage = 'chat-message'
    await b.evaluate(id => {const q=window.__noticeChain;const conversation={scope:'friend',peerAccountId:id};q.chatResource.thread(conversation).draft='本机合成通知验证';q.chatResource.send(conversation)}, identities[0].id)
    await received('chat_message')
    state.stage = 'match-found'
    await a.evaluate(() => window.__noticeChain.net.joinMatchmaking('casual'))
    await b.evaluate(() => window.__noticeChain.net.joinMatchmaking('casual'))
    await received('match_found')
    state.stage = 'opponent-chat-message'
    await a.waitForFunction(() => Boolean(window.__noticeChain.net.l12State.game))
    await b.evaluate(id => {const q=window.__noticeChain;const conversation={scope:'opponent',peerAccountId:id,matchId:q.net.l12State.game.matchId};q.chatResource.thread(conversation).draft='本机合成对局消息验证';q.chatResource.send(conversation)}, identities[0].id)
    await received('chat_message', 2)
    state.stage = 'final-state-contract'
    const final = await a.evaluate(() => window.__noticeChain.snapshot())
    state.finalSnapshot = final; state.browserErrors = errors
    // net clears the transient matchFound after the authoritative snapshot arrives.
    assert.equal(final.room, true); assert.equal(final.matched, false); assert.ok(final.unread > 0)
    state.transientMatchNoticeClearedAfterSnapshot = true
    assert.equal(errors.length, 0)
    Object.assign(state, {status: 'server-to-consumer-passed', notificationPermission: final.permission,
      secureContext: final.secure, notificationApiReplaced: false, audioApiReplaced: false,
      frontendSourceInstrumentedPassively: true, observationBoundary: 'Only signal kinds and successful native oscillator starts; no permissions granted, no OS toast/click claim.'})
    state.stage = 'ordinary-room-exit'
    await a.waitForFunction(() => Boolean(window.__noticeChain.net.l12State.game))
    await a.evaluate(() => window.__noticeChain.net.gameAction({type:'surrender'}))
    await a.waitForFunction(() => window.__noticeChain.net.l12State.game?.phase === 'GameOver')
    for (const page of pages) await page.evaluate(() => window.__noticeChain.net.leaveRoom())
    for (const page of pages) await page.waitForFunction(() => !window.__noticeChain.net.l12State.room)
    state.ordinaryRoomExit = true
  } catch (error) {
    state.status = 'failed'; state.error = error.code ?? error.name
  } finally {
    state.cleanupResourceFailures = await closeNotificationProbeResources(browser, vite)
    if (state.cleanupResourceFailures.length) state.status = 'failed'
    state.browserClosed = !browser || !browser.isConnected(); state.viteClosed = !vite?.httpServer?.listening
    fs.writeFileSync(path.join(outputRoot, 'notification-real-chain.json'), JSON.stringify(state, null, 2), {flag: 'wx'})
  }
  assert.equal(state.status, 'server-to-consumer-passed', 'real notification chain stage: ' + state.stage)
  assert.equal(state.browserClosed && state.viteClosed, true)
  return state
}
