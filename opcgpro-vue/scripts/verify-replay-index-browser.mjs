import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const origin = process.argv[2] || 'http://127.0.0.1:5197'
const output = process.env.L12_REPLAY_INDEX_OUT || 'D:/GPT/Legion12/artifacts/replay-index-20261002'
fs.mkdirSync(output, { recursive: true })
const account = { id: 'synthetic-index', username: '索引验收', role: 'admin', permissions: [], mustChangeUsername: false, mustChangePassword: false }
const player = index => ({ PlayerIndex: index, Name: index ? '乙' : '甲', Faction: 'otherworld', MasterId: `master-${index}`, MasterName: '主宰', Hp: 8, MaxHp: 8, Hand: [], Library: [], Morale: [], MoraleDeck: [], Field: [[null, null, null], [null, null, null]], Graveyard: [], Resolving: [], ExtraRelics: [], SpecialZones: { Runes: 0, TrialLevel: 0, GodPower: [], Trials: [] }, MulliganDone: true })
const commands = Array.from({ length: 75 }, (_, step) => ({ sequence: step + 1, playerIndex: step % 2, command: { type: 'passPriority' }, accepted: true, revision: step + 1, stateHash: `synthetic-${step}`, state: { MatchId: 'index-fixture', RoomCode: 'INDEX', ActivePlayer: Math.floor(step / 10) % 2, FirstPlayer: 0, Phase: 'Main', Round: 1 + Math.floor(step / 10), Players: [player(0), player(1)], Events: [{ Sequence: step + 1, Type: step % 3 === 0 ? 'cost' : 'effect-result', EffectResultStatus: step % 3 === 1 ? 'negated' : 'resolved', Cards: [{ InstanceId: 'public-source', CardId: 'SYNTHETIC', Name: `公开来源${step + 1}`, Hidden: false }] }] } }))
const match = { matchId: 'index-fixture', roomCode: 'INDEX', player0: '甲', player1: '乙', deck0: '', deck1: '', status: 'completed', startedUtc: '2026-10-02T00:00:00Z', endedUtc: '2026-10-02T01:00:00Z', commandCount: commands.length, winner: 0 }
const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const audits = []
try {
  for (const viewport of [{ width: 2560, height: 1440 }, { width: 1440, height: 900 }, { width: 1280, height: 720 }, { width: 1190, height: 650 }]) {
    const context = await browser.newContext({ viewport })
    await context.addInitScript(account => { localStorage.setItem('l12-account', JSON.stringify(account)); localStorage.setItem('l12-auth-token', 'synthetic-only'); localStorage.setItem('l12-nickname', account.username) }, account)
    const page = await context.newPage()
    const errors = []
    const requests = []
    let failNextPage = false
    page.on('pageerror', error => errors.push(error.stack || error.message))
    await page.route('**/api/**', route => {
      const url = new URL(route.request().url())
      requests.push(url.pathname + url.search)
      let body = {}
      let status = 200
      if (url.pathname === '/api/auth/me') body = account
      if (url.pathname === '/api/matches/index-fixture') body = { match, commands }
      if (url.pathname === '/api/admin/matches/index-fixture') body = { summary: { ...match, players: [{ displayName: '甲', result: 'win' }, { displayName: '乙', result: 'loss' }] }, replay: [] }
      if (url.pathname === '/api/admin/matches/index-fixture/replay') {
        const next = url.searchParams.has('cursor')
        if (next && failNextPage) { status = 503; body = { message: '合成分页失败' } }
        else body = { items: next ? commands.slice(50) : commands.slice(0, 50), totalCommands: 75, nextCursor: next ? null : 'synthetic-next' }
      }
      if (url.pathname === '/api/friends/overview') body = { friends: [], requests: [], blocked: [] }
      if (url.pathname.includes('notifications')) body = []
      if (url.pathname === '/api/ranked/integrity/notifications') body = { items: [], nextCursor: null }
      return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    })
    await page.goto(`${origin}/battle/records/replay/index-fixture`, { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: '播放', exact: true }).click()
    await page.getByRole('button', { name: '回放定位', exact: true }).click()
    await page.locator('.replay-index').waitFor()
    assert.equal(await page.getByRole('button', { name: '播放', exact: true }).count(), 1, 'index opening pauses playback')
    await page.locator('.replay-index-filters select').first().selectOption('3')
    assert.match(await page.locator('.replay-index-rounds button').first().textContent(), /我方回合开始/, 'legacy missing viewer index follows board default viewpoint')
    await page.locator('.replay-index-rounds button').first().click()
    assert.match(await page.locator('.replay-controls small').textContent(), /步骤 21 \/ 75/)
    await page.getByRole('button', { name: '回放定位', exact: true }).click()
    await page.locator('.replay-index-filters select').first().selectOption('')
    await page.locator('.replay-index-filters select').nth(1).selectOption('cost')
    await page.locator('.replay-index-search input').fill('公开来源34')
    const event = page.locator('.replay-index-events button').first()
    assert.match(await event.textContent(), /支付费用/)
    await event.click()
    assert.match(await page.locator('.replay-controls small').textContent(), /步骤 34 \/ 75/)
    await page.getByRole('button', { name: '回放定位', exact: true }).click()
    await page.locator('.replay-index-search input').fill('')
    assert(await page.locator('.replay-index-events button').evaluateAll(buttons => buttons.every(button => button.scrollHeight <= button.clientHeight + 1)), 'event labels and metadata must not be vertically clipped')
    const geometry = await page.locator('.replay-index').evaluate(panel => {
      const rect = panel.getBoundingClientRect()
      const playback = document.querySelector('.replay-controls').getBoundingClientRect()
      const back = document.querySelector('.replay-route-controls').getBoundingClientRect()
      const intersects = (a, b) => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom
      return { clipped: rect.left < 0 || rect.top < 0 || rect.right > innerWidth || rect.bottom > innerHeight, playbackOverlap: intersects(rect, playback), returnOverlap: intersects(rect, back), overflow: document.documentElement.scrollWidth > innerWidth + 1, hit: [...panel.querySelectorAll('button')].filter(button => !button.disabled && button.getBoundingClientRect().height).every(button => { const r = button.getBoundingClientRect(); const scroll = button.closest('.replay-index-events')?.getBoundingClientRect(); if (r.top < rect.top || r.bottom > rect.bottom || (scroll && (r.top < scroll.top || r.bottom > scroll.bottom))) return true; const target = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return button === target || button.contains(target) }) }
    })
    await page.screenshot({ path: path.join(output, `index-${viewport.width}x${viewport.height}.png`) })
    assert.deepEqual(geometry, { clipped: false, playbackOverlap: false, returnOverlap: false, overflow: false, hit: true })
    await page.locator('.replay-index').press('Escape')
    assert.equal(await page.locator('.replay-index').count(), 0)
    await page.goto(`${origin}/admin/matches/index-fixture/replay`, { waitUntil: 'networkidle' })
    await page.locator('.replay-controls').waitFor()
    assert.equal(requests.filter(url => url.startsWith('/api/admin/matches/index-fixture/replay')).length, 1, 'no eager full archive request')
    await page.getByRole('button', { name: '回放定位', exact: true }).click()
    assert.match(await page.locator('.replay-index-note').textContent(), /50 \/ 75/)
    failNextPage = true
    await page.getByRole('button', { name: '继续加载索引', exact: true }).click()
    await page.locator('.replay-index [role=alert]').waitFor()
    assert.equal(await page.locator('.replay-loading').count(), 0, 'index retry failure does not destroy loaded playback')
    failNextPage = false
    await page.getByRole('button', { name: '继续加载索引', exact: true }).click()
    await page.locator('.replay-index-filters select').first().selectOption('7')
    await page.locator('.replay-index-rounds button').first().click()
    assert.match(await page.locator('.replay-controls small').textContent(), /步骤 61 \/ 75/)
    assert.deepEqual(errors, [])
    audits.push({ viewport, geometry, status: 'passed', cases: ['pause-and-seek', 'round', 'cost-search', 'exact-frame', 'keyboard-close', 'admin-incremental-index', '503-retry', 'loaded-playback-retained'] })
    await context.close()
  }
  fs.writeFileSync(path.join(output, 'manifest.json'), JSON.stringify({ status: 'passed', generatedAt: new Date().toISOString(), audits }, null, 2))
  console.log(`Replay index browser acceptance passed: ${audits.length} desktop sizes, exact jumps, filters, admin paging and failure retry.`)
} finally { await browser.close() }
