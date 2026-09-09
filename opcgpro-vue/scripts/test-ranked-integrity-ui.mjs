import assert from 'node:assert/strict'
import path from 'node:path'
import fs from 'node:fs'
import { createRequire } from 'node:module'
import { createServer } from 'vite'
const root = path.resolve(import.meta.dirname, '..')
const { chromium } = createRequire(import.meta.url)(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const out = path.resolve(root, '../artifacts/integrity-ui')
fs.mkdirSync(out, { recursive: true })
const entry = `
import {createApp,h} from 'vue';import {createRouter,createMemoryHistory} from 'vue-router';
import Admin from '/src/l12/site/AdminRankedIntegrityPanel.vue';import Notice from '/src/l12/site/RankedIntegrityNotice.vue';import History from '/src/l12/site/RankedPenaltyHistory.vue';
import {platformState} from '/src/l12/platform.ts';import {l12State} from '/src/l12/net.ts';import '/src/style.css';
platformState.account={id:'qa-owner',username:'对方测试长昵称十二军团',role:'admin',permissions:['admin.audit.read','admin.match-governance.write']};platformState.token='fixture-only';
l12State.endpoint=location.origin.replace('http','ws')+'/ws';window.qaAccount=(id)=>platformState.account={id,username:id,role:'player',permissions:[]};
const mode=new URLSearchParams(location.search).get('mode');const router=createRouter({history:createMemoryHistory(),routes:[{path:'/:pathMatch(.*)*',component:{render:()=>null}}]});
createApp({render:()=>h(mode==='admin'?Admin:mode==='history'?History:Notice)}).use(router).mount('#app');`
const server = await createServer({ root, server: { host: '127.0.0.1', port: 0 }, plugins: [{ name: 'integrity-test-fixture', resolveId(id) { if (id === '/__integrity__.js') return id }, load(id) { if (id === '/__integrity__.js') return entry }, configureServer(s) { s.middlewares.use((req, res, next) => { if (req.url?.startsWith('/__integrity__?')) { res.setHeader('Content-Type', 'text/html'); res.end('<meta name="viewport" content="width=device-width,initial-scale=1"><div id="app"></div><script type="module" src="/__integrity__.js"></script>'); return } next() }) } }] })
const effect = { accountId: 'qa-owner', username: '对方测试长昵称十二军团', scoreDelta: -240, restrictionUntil: '2026-09-24T02:00:00+08:00', rewardOutcome: '撤销已确认违规对局的收益' }
const decision = { decisionId: 'decision-fixture', disposition: 'confirmed', effectiveDisposition: 'confirmed', matchIds: ['match-fixture'], reason: '经核查存在反复极短投降并向固定账号输送收益。', evidence: '合成测试证据，不是真实玩家', actorName: '测试管理员', createdAt: '2026-09-10T02:00:00+08:00', accountEffects: [effect] }
const notification = { id: 'notice-fixture', decisionId: decision.decisionId, matchIds: decision.matchIds, outcome: 'confirmed', reason: decision.reason, decidedAt: decision.createdAt, scoreDelta: effect.scoreDelta, restrictionUntil: effect.restrictionUntil, appealGuidance: '如有异议，可在判罚历史提交申诉。申诉不会自动解除限制。', acknowledged: false }
const row = { id: 'audit-fixture', matchId: 'match-fixture', seasonId: 'fixture-season', firstAccountId: 'qa-owner', firstPlayer: '对方测试长昵称十二军团', secondAccountId: 'qa-other', secondPlayer: '合成对方', durationMs: 20000, meaningfulCommandCount: 0, conclusionKind: 'surrender', signals: [{ code: 'very-short-match', label: '异常极短对局' }], reviewRecommended: true, enforcement: 'none', createdAt: decision.createdAt }
let browser
try {
  await server.listen(); browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage(); const errors = []; const requests = []
  page.on('pageerror', e => errors.push(e.message))
  let acked = false; let failAck = true; let appealed = false
  await page.route('**/*', async route => {
    const url = new URL(route.request().url()); if (url.hostname !== '127.0.0.1') return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    const method = route.request().method(); const body = route.request().postDataJSON?.(); requests.push({ path: url.pathname, method, body })
    let value = {}; let status = 200
    if (url.pathname.endsWith('integrity-audits')) value = [row]
    else if (url.pathname.endsWith('/preview')) value = { revision: 1, requestId: body.requestId, disposition: body.disposition, canConfirm: true, blockingReasons: [], matchIds: body.matchIds, accountEffects: [effect] }
    else if (url.pathname.endsWith('/decisions')) value = method === 'POST' ? decision : { items: [decision], nextCursor: null }
    else if (url.pathname.endsWith('/notifications')) value = { items: url.searchParams.get('unreadOnly') === 'true' && acked ? [] : [notification], unreadCount: acked ? 0 : 1 }
    else if (url.pathname.endsWith('/ack')) { if (failAck) { failAck = false; status = 503; value = { message: '模拟网络失败' } } else { acked = true; value = { acknowledged: true } } }
    else if (url.pathname.endsWith('/appeals')) { if (method === 'POST') { appealed = true; value = { id: 'appeal-fixture' } } else value = { items: appealed ? [{ id: 'appeal-fixture', decisionId: decision.decisionId, statement: '网络异常并非故意投降，请核对连接日志。', status: 'reviewing', revision: 1, createdAt: decision.createdAt, reply: '已进入复核，正在核对记录。' }] : [] } }
    return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
  })
  const base = `http://127.0.0.1:${server.httpServer.address().port}/__integrity__?mode=`
  await page.goto(base + 'admin'); await page.getByText('选择此局', { exact: true }).click()
  await page.locator('.integrity-actions select').selectOption('confirmed')
  await page.locator('.restriction input[type="checkbox"]').first().check()
  await page.locator('.restriction input[type="number"]').fill('14')
  await page.locator('.integrity-actions form textarea').nth(0).fill('三场重复极短对局合成证据')
  await page.locator('.integrity-actions form textarea').nth(1).fill('经复核确认存在刷分行为')
  await page.getByRole('button', { name: '预览影响', exact: true }).click()
  await page.locator('.preview').waitFor(); assert.equal(requests.find(r => r.path.endsWith('/preview')).body.restrictionDays, 14)
  await page.getByRole('button', { name: '确认执行本次处置' }).click(); await page.getByText('已登记：确认违规', { exact: false }).waitFor()
  assert.equal(requests.filter(r => r.method === 'POST' && r.path.endsWith('/decisions')).length, 1)
  for (const size of [{ width: 1280, height: 720 }, { width: 390, height: 844 }]) {
    await page.setViewportSize(size); await page.screenshot({ path: path.join(out, `admin-${size.width}.png`), fullPage: true })
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'admin overflow')
  }
  await page.goto(base + 'notice'); await page.locator('.integrity-notice').waitFor()
  await page.getByRole('button', { name: '我要申诉', exact: true }).click(); await page.locator('.ranked-appeal-form textarea').fill('网络异常并非故意投降，请核对连接日志。')
  await page.getByRole('button', { name: '提交申诉', exact: true }).click(); await page.getByText('申诉已提交。', { exact: false }).waitFor()
  assert.equal(requests.filter(r => r.method === 'POST' && r.path.endsWith('/appeals')).length, 1)
  await page.screenshot({ path: path.join(out, 'notice-390.png'), fullPage: true })
  await page.getByRole('button', { name: '我知道了' }).click(); await page.getByText('模拟网络失败', { exact: false }).waitFor(); assert.equal(await page.locator('.integrity-notice').count(), 1)
  await page.getByRole('button', { name: '我知道了' }).click(); await page.locator('.integrity-notice').waitFor({ state: 'detached' })
  await page.goto(base + 'history'); await page.getByText('已进入复核，正在核对记录。', { exact: false }).waitFor(); assert.equal(await page.locator('.penalty-history .history-scroll').first().locator('article').count(), 1, 'ack must retain history')
  await page.screenshot({ path: path.join(out, 'history-390.png'), fullPage: true })
  assert.deepEqual(errors, []); console.log('Ranked integrity UI passed: admin 14-day preview/confirm, long-name containment, notice retry/ack, private history and appeal submission/reply.')
} finally { await browser?.close(); await server.close() }
