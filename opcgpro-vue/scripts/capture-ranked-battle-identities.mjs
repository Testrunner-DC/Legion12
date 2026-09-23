import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'

const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT
  || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
const sharp = require('sharp')
const target = process.argv[2] || 'http://127.0.0.1:5174/__l12_battle_preview__'
const output = process.env.L12_RANKED_IDENTITY_OUT
  || 'C:/Users/neptu/Documents/ChatGPT/Legion12/artifacts/ranked-battle-identity-20260924'
fs.mkdirSync(output, { recursive: true })

const desktop = [
  { width: 1920, height: 1080 },
  { width: 1440, height: 810 },
  { width: 1280, height: 720 },
]
const mobile = [
  { width: 932, height: 430 },
  { width: 844, height: 390 },
  { width: 667, height: 375 },
  { width: 568, height: 320 },
]
const modes = ['full', 'mixed', 'minimal', 'placement']

const browser = await chromium.launch({ headless: true, channel: 'msedge' })
const reports = []
try {
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/*', route => ['127.0.0.1', 'localhost'].includes(
    new URL(route.request().url()).hostname) ? route.continue() : route.abort())

  for (const viewport of [...desktop, ...mobile]) {
    for (const mode of modes) {
      await page.setViewportSize(viewport)
      const isMobile = mobile.some(item => item.width === viewport.width && item.height === viewport.height)
      const query = new URLSearchParams({ identity: mode, hand: '8', rankedClock: '1' })
      if (isMobile) query.set('mobile', '1')
      await page.goto(`${target}?${query}`, { waitUntil: 'domcontentloaded' })
      await page.locator('.player-panel').waitFor()
      if (isMobile) await page.locator('[data-l12-mobile-landscape="true"]').waitFor()
      await page.waitForTimeout(120)

      const report = await page.evaluate(() => {
        const bounds = element => {
          const box = element.getBoundingClientRect()
          return { left: box.left, right: box.right, top: box.top, bottom: box.bottom,
            width: box.width, height: box.height }
        }
        const panel = document.querySelector('.player-panel')
        const summaries = [...document.querySelectorAll('.player-summary')].map(summary => ({
          bounds: bounds(summary),
          scrollWidth: summary.scrollWidth,
          clientWidth: summary.clientWidth,
          scrollHeight: summary.scrollHeight,
          clientHeight: summary.clientHeight,
          labels: [...summary.querySelectorAll('.rank-number,.ranked-identity-badge')]
            .filter(element => { const box = element.getBoundingClientRect(); return box.width > 0 && box.height > 0 })
            .map(element => element.classList.contains('rank-number')
              ? element.textContent?.trim() || ''
              : element.querySelector(':scope>span')?.textContent?.trim() || ''),
          items: [...summary.querySelectorAll('.rank-number,.ranked-identity-badge,.connection-state')]
            .map(bounds).filter(box => box.width > 0 && box.height > 0),
        }))
        const optionalBounds = selector => {
          const element = document.querySelector(selector)
          return element ? bounds(element) : null
        }
        return { viewport: { width: innerWidth, height: innerHeight }, panel: bounds(panel),
          panelScrollHeight: panel.scrollHeight, panelClientHeight: panel.clientHeight, summaries,
          routeControls: optionalBounds('.battle-route-controls'),
          recordTrigger: optionalBounds('.mobile-record-trigger'),
          timedClocks: optionalBounds('.mobile-timed-clocks') }
      })

      for (const summary of report.summaries) {
        assert(summary.scrollWidth <= summary.clientWidth + 1,
          `${viewport.width}x${viewport.height}/${mode}: player identity must not overflow horizontally ${JSON.stringify(summary)}`)
        for (const item of summary.items) {
          assert(item.left >= report.panel.left - 1 && item.right <= report.panel.right + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity item left the player panel ${JSON.stringify({ item, panel: report.panel })}`)
          assert(item.top >= report.panel.top - 1 && item.bottom <= report.panel.bottom + 1,
            `${viewport.width}x${viewport.height}/${mode}: identity item was vertically clipped ${JSON.stringify({ item, panel: report.panel })}`)
        }
        for (let index = 1; index < summary.items.length; index += 1) {
          assert(summary.items[index].top >= summary.items[index - 1].bottom - 0.75,
            `${viewport.width}x${viewport.height}/${mode}: identity items must occupy separate rows ${JSON.stringify(summary.items)}`)
        }
      }
      assert(report.panelScrollHeight <= report.panelClientHeight + 1,
        `${viewport.width}x${viewport.height}/${mode}: player panel content was clipped`)
      if (isMobile) {
        assert(report.routeControls && report.recordTrigger && report.timedClocks,
          `${viewport.width}x${viewport.height}/${mode}: complete mobile route dock must be visible`)
        assert(report.routeControls.top >= report.panel.bottom + 3,
          `${viewport.width}x${viewport.height}/${mode}: route controls overlap the identity panel`)
        assert(report.recordTrigger.top >= report.routeControls.bottom + 3,
          `${viewport.width}x${viewport.height}/${mode}: match record overlaps route controls`)
        assert(report.timedClocks.top >= report.recordTrigger.bottom + 3,
          `${viewport.width}x${viewport.height}/${mode}: clocks overlap the match record`)
        assert(report.timedClocks.bottom <= viewport.height - 48,
          `${viewport.width}x${viewport.height}/${mode}: clocks invade the bottom action dock`)
      }

      const expected = mode === 'full'
        ? ['第 128 名', '混沌先声', '最强阿斯加德']
        : mode === 'mixed' ? ['第 128 名', '统领']
          : mode === 'minimal' ? ['第 247 名', '进阶'] : ['定级 4/5']
      assert.deepEqual(report.summaries[0].labels, expected,
        `${viewport.width}x${viewport.height}/${mode}: opponent identity order changed`)

      const suffix = `${isMobile ? 'mobile' : 'desktop'}-${viewport.width}x${viewport.height}-${mode}`
      await page.screenshot({ path: path.join(output, `${suffix}.png`) })
      reports.push({ suffix, ...report })
    }
  }
  assert.deepEqual(errors, [], 'ranked identity fixture must not throw page errors')
  fs.writeFileSync(path.join(output, 'report.json'), JSON.stringify(reports, null, 2))
  const cellWidth = 400
  const cellHeight = 252
  const columns = 4
  const rows = Math.ceil(reports.length / columns)
  const contactLayers = []
  for (const [index, report] of reports.entries()) {
    const left = (index % columns) * cellWidth
    const top = Math.floor(index / columns) * cellHeight
    const thumbnail = await sharp(path.join(output, `${report.suffix}.png`))
      .resize(380, 214, { fit: 'contain', background: '#070b0d' }).png().toBuffer()
    const caption = Buffer.from(`<svg width="380" height="28" xmlns="http://www.w3.org/2000/svg"><rect width="100%" height="100%" fill="#10191d"/><text x="8" y="19" fill="#e9e6dc" font-size="14" font-family="Microsoft YaHei, sans-serif">${report.suffix}</text></svg>`)
    contactLayers.push({ input: caption, left: left + 10, top: top + 5 })
    contactLayers.push({ input: thumbnail, left: left + 10, top: top + 33 })
  }
  const contactSheet = path.join(output, '各比例验收总览.png')
  await sharp({ create: { width: columns * cellWidth, height: rows * cellHeight, channels: 4, background: '#06090b' } })
    .composite(contactLayers).png().toFile(contactSheet)
  console.log(JSON.stringify({ output, screenshots: reports.length, contactSheet, errors }, null, 2))
} finally {
  await browser.close()
}
