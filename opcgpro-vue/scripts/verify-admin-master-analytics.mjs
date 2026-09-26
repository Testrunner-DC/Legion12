import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { createServer } from 'vite'

const root = path.resolve(import.meta.dirname, '..')
const out = path.resolve(root, '../artifacts/admin-master-analytics')
fs.mkdirSync(out, { recursive: true })
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const read = relative => fs.readFileSync(path.resolve(root, relative), 'utf8')
const adminSource = read('src/l12/site/AdminMasterAnalyticsPanel.vue')
const rankingSource = read('src/l12/site/RankingsPage.vue')
const matchesSource = read('src/l12/site/AdminMatchesPanel.vue')
assert.match(adminSource, /MasterMatchupMatrix/, '后台主宰矩阵尚未复用共享矩阵组件')
assert.match(rankingSource, /MasterMatchupMatrix/, '排行矩阵尚未复用共享矩阵组件')
assert.match(adminSource, /DeckSnapshotViewer/, '热门构筑尚未复用共享构筑查看器')
assert.match(matchesSource, /DeckSnapshotViewer/, '对局档案尚未复用共享构筑查看器')

const entry = `
import { createApp, h } from 'vue'
import Panel from '/src/l12/site/AdminCardAnalyticsPanel.vue'
import { adminApi } from '/src/l12/platform.ts'
import '/src/style.css'
const item = index => ({ masterId: 'QA-M'+index, participantSamples: 40+index, distinctMatches: 40+index, distinctDecks: 8, usageRate: .2, deckShare: .2, wins: 24, winRate: .6, winRateConfidence: { low: .45, high: .73 }, averageDurationSeconds: 900, firstSamples: 20, firstWinRate: .6, secondSamples: 20, secondWinRate: .6 })
const items = [item(1), item(2)]
const allDays = Array.from({ length: 30 }, (_, index) => ({ date: '2026-09-'+String(index+1).padStart(2,'0'), samples: 10+index, wins: 6, winRate: .6 }))
adminApi.masterAnalytics = async query => ({ items, matchups: [{ masterId: 'QA-M1', opponentMasterId: 'QA-M2', samples: 40, wins: 24, winRate: .6 }], trend: allDays.slice(0, Number(new URLSearchParams(location.search).get('days') || 1)), selectedMasterId: query.masterId || null, popularDecks: query.masterId ? [{ signature: 'qa-deck', samples: 40, wins: 24, winRate: .6, cards: [{ cardId: 'QA-CARD', quantity: 2, section: 'main' }] }] : [], cards: null })
createApp({ render: () => h(Panel) }).mount('#app')
`
const server = await createServer({ root, configLoader: 'runner', cacheDir: path.join(root, '.tmp', 'vite-admin-master-analytics'), server: { host: '127.0.0.1', port: 0 }, plugins: [{
  name: 'admin-master-analytics-fixture', resolveId(id) { if (id === '/__admin_master.js') return id }, load(id) { if (id === '/__admin_master.js') return entry },
  configureServer(vite) { vite.middlewares.use((req, res, next) => { if (req.url?.match(/^\/__admin_master(?:\?|$)/)) { res.setHeader('Content-Type', 'text/html'); res.end('<div id="app"></div><script type="module" src="/__admin_master.js"></script>'); return } next() }) },
}] })

let browser
try {
  await server.listen()
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  for (const days of [1, 2, 7, 30]) {
    const page = await browser.newPage({ viewport: { width: days === 30 ? 390 : 1280, height: 900 } })
    const errors = []
    page.on('pageerror', error => errors.push(error.message))
    await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())
    await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__admin_master?days=${days}`)
    await page.getByRole('button', { name: '主宰', exact: true }).click()
    await page.locator('.master-list>button').first().click()
    await page.getByText('胜率与使用趋势', { exact: true }).waitFor()
    const widths = await page.locator('.trend-bars article').evaluateAll(nodes => nodes.map(node => Math.round(node.getBoundingClientRect().width)))
    assert.equal(widths.length, days)
    assert.ok(widths.every(width => width >= 46 && width <= 72), `${days} 日柱宽失控：${widths.join(',')}`)
    assert.equal(await page.locator('.master-matchup-matrix').count(), 1, '后台没有渲染共享主宰矩阵')
    await page.screenshot({ path: path.join(out, `master-${days}d-${days === 30 ? 390 : 1280}.png`), fullPage: true })
    await page.locator('.trend').screenshot({ path: path.join(out, `trend-${days}d-${days === 30 ? 390 : 1280}.png`) })
    await page.getByRole('button', { name: /查看构筑/ }).click()
    await page.locator('.deck-snapshot-viewer').waitFor()
    if (days === 7) await page.screenshot({ path: path.join(out, 'popular-deck-viewer-1280.png'), fullPage: true })
    await page.getByRole('button', { name: '关闭构筑', exact: true }).click()
    const button = page.getByRole('button', { name: '分析卡牌', exact: true })
    await page.getByRole('button', { name: '单卡仪表盘', exact: true }).click()
    await button.waitFor()
    assert.equal((await button.innerText()).trim(), '分析卡牌')
    const box = await button.boundingBox()
    assert.ok(box && box.height <= 46, `分析卡牌按钮高度异常：${box?.height}`)
    await page.screenshot({ path: path.join(out, `single-card-button-${days === 30 ? 390 : 1280}.png`), fullPage: true })
    assert.deepEqual(errors, [])
    await page.close()
  }
  console.log(JSON.stringify({ status: 'passed', trendDays: [1, 2, 7, 30], output: out }))
} finally { await browser?.close(); await server.close() }
