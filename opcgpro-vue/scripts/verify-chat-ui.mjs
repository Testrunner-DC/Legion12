import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const output = path.resolve(process.env.L12_CHAT_UI_OUT || `D:/GPT/Legion12/artifacts/chat-ui-${Date.now()}`)
const cacheDir = path.resolve(process.env.L12_VITE_CACHE_DIR || 'D:/GPT/Legion12/cache/primary/chat-ui-vite')
assert(!fs.existsSync(output), 'Never overwrite previous UI evidence')
assert(/^[DE]:[\\/]/i.test(cacheDir), 'Chat UI Vite cache must stay on physical D/E storage')
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
const mode=new URLSearchParams(location.search).get('mode'),fixtureHasUnread=mode==='site-unread'||mode?.startsWith('site-layout-');window.__fixtureHasUnread=fixtureHasUnread;
if(fixtureHasUnread){chatResource.receive({type:'chatMessage',entry:{id:'older-bob',seq:1,scope:'friend',senderId:'bob',senderName:friend.username,recipientId:'alice',text:'较早未读',createdAt:'2026-10-17T01:00:00Z',moderated:false,policyVersion:'chat-v1'}});chatResource.receive({type:'chatMessage',entry:{id:'newer-carol',seq:1,scope:'friend',senderId:'carol',senderName:newestFriend.username,recipientId:'alice',text:'最新未读',createdAt:'2026-10-17T02:00:00Z',moderated:false,policyVersion:'chat-v1'}})}
const blank={render:()=>null},router=createRouter({history:createMemoryHistory(),routes:['/','/news','/battle','/decks','/cards','/rules','/me','/friends'].map(path=>({path,component:blank}))});window.__router=router;await router.push('/');
const app=createApp({setup(){const open=ref(false),anchor=ref(null);return()=>h('main',[
h('button',{id:'outside',onClick:()=>window.__outside++},'外部功能'),mode?.startsWith('site-')?h(SiteShell,null,{default:()=>h('div',{id:'site-slot'},'主页内容')}):mode==='friends'?h(FriendsPage):mode==='battle'||mode==='spectator'?h('div',{class:'dock-host'},[h(BattleUtilityDock)]):h('div',{class:'dock-host'},[
h('button',{id:'open',ref:anchor,onClick:()=>open.value=!open.value},'好友消息'),open.value?h(SocialPopover,{anchor:anchor.value,onClose:()=>open.value=false}):null])])}});app.use(router);app.mount('#app');await nextTick();window.__ready=true;
`
const html = '<!doctype html><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0;background:#071016;color:#fff;font-family:"Microsoft YaHei",sans-serif}#outside{position:fixed;right:8px;top:8px;min-height:36px;z-index:1}.dock-host{position:fixed;bottom:12px;left:12px;width:240px}button{cursor:pointer}</style><div id="app"></div><script type="module" src="/__chat_entry.js"></script>'
const server = await createServer({ root, configLoader: 'runner', cacheDir, server: { host: '127.0.0.1', port: 0 }, plugins: [{
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
    ['site-clean', { width: 390, height: 844 }, true], ['site-layout-wide', { width: 1920, height: 1080 }, false],
    ['site-layout-desktop-edge', { width: 701, height: 641 }, false], ['site-layout-mobile-edge', { width: 700, height: 700 }, true],
    ['site-layout-height-edge', { width: 844, height: 640 }, true], ['site-layout-short', { width: 844, height: 390 }, true],
    ['site-layout-portrait', { width: 390, height: 844 }, true],
  ]) {
    const result = { mode, viewport, touch, status: 'RUNNING', pageErrors: [], external: [], httpApi: [] }; report.cases.push(result)
    const context = await browser.newContext({ viewport, hasTouch: touch })
    await context.route('**/*', async route => { const url = new URL(route.request().url()); if (!['127.0.0.1', 'localhost'].includes(url.hostname)) { result.external.push(url.origin); await route.abort(); return } if (url.pathname.startsWith('/api/')) { result.httpApi.push(url.pathname); await route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }); return } await route.continue() })
    await context.routeWebSocket(url => !['127.0.0.1', 'localhost'].includes(new URL(url).hostname), socket => { result.external.push('websocket'); socket.close() })
    const page = await context.newPage(); page.on('pageerror', error => result.pageErrors.push(error.message)); page.setDefaultTimeout(15_000)
    await page.goto(`${base}__chat?mode=${mode}`); await page.waitForFunction(() => window.__ready)
    const click = async locator => { await locator.scrollIntoViewIfNeeded(); const box = await locator.boundingBox(); assert(box && box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width + 1 && box.y + box.height <= viewport.height + 1); if (touch) await locator.tap(); else await locator.click() }
    if (mode.startsWith('site-')) {
      const drawerExpected = viewport.width <= 700 || viewport.height <= 640
      const menuButton = page.getByRole('button', { name: '打开导航', exact: true })
      assert.equal(await menuButton.isVisible(), drawerExpected)
      if (drawerExpected) {
        await click(menuButton); await page.locator('.site-sidebar.open').waitFor({ state: 'visible' })
        assert.equal(await page.evaluate(() => document.body.style.overflow), 'hidden')
        assert.equal(await page.locator('.site-drawer-backdrop').isVisible(), true)
        assert.equal(await page.locator('.site-sidebar').getAttribute('role'), 'dialog')
        assert.equal(await page.locator('.site-sidebar').getAttribute('aria-modal'), 'true')
      }
      const nav = page.locator('.site-nav'), navLinks = nav.locator('a')
      assert.equal(await navLinks.count(), 7)
      const reach = async locator => {
        await locator.scrollIntoViewIfNeeded()
        return locator.evaluate(element => {
          const rect = element.getBoundingClientRect(), owner = element.closest('.site-nav'), bounds = owner.getBoundingClientRect()
          const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
          return { contained: rect.top >= bounds.top - 1 && rect.bottom <= bounds.bottom + 1, hit: hit === element || element.contains(hit), scrollTop: owner.scrollTop }
        })
      }
      const lastReach = await reach(navLinks.last()), firstReach = await reach(navLinks.first())
      assert(lastReach.contained && lastReach.hit); assert(firstReach.contained && firstReach.hit)
      const utilities = page.locator('.site-utilities > button'); assert.equal(await utilities.count(), 4)
      const layout = await page.evaluate(() => {
        const rect = element => { const value = element.getBoundingClientRect(); return { left: value.left, top: value.top, right: value.right, bottom: value.bottom, width: value.width, height: value.height } }
        const sidebar = document.querySelector('.site-sidebar'), nav = document.querySelector('.site-nav'), dock = document.querySelector('.site-utilities'), content = document.querySelector('.site-content')
        const buttons = [...dock.querySelectorAll(':scope > button')].map(button => { const box = rect(button), hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2); return { box, hit: hit === button || button.contains(hit) } })
        const badge = dock.querySelector('.utility-unread'), badgeBox = badge ? rect(badge) : null, badgeButtonBox = badge ? rect(badge.closest('button')) : null
        const dockStyle = getComputedStyle(dock), visual = window.visualViewport
        const header = document.querySelector('.site-mobile-head')
        return { inner: { width: innerWidth, height: innerHeight }, visual: { width: visual?.width ?? innerWidth, height: visual?.height ?? innerHeight }, header: rect(header), sidebar: rect(sidebar), nav: rect(nav), dock: rect(dock), content: rect(content), display: dockStyle.display, flexDirection: dockStyle.flexDirection, columns: dockStyle.gridTemplateColumns.split(' ').filter(Boolean).length, buttons, badgeBox, badgeButtonBox, bodyOverflow: document.body.style.overflow }
      })
      assert.equal(layout.inner.width, viewport.width); assert.equal(layout.inner.height, viewport.height)
      assert(Math.abs(layout.visual.width - viewport.width) < 2 && Math.abs(layout.visual.height - viewport.height) < 2)
      assert(layout.nav.bottom <= layout.dock.top + 1); assert(layout.dock.bottom <= layout.sidebar.bottom + 1)
      assert(Math.abs(layout.sidebar.bottom - viewport.height) < 1, 'Sidebar must retain full viewport height, not shrink to its content')
      assert(Math.abs(layout.sidebar.top - (drawerExpected ? layout.header.bottom : 0)) < 1, 'Drawer starts below the safe-area header; wide rail starts at the top')
      assert(layout.buttons.every(item => item.box.width >= 44 && item.box.height >= 44 && item.hit))
      if (drawerExpected) {
        assert.equal(layout.display, 'grid'); assert.equal(layout.columns, 2); assert(layout.dock.height <= 100)
        assert(Math.abs(layout.sidebar.width - Math.min(184, viewport.width * .84)) < 1, 'Compact drawer removes excess horizontal space')
        assert(Math.abs(layout.content.left) < 1); assert(layout.sidebar.right <= viewport.width + 1)
      } else {
        assert.equal(layout.display, 'flex'); assert.equal(layout.flexDirection, 'column'); assert(layout.dock.height <= 192)
        assert(Math.abs(layout.sidebar.width - 72) < 1); assert(Math.abs(layout.content.left - 72) < 1)
      }
      const fixtureHasUnread = await page.evaluate(() => window.__fixtureHasUnread)
      if (fixtureHasUnread) {
        assert(layout.badgeBox && layout.badgeButtonBox)
        assert(layout.badgeBox.left >= layout.badgeButtonBox.left - 1 && layout.badgeBox.right <= layout.badgeButtonBox.right + 1 && layout.badgeBox.top >= layout.badgeButtonBox.top - 1 && layout.badgeBox.bottom <= layout.badgeButtonBox.bottom + 1)
      } else assert.equal(layout.badgeBox, null)
      result.layout = layout; result.navReach = { first: firstReach, last: lastReach }; result.drawer = drawerExpected
      assert.equal(await utilities.nth(0).getAttribute('aria-label'), '设置'); assert.equal(await utilities.nth(1).getAttribute('aria-label'), '更新日志')
      const utilityText = await utilities.allTextContents(); assert(!utilityText.some(text => /设置|更新日志|在线 \/ 消息|连接正常/.test(text)))
      assert.match(await utilities.nth(2).getAttribute('aria-label'), /^在线玩家与好友消息：1 人在线/)
      assert.equal(await utilities.nth(3).getAttribute('aria-label'), '连接正常')
      result.layoutScreenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}-layout.png`); await page.screenshot({ path: result.layoutScreenshot }); result.layoutScreenshotSha256 = sha(fs.readFileSync(result.layoutScreenshot))
      await utilities.nth(0).focus(); await page.keyboard.press('Tab'); assert.equal(await utilities.nth(1).evaluate(element => document.activeElement === element), true)
      await page.keyboard.press('Tab'); assert.equal(await utilities.nth(2).evaluate(element => document.activeElement === element), true)
      await page.keyboard.press('Enter'); const popover = page.locator('[data-l12-social-popover]'); await popover.waitFor({ state: 'visible' })
      assert.equal(await popover.getAttribute('aria-modal'), 'false'); assert.equal(await page.evaluate(() => document.body.style.overflow), '')
      assert.equal(await page.locator('.site-drawer-backdrop').count(), 0)
      const popoverBox = await popover.boundingBox(); assert(popoverBox && popoverBox.x >= 0 && popoverBox.y >= 0 && popoverBox.x + popoverBox.width <= viewport.width + 1 && popoverBox.y + popoverBox.height <= viewport.height + 1)
      if (fixtureHasUnread) {
        assert.equal(await popover.getByRole('button', { name: '好友', exact: true }).getAttribute('aria-pressed'), 'true')
        await popover.getByText('合成好友丙', { exact: true }).waitFor({ state: 'visible' }); await popover.getByText('最新未读', { exact: true }).waitFor({ state: 'visible' })
        assert.equal(await page.evaluate(() => window.__sent.some(payload => payload.type === 'chatRead' && payload.peerAccountId === 'carol')), true)
        result.latestUnreadOpened = 'carol'
      } else {
        assert.equal(await popover.getByRole('button', { name: '在线', exact: true }).getAttribute('aria-pressed'), 'true')
        assert.equal(await popover.locator('.chat-panel').count(), 0); assert.equal(await page.evaluate(() => window.__sent.some(payload => payload.type === 'chatSync' || payload.type === 'chatRead')), false)
        result.noUnreadPath = 'online'
      }
      result.popoverScreenshot = path.join(output, `${mode}-${viewport.width}x${viewport.height}-popover.png`); await page.screenshot({ path: result.popoverScreenshot }); result.popoverScreenshotSha256 = sha(fs.readFileSync(result.popoverScreenshot))
      await page.keyboard.press('Escape'); await popover.waitFor({ state: 'detached' })
      const focusVisible = await page.evaluate(() => { const element = document.activeElement, box = element?.getBoundingClientRect(), style = element ? getComputedStyle(element) : null; return { label: element?.getAttribute?.('aria-label') ?? '', visible: Boolean(box && box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight && box.left < innerWidth && style?.visibility !== 'hidden') } })
      result.focusReturn = focusVisible
      assert.equal(focusVisible.visible, true); assert.equal(focusVisible.label, drawerExpected ? '打开导航' : await utilities.nth(2).getAttribute('aria-label'))
      if (mode === 'site-layout-wide' || mode === 'site-layout-portrait') {
        if (drawerExpected) { await click(menuButton); await page.locator('.site-sidebar.open').waitFor({ state: 'visible' }) }
        await click(utilities.nth(2)); await popover.waitFor({ state: 'visible' })
        const overviewBeforeResize = await page.evaluate(() => window.__overviewCalls)
        assert.equal(overviewBeforeResize, 2, 'Two deliberate popover openings perform exactly two overview loads')
        const resized = drawerExpected ? { width: 1366, height: 768 } : { width: 390, height: 844 }
        await page.setViewportSize(resized)
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
        assert.equal(await menuButton.isVisible(), !drawerExpected)
        await page.keyboard.press('Escape'); await popover.waitFor({ state: 'detached' })
        const restoredFocus = await page.evaluate(() => { const element = document.activeElement, box = element?.getBoundingClientRect(); return { label: element?.getAttribute?.('aria-label') ?? '', visible: Boolean(box && box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight && box.left < innerWidth) } })
        result.resizedFocusReturn = { viewport: resized, ...restoredFocus }
        assert.equal(restoredFocus.visible, true)
        assert.equal(restoredFocus.label, drawerExpected ? await utilities.nth(2).getAttribute('aria-label') : '打开导航')
        assert.equal(await page.evaluate(() => window.__overviewCalls), overviewBeforeResize, 'Resize must not reload the overview')
        await page.setViewportSize(viewport)
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))
      }
      if (drawerExpected) {
        await click(menuButton); await page.locator('.site-sidebar.open').waitFor({ state: 'visible' })
        await click(page.getByRole('link', { name: '图鉴', exact: true })); await page.waitForFunction(() => window.__router.currentRoute.value.path === '/cards')
        assert.equal(await page.locator('.site-sidebar.open').count(), 0); assert.equal(await page.locator('.site-drawer-backdrop').count(), 0); assert.equal(await page.evaluate(() => document.body.style.overflow), '')
      }
      assert.equal(await page.evaluate(() => window.__overviewCalls), mode === 'site-layout-wide' || mode === 'site-layout-portrait' ? 2 : 1); result.utilityButtons = 4; result.utilityHitTargets = true; result.routeClosedDrawer = drawerExpected
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
