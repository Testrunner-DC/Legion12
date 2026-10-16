import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(process.env.L12_CHAT_UI_OUT || `D:/GPT/Legion12/artifacts/chat-ui-${Date.now()}`)
assert(!fs.existsSync(output), 'Never overwrite previous UI evidence')
fs.mkdirSync(output, { recursive: true })
const { chromium } = createRequire(import.meta.url)('C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sources = ['src/l12/chatResource.ts', 'src/l12/site/ChatPanel.vue', 'src/l12/site/SocialPopover.vue', 'src/l12/site/SiteShell.vue', 'src/l12/site/FriendsPage.vue', 'src/l12/game/BattleUtilityDock.vue', 'src/l12/net.ts', 'src/l12/platform.ts', 'src/l12/friendResource.ts', 'scripts/verify-chat-ui.mjs']
const sha = value => crypto.createHash('sha256').update(value).digest('hex').toUpperCase()
const hashes = () => Object.fromEntries(sources.map(file => [file, sha(fs.readFileSync(path.join(root, file)))]))
const before = hashes(), report = { status: 'RUNNING', sourceHashes: before, cases: [], errors: [], synthetic: true, realServer: false }
const entry = `
import '/src/style.css';import '/src/l12/motion.css';import '/src/l12/site/uiSystem.css';
import {createApp,h,ref,nextTick} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router';
import SocialPopover from '/src/l12/site/SocialPopover.vue';import SiteShell from '/src/l12/site/SiteShell.vue';import FriendsPage from '/src/l12/site/FriendsPage.vue';import BattleUtilityDock from '/src/l12/game/BattleUtilityDock.vue';
import {chatResource} from '/src/l12/chatResource.ts';import {friendApi,platformState} from '/src/l12/platform.ts';import {l12State} from '/src/l12/net.ts';
import {alternateArtApi,seasonSummaryApi,telemetryApi} from '/src/l12/platform.ts';
const friend={accountId:'bob',username:'合成好友乙',status:'accepted',online:true},newestFriend={accountId:'carol',username:'合成好友丙',status:'accepted',online:false};
platformState.account={id:'alice',username:'合成甲',role:'player'};platformState.token='synthetic-not-a-token';
window.__overviewCalls=0;friendApi.overview=async()=>{window.__overviewCalls++;return{friends:[friend,newestFriend],requests:[],blocked:[]}};
alternateArtApi.notifications=async()=>[];seasonSummaryApi.notifications=async()=>[];telemetryApi.pageView=async()=>{};
l12State.status='online';l12State.spectating=new URLSearchParams(location.search).get('mode')==='spectator';l12State.accountId='alice';
l12State.presence=[{accountId:'alice',username:'合成甲',online:true,activity:'idle',friendStatus:'self',canInvite:false,canSpectate:false}];
l12State.game={matchId:'synthetic-match',phase:'Main',matchGovernance:{supported:true,opponentAccountId:'bob',opponentName:friend.username}};
window.__sent=[];window.__outside=0;window.__chat=chatResource;
chatResource.connect('alice',true,payload=>{window.__sent.push(payload);if(payload.type==='chatSync')queueMicrotask(()=>chatResource.receive({type:'chatHistory',requestId:payload.requestId,entries:[],hasMore:false}));if(payload.type==='chatRead')queueMicrotask(()=>chatResource.receive({type:'chatReadAck',requestId:payload.requestId,success:true,seq:payload.seq}));return true});
window.__ack=()=>{const p=window.__sent.filter(p=>p.type==='chatSend').at(-1);chatResource.receive({type:'chatAck',requestId:p.requestId,success:true,entry:{id:crypto.randomUUID(),seq:1,scope:p.scope,matchId:p.matchId,senderId:'alice',senderName:'合成甲',recipientId:p.peerAccountId,text:p.text,createdAt:new Date().toISOString(),moderated:false,policyVersion:'chat-v1'}})};
const mode=new URLSearchParams(location.search).get('mode');
if(mode==='site-unread'){chatResource.receive({type:'chatMessage',entry:{id:'older-bob',seq:1,scope:'friend',senderId:'bob',senderName:friend.username,recipientId:'alice',text:'较早未读',createdAt:'2026-10-17T01:00:00Z',moderated:false,policyVersion:'chat-v1'}});chatResource.receive({type:'chatMessage',entry:{id:'newer-carol',seq:1,scope:'friend',senderId:'carol',senderName:newestFriend.username,recipientId:'alice',text:'最新未读',createdAt:'2026-10-17T02:00:00Z',moderated:false,policyVersion:'chat-v1'}})}
const router=createRouter({history:createMemoryHistory(),routes:[{path:'/',component:{render:()=>null}},{path:'/friends',component:{render:()=>null}}]});await router.push('/');
const app=createApp({setup(){const open=ref(false),anchor=ref(null);return()=>h('main',[
h('button',{id:'outside',onClick:()=>window.__outside++},'外部功能'),mode?.startsWith('site-')?h(SiteShell,null,{default:()=>h('div',{id:'site-slot'},'主页内容')}):mode==='friends'?h(FriendsPage):mode==='battle'||mode==='spectator'?h('div',{class:'dock-host'},[h(BattleUtilityDock)]):h('div',{class:'dock-host'},[
h('button',{id:'open',ref:anchor,onClick:()=>open.value=!open.value},'好友消息'),open.value?h(SocialPopover,{anchor:anchor.value,onClose:()=>open.value=false}):null])])}});app.use(router);app.mount('#app');await nextTick();window.__ready=true;
`
const html = '<!doctype html><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;background:#071016;color:#fff;font-family:"Microsoft YaHei",sans-serif}#outside{position:fixed;right:8px;top:8px;min-height:36px;z-index:1}.dock-host{position:fixed;bottom:12px;left:12px;width:240px}button{cursor:pointer}</style><div id="app"></div><script type="module" src="/__chat_entry.js"></script>'
const server = await createServer({ root, configLoader: 'runner', cacheDir: 'D:/GPT/Legion12/cache/primary/chat-ui-vite', server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'chat-bounded-ui-fixture', resolveId(id) { if (id === '/__chat_entry.js') return id }, load(id) { if (id === '/__chat_entry.js') return entry },
  configureServer(vite) { vite.middlewares.use((request, response, next) => { if (request.url?.startsWith('/__chat?')) { response.setHeader('Content-Type', 'text/html'); response.end(html) } else next() }) },
}] })
let browser
try {
  await server.listen(); const base = server.resolvedUrls.local[0]
  browser = await chromium.launch({ channel: 'msedge', headless: true })
  for (const [mode, viewport, touch] of [
    ['social', { width: 1920, height: 1080 }, false], ['social', { width: 390, height: 844 }, true],
    ['social', { width: 844, height: 390 }, true], ['social', { width: 568, height: 320 }, true],
    ['social', { width: 320, height: 568 }, true], ['battle', { width: 844, height: 390 }, true],
    ['spectator', { width: 390, height: 844 }, true], ['friends', { width: 1366, height: 768 }, false],
    ['friends', { width: 390, height: 844 }, true], ['site-unread', { width: 1366, height: 768 }, false],
    ['site-clean', { width: 390, height: 844 }, true],
  ]) {
    const result = { mode, viewport, touch, status: 'RUNNING', pageErrors: [], external: [], httpApi: [] }; report.cases.push(result)
    const context = await browser.newContext({ viewport, hasTouch: touch })
    await context.route('**/*', async route => { const url = new URL(route.request().url()); if (!['127.0.0.1', 'localhost'].includes(url.hostname)) { result.external.push(url.origin); await route.abort(); return } if (url.pathname.startsWith('/api/')) { result.httpApi.push(url.pathname); await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }); return } await route.continue() })
    await context.routeWebSocket(url => !['127.0.0.1', 'localhost'].includes(new URL(url).hostname), socket => { result.external.push('websocket'); socket.close() })
    const page = await context.newPage(); page.on('pageerror', error => result.pageErrors.push(error.message)); page.setDefaultTimeout(15_000)
    await page.goto(`${base}__chat?mode=${mode}`); await page.waitForFunction(() => window.__ready)
    const click = async locator => { await locator.scrollIntoViewIfNeeded(); const box = await locator.boundingBox(); assert(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1); if (touch) await locator.tap(); else await locator.click() }
    if (mode.startsWith('site-')) {
      if (viewport.width <= 700) await click(page.getByRole('button', { name: '打开导航', exact: true }))
      const utilities = page.locator('.site-utilities > button'); assert.equal(await utilities.count(), 4)
      for (let index = 0; index < 4; index++) {
        const utility = utilities.nth(index), utilityBox = await utility.boundingBox()
        assert(utilityBox && utilityBox.width >= 44 && utilityBox.height >= 44)
        await utility.click({ trial: true })
      }
      assert.equal(await utilities.nth(0).getAttribute('aria-label'), '设置'); assert.equal(await utilities.nth(1).getAttribute('aria-label'), '更新日志')
      const utilityText = await utilities.allTextContents(); assert(!utilityText.some(text => /设置|更新日志|在线 \/ 消息|连接正常/.test(text)))
      assert.match(await utilities.nth(2).getAttribute('aria-label'), /^在线玩家与好友消息：1 人在线/)
      assert.equal(await utilities.nth(3).getAttribute('aria-label'), '连接正常')
      await click(utilities.nth(2)); const popover = page.locator('[data-l12-social-popover]'); await popover.waitFor({ state: 'visible' })
      if (mode === 'site-unread') {
        assert.equal(await popover.getByRole('button', { name: '好友', exact: true }).getAttribute('aria-pressed'), 'true')
        await popover.getByText('合成好友丙', { exact: true }).waitFor({ state: 'visible' }); await popover.getByText('最新未读', { exact: true }).waitFor({ state: 'visible' })
        assert.equal(await page.evaluate(() => window.__sent.some(payload => payload.type === 'chatRead' && payload.peerAccountId === 'carol')), true)
        result.latestUnreadOpened = 'carol'
      } else {
        assert.equal(await popover.getByRole('button', { name: '在线', exact: true }).getAttribute('aria-pressed'), 'true')
        assert.equal(await popover.locator('.chat-panel').count(), 0); assert.equal(await page.evaluate(() => window.__sent.some(payload => payload.type === 'chatSync' || payload.type === 'chatRead')), false)
        result.noUnreadPath = 'online'
      }
      assert.equal(await page.evaluate(() => window.__overviewCalls), 1); result.utilityButtons = 4; result.utilityHitTargets = true
    } else if (mode !== 'friends') {
      await click(mode === 'social' ? page.locator('#open') : page.getByRole('button', { name: '打开好友功能', exact: true }))
      const popover = page.locator('[data-l12-social-popover]'); await popover.waitFor({ state: 'visible' })
      assert.equal(await popover.getAttribute('aria-modal'), 'false'); assert.equal(await page.locator('[inert],.battle-modal-mask,.site-modal-mask').count(), 0)
      assert.equal(await page.evaluate(() => document.body.style.overflow), '')
      const box = await popover.boundingBox(); assert(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1)
      if (mode === 'spectator') assert.equal(await page.getByRole('button', { name: '本局对手', exact: true }).count(), 0)
      await click(popover.getByRole('button', { name: '合成好友乙', exact: true }))
    }
    if (mode.startsWith('site-')) {
      assert.deepEqual(result.pageErrors, []); assert.deepEqual(result.external, []); assert.deepEqual(result.httpApi, [])
      result.screenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`); await page.screenshot({ path: result.screenshot }); result.screenshotSha256 = sha(fs.readFileSync(result.screenshot)); result.status = 'PASS'
      await context.close(); continue
    }
    const input = page.locator('.chat-panel textarea'); await input.waitFor({ state: 'visible' }); await input.fill('中文组合输入')
    await input.evaluate(element => { element.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true })); element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true })) })
    assert.equal(await page.evaluate(() => window.__sent.filter(p => p.type === 'chatSend').length), 0)
    await input.evaluate(element => element.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true })))
    await click(page.locator('.chat-panel').getByRole('button', { name: '发送', exact: true }))
    assert.equal(await page.evaluate(() => window.__sent.filter(p => p.type === 'chatSend').length), 1)
    assert.equal(await page.evaluate(() => window.__sent.filter(p => p.type === 'chatSend')[0].text), '中文组合输入')
    await page.evaluate(() => window.__ack()); await page.waitForFunction(() => document.querySelector('.chat-panel textarea').value === '')
    assert.equal(await page.locator('.chat-panel .chat-messages article').count(), 1)
    result.openScreenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}-open.png`)
    await page.screenshot({ path: result.openScreenshot }); result.openScreenshotSha256 = sha(fs.readFileSync(result.openScreenshot))
    await page.evaluate(() => { const finish = window.__chat.suspend('bob'); finish('unknown') })
    await page.getByText('当前会话不可用；请先确认好友或屏蔽状态。').waitFor({ state: 'visible' })
    assert.equal(await page.locator('.chat-panel textarea').count(), 0)
    assert.equal(await page.locator('.chat-panel').getByRole('button', { name: '刷新', exact: true }).isDisabled(), true)
    result.unknownMutationFailsClosed = true
    result.nativeSends = 1; result.imePrematureSends = 0
    if (mode !== 'friends') { await click(page.locator('#outside')); assert.equal(await page.evaluate(() => window.__outside), 1); assert.equal(await page.locator('[data-l12-social-popover]').count(), 0); result.outsideClickable = true }
    assert.deepEqual(result.pageErrors, []); assert.deepEqual(result.external, []); assert.deepEqual(result.httpApi, [])
    result.screenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}.png`); await page.screenshot({ path: result.screenshot }); result.screenshotSha256 = sha(fs.readFileSync(result.screenshot)); result.status = 'PASS'
    await context.close()
  }
  assert.deepEqual(hashes(), before, 'UI source changed during the evidence run'); report.status = 'PASS'
} catch (error) { report.status = 'FAIL'; report.errors.push({ message: error.message, stack: error.stack }); process.exitCode = 1 }
finally { if (browser) await browser.close(); await server.close(); fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' }); console.log(JSON.stringify({ status: report.status, cases: report.cases.length, output })) }
