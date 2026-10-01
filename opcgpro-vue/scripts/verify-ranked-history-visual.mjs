import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.join(process.env.TEMP || root, 'l12-ranked-history-visual')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const entry = `
import { createApp, h } from 'vue'
import RankingsPage from '/src/l12/site/RankingsPage.vue'
import { rankedApi } from '/src/l12/platform.ts'
import '/src/style.css'

rankedApi.leaderboard = async () => ({
  players: [],
  analytics: {
    range: 'season',
    summary: { matches: 0, placedPlayers: 0, activeMasters: 0, updatedAt: '2026-10-01T06:32:16Z' },
    masters: [],
    matchups: [],
  },
})
rankedApi.history = async () => ({
  latestSeasonName: '第1赛季 · 风暴',
  factionTotals: [
    { seasonName: '第1赛季 · 风暴', factions: [
      { faction: '秩序', value: 12, displayValue: '12' },
      { faction: '混沌', value: 10, displayValue: '10' },
      { faction: '命运', value: 8, displayValue: '8' },
    ] },
    { seasonName: '第0赛季 · 始源', factions: [
      { faction: '秩序', value: 2407333, displayValue: '2,407,333' },
      { faction: '混沌', value: 2279096, displayValue: '2,279,096' },
      { faction: '命运', value: 2946802, displayValue: '2,946,802' },
    ] },
  ],
  honors: [
    { seasonName: '第1赛季 · 风暴', title: '最强后号', masterId: 'S01-02M1', winners: [{ username: '公开账号甲', faction: '秩序' }] },
    { seasonName: '第0赛季 · 始源', title: '最强前号', masterId: 'S01-01M1', winners: [{ username: '公开账号乙', faction: '混沌' }] },
    ...['守望天士', '混沌领主', '代行主君', '织命者', '始乱者', '统御者'].map((title, index) => ({
      seasonName: index === 5 ? '第1赛季 · 风暴' : '第0赛季 · 始源', title,
      winners: [{ username: '派系账号' + index, faction: ['秩序', '混沌', '命运'][index % 3] }],
    })),
  ],
})

createApp({ render: () => h(RankingsPage) }).mount('#app')
`

let browser
const server = await createServer({
  root,
  configLoader: 'runner',
  cacheDir: path.join(process.env.TEMP || root, 'vite-ranked-history-visual'),
  server: { host: '127.0.0.1', port: 0, strictPort: false },
  plugins: [{
    name: 'ranked-history-visual',
    resolveId(id) { if (id === '/__ranked_history_qa__.js') return id },
    load(id) { if (id === '/__ranked_history_qa__.js') return entry },
    configureServer(devServer) {
      devServer.middlewares.use((request, response, next) => {
        if (!request.url?.match(/^\/__ranked_history_qa__(\?|$)/)) return next()
        response.setHeader('Content-Type', 'text/html')
        response.end('<div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__ranked_history_qa__.js"></script>')
      })
    },
  }],
})

try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ headless: true, channel: 'msedge' })
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort())

  for (const viewport of [{ width: 1280, height: 900 }, { width: 390, height: 844 }, { width: 360, height: 800 }]) {
    await page.setViewportSize(viewport)
    await page.goto(`http://127.0.0.1:${port}/__ranked_history_qa__`)
    await page.getByRole('button', { name: '历史荣誉', exact: true }).click()
    await page.locator('.faction-final-totals').waitFor()
    await page.locator('.honor-group').first().waitFor()
    const report = await page.evaluate(() => {
      const text = document.querySelector('.history-panel')?.textContent?.replace(/\s+/g, ' ').trim() || ''
      const panel = document.querySelector('.history-panel')
      return {
        text,
        documentOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        panelOverflow: panel ? panel.scrollWidth > panel.clientWidth + 1 : true,
        totals: document.querySelectorAll('.faction-final-totals details[open] .faction-total-values span').length,
        honorGroups: document.querySelectorAll('.honor-group').length,
        honorOrder: [...document.querySelectorAll('.honor-group header .ranked-identity-badge')].map(element => element.querySelector('span')?.textContent?.trim()),
        closedOldSeasons: [...document.querySelectorAll('.history-season-details')].filter(element => element.querySelector('summary')?.textContent?.includes('第0赛季')).every(element => !element.open),
        masterProfiles: [...document.querySelectorAll('.honor-master-profile')].map(element => element.getAttribute('src')),
        factionBadgeClasses: [...document.querySelectorAll('.honor-group header .ranked-identity-badge')].slice(0, 6).map(element => element.className),
        factionFilterVisible: !!document.querySelector('.faction-filter'),
      }
    })
    assert.equal(report.documentOverflow, false, `${viewport.width}px 页面出现横向溢出`)
    assert.equal(report.panelOverflow, false, `${viewport.width}px 历史荣誉区域出现横向溢出`)
    assert.equal(report.totals, 3, `${viewport.width}px 三派结算数值不完整`)
    assert.equal(report.honorGroups, 8, `${viewport.width}px 未按称号独立分组`)
    assert.deepEqual(report.honorOrder, ['统御者', '始乱者', '织命者', '代行主君', '混沌领主', '守望天士', '最强前号', '最强后号'], `${viewport.width}px 称号或主宰编号排序错误`)
    assert.equal(report.closedOldSeasons, true, `${viewport.width}px 旧赛季未默认折叠`)
    assert.equal(report.masterProfiles.length, 2, `${viewport.width}px 最强主宰缺少 Profile`)
    assert(report.masterProfiles.every(src => src?.includes('/special/master/')), `${viewport.width}px 最强主宰未用 Profile 资源`)
    assert(report.factionBadgeClasses.every(value => !value.includes('faction-neutral')), `${viewport.width}px 派系称号未沿用派系徽章`)
    assert.equal(report.factionFilterVisible, false, `${viewport.width}px 历史荣誉仍显示派系筛选`)
    for (const expected of ['历届派系结算数值', '秩序', '混沌', '命运', '公开账号甲'])
      assert(report.text.includes(expected), `${viewport.width}px 缺少玩家可见内容：${expected}`)
    for (const forbidden of ['S00', 'S01', 'SeasonId', 'Provenance', 'EvidenceFingerprint', 'AwardedAt', '赛季结束时冻结', '历史重建值'])
      assert(!report.text.includes(forbidden), `${viewport.width}px 外显内部字段或实现文案：${forbidden}`)
    await page.screenshot({ path: path.join(output, `ranked-history-${viewport.width}.png`), fullPage: true })
    await page.locator('.faction-total-grid details').nth(1).locator('summary').click()
    assert.equal(await page.locator('.faction-total-grid details').nth(1).getAttribute('open'), '', '旧赛季不能手动展开')
    assert(await page.getByText('2,407,333').isVisible(), '展开旧赛季后未显示结算值')
    if (viewport.width <= 390) {
      const group = page.locator('.honor-group').first()
      const winner = group.locator('.honor-winners > span').first()
      await winner.scrollIntoViewIfNeeded()
      const reached = await winner.evaluate(element => {
        const groupRect = element.closest('.honor-group')?.getBoundingClientRect()
        const winnerRect = element.getBoundingClientRect()
        const centerX = Math.min(innerWidth - 1, Math.max(0, winnerRect.left + winnerRect.width / 2))
        const centerY = Math.min(innerHeight - 1, Math.max(0, winnerRect.top + winnerRect.height / 2))
        const hit = document.elementFromPoint(centerX, centerY)
        return !!groupRect
          && groupRect.bottom > 0 && groupRect.top < innerHeight
          && winnerRect.bottom > 0 && winnerRect.top < innerHeight
          && (hit === element || element.contains(hit))
      })
      assert.equal(reached, true, `${viewport.width}px 移动端滚动后仍无法触达获奖者`)
      await page.screenshot({ path: path.join(output, `ranked-history-honors-${viewport.width}.png`) })
    }
  }

  await page.locator('.ranking-search').fill('不存在的荣誉')
  await page.getByText('没有符合搜索条件的历史荣誉或派系结算数值', { exact: true }).waitFor()
  assert.equal(await page.locator('.faction-final-totals').count(), 0, '无匹配数据时派系数值区域没有隐藏')
  assert.equal(await page.locator('.honor-groups').count(), 0, '无匹配数据时称号区域没有隐藏')
  assert.equal(errors.length, 0, `页面错误：${errors.join(' | ')}`)
  console.log(`历史荣誉视觉验收通过：${output}`)
} finally {
  await browser?.close()
  await server.close()
}
